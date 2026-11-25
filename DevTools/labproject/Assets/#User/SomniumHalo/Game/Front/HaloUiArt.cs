using System;
using System.Collections.Generic;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] The menu's own art, read from the player's MAPS\ui.map: on a worker thread the map is opened (HaloUiData), its fonts turned
    /// into distance fields (HaloUiFont) and every picture the front end shows decoded (HaloTextures.Prepare); on the main thread a few textures
    /// are made each frame and handed to the widgets that asked for them (<see cref="Bind"/>, <see cref="BindSliced"/>, <see cref="BindButton"/>).
    /// A widget built before ui.map is read shows its fallback (a plain slab, a text caption) and changes by itself when the picture arrives.
    /// Halo bitmaps are padded to powers of two: each picture names the part it uses from the top-left (the widget bounds of ui.map, measured in
    /// the harness), and its rows are top-down while Unity's are bottom-up, so the UV rect is turned over.
    /// Log: "Front art: ui.map read in N ms: ... pictures, fonts ...".
    /// </summary>
    public static class HaloUiArt
    {
        public enum UiSound { Cursor, Forward, Back, Fail }

        // the pictures read ahead (name, images)
        private static readonly string[][] Wanted =
        {
            new[] { "ui\\shell\\main_menu\\halo_logo", "0" },
            new[] { "ui\\shell\\main_menu\\menu_multiplayer", "0,1" },
            new[] { "ui\\shell\\main_menu\\menu_settings", "0,1" },
            new[] { "ui\\shell\\main_menu\\menu_credits", "0,1" },
            new[] { "ui\\shell\\main_menu\\menu_quit", "0,1" },
            new[] { "ui\\shell\\main_menu\\menu_profile", "0,1" },
            new[] { "ui\\shell\\bitmaps\\mp_map_grafix", "all" },
            new[] { "ui\\shell\\bitmaps\\game_type_grafix", "all" },
            new[] { "ui\\shell\\bitmaps\\sel_list_item_bkd", "1,2" },
            new[] { "ui\\shell\\bitmaps\\sel_list_desc_bkd", "0" },
            new[] { "ui\\shell\\bitmaps\\background", "0" },
            new[] { "ui\\shell\\bitmaps\\gradient", "0" },
            new[] { "ui\\shell\\main_menu\\multiplayer_type_select\\header_multiplayer", "0" },
            new[] { "ui\\shell\\main_menu\\multiplayer_type_select\\mp_map_select\\header_select_map", "0" },
            new[] { "ui\\shell\\main_menu\\gametype_select\\header_select_gametype", "0" },
            new[] { "ui\\shell\\main_menu\\settings_select\\multiplayer_setup\\playlist_edit\\game_type_select\\header_rules", "0" },
            new[] { "ui\\shell\\main_menu\\multiplayer_type_select\\connected\\pregame\\header_enlisted_players", "0" },
            new[] { "ui\\shell\\main_menu\\settings_select\\player_setup\\player_profile_edit\\audio_settings\\header_profile_audio_settings", "0" },
            new[] { "ui\\shell\\main_menu\\settings_select\\player_setup\\player_profile_edit\\header_edit_profile_settings", "0" },
            // the campaign (HaloCampaignPage): CAMPAIGN caption, the 10 missions' pictures (+ the lock), the difficulty pictures, the headers
            new[] { "ui\\shell\\main_menu\\menu_load_campaign", "0,1" },
            new[] { "ui\\shell\\bitmaps\\sp_levels", "all" },
            new[] { "ui\\shell\\main_menu\\difficulty_select\\difficulty_options", "all" },
            new[] { "ui\\shell\\main_menu\\difficulty_select\\difficulty_options_small", "all" },
            new[] { "ui\\shell\\main_menu\\solo_level_select\\header_load_level", "0" },
            new[] { "ui\\shell\\main_menu\\difficulty_select\\header_choose_difficulty", "0" },
            // the hub (HaloHub): the ring and the menu sky (tiled: not clamped)
            new[] { "scenery\\halo\\bitmaps\\halo inner ring", "0" },
            new[] { "scenery\\halo\\bitmaps\\halo outer ring", "0" },
            new[] { "scenery\\halo\\bitmaps\\halo ring inner shadow", "0" },
            new[] { "sky\\space\\bitmaps\\stars", "0" },
            new[] { "sky\\sky_x10\\bitmaps\\galaxy", "0" },
            new[] { "sky\\planets\\threshold space", "0" },
            new[] { "sky\\planets\\basis space", "0" },
        };

        /// <summary>The ring (scenery\halo\halo) and the menu sky (sky\sky_ui\sky_ui) as meshes prepared on the worker (metres, Halo -> Unity axes).</summary>
        public static HaloModelBuilder.Prepared RingMesh, SkyMesh;
        /// <summary>The ring's placement relative to the menu camera "uicam" (Halo units, radians), as the ui scenario has it.</summary>
        public static HVec3 RingOffset, RingRotation;
        public static bool RingPlaced;

        private sealed class Pic { public string Key; public HaloTextures.Prepared Prepared; public Texture2D Texture; public bool Failed; }

        private sealed class BindReq { public RawImage Image; public Image Sliced; public HaloButton Button; public string Key; public int W, H, Border; public Color Color; }

        private static readonly Dictionary<string, Pic> Pics = new Dictionary<string, Pic>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<BindReq> Pending = new List<BindReq>();
        private static readonly List<Pic> Uploads = new List<Pic>();
        private static Thread _thread;
        private static volatile bool _done;
        private static volatile string _error;
        private static HaloUiData _data;
        private static HaloUiFont _fontLarge, _fontSmall;
        private static bool _handled;
        private static long _ms;
        private static int _picCount;
        private static Sprite _slabPlain, _slabLit;
        private static HaloTag _cursor, _forward, _back, _fail, _music;

        /// <summary>ui.map is read (strings and tags can be asked for); the textures may still be coming.</summary>
        public static bool Ready { get { return _handled && _data != null; } }
        public static HaloUiData Data { get { return Ready ? _data : null; } }
        public static string Error { get { return _error; } }
        /// <summary>The hub's menu music (ui.map sound\music\title1\title1, looping), null until read.</summary>
        public static HaloTag Music { get { return _music; } }

        /// <summary>Starts reading ui.map (FrontBootDone). Safe to call again (a new install): the old art stays until the new is read.</summary>
        public static void StartLoad(HaloInstall install)
        {
            if (install == null || string.IsNullOrEmpty(install.UiPath)) return;
            if (_thread != null && _thread.IsAlive) return;
            string path = install.UiPath;
            _done = false; _handled = false; _error = null;
            _thread = new Thread(() => Load(path)) { IsBackground = true, Name = "Halo ui.map" };
            _thread.Start();
        }

        private static void Load(string path)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!System.IO.File.Exists(path)) { _error = "ui.map is not in your MAPS folder: the menu uses plain text."; return; }
                HaloUiData data = HaloUiData.Load(path);
                HaloResources bitmaps = HaloTextures.BitmapsFor(data.Map);
                int n = 0;
                foreach (string[] w in Wanted)
                {
                    HaloTag t = data.Map.Find("bitm", w[0]);
                    if (t == null) continue;
                    int count = 1;
                    try { count = new HaloBitmap(t).Images.Count; } catch (Exception) { }
                    var list = new List<int>();
                    if (w[1] == "all") for (int i = 0; i < count; i++) list.Add(i);
                    else foreach (string s in w[1].Split(',')) list.Add(int.Parse(s));
                    foreach (int i in list)
                    {
                        if (i >= count) continue;
                        var pic = new Pic { Key = Key(w[0], i) };
                        try { pic.Prepared = HaloTextures.Prepare(t, i, bitmaps); }
                        catch (Exception) { pic.Failed = true; }
                        if (pic.Prepared == null) pic.Failed = true;
                        lock (Pics) Pics[pic.Key] = pic;
                        n++;
                    }
                }

                _picCount = n;
                try { _fontLarge = HaloUiFont.Prepare(data.Map.Find("font", "ui\\large_ui")); } catch (Exception e) { _error = "font large_ui: " + e.Message; }
                try { _fontSmall = HaloUiFont.Prepare(data.Map.Find("font", "ui\\small_ui")); } catch (Exception e) { _error = "font small_ui: " + e.Message; }
                // touch the strings the pages use, so the main thread does not parse them
                data.Strings("mp_map_list"); data.Strings("mp_map_select\\map_data"); data.Strings("default_multiplayer_game_setting_names");
                data.Strings("game_variant_descriptions"); data.Strings("main_menu_options");
                _cursor = data.Sound("sound\\sfx\\ui\\cursor"); _forward = data.Sound("sound\\sfx\\ui\\forward");
                _back = data.Sound("sound\\sfx\\ui\\back"); _fail = data.Sound("sound\\sfx\\ui\\flag_failure");
                _music = data.Looping("sound\\music\\title1\\title1");
                try
                {
                    HVec3 cam, pos, rot; float yaw; HaloTag ring;
                    if (data.RingPlacement(out cam, out yaw, out ring, out pos, out rot))
                    {
                        RingOffset = new HVec3(pos.X - cam.X, pos.Y - cam.Y, pos.Z - cam.Z);
                        RingRotation = rot;
                        RingPlaced = true;
                        RingMesh = HaloModelBuilder.Prepare(new HaloModel(ring), new HaloModelBuilder.Options { Scale = HaloHub.RingScale });
                    }

                    HaloTag sky = data.SkyModel();
                    if (sky != null) SkyMesh = HaloModelBuilder.Prepare(new HaloModel(sky), new HaloModelBuilder.Options { Scale = HaloHub.SkyScale });
                }
                catch (Exception e) { _error = "the ring scene: " + e.Message; }
                _data = data;
            }
            catch (Exception e)
            {
                _error = "reading ui.map failed: " + e.Message;
            }
            finally
            {
                _ms = sw.ElapsedMilliseconds;
                _done = true;
            }
        }

        public static string Key(string bitmap, int index) { return bitmap + "#" + index; }

        /// <summary>Once a frame (FrontTick): the read is handed over, a few textures made, the widgets waiting for them served.</summary>
        public static void Tick()
        {
            if (_done && !_handled)
            {
                _handled = true;
                if (_data == null)
                {
                    HaloLog.Warn("Front art: " + (_error ?? "ui.map was not read") + " (" + _ms + " ms)");
                    return;
                }

                lock (Pics) foreach (Pic p in Pics.Values) if (!p.Failed) Uploads.Add(p);
                HaloFonts.Build(_fontLarge, _fontSmall);
                HaloLog.Info("Front art: ui.map read in " + _ms + " ms: " + _picCount + " pictures, fonts " + (_fontLarge != null ? "large_ui " + _fontLarge.Glyphs.Count + " glyphs (" + _fontLarge.AtlasWidth + "x" + _fontLarge.AtlasHeight + ")" : "large_ui MISSING") +
                             (_fontSmall != null ? ", small_ui " + _fontSmall.Glyphs.Count : ", small_ui MISSING") + ", sounds " + (_cursor != null ? "cursor " : "") + (_forward != null ? "forward " : "") + (_back != null ? "back " : "") + (_music != null ? ", music title1" : ", no music") + (_error != null ? " (" + _error + ")" : ""));
            }

            if (!_handled || _data == null) return;
            int made = 0;
            while (Uploads.Count > 0 && made < 6)
            {
                Pic p = Uploads[Uploads.Count - 1];
                Uploads.RemoveAt(Uploads.Count - 1);
                try
                {
                    var tex = HaloTextures.Upload(p.Prepared, false) as Texture2D;
                    if (tex != null && tex != HaloTextures.White)
                    {
                        bool ui = p.Key.StartsWith("ui\\", StringComparison.OrdinalIgnoreCase);
                        tex.wrapMode = ui ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                        if (ui) tex.filterMode = FilterMode.Bilinear;
                        p.Texture = tex;
                    }
                    else p.Failed = true;
                }
                catch (Exception e) { p.Failed = true; HaloLog.Warn("Front art: " + p.Key + " did not upload: " + e.Message); }
                p.Prepared = null;
                made++;
            }

            if (_slabPlain == null)
            {
                Texture2D a = Texture("ui\\shell\\bitmaps\\sel_list_item_bkd", 1), b = Texture("ui\\shell\\bitmaps\\sel_list_item_bkd", 2);
                if (a != null && b != null)
                {
                    _slabPlain = Slice(a, 388, 28, 14);
                    _slabLit = Slice(b, 322, 28, 14);
                }
            }

            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                BindReq r = Pending[i];
                if (TryServe(r)) Pending.RemoveAt(i);
            }
        }

        private static bool TryServe(BindReq r)
        {
            if (r.Button != null)
            {
                if (_slabPlain == null) return false;
                r.Button.SlabReady(_slabPlain, _slabLit);
                return true;
            }

            if (r.Image == null && r.Sliced == null) return true;      // destroyed
            Pic p;
            lock (Pics) Pics.TryGetValue(r.Key, out p);
            if (p == null) return Ready && Uploads.Count == 0;          // never coming: drop
            if (p.Failed) return true;
            if (p.Texture == null) return false;
            if (r.Image != null)
            {
                r.Image.texture = p.Texture;
                r.Image.uvRect = UsedUv(p.Texture, r.W, r.H);
                r.Image.color = r.Color;
                var b = r.Image.GetComponentInParent<HaloButton>();
                if (b != null) b.PictureReady();
            }
            else
            {
                r.Sliced.sprite = Slice(p.Texture, r.W, r.H, r.Border);
                r.Sliced.type = Image.Type.Sliced;
                r.Sliced.color = r.Color;
            }

            return true;
        }

        /// <summary>The UV rect of the used w x h pixels at the picture's top-left, rows turned over (Halo's rows are top-down).</summary>
        public static Rect UsedUv(Texture t, int w, int h)
        {
            float u = w > 0 ? Mathf.Clamp01((float)w / t.width) : 1f, v = h > 0 ? Mathf.Clamp01((float)h / t.height) : 1f;
            return new Rect(0f, v, u, -v);
        }

        private static Sprite Slice(Texture2D t, int w, int h, int border)
        {
            w = Mathf.Min(w, t.width); h = Mathf.Min(h, t.height);
            // the picture's row 0 (its top) is the texture's row 0 (Unity's bottom): the used part is rows 0..h; a sprite is drawn upright
            // only with its rows in Unity's order, so the sliced slab is drawn upside down - the slabs are symmetric top to bottom.
            int bv = Mathf.Min(border, h / 2 - 1);
            var s = Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, bv, border, bv));
            s.name = t.name + " slab";
            return s;
        }

        /// <summary>The texture of a picture once made (null before / missing).</summary>
        public static Texture2D Texture(string bitmap, int index)
        {
            Pic p;
            lock (Pics) Pics.TryGetValue(Key(bitmap, index), out p);
            return p != null ? p.Texture : null;
        }

        public static void Bind(RawImage img, string bitmap, int index, int usedW, int usedH, Color color)
        {
            var r = new BindReq { Image = img, Key = Key(bitmap, index), W = usedW, H = usedH, Color = color };
            if (!TryServeNow(r)) Pending.Add(r);
        }

        public static void BindSliced(Image img, string bitmap, int index, int usedW, int usedH, int border, Color color)
        {
            var r = new BindReq { Sliced = img, Key = Key(bitmap, index), W = usedW, H = usedH, Border = border, Color = color };
            if (!TryServeNow(r)) Pending.Add(r);
        }

        public static void BindButton(HaloButton b)
        {
            var r = new BindReq { Button = b };
            if (!TryServeNow(r)) Pending.Add(r);
        }

        private static bool TryServeNow(BindReq r)
        {
            return _handled && TryServe(r);
        }

        /// <summary>Changes what a bound picture shows (a map preview after the map changed): the new texture at once if made, else when it is.</summary>
        public static void Rebind(RawImage img, string bitmap, int index, int usedW, int usedH, Color color)
        {
            for (int i = Pending.Count - 1; i >= 0; i--) if (Pending[i].Image == img) Pending.RemoveAt(i);
            Bind(img, bitmap, index, usedW, usedH, color);
        }

        // ------------------------------------------------------------------ sounds

        private static float _lastSound;

        public static void PlayUi(UiSound s)
        {
            HaloTag t = s == UiSound.Cursor ? _cursor : s == UiSound.Forward ? _forward : s == UiSound.Back ? _back : _fail;
            if (t == null) return;
            if (s == UiSound.Cursor && Time.unscaledTime - _lastSound < 0.06f) return;
            _lastSound = Time.unscaledTime;
            // the Audio module's UI voice (its "ui" volume group) plays ui.map's own menu sounds by name
            try { HaloAudio.Ui(s == UiSound.Cursor ? "cursor" : s == UiSound.Forward ? "forward" : s == UiSound.Back ? "back" : "flag_failure"); }
            catch (Exception e) { HaloLog.Warn("Front art: UI sound failed: " + e.Message); _cursor = _forward = _back = _fail = null; }
        }

        /// <summary>The menu's string (ui.map) or the fallback before / without it.</summary>
        public static string S(string list, int index, string fallback)
        {
            HaloUiData d = Data;
            return d != null ? d.String(list, index, fallback) : fallback;
        }

        public static void Shutdown()
        {
            Pending.Clear();
            Uploads.Clear();
        }
    }

    /// <summary>
    /// Halo's menu fonts (ui.map ui\large_ui and ui\small_ui) as TextMeshPro font assets made at run time from the distance fields HaloUiFont
    /// prepared (the ETS Dreamport's EtsFont recipe: a TMP_FontAsset with a static atlas, its glyph and character tables filled, the Distance Field
    /// shader). Texts made before the fonts are built get them when they are (<see cref="Apply"/> remembers them); TMP's default font is the
    /// fallback for characters Halo's fonts lack.
    /// </summary>
    public static class HaloFonts
    {
        private static TMP_FontAsset _large, _small;
        private static readonly List<KeyValuePair<TMP_Text, bool>> Waiting = new List<KeyValuePair<TMP_Text, bool>>();
        private static bool _failed;

        /// <summary>Lab / off switch: false keeps TextMeshPro's default font.</summary>
        public static bool UseHaloFonts = true;

        public static TMP_FontAsset Large { get { return _large; } }
        public static TMP_FontAsset Small { get { return _small; } }

        public static void Apply(TMP_Text t, bool large)
        {
            if (t == null) return;
            TMP_FontAsset f = large ? (_large ?? _small) : (_small ?? _large);
            if (f != null) { t.font = f; return; }
            t.font = TMP_Settings.defaultFontAsset;
            if (!_failed && UseHaloFonts) Waiting.Add(new KeyValuePair<TMP_Text, bool>(t, large));
        }

        public static void Build(HaloUiFont large, HaloUiFont small)
        {
            if (!UseHaloFonts) { Waiting.Clear(); return; }
            try
            {
                _large = Make(large);
                _small = Make(small);
            }
            catch (Exception e)
            {
                HaloLog.Warn("Fonts: building Halo's fonts failed (" + e.GetType().Name + ": " + e.Message + ") - TextMeshPro's default font stays.");
                _large = _small = null;
            }

            if (_large == null && _small == null) { _failed = true; Waiting.Clear(); return; }
            int n = 0;
            for (int i = 0; i < Waiting.Count; i++)
            {
                TMP_Text t = Waiting[i].Key;
                if (t == null) continue;
                TMP_FontAsset f = Waiting[i].Value ? (_large ?? _small) : (_small ?? _large);
                t.font = f;
                n++;
            }

            Waiting.Clear();
            HaloLog.Info("Fonts: Halo's " + (_large != null ? "large_ui" : "") + (_small != null ? " small_ui" : "") + " fonts built; " + n + " texts switched to them.");
        }

        private static TMP_FontAsset Make(HaloUiFont f)
        {
            if (f == null || f.Atlas == null) return null;
            Shader shader = Shader.Find("TextMeshPro/Distance Field");
            if (shader == null && TMP_Settings.defaultFontAsset != null && TMP_Settings.defaultFontAsset.material != null) shader = TMP_Settings.defaultFontAsset.material.shader;
            if (shader == null) { HaloLog.Warn("Fonts: no TextMeshPro distance field shader."); return null; }

            var tex = new Texture2D(f.AtlasWidth, f.AtlasHeight, TextureFormat.Alpha8, false, true) { name = "Halo " + f.Name + " field", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.LoadRawTextureData(f.Atlas);
            tex.Apply(false, true);

            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            font.name = "Halo " + f.Name;
            font.atlasTextures = new[] { tex };
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            SetField(font, "m_AtlasWidth", f.AtlasWidth);
            SetField(font, "m_AtlasHeight", f.AtlasHeight);
            SetField(font, "m_AtlasPadding", HaloUiFont.Spread);
            SetField(font, "m_AtlasRenderMode", UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA);

            float em = f.Ascent + f.Descent;
            UnityEngine.TextCore.FaceInfo face = font.faceInfo;
            face.familyName = "Halo";
            face.styleName = f.Name;
            face.pointSize = Mathf.Max(1, Mathf.RoundToInt(em));
            face.scale = 1f;
            face.lineHeight = em + f.Leading;
            face.ascentLine = f.Ascent;
            face.descentLine = -f.Descent;
            face.baseline = 0f;
            face.capLine = f.Ascent * 0.9f;
            face.meanLine = f.Ascent * 0.65f;
            face.underlineOffset = -f.Descent * 0.5f;
            face.underlineThickness = Mathf.Max(1f, em * 0.06f);
            face.strikethroughOffset = f.Ascent * 0.4f;
            face.strikethroughThickness = Mathf.Max(1f, em * 0.06f);
            face.superscriptOffset = f.Ascent * 0.5f;
            face.superscriptSize = 0.5f;
            face.subscriptOffset = -f.Descent;
            face.subscriptSize = 0.5f;
            float space = em * 0.3f;

            uint next = 0;
            foreach (HaloUiFont.Glyph g in f.Glyphs)
            {
                UnityEngine.TextCore.GlyphRect rect;
                UnityEngine.TextCore.GlyphMetrics metrics;
                if (g.W <= 0) { rect = new UnityEngine.TextCore.GlyphRect(0, 0, 0, 0); metrics = new UnityEngine.TextCore.GlyphMetrics(0f, 0f, 0f, 0f, g.Advance); }
                else
                {
                    rect = new UnityEngine.TextCore.GlyphRect(g.X, g.Y, g.W, g.H);
                    metrics = new UnityEngine.TextCore.GlyphMetrics(g.W, g.H, g.BearingX, g.BearingY, g.Advance);
                }

                if (g.Code == 32) space = g.Advance;
                var glyph = new UnityEngine.TextCore.Glyph(next, metrics, rect, 1f, 0);
                font.glyphTable.Add(glyph);
                font.characterTable.Add(new TMP_Character((uint)g.Code, font, glyph));
                next++;
            }

            face.tabWidth = space * 4f;
            font.faceInfo = face;

            var material = new Material(shader) { name = font.name + " material" };
            material.SetTexture(ShaderUtilities.ID_MainTex, tex);
            material.SetFloat(ShaderUtilities.ID_TextureWidth, f.AtlasWidth);
            material.SetFloat(ShaderUtilities.ID_TextureHeight, f.AtlasHeight);
            material.SetFloat(ShaderUtilities.ID_GradientScale, HaloUiFont.Spread + 1f);
            material.SetFloat(ShaderUtilities.ID_WeightNormal, 0f);
            material.SetFloat(ShaderUtilities.ID_WeightBold, 0.6f);
            ShaderUtilities.UpdateShaderRatios(material);
            font.material = material;
            if (TMP_Settings.defaultFontAsset != null) font.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
            SetField(font, "m_Version", "1.1.0");
            font.ReadFontAssetDefinition();
            return font;
        }

        private static void SetField(object target, string name, object value)
        {
            System.Reflection.FieldInfo field = target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (field == null) { HaloLog.Warn("Fonts: TextMeshPro has no field " + name + "."); return; }
            field.SetValue(target, value);
        }
    }
}

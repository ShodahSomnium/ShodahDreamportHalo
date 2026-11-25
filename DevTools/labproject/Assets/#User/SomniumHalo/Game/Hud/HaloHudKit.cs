using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>A picture out of a HUD bitmap tag: texture, uv rect (Unity RawImage convention), size and registration point in bitmap pixels.</summary>
    public sealed class HaloHudSprite
    {
        public Texture Tex; public Rect Uv; public float W, H, RegX, RegY;
        public HaloBitmap Bitmap; public int Image; public int PxX, PxY, PxW, PxH;   // the sprite's pixel rect in its image (top-left origin)
    }

    /// <summary>
    /// What the HUD reads from the current map once (hud globals, the player's unit HUD, the grenade HUDs, fonts, strings) and the helpers
    /// every HUD piece shares: sprites out of bitmap tags, Halo's placement rules on the 640x480 canvas, flashing colours, the look of
    /// Halo's additive HUD on Unity's alpha-blended UI.
    ///
    /// Placement (worked out from the tags, see DevTools/agents/H/NOTES.md): an element's bitmap corner sits on its anchor corner, the
    /// offset points into the screen (x right for left anchors, left for right anchors; y down for top anchors, up for bottom ones);
    /// crosshairs are centred on the screen by their registration point. Size = bitmap pixels x scale, where "use high res scale" halves
    /// it and the multiplayer HUD scale (hud globals, 0.75) applies unless "don't scale size" / "don't scale offset" is set.
    /// </summary>
    public static class HaloHudKit
    {
        public const float CanvasH = 480f, CanvasW = 640f;

        public static HaloMap Map { get; private set; }
        public static HudGlobals Globals { get; private set; }
        public static HudUnit Unit { get; private set; }
        public static HudGrenade[] Grenades = new HudGrenade[0];
        public static HaloFont Font, SmallFont;
        public static List<string> MpText = new List<string>();
        public static List<string> ItemText = new List<string>();
        public static HudMessages Messages;
        /// <summary>The multiplayer HUD scale (hud globals "hud scale in multiplayer"); 1 when the map has none.</summary>
        public static float MpScale = 1f;
        public static HaloTag BipedTag;

        static readonly Dictionary<long, HaloHudSprite> Sprites = new Dictionary<long, HaloHudSprite>();
        static readonly Dictionary<uint, HaloBitmap> Bitmaps = new Dictionary<uint, HaloBitmap>();
        static readonly Dictionary<long, byte[]> Pixels = new Dictionary<long, byte[]>();

        /// <summary>Reads the HUD tags of a map (main thread; cheap: tag reads only, pictures are made when first drawn).</summary>
        public static bool Load(HaloMap map)
        {
            if (map == null) return false;
            if (map == Map && Globals != null) return true;
            Release();
            Map = map;
            try
            {
                Globals = HudGlobals.Read(map);
                MpScale = Globals.MultiplayerScale > 0.05f ? Globals.MultiplayerScale : 1f;
                Font = HaloFont.Read(Globals.Font) ?? HaloFont.Read(Globals.SystemFont);
                SmallFont = HaloFont.Read(Globals.SplitFont) ?? Font;
                Messages = HudMessages.Read(Globals.HudMessages);
                ItemText = HaloStrings.Read(Globals.ItemMessages);
                HaloTag mp = null;
                foreach (var t in map.OfClass("ustr")) if (t.Name.EndsWith("multiplayer_game_text", StringComparison.OrdinalIgnoreCase)) { mp = t; break; }
                MpText = HaloStrings.Read(mp);
                var g = new List<HudGrenade>();
                foreach (var t in Globals.GrenadeHuds) { var gh = HudGrenade.Read(t); if (gh != null) g.Add(gh); }
                Grenades = g.ToArray();
                Unit = HudUnit.Read(PlayerUnitHud(map, out HaloTag biped));
                BipedTag = biped;
                HaloLog.Info("HUD: tags of " + map.Name + ": hud globals " + Name(Globals.Tag) + ", unit hud " + Name(Unit != null ? Unit.Tag : null) + " (biped " + Name(biped)
                             + "), grenade huds " + Grenades.Length + ", font " + Name(Font != null ? Font.Tag : null) + ", multiplayer scale " + MpScale.ToString("0.##")
                             + ", motion sensor range " + Globals.MotionRange.ToString("0.#") + " wu, " + MpText.Count + " multiplayer strings, " + (Messages != null ? Messages.Names.Count : 0) + " hud messages");
                return true;
            }
            catch (Exception e)
            {
                HaloLog.Error("HUD: reading the HUD tags of " + map.Name + " failed: " + e);
                return false;
            }
        }

        static string Name(HaloTag t) { return t != null ? t.Name : "none"; }

        /// <summary>The multiplayer player's biped (globals multiplayer information "unit") and its full-screen unit HUD (the first of its HUD interfaces; the second is split screen).</summary>
        static HaloTag PlayerUnitHud(HaloMap map, out HaloTag biped)
        {
            biped = null;
            foreach (var t in map.OfClass("matg"))
            {
                // campaign: globals "player information" unit (the cyborg); multiplayer: "multiplayer information" unit (cyborg_mp)
                if (HaloCampaign.Active) foreach (var pi in t.Root.Block(368, 244)) { biped = pi.Ref(0); break; }
                if (biped == null) foreach (var mi in t.Root.Block(356, 160)) { biped = mi.Ref(16); break; }
                break;
            }
            if (biped == null) foreach (var t in map.OfClass("bipd")) { biped = t; if (t.Name.Contains("_mp")) break; }
            if (biped != null) foreach (var h in biped.Root.Block(680, 48)) { var u = h.Ref(0); if (u != null) return u; }
            foreach (var t in map.OfClass("unhi")) return t;
            return null;
        }

        public static void Release()
        {
            Sprites.Clear(); Bitmaps.Clear(); Pixels.Clear();
            Map = null; Globals = null; Unit = null; Grenades = new HudGrenade[0]; Font = null; SmallFont = null; Messages = null;
            MpText = new List<string>(); ItemText = new List<string>();
        }

        /// <summary>A multiplayer string (ui multiplayer_game_text) or the fallback.</summary>
        public static string Mp(int index, string fallback)
        {
            return index >= 0 && index < MpText.Count && !string.IsNullOrEmpty(MpText[index]) ? MpText[index] : fallback;
        }

        // ------------------------------------------------------------------ sprites

        public static HaloBitmap BitmapOf(HaloTag t)
        {
            if (t == null) return null;
            HaloBitmap b;
            if (!Bitmaps.TryGetValue(t.Id, out b))
            {
                try { b = new HaloBitmap(t); } catch (Exception e) { HaloLog.Warn("HUD: bitmap " + t.Name + ": " + e.Message); b = null; }
                Bitmaps[t.Id] = b;
            }
            return b;
        }

        /// <summary>Frame <paramref name="frame"/> of sequence <paramref name="sequence"/> of a bitmap (2D images: the sequence's image; sprite sheets: its sprite).</summary>
        public static HaloHudSprite Sprite(HaloTag bitmap, int sequence, int frame = 0)
        {
            if (bitmap == null) return null;
            long key = ((long)bitmap.Id << 24) | ((long)(sequence & 0xFFF) << 12) | (long)(frame & 0xFFF);
            HaloHudSprite s;
            if (Sprites.TryGetValue(key, out s)) return s;
            s = MakeSprite(bitmap, sequence, frame);
            Sprites[key] = s;
            return s;
        }

        static HaloHudSprite MakeSprite(HaloTag tag, int sequence, int frame)
        {
            var b = BitmapOf(tag);
            if (b == null || b.Images.Count == 0) return null;
            if (sequence < 0) sequence = 0;
            HaloBitmap.Sequence seq = sequence < b.Sequences.Count ? b.Sequences[sequence] : null;
            int image; float l = 0f, t = 0f, r = 1f, bo = 1f, rx = -1f, ry = -1f;
            if (seq != null && seq.Sprites.Count > 0)
            {
                var sp = seq.Sprites[Mathf.Clamp(frame, 0, seq.Sprites.Count - 1)];
                image = sp.Bitmap; l = sp.Left; t = sp.Top; r = sp.Right; bo = sp.Bottom; rx = sp.RegX; ry = sp.RegY;
            }
            else if (seq != null && seq.Count > 0) image = seq.First + Mathf.Clamp(frame, 0, seq.Count - 1);
            else image = sequence < b.Images.Count ? sequence : 0;
            if (image < 0 || image >= b.Images.Count) return null;
            var img = b.Images[image];
            Texture tex = HaloTextures.Get(tag, image);
            if (tex == null) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            var s = new HaloHudSprite { Tex = tex, Bitmap = b, Image = image };
            s.PxX = Mathf.RoundToInt(l * img.Width); s.PxY = Mathf.RoundToInt(t * img.Height);
            s.PxW = Mathf.Max(1, Mathf.RoundToInt((r - l) * img.Width)); s.PxH = Mathf.Max(1, Mathf.RoundToInt((bo - t) * img.Height));
            s.W = s.PxW; s.H = s.PxH;
            s.RegX = rx >= 0f ? rx * img.Width : img.RegX;
            s.RegY = ry >= 0f ? ry * img.Height : img.RegY;
            // Halo's rows are stored top first and uploaded as they are (v = 0 at the top): flip v for a RawImage.
            s.Uv = new Rect(l, bo, r - l, t - bo);
            return s;
        }

        /// <summary>The decoded RGBA pixels (top row first) of one image of a bitmap (cached; for CPU-drawn meters).</summary>
        public static byte[] ImagePixels(HaloTag tag, int image)
        {
            var b = BitmapOf(tag);
            if (b == null || image < 0 || image >= b.Images.Count) return null;
            long key = ((long)tag.Id << 16) | (long)image;
            byte[] px;
            if (Pixels.TryGetValue(key, out px)) return px;
            try { px = HaloBitmap.DecodeRgba(b.Images[image], HaloBitmap.Raw(tag.Map, b.Images[image], HaloTextures.BitmapsFor(tag.Map))); }
            catch (Exception e) { HaloLog.Warn("HUD: pixels of " + tag.Name + "#" + image + ": " + e.Message); px = null; }
            Pixels[key] = px;
            return px;
        }

        // ------------------------------------------------------------------ placement

        /// <summary>Size scale of an element (bitmap pixels -> canvas units).</summary>
        public static float SizeScale(HudPlace p, bool mp)
        {
            if ((p.Flags & 2) != 0) return 1f;                      // don't scale size
            float s = p.HighRes ? 0.5f : 1f;
            return mp ? s * MpScale : s;
        }

        public static float OffsetScale(HudPlace p, bool mp) { return (p.Flags & 1) != 0 || !mp ? 1f : MpScale; }

        /// <summary>Puts <paramref name="rt"/> on the canvas: Halo offset (x, y) from <paramref name="a"/>'s corner, size (w, h) canvas units.
        /// For the centre anchors (reg &gt;= 0) the registration point (from the element's top-left, canvas units) sits on the point.</summary>
        public static void Place(RectTransform rt, HudAnchor a, float x, float y, float w, float h, float regX = -1f, float regY = -1f)
        {
            Vector2 an, pos;
            switch (a)
            {
                case HudAnchor.TopRight: an = new Vector2(1f, 1f); pos = new Vector2(-x, -y); break;
                case HudAnchor.BottomLeft: an = new Vector2(0f, 0f); pos = new Vector2(x, y); break;
                case HudAnchor.BottomRight: an = new Vector2(1f, 0f); pos = new Vector2(-x, y); break;
                case HudAnchor.Center: an = new Vector2(0.5f, 0.5f); pos = new Vector2(x, -y); break;
                case HudAnchor.TopCenter: an = new Vector2(0.5f, 1f); pos = new Vector2(x, -y); break;
                case HudAnchor.BottomCenter: an = new Vector2(0.5f, 0f); pos = new Vector2(x, y); break;
                case HudAnchor.LeftCenter: an = new Vector2(0f, 0.5f); pos = new Vector2(x, -y); break;
                case HudAnchor.RightCenter: an = new Vector2(1f, 0.5f); pos = new Vector2(-x, -y); break;
                default: an = new Vector2(0f, 1f); pos = new Vector2(x, -y); break;
            }
            rt.anchorMin = an; rt.anchorMax = an;
            Vector2 pivot = an;
            if (regX >= 0f && w > 0f && h > 0f) pivot = new Vector2(regX / w, 1f - regY / h);
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = pos;
        }

        /// <summary>Halo's flashing colour: default, or a pulse between default and flashing while <paramref name="flashing"/>.</summary>
        public static HudColor FlashColor(HudFlash f, bool flashing, float time)
        {
            if (!flashing) return f.Default;
            float period = f.Period > 0.05f ? f.Period : 0.5f;
            float k = 0.5f - 0.5f * Mathf.Cos(time / period * Mathf.PI * 2f);
            if (f.Reverse) k = 1f - k;
            return Lerp(f.Default, f.Flashing, k);
        }

        public static HudColor Lerp(HudColor a, HudColor b, float k)
        {
            k = Mathf.Clamp01(k);
            return new HudColor((byte)(a.A + (b.A - a.A) * k), (byte)(a.R + (b.R - a.R) * k), (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));
        }

        /// <summary>
        /// Halo draws its HUD additively (a black colour is invisible, colours glow); Unity's UI blends by alpha. ours: the colour is
        /// drawn at full brightness with an alpha of its brightest channel, so black vanishes and bright blue reads as bright blue on
        /// any background. The colour's own alpha (0 in most HUD tags) is not used, except for dark see-through colours.
        /// </summary>
        public static Color Additive(HudColor c, float alpha = 1f)
        {
            float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
            float m = Mathf.Max(r, Mathf.Max(g, b));
            if (m < 0.004f) return new Color(0f, 0f, 0f, 0f);
            float a = c.A > 0 ? c.A / 255f : 1f;
            // a dark colour with its own alpha (the motion sensor's disc, #28061428) is a see-through darkening: blended, ours x3.2 so it
            // reads over a bright ground like the game's darkened disc
            if (m < 0.3f && c.A > 0) return new Color(r, g, b, Mathf.Clamp01(a * 3.2f * alpha));
            // additive colours: the colour's alpha does not dim an added colour (the game ignores it for these), its brightness does
            return new Color(r / m, g / m, b / m, Mathf.Clamp01(m * alpha));
        }

        // ------------------------------------------------------------------ building blocks

        public const int UiLayer = 5;

        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RawImage Raw(Transform parent, string name)
        {
            var rt = Node(parent, name);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            var m = HudMaterial;
            if (m != null) img.material = m;
            return img;
        }

        static Material _hudMat;

        /// <summary>
        /// The UI material for every HUD picture: drawn after everything (queue 4000) and never hidden by depth (ZTest Always), so a
        /// world-space visor / reticle is not covered by the sky (drawn at queue 2501), walls or the gun. A screen overlay is unaffected.
        /// </summary>
        public static Material HudMaterial
        {
            get
            {
                if (_hudMat == null)
                {
                    try
                    {
                        Material baseMat = Canvas.GetDefaultCanvasMaterial();
                        if (baseMat == null) return null;
                        _hudMat = new Material(baseMat) { name = "Halo HUD (on top)", renderQueue = 4000 };
                        _hudMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                    }
                    catch (Exception e) { HaloLog.Warn("HUD: material: " + e.Message); }
                }
                return _hudMat;
            }
        }

        /// <summary>Shows a sprite in a RawImage at a Halo placement (returns false when there is no picture).</summary>
        public static bool Show(RawImage img, HaloHudSprite s, HudAnchor a, HudPlace p, bool mp, bool byReg = false, float extraX = 0f, float extraY = 0f)
        {
            if (s == null) { if (img.enabled) img.enabled = false; return false; }
            float ss = SizeScale(p, mp) * p.WScale, sh = SizeScale(p, mp) * p.HScale, os = OffsetScale(p, mp);
            img.texture = s.Tex; img.uvRect = s.Uv;
            float w = s.W * ss, h = s.H * sh;
            if (byReg) Place(img.rectTransform, a, p.X * os + extraX, p.Y * os + extraY, w, h, s.RegX * ss, s.RegY * sh);
            else Place(img.rectTransform, a, p.X * os + extraX, p.Y * os + extraY, w, h);
            return true;
        }

        public static void SetOn(Behaviour b, bool on) { if (b != null && b.enabled != on) b.enabled = on; }
        public static void SetActive(GameObject g, bool on) { if (g != null && g.activeSelf != on) g.SetActive(on); }
    }
}

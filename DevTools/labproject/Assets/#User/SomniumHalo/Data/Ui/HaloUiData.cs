using System;
using System.Collections.Generic;
using System.Text;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] What the front end reads out of the player's own MAPS\ui.map (Halo PC's menu map), Unity-free so it runs on a
    /// worker thread and in the harness: the menu's string lists (unicode_string_list), its pictures (bitmap tags: the logo, the main
    /// menu items, the map previews, the game type pictures, the list backgrounds), its widget layout (ui_widget_definition bounds),
    /// its fonts (font tags), the menu sounds and music, and the ring scene (scenery\halo\halo, sky\sky_ui\sky_ui and the scenario's
    /// placement of the ring relative to the menu camera "uicam").
    /// Verified in the harness (DevTools/agents/F/harness): 168 string lists, mp_map_list = the 19 MP maps in the game's order with
    /// mp_map_grafix (21 pictures 256x128, the used part 140 x 115: <see cref="PreviewWidth"/>) in the same order.
    /// </summary>
    public sealed class HaloUiData
    {
        public readonly HaloMap Map;
        readonly Dictionary<string, string[]> _strings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The map files of mp_map_list / mp_map_grafix in the game's order (Halo PC's menu, index = picture).</summary>
        public static readonly string[] MenuMapOrder =
        {
            "beavercreek", "sidewinder", "damnation", "ratrace", "prisoner", "hangemhigh", "chillout", "carousel", "boardingaction",
            "bloodgulch", "wizard", "putput", "longest", "icefields", "deathisland", "dangercanyon", "infinity", "timberland", "gephyrophobia",
        };

        /// <summary>The used part of an mp_map_grafix picture (the map select screen's preview widget; the rest of the 256x128 is padding).</summary>
        public const int PreviewWidth = 140, PreviewHeight = 115;

        public HaloUiData(HaloMap map) { Map = map; }

        public static HaloUiData Load(string uiPath)
        {
            return new HaloUiData(new HaloMap(uiPath));
        }

        // ------------------------------------------------------------------ strings

        /// <summary>The strings of a unicode_string_list tag found by the END of its name ("mp_map_list"); empty when missing.</summary>
        public string[] Strings(string nameEnd)
        {
            string[] s;
            lock (_strings)
            {
                if (_strings.TryGetValue(nameEnd, out s)) return s;
            }

            s = ReadStrings(FindByEnd("ustr", nameEnd));
            lock (_strings) _strings[nameEnd] = s;
            return s;
        }

        /// <summary>One string of a list, trimmed (line breaks to spaces when <paramref name="oneLine"/>), or the fallback.</summary>
        public string String(string list, int index, string fallback, bool oneLine = true)
        {
            string[] s = Strings(list);
            if (index < 0 || index >= s.Length || string.IsNullOrEmpty(s[index].Trim())) return fallback;
            string v = s[index];
            if (oneLine) v = v.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
            v = v.Trim();
            while (v.Contains("  ")) v = v.Replace("  ", " ");
            return v;
        }

        public static string[] ReadStrings(HaloTag t)
        {
            var l = new List<string>();
            if (t == null) return l.ToArray();
            try
            {
                foreach (TagReader s in t.Root.Block(0, 20))
                {
                    byte[] b = s.DataBytes(0);
                    l.Add(b == null ? "" : Encoding.Unicode.GetString(b).TrimEnd('\0'));
                }
            }
            catch (Exception) { /* a broken list reads as what it had */ }
            return l.ToArray();
        }

        public HaloTag FindByEnd(string cls, string nameEnd)
        {
            uint c = HaloMap.FourCC(cls);
            foreach (HaloTag t in Map.Tags)
            {
                if (t.Class != c) continue;
                if (t.Name.EndsWith(nameEnd, StringComparison.OrdinalIgnoreCase) && (t.Name.Length == nameEnd.Length || t.Name[t.Name.Length - nameEnd.Length - 1] == '\\')) return t;
            }

            return null;
        }

        public HaloTag Bitmap(string name) { return Map.Find("bitm", name); }
        public HaloTag Sound(string name) { return Map.Find("snd!", name); }
        public HaloTag Looping(string name) { return Map.Find("lsnd", name); }

        // ------------------------------------------------------------------ the maps

        /// <summary>The menu index (picture / label / description) of a map file, -1 for a map the menu does not list.</summary>
        public static int MenuIndex(string mapFile)
        {
            for (int i = 0; i < MenuMapOrder.Length; i++) if (string.Equals(MenuMapOrder[i], mapFile, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The menu's name of a map ("Chiron TL34"), or null.</summary>
        public string MapLabel(string mapFile)
        {
            int i = MenuIndex(mapFile);
            return i < 0 ? null : String("mp_map_list", i, null);
        }

        /// <summary>The map select screen's description ("Red Blood White Snow", "4-16 players", "Supports vehicles"): lines kept.</summary>
        public string MapDescription(string mapFile)
        {
            int i = MenuIndex(mapFile);
            if (i < 0) return "";
            string[] s = Strings("mp_map_select\\map_data");
            if (i >= s.Length) return "";
            string d = s[i].Replace("\r\n", "\n").Replace('\r', '\n');
            while (d.Contains("\n\n\n")) d = d.Replace("\n\n\n", "\n\n");
            return d.Trim();
        }

        // ------------------------------------------------------------------ the campaign

        /// <summary>The 10 missions in the game's order (map_list / sp_levels / solo_level_select\map_data index).</summary>
        public static readonly string[] MissionOrder = { "a10", "a30", "a50", "b30", "b40", "c10", "c20", "c40", "d20", "d40" };

        /// <summary>sp_levels picture of a mission that cannot be played (the lock); its text is map_data [10].</summary>
        public const int LockedPicture = 10;

        public static int MissionIndex(string mapFile)
        {
            for (int i = 0; i < MissionOrder.Length; i++) if (string.Equals(MissionOrder[i], mapFile, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The mission's name as the menu shows it on one line ("The Truth and Reconciliation"), or null.</summary>
        public string MissionName(string mapFile)
        {
            int i = MissionIndex(mapFile);
            return i < 0 ? null : String("map_list_oneline", i, null);
        }

        /// <summary>The level select screen's description of a mission (its line breaks joined), or "".</summary>
        public string MissionDescription(int index)
        {
            string[] s = Strings("solo_level_select\\map_data");
            if (index < 0 || index >= s.Length) return "";
            // the game wraps by hand, hyphenating ("Cove-~nant"): join the lines back
            string d = s[index].Replace("\r\n", "\n").Replace('\r', '\n').Replace("-\n", "").Replace('\n', ' ');
            while (d.Contains("  ")) d = d.Replace("  ", " ");
            return d.Trim();
        }

        // ------------------------------------------------------------------ widgets

        public sealed class Widget
        {
            public HaloTag Tag;
            public string Name;
            public int Left, Top, Width, Height;      // 640 x 480 screen units, the parent's offsets added
            public HaloTag Background, Strings, Font;
            public int StringIndex;
            public float A, R, G, B;
            public readonly List<Widget> Children = new List<Widget>();
        }

        /// <summary>A ui_widget_definition and its children (their offsets added), or null.</summary>
        public Widget ReadWidget(string name, int depth)
        {
            HaloTag t = Map.Find("DeLa", name);
            return t == null ? null : ReadWidget(t, 0, 0, depth);
        }

        static Widget ReadWidget(HaloTag t, int ox, int oy, int depth)
        {
            TagReader r = t.Root;
            int top = r.I16(36), left = r.I16(38), bottom = r.I16(40), right = r.I16(42);
            var w = new Widget
            {
                Tag = t, Name = r.Str32(4), Left = left + ox, Top = top + oy, Width = right - left, Height = bottom - top,
                Background = r.Ref(56), Strings = r.Ref(236), Font = r.Ref(252), StringIndex = r.I16(302),
                A = r.F32(268), R = r.F32(272), G = r.F32(276), B = r.F32(280),
            };
            if (depth <= 0) return w;
            foreach (TagReader c in r.Block(992, 80))
            {
                HaloTag ct = c.Ref(0);
                if (ct != null) w.Children.Add(ReadWidget(ct, w.Left + c.I16(56), w.Top + c.I16(54), depth - 1));
            }

            return w;
        }

        // ------------------------------------------------------------------ the ring scene

        /// <summary>The menu camera ("uicam" cutscene camera point) and the ring scenery placement, Halo world units / radians.</summary>
        public bool RingPlacement(out HVec3 cameraPos, out float cameraYaw, out HaloTag ringModel, out HVec3 ringPos, out HVec3 ringRot)
        {
            cameraPos = new HVec3(); cameraYaw = 0f; ringModel = null; ringPos = new HVec3(); ringRot = new HVec3();
            HaloTag scnr = Map.Scenario;
            if (scnr == null) return false;
            foreach (TagReader c in scnr.Root.Block(1264, 104))
            {
                if (c.Str32(4) == "uicam") { cameraPos = c.Vec3(40); cameraYaw = c.F32(52); break; }
            }

            var s = new HaloScenario(scnr);
            foreach (HaloScenario.Placement p in s.Scenery)
            {
                if (p.Type == null || !p.Type.Name.EndsWith("halo", StringComparison.OrdinalIgnoreCase)) continue;
                ringPos = p.Position;
                ringRot = p.Rotation;
                HaloTag scen = p.Type;
                // scenery : object, the model dependency at +40
                ringModel = scen.Root.Ref(40);
                return ringModel != null;
            }

            return false;
        }

        public HaloTag SkyModel()
        {
            HaloTag sky = Map.Find("sky ", "sky\\sky_ui\\sky_ui");
            return sky != null ? sky.Root.Ref(0) : Map.Find("mod2", "sky\\sky_ui\\sky_ui");
        }
    }
}

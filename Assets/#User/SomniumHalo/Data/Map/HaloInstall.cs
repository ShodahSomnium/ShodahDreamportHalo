using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The player's own Halo: Combat Evolved for PC (retail, cache version 7; Custom Edition 609 is accepted too) - the folder with
    /// halo.exe and MAPS\*.map, bitmaps.map and sounds.map. Nothing of it ships with the world. Unity-free (worker thread / harness).
    /// </summary>
    public sealed class HaloInstall
    {
        public string Root, MapsFolder, BitmapsPath, SoundsPath, UiPath;
        public readonly List<MapEntry> Multiplayer = new List<MapEntry>();
        public readonly List<MapEntry> Campaign = new List<MapEntry>();

        public sealed class MapEntry
        {
            public string File, Name, Label, Build;
            public int Version;
            public long Size;
            public override string ToString() => Label + " (" + Name + ")";
        }

        // the usual places on C:, D: and E: (retail first, then Custom Edition)
        static readonly string[] Drives = { "C", "D", "E" };
        static readonly string[] Places =
        {
            @"\Program Files (x86)\Microsoft Games\Halo",
            @"\Program Files\Microsoft Games\Halo",
            @"\Microsoft Games\Halo",
            @"\Games\Halo",
            @"\Games\Microsoft Games\Halo",
            @"\Halo",
            @"\Halo Combat Evolved",
            @"\Program Files (x86)\Microsoft Games\Halo Custom Edition",
            @"\Program Files\Microsoft Games\Halo Custom Edition",
            @"\Games\Halo Custom Edition",
            @"\Halo Custom Edition",
        };

        /// <summary>Why the folder the player set was not used (null when it was used, or none was set). For the status line and the log.</summary>
        public static string HintProblem;

        /// <summary>The install at <paramref name="hint"/>, else the usual folders, else null.</summary>
        public static HaloInstall Find(string hint)
        {
            HintProblem = null;
            if (!string.IsNullOrEmpty(hint) && !string.IsNullOrEmpty(hint.Trim().Trim('"', '\'')))
            {
                string why;
                var inst = FromFolder(hint, out why);
                if (inst != null) return inst;
                HintProblem = why;
            }
            foreach (string drive in Drives)
                foreach (string place in Places)
                {
                    try
                    {
                        string root = drive + ":" + place;
                        if (!Directory.Exists(Path.Combine(root, "MAPS"))) continue;
                        var inst = Open(root);
                        if (inst != null && inst.Multiplayer.Count > 0) return inst;
                    }
                    catch (Exception) { }
                }
            return null;
        }

        /// <summary>
        /// The install the player pointed at, forgiving how it was written: quotes and spaces around it, forward slashes, the MAPS folder
        /// itself, halo.exe or a map file, or a folder one or two levels above the install (e.g. "D:\Games"). Null with the reason.
        /// </summary>
        public static HaloInstall FromFolder(string hint, out string why)
        {
            why = null;
            string p = (hint ?? "").Trim().Trim('"', '\'').Trim().Replace('/', '\\');
            if (p.Length == 2 && p[1] == ':') p += "\\";
            try
            {
                if (File.Exists(p)) p = Path.GetDirectoryName(p);                                       // halo.exe, a .map file
                p = p.TrimEnd('\\');
                if (p.Length == 2 && p[1] == ':') p += "\\";
                if (!Directory.Exists(p)) { why = "the folder \"" + p + "\" does not exist on this computer"; return null; }
                if (string.Equals(Path.GetFileName(p), "MAPS", StringComparison.OrdinalIgnoreCase)) p = Path.GetDirectoryName(p);
                string bad = null;
                var inst = TryRoot(p, ref bad);
                if (inst != null) return inst;
                // a folder above the install: look one and two levels down
                foreach (string d1 in SubFolders(p))
                {
                    inst = TryRoot(d1, ref bad);
                    if (inst != null) return inst;
                }
                foreach (string d1 in SubFolders(p))
                    foreach (string d2 in SubFolders(d1))
                    {
                        inst = TryRoot(d2, ref bad);
                        if (inst != null) return inst;
                    }
                why = bad ?? "no MAPS folder with Halo's maps was found in \"" + p + "\" or the folders inside it (pick the folder that has halo.exe and MAPS)";
            }
            catch (Exception e)
            {
                why = "\"" + p + "\" could not be read: " + e.Message;
            }
            return null;
        }

        static HaloInstall TryRoot(string root, ref string bad)
        {
            try
            {
                if (!Directory.Exists(Path.Combine(root, "MAPS"))) return null;
                var inst = Open(root);
                if (inst != null && inst.Multiplayer.Count > 0) return inst;
                if (bad == null)
                    bad = "\"" + Path.Combine(root, "MAPS") + "\" has no Halo: Combat Evolved PC multiplayer maps (retail version 7 or Custom Edition; the Master Chief Collection's files are a different format)";
            }
            catch (Exception e)
            {
                if (bad == null) bad = "\"" + root + "\" could not be read: " + e.Message;
            }
            return null;
        }

        static string[] SubFolders(string p)
        {
            try { return Directory.GetDirectories(p); }
            catch (Exception) { return new string[0]; }
        }

        public static HaloInstall Open(string root)
        {
            var inst = new HaloInstall { Root = root, MapsFolder = Path.Combine(root, "MAPS") };
            inst.BitmapsPath = Path.Combine(inst.MapsFolder, "bitmaps.map");
            inst.SoundsPath = Path.Combine(inst.MapsFolder, "sounds.map");
            inst.UiPath = Path.Combine(inst.MapsFolder, "ui.map");
            foreach (string f in Directory.GetFiles(inst.MapsFolder, "*.map"))
            {
                string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                if (n == "bitmaps" || n == "sounds" || n == "loc" || n == "ui") continue;
                var head = new byte[0x800];
                using (var s = File.OpenRead(f)) { if (s.Read(head, 0, head.Length) < head.Length) continue; }
                if (BitConverter.ToUInt32(head, 0) != 0x68656164) continue;
                int version = BitConverter.ToInt32(head, 4);
                if (version != 7 && version != 609) continue;
                int type = BitConverter.ToUInt16(head, 0x60);
                var e = new MapEntry { File = f, Name = n, Version = version, Size = new FileInfo(f).Length, Label = Label(n) };
                int k = 0; while (k < 32 && head[0x40 + k] != 0) k++;
                e.Build = System.Text.Encoding.ASCII.GetString(head, 0x40, k);
                if (type == 1) inst.Multiplayer.Add(e); else if (type == 0) inst.Campaign.Add(e);
            }
            inst.Multiplayer.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            inst.Campaign.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return inst;
        }

        public MapEntry Map(string name)
        {
            foreach (var m in Multiplayer) if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
            foreach (var m in Campaign) if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        /// <summary>The name the game's menu shows for a multiplayer map file.</summary>
        public static string Label(string file)
        {
            switch (file.ToLowerInvariant())
            {
                case "beavercreek": return "Battle Creek";
                case "bloodgulch": return "Blood Gulch";
                case "boardingaction": return "Boarding Action";
                case "carousel": return "Derelict";
                case "chillout": return "Chill Out";
                case "damnation": return "Damnation";
                case "dangercanyon": return "Danger Canyon";
                case "deathisland": return "Death Island";
                case "gephyrophobia": return "Gephyrophobia";
                case "hangemhigh": return "Hang 'Em High";
                case "icefields": return "Ice Fields";
                case "infinity": return "Infinity";
                case "longest": return "Longest";
                case "prisoner": return "Prisoner";
                case "putput": return "Chiron TL-34";
                case "ratrace": return "Rat Race";
                case "sidewinder": return "Sidewinder";
                case "timberland": return "Timberland";
                case "wizard": return "Wizard";
                case "a10": return "Pillar of Autumn";
                case "a30": return "Halo";
                case "a50": return "Truth and Reconciliation";
                case "b30": return "The Silent Cartographer";
                case "b40": return "Assault on the Control Room";
                case "c10": return "343 Guilty Spark";
                case "c20": return "The Library";
                case "c40": return "Two Betrayals";
                case "d20": return "Keyes";
                case "d40": return "The Maw";
                default: return file;
            }
        }
    }
}

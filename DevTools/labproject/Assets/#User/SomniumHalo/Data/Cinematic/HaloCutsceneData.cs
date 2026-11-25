using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A campaign scenario's cutscene data (Halo space, radians): cutscene flags (+1252, 92 B: name +4, position +36, facing yaw / pitch
    /// +48), cutscene camera points (+1264, 104 B: name +4, position +40, orientation yaw / pitch / roll +52, field of view +64), cutscene
    /// titles (+1276, 96 B: name +4, text bounds t/l/b/r +40 (640 x 480 screen), string index +48 into the scenario's "ingame help text"
    /// string list (+1412; verified: a10 3..5 = "Reveille", "Total Carnage", "AI Constructs and Cyborgs First!"), style +50, justification
    /// +52 (left / right / center), text flags +56, text / shadow colour ARGB +60 / +64, fade in / up / fade out ticks +68 / +72 / +76) and
    /// the recorded animations (+876, <see cref="HaloRecording"/>). Lookups by name are case-insensitive like the script compiler's. Unity-free.
    /// </summary>
    public sealed class HaloCutsceneData
    {
        public sealed class CameraPoint
        {
            public int Index; public string Name = "";
            public HVec3 Position; public float Yaw, Pitch, Roll, FieldOfView;
            public override string ToString() => Name + " @" + Position + " ypr " + (Yaw * 57.2958f).ToString("0.#") + "," + (Pitch * 57.2958f).ToString("0.#") + "," + (Roll * 57.2958f).ToString("0.#") + " fov " + (FieldOfView * 57.2958f).ToString("0.#");
        }

        public sealed class Flag
        {
            public int Index; public string Name = "";
            public HVec3 Position; public float Yaw, Pitch;
            public override string ToString() => Name + " @" + Position + " yaw " + (Yaw * 57.2958f).ToString("0.#");
        }

        public sealed class Title
        {
            public int Index; public string Name = "";
            /// <summary>Text bounds on Halo's 640 x 480 screen: top, left, bottom, right.</summary>
            public int Top, Left, Bottom, Right;
            public int StringIndex, Style, Justification, TextFlags;
            /// <summary>ARGB.</summary>
            public uint TextColor, ShadowColor;
            /// <summary>Ticks (30 a second; a10's "training": in 30, up 120, out 30 matches its script's sleep 150).</summary>
            public float FadeIn, Up, FadeOut;
            public string Text = "";
            public const int JustifyLeft = 0, JustifyRight = 1, JustifyCenter = 2;
            public override string ToString() => Name + " \"" + Text + "\" [" + Top + "," + Left + "," + Bottom + "," + Right + "] justify " + Justification + " in/up/out " + FadeIn + "/" + Up + "/" + FadeOut;
        }

        public readonly List<CameraPoint> CameraPoints = new List<CameraPoint>();
        public readonly List<Flag> Flags = new List<Flag>();
        public readonly List<Title> Titles = new List<Title>();
        public readonly List<HaloRecording> Recordings;
        public readonly List<string> TitleStrings;

        readonly Dictionary<string, int> _cams = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _flags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _titles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _recs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public HaloCutsceneData(HaloTag scenario)
        {
            var r = scenario.Root;
            int i = 0;
            foreach (var c in r.Block(1264, 104))
            {
                var p = new CameraPoint { Index = i, Name = c.Str32(4), Position = c.Vec3(40), Yaw = c.F32(52), Pitch = c.F32(56), Roll = c.F32(60), FieldOfView = c.F32(64) };
                CameraPoints.Add(p);
                if (!_cams.ContainsKey(p.Name)) _cams.Add(p.Name, i);
                i++;
            }
            i = 0;
            foreach (var c in r.Block(1252, 92))
            {
                var f = new Flag { Index = i, Name = c.Str32(4), Position = c.Vec3(36), Yaw = c.F32(48), Pitch = c.F32(52) };
                Flags.Add(f);
                if (!_flags.ContainsKey(f.Name)) _flags.Add(f.Name, i);
                i++;
            }
            TitleStrings = HaloStrings.Read(r.Ref(1412));
            i = 0;
            foreach (var c in r.Block(1276, 96))
            {
                var t = new Title
                {
                    Index = i, Name = c.Str32(4), Top = c.I16(40), Left = c.I16(42), Bottom = c.I16(44), Right = c.I16(46),
                    StringIndex = c.I16(48), Style = c.I16(50), Justification = c.I16(52), TextFlags = (int)c.U32(56),
                    TextColor = c.U32(60), ShadowColor = c.U32(64), FadeIn = c.F32(68), Up = c.F32(72), FadeOut = c.F32(76),
                };
                if (t.StringIndex >= 0 && t.StringIndex < TitleStrings.Count) t.Text = TitleStrings[t.StringIndex].Trim();
                Titles.Add(t);
                if (!_titles.ContainsKey(t.Name)) _titles.Add(t.Name, i);
                i++;
            }
            Recordings = HaloRecording.ReadAll(scenario);
            for (int k = 0; k < Recordings.Count; k++) if (!_recs.ContainsKey(Recordings[k].Name)) _recs.Add(Recordings[k].Name, k);
        }

        public CameraPoint Camera(string name) { int i; return name != null && _cams.TryGetValue(name, out i) ? CameraPoints[i] : null; }
        public CameraPoint Camera(int index) { return index >= 0 && index < CameraPoints.Count ? CameraPoints[index] : null; }
        public Flag FindFlag(string name) { int i; return name != null && _flags.TryGetValue(name, out i) ? Flags[i] : null; }
        public Flag FindFlag(int index) { return index >= 0 && index < Flags.Count ? Flags[index] : null; }
        public Title FindTitle(string name) { int i; return name != null && _titles.TryGetValue(name, out i) ? Titles[i] : null; }
        public Title FindTitle(int index) { return index >= 0 && index < Titles.Count ? Titles[index] : null; }
        public HaloRecording Recording(string name) { int i; return name != null && _recs.TryGetValue(name, out i) ? Recordings[i] : null; }
        public HaloRecording Recording(int index) { return index >= 0 && index < Recordings.Count ? Recordings[index] : null; }

        public int RecordingErrors { get { int n = 0; foreach (var r in Recordings) if (r.Error != null) n++; return n; } }
    }
}

using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The sounds of a hand reload, from the game's own files. Halo plays ONE sound per first-person reload animation (the animation graph's
    /// sound reference at the animation's sound frame: sound\sfx\weapons\weapon_anims\pistol_reload, ar_reload, sniper_reload_empty ...),
    /// covering the whole choreography. The VR hand actions (magazine out, magazine in, pull back, release) happen one at a time, so each
    /// plays the piece of that sound around the moment the same action happens in the animation: the moments are measured from the
    /// animation's own part motion (the "frame magazine" node leaving / coming back, the slide / operating handle furthest back and back
    /// at rest; Desk's HaloWeaponAnims sampler), else typical fractions of the animation (ours). The shotgun has a sound per step of its
    /// own (enter = the first shell taken, reload-empty = one shell in, exit-empty / exit-full = the pump); the plasma weapons their vent
    /// sounds (overheating, o-h-exit). Unity-free; the harness prints the table (`reloadsounds bloodgulch`).
    /// </summary>
    public sealed class HaloReloadSounds
    {
        /// <summary>A piece of a sound: from <see cref="Start"/> seconds for <see cref="Length"/> seconds (Length &lt;= 0 = the whole sound).</summary>
        public sealed class Piece
        {
            public HaloTag Sound; public float Start, Length; public string From = "";
            public override string ToString() => Sound == null ? "none" : System.IO.Path.GetFileName(Sound.Name) + (Length > 0f ? " " + Start.ToString("0.00") + "+" + Length.ToString("0.00") + " s" : " (whole)") + (From.Length > 0 ? " [" + From + "]" : "");
        }

        public Piece MagOut, MagIn, Pull, Release, ShellEnter, Shell, Pump, PumpFull, Vent, VentExit, Ready, Melee;
        /// <summary>The whole reload sounds (a monitor reload plays them as Halo does: at the animation's start).</summary>
        public HaloTag ReloadEmpty, ReloadFull;

        /// <summary>The weapon's first-person animations' sounds (name -> sound, frame).</summary>
        public static Dictionary<string, KeyValuePair<HaloTag, int>> AnimationSounds(HaloWeaponDef def)
        {
            var d = new Dictionary<string, KeyValuePair<HaloTag, int>>(StringComparer.OrdinalIgnoreCase);
            HaloTag g = def != null ? def.FirstPersonAnimations : null;
            if (g == null) return d;
            var refs = new List<HaloTag>();
            foreach (var s in g.Root.Block(84, 20)) refs.Add(s.Ref(0));
            foreach (var a in g.Root.Block(116, 180))
            {
                int si = a.I16(60);
                if (si < 0 || si >= refs.Count || refs[si] == null) continue;
                string name = a.Str32(0);
                if (name.StartsWith("first-person ", StringComparison.OrdinalIgnoreCase)) name = name.Substring(13);
                if (!d.ContainsKey(name)) d[name] = new KeyValuePair<HaloTag, int>(refs[si], a.I16(62));
            }

            return d;
        }

        public static HaloReloadSounds Build(HaloWeaponDef def, HaloWeaponPoints points)
        {
            var r = new HaloReloadSounds();
            var snd = AnimationSounds(def);
            KeyValuePair<HaloTag, int> e;
            if (snd.TryGetValue("ready", out e)) r.Ready = Whole(e, "ready");
            if (snd.TryGetValue("melee", out e)) r.Melee = Whole(e, "melee");
            if (snd.TryGetValue("overheating", out e) || snd.TryGetValue("o-h-s-enter", out e)) r.Vent = Whole(e, "overheating");
            if (snd.TryGetValue("o-h-exit", out e)) r.VentExit = Whole(e, "o-h-exit");
            if (def.Kind == HaloWeaponDef.Family.Shotgun)
            {
                if (snd.TryGetValue("enter", out e)) r.ShellEnter = Whole(e, "enter");
                if (snd.TryGetValue("reload-empty", out e)) r.Shell = Whole(e, "reload-empty");
                if (snd.TryGetValue("exit-empty", out e)) r.Pump = Whole(e, "exit-empty");
                if (snd.TryGetValue("exit-full", out e)) r.PumpFull = Whole(e, "exit-full");
                if (r.Pump == null) r.Pump = r.PumpFull;
                r.Pull = r.Pump;
                return r;
            }

            KeyValuePair<HaloTag, int> full, empty;
            bool hasEmpty = snd.TryGetValue("reload-empty", out empty), hasFull = snd.TryGetValue("reload-full", out full);
            if (hasEmpty) r.ReloadEmpty = empty.Key;
            if (hasFull) r.ReloadFull = full.Key;
            if (!hasEmpty && !hasFull) return r;
            KeyValuePair<HaloTag, int> use = hasEmpty ? empty : full;
            string animName = hasEmpty ? "reload-empty" : "reload-full";
            float frame0 = Math.Max(0, use.Value) / 30f;

            // when each action happens in the animation (seconds from its start), measured from its parts, else typical fractions (ours)
            float length = AnimationLength(def, animName);
            float tOut = 0.15f * length, tIn = 0.55f * length, tPull = 0.75f * length, tRel = 0.82f * length;
            string how = "fractions";
            try
            {
                var data = new HaloWeaponAnims(def.Tag);
                var clip = data.Find(animName);
                if (clip != null && clip.Anim != null && clip.Anim.Decode())
                {
                    length = clip.Duration;
                    int mag = data.Node("magazine"), pull = points != null && points.PullNode != null ? data.Node(points.PullNode) : -1;
                    float o, i;
                    if (mag >= 0 && Window(data, clip, mag, out o, out i)) { tOut = o; tIn = i; how = "'frame magazine' motion"; }
                    float p, q;
                    if (pull >= 0 && Peak(data, clip, pull, out p, out q)) { tPull = p; tRel = q; how += ", '" + data.NodeNames[pull] + "' motion"; }
                }
            }
            catch (Exception) { /* fractions */ }

            HaloTag s = use.Key;
            // each piece ends where the next action's piece starts (never two actions in one piece)
            float sOut = Math.Max(0f, tOut - frame0 - 0.05f), sIn = Math.Max(0f, tIn - frame0 - 0.12f), sPull = Math.Max(0f, tPull - frame0 - 0.2f), sRel = Math.Max(0f, tRel - frame0 - 0.05f);
            r.MagOut = new Piece { Sound = s, Start = sOut, Length = Cap(sOut, sIn, 0.55f), From = animName + " @" + tOut.ToString("0.00") + " s, " + how };
            r.MagIn = new Piece { Sound = s, Start = sIn, Length = Cap(sIn, sPull, 0.55f), From = animName + " @" + tIn.ToString("0.00") + " s" };
            r.Pull = new Piece { Sound = s, Start = sPull, Length = Cap(sPull, sRel, 0.3f), From = animName + " @" + tPull.ToString("0.00") + " s" };
            r.Release = new Piece { Sound = s, Start = sRel, Length = 0.45f, From = animName + " @" + tRel.ToString("0.00") + " s" };
            return r;
        }

        private static float Cap(float start, float next, float want) { float room = next - start; return room > 0.15f ? Math.Min(want, room) : want; }

        private static Piece Whole(KeyValuePair<HaloTag, int> e, string from) { return new Piece { Sound = e.Key, Start = 0f, Length = 0f, From = from }; }

        private static float AnimationLength(HaloWeaponDef def, string name)
        {
            HaloTag g = def.FirstPersonAnimations;
            if (g == null) return 2f;
            foreach (var a in g.Root.Block(116, 180))
            {
                string n = a.Str32(0);
                if (n.EndsWith(" " + name, StringComparison.OrdinalIgnoreCase)) return a.U16(34) / 30f;
            }

            return 2f;
        }

        private static float Distance(HaloWeaponAnims data, int node, float[] tr)
        {
            float dx = tr[node * 3] - data.RestTrans[node * 3], dy = tr[node * 3 + 1] - data.RestTrans[node * 3 + 1], dz = tr[node * 3 + 2] - data.RestTrans[node * 3 + 2];
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>When a part first leaves rest (30 % of its largest trip) and when it is last back.</summary>
        private static bool Window(HaloWeaponAnims data, HaloWeaponAnims.Clip c, int node, out float leave, out float back)
        {
            leave = back = 0f;
            int n = data.NodeNames.Length, frames = c.Anim.Frames;
            var rot = new float[n * 4]; var tr = new float[n * 3]; var mv = new bool[n];
            var dist = new float[frames];
            float max = 0f;
            for (int f = 0; f < frames; f++)
            {
                if (!data.Sample(c, f / 30f, rot, tr, mv) || !mv[node]) return false;
                dist[f] = Distance(data, node, tr);
                if (dist[f] > max) max = dist[f];
            }

            if (max < 0.003f) return false;
            float th = max * 0.3f;
            int first = -1, last = -1;
            for (int f = 0; f < frames; f++) if (dist[f] > th) { if (first < 0) first = f; last = f; }
            if (first < 0) return false;
            leave = first / 30f;
            back = (last + 1) / 30f;
            return true;
        }

        /// <summary>When a part is furthest from rest, and when it is back near rest after that.</summary>
        private static bool Peak(HaloWeaponAnims data, HaloWeaponAnims.Clip c, int node, out float far, out float back)
        {
            far = back = 0f;
            int n = data.NodeNames.Length, frames = c.Anim.Frames;
            var rot = new float[n * 4]; var tr = new float[n * 3]; var mv = new bool[n];
            var dist = new float[frames];
            float max = 0f; int at = -1;
            for (int f = 0; f < frames; f++)
            {
                if (!data.Sample(c, f / 30f, rot, tr, mv) || !mv[node]) return false;
                dist[f] = Distance(data, node, tr);
                if (dist[f] > max) { max = dist[f]; at = f; }
            }

            if (max < 0.002f || at < 0) return false;
            int rel = frames - 1;
            for (int f = at; f < frames; f++) if (dist[f] < max * 0.3f) { rel = f; break; }
            // a slide held back from the start (empty) and let go: the "pull" is the moment before it goes forward
            far = at / 30f;
            back = rel / 30f;
            if (back - far > 0.6f) far = Math.Max(0f, back - 0.25f);
            return true;
        }

        public string Describe()
        {
            return "mag out = " + Str(MagOut) + ", mag in = " + Str(MagIn) + ", pull = " + Str(Pull) + ", release = " + Str(Release)
                   + (ShellEnter != null || Shell != null ? ", shells = " + Str(ShellEnter) + " / " + Str(Shell) : "") + (Pump != null ? ", pump = " + Str(Pump) : "")
                   + (Vent != null ? ", vent = " + Str(Vent) + " / " + Str(VentExit) : "");
        }

        private static string Str(Piece p) { return p != null ? p.ToString() : "none"; }
    }
}

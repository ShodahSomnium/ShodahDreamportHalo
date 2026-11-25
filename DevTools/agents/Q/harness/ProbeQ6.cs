using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class Probe
{
    public static int QBiped(string[] a)
    {
        var m = new HaloMap(QMaps + a[1] + ".map"); var sc = new HaloScenario(m); var root = m.Scenario.Root;
        int nameIdx = sc.ObjectNames.FindIndex(n => string.Equals(n, a[2], StringComparison.OrdinalIgnoreCase));
        Console.WriteLine("name index " + nameIdx);
        var pal = root.Block(564, 48).ToList();
        foreach (var b in root.Block(552, 120)) if (b.I16(2) == nameIdx)
        {
            var bipd = pal[b.I16(0)].Ref(0); Console.WriteLine("biped " + bipd + " antr " + bipd.Root.Ref(56));
            var g = new HaloAnimGraph(bipd.Root.Ref(56));
            foreach (var an in g.Animations) if (an != null && (an.Name.Contains(a.Length > 3 ? a[3] : "move"))) { an.Decode(); float sx = 0, sy = 0; if (an.Info != null) for (int f = 0; f < an.Frames; f++) { sx += an.Info[f * 4]; sy += an.Info[f * 4 + 1]; } Console.WriteLine($"  {an.Name} {an.Frames} fr info {an.FrameInfoType} speed {Math.Sqrt(sx * sx + sy * sy) / Math.Max(1, an.Frames):0.####} wu/tick"); }
        }
        return 0;
    }
}
static partial class Probe
{
    public static float QUnitSpeed(HaloTag bipd, string seat, string weapon, out string how)
    {
        how = "none";
        var g = new HaloAnimGraph(bipd.Root.Ref(56));
        foreach (var s in g.Seats) { var sb = new System.Text.StringBuilder(); foreach (var w in s.Weapons) sb.Append(w.Name).Append(' '); }
        var st = g.FindSeat(seat); if (st == null) { how = "no seat " + seat; return 0; }
        var wc = st.Weapon(weapon); if (wc == null) { how = "no class " + weapon + " (have " + string.Join(",", st.Weapons.Select(w => w.Name)) + ")"; wc = st.Weapons.Count > 0 ? st.Weapons[0] : null; if (wc == null) return 0; }
        int ai = wc.Anim(HaloAnimIds.Weapon.MoveFront);
        var an = g.Get(ai); if (an == null) { how += " no move-front in " + wc.Name; return 0; }
        an.Decode(); float sx = 0, sy = 0; if (an.Info != null) for (int f = 0; f < an.Frames; f++) { sx += an.Info[f * 4]; sy += an.Info[f * 4 + 1]; }
        how = bipd.Name + " " + seat + "/" + wc.Name + " -> '" + an.Name + "'";
        return (float)Math.Sqrt(sx * sx + sy * sy) / Math.Max(1, an.Frames);
    }
}
static partial class Probe
{
    public static int QEv(string[] a)
    {
        var m = new HaloMap(QMaps + a[1] + ".map"); var d = new HaloCutsceneData(m.Scenario); var r = d.Recording(a[2]);
        Console.WriteLine(r + " initial " + r.Initial);
        int yaw = r.Initial.FacingYaw, pitch = r.Initial.FacingPitch;
        foreach (var e in r.Events)
        {
            string s = e.Kind == HaloRecording.KindThrottle ? $"throttle {e.X:0.###},{e.Y:0.###}" : e.Kind == HaloRecording.KindTurnSmall || e.Kind == HaloRecording.KindTurnLarge ? $"turn {e.A},{e.B}" : e.Kind == HaloRecording.KindFlags ? $"flags {e.A:X}" : $"kind {e.Kind:X2} {e.A}";
            if (e.Kind == HaloRecording.KindTurnSmall || e.Kind == HaloRecording.KindTurnLarge) { yaw += e.A; pitch += e.B; s += $" -> {yaw * 0.18:0.#},{pitch * 0.18:0.#} deg"; }
            if (a.Length < 4 || e.Kind != HaloRecording.KindTurnSmall) Console.WriteLine($"  t{e.Tick,5} {s}");
        }
        return 0;
    }
}
static partial class Probe
{
    public static int QLeaf(string[] a)
    {
        var m = new HaloMap(QMaps + a[1] + ".map"); var sc = new HaloScenario(m); var d = new HaloCutsceneData(m.Scenario);
        int bsp = int.Parse(a[2]);
        HaloCollisionBsp col = null; foreach (var cb in sc.BspRoot(bsp).Block(176, 96)) { col = new HaloCollisionBsp(cb); break; }
        for (int i = 3; i < a.Length; i++) { var c = d.Camera(a[i]); if (c == null) continue; Console.WriteLine($"{c.Name}: leaf {col.Leaf(c.Position.X, c.Position.Y, c.Position.Z)} {c}"); }
        return 0;
    }
}

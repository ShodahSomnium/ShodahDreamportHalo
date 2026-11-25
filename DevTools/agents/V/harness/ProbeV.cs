using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    /// <summary>V: vehi &lt;map&gt; [name part] - every vehicle tag with seats, physics, collision, weapons.</summary>
    public static int V(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("vehi"))
        {
            if (a.Length > 2 && !t.Name.Contains(a[2])) continue;
            var d = new HaloVehicleDef(t);
            foreach (var l in d.Describe()) Console.WriteLine(l);
            foreach (var w in d.Weapons) if (w != null) { var wd = new HaloWeaponDef(w); foreach (var tr in wd.Triggers) { var p = tr.Projectile != null ? new HaloProjectileDef(tr.Projectile) : null; Console.WriteLine($"  trigger rate {tr.RateMin}..{tr.RateMax} norepeat {tr.DoesNotRepeat} mag {tr.Magazine} proj {tr.Projectile} speed {(p != null ? p.InitialSpeed : 0)} fx {(tr.Effects.Count > 0 ? tr.Effects[0].Firing : null)}"); } foreach (var mg in wd.Magazines) Console.WriteLine($"  mag loaded {mg.RoundsLoadedMaximum} reload {mg.ReloadTime} chamber {mg.ChamberTime}"); }
            Console.WriteLine();
        }
        return 0;
    }
}
static partial class Probe
{
    public static int VColl(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("coll"))
        {
            var r = t.Root;
            Console.WriteLine($"{t.Name}: flags {r.U32(0):X} body {r.F32(8)} shock {r.F32(12)} destroyed thr {r.F32(184)} fx {r.Ref(188)} shield {r.F32(204)} nodes {r.Count(652)}");
        }
        return 0;
    }
}
static partial class Probe
{
    /// <summary>vseats &lt;map&gt;: the cyborg animation graph's vehicle seats - seat animations, weapon classes, IK.</summary>
    public static int VSeats(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var t = m.Tags.First(x => x.ClassName == "antr" && x.Name.EndsWith("cyborg"));
        var g = new HaloAnimGraph(t);
        foreach (var s in g.Seats)
        {
            if (s.Label == "stand" || s.Label == "crouch") continue;
            string sa = "";
            if (s.Anims != null) for (int i = 0; i < s.Anims.Length; i++) if (s.Anims[i] >= 0) sa += " [" + i + "]" + g.Get(s.Anims[i]).Name + "(" + g.Get(s.Anims[i]).Type + "," + g.Get(s.Anims[i]).Frames + ")";
            Console.WriteLine($"seat '{s.Label}' yaw R {s.RightYawPerFrame:0.###}x{s.RightFrames} L {s.LeftYawPerFrame:0.###}x{s.LeftFrames} pitch D {s.DownPitchPerFrame:0.###}x{s.DownFrames} U {s.UpPitchPerFrame:0.###}x{s.UpFrames}:{sa}");
            foreach (var ik in s.Ik) Console.WriteLine($"   ik {ik.Marker} -> {ik.AttachTo}");
            foreach (var w in s.Weapons)
            {
                string wa = "";
                if (w.Anims != null) for (int i = 0; i < w.Anims.Length; i++) if (w.Anims[i] >= 0) wa += " [" + i + "]" + g.Get(w.Anims[i]).Name + "(" + g.Get(w.Anims[i]).Type + "," + g.Get(w.Anims[i]).Frames + ")";
                Console.WriteLine($"   class '{w.Name}' yaw R {w.RightYawPerFrame:0.###}x{w.RightFrames} L {w.LeftYawPerFrame:0.###}x{w.LeftFrames} pitch D {w.DownPitchPerFrame:0.###}x{w.DownFrames} U {w.UpPitchPerFrame:0.###}x{w.UpFrames}:{wa}");
                foreach (var ik in w.Ik) Console.WriteLine($"      ik {ik.Marker} -> {ik.AttachTo}");
            }
        }
        return 0;
    }
}
static partial class Probe
{
    public static int VMarkers(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var t = m.Tags.First(x => x.ClassName == "mod2" && x.Name.EndsWith(a[2]));
        var model = new HaloModel(t);
        foreach (var mk in model.Markers) foreach (var i in mk.Instances) Console.WriteLine($"{mk.Name} node {model.Nodes[i.Node].Name} t {i.Translation} q ({i.Qi:0.###},{i.Qj:0.###},{i.Qk:0.###},{i.Qw:0.###})");
        return 0;
    }
}
static partial class Probe
{
    public static int VAnim(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var t = m.Tags.First(x => x.ClassName == "vehi" && x.Name.Contains(a[2]));
        var r = t.Root;
        var antr = r.Ref(56);
        Console.WriteLine(t.Name + " antr " + antr + " functions " + r.Count(344) + " a/b/c/d in " + r.U16(264) + "," + r.U16(266) + "," + r.U16(268) + "," + r.U16(270) + " unit in " + r.U16(408) + "," + r.U16(410) + "," + r.U16(412) + "," + r.U16(414));
        foreach (var f in r.Block(344, 360)) Console.WriteLine("  function usage '" + f.Str32(328) + "' flags " + f.U32(0) + " period " + f.F32(4) + " fn " + f.U16(10) + " scale by " + f.U16(8) + "/" + f.U16(12));
        if (antr == null) return 0;
        var g = new HaloAnimGraph(antr);
        for (int i = 0; i < g.Animations.Length; i++) Console.WriteLine("  anim " + i + " " + g.Animations[i]);
        var ar = antr.Root;
        foreach (var u in ar.Block(12, 100)) { Console.WriteLine("  unit seat " + u.Str32(0)); foreach (var x in u.Block(64, 2)) Console.Write(" " + x.I16(0)); Console.WriteLine(); }
        foreach (var v in ar.Block(36, 116)) { Console.Write("  vehicle anims:"); foreach (var x in v.Block(92, 2)) Console.Write(" " + x.I16(0)); Console.WriteLine(); }
        foreach (var d in ar.Block(48, 96)) { Console.Write("  device anims:"); foreach (var x in d.Block(84, 2)) Console.Write(" " + x.I16(0)); Console.WriteLine(); }
        return 0;
    }
}
static partial class Probe
{
    public static int VFoot(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var t = m.Tags.First(x => x.ClassName == "foot" && x.Name.Contains(a[2]));
        var e = t.Root; int n = e.Count(0);
        Console.WriteLine(t.Name + ": " + n + " effects");
        for (int k = 0; k < n; k++) { var ef = e.Element(0, k, 28); int mc = ef.Count(0); string s = ""; for (int j = 0; j < mc; j++) { var mm = ef.Element(0, j, 48); var fx = mm.Ref(0); var sn = mm.Ref(16); if (fx != null || sn != null) s += " [" + j + "] " + (fx != null ? fx.Name : "-") + " / " + (sn != null ? sn.Name : "-"); } Console.WriteLine("  effect " + k + ": " + mc + " materials" + s); }
        return 0;
    }
}

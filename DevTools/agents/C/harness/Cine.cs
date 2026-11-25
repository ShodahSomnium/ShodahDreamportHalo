using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class P
{
    // cine <map>: every bipd with compressed animations in its graph: custom-play each animation, count decode / NaN
    static int Cine(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        int bad = 0;
        foreach (var t in m.OfClass("bipd"))
        {
            var d = HaloCharacterDef.From(t);
            if (d == null || d.Model == null || d.Graph == null) continue;
            var g = new HaloAnimGraph(d.Graph);
            if (g.Compressed == 0) continue;
            var sk = new HaloSkeleton(new HaloModel(d.Model));
            var an = new HaloUnitAnimator(sk, g, 1);
            var inp = new HaloUnitAnimInput { Seat = "stand" };
            int ok = 0, fail = 0, nan = 0; float moved = 0;
            foreach (var x in g.Animations)
            {
                if (!an.PlayCustom(g, x, false, false, false)) { fail++; continue; }
                for (int k = 0; k < x.Frames + 2; k++) { an.Step(inp, 1 / 30f); moved += Math.Abs(an.RootX) + Math.Abs(an.RootY); }
                for (int i = 0; i < an.Pose.LRot.Length; i++) if (float.IsNaN(an.Pose.LRot[i])) { nan++; break; }
                ok++;
            }
            Console.WriteLine($"{t.Name}: graph {g.Tag.Name} {g.Animations.Length} anims ({g.Compressed} compressed): played {ok}, failed {fail}, NaN {nan}, root motion {moved * 3.048:0.0} m, custom ends {(an.CustomPlaying ? "no" : "yes")}");
            bad += fail + nan;
        }
        return bad;
    }
}

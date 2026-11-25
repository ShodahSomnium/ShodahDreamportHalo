using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class P
{
    // cine2 <map> <bipd end> : every cinematics antr whose node names match this biped's skeleton: play all its animations on it
    static int Cine2(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var t = m.OfClass("bipd").First(x => x.Name.EndsWith(a[2]));
        var d = HaloCharacterDef.From(t);
        var sk = new HaloSkeleton(new HaloModel(d.Model));
        var own = new HaloAnimGraph(d.Graph);
        int bad = 0;
        foreach (var at in m.OfClass("antr").Where(x => x.Name.StartsWith("cinematics")))
        {
            var g = new HaloAnimGraph(at);
            int matched = sk.MapFrom(g).Count(i => i >= 0);
            if (matched < g.NodeNames.Length * 0.8) continue;
            var an = new HaloUnitAnimator(sk, own, 1);
            var inp = new HaloUnitAnimInput { Seat = "stand" };
            int ok = 0, fail = 0;
            foreach (var x in g.Animations) { if (!an.PlayCustom(g, x, true, false, false)) { fail++; continue; } for (int k = 0; k < 5; k++) an.Step(inp, 1f / 30f); ok++; }
            Console.WriteLine($"  {at.Name}: nodes {matched}/{g.NodeNames.Length}, {g.Animations.Length} anims ({g.Compressed} compressed): played {ok}, failed {fail}");
            bad += fail;
        }
        return bad;
    }
}

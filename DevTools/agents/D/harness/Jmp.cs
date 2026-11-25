using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Jmp(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        if (a.Length > 3)
        {
            var g = new HaloAnimGraph(m.Tags.First(x => x.ClassName == "antr" && x.Name == a[2]));
            var an = g.Get(a[3]); an.Decode(); int n = an.NodeCount;
            for (int fr = 1; fr < an.Frames; fr++) for (int k = 0; k < n; k++) { double j = Ang(an.Rot, (fr * n + k) * 4, an.Rot, ((fr - 1) * n + k) * 4); if (j > 15) Console.WriteLine($"frame {fr} node {k} {g.NodeNames[k]}: {j:0.0} deg"); }
            return 0;
        }
        // calibration: uncompressed cinematic animations' worst frame-to-frame turn
        double worst = 0; string wn = "";
        foreach (var t in m.OfClass("antr").Where(x => x.Name.StartsWith("cinematics")))
        {
            var g = new HaloAnimGraph(t);
            foreach (var an in g.Animations.Where(x => !x.Compressed))
            {
                if (!an.Decode()) continue; int n = an.NodeCount;
                for (int fr = 1; fr < an.Frames; fr++) for (int k = 0; k < n; k++) { double j = Ang(an.Rot, (fr * n + k) * 4, an.Rot, ((fr - 1) * n + k) * 4); if (j > worst) { worst = j; wn = t.Name + " " + an.Name + " f" + fr + " " + g.NodeNames[k]; } }
            }
        }
        Console.WriteLine($"uncompressed cinematic worst frame-to-frame {worst:0.0} deg at {wn}");
        return 0;
    }
}

using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Sl(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var w = m.Find("weap", a[2]);
        var fp = new HaloModel(w.Root.Ref(1116));
        var tp = new HaloModel(w.Root.Ref(40));
        int ni = fp.NodeIndex(a[4]);
        Console.WriteLine($"FP rest {a[4]} t {fp.Nodes[ni].Translation}  3P rest t {tp.Nodes[tp.NodeIndex(a[4])].Translation}");
        foreach (var gt in new[] { w.Root.Ref(1132), w.Root.Ref(56) })
        {
            if (gt == null) continue;
            var g = new HaloAnimGraph(gt);
            var an = g.Get(a[3]); if (an == null) continue;
            an.Decode();
            int k = Array.IndexOf(g.NodeNames, a[4]);
            for (int f = 0; f < an.Frames; f += Math.Max(1, an.Frames / 12))
            {
                int i = f * an.NodeCount + k;
                Console.WriteLine($"  {gt.Name} {an.Name} f{f}: t ({an.Trans[i*3]:0.0000},{an.Trans[i*3+1]:0.0000},{an.Trans[i*3+2]:0.0000}) q ({an.Rot[i*4]:0.000},{an.Rot[i*4+1]:0.000},{an.Rot[i*4+2]:0.000},{an.Rot[i*4+3]:0.000})");
            }
        }
        return 0;
    }
}

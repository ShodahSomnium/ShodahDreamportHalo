using System;
using SomniumSpace.Worlds.SomniumHalo;
static class RockProbe
{
    public static void Run(HaloMap m, string graph, string anim)
    {
        var g = new HaloAnimGraph(m.Find("antr", graph));
        Console.WriteLine("nodes " + string.Join(",", g.NodeNames));
        var a = g.Get(anim); a.Decode();
        Console.WriteLine($"{a.Name} {a.Type} frames {a.Frames} nodes {a.NodeCount} rot {a.RotMask:X} tr {a.TransMask:X} err {a.Error}");
        for (int f = 0; f < a.Frames; f += 10)
            for (int k = 0; k < a.NodeCount; k++)
                Console.WriteLine($" f{f} n{k} t({a.Trans[(f*a.NodeCount+k)*3]:0.###},{a.Trans[(f*a.NodeCount+k)*3+1]:0.###},{a.Trans[(f*a.NodeCount+k)*3+2]:0.###}) q({a.Rot[(f*a.NodeCount+k)*4]:0.##},{a.Rot[(f*a.NodeCount+k)*4+1]:0.##},{a.Rot[(f*a.NodeCount+k)*4+2]:0.##},{a.Rot[(f*a.NodeCount+k)*4+3]:0.##})");
    }
}

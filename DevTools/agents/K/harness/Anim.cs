using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static class AnimProbe
{
    public static void Run(HaloMap m, string part)
    {
        foreach (var t in m.OfClass("mach").Concat(m.OfClass("ctrl")).Where(t => t.Name.Contains(part)))
        {
            var d = new HaloDeviceDef(t);
            Console.WriteLine("== " + d);
            if (d.Model == null) { Console.WriteLine("  no model"); continue; }
            var model = new HaloModel(d.Model);
            Console.WriteLine("  model nodes: " + string.Join(", ", model.Nodes.Select(n => n.Name + " p" + n.Parent + " t" + n.Translation)));
            if (d.AnimationGraph == null) continue;
            var g = new HaloAnimGraph(d.AnimationGraph);
            foreach (var a in g.Animations)
            {
                a.Decode();
                Console.WriteLine($"  anim {a.Name} {a.Type} frames {a.Frames} nodes {a.NodeCount} rotmask {a.RotMask:X} trmask {a.TransMask:X} err {a.Error}");
                if (a.Rot == null) continue;
                for (int f = 0; f < a.Frames; f++)
                    for (int k = 0; k < a.NodeCount; k++)
                        Console.WriteLine($"     f{f} n{k} q({a.Rot[(f * a.NodeCount + k) * 4]:0.###},{a.Rot[(f * a.NodeCount + k) * 4 + 1]:0.###},{a.Rot[(f * a.NodeCount + k) * 4 + 2]:0.###},{a.Rot[(f * a.NodeCount + k) * 4 + 3]:0.###}) t({a.Trans[(f * a.NodeCount + k) * 3]:0.###},{a.Trans[(f * a.NodeCount + k) * 3 + 1]:0.###},{a.Trans[(f * a.NodeCount + k) * 3 + 2]:0.###})");
            }
        }
    }
}

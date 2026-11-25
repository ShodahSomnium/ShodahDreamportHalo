using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Wan(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var w in m.OfClass("weap"))
        {
            var r = w.Root; var model = r.Ref(40); var fpa = r.Ref(1132); var tpa = r.Ref(56);
            if (model == null) continue;
            var hm = new HaloModel(model);
            var names = hm.Nodes.Select(n => n.Name).ToList();
            Console.WriteLine($"== {w.Name}: 3P nodes [{string.Join(", ", names)}]");
            foreach (var (tag, lab) in new[] { (tpa, "3P"), (fpa, "FP") })
            {
                if (tag == null) continue;
                var g = new HaloAnimGraph(tag);
                foreach (var an in g.Animations)
                {
                    var moved = Enumerable.Range(0, an.NodeCount).Where(k => (an.Rotates(k) || an.Translates(k)) && k < g.NodeNames.Length && names.Contains(g.NodeNames[k]) && g.NodeNames[k] != "frame gun").Select(k => g.NodeNames[k].Replace("frame ", ""));
                    var ml = moved.ToList();
                    if (ml.Count > 0 || lab == "3P") Console.WriteLine($"   {lab} '{an.Name}' {an.Frames}f moves [{string.Join(",", ml)}]");
                }
            }
        }
        return 0;
    }
}

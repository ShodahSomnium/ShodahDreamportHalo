using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Fp(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var w in m.OfClass("weap"))
        {
            if (a.Length > 2 && !w.Name.Contains(a[2])) continue;
            var r = w.Root;
            var model = r.Ref(40);
            Console.WriteLine($"== {w.Name}  3P model {model?.Name} antr {r.Ref(56)?.Name}");
            if (model != null) { var hm = new HaloModel(model); Console.WriteLine("   3P nodes: " + string.Join(", ", hm.Nodes.Select(n => n.Name))); }
            // weapon: first person block? find any antr refs among the tag's dependencies by scanning for antr class refs
            for (int o = 0; o < 1288 - 16; o += 4)
            {
                if (r.U32(o) == HaloMap.FourCC("antr") || r.U32(o) == HaloMap.FourCC("mod2"))
                {
                    var t = r.Ref(o); if (t != null) Console.WriteLine($"   ref @{o} {HaloMap.FourCC(r.U32(o))} {t.Name}");
                }
            }
        }
        return 0;
    }
}

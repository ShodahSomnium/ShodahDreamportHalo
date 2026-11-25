using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static class AttSurv
{
    public static void Run(HaloMap m)
    {
        foreach (var t in m.OfClass("effe"))
            foreach (var e in t.Root.Block(52, 68))
                foreach (var p in e.Block(56, 232))
                {
                    if ((p.U32(100) & 1) == 0) continue;
                    var pt = p.Ref(84); if (pt == null) continue;
                    var r = pt.Root;
                    Console.WriteLine($"{t.Name} | {pt.Name} create {p.U16(4)} vel {p.F32(132):0.##}..{p.F32(136):0.##} cone {p.F32(140):0.##} rad {p.F32(160):0.###}..{p.F32(164):0.###} life {r.F32(56):0.##}..{r.F32(60):0.##} fade {r.F32(64):0.##}/{r.F32(68):0.##} orient {r.U16(172)} phys {(r.Ref(20)?.Name ?? "-")}");
                }
    }
}

using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static class PartSurv
{
    public static void Run(HaloMap m)
    {
        foreach (var t in m.OfClass("part"))
        {
            var r = t.Root; var bm = r.Ref(4);
            int first = r.I16(152), ni = r.I16(154), nl = r.I16(156), nf = r.I16(158);
            string fr = "?";
            if (bm != null) { var b = new HaloBitmap(bm); fr = string.Join(",", Enumerable.Range(first, ni + nl + nf).Select(i => i < b.Sequences.Count ? Math.Max(b.Sequences[i].Count, b.Sequences[i].Sprites.Count).ToString() : "x")); }
            float life = r.F32(56), anim = r.F32(128);
            Console.WriteLine($"{t.Name,-60} flags {r.U32(0):X3} life {r.F32(56):0.##}..{r.F32(60):0.##} fade {r.F32(64):0.##}/{r.F32(68):0.##} anim {anim:0.#} seq {ni}/{nl}/{nf} frames [{fr}] radanim {r.F32(116):0.##}..{r.F32(120):0.##} fbfade {r.U16(220)} phys {(r.Ref(20)?.Name ?? "-")}");
        }
    }
}

using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class F
{
    public static void Widget(HaloTag t, int depth, int maxDepth, int ox, int oy)
    {
        var r = t.Root;
        string ind = new string(' ', depth * 2);
        int top = r.I16(36), left = r.I16(38), bottom = r.I16(40), right = r.I16(42);
        var bg = r.Ref(56); var ustr = r.Ref(236); var font = r.Ref(252);
        Console.WriteLine($"{ind}{t.Name} type {r.U16(0)} '{r.Str32(4)}' bounds t{top} l{left} b{bottom} r{right} at +{ox},{oy} bg {bg?.Name} str {ustr?.Name}#{r.I16(302)} font {font?.Name} col {r.F32(268):0.##},{r.F32(272):0.##},{r.F32(276):0.##},{r.F32(280):0.##} just {r.U16(284)} hdr {r.Ref(340)?.Name}");
        if (depth >= maxDepth) return;
        foreach (var c in r.Block(992, 80))
        {
            var w = c.Ref(0);
            if (w != null) Widget(w, depth + 1, maxDepth, c.I16(56), c.I16(54));
        }
    }
}

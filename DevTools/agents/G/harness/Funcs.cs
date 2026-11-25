using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class G
{
    /// <summary>`funcs <map> <weapon name part>`: the weapon's function inputs, object functions and attachment scales.</summary>
    static int Funcs(HaloMap m, string[] a)
    {
        foreach (var t in m.OfClass("weap"))
        {
            if (!t.Name.Contains(a[2])) continue;
            var r = t.Root;
            Console.WriteLine($"{t.Name}: obj in a {r.U16(264)} b {r.U16(266)} c {r.U16(268)} d {r.U16(270)}  weap in a {r.U16(816)} b {r.U16(818)} c {r.U16(820)} d {r.U16(822)} heatIllum {r.F32(864)} overheated {r.Ref(884)}");
            int i = 0;
            foreach (var f in r.Block(344, 360))
                Console.WriteLine($"   fn{i++} '{f.Str32(328)}' flags {f.U32(0)} period {f.F32(4)} scalePeriodBy {f.U16(8)} wave {f.U16(10)} scaleFnBy {f.U16(12)} mapTo {f.U16(30)} add {f.U16(34)} scaleResBy {f.U16(36)} bounds {f.U16(38)} {f.F32(40)}..{f.F32(44)} turnOffWith {f.I16(54)} scaleBy {f.F32(56)}");
            foreach (var at in r.Block(320, 72))
                Console.WriteLine($"   att {at.Ref(0)} @'{at.Str32(16)}' primary {at.U16(48)} secondary {at.U16(50)} color {at.U16(52)}");
            foreach (var l in r.Block(320, 72).Select(x => x.Ref(0)).Where(x => x != null && x.ClassName == "ligh"))
            {
                var lr = l.Root;
                Console.WriteLine($"   ligh {l.Name}: flags {lr.U32(0)} radius {lr.F32(4)} mod {lr.F32(8)}..{lr.F32(12)} lensOnly {lr.F32(24)} col {lr.F32(60)},{lr.F32(64)},{lr.F32(68)}..{lr.F32(76)},{lr.F32(80)},{lr.F32(84)} lens {lr.Ref(172)} intensity {lr.F32(212)} duration {lr.F32(244)}");
            }
            var model = r.Ref(40);
            if (model != null)
            {
                var hm = new HaloModel(model);
                Console.WriteLine("   markers: " + string.Join(", ", hm.Markers.Select(k => k.Name)));
            }
        }
        return 0;
    }
}

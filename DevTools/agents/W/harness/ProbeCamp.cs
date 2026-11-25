using System;
using System.Linq;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Camp(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        var bsps = Enumerable.Range(0, sc.Bsps.Count).Select(i => new HaloBsp(sc, i)).ToList();
        var r = sc.Tag.Root;
        // scenery +32 bsp indices
        int idx = 0, agree = 0, dis = 0, zero = 0;
        foreach (var s in r.Block(528, 72))
        {
            ushort bits = s.U16(32); var p = s.Vec3(8);
            int inside = 0; for (int b = 0; b < bsps.Count; b++) if (bsps[b].ClusterAt(p.X, p.Y, p.Z + 0.05f) >= 0) inside |= 1 << b;
            if (bits == 0) zero++; else if ((bits & inside) != 0 || inside == 0) agree++; else dis++;
            if (idx++ < int.Parse(a.Length > 2 ? a[2] : "8")) Console.WriteLine($"scen {idx-1} bits {Convert.ToString(bits, 2)} inside {Convert.ToString(inside, 2)} {s.U16(4)}");
        }
        Console.WriteLine($"scenery {idx}: bits zero {zero}, agree with containment {agree}, disagree {dis}");
        var counts = new[] { ("machines", sc.Machines), ("controls", sc.Controls), ("lights", sc.LightFixtures), ("sound", sc.SoundScenery), ("scenery", sc.Scenery) };
        foreach (var (n, list) in counts)
        {
            int none = 0, multi = 0;
            foreach (var pl in list) { int c = 0; for (int b = 0; b < bsps.Count; b++) if (bsps[b].ClusterAt(pl.Position.X, pl.Position.Y, pl.Position.Z + 0.05f) >= 0) c++; if (c == 0) none++; if (c > 1) multi++; }
            Console.WriteLine($"{n} {list.Count}: in no bsp {none}, in several {multi}");
        }
        // starting locations bsp
        var st = r.Block(840, 104).ToList();
        Console.WriteLine($"player starting profiles {r.Count(840)} ; start locations {sc.StartLocations.Count}: " + string.Join(" ", sc.StartLocations.Take(4).Select(s => s.Position + " bsp" + s.BspIndex)));
        for (int b = 0; b < bsps.Count; b++)
        {
            var B = bsps[b]; var rr = sc.BspRoot(b);
            Console.WriteLine($" bsp{b} {B.Tag.Name}: weather pal {rr.Count(436)} polys {rr.Count(448)} lensflares {rr.Count(284)} markers {rr.Count(296)} decals {rr.Count(600)} detail {rr.Count(588)} pathsurf {rr.Count(484)} pathedge {rr.Count(496)} bgsound {rr.Count(508)} markers {rr.Count(576)} breakable {rr.Count(364)}");
            foreach (var w in rr.Block(436, 240)) Console.WriteLine($"    weather {w.Str32(0)} ps {w.Ref(32)?.Name} wind {w.Ref(128)?.Name}");
            foreach (var l in rr.Block(284, 16)) Console.WriteLine($"    lens flare {l.Ref(0)?.Name}");
        }
        return 0;
    }
}

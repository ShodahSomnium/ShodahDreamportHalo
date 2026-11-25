using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Extras(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        foreach (var t in m.OfClass("lens"))
        {
            var r = t.Root;
            Console.WriteLine($"lens {t.Name}: falloff {r.F32(0):0.00} cutoff {r.F32(4):0.00} cos {r.F32(8):0.00}/{r.F32(12):0.00} occl r {r.F32(16)} fade {r.F32(24)}-{r.F32(28)} bitmap {r.Ref(32)?.Name} flags {r.U16(48)} rot fn {r.U16(128)} scale h{r.F32(160)} v{r.F32(164)}");
            foreach (var x in r.Block(196, 128)) Console.WriteLine($"    refl flags {x.U16(0)} img {x.I16(4)} pos {x.F32(28)} radius {x.F32(40)}-{x.F32(44)} scaled {x.U16(48)} bright {x.F32(52)}-{x.F32(56)} by {x.U16(60)} tint {x.F32(64)},{x.F32(68)},{x.F32(72)},{x.F32(76)}");
        }
        foreach (var t in m.OfClass("rain"))
        {
            var r = t.Root;
            foreach (var p in r.Block(36, 604)) Console.WriteLine($"weather {t.Name} type {p.Str32(0)} flags {p.U32(32)} fade in {p.F32(36)}-{p.F32(40)} out {p.F32(44)}-{p.F32(48)} count {p.F32(164)}-{p.F32(168)} phys {p.Ref(172)?.Name} accel {p.F32(204)}-{p.F32(208)} radius {p.F32(252)}-{p.F32(256)} color {p.F32(308)},{p.F32(312)},{p.F32(316)},{p.F32(320)} sprite size {p.F32(340)} bitmap {p.Ref(404)?.Name} render {p.U16(420)} dirsrc {p.U16(422)} shader {p.U16(460)} blend {p.U16(466)}");
        }
        foreach (var t in m.OfClass("dobc"))
        {
            var r = t.Root;
            Console.WriteLine($"dobc {t.Name} type {r.U16(0)} z {r.F32(4)} plate {r.Ref(52)?.Name} types {r.Count(68)}");
            foreach (var p in r.Block(68, 96)) Console.WriteLine($"    {p.Str32(0)} seq {p.U8(32)} fl {p.U8(33)} first {p.U8(34)} n {p.U8(35)} fade {p.F32(48)}-{p.F32(52)} size {p.F32(56)} min {p.Vec3(64)} max {p.Vec3(76)}");
        }
        foreach (var dp in sc.Tag.Root.Block(960, 48)) Console.WriteLine("detail palette " + dp.Ref(0)?.Name);
        for (int i = 0; i < sc.Bsps.Count; i++)
        {
            var b = new HaloBsp(sc, i);
            if (!b.HasDetailObjects) continue;
            var d = b.DetailObjects;
            Console.WriteLine($"bsp{i} detail cells {d.Count(0)} instances {d.Count(12)} counts {d.Count(24)} zrefs {d.Count(36)}");
            int k = 0;
            foreach (var c in d.Block(0, 32)) { if (k++ >= 3) break; Console.WriteLine($"   cell {c.I16(0)},{c.I16(2)},{c.I16(4)} offz {c.I16(6)} layers {c.U32(8):X} start {c.I32(12)} count {c.I32(16)}"); }
            k = 0;
            foreach (var c in d.Block(12, 6)) { if (k++ >= 6) break; Console.WriteLine($"   inst {c.I8(0)},{c.I8(1)},{c.I8(2)} data {c.U8(3)} color {c.U16(4):X4}"); }
            k = 0; foreach (var c in d.Block(24, 2)) { if (k++ >= 8) break; Console.Write($" cnt {c.U16(0)}"); } Console.WriteLine();
            k = 0; foreach (var c in d.Block(36, 16)) { if (k++ >= 3) break; Console.WriteLine($"   zref {c.F32(0)},{c.F32(4)},{c.F32(8)},{c.F32(12)}"); }
        }
        return 0;
    }
}

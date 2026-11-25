using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    public const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static int Main(string[] a)
    {
        switch (a[0])
        {
            case "list": return List(a);
            case "anims": return Anims(a);
            case "nums": return Nums(a);
            case "poses": return Poses(a);
            case "boxes": return Boxes(a);
            case "mod": return Mod(a);
            case "all": return AllMaps(a);
            case "weaps": return Weaps(a);
            case "cine": return Cine(a);
            case "cine2": return Cine2(a);
            case "foot": return Foot(a);
            case "ws2": return Ws2(a);
            case "ws2anim": return Ws2Anim(a);
        }
        return 1;
    }

    static string N(HaloTag t) => t == null ? "-" : t.Name;

    static int List(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("bipd"))
        {
            var r = t.Root;
            Console.WriteLine($"bipd {t.Name}  model {N(r.Ref(40))} antr {N(r.Ref(56))} coll {N(r.Ref(112))} team {r.U16(384)} weapons {r.Count(728)} flags {r.U32(756):X}");
            var c = r.Ref(112);
            if (c != null)
            {
                var cr = c.Root;
                Console.Write($"   coll body {cr.F32(8)} shield {cr.F32(204)} shieldmat {cr.U16(210)} regions:");
                foreach (var g in cr.Block(576, 84)) { Console.Write($" {g.Str32(0)}[thr {g.F32(40)} perms "); foreach (var p in g.Block(72, 32)) Console.Write(p.Str32(0) + ","); Console.Write("]"); }
                Console.WriteLine();
                Console.Write("   materials:"); foreach (var mm in cr.Block(564, 72)) Console.Write($" {mm.Str32(0)}(t{mm.U16(36)} f{mm.U32(32)} s{mm.F32(44)} b{mm.F32(60)})"); Console.WriteLine();
                Console.Write("   nodes:"); foreach (var nn in cr.Block(652, 64)) Console.Write($" {nn.Str32(0)}(r{nn.I16(32)} b{nn.Count(52)})"); Console.WriteLine();
            }
            var md = r.Ref(40);
            if (md != null)
            {
                var hm = new HaloModel(md);
                Console.Write($"   model nodes {hm.Nodes.Count} regions:");
                foreach (var g in hm.Regions) { Console.Write($" {g.Name}["); foreach (var p in g.Permutations) Console.Write(p.Name + ","); Console.Write("]"); }
                Console.WriteLine();
                Console.Write("   markers:"); foreach (var mk in hm.Markers) Console.Write(" " + mk.Name); Console.WriteLine();
            }
            var an = r.Ref(56);
            if (an != null)
            {
                var g = new HaloAnimGraph(an);
                Console.Write($"   antr nodes {g.NodeNames.Length} anims {g.Animations.Length} comp {g.Compressed} seats:");
                foreach (var s in g.Seats) { Console.Write($" {s.Label}("); foreach (var w in s.Weapons) Console.Write(w.Name + "/" + w.Types.Count + ","); Console.Write(")"); }
                Console.WriteLine();
            }
        }
        foreach (var t in m.OfClass("actv"))
        {
            var r = t.Root;
            Console.Write($"actv {t.Name} unit {N(r.Ref(20))} weapon {N(r.Ref(100))} major {N(r.Ref(36))} equip {N(r.Ref(448))} body {r.F32(512)} shield {r.F32(516)} gren {r.U16(384)} cnt {r.I16(464)}-{r.I16(466)} perm {r.I16(524)} cc:");
            foreach (var cc in r.Block(556, 32)) Console.Write($" ({cc.F32(0):0.00},{cc.F32(4):0.00},{cc.F32(8):0.00})-({cc.F32(12):0.00},{cc.F32(16):0.00},{cc.F32(20):0.00})");
            Console.WriteLine();
        }
        return 0;
    }
}

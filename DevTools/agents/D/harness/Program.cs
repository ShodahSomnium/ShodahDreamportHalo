using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    public const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static int Main(string[] a)
    {
        switch (a[0])
        {
            case "antr": return Antr(a);
            case "labels": return Labels(a);
            case "bind": return Bind(a);
            case "jmp": return Jmp(a);
            case "twin": return Twin(a);
            case "brute": return Brute(a);
            case "q48": return Q48(a);
            case "hex": return Hex(a);
            case "cmp": return Cmp(a);
            case "wa2": return Wa2(a);
            case "sl": return Sl(a);
            case "wan": return Wan(a);
            case "fp": return Fp(a);
            case "box": return Box(a);
            case "ik": return Ik(a);
            case "dmg": return Dmg(a);
            case "all": return All(a);
            case "hands": return Hands(a);
            case "grid": return Grid(a);
            case "stick": return Stick(a);
            case "fmt": return Fmt(a);
            case "markers": return Markers(a);
        }
        return 1;
    }

    static int Antr(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("antr")) Console.WriteLine($"{t.Name}");
        foreach (var t in m.OfClass("bipd")) Console.WriteLine($"bipd {t.Name}");
        var tag = m.Tags.First(x => x.ClassName == "antr" && x.Name.Contains(a.Length > 2 ? a[2] : "cyborg"));
        var r = tag.Root;
        Console.WriteLine($"== {tag.Name} flags {r.U16(100)} units {r.Count(12)} weapons {r.Count(24)} nodes {r.Count(104)} anims {r.Count(116)}");
        int i = 0;
        foreach (var n in r.Block(104, 64)) Console.WriteLine($"  node {i++} {n.Str32(0)} parent {n.I16(36)} flags {n.U32(40)}");
        i = 0; int comp = 0;
        foreach (var an in r.Block(116, 180))
        {
            ushort fl = an.U16(58);
            if ((fl & 1) != 0) comp++;
            if (a.Length > 3 || i < 400) Console.WriteLine($"  anim {i} '{an.Str32(0)}' type {an.U16(32)} frames {an.U16(34)} fsize {an.U16(36)} finfo {an.U16(38)} nodes {an.U16(44)} loop {an.I16(46)} flags {fl:X} next {an.I16(56)} default {an.DataSize(140)} frame {an.DataSize(160)} finfoSize {an.DataSize(72)} comp@{an.U32(136)} rot {an.U32(108):X8}{an.U32(112):X8} tr {an.U32(92):X8} sc {an.U32(124):X8}");
            i++;
        }
        Console.WriteLine($"compressed {comp} of {i}");
        foreach (var u in r.Block(12, 100))
        {
            Console.WriteLine($" unit '{u.Str32(0)}' anims {u.Count(64)} ik {u.Count(76)} weapons {u.Count(88)} yaw {u.F32(32)}/{u.F32(36)} {u.U16(40)}/{u.U16(42)} pitch {u.F32(44)}/{u.F32(48)} {u.U16(52)}/{u.U16(54)}");
            Console.Write("    unit anims:"); foreach (var x in u.Block(64, 2)) Console.Write(" " + x.I16(0)); Console.WriteLine();
            foreach (var w in u.Block(88, 188))
            {
                Console.Write($"   weapon '{w.Str32(0)}' grip '{w.Str32(32)}' hand '{w.Str32(64)}' types {w.Count(176)} ik {w.Count(164)}:");
                foreach (var x in w.Block(152, 2)) Console.Write(" " + x.I16(0));
                Console.WriteLine();
                foreach (var ty in w.Block(176, 60)) { Console.Write($"     type '{ty.Str32(0)}':"); foreach (var x in ty.Block(48, 2)) Console.Write(" " + x.I16(0)); Console.WriteLine(); }
            }
        }
        return 0;
    }
}

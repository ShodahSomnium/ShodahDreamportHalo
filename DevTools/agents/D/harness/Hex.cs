using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Hex(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var t = m.Tags.First(x => x.ClassName == "antr" && x.Name == a[2]);
        var an = t.Root.Element(116, int.Parse(a[3]), 180);
        byte[] d = an.DataBytes(160);
        int n = a.Length > 4 ? int.Parse(a[4]) : 128;
        Console.WriteLine($"'{an.Str32(0)}' frames {an.U16(34)} nodes {an.U16(44)} size {d.Length}");
        for (int i = 0; i < Math.Min(n, d.Length); i += 16)
        {
            Console.Write($"{i,6:X}: ");
            for (int j = 0; j < 16 && i + j < d.Length; j += 4) Console.Write($"{BitConverter.ToUInt32(d, i + j):X8} ");
            Console.Write("  ");
            for (int j = 0; j < 16 && i + j < d.Length; j += 4) Console.Write($"{BitConverter.ToInt32(d, i + j),9} ");
            Console.WriteLine();
        }
        return 0;
    }
}

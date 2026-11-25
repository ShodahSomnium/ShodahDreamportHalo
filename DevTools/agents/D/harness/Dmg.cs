using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Dmg(string[] a)
    {
        Load(a[1]);
        var r = _g.Tag.Root;
        Console.WriteLine("unit damage " + r.Count(60));
        int i = 0;
        foreach (var x in r.Block(60, 2)) { int k = x.I16(0); Console.WriteLine($"  {i++}: {k} {(_g.Get(k) != null ? _g.Get(k).Name : "-")}"); }
        return 0;
    }
}

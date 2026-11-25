using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Wa2(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var w in m.OfClass("weap"))
        {
            var wa = new HaloWeaponAnims(w);
            if (wa.Clips.Count == 0) continue;
            Console.Write($"{w.Name}:");
            foreach (var k in HaloWeaponAnims.Kinds) { var c = wa.Find(k[0]); if (c != null) Console.Write($" {k[0]}='{c.Name}'{(c.FirstPerson ? "fp" : "3p")} {wa.Travel(c) * 304.8:0.0}cm {c.Duration:0.00}s;"); }
            Console.WriteLine();
        }
        return 0;
    }
}

using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static class Hex
{
    public static int Run(string[] a)
    {
        var m = new HaloMap(MainA.Maps + a[1] + ".map");
        var res = HaloResources.Get(MainA.Maps + "sounds.map");
        foreach (var t in m.OfClass("snd!"))
        {
            if (t.Name.IndexOf(a[2], StringComparison.OrdinalIgnoreCase) < 0) continue;
            var r = t.Root;
            var p = r.Element(152, 0, 72).Element(60, 0, 124);
            var b = res.Read(p.I32(72), Math.Min(96, p.I32(64)));
            Console.WriteLine(t.Name + " fmt " + r.U16(110) + " ch " + r.U16(108));
            Console.WriteLine(string.Join(" ", b.Select(x => x.ToString("X2"))));
        }
        return 0;
    }
}

using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Box(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var w in m.OfClass("weap"))
        {
            var mt = w.Root.Ref(40); if (mt == null) continue;
            var hm = new HaloModel(mt);
            float x0 = 9, x1 = -9, y0 = 9, y1 = -9, z0 = 9, z1 = -9;
            foreach (var g in hm.Geometries.Take(1)) foreach (var p in g.Parts) { var d = hm.Read(p); foreach (var v in d.Position) { x0 = Math.Min(x0, v.X); x1 = Math.Max(x1, v.X); y0 = Math.Min(y0, v.Y); y1 = Math.Max(y1, v.Y); z0 = Math.Min(z0, v.Z); z1 = Math.Max(z1, v.Z); } }
            Console.WriteLine($"{w.Name,-45} fwd {x0*3.048:0.00}..{x1*3.048:0.00} left {y0*3.048:0.00}..{y1*3.048:0.00} up {z0*3.048:0.00}..{z1*3.048:0.00} m");
        }
        return 0;
    }
}

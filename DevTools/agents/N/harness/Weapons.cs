using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class NetProbe
{
    public static int Weapons(string map)
    {
        var m = new HaloMap(Maps + map + ".map");
        foreach (var t in m.OfClass("weap").Concat(m.OfClass("proj").Where(p => p.Name.Contains("grenade"))).Concat(m.OfClass("bipd")))
        {
            var model = t.Root.Ref(40);
            Console.WriteLine(t.Index + " " + t.Name + " model " + (model != null ? model.Name : "-"));
            if (model == null) continue;
            var hm = new HaloModel(model);
            Console.WriteLine("   nodes: " + string.Join(", ", hm.Nodes.Select(n => n.Name)));
            Console.WriteLine("   markers: " + string.Join(", ", hm.Markers.Select(k => k.Name + "@" + (k.Instances.Count > 0 ? k.Instances[0].Translation.ToString() : "?"))));
            float minx = 1e9f, maxx = -1e9f, miny = 1e9f, maxy = -1e9f, minz = 1e9f, maxz = -1e9f;
            foreach (var g in hm.Geometries.Take(1)) foreach (var p in g.Parts) { var d = hm.Read(p); foreach (var v in d.Position) { minx = Math.Min(minx, v.X); maxx = Math.Max(maxx, v.X); miny = Math.Min(miny, v.Y); maxy = Math.Max(maxy, v.Y); minz = Math.Min(minz, v.Z); maxz = Math.Max(maxz, v.Z); } }
            Console.WriteLine($"   bounds x {minx:0.000}..{maxx:0.000} y {miny:0.000}..{maxy:0.000} z {minz:0.000}..{maxz:0.000} (units)");
        }
        return 0;
    }
}

using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class F
{
    public static int Ring(string[] a)
    {
        var m = new HaloMap(Maps + "ui.map");
        foreach (var t in m.OfClass("mod2").Where(x => x.Name.Contains(a[1])))
        {
            var model = new HaloModel(t);
            Console.WriteLine($"{t.Name}: nodes {model.Nodes.Count} regions {model.Regions.Count} geos {model.Geometries.Count} shaders {model.Shaders.Count} uv {model.UScale}x{model.VScale}");
            foreach (var n in model.Nodes) Console.WriteLine($"   node {n.Name} parent {n.Parent} t {n.Translation} q ({n.Qi:0.###},{n.Qj:0.###},{n.Qk:0.###},{n.Qw:0.###})");
            foreach (var r in model.Regions) foreach (var p in r.Permutations) Console.WriteLine($"   region {r.Name} perm {p.Name} lods {string.Join(",", p.Lod)}");
            foreach (var s in model.Shaders) Console.WriteLine($"   shader {s.Shader}");
            int gi = 0;
            foreach (var g in model.Geometries)
            {
                foreach (var p in g.Parts)
                {
                    var d = model.Read(p);
                    float minx = 1e9f, maxx = -1e9f, miny = 1e9f, maxy = -1e9f, minz = 1e9f, maxz = -1e9f, minu = 1e9f, maxu = -1e9f, minv = 1e9f, maxv = -1e9f;
                    for (int i = 0; i < d.Position.Length; i++) { var q = d.Position[i]; minx = Math.Min(minx, q.X); maxx = Math.Max(maxx, q.X); miny = Math.Min(miny, q.Y); maxy = Math.Max(maxy, q.Y); minz = Math.Min(minz, q.Z); maxz = Math.Max(maxz, q.Z); minu = Math.Min(minu, d.U[i]); maxu = Math.Max(maxu, d.U[i]); minv = Math.Min(minv, d.V[i]); maxv = Math.Max(maxv, d.V[i]); }
                    Console.WriteLine($"   geo {gi} part shader {p.Shader} v {p.VertexCount} tris {d.Triangles.Length / 3} x {minx:0.##}..{maxx:0.##} y {miny:0.##}..{maxy:0.##} z {minz:0.##}..{maxz:0.##} u {minu:0.##}..{maxu:0.##} v {minv:0.##}..{maxv:0.##}");
                }
                gi++;
            }
        }
        return 0;
    }
}

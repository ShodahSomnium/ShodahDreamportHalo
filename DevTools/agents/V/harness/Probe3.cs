using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    public static int Models(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        int bad = 0, parts = 0;
        foreach (var t in m.OfClass("mod2"))
        {
            var model = new HaloModel(t);
            int verts = 0, tris = 0;
            foreach (var g in model.Geometries) foreach (var p in g.Parts)
            {
                parts++;
                var d = model.Read(p);
                verts += p.VertexCount; tris += d.Triangles.Length / 3;
                int ib = m.ModelDataOffset + m.ModelIndexOffset + p.IndexOffset;
                int max = 0; for (int i = 0; i < p.IndexCount; i++) { int c = m.U16(ib + i * 2); if (c != 0xFFFF) max = Math.Max(max, c); }
                if (max >= p.VertexCount) bad++;
            }
            if (a.Length > 2 && t.Name.Contains(a[2]))
            {
                Console.WriteLine($"{t.Name}: nodes {model.Nodes.Count} markers {model.Markers.Count} regions {model.Regions.Count} geos {model.Geometries.Count} shaders {model.Shaders.Count} uv {model.UScale}x{model.VScale}");
                foreach (var n in model.Nodes) Console.WriteLine($"   node {n.Name} parent {n.Parent} t {n.Translation} q ({n.Qi:0.###},{n.Qj:0.###},{n.Qk:0.###},{n.Qw:0.###})");
                foreach (var r in model.Regions) foreach (var p in r.Permutations) Console.WriteLine($"   region {r.Name} perm {p.Name} lods {string.Join(",", p.Lod)}");
                foreach (var mk in model.Markers) Console.WriteLine($"   marker {mk.Name} x{mk.Instances.Count} node {mk.Instances.FirstOrDefault()?.Node} t {mk.Instances.FirstOrDefault()?.Translation}");
                foreach (var s in model.Shaders) Console.WriteLine($"   shader {s.Shader}");
                var p0 = model.Geometries[0].Parts[0]; var d0 = model.Read(p0);
                Console.WriteLine($"   part0 v {p0.VertexCount} idx {p0.IndexCount} tris {d0.Triangles.Length / 3} pos0 {d0.Position[0]} uv0 {d0.U[0]},{d0.V[0]} n0 {d0.Node0[0]} w {d0.Weight0[0]}");
            }
            if (a.Length <= 2) Console.WriteLine($"{t.Name,-60} geos {model.Geometries.Count} verts {verts} tris {tris}");
        }
        Console.WriteLine($"parts {parts}, parts with indices beyond their vertices: {bad}");
        return 0;
    }

    public static int BitmapPng(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var res = HaloResources.Get(Maps + "bitmaps.map");
        var t = m.Tags.First(x => x.ClassName == "bitm" && x.Name.Contains(a[2]));
        var b = new HaloBitmap(t);
        Directory.CreateDirectory(a[3]);
        for (int i = 0; i < b.Images.Count && i < 8; i++)
        {
            var img = b.Images[i];
            var raw = HaloBitmap.Raw(m, img, res);
            var rgba = HaloBitmap.DecodeRgba(img, raw);
            string f = Path.Combine(a[3], Path.GetFileName(t.Name).Replace(' ', '_') + "_" + i + ".png");
            Png.Write(f, img.Width, img.Height, rgba);
            Console.WriteLine($"{f} {img.Width}x{img.Height} {img.Format} type {img.Type}");
        }
        return 0;
    }
}

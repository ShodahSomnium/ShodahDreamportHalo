using System;
using System.Linq;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static float GroundZ(HaloBsp b, float x, float y)
    {
        // highest collision surface with normal up containing (x,y)
        var c = b.Collision; float best = float.NaN;
        foreach (var s in c.Surfaces)
        {
            c.PlaneOf(s, out float nx, out float ny, out float nz, out float d);
            if (nz < 0.3f) continue;
            int n = s.Ring.Length; bool inside = true;
            for (int k = 0; k < n && inside; k++)
            {
                int p = s.Ring[k] * 3, q = s.Ring[(k + 1) % n] * 3;
                if ((c.Vertices[q] - c.Vertices[p]) * (y - c.Vertices[p + 1]) - (c.Vertices[q + 1] - c.Vertices[p + 1]) * (x - c.Vertices[p]) < -1e-5f) inside = false;
            }
            if (!inside) continue;
            float z = (d - nx * x - ny * y) / nz;
            if (float.IsNaN(best) || z > best) best = z;
        }
        return best;
    }
    static int Detail(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        var b = new HaloBsp(sc, int.Parse(a.Length > 2 ? a[2] : "0"));
        var d = b.DetailObjects;
        var cells = d.Block(0, 32).ToList(); var inst = d.Block(12, 6).ToList(); var counts = d.Block(24, 2).Select(x => (int)x.U16(0)).ToList(); var zr = d.Block(36, 16).ToList();
        float S = float.Parse(a.Length > 3 ? a[3] : "8");
        for (int ci = 0; ci < 6; ci++)
        {
            var c = cells[ci * 7 + 3];
            int cx = c.I16(0), cy = c.I16(2), cz = c.I16(4), oz = c.I16(6), start = c.I32(12), cnt = counts[c.I32(16)];
            var z = zr[c.I32(16)];
            Console.WriteLine($"cell {cx},{cy},{cz} offz {oz} start {start} n {cnt} zref {z.F32(0):0.000},{z.F32(4):0.000},{z.F32(8):0.000},{z.F32(12):0.000}");
            for (int k = 0; k < Math.Min(4, cnt); k++)
            {
                var i = inst[start + k];
                int ux = i.U8(0), uy = i.U8(1), uz = i.U8(2);
                float fx = (cx + ux / 256f) * S, fy = (cy + uy / 256f) * S;
                float gz = GroundZ(b, fx, fy);
                Console.WriteLine($"   bytes {i.I8(0)},{i.I8(1)},{i.I8(2)} data {i.U8(3)} -> xy ({fx:0.00},{fy:0.00}) ground z {gz:0.000}  cellz*S {cz * S}");
            }
        }
        return 0;
    }
}

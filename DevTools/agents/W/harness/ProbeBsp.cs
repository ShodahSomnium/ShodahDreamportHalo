using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    public static readonly string[] Mp = { "beavercreek","bloodgulch","boardingaction","carousel","chillout","damnation","dangercanyon","deathisland","gephyrophobia","hangemhigh","icefields","infinity","longest","prisoner","putput","ratrace","sidewinder","timberland","wizard" };
    static int Bsp(string[] a)
    {
        var names = a.Length > 1 && a[1] != "all" ? new[] { a[1] } : Mp;
        foreach (var name in names)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var m = new HaloMap(Maps + name + ".map");
            var sc = new HaloScenario(m);
            for (int i = 0; i < sc.Bsps.Count; i++)
            {
                var b = new HaloBsp(sc, i);
                int agree = 0, disagree = 0;
                foreach (var s in b.Collision.Surfaces)
                {
                    b.Collision.PlaneOf(s, out float nx, out float ny, out float nz, out float dd);
                    double px = 0, py = 0, pz = 0; var v = b.Collision.Vertices;
                    for (int k = 0; k < s.Ring.Length; k++)
                    {
                        int p = s.Ring[k] * 3, q = s.Ring[(k + 1) % s.Ring.Length] * 3;
                        px += (v[p + 1] - v[q + 1]) * (v[p + 2] + v[q + 2]);
                        py += (v[p + 2] - v[q + 2]) * (v[p] + v[q]);
                        pz += (v[p] - v[q]) * (v[p + 1] + v[q + 1]);
                    }
                    if (px * nx + py * ny + pz * nz > 0) agree++; else disagree++;
                }
                var flags = b.Collision.Surfaces.GroupBy(s => s.Flags).Select(g => g.Key + ":" + g.Count());
                var mats = b.Collision.Surfaces.GroupBy(s => b.MaterialType(s)).Select(g => g.Key + ":" + g.Count());
                // render-side winding vs vertex normals
                int ra = 0, rd = 0;
                foreach (var s in b.Sections)
                    for (int t = 0; t < s.Triangles.Length; t += 3)
                    {
                        int i0 = s.Triangles[t] * 3, i1 = s.Triangles[t + 1] * 3, i2 = s.Triangles[t + 2] * 3; var P = s.Position;
                        float ux = P[i1] - P[i0], uy = P[i1 + 1] - P[i0 + 1], uz = P[i1 + 2] - P[i0 + 2];
                        float vx = P[i2] - P[i0], vy = P[i2 + 1] - P[i0 + 1], vz = P[i2 + 2] - P[i0 + 2];
                        float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                        if (cx * s.Normal[i0] + cy * s.Normal[i0 + 1] + cz * s.Normal[i0 + 2] > 0) ra++; else rd++;
                    }
                Console.WriteLine($"{name} bsp{i} {b.Tag.Name}: sections {b.Sections.Count} tris {b.TriangleCount} verts {b.VertexCount} lm {b.LightmapBitmap?.Name} | coll surf {b.Collision.Surfaces.Count} verts {b.Collision.Vertices.Length/3} ring/plane agree {agree} disagree {disagree} | render winding ccw-vs-normal {ra}/{rd} | flags {string.Join(" ", flags)} | mats {string.Join(" ", mats)} | fogpal {b.FogPalette.Count} fogplanes {b.FogPlanes.Count} clusters {b.Clusters.Count} skies {string.Join(",", b.Clusters.Select(c=>c.Sky).Distinct())} | bounds z {b.BoundsMinZ:0.0}..{b.BoundsMaxZ:0.0} | problems {b.Problems.Count} {string.Join("; ", b.Problems.Take(3))} | {sw.ElapsedMilliseconds} ms");
            }
        }
        return 0;
    }
}

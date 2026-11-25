using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// "The lightmap under a point": a 2D grid (Halo x / y) over the structure BSP's lightmapped, floor-like render triangles. A query
    /// finds the highest such triangle at or below the point and returns its lightmap colour (bilinear from the decoded lightmap page,
    /// gamma 0..1) and the interpolated incident radiosity direction (lightmap vertex). Halo lights its dynamic objects from the lightmap
    /// of the ground beneath them; the World module turns this into each object's ambient + directional light (HaloObjectLight).
    /// Thread-safe for queries after construction. Halo space. Unity-free.
    /// </summary>
    public sealed class HaloLightGrid
    {
        public struct Sample
        {
            public bool Found;
            /// <summary>Lightmap colour (gamma, 0..1).</summary>
            public float R, G, B;
            /// <summary>Incident direction (towards the light, Halo space, unit).</summary>
            public float Dx, Dy, Dz;
            /// <summary>Height of the surface found (Halo z).</summary>
            public float SurfaceZ;
        }

        /// <summary>A decoded lightmap page (RGBA32, rows as stored: v = 0 at the top).</summary>
        public sealed class Page { public int Width, Height; public byte[] Rgba; }

        struct Tri { public int Section, Index; public float MinZ, MaxZ; }

        readonly HaloBsp _bsp;
        readonly Dictionary<int, Page> _pages;
        readonly float _x0, _y0, _cell;
        readonly int _nx, _ny;
        readonly List<Tri>[] _cells;

        public HaloLightGrid(HaloBsp bsp, Dictionary<int, Page> pages, float cell = 2f)
        {
            _bsp = bsp; _pages = pages; _cell = cell;
            _x0 = bsp.BoundsMinX - cell; _y0 = bsp.BoundsMinY - cell;
            _nx = Math.Max(1, (int)Math.Ceiling((bsp.BoundsMaxX - _x0) / cell) + 2);
            _ny = Math.Max(1, (int)Math.Ceiling((bsp.BoundsMaxY - _y0) / cell) + 2);
            if ((long)_nx * _ny > 4000000) { _cell *= (float)Math.Sqrt((double)_nx * _ny / 4000000.0); _nx = (int)((bsp.BoundsMaxX - _x0) / _cell) + 3; _ny = (int)((bsp.BoundsMaxY - _y0) / _cell) + 3; }
            _cells = new List<Tri>[_nx * _ny];
            for (int s = 0; s < bsp.Sections.Count; s++)
            {
                var sec = bsp.Sections[s];
                if (sec.Lightmap < 0 || sec.LightmapUv == null || !pages.ContainsKey(sec.Lightmap)) continue;
                var P = sec.Position;
                for (int t = 0; t + 2 < sec.Triangles.Length; t += 3)
                {
                    int a = sec.Triangles[t] * 3, b = sec.Triangles[t + 1] * 3, c = sec.Triangles[t + 2] * 3;
                    // face normal z (Halo render triangles are clockwise about their normal)
                    float ux = P[b] - P[a], uy = P[b + 1] - P[a + 1], uz = P[b + 2] - P[a + 2];
                    float vx = P[c] - P[a], vy = P[c + 1] - P[a + 1], vz = P[c + 2] - P[a + 2];
                    float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                    float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (len < 1e-9f || -nz / len < 0.35f) continue;     // only floor-like faces (normal up)
                    float minX = Math.Min(P[a], Math.Min(P[b], P[c])), maxX = Math.Max(P[a], Math.Max(P[b], P[c]));
                    float minY = Math.Min(P[a + 1], Math.Min(P[b + 1], P[c + 1])), maxY = Math.Max(P[a + 1], Math.Max(P[b + 1], P[c + 1]));
                    float minZ = Math.Min(P[a + 2], Math.Min(P[b + 2], P[c + 2])), maxZ = Math.Max(P[a + 2], Math.Max(P[b + 2], P[c + 2]));
                    int i0 = Cx(minX), i1 = Cx(maxX), j0 = Cy(minY), j1 = Cy(maxY);
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            int k = j * _nx + i;
                            if (_cells[k] == null) _cells[k] = new List<Tri>(4);
                            _cells[k].Add(new Tri { Section = s, Index = t, MinZ = minZ, MaxZ = maxZ });
                        }
                }
            }
        }

        int Cx(float x) { return Math.Max(0, Math.Min(_nx - 1, (int)((x - _x0) / _cell))); }
        int Cy(float y) { return Math.Max(0, Math.Min(_ny - 1, (int)((y - _y0) / _cell))); }

        /// <summary>The lightmap at the surface below a Halo-space point (within <paramref name="maxDrop"/> units).</summary>
        public Sample At(float x, float y, float z, float maxDrop = 30f)
        {
            var best = new Sample();
            if (x < _x0 || y < _y0) return best;
            var list = _cells[Cy(y) * _nx + Cx(x)];
            if (list == null) return best;
            float bestZ = float.NegativeInfinity;
            int bs = -1, bt = -1; float bw0 = 0, bw1 = 0, bw2 = 0;
            for (int n = 0; n < list.Count; n++)
            {
                var tr = list[n];
                if (tr.MinZ > z + 0.05f || tr.MaxZ < z - maxDrop) continue;
                var sec = _bsp.Sections[tr.Section];
                var P = sec.Position;
                int a = sec.Triangles[tr.Index] * 3, b = sec.Triangles[tr.Index + 1] * 3, c = sec.Triangles[tr.Index + 2] * 3;
                float d = (P[b + 1] - P[c + 1]) * (P[a] - P[c]) + (P[c] - P[b]) * (P[a + 1] - P[c + 1]);
                if (Math.Abs(d) < 1e-12f) continue;
                float w0 = ((P[b + 1] - P[c + 1]) * (x - P[c]) + (P[c] - P[b]) * (y - P[c + 1])) / d;
                float w1 = ((P[c + 1] - P[a + 1]) * (x - P[c]) + (P[a] - P[c]) * (y - P[c + 1])) / d;
                float w2 = 1f - w0 - w1;
                if (w0 < -0.001f || w1 < -0.001f || w2 < -0.001f) continue;
                float sz = w0 * P[a + 2] + w1 * P[b + 2] + w2 * P[c + 2];
                if (sz > z + 0.05f || sz < z - maxDrop || sz <= bestZ) continue;
                bestZ = sz; bs = tr.Section; bt = tr.Index; bw0 = w0; bw1 = w1; bw2 = w2;
            }
            if (bs < 0) return best;
            var s2 = _bsp.Sections[bs];
            int ia = s2.Triangles[bt], ib = s2.Triangles[bt + 1], ic = s2.Triangles[bt + 2];
            float u = bw0 * s2.LightmapUv[ia * 2] + bw1 * s2.LightmapUv[ib * 2] + bw2 * s2.LightmapUv[ic * 2];
            float v = bw0 * s2.LightmapUv[ia * 2 + 1] + bw1 * s2.LightmapUv[ib * 2 + 1] + bw2 * s2.LightmapUv[ic * 2 + 1];
            Page pg = _pages[s2.Lightmap];
            Bilinear(pg, u, v, out best.R, out best.G, out best.B);
            if (s2.Incident != null)
            {
                float dx = bw0 * s2.Incident[ia * 3] + bw1 * s2.Incident[ib * 3] + bw2 * s2.Incident[ic * 3];
                float dy = bw0 * s2.Incident[ia * 3 + 1] + bw1 * s2.Incident[ib * 3 + 1] + bw2 * s2.Incident[ic * 3 + 1];
                float dz = bw0 * s2.Incident[ia * 3 + 2] + bw1 * s2.Incident[ib * 3 + 2] + bw2 * s2.Incident[ic * 3 + 2];
                float l = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (l > 1e-6f) { best.Dx = dx / l; best.Dy = dy / l; best.Dz = dz / l; } else best.Dz = 1f;
            }
            else best.Dz = 1f;
            best.SurfaceZ = bestZ;
            best.Found = true;
            return best;
        }

        static void Bilinear(Page p, float u, float v, out float r, out float g, out float b)
        {
            float fx = u * p.Width - 0.5f, fy = v * p.Height - 0.5f;
            int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy);
            float tx = fx - x0, ty = fy - y0;
            float r0, g0, b0, r1, g1, b1, r2, g2, b2, r3, g3, b3;
            Texel(p, x0, y0, out r0, out g0, out b0); Texel(p, x0 + 1, y0, out r1, out g1, out b1);
            Texel(p, x0, y0 + 1, out r2, out g2, out b2); Texel(p, x0 + 1, y0 + 1, out r3, out g3, out b3);
            r = Lerp(Lerp(r0, r1, tx), Lerp(r2, r3, tx), ty);
            g = Lerp(Lerp(g0, g1, tx), Lerp(g2, g3, tx), ty);
            b = Lerp(Lerp(b0, b1, tx), Lerp(b2, b3, tx), ty);
        }

        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        static void Texel(Page p, int x, int y, out float r, out float g, out float b)
        {
            x = Math.Max(0, Math.Min(p.Width - 1, x)); y = Math.Max(0, Math.Min(p.Height - 1, y));
            int o = (y * p.Width + x) * 4;
            r = p.Rgba[o] / 255f; g = p.Rgba[o + 1] / 255f; b = p.Rgba[o + 2] / 255f;
        }
    }
}

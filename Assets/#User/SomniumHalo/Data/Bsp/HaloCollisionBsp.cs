using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// One collision BSP (ModelCollisionGeometryBSP: structure BSPs have one, collision models one per node) read as polygons:
    /// each surface is the ring of its edges (edge.left surface == s: take start vertex, go forward; else end vertex, go reverse),
    /// with its plane (bit 31 = flipped), flags (1 two sided, 2 invisible, 4 climbable, 8 breakable) and material index.
    /// Verified on all 19 MP maps: every ring closes and runs counter-clockwise about its plane normal (right-handed Halo space).
    /// Halo space and units. Unity-free.
    /// </summary>
    public sealed class HaloCollisionBsp
    {
        public const int TwoSided = 1, Invisible = 2, Climbable = 4, Breakable = 8;

        public sealed class Surface
        {
            public int Plane; public bool PlaneFlipped; public int Flags; public int Material; public int Breakable;
            /// <summary>Indices into <see cref="Vertices"/>, counter-clockwise about the plane normal.</summary>
            public int[] Ring;
        }

        /// <summary>xyz per vertex.</summary>
        public readonly float[] Vertices;
        /// <summary>(nx, ny, nz, d) per plane.</summary>
        public readonly float[] Planes;
        public readonly List<Surface> Surfaces = new List<Surface>();
        public int BadSurfaces;
        /// <summary>bsp3d nodes: (plane, back child, front child) each; a child with bit 31 set is a leaf (index in the low bits),
        /// -1 (all bits) is outside the BSP (solid / void).</summary>
        public readonly int[] Nodes;

        public HaloCollisionBsp(TagReader cb)
        {
            HaloMap map = cb.Map;
            int nPlanes = Math.Max(0, cb.Count(12));
            Planes = new float[nPlanes * 4];
            int i = 0;
            foreach (var p in cb.Block(12, 16)) { for (int k = 0; k < 4; k++) Planes[i * 4 + k] = p.F32(4 * k); i++; }
            int nNodes = Math.Max(0, cb.Count(0));
            Nodes = new int[nNodes * 3];
            i = 0;
            foreach (var n in cb.Block(0, 12)) { Nodes[i * 3] = n.I32(0); Nodes[i * 3 + 1] = n.I32(4); Nodes[i * 3 + 2] = n.I32(8); i++; }
            int nVerts = Math.Max(0, cb.Count(84));
            Vertices = new float[nVerts * 3];
            i = 0;
            foreach (var v in cb.Block(84, 16)) { Vertices[i * 3] = v.F32(0); Vertices[i * 3 + 1] = v.F32(4); Vertices[i * 3 + 2] = v.F32(8); i++; }
            int nEdges = cb.Count(72);
            if (nEdges <= 0) return;
            int edgeAt = cb.Off(cb.U32(76));
            var ring = new List<int>(16);
            int si = 0;
            foreach (var s in cb.Block(60, 12))
            {
                uint plane = s.U32(0);
                var cs = new Surface
                {
                    Plane = (int)(plane & 0x7FFFFFFF), PlaneFlipped = (plane & 0x80000000) != 0,
                    Flags = s.U8(8), Breakable = s.I8(9), Material = s.I16(10),
                };
                ring.Clear();
                int first = (int)s.U32(4), e = first, guard = 0;
                bool bad = false;
                do
                {
                    if (e < 0 || e >= nEdges) { bad = true; break; }
                    int o = edgeAt + e * 24;
                    int start = map.I32(o), end = map.I32(o + 4), fwd = map.I32(o + 8), rev = map.I32(o + 12), left = map.I32(o + 16);
                    if (left == si) { ring.Add(start); e = fwd; }
                    else { ring.Add(end); e = rev; }
                    if (++guard > 256) { bad = true; break; }
                } while (e != first);
                bool ok = !bad && ring.Count >= 3;
                if (ok) foreach (int v in ring) if (v < 0 || v >= nVerts) ok = false;
                if (ok) { cs.Ring = ring.ToArray(); Surfaces.Add(cs); }
                else BadSurfaces++;
                si++;
            }
        }

        /// <summary>The leaf a point (Halo space) falls in: >= 0 a leaf index, -1 outside the BSP (or no nodes: -2).</summary>
        public int Leaf(float x, float y, float z)
        {
            if (Nodes.Length == 0) return -2;
            int node = 0, guard = 0;
            while (guard++ < 4096)
            {
                int at = node * 3;
                if (at + 2 >= Nodes.Length) return -2;
                int p = (Nodes[at] & 0x7FFFFFFF) * 4;
                float side = p + 3 < Planes.Length ? Planes[p] * x + Planes[p + 1] * y + Planes[p + 2] * z - Planes[p + 3] : 0f;
                int child = side >= 0f ? Nodes[at + 2] : Nodes[at + 1];
                if (child == -1) return -1;
                if (child < 0) return child & 0x7FFFFFFF;
                node = child;
            }
            return -2;
        }

        /// <summary>The surface a point lies on (within <paramref name="maxDistance"/> of its plane and inside its ring seen along the normal):
        /// its plane normal. Linear scan (build-time use: decals).</summary>
        public bool SurfaceAt(float x, float y, float z, float maxDistance, out float nx, out float ny, out float nz)
        {
            nx = 0f; ny = 0f; nz = 1f;
            float best = maxDistance;
            bool found = false;
            foreach (var s in Surfaces)
            {
                float a, b, c, d;
                PlaneOf(s, out a, out b, out c, out d);
                float dist = a * x + b * y + c * z - d;
                if (Math.Abs(dist) > best) continue;
                if (!InsideRing(s, x - a * dist, y - b * dist, z - c * dist, a, b, c)) continue;
                best = Math.Abs(dist); nx = a; ny = b; nz = c; found = true;
            }
            return found;
        }

        bool InsideRing(Surface s, float x, float y, float z, float a, float b, float c)
        {
            int n = s.Ring.Length;
            for (int k = 0; k < n; k++)
            {
                int p = s.Ring[k] * 3, q = s.Ring[(k + 1) % n] * 3;
                float ex = Vertices[q] - Vertices[p], ey = Vertices[q + 1] - Vertices[p + 1], ez = Vertices[q + 2] - Vertices[p + 2];
                float wx = x - Vertices[p], wy = y - Vertices[p + 1], wz = z - Vertices[p + 2];
                // (e x w) . n >= 0 for every edge of a counter-clockwise ring
                float cx = ey * wz - ez * wy, cy = ez * wx - ex * wz, cz = ex * wy - ey * wx;
                if (cx * a + cy * b + cz * c < -1e-4f) return false;
            }
            return true;
        }

        /// <summary>The plane a surface faces (flip bit applied).</summary>
        public void PlaneOf(Surface s, out float nx, out float ny, out float nz, out float d)
        {
            int p = s.Plane * 4;
            if (p + 3 >= Planes.Length) { nx = 0; ny = 0; nz = 1; d = 0; return; }
            float f = s.PlaneFlipped ? -1f : 1f;
            nx = Planes[p] * f; ny = Planes[p + 1] * f; nz = Planes[p + 2] * f; d = Planes[p + 3] * f;
        }
    }
}

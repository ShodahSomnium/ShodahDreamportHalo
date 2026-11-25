using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Pathfinding data of a structure BSP for the AI module, Unity-free, Halo space and units. Halo CE's cache keeps no separate nav
    /// mesh: its AI walks the collision BSP's surfaces (the BSP's "pathfinding surfaces" block is one byte per collision surface, kept
    /// here as <see cref="Surface.PathByte"/>; the "pathfinding edges" block is empty in the retail maps). So the walkable graph is: every
    /// collision surface whose plane faces up enough (<see cref="Surface.Walkable"/>, Halo's 0.7 ~ 45 degrees by default) and is not
    /// invisible-only clip, linked to its neighbours across the shared collision edges (edge left / right surface).
    /// Build once per BSP (tens of ms on a worker): <c>new HaloPathData(bsp)</c>; the World module caches one per built BSP
    /// (HaloWorld.PathData(i)).
    /// </summary>
    public sealed class HaloPathData
    {
        public sealed class Surface
        {
            public int Index;
            /// <summary>Polygon vertices (x, y, z each), counter-clockwise about the normal.</summary>
            public float[] Points;
            public float Nx, Ny, Nz, D;
            public float Cx, Cy, Cz;
            public int MaterialType, Flags;
            public byte PathByte;
            public bool Walkable;
            /// <summary>Neighbouring surfaces across each edge of the ring (-1 = none / open edge), same order as the ring edges.</summary>
            public int[] Neighbours;
        }

        /// <summary>Minimum plane normal z for a walkable surface (ours: Halo's bipeds default to a 45 degree maximum slope).</summary>
        public const float WalkableNormalZ = 0.7f;

        public readonly List<Surface> Surfaces = new List<Surface>();
        public int WalkableCount;

        public HaloPathData(HaloBsp bsp)
        {
            var c = bsp.Collision;
            if (c == null) return;
            // edges: rebuild (start, end) -> surfaces map from the rings (the ring order gives each edge once per side)
            var edgeOwner = new Dictionary<long, int>();
            for (int si = 0; si < c.Surfaces.Count; si++)
            {
                var r = c.Surfaces[si].Ring;
                for (int k = 0; k < r.Length; k++) edgeOwner[Key(r[k], r[(k + 1) % r.Length])] = si;
            }
            for (int si = 0; si < c.Surfaces.Count; si++)
            {
                var s = c.Surfaces[si];
                var ps = new Surface { Index = si, Flags = s.Flags, MaterialType = bsp.MaterialType(s) };
                c.PlaneOf(s, out ps.Nx, out ps.Ny, out ps.Nz, out ps.D);
                ps.Points = new float[s.Ring.Length * 3];
                for (int k = 0; k < s.Ring.Length; k++)
                {
                    int v = s.Ring[k] * 3;
                    ps.Points[k * 3] = c.Vertices[v]; ps.Points[k * 3 + 1] = c.Vertices[v + 1]; ps.Points[k * 3 + 2] = c.Vertices[v + 2];
                    ps.Cx += c.Vertices[v]; ps.Cy += c.Vertices[v + 1]; ps.Cz += c.Vertices[v + 2];
                }
                ps.Cx /= s.Ring.Length; ps.Cy /= s.Ring.Length; ps.Cz /= s.Ring.Length;
                ps.Neighbours = new int[s.Ring.Length];
                for (int k = 0; k < s.Ring.Length; k++)
                {
                    int o;
                    ps.Neighbours[k] = edgeOwner.TryGetValue(Key(s.Ring[(k + 1) % s.Ring.Length], s.Ring[k]), out o) && o != si ? o : -1;
                }
                // surface indices of the collision BSP may skip bad rings: keep the byte by position when counts match
                ps.PathByte = si < bsp.PathfindingSurfaces.Length ? bsp.PathfindingSurfaces[si] : (byte)0;
                ps.Walkable = ps.Nz >= WalkableNormalZ && (s.Flags & HaloCollisionBsp.Invisible) == 0;
                if (ps.Walkable) WalkableCount++;
                Surfaces.Add(ps);
            }
        }

        static long Key(int a, int b) { return ((long)a << 32) | (uint)b; }

        /// <summary>The walkable surface under a point (highest one at or below z + 0.1, within <paramref name="maxDrop"/>), -1 none.
        /// Linear scan: callers cache the result per actor (use a grid of your own for many queries).</summary>
        public int SurfaceUnder(float x, float y, float z, float maxDrop = 2f)
        {
            int best = -1; float bestZ = float.NegativeInfinity;
            for (int i = 0; i < Surfaces.Count; i++)
            {
                var s = Surfaces[i];
                if (!s.Walkable || Math.Abs(s.Nz) < 1e-4f) continue;
                if (!Inside(s, x, y)) continue;
                float sz = (s.D - s.Nx * x - s.Ny * y) / s.Nz;
                if (sz > z + 0.1f || sz < z - maxDrop || sz <= bestZ) continue;
                bestZ = sz; best = i;
            }
            return best;
        }

        static bool Inside(Surface s, float x, float y)
        {
            int n = s.Points.Length / 3;
            for (int k = 0; k < n; k++)
            {
                float ax = s.Points[k * 3], ay = s.Points[k * 3 + 1];
                float bx = s.Points[((k + 1) % n) * 3], by = s.Points[((k + 1) % n) * 3 + 1];
                if ((bx - ax) * (y - ay) - (by - ay) * (x - ax) < -1e-5f) return false;   // counter-clockwise seen from above
            }
            return true;
        }
    }
}

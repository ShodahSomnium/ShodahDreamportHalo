using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Collision BSP polygons (Data/Bsp/HaloCollisionBsp) as Unity meshes for MeshColliders, grouped by material type and the
    /// climbable flag (one collider per group, so a hit's collider says the material: <see cref="HaloWorld.SurfaceMaterial"/>).
    /// Halo rings run counter-clockwise about their plane normal; the axis swap mirrors that, so the ring is reversed to keep the
    /// Halo front side as Unity's front face (one-sided surfaces block from the front as in the game; two-sided ones get both
    /// windings). Invisible surfaces (player clip, map boundaries) collide like any other. <see cref="Prepare"/> runs on any thread.
    /// </summary>
    public static class HaloCollision
    {
        public sealed class Piece
        {
            public int MaterialType;
            public bool Climbable, Breakable, Invisible;
            /// <summary>The BSP breakable surface index of a breakable piece (-1 none): one piece per breakable surface, so it can go.</summary>
            public int BreakableIndex = -1;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<int> Triangles = new List<int>();
            public int Surfaces;
            internal int[] Remap;
        }

        /// <summary>Groups a collision BSP into pieces (Unity space, metres, relative to the BSP's own origin).
        /// <paramref name="materialType"/> maps a surface material index to the MaterialType enum.</summary>
        public static List<Piece> Prepare(HaloCollisionBsp bsp, Func<int, int> materialType, float scale = 1f)
        {
            var pieces = new Dictionary<int, Piece>();
            var list = new List<Piece>();
            if (bsp == null) return list;
            int nVerts = bsp.Vertices.Length / 3;
            float s = HaloSpace.Unit * scale;
            foreach (var surf in bsp.Surfaces)
            {
                int mt = materialType != null ? materialType(surf.Material) : -1;
                bool climb = (surf.Flags & HaloCollisionBsp.Climbable) != 0;
                bool brk = (surf.Flags & HaloCollisionBsp.Breakable) != 0;
                bool inv = (surf.Flags & HaloCollisionBsp.Invisible) != 0;
                int bi = brk ? surf.Breakable : -1;
                int key = ((mt + 1) * 8 + (climb ? 1 : 0) + (brk ? 2 : 0) + (inv ? 4 : 0)) + (bi >= 0 ? (bi + 1) * 4096 : 0);
                Piece p;
                if (!pieces.TryGetValue(key, out p))
                {
                    p = new Piece { MaterialType = mt, Climbable = climb, Breakable = brk, Invisible = inv, BreakableIndex = bi, Remap = new int[nVerts] };
                    for (int i = 0; i < nVerts; i++) p.Remap[i] = -1;
                    pieces[key] = p; list.Add(p);
                }
                int n = surf.Ring.Length;
                var idx = new int[n];
                for (int k = 0; k < n; k++)
                {
                    int v = surf.Ring[k];
                    if (p.Remap[v] < 0)
                    {
                        p.Remap[v] = p.Vertices.Count;
                        p.Vertices.Add(new Vector3(bsp.Vertices[v * 3] * s, bsp.Vertices[v * 3 + 2] * s, bsp.Vertices[v * 3 + 1] * s));
                    }
                    idx[k] = p.Remap[v];
                }
                bool two = (surf.Flags & HaloCollisionBsp.TwoSided) != 0;
                // fan, reversed (Halo ring order mirrored by the axis swap)
                for (int k = 1; k + 1 < n; k++)
                {
                    p.Triangles.Add(idx[0]); p.Triangles.Add(idx[k + 1]); p.Triangles.Add(idx[k]);
                    if (two) { p.Triangles.Add(idx[0]); p.Triangles.Add(idx[k]); p.Triangles.Add(idx[k + 1]); }
                }
                p.Surfaces++;
            }
            foreach (var p in list) p.Remap = null;
            return list;
        }

        /// <summary>Main thread: a mesh for a piece (no renderer).</summary>
        public static Mesh ToMesh(Piece p, string name)
        {
            var m = new Mesh { name = name };
            if (p.Vertices.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(p.Vertices);
            m.SetTriangles(p.Triangles, 0, true);
            return m;
        }

        /// <summary>Main thread: MeshColliders for a collision model (coll) on a built model's nodes (node-local BSPs).
        /// Returns the colliders made. Used for scenery and anyone who wants an object's own collision.</summary>
        public static List<MeshCollider> AddModelColliders(HaloTag collisionTag, HaloModelBuilder.Built built, Transform fallbackRoot, int layer, float scale = 1f)
        {
            var made = new List<MeshCollider>();
            if (collisionTag == null) return made;
            HaloCollisionModel cm;
            try { cm = new HaloCollisionModel(collisionTag); }
            catch (Exception e) { HaloLog.Warn("World: collision model " + collisionTag + " failed: " + e.Message); return made; }
            for (int ni = 0; ni < cm.Nodes.Count; ni++)
            {
                var node = cm.Nodes[ni];
                Transform at = null;
                if (built != null && built.Model != null)
                {
                    int mi = built.Model.NodeIndex(node.Name);
                    if (mi < 0 && ni < built.Nodes.Length) mi = ni;
                    if (mi >= 0 && mi < built.Nodes.Length) at = built.Nodes[mi];
                }
                if (at == null) at = built != null ? built.Root.transform : fallbackRoot;
                foreach (var bsp in node.Bsps)
                {
                    foreach (var piece in Prepare(bsp, cm.MaterialType, scale))
                    {
                        if (piece.Triangles.Count < 3) continue;
                        var go = new GameObject("collision " + node.Name + " " + piece.MaterialType);
                        go.layer = layer;
                        go.transform.SetParent(at, false);
                        var mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = ToMesh(piece, go.name);
                        HaloWorld.Register(mc, piece.MaterialType, piece.Climbable);
                        if (piece.Climbable) HaloWorld.MakeClimbable(go);
                        made.Add(mc);
                    }
                }
            }
            return made;
        }
    }
}

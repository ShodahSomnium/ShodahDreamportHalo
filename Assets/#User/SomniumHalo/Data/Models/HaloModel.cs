using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A gbxmodel tag (mod2): nodes (skeleton), markers, regions with permutations (one geometry index per level of detail),
    /// geometries made of parts (one shader each), shaders. Render vertices and triangle strips of a cache file live in the map's
    /// model data block (header: model data offset, vertex bytes = index start): part +100 = vertex offset into it, part +88 = vertex
    /// count, part +76 = index offset after the vertices, part +72 = index count - 2 (a triangle strip, u16). Vertices are 68 bytes
    /// (ModelVertexUncompressed: position, normal, binormal, tangent, uv, node0/1, weight0/1) in the model's rest space (Halo units).
    /// Layouts: DevTools/reference/layouts.txt. Unity-free.
    /// </summary>
    public sealed class HaloModel
    {
        public sealed class Node
        {
            public string Name; public int Parent, FirstChild, NextSibling;
            public HVec3 Translation; public float Qi, Qj, Qk, Qw; public float DistanceFromParent;
        }

        public sealed class MarkerInstance { public int Region, Permutation, Node; public HVec3 Translation; public float Qi, Qj, Qk, Qw; }
        public sealed class Marker { public string Name; public readonly List<MarkerInstance> Instances = new List<MarkerInstance>(); }

        public sealed class Permutation
        {
            public string Name; public uint Flags;
            /// <summary>Geometry index per level of detail: 0 super low .. 4 super high (-1 = none).</summary>
            public readonly int[] Lod = new int[5];
        }

        public sealed class Region { public string Name; public readonly List<Permutation> Permutations = new List<Permutation>(); }

        public sealed class Part
        {
            public int Shader, VertexCount, IndexCount;
            public int VertexOffset, IndexOffset;
            public HVec3 Centroid;
            public int[] LocalNodes;
        }

        public sealed class Geometry { public readonly List<Part> Parts = new List<Part>(); }

        public sealed class ShaderRef { public HaloTag Shader; public int Permutation; }

        public readonly HaloTag Tag;
        public readonly uint Flags;
        public readonly float UScale, VScale;
        public readonly float[] LodCutoff = new float[5];
        public readonly List<Node> Nodes = new List<Node>();
        public readonly List<Marker> Markers = new List<Marker>();
        public readonly List<Region> Regions = new List<Region>();
        public readonly List<Geometry> Geometries = new List<Geometry>();
        public readonly List<ShaderRef> Shaders = new List<ShaderRef>();

        public HaloModel(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            Flags = r.U32(0);
            // cutoffs are stored super high .. super low at +8..+24
            for (int i = 0; i < 5; i++) LodCutoff[4 - i] = r.F32(8 + 4 * i);
            UScale = r.F32(48); VScale = r.F32(52);
            foreach (var m in r.Block(172, 64))
            {
                var mk = new Marker { Name = m.Map.Str(m.At, 32) };
                foreach (var x in m.Block(52, 32))
                    mk.Instances.Add(new MarkerInstance { Region = x.U8(0), Permutation = x.U8(1), Node = x.U8(2), Translation = x.Vec3(4), Qi = x.F32(16), Qj = x.F32(20), Qk = x.F32(24), Qw = x.F32(28) });
                Markers.Add(mk);
            }
            foreach (var n in r.Block(184, 156))
                Nodes.Add(new Node
                {
                    Name = n.Map.Str(n.At, 32), NextSibling = n.I16(32), FirstChild = n.I16(34), Parent = n.I16(36),
                    Translation = n.Vec3(40), Qi = n.F32(52), Qj = n.F32(56), Qk = n.F32(60), Qw = n.F32(64), DistanceFromParent = n.F32(68),
                });
            foreach (var g in r.Block(196, 76))
            {
                var reg = new Region { Name = g.Map.Str(g.At, 32) };
                foreach (var p in g.Block(64, 88))
                {
                    var pm = new Permutation { Name = p.Map.Str(p.At, 32), Flags = p.U32(32) };
                    for (int i = 0; i < 5; i++) pm.Lod[i] = p.I16(64 + 2 * i);
                    reg.Permutations.Add(pm);
                }
                Regions.Add(reg);
            }
            foreach (var g in r.Block(208, 48))
            {
                var geo = new Geometry();
                foreach (var p in g.Block(36, 132))
                {
                    var part = new Part
                    {
                        Shader = p.I16(4), Centroid = p.Vec3(20),
                        IndexCount = p.I32(72) + 2, IndexOffset = p.I32(80), VertexCount = p.I32(88), VertexOffset = p.I32(100),
                    };
                    int ln = p.U8(107);
                    if (ln > 0 && ln <= 22) { part.LocalNodes = new int[ln]; for (int i = 0; i < ln; i++) part.LocalNodes[i] = p.U8(108 + i); }
                    geo.Parts.Add(part);
                }
                Geometries.Add(geo);
            }
            foreach (var s in r.Block(220, 32)) Shaders.Add(new ShaderRef { Shader = s.Ref(0), Permutation = s.I16(16) });
        }

        public int NodeIndex(string name)
        {
            for (int i = 0; i < Nodes.Count; i++) if (string.Equals(Nodes[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public Marker FindMarker(string name)
        {
            foreach (var m in Markers) if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        /// <summary>One part's vertices, decoded (Halo space, units).</summary>
        public sealed class PartData
        {
            public HVec3[] Position, Normal, Binormal, Tangent;
            public float[] U, V;
            public int[] Node0, Node1; public float[] Weight0, Weight1;
            /// <summary>Triangle list (strip expanded, degenerate triangles dropped), indices into the arrays above, Halo winding.</summary>
            public int[] Triangles;
        }

        public PartData Read(Part part)
        {
            var map = Tag.Map;
            byte[] d = map.Data;
            int vb = map.ModelDataOffset + part.VertexOffset;
            int n = part.VertexCount;
            var pd = new PartData
            {
                Position = new HVec3[n], Normal = new HVec3[n], Binormal = new HVec3[n], Tangent = new HVec3[n],
                U = new float[n], V = new float[n], Node0 = new int[n], Node1 = new int[n], Weight0 = new float[n], Weight1 = new float[n],
            };
            float us = UScale != 0 ? UScale : 1f, vs = VScale != 0 ? VScale : 1f;
            for (int i = 0; i < n; i++)
            {
                int o = vb + i * 68;
                pd.Position[i] = new HVec3(map.F32(o), map.F32(o + 4), map.F32(o + 8));
                pd.Normal[i] = new HVec3(map.F32(o + 12), map.F32(o + 16), map.F32(o + 20));
                pd.Binormal[i] = new HVec3(map.F32(o + 24), map.F32(o + 28), map.F32(o + 32));
                pd.Tangent[i] = new HVec3(map.F32(o + 36), map.F32(o + 40), map.F32(o + 44));
                pd.U[i] = map.F32(o + 48) * us; pd.V[i] = map.F32(o + 52) * vs;
                int n0 = map.I16(o + 56), n1 = map.I16(o + 58);
                if (part.LocalNodes != null)
                {
                    if (n0 >= 0 && n0 < part.LocalNodes.Length) n0 = part.LocalNodes[n0];
                    if (n1 >= 0 && n1 < part.LocalNodes.Length) n1 = part.LocalNodes[n1];
                }
                pd.Node0[i] = n0; pd.Node1[i] = n1;
                pd.Weight0[i] = map.F32(o + 60); pd.Weight1[i] = map.F32(o + 64);
            }
            int ib = map.ModelDataOffset + map.ModelIndexOffset + part.IndexOffset;
            pd.Triangles = Strip(d, ib, part.IndexCount, n);
            return pd;
        }

        /// <summary>A u16 triangle strip as a triangle list (alternate winding fixed, degenerates and 0xFFFF dropped).</summary>
        public static int[] Strip(byte[] d, int at, int count, int vertexCount)
        {
            var list = new List<int>(count * 3);
            int a = -1, b = -1;
            int parity = 0;
            for (int i = 0; i < count; i++)
            {
                int c = d[at + i * 2] | d[at + i * 2 + 1] << 8;
                if (c == 0xFFFF) { a = b = -1; parity = 0; continue; }
                if (a >= 0 && b >= 0)
                {
                    if (a != b && b != c && a != c && a < vertexCount && b < vertexCount && c < vertexCount)
                    {
                        if ((parity & 1) == 0) { list.Add(a); list.Add(b); list.Add(c); }
                        else { list.Add(a); list.Add(c); list.Add(b); }
                    }
                    parity++;
                }
                a = b; b = c;
            }
            return list.ToArray();
        }
    }
}

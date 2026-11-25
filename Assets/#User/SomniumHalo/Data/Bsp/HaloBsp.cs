using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A structure BSP (sbsp) read for drawing and colliding: render sections (one per lightmap x material: shader, lightmap page,
    /// vertices, triangles), the collision BSP as polygons (with material and flags), fog planes / regions / palette, clusters (sky,
    /// fog) and the BSP's default lighting. PC cache layout (verified on Blood Gulch: uncompressed size = rendered x 56 + lightmap x 20):
    /// material +216 "uncompressed vertices" data = rendered vertices (position, normal, binormal, tangent, uv; 56 B) followed by the
    /// lightmap vertices (incident light direction, lightmap uv; 20 B); material +20/+24 = first triangle / count in the BSP's
    /// surfaces block (u16 x3, indices local to the material's vertices). Halo space and units throughout. Unity-free.
    /// </summary>
    public sealed class HaloBsp
    {
        public sealed class Section
        {
            public HaloTag Shader;
            /// <summary>Image index in <see cref="LightmapBitmap"/> (-1 = no lightmap: sky, transparent, self lit).</summary>
            public int Lightmap;
            public int LightmapBlock;
            public ushort Flags;
            public int VertexCount;
            /// <summary>xyz per vertex.</summary>
            public float[] Position, Normal, Binormal, Tangent;
            /// <summary>uv per vertex (texture), and lightmap uv (empty when no lightmap vertices).</summary>
            public float[] Uv, LightmapUv;
            /// <summary>Incident light direction per vertex (the lightmap vertex "normal"; xyz), null without lightmap vertices.</summary>
            public float[] Incident;
            /// <summary>Triangle list, local vertex indices, Halo winding.</summary>
            public int[] Triangles;
            public HVec3 Ambient, Light0Color, Light0Dir, Light1Color, Light1Dir, ShadowDir, ShadowColor;
            public int DistantLights;
            /// <summary>Breakable surface index (+172: into <see cref="BreakableSurfaces"/>), -1 = not breakable.</summary>
            public int Breakable = -1;
            public float ReflectionTintA, ReflectionTintR, ReflectionTintG, ReflectionTintB;
            public bool FogPlane => (Flags & 2) != 0;
        }

        public sealed class CollisionMaterial { public HaloTag Shader; public int Type; }

        public sealed class FogPlane { public int FrontRegion; public int MaterialType; public float Nx, Ny, Nz, D; public HVec3[] Vertices; }
        public sealed class FogRegion { public int Fog, Weather; }
        public sealed class FogEntry { public string Name; public HaloTag Fog; public string ScaleFunction; }
        public sealed class Cluster { public int Sky, Fog, BackgroundSound, SoundEnvironment, Weather; public int[] Surfaces; }

        public readonly HaloTag Tag;
        public readonly HaloTag LightmapBitmap;
        public readonly float VehicleFloor, VehicleCeiling;
        public readonly HVec3 DefaultAmbient, DefaultLight0Color, DefaultLight0Dir, DefaultLight1Color, DefaultLight1Dir, DefaultShadowDir, DefaultShadowColor;
        public readonly float BoundsMinX, BoundsMaxX, BoundsMinY, BoundsMaxY, BoundsMinZ, BoundsMaxZ;
        public readonly List<Section> Sections = new List<Section>();
        public readonly List<CollisionMaterial> CollisionMaterials = new List<CollisionMaterial>();
        /// <summary>The collision BSP (null when the BSP has none).</summary>
        public HaloCollisionBsp Collision;
        public readonly List<FogPlane> FogPlanes = new List<FogPlane>();
        public readonly List<FogRegion> FogRegions = new List<FogRegion>();
        public readonly List<FogEntry> FogPalette = new List<FogEntry>();
        public readonly List<Cluster> Clusters = new List<Cluster>();
        /// <summary>Cluster index per structure leaf (the collision BSP's leaves are the same leaves).</summary>
        public int[] LeafClusters = new int[0];

        public sealed class LensFlareMarker { public HVec3 Position; public float Dx, Dy, Dz; public int Flare; }
        public sealed class RuntimeDecal { public HVec3 Position; public int Type; public int Yaw, Pitch; }
        public sealed class Weather { public string Name; public HaloTag ParticleSystem, Wind; public HVec3 WindDirection; public float WindMagnitude; }
        public sealed class WeatherPolyhedron { public HVec3 Centre; public float Radius; public float[] Planes; }
        public sealed class Marker { public string Name; public float Qi, Qj, Qk, Qw; public HVec3 Position; }

        /// <summary>Lens flare palette (lens_flare tags) and the markers that place them (direction from int8 components / 127).</summary>
        public readonly List<HaloTag> LensFlares = new List<HaloTag>();
        public readonly List<LensFlareMarker> LensFlareMarkers = new List<LensFlareMarker>();
        /// <summary>Decals of the BSP (type = index into the scenario's decal palette, +948; yaw / pitch int8 angles).</summary>
        public readonly List<RuntimeDecal> Decals = new List<RuntimeDecal>();
        public sealed class BreakableSurface { public HVec3 Centroid; public float Radius; public int CollisionSurface; }
        /// <summary>Breakable surfaces (+364): glass panes etc. Render sections and collision surfaces name their index.</summary>
        public readonly List<BreakableSurface> BreakableSurfaces = new List<BreakableSurface>();
        public readonly List<Weather> WeatherPalette = new List<Weather>();
        /// <summary>Weather polyhedra: volumes where the weather does not fall (planes: nx, ny, nz, d each).</summary>
        public readonly List<WeatherPolyhedron> WeatherPolyhedra = new List<WeatherPolyhedron>();
        public readonly List<Marker> Markers = new List<Marker>();
        /// <summary>The per-collision-surface pathfinding byte (+484, one per collision surface in the cache).</summary>
        public byte[] PathfindingSurfaces = new byte[0];
        /// <summary>Detail object data blocks (raw readers kept for the detail object builder).</summary>
        public TagReader DetailObjects;
        public bool HasDetailObjects;
        public readonly List<string> Problems = new List<string>();

        public int TriangleCount { get { int n = 0; foreach (var s in Sections) n += s.Triangles.Length / 3; return n; } }
        public int VertexCount { get { int n = 0; foreach (var s in Sections) n += s.VertexCount; return n; } }

        public HaloBsp(HaloScenario scenario, int index)
        {
            var bsp = scenario.Bsps[index];
            Tag = bsp.Tag;
            TagReader r = scenario.BspRoot(index);
            HaloMap map = scenario.Tag.Map;
            LightmapBitmap = r.Ref(0);
            VehicleFloor = r.F32(16); VehicleCeiling = r.F32(20);
            DefaultAmbient = r.Vec3(44);
            DefaultLight0Color = r.Vec3(60); DefaultLight0Dir = r.Vec3(72);
            DefaultLight1Color = r.Vec3(84); DefaultLight1Dir = r.Vec3(96);
            DefaultShadowDir = r.Vec3(136); DefaultShadowColor = r.Vec3(148);
            BoundsMinX = r.F32(200); BoundsMaxX = r.F32(204);
            BoundsMinY = r.F32(208); BoundsMaxY = r.F32(212);
            BoundsMinZ = r.F32(216); BoundsMaxZ = r.F32(220);

            // surfaces: u16 triangles
            int surfCount = r.Count(248);
            int surfAt = surfCount > 0 ? r.Off(r.U32(252)) : 0;
            byte[] d = map.Data;

            int lmIndex = 0;
            foreach (var lm in r.Block(260, 32))
            {
                int bitmap = lm.I16(0);
                foreach (var mt in lm.Block(20, 256))
                {
                    var s = new Section
                    {
                        Shader = mt.Ref(0), Flags = mt.U16(18), Lightmap = bitmap, LightmapBlock = lmIndex, Breakable = mt.I16(172),
                        Ambient = mt.Vec3(40), DistantLights = mt.I16(52),
                        Light0Color = mt.Vec3(56), Light0Dir = mt.Vec3(68), Light1Color = mt.Vec3(80), Light1Dir = mt.Vec3(92),
                        ReflectionTintA = mt.F32(116), ReflectionTintR = mt.F32(120), ReflectionTintG = mt.F32(124), ReflectionTintB = mt.F32(128),
                        ShadowDir = mt.Vec3(132), ShadowColor = mt.Vec3(144),
                    };
                    int first = mt.I32(20), count = mt.I32(24);
                    int nv = (int)mt.U32(180), nl = (int)mt.U32(200);
                    int size = mt.DataSize(216);
                    if (nv <= 0 || count <= 0 || first < 0 || first + count > surfCount) { if (count > 0) Problems.Add("section " + s.Shader + ": bad range"); continue; }
                    if (size < nv * 56 + nl * 20) { Problems.Add("section " + s.Shader + ": vertex data " + size + " < " + (nv * 56 + nl * 20)); continue; }
                    int vb = mt.DataOffset(216);
                    s.VertexCount = nv;
                    s.Position = new float[nv * 3]; s.Normal = new float[nv * 3]; s.Binormal = new float[nv * 3]; s.Tangent = new float[nv * 3];
                    s.Uv = new float[nv * 2];
                    for (int i = 0; i < nv; i++)
                    {
                        int o = vb + i * 56;
                        for (int k = 0; k < 3; k++)
                        {
                            s.Position[i * 3 + k] = BitConverter.ToSingle(d, o + 4 * k);
                            s.Normal[i * 3 + k] = BitConverter.ToSingle(d, o + 12 + 4 * k);
                            s.Binormal[i * 3 + k] = BitConverter.ToSingle(d, o + 24 + 4 * k);
                            s.Tangent[i * 3 + k] = BitConverter.ToSingle(d, o + 36 + 4 * k);
                        }
                        s.Uv[i * 2] = BitConverter.ToSingle(d, o + 48);
                        s.Uv[i * 2 + 1] = BitConverter.ToSingle(d, o + 52);
                    }
                    if (nl == nv && nl > 0)
                    {
                        s.Incident = new float[nv * 3]; s.LightmapUv = new float[nv * 2];
                        int lb = vb + nv * 56;
                        for (int i = 0; i < nv; i++)
                        {
                            int o = lb + i * 20;
                            for (int k = 0; k < 3; k++) s.Incident[i * 3 + k] = BitConverter.ToSingle(d, o + 4 * k);
                            s.LightmapUv[i * 2] = BitConverter.ToSingle(d, o + 12);
                            s.LightmapUv[i * 2 + 1] = BitConverter.ToSingle(d, o + 16);
                        }
                    }
                    var tris = new List<int>(count * 3);
                    for (int t = 0; t < count; t++)
                    {
                        int o = surfAt + (first + t) * 6;
                        int a = d[o] | d[o + 1] << 8, b = d[o + 2] | d[o + 3] << 8, c = d[o + 4] | d[o + 5] << 8;
                        if (a >= nv || b >= nv || c >= nv) { Problems.Add("section " + s.Shader + ": index out of range"); continue; }
                        if (a == b || b == c || a == c) continue;
                        tris.Add(a); tris.Add(b); tris.Add(c);
                    }
                    s.Triangles = tris.ToArray();
                    Sections.Add(s);
                }
                lmIndex++;
            }

            foreach (var cm in r.Block(164, 20)) CollisionMaterials.Add(new CollisionMaterial { Shader = cm.Ref(0), Type = cm.U16(18) });
            foreach (var cb in r.Block(176, 96)) { Collision = new HaloCollisionBsp(cb); if (Collision.BadSurfaces > 0) Problems.Add(Collision.BadSurfaces + " bad collision surfaces"); break; }

            foreach (var fp in r.Block(376, 32))
            {
                var f = new FogPlane { FrontRegion = fp.I16(0), MaterialType = fp.I16(2), Nx = fp.F32(4), Ny = fp.F32(8), Nz = fp.F32(12), D = fp.F32(16) };
                var vs = new List<HVec3>();
                foreach (var v in fp.Block(20, 12)) vs.Add(v.Vec3(0));
                f.Vertices = vs.ToArray();
                FogPlanes.Add(f);
            }
            foreach (var fr in r.Block(388, 40)) FogRegions.Add(new FogRegion { Fog = fr.I16(36), Weather = fr.I16(38) });
            foreach (var fp in r.Block(400, 136)) FogPalette.Add(new FogEntry { Name = fp.Str32(0), Fog = fp.Ref(32), ScaleFunction = fp.Str32(52) });
            foreach (var lf in r.Block(284, 16)) LensFlares.Add(lf.Ref(0));
            foreach (var lm in r.Block(296, 16))
                LensFlareMarkers.Add(new LensFlareMarker { Position = lm.Vec3(0), Dx = lm.I8(12) / 127f, Dy = lm.I8(13) / 127f, Dz = lm.I8(14) / 127f, Flare = lm.I8(15) });
            foreach (var bs in r.Block(364, 48)) BreakableSurfaces.Add(new BreakableSurface { Centroid = bs.Vec3(0), Radius = bs.F32(12), CollisionSurface = bs.I32(16) });
            foreach (var dc in r.Block(600, 16)) Decals.Add(new RuntimeDecal { Position = dc.Vec3(0), Type = dc.I16(12), Yaw = dc.I8(14), Pitch = dc.I8(15) });
            foreach (var w in r.Block(436, 240))
                WeatherPalette.Add(new Weather { Name = w.Str32(0), ParticleSystem = w.Ref(32), Wind = w.Ref(128), WindDirection = w.Vec3(144), WindMagnitude = w.F32(156) });
            foreach (var wp in r.Block(448, 32))
            {
                var poly = new WeatherPolyhedron { Centre = wp.Vec3(0), Radius = wp.F32(12) };
                var pl = new List<float>();
                foreach (var p in wp.Block(20, 16)) { pl.Add(p.F32(0)); pl.Add(p.F32(4)); pl.Add(p.F32(8)); pl.Add(p.F32(12)); }
                poly.Planes = pl.ToArray();
                WeatherPolyhedra.Add(poly);
            }
            foreach (var mk in r.Block(576, 60)) Markers.Add(new Marker { Name = mk.Str32(0), Qi = mk.F32(32), Qj = mk.F32(36), Qk = mk.F32(40), Qw = mk.F32(44), Position = mk.Vec3(48) });
            int nPath = Math.Max(0, r.Count(484));
            PathfindingSurfaces = new byte[nPath];
            if (nPath > 0) Buffer.BlockCopy(map.Data, r.Off(r.U32(488)), PathfindingSurfaces, 0, nPath);
            foreach (var dd in r.Block(588, 64)) { DetailObjects = dd; HasDetailObjects = dd.Count(12) > 0; break; }
            int nLeaves = Math.Max(0, r.Count(224));
            LeafClusters = new int[nLeaves];
            int li = 0;
            foreach (var lf in r.Block(224, 16)) LeafClusters[li++] = lf.I16(8);
            foreach (var c in r.Block(308, 104))
            {
                var cl = new Cluster { Sky = c.I16(0), Fog = c.I16(2), BackgroundSound = c.I16(4), SoundEnvironment = c.I16(6), Weather = c.I16(8) };
                var list = new List<int>();
                foreach (var si in c.Block(68, 4)) list.Add(si.I32(0));
                cl.Surfaces = list.ToArray();
                Clusters.Add(cl);
            }
        }

        /// <summary>The cluster a point (Halo space) is in: >= 0 a cluster, -1 outside the BSP, -2 unknown (no collision BSP).</summary>
        public int ClusterAt(float x, float y, float z)
        {
            if (Collision == null) return -2;
            int leaf = Collision.Leaf(x, y, z);
            if (leaf < 0) return leaf;
            return leaf < LeafClusters.Length ? LeafClusters[leaf] : -2;
        }

        /// <summary>The collision material type (MaterialType enum) of a collision surface, -1 = unknown.</summary>
        public int MaterialType(HaloCollisionBsp.Surface s)
        {
            return s.Material >= 0 && s.Material < CollisionMaterials.Count ? CollisionMaterials[s.Material].Type : -1;
        }
    }
}

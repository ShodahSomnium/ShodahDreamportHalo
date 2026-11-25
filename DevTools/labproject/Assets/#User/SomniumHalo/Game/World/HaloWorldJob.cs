using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The worker-thread half of <see cref="HaloWorld.Build"/>: reads the structure BSP, decodes the lightmap pages and packs them
    /// into one atlas, builds the render meshes' arrays (one combined mesh per shader, Unity space), the collision pieces, the light
    /// grid, the sky and placed-object lists, and decodes every texture the world will ask for (HaloTextures.PrepareAhead) so the main
    /// thread only uploads. No Unity object is touched here (Vector3 / Color maths only).
    /// </summary>
    public sealed class HaloWorldJob
    {
        /// <summary>One combined render mesh: every BSP triangle drawn with one shader (all lightmap pages through the atlas).</summary>
        public sealed class RenderPiece
        {
            public HaloTag Shader;
            public bool Lightmapped;
            /// <summary>BSP breakable surface index of this piece (-1 none): its own mesh, hidden when broken.</summary>
            public int Breakable = -1;
            public readonly List<Vector3> Positions, Normals, Binormals, Incident;
            public readonly List<Vector4> Tangents;
            public readonly List<Vector2> Uv, LightUv;
            public readonly List<int> Triangles;
            /// <summary>Lists sized up front (no regrowth garbage on the worker: the main thread's GC pauses with it).</summary>
            public RenderPiece(int vertices, int indices)
            {
                Positions = new List<Vector3>(vertices); Normals = new List<Vector3>(vertices); Binormals = new List<Vector3>(vertices); Incident = new List<Vector3>(vertices);
                Tangents = new List<Vector4>(vertices); Uv = new List<Vector2>(vertices); LightUv = new List<Vector2>(vertices); Triangles = new List<int>(indices);
            }
            public int Sections;
        }

        public readonly HaloMap Map;
        public readonly HaloScenario Scenario;
        public readonly int BspIndex;
        public HaloBsp Bsp;
        public HaloAtlas Atlas;
        public HaloLightGrid Light;
        /// <summary>The BSP's pathfinding data for the AI module (built on the worker).</summary>
        public HaloPathData Path;
        public readonly List<RenderPiece> Render = new List<RenderPiece>();
        public List<HaloCollision.Piece> Collision = new List<HaloCollision.Piece>();
        public HaloSky Sky;
        public int SkyIndex = -1;
        public bool Indoor;
        public volatile bool Done;
        public volatile float Progress;
        public volatile string Stage = "starting";
        public string Error;
        public long Milliseconds;
        Thread _thread;

        /// <summary>Also decode the textures of the placed objects (the first BSP of a map does it for all).</summary>
        public readonly bool IncludeObjects;

        public HaloWorldJob(HaloMap map, HaloScenario scenario, int bspIndex, bool includeObjects = true)
        {
            Map = map; Scenario = scenario; BspIndex = bspIndex; IncludeObjects = includeObjects;
        }

        /// <summary>Frees the mesh arrays once the Unity meshes exist (the BSP itself stays for queries).</summary>
        public void ReleaseArrays()
        {
            Render.Clear(); Collision = new List<HaloCollision.Piece>(); Atlas = null;
            // the light grid keeps positions, lightmap uvs, incident directions and triangles; the rest of the vertex data goes
            if (Bsp != null) foreach (var s in Bsp.Sections) { s.Normal = null; s.Binormal = null; s.Tangent = null; s.Uv = null; }
        }

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Halo world" };
            _thread.Start();
        }

        void Run()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { Work(); }
            catch (Exception e) { Error = e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace; }
            Milliseconds = sw.ElapsedMilliseconds;
            Done = true;
        }

        void Work()
        {
            Stage = "reading the structure"; Progress = 0.02f;
            Bsp = new HaloBsp(Scenario, BspIndex);
            HaloResources bitmaps = HaloTextures.BitmapsFor(Map);

            // lightmap pages
            Stage = "lightmaps"; Progress = 0.1f;
            var pages = new Dictionary<int, HaloLightGrid.Page>();
            if (Bsp.LightmapBitmap != null)
            {
                var bm = new HaloBitmap(Bsp.LightmapBitmap);
                foreach (var s in Bsp.Sections)
                {
                    if (s.Lightmap < 0 || s.Lightmap >= bm.Images.Count || pages.ContainsKey(s.Lightmap)) continue;
                    var img = bm.Images[s.Lightmap];
                    var raw = HaloBitmap.Raw(Map, img, bitmaps);
                    pages[s.Lightmap] = new HaloLightGrid.Page { Width = img.Width, Height = img.Height, Rgba = HaloBitmap.DecodeRgba(img, raw) };
                }
            }
            Atlas = HaloAtlas.Pack(pages);
            Light = new HaloLightGrid(Bsp, pages);

            // render pieces: one per shader
            Stage = "surfaces"; Progress = 0.25f;
            var byShader = new Dictionary<uint, RenderPiece>();
            float u = HaloSpace.Unit;
            var sizes = new Dictionary<uint, int[]>();
            foreach (var s in Bsp.Sections)
            {
                if (s.Shader == null || s.Triangles.Length == 0) continue;
                bool lm0 = s.Lightmap >= 0 && s.LightmapUv != null && pages.ContainsKey(s.Lightmap) && s.Shader.ClassName == "senv";
                uint k0 = (s.Shader.Id ^ (lm0 ? 0u : 0x80000000u)) + (s.Breakable >= 0 ? (uint)(s.Breakable + 1) * 2654435761u : 0u);
                int[] sz;
                if (!sizes.TryGetValue(k0, out sz)) { sz = new int[2]; sizes[k0] = sz; }
                sz[0] += s.VertexCount; sz[1] += s.Triangles.Length;
            }
            foreach (var s in Bsp.Sections)
            {
                if (s.Shader == null || s.Triangles.Length == 0) continue;
                bool lm = s.Lightmap >= 0 && s.LightmapUv != null && pages.ContainsKey(s.Lightmap) && s.Shader.ClassName == "senv";
                uint key = (s.Shader.Id ^ (lm ? 0u : 0x80000000u)) + (s.Breakable >= 0 ? (uint)(s.Breakable + 1) * 2654435761u : 0u);
                RenderPiece p;
                if (!byShader.TryGetValue(key, out p)) { p = new RenderPiece(sizes[key][0], sizes[key][1]) { Shader = s.Shader, Lightmapped = lm, Breakable = s.Breakable }; byShader[key] = p; Render.Add(p); }
                int baseV = p.Positions.Count;
                for (int i = 0; i < s.VertexCount; i++)
                {
                    int k = i * 3;
                    p.Positions.Add(new Vector3(s.Position[k] * u, s.Position[k + 2] * u, s.Position[k + 1] * u));
                    p.Normals.Add(new Vector3(s.Normal[k], s.Normal[k + 2], s.Normal[k + 1]));
                    p.Tangents.Add(new Vector4(s.Tangent[k], s.Tangent[k + 2], s.Tangent[k + 1], 1f));
                    p.Binormals.Add(new Vector3(s.Binormal[k], s.Binormal[k + 2], s.Binormal[k + 1]));
                    p.Uv.Add(new Vector2(s.Uv[i * 2], s.Uv[i * 2 + 1]));
                    if (lm)
                    {
                        float au, av;
                        Atlas.Map(s.Lightmap, s.LightmapUv[i * 2], s.LightmapUv[i * 2 + 1], out au, out av);
                        p.LightUv.Add(new Vector2(au, av));
                        p.Incident.Add(new Vector3(s.Incident[k], s.Incident[k + 2], s.Incident[k + 1]));
                    }
                    else
                    {
                        p.LightUv.Add(Vector2.zero);
                        p.Incident.Add(new Vector3(0f, 1f, 0f));
                    }
                }
                // Halo render triangles are clockwise about the normal: after the axis swap that is Unity's front face - order kept
                for (int t = 0; t < s.Triangles.Length; t++) p.Triangles.Add(baseV + s.Triangles[t]);
                p.Sections++;
            }

            // collision
            Stage = "collision"; Progress = 0.4f;
            if (Bsp.Collision != null)
            {
                var bsp = Bsp;
                Collision = HaloCollision.Prepare(Bsp.Collision, m => m >= 0 && m < bsp.CollisionMaterials.Count ? bsp.CollisionMaterials[m].Type : -1);
            }

            // sky: the one most clusters use; indoor when no cluster has one
            Stage = "sky"; Progress = 0.45f;
            var votes = new Dictionary<int, int>();
            int indoor = 0;
            foreach (var c in Bsp.Clusters)
            {
                if (c.Sky < 0) { indoor++; continue; }
                int n; votes.TryGetValue(c.Sky, out n); votes[c.Sky] = n + 1;
            }
            int best = -1, bestVotes = 0;
            foreach (var kv in votes) if (kv.Value > bestVotes) { best = kv.Key; bestVotes = kv.Value; }
            if (best < 0 && Scenario.Skies.Count > 0) best = 0;
            SkyIndex = best;
            Indoor = bestVotes == 0;
            if (best >= 0 && best < Scenario.Skies.Count) Sky = new HaloSky(Scenario.Skies[best]);

            // textures: decode everything the materials will ask for
            Stage = "textures"; Progress = 0.5f;
            var shaders = new List<HaloTag>();
            var seen = new HashSet<uint>();
            foreach (var p in Render) AddShader(p.Shader, shaders, seen);
            if (Sky != null && Sky.Model != null) AddModelShaders(Sky.Model, shaders, seen);
            if (IncludeObjects) foreach (var list in new[] { Scenario.Scenery, Scenario.Machines, Scenario.Controls, Scenario.LightFixtures, Scenario.SoundScenery })
                foreach (var pl in list)
                {
                    if (pl.Type == null) continue;
                    try { var def = new HaloObjectDef(pl.Type); if (def.Model != null) AddModelShaders(def.Model, shaders, seen); }
                    catch (Exception) { }
                }
            var maps = new List<KeyValuePair<HaloTag, int>>();
            foreach (var sh in shaders) Bitmaps(sh, maps);
            // the extras' sprites: lens flares, decals, weather
            int simg;
            foreach (var lf in Bsp.LensFlares)
                if (lf != null) foreach (var refl in lf.Root.Block(196, 128)) { var bm = lf.Root.Ref(32); if (bm != null) { HaloWorldExtras.SpriteRect(bm, refl.I16(4), out simg); maps.Add(new KeyValuePair<HaloTag, int>(bm, simg)); } }
            var decalPalette = new List<HaloTag>();
            foreach (var p in Scenario.Tag.Root.Block(948, 16)) decalPalette.Add(p.Ref(0));
            var usedDecals = new HashSet<int>();
            foreach (var d in Bsp.Decals) if (d.Type >= 0 && d.Type < decalPalette.Count && decalPalette[d.Type] != null && usedDecals.Add(d.Type)) { var bm = decalPalette[d.Type].Root.Ref(216); if (bm != null) { HaloWorldExtras.SpriteRect(bm, 0, out simg); maps.Add(new KeyValuePair<HaloTag, int>(bm, simg)); } }
            foreach (var w in Bsp.WeatherPalette)
                if (w.ParticleSystem != null) foreach (var pt in w.ParticleSystem.Root.Block(36, 604)) { var bm = pt.Ref(404); if (bm != null) { HaloWorldExtras.SpriteRect(bm, 0, out simg); maps.Add(new KeyValuePair<HaloTag, int>(bm, simg)); } }
            for (int i = 0; i < maps.Count; i++)
            {
                HaloTextures.PrepareAhead(maps[i].Key, maps[i].Value, true, bitmaps);
                Progress = 0.5f + 0.5f * (i + 1) / Math.Max(1, maps.Count);
            }
            Stage = "done"; Progress = 1f;
        }

        static void AddShader(HaloTag sh, List<HaloTag> into, HashSet<uint> seen)
        {
            if (sh == null || !seen.Add(sh.Id)) return;
            into.Add(sh);
            var d = HaloShaderDef.Of(sh);
            if (d != null && d.Transparent != null) foreach (var x in d.Transparent.ExtraLayers) AddShader(x, into, seen);
        }

        static void AddModelShaders(HaloTag model, List<HaloTag> into, HashSet<uint> seen)
        {
            if (model == null) return;
            foreach (var s in model.Root.Block(220, 32)) AddShader(s.Ref(0), into, seen);
        }

        static void Bitmaps(HaloTag shader, List<KeyValuePair<HaloTag, int>> into)
        {
            var d = HaloShaderDef.Of(shader);
            if (d == null) return;
            if (d.Environment != null)
            {
                var e = d.Environment;
                Add(into, e.BaseMap); Add(into, e.PrimaryDetail); Add(into, e.SecondaryDetail); Add(into, e.MicroDetail); Add(into, e.BumpMap);
                Add(into, e.SelfIllumMap); Add(into, e.ReflectionCube);
            }
            if (d.Object != null) { Add(into, d.Object.BaseMap); Add(into, d.Object.MultipurposeMap); Add(into, d.Object.DetailMap); Add(into, d.Object.ReflectionCube); }
            if (d.Transparent != null) foreach (var m in d.Transparent.Maps) Add(into, m.Map);
            if (d.Glassy != null) { Add(into, d.Glassy.BackgroundTintMap); Add(into, d.Glassy.BumpMap); Add(into, d.Glassy.DiffuseMap); Add(into, d.Glassy.SpecularMap); Add(into, d.Glassy.ReflectionMap); }
            if (d.Watery != null) { Add(into, d.Watery.BaseMap); Add(into, d.Watery.ReflectionMap); Add(into, d.Watery.RippleMaps); }
            if (d.Gauge != null) Add(into, d.Gauge.Map);
            if (d.Energy != null) { Add(into, d.Energy.PrimaryNoise); Add(into, d.Energy.SecondaryNoise); }
        }

        static void Add(List<KeyValuePair<HaloTag, int>> into, HaloTag bitmap)
        {
            if (bitmap != null) into.Add(new KeyValuePair<HaloTag, int>(bitmap, 0));
        }
    }
}

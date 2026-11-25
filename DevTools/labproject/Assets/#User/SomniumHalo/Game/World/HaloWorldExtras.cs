using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The parts of a structure BSP that are not surfaces, built per BSP under its root (they hide with it):
    /// lens flares (the BSP's lens flare markers - fluorescent glows, hazard lights - and the sky's sun flare), BSP decals (the runtime
    /// decals: scorch marks, signs, blood), and weather (rain / snow / insects of the weather palette, falling round the camera; the driver
    /// shows the one of the camera's cluster). All static meshes drawn by SomniumHalo/Sprite or /Transparent: nothing runs per frame on
    /// the CPU except the weather choice (HaloWorldDriver, 4 times a second).
    /// </summary>
    public sealed class HaloWorldExtras
    {
        public string Summary = "no extras";
        /// <summary>Weather objects by palette index (null = none for that entry).</summary>
        public GameObject[] Weather = new GameObject[0];
        readonly List<Material> _materials = new List<Material>();

        const string SpriteShader = "SomniumHalo/Sprite";

        static Texture2D _glow;
        /// <summary>A soft round glow (lens flares whose tag names no bitmap, e.g. a10's "fluorescent glow").</summary>
        static Texture2D Glow
        {
            get
            {
                if (_glow != null) return _glow;
                const int n = 64;
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float v = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        byte b = (byte)(v * v * 255f);
                        px[y * n + x] = new Color32(b, b, b, b);
                    }
                _glow = new Texture2D(n, n, TextureFormat.RGBA32, false, true) { name = "halo glow", wrapMode = TextureWrapMode.Clamp };
                _glow.SetPixels32(px);
                _glow.Apply(false, true);
                return _glow;
            }
        }

        internal IEnumerator Build(HaloWorldJob job, HaloScenario scenario, Transform parent, HaloWorld.Budget budget, List<UnityEngine.Object> owned)
        {
            var bsp = job.Bsp;
            int flares = 0, decals = 0, weathers = 0;
            HaloWorld._phase = "lens flares";
            try { flares = LensFlares(bsp, job, parent, owned); }
            catch (Exception e) { HaloLog.Warn("World: lens flares of " + bsp.Tag.Name + " failed: " + e.Message); }
            if (budget.Over()) { yield return null; budget.Reset(); }
            HaloWorld._phase = "decals";
            try { decals = Decals(bsp, scenario, parent, owned); }
            catch (Exception e) { HaloLog.Warn("World: decals of " + bsp.Tag.Name + " failed: " + e.Message); }
            if (budget.Over()) { yield return null; budget.Reset(); }
            HaloWorld._phase = "weather";
            try { weathers = WeatherSystems(bsp, parent, owned); }
            catch (Exception e) { HaloLog.Warn("World: weather of " + bsp.Tag.Name + " failed: " + e.Message); }
            Summary = flares + " lens flares, " + decals + " decals, " + weathers + " weather systems";
        }

        public void Release()
        {
            foreach (var m in _materials) if (m != null) UnityEngine.Object.Destroy(m);
            _materials.Clear();
        }

        Material SpriteMaterial(Texture tex, int mode, bool additive, string name)
        {
            Shader s = HaloShaders.Find(SpriteShader);
            if (s == null) return null;
            var m = new Material(s) { name = name, enableInstancing = true };
            m.SetTexture("_MainTex", tex != null ? tex : HaloMaterials.WhiteLinear);
            m.SetFloat("_Mode", mode);
            m.SetFloat("_SrcBlend", (float)(additive ? BlendMode.One : BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.renderQueue = 3050;
            _materials.Add(m);
            return m;
        }

        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        // ------------------------------------------------------------------ quads

        sealed class Quads
        {
            public readonly List<Vector3> P = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Vector4> Corner = new List<Vector4>(), Extra = new List<Vector4>();
            public readonly List<Color> C = new List<Color>();
            public readonly List<int> T = new List<int>();

            public void Add(Vector3 centre, Rect uv, float halfW, float halfH, Vector4 extra, Color c)
            {
                int b = P.Count;
                for (int k = 0; k < 4; k++)
                {
                    float x = k == 0 || k == 3 ? -1f : 1f, y = k < 2 ? -1f : 1f;
                    P.Add(centre);
                    // Halo texture rows are stored top first (v = 0 at the top): y = +1 (up) takes the top of the sprite
                    Uv.Add(new Vector2(x < 0 ? uv.xMin : uv.xMax, y > 0 ? uv.yMin : uv.yMax));
                    Corner.Add(new Vector4(x, y, halfW, halfH));
                    Extra.Add(extra);
                    C.Add(c);
                }
                T.Add(b); T.Add(b + 1); T.Add(b + 2); T.Add(b); T.Add(b + 2); T.Add(b + 3);
            }

            public Mesh ToMesh(string name, Bounds bounds)
            {
                var m = new Mesh { name = name };
                if (P.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(P);
                m.SetUVs(0, Uv);
                m.SetUVs(1, Corner);
                m.SetUVs(2, Extra);
                m.SetColors(C);
                m.SetTriangles(T, 0, false);
                m.bounds = bounds;
                m.UploadMeshData(true);
                return m;
            }
        }

        internal static Rect SpriteRect(HaloTag bitmap, int image, out int imageIndex)
        {
            imageIndex = Math.Max(0, image);
            var r = new Rect(0f, 0f, 1f, 1f);
            if (bitmap == null) return r;
            var bm = new HaloBitmap(bitmap);
            // sprite sheets: the image is a sequence index; the sprite gives the bitmap and its rectangle
            if (bm.BitmapType == 3 && bm.Sequences.Count > 0)
            {
                var seq = bm.Sequences[Mathf.Clamp(image, 0, bm.Sequences.Count - 1)];
                if (seq.Sprites.Count > 0)
                {
                    var sp = seq.Sprites[0];
                    imageIndex = sp.Bitmap;
                    return Rect.MinMaxRect(sp.Left, sp.Top, sp.Right, sp.Bottom);
                }
                if (seq.Count > 0) imageIndex = seq.First;
            }
            else if (bm.Sequences.Count > 0 && image >= 0 && image < bm.Sequences.Count && bm.Sequences[image].Count > 0) imageIndex = bm.Sequences[image].First;
            if (imageIndex >= bm.Images.Count) imageIndex = 0;
            return r;
        }

        // ------------------------------------------------------------------ lens flares

        int LensFlares(HaloBsp bsp, HaloWorldJob job, Transform parent, List<UnityEngine.Object> owned)
        {
            int count = 0;
            var root = new GameObject("Lens flares");
            root.transform.SetParent(parent, false);
            float u = HaloSpace.Unit;
            // markers grouped by flare
            var byFlare = new Dictionary<int, List<HaloBsp.LensFlareMarker>>();
            foreach (var mk in bsp.LensFlareMarkers)
            {
                if (mk.Flare < 0 || mk.Flare >= bsp.LensFlares.Count || bsp.LensFlares[mk.Flare] == null) continue;
                List<HaloBsp.LensFlareMarker> l;
                if (!byFlare.TryGetValue(mk.Flare, out l)) { l = new List<HaloBsp.LensFlareMarker>(); byFlare[mk.Flare] = l; }
                l.Add(mk);
            }
            foreach (var kv in byFlare)
            {
                HaloTag flare = bsp.LensFlares[kv.Key];
                var lf = flare.Root;
                HaloTag bitmap = lf.Ref(32);
                float cosFall = lf.F32(8), cosCut = lf.F32(12), near = lf.F32(24) * u, far = lf.F32(28) * u;
                foreach (var refl in lf.Block(196, 128))
                {
                    if (Mathf.Abs(refl.F32(28)) > 0.001f) continue;   // ghosts along the screen line: not drawn (ours)
                    int img; Rect rect = SpriteRect(bitmap, refl.I16(4), out img);
                    Texture tex = bitmap != null ? HaloTextures.Get(bitmap, img, true) : Glow;
                    float radius = Mathf.Max(refl.F32(40), refl.F32(44)) * u;
                    float bright = Mathf.Max(refl.F32(52), refl.F32(56));
                    Color tint = refl.F32(64) > 0.001f ? new Color(refl.F32(68), refl.F32(72), refl.F32(76)) : Color.white;
                    var q = new Quads();
                    var bounds = new Bounds();
                    bool first = true;
                    foreach (var mk in kv.Value)
                    {
                        Vector3 p = HaloSpace.Pos(mk.Position);
                        Vector3 dir = HaloSpace.Dir(mk.Dx, mk.Dy, mk.Dz);
                        q.Add(p, rect, radius * lf.F32(160) * 0.5f + radius * 0.5f, radius * lf.F32(164) * 0.5f + radius * 0.5f, new Vector4(dir.x, dir.y, dir.z, 0f), tint * bright);
                        if (first) { bounds = new Bounds(p, Vector3.one * radius * 2f); first = false; } else bounds.Encapsulate(p);
                        count++;
                    }
                    bounds.Expand(radius * 4f);
                    var mat = SpriteMaterial(tex, 0, true, "lens flare " + flare.Name);
                    if (mat == null) return count;
                    mat.SetVector("_Fade", new Vector4(0f, 0f, near > 0f || far > 0f ? Mathf.Max(near, 0f) : 1e6f, far > near ? far : 1e6f));
                    mat.SetVector("_Angles", new Vector4(cosFall, cosCut, 0f, 0f));
                    mat.SetFloat("_ZTest", (float)CompareFunction.Always);
                    var mesh = q.ToMesh("lens flares " + System.IO.Path.GetFileName(flare.Name), bounds);
                    owned.Add(mesh);
                    MeshObject("lens flare " + System.IO.Path.GetFileName(flare.Name), root.transform, mesh, mat);
                }
            }
            // the sun's flare (the sky's brightest light with a lens flare)
            var sky = job.Sky;
            if (sky != null)
            {
                HaloSky.Light sun = null;
                foreach (var l in sky.Lights) if (l.LensFlare != null && (sun == null || l.Power > sun.Power)) sun = l;
                if (sun != null)
                {
                    var lf = sun.LensFlare.Root;
                    HaloTag bitmap = lf.Ref(32);
                    Vector3 dir = HaloSpace.Dir(sun.Direction).normalized;
                    foreach (var refl in lf.Block(196, 128))
                    {
                        if (Mathf.Abs(refl.F32(28)) > 0.001f) continue;
                        int img; Rect rect = SpriteRect(bitmap, refl.I16(4), out img);
                        Texture tex = bitmap != null ? HaloTextures.Get(bitmap, img, true) : Glow;
                        float half = Mathf.Max(refl.F32(40), refl.F32(44)) * 1000f * 0.5f;   // ours: the radius as a fraction of the view distance
                        float bright = Mathf.Max(refl.F32(52), refl.F32(56));
                        Color tint = refl.F32(64) > 0.001f ? new Color(refl.F32(68), refl.F32(72), refl.F32(76)) : Color.white;
                        var q = new Quads();
                        q.Add(Vector3.zero, rect, half, half, new Vector4(dir.x, dir.y, dir.z, 1f), tint * bright);
                        var mat = SpriteMaterial(tex, 0, true, "sun flare");
                        if (mat == null) break;
                        mat.SetFloat("_ZTest", (float)CompareFunction.Always);
                        var mesh = q.ToMesh("sun flare", new Bounds(Vector3.zero, Vector3.one * 100000f));
                        owned.Add(mesh);
                        MeshObject("sun flare", root.transform, mesh, mat);
                        count++;
                    }
                }
            }
            return count;
        }

        // ------------------------------------------------------------------ decals

        int Decals(HaloBsp bsp, HaloScenario scenario, Transform parent, List<UnityEngine.Object> owned)
        {
            if (bsp.Decals.Count == 0) return 0;
            var palette = new List<HaloTag>();
            foreach (var p in scenario.Tag.Root.Block(948, 16)) palette.Add(p.Ref(0));
            var root = new GameObject("Decals");
            root.transform.SetParent(parent, false);
            float u = HaloSpace.Unit;
            var byType = new Dictionary<int, List<HaloBsp.RuntimeDecal>>();
            foreach (var d in bsp.Decals)
            {
                if (d.Type < 0 || d.Type >= palette.Count || palette[d.Type] == null) continue;
                List<HaloBsp.RuntimeDecal> l;
                if (!byType.TryGetValue(d.Type, out l)) { l = new List<HaloBsp.RuntimeDecal>(); byType[d.Type] = l; }
                l.Add(d);
            }
            int count = 0, snapped = 0;
            foreach (var kv in byType)
            {
                HaloTag decal = palette[kv.Key];
                var dr = decal.Root;
                HaloTag bitmap = dr.Ref(216);
                int blend = dr.U16(192);
                float radius = Mathf.Max(dr.F32(24), 0.01f) * u;
                Color c0 = new Color(dr.F32(52), dr.F32(56), dr.F32(60)), c1 = new Color(dr.F32(64), dr.F32(68), dr.F32(72));
                Color col = (c0 + c1) * 0.5f;
                if (col.r + col.g + col.b < 0.01f) col = Color.white;
                int img; Rect rect = SpriteRect(bitmap, 0, out img);
                var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var normals = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
                foreach (var d in kv.Value)
                {
                    Vector3 p = HaloSpace.Pos(d.Position);
                    // facing from the stored yaw / pitch (int8 angles), checked against the collision under it
                    float yaw = d.Yaw / 128f * Mathf.PI, pitch = d.Pitch / 128f * (Mathf.PI * 0.5f);
                    Vector3 n = HaloSpace.Dir(Mathf.Cos(yaw) * Mathf.Cos(pitch), Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch)).normalized;
                    float hx, hy, hz;
                    if (bsp.Collision != null && bsp.Collision.SurfaceAt(d.Position.X, d.Position.Y, d.Position.Z, 0.1f, out hx, out hy, out hz)) { n = HaloSpace.Dir(hx, hy, hz).normalized; snapped++; }
                    Vector3 t = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
                    Vector3 b = Vector3.Cross(n, t);
                    float spin = (d.Position.X * 13.1f + d.Position.Y * 7.7f) % (2f * Mathf.PI);
                    Vector3 tt = (t * Mathf.Cos(spin) + b * Mathf.Sin(spin)) * radius, bb = Vector3.Cross(n, tt.normalized) * radius;
                    int bi = verts.Count;
                    verts.Add(p - tt - bb); verts.Add(p + tt - bb); verts.Add(p + tt + bb); verts.Add(p - tt + bb);
                    uvs.Add(new Vector2(rect.xMin, rect.yMax)); uvs.Add(new Vector2(rect.xMax, rect.yMax)); uvs.Add(new Vector2(rect.xMax, rect.yMin)); uvs.Add(new Vector2(rect.xMin, rect.yMin));
                    for (int k = 0; k < 4; k++) { normals.Add(n); cols.Add(Color.white); }
                    // front face towards the normal (Unity clockwise seen from the front)
                    tris.Add(bi); tris.Add(bi + 2); tris.Add(bi + 1); tris.Add(bi); tris.Add(bi + 3); tris.Add(bi + 2);
                    count++;
                }
                var mesh = new Mesh { name = "decals " + System.IO.Path.GetFileName(decal.Name) };
                mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetNormals(normals); mesh.SetColors(cols); mesh.SetTriangles(tris, 0, true);
                mesh.UploadMeshData(true);
                owned.Add(mesh);
                Shader s = HaloShaders.Find(HaloMaterials.TransparentShader);
                if (s == null) continue;
                var m = new Material(s) { name = "decal " + decal.Name, enableInstancing = true };
                m.SetTexture("_Map0", bitmap != null ? HaloTextures.Get(bitmap, img, true) : HaloMaterials.WhiteLinear);
                m.SetVector("_Setup", new Vector4(1f, 0f, blend, 0f));
                m.SetColor("_Tint", new Color(col.r, col.g, col.b, 1f));
                HaloMaterials.SetBlendPublic(m, blend);
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.SetFloat("_OffsetFactor", -1f); m.SetFloat("_OffsetUnits", -4f);
                m.renderQueue = 2995;
                _materials.Add(m);
                MeshObject("decals " + System.IO.Path.GetFileName(decal.Name), root.transform, mesh, m);
            }
            if (count > 0 && snapped < count) HaloLog.Info("World: " + bsp.Tag.Name + " decals: " + snapped + "/" + count + " on a collision surface (the rest face their stored yaw / pitch)");
            return count;
        }

        // ------------------------------------------------------------------ weather

        int WeatherSystems(HaloBsp bsp, Transform parent, List<UnityEngine.Object> owned)
        {
            Weather = new GameObject[bsp.WeatherPalette.Count];
            if (bsp.WeatherPalette.Count == 0) return 0;
            int made = 0;
            float u = HaloSpace.Unit;
            var rnd = new System.Random(1234);
            for (int wi = 0; wi < bsp.WeatherPalette.Count; wi++)
            {
                var w = bsp.WeatherPalette[wi];
                if (w.ParticleSystem == null) continue;
                var go = new GameObject("Weather " + w.Name);
                go.transform.SetParent(parent, false);
                go.SetActive(false);
                Weather[wi] = go;
                foreach (var pt in w.ParticleSystem.Root.Block(36, 604))
                {
                    string name = pt.Str32(0);
                    float fadeInStart = pt.F32(36) * u, fadeInEnd = pt.F32(40) * u, fadeOutStart = pt.F32(44) * u, fadeOutEnd = pt.F32(48) * u;
                    float density = Mathf.Max(pt.F32(164), pt.F32(168));            // particles per cubic world unit
                    float rMin = pt.F32(252) * u, rMax = Mathf.Max(pt.F32(256), pt.F32(252)) * u;
                    HaloTag bitmap = pt.Ref(404);
                    int render = pt.U16(420), blend = pt.U16(466);
                    Color c0 = new Color(pt.F32(312), pt.F32(316), pt.F32(320), pt.F32(308)), c1 = new Color(pt.F32(328), pt.F32(332), pt.F32(336), pt.F32(324));
                    float box = Mathf.Max(4f, fadeOutEnd * 2f);
                    float volumeWu = Mathf.Pow(box / u, 3f);
                    int n = Mathf.Clamp((int)(density * volumeWu), 16, 6000);   // ours: cap for VR
                    bool streak = render == 1;
                    string lower = name.ToLowerInvariant() + " " + w.Name.ToLowerInvariant();
                    // fall speeds (m/s, ours: Halo moves weather by point physics + wind, not in the tag as a speed)
                    Vector3 fall = lower.Contains("rain") ? new Vector3(0f, -9f, 0f) : lower.Contains("snow") ? new Vector3(0.3f, -1.1f, 0.2f) : lower.Contains("debris") || lower.Contains("water") ? new Vector3(0.1f, -0.1f, 0.05f) : new Vector3(0.4f, 0f, 0.3f);
                    float sway = lower.Contains("snow") ? 0.4f : lower.Contains("rain") ? 0f : 0.8f;
                    int img; Rect rect = SpriteRect(bitmap, 0, out img);
                    var q = new Quads();
                    for (int i = 0; i < n; i++)
                    {
                        var p = new Vector3((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble());
                        float r = Mathf.Lerp(rMin, rMax, (float)rnd.NextDouble());
                        q.Add(p, rect, r, r, Vector4.zero, Color.Lerp(c0, c1, (float)rnd.NextDouble()));
                    }
                    var mesh = q.ToMesh("weather " + name, new Bounds(Vector3.zero, Vector3.one * 100000f));
                    owned.Add(mesh);
                    Texture tex = bitmap != null ? HaloTextures.Get(bitmap, img, true) : null;
                    var mat = SpriteMaterial(tex, 2, blend == 3, "weather " + name);
                    if (mat == null) continue;
                    mat.SetVector("_Box", new Vector4(box, box, box, sway));
                    mat.SetVector("_Fall", new Vector4(fall.x, fall.y, fall.z, streak ? 0.02f : 0f));
                    mat.SetVector("_Fade", new Vector4(fadeInStart, fadeInEnd, fadeOutStart, fadeOutEnd));
                    MeshObject("weather " + name, go.transform, mesh, mat);
                    made++;
                }
            }
            return made;
        }
    }
}

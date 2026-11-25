using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// One dynamic mesh of quads drawn with one material (texture x Halo framebuffer blend function), rebuilt every frame by the effects
    /// runtime. Vertices carry the quad centre + corner offsets; Shaders/Fx/HaloFx.shader expands them per eye (see there). Arrays grow
    /// by doubling (allocation only when a batch outgrows its record), the index buffer is a fixed quad pattern.
    /// Alpha-blended batches collect (key, item) pairs first and are written back to front (<see cref="Sorted"/>).
    /// </summary>
    internal sealed class FxBatch
    {
        public Material Mat;
        public Mesh Mesh;
        public GameObject Go;
        public MeshRenderer Renderer;
        public int Blend;
        public bool Sorted;
        public string Name;

        public Vector3[] Pos = new Vector3[64];
        public Color32[] Col = new Color32[64];
        public Vector4[] Uv0 = new Vector4[64];
        public Vector4[] Uv1 = new Vector4[64];
        public int[] Tri = new int[96];
        public int Quads, LastQuads, TriQuads;
        public Vector3 Min, Max;

        // sort lists (alpha blend): key = squared distance, item = whatever the writer needs (particle index)
        public float[] Keys = new float[32];
        public int[] Items = new int[32];
        public int ItemCount;

        public void Begin()
        {
            Quads = 0; ItemCount = 0;
            Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        }

        public void AddItem(int item, float key)
        {
            if (ItemCount == Items.Length) { Array.Resize(ref Items, Items.Length * 2); Array.Resize(ref Keys, Keys.Length * 2); }
            Items[ItemCount] = item; Keys[ItemCount] = -key; ItemCount++;
        }

        public void SortItems() { if (ItemCount > 1) Array.Sort(Keys, Items, 0, ItemCount); }

        void Grow()
        {
            int n = Pos.Length * 2;
            Array.Resize(ref Pos, n); Array.Resize(ref Col, n); Array.Resize(ref Uv0, n); Array.Resize(ref Uv1, n);
        }

        /// <summary>Adds a quad: c = centre (kind 0/1) or corner points (kind 2 uses p0..p3 as final positions).</summary>
        public void Quad(Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Vector4 a0, Vector4 a1, Vector4 a2, Vector4 a3, Vector4 axisKind, Color32 col, float extent)
        {
            int v = Quads * 4;
            if (v + 4 > Pos.Length) Grow();
            Pos[v] = c0; Pos[v + 1] = c1; Pos[v + 2] = c2; Pos[v + 3] = c3;
            Uv0[v] = a0; Uv0[v + 1] = a1; Uv0[v + 2] = a2; Uv0[v + 3] = a3;
            Uv1[v] = axisKind; Uv1[v + 1] = axisKind; Uv1[v + 2] = axisKind; Uv1[v + 3] = axisKind;
            Col[v] = col; Col[v + 1] = col; Col[v + 2] = col; Col[v + 3] = col;
            Quads++;
            Encapsulate(c0, extent); Encapsulate(c2, extent);
            if (axisKind.w > 1.5f) { Encapsulate(c1, 0f); Encapsulate(c3, 0f); }
        }

        /// <summary>A quad of four vertices with their own colours (contrail segments).</summary>
        public void Quad4(Vector3 c0, Vector3 c1, Vector3 c2, Vector3 c3, Vector4 a0, Vector4 a1, Vector4 a2, Vector4 a3,
            Vector4 k0, Vector4 k1, Vector4 k2, Vector4 k3, Color32 col0, Color32 col1, Color32 col2, Color32 col3, float extent)
        {
            int v = Quads * 4;
            if (v + 4 > Pos.Length) Grow();
            Pos[v] = c0; Pos[v + 1] = c1; Pos[v + 2] = c2; Pos[v + 3] = c3;
            Uv0[v] = a0; Uv0[v + 1] = a1; Uv0[v + 2] = a2; Uv0[v + 3] = a3;
            Uv1[v] = k0; Uv1[v + 1] = k1; Uv1[v + 2] = k2; Uv1[v + 3] = k3;
            Col[v] = col0; Col[v + 1] = col1; Col[v + 2] = col2; Col[v + 3] = col3;
            Quads++;
            Encapsulate(c0, extent); Encapsulate(c1, extent); Encapsulate(c2, extent); Encapsulate(c3, extent);
        }

        void Encapsulate(Vector3 p, float e)
        {
            if (p.x - e < Min.x) Min.x = p.x - e; if (p.y - e < Min.y) Min.y = p.y - e; if (p.z - e < Min.z) Min.z = p.z - e;
            if (p.x + e > Max.x) Max.x = p.x + e; if (p.y + e > Max.y) Max.y = p.y + e; if (p.z + e > Max.z) Max.z = p.z + e;
        }

        public void End()
        {
            if (Mesh == null) return;
            bool show = Quads > 0;
            if (Go.activeSelf != show) Go.SetActive(show);
            if (!show) return; // the mesh keeps its last index count (LastQuads) until the next upload
            if (TriQuads < Quads)
            {
                int n = Math.Max(Quads, TriQuads * 2);
                if (Tri.Length < n * 6) Tri = new int[n * 6];
                for (int q = 0; q < n; q++)
                {
                    int i = q * 6, v = q * 4;
                    Tri[i] = v; Tri[i + 1] = v + 1; Tri[i + 2] = v + 2; Tri[i + 3] = v; Tri[i + 4] = v + 2; Tri[i + 5] = v + 3;
                }
                TriQuads = n;
            }
            int verts = Quads * 4;
            if (Quads < LastQuads) Mesh.SetTriangles(Tri, 0, Quads * 6, 0, false);
            Mesh.SetVertices(Pos, 0, verts);
            Mesh.SetColors(Col, 0, verts);
            Mesh.SetUVs(0, Uv0, 0, verts);
            Mesh.SetUVs(1, Uv1, 0, verts);
            if (Quads >= LastQuads) Mesh.SetTriangles(Tri, 0, Quads * 6, 0, false);
            var b = new Bounds();
            b.SetMinMax(Min, Max);
            Mesh.bounds = b;
            LastQuads = Quads;
        }
    }

    /// <summary>Materials and batches of the effects (keyed by texture + blend + layer). Main thread.</summary>
    internal sealed class FxRender
    {
        public const string ShaderName = "SomniumHalo/Fx";
        readonly Dictionary<long, FxBatch> _batches = new Dictionary<long, FxBatch>();
        public readonly List<FxBatch> All = new List<FxBatch>();
        readonly Transform _root;
        Shader _shader;
        bool _shaderLogged;

        public FxRender(Transform root) { _root = root; }

        public bool Ready
        {
            get
            {
                if (_shader == null)
                {
                    _shader = HaloShaders.Find(ShaderName);
                    if (_shader == null && !_shaderLogged) { _shaderLogged = true; HaloLog.Warn("Fx: shader " + ShaderName + " is missing (carrier Shaders/Fx/HaloFx.mat not in HaloGame _shaderCarriers?) - effects are not drawn"); }
                }
                return _shader != null;
            }
        }

        /// <summary>The batch of (texture, blend function, layer). layer: 0 particles, 1 contrails, 2..6 decals by decal layer, 7 lens flares.</summary>
        public FxBatch Get(Texture tex, int blend, int layer)
        {
            if (tex == null) tex = HaloTextures.White;
            long key = ((long)tex.GetInstanceID() << 16) ^ ((long)(blend & 0xFF) << 4) ^ layer;
            FxBatch b;
            if (_batches.TryGetValue(key, out b)) return b;
            b = Create(tex, blend, layer);
            _batches[key] = b;
            return b;
        }

        FxBatch Create(Texture tex, int blend, int layer)
        {
            var b = new FxBatch { Blend = blend, Sorted = blend == 0 && layer < 2, Name = tex.name + " b" + blend + " l" + layer };
            if (!Ready) return b;
            var m = new Material(_shader) { name = "HaloFx " + b.Name };
            Material carrier = HaloShaders.Carrier(ShaderName);
            if (carrier != null) m.CopyPropertiesFromMaterial(carrier);
            m.mainTexture = tex;
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            SetBlend(m, blend);
            bool decal = layer >= 2 && layer <= 6;
            if (layer == 7) m.SetFloat("_GammaAdd", 1f);
            if (decal) { m.SetFloat("_OffsetFactor", -1f); m.SetFloat("_OffsetUnits", -2f); }
            // decals right after the opaque world (by Halo's decal layer), effects in the transparent queue
            m.renderQueue = decal ? 2501 + (layer - 2) : layer == 1 ? 3001 : layer == 7 ? 3003 : 3002;
            b.Mat = m;
            b.Go = new GameObject("fx " + b.Name);
            b.Go.transform.SetParent(_root, false);
            b.Go.layer = _root.gameObject.layer;
            var mf = b.Go.AddComponent<MeshFilter>();
            b.Renderer = b.Go.AddComponent<MeshRenderer>();
            b.Renderer.sharedMaterial = m;
            b.Renderer.shadowCastingMode = ShadowCastingMode.Off;
            b.Renderer.receiveShadows = false;
            b.Renderer.lightProbeUsage = LightProbeUsage.Off;
            b.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            b.Renderer.allowOcclusionWhenDynamic = false;
            b.Mesh = new Mesh { name = b.Go.name };
            b.Mesh.MarkDynamic();
            b.Mesh.indexFormat = IndexFormat.UInt32;
            mf.sharedMesh = b.Mesh;
            b.Go.SetActive(false);
            All.Add(b);
            return b;
        }

        /// <summary>Halo's framebuffer blend functions (enum FramebufferBlendFunction) as Unity blend state + the shader's colour fade mode.</summary>
        public static void SetBlend(Material m, int blend)
        {
            BlendMode src = BlendMode.SrcAlpha, dst = BlendMode.OneMinusSrcAlpha; BlendOp op = BlendOp.Add;
            switch (blend)
            {
                case 1: src = BlendMode.DstColor; dst = BlendMode.Zero; break;                       // multiply
                case 2: src = BlendMode.DstColor; dst = BlendMode.Zero; break;                       // double multiply (the shader outputs the 2x factor)
                case 3: src = BlendMode.One; dst = BlendMode.One; break;                             // add
                case 4: src = BlendMode.One; dst = BlendMode.One; op = BlendOp.ReverseSubtract; break; // subtract
                case 5: src = BlendMode.One; dst = BlendMode.One; op = BlendOp.Min; break;           // component min
                case 6: src = BlendMode.One; dst = BlendMode.One; op = BlendOp.Max; break;           // component max
                case 7: src = BlendMode.One; dst = BlendMode.OneMinusSrcAlpha; break;                // alpha multiply add
                default: blend = 0; break;
            }
            m.SetFloat("_Mode", blend);
            m.SetFloat("_SrcBlend", (float)src);
            m.SetFloat("_DstBlend", (float)dst);
            m.SetFloat("_BlendOp", (float)op);
        }

        public void BeginAll() { for (int i = 0; i < All.Count; i++) All[i].Begin(); }
        public void EndAll() { for (int i = 0; i < All.Count; i++) All[i].End(); }

        public int QuadCount { get { int n = 0; for (int i = 0; i < All.Count; i++) n += All[i].Quads; return n; } }
        public int ActiveBatches { get { int n = 0; for (int i = 0; i < All.Count; i++) if (All[i].Quads > 0) n++; return n; } }

        public void Destroy()
        {
            foreach (var b in All)
            {
                if (b.Mesh != null) UnityEngine.Object.Destroy(b.Mesh);
                if (b.Mat != null) UnityEngine.Object.Destroy(b.Mat);
                if (b.Go != null) UnityEngine.Object.Destroy(b.Go);
            }
            All.Clear();
            _batches.Clear();
        }
    }

    /// <summary>The Unity side of a sprite sheet: one texture per image (made on first use).</summary>
    internal sealed class FxSheetGpu
    {
        public Texture[] Textures;
        public static Texture Texture(FxSpriteSheet s, int image)
        {
            var g = s.Gpu as FxSheetGpu;
            if (g == null) { g = new FxSheetGpu { Textures = new Texture[Math.Max(1, s.Images)] }; s.Gpu = g; }
            if (image < 0 || image >= g.Textures.Length) image = 0;
            var t = g.Textures[image];
            if (t == null) { t = HaloTextures.Get(s.Bitmap, image) ?? HaloTextures.White; g.Textures[image] = t; }
            return t;
        }
    }
}

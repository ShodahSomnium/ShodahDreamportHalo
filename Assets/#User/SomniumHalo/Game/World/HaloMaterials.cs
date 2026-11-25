using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Unity materials for Halo shader tags, cached per (map, shader tag, variant), drawn with the World module's own shaders
    /// (Shaders/: SomniumHalo/Env for shader_environment, SomniumHalo/Model for shader_model, SomniumHalo/Transparent for
    /// shader_transparent_chicago / chicago extended / plasma / meter, SomniumHalo/Glass for glass and water). Every number comes
    /// from the tag (Data/Shaders/HaloShaderDef); textures are uploaded LINEAR (raw values) because the shaders do the game's
    /// gamma-space maths themselves (Shaders/HaloCommon.hlsl). When a custom shader is missing (not carried) the material falls back
    /// to URP Lit / Unlit with the main colour map, as the first version did.
    /// Lighting: models are lit by the object light the world sets per renderer (<see cref="HaloObjectLight"/>) or the map default
    /// (globals, <see cref="SetDefaultLight"/>). Change colours (team colours of armour, etc.): <see cref="SetChangeColor"/>.
    /// </summary>
    public static class HaloMaterials
    {
        public enum Kind { Opaque, Cutout, Additive, Blend }

        public const string EnvShader = "SomniumHalo/Env", ModelShader = "SomniumHalo/Model", TransparentShader = "SomniumHalo/Transparent", GlassShader = "SomniumHalo/Glass";
        /// <summary>Sky geometry queues (URP depth priming: anything drawn behind the world must come after the opaque range).</summary>
        public const int SkyQueue = 2501;

        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        static readonly Dictionary<string, List<Material>> Extras = new Dictionary<string, List<Material>>();
        static Material _fallback;
        static Texture2D _neutral, _white, _flat, _black;
        static bool _globals;

        // ------------------------------------------------------------------ public calls

        /// <summary>The material of a shader tag (any shader class). Null tag = a grey fallback.</summary>
        public static Material Get(HaloTag shader) { return Get(shader, false); }

        /// <summary>The material of a shader tag; <paramref name="sky"/> = the variant for sky geometry (around the eye, far plane, queue 2501+).</summary>
        public static Material Get(HaloTag shader, bool sky)
        {
            EnsureGlobals();
            if (shader == null) return Fallback;
            string key = shader.Map.Path + "|" + shader.Id + (sky ? "|sky" : "");
            Material m;
            if (Cache.TryGetValue(key, out m) && m != null) return m;
            try { m = Build(shader, null, sky); }
            catch (Exception e) { HaloLog.Warn("Materials: " + shader + " failed: " + e.Message); m = null; }
            if (m == null) m = Fallback;
            Cache[key] = m;
            return m;
        }

        /// <summary>A structure-BSP surface material: shader_environment with the BSP's lightmap (atlas) on uv2 (null = not lightmapped).
        /// Other shader classes on BSP surfaces get their normal material.</summary>
        public static Material ForBsp(HaloTag shader, Texture lightmap)
        {
            EnsureGlobals();
            if (shader == null) return Fallback;
            if (shader.ClassName != "senv" || lightmap == null) return Get(shader);
            string key = shader.Map.Path + "|" + shader.Id + "|lm" + lightmap.GetInstanceID();
            Material m;
            if (Cache.TryGetValue(key, out m) && m != null) return m;
            try { m = Build(shader, lightmap, false); }
            catch (Exception e) { HaloLog.Warn("Materials: " + shader + " (bsp) failed: " + e.Message); m = null; }
            if (m == null) m = Fallback;
            Cache[key] = m;
            return m;
        }

        /// <summary>The extra layers of a transparent chicago shader (drawn on the same triangles after it), empty for others.</summary>
        public static List<Material> ExtraLayers(HaloTag shader, bool sky = false)
        {
            var list = new List<Material>();
            if (shader == null) return list;
            string key = shader.Map.Path + "|" + shader.Id + (sky ? "|sky" : "");
            List<Material> l;
            if (Extras.TryGetValue(key, out l)) return l;
            var d = HaloShaderDef.Of(shader);
            if (d != null && d.Transparent != null)
                foreach (var t in d.Transparent.ExtraLayers) if (t != null && t != shader) list.Add(Get(t, sky));
            Extras[key] = list;
            return list;
        }

        public static Material Fallback
        {
            get
            {
                if (_fallback == null) { _fallback = NewLit(); _fallback.name = "Halo fallback"; SetColor(_fallback, new Color(0.5f, 0.5f, 0.5f, 1f)); }
                return _fallback;
            }
        }

        /// <summary>The main colour map of any shader tag (null when it has none). Used by builders and by UI that wants a swatch.</summary>
        public static HaloTag MainMap(HaloTag shader, out float uScale, out float vScale, out Kind kind)
        {
            uScale = 1f; vScale = 1f; kind = Kind.Opaque;
            if (shader == null) return null;
            var d = HaloShaderDef.Of(shader);
            if (d.Environment != null) { if (d.Environment.AlphaTested) kind = Kind.Cutout; return d.Environment.BaseMap; }
            if (d.Object != null)
            {
                uScale = d.Object.UScale != 0 ? d.Object.UScale : 1f; vScale = d.Object.VScale != 0 ? d.Object.VScale : 1f;
                if (d.Object.AlphaTested) kind = Kind.Cutout;
                return d.Object.BaseMap;
            }
            if (d.Transparent != null)
            {
                int b = d.Transparent.Blend;
                kind = b == 3 || b == 7 ? Kind.Additive : Kind.Blend;
                foreach (var mp in d.Transparent.Maps) { uScale = mp.UScale != 0 ? mp.UScale : 1; vScale = mp.VScale != 0 ? mp.VScale : 1; return mp.Map; }
                return null;
            }
            if (d.Glassy != null) { kind = Kind.Blend; return d.Glassy.DiffuseMap ?? d.Glassy.BackgroundTintMap; }
            if (d.Gauge != null) { kind = Kind.Blend; return d.Gauge.Map; }
            if (d.Watery != null) { kind = Kind.Blend; return d.Watery.BaseMap; }
            if (d.Energy != null) { kind = Kind.Additive; return d.Energy.PrimaryNoise; }
            return null;
        }

        /// <summary>Sets the change colour (Halo's object change colour A: armour / team colour) on a renderer's model materials.</summary>
        public static void SetChangeColor(Renderer r, Color c)
        {
            if (r == null) return;
            var b = new MaterialPropertyBlock();
            r.GetPropertyBlock(b);
            b.SetColor(IdChangeColor, c);
            r.SetPropertyBlock(b);
        }

        /// <summary>The map-wide default object light (Unity world direction TOWARDS the sun; gamma colours). HaloWorld sets it per map.</summary>
        public static void SetDefaultLight(Color ambient, Vector3 sunDirection, Color sun, Color sky)
        {
            _globals = true;
            Shader.SetGlobalVector(IdAmbient, ambient);
            Shader.SetGlobalVector(IdSunDir, sunDirection.normalized);
            Shader.SetGlobalVector(IdSunColor, sun);
            Shader.SetGlobalVector(IdSkyLight, sky);
        }

        /// <summary>The atmospheric fog of the shaders (gamma colour, maximum density 0..1, metres). density 0 = off.</summary>
        public static void SetFog(Color colour, float maxDensity, float start, float opaque)
        {
            Shader.SetGlobalVector(IdFogColor, new Vector4(colour.r, colour.g, colour.b, maxDensity));
            Shader.SetGlobalVector(IdFogDist, new Vector4(start, Mathf.Max(opaque, start + 0.01f), 0f, 0f));
        }

        /// <summary>Planar fog (a BSP fog plane): plane in Unity space (n.p = d is the surface, fog below), gamma colour, density, metres.</summary>
        public static void SetPlaneFog(Vector3 normal, float d, Color colour, float maxDensity, float opaqueDepth, float opaqueDistance)
        {
            Shader.SetGlobalVector(IdPlaneFog, new Vector4(normal.x, normal.y, normal.z, d));
            Shader.SetGlobalVector(IdPlaneFogColor, new Vector4(colour.r, colour.g, colour.b, maxDensity));
            Shader.SetGlobalVector(IdPlaneFogDist, new Vector4(Mathf.Max(0.01f, opaqueDepth), Mathf.Max(0.01f, opaqueDistance), 0f, 0f));
        }

        /// <summary>The defaults used outside a map (the hub, previews): soft light from above, no fog.</summary>
        public static void ResetGlobals()
        {
            SetDefaultLight(new Color(0.45f, 0.45f, 0.48f), new Vector3(-0.4f, 0.8f, -0.3f), new Color(0.55f, 0.53f, 0.5f), new Color(0.15f, 0.16f, 0.2f));
            SetFog(Color.gray, 0f, 0f, 1f);
            SetPlaneFog(Vector3.up, -100000f, Color.gray, 0f, 1f, 1f);
        }

        static void EnsureGlobals() { if (!_globals) ResetGlobals(); }

        // ------------------------------------------------------------------ property ids

        static readonly int IdChangeColor = Shader.PropertyToID("_ChangeColor");
        static readonly int IdAmbient = Shader.PropertyToID("_HaloAmbient"), IdSunDir = Shader.PropertyToID("_HaloSunDir"),
            IdSunColor = Shader.PropertyToID("_HaloSunColor"), IdSkyLight = Shader.PropertyToID("_HaloSkyLight");
        static readonly int IdFogColor = Shader.PropertyToID("_HaloFogColor"), IdFogDist = Shader.PropertyToID("_HaloFogDist");
        static readonly int IdPlaneFog = Shader.PropertyToID("_HaloPlaneFog"), IdPlaneFogColor = Shader.PropertyToID("_HaloPlaneFogColor"),
            IdPlaneFogDist = Shader.PropertyToID("_HaloPlaneFogDist");

        // ------------------------------------------------------------------ neutral textures (linear)

        static Texture2D Solid(byte r, byte g, byte b, byte a, string name)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = name };
            t.SetPixels32(new[] { new Color32(r, g, b, a) });
            t.Apply(false, true);
            return t;
        }

        /// <summary>Neutral detail map (0.5 grey, alpha 1): no change under double biased multiply / add.</summary>
        public static Texture2D Neutral { get { if (_neutral == null) _neutral = Solid(128, 128, 128, 255, "halo neutral"); return _neutral; } }
        public static Texture2D WhiteLinear { get { if (_white == null) _white = Solid(255, 255, 255, 255, "halo white"); return _white; } }
        public static Texture2D BlackLinear { get { if (_black == null) _black = Solid(0, 0, 0, 0, "halo black"); return _black; } }
        public static Texture2D FlatBump { get { if (_flat == null) _flat = Solid(128, 128, 255, 255, "halo flat bump"); return _flat; } }

        static Texture Tex(HaloTag bitmap, Texture fallback, int index = 0)
        {
            if (bitmap == null) return fallback;
            Texture t = HaloTextures.Get(bitmap, index, true);
            return t != null ? t : fallback;
        }

        static float NonZero(float v, float d) { return v == 0f || float.IsNaN(v) ? d : v; }
        static Vector4 V(HVec3 v, float w = 1f) { return new Vector4(v.X, v.Y, v.Z, w); }
        static Vector4 Wave(HaloShaderDef.Wave w) { return new Vector4(w.Function, NonZero(w.Period, 1f), w.Phase, w.Scale); }

        // ------------------------------------------------------------------ building

        static Material Build(HaloTag shader, Texture lightmap, bool sky)
        {
            var d = HaloShaderDef.Of(shader);
            Material m = null;
            if (d.Environment != null) m = BuildEnv(d, lightmap);
            else if (d.Object != null) m = BuildModel(d);
            else if (d.Transparent != null) m = BuildChicago(d);
            else if (d.Energy != null) m = BuildPlasma(d);
            else if (d.Gauge != null) m = BuildMeter(d);
            else if (d.Glassy != null || d.Watery != null) m = BuildGlass(d);
            if (m == null) m = BuildPlain(shader);
            if (m == null) return null;
            m.name = shader.Name + (sky ? " (sky)" : "");
            if (sky && m.HasProperty("_Sky"))
            {
                m.SetFloat("_Sky", 1f);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                if (m.HasProperty("_Unlit")) m.SetFloat("_Unlit", 1f);
                m.renderQueue = m.renderQueue >= 3000 ? SkyQueue + 2 : SkyQueue;
            }
            return m;
        }

        static Material New(string shaderName)
        {
            Shader s = HaloShaders.Find(shaderName);
            if (s == null) return null;
            return new Material(s) { enableInstancing = true };
        }

        static Material BuildEnv(HaloShaderDef d, Texture lightmap)
        {
            var e = d.Environment;
            var m = New(EnvShader);
            if (m == null) return null;
            m.SetTexture("_BaseMap", Tex(e.BaseMap, WhiteLinear));
            m.SetTexture("_Detail1", Tex(e.PrimaryDetail, Neutral));
            m.SetTexture("_Detail2", Tex(e.SecondaryDetail, Neutral));
            m.SetTexture("_Micro", Tex(e.MicroDetail, Neutral));
            bool bump = e.BumpMap != null;
            if (bump) m.SetTexture("_BumpMap", Tex(e.BumpMap, FlatBump));
            m.SetTexture("_LightMap", lightmap != null ? lightmap : WhiteLinear);
            m.SetVector("_Scales", new Vector4(NonZero(e.PrimaryDetailScale, 1f), NonZero(e.SecondaryDetailScale, 1f), NonZero(e.MicroDetailScale, 1f), NonZero(e.SelfIllumScale, 1f)));
            float bs = NonZero(e.BumpScale, 1f);
            m.SetVector("_BumpScale", new Vector4(bs * NonZero(e.BumpScaleX, 1f), bs * NonZero(e.BumpScaleY, 1f), 0f, 0f));
            m.SetVector("_Kind", new Vector4(e.Type, e.DetailFunction, e.MicroFunction, bump ? 1f : 0f));
            // alpha test source: Halo cuts alpha-tested environment shaders with the bump map's alpha (a30 trees_filler, a10 burn alpha
            // test, BG teleporter base whose base alpha is not a mask); a bump map whose alpha is all clear (c10_treeline) leaves it to the base
            float alphaTest = e.AlphaTested ? (bump && BumpHasAlpha(e.BumpMap) ? 2f : 1f) : 0f;
            m.SetVector("_Flags", new Vector4(alphaTest, (e.Flags & 2) != 0 ? 1f : 0f, lightmap != null ? 1f : 0f, e.SecondaryDetail != null ? 1f : 0f));
            m.SetVector("_UvAnim", new Vector4(e.UAnim.Function, NonZero(e.UAnim.Period, 1f), e.UAnim.Scale, 0f));
            m.SetVector("_VAnim", new Vector4(e.VAnim.Function, NonZero(e.VAnim.Period, 1f), e.VAnim.Scale, 0f));
            bool refl = e.ReflectionCube != null && (e.PerpendicularBrightness > 0f || e.ParallelBrightness > 0f);
            if (refl)
            {
                m.SetTexture("_ReflCube", Tex(e.ReflectionCube, null));
                float bright = NonZero(e.Brightness, 1f) * ((e.SpecularFlags & 1) != 0 ? 2f : 1f);
                m.SetVector("_Refl", new Vector4(1f, e.ReflectionType == 1 ? 0f : 1f, (e.SpecularFlags & 4) != 0 ? 1f : 0f, bright));
                m.SetColor("_PerpColor", Mul(e.PerpendicularColor, e.PerpendicularBrightness));
                m.SetColor("_ParColor", Mul(e.ParallelColor, e.ParallelBrightness));
            }
            else m.SetVector("_Refl", Vector4.zero);
            if (e.SelfIllumMap != null)
            {
                m.SetFloat("_SelfOn", 1f);
                m.SetTexture("_SelfMap", Tex(e.SelfIllumMap, BlackLinear));
                m.SetColor("_PrimaryOn", Col(e.PrimaryOn)); m.SetColor("_PrimaryOff", Col(e.PrimaryOff));
                m.SetColor("_SecondaryOn", Col(e.SecondaryOn)); m.SetColor("_SecondaryOff", Col(e.SecondaryOff));
                m.SetColor("_PlasmaOn", Col(e.PlasmaOn)); m.SetColor("_PlasmaOff", Col(e.PlasmaOff));
                m.SetVector("_SelfAnim0", Wave(e.PrimaryAnim)); m.SetVector("_SelfAnim1", Wave(e.SecondaryAnim)); m.SetVector("_SelfAnim2", Wave(e.PlasmaAnim));
            }
            m.SetFloat("_Cull", (float)CullMode.Back);
            m.renderQueue = e.AlphaTested ? 2450 : 2000;
            return m;
        }

        /// <summary>True when a bitmap's first image has any alpha >= 0.5 (its smallest mip level is checked: cheap).</summary>
        static bool BumpHasAlpha(HaloTag bitmap)
        {
            try
            {
                var bm = new HaloBitmap(bitmap);
                if (bm.Images.Count == 0) return false;
                var img = bm.Images[0];
                var raw = HaloBitmap.Raw(bitmap.Map, img, HaloTextures.BitmapsFor(bitmap.Map));
                int level = Mathf.Clamp(img.Mips - 2, 0, img.Mips);
                var px = HaloBitmap.DecodeRgba(img, raw, 0, level);
                for (int i = 3; i < px.Length; i += 4) if (px[i] >= 128) return true;
                return false;
            }
            catch (Exception) { return true; }
        }

        static Color Col(HVec3 v) { return new Color(v.X, v.Y, v.Z, 1f); }
        static Color Mul(HVec3 v, float k) { return new Color(v.X * k, v.Y * k, v.Z * k, 1f); }

        static Material BuildModel(HaloShaderDef d)
        {
            var o = d.Object;
            var m = New(ModelShader);
            if (m == null) return null;
            m.SetTexture("_BaseMap", Tex(o.BaseMap, WhiteLinear));
            bool multi = o.MultipurposeMap != null;
            if (multi) m.SetTexture("_MultiMap", Tex(o.MultipurposeMap, BlackLinear));
            bool detail = o.DetailMap != null;
            if (detail) m.SetTexture("_DetailMap", Tex(o.DetailMap, Neutral));
            float us = NonZero(o.UScale, 1f), vs = NonZero(o.VScale, 1f);
            float ds = NonZero(o.DetailScale, 1f), dvs = ds * NonZero(o.DetailVScale, 1f);
            m.SetVector("_UvScale", new Vector4(us, vs, ds, dvs));
            m.SetVector("_Kind", new Vector4(o.DetailFunction, o.DetailMask, o.XboxChannels ? 1f : 0f, multi ? 1f : 0f));
            bool refl = o.ReflectionCube != null && (o.PerpendicularBrightness > 0f || o.ParallelBrightness > 0f);
            m.SetVector("_Flags", new Vector4(o.AlphaTested ? 1f : 0f, detail ? 1f : 0f, o.ChangeColorSource != 0 && multi ? 1f : 0f, refl ? 1f : 0f));
            if (refl)
            {
                m.SetTexture("_ReflCube", Tex(o.ReflectionCube, null));
                m.SetColor("_PerpColor", Mul(o.PerpendicularTint, o.PerpendicularBrightness));
                m.SetColor("_ParColor", Mul(o.ParallelTint, o.ParallelBrightness));
            }
            // self-illumination: the animation colour between its bounds (source none = the shader's own animation)
            bool self = multi && (o.AnimLower.X + o.AnimLower.Y + o.AnimLower.Z + o.AnimUpper.X + o.AnimUpper.Y + o.AnimUpper.Z) > 0.001f;
            if (self)
            {
                m.SetColor("_SelfColor", Col(o.AnimLower)); m.SetColor("_SelfColor2", Col(o.AnimUpper));
                m.SetVector("_SelfAnim", new Vector4(o.ColorAnim.Function, NonZero(o.ColorAnim.Period, 1f), 0f, 1f));
            }
            m.SetVector("_UAnim", Wave(o.UAnim));
            m.SetVector("_VAnim", Wave(o.VAnim));
            m.SetColor("_ChangeColor", Color.white);
            m.SetFloat("_Cull", o.TwoSided ? (float)CullMode.Off : (float)CullMode.Back);
            m.renderQueue = o.AlphaTested ? 2450 : 2000;
            return m;
        }

        /// <summary>Sets Blend / BlendOp of a SomniumHalo/Transparent material for a Halo framebuffer blend function.</summary>
        public static void SetBlendPublic(Material m, int blend) { SetBlend(m, blend); }

        static void SetBlend(Material m, int blend)
        {
            BlendMode src = BlendMode.SrcAlpha, dst = BlendMode.OneMinusSrcAlpha; BlendOp op = BlendOp.Add;
            switch (blend)
            {
                case 1: src = BlendMode.DstColor; dst = BlendMode.Zero; break;
                case 2: src = BlendMode.DstColor; dst = BlendMode.Zero; break;
                case 3: src = BlendMode.One; dst = BlendMode.One; break;
                case 4: src = BlendMode.One; dst = BlendMode.One; op = BlendOp.ReverseSubtract; break;
                case 5: src = BlendMode.One; dst = BlendMode.One; op = BlendOp.Min; break;
                case 6: src = BlendMode.One; dst = BlendMode.One; op = BlendOp.Max; break;
                case 7: src = BlendMode.One; dst = BlendMode.OneMinusSrcAlpha; break;
            }
            m.SetFloat("_SrcBlend", (float)src); m.SetFloat("_DstBlend", (float)dst); m.SetFloat("_BlendOp", (float)op);
        }

        static Material BuildChicago(HaloShaderDef d)
        {
            var c = d.Transparent;
            var m = New(TransparentShader);
            if (m == null) return null;
            int n = Math.Min(4, c.Maps.Count);
            for (int i = 0; i < 4; i++)
            {
                var mp = i < n ? c.Maps[i] : null;
                string s = i.ToString();
                if (mp == null) { m.SetTexture("_Map" + s, WhiteLinear); continue; }
                if (i == 0 && c.FirstMapType > 0) m.SetTexture("_Cube0", Tex(mp.Map, null));
                else m.SetTexture("_Map" + s, Tex(mp.Map, WhiteLinear));
                m.SetVector("_M" + s, new Vector4(NonZero(mp.UScale, 1f), NonZero(mp.VScale, 1f), mp.UOffset, mp.VOffset));
                m.SetVector("_F" + s, new Vector4(mp.ColorFunction, mp.AlphaFunction, mp.Rotation, mp.Flags));
                m.SetVector("_UA" + s, Wave(mp.UAnim));
                m.SetVector("_VA" + s, Wave(mp.VAnim));
                m.SetVector("_RA" + s, Wave(mp.RotationAnim));
                if (i == 0) m.SetVector("_RC", new Vector4(mp.RotationCenterX, mp.RotationCenterY, 0.5f, 0.5f));
            }
            if (n > 1) { var r = m.GetVector("_RC"); r.z = c.Maps[1].RotationCenterX; r.w = c.Maps[1].RotationCenterY; m.SetVector("_RC", r); }
            m.SetVector("_Setup", new Vector4(Math.Max(1, n), c.FirstMapType, c.Blend, c.FadeMode));
            m.SetFloat("_Mode", 0f);
            m.SetFloat("_AlphaClip", c.AlphaTested ? 1f : 0f);
            SetBlend(m, c.Blend);
            m.SetFloat("_Cull", c.TwoSided ? (float)CullMode.Off : (float)CullMode.Back);
            // decals sit on the surface
            if ((c.Flags & 2) != 0) { m.SetFloat("_OffsetFactor", -1f); m.SetFloat("_OffsetUnits", -2f); }
            m.renderQueue = 3000 + ((c.Flags & 16) != 0 ? -10 : 0);
            return m;
        }

        static Material BuildPlasma(HaloShaderDef d)
        {
            var p = d.Energy;
            var m = New(TransparentShader);
            if (m == null) return null;
            m.SetTexture("_Map0", Tex(p.PrimaryNoise, WhiteLinear));
            m.SetTexture("_Map1", Tex(p.SecondaryNoise ?? p.PrimaryNoise, WhiteLinear));
            m.SetFloat("_Mode", 1f);
            m.SetColor("_PerpColor", Mul(p.PerpendicularTint, p.PerpendicularBrightness));
            m.SetColor("_ParColor", Mul(p.ParallelTint, p.ParallelBrightness));
            m.SetVector("_Plasma", new Vector4(NonZero(p.PrimaryPeriod, 1f), NonZero(p.PrimaryScale, 1f), NonZero(p.SecondaryPeriod, 1f), NonZero(p.SecondaryScale, 1f)));
            m.SetVector("_PlasmaDir0", V(p.PrimaryDirection, 0f));
            m.SetVector("_PlasmaDir1", V(p.SecondaryDirection, 0f));
            m.SetVector("_Setup", new Vector4(1f, 0f, 3f, 0f));
            SetBlend(m, 3);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.renderQueue = 3000;
            return m;
        }

        static Material BuildMeter(HaloShaderDef d)
        {
            var g = d.Gauge;
            var m = New(TransparentShader);
            if (m == null) return null;
            m.SetTexture("_Map0", Tex(g.Map, WhiteLinear));
            m.SetFloat("_Mode", 2f);
            m.SetColor("_MeterMin", Col(g.GradientMin)); m.SetColor("_MeterMax", Col(g.GradientMax));
            m.SetColor("_MeterBack", Col(g.Background)); m.SetColor("_MeterFlash", Col(g.Flash));
            m.SetColor("_Tint", (g.Tint.X + g.Tint.Y + g.Tint.Z) > 0.001f ? Col(g.Tint) : Color.white);
            m.SetVector("_Meter", new Vector4(1f, 1f - g.MeterTransparency, 1f - g.BackgroundTransparency, 0f));
            m.SetVector("_Setup", new Vector4(1f, 0f, 0f, 0f));
            SetBlend(m, 0);
            m.SetFloat("_Cull", (g.Flags & 2) != 0 ? (float)CullMode.Off : (float)CullMode.Back);
            if ((g.Flags & 1) != 0) { m.SetFloat("_OffsetFactor", -1f); m.SetFloat("_OffsetUnits", -2f); }
            m.renderQueue = 3000;
            return m;
        }

        static Material BuildGlass(HaloShaderDef d)
        {
            var m = New(GlassShader);
            if (m == null) return null;
            if (d.Glassy != null)
            {
                var g = d.Glassy;
                m.SetTexture("_TintMap", Tex(g.BackgroundTintMap, WhiteLinear));
                bool bump = g.BumpMap != null;
                if (bump) m.SetTexture("_BumpMap", Tex(g.BumpMap, FlatBump));
                m.SetTexture("_DiffuseMap", Tex(g.DiffuseMap, BlackLinear));
                m.SetTexture("_SpecMap", Tex(g.SpecularMap, BlackLinear));
                m.SetTexture("_ReflCube", Tex(g.ReflectionMap, null));
                m.SetColor("_BackTint", (g.BackgroundTint.X + g.BackgroundTint.Y + g.BackgroundTint.Z) > 0.001f ? Col(g.BackgroundTint) : Color.white);
                m.SetColor("_PerpColor", Mul(g.PerpendicularTint, g.PerpendicularBrightness));
                m.SetColor("_ParColor", Mul(g.ParallelTint, g.ParallelBrightness));
                m.SetVector("_Scales", new Vector4(NonZero(g.BackgroundTintScale, 1f), NonZero(g.BumpScale, 1f), NonZero(g.DiffuseScale, 1f), NonZero(g.SpecularScale, 1f)));
                m.SetVector("_Kind", new Vector4(0f, bump ? 1f : 0f, 0f, 0f));
                m.SetFloat("_Cull", (g.Flags & 4) != 0 ? (float)CullMode.Off : (float)CullMode.Back);
            }
            else
            {
                var w = d.Watery;
                m.SetTexture("_TintMap", Tex(w.BaseMap, WhiteLinear));
                bool ripple = w.RippleMaps != null;
                if (ripple) m.SetTexture("_BumpMap", Tex(w.RippleMaps, FlatBump));
                m.SetTexture("_ReflCube", Tex(w.ReflectionMap, null));
                m.SetColor("_BackTint", Color.white);
                m.SetColor("_PerpColor", Mul(w.PerpendicularTint, w.PerpendicularBrightness));
                m.SetColor("_ParColor", Mul(w.ParallelTint, w.ParallelBrightness));
                m.SetVector("_Scales", Vector4.one);
                m.SetVector("_Kind", new Vector4(1f, ripple ? 1f : 0f, (w.Flags & 1) != 0 ? 1f : 0f, (w.Flags & 2) != 0 ? 1f : 0f));
                m.SetVector("_Ripple", new Vector4(w.RippleAngle, w.RippleVelocity, NonZero(w.RippleScale, 1f), 2f));
                m.SetFloat("_Cull", (float)CullMode.Off);
            }
            m.renderQueue = 2990;
            return m;
        }

        /// <summary>The first version's look (URP Lit / Unlit with the main colour map) for when the Halo shaders are not available.</summary>
        static Material BuildPlain(HaloTag shader)
        {
            float us, vs; Kind kind;
            HaloTag map = MainMap(shader, out us, out vs, out kind);
            Texture tex = map != null ? HaloTextures.Get(map) : null;
            Material m = kind == Kind.Opaque || kind == Kind.Cutout ? NewLit() : NewUnlit(kind);
            if (tex != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                m.mainTexture = tex;
                m.mainTextureScale = new Vector2(us, vs);
                if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", new Vector2(us, vs));
            }
            if (shader.ClassName == "swat") SetColor(m, new Color(0.15f, 0.3f, 0.4f, 0.6f));
            if (kind == Kind.Cutout && m.HasProperty("_AlphaClip"))
            {
                m.SetFloat("_AlphaClip", 1f); m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = 2450;
            }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            return m;
        }

        public static Material NewLit()
        {
            Shader sh = HaloShaders.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            return new Material(sh);
        }

        public static Material NewUnlit(Kind kind)
        {
            Shader sh = HaloShaders.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent");
            var m = new Material(sh);
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", kind == Kind.Additive ? 2f : 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)(kind == Kind.Additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = 3000;
            }
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Tint")) m.SetColor("_Tint", c);
        }

        /// <summary>Destroys the cached materials of one map file.</summary>
        public static void Release(string mapPath)
        {
            var dead = new List<string>();
            foreach (var kv in Cache) if (kv.Key.StartsWith(mapPath + "|", StringComparison.Ordinal)) dead.Add(kv.Key);
            foreach (var k in dead) { if (Cache[k] != null && Cache[k] != _fallback) UnityEngine.Object.Destroy(Cache[k]); Cache.Remove(k); }
            dead.Clear();
            foreach (var kv in Extras) if (kv.Key.StartsWith(mapPath + "|", StringComparison.Ordinal)) dead.Add(kv.Key);
            foreach (var k in dead) Extras.Remove(k);
            HaloShaderDef.Release(mapPath);
        }
    }
}

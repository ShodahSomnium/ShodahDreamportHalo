using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A Halo shader tag read into plain numbers (one sub-object per shader class), for the Unity material builder (HaloMaterials)
    /// and anyone who needs a shader's facts. Offsets: DevTools/reference/layouts.txt (Shader base 40 bytes; ShaderEnvironment,
    /// ShaderModel, ShaderTransparentChicago(+Extended), ShaderTransparentGlass, ShaderTransparentWater, ShaderTransparentMeter,
    /// ShaderTransparentPlasma). Enum values as the tag definitions list them (see the comments). Unity-free.
    /// </summary>
    public sealed class HaloShaderDef
    {
        /// <summary>A periodic function on a colour or uv (WaveFunction: 0 one, 1 zero, 2 cosine, 3 cosine variable period,
        /// 4 diagonal wave, 5 diagonal variable, 6 slide, 7 slide variable, 8 noise, 9 jitter, 10 wander, 11 spark).</summary>
        public struct Wave { public int Function, Source; public float Period, Phase, Scale; }

        public sealed class Env
        {
            /// <summary>Flags: 1 alpha tested, 2 bump map is specular mask, 4 true atmospheric fog, 8 alternate bump attenuation, 16 alternate normal type blending.</summary>
            public int Flags;
            /// <summary>0 normal, 1 blended, 2 blended base specular.</summary>
            public int Type;
            /// <summary>Diffuse flags: 1 rescale detail maps, 2 rescale bump map.</summary>
            public int DiffuseFlags;
            public HaloTag BaseMap, PrimaryDetail, SecondaryDetail, MicroDetail, BumpMap, SelfIllumMap, ReflectionCube;
            public float PrimaryDetailScale, SecondaryDetailScale, MicroDetailScale, BumpScale, BumpScaleX, BumpScaleY, SelfIllumScale;
            /// <summary>0 double biased multiply, 1 multiply, 2 double biased add.</summary>
            public int DetailFunction, MicroFunction;
            public HVec3 MaterialColor;
            public Wave UAnim, VAnim;
            public int SelfIllumFlags;
            public HVec3 PrimaryOn, PrimaryOff, SecondaryOn, SecondaryOff, PlasmaOn, PlasmaOff;
            public Wave PrimaryAnim, SecondaryAnim, PlasmaAnim;
            /// <summary>Specular flags: 1 overbright, 2 extra shiny, 4 lightmap is specular.</summary>
            public int SpecularFlags;
            public float Brightness;
            public HVec3 PerpendicularColor, ParallelColor;
            /// <summary>Reflection type: 0 bumped cube map, 1 flat cube map, 2 bumped radiosity.</summary>
            public int ReflectionFlags, ReflectionType;
            public float LightmapBrightnessScale, PerpendicularBrightness, ParallelBrightness;
            public bool AlphaTested => (Flags & 1) != 0;
        }

        public sealed class Model
        {
            /// <summary>Flags: 1 detail after reflection, 2 two sided, 4 not alpha tested, 8 alpha blended decal, 16 true atmospheric fog,
            /// 32 disable two sided culling, 64 use xbox multipurpose channel order.</summary>
            public int Flags;
            public float Translucency;
            /// <summary>Change colour source (FunctionNameNullable: 0 none, 1 A, 2 B, 3 C, 4 D = the object's change colours).</summary>
            public int ChangeColorSource;
            public int MoreFlags;
            /// <summary>Self-illumination colour source and animation (lower .. upper bound).</summary>
            public int ColorSource;
            public Wave ColorAnim;
            public HVec3 AnimLower, AnimUpper;
            public float UScale, VScale;
            public HaloTag BaseMap, MultipurposeMap, DetailMap, ReflectionCube;
            /// <summary>0 double biased multiply, 1 multiply, 2 double biased add.</summary>
            public int DetailFunction;
            /// <summary>0 none, 1 reflection inverse, 2 reflection, 3 self-illum inverse, 4 self-illum, 5 change colour inverse, 6 change colour, 7 aux inverse, 8 aux.</summary>
            public int DetailMask;
            public float DetailScale, DetailVScale;
            public Wave UAnim, VAnim, RotationAnim;
            public float RotationCenterX, RotationCenterY;
            public float ReflectionFalloff, ReflectionCutoff, PerpendicularBrightness, ParallelBrightness;
            public HVec3 PerpendicularTint, ParallelTint;
            public bool TwoSided => (Flags & 2) != 0;
            public bool AlphaTested => (Flags & 4) == 0;
            public bool XboxChannels => (Flags & 64) != 0;
        }

        public sealed class ChicagoMap
        {
            /// <summary>Flags: 1 unfiltered, 2 alpha replicate, 4 u clamped, 8 v clamped.</summary>
            public int Flags;
            /// <summary>ShaderColorFunctionType: 0 current, 1 next map, 2 multiply, 3 double multiply, 4 add, 5 add signed current,
            /// 6 add signed next map, 7 subtract current, 8 subtract next map, 9 blend current alpha, 10 blend current alpha inverse,
            /// 11 blend next map alpha, 12 blend next map alpha inverse.</summary>
            public int ColorFunction, AlphaFunction;
            public float UScale, VScale, UOffset, VOffset, Rotation, MipBias;
            public HaloTag Map;
            public Wave UAnim, VAnim, RotationAnim;
            public float RotationCenterX, RotationCenterY;
        }

        public sealed class Chicago
        {
            public int NumericLimit;
            /// <summary>Flags: 1 alpha tested, 2 decal, 4 two sided, 8 first map in screen space, 16 draw before water, 32 ignore effect,
            /// 64 scale first map with distance, 128 numeric.</summary>
            public int Flags;
            /// <summary>0 2d, 1 reflection cube, 2 object centred cube, 3 viewer centred cube.</summary>
            public int FirstMapType;
            /// <summary>FramebufferBlendFunction: 0 alpha blend, 1 multiply, 2 double multiply, 3 add, 4 subtract, 5 component min,
            /// 6 component max, 7 alpha multiply add.</summary>
            public int Blend;
            /// <summary>0 none, 1 fade when perpendicular, 2 fade when parallel.</summary>
            public int FadeMode, FadeSource;
            public int ExtraFlags;
            public readonly List<ChicagoMap> Maps = new List<ChicagoMap>();
            public readonly List<HaloTag> ExtraLayers = new List<HaloTag>();
            public bool TwoSided => (Flags & 4) != 0;
            public bool AlphaTested => (Flags & 1) != 0;
        }

        public sealed class Glass
        {
            /// <summary>Flags: 1 alpha tested, 2 decal, 4 two sided, 8 bump map is specular mask.</summary>
            public int Flags;
            public HVec3 BackgroundTint; public float BackgroundTintScale; public HaloTag BackgroundTintMap;
            public int ReflectionType; public float PerpendicularBrightness, ParallelBrightness; public HVec3 PerpendicularTint, ParallelTint;
            public HaloTag ReflectionMap; public float BumpScale; public HaloTag BumpMap;
            public float DiffuseScale; public HaloTag DiffuseMap; public float DiffuseDetailScale; public HaloTag DiffuseDetailMap;
            public float SpecularScale; public HaloTag SpecularMap; public float SpecularDetailScale; public HaloTag SpecularDetailMap;
        }

        public sealed class Water
        {
            /// <summary>Flags: 1 base map alpha modulates reflection, 2 base map colour modulates background, 4 atmospheric fog, 8 draw before fog.</summary>
            public int Flags;
            public HaloTag BaseMap, ReflectionMap, RippleMaps;
            public float PerpendicularBrightness, ParallelBrightness; public HVec3 PerpendicularTint, ParallelTint;
            public float RippleAngle, RippleVelocity, RippleScale;
            public readonly List<float[]> Ripples = new List<float[]>(); // (angle, velocity, offset x, offset y, repeats, map index)
        }

        public sealed class Meter
        {
            /// <summary>Flags: 1 decal, 2 two sided, 4 flash colour is negative, 8 tint mode 2, 16 unfiltered.</summary>
            public int Flags;
            public HaloTag Map;
            public HVec3 GradientMin, GradientMax, Background, Flash, Tint;
            public float MeterTransparency, BackgroundTransparency;
            public int BrightnessSource, FlashSource, ValueSource, GradientSource, FlashExtensionSource;
        }

        public sealed class Plasma
        {
            public int IntensitySource; public float IntensityExponent; public int OffsetSource; public float OffsetAmount, OffsetExponent;
            public float PerpendicularBrightness, ParallelBrightness; public HVec3 PerpendicularTint, ParallelTint; public int TintSource;
            public float PrimaryPeriod; public HVec3 PrimaryDirection; public float PrimaryScale; public HaloTag PrimaryNoise;
            public float SecondaryPeriod; public HVec3 SecondaryDirection; public float SecondaryScale; public HaloTag SecondaryNoise;
        }

        public readonly HaloTag Tag;
        public readonly string Class;
        /// <summary>Shader flags: 1 simple parameterization, 2 ignore normals, 4 transparent lit.</summary>
        public readonly int ShaderFlags;
        public readonly int DetailLevel, MaterialType, ShaderType;
        public readonly float Power;
        public readonly HVec3 EmittedLight, LightTint;
        public Env Environment; public Model Object; public Chicago Transparent; public Glass Glassy; public Water Watery; public Meter Gauge; public Plasma Energy;

        static readonly Dictionary<string, HaloShaderDef> Cache = new Dictionary<string, HaloShaderDef>();

        /// <summary>The (cached) definition of a shader tag; null for null.</summary>
        public static HaloShaderDef Of(HaloTag tag)
        {
            if (tag == null) return null;
            string key = tag.Map.Path + "|" + tag.Id;
            lock (Cache)
            {
                HaloShaderDef d;
                if (Cache.TryGetValue(key, out d)) return d;
                d = new HaloShaderDef(tag);
                Cache[key] = d;
                return d;
            }
        }

        public static void Release(string mapPath)
        {
            lock (Cache)
            {
                var dead = new List<string>();
                foreach (var k in Cache.Keys) if (k.StartsWith(mapPath + "|", StringComparison.Ordinal)) dead.Add(k);
                foreach (var k in dead) Cache.Remove(k);
            }
        }

        static Wave W4(TagReader r, int sourceAt, int functionAt, int periodAt, int phaseAt, int scaleAt)
        {
            return new Wave
            {
                Source = sourceAt >= 0 ? r.U16(sourceAt) : 0, Function = r.U16(functionAt), Period = r.F32(periodAt),
                Phase = phaseAt >= 0 ? r.F32(phaseAt) : 0f, Scale = scaleAt >= 0 ? r.F32(scaleAt) : 0f,
            };
        }

        public HaloShaderDef(HaloTag tag)
        {
            Tag = tag;
            Class = tag.ClassName;
            var r = tag.Root;
            ShaderFlags = r.U16(0); DetailLevel = r.U16(2); Power = r.F32(4); EmittedLight = r.Vec3(8); LightTint = r.Vec3(20);
            MaterialType = r.U16(34); ShaderType = r.U16(36);
            switch (Class)
            {
                case "senv": ReadEnv(r); break;
                case "soso": ReadModel(r); break;
                case "schi": ReadChicago(r, 84); break;
                case "scex": ReadChicago(r, 84); if (Transparent.Maps.Count == 0) ReadChicagoMaps(r, 96); break;
                case "sgla": ReadGlass(r); break;
                case "swat": ReadWater(r); break;
                case "smet": ReadMeter(r); break;
                case "spla": ReadPlasma(r); break;
            }
        }

        void ReadEnv(TagReader r)
        {
            var e = new Env
            {
                Flags = r.U16(40), Type = r.U16(42), DiffuseFlags = r.U16(108), BaseMap = r.Ref(136),
                DetailFunction = r.U16(176), PrimaryDetailScale = r.F32(180), PrimaryDetail = r.Ref(184),
                SecondaryDetailScale = r.F32(200), SecondaryDetail = r.Ref(204),
                MicroFunction = r.U16(244), MicroDetailScale = r.F32(248), MicroDetail = r.Ref(252),
                MaterialColor = r.Vec3(268), BumpScale = r.F32(292), BumpMap = r.Ref(296), BumpScaleX = r.F32(312), BumpScaleY = r.F32(316),
                UAnim = W4(r, -1, 336, 340, -1, 344), VAnim = W4(r, -1, 348, 352, -1, 356),
                SelfIllumFlags = r.U16(384),
                PrimaryOn = r.Vec3(412), PrimaryOff = r.Vec3(424), PrimaryAnim = W4(r, -1, 436, 440, 444, -1),
                SecondaryOn = r.Vec3(472), SecondaryOff = r.Vec3(484), SecondaryAnim = W4(r, -1, 496, 500, 504, -1),
                PlasmaOn = r.Vec3(532), PlasmaOff = r.Vec3(544), PlasmaAnim = W4(r, -1, 556, 560, 564, -1),
                SelfIllumScale = r.F32(592), SelfIllumMap = r.Ref(596),
                SpecularFlags = r.U16(636), Brightness = r.F32(656), PerpendicularColor = r.Vec3(680), ParallelColor = r.Vec3(692),
                ReflectionFlags = r.U16(720), ReflectionType = r.U16(722), LightmapBrightnessScale = r.F32(724),
                PerpendicularBrightness = r.F32(756), ParallelBrightness = r.F32(760), ReflectionCube = r.Ref(804),
            };
            Environment = e;
        }

        void ReadModel(TagReader r)
        {
            Object = new Model
            {
                Flags = r.U16(40), Translucency = r.F32(56), ChangeColorSource = r.U16(76), MoreFlags = r.U16(108),
                ColorSource = r.U16(112), ColorAnim = W4(r, -1, 114, 116, -1, -1), AnimLower = r.Vec3(120), AnimUpper = r.Vec3(132),
                UScale = r.F32(156), VScale = r.F32(160), BaseMap = r.Ref(164), MultipurposeMap = r.Ref(188),
                DetailFunction = r.U16(212), DetailMask = r.U16(214), DetailScale = r.F32(216), DetailMap = r.Ref(220), DetailVScale = r.F32(236),
                UAnim = W4(r, 252, 254, 256, 260, 264), VAnim = W4(r, 268, 270, 272, 276, 280), RotationAnim = W4(r, 284, 286, 288, 292, 296),
                RotationCenterX = r.F32(300), RotationCenterY = r.F32(304),
                ReflectionFalloff = r.F32(316), ReflectionCutoff = r.F32(320), PerpendicularBrightness = r.F32(324), PerpendicularTint = r.Vec3(328),
                ParallelBrightness = r.F32(340), ParallelTint = r.Vec3(344), ReflectionCube = r.Ref(356),
            };
        }

        void ReadChicago(TagReader r, int mapsAt)
        {
            var c = new Chicago
            {
                NumericLimit = r.U8(40), Flags = r.U8(41), FirstMapType = r.U16(42), Blend = r.U16(44), FadeMode = r.U16(46), FadeSource = r.U16(48),
                ExtraFlags = (int)r.U32(Class == "scex" ? 108 : 96),
            };
            foreach (var l in r.Block(72, 16)) { var t = l.Ref(0); if (t != null) c.ExtraLayers.Add(t); }
            Transparent = c;
            ReadChicagoMaps(r, mapsAt);
        }

        void ReadChicagoMaps(TagReader r, int mapsAt)
        {
            foreach (var m in r.Block(mapsAt, 220))
            {
                Transparent.Maps.Add(new ChicagoMap
                {
                    Flags = m.U16(0), ColorFunction = m.U16(44), AlphaFunction = m.U16(46),
                    UScale = m.F32(84), VScale = m.F32(88), UOffset = m.F32(92), VOffset = m.F32(96), Rotation = m.F32(100), MipBias = m.F32(104),
                    Map = m.Ref(108),
                    UAnim = W4(m, 164, 166, 168, 172, 176), VAnim = W4(m, 180, 182, 184, 188, 192), RotationAnim = W4(m, 196, 198, 200, 204, 208),
                    RotationCenterX = m.F32(212), RotationCenterY = m.F32(216),
                });
            }
        }

        void ReadGlass(TagReader r)
        {
            Glassy = new Glass
            {
                Flags = r.U16(40), BackgroundTint = r.Vec3(84), BackgroundTintScale = r.F32(96), BackgroundTintMap = r.Ref(100),
                ReflectionType = r.U16(138), PerpendicularBrightness = r.F32(140), PerpendicularTint = r.Vec3(144),
                ParallelBrightness = r.F32(156), ParallelTint = r.Vec3(160), ReflectionMap = r.Ref(172), BumpScale = r.F32(188), BumpMap = r.Ref(192),
                DiffuseScale = r.F32(340), DiffuseMap = r.Ref(344), DiffuseDetailScale = r.F32(360), DiffuseDetailMap = r.Ref(364),
                SpecularScale = r.F32(412), SpecularMap = r.Ref(416), SpecularDetailScale = r.F32(432), SpecularDetailMap = r.Ref(436),
            };
        }

        void ReadWater(TagReader r)
        {
            var w = new Water
            {
                Flags = r.U16(40), BaseMap = r.Ref(76), PerpendicularBrightness = r.F32(108), PerpendicularTint = r.Vec3(112),
                ParallelBrightness = r.F32(124), ParallelTint = r.Vec3(128), ReflectionMap = r.Ref(156),
                RippleAngle = r.F32(188), RippleVelocity = r.F32(192), RippleScale = r.F32(196), RippleMaps = r.Ref(200),
            };
            foreach (var p in r.Block(292, 76)) w.Ripples.Add(new float[] { p.F32(40), p.F32(44), p.F32(48), p.F32(52), p.U16(56), p.I16(58) });
            Watery = w;
        }

        void ReadMeter(TagReader r)
        {
            Gauge = new Meter
            {
                Flags = r.U16(40), Map = r.Ref(76), GradientMin = r.Vec3(124), GradientMax = r.Vec3(136), Background = r.Vec3(148), Flash = r.Vec3(160),
                Tint = r.Vec3(172), MeterTransparency = r.F32(184), BackgroundTransparency = r.F32(188),
                BrightnessSource = r.U16(216), FlashSource = r.U16(218), ValueSource = r.U16(220), GradientSource = r.U16(222), FlashExtensionSource = r.U16(224),
            };
        }

        void ReadPlasma(TagReader r)
        {
            Energy = new Plasma
            {
                IntensitySource = r.U16(44), IntensityExponent = r.F32(48), OffsetSource = r.U16(52), OffsetAmount = r.F32(56), OffsetExponent = r.F32(60),
                PerpendicularBrightness = r.F32(96), PerpendicularTint = r.Vec3(100), ParallelBrightness = r.F32(112), ParallelTint = r.Vec3(116),
                TintSource = r.U16(128), PrimaryPeriod = r.F32(192), PrimaryDirection = r.Vec3(196), PrimaryScale = r.F32(208), PrimaryNoise = r.Ref(212),
                SecondaryPeriod = r.F32(264), SecondaryDirection = r.Vec3(268), SecondaryScale = r.F32(280), SecondaryNoise = r.Ref(284),
            };
        }
    }
}

// SomniumHalo/Env - Halo's shader_environment (senv): the structure BSP's surfaces (and the few scenery / sky parts that use it).
// Written by Game/World/HaloMaterials from the shader tag (Data/Shaders/HaloShaderDef). No keywords (nothing for the upload to strip):
// float switches only. Passes: Forward + DepthOnly + DepthNormals (depth priming is forced in the client), stereo instancing in each.
//
// The game's maths, in gamma space on the raw texels (see HaloCommon.hlsl):
//   detail  = lerp(primary, secondary, blend)   blend = secondary detail alpha (type normal) or base map alpha (blended types)
//   colour  = base (x) detail  then (x) micro detail, each by its function: double biased multiply 2ab, multiply ab, double biased add a+2b-1
//   light   = lightmap (uv2) x bump attenuation (bump normal against the lightmap's incident radiosity direction), or the object light
//             when the surface has no lightmap
//   + reflection = cube map along the (bumped) mirror direction x perpendicular..parallel tint x specular mask (x lightmap if flagged)
//   + self-illumination = map r/g/b x primary / secondary / plasma colours (animated between off and on)
//   then the atmospheric / planar fog.
Shader "SomniumHalo/Env"
{
    Properties
    {
        _BaseMap ("Base map", 2D) = "white" {}
        _Detail1 ("Primary detail map", 2D) = "grey" {}
        _Detail2 ("Secondary detail map", 2D) = "grey" {}
        _Micro ("Micro detail map", 2D) = "grey" {}
        _BumpMap ("Bump map (normal map)", 2D) = "bump" {}
        _LightMap ("Lightmap", 2D) = "white" {}
        _SelfMap ("Self-illumination map", 2D) = "black" {}
        _ReflCube ("Reflection cube map", Cube) = "black" {}
        _Scales ("Scales: primary, secondary, micro, self-illumination", Vector) = (1, 1, 1, 1)
        _BumpScale ("Bump scale xy", Vector) = (1, 1, 0, 0)
        _Kind ("x type (0 normal, 1 blended, 2 blended base specular), y detail function, z micro function, w bump present", Vector) = (0, 0, 0, 0)
        _Flags ("x alpha tested, y bump is specular mask, z has lightmap, w has secondary detail", Vector) = (0, 0, 1, 0)
        _UvAnim ("u anim: function, period, scale (offset = scale x wave)", Vector) = (0, 1, 0, 0)
        _VAnim ("v anim: function, period, scale", Vector) = (0, 1, 0, 0)
        _Refl ("Reflection: x on, y bumped (0 flat), z lightmap is specular, w brightness", Vector) = (0, 1, 0, 1)
        _PerpColor ("Perpendicular tint x brightness", Color) = (0, 0, 0, 1)
        _ParColor ("Parallel tint x brightness", Color) = (0, 0, 0, 1)
        _SelfOn ("Self-illumination on", Float) = 0
        _PrimaryOn ("Primary on", Color) = (0, 0, 0, 1)
        _PrimaryOff ("Primary off", Color) = (0, 0, 0, 1)
        _SecondaryOn ("Secondary on", Color) = (0, 0, 0, 1)
        _SecondaryOff ("Secondary off", Color) = (0, 0, 0, 1)
        _PlasmaOn ("Plasma on", Color) = (0, 0, 0, 1)
        _PlasmaOff ("Plasma off", Color) = (0, 0, 0, 1)
        _SelfAnim0 ("Primary anim: function, period, phase", Vector) = (0, 1, 0, 0)
        _SelfAnim1 ("Secondary anim: function, period, phase", Vector) = (0, 1, 0, 0)
        _SelfAnim2 ("Plasma anim: function, period, phase", Vector) = (0, 1, 0, 0)
        _ObjAmb ("Object light: ambient rgb, w = set (else the map default)", Vector) = (0, 0, 0, 0)
        _ObjL0Dir ("Object light 0 direction (towards the light)", Vector) = (0, 1, 0, 0)
        _ObjL0Col ("Object light 0 colour", Vector) = (0, 0, 0, 0)
        _ObjL1Dir ("Object light 1 direction", Vector) = (0, 1, 0, 0)
        _ObjL1Col ("Object light 1 colour", Vector) = (0, 0, 0, 0)
        _Sky ("Sky geometry (drawn around the eye at the far plane)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _ZWrite ("ZWrite", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "HaloCommon.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_Detail1); SAMPLER(sampler_Detail1);
        TEXTURE2D(_Detail2); SAMPLER(sampler_Detail2);
        TEXTURE2D(_Micro); SAMPLER(sampler_Micro);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_LightMap); SAMPLER(sampler_LightMap);
        TEXTURE2D(_SelfMap); SAMPLER(sampler_SelfMap);
        TEXTURECUBE(_ReflCube); SAMPLER(sampler_ReflCube);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _Scales;
            float4 _BumpScale;
            float4 _Kind;
            float4 _Flags;
            float4 _UvAnim;
            float4 _VAnim;
            float4 _Refl;
            half4 _PerpColor;
            half4 _ParColor;
            half _SelfOn;
            half4 _PrimaryOn;
            half4 _PrimaryOff;
            half4 _SecondaryOn;
            half4 _SecondaryOff;
            half4 _PlasmaOn;
            half4 _PlasmaOff;
            float4 _SelfAnim0;
            float4 _SelfAnim1;
            float4 _SelfAnim2;
            float4 _ObjAmb;
            float4 _ObjL0Dir;
            float4 _ObjL0Col;
            float4 _ObjL1Dir;
            float4 _ObjL1Col;
            float _Sky;
            float _Cull;
            float _ZWrite;
        CBUFFER_END

        float2 EnvUv(float2 uv)
        {
            float2 o = float2(0, 0);
            if (_UvAnim.z != 0) o.x = _UvAnim.z * HaloWave(_UvAnim.x, _UvAnim.y, 0);
            if (_VAnim.z != 0) o.y = _VAnim.z * HaloWave(_VAnim.x, _VAnim.y, 0);
            return TRANSFORM_TEX(uv, _BaseMap) + o;
        }

        float EnvAlpha(float2 uv)
        {
            if (_Flags.x > 1.5) return SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv * _BumpScale.xy).a;
            return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a;
        }

        void EnvAlphaTest(float2 uv)
        {
            // Halo's alpha-tested environment shaders cut with the BUMP map's alpha (a30 trees_filler: base DXT1 opaque, bump alpha =
            // the tree silhouettes); without a bump map the base map's alpha
            if (_Flags.x > 0.5) clip(EnvAlpha(uv) - 0.5);
        }

        half3 HaloDetail(half3 c, half3 d, float f)
        {
            if (f < 0.5) return c * d * 2.0h;
            if (f < 1.5) return c * d;
            return c + 2.0h * d - 1.0h;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite [_ZWrite]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;      // lightmap uv
                float3 binormalOS : TEXCOORD2; // Halo binormal (as stored)
                float3 incidentOS : TEXCOORD3; // lightmap vertex: incident radiosity direction
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 tangentWS : TEXCOORD3;
                float3 binormalWS : TEXCOORD4;
                float3 incidentWS : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS;
                o.positionCS = HaloClipPos(input.positionOS.xyz, _Sky, positionWS);
                o.positionWS = positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.tangentWS = TransformObjectToWorldDir(input.tangentOS.xyz, false);
                o.binormalWS = TransformObjectToWorldDir(input.binormalOS, false);
                o.incidentWS = TransformObjectToWorldDir(input.incidentOS, false);
                o.uv = float4(EnvUv(input.uv0), input.uv1);
                return o;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv.xy;
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                if (_Flags.x > 0.5) clip(EnvAlpha(uv) - 0.5);

                half4 d1 = SAMPLE_TEXTURE2D(_Detail1, sampler_Detail1, uv * _Scales.x);
                half4 d2 = SAMPLE_TEXTURE2D(_Detail2, sampler_Detail2, uv * _Scales.y);
                half blend = _Kind.x < 0.5 ? d2.a : base.a;
                if (_Flags.w < 0.5) blend = 0.0h;
                half4 detail = lerp(d1, d2, blend);
                half4 micro = SAMPLE_TEXTURE2D(_Micro, sampler_Micro, uv * _Scales.z);
                half3 colour = HaloDetail(base.rgb, detail.rgb, _Kind.y);
                colour = HaloDetail(colour, micro.rgb, _Kind.z);
                colour = saturate(colour);
                half specMask = (_Kind.x > 1.5 ? base.a : detail.a) * micro.a;

                half3 n = SafeNormalize(input.normalWS);
                n = IS_FRONT_VFACE(face, n, -n);
                half3 nb = n;
                half4 bump = half4(0.5h, 0.5h, 1.0h, 1.0h);
                if (_Kind.w > 0.5)
                {
                    bump = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv * _BumpScale.xy);
                    half3 t = bump.xyz * 2.0h - 1.0h;
                    nb = SafeNormalize(input.tangentWS * t.x + input.binormalWS * t.y + n * t.z);
                    if (_Flags.y > 0.5) specMask = bump.a;
                }

                half3 light;
                half3 lm = half3(1, 1, 1);
                if (_Flags.z > 0.5)
                {
                    lm = SAMPLE_TEXTURE2D(_LightMap, sampler_LightMap, input.uv.zw).rgb;
                    half att = 1.0h;
                    if (_Kind.w > 0.5)
                    {
                        half3 L = SafeNormalize(input.incidentWS);
                        half flat = saturate(dot(n, L));
                        att = saturate(dot(nb, L)) / max(flat, 0.25h);
                        att = min(att, 1.25h);
                    }
                    light = lm * att;
                }
                else
                {
                    half3 amb = _ObjAmb.w > 0.5 ? (half3)_ObjAmb.rgb : (half3)_HaloAmbient.rgb;
                    float3 l0 = _ObjAmb.w > 0.5 ? _ObjL0Dir.xyz : _HaloSunDir.xyz;
                    half3 c0 = _ObjAmb.w > 0.5 ? (half3)_ObjL0Col.rgb : (half3)_HaloSunColor.rgb;
                    float3 l1 = _ObjAmb.w > 0.5 ? _ObjL1Dir.xyz : float3(0, 1, 0);
                    half3 c1 = _ObjAmb.w > 0.5 ? (half3)_ObjL1Col.rgb : (half3)_HaloSkyLight.rgb;
                    light = amb + c0 * saturate(dot(nb, (half3)l0)) + c1 * saturate(dot(nb, (half3)l1));
                    if (_Sky > 0.5) light = half3(1, 1, 1);
                }
                half3 result = colour * light;

                if (_Refl.x > 0.5)
                {
                    float3 V = SafeNormalize(GetCameraPositionWS() - input.positionWS);
                    half3 rn = _Refl.y > 0.5 ? nb : n;
                    float3 R = reflect(-V, rn);
                    half3 env = SAMPLE_TEXTURECUBE(_ReflCube, sampler_ReflCube, float3(R.x, R.z, R.y)).rgb;   // Halo axes
                    half ndv = saturate(dot(rn, (half3)V));
                    half f = (1.0h - ndv); f *= f;
                    half3 tint = lerp(_PerpColor.rgb, _ParColor.rgb, f) * (half)_Refl.w;
                    half3 r = env * tint * specMask;
                    if (_Refl.z > 0.5) r *= lm;
                    result += r;
                }

                if (_SelfOn > 0.5)
                {
                    half4 s = SAMPLE_TEXTURE2D(_SelfMap, sampler_SelfMap, uv * _Scales.w);
                    half3 p = lerp(_PrimaryOff.rgb, _PrimaryOn.rgb, (half)HaloWave(_SelfAnim0.x, _SelfAnim0.y, _SelfAnim0.z));
                    half3 q = lerp(_SecondaryOff.rgb, _SecondaryOn.rgb, (half)HaloWave(_SelfAnim1.x, _SelfAnim1.y, _SelfAnim1.z));
                    half pw = (half)HaloWave(_SelfAnim2.x, _SelfAnim2.y, _SelfAnim2.z);
                    half plasma = saturate(1.0h - abs(s.a - pw) * 4.0h);
                    half3 pl = lerp(_PlasmaOff.rgb, _PlasmaOn.rgb, plasma);
                    result += s.r * p + s.g * q + s.b * pl;
                }

                if (_Sky < 0.5) result = HaloFog(result, input.positionWS);
                return half4(HaloOut3(result), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull [_Cull]
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings o = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS;
                o.positionCS = HaloClipPos(input.positionOS.xyz, _Sky, positionWS);
                o.uv = EnvUv(input.uv0);
                return o;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                EnvAlphaTest(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DNVaryings DepthNormalsVert(DNAttributes input)
            {
                DNVaryings o = (DNVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS;
                o.positionCS = HaloClipPos(input.positionOS.xyz, _Sky, positionWS);
                o.uv = EnvUv(input.uv0);
                o.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(input.normalOS));
                return o;
            }

            void DepthNormalsFrag(DNVaryings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
                )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                EnvAlphaTest(input.uv);
                half3 n = NormalizeNormalPerPixel(input.normalWS);
                outNormalWS = half4(IS_FRONT_VFACE(face, n, -n), 0.0);
                #ifdef _WRITE_RENDERING_LAYERS
                    outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}

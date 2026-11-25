// SomniumHalo/Model - Halo's shader_model (soso): every object (weapons, vehicles, bipeds, scenery, sky models).
// Written by Game/World/HaloMaterials. No keywords, float switches only; Forward + DepthOnly + DepthNormals + ShadowCaster.
//
// The game's maths, in gamma space on raw texels (HaloCommon.hlsl):
//   multipurpose map channels: PC order r = auxiliary (detail mask), g = self-illumination, b = specular reflection, a = change colour;
//   with the "xbox channel order" flag r = reflection, g = self-illumination, b = change colour, a = auxiliary
//   colour = base (x) detail (by its function, masked by the detail mask), x lerp(1, change colour, change colour mask)
//   light  = object light (ambient + two distant lights, set per object from the lightmap under it, else the map default)
//            + self-illumination colour x its mask
//   + reflection cube x perpendicular..parallel tint x reflection mask; then the fog.
Shader "SomniumHalo/Model"
{
    Properties
    {
        _BaseMap ("Base map", 2D) = "white" {}
        _MultiMap ("Multipurpose map", 2D) = "black" {}
        _DetailMap ("Detail map", 2D) = "grey" {}
        _ReflCube ("Reflection cube map", Cube) = "black" {}
        _UvScale ("Map uv scale (xy), detail scale (zw)", Vector) = (1, 1, 1, 1)
        _Kind ("x detail function, y detail mask, z xbox channel order, w has multipurpose", Vector) = (0, 0, 0, 0)
        _Flags ("x alpha tested, y has detail, z change colour on, w reflection on", Vector) = (0, 0, 0, 0)
        _ChangeColor ("Change colour (the object's)", Color) = (1, 1, 1, 1)
        _SelfColor ("Self-illumination colour lower", Color) = (0, 0, 0, 1)
        _SelfColor2 ("Self-illumination colour upper", Color) = (0, 0, 0, 1)
        _SelfAnim ("Self-illumination anim: function, period, phase, on", Vector) = (0, 1, 0, 0)
        _UAnim ("u anim: function, period, phase, scale", Vector) = (0, 1, 0, 0)
        _VAnim ("v anim: function, period, phase, scale", Vector) = (0, 1, 0, 0)
        _PerpColor ("Perpendicular tint x brightness", Color) = (0, 0, 0, 1)
        _ParColor ("Parallel tint x brightness", Color) = (0, 0, 0, 1)
        _Tint ("Tint (multiplied, for effects / fades)", Color) = (1, 1, 1, 1)
        _ObjAmb ("Object light: ambient rgb, w = set (else the map default)", Vector) = (0, 0, 0, 0)
        _ObjL0Dir ("Object light 0 direction (towards the light)", Vector) = (0, 1, 0, 0)
        _ObjL0Col ("Object light 0 colour", Vector) = (0, 0, 0, 0)
        _ObjL1Dir ("Object light 1 direction", Vector) = (0, 1, 0, 0)
        _ObjL1Col ("Object light 1 colour", Vector) = (0, 0, 0, 0)
        _Unlit ("Unlit (sky models: base x self only)", Float) = 0
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
        TEXTURE2D(_MultiMap); SAMPLER(sampler_MultiMap);
        TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);
        TEXTURECUBE(_ReflCube); SAMPLER(sampler_ReflCube);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _UvScale;
            float4 _Kind;
            float4 _Flags;
            half4 _ChangeColor;
            half4 _SelfColor;
            half4 _SelfColor2;
            float4 _SelfAnim;
            float4 _UAnim;
            float4 _VAnim;
            half4 _PerpColor;
            half4 _ParColor;
            half4 _Tint;
            float4 _ObjAmb;
            float4 _ObjL0Dir;
            float4 _ObjL0Col;
            float4 _ObjL1Dir;
            float4 _ObjL1Col;
            float _Unlit;
            float _Sky;
            float _Cull;
            float _ZWrite;
        CBUFFER_END

        float2 ModelUv(float2 uv)
        {
            float2 o = float2(0, 0);
            if (_UAnim.w != 0) o.x = _UAnim.w * HaloWave(_UAnim.x, _UAnim.y, _UAnim.z);
            if (_VAnim.w != 0) o.y = _VAnim.w * HaloWave(_VAnim.x, _VAnim.y, _VAnim.z);
            return uv * _UvScale.xy + o;
        }

        void ModelAlphaTest(float2 uv)
        {
            if (_Flags.x > 0.5) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).a - 0.5);
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
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
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
                o.uv = ModelUv(input.uv0);
                return o;
            }

            half3 HaloDetail(half3 c, half3 d, float f)
            {
                if (f < 0.5) return c * d * 2.0h;
                if (f < 1.5) return c * d;
                return c + 2.0h * d - 1.0h;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv;
                half4 base = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                if (_Flags.x > 0.5) clip(base.a - 0.5h);
                half4 m = _Kind.w > 0.5 ? SAMPLE_TEXTURE2D(_MultiMap, sampler_MultiMap, uv) : half4(0, 0, 0, 0);
                half aux, self, spec, cc;
                if (_Kind.z > 0.5) { spec = m.r; self = m.g; cc = m.b; aux = m.a; }
                else { aux = m.r; self = m.g; spec = m.b; cc = m.a; }

                half3 colour = base.rgb;
                if (_Flags.y > 0.5)
                {
                    half3 d = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, uv * _UvScale.zw).rgb;
                    half mask = 1.0h;
                    float dm = _Kind.y;
                    if (dm > 0.5 && dm < 1.5) mask = 1.0h - spec;
                    else if (dm < 2.5 && dm > 0.5) mask = spec;
                    else if (dm < 3.5 && dm > 0.5) mask = 1.0h - self;
                    else if (dm < 4.5 && dm > 0.5) mask = self;
                    else if (dm < 5.5 && dm > 0.5) mask = 1.0h - cc;
                    else if (dm < 6.5 && dm > 0.5) mask = cc;
                    else if (dm < 7.5 && dm > 0.5) mask = 1.0h - aux;
                    else if (dm > 0.5) mask = aux;
                    colour = lerp(colour, saturate(HaloDetail(colour, d, _Kind.x)), mask);
                }
                if (_Flags.z > 0.5) colour *= lerp(half3(1, 1, 1), _ChangeColor.rgb, cc);
                colour *= _Tint.rgb;

                half3 n = SafeNormalize(input.normalWS);
                n = IS_FRONT_VFACE(face, n, -n);
                half3 selfColour = half3(0, 0, 0);
                if (_SelfAnim.w > 0.5) selfColour = lerp(_SelfColor.rgb, _SelfColor2.rgb, (half)HaloWave(_SelfAnim.x, _SelfAnim.y, _SelfAnim.z)) * self;

                half3 result;
                if (_Unlit > 0.5)
                {
                    result = colour * (1.0h + selfColour);
                }
                else
                {
                    half3 amb = _ObjAmb.w > 0.5 ? (half3)_ObjAmb.rgb : (half3)_HaloAmbient.rgb;
                    float3 l0 = _ObjAmb.w > 0.5 ? _ObjL0Dir.xyz : _HaloSunDir.xyz;
                    half3 c0 = _ObjAmb.w > 0.5 ? (half3)_ObjL0Col.rgb : (half3)_HaloSunColor.rgb;
                    float3 l1 = _ObjAmb.w > 0.5 ? _ObjL1Dir.xyz : float3(0, 1, 0);
                    half3 c1 = _ObjAmb.w > 0.5 ? (half3)_ObjL1Col.rgb : (half3)_HaloSkyLight.rgb;
                    half3 light = amb + c0 * saturate(dot(n, (half3)l0)) + c1 * saturate(dot(n, (half3)l1)) + selfColour;
                    result = colour * light;
                }

                if (_Flags.w > 0.5)
                {
                    float3 V = SafeNormalize(GetCameraPositionWS() - input.positionWS);
                    float3 R = reflect(-V, n);
                    half3 env = SAMPLE_TEXTURECUBE(_ReflCube, sampler_ReflCube, float3(R.x, R.z, R.y)).rgb;
                    half ndv = saturate(dot(n, (half3)V));
                    half f = 1.0h - ndv; f *= f;
                    result += env * lerp(_PerpColor.rgb, _ParColor.rgb, f) * spec;
                }

                if (_Sky < 0.5) result = HaloFog(result, input.positionWS);
                return half4(HaloOut3(result), 1.0h);
            }
            ENDHLSL
        }

        // Shadows (for the sun of dynamic objects). Shadow maps are not per eye.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull [_Cull]
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings o = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                if (_Sky > 0.5) positionCS = float4(0, 0, -10, 1);
                o.positionCS = positionCS;
                o.uv = ModelUv(input.uv0);
                return o;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ModelAlphaTest(input.uv);
                return 0;
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
                o.uv = ModelUv(input.uv0);
                return o;
            }

            half DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ModelAlphaTest(input.uv);
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
                o.uv = ModelUv(input.uv0);
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
                ModelAlphaTest(input.uv);
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

// SomniumHalo/Glass - Halo's shader_transparent_glass (sgla) and shader_transparent_water (swat). Written by Game/World/HaloMaterials.
// Two passes, as the game draws them: first the background is multiplied by the tint (glass: background tint colour x tint map;
// water: the base map colour when "base map colour modulates background"), then the reflection (cube map along the mirror
// direction of the bumped / rippled normal, x perpendicular..parallel tint, water: x base map alpha when flagged) and the diffuse /
// specular maps are added. URP draws "SRPDefaultUnlit" before "UniversalForward" for each object. No keywords.
Shader "SomniumHalo/Glass"
{
    Properties
    {
        _TintMap ("Background tint map (glass) / base map (water)", 2D) = "white" {}
        _BumpMap ("Bump map (glass) / ripple map (water)", 2D) = "bump" {}
        _DiffuseMap ("Diffuse map", 2D) = "black" {}
        _SpecMap ("Specular map", 2D) = "black" {}
        _ReflCube ("Reflection cube map", Cube) = "black" {}
        _BackTint ("Background tint colour", Color) = (1, 1, 1, 1)
        _PerpColor ("Perpendicular tint x brightness", Color) = (0.2, 0.2, 0.2, 1)
        _ParColor ("Parallel tint x brightness", Color) = (0.6, 0.6, 0.6, 1)
        _Scales ("x tint map scale, y bump scale, z diffuse scale, w specular scale", Vector) = (1, 1, 1, 1)
        _Kind ("x 0 glass 1 water, y has bump, z water: base alpha modulates reflection, w water: base colour modulates background", Vector) = (0, 0, 0, 0)
        _Ripple ("Water ripples: x angle (rad), y velocity, z scale, w second layer scale", Vector) = (0, 0, 1, 2)
        _Sky ("Sky geometry", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "HaloCommon.hlsl"

        TEXTURE2D(_TintMap); SAMPLER(sampler_TintMap);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_DiffuseMap); SAMPLER(sampler_DiffuseMap);
        TEXTURE2D(_SpecMap); SAMPLER(sampler_SpecMap);
        TEXTURECUBE(_ReflCube); SAMPLER(sampler_ReflCube);

        CBUFFER_START(UnityPerMaterial)
            float4 _TintMap_ST;
            half4 _BackTint;
            half4 _PerpColor;
            half4 _ParColor;
            float4 _Scales;
            float4 _Kind;
            float4 _Ripple;
            float _Sky;
            float _Cull;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv0 : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            float3 normalWS : TEXCOORD2;
            float4 tangentWS : TEXCOORD3;
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
            o.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz, false), input.tangentOS.w);
            o.uv = input.uv0;
            return o;
        }

        half3 SurfaceNormal(Varyings input, half3 n)
        {
            if (_Kind.y < 0.5) return n;
            half3 t;
            if (_Kind.x > 0.5)
            {
                // water: two ripple layers sliding along the ripple angle
                float2 dir = float2(cos(_Ripple.x), sin(_Ripple.x)) * _Ripple.y * _HaloTime;
                half3 a = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv * _Ripple.z + dir).xyz * 2.0h - 1.0h;
                half3 b = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv * _Ripple.z * _Ripple.w - dir * 0.7).xyz * 2.0h - 1.0h;
                t = half3(a.xy + b.xy, a.z * b.z);
            }
            else t = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv * _Scales.y).xyz * 2.0h - 1.0h;
            float3 T = SafeNormalize(input.tangentWS.xyz);
            float3 B = cross(n, T) * (input.tangentWS.w < 0 ? -1.0 : 1.0);
            return SafeNormalize(T * t.x + B * t.y + n * max(t.z, 0.2h));
        }
        ENDHLSL

        Pass
        {
            Name "Background"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull [_Cull]
            ZWrite Off
            Blend DstColor Zero

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragTint
            #pragma multi_compile_instancing

            half4 FragTint(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 tint = _BackTint.rgb;
                half4 m = SAMPLE_TEXTURE2D(_TintMap, sampler_TintMap, input.uv * _Scales.x);
                if (_Kind.x < 0.5) tint *= m.rgb;
                else if (_Kind.w > 0.5) tint *= m.rgb;
                half fog = _Sky > 0.5 ? 0.0h : HaloFogOnly(input.positionWS);
                return half4(HaloOut3(lerp(tint, half3(1, 1, 1), fog)), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite Off
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragAdd
            #pragma multi_compile_instancing

            half4 FragAdd(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = SafeNormalize(input.normalWS);
                n = IS_FRONT_VFACE(face, n, -n);
                half3 nb = SurfaceNormal(input, n);
                float3 V = SafeNormalize(GetCameraPositionWS() - input.positionWS);
                float3 R = reflect(-V, nb);
                half3 env = SAMPLE_TEXTURECUBE(_ReflCube, sampler_ReflCube, float3(R.x, R.z, R.y)).rgb;
                half ndv = saturate(dot(n, (half3)V));
                half f = 1.0h - ndv; f *= f;
                half3 c = env * lerp(_PerpColor.rgb, _ParColor.rgb, f);
                if (_Kind.x > 0.5 && _Kind.z > 0.5) c *= SAMPLE_TEXTURE2D(_TintMap, sampler_TintMap, input.uv * _Scales.x).a;
                if (_Kind.x < 0.5)
                {
                    half4 d = SAMPLE_TEXTURE2D(_DiffuseMap, sampler_DiffuseMap, input.uv * _Scales.z);
                    half4 s = SAMPLE_TEXTURE2D(_SpecMap, sampler_SpecMap, input.uv * _Scales.w);
                    c = c * (1.0h + s.rgb) + d.rgb * d.a * (half3)_HaloAmbient.rgb;
                }
                half fog = _Sky > 0.5 ? 0.0h : HaloFogOnly(input.positionWS);
                return half4(HaloOut3(c * (1.0h - fog)), 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

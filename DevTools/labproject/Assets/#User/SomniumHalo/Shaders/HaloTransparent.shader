// SomniumHalo/Transparent - Halo's transparent shaders: shader_transparent_chicago (schi) and chicago extended (scex) with up to four
// map stages, shader_transparent_plasma (spla) and shader_transparent_meter (smet). Written by Game/World/HaloMaterials.
// Forward pass only (transparent queue; nothing in the depth prepass). No keywords, float switches only.
//
// Chicago stages as the game combines them: result = map 0; then for each next map i, the PREVIOUS map's colour / alpha function
// combines the result ("current") with map i ("next map"): current, next, multiply, double multiply, add, add signed current,
// add signed next, subtract current, subtract next, blend current alpha (+ inverse), blend next map alpha (+ inverse).
// Each map: uv scale / offset / rotation and animated offsets (u, v) and rotation. First map may be a cube map (reflection,
// object-centred, viewer-centred). The framebuffer blend function sets Blend/BlendOp from C# and how fog fades the result.
Shader "SomniumHalo/Transparent"
{
    Properties
    {
        _Map0 ("Map 0", 2D) = "white" {}
        _Map1 ("Map 1", 2D) = "white" {}
        _Map2 ("Map 2", 2D) = "white" {}
        _Map3 ("Map 3", 2D) = "white" {}
        _Cube0 ("Map 0 as a cube map", Cube) = "black" {}
        _M0 ("Map 0: u scale, v scale, u offset, v offset", Vector) = (1, 1, 0, 0)
        _M1 ("Map 1: scale / offset", Vector) = (1, 1, 0, 0)
        _M2 ("Map 2: scale / offset", Vector) = (1, 1, 0, 0)
        _M3 ("Map 3: scale / offset", Vector) = (1, 1, 0, 0)
        _F0 ("Map 0: colour function, alpha function, rotation (deg), flags (2 alpha replicate)", Vector) = (0, 0, 0, 0)
        _F1 ("Map 1 functions", Vector) = (0, 0, 0, 0)
        _F2 ("Map 2 functions", Vector) = (0, 0, 0, 0)
        _F3 ("Map 3 functions", Vector) = (0, 0, 0, 0)
        _UA0 ("Map 0 u anim: function, period, phase, scale", Vector) = (0, 1, 0, 0)
        _UA1 ("Map 1 u anim", Vector) = (0, 1, 0, 0)
        _UA2 ("Map 2 u anim", Vector) = (0, 1, 0, 0)
        _UA3 ("Map 3 u anim", Vector) = (0, 1, 0, 0)
        _VA0 ("Map 0 v anim", Vector) = (0, 1, 0, 0)
        _VA1 ("Map 1 v anim", Vector) = (0, 1, 0, 0)
        _VA2 ("Map 2 v anim", Vector) = (0, 1, 0, 0)
        _VA3 ("Map 3 v anim", Vector) = (0, 1, 0, 0)
        _RA0 ("Map 0 rotation anim: function, period, phase, scale (deg)", Vector) = (0, 1, 0, 0)
        _RA1 ("Map 1 rotation anim", Vector) = (0, 1, 0, 0)
        _RA2 ("Map 2 rotation anim", Vector) = (0, 1, 0, 0)
        _RA3 ("Map 3 rotation anim", Vector) = (0, 1, 0, 0)
        _RC ("Rotation centres: map 0 xy, map 1 zw (maps 2, 3 use 0.5)", Vector) = (0.5, 0.5, 0.5, 0.5)
        _Setup ("x map count, y first map type (0 2d, 1 reflection cube, 2 object cube, 3 viewer cube), z framebuffer blend, w fade mode", Vector) = (1, 0, 0, 0)
        _Mode ("0 chicago, 1 plasma, 2 meter", Float) = 0
        _AlphaClip ("Alpha tested", Float) = 0
        _Tint ("Tint (x colour; a = fade, effects)", Color) = (1, 1, 1, 1)
        _PerpColor ("Plasma / fade: perpendicular tint x brightness", Color) = (1, 1, 1, 1)
        _ParColor ("Plasma: parallel tint x brightness", Color) = (1, 1, 1, 1)
        _Plasma ("Plasma: primary period, primary scale, secondary period, secondary scale", Vector) = (1, 1, 1, 1)
        _PlasmaDir0 ("Plasma primary direction", Vector) = (0, 0, 1, 0)
        _PlasmaDir1 ("Plasma secondary direction", Vector) = (0, 0, 1, 0)
        _MeterMin ("Meter gradient min colour", Color) = (0, 0, 0, 1)
        _MeterMax ("Meter gradient max colour", Color) = (1, 1, 1, 1)
        _MeterBack ("Meter background colour", Color) = (0, 0, 0, 1)
        _MeterFlash ("Meter flash colour", Color) = (1, 1, 1, 1)
        _Meter ("Meter: x value (0..1), y meter transparency, z background transparency, w flash", Vector) = (1, 1, 0, 0)
        _Sky ("Sky geometry (drawn around the eye at the far plane)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
        [Enum(UnityEngine.Rendering.BlendOp)] _BlendOp ("Blend op", Float) = 0
        _ZWrite ("ZWrite", Float) = 0
        _OffsetFactor ("Polygon offset factor", Float) = 0
        _OffsetUnits ("Polygon offset units", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "HaloCommon.hlsl"

        TEXTURE2D(_Map0); SAMPLER(sampler_Map0);
        TEXTURE2D(_Map1); SAMPLER(sampler_Map1);
        TEXTURE2D(_Map2); SAMPLER(sampler_Map2);
        TEXTURE2D(_Map3); SAMPLER(sampler_Map3);
        TEXTURECUBE(_Cube0); SAMPLER(sampler_Cube0);

        CBUFFER_START(UnityPerMaterial)
            float4 _Map0_ST;
            float4 _M0; float4 _M1; float4 _M2; float4 _M3;
            float4 _F0; float4 _F1; float4 _F2; float4 _F3;
            float4 _UA0; float4 _UA1; float4 _UA2; float4 _UA3;
            float4 _VA0; float4 _VA1; float4 _VA2; float4 _VA3;
            float4 _RA0; float4 _RA1; float4 _RA2; float4 _RA3;
            float4 _RC;
            float4 _Setup;
            float _Mode;
            float _AlphaClip;
            half4 _Tint;
            half4 _PerpColor;
            half4 _ParColor;
            float4 _Plasma;
            float4 _PlasmaDir0;
            float4 _PlasmaDir1;
            half4 _MeterMin;
            half4 _MeterMax;
            half4 _MeterBack;
            half4 _MeterFlash;
            float4 _Meter;
            float _Sky;
            float _Cull;
            float _SrcBlend;
            float _DstBlend;
            float _BlendOp;
            float _ZWrite;
            float _OffsetFactor;
            float _OffsetUnits;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            BlendOp [_BlendOp]
            Offset [_OffsetFactor], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv0 : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                half4 color : COLOR;
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
                o.positionOS = input.positionOS.xyz;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv0;
                o.color = input.color;
                return o;
            }

            float2 StageUv(float2 uv, float4 st, float4 fn, float4 ua, float4 va, float4 ra, float2 centre)
            {
                float2 u = uv * st.xy + st.zw;
                if (ua.w != 0) u.x += ua.w * HaloWave(ua.x, ua.y, ua.z);
                if (va.w != 0) u.y += va.w * HaloWave(va.x, va.y, va.z);
                float rot = fn.z;
                if (ra.w != 0) rot += ra.w * HaloWave(ra.x, ra.y, ra.z);
                return HaloRotate(u, centre, rot);
            }

            half4 Replicate(half4 c, float4 fn)
            {
                // flags: 2 = alpha replicate (the alpha goes to rgb too)
                if (fmod(floor(fn.w / 2.0), 2.0) > 0.5) c.rgb = c.aaa;
                return c;
            }

            half3 CombineC(half3 cur, half3 nxt, half curA, half nxtA, float f)
            {
                if (f < 0.5) return cur;
                if (f < 1.5) return nxt;
                if (f < 2.5) return cur * nxt;
                if (f < 3.5) return 2.0h * cur * nxt;
                if (f < 4.5) return cur + nxt;
                if (f < 6.5) return cur + nxt - 0.5h;
                if (f < 7.5) return cur - nxt;
                if (f < 8.5) return nxt - cur;
                if (f < 9.5) return lerp(nxt, cur, curA);
                if (f < 10.5) return lerp(cur, nxt, curA);
                if (f < 11.5) return lerp(cur, nxt, nxtA);
                return lerp(nxt, cur, nxtA);
            }

            half CombineA(half cur, half nxt, float f)
            {
                if (f < 0.5) return cur;
                if (f < 1.5) return nxt;
                if (f < 2.5) return cur * nxt;
                if (f < 3.5) return 2.0h * cur * nxt;
                if (f < 4.5) return cur + nxt;
                if (f < 6.5) return cur + nxt - 0.5h;
                if (f < 7.5) return cur - nxt;
                if (f < 8.5) return nxt - cur;
                if (f < 9.5) return lerp(nxt, cur, cur);
                if (f < 10.5) return lerp(cur, nxt, cur);
                if (f < 11.5) return lerp(cur, nxt, nxt);
                return lerp(nxt, cur, nxt);
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 V = SafeNormalize(GetCameraPositionWS() - input.positionWS);
                half3 n = SafeNormalize(input.normalWS);
                n = IS_FRONT_VFACE(face, n, -n);
                half ndv = saturate(abs(dot(n, (half3)V)));
                half4 c;

                if (_Mode > 1.5)
                {
                    // meter: the map's value (luminance) runs along the meter; lit up to the meter value
                    half4 m = SAMPLE_TEXTURE2D(_Map0, sampler_Map0, input.uv * _M0.xy + _M0.zw);
                    half v = m.r;
                    bool lit = v <= (half)_Meter.x;
                    half3 col = lit ? lerp(_MeterMin.rgb, _MeterMax.rgb, v) : _MeterBack.rgb;
                    col = lerp(col, _MeterFlash.rgb, (half)_Meter.w);
                    c = half4(col * _Tint.rgb, m.a * (lit ? (half)_Meter.y : (half)_Meter.z));
                }
                else if (_Mode > 0.5)
                {
                    // plasma: two scrolling noise maps, bright where they meet, tinted by view angle
                    float3 p = input.positionOS;
                    float2 u0 = (p.xy + p.z * 0.37) * _Plasma.y + _PlasmaDir0.xy * (_HaloTime / max(abs(_Plasma.x), 0.01));
                    float2 u1 = (p.yz + p.x * 0.29) * _Plasma.w + _PlasmaDir1.xy * (_HaloTime / max(abs(_Plasma.z), 0.01));
                    half a = SAMPLE_TEXTURE2D(_Map0, sampler_Map0, u0).r;
                    half b = SAMPLE_TEXTURE2D(_Map1, sampler_Map1, u1).r;
                    half k = saturate(1.0h - abs(a - b) * 4.0h);
                    k = k * k;
                    half3 tint = lerp(_ParColor.rgb, _PerpColor.rgb, ndv);
                    c = half4(tint * k * _Tint.rgb, k);
                }
                else
                {
                    if (_Setup.y > 0.5)
                    {
                        float3 d;
                        if (_Setup.y < 1.5) d = reflect(-V, n);
                        else if (_Setup.y < 2.5) d = SafeNormalize(input.positionWS - TransformObjectToWorld(float3(0, 0, 0)));
                        else d = -V;
                        c = SAMPLE_TEXTURECUBE(_Cube0, sampler_Cube0, float3(d.x, d.z, d.y));
                    }
                    else
                    {
                        c = SAMPLE_TEXTURE2D(_Map0, sampler_Map0, StageUv(input.uv, _M0, _F0, _UA0, _VA0, _RA0, _RC.xy));
                    }
                    c = Replicate(c, _F0);
                    if (_Setup.x > 1.5)
                    {
                        half4 m = Replicate(SAMPLE_TEXTURE2D(_Map1, sampler_Map1, StageUv(input.uv, _M1, _F1, _UA1, _VA1, _RA1, _RC.zw)), _F1);
                        half3 rgb = CombineC(c.rgb, m.rgb, c.a, m.a, _F0.x);
                        c = half4(rgb, CombineA(c.a, m.a, _F0.y));
                    }
                    if (_Setup.x > 2.5)
                    {
                        half4 m = Replicate(SAMPLE_TEXTURE2D(_Map2, sampler_Map2, StageUv(input.uv, _M2, _F2, _UA2, _VA2, _RA2, float2(0.5, 0.5))), _F2);
                        half3 rgb = CombineC(c.rgb, m.rgb, c.a, m.a, _F1.x);
                        c = half4(rgb, CombineA(c.a, m.a, _F1.y));
                    }
                    if (_Setup.x > 3.5)
                    {
                        half4 m = Replicate(SAMPLE_TEXTURE2D(_Map3, sampler_Map3, StageUv(input.uv, _M3, _F3, _UA3, _VA3, _RA3, float2(0.5, 0.5))), _F3);
                        half3 rgb = CombineC(c.rgb, m.rgb, c.a, m.a, _F2.x);
                        c = half4(rgb, CombineA(c.a, m.a, _F2.y));
                    }
                    c = saturate(c);
                    c.rgb *= _Tint.rgb;
                    c.a *= _Tint.a;
                }

                if (_AlphaClip > 0.5) clip(c.a - 0.5h);
                // fade with the view angle
                if (_Setup.w > 0.5)
                {
                    half f = _Setup.w < 1.5 ? (1.0h - ndv) : ndv;
                    c.a *= f;
                    if (_Setup.z > 2.5 && _Setup.z < 3.5) c.rgb *= f;
                }

                // framebuffer blend: 0 alpha blend, 1 multiply, 2 double multiply, 3 add, 4 subtract, 5 min, 6 max, 7 alpha multiply add
                float blend = _Setup.z;
                half fog = _Sky > 0.5 ? 0.0h : HaloFogOnly(input.positionWS);
                if (blend < 0.5)
                {
                    half3 rgb = _Sky > 0.5 ? c.rgb : HaloFog(c.rgb, input.positionWS);
                    return half4(HaloOut3(rgb), saturate(c.a));
                }
                if (blend < 1.5) return half4(HaloOut3(lerp(c.rgb, half3(1, 1, 1), fog)), 1.0h);
                if (blend < 2.5) return half4(HaloOutHdr(lerp(2.0h * c.rgb, half3(1, 1, 1), fog)), 1.0h);
                if (blend < 6.5) return half4(HaloOut3(c.rgb * (1.0h - fog)), 1.0h);
                return half4(HaloOut3(c.rgb * (1.0h - fog)), saturate(c.a));
            }
            ENDHLSL
        }
    }

    Fallback Off
}

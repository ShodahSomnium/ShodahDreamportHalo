// SomniumHalo/Fx - Halo's particles, contrails and decals (Game/Fx). Unlit, double sided, no depth write, one material per
// (texture, framebuffer blend function). The blend function is Halo's own (particle / contrail / decal tags):
//   _Mode 0 alpha blend, 1 multiply, 2 double multiply, 3 add, 4 subtract, 5 component min, 6 component max, 7 alpha multiply add
// and Game/Fx/HaloFxRender sets the matching _SrcBlend / _DstBlend / _BlendOp. Colour = texture x vertex colour; the vertex alpha fades
// the colour the way each blend needs (towards white for multiply, grey for double multiply, black for add / subtract / max).
//
// Quads are expanded HERE, per eye (single-pass stereo instancing: UNITY_MATRIX_V / the camera position are the eye's), from
//   POSITION  = the particle centre (or the final point for kind 2)
//   TEXCOORD0 = uv (xy), corner offset in metres (zw)
//   TEXCOORD1 = axis (xyz), kind (w): 0 = screen facing (corner along the view's right / up),
//               1 = turned about the axis to face the eye (corner x along the axis, y across: "parallel to direction" sprites, contrails),
//               2 = world fixed (position is final: decals, "perpendicular to direction" sprites).
// No keywords besides URP fog (nothing is stripped from the upload). Carried by Shaders/Fx/HaloFx.mat (HaloGame _shaderCarriers).
Shader "SomniumHalo/Fx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Mode ("Halo blend function", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
        [Enum(UnityEngine.Rendering.BlendOp)] _BlendOp ("Blend op", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _OffsetFactor ("Offset factor", Float) = 0
        _OffsetUnits ("Offset units", Float) = 0
        _GammaAdd ("Add as Halo does (in gamma: lens flares)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            Offset [_OffsetFactor], [_OffsetUnits]
            BlendOp [_BlendOp]
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Mode;
                half _SrcBlend;
                half _DstBlend;
                half _BlendOp;
                half _ZTest;
                half _OffsetFactor;
                half _OffsetUnits;
                half _GammaAdd;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                half4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half fog : TEXCOORD1;
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

                float3 c = TransformObjectToWorld(input.positionOS);
                float2 k = input.uv0.zw;
                float kind = input.uv1.w;
                float3 p = c;
                if (kind < 0.5)
                {
                    float3 right = UNITY_MATRIX_V[0].xyz;
                    float3 up = UNITY_MATRIX_V[1].xyz;
                    p = c + right * k.x + up * k.y;
                }
                else if (kind < 1.5)
                {
                    float3 axis = input.uv1.xyz;
                    float al = length(axis);
                    axis = al > 1e-6 ? axis / al : UNITY_MATRIX_V[1].xyz;
                    float3 view = c - GetCameraPositionWS();
                    float3 side = cross(axis, view);
                    float sl = length(side);
                    side = sl > 1e-6 ? side / sl : UNITY_MATRIX_V[0].xyz;
                    p = c + axis * k.x + side * k.y;
                }

                o.positionCS = TransformWorldToHClip(p);
                o.uv = input.uv0.xy;
                o.color = input.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half m = _Mode;
                // Halo's tints are gamma-space values (vertex colours arrive raw); textures are sampled linear (sRGB)
                half3 vc = input.color.rgb;
            #if !defined(UNITY_COLORSPACE_GAMMA)
                vc = pow(max(vc, 0.0h), 2.2h);
            #endif
                half4 c = half4(t.rgb * vc, t.a * input.color.a);
                if (m < 0.5h)
                {
                    // alpha blend (SrcAlpha, OneMinusSrcAlpha)
                    c.rgb = MixFog(c.rgb, input.fog);
                    return c;
                }
                if (m < 1.5h || (m > 4.5h && m < 5.5h))
                {
                    // multiply (DstColor, Zero) / component min: fade towards white
                    half3 r = lerp(half3(1.0h, 1.0h, 1.0h), c.rgb, c.a);
                    r = MixFogColor(r, half3(1.0h, 1.0h, 1.0h), input.fog);
                    return half4(r, 1.0h);
                }
                if (m < 2.5h)
                {
                    // double multiply: Halo does dst x 2 x src in gamma space, so 50% grey leaves the surface as it is. Done here as
                    // (DstColor, Zero) with the factor (2 x src_gamma)^2.2 in a linear project; fades towards grey.
                    half3 g = c.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    g = pow(max(g, 0.0h), 1.0h / 2.2h);
                #endif
                    g = lerp(half3(0.5h, 0.5h, 0.5h), g, c.a);
                    g = MixFogColor(g, half3(0.5h, 0.5h, 0.5h), input.fog);
                    half3 r = 2.0h * g;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    r = pow(r, 2.2h);
                #endif
                    return half4(r, 1.0h);
                }
                if (m > 6.5h)
                {
                    // alpha multiply add (One, OneMinusSrcAlpha)
                    c.rgb = MixFogColor(c.rgb, half3(0.0h, 0.0h, 0.0h), input.fog);
                    return c;
                }
                // add / subtract / component max (One, One): fade towards black
                half3 a = c.rgb * c.a;
            #if !defined(UNITY_COLORSPACE_GAMMA)
                // Halo adds in gamma space: a glow over a bright sky adds its gamma value. Adding the linear value (above) makes lens
                // flares (the plasma bolts) nearly vanish in daylight; their gamma value added as linear is close to Halo's look
                if (_GammaAdd > 0.5h) a = pow(max(a, 0.0h), 1.0h / 2.2h);
            #endif
                a = MixFogColor(a, half3(0.0h, 0.0h, 0.0h), input.fog);
                return half4(a, c.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

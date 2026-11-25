// SomniumHalo/Sprite - camera-facing sprites of the World module, all expanded in the vertex shader (static meshes, no CPU per frame):
//   _Mode 0: lens flares - a quad at a BSP lens flare marker or the sun, screen aligned, hidden when the scene depth at its centre is in
//            front of it (URP camera depth texture), faded by the angle to the marker's direction and by distance (the lens_flare tag's
//            cos falloff / cutoff angles, near / far fade distance);
//   _Mode 1: detail objects (grass, flowers) - billboards turning about the world up axis, faded between the type's near and far distance;
//   _Mode 2: weather (rain, snow) - particles in a box that wraps round the camera, falling with _Fall (xyz velocity m/s) and swaying,
//            stretched along the fall for rain (_Stretch), faded with the distance;
// Vertex data: POSITION = sprite centre (object space; weather: position inside the box 0..1), TEXCOORD0 = uv in the sprite (atlas rect
// already applied), TEXCOORD1 = corner (x, y in -1..1) and half size (z, w metres), TEXCOORD2 = extra (lens flare: marker direction xyz,
// w = 1 for a "sun" flare at infinity), COLOR = tint. Stereo instancing in the one pass. No keywords.
Shader "SomniumHalo/Sprite"
{
    Properties
    {
        _MainTex ("Sprite texture (linear)", 2D) = "white" {}
        _Mode ("0 lens flare, 1 detail object, 2 weather", Float) = 1
        _Fade ("x near fade start, y near fade end (fully visible), z far fade start, w far fade end (m)", Vector) = (0, 0, 30, 40)
        _Angles ("Lens flare: x cos falloff, y cos cutoff", Vector) = (0.5, 0.3, 0, 0)
        _Box ("Weather: box size (m) xyz, w particle sway (m)", Vector) = (20, 20, 20, 0.1)
        _Fall ("Weather: fall velocity xyz (m/s), w stretch (rain streak length in s of fall)", Vector) = (0, -1, 0, 0)
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _AlphaClip ("Alpha test (detail objects)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 10
        _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "HaloCommon.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D_X_FLOAT(_CameraDepthTexture); SAMPLER(sampler_CameraDepthTexture);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float _Mode;
            float4 _Fade;
            float4 _Angles;
            float4 _Box;
            float4 _Fall;
            half4 _Tint;
            float _AlphaClip;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
            float _ZTest;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 corner : TEXCOORD1;
                float4 extra : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Ramp(float d)
            {
                float nearF = _Fade.y > _Fade.x ? saturate((d - _Fade.x) / (_Fade.y - _Fade.x)) : 1.0;
                float farF = _Fade.w > _Fade.z ? 1.0 - saturate((d - _Fade.z) / (_Fade.w - _Fade.z)) : 1.0;
                return nearF * farF;
            }

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 cam = GetCameraPositionWS();
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                half4 colour = input.color * _Tint;
                float3 centre;
                float3 cornerWS;

                if (_Mode < 0.5)
                {
                    // lens flare
                    if (input.extra.w > 0.5) centre = cam + normalize(input.extra.xyz) * 1000.0;   // the sun: at infinity in its direction
                    else centre = TransformObjectToWorld(input.positionOS.xyz);
                    float3 toCam = cam - centre;
                    float d = length(toCam);
                    float vis = input.extra.w > 0.5 ? 1.0 : Ramp(d);
                    // the marker's direction: brightest when looked at along it (0 = no direction)
                    float3 dir = TransformObjectToWorldDir(input.extra.xyz, false);
                    if (input.extra.w < 0.5 && dot(dir, dir) > 0.01)
                    {
                        float c = dot(normalize(dir), toCam / max(d, 0.001));
                        vis *= saturate((c - _Angles.y) / max(_Angles.x - _Angles.y, 0.001));
                    }
                    // occlusion: the scene depth at the centre (in front = hidden)
                    float4 cc = TransformWorldToHClip(centre);
                    if (cc.w > 0.0)
                    {
                        float2 suv = cc.xy / cc.w * 0.5 + 0.5;
                        #if UNITY_UV_STARTS_AT_TOP
                            suv.y = 1.0 - suv.y;
                        #endif
                        if (all(suv > 0.0) && all(suv < 1.0))
                        {
                            float raw = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_CameraDepthTexture, UnityStereoTransformScreenSpaceTex(suv), 0).r;
                            float sceneEye = LinearEyeDepth(raw, _ZBufferParams);
                            if (input.extra.w > 0.5) { if (sceneEye < _ProjectionParams.z * 0.98) vis = 0.0; }
                            else if (sceneEye < cc.w - 0.15) vis = 0.0;
                        }
                        else vis = 0.0;
                    }
                    else vis = 0.0;
                    colour.rgb *= vis;
                    colour.a *= vis;
                    cornerWS = centre + right * input.corner.x * input.corner.z + up * input.corner.y * input.corner.w;   // (the sun's size comes scaled to its 1 km distance)
                    if (vis <= 0.0) cornerWS = centre;   // collapsed
                }
                else if (_Mode < 1.5)
                {
                    // detail object: turns about the world up axis
                    centre = TransformObjectToWorld(input.positionOS.xyz);
                    float3 toCam = cam - centre;
                    float d = length(toCam);
                    float3 side = normalize(cross(float3(0, 1, 0), float3(toCam.x, 0.0, toCam.z) + float3(0.0001, 0, 0)));
                    float vis = Ramp(d);
                    colour.a *= vis;
                    cornerWS = centre + side * input.corner.x * input.corner.z + float3(0, 1, 0) * (input.corner.y * 0.5 + 0.5) * input.corner.w * 2.0;
                    if (vis <= 0.0) cornerWS = centre;
                }
                else
                {
                    // weather: a box of particles wrapping round the camera
                    float3 size = _Box.xyz;
                    float3 p = input.positionOS.xyz * size + _Fall.xyz * _Time.y;
                    float sway = _Box.w * sin(_Time.y * 1.3 + input.positionOS.x * 37.0);
                    p.x += sway; p.z += _Box.w * cos(_Time.y * 1.1 + input.positionOS.z * 29.0);
                    float3 origin = cam - size * 0.5;
                    float3 rel = p - origin;
                    rel = rel - floor(rel / size) * size;
                    centre = origin + rel;
                    float d = length(centre - cam);
                    colour.a *= Ramp(d);
                    float3 along = _Fall.w > 0.0 ? normalize(_Fall.xyz) : up;
                    float3 across = normalize(cross(along, cam - centre));
                    float len = _Fall.w > 0.0 ? max(input.corner.w, length(_Fall.xyz) * _Fall.w) : input.corner.w;
                    cornerWS = centre + across * input.corner.x * input.corner.z + along * input.corner.y * len;
                }

                o.positionWS = cornerWS;
                o.positionCS = TransformWorldToHClip(cornerWS);
                o.uv = input.uv;
                o.color = colour;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 c = t * input.color;
                if (_AlphaClip > 0.5) clip(c.a - 0.5h);
                half fog = HaloFogOnly(input.positionWS);
                if (_DstBlend < 1.5 && _SrcBlend < 1.5)
                {
                    // additive (One One): fades to nothing in fog
                    return half4(HaloOut3(c.rgb * c.a * (1.0h - fog)), 1.0h);
                }
                half3 rgb = HaloFog(c.rgb, input.positionWS);
                return half4(HaloOut3(rgb), saturate(c.a));
            }
            ENDHLSL
        }
    }

    Fallback Off
}

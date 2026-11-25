// SomniumHalo shared shader code (included by HaloEnv / HaloModel / HaloTransparent / HaloGlass after URP Core.hlsl).
//
// Colour: Halo shades in gamma space (Direct3D 8, no sRGB). Our Halo textures are uploaded LINEAR (raw stored values), the maths
// below runs on those raw values exactly as the game's pixel shaders do, and HaloOut() turns the gamma-space result into the linear
// value URP writes (the sRGB target encodes it back), so a texel shows on screen as the game shows it.
// Globals are set once per map by Game/World/HaloWorld (HaloWorldLook): atmospheric fog, the default object light, time.
#ifndef SOMNIUM_HALO_COMMON
#define SOMNIUM_HALO_COMMON

float4 _HaloFogColor;     // rgb fog colour (gamma), a = maximum density (0 = no fog)
float4 _HaloFogDist;      // x start distance (m), y opaque distance (m)
float4 _HaloPlaneFog;     // planar fog: xyz plane normal (Unity), w plane distance (n.p = w is the surface; below = in fog)
float4 _HaloPlaneFogColor;// rgb colour, a maximum density (0 = none)
float4 _HaloPlaneFogDist; // x opaque depth below the plane (m), y opaque distance (m)
float4 _HaloAmbient;      // default object light: rgb ambient (gamma)
float4 _HaloSunDir;       // xyz towards the sun (Unity world)
float4 _HaloSunColor;     // rgb
float4 _HaloSkyLight;     // rgb second (sky) light, from above
#define _HaloTime (_Time.y)


// sRGB decode (gamma-space value -> linear), as the sRGB target will encode it back
half3 HaloLinear(half3 c)
{
    half3 lo = c / 12.92h;
    half3 hi = pow(max((c + 0.055h) / 1.055h, 0.0h), 2.4h);
    return half3(c.r <= 0.04045h ? lo.r : hi.r, c.g <= 0.04045h ? lo.g : hi.g, c.b <= 0.04045h ? lo.b : hi.b);
}
half3 HaloOut3(half3 g) { return HaloLinear(saturate(g)); }
half3 HaloOutHdr(half3 g) { return HaloLinear(max(g, 0.0h)); }

// Halo periodic functions (WaveFunction): 0 one, 1 zero, 2 cosine, 3 cosine variable, 4 diagonal wave, 5 diagonal variable, 6 slide,
// 7 slide variable, 8 noise, 9 jitter, 10 wander, 11 spark. Result 0..1.
float HaloHash(float x) { return frac(sin(x * 12.9898) * 43758.5453); }
float HaloWave(float func, float period, float phase)
{
    if (func < 0.5) return 1.0;
    if (func < 1.5) return 0.0;
    float p = max(abs(period), 0.0001);
    float t = _HaloTime / p + phase;
    if (period < 0) t = -t;
    float x = frac(t);
    if (func < 3.5) return 0.5 - 0.5 * cos(6.2831853 * x);
    if (func < 5.5) return 1.0 - abs(2.0 * x - 1.0);
    if (func < 7.5) return x;
    float i = floor(t);
    if (func < 8.5) return lerp(HaloHash(i), HaloHash(i + 1.0), smoothstep(0.0, 1.0, x));
    if (func < 9.5) return HaloHash(floor(t * 8.0));
    if (func < 10.5) return 0.5 + 0.5 * sin(6.2831853 * t) * sin(2.3 * t + 1.7);
    return x < 0.1 ? 1.0 - x * 10.0 : 0.0;
}

// The game's atmospheric fog (sky tag outdoor fog / fog tag): density rises linearly from the start to the opaque distance, capped by
// the maximum density. Planar fog (fog planes of the BSP): density grows with the depth under the plane and the distance through it.
half HaloFogAmount(float3 positionWS)
{
    float3 cam = GetCameraPositionWS();
    float d = length(positionWS - cam);
    half f = 0.0h;
    if (_HaloFogColor.a > 0.0)
    {
        float range = max(_HaloFogDist.y - _HaloFogDist.x, 0.01);
        f = (half)(saturate((d - _HaloFogDist.x) / range) * _HaloFogColor.a);
    }
    return f;
}

half HaloPlaneFogAmount(float3 positionWS)
{
    if (_HaloPlaneFogColor.a <= 0.0) return 0.0h;
    float3 cam = GetCameraPositionWS();
    float depthP = _HaloPlaneFog.w - dot(_HaloPlaneFog.xyz, positionWS);   // > 0 under the plane
    float depthC = _HaloPlaneFog.w - dot(_HaloPlaneFog.xyz, cam);
    if (depthP <= 0.0 && depthC <= 0.0) return 0.0h;
    float d = length(positionWS - cam);
    // the part of the ray that runs under the plane
    float inside = d;
    if (depthP <= 0.0 || depthC <= 0.0)
    {
        float a = max(depthP, 0.0), b = max(depthC, 0.0);
        inside = d * (max(a, b) / max(abs(depthP - depthC), 0.0001));
    }
    float depth = max(max(depthP, depthC), 0.0);
    float byDepth = saturate(depth / max(_HaloPlaneFogDist.x, 0.01));
    float byDist = saturate(inside / max(_HaloPlaneFogDist.y, 0.01));
    return (half)(saturate(max(byDepth, byDist) * byDist) * _HaloPlaneFogColor.a);
}

half3 HaloFog(half3 colour, float3 positionWS)
{
    half f = HaloFogAmount(positionWS);
    colour = lerp(colour, (half3)_HaloFogColor.rgb, f);
    half p = HaloPlaneFogAmount(positionWS);
    return lerp(colour, (half3)_HaloPlaneFogColor.rgb, p);
}

// How much fog hides a blended surface (additive / multiply blends fade to their neutral value instead of the fog colour).
half HaloFogOnly(float3 positionWS)
{
    return saturate(HaloFogAmount(positionWS) + HaloPlaneFogAmount(positionWS));
}

// One clip-space formula for every pass. Sky geometry (_Sky = 1) sits around the eye at the far plane: the model is drawn at the
// camera position (Halo draws its sky at the viewer) with its rotation and scale, depth pushed to the far plane.
float4 HaloClipPos(float3 positionOS, float sky, out float3 positionWS)
{
    if (sky > 0.5)
    {
        float3 offset = mul((float3x3)GetObjectToWorldMatrix(), positionOS);
        positionWS = GetCameraPositionWS() + offset;
        float4 clip = TransformWorldToHClip(positionWS);
        #if UNITY_REVERSED_Z
            clip.z = clip.w * 0.000001;
        #else
            clip.z = clip.w * 0.999999;
        #endif
        return clip;
    }
    positionWS = TransformObjectToWorld(positionOS);
    return TransformWorldToHClip(positionWS);
}

// Halo uv: scale, offset, rotation about a centre (degrees), animated offsets (u/v wave x scale) and rotation (wave x scale degrees).
float2 HaloRotate(float2 uv, float2 centre, float degrees)
{
    if (abs(degrees) < 0.0001) return uv;
    float r = radians(degrees);
    float s = sin(r), c = cos(r);
    float2 d = uv - centre;
    return centre + float2(d.x * c - d.y * s, d.x * s + d.y * c);
}

#endif

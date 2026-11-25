using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    internal struct FxFlare
    {
        public bool Alive, Attached, FirstPerson;
        public int Gen;
        public FxLightDef Light;
        /// <summary>A light volume (mgs2 widget) instead of a light's lens flare.</summary>
        public FxLightVolumeDef Volume;
        /// <summary>The attached root is a built Halo model (its +X is forward), not a plain anchor (+Z).</summary>
        public bool XForward;
        public Transform Attach;
        public Vector3 LocalPos, Pos;
        public float Age, Life, Scale, Rnd;
        public Color From, To;
    }

    /// <summary>
    /// Lens flares of lights (the light tag's +172 "lens flare"): what Halo draws OF a light. The plasma pistol bolt is nothing but its
    /// light's flare (weapons\plasma pistol\plasma.ligh -> flares_generic, green from the light's colour); the plasma rifle / ghost / banshee
    /// bolts are their light's flare (flares weapon lens) plus a thin contrail. Without this those bolts were invisible.
    /// Drawn as screen-facing additive quads (the flare bitmaps have no alpha) in the effects' own batches.
    /// </summary>
    internal sealed partial class HaloFxRuntime
    {
        public const int MaxFlares = 256;   // ours
        FxFlare[] _flares = new FxFlare[MaxFlares];
        int _flareHigh;

        internal int FlareCount { get { int n = 0; for (int i = 0; i < _flareHigh; i++) if (_flares[i].Alive) n++; return n; } }
        internal int VolumeCount { get { int n = 0; for (int i = 0; i < _flareHigh; i++) if (_flares[i].Alive && _flares[i].Volume != null) n++; return n; } }

        void ClearFlares()
        {
            for (int i = 0; i < _flares.Length; i++) { _flares[i].Alive = false; _flares[i].Attach = null; _flares[i].Light = null; _flares[i].Volume = null; }
            _flareHigh = 0;
        }

        /// <summary>Starts the lens flare of a light (none when its tag has no lens flare). <paramref name="life"/> in seconds
        /// (float.MaxValue = until killed / the attached transform goes). Returns the slot or -1.</summary>
        internal int StartFlare(FxLightDef d, Vector3 pos, Transform attach, float life, bool firstPerson)
        {
            if (d == null || d.Lens == null) return -1;
            int slot = -1;
            for (int i = 0; i < _flares.Length; i++) if (!_flares[i].Alive) { slot = i; break; }
            if (slot < 0) return -1;
            ref FxFlare f = ref _flares[slot];
            f.Alive = true; f.Gen = ++_gen; f.Light = d; f.Volume = null; f.XForward = false; f.Age = 0f; f.Life = life; f.Scale = 1f; f.Rnd = UnityEngine.Random.value;
            f.FirstPerson = firstPerson;
            f.Attach = attach; f.Attached = attach != null;
            f.LocalPos = attach != null ? attach.InverseTransformPoint(pos) : pos;
            f.Pos = pos;
            // the light's colour: the brighter bound, falling to the other over the light's duration (as the Unity light does)
            FxColor hi = d.ColHi, lo = d.ColLo;
            if (Brightness(lo) > Brightness(hi)) { var t = hi; hi = lo; lo = t; }
            f.From = new Color(hi.R, hi.G, hi.B, 1f);
            f.To = d.Duration > 0f ? new Color(lo.R, lo.G, lo.B, 1f) : f.From;
            if (slot >= _flareHigh) _flareHigh = slot + 1;
            return slot;
        }

        /// <summary>Starts a light volume (mgs2 widget) on <paramref name="attach"/> (its forward = the object's forward). Slot or -1.</summary>
        internal int StartVolume(FxLightVolumeDef v, Transform attach, bool firstPerson)
        {
            if (v == null || attach == null) return -1;
            int slot = -1;
            for (int i = 0; i < _flares.Length; i++) if (!_flares[i].Alive) { slot = i; break; }
            if (slot < 0) return -1;
            ref FxFlare f = ref _flares[slot];
            f.Alive = true; f.Gen = ++_gen; f.Light = null; f.Volume = v; f.XForward = false; f.Age = 0f; f.Life = float.MaxValue; f.Scale = 1f; f.Rnd = 0f;
            f.FirstPerson = firstPerson;
            f.Attach = attach; f.Attached = true;
            f.LocalPos = Vector3.zero; f.Pos = attach.position;
            if (slot >= _flareHigh) _flareHigh = slot + 1;
            return slot;
        }

        internal void KillFlare(int i)
        {
            if (i < 0 || i >= _flares.Length) return;
            _flares[i].Alive = false; _flares[i].Attach = null; _flares[i].Light = null; _flares[i].Volume = null;
        }

        internal bool FlareAlive(int slot, int gen) { return slot >= 0 && slot < _flares.Length && _flares[slot].Alive && _flares[slot].Gen == gen; }
        internal int FlareGen(int slot) { return slot >= 0 && slot < _flares.Length ? _flares[slot].Gen : 0; }
        internal void FlareScale(int slot, float scale) { if (slot >= 0 && slot < _flares.Length) _flares[slot].Scale = scale; }
        internal void FlareXForward(int slot, bool x) { if (slot >= 0 && slot < _flares.Length) _flares[slot].XForward = x; }

        void UpdateFlares(float dt)
        {
            int high = 0;
            for (int i = 0; i < _flareHigh; i++)
            {
                if (!_flares[i].Alive) continue;
                ref FxFlare f = ref _flares[i];
                f.Age += dt;
                if (f.Attached && (f.Attach == null || !f.Attach.gameObject.activeInHierarchy)) { KillFlare(i); continue; }
                if (f.Age >= f.Life) { KillFlare(i); continue; }
                if (f.Attached) f.Pos = f.Attach.TransformPoint(f.LocalPos);
                high = i + 1;
            }
            _flareHigh = high;
        }

        void DrawFlares(Vector3 eye)
        {
            float u = HaloSpace.Unit;
            for (int i = 0; i < _flareHigh; i++)
            {
                if (!_flares[i].Alive) continue;
                ref FxFlare f = ref _flares[i];
                if (f.Volume != null) { DrawVolume(ref f, eye); continue; }
                var d = f.Light;
                var lens = d != null ? d.Lens : null;
                if (lens == null || f.Scale <= 0.001f) continue;
                if (lens.OnlyFirstPerson && !f.FirstPerson) continue;
                if (lens.OnlyThirdPerson && f.FirstPerson) continue;
                Vector3 c = f.Attached && f.Attach != null ? f.Attach.TransformPoint(f.LocalPos) : f.Pos;
                Vector3 toEye = eye - c;
                float dist = toEye.magnitude;
                if (dist < 1e-3f) continue;
                toEye /= dist;
                // the light's intensity (its attachment's function: the plasma pistol's charge, a lamp's power; 1 = full) picks the point
                // between every reflection's radius and brightness BOUNDS - not a brightness multiplier (see below)
                float input = Mathf.Clamp01(f.Scale);
                float vis = 1f;
                // fades with distance: fully there up to "near fade distance", gone at "far fade distance" (as the World module's flares)
                float near = lens.NearFade * u, far = lens.FarFade * u;
                if (far > near && far > 0f) vis *= 1f - Mathf.Clamp01((dist - Mathf.Max(near, 0f)) / (far - Mathf.Max(near, 0f)));
                // the lens's own falloff / cutoff angles (cache-only cosines) between the object's forward and the eye: 1 inside the falloff
                // angle, 0 past the cutoff. They belong to the LENS, whatever the light's own angles: the needle's all-around light has a
                // lens at 100 / 160 deg (seen from anywhere but behind), the plasma pistol bolt's all-around light reuses the AR flashlight
                // lens at 20 / 75 deg (its flare shows only when the bolt comes at you; from the side / behind the bolt is its light
                // volume), plasma rifle / ghost bolt lenses are 0 / 0 (no angle test).
                Vector3 fwd = f.Attach != null ? (f.XForward || HaloFxMarkers.IsMarker(f.Attach) ? f.Attach.right : f.Attach.forward) : Vector3.forward;
                if (lens.CosCutoff < lens.CosFalloff - 0.0005f)
                    vis *= Mathf.Clamp01((Vector3.Dot(fwd, toEye) - lens.CosCutoff) / (lens.CosFalloff - lens.CosCutoff));
                if (vis <= 0.002f) continue;
                Color lc = f.From;
                if (f.Life < float.MaxValue && d.Duration > 0f) lc = Color.Lerp(f.From, f.To, Falloff(d.Falloff, Mathf.Clamp01(f.Age / f.Life)));
                var refl = lens.Reflections;
                for (int k = 0; k < refl.Length; k++)
                {
                    var r = refl[k];
                    // ghosts along the screen line (position != 0) are not drawn (ours). "Radius / brightness scaled by" (rotation,
                    // rotation and strafing, distance from centre) is the input that picks a value between the radius / brightness BOUNDS
                    // (the fields are bounds, like every "interpolated by an input" Halo field); it is not a multiplier. Every projectile
                    // lens has equal bounds (plasma pistol 0.7..0.7 / 0.3..0.3), so the input changes nothing there. With no "scaled by"
                    // input the bounds follow the LIGHT's intensity, as the light's own radius modifier and colour bounds do: the plasma
                    // pistol's overcharge.lens (radius 0..0.175 wu, brightness 0..1; overcharge.ligh radius modifier 0..1, colour 0..upper)
                    // grows from nothing to its full size with the charge - read with a random radius it was up to 0.53 m from the first
                    // moment of the charge and only its brightness followed. (The camera inputs - rotation, strafing, distance from the
                    // screen centre - are not modelled; ours.)
                    if (Mathf.Abs(r.Position) > 0.001f) continue;
                    // "radius NOT scaled by distance" (flag 2): a screen-space size (suns only) - not drawn here (ours)
                    if ((r.Flags & 2) != 0) continue;
                    var seq = lens.Sheet.Sequence(r.Bitmap);
                    if (seq == null || seq.Frames.Length == 0) continue;
                    var fr = seq.Frames[0];
                    var batch = Render.Get(FxSheetGpu.Texture(lens.Sheet, fr.Image), 3, 7);
                    if (batch.Mesh == null) continue;
                    float rad = (r.RadMin + (r.RadMax - r.RadMin) * input) * u;
                    float bri = (r.BriMin + (r.BriMax - r.BriMin) * input) * vis;
                    if (rad <= 0f || bri <= 0.002f) continue;
                    Color tint = r.Tint.A > 0.001f ? new Color(r.Tint.R, r.Tint.G, r.Tint.B, 1f) : lc;
                    float hw = rad * lens.HScale, hh = rad * lens.VScale;
                    // pulled towards the eye by up to its radius so the surface it flies along does not cut the glow in half (ours)
                    Vector3 p = c + toEye * Mathf.Min(Mathf.Max(hw, hh), dist * 0.5f);
                    Color32 col = ToColor32(tint.r * bri, tint.g * bri, tint.b * bri, 1f);
                    float rot = r.RotOffset * Mathf.Deg2Rad, cs2 = Mathf.Cos(rot), sn = Mathf.Sin(rot);
                    batch.Quad(p, p, p, p,
                        new Vector4(fr.U0, fr.V0, -hw * cs2 - hh * sn, -hw * sn + hh * cs2), new Vector4(fr.U1, fr.V0, hw * cs2 - hh * sn, hw * sn + hh * cs2),
                        new Vector4(fr.U1, fr.V1, hw * cs2 + hh * sn, hw * sn - hh * cs2), new Vector4(fr.U0, fr.V1, -hw * cs2 + hh * sn, -hw * sn - hh * cs2),
                        new Vector4(0f, 0f, 0f, 0f), col, Mathf.Max(hw, hh));
                }
            }
        }
        /// <summary>
        /// A light volume (mgs2): <c>Count</c> screen-facing glow sprites along the object's forward axis, additive (gamma add, as the flares).
        /// Sprite i of n at t = i / (n - 1): position offset + length * t^offset exponent (wu; hither = t 0 = the tail, yon = t 1 = the head),
        /// radius hither..yon by t^radius exponent (the half width, the glow bitmap fills its square), colour hither..yon by t^tint
        /// exponent, brightness = the tint's alpha hither..yon by t^brightness exponent. Plasma pistol bolt: 20 sprites from 0.5 wu
        /// behind to 0.2 wu ahead, 0.035 -> 0.045 wu, green, brightness 0.05 -> 0.35 - a green ball with a short tail from every angle.
        /// Ours: the "perpendicular / parallel brightness scale" are blended by how much the axis points at the eye (|cos|); frame 0 only
        /// (the frame / brightness function sources are object functions projectiles do not have); fully there up to "near fade
        /// distance", gone at "far fade distance"; sprites within 0.3 m of the eye fade out (the first-person bolt's tail passes the head).
        /// </summary>
        void DrawVolume(ref FxFlare f, Vector3 eye)
        {
            var v = f.Volume;
            if (f.Attach == null || f.Scale <= 0.001f || v.Frames.Length == 0) return;
            float u = HaloSpace.Unit;
            Vector3 c = f.Attach.position;
            Vector3 fwd = f.XForward || HaloFxMarkers.IsMarker(f.Attach) ? f.Attach.right : f.Attach.forward;
            Vector3 toEye = eye - c;
            float dist = toEye.magnitude;
            if (dist < 1e-3f) return;
            toEye /= dist;
            float vis = f.Scale;
            float near = v.NearFade * u, far = v.FarFade * u;
            if (far > near && far > 0f) vis *= 1f - Mathf.Clamp01((dist - Mathf.Max(near, 0f)) / (far - Mathf.Max(near, 0f)));
            vis *= Mathf.Lerp(v.Perpendicular, v.Parallel, Mathf.Abs(Vector3.Dot(fwd, toEye)));
            if (vis <= 0.002f) return;
            var seq = v.Sheet.Sequence(v.Sequence);
            if (seq == null || seq.Frames.Length == 0) return;
            var fr = seq.Frames[0];
            var batch = Render.Get(FxSheetGpu.Texture(v.Sheet, fr.Image), 3, 7);
            if (batch.Mesh == null) return;
            var k = v.Frames[0];
            int n = v.Count;
            for (int i = 0; i < n; i++)
            {
                float t = n > 1 ? i / (float)(n - 1) : 1f;
                Vector3 p = c + fwd * ((k.Offset + k.Length * Pow(t, k.OffsetExp)) * u);
                float rad = (k.RadHither + (k.RadYon - k.RadHither) * Pow(t, k.RadExp)) * u;
                float tc = Pow(t, k.TintExp), tb = Pow(t, k.BrightExp);
                float bri = (k.TintHither.A + (k.TintYon.A - k.TintHither.A) * tb) * vis;
                float de = (p - eye).magnitude;
                if (de < 0.3f) bri *= Mathf.Clamp01((de - 0.1f) / 0.2f);
                if (rad <= 0f || bri <= 0.002f) continue;
                float r = k.TintHither.R + (k.TintYon.R - k.TintHither.R) * tc;
                float g = k.TintHither.G + (k.TintYon.G - k.TintHither.G) * tc;
                float b = k.TintHither.B + (k.TintYon.B - k.TintHither.B) * tc;
                Color32 col = ToColor32(r * bri, g * bri, b * bri, 1f);
                batch.Quad(p, p, p, p,
                    new Vector4(fr.U0, fr.V0, -rad, rad), new Vector4(fr.U1, fr.V0, rad, rad),
                    new Vector4(fr.U1, fr.V1, rad, -rad), new Vector4(fr.U0, fr.V1, -rad, -rad),
                    new Vector4(0f, 0f, 0f, 0f), col, rad);
            }
        }

        static float Pow(float t, float e) { return e > 0f && e != 1f ? Mathf.Pow(t, e) : t; }
    }
}

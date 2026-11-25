using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    internal struct FxDecal
    {
        public bool Alive;
        public FxDecalDef Def;
        public FxBatch Batch;
        public Vector3 P0, P1, P2, P3;
        public Vector2 T0, T1, T2, T3;
        public Color Col;
        public float Age, Life, Decay;
    }

    internal struct FxLightSlot
    {
        public Light L;
        public bool Alive, Permanent;
        public int Gen;
        public FxLightDef Def;
        public Transform Attach;
        public bool Attached;
        public Vector3 LocalPos;
        public float Age, Life, Range, Scale;
        public Color From, To;
    }

    internal sealed partial class HaloFxRuntime
    {
        public const int MaxDecals = 512;   // ours (Halo PC keeps a few hundred world decals)
        public const int MaxLights = 8;     // ours: dynamic effect lights at once (URP's per-object limit is 8)
        /// <summary>ours: Unity intensity of an effect light at full colour.</summary>
        public static float LightIntensity = 2.5f;

        FxDecal[] _decals = new FxDecal[MaxDecals];
        int _decalNext, _decalLive;
        FxLightSlot[] _lights;

        internal int DecalCount { get { return _decalLive; } }
        internal int LightCount { get { int n = 0; if (_lights != null) for (int i = 0; i < _lights.Length; i++) if (_lights[i].Alive) n++; return n; } }

        // ------------------------------------------------------------------ decals

        void ClearDecals()
        {
            for (int i = 0; i < _decals.Length; i++) { _decals[i].Alive = false; _decals[i].Batch = null; _decals[i].Def = null; }
            _decalLive = 0; _decalNext = 0;
        }

        /// <summary>Casts from <paramref name="origin"/> along <paramref name="dir"/> (up to <paramref name="reach"/> metres) onto the world and
        /// puts the decal (and its chain) where it lands.</summary>
        internal bool ProjectDecal(FxDecalDef d, Vector3 origin, Vector3 dir, float reach, float scale)
        {
            if (dir.sqrMagnitude < 1e-8f) dir = Vector3.down;
            dir.Normalize();
            RaycastHit hit;
            if (!Physics.Raycast(origin - dir * 0.1f, dir, out hit, reach + 0.1f, HaloFx.DecalMask, QueryTriggerInteraction.Ignore)) return false;
            PlaceDecal(d, hit.point, hit.normal, scale, UnityEngine.Random.value * 2f * Mathf.PI, 0);
            return true;
        }

        internal void PlaceDecal(FxDecalDef d, Vector3 point, Vector3 normal, float scale, float angle, int depth)
        {
            if (d == null || d.Sheet == null || depth > 4) return;
            var seq = d.Sheet.Sequence(UnityEngine.Random.Range(0, d.Sheet.Sequences.Length));
            if (seq == null) return;
            var fr = seq.Frames[UnityEngine.Random.Range(0, seq.Frames.Length)];
            if (normal.sqrMagnitude < 1e-8f) normal = Vector3.up;
            normal.Normalize();
            float r = Rand(d.RadMin, d.RadMax) * HaloSpace.Unit * scale;
            float aspect = fr.PxW > 0f ? fr.PxH / fr.PxW : 1f;
            Vector3 ax = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.95f ? Vector3.up : Vector3.forward).normalized;
            Vector3 ay = Vector3.Cross(ax, normal);
            if (!d.NoRandomRotation)
            {
                float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
                Vector3 rx = ax * cs + ay * sn; ay = ay * cs - ax * sn; ax = rx;
            }
            Vector3 c = point + normal * 0.004f;
            Vector3 hx = ax * r, hy = ay * (r * aspect);
            int i = _decalNext;
            _decalNext = (_decalNext + 1) % _decals.Length;
            if (!_decals[i].Alive) _decalLive++;
            ref FxDecal k = ref _decals[i];
            k.Alive = true; k.Def = d; k.Age = 0f;
            k.Life = Rand(d.LifeMin, d.LifeMax); if (k.Life <= 0f) k.Life = float.MaxValue;
            k.Decay = Mathf.Max(0.01f, Rand(d.DecayMin, d.DecayMax));
            k.P0 = c - hx + hy; k.P1 = c + hx + hy; k.P2 = c + hx - hy; k.P3 = c - hx - hy;
            k.T0 = new Vector2(fr.U0, fr.V0); k.T1 = new Vector2(fr.U1, fr.V0); k.T2 = new Vector2(fr.U1, fr.V1); k.T3 = new Vector2(fr.U0, fr.V1);
            float t = UnityEngine.Random.value;
            FxColor col = FxColor.Lerp(d.ColLo, d.ColHi, t);
            float intensity = Rand(d.IntMin, d.IntMax);
            k.Col = new Color(col.R, col.G, col.B, intensity);
            k.Batch = Render.Ready ? Render.Get(FxSheetGpu.Texture(d.Sheet, fr.Image), d.Blend, 2 + Mathf.Clamp(d.Layer, 0, 4)) : null;
            // the chain: the next decal in the same spot (plasma burns: scorch, inner glow, outer glow)
            if (d.Next != null && Lib != null) PlaceDecal(Lib.Decal(d.Next), point, normal, scale, angle, depth + 1);
        }

        void UpdateDecals(float dt)
        {
            for (int i = 0; i < _decals.Length; i++)
            {
                if (!_decals[i].Alive) continue;
                ref FxDecal k = ref _decals[i];
                k.Age += dt;
                if (k.Age >= k.Life + k.Decay) { k.Alive = false; k.Batch = null; k.Def = null; _decalLive--; }
            }
        }

        void DrawDecals()
        {
            var kind = new Vector4(0f, 0f, 0f, 2f);
            for (int i = 0; i < _decals.Length; i++)
            {
                if (!_decals[i].Alive) continue;
                ref FxDecal k = ref _decals[i];
                if (k.Batch == null || k.Batch.Mesh == null) continue;
                float a = k.Col.a;
                if (k.Age > k.Life) a *= 1f - Mathf.Clamp01((k.Age - k.Life) / k.Decay);
                if (a <= 0.002f) continue;
                Color32 c = ToColor32(k.Col.r, k.Col.g, k.Col.b, a);
                k.Batch.Quad(k.P0, k.P1, k.P2, k.P3,
                    new Vector4(k.T0.x, k.T0.y, 0f, 0f), new Vector4(k.T1.x, k.T1.y, 0f, 0f), new Vector4(k.T2.x, k.T2.y, 0f, 0f), new Vector4(k.T3.x, k.T3.y, 0f, 0f),
                    kind, c, 0f);
            }
        }

        // ------------------------------------------------------------------ lights

        void InitLights()
        {
            _lights = new FxLightSlot[MaxLights];
            for (int i = 0; i < MaxLights; i++)
            {
                var go = new GameObject("fx light " + i);
                go.transform.SetParent(transform, false);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.Auto;
                l.enabled = false;
                _lights[i].L = l;
            }
        }

        void DestroyLights()
        {
            if (_lights == null) return;
            for (int i = 0; i < _lights.Length; i++) if (_lights[i].L != null) Destroy(_lights[i].L.gameObject);
            _lights = null;
        }

        void ClearLights()
        {
            if (_lights == null) return;
            for (int i = 0; i < _lights.Length; i++) { _lights[i].Alive = false; _lights[i].Attach = null; if (_lights[i].L != null) _lights[i].L.enabled = false; }
        }

        static float Brightness(FxColor c) { return c.R + c.G + c.B; }

        /// <summary>A light of a ligh tag. <paramref name="eventDuration"/> is how long the effect event that made it lasts (lights with no
        /// duration of their own flash for that long, at least two ticks). Returns the slot (-1 = none free).</summary>
        internal int SpawnLight(FxLightDef d, Vector3 pos, Transform attach, float eventDuration, bool permanent = false)
        {
            if (_lights == null || d == null) return -1;
            int slot = -1;
            float oldest = -1f;
            for (int i = 0; i < _lights.Length; i++)
            {
                if (!_lights[i].Alive) { slot = i; break; }
                // full: the oldest flash goes first, an object's own light (permanent) only when every slot is one
                float age = _lights[i].Permanent ? _lights[i].Age * 0.001f : _lights[i].Age + 1000f;
                if (age > oldest) { oldest = age; slot = i; }
            }
            if (slot < 0) return -1;
            ref FxLightSlot s = ref _lights[slot];
            s.Alive = true; s.Permanent = permanent; s.Gen = ++_gen; s.Def = d; s.Age = 0f;
            s.Attach = attach; s.Attached = attach != null;
            s.LocalPos = attach != null ? attach.InverseTransformPoint(pos) : pos;
            // ligh "duration" is read as game ticks (frag explosion 45 = 1.5 s, quick flash 4.5 = 0.15 s) - the tag says seconds, which no
            // effect means (a 45 s grenade light) - ours
            s.Life = permanent ? float.MaxValue : d.Duration > 0f ? d.Duration / 30f : Mathf.Max(eventDuration, 2f / 30f);
            s.Range = Mathf.Max(0.5f, d.Radius * Rand(d.RadModMin, d.RadModMax) * HaloSpace.Unit);
            s.Scale = 1f;
            // the brighter bound is where the light starts; it falls to the other over its duration
            FxColor hi = d.ColHi, lo = d.ColLo;
            if (Brightness(lo) > Brightness(hi)) { var t = hi; hi = lo; lo = t; }
            s.From = new Color(hi.R, hi.G, hi.B, 1f);
            s.To = d.Duration > 0f ? new Color(lo.R, lo.G, lo.B, 1f) : s.From;
            var l = s.L;
            l.transform.position = pos;
            l.range = s.Range;
            l.color = s.From;
            l.intensity = LightIntensity;
            l.enabled = true;
            return slot;
        }

        static float Falloff(int fn, float t)
        {
            switch (fn)
            {
                case 1: return Mathf.Sqrt(t);                       // early
                case 2: return Mathf.Sqrt(Mathf.Sqrt(t));           // very early
                case 3: return t * t;                               // late
                case 4: return t * t * t * t;                       // very late
                case 5: return 0.5f - 0.5f * Mathf.Cos(t * Mathf.PI); // cosine
            }
            return t;
        }

        void UpdateLights(float dt)
        {
            if (_lights == null) return;
            for (int i = 0; i < _lights.Length; i++)
            {
                if (!_lights[i].Alive) continue;
                ref FxLightSlot s = ref _lights[i];
                s.Age += dt;
                if (s.Attached && s.Attach == null) { if (s.Permanent) { KillLight(i); continue; } s.Attached = false; }
                if (s.Age >= s.Life) { KillLight(i); continue; }
                if (s.Attached) s.L.transform.position = s.Attach.TransformPoint(s.LocalPos);
                float k = s.Life < float.MaxValue && s.Def.Duration > 0f ? Falloff(s.Def.Falloff, s.Age / s.Life) : 0f;
                Color c = Color.Lerp(s.From, s.To, k);
                s.L.color = c;
                s.L.intensity = LightIntensity * s.Scale;
                s.L.enabled = s.Scale > 0.001f;
            }
        }

        internal void KillLight(int i)
        {
            if (_lights == null || i < 0 || i >= _lights.Length) return;
            _lights[i].Alive = false; _lights[i].Attach = null; _lights[i].Permanent = false;
            if (_lights[i].L != null) _lights[i].L.enabled = false;
        }

        internal bool LightAlive(int slot, int gen) { return _lights != null && slot >= 0 && slot < _lights.Length && _lights[slot].Alive && _lights[slot].Gen == gen; }
        internal int LightGen(int slot) { return _lights != null && slot >= 0 && slot < _lights.Length ? _lights[slot].Gen : 0; }
        internal void LightScale(int slot, float scale) { if (_lights != null && slot >= 0 && slot < _lights.Length) _lights[slot].Scale = scale; }
    }
}

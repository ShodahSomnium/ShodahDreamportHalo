using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    internal struct FxTrailPoint
    {
        public Vector3 Pos, Vel;
        public float Rnd, StateT, Dur, Trans;
        public int State;
        public bool Dead;
    }

    /// <summary>A contrail (cont) behind a moving head (a projectile's transform or positions pushed by the caller). Pooled.</summary>
    internal sealed class FxTrail
    {
        public bool Alive;
        public int Gen;
        public FxContrailDef Def;
        public Transform Follow;
        public bool Following, Stopped, HasHead;
        public Vector3 Head, LastHead, HeadVel;
        public FxTrailPoint[] Pts = new FxTrailPoint[16];
        public int Start, Count;
        public float Accum, T, MaxLife;

        public ref FxTrailPoint At(int k) { return ref Pts[(Start + k) % Pts.Length]; }
    }

    internal sealed partial class HaloFxRuntime
    {
        readonly List<FxTrail> _trails = new List<FxTrail>();
        readonly Stack<FxTrail> _freeTrails = new Stack<FxTrail>();
        const int MaxTrailPoints = 512; // ours

        internal int TrailCount { get { return _trails.Count; } }

        internal FxTrail StartTrail(HaloTag contrail, Transform follow, Vector3 start)
        {
            if (contrail == null) return null;
            UseMap(contrail.Map);
            var def = Lib.Contrail(contrail);
            if (def == null) { Missing(contrail, "not a contrail"); return null; }
            FxTrail t = _freeTrails.Count > 0 ? _freeTrails.Pop() : new FxTrail();
            t.Alive = true; t.Gen = ++_gen; t.Def = def; t.Follow = follow; t.Following = follow != null; t.Stopped = false;
            t.Head = follow != null ? follow.position : start; t.LastHead = t.Head; t.HasHead = true; t.HeadVel = Vector3.zero;
            t.Start = 0; t.Count = 0; t.Accum = 0f; t.T = 0f;
            float life = 0f;
            for (int i = 0; i < def.States.Length - 1; i++) life += Mathf.Max(def.States[i].DurMin, def.States[i].DurMax) + Mathf.Max(def.States[i].TransMin, def.States[i].TransMax);
            t.MaxLife = Mathf.Max(0.02f, life);
            int cap = Mathf.Clamp(Mathf.CeilToInt(def.Rate * t.MaxLife) + 8, 8, MaxTrailPoints);
            if (t.Pts.Length < cap) t.Pts = new FxTrailPoint[cap];
            _trails.Add(t);
            return t;
        }

        void ClearTrails()
        {
            for (int i = 0; i < _trails.Count; i++) { _trails[i].Alive = false; _trails[i].Follow = null; _freeTrails.Push(_trails[i]); }
            _trails.Clear();
        }

        void UpdateTrails(float dt)
        {
            for (int i = _trails.Count - 1; i >= 0; i--)
            {
                var t = _trails[i];
                if (t.Alive) StepTrail(t, dt);
                if (!t.Alive) { _trails.RemoveAt(i); t.Follow = null; _freeTrails.Push(t); }
            }
        }

        void StepTrail(FxTrail t, float dt)
        {
            var d = t.Def;
            t.T += dt;
            if (t.Following)
            {
                if (t.Follow == null || !t.Follow.gameObject.activeInHierarchy) { t.Following = false; t.Stopped = true; }
                else t.Head = t.Follow.position;
            }
            if (!t.Stopped)
            {
                t.HeadVel = (t.Head - t.LastHead) / dt;
                t.Accum += d.Rate * dt;
                int n = (int)t.Accum;
                t.Accum -= n;
                for (int j = 1; j <= n; j++)
                {
                    float f = n > 0 ? (float)j / n : 1f;
                    AddTrailPoint(t, Vector3.Lerp(t.LastHead, t.Head, f), dt * (1f - f));
                }
            }
            t.LastHead = t.Head;

            // age the points
            var states = d.States;
            int last = states.Length - 1;
            for (int k = 0; k < t.Count; k++)
            {
                ref FxTrailPoint p = ref t.At(k);
                if (p.Dead) continue;
                p.StateT += dt;
                while (p.StateT >= p.Dur + p.Trans)
                {
                    if (p.State >= last) { p.Dead = true; break; }
                    p.StateT -= p.Dur + p.Trans;
                    p.State++;
                    if (p.State >= last) { p.Dead = true; break; }
                    var ns = states[p.State];
                    p.Dur = Rand(ns.DurMin, ns.DurMax); p.Trans = Rand(ns.TransMin, ns.TransMax);
                }
                if (p.Dead) { p.State = last; continue; }
                var ph = states[p.State].Physics;
                Vector3 v = p.Vel;
                if (ph != null)
                {
                    if (!ph.NoGravity) v.y += Gravity * ph.AirGravity * dt;
                    float drag = ph.Drag;
                    if (drag > 0f) v /= 1f + drag / Mathf.Max(0.01f, states[p.State].Width * HaloSpace.Unit * 0.5f) * v.magnitude * dt;
                }
                p.Vel = v;
                p.Pos += v * dt;
            }
            while (t.Count > 0 && t.At(0).Dead) { t.Start = (t.Start + 1) % t.Pts.Length; t.Count--; }
            if (t.Stopped && t.Count == 0) t.Alive = false;
        }

        void AddTrailPoint(FxTrail t, Vector3 pos, float age)
        {
            var d = t.Def;
            if (t.Count == t.Pts.Length) { t.Start = (t.Start + 1) % t.Pts.Length; t.Count--; }
            ref FxTrailPoint p = ref t.At(t.Count);
            t.Count++;
            var s0 = d.States[0];
            p.Pos = pos; p.Rnd = UnityEngine.Random.value; p.State = 0; p.StateT = age; p.Dead = d.States.Length < 2;
            p.Dur = Rand(s0.DurMin, s0.DurMax); p.Trans = Rand(s0.TransMin, s0.TransMax);
            float speed = Rand(d.VelMin, d.VelMax) * HaloSpace.Unit;
            Vector3 dir = d.Cone > 0f && t.HeadVel.sqrMagnitude > 1e-4f ? Cone(-t.HeadVel, d.Cone) : UnityEngine.Random.onUnitSphere;
            p.Vel = dir * speed + t.HeadVel * d.Inherit;
        }

        void TrailState(FxContrailDef d, ref FxTrailPoint p, out float width, out Color col)
        {
            var states = d.States;
            var a = states[Mathf.Min(p.State, states.Length - 1)];
            var b = a;
            float k = 0f;
            if (!p.Dead && p.State < states.Length - 1 && p.StateT > p.Dur)
            {
                b = states[p.State + 1];
                k = p.Trans > 0f ? Mathf.Clamp01((p.StateT - p.Dur) / p.Trans) : 1f;
            }
            width = (a.Width + (b.Width - a.Width) * k) * HaloSpace.Unit;
            FxColor ca = FxColor.Lerp(a.ColLo, a.ColHi, p.Rnd), cb = FxColor.Lerp(b.ColLo, b.ColHi, p.Rnd);
            FxColor c = FxColor.Lerp(ca, cb, k);
            col = new Color(c.R, c.G, c.B, c.A);
        }

        void DrawTrails()
        {
            for (int i = 0; i < _trails.Count; i++)
            {
                var t = _trails[i];
                var d = t.Def;
                var seq = d.Sheet.Sequence(d.FirstSeq);
                if (seq == null) continue;
                var fr = seq.Frames[0];
                var batch = Render.Get(FxSheetGpu.Texture(d.Sheet, fr.Image), d.Blend, 1);
                if (batch.Mesh == null) continue;
                int n = t.Count + (t.Stopped ? 0 : 1);
                if (n < 2) continue;
                // the head where the followed object is NOW (projectiles move after the effects' Update: a fast bolt's trail lagged its glow by a frame)
                if (t.Following && t.Follow != null && t.Follow.gameObject.activeInHierarchy) t.Head = t.Follow.position;
                // total length (head first, newest to oldest) for the u coordinate
                float total = 0f;
                Vector3 prev = t.Stopped ? t.At(t.Count - 1).Pos : t.Head;
                for (int k = t.Count - 1 - (t.Stopped ? 1 : 0); k >= 0; k--) { Vector3 q = t.At(k).Pos; total += (q - prev).magnitude; prev = q; }
                if (total < 1e-4f) continue;
                float scroll = t.T * d.AnimU;
                float s = 0f;
                // vertex A = the newer end of a segment, B = the older
                Vector3 pa; float wa; Color ca;
                int kStart;
                if (t.Stopped) { ref FxTrailPoint p0 = ref t.At(t.Count - 1); pa = p0.Pos; TrailState(d, ref p0, out wa, out ca); kStart = t.Count - 2; }
                else
                {
                    pa = t.Head;
                    var s0 = d.States[0];
                    FxColor c0 = FxColor.Lerp(s0.ColLo, s0.ColHi, 0.5f);
                    wa = s0.Width * HaloSpace.Unit; ca = new Color(c0.R, c0.G, c0.B, c0.A);
                    kStart = t.Count - 1;
                }
                // contrail flags "first point unfaded" / "last point unfaded": WITHOUT them the strip's newest end (the head) / oldest end fades
                // to nothing. Only the AR / scorpion bullet tracers keep a bright head (flag 1); every plasma / needle / rocket / sniper
                // trail fades in from its head - drawn at full colour, the charged plasma bolt's 0.5 wu (1.5 m) wide head was a hard
                // vertical edge from the side.
                if (!d.FirstPointUnfaded) ca.a = 0f;
                for (int k = kStart; k >= 0; k--)
                {
                    ref FxTrailPoint pb = ref t.At(k);
                    float wb; Color cb;
                    TrailState(d, ref pb, out wb, out cb);
                    if (k == 0 && !d.LastPointUnfaded) cb.a = 0f;
                    Vector3 axis = pa - pb.Pos;
                    float len = axis.magnitude;
                    if (len > 1e-5f)
                    {
                        float ua = (s / total) * d.RepU - scroll, ub = ((s + len) / total) * d.RepU - scroll;
                        float va = d.RepV;
                        var kind = new Vector4(axis.x, axis.y, axis.z, 1f);
                        Color32 a32 = ToColor32(ca.r, ca.g, ca.b, ca.a), b32 = ToColor32(cb.r, cb.g, cb.b, cb.a);
                        batch.Quad4(pa, pa, pb.Pos, pb.Pos,
                            new Vector4(ua, 0f, 0f, -wa * 0.5f), new Vector4(ua, va, 0f, wa * 0.5f), new Vector4(ub, va, 0f, wb * 0.5f), new Vector4(ub, 0f, 0f, -wb * 0.5f),
                            kind, kind, kind, kind, a32, a32, b32, b32, Mathf.Max(wa, wb));
                        s += len;
                    }
                    pa = pb.Pos; wa = wb; ca = cb;
                }
            }
        }

        internal bool TrailAlive(FxTrail t, int gen) { return t != null && t.Alive && t.Gen == gen; }
    }
}

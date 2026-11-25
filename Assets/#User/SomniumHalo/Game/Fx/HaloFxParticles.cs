using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>One particle of the pool: a particle tag's (kind 0) or a particle system's (kind 1).</summary>
    internal struct FxParticle
    {
        public bool Alive;
        public byte Kind;
        public FxParticleDef Def;
        public FxSpriteSheet Sheet;
        public FxSys Sys;
        public int SysGen, Type;
        public Transform Attach;
        public bool Attached;
        public Vector3 Pos, Vel, Dir;
        public float Age, Life, Radius, Rot, RotVel, Anim, PhaseStart, Rnd;
        public Color Tint;
        public int Seq, Frame0, FrameIdx;
        /// <summary>"animate once per frame": the game tick (1/30 s) of the last frame change.</summary>
        public int Tick;
        public byte Phase;
        public bool Backwards, MirU, MirV, Rest;
        /// <summary>A first-person particle of the local player's own gun (camera mode "only in first person" / "in first person if
        /// possible"): Halo draws those in the view-model space, where they never land in the world - here they end at the first
        /// contact with the world (the VR gun's casings must not pile up at the player's feet for the tag's 30 s).</summary>
        public bool ViewModel;
        public FxPhysicsDef Phys;
        public int PState, Bounces;
        public float StateT, StateDur, TransDur;
        // computed by the draw pass
        public FxFrame CurFrame;
        public FxBatch CurBatch;
    }

    /// <summary>A running particle system (pctl), pooled.</summary>
    internal sealed class FxSys
    {
        public bool Alive;
        public int Gen;
        public FxSystemDef Def;
        public Transform Attach;
        public bool Attached, FirstPerson, StopRequested;
        public Vector3 LocalPos, Pos, F, L, U;
        public Quaternion LocalRot;
        public float Age;
        public int Live;
        public int[] State = new int[2];
        public float[] StateT = new float[2], StateDur = new float[2], TransDur = new float[2], Accum = new float[2];
        public Color[] Mul = new Color[2];
    }

    internal sealed partial class HaloFxRuntime
    {
        /// <summary>Halo's gravity: 0.0035651 world units per tick^2 at 30 ticks a second = 3.21 wu/s^2, in m/s^2.</summary>
        const float Gravity = 0.0035651f * 900f * HaloSpace.Unit;
        public const int MaxParticles = 4096;    // ours: pool size (Halo PC's own particle budget is 1024; we keep more for VR's two eyes' worth of nothing - headroom)
        const int RaycastBudget = 192;           // ours: colliding particle tests per frame
        public static int MaxResting = 96;       // ours: particles lying at rest at once (Halo recycles particles past its budget)

        FxParticle[] _p;
        int _pHigh, _pLive;
        readonly System.Collections.Generic.List<FxSys> _systems = new System.Collections.Generic.List<FxSys>();
        readonly System.Collections.Generic.Stack<FxSys> _freeSystems = new System.Collections.Generic.Stack<FxSys>();
        int _raycasts;

        internal int ParticleCount { get { return _pLive; } }
        internal int SystemCount { get { return _systems.Count; } }

        void InitParticles() { _p = new FxParticle[MaxParticles]; _pHigh = 0; _pLive = 0; }

        void ClearParticles()
        {
            if (_p != null) for (int i = 0; i < _pHigh; i++) { _p[i].Alive = false; _p[i].Attach = null; _p[i].Def = null; _p[i].Sys = null; _p[i].Sheet = null; }
            _pHigh = 0; _pLive = 0;
            for (int i = 0; i < _systems.Count; i++) { _systems[i].Alive = false; _freeSystems.Push(_systems[i]); }
            _systems.Clear();
        }

        int _freeHint;
        int AllocFast()
        {
            if (_p == null) InitParticles();
            int n = _pHigh;
            for (int k = 0; k < n; k++)
            {
                int i = (_freeHint + k) % n;
                if (!_p[i].Alive) { _freeHint = i + 1; return i; }
            }
            if (_pHigh < _p.Length) return _pHigh++;
            return -1;
        }

        // ------------------------------------------------------------------ effect particles (EffectParticle entries)

        static float Distribution(int fn, float p)
        {
            switch (fn)
            {
                case 0: return 1f;                              // start
                case 1: return p >= 1f ? 1f : 0f;               // end
                case 2: return p;                               // constant
                case 3: return p * p;                           // buildup
                case 4: return 1f - (1f - p) * (1f - p);        // falloff
                case 5: return p * p * (3f - 2f * p);           // buildup and falloff
            }
            return 1f;
        }

        void Emit(FxRun r, FxEventDef ev, float progress)
        {
            for (int i = 0; i < ev.Emits.Length; i++)
            {
                var e = ev.Emits[i];
                if (r.Target[i] == 0)
                {
                    bool wanted = e.CreateIn != 2 && e.CreateIn != 3 && e.Violence != 2 && !e.Particle.DiesInAir
                        && !(e.Camera == 1 && !r.FirstPerson) && !(e.Camera == 2 && r.FirstPerson);
                    r.Target[i] = wanted ? RandInt(e.CountMin, e.CountMax) + 1 : 1;
                }
                int target = r.Target[i] - 1;
                if (target <= 0) continue;
                int want = Mathf.Min(target, Mathf.CeilToInt(Distribution(e.Distribution, progress) * target - 1e-4f));
                if (want <= r.Emitted[i]) continue;
                FxFrameW f;
                if (!Frame(r, e.Location, out f)) continue;
                for (int k = r.Emitted[i]; k < want; k++) SpawnEffectParticle(e, ref f, r.FirstPerson && (e.Camera == 1 || e.Camera == 3));
                r.Emitted[i] = want;
            }
        }

        void SpawnEffectParticle(FxEmitDef e, ref FxFrameW f, bool viewModel)
        {
            var d = e.Particle;
            int i = AllocFast();
            if (i < 0) return;
            StatParticlesSpawned++;
            _pLive++;
            Vector3 dir = f.Local(e.Direction);
            if (dir.sqrMagnitude < 1e-8f) dir = f.F;
            dir.Normalize();
            Vector3 pos = f.Pos + f.Local(e.Offset) * HaloSpace.Unit;
            float dr = Rand(e.DistRadMin, e.DistRadMax) * HaloSpace.Unit;
            if (dr > 0f) pos += UnityEngine.Random.insideUnitSphere * dr;
            // "velocity cone angle" is the cone's FULL apex angle (half of it either side of the direction): tags use 3.14 for a hemisphere of
            // gravel over the ground (rocket / scorpion "impact dirt") and 6.28 for an all-around spray (blood aoe) - read as a half angle the
            // gravel went half into the ground and the plasma impact sparks (1.13 = 65 deg) climbed 3 m into a column above the target
            Vector3 vel = Cone(dir, e.Cone * 0.5f) * (Rand(e.VelMin, e.VelMax) * HaloSpace.Unit);
            float t = UnityEngine.Random.value;
            FxColor c = FxColor.Lerp(e.TintLo, e.TintHi, t);
            ref FxParticle p = ref _p[i];
            p = default(FxParticle);
            p.Alive = true; p.Kind = 0; p.Def = d; p.Sheet = d.Sheet; p.Phys = d.Physics;
            p.Life = Mathf.Max(0.02f, Rand(d.LifeMin, d.LifeMax));
            p.Radius = Rand(e.RadMin, e.RadMax) * HaloSpace.Unit;
            p.RotVel = Rand(e.AngVelMin, e.AngVelMax);
            p.Rot = e.RandomAngle ? UnityEngine.Random.value * 2f * Mathf.PI : 0f;
            p.Tint = new Color(c.R, c.G, c.B, c.A);
            p.Anim = Rand(d.AnimMin, d.AnimMax);
            p.Backwards = d.CanAnimateBackwards && UnityEngine.Random.value < 0.5f;
            p.MirU = d.MirrorH && UnityEngine.Random.value < 0.5f;
            p.MirV = d.MirrorV && UnityEngine.Random.value < 0.5f;
            p.ViewModel = viewModel;
            p.Rnd = UnityEngine.Random.value;
            ChooseSequence(ref p, true);
            if (e.StayAttached && f.Attach != null)
            {
                p.Attach = f.Attach; p.Attached = true;
                p.Pos = f.Attach.InverseTransformPoint(pos);
                p.Vel = f.Attach.InverseTransformVector(vel);
                p.Dir = f.Attach.InverseTransformDirection(dir);
            }
            else { p.Pos = pos; p.Vel = vel; p.Dir = dir; }
        }

        static void ChooseSequence(ref FxParticle p, bool first)
        {
            var d = p.Def;
            if (first && d.InitialSeq > 0) { p.Seq = d.FirstSeq + UnityEngine.Random.Range(0, d.InitialSeq); p.Phase = 0; }
            else if (d.LoopSeq > 0) { p.Seq = d.FirstSeq + d.InitialSeq + UnityEngine.Random.Range(0, d.LoopSeq); p.Phase = 1; }
            else { p.Seq = d.FirstSeq; p.Phase = 0; }
            var s = p.Sheet.Sequence(p.Seq);
            int n = s != null ? s.Frames.Length : 1;
            p.Frame0 = d.RandomStartFrame ? UnityEngine.Random.Range(0, n) : 0;
            p.PhaseStart = p.Age;
        }

        // ------------------------------------------------------------------ particle systems (pctl)

        internal FxSys StartSystem(FxSystemDef def, ref FxFrameW f, bool firstPerson)
        {
            if (def.Types.Length == 0) return null;
            FxSys s = _freeSystems.Count > 0 ? _freeSystems.Pop() : new FxSys();
            int n = def.Types.Length;
            if (s.State.Length < n) { s.State = new int[n]; s.StateT = new float[n]; s.StateDur = new float[n]; s.TransDur = new float[n]; s.Accum = new float[n]; s.Mul = new Color[n]; }
            s.Alive = true; s.Gen = ++_gen; s.Def = def; s.FirstPerson = firstPerson; s.StopRequested = false; s.Age = 0f; s.Live = 0;
            s.Attach = f.Attach; s.Attached = f.Attach != null;
            s.Pos = f.Pos; s.F = f.F; s.L = f.L; s.U = f.U;
            if (s.Attached) { s.LocalPos = f.Attach.InverseTransformPoint(f.Pos); s.LocalRot = Quaternion.Inverse(f.Attach.rotation) * Quaternion.LookRotation(f.F, f.U); }
            _systems.Add(s);
            for (int t = 0; t < n; t++)
            {
                var ty = def.Types[t];
                s.State[t] = 0; s.StateT[t] = 0f; s.Accum[t] = 0f;
                var st = ty.States[0];
                s.StateDur[t] = Rand(st.DurMin, st.DurMax); s.TransDur[t] = Rand(st.TransMin, st.TransMax);
                s.Mul[t] = new Color(st.Color.R, st.Color.G, st.Color.B, st.Color.A);
                if (ty.Disabled || (ty.NotInFirstPerson && firstPerson) || (ty.NotInThirdPerson && !firstPerson)) { s.State[t] = ty.States.Length; continue; }
                for (int k = 0; k < ty.Initial; k++) SpawnSystemParticle(s, t);
            }
            return s;
        }

        void UpdateSystems(float dt)
        {
            for (int i = _systems.Count - 1; i >= 0; i--)
            {
                var s = _systems[i];
                if (s.Alive) StepSystem(s, dt);
                if (!s.Alive) { _systems.RemoveAt(i); s.Attach = null; _freeSystems.Push(s); }
            }
        }

        void StepSystem(FxSys s, float dt)
        {
            s.Age += dt;
            if (s.Attached)
            {
                if (s.Attach == null) { s.Attached = false; s.StopRequested = true; } // the object it hung on is gone
                else
                {
                    s.Pos = s.Attach.TransformPoint(s.LocalPos);
                    Quaternion q = s.Attach.rotation * s.LocalRot;
                    s.F = q * Vector3.forward; s.U = q * Vector3.up; s.L = q * Vector3.left;
                }
            }
            bool emitting = false;
            for (int t = 0; t < s.Def.Types.Length; t++)
            {
                var ty = s.Def.Types[t];
                int last = ty.States.Length - 1;
                if (s.State[t] > last) continue;
                if (s.StopRequested) { s.State[t] = last + 1; continue; }
                s.StateT[t] += dt;
                while (s.State[t] < last && s.StateT[t] >= s.StateDur[t] + s.TransDur[t])
                {
                    s.StateT[t] -= s.StateDur[t] + s.TransDur[t];
                    s.State[t]++;
                    var ns = ty.States[s.State[t]];
                    s.StateDur[t] = Rand(ns.DurMin, ns.DurMax); s.TransDur[t] = Rand(ns.TransMin, ns.TransMax);
                }
                var a = ty.States[s.State[t]];
                float k = 0f;
                FxSystemState b = a;
                if (s.State[t] < last && s.StateT[t] > s.StateDur[t] && s.TransDur[t] > 0f) { b = ty.States[s.State[t] + 1]; k = Mathf.Clamp01((s.StateT[t] - s.StateDur[t]) / s.TransDur[t]); }
                FxColor c = FxColor.Lerp(a.Color, b.Color, k);
                s.Mul[t] = new Color(c.R, c.G, c.B, c.A);
                if (s.State[t] == last && (last > 0 || s.StateT[t] >= s.StateDur[t] + s.TransDur[t]))
                {
                    if ((ty.Flags & 1) != 0)
                    {
                        // "type states loop": back to the first state (rocket exhaust keeps smoking for the whole flight)
                        var f0 = ty.States[0];
                        s.State[t] = 0; s.StateT[t] = 0f; s.StateDur[t] = Rand(f0.DurMin, f0.DurMax); s.TransDur[t] = Rand(f0.TransMin, f0.TransMax);
                    }
                    else { s.State[t] = last + 1; continue; }
                }
                emitting = true;
                float rate = a.Rate + (b.Rate - a.Rate) * k;
                if (rate > 0f)
                {
                    s.Accum[t] += rate * dt;
                    int n = (int)s.Accum[t];
                    s.Accum[t] -= n;
                    for (int j = 0; j < n && j < 64; j++) SpawnSystemParticle(s, t);
                }
            }
            if (!emitting && s.Live <= 0) s.Alive = false;
            if (s.Age > 60f) s.Alive = false; // ours: a system nobody stops (attached and never detached) ends after a minute
        }

        void SpawnSystemParticle(FxSys s, int typeIndex)
        {
            var ty = s.Def.Types[typeIndex];
            var ps = ty.ParticleStates[0];
            if (ps.Sheet == null) return;
            int i = AllocFast();
            if (i < 0) return;
            StatParticlesSpawned++;
            _pLive++;
            s.Live++;
            ref FxParticle p = ref _p[i];
            p = default(FxParticle);
            p.Alive = true; p.Kind = 1; p.Sys = s; p.SysGen = s.Gen; p.Type = typeIndex;
            p.Rnd = UnityEngine.Random.value;
            p.Sheet = ps.Sheet; p.Phys = ps.Physics;
            p.PState = 0; p.StateT = 0f; p.StateDur = Rand(ps.DurMin, ps.DurMax); p.TransDur = Rand(ps.TransMin, ps.TransMax);
            p.Life = 30f;
            p.Rot = UnityEngine.Random.value * 2f * Mathf.PI;
            p.RotVel = Lerp(ps.RotMin, ps.RotMax, p.Rnd) * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            var seq = ps.Sheet.Sequence(ps.Sequence);
            p.Seq = ps.Sequence;
            p.Frame0 = seq != null ? UnityEngine.Random.Range(0, seq.Frames.Length) : 0;
            p.Anim = Lerp(ps.AnimMin, ps.AnimMax, p.Rnd);
            Vector3 dir = UnityEngine.Random.onUnitSphere;
            Vector3 pos = s.Pos;
            Vector3 vel = Vector3.zero;
            float radius = Mathf.Max(0.01f, ty.Radius) * HaloSpace.Unit;
            switch (ty.CreationPhysics)
            {
                case 1:
                    // explosion: outwards, speed between the type's first two constants (read as world units per tick - ours)
                    vel = dir * (Rand(ty.K(0, 0.01f), ty.K(1, 0.05f)) * 30f * HaloSpace.Unit);
                    pos += dir * (UnityEngine.Random.value * 0.1f * radius);
                    break;
                case 2:
                    // jet: along the system's forward (ours: speed from the first constant read as world units per second / 10)
                    vel = Cone(s.F, 0.15f) * (ty.K(0, 10f) * 0.1f * HaloSpace.Unit);
                    break;
                default:
                    pos += UnityEngine.Random.insideUnitSphere * (0.05f * radius);
                    break;
            }
            p.Pos = pos; p.Vel = vel; p.Dir = dir;
        }

        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        // ------------------------------------------------------------------ simulation

        void UpdateParticles(float dt)
        {
            if (_p == null) return;
            _raycasts = 0;
            int resting = 0, oldest = -1;
            float oldestAge = -1f;
            for (int i = 0; i < _pHigh; i++)
            {
                if (!_p[i].Alive) continue;
                if (!StepParticle(ref _p[i], dt)) { Kill(i); continue; }
                if (_p[i].Rest) { resting++; if (_p[i].Age > oldestAge) { oldestAge = _p[i].Age; oldest = i; } }
            }
            // ours: debris at rest (casings, gravel, sparks) is capped like Halo's particle budget - the oldest goes first
            if (resting > MaxResting && oldest >= 0) Kill(oldest);
            // (death / collision effects may have spawned anywhere below _pHigh: shrink only past the last live one)
            while (_pHigh > 0 && !_p[_pHigh - 1].Alive) _pHigh--;
            if (_freeHint > _pHigh) _freeHint = 0;
        }

        void Kill(int i)
        {
            ref FxParticle p = ref _p[i];
            if (!p.Alive) return;
            p.Alive = false;
            _pLive--;
            if (p.Kind == 1 && p.Sys != null && p.Sys.Gen == p.SysGen) p.Sys.Live--;
            p.Attach = null; p.Sys = null; p.Def = null; p.Sheet = null; p.CurBatch = null; p.CurFrame = null;
        }

        /// <summary>Game ticks (1/30 s) that began during the last <paramref name="dt"/> of a particle now <paramref name="age"/> old.</summary>
        static int TicksCrossed(float age, float dt) { return (int)(age * 30f) - (int)(Mathf.Max(0f, age - dt) * 30f); }

        /// <summary>
        /// One game tick (1/30 s) of Halo's point physics on a velocity (m/s): gravity x the cached "air gravity scale" (buoyancy:
        /// (air - density) / (air + density) with air 0.0011 g/ml - smoke rises, stones fall), then quadratic air drag on a sphere of the
        /// particle's radius r: dv = -(air friction / mass scale) / r x |v| v per tick, the cache's friction (tag x 10000) over its mass
        /// scale (density x a 1 wu sphere; the ratio is unit-free, so metres and seconds give Halo's wu / tick result), applied as an explicit
        /// step that stops the particle when the drag would reverse it. Halo steps its world 30 times a second; drag that stiff
        /// (k |v| per tick >= 1) stops a particle within ONE tick: the plasma weapons' muzzle petals ("flash c generic med", cold smoke puff,
        /// 0.05 wu, 0.7-1 wu/s: 1.25-1.8 per tick) move v / 30 = 2-3 cm out of the prongs and stay there for their 1 s - the held fire's
        /// petals of every round lie on each other (a steady flame cross). Integrated continuously instead (ours until 2026-11-10) they
        /// crept on for the whole second (0.07 wu = 21 cm) and the rounds' petals stacked into a column above and below the gun.
        /// The tick integrator itself is ours (Halo's code is not available); the tag values, units and the 30 Hz tick are Halo's.
        /// </summary>
        static void PhysicsTick(FxPhysicsDef ph, float radius, ref Vector3 v, bool gravity)
        {
            const float T = 1f / 30f;
            if (gravity && !ph.NoGravity) v.y += Gravity * ph.AirGravity * T;
            float drag = ph.Drag;
            if (drag <= 0f) return;
            float f = drag / Mathf.Max(0.01f, radius) * v.magnitude * T;
            if (f >= 1f) v = Vector3.zero; else v *= 1f - f;
        }

        bool StepParticle(ref FxParticle p, float dt)
        {
            p.Age += dt;
            if (p.Kind == 0)
            {
                if (p.Age >= p.Life)
                {
                    if (p.Def.DeathEffect != null && !p.Attached) SubEffect(p.Def.DeathEffect, p.Pos, Vector3.up, p.Vel);
                    return false;
                }
            }
            else if (!StepSystemParticle(ref p, dt)) return false;

            if (p.Attached)
            {
                if (p.Attach == null) return false;
                // the particle's point physics act in the marker's frame too (drag only - no gravity in a frame that turns with the gun):
                // the muzzle-flash petals stop one tick out of the prongs (PhysicsTick)
                // (the marker's frame may be scaled - the VR gun is drawn at the avatar's size: its local units are 1 / scale metres)
                var aph = p.Phys ?? FxPhysicsDef.Vacuum;
                p.Pos += p.Vel * dt;
                int an = TicksCrossed(p.Age, dt);
                if (an > 0)
                {
                    float sc = Mathf.Max(1e-3f, p.Attach.lossyScale.x);
                    for (int k = 0; k < an && k < 4; k++) PhysicsTick(aph, p.Radius / sc, ref p.Vel, false);
                }
                p.Rot += p.RotVel * dt;
                return true;
            }
            if (!p.Rest)
            {
                var ph = p.Phys ?? FxPhysicsDef.Vacuum;
                Vector3 v = p.Vel;
                // move with the tick's velocity, then Halo's point physics step at each game tick crossed (see PhysicsTick)
                Vector3 np = p.Pos + v * dt;
                int ticks = TicksCrossed(p.Age, dt);
                for (int k = 0; k < ticks && k < 4; k++) PhysicsTick(ph, p.Radius, ref v, true);
                if (ph.Collides && _raycasts < RaycastBudget)
                {
                    Vector3 step = np - p.Pos;
                    float dist = step.magnitude;
                    RaycastHit hit;
                    if (dist > 1e-5f)
                    {
                        _raycasts++;
                        if (Physics.Raycast(p.Pos, step / dist, out hit, dist + 0.005f, HaloFx.CollisionMask, QueryTriggerInteraction.Ignore))
                        {
                            float speed = v.magnitude;
                            if (p.Kind == 0 && p.Def.CollisionEffect != null && speed > 1f && p.Bounces == 0 && CollisionAllowed(p.Def.CollisionEffect)) SubEffect(p.Def.CollisionEffect, hit.point, hit.normal, v);
                            if (p.Kind == 0 && p.Def.DiesOnStructure) return false;
                            if (p.ViewModel) return false;
                            Vector3 n = hit.normal;
                            Vector3 vn = n * Vector3.Dot(v, n);
                            Vector3 vt = v - vn;
                            v = vt * Mathf.Clamp01(1f - ph.SurfaceFriction) - vn * ph.Elasticity;
                            np = hit.point + n * 0.005f;
                            p.Bounces++;
                            if (v.magnitude < 0.3f || p.Bounces > 12)
                            {
                                v = Vector3.zero; p.Rest = true;
                                if (p.Kind == 0 && p.Def.DiesAtRest) return false;
                            }
                        }
                    }
                }
                p.Pos = np; p.Vel = v;
            }
            if (!(p.Rest && p.Kind == 0 && p.Def.AnimStopsAtRest)) p.Rot += p.RotVel * dt;
            return true;
        }

        bool StepSystemParticle(ref FxParticle p, float dt)
        {
            var s = p.Sys;
            if (s == null || s.Gen != p.SysGen) return false;
            var ty = s.Def.Types[p.Type];
            var states = ty.ParticleStates;
            int last = states.Length - 1;
            p.StateT += dt;
            while (p.StateT >= p.StateDur + p.TransDur)
            {
                if (p.PState >= last) return false;
                p.StateT -= p.StateDur + p.TransDur;
                p.PState++;
                if (p.PState >= last) return false;
                var ns = states[p.PState];
                p.StateDur = Rand(ns.DurMin, ns.DurMax); p.TransDur = Rand(ns.TransMin, ns.TransMax);
                if (ns.Sheet != null) p.Sheet = ns.Sheet;
            }
            var cur = states[p.PState];
            if (p.StateT > p.StateDur && p.PState < last && p.TransDur > 0f && (p.StateT - p.StateDur) / p.TransDur > 0.5f) p.Phys = states[p.PState + 1].Physics;
            else p.Phys = cur.Physics;
            return p.Age < p.Life;
        }

        readonly System.Collections.Generic.Dictionary<uint, float> _lastCollision = new System.Collections.Generic.Dictionary<uint, float>();
        /// <summary>ours: a particle tag's collision effect / sound (casings, metal chips, gravel) plays on a particle's FIRST contact only,
        /// and at most 10 times a second per tag - a burst's 30 sparks are one clatter, not 30 (Halo has no flag for it; its own sound
        /// budget drops the rest).</summary>
        bool CollisionAllowed(HaloTag t)
        {
            float now = Time.time, last;
            if (_lastCollision.TryGetValue(t.Id, out last) && now - last < 0.1f && now >= last) return false;
            _lastCollision[t.Id] = now;
            return true;
        }

        void SubEffect(HaloTag t, Vector3 pos, Vector3 normal, Vector3 vel)
        {
            if (t == null) return;
            if (t.Is("snd!")) { HaloAudio.Play(t, pos); return; }
            if (!t.Is("effe")) return;
            Vector3 inc = vel.sqrMagnitude > 1e-6f ? vel.normalized : -normal;
            _depth++;
            try { Begin(t, pos, Quaternion.LookRotation(normal.sqrMagnitude > 1e-6f ? normal : Vector3.up), null, normal, inc, false, false); }
            finally { _depth--; }
        }

        // ------------------------------------------------------------------ drawing

        void DrawParticles(Vector3 eye)
        {
            if (_p == null) return;
            for (int i = 0; i < _pHigh; i++)
            {
                if (!_p[i].Alive) continue;
                ref FxParticle p = ref _p[i];
                if (!PrepareDraw(ref p)) continue;
                if (p.CurBatch.Sorted) p.CurBatch.AddItem(i, (WorldPos(ref p) - eye).sqrMagnitude);
                else WriteParticle(ref p, p.CurBatch);
            }
            var all = Render.All;
            for (int b = 0; b < all.Count; b++)
            {
                var batch = all[b];
                if (batch.ItemCount == 0) continue;
                batch.SortItems();
                for (int k = 0; k < batch.ItemCount; k++) WriteParticle(ref _p[batch.Items[k]], batch);
            }
        }

        internal string DebugParticles(Vector3 from, int max)
        {
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, string>>();
            for (int i = 0; i < _pHigh; i++)
            {
                if (!_p[i].Alive) continue;
                Vector3 w = WorldPos(ref _p[i]);
                float d = (w - from).magnitude;
                string n = _p[i].Kind == 0 && _p[i].Def != null && _p[i].Def.Tag != null ? _p[i].Def.Tag.Name : "system";
                list.Add(new System.Collections.Generic.KeyValuePair<float, string>(d, n + " d " + d.ToString("0.00") + " y " + w.y.ToString("0.00") + " r " + _p[i].Radius.ToString("0.000") + (_p[i].Attached ? " att" : "") + " age " + _p[i].Age.ToString("0.00") + "/" + _p[i].Life.ToString("0.00")));
            }
            list.Sort((a, b) => b.Key.CompareTo(a.Key));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < list.Count && i < max; i++) sb.Append("\n    ").Append(list[i].Value);
            return sb.ToString();
        }

        internal string DebugParticlesAbove(float minY, int max)
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            for (int i = 0; i < _pHigh && n < max; i++)
            {
                if (!_p[i].Alive) continue;
                Vector3 w = WorldPos(ref _p[i]);
                if (w.y < minY) continue;
                n++;
                string name = _p[i].Kind == 0 && _p[i].Def != null && _p[i].Def.Tag != null ? _p[i].Def.Tag.Name : "system";
                sb.Append("\n    ").Append(name).Append(" at ").Append(w.ToString("F2")).Append(" v ").Append(_p[i].Vel.ToString("F2"))
                  .Append(" dir ").Append(_p[i].Dir.ToString("F2")).Append(_p[i].Attached ? " att" : "").Append(" age ").Append(_p[i].Age.ToString("0.00"));
            }
            return sb.ToString();
        }

        static Vector3 WorldPos(ref FxParticle p) { return p.Attached && p.Attach != null ? p.Attach.TransformPoint(p.Pos) : p.Pos; }

        /// <summary>
        /// Particle flag "animate once per frame" (bit 3): every flash particle (AR / pistol / plasma petals, shield and blood impact
        /// bursts) has it, and their sprite sheets are N one-sprite sequences (the petal shapes). The particle shows a new shape every
        /// frame instead of animating by its rate: a multi-frame sequence steps one frame per frame, a one-sprite sequence is swapped for
        /// another of the particle's initial (or looping) sequences - a flickering flash, not one frozen petal shape held for its life.
        /// Ours: Halo's frame = the game tick (1/30 s; a 90 Hz headset would strobe at 90 shapes a second); never the same shape twice.
        /// </summary>
        bool AnimateOncePerFrame(ref FxParticle p)
        {
            var d = p.Def;
            int tick = (int)(p.Age * 30f);
            var seq = p.Sheet.Sequence(p.Seq);
            if (seq == null) return false;
            if (tick == p.Tick && p.FrameIdx < seq.Frames.Length) return true;
            int steps = tick - p.Tick;
            p.Tick = tick;
            int n = seq.Frames.Length;
            if (n > 1) { p.FrameIdx = (p.Frame0 + tick) % n; return true; }
            if (steps <= 0) { p.FrameIdx = 0; return true; }
            int first = d.FirstSeq, count = d.InitialSeq;
            if (p.Phase == 1 || count <= 0) { first = d.FirstSeq + d.InitialSeq; count = d.LoopSeq; }
            if (count > 1)
            {
                int cur = p.Seq - first;
                int k = UnityEngine.Random.Range(0, count - 1);
                if (k >= cur && cur >= 0) k++;
                p.Seq = first + k;
            }
            p.FrameIdx = 0;
            return true;
        }

        /// <summary>Picks the particle's frame and batch for this frame.</summary>
        bool PrepareDraw(ref FxParticle p)
        {
            if (p.Sheet == null) return false;
            int blend;
            float anim = p.Anim;
            int seqIndex = p.Seq;
            if (p.Kind == 0)
            {
                blend = p.Def.Blend;
                if (p.Def.AnimateOncePerFrame && !(p.Rest && p.Def.AnimStopsAtRest)) { if (!AnimateOncePerFrame(ref p)) return false; }
                else if (!(p.Rest && p.Def.AnimStopsAtRest))
                {
                    var seq0 = p.Sheet.Sequence(seqIndex);
                    if (seq0 == null) return false;
                    int n = seq0.Frames.Length;
                    if (anim <= 0f || n <= 1) p.FrameIdx = p.Frame0 % Math.Max(1, n);
                    else
                    {
                        int k = p.Frame0 + (int)((p.Age - p.PhaseStart) * anim);
                        if (p.Phase == 0 && k >= n && p.Def.LoopSeq > 0) { ChooseSequence(ref p, false); seq0 = p.Sheet.Sequence(p.Seq); n = seq0.Frames.Length; k = 0; }
                        p.FrameIdx = p.Phase == 0 ? Math.Min(k, n - 1) : k % n;
                    }
                }
            }
            else
            {
                var s = p.Sys;
                if (s == null) return false;
                var ps = s.Def.Types[p.Type].ParticleStates[p.PState];
                blend = ps.Blend;
                seqIndex = ps.Sequence;
                var seq1 = p.Sheet.Sequence(seqIndex);
                if (seq1 == null) return false;
                int n = seq1.Frames.Length;
                p.FrameIdx = anim > 0f ? (p.Frame0 + (int)(p.Age * anim)) % n : p.Frame0 % n;
                p.Seq = seqIndex;
            }
            var seq = p.Sheet.Sequence(p.Seq);
            if (seq == null) return false;
            int fi = p.FrameIdx;
            if (fi < 0 || fi >= seq.Frames.Length) fi = 0;
            if (p.Backwards) fi = seq.Frames.Length - 1 - fi;
            p.CurFrame = seq.Frames[fi];
            if (blend == 0 && p.Sheet.NoAlpha != null && p.CurFrame.Image < p.Sheet.NoAlpha.Length && p.Sheet.NoAlpha[p.CurFrame.Image]) blend = 3;
            p.CurBatch = Render.Get(FxSheetGpu.Texture(p.Sheet, p.CurFrame.Image), blend, 0);
            return p.CurBatch.Mesh != null;
        }

        void WriteParticle(ref FxParticle p, FxBatch batch)
        {
            var fr = p.CurFrame;
            if (fr == null) return;
            Vector3 c = WorldPos(ref p);
            float halfW, halfH;
            Color col;
            int orientation = 0;
            if (p.Kind == 0)
            {
                var d = p.Def;
                float life01 = Mathf.Clamp01(p.Age / p.Life);
                float r = p.Radius * (d.RadAnim0 + (d.RadAnim1 - d.RadAnim0) * life01);
                // Halo's particle "radius" is the FULL extent of the particle's largest frame (frame pixels x the cached "sprite size" =
                // 1 / largest frame px), not its half: the casings prove it - sniper 0.037 wu = 11.3 cm (14.5x114 mm: 11.4 cm), warthog
                // gun 0.033 wu = 10.1 cm (12.7x99: 9.9 cm), AR 0.015 wu = 4.6 cm (7.62x51: 5.1 cm), pistol 0.01 wu = 3 cm, each sprite
                // cropped tight to the casing. Read as the half extent every particle (muzzle petals, smoke, sparks) was twice Halo's size.
                if (!fr.Whole)
                {
                    float ps = d.SpriteSize > 0f && d.SpriteSize < 0.5f ? d.SpriteSize : p.Sheet.PixelScale;
                    halfW = 0.5f * r * fr.PxW * ps; halfH = 0.5f * r * fr.PxH * ps;
                }
                else
                {
                    // a whole image (cached sprite size 1): the radius is its width, as a sprite's
                    halfW = 0.5f * r; halfH = fr.PxW > 0f ? 0.5f * r * fr.PxH / fr.PxW : 0.5f * r;
                }
                float a = p.Tint.a;
                if (d.FadeIn > 0f) a *= Mathf.Clamp01(p.Age / d.FadeIn);
                if (d.FadeOut > 0f) a *= Mathf.Clamp01((p.Life - p.Age) / d.FadeOut);
                col = new Color(p.Tint.r, p.Tint.g, p.Tint.b, a);
                if (!d.SelfIlluminated) { Color amb = HaloFx.Ambient; col.r *= amb.r; col.g *= amb.g; col.b *= amb.b; }
                orientation = d.Orientation;
            }
            else
            {
                var s = p.Sys;
                var ty = s.Def.Types[p.Type];
                var states = ty.ParticleStates;
                var a = states[p.PState];
                var b = a;
                float k = 0f;
                if (p.PState < states.Length - 1 && p.StateT > p.StateDur && p.TransDur > 0f) { b = states[p.PState + 1]; k = Mathf.Clamp01((p.StateT - p.StateDur) / p.TransDur); }
                float scale = Lerp(Lerp(a.ScaleMin, a.ScaleMax, p.Rnd), Lerp(b.ScaleMin, b.ScaleMax, p.Rnd), k) * Lerp(a.RadiusMul, b.RadiusMul, k);
                FxColor ca = FxColor.Lerp(a.C1, a.C2, p.Rnd), cb = FxColor.Lerp(b.C1, b.C2, p.Rnd);
                FxColor cc = FxColor.Lerp(ca, cb, k);
                Color m = s.Mul[p.Type];
                col = new Color(cc.R * m.r, cc.G * m.g, cc.B * m.b, cc.A * m.a);
                // a system particle's scale is world units per pixel of its frame (ours: the frame's pixel width is its diameter)
                float stateScale = s.Def.Types[p.Type].States[Math.Min(s.State[p.Type], ty.States.Length - 1)].Scale;
                if (stateScale <= 0f) stateScale = 1f;
                halfW = 0.5f * scale * stateScale * fr.PxW * HaloSpace.Unit;
                halfH = 0.5f * scale * stateScale * fr.PxH * HaloSpace.Unit;
            }
            if (col.a <= 0.002f || halfW <= 0f) return;
            Color32 c32 = ToColor32(col.r, col.g, col.b, col.a);

            float wu = fr.U1 - fr.U0, hv = fr.V1 - fr.V0;
            float fx = wu > 1e-6f ? Mathf.Clamp01(fr.RegU / wu) : 0.5f;
            float fy = hv > 1e-6f ? Mathf.Clamp01(fr.RegV / hv) : 0.5f;
            float W = halfW * 2f, H = halfH * 2f;
            float x0 = -fx * W, x1 = (1f - fx) * W, yT = fy * H, yB = -(1f - fy) * H;
            float u0 = fr.U0, u1 = fr.U1, v0 = fr.V0, v1 = fr.V1;
            if (p.MirU) { float t = u0; u0 = u1; u1 = t; }
            if (p.MirV) { float t = v0; v0 = v1; v1 = t; }
            float extent = Mathf.Max(Mathf.Max(-x0, x1), Mathf.Max(yT, -yB));

            if (orientation == 1)
            {
                Vector3 axis = p.Vel.sqrMagnitude > 1e-6f ? p.Vel : p.Dir;
                if (p.Attached && p.Attach != null) axis = p.Attach.TransformDirection(axis);
                var k1 = new Vector4(axis.x, axis.y, axis.z, 1f);
                batch.Quad(c, c, c, c,
                    new Vector4(u0, v0, x0, yT), new Vector4(u1, v0, x1, yT), new Vector4(u1, v1, x1, yB), new Vector4(u0, v1, x0, yB),
                    k1, c32, extent);
                return;
            }
            float cs = Mathf.Cos(p.Rot), sn = Mathf.Sin(p.Rot);
            if (orientation == 2)
            {
                Vector3 n = p.Vel.sqrMagnitude > 1e-6f ? p.Vel : p.Dir;
                if (p.Attached && p.Attach != null) n = p.Attach.TransformDirection(n);
                if (n.sqrMagnitude < 1e-8f) n = Vector3.up;
                n.Normalize();
                Vector3 ax = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 ay = Vector3.Cross(n, ax);
                Vector3 rx = ax * cs + ay * sn, ry = ay * cs - ax * sn;
                var k2 = new Vector4(0f, 0f, 0f, 2f);
                batch.Quad(c + rx * x0 + ry * yT, c + rx * x1 + ry * yT, c + rx * x1 + ry * yB, c + rx * x0 + ry * yB,
                    new Vector4(u0, v0, 0f, 0f), new Vector4(u1, v0, 0f, 0f), new Vector4(u1, v1, 0f, 0f), new Vector4(u0, v1, 0f, 0f),
                    k2, c32, 0f);
                return;
            }
            var k0 = new Vector4(0f, 0f, 0f, 0f);
            batch.Quad(c, c, c, c,
                new Vector4(u0, v0, x0 * cs - yT * sn, x0 * sn + yT * cs), new Vector4(u1, v0, x1 * cs - yT * sn, x1 * sn + yT * cs),
                new Vector4(u1, v1, x1 * cs - yB * sn, x1 * sn + yB * cs), new Vector4(u0, v1, x0 * cs - yB * sn, x0 * sn + yB * cs),
                k0, c32, extent);
        }
    }
}

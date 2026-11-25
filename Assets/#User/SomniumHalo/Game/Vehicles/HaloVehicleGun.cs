using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A vehicle's weapon (the unit's weapons block, Combat's HaloWeaponDef reader): per trigger the rate of fire ramping from its
    /// minimum to its maximum over the acceleration time (the warthog chain gun spins up 8 -> 15 rounds a second), the magazine
    /// (rocket warthog: 3 rockets then the chamber time; scorpion cannon / banshee fuel rod: one round, 4 s), the error cone
    /// (radians, growing with the spin), the firing effect at the "primary trigger" marker(s) (alternating: the ghost's two guns),
    /// and the projectile. The projectiles fly in <see cref="HaloVehicleShots"/> (V's fallback) until the Combat module exposes its own
    /// projectile code (REQUESTS.md). Vehicle weapons have unlimited ammunition in Halo multiplayer.
    /// </summary>
    public sealed class HaloVehicleGun
    {
        public readonly HaloVehicle Vehicle;
        public readonly HaloTag Tag;
        public readonly HaloWeaponDef Def;
        public bool Firing { get; private set; }
        public float Heat { get; private set; }
        public bool Overheated { get; private set; }
        /// <summary>The direction of the last shot (the lab checks the ghost's bolts go forward).</summary>
        public Vector3 LastShotDir { get; private set; }

        private readonly float[] _spin = new float[2], _next = new float[2];
        private readonly int[] _loaded = new int[2];
        private readonly bool[] _wasHeld = new bool[2];
        private int _muzzle;
        private string _seatLabel;
        private readonly bool[] _firstSaid = new bool[2];
        private static readonly System.Random Rng = new System.Random();
        private readonly List<Transform> _secondaryMuzzles = new List<Transform>();

        public HaloVehicleGun(HaloVehicle vehicle, HaloTag tag)
        {
            Vehicle = vehicle; Tag = tag;
            try { Def = new HaloWeaponDef(tag); }
            catch (Exception e) { HaloLog.Warn("Vehicles: weapon " + tag + " unreadable: " + e.Message); }
            for (int i = 0; i < 2; i++) _loaded[i] = LoadedMax(i);
            if (vehicle.Model != null)
            {
                foreach (var t in vehicle.Model.Root.GetComponentsInChildren<Transform>(true))
                    if (t.name.StartsWith("marker:secondary trigger", StringComparison.Ordinal)) _secondaryMuzzles.Add(t);
            }
        }

        private int LoadedMax(int trigger)
        {
            if (Def == null || trigger >= Def.Triggers.Count) return 0;
            int m = Def.Triggers[trigger].Magazine;
            return m >= 0 && m < Def.Magazines.Count ? Def.Magazines[m].RoundsLoadedMaximum : 0;
        }

        /// <summary>Every frame for the seat that mans the gun (local only): fire while held.</summary>
        /// <summary>Who fires (HaloHit.SourcePlayer): the local player's slot, or -2 for an AI gunner / driver (the Characters module's rule).</summary>
        public int Shooter = -1;

        public void Tick(bool primary, bool secondary, float dt, string seatLabel = null, int shooter = int.MinValue)
        {
            Firing = false;
            Shooter = shooter == int.MinValue ? HaloLocal.NetId : shooter;
            _seatLabel = seatLabel;
            if (Def == null) return;
            Step(0, primary, dt);
            if (Def.Triggers.Count > 1) Step(1, secondary, dt);
            if (Def.HeatLossRate > 0f) Heat = Mathf.Max(0f, Heat - Def.HeatLossRate * dt);
            if (Overheated && Heat <= Def.HeatRecoveryThreshold) Overheated = false;
        }

        private void Step(int i, bool held, float dt)
        {
            var t = Def.Triggers[i];
            if (held) _spin[i] = Mathf.MoveTowards(_spin[i], 1f, t.AccelerationTime > 0f ? dt / t.AccelerationTime : 1f);
            else _spin[i] = Mathf.MoveTowards(_spin[i], 0f, t.DecelerationTime > 0f ? dt / t.DecelerationTime : 1f);
            bool pressed = held && !_wasHeld[i];
            _wasHeld[i] = held;
            if (!held || Overheated) return;
            if (t.DoesNotRepeat && !pressed) return;
            if (Time.time < _next[i]) return;
            if (i == 0) Firing = true;
            float rate = Mathf.Lerp(t.RateMin, t.RateMax, _spin[i]);
            float interval = rate > 0f ? 1f / rate : 0.25f;
            int max = LoadedMax(i);
            if (max > 0)
            {
                _loaded[i]--;
                if (_loaded[i] <= 0)
                {
                    var m = Def.Magazines[t.Magazine];
                    interval = Mathf.Max(interval, m.ChamberTime > 0f ? m.ChamberTime : (m.ReloadTime > 0f ? m.ReloadTime : 1f));
                    _loaded[i] = max;
                }
            }
            _next[i] = Time.time + interval;
            Shoot(i, t);
            if (t.HeatPerRound > 0f)
            {
                Heat = Mathf.Min(1f, Heat + t.HeatPerRound);
                if (Def.OverheatedThreshold > 0f && Heat >= Def.OverheatedThreshold)
                {
                    Overheated = true;
                    if (Def.OverheatedEffect != null) HaloFx.Play(Def.OverheatedEffect, Vehicle.GunPivot.position, Vehicle.GunPivot.rotation, Vehicle.GunPivot);
                }
            }
        }

        private Transform Muzzle(int trigger)
        {
            var list = trigger == 1 && _secondaryMuzzles.Count > 0 ? _secondaryMuzzles : Vehicle.Muzzles;
            if (list.Count == 0) return Vehicle.GunPivot;
            _muzzle = (_muzzle + 1) % list.Count;
            return list[_muzzle];
        }

        private void Shoot(int i, HaloWeaponDef.Trigger t)
        {
            Transform muzzle = Muzzle(i);
            Vector3 pos = muzzle.position;
            Vector3 aim = Vehicle.AimDirection;
            float error = Mathf.Lerp(t.ErrorAngleMin, t.ErrorAngleMax, _spin[i]) * 0.5f;
            int count = Mathf.Max(1, t.ProjectilesPerShot);
            Vector3 dir = aim;
            for (int k = 0; k < count; k++)
            {
                dir = Spread(aim, error);
                if (t.Projectile != null) Launch(t.Projectile, pos, dir, Shooter, false);
            }
            var fx = t.Effects.Count > 0 ? t.Effects[0].Firing : null;
            if (fx != null) HaloFx.Play(fx, pos, Quaternion.LookRotation(dir), muzzle);
            LastShotDir = dir;
            HaloEvents.RaiseFired(Tag, pos, dir, Shooter);
            if (_seatLabel != null && !_firstSaid[i] && (_firstSaid[i] = true))
                HaloLog.Info("Vehicles: first shot from " + Vehicle.name + " seat '" + _seatLabel + "' - " + Tag.Name + " trigger " + i + " (" + (t.Projectile != null ? t.Projectile.Name : "no projectile") + "), along " + dir.ToString("F2") + ".");
        }

        /// <summary>Projectiles launched by vehicle guns this session (lab / log).</summary>
        public static int TotalShots;
        private static bool _saidPath;

        /// <summary>The game's projectile through the Combat module (HaloCombat.FireProjectile: flight, damage, effects, material
        /// responses; the firing vehicle ignored); V's own fallback when Combat has no catalog for the map.</summary>
        private void Launch(HaloTag projectile, Vector3 pos, Vector3 dir, int shooter, bool visualOnly)
        {
            TotalShots++;
            bool flown = false;
            try { flown = HaloCombat.FireProjectile(projectile, pos, Vector3.zero, shooter, Vehicle.Def.Tag, Vehicle.transform, dir, visualOnly) != null; }
            catch (Exception e) { HaloLog.Warn("Vehicles: HaloCombat.FireProjectile threw: " + e.Message); }
            if (!_saidPath) { _saidPath = true; HaloLog.Info("Vehicles: vehicle guns fire " + (flown ? "the Combat module's projectiles (HaloCombat.FireProjectile)" : "V's fallback projectiles (no Combat catalog)") + "."); }
            if (!flown) HaloVehicleShots.Fire(projectile, pos, dir, Vehicle.Velocity, shooter, Vehicle.Def.Tag, Vehicle, !visualOnly);
        }

        /// <summary>A direction within a cone of half-angle <paramref name="radians"/> round <paramref name="aim"/>.</summary>
        public static Vector3 Spread(Vector3 aim, float radians)
        {
            if (radians <= 0f) return aim.normalized;
            float r = (float)Math.Sqrt(Rng.NextDouble()) * radians, a = (float)(Rng.NextDouble() * Math.PI * 2.0);
            Quaternion basis = Quaternion.LookRotation(aim);
            Vector3 local = new Vector3(Mathf.Cos(a) * Mathf.Sin(r), Mathf.Sin(a) * Mathf.Sin(r), Mathf.Cos(r));
            return basis * local;
        }

        /// <summary>A remote player's shot with this vehicle's weapon (Net replays it): visuals only, no damage.</summary>
        public void Replay(Vector3 muzzle, Vector3 dir)
        {
            if (Def == null || Def.Triggers.Count == 0) return;
            var t = Def.Triggers[0];
            float best = float.MaxValue; int trigger = 0;
            // the trigger whose muzzle is nearest the reported one
            for (int i = 0; i < Def.Triggers.Count; i++)
            {
                var list = i == 1 && _secondaryMuzzles.Count > 0 ? _secondaryMuzzles : Vehicle.Muzzles;
                foreach (var m in list) { float d = (m.position - muzzle).sqrMagnitude; if (d < best) { best = d; trigger = i; } }
            }
            t = Def.Triggers[trigger];
            if (t.Projectile != null) Launch(t.Projectile, muzzle, dir, -1, true);
            var fx = t.Effects.Count > 0 ? t.Effects[0].Firing : null;
            if (fx != null) HaloFx.Play(fx, muzzle, Quaternion.LookRotation(dir));
            if (trigger == 0) { Firing = true; _replayUntil = Time.time + 0.3f; }
        }

        private float _replayUntil;
        public bool ReplayFiring => Time.time < _replayUntil;
    }

    /// <summary>
    /// V's own projectiles (fallback until Combat's projectile code is exposed): straight flight at the projectile's initial speed
    /// easing to its final speed over its range, air gravity scale x Halo gravity, maximum range, impact damage (jpt! direct amount)
    /// to whatever IHaloDamageable it hits, area damage of the detonation jpt! round the hit, the material response's effect. A
    /// streak (ours) or the projectile's own model shows it. Pooled, no allocation per shot once warmed up.
    /// </summary>
    public static class HaloVehicleShots
    {
        private sealed class Shot
        {
            public HaloProjectileDef Def;
            public HaloDamageDef Impact, Area;
            public Vector3 Pos, Vel;
            public float Travelled, Life;
            public int Shooter;
            public HaloTag Cause;
            public HaloVehicle From;
            public bool Damages, Alive;
            public LineRenderer Streak;
            public GameObject Model;
        }

        private static readonly List<Shot> Live = new List<Shot>();
        private static readonly Stack<Shot> Pool = new Stack<Shot>();
        private static readonly Dictionary<HaloTag, HaloProjectileDef> Defs = new Dictionary<HaloTag, HaloProjectileDef>();
        private static readonly Dictionary<HaloTag, HaloDamageDef> Damages = new Dictionary<HaloTag, HaloDamageDef>();
        private static readonly RaycastHit[] Hits = new RaycastHit[12];
        private static readonly Collider[] Near = new Collider[32];
        private static readonly System.Random Rng = new System.Random();
        private static Transform _root;
        private static Material _streakMat;

        public static Func<HaloTag, Vector3, Vector3, int, HaloTag, Transform, bool> CombatFire; // set when Combat offers its projectile code

        public static int Fired, Impacts, Damaged;
        public static int LiveCount => Live.Count;
        public static Vector3 LastImpact;

        public static void Fire(HaloTag projectile, Vector3 pos, Vector3 dir, Vector3 inherit, int shooter, HaloTag cause, HaloVehicle from, bool damages)
        {
            HaloProjectileDef def;
            if (!Defs.TryGetValue(projectile, out def))
            {
                try { def = new HaloProjectileDef(projectile); } catch (Exception e) { HaloLog.Warn("Vehicles: projectile " + projectile + ": " + e.Message); def = null; }
                Defs[projectile] = def;
            }
            if (def == null) return;
            var s = Pool.Count > 0 ? Pool.Pop() : new Shot();
            s.Def = def; s.Pos = pos; s.Vel = dir.normalized * Mathf.Max(1f, def.InitialSpeed * HaloSpace.Unit);
            s.Travelled = 0f; s.Life = 0f; s.Shooter = shooter; s.Cause = cause; s.From = from; s.Damages = damages; s.Alive = true;
            s.Impact = DamageOf(def.ImpactDamage); s.Area = AreaOf(def);
            Visual(s);
            Live.Add(s);
            Fired++;
        }

        private static readonly Dictionary<HaloTag, HaloDamageDef> Areas = new Dictionary<HaloTag, HaloDamageDef>();

        /// <summary>The area damage of a projectile: the first area damage part of its detonation effect (rocket, shell, fuel rod).</summary>
        private static HaloDamageDef AreaOf(HaloProjectileDef def)
        {
            if (def.DetonationEffect == null) return null;
            HaloDamageDef a;
            if (Areas.TryGetValue(def.DetonationEffect, out a)) return a;
            a = null;
            try
            {
                foreach (var p in HaloDamageDef.PartsOf(def.DetonationEffect)) { var d = DamageOf(p); if (d != null && d.IsArea) { a = d; break; } }
            }
            catch (Exception) { a = null; }
            Areas[def.DetonationEffect] = a;
            return a;
        }

        private static HaloDamageDef DamageOf(HaloTag t)
        {
            if (t == null) return null;
            HaloDamageDef d;
            if (!Damages.TryGetValue(t, out d)) { try { d = new HaloDamageDef(t); } catch (Exception) { d = null; } Damages[t] = d; }
            return d;
        }

        private static void Visual(Shot s)
        {
            if (_root == null)
            {
                var go = new GameObject("Halo Vehicle Shots");
                Transform parent = HaloSession.WorldRoot;
                if (parent != null) go.transform.SetParent(parent, false);
                _root = go.transform;
            }
            if (s.Def.Model != null)
            {
                if (s.Model != null) UnityEngine.Object.Destroy(s.Model);
                var holder = new GameObject("shot model");
                holder.transform.SetParent(_root, false);
                var b = HaloModelBuilder.Build(s.Def.Model, holder.transform, new HaloModelBuilder.Options { CastShadows = false });
                if (b != null) b.Root.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                s.Model = holder;
                if (s.Streak != null) s.Streak.enabled = false;
                return;
            }
            if (s.Streak == null)
            {
                var go = new GameObject("shot");
                go.transform.SetParent(_root, false);
                s.Streak = go.AddComponent<LineRenderer>();
                s.Streak.positionCount = 2; s.Streak.useWorldSpace = true;
                s.Streak.startWidth = 0.06f; s.Streak.endWidth = 0.02f;
                s.Streak.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (_streakMat == null)
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                    if (sh == null) sh = Shader.Find("Sprites/Default");
                    _streakMat = sh != null ? new Material(sh) : null;
                }
                s.Streak.sharedMaterial = _streakMat;
            }
            bool plasma = s.Def.Tag.Name.Contains("bolt") || s.Def.Tag.Name.Contains("turret");
            Color c = plasma ? new Color(0.45f, 0.9f, 1f) : new Color(1f, 0.85f, 0.4f); // ours
            s.Streak.startColor = c; s.Streak.endColor = c;
            s.Streak.enabled = true;
            s.Streak.SetPosition(0, s.Pos); s.Streak.SetPosition(1, s.Pos);
        }

        /// <summary>Steps every projectile (the vehicle director calls it each frame).</summary>
        public static void Tick(float dt)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                var s = Live[i];
                if (!Step(s, dt))
                {
                    if (s.Streak != null) s.Streak.enabled = false;
                    if (s.Model != null) { UnityEngine.Object.Destroy(s.Model); s.Model = null; }
                    s.Alive = false; s.From = null;
                    Live.RemoveAt(i);
                    Pool.Push(s);
                }
            }
        }

        private static bool Step(Shot s, float dt)
        {
            var d = s.Def;
            s.Life += dt;
            float range = d.MaximumRange > 0f ? d.MaximumRange * HaloSpace.Unit : 1500f;
            if (s.Travelled > range || s.Life > 12f) return false;
            float speed = d.SpeedAt(s.Travelled / HaloSpace.Unit, s.Life) * HaloSpace.Unit;
            if (speed > 0.1f) s.Vel = s.Vel.normalized * speed;
            s.Vel += Physics.gravity * d.AirGravityScale * dt;
            Vector3 step = s.Vel * dt;
            float len = step.magnitude;
            Vector3 from = s.Pos;
            RaycastHit hit;
            if (Cast(s, from, step / Mathf.Max(len, 1e-5f), len, out hit))
            {
                s.Pos = hit.point;
                Impact(s, hit);
                return false;
            }
            s.Pos += step; s.Travelled += len;
            if (s.Streak != null && s.Streak.enabled)
            {
                s.Streak.SetPosition(0, s.Pos);
                s.Streak.SetPosition(1, s.Pos - s.Vel.normalized * Mathf.Min(6f, s.Travelled));
            }
            if (s.Model != null) s.Model.transform.SetPositionAndRotation(s.Pos, Quaternion.LookRotation(s.Vel));
            return true;
        }

        private static bool Cast(Shot s, Vector3 from, Vector3 dir, float len, out RaycastHit best)
        {
            best = default(RaycastHit);
            int n = Physics.RaycastNonAlloc(from, dir, Hits, len, HaloLayers.ShotMask, QueryTriggerInteraction.Collide);
            float bd = float.MaxValue; bool found = false;
            Transform own = s.From != null ? s.From.transform : null;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null) continue;
                if (own != null && h.collider.transform.IsChildOf(own)) continue;
                if (h.collider.isTrigger && h.collider.gameObject.layer != HaloLayers.Hitbox) continue;
                if (s.Shooter >= 0 && s.Shooter == HaloLocal.NetId && OwnBody(h.collider)) continue;
                if (h.distance < bd) { bd = h.distance; best = h; found = true; }
            }
            return found;
        }

        /// <summary>The local player's own hit boxes / body (a rider shooting must not hit themselves).</summary>
        private static bool OwnBody(Collider c)
        {
            Transform root = HaloPlayer.Root;
            return root != null && c.transform.IsChildOf(root);
        }

        private static void Impact(Shot s, RaycastHit hit)
        {
            Impacts++; LastImpact = hit.point;
            var d = s.Def;
            int material = HaloWorld.SurfaceMaterial(hit);
            var resp = d.For(material);
            HaloTag fx = resp != null && resp.DefaultEffect != null ? resp.DefaultEffect : d.DetonationEffect;
            if (fx != null) HaloFx.Play(fx, hit.point, Quaternion.LookRotation(hit.normal));
            if (!s.Damages) return;
            var target = hit.collider.GetComponentInParent<IHaloDamageable>();
            if (target != null && s.Impact != null && !s.Impact.Harmless)
            {
                float amount = s.Impact.DirectAmount(Rng, s.Travelled / HaloSpace.Unit, d.AirDamageRangeMin, d.AirDamageRangeMax);
                var h = new HaloHit { Amount = amount, Effect = s.Impact.Tag, SourcePlayer = s.Shooter, Cause = s.Cause, Point = hit.point, Direction = s.Vel.normalized };
                Damaged++;
                HaloGame.Guard("vehicle shot", () => target.TakeHit(h));
            }
            if (s.Area != null) Area(s, hit.point);
        }

        private static void Area(Shot s, Vector3 at)
        {
            float radius = s.Area.RadiusMax * HaloSpace.Unit;
            if (radius <= 0f) return;
            int n = Physics.OverlapSphereNonAlloc(at, radius, Near, (1 << HaloLayers.Hitbox) | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Collide);
            var done = new HashSet<IHaloDamageable>();
            for (int i = 0; i < n; i++)
            {
                var t = Near[i] != null ? Near[i].GetComponentInParent<IHaloDamageable>() : null;
                if (t == null || !done.Add(t)) continue;
                float dist = Vector3.Distance(at, Near[i].ClosestPoint(at)) / HaloSpace.Unit;
                float amount = s.Area.AreaAmount(Rng, dist);
                if (amount <= 0f) continue;
                var h = new HaloHit { Amount = amount, Effect = s.Area.Tag, SourcePlayer = s.Shooter, Cause = s.Cause, Point = at, Direction = (Near[i].transform.position - at).normalized, Explosion = true };
                HaloGame.Guard("vehicle blast", () => t.TakeHit(h));
            }
            // the local player on foot near the blast (their own hit boxes may be the Combat module's, not reached above)
            Vector3 feet;
            if (HaloLocal.Alive && HaloPlayer.TryFeet(out feet))
            {
                float dl = Vector3.Distance(at, feet + Vector3.up * 1f) / HaloSpace.Unit;
                float amount = s.Area.AreaAmount(Rng, dl);
                bool reached = false;
                foreach (var t in done) if (t is Component c && HaloPlayer.Root != null && c.transform.IsChildOf(HaloPlayer.Root)) reached = true;
                if (amount > 0f && !reached)
                {
                    var h = new HaloHit { Amount = amount, Effect = s.Area.Tag, SourcePlayer = s.Shooter, Cause = s.Cause, Point = at, Direction = (feet - at).normalized, Explosion = true };
                    HaloGame.Guard("vehicle blast (local)", () => HaloCombat.TakeHit(h));
                }
            }
        }

        public static void Clear()
        {
            foreach (var s in Live) { if (s.Model != null) UnityEngine.Object.Destroy(s.Model); }
            Live.Clear(); Pool.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Every projectile in flight, flown from its proj tag: speed from initial to final over the maximum range, Halo gravity times the air
    /// gravity scale, guidance toward a target at the guided angular velocity (needles, the charged plasma bolt), the collision radius,
    /// material responses on impact (disappear with the impact effect, detonate, reflect with the response's friction, overpenetrate,
    /// attach: needles and plasma grenades stick), the timer (starting at launch, after the first bounce or at rest) and the arming time.
    /// Halo's "bullets" are projectiles too (fast ones). Grenades are thrown projectiles flown here as well.
    ///
    /// Shooter-authoritative: a flight with <see cref="Flight.Authoritative"/> (fired on this client) hands its hits to the target's
    /// IHaloDamageable (Net's hit boxes publish them to the victim); replays of other players' shots fly for the eyes only.
    /// Allocation-free once the pools are warm. Ticked by the combat driver every frame (main thread).
    /// </summary>
    public static class HaloProjectiles
    {
        public sealed class Flight
        {
            public HaloProjectileDef Def;
            public Vector3 Position, Velocity, Direction;
            public float Traveled;              // world units
            public float Age;                   // seconds
            public int Shooter = -1;            // net slot (-1 = the world / unknown)
            public bool Local;                  // fired by the local player (self-damage rules, hit markers)
            public bool Authoritative;          // decides hits (false = a replay for the eyes)
            public HaloTag Cause;               // weapon / vehicle / grenade tag for the kill feed
            public Transform IgnoreRoot;        // the firing vehicle / the thrower's own body
            public Transform Target;            // guided: what it homes on
            public bool Bounced, AtRest, Stuck;
            public Transform StuckTo; public Vector3 StuckLocal; public IHaloDamageable StuckVictim;
            public float DetonateAt = -1f;      // Age at which the timer ends (-1 = no timer running)
            public float Timer;                 // the chosen timer length
            public int GrenadeKind = -1;        // 0 frag, 1 plasma, -1 not a grenade
            public GameObject Visual;
            public LineRenderer Tracer;
            public HaloFx.Attachments Fx;       // the proj tag's attachments (contrail, light, particles, looping sound) by the Effects module
            public Transform Anchor;            // a pooled empty object the attachments follow when the projectile has no model
            public bool Alive;
            public Collider LastHit;            // (overpenetration: not the same collider twice)
            public int Ring = -1;               // its slot in the recent-flights ring (AI projectile noticing)
        }

        private static readonly List<Flight> Active = new List<Flight>(64);
        private static readonly Stack<Flight> Free = new Stack<Flight>();
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly Collider[] Overlap = new Collider[64];
        private static readonly HashSet<IHaloDamageable> Seen = new HashSet<IHaloDamageable>();
        private static readonly System.Random Rng = new System.Random();
        private static Transform _root;
        private static readonly Dictionary<HaloTag, GameObject> Prototypes = new Dictionary<HaloTag, GameObject>();
        private static readonly Dictionary<int, Material> TracerMats = new Dictionary<int, Material>();
        private static readonly Stack<LineRenderer> FreeTracers = new Stack<LineRenderer>();
        private static readonly Dictionary<HaloTag, Stack<GameObject>> FreeVisuals = new Dictionary<HaloTag, Stack<GameObject>>();
        private static bool _loggedFirst;

        /// <summary>Seconds a projectile without a range or timer lives (ours).</summary>
        public const float MaxLife = 12f;
        /// <summary>Needles on one target that make the super combine (Halo CE: 7; ours from the game, not a tag value).</summary>
        public static int SuperCombineCount = 7;

        public static int Count => Active.Count;

        /// <summary>Raised for every authoritative hit this client hands to a target (Combat's hit feedback).</summary>
        public static event Action<IHaloDamageable, HaloHit> HitDealt;

        private static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var go = new GameObject("Halo Projectiles");
                    Transform parent = HaloSession.WorldRoot != null ? HaloSession.WorldRoot : (HaloGame.Current != null ? HaloGame.Current.transform : null);
                    if (parent != null) go.transform.SetParent(parent, false);
                    _root = go.transform;
                }

                return _root;
            }
        }

        // ------------------------------------------------------------------ launching

        /// <summary>
        /// Launches a projectile tag from <paramref name="position"/> with <paramref name="velocity"/> (m/s; zero = along
        /// <paramref name="direction"/> at the tag's own initial speed).
        /// </summary>
        public static Flight Launch(HaloProjectileDef def, Vector3 position, Vector3 direction, Vector3 velocity, int shooter, HaloTag cause, bool authoritative, bool local, Transform ignoreRoot, Transform target)
        {
            if (def == null) return null;
            Flight f = Free.Count > 0 ? Free.Pop() : new Flight();
            f.Def = def;
            f.Position = position;
            Vector3 dir = direction.sqrMagnitude > 1e-8f ? direction.normalized : (velocity.sqrMagnitude > 1e-8f ? velocity.normalized : Vector3.forward);
            f.Direction = dir;
            f.Velocity = velocity.sqrMagnitude > 1e-8f ? velocity : dir * (def.InitialSpeed * HaloSpace.Unit);
            f.Traveled = 0f; f.Age = 0f;
            f.Shooter = shooter; f.Local = local; f.Authoritative = authoritative; f.Cause = cause;
            f.IgnoreRoot = ignoreRoot; f.Target = target;
            f.Bounced = f.AtRest = f.Stuck = false; f.StuckTo = null; f.StuckVictim = null; f.LastHit = null;
            f.Timer = def.TimerMax > def.TimerMin ? def.TimerMin + (float)Rng.NextDouble() * (def.TimerMax - def.TimerMin) : def.TimerMin;
            f.DetonateAt = def.DetonationTimerStarts == HaloProjectileDef.TimerStart.Immediately && f.Timer > 0f ? f.Timer : -1f;
            f.GrenadeKind = HaloCombat.Catalog != null ? HaloCombat.Catalog.GrenadeKind(def.Tag) : -1;
            f.Alive = true;
            MakeVisual(f);
            Active.Add(f);
            RingStart(f);
            if (!_loggedFirst && authoritative)
            {
                _loggedFirst = true;
                HaloLog.Info("Combat: first projectile " + def.Tag.Name + " at " + (def.InitialSpeed * HaloSpace.Unit).ToString("0") + " m/s (gravity x" + def.AirGravityScale.ToString("0.##") + ", range " + (def.MaximumRange * HaloSpace.Unit).ToString("0") + " m)");
            }

            return f;
        }

        // ------------------------------------------------------------------ flying

        public static void Tick(float dt)
        {
            if (dt <= 0f) return;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                Flight f = Active[i];
                if (f.Alive)
                {
                    try { Step(f, dt); }
                    catch (Exception e) { HaloLog.Warn("Combat: projectile " + f.Def.Tag.Name + " failed: " + e.Message); f.Alive = false; }
                }

                if (!f.Alive)
                {
                    Release(f);
                    Active.RemoveAt(i);
                }
            }
        }

        private static void Step(Flight f, float dt)
        {
            var d = f.Def;
            f.Age += dt;
            if (f.DetonateAt >= 0f && f.Age >= f.DetonateAt && f.Age >= d.ArmingTime) { Detonate(f, f.Position, -f.Velocity.normalized, -1, null); return; }
            if (f.Stuck)
            {
                if (f.StuckTo != null) f.Position = f.StuckTo.TransformPoint(f.StuckLocal);
                else if (f.StuckVictim != null) { f.Stuck = false; f.AtRest = false; }      // the thing it stuck to went away: it falls
                PlaceVisual(f);
                if (f.Age > MaxLife * 3f) f.Alive = false;
                return;
            }

            if (f.AtRest)
            {
                PlaceVisual(f);
                if (f.Age > MaxLife * 3f) f.Alive = false;
                return;
            }

            // speed along the flight (initial -> final), gravity, guidance
            float speedHalo = d.Thrown ? 0f : d.SpeedAt(f.Traveled, f.Age);
            Vector3 v = f.Velocity;
            if (!d.Thrown)
            {
                Vector3 dir = v.sqrMagnitude > 1e-8f ? v.normalized : f.Direction;
                if (f.Target != null && d.GuidedAngularVelocity > 0f)
                {
                    Vector3 to = f.Target.position - f.Position;
                    if (to.sqrMagnitude > 0.01f) dir = Vector3.RotateTowards(dir, to.normalized, d.GuidedAngularVelocity * dt, 0f);
                }

                // the tag's speed along the current direction, plus what gravity added across it
                float along = speedHalo * HaloSpace.Unit;
                Vector3 drop = v - dir * Vector3.Dot(v, dir);
                v = dir * along + drop;
            }

            v += Vector3.down * (HaloProjectileDef.GravityUnitsPerSecond2 * HaloSpace.Unit * d.AirGravityScale * dt);
            if (d.MinimumSpeed > 0f && !d.Thrown && v.magnitude < d.MinimumSpeed * HaloSpace.Unit) { Detonate(f, f.Position, -v.normalized, -1, null); return; }
            f.Velocity = v;

            Vector3 step = v * dt;
            float len = step.magnitude;
            if (len > 1e-6f)
            {
                RaycastHit hit;
                bool any = Cast(f, f.Position, step / len, len, out hit);
                // an AI / world shot on the local player's body (the local player has no hit box of its own)
                float dl; Vector3 pl; bool head;
                if (!f.Local && f.Authoritative && LocalBody(f.Position, step / len, len, out dl, out pl, out head) && (!any || dl < hit.distance))
                {
                    f.Traveled += dl / HaloSpace.Unit;
                    ImpactLocal(f, pl, step / len, head);
                    if (!f.Alive) return;
                }
                else if (any)
                {
                    f.Traveled += hit.distance / HaloSpace.Unit;
                    Impact(f, hit);
                    if (!f.Alive) return;
                }
                else
                {
                    f.Position += step;
                    f.Traveled += len / HaloSpace.Unit;
                }
            }

            RingUpdate(f);
            if (d.MaximumRange > 0f && f.Traveled >= d.MaximumRange && !d.Thrown)
            {
                if (d.DetonationEffect != null && d.TimerMax <= 0f) Detonate(f, f.Position, -f.Velocity.normalized, -1, null);
                else f.Alive = false;
                return;
            }

            if (f.Age > MaxLife && f.DetonateAt < 0f) { if (d.DetonationEffect != null) Detonate(f, f.Position, Vector3.up, -1, null); else f.Alive = false; return; }
            PlaceVisual(f);
        }


        /// <summary>The nearest hit along the segment that is not the shooter's own (ignore root, the last overpenetrated collider).</summary>
        private static bool Cast(Flight f, Vector3 from, Vector3 dir, float len, out RaycastHit best)
        {
            float r = f.Def.CollisionRadius * HaloSpace.Unit;
            int mask = HaloLayers.ShotMask;
            int n = r > 0.01f
                ? Physics.SphereCastNonAlloc(from, Mathf.Min(r, 0.3f), dir, Hits, len, mask, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(from, dir, Hits, len, mask, QueryTriggerInteraction.Ignore);
            best = default(RaycastHit);
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Hits[i];
                if (h.collider == null || h.collider == f.LastHit) continue;
                if (f.IgnoreRoot != null && h.collider.transform.IsChildOf(f.IgnoreRoot)) continue;
                if (HaloCombat.IsOwnCollider(h.collider) && f.Local) continue;
                // a replayed shot of another player (ReplayShot has no ignore root): not on the shooter's own hit boxes around the gun
                if (!f.Local && f.Shooter >= 0 && f.IgnoreRoot == null)
                {
                    var own = h.collider.GetComponent<IHaloHitInfo>();
                    if (own != null && own.HitSlot == f.Shooter) continue;
                }
                if (h.distance <= 0f && h.point == Vector3.zero) { h.point = from; h.normal = -dir; }      // (a sphere starting inside)
                if (h.distance < bd) { bd = h.distance; best = h; }
            }

            return bd < float.MaxValue;
        }

        // ------------------------------------------------------------------ the local player's body (AI / world shots)

        /// <summary>The local player as a target for shots that are not its own (it has no hit box of its own: a capsule from the feet to the head).</summary>
        private sealed class LocalPlayerTarget : IHaloDamageable, IHaloHitInfo
        {
            public bool Head;
            public void TakeHit(HaloHit hit) { HaloCombat.TakeHit(hit); }
            public int HitSlot => HaloLocal.NetId;
            public bool HitIsHead(Collider c) { return Head; }
            public Vector3 HitFacing => HaloLocal.Forward;
            public int HitMaterial(Vector3 point) { return HaloLocal.Shield > 0.01f ? HaloDamageDef.CyborgEnergyShield : HaloDamageDef.CyborgArmor; }
            public bool HitAlive => HaloLocal.Alive;
        }

        private static readonly LocalPlayerTarget LocalTarget = new LocalPlayerTarget();
        /// <summary>The local player's body radius for shots (metres at a 1.7 m avatar; ours) and the head's share of the height.</summary>
        public static float BodyRadius = 0.28f, HeadPart = 0.16f;

        /// <summary>The segment hits the local player's body capsule: distance along it, the point, whether at the head.</summary>
        private static bool LocalBody(Vector3 from, Vector3 dir, float len, out float dist, out Vector3 point, out bool head)
        {
            dist = 0f; point = Vector3.zero; head = false;
            var h = HaloHandling.Current;
            if (h == null || !h.Body.Valid || !HaloLocal.Alive) return false;
            float s = h.Body.Scale;
            Vector3 headAt = HaloCombat.LocalHead;
            Vector3 a = h.Body.Feet + Vector3.up * (0.25f * s), b = headAt + Vector3.up * (0.08f * s);
            float r = BodyRadius * s;
            // closest points of the shot segment and the body axis
            Vector3 d1 = dir * len, d2 = b - a, w = from - a;
            float aa = Vector3.Dot(d1, d1), bb = Vector3.Dot(d1, d2), cc = Vector3.Dot(d2, d2), dd = Vector3.Dot(d1, w), ee = Vector3.Dot(d2, w);
            float den = aa * cc - bb * bb, sc, tc;
            if (den < 1e-8f) { sc = 0f; tc = cc > 1e-8f ? ee / cc : 0f; }
            else { sc = (bb * ee - cc * dd) / den; tc = (aa * ee - bb * dd) / den; }
            sc = Mathf.Clamp01(sc); tc = Mathf.Clamp01(tc);
            Vector3 p1 = from + d1 * sc, p2 = a + d2 * tc;
            float gap = (p1 - p2).magnitude;
            if (gap > r) return false;
            // back off to the surface along the shot
            float back = Mathf.Sqrt(Mathf.Max(0f, r * r - gap * gap));
            dist = Mathf.Max(0f, sc * len - back);
            point = from + dir * dist;
            head = tc > 1f - HeadPart;
            return true;
        }

        private static void ImpactLocal(Flight f, Vector3 at, Vector3 dir, bool head)
        {
            var d = f.Def;
            LocalTarget.Head = head;
            int material = LocalTarget.HitMaterial(at);
            var mr = d.For(material);
            if (d.ImpactDamage != null) Damage(f, LocalTarget, LocalTarget, null, d.ImpactDamage, at, dir, false, f.Traveled);
            HaloProjectileDef.Response resp = mr != null ? mr.Default : HaloProjectiles.DefaultResponse;
            if (mr != null && mr.Potential == HaloProjectileDef.Response.Attach) resp = HaloProjectileDef.Response.Attach;
            HaloTag fx = mr != null ? mr.DefaultEffect : null;
            if (fx != null) HaloCombatFx.Impact(fx, at, -dir, dir);
            switch (resp)
            {
                case HaloProjectileDef.Response.Detonate:
                    f.Position = at - dir * 0.05f;
                    Detonate(f, f.Position, -dir, material, mr);
                    return;
                case HaloProjectileDef.Response.Attach:
                {
                    Transform anchor = HaloCombat.LocalAnchor;
                    f.Stuck = true;
                    f.StuckTo = anchor;
                    f.StuckLocal = anchor != null ? anchor.InverseTransformPoint(at) : Vector3.zero;
                    f.StuckVictim = LocalTarget;
                    f.Position = at;
                    f.Velocity = Vector3.zero;
                    if (f.DetonateAt < 0f) f.DetonateAt = f.Age + (f.Timer > 0f ? f.Timer : 0.75f);
                    HaloLog.Info("Combat: a " + d.Tag.Name + " stuck to the local player");
                    return;
                }
                case HaloProjectileDef.Response.Reflect:
                    f.Velocity = Vector3.Reflect(f.Velocity, -dir) * 0.3f;
                    f.Position = at - dir * 0.05f;
                    return;
                default:
                    f.Alive = false;
                    return;
            }
        }

        private const HaloProjectileDef.Response DefaultResponse = HaloProjectileDef.Response.Disappear;

        // ------------------------------------------------------------------ impacts

        private static void Impact(Flight f, RaycastHit hit)
        {
            var d = f.Def;
            IHaloDamageable victim = hit.collider.GetComponentInParent<IHaloDamageable>();
            IHaloHitInfo info = hit.collider.GetComponentInParent<IHaloHitInfo>();
            if (info != null && !info.HitAlive) victim = null;
            int material = MaterialAt(hit, victim, info);
            Vector3 dir = f.Velocity.sqrMagnitude > 1e-8f ? f.Velocity.normalized : f.Direction;
            float speed = f.Velocity.magnitude;
            var mr = d.For(material);
            HaloProjectileDef.Response resp = mr != null ? mr.Default : HaloProjectileDef.Response.Disappear;
            // the potential response: a plasma grenade (reflect normally) sticks to units and vehicles
            if (mr != null && mr.Potential == HaloProjectileDef.Response.Attach && resp != HaloProjectileDef.Response.Attach && victim != null) resp = HaloProjectileDef.Response.Attach;

            // the direct hit's damage (impact damage of the tag), on whatever was hit
            if (victim != null && f.Authoritative && d.ImpactDamage != null) Damage(f, victim, info, hit.collider, d.ImpactDamage, hit.point, dir, false, f.Traveled);
            // breakable world surfaces (glass): the impact damage on them (every client breaks its own panes, replays too); when this broke it the
            // projectile goes on through - Halo's glass response is "overpenetrate" for bullets, "detonate" / "reflect" for others as their tags say
            if (victim == null && d.ImpactDamage != null && SurfaceHit(hit, d.ImpactDamage, material, f.Traveled, f.Def) && resp == HaloProjectileDef.Response.Disappear) resp = HaloProjectileDef.Response.Overpenetrate;

            HaloTag fx = mr != null ? mr.DefaultEffect : null;
            switch (resp)
            {
                case HaloProjectileDef.Response.Detonate:
                    if (f.Age < d.ArmingTime && d.ArmingTime > 0f) goto case HaloProjectileDef.Response.Reflect;
                    f.Position = hit.point + hit.normal * 0.02f;
                    Detonate(f, f.Position, hit.normal, material, mr);
                    return;
                case HaloProjectileDef.Response.Reflect:
                {
                    if (fx != null && speed > 1f) HaloCombatFx.Impact(fx, hit.point, hit.normal, dir);
                    Vector3 n = hit.normal;
                    Vector3 vn = n * Vector3.Dot(f.Velocity, n), vt = f.Velocity - vn;
                    float perp = mr != null ? Mathf.Clamp01(mr.PerpendicularFriction) : 0.5f, par = mr != null ? Mathf.Clamp01(mr.ParallelFriction) : 0.3f;
                    if (perp <= 0f) perp = 0.45f;            // (ours: a bounce loses something even when the tag says nothing)
                    Vector3 nv = vt * (1f - par) - vn * (1f - perp);
                    f.Position = hit.point + n * 0.03f;
                    f.Velocity = nv;
                    f.LastHit = null;
                    if (!f.Bounced)
                    {
                        f.Bounced = true;
                        if (d.DetonationTimerStarts == HaloProjectileDef.TimerStart.AfterFirstBounce && f.DetonateAt < 0f) f.DetonateAt = f.Age + f.Timer;
                    }

                    if (!d.Thrown) { f.Direction = nv.normalized; }
                    if (nv.magnitude < 0.6f && n.y > 0.6f)
                    {
                        f.AtRest = true;
                        f.Velocity = Vector3.zero;
                        if (d.DetonationTimerStarts != HaloProjectileDef.TimerStart.Immediately && f.DetonateAt < 0f) f.DetonateAt = f.Age + f.Timer;
                    }

                    if (f.GrenadeKind >= 0 && speed > 1.5f && d.Tag != null) HaloCombat.GrenadeBounced(f, hit.point, speed);
                    PlaceVisual(f);
                    return;
                }
                case HaloProjectileDef.Response.Attach:
                {
                    if (fx != null) HaloCombatFx.Impact(fx, hit.point, hit.normal, dir);
                    f.Stuck = true;
                    f.StuckTo = hit.collider.transform;
                    f.StuckLocal = f.StuckTo.InverseTransformPoint(hit.point);
                    f.StuckVictim = victim;
                    f.Position = hit.point;
                    f.Velocity = Vector3.zero;
                    if (f.DetonateAt < 0f) f.DetonateAt = f.Age + (f.Timer > 0f ? f.Timer : 0.75f);
                    if (victim != null && d.HasSuperCombiningExplosion) CheckSuperCombine(f, victim);
                    PlaceVisual(f);
                    return;
                }
                case HaloProjectileDef.Response.Overpenetrate:
                    if (fx != null) HaloCombatFx.Impact(fx, hit.point, -dir, dir);
                    if (mr != null && mr.CannotBeOverpenetrated) { f.Alive = false; return; }
                    f.Position = hit.point + dir * 0.05f;
                    f.LastHit = hit.collider;
                    return;
                default:
                    if (fx != null) HaloCombatFx.Impact(fx, hit.point, hit.normal, dir);
                    if (d.DetonationEffect != null && d.TimerMax <= 0f && (d.ArmingTime <= 0f || f.Age >= d.ArmingTime)) { f.Position = hit.point + hit.normal * 0.02f; Detonate(f, f.Position, hit.normal, material, mr); return; }
                    f.Alive = false;
                    return;
            }
        }

        private static void CheckSuperCombine(Flight f, IHaloDamageable victim)
        {
            int n = 0;
            for (int i = 0; i < Active.Count; i++) if (Active[i].Alive && Active[i].Stuck && Active[i].StuckVictim == victim && Active[i].Def == f.Def) n++;
            if (n < SuperCombineCount) return;
            // every needle in the target goes off at once with the super detonation
            for (int i = 0; i < Active.Count; i++)
            {
                Flight o = Active[i];
                if (!o.Alive || !o.Stuck || o.StuckVictim != victim || o.Def != f.Def) continue;
                if (o != f) { o.Alive = false; continue; }
            }

            HaloLog.Info("Combat: needle super combine (" + n + " needles)");
            Detonate(f, f.Position, Vector3.up, -1, null, f.Def.SuperDetonation);
        }

        /// <summary>Halo material type of what a ray hit: a target's own answer, the world's surface material, else a guess.</summary>
        public static int MaterialAt(RaycastHit hit, IHaloDamageable victim, IHaloHitInfo info)
        {
            if (info != null) return info.HitMaterial(hit.point);
            if (victim != null)
            {
                int layer = hit.collider.gameObject.layer;
                return layer == HaloLayers.Hitbox ? HaloDamageDef.CyborgEnergyShield : 7;
            }

            int m = -1;
            try { m = HaloWorld.SurfaceMaterial(hit); } catch (Exception) { m = -1; }
            return m >= 0 ? m : 0;
        }

        // ------------------------------------------------------------------ damage

        private static void Damage(Flight f, IHaloDamageable victim, IHaloHitInfo info, Collider c, HaloTag effect, Vector3 point, Vector3 dir, bool explosion, float traveled)
        {
            var cat = HaloCombat.Catalog;
            var dd = cat != null ? cat.Damage(effect) : null;
            if (dd == null || dd.Harmless) return;
            float amount = explosion ? dd.AreaAmount(Rng, 0f) : dd.DirectAmount(Rng, traveled, f.Def.AirDamageRangeMin, f.Def.AirDamageRangeMax);
            amount = victim == (IHaloDamageable)LocalTarget ? amount * (f.Shooter == HaloCombat.AiShooter ? HaloCombat.ShooterModifier(f.IgnoreRoot) : 1f) : HaloCombat.ScaleForTarget(amount, f.Shooter, f.IgnoreRoot, victim);
            var hit = new HaloHit
            {
                Amount = amount, Effect = effect, SourcePlayer = f.Shooter, Cause = f.Cause, Point = point, Direction = dir,
                Explosion = explosion, Headshot = dd.CanCauseHeadshots && IsHead(c, info),
            };
            Deliver(victim, hit);
        }

        public static bool IsHead(Collider c, IHaloHitInfo info)
        {
            if (c == null) return false;
            if (info != null) return info.HitIsHead(c);
            return c.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Hands a hit to a target (never throws).</summary>
        public static void Deliver(IHaloDamageable victim, HaloHit hit)
        {
            try { victim.TakeHit(hit); }
            catch (Exception e) { HaloLog.Warn("Combat: a target failed to take a hit: " + e.Message); return; }
            var h = HitDealt;
            if (h != null) { try { h(victim, hit); } catch (Exception e) { HaloLog.Warn("Combat: HitDealt listener: " + e.Message); } }
        }

        // ------------------------------------------------------------------ detonation

        /// <summary>Goes off: the detonation effect (per material when the response names one), its blast damage, the attached damage.</summary>
        public static void Detonate(Flight f, Vector3 at, Vector3 normal, int material, HaloProjectileDef.MaterialResponse mr, HaloTag effectOverride = null)
        {
            if (!f.Alive) return;
            f.Alive = false;
            var d = f.Def;
            HaloTag effect = effectOverride ?? (mr != null && mr.DetonationEffect != null ? mr.DetonationEffect : d.DetonationEffect);
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            if (effect != null) HaloCombatFx.Impact(effect, at, normal, f.Velocity);
            if (!f.Authoritative) return;
            // the attached detonation damage hurts what it stuck in (a stuck plasma grenade, needles)
            if (f.Stuck && f.StuckVictim != null && d.AttachedDetonationDamage != null)
            {
                IHaloHitInfo info = f.StuckTo != null ? f.StuckTo.GetComponentInParent<IHaloHitInfo>() : null;
                Damage(f, f.StuckVictim, info, null, d.AttachedDetonationDamage, at, -normal, true, 0f);
            }

            Blast(effect ?? d.DetonationEffect, at, f.Shooter, f.Cause, f.Local, f.Stuck ? f.StuckVictim : null, f.IgnoreRoot);
        }

        /// <summary>
        /// The area damage of an effect's damage parts around a point: every damageable within the outer radius takes the falloff amount
        /// (walls block: a line from the centre to the target's nearest point must be clear of the world), the local player too
        /// (HaloCombat.TakeHit) when the blast is ours. <paramref name="skip"/> already took the attached damage.
        /// </summary>
        public static void Blast(HaloTag effect, Vector3 at, int shooter, HaloTag cause, bool local, IHaloDamageable skip)
        {
            Blast(effect, at, shooter, cause, local, skip, null);
        }

        private static void Blast(HaloTag effect, Vector3 at, int shooter, HaloTag cause, bool local, IHaloDamageable skip, Transform shooterRoot)
        {
            var cat = HaloCombat.Catalog;
            if (cat == null || effect == null) return;
            var parts = cat.Blast(effect);
            if (parts == null || parts.Count == 0) return;
            for (int p = 0; p < parts.Count; p++)
            {
                var pd = cat.Damage(parts[p]);
                if (pd != null && pd.IsArea) Area(parts[p], at, shooter, cause, local, local || shooter < 0, skip, shooterRoot, null);
            }
        }

        /// <summary>One damage effect's area damage at a point (script damage_new too: a damage effect without a radius reaches 0.5 m - ours).</summary>
        public static void BlastOne(HaloTag jpt, Vector3 at, int shooter, HaloTag cause, bool local, IHaloDamageable skip)
        {
            Area(jpt, at, shooter, cause, local, local || shooter < 0, skip, null, null);
        }

        // ------------------------------------------------------------------ the area damage core (projectile blasts, effect jpt! parts, HaloCombat.AreaDamage)

        /// <summary>An area damage asked for while another one is being applied (a blast kills a unit whose body destroyed effect blasts
        /// at once, a blast detonates a grenade): queued and run right after the current one (the overlap buffers are shared).</summary>
        private struct PendingArea
        {
            public HaloTag Jpt, Cause;
            public Vector3 At;
            public int Shooter;
            public bool Local, HurtsLocal;
            public IHaloDamageable Skip;
            public Transform ShooterRoot, Ignore;
        }

        private static readonly PendingArea[] Pending = new PendingArea[32];
        private static int _pendingHead, _pendingCount, _areaDepth;
        private static bool _loggedPendingFull, _loggedDetonates;
        private static readonly HashSet<uint> AreaLogged = new HashSet<uint>();
        private static int _impulsesLogged;
        /// <summary>The same jpt! at the same point in the same frame is applied once (a body destroyed effect played once by Effects, and
        /// its damage asked for again by the dying unit's code would double): the last few applications.</summary>
        private static readonly HaloTag[] RecentJpt = new HaloTag[8];
        private static readonly Vector3[] RecentAt = new Vector3[8];
        private static readonly int[] RecentFrame = new int[8];
        private static int _recentNext;
        private static bool _loggedDuplicate;
        /// <summary>Knockback: the least upward share of the push direction (a blast on the floor lifts what it throws) and the fastest push (m/s). Ours.</summary>
        public static float ImpulseMinLift = 0.35f, ImpulseMaxSpeed = 20f;

        /// <summary>
        /// The area damage of one jpt! at a point: every damageable within its outer radius (walls block) takes the falloff amount as an
        /// area hit (IsArea, ExplosionCenter, InstantAcceleration); units are pushed away by the jpt!'s instantaneous acceleration x their
        /// acceleration scale x the falloff, the dead too; "only hurts one infection form" stops after the first infection form,
        /// "infection form pop" kills infection forms outright, "detonates explosives" sets off grenades within the radius. The local player
        /// takes it when <paramref name="hurtsLocal"/>. <paramref name="shooterRoot"/> = the unit / vehicle behind it (AI damage scale),
        /// <paramref name="ignore"/> = a root not to hurt. Re-entrant calls are queued. No allocations (NonAlloc buffers).
        /// </summary>
        internal static void Area(HaloTag jpt, Vector3 at, int shooter, HaloTag cause, bool local, bool hurtsLocal, IHaloDamageable skip, Transform shooterRoot, Transform ignore)
        {
            if (jpt == null) return;
            if (_areaDepth > 0)
            {
                if (_pendingCount >= Pending.Length)
                {
                    if (!_loggedPendingFull) { _loggedPendingFull = true; HaloLog.Warn("Combat: area damage queue full (" + Pending.Length + "): " + jpt.Name + " dropped"); }
                    return;
                }

                int slot = (_pendingHead + _pendingCount) % Pending.Length;
                Pending[slot] = new PendingArea { Jpt = jpt, Cause = cause, At = at, Shooter = shooter, Local = local, HurtsLocal = hurtsLocal, Skip = skip, ShooterRoot = shooterRoot, Ignore = ignore };
                _pendingCount++;
                return;
            }

            _areaDepth++;
            try
            {
                AreaNow(jpt, at, shooter, cause, local, hurtsLocal, skip, shooterRoot, ignore);
                int guard = 0;
                while (_pendingCount > 0 && guard++ < 256)
                {
                    PendingArea p = Pending[_pendingHead];
                    Pending[_pendingHead] = default(PendingArea);
                    _pendingHead = (_pendingHead + 1) % Pending.Length;
                    _pendingCount--;
                    AreaNow(p.Jpt, p.At, p.Shooter, p.Cause, p.Local, p.HurtsLocal, p.Skip, p.ShooterRoot, p.Ignore);
                }
            }
            finally
            {
                _areaDepth--;
                if (_areaDepth == 0) { _pendingCount = 0; _pendingHead = 0; }
            }

            // every queued burst has run: now the units destroyed meanwhile are raised (the AI spawns their infection forms out of the bursts)
            if (_areaDepth == 0 && DeferredDestroyed.Count > 0) RaiseDeferredDestroyed();
        }

        private static readonly List<HaloUnit> DeferredDestroyed = new List<HaloUnit>(8);
        private static bool _loggedDeferred;

        /// <summary>A unit whose body is destroyed while area damage is being applied (a blast kills a carrier outright): its body destroyed
        /// effect's own area damage is queued behind the current one, so its "destroyed" (the AI spawns its infection forms there) waits until
        /// the queue has run - Halo's forms are not in their own carrier's burst. False = no area running: raise it now.</summary>
        internal static bool DeferDestroyed(HaloUnit u)
        {
            if (_areaDepth <= 0 || u == null) return false;
            if (!DeferredDestroyed.Contains(u)) DeferredDestroyed.Add(u);
            if (!_loggedDeferred) { _loggedDeferred = true; HaloLog.Info("Combat: " + (u.Def != null ? u.Def.Name : u.name) + " destroyed inside an area damage: its destroyed event (spawns) waits for the queued burst"); }
            return true;
        }

        private static void RaiseDeferredDestroyed()
        {
            int guard = 0;
            while (DeferredDestroyed.Count > 0 && guard++ < 64)
            {
                HaloUnit u = DeferredDestroyed[0];
                DeferredDestroyed.RemoveAt(0);
                if (!ReferenceEquals(u, null)) HaloBipeds.RaiseDestroyed(u);
            }
        }

        private static bool Duplicate(HaloTag jpt, Vector3 at)
        {
            int frame = Time.frameCount;
            for (int i = 0; i < RecentJpt.Length; i++)
                if (RecentJpt[i] == jpt && RecentFrame[i] == frame && (RecentAt[i] - at).sqrMagnitude < 0.0025f)
                {
                    if (!_loggedDuplicate) { _loggedDuplicate = true; HaloLog.Info("Combat: area damage " + jpt.Name + " asked twice at one point in one frame - applied once"); }
                    return true;
                }

            RecentJpt[_recentNext] = jpt; RecentAt[_recentNext] = at; RecentFrame[_recentNext] = frame;
            _recentNext = (_recentNext + 1) % RecentJpt.Length;
            return false;
        }

        /// <summary>An infection form: the unit flags "impact melee dies on shields" / "impact melee attaches to unit" (only flood_infection
        /// carries them); the tag name as a fallback while the flags are not parsed (ours).</summary>
        public static bool IsInfectionForm(HaloUnit u)
        {
            if (u == null || u.Def == null) return false;
            if (u.Def.ImpactMeleeDiesOnShields || u.Def.ImpactMeleeAttachesToUnit) return true;
            return u.Def.Unit != null && u.Def.Unit.Name.IndexOf("flood_infection", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AreaNow(HaloTag jpt, Vector3 at, int shooter, HaloTag cause, bool local, bool hurtsLocal, IHaloDamageable skip, Transform shooterRoot, Transform ignore)
        {
            var cat = HaloCombat.Catalog;
            var dd = cat != null ? cat.Damage(jpt) : null;
            if (dd == null || dd.Harmless) return;
            if (Duplicate(jpt, at)) return;
            float radius = dd.RadiusMax > 0f ? dd.RadiusMax : 0.5f / HaloSpace.Unit;
            float rMax = radius * HaloSpace.Unit;
            float accel = dd.AccelerationMagnitude;
            HaloUnit shooterUnit = shooterRoot != null ? shooterRoot.GetComponentInParent<HaloUnit>() : null;
            if (dd.RadiusMax > 0f) BlastSurfaces(dd, jpt, at, rMax);
            if (dd.DetonatesExplosives) DetonateExplosivesNear(at, rMax, jpt);
            Seen.Clear();
            int hits = 0;
            bool infectionHit = false;
            int n = Physics.OverlapSphereNonAlloc(at, rMax, Overlap, (1 << HaloLayers.Hitbox) | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider c = Overlap[i];
                if (c == null) continue;
                if (ignore != null && c.transform.IsChildOf(ignore)) continue;
                IHaloDamageable v = c.GetComponentInParent<IHaloDamageable>();
                if (v == null || v == skip) continue;
                IHaloHitInfo info = c.GetComponentInParent<IHaloHitInfo>();
                if (info != null && !info.HitAlive) continue;      // (bodies: pushed below)
                if (!Seen.Add(v)) continue;
                HaloUnit u = v as HaloUnit;
                if (u != null && dd.DoesNotHurtOwner && u == shooterUnit) continue;
                bool infection = u != null && IsInfectionForm(u);
                if (infection && dd.OnlyHurtsOneInfectionForm && infectionHit) continue;
                Vector3 near = c.ClosestPoint(at);
                if (!Clear(at, near)) continue;
                float dist = (near - at).magnitude / HaloSpace.Unit;
                float amount = dd.RadiusMax > 0f ? dd.AreaAmount(Rng, dist) : dd.DirectAmount(Rng, 0f, 0f, 0f);
                amount = HaloCombat.ScaleForTarget(amount, shooter, shooterRoot, v);
                bool pop = infection && dd.InfectionFormPop && dist <= radius;
                if (amount <= 0f && !pop) continue;
                Vector3 dir = near - at;
                dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
                Deliver(v, new HaloHit
                {
                    Amount = amount, Effect = jpt, SourcePlayer = shooter, Cause = cause, Point = near, Direction = dir, Explosion = true,
                    IsArea = true, ExplosionCenter = at, InstantAcceleration = accel, Instant = pop,
                });
                hits++;
                if (infection) infectionHit = true;
                if (u != null && accel > 0f) Push(u, at, dd, dist);
            }

            // bodies within the radius fly too (Halo's corpses tumble away from a grenade)
            if (accel > 0f)
            {
                var all = HaloBipeds.All;
                for (int i = 0; i < all.Count; i++)
                {
                    HaloUnit u = all[i];
                    if (u == null || u.Alive || Seen.Contains(u)) continue;
                    if (ignore != null && u.transform.IsChildOf(ignore)) continue;
                    Vector3 cpos = u.Center;
                    float d = (cpos - at).magnitude;
                    if (d > rMax || !Clear(at, cpos)) continue;
                    Push(u, at, dd, d / HaloSpace.Unit);
                }
            }

            // the local player (no hit box of our own): our own blasts and the world's / the AI's
            if (hurtsLocal)
            {
                Vector3 me = HaloCombat.LocalChest;
                if (HaloLocal.Alive && me != Vector3.zero && !(dd.DoesNotHurtOwner && local) && (ignore == null || HaloPlayer.Root == null || !HaloPlayer.Root.IsChildOf(ignore)))
                {
                    float dist = Mathf.Max(0f, (me - at).magnitude - 0.25f) / HaloSpace.Unit;
                    if (dist <= radius && Clear(at, me))
                    {
                        float amount = dd.RadiusMax > 0f ? dd.AreaAmount(Rng, dist) : dd.DirectAmount(Rng, 0f, 0f, 0f);
                        if (shooter == HaloCombat.AiShooter) amount *= HaloCombat.ShooterModifier(shooterRoot);
                        if (amount > 0f)
                        {
                            Vector3 dir = me - at;
                            dir = dir.sqrMagnitude > 1e-8f ? dir.normalized : Vector3.up;
                            HaloCombat.TakeHit(new HaloHit { Amount = amount, Effect = jpt, SourcePlayer = shooter, Cause = cause, Point = me, Direction = dir, Explosion = true, IsArea = true, ExplosionCenter = at, InstantAcceleration = accel });
                            hits++;
                        }
                    }
                }
            }

            if (AreaLogged.Add(jpt.Id)) HaloLog.Info("Combat: area damage " + jpt.Name + " hit " + hits + " units (radius " + dd.RadiusMin.ToString("0.##") + ".." + dd.RadiusMax.ToString("0.##") + " wu, damage " + dd.UpperMin.ToString("0.#") + ".." + dd.UpperMax.ToString("0.#") + ", acceleration " + accel.ToString("0.##") + (dd.InfectionFormPop ? ", infection form pop" : "") + (dd.OnlyHurtsOneInfectionForm ? ", only one infection form" : "") + (dd.DetonatesExplosives ? ", detonates explosives" : "") + ")");
        }

        /// <summary>
        /// Knockback: away from the blast (lifted by at least <see cref="ImpulseMinLift"/> - ours) at the jpt!'s instantaneous acceleration
        /// (read as world units / s) x the unit's acceleration scale (object @32) x the area falloff, capped at <see cref="ImpulseMaxSpeed"/> (ours).
        /// </summary>
        private static void Push(HaloUnit u, Vector3 at, HaloDamageDef dd, float distWu)
        {
            if (u == null || u.Def == null || u.Seat != null) return;
            float fall = dd.AreaFalloff(distWu);
            if (fall <= 0f) return;
            float speed = dd.AccelerationMagnitude * HaloSpace.Unit * Mathf.Max(0f, u.Def.AccelerationScale) * fall;
            if (speed < 0.05f) return;
            if (speed > ImpulseMaxSpeed) speed = ImpulseMaxSpeed;
            Vector3 dir = u.Center - at;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.up;
            dir.Normalize();
            if (dir.y < ImpulseMinLift) { dir.y = ImpulseMinLift; dir.Normalize(); }
            try { u.AddImpulse(dir * speed); }
            catch (Exception e) { HaloLog.Warn("Combat: impulse on " + u.Def.Name + ": " + e.Message); return; }
            if (_impulsesLogged < 12)
            {
                _impulsesLogged++;
                HaloLog.Info("Combat: impulse " + u.Def.Name + (u.Alive ? "" : " (dead)") + " " + speed.ToString("0.0") + " m/s from " + dd.Tag.Name + " at " + (distWu * HaloSpace.Unit).ToString("0.0") + " m (falloff " + fall.ToString("0.00") + ", acceleration scale " + u.Def.AccelerationScale.ToString("0.##") + ")");
            }
        }

        /// <summary>"detonates explosives": every grenade (live, resting or stuck) within the radius goes off now.</summary>
        private static void DetonateExplosivesNear(Vector3 at, float radius, HaloTag jpt)
        {
            float r2 = radius * radius;
            int n = 0;
            for (int i = 0; i < Active.Count; i++)
            {
                Flight o = Active[i];
                if (!o.Alive || o.GrenadeKind < 0 || (o.Position - at).sqrMagnitude > r2) continue;
                Detonate(o, o.Position, Vector3.up, -1, null);
                n++;
            }

            if (n > 0 && !_loggedDetonates) { _loggedDetonates = true; HaloLog.Info("Combat: " + jpt.Name + " detonated " + n + " grenade(s) (detonates explosives)"); }
        }

        // ------------------------------------------------------------------ breakable world surfaces (the World module's glass)

        /// <summary>A damage effect's amount on a breakable world surface (HaloWorld.DamageSurface; the material modifier of the surface applies). True when it broke.</summary>
        public static bool SurfaceHit(RaycastHit hit, HaloTag jpt, int material, float traveled, HaloProjectileDef pd)
        {
            try
            {
                if (hit.collider == null || !HaloWorld.IsBreakable(hit)) return false;
                var cat = HaloCombat.Catalog;
                var dd = cat != null ? cat.Damage(jpt) : null;
                if (dd == null) return false;
                float amount = pd != null ? dd.DirectAmount(Rng, traveled, pd.AirDamageRangeMin, pd.AirDamageRangeMax) : dd.DirectAmount(Rng, 0f, 0f, 0f);
                amount *= dd.Modifier(material);
                bool broke = HaloWorld.DamageSurface(hit, amount);
                if (broke) HaloLog.Info("Combat: " + jpt.Name + " broke a breakable surface (" + amount.ToString("0.#") + ")");
                return broke;
            }
            catch (Exception e) { HaloLog.Warn("Combat: breakable surface: " + e.Message); return false; }
        }

        private static readonly Collider[] SurfaceOverlap = new Collider[32];

        /// <summary>An area effect on the breakable surfaces within its radius (each pane once, at its nearest point, walls in between block).</summary>
        private static void BlastSurfaces(HaloDamageDef dd, HaloTag jpt, Vector3 at, float radius)
        {
            int n = Physics.OverlapSphereNonAlloc(at, radius, SurfaceOverlap, (1 << HaloLayers.World) | (1 << HaloLayers.Environment), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = SurfaceOverlap[i];
                if (c == null || !c.enabled) continue;
                Vector3 near = c.ClosestPoint(at);
                Vector3 d = near - at;
                float len = d.magnitude;
                RaycastHit hit;
                if (len < 0.01f) { d = Vector3.down; len = 0.01f; }
                if (!c.Raycast(new Ray(at - d.normalized * 0.05f, d.normalized), out hit, len + 0.2f)) continue;
                if (!HaloWorld.IsBreakable(hit)) continue;
                float amount = dd.AreaAmount(Rng, len / HaloSpace.Unit) * dd.Modifier(MaterialAt(hit, null, null));
                if (amount <= 0f) continue;
                try { if (HaloWorld.DamageSurface(hit, amount)) HaloLog.Info("Combat: a blast of " + jpt.Name + " broke a breakable surface"); }
                catch (Exception e) { HaloLog.Warn("Combat: breakable surface: " + e.Message); }
            }
        }

        private static bool Clear(Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.05f) return true;
            return !Physics.Raycast(a, d / len, len - 0.05f, (1 << HaloLayers.World) | (1 << HaloLayers.Environment), QueryTriggerInteraction.Ignore);
        }

        // ------------------------------------------------------------------ visuals

        /// <summary>Draw plain tracers for projectiles that have neither a model nor attachments (pistol bullets, shotgun pellets: Halo draws nothing). Off = faithful.</summary>
        public static bool PlainTracers = false;
        private static readonly Stack<Transform> FreeAnchors = new Stack<Transform>();

        private static void MakeVisual(Flight f)
        {
            f.Visual = null; f.Tracer = null; f.Fx = null; f.Anchor = null;
            var d = f.Def;
            if (d.Model != null)
            {
                f.Visual = TakeVisual(d);
                if (f.Visual != null) f.Visual.SetActive(true);
            }

            if (f.Visual == null)
            {
                f.Anchor = FreeAnchors.Count > 0 ? FreeAnchors.Pop() : null;
                if (f.Anchor == null) { f.Anchor = new GameObject("projectile").transform; f.Anchor.SetParent(Root, false); }
                f.Anchor.gameObject.SetActive(true);
            }

            PlaceVisual(f);
            if (d.Attachments.Count > 0)
            {
                try { f.Fx = HaloFx.Attach(d.Tag, f.Visual != null ? f.Visual.transform : f.Anchor, false, true); }
                catch (Exception e) { HaloLog.Warn("Combat: attachments of " + d.Tag + ": " + e.Message); }
            }
            else if (f.Visual == null && PlainTracers) f.Tracer = TakeTracer(TracerKind(d));
            if (f.Tracer != null) PlaceVisual(f);
        }

        private static void PlaceVisual(Flight f)
        {
            Vector3 dir = f.Velocity.sqrMagnitude > 0.01f ? f.Velocity.normalized : f.Direction;
            if (f.Visual != null)
            {
                Quaternion rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir) * Quaternion.Euler(0f, -90f, 0f) : Quaternion.identity;     // (the model's barrel axis is +X)
                if (f.AtRest || f.Stuck) rot = f.Visual.transform.rotation;
                f.Visual.transform.SetPositionAndRotation(f.Position, rot);
            }

            if (f.Anchor != null)
            {
                // no model: HaloFx takes a non-marker root's +Z as its forward (the effects / contrails of the attachments fly along it)
                Quaternion rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir) : Quaternion.identity;
                f.Anchor.SetPositionAndRotation(f.Position, rot);
            }

            if (f.Tracer != null)
            {
                float speed = f.Velocity.magnitude;
                float len = Mathf.Clamp(speed * 0.025f, 0.3f, 4f);
                f.Tracer.SetPosition(0, f.Position - dir * len);
                f.Tracer.SetPosition(1, f.Position);
            }
        }

        private static GameObject TakeVisual(HaloProjectileDef d)
        {
            Stack<GameObject> s;
            if (FreeVisuals.TryGetValue(d.Tag, out s) && s.Count > 0) return s.Pop();
            GameObject proto;
            if (!Prototypes.TryGetValue(d.Tag, out proto))
            {
                proto = null;
                try
                {
                    var holder = new GameObject("proto " + d.Tag.Name);
                    holder.transform.SetParent(Root, false);
                    var b = HaloModelBuilder.Build(d.Model, holder.transform, new HaloModelBuilder.Options { CastShadows = false, Lod = 4 });
                    if (b != null) proto = holder; else UnityEngine.Object.Destroy(holder);
                    if (proto != null) proto.SetActive(false);
                }
                catch (Exception e) { HaloLog.Warn("Combat: projectile model " + d.Model + " failed: " + e.Message); }
                Prototypes[d.Tag] = proto;
            }

            if (proto == null) return null;
            var go = UnityEngine.Object.Instantiate(proto, Root);
            go.name = System.IO.Path.GetFileName(d.Tag.Name);
            return go;
        }

        /// <summary>Tracer colours by the projectile family (ours: Halo draws contrails / lights we do not build yet).</summary>
        private static int TracerKind(HaloProjectileDef d)
        {
            string n = d.Tag.Name;
            if (n.IndexOf("plasma pistol", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("plasma_cannon", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("fuel rod", StringComparison.OrdinalIgnoreCase) >= 0) return 2;
            if (n.IndexOf("plasma", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("bolt", StringComparison.OrdinalIgnoreCase) >= 0) return 1;
            if (n.IndexOf("needle", StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (n.IndexOf("flame", StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            return 0;
        }

        private static LineRenderer TakeTracer(int kind)
        {
            LineRenderer lr = FreeTracers.Count > 0 ? FreeTracers.Pop() : null;
            if (lr == null)
            {
                var go = new GameObject("tracer");
                go.transform.SetParent(Root, false);
                lr = go.AddComponent<LineRenderer>();
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.alignment = LineAlignment.View;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
            }

            Material m;
            if (!TracerMats.TryGetValue(kind, out m) || m == null)
            {
                m = HaloMaterials.NewUnlit(HaloMaterials.Kind.Additive);
                Color c = kind == 1 ? new Color(0.35f, 0.6f, 1f, 0.9f) : kind == 2 ? new Color(0.4f, 1f, 0.35f, 0.9f) : kind == 3 ? new Color(1f, 0.35f, 0.85f, 0.9f) : kind == 4 ? new Color(1f, 0.55f, 0.15f, 0.8f) : new Color(1f, 0.85f, 0.45f, 0.55f);
                HaloMaterials.SetColor(m, c);
                TracerMats[kind] = m;
            }

            lr.sharedMaterial = m;
            float w = kind == 0 ? 0.012f : kind == 4 ? 0.25f : 0.06f;
            lr.startWidth = w; lr.endWidth = w * (kind == 0 ? 0.6f : 1f);
            lr.gameObject.SetActive(true);
            return lr;
        }

        private static void Release(Flight f)
        {
            if (f.Fx != null) { try { HaloFx.Detach(f.Fx); } catch (Exception) { } f.Fx = null; }
            if (f.Anchor != null) { f.Anchor.gameObject.SetActive(false); FreeAnchors.Push(f.Anchor); f.Anchor = null; }
            if (f.Tracer != null) { f.Tracer.gameObject.SetActive(false); FreeTracers.Push(f.Tracer); f.Tracer = null; }
            if (f.Visual != null)
            {
                f.Visual.SetActive(false);
                Stack<GameObject> s;
                if (!FreeVisuals.TryGetValue(f.Def.Tag, out s)) { s = new Stack<GameObject>(); FreeVisuals[f.Def.Tag] = s; }
                s.Push(f.Visual);
                f.Visual = null;
            }

            if (f.Ring >= 0 && RingOwner[f.Ring] == f) { RingUpdate(f); RingOwner[f.Ring] = null; }      // (the last leg to the impact; it stays readable for the window)
            f.Ring = -1;
            f.Target = null; f.StuckTo = null; f.StuckVictim = null; f.IgnoreRoot = null; f.Cause = null; f.Def = null;
            Free.Push(f);
        }

        /// <summary>All flights end, the pools and prototypes go (match end).</summary>
        public static void Clear()
        {
            for (int i = 0; i < Active.Count; i++) { Active[i].Alive = false; Release(Active[i]); }
            Active.Clear();
            Free.Clear();
            FreeTracers.Clear();
            FreeAnchors.Clear();
            FreeVisuals.Clear();
            Prototypes.Clear();
            for (int i = 0; i < RingSize; i++) { RingOwner[i] = null; RingTime[i] = -10f; }
            PerceptionRadius.Clear();      // (HaloTag keys would keep the old map's data alive)
            for (int i = 0; i < RecentJpt.Length; i++) { RecentJpt[i] = null; RecentFrame[i] = -1; }
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
        }

        // ------------------------------------------------------------------ recent flights (the AI's projectile noticing)

        /// <summary>Ring of the recent flights: one slot per projectile, its segment over the last ~0.15..0.3 s, the shooter's team and the
        /// proj tag's "ai perception radius" (@412, world units). Fixed arrays, no allocations.</summary>
        public const int RingSize = 64;
        /// <summary>A flight is "recent" this long after its last movement (seconds; the plan's window, ours).</summary>
        public const float RecentSeconds = 0.3f;
        private const float RingSlide = 0.15f;      // the segment's start moves up once it is this old (ours)
        private static readonly Vector3[] RingFrom = new Vector3[RingSize], RingTo = new Vector3[RingSize];
        private static readonly HaloTeam[] RingTeam = new HaloTeam[RingSize];
        private static readonly float[] RingRadius = new float[RingSize], RingFromTime = new float[RingSize], RingTime = InitTimes();
        private static readonly Flight[] RingOwner = new Flight[RingSize];
        private static readonly Dictionary<HaloTag, float> PerceptionRadius = new Dictionary<HaloTag, float>();
        private static int _ringNext;
        private static bool _loggedRing;

        private static float[] InitTimes() { var a = new float[RingSize]; for (int i = 0; i < a.Length; i++) a[i] = -10f; return a; }

        private static void RingStart(Flight f)
        {
            int slot = _ringNext;
            _ringNext = (_ringNext + 1) % RingSize;
            Flight old = RingOwner[slot];
            if (old != null && old.Ring == slot) old.Ring = -1;      // the oldest flight loses its slot
            f.Ring = slot;
            RingOwner[slot] = f;
            float now = Time.time;
            RingFrom[slot] = f.Position; RingTo[slot] = f.Position; RingFromTime[slot] = now; RingTime[slot] = now;
            RingTeam[slot] = TeamOf(f);
            float r;
            HaloTag t = f.Def.Tag;
            if (!PerceptionRadius.TryGetValue(t, out r))
            {
                try { r = t.Root.F32(412); } catch (Exception) { r = 0f; }
                PerceptionRadius[t] = r;
            }

            RingRadius[slot] = r;
            if (!_loggedRing && r > 0f) { _loggedRing = true; HaloLog.Info("Combat: recent flights ring (" + RingSize + "): " + t.Name + " ai perception radius " + r.ToString("0.##") + " wu, team " + RingTeam[slot]); }
        }

        private static void RingUpdate(Flight f)
        {
            int slot = f.Ring;
            if (slot < 0 || RingOwner[slot] != f) return;
            float now = Time.time;
            if (now - RingFromTime[slot] > RingSlide) { RingFrom[slot] = RingTo[slot]; RingFromTime[slot] = RingTime[slot]; }
            RingTo[slot] = f.Position;
            RingTime[slot] = now;
        }

        /// <summary>The shooter's side: the firing unit's team (its body is the flight's ignore root), the player for players' shots.</summary>
        private static HaloTeam TeamOf(Flight f)
        {
            if (f.IgnoreRoot != null)
            {
                HaloUnit u = f.IgnoreRoot.GetComponentInParent<HaloUnit>();
                if (u != null) return u.Team;
            }

            return f.Shooter >= 0 ? HaloTeam.Player : HaloTeam.None;
        }

        /// <summary>Projectile flights of the last 0.3 s (segment, shooter team, the projectile's AI perception radius in world units) for the AI's
        /// projectile noticing, copied into the caller's arrays (at most <paramref name="max"/>; team / perceptionRadius may be null); returns
        /// the count. Fixed ring buffer of <see cref="RingSize"/>, no allocations.</summary>
        public static int CopyRecentFlights(Vector3[] from, Vector3[] to, HaloTeam[] team, float[] perceptionRadius, int max)
        {
            if (from == null || to == null) return 0;
            int cap = Mathf.Min(max, Mathf.Min(from.Length, to.Length));
            if (team != null) cap = Mathf.Min(cap, team.Length);
            if (perceptionRadius != null) cap = Mathf.Min(cap, perceptionRadius.Length);
            float now = Time.time;
            int n = 0;
            for (int i = 0; i < RingSize && n < cap; i++)
            {
                if (now - RingTime[i] > RecentSeconds) continue;
                from[n] = RingFrom[i]; to[n] = RingTo[i];
                if (team != null) team[n] = RingTeam[i];
                if (perceptionRadius != null) perceptionRadius[n] = RingRadius[i];
                n++;
            }

            return n;
        }

        /// <summary>Grenades (thrown projectiles) currently live: for the lab and the HUD.</summary>
        public static int CountGrenades()
        {
            int n = 0;
            for (int i = 0; i < Active.Count; i++) if (Active[i].Alive && Active[i].GrenadeKind >= 0) n++;
            return n;
        }

        public static Flight Find(Predicate<Flight> match)
        {
            for (int i = 0; i < Active.Count; i++) if (Active[i].Alive && match(Active[i])) return Active[i];
            return null;
        }
    }

    /// <summary>Plays a tag that may be an effect (effe -> HaloFx) or a sound (snd! / lsnd -> HaloAudio).</summary>
    /// <summary>Plays a tag that may be an effect (effe -> HaloFx) or a sound (snd! -> HaloAudio). Never throws.</summary>
    public static class HaloCombatFx
    {
        public static void Play(HaloTag tag, Vector3 position, Quaternion rotation, Transform attach)
        {
            if (tag == null) return;
            try
            {
                if (tag.ClassName == "snd!")
                {
                    if (attach != null) HaloAudio.Play(tag, attach, 1f); else HaloAudio.Play(tag, position, 1f);
                }
                else if (tag.ClassName == "effe") HaloFx.Play(tag, position, rotation, attach);
            }
            catch (Exception e) { HaloLog.Warn("Combat: playing " + tag + " failed: " + e.Message); }
        }

        /// <summary>
        /// An effect created by a unit (its body destroyed / region destroyed effect): the effect's damage parts (jpt!) are applied by
        /// Effects through HaloCombat.AreaDamage with <paramref name="creator"/> as the source (AI damage scale, "does not hurt owner").
        /// Play the effect ONLY - do not call HaloCombat.AreaDamage for the same effect as well.
        /// </summary>
        public static void Play(HaloTag tag, Vector3 position, Quaternion rotation, Transform attach, HaloUnit creator)
        {
            HaloFxRuntime.NextCreator = creator;
            try { Play(tag, position, rotation, attach); }
            finally { HaloFxRuntime.NextCreator = null; }
        }

        /// <summary>An effect on a weapon model's markers (its locations: "primary trigger", "primary ejection" ...). <paramref name="firstPerson"/> = the local player's own weapon.</summary>
        public static void OnModel(HaloTag tag, Transform model, bool firstPerson)
        {
            if (tag == null || model == null) return;
            try
            {
                if (tag.ClassName == "snd!") HaloAudio.Play(tag, model, 1f);
                else if (tag.ClassName == "effe") HaloFx.PlayOn(tag, model, firstPerson);
            }
            catch (Exception e) { HaloLog.Warn("Combat: playing " + tag + " failed: " + e.Message); }
        }

        /// <summary>An impact / detonation effect on a surface (normal) hit while travelling along <paramref name="incoming"/>.</summary>
        public static void Impact(HaloTag tag, Vector3 point, Vector3 normal, Vector3 incoming)
        {
            if (tag == null) return;
            try
            {
                if (tag.ClassName == "snd!") HaloAudio.Play(tag, point, 1f);
                else if (tag.ClassName == "effe") HaloFx.Impact(tag, point, normal, incoming);
            }
            catch (Exception e) { HaloLog.Warn("Combat: impact " + tag + " failed: " + e.Message); }
        }
    }
}

using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Grenades in the hand and physical melee.
    /// Grenades: grip at the chest (frags left, plasma right) takes one, the trigger arms it, letting go throws it with the hand's own
    /// velocity (times <see cref="ThrowScale"/>, at most the cyborg's grenade velocity); let go unarmed at the chest and it goes back.
    /// The flight, bounce, stick (plasma) and fuse are the grenade projectile's own (HaloProjectiles).
    /// Melee: no button in VR - swing the held weapon (its front faster than <see cref="MeleeSpeed"/>) or a free fist (faster than
    /// <see cref="FistSpeed"/>) into someone: the weapon's "player melee damage" (the fist: the cyborg's melee damage), from BEHIND an
    /// instant kill (Halo's rule), then a cooldown of the weapon's first-person melee animation (its length at 30 frames a second).
    /// </summary>
    public sealed partial class HaloHandling
    {
        public static float MeleeSpeed = 2.8f, FistSpeed = 3.2f;       // m/s (ours)
        public static float MeleeRadius = 0.13f;                          // m (ours)

        private GameObject _grenadeVis;
        private int _grenadeHand = -1, _grenadeKind;
        private bool _grenadeArmed;
        private float _meleeReadyAt;
        private Vector3 _lastFront;
        private bool _haveFront;
        private readonly Collider[] _meleeHits = new Collider[16];

        // ------------------------------------------------------------------ grenades

        private bool TryTakeGrenade(int h, Vector3 hp)
        {
            if (_grenadeHand >= 0 || (HaloSession.Rules != null && !HaloSession.Rules.Grenades && HaloLocal.FragGrenades + HaloLocal.PlasmaGrenades == 0)) return false;
            float df = HaloLocal.FragGrenades > 0 ? (hp - ZoneCenter(Zone.Frags)).magnitude : float.MaxValue;
            float dp = HaloLocal.PlasmaGrenades > 0 ? (hp - ZoneCenter(Zone.Plasma)).magnitude : float.MaxValue;
            int kind = df <= dp ? 0 : 1;
            float d = Mathf.Min(df, dp);
            if (d > ZoneRadius(kind == 0 ? Zone.Frags : Zone.Plasma)) return false;
            if (kind == 0) HaloLocal.FragGrenades--; else HaloLocal.PlasmaGrenades--;
            _grenadeKind = kind;
            _grenadeHand = h;
            _grenadeArmed = false;
            _grenadeVis = GrenadeModel(kind);
            HaloXr.Pulse(h == 1, 0.3f, 0.05f);
            return true;
        }

        private void StepGrenade(HaloHand H)
        {
            if (H.TriggerPressed && !_grenadeArmed)
            {
                _grenadeArmed = true;
                HaloXr.Pulse(_grenadeHand == 1, 0.45f, 0.06f);
            }
        }

        private void ReleaseGrenade()
        {
            int h = _grenadeHand;
            if (h < 0) return;
            Vector3 p = _grenadeVis != null ? _grenadeVis.transform.position : HandPos(h);
            Zone home = _grenadeKind == 0 ? Zone.Frags : Zone.Plasma;
            if (!_grenadeArmed && (p - ZoneCenter(home)).magnitude < ZoneRadius(home) * 1.4f) { PutGrenadeBack(); return; }
            Vector3 v = Body.HandVelocity(h == 1) * ThrowScale;
            float max = HaloCombat.Catalog != null && HaloCombat.Catalog.GrenadeVelocity > 0f ? HaloCombat.Catalog.GrenadeVelocity * HaloSpace.Unit : 30f;
            if (v.magnitude > max) v = v.normalized * max;
            if (v.magnitude < 1f) v = Body.HeadRot * Vector3.forward * 1.5f;      // a drop rather than a throw
            ThrowGrenade(_grenadeKind, p, v);
            if (_grenadeVis != null) Destroy(_grenadeVis);
            _grenadeVis = null;
            _grenadeHand = -1;
            HaloXr.Pulse(h == 1, 0.25f, 0.05f);
        }

        private void PutGrenadeBack()
        {
            if (_grenadeHand < 0) return;
            if (_grenadeKind == 0) HaloLocal.FragGrenades++; else HaloLocal.PlasmaGrenades++;
            if (_grenadeVis != null) Destroy(_grenadeVis);
            _grenadeVis = null;
            _grenadeHand = -1;
        }

        /// <summary>A grenade leaves the hand: the globals' grenade projectile with this velocity; the throwing effect; Net hears of it.</summary>
        public void ThrowGrenade(int kind, Vector3 pos, Vector3 vel)
        {
            var cat = HaloCombat.Catalog;
            if (cat == null || kind < 0 || kind >= cat.Grenades.Count) return;
            var gr = cat.Grenades[kind];
            var pd = cat.Projectile(gr.Projectile);
            if (pd == null) return;
            HaloLog.Info("Combat: " + (kind == 0 ? "frag" : "plasma") + " grenade thrown at " + vel.magnitude.ToString("0.0") + " m/s (" + HaloLocal.FragGrenades + " frag / " + HaloLocal.PlasmaGrenades + " plasma left)");
            if (gr.ThrowingEffect != null) HaloCombatFx.Play(gr.ThrowingEffect, pos, Quaternion.LookRotation(vel.normalized), null);
            HaloProjectiles.Launch(pd, pos, vel, vel, HaloLocal.NetId, pd.Tag, true, true, null, null);
            HaloCombat.NoteThrow(pd.Tag, pos, vel);
        }

        // ------------------------------------------------------------------ melee

        private void StepMelee(HaloHand L, HaloHand R, float dt)
        {
            if (_desktop || dt <= 0f) return;
            HaloGun g = HeldGun;
            if (g != null && _gunHand >= 0)
            {
                Vector3 front = g.transform.TransformPoint(g.FrontLocal);
                Vector3 vel = _haveFront ? (front - _lastFront) / dt : Vector3.zero;
                _lastFront = front;
                _haveFront = true;
                if (Time.time >= _meleeReadyAt && vel.magnitude > MeleeSpeed * Body.Scale && !g.Def.PreventsMelee)
                {
                    Vector3 mid = g.transform.TransformPoint((g.FrontLocal + g.RearLocal) * 0.5f);
                    if (SwingAt(front, vel, g.Def.MeleeDamage, g.Def.Tag, _gunHand) || SwingAt(mid, vel, g.Def.MeleeDamage, g.Def.Tag, _gunHand))
                        _meleeReadyAt = Time.time + (g.Def.MeleeSeconds > 0.2f ? g.Def.MeleeSeconds : 1f);
                }
            }
            else _haveFront = false;

            // a free fist
            if (Time.time < _meleeReadyAt || HaloCombat.Catalog == null || HaloCombat.Catalog.UnitMelee == null) return;
            for (int h = 0; h < 2; h++)
            {
                if (Busy(h)) continue;
                Vector3 v = Body.HandVelocity(h == 1);
                if (v.magnitude < FistSpeed * Body.Scale) continue;
                if (SwingAt(HandPos(h), v, HaloCombat.Catalog.UnitMelee, null, h)) { _meleeReadyAt = Time.time + 0.8f; break; }
            }
        }

        /// <summary>A melee strike at a point moving with <paramref name="vel"/>: the first damageable that is not ours takes it. True when it hit.</summary>
        private bool SwingAt(Vector3 at, Vector3 vel, HaloTag effect, HaloTag cause, int hand)
        {
            int n = Physics.OverlapSphereNonAlloc(at, MeleeRadius * Body.Scale, _meleeHits, (1 << HaloLayers.Hitbox) | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider c = _meleeHits[i];
                if (c == null || HaloCombat.IsOwnCollider(c)) continue;
                IHaloDamageable v = c.GetComponentInParent<IHaloDamageable>();
                if (v == null) continue;
                IHaloHitInfo info = c.GetComponentInParent<IHaloHitInfo>();
                if (info != null && !info.HitAlive) continue;
                MeleeHit(v, info, c, at, vel.sqrMagnitude > 1e-6f ? vel.normalized : Body.Forward, effect, cause);
                if (hand >= 0) HaloXr.Pulse(hand == 1, 0.9f, 0.12f);
                return true;
            }

            // no one there: a breakable world surface (glass) the swing runs into
            Vector3 sd = vel.sqrMagnitude > 1e-6f ? vel.normalized : Body.Forward;
            RaycastHit wh;
            if (effect != null && Physics.SphereCast(at - sd * 0.15f, MeleeRadius * 0.5f * Body.Scale, sd, out wh, 0.3f, (1 << HaloLayers.World) | (1 << HaloLayers.Environment), QueryTriggerInteraction.Ignore) && HaloWorld.IsBreakable(wh))
            {
                HaloProjectiles.SurfaceHit(wh, effect, HaloProjectiles.MaterialAt(wh, null, null), 0f, null);
                if (hand >= 0) HaloXr.Pulse(hand == 1, 0.6f, 0.08f);
                return true;
            }

            return false;
        }

        public void MeleeHit(IHaloDamageable v, IHaloHitInfo info, Collider c, Vector3 at, Vector3 dir, HaloTag effect, HaloTag cause)
        {
            var cat = HaloCombat.Catalog;
            var dd = cat != null ? cat.Damage(effect) : null;
            float amount = dd != null ? dd.DirectAmount(new System.Random(), 0f, 0f, 0f) : 50f;
            amount = HaloCombat.ScaleForTarget(amount, HaloLocal.NetId, null, v);      // (the campaign: the unit's difficulty vitality)
            Vector3 facing = info != null ? info.HitFacing : ((Component)v).transform.forward;
            facing.y = 0f;
            Vector3 flat = dir; flat.y = 0f;
            // from behind: the strike runs along the way the victim faces (within 60 degrees). Only BackHit is sent: the victim decides the
            // instant kill (players: HaloVitals; units: HaloUnit.TakeHit, which spares "not instantly killed by melee" units - hunters)
            bool back = facing.sqrMagnitude > 1e-4f && flat.sqrMagnitude > 1e-4f && Vector3.Angle(facing, flat) < 60f;
            var hit = new HaloHit
            {
                Amount = amount, Effect = effect, SourcePlayer = HaloLocal.NetId, Cause = cause, Point = at, Direction = dir,
                Melee = true, BackHit = back,
            };
            HaloLog.Info("Combat: melee hit " + (c != null ? c.name : "?") + " for " + amount.ToString("0") + (back ? " from behind (the victim decides the instant kill)" : ""));
            HaloProjectiles.Deliver(v, hit);
            if (dd != null && dd.Sound != null) HaloAudio.Play(dd.Sound, at, 1f);
            HaloGun mg = HeldGun;      // (the swing's own sound: the first-person melee animation's, e.g. weapon_animsr_melee)
            if (mg != null && mg.Sounds != null && mg.Sounds.Melee != null && mg.Def.MeleeDamage == effect) mg.Sound(mg.Sounds.Melee, "melee");
            HaloCombat.NoteMelee();
        }

        /// <summary>The monitor's melee (F): a short sphere sweep along the view.</summary>
        private void DeskMelee()
        {
            if (Time.time < _meleeReadyAt) return;
            HaloGun g = HeldGun;
            if (g != null && g.Def.PreventsMelee) return;
            HaloTag effect = g != null ? g.Def.MeleeDamage : (HaloCombat.Catalog != null ? HaloCombat.Catalog.UnitMelee : null);
            _meleeReadyAt = Time.time + (g != null && g.Def.MeleeSeconds > 0.2f ? g.Def.MeleeSeconds : 1f);
            if (g != null) g.Animate("melee", g.Def.MeleeSeconds > 0.2f ? g.Def.MeleeSeconds : -1f);
            HaloCombat.NoteMelee();
            Ray r = HaloDesk.AimRay();
            RaycastHit hit;
            float reach = 1.0f + (Body.HeadPos - r.origin).magnitude;
            if (!Physics.SphereCast(r.origin, 0.2f, r.direction, out hit, reach, (1 << HaloLayers.Hitbox) | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Collide))
            {
                RaycastHit wh;      // (a pane of glass in front)
                if (effect != null && Physics.Raycast(r.origin, r.direction, out wh, reach, (1 << HaloLayers.World) | (1 << HaloLayers.Environment), QueryTriggerInteraction.Ignore) && HaloWorld.IsBreakable(wh))
                    HaloProjectiles.SurfaceHit(wh, effect, HaloProjectiles.MaterialAt(wh, null, null), 0f, null);
                return;
            }
            if (HaloCombat.IsOwnCollider(hit.collider)) return;
            IHaloDamageable v = hit.collider.GetComponentInParent<IHaloDamageable>();
            if (v == null) return;
            IHaloHitInfo info = hit.collider.GetComponentInParent<IHaloHitInfo>();
            if (info != null && !info.HitAlive) return;
            MeleeHit(v, info, hit.collider, hit.point, r.direction, effect, g != null ? g.Def.Tag : null);
        }
    }
}

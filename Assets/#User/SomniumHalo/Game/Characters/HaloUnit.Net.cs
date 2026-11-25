using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Co-op puppets (Net) and checkpoints (Mission). A puppet is a client's copy of a host unit: Net moves its transform and writes the
    /// controls; the unit only animates (its velocity from how the transform moved), plays the events Net reports (PlayFire: the firing effect
    /// and a visual-only projectile; PlayMelee / PlayThrow: the animations) and hands hits to the host (HaloNetGame.PuppetHit) instead of
    /// taking them. Checkpoints: HaloBipeds registers the part "characters" (every unit's life, vitality, place, weapon, grenades) with
    /// HaloCheckpoints on a campaign map; a revert puts units that still exist back as they were (bodies of the dead since rise again).
    /// </summary>
    public partial class HaloUnit
    {
        /// <summary>Co-op client copy: no own movement / gravity / firing / damage (Net drives it and forwards hits to the host).</summary>
        public bool Puppet;

        /// <summary>Volleys fired (each FireOnce / PlayFire) - Net sends exact shots from it.</summary>
        public int Shots => ShotsFired;

        private Vector3 _puppetLast;
        private bool _puppetHasLast;

        /// <summary>Net: the held weapon changed.</summary>
        public void SetWeapon(HaloTag weapon) { if (weapon != Weapon) Give(weapon); }

        /// <summary>Net: the host's unit fired one volley (effects, recoil, a projectile for the eyes only).</summary>
        public void PlayFire() { FireOnce(true); }
        public void PlayMelee() { if (Animator != null) Animator.Melee(); }
        public void PlayThrow() { if (Animator != null) Animator.Throw(); }

        private void TickPuppet(float dt)
        {
            Vector3 p = transform.position;
            if (_puppetHasLast && dt > 0f)
            {
                Vector3 v = (p - _puppetLast) / dt;
                if (v.sqrMagnitude > 900f) v = Vector3.zero;      // a snap
                _velocity = Vector3.Lerp(_velocity, v, Mathf.Clamp01(dt * 10f));
            }

            _puppetLast = p;
            _puppetHasLast = true;
            _yaw = transform.eulerAngles.y;
            // a leap in the host's record: airborne (the leap-airborne base) until it ends there
            if (_netLeaping) { _grounded = false; _airTime += dt; }
            else { _grounded = true; _airTime = 0f; }
            AimAngles(dt);
            if (!Alive) return;
            if (_shieldFlash > 0f) _shieldFlash = Mathf.Max(0f, _shieldFlash - dt * 2f);
            // the jackal's energy shield region follows the host's shield (Net writes Shield): down at 0, back when it recharges
            if (MaxShield > 0f)
            {
                if (Shield <= 0f && !_shieldRegionOff) ShieldRegionOff(true);
                else if (Shield > 0f && _shieldRegionOff) ShieldRegionOff(false);
            }

            if (Latched && Animator != null && Animator.Action == HaloUnitAnimator.ActionKind.None) Animator.Gesture(HaloUnitAnimator.AnimMeleeContinuous);
            TickShieldGlow();
        }

        // ------------------------------------------------------------------ body state over the wire (co-op)

        private bool _feignHard, _netLeaping;
        private int _feignDir, _feignRegion;
        private static int _netBodyLogged;

        /// <summary>The held feigned-death clip (hard, direction 0-3, damage region) - Net sends it while <see cref="Feigning"/>.</summary>
        public bool FeignHard => _feignHard;
        public int FeignDir => _feignDir;
        public int FeignRegion => _feignRegion;
        /// <summary>The active camouflage hides the unit right now (not revealed by firing / a hit).</summary>
        public bool CamoHidden => _camoHidden;

        /// <summary>Net (host): the body state for the actor record: 1 feigning death, 2 latched, 3 leaping, 0 none (HaloNetActor.Body*).</summary>
        public int NetBodyState => !Alive ? 0 : Feigning ? 1 : Latched ? 2 : (Puppet ? _netLeaping : Leaping) ? 3 : 0;

        /// <summary>
        /// Net (a co-op follower's puppet): the host unit's body state from its record - the camouflage hides / shows the body, a feigned death
        /// plays the host's held death clip and gets up (resurrect) when it ends there, a latched infection form plays melee-continuous on
        /// its target (its place follows the host's), a leap plays leap-start then leap-airborne until it lands.
        /// </summary>
        public void ApplyNetBody(int state, bool camoHidden, bool feignHard, int feignDir, int feignRegion)
        {
            if (!Puppet || !Alive) return;
            if (camoHidden != _camoHidden) SetCamoHidden(camoHidden);
            bool feign = state == 1, latch = state == 2, leap = state == 3;
            if (feign && !Feigning && Animator != null)
            {
                Feigning = true;
                _feignHard = feignHard; _feignDir = feignDir; _feignRegion = feignRegion;
                _feignBack = feignDir == 3;
                Firing = false;
                Animator.Feign(feignHard, feignDir, feignRegion);
                if (_netBodyLogged++ < 10) HaloLog.Info("Characters: co-op puppet " + Def.Name + " feigns death as the host says (" + (Animator.DeathName ?? "-") + ")");
            }
            else if (!feign && Feigning) EndFeign();

            if (latch && !Latched)
            {
                Latched = true;
                if (Animator != null) Animator.Gesture(HaloUnitAnimator.AnimMeleeContinuous);
                if (_netBodyLogged++ < 10) HaloLog.Info("Characters: co-op puppet " + Def.Name + " is latched as the host says");
            }
            else if (!latch && Latched) Latched = false;      // (a puppet has no latch target / parent: its place comes from the host)

            if (leap != _netLeaping && Animator != null)
            {
                if (leap) { Animator.PlayLeap(HaloUnitAnimator.LeapPhase.Start); Animator.PlayLeap(HaloUnitAnimator.LeapPhase.Airborne); }
                else Animator.EndLeap();
            }

            _netLeaping = leap;
        }

        // ------------------------------------------------------------------ checkpoints

        internal sealed class Saved
        {
            public HaloUnit Unit;
            public bool Alive;
            public float Body, Shield, Yaw;
            public Vector3 Position;
            public HaloTag Weapon;
            public int Grenades, Rounds;
            public string Stance;
        }

        internal Saved Capture()
        {
            return new Saved { Unit = this, Alive = Alive, Body = Body, Shield = Shield, Yaw = _yaw, Position = transform.position, Weapon = Weapon, Grenades = Grenades, Rounds = Rounds, Stance = Stance };
        }

        internal void Restore(Saved s)
        {
            if (s.Alive && !Alive) Revive();
            if (!s.Alive && Alive) { PlaceDeadNow(); }
            Body = s.Body;
            Shield = s.Shield;
            Grenades = s.Grenades;
            Stance = s.Stance;
            if (s.Alive && s.Weapon != Weapon) Give(s.Weapon);
            Rounds = s.Rounds;
            MoveVelocity = Vector3.zero; Firing = false; Melee = false; ThrowGrenade = false; Jump = false;
            if (Seat == null) Teleport(s.Position, s.Yaw);
            // the revert's BSP switch judged this unit where it stood before the revert: judge it again where it now is (hide or show it)
            if (HaloCampaign.Active && !HaloCampaign.SwitchingBsp) OnBspSwitched(-1, HaloCampaign.BspIndex);
        }

        /// <summary>Back to life where it lies (a checkpoint from before its death): full animation, hit boxes, its weapon.</summary>
        public void Revive()
        {
            if (Alive) return;
            Alive = true;
            DeadFor = 0f;
            _bodyDown = false;
            Body = 1f;
            Shield = MaxShield > 0f ? 1f : 0f;
            for (int i = 0; i < Hitboxes.Count; i++) if (Hitboxes[i].Box != null) Hitboxes[i].Box.isTrigger = false;
            if (Built != null && Built.Root != null) Built.Root.SetActive(true);
            if (Animator != null) Animator.Revive();
        }

        private void PlaceDeadNow()
        {
            int f = _frames;
            _frames = 0;
            PlaceDead();
            _frames = f;
            DeadFor = 0f;
        }
    }

    internal static class HaloBipedsCheckpoints
    {
        private static bool _registered;
        private static HaloMap _for;

        /// <summary>Registers the "characters" checkpoint part once per campaign map (HaloBipeds.Spawn calls it).</summary>
        internal static void Ensure()
        {
            if (!HaloCampaign.Active) return;
            HaloMap m = HaloSession.Map;
            if (_registered && _for == m) return;
            _registered = true;
            _for = m;
            HaloCheckpoints.Register("characters", Capture, Restore);
        }

        private static object Capture()
        {
            var l = new List<HaloUnit.Saved>();
            for (int i = 0; i < HaloBipeds.All.Count; i++) if (HaloBipeds.All[i] != null && !HaloBipeds.All[i].Puppet && !HaloAi.IsActor(HaloBipeds.All[i])) l.Add(HaloBipeds.All[i].Capture());
            return l;
        }

        private static void Restore(object state)
        {
            var l = state as List<HaloUnit.Saved>;
            if (l == null) return;
            int n = 0;
            for (int i = 0; i < l.Count; i++)
            {
                HaloUnit u = l[i].Unit;
                if (u == null) continue;
                try { u.Restore(l[i]); n++; } catch (System.Exception e) { HaloLog.Warn("Characters: restoring " + u.name + " failed: " + e.Message); }
            }

            HaloLog.Info("Characters: checkpoint restored " + n + " of " + l.Count + " units");
        }
    }
}

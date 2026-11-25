using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The VR hands: grip near a carrying place to take what is there (a gun from a holster, a grenade from the chest, ammunition from the
    /// pouch), grip a weapon lying in the world to take it (two carried: the one in hand is swapped for it), the free hand on the gun's
    /// handguard (the long guns aim along the line between the hands) or on its charging handle (a rack after an empty reload), the
    /// trigger fires, B / Y (the gun hand's top button) drops the magazine. Letting go of a gun: into a free holster when near one, else it
    /// falls to the ground. A grenade: the trigger arms it, letting go throws it with the hand's velocity (Halo's grenades are thrown, not
    /// cooked; the fuse is the projectile's own).
    /// </summary>
    public sealed partial class HaloHandling
    {
        /// <summary>A free hand this close to the support point takes the handguard; to the charging handle, racks (ours).</summary>
        public static float SupportRadius = 0.15f, RackRadius = 0.13f, CupRadius = 0.14f;
        /// <summary>Pull the charging handle back this far to rack (ours).</summary>
        public static float RackDistance = 0.06f;
        /// <summary>A throw is the hand's speed times this, at most the cyborg's grenade velocity (ours: arms throw weaker than a cyborg).</summary>
        public static float ThrowScale = 1.5f;

        private Quaternion _gripPitch = Quaternion.identity;

        private bool Busy(int h)
        {
            return h == _gunHand || h == _supportHand || h == _magHand || h == _grenadeHand || h == _shellHand || (_objective != null && h == 1);
        }

        private Vector3 HandPos(int h)
        {
            Vector3 p;
            Quaternion q;
            Body.HandPose(h == 1, out p, out q);
            return p;
        }

        private static HaloHand HandOf(int h, HaloHand L, HaloHand R) { return h == 1 ? R : L; }

        private void Hands(HaloHand L, HaloHand R, float dt)
        {
            for (int h = 0; h < 2; h++)
            {
                HaloHand H = HandOf(h, L, R);
                if (!H.GripHeld && Busy(h)) GripReleased(h);
            }

            for (int h = 0; h < 2; h++)
            {
                HaloHand H = HandOf(h, L, R);
                if (H.GripPressed && !Busy(h)) GripPressed(h);
            }

            if (_supportHand >= 0 && _held >= 0) StepSupport();
            if (_heldMag != null) StepMag(dt);
            if (_shellHand >= 0) StepShells(dt);
            if (_grenadeHand >= 0) StepGrenade(HandOf(_grenadeHand, L, R));

            HaloGun g = HeldGun;
            if (g != null && _gunHand >= 0)
            {
                HaloHand G = HandOf(_gunHand, L, R);
                _gunHandPressed = G.TriggerPressed;
                if (G.SecondaryPressed) EjectMagazine(g);
                if (G.PrimaryPressed && HaloCombat.AssistedReload && g.NeedsRack) { g.NeedsRack = false; PlayChamber(g); }
                Trigger(g, G.TriggerHeld, dt);
                // a stick click on the gun hand cycles the zoom of a weapon that has one (VR: the HUD / a scope may use HaloLocal.ZoomMagnification)
                if (G.StickClickPressed) CycleZoom(g);
            }

            HoverHaptics();
            HoverPickups();
        }

        private void GripPressed(int h)
        {
            Vector3 hp = HandPos(h);
            HaloGun g = HeldGun;
            if (g != null)
            {
                // the free hand: the charging handle (a rack is due), the handguard, the pouch, the chest, a weapon on the ground (a swap)
                if (_supportHand < 0)
                {
                    // the nearer of the pulled part (slide, operating handle, pump) and the support / cup point, each within its own reach
                    Vector3 rack = g.transform.TransformPoint(g.RackLocal), sup = g.transform.TransformPoint(g.SupportLocal);
                    float k = Body.Scale;
                    float dr = g.HasPull ? (hp - rack).magnitude : float.MaxValue, ds = (hp - sup).magnitude;
                    float rr = RackRadius * (g.NeedsRack ? 1.3f : 1f) * k, sr = (g.Def.LongGun ? SupportRadius : CupRadius) * k;
                    bool canRack = dr < rr, canSupport = ds < sr && (g.HasSupport || !g.Def.LongGun);
                    if (canRack && (!canSupport || dr <= ds || g.NeedsRack)) { StartSupport(h, Support.Rack); return; }
                    if (canSupport) { StartSupport(h, g.Def.LongGun ? Support.Handguard : Support.Cup); return; }
                }

                if ((hp - ZoneCenter(Zone.Pouch)).magnitude < ZoneRadius(Zone.Pouch) && TakeFromPouch(h, g)) return;
                if (!g.Def.PreventsGrenadeThrowing && TryTakeGrenade(h, hp)) return;
                HaloPickups.Item lying = HaloPickups.NearestWeapon(hp, HaloPickups.HandReach * Body.Scale, true);
                if (lying != null) { SwapFor(lying, h); return; }
                if (_supportHand < 0 && (hp - g.transform.TransformPoint(g.SupportLocal)).magnitude < SupportRadius * 1.6f * Body.Scale) { StartSupport(h, g.Def.LongGun ? Support.Handguard : Support.Cup); return; }
                return;
            }

            // nothing in the hands: the nearest carried thing within reach, else a weapon on the ground
            float best = float.MaxValue;
            int bestGun = -1;
            for (int i = 0; i < 2; i++)
            {
                if (Guns[i] == null) continue;
                float d = GunReach(i, hp);
                if (d < best) { best = d; bestGun = i; }
            }

            if (bestGun >= 0) { Draw(bestGun, h); return; }
            if (TryTakeGrenade(h, hp)) return;
            HaloPickups.Item item = HaloPickups.NearestWeapon(hp, HaloPickups.HandReach * Body.Scale, true);
            if (item != null) { SwapFor(item, h); return; }
        }

        private void GripReleased(int h)
        {
            if (h == _supportHand) EndSupport();
            if (h == _gunHand)
            {
                HaloGun g = HeldGun;
                if (g != null)
                {
                    if (!TryHolsterNear(g)) { _gunHand = -1; DropGun(g, Body.HandVelocity(h == 1) * 0.8f); }
                }

                _gunHand = -1;

                _held = -1;
            }

            if (h == _magHand) DropHeldMag();
            if (h == _shellHand) { _shellHand = -1; _shellsInHand = 0; if (_shellVis != null) _shellVis.SetActive(false); }
            if (h == _grenadeHand) ReleaseGrenade();
        }

        /// <summary>A holstered gun into a hand.</summary>
        private void Draw(int index, int hand)
        {
            HaloGun g = Guns[index];
            if (g == null) return;
            _held = index;
            _lastHeld = index;
            _gunHand = hand;
            _supportHand = -1;
            _support = Support.None;
            _twoHandBlend = 0f;
            g.gameObject.SetActive(true);
            HaloLog.Info("Combat: " + (hand == 1 ? "right" : hand == 0 ? "left" : "monitor") + " hand draws " + g.Def.Name + " from " + g.Slot);
            g.Animate("ready");
            if (g.Def.ReadyEffect != null) HaloCombatFx.OnModel(g.Def.ReadyEffect, g.ModelRoot, true);
            else if (g.Sounds != null && g.Sounds.Ready != null) g.Sound(g.Sounds.Ready, "ready");      // (the first-person ready animation's own sound: "weapon ready", pistol_ready ...)
            else if (g.Def.PickupSound != null) HaloAudio.Play(g.Def.PickupSound, g.transform, 0.6f);
            if (hand >= 0) HaloXr.Pulse(hand == 1, 0.3f, 0.06f);
        }

        /// <summary>A weapon from the ground into the hand: with two carried, the one in hand (else the last drawn) is dropped in its place.</summary>
        private void SwapFor(HaloPickups.Item item, int hand)
        {
            HaloGun inHand = HeldGun;
            if (Carried >= 2)
            {
                HaloGun out_ = inHand ?? Guns[_lastHeld] ?? Guns[0];
                if (out_ != null)
                {
                    if (out_ == inHand) { _gunHand = -1; _held = -1; EndSupport(); }
                    Release(out_);
                    HaloPickups.DropWeapon(out_, Vector3.zero, true, item.Gun != null ? item.Gun.transform.position : (Vector3?)null);
                }
            }
            else if (inHand != null)
            {
                // one carried and in the other hand: it goes to its holster (one weapon in the hands at a time)
                Holster(inHand);
            }

            HaloGun g = HaloPickups.TakeWeapon(item);
            if (g == null) return;
            int i = Carry(g);
            if (i < 0) { HaloPickups.DropWeapon(g, Vector3.zero, true); return; }
            Draw(i, hand);
            HaloCombat.SayPickedUp(g.Def);
        }

        // ------------------------------------------------------------------ the free hand on the gun

        private void StartSupport(int h, Support mode)
        {
            HaloGun g = HeldGun;
            if (g == null) return;
            _supportHand = h;
            _support = mode;
            _rackPulled = false;
            _supportGrabLocal = g.transform.InverseTransformPoint(HandPos(h));
            g.HandOnPull = mode == Support.Rack;
            HaloXr.Pulse(h == 1, mode == Support.Rack ? 0.35f : 0.25f, 0.04f);
        }

        private void EndSupport()
        {
            HaloGun g = HeldGun;
            if (g != null && _support == Support.Rack && _rackPulled && g.Sounds != null) g.Sound(g.Sounds.Release, "release");      // (let go while held back: it runs forward)
            if (g != null) { g.SetPulled(0f); g.HandOnPull = false; }
            _supportHand = -1;
            _support = Support.None;
            _rackPulled = false;
        }

        /// <summary>
        /// The free hand on the slide / operating handle / pump: the part follows the hand back (as far as it travels); past 60 % of its travel
        /// it racks (a waiting rack after an empty reload is done, the locked-back slide is released); the hand may keep holding it.
        /// </summary>
        private void StepSupport()
        {
            HaloGun g = HeldGun;
            if (_support != Support.Rack || g == null) return;
            Vector3 local = g.transform.InverseTransformPoint(HandPos(_supportHand));
            float scale = g.transform.lossyScale.z;
            float pull = Mathf.Max(0f, _supportGrabLocal.z - local.z) * scale;      // metres back along the barrel
            float travel = Mathf.Max(0.02f, g.PullTravel * scale);
            g.SetPulled(Mathf.Min(pull, travel));
            if (!_rackPulled && pull >= Mathf.Max(0.015f, travel * 0.6f))
            {
                _rackPulled = true;
                HaloXr.Pulse(_supportHand == 1, 0.6f, 0.06f);
                if (g.Sounds != null) g.Sound(g.Sounds.Pull, "pull back");
                if (g.NeedsRack) FinishRack(g); else PlayChamber(g);
            }
            else if (_rackPulled && pull < travel * 0.2f) { _rackPulled = false; if (g.Sounds != null) g.Sound(g.Sounds.Release, "release"); }      // (forward again: another pull racks again)
        }

        private void FinishRack(HaloGun g)
        {
            if (!g.NeedsRack) return;
            g.NeedsRack = false;
            HaloLog.Info("Combat: " + g.Def.Name + " racked");
            PlayChamber(g);
        }

        private void PlayChamber(HaloGun g)
        {
            var m = g.Def.PrimaryMagazine;
            if (m != null && m.ChamberingEffect != null) HaloCombatFx.OnModel(m.ChamberingEffect, g.ModelRoot, true);
            else if (g.Def.ReadyEffect != null) HaloCombatFx.OnModel(g.Def.ReadyEffect, g.ModelRoot, true);
        }

        // ------------------------------------------------------------------ the poses of what is in the hands

        private void UpdateHeldPoses(float dt)
        {
            HaloGun g = HeldGun;
            if (g != null && (_gunHand >= 0 || _desktop)) PoseGun(g, dt);
            if (_heldMag != null && _magHand >= 0)
            {
                Vector3 p; Quaternion q;
                Body.HandPose(_magHand == 1, out p, out q);
                _heldMag.transform.SetPositionAndRotation(p + q * new Vector3(0f, 0.0f, 0.05f) * Body.Scale, q * Quaternion.Euler(60f, 0f, 0f));
            }

            if (_shellHand >= 0 && _shellVis != null)
            {
                Vector3 p; Quaternion q;
                Body.HandPose(_shellHand == 1, out p, out q);
                _shellVis.transform.SetPositionAndRotation(p + q * new Vector3(0f, 0f, 0.04f) * Body.Scale, q * Quaternion.Euler(-70f, 0f, 0f));
            }

            if (_grenadeHand >= 0 && _grenadeVis != null)
            {
                Vector3 p; Quaternion q;
                Body.HandPose(_grenadeHand == 1, out p, out q);
                _grenadeVis.transform.SetPositionAndRotation(p + q * new Vector3(0f, 0f, 0.04f) * Body.Scale, q);
                _grenadeVis.transform.localScale = Vector3.one * (Body.AvatarScale * (1.7f / HaloGun.CyborgHeight) * HaloGun.SizeFactor);
            }
        }

        private void PoseGun(HaloGun g, float dt)
        {
            Vector3 hp;
            Quaternion hr;
            int hand = _gunHand >= 0 ? _gunHand : 1;
            Body.HandPose(hand == 1, out hp, out hr);
            if (_desktop) DeskHold(g, ref hp, ref hr);
            Quaternion single = hr * Quaternion.Euler(-HaloCombat.GripPitchDegrees, 0f, 0f);
            Quaternion rot = single;
            bool two = !_desktop && _supportHand >= 0 && _support == Support.Handguard && g.Def.LongGun;
            Vector3 sPos = two ? HandPos(_supportHand) : Vector3.zero;
            Vector3 d = sPos - hp;
            if (two && d.magnitude < 0.12f) two = false;
            _twoHandBlend = Mathf.MoveTowards(_twoHandBlend, two ? 1f : 0f, dt * 12f);
            if (two || _twoHandBlend > 0f)
            {
                if (two)
                {
                    // the support point goes on the line from the grip to the free hand; the roll follows the grip hand
                    Vector3 sl = g.SupportLocal;
                    if (sl.sqrMagnitude < 1e-6f) sl = Vector3.forward;
                    Quaternion r0 = Quaternion.FromToRotation(sl.normalized, Quaternion.Inverse(Quaternion.identity) * d.normalized);
                    Vector3 axis = d.normalized;
                    Vector3 upG = Vector3.ProjectOnPlane(r0 * Vector3.up, axis);
                    Vector3 upH = Vector3.ProjectOnPlane(hr * Vector3.up, axis);
                    float roll = upG.sqrMagnitude > 1e-6f && upH.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(upG, upH, axis) : 0f;
                    _twoHandRot = Quaternion.AngleAxis(roll, axis) * r0;
                }

                rot = Quaternion.Slerp(single, _twoHandRot, _twoHandBlend);
            }

            // a cupped one-handed gun (pistol): the support hand under the grip steadies it - the aim is the two hands' pointing together
            bool cup = !_desktop && _supportHand >= 0 && _support == Support.Cup;
            _cupBlend = Mathf.MoveTowards(_cupBlend, cup ? 1f : 0f, dt * 10f);
            if (_cupBlend > 0f)
            {
                if (cup)
                {
                    Vector3 sp; Quaternion sq;
                    Body.HandPose(_supportHand == 1, out sp, out sq);
                    Vector3 f = (single * Vector3.forward + sq * Vector3.forward * 0.8f).normalized;
                    _cupRot = Quaternion.LookRotation(f, single * Vector3.up);
                }

                rot = Quaternion.Slerp(rot, _cupRot, _cupBlend * 0.5f);
            }

            Quaternion kick;
            Vector3 shove;
            g.StepRecoil(dt, _twoHandBlend > 0.5f ? 0.55f : _cupBlend > 0.5f ? 0.6f : (_supportHand >= 0 ? 0.75f : 1f), out kick, out shove);
            rot = rot * kick;
            Vector3 pos = hp + rot * shove;
            g.transform.SetPositionAndRotation(pos, rot);
            SeatGun(g);
        }

        private bool _saidSeatGun;

        /// <summary>
        /// (Vehicles' request, user report "their gun is displayed outside the car far away") In a seat that allows weapons on a monitor the hand
        /// pose above hangs off the view (HaloBody: the camera), and the seat's camera is a chase camera metres behind the vehicle: the gun goes
        /// in the seated avatar's right hand instead (HaloSeatPoses.OwnGunPose: the seat animation's hand, in the vehicle's frame), so the shots
        /// leave the muzzle the others see in the vehicle. A tracked headset hand keeps the gun (OwnGunPose is false then).
        /// </summary>
        private void SeatGun(HaloGun g)
        {
            Vector3 sp;
            Quaternion sq;
            Transform m = g.ModelRoot;
            if (HaloLocal.Vehicle == null || m == null || !HaloSeatPoses.OwnGunPose(out sp, out sq)) return;
            Quaternion d = sq * Quaternion.Inverse(m.rotation);
            Transform t = g.transform;
            t.SetPositionAndRotation(sp + d * (t.position - m.position), d * t.rotation);
            if (!_saidSeatGun) { _saidSeatGun = true; HaloLog.Info("Combat: seated with a weapon on a monitor - the gun is held in the seat animation's hands (Vehicles' seat pose), not at the view."); }
        }

        private Quaternion _twoHandRot = Quaternion.identity, _cupRot = Quaternion.identity;
        private float _cupBlend;
    }
}

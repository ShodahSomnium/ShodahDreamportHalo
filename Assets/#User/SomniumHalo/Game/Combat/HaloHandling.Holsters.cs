using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Where things are carried (ours: Halo carries the second weapon on the back of the cyborg; the VR places are the user's rule):
    /// long guns (assault rifle, sniper rifle, shotgun, rocket launcher, flamethrower, fuel rod gun) on the BACK, diagonally over the right
    /// shoulder - a second long gun over the left one; small guns (pistol, plasma pistol, needler, plasma rifle) on the WAIST, right hip -
    /// a second small gun on the left hip; frag grenades on the LEFT chest, plasma grenades on the RIGHT chest (up to the game's 4 each,
    /// one model per grenade); the ammunition pouch (magazines, shells, rocket pairs) at the front of the belt, left of the buckle. Left-
    /// handed (Settings) mirrors everything.
    /// </summary>
    public sealed partial class HaloHandling
    {
        public enum Zone { BackRight, BackLeft, HipRight, HipLeft, Frags, Plasma, Pouch }

        /// <summary>A hand this close to a carrying place can take from it (metres at a 1.7 m avatar; ours).</summary>
        public static float DrawRadius = 0.2f;
        /// <summary>A gun let go this close to a free holster of its kind goes into it (ours).</summary>
        public static float HolsterReturnRadius = 0.3f;

        private readonly List<GameObject> _fragVis = new List<GameObject>(), _plasmaVis = new List<GameObject>();
        private GameObject _pouchVis;
        private HaloTag _pouchFor;

        private bool LeftHanded => HaloSettings.LeftHanded;

        /// <summary>The world position of a carrying place on the player's body.</summary>
        public Vector3 ZoneCenter(Zone z)
        {
            float k = Body.Scale;
            Vector3 F = Body.Forward, R = Body.Right * (LeftHanded ? -1f : 1f), U = Body.Up;
            switch (z)
            {
                case Zone.BackRight: return (LeftHanded ? Body.ShoulderL : Body.ShoulderR) + U * (0.07f * k) - F * (0.17f * k);
                case Zone.BackLeft: return (LeftHanded ? Body.ShoulderR : Body.ShoulderL) + U * (0.07f * k) - F * (0.17f * k);
                case Zone.HipRight: return Body.Hips + R * (0.23f * k) - U * (0.06f * k);
                case Zone.HipLeft: return Body.Hips - R * (0.23f * k) - U * (0.06f * k);
                case Zone.Frags: return Body.Chest - R * (0.14f * k) + F * (0.12f * k) + U * (0.02f * k);
                case Zone.Plasma: return Body.Chest + R * (0.14f * k) + F * (0.12f * k) + U * (0.02f * k);
                default: return Body.Hips + F * (0.16f * k) - R * (0.09f * k) + U * (0.02f * k);
            }
        }

        public float ZoneRadius(Zone z)
        {
            switch (z)
            {
                case Zone.BackRight: case Zone.BackLeft: return DrawRadius * 1.2f * Body.Scale;
                case Zone.Frags: case Zone.Plasma: return DrawRadius * 0.7f * Body.Scale;
                case Zone.Pouch: return DrawRadius * 0.8f * Body.Scale;
                default: return DrawRadius * Body.Scale;
            }
        }

        public static Zone ZoneOf(HaloGun.Holster h)
        {
            switch (h)
            {
                case HaloGun.Holster.BackLeft: return Zone.BackLeft;
                case HaloGun.Holster.HipRight: return Zone.HipRight;
                case HaloGun.Holster.HipLeft: return Zone.HipLeft;
                default: return Zone.BackRight;
            }
        }

        // ------------------------------------------------------------------ the back: a generous reach over the shoulder / behind the upper back

        /// <summary>
        /// The back holsters' reach (ours, metres at a 1.7 m avatar; the user: "make it easier to grab behind the back"): a box in the head's
        /// frame turned with the body's yaw - from 0.20 above the eye down to 0.60 below it (mid back), out to 0.45 to either side, behind
        /// the head: at shoulder height and above the hand must be behind the eye's plane by 0.02 (a hand over the shoulder, beside the ear),
        /// lower down behind the back's plane (0.12 behind the eye: never the chest or the face in front). The right holster takes x &gt; -0.10,
        /// the left x &lt; 0.10; with one long gun on the back the whole box reaches it.
        /// </summary>
        public static float BackUp = 0.20f, BackDown = 0.60f, BackSide = 0.45f, BackBehindHigh = 0.02f, BackBehindLow = 0.12f, BackSplit = 0.10f;
        private bool _backLogged;

        /// <summary>The hand in the back box: its depth behind the eye (bigger = deeper), -1 when outside. <paramref name="side"/> 1 right, -1 left, 0 either.</summary>
        public float BackReach(Vector3 hp, int side)
        {
            float k = Body.Scale;
            Vector3 d = hp - Body.HeadPos;
            Vector3 R = Body.Right * (LeftHanded ? -1f : 1f);
            float x = Vector3.Dot(d, R) / k, y = Vector3.Dot(d, Body.Up) / k, z = Vector3.Dot(d, Body.Forward) / k;
            if (y > BackUp || y < -BackDown || Mathf.Abs(x) > BackSide) return -1f;
            float behind = y > -0.12f ? BackBehindHigh : BackBehindLow;
            if (z > -behind) return -1f;
            if (side > 0 && x < -BackSplit) return -1f;
            if (side < 0 && x > BackSplit) return -1f;
            return -z;
        }

        /// <summary>A score for a hand taking carried gun <paramref name="i"/> (smaller = nearer); float.MaxValue when out of reach.</summary>
        private float GunReach(int i, Vector3 hp)
        {
            HaloGun g = Guns[i];
            if (g == null) return float.MaxValue;
            if (g.Slot == HaloGun.Holster.BackRight || g.Slot == HaloGun.Holster.BackLeft)
            {
                if (!_backLogged)
                {
                    _backLogged = true;
                    HaloLog.Info("Combat: back holster reach: from " + BackUp + " m above the eye to " + BackDown + " m below, " + BackSide + " m to each side, behind the eye by " + BackBehindHigh + " m (shoulders) / " + BackBehindLow + " m (back); split at " + BackSplit + " m");
                }

                HaloGun other = Guns[1 - i];
                bool alone = other == null || other.Slot == HaloGun.Holster.HipRight || other.Slot == HaloGun.Holster.HipLeft || other.Slot == HaloGun.Holster.None || IndexOf(other) == _held;
                int side = alone ? 0 : (g.Slot == HaloGun.Holster.BackRight ? 1 : -1);
                float depth = BackReach(hp, side);
                if (depth >= 0f) return 0.05f + (hp - ZoneCenter(ZoneOf(g.Slot))).magnitude * 0.1f;     // (in the box: preferred over a nearby hip)
                float ds = (hp - ZoneCenter(ZoneOf(g.Slot))).magnitude;
                return ds <= ZoneRadius(ZoneOf(g.Slot)) ? ds : float.MaxValue;
            }

            Zone z = ZoneOf(g.Slot);
            float dz = (hp - ZoneCenter(z)).magnitude;
            return dz <= ZoneRadius(z) ? dz : float.MaxValue;
        }

        /// <summary>Everything built from the current map's tags that outlives a spawn (the chest grenades, the pouch and shell props): gone when
        /// the map goes - its materials are destroyed with it (they drew magenta in the next match).</summary>
        public void ForgetMapVisuals()
        {
            for (int i = 0; i < _fragVis.Count; i++) if (_fragVis[i] != null) Destroy(_fragVis[i]);
            for (int i = 0; i < _plasmaVis.Count; i++) if (_plasmaVis[i] != null) Destroy(_plasmaVis[i]);
            _fragVis.Clear();
            _plasmaVis.Clear();
            if (_pouchVis != null) Destroy(_pouchVis);
            _pouchVis = null;
            _pouchFor = null;
            if (_shellVis != null) Destroy(_shellVis);
            _shellVis = null;
            if (_grenadeVis != null) Destroy(_grenadeVis);
            _grenadeVis = null;
            _grenadeHand = -1;
        }

        /// <summary>The first free holster of the gun's kind (long: back right, back left; small: right hip, left hip).</summary>
        private HaloGun.Holster FreeHolster(HaloGun g, int index)
        {
            HaloGun other = Guns[1 - index];
            HaloGun.Holster first = g.Def.LongGun ? HaloGun.Holster.BackRight : HaloGun.Holster.HipRight;
            HaloGun.Holster second = g.Def.LongGun ? HaloGun.Holster.BackLeft : HaloGun.Holster.HipLeft;
            if (other != null && other != g && other.Slot == first) return second;
            return first;
        }

        /// <summary>How a gun rests in its holster: its grip at the place, the barrel's direction, which way the top faces.</summary>
        private void StowedPose(HaloGun.Holster h, out Vector3 pos, out Quaternion rot)
        {
            Vector3 F = Body.Forward, R = Body.Right * (LeftHanded ? -1f : 1f), U = Body.Up;
            pos = ZoneCenter(ZoneOf(h));
            switch (h)
            {
                case HaloGun.Holster.BackRight:
                    rot = Quaternion.LookRotation((U * 0.88f - R * 0.40f - F * 0.10f).normalized, -F);      // barrel up and to the left, the top away from the back
                    break;
                case HaloGun.Holster.BackLeft:
                    rot = Quaternion.LookRotation((U * 0.88f + R * 0.40f - F * 0.10f).normalized, -F);
                    break;
                default:
                    rot = Quaternion.LookRotation((F * 0.15f - U * 0.97f).normalized, F);                     // barrel down, the top of the gun forward
                    break;
            }
        }

        /// <summary>Everything not in a hand at its carrying place; every frame (the body moves).</summary>
        private void UpdateStowed()
        {
            for (int i = 0; i < 2; i++)
            {
                HaloGun g = Guns[i];
                if (g == null || i == _held) continue;
                Vector3 p;
                Quaternion q;
                StowedPose(g.Slot, out p, out q);
                g.transform.SetPositionAndRotation(p, q);
                g.gameObject.SetActive(!_desktop || HaloCombat.ShowStowedOnMonitor);
            }

            UpdateGrenadeRig();
            UpdatePouch();
        }

        // ------------------------------------------------------------------ grenades on the chest

        private void UpdateGrenadeRig()
        {
            bool show = !_desktop && HaloLocal.Alive;
            Rig(_fragVis, 0, show ? HaloLocal.FragGrenades - (_grenadeHand >= 0 && _grenadeKind == 0 ? 0 : 0) : 0, Zone.Frags);
            Rig(_plasmaVis, 1, show ? HaloLocal.PlasmaGrenades : 0, Zone.Plasma);
        }

        private void Rig(List<GameObject> vis, int kind, int count, Zone z)
        {
            while (vis.Count < count)
            {
                GameObject go = GrenadeModel(kind);
                if (go == null) break;
                vis.Add(go);
            }

            Vector3 c = ZoneCenter(z);
            Quaternion yaw = Quaternion.LookRotation(Body.Forward, Body.Up);
            float s = Body.AvatarScale * (1.7f / HaloGun.CyborgHeight) * HaloGun.SizeFactor;
            for (int i = 0; i < vis.Count; i++)
            {
                GameObject go = vis[i];
                if (go == null) continue;
                bool on = i < count;
                if (go.activeSelf != on) go.SetActive(on);
                if (!on) continue;
                // a row across the chest strap: 4 cm apart, inward to outward
                float side = (z == Zone.Frags ? -1f : 1f) * (LeftHanded ? -1f : 1f);
                Vector3 p = c + Body.Right * (side * (i - 1.5f) * 0.045f * Body.Scale) - Body.Up * (Mathf.Abs(i - 1.5f) * 0.012f * Body.Scale);
                go.transform.SetPositionAndRotation(p, yaw);
                go.transform.localScale = Vector3.one * s;
            }
        }

        /// <summary>The grenade's own model (the globals' grenade projectile model), built once per use.</summary>
        public GameObject GrenadeModel(int kind)
        {
            var cat = HaloCombat.Catalog;
            if (cat == null || kind < 0 || kind >= cat.Grenades.Count) return null;
            var pd = cat.Projectile(cat.Grenades[kind].Projectile);
            HaloTag model = pd != null ? pd.Model : null;
            if (model == null) { var eq = cat.Equipment(cat.Grenades[kind].Equipment); model = eq != null ? eq.Model : null; }
            if (model == null) return null;
            var holder = new GameObject(kind == 0 ? "Frag grenade" : "Plasma grenade");
            holder.transform.SetParent(_gear, false);
            var b = HaloModelBuilder.Build(model, holder.transform, new HaloModelBuilder.Options { CastShadows = false });
            if (b == null) { Destroy(holder); return null; }
            b.Root.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            return holder;
        }

        // ------------------------------------------------------------------ the pouch

        /// <summary>A magazine of the held (or last drawn) weapon shown in the belt pouch while there is a reserve to take.</summary>
        private void UpdatePouch()
        {
            HaloGun g = HeldGun ?? (Guns[_lastHeld] != null ? Guns[_lastHeld] : null);
            bool show = !_desktop && g != null && g.State.UsesAmmo && g.State.Reserve > 0 && HaloLocal.Alive;
            HaloTag want = show ? g.Def.Tag : null;
            if (want != _pouchFor)
            {
                if (_pouchVis != null) Destroy(_pouchVis);
                _pouchVis = want != null ? MagazineModel(g, "Pouch") : null;
                _pouchFor = want;
            }

            if (_pouchVis == null) return;
            _pouchVis.SetActive(show && _heldMag == null);
            _pouchVis.transform.SetPositionAndRotation(ZoneCenter(Zone.Pouch), Quaternion.LookRotation(Body.Up, -Body.Forward));
            _pouchVis.transform.localScale = Vector3.one * (Body.AvatarScale * (1.7f / HaloGun.CyborgHeight) * HaloGun.SizeFactor);
        }

        // ------------------------------------------------------------------ holster, drop

        private void Holster(HaloGun g)
        {
            if (g == null) return;
            int i = IndexOf(g);
            if (i == _held)
            {
                EndSupport();
                _held = -1;
                _gunHand = -1;
            }

            g.State.Step(0f, false, false, _shots);        // (the trigger lets go)
            HaloLocal.Zoomed = false; HaloLocal.ZoomLevel = 0; HaloLocal.ZoomMagnification = 1f;
            if (g.Def.PickupSound != null) HaloAudio.Play(g.Def.PickupSound, g.transform, 0.5f);
        }

        /// <summary>A gun let go away from any holster falls to the ground as a pickup anyone can take (Halo: it stays, then goes).</summary>
        private void DropGun(HaloGun g, Vector3 velocity)
        {
            if (g == null) return;
            HaloLog.Info("Combat: " + g.Def.Name + " dropped (" + g.State.Loaded + "/" + g.State.Reserve + ")");
            Release(g);
            HaloPickups.DropWeapon(g, velocity, true);
        }

        /// <summary>A gun let go near a free holster of its kind (its own, or the other one of the same kind) goes in.</summary>
        private bool TryHolsterNear(HaloGun g)
        {
            Vector3 p = g.transform.position;
            float best = HolsterReturnRadius * Body.Scale;
            HaloGun.Holster pick = HaloGun.Holster.None;
            HaloGun.Holster[] kinds = g.Def.LongGun ? LongSlots : SmallSlots;
            int me = IndexOf(g);
            HaloGun other = me >= 0 ? Guns[1 - me] : null;
            for (int k = 0; k < kinds.Length; k++)
            {
                if (other != null && other.Slot == kinds[k]) continue;
                // the grip or the middle of the gun near the place counts (a long gun is reached over the shoulder by its grip)
                float d = Mathf.Min((p - ZoneCenter(ZoneOf(kinds[k]))).magnitude, (g.transform.TransformPoint(g.LocalBounds.center) - ZoneCenter(ZoneOf(kinds[k]))).magnitude);
                if (d < best) { best = d; pick = kinds[k]; }
            }

            // a long gun let go behind the back (the same generous box the draw uses) goes onto the free shoulder on that side, else the other one
            if (pick == HaloGun.Holster.None && g.Def.LongGun && _gunHand >= 0 && BackReach(HandPos(_gunHand), 0) >= 0f)
            {
                bool rightSide = Vector3.Dot(HandPos(_gunHand) - Body.HeadPos, Body.Right * (LeftHanded ? -1f : 1f)) >= 0f;
                HaloGun.Holster want = rightSide ? HaloGun.Holster.BackRight : HaloGun.Holster.BackLeft, alt = rightSide ? HaloGun.Holster.BackLeft : HaloGun.Holster.BackRight;
                pick = other != null && other.Slot == want ? alt : want;
            }

            if (pick == HaloGun.Holster.None) return false;
            g.Slot = pick;
            Holster(g);
            return true;
        }

        private static readonly HaloGun.Holster[] LongSlots = { HaloGun.Holster.BackRight, HaloGun.Holster.BackLeft };
        private static readonly HaloGun.Holster[] SmallSlots = { HaloGun.Holster.HipRight, HaloGun.Holster.HipLeft };
    }
}

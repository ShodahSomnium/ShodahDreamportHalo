using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// One player's avatar posed by Halo's own third-person animations (BO2's Bo2DeskPuppet): a hidden cyborg puppet (HaloPuppet) plays the player's
    /// state, the avatar's humanoid bones are posed onto its sample (HaloPuppetPoser) after the client animated them, and the held weapon goes where
    /// Halo attaches it (the posed right hand's "right hand" marker; the left hand on the weapon's "left hand cyborg"). Seated: the seat's own animation
    /// on the seat marker. HaloDeskBody calls it for the own avatar and every remote one; the lab calls it directly. Player.log: one "Desk body: ..."
    /// line per player (what was found and used), or why not.
    /// </summary>
    public sealed class HaloDeskPlayer
    {
        /// <summary>Lab: the planar speed to animate (the stand-ins do not move); negative = measured from the avatar's root.</summary>
        public static float LabSpeed = -1f;
        private static bool _saidNoData;

        public readonly string Label;
        public HaloPuppet Puppet;
        public readonly HaloPuppetPoser Poser = new HaloPuppetPoser();
        public HaloAvatarCopies Copies;
        public float NextCopies;
        private float _standH = -1f, _retryAt;
        private bool _said, _saidBind, _saidSeat;
        private string _failed;
        private readonly Vector3[] _ik = new Vector3[2];

        public HaloDeskPlayer(string label) { Label = label; }

        /// <summary>The puppet exists (made on the first pose once the cyborg is loaded).</summary>
        private bool Ready(Animator anim)
        {
            if (anim == null) return false;
            if (Puppet == null)
            {
                if (Time.unscaledTime < _retryAt) return false;
                _retryAt = Time.unscaledTime + 2f;
                HaloCyborg c = HaloCyborg.Current;
                if (c == null)
                {
                    if (!_saidNoData) { _saidNoData = true; HaloLog.Info("Desk body: no cyborg animations loaded yet (avatars are not posed until they are)"); }
                    return false;
                }

                string why;
                Puppet = HaloPuppet.Create(c, out why);
                if (Puppet == null)
                {
                    if (_failed == null) { _failed = why; HaloLog.Warn("Desk body: " + Label + ": no Halo puppet (" + why + ")"); }
                    _retryAt = Time.unscaledTime + 30f;
                    return false;
                }
            }

            if (!Poser.Bound(anim))
            {
                string say;
                bool ok = Poser.Bind(anim, out say);
                if (!_saidBind || !ok)
                {
                    _saidBind = true;
                    HaloLog.Info("Desk body: " + Label + "'s avatar '" + anim.name + "' for the Halo puppet: " + say + (ok ? "" : " - not posed"));
                }

                if (!ok) return false;
                _said = false;
                _saidSeat = false;
            }

            return true;
        }

        /// <summary>
        /// On foot: steps the puppet with the player's state and poses the avatar; returns where the held weapon goes (its HaloModelBuilder root:
        /// origin + rotation, barrel +x) in modelPos / modelRot. torso = spine / head / hips may move (false = the own avatar in first person);
        /// gunScale = the held weapon's scale (its markers scale with it).
        /// </summary>
        public bool Pose(Animator anim, ref HaloPuppetInput input, Quaternion aim, Vector3 aimPoint, bool torso, float gunScale, float dt, out Vector3 modelPos, out Quaternion modelRot)
        {
            modelPos = Vector3.zero;
            modelRot = Quaternion.identity;
            if (!Ready(anim)) return false;
            Poser.Begin(dt);
            Vector3 vel = Poser.RootVelocity;
            vel.y = 0f;
            Vector3 af = aim * Vector3.forward;
            af.y = 0f;
            input.Speed = LabSpeed >= 0f ? LabSpeed : vel.magnitude;
            input.MoveAngle = vel.sqrMagnitude > 0.01f && af.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(af, vel, Vector3.up) : 0f;
            float hh = Poser.HeadHeight;
            if (hh > 0.3f) _standH = _standH < 0f ? hh : Mathf.Max(hh, _standH - dt * 0.02f);
            if (!input.Crouch) input.Crouch = _standH > 0f && hh > 0f && hh < _standH * 0.8f;      // (the client crouched the avatar)
            Puppet.Step(input, dt);
            bool hasIk = LeftIk(input.Weapon, gunScale, out _ik[0], out _ikRot);
            Vector3 gp;
            Quaternion gq;
            Poser.Apply(Puppet.Sample, dt, aim, aimPoint, torso, input.Crouch, Puppet.ArmLength, hasIk, _ik[0], _ikRot, out gp, out gq);
            modelPos = gp;
            modelRot = gq * HaloCyborg.ModelFromGun;
            if (!_said && Puppet.Sample.Valid)
            {
                _said = true;
                HaloLog.Info("Desk body: " + Label + " is posed by the Halo cyborg (arm " + Puppet.ArmLength.ToString("0.00") + " m vs the avatar's " + Poser.ArmLength.ToString("0.00") +
                             " m, legs " + Poser.HasLegs + ", fingers " + Poser.HasFingers + "): " + (input.Weapon != null ? input.Weapon.Name : "unarmed") + " as '" + Puppet.ClassName + "', playing '" + Puppet.BaseName + "'" +
                             (hasIk ? ", left hand on the weapon's grip marker" : ", no left-hand grip marker") + (torso ? "" : " (first person: spine, head and hips stay the client's)"));
            }

            return true;
        }

        private Quaternion _ikRot = Quaternion.identity;

        /// <summary>
        /// Seated in a vehicle: the seat's animation on the seat marker. arms = pose the arms (a monitor player); vehicle = to find the seat's IK
        /// markers (Halo's "right hand wheel grip" ...) under it. Returns the weapon's model pose when the seat holds one (passenger).
        /// </summary>
        public bool PoseSeated(Animator anim, ref HaloPuppetInput input, Transform seat, Transform vehicle, bool hipsToo, bool arms, float dt, out Vector3 modelPos, out Quaternion modelRot)
        {
            modelPos = Vector3.zero;
            modelRot = Quaternion.identity;
            if (seat == null || !Ready(anim)) return false;
            Poser.Begin(dt);
            input.Speed = 0f;
            input.MoveAngle = 0f;
            Puppet.Step(input, dt);
            bool lv = false, rv = false;
            Vector3 lw = Vector3.zero, rw = Vector3.zero;
            if (arms && vehicle != null) SeatIk(input.Seat, vehicle, out lv, out lw, out rv, out rw);
            Vector3 gp;
            Quaternion gq;
            Poser.ApplySeated(Puppet.Sample, dt, seat, Puppet.LegLength, Puppet.ArmLength, hipsToo, arms, lv, lw, rv, rw, out gp, out gq);
            modelPos = gp;
            modelRot = gq * HaloCyborg.ModelFromGun;
            if (!_saidSeat && Puppet.Sample.Valid)
            {
                _saidSeat = true;
                HaloLog.Info("Desk body: " + Label + " sits as the cyborg in '" + input.Seat + "' ('" + Puppet.ClassName + "', playing '" + Puppet.BaseName + "'), hips " + hipsToo + ", arms " + arms +
                             (lv || rv ? ", hands on the vehicle's grips" : ""));
            }

            return Puppet.Sample.Valid;
        }

        private Transform _deathFrame;
        private bool _saidDeath;

        /// <summary>
        /// Dead: the game's death animation ("s-kill front gut", "h-kill back gut" ...) on the whole avatar, from the spot it died on (the avatar's
        /// root then, facing its yaw; the animation's root motion slides it), held lying until <see cref="Pose"/> runs again (respawn).
        /// </summary>
        public bool PoseDeath(Animator anim, bool hard, int dir, int region, float dt)
        {
            if (!Ready(anim)) return false;
            if (_deathFrame == null)
            {
                var go = new GameObject("Desk death frame (" + Label + ")");
                go.hideFlags = HideFlags.HideAndDontSave;
                _deathFrame = go.transform;
            }

            bool starting = !Puppet.Dying;
            Poser.Begin(dt);
            if (!Puppet.StepDeath(hard, dir, region, dt)) return false;
            Transform root = anim.transform;
            if (starting)
            {
                Vector3 f = root.forward;
                f.y = 0f;
                if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
                _deathStart = root.position;
                _deathRot = Quaternion.LookRotation(Vector3.Cross(f.normalized, Vector3.up), Vector3.up);      // (the seat frame: x forward, z left)
            }

            _deathFrame.SetPositionAndRotation(_deathStart + _deathRot * Puppet.DeathSlide * (Poser.LegLength > 0f && Puppet.LegLength > 0.1f ? Poser.LegLength / Puppet.LegLength : 0.85f), _deathRot);
            Vector3 gp;
            Quaternion gq;
            Poser.ApplySeated(Puppet.Sample, dt, _deathFrame, Puppet.LegLength, Puppet.ArmLength, true, true, false, Vector3.zero, false, Vector3.zero, out gp, out gq);
            if (!_saidDeath)
            {
                _saidDeath = true;
                HaloLog.Info("Desk body: " + Label + " died: '" + Puppet.DeathName + "' on the avatar (held lying until the respawn)");
            }

            return true;
        }

        private Vector3 _deathStart;
        private Quaternion _deathRot = Quaternion.identity;

        /// <summary>
        /// Where Halo IKs the left hand on this weapon, in the gun frame (+z barrel, +y up; metres): the cyborg's "left hand" marker laid onto the
        /// weapon's "left hand cyborg" marker; the wrist then sits back along the hand by the marker's offset (scaled with the avatar's hand).
        /// </summary>
        private bool LeftIk(HaloTag weapon, float gunScale, out Vector3 pos, out Quaternion grip)
        {
            pos = Vector3.zero;
            grip = Quaternion.identity;
            HaloCyborg c = Puppet != null ? Puppet.Cyborg : null;
            if (c == null || weapon == null || !c.HasLeftMarker) return false;
            HaloCyborg.WeaponGrip g = c.Grip(weapon);
            if (g == null || !g.HasLeft) return false;
            HaloSkeleton.Marker m = c.LeftMarker;
            float k = Puppet.ArmLength > 0.1f ? Mathf.Clamp(Poser.ArmLength / Puppet.ArmLength, 0.4f, 1.6f) : 0.8f;
            // Halo axes, standard quaternions: hand = weapon marker * marker^-1
            Quaternion mq = new Quaternion(m.Qx, m.Qy, m.Qz, m.Qw);
            Quaternion hq = g.LeftRot * Quaternion.Inverse(mq);
            Vector3 mp = hq * new Vector3(m.Tx, m.Ty, m.Tz);
            Vector3 wrist = HaloCyborg.P(g.LeftPos.x, g.LeftPos.y, g.LeftPos.z) * gunScale - HaloCyborg.P(mp.x, mp.y, mp.z) * k;
            Vector3 hz = hq * Vector3.forward, hx = hq * Vector3.right;      // (Halo axes: the hand's z = little -> index, x = wrist -> knuckles)
            Vector3 f = HaloCyborg.D(hz.x, hz.y, hz.z), up = -HaloCyborg.D(hx.x, hx.y, hx.z);
            pos = wrist;
            grip = Quaternion.LookRotation(f, up);
            return true;
        }

        /// <summary>The seat's IK points on the vehicle (Halo: the cyborg's hand markers onto e.g. "right hand wheel grip"): world wrist targets.</summary>
        private void SeatIk(string seatLabel, Transform vehicle, out bool lv, out Vector3 lw, out bool rv, out Vector3 rw)
        {
            lv = rv = false;
            lw = rw = Vector3.zero;
            HaloCyborg c = Puppet.Cyborg;
            HaloAnimGraph.Seat s = c.Graph.FindSeat(seatLabel);
            if (s == null) return;
            for (int i = 0; i < s.Ik.Count; i++)
            {
                HaloAnimGraph.IkPoint p = s.Ik[i];
                bool right = p.Marker.IndexOf("right hand", StringComparison.OrdinalIgnoreCase) >= 0;
                bool left = !right && p.Marker.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!right && !left) continue;
                Transform t = FindMarker(vehicle, p.AttachTo);
                if (t == null) continue;
                // the wrist sits back from the hand marker along the hand (the marker's offset, at the avatar's size)
                HaloSkeleton.Marker m = right ? c.RightMarker : c.LeftMarker;
                float k = Puppet.ArmLength > 0.1f ? Mathf.Clamp(Poser.ArmLength / Puppet.ArmLength, 0.4f, 1.6f) : 0.8f;
                Quaternion hand = t.rotation * Quaternion.Inverse(HaloSpace.Rot(m.Qx, m.Qy, m.Qz, m.Qw));      // (marker laid on the grip: the hand's frame)
                Vector3 off = hand * HaloSpace.Pos(m.Tx, m.Ty, m.Tz) * k;
                Vector3 w = t.position - off;
                if (right) { rv = true; rw = w; } else { lv = true; lw = w; }
            }
        }

        /// <summary>A "marker:name" child HaloModelBuilder made, anywhere under root (null when none).</summary>
        public static Transform FindMarker(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            return Find(root, "marker:" + name);
        }

        private static Transform Find(Transform t, string n)
        {
            if (string.Equals(t.name, n, StringComparison.OrdinalIgnoreCase)) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = Find(t.GetChild(i), n);
                if (r != null) return r;
            }

            return null;
        }

        /// <summary>Gives the avatar's bones back (the player stopped being posed).</summary>
        public void Release()
        {
            try { Poser.Release(); }
            catch (Exception) { /* the avatar went away */ }
        }

        /// <summary>The avatar's other copies (mirror / teleport / shadow) get the same pose; bound once per avatar, looked for again every 5 s while none.</summary>
        public void CopyPose(ref int logs)
        {
            Animator a = Poser.Anim;
            if (a == null || !Poser.Posed) return;
            if (Copies == null || Copies.Main != a || (Copies.Copies == 0 && Time.unscaledTime >= NextCopies))
            {
                bool first = Copies == null || Copies.Main != a;
                NextCopies = Time.unscaledTime + 5f;
                Copies = HaloAvatarCopies.Bind(a);
                if ((first || Copies.Copies > 0) && logs < 30)
                {
                    logs++;
                    HaloLog.Info("Desk body: " + Label + "'s avatar: " + Copies.Copies + " other copies (mirror / teleport / shadow) get the same pose");
                }
            }

            Copies.Copy();
        }
    }
}

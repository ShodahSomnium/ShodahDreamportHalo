using System;
using System.Collections.Generic;
using SomniumSpace.Bridge.Player;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The cyborg's vehicle seat animations on a seat (the Desk module's puppet idea, for seats only): Halo's own clips from the
    /// cyborg's model_animations unit seat block of the seat label ("W-driver", "W-gunner", "W-passenger", "G-driver", "B-driver",
    /// "scorpion-driver", "scorpionLF", "GT-gunner"): the seat's ENTER [7] and EXIT [8] animations, the seat class's IDLE [0] with its
    /// AIM-STILL overlay [25] (the warthog driver's head / body into the steering, the warthog gunner's aim on the chain gun, the passenger's
    /// aim) laid out on the class's yaw / pitch grid (HaloPose.Aim), and the seat's acceleration overlays [2] [3] (front-back, left-right:
    /// the lurch into turns and braking). Sampled on the CPU (HaloPose, no GameObject) into the Desk module's HaloPuppetSample seat joints,
    /// in the seat marker's frame - what HaloPuppetPoser.ApplySeated lays on an avatar. Unity-side, allocation-free per frame.
    /// </summary>
    public sealed class HaloSeatPuppet
    {
        public enum Phase { Enter, Sit, Exit }

        private const int JPelvis = 0, JChest = 1, JNeck = 2, JHead = 3, JLSh = 4, JRSh = 5, JLEl = 6, JREl = 7, JLWr = 8, JRWr = 9,
            JLHip = 10, JRHip = 11, JLKnee = 12, JRKnee = 13, JLAnk = 14, JRAnk = 15, Count = 16;

        public readonly HaloCyborg Cyborg;
        private readonly HaloPose _pose;
        private readonly Vector3[] _j = new Vector3[Count];
        public HaloPuppetSample Sample;
        public float LegLength { get; private set; }
        public float ArmLength { get; private set; }
        public string Playing { get; private set; }

        private string _seatLabel;
        private HaloAnimGraph.Seat _seat;
        private HaloAnimGraph.WeaponClass _class;
        private HaloTag _weapon;

        public HaloSeatPuppet(HaloCyborg c)
        {
            Cyborg = c;
            _pose = new HaloPose(c.Skel);
            _pose.Reset();
            _pose.ToModel();
            Read();
            var b = _j;
            LegLength = 0.5f * ((b[JLKnee] - b[JLHip]).magnitude + (b[JLAnk] - b[JLKnee]).magnitude + (b[JRKnee] - b[JRHip]).magnitude + (b[JRAnk] - b[JRKnee]).magnitude);
            ArmLength = 0.5f * ((b[JLEl] - b[JLSh]).magnitude + (b[JLWr] - b[JLEl]).magnitude + (b[JREl] - b[JRSh]).magnitude + (b[JRWr] - b[JREl]).magnitude);
        }

        /// <summary>The seat block and class (the held weapon's class in a seat that allows weapons; "unarmed" / "fixed" otherwise).</summary>
        private bool Bind(string seatLabel, HaloTag weapon)
        {
            if (seatLabel == _seatLabel && weapon == _weapon && _seat != null) return true;
            _seatLabel = seatLabel; _weapon = weapon;
            _seat = Cyborg.Graph.FindSeat(seatLabel);
            _class = null;
            if (_seat == null) return false;
            if (weapon != null)
            {
                HaloCyborg.WeaponGrip g = Cyborg.Grip(weapon);
                HaloAnimGraph.WeaponType type;
                _class = Cyborg.Graph.ClassFor(_seat, g != null ? g.Label : null, out type);
            }
            if (_class == null) _class = _seat.Weapon("unarmed") ?? _seat.Weapon("fixed") ?? _seat.Weapon("rifle");
            if (_class == null || _class.Anim(HaloAnimIds.Weapon.Idle) < 0)
                for (int k = 0; k < _seat.Weapons.Count; k++) if (_seat.Weapons[k].Anim(HaloAnimIds.Weapon.Idle) >= 0) { _class = _seat.Weapons[k]; break; }
            return true;
        }

        /// <summary>The seat's enter / exit clip length in seconds (0 = the seat has none).</summary>
        public float Length(string seatLabel, Phase phase)
        {
            if (!Bind(seatLabel, _weapon)) return 0f;
            HaloAnimation a = Clip(phase);
            return a != null && a.Decode() ? a.Duration : 0f;
        }

        private HaloAnimation Clip(Phase phase)
        {
            if (_seat == null) return null;
            if (phase == Phase.Enter) return Cyborg.Graph.Get(_seat.Anim(HaloAnimIds.Seat.Enter));
            if (phase == Phase.Exit) return Cyborg.Graph.Get(_seat.Anim(HaloAnimIds.Seat.Exit));
            return _class != null ? Cyborg.Graph.Get(_class.Anim(HaloAnimIds.Weapon.Idle)) : null;
        }

        /// <summary>
        /// One frame: phase clip at time t (enter / exit held on their last frame, idle looping), then (sitting) the aim-still overlay at
        /// aimYaw / aimPitch (radians, + = left / up, relative to the seat) and the acceleration overlays (accForward / accLeft in -1..1).
        /// </summary>
        public bool Step(string seatLabel, HaloTag weapon, Phase phase, float t, float aimYaw, float aimPitch, float accForward, float accLeft)
        {
            Sample.Valid = false;
            if (!Bind(seatLabel, weapon)) return false;
            HaloAnimation a = Clip(phase);
            if (a == null && phase != Phase.Sit) { a = Clip(Phase.Sit); phase = Phase.Sit; }
            if (a == null || !a.Decode()) return false;
            Playing = a.Name;
            _pose.Reset();
            _pose.Apply(a, Cyborg.NodeMap, phase == Phase.Sit ? t : Mathf.Min(t, Mathf.Max(0f, a.Duration - 0.001f)), phase == Phase.Sit, 1f);
            if (phase == Phase.Sit)
            {
                HaloAnimation aim = _class != null ? Cyborg.Graph.Get(_class.Anim(HaloAnimIds.Weapon.AimStill)) : null;
                if (aim != null)
                    _pose.Aim(aim, Cyborg.NodeMap, aimYaw, aimPitch, _class.RightYawPerFrame, _class.LeftYawPerFrame, _class.RightFrames, _class.LeftFrames,
                              _class.DownPitchPerFrame, _class.UpPitchPerFrame, _class.DownFrames, _class.UpFrames, 1f);
                Acc(HaloAnimIds.Seat.AccFrontBack, accForward);
                Acc(HaloAnimIds.Seat.AccLeftRight, accLeft);
            }
            _pose.ToModel();
            Read();
            Fill();
            return true;
        }

        /// <summary>A 3-frame acceleration overlay: frame 1 is rest, 0 / 2 the two extremes.</summary>
        private void Acc(int id, float v)
        {
            if (Mathf.Abs(v) < 0.02f || _seat == null) return;
            HaloAnimation a = Cyborg.Graph.Get(_seat.Anim(id));
            if (a == null || !a.Decode() || a.Frames < 3) return;
            float u = Mathf.Clamp(v, -1f, 1f);
            if (u >= 0f) _pose.Overlay(a, Cyborg.NodeMap, 1, 2, u, 1f);
            else _pose.Overlay(a, Cyborg.NodeMap, 1, 0, -u, 1f);
        }

        private void Read()
        {
            HaloCyborg c = Cyborg;
            _j[JPelvis] = Pt(c.Pelvis); _j[JChest] = Pt(c.Spine1); _j[JNeck] = Pt(c.Neck); _j[JHead] = Pt(c.Head);
            _j[JLSh] = Pt(c.LUpper); _j[JRSh] = Pt(c.RUpper); _j[JLEl] = Pt(c.LFore); _j[JREl] = Pt(c.RFore);
            _j[JLWr] = Pt(c.LHand); _j[JRWr] = Pt(c.RHand); _j[JLHip] = Pt(c.LThigh); _j[JRHip] = Pt(c.RThigh);
            _j[JLKnee] = Pt(c.LCalf); _j[JRKnee] = Pt(c.RCalf); _j[JLAnk] = Pt(c.LFoot); _j[JRAnk] = Pt(c.RFoot);
        }

        private Vector3 Pt(int node)
        {
            if (node < 0) return Vector3.zero;
            float[] t = _pose.MTrans;
            return HaloCyborg.P(t[node * 3], t[node * 3 + 1], t[node * 3 + 2]);
        }

        private Quaternion HandGrip(int node)
        {
            float zx, zy, zz, xx, xy, xz;
            _pose.Axis(node, 0f, 0f, 1f, out zx, out zy, out zz);
            _pose.Axis(node, 1f, 0f, 0f, out xx, out xy, out xz);
            return Quaternion.LookRotation(HaloCyborg.D(zx, zy, zz), -HaloCyborg.D(xx, xy, xz));
        }

        /// <summary>The seat joints in the seat marker's frame (Desk's HaloPuppet.FillSeat, the same maths).</summary>
        private void Fill()
        {
            Vector3[] j = _j;
            Sample.Seated = true;
            Sample.SPelvis = S(j[JPelvis]); Sample.SChest = S(j[JChest]); Sample.SNeck = S(j[JNeck]); Sample.SHead = S(j[JHead]);
            Sample.SLSh = S(j[JLSh]); Sample.SRSh = S(j[JRSh]); Sample.SLEl = S(j[JLEl]); Sample.SREl = S(j[JREl]);
            Sample.SLWr = S(j[JLWr]); Sample.SRWr = S(j[JRWr]); Sample.SLHip = S(j[JLHip]); Sample.SRHip = S(j[JRHip]);
            Sample.SLKnee = S(j[JLKnee]); Sample.SRKnee = S(j[JRKnee]); Sample.SLAnk = S(j[JLAnk]); Sample.SRAnk = S(j[JRAnk]);
            Vector3 up = Sample.SChest - Sample.SPelvis;
            Vector3 across = Sample.SRHip - Sample.SLHip;
            Vector3 fwd = Vector3.Cross(across, up);
            Sample.SPelvisRot = up.sqrMagnitude < 1e-6f || fwd.sqrMagnitude < 1e-8f ? Quaternion.LookRotation(Vector3.right, Vector3.up) : Quaternion.LookRotation(fwd.normalized, up.normalized);
            Sample.SLGrip = SeatRot(HandGrip(Cyborg.LHand));
            Sample.SRGrip = SeatRot(HandGrip(Cyborg.RHand));
            // the weapon in the right hand: the cyborg's "right hand" marker is where Halo attaches the held weapon's origin (Desk's HaloPuppet.FillGun)
            if (Cyborg.HasRightMarker)
            {
                float px, py, pz, fx, fy, fz, ux, uy, uz;
                _pose.MarkerFrame(Cyborg.RightMarker, out px, out py, out pz, out fx, out fy, out fz, out ux, out uy, out uz);
                Quaternion gun = Quaternion.LookRotation(HaloCyborg.D(fx, fy, fz), HaloCyborg.D(ux, uy, uz));
                Sample.GunInRGrip = Quaternion.Inverse(HandGrip(Cyborg.RHand)) * gun;
                Sample.RWristOnGun = Quaternion.Inverse(gun) * (j[JRWr] - HaloCyborg.P(px, py, pz));
            }
            else
            {
                Sample.GunInRGrip = Quaternion.identity;
                Sample.RWristOnGun = Vector3.zero;
            }
            Sample.AimWeight = 1f;
            Sample.Valid = true;
        }

        private static Vector3 S(Vector3 p) { return new Vector3(p.z, p.y, -p.x); }
        private static Quaternion SeatRot(Quaternion q) { return Quaternion.LookRotation(S(q * Vector3.forward), S(q * Vector3.up)); }

        /// <summary>
        /// The seat's IK points on the vehicle (the cyborg's hand markers laid onto e.g. "right hand wheel grip", "gunner left hand"): world
        /// wrist targets at the avatar's arm scale k (Desk's HaloDeskPlayer.SeatIk, the same rule).
        /// </summary>
        public void SeatIk(string seatLabel, Transform vehicle, float k, out bool lv, out Vector3 lw, out bool rv, out Vector3 rw)
        {
            lv = rv = false; lw = rw = Vector3.zero;
            HaloAnimGraph.Seat s = Cyborg.Graph.FindSeat(seatLabel);
            if (s == null) return;
            for (int i = 0; i < s.Ik.Count; i++)
            {
                HaloAnimGraph.IkPoint p = s.Ik[i];
                bool right = p.Marker.IndexOf("right hand", StringComparison.OrdinalIgnoreCase) >= 0;
                bool left = !right && p.Marker.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!right && !left) continue;
                Transform t = HaloDeskPlayer.FindMarker(vehicle, p.AttachTo);
                if (t == null) continue;
                HaloSkeleton.Marker m = right ? Cyborg.RightMarker : Cyborg.LeftMarker;
                Quaternion hand = t.rotation * Quaternion.Inverse(HaloSpace.Rot(m.Qx, m.Qy, m.Qz, m.Qw));
                Vector3 w = t.position - hand * HaloSpace.Pos(m.Tx, m.Ty, m.Tz) * k;
                if (right) { rv = true; rw = w; } else { lv = true; lw = w; }
            }
        }
    }

    /// <summary>
    /// Seated avatars, posed with Halo's own seat animations (the user's request after the first client test: "put avatar in vehicle when
    /// entering, use same animations etc as real Halo"). For every player in a seat - the own avatar and every remote rider HaloVehicleRiders
    /// places - and for the moments around it: ENTER (the seat's enter clip from the enter marker into the seat), SIT (idle + aim-still +
    /// acceleration overlays: the warthog driver leaning into the steering with his hands on the wheel grips, the gunner's body on the turning
    /// mount with the gun's pitch, a passenger's aim) and EXIT (the seat's exit clip out of the vehicle, ending where getting out stands the player).
    /// The pose goes on the avatar's humanoid bones with the Desk module's HaloPuppetPoser.ApplySeated (hips on the seat, legs, torso, arms) after
    /// the client's Animator and IK and after Desk (order 9997), and onto the avatar's mirror / shadow copies (HaloAvatarCopies).
    ///  - monitor players: the whole body; hands on the vehicle's grip markers (Halo's seat IK) or the animation's hands;
    ///  - headset players, first-person seats: hips, legs and torso from the animation, the ARMS reach the tracked controllers (the real hands stay
    ///    theirs; the warthog gunner's hands are on the chain gun's grips because the player holds them there); the hands keep the controllers' turn;
    ///  - a headset driver in the client-style third-person view: the whole body in the seat with the hands on the wheel (the player's own view is
    ///    behind the vehicle; the controllers are not near the wheel).
    /// VR comfort: the own view never follows the enter / exit clip - it cuts to the seat on getting in and to the exit point on getting out (a
    /// teleport, as every Somnium move of the player is), heading only; only the body animates, for everyone else and in mirrors.
    /// While V poses a player, Desk must not (HaloDesk.PoseSeated = false, HaloDesk.PoseOwn = false while the own avatar is V's; remote players:
    /// <see cref="Owns"/>, asked by Desk).
    /// </summary>
    [DefaultExecutionOrder(9997)]
    public sealed class HaloSeatPoses : MonoBehaviour
    {
        public sealed class Rider
        {
            public int Key;                       // -1 own, else the remote's Fusion player id
            public HaloVehicle Vehicle;
            public int Seat;
            public string Label;
            public HaloSeatPuppet.Phase Phase;
            public float T, Length;
            public HaloPuppetPoser Poser;
            public HaloSeatPuppet Puppet;
            public Animator Anim;
            public HaloAvatarCopies Copies;
            public float NextSearch, NextCopies;
            public ISomniumPlayer Player;
            public bool Vr, Seen, Said, GunSaid;
            public HaloTag Weapon;
            /// <summary>The held weapon's model pose (origin = Halo's "right hand" marker, Halo +X along the barrel) in the seat animation's right hand,
            /// world now and in the vehicle's frame (Combat reads that in Update, before this frame's pose: it rides along with the vehicle).</summary>
            public bool GunValid;
            public Transform LeftOf, LeftMarker;
            public Transform ReachProxy;     // a tracked rider's seat frame moved toward the real hands (child of the seat marker)
            public Vector3 Lean;             // ... its offset in the marker's frame
            public bool ReachSaid;
            public bool View;      // the own monitor view is the seat's eye: the gun is Combat's view model
            public bool ViewGun;   // ... holding a gun: drawn in the seat hands for every camera but the own first-person one
            public int ViewGunFrame;
            public Vector3 EyePos;
            public readonly Quaternion[] ArmsOut = new Quaternion[6], ArmsView = new Quaternion[6];      // the seat clip's arms / the own first-person view's
            public bool ArmsValid;
            public float ViewHandToGun = -1f;      // the first-person arms: the right hand from the view model (lab)
            public float ViewLeftErr = -1f;        // ... the left hand from its target on the view model (lab)
            public bool ViewFramed;                // the own first-person view model framed like standing first person (Desk's framing):
            public Vector3 ViewLocalPos;           // ... the model origin in the camera's frame
            public Quaternion ViewLocalRot = Quaternion.identity;
            public float ViewScale = 1f;           // ... drawn at this scale on the own camera only (shrunk toward the eye so the hands reach it)
            public bool ViewBody;                  // ... the body drawn for the own camera like on foot (head at the eye, shoulders square to the look):
            public Vector3 ViewHipsPos;            // ... the hips' local pose then
            public Quaternion ViewHipsRot = Quaternion.identity;
            public Quaternion ViewLegL = Quaternion.identity, ViewLegR = Quaternion.identity;      // ... and the thighs' (kept in the seat clip's direction)
            public float ViewShift;                // ... how far the head moved (lab)
            public float AimYawNow, AimPitchNow;      // the aim-still overlay this frame (radians, seat frame), for the lab and the log
            public Vector3 GunPos, GunLocalPos;
            public Quaternion GunRot = Quaternion.identity, GunLocalRot = Quaternion.identity;
        }

        /// <summary>Lab: the weapon a stand-in rider holds (key -> weapon tag; null = from the net player).</summary>
        public static Func<int, HaloTag> LabWeapon;

        /// <summary>Lab: called after a rider's held weapon pose is known (key, model position, model rotation) - the lab draws a weapon there.</summary>
        public static Action<int, Vector3, Quaternion> LabGun;

        /// <summary>
        /// The own held weapon's model pose in the seat (the hands of the seat animation), when the own avatar sits in a seat that allows weapons and
        /// is posed by the whole animation (a monitor player; a headset player's gun stays in the tracked hand). Taken from the last pose in the
        /// vehicle's frame, so it is where the hands are now even before this frame's pose (Combat places and fires the gun in Update).
        /// </summary>
        public static bool OwnGunPose(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            Rider r;
            if (!Riders.TryGetValue(-1, out r) || !r.GunValid || r.Vehicle == null || r.Anim == null || r.Weapon == null || r.Phase != HaloSeatPuppet.Phase.Sit) return false;
            if (r.View || (r.Vr && !VrThird(r))) return false;
            Transform t = r.Vehicle.transform;
            pos = t.TransformPoint(r.GunLocalPos);
            rot = t.rotation * r.GunLocalRot;
            return true;
        }

        /// <summary>A remote rider's held weapon in a seat that allows it (Net's record: in hand); null otherwise or in the lab's stand-ins without one.</summary>
        private static HaloTag RemoteWeapon(int key, HaloVehicle v, int seat)
        {
            var s = v != null ? v.SeatOf(seat) : null;
            if (s == null || !s.Def.AllowsWeapons) return null;
            if (LabWeapon != null) return LabWeapon(key);
            var p = HaloNetGame.Players;
            for (int i = 0; i < p.Count; i++) if (p[i] != null && p[i].PlayerId == key) return p[i].InHand ? p[i].HeldWeapon : null;
            return null;
        }

        private static readonly Dictionary<int, Rider> Riders = new Dictionary<int, Rider>();
        private static readonly List<int> Gone = new List<int>();
        private static bool _deskHeld, _ownHeld, _poseSeatedWas = true;
        private static HaloCyborg _cyborg;
        private static bool _saidNoCyborg;

        /// <summary>The lab's stand-in avatars (key -> Animator; null = find the client's).</summary>
        public static Func<int, Animator> LabAvatar;

        /// <summary>V poses this player now (key -1 = own): Desk leaves them alone.</summary>
        public static bool Owns(int key) { Rider r; return Riders.TryGetValue(key, out r) && r.Anim != null; }

        /// <summary>The same by net slot (Desk's HaloDeskRemote.Slot).</summary>
        public static bool OwnsSlot(int slot)
        {
            if (slot < 0) return false;
            var p = HaloNetGame.Find(slot);
            return p != null && Owns(p.PlayerId);
        }

        /// <summary>The own avatar's current phase / clip, for the log and the lab.</summary>
        public static string Describe(int key) { Rider r; return Riders.TryGetValue(key, out r) ? r.Phase + " '" + (r.Puppet != null ? r.Puppet.Playing : "-") + "' t " + r.T.ToString("0.00") + "/" + r.Length.ToString("0.00") : "none"; }

        public static Rider Get(int key) { Rider r; return Riders.TryGetValue(key, out r) ? r : null; }

        private static HaloCyborg Cyb()
        {
            HaloCyborg c = HaloCyborg.Current;
            if (c == null && !_saidNoCyborg) { _saidNoCyborg = true; HaloLog.Info("Vehicles: no cyborg animations loaded yet - seated avatars are not posed until they are."); }
            if (c != _cyborg) { _cyborg = c; foreach (var kv in Riders) kv.Value.Puppet = null; }
            return c;
        }

        // ------------------------------------------------------------------ who sits where (Update, from the seat and the riders)

        private void Update()
        {
            try { Track(); }
            catch (Exception e) { HaloLog.Error("Vehicles: seat poses failed: " + e); }
        }

        private static void Track()
        {
            foreach (var kv in Riders) kv.Value.Seen = false;
            var me = HaloVehicleSeat.Local;
            if (me.Seated && me.Vehicle != null) Note(-1, me.Vehicle, me.SeatIndex, null, HaloVehicleSeat.Vr, HaloLocal.SeatAllowsWeapons && HaloLocal.InHand && HaloLocal.HeldWeapon != null ? HaloLocal.HeldWeapon.Tag : (LabWeapon != null && me.Seat.Def.AllowsWeapons ? LabWeapon(-1) : null));
            var list = HaloVehicleRiders.Current;
            for (int i = 0; i < list.Count; i++) Note(list[i].PlayerId, list[i].Vehicle, list[i].Seat, list[i].Player, list[i].Xr, RemoteWeapon(list[i].PlayerId, list[i].Vehicle, list[i].Seat));
            Gone.Clear();
            foreach (var kv in Riders)
            {
                var r = kv.Value;
                if (r.Seen) continue;
                // out of the seat: play the exit clip where the seat is (unless they died, or the vehicle went)
                bool dead = r.Key == -1 ? !HaloLocal.Alive : false;
                if (r.Phase != HaloSeatPuppet.Phase.Exit && !dead && r.Vehicle != null && r.Puppet != null)
                {
                    r.Phase = HaloSeatPuppet.Phase.Exit; r.T = 0f; r.Weapon = null; r.GunValid = false; r.Length = r.Puppet.Length(r.Label, HaloSeatPuppet.Phase.Exit);
                    if (r.Length > 0.05f) { HaloLog.Info("Vehicles: " + Who(r) + " plays '" + r.Label + " exit' (" + r.Length.ToString("0.00") + " s)."); continue; }
                }
                if (r.Phase == HaloSeatPuppet.Phase.Exit && r.T < r.Length && !dead && r.Vehicle != null) continue;
                Gone.Add(kv.Key);
            }
            for (int i = 0; i < Gone.Count; i++) { Drop(Riders[Gone[i]]); Riders.Remove(Gone[i]); }
            HoldDesk(Riders.ContainsKey(-1));
        }

        private static string Who(Rider r) { return r.Key == -1 ? "my avatar" : "player " + r.Key; }

        private static void Note(int key, HaloVehicle v, int seat, ISomniumPlayer player, bool vr, HaloTag weapon)
        {
            var s = v.SeatOf(seat);
            if (s == null) return;
            Rider r;
            if (!Riders.TryGetValue(key, out r) || r.Vehicle != v || r.Seat != seat || r.Phase == HaloSeatPuppet.Phase.Exit)
            {
                if (r != null) Drop(r);
                r = new Rider { Key = key, Vehicle = v, Seat = seat, Label = s.Label, Phase = HaloSeatPuppet.Phase.Enter, T = 0f, Poser = new HaloPuppetPoser() };
                Riders[key] = r;
            }
            r.Seen = true; r.Player = player; r.Vr = vr; r.Weapon = weapon;
        }

        /// <summary>Desk's switches while V poses: seated avatars are V's; the own avatar entirely while V poses it.</summary>
        private static void HoldDesk(bool own)
        {
            if (!_deskHeld)
            {
                _deskHeld = true;
                _poseSeatedWas = HaloDesk.PoseSeated;
                HaloDesk.PoseSeated = false;
            }
            if (own && !_ownHeld) { _ownHeld = true; HaloDesk.PoseOwn = false; }
            else if (!own && _ownHeld) { _ownHeld = false; HaloDesk.PoseOwn = true; }
        }

        /// <summary>Gives Desk its switches back (the match ended).</summary>
        public static void Clear()
        {
            foreach (var kv in Riders) Drop(kv.Value);
            Riders.Clear();
            if (_deskHeld) { HaloDesk.PoseSeated = _poseSeatedWas; _deskHeld = false; }
            if (_ownHeld) { HaloDesk.PoseOwn = true; _ownHeld = false; }
        }

        private static void Drop(Rider r)
        {
            if (r.ReachProxy != null) { try { UnityEngine.Object.Destroy(r.ReachProxy.gameObject); } catch (Exception) { } r.ReachProxy = null; }
            if (r.Key == -1) OwnHead(null, false);
            try { if (r.Poser != null) r.Poser.Release(); } catch (Exception) { }
            r.Anim = null;
        }

        // ------------------------------------------------------------------ the pose (LateUpdate 9997)

        private void LateUpdate()
        {
            try
            {
                float dt = Time.deltaTime;
                HaloCyborg c = Cyb();
                // (the own first-person seat body of the last frame given back before posing, should no camera have drawn since)
                SetViewArms(null, false);
                ViewHead(null);
                if (c == null) return;
                foreach (var kv in Riders) Pose(kv.Value, c, dt);
            }
            catch (Exception e) { HaloLog.Error("Vehicles: posing seated avatars failed: " + e); }
        }

        private static void Pose(Rider r, HaloCyborg c, float dt)
        {
            if (r.Vehicle == null) { RemoteGuns(r, false, null); return; }
            var seat = r.Vehicle.SeatOf(r.Seat);
            if (seat == null || seat.Marker == null) { RemoteGuns(r, false, null); return; }
            if (r.Puppet == null)
            {
                r.Puppet = new HaloSeatPuppet(c);
                if (r.Phase == HaloSeatPuppet.Phase.Enter) { r.Length = r.Puppet.Length(r.Label, HaloSeatPuppet.Phase.Enter); if (r.Length <= 0.05f) r.Phase = HaloSeatPuppet.Phase.Sit; }
            }
            r.T += dt;
            if (r.Phase == HaloSeatPuppet.Phase.Enter && r.T >= r.Length) { r.Phase = HaloSeatPuppet.Phase.Sit; r.T = 0f; }
            if (!FindAvatar(r)) { RemoteGuns(r, false, null); return; }

            // aim / steering / acceleration for the overlays
            float yaw = 0f, pitch = 0f, accF = 0f, accL = 0f;
            Overlays(r, seat, out yaw, out pitch, out accF, out accL);
            r.AimYawNow = yaw; r.AimPitchNow = pitch;
            bool tracked = r.Vr && !VrThird(r);
            bool view = OwnView(r, tracked);
            if (!r.Puppet.Step(r.Label, r.Weapon, r.Phase, r.T, yaw, pitch, accF, accL)) { RemoteGuns(r, false, null); return; }
            if (!r.Said)
            {
                r.Said = true;
                HaloLog.Info("Vehicles: " + Who(r) + " sits as the cyborg in '" + r.Label + "' (" + r.Phase + ", playing '" + r.Puppet.Playing + "'" +
                             (r.Vr ? ", headset: hips + legs + torso, arms to the " + (VrThird(r) ? "vehicle's grips (third-person view)" : "tracked controllers") : ", monitor: the whole body") + ").");
            }

            var poser = r.Poser;
            poser.Begin(dt);
            Transform lh = r.Anim.GetBoneTransform(HumanBodyBones.LeftHand), rh = r.Anim.GetBoneTransform(HumanBodyBones.RightHand);
            Quaternion lRot = lh != null ? lh.rotation : Quaternion.identity, rRot = rh != null ? rh.rotation : Quaternion.identity;
            float k = r.Puppet.ArmLength > 0.1f ? Mathf.Clamp(poser.ArmLength / r.Puppet.ArmLength, 0.4f, 1.6f) : 0.8f;
            bool lv = false, rv = false;
            Vector3 lw = Vector3.zero, rw = Vector3.zero;
            if (tracked)
            {
                // the real hands: the arms reach for the tracked controllers (Bridge hand anchors)
                Transform tl = HandAnchor(r, false), tr = HandAnchor(r, true);
                if (tl != null) { lv = true; lw = tl.position; }
                if (tr != null) { rv = true; rw = tr.position; }
            }
            else if (r.Phase == HaloSeatPuppet.Phase.Sit) r.Puppet.SeatIk(r.Label, r.Vehicle.transform, k, out lv, out lw, out rv, out rw);
            Vector3 gp; Quaternion gq;
            Transform seatT = tracked && seat.LeftGrip != null && !seat.Def.Driver ? Reach(r, seat.Marker, lv, lw, rv, rw, dt) : seat.Marker;
            poser.ApplySeated(r.Puppet.Sample, dt, seatT, r.Puppet.LegLength, r.Puppet.ArmLength, true, true, lv, lw, rv, rw, out gp, out gq);
            Gun(r, gp, gq, tracked, view);
            ViewArms(r, seat, k);
            if (tracked)
            {
                // the hands keep the controllers' turn (the animation's grip is for a monitor player)
                if (lh != null && lv) lh.rotation = lRot;
                if (rh != null && rv) rh.rotation = rRot;
            }
            if (r.Copies == null || r.Copies.Main != r.Anim || (r.Copies.Copies == 0 && Time.unscaledTime >= r.NextCopies))
            {
                r.NextCopies = Time.unscaledTime + 5f;
                r.Copies = HaloAvatarCopies.Bind(r.Anim);
            }
            r.Copies.Copy();
            if (r.Key == -1 && r.ViewGun && r.ArmsValid)
            {
                // the own first-person seat body (ViewArms) laid on now, after the mirror copies took the seat clip's pose, until every camera drew:
                // an avatar is skinned once a frame, so a camera drawing before the own one must not decide what the own view shows (Desk's
                // HaloHeadHide works the same way). Mirrors draw the copies; other players pose this avatar with the seat clip on their machines.
                SetViewArms(r, true);
                ViewHead(r.Anim);
            }
            if (r.Key == -1) OwnHead(r.Anim, r.Vr && VrThird(r));
        }

        /// <summary>
        /// The held weapon in the seat (Halo: a seat that allows weapons - the warthog passenger - holds the unit's weapon in the seat animation's
        /// hands, the "right hand" marker carrying the weapon's origin). Monitor players and every non-tracked avatar: the hand of the seat pose
        /// (gp / gq from ApplySeated); a tracked headset rider: the tracked hand (the gun points where the controller points). The own weapon is
        /// placed here (Combat's model root, after Combat's Update); a remote rider's weapons are Net's (HaloRemoteView.SeatedGuns), placed on the
        /// posed avatar - never where their record says their head is (a monitor rider's head is their chase camera, metres behind the vehicle).
        /// </summary>
        private static void Gun(Rider r, Vector3 gp, Quaternion gq, bool tracked, bool view)
        {
            r.GunValid = false;
            r.ViewGun = false;
            Vector3 pos = gp;
            Quaternion rot = gq * HaloCyborg.ModelFromGun;
            // the own gun only while sitting (the enter / exit clips start and end outside the vehicle, where the own view is not); a remote
            // rider's in every phase (their clip hand is where everyone sees their body)
            if (r.Weapon != null && (r.Key != -1 || r.Phase == HaloSeatPuppet.Phase.Sit))
            {
                if (tracked)
                {
                    Transform a = HandAnchor(r, true);
                    Transform hb = r.Anim.GetBoneTransform(HumanBodyBones.RightHand);
                    if (a != null)
                    {
                        Quaternion aim = a.rotation * HaloBody.GripToAim;      // (the Bridge hand anchor is the controller's grip pose)
                        pos = hb != null ? hb.position : a.position;
                        rot = aim * Quaternion.Euler(0f, -90f, 0f);      // (HaloRemoteView.PoseHeld: Halo's +X along the aim)
                        r.GunValid = true;
                    }
                }
                else r.GunValid = true;
            }
            if (r.GunValid)
            {
                Transform t = r.Vehicle.transform;
                r.GunPos = pos; r.GunRot = rot;
                r.GunLocalPos = t.InverseTransformPoint(pos);
                r.GunLocalRot = Quaternion.Inverse(t.rotation) * rot;
                if (!r.GunSaid)
                {
                    r.GunSaid = true;
                    HaloLog.Info("Vehicles: " + Who(r) + " holds " + r.Weapon.Name + " in '" + r.Label + "' (" + (tracked ? "the tracked hand" : "the seat animation's right hand, class '" + r.Puppet.Playing + "'") + "), " +
                                 (pos - r.Vehicle.SeatOf(r.Seat).Marker.position).magnitude.ToString("0.00") + " m from the seat marker.");
                }
                if (r.Key == -1 && !tracked && !view)
                {
                    Transform m = HaloDesk.HeldWeapon != null ? HaloDesk.HeldWeapon : HaloCombat.HeldModel;
                    if (m != null) m.SetPositionAndRotation(pos, rot);
                }
                if (r.Key == -1 && view)
                {
                    // first person from the seat's eye: Combat's view model for the own camera; every other camera (mirrors, a spectator
                    // camera) draws the gun in the seat animation's hands (OnBeginCamera)
                    r.ViewGun = true;
                    r.ViewGunFrame = Time.frameCount;
                    Transform hb = r.Anim.GetBoneTransform(HumanBodyBones.Head);
                    r.EyePos = hb != null ? hb.position : pos;
                    if (!_saidViewGun)
                    {
                        _saidViewGun = true;
                        HaloLog.Info("Vehicles: first person in '" + r.Label + "' with " + r.Weapon.Name + " - my view draws Combat's view model; seen from outside my avatar holds it in Halo's '" + r.Puppet.Playing + "' idle.");
                    }
                }
                if (LabGun != null) LabGun(r.Key, pos, rot);
            }
            RemoteGuns(r, r.GunValid, r.Anim);
        }

        /// <summary>A remote rider's guns (Net's HaloRemoteView.SeatedGuns): placed on the posed avatar, or hidden (avatar null) when the pose could not
        /// run this frame - never left where they were (a stale place outside the vehicle).</summary>
        private static void RemoteGuns(Rider r, bool valid, Animator avatar)
        {
            if (r.Key == -1) return;
            var p = HaloNetGame.Players;
            for (int i = 0; i < p.Count; i++)
            {
                if (p[i] == null || p[i].PlayerId != r.Key) continue;
                HaloRemoteView rv = HaloNetGame.ViewOf(p[i].Slot);
                if (rv != null) rv.SeatedGuns(valid && avatar != null, r.GunPos, r.GunRot, avatar);
                break;
            }
        }

        // ------------------------------------------------------------------ the own first-person seat gun, per camera

        private static Transform _vgT;
        private static bool _vgMoved, _saidViewGun;
        private static Vector3 _vgLocalPos;
        private static Quaternion _vgLocalRot;

        /// <summary>Lab: where the last camera other than the own first-person one drew the own seated gun (world), and the frame it did.</summary>
        public static Vector3 LabViewGunDrawn;
        public static int LabViewGunDrawnFrame = -1;
        /// <summary>Lab: the frame the own head was last shrunk for the own first-person seat camera.</summary>
        public static int LabViewHeadHiddenFrame = -1;

        private void OnEnable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            UnityEngine.Rendering.RenderPipelineManager.endContextRendering += OnEndContext;
        }

        private void OnDisable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            UnityEngine.Rendering.RenderPipelineManager.endContextRendering -= OnEndContext;
            RestoreViewGun();
        }

        /// <summary>
        /// The own monitor passenger (first person from the seat's eye): the camera at the eye draws Combat's view model where Combat put it; every
        /// other camera draws the gun in the seated avatar's hands (Halo's seat idle), so the avatar never shows arms raised to a gun at the eye.
        /// Combat's placement is given back once all cameras drew (it aims and fires from it in Update).
        /// </summary>
        private static void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            try
            {
                Rider r;
                if (cam == null || !Riders.TryGetValue(-1, out r) || !r.ViewGun || !r.GunValid || r.Vehicle == null || Time.frameCount - r.ViewGunFrame > 1) { RestoreViewGun(); SetViewArms(null, false); ViewHead(null); return; }
                Transform m = HaloDesk.HeldWeapon != null ? HaloDesk.HeldWeapon : HaloCombat.HeldModel;
                if (m == null || (cam.cullingMask & (1 << m.gameObject.layer)) == 0) return;      // (a camera that cannot see the gun, e.g. a HUD overlay)
                bool first = cam == HaloDesk.Cam() && (cam.transform.position - r.EyePos).sqrMagnitude < 1f;      // ours: the seat eye vs the posed head, well inside 1 m
                RestoreViewGun();
                _vgT = m;
                _vgLocalPos = m.localPosition; _vgLocalRot = m.localRotation; _vgLocalScale = m.localScale; _vgMoved = true;
                if (first)
                {
                    // Halo's first person shows the view model and the hands on it, nothing else of the body: the weapon framed like the standing
                    // first person (low right, shrunk toward the eye so the hands reach it), the arms bent onto it, the head shrunk away - this camera only
                    SetViewArms(r, true);
                    ViewHead(r.Anim);
                    if (r.ViewFramed)
                    {
                        Transform ct = cam.transform;
                        m.SetPositionAndRotation(ct.position + ct.rotation * r.ViewLocalPos, ct.rotation * r.ViewLocalRot);
                        if (r.ViewScale < 0.999f) m.localScale = _vgLocalScale * r.ViewScale;
                    }
                    return;
                }
                Transform t = r.Vehicle.transform;
                Vector3 p = t.TransformPoint(r.GunLocalPos);
                m.SetPositionAndRotation(p, t.rotation * r.GunLocalRot);
                LabViewGunDrawn = p;
                LabViewGunDrawnFrame = Time.frameCount;
            }
            catch (Exception) { RestoreViewGun(); SetViewArms(null, false); ViewHead(null); }
        }

        /// <summary>
        /// The own first-person seat view's arms (r.ViewGun): the body keeps Halo's seat clip (the W-passenger holding the rifle, what mirrors and
        /// every other camera draw); for the own camera at the seat's eye the arms are re-reached onto Combat's view model - the right wrist from
        /// the clip's grip offset, the left hand on the weapon's "left hand" marker - kept aside and laid on only while that camera draws.
        /// </summary>
        private static void ViewArms(Rider r, HaloVehicle.SeatInfo seat, float k)
        {
            r.ArmsValid = false;
            r.ViewFramed = false;
            if (!r.ViewGun) return;
            Transform m = HaloDesk.HeldWeapon != null ? HaloDesk.HeldWeapon : HaloCombat.HeldModel;
            if (m == null) return;
            var poser = r.Poser;
            poser.ArmsSave(r.ArmsOut);
            // the camera of this frame: the seat's eye with the look (HaloVehicleCamera.Place runs after this pose, at order 10000)
            Camera cam = HaloDesk.Cam();
            var own = HaloVehicleSeat.Local;
            Vector3 cp = own.Seated && own.Vehicle == r.Vehicle ? own.EyePoint() : cam != null ? cam.transform.position : r.EyePos;
            Quaternion cq = Quaternion.Euler(-HaloVehicleCamera.Pitch, HaloVehicleCamera.Yaw, 0f);
            float fov = cam != null ? cam.fieldOfView : 70f, aspect = cam != null && cam.aspect > 0.1f ? cam.aspect : 16f / 9f;
            Vector3 vp; Quaternion vg; float vs;
            HaloDesk.FirstPersonPose(cp, cq, fov, aspect, r.Weapon, HaloVehicleCamera.AimPoint, m.lossyScale.x, out vp, out vg, out vs);
            Quaternion vm = vg * HaloCyborg.ModelFromGun;
            r.ViewLocalPos = Quaternion.Inverse(cq) * (vp - cp);
            r.ViewLocalRot = Quaternion.Inverse(cq) * vm;
            r.ViewScale = vs;
            r.ViewFramed = true;
            // Halo's seat camera is the vehicle's marker ("camera rider"), and Halo draws no body there - only the first-person arms. The seat
            // clip leans the body forward past that marker (W-passenger rifle idle shoulders the rifle: the head 0.2 m ahead of the camera, the
            // shoulders and back in the view; its bladed stance turns the left shoulder toward the gun). For the own camera only the body is
            // drawn like on foot in first person: the shoulders square to the look and level, the head at the eye (the camera rides the head
            // there), the arms reached from those shoulders onto the view model. Every other camera draws the seat clip's body.
            Transform hips = r.Anim.GetBoneTransform(HumanBodyBones.Hips), headB = r.Anim.GetBoneTransform(HumanBodyBones.Head);
            Transform lUp = r.Anim.GetBoneTransform(HumanBodyBones.LeftUpperArm), rUp = r.Anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Vector3 hipsLocalWas = hips != null ? hips.localPosition : Vector3.zero;
            Transform lUL = r.Anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg), rUL = r.Anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Quaternion hipsRotWas = hips != null ? hips.localRotation : Quaternion.identity;
            Quaternion lulWas = lUL != null ? lUL.localRotation : Quaternion.identity, rulWas = rUL != null ? rUL.localRotation : Quaternion.identity;
            r.ViewBody = false;
            r.ViewShift = 0f;
            if (hips != null && headB != null && lUp != null && rUp != null)
            {
                Vector3 right = rUp.position - lUp.position;
                Vector3 up = Vector3.ProjectOnPlane(headB.position - (lUp.position + rUp.position) * 0.5f, right.normalized);
                if (right.sqrMagnitude > 1e-4f && up.sqrMagnitude > 1e-4f)
                {
                    Quaternion yawQ = Quaternion.Euler(0f, HaloVehicleCamera.Yaw, 0f);
                    Quaternion d = yawQ * Quaternion.Inverse(Quaternion.LookRotation(Vector3.Cross(right, up), up));
                    Vector3 pivot = headB.position;
                    // (the legs keep their seat-clip direction: turning the leaning body upright would swing the thighs up, the knees into the view)
                    Quaternion lulW = lUL != null ? lUL.rotation : Quaternion.identity, rulW = rUL != null ? rUL.rotation : Quaternion.identity;
                    hips.SetPositionAndRotation(pivot + d * (hips.position - pivot), d * hips.rotation);
                    Vector3 shift = Vector3.ClampMagnitude(cp + yawQ * ViewHeadOffset - headB.position, 0.8f);
                    hips.position += shift;
                    if (lUL != null) lUL.rotation = lulW;
                    if (rUL != null) rUL.rotation = rulW;
                    r.ViewLegL = lUL != null ? lUL.localRotation : Quaternion.identity;
                    r.ViewLegR = rUL != null ? rUL.localRotation : Quaternion.identity;
                    r.ViewShift = (headB.position - pivot).magnitude;
                    r.ViewHipsPos = hips.localPosition;
                    r.ViewHipsRot = hips.localRotation;
                    r.ViewBody = true;
                }
            }
            // the right wrist where the standing first person puts it (HaloPuppetPoser: ViewGun + ViewRot * RWristOnGun * k)
            Vector3 rw = vp + vg * (r.Puppet.Sample.RWristOnGun * k);
            if (r.LeftOf != m) { r.LeftOf = m; r.LeftMarker = HaloDeskPlayer.FindMarker(m, "left hand cyborg") ?? HaloDeskPlayer.FindMarker(m, "left hand"); }
            bool lv = r.LeftMarker != null;
            Vector3 lw = Vector3.zero;
            if (lv)
            {
                // the weapon's "left hand" marker on the framed (shrunk) view model; out of the arm's reach the hold slides back along the barrel
                // (the standing first person's rule, HaloPuppetPoser step 6) - never a straight arm stretched across the view
                Vector3 off = Quaternion.Inverse(m.rotation) * (r.LeftMarker.position - m.position);
                lw = vp + vm * (off * vs);
                Transform lu = r.Anim.GetBoneTransform(HumanBodyBones.LeftUpperArm), ll = r.Anim.GetBoneTransform(HumanBodyBones.LeftLowerArm), lhb = r.Anim.GetBoneTransform(HumanBodyBones.LeftHand);
                if (lu != null && ll != null && lhb != null)
                {
                    float reach = ((ll.position - lu.position).magnitude + (lhb.position - ll.position).magnitude) * 0.98f;
                    Vector3 back = -(vg * Vector3.forward);
                    for (int i = 0; i < 16 && (lw - lu.position).sqrMagnitude > reach * reach; i++) lw += back * 0.025f;
                }
            }
            if (r.ViewBody)
            {
                // the body drawn like on foot: the elbows hang down and out (the seat clip's poles raise them to its shouldered rifle - the upper
                // arms then pass beside the eye); the left hand keeps the clip's turn on the weapon, carried onto the view model
                Quaternion lookYaw = Quaternion.Euler(0f, HaloVehicleCamera.Yaw, 0f);
                Quaternion clipGun = r.GunRot * Quaternion.Inverse(HaloCyborg.ModelFromGun);
                Quaternion lGrip = vg * Quaternion.Inverse(clipGun) * (seat.Marker.rotation * r.Puppet.Sample.SLGrip);
                poser.ReachArms(lw, lookYaw * ViewLeftElbow, lGrip, lv, rw, lookYaw * ViewRightElbow, vg * Quaternion.Inverse(r.Puppet.Sample.GunInRGrip), lookYaw * Vector3.right);
            }
            else poser.ReachSeatedArms(r.Puppet.Sample, seat.Marker, lv, lw, true, rw, vg * Quaternion.Inverse(r.Puppet.Sample.GunInRGrip));
            Transform rh = r.Anim.GetBoneTransform(HumanBodyBones.RightHand), lh = r.Anim.GetBoneTransform(HumanBodyBones.LeftHand);
            r.ViewHandToGun = rh != null ? (rh.position - vp).magnitude : -1f;
            r.ViewLeftErr = lv && lh != null ? (lh.position - lw).magnitude : -1f;
            poser.ArmsSave(r.ArmsView);
            poser.ArmsLoad(r.ArmsOut);
            if (hips != null) { hips.localPosition = hipsLocalWas; hips.localRotation = hipsRotWas; }
            if (lUL != null) lUL.localRotation = lulWas;
            if (rUL != null) rUL.localRotation = rulWas;
            r.ArmsValid = true;
        }

        private static Rider _armsOn;
        private static Transform _hipsT;
        private static Vector3 _hipsLocal;
        private static Quaternion _hipsRot = Quaternion.identity;
        private static Transform _legL, _legR;
        private static Quaternion _legLWas = Quaternion.identity, _legRWas = Quaternion.identity;

        /// <summary>The own head bone from the first-person eye, in the look's yaw frame (the head bone sits below and behind the eyes; the desk
        /// lab's standing stand-in is placed the same way). Ours.</summary>
        public static Vector3 ViewHeadOffset = new Vector3(0f, -0.07f, -0.10f);
        /// <summary>The own first-person seat view's elbow poles in the look's yaw frame: down, out and a little back, as arms hang holding a rifle at the hip line. Ours.</summary>
        public static Vector3 ViewLeftElbow = new Vector3(-0.6f, -1f, -0.2f), ViewRightElbow = new Vector3(0.6f, -1f, -0.2f);

        /// <summary>Lab: lays the own first-person seat body on (the arms on the view model, the body moved to the eye) or gives the seat clip's back.</summary>
        public static void LabViewBody(bool on) { Rider r; if (on && Riders.TryGetValue(-1, out r)) SetViewArms(r, true); else { SetViewArms(null, false); ViewHead(null); } }

        /// <summary>The own first-person arms for this camera (true: onto the view model) - given back to the seat clip's at the end of drawing.</summary>
        private static void SetViewArms(Rider r, bool view)
        {
            if (view)
            {
                if (r == null || !r.ArmsValid || r.Anim == null) return;
                if (_armsOn == r) return;
                r.Poser.ArmsLoad(r.ArmsView);
                _armsOn = r;
                _hipsT = r.Anim.GetBoneTransform(HumanBodyBones.Hips);
                if (_hipsT != null && r.ViewBody)
                {
                    _hipsLocal = _hipsT.localPosition;
                    _hipsRot = _hipsT.localRotation;
                    _hipsT.localPosition = r.ViewHipsPos;
                    _hipsT.localRotation = r.ViewHipsRot;
                    _legL = r.Anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); _legR = r.Anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                    if (_legL != null) { _legLWas = _legL.localRotation; _legL.localRotation = r.ViewLegL; }
                    if (_legR != null) { _legRWas = _legR.localRotation; _legR.localRotation = r.ViewLegR; }
                }
                else _hipsT = null;
                return;
            }
            if (_armsOn == null) return;
            try { if (_armsOn.Anim != null && _armsOn.ArmsValid) _armsOn.Poser.ArmsLoad(_armsOn.ArmsOut); } catch (Exception) { }
            try { if (_hipsT != null) { _hipsT.localPosition = _hipsLocal; _hipsT.localRotation = _hipsRot; if (_legL != null) _legL.localRotation = _legLWas; if (_legR != null) _legR.localRotation = _legRWas; } } catch (Exception) { }
            _hipsT = null;
            _legL = null; _legR = null;
            _armsOn = null;
        }

        private static void OnEndContext(UnityEngine.Rendering.ScriptableRenderContext ctx, List<Camera> cams) { RestoreViewGun(); SetViewArms(null, false); ViewHead(null); }

        private static Vector3 _vgLocalScale = Vector3.one;
        private static Transform _vhT;
        private static Vector3 _vhScale;

        /// <summary>The own head shrunk away for the own first-person seat camera only (a non-null avatar), given back for every other camera and at
        /// the end of drawing (Desk's HaloHeadHide does this on foot; V poses the seated own avatar, so Desk's is off then). Monitor only: the
        /// client shrinks a headset player's head itself.</summary>
        private static void ViewHead(Animator a)
        {
            Transform head = a != null ? a.GetBoneTransform(HumanBodyBones.Head) : null;
            if (_vhT != null && _vhT != head) { try { _vhT.localScale = _vhScale; } catch (Exception) { } _vhT = null; }
            if (head == null) return;
            LabViewHeadHiddenFrame = Time.frameCount;      // (also when LateUpdate already shrank it: a camera drawing this frame sees it shrunk)
            if (_vhT == head) return;
            _vhT = head;
            _vhScale = head.localScale;
            head.localScale = _vhScale * 0.0001f;
            LabViewHeadHiddenFrame = Time.frameCount;
        }

        private static void RestoreViewGun()
        {
            if (!_vgMoved) return;
            _vgMoved = false;
            try { if (_vgT != null) { _vgT.localPosition = _vgLocalPos; _vgT.localRotation = _vgLocalRot; _vgT.localScale = _vgLocalScale; } } catch (Exception) { }
        }

        /// <summary>The own monitor player looks from the seat's eye (Halo: a seat without the "third person camera" flag, the warthog passenger).</summary>
        private static bool OwnView(Rider r, bool tracked)
        {
            bool v = r.Key == -1 && !tracked && !r.Vr && !HaloVehicleSeat.Local.ThirdPerson;
            r.View = v;
            return v;
        }

        private static Transform _headT;
        private static Vector3 _headWas;
        private static bool _headShown, _saidHead;

        /// <summary>
        /// The own head in a headset: the client shrinks the own avatar's head on the main copy (so the eyes are not inside it) and V never
        /// hides or scales a head. In the third-person vehicle view, though, the player watches their own avatar driving from behind - there the
        /// head is given its size back on this screen (only while that view is on), and the client's scale is put back for first person, on getting
        /// out and on avatar changes. Mirrors, shadows and other players never had it hidden.
        /// </summary>
        private static void OwnHead(Animator a, bool show)
        {
            Transform head = a != null ? a.GetBoneTransform(HumanBodyBones.Head) : null;
            if (_headT != null && (_headT != head || !show))
            {
                if (_headShown) { try { _headT.localScale = _headWas; } catch (Exception) { } }
                _headT = null; _headShown = false;
            }
            if (!show || head == null) return;
            if (_headT == null) { _headT = head; _headWas = head.localScale; _headShown = false; }
            if (head.localScale.sqrMagnitude < 1e-4f)
            {
                if (!_headShown) _headWas = head.localScale;
                head.localScale = Vector3.one;
                _headShown = true;
                if (!_saidHead) { _saidHead = true; HaloLog.Info("Vehicles: VR third-person view - my avatar's head (shrunk by the client for first person) is shown on this screen while I watch from behind; the client's scale comes back for first person."); }
            }
        }

        /// <summary>The own head's scale now (the lab checks it).</summary>
        public static Vector3 OwnHeadScale(Animator a) { Transform h = a != null ? a.GetBoneTransform(HumanBodyBones.Head) : null; return h != null ? h.localScale : Vector3.zero; }

        private static bool VrThird(Rider r)
        {
            if (r.Key == -1) return HaloVehicleSeat.Local.ThirdPerson;
            // a remote headset driver whose head is far from the seat watches from the chase view
            Transform head = null;
            try { head = r.Player != null ? r.Player.References?.Body?.Head : null; } catch (Exception) { head = null; }
            var s = r.Vehicle.SeatOf(r.Seat);
            return head != null && s != null && (head.position - s.Marker.position).sqrMagnitude > 1.6f * 1.6f;
        }

        /// <summary>
        /// A headset gunner holds the gun's grips with the real hands; Halo's seat clip puts the hips where a 2 m cyborg stands to reach those
        /// grips, so a smaller avatar's arms come up short (vehicles-vr: 0.14 / 0.19 m). The seat frame the body is laid on is moved toward the
        /// hands by what the arms lacked last frame (horizontal, at most 0.35 m - ours), in the seat marker's frame so it rides the turning
        /// mount: the body leans in to the grips as the player's real body does. Only seats with grips (the warthog / turret gunner).
        /// </summary>
        private static Transform Reach(Rider r, Transform marker, bool lv, Vector3 lw, bool rv, Vector3 rw, float dt)
        {
            if (r.ReachProxy == null) { r.ReachProxy = new GameObject("Halo seat reach").transform; r.Lean = Vector3.zero; }
            if (r.ReachProxy.parent != marker) { r.ReachProxy.SetParent(marker, false); r.Lean = Vector3.zero; }
            Transform ls = r.Poser.UpperArm(false), rs = r.Poser.UpperArm(true);
            float lack = Mathf.Max(lv ? r.Poser.LastLeftErr : 0f, rv ? r.Poser.LastRightErr : 0f);
            Vector3 lean = marker.rotation * r.Lean;
            if ((lv || rv) && ls != null && rs != null && lack > 0.01f)
            {
                Vector3 hands = lv && rv ? (lw + rw) * 0.5f : (lv ? lw : rw);
                Vector3 d = hands - (ls.position + rs.position) * 0.5f;
                d.y = 0f;
                if (d.sqrMagnitude > 1e-4f) lean = Vector3.MoveTowards(lean, Vector3.ClampMagnitude(lean + d.normalized * lack, 0.35f), dt * 0.8f);      // ours: 0.8 m/s
            }
            else if (lack <= 0.01f) lean = Vector3.MoveTowards(lean, Vector3.zero, dt * 0.05f);      // (eases back when the hands come in)
            r.Lean = Quaternion.Inverse(marker.rotation) * lean;
            r.ReachProxy.SetPositionAndRotation(marker.position + lean, marker.rotation);
            if (!r.ReachSaid && lean.sqrMagnitude > 0.05f * 0.05f)
            {
                r.ReachSaid = true;
                HaloLog.Info("Vehicles: " + Who(r) + " leans " + lean.magnitude.ToString("0.00") + " m toward the tracked hands on the '" + r.Label + "' grips (the avatar's arms are shorter than the cyborg's).");
            }
            return r.ReachProxy;
        }

        private static Transform HandAnchor(Rider r, bool right)
        {
            if (r.Key == -1) { Transform lab = right ? HaloVehicleInput.LabRightHand : HaloVehicleInput.LabLeftHand; return lab != null ? lab : (right ? HaloPlayer.RightHand : HaloPlayer.LeftHand); }
            try { var b = r.Player != null ? r.Player.References?.Body : null; return b != null ? (right ? b.RightHand : b.LeftHand) : null; }
            catch (Exception) { return null; }
        }

        private static void Overlays(Rider r, HaloVehicle.SeatInfo seat, out float yaw, out float pitch, out float accF, out float accL)
        {
            var v = r.Vehicle;
            yaw = 0f; pitch = 0f;
            if (seat.Def.Driver && !seat.Guns) yaw = v.Steer * 0.45f;                // the warthog driver into the steering (ours: 0.45 rad at full lock)
            else if (seat.Guns && seat.Marker != null)
            {
                // Halo's aim-still overlay is the unit's aim relative to its seat: the seat marker rides whatever node it is on (the warthog
                // gunner's on the gun mount base = the gun's yaw; the gun turret's on the turret end = yaw AND pitch; the scorpion driver's on
                // the hull), so the overlay is the gun's aim in the marker's frame - never the gun's yaw / pitch added on top of a turning seat
                Vector3 local = Quaternion.Inverse(seat.Marker.rotation) * v.AimDirection;      // marker frame: x forward, y up, z left
                yaw = Mathf.Atan2(local.z, local.x);
                pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f));
            }
            else
            {
                // a passenger looks / aims where their head looks (relative to the seat)
                Transform head = r.Key == -1 ? HaloPlayer.HeadBone : null;
                if (r.Key != -1) { try { head = r.Player != null ? r.Player.References?.Body?.Head : null; } catch (Exception) { head = null; } }
                if (head != null && seat.Marker != null)
                {
                    Vector3 local = Quaternion.Inverse(seat.Marker.rotation) * head.forward;      // marker frame: x forward, y up, z left
                    yaw = Mathf.Atan2(local.z, local.x);
                    pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f));
                }
            }
            Vector3 a = v.Acceleration;
            accF = Mathf.Clamp(Vector3.Dot(a, v.transform.forward) / 12f, -1f, 1f);       // ours: 12 m/s^2 = the full lurch
            accL = Mathf.Clamp(-Vector3.Dot(a, v.transform.right) / 12f, -1f, 1f);
        }

        private static bool FindAvatar(Rider r)
        {
            Animator a = LabAvatar != null ? LabAvatar(r.Key) : null;
            if (a == null && r.Anim != null && r.Anim.gameObject.activeInHierarchy) a = r.Anim;
            if (a == null && LabAvatar == null && Time.unscaledTime >= r.NextSearch)
            {
                r.NextSearch = Time.unscaledTime + 1f;
                Transform head = null;
                if (r.Key == -1) head = HaloPlayer.HeadBone;
                else { try { head = r.Player != null ? r.Player.References?.Body?.Head : null; } catch (Exception) { head = null; } }
                if (r.Key != -1)
                {
                    var p = HaloNetGame.Players;
                    for (int i = 0; i < p.Count; i++) if (p[i] != null && p[i].PlayerId == r.Key && p[i].Avatar != null) { a = p[i].Avatar; break; }
                }
                string kind;
                if (a == null && head != null) a = HaloAvatarFind.ByHead(head.position, out kind);
            }
            if (a == null) return false;
            if (a != r.Anim || !r.Poser.Bound(a))
            {
                string say;
                if (!r.Poser.Bind(a, out say)) { HaloLog.Info("Vehicles: " + Who(r) + "'s avatar cannot be seated: " + say); r.Anim = null; return false; }
                r.Anim = a; r.Copies = null;
            }
            return true;
        }

        /// <summary>
        /// Where the seat's exit clip ends (the cyborg's pelvis on its last frame, at an avatar's size of 0.8 of the cyborg's), dropped to the
        /// ground: getting out stands the player there so the clip ends on the player's real spot. False when the seat has no exit clip.
        /// </summary>
        public static bool ExitPoint(HaloVehicle v, int seatIndex, out Vector3 at)
        {
            at = Vector3.zero;
            HaloCyborg c = Cyb();
            var s = v != null ? v.SeatOf(seatIndex) : null;
            if (c == null || s == null || s.Marker == null) return false;
            var p = new HaloSeatPuppet(c);
            float len = p.Length(s.Label, HaloSeatPuppet.Phase.Exit);
            if (len <= 0.05f || !p.Step(s.Label, null, HaloSeatPuppet.Phase.Exit, len, 0f, 0f, 0f, 0f)) return false;
            Vector3 pelvis = s.Marker.TransformPoint(p.Sample.SPelvis * 0.8f);
            RaycastHit hit;
            if (!Physics.Raycast(pelvis + Vector3.up * 1.5f, Vector3.down, out hit, 6f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) return false;
            at = hit.point + Vector3.up * 0.05f;
            return true;
        }
    }
}

using System;
using SomniumSpace.Bridge;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The local player in a vehicle seat: the Euro Truck Simulator Dreamport's EtsTruckSeat, ported. Seated, the Bridge player's own
    /// locomotion, collision, gravity, jump and teleport are switched off, the falling animation and the VR IK leg stepping too, and the
    /// body root is written every frame onto the seat (heading only, never the vehicle's pitch or roll - the ETS user found a tilting
    /// view "VERY disorientating"). Sitting seats hold the client's chair pose (Bridge Poses.SetPose(SittingStraight), re-entered the
    /// same frame whenever the client drops it, the pose's head re-centre undone in a headset - ETS UndoPoseRecentre); the warthog
    /// gunner stands on the seat's foot markers. The capsule ignores the Vehicle layer while seated and the vehicle's shapes ignore
    /// the avatar layers (ETS: the Bridge's collision switch does not stop the capsule shoving the vehicle).
    ///
    /// The view, as the Somnium client's own vehicle system does it (E:\Somnium Space\Main Build VehicleSystem; DevTools/agents/V/NOTES.md):
    ///  - monitor: the client's camera rig is held off (its CinemachineBrain disabled, as EtsTruckCamera) and HaloVehicleCamera places
    ///    the camera: a chase / orbit view for seats with Halo's "third person camera" flag, the seat's eye otherwise;
    ///  - headset, third-person seats (drivers): the client's ThirdPersonViewState - the view rides a rig at the vehicle, heading only,
    ///    turned toward the vehicle's heading at 2/s (reversing: looks back), 4 m up and 6 + speed/10 (at most 10) m behind, the head's own
    ///    tracking kept relative to it. In a world the HMD camera cannot be re-parented, so the XR rig is moved instead so the head lands
    ///    there (ETS PlaceHead). The own avatar would hang under that camera, so its body is shrunk to nothing on this screen only
    ///    (bone scale is not networked) and every other client draws it in the seat (HaloVehicleRiders). Left Y switches to first person,
    ///    as the client's SwitchCamera does; the setting "vehicles.vrthird" keeps the choice.
    ///  - headset, other seats: first person at the seat's eye with the head's own lean kept (0.45 m at most, ETS).
    /// Getting out stands the player at the seat's "enter" marker on the ground (Halo's own entry point) through DoTeleportToPoint.
    /// </summary>
    public sealed class HaloVehicleSeat
    {
        public static readonly HaloVehicleSeat Local = new HaloVehicleSeat();

        private const float LeanLimit = 0.45f;
        private const string PoseOffsetName = "HeadCustomPoseOffset";

        public HaloVehicle Vehicle { get; private set; }
        public HaloVehicle.SeatInfo Seat { get; private set; }
        public int SeatIndex { get; private set; } = -1;
        public bool Seated => Vehicle != null;
        /// <summary>A headset (or the lab's simulated one).</summary>
        public static bool Vr => HaloPlayer.XrPresent || HaloVehicleInput.LabVr;
        public bool Driver => Seated && Seat.Def.Driver;
        /// <summary>The view of this seat is third person (Halo's seat flag; in a headset only while the player wants it).</summary>
        public bool ThirdPerson { get; private set; }

        private Transform _root, _rig, _head, _poseOffset, _poseOffsetFrom, _leftHandOffset, _rightHandOffset, _leftHand, _rightHand;
        private ISomniumPlayerMotion _motion;
        private ISomniumPlayerAnimation _animation;
        private ISomniumPlayerPoses _poses;
        private ISomniumPlayerAvatar _avatar;
        private CharacterController _capsule;
        private bool _capsuleExcluded, _forceSitting = true, _poseReported, _restKnown;
        private Vector3 _headRest;
        private float _chaseYaw; private bool _chaseYawKnown; private Vector3 _lastAnchor;
        private bool _vrThird = true;
        private HaloAvatarHide _hide = new HaloAvatarHide();
        private bool _saidRig;

        /// <summary>Sitting pose for this seat (every seat but a standing gunner).</summary>
        private bool Sits => Seat != null && !Seat.Standing;

        // ------------------------------------------------------------------ in / out

        public bool GetIn(HaloVehicle v, int seatIndex)
        {
            if (v == null || Seated) return false;
            var seat = v.SeatOf(seatIndex);
            if (seat == null || seat.Occupant >= 0) return false;
            ISomniumPlayer player = null;
            try { player = SomniumBridge.PlayersContainer?.LocalPlayer; } catch (Exception) { player = null; }
            _root = player?.References?.Body?.Root;
            _motion = player?.Features?.Motion; _animation = player?.Features?.Animation; _poses = player?.Features?.Poses; _avatar = player?.Features?.Avatar;
            _head = player?.References?.Body?.Head; _leftHand = player?.References?.Body?.LeftHand; _rightHand = player?.References?.Body?.RightHand;
            _poseOffset = null; _poseOffsetFrom = null; _leftHandOffset = null; _rightHandOffset = null; _poseReported = false; _restKnown = false; _rig = null;
            _chaseYawKnown = false; _forceSitting = true;

            Vehicle = v; Seat = seat; SeatIndex = seatIndex;
            seat.Occupant = HaloLocal.NetId >= 0 ? HaloLocal.NetId : 0;
            v.SetLocalSeated(true);
            if (seat.Def.Driver) v.TakeControl();
            ExcludeVehicles(true);
            if (_motion != null)
            {
                try
                {
                    _motion.SetCollisionDisableState(true); _motion.SetFlyModeDisableState(true); _motion.SetTeleportationDisableState(true);
                    _motion.SetMovementSpeed(0f, 0f, 0f); _motion.SetGravity(0f, 0f); _motion.SetJump(0f, 0f);
                }
                catch (Exception e) { HaloLog.Warn("Vehicles: seating the Bridge player threw: " + e.Message); }
            }
            try { _animation?.SetFallingAnimationDisabled(true); } catch (Exception) { }
            try { _avatar?.SetVRIKLegMovementDisabled(true); } catch (Exception) { }
            _vrThird = HaloSettings.Flag("vehicles.vrthird", true);
            ThirdPerson = WantThirdPerson();
            Publish();
            HaloLog.Info("Vehicles: seated in " + v.name + " seat " + seatIndex + " '" + seat.Label + "' (" + (seat.Def.Driver ? "driver" : seat.Guns ? "gunner" : "rider") + ", " +
                         (seat.Standing ? "standing" : "sitting") + ", " + (Vr ? "headset" : "monitor") + ", " + (ThirdPerson ? "third person" : "first person") +
                         (_root == null ? "; no Bridge body (editor)" : "") + ").");
            HaloVehicleNet.NoteSeat(v, seatIndex);
            return true;
        }

        private bool WantThirdPerson()
        {
            bool third = (Seat.Def.Flags & HaloVehicleDef.SeatThirdPerson) != 0;
            if (!Vr) return third;
            // headset: the client's third-person vehicle view for drivers; the warthog gunner holds the grips in first person
            if (Seat.Standing || !Seat.Def.Driver) return false;
            return third && _vrThird;
        }

        /// <summary>Out of the seat onto the ground at the seat's enter marker. False when not seated.</summary>
        public bool GetOut(string why)
        {
            if (!Seated) return false;
            var v = Vehicle; var seat = Seat;
            Vector3 at; float yaw;
            StandPoint(v, seat, out at, out yaw);
            // Halo's exit clip ends beside the vehicle: stand the player where it ends, so the body the others see lands on the real spot
            Vector3 clipEnd;
            if (HaloSeatPoses.ExitPoint(v, SeatIndex, out clipEnd) && (clipEnd - at).sqrMagnitude < 4f * 4f) at = clipEnd;
            Release();
            string how;
            bool moved = HaloPlayer.Teleport(at, yaw, out how);
            HaloLog.Info("Vehicles: got out of " + v.name + " (" + why + ") at " + at.ToString("F1") + (moved ? "" : " - the Bridge would not move the player (" + how + ")") + ".");
            return true;
        }

        /// <summary>Where getting out of a seat stands you: its enter marker dropped to the ground (Halo), else 1.6 m beside the seat (ETS).</summary>
        public static void StandPoint(HaloVehicle v, HaloVehicle.SeatInfo seat, out Vector3 at, out float yaw)
        {
            Transform t = v.transform;
            Vector3 p = seat.Enter != null ? seat.Enter.position : seat.Marker.position + (t.InverseTransformPoint(seat.Marker.position).x < 0f ? -t.right : t.right) * 1.6f;
            // an enter marker sits low on the hull: stand on whatever ground is under it
            RaycastHit hit;
            bool flier = v.Def.Type == HaloVehicleDef.Kind.HumanPlane || v.Def.Type == HaloVehicleDef.Kind.AlienFighter;
            if (flier && seat.Marker != null)
            {
                // a dropship / pelican rider drops out of the bottom of its side pod (Halo: the troop pods open downward), under the hull
                p = seat.Marker.position + (t.InverseTransformPoint(seat.Marker.position).x < 0f ? -t.right : t.right) * 0.6f;
                p.y = Mathf.Min(p.y, v.ShapeBounds().min.y - 0.4f);
            }
            at = p;
            if (Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out hit, flier ? 4.5f : 8f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) at = hit.point + Vector3.up * 0.05f;      // (a hovering flier: no ground near - they fall)
            Vector3 face = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            yaw = face.sqrMagnitude > 1e-4f ? Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg : 0f;
        }

        /// <summary>Lets go of everything (death, match end, the vehicle gone) without moving the player.</summary>
        public void Release()
        {
            if (!Seated) return;
            HaloCombat.NoteTeleport();      // the exit puts the avatar down at once: not a fall
            var v = Vehicle;
            _hide.Show();
            LevelRig();
            if (_poses != null && Sits)
            {
                try { _poses.SetPose(PoseType.None); } catch (Exception e) { HaloLog.Info("Vehicles: leaving the seat pose threw " + e.GetType().Name); }
            }
            try { _avatar?.SetVRIKLegMovementDisabled(false); } catch (Exception) { }
            if (_motion != null)
            {
                try
                {
                    _motion.SetCollisionDisableState(false); _motion.SetFlyModeDisableState(false); _motion.SetTeleportationDisableState(false);
                    _motion.SetMovementSpeed(1f, 1f, 1f); _motion.SetGravity(1f, 1f); _motion.SetJump(1f, 1f);
                }
                catch (Exception e) { HaloLog.Warn("Vehicles: releasing the Bridge player threw: " + e.Message); }
            }
            try { _animation?.SetFallingAnimationDisabled(false); } catch (Exception) { }
            ExcludeVehicles(false);
            if (Seat != null) Seat.Occupant = -1;
            if (v != null)
            {
                v.SetLocalSeated(false);
                if (Seat != null && Seat.Def.Driver) { v.Controls = default(HaloVehicleControls); v.Park(true); }
            }
            Vehicle = null; Seat = null; SeatIndex = -1; ThirdPerson = false;
            HaloVehicleCamera.Release();
            Publish();
            HaloVehicleNet.NoteSeat(null, -1);
        }

        private void Publish()
        {
            HaloLocal.Vehicle = Vehicle != null ? Vehicle.gameObject : null;
            HaloLocal.Seat = Seat != null ? Seat.Label : "";
            HaloLocal.Driving = Driver;
            HaloLocal.VehicleTag = Vehicle != null ? Vehicle.Def.Tag : null;
            HaloLocal.VehicleId = Vehicle != null ? Vehicle.Id : -1;
            HaloLocal.SeatIndex = SeatIndex;
            HaloLocal.Gunning = Seat != null && Seat.Guns && Vehicle.Gun != null;
            HaloLocal.VehicleWeapon = HaloLocal.Gunning ? Vehicle.Gun.Tag : null;
            HaloLocal.VehicleWeaponHud = HaloLocal.Gunning && Vehicle.Gun.Def != null ? Vehicle.Gun.Def.HudInterface : null;
            HaloLocal.SeatHud = Seat != null ? Seat.Def.HudInterface : null;
            HaloLocal.SeatAllowsWeapons = Seat != null && Seat.Def.AllowsWeapons;
            HaloLocal.VehicleCamera = Seated && !Vr;
            if (!Seated) { HaloLocal.VehicleSpeed = 0f; HaloLocal.VehicleHeat = 0f; HaloLocal.VehicleOverheated = false; }
        }

        private void ExcludeVehicles(bool on)
        {
            int bit = 1 << HaloLayers.Vehicle;
            if (on)
            {
                _capsule = _root != null ? _root.GetComponent<CharacterController>() : null;
                if (_capsule != null && (_capsule.excludeLayers.value & bit) == 0) { _capsule.excludeLayers = _capsule.excludeLayers.value | bit; _capsuleExcluded = true; }
                return;
            }
            if (_capsuleExcluded && _capsule != null) _capsule.excludeLayers = _capsule.excludeLayers.value & ~bit;
            _capsuleExcluded = false;
        }

        /// <summary>Headset: left Y switches the driver's view between third and first person (the client's SwitchCamera).</summary>
        public void ToggleView()
        {
            if (!Seated || !Vr || !Seat.Def.Driver || (Seat.Def.Flags & HaloVehicleDef.SeatThirdPerson) == 0) return;
            _vrThird = !_vrThird;
            HaloSettings.SetFlag("vehicles.vrthird", _vrThird);
            ThirdPerson = WantThirdPerson();
            _restKnown = false; _chaseYawKnown = false;
            if (!ThirdPerson) _hide.Show();
            HaloLog.Info("Vehicles: headset view " + (ThirdPerson ? "third person (behind the vehicle)" : "first person (the seat)") + ".");
        }

        // ------------------------------------------------------------------ every frame

        /// <summary>Puts the body (and in a headset the rig) on the seat. Called early in Update and again late in LateUpdate.</summary>
        public void Place(bool late)
        {
            if (!Seated) return;
            if (Vehicle == null) { Release(); return; }
            HaloLocal.VehicleSpeed = Vehicle.SpeedMs;
            if (Vehicle.Gun != null) { HaloLocal.VehicleHeat = Vehicle.Gun.Heat; HaloLocal.VehicleOverheated = Vehicle.Gun.Overheated; }
            if (_root == null) return;
            bool entered = late && Sits && _forceSitting && KeepSitting();
            Vector3 seatPoint = SeatPoint();
            Quaternion heading = Heading();
            _root.SetPositionAndRotation(seatPoint, heading);
            bool vr = HaloPlayer.XrPresent;
            if (vr)
            {
                if (entered) _restKnown = false;
                if (ThirdPerson)
                {
                    PlaceHead(ChasePoint(), Quaternion.Euler(0f, _chaseYaw, 0f), false);
                }
                else PlaceHead(EyePoint(), heading, true);
            }
            else if (_head != null && Sits && PoseLowered)
            {
                // monitor sitting: the pose lowered the head anchor; bring the hips down onto the seat (ETS)
                _root.position += EyePoint() - _head.position;
            }
        }

        private Vector3 SeatPoint()
        {
            if (Seat.Standing) return (Seat.LeftFoot.position + Seat.RightFoot.position) * 0.5f;
            return Seat.Marker.position;
        }

        /// <summary>The seat's heading only (upright). A standing gunner turns with the gun on a monitor; a headset gunner turns their own body.</summary>
        private Quaternion Heading()
        {
            Transform t = Seat.Standing && !HaloPlayer.XrPresent && Seat.Marker != null ? Seat.Marker : Vehicle.transform;
            Vector3 f = t == Vehicle.transform ? t.forward : t.right; // markers carry Halo's forward on their x axis
            f = Vector3.ProjectOnPlane(f, Vector3.up);
            if (f.sqrMagnitude < 1e-6f) f = Vector3.ProjectOnPlane(Vehicle.transform.forward, Vector3.up);
            return Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        /// <summary>The seat's eye: its camera marker when it has one at head height, else 0.72 m over the seat (sitting) / 1.62 m over the feet (ours).</summary>
        public Vector3 EyePoint()
        {
            if (Seat.Standing) return SeatPoint() + Vector3.up * 1.62f;
            Vector3 seat = Seat.Marker.position;
            if (Seat.Camera != null)
            {
                float h = Seat.Camera.position.y - seat.y;
                if (h > 0.3f && h < 1.2f) return Seat.Camera.position;
            }
            return seat + Vector3.up * 0.72f;
        }

        /// <summary>The client's VR third-person vehicle view (ThirdPersonViewState): heading-only rig at the vehicle, 4 m up,
        /// 6 + speed / 10 m (at most 10) behind; the heading follows the vehicle's at 2 / s and looks back when it reverses.</summary>
        private Vector3 ChasePoint()
        {
            Transform t = Vehicle.transform;
            Vector3 anchor = t.position;
            float dt = Time.deltaTime;
            Vector3 f = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            float vehicleYaw = f.sqrMagnitude > 1e-6f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : _chaseYaw;
            if (!_chaseYawKnown) { _chaseYaw = vehicleYaw; _chaseYawKnown = true; _lastAnchor = anchor; }
            Vector3 moved = anchor - _lastAnchor; moved.y = 0f;
            _lastAnchor = anchor;
            if (moved.magnitude > 0.1f * Mathf.Max(1f, dt * 60f))
            {
                float target = vehicleYaw;
                if (Vector3.Angle(moved, f) > 110f) target += 180f;
                _chaseYaw = Mathf.LerpAngle(_chaseYaw, target, Mathf.Clamp01(2f * dt));
            }
            else _chaseYaw = Mathf.LerpAngle(_chaseYaw, vehicleYaw, Mathf.Clamp01(0.5f * dt)); // ours: drift back behind when parked
            float back = Mathf.MoveTowards(6f, 10f, Vehicle.SpeedMs / 10f);
            return anchor + Vector3.up * 4f + Quaternion.Euler(0f, _chaseYaw, 0f) * new Vector3(0f, 0f, -back);
        }

        /// <summary>ETS PlaceHead: shift the rig so the eyes land at <paramref name="eye"/>, keeping the head's lean since sitting down.</summary>
        private void PlaceHead(Vector3 eye, Quaternion rigRotation, bool lean)
        {
            Camera head = HaloPlayer.Head();
            if (head == null) return;
            if (_rig == null)
            {
                bool carried = head.transform.IsChildOf(_root);
                _rig = carried ? _root : head.transform.root;
                if (!_saidRig) { _saidRig = true; HaloLog.Info("Vehicles: VR seat - the camera " + (carried ? "rides on the body" : "is outside the body") + "; moving rig '" + _rig.name + "'."); }
            }
            _rig.rotation = rigRotation;
            Vector3 inRig = _rig.InverseTransformPoint(head.transform.position);
            if (!_restKnown || (inRig - _headRest).sqrMagnitude > 0.7f * 0.7f) { _headRest = inRig; _restKnown = true; }
            Vector3 l = lean ? Vector3.ClampMagnitude(inRig - _headRest, LeanLimit) : inRig - _headRest;
            Vector3 wanted = eye + _rig.TransformVector(l);
            _rig.position += wanted - head.transform.position;
        }

        private void LevelRig()
        {
            if (_root == null) return;
            if (_root.up.y < 0.9999f) _root.rotation = Quaternion.Euler(0f, _root.eulerAngles.y, 0f);
            if (_rig != null && _rig != _root && _rig.up.y < 0.9999f) _rig.rotation = Quaternion.Euler(0f, _rig.eulerAngles.y, 0f);
        }

        private bool PoseLowered => _poseOffset != null && _poseOffset.localPosition.y < -0.05f;

        /// <summary>ETS KeepSitting: enter / hold the client's chair pose every frame; true when the pose began this frame.</summary>
        private bool KeepSitting()
        {
            if (_poses == null) return false;
            Camera camera = HaloPlayer.XrPresent ? HaloPlayer.Head() : null;
            Transform eyes = camera != null ? camera.transform : _head;
            FindPoseOffset(eyes);
            Vector3 before = eyes != null ? _root.InverseTransformPoint(eyes.position) : Vector3.zero;
            try { _poses.SetPose(PoseType.SittingStraight); }
            catch (Exception e)
            {
                _forceSitting = false;
                HaloLog.Info("Vehicles: the client threw on SetPose (" + e.GetType().Name + ": " + e.Message + "); sitting is off for this ride.");
                return false;
            }
            if (HaloPlayer.XrPresent && eyes != null && _poseOffset != null) UndoPoseRecentre(eyes);
            bool moved = eyes != null && (_root.InverseTransformPoint(eyes.position) - before).sqrMagnitude > 0.03f * 0.03f;
            if (moved && !_poseReported)
            {
                _poseReported = true;
                HaloLog.Info("Vehicles: seat pose SittingStraight entered; pose offset " + (_poseOffset != null ? _poseOffset.localPosition.ToString("F2") : "not found") + ".");
            }
            return moved;
        }

        private void UndoPoseRecentre(Transform eyes)
        {
            if (_poseOffset.localRotation != Quaternion.identity)
            {
                _poseOffset.localRotation = Quaternion.identity;
                Vector3 inRig = _root.InverseTransformPoint(eyes.position);
                _poseOffset.position -= _root.TransformVector(new Vector3(inRig.x, 0f, inRig.z));
            }
            UndoHand(_leftHandOffset); UndoHand(_rightHandOffset);
        }

        private void UndoHand(Transform hand)
        {
            if (hand == null) return;
            if (hand.localRotation != Quaternion.identity) hand.localRotation = Quaternion.identity;
            hand.localPosition = _poseOffset.localPosition;
        }

        private void FindPoseOffset(Transform from)
        {
            if (from == null || from == _poseOffsetFrom) return;
            _poseOffsetFrom = from; _poseOffset = null;
            for (Transform t = from; t != null && t != _root; t = t.parent) if (t.name == PoseOffsetName) { _poseOffset = t; break; }
            _leftHandOffset = Ancestor(_leftHand, "LeftHandCustomPoseOffset");
            _rightHandOffset = Ancestor(_rightHand, "RightHandCustomPoseOffset");
        }

        private Transform Ancestor(Transform from, string n)
        {
            for (Transform t = from; t != null && t != _root; t = t.parent) if (t.name == n) return t;
            return null;
        }
    }

    /// <summary>
    /// The own avatar hidden on this screen only (VR third-person vehicle view): every humanoid Animator whose head bone is within 0.6 m
    /// of the player's head (the main copy and its shadow / mirror copies - Black Ops 2's Bo2AvatarFind.ByHead rule, no client types)
    /// gets its hips bone scaled to 1/10000, as BO2 / ETS shrink the head. Bone scale is not networked: everyone else sees the avatar.
    /// Searched at most once a second; restored on Show.
    /// </summary>
    public sealed class HaloAvatarHide
    {
        private readonly System.Collections.Generic.List<Transform> _bones = new System.Collections.Generic.List<Transform>();
        private readonly System.Collections.Generic.List<Vector3> _scales = new System.Collections.Generic.List<Vector3>();
        private float _next;
        private bool _said;

        public void Hide(Transform bodyRoot, Camera head)
        {
            if (_bones.Count == 0 && Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 1f;
                Transform h = HaloPlayer.HeadBone;
                Vector3 at = h != null ? h.position : (head != null ? head.transform.position : bodyRoot.position + Vector3.up * 1.6f);
                foreach (var a in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
                {
                    if (a == null || !a.isHuman || a.avatar == null || !a.gameObject.activeInHierarchy) continue;
                    Transform hb = a.GetBoneTransform(HumanBodyBones.Head), hips = a.GetBoneTransform(HumanBodyBones.Hips);
                    if (hb == null || hips == null || (hb.position - at).sqrMagnitude > 0.6f * 0.6f) continue;
                    _bones.Add(hips); _scales.Add(hips.localScale);
                }
                if (!_said && _bones.Count > 0) { _said = true; HaloLog.Info("Vehicles: own avatar hidden on this screen for the VR third-person view (" + _bones.Count + " copies); others see it in the seat."); }
            }
            for (int i = 0; i < _bones.Count; i++) if (_bones[i] != null) _bones[i].localScale = _scales[i] * 0.0001f;
        }

        public void Show()
        {
            for (int i = 0; i < _bones.Count; i++) { try { if (_bones[i] != null) _bones[i].localScale = _scales[i]; } catch (Exception) { } }
            _bones.Clear(); _scales.Clear();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Something a recorded animation can drive itself (Characters' units or Vehicles' vehicles may implement it on their component for
    /// the game's own movement); without it HaloCinematic writes HaloUnit's control members, and moves anything else (vehicles, lab
    /// puppets) kinematically along the recording.
    /// </summary>
    public interface IHaloRecordedControl
    {
        /// <summary>Every game tick while a recording plays: the unit control of this tick (throttle in the unit's facing frame, facing /
        /// aiming / looking yaw-pitch in Halo world space, control flags, weapon index).</summary>
        void RecordedControl(HaloRecordedControl control);
        /// <summary>The recording ended or was killed (<paramref name="hover"/>: recording_play_and_hover - keep the vehicle hovering).</summary>
        void RecordedControlEnd(bool hover);
    }

    /// <summary>
    /// recording_play / recording_play_and_delete / recording_play_and_hover / recording_kill / recording_time: Halo's recorded unit control
    /// (<see cref="HaloRecording"/>) played on a unit at 30 ticks a second. A HaloUnit gets MoveVelocity (throttle x its own move speed:
    /// <see cref="HaloRecordingSpeed"/> - "alert" posture while the recording holds force-alert), LookPoint / AimPoint from the facing /
    /// aiming / looking directions, Crouching / Jump / Firing / Melee / ThrowGrenade from the control flags.
    /// </summary>
    public static partial class HaloCinematic
    {
        public enum RecordingEnd { Stop = 0, Delete = 1, Hover = 2 }

        sealed class RecPlay
        {
            public GameObject Target;
            public string Name;
            public HaloUnit Unit;
            public IHaloRecordedControl Custom;
            public HaloVehicle Vehicle;
            public HaloRecordedPuppet Puppet;
            public HaloRecordingPlayback Playback;
            public RecordingEnd End;
            // kinematic movers (vehicles without their own recorded control, puppets): the last two ticks' poses
            public bool Kinematic, Flying, Ground;
            public Rigidbody Body; public bool BodyWasKinematic;
            public Vector3 PrevPos, Pos; public Quaternion PrevRot, Rot;
            public float Speed; // m per tick (vehicles: current speed)
            public float Side;  // m per tick (vehicles: current slide speed, left positive)
            public bool Plane; public HaloPlaneState PlaneState; // human planes: the shared recorded-flight model (HaloPlaneFlight)
        }

        static readonly List<RecPlay> _recs = new List<RecPlay>();
        static readonly RaycastHit[] _ground = new RaycastHit[8];

        /// <summary>recording_play: plays a recorded animation on a unit; returns false when it is missing.</summary>
        public static bool PlayRecording(HaloUnit unit, string recordedAnimation)
        {
            return unit != null && PlayRecording(unit.gameObject, recordedAnimation, RecordingEnd.Stop, unit.ObjectName);
        }

        /// <summary>recording_play (Stop), recording_play_and_delete (Delete: <paramref name="objectName"/> is destroyed through
        /// HaloObjects when it ends), recording_play_and_hover (Hover) on any unit object (biped, vehicle).</summary>
        public static bool PlayRecording(GameObject target, string recordedAnimation, RecordingEnd end, string objectName = null)
        {
            HaloCinemaView.Ensure();
            if (target == null) return false;
            EnsureSaveParts();
            var d = D;
            var rec = d != null ? d.Recording(recordedAnimation) : null;
            if (rec == null) { Once("rec?" + recordedAnimation, "Cinematic: no recorded animation \"" + recordedAnimation + "\""); return false; }
            KillRecording(target, false);
            var p = new RecPlay
            {
                Target = target, Name = objectName, End = end, Playback = new HaloRecordingPlayback(rec),
                Custom = target.GetComponent<IHaloRecordedControl>(), Unit = target.GetComponent<HaloUnit>(),
                Vehicle = target.GetComponent<HaloVehicle>(), Puppet = target.GetComponent<HaloRecordedPuppet>(),
            };
            if (string.IsNullOrEmpty(p.Name) && p.Unit != null) p.Name = p.Unit.ObjectName;
            if (p.Custom == null && p.Unit == null) StartKinematic(p);
            _recs.Add(p);
            Apply(p);
            Once("rec", "Cinematic: recording_play " + rec.Name + " (" + rec.Length + " ticks, " + rec.Events.Length + " events) on " + target.name
                        + " - driven by " + (p.Custom != null ? "its own recorded control" : p.Unit != null ? "HaloUnit controls" : p.Vehicle != null ? "a kinematic vehicle path (ours)" : "a kinematic path"));
            return true;
        }

        /// <summary>recording_kill.</summary>
        public static void KillRecording(GameObject target) { KillRecording(target, true); }

        static void KillRecording(GameObject target, bool finish)
        {
            for (int i = _recs.Count - 1; i >= 0; i--)
            {
                if (_recs[i].Target != target) continue;
                var p = _recs[i];
                _recs.RemoveAt(i);
                Stop(p, false);
            }
        }

        /// <summary>recording_time: ticks left in the unit's recording (0 when none plays).</summary>
        public static int RecordingTime(GameObject target)
        {
            for (int i = 0; i < _recs.Count; i++) if (_recs[i].Target == target) return _recs[i].Playback.Remaining;
            return 0;
        }

        /// <summary>A recording drives this object now (the AI leaves it alone, like Halo).</summary>
        public static bool IsRecording(GameObject target)
        {
            if (target == null) return false;
            for (int i = 0; i < _recs.Count; i++) if (_recs[i].Target == target) return true;
            return false;
        }

        public static int RecordingsPlaying => _recs.Count;

        /// <summary>The name of the recorded animation playing on this object (null when none).</summary>
        public static string RecordingName(GameObject target)
        {
            if (target == null) return null;
            for (int i = 0; i < _recs.Count; i++) if (_recs[i].Target == target) return _recs[i].Playback.Recording.Name;
            return null;
        }

        /// <summary>The recorded animation playing on this object (null when none) - a vehicle reads it ahead to plan its flight.</summary>
        public static HaloRecording PlayingRecording(GameObject target)
        {
            if (target == null) return null;
            for (int i = 0; i < _recs.Count; i++) if (_recs[i].Target == target) return _recs[i].Playback.Recording;
            return null;
        }

        static void KillAllRecordings()
        {
            for (int i = _recs.Count - 1; i >= 0; i--) { var p = _recs[i]; _recs.RemoveAt(i); Stop(p, false); }
        }

        static void RecordingsStep()
        {
            for (int i = _recs.Count - 1; i >= 0; i--)
            {
                var p = _recs[i];
                if (p.Target == null) { _recs.RemoveAt(i); continue; }
                bool more = p.Playback.Step();
                if (!more) { _recs.RemoveAt(i); Stop(p, true); continue; }
                Apply(p);
            }
        }

        static void Stop(RecPlay p, bool natural)
        {
            if (p.Target == null) return;
            try
            {
                bool hover = natural && p.End == RecordingEnd.Hover;
                if (p.Custom != null) p.Custom.RecordedControlEnd(hover);
                else if (p.Unit != null)
                {
                    var u = p.Unit;
                    u.MoveVelocity = Vector3.zero; u.Firing = false; u.Melee = false; u.ThrowGrenade = false; u.Jump = false; u.Crouching = false;
                }
                if (p.Kinematic && !hover)
                {
                    p.Target.transform.SetPositionAndRotation(p.Pos, p.Rot);
                    if (p.Body != null) p.Body.isKinematic = p.BodyWasKinematic;
                }
                if (natural && p.End == RecordingEnd.Delete)
                {
                    if (!string.IsNullOrEmpty(p.Name) && HaloObjects.Find(p.Name) == p.Target) HaloObjects.Destroy(p.Name);
                    else Object.Destroy(p.Target);
                }
            }
            catch (System.Exception e) { HaloLog.Error("Cinematic: ending recording on " + p.Target.name + ": " + e); }
        }

        static void Apply(RecPlay p)
        {
            var c = p.Playback.Control;
            if (p.Custom != null) { p.Custom.RecordedControl(c); return; }
            if (p.Unit != null) { ApplyUnit(p.Unit, ref c); return; }
            if (p.Kinematic) StepKinematic(p, ref c);
        }

        /// <summary>A Unity direction from a recording's yaw / pitch (world root rotation applied).</summary>
        public static Vector3 RecordedDirection(int yaw, int pitch)
        {
            Vector3 d = HaloSpace.Dir(HaloRecordedControl.Direction(yaw, pitch));
            var root = HaloSession.WorldRoot;
            return root != null ? root.rotation * d : d;
        }

        static void ApplyUnit(HaloUnit u, ref HaloRecordedControl c)
        {
            Vector3 face = RecordedDirection(c.FacingYaw, 0);
            Vector3 left = Vector3.Cross(face, Vector3.up);
            float speed = HaloRecordingSpeed.WorldUnitsPerTick(u.UnitTag, c.Has(HaloRecordedControl.ForceAlert), c.Has(HaloRecordedControl.Crouch), c.Weapon < 0 ? "unarmed" : "rifle")
                          * HaloSpace.Unit * TicksPerSecond;
            u.MoveVelocity = (face * c.ThrottleForward + left * c.ThrottleLeft) * speed;
            Vector3 eye = u.Eye;
            u.LookPoint = eye + RecordedDirection(c.LookYaw, c.LookPitch) * 20f;
            u.AimPoint = eye + RecordedDirection(c.AimYaw, c.AimPitch) * 20f;
            u.Crouching = c.Has(HaloRecordedControl.Crouch);
            u.Jump = c.Has(HaloRecordedControl.JumpBit);
            u.Firing = c.Has(HaloRecordedControl.PrimaryTrigger);
            u.Melee = c.Has(HaloRecordedControl.MeleeBit);
            u.ThrowGrenade = c.Has(HaloRecordedControl.GrenadeBit);
        }

        // ------------------------------------------------------------------ kinematic movers (vehicles without their own control, puppets)

        static void StartKinematic(RecPlay p)
        {
            p.Kinematic = true;
            var t = p.Target.transform;
            p.Pos = p.PrevPos = t.position; p.Rot = p.PrevRot = t.rotation;
            p.Body = p.Target.GetComponent<Rigidbody>();
            if (p.Body != null) { p.BodyWasKinematic = p.Body.isKinematic; p.Body.isKinematic = true; }
            if (p.Vehicle != null && p.Vehicle.Def != null)
            {
                var k = p.Vehicle.Def.Type;
                // only alien fighters fly along their facing pitch; human planes fly the shared recorded-flight model (HaloPlaneFlight, as
                // HaloVehicle's own recorded control - without its collision sweep)
                p.Flying = k == HaloVehicleDef.Kind.AlienFighter;
                p.Plane = k == HaloVehicleDef.Kind.HumanPlane;
                p.Ground = !p.Flying && !p.Plane;
                if (p.Plane)
                {
                    var root = HaloSession.WorldRoot;
                    Vector3 h = HaloSpace.DirToHalo(root != null ? Quaternion.Inverse(root.rotation) * p.Rot * Vector3.forward : p.Rot * Vector3.forward);
                    p.PlaneState = HaloPlaneFlight.Start(Mathf.Atan2(h.y, h.x));
                }
            }
            else p.Ground = p.Puppet == null || !p.Puppet.Flying;
            p.Speed = 0f; p.Side = 0f;
        }

        static void StepKinematic(RecPlay p, ref HaloRecordedControl c)
        {
            p.PrevPos = p.Pos; p.PrevRot = p.Rot;
            Vector3 face = RecordedDirection(c.FacingYaw, p.Flying ? c.FacingPitch : 0);
            Vector3 flat = new Vector3(face.x, 0f, face.z);
            if (flat.sqrMagnitude < 1e-6f) flat = p.Rot * Vector3.forward;
            flat.Normalize();
            Vector3 left = Vector3.Cross(flat, Vector3.up);
            Vector3 move;
            if (p.Plane && p.Vehicle != null && p.Vehicle.Def != null)
            {
                HaloPlaneFlight.Step(p.Vehicle.Def, ref p.PlaneState, ref c);
                var root = HaloSession.WorldRoot; Quaternion rr = root != null ? root.rotation : Quaternion.identity;
                Vector3 hd = rr * HaloSpace.Dir((float)System.Math.Cos(p.PlaneState.Yaw), (float)System.Math.Sin(p.PlaneState.Yaw), 0f);
                move = rr * HaloSpace.Dir(p.PlaneState.Vx, p.PlaneState.Vy, p.PlaneState.Vz) * HaloSpace.Unit;
                p.Rot = Quaternion.LookRotation(hd, Vector3.up);
            }
            else if (p.Vehicle != null && p.Vehicle.Def != null)
            {
                var def = p.Vehicle.Def;
                // m per tick: speed acceleration while speeding up, speed deceleration while slowing down; the slide speed separately
                float u = HaloSpace.Unit;
                float top = (c.ThrottleForward >= 0f ? def.MaxForward : def.MaxReverse) * c.ThrottleForward * u;
                float acc = (Mathf.Abs(top) > Mathf.Abs(p.Speed) ? def.SpeedAcceleration : def.SpeedDeceleration) * u;
                p.Speed = acc > 0f ? Mathf.MoveTowards(p.Speed, top, acc) : top;
                float side = c.ThrottleLeft * (c.ThrottleLeft >= 0f ? def.MaxLeftSlide : def.MaxRightSlide) * u;
                float sacc = (Mathf.Abs(side) > Mathf.Abs(p.Side) ? def.SlideAcceleration : def.SlideDeceleration) * u;
                p.Side = sacc > 0f ? Mathf.MoveTowards(p.Side, side, sacc) : side;
                move = (p.Flying ? face : flat) * p.Speed + left * p.Side;
                p.Rot = p.Flying ? Quaternion.LookRotation(face, Vector3.up) : Quaternion.LookRotation(flat, Vector3.up);
            }
            else
            {
                HaloTag unitTag = p.Puppet != null ? p.Puppet.UnitTag : null;
                float speed = HaloRecordingSpeed.WorldUnitsPerTick(unitTag, c.Has(HaloRecordedControl.ForceAlert), c.Has(HaloRecordedControl.Crouch), c.Weapon < 0 ? "unarmed" : "rifle") * HaloSpace.Unit;
                if (speed <= 0f) speed = 0.0225f * HaloSpace.Unit; // ours: a walking crewman when the unit has no move animation
                move = (flat * c.ThrottleForward + left * c.ThrottleLeft) * speed;
                p.Rot = Quaternion.LookRotation(flat, Vector3.up);
                if (p.Puppet != null) p.Puppet.Control = c;
            }
            Vector3 next = p.Pos + move;
            if (p.Ground)
            {
                // follow the floor (Halo's units and ground vehicles stay on the BSP)
                int n = Physics.RaycastNonAlloc(next + Vector3.up * 0.9f, Vector3.down, _ground, 2.5f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue; Vector3 hitP = next;
                for (int i = 0; i < n; i++)
                {
                    if (_ground[i].collider == null || _ground[i].collider.transform.IsChildOf(p.Target.transform)) continue;
                    if (_ground[i].distance < best) { best = _ground[i].distance; hitP = _ground[i].point; }
                }
                if (best < float.MaxValue) next.y = hitP.y;
            }
            p.Pos = next;
        }

        /// <summary>Per frame: kinematic movers between their last two tick poses.</summary>
        internal static void RecordingsFrame()
        {
            float f = TickFraction;
            for (int i = 0; i < _recs.Count; i++)
            {
                var p = _recs[i];
                if (!p.Kinematic || p.Target == null) continue;
                p.Target.transform.SetPositionAndRotation(Vector3.Lerp(p.PrevPos, p.Pos, f), Quaternion.Slerp(p.PrevRot, p.Rot, f));
            }
        }
    }

    /// <summary>
    /// A stand-in unit a recording moves kinematically (the lab's crewman before Characters' units walk; any object that should follow a
    /// recording without physics). <see cref="UnitTag"/> gives the move speed (its animation graph), <see cref="Control"/> the last tick.
    /// </summary>
    public sealed class HaloRecordedPuppet : MonoBehaviour
    {
        public HaloTag UnitTag;
        public bool Flying;
        public HaloRecordedControl Control;
    }
}

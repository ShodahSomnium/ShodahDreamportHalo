using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// One-shot animation requests for the AI and script modules (Halo's actor gestures, dives / evades, custom animations of scripts and
    /// cinematics). All return false when the unit's graph has no such animation (or the unit is dead / busy).
    /// </summary>
    public partial class HaloUnit : IHaloRecordedControl
    {
        /// <summary>A class gesture by Halo's weapon-class animation index: 27 surprise-front, 28 surprise-back, 29 berserk, 32 signal-move,
        /// 33 signal-attack, 34 warn, 40 celebrate, 41 panic, 23 ready, 24 put-away ... (HaloAnimIds.Weapon).</summary>
        public bool Gesture(int classAnimation) { return Alive && Animator != null && Animator.Gesture(classAnimation); }

        /// <summary>A dive (0 front, 1 back, 2 left, 3 right): the animation's own root motion carries the unit.</summary>
        public bool Dive(int direction) { return Alive && Animator != null && Seat == null && Animator.Dive(direction, false); }

        /// <summary>An elite's / grunt's evade to the left or right.</summary>
        public bool Evade(bool right) { return Alive && Animator != null && Seat == null && Animator.Dive(right ? 3 : 2, true); }

        /// <summary>
        /// Scripts' custom_animation: plays <paramref name="animationName"/> of the graph <paramref name="graph"/> (the unit's own when null)
        /// over the unit's own animations; its root motion moves the unit; the unit's animations come back at its end. False when missing.
        /// </summary>
        public bool CustomAnimation(HaloTag graph, string animationName, bool interpolate, bool loop = false, bool hold = false)
        {
            if (Animator == null || string.IsNullOrEmpty(animationName)) return false;
            HaloAnimGraph g = graph != null ? HaloBipeds.GraphOf(graph) : Animator.Graph;
            if (g == null) return false;
            HaloAnimation a = g.Get(animationName);
            if (a == null) { Once("custom " + animationName, "Characters: " + Def.Name + ": no animation '" + animationName + "' in " + g.Tag.Name); return false; }
            bool ok = Animator.PlayCustom(g, a, interpolate, loop, hold);
            if (!ok) Once("custom " + animationName, "Characters: " + Def.Name + ": animation '" + animationName + "' of " + g.Tag.Name + " cannot be played (" + (a.Error ?? "?") + ")");
            return ok;
        }

        public bool PlayingCustomAnimation => Animator != null && Animator.CustomPlaying;
        public void StopCustomAnimation(bool interpolate = true) { if (Animator != null) Animator.StopCustom(interpolate); }

        // ------------------------------------------------------------------ recorded animations (cutscene recordings: Cinematics drives the controls)

        private bool _recorded;
        private float _recordedYaw;
        private string _stanceBeforeRecording;

        /// <summary>A recording tick (HaloCinematic.PlayRecording): the body faces exactly the recorded facing, moves by the throttle at its
        /// own animation pace, looks / aims / crouches / jumps / fires / melees / throws as recorded; "force alert" plays the alert seat.</summary>
        public void RecordedControl(HaloRecordedControl c)
        {
            if (!_recorded) { _recorded = true; _stanceBeforeRecording = Stance; }
            Vector3 face = HaloCinematic.RecordedDirection(c.FacingYaw, 0);
            face.y = 0f;
            if (face.sqrMagnitude > 1e-6f) _recordedYaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            Vector3 f = Quaternion.Euler(0f, _recordedYaw, 0f) * Vector3.forward;
            Vector3 left = Vector3.Cross(f, Vector3.up);
            Stance = c.Has(HaloRecordedControl.ForceAlert) && Animator != null && Animator.Graph.FindSeat("alert") != null ? "alert" : _stanceBeforeRecording;
            Crouching = c.Has(HaloRecordedControl.Crouch);
            Vector3 throttle = f * c.ThrottleForward + left * c.ThrottleLeft;
            MoveVelocity = throttle * NaturalSpeedToward(throttle);      // (strafing / backing up at those clips' own pace)
            Vector3 eye = Eye;
            LookPoint = eye + HaloCinematic.RecordedDirection(c.LookYaw, c.LookPitch) * 20f;
            AimPoint = eye + HaloCinematic.RecordedDirection(c.AimYaw, c.AimPitch) * 20f;
            Jump = c.Has(HaloRecordedControl.JumpBit);
            Firing = c.Has(HaloRecordedControl.PrimaryTrigger);
            if (c.Has(HaloRecordedControl.MeleeBit)) Melee = true;
            if (c.Has(HaloRecordedControl.GrenadeBit)) ThrowGrenade = true;
        }

        public void RecordedControlEnd(bool hover)
        {
            if (!_recorded) return;
            _recorded = false;
            Stance = _stanceBeforeRecording;
            MoveVelocity = Vector3.zero;
            Firing = false; Crouching = false; Jump = false;
            LookPoint = Vector3.zero; AimPoint = Vector3.zero;
        }

        private static readonly System.Collections.Generic.HashSet<string> OnceKeys = new System.Collections.Generic.HashSet<string>();
        private static void Once(string key, string msg) { if (OnceKeys.Add(key)) HaloLog.Warn(msg); }
    }
}

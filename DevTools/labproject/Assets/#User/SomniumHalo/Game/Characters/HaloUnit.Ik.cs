using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Halo's weapon IK: the animation class lists IK points (the unit's "left hand" marker attaches to the weapon's "left hand marine" /
    /// "left hand elite" marker); after the pose the hand's two-bone chain (upper arm, forearm, hand) is turned so that marker meets the
    /// weapon's, the hand taking the weapon marker's orientation. Off (faded) while an action has the hand elsewhere (reload, throw, melee,
    /// gestures), when dead or when the weapon has no such marker.
    /// </summary>
    public partial class HaloUnit
    {
        private Transform _ikMarker, _ikHand, _ikFore, _ikUpper, _ikTarget;
        private HaloAnimGraph.WeaponClass _ikClass;
        private string _ikLabel;
        private float _ikW;

        private void SetupIk()
        {
            _ikMarker = _ikHand = _ikFore = _ikUpper = _ikTarget = null;
            _ikClass = Animator != null ? Animator.Class : null;
            _ikLabel = WeaponLabel;
            if (_ikClass == null || _weaponBuilt == null || Built == null) return;
            for (int i = 0; i < _ikClass.Ik.Count; i++)
            {
                HaloAnimGraph.IkPoint p = _ikClass.Ik[i];
                if (string.IsNullOrEmpty(p.Marker) || string.IsNullOrEmpty(p.AttachTo)) continue;
                Transform m = Marker(p.Marker), t = _weaponBuilt.Marker(p.AttachTo);
                if (m == null || t == null || m.parent == null || m.parent.parent == null || m.parent.parent.parent == null) continue;
                _ikMarker = m; _ikTarget = t;
                _ikHand = m.parent; _ikFore = _ikHand.parent; _ikUpper = _ikFore.parent;
                return;
            }
        }

        private void ApplyIk(float dt)
        {
            if (Animator == null) return;
            if (Animator.Class != _ikClass || WeaponLabel != _ikLabel) SetupIk();
            if (_ikTarget == null) return;
            HaloUnitAnimator.ActionKind act = Animator.Action;
            bool free = act == HaloUnitAnimator.ActionKind.Reload || act == HaloUnitAnimator.ActionKind.Throw || act == HaloUnitAnimator.ActionKind.Melee ||
                        act == HaloUnitAnimator.ActionKind.Gesture || act == HaloUnitAnimator.ActionKind.Ready || Animator.CustomPlaying;
            _ikW = Mathf.MoveTowards(_ikW, Alive && !free ? 1f : 0f, dt * 6f);
            if (_ikW <= 0.001f) return;

            // where the hand node must go: the marker's offset from the hand kept, the marker onto the weapon's
            Quaternion markerInHand = Quaternion.Inverse(_ikHand.rotation) * _ikMarker.rotation;
            Vector3 markerOff = _ikMarker.position - _ikHand.position;
            Quaternion handWant = _ikTarget.rotation * Quaternion.Inverse(markerInHand);
            Vector3 offWant = handWant * (Quaternion.Inverse(_ikHand.rotation) * markerOff);
            Vector3 target = _ikTarget.position - offWant;
            if ((target - _ikHand.position).sqrMagnitude > 0.36f) return;      // more than 60 cm off: not this pose's grip (ours)

            Quaternion upper0 = _ikUpper.rotation, fore0 = _ikFore.rotation, hand0 = _ikHand.rotation;
            TwoBone(_ikUpper, _ikFore, _ikHand, target);
            _ikHand.rotation = handWant;
            if (_ikW < 0.999f)
            {
                _ikUpper.rotation = Quaternion.Slerp(upper0, _ikUpper.rotation, _ikW);
                _ikFore.rotation = Quaternion.Slerp(fore0, _ikFore.rotation, _ikW);
                _ikHand.rotation = Quaternion.Slerp(hand0, _ikHand.rotation, _ikW);
            }
        }

        /// <summary>Analytic two-bone IK keeping the current bend plane: the elbow angle for the reach, then the chain swung onto the target.</summary>
        private static void TwoBone(Transform upper, Transform fore, Transform hand, Vector3 target)
        {
            Vector3 a = upper.position, b = fore.position, c = hand.position;
            float la = (b - a).magnitude, lb = (c - b).magnitude;
            if (la < 1e-4f || lb < 1e-4f) return;
            float d = Mathf.Clamp((target - a).magnitude, Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f);
            // the elbow's interior angle now and wanted
            float now = Vector3.Angle(a - b, c - b);
            float want = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - d * d) / (2f * la * lb), -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 axis = Vector3.Cross(b - a, c - b);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(b - a, Vector3.up);
            if (axis.sqrMagnitude < 1e-8f) return;
            axis.Normalize();
            fore.rotation = Quaternion.AngleAxis(now - want, axis) * fore.rotation;
            // swing the whole chain so the hand lies along the target direction
            Vector3 c2 = hand.position;
            upper.rotation = Quaternion.FromToRotation(c2 - a, target - a) * upper.rotation;
        }
    }
}

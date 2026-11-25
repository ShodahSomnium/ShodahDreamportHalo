using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Where the local player's body is, for hanging weapons on it and holding them in the hands. Read only: the avatar is never modified and
    /// nothing is parented under it (WoW's GearBody rules). In VR the hands are the Bridge's Body.RightHand / LeftHand (the controllers'
    /// grip poses); the torso is built from the head (feet at the avatar root, the neck under the head, the chest turning after the head
    /// with the lag a body has). On a monitor there are no hand transforms: the hands are placed in front of the camera.
    /// </summary>
    public sealed class HaloBody
    {
        // Torso frame (world), refreshed by Update.
        public Vector3 Up = Vector3.up, Right = Vector3.right, Forward = Vector3.forward;
        public Vector3 Chest, Hips, ShoulderL, ShoulderR, Feet, HeadPos;
        public Quaternion HeadRot = Quaternion.identity;
        public float Scale = 1f;
        public bool Valid;

        /// <summary>A headset is running and the avatar's hand transforms exist.</summary>
        public bool Vr { get; private set; }

        /// <summary>Lab hook (null in the world): stand in for the Bridge player's root, head and hands so the weapons can be run without the client.</summary>
        public Transform OverrideRoot, OverrideHead, OverrideLeft, OverrideRight;

        /// <summary>Monitor play: the right hand's blend towards the eye (0 hip, 1 aiming down the sights).</summary>
        public float DesktopAim;

        /// <summary>Monitor play: where the gun hand rests in the eye's frame (right, down, forward; metres).</summary>
        public static Vector3 DesktopHip = new Vector3(0.13f, -0.16f, 0.26f);

        private float _yaw;
        private bool _haveYaw;
        private const int Samples = 12;
        private readonly Vector3[,] _pos = new Vector3[2, Samples];
        private readonly float[,] _time = new float[2, Samples];
        private int _head;
        private int _filled;
        private int _velFrame = -1;
        private readonly Vector3[] _vel = new Vector3[2];

        public bool Update()
        {
            Valid = false;
            Transform root = OverrideRoot != null ? OverrideRoot : HaloPlayer.Root;
            Transform l = OverrideLeft != null ? OverrideLeft : HaloPlayer.LeftHand;
            Transform r = OverrideRight != null ? OverrideRight : HaloPlayer.RightHand;
            Vr = l != null && r != null;
            Transform head = OverrideHead != null ? OverrideHead : HaloPlayer.HeadBone;
            Camera cam = HaloPlayer.Head();
            // on a monitor the eye is the camera (the avatar's head bone does not look up and down with the mouse)
            if (cam != null && (head == null || (!Vr && OverrideHead == null))) head = cam.transform;
            if (head == null) return false;
            HeadPos = head.position;
            HeadRot = head.rotation;
            Feet = root != null ? root.position : new Vector3(HeadPos.x, HeadPos.y - 1.65f, HeadPos.z);

            Up = Vector3.up;
            Vector3 hf = head.forward;
            hf.y = 0f;
            if (hf.sqrMagnitude < 1e-4f) { hf = head.up * (head.forward.y < 0f ? 1f : -1f); hf.y = 0f; }
            float hy = Mathf.Atan2(hf.x, hf.z) * Mathf.Rad2Deg;
            if (!_haveYaw) { _yaw = hy; _haveYaw = true; }
            // the torso follows the head's yaw, lagging: it turns once the head is 25 degrees away, at up to 180 degrees a second
            float d = Mathf.DeltaAngle(_yaw, hy);
            if (Mathf.Abs(d) > 25f) _yaw += Mathf.Clamp(d - Mathf.Sign(d) * 25f, -180f * Time.deltaTime, 180f * Time.deltaTime);
            Forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            Right = Vector3.Cross(Up, Forward);

            // (a monitor: the size of the avatar from its head bone, not from the camera)
            Transform bone = !Vr && OverrideHead == null ? HaloPlayer.HeadBone : null;
            float height = Mathf.Max(0.8f, (bone != null ? bone.position.y : HeadPos.y) - Feet.y);
            Scale = Mathf.Clamp(height / 1.7f, 0.6f, 1.5f);
            TrackStandingHeight(height);
            Vector3 neck = HeadPos - Up * (0.2f * Scale);
            Vector3 spine = neck - Forward * (0.05f * Scale);
            Chest = spine - Up * (0.2f * Scale);
            Hips = new Vector3(spine.x, Feet.y + height * 0.55f, spine.z);
            ShoulderL = Chest + Up * (0.1f * Scale) - Right * (0.18f * Scale);
            ShoulderR = Chest + Up * (0.1f * Scale) + Right * (0.18f * Scale);

            Valid = true;
            RecordHands();
            return true;
        }

        /// <summary>
        /// The Bridge's hand anchors are the controller's GRIP pose: its forward runs out of the TOP of the fist, about 90 degrees off from where the controller
        /// points (the Skyrim Dreamport's "spells are cast 90 degrees upwards", the Rec Room Dreamport's GripToAim). Everything of ours works in the AIM frame
        /// of a hand (forward = out of the front of the fist, up = toward the thumb, right = to the user's right, for both hands), so the grip pose is pitched
        /// by this (a rotation of 90 degrees about X: the aim direction is the grip's -Y). The lab's stand-in hands are already in the aim frame.
        /// </summary>
        public static readonly Quaternion GripToAim = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>The hands' pointing direction measured from the client's laser (see <see cref="HaloLaserAim"/>); GripToAim is only the estimate used until it is measured.</summary>
        public readonly HaloLaserAim Laser = new HaloLaserAim();

        /// <summary>The avatar's size against a 1.7 m player: the guns and gear are this big (a small avatar gets small guns). Taken from the highest the head has been in the last 20 s, so crouching does not shrink them.</summary>
        public float AvatarScale = 1f;
        private readonly float[] _standing = new float[20];
        private int _standingAt;
        private float _standingNext;
        private bool _standingFilled;

        private void TrackStandingHeight(float height)
        {
            if (!_standingFilled) { for (int i = 0; i < _standing.Length; i++) _standing[i] = height; _standingFilled = true; _standingNext = Time.unscaledTime + 1f; }
            if (Time.unscaledTime >= _standingNext) { _standingNext = Time.unscaledTime + 1f; _standingAt = (_standingAt + 1) % _standing.Length; _standing[_standingAt] = height; }
            else if (height > _standing[_standingAt]) _standing[_standingAt] = height;
            float max = height;
            for (int i = 0; i < _standing.Length; i++) if (_standing[i] > max) max = _standing[i];
            AvatarScale = Mathf.Lerp(AvatarScale, Mathf.Clamp(max / 1.7f, 0.5f, 1.5f), 0.05f);
        }

        /// <summary>A hand's pose in the aim frame: the controller anchor in VR (grip pose turned to the aim frame); on a monitor a pose in front of the camera.</summary>
        public void HandPose(bool right, out Vector3 pos, out Quaternion rot)
        {
            Transform over = right ? OverrideRight : OverrideLeft;
            Transform t = over != null ? over : (right ? HaloPlayer.RightHand : HaloPlayer.LeftHand);
            if (t != null && Vr)
            {
                pos = t.position;
                if (over != null) rot = t.rotation;
                else
                {
                    // the client's laser says where the hand points; until it is measured the grip pose is turned by the estimate
                    Quaternion aim;
                    rot = Laser.TryAim(right, t, out aim) ? aim : t.rotation * GripToAim;
                }

                return;
            }

            // monitor: a view model, locked to the eye as in the game (it turns up and down with the view), low on the right, drawn up to the eye when aiming
            Quaternion look = HeadRot;
            if (right)
            {
                Vector3 hip = HeadPos + look * DesktopHip;
                Vector3 aim = HeadPos + look * new Vector3(0.0f, -0.07f, 0.42f);
                pos = Vector3.Lerp(hip, aim, DesktopAim);
                rot = look;
            }
            else
            {
                Vector3 front = HeadPos + look * new Vector3(-0.06f, -0.2f, 0.62f);
                pos = front;
                rot = look;
            }
        }

        /// <summary>The hand's velocity in m/s over the last 0.1 s (for throwing).</summary>
        public Vector3 HandVelocity(bool right)
        {
            return _vel[right ? 1 : 0];
        }

        private void RecordHands()
        {
            if (_velFrame == Time.frameCount) return;
            _velFrame = Time.frameCount;
            _head = (_head + 1) % Samples;
            if (_filled < Samples) _filled++;
            for (int h = 0; h < 2; h++)
            {
                Vector3 p;
                Quaternion q;
                HandPose(h == 1, out p, out q);
                _pos[h, _head] = p;
                _time[h, _head] = Time.time;
                // the oldest sample not older than 0.1 s
                int best = _head;
                for (int k = 1; k < _filled; k++)
                {
                    int i = (_head - k + Samples) % Samples;
                    if (Time.time - _time[h, i] > 0.1f) break;
                    best = i;
                }

                float dt = _time[h, _head] - _time[h, best];
                _vel[h] = dt > 0.015f ? (_pos[h, _head] - _pos[h, best]) / dt : Vector3.zero;
            }
        }
    }
}

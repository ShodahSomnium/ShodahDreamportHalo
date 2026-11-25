using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The Skyrim Dreamport's GearPoser as BO2's Bo2PuppetPoser ported it, on Halo: poses one player's Somnium avatar (humanoid bones, transforms only,
    /// no SetHumanPose) from the cyborg puppet's sample after the client animated it (HaloDeskBody, LateUpdate 9996). The client's animation is the
    /// base: every frame the bones are blended from what the Animator wrote toward the game's pose (in 0.2 s, out 0.3 s); a bone the Animator did not
    /// write again (a culled Animator) is put back first, so nothing piles up.
    /// - legs and hips only while the avatar stands (planar speed &lt; 0.25 m/s full, &gt; 0.6 m/s none, airborne or crouched none): walking keeps the client's legs;
    /// - the torso toward the game's twist / bend / lean (TorsoShare) plus a share of the aim's pitch and of the yaw to the aim; the head HeadShare of the
    ///   game's turn; none of this (nor the hips) for the OWN avatar in first person (the client's camera rides its head);
    /// - both arms by two-bone IK onto the game's hands in the AIM frame (the aim's yaw and pitch at the shoulders), the avatar's arm length for the
    ///   cyborg's;
    /// - the weapon sits where Halo attaches it, on the right hand's "right hand" marker (from the posed right hand: the game's grip), turned onto the
    ///   crosshair's point while the game's pose aims (AimWeight); the left hand where Halo IKs it, the weapon's "left hand cyborg" marker, whenever the
    ///   animation's own left hand is at the gun (a reload takes it away); the fingers curl round the grip.
    /// Seated (<see cref="ApplySeated"/>): the hips on the seat marker, legs, torso and (not in VR) the arms from the seat animation. No allocation per frame.
    /// </summary>
    public sealed class HaloPuppetPoser
    {
        private const int Spine = 0, Chest = 1, UpperChest = 2, Head = 3, LUpper = 4, LLower = 5, LHand = 6, RUpper = 7, RLower = 8, RHand = 9;
        private const int Hips = 10, Neck = 11, LThigh = 12, LShin = 13, LFoot = 14, LToes = 15, RThigh = 16, RShin = 17, RFoot = 18, RToes = 19, LFing = 20, RFing = 32, LThumb = 44, RThumb = 47, Count = 50;
        private static readonly HumanBodyBones[] Ids = MakeIds();

        public const float BlendIn = 0.2f, BlendOut = 0.3f, StillFull = 0.25f, StillNone = 0.6f, HeadShare = 0.3f, TorsoShare = 0.7f, MaxHipsYaw = 45f;
        public const float PitchShare = 0.5f, YawShare = 0.6f, MaxPitch = 70f, MaxYaw = 85f, MaxAimFix = 60f;      // ours (BO2's limits)
        /// <summary>The left hand takes the gun where Halo IKs it while the animation's own left hand is within this of that point (cyborg metres), not beyond the second.</summary>
        public const float IkNear = 0.14f, IkFar = 0.3f;      // ours

        private sealed class Bone { public Transform T; public Quaternion Base, Written; public bool Wrote; }

        private readonly Bone[] _b = new Bone[Count];
        private Animator _anim;
        private Avatar _boundAvatar;
        private Vector3 _hipsBase, _hipsWritten;
        private bool _hipsWrote, _legs, _haveRoot;
        private float _w, _ankleH = -1f, _legLen = -1f;
        private Vector3 _lastRoot;
        private HaloPuppetSample _last;

        /// <summary>The avatar's root velocity (m/s, smoothed), its arm length (m), whether bones were written this frame, the wrists' distance from their targets.</summary>
        public Vector3 RootVelocity { get; private set; }
        public float ArmLength { get; private set; }
        public float LegLength { get { return _legLen; } }
        public bool Posed { get; private set; }
        public float Weight { get { return _w; } }
        public float LastRightErr, LastLeftErr, LeftOnGun, AimFix;
        public bool HasFingers { get; private set; }
        /// <summary>First person (own avatar): set before each Apply - the weapon origin wanted in the view (world); FpWeight blends it in / out in 0.25 s.</summary>
        public bool ViewSet;
        public Vector3 ViewGun;
        public Quaternion ViewRot = Quaternion.identity;
        public float FpWeight;
        /// <summary>Third person: the whole hold this much lower (metres, aim frame) - ours.</summary>
        public static float HoldDrop = 0.06f;
        public bool HasLegs { get { return _legs; } }
        public Animator Anim { get { return _anim; } }
        public Transform HeadBone { get { return _b[Head].T; } }
        public Transform HipsBone { get { return _b[Hips].T; } }
        public Transform UpperArm(bool right) { return _b[right ? RUpper : LUpper].T; }

        public HaloPuppetPoser()
        {
            for (int i = 0; i < Count; i++) _b[i] = new Bone();
        }

        private static HumanBodyBones[] MakeIds()
        {
            var ids = new HumanBodyBones[Count];
            HumanBodyBones[] body =
            {
                HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                HumanBodyBones.Hips, HumanBodyBones.Neck,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
                HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes
            };
            System.Array.Copy(body, ids, body.Length);
            for (int i = 0; i < 12; i++)
            {
                ids[LFing + i] = (HumanBodyBones)((int)HumanBodyBones.LeftIndexProximal + i);      // index, middle, ring, little x proximal, intermediate, distal
                ids[RFing + i] = (HumanBodyBones)((int)HumanBodyBones.RightIndexProximal + i);
            }

            for (int i = 0; i < 3; i++)
            {
                ids[LThumb + i] = (HumanBodyBones)((int)HumanBodyBones.LeftThumbProximal + i);
                ids[RThumb + i] = (HumanBodyBones)((int)HumanBodyBones.RightThumbProximal + i);
            }

            return ids;
        }

        /// <summary>Still bound to this Animator and its skeleton.</summary>
        public bool Bound(Animator a)
        {
            return a != null && a == _anim && a.avatar == _boundAvatar && !Gone(_b[Hips].T) && !Gone(_b[RHand].T) && !Gone(_b[LHand].T);
        }

        private static bool Gone(Transform t) { return !ReferenceEquals(t, null) && t == null; }

        /// <summary>Binds the avatar's bones (gives the old ones back first). False without arms. say = the log text of what was found.</summary>
        public bool Bind(Animator a, out string say)
        {
            Release();
            for (int i = 0; i < Count; i++) { _b[i].T = null; _b[i].Wrote = false; }
            _anim = null;
            _boundAvatar = null;
            _ankleH = _legLen = -1f;
            _haveRoot = false;
            _w = 0f;
            say = "no humanoid Animator";
            if (a == null || !a.isHuman || a.avatar == null) return false;
            for (int i = 0; i < Count; i++) _b[i].T = a.GetBoneTransform(Ids[i]);
            bool arms = _b[RUpper].T != null && _b[RLower].T != null && _b[RHand].T != null && _b[LUpper].T != null && _b[LLower].T != null && _b[LHand].T != null;
            _legs = _b[Hips].T != null && _b[LThigh].T != null && _b[LShin].T != null && _b[LFoot].T != null && _b[RThigh].T != null && _b[RShin].T != null && _b[RFoot].T != null;
            if (_legs)
            {
                float l = (_b[LShin].T.position - _b[LThigh].T.position).magnitude + (_b[LFoot].T.position - _b[LShin].T.position).magnitude;
                float r = (_b[RShin].T.position - _b[RThigh].T.position).magnitude + (_b[RFoot].T.position - _b[RShin].T.position).magnitude;
                _legLen = 0.5f * (l + r);
                if (_legLen < 0.2f) _legs = false;
            }

            HasFingers = _b[LFing].T != null && _b[LFing + 3].T != null && _b[LFing + 9].T != null && _b[RFing].T != null && _b[RFing + 3].T != null && _b[RFing + 9].T != null;
            say = "arms " + arms + ", spine " + (_b[Spine].T != null) + ", chest " + (_b[Chest].T != null || _b[UpperChest].T != null) + ", fingers " + HasFingers + ", legs " + _legs +
                  (_legs ? " (leg " + _legLen.ToString("0.00") + " m)" : "") + ", neck " + (_b[Neck].T != null);
            if (!arms) return false;
            _anim = a;
            _boundAvatar = a.avatar;
            ArmLength = ArmLengthBoth();
            return true;
        }

        /// <summary>Once per frame before the puppet steps: what the Animator wrote this frame becomes the base (a bone it left alone carries our last pose: put back first); the root's velocity.</summary>
        public void Begin(float dt)
        {
            if (_anim == null) return;
            for (int i = 0; i < Count; i++)
            {
                Bone b = _b[i];
                if (b.T == null) continue;
                if (b.Wrote && Mathf.Abs(Quaternion.Dot(b.T.localRotation, b.Written)) > 0.99999f) b.T.localRotation = b.Base;
                b.Wrote = false;
                b.Base = b.T.localRotation;
            }

            Transform h = _b[Hips].T;
            if (h != null)
            {
                if (_hipsWrote && (h.localPosition - _hipsWritten).sqrMagnitude < 1e-10f) h.localPosition = _hipsBase;
                _hipsWrote = false;
                _hipsBase = h.localPosition;
            }

            Vector3 rp = _anim.transform.position;
            if (_haveRoot && dt > 1e-5f)
            {
                Vector3 v = (rp - _lastRoot) / dt;
                if (v.sqrMagnitude < 3600f) RootVelocity = Vector3.Lerp(RootVelocity, v, 1f - Mathf.Exp(-dt * 10f));
            }

            _lastRoot = rp;
            _haveRoot = true;
            ArmLength = ArmLengthBoth();
        }

        /// <summary>The head bone's height above the avatar's root (m), -1 without one.</summary>
        public float HeadHeight
        {
            get
            {
                Transform h = _b[Head].T;
                return h != null && _anim != null ? h.position.y - _anim.transform.position.y : -1f;
            }
        }

        /// <summary>Puts back at once every bone that still carries our pose.</summary>
        public void Release()
        {
            _w = 0f;
            for (int i = 0; i < Count; i++)
            {
                Bone b = _b[i];
                if (b.T != null && b.Wrote && Mathf.Abs(Quaternion.Dot(b.T.localRotation, b.Written)) > 0.99999f) b.T.localRotation = b.Base;
                b.Wrote = false;
            }

            Transform h = _b[Hips].T;
            if (h != null && _hipsWrote && (h.localPosition - _hipsWritten).sqrMagnitude < 1e-10f) h.localPosition = _hipsBase;
            _hipsWrote = false;
            Posed = false;
        }

        /// <summary>
        /// Poses the avatar from the sample and returns where the weapon goes (its origin and gun frame: +z barrel, +y up). aim = where the player looks;
        /// aimPoint = what the crosshair is on; torso = the spine, head and hips may move (false for the own avatar in first person); crouch = the client
        /// crouched the avatar (its legs stay the client's); cyborgArm = the puppet's arm length (the avatar's arm against it scales the game's grip);
        /// ikLeft / ikLeftGrip = where Halo IKs the left hand, in the gun frame (metres, at the avatar's scale), hasIk false without one.
        /// </summary>
        public void Apply(in HaloPuppetSample sample, float dt, Quaternion aim, Vector3 aimPoint, bool torso, bool crouch, float cyborgArm,
                          bool hasIk, Vector3 ikLeft, Quaternion ikLeftGrip, out Vector3 gunPos, out Quaternion gunRot)
        {
            Posed = false;
            gunPos = Vector3.zero;
            gunRot = Quaternion.identity;
            if (_anim == null) return;
            _w = Mathf.MoveTowards(_w, sample.Valid ? 1f : 0f, dt / (sample.Valid ? BlendIn : BlendOut));
            if (sample.Valid && !sample.Seated) _last = sample;
            if (!_last.Valid) { Calibrate(dt); return; }
            HaloPuppetSample s = _last;
            Transform root = _anim.transform;
            Vector3 rp = root.position;
            float planar = new Vector2(RootVelocity.x, RootVelocity.z).magnitude;
            float still = Mathf.Clamp01(Mathf.InverseLerp(StillNone, StillFull, planar));
            if (Mathf.Abs(RootVelocity.y) > 1.5f) still = 0f;
            float armW = Mathf.SmoothStep(0f, 1f, _w);
            float tw = torso ? 1f : 0f;
            float legW = _legs && !crouch ? armW * still : 0f;
            float hipsW = legW * tw;
            if (planar < 0.1f) Calibrate(0f);
            if (_ankleH < 0f) _ankleH = Mathf.Clamp(Mathf.Min(_b[LFoot].T != null ? _b[LFoot].T.position.y : rp.y, _b[RFoot].T != null ? _b[RFoot].T.position.y : rp.y) - rp.y, 0.02f, 0.25f);

            // the avatar's model frame (flat) and the aim frame (the aim's yaw within MaxYaw of the body, its pitch within MaxPitch)
            Vector3 f0 = root.forward;
            f0.y = 0f;
            if (f0.sqrMagnitude < 1e-6f) f0 = Vector3.forward;
            Quaternion M = Quaternion.LookRotation(f0.normalized, Vector3.up);
            Vector3 af = aim * Vector3.forward;
            float bodyYaw = Mathf.Atan2(f0.x, f0.z) * Mathf.Rad2Deg;
            float yawDiff = Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, Mathf.Atan2(af.x, af.z) * Mathf.Rad2Deg), -MaxYaw, MaxYaw);
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(af.y, -1f, 1f)) * Mathf.Rad2Deg, -MaxPitch, MaxPitch);      // + = down
            Quaternion A = Quaternion.Euler(pitch, bodyYaw + yawDiff, 0f);
            Quaternion footWasL = _b[LFoot].T != null ? _b[LFoot].T.rotation : Quaternion.identity;
            Quaternion footWasR = _b[RFoot].T != null ? _b[RFoot].T.rotation : Quaternion.identity;

            // 1. hips: the game's crouch / lean and the pelvis' turn (standing only; never under the own first-person camera)
            Transform hips = _b[Hips].T;
            if (hips != null && hipsW > 0.001f)
            {
                hips.position += M * (s.HipsOffset * (_legLen * hipsW));
                hips.rotation = Quaternion.AngleAxis(Mathf.Clamp(s.HipsYaw, -MaxHipsYaw, MaxHipsYaw) * hipsW, Vector3.up) * Quaternion.AngleAxis(s.HipsRoll * hipsW, M * Vector3.forward) * hips.rotation;
            }

            // 2. legs: two-bone IK to the game's ankles (ground-clamped at the avatar's own ankle height)
            if (legW > 0.001f)
            {
                Leg(_b[LThigh].T, _b[LShin].T, _b[LFoot].T, rp, M, s.LFoot, s.LKneePole, s.LToePitch, footWasL, legW);
                Leg(_b[RThigh].T, _b[RShin].T, _b[RFoot].T, rp, M, s.RFoot, s.RKneePole, s.RToePitch, footWasR, legW);
            }

            // 3. torso: the game's twist / bend / lean (a share), a share of the yaw to the aim and of its pitch (less while the gun is down)
            Vector3 cu, cr, cf;
            if (tw > 0.001f && ChestFrame(out cu, out cr, out cf))
            {
                Quaternion cur = Quaternion.LookRotation(cf, cu);
                Quaternion pel = M * Quaternion.AngleAxis(Mathf.Clamp(s.HipsYaw, -MaxHipsYaw, MaxHipsYaw) * hipsW, Vector3.up);
                float bend = s.Bend * TorsoShare + pitch * PitchShare * Mathf.Lerp(0.2f, 1f, s.AimWeight);
                Quaternion des = Quaternion.AngleAxis(yawDiff * YawShare, Vector3.up) * pel * Quaternion.AngleAxis(s.Twist * TorsoShare, Vector3.up) * Quaternion.AngleAxis(bend, Vector3.right) * Quaternion.AngleAxis(s.Lean * TorsoShare, Vector3.forward);
                Spread(Quaternion.Slerp(Quaternion.identity, des * Quaternion.Inverse(cur), armW * tw));
                // 4. head: a share of the game's turn on top of where the client looks
                Transform head = _b[Head].T;
                if (head != null && ChestFrame(out cu, out cr, out cf))
                    head.rotation = Quaternion.AngleAxis(s.HeadYaw * HeadShare * armW * tw, cu) * Quaternion.AngleAxis(-s.HeadPitch * HeadShare * armW * tw, cr) * head.rotation;
            }

            // 5. the right arm onto the game's right hand (aim frame at the shoulders), the weapon on its "right hand" marker
            Vector3 mid = (_b[LUpper].T.position + _b[RUpper].T.position) * 0.5f;
            float arm = ArmLength;
            float k = cyborgArm > 0.1f ? Mathf.Clamp(arm / cyborgArm, 0.4f, 1.6f) : 0.8f;
            Vector3 rt = mid + A * (s.RHand * arm);
            Quaternion rGrip = A * s.RGrip;
            // first person (own avatar): the whole hold moved so the weapon sits where the view wants it (BO2's FirstPersonLift), the animation's motion kept
            FpWeight = Mathf.MoveTowards(FpWeight, ViewSet ? 1f : 0f, dt / 0.25f);
            ViewSet = false;
            Vector3 lift = A * Vector3.down * (HoldDrop * (1f - FpWeight));      // (third person: the cyborg holds at its chest; on the shorter avatar that was at the face)
            rt += lift;
            float fw = Mathf.SmoothStep(0f, 1f, FpWeight);
            if (fw > 0.001f)
            {
                // the hand goes to the weapon framed in the view (ViewGun / ViewRot), as far as the arm reaches
                Vector3 rtV = ViewGun + ViewRot * (s.RWristOnGun * k);
                Vector3 up = (rtV - rt) * fw;
                rt += up;
                lift += up;
                rGrip = Quaternion.Slerp(rGrip, ViewRot * Quaternion.Inverse(s.GunInRGrip), fw);
            }

            TwoBone(_b[RUpper].T, _b[RLower].T, _b[RHand].T, rt, A * s.RElbowPole, armW, A * Vector3.right);
            Grip(RHand, RFing, rGrip, armW);
            LastRightErr = (_b[RHand].T.position - rt).magnitude;
            Quaternion grip;
            if (!GripNow(RHand, RFing, out grip)) grip = rGrip;
            gunRot = grip * s.GunInRGrip;
            Vector3 wrist = _b[RHand].T.position;
            Vector3 wOff = s.RWristOnGun * k;
            gunPos = wrist - gunRot * wOff;
            if (fw > 0.001f) { gunPos = Vector3.Lerp(gunPos, ViewGun, fw); gunRot = Quaternion.Slerp(gunRot, ViewRot, fw); }      // (first person: the weapon exactly where the view frames it)

            // the barrel onto the crosshair)'s point while the game's pose aims
            AimFix = 0f;
            Vector3 want = aimPoint - gunPos;
            if (want.sqrMagnitude > 4f && s.AimWeight > 0.001f)
            {
                Vector3 gf = gunRot * Vector3.forward;
                float ang = Vector3.Angle(gf, want);
                if (ang > 0.01f)
                {
                    float t = s.AimWeight * armW * (1f - fw) * Mathf.Min(1f, MaxAimFix / ang);
                    Quaternion d = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(gf, want), t);
                    gunRot = d * gunRot;
                    _b[RHand].T.rotation = d * _b[RHand].T.rotation;
                    gunPos = wrist - gunRot * wOff;
                    AimFix = ang * t;
                }
            }

            // 6. the left arm: where Halo IKs it on the weapon while the animation's own left hand is there, else the animation's place (reload, throw)
            Vector3 animL = s.LWristOnGun;
            float dl = hasIk ? (animL * k - ikLeft).magnitude / k : animL.magnitude;
            LeftOnGun = hasIk ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(IkNear, IkFar, dl)) : 0f;
            Vector3 lBody = mid + A * (s.LHand * arm) + lift;
            Vector3 lt = hasIk ? Vector3.Lerp(lBody, gunPos + gunRot * ikLeft, LeftOnGun) : lBody;
            // out of reach (first person: the weapon raised into the view; a short avatar): the hold slides back along the barrel, up to 40 cm (BO2's MaxHoldPull)
            if (LeftOnGun > 0.01f)
            {
                Vector3 ls = _b[LUpper].T.position;
                float reach = Arm(LUpper) * 0.98f;
                Vector3 back = -(gunRot * Vector3.forward);
                for (int i = 0; i < 16 && (lt - ls).sqrMagnitude > reach * reach; i++) lt += back * (0.025f * LeftOnGun);
            }

            Quaternion lgw = hasIk ? Quaternion.Slerp(A * s.LGrip, gunRot * ikLeftGrip, LeftOnGun) : A * s.LGrip;
            TwoBone(_b[LUpper].T, _b[LLower].T, _b[LHand].T, lt, A * s.LElbowPole, armW, A * Vector3.left);
            Grip(LHand, LFing, lgw, armW);
            LastLeftErr = (_b[LHand].T.position - lt).magnitude;

            // 7. the fingers round the grip / the handguard
            Curl(RHand, RFing, true, armW);
            Curl(LHand, LFing, false, armW * Mathf.Max(LeftOnGun, 0.5f));
            Written();
        }

        /// <summary>
        /// Seated in a vehicle: the avatar sits as the cyborg does in the seat's animation, scaled to the avatar's legs and arms around the seat marker.
        /// arms = pose the arms too (a monitor player; a VR player's arms are the controllers); hands = world wrist targets instead of the animation's
        /// (Halo's seat IK onto the vehicle's wheel / gun grips), handsValid per side.
        /// </summary>
        public void ApplySeated(in HaloPuppetSample sample, float dt, Transform seat, float cyborgLeg, float cyborgArm, bool hipsToo, bool arms,
                                bool lValid, Vector3 lWorld, bool rValid, Vector3 rWorld, out Vector3 gunPos, out Quaternion gunRot)
        {
            Posed = false;
            gunPos = Vector3.zero;
            gunRot = Quaternion.identity;
            if (_anim == null || seat == null) return;
            _w = Mathf.MoveTowards(_w, sample.Valid ? 1f : 0f, dt / (sample.Valid ? BlendIn : BlendOut));
            if (!sample.Valid || !sample.Seated) return;
            float wgt = Mathf.SmoothStep(0f, 1f, _w);
            float kl = cyborgLeg > 0.1f && _legLen > 0f ? Mathf.Clamp(_legLen / cyborgLeg, 0.4f, 1.6f) : 0.8f;
            float arm = ArmLength;
            float ka = cyborgArm > 0.1f ? Mathf.Clamp(arm / cyborgArm, 0.4f, 1.6f) : 0.8f;
            Transform hips = _b[Hips].T;
            Vector3 pelvis = seat.TransformPoint(sample.SPelvis * kl);

            // 1. hips: on the seat, turned as the cyborg's pelvis
            if (hips != null && hipsToo)
            {
                Vector3 hu, hr, hf;
                if (HipsFrame(out hu, out hr, out hf))
                {
                    Quaternion cur = Quaternion.LookRotation(hf, hu);
                    Quaternion want = seat.rotation * sample.SPelvisRot;
                    hips.rotation = Quaternion.Slerp(Quaternion.identity, want * Quaternion.Inverse(cur), wgt) * hips.rotation;
                }

                hips.position = Vector3.Lerp(hips.position, pelvis, wgt);
            }

            Vector3 hp = hips != null ? hips.position : pelvis;
            Vector3 sp = seat.TransformPoint(sample.SPelvis);      // (the cyborg's pelvis, unscaled: offsets below scale from it)

            // 2. legs: knees and ankles of the seat pose, from the hips at the avatar's leg length
            if (_legs)
            {
                SeatLimb(_b[LThigh].T, _b[LShin].T, _b[LFoot].T, hp + (seat.TransformPoint(sample.SLAnk) - sp) * kl, seat.TransformPoint(sample.SLKnee) - seat.TransformPoint((sample.SLHip + sample.SLAnk) * 0.5f), wgt);
                SeatLimb(_b[RThigh].T, _b[RShin].T, _b[RFoot].T, hp + (seat.TransformPoint(sample.SRAnk) - sp) * kl, seat.TransformPoint(sample.SRKnee) - seat.TransformPoint((sample.SRHip + sample.SRAnk) * 0.5f), wgt);
            }

            // 3. torso: the spine toward the cyborg's (pelvis -> neck), when the hips are ours
            Vector3 cu, cr, cf;
            if (hipsToo && ChestFrame(out cu, out cr, out cf))
            {
                Vector3 wantUp = seat.TransformDirection(sample.SNeck - sample.SPelvis);
                if (wantUp.sqrMagnitude > 1e-6f) Spread(Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cu, wantUp.normalized), wgt * TorsoShare));
            }

            // 4. arms: onto the vehicle's grips (Halo's seat IK) or the animation's hands
            if (arms)
            {
                Vector3 mid = (_b[LUpper].T.position + _b[RUpper].T.position) * 0.5f;
                Vector3 smid = seat.TransformPoint((sample.SLSh + sample.SRSh) * 0.5f);
                Vector3 rt = rValid ? rWorld : mid + (seat.TransformPoint(sample.SRWr) - smid) * ka;
                Vector3 lt = lValid ? lWorld : mid + (seat.TransformPoint(sample.SLWr) - smid) * ka;
                Vector3 rPole = seat.TransformPoint(sample.SREl) - seat.TransformPoint((sample.SRSh + sample.SRWr) * 0.5f);
                Vector3 lPole = seat.TransformPoint(sample.SLEl) - seat.TransformPoint((sample.SLSh + sample.SLWr) * 0.5f);
                TwoBone(_b[RUpper].T, _b[RLower].T, _b[RHand].T, rt, rPole, wgt, seat.right);
                TwoBone(_b[LUpper].T, _b[LLower].T, _b[LHand].T, lt, lPole, wgt, -seat.right);
                Grip(RHand, RFing, seat.rotation * sample.SRGrip, wgt);
                Grip(LHand, LFing, seat.rotation * sample.SLGrip, wgt);
                LastRightErr = (_b[RHand].T.position - rt).magnitude;
                LastLeftErr = (_b[LHand].T.position - lt).magnitude;
                Quaternion grip;
                if (!GripNow(RHand, RFing, out grip)) grip = seat.rotation * sample.SRGrip;
                gunRot = grip * sample.GunInRGrip;
                gunPos = _b[RHand].T.position - gunRot * (sample.RWristOnGun * ka);
                Curl(RHand, RFing, false, wgt);
                Curl(LHand, LFing, false, wgt);
            }

            Written();
        }

        /// <summary>The six arm bones' local rotations (left upper, lower, hand, right upper, lower, hand) into / from q (length 6) - a second arm
        /// pose kept for one camera (Vehicles: the own first-person seat view's hands on the view model; every other camera the seat clip's).</summary>
        public void ArmsSave(Quaternion[] q) { for (int i = 0; i < 6; i++) { Transform t = _b[LUpper + i].T; q[i] = t != null ? t.localRotation : Quaternion.identity; } }

        public void ArmsLoad(Quaternion[] q) { for (int i = 0; i < 6; i++) { Transform t = _b[LUpper + i].T; if (t != null) t.localRotation = q[i]; } }

        /// <summary>After ApplySeated: the arms (only) re-reached to other wrist targets, the right hand turned to rGrip, the left to the seat
        /// clip's grip (the same two-bone rule and elbow poles as ApplySeated). Not recorded as written: the caller restores the arms after.</summary>
        public void ReachSeatedArms(in HaloPuppetSample sample, Transform seat, bool lValid, Vector3 lWorld, bool rValid, Vector3 rWorld, Quaternion rGrip)
        {
            if (_anim == null || seat == null || !sample.Valid || !sample.Seated) return;
            float wgt = Mathf.SmoothStep(0f, 1f, _w);
            if (rValid)
            {
                Vector3 rPole = seat.TransformPoint(sample.SREl) - seat.TransformPoint((sample.SRSh + sample.SRWr) * 0.5f);
                TwoBone(_b[RUpper].T, _b[RLower].T, _b[RHand].T, rWorld, rPole, wgt, seat.right);
                Grip(RHand, RFing, rGrip, wgt);
            }
            if (lValid)
            {
                Vector3 lPole = seat.TransformPoint(sample.SLEl) - seat.TransformPoint((sample.SLSh + sample.SLWr) * 0.5f);
                TwoBone(_b[LUpper].T, _b[LLower].T, _b[LHand].T, lWorld, lPole, wgt, -seat.right);
                Grip(LHand, LFing, seat.rotation * sample.SLGrip, wgt);
            }
        }

        /// <summary>The arms (only) reached to wrist targets with given elbow poles and hand turns (world) - Vehicles' own first-person seat view,
        /// where the body is drawn like on foot and the elbows hang (the seat clip's poles raise them to a shouldered rifle). Not recorded as
        /// written: the caller restores the arms after.</summary>
        public void ReachArms(Vector3 lWorld, Vector3 lPole, Quaternion lGrip, bool lValid, Vector3 rWorld, Vector3 rPole, Quaternion rGrip, Vector3 right)
        {
            if (_anim == null) return;
            float wgt = Mathf.SmoothStep(0f, 1f, _w);
            TwoBone(_b[RUpper].T, _b[RLower].T, _b[RHand].T, rWorld, rPole, wgt, right);
            Grip(RHand, RFing, rGrip, wgt);
            if (!lValid) return;
            TwoBone(_b[LUpper].T, _b[LLower].T, _b[LHand].T, lWorld, lPole, wgt, -right);
            Grip(LHand, LFing, lGrip, wgt);
        }

        private void SeatLimb(Transform u, Transform l, Transform f, Vector3 target, Vector3 pole, float w)
        {
            if (u == null || l == null || f == null) return;
            Quaternion footWas = f.rotation;
            TwoBone(u, l, f, target, pole, w, _anim.transform.right);
            f.rotation = footWas;
        }

        private void Written()
        {
            for (int i = 0; i < Count; i++)
            {
                Bone b = _b[i];
                if (b.T == null) continue;
                Quaternion q = b.T.localRotation;
                if (Mathf.Abs(Quaternion.Dot(q, b.Base)) > 0.9999999f) continue;
                b.Written = q;
                b.Wrote = true;
                Posed = true;
            }

            Transform h = _b[Hips].T;
            if (h != null && (h.localPosition - _hipsBase).sqrMagnitude > 1e-12f)
            {
                _hipsWritten = h.localPosition;
                _hipsWrote = true;
                Posed = true;
            }
        }

        private float ArmLengthBoth()
        {
            float armL = Arm(LUpper), armR = Arm(RUpper);
            float arm = armL > 0f && armR > 0f ? (armL + armR) * 0.5f : Mathf.Max(armL, armR);
            return arm < 0.2f ? 0.55f : arm;
        }

        private float Arm(int upper)
        {
            Transform u = _b[upper].T, l = _b[upper + 1].T, h = _b[upper + 2].T;
            if (u == null || l == null || h == null) return 0f;
            return (l.position - u.position).magnitude + (h.position - l.position).magnitude;
        }

        /// <summary>The avatar's own ankle height above its root while it stands unposed (exp 2/s).</summary>
        private void Calibrate(float dt)
        {
            if (!_legs || _anim == null) return;
            float ah = Mathf.Min(_b[LFoot].T.position.y, _b[RFoot].T.position.y) - _anim.transform.position.y;
            ah = Mathf.Clamp(ah, 0.02f, 0.25f);
            _ankleH = _ankleH < 0f ? ah : Mathf.Lerp(_ankleH, ah, 1f - Mathf.Exp(-Mathf.Max(dt, Time.deltaTime) * 2f));
        }

        /// <summary>A world rotation of the upper body spread over spine / chest / upper chest; the head keeps 75 % of where it looked.</summary>
        private void Spread(Quaternion delta)
        {
            Transform head = _b[Head].T;
            Quaternion headWas = head != null ? head.rotation : Quaternion.identity;
            float w0 = _b[Spine].T != null ? 0.3f : 0f, w1 = _b[Chest].T != null ? 0.35f : 0f, w2 = _b[UpperChest].T != null ? 0.35f : 0f;
            float sum = w0 + w1 + w2;
            if (sum <= 0f) return;
            for (int i = Spine; i <= UpperChest; i++)
            {
                Transform t = _b[i].T;
                if (t == null) continue;
                float share = (i == Spine ? w0 : i == Chest ? w1 : w2) / sum;
                t.rotation = Quaternion.Slerp(Quaternion.identity, delta, share) * t.rotation;
            }

            if (head != null) head.rotation = Quaternion.Slerp(head.rotation, headWas, 0.75f);
        }

        /// <summary>Two-bone IK (upper, lower, end) to a world target with a pole direction, blended from the animated pose by weight.</summary>
        private static void TwoBone(Transform u, Transform l, Transform h, Vector3 target, Vector3 pole, float weight, Vector3 sideFallback)
        {
            if (u == null || l == null || h == null) return;
            Quaternion uBase = u.localRotation, lBase = l.localRotation;
            Vector3 s = u.position, e = l.position, w = h.position;
            float a = (e - s).magnitude, b = (w - e).magnitude;
            if (a < 1e-4f || b < 1e-4f) return;
            Vector3 toT = target - s;
            float dist = toT.magnitude;
            if (dist < 1e-4f) return;
            Vector3 dir = toT / dist;
            float d = Mathf.Clamp(dist, Mathf.Abs(a - b) + 1e-3f, (a + b) * 0.999f);
            pole -= Vector3.Dot(pole, dir) * dir;
            if (pole.sqrMagnitude < 1e-6f) pole = Vector3.Cross(dir, sideFallback).normalized;
            else pole.Normalize();
            float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);
            Vector3 elbow = s + dir * (a * cosA) + pole * (a * sinA);
            u.rotation = Quaternion.FromToRotation(e - s, elbow - s) * u.rotation;
            Vector3 e1 = l.position, w1 = h.position;
            Vector3 want = s + dir * d - e1;
            if ((w1 - e1).sqrMagnitude > 1e-8f && want.sqrMagnitude > 1e-8f) l.rotation = Quaternion.FromToRotation(w1 - e1, want) * l.rotation;
            if (weight < 0.999f)
            {
                Quaternion uIk = u.localRotation, lIk = l.localRotation;
                u.localRotation = Quaternion.Slerp(uBase, uIk, weight);
                l.localRotation = Quaternion.Slerp(lBase, lIk, weight);
            }
        }

        /// <summary>The avatar hand's grip frame now (forward = little -> index knuckle, up = -(wrist -> middle knuckle)); without fingers from the hand's own frame.</summary>
        private bool GripNow(int hand, int fing, out Quaternion grip)
        {
            grip = Quaternion.identity;
            Transform h = _b[hand].T, index = _b[fing].T, middle = _b[fing + 3].T, little = _b[fing + 9].T;
            if (h == null || index == null || little == null || middle == null) return false;
            Vector3 k = middle.position - h.position;
            if (k.sqrMagnitude < 1e-8f) return false;
            k.Normalize();
            Vector3 f = index.position - little.position;
            f -= Vector3.Dot(f, k) * k;
            if (f.sqrMagnitude < 1e-8f) return false;
            grip = Quaternion.LookRotation(f.normalized, -k);
            return true;
        }

        /// <summary>Turns the hand so its grip frame matches the wanted world rotation.</summary>
        private void Grip(int hand, int fing, Quaternion want, float weight)
        {
            Quaternion now;
            Transform h = _b[hand].T;
            if (h == null || !GripNow(hand, fing, out now)) return;
            Quaternion turned = want * Quaternion.Inverse(now) * h.rotation;
            h.rotation = Quaternion.Slerp(h.rotation, turned, Mathf.Clamp01(weight));
        }

        /// <summary>The fingers round the grip: each joint bends toward the palm about the knuckles' line (the right trigger finger less).</summary>
        private void Curl(int hand, int fing, bool trigger, float weight)
        {
            if (weight < 0.01f) return;
            Transform index = _b[fing].T, little = _b[fing + 9].T;
            if (_b[hand].T == null || index == null || little == null) return;
            Vector3 f = index.position - little.position;
            if (f.sqrMagnitude < 1e-8f) return;
            bool right = fing == RFing;
            Vector3 axis = (right ? -f : f).normalized;      // (right hand: index on the thumb side, so the bend is about little -> index reversed)
            for (int fi = 0; fi < 4; fi++)
            {
                bool tf = trigger && fi == 0;
                for (int j = 0; j < 3; j++)
                {
                    Transform b = _b[fing + fi * 3 + j].T;
                    if (b == null) continue;
                    float ang = tf ? (j == 0 ? 15f : j == 1 ? 30f : 20f) : (j == 0 ? 60f : j == 1 ? 70f : 45f);
                    b.rotation = Quaternion.AngleAxis(ang * weight, axis) * b.rotation;
                }
            }
        }

        private void Leg(Transform u, Transform l, Transform f, Vector3 rp, Quaternion M, Vector3 foot, Vector3 kneePole, float toePitch, Quaternion footWas, float w)
        {
            if (u == null || l == null || f == null) return;
            float L = _legLen;
            Vector3 target = rp + M * new Vector3(foot.x * L, 0f, foot.z * L) + Vector3.up * (_ankleH + Mathf.Max(0f, foot.y * L));
            TwoBone(u, l, f, target, M * kneePole, w, M * Vector3.right);
            Quaternion want = Quaternion.AngleAxis(-toePitch, M * Vector3.right) * footWas;
            f.rotation = Quaternion.Slerp(f.rotation, want, w);
        }

        /// <summary>The avatar's chest frame from its bones now (up = neck - hips, right = the upper arms' line).</summary>
        private bool ChestFrame(out Vector3 up, out Vector3 right, out Vector3 fwd)
        {
            up = Vector3.up;
            right = Vector3.right;
            fwd = Vector3.forward;
            Transform hips = _b[Hips].T, neck = _b[Neck].T != null ? _b[Neck].T : _b[Head].T;
            Transform lu = _b[LUpper].T, ru = _b[RUpper].T;
            if (lu == null || ru == null || hips == null || neck == null) return false;
            Vector3 u = neck.position - hips.position;
            if (u.sqrMagnitude < 1e-6f) return false;
            up = u.normalized;
            Vector3 across = ru.position - lu.position;
            Vector3 r = across - Vector3.Dot(across, up) * up;
            if (r.sqrMagnitude < 1e-8f) return false;
            right = r.normalized;
            fwd = Vector3.Cross(right, up);
            return true;
        }

        /// <summary>The avatar's pelvis frame (up = hips -> spine, right = the thighs' line).</summary>
        private bool HipsFrame(out Vector3 up, out Vector3 right, out Vector3 fwd)
        {
            up = Vector3.up;
            right = Vector3.right;
            fwd = Vector3.forward;
            Transform hips = _b[Hips].T, sp = _b[Spine].T != null ? _b[Spine].T : _b[Chest].T, lt = _b[LThigh].T, rt = _b[RThigh].T;
            if (hips == null || sp == null || lt == null || rt == null) return false;
            Vector3 u = sp.position - hips.position;
            if (u.sqrMagnitude < 1e-8f) return false;
            up = u.normalized;
            Vector3 across = rt.position - lt.position;
            Vector3 r = across - Vector3.Dot(across, up) * up;
            if (r.sqrMagnitude < 1e-8f) return false;
            right = r.normalized;
            fwd = Vector3.Cross(right, up);
            return true;
        }
    }
}

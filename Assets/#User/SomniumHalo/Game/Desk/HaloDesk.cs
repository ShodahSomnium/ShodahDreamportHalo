using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Desk module] Monitor (non-VR) play: the camera (first person forced like the BO2 Dreamport, or the client's own third person), the crosshair's
    /// aim, and every player's avatar holding its weapon like Halo's cyborg (HaloDeskBody: Halo's own third-person animations on a hidden cyborg
    /// puppet, the avatar's humanoid bones posed onto it), seated in vehicles with the seat animations.
    /// Others call: Combat sets <see cref="HeldWeapon"/>, notes reload / melee / throw, shoots along <see cref="ShotDirection"/>; Net reports remote
    /// players (<see cref="ReportRemote"/>); Match calls <see cref="Start"/> / <see cref="Stop"/>. Settings: "desk.view" (first | third), "desk.viewkey".
    /// </summary>
    public static class HaloDesk
    {
        /// <summary>True on a monitor (no headset).</summary>
        public static bool Active { get { Ensure(); return !HaloPlayer.XrPresent; } }

        static HaloDesk()
        {
            // (a fallback for Match calling Start / Stop: the first touch of the Desk API - Combat, Net - hooks the session phases)
            HaloSession.PhaseChanged += (o, n) => { if (n == HaloSession.Phase.Playing) Start(); else if (n == HaloSession.Phase.Hub) Stop(); };
        }

        private static int _ensured = -1;

        /// <summary>Started when a match plays (once a frame at most).</summary>
        private static void Ensure()
        {
            if (_ensured == Time.frameCount) return;
            _ensured = Time.frameCount;
            if (!HaloSession.Playing) return;
            HaloGame g = HaloGame.Current;
            if (g == null) return;
            HaloDeskBody b = g.GetComponent<HaloDeskBody>();
            if (b == null || !b.enabled) Start();
        }

        // ------------------------------------------------------------------ switches (ours; for the lab and other modules)

        /// <summary>Pose other monitor players' avatars / the own avatar on a monitor / seated avatars (vehicles).</summary>
        public static bool PoseOthers = true, PoseOwn = true, PoseSeated = true;
        /// <summary>A VR player's seated avatar gets the seat pose's legs (arms stay the controllers').</summary>
        public static bool SeatPoseVr = true;
        /// <summary>Desk also holds the third-person weapon in the avatar's hands in FIRST person (false: Combat draws its own view model then).</summary>
        public static bool PlaceHeldInFirstPerson = true;
        /// <summary>Hide the own head on this screen in first person.</summary>
        public static bool HideHead = true;
        /// <summary>Dead monitor players fall with the game's death animation (held lying until the respawn); VR players never.</summary>
        public static bool DeathFall = true;
        /// <summary>
        /// First person: the held weapon framed like Halo's view model - its rear-top corner (the sights end) pinned at this point of the screen (half-screen
        /// units: +x right, -y down), at <see cref="FirstPersonDepth"/> ahead for a 70 degree vertical field of view (deeper for a narrower one, so the
        /// weapon covers the same part of the picture), the barrel on the crosshair's point. Ours (BO2 iteration 19 framed its sights 8.5 cm right,
        /// 6.5 cm down, 48 cm ahead for the client's 40 degree camera).
        /// </summary>
        public static Vector2 FirstPersonScreen = new Vector2(0.55f, -0.45f);
        public static float FirstPersonDepth = 0.3f;
        /// <summary>First person: the grip no further than this from the eye (the avatar's hand must reach it): beyond, the weapon is shrunk toward the eye on this
        /// screen only while drawing (the picture is the same; others never see this copy). Ours.</summary>
        public static float FirstPersonReach = 0.4f;

        /// <summary>
        /// The first-person framing of a weapon for a camera: the origin and gun frame (+z barrel, +y up) of the weapon model, and the scale (on top
        /// of baseScale = the model's own) it is drawn at so the grip stays within reach.
        /// </summary>
        public static void FirstPersonPose(Camera cam, HaloTag weapon, Vector3 aimPoint, float baseScale, out Vector3 pos, out Quaternion gunRot, out float scale)
        {
            Transform ct = cam.transform;
            FirstPersonPose(ct.position, ct.rotation, cam.fieldOfView, cam.aspect, weapon, aimPoint, baseScale, out pos, out gunRot, out scale);
        }

        /// <summary>The same framing for a camera pose given apart from a camera (Vehicles: the seat eye the vehicle camera takes later this frame).</summary>
        public static void FirstPersonPose(Vector3 camPos, Quaternion camRot, float fov, float aspect, HaloTag weapon, Vector3 aimPoint, float baseScale, out Vector3 pos, out Quaternion gunRot, out float scale)
        {
            HaloCyborg c = HaloCyborg.Current;
            HaloCyborg.WeaponGrip g = c != null ? c.Grip(weapon) : null;
            // the rear-top corner in the gun frame (x right, y up, z forward), metres
            Vector3 rearTop = g != null && g.HasBox ? new Vector3(0f, g.BoxMax.z, g.BoxMin.x) * (HaloSpace.Unit * baseScale) : new Vector3(0f, 0.1f, -0.25f) * baseScale;
            float tanV = Mathf.Tan(Mathf.Clamp(fov, 10f, 120f) * 0.5f * Mathf.Deg2Rad);
            float f = FirstPersonDepth * 0.7002f / tanV;
            Vector3 anchor = camPos + camRot * new Vector3(FirstPersonScreen.x * f * tanV * aspect, FirstPersonScreen.y * f * tanV, f);
            Quaternion rot = camRot;
            for (int i = 0; i < 2; i++)
            {
                Vector3 d = aimPoint - (anchor - rot * rearTop);
                if (d.sqrMagnitude > 1f) rot = Quaternion.LookRotation(d, camRot * Vector3.up);
            }

            Vector3 origin = anchor - rot * rearTop;
            float dist = (origin - camPos).magnitude;
            scale = dist > FirstPersonReach ? FirstPersonReach / dist : 1f;
            pos = camPos + (origin - camPos) * scale;
            gunRot = rot;
        }
        /// <summary>First person: the client's camera draws from a 35 cm near plane (BO2 measured it): held at this just before it draws so the weapon is not cut.</summary>
        public static float FirstPersonNear = 0.08f;

        // ------------------------------------------------------------------ the local player's weapon and events (Combat)

        /// <summary>The held weapon's third-person model root (HaloModelBuilder: origin = the weapon's model origin, barrel +x); null = nothing in hand. Set by Combat.</summary>
        public static Transform HeldWeapon { get { return _held; } set { _held = value; Ensure(); } }
        private static Transform _held;
        /// <summary>Desk placed <see cref="HeldWeapon"/> in the avatar's hands this frame (monitor): do not move it after Update.</summary>
        public static bool HoldsWeapon { get; internal set; }
        /// <summary>Counters of the local player's events (+1 each), for the own puppet and Net's record.</summary>
        public static int LocalReloads, LocalMelees, LocalThrows, LocalFires;

        public static void NoteReload() { LocalReloads++; }
        public static void NoteMelee() { LocalMelees++; }
        public static void NoteThrow() { LocalThrows++; }

        // ------------------------------------------------------------------ the camera

        /// <summary>The saved view in a match on a monitor: first person (Halo's) or the client's third person.</summary>
        public static bool ThirdPersonWanted
        {
            get { return HaloSettings.Text("desk.view", "first") == "third"; }
            set { HaloSettings.SetText("desk.view", value ? "third" : "first"); }
        }

        /// <summary>The key that switches first / third person in a match (V: the client does not bind it; Halo PC leaves it free).</summary>
        public static KeyCode ViewKey
        {
            get
            {
                string k = HaloSettings.Text("desk.viewkey", "V");
                try { return (KeyCode)Enum.Parse(typeof(KeyCode), k, true); } catch (Exception) { return KeyCode.V; }
            }
        }

        /// <summary>The monitor camera is at the avatar's head right now (first person); false in third person or VR.</summary>
        public static bool FirstPerson { get; internal set; }
        /// <summary>The own avatar's humanoid Animator being posed (null when none was found).</summary>
        public static Animator Avatar { get; internal set; }
        /// <summary>The own head is hidden on this screen (first person) this frame.</summary>
        public static bool HeadHidden { get { return HaloHeadHide.Hidden; } }

        private static int _aimFrame = -1;
        private static Vector3 _aimPoint;
        private static bool _aimHit;
        private static RaycastHit _aimInfo;

        /// <summary>Lab: the camera that stands for the player's (null = the client's, HaloPlayer.Head).</summary>
        public static Func<Camera> LabCamera;

        /// <summary>The monitor camera the player looks through.</summary>
        public static Camera Cam() { return LabCamera != null ? LabCamera() : HaloPlayer.Head(); }

        /// <summary>The aim ray on a monitor: from the camera through the crosshair (the screen centre).</summary>
        public static Ray AimRay()
        {
            Ensure();
            Camera c = HaloDesk.Cam();
            return c != null ? new Ray(c.transform.position, c.transform.forward) : new Ray(Vector3.zero, Vector3.forward);
        }

        /// <summary>
        /// What the crosshair is on: the camera's centre ray, started past the own avatar's head (the own body, its capsule and hit boxes are skipped
        /// in third person), against the world, props, vehicles and hit boxes, up to 300 m. Asked once a frame (cached).
        /// </summary>
        public static Vector3 AimPoint
        {
            get
            {
                if (_aimFrame == Time.frameCount) return _aimPoint;
                _aimFrame = Time.frameCount;
                Ray r = AimRay();
                Transform head = HaloPlayer.HeadBone;
                Vector3 eye = head != null ? head.position : r.origin;
                float skip = Mathf.Max(0f, Vector3.Dot(eye - r.origin, r.direction)) + 0.4f;
                Vector3 o = r.origin + r.direction * skip;
                _aimHit = Physics.Raycast(o, r.direction, out _aimInfo, 300f, HaloLayers.ShotMask, QueryTriggerInteraction.Ignore);
                _aimPoint = _aimHit ? _aimInfo.point : o + r.direction * 300f;
                return _aimPoint;
            }
        }

        /// <summary>The crosshair's ray hit something this frame (and what): see <see cref="AimPoint"/>.</summary>
        public static bool AimHit(out RaycastHit hit)
        {
            Vector3 p = AimPoint;
            hit = _aimInfo;
            return _aimHit;
        }

        /// <summary>From a muzzle toward what the crosshair is on (third person: the gun is not on the camera's line); the camera's forward when too close.</summary>
        public static Vector3 ShotDirection(Vector3 muzzle)
        {
            Vector3 d = AimPoint - muzzle;
            if (d.sqrMagnitude < 0.25f) return AimRay().direction;
            return d.normalized;
        }

        // ------------------------------------------------------------------ remote players (Net)

        internal static readonly List<HaloDeskRemote> Reports = new List<HaloDeskRemote>(16);
        internal static int ReportFrame = -1;

        /// <summary>Net: one remote player's state for this frame (call every frame per remote player in my match; a player not reported for 0.5 s is let go).</summary>
        public static void ReportRemote(in HaloDeskRemote r)
        {
            Ensure();
            if (ReportFrame != Time.frameCount) { ReportFrame = Time.frameCount; }
            for (int i = 0; i < Reports.Count; i++)
            {
                if (Reports[i].Slot != r.Slot) continue;
                HaloDeskRemote c = r;
                c.Seen = Time.unscaledTime;
                Reports[i] = c;
                return;
            }

            HaloDeskRemote n = r;
            n.Seen = Time.unscaledTime;
            Reports.Add(n);
        }

        // ------------------------------------------------------------------ seat poses (Vehicles)

        /// <summary>
        /// The cyborg's pose in a vehicle seat ("W-driver", "W-gunner", "W-passenger", "G-driver", "B-driver", "scorpion-driver", "GT-gunner", ...),
        /// sampled from the seat's idle (with the weapon's class for a passenger), joints in the seat marker's frame (HaloModelBuilder marker axes:
        /// x forward, y up, z left; metres at the cyborg's size). False outside a match / without the cyborg / unknown seat.
        /// </summary>
        public static bool SeatPoseFor(string seatLabel, HaloTag weapon, out HaloSeatPose pose)
        {
            pose = default(HaloSeatPose);
            HaloCyborg c = HaloCyborg.Current;
            if (c == null || string.IsNullOrEmpty(seatLabel) || c.Graph.FindSeat(seatLabel) == null) return false;
            string why;
            if (_seatPuppet == null || _seatPuppet.Cyborg != c) _seatPuppet = HaloPuppet.Create(c, out why);
            if (_seatPuppet == null) return false;
            var i = new HaloPuppetInput { Weapon = weapon, Seat = seatLabel };
            _seatPuppet.Step(i, 0f);
            _seatPuppet.Step(i, 0.25f);      // (past the cross-fade from the last seat)
            HaloPuppetSample s = _seatPuppet.Sample;
            if (!s.Valid || !s.Seated) return false;
            pose = new HaloSeatPose
            {
                Pelvis = s.SPelvis, PelvisRot = s.SPelvisRot, Chest = s.SChest, Head = s.SHead,
                LHip = s.SLHip, RHip = s.SRHip, LKnee = s.SLKnee, RKnee = s.SRKnee, LAnkle = s.SLAnk, RAnkle = s.SRAnk,
                LShoulder = s.SLSh, RShoulder = s.SRSh, LElbow = s.SLEl, RElbow = s.SREl, LWrist = s.SLWr, RWrist = s.SRWr,
                Animation = _seatPuppet.BaseName,
            };
            return true;
        }

        private static HaloPuppet _seatPuppet;

        // ------------------------------------------------------------------ life cycle (Match)

        /// <summary>The match is playing (Match calls it after HaloHud.Start): the monitor camera, the posing of every avatar.</summary>
        public static void Start()
        {
            HaloGame g = HaloGame.Current;
            if (g == null) { HaloLog.Warn("Desk: no HaloGame - not started"); return; }
            HaloDeskBody b = g.AddOnce<HaloDeskBody>();
            if (b != null) b.enabled = true;
        }

        /// <summary>The match is over: every avatar given back, the client's camera and keys as they were.</summary>
        public static void Stop()
        {
            HaloGame g = HaloGame.Current;
            HaloDeskBody b = g != null ? g.GetComponent<HaloDeskBody>() : null;
            if (b != null) b.enabled = false;
            Reports.Clear();
            HeldWeapon = null;
            HoldsWeapon = false;
            FirstPerson = false;
            Avatar = null;
        }
    }

    /// <summary>A remote player as Net reports it for the posing (see <see cref="HaloDesk.ReportRemote"/>).</summary>
    public struct HaloDeskRemote
    {
        public int Slot;
        public bool Vr, Alive, Crouch, Zoomed;
        /// <summary>Their head (world) and where they aim (the networked head / aim rotation).</summary>
        public Vector3 Head, Feet;
        public Quaternion HeadRot;
        /// <summary>Their humanoid Animator if Net knows it (else Desk finds it by the head), their Bridge body root.</summary>
        public Animator Avatar;
        public Transform Root;
        /// <summary>The held weapon (null = none) and, if Net draws it, its model's Transform (else Desk builds and places one for a monitor player).</summary>
        public HaloTag Weapon;
        public Transform WeaponObject;
        /// <summary>Event counters from their record (+1 per event): reload, melee, grenade throw. Shots come from HaloEvents.Fired (who = Slot).</summary>
        public int Reloads, Melees, Throws;
        /// <summary>Seated: the vehicle object, the seat label ("W-driver") and the seat marker Transform (null: Desk looks for it on the vehicle).</summary>
        public GameObject Vehicle;
        public string SeatLabel;
        public Transform SeatMarker;
        internal float Seen;
    }

    /// <summary>The cyborg's joints in a vehicle seat, in the seat marker's frame (x forward, y up, z left; metres at the cyborg's size).</summary>
    public struct HaloSeatPose
    {
        public Vector3 Pelvis, Chest, Head, LHip, RHip, LKnee, RKnee, LAnkle, RAnkle, LShoulder, RShoulder, LElbow, RElbow, LWrist, RWrist;
        public Quaternion PelvisRot;
        public string Animation;
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Another human in my match, on my screen (Bo2RemotePlayer for Halo). Their Somnium avatar is drawn by Somnium; this draws what the Dreamport owns: the
    /// weapon in their hand (the third-person model of the map's weap tag, its grip at the hand, Halo's +X along the aim) and the other one on the body (long
    /// guns over the back, small ones on the hip), the shots (the firing effect at the muzzle marker "primary trigger", the impact, the fire sound through the
    /// effect), their grenades in flight, a melee swing, active camouflage (the avatar's renderers off, flickering when they fire), the name over the head
    /// (team mates always, enemies when my aim is on them, as Halo does), and the hit boxes (<see cref="HaloNetHitbox"/>, layer Hitbox) on their avatar's bones,
    /// or - while the avatar is not found - on the body their record describes. The pose is the interpolated one of their record (0.16 s behind).
    /// </summary>
    public sealed class HaloRemoteView : MonoBehaviour
    {
        /// <summary>The weapon models are made for the 2.15 m cyborg (0.704 world units): scaled to a 1.7 m avatar, then by the avatar's own size.</summary>
        public static float WeaponScale = 1.7f / (0.704f * HaloSpace.Unit);     // ours

        /// <summary>A shot is held back so the flash meets the drawn pose (the pose is drawn 0.16 s behind its record).</summary>
        public const float ShotDelay = 0.08f;

        public HaloNetPlayer Player { get; private set; }
        public HaloNet.Remote Record { get; private set; }
        public readonly HaloNetBody Body = new HaloNetBody();
        /// <summary>For the lab and the log.</summary>
        public int ShotsPlayed, ThrowsPlayed, Melees;
        public bool HitBoxesOn { get { return _boxesOn; } }
        public bool OnAvatarBones { get { return _bones; } }
        public Transform HeldModel { get { return _held != null ? _held.Root : null; } }
        public Transform StowedModel { get { return _stowed != null ? _stowed.Root : null; } }

        // hit boxes
        private const int BHead = 0, BTorsoU = 1, BTorsoM = 2, BTorsoL = 3, BRArmU = 4, BLArmU = 5, BRArmL = 6, BLArmL = 7, BRHand = 8, BLHand = 9, BRLegU = 10, BLLegU = 11,
            BRLegL = 12, BLLegL = 13, BRFoot = 14, BLFoot = 15, BoxCount = 16;

        private static readonly int[] Region =
        {
            HaloNetHitbox.Head, HaloNetHitbox.Torso, HaloNetHitbox.Torso, HaloNetHitbox.Torso, HaloNetHitbox.Arm, HaloNetHitbox.Arm, HaloNetHitbox.Arm, HaloNetHitbox.Arm,
            HaloNetHitbox.Arm, HaloNetHitbox.Arm, HaloNetHitbox.Leg, HaloNetHitbox.Leg, HaloNetHitbox.Leg, HaloNetHitbox.Leg, HaloNetHitbox.Leg, HaloNetHitbox.Leg,
        };

        private static readonly bool[] IsSphere = { true, false, false, false, false, false, false, false, true, true, false, false, false, false, true, true };
        private static readonly string[] BoxNames = { "Head", "Torso upper", "Torso mid", "Torso lower", "R arm upper", "L arm upper", "R arm lower", "L arm lower", "R hand", "L hand", "R leg upper", "L leg upper", "R leg lower", "L leg lower", "R foot", "L foot" };
        private readonly Transform[] _boxT = new Transform[BoxCount];
        private readonly CapsuleCollider[] _cap = new CapsuleCollider[BoxCount];
        private readonly SphereCollider[] _sph = new SphereCollider[BoxCount];
        private bool _boxesOn = true, _bones;

        // the avatar
        private Animator _avatar;
        private readonly Transform[] _bone = new Transform[HumanBones.Length];
        private static readonly HumanBodyBones[] HumanBones =
        {
            HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.Chest, HumanBodyBones.Spine, HumanBodyBones.Hips,
            HumanBodyBones.RightUpperArm, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightHand, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.LeftFoot,
        };
        private const int HHead = 0, HNeck = 1, HChest = 2, HSpine = 3, HHips = 4, HRUA = 5, HLUA = 6, HRLA = 7, HLLA = 8, HRH = 9, HLH = 10, HRUL = 11, HLUL = 12, HRLL = 13, HLLL = 14, HRF = 15, HLF = 16;
        private float _nextAvatarLook;
        private int _avatarLooks;
        private readonly List<Renderer> _avatarRenderers = new List<Renderer>(32);
        private bool _camoHidden;

        // weapons
        private sealed class Gun
        {
            public HaloTag Tag;
            public HaloWeaponDef Def;
            public Transform Root, Muzzle;
            public bool Long;
            public HaloWeaponAnim Anim;
        }

        private readonly List<Gun> _guns = new List<Gun>(4);
        private Gun _held, _stowed;
        private float _swing, _kick;
        private int _animReloads = -1;
        private bool _animOverheated;

        // shots waiting for the drawn pose
        private struct Queued { public double At; public HaloNetEvent Ev; }
        private readonly List<Queued> _queue = new List<Queued>(8);
        private static readonly RaycastHit[] Hits = new RaycastHit[16];

        // the name over the head
        private TextMesh _label;
        private Vector3 _lastPos;
        private bool _havePos, _wasAlive = true;
        private float _shownHealth = 1f;

        public void Setup(HaloNet.Remote record, HaloNetPlayer player)
        {
            Record = record;
            Player = player;
            BuildBoxes();
            BuildLabel();
        }

        // ------------------------------------------------------------------ per frame (HaloNetGame.Late)

        public void Refresh(double now, float dt)
        {
            if (Record == null || Player == null) return;
            HaloNetState st = Record.State;
            HaloNetTimeline.Sample s;
            if (!Record.Timeline.Evaluate(now, out s)) return;
            Body.Update(in s, st.Zoomed ? 1f : 0f);
            transform.position = Body.Feet;

            // the player's drawn pose for the other modules
            HaloNetPlayer p = Player;
            p.Position = Body.Feet;
            p.Head = Body.HeadPos;
            p.HeadRot = Body.HeadRot;
            p.Yaw = Body.Yaw;
            p.Forward = Body.Forward;
            p.LeftHand = Body.LHandPos; p.LeftHandRot = Body.LHandRot;
            p.RightHand = Body.RHandPos; p.RightHandRot = Body.RHandRot;
            if (_havePos && dt > 1e-4f) p.Velocity = Vector3.Lerp(p.Velocity, (Body.Feet - _lastPos) / dt, Mathf.Clamp01(dt * 10f));
            _lastPos = Body.Feet;
            _havePos = true;
            Vector3 flat = p.Velocity; flat.y = 0f;
            p.Moving = flat.magnitude > 1.0f || Mathf.Abs(p.Velocity.y) > 2.0f;      // ours: Halo's tracker ignores crouch-walking and standing still
            p.Avatar = _avatar;

            bool alive = st.Alive && st.Status == HaloNetStatus.Playing;
            if (alive != _wasAlive)
            {
                _wasAlive = alive;
                if (!alive) _queue.Clear();
                else _shownHealth = 1f;
            }

            FindAvatar();
            SetBoxesEnabled(alive);
            if (alive) PlaceBoxes();
            UpdateGuns(in st, alive, dt);
            UpdateCamo(st.Camo && alive, p);
            UpdateLabel(alive);
            FlushQueue(now);
        }

        // ------------------------------------------------------------------ the avatar

        private void FindAvatar()
        {
            Vector3 drawnHead = DrawnHead();
            float keep = Player.BridgeHead != null ? 1.0f : 1.8f;      // ours: how far the avatar's head may be from where it should be
            if (_avatar != null && (!_avatar.isActiveAndEnabled || (_avatar.GetBoneTransform(HumanBodyBones.Head) != null && (_avatar.GetBoneTransform(HumanBodyBones.Head).position - drawnHead).sqrMagnitude > keep * keep)))
            {
                RestoreCamo();
                _avatar = null;
                _avatarRenderers.Clear();
            }

            if (_avatar != null || Time.unscaledTime < _nextAvatarLook) return;
            _avatarLooks++;
            _nextAvatarLook = Time.unscaledTime + (_avatarLooks < 10 ? 1f : 5f);
            if (LabAvatarOf != null) _avatar = LabAvatarOf(Player.PlayerId);
            string kind = "lab";
            if (_avatar == null) _avatar = HaloNetAvatar.ByHead(drawnHead, out kind);
            if (_avatar == null) return;
            for (int i = 0; i < HumanBones.Length; i++) _bone[i] = _avatar.GetBoneTransform(HumanBones[i]);
            _avatarRenderers.Clear();
            _avatar.GetComponentsInChildren(true, _avatarRenderers);      // (the avatar model only: its root may be a holder of every player's avatar)
            HaloLog.Info("Net: " + Player.Name + "'s avatar found (" + kind + ", " + _avatarRenderers.Count + " renderers): the hit boxes follow its bones");
        }

        /// <summary>Where Somnium draws their head (the Bridge's head of their body), else where their record says.</summary>
        private Vector3 DrawnHead()
        {
            Transform h = Player.BridgeHead;
            return h != null ? h.position : Body.HeadPos;
        }

        /// <summary>Lab: the stand-in avatar of a scripted player (null in the world).</summary>
        public static System.Func<int, Animator> LabAvatarOf;

        private Vector3 BonePos(int i, Vector3 fallback) { return _bone[i] != null ? _bone[i].position : fallback; }

        // ------------------------------------------------------------------ hit boxes

        private void BuildBoxes()
        {
            for (int i = 0; i < BoxCount; i++)
            {
                var go = new GameObject("Hit " + BoxNames[i]);
                go.layer = HaloLayers.Hitbox;
                go.transform.SetParent(transform, false);
                _boxT[i] = go.transform;
                if (IsSphere[i]) { _sph[i] = go.AddComponent<SphereCollider>(); _sph[i].radius = 0.1f; }
                else { _cap[i] = go.AddComponent<CapsuleCollider>(); _cap[i].direction = 1; _cap[i].radius = 0.1f; _cap[i].height = 0.4f; }
                var hb = go.AddComponent<HaloNetHitbox>();
                hb.Player = Player;
                hb.Region = Region[i];
            }
        }

        private void SetBoxesEnabled(bool on)
        {
            if (on == _boxesOn) return;
            _boxesOn = on;
            for (int i = 0; i < BoxCount; i++)
            {
                if (_cap[i] != null) _cap[i].enabled = on;
                if (_sph[i] != null) _sph[i].enabled = on;
            }
        }

        private void Sphere(int i, Vector3 centre, float r)
        {
            _boxT[i].position = centre;
            if (_sph[i] != null && Mathf.Abs(_sph[i].radius - r) > 0.004f) _sph[i].radius = r;
        }

        private void Capsule(int i, Vector3 a, Vector3 c, float r)
        {
            Vector3 axis = c - a;
            float len = axis.magnitude;
            _boxT[i].position = (a + c) * 0.5f;
            if (len > 1e-4f) _boxT[i].rotation = Quaternion.FromToRotation(Vector3.up, axis / len);
            float h = Mathf.Max(len + 2f * r, 2f * r + 0.001f);
            CapsuleCollider cc = _cap[i];
            if (cc == null) return;
            if (Mathf.Abs(cc.radius - r) > 0.004f) cc.radius = r;
            if (Mathf.Abs(cc.height - h) > 0.006f) cc.height = h;
        }

        /// <summary>The boxes follow the avatar's humanoid bones when its head is where the record says (else the body of the record), sized by the player's height.</summary>
        private void PlaceBoxes()
        {
            float s = Body.Scale;
            Vector3 drawn = DrawnHead();
            float near = Player.BridgeHead != null ? 0.6f : 1.2f;
            _bones = _avatar != null && _bone[HHead] != null && _bone[HHips] != null && (_bone[HHead].position - drawn).sqrMagnitude < near * near;
            if (_bones)
            {
                Vector3 head = _bone[HHead].position, hips = _bone[HHips].position;
                Vector3 up = (head - hips).sqrMagnitude > 1e-4f ? (head - hips).normalized : Vector3.up;
                Vector3 neck = BonePos(HNeck, head - up * 0.1f * s), chest = BonePos(HChest, Vector3.Lerp(hips, neck, 0.65f)), spine = BonePos(HSpine, Vector3.Lerp(hips, neck, 0.35f));
                Sphere(BHead, head + up * (0.09f * s), 0.12f * s);
                Capsule(BTorsoU, neck, chest, 0.16f * s);
                Capsule(BTorsoM, chest, spine, 0.16f * s);
                Capsule(BTorsoL, spine, hips, 0.15f * s);
                Vector3 rh = BonePos(HRH, Body.RHandPos), lh = BonePos(HLH, Body.LHandPos);
                Capsule(BRArmU, BonePos(HRUA, Body.ShoulderR), BonePos(HRLA, Body.Elbow(Body.ShoulderR, rh, 1f)), 0.055f * s);
                Capsule(BLArmU, BonePos(HLUA, Body.ShoulderL), BonePos(HLLA, Body.Elbow(Body.ShoulderL, lh, -1f)), 0.055f * s);
                Capsule(BRArmL, BonePos(HRLA, Body.Elbow(Body.ShoulderR, rh, 1f)), rh, 0.045f * s);
                Capsule(BLArmL, BonePos(HLLA, Body.Elbow(Body.ShoulderL, lh, -1f)), lh, 0.045f * s);
                Sphere(BRHand, rh, 0.06f * s);
                Sphere(BLHand, lh, 0.06f * s);
                Vector3 rk = BonePos(HRLL, hips), lk = BonePos(HLLL, hips), rf = BonePos(HRF, Body.Feet), lf = BonePos(HLF, Body.Feet);
                Capsule(BRLegU, BonePos(HRUL, hips), rk, 0.09f * s);
                Capsule(BLLegU, BonePos(HLUL, hips), lk, 0.09f * s);
                Capsule(BRLegL, rk, rf, 0.07f * s);
                Capsule(BLLegL, lk, lf, 0.07f * s);
                Sphere(BRFoot, rf, 0.07f * s);
                Sphere(BLFoot, lf, 0.07f * s);
                return;
            }

            Vector3 u = Body.Up, right = Body.Right;
            Vector3 nk = Body.HeadPos - u * (0.2f * s), ch = Body.Chest, hp = Body.Hips;
            Vector3 mid = (ch + hp) * 0.5f;
            Sphere(BHead, Body.HeadPos, 0.12f * s);
            Capsule(BTorsoU, nk, ch, 0.16f * s);
            Capsule(BTorsoM, ch, mid, 0.16f * s);
            Capsule(BTorsoL, mid, hp, 0.15f * s);
            Vector3 eR = Body.Elbow(Body.ShoulderR, Body.RHandPos, 1f), eL = Body.Elbow(Body.ShoulderL, Body.LHandPos, -1f);
            Capsule(BRArmU, Body.ShoulderR, eR, 0.055f * s);
            Capsule(BLArmU, Body.ShoulderL, eL, 0.055f * s);
            Capsule(BRArmL, eR, Body.RHandPos, 0.045f * s);
            Capsule(BLArmL, eL, Body.LHandPos, 0.045f * s);
            Sphere(BRHand, Body.RHandPos, 0.06f * s);
            Sphere(BLHand, Body.LHandPos, 0.06f * s);
            Vector3 hipR = hp + right * (0.09f * s), hipL = hp - right * (0.09f * s);
            Vector3 ankleR = Body.Feet + right * (0.09f * s) + u * (0.08f * s), ankleL = Body.Feet - right * (0.09f * s) + u * (0.08f * s);
            Vector3 kneeR = (hipR + ankleR) * 0.5f + Body.Forward * (0.03f * s), kneeL = (hipL + ankleL) * 0.5f + Body.Forward * (0.03f * s);
            Capsule(BRLegU, hipR, kneeR, 0.09f * s);
            Capsule(BLLegU, hipL, kneeL, 0.09f * s);
            Capsule(BRLegL, kneeR, ankleR, 0.07f * s);
            Capsule(BLLegL, kneeL, ankleL, 0.07f * s);
            Sphere(BRFoot, ankleR, 0.07f * s);
            Sphere(BLFoot, ankleL, 0.07f * s);
        }

        /// <summary>A hit box's transform by part (the lab).</summary>
        public Transform Box(int part) { return part >= 0 && part < BoxCount ? _boxT[part] : null; }

        public const int HeadBox = BHead, ChestBox = BTorsoM;

        // ------------------------------------------------------------------ weapons

        private Gun GunFor(HaloTag tag)
        {
            if (tag == null) return null;
            for (int i = 0; i < _guns.Count; i++) if (_guns[i].Tag == tag) return _guns[i];
            if (!HaloNetGame.DrawWeapons || !HaloNetGame.TakeBuildTurn()) return null;
            var g = new Gun { Tag = tag };
            try
            {
                if (tag.Is("weap")) g.Def = new HaloWeaponDef(tag);
                HaloTag model = g.Def != null ? g.Def.Model : tag.Root.Ref(40);
                g.Long = g.Def != null && g.Def.LongGun;
                HaloModelBuilder.Built b = HaloModelBuilder.Build(model, transform, new HaloModelBuilder.Options { Lod = 3, CastShadows = true, Scale = WeaponScale });
                if (b != null)
                {
                    g.Root = b.Root.transform;
                    g.Muzzle = b.Marker("primary trigger");
                    g.Anim = HaloWeaponAnim.Attach(b, tag);      // null when the model has no moving part
                    b.Root.name = "Remote weapon " + System.IO.Path.GetFileName(tag.Name);
                    foreach (Collider c in b.Root.GetComponentsInChildren<Collider>(true)) Destroy(c);
                }
                else
                {
                    g.Root = StandIn(tag, g.Long, out g.Muzzle);
                }

                g.Root.gameObject.SetActive(false);
                HaloLog.Info("Net: " + Player.Name + "'s " + System.IO.Path.GetFileName(tag.Name) + " drawn from " + (b != null ? "its model" : "a stand-in"));
            }
            catch (System.Exception e)
            {
                HaloLog.Warn("Net: building " + tag.Name + " for " + Player.Name + " failed: " + e.Message);
                if (g.Root == null) g.Root = StandIn(tag, g.Long, out g.Muzzle);
            }

            _guns.Add(g);
            return g;
        }

        private Transform StandIn(HaloTag tag, bool longGun, out Transform muzzle)
        {
            var root = new GameObject("Remote weapon stand-in " + System.IO.Path.GetFileName(tag.Name)).transform;
            root.SetParent(transform, false);
            float len = longGun ? 0.7f : 0.22f;
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(root, false);
            box.transform.localScale = new Vector3(len, 0.08f, 0.045f);
            box.transform.localPosition = new Vector3(len * 0.35f, 0.02f, 0f);
            muzzle = new GameObject("primary trigger").transform;
            muzzle.SetParent(root, false);
            muzzle.localPosition = new Vector3(len * 0.85f, 0.03f, 0f);
            return root;
        }

        private void UpdateGuns(in HaloNetState st, bool alive, float dt)
        {
            HaloTag heldTag = alive ? HaloNetU.TagOf(st.Held) : null;
            HaloTag stowTag = alive ? HaloNetU.TagOf(st.Stowed) : null;
            // seated: the guns go on the avatar the Vehicles module seats and poses (HaloSeatPoses calls SeatedGuns after posing, LateUpdate 9997) -
            // never on the record's body: a monitor rider's record head is their chase camera, metres behind the vehicle, and every record is drawn
            // 0.16 s behind while the seat moves with the vehicle. Seated and not (yet) posed there: not drawn.
            bool seated = st.Vehicle >= 0;
            bool seatPosed = seated && Player != null && HaloSeatPoses.Owns(Player.PlayerId);
            Gun held = heldTag != null && (!seated || seatPosed) ? GunFor(heldTag) : null;
            Gun stowed = stowTag != null && stowTag != heldTag && (!seated || seatPosed) ? GunFor(stowTag) : null;
            bool inHand = st.InHand;
            Gun inHandGun = inHand ? held : null;
            Gun onBody1 = inHand ? null : held, onBody2 = stowed;
            // seated guns are drawn only while the seat pose places them (SeatedGuns ran this frame or the last): when the pose cannot run (the seat
            // label not in the cyborg graph, no seat marker, the cyborg reloading) they are hidden, never left at a stale place outside the vehicle
            bool showSeated = !seated || Time.frameCount - _seatPosedFrame <= 1;
            for (int i = 0; i < _guns.Count; i++)
            {
                Gun g = _guns[i];
                bool on = showSeated && (g == inHandGun || g == onBody1 || g == onBody2);
                if (g.Root != null && g.Root.gameObject.activeSelf != on) g.Root.gameObject.SetActive(on);
            }

            AnimateHeld(inHandGun, in st);
            _held = inHandGun;
            _stowed = onBody2 ?? onBody1;
            _swing = Mathf.MoveTowards(_swing, 0f, dt * 3.5f);
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 12f);
            if (!seated) _standScale = Body.Scale;
            _seatOnBody1 = onBody1; _seatOnBody2 = onBody2;
            _seated = seated;
            if (seated) return;
            float scale = Body.Scale;
            if (inHandGun != null && inHandGun.Root != null) PoseHeld(inHandGun, scale);
            if (onBody1 != null && onBody1.Root != null) PoseStowed(onBody1, scale, onBody2 != null && onBody2.Long == onBody1.Long);
            if (onBody2 != null && onBody2.Root != null) PoseStowed(onBody2, scale, false);
        }

        /// <summary>The held weapon's own animations from the record: "ready" when another weapon comes into the hand, "reload-empty" / "reload-full" when
        /// the reload counter advances, the vents held open while overheated (fire plays on each replayed shot).</summary>
        private void AnimateHeld(Gun now, in HaloNetState st)
        {
            int reloads = st.ReloadCounter;
            bool fresh = _animReloads < 0;
            if (now != _held && now != null && now.Anim != null && _held != null) now.Anim.Play("ready");
            if (!fresh && reloads != _animReloads && now != null && now.Anim != null && !now.Anim.Play(st.ReloadEmpty ? "reload-empty" : "reload-full")) now.Anim.Play("reload-full");
            _animReloads = reloads;
            bool hot = st.Overheated && now != null;
            if (hot != _animOverheated || (now != _held))
            {
                if (_held != null && _held.Anim != null && _held != now) _held.Anim.SetState("overheated", false);
                if (now != null && now.Anim != null) now.Anim.SetState("overheated", hot);
                _animOverheated = hot;
            }

            // the vent: the weapon's overheated effect loops on the held gun while the record's overheated bit is set (Combat's HaloGunFx, as the
            // local gun); it ends when the bit clears (the owner's heat fell to the recovery threshold), the gun leaves the hand or is hidden
            if (_ventGun != null && (_ventGun != now || !hot)) { if (_ventGun.Root != null) HaloGunFx.Vent(_ventGun.Root, _ventGun.Def, false); _ventGun = null; }
            if (hot && now.Root != null && now.Def != null && now.Root.gameObject.activeInHierarchy)
            {
                if (_ventGun == null) VentsShown++;
                HaloGunFx.Vent(now.Root, now.Def, true);
                _ventGun = now;
            }
        }

        private Gun _ventGun;
        /// <summary>Remote vents started (labs).</summary>
        public static int VentsShown;

        /// <summary>The weapon in the right hand: grip at the hand (the avatar's drawn hand when known), Halo's +X along the aim; long guns aim at the left hand when it holds the barrel.</summary>
        private void PoseHeld(Gun g, float scale)
        {
            Vector3 hand = _bones && _bone[HRH] != null ? _bone[HRH].position : Body.RHandPos;
            Quaternion aim = Body.RHandRot;
            Vector3 fwd = aim * Vector3.forward, up = aim * Vector3.up;
            if (g.Long && Body.Vr)
            {
                Vector3 toLeft = Body.LHandPos - Body.RHandPos;
                float along = Vector3.Dot(toLeft, fwd);
                if (along > 0.12f && toLeft.magnitude < 0.75f) fwd = toLeft.normalized;      // two hands on the gun
            }

            if (_swing > 0f) fwd = Quaternion.AngleAxis(-70f * Mathf.Sin(_swing * Mathf.PI), aim * Vector3.right) * fwd;      // the melee bash
            Quaternion rot = Quaternion.LookRotation(fwd, up) * Quaternion.Euler(0f, -90f, 0f);
            g.Root.SetPositionAndRotation(hand - fwd * (0.04f * _kick), rot);
            g.Root.localScale = Vector3.one * scale;
        }

        /// <summary>On the body (our holsters, the Combat module's choice): long guns over the back (a second long gun over the other shoulder), small ones on the hip.</summary>
        private void PoseStowed(Gun g, float scale, bool otherSide)
        {
            PoseStowedOn(g, scale, otherSide, Body.Forward, Body.Right, Body.Up, Body.ShoulderL, Body.ShoulderR, Body.Hips);
        }

        private float _standScale = 1f;
        private bool _seated;
        private Gun _seatOnBody1, _seatOnBody2;
        private bool _saidSeated;
        private int _seatPosedFrame = -10;

        /// <summary>
        /// Seated (the Vehicles module's HaloSeatPoses, after it posed the avatar on the seat, LateUpdate 9997): the weapon in hand at the seat
        /// pose's hand (heldValid: the seat allows weapons, Halo's warthog passenger), else on the body like the other one - the body frame from the
        /// posed avatar's bones (hips, upper arms, neck), not from the record.
        /// </summary>
        public void SeatedGuns(bool heldValid, Vector3 heldPos, Quaternion heldRot, Animator avatar)
        {
            if (!_seated) return;
            _seatPosedFrame = avatar != null ? Time.frameCount : -10;
            float scale = _standScale;
            Gun onBody1 = _seatOnBody1, onBody2 = _seatOnBody2;
            if (_held != null && _held.Root != null)
            {
                if (heldValid)
                {
                    if (avatar != null && !_held.Root.gameObject.activeSelf) _held.Root.gameObject.SetActive(true);      // (UpdateGuns hides seated guns not posed in the last frame)
                    _held.Root.SetPositionAndRotation(heldPos - heldRot * Vector3.right * (0.04f * _kick), heldRot);
                    _held.Root.localScale = Vector3.one * scale;
                }
                else if (onBody1 == null) onBody1 = _held;
                else _held.Root.gameObject.SetActive(false);
            }

            Transform hips = avatar != null ? avatar.GetBoneTransform(HumanBodyBones.Hips) : null;
            Transform neck = avatar != null ? (avatar.GetBoneTransform(HumanBodyBones.Neck) ?? avatar.GetBoneTransform(HumanBodyBones.Head)) : null;
            Transform la = avatar != null ? avatar.GetBoneTransform(HumanBodyBones.LeftUpperArm) : null;
            Transform ra = avatar != null ? avatar.GetBoneTransform(HumanBodyBones.RightUpperArm) : null;
            bool frame = hips != null && neck != null && la != null && ra != null;
            Vector3 U = frame ? (neck.position - hips.position) : Vector3.up;
            Vector3 R = frame ? (ra.position - la.position) : Vector3.right;
            if (U.sqrMagnitude < 1e-6f || R.sqrMagnitude < 1e-6f) frame = false;
            U = U.normalized;
            R = Vector3.ProjectOnPlane(R, U).normalized;
            Vector3 F = Vector3.Cross(R, U);
            if (!frame)
            {
                if (onBody1 != null && onBody1.Root != null) onBody1.Root.gameObject.SetActive(false);
                if (onBody2 != null && onBody2.Root != null) onBody2.Root.gameObject.SetActive(false);
                return;
            }

            // the shoulder points of the body frame HaloNetBody builds: the upper-arm heads, a little lower (scaled as there)
            Vector3 sl = la.position, sr = ra.position, hp = hips.position;
            if (onBody1 != null && onBody1.Root != null && !onBody1.Root.gameObject.activeSelf) onBody1.Root.gameObject.SetActive(true);
            if (onBody2 != null && onBody2.Root != null && !onBody2.Root.gameObject.activeSelf) onBody2.Root.gameObject.SetActive(true);
            if (onBody1 != null && onBody1.Root != null) PoseStowedOn(onBody1, scale, onBody2 != null && onBody2.Long == onBody1.Long, F, R, U, sl, sr, hp);
            if (onBody2 != null && onBody2.Root != null) PoseStowedOn(onBody2, scale, false, F, R, U, sl, sr, hp);
            if (!_saidSeated)
            {
                _saidSeated = true;
                HaloLog.Info("Net: " + (Player != null ? Player.ToString() : "a player") + " seated: weapons drawn on the seated avatar (" + (heldValid && _held != null ? "in hand: the seat animation's hand" : "on the body") + ").");
            }
        }

        private void PoseStowedOn(Gun g, float scale, bool otherSide, Vector3 F, Vector3 R, Vector3 U, Vector3 shoulderL, Vector3 shoulderR, Vector3 hips)
        {
            Vector3 pos;
            Vector3 fwd, up;
            if (g.Long)
            {
                float side = otherSide ? -1f : 1f;
                pos = (otherSide ? shoulderL : shoulderR) - F * (0.17f * scale) + U * (0.02f * scale);
                fwd = (U * 0.85f - R * (0.45f * side) - F * 0.05f).normalized;      // the barrel up over the shoulder, across the back
                up = -F;
            }
            else
            {
                float side = otherSide ? -1f : 1f;
                pos = hips + R * (0.21f * scale * side) - U * (0.05f * scale);
                fwd = (-U * 0.97f + F * 0.15f).normalized;      // muzzle down in a hip holster
                up = F;
            }

            g.Root.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, up) * Quaternion.Euler(0f, -90f, 0f));
            g.Root.localScale = Vector3.one * scale;
        }

        // ------------------------------------------------------------------ shots, throws, melee

        public void OnShot(in HaloNetEvent ev)
        {
            if (!_wasAlive) return;
            if (_queue.Count >= 8) _queue.RemoveAt(0);
            _queue.Add(new Queued { At = HaloNetGame.Clock() + ShotDelay, Ev = ev });
        }

        private void FlushQueue(double now)
        {
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (now < _queue[i].At) continue;
                HaloNetEvent ev = _queue[i].Ev;
                _queue.RemoveAt(i);
                PlayShot(in ev);
            }
        }

        private void PlayShot(in HaloNetEvent ev)
        {
            ShotsPlayed++;
            HaloTag weapon = HaloNetU.TagOf(ev.Tag);
            Gun g = _held != null && (weapon == null || _held.Tag == weapon) ? _held : null;
            Vector3 origin = g != null && g.Muzzle != null ? g.Muzzle.position : HaloNetU.V(ev.Pos);
            Vector3 dir = HaloNetU.V(ev.Dir);
            if (dir.sqrMagnitude < 0.5f) dir = g != null ? g.Root.right : Body.HeadRot * Vector3.forward;
            dir.Normalize();
            _kick = 1f;
            if (g != null && g.Anim != null) g.Anim.Play("fire");      // the weapon's own parts (slide, bolt, ...): HaloWeaponAnim
            HaloEvents.RaiseFired(weapon, origin, dir, Player.Slot);
            if (weapon != null && HaloVehicles.IsVehicleWeapon(weapon)) return;      // the Vehicles module replays its own guns (from the Fired event)
            var hook = HaloNetGame.ReplayShot;
            if (hook != null)
            {
                try { hook(weapon, origin, dir, Player.Slot, g != null ? g.Muzzle : null); }
                catch (System.Exception e) { HaloLog.Warn("Net: the ReplayShot hook failed: " + e.Message); }
                return;
            }

            if (HaloCombat.Catalog != null)
            {
                try { HaloCombat.ReplayShot(weapon, origin, dir, Player.Slot, g != null ? g.Muzzle : null); return; }
                catch (System.Exception e) { HaloLog.Warn("Net: HaloCombat.ReplayShot failed (Net's own replay instead): " + e.Message); }
            }

            HaloNetReplay.Shot(g != null ? g.Def : null, weapon, origin, dir, g != null ? g.Muzzle : null, ev.Count, transform);
        }

        public void OnThrow(in HaloNetEvent ev)
        {
            ThrowsPlayed++;
            HaloTag grenade = HaloNetU.TagOf(ev.Tag);
            Vector3 pos = HaloNetU.V(ev.Pos), vel = HaloNetU.V(ev.Vel);
            // the record is a moment old: the grenade has flown for that long already
            const float Lag = 0.12f;
            Vector3 g = Physics.gravity;
            pos += vel * Lag + 0.5f * g * Lag * Lag;
            vel += g * Lag;
            var hook = HaloNetGame.ReplayThrow;
            if (hook != null)
            {
                try { hook(grenade, pos, vel, Player.Slot); }
                catch (System.Exception e) { HaloLog.Warn("Net: the ReplayThrow hook failed: " + e.Message); }
                return;
            }

            if (HaloCombat.Catalog != null)
            {
                try { HaloCombat.ReplayThrow(grenade, pos, vel, Player.Slot); return; }
                catch (System.Exception e) { HaloLog.Warn("Net: HaloCombat.ReplayThrow failed (Net's own replay instead): " + e.Message); }
            }

            HaloNetReplay.Throw(grenade, pos, vel, this);
        }

        public void OnMelee()
        {
            Melees++;
            _swing = 1f;
        }

        public void OnDeath()
        {
            _queue.Clear();
            _shownHealth = 0f;
        }

        /// <summary>My hit landed (my own feedback until their record says more).</summary>
        public void FeedbackHit(HaloHit hit)
        {
            _shownHealth = Mathf.Max(0f, _shownHealth - 0.1f);
        }

        /// <summary>A thrown object released at this player's hand must not be pushed out of the hit boxes it starts inside.</summary>
        public void IgnoreCollisionWith(Collider other)
        {
            if (other == null) return;
            for (int i = 0; i < BoxCount; i++)
            {
                if (_cap[i] != null) Physics.IgnoreCollision(other, _cap[i]);
                if (_sph[i] != null) Physics.IgnoreCollision(other, _sph[i]);
            }
        }

        // ------------------------------------------------------------------ camo

        private void UpdateCamo(bool camo, HaloNetPlayer p)
        {
            // Halo's active camouflage: nearly invisible, seen for a moment when firing (ours: the avatar's renderers off, on for 0.25 s after a shot)
            bool hide = camo && Time.time - p.LastShotAt > 0.25f;
            if (hide == _camoHidden) return;
            _camoHidden = hide;
            for (int i = 0; i < _avatarRenderers.Count; i++)
                if (_avatarRenderers[i] != null) _avatarRenderers[i].forceRenderingOff = hide;
            for (int i = 0; i < _guns.Count; i++)
            {
                if (_guns[i].Root == null) continue;
                foreach (Renderer r in _guns[i].Root.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = hide;
            }

            if (_label != null) _label.gameObject.SetActive(!hide);
        }

        private void RestoreCamo()
        {
            if (!_camoHidden) return;
            _camoHidden = false;
            for (int i = 0; i < _avatarRenderers.Count; i++)
                if (_avatarRenderers[i] != null) _avatarRenderers[i].forceRenderingOff = false;
        }

        // ------------------------------------------------------------------ the name over the head

        private void BuildLabel()
        {
            try
            {
                var go = new GameObject("Name");
                go.transform.SetParent(transform, false);
                _label = go.AddComponent<TextMesh>();
                Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (f != null)
                {
                    _label.font = f;
                    go.GetComponent<MeshRenderer>().sharedMaterial = f.material;
                }

                _label.anchor = TextAnchor.LowerCenter;
                _label.alignment = TextAlignment.Center;
                _label.characterSize = 0.025f;
                _label.fontSize = 48;
                go.SetActive(false);
            }
            catch (System.Exception e)
            {
                HaloLog.Warn("Net: the name label failed: " + e.Message);
                _label = null;
            }
        }

        /// <summary>Halo: a team mate's name always shows over them; an enemy's only while the crosshair is on them (ours: within 2.5 degrees of my view, 40 m).</summary>
        private void UpdateLabel(bool alive)
        {
            if (_label == null) return;
            Camera cam = HaloPlayer.Head();
            bool show = alive && cam != null && !_camoHidden;
            if (show)
            {
                Vector3 at = Body.HeadPos + Vector3.up * (0.3f * Body.Scale);
                Vector3 to = at - cam.transform.position;
                float d = to.magnitude;
                bool mate = !Player.Enemy;
                bool aimed = d < 40f && Vector3.Angle(cam.transform.forward, to) < 2.5f;
                show = mate ? d < 80f : aimed;
                if (show)
                {
                    _label.transform.position = at;
                    _label.transform.rotation = Quaternion.LookRotation(to.sqrMagnitude > 1e-4f ? to : cam.transform.forward, Vector3.up);
                    float k = Mathf.Clamp(d / 6f, 0.6f, 6f);
                    _label.transform.localScale = Vector3.one * k;
                    if (_label.text != Player.Name) _label.text = Player.Name;
                    Color c = HaloNetGame.Net != null && HaloNetGame.Net.TeamGame ? HaloTeams.Of(Player.Team) : (Player.Enemy ? new Color(0.9f, 0.2f, 0.15f) : Color.white);
                    if (_label.color != c) _label.color = c;
                }
            }

            if (_label.gameObject.activeSelf != show) _label.gameObject.SetActive(show);
        }

        // ------------------------------------------------------------------ the end

        public void Retire()
        {
            RestoreCamo();
            for (int i = 0; i < _guns.Count; i++) if (_guns[i].Root != null) { HaloGunFx.Forget(_guns[i].Root); Destroy(_guns[i].Root.gameObject); }
            _ventGun = null;
            _guns.Clear();
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}

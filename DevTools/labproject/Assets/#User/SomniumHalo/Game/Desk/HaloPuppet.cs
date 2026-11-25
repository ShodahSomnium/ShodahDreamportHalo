using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>What a player is doing, for the puppet (HaloDeskBody fills it from Combat / HaloLocal for the own avatar, from Net's report for others).</summary>
    public struct HaloPuppetInput
    {
        public HaloTag Weapon;                 // the held weap (null = unarmed)
        public bool Crouch, Zoomed;
        public int Fire, Reload, Melee, Throw; // event counters: a change plays the event once
        public float Speed, MoveAngle;         // planar m/s; degrees from the aim (0 ahead, +90 right)
        public string Seat;                    // a vehicle seat label ("W-driver") or null / "" on foot
    }

    /// <summary>
    /// One frame of the cyborg's body as the avatar is posed from it (HaloPuppetPoser): BO2's / Skyrim's body sample on Halo's skeleton. Frame: the
    /// puppet's own (x right, y up, z forward = where the player aims, the way Halo's biped faces), angles in degrees relative to the rifle's standing
    /// idle; hands in that aim frame (the poser turns them with the aim's pitch). Seated: <see cref="Seated"/> and the joints in the seat marker's frame.
    /// </summary>
    public struct HaloPuppetSample
    {
        public bool Valid;
        public Vector3 HipsOffset;              // pelvis now - standing, in leg lengths
        public float HipsYaw, HipsRoll;
        public Vector3 RFoot, LFoot;            // ankles: x / z from the origin, y above the standing ankle height, in leg lengths
        public Vector3 RKneePole, LKneePole;
        public float RToePitch, LToePitch;
        public float Twist, Bend, Lean;         // chest vs the pelvis' facing
        public float HeadYaw, HeadPitch;        // head vs chest
        public Vector3 RHand, LHand;            // wrists from the shoulder midpoint, in arm lengths (aim frame)
        public Vector3 RElbowPole, LElbowPole;  // unit (aim frame)
        public Quaternion RGrip, LGrip;         // grip frames (forward = little -> index knuckle, up = -(wrist -> middle knuckle)), aim frame
        public Quaternion GunInRGrip;           // the gun frame (+Z barrel, +Y up: the "right hand" marker) in the right grip frame
        public Vector3 RWristOnGun;             // the right wrist in the gun frame, metres at the cyborg's size
        public Vector3 LWristOnGun;             // the left wrist from the gun origin, metres at the cyborg's size, gun frame (the animation's)
        public Quaternion LGripOnGun;           // the left grip frame in the gun frame (the animation's)
        public float AimWeight;                 // 1 = the gun is up and aimed (idle, fire, walk); 0 = reload, ready, throw, melee
        public float GunLeft;                   // unused by Halo (BO2: the left hand keeps the gun during a grenade); kept 0
        // seated (vehicle): joints in the seat marker's frame, Unity axes (HaloSpace mapping), metres at the cyborg's size
        public bool Seated;
        public Vector3 SPelvis, SChest, SNeck, SHead, SLSh, SRSh, SLEl, SREl, SLWr, SRWr, SLHip, SRHip, SLKnee, SRKnee, SLAnk, SRAnk;
        public Quaternion SPelvisRot;           // pelvis frame (forward = Halo forward of the seat, up = the spine), seat frame
        public Quaternion SLGrip, SRGrip;       // grip frames, seat frame
    }

    /// <summary>
    /// The Skyrim / BO2 Dreamports' puppet on Halo: a hidden copy of the multiplayer cyborg's skeleton (no GameObject, no renderer: HaloPose on the CPU)
    /// playing the game's own third-person animations from the cyborg's model_animations, picked the way the game picks them: the unit seat ("stand",
    /// "crouch", or the vehicle seat label), the weapon class the held weapon's label belongs to ("ar" -> "rifle", "hp" -> "pistol", "rl" -> "missile",
    /// ...), then the class animation (idle, move-front / back / left / right at the avatar's pace) and on top the weapon type's events (fire-1 overlay,
    /// reload-1 / melee replacement, the class's ready after a switch and throw-grenade on the upper body). Read each frame as joint positions and hand /
    /// gun frames (the "right hand" marker = where Halo attaches the weapon). No allocation per frame after the first use of each animation.
    /// </summary>
    public sealed class HaloPuppet
    {
        public enum Shot { None, Fire, Reload, Ready, Throw, Melee }

        private struct Ref
        {
            public bool Valid;
            public float Arm, Leg, AnkleY, HipsYaw, HipsRoll, Twist, Bend, Lean, HeadYaw, HeadPitch, RToe, LToe;
            public Vector3 Pelvis;
        }

        private const int JPelvis = 0, JChest = 1, JNeck = 2, JHead = 3, JHeadFwd = 4, JLSh = 5, JRSh = 6, JLEl = 7, JREl = 8, JLWr = 9, JRWr = 10,
            JLHip = 11, JRHip = 12, JLKnee = 13, JRKnee = 14, JLAnk = 15, JRAnk = 16, JLToe = 17, JRToe = 18, Count = 19;

        private readonly HaloCyborg _c;
        private readonly HaloPose _pose;
        private readonly Vector3[] _j = new Vector3[Count];
        private readonly float[] _q = new float[4];
        private Vector3 _hf = Vector3.right;      // the head node's own axis that points forward in the rest pose (Halo axes)
        private Ref _ref;
        private string _why;

        private HaloTag _weapon;
        private string _seat;
        private bool _crouch;
        private HaloAnimGraph.Seat _seatBlock;
        private HaloAnimGraph.WeaponClass _class;
        private HaloAnimGraph.WeaponType _type;
        private HaloAnimation _base, _prev, _shot;
        private float _tBase, _tPrev, _fade = 1f, _rate = 1f, _tShot, _shotW, _aimW = 1f;
        private Shot _kind;
        private bool _counters;
        private int _fire, _reload, _melee, _throw;
        private Vector3 _gunF = Vector3.forward, _gunU = Vector3.up, _gunP;

        /// <summary>This frame's sample (Valid false before Step found a pose).</summary>
        public HaloPuppetSample Sample;

        public string BaseName { get { return _base != null ? _base.Name : null; } }
        public string ShotName { get { return _shot != null ? _shot.Name : null; } }
        public Shot Playing { get { return _shot != null ? _kind : Shot.None; } }
        public float ShotTime { get { return _tShot; } }
        public float ShotLength { get { return _shot != null ? _shot.Duration : 0f; } }
        public float ArmLength { get { return _ref.Arm; } }
        public float LegLength { get { return _ref.Leg; } }
        public string ClassName { get { return (_seatBlock != null ? _seatBlock.Label : "-") + " " + (_class != null ? _class.Name : "-") + (_type != null ? "/" + _type.Label : ""); } }
        public HaloCyborg Cyborg { get { return _c; } }

        /// <summary>A puppet on the cyborg (null + why when the standing idle or the skeleton is missing).</summary>
        public static HaloPuppet Create(HaloCyborg c, out string why)
        {
            why = null;
            if (c == null) { why = "no cyborg"; return null; }
            var p = new HaloPuppet(c);
            if (!p._ref.Valid) { why = p._why ?? "no reference pose"; return null; }
            return p;
        }

        private HaloPuppet(HaloCyborg c)
        {
            _c = c;
            _pose = new HaloPose(c.Skel);
            // the rest pose: lengths and the head's forward axis
            _pose.Reset();
            _pose.ToModel();
            var bind = new Vector3[Count];
            Read(bind);
            float x, y, z;
            InverseAxis(c.Head, 1f, 0f, 0f, out x, out y, out z);
            _hf = new Vector3(x, y, z);
            // standing: the rifle's standing idle, frame 0
            HaloAnimation stand = c.Graph.Get("stand rifle idle");
            HaloAnimGraph.Seat st = c.Graph.FindSeat("stand");
            if (stand == null && st != null) { HaloAnimGraph.WeaponClass w = st.Weapon("rifle") ?? (st.Weapons.Count > 0 ? st.Weapons[0] : null); if (w != null) stand = c.Graph.Get(w.Anim(HaloAnimIds.Weapon.Idle)); }
            if (stand == null || !stand.Decode()) { _why = "no standing idle animation" + (stand != null ? " (" + stand.Error + ")" : ""); return; }
            _pose.Reset();
            _pose.Apply(stand, c.NodeMap, 0f, true, 1f);
            _pose.ToModel();
            var standJ = new Vector3[Count];
            Read(standJ);
            Reference(standJ, bind);
        }

        private void InverseAxis(int node, float ax, float ay, float az, out float x, out float y, out float z)
        {
            int i = node * 4;
            _q[0] = -_pose.MRot[i]; _q[1] = -_pose.MRot[i + 1]; _q[2] = -_pose.MRot[i + 2]; _q[3] = _pose.MRot[i + 3];
            HQ.Rotate(_q, 0, ax, ay, az, out x, out y, out z);
        }

        // ------------------------------------------------------------------ animation

        /// <summary>Steps the animation for this frame and reads the sample.</summary>
        public void Step(in HaloPuppetInput i, float dt)
        {
            Sample.Valid = false;
            if (!_ref.Valid) return;
            _death = null;      // (alive again)
            string seat =string.IsNullOrEmpty(i.Seat) ? (i.Crouch ? "crouch" : "stand") : i.Seat;
            bool seated = !string.IsNullOrEmpty(i.Seat);
            if (i.Weapon != _weapon || seat != _seat || _seatBlock == null)
            {
                bool swap = _weapon != null && i.Weapon != _weapon && seat == _seat;
                _weapon = i.Weapon;
                _seat = seat;
                _seatBlock = _c.Graph.FindSeat(seat) ?? _c.Graph.FindSeat("stand");
                HaloCyborg.WeaponGrip g = _c.Grip(i.Weapon);
                string label = g != null ? g.Label : null;
                _class = i.Weapon == null ? (_seatBlock != null ? _seatBlock.Weapon("unarmed") : null) : _c.Graph.ClassFor(_seatBlock, label, out _type);
                if (i.Weapon == null) _type = null;
                if (_class == null && _seatBlock != null && _seatBlock.Weapons.Count > 0) _class = _seatBlock.Weapons[0];      // (a seat with one class: "unarmed" / "fixed")
                if (seated && _class != null && _class.Anim(HaloAnimIds.Weapon.Idle) < 0 && _seatBlock != null)
                    for (int k = 0; k < _seatBlock.Weapons.Count; k++) if (_seatBlock.Weapons[k].Anim(HaloAnimIds.Weapon.Idle) >= 0) { _class = _seatBlock.Weapons[k]; break; }
                if (swap) Start(Shot.Ready, ClassAnim(HaloAnimIds.Weapon.Ready));
            }

            _crouch = i.Crouch;
            bool moving = !seated && i.Speed > 0.3f;
            int id = HaloAnimIds.Weapon.Idle;
            if (moving)
            {
                float a = Mathf.Abs(i.MoveAngle);
                id = a < 50f ? HaloAnimIds.Weapon.MoveFront : a > 130f ? HaloAnimIds.Weapon.MoveBack : i.MoveAngle > 0f ? HaloAnimIds.Weapon.MoveRight : HaloAnimIds.Weapon.MoveLeft;
            }

            HaloAnimation b = ClassAnim(id);
            if (b == null && id != HaloAnimIds.Weapon.Idle) b = ClassAnim(HaloAnimIds.Weapon.Idle);
            if (b == null) b = _c.Graph.Get("stand rifle idle");
            if (b != null && b != _base && b.Decode())
            {
                float phase = _base != null && moving && _base.Duration > 0f ? _tBase / _base.Duration : 0f;      // cycles keep their phase
                if (_base != null) { _prev = _base; _tPrev = _tBase; _fade = 0f; }
                _base = b;
                _tBase = phase * b.Duration;
            }

            // the feet keep the pace the avatar really moves (the animation's own root motion: dx per frame)
            float own = moving && _base != null ? RootSpeed(_base) : 0f;
            _rate = own > 0.2f ? Mathf.Clamp(i.Speed / own, 0.5f, 1.6f) : 1f;

            // the events
            if (!_counters)
            {
                _counters = true;
                _fire = i.Fire; _reload = i.Reload; _melee = i.Melee; _throw = i.Throw;
            }
            else
            {
                if (i.Throw != _throw) Start(Shot.Throw, ClassAnim(HaloAnimIds.Weapon.ThrowGrenade));
                else if (i.Melee != _melee) Start(Shot.Melee, TypeAnim(HaloAnimIds.WeaponType.Melee) ?? ClassAnim(HaloAnimIds.Weapon.Melee));
                else if (i.Reload != _reload) Start(Shot.Reload, TypeAnim(HaloAnimIds.WeaponType.Reload1));
                else if (i.Fire != _fire && (_shot == null || _kind == Shot.Fire)) Start(Shot.Fire, TypeAnim(HaloAnimIds.WeaponType.Fire1));
                _fire = i.Fire; _reload = i.Reload; _melee = i.Melee; _throw = i.Throw;
            }

            // time
            _tBase = Advance(_base, _tBase, dt * _rate);
            if (_prev != null)
            {
                _tPrev = Advance(_prev, _tPrev, dt * _rate);
                _fade += dt / 0.2f;
                if (_fade >= 1f) _prev = null;
            }

            if (_shot != null)
            {
                _tShot += dt;
                float len = _shot.Duration;
                bool ending = _tShot >= len - 0.12f;
                _shotW = Mathf.MoveTowards(_shotW, ending ? 0f : 1f, dt / (ending ? 0.12f : (_kind == Shot.Fire ? 0.03f : 0.1f)));
                if (ending && (_shotW <= 0f || _tShot >= len + 0.2f)) { _shot = null; _shotW = 0f; _kind = Shot.None; }
            }

            float aimT = 1f;
            if (_shot != null && _kind != Shot.Fire) aimT = 1f - _shotW;
            _aimW = Mathf.MoveTowards(_aimW, aimT, dt * 5f);

            // the pose: the base (cross-faded), the event on top
            if (_base == null) return;
            _pose.Reset();
            if (_prev != null)
            {
                _pose.Apply(_prev, _c.NodeMap, _tPrev, true, 1f);
                _pose.Apply(_base, _c.NodeMap, _tBase, true, Mathf.Clamp01(_fade));
            }
            else _pose.Apply(_base, _c.NodeMap, _tBase, true, 1f);
            if (_shot != null && _shotW > 0.001f) ApplyShot(_shot, Mathf.Min(_tShot, _shot.Duration), _shotW);
            _pose.ToModel();
            Read(_j);
            if (seated) FillSeat(); else Fill();
        }

        // ------------------------------------------------------------------ death

        private HaloAnimation _death;
        private float _tDeath;

        /// <summary>Playing a death (since <see cref="StepDeath"/> started it; the next <see cref="Step"/> ends it).</summary>
        public bool Dying { get { return _death != null; } }
        public string DeathName { get { return _death != null ? _death.Name : null; } }
        /// <summary>The death animation's root motion so far (Halo: dx, dy in the unit's frame; metres in the seat frame: x forward, z left).</summary>
        public Vector3 DeathSlide { get; private set; }

        /// <summary>
        /// The game's death animation from the unit damage table ("s-kill front gut" ... "h-kill back gut"; hard = explosion / big hit, dir 0 front,
        /// 1 left, 2 right, 3 back, region 0 gut, 1 chest, 2 head), played once and held on its last frame; the sample is the whole body in the frame
        /// of the spot the player died on (as a seat: x forward, y up, z left). False without one.
        /// </summary>
        public bool StepDeath(bool hard, int dir, int region, float dt)
        {
            Sample.Valid = false;
            if (!_ref.Valid) return false;
            if (_death == null)
            {
                _death = _c.Graph.Damage(true, hard, dir, region);
                if (_death == null || !_death.Decode()) { _death = null; return false; }
                _tDeath = 0f;
                _shot = null;
                _kind = Shot.None;
                _base = null;      // (the next life starts fresh)
                _prev = null;
            }

            _tDeath = Mathf.Min(_tDeath + dt, _death.Duration);
            int f0, f1;
            float u;
            HaloPose.FrameAt(_death, _tDeath, false, out f0, out f1, out u);
            float sx = 0f, sy = 0f;
            if (_death.Info != null) for (int f = 0; f <= f0 && f < _death.Frames; f++) { sx += _death.Info[f * 4]; sy += _death.Info[f * 4 + 1]; }
            DeathSlide = new Vector3(sx * HaloSpace.Unit, 0f, sy * HaloSpace.Unit);
            _pose.Reset();
            _pose.ApplyFrames(_death, _c.NodeMap, f0, f1, u, 1f);
            _pose.ToModel();
            Read(_j);
            FillSeat();
            return true;
        }

        private HaloAnimation ClassAnim(int id) { return _class != null ? _c.Graph.Get(_class.Anim(id)) : null; }
        private HaloAnimation TypeAnim(int id) { return _type != null ? _c.Graph.Get(_type.Anim(id)) : null; }

        private void Start(Shot kind, HaloAnimation a)
        {
            if (a == null || !a.Decode()) return;
            if (_shot == null) _shotW = 0f;
            _shot = a;
            _kind = kind;
            _tShot = 0f;
        }

        /// <summary>An event animation: overlays turn on top, replacements replace the nodes they animate, a base-type one (throw-grenade) only the upper body.</summary>
        private void ApplyShot(HaloAnimation a, float t, float w)
        {
            int f0, f1;
            float u;
            HaloPose.FrameAt(a, t, false, out f0, out f1, out u);
            if (a.Type == HaloAnimation.Kind.Overlay) { _pose.Overlay(a, _c.NodeMap, f0, f1, u, w); return; }
            if (a.Type == HaloAnimation.Kind.Replacement) { _pose.ApplyFrames(a, _c.NodeMap, f0, f1, u, w); return; }
            // base type: keep the legs and pelvis of what plays
            int n = _c.Skel.Count;
            for (int k = 0; k < n; k++) { _saveR[k * 4] = _pose.LRot[k * 4]; _saveR[k * 4 + 1] = _pose.LRot[k * 4 + 1]; _saveR[k * 4 + 2] = _pose.LRot[k * 4 + 2]; _saveR[k * 4 + 3] = _pose.LRot[k * 4 + 3]; }
            for (int k = 0; k < n * 3; k++) _saveT[k] = _pose.LTrans[k];
            _pose.ApplyFrames(a, _c.NodeMap, f0, f1, u, w);
            for (int k = 0; k < n; k++)
            {
                if (_c.Upper[k]) continue;
                for (int c = 0; c < 4; c++) _pose.LRot[k * 4 + c] = _saveR[k * 4 + c];
                for (int c = 0; c < 3; c++) _pose.LTrans[k * 3 + c] = _saveT[k * 3 + c];
            }
        }

        private float[] _saveR = new float[64 * 4], _saveT = new float[64 * 3];

        private static float Advance(HaloAnimation a, float t, float step)
        {
            if (a == null) return 0f;
            t += step;
            float d = a.Duration;
            if (d <= 0f) return 0f;
            if (t >= d) t -= d * Mathf.Floor(t / d);
            return t;
        }

        /// <summary>The animation's own walking speed (m/s): its root motion (dx, dy per frame) over its length.</summary>
        private static float RootSpeed(HaloAnimation a)
        {
            if (a.Info == null || a.Frames <= 0) return 0f;
            float sx = 0f, sy = 0f;
            for (int f = 0; f < a.Frames; f++) { sx += a.Info[f * 4]; sy += a.Info[f * 4 + 1]; }
            return Mathf.Sqrt(sx * sx + sy * sy) * HaloSpace.Unit / Mathf.Max(1e-3f, a.Duration);
        }

        // ------------------------------------------------------------------ reading the pose

        private void Read(Vector3[] into)
        {
            HaloCyborg c = _c;
            into[JPelvis] = Pt(c.Pelvis);
            into[JChest] = Pt(c.Spine1);
            into[JNeck] = Pt(c.Neck);
            into[JHead] = Pt(c.Head);
            float x, y, z;
            _pose.Point(c.Head, _hf.x * 0.05f, _hf.y * 0.05f, _hf.z * 0.05f, out x, out y, out z);
            into[JHeadFwd] = HaloCyborg.P(x, y, z);
            into[JLSh] = Pt(c.LUpper); into[JRSh] = Pt(c.RUpper);
            into[JLEl] = Pt(c.LFore); into[JREl] = Pt(c.RFore);
            into[JLWr] = Pt(c.LHand); into[JRWr] = Pt(c.RHand);
            into[JLHip] = Pt(c.LThigh); into[JRHip] = Pt(c.RThigh);
            into[JLKnee] = Pt(c.LCalf); into[JRKnee] = Pt(c.RCalf);
            into[JLAnk] = Pt(c.LFoot); into[JRAnk] = Pt(c.RFoot);
            HaloSkeleton.Marker lf = c.LeftFootMarker, rf = c.RightFootMarker;
            _pose.Point(c.LFoot, lf.Tx, lf.Ty, lf.Tz, out x, out y, out z); into[JLToe] = HaloCyborg.P(x, y, z);
            _pose.Point(c.RFoot, rf.Tx, rf.Ty, rf.Tz, out x, out y, out z); into[JRToe] = HaloCyborg.P(x, y, z);
            if (c.HasRightMarker)
            {
                float px, py, pz, fx, fy, fz, ux, uy, uz;
                _pose.MarkerFrame(c.RightMarker, out px, out py, out pz, out fx, out fy, out fz, out ux, out uy, out uz);
                _gunP = HaloCyborg.P(px, py, pz);
                _gunF = HaloCyborg.D(fx, fy, fz);
                _gunU = HaloCyborg.D(ux, uy, uz);
            }
            else
            {
                _gunP = into[JRWr];
                _gunF = Vector3.forward;
                _gunU = Vector3.up;
            }
        }

        private Vector3 Pt(int node)
        {
            if (node < 0) return Vector3.zero;
            float[] t = _pose.MTrans;
            return HaloCyborg.P(t[node * 3], t[node * 3 + 1], t[node * 3 + 2]);
        }

        /// <summary>A hand's grip frame (puppet frame): Halo's biped hands run along their x (wrist -> knuckles), the knuckles' line little -> index along z (the fp arms' fingers show it).</summary>
        private Quaternion HandGrip(int node)
        {
            float zx, zy, zz, xx, xy, xz;
            _pose.Axis(node, 0f, 0f, 1f, out zx, out zy, out zz);
            _pose.Axis(node, 1f, 0f, 0f, out xx, out xy, out xz);
            Vector3 f = HaloCyborg.D(zx, zy, zz), up = -HaloCyborg.D(xx, xy, xz);
            return Quaternion.LookRotation(f, up);
        }

        private void Fill()
        {
            float leg = _ref.Leg, arm = _ref.Arm;
            Vector3[] j = _j;
            Sample.Seated = false;
            Sample.HipsOffset = (j[JPelvis] - _ref.Pelvis) / leg;
            float hipsYaw = HipsYawOf(j);
            Sample.HipsYaw = Mathf.DeltaAngle(_ref.HipsYaw, hipsYaw);
            Sample.HipsRoll = Mathf.DeltaAngle(_ref.HipsRoll, HipsRollOf(j));
            Vector3 la = j[JLAnk], ra = j[JRAnk];
            Sample.LFoot = new Vector3(la.x / leg, (la.y - _ref.AnkleY) / leg, la.z / leg);
            Sample.RFoot = new Vector3(ra.x / leg, (ra.y - _ref.AnkleY) / leg, ra.z / leg);
            Sample.LKneePole = Pole(j[JLHip], j[JLKnee], la, Vector3.forward);
            Sample.RKneePole = Pole(j[JRHip], j[JRKnee], ra, Vector3.forward);
            Sample.LToePitch = ToePitchOf(j, JLAnk, JLToe) - _ref.LToe;
            Sample.RToePitch = ToePitchOf(j, JRAnk, JRToe) - _ref.RToe;
            float tw, be, le, hy, hp;
            TorsoAngles(j, hipsYaw, out tw, out be, out le);
            Sample.Twist = Mathf.DeltaAngle(_ref.Twist, tw);
            Sample.Bend = Mathf.DeltaAngle(_ref.Bend, be);
            Sample.Lean = Mathf.DeltaAngle(_ref.Lean, le);
            HeadAngles(j, out hy, out hp);
            Sample.HeadYaw = Mathf.DeltaAngle(_ref.HeadYaw, hy);
            Sample.HeadPitch = Mathf.DeltaAngle(_ref.HeadPitch, hp);

            Vector3 mid = (j[JLSh] + j[JRSh]) * 0.5f;
            Sample.RHand = (j[JRWr] - mid) / arm;
            Sample.LHand = (j[JLWr] - mid) / arm;
            Sample.RElbowPole = Pole(j[JRSh], j[JREl], j[JRWr], Vector3.right);
            Sample.LElbowPole = Pole(j[JLSh], j[JLEl], j[JLWr], Vector3.left);
            FillGun(j);
            Sample.AimWeight = _aimW;
            Sample.GunLeft = 0f;
            Sample.Valid = true;
        }

        /// <summary>The grips and the gun (the "right hand" marker) relative to each other.</summary>
        private void FillGun(Vector3[] j)
        {
            Quaternion rg = HandGrip(_c.RHand), lg = HandGrip(_c.LHand);
            Sample.RGrip = rg;
            Sample.LGrip = lg;
            Quaternion gun = Quaternion.LookRotation(_gunF, _gunU);
            Quaternion gunInv = Quaternion.Inverse(gun);
            Sample.GunInRGrip = Quaternion.Inverse(rg) * gun;
            Sample.RWristOnGun = gunInv * (j[JRWr] - _gunP);
            Sample.LWristOnGun = gunInv * (j[JLWr] - _gunP);
            Sample.LGripOnGun = gunInv * lg;
        }

        /// <summary>Seated: the joints straight in the seat marker's frame (the seat animations stand the pelvis on the marker, facing its +x).</summary>
        private void FillSeat()
        {
            Vector3[] j = _j;
            Sample.Seated = true;
            Sample.SPelvis = S(j[JPelvis]); Sample.SChest = S(j[JChest]); Sample.SNeck = S(j[JNeck]); Sample.SHead = S(j[JHead]);
            Sample.SLSh = S(j[JLSh]); Sample.SRSh = S(j[JRSh]); Sample.SLEl = S(j[JLEl]); Sample.SREl = S(j[JREl]);
            Sample.SLWr = S(j[JLWr]); Sample.SRWr = S(j[JRWr]); Sample.SLHip = S(j[JLHip]); Sample.SRHip = S(j[JRHip]);
            Sample.SLKnee = S(j[JLKnee]); Sample.SRKnee = S(j[JRKnee]); Sample.SLAnk = S(j[JLAnk]); Sample.SRAnk = S(j[JRAnk]);
            Vector3 up = Sample.SChest - Sample.SPelvis;
            Vector3 across = Sample.SRHip - Sample.SLHip;      // (seat frame: x forward, z left -> right = -z)
            Vector3 fwd = Vector3.Cross(across, up);
            if (up.sqrMagnitude < 1e-6f || fwd.sqrMagnitude < 1e-8f) Sample.SPelvisRot = Quaternion.LookRotation(Vector3.right, Vector3.up);
            else Sample.SPelvisRot = Quaternion.LookRotation(fwd.normalized, up.normalized);
            Sample.SLGrip = SeatRot(HandGrip(_c.LHand));
            Sample.SRGrip = SeatRot(HandGrip(_c.RHand));
            FillGun(j);
            Sample.AimWeight = _aimW;
            Sample.Valid = true;
        }

        /// <summary>Puppet frame (x right, y up, z forward) to the seat marker's Unity frame (HaloSpace: x forward, y up, z left).</summary>
        private static Vector3 S(Vector3 p) { return new Vector3(p.z, p.y, -p.x); }
        private static Quaternion SeatRot(Quaternion q) { return Quaternion.LookRotation(S(q * Vector3.forward), S(q * Vector3.up)); }

        private void Reference(Vector3[] stand, Vector3[] bind)
        {
            var r = new Ref();
            r.Leg = 0.5f * ((bind[JLKnee] - bind[JLHip]).magnitude + (bind[JLAnk] - bind[JLKnee]).magnitude + (bind[JRKnee] - bind[JRHip]).magnitude + (bind[JRAnk] - bind[JRKnee]).magnitude);
            r.Arm = 0.5f * ((bind[JLEl] - bind[JLSh]).magnitude + (bind[JLWr] - bind[JLEl]).magnitude + (bind[JREl] - bind[JRSh]).magnitude + (bind[JRWr] - bind[JREl]).magnitude);
            if (r.Leg < 1e-3f) r.Leg = Mathf.Max(1e-3f, bind[JPelvis].y);
            r.Pelvis = stand[JPelvis];
            r.AnkleY = 0.5f * (stand[JLAnk].y + stand[JRAnk].y);
            r.HipsYaw = HipsYawOf(stand);
            r.HipsRoll = HipsRollOf(stand);
            TorsoAngles(stand, r.HipsYaw, out r.Twist, out r.Bend, out r.Lean);
            HeadAngles(stand, out r.HeadYaw, out r.HeadPitch);
            r.LToe = ToePitchOf(stand, JLAnk, JLToe);
            r.RToe = ToePitchOf(stand, JRAnk, JRToe);
            r.Valid = r.Arm > 0.1f;
            if (!r.Valid) _why = "arm length " + r.Arm.ToString("0.00") + " m";
            _ref = r;
        }

        // ------------------------------------------------------------------ the Skyrim Dreamport's sample maths (SkyHumanoid), as BO2's puppet

        private struct Frame { public Vector3 R, U, F; }

        private static Vector3 Norm(Vector3 a, Vector3 fallback) { float l = a.magnitude; return l > 1e-6f ? a / l : fallback; }

        private static Frame ChestFrame(Vector3[] j)
        {
            Vector3 up = Norm(j[JNeck] - j[JPelvis], Vector3.up);
            Vector3 across = j[JRSh] - j[JLSh];
            Vector3 right = Norm(across - up * Vector3.Dot(across, up), Vector3.right);
            return new Frame { R = right, U = up, F = Vector3.Cross(right, up) };
        }

        private static float HipsYawOf(Vector3[] j) { Vector3 rv = j[JRHip] - j[JLHip]; return Mathf.Atan2(-rv.z, rv.x) * Mathf.Rad2Deg; }

        private static float HipsRollOf(Vector3[] j) { Vector3 rv = j[JRHip] - j[JLHip]; return Mathf.Atan2(rv.y, Mathf.Sqrt(rv.x * rv.x + rv.z * rv.z)) * Mathf.Rad2Deg; }

        private static void TorsoAngles(Vector3[] j, float hipsYaw, out float twist, out float bend, out float lean)
        {
            Frame c = ChestFrame(j);
            float y = hipsYaw * Mathf.Deg2Rad;
            var pr = new Vector3(Mathf.Cos(y), 0f, -Mathf.Sin(y));
            var pf = new Vector3(Mathf.Sin(y), 0f, Mathf.Cos(y));
            twist = Mathf.Atan2(Vector3.Dot(c.F, pr), Vector3.Dot(c.F, pf)) * Mathf.Rad2Deg;
            bend = Mathf.Atan2(Vector3.Dot(c.U, pf), Vector3.Dot(c.U, Vector3.up)) * Mathf.Rad2Deg;
            lean = Mathf.Atan2(-Vector3.Dot(c.U, pr), Vector3.Dot(c.U, Vector3.up)) * Mathf.Rad2Deg;
        }

        private static void HeadAngles(Vector3[] j, out float yaw, out float pitch)
        {
            Frame c = ChestFrame(j);
            Vector3 d = j[JHeadFwd] - j[JHead];
            float x = Vector3.Dot(d, c.R), y = Vector3.Dot(d, c.U), z = Vector3.Dot(d, c.F);
            yaw = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
            pitch = Mathf.Atan2(y, Mathf.Sqrt(x * x + z * z)) * Mathf.Rad2Deg;
        }

        private static float ToePitchOf(Vector3[] j, int ankle, int toe)
        {
            Vector3 d = j[toe] - j[ankle];
            return Mathf.Atan2(d.y, Mathf.Sqrt(d.x * d.x + d.z * d.z)) * Mathf.Rad2Deg;
        }

        private static Vector3 Pole(Vector3 a, Vector3 b, Vector3 c, Vector3 fallback)
        {
            Vector3 d = c - a;
            float dd = Vector3.Dot(d, d);
            if (dd < 1e-8f) return fallback;
            float t = Vector3.Dot(b - a, d) / dd;
            return Norm(b - (a + d * t), fallback);
        }
    }
}

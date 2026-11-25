using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>What a unit does this step, for <see cref="HaloUnitAnimator.Step"/> (Halo's frame: x forward, y left, z up; world units, radians).</summary>
    public struct HaloUnitAnimInput
    {
        /// <summary>The unit seat label ("stand", "crouch", "alert", "flee", "asleep", "flaming" or a vehicle seat such as "W-passenger"); null = stand.</summary>
        public string Seat;
        /// <summary>The held weapon's label ("ar", "pr", "pp", "ne" ... the weapon tag's label); null / "" = unarmed.</summary>
        public string WeaponLabel;
        /// <summary>Planar velocity in the body frame (world units a second): x forward, y left.</summary>
        public float MoveX, MoveY;
        /// <summary>Body turning in place (radians a second, + = left) - the turn-left / turn-right animations play.</summary>
        public float TurnRate;
        public bool Airborne;
        /// <summary>The aim (weapon) and the look (head) relative to the body: yaw + left, pitch + up (radians).</summary>
        public float AimYaw, AimPitch, LookYaw, LookPitch;
        public bool Aiming, Looking;
    }

    /// <summary>
    /// A campaign unit's animation graph played the way the game picks animations (Unity-free; the Unity side writes the pose onto the model's
    /// node transforms). Per step: the seat (stand / crouch / alert / flee / a vehicle seat) and the weapon class the held weapon's label belongs
    /// to; the base: idle, move-front / back / left / right blended by the velocity's direction and played at the pace the unit moves (the
    /// animations' own root motion), turn-left / turn-right while turning in place, airborne, all cross-faded; on top the class's aim overlay
    /// (aim-still / aim-move grid toward the aim), the seat's look overlay, the weapon type's fire-1 recoil, and one "action" at a time:
    /// reload, melee, throw-grenade (released at its key frame), dive / evade (root motion), surprise, signal, warn, celebrate, berserk,
    /// land-soft / land-hard, ready / put-away, hit reactions (s-ping / h-ping by direction and body region from the unit damage table);
    /// deaths (s-kill / h-kill by direction and region, then airborne-dead / landing-dead) hold their last frame. Animation permutations
    /// ("%0", "%1" chained by "next animation") are drawn by their weights. No allocation per step.
    /// </summary>
    public sealed class HaloUnitAnimator
    {
        public enum ActionKind { None, Reload, Melee, Throw, Gesture, Dive, Ping, Land, Ready }

        public readonly HaloSkeleton Skel;
        public readonly HaloAnimGraph Graph;
        public readonly HaloPose Pose;
        public readonly int[] Map;
        private readonly Random _rng;

        // ---- selection
        private string _seatLabel, _weaponLabel;
        private bool _selected;
        private HaloAnimGraph.Seat _seat, _standSeat;
        private HaloAnimGraph.WeaponClass _class;
        private HaloAnimGraph.WeaponType _type;

        // ---- base
        private HaloAnimation _base, _base2;      // _base2: the second move direction blended in
        private float _base2W, _phase, _rate = 1f;
        private int _baseId = -99;
        private bool _baseLoop = true;
        private float _tBase;
        // cross-fade from a snapshot of the last pose
        private readonly float[] _fromRot, _fromTrans;
        private float _fade = 1f, _fadeTime = 0.2f;

        // ---- action / overlays
        private HaloAnimation _act;
        private ActionKind _actKind;
        private float _tAct, _actW, _actRate = 1f;
        private bool _actRoot, _actUpper, _actKey1;
        private HaloAnimation _fire, _ping;
        private float _tFire, _tPing, _pingW;
        private float _aimW = 1f;

        // ---- death
        private HaloAnimation _death;
        private float _tDeath;
        private bool _dead, _deadAirborne, _landedDead;

        private readonly float[] _saveR, _saveT;
        private readonly bool[] _upper;

        // ---- workstream 2: strafing, leap, landing length, feigned death
        private bool _strafeCapable, _leapAirborne, _feigning;
        private readonly float[] _dirSpeed = new float[4];      // move-front / back / left / right root speeds (world units / s) of the class
        private float _actLen = -1f;                            // an action cut short (landings), -1 = the clip's length
        /// <summary>Below this fraction of the direction's clip speed the unit shows idle instead of a slow-motion run (ours).</summary>
        public static float IdleBelowFraction = 0.35f;

        /// <summary>Root motion of this step that the animation itself drives (dive, evade, death, landing): body frame (x forward, y left, z up;
        /// world units) and yaw (radians, + left). The Unity side moves the unit by it.</summary>
        public float RootX, RootY, RootZ, RootYaw;
        /// <summary>Events of this step: the action's key frame (throw-grenade: the grenade leaves the hand; melee: the hit) and the feet's key frames.</summary>
        public bool ActionKey, FootDown;
        /// <summary>The move-front animation's own speed (world units a second) for the current seat and class.</summary>
        public float NaturalSpeed { get; private set; }
        /// <summary>The direction asked for has no animation of its own (infection forms only run forward): the body should face the movement.</summary>
        public bool FaceMovement { get; private set; }
        public ActionKind Action => _act != null ? _actKind : ActionKind.None;
        public string ActionName => _act != null ? _act.Name : null;
        public string BaseName => _base != null ? _base.Name : null;
        public string DeathName => _death != null ? _death.Name : null;
        public bool Dying => _dead;
        /// <summary>The death animation has played to its end (and the body is on the ground).</summary>
        public bool DeathDone => _dead && (_death == null || _tDeath >= _death.Duration) && !_deadAirborne;
        public string ClassName => (_seat != null ? _seat.Label : "-") + " " + (_class != null ? _class.Name : "-") + (_type != null ? "/" + _type.Label : "");
        public HaloAnimGraph.WeaponClass Class => _class;
        public HaloAnimGraph.Seat SeatBlock => _seat;

        public HaloUnitAnimator(HaloSkeleton skel, HaloAnimGraph graph, int seed)
        {
            Skel = skel;
            Graph = graph;
            Pose = new HaloPose(skel);
            Map = skel.MapFrom(graph);
            _rng = new Random(seed);
            _fromRot = new float[skel.Count * 4];
            _fromTrans = new float[skel.Count * 3];
            _saveR = new float[skel.Count * 4];
            _saveT = new float[skel.Count * 3];
            _upper = new bool[skel.Count];
            int spine = skel.Node("bip01 spine");
            if (spine < 0) spine = skel.Node("bip01 spine1");
            for (int i = 0; i < skel.Count; i++)
            {
                int p = i;
                int guard = 0;
                while (p >= 0 && p != spine && guard++ < 64) p = skel.Parent[p];
                _upper[i] = spine >= 0 && p == spine;
            }

            _standSeat = graph.FindSeat("stand") ?? (graph.Seats.Count > 0 ? graph.Seats[0] : null);
        }

        // ------------------------------------------------------------------ picking animations

        /// <summary>One of an animation's permutations ("%0", "%1" ... chained by next), drawn by weight.</summary>
        public HaloAnimation Pick(int index)
        {
            HaloAnimation a = Graph.Get(index);
            if (a == null || a.Next < 0) return a;
            float sum = 0f;
            int guard = 0;
            for (HaloAnimation x = a; x != null && guard < 16; x = x.Next >= 0 && x.Next != x.Index ? Graph.Get(x.Next) : null, guard++) sum += x.Weight > 0f ? x.Weight : 1f;
            float r = (float)_rng.NextDouble() * sum;
            guard = 0;
            for (HaloAnimation x = a; x != null && guard < 16; x = x.Next >= 0 && x.Next != x.Index ? Graph.Get(x.Next) : null, guard++)
            {
                r -= x.Weight > 0f ? x.Weight : 1f;
                if (r <= 0f) return x;
            }

            return a;
        }

        /// <summary>A class animation (HaloAnimIds.Weapon), falling back to the stand seat's class of the same name, then its first class.</summary>
        public int ClassAnimIndex(int id)
        {
            int i = _class != null ? _class.Anim(id) : -1;
            if (i >= 0) return i;
            if (_standSeat != null && _standSeat != _seat)
            {
                HaloAnimGraph.WeaponClass c = _class != null ? _standSeat.Weapon(_class.Name) : null;
                if (c != null) { i = c.Anim(id); if (i >= 0) return i; }
            }

            return -1;
        }

        public bool HasClassAnim(int id) { return ClassAnimIndex(id) >= 0; }
        private HaloAnimation ClassAnim(int id) { return Pick(ClassAnimIndex(id)); }
        private HaloAnimation TypeAnim(int id) { return _type != null ? Pick(_type.Anim(id)) : null; }

        private void Select(string seat, string label)
        {
            if (_selected && string.Equals(seat, _seatLabel, StringComparison.Ordinal) && string.Equals(label, _weaponLabel, StringComparison.Ordinal)) return;
            _selected = true;
            _seatLabel = seat;
            _weaponLabel = label;
            _seat = Graph.FindSeat(seat) ?? _standSeat;
            _type = null;
            _class = null;
            if (_seat == null) return;
            if (string.IsNullOrEmpty(label)) _class = _seat.Weapon("unarmed");
            else _class = Graph.ClassFor(_seat, label, out _type);
            if (_class == null || _class.Anim(HaloAnimIds.Weapon.Idle) < 0)
                for (int k = 0; k < _seat.Weapons.Count; k++)
                    if (_seat.Weapons[k].Anim(HaloAnimIds.Weapon.Idle) >= 0 && (_class == null || _class.Anim(HaloAnimIds.Weapon.Idle) < 0)) { _class = _seat.Weapons[k]; break; }
            if (_class == null && _seat.Weapons.Count > 0) _class = _seat.Weapons[0];
            if (_type == null && _class != null && !string.IsNullOrEmpty(label)) _type = _class.Type(label);
            _baseId = -99;      // re-pick the base
            HaloAnimation front = Graph.Get(ClassAnimIndex(HaloAnimIds.Weapon.MoveFront));
            NaturalSpeed = front != null ? RootSpeed(front) : 0f;
            for (int k = 0; k < 4; k++) _dirSpeed[k] = RootSpeed(Graph.Get(ClassAnimIndex(HaloAnimIds.Weapon.MoveFront + k)));
            _strafeCapable = HasClassAnim(HaloAnimIds.Weapon.MoveBack) && HasClassAnim(HaloAnimIds.Weapon.MoveLeft) && HasClassAnim(HaloAnimIds.Weapon.MoveRight);
        }

        // ------------------------------------------------------------------ events

        /// <summary>A shot: the weapon type's fire-1 overlay (restarts).</summary>
        public void Fire()
        {
            HaloAnimation a = TypeAnim(HaloAnimIds.WeaponType.Fire1);
            if (a == null || !a.Decode()) return;
            _fire = a;
            _tFire = 0f;
        }

        public bool Reload() { return StartAction(ActionKind.Reload, TypeAnim(HaloAnimIds.WeaponType.Reload1), false); }

        /// <summary>The melee swing. A Base-type clip (the elite's pistol melee: 40 frames, a 1.48 wu lunge) owns the whole body and moves it by
        /// its own frame info (a root action); Replacement / Overlay clips play over the legs. False when the class has no melee clip (marines).</summary>
        public bool Melee()
        {
            HaloAnimation a = TypeAnim(HaloAnimIds.WeaponType.Melee) ?? ClassAnim(HaloAnimIds.Weapon.Melee);
            return StartAction(ActionKind.Melee, a, a != null && a.Type == HaloAnimation.Kind.Base);
        }

        public bool Throw() { return StartAction(ActionKind.Throw, ClassAnim(HaloAnimIds.Weapon.ThrowGrenade), false); }

        /// <summary>Any class animation as a one-shot (surprise-front 27, berserk 29, signal-attack 33, warn 34, celebrate 40, panic 41, ...).</summary>
        /// <summary>Base-type gestures own the body (a root action: the unit stands, or moves by the clip's own frame info).</summary>
        public bool Gesture(int classAnimId)
        {
            HaloAnimation a = ClassAnim(classAnimId);
            return StartAction(ActionKind.Gesture, a, a != null && a.Type == HaloAnimation.Kind.Base);
        }

        /// <summary>A dive (dir 0 front, 1 back, 2 left, 3 right) or an evade (left / right: dir 2 / 3 with <paramref name="evade"/>): root motion moves the unit.</summary>
        public bool Dive(int dir, bool evade)
        {
            int id = evade ? (dir == 3 ? 31 : 30) : HaloAnimIds.Weapon.DiveFront + Math.Max(0, Math.Min(3, dir));
            HaloAnimation a = ClassAnim(id);
            if (a == null && evade) a = ClassAnim(dir == 3 ? HaloAnimIds.Weapon.DiveRight : HaloAnimIds.Weapon.DiveLeft);
            return StartAction(ActionKind.Dive, a, true);
        }

        /// <summary>Halo's weapon-class animation ids past HaloAnimIds.Weapon: 42 melee-airborne, 44 / 45 resurrect-front / back, 46 melee-continuous
        /// (an attached infection form), 47 feeding, 48 / 49 / 50 leap-start / leap-airborne / leap-melee (checked on the c10 graphs: the combat
        /// forms' classes list 42, 44, 45, 48, 49, 50; the infection form's 46, 47).</summary>
        public const int AnimMeleeAirborne = 42, AnimResurrectFront = 44, AnimResurrectBack = 45, AnimMeleeContinuous = 46, AnimFeeding = 47,
            AnimLeapStart = 48, AnimLeapAirborne = 49, AnimLeapMelee = 50;

        public enum LeapPhase { Start, Airborne, Melee }

        /// <summary>
        /// True while what plays owns the body and holds the unit still (or moves it only by the clip's own frame info): dive / evade, Base-type
        /// hit reactions, landings, Base-type gestures, Base-type melee, a feigned death (and its resurrect). The mover zeroes the wanted velocity.
        /// </summary>
        public bool BlocksMovement
        {
            get
            {
                if (_dead) return true;
                if (_act == null) return false;
                switch (_actKind)
                {
                    case ActionKind.Dive:
                    case ActionKind.Land: return true;
                    case ActionKind.Ping:
                    case ActionKind.Gesture:
                    case ActionKind.Melee: return _actRoot;
                    default: return false;
                }
            }
        }

        /// <summary>
        /// The speed (world units / s) Halo moves an animation-driven biped at in a body-frame direction in Halo's frame, as
        /// <see cref="HaloUnitAnimInput"/> (<paramref name="moveX"/> = forward, <paramref name="moveY"/> = left; only the direction counts): the root speed of the two direction clips <c>Directions()</c> picks,
        /// blended by angle (marine rifle: front 2.10, back 1.50, sides 1.75). A class without strafe clips (carrier, alert seats) has only its
        /// move-front speed (it turns to face its movement). 0 before the first step.
        /// </summary>
        public float MaxSpeedFor(float moveX, float moveY)
        {
            if (_class == null) return NaturalSpeed;
            if (moveX * moveX + moveY * moveY < 1e-8f || !_strafeCapable) return NaturalSpeed;
            float a = (float)Math.Atan2(moveY, moveX);      // Halo: + left of forward
            int id, id2;
            float w2;
            Directions(a, out id, out id2, out w2);
            float s1 = DirSpeed(id), s2 = id2 >= 0 ? DirSpeed(id2) : s1;
            if (s1 <= 0.01f) s1 = NaturalSpeed;
            if (s2 <= 0.01f) s2 = s1;
            return s1 * (1f - w2) + s2 * w2;
        }

        private float DirSpeed(int id)
        {
            int k = id - HaloAnimIds.Weapon.MoveFront;
            return k >= 0 && k < 4 ? _dirSpeed[k] : 0f;
        }

        /// <summary>The class has move-back, move-left and move-right (it strafes while facing a target); else it always faces its movement.</summary>
        public bool StrafeCapable => _strafeCapable;

        /// <summary>
        /// The flood leap: Start plays leap-start (48) as a body action, Airborne makes the airborne base leap-airborne (49) until the unit lands
        /// (<see cref="EndLeap"/>), Melee plays leap-melee (50) as a melee (its key frame strikes like a normal melee). False when the class has no
        /// such clip.
        /// </summary>
        public bool PlayLeap(LeapPhase phase)
        {
            switch (phase)
            {
                case LeapPhase.Start: return StartAction(ActionKind.Gesture, ClassAnim(AnimLeapStart), false);
                case LeapPhase.Airborne:
                    if (!HasClassAnim(AnimLeapAirborne)) return false;
                    _leapAirborne = true;
                    return true;
                default: return StartAction(ActionKind.Melee, ClassAnim(AnimLeapMelee), false);
            }
        }

        /// <summary>The leap is over (landed / contact): the normal airborne base again.</summary>
        public void EndLeap() { _leapAirborne = false; }

        public bool Land(bool hard) { return StartAction(ActionKind.Land, ClassAnim(hard ? HaloAnimIds.Weapon.LandHard : HaloAnimIds.Weapon.LandSoft), false); }

        /// <summary>A landing that holds the biped <paramref name="seconds"/> at most (Halo: the biped's maximum soft / hard landing time scaled
        /// by the landing speed, <see cref="HaloCharacterDef.LandingSeconds"/>); the clip is cut there. 0 or less: no landing animation.</summary>
        public bool Land(bool hard, float seconds)
        {
            if (seconds <= 0.02f) return false;
            if (!Land(hard)) return false;
            _actLen = seconds;
            return true;
        }

        /// <summary>A feigned death (flood combat forms): the death clip by direction and region plays and holds while the unit stays alive; the
        /// unit gets up with <see cref="Resurrect"/>.</summary>
        public void Feign(bool hard, int dir, int region)
        {
            Kill(hard, dir, region);
            _feigning = true;
        }

        /// <summary>Back up from a feigned death: resurrect-front (44) after a death toward the front / sides, resurrect-back (45) after one from
        /// behind (the clips are named like the deaths they undo); the clip's root motion moves the unit. False (just standing up) without one.</summary>
        public bool Resurrect(bool fromBack)
        {
            if (!_feigning) return false;
            _feigning = false;
            Revive();
            HaloAnimation a = ClassAnim(fromBack ? AnimResurrectBack : AnimResurrectFront) ?? ClassAnim(fromBack ? AnimResurrectFront : AnimResurrectBack);
            Snapshot(0.15f);
            return StartAction(ActionKind.Gesture, a, true);
        }

        public bool Feigning => _feigning;

        /// <summary>The root melee's lunge (the elite's 1.48 wu) has reached its target: the rest of the clip plays in place (Halo: the biped
        /// collides with the unit it swings at and stops there; our units do not collide with each other, so the unit asks).</summary>
        public void HoldLunge() { if (_act != null && _actRoot && _actKind == ActionKind.Melee) _actHoldLunge = true; }

        /// <summary>A root melee (lunge) is playing and still moving the body.</summary>
        public bool Lunging => _act != null && _actRoot && _actKind == ActionKind.Melee && !_actHoldLunge;

        private bool _actHoldLunge;

        public bool Ready() { return StartAction(ActionKind.Ready, ClassAnim(HaloAnimIds.Weapon.Ready), false); }

        /// <summary>A hit reaction from the unit damage table (hard: h-ping; dir 0 front, 1 left, 2 right, 3 back; region 0 gut, 1 chest, 2 head, 3 l-arm ...).</summary>
        public bool Ping(bool hard, int dir, int region)
        {
            HaloAnimation a = DamageAnim(false, hard, dir, region);
            if (a == null || !a.Decode()) return false;
            if (a.Type == HaloAnimation.Kind.Overlay) { _ping = a; _tPing = 0f; _pingW = 1f; return true; }
            if (_act != null && (_actKind == ActionKind.Dive || _actKind == ActionKind.Melee || _actKind == ActionKind.Throw) && !hard) return false;
            return StartAction(ActionKind.Ping, a, a.Type == HaloAnimation.Kind.Base);
        }

        private HaloAnimation DamageAnim(bool kill, bool hard, int dir, int region)
        {
            int kind = (kill ? 2 : 0) + (hard ? 1 : 0);
            short[] t = Graph.UnitDamage;
            int i = (kind * 4 + Math.Max(0, Math.Min(3, dir))) * 11 + Math.Max(0, Math.Min(10, region));
            int idx = t != null && i < t.Length ? t[i] : -1;
            if (idx < 0 && region != 0) return DamageAnim(kill, hard, dir, 0);
            if (idx < 0 && hard) return DamageAnim(kill, false, dir, region);
            if (idx < 0 && dir != 0) return DamageAnim(kill, hard, 0, region);
            return Pick(idx);
        }

        private bool StartAction(ActionKind kind, HaloAnimation a, bool root)
        {
            if (_dead || a == null || !a.Decode()) return false;
            if (_act == null) _actW = 0f;
            _act = a;
            _actKind = kind;
            _tAct = 0f;
            _actRate = 1f;
            _actRoot = root || (a.Type == HaloAnimation.Kind.Base && kind == ActionKind.Land);
            _actUpper = !_actRoot && a.Type == HaloAnimation.Kind.Base && (kind == ActionKind.Throw || kind == ActionKind.Reload || kind == ActionKind.Melee || kind == ActionKind.Ready);
            _actKey1 = false; _actHoldLunge = false;
            _actLen = -1f;
            if (_actRoot) { Snapshot(0.12f); _actW = 1f; }
            return true;
        }

        /// <summary>Stops the running action (a death or a stance change cancels it).</summary>
        public void StopAction() { _act = null; _actKind = ActionKind.None; _actW = 0f; }

        /// <summary>The death: s-kill / h-kill by direction and region from the unit damage table (none: the current pose falls still).</summary>
        public void Kill(bool hard, int dir, int region)
        {
            if (_dead) { _feigning = false; return; }      // (a feigned death becomes the real one where it lies)
            _dead = true;
            HaloAnimation a = DamageAnim(true, hard, dir, region);
            if (a != null && !a.Decode()) a = null;
            Snapshot(0.1f);
            _act = null; _fire = null; _ping = null;
            _death = a ?? Pick(_seat != null ? _seat.Anim(HaloAnimIds.Seat.LandingDead) : -1);
            if (_death != null && !_death.Decode()) _death = null;
            _tDeath = 0f;
            _deadAirborne = false;
            _landedDead = false;
            if (_death == null && _base != null && _base.Decode()) _death = _base;      // (no death animation at all: what plays runs out and holds)
        }

        /// <summary>Alive again (a revived / re-used unit): the next step picks a fresh base.</summary>
        public void Revive() { _dead = false; _death = null; _deadAirborne = false; _landedDead = false; _baseId = -99; }

        // ------------------------------------------------------------------ custom animations (scripts: custom_animation; cinematics)

        private HaloAnimation _custom;
        private HaloAnimGraph _customGraph;
        private int[] _customMap;
        private float _tCustom;
        private bool _customLoop, _customHold;
        private readonly System.Collections.Generic.Dictionary<HaloAnimGraph, int[]> _maps = new System.Collections.Generic.Dictionary<HaloAnimGraph, int[]>();

        public bool CustomPlaying => _custom != null;
        /// <summary>A held (non-looping) custom animation reached its last frame.</summary>
        public bool CustomDone => _custom != null && !_customLoop && _tCustom >= _custom.Duration;
        public string CustomName => _custom != null ? _custom.Name : null;
        public float CustomTime => _tCustom;

        /// <summary>
        /// Plays an animation of any graph on this unit over everything else (scripts' custom_animation / unit_custom_animation_at_frame,
        /// cinematics): its nodes by name onto this skeleton, its root motion moving the unit; at its end the unit's own animations come back
        /// (<paramref name="hold"/>: it stays on the last frame until <see cref="StopCustom"/>). False when it cannot be played (missing,
        /// compressed and not decodable).
        /// </summary>
        public bool PlayCustom(HaloAnimGraph graph, HaloAnimation a, bool interpolate, bool loop, bool hold, float startTime = 0f)
        {
            if (a == null || graph == null || !a.Decode()) return false;
            int[] map;
            if (graph == Graph) map = Map;
            else if (!_maps.TryGetValue(graph, out map)) { map = Skel.MapFrom(graph); _maps[graph] = map; }
            if (interpolate) Snapshot(0.25f);
            _custom = a;
            _customGraph = graph;
            _customMap = map;
            _tCustom = Math.Max(0f, startTime);
            _customLoop = loop;
            _customHold = hold;
            _act = null; _actKind = ActionKind.None;
            return true;
        }

        public void StopCustom(bool interpolate)
        {
            if (_custom == null) return;
            if (interpolate) Snapshot(0.25f);
            _custom = null;
            _baseId = -99;
        }

        private bool StepCustom(float dt)
        {
            HaloAnimation a = _custom;
            float t0 = _tCustom;
            _tCustom += dt;
            float len = a.Duration;
            if (!_customLoop && _tCustom >= len)
            {
                if (a.Info != null) RootDelta(a, t0, len);
                if (!_customHold) { Snapshot(0.2f); _custom = null; _baseId = -99; return false; }
                _tCustom = len;
            }
            else if (a.Info != null)
            {
                if (_customLoop && _tCustom >= len && len > 0f) { RootDelta(a, t0, len); _tCustom -= len * (float)Math.Floor(_tCustom / len); RootDelta(a, 0f, _tCustom); }
                else RootDelta(a, t0, _tCustom);
            }

            Pose.Reset();
            int f0, f1;
            float u;
            HaloPose.FrameAt(a, _tCustom, _customLoop, out f0, out f1, out u);
            if (a.Type == HaloAnimation.Kind.Overlay) Pose.Overlay(a, _customMap, f0, f1, u, 1f);
            else Pose.ApplyFrames(a, _customMap, f0, f1, u, 1f);
            ApplyEmotion();
            BlendFromSnapshot();
            return true;
        }

        // ------------------------------------------------------------------ the step

        public void Step(in HaloUnitAnimInput i, float dt)
        {
            RootX = RootY = RootZ = RootYaw = 0f;
            ActionKey = false;
            FootDown = false;
            Select(string.IsNullOrEmpty(i.Seat) ? "stand" : i.Seat, string.IsNullOrEmpty(i.WeaponLabel) ? null : i.WeaponLabel);
            if (_fade < 1f) _fade = Math.Min(1f, _fade + dt / Math.Max(0.01f, _fadeTime));
            if (_dead) { StepDeath(i, dt); return; }
            if (_custom != null && StepCustom(dt)) return;

            // ---- the base
            float speed = (float)Math.Sqrt(i.MoveX * i.MoveX + i.MoveY * i.MoveY);
            bool moving = speed > 0.02f && !i.Airborne;
            int id;
            int id2 = -1;
            float w2 = 0f;
            // a class without strafe clips (carrier, alert seats) always faces its movement (no flipping between target and path)
            FaceMovement = moving && !_strafeCapable;
            if (!i.Airborne) _leapAirborne = false;
            if (moving)
            {
                // a slow drift (separation, easing into a goal) shows idle instead of the run cycle in slow motion (ours: 35%)
                float own = _strafeCapable ? MaxSpeedFor(i.MoveX, i.MoveY) : NaturalSpeed;
                if (own > 0.02f && speed < own * IdleBelowFraction) moving = false;
            }

            if (i.Airborne && _leapAirborne && HasClassAnim(AnimLeapAirborne)) id = AnimLeapAirborne;
            else if (i.Airborne && HasClassAnim(HaloAnimIds.Weapon.Airborne)) id = HaloAnimIds.Weapon.Airborne;
            else if (moving)
            {
                float a = (float)Math.Atan2(i.MoveY, i.MoveX);      // + left
                if (_strafeCapable) Directions(a, out id, out id2, out w2);
                else id = HaloAnimIds.Weapon.MoveFront;
                if (!HasClassAnim(id)) { id = HaloAnimIds.Weapon.MoveFront; id2 = -1; FaceMovement = true; }
                if (id2 >= 0 && !HasClassAnim(id2)) { id2 = -1; w2 = 0f; }
                if (!HasClassAnim(id)) id = HaloAnimIds.Weapon.Idle;
            }
            else if (Math.Abs(i.TurnRate) > 0.2f && HasClassAnim(i.TurnRate > 0f ? HaloAnimIds.Weapon.TurnLeft : HaloAnimIds.Weapon.TurnRight))
                id = i.TurnRate > 0f ? HaloAnimIds.Weapon.TurnLeft : HaloAnimIds.Weapon.TurnRight;
            else id = HaloAnimIds.Weapon.Idle;

            if (id != _baseId)
            {
                HaloAnimation b = ClassAnim(id);
                if (b == null && id != HaloAnimIds.Weapon.Idle) b = ClassAnim(HaloAnimIds.Weapon.Idle);
                if (b != null && b.Decode())
                {
                    bool keepPhase = _base != null && IsMove(_baseId) && IsMove(id);
                    if (_base != null) Snapshot(0.2f);
                    _base = b;
                    _baseId = id;
                    _baseLoop = id != HaloAnimIds.Weapon.LandHard && id != HaloAnimIds.Weapon.LandSoft;
                    if (!keepPhase) _phase = 0f;
                    _tBase = _phase * b.Duration;
                }
            }

            _base2 = id2 >= 0 && w2 > 0.01f ? Graph.Get(ClassAnimIndex(id2)) : null;
            if (_base2 != null && !_base2.Decode()) _base2 = null;
            _base2W = _base2 != null ? w2 : 0f;

            // pace: the feet keep the speed the unit really moves (the animations' own root motion), turns the turn speed
            _rate = 1f;
            if (moving && _base != null)
            {
                float own = RootSpeed(_base) * (1f - _base2W) + (_base2 != null ? RootSpeed(_base2) * _base2W : 0f);
                _rate = own > 0.02f ? Clamp(speed / own, 0.4f, 2f) : 1f;
            }
            else if ((_baseId == HaloAnimIds.Weapon.TurnLeft || _baseId == HaloAnimIds.Weapon.TurnRight) && _base != null)
            {
                float own = RootYawSpeed(_base);
                _rate = own > 0.05f ? Clamp(Math.Abs(i.TurnRate) / own, 0.5f, 2f) : 1f;
            }

            if (_base != null)
            {
                float d = Math.Max(HaloAnimation.FrameTime, _base.Duration);
                float t0 = _tBase;
                _phase += dt * _rate / d;
                bool wrapped = _phase >= 1f;
                _phase -= (float)Math.Floor(_phase);
                _tBase = _phase * _base.Duration;
                if (moving) FootDown = Crossed(_base, t0, _tBase);
                // Halo picks the state's animation again each cycle: idles / waddles with permutations ("%0", "%1" ...) vary
                if (wrapped && _baseLoop) RepickBase();
            }

            // ---- the action
            if (_act != null)
            {
                float t0 = _tAct;
                _tAct += dt * _actRate;
                float len = _actLen > 0f ? Math.Min(_actLen, _act.Duration) : _act.Duration;
                int f = (int)(_tAct / HaloAnimation.FrameTime);
                if (!_actKey1 && _act.KeyFrame > 0 && f >= _act.KeyFrame) { _actKey1 = true; ActionKey = true; }
                if (!_actKey1 && _act.KeyFrame <= 0 && _tAct >= len * 0.5f) { _actKey1 = true; ActionKey = true; }
                if (_actRoot && !_actHoldLunge) RootDelta(_act, t0, Math.Min(_tAct, len));
                bool ending = _tAct >= len - 0.1f;
                float fadeIn = _actRoot ? 0.01f : 0.1f;
                _actW = MoveTowards(_actW, ending && !_actRoot ? 0f : 1f, dt / (ending ? 0.1f : fadeIn));
                if (_tAct >= len && (_actRoot || _actW <= 0f)) { if (_actRoot) Snapshot(0.15f); _act = null; _actKind = ActionKind.None; _actW = 0f; }
            }

            if (_fire != null) { _tFire += dt; if (_tFire >= _fire.Duration) _fire = null; }
            if (_ping != null) { _tPing += dt; if (_tPing >= _ping.Duration) _ping = null; }
            float aimT = _act == null || _actKind == ActionKind.Ping ? 1f : _actRoot ? 0f : 1f - _actW;
            _aimW = MoveTowards(_aimW, aimT, dt * 5f);

            // ---- the pose
            Pose.Reset();
            if (_act != null && _actRoot) ApplyAt(_act, Math.Min(_tAct, _act.Duration), false, 1f);
            else
            {
                if (_base != null) ApplyAt(_base, _tBase, _baseLoop, 1f);
                if (_base2 != null) ApplyAt(_base2, _phase * _base2.Duration, true, _base2W);
                if (_act != null && _actW > 0.001f) ApplyAction(_act, Math.Min(_tAct, _act.Duration), _actW);
            }

            if (i.Aiming && _aimW > 0.01f && _class != null)
            {
                HaloAnimation aim = Graph.Get(ClassAnimIndex(moving ? HaloAnimIds.Weapon.AimMove : HaloAnimIds.Weapon.AimStill));
                if (aim == null && moving) aim = Graph.Get(ClassAnimIndex(HaloAnimIds.Weapon.AimStill));
                if (aim != null && aim.Type == HaloAnimation.Kind.Overlay)
                    Pose.Aim(aim, Map, i.AimYaw, i.AimPitch, _class.RightYawPerFrame, _class.LeftYawPerFrame, _class.RightFrames, _class.LeftFrames,
                             _class.DownPitchPerFrame, _class.UpPitchPerFrame, _class.DownFrames, _class.UpFrames, _aimW);
            }

            if (i.Looking && _seat != null)
            {
                HaloAnimation look = Graph.Get(_seat.Anim(HaloAnimIds.Seat.Look));
                if (look != null && look.Type == HaloAnimation.Kind.Overlay)
                    Pose.Aim(look, Map, i.LookYaw, i.LookPitch, _seat.RightYawPerFrame, _seat.LeftYawPerFrame, _seat.RightFrames, _seat.LeftFrames,
                             _seat.DownPitchPerFrame, _seat.UpPitchPerFrame, _seat.DownFrames, _seat.UpFrames, 1f);
            }

            ApplyEmotion();
            if (_fire != null) OverlayAt(_fire, _tFire, 1f);
            if (_ping != null) OverlayAt(_ping, _tPing, _pingW);
            BlendFromSnapshot();
        }

        private void StepDeath(in HaloUnitAnimInput i, float dt)
        {
            HaloAnimation a = _death;
            float t0 = _tDeath;
            if (a == null) { BlendFromSnapshot(); return; }
            if (_deadAirborne)
            {
                // falling dead: the seat's airborne-dead (loops), then landing-dead once down
                _tDeath += dt;
                if (!i.Airborne)
                {
                    HaloAnimation land = Pick(_seat != null ? _seat.Anim(HaloAnimIds.Seat.LandingDead) : -1);
                    _deadAirborne = false;
                    _landedDead = true;
                    if (land != null && land.Decode()) { Snapshot(0.1f); _death = land; _tDeath = 0f; }
                    else _tDeath = _death.Duration;
                }
            }
            else
            {
                _tDeath = Math.Min(_tDeath + dt, a.Duration);
                if (a.Info != null) RootDelta(a, t0, _tDeath);
                if (i.Airborne && !_landedDead && _tDeath >= a.Duration)
                {
                    HaloAnimation air = Pick(_seat != null ? _seat.Anim(HaloAnimIds.Seat.AirborneDead) : -1);
                    if (air != null && air.Decode()) { Snapshot(0.15f); _death = air; _tDeath = 0f; _deadAirborne = true; }
                }
            }

            Pose.Reset();
            if (_death != null) ApplyAt(_death, _tDeath, _deadAirborne, 1f);
            BlendFromSnapshot();
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Scripts' unit_set_emotion: a frame of the seat's "emotions" overlay (one frame per emotion; the captain, crew), -1 = none.</summary>
        public int Emotion = -1;

        private void ApplyEmotion()
        {
            if (Emotion < 0 || _seat == null) return;
            HaloAnimation e = Graph.Get(_seat.Anim(HaloAnimIds.Seat.Emotions));
            if (e == null || e.Type != HaloAnimation.Kind.Overlay || !e.Decode()) return;
            int f = Math.Max(0, Math.Min(e.Frames - 1, Emotion));
            Pose.Overlay(e, Map, f, f, 0f, 1f);
        }

        private static bool IsMove(int id) { return id >= HaloAnimIds.Weapon.MoveFront && id <= HaloAnimIds.Weapon.MoveRight; }

        /// <summary>The base looped: a fresh weighted draw among its permutations (only when it has some); a different clip cross-fades in.</summary>
        private void RepickBase()
        {
            HaloAnimation first = Graph.Get(ClassAnimIndex(_baseId));
            if (first == null || first.Next < 0) return;
            HaloAnimation b = Pick(first.Index);
            if (b == null || b == _base || !b.Decode()) return;
            Snapshot(0.15f);
            _base = b;
            _tBase = _phase * b.Duration;
        }

        /// <summary>The two move directions around an angle (+ left) and the second's weight: front 0, left +90, back 180, right -90.</summary>
        private static void Directions(float a, out int id, out int id2, out float w2)
        {
            const float Q = (float)(Math.PI / 2);
            float x = a / Q;                               // -2..2 : front 0, left 1, back +-2, right -1
            if (x < 0f) x += 4f;                           // 0..4 : front 0, left 1, back 2, right 3
            int k = (int)Math.Floor(x) & 3;
            float u = x - (float)Math.Floor(x);
            int[] order = Order;
            id = order[k]; id2 = order[(k + 1) & 3]; w2 = u;
            if (w2 > 0.5f) { int t = id; id = id2; id2 = t; w2 = 1f - w2; }
        }

        private static readonly int[] Order = { HaloAnimIds.Weapon.MoveFront, HaloAnimIds.Weapon.MoveLeft, HaloAnimIds.Weapon.MoveBack, HaloAnimIds.Weapon.MoveRight };

        private void ApplyAt(HaloAnimation a, float t, bool loop, float w)
        {
            int f0, f1;
            float u;
            HaloPose.FrameAt(a, t, loop, out f0, out f1, out u);
            if (a.Type == HaloAnimation.Kind.Overlay) Pose.Overlay(a, Map, f0, f1, u, w);
            else Pose.ApplyFrames(a, Map, f0, f1, u, w);
        }

        private void OverlayAt(HaloAnimation a, float t, float w)
        {
            int f0, f1;
            float u;
            HaloPose.FrameAt(a, t, false, out f0, out f1, out u);
            if (a.Type == HaloAnimation.Kind.Overlay) Pose.Overlay(a, Map, f0, f1, u, w);
            else Pose.ApplyFrames(a, Map, f0, f1, u, w);
        }

        /// <summary>An action on the base: overlays turn on top, replacements replace their nodes, a base-type one only the upper body (throw, reload).</summary>
        private void ApplyAction(HaloAnimation a, float t, float w)
        {
            int f0, f1;
            float u;
            HaloPose.FrameAt(a, t, false, out f0, out f1, out u);
            if (a.Type == HaloAnimation.Kind.Overlay) { Pose.Overlay(a, Map, f0, f1, u, w); return; }
            if (a.Type == HaloAnimation.Kind.Replacement || !_actUpper) { Pose.ApplyFrames(a, Map, f0, f1, u, w); return; }
            int n = Skel.Count;
            Array.Copy(Pose.LRot, _saveR, n * 4);
            Array.Copy(Pose.LTrans, _saveT, n * 3);
            Pose.ApplyFrames(a, Map, f0, f1, u, w);
            for (int k = 0; k < n; k++)
            {
                if (_upper[k]) continue;
                Array.Copy(_saveR, k * 4, Pose.LRot, k * 4, 4);
                Array.Copy(_saveT, k * 3, Pose.LTrans, k * 3, 3);
            }
        }

        private void Snapshot(float seconds)
        {
            Array.Copy(Pose.LRot, _fromRot, _fromRot.Length);
            Array.Copy(Pose.LTrans, _fromTrans, _fromTrans.Length);
            _fade = 0f;
            _fadeTime = seconds;
        }

        private void BlendFromSnapshot()
        {
            if (_fade >= 1f) return;
            float w = 1f - Smooth(_fade);
            int n = Skel.Count;
            for (int k = 0; k < n; k++)
            {
                HQ.Nlerp(Pose.LRot, k * 4, _fromRot, k * 4, w, Pose.LRot, k * 4);
                for (int c = 0; c < 3; c++) Pose.LTrans[k * 3 + c] += (_fromTrans[k * 3 + c] - Pose.LTrans[k * 3 + c]) * w;
            }
        }

        /// <summary>Adds an animation's root motion between two times (frame info dx dy dz dyaw per frame, interpolated within frames).</summary>
        private void RootDelta(HaloAnimation a, float t0, float t1)
        {
            if (a.Info == null || t1 <= t0) return;
            float x0, y0, z0, w0, x1, y1, z1, w1;
            RootAt(a, t0, out x0, out y0, out z0, out w0);
            RootAt(a, t1, out x1, out y1, out z1, out w1);
            RootX += x1 - x0; RootY += y1 - y0; RootZ += z1 - z0; RootYaw += w1 - w0;
        }

        private static void RootAt(HaloAnimation a, float t, out float x, out float y, out float z, out float yaw)
        {
            x = y = z = yaw = 0f;
            float fx = t / HaloAnimation.FrameTime;
            int n = a.Frames;
            int whole = Math.Min(n, (int)fx);
            float[] info = a.Info;
            for (int f = 0; f < whole; f++) { x += info[f * 4]; y += info[f * 4 + 1]; z += info[f * 4 + 2]; yaw += info[f * 4 + 3]; }
            if (whole < n)
            {
                float u = fx - whole;
                x += info[whole * 4] * u; y += info[whole * 4 + 1] * u; z += info[whole * 4 + 2] * u; yaw += info[whole * 4 + 3] * u;
            }
        }

        /// <summary>An animation's own speed: its root motion (world units) over its length, per second.</summary>
        public static float RootSpeed(HaloAnimation a)
        {
            if (a == null || !a.Decode() || a.Info == null || a.Frames <= 0) return 0f;
            float sx = 0f, sy = 0f;
            for (int f = 0; f < a.Frames; f++) { sx += a.Info[f * 4]; sy += a.Info[f * 4 + 1]; }
            return (float)Math.Sqrt(sx * sx + sy * sy) / Math.Max(1e-3f, a.Duration);
        }

        public static float RootYawSpeed(HaloAnimation a)
        {
            if (a == null || !a.Decode() || a.Info == null || a.Frames <= 0) return 0f;
            float s = 0f;
            for (int f = 0; f < a.Frames; f++) s += a.Info[f * 4 + 3];
            return Math.Abs(s) / Math.Max(1e-3f, a.Duration);
        }

        /// <summary>A key frame (the feet: key frame and second key frame) passed between two looping times.</summary>
        private static bool Crossed(HaloAnimation a, float t0, float t1)
        {
            float f0 = t0 / HaloAnimation.FrameTime, f1 = t1 / HaloAnimation.FrameTime;
            return Between(a.KeyFrame, f0, f1, a.Frames) || Between(a.SecondKeyFrame, f0, f1, a.Frames);
        }

        private static bool Between(int k, float f0, float f1, int n)
        {
            if (k < 0) return false;
            if (f1 >= f0) return k > f0 && k <= f1;
            return k > f0 || k <= f1;      // wrapped
        }

        private static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
        private static float MoveTowards(float v, float t, float step) { return v < t ? Math.Min(t, v + step) : Math.Max(t, v - step); }
        private static float Smooth(float u) { return u * u * (3f - 2f * u); }
    }
}

using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Characters module, C] One campaign biped (marine, crewman, Keyes, grunt, elite, jackal, hunter, flood forms, sentinels, the monitor):
    /// its model (gbxmodel with its region permutations and change colour), its animation graph played by what it does (HaloUnitAnimator:
    /// idle / move blended by velocity / turn / aim / look / fire / reload / melee / throw / dive / pings / deaths ...), its weapon in hand
    /// (fires through Combat's projectiles while <see cref="Firing"/>), its body / shield vitality from its collision model and the actor
    /// variant, hit boxes on its nodes (HaloLayers.Hitbox) so shots hit it, death with the game's death animations, the dropped weapon and a
    /// body that stays a while. The AI module drives it through the control members (written every tick, read every frame); scripts reach it
    /// through HaloObjects / HaloAi. Split over HaloUnit.*.cs (Body, Move, Weapon, Damage).
    /// </summary>
    public partial class HaloUnit : MonoBehaviour, IHaloDamageable
    {
        /// <summary>The unit tag (bipd) and the actor variant (actv) it was spawned from (null for a scripted unit without AI).</summary>
        public HaloTag UnitTag, ActorVariant;
        public HaloTeam Team = HaloTeam.None;
        public string ObjectName = "";
        /// <summary>Vitality as Halo keeps it: fractions of the maximum (<see cref="MaxBody"/> / <see cref="MaxShield"/> points). Shield above 1 = overshield.</summary>
        public float Body = 1f, Shield = 0f;
        public bool Alive = true;

        // ---- control (the AI module writes these every frame; Characters plays them)
        /// <summary>Wanted movement in world space (metres / second; horizontal for walkers, 3D for flying units). The body faces
        /// <see cref="LookPoint"/> / <see cref="AimPoint"/> when set and strafes, else it faces where it goes.</summary>
        public Vector3 MoveVelocity;
        /// <summary>Where the head looks and where the weapon aims (world points; Vector3.zero = not set).</summary>
        public Vector3 LookPoint, AimPoint;
        /// <summary>Firing: the weapon fires at its rate (the actor variant's rate of fire) toward AimPoint. Crouching: the crouch seat.
        /// Melee / ThrowGrenade / Jump: one-shot requests (the unit clears them when it starts the action).</summary>
        public bool Firing, Crouching, Melee, ThrowGrenade, Jump;

        /// <summary>The seat the animation graph plays when not crouching (null = "stand"; AI may set "alert", "flee", "asleep", "flaming").</summary>
        public string Stance;
        /// <summary>The AI holds this unit still (scripts: ai_braindead / a cutscene): no movement, the animation keeps playing.</summary>
        public bool Frozen;

        public HaloCharacterDef Def { get; private set; }
        public float MaxBody => _maxBodyOverride >= 0f ? _maxBodyOverride : Def != null ? Def.MaxBody : 0f;
        public float MaxShield => _maxShieldOverride >= 0f ? _maxShieldOverride : Def != null ? Def.MaxShield : 0f;
        /// <summary>Seconds since the unit died (0 alive).</summary>
        public float DeadFor { get; private set; }
        /// <summary>The last hit that hurt it (for the AI: who / where from).</summary>
        public HaloHit LastHit;
        public float LastHitTime = -100f;

        private bool _ready;
        private System.Random _rng;
        private static int _seed = 17;

        /// <summary>The unit's eye point (for sight lines and the motion tracker).</summary>
        public virtual Vector3 Eye
        {
            get
            {
                if (_headNode != null) return _headNode.position;
                float h = Def != null ? (Crouching ? Def.CrouchingCamera : Def.StandingCamera) * HaloSpace.Unit : 1.6f;
                return transform.position + Vector3.up * (h > 0.05f ? h : 1.6f);
            }
        }

        /// <summary>The body centre (aim point for enemies shooting at it).</summary>
        public Vector3 Center
        {
            get
            {
                if (_pelvisNode != null) return _pelvisNode.position;
                return transform.position + Vector3.up * (Def != null && !Def.Flying ? Mathf.Max(0.3f, Def.StandingHeight * HaloSpace.Unit * 0.5f) : 0f);
            }
        }

        /// <summary>Builds everything from a character definition (HaloBipeds.Spawn calls it).</summary>
        public void Init(HaloCharacterDef def, HaloTeam team)
        {
            Def = def;
            UnitTag = def.Unit;
            ActorVariant = def.Variant;
            Team = team != HaloTeam.None ? team : (HaloTeam)Mathf.Clamp(def.Team, 0, 5);
            _rng = new System.Random(_seed += 7919);
            Body = 1f;
            Shield = def.MaxShield > 0f ? 1f : 0f;
            Alive = true;
            _yaw = transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            BuildBody();
            InitWeapon();
            InitMove();
            _ready = true;
        }

        /// <summary>Names the unit for scripts (HaloObjects) - K / AI call it for named scenario bipeds.</summary>
        public void SetName(string name)
        {
            if (!string.IsNullOrEmpty(ObjectName)) HaloObjects.Unregister(ObjectName);
            ObjectName = name ?? "";
            if (!string.IsNullOrEmpty(ObjectName)) HaloObjects.Register(ObjectName, gameObject);
        }

        private void Update()
        {
            if (!_ready) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (dt > 0.1f) dt = 0.1f;
            _frames++;
            try
            {
                // suspended (dormant actor, another BSP's unit, a BSP switch being built): no movement, no gravity
                bool held = _suspended || _bspHidden || HaloCampaign.SwitchingBsp;
                if (Puppet) { TickPuppet(dt); if (!Alive) DeadFor += dt; }
                else if (Alive)
                {
                    TickShield(dt);
                    if (!held) { TickMove(dt); CheckFloor(); }
                    if (!_bspHidden) TickWeapon(dt);
                }
                else
                {
                    DeadFor += dt;
                    if (!held) { TickDeadMove(dt); CheckFloor(); }
                }

                if (!_bspHidden) TickAnimation(dt);
                if (!held && !Puppet) CheckOutside();
            }
            catch (System.Exception e)
            {
                if (!_tickFailed) { _tickFailed = true; HaloLog.Error("Characters: " + name + " tick failed: " + e); }
            }
        }

        private bool _tickFailed;
        private int _frames;

        private void OnEnable() { HaloCampaign.BspSwitched += OnBspSwitched; }
        private void OnDisable() { HaloCampaign.BspSwitched -= OnBspSwitched; }

        // ------------------------------------------------------------------ BSP switches (Halo simulates and draws only the active BSP)

        /// <summary>Hidden and suspended: a unit (not an actor - the AI suspends those) left in a BSP that is not active.</summary>
        private bool _bspHidden;
        private System.Collections.Generic.List<Renderer> _hiddenRenderers;
        private static int _bspHiddenLogged;

        /// <summary>
        /// After a BSP switch: every unit no actor drives (scripted bipeds, bodies, actors' leftovers) whose feet are not in the new active
        /// BSP (outside it, or no walkable ground under it there) is suspended and hidden, as Halo neither updates nor draws objects outside
        /// the active BSP; one hidden earlier whose place is in the active BSP again comes back (and is settled on its floor).
        /// </summary>
        private void OnBspSwitched(int from, int to)
        {
            if (!_ready || Puppet || Seat != null || Def == null) return;
            try
            {
                if (HaloAi.IsActor(this)) { if (_bspHidden) SetBspHidden(false); return; }
                Vector3 feet = transform.position;
                bool inBsp = !HaloWorld.Outside(feet + Vector3.up * 0.1f) && !HaloWorld.Outside(feet + Vector3.up * Mathf.Max(0.3f, _height * 0.6f));
                if (inBsp && !Def.Flying)
                {
                    float y;
                    inBsp = FindGroundNear(feet, _stepUp + 0.1f, 1f, out y);
                }

                if (!inBsp && !_bspHidden)
                {
                    SetBspHidden(true);
                    if (_bspHiddenLogged++ < 10) HaloLog.Info("Characters: " + Def.Name + (Alive ? "" : " (body)") + " is not in BSP " + to + " (at " + feet.ToString("F1") + "): suspended and hidden");
                }
                else if (inBsp && _bspHidden) SetBspHidden(false);
            }
            catch (System.Exception e) { HaloLog.Warn("Characters: BSP switch for " + name + ": " + e.Message); }
        }

        private void SetBspHidden(bool hide)
        {
            if (hide == _bspHidden) return;
            _bspHidden = hide;
            if (hide)
            {
                _velocity = Vector3.zero;
                _fallSpeed = 0f;
                _platform = null;
                if (_hiddenRenderers == null) _hiddenRenderers = new System.Collections.Generic.List<Renderer>(8);
                _hiddenRenderers.Clear();
                Renderer[] rs = GetComponentsInChildren<Renderer>(false);
                for (int i = 0; i < rs.Length; i++) if (rs[i] != null && rs[i].enabled) { rs[i].enabled = false; _hiddenRenderers.Add(rs[i]); }
                return;
            }

            if (_hiddenRenderers != null)
            {
                for (int i = 0; i < _hiddenRenderers.Count; i++) if (_hiddenRenderers[i] != null) _hiddenRenderers[i].enabled = true;
                _hiddenRenderers.Clear();
            }

            if (!_suspended) Settle();
        }

        private void OnDestroy()
        {
            HaloBipeds.Forget(this);
            if (!string.IsNullOrEmpty(ObjectName) && HaloObjects.Find(ObjectName) == gameObject) HaloObjects.Unregister(ObjectName);
            ReleaseWeapon();
        }

        private float Rand(float a, float b) { return a + (float)_rng.NextDouble() * (b - a); }
    }
}

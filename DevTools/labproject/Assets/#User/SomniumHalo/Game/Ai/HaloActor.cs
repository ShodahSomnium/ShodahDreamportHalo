using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] One AI actor: the brain driving one HaloUnit (Characters) through its control members, from its actor variant (actv)
    /// and actor (actr) data: perception (vision cones, sight lines, perception times, hearing shots, feeling hits), the states Halo plays
    /// (sleeping / guarding / patrolling move positions / searching / combat / fleeing and cowering / hiding with low shields / berserk /
    /// following the players), firing positions from its squad's groups, bursts, grenades, melee, crouching, evading, panicking when a leader
    /// or friends die, and the scripted command lists. Ticked by HaloAi at 30 Hz; every frame the unit moves itself from MoveVelocity.
    /// Split: HaloActor.cs (core, perception, movement), HaloActor.Combat.cs (combat behaviours), HaloActor.Commands.cs (command lists).
    /// </summary>
    public sealed partial class HaloActor
    {
        public enum Mode { Idle, Patrol, Search, Combat, Flee, Cower, Hide, Berserk, Dead }

        public readonly int Id;
        public HaloUnit Unit;
        public HaloAiBody Body;
        public HaloActorVariantDef V;
        public HaloActorDef A;
        public HaloAiEncounter Enc;
        public int Squad = -1, Platoon = -1;
        public HaloAiState InitialState, ReturnState;
        public Mode State = Mode.Idle;
        public bool Braindead, Blind, Deaf, AllowCharge = true, Berserking, ForceActive;
        /// <summary>Scripts' object_set_melee_attack_inhibited / object_set_ranged_attack_inhibited.</summary>
        public bool MeleeInhibited, RangedInhibited;
        public bool Free;                         // ai_free / ai_attach_free: no encounter
        public Vector3 Home; public float HomeYaw;
        public HaloAiScenario.StartingLocation Start;

        // target
        public HaloAiBody Target;
        public Vector3 TargetLastSeen;
        public float TargetSeenAt = -100f, TargetSince = -100f;
        public bool TargetVisible;
        /// <summary>Script preferences: ai_try_to_fight (an ai name), ai_try_to_fight_player.</summary>
        public string FightAi; public bool FightPlayer;
        public HaloAiBody MagicTarget;

        /// <summary>What the actor knows of its target: Visible = seen within 0.35 s, Known = seen within max(4 s, the actr hide-target-not-visible
        /// time), Lost = beyond that while a target is held.</summary>
        public enum Knowledge { None, Visible, Known, Lost }
        public Knowledge Know
        {
            get
            {
                if (Target == null) return Knowledge.None;
                float since = Now - TargetSeenAt;
                if (since <= 0.35f) return Knowledge.Visible;
                float known = A != null ? Mathf.Max(4f, A.HideTargetNotVisibleTime) : 4f;     // 4 s floor: ours
                return since <= known ? Knowledge.Known : Knowledge.Lost;
            }
        }
        /// <summary>One-shot (consumed by Combat): a target was acquired by surprise (not panicking); when it happened.</summary>
        public bool Surprised;
        public float LastSurpriseAt = -100f;
        /// <summary>How long the last sight acquisition took from the first sight (s; labs and logs).</summary>
        public float LastAcquireDelay = -1f;
        int _scanCursor;
        float _lastTargetAt = -100f;                 // the last time a target was held (surprise needs none for 10 s)
        HaloAiBody _lastAttacker; float _lastAttackerAt = -100f, _targetHurtAt = -100f;
        // scripted look
        public GameObject LookAtObject;

        // movement
        Vector3 _goal; bool _hasGoal, _arrived; float _speedFrac = 1f;
        readonly List<Vector3> _path = new List<Vector3>(16);
        int _pathIdx; float _repathAt, _lastRepath = -100f; bool _pathExact; Vector3 _stuckRef; float _stuckCheckAt; int _stuckCount;
        float _pathFailedAt = -100f;
        bool _wasSeated, _wasFeigning;
        float _acceptRadius = 0.45f;
        Vector3 _override; float _overrideUntil = -1f;
        /// <summary>Moves the unit at a fixed velocity for a while (evades, dives, leaps), whatever the path says.</summary>
        public void Push(Vector3 velocity, float seconds) { _override = velocity; _overrideUntil = Now + seconds; }
        static readonly List<HVec3> PathTmp = new List<HVec3>(32);

        // state timers
        float _stateUntil, _stateStarted; Vector3 _threat;
        float _nextIdleLook; Vector3 _idleLook;
        int _patrolIndex = -1, _patrolDir = 1; float _patrolWaitUntil = -1f;

        // damage
        float _lastHitTime = -100f, _lastVitality = 1f, _danger; float _damagedAt = -100f;
        float _dangerPrev, _shotNearAt = -100f;
        float _lastBody = -1f, _recentBodyDamage;
        int _friendsKilled; float _friendsKilledAt = -100f;

        internal int TickPhase;
        public float Now => HaloAi.Time;
        public bool Alive => Unit != null && Unit.Alive && State != Mode.Dead;
        /// <summary>Body + shield strength 0..1 (for platoon rules and ai_strength).</summary>
        public float Strength => Unit != null && Unit.Alive ? Mathf.Clamp01(Unit.Body) : 0f;
        public HaloTeam Team => Unit != null ? Unit.Team : HaloTeam.None;
        public Vector3 Position => Unit != null ? Unit.transform.position : Home;
        public bool Flying => (A != null && A.IsFlying) || (Unit != null && Unit.Def != null && Unit.Def.Flying);
        public string Label => (Enc != null ? Enc.Name + (Squad >= 0 && Squad < Enc.Data.Squads.Count ? "/" + Enc.Data.Squads[Squad].Name : "") : "free") + "#" + Id;

        public HaloActor(int id) { Id = id; TickPhase = id % 3; }

        // ------------------------------------------------------------------ the tick

        public void Tick(float dt)
        {
            TickInner(dt);
            _dangerPrev = _danger;          // a rise before the next tick = a ShotAt (Combat adds to _danger)
        }

        void TickInner(float dt)
        {
            if (State == Mode.Dead) return;
            if (Unit == null || !Unit.Alive) { Died(); return; }
            if (Braindead || (Enc != null && Enc.Braindead) || !HaloAi.Enabled)
            {
                Unit.MoveVelocity = Vector3.zero; Unit.Firing = false; Unit.Frozen = Braindead || (Enc != null && Enc.Braindead);
                return;
            }
            Unit.Frozen = false;
            Unit.Firing = false;
            if (Unit.Suspended) { Unit.MoveVelocity = Vector3.zero; return; }       // dormant / held by the mover (workstream 1)

            // leaving a seat: here is home now (a dropship's starting locations are in the air)
            bool seated = Unit.Seat != null;
            if (_wasSeated && !seated) { Home = Position; HomeYaw = Unit.transform.eulerAngles.y; }
            _wasSeated = seated;

            // playing dead (Flood combat forms): frozen, the target kept; back to the fight afterwards
            if (Unit.Feigning)
            {
                Unit.MoveVelocity = Vector3.zero; Unit.AimPoint = Vector3.zero;
                _wasFeigning = true;
                _lastVitality = Unit.Body + Unit.Shield; _lastBody = Unit.Body;
                if (Target != null) TargetSeenAt = Mathf.Max(TargetSeenAt, Now - 0.5f);     // ours: do not lose it while lying there
                return;
            }
            if (_wasFeigning) { _wasFeigning = false; if (Target != null && Target.Alive && State != Mode.Combat) SetState(Mode.Combat); }

            // friends killed are forgotten slowly: one every 30 s (ours)
            if (_friendsKilled > 0 && Now - _friendsKilledAt > 30f) { _friendsKilled--; _friendsKilledAt = Now; }

            SenseDamage(dt);
            int phase = (HaloAi.TickCount + TickPhase) % 3;
            if (phase == 1 && HaloAi.Dangers.Count > 0) DodgeGrenades();
            if (phase == 0) Perceive();
            UpdateTarget();
            if (phase == 2) { TickDanger(); DodgeVehicles(); }

            // vehicles: riding (drive / gun) and boarding take the tick
            if (Unit.Seat != null && Vehicle != null)
            {
                if (Commands != null && Commands.Active) Commands.Step(this, dt);
                TickRiding();
                return;
            }
            if (TickBoarding()) return;

            // a command list without initiative breaks when the actor is hurt or an enemy comes close (frees the c20 carriers)
            if (Commands != null && Commands.Active && Commands.List != null && !Commands.List.AllowInitiative)
            {
                if (_damagedAt == Now) BreakCommandList("damage");
                else if (phase == 2 && EnemyClose()) BreakCommandList("proximity");
            }

            if (Commands != null && Commands.Active)
            {
                bool interrupt = Commands.List.AllowInitiative && Target != null && TargetVisible;
                if (!interrupt)
                {
                    Commands.Step(this, dt);
                    if (Commands.List.AllowTargeting && Target != null && TargetVisible) Shoot(dt, true);
                    Steer(dt);
                    ClampMoveForward();
                    return;
                }
            }

            switch (State)
            {
                case Mode.Idle: TickIdle(dt); break;
                case Mode.Patrol: TickPatrol(dt); break;
                case Mode.Search: TickSearch(dt); break;
                case Mode.Combat: TickCombat(dt); break;
                case Mode.Flee: TickFlee(dt); break;
                case Mode.Cower: TickCower(dt); break;
                case Mode.Hide: TickHide(dt); break;
                case Mode.Berserk: TickBerserk(dt); break;
            }
            Steer(dt);
            ClampMoveForward();
        }

        public void SetState(Mode m, float duration = 0f)
        {
            if (State == Mode.Dead) return;
            if (State != m)
            {
                if (HaloAi.Verbose) HaloLog.Info("AI: " + Label + " " + State + " -> " + m + (Target != null ? " (target " + Target + ")" : ""));
                HaloAi.NoteState(m);
                if (m == Mode.Search && Target != null) HaloAiVoice.Say(this, HaloAiVoice.SearchStart, 0.5f);
                else if (m == Mode.Hide) HaloAiVoice.Say(this, HaloAiVoice.Cover, 0.6f);
                State = m;
                _stateStarted = Now;
                ReleasePosition();
            }
            _stateUntil = duration > 0f ? Now + duration : -1f;
            if (Unit != null) Unit.Stance = m == Mode.Flee ? "flee" : (m == Mode.Combat || m == Mode.Search || m == Mode.Hide || m == Mode.Berserk ? null : IdleStance());
        }

        string IdleStance()
        {
            var s = Start != null && Start.InitialState != HaloAiState.None ? Start.InitialState : InitialState;
            if (State == Mode.Idle && s == HaloAiState.Sleeping) return "asleep";
            if (State == Mode.Idle && s == HaloAiState.Alert) return "alert";
            return null;
        }

        /// <summary>The state an actor settles in when it has nothing to do (squad / starting location return state).</summary>
        public void ReturnToRest()
        {
            Target = null; TargetVisible = false; Berserking = false;
            if (Unit != null && Unit.Grounded && Unit.Seat == null) { Home = Position; HomeYaw = Unit.transform.eulerAngles.y; }    // no walk back to the spawn
            var s = ReturnState != HaloAiState.None ? ReturnState : InitialState;
            EnterAiState(s);
        }

        /// <summary>Enters one of the scenario's / scripts' AI states (ai_set_current_state, a squad's initial state).</summary>
        public void EnterAiState(HaloAiState s)
        {
            _patrolIndex = -1;
            switch (s)
            {
                case HaloAiState.MovingRepeatSamePosition:
                case HaloAiState.MovingLoop:
                case HaloAiState.MovingLoopBackAndForth:
                case HaloAiState.MovingLoopRandomly:
                case HaloAiState.MovingRandomly:
                    SetState(Mode.Patrol); break;
                case HaloAiState.Searching:
                    SetState(Mode.Search, SearchTime() * 2f); break;
                case HaloAiState.Fleeing:
                    Panic(null, 1f); break;
                default:
                    SetState(Mode.Idle); break;
            }
            InitialState = s;
            if (Unit != null) Unit.Stance = IdleStance();
        }

        // ------------------------------------------------------------------ perception

        float PerceptionTime()
        {
            if (A == null) return 0.5f;
            if (State == Mode.Combat || State == Mode.Berserk || State == Mode.Hide) return Mathf.Max(0f, A.CombatPerceptionTime);
            // searching for a target lost in the last 10 s is combat; the 'searching' initial state with no recent target is alert (ours)
            if (State == Mode.Search) return Mathf.Max(0f, Now - _lastTargetAt < 10f ? A.CombatPerceptionTime : A.GuardPerceptionTime);
            var s = InitialState;
            if (s == HaloAiState.Alert || s == HaloAiState.Guarding || s == HaloAiState.GuardingAtGuardPosition) return Mathf.Max(0f, A.GuardPerceptionTime);
            return Mathf.Max(0f, A.NonCombatPerceptionTime);
        }

        bool Sleeping => State == Mode.Idle && InitialState == HaloAiState.Sleeping;

        bool ShouldSwitch(HaloAiBody b)
        {
            if (Target == null || !Target.Alive) return true;
            if (b == Target) return false;
            if (b.Preferred && !Target.Preferred) return true;
            if (!TargetVisible && Now - TargetSeenAt > 1.5f) return true;
            // an enemy that hurt us in the last second while the target has not for 3 s (ours)
            if (b == _lastAttacker && Now - _lastAttackerAt < 1f && Now - _targetHurtAt > 3f) return true;
            // much closer
            float dNew = (b.Feet - Position).sqrMagnitude, dOld = (Target.Feet - Position).sqrMagnitude;
            return dNew < dOld * 0.25f;
        }

        bool CanSee(Vector3 eye, Vector3 fwd, HaloAiBody b, float range, float cosMax, float periDist, float cosPeri = -1f)
        {
            Vector3 to = b.Center - eye;
            float d = to.magnitude;
            if (d > range) return false;
            if (cosMax > -1f && d > 0.5f)
            {
                float c = Vector3.Dot(fwd, to / d);
                bool inCone = c >= cosMax || (d <= periDist && c >= cosPeri);
                if (!inCone) return false;
            }
            // head first, then centre (half the budget each time)
            if (HaloAiBodies.Clear(eye, b.Head, false)) return true;
            return HaloAiBodies.Clear(eye, b.Center, false);
        }

        public bool IsEnemy(HaloAiBody b)
        {
            if (b == null || !b.Alive || b == Body || b.Disregarded) return false;
            if (Enc != null && Enc.Playfight) return false;
            return HaloAllegiance.AreEnemies(Team, b.Team);
        }

        void Acquire(HaloAiBody b, string how)
        {
            if (b == null) return;
            bool fresh = Target != b;
            // surprise: a new enemy while at rest (no target for 10 s), inside the actr surprise distance or hurting us unseen
            bool surprise = false;
            if (fresh && A != null && (how == "sight" || how == "damage") && (State == Mode.Idle || State == Mode.Patrol) && Now - _lastTargetAt > 10f)
            {
                float sd = A.SurpriseDistance * HaloSpace.Unit;
                bool near = sd > 0f && (b.Feet - Position).sqrMagnitude < sd * sd;
                surprise = near || (how == "damage" && !SeesNow(b));
            }
            Target = b;
            TargetLastSeen = b.Feet;
            TargetSeenAt = Now;
            _lastTargetAt = Now;
            if (fresh)
            {
                TargetSince = Now;
                _targetHurtAt = b == _lastAttacker && Now - _lastAttackerAt < 0.1f ? Now : -100f;
                OnNewTarget();
                HaloAiVoice.Say(this, how == "damage" || surprise ? HaloAiVoice.UnexpectedEnemy : (State == Mode.Combat ? HaloAiVoice.NewEnemyRecentCombat : HaloAiVoice.NewCombatAlone));
                HaloAi.NoteAcquire(this, b, how);
            }
            if (State == Mode.Idle || State == Mode.Patrol || State == Mode.Search)
                if (Released) SetState(Mode.Combat);
            if (surprise)
            {
                _surprises++;
                if (_surprises <= 3 || HaloAi.Verbose) HaloLog.Info("AI: " + Label + " surprised by " + b + " (" + how + ", " + ((b.Feet - Position).magnitude / HaloSpace.Unit).ToString("0.0") + " wu)");
                if (A.Has(HaloActorDef.PanicWhenSurprised)) { HaloAi.NotePanic(this, "surprised"); Panic(b, 1f); }
                else { Surprised = true; LastSurpriseAt = Now; }      // Combat consumes it (surprise delay / fire wildly)
            }
        }

        static int _surprises;

        /// <summary>The squad's delay timer let it fight (else it holds until ai_timer_expire / the timer runs out).</summary>
        bool Released => Enc == null || Enc.SquadReleased(Squad);

        void UpdateTarget()
        {
            ResolveAdoptedTarget();
            if (Target != null && (!Target.Alive || Target.Disregarded || !HaloAllegiance.AreEnemies(Team, Target.Team)))
            {
                var was = Target;
                Target = null; TargetVisible = false;
                if (was != null && !was.Alive) HaloAiVoice.Say(this, was.IsPlayer ? HaloAiVoice.KilledEnemyPlayer : HaloAiVoice.KilledEnemy, 0.6f);
                if (was != null && !was.Alive && (State == Mode.Combat || State == Mode.Berserk)) { _stateUntil = Now + 1.5f; }
            }
            TargetVisible = Target != null && Now - TargetSeenAt < 0.35f;
            if (Target != null) _lastTargetAt = Now;
            if (MagicTarget != null) { if (MagicTarget.Alive && IsEnemy(MagicTarget)) Acquire(MagicTarget, "magic"); MagicTarget = null; }
            if (Target == null && (State == Mode.Combat || State == Mode.Berserk) && (_stateUntil < 0f || Now >= _stateUntil))
                ReturnToRest();
            // a squad held by its timer starts the timer on first contact
            if (Target != null && Enc != null && Squad >= 0 && !Enc.Squads[Squad].TimerStarted)
                Enc.StartTimer(Squad, Now);
            if (Target != null && State != Mode.Combat && (State == Mode.Idle || State == Mode.Patrol) && Released) SetState(Mode.Combat);
        }

        /// <summary>Hits on the unit: who hit us becomes the target (a player by its id, else the enemy we were hit from), panic and berserk checks.</summary>
        void SenseDamage(float dt)
        {
            float vit = Unit.Body + Unit.Shield;
            float lost = _lastVitality - vit;
            _lastVitality = vit;
            // recent body damage (berserk): decays over about a second (ours)
            float body = Unit.Body;
            if (_lastBody < 0f) _lastBody = body;
            _recentBodyDamage = _recentBodyDamage * Mathf.Max(0f, 1f - dt) + Mathf.Max(0f, _lastBody - body);
            _lastBody = body;
            bool hitNow = Unit.LastHitTime > _lastHitTime + 1e-4f;
            // Combat's ShotAt (a player's shot passing close) raised the danger since our last tick
            if (!hitNow && _danger > _dangerPrev + 0.3f) _shotNearAt = Now;
            _danger = Mathf.Max(0f, _danger - 1f / 30f);
            if (hitNow)
            {
                _lastHitTime = Unit.LastHitTime;
                _damagedAt = Now;
                var hit = Unit.LastHit;
                float frac = Mathf.Max(0f, lost);
                _danger += 0.5f + frac * 4f;
                // who?
                HaloAiBody from = null;
                if (hit.SourcePlayer >= 0 || hit.SourcePlayer == -3) from = PlayerBody(hit.SourcePlayer);
                if (from == null) from = EnemyFrom(hit.Point - hit.Direction * 5f, hit.Direction);
                if (from != null && IsEnemy(from))
                {
                    _lastAttacker = from; _lastAttackerAt = Now;
                    if (from == Target) _targetHurtAt = Now;
                }
                // damage acquires at once (no perception time): with no visible target, or an attacker while the target leaves us alone
                if (from != null && IsEnemy(from) && (Target == null || !TargetVisible || (from != Target && Now - _targetHurtAt > 3f))) Acquire(from, "damage");
                if (Sleeping || State == Mode.Idle || State == Mode.Patrol) { InitialState = HaloAiState.Alert; if (from != null && Released) SetState(Mode.Combat); }
                // panic from a big hit
                // ours: a chance, and only once hurt (a panic on every hit is not what the game plays)
                if (A != null && A.PanicDamageThreshold > 0f && frac >= A.PanicDamageThreshold && Unit.Body < 0.75f && State != Mode.Flee && !Berserking && HaloAi.Chance(0.5f))
                { HaloAi.NotePanic(this, "damage"); Panic(from, 1f); }
                // berserk from damage: the body is down to the actr berserk damage threshold and the recent body damage reaches the berserk
                // damage amount (from field pair, unverified: hunter 0.001 / 0.25, flood combat 0.1 / 0.55, elite 0.5 / 0.3)
                if (A != null && A.BerserkDamageThreshold > 0f && !Berserking && State != Mode.Flee && State != Mode.Cower
                    && body <= A.BerserkDamageThreshold && _recentBodyDamage >= Mathf.Max(1e-4f, A.BerserkDamageAmount))
                    GoBerserk("damage");
                if (A != null && A.DiveIntoCoverChance > 0f && State == Mode.Combat && HaloAi.Chance(A.DiveIntoCoverChance * 0.5f)) WantCover();
            }
        }

        HaloAiBody PlayerBody(int sourcePlayer)
        {
            var all = HaloAiBodies.All;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b.Kind == HaloAiBody.Kinds.LocalPlayer && (sourcePlayer == HaloLocal.NetId || sourcePlayer == -1)) return b;
                if (b.Kind == HaloAiBody.Kinds.StandIn && sourcePlayer == -3) return b;
                if (b.Kind == HaloAiBody.Kinds.RemotePlayer && b.Remote != null && b.Remote.Slot == sourcePlayer) return b;
            }
            return HaloAiBodies.LocalPlayer;
        }

        /// <summary>The enemy nearest to the line the hit came along (a guess for hits from AI units).</summary>
        HaloAiBody EnemyFrom(Vector3 origin, Vector3 dir)
        {
            var all = HaloAiBodies.All;
            HaloAiBody best = null; float bd = float.MaxValue;
            Vector3 me = Position;
            Vector3 back = dir.sqrMagnitude > 1e-6f ? -dir.normalized : Vector3.zero;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (!IsEnemy(b)) continue;
                Vector3 to = b.Center - me;
                float d = to.magnitude;
                if (d > 80f) continue;
                float off = back != Vector3.zero && d > 0.1f ? 1f - Vector3.Dot(back, to / d) : 0.5f;
                float score = d * 0.1f + off * 20f;
                if (score < bd) { bd = score; best = b; }
            }
            return best;
        }

        /// <summary>A shot was heard at a point (players' and AI shots).</summary>
        public void Hear(Vector3 at, HaloAiBody shooter)
        {
            if (shooter != null) shooter.FiredAt = Now;          // "shooting" for the danger triggers
            if (Deaf || (Enc != null && Enc.Deaf) || A == null || HaloAi.DebugDeaf) return;
            if (State == Mode.Flee || State == Mode.Cower) return;
            float range = A.HearingDistance * HaloSpace.Unit;
            if ((at - Position).sqrMagnitude > range * range) return;
            if (shooter != null && IsEnemy(shooter))
            {
                if (Target == null || (!TargetVisible && Now - TargetSeenAt > 2f))
                {
                    Target = shooter; TargetLastSeen = at; TargetSeenAt = Now - 1f; TargetSince = Now;
                    if (Sleeping) InitialState = HaloAiState.Alert;
                    if ((State == Mode.Idle || State == Mode.Patrol) && Released) SetState(Mode.Search, SearchTime());
                }
            }
            else if (State == Mode.Idle && Sleeping) InitialState = HaloAiState.Alert;
        }

        /// <summary>Script ai_magically_see_*: aware of the body now, wherever it is.</summary>
        public void MagicallySee(HaloAiBody b) { if (b != null) MagicTarget = b; }

        public void MagicallySeePlayers()
        {
            var p = HaloAiBodies.NearestPlayer(Position);
            if (p != null) MagicallySee(p);
        }

        public float SearchTime()
        {
            float t = A != null ? A.TargetSearchTime.Pick(HaloAi.Rng) : 5f;
            if (t <= 0f) t = 5f;
            if (Enc != null && Enc.Data.SearchBehavior == 2) t *= 3f;          // tenacious
            return t;
        }

        // ------------------------------------------------------------------ death

        void Died()
        {
            if (State == Mode.Dead) return;
            var killer = Unit != null ? Unit.LastHit : default(HaloHit);
            State = Mode.Dead;
            ReleasePosition();
            if (Unit != null) { Unit.MoveVelocity = Vector3.zero; Unit.Firing = false; }
            if (Commands != null) Commands.Stop();
            HaloAi.OnActorDied(this, killer);
        }

        /// <summary>A friend died near us (same encounter or within sight): panic checks (actr friends killed / leader killed).</summary>
        public void FriendDied(HaloActor dead)
        {
            if (!Alive || A == null || dead == this) return;
            // hunters come in bonded pairs: the partner's death sends the other berserk (Halo)
            if (A.Type == HaloActorType.Hunter && dead.A != null && dead.A.Type == HaloActorType.Hunter && dead.Enc == Enc && dead.Squad == Squad && !Berserking)
            { if (dead.Target != null && Target == null) Acquire(dead.Target, "partner"); if (Target != null) GoBerserk("partner died"); return; }
            if (State == Mode.Flee || State == Mode.Cower || Berserking) return;
            float d = (dead.Position - Position).magnitude / HaloSpace.Unit;
            // ours: it must notice the death - close by (5 wu), or in the fight and within 12 wu with a sight line
            bool fighting = Target != null && (State == Mode.Combat || State == Mode.Search || State == Mode.Hide);
            if (d > 12f || (d > 5f && !fighting)) return;
            if (d > 5f && !HaloAiBodies.Clear(Unit.Eye, dead.Position + Vector3.up, true)) return;
            _friendsKilled++; _friendsKilledAt = Now;
            HaloAiVoice.Say(this, dead.Body != null && dead.Body.IsPlayer ? HaloAiVoice.FriendPlayerDied : HaloAiVoice.FriendDied, 0.4f);
            bool leader = dead.A != null && dead.A.Type == A.LeaderType && A.LeaderType != A.Type && A.LeaderKilledPanicChance > 0f;
            if (leader && HaloAi.Chance(A.LeaderKilledPanicChance)) { HaloAiVoice.Say(this, HaloAiVoice.FleeLeaderDied); HaloAi.NotePanic(this, "leader killed"); Panic(dead.Target, 1f); return; }
            if (A.FriendsKilledTrigger > 0 && _friendsKilled >= A.FriendsKilledTrigger && HaloAi.Chance(A.FriendKilledPanicChance)) { HaloAi.NotePanic(this, "friends killed"); Panic(dead.Target, 1f); }
        }

        // ------------------------------------------------------------------ squad changes

        public void MoveToSquad(HaloAiEncounter enc, int squad)
        {
            if (enc != Enc)
            {
                if (Enc != null) Enc.Actors.Remove(this);
                ReleasePosition();
                Enc = enc;
                if (enc != null && !enc.Actors.Contains(this)) enc.Actors.Add(this);
            }
            Squad = squad;
            Platoon = enc != null && squad >= 0 && squad < enc.Data.Squads.Count ? enc.Data.Squads[squad].Platoon : -1;
            ReleasePosition();
            _fposUntil = 0f;
            // the new squad's areas are home now: an idle actor never walks back to its spawn (ai_migrate, maneuvers)
            if (Unit != null && Unit.Seat == null) { Home = Position; HomeYaw = Unit.transform.eulerAngles.y; }
        }

        // ------------------------------------------------------------------ movement

        /// <summary>Head for a point (Unity space) at a fraction of the unit's run speed; repaths when the goal moves (at most every 0.5 s
        /// for a goal that keeps moving), walks back when it drifted off a goal it had reached.</summary>
        public void MoveTo(Vector3 goal, float speedFrac, float accept = 0.45f)
        {
            _speedFrac = speedFrac;
            _acceptRadius = accept;
            bool wasMoving = Moving;
            if (_hasGoal)
            {
                float moved = (goal - _goal).sqrMagnitude;
                if (_arrived && moved < 0.36f)
                {
                    // arrival is re-checked: an actor pushed off its goal walks back
                    Vector3 off = goal - Position; if (!Flying) off.y = 0f;
                    float slack = accept + 0.5f;            // ours
                    if (off.sqrMagnitude <= slack * slack) return;
                    if (IsBadGoal(goal)) return;
                    _goal = goal; _arrived = false; ResetStuck(); Repath();
                    return;
                }
                if (!_arrived && moved < 0.36f && Now < _repathAt) return;
                if (!_arrived && Now - _lastRepath < 0.5f)
                {
                    // a moving goal (a leader, a target): follow it along the current path, no new search yet
                    _goal = goal;
                    if (_pathExact && _path.Count > 0) _path[_path.Count - 1] = goal;
                    return;
                }
            }
            if (IsBadGoal(goal)) { if (wasMoving) Stop(); return; }
            _goal = goal; _hasGoal = true; _arrived = false;
            if (!wasMoving) ResetStuck();
            Repath();
        }

        public void Stop() { _hasGoal = false; _path.Clear(); _arrived = true; if (Unit != null) Unit.MoveVelocity = Vector3.zero; }

        public bool Arrived => !_hasGoal || _arrived;
        public bool Moving => _hasGoal && !_arrived;
        public Vector3 Goal => _goal;

        /// <summary>The walker's steepest slope for the navigation (normal z; humans 0.707).</summary>
        float NavSlope => Unit != null && Unit.Def != null ? Unit.Def.CosMaxSlope : HaloAiNav.HumanSlopeNz;

        void ResetStuck() { _stuckRef = Position; _stuckCheckAt = Now + 1.5f; _stuckCount = 0; }

        void Repath()
        {
            _path.Clear(); _pathIdx = 0; _pathExact = false;
            _repathAt = Now + 2.5f;
            _lastRepath = Now;
            var nav = HaloAi.Nav;
            if (nav != null && !Flying)
            {
                Vector3 from = HaloSpace.ToHalo(Position), to = HaloSpace.ToHalo(_goal);
                float r = A != null && A.PathfindingRadius > 0f ? A.PathfindingRadius : 0.1f;
                bool ok = nav.FindPath(new HVec3(from.x, from.y, from.z + 0.05f), new HVec3(to.x, to.y, to.z + 0.05f), r, PathTmp, 3000, NavSlope);
                HaloAi.NotePath(nav.LastExpanded);
                bool exact = ok;
                if (!ok)
                {
                    // the goal is on another island (a ledge, a cliff, a crate, behind a lip) or off the navigation: go to the nearest
                    // reachable point of our own island instead of walking off an edge (Halo's actors stop at the closest spot they can reach)
                    int ca = HaloAiEncounter.ComponentAt(Position), cb = HaloAiEncounter.ComponentAt(_goal);
                    if (ca >= 0 && cb == ca)
                    {
                        // same island: narrow portals or the search budget - once more without the radius and a bigger budget
                        ok = exact = nav.FindPath(new HVec3(from.x, from.y, from.z + 0.05f), new HVec3(to.x, to.y, to.z + 0.05f), 0f, PathTmp, 9000, NavSlope);
                    }
                    else if (ca >= 0 && cb != ca)
                    {
                        HVec3 near;
                        if (nav.NearestInComponent(ca, to.x, to.y, to.z, 30f, out near) && nav.FindPath(new HVec3(from.x, from.y, from.z + 0.05f), new HVec3(near.X, near.Y, near.Z + 0.05f), r, PathTmp, 3000, NavSlope))
                        { HaloAi.NoteDetour(); _pathFailedAt = Now; }
                        else { _arrived = true; _pathFailedAt = Now; HaloAi.NoteUnreachable(); return; }
                    }
                    else if (ca < 0)
                    {
                        // we stand off the navigation: step onto the nearest polygon first (the goal's island, else any), never a
                        // straight line through walls
                        if (!PathFromOffNav(nav, from, to, cb, r)) { _arrived = true; _pathFailedAt = Now; HaloAi.NoteUnreachable(); }
                        return;
                    }
                }
                for (int i = 0; i < PathTmp.Count; i++) _path.Add(HaloSpace.Pos(PathTmp[i]));
                if (_path.Count > 0 && exact) { _path[_path.Count - 1] = _goal; _pathExact = true; }
            }
            else { _path.Add(_goal); _pathExact = true; }
        }

        bool PathFromOffNav(HaloAiNav nav, Vector3 from, Vector3 to, int goalComp, float r)
        {
            HVec3 entry = default(HVec3);
            bool got = goalComp >= 0 && nav.NearestInComponent(goalComp, from.x, from.y, from.z, 4f, out entry);     // 4 wu: ours
            if (!got)
            {
                int poly = nav.Locate(from.x, from.y, from.z + 0.1f, 1f, 3f, 4f);
                if (poly >= 0) { entry = nav.ClosestPoint(poly, from.x, from.y); got = true; }
            }
            if (got)
            {
                Vector3 e = HaloSpace.Pos(entry);
                if (LineClear(Position, e))
                {
                    _path.Add(e);
                    bool ok = nav.FindPath(new HVec3(entry.X, entry.Y, entry.Z + 0.05f), new HVec3(to.x, to.y, to.z + 0.05f), r, PathTmp, 3000, NavSlope);
                    if (ok)
                    {
                        for (int i = 0; i < PathTmp.Count; i++) _path.Add(HaloSpace.Pos(PathTmp[i]));
                        _path[_path.Count - 1] = _goal; _pathExact = true;
                    }
                    else _pathFailedAt = Now;
                    if (++_offNavStarts <= 3 || HaloAi.Verbose) HaloLog.Info("AI: " + Label + " started off the navigation: entry " + (e - Position).magnitude.ToString("0.0") + " m away" + (ok ? "" : " (the goal is not reachable from it)"));
                    return true;
                }
            }
            // no polygon near: a straight line only when nothing is in the way
            if (LineClear(Position, _goal)) { _path.Add(_goal); _pathExact = true; return true; }
            return false;
        }

        static int _offNavStarts;

        /// <summary>A knee-height line between two points with no world geometry in it (no ray budget: path planning, not sight).</summary>
        static bool LineClear(Vector3 a, Vector3 b)
        {
            Vector3 up = Vector3.up * 0.5f;     // ours: knee height
            return !Physics.Linecast(a + up, b + up, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Writes MoveVelocity toward the next waypoint, never off the navigation / over a drop; stuck tracking and give-up.</summary>
        void Steer(float dt)
        {
            if (Unit == null) return;
            if (Unit.WasRecovered) { Unit.WasRecovered = false; Recovered(); }
            if (Unit.WalkingInhibited) { Unit.MoveVelocity = Vector3.zero; return; }
            if (_overrideUntil > Now)
            {
                Unit.MoveVelocity = NavAllows(Position, _override) ? _override : Vector3.zero;
                return;
            }
            if (!_hasGoal || _arrived || _path.Count == 0) { Unit.MoveVelocity = Vector3.zero; return; }
            Vector3 pos = Position;
            Vector3 next = _path[_pathIdx];
            Vector3 d = next - pos;
            if (!Flying) d.y = 0f;
            bool last = _pathIdx >= _path.Count - 1;
            float accept = last ? _acceptRadius : 0.35f;
            while (d.magnitude < accept)
            {
                if (last) { _arrived = true; Unit.MoveVelocity = Vector3.zero; return; }
                _pathIdx++;
                next = _path[_pathIdx]; d = next - pos; if (!Flying) d.y = 0f;
                last = _pathIdx >= _path.Count - 1;
                accept = last ? _acceptRadius : 0.35f;
            }
            Vector3 dir = d.normalized;
            float speed = RunSpeedToward(dir) * _speedFrac;
            // slow down into the goal
            if (last) speed = Mathf.Min(speed, Mathf.Max(0.6f, d.magnitude * 2.5f));
            // a command-list hold (pause / look / wait / animate) must not drift with the separation push
            Vector3 v = dir * speed + (Commands != null && Commands.Holding ? Vector3.zero : Separation());
            if (!Flying) v.y = 0f;
            if (!NavAllows(pos, v))
            {
                // the next foot position is off the navigation over a drop: hold and look for another way
                v = Vector3.zero;
                OffNavBlocks++;
                if (OffNavBlocks <= 3 || HaloAi.Verbose) HaloLog.Info("AI: " + Label + " held at an edge / off the navigation at " + pos.ToString("0.0"));
                if (Now - _lastRepath >= 0.5f) Repath();
            }
            Unit.MoveVelocity = v;
            // stuck? progress is tracked on its own (a repath does not reset it): 0.25 m within 1.5 s
            Vector3 moved = pos - _stuckRef; moved.y = 0f;
            if (moved.sqrMagnitude >= 0.25f * 0.25f) { _stuckRef = pos; _stuckCheckAt = Now + 1.5f; _stuckCount = 0; }
            else if (Now >= _stuckCheckAt)
            {
                _stuckCount++;
                _stuckCheckAt = Now + 1.5f;
                if (_stuckCount >= 2) GiveUp(); else Repath();
            }
        }

        /// <summary>Lab / log counters: steps refused at an edge, stuck give-ups, mover recoveries seen.</summary>
        public static int OffNavBlocks, StuckGiveUps, Recoveries;

        /// <summary>No progress twice: release the position, remember it (and the goal) as bad for 10 s, stand.</summary>
        void GiveUp()
        {
            StuckGiveUps++;
            HaloAi.NoteStuck();
            if (StuckGiveUps <= 3 || HaloAi.Verbose) HaloLog.Info("AI: " + Label + " stuck going to " + _goal.ToString("0.0") + " - gave up" + (_fpos >= 0 ? " (position " + _fpos + ")" : ""));
            AddBad(_fpos, _goal);
            ReleasePosition();
            _fposUntil = 0f;
            _stuckCount = 0;
            Stop();
        }

        // bad goals (10 s): firing position indices and points the actor could not reach
        readonly int[] _badPos = { -1, -1, -1, -1 };
        readonly Vector3[] _badPoint = new Vector3[4];
        readonly float[] _badUntil = new float[4];
        int _badNext;

        void AddBad(int index, Vector3 point)
        {
            _badPos[_badNext] = index; _badPoint[_badNext] = point; _badUntil[_badNext] = Now + 10f;     // 10 s: ours
            _badNext = (_badNext + 1) % _badPos.Length;
        }

        /// <summary>The encounter firing position <paramref name="index"/> was given up as unreachable in the last 10 s (PickPosition skips it).</summary>
        public bool IsBadPosition(int index)
        {
            if (index < 0) return false;
            for (int i = 0; i < _badPos.Length; i++) if (_badPos[i] == index && Now < _badUntil[i]) return true;
            return false;
        }

        bool IsBadGoal(Vector3 p)
        {
            for (int i = 0; i < _badPos.Length; i++)
                if (Now < _badUntil[i] && (_badPoint[i] - p).sqrMagnitude < 1f) return true;
            return false;
        }

        /// <summary>The mover put the unit back on the ground (it was inside / under the floor): a new path from here.</summary>
        void Recovered()
        {
            Recoveries++;
            if (Recoveries <= 3 || HaloAi.Verbose) HaloLog.Info("AI: " + Label + " was recovered by the mover at " + Position.ToString("0.0") + " - repath");
            // only a walk in progress needs a new path (an arrived / standing actor keeps its goal: re-opening it walked actors back to stale
            // goals and repathed every 0.25 s while the mover kept reporting a recovery in place)
            // (stuck tracking goes on: a unit recovered in place again and again still gives up)
            // a put-back to the last ground is a jump, not progress: the stuck reference follows it (the count is kept)
            _stuckRef = Position;
            if (!Moving) { _recoverCount = 0; return; }
            // recovered again and again on the way to one goal (walking back into a spot the mover keeps pulling it out of): that goal is
            // unreachable - give it up as Halo's actors drop an unreachable firing position (3 within 5 s: ours)
            if (Now - _recoverWindow > 5f) { _recoverWindow = Now; _recoverCount = 0; }
            if (++_recoverCount >= 3)
            {
                _recoverCount = 0;
                if (RecoveryGiveUps++ < 3 || HaloAi.Verbose) HaloLog.Info("AI: " + Label + " recovered 3 times within 5 s going to " + _goal.ToString("0.0") + " - gave the goal up");
                GiveUp();
                return;
            }
            if (Now - _lastRepath >= 0.5f) Repath();
        }

        int _recoverCount; float _recoverWindow = -100f;
        /// <summary>Lab / log counter: goals given up after repeated mover recoveries.</summary>
        public static int RecoveryGiveUps;

        /// <summary>
        /// Whether moving at <paramref name="v"/> keeps the feet on walkable ground: the point a short step ahead is on a navigation polygon
        /// within the step height, or (off the navigation) the world has ground under it within the step height. Units in the air, flying
        /// or seated are not held.
        /// </summary>
        bool NavAllows(Vector3 pos, Vector3 v)
        {
            if (Flying || Unit == null || Unit.Seat != null || !Unit.Grounded) return true;
            Vector3 flat = v; flat.y = 0f;
            float sp = flat.magnitude;
            if (sp < 0.05f) return true;
            const float stepUp = 0.6f;                                          // ours: the mover's largest step (m)
            Vector3 ahead = pos + flat / sp * Mathf.Max(0.45f, sp * 0.15f);     // ours: a short look ahead
            var nav = HaloAi.Nav;
            if (nav != null)
            {
                Vector3 h = HaloSpace.ToHalo(ahead);
                const float upWu = 1f / HaloSpace.Unit, downWu = stepUp / HaloSpace.Unit;
                if (nav.Locate(h.x, h.y, h.z + 0.02f, upWu, downWu, 0f) >= 0) return true;
            }
            // off the navigation polygons: solid ground right under the step is fine (nav gaps, crate tops), a drop is not
            return Physics.Raycast(ahead + Vector3.up * stepUp, Vector3.down, stepUp * 2f, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore);
        }

        public float RunSpeed()
        {
            float s = Unit != null ? Unit.NaturalSpeed : 3f;
            if (s < 0.5f || s > 12f) s = 3f;
            return s;
        }

        /// <summary>The run speed for a world direction (strafe / back-pedal clips have their own speed).</summary>
        float RunSpeedToward(Vector3 dir)
        {
            if (Unit == null) return 3f;
            float s = Unit.NaturalSpeedToward(dir);
            if (s < 0.5f || s > 12f) s = RunSpeed();
            return s;
        }

        Vector3 _sep; int _sepTick = -10;
        /// <summary>A push away from same-team friends on foot standing in the way (only while moving; Steer clamps it to the navigation).</summary>
        Vector3 Separation()
        {
            if (HaloAi.TickCount - _sepTick < 3) return _sep;
            _sepTick = HaloAi.TickCount;
            Vector3 push = Vector3.zero;
            var list = HaloAi.Actors;
            Vector3 me = Position;
            float r = A != null && A.FriendAvoidDist > 0f ? Mathf.Clamp(A.FriendAvoidDist * HaloSpace.Unit, 0.6f, 1.5f) : 0.8f;     // clamp: ours
            HaloTeam team = Team;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == this || !o.Alive || o.Unit == null || o.Unit.Seat != null || o.Team != team) continue;
                Vector3 d = me - o.Position; d.y = 0f;
                float m = d.magnitude;
                if (m < r && m > 1e-3f) push += d / m * (r - m) * 2f;
            }
            _sep = push;
            return push;
        }

        /// <summary>Turns the facing toward a point; AimPoint only when there is something to aim at.</summary>
        void Face(Vector3 look, bool aim)
        {
            if (Unit == null) return;
            Unit.LookPoint = look;
            Unit.AimPoint = aim ? look : Vector3.zero;
        }

        /// <summary>A point on the navigation near a Unity point (null-safe: the point itself without a nav).</summary>
        public static Vector3 OnNav(Vector3 p)
        {
            var nav = HaloAi.Nav;
            if (nav == null) return p;
            Vector3 h = HaloSpace.ToHalo(p);
            int poly = nav.Locate(h.x, h.y, h.z + 0.1f, 0.5f, 2.5f, 2f);
            if (poly < 0) return p;
            var c = nav.ClosestPoint(poly, h.x, h.y);
            return HaloSpace.Pos(c);
        }

        // ------------------------------------------------------------------ idle states

        void TickIdle(float dt)
        {
            // Halo has no walk back to the spawn: an idle actor keeps to its CURRENT squad's guard firing positions when it has some,
            // else it stays where it is (asleep: always)
            if (FloodIdle()) return;
            bool atGuard = InitialState == HaloAiState.GuardingAtGuardPosition || ReturnState == HaloAiState.GuardingAtGuardPosition;
            uint guard = Enc != null && !Sleeping ? GuardMask() : 0u;
            if (Enc != null && FollowLeader(out var lead))
            {
                FollowIdle(lead);
            }
            else if (guard != 0u)
            {
                HoldOnArrival();
                if (_fpos < 0 || Now >= _fposUntil)
                {
                    int p = PickPosition(guard, null, false);
                    if (p >= 0 && IsBadPosition(p)) p = -1;
                    TakePosition(p, A != null ? A.GuardPositionTime.Pick(HaloAi.Rng) : 5f);
                }
                if (_fpos >= 0) MoveTo(Enc.Positions[_fpos], 0.5f); else if (!Moving) Stop();
            }
            else if (!Moving) Stop();

            IdleLook();
            bool crouch = A != null && (A.Has(HaloActorDef.CrouchWhenNotInCombat) || (A.Has(HaloActorDef.CrouchWhenGuarding) && (InitialState == HaloAiState.Guarding || atGuard))) && !Moving;
            Unit.Crouching = crouch || (V != null && V.MovementType == 1);
        }

        void IdleLook()
        {
            if (LookAtObject != null) { Face(LookAtObject.transform.position + Vector3.up * 1.5f, false); return; }
            if (Now >= _nextIdleLook || _idleLook == Vector3.zero)
            {
                float t = A != null ? A.NoncombatIdleFacing.Pick(HaloAi.Rng) : 4f;
                _nextIdleLook = Now + Mathf.Max(1.5f, t);
                float yaw = HomeYaw + HaloAi.Rand(-50f, 50f);
                if (Moving) yaw = Unit.Yaw;
                _idleLook = Unit.Eye + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 10f;
            }
            Face(Moving ? Unit.Eye + Unit.MoveVelocity.normalized * 10f : _idleLook, false);
        }

        void TickPatrol(float dt)
        {
            var sq = Enc != null && Squad >= 0 && Squad < Enc.Data.Squads.Count ? Enc.Data.Squads[Squad] : null;
            if (sq == null || sq.MovePositions.Count == 0) { SetState(Mode.Idle); return; }
            var mps = sq.MovePositions;
            if (_patrolIndex < 0) { _patrolIndex = NearestMove(mps); _patrolWaitUntil = -1f; }
            var mp = mps[_patrolIndex];
            Vector3 at = HaloSpace.Pos(mp.Position);
            if (_patrolWaitUntil < 0f)
            {
                MoveTo(at, 0.45f, 0.4f);
                if (Arrived) _patrolWaitUntil = Now + Mathf.Max(0.5f, mp.Time.Pick(HaloAi.Rng));
            }
            else
            {
                Face(Unit.Eye + HaloSpace.Yaw(mp.Facing) * Vector3.forward * 10f, false);
                if (Now >= _patrolWaitUntil)
                {
                    _patrolWaitUntil = -1f;
                    _patrolIndex = NextMove(mps, _patrolIndex);
                }
            }
            if (Moving) Face(Unit.Eye + (at - Position).normalized * 10f, false);
            Unit.Crouching = V != null && V.MovementType == 1;
        }

        int NearestMove(List<HaloAiScenario.MovePosition> mps)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < mps.Count; i++) { float d = (HaloSpace.Pos(mps[i].Position) - Position).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        int NextMove(List<HaloAiScenario.MovePosition> mps, int i)
        {
            int n = mps.Count;
            if (n <= 1) return 0;
            switch (InitialState)
            {
                case HaloAiState.MovingRepeatSamePosition: return i;
                case HaloAiState.MovingLoopBackAndForth:
                    if (i + _patrolDir >= n || i + _patrolDir < 0) _patrolDir = -_patrolDir;
                    return i + _patrolDir;
                case HaloAiState.MovingLoopRandomly:
                case HaloAiState.MovingRandomly:
                    {
                        // weighted pick (move position weights)
                        float total = 0f; for (int k = 0; k < n; k++) if (k != i) total += Mathf.Max(0.01f, mps[k].Weight);
                        float r = (float)HaloAi.Rng.NextDouble() * total;
                        for (int k = 0; k < n; k++) { if (k == i) continue; r -= Mathf.Max(0.01f, mps[k].Weight); if (r <= 0f) return k; }
                        return (i + 1) % n;
                    }
                default: return (i + 1) % n;
            }
        }
    }
}

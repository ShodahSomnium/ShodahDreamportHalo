using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Actor melee (workstream 6): Halo's melee charge, the Flood / hunter leap and the contact blow.
    /// - actv "melee range" (berserk: "berserk melee range") is the DECISION distance: inside it the actor commits and runs at the target
    ///   for up to actr "melee charge time", giving up past "melee abort range" (berserk: "berserk melee abort range").
    /// - The blow (Unit.Melee) lands only at contact: flat distance &lt;= unit radius + target radius + actr "melee fudge factor" (wu).
    /// - The leap: rolled once per melee opportunity (grounded, inside actr "melee leap range", melee timer passed) with "melee leap chance";
    ///   one launch at "melee leap velocity" (wu/tick) blended between a straight line and the ballistic arc by "melee leap ballistic".
    /// - Carriers (actr "suicidal melee attack"): the contact blow is the detonation (the unit's "melee attack is fatal" kills it; the
    ///   Characters module then destroys the body, plays the burst and releases the infection forms).
    /// - Next melee: Now + difficulty melee delay base + actr "melee attack delay" x difficulty melee delay scale.
    /// </summary>
    public sealed partial class HaloActor
    {
        float _nextMelee;
        bool _charging; float _chargeUntil, _chargeRepathAt;
        bool _leapRolled, _leaping, _leapHit; float _leapStartedAt, _leapUntil = -1f, _leapStartY, _leapApex;

        const float ContactHeight = 1.5f;              // ours: |feet dy| for a contact blow (m)
        const float PlayerRadiusWu = 0.2f;             // ours: a player's body radius (the cyborg pill, wu)

        static int _leapRollLogs, _leapWaitLogs; float _leapWaitLoggedAt = -10f;
        static int _chargeLogs, _leapLogs, _landLogs, _latchLogs, _fallbackLogs;

        /// <summary>The actor's last leap: highest feet height above the take-off (m), for the lab.</summary>
        public float LastLeapApex { get; private set; } = -1f;

        /// <summary>How a leap ended (for the lab and the log): Hit = it touched its target; Flew = it flew its whole planned flight (the
        /// target may have moved); Wall = a face too steep to stand on stopped it (Halo's leaping forms stop on contact); Short = it came down
        /// on open ground before its planned flight time without touching anything - a fizzle.</summary>
        public enum LeapEnd { None, Hit, Flew, Wall, Short }
        /// <summary>All actors' leaps since start, by how they ended (index = (int)LeapEnd; [0] = launched), for the labs.</summary>
        public static readonly int[] LeapEnds = new int[5];
        public LeapEnd LastLeapEnd { get; private set; }
        public bool MeleeCharging => _charging && Alive;
        public bool LeapInFlight => _leaping;

        float MeleeRangeWu => V == null ? 0f : (Berserking && V.BerserkMeleeRange > 0f ? V.BerserkMeleeRange : V.MeleeRange);

        float MeleeAbortWu
        {
            get
            {
                if (V == null) return 0f;
                float a = Berserking && V.BerserkMeleeAbortRange > 0f ? V.BerserkMeleeAbortRange : V.MeleeAbortRange;
                return Mathf.Max(a, MeleeRangeWu);     // an abort range below the commit range would abort at once
            }
        }

        const float GroundReachM = 0.75f;               // ours: a blow from the ground reaches at most this far past the two bodies (m)
        /// <summary>Arrival tolerance of a charge / swarm run at a seen target (m, ours): <see cref="StandOff"/> stops 0.35 m inside the
        /// contact distance, so the body must arrive within 0.25 m of that stop (the old 0.6 m let infection forms stop 1.9 m from the
        /// player, outside their 1.7 m contact, and never strike).</summary>
        const float MeleeArriveM = 0.25f;

        /// <summary>
        /// Contact distance in wu: our radius + the target's + the actr melee fudge factor. The fudge (1 wu = 3 m for infection forms,
        /// 0.75 wu for combat forms) would let a blow land from 3 m away; contact is also held to the two bodies + <see cref="GroundReachM"/>
        /// (on the ground and in a leap alike), so the body has to reach the target for the blow / latch / carrier burst.
        /// </summary>
        float ContactWu(HaloAiBody t)
        {
            float mine = Unit != null && Unit.Def != null && Unit.Def.Radius > 0f ? Unit.Def.Radius : 0.2f;
            float theirs = t != null && t.Unit != null && t.Unit.Def != null && t.Unit.Def.Radius > 0f ? t.Unit.Def.Radius : PlayerRadiusWu;
            float full = mine + theirs + (A != null ? Mathf.Max(0f, A.MeleeFudgeFactor) : 0.5f);
            return Mathf.Min(full, mine + theirs + GroundReachM / HaloSpace.Unit);
        }

        float MeleeDelay()
        {
            float d = HaloAi.DiffAdd(HaloAi.DiffTable.MeleeDelayBase) + (A != null ? A.MeleeAttackDelay : 1f) * HaloAi.DiffScale(HaloAi.DiffTable.MeleeDelayScale);
            return Mathf.Max(0.5f, d);                 // ours: the 0.5 s floor (a swing animation is longer anyway)
        }

        void EndCharge() { _charging = false; }

        /// <summary>Melee per AI tick: the leap in flight, the contact blow, the leap roll, the charge. True = it drives the movement this tick.</summary>
        bool TickMelee(bool visible)
        {
            if (_leaping) { TickLeapFlight(); return true; }
            if (V == null || A == null || Unit == null || Target == null || !Target.Alive) { EndCharge(); return false; }
            if (MeleeInhibited || Unit.MeleeInhibited || Unit.Seat != null) { EndCharge(); return false; }
            float range = MeleeRangeWu;
            if (range <= 0f && A.MeleeLeapChance <= 0f) { EndCharge(); return false; }
            Vector3 me = Position, tf = Target.Feet;
            float flat = Flat(tf - me) / HaloSpace.Unit;
            float dy = tf.y - me.y;

            // the blow at contact
            // (on the ground: an infection form flung out of a carrier burst cannot strike in mid-air)
            if (Now >= _nextMelee && (visible || _charging) && Unit.Grounded && flat <= ContactWu(Target) && Mathf.Abs(dy) < ContactHeight)
            {
                // (a swing is never held for an ally near the blow: MeleeHit passes allies' hit boxes and strikes the next one in reach)
                Strike(flat);
                return false;
            }
            // the leap (once per opportunity)
            if (TryLeap(flat, dy, visible)) return true;
            if (_charging)
            {
                bool carrier = A.Has(HaloActorDef.SuicidalMeleeAttack);
                if (Now >= _chargeUntil)
                {
                    if (carrier)
                    {
                        // Halo's carrier keeps waddling at its target until it touches it (it never gives the charge up while berserk);
                        // one held off just short of contact (a ledge, a body in the way) goes off there (ours: 1.5x the contact distance)
                        if (flat <= ContactWu(Target) * 1.5f) { Strike(flat); return false; }
                        _chargeUntil = Now + (A.MeleeChargeTime > 0f ? A.MeleeChargeTime : 3f);
                    }
                    else { EndCharge(); _nextMelee = Now + MeleeDelay(); return false; }
                }
                if (flat > MeleeAbortWu) { EndCharge(); return false; }
                ChargeMove(tf, flat);
                return true;
            }
            if (visible && Now >= _nextMelee && range > 0f && flat <= range)
            {
                _charging = true;
                float ct = A.MeleeChargeTime > 0f ? A.MeleeChargeTime : 3f;      // ours: 3 s when the tag has none
                _chargeUntil = Now + ct;
                _chargeRepathAt = 0f;
                if (_chargeLogs++ < 3) HaloLog.Info("AI: melee charge: " + Label + " from " + flat.ToString("0.0") + " wu (melee range " + range.ToString("0.##") + ", abort " + MeleeAbortWu.ToString("0.##") + ", contact " + ContactWu(Target).ToString("0.##") + " wu, charge " + ct.ToString("0.#") + " s)");
                ChargeMove(tf, flat);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Where a melee approach stops: at body contact in front of the target (both radii + 0.4 m, ours), not on its feet. Halo's bodies
        /// collide with the player; ours do not always (the stand-in, a VR player), and a body standing inside its target cannot land the
        /// blow (the blow's sphere starts at the attacker's radius in front of it).
        /// </summary>
        Vector3 StandOff(Vector3 targetFeet)
        {
            Vector3 d = targetFeet - Position; d.y = 0f;
            float m = d.magnitude;
            float mine = Unit != null && Unit.Def != null && Unit.Def.Radius > 0f ? Unit.Def.Radius : 0.2f;
            float theirs = Target != null && Target.Unit != null && Target.Unit.Def != null && Target.Unit.Def.Radius > 0f ? Target.Unit.Def.Radius : PlayerRadiusWu;
            // the stop must lie inside the contact distance (the carrier's fudge is only 0.1 wu: radii + 0.4 m would stop it 0.1 m short
            // of contact for ever), never inside the two bodies
            float bodies = (mine + theirs) * HaloSpace.Unit;
            float stop = Mathf.Max(bodies, Mathf.Min(bodies + 0.4f, ContactWu(Target) * HaloSpace.Unit - 0.35f));
            if (m <= stop || m < 1e-3f) return Position;
            return targetFeet - d / m * stop;
        }

        /// <summary>Runs at the target (inside the squad's area), re-pathing at most every 0.5 s; straight at it inside 3 m.</summary>
        void ChargeMove(Vector3 targetFeet, float flatWu)
        {
            Vector3 goal = ClampToArea(StandOff(targetFeet));
            bool close = flatWu * HaloSpace.Unit < 3f;           // ours
            if (close || !Moving || Now >= _chargeRepathAt)
            {
                _chargeRepathAt = Now + 0.5f;                     // ours: the re-path limit
                MoveTo(goal, 1f, 0.2f);
            }
        }

        void Strike(float flatWu)
        {
            bool carrier = A != null && A.Has(HaloActorDef.SuicidalMeleeAttack);
            if (carrier) HaloLog.Info("AI: " + Label + " goes off at " + (flatWu * HaloSpace.Unit).ToString("0.0") + " m from " + Target + " (suicidal melee)");
            EndCharge();
            _leapRolled = false;
            _nextMelee = Now + MeleeDelay();
            ImpactContact(Target != null ? Target.Unit : null);
        }

        /// <summary>The leap roll: grounded, inside the leap range, the melee timer passed; once per opportunity.</summary>
        bool TryLeap(float flatWu, float dy, bool visible)
        {
            if (A.MeleeLeapChance <= 0f || A.MeleeLeapRange.Max <= 0f) return false;
            float lo = Mathf.Min(A.MeleeLeapRange.Min, A.MeleeLeapRange.Max), hi = Mathf.Max(A.MeleeLeapRange.Min, A.MeleeLeapRange.Max);
            if (flatWu < lo || flatWu > hi) { _leapRolled = false; return false; }
            if (!visible || Now < _nextMelee || !Unit.Grounded || Unit.Leaping || _leapRolled || Flying)
            {
                if (!_leapRolled && _leapWaitLogs < 6 && Now - _leapWaitLoggedAt > 1f)
                {
                    _leapWaitLogs++; _leapWaitLoggedAt = Now;
                    HaloLog.Info("AI: leap wait: " + Label + " " + flatWu.ToString("0.0") + " wu:" + (visible ? "" : " not visible") + (Now < _nextMelee ? " melee timer " + (_nextMelee - Now).ToString("0.0") + " s" : "") + (Unit.Grounded ? "" : " airborne") + (Unit.Leaping ? " leaping" : ""));
                }
                return false;
            }
            _leapRolled = true;
            bool go = HaloAi.Chance(A.MeleeLeapChance);
            if (_leapRollLogs++ < 6) HaloLog.Info("AI: leap roll: " + Label + " at " + flatWu.ToString("0.0") + " wu, chance " + A.MeleeLeapChance.ToString("0.##") + " -> " + (go ? "leap" : "no"));
            if (!go) return false;
            Launch(flatWu * HaloSpace.Unit, dy);
            return true;
        }

        float _leapFlightT, _leapLift; bool _leapLifted; Vector3 _leapDir; float _leapSpeed;
        float _leapPrevY, _leapClimbV; bool _leapWall; int _leapClimbs;
        const float LeapSlideNy = 0.7f;                     // the mover's leaping roll-over limit (HaloUnit.RollsOver: a leap rolls over faces with normal y >= 0.7)
        const float LeapLiftSpare = 0.25f;                  // ours: how long past the planned flight time the straight line is still held (s)
        const int LeapLookAheadTicks = 5;                   // ours: AI ticks of flight checked ahead for the floor / a wall (5 x 1/30 s)
        static int _leapBlockLogs, _shortLogs;

        /// <summary>
        /// One launch at the target's body (its centre: Halo's leap goes at the target, not at its feet - an infection form lands on the
        /// player's chest / shield): horizontal speed = melee leap velocity (wu/tick) x 30 x 3.048, flight time t = distance / speed.
        /// "melee leap ballistic" is how ballistic the flight is: 0 = the straight line at the target at that speed (the infection form's
        /// flat, fast leap at the body), 1 = a free ballistic arc that lands on the target (vertical speed dy/t + g t/2), in between the
        /// arc under that share of gravity (combat forms 0.75). The unflown share of gravity is held off for the flight time by a lift each
        /// AI tick (<see cref="TickLeapFlight"/>); the launch is the one that reaches the target's centre at time t on that trajectory.
        /// Rise above the straight line at most 1.5 m (ours).
        /// </summary>
        void Launch(float flatM, float dy)
        {
            float v = Mathf.Max(1f, A.MeleeLeapVelocity * 30f * HaloSpace.Unit);
            Vector3 d = Target.Feet - Position; d.y = 0f;
            Vector3 dir = d.sqrMagnitude > 1e-4f ? d.normalized : Unit.transform.forward;
            float t = Mathf.Max(0.1f, flatM / v);
            float g = HaloUnit.Gravity;
            float b = Mathf.Clamp01(A.MeleeLeapBallistic);
            // centre to centre (the target's body, our body): the straight line's rise over the flight
            float dyc = Target.Center.y - Unit.Center.y;
            if (Mathf.Abs(dyc) > 3f) dyc = dy;                      // (a stale / odd centre: back to the feet)
            float arc = b * 0.5f * g * t;
            float maxArc = b > 0f ? Mathf.Sqrt(2f * b * g * 1.5f) : 0f;      // ours: never more than 1.5 m above the straight line
            float vy = dyc / t + Mathf.Min(arc, maxArc);
            Vector3 vel = dir * v + Vector3.up * vy;
            Stop();
            Push(Vector3.zero, t + 0.3f);                          // the path does not steer a leaping body
            Unit.LookPoint = Target.Head; Unit.AimPoint = Vector3.zero;
            Unit.Leap(vel);
            _leaping = Unit.Leaping; _leapHit = false;
            if (!_leaping) return;                                 // (the unit cannot leap now: seated, suspended)
            _leapStartedAt = Now; _leapUntil = Now + t + 0.8f;     // ours: the landing allowance
            _leapStartY = Position.y; _leapApex = 0f;
            _leapFlightT = t; _leapLift = (1f - b) * g; _leapLifted = _leapLift > 0.01f;
            _leapPrevY = Position.y; _leapWall = false; _leapClimbs = 0; _leapClimbV = 0f; LastLeapEnd = LeapEnd.None; LeapEnds[0]++;
            _leapDir = (dir * v + Vector3.up * (dyc / t)).normalized; _leapSpeed = new Vector2(v, dyc / t).magnitude;
            _nextMelee = Now + MeleeDelay();
            EndCharge();
            HaloAiVoice.Say(this, HaloAiVoice.LeapAttack);
            if (_leapLogs++ < 6) HaloLog.Info("AI: leap: " + Label + " at " + Target + " " + flatM.ToString("0.0") + " m away, dy " + dy.ToString("0.0") + " m (centres " + dyc.ToString("0.0") + " m): " + v.ToString("0.0") + " m/s, up " + vy.ToString("0.0") + " m/s, flight " + t.ToString("0.00") + " s, gravity share " + b.ToString("0.##") + " (actr leap velocity " + A.MeleeLeapVelocity.ToString("0.###") + " wu/tick, ballistic " + A.MeleeLeapBallistic.ToString("0.##") + ")");
        }

        /// <summary>
        /// The straight-line share of the leap: lifts the unit against the unflown share of gravity for the flight time. The next ticks of
        /// the flight are swept ahead: ground the body can stand on while leaping (the mover's own leaping roll-over limit, normal y 0.7)
        /// is slid along - the body gains the vertical speed that runs along that face, as a colliding body slides along a slope instead of
        /// stopping (round 3: a 5 cm rise of the floor ahead ended free#6's leap after 0.03 s, a 0.17 s hop that missed); a steeper face
        /// (a wall) ends the lift and the mover stops the leap on contact, as Halo's leaping forms stop on contact.
        /// </summary>
        void LeapLift()
        {
            float vyNow = (Position.y - _leapPrevY) / HaloAi.Dt; _leapPrevY = Position.y;
            if (!_leapLifted) return;
            if (_leapHit || !Unit.Leaping || !Unit.Alive || Now - _leapStartedAt > _leapFlightT + LeapLiftSpare) { _leapLifted = false; return; }
            float r = Unit.Def != null && Unit.Def.Radius > 0f ? Mathf.Clamp(Unit.Def.Radius * HaloSpace.Unit * 0.9f, 0.08f, 0.6f) : 0.3f;
            Vector3 c = Position + Vector3.up * (r + 0.05f);
            Vector3 hv = Unit.Velocity; hv.y = 0f;
            if (hv.sqrMagnitude < 1e-4f) hv = new Vector3(_leapDir.x, 0f, _leapDir.z) * _leapSpeed;
            Vector3 vel = hv + Vector3.up * vyNow;
            float speed = vel.magnitude;
            RaycastHit h;
            if (speed > 0.1f && Physics.SphereCast(c, r, vel / speed, out h, speed * HaloAi.Dt * LeapLookAheadTicks, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                Vector3 n = h.normal, nf = n; nf.y = 0f;
                float need = n.y > 1e-3f ? -Vector3.Dot(hv, nf) / n.y : float.MaxValue;     // the vertical speed that runs along the face
                if (n.y >= LeapSlideNy && need > 0f)
                {
                    if (need > vyNow)
                    {
                        Unit.AddImpulse(Vector3.up * (need - vyNow));
                        _leapClimbV += need - vyNow;
                        if (_leapClimbs++ == 0 && _leapBlockLogs++ < 6) HaloLog.Info("AI: leap: " + Label + " slides up a rise " + h.distance.ToString("0.00") + " m ahead after " + (Now - _leapStartedAt).ToString("0.00") + " s (face normal y " + n.y.ToString("0.00") + ", +" + (need - vyNow).ToString("0.0") + " m/s up)");
                    }
                }
                else
                {
                    _leapLifted = false;
                    if (n.y < LeapSlideNy) _leapWall = true;
                    if (_leapBlockLogs++ < 6) HaloLog.Info("AI: leap: " + Label + " " + (n.y < LeapSlideNy ? "wall" : "the floor") + " " + h.distance.ToString("0.00") + " m ahead after " + (Now - _leapStartedAt).ToString("0.00") + " s (face normal y " + n.y.ToString("0.00") + "): falls from here");
                    return;
                }
            }
            else if (_leapClimbV > 0f)
            {
                // past the rise: the slide-up speed is given back once the flight without it is clear again (round 4: kept, it carried a
                // 0.9 m/s slide over a 10 cm rise to a 1.8 m apex), so the leap goes on parallel to its line instead of climbing away from it
                Vector3 baseVel = hv + Vector3.up * (vyNow - _leapClimbV);
                float bs = baseVel.magnitude;
                if (bs > 0.1f && !Physics.SphereCast(c, r, baseVel / bs, out h, bs * HaloAi.Dt * LeapLookAheadTicks, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore))
                {
                    Unit.AddImpulse(Vector3.down * _leapClimbV);
                    _leapClimbV = 0f;
                }
            }
            Unit.AddImpulse(Vector3.up * (_leapLift * HaloAi.Dt));
        }

        void TickLeapFlight()
        {
            if (Unit == null) { _leaping = false; return; }
            float up = Position.y - _leapStartY;
            if (up > _leapApex) _leapApex = up;
            LastLeapApex = _leapApex;                              // (also for a leap that ends in a pop before it lands)
            if (!_leapHit && Target != null && Target.Alive && Unit.Alive)
            {
                float flat = Flat(Target.Feet - Position) / HaloSpace.Unit;
                if (flat <= ContactWu(Target) && Mathf.Abs(Target.Feet.y - Position.y) < ContactHeight)
                {
                    _leapHit = true;
                    LastLeapEnd = LeapEnd.Hit; LeapEnds[(int)LeapEnd.Hit]++;
                    ImpactContact(Target.Unit);
                }
            }
            if (Unit != null && Unit.Alive) LeapLift();
            if (!_leapHit && !Unit.Leaping && !Unit.Grounded) _leapWall = true;      // the mover ended the leap in the air: contact with a wall / ceiling / steep face
            bool landed = Now - _leapStartedAt > 0.15f && !Unit.Leaping && Unit.Grounded;
            if (landed || Now > _leapUntil || !Unit.Alive)
            {
                _leaping = false;
                _leapLifted = false;
                LastLeapApex = _leapApex;
                _overrideUntil = -1f;
                float flown = Now - _leapStartedAt;
                if (!_leapHit && Unit.Alive)          // (a form killed in the air by something else is no leap result)
                {
                    // (one AI tick of slack: the flight is measured on AI ticks)
                    LastLeapEnd = flown >= _leapFlightT - HaloAi.Dt ? LeapEnd.Flew : _leapWall ? LeapEnd.Wall : LeapEnd.Short;
                    LeapEnds[(int)LastLeapEnd]++;
                }
                else if (!_leapHit && _shortLogs++ < 6) HaloLog.Info("AI: leap ended: " + Label + " died in the air " + flown.ToString("0.00") + " s into a " + _leapFlightT.ToString("0.00") + " s leap, " + (Target != null ? Flat(Target.Feet - Position).ToString("0.0") + " m from its target" : "no target") + " (not a leap result)");
                // a leap that did not touch its target was no attack: the contact blow may follow at once (the leap is not rolled
                // again before a blow lands or the target leaves the leap range)
                if (!_leapHit && Unit.Alive) _nextMelee = Mathf.Min(_nextMelee, Now);
                if (_landLogs++ < 6 || (LastLeapEnd == LeapEnd.Short && _shortLogs++ < 6)) HaloLog.Info("AI: leap landed: " + Label + " apex " + _leapApex.ToString("0.00") + " m above the take-off, " + (Now - _leapStartedAt).ToString("0.00") + " s of " + _leapFlightT.ToString("0.00") + " planned, " + (_leapHit ? "hit" : "missed (" + LastLeapEnd + ")"));
            }
        }

        /// <summary>Melee / leap contact with a target: infection forms try to latch (HaloUnit.TryLatch: it may also pop them on shields),
        /// combat forms and everything else swing (Unit.Melee). <paramref name="target"/> null = a player (the blow goes through MeleeHit).</summary>
        public void ImpactContact(HaloUnit target)
        {
            if (Unit == null || !Unit.Alive) return;
            bool infection = (A != null && A.Type == HaloActorType.InfectionForm) ||
                (Unit.Def != null && (Unit.Def.ImpactMeleeAttachesToUnit || Unit.Def.ImpactMeleeDiesOnShields));
            if (infection)
            {
                // an infection form's contact is its whole attack: it touches ITS target only (never the generic blow through MeleeHit,
                // which strikes the first hit box in front - a swarm-mate - and its pop chain-kills the swarm)
                if (target != null)
                {
                    // a unit: pops on a "shields fry infection forms" unit's raised shield, else one strike (the unit's melee jpt) and the
                    // latch where the target can be attached (TryLatch)
                    if (Unit.TryLatch(target) && _latchLogs++ < 3) HaloLog.Info("AI: " + Label + " latched onto " + target.name);
                }
                else if (Target != null && Target.Kind == HaloAiBody.Kinds.LocalPlayer)
                {
                    // the local player: TryLatch(null) pops the form on the cyborg's raised shield, else strikes once (the player cannot be
                    // attached)
                    Unit.TryLatch(null);
                }
                else if (Target != null && Target.IsPlayer) PlayerContact(Target);       // co-op remote players (and the lab stand-in)
                HaloAi.NoteMelee(this);
                return;
            }
            Unit.Melee = true;
            HaloAiVoice.Say(this, HaloAiVoice.MeleeAttack, 0.5f);
            HaloAi.NoteMelee(this);
            // a carrier whose unit lacks "melee attack is fatal" (should not happen with the retail tags): it still goes off
            if (A != null && A.Has(HaloActorDef.SuicidalMeleeAttack) && Unit.Def != null && !Unit.Def.MeleeIsFatal)
            {
                if (_fallbackLogs++ < 1) HaloLog.Info("AI: " + Label + " suicidal melee without the unit flag 'melee attack is fatal': killed instead");
                Unit.Kill();
            }
        }

        static int _playerContactLogs;
        static readonly Collider[] ContactOverlap = new Collider[24];

        /// <summary>
        /// An infection form touching a player that is not the host's own (a co-op remote player, the lab stand-in), by the same rules as
        /// <see cref="HaloUnit.TryLatch"/> gives the local player: on a raised shield of the player cyborg ("shields fry infection forms")
        /// a form that "impact melee dies on shields" pops; otherwise its melee damage strikes that player's body once (their hit box: the
        /// hit is published for their own client) - players cannot be attached.
        /// </summary>
        void PlayerContact(HaloAiBody t)
        {
            var cat = HaloCombat.Catalog;
            if (cat == null || Unit.Def == null || !HaloAllegiance.AreEnemies(Unit.Team, t.Team)) return;
            HaloCharacterDef pd = HaloBipeds.Def(cat.PlayerUnit);
            bool shieldUp = t.Kind == HaloAiBody.Kinds.RemotePlayer && t.Remote != null && t.Remote.Shield > 0.01f;
            if (shieldUp && pd != null && pd.ShieldsFryInfectionForms && Unit.Def.ImpactMeleeDiesOnShields)
            {
                if (_playerContactLogs++ < 6) HaloLog.Info("AI: " + Label + " popped on " + t + "'s shield");
                Unit.TakeHit(new HaloHit { SourcePlayer = -1, Instant = true, Point = Unit.Center, Direction = -Unit.transform.forward });
                return;
            }
            HaloTag jpt = Unit.Def.MeleeDamage;
            HaloDamageDef dd = jpt != null ? cat.Damage(jpt) : null;
            if (dd == null) return;
            // that player's own hit box nearest its body centre (never another body's)
            int n = Physics.OverlapSphereNonAlloc(t.Center, 0.9f, ContactOverlap, 1 << HaloLayers.Hitbox, QueryTriggerInteraction.Collide);
            IHaloDamageable best = null; Vector3 at = t.Center; float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Collider col = ContactOverlap[i];
                if (col == null) continue;
                IHaloDamageable v = col.GetComponentInParent<IHaloDamageable>();
                if (v == null || v is HaloUnit) continue;
                if (t.Kind == HaloAiBody.Kinds.RemotePlayer)
                {
                    var hb = v as HaloNetHitbox;
                    if (hb == null || hb.Player != t.Remote) continue;
                }
                Vector3 p = col.ClosestPoint(Unit.Center);
                float d = (p - t.Center).sqrMagnitude;
                if (d < bd) { bd = d; best = v; at = p; }
            }
            if (best == null)
            {
                if (_playerContactLogs++ < 6) HaloLog.Info("AI: " + Label + " touched " + t + " but found no hit box of theirs to strike");
                return;
            }
            float amount = dd.DirectAmount(HaloAi.Rng, 0f, 0f, 0f);
            if (_playerContactLogs++ < 6) HaloLog.Info("AI: " + Label + " strikes " + t + " (" + amount.ToString("0.#") + ", " + HaloAi.Short(jpt) + ")");
            HaloProjectiles.Deliver(best, new HaloHit { Amount = amount, Effect = jpt, SourcePlayer = HaloUnit.AiShooter, Cause = jpt, Point = at, Direction = (at - Unit.Center).normalized, Melee = true });
        }
    }
}

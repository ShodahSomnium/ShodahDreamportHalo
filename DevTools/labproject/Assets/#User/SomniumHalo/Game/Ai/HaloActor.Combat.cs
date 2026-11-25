using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Actor combat (workstream 6): firing positions confined to the squad's groups, group choice from what the actor knows of its target,
    /// hold timers from arrival, pursuit and search by positions, firing patterns (actv burst / new-target / moving / berserk multipliers),
    /// aim with tracking and Halo's burst geometry, surprise, friends' line of fire, retreat, hiding, berserk, crouching and movement types,
    /// flying, following and the Flood's feeding. The melee charge and the leap are in HaloActor.Melee.cs.
    /// </summary>
    public sealed partial class HaloActor
    {
        // firing positions
        int _fpos = -1; float _fposUntil; int _oldFpos = -1;
        float _fposHold; bool _fposArrived; uint _fposMask; float _fposPickedAt;
        bool _wantCover;
        float _outOfRangeSince = -1f;
        // bursts and aim
        bool _bursting; float _burstUntil, _nextBurst, _burstStart, _burstLen; Vector3 _burstOrigin;
        float _burstReturnLen, _burstReturnRot; Vector3 _burstLof;
        Vector3 _aimPoint; int _aimTick = -100;
        float _fireWildlyUntil = -1f; bool _surpriseDelayPending;
        float _lofBlockedSince = -1f, _nextLofStep;
        // grenades
        float _nextGrenadeCheck, _grenadeAimUntil = -1f; Vector3 _grenadeAim;
        // crouch / evade / movement types
        bool _crouch; float _crouchChangeAt; float _lofCrouchUntil = -1f;
        bool _movePhaseCrouch; float _movePhaseUntil = -1f;
        bool _holdCrouch;
        float _nextEvade, _evadeUntil = -1f; Vector3 _evadeDir;
        // search
        Vector3 _searchPoint; float _nextSearchPoint; int _searchVisited, _searchMax; float _searchHoldUntil = -1f, _searchInit = -1f;
        // hide / retreat / berserk
        float _hideInit = -1f, _hideEmergeAt = -1f;
        float _retreatUntil = -1f, _retreatAgainAt;
        float _berserkLostUntil = -1f;
        float _chaseRepathAt;
        float _posInit = -1f, _heldInit = -1f;
        // flying
        float _nextSidestep; Vector3 _flyOffset;
        // gestures
        HaloAiBody _signalledFor, _lastFoe;
        float _nextFeed, _nextCorpseLook; HaloUnit _corpse;
        // the squad's area (bounds of its firing positions)
        HaloAiEncounter _boundsEnc; int _boundsSquad = -2; bool _hasBounds; Vector3 _bMin, _bMax;

        static readonly Vector3[] FriendPts = new Vector3[64];
        static uint _logged;
        /// <summary>One-time facts for Player.log (bit per kind).</summary>
        static bool Once(int bit) { uint m = 1u << bit; if ((_logged & m) != 0) return false; _logged |= m; return true; }

        bool Attacking => Enc == null || !Enc.IsPlatoonDefending(Platoon);

        HaloAiScenario.Squad SquadData => Enc != null && Squad >= 0 && Squad < Enc.Data.Squads.Count ? Enc.Data.Squads[Squad] : null;

        /// <summary>The actor is held to its encounter's firing positions (Halo's area confinement): not free, the encounter has positions.</summary>
        bool Confined => !Free && Enc != null && Enc.Positions.Length > 0;

        uint GuardMask()
        {
            var sq = SquadData; if (sq == null) return 0;
            return Attacking ? sq.AttackingGuard : sq.DefendingGuard;
        }

        uint CombatMask(bool known)
        {
            var sq = SquadData; if (sq == null) return 0;
            if (Attacking) return known ? sq.Attacking : sq.AttackingSearch;
            return known ? sq.Defending : sq.DefendingSearch;
        }

        uint SquadUnionMask()
        {
            var sq = SquadData; if (sq == null) return 0;
            return sq.Attacking | sq.AttackingSearch | sq.AttackingGuard | sq.Defending | sq.DefendingSearch | sq.DefendingGuard | sq.Pursuing;
        }

        void OnNewTarget()
        {
            float d = V != null ? V.FirstBurstDelayTime.Pick(HaloAi.Rng) : 0.5f;
            _nextBurst = Mathf.Max(_nextBurst, Now + d * HaloAi.DiffScale(HaloAi.DiffTable.NewTargetDelay));
            _bursting = false;
        }

        // ------------------------------------------------------------------ the squad's area

        void EnsureBounds()
        {
            if (_boundsEnc == Enc && _boundsSquad == Squad) return;
            _boundsEnc = Enc; _boundsSquad = Squad; _hasBounds = false;
            if (Enc == null) return;
            uint mask = SquadUnionMask();
            var pos = Enc.Positions;
            for (int pass = 0; pass < 2 && !_hasBounds; pass++)
                for (int i = 0; i < pos.Length; i++)
                {
                    if (pass == 0 && (mask == 0 || !Enc.PositionInGroups(i, mask))) continue;
                    if (!_hasBounds) { _bMin = _bMax = pos[i]; _hasBounds = true; }
                    else { _bMin = Vector3.Min(_bMin, pos[i]); _bMax = Vector3.Max(_bMax, pos[i]); }
                }
        }

        /// <summary>A charge goal kept inside the squad's area (the bounds of its firing positions), snapped to the navigation.</summary>
        Vector3 ClampToArea(Vector3 p)
        {
            if (!Confined) return p;
            EnsureBounds();
            if (!_hasBounds) return p;
            float x = Mathf.Clamp(p.x, _bMin.x, _bMax.x), z = Mathf.Clamp(p.z, _bMin.z, _bMax.z);
            if (x == p.x && z == p.z) return p;
            return OnNav(new Vector3(x, p.y, z));
        }

        /// <summary>The target's last known spot lies outside the squad's area (+2 wu, ours).</summary>
        bool TargetOutsideArea()
        {
            if (!Confined || Target == null) return false;
            EnsureBounds();
            if (!_hasBounds) return false;
            float m = 2f * HaloSpace.Unit;
            Vector3 t = TargetLastSeen;
            return t.x < _bMin.x - m || t.x > _bMax.x + m || t.z < _bMin.z - m || t.z > _bMax.z + m;
        }

        // ------------------------------------------------------------------ positions

        /// <summary>Claims a firing position. The hold time (actr guard / combat / pursuit position time) starts on arrival; until then a
        /// travel allowance (the walk at half run speed + 3 s, ours) so a position never reached is given up.</summary>
        void TakePosition(int index, float seconds)
        {
            if (index != _fpos) { ReleasePosition(); if (Enc != null) Enc.Claim(index); }
            _fpos = index;
            _fposHold = Mathf.Max(1f, seconds);
            _fposArrived = false;
            _fposPickedAt = Now;
            if (index < 0 || Enc == null || index >= Enc.Positions.Length) { _fposUntil = Now + _fposHold; return; }
            float d = (Enc.Positions[index] - Position).magnitude;
            _fposUntil = Now + d / Mathf.Max(1f, RunSpeed() * 0.5f) + 3f + _fposHold;
        }

        void ReleasePosition()
        {
            if (_fpos >= 0 && Enc != null) Enc.Release(_fpos);
            if (_fpos >= 0) _oldFpos = _fpos;
            _fpos = -1;
            _fposArrived = false;
        }

        /// <summary>Starts the hold timer the first tick the actor stands at its position (within 1 m, ours).</summary>
        void HoldOnArrival()
        {
            if (_fpos < 0 || _fposArrived || Enc == null || _fpos >= Enc.Positions.Length) return;
            Vector3 d = Enc.Positions[_fpos] - Position;
            if (Mathf.Abs(d.y) > 2f) return;
            d.y = 0f;
            if (d.sqrMagnitude > 1f) return;
            _fposArrived = true;
            _fposUntil = Now + _fposHold;
        }

        /// <summary>
        /// The best firing position of the encounter within the group mask: in the actv's desired combat range of the target, near us,
        /// not held by another actor, away from the one we just left (actr old position avoid dist) and from friends' claimed / current
        /// spots (actr friend avoid dist), close to the leader when following; then a sight-line check on the winner (attacking wants to
        /// see the target, cover wants not to). -1 = none in the mask.
        /// </summary>
        int PickPosition(uint mask, HaloAiBody target, bool cover)
        {
            if (Enc == null || mask == 0) return -1;
            Vector3 me = Position;
            float rMin = V != null ? V.DesiredCombatRange.Min * HaloSpace.Unit : 6f, rMax = V != null ? V.DesiredCombatRange.Max * HaloSpace.Unit : 18f;
            if (rMax < rMin) rMax = rMin;
            float avoid = A != null ? A.OldPositionAvoidDist * HaloSpace.Unit : 0f;
            float fav = A != null ? A.FriendAvoidDist * HaloSpace.Unit : 0f;
            int nf = fav > 0f ? CollectFriendSpots(me, 40f) : 0;     // ours: friends within 40 m matter
            Vector3 tp = target != null ? TargetLastSeen : Vector3.zero;
            Vector3 lead = Vector3.zero; bool following = FollowLeader(out var lb);
            if (following) lead = lb.Feet;
            int best = -1, second = -1; float bs = float.MinValue, ss = float.MinValue;
            var pos = Enc.Positions;
            int myComp = HaloAiEncounter.ComponentAt(me);
            for (int i = 0; i < pos.Length; i++)
            {
                if (!Enc.PositionInGroups(i, mask)) continue;
                // only positions we can walk to (same navigation island), not one we just gave up reaching
                if (myComp >= 0 && !Flying) { int c = Enc.PositionComponent(i); if (c >= 0 && c != myComp) continue; }
                if (IsBadPosition(i)) continue;
                Vector3 p = pos[i];
                float s = 0f;
                if (target != null)
                {
                    float d = (p - tp).magnitude;
                    if (cover) s += Mathf.Min(d, rMax * 1.5f) * 0.15f;
                    else s -= Mathf.Max(0f, rMin - d) * 0.6f + Mathf.Max(0f, d - rMax) * 0.4f;
                }
                s -= (p - me).magnitude * 0.12f;
                int claims = Enc.Claims[i] - (i == _fpos ? 1 : 0);
                s -= claims * 12f;
                if (i == _fpos) s += 1.5f;
                if (i == _oldFpos || (avoid > 0f && _oldFpos >= 0 && _oldFpos < pos.Length && (p - pos[_oldFpos]).sqrMagnitude < avoid * avoid)) s -= 3f;
                for (int k = 0; k < nf; k++) if ((p - FriendPts[k]).sqrMagnitude < fav * fav) { s -= 6f; break; }     // ours: the weight
                if (following) s -= Mathf.Max(0f, Flat(p - lead) - Enc.FollowDistance * HaloSpace.Unit * 2f) * 1.5f;
                s += (float)HaloAi.Rng.NextDouble() * 1.2f;
                if (s > bs) { ss = bs; second = best; bs = s; best = i; }
                else if (s > ss) { ss = s; second = i; }
            }
            if (best >= 0 && target != null && HaloAiBodies.BudgetLeft)
            {
                Vector3 eye = pos[best] + Vector3.up * 1.5f;
                bool see = HaloAiBodies.Clear(eye, target.Head, !cover);
                if (see == cover && second >= 0) best = second;     // the runner-up (unchecked) is often as good
            }
            return best;
        }

        /// <summary>Living friends' current spots and claimed firing positions near us (into FriendPts).</summary>
        int CollectFriendSpots(Vector3 me, float within)
        {
            int n = 0;
            var list = HaloAi.Actors;
            float w2 = within * within;
            for (int i = 0; i < list.Count && n < FriendPts.Length - 1; i++)
            {
                var o = list[i];
                if (o == this || !o.Alive || !HaloAllegiance.AreFriends(Team, o.Team)) continue;
                Vector3 op = o.Position;
                if ((op - me).sqrMagnitude > w2) continue;
                FriendPts[n++] = op;
                if (o._fpos >= 0 && o.Enc != null && o._fpos < o.Enc.Positions.Length) FriendPts[n++] = o.Enc.Positions[o._fpos];
            }
            return n;
        }

        /// <summary>The position in the mask nearest to a point (our navigation island), -1 none.</summary>
        int NearestPositionTo(Vector3 to, uint mask)
        {
            if (Enc == null || mask == 0) return -1;
            int myComp = HaloAiEncounter.ComponentAt(Position);
            int best = -1; float bd = float.MaxValue;
            var pos = Enc.Positions;
            for (int i = 0; i < pos.Length; i++)
            {
                if (!Enc.PositionInGroups(i, mask)) continue;
                if (myComp >= 0 && !Flying) { int c = Enc.PositionComponent(i); if (c >= 0 && c != myComp) continue; }
                if (IsBadPosition(i)) continue;
                float d = (pos[i] - to).sqrMagnitude + (Enc.Claims[i] - (i == _fpos ? 1 : 0)) * 9f;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        /// <summary>No firing positions (free actors, encounters without positions): a point at the desired range on the line to the target.</summary>
        Vector3 RangePoint(HaloAiBody target)
        {
            Vector3 me = Position, tp = TargetLastSeen;
            float rMin = V != null ? V.DesiredCombatRange.Min * HaloSpace.Unit : 6f, rMax = V != null ? V.DesiredCombatRange.Max * HaloSpace.Unit : 18f;
            Vector3 d = me - tp; d.y = 0f;
            float dist = d.magnitude;
            if (dist >= rMin && dist <= Mathf.Max(rMin, rMax)) return me;
            if (dist < 0.1f) d = Unit.transform.forward * -1f; else d /= dist;
            float want = Mathf.Clamp(dist, rMin, Mathf.Max(rMin, rMax));
            return OnNav(tp + d * want);
        }

        /// <summary>Re-paths toward a moving goal at most every 0.5 s (ours), at once when idle or when the goal jumped 3 m.</summary>
        void Chase(Vector3 goal, float speed, float accept)
        {
            if (!Moving || Now >= _chaseRepathAt || (goal - _goal).sqrMagnitude > 9f)
            {
                _chaseRepathAt = Now + 0.5f;
                MoveTo(goal, speed, accept);
            }
        }

        // ------------------------------------------------------------------ combat

        void TickCombat(float dt)
        {
            if (Target == null) { Celebrate(); ReturnToRest(); return; }
            _lastFoe = Target;
            bool visible = TargetVisible;
            if (!Released) { TickHeld(dt, visible); return; }
            ConsumeSurprise(visible);
            SignalAttack();
            Vector3 me = Position;
            float distWu = Flat(Target.Feet - me) / HaloSpace.Unit;
            var know = Know;
            _holdCrouch = false;

            // lost: pursue / search (by positions); shielded actors only with shields at the actr pursue fraction
            if (know == Knowledge.Lost && !Berserking)
            {
                var sq = SquadData;
                bool never = (Enc != null && Enc.Data.SearchBehavior == 1) || (sq != null && sq.Has(HaloAiScenario.SqNeverSearch));
                bool shieldsOk = Unit.MaxShield <= 0f || A == null || A.PursueShieldFraction <= 0f || Unit.Shield >= A.PursueShieldFraction;
                if (never) { if (Now - TargetSeenAt > 20f) { ReturnToRest(); return; } }
                else if (shieldsOk) { SetState(Mode.Search, SearchTime()); return; }
            }

            // low shields: hide (elites); jackals ("hide behind shield") stand behind the shield and relocate only when it popped
            if (A != null && A.HideShieldFraction > 0f && Unit.MaxShield > 0f && !Berserking && Now - _damagedAt < 3f)
            {
                if (A.DefensiveCrouchType == 3)
                {
                    if (Unit.Shield <= 0f) { SetState(Mode.Hide, HideTime()); return; }
                    if (Unit.Shield < A.HideShieldFraction) _holdCrouch = true;
                }
                else if (Unit.Shield < A.HideShieldFraction) { SetState(Mode.Hide, HideTime()); return; }
            }

            // danger triggers (workstream 5): retreat to defending cover for the actr retreat time, then fight again
            if (WantsRetreat && !Berserking && _retreatUntil < Now && Now >= _retreatAgainAt && A != null)
            {
                float rt = A.RetreatTime.Max > 0f ? A.RetreatTime.Pick(HaloAi.Rng) : 4f;     // ours: 4 s without a tag value
                _retreatUntil = Now + rt; _retreatAgainAt = _retreatUntil + 3f;               // ours: 3 s before another retreat
                ReleasePosition(); _fposUntil = 0f;
                HaloAiVoice.Say(this, HaloAiVoice.Cover, 0.6f);
                if (Once(0)) HaloLog.Info("AI: retreat: " + Label + " for " + rt.ToString("0.0") + " s (actr retreat time " + A.RetreatTime + ")");
            }
            bool retreating = _retreatUntil > Now;

            // berserk triggers
            if (A != null && !Berserking && !retreating)
            {
                if (Attacking && A.Has(HaloActorDef.AlwaysBerserkInAttackingMode) && visible) { GoBerserk("attacking"); return; }
                if (A.BerserkProximity > 0f && visible && distWu < A.BerserkProximity) { GoBerserk("proximity"); return; }
                // carriers: the suicide sensing distance starts the run; the burst comes at contact (HaloActor.Melee)
                if (A.Has(HaloActorDef.SuicidalMeleeAttack) && A.SuicideSensingDist > 0f && distWu < A.SuicideSensingDist) { GoBerserk("suicide run"); return; }
            }

            bool charge = AllowCharge && (Enc == null || Enc.AllowCharge) && A != null && !retreating &&
                (A.Has(HaloActorDef.AlwaysChargeAtEnemies) || (Attacking && A.Has(HaloActorDef.AlwaysChargeInAttackingMode)) || A.IsSwarm);
            if (V != null && V.Has(HaloActorVariantDef.CannotUseRangedWeapons) && !retreating) charge = true;

            bool meleeMoves = (_leaping || !retreating) && TickMelee(visible);
            if (meleeMoves) { /* the charge / leap drives the body */ }
            else if (_evadeUntil > Now) { /* evading: Steer is overridden */ }
            else if (Flying) FlyCombat(visible);
            else if (charge) Chase(ClampToArea(visible ? StandOff(Target.Feet) : TargetLastSeen), 1f, visible ? MeleeArriveM : 0.6f);
            else if (_holdCrouch) Stop();
            else PositionMove(visible, know, distWu, retreating);

            FaceTarget(true);
            Shoot(dt, visible);
            Grenade(distWu, visible);
            Evade(visible);
            Crouch(visible);
            LineOfFireCrouchCheck();
        }

        float HideTime()
        {
            float t = A != null ? A.HideBehindCoverTime.Pick(HaloAi.Rng) : 3f;
            if (A != null && !Attacking && A.DefendingHideTimeModifier > 0f) t *= A.DefendingHideTimeModifier;
            return Mathf.Max(1f, t);
        }

        /// <summary>Firing-position movement (confined to the squad's groups) or, for free actors, the range point.</summary>
        void PositionMove(bool visible, Knowledge know, float distWu, bool retreating)
        {
            HoldOnArrival();
            var sq = SquadData;
            bool known = know == Knowledge.Visible || know == Knowledge.Known;
            float maxFire = V != null && V.MaximumFiringDistance > 0f ? V.MaximumFiringDistance : 15f;
            if (visible && distWu > maxFire && !retreating) { if (_outOfRangeSince < 0f) _outOfRangeSince = Now; } else if (visible || retreating) _outOfRangeSince = -1f;
            bool outOfRange = _outOfRangeSince >= 0f && Now - _outOfRangeSince > 2f;      // ours: 2 s out of range
            bool pursue = !retreating && (outOfRange || TargetOutsideArea());

            if (!Confined)
            {
                // free actors / encounters without positions: the range point, the leader
                if (_wantCover || Now >= _fposUntil || outOfRange)
                {
                    float hold = A != null ? A.CombatPositionTime.Pick(HaloAi.Rng) : 5f;
                    ReleasePosition(); _fposUntil = Now + Mathf.Max(1f, hold); _hasGoal = false;
                    MoveTo(FollowLeader(out var l) ? NearLeader(l) : RangePoint(Target), outOfRange ? 1f : 0.9f);
                    _wantCover = false;
                }
                return;
            }

            uint mask = retreating ? (sq != null ? (sq.Defending != 0 ? sq.Defending : sq.DefendingGuard) : 0) : (pursue && sq != null && sq.Pursuing != 0 ? sq.Pursuing : CombatMask(known));
            bool entered = _posInit != _stateStarted;
            _posInit = _stateStarted;
            bool travelling = _fpos >= 0 && !_fposArrived;
            bool invalid = _fpos >= 0 && _fpos < Enc.Claims.Length && Enc.Claims[_fpos] > 1;
            bool repick = entered || _wantCover || Now >= _fposUntil || mask != _fposMask || invalid || (travelling && _damagedAt > _fposPickedAt);
            if (repick)
            {
                int p;
                float hold;
                if (pursue)
                {
                    // the target is out of range / outside the area: the pursuing position nearest the target, else the attacking one, else hold
                    p = NearestPositionTo(TargetLastSeen, sq != null ? sq.Pursuing : 0);
                    if (p < 0) p = NearestPositionTo(TargetLastSeen, CombatMask(true));
                    hold = A != null && A.PursuitPositionTime.Max > 0f ? A.PursuitPositionTime.Pick(HaloAi.Rng) : 3f;
                    if (p >= 0 && Once(1)) HaloLog.Info("AI: pursuit: " + Label + " to a " + (sq != null && sq.Pursuing != 0 ? "pursuing" : "attacking") + " position " + (Enc.Positions[p] - TargetLastSeen).magnitude.ToString("0") + " m from the target");
                }
                else
                {
                    bool cover = _wantCover || retreating;
                    p = PickPosition(mask, Target, cover);
                    // empty mask: attacking -> attacking search -> guard -> hold
                    if (p < 0) p = PickPosition(CombatMask(true), Target, cover);
                    if (p < 0) p = PickPosition(CombatMask(false), Target, cover);
                    if (p < 0) p = PickPosition(GuardMask(), Target, cover);
                    hold = A != null ? A.CombatPositionTime.Pick(HaloAi.Rng) : 5f;
                }
                // following: a firing position far from the leader is no good - stay by the leader
                if (p >= 0 && FollowLeader(out var fl) && (Enc.Positions[p] - fl.Feet).magnitude > Enc.FollowDistance * HaloSpace.Unit * 3f) p = -2;
                if (p >= 0) TakePosition(p, hold);
                else if (p == -2 && FollowLeader(out var l2)) { ReleasePosition(); _fposUntil = Now + Mathf.Max(1f, hold); _hasGoal = false; MoveTo(NearLeader(l2), 0.9f); }
                else
                {
                    ReleasePosition(); _fposUntil = Now + Mathf.Max(1f, hold); Stop();
                    if (Once(2)) HaloLog.Info("AI: confined: " + Label + " holds (no firing position in its squad's groups for this situation)");
                }
                _fposMask = mask;
                _wantCover = false;
            }
            if (_fpos >= 0) MoveTo(Enc.Positions[_fpos], 1f, 0.5f);
        }

        /// <summary>A squad held by its delay timer: on its guard positions, but it turns and shoots at a visible target; damage releases it.</summary>
        void TickHeld(float dt, bool visible)
        {
            if (Now - _damagedAt < 0.5f && Enc != null && Squad >= 0)
            {
                if (!Enc.SquadReleased(Squad))
                {
                    Enc.ExpireTimer(Squad);
                    HaloLog.Info("AI: " + Label + " was hit: squad " + (SquadData != null ? SquadData.Name : "?") + " released early");
                }
                return;
            }
            if (Confined)
            {
                HoldOnArrival();
                bool entered = _heldInit != _stateStarted;
                _heldInit = _stateStarted;
                if (entered || Now >= _fposUntil)
                {
                    int p = PickPosition(GuardMask(), Target, false);
                    if (p >= 0) TakePosition(p, A != null ? A.GuardPositionTime.Pick(HaloAi.Rng) : 5f);
                    else { ReleasePosition(); _fposUntil = Now + 2f; Stop(); }
                }
                if (_fpos >= 0) MoveTo(Enc.Positions[_fpos], 0.5f);
            }
            else TickIdle(dt);
            if (visible) { FaceTarget(true); Shoot(dt, true); } else FaceTarget(false);
            Crouch(visible);
        }

        void FaceTarget(bool aim)
        {
            if (Target == null) return;
            if (_grenadeAimUntil > Now) { Unit.LookPoint = _grenadeAim; Unit.AimPoint = _grenadeAim; return; }
            Vector3 look = TargetVisible ? Target.Head : TargetLastSeen + Vector3.up * 1.5f;
            Unit.LookPoint = look;
            Unit.AimPoint = aim ? AimPointFor(look) : Vector3.zero;
            // actr standing / crouching "must move forward" (hunters, carriers, flood): the body faces where it goes
            if (MustFaceMove())
            {
                Vector3 mv = Unit.MoveVelocity; mv.y = 0f;
                if (mv.sqrMagnitude < 0.01f) { mv = _goal - Position; mv.y = 0f; }
                if (mv.sqrMagnitude > 0.01f) Unit.LookPoint = Unit.Eye + mv.normalized * 10f;
            }
        }

        bool MustFaceMove()
        {
            if (A == null || Unit == null || !Moving || Flying) return false;
            return Unit.Crouching ? A.Has(HaloActorDef.CrouchingMustMoveForward) : A.Has(HaloActorDef.StandingMustMoveForward);
        }

        /// <summary>After Steer (called from HaloActor.Tick when wired): must-move-forward units move only within 45 degrees (ours) of their facing.</summary>
        void ClampMoveForward()
        {
            if (!MustFaceMove()) return;
            Vector3 v = Unit.MoveVelocity; float y = v.y; v.y = 0f;
            float m = v.magnitude;
            if (m < 0.05f) return;
            Vector3 f = Unit.transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) return;
            float ang = Vector3.SignedAngle(f, v, Vector3.up);
            if (Mathf.Abs(ang) <= 45f) return;
            float scale = Mathf.Abs(ang) > 90f ? 0.3f : 1f;        // ours: turning around first
            Vector3 c = Quaternion.AngleAxis(Mathf.Clamp(ang, -45f, 45f), Vector3.up) * f.normalized * (m * scale);
            c.y = y;
            // Steer's edge check ran on the unturned velocity: the turned one must pass it too, else turn in place (no walking off ledges)
            if (!NavAllows(Position, c)) { c = new Vector3(0f, y, 0f); ClampHeldSteps++; }
            Unit.MoveVelocity = c;
        }

        /// <summary>Lab counter: turned steps refused at an edge by ClampMoveForward.</summary>
        public static int ClampHeldSteps;

        /// <summary>
        /// Where the gun points. A persistent aim point follows the target (+ leading) by the actv target tracking (+ the difficulty's),
        /// read as the share of the aim error closed every 0.25 s (ours); while bursting, Halo's burst geometry is added: the burst starts
        /// at the actv burst origin radius off the target, within the origin angle of the target's movement line, and returns onto it over
        /// the return length (wu, at the burst angular velocity when set), turning by up to the return angle.
        /// </summary>
        Vector3 AimPointFor(Vector3 look)
        {
            Vector3 want;
            if (Target == null) want = look;
            else if (!TargetVisible) want = TargetLastSeen + Vector3.up * 1.0f;
            else
            {
                want = Target.Center;
                float lead = V != null ? Mathf.Clamp01(V.TargetLeading + HaloAi.DiffAdd(HaloAi.DiffTable.TargetLeading)) : 0.5f;
                float speed = HaloAi.ProjectileSpeed(Unit.Weapon);
                if (speed > 1f && lead > 0f)
                {
                    float t = (want - Unit.Eye).magnitude / speed;
                    want += Target.Velocity * (t * lead);
                }
            }
            if (_aimTick != HaloAi.TickCount)
            {
                bool stale = HaloAi.TickCount - _aimTick > 15;
                _aimTick = HaloAi.TickCount;
                if (stale) _aimPoint = want;
                else
                {
                    float tr = V != null ? Mathf.Clamp01(V.TargetTracking + HaloAi.DiffAdd(HaloAi.DiffTable.TargetTracking)) : 0.7f;
                    float k = tr >= 0.999f ? 1f : 1f - Mathf.Pow(1f - tr, (1f / 30f) / 0.25f);
                    _aimPoint = Vector3.Lerp(_aimPoint, want, k);
                }
            }
            Vector3 p = _aimPoint;
            if (_bursting && _burstOrigin != Vector3.zero) p += BurstOffset();
            return p;
        }

        void StartBurstOrigin()
        {
            _burstOrigin = Vector3.zero;
            if (V == null || Target == null) return;
            float r = V.BurstOriginRadius * HaloSpace.Unit * HaloAi.DiffScale(HaloAi.DiffTable.BurstError);
            if (r <= 0f) return;
            Vector3 lof = Target.Center - Unit.Eye;
            if (lof.sqrMagnitude < 0.01f) return;
            lof.Normalize();
            Vector3 tv = Vector3.ProjectOnPlane(Target.Velocity, lof);
            Vector3 dir; float ang;
            if (tv.sqrMagnitude > 0.04f)
            {
                dir = -tv.normalized;                                 // ours: the burst starts behind the target's movement and sweeps onto it
                ang = HaloAi.Rand(-V.BurstOriginAngle, V.BurstOriginAngle);
            }
            else
            {
                dir = Vector3.Cross(lof, Vector3.up);
                if (dir.sqrMagnitude < 1e-4f) dir = Vector3.Cross(lof, Vector3.right);
                dir.Normalize();
                ang = HaloAi.Rand(0f, Mathf.PI * 2f);
            }
            _burstLof = lof;
            _burstOrigin = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, lof) * dir * r;
            float rl = V.BurstReturnLength.Max > 0f ? V.BurstReturnLength.Pick(HaloAi.Rng) * HaloSpace.Unit : r;
            _burstReturnLen = Mathf.Max(0.05f, rl);
            _burstReturnRot = V.BurstReturnAngle > 0f ? HaloAi.Rand(-V.BurstReturnAngle, V.BurstReturnAngle) : 0f;
        }

        Vector3 BurstOffset()
        {
            float tau = Now - _burstStart;
            float travelled;
            if (V != null && V.BurstAngularVelocity > 0f && Target != null) travelled = V.BurstAngularVelocity * tau * (Target.Center - Unit.Eye).magnitude;
            else travelled = _burstReturnLen * tau / Mathf.Max(0.05f, _burstLen);
            float k = Mathf.Clamp01(travelled / _burstReturnLen);
            if (k >= 1f) return Vector3.zero;
            return Quaternion.AngleAxis(_burstReturnRot * k * Mathf.Rad2Deg, _burstLof) * _burstOrigin * (1f - k);
        }

        static float Mul(float m) { return m > 0f ? m : 1f; }

        void Shoot(float dt, bool visible)
        {
            if (V == null || Unit == null) return;
            bool lastKnown = !visible && A != null && A.Has(HaloActorDef.ShootAtTargetsLastLocation) && Now - TargetSeenAt < 2.5f;
            bool can = Target != null && (visible || lastKnown) && !RangedInhibited && !Unit.WeaponInhibited && !V.Has(HaloActorVariantDef.CannotUseRangedWeapons) && Unit.Weapon != null;
            bool wild = Now < _fireWildlyUntil;
            if (can)
            {
                float d = (Target.Center - Unit.Eye).magnitude / HaloSpace.Unit;
                if (Berserking && V.BerserkFiringRanges.Max > 0f)
                {
                    // berserk firing ranges (the hunter's 4..2 is stored reversed)
                    float lo = Mathf.Min(V.BerserkFiringRanges.Min, V.BerserkFiringRanges.Max), hi = Mathf.Max(V.BerserkFiringRanges.Min, V.BerserkFiringRanges.Max);
                    if (d < lo || d > hi) can = false;
                }
                else if (d > (V.MaximumFiringDistance > 0f ? V.MaximumFiringDistance : 15f)) can = false;
            }
            if (can && A != null && A.HasMore(HaloActorDef.MustStopToFire) && Moving) can = false;
            if (_grenadeAimUntil > Now) can = false;
            // flyers without "can shoot while flying" fire only hovering still (0.3 m/s, ours)
            if (can && Flying && !V.Has(HaloActorVariantDef.CanShootWhileFlying) && Unit.Velocity.sqrMagnitude > 0.09f) can = false;
            // facing: within the actr max aiming deviation (yaw) of the target (unless it starts firing before aligned / fires wildly)
            if (can && !wild && !(A != null && A.Has(HaloActorDef.StartFiringBeforeAligned)))
            {
                Vector3 to = Target.Center - Unit.transform.position; to.y = 0f;
                Vector3 f = Unit.transform.forward; f.y = 0f;
                float maxDev = A != null && A.MaxAimingDeviationYaw > 0.01f ? A.MaxAimingDeviationYaw * Mathf.Rad2Deg : 60f;
                if (to.sqrMagnitude > 0.01f && Vector3.Angle(f, to) > maxDev) can = false;
            }
            // friends in the line of fire (marines / crew): hold fire, step aside after half a second
            if (can && A != null && A.Has(HaloActorDef.AvoidFriendsLineOfFire))
            {
                HaloAiBody f = FriendInLineOfFire(Unit.Eye, Unit.AimPoint != Vector3.zero ? Unit.AimPoint : Target.Center);
                if (f != null)
                {
                    can = false;
                    if (_lofBlockedSince < 0f) _lofBlockedSince = Now;
                    if (Once(3)) HaloLog.Info("AI: line of fire: " + Label + " holds fire, " + f + " is in the way");
                    if (Now - _lofBlockedSince > 0.5f && Now >= _nextLofStep && !Flying && _evadeUntil < Now)
                    {
                        _nextLofStep = Now + 2f;                                 // ours
                        Vector3 to = Target.Feet - Position; to.y = 0f;
                        Vector3 side = Vector3.Cross(Vector3.up, to.sqrMagnitude > 0.01f ? to.normalized : Unit.transform.forward) * (HaloAi.Chance(0.5f) ? 1f : -1f);
                        if (!SideClear(side)) side = -side;
                        if (SideClear(side)) Push(side * Mathf.Max(1.5f, RunSpeed() * 0.6f), 0.5f);
                    }
                }
                else _lofBlockedSince = -1f;
            }

            // firing pattern multipliers: new target (actv new target firing pattern time), moving, berserk
            bool newT = V.NewTargetFiringPatternTime > 0f && Now - TargetSince < V.NewTargetFiringPatternTime;
            float durM = 1f, sepM = 1f, rofM = 1f, errM = 1f;
            if (newT) { durM *= Mul(V.NewTargetBurstDuration); sepM *= Mul(V.NewTargetBurstSeparation); rofM *= Mul(V.NewTargetRateOfFire); errM *= Mul(V.NewTargetProjectileError); }
            if (Moving) { durM *= Mul(V.MovingBurstDuration); sepM *= Mul(V.MovingBurstSeparation); rofM *= Mul(V.MovingRateOfFire); errM *= Mul(V.MovingProjectileError); }
            if (Berserking) { durM *= Mul(V.BerserkBurstDuration); sepM *= Mul(V.BerserkBurstSeparation); rofM *= Mul(V.BerserkRateOfFire); errM *= Mul(V.BerserkProjectileError); }
            Unit.AiRateScale = rofM * HaloAi.DiffScale(HaloAi.DiffTable.RateOfFire);
            Unit.AiErrorScale = errM * HaloAi.DiffScale(HaloAi.DiffTable.ProjectileError) * (wild ? 3f : 1f);     // ours: x3 firing wildly
            Unit.SpecialFireMinInterval = Mathf.Max(0f, V.SpecialFireDelay * HaloAi.DiffScale(HaloAi.DiffTable.SpecialFireDelay));
            if (Once(4) && (rofM != 1f || errM != 1f)) HaloLog.Info("AI: firing pattern: " + Label + (newT ? " new-target" : "") + (Moving ? " moving" : "") + (Berserking ? " berserk" : "") + " burst x" + durM.ToString("0.##") + ", separation x" + sepM.ToString("0.##") + ", rate x" + rofM.ToString("0.##") + ", error x" + errM.ToString("0.##"));

            if (_bursting)
            {
                if (Now >= _burstUntil || !can)
                {
                    _bursting = false;
                    float sep = V.BurstSeparation.Pick(HaloAi.Rng) * sepM;
                    _nextBurst = Now + Mathf.Max(0.1f, sep * HaloAi.DiffScale(HaloAi.DiffTable.BurstSeparation));
                    if (_surpriseDelayPending) { _surpriseDelayPending = false; if (V.SurpriseDelayTime > 0f) _nextBurst = Mathf.Max(_nextBurst, Now + V.SurpriseDelayTime); }
                }
            }
            else if (can && Now >= _nextBurst)
            {
                _bursting = true;
                _burstLen = V.BurstDuration.Pick(HaloAi.Rng) * durM;
                if (_burstLen <= 0f) _burstLen = 0.5f;
                if (wild) _burstLen = Mathf.Max(_burstLen, _fireWildlyUntil - Now);
                _burstStart = Now; _burstUntil = Now + _burstLen;
                StartBurstOrigin();
                HaloAiVoice.Say(this, Berserking ? HaloAiVoice.ShootingBerserk : HaloAiVoice.Shooting, 0.08f);
                HaloAi.NoteShot(this);
            }
            Unit.Firing = _bursting && can;
            if (!can && Target != null) HaloAi.NoteNoShot(WhyNot(visible, lastKnown));
        }

        int WhyNot(bool visible, bool lastKnown)
        {
            if (!visible && !lastKnown) return 0;
            if (Unit.Weapon == null) return 1;
            if (RangedInhibited || Unit.WeaponInhibited || V.Has(HaloActorVariantDef.CannotUseRangedWeapons)) return 2;
            float d = (Target.Center - Unit.Eye).magnitude / HaloSpace.Unit;
            if (d > (V.MaximumFiringDistance > 0f ? V.MaximumFiringDistance : 15f)) return 3;
            if (_grenadeAimUntil > Now) return 5;
            return 4;   // moving / facing / line of fire
        }

        /// <summary>A living friendly body (players included) within 0.5 m (ours) of the segment eye -> aim and closer than the aim point.</summary>
        HaloAiBody FriendInLineOfFire(Vector3 from, Vector3 to)
        {
            Vector3 seg = to - from;
            float len = seg.magnitude;
            if (len < 0.5f) return null;
            Vector3 dir = seg / len;
            const float clear = 0.5f;
            var all = HaloAiBodies.All;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == Body || b == Target || !b.Alive || !HaloAllegiance.AreFriends(Team, b.Team)) continue;
                if (SegmentDist(b.Center, from, dir, len) < clear || SegmentDist(b.Head, from, dir, len) < clear) return b;
            }
            return null;
        }

        /// <summary>Distance from a point to the segment (from, dir, len), +inf when the point projects outside it (beyond 0.3 m of the ends).</summary>
        static float SegmentDist(Vector3 p, Vector3 from, Vector3 dir, float len)
        {
            Vector3 c = p - from;
            float t = Vector3.Dot(c, dir);
            if (t < 0.3f || t > len - 0.3f) return float.MaxValue;
            return (c - dir * t).magnitude;
        }

        /// <summary>actr "crouch when in line of fire": crouch (min crouch time) while inside a friendly actor's burst (every 3rd tick).</summary>
        void LineOfFireCrouchCheck()
        {
            if (A == null || !A.Has(HaloActorDef.CrouchWhenInLineOfFire) || (HaloAi.TickCount + TickPhase) % 3 != 2) return;
            var list = HaloAi.Actors;
            Vector3 me = Unit.Eye;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == this || !o._bursting || !o.Alive || o.Target == null || o.Unit == null || !HaloAllegiance.AreFriends(Team, o.Team)) continue;
                Vector3 from = o.Unit.Eye, to = o.Target.Center;
                Vector3 seg = to - from; float len = seg.magnitude;
                if (len < 1f) continue;
                if (SegmentDist(me, from, seg / len, len) < 0.5f) { _lofCrouchUntil = Now + Mathf.Max(0.5f, A.MinCrouchTime); return; }
            }
        }

        // ------------------------------------------------------------------ surprise, gestures

        /// <summary>Workstream 5's one-shot Surprised: the surprise gesture, then either a wild burst (actv surprise fire wildly time, error x3)
        /// followed by the surprise delay, or just the surprise delay before the first burst.</summary>
        void ConsumeSurprise(bool visible)
        {
            if (!Surprised) return;
            Surprised = false;
            if (State == Mode.Flee || State == Mode.Cower || Target == null) return;
            Vector3 to = Target.Feet - Position; to.y = 0f;
            bool front = to.sqrMagnitude < 0.01f || Vector3.Dot(Unit.transform.forward, to) >= 0f;
            Unit.Gesture(front ? 27 : 28);                 // surprise-front / surprise-back
            HaloAiVoice.Say(this, HaloAiVoice.Surprise, 0.7f);
            if (V == null) return;
            if (V.SurpriseFireWildlyTime > 0f && visible)
            {
                _fireWildlyUntil = Now + V.SurpriseFireWildlyTime;
                _nextBurst = Now; _bursting = false;
                _surpriseDelayPending = true;
            }
            else if (V.SurpriseDelayTime > 0f) _nextBurst = Mathf.Max(_nextBurst, Now + V.SurpriseDelayTime);
            if (Once(5)) HaloLog.Info("AI: surprise: " + Label + (V.SurpriseFireWildlyTime > 0f && visible ? " fires wildly for " + V.SurpriseFireWildlyTime.ToString("0.##") + " s, then waits " : " waits ") + V.SurpriseDelayTime.ToString("0.##") + " s");
        }

        /// <summary>A leader (an actor whose type is a squadmate's actr leader type) signals the attack once per new target.</summary>
        void SignalAttack()
        {
            if (_signalledFor == Target || A == null || Enc == null) return;
            _signalledFor = Target;
            for (int i = 0; i < Enc.Actors.Count; i++)
            {
                var o = Enc.Actors[i];
                if (o == this || !o.Alive || o.A == null || o.A.Type == A.Type || o.A.LeaderType != A.Type) continue;
                Unit.Gesture(33);                           // signal-attack
                return;
            }
        }

        /// <summary>The last target died and nothing else is known: celebrate.</summary>
        void Celebrate()
        {
            if (_lastFoe == null) return;
            bool dead = !_lastFoe.Alive;
            _lastFoe = null;
            if (!dead || Unit == null) return;
            if (Unit.Gesture(40)) HaloAiVoice.Say(this, HaloAiVoice.Celebration, 0.5f);
        }

        /// <summary>For TickIdle (workstream 5): an infection form without a target feeds on a body nearby (the 'feeding' gesture);
        /// true = it took the tick.</summary>
        bool FloodIdle()
        {
            if (A == null || A.Type != HaloActorType.InfectionForm || Target != null || Unit == null) return false;
            if (Now >= _nextCorpseLook) { _nextCorpseLook = Now + 2f; _corpse = HaloAiBodies.CorpseNear(Position, 12f * HaloSpace.Unit); }   // ours: 12 wu, every 2 s
            if (_corpse == null) return false;
            Vector3 c = _corpse.transform.position;
            if (Flat(c - Position) > 0.8f) { MoveTo(OnNav(c), 0.6f, 0.6f); Face(c + Vector3.up * 0.3f, false); return true; }
            Stop();
            Face(c, false);
            if (Now >= _nextFeed)
            {
                _nextFeed = Now + 4f;                       // the 120-frame feeding clip
                if (Unit.Gesture(47) && Once(6)) HaloLog.Info("AI: " + Label + " feeds on " + _corpse.name);
            }
            return true;
        }

        // ------------------------------------------------------------------ grenades, evasion, crouching

        void Grenade(float distWu, bool visible, bool seekingCover = false, bool berserkThrow = false)
        {
            if (V == null || !HaloAi.Grenades || V.GrenadeStimulus == 0 && !berserkThrow || Target == null) return;
            if (!berserkThrow)
            {
                if (Now < _nextGrenadeCheck && !seekingCover) return;
                _nextGrenadeCheck = Now + Mathf.Max(1f, V.GrenadeCheckTime > 0f ? V.GrenadeCheckTime : 2f);
            }
            if (Unit.Grenades <= 0 && !V.Has(HaloActorVariantDef.HasUnlimitedGrenades)) return;
            if (!berserkThrow)
            {
                if (V.GrenadeStimulus == 1 && !visible) return;
                // "seek cover": as the actor runs for cover / flees, or when the target just went out of sight
                if (V.GrenadeStimulus == 2 && !(seekingCover || _wantCover || State == Mode.Hide || State == Mode.Flee || (!visible && Now - TargetSeenAt < 3f))) return;
            }
            if (distWu < V.GrenadeRanges.Min || (V.GrenadeRanges.Max > 0f && distWu > V.GrenadeRanges.Max)) return;
            if (Enc != null && Now - Enc.LastGrenade < V.EncounterGrenadeTimeout) return;
            // enough enemies near the target, no friends in the blast
            Vector3 at = TargetLastSeen;
            int enemies = 0, friends = 0;
            float er = Mathf.Max(0.5f, V.EnemyRadius) * HaloSpace.Unit, cr = V.CollateralDamageRadius * HaloSpace.Unit;
            var all = HaloAiBodies.All;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (!b.Alive) continue;
                float d2 = (b.Feet - at).sqrMagnitude;
                if (IsEnemy(b)) { if (d2 <= er * er) enemies++; }
                else if (b != Body && HaloAllegiance.AreFriends(Team, b.Team) && d2 <= cr * cr) friends++;
            }
            if (enemies < Mathf.Max(1, V.MinimumEnemyCount) || friends > 0) return;
            if (!berserkThrow && !HaloAi.Chance(V.GrenadeChance * HaloAi.DiffScale(HaloAi.DiffTable.GrenadeChanceScale))) return;
            _grenadeAim = at;
            _grenadeAimUntil = Now + 0.6f;
            Unit.AimPoint = at; Unit.LookPoint = at;
            Unit.ThrowGrenade = true;
            HaloAiVoice.Say(this, HaloAiVoice.GrenadeThrowing);
            if (Enc != null) Enc.LastGrenade = Now;
            HaloAi.NoteGrenade(this);
            if (berserkThrow && Once(7)) HaloLog.Info("AI: " + Label + " throws a grenade as it goes berserk (actr berserk grenade chance " + (A != null ? A.BerserkGrenadeChance.ToString("0.##") : "?") + ")");
        }

        /// <summary>Danger from hits and from enemies shooting at us: past the actor's evasion threshold an evader steps aside.</summary>
        void Evade(bool visible)
        {
            if (A == null || Unit == null || Flying) return;
            float threshold = Attacking ? A.AttackingEvasionThreshold : A.DefendingEvasionThreshold;
            if (threshold <= 0f || Now < _nextEvade || _evadeUntil > Now) return;
            if (_danger < threshold) return;
            _danger = 0f;
            _nextEvade = Now + Mathf.Max(1f, A.EvasionDelayTime > 0f ? A.EvasionDelayTime : 2f);
            if (HaloAi.Chance(A.EvasionSeekCoverChance)) { WantCover(); return; }
            Vector3 to = Target != null ? Target.Feet - Position : Unit.transform.forward; to.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * (HaloAi.Chance(0.5f) ? 1f : -1f);
            // keep on the ground we know: try the other side when this one has no floor / a wall
            if (!SideClear(side)) side = -side;
            if (!SideClear(side)) return;
            float speed = Mathf.Max(RunSpeed() * 1.4f, (Unit.Def != null && Unit.Def.EvadeDistance > 0f ? Unit.Def.EvadeDistance * HaloSpace.Unit / 0.5f : 4f));
            _evadeDir = side * Mathf.Min(speed, 7f);
            _evadeUntil = Now + 0.45f;
            // the unit's own evade animation (its root motion carries it); a plain side step when the graph has none
            bool right = Vector3.Dot(side, Unit.transform.right) > 0f;
            if (!Unit.Evade(right)) Push(_evadeDir, 0.45f);
            HaloAi.NoteEvade(this);
        }

        bool SideClear(Vector3 side)
        {
            Vector3 from = Position + Vector3.up * 0.6f;
            if (!HaloAiBodies.BudgetLeft) return true;
            if (!HaloAiBodies.Clear(from, from + side * 2.2f, true)) return false;
            var nav = HaloAi.Nav;
            if (nav == null) return true;
            Vector3 h = HaloSpace.ToHalo(Position + side * 2f);
            return nav.Locate(h.x, h.y, h.z + 0.1f, 0.3f, 0.6f, 0f) >= 0;
        }

        /// <summary>A shot was fired near / at us (a player's shot along <paramref name="dir"/>): danger for the evasion check; a friendly
        /// player's shot passing close makes "crouch when in line of fire" actors duck.</summary>
        public void ShotAt(Vector3 from, Vector3 dir)
        {
            if (!Alive || A == null) return;
            Vector3 to = Unit.Center - from;
            float d = to.magnitude;
            if (d < 0.5f || d > 40f * HaloSpace.Unit) return;
            if (A.Has(HaloActorDef.CrouchWhenInLineOfFire) && dir.sqrMagnitude > 1e-6f)
            {
                var pl = HaloAiBodies.NearestPlayer(from);
                if (pl != null && !IsEnemy(pl) && (pl.Head - from).sqrMagnitude < 16f && SegmentDist(Unit.Eye, from, dir.normalized, 80f) < 0.7f)     // ours: 0.7 m
                    _lofCrouchUntil = Now + Mathf.Max(0.5f, A.MinCrouchTime);
            }
            float c = Vector3.Dot(dir.normalized, to / d);
            if (c < 0.985f) return;      // ~10 degrees
            _danger += 0.6f;
            if (State == Mode.Idle || State == Mode.Patrol) { var p = PlayerNear(from); if (p != null) Acquire(p, "shot at"); }
        }

        HaloAiBody PlayerNear(Vector3 p)
        {
            var all = HaloAiBodies.All; HaloAiBody best = null; float bd = 4f;
            for (int i = 0; i < all.Count; i++) { var b = all[i]; if (!b.IsPlayer || !IsEnemy(b)) continue; float d = (b.Head - p).magnitude; if (d < bd) { bd = d; best = b; } }
            return best;
        }

        /// <summary>actv movement type "switch types": alternating crouch (crouch time) / run (run time) phases, the first crouched with the
        /// initial crouch chance; "movement switching stay with friends" copies the nearest squadmate's phase.</summary>
        bool MovePhaseCrouch()
        {
            if (V == null) return false;
            if (_movePhaseUntil < 0f)
            {
                _movePhaseCrouch = HaloAi.Chance(V.InitialCrouchChance);
                _movePhaseUntil = Now + PhaseTime(_movePhaseCrouch);
            }
            else if (Now >= _movePhaseUntil)
            {
                HaloActor mate = null;
                if (V.Has(HaloActorVariantDef.MovementSwitchingStayWithFriends) && Enc != null)
                {
                    float bd = 8f * HaloSpace.Unit;          // ours: 8 wu
                    for (int i = 0; i < Enc.Actors.Count; i++)
                    {
                        var o = Enc.Actors[i];
                        if (o == this || !o.Alive || o.Squad != Squad || o._movePhaseUntil <= Now) continue;
                        float d = (o.Position - Position).magnitude;
                        if (d < bd) { bd = d; mate = o; }
                    }
                }
                if (mate != null) { _movePhaseCrouch = mate._movePhaseCrouch; _movePhaseUntil = mate._movePhaseUntil; }
                else { _movePhaseCrouch = !_movePhaseCrouch; _movePhaseUntil = Now + PhaseTime(_movePhaseCrouch); }
            }
            return _movePhaseCrouch;
        }

        float PhaseTime(bool crouch)
        {
            var r = crouch ? V.CrouchTime : V.RunTime;
            float t = r.Max > 0f ? r.Pick(HaloAi.Rng) : 3f;        // ours: 3 s without a tag value
            return Mathf.Max(0.5f, t);
        }

        bool InLeapRange()
        {
            if (A == null || Target == null || A.MeleeLeapRange.Max <= 0f) return false;
            float d = Flat(Target.Feet - Position) / HaloSpace.Unit;
            return d <= Mathf.Max(A.MeleeLeapRange.Min, A.MeleeLeapRange.Max);
        }

        void Crouch(bool visible)
        {
            if (A == null || Unit == null) return;
            bool want = V != null && V.MovementType == 1;
            bool switching = V != null && V.MovementType == 2;
            bool phase = switching && MovePhaseCrouch();
            want |= phase;
            float th = Attacking ? A.AttackingCrouchThreshold : A.DefendingCrouchThreshold;
            if (th > 0f && _danger > th) want = true;
            bool shamble = false;
            switch (A.DefensiveCrouchType)
            {
                case 2: want |= Unit.MaxShield > 0f && Unit.Shield < 0.25f; break;         // low shields (ours: 0.25)
                case 3: want |= visible && !_bursting; break;                             // jackals: behind the shield between bursts
                case 4: want |= Target != null; break;
                case 5:                                                                   // flood shamble: the crouched approach outside leap range
                    shamble = Target != null && Moving && !InLeapRange() && (A.Has(HaloActorDef.DefensiveCrouchWhileCharging) || !Berserking);
                    want |= shamble;
                    break;
            }
            if (_holdCrouch) want = true;
            if (Moving)
            {
                if (A.Has(HaloActorDef.CannotMoveWhileCrouching)) want = false;
                else want = (V != null && V.MovementType == 1) || phase || shamble || (A.DefensiveCrouchType == 3 && want);
            }
            else if (Now < _lofCrouchUntil) want = true;
            if (A.Has(HaloActorDef.MustCrouchToShoot) && _bursting) want = true;
            if (A.HasMore(HaloActorDef.MustStandToFire) && _bursting) want = false;
            if (want != _crouch && Now >= _crouchChangeAt)
            {
                _crouch = want;
                _crouchChangeAt = Now + Mathf.Max(0.3f, want ? A.MinCrouchTime : A.MinStandTime);
            }
            Unit.Crouching = _crouch;
        }

        void WantCover() { _wantCover = true; }

        float _nextDive;
        float _nextDangerStep;
        /// <summary>A live grenade close by: dive away from it (actr "dive from grenade chance"), at most every 2 s, inside the grenade's
        /// proj danger radius (HaloAi.LiveGrenadeRadius; 3.5 m when the tag has none, ours). A live enemy unit with an "ai danger radius"
        /// (the flood carrier) inside that radius: step away from it.</summary>
        void DodgeGrenades()
        {
            if (Flying || Unit == null || A == null) return;
            if (Now >= _nextDangerStep && HaloAi.DangerNear(this, out var danger) && !danger.Grenade)
            {
                _nextDangerStep = Now + 1f;                                   // ours
                Vector3 away = Position - danger.At; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = -Unit.transform.forward;
                Push(away.normalized * Mathf.Max(3f, RunSpeed()), 0.4f);      // ours
            }
            if (A.DiveFromGrenadeChance <= 0f || Now < _nextDive) return;
            Vector3 me = Position;
            var list = HaloAi.LiveGrenades;
            var radius = HaloAi.LiveGrenadeRadius;
            for (int i = 0; i < list.Count; i++)
            {
                Vector3 d = me - list[i];
                float r = i < radius.Count ? radius[i] : 3.5f;
                if (d.sqrMagnitude > r * r || Mathf.Abs(d.y) > 2.5f) continue;
                _nextDive = Now + 2f;
                if (!HaloAi.Chance(A.DiveFromGrenadeChance)) return;
                d.y = 0f;
                if (d.sqrMagnitude < 0.01f) d = -Unit.transform.forward;
                d.Normalize();
                float f = Vector3.Dot(d, Unit.transform.forward), s = Vector3.Dot(d, Unit.transform.right);
                int dir = Mathf.Abs(f) >= Mathf.Abs(s) ? (f > 0f ? 0 : 1) : (s > 0f ? 3 : 2);
                HaloAiVoice.Say(this, HaloAiVoice.GrenadeSighted);
                if (!Unit.Dive(dir)) Push(d * Mathf.Max(4f, RunSpeed() * 1.5f), 0.6f);
                HaloAi.NoteEvade(this);
                return;
            }
        }

        // ------------------------------------------------------------------ search

        /// <summary>
        /// Searching by positions: the squad's search groups (its pursuing group when the target left the area), holding each reached
        /// position for the actr pursuit position time, up to num positions (normal; coord when squadmates search too), ending after the
        /// target search time (the state timer); then the actor settles to guarding. Free actors search around the last known spot.
        /// </summary>
        void TickSearch(float dt)
        {
            if (Target != null && TargetVisible && Released) { SetState(Mode.Combat); return; }
            if (_searchInit != _stateStarted)
            {
                _searchInit = _stateStarted; _searchVisited = 0; _searchHoldUntil = -1f; _nextSearchPoint = 0f;
                int others = 0;
                if (Enc != null) for (int i = 0; i < Enc.Actors.Count; i++) { var o = Enc.Actors[i]; if (o != this && o.Alive && o.Squad == Squad && o.State == Mode.Search) others++; }
                int n = A != null ? (others > 0 && A.NumPositionsCoord > 0 ? A.NumPositionsCoord : A.NumPositionsNormal) : 0;
                _searchMax = n > 0 ? n : 4;                       // ours: 4 without a tag value
            }
            if ((_stateUntil > 0f && Now >= _stateUntil) || _searchVisited >= _searchMax) { EndSearch(); return; }
            float holdT = A != null && A.PursuitPositionTime.Max > 0f ? A.PursuitPositionTime.Pick(HaloAi.Rng) : 2f;   // ours: 2 s

            if (Confined)
            {
                HoldOnArrival();
                if (_fpos >= 0 && _fposArrived && _searchHoldUntil < 0f) _searchHoldUntil = Now + holdT;
                bool next = _fpos < 0 ? Now >= _nextSearchPoint : (_fposArrived ? Now >= _searchHoldUntil : Now >= _fposUntil);
                if (next)
                {
                    if (_fpos >= 0 && _fposArrived) _searchVisited++;
                    if (_searchVisited >= _searchMax) { EndSearch(); return; }
                    var sq = SquadData;
                    uint mask = TargetOutsideArea() && sq != null && sq.Pursuing != 0 ? sq.Pursuing : CombatMask(false);
                    int p = PickPosition(mask, Target, false);
                    if (p < 0) p = PickPosition(CombatMask(true), Target, false);
                    if (p < 0) p = PickPosition(GuardMask(), Target, false);
                    _searchHoldUntil = -1f;
                    if (p >= 0) TakePosition(p, holdT);
                    else { ReleasePosition(); Stop(); _nextSearchPoint = Now + holdT; }
                }
                if (_fpos >= 0) MoveTo(Enc.Positions[_fpos], 0.55f);
            }
            else
            {
                if (Now >= _nextSearchPoint || (Arrived && _searchHoldUntil < 0f))
                {
                    if (Arrived && _searchHoldUntil < 0f) { _searchHoldUntil = Now + holdT; _nextSearchPoint = _searchHoldUntil; }
                    else
                    {
                        if (_searchHoldUntil >= 0f) _searchVisited++;
                        _searchHoldUntil = -1f;
                        _nextSearchPoint = Now + 8f;              // ours: the walk allowance
                        Vector3 c = Target != null ? TargetLastSeen : Position;
                        _searchPoint = c;
                        var nav = HaloAi.Nav;
                        if (nav != null)
                        {
                            Vector3 h = HaloSpace.ToHalo(c);
                            HVec3 q;
                            if (nav.RandomPointNear(new HVec3(h.x, h.y, h.z + 0.1f), 4f, HaloAi.Rng, out q)) _searchPoint = HaloSpace.Pos(q);
                        }
                        MoveTo(_searchPoint, 0.55f);
                    }
                }
            }
            Vector3 lookAt = Target != null && Now - TargetSeenAt < 6f ? TargetLastSeen + Vector3.up * 1.5f : Unit.Eye + Unit.transform.forward * 10f;
            if (Moving) lookAt = Unit.Eye + (_goal - Position).normalized * 10f;
            Face(lookAt, Target != null && !Moving);
            Unit.Crouching = false;
        }

        /// <summary>The search ran out: back to the return state; a squad that would search again settles to guarding instead.</summary>
        void EndSearch()
        {
            if (Once(8)) HaloLog.Info("AI: search ended: " + Label + " after " + _searchVisited + " of " + _searchMax + " positions");
            var s = ReturnState != HaloAiState.None ? ReturnState : InitialState;
            if (s == HaloAiState.Searching)
            {
                Target = null; TargetVisible = false; Berserking = false;
                EnterAiState(GuardMask() != 0 ? HaloAiState.GuardingAtGuardPosition : HaloAiState.Guarding);
            }
            else ReturnToRest();
        }

        // ------------------------------------------------------------------ flee / cower / hide / berserk

        /// <summary>Panics: runs away from the threat (to a firing position of its squad's groups) for the actr retreat time, then cowers
        /// (cowering time); grunts spread it to their squad (actr "panic in groups"); friends nearby hear of it (friends retreating trigger).</summary>
        public void Panic(HaloAiBody threat, float durationScale)
        {
            if (!Alive || A == null) return;
            if (State == Mode.Flee || State == Mode.Cower || Berserking) return;
            _threat = threat != null ? threat.Feet : (Target != null ? TargetLastSeen : Position - Unit.transform.forward * 5f);
            float t = A.RetreatTime.Max > 0f ? A.RetreatTime.Pick(HaloAi.Rng) : 4f;       // ours: 4 s without a tag value
            SetState(Mode.Flee, Mathf.Max(1f, t * durationScale));
            Unit.Gesture(41);          // the graph's "panic" (HaloAnimIds: 41)
            HaloAiVoice.Say(this, HaloAiVoice.Flee);
            if (V != null && V.GrenadeStimulus == 2 && Target != null) Grenade((Target.Feet - Position).magnitude / HaloSpace.Unit, TargetVisible, true);
            _hasGoal = false;
            int p = FarPosition(_threat);
            if (p >= 0) TakePosition(p, 10f);
            // friends within 8 wu (ours) see us run
            var list = HaloAi.Actors;
            float r = 8f * HaloSpace.Unit;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o != this && o.Alive && HaloAllegiance.AreFriends(Team, o.Team) && (o.Position - Position).sqrMagnitude < r * r) o.NotifyFriendFleeing(this);
            }
            if (A.HasMore(HaloActorDef.PanicInGroups) && Enc != null)
                for (int i = 0; i < Enc.Actors.Count; i++)
                {
                    var o = Enc.Actors[i];
                    if (o != this && o.Alive && o.Squad == Squad && (o.Position - Position).magnitude < r) o.Panic(threat, durationScale);
                }
        }

        /// <summary>The firing position farthest from the threat within 25 m of us, only in the squad's groups when confined.</summary>
        int FarPosition(Vector3 from)
        {
            if (Enc == null) return -1;
            uint mask = SquadUnionMask();
            int best = -1; float bs = float.MinValue;
            Vector3 me = Position;
            for (int i = 0; i < Enc.Positions.Length; i++)
            {
                if (mask != 0 && !Enc.PositionInGroups(i, mask)) continue;
                Vector3 p = Enc.Positions[i];
                float dm = Flat(p - me);
                if (dm > 25f * HaloSpace.Unit) continue;
                float s = Flat(p - from) - dm * 0.3f - Enc.Claims[i] * 5f;
                if (s > bs) { bs = s; best = i; }
            }
            return best;
        }

        void TickFlee(float dt)
        {
            if (_stateUntil > 0f && Now >= _stateUntil)
            {
                float cower = A != null && A.CoweringTime.Max > 0f ? A.CoweringTime.Pick(HaloAi.Rng) : 4f;
                SetState(Mode.Cower, Mathf.Max(1f, cower));
                return;
            }
            if (_fpos >= 0) MoveTo(Enc.Positions[_fpos], 1f);
            else if (!Confined)
            {
                Vector3 away = Position - _threat; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = -Unit.transform.forward;
                MoveTo(OnNav(Position + away.normalized * 8f), 1f);
            }
            else { Stop(); Unit.Crouching = true; Face(_threat + Vector3.up * 1.5f, false); return; }    // nowhere to run inside the area: cower
            Unit.Crouching = false;
            Face(Unit.Eye + (Unit.MoveVelocity.sqrMagnitude > 0.01f ? Unit.MoveVelocity.normalized : Unit.transform.forward) * 10f, false);
        }

        void TickCower(float dt)
        {
            Stop();
            Unit.Crouching = true;
            Face(_threat + Vector3.up * 1.5f, false);
            if (_stateUntil > 0f && Now >= _stateUntil)
            {
                if (Target != null && Target.Alive) SetState(Mode.Combat); else ReturnToRest();
            }
        }

        /// <summary>Hiding (low shields): cover positions of the squad's groups; back to the fight after the hide time once the shields reach
        /// the actr attack shield fraction (0 = no requirement), or earlier on the emerge-from-cover chance rolled every uncover delay.</summary>
        void TickHide(float dt)
        {
            if (Target == null) { ReturnToRest(); return; }
            if (_hideInit != _stateStarted) { _hideInit = _stateStarted; _hideEmergeAt = -1f; _fposUntil = 0f; }
            bool timeUp = _stateUntil < 0f || Now >= _stateUntil;
            float back = A != null ? A.AttackShieldFraction : 0f;
            bool shieldsOk = Unit.MaxShield <= 0f || back <= 0f || Unit.Shield >= back;
            if (timeUp && shieldsOk) { SetState(Mode.Combat); return; }
            if (timeUp && A != null && A.EmergeFromCoverChance > 0f)
            {
                if (_hideEmergeAt < 0f) _hideEmergeAt = Now + Mathf.Max(0.5f, A.UncoverDelayTime.Pick(HaloAi.Rng));
                else if (Now >= _hideEmergeAt)
                {
                    if (HaloAi.Chance(A.EmergeFromCoverChance)) { if (Once(9)) HaloLog.Info("AI: " + Label + " emerges from cover with shields " + Unit.Shield.ToString("0.00")); SetState(Mode.Combat); return; }
                    _hideEmergeAt = Now + Mathf.Max(0.5f, A.UncoverDelayTime.Pick(HaloAi.Rng));
                }
            }
            HoldOnArrival();
            if (Now >= _fposUntil)
            {
                int p = PickPosition(CombatMask(false) | GuardMask(), Target, true);
                if (p < 0) p = PickPosition(SquadUnionMask(), Target, true);
                if (p >= 0) TakePosition(p, 6f);
                else if (Confined) { _fposUntil = Now + 3f; Stop(); }
                else
                {
                    Vector3 away = Position - TargetLastSeen; away.y = 0f;
                    MoveTo(OnNav(Position + away.normalized * 6f), 1f);
                    _fposUntil = Now + 3f;
                }
            }
            if (_fpos >= 0) MoveTo(Enc.Positions[_fpos], 1f);
            FaceTarget(true);
            Shoot(dt, TargetVisible);
            Grenade((Target.Feet - Position).magnitude / HaloSpace.Unit, TargetVisible);
            Unit.Crouching = Arrived && A != null && (A.Has(HaloActorDef.CrouchWhenHidingFromUnopposable) || A.DefensiveCrouchType == 3);
        }

        /// <summary>Berserk until the target dies or is lost for the actr target search time: run at it (inside the squad's area), melee
        /// charge / leap / the carrier's burst at contact. Going berserk may start with one grenade (actr berserk grenade chance).</summary>
        public void GoBerserk(string why)
        {
            if (!Alive || Berserking) return;
            Berserking = true;
            _berserkLostUntil = -1f;
            SetState(Mode.Berserk);
            Unit.Gesture(29);          // "berserk"
            HaloAiVoice.Say(this, HaloAiVoice.Berserk);
            HaloAi.NoteBerserk(this, why);
            if (A != null && A.BerserkGrenadeChance > 0f && Target != null && HaloAi.Chance(A.BerserkGrenadeChance * HaloAi.DiffScale(HaloAi.DiffTable.GrenadeChanceScale)))
                Grenade(Flat(Target.Feet - Position) / HaloSpace.Unit, TargetVisible, false, true);
        }

        void TickBerserk(float dt)
        {
            if (Target == null || !Target.Alive) { Berserking = false; EndCharge(); if (Target != null && Target.Alive) SetState(Mode.Combat); else ReturnToRest(); return; }
            if (Know == Knowledge.Lost)
            {
                if (_berserkLostUntil < 0f) _berserkLostUntil = Now + Mathf.Max(1f, A != null ? A.TargetSearchTime.Pick(HaloAi.Rng) : 3f);
                else if (Now >= _berserkLostUntil) { Berserking = false; EndCharge(); SetState(Mode.Search, SearchTime()); return; }
            }
            else _berserkLostUntil = -1f;
            bool visible = TargetVisible;
            if (!TickMelee(visible) && _evadeUntil <= Now) Chase(ClampToArea(visible ? StandOff(Target.Feet) : TargetLastSeen), 1f, visible ? MeleeArriveM : 0.5f);
            Unit.Stance = A != null && A.Has(HaloActorDef.BerserkingUsesPanickedMovement) ? "flee" : null;
            Unit.Crouching = A != null && A.DefensiveCrouchType == 5 && A.Has(HaloActorDef.DefensiveCrouchWhileCharging) && Moving && !InLeapRange();
            FaceTarget(true);
            Shoot(dt, visible);
        }

        // ------------------------------------------------------------------ flying

        void FlyCombat(bool visible)
        {
            // flyers without "can shoot while flying" (sentinels) hover still for their bursts and sidestep between them
            if (V != null && !V.Has(HaloActorVariantDef.CanShootWhileFlying) && visible && Target != null)
            {
                float d = (Target.Center - Unit.Eye).magnitude / HaloSpace.Unit;
                float maxD = V.MaximumFiringDistance > 0f ? V.MaximumFiringDistance : 15f;
                if (_bursting || (Now >= _nextBurst && d <= maxD)) { Stop(); return; }
            }
            // hover in the desired range, a little above the target's head, sidestepping now and then (actr free flying sidestep)
            Vector3 me = Position, tp = TargetLastSeen + Vector3.up * 2.5f;
            float rMin = V != null ? V.DesiredCombatRange.Min * HaloSpace.Unit : 6f, rMax = V != null ? Mathf.Max(V.DesiredCombatRange.Max * HaloSpace.Unit, rMin) : 15f;
            Vector3 dd = me - tp; float dist = dd.magnitude;
            if (dist < 0.1f) dd = Vector3.forward; else dd /= dist;
            if (Now >= _nextSidestep)
            {
                _nextSidestep = Now + HaloAi.Rand(1.5f, 3.5f);
                float side = (A != null && A.FreeFlyingSidestep > 0f ? A.FreeFlyingSidestep : 1f) * HaloSpace.Unit;
                _flyOffset = Vector3.Cross(Vector3.up, dd) * HaloAi.Rand(-side, side) + Vector3.up * HaloAi.Rand(-1f, 1.5f);
            }
            float want = Mathf.Clamp(dist, rMin, rMax);
            Vector3 goal = tp + dd * want + _flyOffset;
            // stay out of walls: if the straight line to the goal is blocked, hold
            if (HaloAiBodies.BudgetLeft && !HaloAiBodies.Clear(me, goal, true)) goal = me + _flyOffset * 0.3f;
            MoveTo(goal, 0.8f, 0.6f);
        }

        // ------------------------------------------------------------------ following (allies)

        /// <summary>The body this actor's encounter follows (ai_follow_target_players / unit / ai), null when none.</summary>
        bool FollowLeader(out HaloAiBody leader)
        {
            leader = null;
            if (Enc == null) return false;
            // per squad (ai_follow_target_* with a squad moves that squad only); the encounter's own form otherwise
            HaloUnit fu; string fa;
            if (Enc.FollowsPlayers(Squad)) leader = HaloAiBodies.NearestPlayer(Position);
            else if ((fu = Enc.FollowUnitOf(Squad)) != null) leader = HaloAiBodies.Of(fu);
            else if ((fa = Enc.FollowAiOf(Squad)) != null) leader = HaloAi.FirstBodyOf(fa, this);
            return leader != null && leader.Alive;
        }

        Vector3 NearLeader(HaloAiBody l)
        {
            // a spot beside / behind the leader, spread by our id
            float ang = (Id * 67f) % 360f;
            Vector3 off = Quaternion.Euler(0f, ang, 0f) * Vector3.forward * Mathf.Max(1.5f, Enc.FollowDistance * HaloSpace.Unit * 0.6f);
            return OnNav(l.Feet + off);
        }

        void FollowIdle(HaloAiBody l)
        {
            float d = Flat(l.Feet - Position);
            float far = Enc.FollowDistance * HaloSpace.Unit;
            if (d > far) MoveTo(NearLeader(l), d > far * 2f ? 1f : 0.6f, 0.8f);
            else if (Arrived) Stop();
            if (!Moving) Face(l.Head, false);
        }
    }
}

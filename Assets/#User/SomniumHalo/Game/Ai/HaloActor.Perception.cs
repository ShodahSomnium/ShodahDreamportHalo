using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Actor perception (workstream 5): per-enemy perception entries with Halo's perception times (actr combat / guard / non-combat
    /// perception time: an enemy must stay perceived that long before the actor acknowledges it), the central / peripheral vision rates,
    /// camouflage, round-robin sight lines; the danger triggers (actr vehicle / player / unreachable danger trigger held for the danger
    /// trigger time -> <see cref="WantsRetreat"/>), the vehicle dodge, friends retreating, and command lists broken by damage / proximity.
    /// </summary>
    public sealed partial class HaloActor
    {
        // ------------------------------------------------------------------ perception entries

        struct Sense
        {
            public HaloAiBody Body; public int Key;
            public float Accum, FirstSeen, LastSeen, LastRay, Rate;
            public bool RayVisible, VisibleNow;
        }
        const int SenseMax = 8;
        readonly Sense[] _sense = new Sense[SenseMax];
        int _senseCount;

        /// <summary>Lab / log counters: sight acquisitions and the first few delays.</summary>
        public static int SightAcquires;

        /// <summary>Whether a body is perceived visible right now (the target's own sight line, or its perception entry).</summary>
        bool SeesNow(HaloAiBody b)
        {
            if (b == null) return false;
            if (b == Target && Now - TargetSeenAt < 0.35f) return true;
            for (int i = 0; i < _senseCount; i++) if (_sense[i].Body == b) return _sense[i].VisibleNow;
            return false;
        }

        /// <summary>Labs / logs (allocates; not per frame): what the actor's perception holds right now - the eye above the feet, the
        /// facing against each entry, its awareness, rate and last sight line, and the target's visibility.</summary>
        public string PerceptionDebug()
        {
            if (Unit == null || A == null) return "no unit";
            var sb = new System.Text.StringBuilder();
            Vector3 eye = Unit.Eye, fwd = Unit.transform.forward;
            sb.Append("eye +").Append((eye.y - Position.y).ToString("0.00")).Append(" m, need ").Append(PerceptionTime().ToString("0.0")).Append(" s, state ").Append(State).Append('/').Append(InitialState);
            sb.Append(", target ").Append(Target != null ? Target.ToString() : "-").Append(" visible ").Append(TargetVisible).Append(" seen ").Append((Now - TargetSeenAt).ToString("0.0")).Append(" s ago");
            sb.Append(", blind ").Append(Blind || (Enc != null && Enc.Blind)).Append(", entries ").Append(_senseCount);
            for (int i = 0; i < _senseCount; i++)
            {
                var s = _sense[i];
                if (s.Body == null) continue;
                Vector3 to = s.Body.Center - eye; float d = to.magnitude;
                float ang = d > 1e-3f ? Mathf.Acos(Mathf.Clamp(Vector3.Dot(fwd, to / d), -1f, 1f)) * Mathf.Rad2Deg : 0f;
                sb.Append(" | ").Append(s.Body).Append(' ').Append((d / HaloSpace.Unit).ToString("0.0")).Append(" wu at ").Append(ang.ToString("0")).Append(" deg, awareness ").Append(s.Accum.ToString("0.00"))
                  .Append(", rate ").Append(s.Rate.ToString("0.0")).Append(", ray ").Append(s.RayVisible).Append(' ').Append((Now - s.LastRay).ToString("0.0")).Append(" s ago");
            }
            if (_senseCount == 0 && HaloAiBodies.LocalPlayer != null)
            {
                Vector3 to = HaloAiBodies.LocalPlayer.Center - eye; float d = to.magnitude;
                float ang = d > 1e-3f ? Mathf.Acos(Mathf.Clamp(Vector3.Dot(fwd, to / d), -1f, 1f)) * Mathf.Rad2Deg : 0f;
                sb.Append(" | (no entry) player ").Append((d / HaloSpace.Unit).ToString("0.0")).Append(" wu at ").Append(ang.ToString("0")).Append(" deg (max ").Append((A.MaxVisionAngle * Mathf.Rad2Deg).ToString("0"))
                  .Append(", peripheral ").Append((A.PeripheralVisionAngle * Mathf.Rad2Deg).ToString("0")).Append(" within ").Append(A.PeripheralDistance.ToString("0.#")).Append(" wu)");
            }
            return sb.ToString();
        }

        int SenseIndex(HaloAiBody b)
        {
            for (int i = 0; i < _senseCount; i++) if (_sense[i].Body == b) return i;
            if (_senseCount < SenseMax)
            {
                int n = _senseCount++;
                _sense[n] = new Sense { Body = b, Key = b.Key, LastRay = -100f, LastSeen = -100f };
                return n;
            }
            // full: replace the least aware entry that is not the target
            int worst = -1; float wa = float.MaxValue;
            for (int i = 0; i < SenseMax; i++)
            {
                if (_sense[i].Body == Target) continue;
                float a = _sense[i].Accum + (_sense[i].Rate > 0f ? 100f : 0f);
                if (a < wa) { wa = a; worst = i; }
            }
            if (worst < 0) return -1;
            _sense[worst] = new Sense { Body = b, Key = b.Key, LastRay = -100f, LastSeen = -100f };
            return worst;
        }

        void DropSense(int i)
        {
            _senseCount--;
            if (i != _senseCount) _sense[i] = _sense[_senseCount];
            _sense[_senseCount] = default(Sense);
        }

        /// <summary>
        /// Looks for enemies (every third tick). The current target keeps its sight line at any angle; every other enemy in range gets a
        /// perception entry when it is inside the vision cone (central angle at full rate, out to the max / peripheral angle at half rate),
        /// sight lines are spent round robin over the in-cone entries, and an entry's awareness grows while it is seen (scaled by the body's
        /// camouflage). An enemy is acquired only once its awareness reaches the actr perception time (damage, magic sight and
        /// ai_try_to_fight are immediate).
        /// </summary>
        void Perceive()
        {
            if (A == null || Unit == null) return;
            bool blind = Blind || (Enc != null && Enc.Blind) || HaloAi.DebugBlind;
            Vector3 eye = Unit.Eye;
            Vector3 fwd = Unit.transform.forward;
            float range = (V != null && V.ModifiedVisionRange > 0f ? V.ModifiedVisionRange : A.MaxVisionDistance) * HaloSpace.Unit;
            if (range <= 0f) range = 40f * HaloSpace.Unit;
            float maxAngle = Mathf.Clamp(A.MaxVisionAngle > 0f ? A.MaxVisionAngle : 1.2f, 0.1f, Mathf.PI);
            float cosMax = Mathf.Cos(maxAngle);
            float cosCentral = Mathf.Cos(Mathf.Clamp(A.CentralVisionAngle > 0f ? Mathf.Min(A.CentralVisionAngle, maxAngle) : maxAngle, 0.05f, Mathf.PI));
            float cosPeri = Mathf.Cos(Mathf.Clamp(A.PeripheralVisionAngle, 0f, Mathf.PI));
            float periDist = A.PeripheralDistance * HaloSpace.Unit;
            if (Sleeping) { range = Mathf.Min(range, 2f * HaloSpace.Unit); cosMax = -1f; cosCentral = -1f; }     // ours: a sleeper notices only what is next to it
            const float period = 3f / 30f;
            const float slowRate = 0.5f;            // ours: peripheral awareness grows at half rate

            // 1) keep the current target in sight
            if (Target != null && Target.Alive)
            {
                bool vis = !blind && CanSee(eye, fwd, Target, range * 1.25f, -1f, 0f);    // once engaged, any angle (the body turns to it)
                if (vis) { TargetSeenAt = Now; TargetLastSeen = Target.Feet; }
            }

            // 2) forget what went away (dead, friendly now, a pooled body reused for something else, long out of view)
            for (int i = _senseCount - 1; i >= 0; i--)
            {
                var s = _sense[i];
                if (s.Body == null || s.Body.Key != s.Key || !IsEnemy(s.Body) || (s.Accum <= 0f && Now - s.LastSeen > 3f && Now - s.LastRay > 1f && s.Body != Target))
                    DropSense(i);
            }
            if (blind)
            {
                for (int i = 0; i < _senseCount; i++) { _sense[i].Accum = Mathf.Max(0f, _sense[i].Accum - period); _sense[i].VisibleNow = false; }
                return;
            }

            // 3) the cone pass: every enemy in range (cheap, no rays)
            for (int i = 0; i < _senseCount; i++) _sense[i].Rate = 0f;
            var all = HaloAiBodies.All;
            float range2 = range * range;
            for (int k = 0; k < all.Count; k++)
            {
                var b = all[k];
                if (!IsEnemy(b)) continue;
                Vector3 to = b.Center - eye;
                float d2 = to.sqrMagnitude;
                if (d2 > range2) continue;
                float rate;
                if (cosMax <= -1f || d2 < 0.25f) rate = 1f;
                else
                {
                    float d = Mathf.Sqrt(d2);
                    float c = Vector3.Dot(fwd, to / d);
                    if (c >= cosCentral) rate = 1f;
                    else if (c >= cosMax) rate = slowRate;
                    else if (d <= periDist && c >= cosPeri) rate = slowRate;
                    else rate = 0f;
                }
                if (b == Target && TargetSeenAt == Now) rate = Mathf.Max(rate, slowRate);      // the target is seen by its own line
                if (rate <= 0f) continue;
                int e = SenseIndex(b);
                if (e >= 0) _sense[e].Rate = rate;
            }

            // 4) sight lines, round robin over the in-cone entries (the index rotates, so a hidden nearest enemy does not blind us)
            int tested = 0;
            for (int n = 0; n < _senseCount && tested < 2; n++)               // ours: two bodies per look
            {
                int e = (_scanCursor + n) % _senseCount;
                if (_sense[e].Rate <= 0f) continue;
                var b = _sense[e].Body;
                bool vis;
                if (b == Target && TargetSeenAt == Now) vis = true;
                else
                {
                    if (!HaloAiBodies.BudgetLeft) break;
                    vis = HaloAiBodies.Clear(eye, b.Head, false) || HaloAiBodies.Clear(eye, b.Center, false);
                }
                _sense[e].RayVisible = vis; _sense[e].LastRay = Now;
                tested++;
                _scanCursor = e + 1;
            }
            if (tested == 0) _scanCursor++;
            if (_scanCursor > 1000000) _scanCursor = 0;

            // 5) awareness
            float need = PerceptionTime();
            HaloAiBody best = null; float bestScore = float.MinValue; int bestIdx = -1;
            Vector3 me = Position;
            for (int i = 0; i < _senseCount; i++)
            {
                var b = _sense[i].Body;
                bool vis = _sense[i].Rate > 0f && _sense[i].RayVisible && Now - _sense[i].LastRay < 0.35f;     // a fresh line (budgeted rays)
                if (b == Target && TargetSeenAt == Now) vis = true;
                _sense[i].VisibleNow = vis;
                if (vis)
                {
                    if (_sense[i].Accum <= 0f) _sense[i].FirstSeen = Now - period;     // this look's period counts (the delay matches the awareness)
                    _sense[i].Accum += period * Mathf.Max(_sense[i].Rate, 0f) * Mathf.Clamp01(b.Visibility);
                    _sense[i].LastSeen = Now;
                }
                else { _sense[i].Accum = Mathf.Max(0f, _sense[i].Accum - period); continue; }
                bool forced = (FightPlayer && b.IsPlayer) || (FightAi != null && b.Actor != null && HaloAi.InSelection(b.Actor, FightAi));
                if (_sense[i].Accum < need && !forced) continue;
                if (b == Target) { TargetSeenAt = Now; TargetLastSeen = b.Feet; }
                // visible > damaged us in the last second > preferred > distance
                float score = -(b.Feet - me).magnitude;
                if (b == _lastAttacker && Now - _lastAttackerAt < 1f) score += 4000f;
                if (b.Preferred) score += 2000f;
                if (forced) score += 8000f;
                if (b == Target) score += 1f;
                if (score > bestScore) { bestScore = score; best = b; bestIdx = i; }
            }
            if (best == null || best == Target) return;
            if (Target == null || !Target.Alive || ShouldSwitch(best))
            {
                float delay = Now - _sense[bestIdx].FirstSeen;
                LastAcquireDelay = delay;
                SightAcquires++;
                if (SightAcquires <= 5 || HaloAi.Verbose)
                    HaloLog.Info("AI: " + Label + " acquired " + best + " after " + delay.ToString("0.0") + " s (perception time " + need.ToString("0.0") + " s, " + ((best.Feet - me).magnitude / HaloSpace.Unit).ToString("0.0") + " wu)");
                Acquire(best, "sight");
            }
        }

        // ------------------------------------------------------------------ danger triggers

        /// <summary>The danger trigger level a body means to us: 0 none, 1 visible, 2 shot in the last 1 s, 3 shot at within 2 wu, 4 damaged us.</summary>
        public int DangerLevelFrom(HaloAiBody b)
        {
            if (b == null || !b.Alive) return 0;
            if (b == _lastAttacker && Now - _lastAttackerAt < 1f) return 4;                 // 1 s window: ours
            bool firing = Now - b.FiredAt < 1f;
            // a shot passing close (Combat's ShotAt: players' shots within ~10 degrees of us) while this body was shooting
            if (firing && b.IsPlayer && Now - _shotNearAt < 1f) return 3;
            if (firing) return 2;
            return SeesNow(b) ? 1 : 0;
        }

        /// <summary>A vehicle / player / unreachable danger trigger has been met for the actor's danger trigger time (Combat retreats).
        /// Cleared once the condition has been off for a second.</summary>
        public bool WantsRetreat { get; private set; }
        /// <summary>Which trigger set <see cref="WantsRetreat"/>: "vehicle", "player" or "unreachable".</summary>
        public string DangerKind { get; private set; }
        public static int DangerRetreats;

        float _dangerSince = -1f, _dangerHold, _dangerOffSince = -1f;
        int _compCacheTick = -100, _myComp = -1, _targetComp = -1;

        void TickDanger()
        {
            string kind = null;
            var t = Target;
            if (A != null && t != null && t.Alive)
            {
                int level = DangerLevelFrom(t);
                if (level > 0)
                {
                    if (t.InVehicle && A.VehicleDangerTrigger > 0 && level >= A.VehicleDangerTrigger) kind = "vehicle";
                    else if (t.IsPlayer && A.PlayerDangerTrigger > 0 && level >= A.PlayerDangerTrigger) kind = "player";
                    else if (A.UnreachableDangerTrigger > 0 && level >= A.UnreachableDangerTrigger && Unreachable(t)) kind = "unreachable";
                }
            }
            if (kind != null)
            {
                _dangerOffSince = -1f;
                if (_dangerSince < 0f) { _dangerSince = Now; _dangerHold = Mathf.Max(0f, A.DangerTriggerTime.Pick(HaloAi.Rng)); }
                if (!WantsRetreat && Now - _dangerSince >= _dangerHold)
                {
                    WantsRetreat = true; DangerKind = kind;
                    DangerRetreats++;
                    if (DangerRetreats <= 5 || HaloAi.Verbose) HaloLog.Info("AI: " + Label + " danger trigger (" + kind + ", level " + DangerLevelFrom(t) + ") held " + _dangerHold.ToString("0.0") + " s -> wants to retreat from " + t);
                }
            }
            else
            {
                _dangerSince = -1f;
                if (WantsRetreat)
                {
                    if (_dangerOffSince < 0f) _dangerOffSince = Now;
                    else if (Now - _dangerOffSince > 1f) { WantsRetreat = false; DangerKind = null; }     // ours
                }
            }
        }

        /// <summary>The target stands on another navigation island, or our last path toward a goal failed recently.</summary>
        bool Unreachable(HaloAiBody t)
        {
            if (Flying || HaloAi.Nav == null) return false;
            if (Now - _pathFailedAt < 3f) return true;                                     // ours
            if (HaloAi.TickCount - _compCacheTick >= 9)                                    // ours: islands every 9 ticks
            {
                _compCacheTick = HaloAi.TickCount;
                _myComp = HaloAiEncounter.ComponentAt(Position);
                _targetComp = HaloAiEncounter.ComponentAt(t.Feet);
            }
            return _myComp >= 0 && _targetComp >= 0 && _myComp != _targetComp;
        }

        // ------------------------------------------------------------------ vehicles bearing down

        float _nextVehicleDodge;
        public static int VehicleDodges;

        /// <summary>An enemy vehicle within its unit "ai vehicle radius" heading at us: a dive aside with the actr notice vehicle chance.</summary>
        void DodgeVehicles()
        {
            if (A == null || A.NoticeVehicleChance <= 0f || Unit.Seat != null || Flying || Now < _nextVehicleDodge) return;
            var all = HaloAiBodies.All;
            Vector3 me = Position;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (!b.InVehicle || b.VehicleRadius <= 0f || !IsEnemy(b)) continue;
                Vector3 v = b.Velocity; v.y = 0f;
                float sp = v.magnitude;
                if (sp < 3f) continue;
                Vector3 to = me - b.Feet; to.y = 0f;
                float d = to.magnitude;
                if (d > b.VehicleRadius + sp * 0.6f || d < 0.01f) continue;     // ours: 0.6 s to react on top of the radius
                Vector3 head = v / sp;
                if (Vector3.Dot(head, to / d) <= 0.7f) continue;
                _nextVehicleDodge = Now + 1.5f;                                   // ours: one roll per approach
                if (!HaloAi.Chance(A.NoticeVehicleChance)) return;
                // dive to the side of the vehicle's path we are on
                Vector3 side = Vector3.Cross(Vector3.up, head);
                if (Vector3.Dot(side, to) < 0f) side = -side;
                bool right = Vector3.Dot(side, Unit.transform.right) > 0f;
                if (Unit.Dive(right ? 3 : 2))
                {
                    VehicleDodges++;
                    if (VehicleDodges <= 3 || HaloAi.Verbose) HaloLog.Info("AI: " + Label + " dove from " + b + " (" + sp.ToString("0") + " m/s, " + d.ToString("0.0") + " m)");
                }
                return;
            }
        }

        // ------------------------------------------------------------------ friends retreating

        readonly int[] _fleeingIds = { -1, -1, -1, -1, -1, -1, -1, -1 };
        readonly float[] _fleeingAt = new float[8];

        /// <summary>A friend started fleeing (counts toward the actr friends-retreating trigger: panic once that many distinct friends within
        /// 8 wu that we can see fled in the last 10 s).</summary>
        public void NotifyFriendFleeing(HaloActor f)
        {
            if (f == null || f == this || !Alive || A == null || A.FriendsRetreatingTrigger <= 0) return;
            if (State == Mode.Flee || State == Mode.Cower || Berserking || f.Team != Team) return;
            float r = 8f * HaloSpace.Unit;                                                // ours
            if ((f.Position - Position).sqrMagnitude > r * r) return;
            Vector3 fc = f.Unit != null ? f.Unit.Center : f.Position + Vector3.up;
            if (!HaloAiBodies.Clear(Unit.Eye, fc, true)) return;
            int slot = -1, oldest = 0;
            for (int i = 0; i < _fleeingIds.Length; i++)
            {
                if (_fleeingIds[i] == f.Id) { slot = i; break; }
                if (_fleeingAt[i] < _fleeingAt[oldest]) oldest = i;
            }
            if (slot < 0) slot = oldest;
            _fleeingIds[slot] = f.Id; _fleeingAt[slot] = Now;
            int n = 0;
            for (int i = 0; i < _fleeingIds.Length; i++) if (_fleeingIds[i] >= 0 && Now - _fleeingAt[i] < 10f) n++;     // 10 s: ours
            if (n >= A.FriendsRetreatingTrigger)
            {
                HaloAi.NotePanic(this, "friends retreating");
                if (HaloAi.Verbose) HaloLog.Info("AI: " + Label + " panics: " + n + " friends retreating (trigger " + A.FriendsRetreatingTrigger + ")");
                Panic(f.Target != null ? f.Target : Target, 1f);
            }
        }

        // ------------------------------------------------------------------ command lists

        public static int CommandBreaks;

        /// <summary>An enemy inside the actr suicide sensing distance (carriers) or surprise distance.</summary>
        bool EnemyClose()
        {
            if (A == null) return false;
            float wu = A.SuicideSensingDist > 0f ? A.SuicideSensingDist : A.SurpriseDistance;
            if (wu <= 0f) return false;
            float r = wu * HaloSpace.Unit, r2 = r * r;
            Vector3 me = Position;
            var all = HaloAiBodies.All;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (IsEnemy(b) && (b.Feet - me).sqrMagnitude < r2) return true;
            }
            return false;
        }

        void BreakCommandList(string why)
        {
            if (Commands == null || Commands.List == null) return;
            CommandBreaks++;
            if (CommandBreaks <= 10 || HaloAi.Verbose) HaloLog.Info("AI: " + Label + " broke command list " + Commands.List.Name + " (" + why + ")");
            Commands.Stop();
            if (Sleeping) InitialState = HaloAiState.Alert;
            if (Target != null && Target.Alive && (State == Mode.Idle || State == Mode.Patrol) && Released) SetState(Mode.Combat);
        }
    }
}

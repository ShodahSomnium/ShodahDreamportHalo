using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] One scenario encounter at run time: its actors, the squads' delay timers and placement counts, the platoons'
    /// attacking / defending state and their change / maneuver rules, the firing positions (with who holds which), the follow target,
    /// script switches (blind, deaf, braindead, respawn, force active) and the team (encounter team, else each unit's own).
    /// </summary>
    public sealed class HaloAiEncounter
    {
        public sealed class SquadState
        {
            public int Placed, Killed;
            public bool TimerStarted, TimerExpired;
            public float TimerEnds = -1f;
            /// <summary>ai_automatic_migration_target (default on: a50 switches whole sets off before the scripted moves; unverified).</summary>
            public bool AutomaticMigrationTarget = true;
            /// <summary>Squad-form ai_follow_target_*: when HasFollow, the squad follows these instead of the encounter's.</summary>
            public bool HasFollow, FollowPlayers; public HaloUnit FollowUnit; public string FollowAi;
            public int RespawnsLeft;
            public float NextRespawn = -1f;
        }

        public sealed class PlatoonState
        {
            public bool Defending;
            public bool Changed, Maneuvered;
            public bool ManeuverEnabled = true;
        }

        public readonly HaloAiScenario.Encounter Data;
        public readonly int Index;
        public readonly SquadState[] Squads;
        public readonly PlatoonState[] Platoons;
        public readonly List<HaloActor> Actors = new List<HaloActor>(16);
        /// <summary>Firing positions in Unity space (feet) and how many actors hold / head for each.</summary>
        public readonly Vector3[] Positions;
        public readonly int[] Claims;
        public HaloTeam Team;
        public bool Blind, Deaf, Braindead, Respawn, ForceActive, Playfight, ManeuverEnabled = true, AllowCharge = true, AllowDormant = true;
        /// <summary>Encounter activity (HaloAi, about 1 Hz): ai_force_active, a player within 40 wu (ours) or in the same BSP cluster as one
        /// of its actors, an actor still fighting, or the encounter it is linked to (ai_link_activation) active. An inactive encounter that
        /// may go dormant (ai_allow_dormant, default on) is not ticked and its units are suspended.</summary>
        public bool Active = true;
        // following: the encounter form of ai_follow_target_* (squad forms live in SquadState). FollowPlayers / FollowUnit / FollowAi are
        // the legacy encounter-wide view (any squad following counts) for readers that do not ask per squad: use FollowsPlayers(squad) etc.
        public bool FollowPlayers; public HaloUnit FollowUnit; public string FollowAi; public float FollowDistance = 2.5f;   // world units (ours: ai_follow_distance default)
        public bool EncFollowPlayers; public HaloUnit EncFollowUnit; public string EncFollowAi;
        public float LastGrenade = -100f;
        public int TotalPlaced, TotalKilled;
        /// <summary>The encounter has been placed at least once (scripts' ai_place or "not initially created" unset at mission start).</summary>
        public bool Created;
        public HaloAiEncounter LinkedTo;

        public HaloAiEncounter(HaloAiScenario.Encounter data)
        {
            Data = data; Index = data.Index;
            Squads = new SquadState[data.Squads.Count];
            for (int i = 0; i < Squads.Length; i++) Squads[i] = new SquadState { RespawnsLeft = data.Squads[i].RespawnTotal };
            Platoons = new PlatoonState[data.Platoons.Count];
            for (int i = 0; i < Platoons.Length; i++) Platoons[i] = new PlatoonState { Defending = (data.Platoons[i].Flags & HaloAiScenario.PlStartInDefendingState) != 0 };
            Positions = new Vector3[data.FiringPositions.Count];
            for (int i = 0; i < Positions.Length; i++) Positions[i] = HaloSpace.Pos(data.FiringPositions[i].Position);
            Claims = new int[Positions.Length];
            Team = data.Team > 0 && data.Team <= 5 ? (HaloTeam)data.Team : HaloTeam.None;
            Blind = data.Has(HaloAiScenario.EncInitiallyBlind);
            Deaf = data.Has(HaloAiScenario.EncInitiallyDeaf);
            Braindead = data.Has(HaloAiScenario.EncInitiallyBraindead);
            Respawn = data.Has(HaloAiScenario.EncRespawnEnabled);
        }

        public string Name => Data.Name;

        /// <summary>Does an actor belong to the selection (squad / platoon -1 = any)?</summary>
        public static bool Matches(HaloActor a, int squad, int platoon)
        {
            if (squad >= 0 && a.Squad != squad) return false;
            if (platoon >= 0 && a.Platoon != platoon) return false;
            return true;
        }

        public int Living(int squad, int platoon)
        {
            int n = 0;
            for (int i = 0; i < Actors.Count; i++) { var a = Actors[i]; if (a.Alive && Matches(a, squad, platoon)) n++; }
            return n;
        }

        public bool IsPlatoonDefending(int platoon) { return platoon >= 0 && platoon < Platoons.Length && Platoons[platoon].Defending; }

        /// <summary>The platoon's strength and dead fraction (members placed so far).</summary>
        public void PlatoonStats(int platoon, out float strength, out int dead, out int total)
        {
            float vit = 0f; dead = 0; total = 0;
            for (int i = 0; i < Actors.Count; i++)
            {
                var a = Actors[i];
                if (a.Platoon != platoon) continue;
                total++;
                if (!a.Alive) { dead++; continue; }
                vit += a.Strength;
            }
            strength = total > 0 ? vit / total : 1f;
        }

        /// <summary>ScenarioChangeAttackingDefendingStateWhen: 0 never, 1 75% strength, 2 50% strength, 3 25% strength, 4 anybody dead,
        /// 5 25% dead, 6 50% dead, 7 75% dead, 8 all but one dead, 9 all dead.</summary>
        public bool Condition(int when, int platoon)
        {
            if (when <= 0 || platoon < 0 || platoon >= Platoons.Length) return false;
            float s; int dead, total;
            PlatoonStats(platoon, out s, out dead, out total);
            if (total == 0) return false;
            float df = (float)dead / total;
            switch (when)
            {
                case 1: return s <= 0.75f;
                case 2: return s <= 0.5f;
                case 3: return s <= 0.25f;
                case 4: return dead > 0;
                case 5: return df >= 0.25f;
                case 6: return df >= 0.5f;
                case 7: return df >= 0.75f;
                case 8: return total - dead <= 1;
                case 9: return dead >= total;
            }
            return false;
        }

        /// <summary>Platoon rules, squad timers, respawns (a few times a second).</summary>
        float _nextMigration;

        public void Think(float now)
        {
            for (int p = 0; p < Platoons.Length; p++)
            {
                var pd = Data.Platoons[p];
                var ps = Platoons[p];
                if (!ps.Changed && pd.ChangeWhen > 0 && Condition(pd.ChangeWhen, pd.ChangeHappensTo >= 0 ? pd.ChangeHappensTo : p))
                {
                    ps.Changed = true;
                    ps.Defending = !ps.Defending;
                    if (HaloAi.Verbose) HaloLog.Info("AI: " + Name + "/" + pd.Name + " now " + (ps.Defending ? "defending" : "attacking"));
                }
                if (!ps.Maneuvered && pd.ManeuverWhen > 0 && Condition(pd.ManeuverWhen, pd.ManeuverHappensTo >= 0 ? pd.ManeuverHappensTo : p))
                {
                    // the rule is used up when it triggers; ai_maneuver_enable false only withholds the order (Halo)
                    ps.Maneuvered = true;
                    if (ManeuverEnabled && ps.ManeuverEnabled) Maneuver(p, (pd.Flags & HaloAiScenario.PlFleeWhenManeuvering) != 0);
                }
            }

            if (now >= _nextMigration) { _nextMigration = now + 1f; AutomaticMigration(); }

            for (int s = 0; s < Squads.Length; s++)
            {
                var ss = Squads[s];
                if (ss.TimerStarted && !ss.TimerExpired && ss.TimerEnds >= 0f && now >= ss.TimerEnds) ExpireTimer(s);
                // respawn (encounter "respawn enabled" + squad respawn counts)
                var sd = Data.Squads[s];
                // respawn total 0 (b30 valley_mouth / ref_mouth_jackal: respawn 4-5, total 0) = unlimited while respawn is on (unverified)
                if (Respawn && (ss.RespawnsLeft > 0 || sd.RespawnTotal <= 0) && sd.RespawnMax > 0)
                {
                    int living = Living(s, -1);
                    if (living < sd.RespawnMin || (living < sd.RespawnMax && ss.NextRespawn >= 0f))
                    {
                        if (ss.NextRespawn < 0f) ss.NextRespawn = now + Mathf.Max(0.5f, sd.RespawnDelay.Max > 0f ? HaloAi.Rand(sd.RespawnDelay.Min, sd.RespawnDelay.Max) : HaloAi.Rand(Data.RespawnDelay.Min, Data.RespawnDelay.Max));
                        else if (now >= ss.NextRespawn)
                        {
                            ss.NextRespawn = -1f;
                            if (sd.RespawnTotal > 0) ss.RespawnsLeft--;
                            HaloAi.PlaceSquad(this, s, 1, false);
                        }
                    }
                }
            }
        }

        /// <summary>Starts the delay timer of every squad (an actor of the encounter acquired a target: the encounter is alerted). Squads
        /// flagged "no timer, delay forever" keep waiting for ai_timer_expire.</summary>
        public void StartAllTimers()
        {
            bool any = false;
            for (int s = 0; s < Squads.Length; s++)
            {
                if (Squads[s].TimerStarted || Data.Squads[s].Has(HaloAiScenario.SqNoTimerDelayForever)) continue;
                StartTimer(s, HaloAi.Time);
                any = true;
            }
            if (any && HaloAi.Verbose) HaloLog.Info("AI: " + Name + " alerted: squad timers started");
        }

        /// <summary>An actor arrived in a squad (migration, maneuver, spawned): the squad's own timer flags apply to it - "start timer
        /// immediately" starts the squad's timer, and an already expired timer with "magic sight after timer" lets it see the players.</summary>
        public void Joined(HaloActor a)
        {
            if (a == null || a.Enc != this || a.Squad < 0 || a.Squad >= Squads.Length) return;
            var sd = Data.Squads[a.Squad]; var ss = Squads[a.Squad];
            if (!ss.TimerStarted && sd.Has(HaloAiScenario.SqStartTimerImmediately)) StartTimer(a.Squad, HaloAi.Time);
            else if (ss.TimerExpired && sd.Has(HaloAiScenario.SqMagicSightAfterTimer) && a.Alive) a.MagicallySeePlayers();
        }

        // ------------------------------------------------------------------ following (per squad)

        /// <summary>Does the squad follow the players (its own ai_follow_target_players, else the encounter's)?</summary>
        public bool FollowsPlayers(int squad) { var s = SquadOf(squad); return s != null && s.HasFollow ? s.FollowPlayers : EncFollowPlayers; }
        /// <summary>The unit the squad follows (ai_follow_target_unit), null none.</summary>
        public HaloUnit FollowUnitOf(int squad) { var s = SquadOf(squad); return s != null && s.HasFollow ? s.FollowUnit : EncFollowUnit; }
        /// <summary>The ai the squad follows (ai_follow_target_ai), null none.</summary>
        public string FollowAiOf(int squad) { var s = SquadOf(squad); return s != null && s.HasFollow ? s.FollowAi : EncFollowAi; }

        SquadState SquadOf(int squad) { return squad >= 0 && squad < Squads.Length ? Squads[squad] : null; }

        /// <summary>Sets the follow target of one squad (squad >= 0) or of the whole encounter (squad &lt; 0: every squad's own setting is dropped).</summary>
        public void SetFollow(int squad, bool players, HaloUnit unit, string ai)
        {
            if (squad >= 0 && squad < Squads.Length)
            {
                var s = Squads[squad];
                s.HasFollow = true; s.FollowPlayers = players; s.FollowUnit = unit; s.FollowAi = ai;
            }
            else
            {
                EncFollowPlayers = players; EncFollowUnit = unit; EncFollowAi = ai;
                for (int i = 0; i < Squads.Length; i++) { Squads[i].HasFollow = false; Squads[i].FollowUnit = null; Squads[i].FollowAi = null; Squads[i].FollowPlayers = false; }
            }
            RefreshFollowView();
        }

        /// <summary>The legacy encounter-wide view: the encounter form, else any squad's.</summary>
        public void RefreshFollowView()
        {
            bool p = EncFollowPlayers; HaloUnit u = EncFollowUnit; string ai = EncFollowAi;
            for (int i = 0; i < Squads.Length; i++)
            {
                var s = Squads[i];
                if (!s.HasFollow) continue;
                p |= s.FollowPlayers;
                if (u == null) u = s.FollowUnit;
                if (ai == null) ai = s.FollowAi;
            }
            FollowPlayers = p; FollowUnit = p ? null : u; FollowAi = p || u != null ? null : ai;
        }

        public void StartTimer(int squad, float now)
        {
            if (squad < 0 || squad >= Squads.Length) return;
            var ss = Squads[squad];
            if (ss.TimerStarted) return;
            ss.TimerStarted = true;
            float d = Data.Squads[squad].SquadDelayTime;
            ss.TimerEnds = now + Mathf.Max(0f, d);
            if (d <= 0f) ExpireTimer(squad);
        }

        public void ExpireTimer(int squad)
        {
            if (squad < 0 || squad >= Squads.Length) return;
            var ss = Squads[squad];
            ss.TimerStarted = true; ss.TimerExpired = true;
            if (Data.Squads[squad].Has(HaloAiScenario.SqMagicSightAfterTimer))
                for (int i = 0; i < Actors.Count; i++) { var a = Actors[i]; if (a.Alive && a.Squad == squad) a.MagicallySeePlayers(); }
        }

        /// <summary>Whether the squad may fight yet (its delay timer).</summary>
        public bool SquadReleased(int squad)
        {
            if (squad < 0 || squad >= Squads.Length) return true;
            var sd = Data.Squads[squad];
            if (sd.SquadDelayTime <= 0f && !sd.Has(HaloAiScenario.SqNoTimerDelayForever)) return true;
            return Squads[squad].TimerExpired;
        }

        /// <summary>The platoon's squads move into their "maneuver to" squads (taking that squad's firing position groups).</summary>
        public int Maneuver(int platoon, bool flee) { return Maneuver(platoon, -1, flee, null); }

        /// <summary>
        /// Moves the living actors of one squad (squad >= 0) or of a platoon into their squads' "maneuver to" squads (ai_retreat /
        /// ai_maneuver act on exactly the ai given). The arrivals take the new squad's timer flags. "flee when maneuvering": they go to the
        /// new squad's positions as their squad (not a generic panic run to any position); "say advancing when maneuver" voices it.
        /// <paramref name="log"/> (a script call) logs who moved where, once per call text. Returns the actors moved.
        /// </summary>
        public int Maneuver(int platoon, int squad, bool flee, string log)
        {
            int moved = 0;
            bool advancing = platoon >= 0 && platoon < Data.Platoons.Count && (Data.Platoons[platoon].Flags & HaloAiScenario.PlSayAdvancingWhenManeuver) != 0;
            System.Text.StringBuilder sb = log != null ? new System.Text.StringBuilder() : null;
            Tmp.Clear(); Tmp.AddRange(Actors);
            for (int i = 0; i < Tmp.Count; i++)
            {
                var a = Tmp[i];
                if (!a.Alive) continue;
                if (squad >= 0 ? a.Squad != squad : a.Platoon != platoon) continue;
                int from = a.Squad;
                int to = from >= 0 && from < Data.Squads.Count ? Data.Squads[from].ManeuverToSquad : -1;
                if (to < 0 || to >= Data.Squads.Count) continue;
                a.MoveToSquad(this, to);
                Joined(a);
                if (flee) HaloAiVoice.Say(a, HaloAiVoice.Retreat, 0.5f);
                else if (advancing) HaloAiVoice.Say(a, HaloAiVoice.Advance, 0.5f);
                if (sb != null) sb.Append(sb.Length > 0 ? ", " : "").Append(a.Label).Append(" (").Append(Data.Squads[from].Name).Append(" -> ").Append(Data.Squads[to].Name).Append(')');
                moved++;
            }
            Tmp.Clear();
            if (sb != null) HaloLog.Info("AI: " + log + ": " + moved + " actors moved" + (moved > 0 ? ": " + sb : ""));
            else if (moved > 0 && HaloAi.Verbose) HaloLog.Info("AI: " + Name + "/" + Data.Platoons[platoon].Name + " maneuvers (" + moved + " actors)" + (flee ? " fleeing" : ""));
            return moved;
        }

        static readonly List<HaloActor> Tmp = new List<HaloActor>(16);

        // ------------------------------------------------------------------ automatic migration

        /// <summary>
        /// Squads flagged "automatic migration" move their living actors into the encounter's automatic-migration squads (also flagged, and
        /// ai_automatic_migration_target on) whose firing positions are nearest the nearest player - the a50 / c20 allies moving with the
        /// player. A move needs the new squad to be 2 wu (ours) nearer than the current one. Semantics from the field names, unverified.
        /// </summary>
        public void AutomaticMigration()
        {
            int best = -1; float bestD = float.MaxValue;
            HaloAiBody player = null;
            for (int s = 0; s < Squads.Length; s++)
            {
                if (!Squads[s].AutomaticMigrationTarget || !Data.Squads[s].Has(HaloAiScenario.SqAutomaticMigration)) continue;
                if (player == null)
                {
                    Vector3 c = Vector3.zero; int n = 0;
                    for (int i = 0; i < Actors.Count; i++) if (Actors[i].Alive && Data.Squads.Count > Actors[i].Squad && Actors[i].Squad >= 0 && Data.Squads[Actors[i].Squad].Has(HaloAiScenario.SqAutomaticMigration)) { c += Actors[i].Position; n++; }
                    if (n == 0) return;
                    player = HaloAiBodies.NearestPlayer(c / n);
                    if (player == null) return;
                }
                float d = SquadDistance(s, player.Feet);
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best < 0) return;
            int moved = 0;
            for (int i = 0; i < Actors.Count; i++)
            {
                var a = Actors[i];
                if (!a.Alive || a.Squad == best || a.Squad < 0 || a.Squad >= Data.Squads.Count || !Data.Squads[a.Squad].Has(HaloAiScenario.SqAutomaticMigration)) continue;
                if (a.Vehicle != null || a.BoardTarget != null) continue;
                float cur = SquadDistance(a.Squad, player.Feet);
                if (cur - bestD < 2f * HaloSpace.Unit) continue;         // ours: hysteresis
                a.MoveToSquad(this, best);
                Joined(a);
                moved++;
            }
            if (moved > 0 && (HaloAi.Verbose || !_migrationLogged))
            {
                _migrationLogged = true;
                HaloLog.Info("AI: " + Name + ": automatic migration of " + moved + " actors to " + Data.Squads[best].Name);
            }
        }

        bool _migrationLogged;

        /// <summary>Distance (m) from a point to the nearest firing position of the squad's groups (its start when it has none).</summary>
        float SquadDistance(int s, Vector3 p)
        {
            var sd = Data.Squads[s];
            uint mask = sd.Attacking | sd.AttackingGuard | sd.Defending | sd.DefendingGuard;
            float best = float.MaxValue;
            if (mask != 0)
                for (int i = 0; i < Positions.Length; i++) if (PositionInGroups(i, mask)) { float d = (Positions[i] - p).sqrMagnitude; if (d < best) best = d; }
            if (best == float.MaxValue)
                for (int i = 0; i < sd.Starts.Count; i++) { float d = (HaloSpace.Pos(sd.Starts[i].Position) - p).sqrMagnitude; if (d < best) best = d; }
            return best == float.MaxValue ? best : Mathf.Sqrt(best);
        }

        // ------------------------------------------------------------------ firing positions

        public bool PositionInGroups(int index, uint mask)
        {
            int g = Data.FiringPositions[index].Group;
            return g >= 0 && g < 26 && (mask & (1u << g)) != 0;
        }

        public bool HasPositions(uint mask)
        {
            if (mask == 0) return false;
            for (int i = 0; i < Positions.Length; i++) if (PositionInGroups(i, mask)) return true;
            return false;
        }

        int[] _posComp; HaloAiNav _compNav;

        /// <summary>The navigation island of a firing position (-1 off the navigation / no navigation), cached per BSP.</summary>
        public int PositionComponent(int index)
        {
            var nav = HaloAi.Nav;
            if (nav == null) return -1;
            if (_posComp == null || _compNav != nav)
            {
                _compNav = nav;
                _posComp = new int[Positions.Length];
                for (int i = 0; i < Positions.Length; i++) _posComp[i] = -2;
            }
            if (_posComp[index] == -2) _posComp[index] = ComponentAt(Positions[index]);
            return _posComp[index];
        }

        /// <summary>The navigation island under a Unity point (-1 none).</summary>
        public static int ComponentAt(Vector3 p)
        {
            var nav = HaloAi.Nav;
            if (nav == null) return -1;
            Vector3 h = HaloSpace.ToHalo(p);
            int poly = nav.Locate(h.x, h.y, h.z + 0.1f, 0.4f, 2f, 1f);
            return poly >= 0 ? nav.Component(poly) : -1;
        }

        public void Claim(int index) { if (index >= 0 && index < Claims.Length) Claims[index]++; }
        public void Release(int index) { if (index >= 0 && index < Claims.Length && Claims[index] > 0) Claims[index]--; }
    }
}

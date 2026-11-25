using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] The HaloScript ai_* functions (names as in Halo's script reference; "ai" arguments are "encounter",
    /// "encounter/squad" or "encounter/platoon"; object lists are lists of units). Anything not done yet logs once
    /// ("AI: unimplemented ...") and returns the type's default, as the campaign rules ask.
    /// </summary>
    public static partial class HaloAi
    {
        static void Each(string ai, System.Action<HaloActor> f)
        {
            var list = Selection(ai);
            // f may change the selection list (migrate): walk a copy
            Copy.Clear(); Copy.AddRange(list);
            for (int i = 0; i < Copy.Count; i++) f(Copy[i]);
        }

        static readonly List<HaloActor> Copy = new List<HaloActor>(32);

        static void EachEncounter(string ai, System.Action<HaloAiEncounter, int, int> f)
        {
            HaloAiEncounter enc; int squad, platoon;
            if (Resolve(ai, out enc, out squad, out platoon)) f(enc, squad, platoon);
        }

        // ---- placement / removal
        /// <summary>ai_spawn_actor: one actor of the encounter / squad.</summary>
        public static void SpawnActor(string ai)
        {
            EachEncounter(ai, (e, s, p) =>
            {
                int squad = s;
                if (squad < 0) for (int i = 0; i < e.Data.Squads.Count; i++) if (e.Data.Squads[i].Starts.Count > 0 && (p < 0 || e.Data.Squads[i].Platoon == p)) { squad = i; break; }
                if (squad >= 0) { e.Created = true; PlaceSquad(e, squad, 1, true); }
            });
        }

        public static void KillSilent(string ai) { var list = Selection(ai); for (int i = list.Count - 1; i >= 0; i--) if (list[i].Unit != null) list[i].Unit.KillSilent(); }
        public static void EraseAll() { for (int i = Actors.Count - 1; i >= 0; i--) Remove(Actors[i], true); }

        // ---- attach / detach / free
        /// <summary>ai_attach: a scripted unit becomes an actor of the encounter (squad 0 unless a squad is named).</summary>
        public static void Attach(HaloUnit unit, string ai)
        {
            if (unit == null) return;
            HaloAiEncounter enc; int squad, platoon;
            if (!Resolve(ai, out enc, out squad, out platoon)) return;
            var a = ActorOf(unit);
            if (a == null)
            {
                var v = Catalog.Variant(unit.ActorVariant);
                if (v == null && squad >= 0) v = Catalog.Variant(Data.PaletteVariant(enc.Data.Squads[squad].ActorType));
                if (v == null) for (int i = 0; i < enc.Data.Squads.Count && v == null; i++) v = Catalog.Variant(Data.PaletteVariant(enc.Data.Squads[i].ActorType));
                if (v == null) { Unimplemented("ai_attach of a unit without an actor variant"); return; }
                a = Wrap(unit, v);
            }
            if (enc.Team != HaloTeam.None) unit.Team = enc.Team;
            a.Free = false;
            a.MoveToSquad(enc, squad >= 0 ? squad : 0);
            enc.Created = true;
            enc.Joined(a);
            a.EnterAiState(squad >= 0 ? enc.Data.Squads[squad].InitialState : HaloAiState.Alert);
        }

        /// <summary>ai_attach_free: the unit gets a new free actor of the variant.</summary>
        public static void AttachFree(HaloUnit unit, HaloTag actorVariant)
        {
            if (unit == null || !Ensure()) return;
            var v = Catalog.Variant(actorVariant ?? unit.ActorVariant);
            if (v == null) { Unimplemented("ai_attach_free without an actor variant"); return; }
            var a = ActorOf(unit) ?? Wrap(unit, v);
            a.MoveToSquad(null, -1);
            a.Free = true;
            a.EnterAiState(HaloAiState.Alert);
        }

        /// <summary>ai_detach: the unit leaves all AI (it stands still, scripts drive it).</summary>
        public static void Detach(HaloUnit unit) { var a = ActorOf(unit); if (a != null) Remove(a, false); }

        /// <summary>ai_free: the actors leave their encounter and act on their own.</summary>
        public static void Free(string ai) { Each(ai, a => { a.MoveToSquad(null, -1); a.Free = true; }); }
        public static void FreeUnits(IList<HaloUnit> units) { ForUnits(units, a => { a.MoveToSquad(null, -1); a.Free = true; }); }

        static void ForUnits(IList<HaloUnit> units, System.Action<HaloActor> f)
        {
            if (units == null) return;
            for (int i = 0; i < units.Count; i++) { var a = ActorOf(units[i]); if (a != null) f(a); }
        }

        // ---- encounter switches
        public static void SetRespawn(string ai, bool on) { EachEncounter(ai, (e, s, p) => e.Respawn = on); }
        public static void SetDeaf(string ai, bool on) { EachEncounter(ai, (e, s, p) => { if (s < 0 && p < 0) e.Deaf = on; }); Each(ai, a => a.Deaf = on); }
        public static void SetBlind(string ai, bool on) { EachEncounter(ai, (e, s, p) => { if (s < 0 && p < 0) e.Blind = on; }); Each(ai, a => a.Blind = on); }
        public static void Braindead(string ai, bool on) { EachEncounter(ai, (e, s, p) => { if (s < 0 && p < 0) e.Braindead = on; }); Each(ai, a => { a.Braindead = on; if (!on) a.EnterAiState(a.InitialState); }); }
        public static void BraindeadByUnit(IList<HaloUnit> units, bool on) { ForUnits(units, a => { a.Braindead = on; if (!on) a.EnterAiState(a.InitialState); }); }
        public static void ForceActive(string ai, bool on) { EachEncounter(ai, (e, s, p) => e.ForceActive = on); }
        public static void ForceActiveByUnit(HaloUnit u, bool on) { var a = ActorOf(u); if (a != null) a.ForceActive = on; }
        public static void Playfight(string ai, bool on) { EachEncounter(ai, (e, s, p) => e.Playfight = on); }
        public static void AllowCharge(string ai, bool on) { Each(ai, a => a.AllowCharge = on); }
        public static void AllowDormant(string ai, bool on) { EachEncounter(ai, (e, s, p) => e.AllowDormant = on); }
        public static void Berserk(string ai, bool on) { Each(ai, a => { if (on) a.GoBerserk("script"); else a.Berserking = false; }); }
        public static void SetTeam(string ai, HaloTeam team) { EachEncounter(ai, (e, s, p) => { if (s < 0 && p < 0) e.Team = team; }); Each(ai, a => { if (a.Unit != null) a.Unit.Team = team; }); }
        public static void LinkActivation(string ai, string other) { HaloAiEncounter b; int s, p; if (Resolve(other, out b, out s, out p)) EachEncounter(ai, (e, s2, p2) => e.LinkedTo = b); }
        public static void SelectEncounter(string ai) { }
        public static void Deselect() { }
        public static void Reconnect() { }
        public static void DialogueTriggersOn(bool on) { DialogueTriggers = on; }
        public static void GrenadesOn(bool on) { Grenades = on; }

        // ---- awareness
        public static void MagicallySeeEncounter(string ai, string other)
        {
            var targets = new List<HaloActor>(Selection(other));
            if (targets.Count == 0) return;
            Each(ai, a => { var t = targets[Rng.Next(targets.Count)]; if (t.Body != null) a.MagicallySee(t.Body); });
        }

        public static void MagicallySeePlayers(string ai) { Each(ai, a => a.MagicallySeePlayers()); }
        public static void MagicallySeeUnit(string ai, HaloUnit unit) { var b = HaloAiBodies.Of(unit); if (b != null) Each(ai, a => a.MagicallySee(b)); }

        // ---- squads' timers
        public static void TimerStart(string ai) { EachEncounter(ai, (e, s, p) => { for (int i = 0; i < e.Squads.Length; i++) if ((s < 0 || i == s) && (p < 0 || e.Data.Squads[i].Platoon == p)) e.StartTimer(i, Time); }); }
        public static void TimerExpire(string ai) { EachEncounter(ai, (e, s, p) => { for (int i = 0; i < e.Squads.Length; i++) if ((s < 0 || i == s) && (p < 0 || e.Data.Squads[i].Platoon == p)) e.ExpireTimer(i); }); }

        // ---- platoons
        static void Platoons(string ai, System.Action<HaloAiEncounter, int> f)
        {
            EachEncounter(ai, (e, s, p) =>
            {
                if (p >= 0) { f(e, p); return; }
                if (s >= 0) { int pp = e.Data.Squads[s].Platoon; if (pp >= 0) f(e, pp); return; }
                for (int i = 0; i < e.Platoons.Length; i++) f(e, i);
            });
        }

        public static void Attack(string ai) { Platoons(ai, (e, p) => e.Platoons[p].Defending = false); }
        public static void Defend(string ai) { Platoons(ai, (e, p) => e.Platoons[p].Defending = true); }
        /// <summary>ai_retreat: a squad argument moves only that squad into its "maneuver to" squad (a30 retreats wave_1_lz_grunt 3 s
        /// before the rest of its platoon); platoon and encounter arguments move the platoons.</summary>
        public static void Retreat(string ai) { ManeuverOrder(ai, "ai_retreat " + ai); }
        /// <summary>ai_maneuver: as ai_retreat.</summary>
        public static void Maneuver(string ai) { ManeuverOrder(ai, "ai_maneuver " + ai); }

        static void ManeuverOrder(string ai, string what)
        {
            HaloAiEncounter e; int s, p;
            if (!Resolve(ai, out e, out s, out p)) return;
            string log = FirstPlace("#order " + what) || Verbose ? what : null;
            if (s >= 0) { e.Maneuver(e.Data.Squads[s].Platoon, s, false, log); return; }
            for (int i = 0; i < e.Platoons.Length; i++)
                if (p < 0 || i == p) e.Maneuver(i, -1, false, log != null ? log + " (platoon " + e.Data.Platoons[i].Name + ")" : null);
        }
        public static void ManeuverEnable(string ai, bool on) { EachEncounter(ai, (e, s, p) => { if (p >= 0) e.Platoons[p].ManeuverEnabled = on; else e.ManeuverEnabled = on; }); }

        public static bool IsAttacking(string ai)
        {
            bool any = false;
            Platoons(ai, (e, p) => { if (!e.Platoons[p].Defending) any = true; });
            return any;
        }

        // ---- migration
        /// <summary>ai_migrate: the actors join another encounter / squad (its firing positions, team and rules).</summary>
        public static void Migrate(string from, string to)
        {
            HaloAiEncounter enc; int squad, platoon;
            if (!Resolve(to, out enc, out squad, out platoon)) return;
            int target = squad;
            if (target < 0 && platoon >= 0) for (int i = 0; i < enc.Data.Squads.Count; i++) if (enc.Data.Squads[i].Platoon == platoon) { target = i; break; }
            int moved = 0;
            Each(from, a =>
            {
                int sq = target >= 0 ? target : (a.Squad >= 0 && a.Squad < enc.Data.Squads.Count && a.Enc == enc ? a.Squad : 0);
                if (sq >= enc.Data.Squads.Count) sq = enc.Data.Squads.Count - 1;
                a.MoveToSquad(enc, sq);
                enc.Joined(a);
                if (enc.Team != HaloTeam.None && a.Unit != null) a.Unit.Team = enc.Team;
                moved++;
            });
            enc.Created = true;
            if (Verbose) HaloLog.Info("AI: ai_migrate " + from + " -> " + to + " (" + moved + ")");
        }

        public static void MigrateAndSpeak(string from, string to, string speech) { Migrate(from, to); }

        public static void MigrateByUnit(IList<HaloUnit> units, string to)
        {
            HaloAiEncounter enc; int squad, platoon;
            if (!Resolve(to, out enc, out squad, out platoon)) return;
            ForUnits(units, a => { a.MoveToSquad(enc, squad >= 0 ? squad : 0); enc.Joined(a); });
        }

        public static void AutomaticMigrationTarget(string ai, bool on) { EachEncounter(ai, (e, s, p) => { for (int i = 0; i < e.Squads.Length; i++) if (s < 0 || i == s) e.Squads[i].AutomaticMigrationTarget = on; }); }

        // ---- allegiance
        public static void Allegiance(HaloTeam a, HaloTeam b) { HaloAllegiance.Set(a, b, true); }
        public static void AllegianceRemove(HaloTeam a, HaloTeam b) { if (a != b) HaloAllegiance.Set(a, b, false); }
        public static bool AllegianceBroken(HaloTeam a, HaloTeam b) { return false; }

        // ---- counts
        public static float LivingFraction(string ai)
        {
            HaloAiEncounter enc; int squad, platoon;
            if (!Resolve(ai, out enc, out squad, out platoon)) return 0f;
            int total = 0, living = 0;
            for (int i = 0; i < enc.Actors.Count; i++) { var a = enc.Actors[i]; if (!HaloAiEncounter.Matches(a, squad, platoon)) continue; total++; if (a.Alive) living++; }
            return total > 0 ? (float)living / total : 0f;
        }

        public static float Strength(string ai)
        {
            HaloAiEncounter enc; int squad, platoon;
            if (!Resolve(ai, out enc, out squad, out platoon)) return 0f;
            int total = 0; float sum = 0f;
            for (int i = 0; i < enc.Actors.Count; i++) { var a = enc.Actors[i]; if (!HaloAiEncounter.Matches(a, squad, platoon)) continue; total++; sum += a.Strength; }
            return total > 0 ? sum / total : 0f;
        }

        public static int SwarmCount(string ai) { int n = 0; foreach (var a in Selection(ai)) if (a.A != null && a.A.IsSwarm) n++; return n; }
        public static int NonswarmCount(string ai) { int n = 0; foreach (var a in Selection(ai)) if (a.A == null || !a.A.IsSwarm) n++; return n; }

        /// <summary>ai_status: 0 inactive, 1 noncombat, 2 guarding, 3 search / suspicious, 4 definite enemy (heard / magic), 5 visible enemy, 6 engaging in combat.</summary>
        public static int Status(string ai)
        {
            int best = 0;
            foreach (var a in Selection(ai))
            {
                int s;
                switch (a.State)
                {
                    case HaloActor.Mode.Combat: case HaloActor.Mode.Berserk: case HaloActor.Mode.Hide: s = a.TargetVisible ? 6 : 4; break;
                    case HaloActor.Mode.Search: s = 3; break;
                    case HaloActor.Mode.Flee: case HaloActor.Mode.Cower: s = a.Target != null ? 5 : 3; break;
                    default: s = a.InitialState == HaloAiState.Guarding || a.InitialState == HaloAiState.GuardingAtGuardPosition || a.InitialState == HaloAiState.Alert ? 2 : 1; break;
                }
                if (s > best) best = s;
            }
            return best;
        }

        // ---- states
        public static void SetReturnState(string ai, HaloAiState s) { Each(ai, a => a.ReturnState = s); }
        public static void SetCurrentState(string ai, HaloAiState s) { Each(ai, a => a.EnterAiState(s)); }

        public static void TeleportToStartingLocation(string ai) { Each(ai, a => TeleportHome(a)); }
        public static void TeleportToStartingLocationIfUnsupported(string ai) { Each(ai, a => { if (a.Unit != null && !a.Unit.Grounded && !a.Flying) TeleportHome(a); }); }

        static void TeleportHome(HaloActor a)
        {
            if (a.Unit == null) return;
            var sq = a.Enc != null && a.Squad >= 0 && a.Squad < a.Enc.Data.Squads.Count ? a.Enc.Data.Squads[a.Squad] : null;
            Vector3 p = a.Home; float yaw = a.HomeYaw;
            if (sq != null && sq.Starts.Count > 0) { var st = sq.Starts[Rng.Next(sq.Starts.Count)]; p = HaloSpace.Pos(st.Position); yaw = 90f - st.Facing * Mathf.Rad2Deg; }
            a.Unit.Teleport(p, yaw);
            a.Unit.Settle();
            a.Stop();
        }

        /// <summary>ai_renew: health, shields and grenades as new.</summary>
        public static void Renew(string ai)
        {
            Each(ai, a =>
            {
                if (a.Unit == null) return;
                a.Unit.Body = 1f;
                if (a.Unit.MaxShield > 0f) a.Unit.Shield = 1f;
                if (a.V != null) a.Unit.Grenades = Mathf.Max(a.Unit.Grenades, a.V.GrenadeCountMax);
            });
        }

        // ---- targets
        public static void TryToFightNothing(string ai) { Each(ai, a => { a.FightAi = null; a.FightPlayer = false; }); }
        public static void TryToFight(string ai, string other) { Each(ai, a => { a.FightAi = other; a.FightPlayer = false; }); }
        public static void TryToFightPlayer(string ai) { Each(ai, a => { a.FightPlayer = true; a.FightAi = null; }); }
        public static void Disregard(IList<HaloUnit> units, bool on) { if (units != null) for (int i = 0; i < units.Count; i++) HaloAiBodies.SetDisregard(units[i], on); }
        public static void PreferTarget(IList<HaloUnit> units, bool on) { if (units != null) for (int i = 0; i < units.Count; i++) HaloAiBodies.SetPrefer(units[i], on); }
        /// <summary>ai_disregard / ai_prefer_target with the players in the object list.</summary>
        public static void DisregardPlayers(bool on) { HaloAiBodies.SetDisregardPlayers(on); }
        public static void PreferPlayers(bool on) { HaloAiBodies.SetPreferPlayers(on); }

        public static void LookAtObject(HaloUnit unit, GameObject obj) { var a = ActorOf(unit); if (a != null) a.LookAtObject = obj; }
        public static void StopLooking(HaloUnit unit) { var a = ActorOf(unit); if (a != null) a.LookAtObject = null; }

        // ---- command lists
        public static void CommandList(string ai, string list)
        {
            if (!Ensure()) return;
            var cl = Data.FindCommandList(list);
            if (cl == null) { Unimplemented("unknown command list '" + list + "'"); return; }
            Each(ai, a => a.RunCommandList(cl));
        }

        public static void CommandListByUnit(HaloUnit unit, string list)
        {
            if (!Ensure()) return;
            var cl = Data.FindCommandList(list);
            var a = ActorOf(unit);
            if (a == null && unit != null) { AttachFree(unit, unit.ActorVariant); a = ActorOf(unit); }
            if (a != null && cl != null) a.RunCommandList(cl);
        }

        public static void CommandListAdvance(string ai) { Each(ai, a => { if (a.Commands != null) a.Commands.Advance(); }); }
        public static void CommandListAdvanceByUnit(HaloUnit unit) { var a = ActorOf(unit); if (a != null && a.Commands != null) a.Commands.Advance(); }

        /// <summary>ai_command_list_status over units: 3 when any still runs, else 2 when any waits, else 1 when any finished, else 0.</summary>
        public static int CommandListStatus(IList<HaloUnit> units)
        {
            int best = 0;
            if (units == null) return 0;
            for (int i = 0; i < units.Count; i++)
            {
                var a = ActorOf(units[i]);
                int s = a != null ? a.CommandStatus : 0;
                int rank = s == 3 ? 3 : s == 2 ? 2 : s == 1 ? 1 : 0;
                if (rank > best) best = rank;
            }
            return best;
        }

        // ---- following
        /// <summary>ai_follow_target_*: the squad form sets that squad only (a50 muster_bay_marines/squad_a), the encounter form every squad
        /// (platoon form: its squads).</summary>
        public static void FollowTargetDisable(string ai) { SetFollow(ai, false, null, null); }
        public static void FollowTargetPlayers(string ai) { SetFollow(ai, true, null, null); }
        public static void FollowTargetUnit(string ai, HaloUnit unit) { SetFollow(ai, false, unit, null); }
        public static void FollowTargetAi(string ai, string other) { SetFollow(ai, false, null, other); }

        static void SetFollow(string ai, bool players, HaloUnit unit, string other)
        {
            HaloAiEncounter e; int s, p;
            if (!Resolve(ai, out e, out s, out p)) return;
            if (s >= 0) e.SetFollow(s, players, unit, other);
            else if (p >= 0) { for (int i = 0; i < e.Squads.Length; i++) if (e.Data.Squads[i].Platoon == p) e.SetFollow(i, players, unit, other); }
            else e.SetFollow(-1, players, unit, other);
        }
        public static void FollowDistance(string ai, float worldUnits) { EachEncounter(ai, (e, s, p) => e.FollowDistance = Mathf.Max(0.3f, worldUnits)); }

        // ---- game_all_quiet / game_safe_to_speak / object_set_*_attack_inhibited

        /// <summary>game_all_quiet: no actor hostile to the players is fighting (combat / search / berserk / hide with a target) within
        /// 30 m of a player (ours: the distance).</summary>
        public static bool AllQuiet
        {
            get
            {
                for (int i = 0; i < Actors.Count; i++)
                {
                    var a = Actors[i];
                    if (!a.Alive || a.Target == null) continue;
                    if (a.State != HaloActor.Mode.Combat && a.State != HaloActor.Mode.Search && a.State != HaloActor.Mode.Berserk && a.State != HaloActor.Mode.Hide) continue;
                    if (!HaloAllegiance.AreEnemies(a.Team, HaloTeam.Player) && !a.Target.IsPlayer) continue;
                    var p = HaloAiBodies.NearestPlayer(a.Position);
                    if (p != null && (p.Feet - a.Position).sqrMagnitude < 30f * 30f) return false;
                }
                return true;
            }
        }

        /// <summary>game_safe_to_speak: no AI conversation is playing its lines.</summary>
        public static bool SafeToSpeak
        {
            get
            {
                for (int i = 0; i < ConversationTick.Count; i++) { int s = ConversationTick[i].Status; if (s == 3 || s == 4) return false; }
                return true;
            }
        }

        public static void SetMeleeInhibited(GameObject obj, bool on) { var a = ActorOf(obj != null ? obj.GetComponent<HaloUnit>() : null); if (a != null) a.MeleeInhibited = on; }
        public static void SetRangedInhibited(GameObject obj, bool on) { var a = ActorOf(obj != null ? obj.GetComponent<HaloUnit>() : null); if (a != null) a.RangedInhibited = on; }

        // ---- vehicles (not yet: logged once, defaults returned)
        /// <summary>ai_go_to_vehicle: the actors walk to free seats of the vehicle whose label contains <paramref name="seat"/> ("" = any) and get in.</summary>
        public static void GoToVehicle(string ai, GameObject vehicle, string seat) { GoTo(ai, vehicle, seat, false); }
        /// <summary>ai_go_to_vehicle_override: as GoToVehicle, replacing what they were boarding.</summary>
        public static void GoToVehicleOverride(string ai, GameObject vehicle, string seat) { GoTo(ai, vehicle, seat, true); }

        static void GoTo(string ai, GameObject vehicle, string seat, bool over)
        {
            var v = vehicle != null ? vehicle.GetComponentInParent<HaloVehicle>() : null;
            if (v == null) { Unimplemented("ai_go_to_vehicle on a non-vehicle"); return; }
            int n = 0;
            Each(ai, a => { if (a.Vehicle == null && a.GoToVehicle(v, seat ?? "", over)) n++; });
            if (Verbose) HaloLog.Info("AI: ai_go_to_vehicle " + ai + " -> " + v.name + " (" + (seat ?? "") + "): " + n + " boarding");
        }

        /// <summary>ai_going_to_vehicle: actors still walking to the vehicle.</summary>
        public static int GoingToVehicle(GameObject vehicle)
        {
            var v = vehicle != null ? vehicle.GetComponentInParent<HaloVehicle>() : null;
            int n = 0;
            if (v != null) for (int i = 0; i < Actors.Count; i++) if (Actors[i].Alive && Actors[i].BoardTarget == v) n++;
            return n;
        }

        public static void ExitVehicle(string ai) { Each(ai, a => a.ExitVehicle()); }
        public static void VehicleEncounter(GameObject vehicle, string ai) { Unimplemented("ai_vehicle_encounter"); }
        public static void VehicleEnterableDistance(GameObject vehicle, float d) { Unimplemented("ai_vehicle_enterable_distance"); }
        public static void VehicleEnterableTeam(GameObject vehicle, HaloTeam t) { Unimplemented("ai_vehicle_enterable_team"); }
        public static void VehicleEnterableActorType(GameObject vehicle, int actorType) { Unimplemented("ai_vehicle_enterable_actor_type"); }
        public static void VehicleEnterableActors(GameObject vehicle, string ai) { Unimplemented("ai_vehicle_enterable_actors"); }
        public static void VehicleEnterableDisable(GameObject vehicle) { }
    }
}

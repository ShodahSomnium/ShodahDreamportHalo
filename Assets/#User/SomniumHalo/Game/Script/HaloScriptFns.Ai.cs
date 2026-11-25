using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] ai_*: HaloAi (I) by "encounter" / "encounter/squad" / "encounter/platoon" names, units of object lists,
    /// HaloAllegiance.</summary>
    public static partial class HaloScriptFns
    {
        static void BindAi(HsVm vm)
        {
            vm.Bind("ai_place", AiPlace);
            vm.Bind("ai_spawn_actor", a => { HaloAi.SpawnActor(Ai(a, 0)); return 0; });
            vm.Bind("ai_kill", AiKill);
            vm.Bind("ai_kill_silent", a => { HaloAi.KillSilent(Ai(a, 0)); return 0; });
            vm.Bind("ai_erase", AiErase);
            vm.Bind("ai_erase_all", a => { HaloAi.EraseAll(); return 0; });
            vm.Bind("ai_living_count", AiLivingCount);
            vm.Bind("ai_living_fraction", a => R(HaloAi.LivingFraction(Ai(a, 0))));
            vm.Bind("ai_strength", a => R(HaloAi.Strength(Ai(a, 0))));
            vm.Bind("ai_nonswarm_count", a => HaloAi.NonswarmCount(Ai(a, 0)));
            vm.Bind("ai_swarm_count", a => HaloAi.SwarmCount(Ai(a, 0)));
            vm.Bind("ai_status", a => HaloAi.Status(Ai(a, 0)));
            vm.Bind("ai_actors", AiActors);
            vm.Bind("ai", AiEnable);
            vm.Bind("ai_allegiance", a => { HaloAi.Allegiance(Team(a, 0), Team(a, 1)); return 0; });
            vm.Bind("ai_allegiance_remove", a => { HaloAi.AllegianceRemove(Team(a, 0), Team(a, 1)); return 0; });
            vm.Bind("ai_allegiance_broken", a => B(HaloAi.AllegianceBroken(Team(a, 0), Team(a, 1))));

            vm.Bind("ai_attach", a => { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null) HaloAi.Attach(u, Ai(a, 1)); return 0; });
            vm.Bind("ai_attach_free", a => { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null) HaloAi.AttachFree(u, a.Tag(1)); return 0; });
            vm.Bind("ai_detach", a => { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null) HaloAi.Detach(u); return 0; });
            vm.Bind("ai_free", a => { HaloAi.Free(Ai(a, 0)); return 0; });
            vm.Bind("ai_free_units", a => { HaloAi.FreeUnits(UnitsOf(a, 0)); return 0; });

            vm.Bind("ai_magically_see_players", a => { HaloAi.MagicallySeePlayers(Ai(a, 0)); return 0; });
            vm.Bind("ai_magically_see_encounter", a => { HaloAi.MagicallySeeEncounter(Ai(a, 0), Ai(a, 1)); return 0; });
            vm.Bind("ai_magically_see_unit", a => { var u = HaloScriptWorld.Unit(a.Obj(1)); if (u != null) HaloAi.MagicallySeeUnit(Ai(a, 0), u); return 0; });

            vm.Bind("ai_command_list", a => { HaloAi.CommandList(Ai(a, 0), a.Str(1)); return 0; });
            vm.Bind("ai_command_list_by_unit", a => { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null) HaloAi.CommandListByUnit(u, a.Str(1)); return 0; });
            vm.Bind("ai_command_list_advance", a => { HaloAi.CommandListAdvance(Ai(a, 0)); return 0; });
            vm.Bind("ai_command_list_advance_by_unit", a => { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null) HaloAi.CommandListAdvanceByUnit(u); return 0; });
            vm.Bind("ai_command_list_status", a => HaloAi.CommandListStatus(UnitsOf(a, 0)));

            vm.Bind("ai_migrate", a => { HaloAi.Migrate(Ai(a, 0), Ai(a, 1)); return 0; });
            vm.Bind("ai_migrate_and_speak", a => { HaloAi.MigrateAndSpeak(Ai(a, 0), Ai(a, 1), a.Str(2)); return 0; });
            vm.Bind("ai_migrate_by_unit", a => { HaloAi.MigrateByUnit(UnitsOf(a, 0), Ai(a, 1)); return 0; });
            vm.Bind("ai_automatic_migration_target", a => { HaloAi.AutomaticMigrationTarget(Ai(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_attack", a => { HaloAi.Attack(Ai(a, 0)); return 0; });
            vm.Bind("ai_defend", a => { HaloAi.Defend(Ai(a, 0)); return 0; });
            vm.Bind("ai_retreat", a => { HaloAi.Retreat(Ai(a, 0)); return 0; });
            vm.Bind("ai_maneuver", a => { HaloAi.Maneuver(Ai(a, 0)); return 0; });
            vm.Bind("ai_maneuver_enable", a => { HaloAi.ManeuverEnable(Ai(a, 0), a.Bool(1)); return 0; });

            vm.Bind("ai_follow_target_players", a => { HaloAi.FollowTargetPlayers(Ai(a, 0)); return 0; });
            vm.Bind("ai_follow_target_disable", a => { HaloAi.FollowTargetDisable(Ai(a, 0)); return 0; });
            vm.Bind("ai_follow_target_ai", a => { HaloAi.FollowTargetAi(Ai(a, 0), Ai(a, 1)); return 0; });
            vm.Bind("ai_follow_target_unit", a => { var u = HaloScriptWorld.Unit(a.Obj(1)); HaloAi.FollowTargetUnit(Ai(a, 0), u); return 0; });
            vm.Bind("ai_follow_distance", a => { HaloAi.FollowDistance(Ai(a, 0), a.Real(1)); return 0; });

            vm.Bind("ai_braindead", a => { HaloAi.Braindead(Ai(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_braindead_by_unit", a => { HaloAi.BraindeadByUnit(UnitsOf(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_force_active", a => { HaloAi.ForceActive(Ai(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_force_active_by_unit", a => { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null) HaloAi.ForceActiveByUnit(u, a.Bool(1)); return 0; });
            vm.Bind("ai_playfight", a => { HaloAi.Playfight(Ai(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_allow_charge", a => { HaloAi.AllowCharge(Ai(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_allow_dormant", a => { HaloAi.AllowDormant(Ai(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_berserk", a => { HaloAi.Berserk(Ai(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_set_blind", a => { HaloAi.SetBlind(Ai(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_set_deaf", a => { HaloAi.SetDeaf(Ai(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_set_respawn", a => { HaloAi.SetRespawn(Ai(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_set_team", a => { HaloAi.SetTeam(Ai(a, 0), Team(a, 1)); return 0; });
            vm.Bind("ai_link_activation", a => { HaloAi.LinkActivation(Ai(a, 0), Ai(a, 1)); return 0; });
            vm.Bind("ai_renew", a => { HaloAi.Renew(Ai(a, 0)); return 0; });
            vm.Bind("ai_reconnect", a => { HaloAi.Reconnect(); return 0; });
            vm.Bind("ai_timer_start", a => { HaloAi.TimerStart(Ai(a, 0)); return 0; });
            vm.Bind("ai_timer_expire", a => { HaloAi.TimerExpire(Ai(a, 0)); return 0; });
            vm.Bind("ai_teleport_to_starting_location", a => { HaloAi.TeleportToStartingLocation(Ai(a, 0)); return 0; });
            vm.Bind("ai_teleport_to_starting_location_if_unsupported", a => { HaloAi.TeleportToStartingLocationIfUnsupported(Ai(a, 0)); return 0; });

            vm.Bind("ai_try_to_fight", a => { HaloAi.TryToFight(Ai(a, 0), Ai(a, 1)); return 0; });
            vm.Bind("ai_try_to_fight_player", a => { HaloAi.TryToFightPlayer(Ai(a, 0)); return 0; });
            vm.Bind("ai_try_to_fight_nothing", a => { HaloAi.TryToFightNothing(Ai(a, 0)); return 0; });
            vm.Bind("ai_disregard", a => { HaloAi.Disregard(UnitsOf(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_prefer_target", a => { HaloAi.PreferTarget(UnitsOf(a, 0), a.Bool(1)); return 0; });
            vm.Bind("ai_dialogue_triggers", a => { HaloAi.DialogueTriggersOn(a.Bool(0)); return 0; });
            vm.Bind("ai_grenades", a => { HaloAi.GrenadesOn(a.Bool(0)); return 0; });

            vm.Bind("ai_conversation", a => B(HaloAi.Conversation(a.Str(0))));
            vm.Bind("ai_conversation_stop", a => { HaloAi.ConversationStop(a.Str(0)); return 0; });
            vm.Bind("ai_conversation_advance", a => { HaloAi.ConversationAdvance(a.Str(0)); return 0; });
            vm.Bind("ai_conversation_line", a => HaloAi.ConversationLine(a.Str(0)));
            vm.Bind("ai_conversation_status", a => HaloAi.ConversationStatus(a.Str(0)));

            vm.Bind("ai_go_to_vehicle", a => { HaloAi.GoToVehicle(Ai(a, 0), HaloScriptWorld.Get(a.Obj(1)), a.Str(2)); return 0; });
            vm.Bind("ai_going_to_vehicle", a => HaloAi.GoingToVehicle(HaloScriptWorld.Get(a.Obj(0))));
            vm.Bind("ai_exit_vehicle", a => { HaloAi.ExitVehicle(Ai(a, 0)); return 0; });
            vm.Bind("ai_vehicle_encounter", a => { HaloAi.VehicleEncounter(HaloScriptWorld.Get(a.Obj(0)), Ai(a, 1)); return 0; });
            vm.Bind("ai_vehicle_enterable_distance", a => { HaloAi.VehicleEnterableDistance(HaloScriptWorld.Get(a.Obj(0)), a.Real(1)); return 0; });
            vm.Bind("ai_vehicle_enterable_team", a => { HaloAi.VehicleEnterableTeam(HaloScriptWorld.Get(a.Obj(0)), Team(a, 1)); return 0; });
            vm.Bind("ai_vehicle_enterable_actor_type", a => { HaloAi.VehicleEnterableActorType(HaloScriptWorld.Get(a.Obj(0)), a.Int(1)); return 0; });
            vm.Bind("ai_vehicle_enterable_actors", a => { HaloAi.VehicleEnterableActors(HaloScriptWorld.Get(a.Obj(0)), Ai(a, 1)); return 0; });
            vm.Bind("ai_vehicle_enterable_disable", a => { HaloAi.VehicleEnterableDisable(HaloScriptWorld.Get(a.Obj(0))); return 0; });
        }

        static string Ai(HsArgs a, int i) => a.Str(i);
        static HaloTeam Team(HsArgs a, int i) { int t = a.Int(i); return t >= 0 && t <= 7 ? (HaloTeam)t : HaloTeam.None; }

        static readonly List<HaloUnit> _units = new List<HaloUnit>();
        /// <summary>The units of an object list argument (a shared list: use it before the next call).</summary>
        static List<HaloUnit> UnitsOf(HsArgs a, int i)
        {
            _units.Clear();
            int l = ListOf(a, i), n = Count(l);
            for (int k = 0; k < n; k++) { var u = HaloScriptWorld.Unit(At(l, k)); if (u != null) _units.Add(u); }
            return _units;
        }

        static int AiPlace(HsArgs a) { HaloAi.Place(Ai(a, 0)); return 0; }
        static int AiKill(HsArgs a) { HaloAi.Kill(Ai(a, 0)); return 0; }
        static int AiErase(HsArgs a) { HaloAi.Erase(Ai(a, 0)); return 0; }
        static int AiLivingCount(HsArgs a) => HaloAi.LivingCount(Ai(a, 0));

        static int AiActors(HsArgs a)
        {
            int l = NewList();
            var units = HaloAi.Units(Ai(a, 0));
            if (units == null) return l;
            for (int i = 0; i < units.Count; i++) if (units[i] != null && units[i].Alive) Add(l, HaloScriptWorld.Handle(units[i].gameObject));
            return l;
        }

        static int AiEnable(HsArgs a) { HaloAi.Enabled = a.Bool(0); return 0; }
    }
}

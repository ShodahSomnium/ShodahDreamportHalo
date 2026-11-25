using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] game_*, switch_bsp, volume_test_*, players: HaloCampaign (K) and the trigger volumes of the scenario.</summary>
    public static partial class HaloScriptFns
    {
        static bool _reverted;
        static void OnReverted() { _reverted = true; }

        static void BindGame(HsVm vm)
        {
            vm.Bind("game_save", GameSave);
            vm.Bind("game_save_no_timeout", GameSaveNoTimeout);
            vm.Bind("game_save_totally_unsafe", GameSaveUnsafe);
            vm.Bind("game_save_cancel", GameSaveCancel);
            vm.Bind("game_saving", GameSaving);
            vm.Bind("game_revert", GameRevert);
            vm.Bind("game_reverted", GameReverted);
            vm.Bind("game_won", GameWon);
            vm.Bind("game_lost", GameLost);
            vm.Bind("game_safe_to_save", GameSafeToSave);


            vm.Bind("switch_bsp", SwitchBsp);
            vm.Bind("structure_bsp_index", StructureBspIndex);
            vm.Bind("players", Players);
            vm.Bind("volume_test_object", VolumeTestObject);
            vm.Bind("volume_test_objects", VolumeTestObjects);
            vm.Bind("volume_test_objects_all", VolumeTestObjectsAll);
            vm.Bind("volume_teleport_players_not_inside", VolumeTeleportPlayersNotInside);
        }

        static int GameSave(HsArgs a) { _reverted = false; HaloCampaign.Save(); return 0; }
        static int GameSaveNoTimeout(HsArgs a) { _reverted = false; HaloCampaign.SaveNoTimeout(); return 0; }
        static int GameSaveUnsafe(HsArgs a) { _reverted = false; HaloCampaign.SaveTotallyUnsafe(); return 0; }
        static int GameSaveCancel(HsArgs a) { HaloCampaign.SaveCancel(); return 0; }
        static int GameSaving(HsArgs a) => B(HaloCampaign.Saving);
        static int GameRevert(HsArgs a) { HaloCampaign.Revert(); return 0; }
        /// <summary>True after a checkpoint revert until the next save (cinematic_skip_start: a skipped cutscene reverts to the save
        /// taken just before it, and the script then skips the cutscene).</summary>
        static int GameReverted(HsArgs a) => B(_reverted);
        static int GameWon(HsArgs a) { Once("won", "Script: game_won"); HaloCampaign.End(true); return 0; }
        static int GameLost(HsArgs a) { Once("lost", "Script: game_lost"); HaloCampaign.End(false); return 0; }
        static int GameSafeToSave(HsArgs a) => B(HaloCampaign.SafeToSave);

        static int SwitchBsp(HsArgs a)
        {
            int i = a.Int(0);
            if (i != HaloCampaign.BspIndex) HaloCampaign.SwitchBsp(i);
            return 0;
        }
        static int StructureBspIndex(HsArgs a) => HaloCampaign.BspIndex;

        static int Players(HsArgs a)
        {
            int l = NewList();
            if (HaloScriptWorld.PlayerCount > 0) Add(l, HaloScriptWorld.PlayerBase);
            return l;
        }

        static int VolumeTestObject(HsArgs a) => B(HaloScriptWorld.ObjectInVolume(a.Int(0), a.Obj(1)));

        static int VolumeTestObjects(HsArgs a)
        {
            int v = a.Int(0), l = ListOf(a, 1), n = Count(l);
            for (int i = 0; i < n; i++) if (HaloScriptWorld.ObjectInVolume(v, At(l, i))) return 1;
            return 0;
        }

        static int VolumeTestObjectsAll(HsArgs a)
        {
            int v = a.Int(0), l = ListOf(a, 1), n = Count(l), live = 0;
            for (int i = 0; i < n; i++)
            {
                int h = At(l, i);
                if (!HaloScriptWorld.Exists(h)) continue;
                live++;
                if (!HaloScriptWorld.ObjectInVolume(v, h)) return 0;
            }
            return B(live > 0);
        }

        /// <summary>Players outside the volume are moved to the cutscene flag (Halo: catches stragglers before a BSP switch / cutscene).</summary>
        static int VolumeTeleportPlayersNotInside(HsArgs a)
        {
            int v = a.Int(0);
            if (HaloScriptWorld.PlayerCount == 0 || HaloScriptWorld.ObjectInVolume(v, HaloScriptWorld.PlayerBase)) return 0;
            Vector3 p; float yaw;
            if (!HaloScriptWorld.FlagPose(a.Int(1), out p, out yaw)) return 0;
            if (HaloCinematic.DeferPlayerTeleport(p, yaw, false)) return 0;
            string how;
            HaloPlayer.Teleport(p, yaw, out how);
            Once("vtp", "Script: volume_teleport_players_not_inside moved the player to " + a.Str(1) + " (" + how + ")");
            return 0;
        }
    }
}

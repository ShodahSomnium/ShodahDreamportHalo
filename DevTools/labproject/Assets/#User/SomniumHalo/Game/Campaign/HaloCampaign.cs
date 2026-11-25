using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Mission module, K] The campaign session: which mission (a10 .. d40), the difficulty, the active structure BSP, checkpoints and the
    /// mission's end. Started from the hub / tablet campaign page (Front calls <see cref="StartMission"/>; the instance master's choice is
    /// carried by Net for co-op). The campaign reuses HaloSession (phase Hub / Loading / Playing / Ending, Map, Scenario, WorldRoot) with
    /// <see cref="Active"/> true, so the multiplayer modules know a campaign map is up.
    /// The flow is in HaloCampaign.Load.cs (loading, world, objects, player, scripts, leave) and HaloCampaign.Tick.cs (the fixed 30 Hz game
    /// tick driving HaloScript / AI / devices / trigger volumes, checkpoints, death and revert, the mission's end).
    /// Player.log: "Campaign: ..." lines.
    /// </summary>
    public static partial class HaloCampaign
    {
        public enum Difficulty { Easy = 0, Normal = 1, Heroic = 2, Legendary = 3 }

        /// <summary>A campaign mission is loaded / playing (false in the hub and in multiplayer).</summary>
        public static bool Active { get; private set; }
        public static string Mission { get; private set; } = "";
        public static Difficulty Level { get; private set; } = Difficulty.Normal;
        /// <summary>The structure BSP the players are in (Halo switches BSPs at its switch trigger volumes / by script).</summary>
        public static int BspIndex { get; private set; }

        /// <summary>The active BSP changed (old, new): World swaps the geometry, AI and devices react.</summary>
        public static event Action<int, int> BspSwitched;
        /// <summary>A checkpoint was taken (game_save): the state the players return to after a death.</summary>
        public static event Action Checkpoint;
        /// <summary>The mission ended (won = true when its script ended the game normally).</summary>
        public static event Action<bool> Ended;
        /// <summary>The game was reverted to the last checkpoint (after the restore: every module's part is back).</summary>
        public static event Action Reverted;

        /// <summary>The ten missions of Halo PC in the game's order, with the names the game's menu shows.</summary>
        public static readonly string[] Missions = { "a10", "a30", "a50", "b30", "b40", "c10", "c20", "c40", "d20", "d40" };

        /// <summary>The mission's name as the game shows it ("Pillar of Autumn").</summary>
        public static string Label(string mission) { return HaloInstall.Label(mission ?? ""); }

        /// <summary>The difficulty as the game names it.</summary>
        public static string Name(Difficulty d)
        {
            switch (d) { case Difficulty.Easy: return "Easy"; case Difficulty.Heroic: return "Heroic"; case Difficulty.Legendary: return "Legendary"; default: return "Normal"; }
        }

        /// <summary>This client runs the mission's scripts and AI (solo / offline / the instance master in co-op; HaloNetGame.CampaignHost read
        /// when the mission starts). A co-op client draws the host's units and follows its BSP, objects and devices.</summary>
        public static bool Host { get; private set; } = true;

        /// <summary>The parsed campaign data of the running mission (object names, devices, trigger volumes, profiles; null in the hub).</summary>
        public static HaloMissionData Data { get; private set; }

        /// <summary>Game ticks (30 a second) since the mission started (script game_time).</summary>
        public static int Ticks { get; private set; }
        /// <summary>Halo's game tick length.</summary>
        public const float TickSeconds = 1f / 30f;
        /// <summary>The fraction of the next tick already elapsed (0..1): for interpolating visuals between ticks.</summary>
        public static float TickFraction { get; private set; }

        /// <summary>Loads a campaign map (e.g. "a10") at a difficulty and plays it from its start.</summary>
        public static void StartMission(string mission, Difficulty difficulty)
        {
            try { BeginMission(mission, difficulty); }
            catch (Exception e) { HaloLog.Error("Campaign: StartMission " + mission + " failed: " + e); }
        }

        /// <summary>Back to the ring hub.</summary>
        public static void LeaveMission()
        {
            try { Leave(); }
            catch (Exception e) { HaloLog.Error("Campaign: LeaveMission failed: " + e); }
        }

        /// <summary>Script / trigger volume: make <paramref name="index"/> the active BSP.</summary>
        public static void SwitchBsp(int index)
        {
            try { DoSwitchBsp(index); }
            catch (Exception e) { HaloLog.Error("Campaign: SwitchBsp " + index + " failed: " + e); }
        }

        /// <summary>Script game_save: a checkpoint as soon as it is safe (no damage lately, alive, not falling; gives up after a while).</summary>
        public static void Save() { RequestSave(SaveKind.Safe); }
        /// <summary>Script game_save_totally_unsafe: a checkpoint now.</summary>
        public static void SaveTotallyUnsafe() { RequestSave(SaveKind.Now); }
        /// <summary>Script game_save_no_timeout: a checkpoint as soon as it is safe, however long that takes.</summary>
        public static void SaveNoTimeout() { RequestSave(SaveKind.NoTimeout); }
        /// <summary>Script game_save_cancel.</summary>
        public static void SaveCancel() { _savePending = SaveKind.None; }
        /// <summary>Script game_saving: a requested checkpoint is still waiting to be taken.</summary>
        public static bool Saving { get { return _savePending != SaveKind.None; } }
        /// <summary>A checkpoint exists to revert to (script game_reverted / game_safe_to_save helpers).</summary>
        public static bool HasCheckpoint { get { return HaloCheckpoints.HasSave; } }

        /// <summary>Back to the last checkpoint (death in the campaign).</summary>
        public static void Revert()
        {
            try { DoRevert("script / request"); }
            catch (Exception e) { HaloLog.Error("Campaign: Revert failed: " + e); }
        }

        /// <summary>Script game_won / game_lost.</summary>
        public static void End(bool won)
        {
            try { DoEnd(won); }
            catch (Exception e) { HaloLog.Error("Campaign: End failed: " + e); }
        }

        // ---- for the owner only (raise helpers keep one failing listener from stopping the rest)
        internal static void RaiseBspSwitched(int a, int b) { var h = BspSwitched; if (h == null) return; foreach (Action<int, int> d in h.GetInvocationList()) { try { d(a, b); } catch (Exception e) { HaloLog.Error("Campaign: BspSwitched listener: " + e); } } }
        internal static void RaiseCheckpoint() { var h = Checkpoint; if (h == null) return; foreach (Action d in h.GetInvocationList()) { try { d(); } catch (Exception e) { HaloLog.Error("Campaign: Checkpoint listener: " + e); } } }
        internal static void RaiseEnded(bool won) { var h = Ended; if (h == null) return; foreach (Action<bool> d in h.GetInvocationList()) { try { d(won); } catch (Exception e) { HaloLog.Error("Campaign: Ended listener: " + e); } } }
        internal static void RaiseReverted() { var h = Reverted; if (h == null) return; foreach (Action d in h.GetInvocationList()) { try { d(); } catch (Exception e) { HaloLog.Error("Campaign: Reverted listener: " + e); } } }
        internal static void SetState(bool active, string mission, Difficulty level, int bsp) { Active = active; Mission = mission ?? ""; Level = level; BspIndex = bsp; }
    }

    /// <summary>Campaign teams as the game numbers them (UnitDefaultTeam / ScenarioTeamIndex).</summary>
    public enum HaloTeam { None = 0, Player = 1, Human = 2, Covenant = 3, Flood = 4, Sentinel = 5 }

    /// <summary>Halo's alliances: player + human are friends; covenant, flood and sentinel fight everybody else (scripts can change
    /// it with ai_allegiance - the AI module owns the live table).</summary>
    public static class HaloAllegiance
    {
        static readonly bool[,] Friends = new bool[8, 8];
        static HaloAllegiance() { Reset(); }

        public static void Reset()
        {
            Array.Clear(Friends, 0, Friends.Length);
            for (int i = 0; i < 8; i++) Friends[i, i] = true;
            Set(HaloTeam.Player, HaloTeam.Human, true);
        }

        public static void Set(HaloTeam a, HaloTeam b, bool friends) { Friends[(int)a, (int)b] = friends; Friends[(int)b, (int)a] = friends; }
        public static bool AreFriends(HaloTeam a, HaloTeam b) { return a == HaloTeam.None || b == HaloTeam.None || Friends[(int)a & 7, (int)b & 7]; }
        public static bool AreEnemies(HaloTeam a, HaloTeam b) { return !AreFriends(a, b); }
    }
}

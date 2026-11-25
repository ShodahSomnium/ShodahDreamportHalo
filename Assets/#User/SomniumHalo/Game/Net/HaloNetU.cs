using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>Conversions between the wire's plain structs and Unity's, and the tag indices of the current map.</summary>
    public static class HaloNetU
    {
        public static Vector3 V(HaloV3 v) { return new Vector3(v.X, v.Y, v.Z); }
        public static HaloV3 V(Vector3 v) { return new HaloV3(v.x, v.y, v.z); }
        public static Quaternion Q(HaloQ q) { return new Quaternion(q.X, q.Y, q.Z, q.W); }
        public static HaloQ Q(Quaternion q) { return new HaloQ(q.x, q.y, q.z, q.w); }

        /// <summary>Unix time in whole seconds, the clock of game starts.</summary>
        public static int UtcSeconds() { return (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(); }

        /// <summary>A tag's index on the wire (0xFFFF none). Only tags of the current match's map are sent.</summary>
        public static int TagIndex(HaloTag t)
        {
            if (t == null || t.Index < 0 || t.Index >= 0xFFFF) return HaloNetWire.NoTag;
            HaloMap m = HaloSession.Map;
            return m != null && t.Map == m ? t.Index : HaloNetWire.NoTag;
        }

        /// <summary>The tag of an index of the current match's map (null for none / out of range).</summary>
        public static HaloTag TagOf(int index)
        {
            HaloMap m = HaloSession.Map;
            if (m == null || index < 0 || index == HaloNetWire.NoTag || index >= m.Tags.Length) return null;
            return m.Tags[index];
        }

        /// <summary>The 16-bit fingerprint of the current match's map build (0 when none).</summary>
        public static int MapPrint()
        {
            HaloMap m = HaloSession.Map;
            return m != null ? HaloNetWire.MapPrint(m.Tags.Length, m.ScenarioTagId, m.TagDataSize) : 0;
        }
    }

    /// <summary>
    /// The game variant on the wire: three ints (<see cref="HaloNetWire"/> 50-52) packed from <see cref="HaloRules"/>; the map travels as its name hash (int 53).
    ///   A: game type (0-3) | teams (4) | score limit (5-16, 0-4095) | time limit minutes (17-23, 0-127) | respawn seconds (24-29, 0-63) | shields (30) | motion tracker (31)
    ///   B: friendly fire (0) | weapon set (1-5) | vehicle set (6-10) | grenades (11) | start with oddball (12) | speed x20 (13-19) | lives (20-23) | suicide penalty s (24-28) |
    ///      moving hill (29) | flag at home to score (30)
    ///   C: race type (0-1) | race team scoring (2-3)
    /// Tell the Net module when HaloRules gets a field (it must be added here, or late joiners and other players play without it).
    /// </summary>
    public static class HaloNetRules
    {
        /// <summary>Rules int C bit 4: the master's game is a campaign mission (difficulty in bits 5-6; the map hash is the mission's).</summary>
        public const int CampaignBit = 1 << 4;

        public static void Pack(HaloRules r, out int a, out int b, out int c)
        {
            a = ((int)r.Type & 0xF) | (r.Teams ? 1 << 4 : 0) | (HaloNetWire.Clamp(r.ScoreLimit, 0, 4095) << 5) | (HaloNetWire.Clamp(r.TimeLimitMinutes, 0, 127) << 17)
                | (HaloNetWire.Clamp(r.RespawnSeconds, 0, 63) << 24) | (r.Shields ? 1 << 30 : 0) | (r.MotionTracker ? 1 << 31 : 0);
            int speed = HaloNetWire.Clamp((int)Math.Round(r.SpeedScale * 20f), 0, 127);
            b = (r.FriendlyFire ? 1 : 0) | (HaloNetWire.Clamp(r.WeaponSet, 0, 31) << 1) | (HaloNetWire.Clamp(r.Vehicles, 0, 31) << 6) | (r.Grenades ? 1 << 11 : 0)
                | (r.StartWithOddball ? 1 << 12 : 0) | (speed << 13) | (HaloNetWire.Clamp(r.Lives, 0, 15) << 20) | (HaloNetWire.Clamp(r.SuicidePenaltySeconds, 0, 31) << 24)
                | (r.KingMovingHill ? 1 << 29 : 0) | (r.FlagAtHomeToScore ? 1 << 30 : 0);
            c = HaloNetWire.Clamp(r.RaceType, 0, 3) | (HaloNetWire.Clamp(r.RaceTeamScoring, 0, 3) << 2);
        }

        /// <summary>The rules from the three ints; the map name from the lobby's map hash (looked up in the install's multiplayer maps, null when unknown).</summary>
        public static HaloRules Unpack(int a, int b, int c, string mapName)
        {
            var r = new HaloRules();
            r.Type = (HaloScenario.GameType)(a & 0xF);
            r.Teams = (a & (1 << 4)) != 0;
            r.ScoreLimit = (a >> 5) & 0xFFF;
            r.TimeLimitMinutes = (a >> 17) & 0x7F;
            r.RespawnSeconds = (a >> 24) & 0x3F;
            r.Shields = (a & (1 << 30)) != 0;
            r.MotionTracker = (a & (1 << 31)) != 0;
            r.FriendlyFire = (b & 1) != 0;
            r.WeaponSet = (b >> 1) & 31;
            r.Vehicles = (b >> 6) & 31;
            r.Grenades = (b & (1 << 11)) != 0;
            r.StartWithOddball = (b & (1 << 12)) != 0;
            r.SpeedScale = ((b >> 13) & 127) / 20f;
            r.Lives = (b >> 20) & 15;
            r.SuicidePenaltySeconds = (b >> 24) & 31;
            r.KingMovingHill = (b & (1 << 29)) != 0;
            r.FlagAtHomeToScore = (b & (1 << 30)) != 0;
            r.RaceType = c & 3;
            r.RaceTeamScoring = (c >> 2) & 3;
            if (!string.IsNullOrEmpty(mapName)) r.MapName = mapName;
            return r;
        }

        /// <summary>The installed multiplayer map whose name hashes to this (null when this install has none).</summary>
        public static string MapOf(int hash)
        {
            if (hash == 0) return null;
            HaloGame g = HaloGame.Current;
            HaloInstall inst = g != null ? g.Install : null;
            if (inst == null) return null;
            for (int i = 0; i < inst.Campaign.Count; i++)
                if (HaloNetWire.NameHash(inst.Campaign[i].Name) == hash) return inst.Campaign[i].Name;
            for (int i = 0; i < inst.Multiplayer.Count; i++)
                if (HaloNetWire.NameHash(inst.Multiplayer[i].Name) == hash) return inst.Multiplayer[i].Name;
            return null;
        }
    }
}

using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The lobby over the net: the Front module owns the lobby's flow (HaloLobby: the master's choices, START GAME and its countdown, the pull-in of the other
    /// players and out again) and talks to the instance only through <see cref="IHaloLobbyNet"/>; this is Net's implementation of it, registered in
    /// <see cref="HaloLobbyNet.Net"/> while the layer runs. The master's lobby state rides in the master's record (HaloNetWire ints 46-53): phase, countdown,
    /// map, version, the packed rules, and in a match the game command (map, the lobby's match number, the start in UTC seconds) that late joiners and
    /// the world blocks key on. My own game number (<see cref="GameNumber"/>) is the command's when I play its map, else 0 (a solo match).
    /// </summary>
    public static partial class HaloNetGame
    {
        private static readonly LobbyLink Link = new LobbyLink();
        private static HaloRules _readRules;
        private static int _readVersion = -1, _readMaster = -2, _readZone;
        private static int _ownSeq, _ownSeqLogged = -1;

        /// <summary>The rules the instance master published (null while nothing arrived; the master's own lobby is HaloLobby.Mine).</summary>
        public static HaloRules MasterRules
        {
            get
            {
                if (!Up) return null;
                HaloNet.LobbyInfo l = Net.Lobby;
                if (!l.RulesOk) return null;
                if (_readRules == null || l.Version != _readVersion || l.MasterId != _readMaster || l.ZoneHash != _readZone)
                {
                    _readRules = HaloNetRules.Unpack(l.RulesA, l.RulesB, l.RulesC, HaloNetRules.MapOf(l.ZoneHash));
                    _readVersion = l.Version;
                    _readMaster = l.MasterId;
                    _readZone = l.ZoneHash;
                }

                return _readRules;
            }
        }

        /// <summary>The version of the master's published lobby state (changes with every change; 0 = nothing published yet).</summary>
        public static int RulesVersion { get { return Up ? Net.Lobby.Version : 0; } }

        /// <summary>The master has a match running (players arriving are pulled in by the lobby).</summary>
        public static bool MatchRunning { get { return Up && Net.Game.On; } }

        /// <summary>The map of the master's running match (null when none, or not in this install).</summary>
        public static string RunningMap { get { return MatchRunning ? HaloNetRules.MapOf(Net.Game.ZoneHash) : null; } }

        /// <summary>The master's match start in UTC seconds (0 = none / not known yet).</summary>
        public static double GameStartUtc { get { return MatchRunning ? Net.Game.Start : 0; } }

        /// <summary>The number of the master's match I play (0 = none / solo).</summary>
        public static int GameNumber { get { return _ownSeq; } }

        // ------------------------------------------------------------------ teams

        /// <summary>My side: 0 red / 1 blue (in a team match HaloLocal.Team; in the hub the side the lobby gave me).</summary>
        public static int MyTeam
        {
            get
            {
                if (HaloLocal.Team == HaloTeams.Red || HaloLocal.Team == HaloTeams.Blue) return HaloLocal.Team;
                return Up ? Net.Own.Team & 1 : HaloTeams.Red;
            }
        }

        public static bool CanChangeTeam(out string why)
        {
            why = null;
            if (!Up) return true;
            return Net.CanSwapTeam(out why);
        }

        /// <summary>Moves me to a side (the lobby, or a running team match through HaloMatchControl.SwitchTeam). False with the reason.</summary>
        public static bool RequestTeam(int team, out string why)
        {
            why = null;
            if (team != HaloTeams.Red && team != HaloTeams.Blue) { why = "No such team."; return false; }
            if (Up && !Net.CanSwapTeam(out why)) return false;
            if (HaloSession.InMatch)
            {
                HaloGame.Guard("team switch", () => HaloMatchControl.SwitchTeam(team));
                if (Up && HaloLocal.Team == team) Net.SwapTeam(team, out why);
                return HaloLocal.Team == team;
            }

            bool ok = !Up || Net.SwapTeam(team, out why);
            if (ok) HaloLocal.Team = team;
            return ok;
        }

        // ------------------------------------------------------------------ the match I play (from Tick)

        private static void OnMapOpened()
        {
            // a match opens (the lobby's, a late join, or a solo one): it is the master's game when its command names this map
            HaloInstall.MapEntry e = HaloSession.MapEntry;
            int zone = e != null ? HaloNetWire.NameHash(e.Name) : 0;
            HaloNet.GameInfo g = Up ? Net.Game : default(HaloNet.GameInfo);
            _ownSeq = Up && Net.Online && g.On && g.ZoneHash == zone ? g.Seq : 0;
            if (_ownSeq != _ownSeqLogged)
            {
                _ownSeqLogged = _ownSeq;
                HaloLog.Info("Net: " + (_ownSeq != 0 ? "playing the master's game " + _ownSeq + " (start UTC " + g.Start + ")" : "playing a match of my own (no game of the master on this map)"));
            }
        }

        private static void StepInstance()
        {
            HaloSession.Phase ph = HaloSession.Current;
            bool inMatch = ph != HaloSession.Phase.Hub;
            // the master's command appeared after my map opened (its lobby published a moment later): take its number
            if (inMatch && _ownSeq == 0 && Net.Online && Net.Game.On && HaloSession.MapEntry != null && Net.Game.ZoneHash == HaloNetWire.NameHash(HaloSession.MapEntry.Name)
                && (Net.IsInstanceMaster || Math.Abs(HaloSession.StartUtc - Net.Game.Start) < 2.0 || Net.Game.Start == 0))
                OnMapOpened();
            if (_wasInMatch && !inMatch) LeftMatch();
            _wasInMatch = inMatch;
        }

        private static bool _wasInMatch;

        /// <summary>My match is over (back in the hub): the record says "present", the drawn players go.</summary>
        private static void LeftMatch()
        {
            int left = _ownSeq;
            _ownSeq = 0;
            _ownSeqLogged = -1;
            ClearViews();
            Net.LeaveMatch();
            HaloLog.Info("Net: back in the hub" + (left != 0 ? " (left game " + left + ")" : ""));
        }

        // ------------------------------------------------------------------ the Front module's lobby link

        /// <summary>IHaloLobbyNet over the slot layer (see HaloLobbyNet.cs for the contract).</summary>
        private sealed class LobbyLink : IHaloLobbyNet
        {
            private HaloRules _rules;
            private int _rulesVersion = -1, _rulesMaster = -2, _rulesZone;

            public bool Online { get { return HaloNetGame.Online && Net.Claimed; } }

            public bool MasterKnown { get { return !Net.MasterPending; } }

            public bool IsMaster { get { return Net.IsInstanceMaster; } }

            public string MasterName { get { return Net.MasterPending ? null : HaloNetGame.MasterName; } }

            public string LocalName { get { return LocalNick(); } }

            public void Players(List<HaloLobbyPlayer> into)
            {
                if (into == null) return;
                bool inMatch = HaloSession.Current != HaloSession.Phase.Hub;
                into.Add(new HaloLobbyPlayer { Id = HaloLocal.NetId, Name = LocalNick(), Team = TeamOf(Net.Own.Team), Me = true, Master = Net.IsInstanceMaster, InMatch = inMatch });
                for (int i = 0; i < List.Count; i++)
                {
                    HaloNetPlayer p = List[i];
                    into.Add(new HaloLobbyPlayer { Id = p.Slot, Name = p.Name, Team = TeamOf(p.Team), Me = false, Master = p.IsMaster, InMatch = p.Status == HaloNetStatus.Playing });
                }
            }

            private static int TeamOf(int side) { return (side & 1) == 0 ? HaloTeams.Red : HaloTeams.Blue; }

            public void Publish(HaloLobbyState state)
            {
                if (!Net.IsInstanceMaster || state.Rules == null) return;
                int a, b, c;
                HaloNetRules.Pack(state.Rules, out a, out b, out c);
                bool mission = !string.IsNullOrEmpty(state.Mission);
                int zone = HaloNetWire.NameHash(mission ? state.Mission : state.Rules.MapName);
                if (mission) c = (c & 0xF) | HaloNetRules.CampaignBit | ((state.Difficulty & 3) << 5);
                bool inMatch = state.Phase == HaloLobbyState.InMatch;
                Net.PublishLobbyState(state.Phase + 1, state.Count, zone, state.Version, a, b, c, inMatch, state.MatchId, inMatch ? (int)state.StartUtc : 0);
            }

            public bool ReadMaster(out HaloLobbyState state)
            {
                state = default(HaloLobbyState);
                HaloNet.LobbyInfo l = Net.Lobby;
                if (l.Phase == 0 || !l.RulesOk) return false;
                HaloNet.GameInfo g = Net.Game;
                state.Phase = Math.Max(0, Math.Min(2, l.Phase - 1));
                state.Count = l.Count;
                state.MatchId = Net.MasterGameSeq;
                state.StartUtc = g.On ? g.Start : 0;
                state.Version = l.Version;
                if (_rules == null || l.Version != _rulesVersion || l.MasterId != _rulesMaster || l.ZoneHash != _rulesZone)
                {
                    _rules = HaloNetRules.Unpack(l.RulesA, l.RulesB, l.RulesC, HaloNetRules.MapOf(l.ZoneHash));
                    _rulesVersion = l.Version;
                    _rulesMaster = l.MasterId;
                    _rulesZone = l.ZoneHash;
                }

                state.Rules = _rules;
                if ((l.RulesC & HaloNetRules.CampaignBit) != 0)
                {
                    state.Mission = HaloNetRules.MapOf(l.ZoneHash);
                    state.Difficulty = (l.RulesC >> 5) & 3;
                }
                return true;
            }

            public bool SetTeam(int team, out string why)
            {
                why = null;
                if (team != HaloTeams.Red && team != HaloTeams.Blue) return true;      // (a free-for-all side means nothing)
                return Net.SwapTeam(team, out why);
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The instance master, the game it commands and the lobby (part of <see cref="HaloNet"/>, pure C#; Bo2Net.Master / Bo2Net.Lobby ported).
    ///
    /// WHO IS THE INSTANCE MASTER. Somnium's notion of "host" is the Fusion shared-mode master client (NetworkRunner.IsSharedModeMasterClient). The
    /// upload server's Fusion has no GetMasterClient, so every client publishes its own IsSharedModeMasterClient as a bit of its record and every client
    /// reads the same answer from the same records: 1. I am the master when my runner says so (and I hold a record); 2. else the owner of the first
    /// listed record with the bit; 3. else (the room's master is a visitor, or the bit has not arrived) the Dreamport player with the LOWEST player id
    /// (Fusion's ids grow with the join order). Not online: the local player is alone and is the master.
    ///
    /// THE GAME. Only the master starts a game: <see cref="StartGame"/> writes the command into its own record (map hash, a running number, the start
    /// in UTC seconds); every other client reads the MASTER's record (<see cref="Game"/>) and joins that game once (HaloNetGame does the loading); a
    /// client that arrives while the game runs finds the same command. A game = the same map AND the same number, so a fresh game on the same map
    /// is a new game for everybody. A new master (the old one left) that plays the running game keeps commanding it, and keeps the lobby it read.
    ///
    /// THE LOBBY. The master opens it on a map, publishes the rules and starts the match with a countdown; everybody reads it from the master's record.
    /// The lobby survives a match. The side a player stands on is chosen when it arrives (the smaller side) and kept into the match.
    /// </summary>
    public sealed partial class HaloNet
    {
        /// <summary>The countdown before a match begins, in seconds (ours; Halo's pregame countdown is about this).</summary>
        public const int LobbyCountdownSeconds = 5;

        /// <summary>The game the instance master has open (read from the master's record).</summary>
        public struct GameInfo
        {
            public bool On;
            public int ZoneHash, Seq, Start, MasterId;
        }

        /// <summary>The lobby as this client sees it: its own when it is the master, else the master's record.</summary>
        public struct LobbyInfo
        {
            public bool Open;
            /// <summary>0 none, 1 open, 2 countdown.</summary>
            public int Phase, Count, ZoneHash, Version, RulesA, RulesB, RulesC, MasterId;
            public bool RulesOk;
        }

        /// <summary>A player for the lobby page's lists.</summary>
        public struct LobbyPlayer
        {
            public int PlayerId, Slot, Team;
            public bool InMatch, Master, Me;
        }

        private GameInfo _game;
        private LobbyInfo _lobby;
        private int _masterLogged = int.MinValue;
        private bool _wasMaster;
        private double _countAt;
        private bool _lobbyTeamSet;
        private int _lobbyTeamGen = -1;
        private double _lobbyTeamAt = double.NegativeInfinity;

        /// <summary>The instance master's player id; -1 while not known (not online, or my own record is not claimed yet).</summary>
        public int MasterId { get; private set; } = -1;

        /// <summary>This client is the instance master (or alone offline, where it decides everything).</summary>
        public bool IsInstanceMaster { get { return !Online || (MasterId >= 0 && MasterId == LocalId); } }

        /// <summary>Online, but the instance master is not known yet (the claim is not done): choose nothing yet.</summary>
        public bool MasterPending { get { return Online && MasterId < 0; } }

        public GameInfo Game { get { return _game; } }

        public LobbyInfo Lobby { get { return _lobby; } }

        /// <summary>The record of another player by player id (in the room or in a match), null if none.</summary>
        public Remote RemoteOfPlayer(int playerId)
        {
            if (playerId < 0) return null;
            for (int k = 0; k < MaxSlots; k++)
            {
                Remote r = _remoteBySlot[k];
                if (r != null && r.PlayerId == playerId) return r;
            }

            return null;
        }

        private void StepMaster(double now, int localId)
        {
            bool online = Online;
            Own.Master = online && _t.IsMaster;
            int master = -1;
            if (online && MySlot >= 0)
            {
                if (Own.Master) master = localId;
                else
                {
                    for (int k = 0; k < MaxSlots && master < 0; k++)
                    {
                        Remote r = _remoteBySlot[k];
                        if (r != null && r.Decoded && r.State.Master) master = r.PlayerId;
                    }
                }

                if (master < 0)
                {
                    master = localId;
                    for (int k = 0; k < MaxSlots; k++)
                    {
                        Remote r = _remoteBySlot[k];
                        if (r != null && r.Decoded && r.PlayerId >= 0 && r.PlayerId < master) master = r.PlayerId;
                    }
                }
            }

            if (master != MasterId)
            {
                MasterId = master;
                if (master != _masterLogged)
                {
                    _masterLogged = master;
                    Log(master < 0 ? "the instance master is not known." : "the instance master is " + (master == localId ? "this client" : "player " + master) + " (" + (Own.Master ? "the room's shared-mode master client" : "named by the records") + ").");
                }
            }

            bool mine = online ? master >= 0 && master == localId : true;
            if (mine && !_wasMaster && online) TakeOverAsMaster();
            _wasMaster = mine && online;

            GameInfo g = default(GameInfo);
            if (master >= 0)
            {
                g.MasterId = master;
                if (master == localId) { g.ZoneHash = Own.GameZone; g.Seq = Own.GameSeq; g.Start = Own.GameStart; }
                else
                {
                    Remote r = RemoteOfPlayer(master);
                    if (r != null && r.Decoded) { g.ZoneHash = r.State.GameZone; g.Seq = r.State.GameSeq; g.Start = r.State.GameStart; }
                }

                g.On = g.ZoneHash != 0 && g.Seq != 0;
            }
            else if (!online)
            {
                g.MasterId = -1;
                g.ZoneHash = Own.GameZone; g.Seq = Own.GameSeq; g.Start = Own.GameStart;
                g.On = g.ZoneHash != 0 && g.Seq != 0;
            }

            _game = g;
            StepLobby(master, localId);
        }

        /// <summary>I just became the master of an instance with other players: keep commanding the game I play, and keep the lobby I read from the old master.</summary>
        private void TakeOverAsMaster()
        {
            if (Own.GameZone == 0 && Own.Status == HaloNetStatus.Playing && Own.MatchSeq != 0 && Own.MatchState == HaloMatchState.Running)
            {
                Own.GameZone = Own.ZoneHash;
                Own.GameSeq = Own.MatchSeq;
                Own.GameStart = Own.MatchStart;
                Log("now the instance master: commanding the running game " + Own.GameSeq + " further.");
            }
            else if (Own.GameZone == 0 && _game.On && _lobby.Open && _lobby.Phase == 3)
            {
                // I sit in the hub but the old master's game runs on for the others: keep commanding it (the lowest player in it writes the world)
                Own.GameZone = _game.ZoneHash;
                Own.GameSeq = _game.Seq;
                Own.GameStart = _game.Start;
                Log("now the instance master (in the hub): the running game " + Own.GameSeq + " goes on for its players.");
            }

            if (Own.LobbyPhase == 0 && _lobby.Open)
            {
                Own.LobbyPhase = _lobby.Phase == 3 ? 3 : 1;      // (a running match / mission stays running: the user's hand-over rule; a countdown goes back to the lobby)
                Own.LobbyCount = 0;
                Own.LobbyZone = _lobby.ZoneHash;
                if (_lobby.RulesOk) { Own.RulesVersion = _lobby.Version; Own.RulesA = _lobby.RulesA; Own.RulesB = _lobby.RulesB; Own.RulesC = _lobby.RulesC; }
                Log("now the instance master: the lobby on " + _lobby.ZoneHash.ToString("x8") + " stays open.");
            }

            Early();
        }

        // ------------------------------------------------------------------ the master's command

        /// <summary>
        /// The master opens a game on a map: the next running number (above every number anyone has written, so it also grows over a hand-over), the map's hash
        /// and the start go into the master's record. Returns the number, 0 when this client may not (not the master). Offline the number is 1 (a solo game).
        /// </summary>
        public int StartGame(int zoneHash, int startUtc)
        {
            if (!IsInstanceMaster || zoneHash == 0) return 0;
            int max = Own.GameSeq;
            for (int k = 0; k < MaxSlots; k++)
            {
                Remote r = _remoteBySlot[k];
                if (r != null && r.Decoded) max = Math.Max(max, Math.Max(r.State.GameSeq, r.State.MatchSeq));
            }

            int seq = max % 0xFFFF + 1;
            Own.GameZone = zoneHash;
            Own.GameSeq = seq;
            Own.GameStart = startUtc;
            Early();
            Log("game " + seq + " commanded on map " + zoneHash.ToString("x8") + " (start " + startUtc + ").");
            return seq;
        }

        /// <summary>The master closes its game (it is over): later arrivals are not pulled into it. The running number stays.</summary>
        public void EndGame()
        {
            if (Own.GameZone == 0) return;
            Log("game " + Own.GameSeq + " closed.");
            Own.GameZone = 0;
            Early();
        }

        // ------------------------------------------------------------------ teams

        /// <summary>The humans of my match on each side, me included.</summary>
        public void TeamCounts(out int team0, out int team1)
        {
            team0 = team1 = 0;
            for (int i = 0; i < _remotes.Count; i++)
            {
                if ((_remotes[i].State.Team & 1) == 0) team0++; else team1++;
            }

            if (Own.Status == HaloNetStatus.Playing)
            {
                if ((Own.Team & 1) == 0) team0++; else team1++;
            }
        }

        /// <summary>Whether I may change sides: in the lobby (not during the countdown) or in a team match where the sides stay within one of each other.</summary>
        public bool CanSwapTeam(out string why)
        {
            why = null;
            if (Own.Status != HaloNetStatus.Playing)
            {
                if (_lobby.Phase == 2) { why = "The match is about to begin."; return false; }
                return true;
            }

            if (!TeamGame) { why = "This is a free-for-all game."; return false; }
            if (_remotes.Count == 0) return true;
            int n0, n1;
            TeamCounts(out n0, out n1);
            int mine = Own.Team & 1;
            int a0 = mine == 0 ? n0 - 1 : n0 + 1, a1 = mine == 1 ? n1 - 1 : n1 + 1;
            if (Math.Abs(a0 - a1) > 1) { why = "The teams would be unbalanced (" + a0 + " against " + a1 + ")."; return false; }
            return true;
        }

        /// <summary>Moves me to the side <paramref name="team"/> (0 / 1) if <see cref="CanSwapTeam"/> allows it. True when I am on that side afterwards.</summary>
        public bool SwapTeam(int team, out string why)
        {
            why = null;
            if ((Own.Team & 1) == (team & 1)) return true;
            if (!CanSwapTeam(out why)) return false;
            Own.Team = team & 1;
            _teamChosen = true;
            _teamChosenAt = double.NegativeInfinity;
            _lobbyTeamSet = true;
            _lobbyTeamAt = double.NegativeInfinity;
            Early();
            Log("side changed to " + Own.Team + " by the player.");
            return true;
        }

        // ------------------------------------------------------------------ out of a match, back to the hub

        /// <summary>
        /// The match is over for me but this client stays in the instance: the record says "present" again, the other players' match state is dropped. The master's
        /// open game, the lobby and rules, the side and the event counters (readers compare them) outlive the match.
        /// </summary>
        public void LeaveMatch()
        {
            for (int j = 0; j < _remotes.Count; j++) _remotes[j].InMatch = false;
            _remotes.Clear();
            Events.Clear();
            HaloNetState was = Own;
            Own = HaloNetState.Empty();
            Own.Status = HaloNetStatus.Present;
            Own.GameZone = was.GameZone; Own.GameSeq = was.GameSeq; Own.GameStart = was.GameStart;
            Own.Team = was.Team;
            Own.LobbyPhase = was.LobbyPhase; Own.LobbyCount = was.LobbyCount; Own.LobbyZone = was.LobbyZone;
            Own.RulesVersion = was.RulesVersion; Own.RulesA = was.RulesA; Own.RulesB = was.RulesB; Own.RulesC = was.RulesC;
            Own.Deaths = was.Deaths; Own.FireCounter = was.FireCounter; Own.ThrowCounter = was.ThrowCounter; Own.MeleeCounter = was.MeleeCounter;
            Own.HitSeq = was.HitSeq; Own.ObjCounter = was.ObjCounter; Own.PickupCounter = was.PickupCounter; Own.DropCounter = was.DropCounter;
            ResetMatch();
            Early();
        }

        // ------------------------------------------------------------------ the lobby

        private void StepLobby(int master, int localId)
        {
            var l = default(LobbyInfo);
            bool mine = IsInstanceMaster;
            if (mine) FillLobby(ref l, in Own, Online ? localId : -1, true);
            else if (master >= 0)
            {
                Remote r = RemoteOfPlayer(master);
                if (r != null && r.Decoded) FillLobby(ref l, in r.State, master, false);
            }

            _lobby = l;
            // a record that is not the master's carries no lobby (an old master's leftovers must not come back to life)
            if (!mine && master >= 0 && (Own.LobbyPhase != 0 || Own.LobbyCount != 0 || Own.LobbyZone != 0 || Own.RulesVersion != 0 || Own.RulesA != 0 || Own.RulesB != 0 || Own.RulesC != 0))
            {
                Own.LobbyPhase = Own.LobbyCount = Own.LobbyZone = Own.RulesVersion = Own.RulesA = Own.RulesB = Own.RulesC = 0;
                Early();
            }

            if (!mine && master >= 0 && Own.GameZone != 0)
            {
                Own.GameZone = 0;      // only the master commands games
                Early();
            }
        }

        private static void FillLobby(ref LobbyInfo l, in HaloNetState s, int masterId, bool own)
        {
            l.MasterId = masterId;
            l.Phase = s.LobbyPhase;
            l.Count = s.LobbyCount;
            l.ZoneHash = s.LobbyZone;
            l.Version = s.RulesVersion;
            l.RulesA = s.RulesA;
            l.RulesB = s.RulesB;
            l.RulesC = s.RulesC;
            l.RulesOk = own ? s.RulesVersion != 0 : s.RulesOk;
            l.Open = s.LobbyPhase >= 1 && s.LobbyZone != 0;
        }

        private void Touch()
        {
            Early();
            if (IsInstanceMaster) FillLobby(ref _lobby, in Own, MasterId >= 0 ? MasterId : LocalId, true);
        }

        /// <summary>The master opens the lobby on a map, or changes its map (not during the countdown).</summary>
        public bool LobbyOpen(int zoneHash)
        {
            if (!IsInstanceMaster || zoneHash == 0 || Own.LobbyPhase == 2) return false;
            if (Own.LobbyPhase == 1 && Own.LobbyZone == zoneHash) return true;
            bool opened = Own.LobbyPhase == 0;
            Own.LobbyPhase = 1;
            Own.LobbyCount = 0;
            Own.LobbyZone = zoneHash;
            Touch();
            Log("lobby " + (opened ? "opened" : "map changed") + " on " + zoneHash.ToString("x8") + ".");
            return true;
        }

        public void LobbyClose()
        {
            if (!IsInstanceMaster || Own.LobbyPhase == 0) return;
            Own.LobbyPhase = 0;
            Own.LobbyCount = 0;
            Own.LobbyZone = 0;
            Touch();
            Log("lobby closed.");
        }

        public bool LobbyStart(int seconds)
        {
            if (!IsInstanceMaster || Own.LobbyPhase != 1 || Own.LobbyZone == 0) return false;
            Own.LobbyPhase = 2;
            _countSeconds = Math.Max(0, Math.Min(31, seconds));
            Own.LobbyCount = _countSeconds;
            _countAt = Now;
            Touch();
            Log("match countdown started (" + _countSeconds + " s).");
            return true;
        }

        private int _countSeconds = LobbyCountdownSeconds;

        public bool LobbyCancel()
        {
            if (!IsInstanceMaster || Own.LobbyPhase != 2) return false;
            Own.LobbyPhase = 1;
            Own.LobbyCount = 0;
            Touch();
            Log("match countdown cancelled.");
            return true;
        }

        /// <summary>Once a frame on the master: counts the countdown down. True the moment it reaches zero (the lobby stays open; the caller starts the game).</summary>
        public bool LobbyStep()
        {
            if (!IsInstanceMaster || Own.LobbyPhase != 2) return false;
            int left = _countSeconds - (int)Math.Floor(Now - _countAt);
            if (left <= 0)
            {
                Own.LobbyPhase = 1;
                Own.LobbyCount = 0;
                Touch();
                return true;
            }

            if (left != Own.LobbyCount)
            {
                Own.LobbyCount = left;
                Touch();
            }

            return false;
        }

        /// <summary>The master publishes its rules (a version 1..4095 that grows with every change, the three packed ints). Cheap when nothing changed.</summary>
        public void PublishRules(int a, int b, int c)
        {
            if (!IsInstanceMaster) return;
            if (Own.RulesVersion != 0 && Own.RulesA == a && Own.RulesB == b && Own.RulesC == c) return;
            int max = Own.RulesVersion;
            if (_lobby.Version > max) max = _lobby.Version;
            Own.RulesVersion = max % 4095 + 1;
            Own.RulesA = a;
            Own.RulesB = b;
            Own.RulesC = c;
            Touch();
        }

        /// <summary>
        /// The master's whole lobby state as the Front module's lobby keeps it (HaloLobbyState): phase 1 lobby / 2 countdown / 3 in a match, the countdown, the map,
        /// the lobby's own version (any number: readers only compare it), the packed rules, and - in a match - the match number and its start (that is the game
        /// command every client reads; late joiners and the world blocks key on it). Nothing for a non-master.
        /// </summary>
        public void PublishLobbyState(int phase, int count, int zoneHash, int version, int a, int b, int c, bool inMatch, int matchId, int startUtc)
        {
            if (!IsInstanceMaster) return;
            int v = (int)((uint)version % 4095u) + 1;
            int seq = matchId & 0xFFFF;
            if (seq == 0 && inMatch) seq = 1;
            int zone = inMatch ? zoneHash : 0;
            bool changed = Own.LobbyPhase != (phase & 3) || Own.LobbyCount != (count & 31) || Own.LobbyZone != zoneHash || Own.RulesVersion != v || Own.RulesA != a || Own.RulesB != b
                           || Own.RulesC != c || Own.GameZone != zone || Own.GameSeq != seq || Own.GameStart != startUtc;
            if (!changed) return;
            if (inMatch && (Own.GameZone != zone || Own.GameSeq != seq)) Log("game " + seq + " commanded on map " + zoneHash.ToString("x8") + " (the lobby's match).");
            else if (!inMatch && Own.GameZone != 0) Log("game " + Own.GameSeq + " closed (the lobby left the match).");
            Own.LobbyPhase = phase & 3;
            Own.LobbyCount = count & 31;
            Own.LobbyZone = zoneHash;
            Own.RulesVersion = v;
            Own.RulesA = a;
            Own.RulesB = b;
            Own.RulesC = c;
            Own.GameZone = zone;
            Own.GameSeq = seq;
            Own.GameStart = startUtc;
            Touch();
        }

        /// <summary>The master's game number as published (also outside a match: the lobby's last match number), 0 = none.</summary>
        public int MasterGameSeq { get { return _game.Seq; } }

        /// <summary>Me first, then every other Dreamport player in the instance (in the lobby or in a match). Returns the count.</summary>
        public int LobbyPlayers(List<LobbyPlayer> into)
        {
            into.Clear();
            into.Add(new LobbyPlayer { PlayerId = LocalId, Slot = MySlot, Team = Own.Team & 1, InMatch = Own.Status == HaloNetStatus.Playing, Master = IsInstanceMaster, Me = true });
            for (int i = 0; i < _listed.Count; i++)
            {
                Remote r = _listed[i];
                into.Add(new LobbyPlayer { PlayerId = r.PlayerId, Slot = r.Slot, Team = r.State.Team & 1, InMatch = r.State.Status == HaloNetStatus.Playing, Master = r.PlayerId == MasterId });
            }

            return into.Count;
        }

        /// <summary>
        /// A player standing in the hub / lobby picks the side with fewer players when it arrives (a tie by player id parity; two that arrive together and land on the
        /// same side are fixed within 3 s). The side stays when the match starts (<see cref="StepTeam"/> keeps it).
        /// </summary>
        private void StepLobbyTeam(double now)
        {
            if (Own.Status == HaloNetStatus.Playing || MySlot < 0 || !Claimed) return;
            int gen = _claim.Generation;
            if (gen != _lobbyTeamGen)
            {
                _lobbyTeamGen = gen;
                _lobbyTeamSet = false;
            }

            int n0 = 0, n1 = 0;
            for (int i = 0; i < _listed.Count; i++)
            {
                if ((_listed[i].State.Team & 1) == 0) n0++; else n1++;
            }

            if (!_lobbyTeamSet)
            {
                Own.Team = n0 < n1 ? 0 : n1 < n0 ? 1 : (LocalId & 1);
                _lobbyTeamSet = true;
                _lobbyTeamAt = now;
                Early();
                Log("lobby side " + Own.Team + " chosen (" + n0 + " on red, " + n1 + " on blue).");
            }
            else if (now - _lobbyTeamAt <= SettleWindow)
            {
                int mine = Own.Team & 1;
                int cMine = (mine == 0 ? n0 : n1) + 1, cOther = mine == 0 ? n1 : n0;
                if (cMine - cOther >= 2)
                {
                    bool higher = false;
                    for (int i = 0; i < _listed.Count; i++)
                        if ((_listed[i].State.Team & 1) == mine && _listed[i].PlayerId > LocalId) higher = true;
                    if (!higher)
                    {
                        Own.Team = 1 - mine;
                        Early();
                        Log("lobby side changed to " + Own.Team + " (the lobby was lopsided " + cMine + " : " + cOther + ").");
                    }
                }
            }
        }
    }
}

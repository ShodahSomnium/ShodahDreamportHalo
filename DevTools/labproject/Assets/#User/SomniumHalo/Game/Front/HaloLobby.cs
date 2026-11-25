using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] The pre-game lobby's state, BO2's Bo2Game.Lobby flow with Halo's game: the instance master picks the game variant (Halo PC's
    /// standard game types by their own names), the map (the 19 multiplayer maps) and the rules (the game's own option lists), presses START GAME
    /// (a <see cref="CountdownSeconds"/> countdown, ours) and the match starts for everyone in the instance (HaloMatchControl.StartMatch with the
    /// master's rules); every other client sees it read-only, can pick a team, and is pulled into the master's match and out of it again when it ends.
    /// The net layer carries the state (<see cref="IHaloLobbyNet"/>); without it this client is the master. The master's choices are kept for next
    /// time (HaloSettings "lobby.rules"). The pages that show it are HaloLobbyPage (one per menu page).
    /// Log lines: "Lobby: ...".
    /// </summary>
    public static class HaloLobby
    {
        public static int CountdownSeconds = 5;      // ours (Halo PC's server start is immediate)
        /// <summary>A started match that has not left the hub after this long is given up (HaloMatchControl did nothing).</summary>
        public static float StartGraceSeconds = 6f;

        /// <summary>The master's own choices (also offline).</summary>
        public static HaloRules Mine { get; private set; }

        // the master's lobby
        private static int _phase, _matchId, _version = 1;
        private static float _countEnd, _startedAt;
        private static bool _sawMatch;
        private static double _publishedStart;
        private static float _nextPublish;

        // what this client shows
        public static HaloRules ViewRules { get; private set; }
        public static int ViewPhase { get; private set; }
        public static int ViewCount { get; private set; }
        public static int ViewMatchId { get; private set; }
        public static double ViewStartUtc { get; private set; }
        /// <summary>The running / coming game is this campaign mission (null = multiplayer) at this difficulty (HaloCampaign.Difficulty as int).</summary>
        public static string ViewMission { get; private set; }
        public static int ViewDifficulty { get; private set; }

        /// <summary>The master's campaign page choice (saved: campaign.mission, campaign.difficulty).</summary>
        public static string CampaignMission
        {
            get { return HaloSettings.Text("campaign.mission", "a10"); }
            set { HaloSettings.SetText("campaign.mission", value ?? "a10"); _version++; Bump(); }
        }

        public static HaloCampaign.Difficulty CampaignDifficulty
        {
            get { return (HaloCampaign.Difficulty)Mathf.Clamp(HaloSettings.Int("campaign.difficulty", 1), 0, 3); }
            set { HaloSettings.SetInt("campaign.difficulty", (int)value); _version++; Bump(); }
        }

        private static string _mission;
        private static int _difficulty = 1;
        public static bool IsMaster { get; private set; }
        public static bool Online { get; private set; }
        public static string MasterName { get; private set; }
        /// <summary>Changes whenever anything the pages show changes (the pages compare it).</summary>
        public static int Signature { get; private set; }

        private static int _viewVersion = int.MinValue;
        private static int _joinedMatch = -1, _declinedMatch = -1;
        private static bool _wasInMatch;
        private static readonly List<HaloLobbyPlayer> _players = new List<HaloLobbyPlayer>(32);
        private static int _errors;
        private static bool _loggedLink;

        public static List<HaloLobbyPlayer> Players { get { return _players; } }

        public static void Init()
        {
            Mine = LoadRules();
            ViewRules = Mine;
            IsMaster = true;
            HaloLog.Info("Lobby: ready (" + Mine + ").");
        }

        private static HaloRules LoadRules()
        {
            try
            {
                string j = HaloSettings.Text("lobby.rules", "");
                if (!string.IsNullOrEmpty(j))
                {
                    HaloRules r = Unpack(j);
                    if (r != null) return r;
                }
            }
            catch (Exception e) { HaloLog.Warn("Lobby: the saved rules could not be read: " + e.Message); }
            return HaloRules.Defaults(HaloScenario.GameType.Slayer, false);
        }

        private static void SaveRules()
        {
            try { HaloSettings.SetText("lobby.rules", Pack(Mine)); }
            catch (Exception e) { HaloLog.Warn("Lobby: saving the rules failed: " + e.Message); }
        }

        /// <summary>The rules as one line of text (the saved choice): "key:value" pairs separated by ';'.</summary>
        public static string Pack(HaloRules r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("type:").Append((int)r.Type).Append(";teams:").Append(r.Teams ? 1 : 0).Append(";score:").Append(r.ScoreLimit).Append(";time:").Append(r.TimeLimitMinutes)
              .Append(";respawn:").Append(r.RespawnSeconds).Append(";shields:").Append(r.Shields ? 1 : 0).Append(";radar:").Append(r.MotionTracker ? 1 : 0).Append(";ff:").Append(r.FriendlyFire ? 1 : 0)
              .Append(";weapons:").Append(r.WeaponSet).Append(";vehicles:").Append(r.Vehicles).Append(";grenades:").Append(r.Grenades ? 1 : 0).Append(";lives:").Append(r.Lives)
              .Append(";suicide:").Append(r.SuicidePenaltySeconds).Append(";hill:").Append(r.KingMovingHill ? 1 : 0).Append(";racetype:").Append(r.RaceType).Append(";racescore:").Append(r.RaceTeamScoring)
              .Append(";flaghome:").Append(r.FlagAtHomeToScore ? 1 : 0).Append(";map:").Append(r.MapName ?? "");
            return sb.ToString();
        }

        public static HaloRules Unpack(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("type:", StringComparison.Ordinal) < 0) return null;
            var r = new HaloRules();
            foreach (string part in s.Split(';'))
            {
                int c = part.IndexOf(':');
                if (c <= 0) continue;
                string k = part.Substring(0, c), v = part.Substring(c + 1);
                int n;
                int.TryParse(v, out n);
                switch (k)
                {
                    case "type": r.Type = (HaloScenario.GameType)n; break;
                    case "teams": r.Teams = n != 0; break;
                    case "score": r.ScoreLimit = n; break;
                    case "time": r.TimeLimitMinutes = n; break;
                    case "respawn": r.RespawnSeconds = n; break;
                    case "shields": r.Shields = n != 0; break;
                    case "radar": r.MotionTracker = n != 0; break;
                    case "ff": r.FriendlyFire = n != 0; break;
                    case "weapons": r.WeaponSet = n; break;
                    case "vehicles": r.Vehicles = n; break;
                    case "grenades": r.Grenades = n != 0; break;
                    case "lives": r.Lives = n; break;
                    case "suicide": r.SuicidePenaltySeconds = n; break;
                    case "hill": r.KingMovingHill = n != 0; break;
                    case "racetype": r.RaceType = n; break;
                    case "racescore": r.RaceTeamScoring = n; break;
                    case "flaghome": r.FlagAtHomeToScore = n != 0; break;
                    case "map": if (v.Length > 0) r.MapName = v; break;
                }
            }

            return r;
        }

        public static bool CanEdit { get { return IsMaster && _phase == HaloLobbyState.Lobby && HaloSession.Current == HaloSession.Phase.Hub; } }

        /// <summary>The master changed a rule (or the map / game type): a new version, saved, published.</summary>
        public static void Changed()
        {
            _version++;
            SaveRules();
            Publish(true);
            Bump();
        }

        public static void SetMap(string mapFile)
        {
            if (!CanEdit || Mine == null) return;
            Mine.MapName = mapFile;
            HaloLog.Info("Lobby: map " + mapFile);
            Changed();
        }

        public static void SetVariant(HaloGameTypes.Variant v)
        {
            if (!CanEdit || v == null || !v.Built) return;
            string map = Mine.MapName;
            Mine = v.Make();
            Mine.MapName = map;
            HaloLog.Info("Lobby: game type " + v.Name + " (" + Mine + ")");
            Changed();
        }

        // ------------------------------------------------------------------ the master's commands

        /// <summary>START GAME (or CANCEL during the countdown). Refused for non-masters and outside the hub.</summary>
        public static string PressStart() { return Begin(null); }

        /// <summary>START MISSION on the campaign page: the master's mission and difficulty (<see cref="CampaignMission"/>, <see cref="CampaignDifficulty"/>), for everybody in the instance (co-op).</summary>
        public static string PressStartCampaign() { return Begin(CampaignMission); }

        private static string Begin(string mission)
        {
            if (!IsMaster) return "Only the instance master" + (MasterName != null ? " (" + MasterName + ")" : "") + " starts the game.";
            if (HaloSession.Current != HaloSession.Phase.Hub) return "A match is loading or running.";
            if (HaloGame.Current == null || HaloGame.Current.Install == null) return "Your Halo install was not found.";
            string file = mission ?? Mine.MapName;
            if (HaloGame.Current.Install.Map(file) == null) return "The map " + file + " is not in your MAPS folder.";
            if (_phase == HaloLobbyState.Countdown && !string.Equals(_mission, mission, StringComparison.OrdinalIgnoreCase)) _phase = HaloLobbyState.Lobby;      // (the other page's countdown: replaced)
            if (_phase == HaloLobbyState.Lobby) { _mission = mission; _difficulty = (int)CampaignDifficulty; }
            if (_phase == HaloLobbyState.Countdown)
            {
                _phase = HaloLobbyState.Lobby;
                HaloLog.Info("Lobby: the countdown was cancelled.");
            }
            else if (_phase == HaloLobbyState.Lobby)
            {
                _phase = HaloLobbyState.Countdown;
                _countEnd = Time.unscaledTime + CountdownSeconds;
                HaloLog.Info("Lobby: START - the countdown begins (" + (_mission != null ? "campaign " + _mission + " " + (HaloCampaign.Difficulty)_difficulty : Mine.ToString()) + ")");
            }
            else return "The game is running: use JOIN GAME.";

            _version++;
            Publish(true);
            Bump();
            return null;
        }

        /// <summary>The master ends the running match for everybody (the tablet's END GAME).</summary>
        public static void EndForEveryone()
        {
            if (!IsMaster) return;
            HaloLog.Info("Lobby: the master ends the match for everyone.");
            if (HaloSession.Playing && !HaloCampaign.Active)
            {
                // the game's own end (results for HaloMatchControl.ResultsSeconds, then the lobby); the others are pulled out when the master is back
                try { HaloMatchControl.EndGame("the instance master ended the game"); }
                catch (Exception e) { HaloLog.Error("Lobby: ending the game failed: " + e); SafeLeave("the master ended the match"); }
                Bump();
                return;
            }

            _phase = HaloLobbyState.Lobby;
            _version++;
            Publish(true);
            if (HaloSession.InMatch || HaloSession.Current == HaloSession.Phase.Loading) SafeLeave("the master ended the match");
            Bump();
        }

        /// <summary>The master starts a fresh game on another map while one runs (the tablet's CHANGE MAP): everybody is pulled into the new one.</summary>
        public static void RestartOn(string mapFile)
        {
            if (!IsMaster || Mine == null) return;
            Mine.MapName = mapFile;
            _mission = null;
            SaveRules();
            HaloLog.Info("Lobby: the master restarts the game on " + mapFile);
            StartNow();      // (HaloMatchControl.StartMatch replaces a running match by itself)
            Bump();
        }

        private static bool _restartPending;

        /// <summary>A non-master who left the running game joins it again (the lobby's JOIN GAME).</summary>
        public static void Join()
        {
            _declinedMatch = -1;
            if (IsMaster && _phase == HaloLobbyState.InMatch && HaloSession.Current == HaloSession.Phase.Hub) StartLocal(Mine.Clone(), _mission, _difficulty);
        }

        public static bool CanJoin
        {
            get { return HaloSession.Current == HaloSession.Phase.Hub && ViewPhase == HaloLobbyState.InMatch && (_declinedMatch == ViewMatchId || IsMaster); }
        }

        public static string SwitchTeam()
        {
            if (ViewRules == null || !ViewRules.Teams) return "This game type has no teams.";
            int t = HaloLocal.Team == HaloTeams.Red ? HaloTeams.Blue : HaloTeams.Red;
            string why = null;
            IHaloLobbyNet net = HaloLobbyNet.Net;
            if (net != null && net.Online && !net.SetTeam(t, out why)) return why ?? "The team change was refused.";
            HaloLocal.Team = t;
            HaloLog.Info("Lobby: team " + HaloTeams.Name(t));
            Bump();
            return null;
        }

        private static void StartNow()
        {
            _matchId++;
            _phase = HaloLobbyState.InMatch;
            _startedAt = Time.unscaledTime;
            _sawMatch = false;
            _publishedStart = 0;
            _version++;
            HaloSession.StartUtc = 0;
            Publish(true);
            HaloLog.Info("Lobby: the match starts (" + (_mission != null ? "campaign " + _mission + " " + (HaloCampaign.Difficulty)_difficulty : Mine.ToString()) + ", match " + _matchId + ")");
            StartLocal(Mine.Clone(), _mission, _difficulty);
        }

        private static void StartLocal(HaloRules r, string mission, int difficulty)
        {
            try
            {
                if (!string.IsNullOrEmpty(mission))
                {
                    HaloCampaign.StartMission(mission, (HaloCampaign.Difficulty)Mathf.Clamp(difficulty, 0, 3));
                    return;
                }

                if (r.Teams && HaloLocal.Team == HaloTeams.None) HaloLocal.Team = HaloTeams.Red;
                HaloMatchControl.StartMatch(r);
            }
            catch (Exception e) { HaloLog.Error("Lobby: starting the match failed: " + e); }
        }

        private static void SafeLeave(string why)
        {
            try
            {
                HaloLog.Info("Lobby: leaving the " + (HaloCampaign.Active ? "mission" : "match") + " (" + why + ").");
                if (HaloCampaign.Active) HaloCampaign.LeaveMission(); else HaloMatchControl.LeaveMatch();
            }
            catch (Exception e) { HaloLog.Error("Lobby: leaving the match failed: " + e); }
        }

        // ------------------------------------------------------------------ per frame

        public static void Tick()
        {
            if (Mine == null) Init();
            try { Step(); }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 3) HaloLog.Warn("Lobby: the frame failed (" + _errors + (_errors == 3 ? ", later ones not logged" : "") + "): " + e);
            }
        }

        private static void Step()
        {
            IHaloLobbyNet net = HaloLobbyNet.Net;
            bool online = net != null && net.Online;
            bool known = online && net.MasterKnown;
            bool master = !online || (known && net.IsMaster);
            string masterName = !online ? "You" : known ? (net.IsMaster ? (net.LocalName ?? "You") : net.MasterName) : null;
            if (online != Online || master != IsMaster) HaloLog.Info("Lobby: " + (online ? "online, " + (known ? (master ? "I am the instance master" : "the instance master is " + masterName) : "the master is not decided yet") : "offline - this client is the master"));
            if (online && !_loggedLink) { _loggedLink = true; }
            Online = online;
            if (master && !IsMaster && net != null)
            {
                // I just became the master (the old one left): take over the published game as my own
                HaloLobbyState taken;
                if (net.ReadMaster(out taken) && taken.Rules != null) { Mine = taken.Rules.Clone(); _matchId = taken.MatchId; _mission = string.IsNullOrEmpty(taken.Mission) ? null : taken.Mission; _difficulty = taken.Difficulty; }
                // the user's rule: the old master left mid-game -> hosting moves to me and the running mission / match goes on (same match id);
                // nobody is pulled out. Only a lobby or a countdown goes back to the lobby.
                bool keep = taken.Phase == HaloLobbyState.InMatch;
                _phase = keep ? HaloLobbyState.InMatch : HaloLobbyState.Lobby;
                _sawMatch = keep && HaloSession.Current != HaloSession.Phase.Hub;
                _startedAt = Time.unscaledTime;
                _publishedStart = keep ? taken.StartUtc : 0;
                _version = Math.Max(_version, taken.Version) + 1;
                HaloLog.Info("Lobby: I am the new instance master" + (keep ? " - the running game goes on, hosted by me (match " + _matchId + ")" : " - back to the lobby") + ".");
            }

            IsMaster = master;
            MasterName = masterName;
            HaloMasterLabel.Set(known || !online ? masterName : null, master && (known || !online));
            HaloSession.Phase s = HaloSession.Current;
            bool inMatch = s != HaloSession.Phase.Hub;

            if (master)
            {
                if (_phase == HaloLobbyState.Countdown && Time.unscaledTime >= _countEnd)
                {
                    if (s == HaloSession.Phase.Hub) StartNow(); else _phase = HaloLobbyState.Lobby;
                }

                if (_phase == HaloLobbyState.InMatch)
                {
                    if (inMatch) _sawMatch = true;
                    // others still in the game (the master left it, or took over the host while in the hub): it goes on until the last one leaves
                    bool othersIn = false;
                    for (int i = 0; i < _players.Count; i++) if (!_players[i].Me && _players[i].InMatch) othersIn = true;
                    if (!inMatch && !othersIn && (_sawMatch || Time.unscaledTime - _startedAt > StartGraceSeconds))
                    {
                        // the match is over for the master (ended, left, or it never started): everybody back in the lobby
                        HaloLog.Info("Lobby: " + (_sawMatch ? "the match is over - back in the lobby" : "the match did not start (HaloMatchControl stayed in the hub)") + ".");
                        _phase = HaloLobbyState.Lobby;
                        _version++;
                        Publish(true);
                    }

                    if (s == HaloSession.Phase.Playing && HaloSession.StartUtc > 0 && HaloSession.StartUtc != _publishedStart)
                    {
                        _publishedStart = HaloSession.StartUtc;
                        _version++;
                        Publish(true);
                    }
                }

                if (_restartPending && s == HaloSession.Phase.Hub) { _restartPending = false; StartNow(); }
                Publish(false);
                ViewRules = Mine;
                ViewPhase = _phase;
                ViewCount = _phase == HaloLobbyState.Countdown ? Mathf.Max(0, Mathf.CeilToInt(_countEnd - Time.unscaledTime)) : 0;
                ViewMatchId = _matchId;
                ViewStartUtc = _publishedStart;
                ViewMission = _mission;
                ViewDifficulty = _difficulty;
            }
            else
            {
                HaloLobbyState st;
                if (net != null && net.ReadMaster(out st))
                {
                    if (st.Version != _viewVersion || st.Count != ViewCount || st.Phase != ViewPhase)
                    {
                        _viewVersion = st.Version;
                        if (st.Rules != null) ViewRules = st.Rules;
                        ViewPhase = st.Phase;
                        ViewCount = st.Count;
                        ViewMatchId = st.MatchId;
                        ViewStartUtc = st.StartUtc;
                        ViewMission = string.IsNullOrEmpty(st.Mission) ? null : st.Mission;
                        ViewDifficulty = st.Difficulty;
                    }
                }
                else if (ViewRules == null) ViewRules = Mine;

                // pulled in and out of the master's match
                if (ViewPhase == HaloLobbyState.InMatch)
                {
                    if (s == HaloSession.Phase.Hub && !_wasInMatch && _declinedMatch != ViewMatchId && _joinedMatch != ViewMatchId)
                    {
                        _joinedMatch = ViewMatchId;
                        HaloSession.StartUtc = ViewStartUtc;
                        HaloLog.Info("Lobby: joining " + (MasterName ?? "the master") + "'s match " + ViewMatchId + " (" + ViewRules + ")");
                        StartLocal(ViewRules.Clone(), ViewMission, ViewDifficulty);
                    }
                    else if (s == HaloSession.Phase.Hub && _wasInMatch && _joinedMatch == ViewMatchId)
                    {
                        _declinedMatch = ViewMatchId;      // left it on my own: not pulled in again (JOIN GAME)
                        HaloLog.Info("Lobby: left the running match; JOIN GAME takes me back.");
                    }
                    else if (inMatch && _joinedMatch >= 0 && _joinedMatch != ViewMatchId)
                    {
                        _joinedMatch = ViewMatchId;
                        HaloSession.StartUtc = ViewStartUtc;
                        HaloLog.Info("Lobby: the master started a new game (" + ViewRules + ") - it replaces this one.");
                        StartLocal(ViewRules.Clone(), ViewMission, ViewDifficulty);
                    }

                    if (inMatch && ViewStartUtc > 0 && Math.Abs(HaloSession.StartUtc - ViewStartUtc) > 0.01) HaloSession.StartUtc = ViewStartUtc;
                }
                else if (inMatch && _joinedMatch >= 0 && s != HaloSession.Phase.Ending)
                {
                    _joinedMatch = -1;
                    SafeLeave("the master's match is over");
                }
            }

            _wasInMatch = inMatch;

            // the players
            _players.Clear();
            if (online) net.Players(_players);
            if (_players.Count == 0)
                _players.Add(new HaloLobbyPlayer { Id = HaloLocal.NetId, Name = LocalName(), Team = HaloLocal.Team, Me = true, Master = master, InMatch = inMatch });
            if (ViewRules != null && ViewRules.Teams && s == HaloSession.Phase.Hub)
            {
                // online the net side decides the team (balanced on arrival, SetTeam): the lobby follows it
                if (online) for (int i = 0; i < _players.Count; i++) if (_players[i].Me && (_players[i].Team == HaloTeams.Red || _players[i].Team == HaloTeams.Blue)) HaloLocal.Team = _players[i].Team;
                if (HaloLocal.Team == HaloTeams.None) HaloLocal.Team = HaloTeams.Red;
            }

            int sig = ViewPhase * 7 + ViewCount * 131 + ViewMatchId * 17 + (IsMaster ? 1 : 0) + (Online ? 2 : 0) + (int)s * 1009 + (_declinedMatch == ViewMatchId ? 5 : 0) + HaloLocal.Team * 3;
            sig = sig * 31 + (IsMaster ? _version : _viewVersion);
            if (MasterName != null) sig = sig * 31 + MasterName.GetHashCode();
            for (int i = 0; i < _players.Count; i++) { HaloLobbyPlayer p = _players[i]; sig = sig * 31 + p.Id * 8 + p.Team * 2 + (p.InMatch ? 1 : 0) + (p.Master ? 4 : 0); }
            Signature = sig + _bump;
        }

        private static int _bump;
        private static void Bump() { _bump++; }

        public static string LocalName()
        {
            IHaloLobbyNet net = HaloLobbyNet.Net;
            if (net != null && !string.IsNullOrEmpty(net.LocalName)) return net.LocalName;
            if (!string.IsNullOrEmpty(HaloAccess.Nick)) return HaloAccess.Nick;
            if (!string.IsNullOrEmpty(HaloLocal.Nick)) return HaloLocal.Nick;
            return "You";
        }

        private static void Publish(bool now)
        {
            IHaloLobbyNet net = HaloLobbyNet.Net;
            if (net == null || !net.Online || !IsMaster) return;
            if (!now && Time.unscaledTime < _nextPublish) return;
            _nextPublish = Time.unscaledTime + 1f;
            try
            {
                net.Publish(new HaloLobbyState
                {
                    Phase = _phase,
                    Count = _phase == HaloLobbyState.Countdown ? Mathf.Max(0, Mathf.CeilToInt(_countEnd - Time.unscaledTime)) : 0,
                    MatchId = _matchId,
                    StartUtc = _publishedStart,
                    Version = _version,
                    Rules = Mine,
                    Mission = _mission,
                    Difficulty = _difficulty,
                });
            }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 3) HaloLog.Warn("Lobby: publishing failed: " + e.Message);
            }
        }
    }

    /// <summary>Halo PC's standard game types (ui.map default_multiplayer_game_setting_names 26..37 and their game_variant_descriptions 36..47) as the lobby offers them.</summary>
    public static class HaloGameTypes
    {
        public sealed class Variant
        {
            public string Name, Description;
            public HaloScenario.GameType Type;
            public bool Teams, Built, MovingHill;
            public int Respawn = -1, NameIndex;

            public HaloRules Make()
            {
                HaloRules r = HaloRules.Defaults(Type, Teams);
                if (MovingHill) r.KingMovingHill = true;
                if (Respawn >= 0) r.RespawnSeconds = Respawn;
                return r;
            }
        }

        private static List<Variant> _all;

        /// <summary>The picture of a game type in ui\shell\bitmaps\game_type_grafix (flag, crown, Spartan, skull, bolt, "?").</summary>
        public static int Picture(HaloScenario.GameType t)
        {
            switch (t)
            {
                case HaloScenario.GameType.Ctf: return 0;
                case HaloScenario.GameType.King: return 1;
                case HaloScenario.GameType.Slayer: return 2;
                case HaloScenario.GameType.Oddball: return 3;
                case HaloScenario.GameType.Race: return 4;
                default: return 5;
            }
        }

        public static List<Variant> All
        {
            get
            {
                if (_all != null && HaloUiArt.Ready) return _all;
                var l = new List<Variant>();
                // (name index, type, teams, built, extras) in the game's own order of the STANDARD bank
                Add(l, 26, "Slayer", HaloScenario.GameType.Slayer, false, true);
                Add(l, 27, "Oddball", HaloScenario.GameType.Oddball, false, true);
                Add(l, 28, "Juggernaut", HaloScenario.GameType.Slayer, false, false);
                Add(l, 29, "King", HaloScenario.GameType.King, false, true);
                Add(l, 30, "Crazy King", HaloScenario.GameType.King, false, true).MovingHill = true;
                Add(l, 31, "Race", HaloScenario.GameType.Race, false, true);
                Add(l, 32, "CTF", HaloScenario.GameType.Ctf, true, true);
                Add(l, 33, "Assault", HaloScenario.GameType.Ctf, true, false);
                Add(l, 34, "Team Slayer", HaloScenario.GameType.Slayer, true, true);
                Add(l, 35, "Team Oddball", HaloScenario.GameType.Oddball, true, true).Respawn = 5;      // "5-second respawn time" (its description)
                Add(l, 36, "Team King", HaloScenario.GameType.King, true, true).Respawn = 10;           // "10-second respawn time"
                Add(l, 37, "Team Race", HaloScenario.GameType.Race, true, true);
                if (HaloUiArt.Ready) _all = l;
                return l;
            }
        }

        private static Variant Add(List<Variant> l, int index, string name, HaloScenario.GameType t, bool teams, bool built)
        {
            HaloUiData d = HaloUiArt.Data;
            var v = new Variant
            {
                NameIndex = index, Type = t, Teams = teams, Built = built,
                Name = d != null ? d.String("default_multiplayer_game_setting_names", index, name) : name,
                Description = d != null ? d.String("game_variant_descriptions", index + 10, "") : "",
            };
            l.Add(v);
            return v;
        }

        /// <summary>The variant a rules object is (by type, teams and the moving hill), or null.</summary>
        public static Variant Of(HaloRules r)
        {
            if (r == null) return null;
            foreach (Variant v in All)
                if (v.Built && v.Type == r.Type && v.Teams == r.Teams && (v.Type != HaloScenario.GameType.King || v.MovingHill == r.KingMovingHill)) return v;
            return null;
        }

        public static string Name(HaloRules r)
        {
            Variant v = Of(r);
            return v != null ? v.Name : r != null ? r.Label : "";
        }
    }
}

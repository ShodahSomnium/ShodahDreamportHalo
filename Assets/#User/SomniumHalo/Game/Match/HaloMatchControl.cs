using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Match module] The whole multiplayer match loop of Halo PC: <see cref="StartMatch"/> (loading screen, map file, world, vehicles,
    /// spawn, HUD - HaloMatchControl.Load.cs), Halo-style spawning (<see cref="HaloSpawns"/>) and respawning after the game's countdown,
    /// the game types (Slayer, CTF, King of the Hill, Oddball, Race: <see cref="HaloObjectives"/>), the score / time limits, game over with
    /// the results and the way back to the hub. The lobby (Front) and the net layer (Net) call it; the HUD reads it.
    /// Scores: every player owns their own counters (HaloLocal.Kills / Deaths / Score, carried on the wire); a team's score is the sum
    /// of its members' (<see cref="TeamScore"/>); a kill is counted by the victim (deaths, suicides) and by the killer when the victim's
    /// announcement arrives (<see cref="ReportKill"/>), as the Black Ops 2 Dreamport did.
    /// </summary>
    public static partial class HaloMatchControl
    {
        /// <summary>Halo's fixed part of the respawn countdown (the variant's respawn time is added).</summary>
        public const float BaseRespawnSeconds = 3f;
        /// <summary>How long the results show after the game ends before everyone is back in the lobby (ours).</summary>
        public static float ResultsSeconds = 12f;
        /// <summary>Kills closer together than this chain into a multi kill (ours: Halo's window is code).</summary>
        public const float MultiKillSeconds = 4f;
        /// <summary>A player this far below the world's lowest point has fallen out of the map (metres, ours).</summary>
        public const float FallOutDepth = 4f;

        static HaloRules _rules;
        static HaloScenario _scenario;
        static bool _subscribed, _dead, _spawnedOnce, _outOfLives;
        static float _respawnAt, _spawnedAt, _verifyAt, _endedAt, _lastKillAt, _localStart;
        static int _multi, _lastBeep, _lastTimerBeep = -1;
        static bool _netTickFailed;
        static Vector3 _spawnPos;
        static float _spawnYaw;

        /// <summary>The rules of the running match (null in the hub).</summary>
        public static HaloRules Rules { get { return _rules; } }
        public static bool Running { get { return HaloSession.Playing; } }
        public static bool GameOver { get; private set; }
        /// <summary>After game over: the winning team (team games, -1 = tie) and the winning player (FFA, int.MinValue = tie / none).</summary>
        public static int WinnerTeam { get; private set; } = HaloTeams.None;
        public static int WinnerId { get; private set; } = int.MinValue;
        /// <summary>After game over: "Red Team wins", "You win", "Tie", ...</summary>
        public static string Result { get; private set; } = "";
        public static bool LocalDead { get { return _dead; } }
        public static bool OutOfLives { get { return _outOfLives; } }

        /// <summary>Points removed players took with them, per team (a member who leaves keeps counting for the team).</summary>
        static readonly int[] Departed = new int[2];

        /// <summary>Seconds since the match started (the shared start when the net layer gave one).</summary>
        public static float Elapsed
        {
            get
            {
                if (!HaloSession.InMatch) return 0f;
                if (HaloSession.StartUtc > 0) return Mathf.Max(0f, (float)(HaloSession.UtcNow - HaloSession.StartUtc));
                return Time.time - _localStart;
            }
        }

        /// <summary>Seconds left of the time limit (-1 = none), for the HUD.</summary>
        public static float TimeLeft
        {
            get
            {
                if (_rules == null || _rules.TimeLimitMinutes <= 0 || !HaloSession.InMatch) return -1f;
                return Mathf.Max(0f, _rules.TimeLimitMinutes * 60f - Elapsed);
            }
        }

        /// <summary>Score of a team (team games) for the HUD / scoreboard: the members' scores (race: min / max / sum of laps by the variant).</summary>
        public static int TeamScore(int team)
        {
            if (team != HaloTeams.Red && team != HaloTeams.Blue) return 0;
            bool race = _rules != null && _rules.Type == HaloScenario.GameType.Race;
            int sum = 0, min = int.MaxValue, max = 0, n = 0;
            for (int i = 0; i < HaloRoster.Players.Count; i++)
            {
                HaloMatchPlayer p = HaloRoster.Players[i];
                if (p.Team != team) continue;
                n++; sum += p.Score;
                if (p.Score < min) min = p.Score;
                if (p.Score > max) max = p.Score;
            }
            if (race && _rules.RaceTeamScoring == 0) return n > 0 ? min : 0;
            if (race && _rules.RaceTeamScoring == 1) return max;
            return sum + (race ? 0 : Departed[team]);
        }

        /// <summary>The player with the highest score (FFA leader), null when nobody.</summary>
        public static HaloMatchPlayer Leader
        {
            get
            {
                HaloMatchPlayer best = null;
                for (int i = 0; i < HaloRoster.Players.Count; i++)
                    if (best == null || HaloRoster.Players[i].Score > best.Score) best = HaloRoster.Players[i];
                return best;
            }
        }

        static bool Teams { get { return _rules != null && (_rules.Teams || _rules.Type == HaloScenario.GameType.Ctf); } }
        static bool Slayer { get { return _rules != null && _rules.Type == HaloScenario.GameType.Slayer; } }

        // ------------------------------------------------------------------ the round

        static void BeginRound(HaloScenario sc, HaloRules rules)
        {
            _scenario = sc;
            GameOver = false; WinnerTeam = HaloTeams.None; WinnerId = int.MinValue; Result = "";
            Departed[0] = Departed[1] = 0;
            _dead = false; _spawnedOnce = false; _outOfLives = false; _multi = 0; _lastKillAt = -100f; _endedAt = 0f; _lastBeep = -1;
            HaloLocal.Kills = HaloLocal.Deaths = HaloLocal.Assists = HaloLocal.Score = 0;
            HaloLocal.Spree = 0; HaloLocal.Carry = 0; HaloLocal.RaceLaps = 0; HaloLocal.RaceNext = 0; HaloLocal.InHill = false;
            HaloLocal.LivesLeft = rules.Lives > 0 ? rules.Lives : -1;
            AssignTeam(rules);
            HaloRoster.SyncLocal(Teams);
            if (HaloSession.StartUtc <= 0) HaloSession.StartUtc = HaloSession.UtcNow;
            _localStart = Time.time;
            HaloObjectives.Begin(sc, rules, _root != null ? _root.transform : null);
            HaloGame.Guard("net begin", () => HaloMatchNet.Begin());
            Subscribe();
        }

        static void ResetRound()
        {
            _rules = null; _scenario = null; GameOver = false; _dead = false; _outOfLives = false;
            HaloLocal.Carry = 0; HaloLocal.InHill = false; HaloLocal.Spree = 0;
        }

        /// <summary>Team games: keep a team the lobby gave, else join the smaller team (ties: red). FFA: no team.</summary>
        static void AssignTeam(HaloRules rules)
        {
            bool teams = rules.Teams || rules.Type == HaloScenario.GameType.Ctf;
            if (!teams) { HaloLocal.Team = HaloTeams.None; return; }
            if (HaloLocal.Team == HaloTeams.Red || HaloLocal.Team == HaloTeams.Blue) return;
            int red = 0, blue = 0;
            for (int i = 0; i < HaloRoster.Players.Count; i++)
            {
                HaloMatchPlayer p = HaloRoster.Players[i];
                if (p.Local) continue;
                if (p.Team == HaloTeams.Red) red++; else if (p.Team == HaloTeams.Blue) blue++;
            }
            HaloLocal.Team = blue < red ? HaloTeams.Blue : HaloTeams.Red;
            HaloLog.Info("Match: team " + HaloTeams.Name(HaloLocal.Team) + " (red " + red + ", blue " + blue + " others)");
        }

        /// <summary>Changes the local player's team in a running team game (the tablet's team swap): counts as a fresh life.</summary>
        public static void SwitchTeam(int team)
        {
            if (!Teams || (team != HaloTeams.Red && team != HaloTeams.Blue) || team == HaloLocal.Team) return;
            HaloObjectives.DropLocal("team change");
            HaloLocal.Team = team;
            HaloLog.Info("Match: switched to " + HaloTeams.Name(team));
            if (HaloSession.Playing && !_dead && _spawnedOnce) TrySpawn(false);      // a fresh life at the new team's start (no death, no penalty)
        }

        static void Subscribe()
        {
            if (_subscribed) return;
            HaloEvents.LocalDied += OnLocalDied;
            HaloEvents.Killed += OnKilled;
            _subscribed = true;
        }

        static void Unsubscribe()
        {
            if (!_subscribed) return;
            HaloEvents.LocalDied -= OnLocalDied;
            HaloEvents.Killed -= OnKilled;
            _subscribed = false;
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>Picks a start location and moves the player there; false when the player cannot be moved yet (retried next frame).</summary>
        static bool TrySpawn(bool first)
        {
            Vector3 p; float yaw; string note;
            bool found = HaloSpawns.Pick(_scenario, _rules, HaloLocal.Team, out p, out yaw, out note);
            string how;
            HaloCombat.NoteTeleport();
            if (!HaloPlayer.Teleport(p, yaw, out how)) return false;
            if (!found) HaloLog.Warn("Match: spawn fallback - " + note);
            Spawned(p, yaw, first, how + ", " + note);
            return true;
        }

        static void SpawnInPlace()
        {
            Vector3 feet;
            if (!HaloPlayer.TryFeet(out feet)) feet = HaloLocal.Position;
            Spawned(feet, 0f, true, "in place");
        }

        static void Spawned(Vector3 p, float yaw, bool first, string note)
        {
            _spawnPos = p; _spawnYaw = yaw;
            _spawnedAt = Time.time; _verifyAt = Time.time + 1.2f;
            _dead = false; _lastBeep = -1;
            HaloLocal.ResetLife();
            HaloLocal.Position = p;
            HaloLocal.Forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            HaloGame.Guard("combat spawned", () => HaloCombat.Spawned());
            HaloEvents.RaiseLocalSpawned(p);
            if (!first) HaloAnnouncer.Say("player_respawn");
            _spawnedOnce = true;
            HaloLog.Info("Match: " + (first ? "spawned" : "respawned") + " at " + p.ToString("0.0") + " facing " + yaw.ToString("0") + " (" + note + ")");
        }

        // ------------------------------------------------------------------ deaths and kills

        static void OnLocalDied(HaloKill k) { Died(k, "event"); }

        /// <summary>Kills the local player (fell out of the map, team change): through the Combat module, which raises the death.</summary>
        public static void KillLocal(string why)
        {
            if (_dead || !HaloSession.Playing) return;
            HaloLog.Info("Match: the local player dies: " + why);
            HaloGame.Guard("combat kill", () => HaloCombat.TakeHit(new HaloHit { Amount = 1e6f, Instant = true, SourcePlayer = -1 }));
            if (!_dead)
            {
                HaloLocal.Alive = false;
                Died(new HaloKill { Killer = -1, Victim = HaloLocal.NetId }, why);
            }
        }

        static void Died(HaloKill k, string how)
        {
            if (_dead || !HaloSession.InMatch || !_spawnedOnce) return;
            _dead = true;
            HaloLocal.Alive = false;
            if (GameOver) return;
            int me = HaloLocal.NetId;
            k.Victim = me;
            bool world = k.Killer < 0 && !(me < 0 && k.Suicide);
            bool suicide = k.Suicide || (!world && k.Killer == me);
            if (suicide) k.Suicide = true;
            if (!world && !suicide && Teams)
            {
                HaloMatchPlayer killer = HaloRoster.Find(k.Killer);
                if (killer != null && !killer.Local && killer.Team == HaloLocal.Team) k.Betrayal = true;
            }
            HaloLocal.Deaths++;
            HaloLocal.Spree = 0;
            if (Slayer && (suicide || world)) HaloLocal.Score--;       // Halo slayer: a suicide (or a death by the world) costs a point
            if (HaloLocal.LivesLeft > 0) { HaloLocal.LivesLeft--; if (HaloLocal.LivesLeft == 0) _outOfLives = true; }
            HaloObjectives.DropLocal("died");
            HaloGame.Guard("vehicles exit", () => HaloVehicles.ExitSeat());
            float delay = BaseRespawnSeconds + Mathf.Max(0, _rules.RespawnSeconds) + (suicide || world ? Mathf.Max(0, _rules.SuicidePenaltySeconds) : 0);
            _respawnAt = Time.time + delay;
            HaloLocal.RespawnIn = _outOfLives ? 0f : delay;
            HaloLog.Info("Match: you died (" + how + ": " + (world ? "the world" : suicide ? "suicide" : "killed by " + k.Killer + (k.Betrayal ? ", betrayal" : "")) + ") - "
                         + (_outOfLives ? "out of lives" : "respawn in " + delay.ToString("0") + " s") + "; score " + HaloLocal.Score + ", deaths " + HaloLocal.Deaths);
            HaloEvents.RaiseKilled(k);
        }

        /// <summary>
        /// A kill announced by a remote victim (the net layer, once per announcement): the kill feed on this screen and, when the local
        /// player is the killer, the kill (+1 in slayer, a betrayal -1), the multi kill and spree announcer. Not for the local death.
        /// </summary>
        public static void ReportKill(HaloKill k)
        {
            if (!HaloSession.InMatch || GameOver) return;
            if (k.Victim == HaloLocal.NetId) return;      // the local death was counted and raised when it happened
            HaloEvents.RaiseKilled(k);                      // the kill feed; OnKilled credits the local killer
        }

        /// <summary>Every kill on this screen (HaloEvents.Killed: the local death raised here, remote deaths raised by the net layer or
        /// ReportKill): when the local player is the killer of someone else, the kill counts here.</summary>
        static void OnKilled(HaloKill k)
        {
            if (!HaloSession.Playing || GameOver) return;
            int me = HaloLocal.NetId;
            if (k.Victim == me) return;
            if (k.Killer == me && me >= 0)
            {
                HaloMatchPlayer victim = HaloRoster.Find(k.Victim);
                if (!k.Betrayal && Teams && victim != null && victim.Team == HaloLocal.Team) k.Betrayal = true;
                if (k.Betrayal)
                {
                    if (Slayer) HaloLocal.Score--;
                    HaloLog.Info("Match: you betrayed " + (victim != null ? victim.Name : k.Victim.ToString()));
                }
                else
                {
                    HaloLocal.Kills++;
                    HaloLocal.Spree++;
                    if (Slayer) HaloLocal.Score++;
                    CountMultiKill();
                    if (HaloLocal.Spree == 5) HaloAnnouncer.Say("killing_spree");
                    else if (HaloLocal.Spree == 10) HaloAnnouncer.Say("running_riot");
                    HaloLog.Info("Match: you killed " + (victim != null ? victim.Name : k.Victim.ToString()) + " (" + HaloLocal.Kills + " kills, score " + HaloLocal.Score + ")");
                }
            }
        }

        static void CountMultiKill()
        {
            float now = Time.time;
            _multi = now - _lastKillAt <= MultiKillSeconds ? _multi + 1 : 1;
            _lastKillAt = now;
            if (_multi == 2) HaloAnnouncer.Say("double_kill");
            else if (_multi == 3) HaloAnnouncer.Say("triple_kill");
            else if (_multi >= 4) HaloAnnouncer.Say("killtacular");
        }

        /// <summary>A remote player's objective event (flag taken / dropped / returned / captured, ball): from the net layer.</summary>
        public static void RemoteObjectiveEvent(int playerId, int code, Vector3 position)
        {
            try { HaloObjectives.RemoteEvent(playerId, code, position); }
            catch (Exception e) { HaloLog.Error("Match: objective event " + code + " of " + playerId + ": " + e); }
        }

        /// <summary>The net layer removes a player: their score stays with their team.</summary>
        public static void PlayerLeft(int id)
        {
            HaloMatchPlayer p = HaloRoster.Find(id);
            if (p == null || p.Local) return;
            if (HaloSession.InMatch && (p.Team == HaloTeams.Red || p.Team == HaloTeams.Blue) && (_rules == null || _rules.Type != HaloScenario.GameType.Race)) Departed[p.Team] += p.Score;
            HaloRoster.Remove(id);
        }

        // ------------------------------------------------------------------ the game's end

        /// <summary>Ends the game now (score / time limit, or the master's "end match"): results for <see cref="ResultsSeconds"/>, then the lobby.</summary>
        public static void EndGame(string why)
        {
            if (!HaloSession.Playing || GameOver) return;
            GameOver = true;
            _endedAt = Time.time;
            HaloObjectives.DropLocal("game over");
            if (Teams)
            {
                int r = TeamScore(HaloTeams.Red), b = TeamScore(HaloTeams.Blue);
                WinnerTeam = r == b ? HaloTeams.None : r > b ? HaloTeams.Red : HaloTeams.Blue;
                Result = WinnerTeam == HaloTeams.None ? "Tie game" : HaloTeams.Name(WinnerTeam) + " wins";
                Result += " " + r + " to " + b;
            }
            else
            {
                HaloMatchPlayer best = null; bool tie = false;
                for (int i = 0; i < HaloRoster.Players.Count; i++)
                {
                    HaloMatchPlayer p = HaloRoster.Players[i];
                    if (best == null || p.Score > best.Score) { best = p; tie = false; }
                    else if (p.Score == best.Score) tie = true;
                }
                WinnerId = best != null && !tie ? best.Id : int.MinValue;
                Result = best == null || tie ? "Tie game" : best.Local ? "You win" : best.Name + " wins";
            }
            HaloLog.Info("Match: game over (" + why + "): " + Result + " - your kills " + HaloLocal.Kills + ", deaths " + HaloLocal.Deaths + ", score " + HaloLocal.Score
                         + " after " + Elapsed.ToString("0") + " s");
            if (why == "time limit") HaloAnnouncer.Say("countdown_timer_end");
            HaloAnnouncer.Say("game_over");
            HaloSession.SetPhase(HaloSession.Phase.Ending);
            HaloGame.Guard("combat clear", () => HaloCombat.Clear());
            HaloGame.Guard("scoreboard", () => HaloHud.ShowScoreboard(true));
        }

        static void CheckLimits()
        {
            if (_rules == null || GameOver) return;
            if (_rules.TimeLimitMinutes > 0 && Elapsed >= _rules.TimeLimitMinutes * 60f) { EndGame("time limit"); return; }
            int limit = _rules.ScoreLimit;
            if (limit > 0)
            {
                if (Teams)
                {
                    if (TeamScore(HaloTeams.Red) >= limit || TeamScore(HaloTeams.Blue) >= limit) { EndGame("score limit"); return; }
                }
                else
                {
                    for (int i = 0; i < HaloRoster.Players.Count; i++) if (HaloRoster.Players[i].Score >= limit) { EndGame("score limit"); return; }
                }
            }
            if (_outOfLives && HaloRoster.RemoteCount == 0) EndGame("out of lives");
        }

        // ------------------------------------------------------------------ every frame (HaloGame.MatchTick)

        public static void Tick()
        {
            if (HaloCampaign.Active) return;      // a campaign mission runs its own flow (K: leave, falls, deaths)
            HaloSession.Phase ph = HaloSession.Current;
            if (ph != HaloSession.Phase.Playing && ph != HaloSession.Phase.Ending) return;
            float dt = Time.deltaTime;
            Vector3 feet;
            if (!HaloPlayer.TryFeet(out feet)) feet = HaloLocal.Position;
            float yaw; string src;
            if (HaloPlayer.TryFacingYaw(out yaw, out src)) HaloRoster.Me.Forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 fwd = HaloRoster.Me.Forward;
            HaloRoster.SyncLocal(Teams);
            HaloRoster.Me.Position = feet;
            HaloRoster.Me.Forward = fwd;
            try { HaloMatchNet.Tick(); } catch (Exception e) { if (!_netTickFailed) { _netTickFailed = true; HaloLog.Error("Match: net tick: " + e); } }

            if (ph == HaloSession.Phase.Ending)
            {
                HaloObjectives.Tick(dt, Elapsed, feet, false, true);
                if (_leaving != null) return;
                if (Time.time - _endedAt >= ResultsSeconds) { HaloLog.Info("Match: results shown - back to the lobby"); LeaveMatch(); }
                else if (HaloPlayer.BackKeyPressed()) LeaveMatch();
                return;
            }

            // a death the Combat module did not announce (Alive went false without HaloEvents.LocalDied)
            if (!_dead && _spawnedOnce && !HaloLocal.Alive) Died(new HaloKill { Killer = -1, Victim = HaloLocal.NetId }, "no death event");

            if (_dead)
            {
                if (!_outOfLives)
                {
                    float left = _respawnAt - Time.time;
                    HaloLocal.RespawnIn = Mathf.Max(0f, left);
                    int sec = Mathf.CeilToInt(left);
                    if (sec != _lastBeep && sec >= 1 && sec <= 3) { _lastBeep = sec; HaloAnnouncer.Say("countdown_for_respawn"); }
                    if (left <= 0f) TrySpawn(false);
                }
            }
            else
            {
                // the client applies a teleport asynchronously: once after a spawn, check the player really arrived
                if (_verifyAt > 0f && Time.time >= _verifyAt)
                {
                    _verifyAt = 0f;
                    if ((feet - _spawnPos).sqrMagnitude > 25f * 25f)
                    {
                        string how;
                        HaloCombat.NoteTeleport();
                        HaloPlayer.Teleport(_spawnPos, _spawnYaw, out how);
                        HaloLog.Warn("Match: the player was " + (feet - _spawnPos).magnitude.ToString("0") + " m from the spawn after the teleport - moved again (" + how + ")");
                    }
                }
                // Halo kills a player who falls out of the world
                if (Time.time - _spawnedAt > 2f && HaloWorld.Ready)
                {
                    Bounds b = HaloWorld.Bounds;
                    if (b.size.y > 0.5f && feet.y < b.min.y - FallOutDepth) KillLocal("fell out of the map at " + feet.ToString("0"));
                }
            }

            // (until the client has applied a spawn teleport the feet are still where the player died: no touches there)
            bool arrived = Time.time - _spawnedAt > 2f || (feet - _spawnPos).sqrMagnitude < 4f;
            HaloObjectives.Tick(dt, Elapsed, feet, HaloLocal.Alive && !_dead && arrived, false);
            float tl = TimeLeft;      // the last ten seconds of a timed game beep
            if (tl >= 0f && tl <= 10f) { int s = Mathf.CeilToInt(tl); if (s != _lastTimerBeep && s >= 1) { _lastTimerBeep = s; HaloAnnouncer.Say("countdown_timer"); } }
            HaloRoster.Me.Score = HaloLocal.Score;
            CheckLimits();
            if (HaloPlayer.BackKeyPressed()) LeaveMatch();
        }
    }
}

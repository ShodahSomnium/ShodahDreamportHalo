using System.Collections;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Match module labs (DevTools/lab.sh match-slayer | match-modes):
    /// match-slayer: a Slayer match on Blood Gulch from code - loading to Playing, the player at a start location, a death and the respawn,
    ///   kills to the score limit, game over, results, back in the hub with the map gone.
    /// match-modes: CTF (take the enemy flag, capture it), King of the Hill (score in the hill), a teleporter, Oddball (score with the ball,
    ///   drop on death), Race (a lap through the checkpoints) - each a fresh match on Blood Gulch, pictures of the objects.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabMatch
    {
        static HaloLabMatch()
        {
            HaloLabRunner.Register("match-slayer", r => Slayer(r));
            HaloLabRunner.Register("match-modes", r => Modes(r));
        }

        static string MapArg { get { return HaloLabBoot.Arg("-halomap", "bloodgulch"); } }

        static IEnumerator Seconds(float s)
        {
            float t = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < t) yield return null;
        }

        /// <summary>The editor has no Somnium player: a CharacterController stands in (HaloPlayer.Teleport moves it, TryFeet reads it).</summary>
        static void EnsurePlayer()
        {
            CharacterController sdk = Object.FindFirstObjectByType<CharacterController>();
            if (sdk != null)
            {
                // the SDK's test player moves itself (gravity, its own velocity): after the lab's teleports that looked like falls to the
                // Combat module. Its scripts are switched off so the player stands where the lab puts it.
                foreach (MonoBehaviour b in sdk.GetComponents<MonoBehaviour>()) { b.enabled = false; HaloLabRunner.Log("lab player: disabled " + b.GetType().Name + " on " + sdk.name); }
                return;
            }
            var go = new GameObject("Lab Player");
            go.transform.position = HaloSpace.RoomOrigin;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            go.layer = HaloLayers.Avatar;
        }

        static Vector3 Feet()
        {
            Vector3 f;
            return HaloPlayer.TryFeet(out f) ? f : Vector3.zero;
        }

        static IEnumerator Start(HaloLabRunner r, HaloRules rules, string what)
        {
            HaloMatchControl.StartMatch(rules);
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Playing, 600f, what + ": the match to start");
            r.Check(HaloSession.Playing, what + ": playing (" + HaloMatchControl.Rules + ")");
            yield return Seconds(1.6f);      // past the spawn check (1.2 s) before the lab moves the player
        }

        static IEnumerator Leave(HaloLabRunner r, string what)
        {
            HaloMatchControl.LeaveMatch();
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Hub, 10f, what + ": back to the hub");
            yield return null;
        }

        static void Teleport(Vector3 p)
        {
            string how;
            HaloPlayer.Teleport(HaloSpawns.Ground(p), 0f, out how);
        }

        /// <summary>Lab camera at the player's eye looking along the facing, and one from above-behind.</summary>
        static void Views(HaloLabRunner r, string name, Vector3 feet, float yaw)
        {
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            r.Aim(feet + Vector3.up * 1.7f, feet + Vector3.up * 1.5f + fwd * 10f, 75f);
            r.Shot(name + "-eye");
            r.Aim(feet + Vector3.up * 6f - fwd * 8f, feet + Vector3.up * 0.8f, 60f);
            r.Shot(name + "-above");
        }

        static IEnumerator Slayer(HaloLabRunner r)
        {
            EnsurePlayer();
            int savedId = HaloLocal.NetId;

            float savedResults = HaloMatchControl.ResultsSeconds;
            HaloMatchControl.ResultsSeconds = 4f;
            var rules = HaloRules.Defaults(HaloScenario.GameType.Slayer, false);
            rules.MapName = MapArg;
            rules.ScoreLimit = 3;
            float t0 = Time.realtimeSinceStartup;
            yield return Start(r, rules, "slayer");
            if (!HaloSession.Playing) yield break;
            HaloRunnerLog("loaded in " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s, world ready " + HaloWorld.Ready + ", bounds " + HaloWorld.Bounds);
            r.Check(HaloSession.Map != null && HaloSession.Scenario != null && HaloSession.WorldRoot != null, "map, scenario and world root are open");

            Vector3 feet = Feet();
            float best = NearestStart(feet, rules);
            r.Check(best < 1.0f, "the player stands on a Slayer start location (" + best.ToString("0.00") + " m away, at " + feet.ToString("0.0") + ")");
            r.Check(HaloLocal.Alive && HaloLocal.Team == HaloTeams.None, "alive, no team (FFA)");
            float yaw; string src;
            HaloPlayer.TryFacingYaw(out yaw, out src);
            Views(r, "spawn", feet, yaw);

            // a death (a suicide: -1 in slayer) and the respawn after Halo's 3 s
            HaloLocal.Alive = false;
            HaloEvents.RaiseLocalDied(new HaloKill { Killer = HaloLocal.NetId, Victim = HaloLocal.NetId, Suicide = true });
            yield return null;
            r.Check(HaloMatchControl.LocalDead && HaloLocal.RespawnIn > 2.5f, "dead, respawn countdown " + HaloLocal.RespawnIn.ToString("0.0") + " s");
            r.Check(HaloLocal.Deaths == 1 && HaloLocal.Score == -1, "the suicide: deaths " + HaloLocal.Deaths + ", score " + HaloLocal.Score);
            float died = Time.realtimeSinceStartup;
            yield return r.Wait(() => HaloLocal.Alive, 10f, "the respawn");
            float took = Time.realtimeSinceStartup - died;
            r.Check(HaloLocal.Alive && took > 2.5f && took < 4.5f, "respawned after " + took.ToString("0.0") + " s");
            yield return null;
            feet = Feet();
            r.Check(NearestStart(feet, rules) < 1.0f, "respawned on a start location at " + feet.ToString("0.0"));

            // a fall out of the world (only when the world has bounds)
            if (HaloWorld.Ready && HaloWorld.Bounds.size.y > 0.5f)
            {
                yield return Seconds(2.2f);
                Teleport(new Vector3(feet.x, HaloWorld.Bounds.min.y - 10f, feet.z));
                yield return Seconds(0.3f);
                r.Check(HaloMatchControl.LocalDead && HaloLocal.Deaths == 2 && HaloLocal.Score == -2, "fell out of the map: dead, deaths " + HaloLocal.Deaths + ", score " + HaloLocal.Score);
                yield return r.Wait(() => HaloLocal.Alive, 10f, "the respawn after the fall");
            }
            else HaloRunnerLog("no world bounds (stub world): the fall check is skipped");

            // kills announced by victims: +1 each up to the score limit (3)
            int need = rules.ScoreLimit - HaloLocal.Score;
            int feed = 0;
            System.Action<HaloKill> count = k => feed++;
            HaloEvents.Killed += count;
            for (int i = 0; i < need; i++)
            {
                HaloLocal.NetId = 7;      // (the net layer writes -1 every frame while unclaimed: set it in the same frame)
                HaloMatchControl.ReportKill(new HaloKill { Killer = 7, Victim = 20 + i });
                yield return null;
            }
            HaloEvents.Killed -= count;
            r.Check(feed == need, "kill feed: " + feed + " kills raised");
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Ending, 3f, "game over by the score limit");
            r.Check(HaloMatchControl.GameOver && HaloMatchControl.Result == "You win", "game over: " + HaloMatchControl.Result + " (kills " + HaloLocal.Kills + ", score " + HaloLocal.Score + ")");
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Hub, 12f, "back to the lobby after the results");
            yield return null;
            feet = Feet();
            r.Check((feet - HaloSpace.RoomOrigin).magnitude < 2f, "the player is back in the hub at " + feet.ToString("0.0"));
            r.Check(GameObject.Find("Halo Map " + MapArg) == null && HaloSession.Map == null, "the map is gone");
            HaloMatchControl.ResultsSeconds = savedResults;
            HaloLocal.NetId = savedId;
        }

        static void HaloRunnerLog(string s) { HaloLabRunner.Log(s); }

        static float NearestStart(Vector3 feet, HaloRules rules)
        {
            float best = 1e9f;
            HaloScenario sc = HaloSession.Scenario;
            if (sc == null) return best;
            foreach (var s in sc.StartLocations)
            {
                if (!s.Allows(rules.Type)) continue;
                Vector3 p = HaloSpace.MapOrigin + HaloSpace.Pos(s.Position);
                Vector3 d = feet - p; d.y = 0f;
                if (d.magnitude < best) best = d.magnitude;
            }
            return best;
        }

        static IEnumerator Modes(HaloLabRunner r)
        {
            EnsurePlayer();
            int savedId = HaloLocal.NetId;

            // ---- CTF: red team takes the blue flag and brings it home
            var ctf = HaloRules.Defaults(HaloScenario.GameType.Ctf, true);
            ctf.MapName = MapArg;
            HaloLocal.Team = HaloTeams.Red;
            yield return Start(r, ctf, "ctf");
            if (!HaloSession.Playing) yield break;
            r.Check(HaloLocal.Team == HaloTeams.Red, "ctf: red team");
            var blue = HaloObjectives.BlueFlag;
            var red = HaloObjectives.RedFlag;
            r.Check(blue.Present && red.Present, "ctf: both flags (red " + red.Home.ToString("0.0") + ", blue " + blue.Home.ToString("0.0") + ")");
            Views(r, "ctf-redflag", red.Home + Vector3.back * 3f, 0f);
            Teleport(blue.Home);
            yield return Seconds(0.4f);
            r.Check(HaloLocal.Carry == HaloObjectives.CarryBlueFlag && blue.State == HaloObjectives.State.Carried, "ctf: took the blue flag (carry " + HaloLocal.Carry + ")");
            Teleport(red.Home + Vector3.right * 0.3f);
            yield return Seconds(0.4f);
            r.Check(HaloLocal.Score == 1 && HaloLocal.Carry == 0 && blue.State == HaloObjectives.State.Home, "ctf: captured (score " + HaloLocal.Score + ", team " + HaloMatchControl.TeamScore(HaloTeams.Red) + ")");
            // drop on death, return by touch
            Teleport(blue.Home);
            yield return Seconds(0.4f);
            Vector3 dropAt = blue.Home + new Vector3(6f, 0f, 0f);
            Teleport(dropAt);
            yield return Seconds(0.2f);
            HaloLocal.Alive = false;
            HaloEvents.RaiseLocalDied(new HaloKill { Killer = 30, Victim = HaloLocal.NetId });
            yield return null;
            r.Check(blue.State == HaloObjectives.State.Dropped, "ctf: the flag dropped on death at " + blue.Position.ToString("0.0"));
            yield return r.Wait(() => HaloLocal.Alive, 10f, "ctf respawn");
            r.Check(HaloLocal.Score == 1 && HaloLocal.Deaths == 1, "ctf: a death costs no capture (score " + HaloLocal.Score + ")");
            yield return Leave(r, "ctf");

            // ---- King of the Hill + a teleporter
            var king = HaloRules.Defaults(HaloScenario.GameType.King, false);
            king.MapName = MapArg;
            yield return Start(r, king, "king");
            if (!HaloSession.Playing) yield break;
            var hill = HaloObjectives.CurrentHill;
            r.Check(hill != null, "king: a hill (" + HaloObjectives.Hills.Count + " hills)");
            if (hill != null)
            {
                Teleport(HaloSpawns.Ground(hill.Center));
                yield return Seconds(3.4f);
                r.Check(HaloLocal.InHill && HaloLocal.Score >= 2, "king: in the hill, score " + HaloLocal.Score);
                Views(r, "king-hill", hill.Center + (hill.Center - hill.Outline[0]).normalized * 6f, 180f);
                r.Aim(hill.Center + new Vector3(0f, 9f, -9f), hill.Center, 50f);
                r.Shot("king-hill-outline");
            }
            HaloScenario.NetgameFlag from = null, to = null;
            foreach (var f in HaloSession.Scenario.Flags) if (f.Type == HaloScenario.FlagType.TeleportFrom) { from = f; break; }
            if (from != null) foreach (var f in HaloSession.Scenario.Flags) if (f.Type == HaloScenario.FlagType.TeleportTo && f.UsageId == from.UsageId) { to = f; break; }
            if (from != null && to != null)
            {
                Vector3 exit = HaloSpace.MapOrigin + HaloSpace.Pos(to.Position);
                Teleport(HaloSpace.MapOrigin + HaloSpace.Pos(from.Position));
                yield return Seconds(0.4f);
                Vector3 d = Feet() - exit; d.y = 0f;
                r.Check(d.magnitude < 1.5f, "teleporter " + from.UsageId + ": the player is at the exit (" + d.magnitude.ToString("0.00") + " m)");
            }
            else HaloRunnerLog("no teleporter pair on " + MapArg);
            yield return Leave(r, "king");

            // ---- Oddball
            var odd = HaloRules.Defaults(HaloScenario.GameType.Oddball, false);
            odd.MapName = MapArg;
            yield return Start(r, odd, "oddball");
            if (!HaloSession.Playing) yield break;
            var ball = HaloObjectives.Ball;
            r.Check(ball.Present, "oddball: the ball at " + ball.Home.ToString("0.0"));
            Views(r, "oddball", ball.Home + Vector3.back * 3f, 0f);
            Teleport(ball.Home);
            yield return Seconds(2.4f);
            r.Check(HaloLocal.Carry == HaloObjectives.CarryBall && HaloLocal.Score >= 2, "oddball: carrying, score " + HaloLocal.Score);
            HaloMatchControl.KillLocal("lab");
            yield return null;
            r.Check(ball.State == HaloObjectives.State.Dropped && HaloLocal.Carry == 0, "oddball: dropped on death");
            yield return Leave(r, "oddball");

            // ---- Race: one lap
            var race = HaloRules.Defaults(HaloScenario.GameType.Race, false);
            race.MapName = MapArg;
            yield return Start(r, race, "race");
            if (!HaloSession.Playing) yield break;
            int n = HaloObjectives.Checkpoints.Count;
            r.Check(n > 0, "race: " + n + " checkpoints");
            for (int i = 0; i < n; i++) { Teleport(HaloSpawns.Ground(HaloObjectives.Checkpoints[i])); yield return Seconds(0.3f); }
            r.Check(HaloLocal.RaceLaps == 1 && HaloLocal.Score == 1, "race: one lap (laps " + HaloLocal.RaceLaps + ", next " + HaloLocal.RaceNext + ")");
            yield return Leave(r, "race");
            HaloLocal.NetId = savedId;
        }
    }
}

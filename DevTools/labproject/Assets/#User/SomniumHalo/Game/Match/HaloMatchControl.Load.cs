using System;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>The way into a match (loading screen, map file on a worker, world build, vehicles, first spawn) and back out to the hub.</summary>
    public static partial class HaloMatchControl
    {
        static int _gen;                     // bumps on every start / leave: a load that belongs to an older generation stops
        static Coroutine _loading;
        static GameObject _root;
        static string _mapPath;
        static float _loadStarted;

        // worker -> main thread
        sealed class OpenJob { public string File; public volatile bool Done; public HaloMap Map; public HaloScenario Scenario; public string Error; public long Ms; }

        /// <summary>Loads the rules' map and starts playing them (the instance master's START, or a late join).</summary>
        public static void StartMatch(HaloRules rules)
        {
            if (rules == null) rules = new HaloRules();
            HaloGame game = HaloGame.Current;
            if (game == null) { HaloLog.Warn("Match: StartMatch before the Dreamport started - ignored"); return; }
            if (HaloSession.Current != HaloSession.Phase.Hub)
            {
                if (_rules != null && string.Equals(_rules.MapName, rules.MapName, StringComparison.OrdinalIgnoreCase) && _rules.Type == rules.Type && _rules.Teams == rules.Teams)
                {
                    HaloLog.Info("Match: StartMatch for the match already " + (HaloSession.Current == HaloSession.Phase.Loading ? "loading" : "running") + " - ignored");
                    return;
                }
                HaloLog.Info("Match: a new match replaces the current one");
                double keepStart = HaloSession.StartUtc;
                Teardown("a new match", false);
                HaloSession.StartUtc = keepStart;
            }
            if (game.Install == null) { game.SetStatus("Halo is not found yet - cannot start a match.", true); HaloLog.Warn("Match: no install - StartMatch ignored"); return; }
            HaloInstall.MapEntry entry = game.Install.Map(rules.MapName);
            if (entry == null) { game.SetStatus("The map '" + rules.MapName + "' is not in your Halo install.", true); HaloLog.Warn("Match: map " + rules.MapName + " not installed"); return; }

            _rules = rules.Clone();
            int gen = ++_gen;
            _loadStarted = Time.realtimeSinceStartup;
            HaloLog.Info("Match: starting " + _rules + " (" + entry.File + ")" + (HaloSession.StartUtc > 0 ? ", joining the start at UTC " + HaloSession.StartUtc.ToString("0") : ""));
            HaloSession.SetPhase(HaloSession.Phase.Loading);
            game.SetStatus("Loading " + entry.Label + "...", false);
            HaloGame.Guard("loading show", () => HaloLoading.Show(entry, _rules));
            _loading = game.StartCoroutine(Load(entry, gen));
        }

        static void Progress(float f, string label)
        {
            try { HaloLoading.Progress(Mathf.Clamp01(f), label); }
            catch (Exception e) { HaloLog.Warn("Match: loading progress: " + e.Message); }
        }

        static IEnumerator Load(HaloInstall.MapEntry entry, int gen)
        {
            HaloRules rules = _rules;
            Progress(0.02f, "Opening " + entry.Label);
            var job = new OpenJob { File = entry.File };
            var thread = new Thread(() => OpenMap(job), 16 * 1024 * 1024) { IsBackground = true, Name = "Halo map " + entry.Name };
            thread.Start();
            while (!job.Done)
            {
                if (gen != _gen) yield break;
                Progress(0.02f + Mathf.Min(0.1f, (Time.realtimeSinceStartup - _loadStarted) * 0.03f), "Opening " + entry.Label);
                yield return null;
            }
            if (gen != _gen) yield break;
            if (job.Error != null) { Fail("Opening " + entry.Label + " failed: " + job.Error); yield break; }
            HaloLog.Info("Match: opened " + entry.Name + " in " + job.Ms + " ms: " + job.Map.Tags.Length + " tags, " + job.Scenario.StartLocations.Count + " starting locations, "
                         + job.Scenario.Flags.Count + " netgame flags, " + job.Scenario.NetEquipment.Count + " netgame equipment, " + job.Scenario.Vehicles.Count + " vehicles");

            _mapPath = job.Map.Path;
            _root = new GameObject("Halo Map " + entry.Name);
            _root.transform.SetPositionAndRotation(HaloSpace.MapOrigin, Quaternion.identity);
            HaloAnnouncer.Load(job.Map);
            HaloSession.Open(entry, job.Map, job.Scenario, _root.transform, rules);
            HaloGame.Guard("audio preload", () => HaloAudio.Preload(job.Map));
            HaloGame.Guard("fx preload", () => HaloFx.Preload(job.Map));
            Progress(0.15f, "Building " + entry.Label);
            yield return null;

            // the world (BSP, collision, sky, scenery): the World module's builder over several frames
            IEnumerator build = null;
            try { build = HaloWorld.Build(job.Map, job.Scenario, _root.transform, (f, l) => Progress(0.15f + 0.75f * Mathf.Clamp01(f), l)); }
            catch (Exception e) { HaloLog.Error("Match: starting the world build failed: " + e); }
            while (build != null)
            {
                if (gen != _gen) yield break;
                bool more;
                try { more = build.MoveNext(); }
                catch (Exception e) { HaloLog.Error("Match: the world build failed: " + e); break; }
                if (!more) break;
                yield return build.Current;
            }
            if (gen != _gen) yield break;
            HaloLog.Info("Match: world " + (HaloWorld.Ready ? "ready" : "NOT ready") + ", bounds " + HaloWorld.Bounds.min.ToString("0") + " .. " + HaloWorld.Bounds.max.ToString("0")
                         + " (" + (Time.realtimeSinceStartup - _loadStarted).ToString("0.0") + " s)");
            Physics.SyncTransforms();
            HaloSession.NotifyWorldBuilt();
            Progress(0.92f, "Placing vehicles");
            HaloGame.Guard("vehicles spawn", () => HaloVehicles.Spawn(rules));
            yield return null;
            if (gen != _gen) yield break;

            BeginRound(job.Scenario, rules);
            Progress(0.96f, "Spawning");

            // the first spawn: the client applies a teleport a few frames later and may not have the player yet
            float until = Time.realtimeSinceStartup + 15f;
            while (!TrySpawn(true))
            {
                if (gen != _gen) yield break;
                if (Time.realtimeSinceStartup > until) { HaloLog.Warn("Match: the player could not be moved to a start location for 15 s - playing where they are"); SpawnInPlace(); break; }
                yield return null;
            }

            Progress(1f, "Ready");
            HaloGame.Guard("loading hide", () => HaloLoading.Hide());
            HaloGame.Guard("hud start", () => HaloHud.Start());
            HaloGame.Guard("desk start", () => HaloDesk.Start());
            HaloSession.SetPhase(HaloSession.Phase.Playing);
            _loading = null;
            HaloAnnouncer.GameType(rules, HaloLocal.Team);
            if (HaloGame.Current != null) HaloGame.Current.SetStatus("Playing " + rules.Label + " on " + entry.Label, false);
            HaloLog.Info("Match: playing " + rules.Label + " on " + entry.Label + " - loaded in " + (Time.realtimeSinceStartup - _loadStarted).ToString("0.0") + " s, team " + HaloTeams.Name(HaloLocal.Team)
                         + ", start UTC " + HaloSession.StartUtc.ToString("0"));
        }

        static void OpenMap(OpenJob job)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                job.Map = new HaloMap(job.File);
                job.Scenario = new HaloScenario(job.Map);
            }
            catch (Exception e) { job.Error = e.Message; HaloLog.Warn("Match: opening " + job.File + ": " + e); }
            job.Ms = sw.ElapsedMilliseconds;
            job.Done = true;
        }

        static void Fail(string message)
        {
            HaloLog.Warn("Match: " + message);
            Teardown("failed", true);
            if (HaloGame.Current != null) HaloGame.Current.SetStatus(message, true);
        }

        /// <summary>Back to the ring hub.</summary>
        public static void LeaveMatch()
        {
            if (HaloSession.Current == HaloSession.Phase.Hub && _root == null) return;
            if (_leaving != null) return;
            HaloGame game = HaloGame.Current;
            if (game != null && HaloSession.InMatch)
            {
                // the Front's cover fades to black (0.35 s) before the world goes; the hub fades in once the player stands in it
                HaloGame.Guard("loading leave", () => HaloLoading.Leave());
                _leaving = game.StartCoroutine(LeaveSoon(_gen));
                return;
            }
            Teardown("left", true);
            if (game != null) game.SetStatus("Back in the hub.", false);
        }

        static Coroutine _leaving;

        static IEnumerator LeaveSoon(int gen)
        {
            float until = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < until) yield return null;
            _leaving = null;
            if (gen != _gen) yield break;
            Teardown("left", true);
            if (HaloGame.Current != null) HaloGame.Current.SetStatus("Back in the hub.", false);
        }

        /// <summary>Everything of the match goes; <paramref name="toHub"/> = the player is put back into the ring hub.</summary>
        static void Teardown(string why, bool toHub)
        {
            _gen++;
            if (_leaving != null && HaloGame.Current != null) { try { HaloGame.Current.StopCoroutine(_leaving); } catch (Exception) { } }
            _leaving = null;
            bool had = _root != null || HaloSession.Current != HaloSession.Phase.Hub;
            if (_loading != null && HaloGame.Current != null) { try { HaloGame.Current.StopCoroutine(_loading); } catch (Exception) { } }
            _loading = null;
            if (!had) return;
            HaloLog.Info("Match: leaving (" + why + ")");
            Unsubscribe();
            HaloObjectives.DropLocal("the match ends");
            HaloSession.Close();      // Unloading: modules clean up what they own (the world root is still alive)
            HaloGame.Guard("combat clear", () => HaloCombat.Clear());
            HaloGame.Guard("vehicles clear", () => HaloVehicles.Clear());
            HaloGame.Guard("fx clear", () => HaloFx.Clear());
            HaloGame.Guard("scoreboard", () => HaloHud.ShowScoreboard(false));
            HaloGame.Guard("hud stop", () => HaloHud.Stop());
            HaloGame.Guard("desk stop", () => HaloDesk.Stop());
            HaloGame.Guard("net end", () => HaloMatchNet.End());
            HaloGame.Guard("world unload", () => HaloWorld.Unload());
            HaloObjectives.End();
            HaloAnnouncer.Clear();
            if (_root != null) { UnityEngine.Object.Destroy(_root); _root = null; }
            if (_mapPath != null)
            {
                string path = _mapPath;
                HaloGame.Guard("textures release", () => HaloTextures.Release(path));
                HaloGame.Guard("materials release", () => HaloMaterials.Release(path));
                _mapPath = null;
            }
            if (HaloLoading.Showing) HaloGame.Guard("loading hide", () => HaloLoading.Hide());
            HaloRoster.ClearRemotes();
            ResetRound();
            HaloLocal.Alive = false; HaloLocal.RespawnIn = 0f; HaloLocal.Vehicle = null; HaloLocal.Seat = ""; HaloLocal.Driving = false;
            if (toHub)
            {
                string how;
                bool moved = HaloPlayer.Teleport(HaloSpace.RoomOrigin + new Vector3(0f, 0.2f, 0f), 0f, out how);
                HaloLog.Info("Match: back to the hub (" + (moved ? how : "not moved: " + how) + ")");
            }
            HaloSession.SetPhase(HaloSession.Phase.Hub);
            try { Resources.UnloadUnusedAssets(); } catch (Exception) { }
            GC.Collect();
        }

        /// <summary>The scene is going (HaloGame.OnDestroy): tear down without moving anybody.</summary>
        public static void Stop()
        {
            try { Teardown("the world closes", false); }
            catch (Exception e) { HaloLog.Error("Match: stop: " + e); }
        }
    }
}

using System;
using System.Collections;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>The way into a mission (loading screen, map on a worker, world for the start BSP, the scenario's objects by name, the player at
    /// the campaign start with the starting profile, scripts) and back out to the hub - the Match module's recipe (HaloMatchControl.Load.cs).</summary>
    public static partial class HaloCampaign
    {
        static int _gen;                     // bumps on every start / leave: a load of an older generation stops
        static Coroutine _loading, _leaving, _switching;
        static GameObject _root;
        static string _mapPath;
        static float _loadStarted;
        static HaloInstall.MapEntry _entry;

        sealed class OpenJob { public string File; public volatile bool Done; public HaloMap Map; public HaloScenario Scenario; public HaloMissionData Data; public string Error; public long Ms; }

        /// <summary>The root everything of the mission hangs under (null in the hub).</summary>
        public static Transform Root { get { return _root != null ? _root.transform : null; } }

        static void BeginMission(string mission, Difficulty difficulty)
        {
            HaloGame game = HaloGame.Current;
            if (game == null) { HaloLog.Warn("Campaign: StartMission before the Dreamport started - ignored"); return; }
            mission = (mission ?? "").Trim().ToLowerInvariant();
            if (Active || HaloSession.Current != HaloSession.Phase.Hub)
            {
                if (Active && Mission == mission && Level == difficulty && HaloSession.Current == HaloSession.Phase.Loading) { HaloLog.Info("Campaign: " + mission + " is already loading - ignored"); return; }
                if (!Active) { HaloLog.Warn("Campaign: a multiplayer match is running - leave it first (StartMission " + mission + " ignored)"); return; }
                HaloLog.Info("Campaign: a new mission replaces " + Mission);
                Teardown("a new mission", false);
            }
            if (game.Install == null) { game.SetStatus("Halo is not found yet - cannot start a mission.", true); HaloLog.Warn("Campaign: no install - StartMission ignored"); return; }
            HaloInstall.MapEntry entry = null;
            foreach (var m in game.Install.Campaign) if (string.Equals(m.Name, mission, StringComparison.OrdinalIgnoreCase)) entry = m;
            if (entry == null) { game.SetStatus("The mission '" + mission + "' is not in your Halo install.", true); HaloLog.Warn("Campaign: mission " + mission + " not installed"); return; }

            _entry = entry;
            int gen = ++_gen;
            _loadStarted = Time.realtimeSinceStartup;
            SetState(true, entry.Name, difficulty, 0);
            Ticks = 0; TickFraction = 0f; _acc = 0f;
            ResetRun();
            HaloLog.Info("Campaign: starting " + entry.Name + " (" + entry.Label + ") on " + Name(difficulty) + " (" + entry.File + ")");
            HaloSession.SetPhase(HaloSession.Phase.Loading);
            game.SetStatus("Loading " + entry.Label + "...", false);
            HaloGame.Guard("loading show", () => HaloLoading.Show(entry, null));
            _loading = game.StartCoroutine(Load(entry, gen));
        }

        static void Progress(float f, string label)
        {
            try { HaloLoading.Progress(Mathf.Clamp01(f), label); }
            catch (Exception e) { HaloLog.Warn("Campaign: loading progress: " + e.Message); }
        }

        static IEnumerator Load(HaloInstall.MapEntry entry, int gen)
        {
            Progress(0.02f, "Opening " + entry.Label);
            var job = new OpenJob { File = entry.File };
            var thread = new Thread(() => OpenMap(job), 16 * 1024 * 1024) { IsBackground = true, Name = "Halo mission " + entry.Name };
            thread.Start();
            while (!job.Done)
            {
                if (gen != _gen) yield break;
                Progress(0.02f + Mathf.Min(0.1f, (Time.realtimeSinceStartup - _loadStarted) * 0.03f), "Opening " + entry.Label);
                yield return null;
            }
            if (gen != _gen) yield break;
            if (job.Error != null) { Fail("Opening " + entry.Label + " failed: " + job.Error); yield break; }
            Data = job.Data;
            HaloScenario sc = job.Scenario;

            // the start BSP: the first player starting location's (campaign starts carry their BSP)
            int startBsp = 0;
            if (sc.StartLocations.Count > 0) startBsp = Mathf.Clamp(sc.StartLocations[0].BspIndex, 0, Mathf.Max(0, sc.Bsps.Count - 1));
            SetState(true, entry.Name, Level, startBsp);
            bool host = true;
            try { host = HaloNetGame.CampaignHost; } catch (Exception e) { HaloLog.Warn("Campaign: HaloNetGame.CampaignHost: " + e.Message); }
            Host = host;
            HaloObjects.SkipBipeds = !host;
            HaloLog.Info("Campaign: opened " + entry.Name + " in " + job.Ms + " ms: " + job.Map.Tags.Length + " tags, " + sc.Bsps.Count + " BSPs (start " + startBsp + "), "
                         + Data.Objects.Count + " placements (" + Data.Names.Count + " names), " + Data.DeviceGroups.Count + " device groups, " + Data.TriggerVolumes.Count + " trigger volumes ("
                         + Data.BspSwitches.Count + " BSP switches), " + Data.Profiles.Count + " starting profiles, " + sc.StartLocations.Count + " starting locations"
                         + (Data.Problems.Count > 0 ? "; problems: " + string.Join("; ", Data.Problems.ToArray()) : ""));

            _mapPath = job.Map.Path;
            _root = new GameObject("Halo Mission " + entry.Name);
            _root.transform.SetPositionAndRotation(HaloSpace.MapOrigin, Quaternion.identity);
            HaloAllegiance.Reset();
            var rules = new HaloRules { MapName = entry.Name, Teams = false, Grenades = true, Shields = true, MotionTracker = true, FriendlyFire = false };
            HaloSession.Open(entry, job.Map, sc, _root.transform, rules);
            RegisterHandover();
            HaloGame.Guard("audio preload", () => HaloAudio.Preload(job.Map));
            HaloGame.Guard("fx preload", () => HaloFx.Preload(job.Map));
            Progress(0.15f, "Building " + entry.Label);
            yield return null;

            // the world of the start BSP (World module; the other BSPs keep building in the background). K places the objects itself.
            IEnumerator build = null;
            HaloWorld.PlaceObjects = false;
            try { build = HaloWorld.Build(job.Map, sc, _root.transform, (f, l) => Progress(0.15f + 0.6f * Mathf.Clamp01(f), l), startBsp); }
            catch (Exception e) { HaloLog.Error("Campaign: starting the world build failed: " + e); }
            while (build != null)
            {
                if (gen != _gen) yield break;
                bool more;
                try { more = build.MoveNext(); }
                catch (Exception e) { HaloLog.Error("Campaign: the world build failed: " + e); break; }
                if (!more) break;
                yield return build.Current;
            }
            if (gen != _gen) yield break;
            RemoveWorldObjects();
            if (HaloWorld.ActiveBsp != startBsp) HaloLog.Warn("Campaign: the World module shows BSP " + HaloWorld.ActiveBsp + ", not the start BSP " + startBsp);
            HaloLog.Info("Campaign: world " + (HaloWorld.Ready ? "ready" : "NOT ready") + " (BSP " + BspIndex + "), bounds " + HaloWorld.Bounds.min.ToString("0") + " .. " + HaloWorld.Bounds.max.ToString("0")
                         + " (" + (Time.realtimeSinceStartup - _loadStarted).ToString("0.0") + " s)");
            Physics.SyncTransforms();
            HaloSession.NotifyWorldBuilt();

            // the scenario's objects of the start BSP, by name (scenery, devices, items, vehicles, bipeds)
            Progress(0.78f, "Placing objects");
            HaloDevices.Begin(Data);
            HaloTriggers.Begin(Data);
            IEnumerator place = HaloObjects.PlaceBsp(Data, BspIndex, _root.transform, (f, l) => Progress(0.78f + 0.16f * f, l));
            while (true)
            {
                if (gen != _gen) yield break;
                bool more;
                try { more = place.MoveNext(); }
                catch (Exception e) { HaloLog.Error("Campaign: placing objects failed: " + e); break; }
                if (!more) break;
                yield return place.Current;
            }
            Physics.SyncTransforms();
            if (gen != _gen) yield break;

            // the player at the campaign start, with the starting profile
            Progress(0.95f, "Spawning");
            float until = Time.realtimeSinceStartup + 15f;
            while (!TrySpawn())
            {
                if (gen != _gen) yield break;
                if (Time.realtimeSinceStartup > until) { HaloLog.Warn("Campaign: the player could not be moved to the start for 15 s - playing where they are"); SpawnedAt(Feet(), 0f, "in place"); break; }
                yield return null;
            }

            // scripts and AI
            if (Host)
            {
                HaloAi.Enabled = true;
                HaloAi.Difficulty = Level;
                HaloGame.Guard("script start", () => HaloScript.Start(job.Map, sc));
            }
            else
            {
                HaloAi.Enabled = false;
                // build the script VM without running it, so this follower can adopt the host's scripts at a host hand-over
                HaloGame.Guard("script prepare", () => HaloScript.Prepare(job.Map, sc));
                HaloLog.Info("Campaign: co-op client - the host runs the scripts and the AI (bipeds come as its puppets)");
            }
            HaloLog.Info("Campaign: scripts " + (HaloScript.Running ? "running" : "NOT running") + " (" + sc.Tag.Root.Count(1180) + " scripts, " + sc.Tag.Root.Count(1192) + " globals)");

            Progress(1f, "Ready");
            HaloGame.Guard("loading hide", () => HaloLoading.Hide());
            HaloGame.Guard("hud start", () => HaloHud.Start());
            HaloGame.Guard("desk start", () => HaloDesk.Start());
            Subscribe();
            HaloSession.SetPhase(HaloSession.Phase.Playing);
            _loading = null;
            // the mission's start is the first revert point (Halo restarts the mission when there is no checkpoint yet)
            HaloCheckpoints.Take("mission start");
            if (HaloGame.Current != null) HaloGame.Current.SetStatus("Playing " + entry.Label + " (" + Name(Level) + ")", false);
            HaloLog.Info("Campaign: playing " + entry.Name + " (" + entry.Label + ") on " + Name(Level) + " - loaded in " + (Time.realtimeSinceStartup - _loadStarted).ToString("0.0") + " s, "
                         + HaloObjects.Count + " objects placed (" + HaloObjects.NamedCount + " named), " + HaloDevices.Count + " devices");
        }

        static void OpenMap(OpenJob job)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                job.Map = new HaloMap(job.File);
                job.Scenario = new HaloScenario(job.Map);
                job.Data = new HaloMissionData(job.Scenario);
            }
            catch (Exception e) { job.Error = e.Message; HaloLog.Warn("Campaign: opening " + job.File + ": " + e); }
            job.Ms = sw.ElapsedMilliseconds;
            job.Done = true;
        }

        /// <summary>The World module places scenario objects itself for multiplayer; in the campaign K places them (named, per BSP, devices
        /// that move). Until World skips them while <see cref="Active"/>, its "Objects" are removed here.</summary>
        static void RemoveWorldObjects()
        {
            Transform w = HaloWorld.Root;
            if (w == null) return;
            Transform objs = w.Find("Objects");
            if (objs == null) return;
            int n = objs.childCount;
            UnityEngine.Object.Destroy(objs.gameObject);
            if (!_saidWorldObjects) { _saidWorldObjects = true; HaloLog.Info("Campaign: removed the " + n + " objects the World build placed (K places the campaign's objects by name)"); }
        }
        static bool _saidWorldObjects;

        static void Fail(string message)
        {
            HaloLog.Warn("Campaign: " + message);
            Teardown("failed", true);
            if (HaloGame.Current != null) HaloGame.Current.SetStatus(message, true);
        }

        // ------------------------------------------------------------------ the player

        static Vector3 _spawnPos;
        static float _spawnYaw, _spawnedAt, _verifyAt;

        static Vector3 Feet()
        {
            Vector3 f;
            return HaloPlayer.TryFeet(out f) ? f : HaloLocal.Position;
        }

        /// <summary>The campaign start: the scenario's first player starting location (Halo puts player 0 there).</summary>
        static bool StartPoint(out Vector3 p, out float yaw)
        {
            HaloScenario sc = HaloSession.Scenario;
            if (sc != null && sc.StartLocations.Count > 0)
            {
                var s = sc.StartLocations[0];
                p = HaloSpace.MapOrigin + HaloSpace.Pos(s.Position) + Vector3.up * 0.02f;
                yaw = 90f - s.Facing * Mathf.Rad2Deg;
                return true;
            }
            p = HaloWorld.Ready ? HaloWorld.Bounds.center : HaloSpace.MapOrigin;
            yaw = 0f;
            return false;
        }

        static bool TrySpawn()
        {
            Vector3 p; float yaw;
            bool found = StartPoint(out p, out yaw);
            string how;
            HaloCombat.NoteTeleport();
            if (!HaloPlayer.Teleport(p, yaw, out how)) return false;
            if (!found) HaloLog.Warn("Campaign: the scenario has no player starting location - spawned at the world's centre");
            SpawnedAt(p, yaw, how);
            return true;
        }

        static void SpawnedAt(Vector3 p, float yaw, string how)
        {
            _spawnPos = p; _spawnYaw = yaw; _spawnedAt = Time.time; _verifyAt = Time.time + 1.2f;
            _dead = false;
            HaloLocal.ResetLife();
            HaloLocal.Team = HaloTeams.None;
            HaloLocal.Position = p;
            HaloLocal.Forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            GiveProfile(Data != null && Data.Profiles.Count > 0 ? Data.Profiles[0] : null);
            HaloEvents.RaiseLocalSpawned(p);
            HaloLog.Info("Campaign: the player starts at " + p.ToString("0.0") + " facing " + yaw.ToString("0") + " (" + how + ")");
        }

        /// <summary>A fresh life with the scenario's player starting profile (weapons with their rounds, grenades, health / shield).</summary>
        static void GiveProfile(HaloMissionData.StartingProfile prof)
        {
            HaloGame.Guard("combat spawned", () => HaloCombat.Spawned());
            var h = HaloCombat.Handling;
            if (h == null) { HaloLog.Warn("Campaign: no combat driver - the starting profile is not given"); return; }
            h.Unequip();
            string what = "nothing";
            if (prof != null)
            {
                what = "'" + prof.Name + "':";
                if (prof.Primary != null) what += " " + GiveWeapon(prof.Primary, prof.PrimaryLoaded, prof.PrimaryReserved);
                if (prof.Secondary != null) what += " " + GiveWeapon(prof.Secondary, prof.SecondaryLoaded, prof.SecondaryReserved);
                HaloLocal.FragGrenades = Mathf.Max(0, prof.Frag);
                HaloLocal.PlasmaGrenades = Mathf.Max(0, prof.Plasma);
                if (prof.Health > 0f && prof.Health < 1f) HaloVitals.Body = HaloVitals.MaxBody * prof.Health;
                if (prof.Shield > 0f && prof.Shield < 1f) HaloVitals.ShieldPoints = HaloVitals.MaxShield * prof.Shield;
                what += ", " + HaloLocal.FragGrenades + " frag / " + HaloLocal.PlasmaGrenades + " plasma";
            }
            else
            {
                HaloLocal.FragGrenades = 0; HaloLocal.PlasmaGrenades = 0;
            }
            HaloLocal.GrenadeSelected = HaloLocal.FragGrenades > 0 || HaloLocal.PlasmaGrenades == 0 ? 0 : 1;
            HaloLocal.Held = h.Guns[0] != null ? 0 : -1;
            HaloLog.Info("Campaign: starting profile " + what);
        }

        /// <summary>A weapon on the body with its rounds (loaded / reserved &lt; 0 or both 0 = the weapon's defaults).</summary>
        internal static string GiveWeapon(HaloTag weapon, int loaded, int reserve)
        {
            var h = HaloCombat.Handling;
            if (h == null || weapon == null) return "";
            HaloGun g = null;
            try { g = h.Give(weapon); }
            catch (Exception e) { HaloLog.Warn("Campaign: giving " + weapon + " failed: " + e.Message); }
            if (g == null) return System.IO.Path.GetFileName(weapon.Name) + " (failed)";
            if (g.State != null && (loaded > 0 || reserve > 0))
            {
                g.State.Loaded = Mathf.Max(0, loaded);
                g.State.Reserve = Mathf.Max(0, reserve);
            }
            int i = h.IndexOf(g);
            if (i >= 0 && HaloLocal.Weapons[i] != null && g.State != null) { HaloLocal.Weapons[i].Loaded = g.State.Loaded; HaloLocal.Weapons[i].Reserve = g.State.Reserve; }
            return System.IO.Path.GetFileName(weapon.Name) + (g.State != null ? " " + g.State.Loaded + "/" + g.State.Reserve : "");
        }

        // ------------------------------------------------------------------ leaving

        static void Leave()
        {
            if (!Active && _root == null) return;
            if (_leaving != null) return;
            HaloGame game = HaloGame.Current;
            if (game != null && HaloSession.InMatch)
            {
                HaloGame.Guard("loading leave", () => HaloLoading.Leave());
                _leaving = game.StartCoroutine(LeaveSoon(_gen));
                return;
            }
            Teardown("left", true);
            if (game != null) game.SetStatus("Back in the hub.", false);
        }

        static IEnumerator LeaveSoon(int gen)
        {
            float until = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < until) yield return null;
            _leaving = null;
            if (gen != _gen) yield break;
            Teardown("left", true);
            if (HaloGame.Current != null) HaloGame.Current.SetStatus("Back in the hub.", false);
        }

        static void StopRoutine(ref Coroutine c)
        {
            if (c != null && HaloGame.Current != null) { try { HaloGame.Current.StopCoroutine(c); } catch (Exception) { } }
            c = null;
        }

        /// <summary>Everything of the mission goes; <paramref name="toHub"/> = the player is put back into the ring hub.</summary>
        static void Teardown(string why, bool toHub)
        {
            _gen++;
            StopRoutine(ref _leaving);
            StopRoutine(ref _loading);
            StopRoutine(ref _switching);
            bool had = Active || _root != null;
            if (!had) return;
            HaloLog.Info("Campaign: leaving " + Mission + " (" + why + ") after " + (Ticks / 30f).ToString("0") + " s of game time");
            Unsubscribe();
            UnregisterHandover();
            HaloGame.Guard("script stop", () => HaloScript.Stop());
            HaloGame.Guard("ai clear", () => HaloAi.Clear());
            HaloGame.Guard("cinematic clear", () => HaloCinematic.Clear());
            HaloSession.Close();      // Unloading: modules clean up what they own (the world root is still alive)
            HaloGame.Guard("bipeds clear", () => HaloBipeds.Clear());
            HaloGame.Guard("combat clear", () => HaloCombat.Clear());
            HaloGame.Guard("vehicles clear", () => HaloVehicles.Clear());
            HaloGame.Guard("fx clear", () => HaloFx.Clear());
            HaloGame.Guard("hud stop", () => HaloHud.Stop());
            HaloGame.Guard("desk stop", () => HaloDesk.Stop());
            HaloGame.Guard("devices clear", () => HaloDevices.Clear());
            HaloGame.Guard("triggers clear", () => HaloTriggers.Clear());
            HaloGame.Guard("objects clear", () => HaloObjects.Clear());
            HaloGame.Guard("checkpoints clear", () => HaloCheckpoints.Clear());
            HaloGame.Guard("world unload", () => HaloWorld.Unload());
            HaloWorld.PlaceObjects = true;      // multiplayer places them
            if (_root != null) { UnityEngine.Object.Destroy(_root); _root = null; }
            if (_mapPath != null)
            {
                string path = _mapPath;
                HaloGame.Guard("textures release", () => HaloTextures.Release(path));
                HaloGame.Guard("materials release", () => HaloMaterials.Release(path));
                _mapPath = null;
            }
            if (HaloLoading.Showing) HaloGame.Guard("loading hide", () => HaloLoading.Hide());
            HaloAllegiance.Reset();
            HaloLocal.Alive = false; HaloLocal.RespawnIn = 0f; HaloLocal.Vehicle = null; HaloLocal.Seat = ""; HaloLocal.Driving = false;
            Data = null;
            _entry = null;
            SetState(false, "", Level, 0);
            Host = true;
            HaloObjects.SkipBipeds = false;
            HaloAi.Enabled = true;
            ResetRun();
            if (toHub)
            {
                string how;
                bool moved = HaloPlayer.Teleport(HaloSpace.RoomOrigin + new Vector3(0f, 0.2f, 0f), 0f, out how);
                HaloLog.Info("Campaign: back to the hub (" + (moved ? how : "not moved: " + how) + ")");
            }
            HaloSession.SetPhase(HaloSession.Phase.Hub);
            try { Resources.UnloadUnusedAssets(); } catch (Exception) { }
            GC.Collect();
        }

        /// <summary>The scene is going (HaloGame.OnDestroy): tear down without moving anybody.</summary>
        public static void Stop()
        {
            try { Teardown("the world closes", false); }
            catch (Exception e) { HaloLog.Error("Campaign: stop: " + e); }
        }

        // ------------------------------------------------------------------ BSP switching

        static void DoSwitchBsp(int index)
        {
            if (!Active || HaloSession.Scenario == null) return;
            int n = HaloSession.Scenario.Bsps.Count;
            if (index < 0 || index >= n) { HaloLog.Warn("Campaign: switch_bsp " + index + " - the map has " + n + " BSPs"); return; }
            if (index == BspIndex && _switching == null) return;
            if (HaloGame.Current == null) return;
            StopRoutine(ref _switching);
            int old = BspIndex;
            BspIndex = index;
            _switching = HaloGame.Current.StartCoroutine(Switch(old, index, _gen));
        }

        static IEnumerator Switch(int old, int index, int gen)
        {
            float t0 = Time.realtimeSinceStartup;
            HaloLog.Info("Campaign: switching BSP " + old + " -> " + index + " (" + HaloSession.Scenario.Bsps[index].Tag + ")");
            // The World module keeps every BSP built (in the background after the start BSP): a switch is instant once it is built,
            // else it activates when ready.
            bool now = false;
            try { now = HaloWorld.SwitchBsp(index); }
            catch (Exception e) { HaloLog.Error("Campaign: HaloWorld.SwitchBsp " + index + " failed: " + e); }
            if (!now)
            {
                float until = Time.realtimeSinceStartup + 120f;
                while (HaloWorld.ActiveBsp != index && Time.realtimeSinceStartup < until)
                {
                    if (gen != _gen) yield break;
                    yield return null;
                }
                if (HaloWorld.ActiveBsp != index) HaloLog.Warn("Campaign: BSP " + index + " did not become active in 120 s (World is on " + HaloWorld.ActiveBsp + ")");
            }
            if (gen != _gen) yield break;
            IEnumerator place = HaloObjects.PlaceBsp(Data, index, _root.transform, null);
            while (true)
            {
                if (gen != _gen) yield break;
                bool more;
                try { more = place.MoveNext(); }
                catch (Exception e) { HaloLog.Error("Campaign: placing BSP " + index + " objects failed: " + e); break; }
                if (!more) break;
                yield return place.Current;
            }
            HaloObjects.ShowBsp(Data, index);
            Physics.SyncTransforms();
            _switching = null;
            HaloLog.Info("Campaign: BSP " + index + " active in " + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0") + " ms, world " + (HaloWorld.Ready ? "ready" : "NOT ready") + ", " + HaloObjects.Count + " objects");
            RaiseBspSwitched(old, index);
        }

        /// <summary>A BSP switch is being built.</summary>
        public static bool SwitchingBsp { get { return _switching != null; } }
    }
}

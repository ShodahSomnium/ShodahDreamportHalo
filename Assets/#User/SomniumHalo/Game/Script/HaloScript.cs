using System;
using System.Collections.Generic;
using System.Text;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Script module, S] HaloScript: runs a campaign map's own compiled scripts (scenario "script syntax data" nodes, "script string
    /// data", scripts, globals) - startup, dormant, continuous, static scripts, sleep / sleep_until / wake, and the engine functions and
    /// external globals bound by NAME (the maps' own function-name strings; their index tables differ between maps). Functions call the
    /// other modules' public APIs (HaloAi, HaloObjects, HaloCampaign, HaloCinematic, HaloAudio, HaloFx, HaloVehicles, HaloHud...); a
    /// function nobody implements yet logs "Script: unimplemented &lt;name&gt;" once and returns its type's default.
    /// The VM itself is Unity-free (Data/Script/HsVm.cs, tested in the harness: dotnet run -- run a10 60).
    /// </summary>
    public static class HaloScript
    {
        public static bool Running { get; private set; }
        /// <summary>The running VM (null when stopped): threads, globals, call counts.</summary>
        public static HsVm Vm { get; private set; }
        public static HsData Data { get; private set; }
        /// <summary>Optional trace of script events (start / sleep forever / wake / end) - the lab sets it.</summary>
        public static Action<string> Trace;
        static bool _failed;

        /// <summary>Starts the scenario's scripts (after the world, objects and players are up) - the host.</summary>
        public static void Start(HaloMap map, HaloScenario scenario)
        {
            _lastCheckpoint = null;
            if (!Build(map, scenario)) return;
            try { Begin(0, null); }
            catch (Exception e) { HaloLog.Error("Script: start failed: " + e); Running = false; Vm = null; Data = null; }
        }

        /// <summary>A co-op follower: reads the scenario's scripts and builds the VM WITHOUT running it, so a host hand-over can adopt the
        /// old host's script state at once (Mission K calls it where a follower would have called Start).</summary>
        public static void Prepare(HaloMap map, HaloScenario scenario)
        {
            _lastCheckpoint = null;
            if (Build(map, scenario)) HaloLog.Info("Script: " + map.Name + " prepared (co-op follower: the host runs the scripts; ready to take them over)");
        }

        /// <summary>Reads the scripts and builds a bound, not started VM (Vm / Data set, Running false).</summary>
        static bool Build(HaloMap map, HaloScenario scenario)
        {
            Stop();
            RegisterNetPart();
            if (map == null) return false;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var d = HsData.Read(map, scenario != null ? scenario.Tag : map.Scenario);
                if (d.Scripts.Length == 0 && d.Globals.Length == 0) { HaloLog.Info("Script: " + map.Name + " has no scripts"); return false; }
                var vm = new HsVm(d);
                vm.Log = HaloLog.Info;
                vm.Trace = Trace;
                HsCore.Bind(vm);
                HsCore.Difficulty = (int)HaloCampaign.Level;
                HsCore.Cooperative = false;
                HsCore.Print = null;
                HaloScriptWorld.Begin(d);
                HaloScriptFns.Bind(vm);
                Data = d; Vm = vm; _map = map;
                _failed = false;
                _buildMs = sw.ElapsedMilliseconds;
                return true;
            }
            catch (Exception e)
            {
                HaloLog.Error("Script: reading the scripts failed: " + e);
                Running = false; Vm = null; Data = null;
                return false;
            }
        }

        static HaloMap _map;
        static long _buildMs;

        /// <summary>Runs the built VM: <paramref name="adopt"/> null = a fresh start (tick 0) or in place at a tick; else the old host's state.</summary>
        static void Begin(int inPlaceTick, HsSnapshot adopt)
        {
            var vm = Vm; var d = Data;
            if (vm == null) return;
            if (adopt != null) { vm.Start(); vm.Restore(adopt); }
            else if (inPlaceTick > 0) vm.StartInPlace(inPlaceTick);
            else vm.Start();
            Running = true;
            HaloScriptFns.Started();
            HaloCheckpoints.Register("script", CaptureState, RestoreState);
            HaloLog.Info("Script: " + (_map != null ? _map.Name : "?") + " - " + d.Scripts.Length + " scripts (" + vm.Threads.Length + " threads), " + d.Globals.Length + " globals, "
                + d.Nodes.Length + " nodes read in " + _buildMs + " ms; " + CoverageLine());
            if (vm.UnknownFunctions.Count > 0) HaloLog.Info("Script: functions not in the table: " + string.Join(", ", vm.UnknownFunctions));
            foreach (var p in d.Problems) HaloLog.Info("Script: data problem: " + p);
        }

        // ------------------------------------------------------------------ co-op host hand-over (Net's HaloNetHostState)

        static bool _netRegistered;
        static HsSnapshot _lastCheckpoint;

        static void RegisterNetPart()
        {
            if (_netRegistered) return;
            _netRegistered = true;
            HaloNetHostState.Register("script", 20, NetCapture, NetAdopt);
        }

        /// <summary>The host, every 15 s and at checkpoints: the whole script state as bytes (a few KB).</summary>
        static byte[] NetCapture()
        {
            var vm = Vm;
            if (!Running || vm == null) return null;
            return vm.Capture().ToBytes(vm.D);
        }

        /// <summary>The new host: the old host's script state into this client's VM, ticking on from there (no startup script runs again).
        /// No / unusable bytes: the last checkpoint's script state this client holds, else the scripts start in place at the current tick.</summary>
        static void NetAdopt(byte[] bytes)
        {
            try
            {
                if (Running) { HaloLog.Info("Script: hand-over - this client already runs the scripts, kept"); return; }
                if (Vm == null && !Build(HaloSession.Map, HaloSession.Scenario)) { HaloLog.Warn("Script: hand-over - no mission scripts to adopt"); return; }
                string why = null;
                var s = bytes != null ? HsSnapshot.FromBytes(bytes, Data, out why) : null;
                if (s != null) { Begin(0, s); HaloLog.Info("Script: hand-over - adopted the old host's scripts at tick " + s.Tick + " (" + bytes.Length + " bytes; " + ThreadsLine() + ")"); return; }
                if (bytes == null) why = "no script state in the blob";
                if (_lastCheckpoint != null && _lastCheckpoint.Filled) { Begin(0, _lastCheckpoint); HaloLog.Info("Script: hand-over - " + why + ": resumed from the last checkpoint (tick " + _lastCheckpoint.Tick + ")"); return; }
                int tick = HaloCampaign.Ticks;
                Begin(tick > 0 ? tick : 1, null);
                HaloLog.Info("Script: hand-over - " + why + " and no checkpoint: the scripts start in place at tick " + tick + " (startup scripts not run again)");
            }
            catch (Exception e) { HaloLog.Error("Script: hand-over adopt failed: " + e); }
        }

        public static void Stop()
        {
            if (Vm != null)
            {
                HaloLog.Info("Script: stopped after " + Vm.Tick + " ticks; " + CallsLine(12));
                try { HaloScriptFns.Stopped(); } catch (Exception e) { HaloLog.Error("Script: stop: " + e.Message); }
            }
            Running = false; Vm = null; Data = null;
            HaloScriptWorld.End();
        }

        /// <summary>Called every game tick (Halo runs scripts at 30 ticks a second; HaloCampaign calls it).</summary>
        public static void Tick()
        {
            var vm = Vm;
            if (!Running || vm == null) return;
            try
            {
                HaloScriptFns.PreTick();
                vm.Step();
            }
            catch (Exception e)
            {
                if (!_failed) { _failed = true; HaloLog.Error("Script: tick failed (VM bug): " + e); }
            }
        }

        // ------------------------------------------------------------------ checkpoints (K's HaloCheckpoints)

        /// <summary>game_save: the globals and every thread (sleep timers, where each script is). Taken at the end of the tick.</summary>
        static object CaptureState() { var vm = Vm; if (vm == null) return null; var s = vm.Capture(); _lastCheckpoint = s; return s; }

        /// <summary>Revert: the scripts go back to where they were at the checkpoint.</summary>
        static void RestoreState(object state)
        {
            var vm = Vm; var s = state as HsSnapshot;
            if (vm == null || s == null) return;
            bool ok = vm.Restore(s);
            HaloLog.Info("Script: reverted to the checkpoint of tick " + s.Tick + (ok ? "" : " - FAILED (snapshot incomplete)"));
        }

        // ------------------------------------------------------------------ reports

        /// <summary>"N functions used: B bound (k no-op), U unimplemented" for this map.</summary>
        public static string CoverageLine()
        {
            var vm = Vm; if (vm == null) return "not running";
            int used = 0, bound = 0, noop = 0, special = 0;
            var missing = new List<int>();
            for (int i = 0; i < vm.Uses.Length; i++)
            {
                if (vm.Uses[i] == 0) continue;
                used++;
                if (HsVm.IsVmSpecial(i)) special++;
                else if (vm.Natives[i] != null) { bound++; if (HsCore.IsNoOp(HsDefs.FunctionNames[i])) noop++; }
                else missing.Add(i);
            }
            missing.Sort((a, b) => vm.Uses[b].CompareTo(vm.Uses[a]));
            var sb = new StringBuilder();
            sb.Append(used).Append(" functions used: ").Append(special).Append(" VM forms, ").Append(bound).Append(" bound (").Append(noop)
              .Append(" no-op by design), ").Append(missing.Count).Append(" unimplemented");
            if (missing.Count > 0)
            {
                sb.Append(':');
                for (int k = 0; k < missing.Count; k++) sb.Append(' ').Append(HsDefs.FunctionNames[missing[k]]).Append('(').Append(vm.Uses[missing[k]]).Append(')');
            }
            return sb.ToString();
        }

        /// <summary>The most called functions so far.</summary>
        public static string CallsLine(int top)
        {
            var vm = Vm; if (vm == null) return "";
            var list = new List<int>();
            for (int i = 0; i < vm.Calls.Length; i++) if (vm.Calls[i] > 0) list.Add(i);
            list.Sort((a, b) => vm.Calls[b].CompareTo(vm.Calls[a]));
            var sb = new StringBuilder("calls:");
            for (int k = 0; k < list.Count && k < top; k++) sb.Append(' ').Append(HsDefs.FunctionNames[list[k]]).Append('=').Append(vm.Calls[list[k]]);
            return sb.ToString();
        }

        /// <summary>Thread states: "started a / finished b / sleeping c / dormant d".</summary>
        public static string ThreadsLine()
        {
            var vm = Vm; if (vm == null) return "";
            int started = 0, dead = 0, asleep = 0, dormant = 0;
            foreach (var t in vm.Threads)
            {
                if (t.Starts > 0) started++;
                if (t.State == HsThreadState.Dead) dead++; else if (t.WakeTick == int.MaxValue) dormant++; else asleep++;
            }
            return "threads " + vm.Threads.Length + ": started " + started + ", finished " + dead + ", sleeping " + asleep + ", dormant/asleep forever " + dormant;
        }
    }
}

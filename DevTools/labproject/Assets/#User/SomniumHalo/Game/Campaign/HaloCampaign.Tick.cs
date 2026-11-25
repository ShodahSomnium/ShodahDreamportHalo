using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>The mission's frame (HaloGame.CampaignTick): Halo's fixed 30 Hz game tick (scripts, AI, devices, trigger volumes, BSP
    /// switches), checkpoints when safe, the player's death and the revert to the last checkpoint, the mission's end.</summary>
    public static partial class HaloCampaign
    {
        /// <summary>At most this many game ticks are run in one frame (a long frame does not snowball).</summary>
        public const int MaxTicksPerFrame = 4;
        /// <summary>Seconds after the player's death before the game returns to the last checkpoint (ours: Halo fades out after a few seconds).</summary>
        public static float RevertDelay = 4f;
        /// <summary>game_save gives up after this long without a safe moment (ours).</summary>
        public static float SaveTimeout = 8f;
        /// <summary>A checkpoint waits until the player took no damage for this long (ours).</summary>
        public static float SafeAfterDamage = 3f;
        /// <summary>Seconds the "mission complete" message shows before the hub (ours).</summary>
        public static float EndSeconds = 8f;

        enum SaveKind { None, Safe, NoTimeout, Now }

        static float _acc;
        static bool _subscribed, _dead, _ended, _tickFailed, _scriptFailed, _cineFailed;
        static float _diedAt, _endedAt, _saveAskedAt, _heldAt = -100f;
        static SaveKind _savePending;

        /// <summary>The player is dead and the revert is due (the HUD may show it).</summary>
        public static bool PlayerDead { get { return _dead; } }
        /// <summary>The mission has ended (game_won / game_lost) and the hub follows.</summary>
        public static bool MissionOver { get { return _ended; } }

        static void ResetRun()
        {
            _acc = 0f; _dead = false; _ended = false; _coopRespawn = false; _cineFailed = false; _speed = 1f; _countdownMs = 0; _countdownRunning = false; _savePending = SaveKind.None; _tickFailed = false; _scriptFailed = false;
        }

        static void Subscribe()
        {
            if (_subscribed) return;
            HaloEvents.LocalDied += OnLocalDied;
            _subscribed = true;
        }

        static void Unsubscribe()
        {
            if (!_subscribed) return;
            HaloEvents.LocalDied -= OnLocalDied;
            _subscribed = false;
        }

        static void OnLocalDied(HaloKill k) { Died("killed"); }

        static void Died(string how)
        {
            if (_dead || !Active || !HaloSession.Playing) return;
            _dead = true;
            _diedAt = Time.time;
            HaloLocal.Alive = false;
            _savePending = SaveKind.None;
            HaloGame.Guard("vehicles exit", () => HaloVehicles.ExitSeat());
            Vector3 other;
            _coopRespawn = CoopAlive(out other);
            _saidWaitHost = false;
            HaloLocal.RespawnIn = _coopRespawn ? CoopRespawnDelay : RevertDelay;
            if (_coopRespawn) { HaloLog.Info("Campaign: the player died (" + how + ") - another player lives: respawn next to them in " + CoopRespawnDelay.ToString("0") + " s"); return; }
            HaloLog.Info("Campaign: the player died (" + how + ") - back to the " + (HaloCheckpoints.Label.Length > 0 ? "checkpoint '" + HaloCheckpoints.Label + "'" : "mission start") + " in " + RevertDelay.ToString("0") + " s");
        }

        // ------------------------------------------------------------------ every frame

        public static void Tick()
        {
            if (!Active) return;
            HaloSession.Phase ph = HaloSession.Current;
            if (ph != HaloSession.Phase.Playing && ph != HaloSession.Phase.Ending) return;
            float dt = Time.deltaTime;
            if (HaloPlayer.BackKeyPressed()) { HaloLog.Info("Campaign: the back key - leaving the mission"); Leave(); return; }

            if (_ended)
            {
                if (_leaving == null && Time.time - _endedAt >= EndSeconds) { HaloLog.Info("Campaign: the mission is over - back to the hub"); Leave(); }
                return;
            }

            // a death the Combat module did not announce
            if (!_dead && !HaloLocal.Alive && Time.time - _spawnedAt > 1f) Died("no death event");
            bool held = false;
            try { held = HaloCinematic.HoldsPlayer; } catch (Exception) { held = false; }
            if (held) _heldAt = Time.time;
            Vector3 feet = Feet();
            if (_dead)
            {
                float wait = _coopRespawn ? CoopRespawnDelay : RevertDelay;
                HaloLocal.RespawnIn = Mathf.Max(0f, wait - (Time.time - _diedAt));
                if (Time.time - _diedAt >= wait)
                {
                    if (_coopRespawn) CoopRespawn();
                    else if (Host) DoRevert("death");
                    else if (!_saidWaitHost) { _saidWaitHost = true; HaloLog.Info("Campaign: co-op client dead with nobody alive to respawn at - waiting for the host's revert"); }
                }
            }
            else if (held) _verifyAt = 0f;      // the cutscene holds the player (cinema floor, deferred teleports): no spawn re-check
            else if (_verifyAt > 0f && Time.time >= _verifyAt)
            {
                // the client applies a teleport asynchronously: once after a spawn / revert, check the player really arrived
                _verifyAt = 0f;
                if ((feet - _spawnPos).sqrMagnitude > 25f * 25f)
                {
                    string how;
                    HaloCombat.NoteTeleport();
                    HaloPlayer.Teleport(_spawnPos, _spawnYaw, out how);
                    HaloLog.Warn("Campaign: the player was " + (feet - _spawnPos).magnitude.ToString("0") + " m from the start after the teleport - moved again (" + how + ")");
                }
            }

            // the fixed game tick (the world goes on while the player is dead, as in Halo)
            _acc += Mathf.Min(dt, 0.25f) * Speed;
            int n = 0, max = Mathf.Clamp(Mathf.CeilToInt(MaxTicksPerFrame * Speed), 1, 40);
            while (_acc >= TickSeconds && n < max)
            {
                _acc -= TickSeconds;
                n++;
                GameTick();
                if (!Active || _ended) break;
            }
            if (n == max && _acc > TickSeconds) _acc = 0f;
            TickFraction = Mathf.Clamp01(_acc / TickSeconds);

            if (_savePending != SaveKind.None) TrySave();

            // fell out of the world (below the active BSP)
            if (!_dead && !held && Time.time - _heldAt > 2f && Time.time - _spawnedAt > 2f && HaloWorld.Ready && !SwitchingBsp)
            {
                Bounds b = HaloWorld.Bounds;
                if (b.size.y > 0.5f && feet.y < b.min.y - 6f)
                {
                    HaloLog.Info("Campaign: the player fell out of the world at " + feet.ToString("0"));
                    HaloGame.Guard("combat kill", () => HaloCombat.TakeHit(new HaloHit { Amount = 1e6f, Instant = true, SourcePlayer = -1 }));
                    Died("fell out of the world");
                }
            }
        }

        /// <summary>One Halo game tick (1/30 s): scripts, AI, devices, trigger volumes.</summary>
        static void GameTick()
        {
            Ticks++;
            try { HaloTriggers.Step(); }
            catch (Exception e) { if (!_tickFailed) { _tickFailed = true; HaloLog.Error("Campaign: trigger step: " + e); } }
            if (Host)
            {
                try { HaloScript.Tick(); }
                catch (Exception e) { if (!_scriptFailed) { _scriptFailed = true; HaloLog.Error("Campaign: script tick: " + e); } }
                try { HaloAiTick(); }
                catch (Exception e) { if (!_tickFailed) { _tickFailed = true; HaloLog.Error("Campaign: AI tick: " + e); } }
            }
            try { HaloCinematic.Tick(); }
            catch (Exception e) { if (!_cineFailed) { _cineFailed = true; HaloLog.Error("Campaign: cinematic tick: " + e); } }
            try { HaloObjects.Step(); CountdownStep(); }
            catch (Exception e) { if (!_tickFailed) { _tickFailed = true; HaloLog.Error("Campaign: object animation step: " + e); } }
            try { HaloDevices.Step(TickSeconds); }
            catch (Exception e) { if (!_tickFailed) { _tickFailed = true; HaloLog.Error("Campaign: device step: " + e); } }
        }

        // ------------------------------------------------------------------ game speed, countdown timer

        static float _speed = 1f;
        static bool _saidSpeed;
        /// <summary>Script game_speed: game ticks per real tick (a30 runs its pelican recording at 5 under a black screen). 0..10.</summary>
        public static float Speed
        {
            get { return _speed; }
            set
            {
                float v = Mathf.Clamp(value, 0f, 10f);
                if (Mathf.Abs(v - _speed) > 1e-3f && Active) HaloLog.Info("Campaign: game_speed " + v.ToString("0.##") + (_saidSpeed ? "" : " (game ticks per real tick; the player's own movement is not sped up)"));
                if (Active) _saidSpeed = true;
                _speed = v;
            }
        }

        static long _countdownMs;
        static bool _countdownRunning;
        static long _countdownStartMs;

        /// <summary>Script numeric_countdown_timer_set (milliseconds, auto start) - d40's reactor countdown.</summary>
        public static void CountdownSet(long milliseconds, bool autoStart)
        {
            _countdownMs = _countdownStartMs = Math.Max(0L, milliseconds);
            _countdownRunning = autoStart;
            HaloLog.Info("Campaign: countdown timer " + (_countdownMs / 1000) + " s" + (autoStart ? ", running" : ", stopped"));
        }
        /// <summary>numeric_countdown_timer_stop.</summary>
        public static void CountdownStop() { _countdownRunning = false; }
        /// <summary>numeric_countdown_timer_restart: runs on from where it stood.</summary>
        public static void CountdownRestart() { _countdownRunning = true; }
        /// <summary>Milliseconds left on the countdown timer.</summary>
        public static long CountdownMs { get { return _countdownMs; } }
        public static bool CountdownRunning { get { return _countdownRunning; } }

        /// <summary>numeric_countdown_timer_get: one digit of the time left as MM:SS:cc - digit 0 = tens of minutes, 1 minutes, 2 tens of
        /// seconds, 3 seconds, 4 tenths, 5 hundredths (ours: the order of Halo's digit indices is not verified).</summary>
        public static int CountdownDigit(int index)
        {
            long ms = _countdownMs;
            long min = ms / 60000, sec = ms / 1000 % 60, cs = ms / 10 % 100;
            switch (index)
            {
                case 0: return (int)(min / 10 % 10);
                case 1: return (int)(min % 10);
                case 2: return (int)(sec / 10);
                case 3: return (int)(sec % 10);
                case 4: return (int)(cs / 10);
                case 5: return (int)(cs % 10);
                default: return 0;
            }
        }

        static void CountdownStep()
        {
            if (!_countdownRunning || _countdownMs <= 0) return;
            _countdownMs = Math.Max(0L, _countdownMs - (Ticks % 3 == 0 ? 34L : 33L));      // one game tick (1/30 s: 33, 33, 34 ms)
        }

        internal static void CountdownState(out long ms, out bool running) { ms = _countdownMs; running = _countdownRunning; }
        internal static void SetCountdownState(long ms, bool running) { _countdownMs = ms; _countdownRunning = running; }

        // ------------------------------------------------------------------ co-op

        /// <summary>Co-op: a dead player comes back next to a living one after this long (ours; Halo respawns a co-op player while another lives).</summary>
        public static float CoopRespawnDelay = 5f;
        static bool _coopRespawn, _saidWaitHost;

        /// <summary>Another player of this mission is alive (co-op): the nearest one's feet.</summary>
        static bool CoopAlive(out Vector3 at)
        {
            at = Vector3.zero;
            try
            {
                var list = HaloNetGame.Players;
                if (list == null) return false;
                float best = 1e18f; bool found = false;
                Vector3 me = Feet();
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (p == null || !p.InMatch || !p.Alive || p.Slot == HaloLocal.NetId) continue;
                    float d = (p.Position - me).sqrMagnitude;
                    if (d < best) { best = d; at = p.Position; found = true; }
                }
                return found;
            }
            catch (Exception) { return false; }
        }

        static void CoopRespawn()
        {
            Vector3 at;
            if (!CoopAlive(out at))
            {
                // the other players died meanwhile: the checkpoint (host) / the host's revert (client)
                _coopRespawn = false; _diedAt = Time.time - RevertDelay + 1f;
                return;
            }
            Vector3 p = at + Vector3.right * 1.2f;
            RaycastHit h;
            p = Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out h, 4f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore) ? h.point + Vector3.up * 0.02f : at;
            string how;
            HaloCombat.NoteTeleport();
            if (!HaloPlayer.Teleport(p, 0f, out how)) return;
            _coopRespawn = false;
            HaloMissionData.StartingProfile prof = null;
            if (Data != null)
            {
                foreach (var pr in Data.Profiles) if (pr.Name.IndexOf("coop", StringComparison.OrdinalIgnoreCase) >= 0) { prof = pr; break; }
                if (prof == null && Data.Profiles.Count > 0) prof = Data.Profiles[0];
            }
            _spawnPos = p; _spawnYaw = 0f; _spawnedAt = Time.time; _verifyAt = Time.time + 1.2f;
            _dead = false;
            HaloLocal.ResetLife();
            GiveProfile(prof);
            HaloLocal.Alive = true;
            HaloEvents.RaiseLocalSpawned(p);
            HaloLog.Info("Campaign: co-op respawn next to a living player at " + p.ToString("0.0") + " (" + how + ")");
        }

        /// <summary>The AI module's 30 Hz step.</summary>
        static void HaloAiTick() { HaloAi.Tick(); }

        // ------------------------------------------------------------------ checkpoints

        static void RequestSave(SaveKind kind)
        {
            if (!Active || _dead || _ended) return;
            if (kind == SaveKind.Now) { _savePending = SaveKind.None; HaloCheckpoints.Take("game_save_totally_unsafe"); return; }
            if (_savePending == SaveKind.None || kind == SaveKind.NoTimeout) _savePending = kind;
            _saveAskedAt = Time.time;
            TrySave();
        }

        /// <summary>Halo saves only when it is safe: alive, on the ground, not hurt lately, no BSP switch / cutscene in progress.</summary>
        public static bool SafeToSave
        {
            get
            {
                if (!Active || _dead || _ended || !HaloLocal.Alive || SwitchingBsp) return false;
                if (Time.time - HaloLocal.DamagedAt < SafeAfterDamage) return false;
                if (HaloCinematic.Playing) return false;
                return true;
            }
        }

        static void TrySave()
        {
            if (_savePending == SaveKind.None) return;
            if (SafeToSave)
            {
                string what = _savePending == SaveKind.NoTimeout ? "game_save_no_timeout" : "game_save";
                _savePending = SaveKind.None;
                HaloCheckpoints.Take(what);
                return;
            }
            if (_savePending == SaveKind.Safe && Time.time - _saveAskedAt > SaveTimeout)
            {
                _savePending = SaveKind.None;
                HaloLog.Info("Campaign: game_save gave up - no safe moment in " + SaveTimeout.ToString("0") + " s");
            }
        }

        static void DoRevert(string why)
        {
            if (!Active) return;
            _savePending = SaveKind.None;
            HaloCheckpoints.Restore(why);
            _dead = false;
            _acc = 0f;
            HaloLocal.RespawnIn = 0f;
            RaiseReverted();
        }

        /// <summary>After a revert put the player at <paramref name="p"/> (HaloCheckpoints).</summary>
        internal static void PlacedAfterRevert(Vector3 p, float yaw)
        {
            _spawnPos = p; _spawnYaw = yaw; _spawnedAt = Time.time; _verifyAt = Time.time + 1.2f; _dead = false;
        }

        /// <summary>Within 2.5 s of a spawn / revert teleport (the client applies it a few frames later): where the player was put.</summary>
        internal static bool RecentSpawn(out Vector3 p, out float yaw) { p = _spawnPos; yaw = _spawnYaw; return Time.time - _spawnedAt < 2.5f; }

        internal static void SetTicks(int ticks) { Ticks = Mathf.Max(0, ticks); }

        // ------------------------------------------------------------------ the end

        static void DoEnd(bool won)
        {
            if (!Active || _ended) return;
            if (!won)
            {
                // game_lost: Halo's "mission failed" goes back to the last checkpoint
                HaloLog.Info("Campaign: game_lost - back to the last checkpoint");
                HaloEvents.RaiseMessage("Mission failed", 3f);
                Died("game_lost");
                return;
            }
            _ended = true;
            _endedAt = Time.time;
            _savePending = SaveKind.None;
            HaloLog.Info("Campaign: game_won - " + Mission + " (" + Label(Mission) + ") complete on " + Name(Level) + " after " + (Ticks / 30f).ToString("0") + " s of game time");
            HaloEvents.RaiseMessage(Label(Mission) + " - mission complete", EndSeconds);
            // (the phase stays Playing: Ending belongs to the multiplayer results flow)
            HaloGame.Guard("script stop", () => HaloScript.Stop());
            RaiseEnded(true);
        }
    }
}

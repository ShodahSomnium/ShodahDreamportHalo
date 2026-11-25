using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] Halo's campaign AI: actors and actor variants, encounters / squads / platoons with their starting locations,
    /// firing positions, command lists, the actor types' behaviours (grunt, elite, jackal, hunter, marine, crew, flood forms, sentinel,
    /// monitor), perception, combat, allies following the players. Scripts address AI by "encounter", "encounter/squad" or
    /// "encounter/platoon" (ai_place, ai_kill, ai_erase, ai_living_count, ai_attach, ai_command_list, ai_magically_see_*, ai_allegiance,
    /// ai_migrate, ai_follow_target_*...). The data comes from the map (Data/Ai: HaloAiScenario, HaloActorCatalog, HaloAiDifficulty,
    /// HaloAiNav). The Mission module calls <see cref="Tick"/> 30 times a second (labs: <see cref="HaloAiDriver"/>); units are the
    /// Characters module's HaloUnit, spawned through HaloBipeds.Spawn and driven through their control members.
    /// Split: HaloAi.cs (core, placement, tick), HaloAi.Script.cs (the script functions), HaloAi.Conversations.cs.
    /// </summary>
    public static partial class HaloAi
    {
        /// <summary>Every AI system on / off (script "ai true/false"; Net's co-op clients set it false).</summary>
        public static bool Enabled = true;
        /// <summary>The mission's difficulty (K sets it before HaloScript.Start; else HaloCampaign.Level).</summary>
        public static HaloCampaign.Difficulty Difficulty = HaloCampaign.Difficulty.Normal;
        /// <summary>Script ai_grenades / ai_dialogue_triggers.</summary>
        public static bool Grenades = true, DialogueTriggers = true;
        /// <summary>Logs every state change (labs).</summary>
        public static bool Verbose;
        public static bool DebugBlind, DebugDeaf;
        /// <summary>Labs: a spawner used when HaloBipeds.Spawn returns null (variant, Unity position, yaw degrees, team).</summary>
        public static Func<HaloTag, Vector3, float, HaloTeam, HaloUnit> SpawnFallback;
        /// <summary>Labs without a campaign session: the map / scenario / BSP to read.</summary>
        public static HaloMap LabMap; public static HaloScenario LabScenario; public static int LabBsp = -1;
        /// <summary>Labs on a flat floor: no navigation (straight lines).</summary>
        public static bool NoNav;

        public const float Dt = 1f / 30f;
        public static float Time { get; private set; }
        public static int TickCount { get; private set; }
        public static readonly System.Random Rng = new System.Random(4242);

        internal static HaloMap Map; internal static HaloScenario Scenario;
        public static HaloAiScenario Data { get; private set; }
        public static HaloActorCatalog Catalog { get; private set; }
        public static HaloAiDifficulty Diff { get; private set; } = new HaloAiDifficulty();
        public static readonly List<HaloAiEncounter> Encounters = new List<HaloAiEncounter>();
        public static readonly List<HaloActor> Actors = new List<HaloActor>(64);
        public static HaloAiNav Nav { get; private set; }
        static readonly Dictionary<int, HaloAiNav> Navs = new Dictionary<int, HaloAiNav>();
        static int _navBsp = -999;
        static int _nextId;
        static bool _initialPlaced;
        static float _lastExternalTime = -100f;

        // ------------------------------------------------------------------ helpers

        public static float Rand(float a, float b) { return a + (float)Rng.NextDouble() * (b - a); }
        public static bool Chance(float p) { return p > 0f && Rng.NextDouble() < p; }
        static int D => Mathf.Clamp((int)Difficulty, 0, 3);

        public enum DiffTable { RateOfFire, ProjectileError, BurstError, NewTargetDelay, BurstSeparation, TargetTracking, TargetLeading, MeleeDelayBase, MeleeDelayScale, GrenadeChanceScale, GrenadeTimerScale, SpecialFireDelay,
            InfectionForms, OverchargeChance, GuidanceVsPlayer }

        /// <summary>A multiplier from the globals' difficulty table (matg +284, HaloAiDifficulty) for the current difficulty: rate of fire,
        /// projectile error, burst error / separation, new target delay, melee delay scale, grenade chance / timer, special fire delay,
        /// guidance vs player; InfectionForms = 1 + the table's offset (a count multiplier). Fields without a scale give 1.</summary>
        public static float DiffScale(DiffTable t)
        {
            var d = Diff; int i = D;
            switch (t)
            {
                case DiffTable.RateOfFire: return d.RateOfFire[i];
                case DiffTable.ProjectileError: return d.ProjectileError[i];
                case DiffTable.BurstError: return d.BurstError[i];
                case DiffTable.NewTargetDelay: return d.NewTargetDelay[i];
                case DiffTable.BurstSeparation: return d.BurstSeparation[i];
                case DiffTable.MeleeDelayScale: return d.MeleeDelayScale[i];
                case DiffTable.GrenadeChanceScale: return d.GrenadeChanceScale[i];
                case DiffTable.GrenadeTimerScale: return d.GrenadeTimerScale[i];
                case DiffTable.SpecialFireDelay: return d.SpecialFireDelay[i];
                case DiffTable.InfectionForms: return Mathf.Max(0f, 1f + d.InfectionForms[i]);      // the table holds an offset (easy -0.4 .. legendary 1): count x (1 + x), ours until verified
                case DiffTable.GuidanceVsPlayer: return d.GuidanceVsPlayer[i];
            }
            return 1f;
        }

        /// <summary>An additive value from the difficulty table: target tracking / leading, melee delay base, overcharge chance, the raw
        /// infection forms offset. Fields without one give 0.</summary>
        public static float DiffAdd(DiffTable t)
        {
            var d = Diff; int i = D;
            switch (t)
            {
                case DiffTable.TargetTracking: return d.TargetTracking[i];
                case DiffTable.TargetLeading: return d.TargetLeading[i];
                case DiffTable.MeleeDelayBase: return d.MeleeDelayBase[i];
                case DiffTable.InfectionForms: return d.InfectionForms[i];
                case DiffTable.OverchargeChance: return d.OverchargeChance[i];
            }
            return 0f;
        }

        static readonly Dictionary<HaloTag, float> SpeedCache = new Dictionary<HaloTag, float>();
        /// <summary>The held weapon's projectile speed (m/s) for leading, 0 unknown.</summary>
        public static float ProjectileSpeed(HaloTag weapon)
        {
            if (weapon == null) return 0f;
            float s;
            if (SpeedCache.TryGetValue(weapon, out s)) return s;
            s = 0f;
            try
            {
                var cat = HaloCombat.Catalog;
                var w = cat != null ? cat.Weapon(weapon) : null;
                var t = w != null ? w.PrimaryTrigger : null;
                var p = t != null ? cat.Projectile(t.Projectile) : null;
                if (p != null) s = p.InitialSpeed * HaloSpace.Unit;
            }
            catch (Exception) { s = 0f; }
            SpeedCache[weapon] = s;
            return s;
        }

        // ------------------------------------------------------------------ data

        /// <summary>Reads the map's AI data when the session map changed (called by every entry point).</summary>
        static bool Ensure()
        {
            HaloMap map = HaloSession.Map ?? LabMap;
            HaloScenario sc = HaloSession.Scenario ?? LabScenario;
            if (map == null || sc == null) return false;
            if (Data != null && Map == map) return true;
            Reset(false);
            try
            {
                var t0 = DateTime.UtcNow;
                Map = map; Scenario = sc;
                Data = new HaloAiScenario(sc);
                Catalog = new HaloActorCatalog(map);
                Diff = new HaloAiDifficulty(map);
                foreach (var e in Data.Encounters) Encounters.Add(new HaloAiEncounter(e));
                int squads = 0, starts = 0, fpos = 0;
                foreach (var e in Data.Encounters) { squads += e.Squads.Count; fpos += e.FiringPositions.Count; foreach (var s in e.Squads) starts += s.Starts.Count; }
                int variants = 0; foreach (var v in Data.ActorPalette) if (Catalog.Variant(v) != null) variants++;
                if (HaloCampaign.Active) Difficulty = HaloCampaign.Level;
                HaloLog.Info("AI: " + map.Name + ": " + Data.Encounters.Count + " encounters, " + squads + " squads, " + starts + " starting locations, " + fpos +
                    " firing positions, " + Data.CommandLists.Count + " command lists, " + Data.Conversations.Count + " conversations, actor palette " + variants + "/" + Data.ActorPalette.Count +
                    ", difficulty " + Difficulty + " (" + (Diff.FromTag ? "globals table" : "no table") + ") read in " + (DateTime.UtcNow - t0).TotalMilliseconds.ToString("0") + " ms");
                Hook();
                RegisterCheckpoint();
                RegisterHandover();
                if (UnityEngine.Time.unscaledTime - _lastExternalTime > 0.5f) HaloAiDriver.Ensure(HaloGame.Current != null ? HaloGame.Current.transform : null);
                return true;
            }
            catch (Exception e)
            {
                HaloLog.Error("AI: reading " + map.Name + "'s AI data failed: " + e);
                Data = null; Map = map;
                return false;
            }
        }

        static bool _hooked;
        static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            HaloEvents.Fired += OnFired;
            HaloCampaign.BspSwitched += OnBspSwitched;
            HaloBipeds.Destroyed += OnUnitDestroyed;
            HaloSession.Unloading += Clear;
        }

        /// <summary>The structure BSP changed (after HaloWorld activated it): every actor (living or a body) whose encounter belongs to
        /// another BSP is suspended and hidden (Halo does not simulate objects outside the loaded BSP); the ones of the new BSP are settled
        /// on their floor again, then the new BSP's initially created encounters are placed.</summary>
        static void OnBspSwitched(int from, int to)
        {
            _navBsp = -999;
            int suspended = 0, resumed = 0;
            for (int i = 0; i < Actors.Count; i++)
            {
                var a = Actors[i];
                var u = a.Unit;
                if (u == null) continue;
                bool dormant;
                if (a.Enc != null && a.Enc.Data.Bsp >= 0) dormant = a.Enc.Data.Bsp != to;
                else dormant = u.Seat == null && HaloWorld.Outside(u.transform.position + Vector3.up * 0.1f);     // free actors: by their feet
                if (dormant) { if (Suspend(u, SuspendBsp)) suspended++; }
                else if (Resume(u, SuspendBsp)) resumed++;
            }
            HaloLog.Info("AI: " + suspended + " actors suspended for bsp " + to + " (" + resumed + " resumed on their floor)");
            PlaceInitialForBsp(to);
        }

        // ------------------------------------------------------------------ suspension (other BSP / dormant encounter)

        const byte SuspendBsp = 1, SuspendDormant = 2;
        /// <summary>Units the AI suspended, with why (bits). Hidden while suspended for another BSP.</summary>
        static readonly Dictionary<HaloUnit, byte> SuspendedBy = new Dictionary<HaloUnit, byte>();
        static readonly Dictionary<HaloUnit, List<Renderer>> HiddenRenderers = new Dictionary<HaloUnit, List<Renderer>>();
        static readonly List<Renderer> RendererTmp = new List<Renderer>(32);

        /// <summary>Labs / other modules: is the unit suspended because its encounter's BSP is not loaded (hidden too)?</summary>
        public static bool SuspendedForBsp(HaloUnit u) { return IsSuspended(u, SuspendBsp); }
        /// <summary>Labs / other modules: is the unit suspended because its encounter is dormant (no player near)?</summary>
        public static bool SuspendedDormant(HaloUnit u) { return IsSuspended(u, SuspendDormant); }

        /// <summary>Is a unit suspended by the AI for this reason?</summary>
        static bool IsSuspended(HaloUnit u, byte why) { byte b; return u != null && SuspendedBy.TryGetValue(u, out b) && (b & why) != 0; }

        /// <summary>Suspends a unit (no movement, no gravity) for a reason; another BSP also hides it. True when newly suspended for it.</summary>
        static bool Suspend(HaloUnit u, byte why)
        {
            if (u == null) return false;
            byte b; SuspendedBy.TryGetValue(u, out b);
            if ((b & why) != 0) return false;
            SuspendedBy[u] = (byte)(b | why);
            u.MoveVelocity = Vector3.zero; u.Firing = false;
            if (!u.Suspended) u.Suspended = true;
            if (why == SuspendBsp && !HiddenRenderers.ContainsKey(u))
            {
                var hidden = new List<Renderer>(8);
                u.GetComponentsInChildren(false, RendererTmp);
                for (int i = 0; i < RendererTmp.Count; i++) if (RendererTmp[i] != null && RendererTmp[i].enabled) { RendererTmp[i].enabled = false; hidden.Add(RendererTmp[i]); }
                RendererTmp.Clear();
                HiddenRenderers[u] = hidden;
            }
            return true;
        }

        /// <summary>Clears one reason; when none is left the unit is settled on its floor (HaloUnit.Suspended = false runs Settle).</summary>
        static bool Resume(HaloUnit u, byte why)
        {
            byte b;
            if (u == null || !SuspendedBy.TryGetValue(u, out b) || (b & why) == 0) return false;
            b = (byte)(b & ~why);
            List<Renderer> hidden;
            if (why == SuspendBsp && HiddenRenderers.TryGetValue(u, out hidden))
            {
                for (int i = 0; i < hidden.Count; i++) if (hidden[i] != null) hidden[i].enabled = true;
                HiddenRenderers.Remove(u);
            }
            if (b != 0) { SuspendedBy[u] = b; return true; }
            SuspendedBy.Remove(u);
            if (u.Suspended && !u.Latched) u.Suspended = false;     // (a latched infection form stays held: HaloUnit.Unlatch frees it)
            return true;
        }

        static readonly List<HaloUnit> PurgeTmp = new List<HaloUnit>(8);
        /// <summary>Drops destroyed units from the suspension tables (a few times a minute).</summary>
        static void PurgeSuspended()
        {
            PurgeTmp.Clear();
            foreach (var kv in SuspendedBy) if (kv.Key == null) PurgeTmp.Add(kv.Key);
            for (int i = 0; i < PurgeTmp.Count; i++) { SuspendedBy.Remove(PurgeTmp[i]); HiddenRenderers.Remove(PurgeTmp[i]); }
            PurgeTmp.Clear();
        }

        static void ForgetSuspended()
        {
            foreach (var kv in HiddenRenderers) for (int i = 0; i < kv.Value.Count; i++) if (kv.Value[i] != null) kv.Value[i].enabled = true;
            HiddenRenderers.Clear();
            SuspendedBy.Clear();
        }

        /// <summary>An actor whose encounter belongs to a structure BSP that is not loaded (campaign only).</summary>
        static bool InOtherBsp(HaloActor a)
        {
            return HaloCampaign.Active && a.Enc != null && a.Enc.Data.Bsp >= 0 && a.Enc.Data.Bsp != _navBsp && (a.Unit == null || a.Unit.Seat == null);
        }

        /// <summary>The navigation of the active BSP (built on first use, cached per BSP).</summary>
        static void UpdateNav()
        {
            int bsp = LabBsp >= 0 ? LabBsp : (HaloCampaign.Active ? HaloCampaign.BspIndex : 0);
            if (NoNav) { Nav = null; _navBsp = bsp; return; }
            if (bsp == _navBsp && (Nav != null || Scenario == null)) return;
            _navBsp = bsp;
            HaloAiNav nav;
            if (!Navs.TryGetValue(bsp, out nav))
            {
                var t0 = DateTime.UtcNow;
                try { nav = HaloAiNav.ForBsp(Scenario, bsp); }
                catch (Exception e) { HaloLog.Warn("AI: navigation of bsp " + bsp + " failed: " + e.Message); nav = null; }
                Navs[bsp] = nav;
                if (nav != null)
                    HaloLog.Info("AI: navigation bsp " + bsp + ": " + nav.PolyCount + " walkable surfaces (" + (nav.FromPathfindingData ? "sbsp pathfinding bytes" : "collision normals") + "), " +
                        nav.LinkCount + " shared edges, " + nav.StepLinks + " step links (" + nav.SlopeLinks + " across slopes, normal z >= " + HaloAiNav.SlopeNz + "), " + nav.Components + " islands, built in " + (DateTime.UtcNow - t0).TotalMilliseconds.ToString("0") + " ms");
            }
            Nav = nav;
        }

        public static int ActiveBsp => _navBsp;

        // ------------------------------------------------------------------ the tick

        /// <summary>One 30 Hz AI step (the Mission module calls it after HaloScript.Tick).</summary>
        public static void Tick()
        {
            _lastExternalTime = UnityEngine.Time.unscaledTime;
            Step();
        }

        /// <summary>Labs / fallback: called every frame by <see cref="HaloAiDriver"/>; steps at 30 Hz unless the Mission module ticks us.</summary>
        static float _acc;
        public static void Frame(float deltaTime)
        {
            if (UnityEngine.Time.unscaledTime - _lastExternalTime < 0.5f) { _acc = 0f; return; }   // K ticks us
            _acc += Mathf.Min(deltaTime, 0.25f);
            int n = 0;
            while (_acc >= Dt && n < 4) { _acc -= Dt; n++; Step(); }
            if (n == 4) _acc = 0f;
        }

        static readonly System.Diagnostics.Stopwatch Watch = new System.Diagnostics.Stopwatch();
        static double _tickMsMax, _tickMsSum; static int _tickMsN;

        static void Step()
        {
            if (!Enabled)
            {
                if (Data == null && (HaloSession.Map != null || LabMap != null)) Prepare();     // followers: ready to adopt a hand-over
                for (int i = 0; i < Actors.Count; i++) { var u = Actors[i].Unit; if (u != null) { u.MoveVelocity = Vector3.zero; u.Firing = false; } }
                return;
            }
            if (!Ensure()) return;
            Watch.Reset(); Watch.Start();
            Time += Dt;
            TickCount++;
            try
            {
                UpdateNav();
                if (!_initialPlaced && (HaloCampaign.Active || LabMap != null && LabPlaceInitial))
                {
                    _initialPlaced = true;
                    PlaceInitialForBsp(_navBsp);
                }
                HaloAiBodies.Refresh(Actors, Dt);
                if (TickCount % 3 == 0) { ScanGrenades(); NoticeProjectiles(); }
                if (TickCount % 30 == 0) UpdateActivity();
                ProcessNoises();
                bool switching = HaloCampaign.Active && HaloCampaign.SwitchingBsp;
                for (int i = 0; i < Actors.Count; i++)
                {
                    var a = Actors[i];
                    var u = a.Unit;
                    if (switching)
                    {
                        // the BSP is being swapped (BspIndex is already the new one): nobody thinks or moves until BspSwitched
                        if (u != null) { u.MoveVelocity = Vector3.zero; u.Firing = false; }
                        continue;
                    }
                    if (u != null && IsSuspended(u, SuspendBsp))
                    {
                        // encounter actors come back when their BSP is loaded; free actors only on the next BspSwitched
                        if (a.Enc != null && a.Enc.Data.Bsp >= 0 && !InOtherBsp(a)) Resume(u, SuspendBsp);
                        else continue;
                    }
                    else if (u != null && InOtherBsp(a))
                    {
                        Suspend(u, SuspendBsp);
                        continue;   // dormant: its BSP is not loaded (suspended and hidden, settled again on BspSwitched)
                    }
                    if (u != null && a.Enc != null && !a.Enc.Active && a.Enc.AllowDormant && a.Alive)
                    {
                        Suspend(u, SuspendDormant);
                        continue;   // dormant encounter: no player near it (ai_allow_dormant false / ai_force_active keep it running)
                    }
                    if (u != null && IsSuspended(u, SuspendDormant)) Resume(u, SuspendDormant);
                    if (u != null && a.State != HaloActor.Mode.Dead && HaloCinematic.IsRecording(u.gameObject)) continue;   // a recording drives it (Q)
                    try { a.Tick(Dt); }
                    catch (Exception e) { if (!_actorFailed) { _actorFailed = true; HaloLog.Error("AI: actor " + a.Label + " tick failed: " + e); } }
                }
                // actors whose unit is gone (erased by others, bodies cleaned up) leave the tick list (encounters keep them for their counts)
                for (int i = Actors.Count - 1; i >= 0; i--) if (Actors[i].State == HaloActor.Mode.Dead && Actors[i].Unit == null) Actors.RemoveAt(i);
                if (TickCount % 300 == 0 && SuspendedBy.Count > 0) PurgeSuspended();
                if (PendingSeats.Count > 0) RetrySeats();
                ApplyVehicleWishes();
                if (TickCount % 10 == 0) for (int i = 0; i < Encounters.Count; i++) if (Encounters[i].Actors.Count > 0) Encounters[i].Think(Time);
                TickConversations();
            }
            catch (Exception e) { if (!_tickFailed) { _tickFailed = true; HaloLog.Error("AI: tick failed: " + e); } }
            Watch.Stop();
            double ms = Watch.Elapsed.TotalMilliseconds;
            _tickMsSum += ms; _tickMsN++; if (ms > _tickMsMax) _tickMsMax = ms;
            if (TickCount % 1800 == 0 && Actors.Count > 0) LogBudget();
        }

        static bool _tickFailed, _actorFailed;

        public static void LogBudget()
        {
            int living = 0; foreach (var a in Actors) if (a.Alive) living++;
            HaloLog.Info("AI: budget: " + living + " living actors, tick avg " + (_tickMsN > 0 ? _tickMsSum / _tickMsN : 0).ToString("0.00") + " ms max " + _tickMsMax.ToString("0.00") +
                " ms, sight rays " + HaloAiBodies.RaysTotal + ", paths " + _paths + " (max " + _pathMaxExpand + " surfaces), shots " + _shots + ", grenades " + _grenadesThrown + ", melee " + _melees +
                ", evades " + _evades + ", panics " + _panics + ", deaths " + _deaths + ", stuck " + _stuck + ", unreachable goals " + _unreachable + " (detoured to the nearest reachable spot " + _detours + "); " + NoShotLine);
            _tickMsMax = 0; _tickMsSum = 0; _tickMsN = 0;
        }

        public static string BudgetLine => "tick avg " + (_tickMsN > 0 ? _tickMsSum / _tickMsN : 0).ToString("0.00") + " ms max " + _tickMsMax.ToString("0.00") + " ms";

        // ------------------------------------------------------------------ live grenades (actors dive away: actr "dive from grenade chance")

        /// <summary>Live grenades this tick (positions), refreshed every third tick.</summary>
        public static readonly List<Vector3> LiveGrenades = new List<Vector3>(8);
        /// <summary>Per <see cref="LiveGrenades"/> entry: the grenade projectile's "danger radius" (proj +424) in metres (3.5 m when the tag has none, ours).</summary>
        public static readonly List<float> LiveGrenadeRadius = new List<float>(8);
        static readonly Predicate<HaloProjectiles.Flight> GrenadeCollector = f =>
        {
            if (f.Alive && f.GrenadeKind >= 0 && f.Authoritative && LiveGrenades.Count < 16)
            {
                LiveGrenades.Add(f.Position);
                float r = f.Def != null && f.Def.DangerRadius > 0f ? f.Def.DangerRadius * HaloSpace.Unit : 3.5f;
                LiveGrenadeRadius.Add(r);
            }
            return false;
        };

        /// <summary>Something to keep away from: a live grenade (its danger radius) or a living unit whose unit tag has an "ai danger radius"
        /// (unit +644: the flood carrier, 2 wu). Team None = dangerous to everybody (grenades).</summary>
        public struct DangerSource { public Vector3 At; public float Radius; public HaloTeam Team; public bool Grenade; public HaloUnit Unit; }
        /// <summary>Refreshed every third tick with <see cref="LiveGrenades"/>.</summary>
        public static readonly List<DangerSource> Dangers = new List<DangerSource>(16);

        static void ScanGrenades()
        {
            LiveGrenades.Clear(); LiveGrenadeRadius.Clear(); Dangers.Clear();
            if (HaloProjectiles.Count > 0)
            {
                try { HaloProjectiles.Find(GrenadeCollector); } catch (Exception) { }
                for (int i = 0; i < LiveGrenades.Count; i++) Dangers.Add(new DangerSource { At = LiveGrenades[i], Radius = LiveGrenadeRadius[i], Team = HaloTeam.None, Grenade = true });
            }
            for (int i = 0; i < Actors.Count && Dangers.Count < 32; i++)
            {
                var u = Actors[i].Unit;
                if (u == null || !u.Alive || u.Def == null || u.Def.AiDangerRadius <= 0f || u.Suspended) continue;
                Dangers.Add(new DangerSource { At = u.transform.position, Radius = u.Def.AiDangerRadius * HaloSpace.Unit, Team = u.Team, Grenade = false, Unit = u });
            }
        }

        /// <summary>The nearest danger an actor should keep out of (not its own unit; units' dangers only for other teams), false none.</summary>
        public static bool DangerNear(HaloActor a, out DangerSource danger)
        {
            danger = default(DangerSource);
            if (a == null || a.Unit == null) return false;
            Vector3 p = a.Position; float best = float.MaxValue;
            for (int i = 0; i < Dangers.Count; i++)
            {
                var d = Dangers[i];
                if (d.Unit == a.Unit || (!d.Grenade && d.Team == a.Team)) continue;
                float s = (d.At - p).sqrMagnitude;
                if (s > d.Radius * d.Radius || s >= best) continue;
                best = s; danger = d;
            }
            return best < float.MaxValue;
        }

        // ------------------------------------------------------------------ hearing

        struct Noise { public Vector3 At, Dir; public HaloAiBody Shooter; public bool Player; }
        static readonly Noise[] Noises = new Noise[16];
        static int _noiseCount;

        static void OnFired(HaloTag weapon, Vector3 at, Vector3 dir, int who)
        {
            if (Actors.Count == 0 || _noiseCount >= Noises.Length) return;
            // one per shooter per tick is plenty
            for (int i = 0; i < _noiseCount; i++) if (Noises[i].Player && (Noises[i].At - at).sqrMagnitude < 1f) return;
            Noises[_noiseCount++] = new Noise { At = at, Dir = dir, Shooter = null, Player = true };
        }

        static void AiNoise(HaloActor a)
        {
            if (_noiseCount >= Noises.Length || a.Body == null) return;
            for (int i = 0; i < _noiseCount; i++) if (Noises[i].Shooter == a.Body) return;
            Noises[_noiseCount++] = new Noise { At = a.Unit.Eye, Dir = a.Unit.AimPoint - a.Unit.Eye, Shooter = a.Body, Player = false };
        }

        static void ProcessNoises()
        {
            for (int n = 0; n < _noiseCount; n++)
            {
                var z = Noises[n];
                HaloAiBody shooter = z.Shooter;
                if (z.Player) shooter = NearestPlayerTo(z.At);
                for (int i = 0; i < Actors.Count; i++)
                {
                    var a = Actors[i];
                    if (!a.Alive || a.Body == shooter) continue;
                    a.Hear(z.At, shooter);
                    if (z.Player) a.ShotAt(z.At, z.Dir);
                }
            }
            _noiseCount = 0;
        }

        static HaloAiBody NearestPlayerTo(Vector3 p)
        {
            var all = HaloAiBodies.All; HaloAiBody best = null; float bd = 9f;
            for (int i = 0; i < all.Count; i++) { var b = all[i]; if (!b.IsPlayer) continue; float d = (b.Head - p).sqrMagnitude; if (d < bd) { bd = d; best = b; } }
            return best ?? HaloAiBodies.LocalPlayer;
        }

        // ------------------------------------------------------------------ placement

        /// <summary>Labs: place the initially created encounters on the first tick like a mission start.</summary>
        public static bool LabPlaceInitial;

        /// <summary>Mission start / BSP switch: every encounter without "not initially created" of that BSP is placed (Halo does it).</summary>
        static void PlaceInitialForBsp(int bsp)
        {
            if (Data == null) return;
            int placed = 0, encs = 0;
            foreach (var e in Encounters)
            {
                if (e.Created || e.Data.Has(HaloAiScenario.EncNotInitiallyCreated)) continue;
                int eb = e.Data.Bsp;
                if (eb >= 0 && bsp >= 0 && eb != bsp) continue;
                int before = Actors.Count;
                PlaceEncounter(e, -1, -1, -1);
                placed += Actors.Count - before; encs++;
            }
            if (encs > 0) HaloLog.Info("AI: bsp " + bsp + ": placed " + encs + " initially created encounters (" + placed + " actors)");
        }

        /// <summary>Places the actors of an encounter or "encounter/squad" / "encounter/platoon" (script ai_place).</summary>
        public static void Place(string encounterOrSquad) { Place(encounterOrSquad, -1); }

        /// <summary>As <see cref="Place(string)"/> with a count per squad (-1 = the squads' difficulty counts).</summary>
        public static void Place(string encounterOrSquad, int count)
        {
            if (!Ensure()) { Unimplemented("ai_place without a map"); return; }
            HaloAiEncounter enc; int squad, platoon;
            if (!Resolve(encounterOrSquad, out enc, out squad, out platoon)) return;
            int before = Actors.Count;
            PlaceEncounter(enc, squad, platoon, count);
            if (FirstPlace(encounterOrSquad)) HaloLog.Info("AI: ai_place " + encounterOrSquad + ": " + (Actors.Count - before) + " actors (" + Difficulty + ")" + PlacedSummary(before));
        }

        static readonly HashSet<string> Placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static bool FirstPlace(string s) { return Placed.Add(s ?? ""); }

        static string PlacedSummary(int from)
        {
            if (Actors.Count - from <= 0) return "";
            var counts = new Dictionary<string, int>();
            for (int i = from; i < Actors.Count; i++) { string n = Short(Actors[i].V != null ? Actors[i].V.Tag : null); int c; counts.TryGetValue(n, out c); counts[n] = c + 1; }
            var sb = new System.Text.StringBuilder(": ");
            foreach (var kv in counts) sb.Append(kv.Value).Append(" x ").Append(kv.Key).Append(", ");
            sb.Length -= 2;
            return sb.ToString();
        }

        static void PlaceEncounter(HaloAiEncounter enc, int squad, int platoon, int count)
        {
            enc.Created = true;
            for (int s = 0; s < enc.Data.Squads.Count; s++)
            {
                if (squad >= 0 && s != squad) continue;
                if (platoon >= 0 && enc.Data.Squads[s].Platoon != platoon) continue;
                PlaceSquad(enc, s, count, true);
            }
        }

        /// <summary>
        /// Places one squad: its difficulty count (normal diff count on easy / normal, insane diff count on heroic / legendary) at its
        /// starting locations ("required" ones first, then at random; reused when there are more actors than locations), each actor the
        /// location's actor type or the squad's, upgraded to the variant's major variant by the globals' major upgrade fraction for the
        /// squad's "major upgrade" setting.
        /// </summary>
        internal static void PlaceSquad(HaloAiEncounter enc, int s, int count, bool timers)
        {
            var sq = enc.Data.Squads[s];
            int n = count >= 0 ? count : HaloAiScenario.CountFor(sq, D);
            if (n <= 0 || sq.Starts.Count == 0) return;
            // order the starting locations: required first, then shuffled
            var order = TmpOrder; order.Clear();
            for (int i = 0; i < sq.Starts.Count; i++) if (sq.Starts[i].Required) order.Add(i);
            int req = order.Count;
            for (int i = 0; i < sq.Starts.Count; i++) if (!sq.Starts[i].Required) order.Add(i);
            for (int i = order.Count - 1; i > req; i--) { int j = req + Rng.Next(i - req + 1); int t = order[i]; order[i] = order[j]; order[j] = t; }
            float major = Diff.MajorFraction(sq.MajorUpgrade, D);
            // an encounter of a BSP that is not loaded: its actors are created suspended at their starting locations (no floor to stand
            // on yet) and settled when the BSP is loaded (Halo creates them; they are not simulated outside the loaded BSP)
            bool otherBsp = HaloCampaign.Active && enc.Data.Bsp >= 0 && _navBsp >= 0 && enc.Data.Bsp != _navBsp;
            if (otherBsp && FirstPlace("#otherbsp " + enc.Name)) HaloLog.Info("AI: " + enc.Name + " (bsp " + enc.Data.Bsp + ") placed while bsp " + _navBsp + " is loaded: suspended until its bsp is");
            for (int k = 0; k < n; k++)
            {
                var st = sq.Starts[order[k % order.Count]];
                int type = st.ActorType >= 0 ? st.ActorType : sq.ActorType;
                HaloTag vt = Data.PaletteVariant(type);
                var v = Catalog.Variant(vt);
                if (v == null) { Unimplemented("squad without an actor variant (" + enc.Name + "/" + sq.Name + ")"); continue; }
                if (v.MajorVariant != null && Chance(major)) { var mv = Catalog.Variant(v.MajorVariant); if (mv != null) v = mv; }
                Vector3 pos = HaloSpace.Pos(st.Position);
                if (k >= order.Count && !otherBsp) pos = JitterOnNav(pos);     // more actors than locations
                float yaw = 90f - st.Facing * Mathf.Rad2Deg;
                var a = Spawn(v, pos, yaw, enc.Team);
                if (a == null) continue;
                if (otherBsp)
                {
                    a.Unit.transform.position = pos;     // undo a spawn snap onto another BSP's geometry
                    a.Home = pos;
                    Suspend(a.Unit, SuspendBsp);
                }
                a.Start = st;
                a.InitialState = st.InitialState != HaloAiState.None ? st.InitialState : sq.InitialState;
                a.ReturnState = st.ReturnState != HaloAiState.None ? st.ReturnState : sq.ReturnState;
                a.MoveToSquad(enc, s);
                enc.Squads[s].Placed++; enc.TotalPlaced++;
                a.EnterAiState(a.InitialState);
                if (st.CommandList >= 0 && st.CommandList < Data.CommandLists.Count) a.RunCommandList(Data.CommandLists[st.CommandList]);
            }
            if (timers && sq.Has(HaloAiScenario.SqStartTimerImmediately)) enc.StartTimer(s, Time);
        }

        static readonly List<int> TmpOrder = new List<int>(32);

        /// <summary>A spot up to 0.6 m (ours) beside a starting location for a squad with more actors than locations: only a point that
        /// lies on the walkable navigation within a step of the start's height (never into a wall or off a ledge); else the start itself.</summary>
        static Vector3 JitterOnNav(Vector3 start)
        {
            var nav = Nav;
            for (int t = 0; t < 4; t++)
            {
                Vector3 p = start + new Vector3(Rand(-0.6f, 0.6f), 0f, Rand(-0.6f, 0.6f));
                if (nav == null) return p;
                Vector3 h = HaloSpace.ToHalo(p);
                int poly = nav.Locate(h.x, h.y, h.z + 0.1f, 0.25f, 0.4f, 0f);
                if (poly < 0) continue;
                float z = nav.HeightAt(poly, h.x, h.y);
                if (Mathf.Abs(z - h.z) > HaloAiNav.MaxSlopeRise) continue;
                return p;
            }
            return start;
        }

        /// <summary>Spawns an actor of a variant tag (Unity position, yaw degrees) and joins it to an encounter's squad (enc null or no such
        /// squad = a free actor); the squad's timer flags apply (<see cref="HaloAiEncounter.Joined"/>). Null when it cannot.</summary>
        public static HaloActor Spawn(HaloTag actorVariant, Vector3 pos, float yaw, HaloAiEncounter enc, int squad)
        {
            if (!Ensure()) return null;
            var v = Catalog != null && actorVariant != null ? Catalog.Variant(actorVariant) : null;
            if (v == null) return null;
            var a = Spawn(v, pos, yaw, enc != null ? enc.Team : HaloTeam.None);
            if (a == null) return null;
            if (enc != null && squad >= 0 && squad < enc.Squads.Length) { a.MoveToSquad(enc, squad); enc.Created = true; enc.TotalPlaced++; enc.Joined(a); }
            else a.Free = true;
            a.EnterAiState(HaloAiState.Alert);
            return a;
        }

        // ------------------------------------------------------------------ spawned actors (flood carrier forms bursting)

        static int _released;
        /// <summary>Infection forms released by bursting carriers so far (labs).</summary>
        public static int Released => _released;

        /// <summary>
        /// A body was destroyed (HaloBipeds.Destroyed, raised before Destroy): when its unit tag has a "spawned actor" (unit +588: the flood
        /// carrier's characters\flood_infection\flood_infection, count +604..+606 = 3..6, spawned velocity +608 = 3) the AI creates that many
        /// actors at the body, flung outward at the spawned velocity, on the body's team, in the dead actor's encounter / squad (so
        /// ai_living_count counts them), fighting the carrier's target. The difficulty's "infection forms" value (globals: easy -0.4 .. legendary 1)
        /// scales the count as round(n * (1 + x)) - ours until verified against the retail game.
        /// </summary>
        static void OnUnitDestroyed(HaloUnit u)
        {
            if (!Enabled || u == null || u.Def == null || u.Def.SpawnedActor == null) return;
            if (!Ensure()) return;
            try
            {
                var v = Catalog.Variant(u.Def.SpawnedActor);
                if (v == null) { Unimplemented("spawned actor " + Short(u.Def.SpawnedActor) + " (not an actor variant)"); return; }
                int lo = Mathf.Max(0, u.Def.SpawnedCountMin), hi = Mathf.Max(lo, u.Def.SpawnedCountMax);
                int n = lo + Rng.Next(hi - lo + 1);
                n = Mathf.Max(0, Mathf.RoundToInt(n * DiffScale(DiffTable.InfectionForms)));        // ours until verified
                if (n == 0) return;
                var dead = DeadActorOf(u);
                var enc = dead != null ? dead.Enc : null;
                int squad = dead != null ? dead.Squad : -1;
                HaloAiBody target = dead != null ? dead.Target : null;
                Vector3 c = u.Center;
                // the tag's spawned velocity is world units per second (3 wu/s = 9.1 m/s); the launch elevation is ours
                float speed = Mathf.Max(0f, u.Def.SpawnedVelocity) * HaloSpace.Unit;
                float a0 = Rand(0f, 360f);
                int made = 0;
                for (int k = 0; k < n; k++)
                {
                    float ang = (a0 + k * 360f / n + Rand(-20f, 20f)) * Mathf.Deg2Rad;
                    Vector3 outward = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    Vector3 p = c + outward * Rand(0.1f, 0.3f);                          // ours: 0.3 m jitter
                    var a = Spawn(v, p, Rand(0f, 360f), u.Team);
                    if (a == null) continue;
                    a.Unit.Team = u.Team;
                    if (enc != null && squad >= 0 && squad < enc.Squads.Length) { a.MoveToSquad(enc, squad); enc.TotalPlaced++; enc.Joined(a); }
                    else a.Free = true;
                    a.EnterAiState(HaloAiState.Alert);
                    if (target != null && target.Alive) a.MagicallySee(target);
                    float elev = Rand(30f, 60f) * Mathf.Deg2Rad;                          // ours
                    if (speed > 0f) a.Unit.AddImpulse((outward * Mathf.Cos(elev) + Vector3.up * Mathf.Sin(elev)) * speed);
                    made++;
                }
                _released += made;
                if (_released == made || Verbose)
                    HaloLog.Info("AI: " + Short(u.UnitTag) + (dead != null ? " (" + dead.Label + ")" : "") + " released " + made + " " + Short(v.Tag) +
                        " (" + lo + ".." + hi + ", difficulty x" + DiffScale(DiffTable.InfectionForms).ToString("0.##") + ", " + speed.ToString("0.0") + " m/s)");
            }
            catch (Exception e) { if (!_burstFailed) { _burstFailed = true; HaloLog.Error("AI: spawned actors of " + u.name + " failed: " + e); } }
        }

        static bool _burstFailed;

        /// <summary>The actor that drove a unit, also after its death (encounters keep their dead).</summary>
        static HaloActor DeadActorOf(HaloUnit u)
        {
            var a = ActorOf(u);
            if (a != null) return a;
            for (int e = 0; e < Encounters.Count; e++)
            {
                var l = Encounters[e].Actors;
                for (int i = 0; i < l.Count; i++) if (ReferenceEquals(l[i].Unit, u)) return l[i];
            }
            return null;
        }

        /// <summary>Spawns the unit of an actor variant and wraps it in an actor (not yet in an encounter).</summary>
        static HaloActor Spawn(HaloActorVariantDef v, Vector3 pos, float yaw, HaloTeam team)
        {
            HaloUnit u = null;
            try { u = HaloBipeds.Spawn(v.Tag, pos, yaw, team); }
            catch (Exception e) { if (!_spawnFailed) { _spawnFailed = true; HaloLog.Error("AI: HaloBipeds.Spawn(" + v.Tag.Name + ") failed: " + e); } }
            if (u == null && SpawnFallback != null) u = SpawnFallback(v.Tag, pos, yaw, team);
            if (u == null) { if (!_spawnNull) { _spawnNull = true; HaloLog.Warn("AI: no unit for " + v.Tag.Name + " (HaloBipeds.Spawn returned null)"); } return null; }
            if (team != HaloTeam.None) u.Team = team;
            return Wrap(u, v);
        }

        static bool _spawnFailed, _spawnNull;

        static HaloActor Wrap(HaloUnit u, HaloActorVariantDef v)
        {
            var a = new HaloActor(++_nextId)
            {
                Unit = u, V = v, A = v != null ? v.Actor : null,
                Home = u.transform.position, HomeYaw = u.transform.eulerAngles.y,
            };
            a.Dialogue = HaloAiVoice.DialogueFor(u);
            Actors.Add(a);
            if (A0 == null && a.A != null) { A0 = a; HaloLog.Info("AI: first actor " + Short(v != null ? v.Tag : null) + " (" + a.A.Type + ") at " + u.transform.position.ToString("0.0")); }
            return a;
        }

        static HaloActor A0;

        // ------------------------------------------------------------------ selections

        /// <summary>Resolves a script ai name into an encounter and an optional squad / platoon (logs unknown names once).</summary>
        public static bool Resolve(string ai, out HaloAiEncounter enc, out int squad, out int platoon)
        {
            enc = null; squad = -1; platoon = -1;
            if (!Ensure() || string.IsNullOrEmpty(ai)) return false;
            HaloAiScenario.Encounter e;
            if (!Data.Resolve(ai, out e, out squad, out platoon))
            {
                if (Unknown.Add(ai)) HaloLog.Warn("AI: unknown ai '" + ai + "'");
                return false;
            }
            enc = Encounters[e.Index];
            return true;
        }

        static readonly HashSet<string> Unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Is the actor in a script selection ("encounter", "encounter/squad", "encounter/platoon")?</summary>
        public static bool InSelection(HaloActor a, string ai)
        {
            HaloAiEncounter enc; int squad, platoon;
            if (a == null || a.Enc == null || !Resolve(ai, out enc, out squad, out platoon)) return false;
            return a.Enc == enc && HaloAiEncounter.Matches(a, squad, platoon);
        }

        /// <summary>The actors of a selection (living only unless <paramref name="dead"/>), into a reused list.</summary>
        static List<HaloActor> Selection(string ai, bool dead = false)
        {
            Sel.Clear();
            HaloAiEncounter enc; int squad, platoon;
            if (!Resolve(ai, out enc, out squad, out platoon)) return Sel;
            for (int i = 0; i < enc.Actors.Count; i++)
            {
                var a = enc.Actors[i];
                if ((dead || a.Alive) && HaloAiEncounter.Matches(a, squad, platoon)) Sel.Add(a);
            }
            return Sel;
        }

        static readonly List<HaloActor> Sel = new List<HaloActor>(32);

        public static HaloActor ActorOf(HaloUnit u)
        {
            if (u == null) return null;
            for (int i = 0; i < Actors.Count; i++) if (Actors[i].Unit == u) return Actors[i];
            return null;
        }

        /// <summary>Is this unit driven by the AI (Characters / checkpoints may ask).</summary>
        public static bool IsActor(HaloUnit u) { return ActorOf(u) != null; }

        internal static HaloAiBody FirstBodyOf(string ai, HaloActor except)
        {
            var list = Selection(ai);
            for (int i = 0; i < list.Count; i++) if (list[i] != except && list[i].Body != null) return list[i].Body;
            return null;
        }

        // ------------------------------------------------------------------ the stub's API

        /// <summary>Removes them without a trace (ai_erase).</summary>
        public static void Erase(string encounterOrSquad)
        {
            var list = Selection(encounterOrSquad, true);
            for (int i = list.Count - 1; i >= 0; i--) Remove(list[i], true);
        }

        /// <summary>Kills them (ai_kill).</summary>
        public static void Kill(string encounterOrSquad)
        {
            var list = Selection(encounterOrSquad);
            for (int i = list.Count - 1; i >= 0; i--) if (list[i].Unit != null) list[i].Unit.Kill();
        }

        /// <summary>Living actors of an encounter / squad / platoon (ai_living_count).</summary>
        public static int LivingCount(string encounterOrSquad)
        {
            HaloAiEncounter enc; int squad, platoon;
            if (!Resolve(encounterOrSquad, out enc, out squad, out platoon)) return 0;
            return enc.Living(squad, platoon);
        }

        /// <summary>Actors of an encounter / squad that are alive (ai_actors) as units (a new list).</summary>
        public static List<HaloUnit> Units(string encounterOrSquad)
        {
            var r = new List<HaloUnit>();
            var list = Selection(encounterOrSquad);
            for (int i = 0; i < list.Count; i++) if (list[i].Unit != null) r.Add(list[i].Unit);
            return r;
        }

        /// <summary>Everything off: actors' units destroyed, data forgotten (mission leave / before a revert's restore).</summary>
        public static void Clear() { Reset(true); }

        static void Reset(bool allegiances)
        {
            if (Actors.Count > 0 || Data != null)
            {
                if (_tickMsN > 0 && Actors.Count > 0) LogBudget();
            }
            _paths = _pathMaxExpand = _shots = _grenadesThrown = _melees = _evades = _panics = _deaths = _stuck = _berserks = _unreachable = _acquired = _detours = 0;
            System.Array.Clear(NoShot, 0, NoShot.Length); System.Array.Clear(StateCounts, 0, StateCounts.Length); FirstShotBy.Clear();
            for (int i = Actors.Count - 1; i >= 0; i--) Remove(Actors[i], true);
            Actors.Clear();
            ForgetSuspended(); _released = 0; _dormantLogged = false; PendingSeats.Clear();
            Encounters.Clear();
            Navs.Clear(); Nav = null; _navBsp = -999;
            Data = null; Catalog = null; Map = null; Scenario = null; Diff = new HaloAiDifficulty();
            HaloAiBodies.Clear();
            HaloAiVoice.Clear(); SpeedCache.Clear(); Placed.Clear(); Unknown.Clear(); VehicleVariants.Clear();
            _initialPlaced = false; A0 = null;
            ClearConversations();
            if (allegiances) HaloAllegiance.Reset();
            _noiseCount = 0;
            _cpRegistered = false; _netRegistered = false;
        }

        /// <summary>Takes an actor out of the AI (and its unit out of the world when <paramref name="destroyUnit"/>).</summary>
        static void Remove(HaloActor a, bool destroyUnit)
        {
            if (a.Commands != null) a.Commands.Stop();
            if (a.Unit != null) { Resume(a.Unit, SuspendBsp); Resume(a.Unit, SuspendDormant); }
            a.State = HaloActor.Mode.Dead;
            if (a.Enc != null) { a.MoveToSquad(null, -1); }
            Actors.Remove(a);
            if (destroyUnit && a.Unit != null)
            {
                try { HaloBipeds.Erase(a.Unit); } catch (Exception) { }      // (Erase lets latched infection forms go first: they would go with it and count as killed)
            }
            else if (a.Unit != null) { a.Unit.MoveVelocity = Vector3.zero; a.Unit.Firing = false; a.Unit.Frozen = false; }
            a.Unit = null;
        }

        internal static void OnActorDied(HaloActor a, HaloHit killer)
        {
            _deaths++;
            if (a.Enc != null)
            {
                a.Enc.TotalKilled++;
                if (a.Squad >= 0 && a.Squad < a.Enc.Squads.Length) a.Enc.Squads[a.Squad].Killed++;
            }
            if (_deaths == 1) HaloLog.Info("AI: first death: " + a.Label + " (" + (a.A != null ? a.A.Type.ToString() : "?") + ")" + (killer.Cause != null ? " by " + Short(killer.Cause) : ""));
            // friends who saw it
            for (int i = 0; i < Actors.Count; i++)
            {
                var o = Actors[i];
                if (o == a || !o.Alive) continue;
                if (o.Enc != a.Enc && (o.Position - a.Position).sqrMagnitude > 15f * 15f) continue;
                if (!HaloAllegiance.AreFriends(o.Team, a.Team)) continue;
                o.FriendDied(a);
            }
        }

        // ------------------------------------------------------------------ notes for Player.log / the lab

        static int _paths, _pathMaxExpand, _shots, _grenadesThrown, _melees, _evades, _panics, _deaths, _stuck, _berserks;
        static readonly HashSet<HaloActorType> FirstShotBy = new HashSet<HaloActorType>();
        public static int Shots => _shots; public static int GrenadesThrown => _grenadesThrown; public static int Melees => _melees; public static int Evades => _evades;
        public static int Panics => _panics; public static int Deaths => _deaths; public static int PathCount => _paths; public static int Berserks => _berserks;
        static readonly int[] StateCounts = new int[16];
        public static int StateEntered(HaloActor.Mode m) => StateCounts[(int)m];

        internal static void NotePath(int expanded) { _paths++; if (expanded > _pathMaxExpand) _pathMaxExpand = expanded; }
        internal static void NoteStuck() { _stuck++; }
        static readonly int[] NoShot = new int[6];
        internal static void NoteNoShot(int why) { NoShot[why]++; }
        public static string NoShotLine => "not firing at a known target (ticks): unseen " + NoShot[0] + ", no weapon " + NoShot[1] + ", inhibited " + NoShot[2] + ", out of range " + NoShot[3] + ", moving/facing " + NoShot[4] + ", grenade " + NoShot[5];
        static int _unreachable;
        internal static void NoteUnreachable() { _unreachable++; }
        static int _detours;
        internal static void NoteDetour() { _detours++; }
        internal static void NoteState(HaloActor.Mode m) { StateCounts[(int)m]++; }
        internal static void NoteShot(HaloActor a)
        {
            _shots++;
            AiNoise(a);
            if (a.A != null && FirstShotBy.Add(a.A.Type)) HaloLog.Info("AI: first burst by a " + a.A.Type + " (" + Short(a.Unit != null ? a.Unit.Weapon : null) + ") at " + (a.Target != null ? a.Target.ToString() : "?"));
        }
        internal static void NoteGrenade(HaloActor a) { _grenadesThrown++; if (_grenadesThrown == 1) HaloLog.Info("AI: first grenade thrown by " + a.Label); }
        internal static void NoteMelee(HaloActor a) { _melees++; if (_melees == 1) HaloLog.Info("AI: first melee by " + a.Label); }
        internal static void NoteEvade(HaloActor a) { _evades++; if (_evades == 1) HaloLog.Info("AI: first evade by " + a.Label); }
        internal static void NotePanic(HaloActor a, string why) { _panics++; if (_panics == 1) HaloLog.Info("AI: first panic: " + a.Label + " (" + why + ")"); }
        internal static void NoteBerserk(HaloActor a, string why) { _berserks++; if (_berserks == 1) HaloLog.Info("AI: first berserk: " + a.Label + " (" + why + ")"); }
        internal static void NoteSuicide(HaloActor a) { HaloLog.Info("AI: " + a.Label + " goes off (suicidal melee)"); }
        static int _acquired;
        /// <summary>An actor acquired a new target (HaloActor.Acquire): the encounter is alerted - every squad's delay timer starts - and
        /// the actor tells its encounter's friends nearby (<see cref="ShareTarget"/>).</summary>
        internal static void NoteAcquire(HaloActor a, HaloAiBody b, string how)
        {
            if (++_acquired == 1) HaloLog.Info("AI: first target: " + a.Label + " -> " + b + " (" + how + ")");
            if (a == null || b == null || a.Enc == null) return;
            a.Enc.StartAllTimers();
            ShareTarget(a, b);
        }

        /// <summary>Knowledge sharing range (world units). Ours.</summary>
        const float ShareRange = 15f;
        static int _shared;

        /// <summary>
        /// Encounter knowledge sharing (Halo's actors call out what they see): same-encounter actors within 15 wu (ours) that do not already
        /// see a target get this one as heard (seen a second ago at its feet) and start searching it; neither the teller nor the listener may
        /// run a command list with "disable communication".
        /// </summary>
        static void ShareTarget(HaloActor a, HaloAiBody b)
        {
            if (Silenced(a)) return;
            Vector3 at = a.Position; float r2 = ShareRange * HaloSpace.Unit * ShareRange * HaloSpace.Unit;
            var list = a.Enc.Actors;
            int told = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == a || !o.Alive || o.Target == b || (o.Position - at).sqrMagnitude > r2 || Silenced(o) || !o.IsEnemy(b)) continue;
                if (o.Target != null && (o.TargetVisible || o.Now - o.TargetSeenAt <= 2f)) continue;
                if (o.Unit != null && IsSuspended(o.Unit, SuspendBsp)) continue;
                o.Target = b; o.TargetLastSeen = b.Feet; o.TargetSeenAt = o.Now - 1f; o.TargetSince = o.Now;
                if ((o.State == HaloActor.Mode.Idle || o.State == HaloActor.Mode.Patrol) && o.Enc.SquadReleased(o.Squad)) o.SetState(HaloActor.Mode.Search, o.SearchTime());
                told++;
            }
            if (told > 0 && (++_shared == 1 || Verbose)) HaloLog.Info("AI: " + a.Label + " told " + told + " of " + a.Enc.Name + " about " + b);
        }

        static bool Silenced(HaloActor a) { return a.Commands != null && a.Commands.Active && a.Commands.List.DisableCommunication; }

        // ------------------------------------------------------------------ encounter activity (dormancy)

        /// <summary>Activity distance (world units) from the nearest player. Ours.</summary>
        const float ActiveRange = 40f;
        static bool _dormantLogged;
        static readonly List<int> PlayerClusters = new List<int>(8);
        static readonly List<Vector3> PlayerFeet = new List<Vector3>(8);

        /// <summary>
        /// About once a second (campaign only): an encounter is active when ai_force_active, a player is within 40 wu (ours) of one of its
        /// living actors or in the same BSP cluster, one of its actors sees a player within its own vision range (a sight line: snipers), was hit
        /// or fought in the last 10 s (ours), or the encounter it is linked to
        /// (ai_link_activation) is active. Without players (labs, menus) everything stays active.
        /// </summary>
        static void UpdateActivity()
        {
            PlayerFeet.Clear(); PlayerClusters.Clear();
            var all = HaloAiBodies.All;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || !b.IsPlayer || !b.Alive) continue;
                PlayerFeet.Add(b.Feet);
                int c = HaloWorld.ClusterAt(b.Feet + Vector3.up * 0.5f);
                if (c >= 0) PlayerClusters.Add(c);
            }
            bool judge = HaloCampaign.Active && PlayerFeet.Count > 0;
            float r2 = ActiveRange * HaloSpace.Unit * ActiveRange * HaloSpace.Unit;
            int rays = 0;
            for (int e = 0; e < Encounters.Count; e++)
            {
                var enc = Encounters[e];
                if (!judge || enc.ForceActive || enc.Actors.Count == 0) { enc.Active = true; continue; }
                bool active = false;
                for (int i = 0; i < enc.Actors.Count && !active; i++)
                {
                    var a = enc.Actors[i];
                    if (!a.Alive) continue;
                    if (a.ForceActive || (a.Target != null && Time - a.TargetSeenAt < 10f)) { active = true; break; }
                    // scripted work keeps it running (a command list a script waits on, riding / boarding a vehicle) - ours
                    if ((a.Commands != null && a.Commands.Active) || a.Vehicle != null || a.BoardTarget != null || (a.Unit != null && a.Unit.Seat != null)) { active = true; break; }
                    // shot at recently (a sniper hit from beyond 40 wu must react) - ours, like the 10 s fight window
                    if (a.Unit != null && UnityEngine.Time.time - a.Unit.LastHitTime < 10f) { active = true; break; }
                    Vector3 p = a.Position;
                    for (int k = 0; k < PlayerFeet.Count; k++) if ((PlayerFeet[k] - p).sqrMagnitude <= r2) { active = true; break; }
                    if (!active && SeesAPlayer(a, ref rays)) active = true;
                    if (!active && PlayerClusters.Count > 0)
                    {
                        int c = HaloWorld.ClusterAt(p + Vector3.up * 0.5f);
                        if (c >= 0 && PlayerClusters.Contains(c)) active = true;
                    }
                }
                enc.Active = active;
            }
            if (!judge) return;
            // linked encounters activate together (a10: every crew encounter with its *_anti boarders)
            for (int pass = 0; pass < 2; pass++)
                for (int e = 0; e < Encounters.Count; e++) { var enc = Encounters[e]; if (!enc.Active && enc.LinkedTo != null && enc.LinkedTo.Active) enc.Active = true; }
            if (!_dormantLogged)
                for (int e = 0; e < Encounters.Count; e++)
                {
                    var enc = Encounters[e];
                    if (enc.Active || !enc.AllowDormant || enc.Living(-1, -1) == 0) continue;
                    _dormantLogged = true;
                    HaloLog.Info("AI: encounter " + enc.Name + " dormant (no player within " + ActiveRange + " wu or in its cluster): not ticked, units suspended");
                    break;
                }
        }

        const int ActivityRayBudget = 48;     // ours: sight lines per activity pass (about once a second)

        /// <summary>Activity: a player inside the actor's vision range (actv modified / actr max vision distance) with a clear sight line
        /// from the actor's eye to the player's head. A long-range actor (jackal sniper) that the player can see and shoot, and that can see
        /// the player, is never frozen by the 40 wu rule. Over the ray budget the answer is "sees" (never freeze on doubt).</summary>
        static bool SeesAPlayer(HaloActor a, ref int rays)
        {
            if (a.Unit == null) return false;
            float range = a.V != null && a.V.ModifiedVisionRange > 0f ? a.V.ModifiedVisionRange : (a.A != null ? a.A.MaxVisionDistance : 0f);
            if (range <= ActiveRange) return false;     // the distance rule already covers it
            float rr = range * HaloSpace.Unit; rr *= rr;
            Vector3 eye = a.Unit.Eye;
            var all = HaloAiBodies.All;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b == null || !b.IsPlayer || !b.Alive) continue;
                Vector3 d = b.Head - eye;
                float sq = d.sqrMagnitude;
                if (sq > rr) continue;
                if (rays >= ActivityRayBudget) return true;
                rays++;
                float len = Mathf.Sqrt(sq);
                if (len < 0.05f || !Physics.Raycast(eye, d / len, len - 0.05f, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ projectile noticing

        static readonly Vector3[] FlFrom = new Vector3[64], FlTo = new Vector3[64];
        static readonly HaloTeam[] FlTeam = new HaloTeam[64];
        static readonly float[] FlRadius = new float[64];
        static int _noticed;

        /// <summary>
        /// Every third tick: projectiles of the last 0.3 s (HaloProjectiles.CopyRecentFlights) that pass an actor within the projectile's "ai
        /// perception radius" (proj +412) are noticed with the actor's "notice projectile chance" (actr) - the same shot is seen in about
        /// three scans, so each scan rolls 1 - (1 - p)^(1/3) (ours). Noticing alerts a sleeping actor and counts as being shot at (danger,
        /// evasion). AI-vs-AI fire counts too; an actor's own team's shots do not.
        /// </summary>
        static void NoticeProjectiles()
        {
            int n;
            try { n = HaloProjectiles.CopyRecentFlights(FlFrom, FlTo, FlTeam, FlRadius, FlFrom.Length); } catch (Exception) { return; }
            if (n <= 0) return;
            for (int i = 0; i < Actors.Count; i++)
            {
                var a = Actors[i];
                if (!a.Alive || a.A == null || a.A.NoticeProjectileChance <= 0f || a.Unit == null || a.Unit.Suspended) continue;
                Vector3 c = a.Unit.Center;
                for (int f = 0; f < n; f++)
                {
                    if (FlTeam[f] != HaloTeam.None && !HaloAllegiance.AreEnemies(a.Team, FlTeam[f])) continue;
                    float r = FlRadius[f] * HaloSpace.Unit;            // world units -> m
                    if (r <= 0f) continue;
                    if (DistanceToSegmentSq(c, FlFrom[f], FlTo[f]) > r * r) continue;
                    float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(a.A.NoticeProjectileChance), 1f / 3f);
                    if (!Chance(p)) break;
                    a.Hear(FlFrom[f], null);                       // a sleeping actor wakes up (alert)
                    a.ShotAt(FlFrom[f], c - FlFrom[f]);            // danger: evasion / crouching
                    if (++_noticed == 1) HaloLog.Info("AI: first projectile noticed by " + a.Label + " (chance " + a.A.NoticeProjectileChance.ToString("0.##") + ")");
                    break;
                }
            }
        }

        static float DistanceToSegmentSq(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float l = ab.sqrMagnitude;
            float t = l > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / l) : 0f;
            return (a + ab * t - p).sqrMagnitude;
        }

        static readonly Dictionary<string, int> Missing = new Dictionary<string, int>();
        /// <summary>Something the AI cannot do yet: logged once ("AI: unimplemented x"), counted.</summary>
        public static void Unimplemented(string what)
        {
            int n;
            Missing.TryGetValue(what, out n);
            Missing[what] = n + 1;
            if (n == 0) HaloLog.Warn("AI: unimplemented " + what);
        }

        public static string Short(HaloTag t) { if (t == null) return "-"; int i = t.Name.LastIndexOf('\\'); return i >= 0 ? t.Name.Substring(i + 1) : t.Name; }

        internal static string ObjectName(int index)
        {
            var sc = Scenario;
            return sc != null && index >= 0 && index < sc.ObjectNames.Count ? sc.ObjectNames[index] : null;
        }

        /// <summary>Command list "script" atoms: wakes / runs the scenario's ai script reference (Script module).</summary>
        internal static void RunScript(int index)
        {
            string n = Data != null && index >= 0 && index < Data.ScriptReferences.Count ? Data.ScriptReferences[index] : null;
            var vm = HaloScript.Vm;
            int s = vm != null && n != null ? vm.ScriptIndex(n) : -1;
            if (s >= 0) vm.Wake(s);
            else Unimplemented("command list script atom (" + (n ?? index.ToString()) + ": no such script running)");
        }

        /// <summary>Command list "animate" atoms: the scenario's ai animation reference played on the unit.</summary>
        internal static void Animate(HaloActor a, int index)
        {
            var r = Data != null && index >= 0 && index < Data.Animations.Count ? Data.Animations[index] : null;
            if (r == null) return;
            if (a.Unit == null || !a.Unit.CustomAnimation(r.Graph, r.Name, true)) Unimplemented("command list animate atom: '" + r.Name + "' not playable");
        }

        // ------------------------------------------------------------------ checkpoints

        sealed class SaveActor
        {
            public int Enc, Squad; public HaloTag Variant; public Vector3 Pos; public float Yaw, Body, Shield;
            public HaloAiState Initial, Return; public int Grenades; public string Name;
            /// <summary>Seated actors: the vehicle (scenario object name, else its id) and the seat index (-1 on foot).</summary>
            public string VehicleName; public int VehicleId = -1, Seat = -1;
        }

        sealed class SaveState
        {
            public readonly List<SaveActor> Actors = new List<SaveActor>();
            public bool[] Created; public bool[][] Timers; public bool[][] Defending; public HaloTeam[] Teams;
            public bool[] Flags; public float Time;
            /// <summary>Per squad: 0 the encounter's follow target, 1 its own "follow nothing", 2 its own "follow the players".</summary>
            public byte[][] SquadFollow;
        }

        static bool _cpRegistered;
        static void RegisterCheckpoint()
        {
            if (_cpRegistered) return;
            try { HaloCheckpoints.Register("ai", Capture, Restore); _cpRegistered = true; }
            catch (Exception e) { HaloLog.Warn("AI: checkpoint registration failed: " + e.Message); }
        }

        static object Capture()
        {
            var s = new SaveState { Time = Time };
            int n = Encounters.Count;
            s.Created = new bool[n]; s.Timers = new bool[n][]; s.Defending = new bool[n][]; s.Teams = new HaloTeam[n]; s.Flags = new bool[n * 5]; s.SquadFollow = new byte[n][];
            for (int i = 0; i < n; i++)
            {
                var e = Encounters[i];
                s.Created[i] = e.Created; s.Teams[i] = e.Team;
                s.Timers[i] = new bool[e.Squads.Length]; for (int k = 0; k < e.Squads.Length; k++) s.Timers[i][k] = e.Squads[k].TimerExpired;
                s.Defending[i] = new bool[e.Platoons.Length]; for (int k = 0; k < e.Platoons.Length; k++) s.Defending[i][k] = e.Platoons[k].Defending;
                s.SquadFollow[i] = new byte[e.Squads.Length]; for (int k = 0; k < e.Squads.Length; k++) s.SquadFollow[i][k] = (byte)(e.Squads[k].HasFollow ? (e.Squads[k].FollowPlayers ? 2 : 1) : 0);
                s.Flags[i * 5] = e.Blind; s.Flags[i * 5 + 1] = e.Deaf; s.Flags[i * 5 + 2] = e.Braindead; s.Flags[i * 5 + 3] = e.EncFollowPlayers; s.Flags[i * 5 + 4] = e.Respawn;
            }
            foreach (var a in Actors)
            {
                if (!a.Alive || a.Unit == null || a.V == null) continue;
                // seated actors keep their vehicle and seat; others their last position on walkable ground (never mid-fall / sunk)
                var veh = a.Unit.Seat != null ? a.Unit.Seat.GetComponentInParent<HaloVehicle>() : null;
                int seat = veh != null ? veh.SeatOfUnit(a.Unit) : -1;
                if (seat < 0) veh = null;
                s.Actors.Add(new SaveActor
                {
                    Enc = a.Enc != null ? a.Enc.Index : -1, Squad = a.Squad, Variant = a.V.Tag, Pos = veh != null ? a.Unit.transform.position : a.Unit.LastGroundPos, Yaw = a.Unit.Yaw,
                    Body = a.Unit.Body, Shield = a.Unit.Shield, Initial = a.InitialState, Return = a.ReturnState, Grenades = a.Unit.Grenades, Name = a.Unit.ObjectName,
                    VehicleName = veh != null ? veh.ObjectName : null, VehicleId = veh != null ? veh.Id : -1, Seat = seat,
                });
            }
            return s;
        }

        static void Restore(object o)
        {
            var s = o as SaveState;
            if (s == null || !Ensure()) return;
            _initialPlaced = true;
            for (int i = Actors.Count - 1; i >= 0; i--) Remove(Actors[i], true);
            ClearConversations();
            for (int i = 0; i < Encounters.Count && i < s.Created.Length; i++)
            {
                var e = Encounters[i];
                e.Created = s.Created[i]; e.Team = s.Teams[i];
                for (int k = 0; k < e.Squads.Length; k++) { e.Squads[k].TimerExpired = s.Timers[i][k]; e.Squads[k].TimerStarted = s.Timers[i][k]; e.Squads[k].TimerEnds = -1f; }
                for (int k = 0; k < e.Platoons.Length; k++) e.Platoons[k].Defending = s.Defending[i][k];
                e.Blind = s.Flags[i * 5]; e.Deaf = s.Flags[i * 5 + 1]; e.Braindead = s.Flags[i * 5 + 2]; e.SetFollow(-1, s.Flags[i * 5 + 3], null, null);
                if (s.SquadFollow != null && i < s.SquadFollow.Length) for (int k = 0; k < e.Squads.Length && k < s.SquadFollow[i].Length; k++) if (s.SquadFollow[i][k] != 0) e.SetFollow(k, s.SquadFollow[i][k] == 2, null, null); e.Respawn = s.Flags[i * 5 + 4];
            }
            int n = 0;
            foreach (var r in s.Actors)
            {
                var v = Catalog.Variant(r.Variant);
                if (v == null) continue;
                var enc = r.Enc >= 0 && r.Enc < Encounters.Count ? Encounters[r.Enc] : null;
                var a = Spawn(v, r.Pos, r.Yaw, enc != null ? enc.Team : HaloTeam.None);
                if (a == null) continue;
                if (enc != null && HaloCampaign.Active && enc.Data.Bsp >= 0 && _navBsp >= 0 && enc.Data.Bsp != _navBsp) { a.Unit.transform.position = r.Pos; Suspend(a.Unit, SuspendBsp); }
                a.Unit.Body = r.Body; a.Unit.Shield = r.Shield; a.Unit.Grenades = r.Grenades;
                if (!string.IsNullOrEmpty(r.Name)) a.Unit.SetName(r.Name);
                a.InitialState = r.Initial; a.ReturnState = r.Return;
                if (enc != null) a.MoveToSquad(enc, r.Squad); else a.Free = true;
                a.EnterAiState(r.Initial == HaloAiState.Sleeping ? HaloAiState.Alert : r.Initial);
                if (r.Seat >= 0) { if (!Reseat(a, r)) PendingSeats.Add(new PendingSeat { Actor = a, Save = r, Until = UnityEngine.Time.unscaledTime + 3f }); }
                else if (!IsSuspended(a.Unit, SuspendBsp)) a.Unit.Settle();
                n++;
            }
            HaloLog.Info("AI: checkpoint restored: " + n + " actors");
        }

        struct PendingSeat { public HaloActor Actor; public SaveActor Save; public float Until; }
        static readonly List<PendingSeat> PendingSeats = new List<PendingSeat>(4);

        /// <summary>Puts a restored actor back on its saved vehicle seat (vehicle_load_magic-like). False when the vehicle is not there (yet).</summary>
        static bool Reseat(HaloActor a, SaveActor r)
        {
            if (a == null || a.Unit == null || r.Seat < 0) return true;
            HaloVehicle v = null;
            if (!string.IsNullOrEmpty(r.VehicleName)) { var go = HaloObjects.Find(r.VehicleName); if (go != null) v = go.GetComponentInParent<HaloVehicle>(); }
            if (v == null && r.VehicleId >= 0) v = HaloVehicles.Find(r.VehicleId);
            if (v == null) return false;
            bool ok = v.SeatUnit(r.Seat, a.Unit);
            if (!ok) HaloLog.Warn("AI: checkpoint: " + a.Label + " could not take seat " + r.Seat + " of " + v.name);
            return true;
        }

        /// <summary>Restored seated actors whose vehicle came back after the AI part (retried for 3 s, then they stand where they are).</summary>
        static void RetrySeats()
        {
            for (int i = PendingSeats.Count - 1; i >= 0; i--)
            {
                var p = PendingSeats[i];
                bool done = p.Actor == null || !p.Actor.Alive || Reseat(p.Actor, p.Save);
                if (!done && UnityEngine.Time.unscaledTime < p.Until) continue;
                if (!done) { HaloLog.Warn("AI: checkpoint: " + p.Actor.Label + "'s vehicle " + (p.Save.VehicleName ?? "#" + p.Save.VehicleId) + " is gone: left on foot"); p.Actor.Unit.Settle(); }
                PendingSeats.RemoveAt(i);
            }
        }
    }

    /// <summary>[AI module, I] Steps the AI from Update when the Mission module does not tick it (labs, multiplayer-less tests).</summary>
    public sealed class HaloAiDriver : MonoBehaviour
    {
        static HaloAiDriver _instance;
        public static void Ensure(Transform parent)
        {
            if (_instance != null) return;
            var go = new GameObject("Halo AI Driver");
            if (parent != null) go.transform.SetParent(parent, false);
            _instance = go.AddComponent<HaloAiDriver>();
        }

        private void Update() { HaloAi.Frame(Time.deltaTime); }
        private void OnDestroy() { if (_instance == this) _instance = null; }
    }
}

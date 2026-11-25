using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Actor sensing and steering lab (workstream 5): DevTools/lab.sh ai-sense [-halomap a30] [-haloenc lz_search] [-haloseconds 25].
    /// Builds the encounter's BSP, places the encounter and a stand-in player, then:
    /// 1. perception: every actor but one test grunt is braindead and blind; the stand-in appears 8 wu BEHIND the idle grunt. The grunt must
    ///    not acquire it for at least 1.4 s, and every sight acquisition must have taken the actr perception time ('AI: ... acquired ... after
    ///    X s'). Then the stand-in moves in FRONT of a second idle actor and the same holds.
    /// 2. migration (runs first, before anyone has a target): an idle awake actor is teleported 10 m from its spawn and ai_migrate'd to a
    ///    squad without guard positions; for 20 s (every frame) it must stay at rest, never get closer to its spawn and never get a goal there
    ///    (the pre-change code walked such an actor back to its spawn).
    /// 3. steering: once a second every grounded on-foot actor is located on the navigation; steps from on- to off-navigation are counted
    ///    (must be 0) together with the edge holds, stuck give-ups and mover recoveries.
    /// 4. command lists (c20 enc1 and the like): when an actor runs a command list without 'allow initiative', the stand-in walks up to
    ///    it: the list must break ('AI: ... broke command list ... (proximity)') and the actor must move.
    /// </summary>
    [InitializeOnLoadAttribute]
    static class HaloLabAiSense
    {
        static HaloLabAiSense()
        {
            HaloLabRunner.Register("ai-sense", r => Run(r));
        }

        sealed class Dummy : MonoBehaviour, IHaloDamageable
        {
            public int Hits;
            public void TakeHit(HaloHit hit) { Hits++; }
        }

        static IEnumerator Run(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "a30");
            string encName = HaloLabBoot.Arg("-haloenc", "lz_search");
            float seconds = float.Parse(HaloLabBoot.Arg("-haloseconds", "25"), System.Globalization.CultureInfo.InvariantCulture);
            var entry = r.Game.Install.Map(mapName);
            if (entry == null) foreach (var e in r.Game.Install.Campaign) if (e.Name == mapName) entry = e;
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            var map = new HaloMap(entry.File);
            var sc = new HaloScenario(map);
            var data = new HaloAiScenario(sc);
            var enc = data.FindEncounter(encName);
            if (!r.Check(enc != null, "encounter " + encName)) yield break;
            int bsp = Mathf.Clamp(enc.Bsp, 0, sc.Bsps.Count - 1);
            HaloLabRunner.Log("ai-sense: " + mapName + " encounter " + encName + " (bsp " + bsp + ", " + enc.Squads.Count + " squads)");

            Time.captureFramerate = 60;
            HaloCombat.LabMap = map; HaloCombat.LabScenario = sc;
            HaloAi.LabMap = map; HaloAi.LabScenario = sc; HaloAi.LabBsp = bsp; HaloAi.Verbose = false;
            var hub = GameObject.Find("Halo Hub");
            if (hub != null) hub.SetActive(false);
            var stage = new GameObject("ai sense lab");
            HaloBipeds.LabParent = stage.transform;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.0f; sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

            HaloWorld.PlaceObjects = false;
            var root = new GameObject("Lab World Root");
            root.transform.position = HaloSpace.MapOrigin;
            var it = HaloWorld.Build(map, sc, root.transform, null, bsp);
            while (it.MoveNext()) yield return it.Current;
            r.Check(HaloWorld.Ready, "bsp " + bsp + " built");
            HaloWorldDriver.View = r.Cam;
            Physics.SyncTransforms();
            yield return null;

            // the stand-in player starts far away (no one sees it while the encounter settles)
            var standIn = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            standIn.name = "stand-in player";
            standIn.transform.SetParent(stage.transform, false);
            standIn.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            standIn.layer = HaloLayers.Hitbox;
            standIn.AddComponent<Dummy>();
            var feet = new GameObject("feet"); feet.transform.SetParent(stage.transform, false);
            System.Action<Vector3> putPlayer = p => { feet.transform.position = p; standIn.transform.position = p + Vector3.up * 0.9f; };
            putPlayer(new Vector3(0f, -5000f, 0f));
            HaloAiBodies.StandInPlayer = feet.transform;

            HaloAiDriver.Ensure(stage.transform);
            HaloAi.Place(encName);
            for (int k = 0; k < 180; k++) yield return null;      // 3 s: land, settle
            int placed = HaloAi.LivingCount(encName);
            if (!r.Check(placed > 0, "ai_place " + encName + ": " + placed + " living actors")) { HaloAi.Clear(); yield break; }
            var spawn = new Dictionary<HaloActor, Vector3>();
            foreach (var a in HaloAi.Actors) { spawn[a] = a.Position; HaloLabRunner.Log("  actor " + a.Label + " (" + (a.A != null ? a.A.Type.ToString() : "?") + ") " + a.State + "/" + a.InitialState + " at " + a.Position.ToString("0.0") + (a.CommandStatus != 0 ? " command list " + (a.Commands != null && a.Commands.List != null ? a.Commands.List.Name : "?") : "")); }

            // steering watch (runs through every phase)
            int offNavSteps = 0, samples = 0;
            var wasOn = new Dictionary<HaloActor, bool>();
            System.Action sample = () =>
            {
                var nav = HaloAi.Nav;
                if (nav == null) return;
                foreach (var a in HaloAi.Actors)
                {
                    if (!a.Alive || a.Unit == null || a.Flying || a.Unit.Seat != null || !a.Unit.Grounded) continue;
                    Vector3 h = HaloSpace.ToHalo(a.Position);
                    bool on = nav.Locate(h.x, h.y, h.z + 0.05f, 0.3f, 0.6f, 0.3f) >= 0;
                    samples++;
                    bool before;
                    if (wasOn.TryGetValue(a, out before) && before && !on)
                    {
                        offNavSteps++;
                        HaloLabRunner.Log("off the navigation: " + a.Label + " at " + a.Position.ToString("0.0") + (a.Moving ? " going to " + a.Goal.ToString("0.0") : ""));
                    }
                    wasOn[a] = on;
                }
            };
            int blocks0 = HaloActor.OffNavBlocks, stuck0 = HaloActor.StuckGiveUps, rec0 = HaloActor.Recoveries;

            // ---- 2. migration: no walk back to the spawn (runs FIRST, while no actor has a target: once one actor sees the player the
            // whole encounter shares it and no idle actor is left). The pre-change code walked an Idle actor (initial state not None, not
            // asleep) more than 2 m from Home back to Home = its spawn; Halo has no such walk. The actor must be eligible for that old walk
            // (idle, awake, initial state set), is put 10 m from its spawn and ai_migrate'd to a squad without guard positions (nothing
            // should move it); then every frame for 20 s its position, its movement goal and its state are watched.
            HaloActor mig = null; int toSquad = -1;
            foreach (var a in HaloAi.Actors) { a.Deaf = true; a.Blind = true; }
            for (int pass = 0; pass < 4 && mig == null; pass++)
                foreach (var a in HaloAi.Actors)
                {
                    if (!a.Alive || a.Unit == null || a.Unit.Seat != null || a.Flying || a.Enc == null || a.Target != null || !a.Unit.Grounded) continue;
                    if (a.CommandStatus == 2 || a.CommandStatus == 3) continue;
                    if (pass < 3 && (a.State != HaloActor.Mode.Idle || a.InitialState == HaloAiState.Sleeping || a.InitialState == HaloAiState.None)) continue;
                    toSquad = -1;
                    for (int s = 0; s < a.Enc.Data.Squads.Count; s++)
                    {
                        if (s == a.Squad) continue;
                        var sq = a.Enc.Data.Squads[s];
                        bool guards = sq.DefendingGuard != 0u || sq.AttackingGuard != 0u;
                        if (pass == 0 && guards) continue;
                        toSquad = s; break;
                    }
                    if (toSquad >= 0) { mig = a; break; }
                }
            if (mig != null)
            {
                Vector3 s0 = spawn.ContainsKey(mig) ? spawn[mig] : mig.Position;
                Vector3 moveTo = s0;
                for (int k = 0; k < 16; k++)
                {
                    Vector3 c = HaloActor.OnNav(s0 + Quaternion.Euler(0f, 22.5f * k, 0f) * Vector3.forward * 10f);
                    if ((c - s0).magnitude > 7f && Mathf.Abs(c.y - s0.y) < 2f && !Physics.Linecast(s0 + Vector3.up, c + Vector3.up, HaloAiBodies.Blockers)) { moveTo = c; break; }
                }
                var toSq = mig.Enc.Data.Squads[toSquad];
                string state0 = mig.State + "/" + mig.InitialState;
                mig.Unit.Teleport(moveTo, mig.Unit.transform.eulerAngles.y);
                for (int k = 0; k < 10; k++) yield return null;          // lands on its new spot
                string fromName = mig.Enc.Name + "/" + mig.Enc.Data.Squads[mig.Squad].Name, toName = mig.Enc.Name + "/" + toSq.Name;
                HaloAi.Migrate(fromName, toName);
                Vector3 p0 = mig.Position;
                HaloLabRunner.Log("script: ai_migrate " + fromName + " " + toName + " (" + mig.Label + ", " + state0 + ", put " + (p0 - s0).magnitude.ToString("0.0") + " m from its spawn; target squad guard masks defending " +
                    toSq.DefendingGuard + " attacking " + toSq.AttackingGuard + "; Home now " + (mig.Home - s0).magnitude.ToString("0.0") + " m from the spawn)");
                float minToSpawn = float.MaxValue, minGoalToSpawn = float.MaxValue, maxMoved = 0f; int stateChanges = 0, movingFrames = 0, frames = 0;
                var lastState = mig.State; string states = mig.State.ToString();
                for (int k = 0; k < 60 * 20; k++)
                {
                    yield return null;
                    if (k % 60 == 0) sample();
                    if (!mig.Alive) break;
                    frames++;
                    minToSpawn = Mathf.Min(minToSpawn, (mig.Position - s0).magnitude);
                    maxMoved = Mathf.Max(maxMoved, (mig.Position - p0).magnitude);
                    if (mig.Moving) { movingFrames++; minGoalToSpawn = Mathf.Min(minGoalToSpawn, (mig.Goal - s0).magnitude); }
                    if (mig.State != lastState) { stateChanges++; lastState = mig.State; if (states.Length < 120) states += " > " + mig.State; }
                }
                float dist = (mig.Position - s0).magnitude;
                HaloLabRunner.Log("after 20 s " + mig.Label + " is " + dist.ToString("0.0") + " m from its spawn (closest in 20 s " + minToSpawn.ToString("0.0") + " m), moved at most " + maxMoved.ToString("0.0") + " m from where it was put, " +
                    "moving " + movingFrames + "/" + frames + " frames" + (movingFrames > 0 ? " (closest goal to the spawn " + minGoalToSpawn.ToString("0.0") + " m)" : "") + ", states " + states + ", squad now " + (mig.Enc != null && mig.Squad >= 0 ? mig.Enc.Data.Squads[mig.Squad].Name : "?"));
                bool put = (p0 - s0).magnitude > 6f;
                r.Check(put, "the migration test actor was put " + (p0 - s0).magnitude.ToString("0.0") + " m from its spawn (> 6 m)");
                r.Check(mig.Squad == toSquad, "ai_migrate moved " + mig.Label + " into " + toName);
                r.Check(state0.StartsWith("Idle") && stateChanges == 0, "the migrated actor stayed at rest (" + states + ") - the case the old code walked home");
                r.Check(put && minToSpawn > (p0 - s0).magnitude - 2f, "the migrated actor never walked back toward its spawn (closest " + minToSpawn.ToString("0.0") + " m of " + (p0 - s0).magnitude.ToString("0.0") + " m)");
                r.Check(movingFrames == 0 || minGoalToSpawn > 3f, "the migrated actor never had a movement goal at its spawn (" + (movingFrames == 0 ? "no goal" : "closest goal " + minGoalToSpawn.ToString("0.0") + " m") + ")");
                r.Aim(mig.Position + Vector3.up * 4f + (mig.Position - s0).normalized * 4f, Vector3.Lerp(mig.Position, s0, 0.5f), 75f); yield return null; r.Shot("sense_migrated");
            }
            else r.Check(false, "an idle actor with another squad to migrate to");
            foreach (var a in HaloAi.Actors) { a.Deaf = false; a.Blind = false; }

            // ---- 1. perception
            HaloActor grunt = null, second = null;
            // the behind test wants an idle actor (a grunt if there is one); the front test another actor at rest with no target (idle,
            // else one in the 'searching' initial state)
            for (int pass = 0; pass < 3 && grunt == null; pass++)
                foreach (var a in HaloAi.Actors)
                {
                    if (a == mig || !a.Alive || a.Unit == null || a.Unit.Seat != null || a.Flying || a.Target != null || !a.Unit.Grounded) continue;
                    if (a.CommandStatus == 2 || a.CommandStatus == 3) continue;
                    if (pass < 2 && a.State != HaloActor.Mode.Idle) continue;
                    if (pass == 0 && (a.A == null || a.A.Type != HaloActorType.Grunt)) continue;
                    grunt = a; break;
                }
            for (int pass = 0; pass < 2 && second == null; pass++)
                foreach (var a in HaloAi.Actors)
                {
                    if (a == grunt || a == mig || !a.Alive || a.Unit == null || a.Unit.Seat != null || a.Flying || a.Target != null || !a.Unit.Grounded) continue;
                    if (a.CommandStatus == 2 || a.CommandStatus == 3) continue;
                    if (a.State != HaloActor.Mode.Idle && (pass == 0 || a.State != HaloActor.Mode.Search)) continue;
                    second = a; break;
                }
            if (grunt != null)
            {
                foreach (var a in HaloAi.Actors) { a.Deaf = true; a.Blind = a != grunt; a.Braindead = a != grunt; }
                Vector3 at = SpotAround(grunt, -1f);
                putPlayer(at);
                HaloLabRunner.Log("perception: " + grunt.Label + " (" + grunt.A.Type + ", perception time combat/guard/noncombat " + grunt.A.CombatPerceptionTime.ToString("0.0") + "/" + grunt.A.GuardPerceptionTime.ToString("0.0") + "/" + grunt.A.NonCombatPerceptionTime.ToString("0.0") + " s) with the stand-in " + ((at - grunt.Position).magnitude / HaloSpace.Unit).ToString("0.0") + " wu behind it");
                float t0 = HaloAi.Time; int acq0 = HaloActor.SightAcquires; float acquiredAfter = -1f;
                r.Aim(grunt.Position + Vector3.up * 3f + (grunt.Position - at).normalized * 3f, at, 70f); yield return null; r.Shot("sense_behind_start");
                for (int k = 0; k < 60 * 8; k++)
                {
                    yield return null;
                    if (k % 60 == 0) sample();
                    if (grunt.Target != null && acquiredAfter < 0f) { acquiredAfter = HaloAi.Time - t0; break; }
                }
                HaloLabRunner.Log("the grunt acquired " + (acquiredAfter < 0f ? "nothing in 8 s" : "the stand-in after " + acquiredAfter.ToString("0.0") + " s (perception delay " + grunt.LastAcquireDelay.ToString("0.0") + " s)"));
                r.Check(acquiredAfter < 0f || acquiredAfter >= 1.4f, "an idle grunt with the player behind it did not acquire for >= 1.4 s (" + acquiredAfter.ToString("0.0") + ")");
                if (HaloActor.SightAcquires > acq0) r.Check(grunt.LastAcquireDelay >= Mathf.Min(grunt.A.GuardPerceptionTime, grunt.A.NonCombatPerceptionTime) - 0.15f, "sight acquisition took the perception time (" + grunt.LastAcquireDelay.ToString("0.0") + " s)");
                r.Aim(grunt.Position + Vector3.up * 3f + (grunt.Position - at).normalized * 3f, at, 70f); yield return null; r.Shot("sense_behind_end");

                if (second != null)
                {
                    foreach (var a in HaloAi.Actors) { a.Blind = a != second; a.Braindead = a != second; }
                    Vector3 front = SpotAround(second, 1f);
                    putPlayer(front);
                    float t1 = HaloAi.Time; float after2 = -1f;
                    for (int k = 0; k < 60 * 8; k++)
                    {
                        yield return null;
                        if (k % 60 == 0) sample();
                        if (second.Target != null) { after2 = HaloAi.Time - t1; break; }
                    }
                    float need = second.A != null ? Mathf.Min(second.A.GuardPerceptionTime, second.A.NonCombatPerceptionTime) : 0f;
                    HaloLabRunner.Log("in front of " + second.Label + " (" + (second.A != null ? second.A.Type.ToString() : "?") + "): acquired after " + after2.ToString("0.0") + " s (delay " + second.LastAcquireDelay.ToString("0.0") + " s, need " + need.ToString("0.0") + " s)");
                    float needMax = second.A != null ? Mathf.Max(second.A.GuardPerceptionTime, second.A.NonCombatPerceptionTime) : 0f;
                    r.Check(after2 >= need - 0.15f, "an actor with the player in plain view waited its perception time (" + after2.ToString("0.0") + " >= " + need.ToString("0.0") + ")");
                    r.Check(after2 >= 0f && after2 <= needMax + 1.0f, "an actor with the player in plain view acquired it soon after its perception time (" + after2.ToString("0.0") + " <= " + (needMax + 1f).ToString("0.0") + ")");
                    r.Aim(second.Position + Vector3.up * 2.5f + (second.Position - front).normalized * 3f, front, 70f); yield return null; r.Shot("sense_front");
                }
                putPlayer(new Vector3(0f, -5000f, 0f));
                foreach (var a in HaloAi.Actors) { a.Blind = true; a.Braindead = false; }
            }
            else r.Check(false, "an idle grounded actor for the perception test");

            // ---- 4. command lists without initiative break on proximity
            HaloActor cmd = null;
            foreach (var a in HaloAi.Actors)
                if (a.Alive && a.Unit != null && a.Unit.Seat == null && a.Commands != null && a.Commands.Active && a.Commands.List != null && !a.Commands.List.AllowInitiative) { cmd = a; break; }
            if (cmd != null)
            {
                foreach (var a in HaloAi.Actors) { a.Blind = false; a.Deaf = false; a.Braindead = false; }
                int breaks0 = HaloActor.CommandBreaks;
                Vector3 c0 = cmd.Position;
                string listName = cmd.Commands.List.Name;
                putPlayer(HaloActor.OnNav(c0 + cmd.Unit.transform.forward * 1.5f));
                HaloLabRunner.Log("command list: the stand-in walks up to " + cmd.Label + " running " + listName);
                for (int k = 0; k < 60 * 6; k++) { yield return null; if (k % 60 == 0) sample(); }
                r.Check(HaloActor.CommandBreaks > breaks0, "command list " + listName + " broke on proximity (" + (HaloActor.CommandBreaks - breaks0) + " breaks)");
                HaloLabRunner.Log(cmd.Label + " moved " + (cmd.Position - c0).magnitude.ToString("0.0") + " m, state " + cmd.State + (cmd.Target != null ? " -> " + cmd.Target : ""));
                r.Aim(c0 + Vector3.up * 3f + Vector3.back * 4f, c0, 70f); yield return null; r.Shot("sense_command_break");
            }
            else HaloLabRunner.Log("command lists: no actor runs a list without initiative here");

            // ---- 3. steering summary
            for (int k = 0; k < 60 * Mathf.Max(1f, seconds - 20f); k++) { yield return null; if (k % 60 == 0) sample(); }
            HaloLabRunner.Log("steering: " + samples + " samples, off-navigation steps " + offNavSteps + ", edge holds " + (HaloActor.OffNavBlocks - blocks0) + ", stuck give-ups " + (HaloActor.StuckGiveUps - stuck0) + ", mover recoveries " + (HaloActor.Recoveries - rec0) + ", danger retreats " + HaloActor.DangerRetreats + ", sight acquisitions " + HaloActor.SightAcquires);
            r.Check(offNavSteps == 0, "no actor stepped off the navigation (" + offNavSteps + ")");
            HaloAi.LogBudget();
            HaloAi.Clear();
        }

        /// <summary>A point 8 wu from the actor, behind it (side -1) or in front (side 1), on the navigation and in its line of sight; the best
        /// of a few angles.</summary>
        static Vector3 SpotAround(HaloActor a, float side)
        {
            Vector3 eye = a.Unit.Eye, pos = a.Position;
            Vector3 fwd = a.Unit.transform.forward; fwd.y = 0f; fwd.Normalize();
            float dist = 8f * HaloSpace.Unit;
            Vector3 best = HaloActor.OnNav(pos + fwd * side * dist);
            for (int k = 0; k < 7; k++)
            {
                float ang = (k % 2 == 0 ? 1f : -1f) * 15f * ((k + 1) / 2);
                for (int s = 0; s < 3; s++)
                {
                    float d = dist * (1f - 0.25f * s);
                    Vector3 c = HaloActor.OnNav(pos + Quaternion.Euler(0f, ang, 0f) * fwd * side * d);
                    if (Mathf.Abs(c.y - pos.y) > 3f) continue;
                    if (!Physics.Linecast(eye, c + Vector3.up * 1.6f, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore)) return c;
                }
            }
            return best;
        }
    }
}

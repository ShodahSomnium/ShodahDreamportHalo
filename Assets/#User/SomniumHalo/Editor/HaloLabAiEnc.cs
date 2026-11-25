using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Encounter lab (workstream 7: encounters, BSP dormancy, squad orders, carrier bursts).
    /// - ai-enc [-halomission a10] [-halomission2 a30]: a10 from code (scripts stopped, back to the start BSP), the start BSP's encounters
    ///   placed plus one encounter of another BSP (must come out suspended and hidden); switch_bsp to that other BSP: every start-BSP actor
    ///   is suspended ("AI: n actors suspended for bsp k" logged) and its feet height stays within 0.1 m for 20 s; the other BSP's actors
    ///   are shown again; switch back: the start-BSP actors resume on their floor. Then a30: first_wave's wave_1 platoon placed and
    ///   "ai_retreat first_wave/wave_1_lz_grunt": only the grunt squad moves to its "maneuver to" squad (the squad of every actor logged).
    /// - ai-enc-carrier [-halomission d20]: a flood carrier spawned in front of the player and killed: its body is destroyed after the death
    ///   animation and 3..6 infection forms (normal difficulty) are released, land on the floor and go for the player (the campaign's own
    ///   local player, in sight of the burst): twice - a carrier killed unaware (the forms must sense the player themselves) and an aware one
    ///   (the forms inherit its target). The forms must take the player as target, reach it, hurt it (shield / health) and none may die away
    ///   from it.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabAiEnc
    {
        static HaloLabAiEnc()
        {
            HaloLabRunner.Register("ai-enc", r => Run(r));
            HaloLabRunner.Register("ai-enc-carrier", r => Carrier(r));
            HaloLabRunner.Register("ai-enc-area", r => Area(r));
        }

        static IEnumerator Seconds(float s)
        {
            float t = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < t) yield return null;
        }

        static void EnsurePlayer()
        {
            CharacterController sdk = Object.FindFirstObjectByType<CharacterController>();
            if (sdk != null)
            {
                foreach (MonoBehaviour b in sdk.GetComponents<MonoBehaviour>()) b.enabled = false;
                return;
            }
            var go = new GameObject("Lab Player");
            go.transform.position = HaloSpace.RoomOrigin;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            go.layer = HaloLayers.Avatar;
        }

        static float Flat(Vector3 d) { d.y = 0f; return d.magnitude; }

        static Vector3 Feet() { Vector3 f; return HaloPlayer.TryFeet(out f) ? f : Vector3.zero; }

        static Vector3 Ground(Vector3 p)
        {
            RaycastHit h;
            if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out h, 6f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) return h.point + Vector3.up * 0.02f;
            return p;
        }

        /// <summary>Feet within 0.3 m of a floor below / at them.</summary>
        static bool OnFloor(Vector3 feet, out float gap)
        {
            RaycastHit h;
            gap = 99f;
            if (!Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out h, 3f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) return false;
            gap = h.distance - 0.5f;
            return Mathf.Abs(gap) <= 0.3f;
        }

        static IEnumerator StartMission(HaloLabRunner r, string mission)
        {
            HaloCampaign.StartMission(mission, HaloCampaign.Difficulty.Normal);
            yield return r.Wait(() => HaloSession.Playing && HaloCampaign.Active, 900f, mission + ": the mission to start");
            if (!r.Check(HaloSession.Playing && HaloCampaign.Active, mission + ": playing")) yield break;
            yield return Seconds(1f);
            HaloScript.Stop();
            HaloCinematic.Clear();
            var sl = HaloSession.Scenario.StartLocations;
            int startBsp = sl.Count > 0 ? sl[0].BspIndex : 0;
            if (HaloCampaign.BspIndex != startBsp)
            {
                HaloCampaign.SwitchBsp(startBsp);
                yield return r.Wait(() => HaloCampaign.BspIndex == startBsp && !HaloCampaign.SwitchingBsp, 120f, mission + ": back to the start BSP");
            }
            if (sl.Count > 0)
            {
                string how;
                HaloPlayer.Teleport(HaloSpace.MapOrigin + HaloSpace.Pos(sl[0].Position), 90f - sl[0].Facing * Mathf.Rad2Deg, out how);
                Physics.SyncTransforms();
            }
            yield return Seconds(1f);
        }

        // ------------------------------------------------------------------ ai-enc

        static IEnumerator Run(HaloLabRunner r)
        {
            EnsurePlayer();
            yield return r.Wait(() => HaloGame.Current != null && HaloGame.Current.Install != null, 120f, "the install");
            if (HaloGame.Current == null || HaloGame.Current.Install == null) { r.Fail("no install"); yield break; }
            HaloAi.Verbose = false;
            yield return BspDormancy(r, HaloLabBoot.Arg("-halomission", "a10"));
            HaloCampaign.LeaveMission();
            yield return Seconds(2f);
            yield return SquadRetreat(r, HaloLabBoot.Arg("-halomission2", "a30"));
            HaloAi.LogBudget();
            HaloCampaign.LeaveMission();
        }

        static IEnumerator BspDormancy(HaloLabRunner r, string mission)
        {
            yield return StartMission(r, mission);
            if (!HaloCampaign.Active) yield break;
            int home = HaloCampaign.BspIndex;
            int nBsp = HaloSession.Scenario.Bsps.Count;
            if (!r.Check(nBsp > 1, mission + ": " + nBsp + " BSPs")) yield break;
            // the start BSP's encounters (the initial ones are placed by the AI; place the rest that have starting locations)
            HaloAiEncounter otherEnc = null; int other = -1;
            foreach (var e in HaloAi.Encounters)
            {
                int eb = e.Data.Bsp;
                int starts = 0; foreach (var s in e.Data.Squads) starts += s.Starts.Count;
                if (starts == 0) continue;
                if (eb == home && !e.Created) HaloAi.Place(e.Name);
                else if (eb >= 0 && eb != home && otherEnc == null) { otherEnc = e; other = eb; }
            }
            if (!r.Check(otherEnc != null, mission + ": an encounter of another BSP (" + (otherEnc != null ? otherEnc.Name + ", bsp " + other : "none") + ")")) yield break;
            HaloAi.Place(otherEnc.Name);
            yield return Seconds(3f);
            // who stands where now
            var homeActors = new List<HaloActor>(); var feetY = new List<float>();
            int otherPlaced = 0, otherSuspended = 0;
            foreach (var a in HaloAi.Actors)
            {
                if (a.Unit == null || a.Enc == null) continue;
                if (a.Enc == otherEnc) { otherPlaced++; if (HaloAi.SuspendedForBsp(a.Unit)) otherSuspended++; continue; }
                if (a.Enc.Data.Bsp != home || !a.Alive) continue;
                homeActors.Add(a); feetY.Add(a.Unit.transform.position.y);
            }
            r.Check(otherPlaced > 0 && otherSuspended == otherPlaced, mission + ": " + otherEnc.Name + " placed while bsp " + home + " is loaded: " + otherSuspended + "/" + otherPlaced + " suspended (hidden)");
            if (!r.Check(homeActors.Count > 0, mission + ": " + homeActors.Count + " living actors of bsp " + home)) yield break;
            Vector3 f0 = homeActors[0].Unit.transform.position;
            r.Aim(f0 + new Vector3(4f, 3f, 4f), f0 + Vector3.up, 60f); r.Shot("enc-before");

            // switch away: home actors suspended, their feet stay
            HaloCampaign.SwitchBsp(other);
            yield return r.Wait(() => HaloCampaign.BspIndex == other && !HaloCampaign.SwitchingBsp, 180f, mission + ": switch to bsp " + other);
            yield return Seconds(1f);
            int susp = 0; foreach (var a in homeActors) if (a.Unit != null && HaloAi.SuspendedForBsp(a.Unit)) susp++;
            r.Check(susp == homeActors.Count, mission + ": bsp " + other + " loaded: " + susp + "/" + homeActors.Count + " bsp-" + home + " actors suspended");
            int shown = 0, otherNow = 0;
            foreach (var a in otherEnc.Actors) if (a.Unit != null) { otherNow++; if (!HaloAi.SuspendedForBsp(a.Unit)) shown++; }
            r.Check(otherNow > 0 && shown == otherNow, mission + ": " + otherEnc.Name + "'s " + shown + "/" + otherNow + " actors no longer suspended for their bsp");
            yield return Seconds(20f);
            int kept = 0; float worst = 0f;
            for (int i = 0; i < homeActors.Count; i++)
            {
                var u = homeActors[i].Unit;
                if (u == null) continue;
                float dy = Mathf.Abs(u.transform.position.y - feetY[i]);
                if (dy > worst) worst = dy;
                if (dy <= 0.1f) kept++;
            }
            r.Check(kept == homeActors.Count, mission + ": after 20 s suspended " + kept + "/" + homeActors.Count + " kept their feet height (worst " + worst.ToString("0.00") + " m)");

            // switch back: resumed on the floor
            HaloCampaign.SwitchBsp(home);
            yield return r.Wait(() => HaloCampaign.BspIndex == home && !HaloCampaign.SwitchingBsp, 180f, mission + ": switch back to bsp " + home);
            yield return Seconds(3f);
            int back = 0, floor = 0, alive = 0;
            foreach (var a in homeActors)
            {
                if (a.Unit == null) continue;
                alive++;
                if (!HaloAi.SuspendedForBsp(a.Unit)) back++;
                float gap;
                if (OnFloor(a.Unit.transform.position, out gap)) floor++;
                else HaloLabRunner.Log(a.Label + " at " + a.Unit.transform.position.ToString("0.00") + " floor gap " + gap.ToString("0.00") + (HaloAi.SuspendedDormant(a.Unit) ? " (dormant encounter)" : ""));
            }
            r.Check(alive > 0 && back == alive, mission + ": back on bsp " + home + ": " + back + "/" + alive + " resumed");
            r.Check(alive > 0 && floor == alive, mission + ": " + floor + "/" + alive + " stand on their floor (within 0.3 m)");
            Vector3 f1 = homeActors[0].Unit != null ? homeActors[0].Unit.transform.position : f0;
            r.Aim(f1 + new Vector3(4f, 3f, 4f), f1 + Vector3.up, 60f); r.Shot("enc-after");
        }

        static IEnumerator SquadRetreat(HaloLabRunner r, string mission)
        {
            yield return StartMission(r, mission);
            if (!HaloCampaign.Active) yield break;
            const string squadName = "first_wave/wave_1_lz_grunt";
            HaloAiEncounter enc; int squad, platoon;
            if (!r.Check(HaloAi.Resolve(squadName, out enc, out squad, out platoon) && squad >= 0, mission + ": " + squadName + " exists")) yield break;
            int pl = enc.Data.Squads[squad].Platoon;
            int to = enc.Data.Squads[squad].ManeuverToSquad;
            r.Check(to >= 0, mission + ": " + squadName + " maneuvers to " + (to >= 0 ? enc.Data.Squads[to].Name : "nothing"));
            // the whole platoon (or just the squad when it has none)
            for (int s = 0; s < enc.Data.Squads.Count; s++)
                if (s == squad || (pl >= 0 && enc.Data.Squads[s].Platoon == pl)) HaloAi.Place(enc.Name + "/" + enc.Data.Squads[s].Name);
            yield return Seconds(1f);
            var before = new Dictionary<HaloActor, int>();
            foreach (var a in enc.Actors) if (a.Alive) before[a] = a.Squad;
            int inSquad = 0, others = 0;
            foreach (var kv in before) if (kv.Value == squad) inSquad++; else others++;
            HaloLabRunner.Log(mission + ": placed " + before.Count + " actors of platoon " + (pl >= 0 ? enc.Data.Platoons[pl].Name : "-") + ": " + inSquad + " in " + squadName + ", " + others + " in its other squads");
            HaloAi.Retreat(squadName);
            int movedRight = 0, movedWrong = 0, stayed = 0;
            foreach (var kv in before)
            {
                var a = kv.Key;
                string line = a.Label + ": " + enc.Data.Squads[kv.Value].Name + " -> " + (a.Squad >= 0 ? enc.Data.Squads[a.Squad].Name : "-");
                HaloLabRunner.Log(line);
                if (kv.Value == squad) { if (a.Squad == to) movedRight++; else stayed++; }
                else if (a.Squad != kv.Value) movedWrong++;
            }
            r.Check(inSquad > 0 && movedRight == inSquad, mission + ": ai_retreat " + squadName + ": " + movedRight + "/" + inSquad + " grunts moved to " + (to >= 0 ? enc.Data.Squads[to].Name : "-"));
            r.Check(movedWrong == 0, mission + ": " + movedWrong + " actors of the other squads moved (" + others + " there)");

            // dormancy (40 wu, ours) must not freeze an encounter one of whose actors sees the player inside its own vision range (snipers)
            var startPos = new Dictionary<HaloActor, Vector3>();
            foreach (var kv in before) if (kv.Key.Unit != null) startPos[kv.Key] = kv.Key.Unit.transform.position;
            yield return Seconds(2.5f);
            Vector3 head = Feet() + Vector3.up * 1.6f;
            int seeing = 0; float nearest = 999f;
            foreach (var kv in before)
            {
                var a = kv.Key;
                if (a.Unit == null || !a.Alive) continue;
                float range = (a.V != null && a.V.ModifiedVisionRange > 0f ? a.V.ModifiedVisionRange : (a.A != null ? a.A.MaxVisionDistance : 0f)) * HaloSpace.Unit;
                Vector3 eye = a.Unit.Eye; float d = (head - eye).magnitude;
                nearest = Mathf.Min(nearest, d);
                if (d <= range && !Physics.Linecast(eye, head, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore)) seeing++;
            }
            float walked = 0f; foreach (var kv in startPos) if (kv.Key.Unit != null) walked = Mathf.Max(walked, (kv.Key.Unit.transform.position - kv.Value).magnitude);
            HaloLabRunner.Log(mission + ": " + enc.Name + " " + (enc.Active ? "active" : "dormant") + ", nearest actor " + (nearest / HaloSpace.Unit).ToString("0") + " wu from the player, " + seeing + " see the player, furthest walk " + walked.ToString("0.0") + " m");
            if (seeing > 0) r.Check(enc.Active, mission + ": " + enc.Name + " stays active while " + seeing + " of its actors see the player (" + (nearest / HaloSpace.Unit).ToString("0") + " wu)");
            // shot from afar: a hit wakes a dormant encounter (sniping it from beyond 40 wu)
            HaloActor victim = null; foreach (var kv in before) if (kv.Key.Unit != null && kv.Key.Alive) { victim = kv.Key; break; }
            if (victim != null)
            {
                victim.Unit.LastHitTime = Time.time;
                yield return Seconds(1.5f);
                r.Check(enc.Active && !HaloAi.SuspendedDormant(victim.Unit), mission + ": " + enc.Name + " active after " + victim.Label + " was hit (" + (nearest / HaloSpace.Unit).ToString("0") + " wu away)");
            }
        }

        // ------------------------------------------------------------------ ai-enc-carrier

        static IEnumerator Carrier(HaloLabRunner r)
        {
            EnsurePlayer();
            yield return r.Wait(() => HaloGame.Current != null && HaloGame.Current.Install != null, 120f, "the install");
            if (HaloGame.Current == null || HaloGame.Current.Install == null) { r.Fail("no install"); yield break; }
            string mission = HaloLabBoot.Arg("-halomission", "d20");
            yield return StartMission(r, mission);
            if (!HaloCampaign.Active) yield break;
            HaloTag carrier = null;
            foreach (var t in HaloAi.Data.ActorPalette) if (t != null && t.Name.Contains("floodcarrier")) { carrier = t; break; }
            if (!r.Check(carrier != null, mission + ": a flood carrier in the actor palette (" + (carrier != null ? carrier.Name : "none") + ")")) yield break;
            HaloAiEncounter flood = null;
            // a flood encounter of the loaded BSP (one of another BSP would keep the carrier and its forms suspended and hidden). In Halo every
            // actor belongs to an encounter (its members share what they see); a free actor would not, so free actors are the last resort.
            // Flood encounters mostly have team "default" (None): a squad whose actor is a flood one marks them. Its own carrier squad first.
            int floodSquad = -1;
            var pal = HaloAi.Data.ActorPalette;
            for (int pass = 0; pass < 2 && flood == null; pass++)
                foreach (var e in HaloAi.Encounters)
                {
                    if (e.Squads.Length == 0 || !(e.Data.Bsp < 0 || e.Data.Bsp == HaloAi.ActiveBsp)) continue;
                    if (e.Team != HaloTeam.Flood && e.Team != HaloTeam.None) continue;
                    for (int s = 0; s < e.Data.Squads.Count && s < e.Squads.Length; s++)
                    {
                        int at0 = e.Data.Squads[s].ActorType;
                        string an = at0 >= 0 && at0 < pal.Count && pal[at0] != null ? pal[at0].Name : "";
                        if (pass == 0 ? an.Contains("floodcarrier") : an.Contains("flood")) { flood = e; floodSquad = s; break; }
                    }
                    if (flood != null) break;
                }
            HaloLabRunner.Log(mission + ": active bsp " + HaloAi.ActiveBsp + ", flood encounter " + (flood != null ? flood.Name + "/" + flood.Data.Squads[floodSquad].Name + " (bsp " + flood.Data.Bsp + ", team " + flood.Team + ", squad released " + flood.SquadReleased(floodSquad) + ")" : "none (free actors)"));
            if (flood != null)
            {
                // Halo keeps an encounter's actors inside their squad's area (its firing positions); a swarm released outside that area runs
                // back into it (run 6: all forms ended 20 m off at their area). The meeting has to happen where the game puts it: the player
                // is moved onto the squad's firing position nearest the centre of its area.
                var sqd = flood.Data.Squads[floodSquad];
                uint mask = sqd.Attacking | sqd.AttackingSearch | sqd.AttackingGuard | sqd.Defending | sqd.DefendingSearch | sqd.DefendingGuard | sqd.Pursuing;
                Vector3 sum = Vector3.zero; int cnt = 0;
                for (int i = 0; i < flood.Positions.Length; i++) if (mask == 0 || flood.PositionInGroups(i, mask)) { sum += flood.Positions[i]; cnt++; }
                if (cnt > 0)
                {
                    Vector3 centre = sum / cnt, best = Vector3.zero; float bd = float.MaxValue;
                    for (int i = 0; i < flood.Positions.Length; i++)
                    {
                        if (mask != 0 && !flood.PositionInGroups(i, mask)) continue;
                        float d = (flood.Positions[i] - centre).sqrMagnitude;
                        if (d < bd) { bd = d; best = flood.Positions[i]; }
                    }
                    string how;
                    HaloPlayer.Teleport(Ground(HaloActor.OnNav(best)), 0f, out how);
                    Physics.SyncTransforms();
                    yield return Seconds(1f);
                    // the lab has no combat body (HaloHandling's body is not valid in batch mode), so nothing feeds HaloLocal.Position from the
                    // moved controller: the AI would keep seeing the player where it spawned (round 3 run 1: 50 m away). Feed it once here.
                    if (HaloCombat.LocalChest == Vector3.zero) { Vector3 nf = Feet(); if (nf != Vector3.zero) HaloLocal.Position = nf; }
                    yield return Seconds(0.2f);
                    HaloLabRunner.Log(mission + ": the player moved into " + flood.Name + "/" + sqd.Name + "'s area (" + cnt + " firing positions, centre " + centre.ToString("0.0") + ") at " + Feet().ToString("0.0") + " (" + how + ")");
                }
            }
            Vector3 feet = Feet();
            float yaw; string src; HaloPlayer.TryFacingYaw(out yaw, out src);
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            // the burst spot: about 6 m away on the navigation, level with the player and in plain sight of them (the forms must be able to
            // see the player; a spot behind a wall tests nothing)
            Vector3 at = Ground(HaloActor.OnNav(feet + fwd * 6f));
            bool seen = false;
            for (int k = 0; k < 12 && !seen; k++)
            {
                Vector3 dir = Quaternion.Euler(0f, yaw + k * 30f, 0f) * Vector3.forward;
                Vector3 c = Ground(HaloActor.OnNav(feet + dir * 6f));
                if (Mathf.Abs(c.y - feet.y) > 1f || (c - feet).magnitude < 3f) continue;
                if (Physics.Linecast(feet + Vector3.up * 1.6f, c + Vector3.up * 0.3f, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore)) continue;
                at = c; fwd = dir; yaw += k * 30f; seen = true;
            }
            r.Check(seen, mission + ": a burst spot in sight of the player " + (at - feet).magnitude.ToString("0.0") + " m away");
            // the target: the campaign's own local player (the lab's character controller, fed into HaloLocal by Combat), so the infection
            // forms' contact goes through the real player path (ImpactContact -> TryLatch(null): pop on the cyborg's shield, else strike
            // HaloCombat.TakeHit). A blue capsule marks it in the pictures (no collider). Only when the AI has no local player body at the feet
            // does a stand-in body replace it (its contact path is the generic melee blow, not the player's).
            var standIn = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            standIn.name = "player marker";
            standIn.transform.position = feet + Vector3.up * 0.9f;
            standIn.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            Object.DestroyImmediate(standIn.GetComponent<Collider>());
            var sm = new Material(Shader.Find("Universal Render Pipeline/Lit")); sm.SetColor("_BaseColor", new Color(0.1f, 0.6f, 1f));
            standIn.GetComponent<Renderer>().sharedMaterial = sm;
            var sf = new GameObject("stand-in feet"); sf.transform.position = feet;
            HaloAiBodies.StandInPlayer = null;
            yield return Seconds(0.5f);
            var pb = HaloAiBodies.LocalPlayer;
            bool realPlayer = pb != null && pb.Kind == HaloAiBody.Kinds.LocalPlayer && Flat(pb.Feet - feet) < 1f && Mathf.Abs(pb.Head.y - feet.y - 1.6f) < 0.8f;
            HaloLabRunner.Log(mission + ": AI player body " + (pb != null ? pb.Kind + " feet " + pb.Feet.ToString("0.00") + " head " + pb.Head.ToString("0.00") + " alive " + pb.Alive : "none") +
                ", lab feet " + feet.ToString("0.00") + ", HaloLocal.Position " + HaloLocal.Position.ToString("0.00") + ", chest " + HaloCombat.LocalChest.ToString("0.00") +
                ", shield " + HaloLocal.Shield.ToString("0.00") + ", health " + HaloLocal.Health.ToString("0.00"));
            if (!realPlayer)
            {
                HaloAiBodies.StandInPlayer = sf.transform;
                HaloAiBodies.StandInTeam = HaloTeam.Player;
                HaloLabRunner.Log(mission + ": no usable local player body for the AI - a stand-in body replaces it (its contact is the generic melee blow)");
            }
            r.Check(realPlayer, mission + ": the AI targets the campaign's own local player (not a stand-in)");
            // two bursts: 1) the carrier never saw the player (killed unaware): the forms must sense the player themselves (actr guard
            // perception time, 12 wu vision); 2) the carrier knows the player (as when it walks at them): the forms inherit its target
            for (int round = 0; round < 2; round++)
            {
                bool aware = round == 1;
                string tag = aware ? " [aware carrier]" : " [unaware carrier]";
                string px = aware ? "aware-" : "";
                if (round > 0)
                {
                    // the first swarm is gone and the shield is back before the second burst
                    float tw = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - tw < 14f && HaloLocal.Shield < 0.99f) yield return null;
                    HaloLabRunner.Log(mission + ": before the second burst the player shield is " + HaloLocal.Shield.ToString("0.00") + ", health " + HaloLocal.Health.ToString("0.00") + ", alive " + HaloLocal.Alive);
                }
                var a = HaloAi.Spawn(carrier, at, yaw + 180f, flood, flood != null ? floodSquad : -1);
                if (!r.Check(a != null && a.Unit != null, mission + ": carrier spawned at " + at.ToString("0.0") + (flood != null ? " in " + flood.Name : " (free)") + tag)) break;
                a.Braindead = true;
                var def = a.Unit.Def;
                if (round == 0) HaloLabRunner.Log(mission + ": carrier spawned actor " + (def.SpawnedActor != null ? def.SpawnedActor.Name : "-") + " x" + def.SpawnedCountMin + ".." + def.SpawnedCountMax +
                    " at " + def.SpawnedVelocity + " wu/s, destroyed after dying " + def.DestroyedAfterDying + ", ai danger radius " + def.AiDangerRadius);
                yield return Seconds(1f);
                r.Check(a.Unit != null && !a.Unit.Suspended, mission + ": the carrier is simulated (not suspended) before it dies" + tag);
                r.Aim(feet + Vector3.up * 1.7f, at + Vector3.up * 0.8f, 65f); r.Shot(px + "carrier-alive");
                int living0 = flood != null ? flood.Living(-1, -1) : 0;
                int released0 = HaloAi.Released;
                if (aware)
                {
                    // in the game a carrier bursts next to the player it walks at: it knows its target (the forms inherit it)
                    a.Braindead = false; a.MagicallySee(HaloAiBodies.LocalPlayer);
                    yield return Seconds(0.3f);
                }
                HaloLabRunner.Log(mission + ": the carrier targets " + (a.Target != null ? a.Target.ToString() : "nothing") + " before it dies" + tag);
                if (aware) r.Check(a.Target != null && a.Target.IsPlayer, mission + ": the aware carrier targets the player before it dies");
                a.Unit.Kill();
                float t0 = Time.realtimeSinceStartup;
                int pic = 0;
                while (HaloAi.Released == released0 && Time.realtimeSinceStartup - t0 < 6f)
                {
                    yield return null;
                    if (round == 0 && pic < 2 && Time.realtimeSinceStartup - t0 > pic * 0.2f) r.Shot("carrier-dying" + (pic++));
                }
                int n = HaloAi.Released - released0;
                r.Check(n >= 3 && n <= 6, mission + ": the carrier burst after " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s and released " + n + " infection forms (3..6 at normal)" + tag);
                float tBurst = Time.realtimeSinceStartup;
                if (round == 0) r.Shot("carrier-burst");
                var forms = new List<HaloActor>();
                for (int i = HaloAi.Actors.Count - 1; i >= 0 && forms.Count < n; i--) { var f = HaloAi.Actors[i]; if (f.Unit != null && f.V != null && f.V.Tag != null && f.V.Tag.Name.Contains("infection")) forms.Add(f); }
                if (flood != null) r.Check(flood.Living(-1, -1) >= living0 - 1 + n, mission + ": " + flood.Name + " counts them (living " + living0 + " -> " + flood.Living(-1, -1) + ")");

                // every frame from the burst for 9 s: per form when it took the player as its target, how close it got, where it went (a
                // form gone next to the player popped on its shield / hit it; one gone away from it was killed by something else - e.g. a
                // swarm-mate's blow - and is a failure); the floor check at 3 s
                float shield0 = HaloLocal.Shield, health0 = HaloLocal.Health, shieldMin = shield0, healthMin = health0;
                int melees0 = HaloAi.Melees;
                int m = forms.Count;
                var minD = new float[m]; var lastPos = new Vector3[m]; var acquiredAt = new float[m]; var goneD = new float[m]; var goneAt = new float[m]; var sight = new bool[m];
                for (int i = 0; i < m; i++) { minD[i] = 999f; goneD[i] = -1f; acquiredAt[i] = -1f; lastPos[i] = forms[i].Unit != null ? forms[i].Unit.transform.position : at; }
                bool floorDone = false, airShot = false; int apic = 0, diag = 0;
                var flying = new bool[m]; int leaps = 0; float apexMax = 0f;
                var leapEnds0 = (int[])HaloActor.LeapEnds.Clone();
                while (Time.realtimeSinceStartup - tBurst < 9f)
                {
                    yield return null;
                    float t = Time.realtimeSinceStartup - tBurst;
                    Vector3 pf = realPlayer && HaloLocal.Position != Vector3.zero ? HaloLocal.Position : feet;
                    shieldMin = Mathf.Min(shieldMin, HaloLocal.Shield); healthMin = Mathf.Min(healthMin, HaloLocal.Health);
                    for (int i = 0; i < m; i++)
                    {
                        var f = forms[i];
                        if (goneD[i] >= 0f) continue;
                        if (f.Unit == null || !f.Alive) { goneD[i] = Flat(lastPos[i] - pf); goneAt[i] = t; continue; }
                        lastPos[i] = f.Unit.transform.position;
                        if (acquiredAt[i] < 0f && f.Target != null && f.Target.IsPlayer) acquiredAt[i] = t;
                        minD[i] = Mathf.Min(minD[i], Flat(lastPos[i] - pf));
                        if (f.LeapInFlight) { if (!flying[i]) { flying[i] = true; leaps++; } }
                        else if (flying[i]) { flying[i] = false; apexMax = Mathf.Max(apexMax, f.LastLeapApex); }
                        // a clear line from the form to the player's head at some moment (sampled every 6th frame): a form that never had one cannot see it
                        if (!sight[i] && (Time.frameCount % 6) == 0 && (!Physics.Linecast(f.Unit.Eye, pf + Vector3.up * 1.6f, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore) || !Physics.Linecast(f.Unit.Eye, pf + Vector3.up * 1.0f, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore))) sight[i] = true;
                    }
                    if (round == 0 && !airShot && t > 0.4f) { airShot = true; r.Shot("carrier-airborne"); }
                    if (t > 2f + 2f * diag && diag < 4)
                    {
                        diag++;
                        for (int i = 0; i < m; i++)
                        {
                            var f = forms[i];
                            if (goneD[i] >= 0f || f.Unit == null) continue;
                            HaloLabRunner.Log("  t=" + t.ToString("0.0") + " " + f.Label + " " + Flat(f.Unit.transform.position - pf).ToString("0.0") + " m (dy " + (f.Unit.transform.position.y - pf.y).ToString("0.0") + "), moving " + f.Moving + ", charging " + f.MeleeCharging + ", leaping " + f.LeapInFlight +
                                ", grounded " + f.Unit.Grounded + ": " + f.PerceptionDebug());
                        }
                    }
                    if (!floorDone && t > 3f)
                    {
                        floorDone = true;
                        int floor = 0, alive = 0;
                        for (int i = 0; i < m; i++)
                        {
                            var f = forms[i];
                            if (f.Unit == null || !f.Alive) continue;
                            alive++;
                            float gap;
                            bool on = OnFloor(f.Unit.transform.position, out gap) || f.Unit.Latched;
                            if (on) floor++;
                            HaloLabRunner.Log(f.Label + " at " + f.Unit.transform.position.ToString("0.00") + " (" + (f.Unit.transform.position - at).magnitude.ToString("0.0") + " m from the burst), floor gap " + gap.ToString("0.00") + ", state " + f.State + ", target " + (f.Target != null ? f.Target.ToString() : "-") + (f.Unit.Grounded ? "" : " (airborne)"));
                        }
                        r.Check(floor == alive, mission + ": " + floor + "/" + alive + " living infection forms on the floor 3 s after the burst" + tag);
                    }
                    if (apic < 3 && t > 1.0f + apic * 1.5f) { r.Aim(feet + Vector3.up * 2.4f - fwd * 2.5f, feet + fwd * 2.5f, 80f); r.Shot(px + "carrier-swarm" + (apic++)); }
                }
                // who must have acquired: aware - every form (it inherits the carrier's target at birth); unaware - every form that had a
                // clear line to the player and lived at least 4 s after the burst (landing ~1 s + guard perception 1.4 s at half rate = 3.8 s)
                int fighting = 0, required = 0, requiredOk = 0, contact = 0, awayGone = 0, stalled = 0; float closest = 999f, slowest = 0f;
                for (int i = 0; i < m; i++)
                {
                    var f = forms[i];
                    bool gone = goneD[i] >= 0f;
                    // still alive at the end within 2.5 m of a shielded player: it reached the player and never struck (a form at contact pops on the
                    // cyborg's shield or strikes at once in Halo)
                    if (!gone && f.Unit != null && f.Alive && minD[i] < 2.5f && Flat(f.Unit.transform.position - (realPlayer && HaloLocal.Position != Vector3.zero ? HaloLocal.Position : feet)) < 2.5f) stalled++;
                    bool must = aware || (sight[i] && (!gone || goneAt[i] > 4f));
                    if (acquiredAt[i] >= 0f) { fighting++; slowest = Mathf.Max(slowest, acquiredAt[i]); }
                    if (must) { required++; if (acquiredAt[i] >= 0f) requiredOk++; }
                    closest = Mathf.Min(closest, minD[i]);
                    if (minD[i] < 2.5f || (gone && goneD[i] < 4.5f)) contact++;
                    // gone away from the player: fine only as a chain pop (another form went within 0.3 s and 2.5 m: the 'infection form pop'
                    // damage, radius 0.75 wu = 2.3 m); else something else killed it (a swarm-mate's melee blow)
                    bool chain = false;
                    if (gone && goneD[i] >= 4.5f)
                        for (int j = 0; j < m; j++)
                            if (j != i && goneD[j] >= 0f && Mathf.Abs(goneAt[j] - goneAt[i]) < 0.3f && (lastPos[j] - lastPos[i]).magnitude < 2.5f) chain = true;
                    if (gone && goneD[i] >= 4.5f && !chain) awayGone++;
                    HaloLabRunner.Log(f.Label + tag + ": took the player as target " + (acquiredAt[i] >= 0f ? acquiredAt[i].ToString("0.00") + " s after the burst" : "never") + (f.LastAcquireDelay > 0f ? " (sight delay " + f.LastAcquireDelay.ToString("0.0") + " s)" : "") +
                        ", sight line " + sight[i] + ", closest " + minD[i].ToString("0.0") + " m, " + (gone ? "gone " + goneAt[i].ToString("0.0") + " s after the burst " + goneD[i].ToString("0.0") + " m from the player" + (chain ? " (chain pop)" : "") : "alive, state " + f.State + ", target " + (f.Target != null ? f.Target.ToString() : "-") +
                        ", latched " + (f.Unit != null && f.Unit.Latched) + ", suspended " + (f.Unit != null && f.Unit.Suspended) + (f.Unit != null && HaloAi.SuspendedDormant(f.Unit) ? " (dormant)" : "") + (f.Unit != null ? " at " + f.Unit.transform.position.ToString("0.00") : "")));
                }
                for (int i = 0; i < m; i++) if (flying[i] && forms[i] != null) apexMax = Mathf.Max(apexMax, forms[i].LastLeapApex);
                int strikes = HaloAi.Melees - melees0;
                HaloLabRunner.Log(mission + tag + ": " + leaps + " infection-form leaps, highest " + apexMax.ToString("0.00") + " m above the take-off; " + strikes + " melee contacts (pops / strikes / latches)");
                bool chestKnown = HaloCombat.LocalChest != Vector3.zero;
                HaloLabRunner.Log(mission + tag + ": player shield " + shield0.ToString("0.00") + " -> min " + shieldMin.ToString("0.00") + ", health " + health0.ToString("0.00") + " -> min " + healthMin.ToString("0.00") + ", AI melee contacts +" + (HaloAi.Melees - melees0) + (realPlayer ? "" : " (stand-in: no vitals)") +
                    (chestKnown ? "" : " (the lab's player has no combat body: HaloCombat.LocalChest is zero, so blows / pop damage cannot reach its vitals - shield pops still happen)"));
                float limit = aware ? 1.0f : 5f;
                // (required 0: no form both saw the player and lived 4 s - fine only when every form reached it, i.e. they attacked first;
                // round 3 run 2: all 5 popped on the shield 1.3..2.7 s after the burst)
                r.Check((required > 0 ? requiredOk == required : contact == m && m > 0) && slowest <= limit, mission + ": " + requiredOk + "/" + required + " infection forms that could see the player took it as their target (" + fighting + "/" + m + " in all), the last " + slowest.ToString("0.0") + " s after the burst (limit " + limit.ToString("0.0") + " s)" + tag);
                // a swarm: at least half the forms get to the player (run 6 passed a weaker "one form" rule with 3 of 4 running off)
                r.Check(contact * 2 >= m && contact > 0, mission + ": " + contact + "/" + m + " infection forms reached the player (at least half; closest " + closest.ToString("0.0") + " m, or gone within 4.5 m of it)" + tag);
                r.Check(awayGone == 0, mission + ": no infection form was killed away from the player except by a chain pop (" + awayGone + " were)" + tag);
                r.Check(stalled == 0, mission + ": no infection form stands next to the player without attacking at the end (" + stalled + " do)" + tag);
                // the forms that reach the player attack it (Halo: pop on the raised shield, else strike) - the user saw them sit next to the player
                if (contact > 0) r.Check(strikes > 0, mission + ": the forms that reached the player attacked it: " + strikes + " melee contacts" + tag);
                // an infection form's leap is an attack that reaches its target: actr flood_infection leap 1..3.5 wu at 0.08 wu/tick (7.3 m/s,
                // 0.4 .. 1.5 s), ballistic 0 = a flat, straight leap at the target's body (no arc: the biped has no leap clips at all, so its
                // height is not a test - a straight line from body centre to body centre rises only by their height difference). The lab's
                // player stands still, so every leap that ends must have touched it, or have been stopped by a wall (a face too steep to stand
                // on: Halo's leaping forms stop on contact); a leap that comes down on open floor before its planned flight time ("Short",
                // round 2/3: 0.17 s hops) or flies its whole planned time past a standing target ("Flew") is a failed leap.
                // (a form killed in the air by a swarm-mate's pop before it touched anything is "unfinished": not a leap result)
                {
                    int lHit = HaloActor.LeapEnds[(int)HaloActor.LeapEnd.Hit] - leapEnds0[(int)HaloActor.LeapEnd.Hit];
                    int lFlew = HaloActor.LeapEnds[(int)HaloActor.LeapEnd.Flew] - leapEnds0[(int)HaloActor.LeapEnd.Flew];
                    int lWall = HaloActor.LeapEnds[(int)HaloActor.LeapEnd.Wall] - leapEnds0[(int)HaloActor.LeapEnd.Wall];
                    int lShort = HaloActor.LeapEnds[(int)HaloActor.LeapEnd.Short] - leapEnds0[(int)HaloActor.LeapEnd.Short];
                    int lAll = HaloActor.LeapEnds[0] - leapEnds0[0];
                    HaloLabRunner.Log(mission + tag + ": leaps launched " + lAll + ": hit " + lHit + ", flew the planned time without contact " + lFlew + ", stopped by a wall " + lWall + ", short " + lShort + ", unfinished (killed in the air / still flying) " + (lAll - lHit - lFlew - lWall - lShort));
                    if (lAll > 0) r.Check(lShort == 0 && lFlew == 0 && (lHit > 0 || lWall + lShort + lFlew == 0), mission + ": infection-form leaps reach the standing player (" + lHit + " hit, " + lWall + " stopped by a wall, " + lShort + " short, " + lFlew + " flew past)" + tag);
                }
                if (realPlayer && chestKnown) r.Check(shieldMin < shield0 - 0.01f || healthMin < health0 - 0.01f, mission + ": the infection forms hurt the player (shield " + shield0.ToString("0.00") + " -> " + shieldMin.ToString("0.00") + ", health " + health0.ToString("0.00") + " -> " + healthMin.ToString("0.00") + ")" + tag);
                r.Aim(feet + Vector3.up * 1.7f + fwd * 4f, feet + Vector3.up * 0.5f, 75f); r.Shot(px + "carrier-attack");
                // what is left of this swarm goes before the next burst
                foreach (var f in forms) if (f.Unit != null && f.Alive) f.Unit.Kill();
            }
            HaloAi.LogBudget();
            HaloAiBodies.StandInPlayer = null;
            Object.Destroy(standIn); Object.Destroy(sf);
            HaloCampaign.LeaveMission();
        }

        // ------------------------------------------------------------------ ai-enc-area (squads keep to their firing positions)

        sealed class AreaStandIn : MonoBehaviour, IHaloDamageable { public int Hits; public void TakeHit(HaloHit hit) { Hits++; } }

        static uint SquadUnion(HaloAiScenario.Squad sq) => sq.Attacking | sq.AttackingSearch | sq.AttackingGuard | sq.Defending | sq.DefendingSearch | sq.DefendingGuard | sq.Pursuing;

        /// <summary>A camera spot with a clear view of a point: the stand-in player's eye, else 6 m up and 12 m to a side.</summary>
        static Vector3 ViewEye(Vector3 at, Vector3 player)
        {
            Vector3 look = at + Vector3.up;
            Vector3 eye = player + Vector3.up * 1.7f;
            if (!Physics.Linecast(eye, look, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore)) return eye;
            for (int k = 0; k < 8; k++)
            {
                Vector3 e = at + Vector3.up * 6f + Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * 12f;
                if (!Physics.Linecast(e, look, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore)) return e;
            }
            return at + Vector3.up * 8f + Vector3.back * 6f;
        }

        /// <summary>
        /// ai-enc-area [-halomap a30] [-haloenc lz_search] [-haloseconds 60]: the encounter placed against a stand-in player (as ai-combat-area),
        /// with two setup corrections: (1) a squad whose starting locations stand on a navigation island where none of its firing positions
        /// lie is a dropship-loaded squad (a30 lz_search/cship_toon: ai_place then vehicle_load_magic lz_cship; its starting locations are on
        /// the canyon rim 12+ wu above every position) - it is put down on the positions' island at the nearest point (vehicle_unload);
        /// (2) Halo walks an actor from its starting location onto its squad's positions, so an actor that has not yet been inside its squad's
        /// area may be outside while it closes in (at least 0.5 wu per 2 s sample; 3 samples in a row without progress = stalled).
        /// Checks: no actor ever more than 2 wu outside the area after having been in it; none stalled outside.
        /// </summary>
        static IEnumerator Area(HaloLabRunner r)
        {
            yield return r.Wait(() => HaloGame.Current != null && HaloGame.Current.Install != null, 120f, "the install");
            if (HaloGame.Current == null || HaloGame.Current.Install == null) { r.Fail("no install"); yield break; }
            string mapName = HaloLabBoot.Arg("-halomap", "a30");
            string encName = HaloLabBoot.Arg("-haloenc", "lz_search");
            float seconds = float.Parse(HaloLabBoot.Arg("-haloseconds", "60"), System.Globalization.CultureInfo.InvariantCulture);
            var entry = r.Game.Install.Map(mapName);
            if (entry == null) foreach (var e in r.Game.Install.Campaign) if (e.Name == mapName) entry = e;
            if (!r.Check(entry != null, "map " + mapName)) yield break;
            var map = new HaloMap(entry.File);
            var sc = new HaloScenario(map);
            var data = new HaloAiScenario(sc);
            var enc = data.FindEncounter(encName);
            if (!r.Check(enc != null, "encounter " + encName)) yield break;
            int bsp = Mathf.Clamp(enc.Bsp, 0, sc.Bsps.Count - 1);

            // stage (as HaloLabAiCombat.Stage)
            Time.captureFramerate = 60;
            HaloCombat.LabMap = map; HaloCombat.LabScenario = sc;
            HaloAi.LabMap = map; HaloAi.LabScenario = sc; HaloAi.LabBsp = bsp; HaloAi.Verbose = true;
            HaloAi.Difficulty = (HaloCampaign.Difficulty)int.Parse(HaloLabBoot.Arg("-halodiff", "1"));
            try { HaloFx.Preload(map); } catch (System.Exception ex) { HaloLabRunner.Log("fx preload: " + ex.Message); }
            var hub = GameObject.Find("Halo Hub");
            if (hub != null) hub.SetActive(false);
            var stage = new GameObject("ai area lab");
            HaloBipeds.LabParent = stage.transform;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.0f; sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            HaloWorld.PlaceObjects = false;
            var root = new GameObject("Lab World Root");
            root.transform.position = HaloSpace.MapOrigin;
            var it = HaloWorld.Build(map, sc, root.transform, null, bsp);
            while (it.MoveNext()) yield return it.Current;
            if (!r.Check(HaloWorld.Ready, "bsp " + bsp + " built")) yield break;
            HaloWorldDriver.View = r.Cam;
            Physics.SyncTransforms();
            yield return null;
            HaloAiDriver.Ensure(stage.transform);

            // the player: a firing position 12..35 m from the starting locations' centre that sees the most of them (as ai-combat-area)
            Vector3 centre = Vector3.zero; int ns = 0;
            var starts = new List<Vector3>();
            foreach (var s in enc.Squads) foreach (var l in s.Starts) { var q = HaloSpace.Pos(l.Position); centre += q; ns++; starts.Add(q); }
            if (ns > 0) centre /= ns;
            Vector3 player = HaloActor.OnNav(centre + new Vector3(18f, 0f, 0f)); float best = -1f;
            foreach (var f in enc.FiringPositions)
            {
                Vector3 p = HaloSpace.Pos(f.Position);
                float d = (p - centre).magnitude;
                if (d < 12f || d > 35f) continue;
                int seen = 0;
                foreach (var s in starts) if (!Physics.Linecast(p + Vector3.up * 1.6f, s + Vector3.up * 1.2f, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore)) seen++;
                if (seen + d * 0.01f > best) { best = seen + d * 0.01f; player = p; }
            }
            var standIn = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            standIn.name = "stand-in player";
            standIn.transform.position = player + Vector3.up * 0.9f;
            standIn.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            standIn.layer = HaloLayers.Hitbox;
            var target = standIn.AddComponent<AreaStandIn>();
            var sf = new GameObject("feet"); sf.transform.position = player;
            HaloAiBodies.StandInPlayer = sf.transform;
            HaloAiBodies.StandInTeam = HaloTeam.Player;

            HaloAi.Place(encName);
            yield return null;
            r.Check(HaloAi.LivingCount(encName) > 0, "ai_place " + encName + ": " + HaloAi.LivingCount(encName) + " actors");
            yield return r.Wait(() => HaloAi.Nav != null, 15f, "the navigation");     // an AI tick builds it
            var nav = HaloAi.Nav;
            if (!r.Check(nav != null, "navigation of bsp " + bsp)) yield break;

            // (1) dropship-loaded squads: put down on their positions' island (the nearest point to their starting location)
            int delivered = 0; var dropped = new HashSet<HaloActor>();
            foreach (var a in HaloAi.Actors)
            {
                if (a.Unit == null || !a.Alive || a.Enc == null || a.Free || a.Squad < 0 || a.Squad >= a.Enc.Data.Squads.Count) continue;
                uint mask = SquadUnion(a.Enc.Data.Squads[a.Squad]);
                int myComp = HaloAiEncounter.ComponentAt(a.Position);
                if (myComp < 0) continue;
                bool reach = false; int island = -1; float nd = float.MaxValue;
                for (int i = 0; i < a.Enc.Positions.Length && !reach; i++)
                {
                    if (mask != 0 && !a.Enc.PositionInGroups(i, mask)) continue;
                    int c = a.Enc.PositionComponent(i);
                    if (c < 0 || c == myComp) { reach = true; break; }
                    float d = (a.Enc.Positions[i] - a.Position).sqrMagnitude;
                    if (d < nd) { nd = d; island = c; }
                }
                if (reach || island < 0) continue;
                Vector3 h = HaloSpace.ToHalo(a.Position); HVec3 q;
                if (!nav.NearestInComponent(island, h.x, h.y, h.z, 200f, out q)) { r.Check(false, a.Label + ": no point of island " + island + " to put it down on"); continue; }
                Vector3 to = HaloSpace.Pos(q);
                HaloLabRunner.Log("setup: " + a.Label + " starts on island " + myComp + " where its squad has no firing position (dropship-loaded squad): put down on island " + island + " " + (to - a.Position).magnitude.ToString("0.0") + " m away");
                a.Unit.Teleport(to, a.Unit.transform.eulerAngles.y);
                delivered++; dropped.Add(a);
            }
            HaloLabRunner.Log("setup: " + delivered + " actors put down on their squad's positions' island");

            // (2) every 2 s: inside / walking in / LEFT / STALLED
            var entered = new HashSet<HaloActor>();
            var lastOff = new Dictionary<HaloActor, float>();
            var noProgress = new Dictionary<HaloActor, int>();
            float t = 0f, nextCheck = 2f; int samples = 0, left = 0, stalled = 0, walking = 0;
            bool midShot = false;
            float m = 2f * HaloSpace.Unit;
            while (t < seconds)
            {
                yield return null;
                t += 1f / 60f;
                if (t < nextCheck) continue;
                nextCheck += 2f;
                Vector3 sum = Vector3.zero; int na = 0;
                foreach (var a in HaloAi.Actors)
                {
                    if (a.Unit == null || !a.Alive || a.Enc == null || a.Free || a.Enc.FollowPlayers || a.Squad < 0 || a.Squad >= a.Enc.Data.Squads.Count) continue;
                    var sq = a.Enc.Data.Squads[a.Squad];
                    uint mask = SquadUnion(sq);
                    bool any = false; Vector3 mn = Vector3.zero, mx = Vector3.zero;
                    for (int i = 0; i < a.Enc.Positions.Length; i++)
                    {
                        if (mask != 0 && !a.Enc.PositionInGroups(i, mask)) continue;
                        var p = a.Enc.Positions[i];
                        if (!any) { mn = mx = p; any = true; } else { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
                    }
                    if (!any) continue;
                    samples++;
                    Vector3 qa = a.Position; if (dropped.Count == 0 || dropped.Contains(a)) { sum += qa; na++; }
                    float ox = Mathf.Max(0f, Mathf.Max(mn.x - qa.x, qa.x - mx.x)), oz = Mathf.Max(0f, Mathf.Max(mn.z - qa.z, qa.z - mx.z));
                    float off = Mathf.Sqrt(ox * ox + oz * oz);
                    string tag;
                    if (off <= m) { entered.Add(a); tag = off > 0f ? "at the edge" : "inside"; noProgress[a] = 0; }
                    else if (entered.Contains(a)) { left++; tag = "LEFT its area"; }
                    else
                    {
                        float prev; int np;
                        if (!lastOff.TryGetValue(a, out prev) || prev - off >= 0.5f * HaloSpace.Unit) { np = 0; tag = "walking in"; walking++; }
                        else
                        {
                            noProgress.TryGetValue(a, out np); np++;
                            tag = np >= 3 ? "STALLED outside" : "walking in (no progress " + np + ")";
                            if (np >= 3) stalled++;
                        }
                        noProgress[a] = np;
                    }
                    lastOff[a] = off;
                    HaloLabRunner.Log("  t=" + t.ToString("0") + " " + a.Label + " " + a.State + " squad " + sq.Name + " " + tag + (off > 0f ? " (" + (off / HaloSpace.Unit).ToString("0.0") + " wu out)" : "") + (a.Moving ? " moving" : ""));
                }
                if (!midShot && t >= 20f && na > 0)
                {
                    midShot = true;
                    Vector3 c = sum / na;
                    r.Aim(ViewEye(c, player), c + Vector3.up, 70f); yield return null; r.Shot("ai-enc-area_20s");
                }
            }
            int alive = 0, inArea = 0;
            foreach (var a in HaloAi.Actors) { if (a.Unit == null || !a.Alive || a.Enc == null || a.Free) continue; alive++; if (entered.Contains(a)) inArea++; }
            HaloLabRunner.Log("area: " + samples + " samples, " + walking + " walking-in samples, " + inArea + " of " + alive + " living actors reached their squad's area; player hit " + target.Hits + "x");
            r.Check(samples > 0 && left == 0, "actors more than 2 wu outside their squad's firing-position bounds after having been in it: " + left + " of " + samples + " samples");
            r.Check(stalled == 0, "actors stalled outside their squad's area (3 samples without closing in): " + stalled + " samples");
            Vector3 cc = Vector3.zero; int nc = 0;
            foreach (var a in HaloAi.Actors) if (a.Unit != null && a.Alive && (dropped.Count == 0 || dropped.Contains(a))) { cc += a.Position; nc++; }
            if (nc > 0) { cc /= nc; r.Aim(ViewEye(cc, player), cc + Vector3.up, 70f); yield return null; r.Shot("ai-enc-area_end"); }
            HaloLabRunner.Log("edge holds: steer " + HaloActor.OffNavBlocks + ", stuck give-ups " + HaloActor.StuckGiveUps + ", mover recoveries " + HaloActor.Recoveries);
            HaloAi.LogBudget();
            HaloAi.Clear();
            HaloAiBodies.StandInPlayer = null;
            Object.Destroy(standIn); Object.Destroy(sf);
        }
    }
}

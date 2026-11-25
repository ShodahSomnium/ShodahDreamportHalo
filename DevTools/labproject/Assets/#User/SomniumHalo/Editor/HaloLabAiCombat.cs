using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Workstream 6 lab (actor combat, melee, positions, Flood).
    /// - ai-combat [-halomap d20] [-haloseconds 30]: a Flood encounter's BSP of the real map, a stand-in player at one of its firing
    ///   positions, and 1 floodcombat_human, 1 floodcombat elite, 1 carrier and 4 infection forms placed 8 wu around it (free actors, Flood
    ///   team, magically seeing the player). Checks: every leap hits the stand-in or a wall (none comes down short); no melee blow on the stand-in from a Flood body farther than 2 m;
    ///   the carrier goes off within 3 m and releases 3..6 infection forms (HaloAi.Released, with workstreams 2 and 7); no body sunk under a
    ///   floor. Pictures: mid-leap, the carrier burst.
    /// - ai-combat-area [-halomap a30] [-haloenc lz_search] [-haloseconds 40]: the encounter placed against a stand-in player; actors whose
    ///   starting island holds none of their squad's positions (dropship-loaded squads) are put down on the nearest such island; every 2 s
    ///   each confined actor's squad-area membership is logged: once inside (&lt;= 2 wu) it must never be more than 2 wu outside again, and an
    ///   actor walking in must close in by &gt;= 0.5 wu a sample (3 samples without progress = stalled);
    ///   grunt grenades: landing distance from the aim point (&lt; 2 m); burst error: mean AiErrorScale moving vs standing (logged).
    /// </summary>
    [InitializeOnLoadAttribute]
    static class HaloLabAiCombat
    {
        static HaloLabAiCombat()
        {
            HaloLabRunner.Register("ai-combat", r => Flood(r));
            HaloLabRunner.Register("ai-combat-area", r => Area(r));
        }

        sealed class StandIn : MonoBehaviour, IHaloDamageable
        {
            public float Taken; public int Hits, MeleeHits; public float WorstMeleeDist;
            public void TakeHit(HaloHit hit)
            {
                Taken += hit.Amount; Hits++;
                if (!hit.Melee) return;
                MeleeHits++;
                // the blow came from the nearest living Flood body
                float best = 999f;
                Vector3 me = transform.position; me.y -= 0.9f;
                foreach (var a in HaloAi.Actors)
                {
                    if (a.Unit == null || !a.Unit.Alive) continue;
                    Vector3 d = a.Unit.transform.position - me; d.y = 0f;
                    best = Mathf.Min(best, d.magnitude);
                }
                WorstMeleeDist = Mathf.Max(WorstMeleeDist, best);
                HaloLabRunner.Log("stand-in player: melee blow " + hit.Amount.ToString("0.0") + " by " + (hit.Cause != null ? hit.Cause.Name : "?") + ", nearest Flood body " + best.ToString("0.00") + " m");
            }
        }

        static HaloInstall.MapEntry Entry(HaloLabRunner r, string name)
        {
            var entry = r.Game.Install.Map(name);
            if (entry == null) foreach (var e in r.Game.Install.Campaign) if (e.Name == name) entry = e;
            return entry;
        }

        static IEnumerator Stage(HaloLabRunner r, HaloMap map, HaloScenario sc, int bsp, string label)
        {
            Time.captureFramerate = 60;
            HaloCombat.LabMap = map; HaloCombat.LabScenario = sc;
            HaloAi.LabMap = map; HaloAi.LabScenario = sc; HaloAi.LabBsp = bsp; HaloAi.Verbose = true;
            HaloAi.Difficulty = (HaloCampaign.Difficulty)int.Parse(HaloLabBoot.Arg("-halodiff", "1"));
            try { HaloFx.Preload(map); } catch (System.Exception e) { HaloLabRunner.Log("fx preload: " + e.Message); }
            var hub = GameObject.Find("Halo Hub");
            if (hub != null) hub.SetActive(false);
            var stage = new GameObject(label);
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
            r.Check(HaloWorld.Ready, "bsp " + bsp + " built");
            HaloWorldDriver.View = r.Cam;
            Physics.SyncTransforms();
            yield return null;
            HaloAiDriver.Ensure(stage.transform);
        }

        static StandIn MakeStandIn(Vector3 feet)
        {
            var standIn = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            standIn.name = "stand-in player";
            standIn.transform.position = feet + Vector3.up * 0.9f;
            standIn.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            standIn.layer = HaloLayers.Hitbox;
            var sm = new Material(Shader.Find("Universal Render Pipeline/Lit")); sm.SetColor("_BaseColor", new Color(0.1f, 0.6f, 1f));
            standIn.GetComponent<Renderer>().sharedMaterial = sm;
            var f = new GameObject("feet"); f.transform.position = feet;
            HaloAiBodies.StandInPlayer = f.transform;
            HaloAiBodies.StandInTeam = HaloTeam.Player;
            return standIn.AddComponent<StandIn>();
        }

        /// <summary>The real walkable floor under a point (within 2 m above .. 4 m below it); false over a pit / off the BSP.</summary>
        static bool Floor(Vector3 p, out Vector3 floor)
        {
            RaycastHit h;
            floor = p;
            if (!Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out h, 6f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore) || h.normal.y < 0.7f) return false;
            floor = h.point;
            return true;
        }

        /// <summary>
        /// A ring point stands on real floor (HaloActor.OnNav returns the input unchanged when it finds no navigation, which put the first
        /// runs' Flood in mid-air over the d20 pit, 10 m below the stand-in, out of its sight), at the ring distance (+-2 m), level with
        /// the centre (+-1.5 m), in sight of it, and joined to it by floor all along the straight line (no pit or drop in between).
        /// <paramref name="q"/> is replaced by the floor point.
        /// </summary>
        static bool RingOk(Vector3 centre, ref Vector3 q, float ring)
        {
            Vector3 f;
            if (!Floor(q, out f)) return false;
            q = f;
            Vector3 d = q - centre; float dy = d.y; d.y = 0f;
            if (Mathf.Abs(dy) >= 1.5f || Mathf.Abs(d.magnitude - ring) >= 2f) return false;
            if (Physics.Linecast(centre + Vector3.up * 1.2f, q + Vector3.up * 1.2f, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore)) return false;
            for (int i = 1; i < 8; i++)
            {
                Vector3 m = Vector3.Lerp(centre, q, i / 8f), g;
                if (!Floor(m, out g) || Mathf.Abs(g.y - m.y) > 0.8f) return false;
            }
            return true;
        }

        /// <summary>The body stands sunk in a floor: grounded, and a walkable surface lies more than 0.3 m above its feet.</summary>
        static bool Sunk(HaloUnit u)
        {
            if (u == null || !u.Grounded) return false;
            Vector3 p = u.transform.position;
            RaycastHit h;
            return Physics.Raycast(p + Vector3.up * 2f, Vector3.down, out h, 2.5f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore) && h.point.y > p.y + 0.3f && h.normal.y > 0.7f;
        }

        /// <summary>The union of a squad's position groups (attacking / defending / pursuing, all modes).</summary>
        static uint SquadMask(HaloAiScenario.Squad sq) => sq.Attacking | sq.AttackingSearch | sq.AttackingGuard | sq.Defending | sq.DefendingSearch | sq.DefendingGuard | sq.Pursuing;

        // ------------------------------------------------------------------ ai-combat (the Flood)

        static IEnumerator Flood(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "d20");
            float seconds = float.Parse(HaloLabBoot.Arg("-haloseconds", "30"), System.Globalization.CultureInfo.InvariantCulture);
            var entry = Entry(r, mapName);
            if (!r.Check(entry != null, "map " + mapName)) yield break;
            var map = new HaloMap(entry.File);
            var sc = new HaloScenario(map);
            var data = new HaloAiScenario(sc);
            // the variants
            HaloTag human = null, elite = null, carrier = null, infection = null;
            foreach (var tg in map.OfClass("actv"))
            {
                string nm = tg.Name;
                if (human == null && nm.Contains("floodcombat_human") && nm.EndsWith("unarmed")) human = tg;
                else if (elite == null && nm.Contains("floodcombat elite") && nm.EndsWith("unarmed")) elite = tg;
                else if (carrier == null && nm.Contains("floodcarrier")) carrier = tg;
                else if (infection == null && nm.Contains("flood_infection")) infection = tg;
            }
            if (!r.Check(human != null && elite != null && carrier != null && infection != null, "flood variants: " + HaloAi.Short(human) + ", " + HaloAi.Short(elite) + ", " + HaloAi.Short(carrier) + ", " + HaloAi.Short(infection))) yield break;
            // a Flood encounter with firing positions: its BSP and a position with floor room around it
            HaloAiScenario.Encounter enc = null;
            foreach (var e in data.Encounters)
            {
                if (e.FiringPositions.Count < 4) continue;
                bool flood = false;
                foreach (var s in e.Squads) { var v = data.PaletteVariant(s.ActorType); if (v != null && v.Name.Contains("flood")) flood = true; }
                if (flood) { enc = e; break; }
            }
            if (!r.Check(enc != null, "a flood encounter with firing positions in " + mapName)) yield break;
            int bsp = Mathf.Clamp(enc.Bsp, 0, sc.Bsps.Count - 1);
            HaloLabRunner.Log("ai-combat: " + mapName + " encounter " + enc.Name + " (bsp " + bsp + ")");
            var it = Stage(r, map, sc, bsp, "ai combat lab");
            while (it.MoveNext()) yield return it.Current;

            // the player spot: the firing position (of any encounter in this BSP) with the most clear ring points 8 wu around it
            float ring = 8f * HaloSpace.Unit;
            Vector3 player = HaloSpace.Pos(enc.FiringPositions[0].Position); int bestFree = -1;
            foreach (var e in data.Encounters)
            {
                if (e.Bsp != bsp && e != enc) continue;
                foreach (var f in e.FiringPositions)
                {
                    Vector3 p;
                    if (!Floor(HaloActor.OnNav(HaloSpace.Pos(f.Position)), out p)) continue;
                    int free = 0;
                    for (int k = 0; k < 16; k++) { Vector3 q = HaloActor.OnNav(p + Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward * ring); if (RingOk(p, ref q, ring)) free++; }
                    if (free > bestFree) { bestFree = free; player = p; }
                }
            }
            HaloLabRunner.Log("stand-in player at " + player.ToString("0.0") + " (" + bestFree + "/16 clear ring points at 8 wu)");
            var target = MakeStandIn(player);

            var tags = new[] { human, elite, carrier, infection, infection, infection, infection };
            var actors = new List<HaloActor>();
            var startY = new Dictionary<HaloActor, float>();
            HaloActor carrierActor = null;
            for (int i = 0, k = 0; i < tags.Length && k < 48; k++)
            {
                // 16 directions at 8 wu, then 16 more at 7 and 9 wu (offset), each a real ring point in sight of the player
                float r8 = k < 16 ? ring : (k < 32 ? 7f : 9f) * HaloSpace.Unit;
                Vector3 q = HaloActor.OnNav(player + Quaternion.Euler(0f, k * 22.5f * 7f + (k >= 16 ? 11.25f : 0f), 0f) * Vector3.forward * r8);
                if (!RingOk(player, ref q, r8)) continue;
                bool crowded = false;
                foreach (var o in actors) if (o.Unit != null && (o.Unit.transform.position - q).magnitude < 1.5f) crowded = true;
                if (crowded) continue;
                Vector3 to = player - q; to.y = 0f;
                float yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                var a = HaloAi.Spawn(tags[i], q, yaw, null, -1);
                if (a == null || a.Unit == null) { r.Fail("spawn " + HaloAi.Short(tags[i])); i++; continue; }
                a.Unit.Team = HaloTeam.Flood;
                actors.Add(a); startY[a] = q.y;
                if (tags[i] == carrier) carrierActor = a;
                HaloLabRunner.Log("  " + a.Label + " " + HaloAi.Short(tags[i]) + " at " + q.ToString("0.0") + " " + (q - player).magnitude.ToString("0.0") + " m from the player; actv melee " + a.V.MeleeRange + "/" + a.V.MeleeAbortRange + " wu, leap " + a.A.MeleeLeapRange + " wu @" + a.A.MeleeLeapVelocity + " chance " + a.A.MeleeLeapChance + " ballistic " + a.A.MeleeLeapBallistic + ", fudge " + a.A.MeleeFudgeFactor + ", radius " + (a.Unit.Def != null ? a.Unit.Def.Radius : 0f) + " wu");
                i++;
            }
            r.Check(actors.Count == tags.Length, actors.Count + "/" + tags.Length + " flood actors placed 8 wu around the player");
            // magic sight needs the stand-in in the AI's body list (HaloAiBodies.Refresh, on the next AI tick): the first runs called it one
            // frame after spawning, found no player body, and the actors had to perceive it by sight
            for (int w = 0; w < 120 && HaloAiBodies.LocalPlayer == null; w++) yield return null;
            r.Check(HaloAiBodies.LocalPlayer != null, "the stand-in is an AI body (" + (HaloAiBodies.LocalPlayer != null ? HaloAiBodies.LocalPlayer.ToString() : "none") + ")");
            foreach (var a in actors) a.MagicallySee(HaloAiBodies.LocalPlayer);

            int released0 = HaloAi.Released;
            var leapEnds0 = (int[])HaloActor.LeapEnds.Clone();
            float t = 0f, maxApex = 0f; int leaps = 0, sunk = 0; bool midLeapShot = false, burstShot = false;
            float carrierDist = -1f; Vector3 carrierAt = Vector3.zero;
            int walkSamples = 0, walkFacing = 0; float worstWalkAng = 0f; bool walkShot = false;
            var inFlight = new HashSet<HaloActor>();
            float nextLog = 2f;
            while (t < seconds)
            {
                yield return null;
                t += 1f / 60f;
                foreach (var a in actors)
                {
                    if (a.Unit == null) continue;
                    if (a.LeapInFlight)
                    {
                        if (inFlight.Add(a)) leaps++;
                        if (!midLeapShot && a.Unit.transform.position.y - startY[a] > 0.3f)
                        {
                            midLeapShot = true;
                            Vector3 p = a.Unit.transform.position;
                            Vector3 side = Vector3.Cross(Vector3.up, (player - p).normalized);
                            r.Aim(p + side * 5f + Vector3.up * 1.5f, p, 65f); yield return null; r.Shot("ai-combat-midleap");
                        }
                    }
                    else if (inFlight.Remove(a)) maxApex = Mathf.Max(maxApex, a.LastLeapApex);
                    if (a.Alive && Sunk(a.Unit)) { sunk++; HaloLabRunner.Log("BELOW FLOOR: " + a.Label + " at " + a.Unit.transform.position.ToString("0.00")); }
                }
                if (carrierActor != null && carrierDist < 0f && (carrierActor.Unit == null || !carrierActor.Unit.Alive))
                {
                    carrierAt = carrierActor.Unit != null ? carrierActor.Unit.transform.position : carrierAt;
                    Vector3 d = carrierAt - player; d.y = 0f;
                    carrierDist = d.magnitude;
                    HaloLabRunner.Log("carrier went off at t=" + t.ToString("0.0") + " s, " + carrierDist.ToString("0.00") + " m from the player");
                }
                if (carrierActor != null && carrierActor.Unit != null) carrierAt = carrierActor.Unit.transform.position;
                // the carrier's waddle: it faces where it walks (actr standing must move forward), a picture on the way in
                if (carrierActor != null && carrierActor.Alive && carrierActor.Unit != null)
                {
                    Vector3 v = carrierActor.Unit.Velocity; v.y = 0f;
                    if (v.magnitude > 0.3f)
                    {
                        Vector3 fw = carrierActor.Unit.transform.forward; fw.y = 0f;
                        float ang = Vector3.Angle(fw, v);
                        walkSamples++; if (ang <= 45f) walkFacing++;
                        worstWalkAng = Mathf.Max(worstWalkAng, ang);
                        float dp = Vector3.Distance(carrierAt, player);
                        if (!walkShot && dp < 8f)
                        {
                            walkShot = true;
                            Vector3 side = Vector3.Cross(Vector3.up, v.normalized);
                            r.Aim(carrierAt + side * 4f + Vector3.up * 1.6f, carrierAt + Vector3.up * 0.6f, 65f); yield return null; r.Shot("ai-combat-carrier-walk");
                            HaloLabRunner.Log("carrier walking " + v.magnitude.ToString("0.0") + " m/s, " + dp.ToString("0.0") + " m from the player, facing " + ang.ToString("0") + " deg off its movement");
                        }
                    }
                }
                if (!burstShot && HaloAi.Released > released0)
                {
                    burstShot = true;
                    r.Aim(carrierAt + new Vector3(3f, 3f, 3f), carrierAt + Vector3.up * 0.5f, 70f); yield return null; r.Shot("ai-combat-burst");
                }
                if (t >= nextLog)
                {
                    nextLog += 2f;
                    string line = "t=" + t.ToString("0") + " s: player hit " + target.Hits + "x (" + target.Taken.ToString("0") + "), melee blows " + target.MeleeHits + ", released " + (HaloAi.Released - released0) + ";";
                    foreach (var a in actors)
                        if (a.Unit != null) line += " " + a.Label + " " + (a.Alive ? a.State.ToString() : "DEAD") + (a.Berserking ? " BERSERK" : "") + (a.MeleeCharging ? " CHARGING" : "") + (a.LeapInFlight ? " LEAP" : "") + " " + Vector3.Distance(a.Unit.transform.position, player).ToString("0.0") + " m;";
                    HaloLabRunner.Log(line);
                }
            }
            foreach (var a in inFlight) if (a.LastLeapApex > maxApex) maxApex = a.LastLeapApex;
            r.Check(leaps > 0, "flood leaps: " + leaps);
            // a leap is an attack that reaches its target or is stopped by a wall (no invented height bound: the leap's shape comes from
            // actr "melee leap velocity / ballistic", see HaloActor.Launch). The stand-in stands still, so a leap that comes down on open
            // floor before its planned flight time ("Short": round 3 free#6, a 0.17 s hop that missed) is a failed leap. "Flew the
            // planned time without contact" is logged, not failed.
            int lHit = HaloActor.LeapEnds[(int)HaloActor.LeapEnd.Hit] - leapEnds0[(int)HaloActor.LeapEnd.Hit];
            int lFlew = HaloActor.LeapEnds[(int)HaloActor.LeapEnd.Flew] - leapEnds0[(int)HaloActor.LeapEnd.Flew];
            int lWall = HaloActor.LeapEnds[(int)HaloActor.LeapEnd.Wall] - leapEnds0[(int)HaloActor.LeapEnd.Wall];
            int lShort = HaloActor.LeapEnds[(int)HaloActor.LeapEnd.Short] - leapEnds0[(int)HaloActor.LeapEnd.Short];
            int lAll = HaloActor.LeapEnds[0] - leapEnds0[0];
            HaloLabRunner.Log("leaps launched " + lAll + ": hit " + lHit + ", flew the planned time without contact " + lFlew + ", stopped by a wall " + lWall + ", short " + lShort + ", unfinished " + (lAll - lHit - lFlew - lWall - lShort) + "; highest apex " + maxApex.ToString("0.00") + " m above the take-off");
            r.Check(lShort == 0 && lHit > 0, "flood leaps reach the stand-in: " + lHit + " hit, " + lWall + " stopped by a wall, " + lFlew + " flew past, " + lShort + " short (came down on open floor before their planned flight)");
            r.Check(target.MeleeHits == 0 || target.WorstMeleeDist <= 2f, "melee blows on the player: " + target.MeleeHits + ", farthest nearest-Flood distance " + target.WorstMeleeDist.ToString("0.00") + " m (<= 2 m)");
            r.Check(carrierDist >= 0f && carrierDist <= 3f, "the carrier went off " + (carrierDist >= 0f ? carrierDist.ToString("0.00") + " m" : "never") + " from the player (<= 3 m)");
            int n = HaloAi.Released - released0;
            r.Check(n >= 3 && n <= 6, "the carrier released " + n + " infection forms (3..6)");
            r.Check(sunk == 0, "bodies sunk under a floor: " + sunk + " samples");
            r.Check(walkSamples > 0 && walkFacing >= walkSamples * 0.9f, "the carrier faces its movement: " + walkFacing + "/" + walkSamples + " walking samples within 45 deg (worst " + worstWalkAng.ToString("0") + ")");
            r.Aim(player + Vector3.up * 3f + new Vector3(4f, 0f, 4f), player, 70f); yield return null; r.Shot("ai-combat-end");
            HaloLabRunner.Log("edge holds: steer " + HaloActor.OffNavBlocks + ", turn clamp (must-move-forward) " + HaloActor.ClampHeldSteps + ", stuck give-ups " + HaloActor.StuckGiveUps + ", mover recoveries " + HaloActor.Recoveries);
            HaloAi.LogBudget();
            HaloAi.Clear();
        }

        // ------------------------------------------------------------------ ai-combat-area (squads stay in their areas)

        static IEnumerator Area(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "a30");
            string encName = HaloLabBoot.Arg("-haloenc", "lz_search");
            float seconds = float.Parse(HaloLabBoot.Arg("-haloseconds", "40"), System.Globalization.CultureInfo.InvariantCulture);
            var entry = Entry(r, mapName);
            if (!r.Check(entry != null, "map " + mapName)) yield break;
            var map = new HaloMap(entry.File);
            var sc = new HaloScenario(map);
            var data = new HaloAiScenario(sc);
            var enc = data.FindEncounter(encName);
            if (!r.Check(enc != null, "encounter " + encName)) yield break;
            int bsp = Mathf.Clamp(enc.Bsp, 0, sc.Bsps.Count - 1);
            var it = Stage(r, map, sc, bsp, "ai combat area lab");
            while (it.MoveNext()) yield return it.Current;

            // the player: a firing position 12..35 m from the starting locations' centre that sees the most of them
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
            var target = MakeStandIn(player);
            HaloAi.Place(encName);
            yield return null;
            r.Check(HaloAi.LivingCount(encName) > 0, "ai_place " + encName + ": " + HaloAi.LivingCount(encName) + " actors");
            yield return r.Wait(() => HaloAi.Nav != null, 15f, "the navigation");     // an AI tick builds it
            var nav = HaloAi.Nav;
            if (!r.Check(nav != null, "navigation of bsp " + bsp)) yield break;

            // setup (as ai-enc-area): squads whose starting locations lie on a navigation island holding none of their squad's positions
            // (a30 lz_search/cship_toon: on the canyon rim, loaded into the dropship by the script and only unloaded at the LZ) are put
            // down on the nearest point of the nearest such position's island - placed standalone, Halo would leave them unable to path
            int delivered = 0; var dropped = new HashSet<HaloActor>();
            foreach (var a in HaloAi.Actors)
            {
                if (a.Unit == null || !a.Alive || a.Enc == null || a.Free || a.Squad < 0 || a.Squad >= a.Enc.Data.Squads.Count) continue;
                uint um = SquadMask(a.Enc.Data.Squads[a.Squad]);
                int myComp = HaloAiEncounter.ComponentAt(a.Position);
                if (myComp < 0) continue;
                bool reach = false; int island = -1; float nd = float.MaxValue;
                for (int i = 0; i < a.Enc.Positions.Length; i++)
                {
                    if (um != 0 && !a.Enc.PositionInGroups(i, um)) continue;
                    int c = a.Enc.PositionComponent(i);
                    if (c < 0 || c == myComp) { reach = true; break; }
                    float d = (a.Enc.Positions[i] - a.Position).sqrMagnitude;
                    if (d < nd) { nd = d; island = c; }
                }
                if (reach || island < 0) continue;
                Vector3 h = HaloSpace.ToHalo(a.Position); HVec3 hq;
                if (!nav.NearestInComponent(island, h.x, h.y, h.z, 200f, out hq)) { r.Check(false, a.Label + ": no point of island " + island + " to put it down on"); continue; }
                Vector3 to = HaloSpace.Pos(hq);
                HaloLabRunner.Log("setup: " + a.Label + " starts on island " + myComp + " where its squad has no firing position (dropship-loaded squad): put down on island " + island + " " + (to - a.Position).magnitude.ToString("0.0") + " m away");
                a.Unit.Teleport(to, a.Unit.transform.eulerAngles.y);
                delivered++; dropped.Add(a);
            }
            HaloLabRunner.Log("setup: " + delivered + " actors put down on their squad's positions' island");
            var entered = new HashSet<HaloActor>();
            var lastOff = new Dictionary<HaloActor, float>();
            var noProgress = new Dictionary<HaloActor, int>();
            int left = 0, stalled = 0, walkingIn = 0;

            float t = 0f, nextCheck = 2f; int samples = 0;
            double errMoving = 0, errStill = 0; int nMoving = 0, nStill = 0;
            var grenades = new Dictionary<HaloActor, int>();
            foreach (var a in HaloAi.Actors) if (a.Unit != null) grenades[a] = a.Unit.Grenades;
            // grenade tracking: the thrower's target spot, then the nearest live grenade until it stops / disappears
            Vector3 aim = Vector3.zero, gLast = Vector3.zero; bool tracking = false; float trackStart = 0f; int landed = 0; float worstLand = 0f;
            var landings = new List<float>();
            while (t < seconds)
            {
                yield return null;
                t += 1f / 60f;
                // throws
                foreach (var a in HaloAi.Actors)
                {
                    if (a.Unit == null) continue;
                    int g0; if (!grenades.TryGetValue(a, out g0)) g0 = a.Unit.Grenades;
                    if (a.Unit.Grenades < g0 && !tracking && a.Target != null) { tracking = true; trackStart = t; aim = a.TargetLastSeen; gLast = a.Unit.transform.position; HaloLabRunner.Log("grenade by " + a.Label + " (" + (a.A != null ? a.A.Type.ToString() : "?") + ") at " + aim.ToString("0.0")); }
                    grenades[a] = a.Unit.Grenades;
                }
                if (tracking)
                {
                    var live = HaloAi.LiveGrenades;
                    float bd = 999f; Vector3 near = gLast;
                    for (int i = 0; i < live.Count; i++) { float d = (live[i] - gLast).sqrMagnitude; if (d < bd) { bd = d; near = live[i]; } }
                    bool gone = live.Count == 0 && t - trackStart > 0.5f;
                    bool still = live.Count > 0 && (near - gLast).sqrMagnitude < 0.0004f && t - trackStart > 0.5f;
                    if (live.Count > 0) { if (!still) gLast = near; }
                    if (gone || still || t - trackStart > 6f)
                    {
                        tracking = false;
                        Vector3 d = gLast - aim; d.y = 0f;
                        landings.Add(d.magnitude); landed++; worstLand = Mathf.Max(worstLand, d.magnitude);
                        HaloLabRunner.Log("grenade landed " + d.magnitude.ToString("0.0") + " m from the aim point (" + (t - trackStart).ToString("0.0") + " s)");
                    }
                }
                // error scale moving vs standing
                foreach (var a in HaloAi.Actors)
                {
                    if (a.Unit == null || !a.Alive || !a.Unit.Firing) continue;
                    if (a.Moving) { errMoving += a.Unit.AiErrorScale; nMoving++; } else { errStill += a.Unit.AiErrorScale; nStill++; }
                }
                if (t >= nextCheck)
                {
                    nextCheck += 2f;
                    foreach (var a in HaloAi.Actors)
                    {
                        if (a.Unit == null || !a.Alive || a.Enc == null || a.Free || a.Enc.FollowPlayers || a.Squad < 0 || a.Squad >= a.Enc.Data.Squads.Count) continue;
                        var sq = a.Enc.Data.Squads[a.Squad];
                        uint mask = SquadMask(sq);
                        bool any = false; Vector3 mn = Vector3.zero, mx = Vector3.zero;
                        for (int i = 0; i < a.Enc.Positions.Length; i++)
                        {
                            if (mask != 0 && !a.Enc.PositionInGroups(i, mask)) continue;
                            var p = a.Enc.Positions[i];
                            if (!any) { mn = mx = p; any = true; } else { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
                        }
                        if (!any) continue;
                        samples++;
                        float m = 2f * HaloSpace.Unit;
                        Vector3 q = a.Position;
                        float ox = Mathf.Max(0f, Mathf.Max(mn.x - q.x, q.x - mx.x)), oz = Mathf.Max(0f, Mathf.Max(mn.z - q.z, q.z - mx.z));
                        float off = Mathf.Sqrt(ox * ox + oz * oz);
                        // Halo walks an actor from its starting location onto its squad's positions: until it has been inside (<= 2 wu) it
                        // counts as walking in while it closes in by >= 0.5 wu a sample (3 samples without progress = STALLED); once
                        // inside, any sample more than 2 wu out is a failure (LEFT its area)
                        string tag;
                        if (off <= m) { entered.Add(a); tag = off > 0f ? "at the edge" : "inside"; noProgress[a] = 0; }
                        else if (entered.Contains(a)) { left++; tag = "LEFT its area"; }
                        else
                        {
                            float prev; int np;
                            if (!lastOff.TryGetValue(a, out prev) || prev - off >= 0.5f * HaloSpace.Unit) { np = 0; tag = "walking in"; walkingIn++; }
                            else
                            {
                                noProgress.TryGetValue(a, out np); np++;
                                tag = np >= 3 ? "STALLED outside" : "walking in (no progress " + np + ")";
                                if (np >= 3) stalled++;
                            }
                            noProgress[a] = np;
                        }
                        lastOff[a] = off;
                        HaloLabRunner.Log("  t=" + t.ToString("0") + " " + a.Label + " " + a.State + " squad " + sq.Name + " " + tag + (off > 0f ? " (" + (off / HaloSpace.Unit).ToString("0.0") + " wu out)" : "") + (a.Moving ? " moving" : "") + " err x" + a.Unit.AiErrorScale.ToString("0.00"));
                    }
                }
            }
            int alive = 0, inArea = 0;
            foreach (var a in HaloAi.Actors) { if (a.Unit == null || !a.Alive || a.Enc == null || a.Free) continue; alive++; if (entered.Contains(a)) inArea++; }
            HaloLabRunner.Log("area: " + samples + " samples, " + walkingIn + " walking-in samples, " + inArea + " of " + alive + " living actors reached their squad's area");
            r.Check(samples > 0 && left == 0, "actors more than 2 wu outside their squad's firing-position bounds after having been in it: " + left + " of " + samples + " samples");
            r.Check(stalled == 0, "actors stalled outside their squad's area (3 samples without closing in): " + stalled + " samples");
            if (landed > 0) r.Check(worstLand < 2f, "grenades: " + landed + " landed, worst " + worstLand.ToString("0.0") + " m from the aim point (< 2 m)");
            else HaloLabRunner.Log("grenades: none thrown in " + seconds + " s");
            HaloLabRunner.Log("burst error scale: moving " + (nMoving > 0 ? (errMoving / nMoving).ToString("0.00") : "-") + " (" + nMoving + " ticks), standing " + (nStill > 0 ? (errStill / nStill).ToString("0.00") : "-") + " (" + nStill + " ticks); player hit " + target.Hits + "x");
            if (nMoving > 0 && nStill > 0) r.Check(errMoving / nMoving >= errStill / nStill, "burst error on the move >= standing");
            // the picture from above the living actors' centroid (the old one, from the player's spot, looked into rock)
            Vector3 cc = Vector3.zero; int nc = 0;
            foreach (var a in HaloAi.Actors) if (a.Unit != null && a.Alive && (dropped.Count == 0 || dropped.Contains(a))) { cc += a.Position; nc++; }
            if (nc == 0) cc = centre; else cc /= nc;
            r.Aim(cc + new Vector3(0f, 45f, -25f), cc, 70f); yield return null; r.Shot("ai-combat-area");
            HaloLabRunner.Log("edge holds: steer " + HaloActor.OffNavBlocks + ", turn clamp (must-move-forward) " + HaloActor.ClampHeldSteps + ", stuck give-ups " + HaloActor.StuckGiveUps + ", mover recoveries " + HaloActor.Recoveries);
            HaloAi.LogBudget();
            HaloAi.Clear();
        }
    }
}

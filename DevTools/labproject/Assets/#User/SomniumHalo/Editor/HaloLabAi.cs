using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// AI module lab (DevTools/lab.sh ai [-halomap a30] [-haloenc cave_floor] [-haloallies first_marine/left_back] [-haloseconds 40]
    /// [-halodiff 1] [-haloflat 1]): builds the encounter's BSP of the real map (World), a stand-in player (a capsule with a hit box that
    /// counts the damage it takes) at one of the encounter's firing positions far from its starting locations, places the encounter with
    /// HaloAi.Place (difficulty counts, starting locations, major upgrades) and two allied marines teleported next to the player following
    /// it, then lets the AI run: pictures from behind the player and from above (taken positions, fights, deaths), the elites killed by
    /// script half way (the grunts' leader-killed panic), a log of every actor's state / position / target every few seconds, the counts of
    /// bursts, grenades, melee, evades, panics, deaths and the AI tick cost. -haloflat 1 = a flat floor instead of the map (the
    /// encounter's own positions moved onto it).
    /// </summary>
    [InitializeOnLoadAttribute]
    static class HaloLabAi
    {
        static HaloLabAi()
        {
            HaloLabRunner.Register("ai", r => Run(r));
            HaloLabRunner.Register("ai-vehicle", r => Vehicle(r));
            HaloLabRunner.Register("ai-banshee", r => Banshee(r));
        }

        /// <summary>
        /// ai-banshee (a30): mission_lz_banshee by hand - pass_banshee_1/2 created, lz_banshee/pilot_1/2 placed and seated as drivers
        /// (vehicle_load_magic), teleported to lz_banshee_simple_flag_1/2, command lists lz_banshee_1/2, then ai_magically_see_players: the
        /// banshees fly their points and make attack runs on the stand-in player (heights, distances, bursts logged), then lz_banshee_3.
        /// </summary>
        static IEnumerator Banshee(HaloLabRunner r)
        {
            HaloInstall.MapEntry entry = null;
            foreach (var e in r.Game.Install.Campaign) if (e.Name == "a30") entry = e;
            if (!r.Check(entry != null, "map a30")) yield break;
            var map = new HaloMap(entry.File);
            var sc = new HaloScenario(map);
            Time.captureFramerate = 60;
            HaloCombat.LabMap = map; HaloCombat.LabScenario = sc;
            HaloAi.LabMap = map; HaloAi.LabScenario = sc; HaloAi.LabBsp = 0; HaloAi.Verbose = true;
            var hub = GameObject.Find("Halo Hub"); if (hub != null) hub.SetActive(false);
            var stage = new GameObject("ai banshee lab");
            HaloBipeds.LabParent = stage.transform;
            var sun = new GameObject("sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            HaloWorld.PlaceObjects = false;
            var root = new GameObject("Lab World Root");
            var it = HaloWorld.Build(map, sc, root.transform, null, 0);
            while (it.MoveNext()) yield return it.Current;
            HaloWorldDriver.View = r.Cam;
            // flags
            var flags = new Dictionary<string, Vector3>();
            foreach (var f in sc.Tag.Root.Block(1252, 92)) flags[f.Str32(4)] = HaloSpace.Pos(f.Vec3(36));
            var hogs = new List<HaloVehicle>();
            for (int k = 1; k <= 2; k++)
            {
                HaloScenario.Placement pl = null;
                foreach (var p in sc.Vehicles) if (p.NameIndex >= 0 && p.NameIndex < sc.ObjectNames.Count && sc.ObjectNames[p.NameIndex] == "pass_banshee_" + k) pl = p;
                if (!r.Check(pl != null, "placement pass_banshee_" + k)) yield break;
                var v = HaloVehicles.SpawnPlacement(pl, "pass_banshee_" + k, root.transform);
                hogs.Add(v);
            }
            yield return null;
            Vector3 f1 = flags.ContainsKey("lz_banshee_simple_flag_1") ? flags["lz_banshee_simple_flag_1"] : hogs[0].transform.position + Vector3.up * 20f;
            Vector3 f2 = flags.ContainsKey("lz_banshee_simple_flag_2") ? flags["lz_banshee_simple_flag_2"] : hogs[1].transform.position + Vector3.up * 20f;
            HaloAiDriver.Ensure(stage.transform);
            for (int k = 1; k <= 2; k++)
            {
                var v = hogs[k - 1];
                HaloAi.Place("lz_banshee/pilot_" + k);
                yield return null;
                var us = HaloAi.Units("lz_banshee/pilot_" + k);
                if (!r.Check(us.Count == 1, "pilot " + k + " placed")) yield break;
                int drv = -1; for (int s = 0; s < v.Seats.Count; s++) if (v.Seats[s].Def.Driver) drv = s;
                r.Check(v.SeatUnit(drv, us[0]), "pilot " + k + " seated (" + v.Seats[drv].Label + ")");
                v.Place(k == 1 ? f1 : f2, v.transform.rotation, Vector3.zero);
                HaloAi.CommandList("lz_banshee/pilot_" + k, "lz_banshee_" + k);
            }
            // the stand-in player: on the ground near the first command list point
            var cl = HaloAi.Data.FindCommandList("lz_banshee_1");
            Vector3 aimAt = cl != null && cl.Points.Count > 0 ? HaloSpace.Pos(cl.Points[0]) : f1;
            var feet = new GameObject("feet"); feet.transform.SetParent(stage.transform, false);
            RaycastHit gh;
            Vector3 ground = Physics.Raycast(aimAt + Vector3.up * 5f, Vector3.down, out gh, 200f, HaloLayers.GroundMask) ? gh.point : aimAt;
            feet.transform.position = HaloActor.OnNav(ground + new Vector3(25f, 0f, 10f));
            HaloAiBodies.StandInPlayer = feet.transform;
            var standIn = GameObject.CreatePrimitive(PrimitiveType.Capsule); standIn.transform.position = feet.transform.position + Vector3.up * 0.9f; standIn.layer = HaloLayers.Hitbox;
            HaloLabRunner.Log("flags " + f1.ToString("0") + " / " + f2.ToString("0") + ", first point " + aimAt.ToString("0") + ", player " + feet.transform.position.ToString("0"));
            int shots0 = HaloAi.Shots; float minH = 999f, maxH = -999f, minD = 999f; int pic = 0;
            for (int fr = 0; fr < 45 * 60; fr++)
            {
                yield return null;
                float t = fr / 60f;
                if (fr == 180) HaloAi.MagicallySeePlayers("lz_banshee");
                if (fr == 32 * 60) { HaloAi.CommandList("lz_banshee/pilot_1", "lz_banshee_3"); HaloAi.CommandList("lz_banshee/pilot_2", "lz_banshee_3"); HaloLabRunner.Log("script: ai_command_list lz_banshee_3"); }
                foreach (var v in hogs)
                {
                    if (v == null) continue;
                    Vector3 p = v.transform.position;
                    RaycastHit h; float above = Physics.Raycast(p, Vector3.down, out h, 300f, HaloLayers.GroundMask) ? h.distance : 300f;
                    if (t > 3f) { minH = Mathf.Min(minH, above); maxH = Mathf.Max(maxH, above); minD = Mathf.Min(minD, (p - feet.transform.position).magnitude); }
                }
                if (fr % 300 == 0 && fr > 0)
                {
                    string line = "t=" + t.ToString("0") + " s:";
                    foreach (var v in hogs) if (v != null) line += " " + v.name + " at " + v.transform.position.ToString("0") + " " + (v.transform.position - feet.transform.position).magnitude.ToString("0") + " m from the player, ai " + v.AiDriven + ";";
                    HaloLabRunner.Log(line + " bursts " + (HaloAi.Shots - shots0));
                }
                if (fr % 240 == 120 && hogs[0] != null)
                {
                    Vector3 p = hogs[0].transform.position;
                    r.Aim(feet.transform.position + Vector3.up * 1.7f, p, 60f); yield return null;
                    r.Shot("aib_" + (pic++));
                }
            }
            r.Check(maxH > 4f, "banshees flew (height over ground " + minH.ToString("0.0") + " .. " + maxH.ToString("0.0") + " m)");
            r.Check(minD < 40f, "an attack run came within " + minD.ToString("0") + " m of the player");
            r.Check(HaloAi.Shots - shots0 > 0, "banshee bursts " + (HaloAi.Shots - shots0));
            HaloAi.LogBudget();
            HaloAi.Clear();
        }

        /// <summary>
        /// ai-vehicle [-halomap a30]: the map's first warthog placement on its BSP, two marines (first_marine/left_back) next to it
        /// told ai_go_to_vehicle (driver + gunner), following a stand-in player that then walks 40 m away: the marines board, the driver
        /// drives after the player (distance covered logged), pictures.
        /// </summary>
        static IEnumerator Vehicle(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "a30");
            string allies = HaloLabBoot.Arg("-haloallies", "first_marine/left_back");
            HaloInstall.MapEntry entry = null;
            foreach (var e in r.Game.Install.Campaign) if (e.Name == mapName) entry = e;
            if (!r.Check(entry != null, "map " + mapName)) yield break;
            var map = new HaloMap(entry.File);
            var sc = new HaloScenario(map);
            HaloScenario.Placement hog = null;
            foreach (var p in sc.Vehicles) if (p.Type != null && p.Type.Name.Contains("warthog")) { hog = p; break; }
            if (!r.Check(hog != null, "a warthog placement")) yield break;
            Time.captureFramerate = 60;
            HaloCombat.LabMap = map; HaloCombat.LabScenario = sc;
            HaloAi.LabMap = map; HaloAi.LabScenario = sc; HaloAi.LabBsp = 0; HaloAi.Verbose = true;
            var hub = GameObject.Find("Halo Hub");
            if (hub != null) hub.SetActive(false);
            var stage = new GameObject("ai vehicle lab");
            HaloBipeds.LabParent = stage.transform;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.0f; sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            HaloWorld.PlaceObjects = false;
            var root = new GameObject("Lab World Root");
            var it = HaloWorld.Build(map, sc, root.transform, null, 0);
            while (it.MoveNext()) yield return it.Current;
            HaloWorldDriver.View = r.Cam;
            var v = HaloVehicles.SpawnPlacement(hog, "lab_hog", root.transform);
            if (!r.Check(v != null, "warthog spawned at " + HaloSpace.Pos(hog.Position).ToString("0.0"))) yield break;
            for (int i = 0; i < 90; i++) yield return null;
            Vector3 hp = v.transform.position;
            var feet = new GameObject("feet"); feet.transform.SetParent(stage.transform, false);
            feet.transform.position = HaloActor.OnNav(hp + v.transform.right * 6f);
            HaloAiBodies.StandInPlayer = feet.transform;
            HaloAiDriver.Ensure(stage.transform);
            HaloAi.Place(allies);
            yield return null;
            var units = HaloAi.Units(allies);
            for (int i = 0; i < units.Count; i++) units[i].Teleport(HaloActor.OnNav(hp - v.transform.right * 4f + v.transform.forward * (i * 2f - 1f)), 0f);
            HaloAi.FollowTargetPlayers(allies);
            HaloAi.GoToVehicle(allies, v.gameObject, "");
            float t = 0f;
            while (t < 12f && HaloAi.GoingToVehicle(v.gameObject) > 0) { yield return null; t += 1f / 60f; }
            int seated = 0;
            for (int i = 0; i < v.Seats.Count; i++) if (v.UnitIn(i) != null) { seated++; HaloLabRunner.Log("seat " + v.Seats[i].Label + ": " + v.UnitIn(i).name); }
            r.Check(seated > 0, seated + " marines boarded in " + t.ToString("0.0") + " s");
            r.Aim(hp + Vector3.up * 6f - v.transform.forward * 10f, hp, 70f); yield return null; r.Shot("aiv_" + mapName + "_boarded");
            // the player walks away: the driver follows
            Vector3 start = v.transform.position;
            Vector3 away = HaloActor.OnNav(hp + v.transform.forward * 40f);
            feet.transform.position = away;
            HaloLabRunner.Log("stand-in player moved to " + away.ToString("0.0") + " (" + (away - start).magnitude.ToString("0") + " m from the warthog)");
            for (float k = 0f; k < 15f; k += 1f / 60f)
            {
                yield return null;
                if (Mathf.Abs(k - 5f) < 0.01f || Mathf.Abs(k - 14f) < 0.01f) HaloLabRunner.Log("t=" + k.ToString("0") + " s: warthog at " + v.transform.position.ToString("0.0") + ", " + (v.transform.position - away).magnitude.ToString("0.0") + " m from the player, ai driven " + v.AiDriven);
            }
            float moved = (v.transform.position - start).magnitude;
            r.Check(moved > 8f, "the AI driver drove " + moved.ToString("0.0") + " m toward the player (now " + (v.transform.position - away).magnitude.ToString("0.0") + " m from it)");
            Vector3 vp = v.transform.position;
            r.Aim(vp + Vector3.up * 5f - (away - vp).normalized * 10f, vp, 70f); yield return null; r.Shot("aiv_" + mapName + "_driving");
            HaloAi.LogBudget();
            HaloAi.Clear();
        }

        sealed class StandIn : MonoBehaviour, IHaloDamageable
        {
            public float Taken; public int Hits;
            public void TakeHit(HaloHit hit) { Taken += hit.Amount; Hits++; if (Hits == 1) HaloLabRunner.Log("stand-in player: first hit " + hit.Amount.ToString("0.0") + " by " + (hit.Cause != null ? hit.Cause.Name : "?")); }
        }

        static IEnumerator Run(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "a30");
            string encName = HaloLabBoot.Arg("-haloenc", "cave_floor");
            string allies = HaloLabBoot.Arg("-haloallies", "first_marine/left_back");
            float seconds = float.Parse(HaloLabBoot.Arg("-haloseconds", "40"), System.Globalization.CultureInfo.InvariantCulture);
            bool flat = HaloLabBoot.Arg("-haloflat", "0") == "1";
            var entry = r.Game.Install.Map(mapName);
            if (entry == null) foreach (var e in r.Game.Install.Campaign) if (e.Name == mapName) entry = e;
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            var map = new HaloMap(entry.File);
            var sc = new HaloScenario(map);
            var data = new HaloAiScenario(sc);
            var enc = data.FindEncounter(encName);
            if (!r.Check(enc != null, "encounter " + encName)) yield break;
            int bsp = Mathf.Clamp(enc.Bsp, 0, sc.Bsps.Count - 1);
            HaloLabRunner.Log("ai: " + mapName + " encounter " + encName + " (bsp " + bsp + ", " + enc.Squads.Count + " squads, " + enc.FiringPositions.Count + " firing positions)");

            Time.captureFramerate = 60;
            HaloCombat.LabMap = map; HaloCombat.LabScenario = sc;
            HaloAi.LabMap = map; HaloAi.LabScenario = sc; HaloAi.LabBsp = bsp; HaloAi.Verbose = true;
            HaloAi.Difficulty = (HaloCampaign.Difficulty)int.Parse(HaloLabBoot.Arg("-halodiff", "1"));
            try { HaloFx.Preload(map); } catch (System.Exception e) { HaloLabRunner.Log("fx preload: " + e.Message); }
            var hub = GameObject.Find("Halo Hub");
            if (hub != null) hub.SetActive(false);
            var stage = new GameObject("ai lab");
            HaloBipeds.LabParent = stage.transform;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.0f; sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);

            // the encounter's points (Unity space)
            Vector3 centre = Vector3.zero; int nStarts = 0;
            foreach (var s in enc.Squads) foreach (var l in s.Starts) { centre += HaloSpace.Pos(l.Position); nStarts++; }
            if (nStarts > 0) centre /= nStarts;
            // encounters spread over the map: the densest cluster of starting locations (within 25 m of the one with most neighbours)
            {
                var all = new List<Vector3>();
                foreach (var s in enc.Squads) foreach (var l in s.Starts) all.Add(HaloSpace.Pos(l.Position));
                int bestN = -1; Vector3 seed = centre;
                foreach (var a in all) { int n = 0; foreach (var b in all) if ((a - b).sqrMagnitude < 25f * 25f) n++; if (n > bestN) { bestN = n; seed = a; } }
                Vector3 sum = Vector3.zero; int k = 0;
                foreach (var b in all) if ((seed - b).sqrMagnitude < 25f * 25f) { sum += b; k++; }
                if (k > 0) centre = sum / k;
                HaloLabRunner.Log("starting locations: " + all.Count + ", densest cluster " + k + " around " + centre.ToString("0.0"));
            }

            float yOffset = 0f;
            if (flat)
            {
                HaloAi.NoNav = true;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "floor"; floor.transform.SetParent(stage.transform, false);
                floor.transform.position = new Vector3(centre.x, centre.y - 0.02f, centre.z);
                floor.transform.localScale = new Vector3(40f, 1f, 40f);
                floor.layer = HaloLayers.World;
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f));
                floor.GetComponent<Renderer>().sharedMaterial = mat;
                yOffset = 0f;
            }
            else
            {
                HaloWorld.PlaceObjects = false;
                var root = new GameObject("Lab World Root");
                root.transform.position = HaloSpace.MapOrigin;
                float t0 = Time.realtimeSinceStartup;
                var it = HaloWorld.Build(map, sc, root.transform, null, bsp);
                while (it.MoveNext()) yield return it.Current;
                r.Check(HaloWorld.Ready, "bsp " + bsp + " built in " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s");
                HaloWorldDriver.View = r.Cam;
            }
            Physics.SyncTransforms();
            yield return null;

            // the stand-in player: the encounter's firing position 12..35 m from the starting locations' centre that sees the most of them
            Vector3 player = centre;
            float best = -1f;
            var starts = new List<Vector3>();
            foreach (var s in enc.Squads) foreach (var l in s.Starts) { var q = HaloSpace.Pos(l.Position); if ((q - centre).sqrMagnitude < 30f * 30f) starts.Add(q); }
            foreach (var f in enc.FiringPositions)
            {
                Vector3 p = HaloSpace.Pos(f.Position);
                float d = (p - centre).magnitude;
                if (d < 12f || d > 35f) continue;
                int seen = 0;
                foreach (var s in starts) if (!Physics.Linecast(p + Vector3.up * 1.6f, s + Vector3.up * 1.2f, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore)) seen++;
                float score = seen + d * 0.01f;
                if (score > best) { best = score; player = p; }
            }
            if (best < 0f) player = HaloActor.OnNav(centre + new Vector3(18f, 0f, 0f));
            HaloLabRunner.Log("stand-in spot sees " + Mathf.FloorToInt(best) + " of " + starts.Count + " starting locations");
            var standIn = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            standIn.name = "stand-in player";
            standIn.transform.SetParent(stage.transform, false);
            standIn.transform.position = player + Vector3.up * 0.9f;
            standIn.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            standIn.layer = HaloLayers.Hitbox;
            var sm = new Material(Shader.Find("Universal Render Pipeline/Lit")); sm.SetColor("_BaseColor", new Color(0.1f, 0.6f, 1f));
            standIn.GetComponent<Renderer>().sharedMaterial = sm;
            var target = standIn.AddComponent<StandIn>();
            var feet = new GameObject("feet"); feet.transform.SetParent(stage.transform, false); feet.transform.position = player;
            HaloAiBodies.StandInPlayer = feet.transform;
            HaloLabRunner.Log("stand-in player at " + player.ToString("0.0") + ", " + (player - centre).magnitude.ToString("0") + " m from the encounter's centre " + centre.ToString("0.0"));

            // place the encounter, the allies next to the player
            HaloAiDriver.Ensure(stage.transform);
            HaloAi.Place(encName);
            yield return null;
            int placed = HaloAi.LivingCount(encName);
            r.Check(placed > 0, "ai_place " + encName + ": " + placed + " living actors");
            if (!string.IsNullOrEmpty(allies) && allies != "none")
            {
                HaloAi.Place(allies);
                var units = HaloAi.Units(allies);
                if (HaloLabBoot.Arg("-haloalliesstay", "0") != "1")
                {
                    for (int i = 0; i < units.Count; i++) units[i].Teleport(HaloActor.OnNav(player + Quaternion.Euler(0f, 120f * i, 0f) * Vector3.forward * 2.5f), 0f);
                    HaloAi.FollowTargetPlayers(allies);
                }
                r.Check(units.Count > 0, "allies " + allies + ": " + units.Count + " next to the player, following");
            }
            foreach (var a in HaloAi.Actors) HaloLabRunner.Log("  actor " + a.Label + " " + HaloAi.Short(a.V != null ? a.V.Tag : null) + " (" + (a.A != null ? a.A.Type.ToString() : "?") + ") team " + a.Team + " state " + a.State + "/" + a.InitialState + " at " + a.Position.ToString("0.0") + " weapon " + HaloAi.Short(a.Unit != null ? a.Unit.Weapon : null));

            Vector3 behind = player + (player - centre).normalized * 4f + Vector3.up * 2.2f;
            // the second view: halfway, raised, looking at the starting locations (interiors have ceilings)
            Vector3 above = Vector3.Lerp(player, centre, 0.45f) + Vector3.up * 3.5f;
            if (Physics.Linecast(player + Vector3.up * 1.6f, above, HaloAiBodies.Blockers)) above = player + Vector3.up * 3f;
            float t = 0f; int shot = 0;
            float killAt = seconds * 0.4f; bool killed = false;
            float[] pics = { 0.5f, 3f, 6f, 10f, killAt + 1f, killAt + 4f, seconds * 0.75f, seconds - 0.5f };
            int nextPic = 0; float nextLog = 2f;
            while (t < seconds)
            {
                yield return null;
                t += 1f / 60f;
                if (!killed && t >= killAt)
                {
                    killed = true;
                    int before = HaloAi.Panics;
                    foreach (var s in enc.Squads)
                    {
                        var v = HaloAi.Catalog != null ? HaloAi.Catalog.Variant(data.PaletteVariant(s.ActorType)) : null;
                        if (v != null && v.Actor != null && v.Actor.Type == HaloActorType.Elite) { HaloLabRunner.Log("script: ai_kill " + encName + "/" + s.Name + " (" + HaloAi.LivingCount(encName + "/" + s.Name) + " living)"); HaloAi.Kill(encName + "/" + s.Name); }
                    }
                }
                if (t >= nextLog)
                {
                    nextLog += 5f;
                    HaloLabRunner.Log("t=" + t.ToString("0.0") + " s: living " + HaloAi.LivingCount(encName) + " covenant" + (allies != "none" ? ", " + HaloAi.LivingCount(allies) + " allies" : "") + ", bursts " + HaloAi.Shots + ", grenades " + HaloAi.GrenadesThrown + ", melee " + HaloAi.Melees + ", evades " + HaloAi.Evades + ", panics " + HaloAi.Panics + ", deaths " + HaloAi.Deaths + ", paths " + HaloAi.PathCount + ", player hit " + target.Hits + "x (" + target.Taken.ToString("0") + "), " + HaloAi.BudgetLine);
                    foreach (var a in HaloAi.Actors)
                        if (a.Unit != null) HaloLabRunner.Log("   " + a.Label + " " + (a.A != null ? a.A.Type.ToString() : "?") + " " + (a.Alive ? a.State.ToString() : "DEAD") + (a.Target != null ? " -> " + a.Target + (a.TargetVisible ? " (seen)" : "") : "") + " at " + a.Position.ToString("0.0") + (a.Moving ? " going to " + a.Goal.ToString("0.0") : "") + " body " + a.Unit.Body.ToString("0.00") + " shield " + a.Unit.Shield.ToString("0.00") + (a.Unit.Crouching ? " crouching" : "") + (a.Unit.Firing ? " FIRING" : ""));
                }
                if (nextPic < pics.Length && t >= pics[nextPic])
                {
                    nextPic++;
                    r.Aim(behind, centre + Vector3.up * 1f, 75f);
                    yield return null;
                    r.Shot("ai_" + mapName + "_" + (shot) + "_behind");
                    r.Aim(above, centre, 70f);
                    yield return null;
                    r.Shot("ai_" + mapName + "_" + (shot++) + "_above");
                }
            }
            // host hand-over: capture (offline: no net serials), then the follower path without a blob - the units re-adopted
            int livingBefore = 0; var keep = new List<HaloUnit>();
            foreach (var a in HaloAi.Actors) if (a.Alive && a.Unit != null) { livingBefore++; keep.Add(a.Unit); }
            var serials = new Dictionary<int, HaloUnit>();
            for (int k = 0; k < keep.Count; k++) serials[k + 1] = keep[k];
            HaloAi.SerialOfUnit = u => { int s = keep.IndexOf(u); return s >= 0 ? s + 1 : 0; };
            HaloAi.UnitOfSerialFn = s => serials.ContainsKey(s) ? serials[s] : null;
            var blob = HaloAi.CaptureNet();
            HaloLabRunner.Log("hand-over capture: " + (blob != null ? blob.Length : 0) + " bytes for " + livingBefore + " actors");
            foreach (var u in keep) HaloAi.Detach(u);
            HaloAi.AdoptNet(blob);
            int re = 0; foreach (var u in keep) if (HaloAi.IsActor(u)) re++;
            r.Check(re == livingBefore, "hand-over with the blob re-adopted " + re + " of " + livingBefore + " living units");
            foreach (var u in keep) HaloAi.Detach(u);
            HaloAi.AdoptNet(null);
            int re2 = 0; foreach (var u in keep) if (HaloAi.IsActor(u)) re2++;
            r.Check(re2 == livingBefore, "hand-over without a blob re-seeded " + re2 + " of " + livingBefore);
            int shotsBefore = HaloAi.Shots;
            for (int k = 0; k < 300; k++) yield return null;
            HaloLabRunner.Log("after the hand-over: bursts +" + (HaloAi.Shots - shotsBefore) + " in 5 s");
            HaloAi.LogBudget();
            r.Check(HaloAi.Shots > 0, "the AI fired " + HaloAi.Shots + " bursts");
            r.Check(HaloAi.StateEntered(HaloActor.Mode.Combat) > 0, "actors went into combat " + HaloAi.StateEntered(HaloActor.Mode.Combat) + " times");
            r.Check(HaloAi.PathCount > 0, "paths found " + HaloAi.PathCount);
            HaloLabRunner.Log("deaths " + HaloAi.Deaths + ", panics " + HaloAi.Panics + " (flee " + HaloAi.StateEntered(HaloActor.Mode.Flee) + ", cower " + HaloAi.StateEntered(HaloActor.Mode.Cower) + ", hide " + HaloAi.StateEntered(HaloActor.Mode.Hide) + ", search " + HaloAi.StateEntered(HaloActor.Mode.Search) + ", berserk " + HaloAi.Berserks + "), player hit " + target.Hits + " times for " + target.Taken.ToString("0"));
            HaloAi.Clear();
        }
    }
}

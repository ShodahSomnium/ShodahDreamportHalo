using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Unit movement physics labs (workstream 1: floor falls).
    /// "floor" [-halomap a10 | c10] [-halobsp 0]: builds the map's BSP (World collision), finds steep bevels next to walkable floor near the
    /// scenario's actor starting locations (rays at knee height from the floor: faces with a normal z between 0.3 and the biped's maximum
    /// slope), stands 6 marines and 6 grunts (c10: flood) on the floor before them and steers them into the faces with MoveVelocity for 10 s,
    /// drops 2 more from 4 m above faces, then kills half with a body explosion (Kill + AddImpulse). Checks: every live unit and body ends
    /// with its feet within 0.3 m above walkable ground, none more than 0.5 m under its start floor. Logs "below floor: n", "recovered: n".
    /// Pictures floor_site_*_start / _end (side views).
    /// "floor-lift" [-halomap a10]: a flat stage with a kinematic platform (a lift) carrying 3 marines 12 m up while turning, with one
    /// stalled frame (the platform jumps 0.8 m, as a 400 ms frame stall at 2 m/s): all three must arrive on top of it.
    /// </summary>
    [InitializeOnLoadAttribute]
    static class HaloLabFloor
    {
        static HaloLabFloor()
        {
            HaloLabRunner.Register("floor", r => Floor(r));
            HaloLabRunner.Register("floor-lift", r => Lift(r));
        }

        private sealed class Site
        {
            public Vector3 Floor;      // where the unit starts (walkable floor)
            public Vector3 Face;       // the steep face's hit point
            public Vector3 Normal;     // its normal
            public HaloUnit Unit;
            public bool Dropped;       // dropped from above the face
        }

        private static IEnumerator Seconds(float s)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(s * 60f));
            for (int i = 0; i < n; i++) yield return null;
        }

        private static HaloTag FindVariant(HaloMap map, params string[] parts)
        {
            foreach (string part in parts)
                foreach (HaloTag v in map.OfClass("actv"))
                    if (v.Name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0 && v.Root.Ref(20) != null && v.Root.Ref(20).Is("bipd")) return v;
            return null;
        }

        private static bool Walkable(Vector3 n) { return n.y >= 0.7f; }

        /// <summary>Feet within 0.3 m above walkable ground (the ground's height out).</summary>
        private static bool OnGround(Vector3 feet, out float groundY)
        {
            groundY = float.NaN;
            RaycastHit h;
            if (!Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out h, 0.65f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) return false;
            groundY = h.point.y;
            return h.normal.y >= 0.45f && feet.y - h.point.y >= -0.06f && feet.y - h.point.y <= 0.3f;
        }

        private static IEnumerator Floor(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "a10");
            int bsp = int.Parse(HaloLabBoot.Arg("-halobsp", "0"));
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            var map = new HaloMap(entry.File);
            var sc = new HaloScenario(map);
            Time.captureFramerate = 60;
            HaloLocal.Alive = true;
            HaloCombat.LabMap = map; HaloCombat.LabScenario = sc;
            try { HaloFx.Preload(map); } catch (System.Exception e) { HaloLabRunner.Log("fx preload: " + e.Message); }
            try { HaloFootsteps.Load(map); } catch (System.Exception) { }
            var hub = GameObject.Find("Halo Hub"); if (hub != null) hub.SetActive(false);
            var stage = new GameObject("floor lab");
            HaloBipeds.LabParent = stage.transform;
            var sun = new GameObject("sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            HaloWorld.PlaceObjects = false;
            var root = new GameObject("Lab World Root");
            float t0 = Time.realtimeSinceStartup;
            var it = HaloWorld.Build(map, sc, root.transform, null, bsp);
            while (it.MoveNext()) yield return it.Current;
            HaloWorldDriver.View = r.Cam;
            if (!r.Check(HaloWorld.Ready, "bsp " + bsp + " built in " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s")) yield break;
            Physics.SyncTransforms();

            // ---- bevel sites near the actors' starting locations (encounters @1068, squads @128, starting locations @208)
            var starts = new List<Vector3>();
            foreach (var e in sc.Tag.Root.Block(1068, 176))
                foreach (var sq in e.Block(128, 232))
                    foreach (var st in sq.Block(208, 28))
                    {
                        Vector3 p = HaloSpace.Pos(st.Vec3(0));
                        if (!HaloWorld.Outside(p + Vector3.up * 0.3f)) starts.Add(p);
                    }
            HaloLabRunner.Log("floor: " + map.Name + " bsp " + bsp + ": " + starts.Count + " actor starting locations inside the BSP");
            var sites = new List<Site>();
            var used = new List<Vector3>();
            // probe points: each start, and a ring of points 4 / 8 m around it (interiors like a10 have few bevels right at the starts)
            var probes = new List<Vector3>();
            for (int s = 0; s < starts.Count; s += Mathf.Max(1, starts.Count / 60))
            {
                probes.Add(starts[s]);
                for (int ring = 1; ring <= 2; ring++)
                    for (int k = 0; k < 8; k++)
                    {
                        float a = (k * 45f + ring * 22.5f) * Mathf.Deg2Rad;
                        probes.Add(starts[s] + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (4f * ring));
                    }
            }
            for (int s = 0; s < probes.Count && sites.Count < 14; s++)
            {
                RaycastHit fh;
                if (HaloWorld.Outside(probes[s] + Vector3.up * 0.3f)) continue;
                if (!Physics.Raycast(probes[s] + Vector3.up * 0.5f, Vector3.down, out fh, 3f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore) || !Walkable(fh.normal)) continue;
                Vector3 floor = fh.point;
                for (int k = 0; k < 32 && sites.Count < 14; k++)
                {
                    float a = (k & 15) * 22.5f * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    RaycastHit wh;
                    if (!Physics.Raycast(floor + Vector3.up * (k < 16 ? 0.25f : 0.08f), dir, out wh, 10f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) continue;
                    if (wh.normal.y < 0.3f || wh.normal.y > 0.69f) continue;      // a bevel: steeper than any biped's maximum slope (45 deg) but not a wall
                    Vector3 n = new Vector3(wh.normal.x, 0f, wh.normal.z).normalized;
                    Vector3 stand = wh.point + n * 1.2f;
                    RaycastHit sh;
                    if (!Physics.Raycast(stand + Vector3.up * 0.6f, Vector3.down, out sh, 1.5f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore) || !Walkable(sh.normal)) continue;
                    bool near = false;
                    foreach (Vector3 u in used) if ((u - sh.point).sqrMagnitude < 9f) near = true;
                    if (near) continue;
                    used.Add(sh.point);
                    sites.Add(new Site { Floor = sh.point, Face = wh.point, Normal = wh.normal });
                    break;
                }
            }
            HaloLabRunner.Log("floor: " + sites.Count + " bevel sites");
            if (!r.Check(sites.Count >= 6, "at least 6 bevel sites next to walkable floor (" + sites.Count + ")")) yield break;

            HaloTag a1 = FindVariant(map, "marine", "crewman");
            HaloTag a2 = FindVariant(map, "grunt", "floodcombat", "flood");
            HaloTag inf = FindVariant(map, "flood_infection", "infection");
            if (!r.Check(a1 != null && a2 != null, "characters " + (a1 != null ? a1.Name : "-") + " / " + (a2 != null ? a2.Name : "-"))) yield break;
            int walkers = Mathf.Min(12, sites.Count);
            for (int i = 0; i < sites.Count; i++)
            {
                Site s = sites[i];
                Vector3 n = new Vector3(s.Normal.x, 0f, s.Normal.z).normalized;
                bool drop = i >= walkers || i >= 12;
                s.Dropped = drop;
                HaloTag who = i % 2 == 0 ? a1 : a2;
                if (i == sites.Count - 1 && inf != null) who = inf;
                Vector3 at = drop ? s.Face + Vector3.up * 4f + n * 0.2f : s.Floor;
                s.Unit = HaloBipeds.Spawn(who, at, Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg, HaloTeam.None);
                if (s.Unit == null) { HaloLabRunner.Log("  spawn failed: " + who.Name); continue; }
                HaloLabRunner.Log("  site " + i + ": " + who.Name + (drop ? " dropped onto" : " walks into") + " a " + (Mathf.Acos(s.Normal.y) * Mathf.Rad2Deg).ToString("0") + " deg face at " + s.Face.ToString("F1") + ", floor y " + s.Floor.y.ToString("F2") + ", spawned feet " + s.Unit.transform.position.ToString("F2"));
            }
            // two more: dropped onto the first two faces from 4 m
            for (int i = 0; i < 2 && i < sites.Count; i++)
            {
                Site s = sites[i];
                Vector3 n = new Vector3(s.Normal.x, 0f, s.Normal.z).normalized;
                var d = new Site { Floor = s.Floor, Face = s.Face, Normal = s.Normal, Dropped = true };
                d.Unit = HaloBipeds.Spawn(i == 0 ? a2 : a1, s.Face + Vector3.up * 4f + n * 0.15f, 0f, HaloTeam.None);
                if (d.Unit != null) sites.Add(d);
            }

            yield return Seconds(0.5f);
            for (int i = 0; i < Mathf.Min(4, sites.Count); i++) SideShot(r, sites[i], "floor_site_" + i + "_start");
            int rec0 = HaloUnit.RecoveredFromFloor + HaloUnit.RecoveredFromOutside;

            // ---- 10 s steered into the faces
            for (int f = 0; f < 600; f++)
            {
                foreach (Site s in sites)
                {
                    if (s.Unit == null || !s.Unit.Alive) continue;
                    Vector3 n = new Vector3(s.Normal.x, 0f, s.Normal.z).normalized;
                    s.Unit.MoveVelocity = -n * s.Unit.NaturalSpeed;
                    if (f % 60 == 0 && s.Unit.Def.CanClimbAnySurface)
                    {
                        // a climber's path (one line a second): where it is and how steep the face under it is
                        Vector3 p = s.Unit.transform.position;
                        RaycastHit gh;
                        bool any = Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out gh, 1f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore);
                        HaloLabRunner.Log("  climber " + s.Unit.Def.Name + " t " + (f / 60) + " s: feet " + p.ToString("F2") + " grounded " + s.Unit.Grounded + " under it " + (any ? (Mathf.Acos(Mathf.Clamp01(gh.normal.y)) * Mathf.Rad2Deg).ToString("0") + " deg face " + (p.y - gh.point.y).ToString("F2") + " m below" : "nothing within 0.7 m"));
                    }
                }
                yield return null;
            }
            foreach (Site s in sites) if (s.Unit != null) s.Unit.MoveVelocity = Vector3.zero;
            yield return Seconds(0.5f);
            int below = Report(r, sites, "after 10 s into the faces");

            // ---- half of them killed by a body explosion
            int killed = 0;
            for (int i = 0; i < sites.Count; i += 2)
            {
                Site s = sites[i];
                if (s.Unit == null || !s.Unit.Alive) continue;
                Vector3 n = new Vector3(s.Normal.x, 0f, s.Normal.z).normalized;
                s.Unit.Kill();
                s.Unit.AddImpulse(-n * 3f + Vector3.up * 5f);      // thrown up and into the face (the worst case)
                killed++;
            }
            HaloLabRunner.Log("floor: " + killed + " killed with an explosion impulse");
            yield return Seconds(5f);
            below += Report(r, sites, "after the explosions");
            for (int i = 0; i < Mathf.Min(4, sites.Count); i++) SideShot(r, sites[i], "floor_site_" + i + "_end");
            int recovered = HaloUnit.RecoveredFromFloor + HaloUnit.RecoveredFromOutside - rec0;
            HaloLabRunner.Log("floor: below floor: " + below);
            HaloLabRunner.Log("floor: recovered: " + recovered + " (from the floor " + HaloUnit.RecoveredFromFloor + ", from outside " + HaloUnit.RecoveredFromOutside + ", removed outside " + HaloUnit.RemovedOutside + ", landing deaths " + HaloUnit.LandingDeaths + ", pushed out of walls " + HaloUnit.PushedOutOfWalls + ", AI recovery give-ups " + HaloActor.RecoveryGiveUps + ")");
            r.Check(below == 0, "no unit or body ends under its start floor or off walkable ground (" + below + ")");
        }

        /// <summary>Per site: feet on walkable ground (within 0.3 m above it) and not under the start floor; the failures counted.</summary>
        private static int Report(HaloLabRunner r, List<Site> sites, string when)
        {
            int bad = 0, n = 0;
            for (int i = 0; i < sites.Count; i++)
            {
                Site s = sites[i];
                if (s.Unit == null) { HaloLabRunner.Log("  site " + i + ": unit gone"); continue; }
                n++;
                Vector3 feet = s.Unit.transform.position;
                float gy;
                bool on = OnGround(feet, out gy);
                bool under = feet.y < s.Floor.y - 0.5f;
                if (!on || under) bad++;
                HaloLabRunner.Log("  " + when + ": site " + i + " " + s.Unit.Def.Name + (s.Unit.Alive ? "" : " (dead)") + " feet " + feet.ToString("F2") + " grounded " + s.Unit.Grounded + " ground " + (on ? "ok y " + gy.ToString("F2") : "NONE within 0.3 m") + (under ? " UNDER the start floor (" + s.Floor.y.ToString("F2") + ")" : "") + (s.Unit.WasRecovered ? " (recovered)" : ""));
            }

            r.Check(bad == 0, when + ": " + (n - bad) + " of " + n + " units / bodies on walkable ground, none under the floor");
            return bad;
        }

        private static void SideShot(HaloLabRunner r, Site s, string name)
        {
            Vector3 n = new Vector3(s.Normal.x, 0f, s.Normal.z).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, n);
            Vector3 mid = (s.Floor + s.Face) * 0.5f + Vector3.up * 0.8f;
            r.Aim(mid + side * 4.5f + n * 1.5f + Vector3.up * 0.6f, mid, 65f);
            r.Shot(name, 960, 540);
        }

        // ------------------------------------------------------------------ floor-lift

        private static IEnumerator Lift(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "a10");
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            var map = new HaloMap(entry.File);
            Time.captureFramerate = 60;
            HaloLocal.Alive = true;
            HaloCombat.LabMap = map;
            try { HaloFootsteps.Load(map); } catch (System.Exception) { }
            var stage = new GameObject("lift lab");
            HaloBipeds.LabParent = stage.transform;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.SetParent(stage.transform, false);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            floor.layer = HaloLayers.World;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f));
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            var lift = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lift.name = "lift"; lift.transform.SetParent(stage.transform, false);
            lift.transform.localScale = new Vector3(5f, 0.3f, 5f);
            lift.transform.position = new Vector3(0f, 0.15f, 0f);      // top at 0.30 m: a step onto it
            lift.layer = HaloLayers.World;
            var lm = new Material(mat); lm.SetColor("_BaseColor", new Color(0.6f, 0.5f, 0.3f));
            lift.GetComponent<Renderer>().sharedMaterial = lm;
            var rb = lift.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false;
            var sun = new GameObject("sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            Physics.SyncTransforms();
            yield return null;

            HaloTag marine = FindVariant(map, "marine", "crewman");
            if (!r.Check(marine != null, "a marine variant")) yield break;
            var riders = new List<HaloUnit>();
            for (int i = 0; i < 3; i++)
            {
                HaloUnit u = HaloBipeds.Spawn(marine, new Vector3(-1.5f + i * 1.5f, 0.6f, (i - 1) * 0.8f), i * 90f, HaloTeam.Human);
                if (u != null) riders.Add(u);
            }
            if (!r.Check(riders.Count == 3, "three marines on the lift")) yield break;
            yield return Seconds(1f);
            foreach (HaloUnit u in riders) HaloLabRunner.Log("  rider at " + u.transform.position.ToString("F2") + " grounded " + u.Grounded);
            r.Aim(new Vector3(9f, 4f, -9f), new Vector3(0f, 1f, 0f), 60f);
            r.Shot("lift_0_bottom", 960, 540);

            // up 12 m at 2 m/s while turning 10 deg/s; at 3 s one frame jumps 0.8 m (a 400 ms stall's worth)
            float y = 0.15f, yaw = 0f;
            for (int f = 0; f < 6 * 60 + 30; f++)
            {
                float dy = f < 6 * 60 ? 2f / 60f : 0f;
                if (f == 3 * 60) { dy += 0.8f; HaloLabRunner.Log("  stall: the lift jumps 0.8 m in one frame"); }
                y = Mathf.Min(12.15f, y + dy);
                yaw += f < 6 * 60 ? 10f / 60f : 0f;
                lift.transform.SetPositionAndRotation(new Vector3(0f, y, 0f), Quaternion.Euler(0f, yaw, 0f));
                if (f == 2 * 60 || f == 3 * 60 + 2 || f == 5 * 60)
                {
                    r.Aim(new Vector3(9f, y + 3f, -9f), new Vector3(0f, y + 0.8f, 0f), 60f);
                    r.Shot("lift_" + f, 960, 540);
                }
                yield return null;
            }

            yield return Seconds(1f);
            float top = y + 0.15f;
            int ok = 0;
            foreach (HaloUnit u in riders)
            {
                Vector3 p = u.transform.position;
                Vector3 local = lift.transform.InverseTransformPoint(p);
                bool on = Mathf.Abs(p.y - top) < 0.3f && Mathf.Abs(local.x) < 0.5f && Mathf.Abs(local.z) < 0.5f && u.Grounded;
                if (on) ok++;
                HaloLabRunner.Log("  rider at " + p.ToString("F2") + " (lift top " + top.ToString("F2") + ") grounded " + u.Grounded + (on ? " on the lift" : " NOT on the lift"));
            }

            r.Aim(new Vector3(9f, top + 3f, -9f), new Vector3(0f, top + 0.8f, 0f), 60f);
            r.Shot("lift_1_top", 960, 540);
            r.Check(ok == 3, "all three marines arrived on top of the lift (" + ok + ")");
        }
    }
}

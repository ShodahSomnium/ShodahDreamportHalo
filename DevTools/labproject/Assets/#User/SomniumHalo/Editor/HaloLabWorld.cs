using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// World module labs (DevTools/lab.sh world [-halomap bloodgulch] | world-models | world-all):
    /// world: builds a map's world under a root at HaloSpace.MapOrigin (no match), pictures from the first start locations, from above,
    ///   of the sky, close-ups; checks that a ray down from every start location hits the collision and that no start is "outside".
    /// world-models: Blood Gulch plus a warthog, a cyborg and weapons at a start location (model shading, change colour, object light).
    /// world-all: every multiplayer map in turn, one picture each from the first start and a timing line (use sparingly).
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabWorld
    {
        static HaloLabWorld()
        {
            HaloLabRunner.Register("world", r => World(r, MapArg, true));
            HaloLabRunner.Register("world-models", r => Models(r));
            HaloLabRunner.Register("world-all", r => All(r));
            HaloLabRunner.Register("world-look", r => Look(r));
            HaloLabRunner.Register("world-campaign", r => Campaign(r));
        }

        static string MapArg { get { return HaloLabBoot.Arg("-halomap", "bloodgulch"); } }

        static IEnumerator Build(HaloLabRunner r, string mapName, GameObject[] rootOut)
        {
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            float t0 = Time.realtimeSinceStartup;
            var map = new HaloMap(entry.File);
            var scenario = new HaloScenario(map);
            HaloLabRunner.Log("map read in " + (Time.realtimeSinceStartup - t0).ToString("0.00") + " s");
            // the hub (ring scene, its 7 km sky dome) hides itself when a match phase starts
            HaloSession.SetPhase(HaloSession.Phase.Loading);
            yield return null;
            var hub = GameObject.Find("Halo Hub");
            if (hub != null) hub.SetActive(false);
            var root = new GameObject("Lab World Root");
            root.transform.position = HaloSpace.MapOrigin;
            rootOut[0] = root;
            float worst = 0f, last = Time.realtimeSinceStartup;
            var it = HaloWorld.Build(map, scenario, root.transform, null);
            while (true)
            {
                float a = Time.realtimeSinceStartup;
                if (!it.MoveNext()) break;
                worst = Mathf.Max(worst, Time.realtimeSinceStartup - a);
                yield return it.Current;
            }
            HaloLabRunner.Log("world " + mapName + " built in " + (Time.realtimeSinceStartup - t0).ToString("0.00") + " s, worst main-thread step " + (worst * 1000f).ToString("0") + " ms");
            r.Check(HaloWorld.Ready, "world ready");
            yield return null;
            yield return null;
        }

        static Vector3 Eye(HaloScenario.StartLocation s) { return HaloSpace.Pos(s.Position) + HaloSpace.MapOrigin + Vector3.up * 1.7f; }
        static Vector3 Facing(float yaw) { return HaloSpace.Yaw(yaw) * Vector3.forward; }

        static IEnumerator World(HaloLabRunner r, string mapName, bool many)
        {
            var root = new GameObject[1];
            yield return Build(r, mapName, root);
            if (!HaloWorld.Ready) yield break;
            var scenario = new HaloScenario(new HaloMap(r.Game.Install.Map(mapName).File));
            r.Cam.enabled = false;
            HaloWorldDriver.View = r.Cam;

            // walk check
            int hits = 0, outside = 0, n = scenario.StartLocations.Count;
            float worstDrop = 0f;
            foreach (var s in scenario.StartLocations)
            {
                Vector3 p = HaloSpace.Pos(s.Position) + HaloSpace.MapOrigin;
                RaycastHit hit;
                if (Physics.Raycast(p + Vector3.up * 1f, Vector3.down, out hit, 4f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore))
                {
                    hits++; worstDrop = Mathf.Max(worstDrop, Mathf.Abs(hit.point.y - p.y));
                }
                if (HaloWorld.Outside(p + Vector3.up * 0.3f)) outside++;
            }
            r.Check(hits == n, "ray down from every start location hits collision: " + hits + "/" + n + " (worst |hit - start| " + worstDrop.ToString("0.00") + " m)");
            r.Check(outside == 0, "no start location outside the BSP (" + outside + ")");
            r.Check(HaloWorld.Outside(HaloWorld.Bounds.center + Vector3.down * (HaloWorld.Bounds.extents.y + 50f)), "a point far below the map is outside");
            if (n > 0)
            {
                RaycastHit hit;
                Vector3 p = HaloSpace.Pos(scenario.StartLocations[0].Position) + HaloSpace.MapOrigin;
                if (Physics.Raycast(p + Vector3.up, Vector3.down, out hit, 4f, HaloLayers.GroundMask)) HaloLabRunner.Log("start 0 surface material " + HaloWorld.SurfaceMaterial(hit) + " cluster " + HaloWorld.ClusterAt(p + Vector3.up * 0.3f));
                Color c; Vector3 d;
                if (HaloWorld.LightAt(p + Vector3.up * 0.3f, out c, out d)) HaloLabRunner.Log("start 0 lightmap under it " + c + " incident " + d.ToString("0.00"));
            }

            // pictures
            int shots = many ? Mathf.Min(4, n) : Mathf.Min(1, n);
            for (int i = 0; i < shots; i++)
            {
                var s = scenario.StartLocations[i * Mathf.Max(1, n / Mathf.Max(1, shots))];
                Vector3 e = Eye(s);
                r.Aim(e, e + Facing(s.Facing) * 10f, 75f);
                yield return null;
                r.Shot(mapName + "_start" + i);
            }
            Bounds b = HaloWorld.Bounds;
            r.Aim(b.center + new Vector3(-b.extents.x * 0.9f, b.extents.y + 120f, -b.extents.z * 0.9f), b.center, 60f);
            yield return null;
            r.Shot(mapName + "_above");
            if (!many) yield break;
            r.Aim(b.center + new Vector3(0f, b.extents.y * 2f + 300f, 0f), b.center + new Vector3(0f, 0f, 0.1f), 70f);
            yield return null;
            r.Shot(mapName + "_top");
            if (n > 0)
            {
                Vector3 e = Eye(scenario.StartLocations[0]);
                r.Aim(e, e + Facing(scenario.StartLocations[0].Facing) * 3f + Vector3.up * 6f, 80f);
                yield return null;
                r.Shot(mapName + "_sky");
                for (int k = 0; k < 4; k++)
                {
                    Vector3 dir = Quaternion.Euler(0f, 90f * k, 0f) * Facing(scenario.StartLocations[0].Facing);
                    r.Aim(e, e + dir * 10f + Vector3.up * 3f, 90f);
                    yield return null;
                    r.Shot(mapName + "_sky" + k);
                }
                r.Aim(e, e + Facing(scenario.StartLocations[0].Facing) * 2f + Vector3.down * 1.5f, 70f);
                yield return null;
                r.Shot(mapName + "_ground");
                r.Aim(e, e - Facing(scenario.StartLocations[0].Facing) * 10f, 75f);
                yield return null;
                r.Shot(mapName + "_back");
            }
        }

        static IEnumerator Models(HaloLabRunner r)
        {
            var root = new GameObject[1];
            yield return Build(r, "bloodgulch", root);
            if (!HaloWorld.Ready) yield break;
            var map = new HaloMap(r.Game.Install.Map("bloodgulch").File);
            var sc = new HaloScenario(map);
            var s = sc.StartLocations[0];
            Vector3 p = HaloSpace.Pos(s.Position) + HaloSpace.MapOrigin;
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up, Vector3.down, out hit, 4f, HaloLayers.GroundMask)) p = hit.point;
            Vector3 f = Facing(s.Facing);
            Vector3 right = Vector3.Cross(Vector3.up, f).normalized;
            var holder = new GameObject("Lab Models");
            string[] names = { "vehicles\\warthog\\warthog", "characters\\cyborg\\cyborg", "weapons\\assault rifle\\assault rifle", "vehicles\\ghost\\ghost", "weapons\\plasma rifle\\plasma rifle" };
            Vector3[] at = { p + f * 6f, p + f * 3f + right * 2.5f, p + f * 2.5f - right * 1.2f + Vector3.up * 1.0f, p + f * 7f + right * 5.5f, p + f * 2.5f - right * 0.3f + Vector3.up * 1.0f };
            for (int i = 0; i < names.Length; i++)
            {
                var tag = map.Find("mod2", names[i]);
                if (!r.Check(tag != null, "model " + names[i])) continue;
                var go = new GameObject(names[i]);
                go.transform.SetParent(holder.transform, false);
                go.transform.position = at[i];
                go.transform.rotation = Quaternion.LookRotation(-right, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
                var b = HaloModelBuilder.Build(tag, go.transform);
                r.Check(b != null && (b.Mesh != null), "built " + names[i]);
                if (b != null && b.Mesh != null)
                {
                    if (i == 1) HaloMaterials.SetChangeColor(b.Mesh, new Color(0.8f, 0.1f, 0.1f));
                }
            }
            yield return null;
            Vector3 eye = p + Vector3.up * 1.7f;
            r.Aim(eye, p + f * 4f + Vector3.up * 0.8f, 70f);
            yield return null; yield return null;
            r.Shot("models_front");
            r.Aim(p + f * 4f + right * 6f + Vector3.up * 2.5f, p + f * 4.5f + Vector3.up * 0.6f, 60f);
            yield return null;
            r.Shot("models_side");
            r.Aim(p + f * 2f + Vector3.up * 1.6f - right * 0.2f, p + f * 2.5f - right * 0.8f + Vector3.up * 1.0f, 40f);
            yield return null;
            r.Shot("models_weapons");
        }

        /// <summary>world-look -halomap name -haloviews "x,y,z,yawdeg,pitchdeg;..." (Halo units / degrees): one picture per view.</summary>
        static IEnumerator Look(HaloLabRunner r)
        {
            var root = new GameObject[1];
            yield return Build(r, MapArg, root);
            if (!HaloWorld.Ready) yield break;
            HaloWorldDriver.View = r.Cam;
            string views = HaloLabBoot.Arg("-haloviews", "");
            int i = 0;
            foreach (var v in views.Split(';'))
            {
                var f = v.Split(',');
                if (f.Length < 5) continue;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                Vector3 eye = HaloSpace.Pos(float.Parse(f[0], inv), float.Parse(f[1], inv), float.Parse(f[2], inv)) + HaloSpace.MapOrigin;
                float yaw = float.Parse(f[3], inv) * Mathf.Deg2Rad, pitch = float.Parse(f[4], inv) * Mathf.Deg2Rad;
                Vector3 dir = HaloSpace.Dir(Mathf.Cos(yaw) * Mathf.Cos(pitch), Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch));
                r.Aim(eye, eye + dir * 10f, 75f);
                for (int k = 0; k < 3; k++) yield return null;
                r.Shot(MapArg + "_view" + i++);
            }
        }

        /// <summary>
        /// world-campaign -halomap a10 [-haloswitch 1] [-haloviews ...]: builds the start BSP (the first start location's), pictures from the
        /// start, waits for the background build of every BSP (worst frame time logged), switches to BSP -haloswitch, checks a ray down at a
        /// point of that BSP hits its collision, pictures there, memory used, devices opened.
        /// </summary>
        static IEnumerator Campaign(HaloLabRunner r)
        {
            string mapName = MapArg;
            var entry = FindCampaign(r, mapName) ?? r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName)) yield break;
            HaloSession.SetPhase(HaloSession.Phase.Loading);
            yield return null;
            var hub = GameObject.Find("Halo Hub");
            if (hub != null) hub.SetActive(false);
            long mem0 = System.GC.GetTotalMemory(false);
            float t0 = Time.realtimeSinceStartup;
            var map = new HaloMap(entry.File);
            var sc = new HaloScenario(map);
            int start = sc.StartLocations.Count > 0 ? Mathf.Clamp(sc.StartLocations[0].BspIndex, 0, sc.Bsps.Count - 1) : 0;
            HaloLabRunner.Log(mapName + ": " + sc.Bsps.Count + " BSPs, start BSP " + start + ", map read in " + (Time.realtimeSinceStartup - t0).ToString("0.00") + " s");
            var root = new GameObject("Lab World Root");
            root.transform.position = HaloSpace.MapOrigin;
            float worst = 0f;
            var it = HaloWorld.Build(map, sc, root.transform, null, start);
            while (true)
            {
                float a = Time.realtimeSinceStartup;
                if (!it.MoveNext()) break;
                worst = Mathf.Max(worst, Time.realtimeSinceStartup - a);
                yield return it.Current;
            }
            r.Check(HaloWorld.Ready, "start BSP ready in " + (Time.realtimeSinceStartup - t0).ToString("0.00") + " s (worst step " + (worst * 1000f).ToString("0") + " ms)");
            HaloWorldDriver.View = r.Cam;
            if (sc.StartLocations.Count > 0)
            {
                var s = sc.StartLocations[0];
                Vector3 p = HaloSpace.Pos(s.Position) + HaloSpace.MapOrigin;
                RaycastHit hit;
                r.Check(Physics.Raycast(p + Vector3.up, Vector3.down, out hit, 4f, HaloLayers.GroundMask), "ray down at the player start hits collision");
                r.Check(!HaloWorld.Outside(p + Vector3.up * 0.3f), "player start inside the BSP");
                Vector3 e = Eye(s);
                for (int k = 0; k < 3; k++)
                {
                    r.Aim(e, e + Quaternion.Euler(0f, 120f * k, 0f) * Facing(s.Facing) * 10f, 80f);
                    for (int f = 0; f < 4; f++) yield return null;
                    r.Shot(mapName + "_start" + k);
                }
            }
            yield return LookViews(r, mapName + "_bsp" + start);
            // the other BSPs build in the background: frame times
            float tb = Time.realtimeSinceStartup, worstFrame = 0f;
            int frames = 0, slow = 0, gc0 = System.GC.CollectionCount(0);
            var slowList = new System.Text.StringBuilder();
            while (Time.realtimeSinceStartup - tb < 300f)
            {
                bool all = true;
                for (int i = 0; i < HaloWorld.BspCount; i++) if (!HaloWorld.BspBuilt(i)) all = false;
                if (all) break;
                float fa = Time.realtimeSinceStartup;
                yield return null;
                float ft = Time.realtimeSinceStartup - fa;
                worstFrame = Mathf.Max(worstFrame, ft);
                if (ft > 0.025f) { slow++; if (slowList.Length < 400) slowList.Append((ft * 1000f).ToString("0")).Append("ms@").Append((Time.realtimeSinceStartup - tb).ToString("0.0")).Append(" "); }
                frames++;
            }
            bool built = true;
            for (int i = 0; i < HaloWorld.BspCount; i++) if (!HaloWorld.BspBuilt(i)) built = false;
            HaloLabRunner.Log("incremental GC: " + UnityEngine.Scripting.GarbageCollector.isIncremental);
            HaloLabRunner.Log("background build: " + slow + " frames over 25 ms: " + slowList + "; GC collections " + (System.GC.CollectionCount(0) - gc0));
            r.Check(built, "all " + HaloWorld.BspCount + " BSPs built in the background in " + (Time.realtimeSinceStartup - tb).ToString("0.0") + " s over " + frames + " frames, worst frame " + (worstFrame * 1000f).ToString("0") + " ms");
            HaloLabRunner.Log("managed memory +" + ((System.GC.GetTotalMemory(false) - mem0) / (1024 * 1024)) + " MB, Unity total allocated " + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)) + " MB");
            int target = int.Parse(HaloLabBoot.Arg("-haloswitch", HaloWorld.BspCount > 1 ? ((start + 1) % HaloWorld.BspCount).ToString() : "0"));
            float ts = Time.realtimeSinceStartup;
            bool now = HaloWorld.SwitchBsp(target);
            yield return r.Wait(() => HaloWorld.ActiveBsp == target, 60f, "the switch to BSP " + target);
            r.Check(HaloWorld.ActiveBsp == target, "switched to BSP " + target + (now ? " at once" : " after building") + " in " + ((Time.realtimeSinceStartup - ts) * 1000f).ToString("0") + " ms");
            yield return null;
            yield return r.Wait(() => HaloWorld.PathData(target) != null, 30f, "the path data of BSP " + target);
            var pd = HaloWorld.PathData(target);
            if (r.Check(pd != null && pd.WalkableCount > 0, "path data of BSP " + target + ": " + (pd != null ? pd.Surfaces.Count + " surfaces, " + pd.WalkableCount + " walkable" : "none")))
            {
                // the largest walkable surface with a ceiling above it (inside the level, not on the hull)
                HaloPathData.Surface best = null;
                foreach (var ps in pd.Surfaces)
                {
                    if (!ps.Walkable || (best != null && Area(ps) <= Area(best))) continue;
                    Vector3 c = HaloSpace.Pos(ps.Cx, ps.Cy, ps.Cz) + HaloSpace.MapOrigin + Vector3.up * 0.5f;
                    if (Physics.Raycast(c, Vector3.up, 8f, HaloLayers.GroundMask)) best = ps;
                }
                if (best == null) foreach (var ps in pd.Surfaces) if (ps.Walkable && (best == null || Area(ps) > Area(best))) best = ps;
                Vector3 p = HaloSpace.Pos(best.Cx, best.Cy, best.Cz) + HaloSpace.MapOrigin;
                RaycastHit hit;
                r.Check(Physics.Raycast(p + Vector3.up, Vector3.down, out hit, 3f, HaloLayers.GroundMask), "ray down in BSP " + target + " hits its collision");
                Vector3 e = p + Vector3.up * 1.7f;
                for (int k = 0; k < 2; k++)
                {
                    r.Aim(e, e + Quaternion.Euler(0f, 180f * k, 0f) * Vector3.forward * 10f, 80f);
                    for (int f = 0; f < 4; f++) yield return null;
                    r.Shot(mapName + "_bsp" + target + "_" + k);
                }
            }
            yield return LookViews(r, mapName + "_after" + target, "-haloviews2");
            int animated = 0;
            foreach (var d in HaloWorld.Devices) if (d.Animated) { animated++; d.SetPosition(1f); }
            HaloLabRunner.Log("devices " + HaloWorld.Devices.Count + " (" + animated + " with a position animation, all set to 1), placed objects " + HaloWorld.Placed.Count);
        }

        static float Area(HaloPathData.Surface s)
        {
            float a = 0f; int n = s.Points.Length / 3;
            for (int k = 0; k < n; k++) { int p = k * 3, q = ((k + 1) % n) * 3; a += s.Points[p] * s.Points[q + 1] - s.Points[q] * s.Points[p + 1]; }
            return Mathf.Abs(a) * 0.5f;
        }

        static HaloInstall.MapEntry FindCampaign(HaloLabRunner r, string name)
        {
            foreach (var e in r.Game.Install.Campaign) if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(e.File), name, System.StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        static IEnumerator LookViews(HaloLabRunner r, string prefix, string arg = "-haloviews")
        {
            string views = HaloLabBoot.Arg(arg, "");
            int i = 0;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var v in views.Split(';'))
            {
                var f = v.Split(',');
                if (f.Length < 5) continue;
                Vector3 eye = HaloSpace.Pos(float.Parse(f[0], inv), float.Parse(f[1], inv), float.Parse(f[2], inv)) + HaloSpace.MapOrigin;
                float yaw = float.Parse(f[3], inv) * Mathf.Deg2Rad, pitch = float.Parse(f[4], inv) * Mathf.Deg2Rad;
                Vector3 dir = HaloSpace.Dir(Mathf.Cos(yaw) * Mathf.Cos(pitch), Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch));
                r.Aim(eye, eye + dir * 10f, 75f);
                for (int k = 0; k < 4; k++) yield return null;
                r.Shot(prefix + "_view" + i++);
            }
        }

        static IEnumerator All(HaloLabRunner r)
        {
            foreach (var e in r.Game.Install.Multiplayer)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(e.File);
                yield return World(r, name, false);
                HaloWorld.Unload();
                yield return null;
            }
        }
    }
}

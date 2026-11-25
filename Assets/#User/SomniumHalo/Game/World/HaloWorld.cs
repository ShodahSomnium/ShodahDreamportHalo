using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [World module] Builds and tears down the map world: the structure BSPs (lightmapped surfaces with the game's shaders, one combined
    /// mesh per shader through a lightmap atlas per BSP), their collision (MeshColliders on HaloLayers.World, one per material type),
    /// the sky and atmosphere (HaloWorldLook), the placed objects (scenery, machines, controls, light fixtures, sound scenery with their
    /// models, collision models and attachments) and the BSP extras (lens flares, decals, weather, detail objects: HaloWorldExtras).
    /// <see cref="Build"/> is a coroutine: the START BSP first (worker-thread parsing and texture decoding, Unity objects within ~8 ms a
    /// frame), then <see cref="Ready"/> - and every other BSP of a campaign map keeps building in the background (one worker job at a
    /// time, ~3 ms of main thread a frame, built inactive). Halo shows ONE BSP at a time: <see cref="SwitchBsp"/> swaps render,
    /// collision, sky / fog and the BSP-scoped objects in one frame (or as soon as that BSP is built).
    /// Queries: <see cref="SurfaceMaterial"/>, <see cref="Outside"/>, <see cref="ClusterAt"/>, <see cref="LightAt"/>, <see cref="PathData"/>;
    /// devices for the Mission module: <see cref="Devices"/>. Player.log: "World: ..." lines.
    /// </summary>
    public static class HaloWorld
    {
        public static bool Ready { get; private set; }
        /// <summary>Bounds of the active BSP in Unity space (valid when Ready).</summary>
        public static Bounds Bounds { get; private set; }
        /// <summary>The active structure BSP (null when no world).</summary>
        public static HaloBsp Bsp { get; private set; }
        /// <summary>The root of the built world (child of the root given to Build).</summary>
        public static Transform Root { get; private set; }
        /// <summary>Realtime shadows of objects (off: the lightmapped world does not receive them).</summary>
        public static bool ObjectShadows = false;
        /// <summary>The active BSP index (-1 none).</summary>
        public static int ActiveBsp { get; private set; } = -1;
        public static int BspCount { get { return _slots != null ? _slots.Length : 0; } }
        internal static HaloWorldExtras ActiveExtras { get { return _slots != null && ActiveBsp >= 0 && ActiveBsp < _slots.Length ? _slots[ActiveBsp].Extras : null; } }
        /// <summary>A BSP became active (index). Raised on the main thread.</summary>
        public static event Action<int> BspActivated;
        /// <summary>Every device (machines, then controls) the world built, for the Mission module.</summary>
        public static readonly List<HaloWorldDevice> Devices = new List<HaloWorldDevice>();
        /// <summary>Every placed object the world built: (kind, scenario index) -> root, with its BSP mask.</summary>
        public static readonly List<PlacedObject> Placed = new List<PlacedObject>();
        /// <summary>Place the scenario's scenery / devices / light fixtures / sound scenery (multiplayer: yes). The Mission module places the
        /// campaign's objects itself by name: it sets this false before Build (saves the time).</summary>
        public static bool PlaceObjects = true;

        public sealed class PlacedObject
        {
            public string Kind, Name; public int Index; public GameObject Root; public int BspMask; public bool Hidden;
        }

        sealed class Slot
        {
            public int Index;
            public HaloWorldJob Job;
            public GameObject Go;
            public Texture2D Atlas;
            public bool Built, Building;
            public Bounds Bounds;
            public volatile HaloPathData Path;
            public bool PathBuilding;
            public readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
            public HaloWorldExtras Extras;
            public readonly Dictionary<int, Breakable> Breakables = new Dictionary<int, Breakable>();
        }

        sealed class Breakable
        {
            public int Slot, Index, MaterialType = -1;
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public readonly List<Collider> Colliders = new List<Collider>();
            public float Health = -1f;
            public bool Broken;
            public Vector3 Centre;
        }

        static readonly Dictionary<int, Breakable> BreakableByCollider = new Dictionary<int, Breakable>();

        static Slot[] _slots;
        static HaloMap _map;
        static HaloScenario _scenario;
        static HaloLightGrid _light;
        static Vector3 _origin;
        static int _pending = -1;
        internal static string _phase = "";
        static readonly Dictionary<int, int> ColliderMaterial = new Dictionary<int, int>();
        static readonly HashSet<int> Climbable = new HashSet<int>();
        static readonly List<HaloFx.Attachments> Attached = new List<HaloFx.Attachments>();
        static readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();
        static GameObject _built;
        static HaloWorldDriver _driver;
        static int _generation;

        /// <summary>Builds the map from BSP 0 (multiplayer).</summary>
        public static IEnumerator Build(HaloMap map, HaloScenario scenario, Transform root, Action<float, string> progress)
        {
            return Build(map, scenario, root, progress, 0);
        }

        /// <summary>Builds the start BSP and the placed objects under <paramref name="root"/> over several frames (Ready at the end), then
        /// the other BSPs keep building in the background; <paramref name="progress"/>(0..1, label).</summary>
        public static IEnumerator Build(HaloMap map, HaloScenario scenario, Transform root, Action<float, string> progress, int startBsp)
        {
            Unload();
            int gen = ++_generation;
            float t0 = Time.realtimeSinceStartup;
            if (map == null || scenario == null || scenario.Bsps.Count == 0) { HaloLog.Warn("World: nothing to build (no map / BSP)"); yield break; }
            if (startBsp < 0 || startBsp >= scenario.Bsps.Count) startBsp = 0;
            _map = map; _scenario = scenario;
            _slots = new Slot[scenario.Bsps.Count];
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new Slot { Index = i };
            Report(progress, 0f, "reading the map");
            var first = _slots[startBsp];
            first.Job = new HaloWorldJob(map, scenario, startBsp, true);
            first.Job.Start();
            while (!first.Job.Done) { Report(progress, first.Job.Progress * 0.45f, first.Job.Stage); yield return null; }
            if (gen != _generation) yield break;
            if (first.Job.Error != null) { HaloLog.Error("World: preparing " + map.Name + " BSP " + startBsp + " failed: " + first.Job.Error); yield break; }
            float tJob = Time.realtimeSinceStartup;

            _built = new GameObject("Halo World " + map.Name);
            _built.transform.SetParent(root, false);
            _built.transform.position = root != null ? root.position : Vector3.zero;
            Root = _built.transform;
            _origin = Root.position;

            yield return BuildSlot(first, 0.008f, progress, 0.45f, 0.3f, gen);
            if (gen != _generation) yield break;
            float tBsp = Time.realtimeSinceStartup;

            // placed objects of every BSP (shown by BSP membership)
            if (PlaceObjects) yield return PlaceAll(progress, gen);
            if (gen != _generation) yield break;
            float tObjects = Time.realtimeSinceStartup;

            _driver = _built.AddComponent<HaloWorldDriver>();
            Activate(startBsp);
            Ready = true;
            Report(progress, 1f, "world ready");
            HaloLog.Info("World: " + map.Name + " ready: BSP " + startBsp + "/" + _slots.Length + " " + first.Job.Bsp.Tag.Name + ", " + Placed.Count + " objects (" + Devices.Count +
                         " devices); worker " + first.Job.Milliseconds + " ms, BSP " + Ms(tBsp - tJob) + " ms, objects " + Ms(tObjects - tBsp) + " ms, total " + Ms(Time.realtimeSinceStartup - t0) + " ms");
            if (_slots.Length > 1) _driver.StartCoroutine(BuildRest(gen));
        }

        /// <summary>Background: every other BSP (the pending switch target first), one worker job at a time, ~3 ms a frame.</summary>
        static IEnumerator BuildRest(int gen)
        {
            float t0 = Time.realtimeSinceStartup;
            for (;;)
            {
                if (gen != _generation) yield break;
                Slot next = null;
                if (_pending >= 0 && !_slots[_pending].Built) next = _slots[_pending];
                if (next == null) foreach (var s in _slots) if (!s.Built) { next = s; break; }
                if (next == null) break;
                if (next.Job == null) { next.Job = new HaloWorldJob(_map, _scenario, next.Index, false); next.Job.Start(); }
                while (!next.Job.Done) yield return null;
                if (gen != _generation) yield break;
                if (next.Job.Error != null) { HaloLog.Error("World: preparing BSP " + next.Index + " failed: " + next.Job.Error); next.Built = true; continue; }
                // stepped here to log the longest main-thread step (VR frame budget)
                var steps = BuildSlot(next, _pending == next.Index ? 0.008f : 0.003f, null, 0f, 0f, gen);
                float worst = 0f; string worstAt = "";
                for (;;)
                {
                    float a = Time.realtimeSinceStartup;
                    _phase = "";
                    bool more = steps.MoveNext();
                    float dt = Time.realtimeSinceStartup - a;
                    if (dt > worst) { worst = dt; worstAt = _phase; }
                    if (!more) break;
                    yield return steps.Current;
                }
                if (worst > 0.012f) HaloLog.Info("World: BSP " + next.Index + " background build: longest main-thread step " + Ms(worst) + " ms (" + worstAt + ")");
                if (gen != _generation) yield break;
                if (_pending == next.Index) { _pending = -1; Activate(next.Index); }
            }
            HaloLog.Info("World: all " + _slots.Length + " BSPs of " + _map.Name + " built in the background (" + Ms(Time.realtimeSinceStartup - t0) + " ms)");
        }

        /// <summary>Makes a BSP the active one: true when done now, false when it is still building (it activates when built).</summary>
        public static bool SwitchBsp(int index)
        {
            if (_slots == null || index < 0 || index >= _slots.Length) { HaloLog.Warn("World: SwitchBsp(" + index + ") - no such BSP"); return false; }
            if (index == ActiveBsp) return true;
            if (!_slots[index].Built || _slots[index].Building) { _pending = index; HaloLog.Info("World: BSP " + index + " still building - switching when ready"); return false; }
            _pending = -1;
            Activate(index);
            return true;
        }

        /// <summary>True when a BSP's Unity objects exist (a switch to it is instant).</summary>
        public static bool BspBuilt(int index) { return _slots != null && index >= 0 && index < _slots.Length && _slots[index].Built && !_slots[index].Building; }

        static void Activate(int index)
        {
            var s = _slots[index];
            if (s.Go == null) return;
            float t0 = Time.realtimeSinceStartup;
            if (ActiveBsp >= 0 && ActiveBsp < _slots.Length && _slots[ActiveBsp].Go != null) _slots[ActiveBsp].Go.SetActive(false);
            s.Go.SetActive(true);
            int old = ActiveBsp;
            ActiveBsp = index;
            Bsp = s.Job.Bsp;
            _light = s.Job.Light;
            Bounds = s.Bounds;
            HaloWorldLook.Apply(s.Job, Root);
            int bit = 1 << Mathf.Min(index, 30);
            foreach (var p in Placed)
            {
                bool show = !p.Hidden && (p.BspMask == 0 || (p.BspMask & bit) != 0);
                if (p.Root != null && p.Root.activeSelf != show) p.Root.SetActive(show);
            }
            if (_driver != null) _driver.Init(s.Job);
            HaloObjectLight.Clear();
            HaloLog.Info("World: BSP " + old + " -> " + index + " (" + s.Job.Bsp.Tag.Name + ") active in " + Ms(Time.realtimeSinceStartup - t0) + " ms");
            var h = BspActivated;
            if (h != null) foreach (Action<int> d in h.GetInvocationList()) { try { d(index); } catch (Exception e) { HaloLog.Error("World: BspActivated listener: " + e); } }
        }

        /// <summary>The Unity objects of one BSP (render, collision, extras), built inactive unless it is the first.</summary>
        static IEnumerator BuildSlot(Slot slot, float budgetSeconds, Action<float, string> progress, float p0, float pSpan, int gen)
        {
            slot.Building = true;
            var job = slot.Job;
            var budget = new Budget(budgetSeconds);
            float t0 = Time.realtimeSinceStartup;
            slot.Go = new GameObject("BSP " + slot.Index + " " + System.IO.Path.GetFileName(job.Bsp.Tag.Name));
            slot.Go.SetActive(false);
            slot.Go.transform.SetParent(Root, false);

            _phase = "atlas";
            Texture2D atlas = null;
            if (job.Atlas != null && job.Atlas.Rects.Count > 0)
            {
                atlas = new Texture2D(job.Atlas.Width, job.Atlas.Height, TextureFormat.RGBA32, false, true)
                { name = "lightmap atlas " + job.Bsp.Tag.Name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                atlas.LoadRawTextureData(job.Atlas.Rgba);
                atlas.Apply(false, true);
                slot.Owned.Add(atlas);
                slot.Atlas = atlas;
            }

            _phase = "surfaces";
            var bspGo = new GameObject("Structure");
            bspGo.transform.SetParent(slot.Go.transform, false);
            int drawn = 0, tris = 0;
            for (int i = 0; i < job.Render.Count; i++)
            {
                var p = job.Render[i];
                _phase = p.Shader.Name;
                if (progress != null) Report(progress, p0 + pSpan * 0.7f * i / Mathf.Max(1, job.Render.Count), "surfaces");
                if (budget.Over()) { yield return null; budget.Reset(); if (gen != _generation) yield break; }
                Material mat = p.Lightmapped ? HaloMaterials.ForBsp(p.Shader, atlas) : HaloMaterials.Get(p.Shader);
                var mesh = new Mesh { name = System.IO.Path.GetFileName(p.Shader.Name) };
                if (p.Positions.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(p.Positions);
                mesh.SetNormals(p.Normals);
                mesh.SetTangents(p.Tangents);
                mesh.SetUVs(0, p.Uv);
                mesh.SetUVs(1, p.LightUv);
                mesh.SetUVs(2, p.Binormals);
                mesh.SetUVs(3, p.Incident);
                var extras = HaloMaterials.ExtraLayers(p.Shader);
                mesh.subMeshCount = 1 + extras.Count;
                for (int k = 0; k <= extras.Count; k++) mesh.SetTriangles(p.Triangles, k, k == 0);
                mesh.UploadMeshData(true);
                slot.Owned.Add(mesh);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(bspGo.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mats = new Material[1 + extras.Count];
                mats[0] = mat; for (int k = 0; k < extras.Count; k++) mats[k + 1] = extras[k];
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                drawn++; tris += p.Triangles.Count / 3;
                if (p.Breakable >= 0) BreakableOf(slot, p.Breakable).Renderers.Add(mr);
            }
            float tRender = Time.realtimeSinceStartup;

            // collision: meshes on the main thread, PhysX cooking on a worker (Physics.BakeMesh), then the colliders (no main-thread cooking)
            _phase = "collision";
            if (progress != null) Report(progress, p0 + pSpan * 0.75f, "collision");
            var colGo = new GameObject("Collision");
            colGo.transform.SetParent(slot.Go.transform, false);
            colGo.layer = HaloLayers.World;
            var colMeshes = new List<Mesh>();
            var pieces = new List<HaloCollision.Piece>();
            int colTris = 0;
            foreach (var piece in job.Collision)
            {
                if (piece.Triangles.Count < 3) continue;
                if (budget.Over()) { yield return null; budget.Reset(); if (gen != _generation) yield break; }
                var mesh = HaloCollision.ToMesh(piece, "collision " + piece.MaterialType);
                slot.Owned.Add(mesh);
                colMeshes.Add(mesh); pieces.Add(piece);
                colTris += piece.Triangles.Count / 3;
            }
            var ids = new int[colMeshes.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = colMeshes[i].GetInstanceID();
            bool baked = false; string bakeError = null;
            var bake = new Thread(() =>
            {
                try { for (int i = 0; i < ids.Length; i++) Physics.BakeMesh(ids[i], false); }
                catch (Exception e) { bakeError = e.Message; }
                baked = true;
            }) { IsBackground = true, Name = "Halo collision bake" };
            bake.Start();
            while (!baked) yield return null;
            if (gen != _generation) yield break;
            if (bakeError != null) HaloLog.Warn("World: collision baking on a worker failed (" + bakeError + ") - cooking on the main thread");
            for (int i = 0; i < colMeshes.Count; i++)
            {
                if (budget.Over()) { yield return null; budget.Reset(); if (gen != _generation) yield break; }
                var piece = pieces[i];
                var go = new GameObject("collision " + piece.MaterialType + (piece.Climbable ? " climbable" : "") + (piece.Invisible ? " invisible" : ""));
                go.layer = HaloLayers.World;
                go.transform.SetParent(colGo.transform, false);
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = colMeshes[i];
                Register(mc, piece.MaterialType, piece.Climbable);
                if (piece.BreakableIndex >= 0)
                {
                    var br = BreakableOf(slot, piece.BreakableIndex);
                    br.Colliders.Add(mc);
                    br.MaterialType = piece.MaterialType;
                    BreakableByCollider[mc.GetInstanceID()] = br;
                }
                if (piece.Climbable) MakeClimbable(go);
            }
            float tCollision = Time.realtimeSinceStartup;

            // extras: lens flares, decals, weather, detail objects
            _phase = "extras";
            slot.Extras = new HaloWorldExtras();
            yield return slot.Extras.Build(job, _scenario, slot.Go.transform, budget, slot.Owned);
            if (gen != _generation) yield break;

            // bounds (Unity space); an empty BSP (d40 space) has inverted bounds
            var b = job.Bsp;
            if (b.BoundsMinX <= b.BoundsMaxX && b.BoundsMinZ <= b.BoundsMaxZ)
            {
                Vector3 a = HaloSpace.Pos(b.BoundsMinX, b.BoundsMinY, b.BoundsMinZ) + _origin, c = HaloSpace.Pos(b.BoundsMaxX, b.BoundsMaxY, b.BoundsMaxZ) + _origin;
                var bounds = new Bounds((a + c) * 0.5f, Vector3.zero);
                bounds.Encapsulate(a); bounds.Encapsulate(c);
                slot.Bounds = bounds;
            }
            else slot.Bounds = new Bounds(_origin, Vector3.zero);

            HaloLog.Info("World: BSP " + slot.Index + " " + b.Tag.Name + " built: " + drawn + " surface meshes (" + tris + " triangles, " + b.Sections.Count + " sections, lightmap atlas " +
                         (atlas != null ? atlas.width + "x" + atlas.height : "none") + "), collision " + pieces.Count + " pieces / " + colTris + " triangles" + (bakeError == null ? " (cooked on a worker)" : "") +
                         ", " + slot.Extras.Summary + "; worker " + job.Milliseconds + " ms, render " + Ms(tRender - t0) + " ms, collision " + Ms(tCollision - tRender) +
                         " ms; bounds " + slot.Bounds.min.ToString("0") + ".." + slot.Bounds.max.ToString("0") + (b.Problems.Count > 0 ? "; problems: " + string.Join("; ", b.Problems.ToArray()) : ""));
            job.ReleaseArrays();
            slot.Building = false;
            slot.Built = true;
        }

        static string Ms(float s) { return (s * 1000f).ToString("0"); }

        static void Report(Action<float, string> progress, float f, string label)
        {
            if (progress == null) return;
            try { progress(f, label); } catch (Exception e) { HaloLog.Warn("World: progress callback: " + e.Message); }
        }

        internal sealed class Budget
        {
            float _start = Time.realtimeSinceStartup;
            readonly float _limit;
            public Budget(float seconds) { _limit = seconds; }
            public bool Over() { return Time.realtimeSinceStartup - _start > _limit; }
            public void Reset() { _start = Time.realtimeSinceStartup; }
        }

        // ------------------------------------------------------------------ placed objects

        static IEnumerator PlaceAll(Action<float, string> progress, int gen)
        {
            var sc = _scenario;
            var lists = new[] { sc.Scenery, sc.Machines, sc.Controls, sc.LightFixtures, sc.SoundScenery };
            var kinds = new[] { "scenery", "machine", "control", "light fixture", "sound scenery" };
            // the BSP membership bits of scenery / light fixtures (+32 "bsp indices"); others: which BSPs contain the position
            var bits = new Dictionary<string, List<int>>();
            bits["scenery"] = ReadBits(sc, 528, 72);
            bits["light fixture"] = ReadBits(sc, 708, 88);
            var bsps = new HaloBsp[sc.Bsps.Count];
            var objGo = new GameObject("Objects");
            objGo.transform.SetParent(Root, false);
            var budget = new Budget(0.008f);
            int total = 0; foreach (var l in lists) total += l.Count;
            int done = 0;
            for (int li = 0; li < lists.Length; li++)
            {
                var list = lists[li];
                List<int> b;
                bits.TryGetValue(kinds[li], out b);
                for (int i = 0; i < list.Count; i++)
                {
                    if (budget.Over()) { yield return null; budget.Reset(); if (gen != _generation) yield break; Report(progress, 0.75f + 0.25f * done / Mathf.Max(1, total), kinds[li]); }
                    done++;
                    var pl = list[i];
                    int mask = b != null && i < b.Count ? b[i] : 0;
                    if (mask == 0 && sc.Bsps.Count > 1) mask = Containing(pl.Position, bsps);
                    try { Place(pl, kinds[li], i, objGo.transform, mask); }
                    catch (Exception e) { HaloLog.Warn("World: placing " + kinds[li] + " " + pl + " failed: " + e.Message); }
                }
            }
        }

        static List<int> ReadBits(HaloScenario sc, int blockAt, int stride)
        {
            var l = new List<int>();
            foreach (var s in sc.Tag.Root.Block(blockAt, stride)) l.Add(s.U16(32));
            return l;
        }

        /// <summary>Bits of the BSPs whose leaves contain a Halo point (0 = none: shown in every BSP).</summary>
        static int Containing(HVec3 p, HaloBsp[] cache)
        {
            int m = 0;
            for (int i = 0; i < cache.Length && i < 31; i++)
            {
                HaloBsp b = cache[i];
                if (b == null)
                {
                    b = _slots[i].Job != null && _slots[i].Job.Bsp != null ? _slots[i].Job.Bsp : null;
                    if (b == null) { try { b = new HaloBsp(_scenario, i); } catch (Exception) { continue; } }
                    cache[i] = b;
                }
                if (b.ClusterAt(p.X, p.Y, p.Z + 0.05f) >= 0) m |= 1 << i;
            }
            return m;
        }

        /// <summary>A placed object: its model (static scenery; skinned devices), collision model and attachments.</summary>
        static void Place(HaloScenario.Placement pl, string kind, int index, Transform parent, int mask)
        {
            if (pl.Type == null) return;
            var def = new HaloObjectDef(pl.Type);
            bool device = kind == "machine" || kind == "control";
            string name = pl.NameIndex >= 0 && pl.NameIndex < _scenario.ObjectNames.Count ? _scenario.ObjectNames[pl.NameIndex] : null;
            var go = new GameObject(kind + " " + index + " " + (name ?? System.IO.Path.GetFileName(pl.Type.Name)));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = HaloSpace.Pos(pl.Position);
            go.transform.localRotation = ObjectRotation(pl.Rotation);
            HaloModelBuilder.Built built = null;
            if (def.Model != null)
            {
                var opt = new HaloModelBuilder.Options { Static = !device, CastShadows = false };
                built = HaloModelBuilder.Build(def.Model, go.transform, opt);
                if (built != null && built.Mesh != null)
                {
                    var cc = def.PickColor(0, index * 31 + pl.PaletteIndex);
                    if (cc != null) HaloMaterials.SetChangeColor(built.Mesh, new Color(cc[0], cc[1], cc[2]));
                }
            }
            if (def.CollisionModel != null)
            {
                var cols = HaloCollision.AddModelColliders(def.CollisionModel, built, go.transform, device ? HaloLayers.World : HaloLayers.Environment);
                foreach (var mc in cols) Owned.Add(mc.sharedMesh);
                if (device && cols.Count > 0)
                {
                    // moving colliders: a kinematic body so PhysX moves them as bodies (and carries what stands on them)
                    var rb = go.AddComponent<Rigidbody>();
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
            }
            try { Attached.Add(HaloFx.Attach(pl.Type, built != null ? built.Root.transform : go.transform)); }
            catch (Exception e) { HaloLog.Warn("World: attachments of " + pl.Type + " failed: " + e.Message); }
            bool hidden = (pl.NotPlaced & 1) != 0 && kind != "sound scenery" && _scenario.Bsps.Count > 1;   // "not placed: automatically" (campaign: placed by script)
            Placed.Add(new PlacedObject { Kind = kind, Name = name, Index = index, Root = go, BspMask = mask, Hidden = hidden });
            if (hidden) go.SetActive(false);
            if (device) Devices.Add(new HaloWorldDevice { Kind = kind, Index = index, Placement = pl, Def = def, Root = go, Model = built, Name = name ?? go.name, BspMask = mask });
            if (!string.IsNullOrEmpty(name)) HaloObjects.Register(name, go);
        }

        // ------------------------------------------------------------------ breakable surfaces (glass panes ...)

        static Breakable BreakableOf(Slot slot, int index)
        {
            Breakable b;
            if (!slot.Breakables.TryGetValue(index, out b))
            {
                b = new Breakable { Slot = slot.Index, Index = index };
                var bs = slot.Job.Bsp.BreakableSurfaces;
                b.Centre = (index >= 0 && index < bs.Count ? HaloSpace.Pos(bs[index].Centroid) : Vector3.zero) + _origin;
                slot.Breakables[index] = b;
            }
            return b;
        }

        /// <summary>Script breakable_surfaces_enable: false = no surface breaks (they still take hits).</summary>
        public static bool BreakableSurfacesEnabled = true;
        static TagReader _globalsMaterials;
        static int _globalsMaterialCount = -1;

        /// <summary>The globals tag's material entry (+404, 884 B) of a material type: breakable surface vitality / effect / sound.</summary>
        static bool GlobalsMaterial(int type, out TagReader m)
        {
            m = default(TagReader);
            if (_globalsMaterialCount < 0 && _map != null)
            {
                _globalsMaterialCount = 0;
                foreach (var t in _map.OfClass("matg")) { var r = t.Root; _globalsMaterialCount = r.Count(404); if (_globalsMaterialCount > 0) _globalsMaterials = r.Element(404, 0, 884); break; }
            }
            if (type < 0 || type >= _globalsMaterialCount) return false;
            m = new TagReader(_globalsMaterials.Map, _globalsMaterials.At + type * 884, _globalsMaterials.Magic);
            return true;
        }

        /// <summary>True when a hit collider is a breakable BSP surface that is still whole.</summary>
        public static bool IsBreakable(RaycastHit hit)
        {
            Breakable b;
            return hit.collider != null && BreakableByCollider.TryGetValue(hit.collider.GetInstanceID(), out b) && !b.Broken;
        }

        /// <summary>
        /// Damage on a BSP surface (Combat calls it for every bullet / projectile / melee hit on world collision): a breakable surface
        /// (glass) loses vitality - the globals material of its collision material ("breakable surface" maximum vitality, Halo default 1
        /// when 0) - and breaks at zero: its render mesh and collision go, the material's breaking effect and sound play at the hit.
        /// Returns true when this hit broke it.
        /// </summary>
        public static bool DamageSurface(RaycastHit hit, float damage)
        {
            Breakable b;
            if (hit.collider == null || !BreakableByCollider.TryGetValue(hit.collider.GetInstanceID(), out b) || b.Broken) return false;
            TagReader gm;
            bool have = GlobalsMaterial(b.MaterialType, out gm);
            if (b.Health < 0f) b.Health = have && gm.F32(724) > 0f ? gm.F32(724) : 1f;
            if (!BreakableSurfacesEnabled) return false;
            b.Health -= Mathf.Max(0f, damage);
            if (b.Health > 0f) return false;
            Break(b, hit.point, hit.normal);
            return true;
        }

        static void Break(Breakable b, Vector3 point, Vector3 normal)
        {
            b.Broken = true;
            foreach (var r in b.Renderers) if (r != null) r.enabled = false;
            foreach (var c in b.Colliders) if (c != null) c.enabled = false;
            TagReader gm;
            if (GlobalsMaterial(b.MaterialType, out gm))
            {
                HaloTag effect = gm.Ref(740), sound = gm.Ref(756);
                try { if (effect != null) HaloFx.Play(effect, point, Quaternion.LookRotation(normal.sqrMagnitude > 0.5f ? normal : Vector3.up)); } catch (Exception e) { HaloLog.Warn("World: breaking effect: " + e.Message); }
                try { if (sound != null) HaloAudio.Play(sound, point); } catch (Exception e) { HaloLog.Warn("World: breaking sound: " + e.Message); }
            }
            HaloLog.Info("World: breakable surface " + b.Index + " of BSP " + b.Slot + " broke (material " + b.MaterialType + ")");
        }

        /// <summary>Breaks a breakable surface by BSP and index (scripts / net replay). False when no such surface.</summary>
        public static bool BreakSurface(int bsp, int index)
        {
            Breakable b;
            if (_slots == null || bsp < 0 || bsp >= _slots.Length || !_slots[bsp].Breakables.TryGetValue(index, out b) || b.Broken) return false;
            Break(b, b.Centre, Vector3.up);
            return true;
        }

        /// <summary>Script breakable_surfaces_enable (all surfaces of the level breakable or not).</summary>
        public static void BreakableSurfacesEnable(bool on) { BreakableSurfacesEnabled = on; }

        /// <summary>Script breakable_surfaces_reset: every broken surface whole again (render + collision, full vitality).</summary>
        public static void BreakableSurfacesReset()
        {
            if (_slots == null) return;
            foreach (var s in _slots)
                foreach (var b in s.Breakables.Values)
                {
                    b.Broken = false; b.Health = -1f;
                    foreach (var r in b.Renderers) if (r != null) r.enabled = true;
                    foreach (var c in b.Colliders) if (c != null) c.enabled = true;
                }
        }

        /// <summary>Breakable surfaces built (all BSPs) and how many are broken - for logs / labs.</summary>
        public static int BreakableCount(out int broken)
        {
            int n = 0; broken = 0;
            if (_slots != null) foreach (var s in _slots) foreach (var b in s.Breakables.Values) { n++; if (b.Broken) broken++; }
            return n;
        }

        /// <summary>A device by kind ("machine" / "control") and scenario index, null when none.</summary>
        public static HaloWorldDevice Device(string kind, int index)
        {
            foreach (var d in Devices) if (d.Index == index && d.Kind == kind) return d;
            return null;
        }

        /// <summary>A Halo placement rotation (yaw, pitch, roll in radians) as the Unity rotation of a model built by HaloModelBuilder
        /// (model +x forward, +y left, +z up in Halo terms): forward / left / up are rotated as the game does.</summary>
        public static Quaternion ObjectRotation(HVec3 ypr)
        {
            float cy = Mathf.Cos(ypr.X), sy = Mathf.Sin(ypr.X), cp = Mathf.Cos(ypr.Y), sp = Mathf.Sin(ypr.Y), cr = Mathf.Cos(ypr.Z), sr = Mathf.Sin(ypr.Z);
            var l0 = new HVec3(-sy, cy, 0f);
            var u0 = new HVec3(-cy * sp, -sy * sp, cp);
            // roll about forward
            var l = new HVec3(l0.X * cr + u0.X * sr, l0.Y * cr + u0.Y * sr, l0.Z * cr + u0.Z * sr);
            var u = new HVec3(u0.X * cr - l0.X * sr, u0.Y * cr - l0.Y * sr, u0.Z * cr - l0.Z * sr);
            return Quaternion.LookRotation(HaloSpace.Dir(l), HaloSpace.Dir(u));
        }

        /// <summary>Remembers a collider's material type (and the climbable flag) for <see cref="SurfaceMaterial"/>.</summary>
        public static void Register(Collider c, int materialType, bool climbable)
        {
            if (c == null) return;
            ColliderMaterial[c.GetInstanceID()] = materialType;
            if (climbable) Climbable.Add(c.GetInstanceID());
        }

        public static void Unload()
        {
            _generation++;
            Ready = false;
            foreach (var a in Attached) { try { HaloFx.Detach(a); } catch (Exception) { } }
            Attached.Clear();
            if (_slots != null)
                foreach (var s in _slots)
                {
                    if (s.Extras != null) s.Extras.Release();
                    foreach (var o in s.Owned) if (o != null) UnityEngine.Object.Destroy(o);
                    s.Owned.Clear();
                }
            _slots = null;
            if (_built != null) UnityEngine.Object.Destroy(_built);
            _built = null;
            _driver = null;
            Root = null;
            foreach (var o in Owned) if (o != null) UnityEngine.Object.Destroy(o);
            Owned.Clear();
            foreach (var p in Placed) if (!string.IsNullOrEmpty(p.Name)) HaloObjects.Unregister(p.Name);
            Placed.Clear();
            Devices.Clear();
            BreakableByCollider.Clear();
            BreakableSurfacesEnabled = true;
            _globalsMaterialCount = -1;
            ColliderMaterial.Clear();
            Climbable.Clear();
            HaloWorldLook.Restore();
            HaloObjectLight.Clear();
            Bsp = null;
            _light = null;
            _map = null; _scenario = null;
            ActiveBsp = -1;
            _pending = -1;
        }

        /// <summary>The pathfinding data of a BSP: null until ready - the first call starts building it on a worker (poll again; ~1 s).</summary>
        public static HaloPathData PathData(int bspIndex)
        {
            if (_slots == null || bspIndex < 0 || bspIndex >= _slots.Length) return null;
            var s = _slots[bspIndex];
            if (s.Path != null) return s.Path;
            if (s.Job == null || !s.Job.Done || s.Job.Bsp == null || s.PathBuilding) return null;
            // built on a worker at the first request (tens of thousands of small objects: kept off the load)
            s.PathBuilding = true;
            var bsp = s.Job.Bsp;
            var t = new Thread(() => { try { s.Path = new HaloPathData(bsp); } catch (Exception e) { HaloLog.Warn("World: path data of BSP " + s.Index + " failed: " + e.Message); } })
            { IsBackground = true, Name = "Halo path data" };
            t.Start();
            return null;
        }

        /// <summary>The structure BSP data of any BSP of the map (null while not prepared).</summary>
        public static HaloBsp BspData(int bspIndex)
        {
            if (_slots == null || bspIndex < 0 || bspIndex >= _slots.Length) return null;
            var j = _slots[bspIndex].Job;
            return j != null && j.Done ? j.Bsp : null;
        }

        /// <summary>Halo material type (MaterialType enum: 0 dirt, 1 sand, 2 stone, 3 snow, 4 wood, 5 metal hollow, 6 metal thin, 7 metal thick,
        /// 8 rubber, 9 glass, 10 force field, ..., 19 water ...) of the surface a ray hit, -1 = unknown.</summary>
        public static int SurfaceMaterial(RaycastHit hit)
        {
            if (hit.collider == null) return -1;
            int t;
            return ColliderMaterial.TryGetValue(hit.collider.GetInstanceID(), out t) ? t : -1;
        }

        static int _climbables;
        static Type _climbType;
        static bool _climbLooked;

        /// <summary>
        /// Halo's ladders (collision surfaces flagged "climbable") become Somnium climbable surfaces: the Bridge's SomniumClimbableSurface
        /// control component on the collider's object, which the client turns into its own VR climbing (ClimbBasedOnTeleportInteractable
        /// on the object's non-trigger colliders, interaction layers Climbing + Teleport). The type is looked up by its public Bridge name
        /// at run time, so the upload compile never depends on it (no world has used it on the upload server yet). Does nothing in the
        /// editor (no client).
        /// </summary>
        public static void MakeClimbable(GameObject go)
        {
            if (go == null) return;
            if (!_climbLooked)
            {
                _climbLooked = true;
                try { _climbType = Type.GetType("SomniumSpace.Bridge.Components.SomniumClimbableSurface, SomniumSpace.Bridge"); }
                catch (Exception) { _climbType = null; }
                HaloLog.Info("World: Somnium climbable surfaces " + (_climbType != null ? "available - Halo's ladders get one" : "not in this client's Bridge - ladders stay plain collision"));
            }
            if (_climbType == null || go.GetComponent(_climbType) != null) return;
            try
            {
                go.AddComponent(_climbType);
                if (_climbables++ == 0) HaloLog.Info("World: ladders - '" + go.name + "' (and every other climbable Halo surface) is a Somnium climbable surface");
            }
            catch (Exception e)
            {
                HaloLog.Warn("World: making '" + go.name + "' climbable failed: " + e.Message);
            }
        }

        /// <summary>True when the hit surface is climbable (Halo ladders).</summary>
        public static bool IsClimbable(RaycastHit hit) { return hit.collider != null && Climbable.Contains(hit.collider.GetInstanceID()); }

        /// <summary>True when a Unity-space point is outside the active structure BSP (the void around the map: Halo kills objects there).
        /// False when no world is built.</summary>
        public static bool Outside(Vector3 p)
        {
            var b = Bsp;
            if (b == null || !Ready) return false;
            Vector3 h = HaloSpace.ToHalo(p - _origin);
            return b.ClusterAt(h.x, h.y, h.z) == -1;
        }

        /// <summary>The cluster of the active BSP at a Unity-space point (-1 outside, -2 unknown).</summary>
        public static int ClusterAt(Vector3 p)
        {
            var b = Bsp;
            if (b == null) return -2;
            Vector3 h = HaloSpace.ToHalo(p - _origin);
            return b.ClusterAt(h.x, h.y, h.z);
        }

        /// <summary>The lightmap colour (gamma) and incident light direction (Unity) of the ground under a Unity-space point.</summary>
        public static bool LightAt(Vector3 p, out Color colour, out Vector3 incident)
        {
            colour = Color.white; incident = Vector3.up;
            var g = _light;
            if (g == null) return false;
            Vector3 h = HaloSpace.ToHalo(p - _origin);
            var s = g.At(h.x, h.y, h.z);
            if (!s.Found) return false;
            colour = new Color(s.R, s.G, s.B, 1f);
            incident = HaloSpace.Dir(s.Dx, s.Dy, s.Dz);
            return true;
        }
    }
}

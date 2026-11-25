using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A frame where an effect part is created: position plus Halo's local axes (forward, left, up) in Unity world space, and the transform
    /// it may stay attached to. Halo local (x forward, y left, z up) -> world = F*x + L*y + U*z.
    /// </summary>
    internal struct FxFrameW
    {
        public Vector3 Pos, F, L, U;
        public Transform Attach;
        public Vector3 Local(HVec3 v) { return F * v.X + L * v.Y + U * v.Z; }
    }

    /// <summary>One playing effect (pooled).</summary>
    internal sealed class FxRun
    {
        public bool Alive;
        public int Gen;
        public FxEffectDef Def;
        public Transform Attach;
        public bool Attached;
        public Vector3 LocalPos, WorldPos;
        public Quaternion LocalRot, WorldRot;
        public Vector3 Normal, Incident;
        public bool FirstPerson, Looping, StopRequested;
        public int Event;
        public float T, Delay, Duration;
        public bool Started, Skipped;
        public int[] Emitted = new int[8];
        public int[] Target = new int[8];
        public float LastLightEnd;
        /// <summary>The top-level effect tag this run belongs to (nested effects keep their parent's), and the unit that created it
        /// (HaloCombatFx.Play with a creator; else the unit the effect hangs on): for the damage parts (jpt!).</summary>
        public HaloTag RootTag;
        public HaloUnit Creator;
    }

    /// <summary>
    /// The effects runtime: a hidden object under HaloGame that plays effect tags (events, parts, particles), simulates the particle pool,
    /// particle systems, contrails, decals and lights, and rebuilds the batch meshes once per frame (LateUpdate, after everything moved).
    /// Every list is preallocated / pooled: no allocation per frame once the pools have grown to the match's needs.
    /// </summary>
    internal sealed partial class HaloFxRuntime : MonoBehaviour
    {
        public static HaloFxRuntime Instance { get; private set; }

        internal HaloFxLibrary Lib;
        internal FxRender Render;
        readonly List<FxRun> _runs = new List<FxRun>();
        readonly Stack<FxRun> _freeRuns = new Stack<FxRun>();
        int _gen = 1;
        int _depth;
        float _dt;
        FxRun _nestParent;      // the run creating a nested effect part ("effe" part) right now

        /// <summary>The unit creating the next top-level effect (HaloCombatFx.Play with a creator sets it around the call).</summary>
        internal static HaloUnit NextCreator;

        // statistics (HaloFx.Stats)
        internal int StatPlays, StatParticlesSpawned, StatMissing;
        static readonly HashSet<uint> LoggedTags = new HashSet<uint>();

        public static HaloFxRuntime Ensure()
        {
            if (Instance != null) return Instance;
            var parent = HaloGame.Current != null ? HaloGame.Current.transform : null;
            var go = new GameObject("Halo Fx");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            Instance = go.AddComponent<HaloFxRuntime>();
            return Instance;
        }

        void Awake()
        {
            Render = new FxRender(transform);
            InitParticles();
            InitLights();
        }

        void OnDestroy()
        {
            ClearAll();
            if (Render != null) Render.Destroy();
            DestroyLights();
            if (Instance == this) Instance = null;
        }

        internal void UseMap(HaloMap map)
        {
            if (map == null) return;
            if (Lib != null && Lib.Map == map) return;
            ClearAll();
            if (Render != null) Render.Destroy();
            Lib = HaloFxLibrary.For(map);
        }

        internal void ClearAll()
        {
            for (int i = 0; i < _runs.Count; i++) { _runs[i].Alive = false; _freeRuns.Push(_runs[i]); }
            _runs.Clear();
            ClearParticles();
            ClearTrails();
            ClearDecals();
            ClearLights();
            ClearFlares();
        }

        // ------------------------------------------------------------------ starting effects

        internal FxRun Begin(HaloTag effect, Vector3 pos, Quaternion rot, Transform attach, Vector3 normal, Vector3 incident, bool firstPerson, bool loop)
        {
            if (effect == null) return null;
            UseMap(effect.Map);
            var def = Lib.Effect(effect);
            if (def == null) { Missing(effect, "not an effect"); return null; }
            if (_depth > 4) return null; // nested effects (particle death / collision effects) cannot recurse forever
            FxRun r = _freeRuns.Count > 0 ? _freeRuns.Pop() : new FxRun();
            r.Alive = true; r.Gen = ++_gen; r.Def = def; r.Attach = attach; r.Attached = attach != null;
            r.WorldPos = pos; r.WorldRot = rot;
            if (attach != null) { r.LocalPos = attach.InverseTransformPoint(pos); r.LocalRot = Quaternion.Inverse(attach.rotation) * rot; }
            Vector3 fwd = rot * Vector3.forward;
            r.Normal = normal.sqrMagnitude > 1e-8f ? normal.normalized : fwd;
            r.Incident = incident.sqrMagnitude > 1e-8f ? incident.normalized : -fwd;
            r.FirstPerson = firstPerson; r.Looping = loop && def.Loops; r.StopRequested = false;
            r.Event = -1; r.LastLightEnd = 0f;
            if (_nestParent != null) { r.RootTag = _nestParent.RootTag; r.Creator = _nestParent.Creator; }
            else { r.RootTag = effect; r.Creator = NextCreator; }
            if (r.Emitted.Length < def.MaxEmits) { r.Emitted = new int[def.MaxEmits]; r.Target = new int[def.MaxEmits]; }
            _runs.Add(r);
            StatPlays++;
            NextEvent(r, 0);
            // the first event usually has no delay: create it now so a muzzle flash is there this very frame
            _depth++;
            try { Step(r, 0f); } finally { _depth--; }
            return r;
        }

        internal void Missing(HaloTag t, string why)
        {
            StatMissing++;
            if (t != null && LoggedTags.Add(t.Id)) HaloLog.Warn("Fx: " + t + " skipped (" + why + ")");
        }

        void NextEvent(FxRun r, int index)
        {
            var d = r.Def;
            if (r.Looping && !r.StopRequested && d.LoopStop >= 0 && index > d.LoopStop) index = d.LoopStart;
            if (index >= d.Events.Length) { r.Alive = false; return; }
            r.Event = index;
            var ev = d.Events[index];
            r.T = 0f;
            r.Delay = Rand(ev.DelayMin, ev.DelayMax);
            r.Duration = Rand(ev.DurMin, ev.DurMax);
            r.Started = false;
            r.Skipped = ev.Skip > 0f && UnityEngine.Random.value < ev.Skip;
            for (int i = 0; i < ev.Emits.Length; i++) { r.Emitted[i] = 0; r.Target[i] = 0; }
        }

        void UpdateRuns(float dt)
        {
            for (int i = _runs.Count - 1; i >= 0; i--)
            {
                var r = _runs[i];
                if (r.Alive) Step(r, dt);
                if (!r.Alive) { r.Creator = null; r.RootTag = null; _runs.RemoveAt(i); _freeRuns.Push(r); }
            }
        }

        /// <summary>Advances one run: waits the event's delay, creates its parts once, emits its particles over its duration.</summary>
        void Step(FxRun r, float dt)
        {
            int guard = 0;
            float left = dt;
            while (r.Alive && guard++ < 64)
            {
                if (r.Attached && r.Attach == null)
                {
                    // the object it hung on is gone
                    if (r.Def.DeletedWhenDetached) { r.Alive = false; return; }
                    r.Attached = false;
                    if (r.Looping) r.StopRequested = true; // an attachment loop ends with its object
                }
                if (r.Attached) { r.WorldPos = r.Attach.TransformPoint(r.LocalPos); r.WorldRot = r.Attach.rotation * r.LocalRot; }
                var ev = r.Def.Events[r.Event];
                r.T += left; left = 0f;
                if (r.T < r.Delay) return;
                float into = r.T - r.Delay;
                if (!r.Skipped)
                {
                    if (!r.Started) { r.Started = true; CreateParts(r, ev); }
                    Emit(r, ev, r.Duration > 0f ? Mathf.Clamp01(into / r.Duration) : 1f);
                }
                if (into < r.Duration) return;
                // event over: carry the remaining time into the next one
                left = into - r.Duration;
                bool wraps = r.Looping && !r.StopRequested && r.Event >= r.Def.LoopStop;
                NextEvent(r, r.Event + 1);
                if (wraps)
                {
                    // Halo runs a loop pass per game tick: never faster than 30 a second (a zero-length loop would spin)
                    r.Delay = Mathf.Max(r.Delay, 1f / 30f);
                    return;
                }
            }
        }

        // ------------------------------------------------------------------ locations

        static readonly string[] NoLocations = new string[0];

        internal bool Frame(FxRun r, int location, out FxFrameW f)
        {
            f = new FxFrameW();
            Quaternion q = r.WorldRot;
            f.Pos = r.WorldPos; f.F = q * Vector3.forward; f.U = q * Vector3.up; f.L = q * Vector3.left;
            f.Attach = r.Attached ? r.Attach : null;
            string name = location >= 0 && location < r.Def.Locations.Length ? r.Def.Locations[location] : null;
            if (string.IsNullOrEmpty(name)) return true;
            switch (name)
            {
                case "normal": Point(ref f, r.Normal); return true;
                case "gravity": Point(ref f, Vector3.down); return true;
                case "up": Point(ref f, Vector3.up); return true;
                // Halo's incident vector points back where the projectile came from: "negative incident" is its travel (into the
                // surface - bullet hole decals project along it). r.Incident holds the travel direction.
                case "incident": Point(ref f, -r.Incident); return true;
                case "negative incident": Point(ref f, r.Incident); return true;
                case "reflection": Point(ref f, Vector3.Reflect(r.Incident, r.Normal)); return true;
            }
            if (r.Attached)
            {
                Transform m = HaloFxMarkers.Find(r.Attach, name);
                if (m != null) { FromTransform(m, ref f); return true; }
            }
            return true;
        }

        static void Point(ref FxFrameW f, Vector3 forward)
        {
            if (forward.sqrMagnitude < 1e-8f) return;
            forward.Normalize();
            Vector3 up = f.U - forward * Vector3.Dot(f.U, forward);
            if (up.sqrMagnitude < 1e-6f) { up = Vector3.up - forward * forward.y; if (up.sqrMagnitude < 1e-6f) up = Vector3.forward - forward * forward.z; }
            up.Normalize();
            f.F = forward; f.U = up;
            f.L = Vector3.Cross(forward, up); // Unity left-handed: forward x up = left
        }

        /// <summary>A transform's Halo axes. HaloModelBuilder markers ("marker:...") keep Halo's local frame: +X forward, +Y up, +Z left.
        /// Any other transform is read the Unity way (+Z forward, +Y up).</summary>
        internal static void FromTransform(Transform t, ref FxFrameW f)
        {
            f.Pos = t.position; f.Attach = t;
            if (HaloFxMarkers.IsMarker(t)) { f.F = t.right; f.U = t.up; f.L = t.forward; }
            else { f.F = t.forward; f.U = t.up; f.L = -t.right; }
        }

        // ------------------------------------------------------------------ parts

        void CreateParts(FxRun r, FxEventDef ev)
        {
            for (int i = 0; i < ev.Parts.Length; i++)
            {
                var p = ev.Parts[i];
                if (p.CreateIn == 2 || p.CreateIn == 3 || p.Violence == 2) continue; // water / space only, non-violent only
                FxFrameW f;
                if (!Frame(r, p.Location, out f)) continue;
                try { CreatePart(r, p, ref f); }
                catch (Exception e) { HaloLog.Warn("Fx: part " + p.Tag + " of " + r.Def.Tag + " failed: " + e.Message); }
            }
        }

        void CreatePart(FxRun r, FxPartDef p, ref FxFrameW f)
        {
            switch (p.Class)
            {
                case "snd!":
                    if (f.Attach != null) HaloAudio.Play(p.Tag, f.Attach);
                    else HaloAudio.Play(p.Tag, f.Pos);
                    return;
                case "lsnd":
                    return; // looping sounds belong to attachments (HaloFx.Attach), not to one-shot events
                case "ligh":
                {
                    var l = Lib.Light(p.Tag);
                    if (l != null) SpawnLight(l, f.Pos, f.Attach, Mathf.Max(r.Duration, 0f));
                    return;
                }
                case "deca":
                {
                    var d = Lib.Decal(p.Tag);
                    if (d == null) return;
                    Vector3 dir = p.FaceDown ? Vector3.down : f.F;
                    float reach = Mathf.Max(0.1f, p.VelMax) * HaloSpace.Unit; // the part's velocity is how far the decal is projected (world units)
                    float scale = p.RadModMax > 0f ? Rand(p.RadModMin, p.RadModMax) : 1f;
                    ProjectDecal(d, f.Pos, dir, reach, scale);
                    return;
                }
                case "pctl":
                {
                    var s = Lib.System(p.Tag);
                    if (s != null) StartSystem(s, ref f, r.FirstPerson);
                    return;
                }
                case "effe":
                {
                    FxRun parent = _nestParent;
                    _nestParent = r;
                    _depth++;
                    try { Begin(p.Tag, f.Pos, Quaternion.LookRotation(f.F, f.U), f.Attach, r.Normal, r.Incident, r.FirstPerson, false); }
                    finally { _depth--; _nestParent = parent; }
                    return;
                }
                case "jpt!":
                {
                    // Halo applies every damage part of an effect at the part's location, the effect's creator as the cause. Combat decides
                    // (world owner only; projectile detonation effects are skipped - their blast is the projectile's own)
                    HaloUnit creator = r.Creator;
                    if (creator == null && r.Attach != null) creator = r.Attach.GetComponentInParent<HaloUnit>();
                    HaloCombat.EffectDamage(p.Tag, f.Pos, r.RootTag, creator);
                    return;
                }
                case "cdmg":
                    return; // continuous damage: not done
                default:
                    if (p.Tag.Is("obje"))
                    {
                        Vector3 v = Cone(f.F, p.Cone * 0.5f) * // the full apex angle (see SpawnEffectParticle)
                             Rand(p.VelMin, p.VelMax) * HaloSpace.Unit;
                        HaloFx.RaiseObjectSpawned(p.Tag, f.Pos, v);
                    }
                    return;
            }
        }

        // ------------------------------------------------------------------ the frame

        void Update()
        {
            _dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (_dt <= 0f) return;
            try { UpdateRuns(_dt); } catch (Exception e) { HaloLog.Warn("Fx: effects update failed: " + e); }
            try { UpdateSystems(_dt); } catch (Exception e) { HaloLog.Warn("Fx: systems update failed: " + e); }
            try { UpdateParticles(_dt); } catch (Exception e) { HaloLog.Warn("Fx: particles update failed: " + e); }
            try { UpdateTrails(_dt); } catch (Exception e) { HaloLog.Warn("Fx: contrails update failed: " + e); }
            try { UpdateDecals(_dt); } catch (Exception e) { HaloLog.Warn("Fx: decals update failed: " + e); }
            try { UpdateLights(_dt); } catch (Exception e) { HaloLog.Warn("Fx: lights update failed: " + e); }
            try { UpdateFlares(_dt); } catch (Exception e) { HaloLog.Warn("Fx: lens flares update failed: " + e); }
        }

        void LateUpdate() { Draw(); }

        /// <summary>Rebuilds every batch (call again after moving the camera by hand, e.g. the lab).</summary>
        internal void Draw()
        {
            if (Render == null || !Render.Ready) return;
            Vector3 eye = EyePosition();
            Render.BeginAll();
            try { DrawParticles(eye); } catch (Exception e) { HaloLog.Warn("Fx: drawing particles failed: " + e); }
            try { DrawTrails(); } catch (Exception e) { HaloLog.Warn("Fx: drawing contrails failed: " + e); }
            try { DrawDecals(); } catch (Exception e) { HaloLog.Warn("Fx: drawing decals failed: " + e); }
            try { DrawFlares(eye); } catch (Exception e) { HaloLog.Warn("Fx: drawing lens flares failed: " + e); }
            Render.EndAll();
        }

        static Vector3 EyePosition()
        {
            Camera c = HaloFx.ViewCamera != null ? HaloFx.ViewCamera : Camera.main;
            return c != null ? c.transform.position : Vector3.zero;
        }

        // ------------------------------------------------------------------ helpers

        internal static float Rand(float a, float b) { return a == b ? a : a + (b - a) * UnityEngine.Random.value; }
        internal static int RandInt(int a, int b) { return b <= a ? a : UnityEngine.Random.Range(a, b + 1); }

        /// <summary>A random direction within <paramref name="cone"/> radians of <paramref name="axis"/>.</summary>
        internal static Vector3 Cone(Vector3 axis, float cone)
        {
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.up;
            axis.Normalize();
            if (cone <= 1e-5f) return axis;
            float c = Mathf.Cos(Mathf.Min(cone, Mathf.PI));
            float z = c + (1f - c) * UnityEngine.Random.value;
            float phi = UnityEngine.Random.value * 2f * Mathf.PI;
            float s = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            Vector3 a = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 b = Vector3.Cross(axis, a);
            return axis * z + a * (s * Mathf.Cos(phi)) + b * (s * Mathf.Sin(phi));
        }

        internal static Color32 ToColor32(float r, float g, float b, float a)
        {
            return new Color32((byte)(Mathf.Clamp01(r) * 255f + 0.5f), (byte)(Mathf.Clamp01(g) * 255f + 0.5f), (byte)(Mathf.Clamp01(b) * 255f + 0.5f), (byte)(Mathf.Clamp01(a) * 255f + 0.5f));
        }

        internal int RunCount { get { return _runs.Count; } }

        internal bool IsAlive(FxRun r, int gen) { return r != null && r.Alive && r.Gen == gen; }
    }

    /// <summary>Marker lookup under an attached object ("marker:name" children made by HaloModelBuilder), cached per root.</summary>
    internal static class HaloFxMarkers
    {
        static readonly Dictionary<int, Dictionary<string, Transform>> Cache = new Dictionary<int, Dictionary<string, Transform>>();
        static readonly Dictionary<int, bool> IsMarkerCache = new Dictionary<int, bool>();

        public static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            int id = root.GetInstanceID();
            Dictionary<string, Transform> map;
            if (!Cache.TryGetValue(id, out map) || (map.Count > 0 && AnyDead(map)))
            {
                map = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
                Collect(root, map);
                Cache[id] = map;
                if (Cache.Count > 512) Cache.Clear();
            }
            Transform t;
            if (map.TryGetValue(name, out t) && t != null) return t;
            return null;
        }

        static bool AnyDead(Dictionary<string, Transform> map)
        {
            foreach (var kv in map) { if (kv.Value == null) return true; break; }
            return false;
        }

        static void Collect(Transform t, Dictionary<string, Transform> map)
        {
            string n = t.name;
            if (n.StartsWith("marker:", StringComparison.Ordinal))
            {
                string key = n.Substring(7);
                int hash = key.IndexOf('#');
                if (hash >= 0) key = key.Substring(0, hash);
                if (!map.ContainsKey(key)) map[key] = t;
            }
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), map);
        }

        public static bool IsMarker(Transform t)
        {
            int id = t.GetInstanceID();
            bool v;
            if (IsMarkerCache.TryGetValue(id, out v)) return v;
            v = t.name.StartsWith("marker:", StringComparison.Ordinal);
            if (IsMarkerCache.Count > 4096) IsMarkerCache.Clear();
            IsMarkerCache[id] = v;
            return v;
        }

        public static void Forget() { Cache.Clear(); IsMarkerCache.Clear(); }
    }
}

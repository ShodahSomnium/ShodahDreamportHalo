using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Mission module, K] The campaign's named objects: every scenario placement (scenery, bipeds, vehicles, weapons, equipment, machines,
    /// controls, light fixtures, sound scenery) is addressable by its scenario object name (object names block) as scripts do
    /// (object_create "door_1", object_destroy, unit "keyes", device_set_position ...). Modules that build an object register it here.
    /// Creation by kind: bipeds through Characters (HaloBipeds.Spawn), vehicles through Vehicles, weapons / equipment through Combat's
    /// pickups (HaloPickups.PlaceAt), scenery / devices / light fixtures / sound scenery here (models, collision and attachments with the
    /// World module's builders; devices animated by HaloDevices).
    /// Halo places every placement that is not flagged "not placed automatically" (nor "not placed" on the difficulty); scripts create the
    /// rest with object_create. Placements are built per structure BSP (the active one; others when the game switches there); an object of
    /// another BSP is hidden while that BSP is not active (Halo: disconnected from the map).
    /// </summary>
    public static class HaloObjects
    {
        static readonly Dictionary<string, GameObject> ByName = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Placement states: never built yet, alive, destroyed by a script (not placed again automatically).</summary>
        public enum State : byte { NotYet = 0, Live = 1, Destroyed = 2 }

        static HaloMissionData _data;
        static State[] _state = new State[0];
        static GameObject[] _live = new GameObject[0];
        static Transform _objectsRoot;
        static readonly Dictionary<HaloTag, int> TeamOf = new Dictionary<HaloTag, int>();
        static readonly HashSet<string> Said = new HashSet<string>();

        /// <summary>Live placements (named or not).</summary>
        public static int Count { get; private set; }
        /// <summary>Live placements with a scenario name.</summary>
        public static int NamedCount { get; private set; }

        /// <summary>A script created / destroyed a named scenario object (raised on the host; Net replays them on co-op clients).</summary>
        public static event Action<string> Created, Destroyed;
        /// <summary>Co-op client: the scenario's bipeds are not placed here (the host's units arrive as puppets).</summary>
        public static bool SkipBipeds;

        static void Note(bool created, int i)
        {
            if (_data == null || i < 0 || i >= _data.Objects.Count) return;
            string n = _data.Objects[i].Name;
            if (n.Length == 0 || !HaloCampaign.Host) return;
            try { if (created) HaloNetGame.NoteObjectCreated(n); else HaloNetGame.NoteObjectDestroyed(n); }
            catch (Exception e) { Once("net note", "Campaign: telling Net about '" + n + "' failed: " + e.Message); }
            var h = created ? Created : Destroyed;
            if (h == null) return;
            foreach (Action<string> d in h.GetInvocationList()) { try { d(n); } catch (Exception e) { HaloLog.Error("Campaign: object event listener: " + e); } }
        }

        /// <summary>The live object of a scenario object name (null when not created / destroyed).</summary>
        public static GameObject Find(string name) { GameObject g; return name != null && ByName.TryGetValue(name, out g) && g != null ? g : null; }
        /// <summary>Script object exists (live under that name).</summary>
        public static bool Exists(string name) { return Find(name) != null; }
        /// <summary>Called by a module that created a named object.</summary>
        public static void Register(string name, GameObject go) { if (!string.IsNullOrEmpty(name) && go != null) ByName[name] = go; }
        public static void Unregister(string name) { if (name != null) ByName.Remove(name); }

        /// <summary>Everything goes (mission leave): the objects themselves go with the mission root.</summary>
        public static void Clear()
        {
            for (int i = 0; i < _live.Length; i++) if (_live[i] != null) Forget(i, false);
            ByName.Clear();
            _data = null;
            _state = new State[0];
            _live = new GameObject[0];
            _objectsRoot = null;
            Count = 0; NamedCount = 0;
            TeamOf.Clear();
            HaloObjectAnimator.ClearCache();
            Said.Clear();
        }

        /// <summary>Script object_create: builds the named scenario object (no-op when it exists).</summary>
        public static GameObject Create(string name)
        {
            GameObject g = Find(name);
            if (g != null) return g;
            int i = IndexOf(name);
            if (i < 0) { Once("create " + name, "Campaign: object_create '" + name + "' - no such scenario object"); return null; }
            var made = Build(i);
            if (made != null) Note(true, i);
            return made;
        }

        /// <summary>Script object_create_anew: destroyed first when it exists, then built at its placement.</summary>
        public static GameObject CreateAnew(string name)
        {
            Destroy(name);
            return Create(name);
        }

        /// <summary>Script object_destroy.</summary>
        public static void Destroy(string name)
        {
            int i = IndexOf(name);
            if (i >= 0) { bool was = _live[i] != null; Kill(i); if (was) Note(false, i); return; }
            var g = Find(name);
            if (g != null) UnityEngine.Object.Destroy(g);
            Unregister(name);
        }

        /// <summary>Script object_create_containing: every named placement whose name contains <paramref name="part"/>.</summary>
        public static int CreateContaining(string part, bool anew = false)
        {
            if (_data == null || string.IsNullOrEmpty(part)) return 0;
            int n = 0;
            for (int i = 0; i < _data.Objects.Count; i++)
            {
                string nm = _data.Objects[i].Name;
                if (nm.Length == 0 || nm.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (anew) Kill(i);
                if (_live[i] == null && Build(i) != null) { n++; Note(true, i); }
            }
            return n;
        }

        /// <summary>Script object_destroy_containing.</summary>
        public static int DestroyContaining(string part)
        {
            if (_data == null || string.IsNullOrEmpty(part)) return 0;
            int n = 0;
            for (int i = 0; i < _data.Objects.Count; i++)
            {
                string nm = _data.Objects[i].Name;
                if (nm.Length == 0 || nm.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (_live[i] != null) { n++; Kill(i); Note(false, i); }
                else Kill(i);
            }
            return n;
        }

        /// <summary>Script object_create_anew_all / objects of a kind: every placement of the kind (null = all kinds) that is not live.</summary>
        public static int CreateAll(HaloMissionData.Kind? kind = null)
        {
            if (_data == null) return 0;
            int n = 0;
            for (int i = 0; i < _data.Objects.Count; i++)
                if ((kind == null || _data.Objects[i].Kind == kind.Value) && _live[i] == null && Build(i) != null) { n++; Note(true, i); }
            return n;
        }

        /// <summary>Script object_destroy_all (every placement; the players stay).</summary>
        public static int DestroyAll(HaloMissionData.Kind? kind = null)
        {
            if (_data == null) return 0;
            int n = 0;
            for (int i = 0; i < _data.Objects.Count; i++)
            {
                if (kind != null && _data.Objects[i].Kind != kind.Value) continue;
                if (_live[i] != null) { n++; Kill(i); Note(false, i); }
                else Kill(i);
            }
            return n;
        }

        /// <summary>Script object_teleport: a named object to a cutscene flag (its position and facing).</summary>
        public static bool Teleport(string name, string cutsceneFlag)
        {
            var g = Find(name);
            var f = _data != null ? _data.Flag(cutsceneFlag) : null;
            if (g == null || f == null) return false;
            g.transform.SetPositionAndRotation(HaloSpace.MapOrigin + HaloSpace.Pos(f.Position), Quaternion.Euler(0f, 90f - f.Yaw * Mathf.Rad2Deg, 0f));
            return true;
        }

        /// <summary>Script scenery_animation_start / scenery_animation_start_at_frame: <paramref name="animation"/> of <paramref name="graph"/>
        /// (null = the object's own) played once on the named object's model from <paramref name="frame"/> on the game clock.</summary>
        public static bool AnimateScenery(string name, HaloTag graph, string animation, int frame = 0)
        {
            var g = Find(name);
            if (g == null) { Once("anim " + name, "Campaign: scenery_animation_start '" + name + "' - no such object (not created?)"); return false; }
            var mo = g.GetComponent<HaloMissionObject>();
            HaloModelBuilder.Built built = mo != null ? mo.Built : null;
            if (graph == null && mo != null && mo.Placement != null && mo.Placement.Type != null)
            {
                try { graph = new HaloObjectDef(mo.Placement.Type).AnimationGraph; } catch (Exception) { graph = null; }
            }
            var an = g.GetComponent<HaloObjectAnimator>();
            if (an == null) an = g.AddComponent<HaloObjectAnimator>();
            string why;
            if (!an.Play(built, graph, animation, frame, out why))
            {
                Once("anim " + name + animation, "Campaign: scenery_animation_start '" + name + "' '" + animation + "' - " + why);
                return false;
            }
            HaloLog.Info("Campaign: scenery '" + name + "' plays '" + animation + "' from frame " + frame + " (" + (graph != null ? graph.Name : "?") + ")");
            return true;
        }

        /// <summary>The custom animation of a named object has reached its last frame (or none plays).</summary>
        public static bool AnimationDone(string name)
        {
            var g = Find(name);
            var an = g != null ? g.GetComponent<HaloObjectAnimator>() : null;
            return an == null || an.Done;
        }

        /// <summary>Game tick: custom object animations advance a frame.</summary>
        internal static void Step()
        {
            var list = HaloObjectAnimator.Playing;
            for (int i = list.Count - 1; i >= 0; i--) { var a = list[i]; if (a == null) list.RemoveAt(i); else if (a.isActiveAndEnabled) a.Step(); }
        }

        /// <summary>Every frame: animations between ticks.</summary>
        internal static void Draw(float u)
        {
            var list = HaloObjectAnimator.Playing;
            for (int i = 0; i < list.Count; i++) { var a = list[i]; if (a != null && a.isActiveAndEnabled) a.Draw(u); }
        }

        /// <summary>The scenario placement index of a name (-1 none).</summary>
        public static int IndexOf(string name)
        {
            if (_data == null || string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < _data.Objects.Count; i++) if (string.Equals(_data.Objects[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The placement of a live object (null when the object is not a scenario placement).</summary>
        public static HaloMissionData.Obj PlacementOf(GameObject go)
        {
            if (go == null) return null;
            var m = go.GetComponent<HaloMissionObject>();
            return m != null ? m.Placement : null;
        }

        /// <summary>Placement index -> state / live object (checkpoints).</summary>
        public static State StateOf(int index) { return index >= 0 && index < _state.Length ? _state[index] : State.NotYet; }
        public static GameObject LiveAt(int index) { return index >= 0 && index < _live.Length ? _live[index] : null; }
        public static int PlacementCount { get { return _state.Length; } }

        /// <summary>Checkpoint restore: the placement goes back to a state (built / destroyed / not built).</summary>
        public static GameObject SetState(int index, State s)
        {
            if (_data == null || index < 0 || index >= _state.Length) return null;
            if (s == State.Live) return _live[index] != null ? _live[index] : Build(index);
            if (_live[index] != null) Forget(index, true);
            _state[index] = s;
            return null;
        }

        // ------------------------------------------------------------------ placing per BSP

        /// <summary>Builds the automatically placed objects of BSP <paramref name="bsp"/> that were never built (over several frames, ~8 ms each).</summary>
        public static IEnumerator PlaceBsp(HaloMissionData data, int bsp, Transform root, Action<float, string> progress)
        {
            if (data == null) yield break;
            if (_data != data)
            {
                Clear();
                _data = data;
                _state = new State[data.Objects.Count];
                _live = new GameObject[data.Objects.Count];
            }
            if (_objectsRoot == null && root != null)
            {
                var go = new GameObject("Scenario Objects");
                go.transform.SetParent(root, false);
                _objectsRoot = go.transform;
            }
            int diff = (int)HaloCampaign.Level;
            int made = 0, skippedScript = 0, skippedDifficulty = 0;
            float start = Time.realtimeSinceStartup;
            float frame = start;
            var counts = new int[12];
            for (int i = 0; i < data.Objects.Count; i++)
            {
                var o = data.Objects[i];
                if (_state[i] != State.NotYet || !data.InBsp(o, bsp)) continue;
                if (!o.AutomaticallyPlaced) { skippedScript++; continue; }
                if (SkipBipeds && o.Kind == HaloMissionData.Kind.Biped) continue;
                if (!o.PlacedOn(diff)) { skippedDifficulty++; continue; }
                if (Time.realtimeSinceStartup - frame > 0.008f)
                {
                    if (progress != null) { try { progress((float)i / data.Objects.Count, "Placing " + Plural(o.Kind)); } catch (Exception) { } }
                    yield return null;
                    frame = Time.realtimeSinceStartup;
                    if (_data != data) yield break;
                }
                if (Build(i) != null) { made++; counts[(int)o.Kind]++; }
            }
            string list = "";
            for (int k = 0; k < counts.Length; k++) if (counts[k] > 0) list += (list.Length > 0 ? ", " : "") + counts[k] + " " + Plural((HaloMissionData.Kind)k);
            HaloLog.Info("Campaign: BSP " + bsp + ": placed " + made + " objects (" + (list.Length > 0 ? list : "none") + ") in " + ((Time.realtimeSinceStartup - start) * 1000f).ToString("0") + " ms; "
                         + skippedScript + " wait for a script (not placed automatically), " + skippedDifficulty + " not on " + HaloCampaign.Name(HaloCampaign.Level) + "; live " + Count + " (" + NamedCount + " named)");
        }

        /// <summary>Static objects (scenery, devices, light fixtures, sound scenery) of other BSPs are hidden while <paramref name="bsp"/> is active.</summary>
        public static void ShowBsp(HaloMissionData data, int bsp)
        {
            if (data == null || data != _data) return;
            int shown = 0, hidden = 0;
            for (int i = 0; i < _live.Length; i++)
            {
                var g = _live[i];
                if (g == null) continue;
                var o = data.Objects[i];
                if (!IsStatic(o.Kind)) continue;
                bool on = data.InBsp(o, bsp);
                if (g.activeSelf != on) g.SetActive(on);
                if (on) shown++; else hidden++;
            }
            HaloLog.Info("Campaign: BSP " + bsp + " objects shown " + shown + ", hidden " + hidden + " (other BSPs)");
        }

        static bool IsStatic(HaloMissionData.Kind k)
        {
            return k == HaloMissionData.Kind.Scenery || k == HaloMissionData.Kind.Machine || k == HaloMissionData.Kind.Control || k == HaloMissionData.Kind.LightFixture || k == HaloMissionData.Kind.SoundScenery;
        }

        static string Plural(HaloMissionData.Kind k)
        {
            switch (k)
            {
                case HaloMissionData.Kind.Biped: return "bipeds";
                case HaloMissionData.Kind.Vehicle: return "vehicles";
                case HaloMissionData.Kind.Weapon: return "weapons";
                case HaloMissionData.Kind.Equipment: return "equipment";
                case HaloMissionData.Kind.Machine: return "machines";
                case HaloMissionData.Kind.Control: return "controls";
                case HaloMissionData.Kind.LightFixture: return "light fixtures";
                case HaloMissionData.Kind.SoundScenery: return "sound scenery";
                default: return "scenery";
            }
        }

        // ------------------------------------------------------------------ one placement

        static GameObject Build(int i)
        {
            if (_data == null || i < 0 || i >= _data.Objects.Count) return null;
            if (_live[i] != null) return _live[i];
            var o = _data.Objects[i];
            if (o.Type == null) return null;
            GameObject go = null;
            try
            {
                switch (o.Kind)
                {
                    case HaloMissionData.Kind.Biped: go = BuildBiped(o); break;
                    case HaloMissionData.Kind.Vehicle: go = BuildVehicle(o); break;
                    case HaloMissionData.Kind.Weapon:
                    case HaloMissionData.Kind.Equipment: go = BuildItem(o); break;
                    default: go = BuildStatic(o); break;
                }
            }
            catch (Exception e) { Once("build " + o.Type.Name, "Campaign: building " + o + " failed: " + e); go = null; }
            if (go == null) return null;
            var mo = go.GetComponent<HaloMissionObject>();
            if (mo == null) mo = go.AddComponent<HaloMissionObject>();
            mo.Placement = o;
            mo.PlacementIndex = i;
            var dev = go.GetComponent<HaloDevice>();
            if (dev != null) dev.PlacementIndex = i;
            _live[i] = go;
            _state[i] = State.Live;
            Count++;
            if (o.Name.Length > 0) { Register(o.Name, go); NamedCount++; }
            return go;
        }

        /// <summary>A placement's object goes (object_destroy): state Destroyed.</summary>
        static void Kill(int i)
        {
            if (_data == null || i < 0 || i >= _live.Length) return;
            if (_live[i] != null) Forget(i, true);
            _state[i] = State.Destroyed;
        }

        static void Forget(int i, bool destroy)
        {
            var go = _live[i];
            _live[i] = null;
            if (go == null) return;
            Count = Mathf.Max(0, Count - 1);
            var o = _data != null && i < _data.Objects.Count ? _data.Objects[i] : null;
            if (o != null && o.Name.Length > 0) { if (Find(o.Name) == go) Unregister(o.Name); NamedCount = Mathf.Max(0, NamedCount - 1); }
            var mo = go.GetComponent<HaloMissionObject>();
            if (mo != null) mo.Release();
            HaloDevices.Remove(go);
            if (!destroy) return;
            if (o != null && o.Kind == HaloMissionData.Kind.Vehicle)
            {
                var v = go.GetComponent<HaloVehicle>();
                if (v != null) { HaloVehicles.Remove(v); return; }
            }
            if (o != null && (o.Kind == HaloMissionData.Kind.Weapon || o.Kind == HaloMissionData.Kind.Equipment))
            {
                for (int k = HaloPickups.Items.Count - 1; k >= 0; k--)
                {
                    var it = HaloPickups.Items[k];
                    if ((it.Gun != null && it.Gun.gameObject == go) || it.Visual == go) { HaloPickups.Items.RemoveAt(k); break; }
                }
                // a weapon the player carries is no longer the placement: it stays in the hands
                var gun = go.GetComponent<HaloGun>();
                var h = HaloCombat.Handling;
                if (gun != null && h != null && h.IndexOf(gun) >= 0) return;
            }
            UnityEngine.Object.Destroy(go);
        }

        static Vector3 PositionOf(HaloMissionData.Obj o) { return HaloSpace.MapOrigin + HaloSpace.Pos(o.Position); }
        static float YawOf(HaloMissionData.Obj o) { return 90f - o.Rotation.X * Mathf.Rad2Deg; }

        /// <summary>Scenery, machines, controls, light fixtures, sound scenery: model (devices skinned so their nodes move), collision on its
        /// nodes, change colours, attachments (lights, sounds, effects); devices go to HaloDevices.</summary>
        static GameObject BuildStatic(HaloMissionData.Obj o)
        {
            var def = new HaloObjectDef(o.Type);
            bool device = o.Kind == HaloMissionData.Kind.Machine || o.Kind == HaloMissionData.Kind.Control || o.Kind == HaloMissionData.Kind.LightFixture;
            var go = new GameObject(KindName(o.Kind) + " " + o.Index + " " + (o.Name.Length > 0 ? o.Name : System.IO.Path.GetFileName(o.Type.Name)));
            go.transform.SetParent(_objectsRoot, false);
            go.transform.localPosition = HaloSpace.Pos(o.Position);
            go.transform.localRotation = HaloWorld.ObjectRotation(o.Rotation);
            HaloModelBuilder.Built built = null;
            if (def.Model != null)
            {
                // devices move their nodes; named scenery may get a scenery_animation_start: both skinned
                bool animatable = device || (o.Kind == HaloMissionData.Kind.Scenery && o.Name.Length > 0);
                var opt = new HaloModelBuilder.Options { Static = !animatable, CastShadows = false };
                built = HaloModelBuilder.Build(def.Model, go.transform, opt);
                if (built != null && built.Mesh != null)
                {
                    var cc = def.PickColor(0, o.Index * 31 + o.PaletteIndex);
                    if (cc != null) HaloMaterials.SetChangeColor(built.Mesh, new Color(cc[0], cc[1], cc[2]));
                }
            }
            if (def.CollisionModel != null) HaloCollision.AddModelColliders(def.CollisionModel, built, go.transform, HaloLayers.Environment);
            var mo = go.AddComponent<HaloMissionObject>();
            mo.Built = built;
            try { mo.Attached = HaloFx.Attach(o.Type, built != null ? built.Root.transform : go.transform); }
            catch (Exception e) { Once("attach " + o.Type.Name, "Campaign: attachments of " + o.Type + " failed: " + e.Message); }
            if (device) HaloDevices.Add(go, o, built);
            return go;
        }

        static string KindName(HaloMissionData.Kind k)
        {
            switch (k)
            {
                case HaloMissionData.Kind.Machine: return "machine";
                case HaloMissionData.Kind.Control: return "control";
                case HaloMissionData.Kind.LightFixture: return "light fixture";
                case HaloMissionData.Kind.SoundScenery: return "sound scenery";
                default: return "scenery";
            }
        }

        /// <summary>The unit's default team (struct Unit +384: none, player, human, covenant, flood, sentinel).</summary>
        static HaloTeam UnitTeam(HaloTag unit)
        {
            int t;
            if (!TeamOf.TryGetValue(unit, out t))
            {
                try { t = unit.Root.U16(384); } catch (Exception) { t = 0; }
                TeamOf[unit] = t;
            }
            return t >= 0 && t <= 5 ? (HaloTeam)t : HaloTeam.None;
        }

        static GameObject BuildBiped(HaloMissionData.Obj o)
        {
            HaloUnit u = null;
            try { u = HaloBipeds.Spawn(o.Type, PositionOf(o), YawOf(o), UnitTeam(o.Type)); }
            catch (Exception e) { Once("biped " + o.Type.Name, "Campaign: HaloBipeds.Spawn " + o.Type + " failed: " + e.Message); }
            if (u == null)
            {
                // the Characters module is not there yet: a named stand-in where the biped stands, so scripts find the object
                Once("biped stub", "Campaign: HaloBipeds.Spawn gave no unit (Characters module not ready) - bipeds are empty named markers for now");
                var g = new GameObject("biped " + o.Index + " " + (o.Name.Length > 0 ? o.Name : System.IO.Path.GetFileName(o.Type.Name)) + " (marker)");
                g.transform.SetParent(_objectsRoot, false);
                g.transform.SetPositionAndRotation(PositionOf(o), Quaternion.Euler(0f, YawOf(o), 0f));
                return g;
            }
            u.ObjectName = o.Name;
            if (o.Dead) { try { u.Kill(); } catch (Exception e) { Once("kill " + o.Type.Name, "Campaign: killing the dead biped " + o + " failed: " + e.Message); } }
            return u.gameObject;
        }

        static GameObject BuildVehicle(HaloMissionData.Obj o)
        {
            var sc = HaloSession.Scenario;
            if (sc == null || o.Index < 0 || o.Index >= sc.Vehicles.Count) return null;
            var v = HaloVehicles.SpawnPlacement(o.Index, sc.Vehicles[o.Index]);
            return v != null ? v.gameObject : null;
        }

        static GameObject BuildItem(HaloMissionData.Obj o)
        {
            var it = HaloPickups.PlaceAt(o.Type, PositionOf(o), YawOf(o));
            if (it == null) { Once("item " + o.Type.Name, "Campaign: no pickup for " + o.Type); return null; }
            if (it.Gun != null)
            {
                if (it.Gun.State != null && (o.RoundsLoaded > 0 || o.RoundsReserved > 0)) { it.Gun.State.Loaded = o.RoundsLoaded; it.Gun.State.Reserve = o.RoundsReserved; }
                return it.Gun.gameObject;
            }
            return it.Visual;
        }

        static void Once(string key, string line)
        {
            if (Said.Add(key)) HaloLog.Warn(line);
        }
    }

    /// <summary>[Mission module, K] On every scenario object K built: its placement (name, kind, index), the model and its attachments.</summary>
    public sealed class HaloMissionObject : MonoBehaviour
    {
        public HaloMissionData.Obj Placement;
        public int PlacementIndex = -1;
        public HaloModelBuilder.Built Built;
        public HaloFx.Attachments Attached;

        public string ObjectName { get { return Placement != null ? Placement.Name : ""; } }

        public void Release()
        {
            if (Attached != null) { try { HaloFx.Detach(Attached); } catch (Exception) { } }
            Attached = null;
        }

        private void OnDestroy() { Release(); }
    }
}

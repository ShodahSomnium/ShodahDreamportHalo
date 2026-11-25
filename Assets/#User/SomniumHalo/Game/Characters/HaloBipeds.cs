using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Characters module, C] Spawning campaign bipeds and keeping track of them: <see cref="Spawn"/> builds a <see cref="HaloUnit"/> from a unit
    /// (bipd) or actor variant (actv) tag under the world root; the tags' parsed forms (character definitions, models, skeletons, animation
    /// graphs) are cached per map; dead bodies stay <see cref="BodySeconds"/> (at most <see cref="MaxBodies"/>), then go. Events for the AI and
    /// script modules: <see cref="Died"/>, <see cref="Damaged"/>, <see cref="Fired"/>. Player.log: "Characters: ..." (first spawn of each kind,
    /// deaths, failures).
    /// </summary>
    public static class HaloBipeds
    {
        /// <summary>Every live unit object (alive or a body still lying there).</summary>
        public static readonly List<HaloUnit> All = new List<HaloUnit>();
        /// <summary>Seconds a body stays after the death animation (ours: Halo keeps them until its object garbage collection).</summary>
        public static float BodySeconds = 90f;
        public static int MaxBodies = 40;
        /// <summary>Where units are made when there is no world root (the lab).</summary>
        public static Transform LabParent;

        /// <summary>A unit died (the hit that did it; SourcePlayer = the player's id or -1).</summary>
        public static event Action<HaloUnit, HaloHit> Died;
        /// <summary>A unit took damage (and lives).</summary>
        public static event Action<HaloUnit, HaloHit> Damaged;
        /// <summary>A unit fired (weapon, muzzle, direction): the motion tracker, the AI's hearing.</summary>
        public static event Action<HaloUnit, HaloTag, Vector3, Vector3> Fired;

        /// <summary>A body was destroyed (raised once, before Destroy(gameObject); the unit is dead, still where it was, its Def readable):
        /// carrier bursts (the AI releases Def.SpawnedActor from it), infection form pops.</summary>
        public static event Action<HaloUnit> Destroyed;
        /// <summary>Recent dead bodies (alive = false, not destroyed), newest last, at most <see cref="MaxCorpses"/>: the infection forms' feeding gesture.</summary>
        public static IReadOnlyList<HaloUnit> Corpses => CorpseList;
        public const int MaxCorpses = 32;
        private static readonly List<HaloUnit> CorpseList = new List<HaloUnit>(MaxCorpses);

        internal static void AddCorpse(HaloUnit u)
        {
            if (u == null) return;
            CorpseList.Remove(u);
            if (CorpseList.Count >= MaxCorpses) CorpseList.RemoveAt(0);
            CorpseList.Add(u);
        }

        internal static void RaiseDestroyed(HaloUnit u)
        {
            CorpseList.Remove(u);
            var e = Destroyed;
            if (e == null) return;
            foreach (Delegate x in e.GetInvocationList())
            {
                try { ((Action<HaloUnit>)x)(u); }
                catch (Exception ex) { HaloLog.Warn("Characters: Destroyed listener: " + ex.Message); }
            }
        }

        /// <summary>Infection forms attached to a unit that goes let go first (they would go with it: they are its children).</summary>
        internal static void ReleaseLatchers(HaloUnit target)
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                HaloUnit u = All[i];
                if (u != null && u.LatchTarget == target) u.Unlatch();
            }
        }

        private static HaloMap _cacheMap;
        private static readonly Dictionary<HaloTag, HaloCharacterDef> Defs = new Dictionary<HaloTag, HaloCharacterDef>();
        private static readonly Dictionary<HaloTag, HaloModel> Models = new Dictionary<HaloTag, HaloModel>();
        private static readonly Dictionary<HaloTag, HaloSkeleton> Skeletons = new Dictionary<HaloTag, HaloSkeleton>();
        private static readonly Dictionary<HaloTag, HaloAnimGraph> Graphs = new Dictionary<HaloTag, HaloAnimGraph>();
        private static readonly HashSet<HaloTag> Logged = new HashSet<HaloTag>();
        private static GameObject _driver;

        public static Transform Parent => HaloSession.WorldRoot != null ? HaloSession.WorldRoot : LabParent;

        /// <summary>A biped from a unit (bipd) or actor variant (actv) tag at a Unity position / yaw (degrees), under the world root. Team None = the unit's own.</summary>
        public static HaloUnit Spawn(HaloTag unitOrVariant, Vector3 position, float yawDegrees, HaloTeam team)
        {
            if (unitOrVariant == null) return null;
            HaloCharacterDef def = Def(unitOrVariant);
            if (def == null) { Once(unitOrVariant, "Characters: " + unitOrVariant.Name + " (" + unitOrVariant.ClassName + ") is not a biped or an actor variant of one"); return null; }
            var go = new GameObject("Unit " + System.IO.Path.GetFileName(def.Name));
            go.transform.SetParent(Parent, false);
            go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
            HaloUnit u = go.AddComponent<HaloUnit>();
            try { u.Init(def, team); }
            catch (Exception e)
            {
                HaloLog.Error("Characters: spawning " + def.Name + " failed: " + e);
                UnityEngine.Object.Destroy(go);
                return null;
            }

            All.Add(u);
            EnsureDriver();
            HaloBipedsCheckpoints.Ensure();
            if (Logged.Add(unitOrVariant))
                HaloLog.Info("Characters: first " + def.Name + ": model " + (def.Model != null ? def.Model.Name : "-") + ", " + (u.Animator != null ? u.Animator.Graph.Animations.Length + " animations" : "no animation graph") +
                             ", weapon " + (u.Weapon != null ? u.Weapon.Name + " (" + u.WeaponLabel + ")" : "none") + ", body " + def.MaxBody + " shield " + def.MaxShield + ", team " + u.Team +
                             ", hit boxes " + u.Hitboxes.Count + (def.Flying ? ", flying" : "") + ", permutations " + PermString(u));
            return u;
        }

        private static string PermString(HaloUnit u)
        {
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, string> kv in u.Permutations) { if (sb.Length > 0) sb.Append(' '); sb.Append(kv.Key).Append('=').Append(kv.Value); }
            return sb.ToString();
        }

        /// <summary>Removes every unit (mission end / revert).</summary>
        public static void Clear()
        {
            for (int i = All.Count - 1; i >= 0; i--) if (All[i] != null) UnityEngine.Object.Destroy(All[i].gameObject);
            All.Clear();
            CorpseList.Clear();
            HaloUnit.ClearStaticCaches();      // (tag-keyed: they would hold the old map)
        }

        /// <summary>Removes one unit without a trace (ai_erase / object_destroy).</summary>
        public static void Erase(HaloUnit u) { if (u != null) { ReleaseLatchers(u); UnityEngine.Object.Destroy(u.gameObject); } }

        internal static void Forget(HaloUnit u) { All.Remove(u); CorpseList.Remove(u); }

        // ------------------------------------------------------------------ the parsed tags (per map)

        private static void CheckMap(HaloMap m)
        {
            if (m == _cacheMap) return;
            _cacheMap = m;
            Defs.Clear(); Models.Clear(); Skeletons.Clear(); Graphs.Clear(); Logged.Clear();
        }

        /// <summary>The character definition of a bipd / actv tag (cached; null for other tags).</summary>
        public static HaloCharacterDef Def(HaloTag t)
        {
            if (t == null) return null;
            CheckMap(t.Map);
            HaloCharacterDef d;
            if (Defs.TryGetValue(t, out d)) return d;
            try { d = HaloCharacterDef.From(t); }
            catch (Exception e) { HaloLog.Warn("Characters: reading " + t.Name + " failed: " + e.Message); d = null; }
            Defs[t] = d;
            return d;
        }

        public static HaloModel ModelOf(HaloTag mod2)
        {
            if (mod2 == null) return null;
            CheckMap(mod2.Map);
            HaloModel m;
            if (Models.TryGetValue(mod2, out m)) return m;
            try { m = new HaloModel(mod2); } catch (Exception) { m = null; }
            Models[mod2] = m;
            return m;
        }

        public static HaloSkeleton SkeletonOf(HaloTag mod2, HaloModel built)
        {
            CheckMap(mod2.Map);
            HaloSkeleton s;
            if (Skeletons.TryGetValue(mod2, out s)) return s;
            s = new HaloSkeleton(built ?? ModelOf(mod2));
            Skeletons[mod2] = s;
            return s;
        }

        public static HaloAnimGraph GraphOf(HaloTag antr)
        {
            if (antr == null) return null;
            CheckMap(antr.Map);
            HaloAnimGraph g;
            if (Graphs.TryGetValue(antr, out g)) return g;
            try { g = new HaloAnimGraph(antr); }
            catch (Exception e) { HaloLog.Warn("Characters: animation graph " + antr.Name + " failed: " + e.Message); g = null; }
            Graphs[antr] = g;
            return g;
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Living units of a team (allocates the list: not for every frame).</summary>
        public static List<HaloUnit> Living(HaloTeam team)
        {
            var l = new List<HaloUnit>();
            for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].Alive && (team == HaloTeam.None || All[i].Team == team)) l.Add(All[i]);
            return l;
        }

        /// <summary>The unit a collider belongs to (a hit box), null when none.</summary>
        public static HaloUnit Of(Collider c) { return c != null ? c.GetComponentInParent<HaloUnit>() : null; }

        // ------------------------------------------------------------------ events (owner helpers)

        internal static void RaiseDied(HaloUnit u, HaloHit h) { Raise(Died, u, h, "Died"); }
        internal static void RaiseDamaged(HaloUnit u, HaloHit h) { Raise(Damaged, u, h, "Damaged"); }

        internal static void RaiseFired(HaloUnit u, HaloTag w, Vector3 p, Vector3 d)
        {
            var e = Fired;
            if (e == null) return;
            foreach (Delegate x in e.GetInvocationList())
            {
                try { ((Action<HaloUnit, HaloTag, Vector3, Vector3>)x)(u, w, p, d); }
                catch (Exception ex) { HaloLog.Warn("Characters: Fired listener: " + ex.Message); }
            }
        }

        private static void Raise(Action<HaloUnit, HaloHit> e, HaloUnit u, HaloHit h, string what)
        {
            if (e == null) return;
            foreach (Delegate x in e.GetInvocationList())
            {
                try { ((Action<HaloUnit, HaloHit>)x)(u, h); }
                catch (Exception ex) { HaloLog.Warn("Characters: " + what + " listener: " + ex.Message); }
            }
        }

        private static void Once(HaloTag t, string msg) { if (Logged.Add(t)) HaloLog.Warn(msg); }

        // ------------------------------------------------------------------ bodies

        private static void EnsureDriver()
        {
            if (_driver != null) return;
            _driver = new GameObject("Halo Bipeds");
            _driver.AddComponent<HaloBipedsDriver>();
            if (HaloGame.Current != null) _driver.transform.SetParent(HaloGame.Current.transform, false);
            HaloSession.Unloading += Clear;
        }

        private static readonly List<HaloUnit> Bodies = new List<HaloUnit>();

        /// <summary>Once a second: bodies past their time (or over the limit, oldest first) go.</summary>
        internal static void Sweep()
        {
            Bodies.Clear();
            for (int i = All.Count - 1; i >= 0; i--)
            {
                HaloUnit u = All[i];
                if (u == null) { All.RemoveAt(i); continue; }
                if (!u.Alive) Bodies.Add(u);
            }

            int over = Bodies.Count - MaxBodies;
            for (int i = 0; i < Bodies.Count; i++)
            {
                HaloUnit u = Bodies[i];
                if (u.DeadFor > BodySeconds || over > 0 && u.DeadFor > 5f) { over--; UnityEngine.Object.Destroy(u.gameObject); }
            }
        }
    }

    /// <summary>Runs the bodies' sweep once a second (made with the first unit).</summary>
    public sealed class HaloBipedsDriver : MonoBehaviour
    {
        private float _next;

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 1f;
            HaloBipeds.Sweep();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Script module, S] What a HaloScript object value means in the Unity world. Object handles (ints in the VM):
    /// -1 = none; 0..N-1 = the scenario object name of that index (resolved live through HaloObjects by name, so a destroyed / not yet
    /// created object reads as none); <see cref="PlayerBase"/> + i = player i (the local player for now); <see cref="DynamicBase"/> + k =
    /// an unnamed object handed out at run time (AI actors from ai_actors, vehicles...). Positions are converted with HaloSpace.
    /// </summary>
    public static class HaloScriptWorld
    {
        public const int DynamicBase = 0x10000, PlayerBase = 0x20000;

        static HsData _d;
        static readonly Dictionary<string, int> NameIndex = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        static readonly List<GameObject> Dynamic = new List<GameObject>();
        static readonly Dictionary<GameObject, int> DynamicIndex = new Dictionary<GameObject, int>();

        public static void Begin(HsData d)
        {
            _d = d;
            NameIndex.Clear(); Dynamic.Clear(); DynamicIndex.Clear();
            if (d == null) return;
            for (int i = 0; i < d.ObjectNames.Length; i++) if (!NameIndex.ContainsKey(d.ObjectNames[i])) NameIndex[d.ObjectNames[i]] = i;
        }

        public static void End() { _d = null; NameIndex.Clear(); Dynamic.Clear(); DynamicIndex.Clear(); }

        public static bool IsPlayer(int h) => h >= PlayerBase && h < PlayerBase + 16;
        /// <summary>The local player exists (the client avatar, or in the editor the SDK test controller / camera).</summary>
        public static int PlayerCount { get { Vector3 f; return HaloPlayer.TryFeet(out f) ? 1 : 0; } }

        public static string NameOf(int h) => _d != null && h >= 0 && h < _d.ObjectNames.Length ? _d.ObjectNames[h] : "";
        public static int NameHandle(string name) { int i; return name != null && NameIndex.TryGetValue(name, out i) ? i : -1; }

        /// <summary>The live GameObject of a handle (null = none / not created / destroyed).</summary>
        public static GameObject Get(int h)
        {
            if (h < 0) return null;
            if (_d != null && h < _d.ObjectNames.Length) return HaloObjects.Find(_d.ObjectNames[h]);
            if (IsPlayer(h)) { var r = HaloPlayer.Root; return h == PlayerBase && r != null ? r.gameObject : null; }
            int k = h - DynamicBase;
            if (k >= 0 && k < Dynamic.Count) return Dynamic[k];
            return null;
        }

        public static bool Exists(int h) => h == PlayerBase ? PlayerCount > 0 : Get(h) != null;

        /// <summary>The handle of a live object: its scenario name when it has one, else a run-time handle.</summary>
        public static int Handle(GameObject go)
        {
            if (go == null) return -1;
            var root = HaloPlayer.Root;
            if (root != null && go == root.gameObject) return PlayerBase;
            int h;
            if (DynamicIndex.TryGetValue(go, out h)) return h;
            var u = go.GetComponent<HaloUnit>();
            string name = u != null ? u.ObjectName : null;
            if (string.IsNullOrEmpty(name)) { var m = go.GetComponent<HaloMissionObject>(); if (m != null) name = m.ObjectName; }
            if (!string.IsNullOrEmpty(name) && NameIndex.TryGetValue(name, out h) && HaloObjects.Find(name) == go) return h;
            h = DynamicBase + Dynamic.Count;
            Dynamic.Add(go); DynamicIndex[go] = h;
            return h;
        }

        public static HaloUnit Unit(int h) { var g = Get(h); return g != null ? g.GetComponent<HaloUnit>() : null; }
        public static HaloVehicle Vehicle(int h) { var g = Get(h); return g != null ? g.GetComponentInParent<HaloVehicle>() : null; }

        /// <summary>The point Halo tests an object by (its centre): units ~ chest height, the player from his feet.</summary>
        public static bool TryCenter(int h, out Vector3 p)
        {
            if (IsPlayer(h))
            {
                Vector3 feet;
                if (HaloPlayer.TryFeet(out feet)) { p = feet + Vector3.up * 0.9f; return true; }   // ours: chest of a 1.8 m player
                var r = HaloPlayer.Root; if (r != null) { p = r.position + Vector3.up * 0.9f; return true; }
                p = Vector3.zero; return false;
            }
            var g = Get(h);
            if (g == null) { p = Vector3.zero; return false; }
            var u = g.GetComponent<HaloUnit>();
            p = u != null ? g.transform.position + Vector3.up * 0.9f : g.transform.position;   // ours: biped centre
            return true;
        }

        /// <summary>Eye point and view direction (player: the head camera; units: their eye / look point).</summary>
        public static bool TryEye(int h, out Vector3 eye, out Vector3 forward)
        {
            if (IsPlayer(h))
            {
                var cam = HaloPlayer.Head();
                if (cam != null) { eye = cam.transform.position; forward = cam.transform.forward; return true; }
            }
            var g = Get(h);
            if (g == null) { eye = Vector3.zero; forward = Vector3.forward; return false; }
            var u = g.GetComponent<HaloUnit>();
            if (u != null)
            {
                eye = u.Eye;
                Vector3 d = u.LookPoint - eye;
                forward = d.sqrMagnitude > 0.01f ? d.normalized : g.transform.forward;
                return true;
            }
            eye = g.transform.position; forward = g.transform.forward; return true;
        }

        // ------------------------------------------------------------------ scenario points

        public static bool FlagPose(int flag, out Vector3 position, out float yawDegrees)
        {
            if (_d == null || flag < 0 || flag >= _d.FlagPositions.Length) { position = Vector3.zero; yawDegrees = 0f; return false; }
            position = HaloSpace.MapOrigin + HaloSpace.Pos(_d.FlagPositions[flag]);
            yawDegrees = 90f - _d.FlagYaw[flag] * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>Is the point (Unity world) inside a scenario trigger volume?</summary>
        public static bool InVolume(int volume, Vector3 p)
        {
            if (_d == null || volume < 0 || volume >= _d.Volumes.Length) return false;
            Vector3 h = HaloSpace.ToHalo(p - HaloSpace.MapOrigin);
            return _d.Volumes[volume].Contains(h.x, h.y, h.z);
        }

        public static bool ObjectInVolume(int volume, int h)
        {
            Vector3 p;
            return TryCenter(h, out p) && InVolume(volume, p);
        }
    }
}

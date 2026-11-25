using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Mission module, K] The scenario's trigger volumes (+864: oriented boxes) for the scripts (volume_test_object / volume_test_objects /
    /// players, volume_teleport_players_not_inside) and the BSP switch trigger volumes (+924): a player inside a switch volume whose source is
    /// the active BSP switches to its destination (HaloCampaign.SwitchBsp). Tests use an object's centre (units: about the waist).
    /// </summary>
    public static class HaloTriggers
    {
        static HaloMissionData _data;
        static int _lastSwitchTick = -1000;
        static readonly HashSet<string> Said = new HashSet<string>();

        public static int Count { get { return _data != null ? _data.TriggerVolumes.Count : 0; } }

        public static void Begin(HaloMissionData data) { _data = data; _lastSwitchTick = -1000; Said.Clear(); }
        public static void Clear() { _data = null; Said.Clear(); }

        /// <summary>The index of a trigger volume by name (-1 none).</summary>
        public static int Index(string volume) { return _data != null ? _data.TriggerVolumeIndex(volume) : -1; }

        static HaloMissionData.TriggerVolume Volume(string name)
        {
            int i = Index(name);
            if (i < 0) { if (_data != null && Said.Add(name ?? "")) HaloLog.Warn("Campaign: trigger volume '" + name + "' does not exist"); return null; }
            return _data.TriggerVolumes[i];
        }

        /// <summary>Is a Unity point inside the named volume.</summary>
        public static bool Contains(string volume, Vector3 point)
        {
            var v = Volume(volume);
            return v != null && Contains(v, point);
        }

        public static bool Contains(HaloMissionData.TriggerVolume v, Vector3 point)
        {
            Vector3 h = HaloSpace.ToHalo(point - HaloSpace.MapOrigin);
            return v.Contains(h.x, h.y, h.z);
        }

        /// <summary>An object's test point: units their centre, the rest their position.</summary>
        public static Vector3 CenterOf(GameObject g)
        {
            if (g == null) return Vector3.zero;
            var u = g.GetComponent<HaloUnit>();
            if (u != null) return g.transform.position + Vector3.up * 0.9f;
            return g.transform.position;
        }

        /// <summary>The local player's test point (feet + 0.9 m), false when unknown / dead.</summary>
        public static bool PlayerPoint(out Vector3 p)
        {
            Vector3 feet;
            if (!HaloPlayer.TryFeet(out feet)) { p = Vector3.zero; return false; }
            p = feet + Vector3.up * 0.9f;
            return true;
        }

        /// <summary>volume_test_object: the named object (or the player for "player" names) is inside.</summary>
        public static bool TestObject(string volume, string objectName)
        {
            var v = Volume(volume);
            if (v == null) return false;
            var g = HaloObjects.Find(objectName);
            return g != null && g.activeInHierarchy && Contains(v, CenterOf(g));
        }

        public static bool TestObject(string volume, GameObject g)
        {
            var v = Volume(volume);
            return v != null && g != null && g.activeInHierarchy && Contains(v, CenterOf(g));
        }

        /// <summary>volume_test_objects: any of them inside (<paramref name="all"/>: every one of them, volume_test_objects_all).</summary>
        public static bool TestObjects(string volume, IList<GameObject> objects, bool all = false)
        {
            var v = Volume(volume);
            if (v == null || objects == null || objects.Count == 0) return false;
            for (int i = 0; i < objects.Count; i++)
            {
                var g = objects[i];
                bool inside = g != null && g.activeInHierarchy && Contains(v, CenterOf(g));
                if (all && !inside) return false;
                if (!all && inside) return true;
            }
            return all;
        }

        /// <summary>volume_test_objects (players): a living player inside (solo: the local player; co-op adds the others' feet).</summary>
        public static bool TestPlayers(string volume, bool all = false)
        {
            var v = Volume(volume);
            if (v == null || !HaloLocal.Alive) return false;
            Vector3 p;
            return PlayerPoint(out p) && Contains(v, p);
        }

        /// <summary>volume_teleport_players_not_inside: players outside the volume go to the cutscene flag.</summary>
        public static void TeleportPlayersNotInside(string volume, string cutsceneFlag)
        {
            var v = Volume(volume);
            if (v == null || _data == null) return;
            Vector3 p;
            if (!PlayerPoint(out p) || Contains(v, p)) return;
            var f = _data.Flag(cutsceneFlag);
            if (f == null) { HaloLog.Warn("Campaign: volume_teleport_players_not_inside - no cutscene flag '" + cutsceneFlag + "'"); return; }
            string how;
            HaloCombat.NoteTeleport();
            HaloPlayer.Teleport(HaloSpace.MapOrigin + HaloSpace.Pos(f.Position), 90f - f.Yaw * Mathf.Rad2Deg, out how);
            HaloLog.Info("Campaign: the player was outside '" + volume + "' - teleported to '" + cutsceneFlag + "' (" + how + ")");
        }

        /// <summary>Every game tick: BSP switch volumes (a player inside one whose source is the active BSP).</summary>
        internal static void Step()
        {
            if (_data == null || _data.BspSwitches.Count == 0 || HaloCampaign.SwitchingBsp || !HaloLocal.Alive) return;
            if (HaloCampaign.Ticks - _lastSwitchTick < 15) return;
            Vector3 p;
            if (!PlayerPoint(out p)) return;
            int cur = HaloCampaign.BspIndex;
            for (int i = 0; i < _data.BspSwitches.Count; i++)
            {
                var s = _data.BspSwitches[i];
                if (s.Source != cur || s.TriggerVolume < 0 || s.TriggerVolume >= _data.TriggerVolumes.Count) continue;
                if (s.Destination < 0 || s.Destination == cur) continue;
                if (!Contains(_data.TriggerVolumes[s.TriggerVolume], p)) continue;
                _lastSwitchTick = HaloCampaign.Ticks;
                HaloLog.Info("Campaign: BSP switch volume '" + _data.TriggerVolumes[s.TriggerVolume].Name + "' -> BSP " + s.Destination);
                HaloCampaign.SwitchBsp(s.Destination);
                return;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Keeps the Somnium tablet from opening while a gun is in a hand. The client opens its tablet on a tap (under 0.4 s) of the secondary button of either
    /// hand (B right, Y left: the InputActions "RightHandTablet" / "LeftHandTablet" of XRTabletFeature), the same buttons that drop the magazine here. A world
    /// has no tablet API, so the two actions are switched off for as long as something is held, and switched on again a moment after (only the ones this
    /// switched off). The client's feature is found by its type name and its two InputActionReference fields read by reflection, the way the Euro Truck Sim
    /// Dreamport finds the tablet; no Input System type is referenced. If anything is not found or throws, nothing happens and the tablet works as before.
    /// </summary>
    public static class HaloTabletGuard
    {
        private sealed class Entry
        {
            public string Name;
            public Action Disable, Enable;
            public Func<bool> Enabled;
            public bool WeDisabled;
        }

        private const float ReleaseAfter = 0.8f;       // keep it off this long after the last thing was put down (a tap ends within 0.4 s of its press)
        private static readonly List<Entry> Entries = new List<Entry>();
        private static bool _searched;
        private static int _searches;
        private static float _nextSearch, _lastHeld = -10f;
        private static bool _loggedBlock, _failed;

        /// <summary>Every frame in VR with whether a gun (or anything in the hands that uses those buttons) is held.</summary>
        public static void Update(bool holding)
        {
            if (_failed) return;
            float now = Time.unscaledTime;
            if (holding) _lastHeld = now;
            bool block = holding || now - _lastHeld < ReleaseAfter;
            try
            {
                if (!_searched)
                {
                    if (!holding || now < _nextSearch) return;
                    Search();
                    if (!_searched) return;
                }

                for (int i = 0; i < Entries.Count; i++)
                {
                    Entry e = Entries[i];
                    bool on = e.Enabled();
                    if (block)
                    {
                        if (on) { e.Disable(); e.WeDisabled = true; if (!_loggedBlock) { _loggedBlock = true; HaloLog.Info("Tablet: the client's tablet buttons are switched off while a gun is held (" + e.Name + ")."); } }
                    }
                    else if (e.WeDisabled)
                    {
                        if (!on) e.Enable();
                        e.WeDisabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                _failed = true;
                HaloLog.Warn("Tablet: blocking the client's tablet buttons failed (" + ex.GetType().Name + ": " + ex.Message + "): the tablet opens as before.");
                Release();
            }
        }

        /// <summary>Puts the client's tablet buttons back as they were (leaving the match).</summary>
        public static void Release()
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry e = Entries[i];
                try { if (e.WeDisabled && !e.Enabled()) e.Enable(); } catch (Exception) { /* nothing more to do */ }
                e.WeDisabled = false;
            }

            _lastHeld = -10f;
        }

        private static void Search()
        {
            _searches++;
            _nextSearch = Time.unscaledTime + (_searches < 2 ? 3f : 15f);
            if (_searches > 4) { _searched = true; return; }      // not found after a few tries: the tablet is left alone
            MonoBehaviour[] all = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                MonoBehaviour mb = all[i];
                if (mb == null) continue;
                Type t = mb.GetType();
                if (!IsTabletFeature(t)) continue;
                for (Type c = t; c != null && c != typeof(MonoBehaviour); c = c.BaseType)
                {
                    foreach (FieldInfo f in c.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType.Name != "InputActionReference" || f.Name.IndexOf("Tablet", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        AddReference(f.GetValue(mb), f.Name);
                    }
                }
            }

            if (Entries.Count > 0)
            {
                _searched = true;
                HaloLog.Info("Tablet: found the client's tablet feature (" + Entries.Count + " button action(s)).");
            }
        }

        private static bool IsTabletFeature(Type t)
        {
            for (Type c = t; c != null; c = c.BaseType) if (c.Name == "XRTabletFeature") return true;
            return false;
        }

        private static void AddReference(object reference, string name)
        {
            if (reference == null) return;
            PropertyInfo actionProp = reference.GetType().GetProperty("action", BindingFlags.Instance | BindingFlags.Public);
            object action = actionProp != null ? actionProp.GetValue(reference, null) : null;
            if (action == null) return;
            Type at = action.GetType();
            MethodInfo disable = at.GetMethod("Disable", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            MethodInfo enable = at.GetMethod("Enable", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            PropertyInfo enabled = at.GetProperty("enabled", BindingFlags.Instance | BindingFlags.Public);
            if (disable == null || enable == null || enabled == null) return;
            foreach (Entry old in Entries) if (old.Name == name) return;
            Entries.Add(new Entry
            {
                Name = name,
                Disable = (Action)Delegate.CreateDelegate(typeof(Action), action, disable),
                Enable = (Action)Delegate.CreateDelegate(typeof(Action), action, enable),
                Enabled = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), action, enabled.GetGetMethod()),
            });
        }
    }
}

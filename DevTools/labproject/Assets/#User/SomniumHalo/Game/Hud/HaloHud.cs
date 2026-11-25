using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [HUD module] Halo's own HUD, built from the map's HUD tags (hud globals, the cyborg's unit HUD, the held weapon's weapon HUD, the
    /// grenade HUDs, Halo's fonts, digits and strings): shields and health (top right), ammo / battery / heat of the gun (top left), grenade
    /// counts, the crosshair with its warnings ("Reload", "No Ammo"), the sniper / pistol zoom overlay, the motion tracker (bottom left),
    /// HUD messages, the kill feed in Halo's wording, the score and time (bottom right), objective waypoints, damage arrows, the respawn
    /// countdown, the end of the game and the scoreboard (F1 on a monitor, Halo PC's "show scores" key; hold Y on the left controller in VR).
    /// A monitor gets a ScreenSpaceOverlay canvas (Halo's 4:3 layout pushed to the screen's corners); a headset a head-locked visor
    /// (smoothed) with the reticle at the gun's aim point. Started by the Match module at Playing, stopped on leave.
    /// </summary>
    public static partial class HaloHud
    {
        static HaloHudView _view;
        static bool _subscribed;

        /// <summary>The HUD is up (a match is being played).</summary>
        public static bool Running { get { return _view != null; } }
        public static bool ScoreboardShown { get; private set; }
        static bool _scoreboardForced;

        // ---- optional inputs from other modules (all have fallbacks)

        /// <summary>VR: where the gun points this frame (Combat calls <see cref="SetAim"/>); the reticle sits where this ray hits.
        /// Without it the right hand's forward is used.</summary>
        public static Vector3 AimOrigin, AimDirection;
        public static int AimFrame = -10;
        public static void SetAim(Vector3 origin, Vector3 direction) { AimOrigin = origin; AimDirection = direction; AimFrame = Time.frameCount; }

        /// <summary>The team of the remote player a collider belongs to (Net may set it: -1 none/FFA player, -2 not a player). Used for the
        /// red crosshair over an enemy. Without it a collider on the hit box layer counts as an enemy in free for all only.</summary>
        public static Func<Collider, int> TeamOfCollider;

        /// <summary>The name of a player by net id (Net may set it; default: the match roster's name).</summary>
        public static Func<int, string> NameOf;

        /// <summary>A hit on the local player from a world point (Combat calls it): Halo's red damage arrow towards it.</summary>
        public static void Damaged(Vector3 fromWorld, float amount = 1f)
        {
            if (_view != null) _view.OnDamaged(fromWorld, amount);
        }

        /// <summary>Halo's HUD message text (hud messages.hmt): "pickup", "swap_weapon", "enter_vehicle", ... with the button shown as
        /// <paramref name="button"/> and the item / seat names filled in. Null when the map has no such message.</summary>
        public static string Hmt(string name, string button, string a = null, string b = null)
        {
            var m = HaloHudKit.Messages;
            if (m == null) return null;
            List<HudMessages.Part> parts;
            if (!m.ByName.TryGetValue(name, out parts)) return null;
            var sb = new System.Text.StringBuilder();
            int arg = 0;
            foreach (var p in parts)
            {
                if (!p.Icon) { sb.Append(p.Text); continue; }
                if (p.IconIndex >= 32) { sb.Append(arg++ == 0 ? (a ?? "") : (b ?? "")); continue; }
                sb.Append(button ?? "E");
            }
            return sb.ToString().Trim();
        }

        public static void Start()
        {
            if (_view != null) return;
            HaloMap map = HaloSession.Map;
            if (map == null) { HaloLog.Warn("HUD: Start without a map - nothing to read the HUD from"); return; }
            if (!HaloHudKit.Load(map)) return;
            var parent = HaloGame.Current != null ? HaloGame.Current.transform : null;
            var go = new GameObject("Halo HUD");
            if (parent != null) go.transform.SetParent(parent, false);
            _view = go.AddComponent<HaloHudView>();
            _view.Build();
            Subscribe(true);
            ScoreboardShown = false; _scoreboardForced = false;
            HaloLog.Info("HUD: started on " + map.Name + " (" + (HaloPlayer.XrPresent ? "headset visor" : "monitor overlay") + ")");
        }

        public static void Stop()
        {
            Subscribe(false);
            if (_view != null)
            {
                try { _view.Teardown(); } catch (Exception e) { HaloLog.Warn("HUD: teardown: " + e.Message); }
                UnityEngine.Object.Destroy(_view.gameObject);
                _view = null;
                HaloLog.Info("HUD: stopped");
            }
            ScoreboardShown = false; _scoreboardForced = false;
            HaloHudKit.Release();
            ResetScriptState();
        }

        /// <summary>Shows the scoreboard (the game over screen keeps it up; the player's key / button shows it while held).</summary>
        public static void ShowScoreboard(bool on)
        {
            _scoreboardForced = on;
            ScoreboardShown = on;
        }

        internal static bool ScoreboardForced { get { return _scoreboardForced; } }
        internal static void SetScoreboardHeld(bool held) { ScoreboardShown = _scoreboardForced || held; }

        /// <summary>Lab only: draw the HUD into <paramref name="cam"/>'s view (monitor layout filling its frustum, or the headset visor).</summary>
        public static void LabAttach(Camera cam, bool vr)
        {
            if (_view != null) _view.LabCamera(cam, vr);
        }

        public static HaloHudView View { get { return _view; } }

        static void Subscribe(bool on)
        {
            if (on == _subscribed) return;
            _subscribed = on;
            if (on)
            {
                HaloEvents.Message += OnMessage;
                HaloEvents.Killed += OnKilled;
                HaloEvents.Fired += OnFired;
                HaloEvents.LocalSpawned += OnSpawned;
            }
            else
            {
                HaloEvents.Message -= OnMessage;
                HaloEvents.Killed -= OnKilled;
                HaloEvents.Fired -= OnFired;
                HaloEvents.LocalSpawned -= OnSpawned;
            }
        }

        static void OnMessage(string text, float seconds) { if (_view != null) _view.OnMessage(text, seconds); }
        static void OnKilled(HaloKill k) { if (_view != null) _view.OnKilled(k); }
        static void OnFired(HaloTag w, Vector3 p, Vector3 d, int who) { if (_view != null) _view.OnFired(who); }
        static void OnSpawned(Vector3 p) { if (_view != null) _view.OnSpawned(); }

        internal static string Name(int id)
        {
            var p = HaloRoster.Find(id);
            if (p != null && !string.IsNullOrEmpty(p.Name)) return p.Name;
            if (NameOf != null) { try { string n = NameOf(id); if (!string.IsNullOrEmpty(n)) return n; } catch (Exception) { } }
            if (id == HaloLocal.NetId && !string.IsNullOrEmpty(HaloLocal.Nick)) return HaloLocal.Nick;
            return id >= 0 ? "Player " + (id + 1) : "Someone";
        }
    }
}

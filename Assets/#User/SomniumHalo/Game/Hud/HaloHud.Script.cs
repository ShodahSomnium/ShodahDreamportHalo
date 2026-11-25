using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The campaign scripts' HUD controls (the Script module binds the HaloScript functions to these): show_hud, hud_show_* / hud_blink_*
    /// (health, shield, motion_sensor, crosshair), enable_hud_help_flash, the nav points (activate_[team_]nav_point_flag / _object, drawn
    /// with hud globals' waypoint arrows by type name, e.g. "default_red") and the HUD timer (show_hud_timer, pause_hud_timer,
    /// hud_set_timer_time / _warning_time / _position, hud_get_timer_ticks; d40's countdown). The help text and the objective come from
    /// HaloScriptFns.HelpText / Objective (the script module's state) and show in the HUD's message line.
    /// </summary>
    public static partial class HaloHud
    {
        /// <summary>show_hud false hides the whole HUD (every cutscene does it).</summary>
        public static bool Hidden { get; private set; }
        public static void ShowHud(bool on) { Hidden = !on; }

        public const int PartHealth = 0, PartShield = 1, PartMotionSensor = 2, PartCrosshair = 3;
        static readonly bool[] _partOff = new bool[4];
        static readonly bool[] _partBlink = new bool[4];

        static int PartIndex(string part)
        {
            if (string.IsNullOrEmpty(part)) return -1;
            part = part.ToLowerInvariant();
            if (part.Contains("health")) return PartHealth;
            if (part.Contains("shield")) return PartShield;
            if (part.Contains("motion") || part.Contains("sensor")) return PartMotionSensor;
            if (part.Contains("cross")) return PartCrosshair;
            return -1;
        }

        /// <summary>hud_show_health / shield / motion_sensor / crosshair.</summary>
        public static void ShowPart(string part, bool on) { int i = PartIndex(part); if (i >= 0) _partOff[i] = !on; }
        /// <summary>hud_blink_health / shield / motion_sensor (the a10 training).</summary>
        public static void BlinkPart(string part, bool on) { int i = PartIndex(part); if (i >= 0) _partBlink[i] = on; }

        /// <summary>A part is drawn this frame (shown, and in the "on" half of a blink).</summary>
        public static bool PartVisible(int part)
        {
            if (part < 0 || part >= 4 || _partOff[part]) return false;
            return !_partBlink[part] || Mathf.Repeat(Time.unscaledTime, 0.5f) < 0.3f;      // ours: Halo's HUD blink, ~2 per second
        }

        /// <summary>enable_hud_help_flash: the help text flashes in hud globals' "hud help" colours.</summary>
        public static bool HelpFlashing { get; private set; }
        static float _helpFlashFrom;
        public static void HelpFlash(bool on) { if (on && !HelpFlashing) _helpFlashFrom = Time.unscaledTime; HelpFlashing = on; }
        /// <summary>hud_help_flash_restart.</summary>
        public static void HelpFlashRestart() { _helpFlashFrom = Time.unscaledTime; }
        internal static float HelpFlashTime { get { return Time.unscaledTime - _helpFlashFrom; } }

        // ------------------------------------------------------------------ nav points

        public sealed class Nav { public string Key, Type; public Vector3 At; public Transform Follow; public float OffsetUnits; }
        static readonly List<Nav> _navs = new List<Nav>();
        public static List<Nav> NavPoints { get { return _navs; } }

        /// <summary>activate_[team_]nav_point_flag / _object: a waypoint arrow of hud globals type <paramref name="type"/> ("default_red",
        /// "flag_blue"...) over a world point or following a transform, <paramref name="offsetUnits"/> world units above it. Same key = replaced.</summary>
        public static void NavPoint(string key, string type, Vector3 at, Transform follow, float offsetUnits)
        {
            if (string.IsNullOrEmpty(key)) key = type ?? "nav";
            for (int i = 0; i < _navs.Count; i++)
                if (_navs[i].Key == key) { _navs[i].Type = type; _navs[i].At = at; _navs[i].Follow = follow; _navs[i].OffsetUnits = offsetUnits; return; }
            _navs.Add(new Nav { Key = key, Type = type, At = at, Follow = follow, OffsetUnits = offsetUnits });
            HaloLog.Info("HUD: nav point " + key + " (" + type + ")" + (follow != null ? " on " + follow.name : " at " + at.ToString("0.0")));
        }

        /// <summary>deactivate_[team_]nav_point_flag / _object.</summary>
        public static void ClearNavPoint(string key)
        {
            for (int i = _navs.Count - 1; i >= 0; i--) if (_navs[i].Key == key) _navs.RemoveAt(i);
        }

        // ------------------------------------------------------------------ the HUD timer (d40)

        public static bool TimerShown { get; private set; }
        public static bool TimerPaused { get; private set; }
        static float _timerLeft, _timerWarning = 60f;
        static string _timerCorner = "top_right";
        static int _timerX, _timerY;

        public static void TimerShow(bool on) { TimerShown = on; }
        public static void TimerPause(bool on) { TimerPaused = on; }
        public static void TimerSet(int minutes, int seconds) { _timerLeft = Mathf.Max(0, minutes * 60 + seconds); }
        public static void TimerWarning(int minutes, int seconds) { _timerWarning = Mathf.Max(0, minutes * 60 + seconds); }
        /// <summary>hud_set_timer_position x y corner ("top_left", "top_right", "bottom_left", "bottom_right").</summary>
        public static void TimerPosition(int x, int y, string corner) { _timerX = x; _timerY = y; _timerCorner = corner ?? "top_right"; }
        /// <summary>hud_get_timer_ticks: what is left, in Halo ticks (30 a second).</summary>
        public static int TimerTicks { get { return Mathf.Max(0, Mathf.RoundToInt(_timerLeft * 30f)); } }
        internal static float TimerLeft { get { return _timerLeft; } }
        internal static bool TimerWarningNow { get { return _timerLeft <= _timerWarning; } }
        internal static int TimerX { get { return _timerX; } }
        internal static int TimerY { get { return _timerY; } }
        internal static HudAnchor TimerAnchor
        {
            get
            {
                string c = _timerCorner.ToLowerInvariant();
                bool bottom = c.Contains("bottom"), left = c.Contains("left");
                return bottom ? (left ? HudAnchor.BottomLeft : HudAnchor.BottomRight) : (left ? HudAnchor.TopLeft : HudAnchor.TopRight);
            }
        }

        /// <summary>Called by the HUD each frame (and at Stop / mission start): counts the timer down.</summary>
        internal static void TickTimer(float dt) { if (TimerShown && !TimerPaused && _timerLeft > 0f) _timerLeft = Mathf.Max(0f, _timerLeft - dt); }

        /// <summary>Back to a fresh mission (HUD shown, all parts, no nav points, no timer).</summary>
        internal static void ResetScriptState()
        {
            Hidden = false; HelpFlashing = false;
            for (int i = 0; i < 4; i++) { _partOff[i] = false; _partBlink[i] = false; }
            _navs.Clear();
            TimerShown = false; TimerPaused = false; _timerLeft = 0f; _timerWarning = 60f; _timerCorner = "top_right"; _timerX = 0; _timerY = 0;
        }
    }
}

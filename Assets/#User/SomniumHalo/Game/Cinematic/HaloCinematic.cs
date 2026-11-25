using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Cinematics module, Q] Halo's in-engine cutscenes, driven by the campaign scripts (S binds the script functions to these members):
    /// cinematic_start / stop (<see cref="Begin"/> / <see cref="End"/>: <see cref="Playing"/> holds player input and hides the HUD),
    /// camera_control, camera_set / camera_set_relative / camera_set_first_person / camera_set_dead / camera_time to the scenario's
    /// cutscene camera points (+1264) moving over N ticks, cinematic_show_letterbox, fade_in / fade_out, cinematic_set_title (+1276, text
    /// from the scenario's "ingame help text"), cinematic_set_near_clip_distance, cinematic_skip_start / stop (skip = a fresh press of
    /// Space / Enter / E or a controller's A / X / trigger: reverts to the skip point's checkpoint like Halo), recording_play / kill / time
    /// (HaloCinematic.Recordings.cs: Halo's recorded unit control played on a unit) and the cutscene flags (+1252).
    /// Presentation (HaloCinemaView): VR - the player's own view never moves: the cutscene camera renders into a large floating screen in
    /// front of the player (letterbox, title, fades on it, the world around dimmed); monitor - the cutscene picture takes the screen.
    /// Time: <see cref="Tick"/> at Halo's 30 Hz (K's campaign tick calls it after the scripts; until then the view's own clock does).
    /// </summary>
    public static partial class HaloCinematic
    {
        public const int TicksPerSecond = 30;
        public const float TickSeconds = 1f / TicksPerSecond;

        /// <summary>cinematic_start .. cinematic_stop: player input is held (Combat / Vehicles check this) and the HUD hidden.</summary>
        public static bool Playing { get; private set; }
        /// <summary>camera_control true: the cutscene camera has the view (VR: the floating screen shows).</summary>
        public static bool CameraControlled { get; private set; }
        /// <summary>cinematic_show_letterbox.</summary>
        public static bool Letterboxed { get; private set; }
        /// <summary>The cutscene camera point the camera is at / moving to (-1: none, relative to an object or following a unit).</summary>
        public static int CameraPoint { get; private set; } = -1;
        /// <summary>The cutscene data of the map in play (camera points, flags, titles, recordings).</summary>
        public static HaloCutsceneData Data { get; private set; }
        /// <summary>Game ticks counted by <see cref="Tick"/> since the map's data was loaded.</summary>
        public static int Ticks { get; private set; }
        /// <summary>How far the game is between the last tick and the next (0..1), for smooth camera / unit motion.</summary>
        public static float TickFraction { get; private set; }
        /// <summary>cinematic_suppress_bsp_object_creation (K may read it).</summary>
        public static bool SuppressBspObjectCreation { get; private set; }
        /// <summary>The near clip distance the cutscene camera uses (metres; cinematic_set_near_clip_distance gives world units).</summary>
        public static float NearClip { get; private set; } = DefaultNearClip;
        const float DefaultNearClip = 0.0625f * HaloSpace.Unit; // ours: Halo's usual first person near plane

        public static event Action Started, Stopped, CameraTaken, CameraReleased;

        static HaloMap _dataMap;
        static float _lastExternalTick = -100f;

        // ------------------------------------------------------------------ map data

        /// <summary>Reads the cutscene data of a map (done on first use for HaloSession.Map; the lab calls it directly).</summary>
        public static HaloCutsceneData Load(HaloMap map)
        {
            if (map == null) return Data;
            if (map == _dataMap && Data != null) return Data;
            _dataMap = map;
            try
            {
                Data = new HaloCutsceneData(map.Scenario);
                HaloRecordingSpeed.Clear();
                Ticks = 0;
                HaloLog.Info("Cinematic: " + map.Name + ": " + Data.CameraPoints.Count + " camera points, " + Data.Flags.Count + " cutscene flags, " + Data.Titles.Count
                             + " titles, " + Data.Recordings.Count + " recorded animations (" + Data.RecordingErrors + " unreadable)"
                             + (Data.Titles.Count > 0 ? ", first title \"" + Data.Titles[0].Text + "\"" : ""));
            }
            catch (Exception e)
            {
                Data = null;
                HaloLog.Error("Cinematic: reading the cutscene data of " + map.Name + " failed: " + e);
            }
            return Data;
        }

        static HaloCutsceneData D
        {
            get
            {
                var m = HaloSession.Map;
                if (m != null && m != _dataMap) Load(m);
                return Data;
            }
        }

        // ------------------------------------------------------------------ cinematic_start / stop

        public static void Begin()
        {
            HaloCinemaView.Ensure();
            if (Playing) return;
            Playing = true;
            Once("start", "Cinematic: cinematic_start (player input held, HUD hidden) - " + (HaloPlayer.XrPresent ? "VR cinema screen" : "monitor picture"));
            Raise(Started, "Started");
        }

        public static void End()
        {
            if (!Playing) return;
            Playing = false;
            Raise(Stopped, "Stopped");
        }

        /// <summary>camera_control: true = the cutscene camera has the view.</summary>
        public static void CameraControl(bool on)
        {
            HaloCinemaView.Ensure();
            if (on == CameraControlled) return;
            CameraControlled = on;
            if (on)
            {
                // Halo keeps the scripted camera where it was last set; a fresh control starts at the player's eye until camera_set
                if (!_camValid) StartAtPlayer();
                Raise(CameraTaken, "CameraTaken");
            }
            else
            {
                _mode = CamMode.Points;
                Raise(CameraReleased, "CameraReleased");
            }
        }

        /// <summary>cinematic_show_letterbox.</summary>
        public static void Letterbox(bool on) { HaloCinemaView.Ensure(); Letterboxed = on; }

        /// <summary>cinematic_set_near_clip_distance (world units).</summary>
        public static void SetNearClip(float worldUnits) { NearClip = Mathf.Max(0.005f, worldUnits * HaloSpace.Unit); }

        /// <summary>cinematic_suppress_bsp_object_creation.</summary>
        public static void SuppressBspObjects(bool on) { SuppressBspObjectCreation = on; }

        // ------------------------------------------------------------------ fades

        static Color _fadeColour = Color.black;
        static float _fadeFrom, _fadeTo, _fadeTicks, _fadeElapsed;

        /// <summary>fade_in (from the colour to the picture) / fade_out (to the colour): r, g, b 0..1 over <paramref name="ticks"/>.</summary>
        public static void Fade(bool fadeIn, Color colour, int ticks)
        {
            HaloCinemaView.Ensure();
            _fadeColour = new Color(colour.r, colour.g, colour.b, 1f);
            _fadeFrom = fadeIn ? 1f : FadeAlpha();
            _fadeTo = fadeIn ? 0f : 1f;
            _fadeTicks = Mathf.Max(0, ticks);
            _fadeElapsed = 0f;
        }

        /// <summary>The fade's colour and cover (alpha 0 = no fade) now.</summary>
        public static Color FadeColour
        {
            get
            {
                var c = _fadeColour; c.a = FadeAlpha();
                float x = _xAlpha;
                if (x > c.a) { c = Color.Lerp(new Color(c.r, c.g, c.b, 1f), Color.black, x <= 0f ? 0f : (x - c.a) / x); c.a = x; }
                return c;
            }
        }

        static float FadeAlpha()
        {
            if (_fadeTicks <= 0f) return _fadeTo;
            float t = Mathf.Clamp01((_fadeElapsed + TickFraction) / _fadeTicks);
            return Mathf.Lerp(_fadeFrom, _fadeTo, t);
        }

        // ------------------------------------------------------------------ titles

        static HaloCutsceneData.Title _title;
        static float _titleAge, _titleDelay;

        /// <summary>cinematic_set_title.</summary>
        public static void Title(string cutsceneTitle) { TitleDelayed(cutsceneTitle, 0f); }

        /// <summary>cinematic_set_title_delayed (seconds).</summary>
        public static void TitleDelayed(string cutsceneTitle, float seconds)
        {
            HaloCinemaView.Ensure();
            var d = D;
            var t = d != null ? d.FindTitle(cutsceneTitle) : null;
            if (t == null) { Once("title?" + cutsceneTitle, "Cinematic: no cutscene title \"" + cutsceneTitle + "\""); return; }
            ShowTitle(t, seconds);
        }

        public static void Title(int index)
        {
            HaloCinemaView.Ensure();
            var d = D;
            var t = d != null ? d.FindTitle(index) : null;
            if (t != null) ShowTitle(t, 0f);
        }

        static void ShowTitle(HaloCutsceneData.Title t, float delaySeconds)
        {
            _title = t; _titleAge = 0f; _titleDelay = Mathf.Max(0f, delaySeconds) * TicksPerSecond;
            Once("title " + t.Name, "Cinematic: title \"" + t.Name + "\" = \"" + t.Text + "\" (in " + t.FadeIn + " / up " + t.Up + " / out " + t.FadeOut + " ticks)");
        }

        /// <summary>The title showing now and its opacity (null when none).</summary>
        public static HaloCutsceneData.Title CurrentTitle(out float alpha)
        {
            alpha = 0f;
            var t = _title;
            if (t == null) return null;
            float age = _titleAge + TickFraction - _titleDelay;
            if (age < 0f) return null;
            if (age < t.FadeIn) alpha = t.FadeIn > 0f ? age / t.FadeIn : 1f;
            else if (age < t.FadeIn + t.Up) alpha = 1f;
            else if (age < t.FadeIn + t.Up + t.FadeOut) alpha = t.FadeOut > 0f ? 1f - (age - t.FadeIn - t.Up) / t.FadeOut : 0f;
            else return null;
            return t;
        }

        // ------------------------------------------------------------------ cutscene flags

        /// <summary>A cutscene flag's Unity position (world root applied) and Halo facing as a Unity yaw (degrees). False when unknown.</summary>
        public static bool Flag(string name, out Vector3 position, out float yawDegrees)
        {
            var d = D;
            var f = d != null ? d.FindFlag(name) : null;
            if (f == null) { position = Vector3.zero; yawDegrees = 0f; return false; }
            position = ToWorld(HaloSpace.Pos(f.Position));
            yawDegrees = HaloSpace.Yaw(f.Yaw).eulerAngles.y;
            return true;
        }

        /// <summary>A cutscene flag's Unity rotation (yaw and pitch).</summary>
        public static bool Flag(string name, out Vector3 position, out Quaternion rotation)
        {
            var d = D;
            var f = d != null ? d.FindFlag(name) : null;
            if (f == null) { position = Vector3.zero; rotation = Quaternion.identity; return false; }
            position = ToWorld(HaloSpace.Pos(f.Position));
            rotation = HaloSpace.Euler(f.Yaw, f.Pitch, 0f);
            return true;
        }

        internal static Vector3 ToWorld(Vector3 mapPosition)
        {
            var root = HaloSession.WorldRoot;
            return root != null ? root.TransformPoint(mapPosition) : mapPosition + HaloSpace.MapOrigin;
        }

        // ------------------------------------------------------------------ skip (cinematic_skip_start / cinematic_skip_stop)

        /// <summary>Between cinematic_skip_start_internal and cinematic_skip_stop_internal: a skip press reverts to the skip point.</summary>
        public static bool SkipAvailable { get; private set; }
        /// <summary>Set when the player skipped (cleared by <see cref="ConsumeSkip"/>); the scripts' game_reverted check does the rest.</summary>
        public static bool SkipRequested { get; private set; }
        /// <summary>Raised when the player skips a cutscene (Audio may stop cutscene dialogue).</summary>
        public static event Action Skipped;
        /// <summary>What a skip does (default: <see cref="HaloCampaign.Revert"/>, Halo's own way: cinematic_skip_start took a checkpoint
        /// with game_save_totally_unsafe, the revert brings the game back to it and (not (game_reverted)) makes the script jump the
        /// cutscene). S / K may replace it; return false when it could not skip.</summary>
        public static Func<bool> SkipHandler;

        public static void SkipStartInternal() { SkipAvailable = true; SkipRequested = false; _skipArmed = false; Once("skip", "Cinematic: skip point (a fresh press of Space / Enter / E or A / X / a trigger skips)"); }
        public static void SkipStopInternal() { SkipAvailable = false; }
        public static bool ConsumeSkip() { bool r = SkipRequested; SkipRequested = false; return r; }

        static bool _skipArmed;

        /// <summary>Skips now (input or a menu button): returns true when a skip point was open.</summary>
        public static bool Skip()
        {
            if (!SkipAvailable) return false;
            SkipAvailable = false;
            SkipRequested = true;
            HaloLog.Info("Cinematic: skipped by the player at tick " + Ticks + (CameraPoint >= 0 && Data != null ? " (camera at " + Data.CameraPoints[CameraPoint].Name + ")" : ""));
            Raise(Skipped, "Skipped");
            bool done = false;
            try
            {
                if (SkipHandler != null) done = SkipHandler();
                else if (HaloCampaign.Active) { HaloCampaign.Revert(); done = true; }
            }
            catch (Exception e) { HaloLog.Error("Cinematic: skip handler: " + e); }
            if (!done) Once("skipfail", "Cinematic: skip requested but nothing reverted (no campaign / no handler)");
            return done;
        }

        static void SkipInput()
        {
            if (!SkipAvailable || !Playing) { _skipArmed = false; return; }
            bool down = SkipDown();
            if (!_skipArmed) { if (!down) _skipArmed = true; return; } // a press must start after the cutscene did
            if (down && !_skipWasDown) Skip();
            _skipWasDown = down;
        }

        static bool _skipWasDown, _keysBroken;

        static bool SkipDown()
        {
            bool d = false;
            if (!_keysBroken)
            {
                try { d = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.KeypadEnter); }
                catch (Exception) { _keysBroken = true; }
            }
            if (!d && HaloPlayer.XrPresent)
            {
                try
                {
                    var r = HaloXr.Hand(true); var l = HaloXr.Hand(false);
                    d = r.Primary || r.Trigger > 0.8f || l.Primary || l.Trigger > 0.8f;
                }
                catch (Exception) { }
            }
            return d;
        }

        // ------------------------------------------------------------------ time

        /// <summary>One Halo game tick (30 a second): camera moves, fades, titles, recordings. K's campaign tick calls it after the scripts.</summary>
        public static void Tick()
        {
            _lastExternalTick = Time.unscaledTime;
            EnsureSaveParts();
            Step();
        }

        /// <summary>The view's clock while nobody calls <see cref="Tick"/> (lab, before K wires it).</summary>
        internal static void SelfClock(float dt)
        {
            if (Time.unscaledTime - _lastExternalTick < 0.25f)
            {
                TickFraction = HaloCampaign.Active ? HaloCampaign.TickFraction : TickFraction;
                return;
            }
            _acc += Mathf.Min(dt, 0.25f);
            int n = 0;
            while (_acc >= TickSeconds && n < 4) { _acc -= TickSeconds; Step(); n++; }
            if (_acc > TickSeconds) _acc = 0f;
            TickFraction = Mathf.Clamp01(_acc / TickSeconds);
        }

        static float _acc;

        static void Step()
        {
            Ticks++;
            if (_fadeTicks > 0f && _fadeElapsed < _fadeTicks) _fadeElapsed += 1f;
            if (_title != null) _titleAge += 1f;
            CameraStep();
            RecordingsStep();
            PlayerStep();
        }

        /// <summary>Per frame (the view calls it): skip input.</summary>
        internal static void Frame() { SkipInput(); }

        // ------------------------------------------------------------------ clear

        /// <summary>Everything off (mission leave, revert): camera, letterbox, fades, titles, recordings, skip point.</summary>
        public static void Clear()
        {
            bool was = Playing || CameraControlled;
            Playing = false;
            CameraControlled = false;
            Letterboxed = false;
            CameraPoint = -1;
            _camValid = false; _mode = CamMode.Points; _moveTicks = 0; _relative = null; _follow = null;
            _title = null;
            _fadeTicks = 0f; _fadeTo = 0f; _fadeFrom = 0f;
            SkipAvailable = false; SkipRequested = false;
            NearClip = DefaultNearClip;
            SuppressBspObjectCreation = false;
            ScreenEffectStop();
            KillAllRecordings();
            ClearPlayer();
            if (was) { Raise(CameraReleased, "CameraReleased"); Raise(Stopped, "Stopped"); }
        }

        // ------------------------------------------------------------------ helpers

        static readonly System.Collections.Generic.HashSet<string> _once = new System.Collections.Generic.HashSet<string>();

        internal static void Once(string key, string message)
        {
            if (_once.Add(key)) HaloLog.Info(message);
        }

        static void Raise(Action h, string what)
        {
            if (h == null) return;
            foreach (Action d in h.GetInvocationList())
            {
                try { d(); } catch (Exception e) { HaloLog.Error("Cinematic: " + what + " listener: " + e); }
            }
        }
    }
}

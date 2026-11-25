using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// What the campaign scripts need beyond multiplayer: per sound class gains with a ramp (sound_class_set_gain "device_machinery" 0 0),
    /// stopping a playing impulse sound (sound_impulse_stop), dialogue from a unit's head marker, and the ambience following BSP switches.
    /// </summary>
    public sealed partial class HaloAudioSystem
    {
        /// <summary>Halo's SoundClass enum as the scripts name it (spaces as underscores), index = value.</summary>
        internal static readonly string[] ClassNames =
        {
            "projectile_impact", "projectile_detonation", "unused", "unused1", "weapon_fire", "weapon_ready", "weapon_reload", "weapon_empty",
            "weapon_charge", "weapon_overheat", "weapon_idle", "unused2", "unused3", "object_impacts", "particle_impacts", "slow_particle_impacts",
            "unused4", "unused5", "unit_footsteps", "unit_dialog", "unused6", "unused7", "vehicle_collision", "vehicle_engine", "unused8",
            "unused9", "device_door", "device_force_field", "device_machinery", "device_nature", "device_computers", "unused10", "music",
            "ambient_nature", "ambient_machinery", "ambient_computers", "unused11", "unused12", "unused13", "first_person_damage", "unused14",
            "unused15", "unused16", "unused17", "scripted_dialog_player", "scripted_effect", "scripted_dialog_other",
            "scripted_dialog_force_unspatialized", "unused18", "unused19", "game_event",
        };

        readonly float[] _classGain = new float[64], _classTo = new float[64], _classRate = new float[64];
        bool _classRamp, _classInit;

        internal static int ClassIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            string n = name.Trim().Replace(' ', '_');
            for (int i = 0; i < ClassNames.Length; i++) if (string.Equals(ClassNames[i], n, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        void InitClassGains()
        {
            if (_classInit) return;
            _classInit = true;
            for (int i = 0; i < _classGain.Length; i++) { _classGain[i] = 1f; _classTo[i] = 1f; _classRate[i] = 0f; }
        }

        internal float ClassGain(int cls) { InitClassGains(); return cls >= 0 && cls < _classGain.Length ? _classGain[cls] : 1f; }

        /// <summary>sound_class_set_gain: the class's gain goes to <paramref name="gain"/> over <paramref name="seconds"/> (0 = at once).</summary>
        internal bool SetClassGain(string cls, float gain, float seconds)
        {
            InitClassGains();
            int i = ClassIndex(cls);
            if (i < 0) return false;
            gain = Mathf.Clamp01(gain);
            _classTo[i] = gain;
            if (seconds <= 0.001f) { _classGain[i] = gain; _classRate[i] = 0f; }
            else { _classRate[i] = Mathf.Abs(gain - _classGain[i]) / seconds; _classRamp = true; }
            return true;
        }

        internal void ResetClassGains() { _classInit = false; InitClassGains(); _classRamp = false; }

        void TickClassGains(float dt)
        {
            if (!_classRamp) return;
            bool any = false;
            for (int i = 0; i < _classGain.Length; i++)
            {
                if (_classRate[i] <= 0f) continue;
                _classGain[i] = Mathf.MoveTowards(_classGain[i], _classTo[i], _classRate[i] * dt);
                if (Mathf.Approximately(_classGain[i], _classTo[i])) _classRate[i] = 0f; else any = true;
            }
            _classRamp = any;
        }

        /// <summary>sound_impulse_stop: every voice playing (or about to play) this sound stops now (a short fade, no click).</summary>
        internal int StopSound(HaloTag tag)
        {
            if (tag == null) return 0;
            HaloAudioBank b;
            if (!_banks.TryGetValue(tag, out b) || b == null) return 0;
            int n = 0;
            for (int i = 0; i < _voices.Count; i++) if (!_voices[i].Done && _voices[i].Uses(b)) { _voices[i].Stop(0.05f); n++; }
            // a queued announcer line of it
            return n;
        }

        internal bool IsPlaying(HaloTag tag)
        {
            if (tag == null) return false;
            HaloAudioBank b;
            if (!_banks.TryGetValue(tag, out b) || b == null) return false;
            for (int i = 0; i < _voices.Count; i++) if (!_voices[i].Done && _voices[i].Uses(b)) return true;
            foreach (var q in _announce) if (q == tag) return true;
            return false;
        }

        /// <summary>Dialogue speaks from the unit's head: its "head" model marker when the transform is a built HaloUnit's.</summary>
        internal static Transform SpeakerOf(Transform t, HaloSound s)
        {
            if (t == null || s == null) return t;
            int c = s.Class;
            if (c != HaloSound.ClassUnitDialog && c != HaloSound.ClassScriptedDialogOther && c != HaloSound.ClassScriptedDialogPlayer) return t;
            try
            {
                var u = t.GetComponentInParent<HaloUnit>();
                if (u == null) u = t.GetComponentInChildren<HaloUnit>();
                var head = u != null ? u.Marker("head") : null;
                return head != null ? head : t;
            }
            catch (Exception) { return t; }
        }

        /// <summary>The player skipped a cutscene: its dialogue stops (the script jumps past it; music is the script's own business).</summary>
        void OnCinematicSkipped()
        {
            int n = 0;
            for (int i = 0; i < _voices.Count; i++)
            {
                var v = _voices[i];
                var e = v.Current;
                if (v.Done || e == null) continue;
                int c = e.Sound.Class;
                if (c == HaloSound.ClassUnitDialog || c == HaloSound.ClassScriptedDialogPlayer || c == HaloSound.ClassScriptedDialogOther
                    || c == HaloSound.ClassScriptedDialogForceUnspatialized) { v.Stop(0.1f); n++; }
            }
            if (n > 0) HaloLog.Info("Audio: cutscene skipped - " + n + " dialogue line(s) stopped");
        }

        // ---- ambience: the background sound palette of the BSP the campaign switched to
        void OnBspSwitched(int from, int to)
        {
            try { if (_ambienceOn) LoadBackground(HaloSession.Scenario, to); } catch (Exception e) { HaloLog.Warn("Audio: ambience on the BSP switch: " + e.Message); }
        }
    }
}

using System;
using System.Text;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>Client diagnostics: 20 s after a match / mission starts, one "Audio: diag" line describing the listener and every voice
    /// (playing, muted, inactive, volume, distance against the max distance), so a Player.log shows why something is not heard.</summary>
    public sealed partial class HaloAudioSystem
    {
        float _diagAt = -1f;
        int _diagCount;

        // per sound class (SoundClass enum): one-shots asked for, started, skipped (skip fraction), dropped (cap / not decoded in time), loops
        readonly int[] _statReq = new int[64], _statPlay = new int[64], _statSkip = new int[64], _statDrop = new int[64], _statLoop = new int[64], _mapClassTags = new int[64];
        int _statNoSound;
        readonly System.Collections.Generic.Dictionary<HaloTag, int> _tagReq = new System.Collections.Generic.Dictionary<HaloTag, int>();
        float _coverageAt = -1f;

        static void Stat(int[] a, int cls) { if (cls >= 0 && cls < a.Length) a[cls]++; }

        void ResetStats(HaloMap map)
        {
            Array.Clear(_statReq, 0, 64); Array.Clear(_statPlay, 0, 64); Array.Clear(_statSkip, 0, 64); Array.Clear(_statDrop, 0, 64); Array.Clear(_statLoop, 0, 64); Array.Clear(_mapClassTags, 0, 64);
            _statNoSound = 0; _tagReq.Clear();
            if (map == null) return;
            foreach (var t in map.OfClass("snd!")) { var b = Bank(t); if (b != null) Stat(_mapClassTags, b.Sound.Class); }
        }

        /// <summary>Per sound class: the map's tags, one-shots asked / started / skipped / dropped, loops started; and the classes the map has
        /// sounds for that nothing asked for yet ("never").</summary>
        internal string Coverage()
        {
            var sb = new StringBuilder();
            sb.Append("class tags:asked/started/skipped/dropped+loops -");
            var never = new StringBuilder();
            for (int i = 0; i < ClassNames.Length; i++)
            {
                if (_mapClassTags[i] == 0 && _statReq[i] == 0 && _statLoop[i] == 0) continue;
                if (_statReq[i] == 0 && _statLoop[i] == 0) { never.Append(' ').Append(ClassNames[i]).Append('(').Append(_mapClassTags[i]).Append(')'); continue; }
                sb.Append(' ').Append(ClassNames[i]).Append(' ').Append(_mapClassTags[i]).Append(':').Append(_statReq[i]).Append('/').Append(_statPlay[i]).Append('/').Append(_statSkip[i]).Append('/').Append(_statDrop[i]);
                if (_statLoop[i] > 0) sb.Append('+').Append(_statLoop[i]);
            }
            sb.Append("; never asked:").Append(never.Length > 0 ? never.ToString() : " none").Append("; not a sound tag: ").Append(_statNoSound);
            // the most asked-for one-shot sounds
            var top = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<HaloTag, int>>(_tagReq);
            top.Sort((x, y) => y.Value.CompareTo(x.Value));
            sb.Append("; most asked:");
            for (int i = 0; i < top.Count && i < 6; i++) sb.Append(' ').Append(top[i].Key.Name).Append(" x").Append(top[i].Value);
            sb.Append("; unit voice: ").Append(HaloUnitVoice.Stats()).Append("; footsteps: ").Append(HaloFootsteps.Played).Append(" played, ").Append(HaloFootsteps.Silent).Append(" silent");
            return sb.ToString();
        }

        void OnPhaseForDiag(HaloSession.Phase from, HaloSession.Phase to)
        {
            if (to == HaloSession.Phase.Playing) { _diagAt = Time.unscaledTime + 20f; _coverageAt = Time.unscaledTime + 120f; }
            if (to == HaloSession.Phase.Loading) ResetStats(null);
        }

        void TickDiag()
        {
            if (_coverageAt > 0f && Time.unscaledTime >= _coverageAt) { _coverageAt = Time.unscaledTime + 600f; if (_diagCount < 12) { _diagCount++; HaloLog.Info("Audio: coverage - " + Coverage()); } }
            if (_diagAt < 0f || Time.unscaledTime < _diagAt) return;
            _diagAt = -1f;
            if (_diagCount++ < 6) HaloLog.Info("Audio: diag - " + Diag() + " | " + Coverage());
        }

        /// <summary>The active listener: an enabled AudioListener on an active object (the client may keep disabled ones around).</summary>
        internal AudioListener ActiveListener(out int total)
        {
            total = 0;
            AudioListener best = null;
            AudioListener[] all;
            try { all = FindObjectsByType<AudioListener>(FindObjectsSortMode.None); } catch (Exception) { return null; }
            foreach (var l in all)
            {
                total++;
                if (l != null && l.enabled && l.gameObject.activeInHierarchy) { best = l; }
            }
            return best;
        }

        internal string Diag()
        {
            var sb = new StringBuilder();
            int n;
            var l = ActiveListener(out n);
            Vector3 lp = l != null ? l.transform.position : ListenerPosition();
            sb.Append("listener ").Append(l != null ? l.name : "NONE ACTIVE").Append(" at ").Append(lp.ToString("F1")).Append(" (").Append(n).Append(" listeners), AudioListener.volume ")
              .Append(AudioListener.volume.ToString("0.##")).Append(AudioListener.pause ? " PAUSED" : "").Append("; ");
            int playing = 0, audible = 0, muted = 0, inactive = 0, silent = 0, far = 0, d2 = 0, d3 = 0, waiting = 0;
            string sample = null;
            foreach (var v in _voices)
            {
                if (v.Done) continue;
                var s = v.ActiveSource;
                if (s == null) { waiting++; continue; }
                if (!s.gameObject.activeInHierarchy || !s.enabled) { inactive++; continue; }
                if (s.mute) muted++;
                if (!s.isPlaying) continue;
                playing++;
                if (s.spatialBlend < 0.5f) d2++; else d3++;
                float dist = (s.transform.position - lp).magnitude;
                bool inRange = s.spatialBlend < 0.5f || dist < s.maxDistance;
                if (!inRange) far++;
                if (s.volume <= 0.0001f) silent++;
                if (inRange && !s.mute && s.volume > 0.0001f) { audible++; if (sample == null && s.spatialBlend > 0.5f) sample = s.clip.name + " vol " + s.volume.ToString("0.###") + " at " + dist.ToString("0.0") + "/" + s.maxDistance.ToString("0") + " m"; }
            }
            sb.Append(_voices.Count).Append(" voices: ").Append(playing).Append(" playing (").Append(d2).Append(" 2D, ").Append(d3).Append(" 3D), ").Append(audible).Append(" audible, ")
              .Append(far).Append(" beyond max distance, ").Append(silent).Append(" at volume 0, ").Append(muted).Append(" muted, ").Append(inactive).Append(" inactive, ").Append(waiting).Append(" waiting for a clip");
            if (sample != null) sb.Append(" (e.g. ").Append(sample).Append(")");
            sb.Append("; one-shots ").Append(_oneShots).Append("/").Append(MaxVoices).Append(", dropped ").Append(_dropped);
            sb.Append("; sources ").Append(_free.Count).Append(" free / ").Append(_sourceCount).Append("; root ").Append(gameObject.activeInHierarchy ? "active" : "INACTIVE");
            sb.Append("; master ").Append(HaloAudio.MasterVolume.ToString("0.##")).Append(", groups");
            for (int i = 0; i < GroupNames.Length; i++) sb.Append(' ').Append(GroupNames[i]).Append('=').Append(_groupGain[i].ToString("0.##"));
            sb.Append("; class gains below 1:");
            InitClassGains();
            bool any = false;
            for (int i = 0; i < ClassNames.Length; i++) if (_classGain[i] < 0.999f) { sb.Append(' ').Append(ClassNames[i]).Append('=').Append(_classGain[i].ToString("0.##")); any = true; }
            if (!any) sb.Append(" none");
            var cfg = AudioSettings.GetConfiguration();
            sb.Append("; real voices ").Append(cfg.numRealVoices).Append(", virtual ").Append(cfg.numVirtualVoices);
            return sb.ToString();
        }
    }
}

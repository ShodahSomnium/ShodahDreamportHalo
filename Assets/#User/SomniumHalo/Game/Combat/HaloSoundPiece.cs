using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Plays a piece of a game sound at a weapon (a hand reload's action: the matching part of the weapon's reload sound, see
    /// HaloReloadSounds). Whole sounds go through HaloAudio; a piece is cut from the decoded clip (HaloAudio.ClipOf) on a pooled 3D
    /// AudioSource that follows the gun and fades out at the piece's end (30 ms). A clip not decoded yet is asked for and the piece skipped
    /// (the gun asks for its reload sounds when it is built). Volume: HaloAudio.MasterVolume x the "weapon" group.
    /// </summary>
    public sealed class HaloSoundPiece : MonoBehaviour
    {
        private static readonly Stack<HaloSoundPiece> Free = new Stack<HaloSoundPiece>();
        private static Transform _root;
        /// <summary>Log every action's sound (the lab).</summary>
        public static bool LogPlays;
        public static int Played;

        private AudioSource _src;
        private Transform _follow;
        private float _end, _gain;

        /// <summary>Plays <paramref name="p"/> at <paramref name="at"/>; <paramref name="action"/> names it for the log. False when there was nothing to play.</summary>
        public static bool Play(HaloReloadSounds.Piece p, Transform at, string action, string weapon)
        {
            if (p == null || p.Sound == null || at == null) return false;
            try
            {
                if (p.Length <= 0f)
                {
                    HaloAudio.Play(p.Sound, at, 1f);
                    Note(action, weapon, p);
                    return true;
                }

                AudioClip clip = HaloAudio.ClipOf(p.Sound);
                if (clip == null || p.Start >= clip.length) { if (LogPlays) HaloLog.Info("Combat: sound " + action + " for " + weapon + ": " + p.Sound.Name + " not decoded yet (skipped)"); return false; }
                HaloSoundPiece v = Free.Count > 0 ? Free.Pop() : Make();
                v.gameObject.SetActive(true);
                v._follow = at;
                v.transform.position = at.position;
                v._gain = Mathf.Clamp01(HaloAudio.MasterVolume * HaloAudio.GroupVolume("weapon"));
                v._src.clip = clip;
                v._src.volume = v._gain;
                v._src.time = Mathf.Min(p.Start, clip.length - 0.01f);
                v._end = Time.time + Mathf.Min(p.Length, clip.length - p.Start);
                v._src.Play();
                Note(action, weapon, p);
                return true;
            }
            catch (Exception e) { HaloLog.Warn("Combat: sound " + action + " failed: " + e.Message); return false; }
        }

        /// <summary>Asks for a sound's decode ahead (pieces need the clip).</summary>
        public static void Warm(HaloReloadSounds.Piece p) { if (p != null && p.Sound != null && p.Length > 0f) HaloAudio.ClipOf(p.Sound); }

        private static void Note(string action, string weapon, HaloReloadSounds.Piece p)
        {
            Played++;
            if (LogPlays) HaloLog.Info("Combat: sound " + action + " for " + weapon + ": " + p);
        }

        private static HaloSoundPiece Make()
        {
            if (_root == null) { _root = new GameObject("Halo Reload Sounds").transform; if (HaloGame.Current != null) _root.SetParent(HaloGame.Current.transform, false); }
            var go = new GameObject("piece");
            go.transform.SetParent(_root, false);
            var v = go.AddComponent<HaloSoundPiece>();
            v._src = go.AddComponent<AudioSource>();
            v._src.playOnAwake = false;
            v._src.spatialBlend = 1f;
            v._src.rolloffMode = AudioRolloffMode.Logarithmic;
            v._src.minDistance = 1f;
            v._src.maxDistance = 40f;
            v._src.dopplerLevel = 0f;
            return v;
        }

        private void Update()
        {
            if (_follow != null) transform.position = _follow.position;
            float left = _end - Time.time;
            if (left < 0.03f) _src.volume = _gain * Mathf.Clamp01(left / 0.03f);
            if (left <= 0f || !_src.isPlaying)
            {
                _src.Stop();
                _src.clip = null;
                _follow = null;
                gameObject.SetActive(false);
                Free.Push(this);
            }
        }
    }
}

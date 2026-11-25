using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// One playing sound: a queue of entries played gaplessly on two AudioSources (the next one is scheduled on the DSP clock where
    /// the current one ends), so permutation chains, streamed music pieces and random loop permutations follow each other without
    /// a gap. A looping voice picks a new random permutation of its loop sound each time the queue runs dry, in the pitch range its
    /// current pitch selects (engines: the range changes with the pitch; a jump out of the playing range crossfades at once).
    /// Main thread only; no allocations per frame.
    /// </summary>
    internal sealed class HaloVoice
    {
        public HaloAudioSystem Sys;
        public readonly AudioSource[] Src = new AudioSource[2];
        readonly HaloAudioEntry[] _on = new HaloAudioEntry[2];
        readonly double[] _start = new double[2];
        readonly bool[] _exact = new bool[2];
        readonly float[] _startPitch = new float[2];
        HaloSound _rgSound;
        float _rgValue = 1f;
        readonly float[] _srcGain = new float[2], _srcRandPitch = new float[2];
        readonly float[] _fade = { 1f, 1f }, _fadeTo = { 1f, 1f }, _fadeRate = { 0f, 0f };
        int _active = -1;
        bool _scheduled;
        readonly List<HaloAudioEntry> _queue = new List<HaloAudioEntry>(4);

        /// <summary>Repeat random permutations of this bank once the queue is empty (null = a one-shot).</summary>
        public HaloAudioBank LoopBank;
        public bool Spatial = true;
        public Vector3 Position;
        public Transform Follow;
        public bool Following;
        /// <summary>Caller gain (x track gain), the Halo scale (0..1: zero/one modifiers) and the Halo pitch (1 = natural).</summary>
        public float Gain = 1f, Scale = 1f, Pitch = 1f;
        /// <summary>Fade of the whole voice (loop track fades), 0..1.</summary>
        public float VoiceFade = 1f;
        float _voiceFadeTo = 1f, _voiceFadeRate;
        public int Group;
        public bool Done;
        public float WaitLimit = 0.5f;   // a one-shot whose clip is not decoded within this is dropped // ours
        float _born;
        float _pitchNow = -1f;
        bool _stopAfterFade;
        public int Priority = 128;
        /// <summary>A loop track's voice (not counted against the one-shot limit).</summary>
        public bool Looping;
        /// <summary>One-shots: the sound it plays, how important it is (0 particle / object impacts .. 3 dialogue, UI), when it began.</summary>
        public HaloAudioBank OneShotBank;
        public int Importance;
        public float Born;
        /// <summary>Diagnostics: scheduled joins done, and frames in which the active source had already ended (a gap).</summary>
        public int Joins, GapFrames;

        public bool Started => _active >= 0;
        public bool Playing => !Done && _active >= 0;

        public void Init(HaloAudioSystem sys)
        {
            Sys = sys; _born = Time.unscaledTime;
        }

        /// <summary>Adds the entries of one permutation (joined or its pieces) to the queue and asks for their decode.</summary>
        public void Enqueue(HaloAudioBank bank, int pr, int perm)
        {
            int before = _queue.Count;
            bank.Sequence(pr, perm, _queue);
            for (int i = before; i < _queue.Count; i++) _queue[i].Users++;
            if (_queue.Count > before) Sys.Request(_queue[before]);
        }

        public void Stop(float fadeSeconds)
        {
            LoopBank = null;
            ClearQueue();
            if (fadeSeconds <= 0.001f || _active < 0) { Finish(); return; }
            _voiceFadeTo = 0f; _voiceFadeRate = VoiceFade / fadeSeconds; _stopAfterFade = true;
            if (_scheduled) { int o = 1 - _active; ReleaseSource(o); _scheduled = false; }
        }

        public void FadeTo(float target, float seconds)
        {
            _voiceFadeTo = target;
            _voiceFadeRate = seconds <= 0.001f ? 1000f : Mathf.Abs(target - VoiceFade) / seconds;
            if (_voiceFadeRate <= 0f) _voiceFadeRate = 1000f;
        }

        void ClearQueue()
        {
            for (int i = 0; i < _queue.Count; i++) Sys.Unuse(_queue[i]);
            _queue.Clear();
        }

        public void Finish()
        {
            if (Done) return;
            Done = true;
            ClearQueue();
            ReleaseSource(0); ReleaseSource(1);
            _active = -1; _scheduled = false;
        }

        void ReleaseSource(int i)
        {
            if (Src[i] != null) { Sys.ReturnSource(Src[i]); Src[i] = null; }
            if (_on[i] != null) { Sys.Unuse(_on[i]); _on[i] = null; }
            _fade[i] = 1f; _fadeTo[i] = 1f; _fadeRate[i] = 0f;
        }

        /// <summary>The Halo pitch that picks the loop's pitch range: caller pitch x the loop sound's scale pitch modifier.</summary>
        float RangePitch(HaloSound s) => _pitchNow * Mathf.Lerp(s.ZeroPitch, s.OnePitch, Scale);

        HaloAudioEntry NextEntry()
        {
            if (_queue.Count == 0 && LoopBank != null)
            {
                int pr = LoopBank.Sound.PitchRangeFor(RangePitch(LoopBank.Sound));
                Enqueue(LoopBank, pr, LoopBank.PickPermutation(pr, Sys.Rng));
            }
            return _queue.Count > 0 ? _queue[0] : null;
        }

        void StartOn(int i, HaloAudioEntry e, double at, double dspNow)
        {
            if (Src[i] == null) Src[i] = Sys.TakeSource();
            var src = Src[i];
            if (src == null) { Sys.Unuse(e); return; }
            if (_on[i] != null) Sys.Unuse(_on[i]);
            _on[i] = e;   // (the queue's use passes to the source)
            var s = e.Sound;
            var perm = s.PitchRanges[e.Pr].Permutations[e.Perm];
            // random gain: once per sound instance (a loop keeps its level from permutation to permutation)
            if (_rgSound != s) { _rgSound = s; _rgValue = s.RandomGain < 1f ? Mathf.Lerp(s.RandomGain, 1f, (float)Sys.Rng.NextDouble()) : 1f; }
            float rg = _rgValue;
            _srcGain[i] = rg * (perm.Gain > 0f ? perm.Gain : 1f);
            float rp = s.RandomPitchMin != s.RandomPitchMax ? Mathf.Lerp(s.RandomPitchMin, s.RandomPitchMax, (float)Sys.Rng.NextDouble()) : s.RandomPitchMin;
            _srcRandPitch[i] = rp / s.PitchRanges[e.Pr].NaturalPitch;
            Sys.Configure(src, s, Spatial, Priority);
            src.clip = e.Clip;
            src.loop = false;
            src.timeSamples = 0;
            ApplySource(i);
            if (at <= dspNow + 0.001) { src.Play(); _start[i] = dspNow; _exact[i] = false; }
            else { src.PlayScheduled(at); _start[i] = at; _exact[i] = true; }
            _startPitch[i] = src.pitch;
        }

        float Remaining(int i, double dspNow)
        {
            var src = Src[i];
            var clip = src != null ? src.clip : null;
            if (clip == null) return 0f;
            float rate = clip.frequency * Mathf.Max(0.01f, Mathf.Abs(src.pitch));
            // started on the DSP clock at a known time and the pitch did not move: the end is exact (gapless joins)
            if (_exact[i] && Mathf.Abs(src.pitch - _startPitch[i]) < 1e-4f) return (float)(_start[i] + clip.samples / (double)rate - dspNow);
            _exact[i] = false;
            if (dspNow < _start[i]) return (float)(_start[i] - dspNow) + clip.samples / rate;
            return (clip.samples - src.timeSamples) / rate;
        }

        public void Tick(double dspNow, float dt)
        {
            if (Done) return;
            if (Following)
            {
                if (Follow != null) Position = Follow.position;
                else
                {
                    Following = false;
                    if (LoopBank != null) { Stop(0.05f); }   // the object of a loop is gone
                }
            }
            // pitch: the loop sound's maximum bend per second limits how fast it follows (engines)
            float bend = LoopBank != null ? LoopBank.Sound.MaxBendPerSecond : 0f;
            if (_pitchNow < 0f || bend <= 0f) _pitchNow = Pitch;
            else _pitchNow = Mathf.MoveTowards(_pitchNow, Pitch, bend * dt);

            if (_voiceFadeRate > 0f)
            {
                VoiceFade = Mathf.MoveTowards(VoiceFade, _voiceFadeTo, _voiceFadeRate * dt);
                if (Mathf.Approximately(VoiceFade, _voiceFadeTo)) { _voiceFadeRate = 0f; if (_stopAfterFade && VoiceFade <= 0.001f) { Finish(); return; } }
            }
            for (int i = 0; i < 2; i++)
            {
                if (_fadeRate[i] <= 0f) continue;
                _fade[i] = Mathf.MoveTowards(_fade[i], _fadeTo[i], _fadeRate[i] * dt);
                if (Mathf.Approximately(_fade[i], _fadeTo[i]))
                {
                    _fadeRate[i] = 0f;
                    if (_fadeTo[i] <= 0.001f && i != _active) ReleaseSource(i);
                }
            }

            if (_active < 0)
            {
                var e = NextEntry();
                if (e == null) { Finish(); return; }
                if (e.Failed || e.Dead) { _queue.RemoveAt(0); Sys.Unuse(e); if (_queue.Count == 0 && LoopBank == null) Finish(); return; }
                if (!e.Ready)
                {
                    Sys.Request(e);
                    if (LoopBank == null && Time.unscaledTime - _born > WaitLimit && WaitLimit > 0f) { Sys.CountDropped(e.Sound); Finish(); }
                    return;
                }
                _queue.RemoveAt(0);
                // a sequence (loop, chain pieces) starts on the DSP clock so every join after it is sample exact; a lone one-shot at once
                bool more = _queue.Count > 0 || LoopBank != null;
                StartOn(0, e, more ? dspNow + 0.04 : 0, dspNow);
                _active = 0;
                if (!Looping) Sys.CountStarted(e.Sound);
                if (Src[0] == null) { Finish(); return; }
            }
            else
            {
                int a = _active, o = 1 - a;
                if (_scheduled && dspNow >= _start[o])
                {
                    ReleaseSource(a);
                    _active = o; a = o; o = 1 - a; _scheduled = false;
                    Joins++;
                }
                var src = Src[a];
                if (src == null) { Finish(); return; }

                // engines: the pitch left the playing range -> crossfade to the right range now
                if (LoopBank != null && LoopBank.Sound.PitchRanges.Length > 1 && _fadeRate[0] <= 0f && _fadeRate[1] <= 0f)
                    CheckRangeJump(dspNow);
                a = _active; o = 1 - a; src = Src[a];
                if (src == null) { Finish(); return; }

                float remain = Remaining(a, dspNow);
                bool ended = !src.isPlaying && dspNow > _start[a] + 0.05;
                if (ended) GapFrames++;
                // a loop picks its next permutation a few seconds ahead so a long one (music) is decoded before it is due
                if (LoopBank != null && _queue.Count == 0 && remain < 3f) NextEntry();
                if (!_scheduled && Src[o] == null && remain < 0.35f)
                {
                    var n = NextEntry();
                    if (n != null && (n.Failed || n.Dead)) { _queue.RemoveAt(0); Sys.Unuse(n); n = null; }
                    if (n != null && n.Ready)
                    {
                        _queue.RemoveAt(0);
                        StartOn(o, n, ended ? 0 : dspNow + Mathf.Max(0f, remain), dspNow);
                        if (Src[o] != null) _scheduled = true;
                    }
                    else if (n != null) Sys.Request(n);
                    else if (ended) { Finish(); return; }
                }
                else if (ended && !_scheduled && _queue.Count == 0 && LoopBank == null && Src[o] == null) { Finish(); return; }
                // decode ahead: the next two pieces
                for (int k = 0; k < _queue.Count && k < 2; k++) if (!_queue[k].Ready) Sys.Request(_queue[k]);
            }
            ApplySource(0); ApplySource(1);
        }

        void CheckRangeJump(double dspNow)
        {
            int a = _active, o = 1 - a;
            var e = _on[a];
            if (e == null || e.Bank != LoopBank) return;
            var s = LoopBank.Sound;
            float p = RangePitch(s);
            var r = s.PitchRanges[e.Pr];
            const float margin = 0.05f;   // ours
            if (p >= r.BendMin - margin && p <= r.BendMax + margin) return;
            int want = s.PitchRangeFor(p);
            if (want == e.Pr) return;
            // drop what was queued / scheduled for the old range
            if (_scheduled) { ReleaseSource(o); _scheduled = false; }
            ClearQueue();
            Enqueue(LoopBank, want, LoopBank.PickPermutation(want, Sys.Rng));
            var n = _queue[0];
            if (!n.Ready) { Sys.Request(n); return; }   // keep the old range until the new one is decoded
            _queue.RemoveAt(0);
            StartOn(o, n, 0, dspNow);
            if (Src[o] == null) return;
            const float xfade = 0.12f;    // ours
            _fade[o] = 0f; _fadeTo[o] = 1f; _fadeRate[o] = 1f / xfade;
            _fadeTo[a] = 0f; _fadeRate[a] = 1f / xfade;
            _active = o;
            ApplySource(o);
        }

        void ApplySource(int i)
        {
            var src = Src[i];
            var e = _on[i];
            if (src == null || e == null) return;
            var s = e.Sound;
            float g = Gain * _srcGain[i] * Mathf.Lerp(s.ZeroGain, s.OneGain, Scale) * _fade[i] * VoiceFade * Sys.GroupGain(Group) * Sys.ClassGain(s.Class);
            src.volume = Mathf.Clamp01(g);
            float pitch = (_pitchNow < 0f ? Pitch : _pitchNow) * Mathf.Lerp(s.ZeroPitch, s.OnePitch, Scale) * _srcRandPitch[i];
            src.pitch = Mathf.Clamp(pitch, 0.05f, 3f);
            if (Spatial) src.transform.position = Position;
        }

        /// <summary>Plays, has queued, or loops entries of this bank.</summary>
        public bool Uses(HaloAudioBank b)
        {
            if (LoopBank == b) return true;
            for (int i = 0; i < 2; i++) if (_on[i] != null && _on[i].Bank == b) return true;
            for (int i = 0; i < _queue.Count; i++) if (_queue[i].Bank == b) return true;
            return false;
        }

        /// <summary>Loops another bank from the next permutation on (what was picked ahead from the old one is dropped).</summary>
        public void SetLoopBank(HaloAudioBank b)
        {
            if (LoopBank == b) return;
            LoopBank = b;
            ClearQueue();
        }

        /// <summary>Music stop: no more loop permutations; the playing one ends, then <paramref name="end"/> (if any) follows gaplessly.</summary>
        public void EndWith(HaloAudioBank end, System.Random rng)
        {
            LoopBank = null;
            ClearQueue();
            if (end != null) { int pr = end.Sound.PitchRangeFor(1f); Enqueue(end, pr, end.PickPermutation(pr, rng)); }
            if (_active < 0 && end == null) Finish();
        }

        /// <summary>The entry on the active source (lab / inspection).</summary>
        public HaloAudioEntry Current => _active >= 0 ? _on[_active] : null;
        public AudioSource ActiveSource => _active >= 0 ? Src[_active] : null;
    }
}

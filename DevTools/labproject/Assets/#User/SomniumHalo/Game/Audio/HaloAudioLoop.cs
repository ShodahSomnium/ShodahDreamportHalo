using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A looping sound being played: an lsnd's tracks (start sound, then random permutations of the loop sound, or of the alternate
    /// loop while "alternate" is on; the end / alternate end sound at stop; fade in at start and fade out at stop as the track's flags
    /// say) and its detail sounds (one-shots every random period, at a random yaw / pitch / distance around the source). A snd! given
    /// to Loop plays as a one-track loop. Main thread only.
    /// </summary>
    internal sealed class HaloAudioLoop
    {
        public HaloAudio.Handle Handle;
        public HaloAudioSystem Sys;
        public HaloLoopingSound L;
        public HaloTag Tag;
        public Transform At;
        public bool Following, Spatial = true;
        public Vector3 Position;
        public float Gain = 1f, Scale = 1f, Pitch = 1f;
        public bool Alternate;
        public int Group = -1;
        public bool Done, Stopping;

        sealed class TrackState
        {
            public HaloLoopingSound.Track Def;
            public HaloVoice Voice;
            public bool Alt;
        }

        TrackState[] _tracks;
        float[] _detailAt;

        public bool Start()
        {
            if (L != null)
            {
                _tracks = new TrackState[L.Tracks.Length];
                for (int i = 0; i < _tracks.Length; i++)
                {
                    var d = L.Tracks[i];
                    var t = new TrackState { Def = d };
                    _tracks[i] = t;
                    var loopBank = Sys.Bank(Alternate && d.AlternateLoop != null ? d.AlternateLoop : d.Loop);
                    t.Alt = Alternate && d.AlternateLoop != null;
                    var startBank = Sys.Bank(d.Start);
                    if (loopBank == null && startBank == null) continue;
                    var v = Sys.NewVoice(loopBank != null ? loopBank.Sound : startBank.Sound, Group);
                    if (v == null) continue;
                    v.WaitLimit = 0f; v.Looping = true;
                    v.LoopBank = (L.Flags & HaloLoopingSound.FlagNotALoop) != 0 ? null : loopBank;
                    if (startBank != null) EnqueueOne(v, startBank);
                    if (v.LoopBank == null && loopBank != null) EnqueueOne(v, loopBank);
                    if ((d.Flags & HaloLoopingSound.TrackFadeInAtStart) != 0 && d.FadeIn > 0f) { v.VoiceFade = 0f; v.FadeTo(1f, d.FadeIn); }
                    t.Voice = v;
                }
                _detailAt = new float[L.Details.Length];
                for (int i = 0; i < _detailAt.Length; i++) _detailAt[i] = Time.unscaledTime + DetailPeriod(L.Details[i]);
            }
            else
            {
                var bank = Sys.Bank(Tag);
                if (bank == null) return false;
                var v = Sys.NewVoice(bank.Sound, Group);
                if (v == null) return false;
                v.WaitLimit = 0f; v.Looping = true;
                v.LoopBank = bank;
                _tracks = new[] { new TrackState { Voice = v } };
                _detailAt = new float[0];
            }
            Apply(0f);
            return true;
        }

        void EnqueueOne(HaloVoice v, HaloAudioBank b)
        {
            int pr = b.Sound.PitchRangeFor(Pitch * Mathf.Lerp(b.Sound.ZeroPitch, b.Sound.OnePitch, Scale));
            v.Enqueue(b, pr, b.PickPermutation(pr, Sys.Rng));
        }

        float DetailPeriod(HaloLoopingSound.Detail d)
        {
            float p = Mathf.Lerp(d.PeriodMin, Mathf.Max(d.PeriodMin, d.PeriodMax), (float)Sys.Rng.NextDouble());
            p *= Mathf.Lerp(L.ZeroDetailPeriod, L.OneDetailPeriod, Scale);
            return Mathf.Max(0.1f, p);
        }

        /// <summary>Stops: end sounds play, tracks with "fade out at stop" fade over their fade-out time, the rest stop at once.</summary>
        public void Stop(bool immediate, float fadeOverride)
        {
            if (Stopping) return;
            Stopping = true;
            if (_tracks == null) { Done = true; return; }
            foreach (var t in _tracks)
            {
                if (t == null || t.Voice == null) continue;
                var d = t.Def;
                var end = !immediate && d != null ? (t.Alt && d.AlternateEnd != null ? d.AlternateEnd : d.End) : null;
                bool fadeFlag = d != null && (d.Flags & HaloLoopingSound.TrackFadeOutAtStop) != 0;
                // music (scripts start / stop it): the playing permutation ends, then the end piece follows on the beat
                if (!immediate && fadeOverride < 0f && !fadeFlag && t.Voice.Started && t.Voice.Group == HaloAudioSystem.GMusic)
                {
                    t.Voice.EndWith(Sys.Bank(end), Sys.Rng);
                    continue;
                }
                if (end != null && t.Voice.Started) Sys.PlayOneShot(end, Position, Following ? At : null, Spatial, Gain * (d.Gain > 0f ? d.Gain : 1f), Scale, Pitch, Group);
                float fade = fadeOverride >= 0f ? fadeOverride
                    : !immediate && d != null && (d.Flags & HaloLoopingSound.TrackFadeOutAtStop) != 0 ? d.FadeOut : 0.03f;   // 0.03: no click // ours
                t.Voice.Stop(fade);
            }
        }

        public void Tick(float dt)
        {
            if (Done) return;
            if (Following)
            {
                if (At != null) Position = At.position;
                else { Following = false; Stop(true, 0.05f); }
            }
            bool alive = false;
            if (_tracks != null)
                foreach (var t in _tracks)
                {
                    if (t == null || t.Voice == null) continue;
                    if (!t.Voice.Done) alive = true;
                    // alternate loop: switch at the next permutation boundary
                    if (!Stopping && t.Def != null && t.Def.AlternateLoop != null && t.Alt != Alternate)
                    {
                        t.Alt = Alternate;
                        var b = Sys.Bank(Alternate ? t.Def.AlternateLoop : t.Def.Loop);
                        if (b != null && t.Voice.LoopBank != null) t.Voice.SetLoopBank(b);
                    }
                }
            Apply(dt);
            if (!Stopping && L != null)
                for (int i = 0; i < _detailAt.Length; i++)
                {
                    if (Time.unscaledTime < _detailAt[i]) continue;
                    var d = L.Details[i];
                    _detailAt[i] = Time.unscaledTime + DetailPeriod(d);
                    if ((d.Flags & HaloLoopingSound.DetailNotWithAlternate) != 0 && Alternate) continue;
                    if ((d.Flags & HaloLoopingSound.DetailOnlyWithAlternate) != 0 && !Alternate) continue;
                    if (d.Sound == null) continue;
                    float yaw = Mathf.Lerp(d.YawMin, d.YawMax, (float)Sys.Rng.NextDouble());
                    float pitch = Mathf.Lerp(d.PitchMin, d.PitchMax, (float)Sys.Rng.NextDouble());
                    float dist = Mathf.Lerp(d.DistanceMin, d.DistanceMax, (float)Sys.Rng.NextDouble()) * HaloSpace.Unit;
                    Vector3 dir = new Vector3(Mathf.Cos(pitch) * Mathf.Sin(yaw), Mathf.Sin(pitch), Mathf.Cos(pitch) * Mathf.Cos(yaw));
                    Vector3 center = Spatial ? Position : Sys.ListenerPosition();
                    Sys.PlayOneShot(d.Sound, center + dir * dist, null, true, Gain * (d.Gain > 0f ? d.Gain : 1f), Scale, 1f, Group);
                }
            if (!alive && (Stopping || _tracks == null || AllStarted())) { Done = true; }
        }

        bool AllStarted()
        {
            foreach (var t in _tracks) if (t != null && t.Voice != null && !t.Voice.Done) return false;
            return true;
        }

        void Apply(float dt)
        {
            if (_tracks == null) return;
            foreach (var t in _tracks)
            {
                if (t == null || t.Voice == null) continue;
                var v = t.Voice;
                v.Gain = Gain * (t.Def != null && t.Def.Gain > 0f ? t.Def.Gain : 1f);
                v.Scale = Scale; v.Pitch = Pitch;
                v.Spatial = Spatial; v.Position = Position;
            }
        }

        /// <summary>Fades every track in from silence (music).</summary>
        public void FadeIn(float seconds)
        {
            if (_tracks == null || seconds <= 0f) return;
            foreach (var t in _tracks) if (t != null && t.Voice != null) { t.Voice.VoiceFade = 0f; t.Voice.FadeTo(1f, seconds); }
        }

        /// <summary>Lab / inspection: the voice of a track.</summary>
        public HaloVoice TrackVoice(int i) => _tracks != null && i < _tracks.Length && _tracks[i] != null ? _tracks[i].Voice : null;
        public int TrackCount => _tracks != null ? _tracks.Length : 0;
    }
}

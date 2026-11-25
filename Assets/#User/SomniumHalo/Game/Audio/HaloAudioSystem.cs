using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The engine behind <see cref="HaloAudio"/>: a worker thread decodes permutations (Xbox ADPCM / Ogg Vorbis from MAPS\sounds.map,
    /// Data/Audio), the main thread turns them into AudioClips (AudioClip.Create + SetData, a budget of floats a frame - the route
    /// the Somnium client plays; OnAudioFilterRead is never called for world code), keeps them per sound tag, and runs the voices
    /// (pooled AudioSources), loops, the announcer queue and the music. Created on first use under the HaloGame object.
    /// Clips of a match map are released when the match unloads; ui.map's (menu sounds, title music) stay.
    /// </summary>
    public sealed partial class HaloAudioSystem : MonoBehaviour
    {
        public static HaloAudioSystem Instance { get; private set; }

        // volume groups (index into _groupGain); settings "audio.<name>"
        internal const int GEffect = 0, GWeapon = 1, GVehicle = 2, GAmbient = 3, GAnnouncer = 4, GMusic = 5, GUi = 6;
        internal static readonly string[] GroupNames = { "effect", "weapon", "vehicle", "ambient", "announcer", "music", "ui" };
        readonly float[] _groupGain = { 1f, 1f, 1f, 1f, 1f, 1f, 1f };

        /// <summary>Floats turned into clips per frame (about 1 MB of PCM, well under a millisecond). // ours</summary>
        public const int UploadFloatsPerFrame = 262144;
        /// <summary>Most ONE-SHOT voices at once (one-shots past this are dropped; loops - ambience, engines, music - are not counted:
        /// a30 alone runs 124 sound scenery loops, which once used up the old shared limit and silenced every one-shot). // ours</summary>
        public const int MaxVoices = 72;
        int _oneShots;

        internal readonly System.Random Rng = new System.Random();
        readonly Dictionary<HaloTag, HaloAudioBank> _banks = new Dictionary<HaloTag, HaloAudioBank>();
        readonly List<HaloVoice> _voices = new List<HaloVoice>();
        readonly List<HaloAudioLoop> _loops = new List<HaloAudioLoop>();
        readonly List<AudioSource> _free = new List<AudioSource>();
        int _sourceCount;
        readonly Dictionary<int, AnimationCurve> _curves = new Dictionary<int, AnimationCurve>();

        struct Job { public HaloAudioEntry E; public HaloSound S; public string SoundsPath; }
        struct Result { public HaloAudioEntry E; public float[] Samples; public int Channels, Rate; public float Peak, Rms; public string Error; public double Ms; }
        // two queues: what a voice waits for now (first), and the preload behind it (a dialogue line must not wait for 400 preloads)
        readonly BlockingCollection<Job> _jobs = new BlockingCollection<Job>(), _background = new BlockingCollection<Job>();
        BlockingCollection<Job>[] _queues;
        bool _preloading;
        readonly ConcurrentQueue<Result> _results = new ConcurrentQueue<Result>();
        Thread _thread;
        volatile bool _stop;

        // stats
        int _clips, _failed, _dropped, _decoded, _stepFails;
        long _floats;
        double _decodeMs;
        int _preloadLeft;
        float _preloadStarted;
        string _preloadMap;
        float _preloadSeconds;

        // announcer
        HaloMap _mpMap;
        HaloTag[] _mpSounds;
        readonly Queue<HaloTag> _announce = new Queue<HaloTag>();
        readonly Queue<float> _announceAt = new Queue<float>();
        HaloVoice _announcer;

        // music + ui.map
        HaloAudioLoop _music;
        volatile HaloMap _ui;
        volatile bool _uiLoading;

        bool _pendingTitle;
        AudioListener _listener;
        float _listenerLookAt;

        internal static HaloAudioSystem Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Halo Audio");
            if (HaloGame.Current != null) go.transform.SetParent(HaloGame.Current.transform, false);
            Instance = go.AddComponent<HaloAudioSystem>();
            return Instance;
        }

        void Awake()
        {
            Instance = this;
            _queues = new[] { _jobs, _background };
            _thread = new Thread(Worker, 1024 * 1024) { IsBackground = true, Name = "Halo audio decode" };
            _thread.Start();
            HaloSession.Unloading += OnUnloading;
            HaloSession.WorldBuilt += OnWorldBuilt;
            HaloSession.PhaseChanged += OnPhaseForDiag;
            HaloUnitVoice.Hook();
            HaloCampaign.BspSwitched += OnBspSwitched;
            HaloCinematic.Skipped += OnCinematicSkipped;
            HaloSettings.Changed += OnSetting;
            ReadSettings();
            int buf, n; AudioSettings.GetDSPBufferSize(out buf, out n);
            HaloLog.Info("Audio: started - output " + AudioSettings.outputSampleRate + " Hz, " + AudioSettings.driverCapabilities + ", dsp buffer " + buf + " x " + n);
        }

        void OnDestroy()
        {
            _stop = true;
            try { _jobs.CompleteAdding(); _background.CompleteAdding(); } catch (Exception) { }
            HaloSession.Unloading -= OnUnloading;
            HaloSession.WorldBuilt -= OnWorldBuilt;
            HaloSession.PhaseChanged -= OnPhaseForDiag;
            HaloUnitVoice.Unhook();
            HaloCampaign.BspSwitched -= OnBspSwitched;
            HaloCinematic.Skipped -= OnCinematicSkipped;
            HaloSettings.Changed -= OnSetting;
            foreach (var b in _banks.Values) foreach (var e in b.Entries()) { e.Dead = true; if (e.Clip != null) Destroy(e.Clip); e.Clip = null; }
            _banks.Clear();
            if (Instance == this) Instance = null;
        }

        void OnSetting(string key)
        {
            if (key == null) return;
            if (string.Equals(key, "volume", StringComparison.OrdinalIgnoreCase) || key.StartsWith("audio.", StringComparison.OrdinalIgnoreCase)) ReadSettings();
        }

        void ReadSettings()
        {
            HaloAudio.MasterVolume = Mathf.Clamp01(HaloSettings.Volume);
            for (int i = 0; i < GroupNames.Length; i++) _groupGain[i] = Mathf.Clamp(HaloSettings.Num("audio." + GroupNames[i], 1f), 0f, 2f);
        }

        internal float GroupGain(int g) => HaloAudio.MasterVolume * (g >= 0 && g < _groupGain.Length ? _groupGain[g] : 1f);

        internal static int GroupOf(HaloSound s)
        {
            switch (HaloSound.ClassGroup(s.Class))
            {
                case "weapon": return GWeapon;
                case "vehicle": return GVehicle;
                case "ambient": return GAmbient;
                case "announcer": return GAnnouncer;
                case "music": return GMusic;
                default: return GEffect;
            }
        }

        // ------------------------------------------------------------------ worker

        void Worker()
        {
            while (!_stop)
            {
                Job j;
                try { if (BlockingCollection<Job>.TryTakeFromAny(_queues, out j, 250) < 0) continue; }
                catch (Exception) { return; }
                if (j.E == null)
                {
                    LoadUi(j.SoundsPath);
                    continue;
                }
                var r = new Result { E = j.E };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    if (j.E.Dead || j.E.Decoded) continue;
                    j.E.Decoded = true;
                    var res = HaloResources.Get(j.SoundsPath);
                    HaloPcm pcm;
                    if (j.E.Piece)
                    {
                        int ch, rate;
                        var f = HaloPcm.DecodePiece(j.S, j.S.PitchRanges[j.E.Pr].Permutations[j.E.Perm], res, out ch, out rate);
                        pcm = new HaloPcm { Samples = f, Channels = ch, Rate = rate };
                    }
                    else pcm = HaloPcm.Decode(j.S, j.E.Pr, j.E.Perm, res);
                    pcm.Levels(out r.Peak, out r.Rms);
                    r.Samples = pcm.Samples; r.Channels = pcm.Channels; r.Rate = pcm.Rate;
                }
                catch (Exception e) { r.Error = e.GetType().Name + ": " + e.Message; }
                r.Ms = sw.Elapsed.TotalMilliseconds;
                _results.Enqueue(r);
            }
        }

        void LoadUi(string path)
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var m = new HaloMap(path);
                _ui = m;
                HaloLog.Info("Audio: ui.map read (" + CountSounds(m) + " sounds, " + sw.ElapsedMilliseconds + " ms)");
            }
            catch (Exception e) { HaloLog.Warn("Audio: reading ui.map failed: " + e.Message); }
            _uiLoading = false;
        }

        static int CountSounds(HaloMap m) { int n = 0; foreach (var t in m.OfClass("snd!")) n++; return n; }

        static string SoundsPathOf(HaloMap map)
        {
            try { return Path.Combine(Path.GetDirectoryName(map.Path), "sounds.map"); } catch (Exception) { return null; }
        }

        internal void Request(HaloAudioEntry e)
        {
            if (e == null || e.Ready || e.Failed || e.Dead) return;
            if (e.Requested)
            {
                // asked for in the preload and wanted now: once more at the front (the worker decodes it once)
                if (e.Background && !_preloading) { e.Background = false; try { _jobs.Add(new Job { E = e, S = e.Sound, SoundsPath = SoundsPathOf(e.Sound.Tag.Map) }); } catch (Exception) { } }
                return;
            }
            e.Requested = true; e.RequestedAt = Time.unscaledTime; e.Background = _preloading;
            try { (_preloading ? _background : _jobs).Add(new Job { E = e, S = e.Sound, SoundsPath = SoundsPathOf(e.Sound.Tag.Map) }); }
            catch (Exception) { e.Failed = true; }
        }

        internal void Unuse(HaloAudioEntry e)
        {
            if (e == null) return;
            e.Users--;
            if (e.Users <= 0) { e.Users = 0; if (e.Streamed) ReleaseClip(e); }
        }

        void ReleaseClip(HaloAudioEntry e)
        {
            if (e.Clip != null) { _floats -= (long)e.Clip.samples * e.Clip.channels; _clips--; Destroy(e.Clip); e.Clip = null; }
            e.Requested = false; e.Decoded = false;
        }

        void Upload()
        {
            int budget = UploadFloatsPerFrame;
            Result r;
            while (budget > 0 && _results.TryDequeue(out r))
            {
                var e = r.E;
                _decodeMs += r.Ms;
                if (e.Dead || e.Clip != null) continue;
                if (r.Error != null || r.Samples == null)
                {
                    e.Failed = true; _failed++;
                    HaloLog.Warn("Audio: decoding " + e.Name + " failed: " + (r.Error ?? "no samples"));
                    PreloadStep();
                    continue;
                }
                if (e.Streamed && e.Users <= 0) { e.Requested = false; e.Decoded = false; continue; }   // nobody waits for this piece any more
                int frames = r.Channels > 0 ? r.Samples.Length / r.Channels : 0;
                if (frames <= 0) { e.Failed = true; _failed++; PreloadStep(); continue; }
                try
                {
                    var clip = AudioClip.Create(e.Name, frames, r.Channels, r.Rate, false);
                    clip.SetData(r.Samples, 0);
                    e.Clip = clip;
                    e.Seconds = (float)frames / r.Rate; e.Peak = r.Peak; e.Rms = r.Rms;
                    _clips++; _decoded++; _floats += r.Samples.Length;
                    budget -= r.Samples.Length;
                    if (!e.Streamed) _preloadSeconds += e.Seconds;
                }
                catch (Exception ex) { e.Failed = true; _failed++; HaloLog.Warn("Audio: clip " + e.Name + ": " + ex.Message); }
                PreloadStep();
            }
        }

        void PreloadStep()
        {
            if (_preloadLeft <= 0) return;
            if (--_preloadLeft == 0)
                HaloLog.Info("Audio: preloaded " + _preloadMap + " - " + _clips + " clips, " + _preloadSeconds.ToString("0") + " s of sound, "
                    + (_floats * 4 / (1024 * 1024)) + " MB PCM, " + _failed + " failed, decode " + _decodeMs.ToString("0") + " ms worker, "
                    + (Time.unscaledTime - _preloadStarted).ToString("0.0") + " s wall");
        }

        // ------------------------------------------------------------------ banks / preload

        internal HaloAudioBank Bank(HaloTag t)
        {
            if (t == null) return null;
            HaloAudioBank b;
            if (_banks.TryGetValue(t, out b)) return b;
            HaloSound s = null;
            try { s = HaloSound.Get(t); }
            catch (Exception e) { HaloLog.Warn("Audio: reading " + t + " failed: " + e.Message); }
            if (s == null || s.PitchRanges.Length == 0) { _banks[t] = null; return null; }
            b = new HaloAudioBank(s);
            _banks[t] = b;
            return b;
        }

        /// <summary>Classes decoded ahead at map open: weapons (not impacts), footsteps, vehicles, first person damage, game events,
        /// ambience and devices (491 s / 52 MB of PCM on Blood Gulch, lab). Impacts, dialog, music and scripted effects decode on
        /// first use (a frame or two late, once). // ours</summary>
        static bool PreloadClass(int c)
        {
            if (c >= HaloSound.ClassProjectileDetonation && c <= HaloSound.ClassWeaponIdle) return true;
            if (c >= HaloSound.ClassDeviceDoor && c <= HaloSound.ClassDeviceComputers) return true;
            if (c >= HaloSound.ClassAmbientNature && c <= HaloSound.ClassAmbientComputers) return true;
            return c == HaloSound.ClassUnitFootsteps || c == HaloSound.ClassVehicleCollision || c == HaloSound.ClassVehicleEngine
                || c == HaloSound.ClassFirstPersonDamage || c == HaloSound.ClassGameEvent;
        }

        internal void Preload(HaloMap map)
        {
            if (map == null) return;
            int n = 0;
            _preloading = true;
            Array.Clear(_mapClassTags, 0, _mapClassTags.Length);
            try
            {
            foreach (var t in map.OfClass("snd!"))
            {
                var b = Bank(t);
                if (b == null) continue;
                Stat(_mapClassTags, b.Sound.Class);
                int c = b.Sound.Class;
                if (!PreloadClass(c)) continue;
                for (int pr = 0; pr < b.Sound.PitchRanges.Length; pr++)
                    for (int p = 0; p < b.Sound.PitchRanges[pr].ActualPermutationCount; p++)
                    {
                        if (b.IsLong(pr, p)) continue;
                        var tmp = new List<HaloAudioEntry>(1);
                        b.Sequence(pr, p, tmp);
                        var e = tmp[0];
                        if (e.Ready || e.Requested) continue;
                        Request(e); n++;
                    }
            }
            // the announcer lines too (unit dialog class is skipped above)
            foreach (var t in MpSounds(map))
            {
                var b = Bank(t);
                if (b == null) continue;
                var tmp = new List<HaloAudioEntry>(1);
                b.Sequence(0, 0, tmp);
                if (!tmp[0].Ready && !tmp[0].Requested && !tmp[0].Piece) { Request(tmp[0]); n++; }
            }
            }
            finally { _preloading = false; }
            _preloadLeft += n; _preloadStarted = Time.unscaledTime; _preloadMap = map.Name;
            HaloLog.Info("Audio: preloading " + n + " sound clips of " + map.Name);
        }

        void OnUnloading()
        {
            if (_diagCount++ < 12) HaloLog.Info("Audio: coverage at unload - " + Coverage());
            StopAmbience();
            var m = HaloSession.Map;
            if (m != null) Forget(m);
        }

        /// <summary>Stops everything of one map and frees its clips.</summary>
        internal void Forget(HaloMap map)
        {
            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                var v = _voices[i];
                var c = v.Current;
                if (c != null && c.Sound.Tag.Map == map) v.Finish();
                else if (v.LoopBank != null && v.LoopBank.Sound.Tag.Map == map) v.Finish();
            }
            for (int i = _loops.Count - 1; i >= 0; i--)
                if ((_loops[i].Tag != null && _loops[i].Tag.Map == map)) { _loops[i].Stop(true, 0f); }
            var drop = new List<HaloTag>();
            int clips = 0;
            foreach (var kv in _banks)
            {
                if (kv.Key.Map != map) continue;
                drop.Add(kv.Key);
                if (kv.Value == null) continue;
                foreach (var e in kv.Value.Entries()) { e.Dead = true; if (e.Clip != null) { clips++; ReleaseClip(e); } }
            }
            foreach (var t in drop) _banks.Remove(t);
            HaloSound.Forget(map); HaloLoopingSound.Forget(map);
            _preloadLeft = 0;
            ResetClassGains();
            if (_mpMap == map) { _mpMap = null; _mpSounds = null; }
            _announce.Clear(); _announceAt.Clear();
            HaloLog.Info("Audio: released " + clips + " clips of " + map.Name);
        }

        // ------------------------------------------------------------------ sources

        internal AudioSource TakeSource()
        {
            while (_free.Count > 0)
            {
                var a = _free[_free.Count - 1];
                _free.RemoveAt(_free.Count - 1);
                if (a != null) { if (!a.enabled) a.enabled = true; return a; }
                _sourceCount--;
            }
            var go = new GameObject("Voice");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            _sourceCount++;
            return src;
        }

        internal void ReturnSource(AudioSource a)
        {
            if (a == null) { _sourceCount--; return; }
            a.Stop();
            a.clip = null;
            _free.Add(a);
        }

        internal void Configure(AudioSource src, HaloSound s, bool spatial, int priority)
        {
            src.priority = priority;
            src.dopplerLevel = 0f;
            src.bypassReverbZones = !spatial;
            if (!spatial) { src.spatialBlend = 0f; return; }
            src.spatialBlend = 1f;
            src.spread = 0f;
            float min = Mathf.Max(0.05f, s.MinDistance * HaloSpace.Unit), max = Mathf.Max(min + 0.1f, s.MaxDistance * HaloSpace.Unit);
            src.rolloffMode = AudioRolloffMode.Custom;
            src.minDistance = min;
            src.maxDistance = max;
            src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Curve(min / max));
        }

        /// <summary>Distance attenuation over distance / maximum distance: full up to the minimum distance, then 1/d, faded to silence
        /// at the maximum distance (Halo: "not heard beyond"). The exact Halo curve is unknown. // ours</summary>
        AnimationCurve Curve(float ratio)
        {
            int key = Mathf.Clamp(Mathf.RoundToInt(ratio * 200f), 1, 199);
            AnimationCurve c;
            if (_curves.TryGetValue(key, out c)) return c;
            float m = key / 200f;
            var keys = new List<Keyframe> { new Keyframe(0f, 1f), new Keyframe(m, 1f) };
            for (int i = 1; i <= 12; i++)
            {
                float x = m + (1f - m) * i / 12f;
                float u = (x - m) / (1f - m);
                keys.Add(new Keyframe(x, (m / x) * (1f - u * u)));
            }
            c = new AnimationCurve(keys.ToArray());
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            _curves[key] = c;
            return c;
        }

        internal Vector3 ListenerPosition()
        {
            bool usable = _listener != null && _listener.enabled && _listener.gameObject.activeInHierarchy;
            if (!usable && Time.unscaledTime >= _listenerLookAt)
            {
                _listenerLookAt = Time.unscaledTime + 1f;
                int n; _listener = ActiveListener(out n);   // an enabled listener on an active object (the client keeps disabled ones)
                usable = _listener != null;
            }
            if (usable) return _listener.transform.position;
            var head = HaloPlayer.Head();
            if (head != null) return head.transform.position;
            var cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        // ------------------------------------------------------------------ playing

        internal HaloVoice NewVoice(HaloSound s, int group)
        {
            var v = new HaloVoice();
            v.Init(this);
            v.Group = group >= 0 ? group : GroupOf(s);
            v.Priority = PriorityOf(v.Group);
            _voices.Add(v);
            return v;
        }

        static int PriorityOf(int g)
        {
            switch (g)
            {
                case GAnnouncer: case GMusic: case GUi: return 8;
                case GWeapon: return 64;
                case GVehicle: return 80;
                case GAmbient: return 200;
                default: return 128;
            }
        }

        /// <summary>A one-shot (null when skipped: skip fraction, too many voices, not a sound).</summary>
        internal HaloVoice PlayOneShot(HaloTag tag, Vector3 pos, Transform follow, bool spatial, float gain, float scale, float pitch, int group)
        {
            if (tag == null) return null;
            if (tag.ClassName == "lsnd")
            {
                // a looping sound asked for once: play its first track's loop sound once
                var l = HaloLoopingSound.Get(tag);
                if (l == null || l.Tracks.Length == 0) return null;
                tag = l.Tracks[0].Start ?? l.Tracks[0].Loop;
                if (tag == null) return null;
            }
            var b = Bank(tag);
            if (b == null) { _statNoSound++; return null; }
            var s = b.Sound;
            Stat(_statReq, s.Class);
            int tc; _tagReq.TryGetValue(tag, out tc); _tagReq[tag] = tc + 1;
            float skip = s.SkipFraction * Mathf.Lerp(s.ZeroSkip, s.OneSkip, scale);
            if (skip > 0f && Rng.NextDouble() < skip) { Stat(_statSkip, s.Class); return null; }
            if (!MakeRoom(b, s)) { _dropped++; Stat(_statDrop, s.Class); if (_dropped == 1 || _dropped % 2000 == 0) HaloLog.Warn("Audio: " + _dropped + " one-shot(s) dropped (voice budget: " + MaxVoices + " one-shots, per-sound limits)"); return null; }
            _oneShots++;
            if (HaloSound.Unspatialized(s)) spatial = false;
            var v = NewVoice(s, group);
            v.OneShotBank = b; v.Importance = Importance(s.Class); v.Born = Time.unscaledTime;
            follow = SpeakerOf(follow, s);   // dialogue: from the unit's head
            v.Spatial = spatial; v.Position = pos; v.Follow = follow; v.Following = follow != null;
            if (follow != null) v.Position = follow.position;
            v.Gain = gain; v.Scale = scale; v.Pitch = pitch;
            int pr = s.PitchRangeFor(pitch * Mathf.Lerp(s.ZeroPitch, s.OnePitch, scale));
            int perm = b.PickPermutation(pr, Rng);
            v.Enqueue(b, pr, perm);
            if (b.IsLong(pr, perm)) v.WaitLimit = 0f;   // streamed: plays when its first piece is decoded
            v.Tick(AudioSettings.dspTime, 0f);
            return v;
        }

        internal HaloAudioLoop StartLoop(HaloTag tag, Vector3 pos, Transform at, bool spatial, float gain, int group)
        {
            if (tag == null) return null;
            var lp = new HaloAudioLoop { Sys = this, Tag = tag, At = at, Following = at != null, Position = at != null ? at.position : pos, Spatial = spatial, Gain = gain, Group = group };
            if (tag.ClassName == "lsnd") { lp.L = HaloLoopingSound.Get(tag); if (lp.L == null) return null; }
            else if (tag.ClassName != "snd!") return null;
            // shield / health alarms (first person damage), music: Halo plays these without position
            var probe = lp.L != null ? (lp.L.Tracks.Length > 0 ? (lp.L.Tracks[0].Loop ?? lp.L.Tracks[0].Start) : null) : tag;
            var pb = Bank(probe);
            if (pb != null && HaloSound.Unspatialized(pb.Sound)) lp.Spatial = false;
            if (pb != null) Stat(_statLoop, pb.Sound.Class);
            if (!lp.Start()) return null;
            lp.Handle = new HaloAudio.Handle { Alive = true, Loop = lp };
            _loops.Add(lp);
            return lp;
        }

        internal void CountDropped(HaloSound s) { _dropped++; if (s != null) Stat(_statDrop, s.Class); }
        internal void CountStarted(HaloSound s) { if (s != null) Stat(_statPlay, s.Class); }

        // ------------------------------------------------------------------ announcer

        internal HaloTag[] MpSounds(HaloMap map)
        {
            if (map == null) return new HaloTag[0];
            if (_mpMap != map)
            {
                _mpMap = map;
                try { _mpSounds = HaloMpSounds.Read(map); }
                catch (Exception e) { _mpSounds = new HaloTag[0]; HaloLog.Warn("Audio: reading the multiplayer sounds failed: " + e.Message); }
                int n = 0; foreach (var t in _mpSounds) if (t != null) n++;
                HaloLog.Info("Audio: announcer - " + n + " multiplayer sounds in " + map.Name + "'s globals");
            }
            return _mpSounds ?? new HaloTag[0];
        }

        internal bool Announce(HaloMap map, int index)
        {
            var list = MpSounds(map);
            if (index < 0 || index >= list.Length || list[index] == null) return false;
            var tag = list[index];
            if (!QueueAnnouncer(tag)) PlayOneShot(tag, Vector3.zero, null, false, 1f, 1f, 1f, GAnnouncer);
            return true;
        }

        /// <summary>An announcer voice line (a snd! under sound\dialog\) joins the queue - one at a time, as in Halo; false for
        /// anything else (game-event effects like the countdown timer or the ting play at once).</summary>
        internal bool QueueAnnouncer(HaloTag tag)
        {
            if (tag == null || tag.ClassName != "snd!" || !tag.Name.StartsWith("sound\\dialog\\multiplayer", StringComparison.OrdinalIgnoreCase)) return false;   // (campaign dialogue is timed by its scripts)
            if (_announce.Count >= 4) { _announce.Dequeue(); _announceAt.Dequeue(); }   // ours
            _announce.Enqueue(tag); _announceAt.Enqueue(Time.unscaledTime);
            var b = Bank(tag);
            if (b != null) { var tmp = new List<HaloAudioEntry>(1); b.Sequence(0, 0, tmp); Request(tmp[0]); }
            return true;
        }

        void TickAnnouncer()
        {
            if (_announcer != null && !_announcer.Done) return;
            _announcer = null;
            while (_announce.Count > 0)
            {
                var t = _announce.Peek();
                float at = _announceAt.Peek();
                var b = Bank(t);
                if (b == null || Time.unscaledTime - at > 6f) { _announce.Dequeue(); _announceAt.Dequeue(); continue; }   // stale // ours
                var tmp = new List<HaloAudioEntry>(1); b.Sequence(0, 0, tmp);
                if (!tmp[0].Ready && !tmp[0].Failed) { Request(tmp[0]); return; }   // wait for its decode
                _announce.Dequeue(); _announceAt.Dequeue();
                _announcer = PlayOneShot(t, Vector3.zero, null, false, 1f, 1f, 1f, GAnnouncer);
                if (_announcer != null) return;
            }
        }

        // ------------------------------------------------------------------ music / ui.map

        internal HaloMap UiMap()
        {
            if (_ui != null) return _ui;
            if (!_uiLoading)
            {
                string path = null;
                var g = HaloGame.Current;
                if (g != null && g.Install != null) path = g.Install.UiPath;
                if (path != null && File.Exists(path))
                {
                    _uiLoading = true;
                    try { _jobs.Add(new Job { E = null, SoundsPath = path }); } catch (Exception) { _uiLoading = false; }
                }
            }
            return null;
        }

        internal void Music(HaloTag tag, float fadeIn)
        {
            if (_music != null && !_music.Stopping && _music.Tag != null && tag != null && _music.Tag.Name == tag.Name) return;   // already playing it
            StopMusic(0.5f);
            if (tag == null) return;
            _music = StartLoop(tag, Vector3.zero, null, false, 1f, GMusic);
            if (_music != null) { _music.FadeIn(fadeIn); HaloLog.Info("Audio: music " + tag.Name + (fadeIn > 0f ? " (fade in " + fadeIn.ToString("0.#") + " s)" : "")); }
        }

        internal void StopMusic(float fade)
        {
            _pendingTitle = false;
            if (_music != null) { _music.Stop(fade <= 0f, fade > 0f ? fade : 0f); _music = null; }
        }

        internal bool MusicPlaying => _music != null && !_music.Done;
        internal HaloAudio.Handle MusicHandle => _music != null ? _music.Handle : null;

        float _titleFade;

        internal void TitleMusic(float fadeIn)
        {
            _titleFade = fadeIn;
            var ui = UiMap();
            if (ui == null) { _pendingTitle = true; return; }
            _pendingTitle = false;
            var t = ui.Find("lsnd", "sound\\music\\title1\\title1");
            if (t == null) { HaloLog.Warn("Audio: ui.map has no sound\\music\\title1\\title1.lsnd"); return; }
            Music(t, _titleFade);
        }

        internal HaloTag UiSound(string name)
        {
            var ui = UiMap();
            if (ui == null) return null;
            string full = name.IndexOf('\\') >= 0 ? name : "sound\\sfx\\ui\\" + name;
            return ui.Find("snd!", full);
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            Upload();
            if (_pendingTitle) TitleMusic(_titleFade);
            double dsp = AudioSettings.dspTime;
            float dt = Time.unscaledDeltaTime;
            for (int i = _loops.Count - 1; i >= 0; i--)
            {
                var l = _loops[i];
                l.Tick(dt);
                if (l.Done) { if (l.Handle != null) l.Handle.Alive = false; _loops.RemoveAt(i); }
            }
            int shots = 0;
            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                var v = _voices[i];
                v.Tick(dsp, dt);
                if (v.Done) _voices.RemoveAt(i);
                else if (!v.Looping) shots++;
            }
            _oneShots = shots;
            TickAnnouncer();
            TickAmbience();
            TickClassGains(dt);
            TickDiag();
            try { HaloUnitVoice.Tick(); } catch (Exception) { }
            try { HaloFootsteps.Tick(dt); } catch (Exception e) { if (_stepFails++ < 5) HaloLog.Warn("Audio: footsteps: " + e); }
        }

        // ------------------------------------------------------------------ inspection

        internal string Stats()
        {
            return _clips + " clips (" + (_floats * 4 / (1024 * 1024)) + " MB), " + _decoded + " decoded, " + _failed + " failed, " + _voices.Count + " voices, "
                + _loops.Count + " loops, " + _sourceCount + " sources (" + _free.Count + " free), " + _dropped + " dropped, queue " + _jobs.Count + "+" + _background.Count + "/" + _results.Count
                + ", decode " + _decodeMs.ToString("0") + " ms";
        }

        internal int PendingJobs => _jobs.Count + _background.Count + _results.Count;
        internal int PreloadLeft => _preloadLeft;
        internal List<HaloVoice> Voices => _voices;

        /// <summary>The first clip of a sound (decoded on demand; null until it is there).</summary>
        internal AudioClip FirstClip(HaloTag t, out HaloAudioEntry entry)
        {
            entry = null;
            var b = Bank(t);
            if (b == null) return null;
            var tmp = new List<HaloAudioEntry>(1);
            b.Sequence(0, 0, tmp);
            entry = tmp[0];
            Request(entry);
            return entry.Clip;
        }
    }
}

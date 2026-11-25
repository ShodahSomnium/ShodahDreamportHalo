using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] sound_impulse_* / sound_looping_*: HaloAudio (A). Dialogue and music of the missions are these calls.</summary>
    public static partial class HaloScriptFns
    {
        static readonly Dictionary<HaloTag, HaloAudio.Handle> Loops = new Dictionary<HaloTag, HaloAudio.Handle>();
        static readonly Dictionary<HaloTag, int> TicksOf = new Dictionary<HaloTag, int>();

        static void BindSound(HsVm vm)
        {
            vm.Bind("sound_impulse_start", SoundImpulseStart);
            vm.Bind("sound_impulse_time", SoundImpulseTime);
            vm.Bind("sound_looping_start", SoundLoopingStart);
            vm.Bind("sound_looping_stop", SoundLoopingStop);
            vm.Bind("sound_looping_set_alternate", SoundLoopingSetAlternate);
            vm.Bind("sound_looping_set_scale", SoundLoopingSetScale);
        }

        /// <summary>sound_impulse_start sound object scale: from the object (dialogue on the speaker) or flat (none).</summary>
        static int SoundImpulseStart(HsArgs a)
        {
            var tag = a.Tag(0);
            if (tag == null) return 0;
            int h = a.Obj(1);
            float scale = a.Has(2) ? a.Real(2) : 1f;
            var g = h != -1 && !HaloScriptWorld.IsPlayer(h) ? HaloScriptWorld.Get(h) : null;
            if (g != null) HaloAudio.Play(tag, g.transform, scale);
            else HaloAudio.Play2D(tag, scale);
            HaloNetGame.NoteSound(tag, g, scale);   // N: co-op followers hear it
            if (_vm != null) StartedAt[tag] = _vm.Tick;
            return 0;
        }

        /// <summary>sound_impulse_time: ticks REMAINING of the impulse started last with this sound (Halo: "the time remaining"; a10 waits
        /// for (= 0 (sound_impulse_time ...))): its length from the tag's own sample counts (right before the sound was ever decoded) minus
        /// the ticks since sound_impulse_start; 0 when it never started or was stopped.</summary>
        static int SoundImpulseTime(HsArgs a)
        {
            var tag = a.Tag(0);
            if (tag == null) return 0;
            int started;
            if (!StartedAt.TryGetValue(tag, out started) || _vm == null) return 0;
            int t;
            if (!TicksOf.TryGetValue(tag, out t)) { t = SoundTicks(tag); TicksOf[tag] = t; }
            int since = _vm.Tick - started;
            if (since < 0) return 0;   // started after a checkpoint we reverted to
            int left = t - since;
            return left > 0 ? left : 0;
        }

        static readonly Dictionary<HaloTag, int> StartedAt = new Dictionary<HaloTag, int>();

        public static int SoundTicks(HaloTag tag)
        {
            var s = HaloSound.Get(tag);
            if (s == null || s.PitchRanges == null || s.PitchRanges.Length == 0) return 0;
            var pr = s.PitchRanges[0];
            if (pr.Permutations == null || pr.Permutations.Length == 0) return 0;
            int rate = s.SampleRate > 0 ? s.SampleRate : 22050;
            long samples = s.ChainFrames(0, 0);   // A: exact for ADPCM, Ogg from its 16-bit buffer size
            if (samples <= 0 && s.LongestPermutationMs > 0) return (int)(s.LongestPermutationMs * 30L / 1000L);
            return (int)(samples * 30L / rate);
        }

        static int SoundLoopingStart(HsArgs a)
        {
            var tag = a.Tag(0);
            if (tag == null) return 0;
            HaloAudio.Handle old;
            if (Loops.TryGetValue(tag, out old)) { HaloAudio.StopNow(old, 0f); Loops.Remove(tag); }
            int h = a.Obj(1);
            float scale = a.Has(2) ? a.Real(2) : 1f;
            var g = h != -1 && !HaloScriptWorld.IsPlayer(h) ? HaloScriptWorld.Get(h) : null;
            var hd = g != null ? HaloAudio.Loop(tag, g.transform, 1f) : HaloAudio.Loop2D(tag, 1f);
            if (hd != null) { HaloAudio.SetScale(hd, scale); Loops[tag] = hd; }
            HaloNetGame.NoteLoopStart(tag, g, scale);   // N: co-op followers (and late joiners) play it
            return 0;
        }

        static int SoundLoopingStop(HsArgs a)
        {
            var tag = a.Tag(0);
            HaloAudio.Handle h;
            if (tag != null && Loops.TryGetValue(tag, out h)) { HaloAudio.Stop(h); Loops.Remove(tag); }
            HaloNetGame.NoteLoopStop(tag);   // N
            return 0;
        }

        static int SoundLoopingSetAlternate(HsArgs a)
        {
            var tag = a.Tag(0);
            HaloAudio.Handle h;
            if (tag != null && Loops.TryGetValue(tag, out h)) HaloAudio.SetAlternate(h, a.Bool(1));
            HaloNetGame.NoteLoopAlternate(tag, a.Bool(1));   // N
            return 0;
        }

        static int SoundLoopingSetScale(HsArgs a)
        {
            var tag = a.Tag(0);
            HaloAudio.Handle h;
            if (tag != null && Loops.TryGetValue(tag, out h)) HaloAudio.SetScale(h, a.Real(1));
            HaloNetGame.NoteLoopScale(tag, a.Real(1));   // N
            return 0;
        }

        static void StopLoops()
        {
            foreach (var kv in Loops) HaloAudio.StopNow(kv.Value, 0.5f);
            Loops.Clear();
            TicksOf.Clear();
            StartedAt.Clear();
        }
    }
}

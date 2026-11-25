using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Audio module] Plays the game's own sounds: snd! / lsnd tags of the current map (or ui.map), samples decoded at run time from
    /// the player's MAPS\sounds.map (Xbox ADPCM, Ogg Vorbis). Positional with the tag's distances (0 = Halo's sound class defaults),
    /// random permutation (never the same twice in a row), random pitch / gain, skip fraction, scale modifiers, pitch ranges.
    /// Everything is safe to call any time from the main thread (never throws; a sound not decoded yet plays as soon as it is,
    /// within half a second, or is dropped). See DevTools/agents/A/NOTES.md for the API overview.
    /// </summary>
    public static class HaloAudio
    {
        /// <summary>A playing loop. <see cref="Alive"/> turns false when it has stopped.</summary>
        public sealed class Handle
        {
            public bool Alive;
            internal HaloAudioLoop Loop;
        }

        /// <summary>The master volume (setting "volume"; set by the settings, may be set directly).</summary>
        public static float MasterVolume = 1f;

        // ------------------------------------------------------------------ one-shots (the stub's calls)

        /// <summary>A one-shot 3D sound at a point (a random permutation, the tag's distances / pitch / gain).</summary>
        public static void Play(HaloTag sound, Vector3 position, float gain = 1f) { PlayAt(sound, position, gain, 1f, 1f); }
        /// <summary>A one-shot that follows a transform (gun sounds in the hand).</summary>
        public static void Play(HaloTag sound, Transform follow, float gain = 1f) { PlayOn(sound, follow, gain, 1f, 1f); }
        /// <summary>A 2D (head-relative) one-shot: UI, own weapon, HUD alerts.</summary>
        public static void Play2D(HaloTag sound, float gain = 1f) { PlayFlat(sound, gain, 1f, 1f); }

        // ------------------------------------------------------------------ one-shots with Halo's scale and pitch

        /// <summary>A one-shot at a point with Halo's <paramref name="scale"/> (0..1: the sound's zero / one gain, pitch and skip
        /// modifiers - impacts by strength) and <paramref name="pitch"/> (1 = natural; picks the pitch range).</summary>
        public static void PlayAt(HaloTag sound, Vector3 position, float gain, float scale, float pitch)
        {
            if (sound == null) return;
            try { HaloAudioSystem.Ensure().PlayOneShot(sound, position, null, true, gain, scale, pitch, -1); }
            catch (Exception e) { Fail("Play " + sound, e); }
        }

        /// <summary>As <see cref="PlayAt"/>, following a transform while it plays.</summary>
        public static void PlayOn(HaloTag sound, Transform follow, float gain, float scale, float pitch)
        {
            if (sound == null) return;
            try { HaloAudioSystem.Ensure().PlayOneShot(sound, follow != null ? follow.position : Vector3.zero, follow, follow != null, gain, scale, pitch, -1); }
            catch (Exception e) { Fail("Play " + sound, e); }
        }

        /// <summary>As <see cref="PlayAt"/>, not positional.</summary>
        public static void PlayFlat(HaloTag sound, float gain, float scale, float pitch)
        {
            if (sound == null) return;
            try
            {
                var sys = HaloAudioSystem.Ensure();
                if (sys.QueueAnnouncer(sound)) return;   // multiplayer announcer lines (sound\dialog\multiplayer*) queue one after another
                sys.PlayOneShot(sound, Vector3.zero, null, false, gain, scale, pitch, -1);
            }
            catch (Exception e) { Fail("Play2D " + sound, e); }
        }

        // ------------------------------------------------------------------ loops

        /// <summary>A looping sound (lsnd or snd!) attached to a transform until stopped. Null when it cannot play (no tag).</summary>
        public static Handle Loop(HaloTag sound, Transform at, float gain = 1f)
        {
            if (sound == null) return null;
            try { var l = HaloAudioSystem.Ensure().StartLoop(sound, at != null ? at.position : Vector3.zero, at, at != null, gain, -1); return l != null ? l.Handle : null; }
            catch (Exception e) { Fail("Loop " + sound, e); return null; }
        }

        /// <summary>A looping sound at a fixed point (sound scenery).</summary>
        public static Handle LoopAt(HaloTag sound, Vector3 position, float gain = 1f)
        {
            if (sound == null) return null;
            try { var l = HaloAudioSystem.Ensure().StartLoop(sound, position, null, true, gain, -1); return l != null ? l.Handle : null; }
            catch (Exception e) { Fail("LoopAt " + sound, e); return null; }
        }

        /// <summary>A looping sound without position (the BSP's background sound, shield alarms of the own player).</summary>
        public static Handle Loop2D(HaloTag sound, float gain = 1f)
        {
            if (sound == null) return null;
            try { var l = HaloAudioSystem.Ensure().StartLoop(sound, Vector3.zero, null, false, gain, -1); return l != null ? l.Handle : null; }
            catch (Exception e) { Fail("Loop2D " + sound, e); return null; }
        }

        /// <summary>Stops a loop the Halo way: end sounds play, tracks flagged "fade out at stop" fade out.</summary>
        public static void Stop(Handle h)
        {
            if (h == null || h.Loop == null) return;
            try { h.Loop.Stop(false, -1f); } catch (Exception e) { Fail("Stop", e); }
        }

        /// <summary>Stops a loop now (no end sound) or over <paramref name="fadeSeconds"/>.</summary>
        public static void StopNow(Handle h, float fadeSeconds = 0f)
        {
            if (h == null || h.Loop == null) return;
            try { h.Loop.Stop(true, Mathf.Max(0f, fadeSeconds)); } catch (Exception e) { Fail("StopNow", e); }
        }

        /// <summary>Halo pitch of a loop (1 = natural). Multi-range sounds (engines) pick the pitch range whose bend bounds hold it;
        /// the playback rate is pitch / the range's natural pitch. Changes are limited by the sound's "maximum bend per second".</summary>
        public static void SetPitch(Handle h, float pitch) { if (h != null && h.Loop != null) h.Loop.Pitch = Mathf.Max(0.01f, pitch); }
        /// <summary>Gain of a loop (multiplies the tag's).</summary>
        public static void SetGain(Handle h, float gain) { if (h != null && h.Loop != null) h.Loop.Gain = Mathf.Max(0f, gain); }
        /// <summary>Halo scale of a loop, 0..1 (e.g. an engine's power / speed): lerps the sounds' zero..one gain and pitch modifiers
        /// (warthog engine: pitch 1 at 0, x2 at 1) and the detail sound period. Loops start at 1.</summary>
        public static void SetScale(Handle h, float scale) { if (h != null && h.Loop != null) h.Loop.Scale = Mathf.Clamp01(scale); }
        /// <summary>Gain and Halo pitch of a loop at once (Vehicles asked for it). Engines: prefer SetScale(speed 0..1) - the tags
        /// turn the scale into Halo's own pitch (warthog: x1..x2) and gain.</summary>
        public static void SetLoop(Handle h, float gain, float pitch) { SetGain(h, gain); SetPitch(h, pitch); }
        /// <summary>The lsnd's alternate loop / end on or off (switches at the next permutation).</summary>
        public static void SetAlternate(Handle h, bool on) { if (h != null && h.Loop != null) h.Loop.Alternate = on; }
        /// <summary>Moves a loop that is not attached to a transform.</summary>
        public static void SetPosition(Handle h, Vector3 position) { if (h != null && h.Loop != null && !h.Loop.Following) h.Loop.Position = position; }

        // ------------------------------------------------------------------ announcer, music, menu sounds

        /// <summary>The multiplayer announcer / game event sound by Halo's name (globals multiplayer information sounds, enum
        /// MultiplayerInformationSound): "slayer", "double kill", "triple kill", "killtacular", "killing spree", "running riot",
        /// "red team score", "blue team has the flag", "game over", "play ball", "countdown timer", "ting"... (spaces / case /
        /// underscores ignored; <see cref="HaloMpSounds.Names"/> lists all 44). Voice lines queue one after another; false = unknown.</summary>
        public static bool Announce(string key)
        {
            int i = HaloMpSounds.Index(key);
            if (i < 0) { HaloLog.Warn("Audio: no announcer sound '" + key + "'"); return false; }
            return Announce(i);
        }

        /// <summary>The announcer sound by its enum index.</summary>
        public static bool Announce(int index)
        {
            try
            {
                var map = HaloSession.Map;
                if (map == null) return false;
                return HaloAudioSystem.Ensure().Announce(map, index);
            }
            catch (Exception e) { Fail("Announce " + index, e); return false; }
        }

        /// <summary>The announcer sound tag of the current map (null when there is none).</summary>
        public static HaloTag AnnouncerSound(string key)
        {
            int i = HaloMpSounds.Index(key);
            var map = HaloSession.Map;
            if (i < 0 || map == null) return null;
            var list = HaloAudioSystem.Ensure().MpSounds(map);
            return i < list.Length ? list[i] : null;
        }

        /// <summary>Plays music (an lsnd - start, then random loop permutations - or a snd!), not positional, replacing the
        /// music playing. Long sounds stream piece by piece.</summary>
        public static void Music(HaloTag sound) { Music(sound, 0f); }

        /// <summary>As <see cref="Music(HaloTag)"/>, fading in over <paramref name="fadeInSeconds"/>. The same tag again keeps playing.</summary>
        public static void Music(HaloTag sound, float fadeInSeconds)
        {
            try { HaloAudioSystem.Ensure().Music(sound, fadeInSeconds); } catch (Exception e) { Fail("Music", e); }
        }

        /// <summary>Halo's title music from ui.map (sound\music\title1\title1.lsnd: "in", then the three title loops), for the ring
        /// hub. Starts as soon as ui.map is read (a few ms after the first call).</summary>
        public static void TitleMusic(float fadeInSeconds = 0f)
        {
            try { HaloAudioSystem.Ensure().TitleMusic(fadeInSeconds); } catch (Exception e) { Fail("TitleMusic", e); }
        }

        public static void StopMusic(float fadeSeconds = 1f)
        {
            try { if (HaloAudioSystem.Instance != null) HaloAudioSystem.Instance.StopMusic(fadeSeconds); } catch (Exception e) { Fail("StopMusic", e); }
        }

        public static bool MusicPlaying => HaloAudioSystem.Instance != null && HaloAudioSystem.Instance.MusicPlaying;

        /// <summary>One line about the music (its track, entry / piece, source state). Lab.</summary>
        public static string MusicInfo => HaloAudioSystem.Instance != null ? Describe(HaloAudioSystem.Instance.MusicHandle) : "no music";

        /// <summary>A menu sound of ui.map, 2D: "cursor", "forward", "back", "flag_failure" (or a full tag path).</summary>
        public static void Ui(string name)
        {
            try
            {
                var sys = HaloAudioSystem.Ensure();
                var t = sys.UiSound(name);
                if (t != null) sys.PlayOneShot(t, Vector3.zero, null, false, 1f, 1f, 1f, HaloAudioSystem.GUi);
            }
            catch (Exception e) { Fail("Ui " + name, e); }
        }

        /// <summary>ui.map's sound tag by name (null until ui.map is read - call once early, e.g. at boot).</summary>
        public static HaloTag UiSound(string name)
        {
            try { return HaloAudioSystem.Ensure().UiSound(name); } catch (Exception) { return null; }
        }

        // ------------------------------------------------------------------ loading / settings / inspection

        /// <summary>Decodes the sounds a map uses ahead (worker) so first plays do not stall: every snd! except dialog and music,
        /// plus the announcer lines. Clips are released when the match unloads.</summary>
        public static void Preload(HaloMap map)
        {
            try { HaloAudioSystem.Ensure().Preload(map); } catch (Exception e) { Fail("Preload", e); }
        }

        /// <summary>Volume of a group: "weapon", "vehicle", "ambient", "announcer", "music", "ui", "effect" (setting "audio.&lt;group&gt;").</summary>
        public static void SetGroupVolume(string group, float volume) { HaloSettings.SetNum("audio." + group, Mathf.Clamp(volume, 0f, 2f)); }
        public static float GroupVolume(string group) => HaloSettings.Num("audio." + group, 1f);

        /// <summary>Seconds of a sound's first permutation (0 until decoded; asks for the decode).</summary>
        public static float Length(HaloTag sound)
        {
            try { HaloAudioEntry e; var c = HaloAudioSystem.Ensure().FirstClip(sound, out e); return c != null ? c.length : 0f; }
            catch (Exception) { return 0f; }
        }

        // ------------------------------------------------------------------ campaign scripts

        /// <summary>sound_class_set_gain: the gain (0..1) of every sound of a Halo sound class ("device_machinery", "ambient_nature",
        /// "music", "unit_dialog", "scripted_dialog_other" ... - SoundClass names, spaces or underscores) ramps there over
        /// <paramref name="seconds"/> (the script's ticks / 30). Reset when the map unloads. False = unknown class name.</summary>
        public static bool SetClassGain(string haloSoundClass, float gain, float seconds)
        {
            try
            {
                bool ok = HaloAudioSystem.Ensure().SetClassGain(haloSoundClass, gain, seconds);
                if (!ok && _classWarned++ < 5) HaloLog.Warn("Audio: no sound class '" + haloSoundClass + "'");
                return ok;
            }
            catch (Exception e) { Fail("SetClassGain", e); return false; }
        }
        static int _classWarned;

        /// <summary>sound_impulse_stop: stops every voice playing this sound now (a dialogue line cut by the script). Returns how many.</summary>
        public static int StopSound(HaloTag sound)
        {
            try { return HaloAudioSystem.Instance != null ? HaloAudioSystem.Instance.StopSound(sound) : 0; }
            catch (Exception e) { Fail("StopSound", e); return 0; }
        }

        /// <summary>True while a voice plays (or waits to play) this sound.</summary>
        public static bool IsPlaying(HaloTag sound)
        {
            var sys = HaloAudioSystem.Instance;
            return sys != null && sys.IsPlaying(sound);
        }

        /// <summary>Starts the map's ambience (BSP background sound by the listener's cluster + sound scenery loops). Done by itself on
        /// HaloSession.WorldBuilt; stopped on Unloading. Call only for a world built outside the session (lab).</summary>
        public static void StartAmbience(HaloMap map, HaloScenario scenario, Transform root)
        {
            try { HaloAudioSystem.Ensure().StartAmbience(map, scenario, root); } catch (Exception e) { Fail("StartAmbience", e); }
        }

        public static void StopAmbience()
        {
            try { if (HaloAudioSystem.Instance != null) HaloAudioSystem.Instance.StopAmbience(); } catch (Exception e) { Fail("StopAmbience", e); }
        }

        public static string AmbienceInfo => HaloAudioSystem.Instance != null ? HaloAudioSystem.Instance.AmbienceInfo : "audio not started";

        /// <summary>Frees a map's clips and stops its sounds (done by itself on HaloSession.Unloading).</summary>
        public static void Release(HaloMap map)
        {
            try { if (HaloAudioSystem.Instance != null && map != null) HaloAudioSystem.Instance.Forget(map); } catch (Exception e) { Fail("Release", e); }
        }

        /// <summary>As <see cref="Announce(string)"/> for a given map (lab, or before the session holds the map).</summary>
        public static bool AnnounceOn(HaloMap map, string key)
        {
            int i = HaloMpSounds.Index(key);
            if (i < 0 || map == null) return false;
            try { return HaloAudioSystem.Ensure().Announce(map, i); } catch (Exception e) { Fail("Announce " + key, e); return false; }
        }

        /// <summary>The clip of a sound's first permutation chain (asks for the decode; null until it is there). Lab / inspection.</summary>
        public static AudioClip ClipOf(HaloTag sound)
        {
            try { HaloAudioEntry e; return HaloAudioSystem.Ensure().FirstClip(sound, out e); } catch (Exception) { return null; }
        }

        /// <summary>Decodes or preloads still running.</summary>
        public static bool Busy => HaloAudioSystem.Instance != null && (HaloAudioSystem.Instance.PendingJobs > 0 || HaloAudioSystem.Instance.PreloadLeft > 0);

        /// <summary>Voices playing or waiting.</summary>
        public static int VoiceCount => HaloAudioSystem.Instance != null ? HaloAudioSystem.Instance.Voices.Count : 0;

        /// <summary>One line about a loop: per track the playing entry, its pitch range, the source's pitch / volume / position. Lab.</summary>
        public static string Describe(Handle h)
        {
            if (h == null || h.Loop == null) return "no loop";
            var l = h.Loop;
            var sb = new System.Text.StringBuilder();
            sb.Append(l.Tag != null ? l.Tag.Name : "?").Append(h.Alive ? " alive" : " stopped").Append(" scale ").Append(l.Scale.ToString("0.##")).Append(" pitch ").Append(l.Pitch.ToString("0.##"));
            for (int i = 0; i < l.TrackCount; i++)
            {
                var v = l.TrackVoice(i);
                if (v == null) { sb.Append(" | track ").Append(i).Append(": -"); continue; }
                var e = v.Current; var s = v.ActiveSource;
                sb.Append(" | track ").Append(i).Append(": ");
                if (e == null || s == null) { sb.Append(v.Done ? "done" : "waiting"); continue; }
                sb.Append(e.Name).Append(" range '").Append(e.Sound.PitchRanges[e.Pr].Name).Append("' src pitch ").Append(s.pitch.ToString("0.###"))
                  .Append(" vol ").Append(s.volume.ToString("0.###")).Append(s.isPlaying ? " playing @" : " idle @").Append(s.timeSamples).Append(" joins ").Append(v.Joins).Append(" gap frames ").Append(v.GapFrames);
            }
            return sb.ToString();
        }

        /// <summary>The "Audio: diag" line now (listener, voices playing / audible / muted, sources, gains).</summary>
        public static string Diag() => HaloAudioSystem.Instance != null ? HaloAudioSystem.Instance.Diag() : "audio not started";

        /// <summary>Per sound class: tags in the map, one-shots asked / started / skipped / dropped, loops; classes never asked for.</summary>
        public static string Coverage() => HaloAudioSystem.Instance != null ? HaloAudioSystem.Instance.Coverage() : "audio not started";

        /// <summary>One line of counters (clips, MB, voices, sources...).</summary>
        public static string Stats() => HaloAudioSystem.Instance != null ? HaloAudioSystem.Instance.Stats() : "audio not started";

        static int _fails;
        static void Fail(string what, Exception e)
        {
            if (_fails++ < 20) HaloLog.Error("Audio: " + what + " failed: " + e);
        }
    }
}

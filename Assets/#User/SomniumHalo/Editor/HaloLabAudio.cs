using System.Collections;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Audio lab (you cannot listen in batch mode: everything is measured). "audio": opens a map (-halomap, default bloodgulch),
    /// preloads its sounds, measures clips (length, peak, rms from AudioClip.GetData), plays one-shots, an engine loop across its
    /// scale (pitch ranges), the announcer, the title music from ui.map, then releases the map.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabAudio
    {
        static HaloLabAudio() { HaloLabRunner.Register("audio", r => Run(r)); HaloLabRunner.Register("audio-campaign", r => Campaign(r)); HaloLabRunner.Register("audio-mission", r => Mission(r)); }

        static void Measure(AudioClip c, out float peak, out float rms)
        {
            peak = 0f; rms = 0f;
            if (c == null) return;
            var d = new float[c.samples * c.channels];
            c.GetData(d, 0);
            double sum = 0;
            for (int i = 0; i < d.Length; i++) { float v = Mathf.Abs(d[i]); if (v > peak) peak = v; sum += d[i] * (double)d[i]; }
            rms = d.Length > 0 ? (float)System.Math.Sqrt(sum / d.Length) : 0f;
        }

        static IEnumerator Run(HaloLabRunner r)
        {
            if (Object.FindFirstObjectByType<AudioListener>() == null) r.Cam.gameObject.AddComponent<AudioListener>();
            string name = HaloLabBoot.Arg("-halomap", "bloodgulch");
            var entry = r.Game.Install.Map(name);
            if (!r.Check(entry != null, "map " + name + " in the install")) yield break;
            var map = new HaloMap(entry.File);
            HaloLabRunner.Log("map " + map.Name + " " + map.Tags.Length + " tags; output " + AudioSettings.outputSampleRate + " Hz, dsp " + AudioSettings.dspTime.ToString("0.00"));

            float t0 = Time.realtimeSinceStartup;
            HaloAudio.Preload(map);
            yield return r.Frames(2);
            yield return r.Wait(() => !HaloAudio.Busy, 120f, "the preload");
            HaloLabRunner.Log("preload " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s: " + HaloAudio.Stats());

            string[] names =
            {
                "sound\\sfx\\weapons\\pistol\\fire", "sound\\sfx\\weapons\\assault rifle\\fire", "sound\\sfx\\weapons\\frag grenade\\expl",
                "sound\\sfx\\weapons\\rocket launcher\\fire", "sound\\sfx\\vehicles\\warthog_7_engine", "sound\\sfx\\impulse\\footsteps\\cyborg\\dirt\\walk",
                "sound\\dialog\\multiplayer1\\double_kill", "sound\\sfx\\ambience\\multiplayer\\countryday2", "sound\\sfx\\ui\\shield_charge",
            };
            int good = 0;
            foreach (var n in names)
            {
                var t = map.Find("snd!", n);
                if (t == null) { HaloLabRunner.Log("  (no " + n + ")"); continue; }
                var c = HaloAudio.ClipOf(t);
                float w = Time.realtimeSinceStartup;
                while (c == null && Time.realtimeSinceStartup - w < 5f) { yield return null; c = HaloAudio.ClipOf(t); }
                float peak, rms; Measure(c, out peak, out rms);
                bool ok = c != null && c.length > 0.05f && c.length < 60f && peak > 0.01f && peak <= 1.0001f && rms > 0.0005f;
                if (ok) good++;
                r.Check(ok, n + ": " + (c != null ? c.length.ToString("0.000") + " s " + c.channels + " ch " + c.frequency + " Hz peak " + peak.ToString("0.000") + " rms " + rms.ToString("0.0000") : "no clip"));
            }

            // one-shots: do voices start and run?
            var head = new GameObject("lab head").transform;
            head.position = r.Cam.transform.position;
            var fire = map.Find("snd!", "sound\\sfx\\weapons\\assault rifle\\fire");
            int before = HaloAudio.VoiceCount;
            for (int i = 0; i < 5; i++) { HaloAudio.Play(fire, head.position + Vector3.forward * (2f + i * 10f)); yield return r.Frames(4); }
            HaloLabRunner.Log("one-shots: voices " + before + " -> " + HaloAudio.VoiceCount + "; " + HaloAudio.Stats());
            yield return new WaitForSecondsRealtime(1.5f);
            HaloLabRunner.Log("after 1.5 s: voices " + HaloAudio.VoiceCount + " (one-shots of ~0.3 s should be done)");

            // engine loop: scale 0 -> 1 changes the pitch range
            var engine = map.Find("lsnd", "sound\\sfx\\vehicles\\warthog_7_engine");
            var car = new GameObject("lab warthog").transform;
            car.position = head.position + Vector3.forward * 5f;
            var h = HaloAudio.Loop(engine, car);
            r.Check(h != null, "warthog engine loop handle");
            HaloAudio.SetScale(h, 0f);
            yield return new WaitForSecondsRealtime(2.5f);
            HaloLabRunner.Log("engine scale 0: " + HaloAudio.Describe(h));
            HaloAudio.SetScale(h, 0.5f);
            yield return new WaitForSecondsRealtime(1.5f);
            HaloLabRunner.Log("engine scale 0.5: " + HaloAudio.Describe(h));
            HaloAudio.SetScale(h, 1f);
            yield return new WaitForSecondsRealtime(1.5f);
            HaloLabRunner.Log("engine scale 1: " + HaloAudio.Describe(h));
            HaloAudio.Stop(h);
            yield return new WaitForSecondsRealtime(2.5f);
            r.Check(!h.Alive, "engine loop stopped (" + HaloAudio.Describe(h) + ")");

            // announcer
            r.Check(HaloAudio.AnnounceOn(map, "double kill"), "announce double kill");
            r.Check(HaloAudio.AnnounceOn(map, "Killing_Spree"), "announce killing spree (queued)");
            yield return new WaitForSecondsRealtime(0.5f);
            HaloLabRunner.Log("announcer: " + HaloAudio.Stats());

            // title music from ui.map
            HaloAudio.TitleMusic();
            float m0 = Time.realtimeSinceStartup;
            yield return r.Wait(() => HaloAudio.MusicPlaying, 20f, "the title music");
            yield return new WaitForSecondsRealtime(3f);
            HaloLabRunner.Log("music after " + (Time.realtimeSinceStartup - m0).ToString("0.0") + " s: " + HaloAudio.MusicInfo + "; " + HaloAudio.Stats());
            yield return new WaitForSecondsRealtime(11f);
            string mi = HaloAudio.MusicInfo;
            HaloLabRunner.Log("music after " + (Time.realtimeSinceStartup - m0).ToString("0.0") + " s: " + mi + "; " + HaloAudio.Stats());
            r.Check(mi.Contains(" gap frames 0") && !mi.Contains(" joins 0 "), "music pieces stream gaplessly (joins > 0, no gap frames)");
            var cursor = HaloAudio.UiSound("cursor");
            r.Check(cursor != null, "ui.map cursor sound");
            HaloAudio.Ui("cursor");
            HaloAudio.StopMusic(1f);
            yield return new WaitForSecondsRealtime(1.5f);
            r.Check(!HaloAudio.MusicPlaying, "music stopped");

            // ambience: BSP background sound + sound scenery loops
            HaloAudio.StartAmbience(map, new HaloScenario(map), null);
            yield return new WaitForSecondsRealtime(2f);
            HaloLabRunner.Log("ambience: " + HaloAudio.AmbienceInfo + "; " + HaloAudio.Stats());
            r.Check(HaloAudio.AmbienceInfo.IndexOf("no background", System.StringComparison.Ordinal) < 0, "background sound loop running");
            HaloAudio.StopAmbience();

            HaloAudio.Release(map);
            yield return r.Frames(2);
            HaloLabRunner.Log("released: " + HaloAudio.Stats());
            r.Check(good >= 7, "clips measured sane: " + good + "/" + names.Length);
        }

        static string Src(AudioSource s, Vector3 ear)
        {
            if (s == null) return "no source";
            return s.clip != null ? s.clip.name : "no clip";
        }

        /// <summary>"audio-mission" (-halomission a30): the real campaign flow; the diag line, then one-shots near the player (3D and 2D)
        /// measured: source state, volume, distance to the active listener.</summary>
        static IEnumerator Mission(HaloLabRunner r)
        {
            string mission = HaloLabBoot.Arg("-halomission", "a30");
            HaloCampaign.StartMission(mission, HaloCampaign.Difficulty.Normal);
            yield return r.Wait(() => HaloSession.Playing && HaloCampaign.Active, 900f, mission + " to start");
            if (!r.Check(HaloSession.Playing, mission + " playing")) yield break;
            yield return Secs(6f);
            HaloLabRunner.Log("diag: " + HaloAudio.Diag());
            var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            foreach (var l in listeners) HaloLabRunner.Log("listener " + l.name + " enabled " + l.enabled + " active " + l.gameObject.activeInHierarchy + " at " + l.transform.position.ToString("F1"));
            Vector3 feet; HaloPlayer.TryFeet(out feet);
            var head = HaloPlayer.Head();
            HaloLabRunner.Log("player feet " + feet.ToString("F1") + ", head " + (head != null ? head.name + " " + head.transform.position.ToString("F1") : "none"));
            HaloTag fire = null, dlg = null;
            foreach (var t in HaloSession.Map.OfClass("snd!"))
            {
                if (fire == null && t.Name.EndsWith("assault rifle\\fire")) fire = t;
                if (dlg == null && t.Name.StartsWith("sound\\dialog\\")) dlg = t;
            }
            Vector3 ear = head != null ? head.transform.position : feet + Vector3.up * 1.6f;
            HaloAudio.Play(fire, ear + Vector3.forward * 2f);
            HaloAudio.Play2D(dlg);
            yield return Secs(0.2f);
            HaloLabRunner.Log("after a 3D rifle shot 2 m away and a 2D line: " + HaloAudio.Diag());
            foreach (var go in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (!go.isPlaying) continue;
                if (go.clip == null || (go.clip.name.IndexOf("fire", System.StringComparison.Ordinal) < 0 && go.clip.name.IndexOf("dialog", System.StringComparison.Ordinal) < 0)) continue;
                HaloLabRunner.Log("  source " + go.name + " clip " + go.clip.name + " vol " + go.volume.ToString("0.###") + " mute " + go.mute + " blend " + go.spatialBlend
                    + " min/max " + go.minDistance.ToString("0.#") + "/" + go.maxDistance.ToString("0.#") + " dist " + (go.transform.position - ear).magnitude.ToString("0.0")
                    + " prio " + go.priority + " active " + go.gameObject.activeInHierarchy + " parent " + (go.transform.parent != null ? go.transform.parent.name : "-")
                    + " mixer " + (go.outputAudioMixerGroup != null ? go.outputAudioMixerGroup.name : "none") + " time " + go.timeSamples);
            }
            int all = 0, playing = 0;
            foreach (var s in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) { all++; if (s.isPlaying) playing++; }
            HaloLabRunner.Log("scene AudioSources: " + all + ", playing " + playing);
            HaloCampaign.LeaveMission();
            yield return Secs(2f);
        }

        static IEnumerator Secs(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) yield return null; }

        /// <summary>"audio-campaign": a10's music (levels\a10\music\a10_05: in, loops, alternate loops, end on the beat), a dialogue line
        /// during the preload (the priority queue), positional dialogue, sound_impulse_stop, sound_class_set_gain.</summary>
        static IEnumerator Campaign(HaloLabRunner r)
        {
            if (Object.FindFirstObjectByType<AudioListener>() == null) r.Cam.gameObject.AddComponent<AudioListener>();
            var entry = r.Game.Install.Map("a10");
            if (!r.Check(entry != null, "a10 in the install")) yield break;
            var map = new HaloMap(entry.File);
            HaloAudio.StopMusic(0f);

            // a dialogue line asked for while 1000+ preloads are queued: it must not wait behind them
            HaloAudio.Preload(map);
            var line = map.Find("snd!", "sound\\dialog\\a10\\a10_440_cryoassist");
            if (line == null) foreach (var t in map.OfClass("snd!")) if (t.Name.StartsWith("sound\\dialog\\a10\\")) { line = t; break; }
            r.Check(line != null, "a10 dialogue line " + (line != null ? line.Name : "-"));
            float t0 = Time.realtimeSinceStartup;
            HaloAudio.Play2D(line);
            yield return r.Wait(() => HaloAudio.VoiceCount > 0 && HaloAudio.IsPlaying(line), 5f, "the dialogue line");
            float waited = Time.realtimeSinceStartup - t0;
            yield return r.Frames(3);
            r.Check(waited < 0.5f, "2D dialogue started after " + (waited * 1000f).ToString("0") + " ms during the preload (" + HaloAudio.Stats() + ")");
            r.Check(HaloAudio.StopSound(line) > 0, "sound_impulse_stop stops it");
            yield return Secs(0.3f);
            r.Check(!HaloAudio.IsPlaying(line), "line stopped");
            // positional on a speaker
            var speaker = new GameObject("lab speaker").transform;
            speaker.position = r.Cam.transform.position + Vector3.forward * 4f;
            HaloAudio.Play(line, speaker);
            yield return Secs(0.5f);
            r.Check(HaloAudio.IsPlaying(line), "positional dialogue on a speaker plays");
            HaloAudio.StopSound(line);

            // music a10_05: start (rhythm1 in), loops, alternate, stop -> end piece
            var music = map.Find("lsnd", "levels\\a10\\music\\a10_05");
            if (!r.Check(music != null, "levels\\a10\\music\\a10_05.lsnd")) yield break;
            var h = HaloAudio.Loop2D(music);
            r.Check(h != null, "music loop started");
            yield return r.Wait(() => HaloAudio.Describe(h).Contains("playing"), 10f, "the music to play");
            HaloLabRunner.Log("music: " + HaloAudio.Describe(h));
            yield return Secs(6f);
            HaloLabRunner.Log("music +6 s: " + HaloAudio.Describe(h));
            HaloAudio.SetAlternate(h, true);
            string d = "";
            float a0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - a0 < 20f) { d = HaloAudio.Describe(h); if (d.Contains("alt_loops")) break; yield return null; }
            r.Check(d.Contains("alt_loops"), "alternate loop after " + (Time.realtimeSinceStartup - a0).ToString("0.0") + " s: " + d);
            // sound_class_set_gain "music" 0 over 1 s, then back
            HaloAudio.SetClassGain("music", 0f, 1f);
            yield return Secs(1.3f);
            string q = HaloAudio.Describe(h);
            r.Check(q.Contains(" vol 0 ") || q.Contains(" vol 0.0"), "music class gain 0: " + q);
            HaloAudio.SetClassGain("music", 1f, 0f);
            r.Check(!HaloAudio.SetClassGain("no_such_class", 1f, 0f), "unknown class refused");
            HaloAudio.Stop(h);
            float s0 = Time.realtimeSinceStartup;
            bool sawEnd = false;
            while (h.Alive && Time.realtimeSinceStartup - s0 < 40f) { d = HaloAudio.Describe(h); if (d.Contains("alt_out") || d.Contains("\\out__")) sawEnd = true; yield return null; }
            r.Check(sawEnd && !h.Alive, "stop: the playing permutation ended, then the end piece (alt_out), then done in " + (Time.realtimeSinceStartup - s0).ToString("0.0") + " s");
            HaloLabRunner.Log("after: " + HaloAudio.Stats());
            HaloAudio.Release(map);
        }
    }
}

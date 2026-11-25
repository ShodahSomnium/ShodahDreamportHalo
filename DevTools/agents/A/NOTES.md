# A notes (Audio)

## Status
- Data/Audio (Unity-free, harness-verified): HaloSound (snd! parse + class default distances), HaloLoopingSound (lsnd), HaloMpSounds
  (globals multiplayer sounds = announcer, 44 names), HaloAdpcm (Xbox/IMA ADPCM, PCM16), HaloOgg (Ogg container -> vendored NVorbis in
  Data/Audio/Vorbis, MIT, nested class `NVorbis`), HaloPcm (decode one piece / a whole permutation chain, levels, WAV).
- Game/Audio: HaloAudio (public API), HaloAudioSystem (worker decode, clip cache, pool, announcer, music, ui.map), HaloAudioVoice
  (gapless 2-source sequencing on the DSP clock, loop permutations, pitch-range crossfade), HaloAudioLoop (lsnd tracks/details),
  HaloAudioBank (clips per tag). Editor/HaloLabAudio.cs scenario `audio`.

## Verified (harness DevTools/agents/A/harness, `dotnet run -c Release --no-build -- <cmd>`)
- `scan`: all 30 maps, every permutation is external in sounds.map; offset/size/name ("<tag>__<pr>__<perm>") match 100%.
  Formats in the PC files: Xbox ADPCM (fmt 1) and Ogg Vorbis (fmt 3) only. 22050/44100 Hz, mono/stereo.
- `decode <map>`: EVERY sound of the 19 MP maps + ui.map decodes, 0 failures, 0 silent, 0 NaN (~950 pieces / ~900 s per map, 1.5 s).
  Ogg: decoded length == the tag's 16-bit buffer size for 60/60 (after Ogg granule end-trimming). ADPCM: 65 samples per 36-byte block
  (header sample + 64 codes); `cont` shows the next block's header one typical step past our last sample (as an encoder writes it).
- Title music IS in the PC files: ui.map `sound\music\title1\title1.lsnd` (start `in` 33 s, loop `loops` = 3 perms 108/61/30 s,
  Ogg 44.1k stereo). Menu sounds: ui.map `sound\sfx\ui\cursor|forward|back|flag_failure`.

## API (HaloAudio, main thread, never throws)
- One-shots: `Play(tag, pos, gain)`, `Play(tag, transform, gain)` (follows), `Play2D(tag, gain)`; with Halo's scale/pitch:
  `PlayAt(tag, pos, gain, scale, pitch)`, `PlayOn(tag, tr, gain, scale, pitch)`, `PlayFlat(...)`. A tag of ANY map works (ui.map too).
  Not decoded yet -> plays when ready (<0.5 s) or dropped. Announcer lines (snd! under sound\dialog\) given to Play2D queue one at a time.
- Loops: `Loop(tag lsnd|snd!, transform, gain)`, `LoopAt(tag, pos)`, `Loop2D(tag)` -> Handle (`Alive`). `Stop(h)` (end sounds, fade-out
  flags), `StopNow(h, fade)`. Control: `SetScale(h, 0..1)` (Halo scale -> the tags' zero/one gain + pitch modifiers: warthog engine pitch
  x1..x2 and its pitch ranges idle/low/med/hi; USE THIS for engines with speed), `SetPitch(h, p)` (extra multiplier, 1 = natural),
  `SetGain(h, g)`, `SetLoop(h, gain, pitch)`, `SetAlternate(h, on)`, `SetPosition(h, pos)`.
- Announcer: `Announce("double kill")` / `Announce(index)` (HaloMpSounds.Names: "slayer", "killing spree", "red team score", "game over",
  "countdown timer", "ting"...; current HaloSession.Map), `AnnounceOn(map, key)`, `AnnouncerSound(key)` -> tag.
- Music: `Music(tag, fadeInSeconds)`, `TitleMusic(fadeIn)` (ui.map title1), `StopMusic(fadeOut)`, `MusicPlaying`. Long sounds stream
  piece by piece (decoded just ahead, freed after playing).
- Menu: `Ui("cursor"|"forward"|"back"|"flag_failure")`, `UiSound(name)` -> ui.map tag (null until ui.map is read; first call starts it).
- `Preload(map)` (Match calls it at map open: weapons (not impacts), footsteps, vehicles, first person damage, game events, ambience,
  devices + announcer lines; 491 s / 52 MB PCM on Blood Gulch; the rest decodes on first use), `Release(map)` (automatic on
  HaloSession.Unloading). Volumes: `MasterVolume` (setting "volume"), `SetGroupVolume("weapon|vehicle|ambient|announcer|music|ui|effect")`.
- Ambience (automatic on HaloSession.WorldBuilt, stopped on Unloading): the BSP background sound of the listener's cluster
  (HaloWorld.ClusterAt, polled 2x/s; most MP maps have one, Danger Canyon / Death Island / Gephyrophobia / Icefields switch) as a 2D loop
  + every sound scenery's lsnd attachment looping at its place (Blood Gulch teleporters, Timberland streams). W: do not play these.
  `StartAmbience(map, scenario, root)` / `StopAmbience()` / `AmbienceInfo` for labs.
- Inspection: `Stats()`, `ClipOf(tag)`, `Length(tag)`, `Describe(handle)`, `Busy`, `VoiceCount`.

## Behaviour from the tags
- Distances 0 in the tag (most weapons/UI/dialog) -> Halo's sound class defaults (weapon fire 4..70 wu, detonation 8..120, ...).
- Random permutation, never the last one twice; random pitch bounds; random gain modifier = gain random in [modifier, 1] (ASSUMPTION,
  the field's exact meaning is undocumented); skip fraction x zero/one skip modifiers; permutation gain; unspatialized classes
  (game event, music, first person damage, forced-unspatialized dialog) play 2D.
- Distance curve: full to min distance, then 1/d, faded to 0 at max distance (ours - Halo's exact curve unknown). No doppler.

## Verified in the lab (`bash DevTools/lab.sh audio`, passed 2026-10-19; log copy DevTools/agents/A/lab_audio.txt)
- Preload Blood Gulch: 457 clips, 491 s, 52 MB PCM, 0 failed, 0.7 s wall. 9/9 clips measured from AudioClip.GetData (lengths,
  peak, rms sane: pistol fire 1.36 s, AR fire 0.71 s, frag 1.42 s, double kill 1.33 s stereo, countryday2 6.19 s 44.1k stereo...).
- Sources really play in batch mode (isPlaying, timeSamples advance). 5 one-shots -> 5 voices, all finished 1.5 s later.
- Warthog engine lsnd: scale 0 -> ranges porsche_idle / idle (rate 0.909), scale 1 -> porsche_hi / high; Stop -> handle dead.
- Title music (ui.map) streamed piece by piece: 4 joins in 14 s, 0 gap frames. Announcer queue, ui cursor, ambience
  (bloodgultch background + 2 teleporter loops), Release(map) frees 457 clips.

## Open / not verified
- Not listened to (cannot): the distance curve, random gain interpretation and loudness balance want a client ear test.
- Cone angles (all MP sounds are omni), sound environments (senv reverb) and promotion sounds are not used.

## Footsteps (Game/Audio/HaloFootsteps.cs, lab `steps`, passed on bloodgulch + chillout 2026-10-19)
- Table from the files: globals MP unit characters\cyborg_mp\cyborg_mp.bipd -> footsteps globals\cyborg.foot (6 kinds: 0 walk, 1 run,
  2 sliding (water fx only), 3 shuffle, 4 jump, 5 land x 32 materials, 84 sounds). Material under the feet: ray down (GroundMask) +
  HaloWorld.SurfaceMaterial (unknown -> last known, else stone).
- Run table: dirt=dirt, sand=gravel, stone=grtcement, snow=snow, wood=wood, metal hollow/thin=grating, metal thick=cleancement,
  glass/jackal shield/ice=marble, water=water (+ effects\footsteps water fx), leaves=grass (sound\sfx\impulse\footsteps\cyborg\<x>\run|walk|jump).
  Land plays <x>\jump, jump plays <x>\jump_up (genericjump on character materials).
- Local: avatar root (or the head when the root does not move), on foot, alive, in a match; gain x0.6 (HaloFootsteps.OwnGain, ours).
  Remote: HaloNetGame.Players in the match, alive, not seated, within 36 m; positional at their drawn feet.
- Stride: Halo's (stand rifle move-front 4.57 m / 20 frames / 2 feet = 2.29 m at 6.86 m/s; crouch 1.37 m) blended to 0.75 m at 1.5 m/s.
  Run sound above 3.2 m/s (walk below 2.6). Landing after a fall >= 0.12 m, gain 0.6..1.4 by height.
- Lab: Blood Gulch floors stone/sand/metal thick walked -> grtcement / gravel / cleancement run + walk, landing, remote walker 5 steps at
  6.5 m/s (stride 2.18 m); Chill Out metal thick -> cleancement. Log: `Audio: step own|remote <kind> on <material> -> <sound>` (once per combo).

## Campaign (phase 2; lab `audio-campaign` passed on a10)
- `SetClassGain(cls, gain, seconds)` (sound_class_set_gain, SoundClass names), `StopSound(tag)` (sound_impulse_stop), `IsPlaying(tag)`.
- Dialogue on a unit speaks from its "head" marker; campaign Play2D lines play at once (only multiplayer announcer lines queue).
- Two decode queues: on-demand first, preload behind (a10 preload 841 clips / 83 MB; a line asked for during it started in 3 ms).
- Music lsnd: start piece, random loop permutations picked 3 s ahead (decoded before due), set_alternate switches at the next
  permutation, sound_looping_stop lets the playing permutation finish then plays end / alternate end gaplessly (a10_05: in -> loops ->
  alt_loops -> alt_out, done 12.5 s after stop); "not a loop" lsnds play once; class gain "music" ramps verified.
- Ambience reloads the background palette of the BSP on HaloCampaign.BspSwitched.
- `HaloFootsteps.Step(feet, kind, bipedOrFootTag, gain)` for campaign bipeds (their own foot tags).

## Bug fix (iteration 5 client: "no sfx in campaign, only music")
- Cause: the one-shot cap (MaxVoices 72) counted ALL voices; a30 runs 124 sound-scenery loops (+ background, music) = 148 voices, so every
  one-shot (weapons, footsteps, dialogue, devices) was dropped before it got a source. Loops (music, ambience) still played.
- Fix: only one-shots count against the cap (`HaloVoice.Looping` for loop tracks); a dropped one-shot now logs a warning.
  Lab `audio-mission -halomission a30`: before 0 one-shot voices created; after: rifle shot 2 m away playing (vol 0.83, 3D) and a 2D line.
- Listener: ListenerPosition now takes an enabled AudioListener on an active object (re-checked), else HaloPlayer.Head().
- Client diagnostic 20 s after every Playing phase: `Audio: diag - listener ... N voices: P playing (2D/3D), A audible, beyond max
  distance, muted, inactive, waiting; one-shots x/72, dropped; sources free/total; master, groups; class gains below 1; real voices`.

## All-SFX audit (iteration 7; lab `audio-sfx` a30, logs lab_sfx_before.log / lab_sfx_after.log)
- Coverage line in Player.log (`Audio: diag ... | class tags:asked/started/skipped/dropped+loops ...; never asked: ...; most asked: ...`,
  20 s after Playing, every 10 min, at unload).
- Root cause of lost SFX in fights: particle collision sounds (metal_chips 9440 asks / 45 s) filled the 72 one-shots -> weapon fire
  91 dropped, footsteps 15, dialogue. HaloAudioBudget: per-tag copy limit (impacts 2, effects 4, weapons 6, dialogue 2) and priority
  stealing (impacts < effects < weapons < dialogue). After: weapon_fire 419/419, projectile_impact 371/371, footsteps 139/139,
  unit_dialog 40/40 started; only the metal_chips flood is dropped.
- HaloUnitVoice: pain (minor/major/shield), death (quiet/violent/instant/falling), damaged-friend-player, the player's own pain/death
  (sound\dialog\chief\chief) - lab: 14 pain, 4 death, 3 own.
- HaloImpacts.Play(objTag, Impact(8) | TireSlip(9), point, material, strength): object material effects (asked of G / C / V).
- AI footsteps from the unit's own foot tag + AI landing sound (one-liners in HaloUnit.Move.cs).
- Still never asked on a30: weapon_ready / empty / overheat / charge / idle (AI), object_impacts, vehicle_collision, device_door
  (requests to C, G, V, K in REQUESTS.md).

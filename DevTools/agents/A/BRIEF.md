# A - Audio

Goal: every sound of Halo multiplayer from the player's own files, positional, at the game's levels: weapons (fire, reload, empty,
pickup, ready, melee), impacts per material, grenades, explosions, vehicles (engine loops with pitch by speed, horn, impacts, tires),
footsteps, shields (recharge, depleted, low), UI sounds (cursor/forward/back in ui.map), the multiplayer announcer ("Slayer",
"Double Kill", "Killing Spree", "Red team scores", "Flag taken"... from the globals multiplayer information sounds + the multiplayer
sound list), ambience (sound scenery / BSP background sound palette / looping sounds lsnd), and the menu music for the ring hub (Halo's
title music if it is in the files: search sounds.map / ui.map for music lsnd/snd!; if the PC files do not have it, say so).

Build (Data/Audio, Game/Audio):
1. sound tag (snd!, struct Sound in layouts.txt): pitch ranges, permutations (with `next permutation index` chains = one sound split in
   pieces), format per permutation (0 = 16-bit PCM, 1 = Xbox ADPCM, 2 = IMA ADPCM, 3 = Ogg Vorbis - check the enum), channel count,
   sample rate (22050/44100), gain, distances (min/max in world units -> metres), random pitch/gain, skip fraction. Samples live in
   MAPS\sounds.map (retail PC: the permutation's samples data ref is external, its offset absolute in sounds.map - VERIFY; the resource
   map's entries are named "<tag path>__<pitch range>__<permutation>").
2. Decoders (Unity-free, Data/Audio): Xbox ADPCM (36-byte blocks per channel, 64 samples), IMA ADPCM, PCM, Ogg Vorbis (port the NVorbis
   copy from the ETS Dreamport `...SomniumEuroTruck/Ets/Vorbis` - it is MIT licensed, keep its licence header). Validate in the harness:
   decode every sound of Blood Gulch, count failures, write a few WAVs and check durations/levels are sane.
3. Game/Audio: implement the `HaloAudio` stub (Play at point / following / 2D, Loop handle with pitch & gain control for engines -
   add members as needed, e.g. `SetPitch(handle)`), AudioSource pooling, decode on a worker + AudioClip.Create/SetData on the main
   thread (OnAudioFilterRead is NOT usable in worlds - ETS finding), cache clips per map, release on unload. Sound class volumes
   (weapon / vehicle / ambient / announcer / UI), `HaloAudio.MasterVolume` from HaloSettings "volume".
4. Looping sounds (lsnd): tracks (start/loop/end), detail sounds; sound scenery (World places the objects and calls Loop).
5. Announcer API for the Match/HUD modules: `HaloAudio.Announce(string key)` mapping Halo's announcer events to the globals' sounds.
6. Music API for Front: `HaloAudio.Music(HaloTag lsnd or snd!)` + `StopMusic(fadeSeconds)`.
Lab: `Editor/HaloLabAudio.cs` - play a set of sounds and log clip lengths / peak levels (you cannot listen; measure).

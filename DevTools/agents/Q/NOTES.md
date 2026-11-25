# Q notes (Cinematics: cutscene camera, letterbox, fades, titles, recorded animations, VR cinema screen)

## Files
- Data/Cinematic (Unity-free; harness DevTools/agents/Q/harness, own copy compiling only Data/Map, Bitmaps, Models, Anims, Hud, Bsp, Cinematic):
  - `HaloRecording.cs` - `HaloRecording` (one scenario recorded animation, parsed event stream), `HaloRecordingPlayback` (per-tick cursor:
    Control, Remaining = recording_time, Step), `HaloRecordedControl` (unit control: animation state, aiming speed, flags, weapon, throttle,
    facing / aiming / looking yaw-pitch in pi/1000 units, flag constants).
  - `HaloCutsceneData.cs` - camera points (+1264), flags (+1252), titles (+1276, text from the scenario's "ingame help text" +1412),
    recordings (+876); name / index lookups.
  - `HaloRecordingSpeed.cs` - a biped's move speed at throttle 1 = root motion of "<seat> <class> move-front" (alert seat with force-alert).
- Game/Cinematic: `HaloCinematic.cs` (API, fades, titles, flags, skip, clock), `.Camera.cs` (camera_set / relative / first person / dead /
  camera_time), `.Recordings.cs` (recording_play / kill / time, `IHaloRecordedControl`, kinematic fallback, `HaloRecordedPuppet`),
  `.Effects.cs` (cinematic_screen_effect_*), `HaloCinemaView.cs` (the camera + RT, monitor overlay, VR screen + dim box, VR head layer).
- Editor/HaloLabCinematic.cs - labs `cinematic` (a10 bridge driven directly) and `cinematic-script` (K's StartMission + S's scripts).

## Recorded animation stream (reverse-engineered; ALL 349 recordings of the 10 maps parse, end tick = length - 1 in every one)
Header: u8 animation state, u8 aiming speed, u16 control flags, i16 weapon index, (ucv >= 1: i8 grenade, i8 ?), 2 floats throttle,
3 x vec3 facing / aiming / looking (52 B); then ucv 0: 3 (yaw, pitch) i16 pairs; ucv 3: 4 B + 2 B + pairs (70 B); ucv 4: 4 B + 4 B + pairs (72 B).
Angles: pi/1000 rad (header pairs = the header vectors; e.g. johnson_walk_1 facing (-0.998,-0.032,0) = yaw -990). Recordings are ABSOLUTE
world directions: every start yaw equals its cutscene flag's facing (x10_walk_1_base 0 / 0, walk_2 90 / 500, walk_3 89 / 495, panic_4 -180 / -1000...).
Events: byte = kind (bits 2-7) | time code (0 same tick, 1 +1, 2 +u8, 3 +u16):
0x04 end; 0x08 animation state u8 (keyes_1 / old ucv0 streams set 3 at tick 1); 0x10 control flags u16 (Halo unit_control bits: 1 crouch,
2 jump, 0x40 action, 0x80 melee, 0x100 look-dont-turn, 0x200 force alert, 0x400 reload, 0x800 primary trigger, 0x2000 grenade);
0x14 weapon index i16; 0x18 throttle 2 floats (forward, left; the unit's facing frame); 0x38 turn (i8 yaw, i8 pitch); 0x58 turn (i16, i16).
Turns move facing + aiming + looking together; yaw stays in -1000..1000 in every stream (wraps), pitch in -500..500.
No position data: the unit's own movement produces the path.

Validation (harness `qpath`, dead-reckoning on a10 BSP 1's collision BSP, pictures DevTools/agents/Q/pics/):
- keyes_walk_1 from flag keyes_deck (the script's teleport) ends at (-0.02..0.11, 2.63..2.80) wu = 0.14-0.25 wu from flag keyes_peer, where
  the script teleports Keyes next, facing 110 deg vs the flag's 104 deg (captain "stand/alert unarmed move-front", 1.3-1.4 m/s).
- crew x10_crew_walk_1 / 3 / 4 (flags 0x200 force alert -> "alert" seat; marine.antr unarmed class -> "alert pistol move-front" 0.0225 wu/tick)
  walk along the bridge walkways and turn at the aisles (path_crew1_alert.png, path_crew4_alert.png); with the "stand" pace (0.075) they walk
  out of the bridge window - so the alert seat matters.

## Titles (all 44 of the 10 maps have text), e.g. a10 "Reveille" / "Total Carnage" / "AI Constructs and Cyborgs First!", b30 "The Silent
Cartographer" / "It's Quiet..." / "Shafted"; bounds 425..446 of 480 = inside the bottom letterbox bar, right-justified; 30 / 120 / 30 ticks.
Subtitles: none in Halo's data (no dialogue text in any ustr / hmt).

## Presentation
- Cutscene camera renders a 4:3 RT (Halo's frame; camera point FOV = horizontal at 4:3) only while camera_control is on; culling = the head
  camera's mask minus UI layer 5 (our VR canvases live on 5). HaloWorldDriver.View / HaloFx.ViewCamera point at it meanwhile (fog / particles).
- Layout on Halo's 640 x 480: letterbox bars 60 px each, title in large_ui font (text colour + 1 px shadow), screen-effect wash, fade.
- VR: screen 3.6 m wide 3.2 m ahead at head height and yaw when the camera is taken, riding with HaloPlayer.Root (teleports / lifts), re-centred
  slowly after 1.5 s looking >70 deg away; 6-face dim box (alpha 0.85, setting cinematic.dim) around the head; ZTest Always, sorting 31000+.
  Fades / titles while the camera is not taken: head-locked layer 0.6 m ahead (70 deg wide), fade covers everything. No letterbox bars then.
- Monitor: ScreenSpaceOverlay canvas (sorting 100): picture fitted 4:3 on black, bars full width (1/8 height), title, fade full screen.
  The client camera is never touched (nothing to restore).
- Camera moves: cubic Hermite in progress (smoothstep from rest; a move given mid-move starts at the camera's current speed) - ours.
- Skip: a fresh press (after the skip point opened) of Space / Enter / E / VR A, X, trigger -> HaloCampaign.Revert() (Halo's mechanism).

## Status
- Harness: all 10 maps parse (349 recordings, 962 camera points, 1025 flags, 44 titles); bridge paths validated (above).
- Compiles (runtime, server-like, editor tools); server API check clean.
- Lab `cinematic` (a10 bridge driven directly, PASSED): camera_time 200 -> 99 at half -> 0 at keyes_x10_deck_1b (0.00 m); recording_time 350;
  Keyes (C's unit through IHaloRecordedControl) ends 0.75 m from keyes_peer; crew 1 / 4 end within 0.05 m of the harness dead-reckoning
  ((5.29,1.77) vs (5.32,1.73); (-1.10,7.05) vs (-1.14,7.09)). Pictures lab/cinematic: cine_*.png (cutscene camera), vr_screen / vr_side
  (screen 4:3 with bars + "Total Carnage" in the bottom bar, bridge dimmed around), mon_overlay (4:3 pillarboxed, bars, title).
- Lab `cinematic-script` (K's StartMission a10 + S's scripts, PASSED): x10 runs by itself - skip point, BSP 0 -> 7 (space), autumn_glory
  camera chain with the Pillar of Autumn / Halo, fighterbombers flown by recordings (kinematic), BSP 7 -> 1, Keyes on the bridge, crew
  walking, display shots (lab/cinematic-script/script_00..19). The held player stays at the a10 start on the cinema floor (before it:
  fell out of the world in BSP 7 -> death -> revert = x10 skipped by itself).
- Player hold: HaloCinematic.Player.cs (DeferPlayerTeleport, cinema floor, last teleport after the cutscene behind a fade) - the defer
  path waits for S's hook (REQUESTS); not exercised in a lab yet.
- W: a10's space sky fog (density 1, 0-0 m) blacks the world beyond 1 m - labs work around it (HaloMaterials.SetFog off), reported.

## Open
- Halo's exact camera interpolation curve, camera_set_dead path, convolution blur / desaturation (shown as a wash) / video effect: ours or missing.
- Vehicles under recordings: kinematic fallback until V implements IHaloRecordedControl.
- Requests out: S (bindings), K (Tick + skip revert), G / V (input hold), H (HUD hide), I (leave recorded units alone).

# S - Script (HaloScript VM + engine function bindings) - notes

## Files
- `Data/Script/HsDefs.cs` - GENERATED (harness `hsdefs <riat/definition.json> <out>`): RIAT's function names / return types / parameter
  types by gbx-retail index, and the 490 engine globals with their types (RIAT has no PC indices for globals).
- `Data/Script/HsData.cs` - reader (Unity-free): node table, string data, scripts, globals, the scenario blocks constants index (object
  names, trigger volumes + their boxes, cutscene flags + positions/facing, camera points, titles, recordings, device groups, command lists,
  starting profiles, conversations, encounters / squads / platoons), `CallName`, `AiName`.
- `Data/Script/HsDecompiler.cs` - node table -> HSC text (cond rebuilt from the compiler's if-chains), checks every constant against the
  string the compiler kept.
- `Data/Script/HsVm.cs` - the VM (Unity-free): threads with explicit evaluation stacks, special forms, conversions, object lists, natives
  by name, checkpoint snapshots. `Data/Script/HsCore.cs` - world-less natives + no-ops by design. `Data/Script/HsHudText.cs` - the
  scenario's hud message texts (+1428) and ingame help (+1412).
- `Game/Script/HaloScript.cs` (Start / Stop / Tick / Running + Vm, Data, Trace, CoverageLine, CallsLine, ThreadsLine, checkpoint part
  "script"), `HaloScriptWorld.cs` (object handles <-> GameObjects, flags, volumes), `HaloScriptFns*.cs` (bindings: Game, Objects, Sound,
  Hud, Cinematic, Ai, Units, Characters, Devices, Player (rumble), Input (player_action_test_*)).
- `Editor/HaloLabScript.cs` - lab `script` (`lab.sh script -halomission a10 -haloseconds 45`).
- Harness: `DevTools/harness/ProbeS.cs` (shared harness: `hsc / hsrun / hsuses / hshmt / hsraw / hsdefs / hsfnidx / hstypes / hsglobs /
  hsgvals`); my private copy `DevTools/agents/S/harness` compiles only Data/Map + Data/Script (other agents' Data cannot break it).
- Outputs: `hsc/<map>.hsc` (decompiled scripts of all 10 maps, for AI / Mission / Cinematics), `uses.txt` (function uses per map),
  `coverage.md` (function -> status, uses per map; `coverage.sh` regenerates it).

## Data layout (verified on all 10 maps)
- Script syntax data (+1140): 56-byte data-array header ("script node", max 19001, element size 20, 'd@t@'), then 20-byte nodes:
  salt +0 (0 = free slot), index union +2, value type +4, flags +6 (1 primitive, 2 script call, 4 global, 8 garbage collectable, 16 local),
  next node id +8 (salt<<16 | index, -1 none), string offset +12, data +16.
- Function call node: data = id of its first child = the function-name node (type "function name", string = the function's name),
  whose `next` is the first argument. Script call: flag 2, index union = script index.
- Constants: the index union holds the constant's OWN type, the node type the type it was converted to (unit_name "chief" as unit:
  type 38 index 44). Data: bool byte, short / long / real, string = string offset, object names / trigger volumes / flags / cameras / titles /
  recordings / device groups / command lists / profiles / conversations / hud messages = block index (short), tag types = tag id,
  ai = encounter index | 0x80000000 + squad<<16 (| 0x40000000 + platoon<<16), enums (team, difficulty) = short.
- Globals: data = scenario global index; engine globals = 0x8000 | the engine's own index, named by the string.
- **The function index union is NOT the same table on every map**: a10 compiled cinematic_skip_start_internal as 302, other maps
  296 / 298 (RIAT gbx-retail: 298); 161 of 322 distinct (index, name) pairs disagree with RIAT and maps disagree with each other
  (harness `hsfnidx`). So the VM binds every call node by the map's own name string (looked up in HsDefs for the return type).
- Script (+1180, 92 B): name, type +32 (startup / dormant / continuous / static / stub), return type +34, root node id +36, parameters +80.
  Global (+1192, 92 B): name, type +32, initialiser node id +40.

## Verified
- Decompile proof (harness `hsc`): all 10 maps, 0 bad nodes, 0 constant/string mismatches. Nodes visited: a10 11791, a30 7900, a50 9815,
  b30 2991, b40 6704, c10 6019, c20 5485, c40 5141, d20 4873, d40 8741. Output reads like Bungie's HSC (mission_a10, x10 intro with
  camera_set / sound_impulse_start / recording_play, cond blocks, cinematic_skip_start ...). 1938 scripts, 723 globals in total.
- Globals initialise right (`hsgvals`: (* 30.0 300.0) -> long 9000, effect tags, reals).
- VM world-less (`hsrun <map> 120 q`): every map runs 3600 ticks without a bad node / runaway, 3-8 us a tick (a10: 37 threads started).
- a10 trace (`hsrun a10 60`): the startup / continuous scripts start, mission_a10 runs cinematic_skip_start -> x10 intro (camera_set,
  recordings, dialogue sleeps), wakes x10_crew_salute_*.
- Checkpoint + skip (`hsrun a10 40 v snap`): game_save_totally_unsafe captures mid-call, a revert with game_reverted true makes
  mission_a10 jump the x10 cutscene and go on (wake x10_post, tutorial_setup, mission_bsp, music_a10, linkage) - Halo's own skip logic.
- Hud texts (`hshmt a10`): "Use [Action] to exit the cryo-tube", "» Find Captain Keyes on the Bridge" ... (b30 too).

## Coverage (291 functions used by the 10 maps): see coverage.md
- VM special forms 21, bound 255, no-op by design 15, unimplemented 0 (291 of 291 functions the 10 maps use).
- Per map (implemented incl. VM + no-op / used): a10 189/195, a30 146/148, a50 159/162, b30 122/122, b40 144/145, c10 137/140,
  c20 113/115, c40 125/126, d20 136/140, d40 154/160.
- "bound" means S calls the owner's API; the owner's body may itself still be partial (I's ai_go_to_vehicle etc. log "AI: unimplemented").

## Design notes
- Object handles: -1 none; 0..N-1 scenario object name index (live object through HaloObjects.Find(name)); 0x20000 player 0 (local player
  root); 0x10000+k run-time objects (AI units from ai_actors...). Object lists: a 1024-slot ring pool in the VM (Halo garbage-collects them).
- Threads: startup and continuous from the first tick, dormant asleep until wake; continuous restart the tick after they end; sleep -1 =
  until woken; other negative sleeps = next tick; sleep_until period default 30, optional timeout (ticks); wake runs the script this
  tick when it comes later in the order, else next tick. Ops budget 20000 steps per thread per tick (then paused to the next tick, logged).
- Checkpoint (K's HaloCheckpoints part "script"): globals + engine globals + every thread's state / wake tick / frames / values; a capture
  inside a native (game_save_totally_unsafe) stores the calling thread as having finished that call.
- VR comfort: player_effect_* shake -> controller rumble only; object_teleport / object_set_facing / volume_teleport_players_not_inside on the
  player = HaloPlayer.Teleport (a teleport, the player's own view); objects_attach / unit_suspended on the player and player_camera_control
  are ignored (logged once).
- Per-tick allocation: none in the VM steady state (stacks / lists pooled); natives bound once (static methods / non-capturing lambdas);
  first use of a string constant caches it. ai_actors allocates in HaloAi.Units (I's API returns a new list).

## Open
- 0 unimplemented after round 3 (C HaloUnit Invulnerable / DropsItems / SetEmotion / vitality / SetPermutation / FlashlightOn / WeaponReadied / TotalGrenades, G flashlight + night vision, W breakable surfaces, I attack inhibit).
- Co-op: `players` = the local player only; Net hosts scripts on the instance master later.
- HUD help text goes to HaloEvents.Message (6 s) until H has a help panel.

## Lab (DevTools/lab.sh script -halomission <m> -haloseconds 60; logs kept as lab_a10.log / lab_a30.log)
- a10 (passed): K's StartMission -> HaloScript.Start; ticks at 29.8 / s (1793 ticks), 19 threads ran, mission_a10 started, the x10 intro
  plays (switch_bsp 7, object_teleport, camera_set x15, recording_play x15, custom_animation, vehicle_load_magic); picture
  lab/script/a10-script-1.png = Threshold and the Pillar of Autumn (autumn_glory). 0 bad nodes / throws. 24 functions unimplemented on a10.
- a30 (passed): 29.5 ticks / s, the insertion cutscene ends ~tick 875, setup_dead / mission_lz / mission_first / mission_cave /
  objectives_a30 / music_a30 start, objective "» Evade the Covenant patrols"; picture a30-script-2.png = inside the crashed lifeboat.
  12 functions unimplemented on a30 (nav points, player_enable_input, game_speed, damage_object, show_hud...).
- Q's player hold: Place() (object_teleport / object_set_facing on the player) and volume_teleport_players_not_inside call
  `HaloCinematic.DeferPlayerTeleport(feet, yaw, facingOnly)` first; seating the player in a vehicle is skipped while HaloCinematic.HoldsPlayer.
  camera_set uses the camera point index; cinematic_abort bound. G's player_enable_input / players_unzoom_all / damage_object / damage_new /
  player_add_equipment (scenario starting profile +840) bound.
- Lab a10 300 s with Q's hold (lab_a10c.log, passed): the x10 intro runs from tick 0 to 7339 (~245 s), script teleports of the player are
  deferred ("Cinematic: the player is held where they stand ..."), the player moved 0.00 m during it; after it mission_a10's
  object_set_facing (player0) placed the player itself (Q dropped the deferred space-base teleport) - the player stands on the scenario
  start (-169.0, 0.0, 2.4); objective "» Complete training diagnostic"; 52 threads ran, 30.0 ticks / s, 0 bad nodes.
- Fix: HaloScriptWorld.PlayerCount uses HaloPlayer.TryFeet (the editor has no Bridge avatar: `players` was empty in the lab before,
  so no script ever saw the player there; the client was fine).
- Fix: sound_impulse_time = ticks REMAINING since sound_impulse_start (0 when not started / stopped / after a revert), not the full length:
  a10's tutorial_introduction waits for (= 0 (sound_impulse_time ...)) and hung before.
- Lab a10 330 s (lab_a10e.log, passed, 0 unimplemented, 0 bad nodes): intro ends at tick 6546, player held 0.00 m, then placed on the start;
  unit_enter_vehicle seats the player in the cryotube ('CT-driver'), tutorial_introduction runs to its end (tick 7033), tutorial_looking
  shows help "Look around" (waits for head movement - the lab head does not move). Pictures a10-script-6..9-after-intro: the cryo bay
  (cryotubes, the shield-charge zapper glowing yellow, steam), seen from the player's tube. unit_open of the cryotube: V only logs (no door
  animation yet).
- Co-op host hand-over: HaloScript registers HaloNetHostState part "script" (order 20). Capture = HsSnapshot.ToBytes (header with node /
  script / global counts, tick, globals, engine globals, each thread's state / wake / result / value stack / frames; a10 4194 B). Adopt =
  FromBytes -> fresh VM Start + Restore -> Running (no startup re-run); null -> last checkpoint snapshot, else StartInPlace(HaloCampaign.Ticks).
  HaloScript.Prepare(map, scenario) builds the VM on followers (asked K to call it). Harness `hshandover`: exact on a10 / a30 / b30 / d40.
  Object-list handles in stacks point into the old host's list pool (short-lived; they read as other / empty lists).

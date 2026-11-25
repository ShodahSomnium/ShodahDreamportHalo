# Halo: Combat Evolved Multiplayer Dreamport (Somnium Space)

Halo PC multiplayer inside a Somnium Space world, played as your own Somnium avatar, in VR and on a monitor. The world ships **no game
assets**: every map, model, texture, sound, effect, HUD picture, string and number is read at run time from the player's own Halo PC
install (default `C:\Program Files (x86)\Microsoft Games\Halo`: `MAPS\*.map` cache files version 7, `MAPS\bitmaps.map`, `MAPS\sounds.map`,
`MAPS\ui.map`). Our code carries only tag names, offsets and the numbers the game files do not hold (marked `// ours`).

Access is the same whitelist as the other Dreamports (Shodah, Wildy, Meloz, "ꨄ Rage Quit ꨄ", denevraut; `Core/HaloAccess.cs`): anyone else
starts nothing, sees nothing and hears nothing, and other players are hidden and silent for them. All log lines are tagged `[SomniumHalo]`.

## Iterations

| # | Date | What |
|---|------|------|
| 1 | 2026-10-19 | First build: the ring hub, whitelist, tablet app, lobby, all 19 multiplayer maps, the game's weapons in VR, vehicles, the HUD, effects, sounds, multiplayer |
| 2 | 2026-10-19 | After the first client test: the teleporter hum ("game event" sound class) played unpositioned at full volume everywhere - game-event sounds with their own distances are placed in the world now (teleporter: 0.5-2.5 units); leaving a vehicle killed the player (the seat exit moved the avatar in one frame, read as an 84-299 m/s fall) - no falling damage for 1.5 s after a seat exit / teleport / spawn and a jump filter; reloads: magazine well and pull part measured from each gun's model (AR / sniper operating handle, pistol slide, shotgun pump; the AR's handle was guessed 16 cm off, so it never racked), the pulled part follows the hand, the pistol slide locks back on empty; one-handed guns can be cupped with the free hand; a vibration when a free hand hovers over a holster / grenade that holds something; a large over-the-shoulder back reach; the chest grenades turned pink after a map change (they outlived the match's materials) - rebuilt per match; Halo's own weapon part animations (pistol slide, sniper bolt, shotgun pump, AR handle, plasma vents) on own and other players' guns; bullet visuals: a frozen-sprite mesh bug and own casings piling up (your own casings vanish on landing, resting debris capped at 96); avatars sit in vehicle seats with Halo's own enter / idle / steering / aim / exit animations (Game/Vehicles/HaloSeatPoses.cs) |
| 3 | 2026-10-19 | After the second client test: the Somnium tablet's B / Y buttons are held off while seated and 1 s around getting in / out (B exits); the own head (shrunk by the client for first person) is shown in the VR third-person seat view; the warthog chain gun fires on either trigger in the gunner seat (on the grips or not); the Scorpion's rider seats are only offered with a driver (the tag's flag) and the driver seat is reached from beside the hull - it drives, the turret follows the head, right trigger cannon, left trigger machine gun; Ghost and Banshee guns fire straight forward (steer to aim); reload sounds per hand action (slices of each gun's own first-person reload sound at the moments the animation moves the magazine / slide, Data/Combat/HaloReloadSounds.cs); Halo's footsteps from globals\cyborg.foot per surface (own steps at the feet, remote players positional, landings; Game/Audio/HaloFootsteps.cs); Halo's ladders (collision surfaces flagged climbable, on 13 of the 19 maps) get the Bridge's SomniumClimbableSurface (looked up by name at run time) |

| 4 | 2026-10-19 | THE CAMPAIGN (phase 2, first build): CAMPAIGN page in the hub and the tablet (the 10 missions with the game's own names / pictures / descriptions, the four difficulties with their pictures; START for the instance master, the others pulled in), campaign loading card, the mission's tablet tab (resume / restart from checkpoint / leave). Every campaign map builds with all its BSPs (start BSP first, the rest in the background, switches in a few ms), lens flares, decals, weather, alpha cut-out foliage, breakable glass, space skies. The missions' OWN compiled HaloScript runs (Data/Script: reader + VM, 277 of the 291 functions the 10 maps use bound by name; the x10 intro, objectives, checkpoints). Halo's AI from the encounters (grunts / elites / jackals / hunters / flood / sentinels / marines: perception, bursts by difficulty, grenades, melee, evades, panic, firing positions, navigation from the BSP, allies following, AI in vehicles, conversations). Every campaign biped with its own animation graph (incl. the compressed cinematic animations), damage regions, shields, deaths, dropped weapons. Mission flow: objects by scenario name, devices (doors open for players, lifts carry them, controls by touch / trigger / E), trigger volumes, BSP switch volumes, checkpoints (game_save at a safe moment) and revert on death, game speed, scenery animations, countdown timer. Cutscenes: camera points / moves, letterbox, titles, fades, skip, Halo's recorded animations (reverse-engineered; all 349 parse), shown on a floating cinema screen in VR (the player is held where they stand; the script's last teleport applied after the cutscene) or the monitor overlay. Campaign HUD (objectives, help text, nav points, timer, AI on the motion tracker, hidden in cutscenes), campaign music / dialogue / sound classes, per-biped footsteps, AI hits on the player with Halo's difficulty damage table, campaign player vitality / starting profiles, flashlight (L / free-hand stick click) and sniper night vision. Co-op plumbing: the instance master hosts scripts + AI, followers draw up to 63 AI units from 9 "Halo Net Actor" slots + 2 "Halo Net Mission" blocks |
| 5 | 2026-10-19 | Host hand-over (the user's rule): when the instance master leaves mid-mission or mid-match, the next player becomes master and host and the game keeps going - no reload, teleport or lobby. The host streams its state every 15 s and at checkpoints through 4 new scene blocks "Halo Net Host 0..3" (script VM snapshot, AI actors by unit serial, mission ticks / timer / devices / placements); the new host takes the puppets over as its own units, adopts the parts and takes a "host hand-over" checkpoint; followers prepare the script VM so they can adopt |
| 6 | 2026-10-19 | Campaign sound effects: every one-shot (weapons, footsteps, dialogue, devices) was dropped in the campaign because the 72-voice cap counted the ambient loops too (a30 alone has 124 sound scenery loops) - loops no longer count against it; the listener is the enabled one (or the head camera); a one-time `Audio: diag` line 20 s into each match / mission |
| 7 | 2026-10-19 | Halo-like AI: dropships fly their recordings, hover and open the pod; troops play the seat exit animation, fall and land; AI banshees fly command lists in 3D and make dive attack runs with nose guns and fuel rods (ground clearance, no friendly fire); unreachable goals detour to the nearest reachable spot; actors speak from their dialogue tags; hunters go berserk when their partner dies, grunt kamikazes; budget line explains why actors do not fire |
| 8 | 2026-10-20 | Campaign co-op: AI / recording / script-driven vehicles travel in the actor blocks (~10 Hz pose, rotation, velocity, gunfire replay, pod open / hover), followers draw them on the host clock; actor records carry vehicle + seat + exiting so puppets ride and play the exit clip; enemy voice lines go through the mission event ring (once, not replayed to late joiners); hand-over keeps flying the vehicles; player timeline catches up gradually after a late first sample |
| 9 | 2026-10-20 | All SFX + NPC dialogue: per-tag voice budget with priority stealing (particle spark sounds had starved weapon fire / footsteps / dialogue; sparks now sound on first contact, max 10/s per tag); unit pain / death / "watch your fire" lines and the Chief's own; per-species AI footsteps and landings; AI weapon reload / empty / overheat (AI plasma now overheats) / charge / ready / idle hum, body impacts; dropped weapon and grenade bounces, weapon ready / melee / ammo pickup sounds; vehicle engines follow real speed (AI, recordings, co-op followers), tyre skids, crashes, suspension; device open / close / motor loops, control press feedback, lights follow power. Co-op: script dialogue, conversations, music / ambience loops (late joiners get the running loops), script effects; followers' control presses go to the host; devices above placement 255 now sync |
| 10 | 2026-10-21 | Networking upgraded to Photon Fusion 2.1.3 with the Somnium migration tool V1.0 (unattended, Full) and Community Modules 4.0 (its assembly renamed `SomniumHalo.CommunityModules`, assembly names must be unique on the server); `SceneNetworking.cs` follows the 2.1 API (`OnReliableDataReceived` takes a `ReadOnlySpan<byte>`, default flags without V1, both under `#if FUSION_2_1_OR_NEWER`); the scene re-saved so Fusion re-baked its 69 network objects (flags V2); DevTools build compiles against this project's Fusion 2.1 dlls. Backup of the 2.0 project: `D:DreamportHaloBackup_preFusion21` and `MigrationBackups/`. Worlds on Fusion 2.0 no longer sync with the client: test with two clients and re-upload |
| 11 | 2026-10-21 | Multiplayer: while the master sits in the hub during a running match, the lowest-id player in the match writes the world blocks (parked vehicles, pickup timers, objectives; at most one writer, 0.5 s hand-over) and the master takes the role back on rejoin; a new master in the hub keeps commanding the running game; side scores no longer lose a player's kills across a leave / rejoin. Co-op hosting unchanged (the master ends the mission for everyone when it leaves it) |
| 12 | 2026-10-21 | Whitelist: `Ninquiet` added |
| 13 | 2026-10-21 | Halo folder: the folder set in Settings is checked on SET and the install's own folder saved (quotes, slashes, the MAPS folder, halo.exe, or a folder up to two levels above are accepted); when Halo was not found it loads right away; the reason a folder is refused is shown and logged; the usual install places are searched on C:, D: and E: |
| 14 | 2026-11-04 | Halo-accurate AI round. Floors: swept unit mover (max slope from the biped tag, slides on steep faces, depenetration + last-ground recovery, platform carry, suspension of units in inactive BSPs). Dropships / Pelicans: recorded flight on a tag-driven human-plane model (Crouch/Jump vertical, level hull, slide on contact, not-collideable exits) - every a30 dropship drops inside its script LZ on the floor; vehicle cargo seats; recordings saved in checkpoints and co-op hand-over. Flood: carriers waddle at the target and burst after their death animation (area damage, 3-6 infection forms), infection forms leap / latch / pop (shields fry them), combat forms feign death, Flood melee never hits its own swarm. AI from the actor tags: perception time, surprise, danger triggers, melee charge/leap at contact reach, grenade speed, firing patterns and aim, line of fire, retreat, squads kept on their firing positions, encounter knowledge sharing, automatic migration, hunters survive back melee, jackal shields, active camo; co-op followers see camo / feign / latch / leap / shield states. Known: the a30 and b30 Pelican jeep drops land ~25-32 m from their small drop zones |
| 15 | 2026-11-10 | Plasma: bolts drawn with their light volumes (mgs2 widgets) and lens flares; particle physics stepped per Halo tick (the held-fire flash column is gone), particles at Halo size, effect cone = full angle (sparks stay low), animate-once-per-frame flashes, overcharge flare grows with the charge, vents loop while overheated (player, AI, remote), AI weapon attachments driven by their functions (no permanent glow). Vehicles: seated guns placed in the posed hands for everyone (never from the net record), own passenger view shows only gun + hands, gunner hands on the turret grips with Halo seat overlays, empty vehicles hold still and park |

## What iteration 1 does

* **Ring hub** (Front): the main menu's own Halo ring from `ui.map` at 40x scale (about 2.3 km radius, arcing high overhead) with Threshold,
  Basis and the menu sky, turning slowly; you stand on a platform with the menu in the middle (placed on the floor in front of you, as in
  Black Ops 2). Halo's title music. Menu in Halo's own fonts and pictures: MULTIPLAYER, SETTINGS, CREDITS, "INSTANCE MASTER: name".
* **Lobby**: Slayer / Team Slayer / Capture the Flag / King of the Hill / Oddball / Race (the game's own names and descriptions), the 19 maps
  with their preview pictures, the rules with the game's own option labels; players by team; the instance master presses START GAME
  (5 s countdown), everyone in the instance is pulled in; late joiners join the running match. The loading card shows the map's picture.
* **Tablet**: a "HALO" app on the Somnium tablet's home screen holds the same menu; in a match: SCORES, TEAM, MATCH (leave; the master
  ends the game or starts a new one).
* **Maps** (World): every multiplayer map built from its BSP with the game's lightmaps, shaders (environment detail maps, model
  multipurpose / self-illumination / reflections, transparent stages, glass, water), sky (Threshold, the ring), fog, collision (every
  start location verified on all 19 maps), scenery and light fixtures. Maps build in under a second.
* **Match flow** (Match): Halo spawning (start locations for the game type, away from enemies), respawn after the game's 3 s + the variant's
  time, scoring (suicide and betrayal -1), multi-kill and spree announcer lines, CTF (take / return / capture, flag drops on death), King of
  the Hill (moving hill option), Oddball, Race (laps through the race flags), teleporters, falling out of the map kills, score / time
  limits, results for 12 s, back to the lobby.
* **Weapons in VR** (Combat, the Black Ops 2 way): at most two weapons plus grenades. Long guns (assault rifle, sniper, shotgun, rocket
  launcher, flamethrower, fuel rod) on the back over the shoulder, small guns (pistol, plasma pistol, needler, plasma rifle) on the hips;
  frag grenades on the left chest, plasma grenades on the right chest. Reach and squeeze the grip to take one. Two-handed aim along the line
  between the hands. Immersive reload: B (Y on the left) drops the magazine, take a fresh one from the belt pouch and seat it; plasma
  weapons overheat and vent instead. Grenades: take, trigger to arm, throw with your hand's own speed; plasma grenades stick.
  **Physical melee**: swing the gun (or your fist) into someone - the weapon's own melee damage, from behind it kills at once, as in Halo.
  **Pick up / drop / swap**: walk over a weapon you carry for its ammo; grip a weapon on the ground to take it (with two carried, the one in
  your hand is swapped); let go of a gun away from the holsters and it drops. Powerups by touch (overshield, active camouflage, health).
  Every number from the tags: rates of fire, heat, battery, spread, projectile speeds and gravity, damage, shields 75 / body 75, shield
  recharge 6 s after damage and 4 s to refill, grenades 4 max with 2 frags at spawn.
* **Monitor play** (Desk + Combat): mouse to fire, right mouse zoom, R reload, F melee, G grenade, 1 / 2 weapons, E pick up / enter, V first /
  third person. Every monitor player's avatar holds its weapon like the Spartan: a hidden cyborg puppet plays Halo's own third-person
  animations (idle, moving, fire, reload, melee, throw, death, vehicle seats) and the avatar's bones are posed onto it.
* **Vehicles** (Vehicles): Warthog (driver, passenger, chain gunner holding the gun's two grips), Rocket Warthog, Ghost, Scorpion (driver +
  riders), Banshee, gun turret, placed from the scenario for the game type and the rules' vehicle set. Driving from the tags' physics (top
  speeds match the game: Warthog 24.8 of 25.1 m/s, Ghost 20.5 of 20.6, Scorpion 12.8 of 12.8), splatter, flip over with E. Halo CE
  multiplayer vehicles are indestructible (their collision models have no body vitality); an empty moved / overturned vehicle returns to
  its spot after 30 s. Seats and passengers work like the Euro Truck Simulator Dreamport (forced sitting pose, the vehicle carries the
  avatar, heading-only view). The driver's view is third person the way the Somnium client's own vehicle system does it (monitor: Halo's
  chase camera, steer toward the view; headset: lifted behind the vehicle, heading only, left Y = first person); VR steering on the stick
  (Halo's "drive where you look" is the setting `vehicles.vrsteer`).
* **HUD** (HUD): Halo's own HUD from its tags - shields and health top right, ammo / battery / heat top left, grenade counts, the crosshair
  (red over an enemy) and its warnings, sniper / pistol zoom overlay, the motion tracker bottom left, HUD messages ("Hold [grip] to pick up
  ..."), the kill feed in Halo's wording, score and time, waypoints over objectives, damage arrows, respawn countdown, game over and the
  scoreboard (F1 on a monitor, hold left Y in VR). Monitor: a screen overlay; headset: a head-locked visor.
* **Effects** (Effects): the game's own effects - muzzle flashes, casings, impacts per surface, plasma, contrails, explosions, decals,
  lights; vehicles' lights and exhaust.
* **Sounds** (Audio): every sound decoded from `sounds.map` (Xbox ADPCM and Ogg Vorbis), positional at the tags' distances; weapons,
  impacts, vehicles (engine pitch by speed), ambience per map, the 44 announcer lines, ui sounds, the title music in the hub.
* **Multiplayer** (Net, the Black Ops 2 slot layer): 50 scene slots "Halo Net Player 0..49" plus 4 master world blocks "Halo Net World 0..3"
  (pickup timers for late joiners, flags / ball / hill, parked vehicles). Other players' weapons in their hands and on their bodies, their
  shots / grenades / melee replayed, hit boxes on their avatars, shooter-authoritative hits applied once, deaths with the killer, the
  instance master and the lobby, drivers authoritative for their vehicles.

## How to test

1. Upload the world; enter it with a whitelisted Somnium name. Anyone else sees an empty world.
2. In the ring hub: MULTIPLAYER -> pick a game type and a map -> START GAME (instance master). Or the HALO app on the tablet.
3. In VR: draw the pistol from the right hip, the rifle over the right shoulder, take the handguard with the left hand; B / Y drops the
   magazine, take one from the belt pouch; grenades from the chest; swing the gun at someone for melee; walk to a Warthog and grip near a seat.
4. On a monitor: the keys above; V switches first / third person.

## Player.log lines to look for

`Halo Dreamport ... iteration 1`, `Access: ...`, `Boot: install ..., 19 multiplayer maps`, `Front: the ring hub and its menu are up`,
`Lobby: ...`, `Loading: <map> (<type>)`, `World: <map> built: ...`, `Match: spawned at ...`, `Combat: spawned with ...`,
`Vehicles: placed N of M scenario vehicles ...`, `HUD: started on ...`, `Audio: preloaded ...`, `Fx: <map> effects ready ...`,
`Net: the instance master is ...`, `Desk body: ...`. Each module's notes list more (DevTools/agents/<code>/NOTES.md).

## Verified and not verified

Every module was exercised in the Unity editor's play-mode lab on the real install (DevTools/lab.sh scenarios: front-hub, front-lobby,
front-tablet, world, world-all, world-models, match-slayer, match-modes, combat, vehicles, desk, hud, fx, audio, net) with simulated hands
and scripted players. **Not tested by anyone yet: the Somnium client, a real headset, a real Fusion session with two players, the upload
server's compile.** Sounds were measured, not listened to.

## Known gaps

* Not drawn: lens flares, weather, grass detail sprites, BSP decals; moving machines (lifts) stay still; the CTF flag shows its pole and
  emblem without the cloth.
* Monitor first person: the left hand stops about 7 cm short of the handguard; a Warthog driver's hands stop 10-12 cm short of the wheel
  (avatar arms are shorter than the cyborg's).
* VR zoom sets the zoom value only (no scope view yet). Juggernaut and Assault game types are not built. Flamethrower, fuel rod gun and
  needler super-combine were not exercised. If the instance master leaves mid-match, everybody returns to the lobby.
* Some numbers are ours (marked in the code): holster positions, the sound distance curve, effect light lengths, vehicle respawn 30 s,
  seat reach 1.6 m, spawn weights.

## License note

The tag layouts were derived from Invader's tag definitions (DevTools/reference/invader-defs, GPL-3.0) and the Ogg decoder is NVorbis
(MIT, licence header kept in Data/Audio/Vorbis). Fine for private testing with the whitelisted team; decide before any public release.

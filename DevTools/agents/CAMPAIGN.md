# Halo Dreamport - CAMPAIGN (phase 2) shared brief

Read DevTools/AGENTS.md first (all its rules still hold: no game assets shipped, C# 9, no Span, tools, lab, ownership, never kill Unity).
Multiplayer is done and live (iteration 3, tested by the user in VR). The user now asked: **"start doing the campaign!"**

## Goal
Halo: Combat Evolved's campaign, playable in Somnium as the player's own avatar (VR first-class, monitor too), from the player's own
install: `MAPS\a10.map` (Pillar of Autumn), a30 (Halo), a50 (Truth and Reconciliation), b30 (Silent Cartographer), b40 (Assault on the
Control Room), c10 (343 Guilty Spark), c20 (The Library), c40 (Two Betrayals), d20 (Keyes), d40 (The Maw). The missions' OWN content
drives everything: their compiled scripts, AI encounters, devices, cutscenes, dialogue. Nothing invented where the files have it.

**Milestone 1 = a10 (Pillar of Autumn) playable start to finish solo** (its intro cutscene, Keyes, the crew, the Covenant boarding, the
lifeboat), then a30 and b30, then the rest. Keep every system general (all 10 maps); use a10 as the test bench, then a30.
Co-op (every whitelisted player in the instance in the same mission, the instance master hosts AI + scripts) is designed in parallel by
Net and switched on once solo works.

## Data (verified by the lead, harness `camp`)
| map | MB | BSPs | scripts | globals | actor palette | encounters | squads | actor starts | command lists | recorded anims | cutscene cams | devices m/c | vehicles |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| a10 | 91 | 9 | 185 | 84 | 18 | 39 | 144 | 296 | 124 | 99 | 292 | 245/0 | 56 |
| a30 | 36 | 2 | 139 | 108 | 10 | 22 | 179 | 390 | 3 | 63 | 25 | 4/1 | 31 |
| b30 | 34 | 2 | 85 | 74 | 10 | 32 | 115 | 280 | 7 | 34 | 34 | 129/6 | 27 |
| d40 | 89 | 10 | 297 | 65 | 35 | 56 | 192 | 1142 | 69 | 11 | 203 | 156/5 | 20 |
Scenario offsets (layouts.txt `struct Scenario`): actor palette +1056, encounters +1068 (176 B: squads +128 (232 B: actor starting
locations +208, 28 B), platoons +140, firing positions +152), command lists +1080, ai conversations +1128, script syntax data +1140
(data ref, 380076 B = a fixed node table), script string data +1160, scripts +1180 (92 B), globals +1192 (92 B), references +1204,
cutscene flags +1252, cutscene camera points +1264, cutscene titles +1276, recorded animations +876 (64 B, event stream data ref +44),
trigger volumes +864 (96 B), bsp switch trigger volumes +924, device groups +648, player starting profile +840, structure bsps +1444.
HaloScript function / global tables with their Halo PC retail ("gbx-retail") indices: `DevTools/reference/riat/definition.json`
(RIAT, the open HSC compiler - read its README/compile code for the node layout and value types).

## Contracts (new stubs written by the lead; owners fill them, add members, never break signatures)
- `HaloCampaign` (Game/Campaign, K): Active, Mission, Difficulty, BspIndex, StartMission / LeaveMission / SwitchBsp / Save / Revert / End,
  events BspSwitched, Checkpoint, Ended. Also `HaloTeam` + `HaloAllegiance`.
- `HaloObjects` (Game/Campaign, K): scenario objects by name (Find / Register / Create / Destroy).
- `HaloUnit` + `HaloBipeds` (Game/Characters, C): the biped component with its AI control surface (MoveVelocity, LookPoint, AimPoint,
  Firing, Crouching, Melee, ThrowGrenade, Jump), vitality, team, Kill, TakeHit (IHaloDamageable); Spawn(unit or actor variant tag ...).
- `HaloAi` (Game/Ai, I): Place / Erase / Kill / LivingCount / Units / Enabled / Clear.
- `HaloScript` (Game/Script, S): Start / Stop / Tick / Running.
- `HaloCinematic` (Game/Cinematic, Q): Begin / End / CameraSet / CameraControl / Letterbox / Fade / Title / PlayRecording / Clear.
Existing multiplayer modules stay owned by their agents (some are resumed for campaign additions): World (W), Combat (G), Vehicles (V),
Audio (A), Effects (X), HUD (H), Front (F), Net (N), Match (M), Desk (D).

## Ownership (campaign)
| Code | Module | Owns |
|------|--------|------|
| S | Script (HaloScript VM + every engine function binding) | Data/Script/, Game/Script/, Editor/HaloLabScript.cs |
| I | AI (actors, encounters, behaviours, perception, pathing, allies, AI in vehicles) | Data/Ai/, Game/Ai/, Editor/HaloLabAi.cs |
| C | Characters (bipeds: models, full animation graph playback, damage regions, deaths, weapons in hand, grenades by AI) | Data/Characters/, Game/Characters/, Editor/HaloLabCharacters.cs |
| K | Mission (campaign flow, map load with all BSPs, player start / profile, devices (doors, lifts, controls), trigger volumes, checkpoints / revert, objectives, difficulty, named objects) | Data/Campaign/, Game/Campaign/, Game/HaloGame.Campaign.cs (new partial hooks below), Editor/HaloLabCampaign.cs |
| Q | Cinematics (cutscene camera, letterbox, fades, titles, recorded animations, VR cinema screen, subtitles / dialogue) | Data/Cinematic/, Game/Cinematic/, Editor/HaloLabCinematic.cs |
The lead adds HaloGame partial hooks for K: `CampaignTick()` (every frame) and `CampaignStop()` (leaving the world) - K implements them
in Game/HaloGame.Campaign.cs.

## Rules for the campaign
- Game tick: Halo runs scripts and AI at 30 ticks a second; keep a fixed 30 Hz simulation step for scripts / AI and interpolate visuals.
- A script function you cannot do yet must never crash the script: log it ONCE ("Script: unimplemented <name> (<n> calls)") and return
  the type's default; the user's Player.log then lists exactly what is missing. Same for AI behaviours and devices.
- VR comfort: the player's view is never moved by a cutscene, script or device without the player (lifts carry the player like the
  Euro Truck Simulator riders: the client's moving-platform technique; cutscenes show on a floating screen in VR).
- Log one-time facts for Player.log (`Script:`, `AI:`, `Characters:`, `Campaign:`, `Cinematic:`).
- Coordinate in DevTools/agents/REQUESTS.md as before; NOTES.md per agent.

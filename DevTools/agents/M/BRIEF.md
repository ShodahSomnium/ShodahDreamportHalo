# M - Match (session flow, map load, spawning, game types, scoring, respawn)

Goal: "Let's start off with Multiplayer first": the whole match loop of Halo PC multiplayer, from the lobby's START to the end screen
and back to the ring hub. You are the integrator of the game flow: you call the other modules at the right moments.

Build (Game/Match, Game/HaloGame.Match.cs, Game/Shared/HaloSession.cs is yours):
1. `HaloMatchControl.StartMatch(rules)`: HaloLoading.Show -> open the map file on a worker (`new HaloMap(entry.File)`, HaloScenario) ->
   `HaloSession.Open(...)` with a world root at HaloSpace.MapOrigin -> `HaloAudio.Preload`, `HaloFx.Preload` -> `yield HaloWorld.Build`
   (progress to HaloLoading.Progress) -> `HaloSession.NotifyWorldBuilt()` -> `HaloVehicles.Spawn(rules)` and the Combat module's
   item spawns (it may listen to WorldBuilt itself - agree in REQUESTS.md) -> spawn the player -> HaloLoading.Hide -> HaloHud.Start ->
   phase Playing. `LeaveMatch()`: HaloSession.Close (Unloading), HaloCombat.Clear, HaloVehicles.Clear, HaloFx.Clear, HaloHud.Stop,
   HaloWorld.Unload, destroy the world root, release textures/materials (HaloTextures.Release / HaloMaterials.Release), teleport back to
   the hub (HaloSpace.RoomOrigin) with the Front's fade.
2. Spawning like Halo: start locations of the scenario allowed for the game type (`StartLocation.Allows`), team ones for team games;
   pick like the game (avoid spawning near enemies / in view; Halo's spawn weighting), teleport via `HaloPlayer.Teleport(pos, yaw)`
   (Unity yaw from HaloSpace.Yaw(facing)), `HaloLocal.ResetLife()`, `HaloCombat.Spawned()`, `HaloEvents.RaiseLocalSpawned`.
   Respawn after death: HaloEvents.LocalDied -> countdown (rules.RespawnSeconds + Halo's base) -> HaloLocal.RespawnIn for the HUD.
   While dead: the player's view stays put (VR comfort) - the HUD shows the respawn timer.
3. Game types with Halo PC's rules: Slayer (FFA / team; kills, suicides -1, betrayals), Capture the Flag (flags from the netgame flags,
   carried = the flag weapon from globals multiplayer information, return by touch, capture at home flag), King of the Hill (hill flags
   with the same usage id form the hill outline - a moving hill option; time in hill), Oddball (ball weapon, time carried), Race (race
   track flags in order, laps, vehicles). Score limit / time limit, end of game -> phase Ending -> results (HUD) -> back to the lobby
   (Front) after a few seconds. Team assignment (balanced) for team games; FFA colours.
4. Scoring and the kill feed source: HaloEvents.Killed from local deaths (victim side, like BO2) and the Net module's remote announcements;
   keep per-player kills/deaths/score (HaloLocal + the Net players list); HaloMatchControl.TimeLeft / TeamScore for the HUD.
5. Out-of-map safety: falling below the map's lowest point / death barriers -> death (Halo kills you when you fall off), and a
   fallback if a spawn fails (log it).
6. Multiplayer timing: the master starts (Net publishes rules + UTC start); late joiners are pulled in; everybody plays the rules they
   read when the match began (BO2 pattern). Coordinate with Net (REQUESTS.md).
Implement `Game/HaloGame.Match.cs` (partial void MatchTick/MatchStop).
Lab: `Editor/HaloLabMatch.cs` - start a Slayer match on Blood Gulch from code, wait for Playing, check the player was teleported to a
start location, simulate a death and respawn, end by score limit; pictures from the spawn (needs the World module's build: until it lands,
the stub world is empty - verify the flow, then re-check when W's builder is in).

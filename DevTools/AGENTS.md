# Halo Dreamport - shared brief for module agents

You are one of several agents building the **Halo: Combat Evolved multiplayer Dreamport** for Somnium Space (a Unity 6000.3.5f2 world)
in parallel, each owning one module. The lead (the main session) set up the project, the data core, the scene and the contracts below.
Read this whole file first, then your own brief (DevTools/agents/<code>/BRIEF.md).

## What a Dreamport is (the user's rules, non-negotiable)
- The SAME game, playable inside Somnium as the player's own avatar, VR first-class and monitor (non-XR) too.
- The world ships **no game assets**: everything (maps, models, textures, sounds, strings, HUD pictures, numbers) is read at run time
  from the player's own install `C:\Program Files (x86)\Microsoft Games\Halo` (`MAPS\*.map` cache files version 7, `MAPS\bitmaps.map`,
  `MAPS\sounds.map`, `MAPS\ui.map`). Our code carries only ids, offsets, numbers and the names of tags it asks for.
- "Exactly like the game": read the game's own tag values (weapon stats, timings, HUD layout, sounds, effects) instead of inventing.
  When you must invent a number (VR-only things like holster positions), mark it `// ours` in the code.
- The whitelist gate (Core/HaloAccess) runs before anything; a visitor sees nothing. Never start anything outside `HaloGame` hooks.
- VR comfort: never pitch/roll the player's view with a vehicle (heading only); never move the VR camera without the player.
- The user tests uploads in the Somnium client and reads `Player.log` (`C:\Users\derii\AppData\LocalLow\Somnium Space Ltd\Somnium Space VR\Player.log`).
  Log useful one-time facts with `HaloLog.Info("Module: ...")` (tag `[SomniumHalo]`) so a client run proves what happened.

## Project
- Unity project: `D:\Dreamport\Halo\Halo Dreamport`. Code: `Assets/#User/SomniumHalo` (asmdef `SomniumHalo`, ONE namespace
  `SomniumSpace.Worlds.SomniumHalo` for everything; Editor code in `Editor/` namespace `SomniumSpace.Worlds.SomniumHalo.Editor`).
- Scene `Assets/Halo Scene.unity`: object "Halo Game" (HaloGame), "Halo Net" (SceneNetworking) and 50 network slots
  "Halo Net Player 0..49" (Fusion NetworkObject + NetworkBridgeData64). Do not edit the scene unless your brief says so.
- **C# 9**, no `Span`/`Memory`/`stackalloc` (the Fusion 2.0 upload server rejected them; since Fusion 2.1 only SceneNetworking.OnReliableDataReceived takes a `ReadOnlySpan<byte>`, as Fusion requires - build.sh checks), no `unsafe`, no `dynamic`, no new assembly references
  (allowed: UnityEngine modules, UI, TMP, XRI, URP, Fusion, SomniumSpace.Bridge/Network). No reflection into the Somnium client by
  class/field name (it is obfuscated); the Input System by reflection is OK (see BO2's Bo2FirstPerson).
- Per-frame code: no LINQ, no lambdas/closures, no allocations in steady state.
- `Data/` is Unity-FREE pure C# (shared with the dotnet harness). `Game/` is Unity.

## Tools (all offline, no Unity needed except the lab)
- **Compile**: `bash "D:/Dreamport/Halo/Halo Dreamport/DevTools/build/build.sh"` - editor-like, server-like and editor-tool builds,
  prints errors (all agents share one tree: errors in OTHER modules' files are not yours - filter with grep on your folder; never "fix"
  another module's file, write in DevTools/agents/REQUESTS.md instead). Run it often; never leave your files uncompiling.
- **Server API guard**: `bash "D:/Dreamport/Halo/Halo Dreamport/DevTools/fusionapi/check.sh" "D:/Dreamport/Halo/Halo Dreamport/DevTools/build/out/server_like.dll"`
  - any Fusion/Somnium member it prints is unproven on the upload server: avoid it (use what BO2 used) or tell the lead.
- **New files need .meta files**: run `bash "D:/Dreamport/Halo/Halo Dreamport/DevTools/metas.sh"` after creating files/folders.
- **Data harness** (dotnet 8, compiles `Data/**`): `DevTools/harness` - add your own probe file `Probe<Code>.cs` (partial class Probe,
  method `public static int <Code>(string[] a)` and register it in Probe2.cs's switch ONLY by adding one `case` line), then
  `cd DevTools/harness && dotnet build -c Release -v q && dotnet run -c Release --no-build -- <cmd> ...`. `Png.Write` writes pictures
  you can look at with the Read tool. Existing: `head <map> [class]`, `bitm <map>`, `res bitmaps|sounds`, `models <map> [name]`,
  `png <map> <bitmap name part> <outdir>`, `scen <map>`. Several agents build the harness: if a build fails because of another
  agent's probe, wait a minute and retry (or copy the harness to DevTools/agents/<code>/harness for yourself).
- **Lab** (Unity play mode with graphics, ~2-4 min a run, 2 slots, queued): `bash "D:/Dreamport/Halo/Halo Dreamport/DevTools/lab.sh" <scenario> [args]`.
  Add your scenarios in `Editor/HaloLab<Module>.cs`:
  `[InitializeOnLoad] static class HaloLabX { static HaloLabX() { HaloLabRunner.Register("x-name", r => Run(r)); } static IEnumerator Run(HaloLabRunner r) { ... r.Aim(eye, target); r.Shot("name"); r.Check(cond, "what"); yield return null; } }`
  Pictures land in `DevTools/lab/<scenario>/`; LOOK at them (Read tool) before claiming visuals work. Compile with build.sh first
  (a compile error makes Unity hang in batch mode until the timeout). **NEVER kill Unity.exe processes** (other projects' editors
  run on this machine) - a lab ends by its own timeout. Use the lab sparingly (the user dislikes heavy test sweeps): a few runs to
  verify what you built, not regression sweeps.
- Format reference: `DevTools/reference/layouts.txt` = EVERY Halo tag struct with field offsets (generated from Invader's definitions
  in `DevTools/reference/invader-defs/*.json`, also handy for enum option lists). Grep it: `awk '/^struct Weapon /,/== end/' layouts.txt`.
  Structs with `: Base` include the base's fields first (Weapon : Item : Object). All offsets verified to match the declared sizes.
- Decompiled Halo (Xbox) for behaviour questions: `D:\Dreamport\Halo\REFERENCE\halo-main` (small, mostly main loop) - rarely useful.
- The previous Dreamports to copy patterns from (read-only!):
  - **Black Ops 2** `D:\Dreamport\BO2\Black Ops 2\Assets\#User\SomniumBO2` (README.md there = what each piece does; the most complete
    VR shooter: Game/Vr, Game/Combat/Bo2Handling*.cs (holsters, two-hand aim, immersive reload, grenades), Game/Net (slots, wire,
    remote players, instance master, lobby), Game/Front (room menu, tablet app Bo2TabletApp/Bo2TabletLink, loading screen, HUD
    Bo2Hud/Bo2DeskHud), Game/Net/Bo2Desk*/Bo2Puppet* (monitor player's avatar holding the gun, third person)). Port freely: rename
    Bo2->Halo, keep what is generic, replace what is BO2 data.
  - **Euro Truck Simulator** `D:\Dreamport\Euro Truck Simulator\Euro Truck Sim\Assets\#User\SomniumEuroTruck` (vehicles, seats,
    passengers: EtsRiders.cs, EtsTruckSeat.cs, EtsAvatarSync.cs, EtsAvatarCloth.cs, EtsTruckDoor.cs, EtsSteeringGrab.cs, EtsNetVehicle.cs;
    vendored NVorbis Ogg decoder in Ets/Vorbis; FMOD/sound code).
  - Somnium client source `E:\Somnium Space\Main Build\Assets\_SomniumSpace` (VehicleSystem, Elevators (MobilePlatform),
    Player/Scripts/Interaction/ThirdPersoneMode, VRTablet, Inputs/*.inputactions) - how the client itself does things.

## Data core you can use (lead-owned; ask before changing)
- `HaloMap` (Data/Map): `new HaloMap(path)` loads a .map; `Tags`, `Find(cls, name)`, `OfClass("weap")`, `Tag(id)`, `Scenario`.
- `TagReader` (`tag.Root`): `U8/I16/U16/I32/U32/F32/Vec3(offset)`, `Block(offset, stride)` (reflexive), `Ref(offset)` (dependency ->
  HaloTag), `DataBytes(offset)` (data ref), `Str32`. BSP data has its own address base: `HaloScenario.BspRoot(i)`.
- `HaloResources.Get(path)` (bitmaps.map / sounds.map: retail PC keeps ALL bitmap pixels and sound samples there; the tag's data offset
  is absolute in that file when flagged external).
- `HaloBitmap` (images, sequences, sprites, `Raw`, `DecodeRgba`) + `HaloDxt`. Verified: textures decode correctly.
- `HaloModel` (gbxmodel: nodes, markers, regions/permutations/LODs, geometries/parts, `Read(part)` = vertices + triangle list).
  Verified: all 447 Blood Gulch parts index within their vertices. Node rotation convention NOT yet verified (see HaloModelBuilder).
- `HaloScenario` (BSPs, skies, scenery/vehicles/weapons/equipment/machines/controls/lights/sound scenery with palettes, start locations,
  netgame flags, netgame equipment, starting equipment). Verified on Blood Gulch.
- `HaloInstall` (find the install, 19 MP maps with labels). `HaloSpace` (Halo -> Unity: (x,y,z) -> (x,z,y) * 3.048 m, `Rot`, `Yaw`).
- Unity side: `HaloTextures.Get(bitmapTag, index)`, `HaloMaterials.Get(shaderTag)` (first version: main map only),
  `HaloModelBuilder.Build(modelTag, parent, options)` (SkinnedMeshRenderer, node transforms by name, markers "marker:name").
  These three are owned by the World module from now on (it will make them faithful; their public calls stay).
- `HaloPlayer` (Game/Vr, ported from BO2: Bridge local player, Head(), XrPresent, Root, HeadBone, hands, Teleport, BackKey),
  `HaloXr` (controller input), `HaloBody`, `HaloLaserAim`, `HaloTabletGuard` - owned by the Combat module (it may extend them).

## Contracts between modules (Game/Shared + stubs)
- `HaloSession` (phase Hub/Loading/Playing/Ending, Map, Scenario, WorldRoot, Rules; events PhaseChanged, MapOpened, WorldBuilt,
  Unloading) and `HaloRules` (the game variant). Owner: Match.
- `HaloLocal` (static partial: the local player's health/shield/weapons/grenades/vehicle seat/team/score) - owners add fields only in
  their own partial file `Game/Shared/HaloLocal.<Module>.cs`; never change another module's fields.
- `HaloHit`, `IHaloDamageable`, `HaloKill`, `HaloTeams`, `HaloEvents` (Killed, Fired, LocalSpawned, LocalDied, Message), `HaloLayers`.
- Stub APIs, one per module, written by the lead with agreed signatures - the OWNER replaces the body and may ADD members; never remove
  or change an existing signature without fixing every caller (grep the tree):
  `HaloWorld` (World), `HaloAudio` (Audio), `HaloFx` (Effects), `HaloLoading` (Front), `HaloMatchControl` (Match), `HaloHud` (HUD),
  `HaloCombat` (Combat), `HaloVehicles` (Vehicles), `HaloDesk` (Desk), `HaloNetGame` (Net).
- `HaloGame` partial hooks (implement them in YOUR `Game/HaloGame.<Module>.cs` as `partial void X() { ... }`): Front: `FrontStart`,
  `FrontBootDone`, `FrontTick`, `FrontStop`; Net: `NetStart`, `NetTick`, `NetStop`; Match: `MatchTick`, `MatchStop`. Other modules
  start from these (e.g. the Match module calls HaloHud.Start / HaloVehicles.Spawn at the right moments) or from HaloSession events.
- `HaloSettings` (key=value saved choices: `Num/Flag/Text/Int` + setters, `Changed` event). Use your own key prefix ("combat.", "hud.").

## Ownership (only edit files you own; new files only inside your folders)
| Code | Module | Owns |
|------|--------|------|
| lead | lead | Core/, Data/Map/, Data/Bitmaps/, Data/Models/HaloModel.cs, Game/HaloGame.cs, Game/HaloSettings.cs, Game/Shared/ (except owners' HaloLocal.<Module>.cs), Game/Access/, Editor/HaloLab.cs, Editor/*.asmdef, DevTools scripts, the scene |
| W | World (map look + collision) | Data/Bsp/, Data/Shaders/, Data/World/, Game/World/ (incl. HaloTextures, HaloMaterials, HaloModelBuilder), Shaders/, Resources/ (shader carrier materials), Editor/HaloLabWorld.cs; may set `_shaderCarriers` in the scene's HaloGame block |
| A | Audio | Data/Audio/, Game/Audio/, Editor/HaloLabAudio.cs |
| X | Effects | Data/Effects/, Game/Fx/, Shaders/Fx/ (coordinate carriers with W via REQUESTS.md), Editor/HaloLabFx.cs |
| G | Combat (weapons, VR handling, grenades, melee, damage, pickups, powerups) | Data/Combat/, Game/Combat/, Game/Vr/, Game/Shared/HaloLocal.Combat.cs, Editor/HaloLabCombat.cs |
| V | Vehicles (driving, seats, passengers, vehicle weapons, vehicle third person) | Data/Vehicles/, Game/Vehicles/, Game/Shared/HaloLocal.Vehicles.cs, Editor/HaloLabVehicles.cs |
| F | Front (ring hub, room menu, tablet app, lobby UI, loading screen, settings UI) | Data/Ui/, Game/Front/, Game/HaloGame.Front.cs, Editor/HaloLabFront.cs, Editor/LabStubs/ |
| H | HUD (Halo HUD in VR + monitor, scoreboard, messages, motion tracker) | Data/Hud/, Game/Hud/, Editor/HaloLabHud.cs |
| N | Net (slots, wire, remote players, hits, instance master, lobby sync, world-object sync) | Game/Net/, Game/HaloGame.Net.cs, Game/Shared/HaloLocal.Net.cs, Editor/HaloLabNet.cs |
| M | Match (session flow, map load, spawning, game types, scoring, respawn) | Game/Match/, Game/HaloGame.Match.cs, Game/Shared/HaloSession.cs (+HaloRules), Editor/HaloLabMatch.cs |
| D | Desk (monitor play: camera, avatar posing with the weapon, third person, Halo character animations) | Data/Anims/, Game/Desk/, Editor/HaloLabDesk.cs |

## Coordination
- Your notes: `DevTools/agents/<code>/NOTES.md` - keep a short running status (what works, verified how, what is open). Others read it.
- Cross-module needs: append to `DevTools/agents/REQUESTS.md` ("[from X to Y] need ... because ..."). Check it now and then for requests
  to you and answer by doing them (note "done" under the request). If you need an API from a module that is not there yet, code
  against the stub signature; if the stub lacks something, ask (or add an additive member to that stub ONLY if the owner agreed).
- When done: final report = what you built (files), what is verified (lab pictures / harness numbers) and what is NOT, open items,
  Player.log lines to look for. Keep it factual and short.

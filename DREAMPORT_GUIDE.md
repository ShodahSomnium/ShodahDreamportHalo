# How to build a Dreamport (developer guide)

A **Dreamport** is a Somnium Space world that lets players play an existing game *inside Somnium*, as their own avatar, in VR and on a
monitor: the same game, rebuilt in Unity, reading everything from the player's **own copy of the game** at run time. This guide is
distilled from the Dreamports built so far:

| Dreamport | Project | Code | What it shows best |
|---|---|---|---|
| Halo: Combat Evolved | `D:\Dreamport\Halo\Halo Dreamport` | `Assets/#User/SomniumHalo` | The current template: whitelist, module split, offline tools, lab, campaign + multiplayer, co-op with host hand-over |
| Black Ops II | `D:\Dreamport\BO2\Black Ops 2` | `Assets/#User/SomniumBO2` | VR gun handling (holsters, two-hand aim, immersive reload), net slot layer, monitor avatar posing (puppet), tablet app |
| Euro Truck Simulator 2 | `D:\Dreamport\Euro Truck Simulator\Euro Truck Sim` | `Assets/#User/SomniumEuroTruck` | Vehicles, seats, passengers riding a moving vehicle, map streaming, sound banks |
| World of Warcraft | `D:\Dreamport\WoW\World Of Warcraft` | `Assets/#User/SomniumWoW` | The original whitelist / isolation, floating UI |
| Metroid Prime | `D:\Dreamport\Metroid Prime\METROIDPRIME` | `Assets/#User/SomniumMetroid` | Disc-image reading, particles, offline build heads |
| Skyrim, Rec Room, Fallout 2, Age of Mythology | `D:\Dreamport\...` | | Avatar posing (Skyrim GearPoser), isolation origin (Rec Room RrIsolation) |

Each project has a `README.md` in its code folder with an **Iterations** table (what each upload changed) and the Player.log lines to
look for. Read Halo's and BO2's first.

---

## 1. The rules (non-negotiable)

1. **Ship no game assets.** The world contains only code, a few shaders and carrier materials. Every map, model, texture, sound,
   animation, string and number comes from the player's own install, read at run time (Halo: `C:\Program Files (x86)\Microsoft
   Games\Halo\MAPS\*.map`; BO2: the Steam folder's fastfiles; ETS2: the `.scs` archives). Our code may hold only tag names, file
   offsets and the numbers the game files do not have. Mark every invented number `// ours` in the code.
2. **The whitelist gate runs first, and a visitor sees and hears NOTHING** (section 2). Not the menu, not a log line, not another
   whitelisted player.
3. **"Exactly like the game."** Read values from the game's files (weapon stats, HUD layout, timings, sounds, effects). The users judge
   every build against the real game; "close enough" custom UI gets rejected.
4. **VR comfort.** Never move, pitch or roll the VR camera without the player. Vehicles turn the seated view by heading only. Cutscenes
   play on a floating screen in VR, never by moving the player's head.
5. **Monitor (non-XR) works too.** Every feature needs a desktop path (keys, mouse aim, first / third person).
6. **Report honestly.** Say what is verified (by a measurement or a picture you looked at) and what is not. The user tests uploads in the
   client and reads `Player.log` - log one-time facts so a client run proves what happened.

---

## 2. The whitelist and the isolation (copy this exactly)

Files (Halo names; every Dreamport has the same three pieces with its own prefix):

| File | Role |
|---|---|
| `Core/HaloAccess.cs` | The list and the matching rule (pure C#) |
| `Core/Unity/HaloAccessCheck.cs` | Reads the local player's name / id from the Bridge and decides |
| `Game/HaloGame.cs` | The scene root: runs the gate in `Awake` before ANYTHING else |
| `Game/Access/HaloIsolation.cs` + `HaloIsolationDriver.cs` | Hides and silences other players on this screen |
| `Game/Access/HaloGear.cs` | "Is this remote player a Dreamport player?" (has a network slot record) |
| `Game/Access/HaloRemotes.cs`, `HaloTags.cs` | Finding remote avatars / name tags and restoring them exactly |

### The list
```csharp
public static readonly string[] Whitelist =
{
    "Shodah", "Wildy", "Meloz", "ꨄ Rage Quit ꨄ", "denevraut",
};
```
- Entries are Somnium usernames exactly as on the name tag (letter case and surrounding spaces ignored) **or** Somnium user ids.
- Player.log prints every player's verdict with their id: `Access: <name> (id ...) is on the whitelist` / `is not on the whitelist`,
  so an id can be copied into the list.
- An EMPTY list opens the world to everyone. The Unity editor is always let in (no client there).

### The gate (in the root component's `Awake`)
1. **Hold every log line** in memory (`HaloLog.Sink = HoldLog`): nothing about the Dreamport reaches Player.log before the verdict.
2. **Start the isolation with the rule "nobody is with me"** and show only a plain grey floor while the check waits.
3. **Wait for the username** (`SomniumBridge.PlayersContainer.LocalPlayer.Properties.NickName` / `.Id`; the client fills it a moment
   after the world starts). Nothing starts while waiting, however long it takes: the gate **fails closed**.
4. **Visitor** (not on the list): keep the "nobody is with me" isolation for good, keep the grey floor, **drop** the held log lines and
   stop logging. Nothing else is built, read, loaded, saved or networked: no menu, no hub, no game files opened, no network slot
   claimed, no settings file touched.
5. **Whitelisted:** flush the held log, remove the floor, switch the isolation to the whitelisted rule, then start the Dreamport.
6. Every module starts from the root's hooks (Halo: partial methods `FrontStart`, `NetStart`, `MatchTick`, `CampaignTick`, ...) so
   NOTHING can run before or without the gate. Never add a MonoBehaviour to the scene that does its own thing in `Awake`/`Start`.

### The isolation (both directions of privacy)
- **A visitor's screen:** every other player is hidden and silent ("nobody is with me").
- **A whitelisted player's screen:** a remote player is shown only if they are a Dreamport player - in Halo, if they have claimed one of
  the scene's network slots (`HaloGear.HasRecord(photonId)`). A visitor never claims a slot, so testers never see or hear visitors.
- Decisions are per Photon id, re-read every decision; an unknown id or a failing check counts as "apart" (fails closed). The first
  decision about a new player is applied at once, so a stranger is never drawn even for a frame.
- **How hiding is done** (measured against the client source `E:\Somnium Space\Main Build\Assets\_SomniumSpace`; read the long comment
  at the top of `HaloIsolation.cs`): `Renderer.forceRenderingOff` (never `enabled`, the client writes that), name tag CanvasGroup
  alpha 0 + blocksRaycasts false, chat bubble canvases off, their CharacterController / head trigger colliders off, their voice
  `AudioSource.volume = 0` (never `mute` - that is the client's own block state), other sounds `mute`. Everything is restored exactly
  when they become "with me" again or when the world closes (`OnDestroy` -> `HaloRemotes.Reset()` first, in its own try/catch).
- Name tags of other Dreamport players are hidden while the Dreamport runs (Bridge `SetPlayerTagDisableState`, fallback CanvasGroup).

**Test it every upload:** join with a non-whitelisted account (or ask someone): they must see only the grey floor and no other players,
hear nobody, and their Player.log must contain nothing tagged with the Dreamport after the Access line.

---

## 3. Project setup

- **Unity 6000.3.5f2**, URP, the Somnium SDK template (`SomniumSpaceTemplates`). Copy `Assets/Photon` and
  `Assets/#User/Community Modules` (SomniumSpace.Network) from an existing Dreamport **with their .meta files**.
- **One asmdef** for the world (`SomniumHalo`, `rootNamespace` = the one namespace for all code); Editor code in its own Editor asmdef.
  Allowed references only: Unity modules, `UnityEngine.UI`, `Unity.TextMeshPro`, `Unity.XR.Interaction.Toolkit`, URP Runtime/Core,
  `Fusion.Unity`, `SomniumSpace.Network`, the Bridge DLL (auto-referenced). The upload server compiles against its own list
  (`~ServerReferences`) - anything else fails the upload (AoM once failed on a CommunityModules reference).
- **The scene:** copy the last Dreamport's scene, then **sanitise the uploader**: `WorldIdForUpload: 0`, `DoReplaceExistingWorld: 0`,
  your `AssetName` / description, `ScriptingAssembly: <your asmdef>`, `AsmdefReference` -> your asmdef's guid, preview textures 0.
  (A copied scene still points at the old world's id: uploading it unsanitised would overwrite the other Dreamport.)
- Scene objects: the root ("Halo Game"), "Halo Net" (SceneNetworking) and the network slots (section 6). Custom shaders reach the
  upload only through **carrier materials** listed on the root (`_shaderCarriers`); `Shader.Find` only finds shaders the client ships.
- **Code layout** (Halo):
  - `Core/` - log, access, version, coordinate conversion, shader carriers.
  - `Data/` - **Unity-free** readers of the game's formats (also compiled by a dotnet harness).
  - `Game/<Module>/` - Unity side, one folder per module.
  - `Editor/` - lab scenarios.

## 4. Coding rules the upload server and client impose

- **C# 9.** No `Span`/`Memory`/`stackalloc` (the server's mscorlib rejects them), no `unsafe`, no `dynamic`.
- **No reflection into the client by class / field name** - the shipped client is obfuscated (names differ per build). Public Bridge
  types by their public name are fine (look them up with `Type.GetType` if no world has used them on the server yet), and the Input
  System by action names (BO2's `Bo2FirstPerson`).
- The server's **Fusion is older** than the local copy: run the API guard (section 5) - e.g. `NetworkRunner.GetMasterClient` does
  not exist there.
- Per-frame code: no LINQ, no lambdas (a closure allocates at method entry), no allocations in steady state.
- `OnAudioFilterRead` is not usable in worlds: use `AudioClip.Create` + `SetData` (decode on a worker).
- A MonoBehaviour must not have a method named `Start`/`Update`/... with parameters (Unity rejects the script).
- Every module call from the root goes through a guard (`try/catch` -> one log line): a missing piece is a status line, never a crash.

## 5. Tools (Halo's `DevTools/` - copy the folder)

| Tool | Use |
|---|---|
| `DevTools/build/build.sh` | Offline compile, three ways (editor-like, **server-like**, editor tools), no Unity needed; also checks for Span |
| `DevTools/fusionapi/check.sh <server_like.dll>` | Lists any Fusion / Somnium member no world has used on the upload server before. Must print "all proven" before an upload |
| `DevTools/metas.sh` | Writes `.meta` files for new files / folders (Unity needs them; never hand-edit metas while the user's Unity has the project open) |
| `DevTools/harness/` | dotnet console that compiles `Data/**`: probe the game files, dump layouts, write PNGs (`Png.Write`) you can look at |
| `DevTools/lab.sh <scenario>` | Unity play mode in batch WITH graphics: boots the real scene, runs a scenario from `Editor/HaloLab*.cs`, writes pictures + `LAB:` lines. Two slots (main project / a private copy `DevTools/labproject`) |
| `DevTools/reference/` | Format references (Halo: every tag struct with offsets generated from Invader's definitions; the HaloScript function table from RIAT) |

Lab rules: compile with build.sh first (a compile error makes batch Unity hang); look at the pictures before claiming visuals work;
**never kill `Unity.exe`** (other editors run on the machine); a lab ends by its own timeout. Keep lab runs few - the user tests in the
client and does not want long test sweeps.

## 6. Multiplayer (the slot layer)

Worlds cannot spawn networked objects at run time, so the scene carries pre-placed slots: **"Halo Net Player 0..49"** (Fusion
`NetworkObject` + `NetworkBridgeData64` = 64 ints each) plus extra blocks for world state ("Halo Net World 0..3", campaign "Halo Net
Actor 0..8", "Halo Net Mission 0..1", "Halo Net Host 0..3"). Add slots by copying an existing block's YAML with new fileIDs and
unique SortKeys, as children of "Halo Net".
- Each client claims one free player slot (RequestStateAuthority + claim record), writes only its own (~10 Hz), reads the others.
  A slot is tied to an avatar by the slot owner's PlayerId == `ISomniumPlayer.Properties.PhotonId`.
- Shooter-authoritative hits; the victim applies a hit once (hit counters); deaths are announced by the victim with the killer.
- The **instance master** runs shared things (lobby, world objects, campaign scripts/AI). If the master leaves, the next player takes
  over and the game continues (Halo iteration 5: host state streamed every 15 s; each module registers a capture/adopt part).
- Remote poses are drawn ~0.16 s behind, interpolated.

## 7. Client facts that cost days to learn

- **Bridge hand = the controller GRIP pose**; its forward points ~90 degrees up from where the controller points. Turn it with
  `Quaternion.Euler(90, 0, 0)`, or measure against the client's own laser (NearFarInteractor per hand side).
- **Set `RenderSettings.ambientProbe` directly** (the client does not rebuild it) and keep scene **Fog ON + Linear** or the upload
  strips fog shader variants.
- **Layers:** the avatar's CharacterController is on layer 2 and stays solid against vehicles even with "collision disabled"; desktop
  clicks reach layers 0,1,4,5,14,16; the VR pointer 0,5,6,10,14,16,23 at 5 m. A vehicle the avatar rides must exclude layers 2|7|24.
- **Tablet:** opens on a tap of the secondary button (B/Y) - switch the client's tablet actions off while those buttons mean something
  in game (guns held, seated in a vehicle). Short X press is ours; a long press opens the inspector.
- **Monitor first person** is the client's camera at minimum zoom (force it by queuing scroll notches through the Input System);
  the first-person near plane is 35 cm (move it closer just before rendering); the own head is never hidden by the client.
- **The avatar model is not under the Bridge body** - find it by head position (Skyrim/BO2 `AvatarFind`); mirror / teleport copies
  must be posed too.
- **Lifts / moving vehicles:** carry the body root each frame (ETS riders); for VR seats turn by heading only.
- **Logs:** `C:\Users\<user>\AppData\LocalLow\Somnium Space Ltd\Somnium Space VR\Player.log`. Check the version line
  (`... iteration N`) first - complaints often come from an older upload.
- URP depth priming: anything drawn "first" (a sky) must go in render queue 2501+.

## 8. How the work was organised (it scales)

- **The lead** owns the core, the contracts and the scene; **module agents / developers** each own folders (World, Audio, Effects,
  Combat, Vehicles, Front, HUD, Net, Match, Desk; campaign: Script, AI, Characters, Mission, Cinematics) and never edit another's files.
- Contracts first: one stub class per module with agreed signatures (`HaloWorld.Build`, `HaloAudio.Play`, `HaloCombat.TakeHit`, ...),
  shared state in `Game/Shared` (`HaloSession`, `HaloLocal` partial per owner, `HaloEvents`). Owners fill bodies and only ADD members.
- Cross-module asks go in `DevTools/agents/REQUESTS.md`; each owner keeps `NOTES.md` (what works, verified how, what is open).
- Every upload: compile x3, API guard, one end-to-end lab, bump `Version.Iteration` + a README row, then tell the user plainly
  "READY TO UPLOAD: iteration N" (or what is still missing).

## 9. Starting a new Dreamport - checklist

1. New SDK project; copy Photon + Community Modules; copy Halo's `DevTools/` and adapt paths in its scripts.
2. Copy `Core/` (access, log, version, space conversion, shader carriers), `Game/HaloGame.cs`, `Game/Access/*`,
   `Game/Net/SceneNetworking.cs` with renames (Halo -> your prefix). Keep the gate and isolation exactly as they are.
3. Copy the scene, sanitise the uploader (section 3), rename the root / net objects.
4. Find the install (`<Game>Install.Find`: known folders + a settings override). Write the format readers in `Data/` against the real
   files, verified in the harness (dumps, PNGs) before any Unity code.
5. Build the hub / menu (behind the gate), then the world, then gameplay; VR and monitor paths together.
6. Compile x3, guard, lab, README, upload, read the user's Player.log, fix, repeat.

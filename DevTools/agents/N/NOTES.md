# N notes (Net)

Status: COMPLETE (lab passed 2026-10-19). Slot layer, lobby/master, game start + late join, remote players (weapons, hit boxes, shot / grenade / melee replay, camo,
names), hits both ways, deaths -> kill feed, pickups / drops / objective events, world blocks - IMPLEMENTED. Core verified in the
harness (66 checks: DevTools/agents/N/harness `dotnet run -c Release --no-build -- net`). Lab `DevTools/lab.sh net` = loopback
with scripted players B and C (see bottom for results).

Everything is a static on `HaloNetGame` (namespace SomniumSpace.Worlds.SomniumHalo). Main thread only. Never throws.
Alone / offline / in the editor / without the scene slots: `Online == false`, `IsMaster == true`, `Players` empty, every Note* call
is a no-op, and `StartMatch` counts down and starts a solo match.

## Ids
- **Net player id = slot index 0..49** (HaloHit.SourcePlayer, HaloKill.Killer/Victim, HaloLocal.NetId, HaloRoster ids). -1 = none /
  the world / offline. `HaloLocal.NetId` is written by Net (= my slot once claimed, -1 before).
- Weapons / grenades / causes on the wire = `HaloTag.Index` of the current map (every client reads the same map file; a 16-bit
  fingerprint of the map build is compared - a player on another build is not shown in my match, logged).
- Vehicles = `HaloLocal.VehicleId` (scenario placement index), seats = `HaloLocal.SeatIndex`. Pickups = scenario NetEquipment index;
  dropped weapons have ids >= 1024.

## Files (Game/Net)
- Core/ (pure C#, harness-compiled): HaloNetWire (64-int player record, layout in its header), HaloSlotClaim, HaloNetTransport
  (interface + loopback), HaloNetTimeline (0.16 s interpolation), HaloNet (+ .Master: master/lobby/game command/teams), HaloNetWorld
  (master's world blocks).
- HaloFusionTransport (scene objects over Fusion), HaloNetGame(.Lobby/.Publish/.Remote/.World) = the API, HaloNetPlayer (+HaloNetBody),
  HaloRemoteView (a remote in my match as drawn), HaloNetHitbox, HaloNetReplay (+HaloNetGrenade: fallback visuals), HaloNetAvatar,
  HaloNetU (+HaloNetRules: HaloRules <-> 3 ints). Game/HaloGame.Net.cs, Game/Shared/HaloLocal.Net.cs (NetOnline, NetMaster).

## Status / players (Front, HUD, Match, Desk)
```
bool   HaloNetGame.Online / Claimed / IsMaster / MasterKnown / Networked (another human in MY match)
string HaloNetGame.MasterName, HaloNetGame.NameOf(int slot), HaloNetGame.LocalNick()
int    HaloNetGame.PlayerCount        // me included
IReadOnlyList<HaloNetPlayer> HaloNetGame.Players   // the OTHER Dreamport players (hub or match), slot order, refreshed every frame
HaloNetPlayer HaloNetGame.Find(int slot)
event Action<HaloNetPlayer> HaloNetGame.PlayerJoined / PlayerLeft   // into / out of MY match
```
`HaloNetPlayer`: Slot, PlayerId, Name, IsMaster, InMatch, Alive, Enemy, Team (0/1), Kills, Deaths, Assists, Score, LivesLeft, Carry,
Health (0..1), Shield (0..3), Camo, Vr, Zoomed, Crouch, Reloading, InHand, Position (feet), Head, HeadRot, Yaw, Forward, LeftHand(+Rot),
RightHand(+Rot) (aim frame), Velocity, **Moving**, **FiredRecently**, LastShotAt, HeldWeapon, StowedWeapon (HaloTag), Frags, Plasmas,
VehicleIndex, Seat, Driving, Aim, Avatar (their humanoid Animator once found), Root (Bridge body root), Seen.
For players in my match the pose is the interpolated, drawn one; for players in the hub it is their raw record.
Net also fills M's `HaloRoster` for the players of my match every frame and calls `HaloMatchControl.PlayerLeft(slot)`.

## Lobby (Front)
The Front module owns the lobby flow (HaloLobby: choices, START GAME countdown, pull-in / pull-out of the other players) and talks to
Net only through `IHaloLobbyNet` (Game/Front/HaloLobbyNet.cs), which Net implements (HaloNetGame.Lobby.cs, LobbyLink) and registers in
`HaloLobbyNet.Net` at NetStart. Net carries the master's HaloLobbyState in its record (phase, countdown, map, version, every HaloRules
field, and in a match the game command: map, MatchId, StartUtc). Read-only helpers for others:
```
HaloRules HaloNetGame.MasterRules   // what the master published (null while nothing arrived)
int       HaloNetGame.RulesVersion
bool      HaloNetGame.MatchRunning;  string RunningMap;  double GameStartUtc   // the master's running match
int       HaloNetGame.GameNumber    // the master's match I play (0 = none / solo)
int       HaloNetGame.MyTeam        // 0 red / 1 blue (in the hub: the net side, balanced on arrival; HaloLocal.Team mirrors it there)
bool      HaloNetGame.CanChangeTeam(out why); bool HaloNetGame.RequestTeam(team, out why)   // in a match through HaloMatchControl.SwitchTeam
```
Rules on the wire: HaloNetRules packs every HaloRules field that exists today. **M: tell me when HaloRules gets a field.**

## Match (as agreed in REQUESTS.md)
- Remote deaths -> `HaloMatchControl.ReportKill(kill)` once per announcement (Killer = killer slot or -1, Victim = their slot, Cause,
  Melee, Headshot, Splatter, Grenade, Suicide, Betrayal). My own death is announced from `HaloEvents.Killed` with Victim == me (M raises it).
- M fills HaloRoster from `HaloNetGame.Players` and handles `PlayerLeft` (Net writes neither the roster nor calls PlayerLeft).
- Objectives: `HaloNetGame.SetCarrying(i)` (0 red flag, 1 blue flag, 2 oddball, -1 none), `HaloNetGame.NoteGameEvent(kind, arg)` (8 bits
  each, queued one per record write, with my feet position), `event HaloNetGame.GameEvent(slot, kind, arg)` on the others.
- World block: `SetObjective(i, state, carrierSlot, pos, timer, flags = 0)` (master, i 0..3) / `TryObjective(i, out HaloNetObjective)`,
  `SetMatchInt(i, v)` / `TryMatchInt(i, out v)` (i 0..3).
- Net publishes HaloLocal.Kills / Assists / Score / LivesLeft / Team / Alive / Health / Shield and its own death count per match.
- `HaloNetGame.Net.TeamTotals(team, out kills, out score)` (members + those who left) if useful.

## Combat (wired to G's side directly)
- Hit boxes: `HaloNetHitbox : MonoBehaviour, IHaloDamageable, IHaloHitInfo` on layer `HaloLayers.Hitbox` (`Player`, `Region` 0 head /
  1 torso / 2 arm / 3 leg, `IsHead`; HitSlot, HitIsHead, HitFacing, HitMaterial 21 armour / 22 shield, HitAlive), on the remote avatar's
  humanoid bones when found (checked against the Bridge's drawn head), else on the body of their record. `TakeHit(hit)` with your
  measured damage (SourcePlayer = HaloLocal.NetId or -1): Net publishes it; the VICTIM's client calls `HaloCombat.TakeHit(hit)` once
  (SourcePlayer = shooter slot, Cause, Effect, Melee/Headshot/Explosion/BackHit/Instant, Point = my chest, Direction from the shooter).
  Hits of one cause between two writes are merged (pellets, bursts). Team mates filtered unless friendly fire; the dead are not hit.
  `HaloNetGame.IsRemoteHitbox(Collider, out HaloNetPlayer)` helper.
- Shots: `HaloEvents.RaiseFired(weapon, muzzle, dir, HaloLocal.NetId)` per local shot is what Net publishes. Remote shots raise Fired with
  who = their slot (HUD) and are replayed by `HaloCombat.ReplayShot` (Net's own fallback when HaloCombat.Catalog is null); vehicle
  weapons are left to V. Grenades: `HaloCombat.Thrown` -> record -> `HaloCombat.ReplayThrow` on the others. `HaloCombat.Meleed` /
  `Reloaded` -> counters (the remote weapon swings; D plays reload / melee on monitor avatars).
- Pickups / drops: `HaloPickups.Taken` -> NotePickup(item.Id); `HaloPickups.LocalDropped` -> drop id written into item.Id (loaded, reserve,
  battery travel); remote: `HaloPickups.ApplyRemoteTaken(id)` / `HaloPickups.RemoteDrop(id, weapon, loaded, reserve, pos, vel, slot)`, plus
  the events `HaloNetGame.ItemTaken(item, slot)` / `RemoteDropped(HaloNetDrop)`. Late join: `HaloNetGame.TryItemTaken(item, out secondsAgo)`
  (from the master's world block; items < 128). The explicit `NoteThrow / NoteMelee / NotePickup / NoteDrop` calls also exist.
- Published from HaloLocal: Weapons/Held/InHand/Objective, Frag/PlasmaGrenades, Health, Shield, Camo/CamoUntil, Zoomed, Reloading.
- Remote weapons are drawn at `HaloRemoteView.WeaponScale` (1.7 m / cyborg 2.15 m, x avatar size): grip at the hand, +X along the aim,
  long guns pointing at the left hand when it holds them; stowed: long guns over the back, small ones on the hip.

## Vehicles
```
HaloLocal.VehicleId / SeatIndex / Driving / Vehicle   // read by Net every frame (the driver's pose from HaloLocal.Vehicle's transform + Rigidbody)
void HaloNetGame.PublishDriving(Vector3 pos, Quaternion rot, Vector3 vel, float health01)  // optional override, every frame
void HaloNetGame.PublishAim(Vector3 dir)                  // gunner / vehicle weapon aim (else the head's forward)
bool HaloNetGame.TryDriven(int vehicle, out HaloNetVehicle v)   // somebody else drives it: Position, Rotation, Velocity, AngularVelocity, Aim, Health, Flags, Age, Driver (0.16 s behind)
void HaloNetGame.SetSeat(int vehicle, int seat)          // optional override of HaloLocal.VehicleId / SeatIndex
HaloNetPlayer HaloNetGame.Occupant(int vehicle, int seat)
bool HaloNetGame.WorldWriter                               // I write the world state (instance master in a game)
void HaloNetGame.PublishParked(int vehicle, pos, rot, health01, flags, destroyed=false) / ClearParked(vehicle)   // master
bool HaloNetGame.TryParked(int vehicle, out HaloNetVehicle v)   // vehicles 0..36
```

## World blocks
"Halo Net World 0..3" (NetworkBridgeData64, master-written; layout in Core/HaloNetWorld.cs) - in the scene (the lead added them).
Without them: parked vehicles, objectives and pickup times are not shared with late joiners (Try* return false); all else works.

## Player.log lines
`[SomniumHalo] Net: started`, `Net: N player slot object(s) found ... W world block(s)`, `Net: room joined (player id ...)`,
`Net: slot k claimed (generation g) in N ms`, `Net: the instance master is ...`, `Net: lobby side ...`, `Lobby: net link set (LobbyLink)`,
`Net: game N commanded on map ...` (master), `Net: playing the master's game N`, `Net: player P (slot k) is in my match`, `Net: X's avatar found`,
`Net: X died - killed by Y`, `Net: my death announced`, `Net: back in the hub`, `Net: ... slots, ... world blocks, mine k, ...` (every 2 min).

## Verified
- Harness (Unity-free, DevTools/agents/N/harness `dotnet run -c Release --no-build -- net`, 71 checks): wire round trip of every field,
  claims of 3 clients over a Fusion-like loopback, master agreement, lobby / rules / countdown, game command + late join, F's lobby link
  (PublishLobbyState: phases, match number, start arriving later keeps the world), shots / throws / melee, hits merged + applied once + team
  and dead filters, death with killer, objective / pickup ring / drop events, world blocks (master writes, late joiner reads, survives a
  master hand-over), lobby + running game survive a hand-over, 0 bytes allocated in 300 steady ticks, interpolation smooth, ~0.17 s behind.
- Lab `DevTools/lab.sh net` (2026-10-19, passed, all checks ok): Blood Gulch with the real HaloGame, F's lobby as master (PressStart),
  scripted B and late C over the loopback: claims, master, lobby sides, B saw the countdown 5..1 and decoded the rules, same game number and
  start, B drawn (AR in hand, pistol on hip, head box at the head; pictures net_b_drawn.png / net_b_close.png looked at), 5 shots replayed
  + Fired events, grenade replayed by HaloCombat.ReplayThrow, melee seen, hits both ways once (24 merged points to A through Combat's
  TakeHit 2.00 -> 1.68; A's ray -> B's hit box -> 20 points at B), B's death -> kill feed -> Match credited A's kill, dead boxes off,
  pickup / drop (9 + 40 rounds) / game event once, Combat placed the drop and removed pickup 3, world block pickup time + parked vehicle
  read by late C, A leaving -> F closed the game for all.
- Server API guard: all 117 Fusion/Somnium members proven (clean tree build).
- Weapon part animations (Desk's HaloWeaponAnim on every remote weapon model): "fire" on each replayed shot, "ready" when another weapon
  comes into the hand, "reload-empty" / "reload-full" when the record's reload counter advances (wire int 11 bit 28 = that reload started
  empty: HaloLocal.HeldWeapon.Loaded == 0 at HaloCombat.Reloaded), vents held open while the record says overheated. Lab (2026-10-19,
  passed): the remote pistol's slide moved 41 mm on a replayed shot, its "first-person reload-empty" played on the counter (picture
  net_pistol_reload.png: the magazine dropping out).
## Not verified
Real Fusion session with two clients (only the loopback), the client's avatars (hit boxes on avatar bones, camo hiding, name labels
with the builtin font) - the editor has no Somnium avatars, so boxes were on the record body; Net's own replay fallback (HaloNetReplay)
was not exercised (Combat's replay was used);
the client's avatar bones for hit boxes (editor has no avatars).

# Campaign co-op (phase 2) - design
Host = the instance master (HaloNet.IsInstanceMaster): it alone runs HaloScript, HaloAi, devices, checkpoints. Clients load the same mission
with scripts and AI off (`HaloCampaign.Host == false`) and draw what the host publishes. Solo / offline / no scene objects: nothing changes.
- **Start / late join**: through F's lobby link (HaloLobbyState.Campaign + Difficulty, Rules.MapName = "a10"..., carried in rules int C
  bits 4-6; the game command = map + MatchId + StartUtc as for multiplayer). A late joiner loads the mission, then follows the mission block:
  current BSP, cutscene camera, created/destroyed objects (the event ring keeps the last 32), device positions, and the actors.
- **Actors** ("Halo Net Actor 0..8", NetworkBridgeData64, host-written ~10 Hz): 8-int header (magic 'HLA1', seq, stamp, writer, mission
  hash, mission seq) + 7 actors x 8 ints = 63 units at once:
  a0 serial (0-11, 0 empty) | team (12-14) | alive (15) | unit tag index (16-31); a1-a2 position (cm2); a3 body yaw 16 | aim yaw 16;
  a4 aim pitch 16 | body vitality 8 | shield 8; a5 velocity 3 x 10 bits; a6 weapon tag index 16 | fire counter 8 | melee 4 | grenade 4;
  a7 actor variant tag index 16 | crouch (16) | firing (17) | animation state 8 (24-31, C's code).
  Clients spawn a puppet per serial (HaloBipeds.Spawn of the variant / unit, `Puppet = true`), move it on the interpolated pose
  (~0.1 s behind), replay shots (fire counter -> HaloCombat.ReplayShot from the unit's weapon), melee, grenades, death (alive bit).
- **Mission** ("Halo Net Mission 0": magic 'HLM1', seq, stamp, writer, mission hash, mission seq, then difficulty (0-1) | BSP index (2-9) |
  cutscene on (10) | letterbox (11) | camera point + 1 (12-23) | checkpoint counter (24-31); event counter; event ring of 32: kind (0-3:
  1 create object, 2 destroy object) | scenario object name index (4-19)). "Halo Net Mission 1": 256 device positions, 8 bits each.
- **Damage**: AI -> player: on the host an AI hits a remote player's HaloNetHitbox (TakeHit with SourcePlayer -2 = AI): sent in the host's
  hit ring with the AI bit (31); the victim applies it with SourcePlayer -2. Player -> AI: a client hits a puppet: sent in its hit ring as
  victim 62 = "an actor" with the actor's serial in the hit's effect field; the host applies it to that HaloUnit (TakeHit, SourcePlayer =
  the shooter's slot).

## Campaign co-op - API (implemented: Core/HaloNetCoop.cs, HaloNetGame.Coop.cs, HaloNetPuppet.cs)
```
bool HaloNetGame.CampaignHost        // run scripts + AI here? true solo / offline / as the master; false when following the master's mission
bool HaloNetGame.CoopLive            // co-op blocks of the master's mission are up
void HaloNetGame.TrackUnit(HaloUnit u)          // host: put a unit on the wire now (Net also finds every HaloUnit once a second)
void HaloNetGame.NoteObjectCreated(string name) / NoteObjectDestroyed(string name)   // host: scenario objects by name -> clients' HaloObjects
void HaloNetGame.PuppetHit(HaloUnit puppet, HaloHit hit)   // C: a client puppet's TakeHit forwards here (goes to the host)
HaloNetPuppet HaloNetGame.PuppetAt(int place); HaloUnit HaloNetGame.UnitOfSerial(int serial)
```
- Host -> clients each frame (~10 Hz on the wire): every unit's serial, team, alive, unit / variant tag, position, body yaw, aim, vitality,
  velocity, crouch, firing (+ a fire counter: rising edge of HaloUnit.Firing, then one per AiShotInterval 0.15 s - ours until C counts
  shots), mission state (difficulty, BSP index, cutscene on = HaloCinematic.Playing, checkpoint counter from HaloCampaign.Checkpoint).
- Clients: a puppet per unit (HaloBipeds.Spawn, or a stand-in capsule on layer Hitbox until C's Spawn works), 0.1 s behind; shots ->
  HaloEvents.Fired(who = -2) + HaloCombat.ReplayShot when the weapon is known; death -> HaloUnit.Kill / the stand-in falls; mission ->
  HaloCampaign.SwitchBsp, HaloCinematic.Begin / End, HaloObjects.Create / Destroy.
- Hits: client on puppet -> host HaloUnit.TakeHit (SourcePlayer = shooter slot); host AI on a remote player (TakeHit on their
  HaloNetHitbox with SourcePlayer -2) -> victim's HaloCombat.TakeHit with SourcePlayer -2.
- Lab: `lab.sh net-coop` (Blood Gulch treated as a mission via HaloNetGame.LabCampaign).
- Wired to the delivered modules (2026-10-19): lobby link carries `HaloLobbyState.Mission` / `Difficulty` (rules int C bit 4 = campaign,
  bits 5-6 difficulty, the lobby / game map hash = the mission's; `HaloNetRules.MapOf` knows the campaign maps). `CampaignHost` = not a
  follower of the master's campaign game (K reads it as HaloCampaign.Host). Host: units from `HaloBipeds.All` (puppets skipped), exact
  `HaloUnit.Shots`, `Weapon`, melee / grenade from the Melee / ThrowGrenade flags, cutscene camera `HaloCinematic.CameraPoint` / `Letterboxed`,
  devices `HaloDevices.All` (PlacementIndex < 256, Position). Clients: `HaloUnit.Puppet = true`, `SetWeapon`, `PlayFire` per shot,
  `PlayMelee` / `PlayThrow`, `Kill`; `HaloCinematic.CameraSet(point, 0)` / `Letterbox`; `HaloDevices.SetPosition(index, p)`. No lambdas
  in the per-frame co-op path.
- Verified: harness (81 checks incl. 10 co-op: every actor field, mission state, devices, event ring once / late joiner / survives a host
  hand-over, hits on actors to the host, AI hits marked for the victim). Lab `lab.sh net-coop` (2026-10-19, passed): A hosts 3 units ->
  B reads them (positions, shot counter), B's hit applied once on the host unit (source = B's slot); then S (player 0) takes over as
  master and hosts, A draws its 2 units as puppets (stand-ins: the lab units have no tags), follows a walk smoothly (largest step 4.6 cm),
  replays 10 shots, A's ray on a puppet -> S gets the hit once (30 points), the unit dies on A's screen, S's AI hit reaches A once.
  Picture net_coop_puppets.png looked at. API guard: 117 members proven.
- Not verified: real campaign missions in co-op (K / I / C puppets on a10 with two clients), C's bipeds as puppets, devices / cutscene
  camera / BSP following on a real client, a real Fusion session.
- Open: when the master leaves mid-mission F's new master sets phase Lobby (everyone leaves the mission) - F's call (see my earlier note).

## Campaign co-op - HOST HAND-OVER (user decision, implemented 2026-10-19)
- Host state: `HaloNetHostState.Register(key, order, capture -> byte[], adopt(byte[]))` (S "script" 20, I "ai" 30, K "mission" 10 asked
  in REQUESTS.md); the host captures every part every 15 s (`HaloNetHostState.Every`) and at each HaloCampaign.Checkpoint and streams the
  blob through "Halo Net Host 0..3" (Core/HaloNetStream.cs: chunks of 224 bytes, 4 per 0.1 s, round and round, checksum; blob ids carry
  the writer). Followers keep the last complete blob (`HaveBlob`, `BlobAge`).
- When the master leaves: the next master (lowest id / Fusion master bit) -> its core keeps commanding the running game (lobby phase InMatch
  kept, TakeOverAsMaster), Coop.Writing turns on with the actor / mission / device state it read, `Promote()`: each puppet that is a real
  HaloUnit becomes a hosted unit (Puppet = false, the same serial and place - the other followers' puppets stay), stand-ins are dropped,
  then every part adopts its bytes (null = fallback) in order, then `HaloNetHostState.Promoted` (K sets HaloCampaign.Host and starts the
  host ticks in place). No reload, no teleport, no lobby. F keeps InMatch on a takeover (already in HaloLobby).
- API: `HaloNetGame.SerialOf(unit)` (host), `UnitOfSerial(serial)`, `HaloNetHostState.Promotions / Captures`, `Pack / Unpack`.
- Verified: harness 85 checks (stream: 5000-byte blob assembled by two followers in 0.55 s (23 chunks), a new blob replaces it, the new
  host after the master left still holds the old host's last blob and streams its own). Lab `net-coop` phase 3 (2026-10-19, passed):
  S hosts a real cyborg unit (Characters puppet on A) + a host-state part; S leaves -> A is master + host, the part adopted S's 4 bytes,
  Promoted raised, A still Playing and the lobby InMatch, the cyborg is A's own unit (Puppet off, serial 201 kept), B stays in and reads
  unit 201 from A's blocks at the same place, B's hit on it is applied by A (shield 0.39 -> 0, body 1 -> 0.86), A streams its own host
  state and B receives it. Picture net_coop_after_handover.png looked at.
- Not verified: S / I / K's real parts (scripts continuing mid-thread, AI encounters adopting) - they must register them; a real Fusion
  session; the lead's "Halo Net Host 0..3" objects in the real scene (requested).

## Campaign co-op - AI vehicles, troop exits, voices (2026-10-20)
- Vehicle records in the actor blocks (team 7): a campaign vehicle the host's AI / a recording / the scripts move, with units in it, open or
  hovering travels ~10 Hz (pose, full rotation, velocity, last shot muzzle offset + direction, fire counter, vitality, open, hover) and
  lingers 10 s after it was last active (then the world block's parked pose). Places: units from the bottom, vehicles from the top of the
  63; vehicle serial = 3840 + id (ids up to 510; unit serials stay below 3840). Followers draw it kinematic through HaloNetTimeline on the
  host's block stamp (0.16 s behind, like players), replay its gunfire on its own gun (HaloVehicleGun.Replay), open / close its hatch;
  `HaloNetGame.CoopVehicle(id)` tells V to leave it alone. Host gun shots counted from HaloEvents.Fired (who -2, the vehicle's gun tag).
  Script-created vehicles keep the scenario placement index as id on both sides (K's SpawnPlacement via the object event).
- Unit records carry their seat: a7 exiting (18) | seat (19-22, 15 none) | vehicle id (23-31, 511 none). Followers seat the puppet with V's
  SeatUnit (C's enter clip) and get it out with UnseatUnit (C's exit clip); the seat / clip owns the transform meanwhile.
- Voices: mission ring kind 3 = (serial 0-11 | sound tag 12-27); HaloAiVoice.Say on the host calls `HaloNetGame.NoteVoice(unit, sound)`;
  followers play it once from the puppet; a late joiner does not replay old lines. Ring values are 28 bits now.
- Hand-over: vehicles a follower drew are held where they are and stay on the wire (same places) for the new host's AI.
- Timeline: the drawn time now catches up gradually (0.3 s per s) after a quicker delivery instead of jumping (a late first sample used
  to jump players / vehicles ahead).
- Glue edits in other modules (lead-authorised): V HaloVehicleNet.Receive skips HaloNetGame.CoopVehicle(v.Id); I HaloAiVoice.Say calls
  HaloNetGame.NoteVoice. Nothing in C (V's SeatUnit / UnseatUnit call C's EnterSeat / ExitSeat).
- Verified: harness 88 checks (vehicle record round trip, unit seat fields, voice event once and not for late joiners, late-first-sample
  timeline: largest step 0.217 m at 10 m/s). Lab `net-coop-vehicles` (a30, two clients over the loopback, passed): S hosts, A follows
  (scripts / AI off): lz_cship (id 0) and pass_banshee_1 (id 11) created by the host's object events with the same ids; 4 grunt puppets
  seated during the flight in; the dropship drawn smoothly (largest step 0.214 m a frame, at most 1.15 x its speed x frame time; 1 long
  lab frame left out), 0.00 m off at the hover point; pod opens; 4/4 grunts get out with the exit clip; banshee gunfire replayed (31 shots);
  the grunt line sighted_new_enemy played once. Then S left: A hosts (no reload), the grunts are A's units, a grunt flies the banshee
  (SetAiFlight) firing - follower C reads it flying 55 m in 4 s, fire counter +17 for 18 shots, the pilot seated (vehicle 11 seat 0), the
  voice line once. Pictures netveh_dropship_flying / troops_out / banshee_firing / host_banshee looked at.
- Not verified: the real a30 mission's own scripts / recordings / AI driving these vehicles in co-op (the host here was scripted), two
  real Fusion clients.

## Campaign co-op - NPC dialogue and all script / AI SFX (2026-10-20)
- One-shots: mission block 0 ints 40-63 = sound ring of 8 entries x 3 ints (counter in int 7 bits 16-31). Entry a = kind (0-3: 1 impulse,
  2 impulse stop, 3 effect, 4 conversation line, 5 class gain) | tag index / class (4-19) | scale x64 or yaw (20-27) | target kind (28-30:
  0 2D, 1 unit serial, 2 vehicle id, 3 scenario object name index, 4 position); b = target value | aux, c = marker hash (lower-case, `#n`
  cut) or the position (cm2 b, c). Played once; late joiners skip old one-shots. 8 a write, so a dialogue burst does not overflow at 10 Hz.
- Loops (music, ambience) are STATE: one per actor block header (ints 6-7), 9 at once. a = tag | scale x255 (16-23) | alternate (24) |
  target kind (25-27) | on (28); b = target value. Followers reconcile each frame into Net-held HaloAudio handles; a late joiner starts the
  ones playing now. A promoted host keeps the table and its handles; its script's stop / scale / alternate act on both.
- Host API (HaloNetGame.CoopSound.cs): NoteSound, NoteSoundStop, NoteLoopStart / Stop / Alternate / Scale, NoteClassGain, NoteEffectAt,
  NoteEffectOn; AI conversation lines from HaloAi.LineSpoken (no I edit). Glue in S's Sound.cs / Extra.cs / Objects.cs (REQUESTS.md).
- Device open / close sounds: K plays them from the position change inside the device, which followers get from the host's device bytes
  - already local, no event needed. Open: a follower pressing a control runs K's Use only locally (REQUESTS to K).
- Verified: harness 94 ok (burst of 6 once and in order, loop table, late joiner gets the loop and no one-shots, stopped loop gone, new
  host continues the ring). Lab net-coop-vehicles (25 ok, passed): A as follower hears a30_010_cortana from grunt 12's puppet and the
  conversation line a30_020_cortana (2D) once each, sees the wraith death explosion once (picture netsnd_host_effect), plays the music
  levels\a30\music\a30_01; late joiner C reads the running loop and none of the old one-shots; after the hand-over A keeps the music,
  its dialogue + effect reach C once each, its loop stop ends the music for C and on A.
- Not verified: the real a30 scripts driving these calls in co-op (the lab host was scripted / direct Note* calls), audible output (log
  counters only), scripted object damage / destroy effects (not on the ring), two real Fusion clients.

## Co-op device controls from followers + press feedback (2026-10-20)
- A follower's control press: K's HaloDevices.Use asks `HaloNetGame.NoteControlUse(c)` first (glue); on a co-op follower it sends the
  press in its own hit ring (victim 61 = HaloNet.ControlVictim, the scenario placement index in the 16-bit effect field) and does nothing
  locally. The host gets HaloNetEventKind.UseControl (Slot = the presser) and runs K's Use(d, "co-op player <slot>") once.
- Device wire index (FIX): device bytes were keyed by the scenario placement index, which counts every object (a30's bridge_control is
  838), so most devices never synced. Now the wire index = the ordinal among the mission's Machine / Control / LightFixture placements
  (HaloNetGame.DeviceWire / DevicePlacement; 256 max, warned beyond).
- Press feedback (K's PressCount / LastPressDenied): a press table in header int 5 bits 16-31 of the 10 header blocks: wire (0-7) |
  count mod 8 (8-10) | denied (11) | used (12); the host fills a slot per pressed control (reused oldest first; more than 10 different
  controls in one write lose the oldest presses' sound). Everybody keeps each control's count; followers add the delta to the device's
  PressCount (K plays on / off / deny); a late joiner's first read takes the counts silently; a promoted host continues them.
- Metric fix: the dropship smoothness stat measures each step against the net-clock gap between draws (a 2 ms Unity frame had ~10 ms
  of net clock and failed the 'jerk' check).
- Verified: harness 101 ok (control press reaches only the host, once, also after a hand-over; press table once / denied / late joiner
  silent / slot reuse / new host continues). Lab net-coop-vehicles passed (31 ok): follower A's bridge_control press goes to host S once
  and is not used locally; S's denied press reaches A once (PressCount +1, denied); after the hand-over follower C's press is applied
  once by host A ("control 'bridge_control' used (co-op player 3) -> 1"), the control moves 0 -> 1 on A and in C's device bytes, C reads
  the accepted press once. Not verified: the deny / on sounds actually heard; scripts reading the activating unit (K's Use has none).

## Fusion 2.1.3 check + MP master leaving (2026-10-21)
- Intended: a running MP match goes on when the master goes to the hub (F's lobby keeps InMatch while others play; the master keeps
  commanding the game - start / seq unchanged, it can rejoin) and when it leaves the room (TakeOverAsMaster: the next master keeps the
  game and lobby phase 3, continues the world blocks it read). Timer = the shared start (each client), scores = player records + kills
  banked for players who left, respawns / objectives local + record events.
- Lab `net` step 7 split: 7 = A to the hub (game goes on for B and C, scoreboard intact); 8 = A leaves the room (_world.Leave): B is the
  master, keeps game 1, writes the world blocks (C still reads the parked vehicle and pickup 3), scoreboard intact. Passed, 45 ok.
- Fusion 2.1: HaloFusionTransport.RequestAuthority logs once when RequestStateAuthority returns false (#if FUSION_2_1_OR_NEWER).
  StateAuthority None after a leave: SceneNetworking's master re-requests None objects (SlowLoop + OnPlayerLeft); my world / co-op /
  stream writers re-ask every 1 s and slot claims ask for their own slot, so nothing waits on the old owner. No PlayerRef.MasterClient use.
- Open: while the master is in the hub it still writes the world blocks but no longer reads the match's pickups / drops (its remotes are
  cleared), so pickup timers / parked vehicles go stale for late joiners until it rejoins or leaves the room.

## World writer while the master is in the hub (2026-10-21)
- HaloNet.WorldWriterId (core, HaloNetWorld.cs StepWriter): the master while it plays the running game (or nobody plays it yet), else the
  lowest-id player whose record plays it. All clients compute it from the same records; a client stops writing at once and starts only
  after being the pick for 0.5 s (WriterSettle) -> never two writers (harness: most at once 1). The new writer continues the blocks it
  read (HaloNetWorld.SetGame copies _read). HaloNet.WorldOwner / HaloNetGame.WorldOwner = that, or the master offline / without a game;
  V (parked / respawn of empty vehicles) and M (objectives) use it (REQUESTS).
- TakeOverAsMaster: a new master sitting in the hub keeps the old master's running game commanded (lobby phase 3) - before it dropped it.
- Scoreboard fix: kills / score kept for a player who leaves are the last values seen while it was IN the match (its leaving record may
  already be reset), and a player who rejoins with fresh counters keeps its earlier kills banked for its side (added to on its next leave);
  a record that comes back carrying them un-banks as before.
- Co-op decision: the co-op host stays the master (F: the master's LEAVE MISSION ends the mission for everybody, so the host never sits in
  the hub mid-mission; moving scripts / AI there and back would need two full host-state hand-overs). The world-writer rule is general and
  picks the master in co-op too.
- Verified: harness 111 ok (writer hand-over to B, late joiner D reads B's vehicle + pickup, master rejoins and takes it back continuing
  B's world, never two writers, master leaves the room from the hub -> B master in the hub keeps the game, C writes; score kept across
  rejoin). Lab net passed (49 ok): A to the hub -> B writes; B parks vehicle 0 (flags 3, health 0.6) and takes pickup 7; late joiner D reads
  both; A rejoins (HaloLobby.Join) -> A writes again, vehicle 0 where B parked it (0.00 m), pickup 7 kept; A leaves the room -> B master +
  writer, C reads vehicle 0 and pickups 3 + 7, scoreboard intact.

## Co-op body states (coop-body fixer, 2026-11-04)
- Actor unit records carry the body state in bits the position / velocity left free (record size unchanged): a2 bit 31 = camouflage hides
  the unit now (HaloUnit.CamoHidden); a5 bits 30-31 = body state (0 none, 1 feigning death, 2 latched, 3 leaping; HaloUnit.NetBodyState);
  while feigning a5 bits 0-29 carry the held death clip instead of the velocity: hard (0) | direction (1-2) | damage region (3-7).
  Followers: HaloNetPuppet.Take -> HaloUnit.ApplyNetBody (camo renderers, Animator.Feign / Resurrect, melee-continuous gesture, leap-start +
  leap-airborne); the jackal's shield region follows the replicated Shield in HaloUnit.TickPuppet. Shield / Body changes alone now count as
  a new record in Take (before, a shield-only change was dropped until something else moved).
- Verified: harness net 116 ok (round trip of each state, the feign clip, full velocity range with states 2/3, the camo bit at -10000 m);
  lab net-coop-vehicles passed (all 6 body-state checks; pictures netbody_states.png, netbody_jackal_shield_off.png looked at).

## 2026-11-10 accepted by lead: seated guns (from V)
- HaloRemoteView.SeatedGuns(heldValid, pos, rot, avatar) and PoseStowedOn, and Editor/HaloLabNet.cs section 1b (a remote player seated with a
  gun, record head 9 m away) were added by V's seats fixer and are ACCEPTED BY LEAD as N's code - keep them when editing these files.
- Robustness (round 2): UpdateGuns draws seated guns only if SeatedGuns placed them this frame or the last (`_seatPosedFrame`); SeatedGuns
  re-activates what it places; HaloSeatPoses calls SeatedGuns(false, .., null) (= hide) on every early return of its pose (no vehicle /
  seat marker, avatar not found, seat label not in the cyborg graph). With no cyborg loaded nothing calls it and the frame guard hides them.

## 2026-11-10 remote vent (attachments fixer, lead-authorised edit of HaloRemoteView)
- AnimateHeld: while st.Overheated, HaloGunFx.Vent(held root, def, true) loops the weapon's overheated effect; off when the bit clears,
  the gun leaves the hand or is hidden; Retire calls HaloGunFx.Forget per gun. Counter HaloRemoteView.VentsShown. Lab net section 2c
  (B's plasma rifle overheated: 1 vent, stops on clear; net_pr_vent.png shows cyan steam at the vent) - net passed 56 ok.
- Open: remote charge glow needs o.TriggerHeld published (FlagTrigger bit 14 exists, never set) - see REQUESTS.md.

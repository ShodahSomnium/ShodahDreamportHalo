# N - Net

Goal: several whitelisted players in the same Halo match in one Somnium instance. Port the Black Ops 2 Dreamport's slot layer
(`...SomniumBO2/Game/Net/`: Bo2Net, Bo2NetWire (64-int record layout in its header), Bo2SlotClaim, Bo2NetTransport, Bo2FusionTransport,
Bo2NetTimeline (interpolation 0.16 s behind), Bo2NetGame, Bo2Instance, Bo2Net.Master, Bo2Net.Lobby, Bo2Match.Net, Bo2RemotePlayer,
Bo2RemoteGun, Bo2RemoteArms, Bo2NetBody, SceneNetworking (already copied: Game/Net/SceneNetworking.cs)). BO2's README "Multiplayer"
section explains the design: worlds cannot create networked objects at run time, so 50 scene slots "Halo Net Player 0..49"
(NetworkObject + NetworkBridgeData64) exist in Assets/Halo Scene.unity; each client claims one (RequestStateAuthority + claim record),
writes only its own ~10x/s, reads the others; a player is tied to their avatar by the slot owner's PlayerId == ISomniumPlayer
Properties.PhotonId. Shooter-authoritative hits, victim applies once, victim announces death with the killer.
Run the server API guard often: the upload server's Fusion is OLDER (no GetMasterClient; use what BO2 used - known_good.txt).

Build (Game/Net) for Halo:
1. The slot layer + wire record for Halo: avatar root/head/hands poses, held weapon (tag index in the current map) + stowed weapons,
   weapon state (firing counter + shot data so remote shots/tracers/sounds/effects replay via HaloFx/HaloAudio), melee swing counter,
   grenade throw counter (+ type, origin, velocity), hits ring (last N hits with victim, amount, cause, flags), death event (+ killer,
   cause), health/shield (for remote shield flare), camo flag, team, kills/deaths/score, the map + match start, vehicle seat (vehicle id
   + seat index) and, when driving, the vehicle pose/velocity/turret aim (or a separate block - design with V), CTF flag carried,
   oddball carried. Keep it within the 64 ints (pack bits) or justify more objects.
2. World-object state (owned by the instance master unless held/driven): weapon/powerup spawn timers taken/respawned, unoccupied
   vehicles, CTF flags at home/dropped/carried, oddball, hill index. Options: the master's own record carries it, or extra scene slots
   (you may append scene YAML for e.g. "Halo Net Object 0..31" with NetworkBridgeData16 - ask the lead in REQUESTS.md first and copy the
   BO2/ETS approach (ETS added "Ets Net Traffic 0..3" blocks; BO2 `scene/make_bo2_net_yaml.sh` in its delivery folder)).
3. Remote players (Game/Net): hit boxes on their avatar bones (layer HaloLayers.Hitbox, IHaloDamageable -> sends the hit), the
   weapon in their hands / on their back (HaloModelBuilder), muzzle flash/sound/projectile replay, grenades, melee, deaths (BO2 dropped
   weapons), camo (their avatar semi-invisible), names over heads (HaloTags already ported in Game/Access), team colour.
4. Instance master + lobby sync for the Front module (who is master, master name, players list with team, the master's HaloRules
   published, START with a shared UTC start, late join = pulled into the running match): expose a clear static API (e.g.
   `HaloNetGame.IsMaster`, `MasterName`, `Players`, `PublishRules`, `MasterRules`, `MatchRunning`, `RequestTeam`) and document it in NOTES.
   The isolation already calls `HaloNetGame.TryHasRecord(photonId, out known)` (keep it).
5. The HUD's motion tracker and scoreboard need the remote players' positions/teams/moving/firing/kills: expose it.
6. Implement `Game/HaloGame.Net.cs` (partial void NetStart/NetTick/NetStop).
Lab: port BO2's loopback lab (`Editor/Bo2LabNet.cs`: a scripted second player over an in-memory loopback of the slot objects) to
`Editor/HaloLabNet.cs`; a Unity-free wire round-trip test in the harness if useful.

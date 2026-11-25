# V - Vehicles

Goal: Halo's multiplayer vehicles working in Somnium: Warthog (driver, passenger, gunner on the chain gun), Rocket Warthog, Ghost,
Scorpion (driver + riders on the hull), Banshee, Covenant gun turret (shade). User's words: "vehicles should work, check main build,
do 3rd person the same way, look at euro truck simulator for the passenger logic, do exactly the same."
- **Check the Somnium client's own vehicle system** in `E:\Somnium Space\Main Build\Assets\_SomniumSpace\VehicleSystem` (Vehicle.cs,
  Controllers, Network, Parts, VehicleEntryType), its inputs (`Inputs/VR/XRI Vehicle inputs.inputactions`, `Input/Configs/
  VehicleInputConfig.asset`) and its third-person mode (`Player/Scripts/Interaction/ThirdPersoneMode`, `Inputs/VR/XRI ThirdPersone
  Mode.inputactions`): how it seats the avatar, drives, and shows the driver in THIRD PERSON. Do the vehicle third person the same way
  (Halo drives in third person) - in VR too if the client does it (comfort: heading only, no pitch/roll of the view).
- **Passengers exactly like the Euro Truck Simulator Dreamport**: `...SomniumEuroTruck/EtsRiders.cs` (riders follow the vehicle frame
  each frame: EarlyPass/LatePass, LocalPlatformFrame, the client's MobilePlatform/elevator sync), `EtsTruckSeat.cs` (Bridge body parked
  at a seat, forced sitting pose with Bridge Poses.SetPose every seated frame, seat rotation = heading only), `EtsAvatarSync.cs`
  (own head hidden while seated, LateUpdate avatar carry), `EtsAvatarCloth.cs` (cloth/dynamic bones anchored to the vehicle),
  `EtsTruckDoor.cs` (click/point + trigger to enter: layer 16 + XRSimpleInteractable + IPointerClickHandler), `EtsNetVehicle.cs`
  (driver-authoritative pose sync, puppets elsewhere). Read the ETS memory facts: the avatar capsule (layer 2) is solid against a dynamic
  vehicle even "collision disabled" -> vehicle colliders exclude layers 2|7|24 while seated; a parked vehicle kinematic.

Data (Data/Vehicles): unit (struct Unit: seats block - label, marker name, flags (driver, gunner, ...), enter marker, enter radius,
camera marker/track, weapons/turret), vehicle (struct Vehicle: type (human jeep, human tank, human plane, alien scout, alien fighter,
turret), speeds, acceleration, turn rates, suspension, mass, flags), physics (phys: mass points - powered, steerable, friction,
radius - Halo's own vehicle physics), collision model (coll: body/shield vitality for damage, materials), model animations for steering,
suspension, turret aim (antr - the Desk module writes the Halo animation reader in Data/Anims; coordinate, or turn nodes directly by
name: "frame steering wheel", "frame left front tire", "frame gun"...: see the warthog's nodes and markers via
`dotnet run -- models bloodgulch "warthog\warthog"`).

Game (Game/Vehicles):
1. Spawn the scenario's vehicles for the rules (HaloScenario.Vehicles: MpSpawnFlags bits = default/allowed per game type; HaloRules.Vehicles
   set), respawn timers (Halo respawns abandoned/destroyed vehicles), implement the HaloVehicles stub.
2. Physics in Unity (Rigidbody + raycast wheels for wheeled/treads tuned from the phys/vehi tags; hover for ghost; flight for banshee
   (Halo's banshee hover/boost/flip); turret stationary aim). Collisions with world and players; splatter: hitting a player above a speed
   kills (HaloHit.Instant via the player's IHaloDamageable).
3. Enter/exit: near a seat's enter marker within its enter radius - VR: grip/trigger on the vehicle there (or a button prompt); monitor:
   E (keep the client's clashing keys in mind, BO2 notes). Seat poses (forced sit), the driver steers (VR: thumbstick or Halo's "drive
   where you look" - pick one, explain in NOTES, offer the other as a setting), the gunner aims the warthog chain gun by holding its
   grips ("gunner left/right hand" markers exist!) and fires with the trigger; ghost/banshee/scorpion weapons fire on trigger (vehicle
   weapon tags with their projectiles: reuse the Combat module's projectile code - coordinate via REQUESTS.md, or call a small API they
   expose). HaloLocal.Vehicle/Seat/Driving kept current.
4. Damage + destruction (vehicle vitality from its collision model, explosion effect, burning wreck, respawn), IHaloDamageable.
5. Net: the driver is authoritative for the vehicle pose; passengers ride locally with the ETS rider technique. The Net module owns the
   slots: tell it (REQUESTS.md) what a vehicle needs on the wire (vehicle id, seat, pose 10 Hz, velocity, turret aim, health) and use
   its API; unoccupied vehicles belong to the instance master.
Lab: `Editor/HaloLabVehicles.cs` - spawn a warthog on a flat floor (or Blood Gulch once the world builds), drive it with simulated
input, pictures of the warthog with wheels in place, seat a dummy, check the turret grips.

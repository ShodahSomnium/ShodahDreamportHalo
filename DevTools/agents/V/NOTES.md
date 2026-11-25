# V notes (Vehicles)

Status: complete; offline verified in the lab (vehicles: passed, 2026-10-19). Net link coded against N's live API (HaloNetGame.World) - NOT yet seen with two clients.

## Files
- `Data/Vehicles/HaloVehicleDef.cs` - vehi/unit/object reader: type, speeds (wu/tick), turn, slide, seats (label, marker, flags,
  yaw/pitch rates + ranges, camera marker, camera track, hud), weapons, attachments, physics (mass points, powered points, antigrav,
  inertia), collision model (vitality, regions, materials, bbox), animation graph vehicle block (aim limits, suspension depths per
  mass point), camera tracks (trak control points). Weapons / projectiles / damage use Combat's Data/Combat readers.
- `Game/Vehicles/HaloVehicles.cs` - the stub facade: Spawn(rules) (spawn flags x vehicle set), ExitSeat, Clear, + All, Find(id),
  IsVehicleWeapon, ReplayShot (for Net), RespawnAbandoned, AddLab.
- `HaloVehicle.cs` (+ `.Drive.cs`) - one vehicle: model, Rigidbody, contact spheres = Halo mass points, coll BSP hit meshes per node
  (non-convex, touch nothing), seats, wheels (raycast suspension from the antr suspension depths), antigrav points, node animation
  (steering wheel, front hubs, tyre spin, suspension travel, gun yaw/pitch, spinning barrels), flip, respawn, IHaloDamageable.
  Driving: jeep (wheel grip + drive force, steer to view or stick), tank (treads, yaw servo), scout (antigrav hover, heading servo,
  strafe), fighter (weightless while powered, flies toward the view), turret (stationary). Splatter by overlap on the Hitbox layer.
- `HaloVehicleGun.cs` - vehicle weapon (rate ramp, magazine + chamber time, error cone, alternating muzzles, firing effect,
  HaloEvents.RaiseFired) + `HaloVehicleShots` fallback projectiles (until Combat exposes FireProjectile).
- `HaloVehicleSeat.cs` - the local seat (ETS EtsTruckSeat port) + `HaloAvatarHide`.
- `HaloVehicleCamera.cs` - monitor camera. `HaloVehicleInput.cs` - controls. `HaloVehicleEntry.cs` - getting in.
- `HaloVehicleRiders.cs` - remote riders on my drawn vehicle (ETS rider technique). `HaloVehicleNet.cs` - wire adapter (offline now).
- `HaloVehicleDirector.cs` - frame order (-90 early, 10000 late). `Game/Shared/HaloLocal.Vehicles.cs`. `Editor/HaloLabVehicles.cs`.

## Facts from the tags (Blood Gulch, harness `vehi` in DevTools/agents/V/harness)
- Halo CE MP vehicles are INVULNERABLE: every MP coll has body vitality 0 (cyborg reads 75/75 with the same reader). So no vehicle
  destruction; "damage" = push + rider damage fraction (0.18 warthog) passed to a local rider. Abandoned vehicles respawn (30 s, ours).
- Speeds are wu/tick: warthog 0.275 = 25.1 m/s, accel 0.0028 = 7.7 m/s^2; ghost 0.225 = 20.6 m/s; banshee 0.22; scorpion 0.14.
- Seat entry: CE has no enter radius - the model has "<seat marker> enter" markers ("driver enter", "passenger lf enter").
- Warthog gunner seat has "gunner left/right hand" grips on "frame barrels" and foot markers (standing seat).
- Camera tracks: warthog_driver_loose middle point (-2.75, 0, 0.68) wu = 8.4 m behind, 2.1 m up (client's own view: 7.3 / 1.74).
- MP spawn flags: bits 0-3 default in slayer/ctf/king/oddball, bits 8-11 allowed. Blood Gulch slayer default = 2 warthogs + 2 rocket warthogs.

## How the Somnium client does vehicles (studied E:\Somnium Space\Main Build; what we copied)
- Desktop: the player's own Cinemachine FreeLook follows the body moved to the vehicle's bounds centre; view preset 0 ~7.3 m back,
  1.74 m up; collides with layers 14/16/23 only. We: hold the brain off (EtsTruckCamera way) and place the camera ourselves with Halo's
  camera track around the vehicle centre (or the seat's camera marker), mouse orbit always (Halo), collision 0/14/16/23, radius 0.1.
- VR: the client goes THIRD PERSON while driving: the HMD camera is re-parented under a rig at the vehicle - heading only, Lerp 2*dt
  toward the vehicle heading (reversing: looks back), 4 m up, 6 + speed/10 (max 10) m behind; left Y switches first/third person;
  the avatar is IK-retargeted into the seat. A world cannot re-parent the client's HMD camera or retarget VRIK, so we move the XR
  rig so the head lands at that chase point (ETS PlaceHead, lean kept), hide our own avatar on our screen (hips bone scale, not
  networked) and every other client draws our body in the seat (HaloVehicleRiders: head forced to the seat eye when far from it).
  Heading only - no pitch/roll (comfort). Setting `vehicles.vrthird` (1 default) keeps the Y toggle.
- VR bindings copied from XRI Vehicle inputs: right trigger accelerate, left trigger brake/reverse, left stick X steer, right B exit,
  left Y camera switch. For drivers who also shoot (ghost/banshee/scorpion/turret): left stick drives, right stick X turns, right
  trigger fires where the right hand points, left trigger = secondary.
- VR steering choice: STICK by default (head stays free to look around); Halo's "drive where you look" (head heading) is
  `vehicles.vrsteer = 1`. Banshee always flies toward the head's gaze when the right stick is centred.
- Monitor = Halo PC: W/S throttle, A/D turn (warthog/scorpion) or strafe (ghost/banshee), mouse looks and the vehicle steers toward
  the view, LMB/RMB triggers, Space brake, E in/out. VR in: grip/trigger on the vehicle (ETS door handle: layer 16 box +
  XRSimpleInteractable + IPointerClickHandler) or A.

## Passengers (ETS logic)
- Seat: Bridge motion/collision/gravity/jump/teleport off, falling anim + VRIK legs off, Poses.SetPose(SittingStraight) every frame
  (re-entered when the client drops it; UndoPoseRecentre in VR), capsule excludes layer 28, vehicle shapes exclude 2|7|24 while
  anyone local sits in it; parked vehicles kinematic (settle 3 s dynamic after spawn/release with avatar layers excluded).
- Seat written early (Update order -90, before the client's order-0 avatar placement) and again late (LateUpdate 10000).
- Remote riders: ETS EtsRiders Correct() on Fusion Body/Root, Head, LeftHand, RightHand (found by StateAuthority.PlayerId) + Bridge
  transforms, early and late; IK legs + falling anim off for them.

## Net (coded, not verified with 2 clients)
- HaloVehicleNet: occupants from HaloNetGame.Players (VehicleIndex/Seat/Aim/PlayerId), remote driver -> TryDriven (kinematic), master
  PublishParked / others TryParked, local driver PublishDriving + PublishAim; seat clash: lower slot keeps it. Net reads HaloLocal.VehicleId/
  SeatIndex/Driving. Remote vehicle-gun shots: V listens to HaloEvents.Fired (who != me) and replays (Net skips its own replay for them).
- Guns fire through HaloCombat.FireProjectile (V's HaloVehicleShots only when Combat has no catalog). Lights/engine/contrails via HaloFx.Attach.

## Open
- Two-client test of the net link (riders, remote driver, parked sync, seat clash). VR: none of the headset paths (third-person rig, hide,
  grips, stick driving) can run in the lab - first proof will be a client run (Player.log lines below).
- Monitor: client camera brain hold + cursor lock not seen in the client yet.
- Banshee tricks: CE has none (the tag's "flipping angular velocity" is for overturned vehicles; we flip those on E).
- Desk: own-head hiding for first-person seats on a monitor is left to D (scorpion riders are the only first-person seats).

## Verified (lab "vehicles", Blood Gulch tags, flat floor, pictures looked at in DevTools/lab/vehicles/)
- All 6 MP vehicles build (models, seats, markers, grips, camera tracks, contact spheres, 3-16 coll hit meshes) and settle upright
  (banshee rests on wingtips+nose, 22 deg). Warthog side picture: tyres on the floor, hull at Halo's height (suspension = antr depths).
- A bullet from above / the side hits the coll BSP hit mesh ("hit frame hull"), not the contact spheres (layer 31).
- Warthog: top 24.8 m/s (tag 25.1), 0-20 in 3.8 s, 37 deg in 1.5 s at full steer, steers toward the view, brakes to 0; splatters a
  hit-box dummy at 22 m/s (Instant). Exit -> parked kinematic. Gunner: chain gun turns to yaw -90 (picture), 17 rounds in 1.5 s
  (8->15/s ramp) through Combat's projectiles. Ghost: hovers 0.57 m level, 20.5 m/s (tag 20.6), 6 bolts/s. Banshee: climbs 20 m in
  4 s toward the aim, holds height with no throttle. Scorpion: 12.8 m/s (tag 12.8), turret aims, one shell per pull (picture with smoke).
- Chase pictures from Halo's camera tracks (warthog_chase_*, ghost_chase_firing, banshee_flying, scorpion_cannon).

## Player.log lines
`Vehicles: placed N of M scenario vehicles for <type> (vehicle set k): ...`, `Vehicles: net online|not up ...`, `Vehicles: prompt ...`,
`Vehicles: seated in <vehicle> seat i 'label' (driver/gunner/rider, sitting/standing, headset/monitor, third/first person)`,
`Vehicles: seat pose SittingStraight entered ...`, `Vehicles: VR seat - the camera rides on the body ...`, `Vehicles: own avatar hidden ...`,
`Vehicles: monitor vehicle camera on (Halo chase track ...); ... CinemachineBrain disabled`, `Vehicles: <v> powered up: n looping sound(s)...`,
`Vehicles: vehicle guns fire the Combat module's projectiles`, `Vehicles: got out of ...`, `Vehicles: rider <id> in <v> seat s: network body bound`,
`Vehicles: <v> is driven by another player`, `Vehicles: <v> hit a player at x m/s (splatter)`, `Vehicles: <v> respawned at its scenario spot.`

## Seat animations (2026-10-19, after the first client test: "put avatar in vehicle when entering, same animations as real Halo")
- `Game/Vehicles/HaloSeatPoses.cs`: `HaloSeatPuppet` samples the cyborg's own seat clips (antr unit seat of the seat label: enter [7],
  exit [8], class idle [0] + aim-still overlay [25] on the class's yaw/pitch grid, acc-front-back / acc-left-right overlays [2][3]) into
  Desk's HaloPuppetSample seat joints; `HaloSeatPoses` (LateUpdate 9997) lays them on every seated avatar (own + remote riders) with Desk's
  HaloPuppetPoser.ApplySeated + HaloAvatarCopies (mirror / shadow copies). Enter on sitting down, exit clip on getting out (own and remote).
  Warthog driver: hands on the wheel grips (seat IK), body into the steering; gunner: on the turning mount, aim-still pitch = gun pitch,
  hands on "gunner left/right hand"; passenger: aim-still from where the head looks.
- Why the client log said "hips False, arms False": Desk's seated path passes hips/arms = monitor only (VR got legs only). Now VR =
  hips + legs + torso from the animation, arms IK'd to the tracked controllers (hand rotation kept from the controllers); a VR driver in
  the third-person view gets the whole body on the seat with hands on the wheel (the own avatar is no longer hidden there).
- VR comfort: the view CUTS (teleport) into the seat and out to the exit point; only the body plays enter / exit. The exit point is where
  Halo's exit clip ends (HaloSeatPoses.ExitPoint), so the body lands on the player's real spot.
- Desk held off while V poses: HaloDesk.PoseSeated = false, PoseOwn = false while the own avatar is V's. Asked D (REQUESTS.md) for one line:
  Remote(): `if (HaloSeatPoses.OwnsSlot(r.Slot)) { Drop(e); return; }` - until then a remote MONITOR rider is posed twice.
- Lab "vehicles-seats" (passed, pictures looked at): enter_* (driver climbs in from the side), warthog_seated_* (driver on the wheel,
  passenger, gunner on the grips), warthog_steer_gun (gunner turned with the gun), ghost / scorpion (driver in the hatch, rider on the
  fender) / banshee (prone pilot) seated, exit_* (all seven play their exit clips). Hips land 0.00-0.07 m from the seat markers.

## Iteration-2 VR fixes (2026-10-19, user test on Death Island)
- Tablet: while seated in a headset and 1 s after getting out, V calls Combat's HaloTabletGuard.Update(true) (the client's B / Y tablet
  actions off; B gets out, Y switches the view). `HaloVehicleDirector.TabletHeld`.
- Head: V never scales a head. The client shrinks the own head for first person; in the VR third-person view V gives the own main copy's
  head its size back on this screen only and puts the client's scale back for first person / on getting out (`HaloSeatPoses.OwnHead`).
- Gunner: either trigger fires in the gunner seat, on the grips or not; on the grips the gun turns so the grips follow the hands, off them
  it turns to where the firing hand points. First shot per seat/trigger is logged ("Vehicles: first shot from ...").
- Seat choice: seats flagged "not valid without driver" (the scorpion's 4 riders) are only offered once somebody drives; the driver seat
  is also reachable from beside the hull (counted 1 m from its door). The prompt and the press use the same choice (`HaloVehicleEntry.Choose`).
- Ghost / banshee guns are fixed: aim yaw / pitch 0, bolts along the hull (VR and monitor). Scorpion: left stick drives / turns, cannon
  turret follows the head, right trigger = cannon (one shell per pull, 4 s chamber), left trigger = machine gun.
- Lab "vehicles-vr" (passed): simulated Touch controllers through HaloXr.Simulated + stand-in hands/head (HaloVehicleInput.LabVr).

## Campaign (phase 2)
- `HaloVehicles.SpawnPlacement(...)` (2 forms) + `Remove(v)`; `HaloVehicle.Campaign.cs`: Campaign flag (no MP respawn), ObjectName (HaloObjects),
  Vitality from the coll's body (campaign banshee 100, ghost 160; MP tags 0), Destroyed (effect: coll destroyed / depleted / region / vehicle
  effect; riders out; not enterable), units in seats (UnitIn / SeatUnit / UnseatUnit; NoteUnits marks seats with a HaloUnit child taken),
  AI surface SetAiInput / SteerToward / AiDriven (AiStep in the director), Hover(bool), Open(bool) (logs), EnterableByPlayer.
- Vehicle contact spheres now always exclude the Hitbox layer (24): a rider's kinematic hit boxes under the seat marker pinned the hull.
- Lab "vehicles-campaign" (a30, passed): 'jeep' placed with full rotation (roll 9.9) and registered; marine gunner fires the chain gun at
  its aim point (picture campaign_ai_gunner_firing), marine driver drives 18.5 m in 3 s, banshee destroyed by 120 of 100 points.
- unit_open / unit_close (2026-10-19): `HaloVehicle.Open(bool)` plays the vehicle's own graph's unit seat "stand" slot 27 "opening" / 28
  "closing" (none: opening backwards) on the model nodes over the clip's length, held at the end; `IsOpen`, `HasHatch`. A vehicle with a
  hatch lets the local player out only while open; Action (E / VR A or B) gets out. a10 cryotube: 'stand opening' 154 frames (5.13 s),
  door swings 90 deg (lab vehicles-cryo, passed, pictures cryo_closed / cryo_opening / cryo_open). Dropship: opening / closing 14 frames.
  Pelicans have no opening animation in their graph (logged once). Vehicles without a physics tag (cryotube) are never settled.
- a30 dropship / banshee (2026-10-19, lab vehicles-dropship passed): lz_cship placed by name, teleported to lz_cship_flag, 4 grunts in the
  CD-passenger seats, unit_close, recording_play_and_hover lz_cship_enter (Q's kinematic path: 1037 ticks, flew 199 m) then HOVERS (0.00 m in
  2 s; human-plane hulls never settle / drop, Hover(false) keeps them kinematic), engine loop runs while a recording plays, unit_open plays
  'stand opening' (14 frames), UnseatUnit drops each grunt under its side pod - they fall and land (4/4). Banshee: SetAiFlight (3D), banking
  of the model into turns (the rider's view stays level), CE has no boost / tricks (the tag has none).
- SFX (2026-10-20, lab vehicles-sfx passed): engine loops (HaloFx.Attach sounds, scale = speed / tag top speed) on every vehicle incl. AI-driven,
  recording-flown and co-op followers (Velocity = measured transform motion when kinematic without a net velocity); tyre slip (HaloImpacts
  TireSlip, 8 in a hard warthog turn), crash (vehi crash sound + vehicle.foot effect 10), suspension slam (vehi suspension sound). Banshee
  dive whoosh = its banshee_contrail.lsnd attachment scaled by speed. Vehicle explosion = the coll / region destroyed effect (its sounds). No
  horn: Halo CE vehicles have none (no tag field).

## Workstream 4 (2026-11-04): recorded vehicle flight and vehicle cargo
- `HaloVehicle : IHaloRecordedControl` (HaloVehicle.Campaign.cs): HaloCinematic now hands every vehicle recording to the vehicle (Custom),
  the kinematic fallback StepKinematic no longer drives vehicles. Per tick (`RecordedControl`): human planes (c_dropship, pelican) keep a
  LEVEL heading (yaw = recorded facing, held while LookDontTurn 0x100; look/facing pitch never tilts the hull); forward speed toward
  throttle x max forward (max reverse backwards) at speed acceleration speeding up / speed deceleration slowing down (0.03 / 0.01
  wu/tick^2); throttle-left = its own slide speed (max left/right slide, slide accel/decel); Jump (0x2) climbs, Crouch (0x1) descends at
  max left/right slide 0.1 wu/tick with slide accel 0.01 ("// ours, calibrated by Q/carrier M4"). Alien fighters fly along the 3D facing;
  ground kinds follow the floor. Every move is swept (SphereCast of the contact spheres = Halo mass points, plus a carried vehicle's
  spheres and wheels, CheckSphere broad phase) against GroundMask: a descent stops on the ground, a horizontal move into terrain slides.
  Per frame (LateUpdate `RecordedFrame`) the pose is interpolated between the last two tick poses (HaloCinematic.TickFraction); the body
  is kinematic with interpolation None while a recording drives it. `RecordedControlEnd(hover)`: hover holds it kinematic; otherwise
  planes / fighters stay where they are (Park(false)), ground vehicles settle (Park(true)). Log per recording:
  "Vehicles: <name> recording '<rec>' ends ... X wu (Y m) over the ground, ground contact from tick T (n landing ticks, m terrain-contact
  ticks), max tilt 0 deg." `LastRecording` (RecordingStats) and `RecordingDrives` are public for the lab.
- StepKinematic (puppets / fallback): separate accel / decel, lateral from the slide fields, 3D pitch flight only for AlienFighter (human
  planes hold their height there).
- Cargo: `SeatVehicle(seat, cargo)` parents the cargo root on the seat marker (localRotation (0,90,0) like units), kinematic,
  interpolation None, not enterable, contact spheres ignore the carrier's (permanently, ours), seat Occupant = `CargoOccupant`, seat
  `SeatInfo.Cargo`, `cargo.CarriedBy`. `UnseatVehicle(cargo)` unparents at the current pose, restores enterable / interpolation,
  Park(true) (drops, settles) with the carrier's velocity. A carried vehicle is never AI-driven / TakeControl'd / settled. A destroyed
  carrier releases its cargo. `CargoIn(seat)`.
- Script (HaloScriptFns.Units/Characters): unit_enter_vehicle / vehicle_load_magic with a VEHICLE handle seat it as cargo (seat label or
  marker match); unit_exit_vehicle on a carried vehicle releases it; vehicle_unload releases matching cargo seats too; a handle that is
  neither unit nor vehicle logs once. Riders of a carried vehicle no longer count as riders of the carrier (UnitIn / UnseatUnit use the
  nearest HaloVehicle parent).
- `SeatDef.AllowsWeapons` (0x20) already existed on HaloVehicleDef.Seat (read by workstream 6).
- Probe DevTools/agents/Q/carrier: new model M6 = this implementation (BSP stops only descents; horizontal entries into Leaf -1 are
  counted, not stopped, because a point Leaf test cannot tell terrain from the void outside the BSP). Results in
  Q/carrier/results_M0_M4_M6.txt (MODELS=0,4,6 for a30 b30 a50 b40). End heights over the BSP (M0 -> M6):
  a30 lz_cship_landing_drop 25.5 wu -> 0.1 wu (landed, 41 landing ticks); rubble_rock drop IN-SOLID -> 0.1; rubble_middle drop -> 0.1;
  river_boat drop -> 0.1; rubble_boat drop 0.2; foehammer_river_in -> 0.1; b30 extraction_pelican_2 -> 0.1; fort_cship_drop 11.9 wu,
  pipe_cship_drop 27.6 wu (as M4: these drops hold Crouch too briefly to reach the floor; the vertical rate stays "ours" until a retail
  comparison). The lab (Unity colliders, hull spheres) is the real check.
- Lab: Editor/HaloLabCarrier.cs - "carrier" (a30 BSP built, lz_cship chain: drop end <= 3 wu over the floor, grunts within 1 m of the
  ground, exit 0 terrain-contact ticks, hull tilt 0, deletion) and "carrier-pelican" (foehammer_first + jeep cargo: the jeep stays on
  the cargo marker, hull level, released jeep lands on its wheels). NOT RUN YET (verification phase).
- 2026-11-04 WS4 verify: human-plane Jump/Crouch rate is now max forward speed (0.5 wu/tick dropship) with slide accel 0.01 (ours; probe
  Q/carrier M6 VFWD=1: every a30/b30/c10 drop lands, incl. fort_cship_drop / pipe_cship_drop which ended 12 / 28 wu high at 0.1).
  No single analytic model fits all retail recordings (M7 = 3D-facing climb fixes fort/pipe but sinks rubble/river enters into the
  ground and leaves lz 15 wu high; see Q/carrier models 7-9, PK/POWONLY/VSCALE/VFWD env). A human plane meeting terrain sideways now
  climbs over it on its recorded track (RecordingStats.Climbs) instead of sliding off it: lab 'carrier' lz_cship_enter used to slide off
  the ridge at tick 231 and end 80 wu off course over the void (drop 128 wu high); now enter ends at the LZ, drop ends 0.3 wu over the
  floor, grunts on the grass. Settle: a released cargo stays dynamic until it rests (was frozen mid-tumble at 3 s) - not lab-verified yet.

## Recorded human-plane flight model (rounds 5-6, 2026-11-04) - supersedes the per-tick rules above for c_dropship / pelican
- One shared model, `HaloPlaneFlight` (Data/Vehicles/HaloVehicleDef.cs), state `HaloPlaneState` (Halo space, wu / ticks / radians); run
  by HaloVehicle.PlaneTick (HaloVehicle.Campaign.cs, swept against the BSP, contact velocity losses fed back), HaloCinematic's fallback
  mover and the offline probe (DevTools/agents/Q/carrier `fly eval`, VEL=game) - bit for bit the same code. Per 30 Hz tick:
  1. heading: the hull turns 0.04 of the remaining angle toward the recorded facing yaw (held while LookDontTurn 0x100); pitch never
     tilts it or moves it up / down;
  2. control speeds (tag): forward speed ramps toward throttle x max forward (reverse) at speed acceleration 0.03 while speeding up,
     speed deceleration 0.01 otherwise; slide likewise with the slide fields; Jump / Crouch ask max forward speed (0.5) up / down;
  3. hull velocity closes 0.05 of the gap to that target velocity, then loses the physics tag's air friction (0.02) on every axis ->
     steady flight 0.72 x max speed, ~14 ticks to follow a change.
  Tag values for c_dropship and pelican are identical (vehicle: 0.5 / 0.1 / 0.03 / 0.01, slides 0.1 / 0.01, turn rate 180; physics: mass
  10000, air friction 0.02, gravity 1, no powered mass points); they differ only in centre of mass, moments / inertia product and mass
  points (11 vs 19), which the model does not use (mass points = contact spheres only). 0.04 and 0.05 are ours (measured; retail code
  unreadable: halo.exe is SafeDisc-encrypted). Log once: "Vehicles: recorded human-plane flight (...)".
- Per-recording result (probe VEL=game, a30 / b30 truth; lab numbers equal the probe's):
  lz_cship -> lz_landing 50x34: 10.9 wu from centre INSIDE (lab 13.7) | fort_cship -> first_fort_lz 19x21: 5.7 INSIDE |
  pipe_cship -> first_pipe_lz 18x21: 5.8 INSIDE | back_cship -> first_back_lz 18x31: 5.4 INSIDE | rubble_rock -> 30x32: 2.0 INSIDE |
  rubble_middle -> 15x20: 2.5 INSIDE | rubble_boat -> 20x18: 7.9 INSIDE | river_boat -> river_boat_lz: 48.5 (not its drop volume) |
  foehammer_first jeep -> first_drop 2x3: 10.6 OUT (short along the final approach) | b30 hog_pelican jeep -> jeep_delivery 3x5: 8.1
  OUT (mostly across track) | b30 insertion_pelican_1 / _2 -> designer placement: 3.1 / 8.8.
- The jeep misses are the model's general precision (2..11 wu over 150..300 wu, pelicans and dropships alike) meeting 2x3 / 3x5 volumes,
  not a pelican-specific error. Ruled out (round 6, probe `why` / `stats` / `ground` / `grid`): tag differences (above); LookDontTurn (3
  start ticks in foehammer_first_in, none in hog_pelican_in); large turns (foehammer's miss stays 7..13 wu for any yaw response
  0.02..1, rate-limited or second-order turning); cargo marker (-1.662, 0, -0.428) on the hull root node, identity rotation; release
  timing (unit_exit_vehicle after sleep (recording_time), pelican at ~0 velocity); script moves (only object_teleport to the start flag
  before the recording; a30's flag = the placement); first_drop / jeep_delivery are the drop zones (scripts wait for players to leave
  them before unit_exit_vehicle). No global variant tried (velocity response 0.03..0.12, separate decel / sideways responses, scaled
  tag accel / decel, cos(pitch) horizontal speed) gets both jeeps in without pushing dropships out; foehammer_first alone fits a uniform
  0.92..0.93 speed scale, which hog_pelican / the insertion pelicans (best 1.00..1.01) refute. Lab carrier-pelican therefore still fails
  its first_drop check (honest: the check is unchanged).

## Seated weapons + gunner overlay (2026-11-10, user: "gun displayed outside the car far away", "fix the animation on the gunner")
- Root cause (others' view): HaloRemoteView drew a seated player's guns from the net RECORD body (head/hands/shoulders), which for a monitor
  rider is their chase camera (metres behind the vehicle) and is drawn 0.16 s late while the seat moves; the held gun of a weapon seat was
  never put in the hands at all (seated = not in hand). Fix: HaloRemoteView.UpdateGuns no longer poses guns while seated; HaloSeatPoses
  (9997, after posing the rider) calls HaloRemoteView.SeatedGuns(valid, pos, rot, avatar): the held weapon in the seat animation's right hand
  (seat flag "allows weapons"), the other one on the posed avatar's body (frame from hips / upper arms / neck); seated but not posed: hidden.
- HaloSeatPuppet now fills GunInRGrip / RWristOnGun from the cyborg's "right hand" marker (Desk's HaloPuppet.FillGun) -> ApplySeated's gun
  pose is the weapon origin in the hand. Remote riders get their weapon class (Net InHand + HeldWeapon) when the seat allows weapons.
- Own monitor player: first-person seat (warthog passenger: no third-person flag) keeps Combat's view model; the seated avatar's hands IK
  onto it (right wrist from RWristOnGun, left on "left hand cyborg"). Third-person weapon seat (none in MP): Combat's gun goes in the seat
  hands (HaloSeatPoses.OwnGunPose, read by HaloHandling.SeatGun in Update so the shots leave that muzzle). Headset: tracked hand (unchanged).
- Gunner overlay: aim-still = the gun's aim (AimDirection) in the SEAT MARKER's frame. Was: warthog gunner yaw 0 / pitch AimPitch (same
  numbers now), but the gun turret's marker rides "turret end" (yaw+pitch) and got AimYaw/AimPitch applied a second time (double turn);
  the scorpion driver (hull marker) keeps the full turret yaw. Halo's W-gunner has only "fixed idle" + "fixed aim-still": no fire anim.
- MP hatch: the scorpion's own graph has "stand opening", so HasHatch && !IsOpen blocked every exit from MP scorpions (B / E): the lock now
  needs v.Campaign. (Campaign scorpions are still locked until unit_open - open item.)
- Labs: vehicles-seats PASSED (new: passenger_gun_*, gunner_{level,left_up,right_down}_{quarter,side}, turret_gunner_0/1); net PASSED with
  new section 1b (B seated, record head 9 m away: AR 0.11 m from the posed hand inside the warthog; net_b_seated_gun/wide.png).
  vehicles-vr: the chain gun did not follow lab hands on the grips (yaw 0 -> 0) and later steps failed (scorpion hatch, fixed above, not
  re-run); vr_gunner_body.png shows the headset gunner's body on the mount. desk-monitor: Combat never drew a weapon in the lab (TIMEOUT
  before my section) -> the own monitor passenger is NOT lab-verified.
- vehicles-vr re-run (8 lab runs used): scorpion exit and everything after now pass. The one remaining FAIL ("the chain gun turned to the
  hands on its grips") is a lab artefact found by a frame log: the gun DOES turn (mount 7 -> 15 deg in 12 frames) but the warthog creeps
  backwards ~0.2 m/s with only a gunner aboard while firing (left grip z -2.29 -> -2.54 m), so the lab's world-fixed stand-in hands drop out
  of the 0.35 m grip reach and the gun swings back to the hand aim. In the client the hands ride the rig with the vehicle. Open: why an
  undriven warthog with a gunner creeps (no gun recoil force exists in HaloVehicleGun) - and the lab hands should ride the vehicle.
- 11-10 creep fixed (cause found): with no driver (settle / driver got out / gunner only) the suspension springs push along the hull's up;
  the warthog rests ~2.5 deg tilted (up.y 0.999), so the springs give a steady horizontal push g*sin(tilt) ~0.4 m/s^2, and the no-driver
  contact drag was only viscous (2*m*v) -> terminal creep ~0.2 m/s backwards, never under the 0.1 m/s "resting" bar, so the settle never
  parked it (dynamic up to 11 s; warthogs were "kinematic False" even after the spawn settle). Fix in HaloVehicle.Drive.cs Suspension(grip
  false): static friction at each grounded wheel (cancel 80 % of the wheel's ground-plane velocity a tick, capped at load * tyre grip *
  3.5 like the drive path) = Halo's empty vehicle holds where it stops; airborne / tumbling hulls (no load) still slide. Verified: lab
  vehicles `done (passed)` (warthogs now "kinematic True" after the spawn settle, "the empty warthog is parked (kinematic)" ok); vehicles-vr:
  new check "the undriven warthog stays put under its gunner (no creep)" ok (moved 0.000 m, kinematic) and "the chain gun turned to the
  hands on its grips" now passes. Still FAILS there: "VR gunner ... avatar's hands on the tracked hands" (L 0.13 R 0.18 m vs 0.12) = seat
  pose IK, not the creep.

## 2026-11-10 seated gun robustness (round 2)
- HaloSeatPoses: own gun (GunValid / OwnGunPose / the first-person view gun) only in the Sit phase; r.Weapon cleared when a rider goes to Exit.
- Remote seated guns: RemoteGuns(r, false, null) on every early return of Pose; HaloRemoteView draws seated guns only if SeatedGuns ran
  this frame or the last (covers no cyborg loaded too).
- Own monitor first-person passenger: the body plays Halo's seat clip (W-passenger rifle idle + aim-still: the rifle shouldered, as remote
  players see it). Per camera (beginCameraRendering): the own camera at the seat eye draws Combat's view model with the arms re-reached onto it
  (HaloPuppetPoser.ReachSeatedArms / ArmsSave / ArmsLoad); every other camera draws the gun in the clip's hands. Restored at endContextRendering.
- Headset gunner (seat with grips, not a driver): Reach() moves the seat frame up to 0.35 m (ours) toward the tracked hands by what the arms
  lacked - a short avatar now holds the grips (vehicles-vr: hands 0.00 m, hips 0.27 m from the seat).
- vehicles-vr: the stand-in tracked hands ride the warthog (kept in its frame). Labs passed: desk-monitor, vehicles-vr, vehicles-seats, net.
- Own monitor first-person passenger (user picture: a white piece of the own left arm / shoulder over the lower-left quarter). Cause: the
  camera is the vehicle's "camera rider" marker, but the W-passenger rifle clip leans the body 0.2 m ahead of it (head at depth +0.22,
  left shoulder joint in view), and the left arm was stretched straight to an unreachable "left hand" marker on the unscaled view model;
  the head was never hidden (Desk's HaloHeadHide is off while V poses the own avatar). Fix (HaloSeatPoses.ViewArms / SetViewArms /
  ViewHead / OnBeginCamera): the view model framed like on foot (HaloDesk.FirstPersonPose for the seat eye, shrunk x0.64 toward the eye),
  the body drawn like on foot for the own view (shoulders square to the look yaw, head at the eye + ViewHeadOffset (0,-0.07,-0.10) ours,
  thighs kept in the clip's direction so the knees stay down), elbows hanging (poles ViewLeft/RightElbow, ours; HaloPuppetPoser.ReachArms),
  the left hand on the shrunk marker with the standing slide-back for reach, the head shrunk. Laid on in LateUpdate after the mirror copies
  copied the clip (an avatar is skinned once a frame), given back at endContextRendering / start of the next LateUpdate. The gun stays per
  camera. desk-monitor (run 6 of 6, before the LateUpdate step): own view shows only the view model + hands, 0.0 % white in the lower-left
  quarter, no shoulder/upper-arm/chest/neck/knee joint in view, left hand 0.00 m. The LateUpdate step and the next-frame outside shot are
  compiled, NOT lab-run. Headset passengers are untouched (ViewGun is monitor-only; no camera is moved).

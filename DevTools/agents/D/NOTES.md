# D notes (Desk: monitor play, the avatar holding the weapon, third person, Halo character animations)

## Status
- Data/Anims (Unity-free, verified in my harness DevTools/agents/D/harness):
  - `HaloAnimGraph` (antr: seats/units, weapon classes, weapon types, IK points, nodes, animations; `ClassFor(seat, weaponLabel)`),
    `HaloAnimIds` (the game's enums: Seat / Weapon / WeaponType indexes, checked against the cyborg's names).
  - `HaloAnimation` (frame data: little-endian, per frame per node rot(int16x4)/trans(3 float)/scale(float) by the flag masks, bits above
    the node count ignored; default data the same interleave; stored quaternions are INVERTED -> conjugated on decode; root motion dx dy dz dyaw).
    Blood Gulch cyborg: 19 nodes, 254 animations, 0 compressed, all quaternions unit length.
  - `HaloSkeleton` / `HaloPose` / `HQ` (gbxmodel nodes + markers, local->model, base/replacement/overlay blending, the aim grid).
    Verified: rest pose from the conjugated node quaternions lands on the vertex clusters (harness `bind`); "stand rifle idle" stick figure
    = a standing Spartan holding the rifle level at chest height (harness `stick`); the aim-still grid: overlays apply as q_base * delta,
    columns full right -> full left, rows full down -> full up, the weapon class's own yaw/pitch per frame (90 deg) (harness `grid`).
- Game/Desk:
  - `HaloDesk` API (Active, AimRay, AimPoint, AimHit, ShotDirection, FirstPerson, ThirdPersonWanted ("desk.view"), ViewKey (V),
    HeldWeapon / HoldsWeapon / PlaceHeldInFirstPerson, NoteReload/NoteMelee/NoteThrow + Local* counters, ReportRemote(HaloDeskRemote),
    SeatPoseFor(seat, weapon, out HaloSeatPose), Start/Stop, switches PoseOwn/PoseOthers/PoseSeated/SeatPoseVr/HideHead).
  - `HaloCyborg` (the match map's cyborg_mp biped: skeleton, graph, hand markers, weapons' "left hand cyborg" grips).
  - `HaloPuppet` (hidden cyborg puppet playing Halo's own 3rd-person animations: seat stand/crouch/vehicle, class by weapon label,
    idle/move by speed & direction at the avatar's pace, fire-1 overlay, reload-1 / melee replacement, ready on a switch, throw-grenade on
    the upper body) -> `HaloPuppetSample` (BO2's sample on Halo's skeleton; grips from the biped hands: x = wrist->knuckles, z = little->index).
  - `HaloPuppetPoser` (BO2's Bo2PuppetPoser core: blends, legs/hips standing still, torso/head shares, two-bone arms in the aim frame,
    weapon on the "right hand" marker, aim fix to the crosshair, left hand on the weapon's "left hand cyborg" IK point, fingers;
    `ApplySeated`: hips on the seat marker, legs, torso, arms to the vehicle's wheel/gun grip markers).
  - `HaloDeskPlayer` (one avatar: puppet + poser + Halo IK + seat IK + copies), `HaloDeskBody` (LateUpdate 9996 driver: own avatar,
    reported remotes, seated, copies, head hide; Update: HaloFirstPerson.Tick with ForceFirstPerson from the view setting, V toggles).
  - `HaloAvatarFind`, `HaloAvatarCopies`, `HaloHeadHide` (BO2 ports).
- Lab `desk` PASSED (DevTools/lab/desk/*.png, looked at): RPM stand-in holds the AR (pistol grip in the right hand, left hand on the
  "left hand cyborg" marker, barrel 0.0 deg from the aim) and the pistol two-handed (Halo's own pistol hold); reload (Halo's reload-1,
  left hand off the gun), fire overlay, walk cycle, first person (gun raised low-right into the 40 deg view; left hand 7.5 cm short = reach
  limit), death (s-kill front gut, lying), Warthog driver (hands toward the wheel grips, 10-12 cm short: avatar arms 0.54 m vs cyborg 0.68 m),
  gunner (on the turret handles, 1.6 cm), passenger with the AR. 10/12 within the reach thresholds. `desk-data`: all 254 animations decode;
  harness: all 19 MP maps 0 compressed / 0 failures.
- Also: death fall (unit damage table, HaloDesk.DeathFall), monitor zoom (FOV / HaloLocal.ZoomMagnification while drawing), near plane 8 cm
  in first person, self-start on HaloSession Playing (fallback for Match not calling HaloDesk.Start), Combat's HeldModel fallback.
- NOT verified: anything in the Somnium client (own avatar found by head, first-person forcing / V toggle, head hide, mirror copies, Net's
  remote reports, zoom FOV, near plane). Weapon materials drew magenta in the lab (World's HaloMaterials; the lab paints them plain).

## Contracts (see REQUESTS.md "from D")
- Combat: set `HaloDesk.HeldWeapon` (3rd-person model root), call NoteReload/NoteMelee/NoteThrow; shots along `HaloDesk.ShotDirection`.
  Desk calls HaloFirstPerson.Tick/HoldKeys/Release (Combat-owned file) - Combat should not.
- Net: `HaloDesk.ReportRemote(new HaloDeskRemote{...})` per remote player per frame; fire from HaloEvents.Fired(who = slot).
- Vehicles: Desk poses seated avatars (own from HaloLocal.Vehicle/VehicleTag/SeatIndex, seat marker "marker:<name>"), `SeatPoseFor` for V.
- Match: HaloDesk.Start() at Playing, HaloDesk.Stop() at leave.

## Player.log lines
`Desk: cyborg ...`, `Desk: started (...)`, `Desk: monitor camera ...`, `View: ...` (HaloFirstPerson), `Desk body: posing my own avatar '...'`,
`Desk body: my own avatar's avatar '...' for the Halo puppet: arms True ...`, `Desk body: my own avatar is posed by the Halo cyborg (...)`,
`Desk body: ... sits as the cyborg in 'W-driver' ...`, `Desk body: first person: my avatar's head ... hidden`, `Desk body: ... other copies ...`.

## First-person framing (lead review, 2026-10-19)
- Reframed: `HaloDesk.FirstPersonPose` pins the weapon's rear-top corner (its model bounds) at `FirstPersonScreen` (0.55, -0.45 of the
  half screen) at `FirstPersonDepth` 0.30 m for a 70 deg camera (deeper for narrower FOVs: same screen share), barrel on the crosshair point;
  the grip is kept within `FirstPersonReach` 0.40 m by drawing the weapon shrunk toward the eye on this screen only (HaloViewScale, given
  back after drawing; x0.64 at 70 deg, x0.46 at 40 deg - same picture). Hidden while zoomed. Hands IK onto it (left pull-back up to 40 cm).
  Without a humanoid avatar the weapon is still framed (no arms). Lab `desk-monitor` (Blood Gulch slayer, HUD-lab camera): monitor-*.png.
- Lab note: lab pictures taken from a coroutine see Combat's Update pose of the weapon (Desk places it in LateUpdate); the HUD lab's
  monitor-full.png showed Combat's own hold for that reason. My lab re-runs the pass first (`HaloDeskBody.LabPoseNow`).

## Weapon animations (user request, 2026-10-19)
- Data/Anims/HaloWeaponAnims.cs + Game/Desk/HaloWeaponAnim.cs (API in REQUESTS.md "from D ... weapon model animations"). 3P graphs exist only
  for pistol / shotgun / sniper; everything else comes from the fp graph's part channels (weap +1116 fp model, +1132 fp antr), as deltas.
  Harness `wan` / `wa2` list them; lab `weapon-anims` passed 6/6 (pictures weapon_*.png: top rest, bottom animated).
- Not done: shader / ammo-counter object functions (the AR display digits) - shader side (World / HUD).

## Compressed animations (campaign, 2026-10-19)
- HaloAnimation.DecodeCompressed: format worked out from the campaign maps (harness `hex`, `q48`, `brute`), verified with harness `twin`
  (186/186 decode, twins within 0.19 deg) and `jmp` (frame-to-frame turns; a few isolated hand flips up to 177 deg in a10 x10 marine /
  crewman clips - in the data as far as I can tell: no twin to check against). Lab `cine-pose` (Keyes, a10 x10 'well_thats_it_then').
- (regression pass 11-10) Lab `desk-monitor`: the runner's camera is disabled/untagged, so HaloBody had no head (HaloPlayer.Head ->
  Camera.main) and Combat never drew a weapon. Monitor() now tags r.Cam "MainCamera" and enables it for the scenario (restored at the
  end); the passenger wait is 2.5 s of game time instead of 150 frames (was mid 'W-passenger enter'). Passed: AR 0.70 m from the seat
  marker, right hand 0.11 m from the gun (monitor-passenger-view/outside.png).
- (V, seats) HaloDesk.FirstPersonPose has a camera-pose overload (pos, rot, fov, aspect) - same framing; HaloPuppetPoser.ReachArms (arms to
  wrist targets with given elbow poles and hand turns) for the own first-person seat view.

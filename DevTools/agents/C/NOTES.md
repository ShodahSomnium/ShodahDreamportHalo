# C notes (Characters: campaign bipeds)

## Files
- Data/Characters (Unity-free, harness DevTools/agents/C/harness: `list`, `anims`, `nums`, `poses`, `boxes`):
  - `HaloCharacterDef` (bipd + actv: model / graph / coll, team, biped flags (Flying ...), turning, heights, ping / death thresholds,
    grenades, melee damage, unit weapons, change colours; actv: weapon, body / shield vitality override (absolute points), rate of fire,
    projectile error, burst, grenades, drop numbers, colours). `ColorA(rng)` = change colour A.
  - `HaloCollisionDef` (coll: vitality, shield material type, stun / recharge, effects, materials (type, head, multipliers, leak),
    regions (threshold, flags, destroyed effect, permutations), nodes with per-permutation BSP vertex bounds + main material).
  - `HaloUnitAnimator` (the graph state machine on D's HaloPose: seat (stand / crouch / alert / flee / vehicle seat) + weapon class by label,
    idle / move-front/back/left/right blended by direction at the unit's pace (root speed), turn-left/right, airborne, cross-fades by pose
    snapshot; aim-still / aim-move grid, seat look grid, fire-1 overlay, one action (reload, melee, throw (key frame -> release), gesture,
    dive / evade (root motion), land, ready, pings), deaths (s/h-kill by dir + region, airborne-dead / landing-dead), permutations "%n" by
    weight, custom animations of any graph (scripts / cinematics) with root motion).
- Game/Characters: `HaloUnit` (partial: .cs control / Init / Update, .Body model + permutations + colour + hit boxes + pose write,
  .Move kinematic mover, .Weapon weapon in hand / firing / grenades / melee / drop, .Damage TakeHit / shields / regions / pings / death /
  IHaloHitInfo, .Control gestures / dive / evade / CustomAnimation, .Net co-op puppet + checkpoints), `HaloBipeds` (Spawn, Clear, Erase,
  All, caches per map, events Died / Damaged / Fired, body sweep).
- Editor/HaloLabCharacters.cs: labs `characters` (-halomap a10 / a30: line-up pictures idle / aim / fire / walk / strafe / dying / dead +
  close-ups) and `characters-damage` (pistol bullets on a grunt and an elite).

## Contract (for I, K, N, S, Q)
- `HaloBipeds.Spawn(bipd or actv, pos, yawDeg (Unity), team)`; team None = the unit's default. `HaloBipeds.All`, `Erase(u)`, `Clear()`.
- Controls: MoveVelocity (m/s world; walkers horizontal, flying 3D), LookPoint / AimPoint (Vector3.zero = unset), Firing (the unit fires
  its weapon itself: actv rate of fire, actv projectile error + trigger error, magazine + reload-1, infinite reserve), Crouching, Melee /
  ThrowGrenade / Jump (pulses, cleared by the unit), Stance ("alert", "flee", "asleep", "flaming"; null = stand), Frozen.
  `NaturalSpeed` (m/s of the move-front animation now), `Grounded`, `Yaw`, `Eye`, `Center`, `Muzzle` (Transform, barrel = +X / right),
  `Weapon`, `WeaponLabel`, `Grenades`, `GrenadeType`, `Body` / `Shield` (fractions), `MaxBody` / `MaxShield` (points), `LastHit(Time)`,
  `Def` (HaloCharacterDef: Flying ...), `FireOnce()`, `Give(weap)`, `Gesture(id)`, `Dive(dir)`, `Evade(right)`, `CustomAnimation(antr, name,
  interpolate, loop, hold)`, `PlayingCustomAnimation`, `StopCustomAnimation()`, `EnterSeat(marker, label)` / `ExitSeat(at, yaw)`, `Teleport`,
  `Kill()` (before the first frame = placed dead: `PlaceDead()`), `KillSilent()`, `Revive()`, `SetName(name)` (HaloObjects).
- AI shots: `HaloUnit.AiShooter = -2` as shooterSlot / SourcePlayer. Units never raise HaloEvents.Fired (Desk would animate the local
  avatar); they raise `HaloBipeds.Fired`. Death raises HaloEvents.Killed (Killer = hit.SourcePlayer, Victim = -1) + HaloBipeds.Died.
- Co-op: `Puppet = true` (Net moves it; hits -> HaloNetGame.PuppetHit), `SetWeapon`, `PlayFire` (visual-only), `PlayMelee`, `PlayThrow`, `Shots`.
- Checkpoint part "characters" registered with HaloCheckpoints on campaign maps (life, vitality, place, weapon, grenades of existing units).

## Damage model
Hit -> nearest hit box (coll node BSP bounds on the model node, layer Hitbox, kinematic Rigidbody on the model holder) -> its material:
shield first when material.ShieldMultiplier > 0 (modifier of the coll's shield material type x multiplier; leak passes), body by the
material type's modifier x body multiplier; head material + "can cause headshots" + no shield = death. Region thresholds shoot regions off
("~damaged" permutation, model rebuilt). Shield: stun (coll stun time) then recharge (coll recharge time). Pings by the biped thresholds.
Death: h-kill for explosions / past the hard death threshold; units without death animations and with a body-destroyed effect pop.
Dead bodies: hit boxes become triggers (shots and players pass), body stays 90 s (max 40).

## Status
- Harness `all a10 a30 a50 b30 b40 c10 c20 c40 d20 d40`: 411 bipds / actvs through every state (seats, move, fire, reload, throw, melee,
  dive, ping, death 200 steps), 0 exceptions, 0 NaN. Compressed animations (D decodes them now): cyborg_cinematic 9/9, pilot 4/6 and every a10 cinematics graph play as custom animations (harness `cine`, `cine2`: 0 failures, 0 NaN).
- Lab `characters` (a10, 23 characters), `characters-a30` (22), `characters-c10` (flood forms, sentinels, monitor, Keyes), `characters-a50`
  (hunters): pictures DevTools/lab/characters*/lineup_{idle,aim,fire,walk,strafe,dying,dead}_rowN.png + close_*.png, looked at: models textured
  with their permutations (marine helmets, crewmen heads) and change colours (crewman shirts, grunt / elite ranks), weapons in the right
  hands (elite plasma rifle, jackal pistol on the left with its arm shield), muzzle flashes / plasma bolts / needles flying, blood decals,
  run / strafe cycles, aim grid, h-kill / s-kill deaths, dropped weapons. a30, a50, c10 passed (sentinels hover); the first a10 run failed only on Cortana (invulnerable, check fixed since).
- Lab `characters-damage` PASSED: pistol bullet on a grunt (30 -> 10 points, h-ping), 3 bullets take an elite minor's shield 1.00 -> 0.40
  with the body untouched (impact material 30 = elite energy shield), shield recharging after the coll's 12 s stun, grunt killed (s-kill
  front gut), HaloBipeds.Died with killer 0, plasma pistol dropped as a pickup.
- Weapon IK, shield glow overlay, distance animation LOD, recorded-control (IHaloRecordedControl) are in but not checked by picture.

## Open
- Shield glow (the biped's modifier shader overlay) not drawn; only the coll's shield effects play.
- Left-hand IK onto the weapon ("left hand marine" etc.) not done (the animations hold the weapons themselves).
- actv weapon damage modifier (AI damage scaling) needs Combat (G); forced shader permutation (elite majors' armour shade) not applied.
- Footsteps use the cyborg's table (HaloFootsteps.Step), not each biped's own footsteps tag.

## Script surface (round 2, HaloUnit.Script.cs)
Invulnerable, DropsItems, SetEmotion(int) (seat "emotions" overlay frame), SetMaximumVitality / SetCurrentVitality (points; MaxBody /
MaxShield include the override), SetPermutation(region, perm) (rebuild), FlashlightOn (light at the "flashlight" marker, numbers ours),
WeaponReadied / ReadyWeapon(), TotalGrenades / SetGrenadeCount. Checkpoint part skips HaloAi.IsActor units (I restores its own).

## Vehicle seats (enter / exit clips)
EnterSeat plays the seat's enter clip (seat anim 7), ExitSeat the exit clip (8) on the seat, then FinishExit stands the unit under the clip's
final pelvis (standing pelvis height from the stand idle) -> airborne -> landing -> normal. `Exiting` while it plays. Lab
`characters-dropship` PASSED (a30 lz_cship, 4 grunts out of the pod, land-hard, then move-right; dropship_exit_0..10 / _after.png).

## Weapon / body sounds (A's audit)
Fire = trigger firing effect; reload = magazine reloading effect; empty = firing effect "empty"; heat + overheated effect (vents, AI plasma
weapons overheat); overcharge (actv special fire mode 1) charging effect + trigger 2; ready effect; weapon attachments via HaloFx.Attach;
body down = HaloImpacts Impact, else the foot tag's Land. Lab characters-a30: fire 233, empty 2, overheat 5, body impacts 10.
NB: a perl substitution emptied HaloUnit.Weapon.cs once; restored from DevTools/labproject (identical copy of the last lab), then re-edited.

## Workstream 2 (2026-11-04): body, damage, Flood death mechanics, defs, animator
Verified offline with the C harness (`ws2 <map> [name]`, `ws2anim <map> <name> [label]`, `boxes`); lab scenario `body` (Editor/HaloLabBody.cs, d20) written, NOT run yet.
- HaloCharacterDef: CosMaxSlope (bipd @1232, fallback cos @856: humans / infection 0.707, Covenant / flood-elite / carrier 0.5), CanClimbAnySurface
  (biped bit 6), AccelerationScale (@32: carrier 1.2, hunter 0.5, cyborg 0.7), SpawnedCountMin/Max/SpawnedVelocity (@604/@606/@608: carrier
  flood_infection 3..6 v 3), unit flags DestroyedAfterDying (1) / MeleeIsFatal (8) / ImpactMeleeAttachesToUnit (12) / ImpactMeleeDiesOnShields (13) /
  MeleeAttackersCannotAttach (15) / NotInstantlyKilledByMelee (16) / ShieldsFryInfectionForms (22) (carrier, infection, hunter, cyborg checked),
  FeignRepeatChance (@584 0.25), AiVehicleRadius / AiDangerRadius (@640/@644: carrier 2), MaxSoft/HardLandingTime (@980/@984), LandingSeconds(v),
  InfectionForm, SpecialFireDelay (actv @348: jackal 5, grunt 10-15), GrenadeTrajectory (actv @386), ActiveCamouflage / SuperActiveCamouflage
  (actv 0x10 / 0x20: stealth elites), MeleeAttackDelay (actr @888: infection 3, combat forms 2).
- HaloCollisionDef: each BSP also split per surface material (Box.Parts: edge-ring walk of each surface -> its material's vertex bounds).
  Hit boxes are one per (node, material) when a node mixes materials (hunter: 24 boxes - the flesh behind the plates, the arm no longer all
  "shield"); TakeHit's Nearest prefers the smaller box on ties.
- Die(): destroyed = body destroyed effect && (destroyed after dying [after the death clip: carrier s-kill 10 frames, infection h-kill 7] ||
  overkill >= threshold || no death clip). DestroyBody plays coll.BodyDestroyedEffect (its jpt! damage = the effect runtime, WS3), raises
  HaloBipeds.Destroyed, hides, Destroy. Log "Characters: <unit> destroyed after dying (...)" once per kind. Not destroyed -> HaloBipeds.Corpses (32).
- MeleeIsFatal (carrier): its melee kills it at the clip's key frame (or at once without a clip) -> bursts. Log "... melee is fatal: it goes off".
- Melee reach: contact = unit radius + 1 wu (MeleeContactWu, ours) - not the actv melee range. Units without a melee clip (marines) strike at once.
  Base melee (elites) is a root action (the lunge moves the unit by its frame info).
- Grenades: actv grenade velocity x 30 (per tick: grunt 0.13 -> 11.9 m/s); the unit's @704 stays per second. Trajectory toss / lob (high arc) / bounce.
- Infection forms: "infection form pop" damage pops them; TryLatch(target | null = player): pop on a fried shield, else strike + latch
  (Suspended, parented to the host's body marker, melee-continuous 46, strike every melee attack delay); any damage pops a latched one.
- Hunters: melee hits never Instant/BackHit for "not instantly killed by melee". Jackal: shield at 0 -> region "shield" ~shield_off (model + hit
  box from bsp1), back when it recharges (log "...energy shield is down").
- Feign death (flood combat forms): non-lethal hit >= 30% body, chance 0.4 then 0.25: death clip, alive, Feigning; after 2.5 s resurrect-front/back.
- Region inhibits (MeleeInhibited / WeaponInhibited / WalkingInhibited) from shot-off flood regions; the unit stops melee / firing itself.
- AiRateScale / AiErrorScale on rate of fire / projectile error; SpecialFireMinInterval (= actv special fire delay) limits overcharges.
- Camouflage: renderers hidden, shown 0.5 s after firing / a hit (super: 0.25 s), faint shield-overlay shimmer (CamoShimmer 0.12, ours).
- Animator: BlocksMovement (dive, land, Base ping / gesture / melee, dead / feigning); MaxSpeedFor(x fwd, y left) (marine 2.10 / 1.50 / 1.75,
  grunt 1.88 / 0.98 / 1.20, elite 2.60 / 2.19); StrafeCapable (no back/left/right clip -> FaceMovement whenever moving: carrier wobble fix);
  idle below 35% of the clip speed (ours); permutations re-picked at each loop; PlayLeap (48/49/50) / EndLeap; Land(hard, seconds) cut;
  Feign / Resurrect (44/45); Base gestures are root actions.
- Open: lab `body` not run; area damage of the burst depends on WS3; WS1 should call Land(hard, Def.LandingSeconds(v, out hard)) (REQUESTS.md).

## Movement physics rewrite (workstream 1, "floor falls") - HaloUnit.Move.cs / HaloUnit.cs / HaloUnit.Control.cs
Compiles (build.sh 0 errors); NOT yet lab-verified (labs `floor` / `floor-lift` written, Editor/HaloLabFloor.cs, run them in verification).
- Walkable = normal y >= Def.CosMaxSlope (bipd @1232; WS2 parses it, clamped 0.3..0.95 ours); CanClimbAnySurface: any non-overhang (n.y >= 0.05).
- Ground probe: SphereCast (0.5 x radius) from step height above the feet, nearest WALKABLE hit, feet = centre - r / n.y (exact on planes);
  a ray down the middle when the sphere starts overlapping. Grounded: snaps from +stepUp to -0.35 m (more when the ground moved).
- Fall: a sphere (0.85 x radius) at the feet sweeps the fall; walkable -> lands; steeper -> stops at the face and slides the rest along it
  (fall speed reduced to its along-face part). Rising: sphere of 0.9 x radius sweeps up from the head (ceilings). Never a free move.
- Horizontal: main capsule above the step height (as before) + a LEG capsule 5 cm..step height that blocks only faces steeper than the max
  slope that are not a step (walkable top <= step height ahead). Overlapping leg hits re-read the face's real normal (Collider.Raycast).
- Spherical units (Def.Spherical or standing height 0: infection forms): height 2r, step r, one sphere for every sweep. Feet = transform
  position (model feet are at y 0.01 in the characters-c10 lab, so no PillCenteredAtOrigin offset).
- Embedded check 4x/s (CheckFloor): 6 cm sphere on the axis above the step height, or a backface-enabled ray up from feet+0.15 whose first
  hit faces up (the back of a floor) -> back to LastGroundPos (else Settle). Log "Characters: <name> recovered from inside the floor".
  LastGroundPos is recorded at that check while grounded and not embedded.
- Out of the world 2x/s (CheckOutside, alive and bodies, not seated/puppet/flying/on a machine): feet AND body HaloWorld.Outside twice in a
  row, or feet < Bounds.min.y - 6 -> LastGroundPos when grounded within 2 s, else Kill() (bodies: Destroy). Log "... is outside the BSP" /
  "fell under the world". The old "nothing within 60 m -> wait in place" now applies only to a unit that never stood anywhere (collision
  not built yet).
- Suspended (API): skips TickMove/TickDeadMove/Vertical/CheckFloor/CheckOutside; clearing it runs Settle(). HaloCampaign.SwitchingBsp holds
  every unit the same way. HaloUnit.cs subscribes HaloCampaign.BspSwitched: non-actor units (scripted bipeds, bodies) whose feet are outside
  the new BSP or have no walkable ground there are suspended + renderers hidden (log "... is not in BSP n: suspended and hidden"), and come
  back (Settle) when their place is in the active BSP again. Actors are the AI's (HaloAi Suspend/Resume, WS7).
- Settle(): out of solid (nearest free point straight down then up, 0.1 m steps, 2 m), then the walkable surface closest to the feet height
  (rays from feet+0.05 and feet+1, 4 m down). Used by InitMove, Teleport (so checkpoint Restore), the end of suspension. SnapToGround = Settle.
- Platforms: the ground collider is remembered when it moves (kinematic rigidbody, HaloDevice parent, Vehicle layer); each tick the unit gets
  its delta (position + yaw about its pivot, whole delta after a stall, ignored > 5 m), Physics.SyncTransforms, probe reach |dy| + 0.1.
- FinishExit: SphereCast from pelvis+1 m for pelvisHeight+1.5 m -> feet on walkable ground found (below the pelvis), else hang and fall;
  then Physics.ComputePenetration against overlapping Vehicle colliders (a shared trigger CapsuleCollider far away as the shape).
- Leap(v): flat velocity + fall speed, Leaping until landing or contact (horizontal block / ceiling / steep face); no steering in flight;
  Animator.PlayLeap(Start) then (Airborne) after 0.15 s. AddImpulse(v): additive, airborne when v.y > 0.1; bodies keep their flight
  (friction 8 m/s^2 only on the ground) and land swept.
- Landed(): landing velocities read as world units / s (Invader: bipd @988..1000 are wu/s); death at 'death hard landing velocity' unless
  'immune to falling damage' (log "died of a hard landing"; counter LandingDeaths). Movement frozen while Animator.BlocksMovement (WS2).
- Speed: animation-driven bipeds (unit max velocity 0) clamp MoveVelocity to Animator.MaxSpeedFor(body-frame dir) (WS2); NaturalSpeedToward(dir)
  for the AI; recordings move at NaturalSpeedToward(throttle direction).
- Counters for labs: HaloUnit.RecoveredFromFloor / RecoveredFromOutside / RemovedOutside / LandingDeaths.

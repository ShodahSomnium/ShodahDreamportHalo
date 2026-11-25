# I (AI) - notes

## Built
- Data/Ai (Unity-free; my harness DevTools/agents/I/harness: `dotnet run -c Release --no-build -- actors|enc [name]|cmd|conv|path|nav [bsp]|navpng <bsp> <enc> <png>|squads|diff <map>`):
  - HaloActorDef.cs: actr (HaloActorDef: type, vision, perception times, cover, danger / panic / retreat, crouch, evasion, melee,
    berserk, position times), actv (HaloActorVariantDef: actor, unit, major variant, weapon, firing / burst / new-target / moving / berserk
    params, grenades, drops, vitality), HaloActorCatalog (per map, cached), HRange, HaloActorType.
  - HaloAiScenario.cs: actor palette, encounters (squads: starts, move positions, difficulty counts, group masks; platoons; firing
    positions), command lists (atoms + points), ai animation / script / recording references, conversations (participants, lines);
    names "enc", "enc/squad", "enc/platoon"; CountFor = normal count on easy/normal, insane count on heroic/legendary (0 = none).
  - HaloAiDifficulty.cs: the globals' (matg +284) difficulty table. a30 legendary: rof x1.5, burst sep x0.5, grenade chance x2, major n0.75.
  - HaloAiNav.cs: navigation from the sbsp itself. "pathfinding surfaces" = one byte per collision surface, bit 0x40 = walkable (exactly
    the surfaces with normal z > 0.7 on a10 / a30 / b30); "pathfinding edges" is EMPTY in the retail maps, so links come from the collision
    edges (left / right surface), plus step links over risers <= 0.12 wu and slope links; grid locate; A* + funnel kept a radius from walls.
    Built in 2-35 ms a BSP. a10: every encounter start / firing position on the nav, start->firing paths 60/63 .. 198/198 per BSP.
    a30 (outdoor): starts on nav 153/166 (dropship starts above ground), failures are mostly between separate terrain islands (cliffs).
- Game/Ai:
  - HaloAi.cs: data (HaloSession or LabMap), Tick() for K (30 Hz), Frame() + HaloAiDriver (labs; stands down when K ticks), Difficulty,
    placement (counts, required starts first, shuffled, major upgrades by the globals' fractions, encounter team), initially created
    encounters placed at mission start / BSP switch, dormant actors outside the active BSP, Erase / Kill / LivingCount / Units / Clear,
    hearing (HaloEvents.Fired + AI bursts), checkpoints (HaloCheckpoints "ai"), one-time Player.log facts, budget line every 60 s.
  - HaloAi.Script.cs: every ai_* function (list in REQUESTS.md "[from I to S]"); vehicles ones log unimplemented.
  - HaloAi.Conversations.cs: ai_conversation (participants, lines with delays, advance, status / line), HaloAudio playback, LineSpoken event.
  - HaloAiEncounter.cs: squads (delay timers, respawn), platoons (attacking / defending, change / maneuver rules), position claims, following.
  - HaloAiBodies.cs: what actors perceive (local player: HaloPlayer.Head + HaloLocal.Position; remote players: HaloNetGame.Players; every
    HaloUnit; labs: StandInPlayer); sight rays with a budget of 48 a tick.
  - HaloActor.cs / .Combat.cs / .Commands.cs: the brain - idle / guard (guard firing positions) / patrol (move positions) / search /
    combat (squad group masks by attacking-defending x target visible, desired combat range, claims, cover) / flee + cower (leader killed,
    friends killed, big hit, surprise; panic in groups) / hide (low shields, elites) / berserk / flying / following / command lists;
    bursts x difficulty, burst origin sweep, target leading, grenades (stimulus, ranges, enemy count, collateral, encounter timeout,
    chance x difficulty), melee (+ leaps), defensive crouch types (jackal shield), evasion from danger.
- Division with C: C fires while `Firing` (actv rof / error / damage), throws on `ThrowGrenade` toward AimPoint, strikes on `Melee`;
  I decide when and where.

## Verified
- harness numbers above (data, nav, a10/a30/b30 encounter dumps: harness/enc_a10.txt, enc_a30.txt, cmd_a10.txt).
- lab `ai -halomap a30 -haloenc lz_search` (real BSP 0, stand-in player + 2 marines following it): 17 covenant placed by difficulty
  counts (incl. major upgrades), grunts / elite / marines acquire by sight, 32 bursts (C fires them), stand-in hit 37x for 518, marines
  killed 7 covenant, panics on friends / leader killed (flee -> cower -> back to combat), elite melee + evades (earlier run), 163 paths,
  tick avg 0.17 ms (max 87 ms = the one-time nav build). Pictures DevTools/lab/ai/ai_a30_*.png. Cave run (cave_floor) showed the
  scripted ai_kill of the elites making the grunts panic.
- round 2: grenades - grunts (stimulus "seek cover", chance 0.2) now throw as they panic / run for cover: lab ai lz_search "first
  grenade thrown by lz_search/grunt#1". AI in vehicles (HaloActor.Vehicle.cs, V's SetAiInput): lab ai-vehicle a30 - 2 marines board the
  warthog (W-driver, W-gunner) in 0.8 s, the driver follows the stand-in player 33 m and stops 8 m short (DevTools/lab/ai-vehicle/*.png).
  object_set_*_attack_inhibited, command list script atoms wake scripts (HaloScript.Vm).
- NOT verified: conversations in a mission, command lists on a10 crew,
  checkpoint restore, co-op, a10 in the full mission.

## Open
- vehicle enterable rules (ai_vehicle_enterable_*, ai_vehicle_encounter) still logged unimplemented; automatic migration; command
  list atoms recording / vocalize;
  dialogue vocalizations; ledge drop links in the nav.
- round 3: co-op host hand-over (HaloAi.Handover.cs, Net part "ai" order 30): lab ai - capture 5.5 KB / 16 actors, adopt with blob
  16/16, without blob 16/16 re-seeded, bursts continue.
- round 4 (user: AI like Halo): nav detours to the nearest reachable spot of the own island (+ a no-radius retry) instead of
  'unreachable'; out-of-firing-range actors close in; vocalizations from the units' dialogue tags (HaloAiVoice: new enemy, grenade,
  flee / leader died, berserk, melee, leap, search, cover, killed enemy, friend died, shooting, vehicle entry); hunters berserk when the
  partner dies; grunt kamikaze (actr berserk grenade chance); actv new-target firing pattern + surprise delay; flying vehicles
  (HaloActor.Vehicle.cs Fly: command list points in 3D, attack runs, ground / obstacle clearance, friendly line of fire); ghost drivers
  strafe / ram; vehicle bursts from the vehicle's actv. Diagnostics in the budget line: 'not firing at a known target' reasons, detours.
  Labs: ai-banshee a30 (aib_*.png banshees in the air), ai a10 crossfire 22 bursts / unreachable 0, containment elites hit the player 75x.

## Workstream 5 (actor sensing, steering, recovery) - 2026-11-04
Files: Game/Ai/HaloActor.cs, Game/Ai/HaloActor.Perception.cs, Game/Ai/HaloAiBodies.cs, Editor/HaloLabAiSense.cs (lab `ai-sense`).
- Perception (HaloActor.Perception.cs Perceive): 8 per-enemy entries (body, awareness, first/last seen). Cone pass over every enemy in range
  (central angle full rate, max / peripheral half rate - ours), sight rays round robin over the in-cone entries (2 bodies per look, a real
  index rotation), awareness x body.Visibility (camo 0.2 ours; local player HaloLocal.CamoVisibility, remote p.Camo, units Camouflaged).
  A NEW target (first or switch) only after awareness >= PerceptionTime() (actr combat / guard / non-combat); damage, magic sight and
  ai_try_to_fight stay immediate. Choice: damaged us in 1 s > preferred > distance. Logs 'AI: <actor> acquired <body> after X s'.
  Switch to an attacker that hurt us in the last 1 s when the target has not for 3 s (ours). Know: Known = within max(4 s, hide-target-not-visible).
- Surprise: in Acquire (sight / damage) while Idle/Patrol with no target for 10 s, inside SurpriseDistance or hurt by an unseen enemy:
  PanicWhenSurprised -> Panic, else Surprised / LastSurpriseAt for Combat. The per-sight panic is gone.
- Berserk from damage: body <= BerserkDamageThreshold && recent body damage (decays over ~1 s, ours) >= BerserkDamageAmount (pair semantics unverified).
- Danger triggers (TickDanger every 3rd tick): target in a vehicle / player / unreachable (other nav island, or a path failed in 3 s) at level
  1 visible / 2 fired in 1 s (HaloAiBody.FiredAt from heard shots) / 3 player shot passing close (Combat ShotAt's _danger rise) / 4 hurt us in 1 s,
  held DangerTriggerTime.Pick -> WantsRetreat (cleared 1 s after). Vehicle dodge: enemy vehicle body within its vehi @640 'ai vehicle radius'
  + 0.6 s of travel (ours), heading at us (dot > 0.7, > 3 m/s) -> Unit.Dive aside with NoticeVehicleChance. Friends retreating:
  NotifyFriendFleeing counts distinct visible fleeing friends within 8 wu over 10 s (ours) -> Panic at FriendsRetreatingTrigger.
  _friendsKilled decays one per 30 s (ours).
- Command lists without initiative break on damage or an enemy within SuicideSensingDist (else SurpriseDistance): 'AI: ... broke command list'.
- Idle: no Home walk-back; idle (not asleep) actors use the CURRENT squad's guard positions, else stand. Home = position on MoveToSquad
  (ai_migrate), ReturnToRest (grounded) and leaving a seat.
- Steering: NavAllows - the point 0.45 m ahead must be on a nav polygon within 0.6 m down (ours) or have world ground under it, else the
  velocity is zeroed and a repath runs (overrides / separation clamped too). WasRecovered -> repath + log. Stuck: progress tracked apart
  from Repath (0.25 m per 1.5 s); give-up releases the position, keeps it (and the goal) bad for 10 s (IsBadPosition), Stop().
  Moving goals repath at most every 0.5 s (the path end follows the goal meanwhile). Off-nav start: the nearest polygon (goal island, else
  any within 4 wu) when the line to it is clear, never a blind straight line. Separation only while moving, same team, on foot. Arrival
  re-checks the goal distance (drifted actors walk back). Suspended units: velocity 0, no tick. Feigning: frozen, target kept, Combat after.
  WalkingInhibited: no movement. Counters: HaloActor.OffNavBlocks / StuckGiveUps / Recoveries / CommandBreaks / DangerRetreats / VehicleDodges.
- NOT verified in a lab yet (this phase forbids lab runs): run `ai-sense` (a30 lz_search) and `ai-sense -halomap c20 -haloenc enc1`.

## Workstream 7 (2026-11-04): encounters, BSP dormancy, carriers, nav slopes, script orders - built, NOT lab-verified yet
Lab: `lab.sh ai-enc [-halomission a10] [-halomission2 a30]` and `lab.sh ai-enc-carrier [-halomission d20]` (Editor/HaloLabAiEnc.cs).
- BSP dormancy (HaloAi.cs): actors of encounters whose BSP is not loaded (living AND bodies) get `HaloUnit.Suspended = true` and their
  renderers hidden (only the ones that were on; restored on resume); cleared (Suspended=false -> Settle) when the BSP is active again,
  in the BspSwitched handler BEFORE PlaceInitialForBsp. While `HaloCampaign.SwitchingBsp` nobody thinks or moves. Free actors / no-BSP
  encounters: judged by `HaloWorld.Outside(feet)` at each switch. ForceActive no longer keeps an actor of another BSP running (no floor).
  Log: `AI: n actors suspended for bsp k (m resumed on their floor)`. API: `HaloAi.SuspendedForBsp(u)`, `HaloAi.SuspendedDormant(u)`.
  The AI keeps its own reason table (bsp / dormant) and only clears Suspended when no reason is left.
- ai_place of an encounter of another BSP: placed suspended at the authored start (no snap onto the loaded BSP); logged once per encounter.
  PlaceSquad: extra actors (count > starts) are jittered only onto walkable nav within a step of the start height, else the start itself.
- Carrier burst: `HaloBipeds.Destroyed` -> unit Def.SpawnedActor (flood_infection), n = rand(SpawnedCountMin..Max) x DiffScale(InfectionForms)
  (= 1 + table offset; easy 0.6 .. legendary 2, ours until verified), at Center + 0.3 m, outward at SpawnedVelocity wu/s (x HaloSpace.Unit)
  with 30-60 deg elevation (ours) via Unit.AddImpulse, same team, joined to the dead actor's encounter/squad (counted by ai_living_count),
  magically see the carrier's target. Log once: `AI: floodcarrier (...) released N flood_infection (3..6, difficulty x1, 9.1 m/s)`. `HaloAi.Released`.
- Nav (HaloAiNav): slope links only across surfaces nz >= 0.5 rising <= 0.2 wu (was 0.45 / 0.5 wu); every link keeps its nz and
  `FindPath(..., maxExpand, minSlopeNz)` skips steeper ones; the old overload uses HumanSlopeNz 0.707. Walkable filter: bit 0x40 && nz >= 0.7.
  Harness: a30 step links 91->32 / 71->59, c20 71->39 ..., start->firing path success unchanged (a30 214/489, 471/672; c20, b30 identical).
- Squad timers: any acquisition (HaloActor.Acquire -> NoteAcquire) starts every squad's timer (not "delay forever" squads). Arrivals in a
  squad (`HaloAiEncounter.Joined`: migrate, maneuver, spawned forms, ai_attach) start a "start timer immediately" squad's timer, or get
  magic sight when its timer already expired with "magic sight after timer".
- Knowledge sharing: an acquisition is told to same-encounter actors within 15 wu (ours) that see nothing (heard: seen 1 s ago, Search),
  unless the teller or listener runs a "disable communication" command list. First one logged.
- Squad-scoped orders: ai_retreat / ai_maneuver with a squad move only that squad (logged once per call text with every actor's
  squad -> squad); ai_follow_target_* squad form per squad (`Enc.FollowsPlayers(squad)`, `FollowUnitOf`, `FollowAiOf`; the old
  `Enc.FollowPlayers/FollowUnit/FollowAi` are now the encounter-wide "any squad" view until WS6 reads per squad). Flee maneuvers no
  longer Panic() (voice "retreat"); "say advancing when maneuver" voices "advance"; a maneuver rule is used up even while disabled.
- Automatic migration (Think, 1 Hz): squads flagged automatic migration move their actors to the flagged target squad (target on,
  default true - a50 switches sets off before scripted moves) whose firing positions are nearest the nearest player, 2 wu hysteresis (ours).
- Encounter activity (1 Hz, campaign with players only): Active = force active, actor fighting in the last 10 s, a player within 40 wu or
  in the same cluster, or linked encounter active. Inactive + AllowDormant -> not ticked, units suspended (not hidden). First one logged.
- Checkpoints: seated actors store vehicle (object name / id) + seat and are re-seated (retried 3 s if the vehicle comes back later);
  others store LastGroundPos and Settle(); per-squad follow saved. Handover v2 carries per-squad follow (players).
- Difficulty: DiffScale adds InfectionForms (1 + x), GuidanceVsPlayer; DiffAdd adds InfectionForms (raw), OverchargeChance.
  HaloAiDifficulty.InfectionForms defaults to 0 (was 1) when no globals table.
- Commands: Teleport atom ends with Settle(); `HaloAiCommandRunner.Holding` (hold atoms this tick) for Steer's separation; respawn total
  <= 0 with respawn on = unlimited (unverified).
- Projectile noticing (every 3rd tick): HaloProjectiles.CopyRecentFlights segments within the projectile's AI perception radius (wu ->
  m) of an actor (not its own team's) are noticed with actr notice-projectile chance (per scan 1-(1-p)^(1/3), ours): Hear (alert) + ShotAt
  (danger). `HaloAi.LiveGrenadeRadius` (proj danger radius, m), `HaloAi.Dangers` / `HaloAi.DangerNear(actor, out d)` (grenades + living
  units with ai danger radius, e.g. carriers 2 wu, not for their own team).

## Workstream 6 (2026-11-04): actor combat, melee, positions, Flood (HaloActor.Combat.cs, HaloActor.Melee.cs, HaloActor.Vehicle.cs)
Compiles (build.sh 0 errors); NOT lab-verified yet (lab scenarios written: `ai-combat` (d20 Flood), `ai-combat-area` (a30 lz_search)).
- Melee (new HaloActor.Melee.cs): actv melee range (berserk: berserk melee range) = commit distance -> charge (MoveTo target, re-path
  <= 0.5 s, clamped to the squad area) for actr melee charge time; abort past melee abort range; the blow (ImpactContact) only at contact:
  flat dist <= unit radius + target radius (players 0.2 wu, ours) + actr melee fudge factor, |dy| < 1.5 m (ours). Next melee = DiffAdd
  (MeleeDelayBase) + MeleeAttackDelay x DiffScale(MeleeDelayScale), floor 0.5 s (ours; the old 0.3 factor is gone).
- Leap: rolled ONCE per opportunity (grounded, inside actr leap range, melee timer passed); Unit.Leap(dir*v + up*vy) with v = leap
  velocity x30 x3.048, t = d/v, vy = dy/t + ballistic x g t/2 (blend straight line / arc), |vy| <= sqrt(2 g 1.5 m) (ours). Contact in flight
  or on landing -> ImpactContact. Old Jump + Push removed. Logs 'AI: leap:' / 'AI: leap landed: apex ..'.
- ImpactContact(target): infection forms -> Unit.TryLatch(target) (WS2: fry / attach), else Unit.Melee. Carriers: the contact blow is the
  detonation via Def.MeleeIsFatal (fallback Unit.Kill() + one log if the flag is missing). Suicide()/Kill() deleted; suicide sensing dist
  only starts the berserk run; a carrier whose charge times out inside its (non-berserk) melee range goes off there. Log 'AI: X goes off at n m'.
- Berserk: no 12 s timeout; ends when the target dies or is Lost for actr target search time (-> Search). Kamikaze grenade carry removed:
  GoBerserk rolls actr berserk grenade chance (x difficulty grenade chance) for one normal Grenade() (range + collateral checks), then charges.
  Berserk firing ranges min..max (reversed hunter 4..2 handled).
- Confinement: actors of an encounter with positions never get free goals: out of range 2 s / target outside the squad bounds (+2 wu) ->
  nearest Pursuing position to the target, else attacking, else hold; empty mask -> known mask -> search -> guard -> Stop; hide/flee use
  the squad's union mask (flee with nowhere to go cowers); search uses positions only; charges clamp to the squad's position bounds.
  RangePoint / random search points only for Free actors and encounters with 0 positions.
- Group choice from Know (WS5): Visible/Known -> attacking/defending, Lost -> Search (shielded actors only with Shield >= pursue
  fraction). Search: visit up to NumPositionsNormal (Coord when a squadmate also searches; 4 ours without a value), hold each PursuitPositionTime,
  end at the state timer (TargetSearchTime) -> return state; a Searching return state settles to (GuardingAt)Guarding instead.
- Hold timers start on arrival (within 1 m): TakePosition gives a travel allowance (dist / half run speed + 3 s, ours) + hold. While
  travelling re-pick only on damage, cover request, mask change, double claim or state entry. PickPosition: -6 near friends' current /
  claimed spots (actr friend avoid dist, no clamp), skips IsBadPosition (WS5).
- Firing: burst = BurstDuration/Separation.Pick x active new-target/moving/berserk multipliers; Unit.AiRateScale / AiErrorScale = their
  rof / error products x DiffScale(RateOfFire / ProjectileError) (x3 firing wildly, ours); Unit.SpecialFireMinInterval = actv special fire
  delay x DiffScale(SpecialFireDelay). Facing gate = actr max aiming deviation yaw (60 deg ours when 0). Flyers without 'can shoot while
  flying' hold still for bursts (velocity <= 0.3 m/s, ours) and sidestep between them.
- Aim: persistent aim point closing (tracking + DiffAdd(TargetTracking)) of the error every 0.25 s (ours); burst origin at origin radius x
  burst error, behind the target's movement within origin angle (random when still), returning over return length (wu; burst angular
  velocity when > 0) and turning up to the return angle.
- Surprise (WS5's Surprised): gesture 27/28, then a wild burst for surprise fire wildly time followed by surprise delay, else the delay.
- Line of fire: AvoidFriendsLineOfFire holds fire for a friendly body within 0.5 m of eye->aim (side step after 0.5 s, every 2 s);
  CrouchWhenInLineOfFire crouches inside a friendly actor's burst or a friendly player's shot (0.7 m) for min crouch time.
- Retreat (WS5 WantsRetreat): defending (-guard) cover positions for actr retreat time, 3 s before another (ours). Panic flee = retreat
  time (no 0.35, no 2..8 clamp), then cowering time; NotifyFriendFleeing to friends within 8 wu.
- Held squads (timer): guard positions, turn and shoot at a visible target, damage expires the squad timer (logged).
- Crouch: attacking/defending crouch threshold on _danger; type 2 shield < 0.25 (ours); type 3 jackal holds crouched in place while the
  shield is up and below hide fraction, hides only when it popped (attack shield fraction 0 = no requirement); type 5 flood shamble;
  movement type 2 crouch/run phases (initial crouch chance, stay-with-friends copy). Must-move-forward units face their movement.
- Elites hiding: after the hide time, emerge-from-cover chance every uncover delay; defending hide time x actr modifier.
- Vehicles: boarding waits below an enter marker > 2 m above; 'allows weapons' riders fire their own weapon; GetsInVehiclesWithPlayer
  followers join the local player's driven vehicle within 6 wu (ours) in a gunner/passenger seat (prefer passenger flag), never the
  driver, and get out when the player leaves it.
- Gestures: surprise 27/28, signal-attack 33 by leaders once per target, celebrate 40 when the last target died, feeding 47 (FloodIdle).
- OPEN (requests to WS5 in REQUESTS.md): FloodIdle() from TickIdle, ClampMoveForward() after Steer, HoldOnArrival() in TickIdle's guard
  branch. Not done: death fire wildly (needs Characters), stalking (finding 2.11 / 5.13; not in the plan's task list).

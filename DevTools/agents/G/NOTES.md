# G notes (Combat: weapons, VR handling, grenades, melee, damage, pickups, powerups)

Status: built and compiling (Data/Combat, Game/Combat, Game/Shared/HaloLocal.Combat.cs, Editor/HaloLabCombat.cs). Data numbers checked in
my harness (DevTools/agents/G/harness: `dotnet run -c Release --no-build -- catalog|sim|weap|proj|jpt|eqip|itmc|netequip|matg|unit|fpanim|hmt bloodgulch`).
Lab `combat` / `combat-quick`: see "Verified" below.

## Files
- Data/Combat (Unity-free): `HaloWeaponDef` (weap: triggers, magazines, heat/age, zoom, melee, models, firing effects, family),
  `HaloProjectileDef` (proj: speeds CONVERTED to world units / SECOND (cache = per tick x30), gravity, guidance rad/s, timers, material
  responses, attachments), `HaloDamageDef` (jpt!: amounts, radius, flags, 33 material modifiers, `DirectAmount`, `AreaAmount`, `PartsOf(effect)`)
  + `HaloVitality` (coll: body/shield, stun, recharge, materials, shield effects), `HaloEquipmentDef` + `HaloItemCollection`,
  `HaloCombatCatalog` (per-map cache + globals grenades / MP unit / falling / HUD item names / HUD prompts), `HaloWeaponState` (firing mechanics).
- Game/Combat: `HaloCombat` (public API), `HaloHandling` (+ .Hands .Holsters .Reload .Melee .Desk .Objective: the one MonoBehaviour
  "Halo Combat" under the Halo Game object), `HaloGun`, `HaloMagazine`, `HaloProjectiles` (+ `HaloCombatFx`), `HaloPickups`, `HaloVitals`, `IHaloHitInfo`.
- Game/Vr (ported from BO2, lead-made, mine): HaloPlayer, HaloXr (Simulated hands for labs), HaloBody, HaloLaserAim, HaloTabletGuard, HaloFirstPerson.

## Game numbers used (Blood Gulch, from the tags)
- MP unit characters\cyborg_mp: body 75, shield 75 (cyborg energy shield = material 22, armor = 21), shields stunned 6 s after damage,
  full in 4 s; melee characters\cyborg\melee; grenade velocity 10 wu/s (30.5 m/s); starting weapons = the unit's AR + pistol ("generic").
- Grenades (globals): max 4 / 4, MP spawn 2 frag / 0 plasma. Frag: arming 1.5 s, timer 0.5 s after the first bounce; plasma: sticks
  (potential response "attach" on units), 2 s timer at rest / attached, attached damage 120.
- Weapons: AR 60/240 (15 rps, error 2-6.5 deg), pistol 12/60 (3.5 rps, 2x zoom), shotgun 12/24 (15 pellets, 10 deg), sniper 4/12
  (2x/8x), RL 2/4, PR heat 0.08/round (overheats ~1.5 s), PP charge 0.6 s (charged bolt overheats it), needler 20/80 (homing),
  flamethrower 100/300, fuel rod (heat + battery). Melee cooldown = the weapon's first-person "melee" animation (AR 1.23 s).
- Falling: harmful 4.39..6.21 wu/s (13.4..18.9 m/s), beyond: globals\distance (fatal).
- Item names / prompts: ui\hud\hud_item_messages ("Picked up a pistol" -> "Pistol"; "%d rounds for ..."), hud messages "pickup" /
  "swap_weapon" ("Hold [grip] to pick up Shotgun").

## Choices that are ours (marked `// ours` in code)
- Holsters (user's rule): long guns (AR, sniper, shotgun, RL, flamethrower, fuel rod) on the BACK over the right shoulder (second long
  gun over the left one); small guns (pistol, plasma pistol, needler, plasma rifle) on the right hip (second: left hip). Frags left
  chest, plasma right chest (one model per grenade, up to 4 in a row). Ammo pouch at the belt front-left. Left-handed setting mirrors.
  Why: long guns cannot hang at the waist without hitting the legs and are reached over the shoulder naturally; the small Halo
  one-handers are the quick-draw sidearms.
- One gun in the hands at a time (Halo). Pistol-class guns are one-handed (support hand steadies the recoil, no two-hand aim); long guns
  aim along the line between the hands.
- Reload: B/Y drops the magazine; pouch -> well; seats no sooner than 40 % of the magazine's reload time after eject; an EMPTY magazine
  gun wants a rack (setting `combat.assisted` = auto, A also chambers). Shotgun: a handful (4) of shells, one per magazine reload time
  (0.4 s) while held at the port. RL: the rocket pair is its magazine (well at the back of the tube). Plasma: no reload.
- Weapon scale: avatar height / cyborg height (2.146 m) * `combat.gunscale` (HaloGun.SizeFactor).
- Melee: front of the gun faster than 2.8 m/s (fist 3.2 m/s) into a hit box; from behind = within 60 degrees of the victim's facing.
- Grenade throw = hand velocity x1.5, capped at the unit's grenade velocity.
- Pickups: walk-over radius 0.7 m; grip reach 0.6 m from the hand to the weapon's axis; dropped weapons go after 30 s; spots with no
  respawn time use 30 s. Weapon sets (HaloRules.WeaponSet) swap / remove map weapons and pick the spawn pair by weapon name.
- Area damage falloff: full inside max(inner radius, core radius), linear to 0 at the outer radius; direct hits fall from the upper
  bound to the lower bound across the projectile's air damage range. Walls block blasts.
- Projectile speed from initial to final over the maximum range (1 s when unlimited). Needle super combine at 7.
- Tracers (LineRenderer, additive) for projectiles without a model until Effects draws contrails.

## APIs for other modules
- **Match**: `HaloCombat.Spawned()` (after each spawn teleport; the first one of a session lays out the map's items - I subscribe
  HaloSession.Unloading myself), `HaloCombat.TakeHit(hit)` (`Instant` = death, killer = SourcePlayer), `HaloCombat.Clear()`.
  Local death: I raise `HaloEvents.RaiseLocalDied(kill)` and set `HaloLocal.Alive = false` (never Killed). Objective:
  `HaloCombat.CarryObjective(weaponTag)`, `HaloCombat.DropObjective()` (no event), `HaloCombat.CarryingObjective`,
  `event HaloCombat.ObjectiveDropped(tag, pos, vel)` (player let go: VR right-hand squeeze+release, monitor 1/2/E; also on death).
- **Vehicles**: `HaloCombat.FireProjectile(projTag, pos, velocity(m/s, zero = tag speed), shooterSlot, cause, ignoreRoot, direction, visualOnly)`
  -> `HaloProjectiles.Flight`. `HaloCombat.Catalog` (Weapon/Projectile/Damage/Equipment/Collection/Blast). `HaloWeaponState` for vehicle guns.
  While `HaloLocal.Vehicle != null && !HaloLocal.SeatAllowsWeapons` the hands are emptied and nothing fires. Own vehicle colliders are
  never hit by my shots (`HaloCombat.IsOwnCollider`).
- **Net**: hits go to `IHaloDamageable.TakeHit(hit)` on your hit boxes; `hit.Amount` = the shooter's amount BEFORE the victim's material
  modifiers (the victim applies shield 22 / armor 21 modifiers of `hit.Effect` in HaloVitals - only it knows its shields).
  Optional `IHaloHitInfo` on the same component (HitSlot, HitIsHead(collider), HitFacing, HitMaterial(point), HitAlive) - please implement
  it on HaloNetHitbox (head detection, back melee, shield/armor impact effects). Shots: `HaloEvents.RaiseFired(weap, muzzle, dir, NetId)`
  per round. Replays: `HaloCombat.ReplayShot(weapon, muzzle, dir, slot, muzzleT)`, `HaloCombat.ReplayThrow(grenadeProjOrEqip, pos, vel, slot)`.
  Events: `HaloCombat.Thrown(projTag, pos, vel)`, `HaloCombat.Meleed`, `HaloCombat.Reloaded`; pickups `HaloPickups.Taken(item)` (item.Id =
  NetEquipment index or >= 1024 drop), `HaloPickups.LocalDropped(item)` (item.Gun.State.Loaded/Reserve, item.Tag; you may overwrite
  item.Id with your drop id), `HaloPickups.ApplyRemoteTaken(id)`, `HaloPickups.RemoteDrop(id, weap, loaded, reserve, pos, vel, slot)`.
  Own colliders: `HaloCombat.RegisterOwn(collider)` if you make a hit box for the local player. Camo: `HaloLocal.CamoUntil`, `HaloLocal.CamoVisibility`.
- **HUD**: HaloLocal: Weapons[i] (Tag, Loaded, Reserve, Charge = battery, Heat, Overheated, Label), Held (in hand or last drawn), InHand,
  Health/Shield (0..1, shield to 3 with over shield), OverShield, ShieldRecharging, ShieldLow, Frag/PlasmaGrenades (+Max), GrenadeSelected,
  Zoomed/ZoomLevel/ZoomMagnification, Charge (plasma pistol), Reloading/ReloadProgress, PickupPrompt, DamagedAt/DamageFrom, HitDealtAt,
  Camo/CamoVisibility. Messages through HaloEvents.RaiseMessage (pickups, prompts, ammo).
- **Desk**: `HaloCombat.HeldModel` = the held weapon's third-person model root (HaloModelBuilder root; origin = model origin, barrel +X).
  I reset it under my pivot every Update, so placing it in LateUpdate is fine. Monitor shots: from the muzzle toward what
  `HaloDesk.AimRay()` hits. `HaloCombat.Reloaded / Meleed / Thrown` events for the puppet. `HaloLocal.ZoomMagnification` for the FOV.
- **Front**: set `HaloCombat.TabletOpen` while the Somnium tablet is open (no firing). Settings keys: `combat.assisted` (bool),
  `combat.recoil` (x), `combat.grippitch` (deg), `combat.gunscale` -> HaloGun.SizeFactor (read at start).

## Verified / not verified
- Harness: catalog parses on bloodgulch (names, prompts, vitality, grenades), HaloWeaponState simulation: AR 15 rps (45 in 3 s), PR
  overheats 1.48 s / cools, PP tap + charged shot overheats, needler accelerates to 20 rounds in 3 s, sniper/RL one per pull.
- Lab `combat` (2026-10-19, passed, pictures DevTools/lab/combat/01..13): spawn with AR + pistol on the body (01, 02: AR diagonal on the
  back, pistol right hip, 2 frags left chest, magazine in the pouch); pistol drawn from the hip, 5/5 hits (03); B ejects, pouch -> well
  seats 12 rounds; holster; AR over the shoulder, left hand on the handguard, aim follows the support hand (21 deg), 1 s burst 15 rounds
  8 hits (07: muzzle flash + casing from the game's effect); melee from behind = instant, from the front 55; frag thrown 8.1 m/s, blast
  120 on the dummy; plasma grenade sticks to the dummy (10) and does 120; letting go away from holsters drops the AR lying on its side
  (11; sizes logged: pistol 0.21 m, shotgun 0.94 m, scale 0.792); pickup prompt "Hold [grip] to pick up Shotgun"; swap with 2 carried;
  walk-over ammo and over shield (3.0); 3 pistol bullets empty the shield, the 4th takes 50 % body, shields back 7.5 s later, headshots
  on no shield kill, weapons dropped on death; plasma rifle overheats after 1.50 s; monitor: auto-draw, fire hits along the aim (13:
  the game's shield-impact effects), R reload, G grenade (30.5 m/s), 1/2 switch.
- NOT verified: a real headset (grip-pose / laser aim frame, holster reach, throw feel), the Somnium client, a real networked match
  (Net's loopback lab uses my side), flamethrower / fuel rod / needler super-combine in a lab, falling damage, VR zoom (only
  HaloLocal.ZoomMagnification is set; no scope view), camo look of the own gun.

## Player.log lines
`Combat: ready (headset|monitor)`, `Combat: <map>: N weapons ... grenades 4/4 (spawn with 2/0)`, `Combat: vitality from ... body 75, shield 75`,
`Combat: placed N weapons and M equipment items`, `Combat: spawned with Assault Rifle + Pistol, 2 frag / 0 plasma`,
`Combat: <hand> hand draws <weapon> from <holster>`, `Combat: first projectile ...`, `Combat: <weapon> magazine out / seated / racked`,
`Combat: frag grenade thrown at X m/s`, `Combat: melee hit ...`, `Combat: picked up ...`, `Combat: died (...)`, `Combat: <weapon> overheated`.

## Iteration 2 (after the user's first client test)
- Why the AR / sniper reload never finished: the magazine seated, but the rack point was a guess (top-rear of the bounds, 16 cm off the
  real operating handle) inside an 11 cm reach - unreachable. Now every point comes from the models (Data/Combat/HaloWeaponPoints.cs):
  well = the bottom of the "frame magazine" node's geometry (pistol, sniper: 3P model; AR: its FP model, same frame), pulled part =
  "frame slide" (pistol, travel 1.8 cm Halo = 5.5 cm), "frame ophandle" (AR, sniper: the operating handle, 3 cm Halo), "frame pump"
  (shotgun), RL = the back of "frame tubes", needler = the top of its FP needles, shotgun port under the receiver at the FP shell.
  Player.log: `Combat: reload points for X: well (...) (from ...) = ... m, pull 'frame ...' at ... = ... m, travel N cm, support ...`.
- The pulled part MOVES (the node follows the hand back; past 60 % of its travel it racks; the pistol slide also runs back on each shot
  and LOCKS BACK on empty / until racked). Reach 13 cm (17 cm while a rack is due), the nearer of pull / support wins.
- The magazine prop is the weapon's OWN magazine cut out of its model (triangles of "frame magazine"); the gun's own magazine node is
  hidden while the magazine is out (pistol, sniper). It seats when its middle is within 14 cm of the seated place / the well.
- Shotgun: shells into an empty gun -> pump it (pull the pump back). Assisted reload (`combat.assisted`) skips every rack.
- Cupped pistol (all one-handed guns): the free hand at Halo's own "left hand cyborg" marker (under / in front of the grip, 14 cm reach)
  cups it: the aim blends the two hands' pointing, recoil steadier; letting go returns to one hand.
- Hover haptics (HaloHandling.Haptics.cs): a free hand entering a place with something in it buzzes once (gun 0.45 / 60 ms, grenade
  0.28 / 35 ms, a due rack / the pouch with a magazine out 0.35 / 45 ms) and ticks lightly every 0.6 s while it stays; empty = nothing;
  HaloSettings.Haptics off = nothing.
- Fuel rod gun "0/0": it has no magazine in Halo PC's tag (heat + battery "age", like the plasma weapons) - Weapons[i].Charge is its
  battery for the HUD; 0/0 rounds is right.
- Magenta grenades (client): the chest grenade / pouch / shell props survived a match end, whose materials Match destroys
  (HaloMaterials.Release) - now dropped on HaloCombat.Clear and rebuilt from the next map (`ForgetMapVisuals`).
- Back holsters (user: "easier to grab behind the back"): a box in the head's frame turned with the body yaw - 0.20 m above the eye to
  0.60 m below, 0.45 m to each side, behind the eye by 0.02 m (shoulder height and up) / 0.12 m (lower: never the chest or face); right
  holster x > -0.10, left x < 0.10; one long gun on the back = either hand, either shoulder. The hover buzz uses the same box; a long gun
  let go inside it goes onto the shoulder on that side (else the free one). Player.log `Combat: back holster reach: ...` once.
- Weapon animations (Desk's HaloWeaponAnim) wired: Attach on every HaloGun model (held, holstered, ground); Play "fire" / "charged"
  (shotgun "chamber"), "ready" on a draw, "melee" (monitor F), monitor reload "reload-empty"/"reload-full" in the magazine's time and
  "reload-exit" after the shotgun's last shell; SetState "empty" (magazine in, no rounds, no rack due) and "overheated" with
  "overheating" / "overheat-exit" (holstered weapons cool too now). One driver per part: Combat moves slide / ophandle / pump only while
  a VR hand holds it or a rack is due (SetHeld then), else the animation does; magazine always SetHeld (Combat hides the 3P magazine during a monitor reload and shows it when seated). Combat's
  own slide cycle remains only for weapons without animations. Lab combat-reload: 62 ok (shot animations move the slide 3.0 cm, sniper
  bolt 9.0 cm, shotgun pump 9.2 cm; PR vents open / close; monitor AR reload plays first-person reload-full).
- Reload sounds (Data/Combat/HaloReloadSounds.cs, Game/Combat/HaloSoundPiece.cs): Halo has ONE sound per first-person reload animation
  (the fp graph's sound reference: weapon_anims\pistol_reload, ar_reload, sniper_reload_empty, rocket_reload_e, needle_reload,
  flamethrower_reload; the magazine blocks' reloading/chambering effects are empty or absent). Each VR hand action plays the PIECE of
  that sound around the moment the same action happens in the animation (measured from the 'frame magazine' / slide / ophandle motion,
  else fractions 0.15 / 0.55 / 0.75 / 0.82); cut from HaloAudio.ClipOf on a pooled 3D AudioSource with a 30 ms fade. Shotgun: enter
  (shells taken), reload-empty (each shell), exit-empty (pump). Plasma: overheating / o-h-exit on vent / cool. Monitor reload: the whole
  sound at the start (as Halo). Player.log `Combat: reload sounds for X: ...` once per weapon; HaloSoundPiece.LogPlays logs each play (lab).

## Campaign (phase 2)
- AI shots (shooter `HaloCombat.AiShooter` = -2, HaloUnit's): Local = false, Authoritative; HaloProjectiles tests the LOCAL player's body
  capsule (feet + 0.25 m -> head + 0.08 m, radius 0.28 m at 1.7 m; head = the top 16 %; monitor: the avatar's head bone) against every
  segment: impact damage / headshot / material response (attach: sticks to HaloCombat.LocalAnchor, the chest); blasts with shooter < 0 hurt it.
- Campaign vitality from the globals' player information unit (cyborg); multiplayer from the MP unit (cyborg_mp). Spawned() in the campaign
  gives nothing (the mission's profile: GiveProfile / SpawnedWith) and places no netgame items; campaign drops stay 600 s.
- Script: InputEnabled (+ HaloCinematic.Playing holds input), Unzoom, ApplyDamage(jpt, object), ApplyDamageAt(jpt, point), GiveProfile.
- Lab `combat-ai` (a10): grunt kills the stand-in player in ~3 s of fire (shields, then body), own shots never hit; passed.
- Difficulty (campaign; HaloCombat.ScaleForTarget / ScaleForPlayer, table = I's Data/Ai/HaloAiDifficulty from the globals): AI hits on the player x enemy damage (a10: 0.3 / 1 / 1.4 / 1.8) x the actor variant's weapon damage modifier (grunt minor 0.5); AI hits on units x friend / enemy damage; the player's hits on units / enemy (friend) vitality (0.6 / 1 / 1.2 / 1.4). Lab combat-ai: a grunt plasma-pistol bolt on the player Easy 1.7 points, Legendary 9.7.
- Flashlight / night vision (HaloHandling.Flashlight.cs): monitor L, VR the free hand's stick click; the cyborg's flashlight_cyborg.ligh through HaloFx.Attach at the eye (SetLights 0/1), toggle click = the unit's integrated light toggle effect; zoomed sniper: the button toggles HaloLocal.NightVision. Script: HaloCombat.FlashlightOn, NightVisionAvailable. Not lab-verified.
- Breakable glass (World's HaloWorld.DamageSurface): every projectile impact on world collision (its impact damage x the surface material's modifier; replays too, each client breaks its own panes), melee swings (VR and F) into a pane, and blasts (each pane within the radius at its nearest point). A broken pane lets the projectile go on (glass response overpenetrate for bullets; disappear -> overpenetrate when it broke it). Lab combat-glass (a30): 3 panes, one AR bullet broke pane 0 (10 damage vs its vitality).
- SFX (A's requests): dropped weapons / items landing and grenade bounces play the item's material effects (HaloImpacts.Play(tag, Impact, point, -1, speed/6); grenades through the globals grenade equipment); a draw plays the weap ready effect, else the fp 'ready' animation sound; a melee hit adds the fp 'melee' animation sound; walking-over ammo plays the magazine object equipment's pickup sound (weapon_pickup_ammo\*). Empty click / overheat / charging / pickup / throw were already the tags' own. Lab combat: 40 ok, impacts played 6 (silent 0), weapon sound pieces 11.

## Workstream 3 (2026-11-04): area damage, impulses, damage flags, flight ring (G + X glue)
- `HaloDamageDef` (Data): damage flags verified against invader-defs DamageEffectDamageFlags: bit 5 `DetonatesExplosives`, bit 10
  `OnlyHurtsOneInfectionForm`, bit 12 `InfectionFormPop`, bit 16 `Use3dAcceleration`; `AccelerationMagnitude` (i component, or |vector|
  with the 3d flag), `AreaFalloff(distWu)` (the AreaAmount shape, 0..1). Tag values (d20): frag explosion r 1.5..2.5 accel 4, plasma
  0.5..2 accel 4, rocket 0.5..2 accel 6; dflags 0x20 (detonates explosives) on all three.
- `HaloProjectiles.Area` = the ONE area-damage core (projectile blasts via Blast/BlastOne, `HaloCombat.AreaDamage`, Effects' jpt! parts):
  IsArea hits with ExplosionCenter + InstantAcceleration; knockback `HaloUnit.AddImpulse(dir * accel * 3.048 * Def.AccelerationScale * falloff)`
  for living AND dead units (bodies found through HaloBipeds.All), dir lifted to >= 0.35 up, capped 20 m/s (both ours). We read the
  acceleration as WORLD UNITS / SECOND (the plan's wu/tick x30 would throw a grunt at 365 m/s - noted, ours). "infection form pop" ->
  Instant hit on infection forms (unit flags dies-on-shields / attaches, name fallback); "only hurts one infection form" -> first one only;
  "detonates explosives" -> grenades (live / resting / stuck) within the radius go off. Re-entrant calls (a blast kills a unit whose burst
  plays at once, a blast sets off a grenade) are queued (32) and run after; the same jpt! at the same point in one frame is applied once.
  Log once per tag: "Combat: area damage <jpt> hit n units"; first 12 "Combat: impulse <unit> v m/s ...".
- `HaloCombat.AreaDamage(jpt|effe, at, sourcePlayer, sourceUnit, ignore)`: implemented (an effe applies all its damage parts); shooter =
  player id, else AiShooter when sourceUnit, else -1 (world); the local player is always in reach.
- `HaloCombat.EffectDamage` (called by HaloFxRuntime for every jpt! part): world owner only (`HaloNetGame.WorldOwner`), never for projectile
  detonation / impact effects (`IsProjectileEffect`: every proj's @428 / @396 / material response effects of the map) - those stay the
  projectile's (shooter-authoritative; replays and vehicle guns play them too). A living creator's own body is not hurt (ours).
- `HaloCombatFx.Play(tag, pos, rot, attach, HaloUnit creator)`: new overload - the effect's jpt! parts get the creator as source.
- `HaloProjectiles.CopyRecentFlights`: ring of 64 slots (one per flight, segment over the last 0.15..0.3 s, team of the shooter unit /
  Player, proj @412 ai perception radius); filled at Launch / each Step / Release, no allocations.
- Melee (`HaloHandling.MeleeHit`): sends BackHit only (no Instant); players' HaloVitals already kills on Melee+BackHit, units decide in TakeHit.
- Lab `area-damage` (Editor/HaloLabArea.cs, d20): frag among 3 grunts (IsArea hits, impulse, on the floor after), infection pop chain,
  carrier burst through Effects (grunt hurt, infection popped, lying grenade detonated), recent flights. NOT run yet (verification phase).

## 2026-11-10 projectile visuals
- HaloProjectiles: the anchor of a model-less projectile now faces the flight with +Z (HaloFx's non-marker convention; it had +X, so
  attachment effects / systems emitted sideways). Replayed shots (ReplayShot, no ignore root) skip the shooter's own hit boxes
  (IHaloHitInfo.HitSlot == shooter) so a remote player's bolt does not burst on its own body at the muzzle.
- Plasma bolts are drawn by Fx lens flares now (see X/NOTES 2026-11-10).

## 2026-11-10 accepted by lead: SeatGun (from V)
- HaloHandling.Hands.cs PoseGun -> SeatGun(g) was added by V's seats fixer and is ACCEPTED BY LEAD as G's code: only acts while
  HaloSeatPoses.OwnGunPose is true (own monitor player in a third-person weapon seat, now also only in the seat's Sit phase - not during the
  enter / exit clips); the gun goes into the seat animation's hand so shots leave the muzzle the others see. Keep it when editing.
- The own monitor first-person passenger keeps Combat's view model for the own camera; V moves the model into the seated avatar's hands
  only while other cameras draw (beginCameraRendering) and gives Combat's local pose back at endContextRendering.
- 2026-11-10 overheat / charge fix (Game/Combat/HaloGunFx.cs, new): the weapon's overheated effect (weap +884; PR/PP effects loop 0..0,
  one 0.05 s event) now LOOPS (HaloFx.Loop on the model, Halo +X frame) from the overheat until the heat falls to the recovery threshold
  (vents close), for held AND holstered guns; stopped on release / destroy / HaloCombat.Clear. PP charge: HaloWeaponDef.ChargeAttachments
  (attachments whose primary scale comes through an object function from weapon in "primary/secondary charged": PP overcharge.ligh +
  lens flare at "secondary trigger flare1", charging.lsnd at "primary trigger"; the PP trigger has NO charging effect) are started while
  charging and scaled by the charge (light / flare brightness / lsnd scale), gone with the shot. Keyed API for remote players / AI:
  HaloGunFx.Vent(model, def, overheated) / HaloGunFx.Charge(model, def, charge or -1) (requested from N and the HaloUnit.Weapon owner).
  Lab combat (passed, section after picture 12): PR venting at 0.5 s and 1.5 s, cooled 2.53 s (1 -> 0.25 at 0.3/s), vent stopped;
  PP charge 0.53 -> 1.00, glow on, charged bolt flies, glow off, the charged shot overheats it and it vents (green steam at the top vents).
  Pictures 12v_* / 12c_*. Open: flare radius is X's random-in-bounds (Halo likely interpolates 0..0.175 wu by the charge), heat-driven
  flares (PR "heat flare" / PP "plasma pistol flare", weapon in "heat") not started for the local gun; remote charge not on the wire.

## 2026-11-10 AI / remote weapon attachments (attachments fixer)
- HaloGunFx: + SetReady / static Ready (attachments fed by weapon input 6 "ready", at the function's upper bound: energy sword glow),
  + static Forget(model). AI weapons (HaloUnit.Weapon.cs) and remote players (HaloRemoteView) use the keyed API (Vent / Charge / Ready).
- Labs: combat-ai new section "ai weapon" (AI plasma pistol: 0 lights/flares when given; overheats after 1.2 s / 7 shots, vents, stops
  after 0.77 s cooling; pictures ai_pp_idle / ai_pp_vent) passed. Heat / illumination-driven flares still not drawn (local, AI, remote).

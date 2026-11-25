# G - Combat (weapons, VR handling, grenades, melee, damage, pickups, powerups)

Goal: Halo's sandbox in VR (and on a monitor), with the game's own numbers.

User's words: "immersive gun holding and reloading, put grenades on chest, only carry two weapons, think what is best on the back and
what is best on the waist"; "melee of the guns, players PHYSICALLY hitting with the weapons"; "we need to be able to pick up weapons,
use them, drop them and use others". Copy the Black Ops 2 Dreamport's VR handling (Game/Combat/Bo2Handling*.cs, Bo2Gun*, Bo2Throwable,
Bo2Ballistics, Bo2Projectile, Bo2Damage, Bo2LocalPlayer, Game/Vr/*) and adapt it to Halo.

Data (Data/Combat): weapon (weap: triggers (rate of fire, projectiles per shot, error/spread, charging, overheat), magazines (rounds,
reload time, magazine equipment), heat/battery (age), zoom levels, melee damage, autoaim; first/third person models, sounds/effects per
trigger: firing effect, empty, overheated...), projectile (proj: initial/final velocity, gravity scale, guided (needler), detonation
effects, material responses, attached contrail, timers, arming), damage effects (jpt!: damage amount bounds, area of effect, category,
modifiers vs shields, instant kill flags), equipment (eqip: powerups - overshield, active camouflage, health pack; grenade pickups),
item collections (itmc: weighted random item), globals (matg): grenades block (max count, mp spawn default, projectile), player
information, weapon list, materials; the MP biped (globals multiplayer information unit -> characters\cyborg_mp) for shield/health
vitality and recharge (its collision model).

Game (Game/Combat, Game/Vr):
1. Weapons from the map's own tags, built with HaloModelBuilder (the third-person weapon model scaled to the avatar - the cyborg is
   ~6.5 ft, avatars vary; BO2 scaled to avatar height). Hold by the grip (find it from the model's markers/nodes, e.g. "right hand"
   marker or the trigger area), two-hand where the gun has a handguard (AR, sniper, shotgun, rocket launcher, plasma rifle? - plasma
   rifle/pistol/needler/pistol are one-handed in Halo; pistols may take a support hand).
2. Two weapons max + grenades. Holsters (VR, our choice - explain in NOTES): long guns (AR, sniper, shotgun, rocket launcher,
   flamethrower, fuel rod) on the BACK over the shoulder (two long guns: right and left shoulder); small guns (pistol, plasma pistol,
   needler, plasma rifle) on the WAIST (right hip; second small gun left hip). Frag grenades and plasma grenades on the CHEST (left
   chest frags, right chest plasma), counts from the game (max 4 each in MP? read globals). Oddball/flag are held weapons too.
3. Immersive reload per weapon family: magazine guns (AR, pistol, sniper): eject (B/Y), take a magazine from a pouch, seat it, rack
   (the game has no racking - optional "assisted" setting like BO2); shotgun: shells one by one into the tube; rocket launcher: load
   the tube/rockets; plasma weapons: no reload - heat, overheat vents (the game's vent animation time), battery drains. Reload timing
   minimums from the tag.
4. Firing: projectile simulation from the proj tag (most Halo "bullets" are projectiles with velocity and gravity; sniper/pistol are
   very fast; plasma is slow; needles home; rockets/fuel rod arc & explode); spread/error from the trigger; recoil feel ours; ammo use;
   plasma pistol charge shot (hold trigger). Hits: world (HaloLayers.World/Environment -> impact effect HaloFx + sound HaloAudio by
   the surface material from HaloWorld.SurfaceMaterial), players (HaloLayers.Hitbox colliders owned by Net's remote players, implement
   IHaloDamageable on them... Net makes them; you call `TakeHit`), vehicles (IHaloDamageable from Vehicles). Shooter-authoritative
   like BO2: you decide the hit and hand it to the target's IHaloDamageable; Net carries remote hits.
5. Physical melee: a swing of the held weapon (hand speed over a threshold, weapon collider sweep) or the free fist hits; damage from
   the weapon's melee damage effect; Halo's melee lunge is not needed; a melee hit from BEHIND kills instantly (Halo rule). Cooldown
   from the tag. Melee needs no button in VR (pure motion), F on a monitor.
6. Grenades: take from the chest, trigger = arm, throw with the hand's velocity (Halo grenades are thrown, not cooked: frag fuse
   starts at impact/arming per the projectile tag; plasma sticks to players and vehicles).
7. Pickups: weapons from the scenario's netgame equipment (item collections with spawn times / respawn) and the starting equipment
   (game type + rules.WeaponSet), placed lying on the ground; walk over a weapon you carry = take its ammo; grip a weapon on the
   ground = take it (if two carried, the one in hand drops / is swapped); letting go of a held weapon outside a holster drops it as a
   physical item (stays, despawns after Halo's time); dead players drop their weapons; powerups by touch (overshield, active camo
   -> HaloLocal.CamoUntil + make the own avatar's weapon semi-transparent for others via Net, health pack). Pickup HUD messages through
   HaloEvents.RaiseMessage ("Hold [grip] to pick up the Shotgun" style, use the game's strings if found in hud message text).
8. Damage model of the local player: shields then health (cyborg_mp vitality), shield recharge delay and rate, overshield,
   falling damage (globals falling damage), death -> HaloEvents.RaiseLocalDied + HaloLocal.Alive = false (Match respawns).
   Implement HaloCombat.Spawned / TakeHit / Clear / Label.
9. Monitor play controls (with the Desk module, who poses the avatar and owns the camera): mouse fire, right mouse zoom (sniper /
   pistol zoom levels with the game's zoom), R reload, F melee, G throw grenade, Q switch grenade type?, 1/2 or wheel switch weapon,
   E pick up. Shots go along `HaloDesk.AimRay()`. Do not fight the client's keys (BO2 Bo2FirstPerson notes).
10. Keep HaloLocal (weapons, grenades, health, shields) current every frame for the HUD/Net.
Lab: `Editor/HaloLabCombat.cs` with simulated hands (BO2's Bo2Lab did this: Override hands) on a flat test floor or Blood Gulch when the
World module's builder works: draw, fire, reload, swap, drop, pick up, throw both grenades, melee a target; pictures of a weapon held in a hand.

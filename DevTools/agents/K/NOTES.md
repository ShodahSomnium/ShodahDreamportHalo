# K notes (Mission: campaign flow, BSPs, objects by name, devices, trigger volumes, checkpoints, mission end)

## Files
- `Data/Campaign/HaloMissionData.cs` - Unity-free: every placement of every kind with its name / not-placed bits / bsp bits, device fields,
  device groups (+648), trigger volumes (+864, Halo's point test), bsp switch volumes (+924), starting profiles (+840), cutscene flags
  (+1252), each BSP's world bounds; `HaloDeviceDef` (device / machine / control tag fields).
- `Game/Campaign/HaloCampaign.cs` (API), `.Load.cs` (StartMission flow, player start + profile, leave / teardown, BSP switch),
  `.Tick.cs` (30 Hz game tick, saves when safe, death -> revert, co-op respawn, mission end).
- `Game/Campaign/HaloObjects.cs` - named objects; placement per BSP; create / destroy by kind; `HaloMissionObject` component.
- `Game/Campaign/HaloDevices.cs` - `HaloDevices` (groups, doors, controls, ride, script API) + `HaloDevice` (state + "device position" pose).
- `Game/Campaign/HaloTriggers.cs` - volume tests + BSP switch volumes. `Game/Campaign/HaloCheckpoints.cs` - snapshot / revert + module parts.
- `Game/Campaign/HaloCampaignDriver.cs` (order -95: device visuals, ride, control input), `Game/HaloGame.Campaign.cs` (hooks).
- `Editor/HaloLabCampaign.cs` - lab `campaign` (-halomission a10,a30). Harness: DevTools/agents/K/harness (`sum|vol|dev|anim|names <map>`).
- Private compile while others are mid-edit: `STUBS=1 EXCLUDE="regex" bash DevTools/agents/K/mybuild.sh`.

## Flow
StartMission(m, d): SetState(Active, m, d) -> phase Loading -> HaloLoading.Show(entry, null) -> worker: HaloMap + HaloScenario + HaloMissionData
-> start BSP = first player start location's bsp -> Host = HaloNetGame.CampaignHost -> HaloSession.Open (rules: MapName only)
-> HaloWorld.PlaceObjects = false; HaloWorld.Build(..., startBsp) -> NotifyWorldBuilt -> HaloDevices.Begin / HaloTriggers.Begin
-> HaloObjects.PlaceBsp(start) (auto-placed, difficulty bits, BSP membership: scenery/light bsp bits else BSP bounds)
-> player teleported to start location 0 (yaw 90 - facing) + starting profile 0 (HaloCombat.Spawned, then Unequip + Give + rounds, grenades,
   health/shield) -> Host: HaloScript.Start -> Loading.Hide, HUD, Desk -> Playing -> checkpoint "mission start".
Tick (every frame): back key = leave; death -> 4 s -> revert (co-op: 5 s -> next to a living player); fixed 30 Hz GameTick: HaloTriggers.Step
(BSP switch volumes), Host: HaloScript.Tick + AI tick, HaloDevices.Step; game_save waits for SafeToSave (alive, 3 s without damage, no
cutscene / switch; 8 s timeout); fell 6 m below the BSP = death. End(true) = message + hub after 8 s; End(false) = death -> revert.

## APIs (for S, I, C, Q, N, F)
See REQUESTS.md "from K". HaloCheckpoints.Register(key, capture, restore); HaloCampaign.Reverted event; HaloDevices.* by name and by group;
HaloTriggers.TestObject / TestObjects / TestPlayers / TeleportPlayersNotInside; HaloObjects.Create / CreateAnew / Destroy / *Containing /
CreateAll / DestroyAll / Teleport(name, flag) / Exists / PlacementOf; HaloCampaign.Data (HaloMissionData).

## Devices (as implemented)
Power -> PowerTarget at the power transition time; position -> desired at the position transition time (depowered machines stand);
groups: setting a member's position sets its position group (all members follow), "can change only once" groups lock after a change.
Doors (machine type door, not "does not operate automatically"): a unit / player within the activation radius (else the object's bounding
radius, min 1.5 m, ours) - one sided: in front (+x) - opens it; it closes after the door open time. Sounds/effects: open / close on start,
opened / closed at the ends, depowered / repowered, control on / off / deny. Pose: "device position" animation, frame = position x (frames-1)
(position loops / not interpolated / reversed honoured). Controls: VR hand within 18 cm + trigger, or a hand touching (< 4 cm);
monitor E with the aim ray (2.2 m) when no pickup / vehicle prompt. Toggle / on / off / call (call value). Ride: a ray down from the feet
hitting a device collider -> the body root follows that node's motion each frame (yaw only in VR), Physics.SyncTransforms.

## Status (lab `campaign -halomission a10,a30` PASSED, 12:4x)
- a10: Playing in 1.7 s, BSP 0, 85 objects (17 named: blam_steam_1, 2_marine_3 biped via C, cryotube_1 vehicle, containment_1_door_1 ...),
  37 devices, player exactly on the start, profile player0 (no weapons - correct for a10), 31 ticks/s, the script switched to BSP 7
  (intro space) by itself; lab stops scripts, SwitchBsp(0) -> World BSP 0; door small (machine 13) closed -> opened for the player
  (pictures lab/campaign/a10-door-closed.png / a10-door-open.png: the two halves slide apart) -> closed 1.5 s after; device_set_position
  cryo_door_2 1 / immediate 0; checkpoint -> object_destroy + machine moved + player moved -> death -> revert after 4 s: player back
  (0.00 m), object back, machine back; BSP switch volume bsp0,1 -> BSP 1 active; leave -> hub, root + objects gone.
- a30: Playing in 1.2 s, 435 objects, player in the lifeboat with AR 60/120 + pistol 12/64 (a30-start-eye.png), bridge_control used
  (toggle 0 -> 1, group bridge_control_position), first_beam_emitter by script, revert, BSP 0 -> 1 by its volume, leave.
- NOT verified: riding a moving lift / platform (no lift reached in the lab), VR hand touch / trigger on a control, monitor E, co-op
  (Host false path, coop respawn), game_won end flow, game_save waiting for a safe moment, the client (async teleports).

## Player.log
`Campaign: starting ...`, `Campaign: opened a10 in N ms: ...`, `Campaign: world ready (BSP i)`, `Campaign: BSP i: placed N objects (...)`,
`Campaign: the player starts at ...`, `Campaign: starting profile '...': ...`, `Campaign: scripts running`, `Campaign: playing ... loaded in N s`,
`Campaign: devices: <door def>`, `Campaign: control '...' used (...)`, `Campaign: checkpoint N (...)`, `Campaign: the player died (...)`,
`Campaign: revert N (...)`, `Campaign: reverted to checkpoint ...`, `Campaign: BSP switch volume '...'`, `Campaign: switching BSP a -> b`,
`Campaign: game_won - ...`, `Campaign: leaving ...`, `Campaign: back to the hub (...)`, `Campaign: riding a moving machine (...)`.

## Round 2 (after S / Q / V)
- `HaloCampaign.Speed` (game_speed), countdown timer (`CountdownSet/Stop/Restart/Digit/Ms`, in checkpoints), `HaloCinematic.Tick()` each
  game tick, `HaloObjects.AnimateScenery / AnimationDone` + `Game/Campaign/HaloObjectAnimator.cs` (named scenery built skinned),
  vehicles via `HaloVehicles.SpawnPlacement` / `Remove`.
- Lab `campaign -halomission a10,b30` (16:0x): all a10 checks again + game_speed 5 -> 166-169 ticks/s, countdown 65 s -> 64000 ms after 1 s,
  b30: 896 objects, AR 60/240 + pistol 12/72 + 4 frag, door small (auto radius 0.85) opens/closes, shafta_map_control used,
  holohalo_1 by script, revert, BSP 0 -> 1; rock_kick (object_create, not in the start BSP) clip01-rockfalling: frame 91, fell 30.6 m. PASSED.
- While `HaloCinematic.HoldsPlayer` (cinema floor, deferred script teleports) K skips the fall-out-of-the-world death (and for 2 s after
  the hold ends) and cancels the pending spawn re-check teleport. Compiled; not lab-run (x10 needs the full intro).
- Host hand-over (Game/Campaign/HaloCampaign.Handover.cs): "mission" host-state part (order 10) + Promoted -> host in place, checkpoint
  "host hand-over". Compiled; not lab-run (Net's net-coop lab covers the hand-over plumbing).
- Device SFX (for A): open/close/opened/closed/(de|re)powered + control on/off keyed on position movement (all clients); attached
  looping sounds scaled by the device inputs per their "primary scale" (HaloDevice.MapLoops / Loops). Lab a10,a30 coverage: device_door
  8/8 + 6 loops (a10), device_machinery 39 loops + device_door 2 (a30). Lights' function scaling NOT done (attached lights stay on).
- Press counter (HaloDevice.PressCount / LastPressDenied -> on/off/deny on every client, also without movement; Net asked to carry it); attached lights follow power on devices whose inputs read power. Compiled, no lab.

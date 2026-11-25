# M notes (Match: session flow, map load, spawning, game types, scoring, respawn)

## Files
- `Game/Match/HaloMatchControl.cs` - public API, spawning/death/respawn, kills & scoring, limits, game over, the per-frame Tick.
- `Game/Match/HaloMatchControl.Load.cs` - StartMatch (loading screen, map on a worker, HaloSession.Open, HaloWorld.Build, WorldBuilt,
  vehicles, first spawn, HUD, Playing) and LeaveMatch / Teardown (Unloading, module clears, world root, texture/material release, hub).
- `Game/Match/HaloSpawns.cs` - Halo-style start location choice. `Game/Match/HaloObjectives.cs` - CTF, Oddball, KOTH, Race, teleporters.
- `Game/Match/HaloRoster.cs` - `HaloRoster.Players` (HaloMatchPlayer list; local entry `HaloRoster.Me` synced from HaloLocal every frame).
- `Game/Match/HaloAnnouncer.cs` - globals multiplayer information (flag / ball weapons, hill shader, the 44 MP sounds by file name).
- `Game/Shared/HaloSession.cs` (+ HaloRules), `Game/Shared/HaloLocal.Match.cs`, `Game/HaloGame.Match.cs`, `Editor/HaloLabMatch.cs`.

## API for other modules
- `HaloMatchControl.StartMatch(rules)` / `LeaveMatch()` / `EndGame(why)` (master "end match") / `SwitchTeam(team)` (tablet team swap).
- HUD: `TimeLeft` (-1 none), `Elapsed`, `TeamScore(team)`, `Leader`, `GameOver`, `Result`, `WinnerTeam`, `WinnerId`, `LocalDead`, `OutOfLives`,
  `Rules`; `HaloLocal.RespawnIn`, `HaloLocal.Carry/RaceLaps/RaceNext/InHill/LivesLeft/Spree`; `HaloObjectives.RedFlag/BlueFlag/Ball`
  (State Home/Carried/Dropped, Position, Carrier), `CurrentHill` (Outline, Center), `HillOwner`, `NextCheckpoint(out pos)`.
- Net: fill `HaloRoster.Remote(id)` fields each tick; `HaloMatchControl.PlayerLeft(id)` when a slot goes (keeps the team's points);
  `HaloMatchControl.ReportKill(kill)` once per remote death announcement; `HaloMatchControl.RemoteObjectiveEvent(id, code, pos)` when a
  remote `ObjEvent` counter advances; carry `HaloLocal.Carry`, `ObjEvent`, `ObjEventCode`, `ObjEventPos` (+ Team/Kills/Deaths/Score/Alive).
  Start sync: set `HaloSession.StartUtc` before StartMatch for a late join; M sets it to now (if 0) when the world is entered.
- Combat: M calls `HaloCombat.Spawned()` after every spawn teleport, `Clear()` at game over and on leave, `TakeHit(Instant)` to kill a
  player who fell out of the map. M listens to `HaloEvents.LocalDied` (also catches Alive going false without the event).
- `HaloRules.Defaults(type, teams)`, `HaloRules.Label`. New fields: Lives, SuicidePenaltySeconds, KingMovingHill, RaceType, RaceTeamScoring,
  FlagAtHomeToScore. **RespawnSeconds now means the variant's extra respawn time (default 0 = Halo's "instant" = the fixed 3 s).**

## Rules as implemented
- Respawn: 3 s + RespawnSeconds (+ SuicidePenaltySeconds after a suicide / world death); beeps (countdown_for_respawn) in the last 3 s.
- Slayer: kill +1, suicide / world death -1, betrayal -1 (team). Multi kills within 4 s (double, triple, killtacular), sprees 5 / 10.
- CTF: flag stands = ctf flag netgame flags (usage id = team); touch enemy flag = take; touch own dropped flag = return; capture at own
  stand (own flag must be home by default); dropped flags return after 30 s (ours). Flag/ball shown at the right hand when carried (fallback
  until Combat has CarryObjective).
- Oddball: ball at the lowest-id oddball spawn, 1 point per second carried, reset 30 s after a drop.
- King: hill = hill flags of one usage id (polygon by angle), first hill or moving every 60 s in id order; 1 point/s alone in the hill
  (team: once per team via the lowest id inside); announcer hill_controlled / contested / hill_move. Outline = LineRenderer with the
  globals hill shader's material (fallback additive yellow).
- Race: race track flags by usage id; normal / any order / rally; Score = laps; team score min/max/sum.
- Teleporters: TeleportFrom -> random TeleportTo of the same usage id, on foot only; re-armed once the player stands on no entrance
  (two-way pads: Wizard / Damnation put an entrance on each exit).
- Spawns: start locations allowed for the game type; team games use the team's own starts when it has >= 4 (Blood Gulch bases), else all
  (Wizard / Prisoner / Boarding Action non-CTF starts carry teams 0..3, not red/blue). Weights: enemy within 9 wu -, enemy looking at it
  within 25 wu with line of sight -1, ally within 8 wu +, anyone within 0.8 wu blocks (all ours), small random.
- Fell out: below HaloWorld.Bounds.min.y - 4 m -> death (world). Spawn verify 1.2 s after: > 25 m off -> teleport again.
- Game over: score limit / time limit / out of lives alone -> phase Ending, HaloCombat.Clear, scoreboard, "game_over", 12 s -> LeaveMatch -> Hub.
- Back key (K) in a match = leave the match.

## Integration (done)
- Net: `Game/Match/HaloMatchNet.cs` fills HaloRoster from `HaloNetGame.Players` (InMatch), PlayerLeft keeps team points, SetCarrying /
  NoteGameEvent out, GameEvent in (drop position = the slot's Position), master SetObjective / late-join TryObjective. Kills: N calls
  ReportKill -> HaloEvents.Killed -> M credits the local killer (OnKilled).
- Combat: pickup -> `HaloCombat.CarryObjective(flag/ball weap)`, capture/death/leave -> `DropObjective()`, `ObjectiveDropped` -> drop at
  that point; M's own model hides while Combat holds it (fallback: model at the right hand).
- Front: `HaloLoading.Leave()` 0.4 s before the teardown; Desk: `HaloDesk.Start/Stop` with the HUD.
- Objective touches are ignored until the client applied a spawn teleport (feet within 2 m of the spawn or 2 s).

## Status (verified in the lab, Blood Gulch, slot B, real World / Combat / HUD / Audio modules)
- `match-slayer` PASSED: loads in ~1 s to Playing, player on a Slayer start (0.00 m), suicide -> -1 + 3 s respawn on a start, fell
  below the bounds -> world death -1, 5 kills via ReportKill -> score limit 3 -> "You win" -> 4 s results -> Leave fade -> hub, map gone.
  Pictures: lab/match-slayer/spawn-eye.png (outside the red base facing it), spawn-above.png.
- `match-modes` PASSED: CTF take / capture / drop on death; KOTH 3 s in the hill = 3; teleporter 0 to its exit; Oddball 2 s = 2, drop on
  death; Race one lap through 5 checkpoints. Pictures: lab/match-modes/ctf-redflag-*.png (flag pole on the red stand), king-hill-*.png.
- NOT verified: real multiplayer (two clients), the Somnium client (async DoTeleportToPoint, spawn verify), VR, moving hill, rally race,
  time limit end, lives, team balance with others. Flag cloth (globals flag shader / cloth) is not drawn: the pole + emblem only. The hill
  outline (LineRenderer with the hilltop shader) is faint.
- Lab note: the SDK's PlayerController moves itself after teleports (Combat saw "fatal falls"): the lab disables its scripts.

## Player.log lines
`Match: starting ...`, `Match: opened <map> in N ms ...`, `Match: globals multiplayer information ...`, `Match: world ready, bounds ...`,
`Match: objectives: ...`, `Match: spawned at ... (start i of n for Slayer ...)`, `Match: playing ... loaded in N s`, `Match: you died (...)`,
`Match: respawned at ...`, `Match: you killed ...`, `Match: you took / captured / dropped ...`, `Match: teleporter i -> ...`,
`Match: game over (score limit): ...`, `Match: results shown - back to the lobby`, `Match: leaving (...)`, `Match: back to the hub (...)`,
`Session: A -> B`; warnings `Match: spawn fallback`, `the player was N m from the spawn after the teleport - moved again`.

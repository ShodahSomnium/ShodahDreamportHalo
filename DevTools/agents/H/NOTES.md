# H notes (HUD)

Status: DONE - built, compiles (runtime, server, editor tool), server API guard clean, lab `hud` passes (DevTools/lab.sh hud, ~3 min).

## Files
- Data/Hud/HaloHudTags.cs - tag readers: HudGlobals (hudg + matg interface bitmaps / grenades / MP biped), HudUnit (unhi), HudWeapon (wphi +
  child chain), HudGrenade (grhi), HudDigits (hud#), HaloStrings (ustr), HudMessages (hmt), HaloFont (font glyphs).
- Game/Hud/HaloHud.cs - public API (Start/Stop/ShowScoreboard, SetAim, Damaged, Hmt, TeamOfCollider, NameOf, LabAttach).
- Game/Hud/HaloHudKit.cs - loading, sprites, Halo placement rules, flashing, additive look. HaloHudMeter.cs (CPU meters), HaloHudDigits.cs
  (hud# numbers), HaloHudLabel.cs (Halo font text). HaloHudView*.cs - the canvas and the pieces.
- Editor/HaloLabHud.cs - lab `hud`. DevTools/agents/H/harness - my probe (dotnet run -- hud|png|str|font|row|sheet <map> ...).

## What the tags say (worked out with the probe; Blood Gulch)
- MP biped cyborg_mp has two unit HUDs: [0] ui\hud\cyborg = FULLSCREEN (used), [1] cyborg_mp = split screen. Weapon elements: view 1 = fullscreen.
- hud globals: multiplayer HUD scale 0.75 (= the split-screen shrink: NOT applied, full screen draws the tags at their size - lead review vs Halo PC); motion sensor range 20 (world units), velocity 0.0002, scale 32; text colour #8075BAFF; font large_ui.
- Placement: bitmap corner on the anchor corner, offset into the screen (right anchors: +x = left; bottom: +y = up). "use high res scale" = x0.5,
  MP scale 0.75 unless "don't scale size/offset" (flags 2/1; don't-scale-size also cancels high res). Proven by the shield meter's tear lining up
  with the shield panel's (SP tags) and the split-screen tags (meter shapes on the panel, health under the bar) only matching with these rules.
- Meters: bitmap RGB = shape, alpha = gradient; lit when gradient <= value*alpha multiplier + alpha bias, value = rounds (ammo meters: AR bullets
  carry 4n, mul 4 bias 2; pistol 20n, 20/0; needler 12/6; rocket 120/1; sniper 60/1) or fraction*value scale (shield: 198, bias 55, gradient 63..243),
  else fraction*255 (health: 8 segments 30..240, minimum meter value 31 = one segment while alive). So shields/health drain toward the screen edge.
- Colours with alpha 0 are drawn (A unused); Halo's HUD is additive - drawn here as colour/max with alpha = max channel (black vanishes).
- Crosshair "flashes when active" on the aim reticle = red over an enemy. Warnings ("Reload", "No Ammo", "Low Ammo", battery) are crosshair types 3/8/10/6/18.
- Strings: ui\multiplayer_game_text (kill feed 75-94, "Rejoin in %d" 97, results 55-59, scoreboard headers 67-71); hud messages.hmt ("pickup":
  "Hold [18] to pick up [32]").
- Grenades: both grhi tags sit on the same spot; the second is drawn one panel width to the right (ours).

## ours (invented, marked in code)
visor 52 deg high (~66 deg wide) at 0.6 m (settings hud.vrfov / hud.vrdist) with smoothing 14/s; meter alpha = 1 - translucency/4; dark see-through colours (tracker disc) alpha x3.2; tracker 10-unit margin from the corner, sweep 1 s, blip speed 1.2 m/s, fire 0.6 s, blip 8 px;
radius = sensor centre offset; feed 5 lines 5 s; score box layout bottom right; scoreboard layout; waypoint sprites at half size; ally marker
(multiplayer HUD bitmap, green triangle) over allies' heads; damage arrows fade 1 s; VR scoreboard = hold left Y; monitor = F1 (Halo PC "show scores").

## Player.log
`HUD: tags of bloodgulch: hud globals ui\hud\default, unit hud ui\hud\cyborg ...`, `HUD: started on ...`, `HUD: monitor screen overlay` /
`HUD: headset visor (...)`, `HUD: weapon HUD weapons\assault rifle\assault rifle (3 levels, N elements, magazine 60)`, `HUD: stopped`.
Errors: `HUD: a frame failed: ...` (once).

## Verified (lab `hud`, pictures in DevTools/lab/hud, looked at)
- monitor-full: Blood Gulch with AR 32 + 180 (counter, bullet icon, 32 of 60 bullets lit in Halo's 3 rows), frag 2 / plasma 1 side by side
  (unselected dimmer), shields full + 8 health segments top right, crosshair, "Hold E to pick up Shotgun" (hud messages.hmt), kill feed in
  Halo's strings + "Double Kill!", time 9:59 and two score rows bottom right, motion tracker with 3 blips (the standing player hidden).
- monitor-low: shield sliver at the screen-edge end, health yellow (medium), bullets flashing, "Reload" under the crosshair, left damage arrow.
- monitor-sniper: zoom mask + black bars beyond 4:3, "2x", rangefinder 0001.554. monitor-plasma: "064" battery + icon, heat bar 40 %.
- monitor-dead: "Rejoin in 3". monitor-scores: Place/Name/Kills/Assists/Deaths in Halo's font, own row highlighted. vr-full: the visor
  (4:3, 46 deg) drawn over the gun and the sky, reticle canvas at the aim point.
- Fixed by the lab: plasma guns have an empty magazine block (energy detection), world-space UI needs a high Canvas sorting order or the
  sky (order 0, far plane, no depth) paints over it, HUD material queue 4000 + ZTest Always.
- Harness `layout` (ProbeLayout.cs) composes the MP HUD offline from the tags with the same rules (png/layout.png).

## Not verified / open
- Not seen in the Somnium client or a headset (visor size/comfort, scoreboard on left Y, F1 key, legacy Input in the client).
- Waypoints (CTF/oddball/hill/race), ally markers, team scores, the game-over line and the unit HUD sounds were not exercised by the lab
  (code paths only). Overshield look is ours (the tags' overcharge colours are 0). Lab kill lines say "died" because the offline net id is -1.
- Meter alpha / additive look / tracker sweep timing / grenade side-by-side / feed placement are ours (marked in code).

## Campaign (phase 2)
- Game/Hud/HaloHud.Script.cs: ShowHud / ShowPart / BlinkPart / HelpFlash(+Restart) / NavPoint / ClearNavPoint / Timer* (API for S, see REQUESTS
  "-> done (H) for S"). Help text (HaloScriptFns.HelpText, persistent, hud globals' help colours / flash) and objective (HaloScriptFns.Objective,
  8 s when it changes) in the message line; the scores key shows mission + objective in a mission.
- Mission mode (HaloCampaign.Active): unit HUD from globals "player information" unit (cyborg); no score box / kill feed / respawn text /
  ally markers; whole HUD hidden while HaloCinematic.Playing or show_hud false; tracker shows HaloBipeds.All (moving > 1.2 m/s or
  HaloBipeds.Fired in 0.6 s; HaloAllegiance friends of the player yellow, others red); nav points with hud globals' arrows.
- Lab `hud-campaign -halomission a10|a30` (passed both): a10 intro HUD hidden (a10-hud-1), after the skip shields + tracker, unarmed
  (a10-hud-2); nav point arrow + d40-style timer 1:29 flashing below the warning time (a10-hud-api); a30 lifeboat: script's show_hud false
  honoured (shot 2 hidden), then AR 120 / grenades 0 0 flashing / shields / tracker / crosshair (a30-hud-3).
- Not verified: help-flash look, blink, a real script nav point (a30 activate_team_nav_point_* once S binds them), AI blips (no moving AI
  in the shots), the objective line on screen (set at revert and faded before the shot).

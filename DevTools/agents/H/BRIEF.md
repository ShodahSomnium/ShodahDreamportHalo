# H - HUD

User's words: "Do the hud exactly the same." Halo's own HUD from its tags, shown in VR and on a monitor (BO2 had a world-space VR HUD +
a ScreenSpaceOverlay monitor HUD: Game/Front/Bo2Hud.cs, Bo2DeskHud.cs - same split here).

Data (Data/Hud): hud_globals (hudg: anchor offsets, default colours, messaging, waypoints, multiplayer icons, hud numbers (hud#), the
font), unit_hud_interface (unhi: the shield meter + health meter panel (top right), the motion tracker (bottom left) with its
background, foreground, centre and the radius; warning sounds/flash states), weapon_hud_interface (wphi: crosshairs per weapon (zoomed /
not, overheat), ammo meters / bullet icon rows / counters, static elements, overlays e.g. sniper scope, zoom), grenade_hud_interface
(grhi: grenade counts and icons, top left), hud message text (hmt: "Press E to pick up..." with icons), meters (metr) / smet shader.
Layout: Halo's HUD is laid out on a 640x480 canvas with anchors (top-left, top-right, bottom-left, bottom-right, centre) and per-element
offsets/scales/colours/flash; meters fill by a gradient threshold of their bitmap. Read every number from the tags.

Game (Game/Hud), implement the HaloHud stub (Start/Stop/ShowScoreboard + whatever you need):
1. Reads `HaloLocal` every frame (Shield, Health, ShieldLow/Recharging, Weapons[Held] Loaded/Reserve/Charge/Heat, grenades, Zoomed,
   Vehicle) and draws Halo's HUD: shields (blue -> flashing red when low, the game's "shield low" beep is the Audio module's
   HaloAudio.Play2D of the unhi sound), health bar (MP shows health too), ammo/battery/heat per weapon from its wphi, grenade counts,
   crosshair (monitor: centre of screen; VR: the reticle along the gun's barrel at the aim point), sniper/pistol zoom overlay on a
   monitor, the motion tracker (players within its range in the game's units: enemies red, allies yellow/team colour; only those moving
   above a speed or firing, like the game; the Net module provides other players' positions - REQUESTS.md: you need a list of remote
   players with position, team, moving/firing), messages (HaloEvents.Message), kill feed / multiplayer messages ("X was killed by Y",
   "You killed X"; Halo's colours and wording, strings from ui.map/hud if present), game type HUD (score of you/leader, time left from
   HaloMatchControl, CTF/oddball/hill indicators and waypoint arrows over objectives, the flag carrier).
2. VR: a head-locked visor HUD (it is Master Chief's helmet HUD after all) at a comfortable distance with slight smoothing; keep it out
   of the way of the gun; optional wrist copy. Monitor: ScreenSpaceOverlay canvas (BO2 found world-space HUDs smeared by the
   upscaler/TAA on a monitor), Halo's 4:3 layout stretched by anchors to the screen.
3. Scoreboard (Halo's in-game scoreboard look: names, kills, deaths, assists?, team totals, ping column optional) on a button
   (Tab is the Somnium tablet; on a monitor use a free key; VR: hold a controller button or the tablet app's SCORES page by Front).
4. Death screen / respawn countdown ("Respawn in 3"), end-of-game "Game over" + scores.
Lab: `Editor/HaloLabHud.cs` - set HaloLocal values (full shields, low shields, AR with 32/60, sniper zoomed, grenades 2/1) and take
pictures of the monitor HUD and the VR HUD. Compare with how Halo's HUD looks (you know it) and the tag data.

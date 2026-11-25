# F - Front (ring hub, room menu, tablet app, lobby, loading screen, settings)

User's words: "make the tablet app, set up the room, look at how we did Black Ops 2"; "Make the main area the HUGE halo ring with
menu in the middle, so it is expanded just like Black ops 2"; "Add the White list system so no one can see it if they come in and isn't
on the whitelist" (the gate is in place in HaloGame - you must keep everything you build behind it: only start from the Front* hooks).

1. **The ring hub** (the room at HaloSpace.RoomOrigin, 3 km above the maps): the HUGE Halo ring around the player with the menu in the
   middle. ui.map holds the main menu's own ring: `scenery\halo\halo.scen` / `scenery\halo\halo.mod2` and `sky\sky_ui\sky_ui.mod2`
   (check with `dotnet run -- models ui halo`), plus Threshold / sky pictures. Build it at a scale that reads as a ring world (kilometres
   across, the band arcing overhead), the planet/sky backdrop, slow rotation like the game's menu; the floor the player stands on;
   lighting that makes the ring look like the menu. Expanded like BO2's room (Bo2Room, Bo2RoomLook, Bo2RoomSmoke, Bo2PanelPlacer:
   the panel stands on the floor in front of the player).
2. **Room menu** (world-space panel, monitor + VR) styled like Halo's PC main menu (ui.map: fonts (font tags - Halo's menu font; build a
   TMP font at run time like the ETS Dreamport's EtsFont if feasible, else a close fallback), ui_widget_definition bitmaps (the main menu
   background, button highlight), strings (unicode_string_list: "MULTIPLAYER", game type names...), ui sounds (cursor/forward/back -
   HaloAudio.Play2D). Pages: MULTIPLAYER (-> lobby), SETTINGS, ABOUT. The instance master label ("INSTANCE MASTER: name", BO2 Bo2MasterLabel).
3. **Lobby** (BO2 Bo2Game.Lobby + Bo2GameRules UI): game type (Slayer, Team Slayer, CTF, King of the Hill, Oddball, Race - the game's own
   variant names), map (the 19 MP maps with their preview pictures from ui.map - find the map thumbnails), the rules (`HaloRules`: score
   limit, time limit, respawn time, shields, motion tracker, weapon set, vehicle set, friendly fire, teams), players by team, START MATCH
   (master only, countdown) -> `HaloMatchControl.StartMatch(rules)`. Non-masters see it read-only and can pick team. The Net module
   publishes the master's rules (`HaloNetGame` API - coordinate via REQUESTS.md: you need "am I master", "master name", "players list",
   "publish rules", "rules from master", "match started by master").
4. **Tablet app** exactly like BO2's (`Game/Front/Bo2TabletApp.cs`, `Bo2TabletLink.cs`, LabStubs in BO2's Editor/LabStubs for the lab):
   a "HALO" app on the Somnium tablet's home screen holding the same menu; in a match: scoreboard, team swap, leave match, (master) end
   match / change map.
5. **Loading screen** (implement the HaloLoading stub): the game's loading look (the map's loading picture from ui.map if present, "Loading..."
   bar), music fade (HaloAudio.StopMusic), fade into the match, fade back to the hub on leave (BO2 Bo2LoadingScreen/Bo2LoadingArt).
6. **Settings** page: volume, haptics, left-handed, VR options other modules register (read HaloSettings keys they document in their
   NOTES), install folder override.
7. Menu music in the hub (HaloAudio.Music if the Audio module finds Halo's title music).
Implement `Game/HaloGame.Front.cs` (partial void FrontStart/FrontBootDone/FrontTick/FrontStop). Call HaloSettings.Tick() in FrontTick.
Lab: `Editor/HaloLabFront.cs` - pictures of the hub (ring visible, menu readable) on a monitor-like camera and from VR height; click
through the lobby with simulated input; the tablet app with the LabStubs.

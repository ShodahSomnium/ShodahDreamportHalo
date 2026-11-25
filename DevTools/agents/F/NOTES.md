# F notes (Front: ring hub, menu, lobby, tablet app, loading screen, settings)

## Files
- `Data/Ui/HaloUiData.cs` - ui.map reader (Unity-free): string lists by name end, map labels/descriptions (mp_map_list,
  mp_map_select\map_data, order = `HaloUiData.MenuMapOrder`), widgets (DeLa bounds), the ring placement (uicam + scenery\halo\halo).
- `Data/Ui/HaloUiFont.cs` - Halo font tag -> SDF atlas (Alpha8) + glyph metrics (worker thread).
- `Game/Front/HaloUiArt.cs` - worker load of ui.map, pictures (HaloTextures.Prepare/Upload), `Bind/BindSliced/Rebind`, UI sounds, music tag,
  ring/sky meshes; `HaloFonts` = TMP fonts from the SDF (EtsFont recipe).
- `Game/Front/HaloUi.cs` - canvas/rect/text/picture helpers, `HaloButton` (sel_list_item_bkd slabs, menu_* caption pictures), `HaloPanelPlacer`.
- `Game/Front/HaloHub.cs` - the ring hub: platform + colliders + fence, sky dome (sky_ui: stars, galaxy, Threshold, Basis), the ring x40
  where the ui scenario puts it relative to uicam, turning 0.25 deg/s, own sun + ambient; turns the scene behind the menu at first placement.
- `Game/Front/HaloMenuPanel.cs` - one menu page (logo, MULTIPLAYER/SETTINGS/CREDITS pictures; tablet in a match: SCORES/TEAM/MATCH),
  `HaloMasterLabel` (+view, crown).
- `Game/Front/HaloLobby.cs` - lobby state machine (master / pull-in / countdown / match id), `HaloGameTypes` (standard bank names +
  descriptions from ui.map), rules saved as `lobby.rules` (HaloLobby.Pack/Unpack).
- `Game/Front/HaloLobbyNet.cs` - **the contract with N**: `IHaloLobbyNet`, `HaloLobbyState`, `HaloLobbyPlayer`, `HaloLobbyNet.Net`.
- `Game/Front/HaloLobbyPage.cs` - lobby / map select (19 previews) / game type / rules screens, `HaloRuleRows` (ui.map option labels+values).
- `Game/Front/HaloSettingsUi.cs` - settings rows; **other modules register their options here** (see below).
- `Game/Front/HaloFront.cs` - conductor (Start/BootDone/Tick/Stop), pages, settings/about/in-game tabs, music, hub keep rule.
- `Game/Front/HaloLoading.cs` - HaloLoading (Show/Progress/Hide/Leave/BackToHub) + HaloLoadingScreen.
- `Game/Front/HaloTabletApp.cs`, `HaloTabletLink.cs` - "HALO" app (BO2 port).
- `Game/HaloGame.Front.cs` - the hooks. `Editor/HaloLabFront.cs` (front-hub, front-lobby, front-tablet), `Editor/LabStubs/*` (+asmdef,
  used by reflection: the lead's editor asmdef does not reference it).

## For other modules
- Settings: `HaloSettingsUi.Toggle("combat.x", "LABEL:", false)`, `Stepper(key, label, default, min, max, step, format, showScale)`,
  `Choice(key, label, options, default)` - call at your start; values in HaloSettings under your key.
- Built-in keys: `volume` (-> HaloAudio.MasterVolume), `front.music`, `haptics`, `lefthanded`, `install`, `lobby.rules`.
- Loading: M calls Show/Progress/Hide; `HaloLoading.Leave()` = fade to black before a teardown; F covers + fades into the hub by itself on
  PhaseChanged -> Hub (from Playing/Ending).
- Hub visibility: hidden on Playing/Ending, panel hidden on Loading, everything back on Hub.

## Status (2026-10-19)
- Harness verified: ui.map strings, map order (mp_map_grafix sheet = mp_map_list order), preview used size 140x115, font SDF render
  (png/ft*.png), ring placement (12.3,-1.8,15.5) rel. uicam; ring x40: nearest band 124 m, arcs to 79 deg elevation, farthest 4.7 km.
- Lab (DevTools/lab/front-*/, pictures looked at):
  - front-hub PASSED: ring + Threshold/Basis/galaxy/stars sky, platform, menu panel with the logo above, Halo fonts, settings, credits.
    Fix found: blended sky parts must be in queues > 2500 (the deferred G-buffer pass ignores blending below).
  - front-lobby PASSED: game type / map / rule clicks, START GAME -> countdown -> loading card -> Blood Gulch playing (hub hidden) ->
    LeaveMatch -> hub shown, cover fades in on the platform, lobby phase back to Lobby.
  - front-tablet PASSED: stand-in tablet found by type names, HALO button first, page in the frame, Small->Large, VR ray click on
    SETTINGS, Back, shutdown removes everything.
- NOT verified: the real Somnium client (tablet, VR ray on the hub panel, music audible), two-player lobby through N's LobbyLink
  (only offline/master path ran in the lab), the in-match tablet tabs (scores/team/match) visually, loading card on a monitor
  (ScreenSpaceOverlay is not in lab pictures).

## Phase 2: campaign (2026-10-19)
- `Game/Front/HaloCampaignPage.cs`: the CAMPAIGN tab (hub + tablet): 10 missions (map_list_oneline names, sp_levels pictures 140x115,
  solo_level_select\map_data descriptions; not installed -> lock picture 10 + "You cannot play this level... yet!"), difficulty
  (difficulty_options pictures 200x211 + strings + difficulty_descriptions), START MISSION / JOIN MISSION; the tablet's MISSION tab
  (RESUME = close the tablet, RESTART FROM CHECKPOINT = HaloCampaign.Revert, LEAVE MISSION = LeaveMission, master = end for everyone).
- Nav: CAMPAIGN (menu_load_campaign caption picture) first, then MULTIPLAYER / SETTINGS / CREDITS. Tabs Campaign = 6, Mission = 7.
- Lobby: `HaloLobby.CampaignMission` / `CampaignDifficulty` (settings campaign.mission / campaign.difficulty), `PressStartCampaign()`;
  the same countdown / InMatch / pull-in flow; `HaloLobbyState.Mission` + `Difficulty` (new fields, N carries them).
- Loading card: a campaign map shows its sp_levels picture, mission name, "CAMPAIGN - <DIFFICULTY>" and the level description.
- Lab scenario `front-campaign`.

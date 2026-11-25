using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// [Front module] Lab scenarios (DevTools/lab.sh):
    ///  front-hub     the ring hub: pictures from a monitor-like camera (the menu readable), from VR eye height (wide), looking up at the arc, behind, and from afar.
    ///  front-lobby   the lobby clicked through (game type, map, a rule), START GAME -> countdown -> the loading screen (its world-space card) -> back to the hub.
    ///                -halolobbymatch 0 skips the real match start.
    ///  front-tablet  the "HALO" tablet app against a stand-in of the client's tablet (Editor/LabStubs, found by type name like the real one): link, the home
    ///                button, the app's page, a click through the TrackedDeviceGraphicRaycaster, Back, shutdown.
    /// </summary>
    [InitializeOnLoad]
    public static class HaloLabFront
    {
        static HaloLabFront()
        {
            HaloLabRunner.Register("front-hub", r => Hub(r));
            HaloLabRunner.Register("front-lobby", r => Lobby(r));
            HaloLabRunner.Register("front-tablet", r => Tablet(r));
            HaloLabRunner.Register("front-campaign", r => Campaign(r));
            HaloLabRunner.Register("front-handover", r => Handover(r));
        }

        // ------------------------------------------------------------------ the master leaves mid-game (the user's rule: hosting moves on, the game goes on)

        private sealed class FakeNet : IHaloLobbyNet
        {
            public bool Master;
            public HaloLobbyState State;
            public bool Online { get { return true; } }
            public bool MasterKnown { get { return true; } }
            public bool IsMaster { get { return Master; } }
            public string MasterName { get { return Master ? "Lab" : "Bob"; } }
            public string LocalName { get { return "Lab"; } }
            public void Players(List<HaloLobbyPlayer> into)
            {
                into.Add(new HaloLobbyPlayer { Id = 1, Name = "Lab", Team = HaloTeams.Red, Me = true, Master = Master, InMatch = HaloSession.Current != HaloSession.Phase.Hub });
                if (!Master) into.Add(new HaloLobbyPlayer { Id = 2, Name = "Bob", Team = HaloTeams.Blue, Master = true, InMatch = true });
            }
            public void Publish(HaloLobbyState s) { if (Master) State = s; }
            public bool ReadMaster(out HaloLobbyState s) { s = State; return State.Rules != null; }
            public bool SetTeam(int team, out string why) { why = null; return true; }
        }

        private static IEnumerator Handover(HaloLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            IHaloLobbyNet real = HaloLobbyNet.Net;
            var fake = new FakeNet();
            HaloRules rules = HaloRules.Defaults(HaloScenario.GameType.Slayer, false);
            rules.MapName = "bloodgulch";
            fake.State = new HaloLobbyState { Phase = HaloLobbyState.InMatch, MatchId = 7, Version = 3, Rules = rules, StartUtc = HaloSession.UtcNow };
            HaloLobbyNet.Net = fake;
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Playing, 120f, "being pulled into Bob's match");
            r.Check(!HaloLobby.IsMaster && HaloSession.Playing, "pulled into the master Bob's running match");
            // Bob leaves the instance: the net makes me the master, Bob's last state is what was published
            fake.Master = true;
            yield return r.Frames(30);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 3f) yield return null;
            r.Check(HaloLobby.IsMaster && HaloMasterLabel.Mine, "I am the new instance master (crown label: " + HaloMasterLabel.Text + ")");
            r.Check(HaloSession.Playing, "the match goes on (phase " + HaloSession.Current + ")");
            r.Check(HaloLobby.ViewPhase == HaloLobbyState.InMatch && HaloLobby.ViewMatchId == 7 && fake.State.Phase == HaloLobbyState.InMatch && fake.State.MatchId == 7, "the lobby keeps the game running and publishes it as mine (phase " + fake.State.Phase + ", match " + fake.State.MatchId + ")");
            HaloMatchControl.LeaveMatch();
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Hub, 10f, "the way back");
            yield return r.Frames(30);
            r.Check(HaloLobby.ViewPhase == HaloLobbyState.Lobby, "with nobody left in the game it ends (phase " + HaloLobby.ViewPhase + ")");
            HaloLobbyNet.Net = real;
        }

        // ------------------------------------------------------------------ the campaign page (phase 2)

        private static IEnumerator Campaign(HaloLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            string mission0 = HaloLobby.CampaignMission;
            HaloCampaign.Difficulty diff0 = HaloLobby.CampaignDifficulty;
            HaloHub hub = HaloHub.Current;
            Transform page = hub.Menu.Root;
            Canvas panel = hub.Panel;
            Vector3 pc = panel.transform.position + panel.transform.up * (hub.Placer != null ? hub.Placer.CentreUp : 0f);
            hub.Menu.SetTab(HaloMenuPanel.Tab.Campaign, true);
            yield return r.Frames(10);
            r.Aim(Eye, pc, 58f);
            r.Shot("campaign_1_page", 1280, 720);
            yield return Press(r, page, "Mission a30");
            yield return Press(r, page, "Difficulty 3");
            r.Check(HaloLobby.CampaignMission == "a30" && HaloLobby.CampaignDifficulty == HaloCampaign.Difficulty.Legendary, "mission a30 on Legendary picked (" + HaloLobby.CampaignMission + ", " + HaloLobby.CampaignDifficulty + ")");
            yield return r.Frames(10);
            r.Aim(Eye + new Vector3(0f, 0.3f, 0.7f), pc + new Vector3(0f, 0.2f, 0f), 50f);
            r.Shot("campaign_2_a30_legendary", 1280, 720);

            // START MISSION -> the countdown -> HaloCampaign.StartMission (K's; the stub does nothing, so the lobby gives it up after its grace)
            yield return Press(r, page, "Campaign Start");
            r.Check(HaloLobby.ViewPhase == HaloLobbyState.Countdown && HaloLobby.ViewMission == "a30", "START MISSION begins the countdown for a30 (phase " + HaloLobby.ViewPhase + ", mission " + HaloLobby.ViewMission + ")");
            yield return r.Wait(() => HaloLobby.ViewPhase == HaloLobbyState.InMatch, 10f, "the mission start");
            HaloLabRunner.Log("after START MISSION: session " + HaloSession.Current + ", campaign active " + HaloCampaign.Active + ", mission " + HaloCampaign.Mission);
            float t0 = Time.realtimeSinceStartup;
            while (HaloSession.Current == HaloSession.Phase.Loading && Time.realtimeSinceStartup - t0 < 120f) yield return null;
            if (HaloCampaign.Active) { HaloCampaign.LeaveMission(); yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Hub, 15f, "the way back"); }
            yield return r.Wait(() => HaloLobby.ViewPhase == HaloLobbyState.Lobby, 15f, "the lobby back in its idle phase");

            // the campaign loading card (world screen) for a10
            HaloInstall.MapEntry a10 = HaloGame.Current.Install.Map("a10");
            if (r.Check(a10 != null, "a10.map is installed"))
            {
                HaloLoadingScreen.ForceWorld = true;
                HaloLoading.Show(a10, null);
                HaloLoading.Progress(0.4f, "Building The Pillar of Autumn");
                yield return r.Frames(90);
                HaloLoadingScreen ls = HaloLoadingScreen.Current;
                if (ls != null && ls.Card != null) { Transform c = ls.Card.transform; r.Aim(c.position - c.forward * 2.6f, c.position, 70f); r.Shot("campaign_3_loading", 1280, 720); }
                HaloLoading.Hide();
                HaloLoadingScreen.ForceWorld = false;
                yield return r.Frames(60);
            }

            // the tablet page's MISSION tab: a second page as the tablet builds it, switched to a running mission
            Canvas c2 = HaloUi.MakeCanvas("Lab Tablet Page", hub.transform, HaloMenuPanel.Width, HaloMenuPanel.Height, 0.0008f);
            c2.transform.position = Eye + new Vector3(0f, -0.1f, 0.9f);
            c2.transform.rotation = Quaternion.identity;
            HaloMenuPanel tp = HaloMenuPanel.Build(c2.transform, HaloMenuPanel.Width, HaloMenuPanel.Height, HaloMenuPanel.Tab.Multiplayer, "Lab tablet", "", false, true);
            HaloFront.AddPage(tp, true);
            yield return r.Frames(5);
            tp.SetGameMode(true, true);
            tp.RefreshAll();
            yield return r.Frames(10);
            r.Check(tp.Current == HaloMenuPanel.Tab.Mission && Find(tp.Root, "Mission Revert") != null && Find(tp.Root, "Mission Leave") != null, "in a mission the tablet page shows MISSION with RESTART FROM CHECKPOINT and LEAVE MISSION");
            r.Aim(Eye + new Vector3(0f, -0.1f, 0.05f), c2.transform.position, 70f);
            r.Shot("campaign_4_tablet_mission", 1280, 720);
            UnityEngine.Object.Destroy(c2.gameObject);

            HaloLobby.CampaignMission = mission0;
            HaloLobby.CampaignDifficulty = diff0;
        }

        private static IEnumerator Ready(HaloLabRunner r)
        {
            r.Cam.farClipPlane = 12000f;
            r.Cam.nearClipPlane = 0.05f;
            yield return r.Wait(() => HaloFront.Started && HaloFront.LobbyReady, 90f, "the front end (install + ui.map)");
            yield return r.Wait(() => HaloHub.Current != null && HaloHub.Current.RingBuilt && HaloHub.Current.SkyBuilt, 60f, "the ring and the sky");
            yield return r.Frames(30);
            HaloHub hub = HaloHub.Current;
            // the menu is placed from the lab camera at the hub's eye, facing +z (the hub turns the ring scene behind it)
            r.Aim(HaloSpace.RoomOrigin + new Vector3(0f, HaloHub.EyeHeight, 0f), HaloSpace.RoomOrigin + new Vector3(0f, HaloHub.EyeHeight, 10f), 60f);
            if (hub.Placer != null) hub.Placer.Place(r.Cam, "lab");
            yield return r.Frames(10);
            r.Check(HaloFonts.Large != null, "Halo's large_ui font built as a TextMeshPro font");
            r.Check(HaloUiArt.Texture("ui\\shell\\main_menu\\halo_logo", 0) != null, "the HALO logo picture read from ui.map");
            r.Check(HaloUiArt.Texture("ui\\shell\\bitmaps\\mp_map_grafix", 9) != null, "the map previews read (Blood Gulch = picture 9)");
        }

        private static Vector3 Eye { get { return HaloSpace.RoomOrigin + new Vector3(0f, HaloHub.EyeHeight, 0f); } }

        private static IEnumerator Hub(HaloLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            HaloHub hub = HaloHub.Current;
            Canvas panel = hub.Panel;
            Vector3 pc = panel.transform.position + panel.transform.up * (hub.Placer != null ? hub.Placer.CentreUp : 0f);
            HaloLabRunner.Log("panel at " + (panel.transform.position - HaloSpace.RoomOrigin).ToString("0.00") + ", ring scene rotation " + hub.Scene.rotation.eulerAngles.ToString("0"));
            r.Aim(Eye, pc, 60f);
            r.Shot("hub_monitor", 1280, 720);
            r.Aim(Eye + new Vector3(0f, 0f, -0.6f), Eye + new Vector3(0f, 1.2f, 10f), 100f);
            r.Shot("hub_vr_wide", 1280, 960);
            r.Aim(Eye, Eye + new Vector3(0f, 10f, 4f), 100f);
            r.Shot("hub_up", 1280, 960);
            r.Aim(Eye, Eye + new Vector3(0f, 2f, -10f), 90f);
            r.Shot("hub_behind", 1280, 720);
            r.Aim(Eye, Eye + new Vector3(10f, 1f, 0f), 90f);
            r.Shot("hub_side", 1280, 720);
            r.Aim(Eye + new Vector3(0f, 1500f, -6500f), Eye + new Vector3(0f, 1200f, 0f), 60f);
            r.Shot("hub_far", 1280, 720);
            // the settings and the credits page
            hub.Menu.SetTab(HaloMenuPanel.Tab.Settings, true);
            yield return r.Frames(5);
            r.Aim(Eye, pc, 60f);
            r.Shot("hub_settings", 1280, 720);
            hub.Menu.SetTab(HaloMenuPanel.Tab.About, true);
            yield return r.Frames(5);
            r.Shot("hub_about", 1280, 720);
            hub.Menu.SetTab(HaloMenuPanel.Tab.Multiplayer, true);
            yield return r.Frames(5);
            r.Aim(Eye + new Vector3(0f, 0.3f, 0.6f), pc + new Vector3(0f, 0.2f, 0f), 50f);
            r.Shot("hub_lobby_close", 1280, 720);
        }

        // ------------------------------------------------------------------ the lobby

        private static HaloButton Find(Transform root, string name)
        {
            if (root == null) return null;
            foreach (HaloButton b in root.GetComponentsInChildren<HaloButton>(true)) if (b.gameObject.name == name) return b;
            return null;
        }

        private static IEnumerator Press(HaloLabRunner r, Transform root, string name)
        {
            HaloButton b = Find(root, name);
            if (!r.Check(b != null, "button '" + name + "' exists")) yield break;
            b.Press();
            yield return r.Frames(8);
        }

        private static IEnumerator Lobby(HaloLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            HaloHub hub = HaloHub.Current;
            Transform page = hub.Menu.Root;
            Canvas panel = hub.Panel;
            Vector3 pc = panel.transform.position + panel.transform.up * (hub.Placer != null ? hub.Placer.CentreUp : 0f);
            r.Aim(Eye, pc, 58f);
            string map0 = HaloLobby.Mine.MapName;
            r.Check(HaloLobby.IsMaster, "offline: this client is the master");
            r.Shot("lobby_1_lobby", 1280, 720);

            yield return Press(r, page, "Lobby Game Type");
            r.Shot("lobby_2_types", 1280, 720);
            yield return Press(r, page, "Type Team Slayer");
            r.Check(HaloLobby.Mine.Type == HaloScenario.GameType.Slayer && HaloLobby.Mine.Teams && HaloLobby.Mine.ScoreLimit == 50, "Team Slayer picked: " + HaloLobby.Mine);
            yield return Press(r, page, "Types Back");

            yield return Press(r, page, "Lobby Change Map");
            r.Shot("lobby_3_maps", 1280, 720);
            string pick = HaloGame.Current.Install.Map("bloodgulch") != null ? "bloodgulch" : HaloGame.Current.Install.Multiplayer[0].Name;
            yield return Press(r, page, "Map " + pick);
            r.Check(HaloLobby.Mine.MapName == pick, "map " + pick + " picked (was " + map0 + ")");

            yield return Press(r, page, "Lobby Rules");
            int s0 = HaloLobby.Mine.ScoreLimit;
            yield return Press(r, page, "Rule score >");
            r.Check(HaloLobby.Mine.ScoreLimit != s0, "the score rule stepped: " + s0 + " -> " + HaloLobby.Mine.ScoreLimit);
            yield return Press(r, page, "Rule score <");
            yield return r.Frames(10);
            r.Shot("lobby_4_rules", 1280, 720);
            yield return Press(r, page, "Rules Back");
            yield return r.Frames(10);
            r.Shot("lobby_5_ready", 1280, 720);

            if (HaloLabBoot.Arg("-halolobbymatch", "1") == "0") yield break;
            HaloLoadingScreen.ForceWorld = true;
            yield return Press(r, page, "Lobby Start");
            r.Check(HaloLobby.ViewPhase == HaloLobbyState.Countdown, "START GAME begins the countdown");
            yield return r.Frames(20);
            r.Shot("lobby_6_countdown", 1280, 720);
            yield return r.Wait(() => HaloSession.Current != HaloSession.Phase.Hub, 15f, "the match to start loading");
            r.Check(HaloLoading.Showing, "the loading screen shows");
            yield return r.Frames(60);
            HaloLoadingScreen ls = HaloLoadingScreen.Current;
            if (ls != null && ls.Card != null)
            {
                Transform c = ls.Card.transform;
                r.Aim(c.position - c.forward * 2.6f, c.position, 70f);
                r.Shot("lobby_7_loading", 1280, 720);
            }

            float t0 = Time.realtimeSinceStartup;
            while (HaloSession.Current == HaloSession.Phase.Loading && Time.realtimeSinceStartup - t0 < 240f)
            {
                if (ls != null && ls.Card != null && Time.realtimeSinceStartup - t0 > 8f && Time.realtimeSinceStartup - t0 < 9f) { Transform c = ls.Card.transform; r.Aim(c.position - c.forward * 2.6f, c.position, 70f); r.Shot("lobby_8_loading_later", 1280, 720); t0 -= 1.1f; }
                yield return null;
            }

            HaloLabRunner.Log("after loading: phase " + HaloSession.Current + ", hub shown " + hub.Shown + ", lobby phase " + HaloLobby.ViewPhase);
            r.Check(HaloSession.Current == HaloSession.Phase.Playing && !hub.Shown, "playing: the hub is hidden");
            yield return r.Frames(30);
            HaloMatchControl.LeaveMatch();
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Hub, 10f, "the way back to the hub");
            yield return r.Frames(10);
            HaloLoadingScreen cov = HaloLoadingScreen.Current;
            HaloLabRunner.Log("back: cover " + (cov != null ? cov.CoverAlpha.ToString("0.00") : "gone"));
            r.Check(HaloSession.Current == HaloSession.Phase.Hub && hub.Shown, "left: back in the hub, shown");
            yield return r.Frames(30);
            r.Check(HaloLobby.ViewPhase == HaloLobbyState.Lobby, "the lobby is open again (phase " + HaloLobby.ViewPhase + ")");
            HaloLoadingScreen.ForceWorld = false;
        }

        // ------------------------------------------------------------------ the tablet (LabStubs, by reflection: the editor assembly does not reference them)

        private static Type Stub(string name) { return Type.GetType("SomniumSpace.Worlds.SomniumHalo.LabStubs." + name + ", SomniumHalo.LabStubs"); }

        private sealed class Fake { public GameObject Root; public Component Control; public CanvasGroup Group; public Canvas Home; public RectTransform HomeRect; public Transform Grid; public Button Back, Resize; public int Resizes; }

        private static Fake BuildFake(Vector3 at, Camera cam)
        {
            var f = new Fake();
            f.Root = new GameObject("Tablet.XR(Clone)");
            f.Root.transform.position = at;
            f.Control = f.Root.AddComponent(Stub("TabletControl"));
            var canvases = new GameObject("Canvases", typeof(RectTransform));
            canvases.transform.SetParent(f.Root.transform, false);
            f.Group = canvases.AddComponent<CanvasGroup>();
            canvases.transform.localScale = Vector3.one * 0.0004f;
            f.Control.GetType().GetField("Group").SetValue(f.Control, f.Group);
            f.Control.GetType().GetMethod("Activate").Invoke(f.Control, null);
            var tabs = new GameObject("Tablet.XR.Tabs", typeof(RectTransform));
            tabs.transform.SetParent(canvases.transform, false);
            tabs.AddComponent(Stub("TabsContainer"));
            var home = new GameObject("Tablet.XR.HomeTab", typeof(RectTransform));
            home.layer = 9;
            home.transform.SetParent(tabs.transform, false);
            home.AddComponent(Stub("HomeTab"));
            f.Home = home.AddComponent<Canvas>();
            f.Home.renderMode = RenderMode.WorldSpace;
            f.Home.worldCamera = cam;
            home.AddComponent<TrackedDeviceGraphicRaycaster>();
            f.HomeRect = (RectTransform)home.transform;
            f.HomeRect.sizeDelta = new Vector2(960f, 1080f);
            home.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 1f);
            var content = new GameObject("ContentView", typeof(RectTransform));
            content.transform.SetParent(home.transform, false);
            var cr = (RectTransform)content.transform; cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one; cr.offsetMin = cr.offsetMax = Vector2.zero;
            var grid = new GameObject("Grid", typeof(RectTransform));
            grid.transform.SetParent(content.transform, false);
            var gr = (RectTransform)grid.transform; gr.anchorMin = Vector2.zero; gr.anchorMax = Vector2.one; gr.offsetMin = new Vector2(20f, 20f); gr.offsetMax = new Vector2(-20f, -20f);
            var layout = grid.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(220f, 250f);
            layout.spacing = new Vector2(16f, 16f);
            f.Grid = grid.transform;
            foreach (string a in new[] { "WORLDS", "AVATARS", "SOCIAL", "MAP", "CAMERA", "OPTIONS" })
            {
                var go = new GameObject("App." + a, typeof(RectTransform), typeof(Image), typeof(Button));
                go.layer = 5;
                go.transform.SetParent(f.Grid, false);
                go.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 1f);
                go.AddComponent(Stub("SwitchTabButton"));
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                icon.transform.SetParent(go.transform, false);
                ((RectTransform)icon.transform).sizeDelta = new Vector2(150f, 150f);
                var cap = new GameObject("Caption", typeof(RectTransform));
                cap.transform.SetParent(go.transform, false);
                ((RectTransform)cap.transform).anchoredPosition = new Vector2(0f, -100f);
                var t = cap.AddComponent<TextMeshProUGUI>();
                t.text = a; t.fontSize = 30f; t.alignment = TextAlignmentOptions.Center;
            }

            var worlds = new GameObject("Tab.Worlds", typeof(RectTransform));
            worlds.transform.SetParent(tabs.transform, false);
            var wc = new GameObject("ContentView", typeof(RectTransform));
            wc.transform.SetParent(worlds.transform, false);
            wc.SetActive(false);
            var bar = new GameObject("Tablet.XR.StatusBar", typeof(RectTransform));
            bar.transform.SetParent(f.Root.transform, false);
            bar.AddComponent(Stub("TabletStatusBarView"));
            f.Back = new GameObject("Tablet.XR.StatusBar.Back", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            f.Back.transform.SetParent(bar.transform, false);
            f.Back.gameObject.SetActive(false);
            f.Resize = new GameObject("Tablet.XR.StatusBar.Resize", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
            f.Resize.transform.SetParent(bar.transform, false);
            Fake ff = f;
            f.Resize.onClick.AddListener(() => { ff.Resizes++; ff.HomeRect.sizeDelta = new Vector2(ff.HomeRect.sizeDelta.x > 1440f ? 960f : 1920f, 1080f); });
            var meshHome = new GameObject("HomeButton");
            meshHome.transform.SetParent(f.Root.transform, false);
            meshHome.AddComponent(Stub("XRInteractableButton"));
            return f;
        }

        private static readonly List<RaycastResult> Hits = new List<RaycastResult>();

        /// <summary>The VR laser: a ray from a hand below the camera through the widget, tested by the canvas's TrackedDeviceGraphicRaycaster; the top hit is clicked.</summary>
        private static GameObject RayClick(Camera cam, Canvas canvas, Component widget)
        {
            EventSystem es = EventSystem.current != null ? EventSystem.current : UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (es == null) es = new GameObject("Lab EventSystem").AddComponent<EventSystem>();
            var raycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (raycaster == null) return null;
            var rt = (RectTransform)widget.transform;
            Vector3 target = rt.TransformPoint(rt.rect.center);
            Vector3 from = cam.transform.position + cam.transform.right * 0.15f - cam.transform.up * 0.25f;
            Vector3 dir = (target - from).normalized;
            var data = new TrackedDeviceEventData(es) { rayPoints = new List<Vector3> { from, target + dir * 0.3f }, layerMask = ~0, pointerId = 7 };
            Hits.Clear();
            raycaster.Raycast(data, Hits);
            GameObject hit = Hits.Count > 0 ? Hits[0].gameObject : null;
            if (hit == null) return null;
            var pd = new TrackedDeviceEventData(es) { pointerId = 7, button = PointerEventData.InputButton.Left };
            ExecuteEvents.ExecuteHierarchy(hit, pd, ExecuteEvents.pointerEnterHandler);
            GameObject down = ExecuteEvents.ExecuteHierarchy(hit, pd, ExecuteEvents.pointerDownHandler);
            pd.pointerPress = down;
            ExecuteEvents.ExecuteHierarchy(hit, pd, ExecuteEvents.pointerUpHandler);
            GameObject click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            if (click != null && click == down) ExecuteEvents.Execute(click, pd, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pd, ExecuteEvents.pointerExitHandler);
            HaloLabRunner.Log("ray at '" + widget.name + "': top graphic '" + hit.name + "', click taken by '" + (click != null && click == down ? click.name : "nobody") + "'");
            return click != null && click == down ? click : null;
        }

        private static IEnumerator Tablet(HaloLabRunner r)
        {
            yield return Ready(r);
            if (r.Failed) yield break;
            if (!r.Check(Stub("TabletControl") != null, "the LabStubs assembly is loaded")) yield break;
            HaloTabletApp app = HaloTabletApp.Instance;
            if (!r.Check(app != null && app.Link != null, "the tablet app exists (HaloFront.Start)")) yield break;
            HaloTabletLink link = app.Link;
            link.SearchInterval = 0.2f;
            r.Check(!link.IsLinked, "no tablet yet: not linked");
            Vector3 at = Eye + new Vector3(0f, -0.05f, 0.6f);
            Fake f = BuildFake(at, r.Cam);
            yield return r.Wait(() => link.IsLinked, 20f, "the link to the stand-in tablet");
            if (!link.IsLinked) yield break;
            HaloLabRunner.Log("link report: " + link.Report);
            yield return r.Frames(40);
            Button ours = null;
            foreach (Transform c in f.Grid) if (c.name == HaloTabletLink.Owned + "Button") ours = c.GetComponent<Button>();
            r.Check(ours != null && ours.transform.GetSiblingIndex() == 0, "the HALO button is the first of the home grid");
            TMP_Text cap = ours != null ? ours.GetComponentInChildren<TMP_Text>(true) : null;
            r.Check(cap != null && cap.text == "HALO", "its caption reads HALO");
            r.Check(app.Page != null && app.Page.Root != null && app.Page.Root.IsChildOf(link.Frame), "the menu page is built into the app's frame");
            r.Aim(at + new Vector3(0f, 0.02f, -0.45f), at, 60f);
            r.Shot("tablet_1_home", 1280, 720);
            if (ours != null) ours.onClick.Invoke();
            yield return r.Frames(20);
            r.Check(link.AppOpen, "the app opens from its home button");
            r.Check(f.Resizes == 1, "a Small tablet was made Large for the wide page (" + f.Resizes + " presses)");
            yield return r.Frames(20);
            r.Aim(at + new Vector3(0f, 0.02f, -0.55f), at, 60f);
            r.Shot("tablet_2_app", 1280, 720);

            // a ray click on the SETTINGS item of the tablet's page
            HaloButton settings = null;
            foreach (HaloButton b in app.Page.Root.GetComponentsInChildren<HaloButton>(true)) if (b.gameObject.name.StartsWith("Menu item SETTINGS")) settings = b;
            if (r.Check(settings != null, "the page has the SETTINGS item"))
            {
                GameObject taken = RayClick(r.Cam, link.AppCanvas, settings);
                yield return r.Frames(5);
                r.Check(taken != null && app.Page.Current == HaloMenuPanel.Tab.Settings, "a VR ray click on SETTINGS opens the settings (tab " + app.Page.Current + ")");
                r.Shot("tablet_3_settings", 1280, 720);
            }

            f.Back.onClick.Invoke();
            yield return r.Frames(10);
            r.Check(!link.AppOpen && f.Resizes == 2, "Back closes the app and gives the tablet its size back");
            HaloTabletApp.Shutdown();
            yield return r.Frames(5);
            int left = 0;
            foreach (Transform c in f.Grid) if (c.name.StartsWith(HaloTabletLink.Owned)) left++;
            r.Check(left == 0, "shutdown takes the HALO button off the client's tablet (" + left + " left)");
            UnityEngine.Object.Destroy(f.Root);
        }
    }
}

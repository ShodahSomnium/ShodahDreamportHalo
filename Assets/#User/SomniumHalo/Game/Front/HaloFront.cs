using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] The front end's conductor, started only from the HaloGame Front* hooks (so only for a whitelisted player): the ring hub and its
    /// menu panel (HaloHub, HaloMenuPanel), the "HALO" Somnium tablet app (HaloTabletApp), ui.map's art (HaloUiArt), the lobby (HaloLobby,
    /// HaloLobbyPage), the settings and about pages, the in-game tabs of the tablet (scores, team, match), the menu music, the loading screen's
    /// hand-offs (HaloLoading) and the hub's keep rule (a player off the platform in the hub is put back on it).
    /// Every menu page (the hub's, the tablet's) is filled by <see cref="FillPage"/> - one implementation.
    /// </summary>
    public static class HaloFront
    {
        private static HaloGame _game;
        private static HaloHub _hub;
        private static HaloTabletApp _app;
        private static bool _started, _hubFilled;
        private static readonly List<PageUi> _pages = new List<PageUi>();
        private static bool _musicOn;
        private static bool _musicWanted = true;
        private static float _awaySince = -1f, _nextKeep;
        private static int _keepMoves;

        private sealed class PageUi
        {
            public HaloMenuPanel Panel;
            public bool Tablet, LobbyBuilt;
            public int SettingsVersion = -1;
            public HaloLobbyPage Lobby;
            public HaloCampaignPage Campaign;
            public TextMeshProUGUI ScoreHead, ScoreInfo, TeamLine, MatchLine;
            public readonly TextMeshProUGUI[] ScoreRows = new TextMeshProUGUI[16], ScoreK = new TextMeshProUGUI[16], ScoreD = new TextMeshProUGUI[16], ScoreS = new TextMeshProUGUI[16];
            public HaloButton Red, Blue, LeaveBtn, EndBtn;
            public readonly List<KeyValuePair<HaloButton, string>> MapBtns = new List<KeyValuePair<HaloButton, string>>();
            public string Armed;
            public float ArmedAt = -100f, NextScore;
        }

        public static HaloHub Hub { get { return _hub; } }
        public static HaloTabletApp App { get { return _app; } }
        public static bool Started { get { return _started; } }
        /// <summary>The hub's pages are filled with the lobby (the install and ui.map are read).</summary>
        public static bool LobbyReady { get { return _hubFilled; } }

        // ------------------------------------------------------------------ the hooks

        public static void Start(HaloGame game)
        {
            if (_started) return;
            _started = true;
            _game = game;
            HaloSettings.Load();
            HaloAudio.MasterVolume = HaloSettings.Volume;
            HaloSettings.Changed += OnSetting;
            HaloPlayer.EnsureEditorEventSystem();
            HaloLobby.Init();

            _hub = HaloHub.Create(game.transform);
            HaloMenuPanel menu = _hub.BuildPanel(HaloMenuPanel.Tab.Multiplayer, game.Status, game.StatusIsError);
            AddPage(menu, false);
            game.StatusChanged += OnStatus;
            HaloSession.PhaseChanged += OnPhase;

            try { _app = HaloTabletApp.Create(game, _hub); }
            catch (Exception e) { HaloLog.Warn("Tablet: the app could not start (" + e.GetType().Name + ": " + e.Message + ") - the hub panel is the menu."); }
            HaloLog.Info("Front: the ring hub and its menu are up (whitelisted player).");
        }

        public static void BootDone()
        {
            if (_game == null) return;
            HaloUiArt.StartLoad(_game.Install);
        }

        public static void Tick()
        {
            if (!_started) return;
            HaloSettings.Tick();
            HaloUiArt.Tick();
            HaloLobby.Tick();

            bool canFill = _game != null && _game.Install != null && (HaloUiArt.Ready || HaloUiArt.Error != null);
            for (int i = _pages.Count - 1; i >= 0; i--)
            {
                PageUi p = _pages[i];
                if (p.Panel == null || p.Panel.Root == null) { _pages.RemoveAt(i); continue; }
                if (!p.LobbyBuilt && canFill) FillLobby(p);
                if (p.SettingsVersion != HaloSettingsUi.Version) BuildSettings(p);
                if (p.Tablet) TickGameTabs(p);
            }

            if (!_hubFilled && canFill) _hubFilled = true;
            HaloLobbyPage.TickAll();
            HaloCampaignPage.TickAll();
            TickMusic();
            TickKeep();
        }

        public static void Stop()
        {
            if (!_started) return;
            _started = false;
            StopMusic(0f);
            HaloSettings.Changed -= OnSetting;
            HaloSession.PhaseChanged -= OnPhase;
            if (_game != null) _game.StatusChanged -= OnStatus;
            HaloTabletApp.Shutdown();
            HaloUiArt.Shutdown();
            HaloSettings.Save();
            _pages.Clear();
            HaloLobbyPage.Pages.Clear();
            if (_hub != null) UnityEngine.Object.Destroy(_hub.gameObject);
            _hub = null;
            _app = null;
            _game = null;
            _hubFilled = false;
        }

        // ------------------------------------------------------------------ pages

        /// <summary>A menu page joins the front end (the hub's panel; the tablet app's page): its tabs are filled now and when ui.map is read.</summary>
        public static void AddPage(HaloMenuPanel panel, bool tablet)
        {
            if (panel == null) return;
            var p = new PageUi { Panel = panel, Tablet = tablet };
            _pages.Add(p);
            BuildSettings(p);
            BuildAbout(p);
            if (tablet) BuildGameTabs(p);
            if (_game != null && _game.Install != null && (HaloUiArt.Ready || HaloUiArt.Error != null)) FillLobby(p);
            else HaloUi.OneLine(HaloUi.Text(panel.MultiplayerRoot, "Waiting", "Reading your Halo install...", 28f, HaloUi.Blue, TextAlignmentOptions.Center, 0f, 300f, HaloMenuPanel.ContentW, 60f, true));
            panel.SetStatus(_game != null ? _game.Status : "", _game != null && _game.StatusIsError);
        }

        private static void FillLobby(PageUi p)
        {
            p.LobbyBuilt = true;
            Transform waiting = p.Panel.MultiplayerRoot.Find("Waiting");
            if (waiting != null) UnityEngine.Object.Destroy(waiting.gameObject);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { p.Lobby = new HaloLobbyPage(p.Panel); }
            catch (Exception e) { HaloLog.Error("Front: building the lobby failed: " + e); }
            try { p.Campaign = new HaloCampaignPage(p.Panel); }
            catch (Exception e) { HaloLog.Error("Front: building the campaign page failed: " + e); }
            if (p.Tablet) HaloCampaignPage.BuildMissionTab(p.Panel, Resume);
            if (p.Tablet) BuildMatchMaps(p);
            BuildAbout(p);
            HaloLog.Info("Front: the " + (p.Tablet ? "tablet's" : "hub's") + " lobby was built in " + watch.ElapsedMilliseconds + " ms (" + HaloLobbyPage.MapList().Count + " maps, ui.map " + (HaloUiArt.Ready ? "read" : "not read: " + HaloUiArt.Error) + ").");
        }

        public static void Say(string text, bool error)
        {
            if (_game != null) _game.SetStatus(text, error);
        }

        private static void OnStatus(string text, bool error)
        {
            for (int i = 0; i < _pages.Count; i++) if (_pages[i].Panel != null) _pages[i].Panel.SetStatus(text, error);
        }

        private static bool _lastWasCampaign;

        /// <summary>The tablet's RESUME: the app and the client's tablet close.</summary>
        private static void Resume()
        {
            HaloTabletApp app = HaloTabletApp.Instance;
            if (app != null && app.Link != null) app.Link.CloseTablet("resume");
        }

        private static void OnPhase(HaloSession.Phase from, HaloSession.Phase to)
        {
            if (_hub == null) return;
            if (to == HaloSession.Phase.Playing) _lastWasCampaign = HaloCampaign.Active;
            if (to == HaloSession.Phase.Loading)
            {
                _hub.SetPanelShown(false);
                StopMusic(1.5f);
            }
            else if (to == HaloSession.Phase.Playing || to == HaloSession.Phase.Ending)
            {
                _hub.SetShown(false);
            }
            else if (to == HaloSession.Phase.Hub)
            {
                bool fromMatch = from == HaloSession.Phase.Playing || from == HaloSession.Phase.Ending;
                _hub.SetShown(true);
                _hub.SetPanelShown(true);
                if (_hub.Placer != null) _hub.Placer.Replace();
                if (_hub.Menu != null) _hub.Menu.SetTab(_lastWasCampaign ? HaloMenuPanel.Tab.Campaign : HaloMenuPanel.Tab.Multiplayer, false);
                if (fromMatch) HaloLoading.BackToHub();
                _musicWanted = true;
            }
        }

        private static void OnSetting(string key)
        {
            if (string.Equals(key, "volume", StringComparison.OrdinalIgnoreCase)) HaloAudio.MasterVolume = HaloSettings.Volume;
            if (string.Equals(key, "front.music", StringComparison.OrdinalIgnoreCase) && !HaloSettings.Flag("front.music", true)) StopMusic(0.5f);
        }

        // ------------------------------------------------------------------ settings

        private static void BuildSettings(PageUi p)
        {
            p.SettingsVersion = HaloSettingsUi.Version;
            RectTransform root = p.Panel.SettingsRoot;
            for (int i = root.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(root.GetChild(i).gameObject);
            RawImage h = HaloUi.Picture(root, "Header", -10f, -6f, 640f, 74f, "ui\\shell\\main_menu\\settings_select\\player_setup\\player_profile_edit\\header_edit_profile_settings", 0, 512, 59, Color.white);
            GameObject ht = HaloUi.OneLine(HaloUi.Text(root, "Header text", "SETTINGS", 34f, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, 30f, 0f, 560f, 60f, true)).gameObject;
            h.gameObject.AddComponent<HaloHideTextWhenPicture>().Text = ht;
            HaloUi.OneLine(HaloUi.Text(root, "Hint", "Saved on this computer.", 18f, HaloUi.Grey, TextAlignmentOptions.Left, 0f, 66f, 900f, 24f, false), 10f);

            List<HaloSettingsUi.Row> rows = HaloSettingsUi.Rows;
            for (int i = 0; i < rows.Count && i < 24; i++)
            {
                HaloSettingsUi.Row row = rows[i];
                int col = i < 12 ? 0 : 1, line = i < 12 ? i : i - 12;
                float x = col * 480f, y = 96f + line * 45f;
                RectTransform r = HaloUi.Place(root, "Setting " + row.Key, x, y, 470f, 42f);
                HaloUi.OneLine(HaloUi.Text(r, "Label", row.Label, 20f, HaloUi.Blue, TextAlignmentOptions.Left, 0f, 0f, 250f, 42f, true), 9f);
                TextMeshProUGUI value = HaloUi.OneLine(HaloUi.Text(r, "Value", row.Value(), 21f, Color.white, TextAlignmentOptions.Left, 254f, 0f, 120f, 42f, true), 9f);
                HaloSettingsUi.Row captured = row;
                HaloUi.Button(r, "<", 376f, 2f, 44f, 38f, 22f, () => { captured.Change(-1); value.text = captured.Value(); }).gameObject.name = "Setting " + row.Key + " <";
                HaloUi.Button(r, ">", 424f, 2f, 44f, 38f, 22f, () => { captured.Change(1); value.text = captured.Value(); }).gameObject.name = "Setting " + row.Key + " >";
                p.Panel.AddRefresh(() => { if (value != null) value.text = captured.Value(); });
            }

            // the Halo folder (HaloSettings "install"): read at the next visit
            HaloUi.Panel(root, "Folder panel", 0f, 652f, 950f, 104f);
            HaloUi.OneLine(HaloUi.Text(root, "Folder label", "HALO FOLDER:", 17f, HaloUi.Blue, TextAlignmentOptions.Left, 16f, 660f, 200f, 30f, true));
            string now = _game != null && _game.Install != null ? _game.Install.Root : "not found";
            TextMeshProUGUI cur = HaloUi.OneLine(HaloUi.Text(root, "Folder now", "In use: " + now + (string.IsNullOrEmpty(HaloSettings.InstallOverride) ? "" : "   (set: " + HaloSettings.InstallOverride + ")"), 15f, HaloUi.BlueBright, TextAlignmentOptions.Left, 200f, 660f, 734f, 30f, false), 9f);
            TMP_InputField field = InputField(root, "Folder field", 16f, 698f, 560f, 44f, HaloSettings.InstallOverride);
            HaloUi.Button(root, "SET", 590f, 696f, 150f, 48f, 20f, () =>
            {
                string typed = field != null ? field.text.Trim() : "";
                // checked now (a click, so a short disk read is fine): the install's own folder is what gets saved, however it was written
                string why = "type the folder with halo.exe and MAPS";
                HaloInstall found = string.IsNullOrEmpty(typed) ? null : HaloInstall.FromFolder(typed, out why);
                if (found == null)
                {
                    HaloLog.Warn("Settings: Halo folder '" + typed + "' not used: " + why);
                    cur.text = "In use: " + now + "   (not set: " + why + ")";
                    Say("That folder was not saved: " + why + ".", true);
                    return;
                }
                HaloSettings.InstallOverride = found.Root;
                HaloSettings.Save();
                if (field != null) field.text = found.Root;
                HaloLog.Info("Settings: Halo folder set to " + found.Root + " (" + found.Multiplayer.Count + " multiplayer maps)");
                if (_game != null && _game.Install == null && _game.Rescan())
                {
                    cur.text = "In use: " + found.Root + "   (loading it now)";
                    Say("Halo found in " + found.Root + ": loading it now.", false);
                }
                else
                {
                    cur.text = "In use: " + now + "   (set: " + found.Root + " - used the next time you enter the world)";
                    Say("Halo found in " + found.Root + ": saved, used the next time you enter the world.", false);
                }
            }).gameObject.name = "Folder Set";
            HaloUi.Button(root, "DEFAULT", 750f, 696f, 180f, 48f, 20f, () =>
            {
                HaloSettings.InstallOverride = "";
                HaloSettings.Save();
                if (field != null) field.text = "";
                cur.text = "In use: " + now;
                Say("The Halo folder is looked for in the usual places the next time you enter the world.", false);
            }).gameObject.name = "Folder Default";
        }

        private static TMP_InputField InputField(Transform parent, string name, float x, float y, float w, float h, string text)
        {
            try
            {
                RectTransform r = HaloUi.Place(parent, name, x, y, w, h);
                Image bg = r.gameObject.AddComponent<Image>();
                bg.color = new Color(0f, 0.06f, 0.14f, 1f);
                RectTransform area = HaloUi.Place(r, "Text Area", 10f, 4f, w - 20f, h - 8f);
                area.gameObject.AddComponent<RectMask2D>();
                TextMeshProUGUI ph = HaloUi.OneLine(HaloUi.Text(area, "Placeholder", "e.g. D:\\Games\\Halo", 17f, HaloUi.BlueDim, TextAlignmentOptions.MidlineLeft, 0f, 0f, w - 20f, h - 8f, false));
                TextMeshProUGUI t = HaloUi.OneLine(HaloUi.Text(area, "Text", "", 17f, Color.white, TextAlignmentOptions.MidlineLeft, 0f, 0f, w - 20f, h - 8f, false));
                t.raycastTarget = false;
                var f = r.gameObject.AddComponent<TMP_InputField>();
                f.textViewport = area;
                f.textComponent = t;
                f.placeholder = ph;
                f.targetGraphic = bg;
                f.lineType = TMP_InputField.LineType.SingleLine;
                f.text = text ?? "";
                return f;
            }
            catch (Exception e)
            {
                HaloLog.Warn("Front: the folder field could not be made: " + e.Message);
                return null;
            }
        }

        // ------------------------------------------------------------------ about

        private static void BuildAbout(PageUi p)
        {
            RectTransform root = p.Panel.AboutRoot;
            for (int i = root.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(root.GetChild(i).gameObject);
            HaloUi.OneLine(HaloUi.Text(root, "Title", "HOW TO PLAY", 34f, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, 30f, 0f, 560f, 60f, true));
            string text =
                "MULTIPLAYER: the instance master picks the game type, the map and the rules in the lobby and presses START GAME; everybody in the instance is pulled into the game, " +
                "and back into the lobby when it ends. Everyone else picks a team (team games) and waits.\n\n" +
                "In a headset your weapons are carried on your body: reach for one and squeeze the GRIP to take it; TRIGGER fires. On a monitor: mouse to aim and fire.\n\n" +
                "The Somnium tablet holds the same menu (the HALO app on its home screen): in a game it shows the SCORES, lets you change TEAM and leave the game; " +
                "the instance master can end the game or start a new one on another map.\n\n" +
                "Back to this ring from a game on a monitor: " + HaloPlayer.BackKeyName + ".\n\n" +
                "Everything you see and hear - the maps, the weapons, the sounds, this menu's pictures and fonts - is read from your own Halo: Combat Evolved for PC " +
                "(MAPS\\*.map). Nothing of the game is in this world.";
            var t = HaloUi.Text(root, "Text", text, 21f, HaloUi.BlueBright, TextAlignmentOptions.TopLeft, 0f, 78f, 940f, 600f, false);
            t.overflowMode = TextOverflowModes.Overflow;
            HaloUi.OneLine(HaloUi.Text(root, "Credits", "Halo: Combat Evolved (c) Microsoft / Bungie. A Somnium Space Dreamport.", 16f, HaloUi.Grey, TextAlignmentOptions.Left, 0f, 720f, 940f, 26f, false), 9f);
        }

        // ------------------------------------------------------------------ the tablet's in-game tabs

        private static void BuildGameTabs(PageUi p)
        {
            RectTransform s = p.Panel.ScoreRoot;
            p.ScoreHead = HaloUi.OneLine(HaloUi.Text(s, "Head", "SCORES", 34f, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, 30f, 0f, 560f, 60f, true));
            p.ScoreInfo = HaloUi.OneLine(HaloUi.Text(s, "Info", "", 20f, HaloUi.BlueBright, TextAlignmentOptions.Right, 420f, 12f, 530f, 34f, true), 10f);
            HaloUi.Panel(s, "Board", 0f, 66f, 950f, 690f);
            HaloUi.Text(s, "Col player", "PLAYER", 16f, HaloUi.Grey, TextAlignmentOptions.Left, 20f, 74f, 500f, 26f, false);
            HaloUi.Text(s, "Col kills", "KILLS", 16f, HaloUi.Grey, TextAlignmentOptions.Right, 560f, 74f, 110f, 26f, false);
            HaloUi.Text(s, "Col deaths", "DEATHS", 16f, HaloUi.Grey, TextAlignmentOptions.Right, 690f, 74f, 110f, 26f, false);
            HaloUi.Text(s, "Col score", "SCORE", 16f, HaloUi.Grey, TextAlignmentOptions.Right, 820f, 74f, 110f, 26f, false);
            for (int i = 0; i < p.ScoreRows.Length; i++)
            {
                float y = 104f + i * 40f;
                p.ScoreRows[i] = HaloUi.OneLine(HaloUi.Text(s, "Row " + i, "", 20f, Color.white, TextAlignmentOptions.Left, 20f, y, 530f, 38f, false), 10f);
                p.ScoreK[i] = HaloUi.OneLine(HaloUi.Text(s, "Kills " + i, "", 20f, Color.white, TextAlignmentOptions.Right, 560f, y, 110f, 38f, false));
                p.ScoreD[i] = HaloUi.OneLine(HaloUi.Text(s, "Deaths " + i, "", 20f, Color.white, TextAlignmentOptions.Right, 690f, y, 110f, 38f, false));
                p.ScoreS[i] = HaloUi.OneLine(HaloUi.Text(s, "Score " + i, "", 20f, Color.white, TextAlignmentOptions.Right, 820f, y, 110f, 38f, false));
            }

            RectTransform t = p.Panel.TeamRoot;
            HaloUi.OneLine(HaloUi.Text(t, "Head", "TEAM", 34f, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, 30f, 0f, 560f, 60f, true));
            p.TeamLine = HaloUi.Text(t, "Line", "", 26f, Color.white, TextAlignmentOptions.TopLeft, 0f, 90f, 940f, 120f, true);
            p.Red = HaloUi.Button(t, HaloUiArt.S("pregame\\teams", 0, "Red Team").ToUpperInvariant(), 0f, 230f, 420f, 70f, 28f, () => SwitchInMatch(HaloTeams.Red));
            p.Blue = HaloUi.Button(t, HaloUiArt.S("pregame\\teams", 1, "Blue Team").ToUpperInvariant(), 440f, 230f, 420f, 70f, 28f, () => SwitchInMatch(HaloTeams.Blue));
            p.Red.gameObject.name = "Team Red";
            p.Blue.gameObject.name = "Team Blue";

            RectTransform m = p.Panel.MatchRoot;
            HaloUi.OneLine(HaloUi.Text(m, "Head", "MATCH", 34f, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, 30f, 0f, 560f, 60f, true));
            p.MatchLine = HaloUi.Text(m, "Line", "", 22f, HaloUi.BlueBright, TextAlignmentOptions.TopLeft, 0f, 70f, 940f, 70f, false);
            p.LeaveBtn = HaloUi.Button(m, "LEAVE GAME", 0f, 150f, 300f, 60f, 24f, () => { HaloLog.Info("Front: the player leaves the game (tablet)."); HaloMatchControl.LeaveMatch(); /* (M fades out through HaloLoading.Leave) */ });
            p.EndBtn = HaloUi.Button(m, "END GAME FOR EVERYONE", 320f, 150f, 420f, 60f, 24f, () => HaloLobby.EndForEveryone());
            p.LeaveBtn.gameObject.name = "Match Leave";
            p.EndBtn.gameObject.name = "Match End";
        }

        private static void BuildMatchMaps(PageUi p)
        {
            RectTransform m = p.Panel.MatchRoot;
            if (m.Find("New game") != null) return;
            HaloUi.OneLine(HaloUi.Text(m, "New game", "NEW GAME ON (master; press twice):", 20f, HaloUi.Blue, TextAlignmentOptions.Left, 0f, 230f, 940f, 30f, true), 10f);
            List<string> maps = HaloLobbyPage.MapList();
            for (int i = 0; i < maps.Count && i < 20; i++)
            {
                string file = maps[i];
                HaloButton b = HaloUi.Button(m, HaloLobbyPage.MapLabel(file).ToUpperInvariant(), (i % 4) * 237f, 266f + (i / 4) * 54f, 230f, 48f, 17f, () => ArmMap(p, file));
                b.gameObject.name = "New game " + file;
                p.MapBtns.Add(new KeyValuePair<HaloButton, string>(b, file));
            }
        }

        private static void ArmMap(PageUi p, string file)
        {
            if (!HaloLobby.IsMaster) { Say("Only the instance master starts a new game.", true); return; }
            if (p.Armed == file && Time.unscaledTime - p.ArmedAt < 4f)
            {
                p.Armed = null;
                HaloLobby.RestartOn(file);
                return;
            }

            p.Armed = file;
            p.ArmedAt = Time.unscaledTime;
            Say("Press " + HaloLobbyPage.MapLabel(file) + " again to start a new game there for everybody.", false);
        }

        private static void SwitchInMatch(int team)
        {
            HaloRules r = HaloMatchControl.Rules;
            if (r == null || !r.Teams) { Say("This game has no teams.", true); return; }
            HaloMatchControl.SwitchTeam(team);
            string why;
            IHaloLobbyNet net = HaloLobbyNet.Net;
            if (net != null && net.Online && !net.SetTeam(team, out why)) Say(why ?? "The team change was refused.", true);
            else Say("You are on the " + HaloTeams.Name(team) + ".", false);
        }

        private static readonly List<HaloMatchPlayer> _sorted = new List<HaloMatchPlayer>(32);
        private static int _lastScoreSig = int.MinValue;

        private static void TickGameTabs(PageUi p)
        {
            if (!HaloSession.InMatch || Time.unscaledTime < p.NextScore) return;
            p.NextScore = Time.unscaledTime + 0.5f;
            HaloRules r = HaloMatchControl.Rules;
            bool teams = r != null && r.Teams;
            if (p.Panel.ScoreRoot.gameObject.activeInHierarchy)
            {
                _sorted.Clear();
                List<HaloMatchPlayer> all = HaloRoster.Players;
                for (int i = 0; i < all.Count; i++)
                {
                    HaloMatchPlayer x = all[i];
                    int at = _sorted.Count;
                    _sorted.Add(x);
                    while (at > 0 && (teams ? (x.Team < _sorted[at - 1].Team || (x.Team == _sorted[at - 1].Team && x.Score > _sorted[at - 1].Score)) : x.Score > _sorted[at - 1].Score)) { _sorted[at] = _sorted[at - 1]; at--; }
                    _sorted[at] = x;
                }

                float left = HaloMatchControl.TimeLeft;
                int sec = left < 0f ? -1 : Mathf.CeilToInt(left);
                int sig = sec * 31 + (teams ? HaloMatchControl.TeamScore(HaloTeams.Red) * 1000 + HaloMatchControl.TeamScore(HaloTeams.Blue) : 0);
                for (int i = 0; i < _sorted.Count; i++) sig = sig * 31 + _sorted[i].Score * 7 + _sorted[i].Kills * 3 + _sorted[i].Deaths + _sorted[i].Team;
                if (sig != _lastScoreSig || p.ScoreHead.text.Length == 0)
                {
                    _lastScoreSig = sig;
                    p.ScoreHead.text = r != null ? HaloGameTypes.Name(r).ToUpperInvariant() : "SCORES";
                    p.ScoreInfo.text = (HaloSession.MapEntry != null ? HaloLobbyPage.MapLabel(HaloSession.MapEntry.Name).ToUpperInvariant() : "") + "     TIME " + (sec < 0 ? "--:--" : (sec / 60) + ":" + (sec % 60).ToString("00")) +
                                       (teams ? "     RED " + HaloMatchControl.TeamScore(HaloTeams.Red) + "  BLUE " + HaloMatchControl.TeamScore(HaloTeams.Blue) : "") + "     TO WIN " + (r != null ? HaloRuleRows.Line(r, "score").Replace(":", "") : "");
                    for (int i = 0; i < p.ScoreRows.Length; i++)
                    {
                        TextMeshProUGUI row = p.ScoreRows[i];
                        if (i >= _sorted.Count) { row.text = ""; p.ScoreK[i].text = ""; p.ScoreD[i].text = ""; p.ScoreS[i].text = ""; continue; }
                        HaloMatchPlayer x = _sorted[i];
                        string name = (x.Name ?? "Player") + (x.Local ? "  (YOU)" : "");
                        row.text = name;
                        p.ScoreK[i].text = x.Kills.ToString();
                        p.ScoreD[i].text = x.Deaths.ToString();
                        p.ScoreS[i].text = x.Score.ToString();
                        Color rc = teams ? Color.Lerp(HaloTeams.Of(x.Team), Color.white, x.Local ? 0.6f : 0.3f) : x.Local ? Color.white : HaloUi.BlueBright;
                        row.color = rc; p.ScoreK[i].color = rc; p.ScoreD[i].color = rc; p.ScoreS[i].color = rc;
                    }
                }
            }

            if (p.Panel.TeamRoot.gameObject.activeInHierarchy)
            {
                p.TeamLine.text = teams ? "You are on the " + HaloTeams.Name(HaloLocal.Team) + ". A change puts you at your new team's base (no death)." : "This game has no teams (free for all).";
                p.Red.Interactable = teams && HaloLocal.Team != HaloTeams.Red;
                p.Blue.Interactable = teams && HaloLocal.Team != HaloTeams.Blue;
                p.Red.Selected = HaloLocal.Team == HaloTeams.Red;
                p.Blue.Selected = HaloLocal.Team == HaloTeams.Blue;
            }

            if (p.Panel.MatchRoot.gameObject.activeInHierarchy)
            {
                p.MatchLine.text = (r != null ? HaloGameTypes.Name(r) + " on " + HaloLobbyPage.MapLabel(r.MapName) : "") + "   -   instance master: " + (HaloLobby.MasterName ?? "?") + (HaloLobby.IsMaster ? " (you)" : "");
                p.EndBtn.gameObject.SetActive(HaloLobby.IsMaster);
                for (int i = 0; i < p.MapBtns.Count; i++)
                {
                    HaloButton b = p.MapBtns[i].Key;
                    if (b.gameObject.activeSelf != HaloLobby.IsMaster) b.gameObject.SetActive(HaloLobby.IsMaster);
                    b.Selected = p.Armed == p.MapBtns[i].Value && Time.unscaledTime - p.ArmedAt < 4f;
                }

                Transform ng = p.Panel.MatchRoot.Find("New game");
                if (ng != null && ng.gameObject.activeSelf != HaloLobby.IsMaster) ng.gameObject.SetActive(HaloLobby.IsMaster);
            }
        }

        // ------------------------------------------------------------------ music

        private static void TickMusic()
        {
            bool want = _musicWanted && HaloSession.Current == HaloSession.Phase.Hub && _hub != null && _hub.Shown && HaloSettings.Flag("front.music", true) && HaloUiArt.Music != null && !HaloLoading.Showing;
            if (want && !_musicOn)
            {
                _musicOn = true;
                try
                {
                    HaloAudio.Music(HaloUiArt.Music, 2f);      // Halo's title music (ui.map sound\music\title1\title1: "in", then its loops)
                    HaloLog.Info("Front: menu music started (sound\\music\\title1\\title1).");
                }
                catch (Exception e) { _musicWanted = false; HaloLog.Warn("Front: the menu music failed: " + e.Message); }
            }
            else if (!want && _musicOn) StopMusic(0.8f);
        }

        /// <summary>The hub's music fades out (a load starts, the setting is off, the hub hides). Other modules' music is not touched.</summary>
        public static void StopMusic(float fadeSeconds)
        {
            if (!_musicOn) return;
            _musicOn = false;
            try { HaloAudio.StopMusic(fadeSeconds); }
            catch (Exception e) { HaloLog.Warn("Front: stopping the music failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ the hub's keep rule

        private static void TickKeep()
        {
            if (_hub == null || !_hub.Shown || HaloSession.Current != HaloSession.Phase.Hub || Time.unscaledTime < _nextKeep) return;
            _nextKeep = Time.unscaledTime + 0.5f;
            if (!HaloPlayer.HasBridgePlayer) return;
            Vector3 feet;
            if (!HaloPlayer.TryFeet(out feet)) return;
            if (!HaloHub.IsAway(feet)) { _awaySince = -1f; return; }
            if (_awaySince < 0f) { _awaySince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _awaySince < 2f) return;
            _awaySince = -1f;
            string how;
            float yaw;
            string src;
            if (!HaloPlayer.TryFacingYaw(out yaw, out src)) yaw = 0f;
            bool moved = HaloPlayer.Teleport(HaloSpace.RoomOrigin + new Vector3(0f, 0.2f, 0f), yaw, out how);
            _keepMoves++;
            if (_keepMoves <= 5) HaloLog.Info("Hub: the player was off the platform (" + (feet - HaloSpace.RoomOrigin).ToString("0") + ") - put back (" + (moved ? how : "not moved: " + how) + ").");
        }
    }
}

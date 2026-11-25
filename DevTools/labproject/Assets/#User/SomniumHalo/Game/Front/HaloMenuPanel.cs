using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] ONE menu page in Halo PC's main menu look: the HALO logo (ui\shell\main_menu\halo_logo), the main menu column drawn by the
    /// game's own caption pictures (MULTIPLAYER, SETTINGS, CREDITS: menu_* [0] plain / [1] lit), the content area the builders fill (the lobby,
    /// the settings, the about page), the instance master line and the status line. The hub's panel and the "HALO" tablet app are two instances
    /// of the same page filled by the same builders (HaloFront.BuildPage), BO2's Bo2MenuPanel pattern. On the tablet, while a match runs, the
    /// column switches to the in-game tabs SCORES / TEAM / MATCH.
    /// Layout in canvas units from the top-left (HaloUi).
    /// </summary>
    public sealed class HaloMenuPanel
    {
        public const float Width = 1300f, Height = 860f;
        /// <summary>The content area (page units from the top-left).</summary>
        public const float ContentX = 330f, ContentY = 24f, ContentW = 950f, ContentH = 760f;

        public enum Tab { Multiplayer = 0, Settings = 1, About = 2, Score = 3, Team = 4, Match = 5, Campaign = 6, Mission = 7 }

        public string LogName = "Menu";
        public RectTransform Root { get; private set; }
        public RectTransform TitleRoot { get; private set; }
        public RectTransform NavRoot { get; private set; }
        public RectTransform MultiplayerRoot { get; private set; }
        public RectTransform SettingsRoot { get; private set; }
        public RectTransform AboutRoot { get; private set; }
        public RectTransform ScoreRoot { get; private set; }
        public RectTransform TeamRoot { get; private set; }
        public RectTransform MatchRoot { get; private set; }
        /// <summary>The CAMPAIGN tab (HaloCampaignPage) and the tablet's MISSION tab while a campaign mission runs.</summary>
        public RectTransform CampaignRoot { get; private set; }
        public RectTransform MissionRoot { get; private set; }
        /// <summary>In game mode: the running game is a campaign mission (MISSION) rather than a multiplayer match (SCORES / TEAM / MATCH).</summary>
        public bool CampaignMode { get; private set; }
        public Tab Current { get; private set; }
        public bool GameMode { get; private set; }
        public bool Interactive { get { return _interactive; } }
        public event Action<Tab> TabChanged;

        private HaloButton _mp, _settings, _about, _score, _team, _match, _campaign, _missionBtn;
        private TextMeshProUGUI _status;
        private string _statusText = "";
        private bool _statusError, _interactive = true, _modeSet;
        private readonly List<Action> _refreshers = new List<Action>();

        public static HaloMenuPanel Build(Transform parent, float width, float height, Tab tab, string logName, string status, bool statusError, bool interactive)
        {
            var p = new HaloMenuPanel { LogName = logName, Current = tab, _statusText = status ?? "", _statusError = statusError, _interactive = interactive };
            RectTransform root = HaloUi.Place(parent, "Halo Menu", 0f, 0f, width, height);
            p.Root = root;
            root.gameObject.AddComponent<HaloMenuPanelHook>().Panel = p;

            // the page: the game's menu gradient over a navy base, a blue rim (the list screens' look)
            HaloUi.Box(root, "Backdrop", 0f, 0f, width, height, new Color(0f, 0.02f, 0.05f, 0.90f));
            RawImage grad = HaloUi.Picture(root, "Gradient", 0f, 0f, width, height, "ui\\shell\\bitmaps\\gradient", 0, 4, 480, new Color(1f, 1f, 1f, 0.85f));
            grad.raycastTarget = false;
            HaloUi.Box(root, "Rim top", 0f, 0f, width, 3f, HaloUi.Rim);
            HaloUi.Box(root, "Rim bottom", 0f, height - 3f, width, 3f, HaloUi.Rim);
            HaloUi.Box(root, "Rim left", 0f, 0f, 3f, height, new Color(0.18f, 0.55f, 1f, 0.6f));
            HaloUi.Box(root, "Rim right", width - 3f, 0f, 3f, height, new Color(0.18f, 0.55f, 1f, 0.6f));
            HaloUi.Box(root, "Column rule", ContentX - 14f, 30f, 2f, height - 120f, new Color(0.18f, 0.55f, 1f, 0.35f));

            // the logo (640 x 251 used of 1024 x 256), 300 units wide
            p.TitleRoot = HaloUi.Place(root, "Title", 14f, 14f, 300f, 118f);
            HaloUi.Picture(p.TitleRoot, "Logo", 0f, 0f, 300f, 118f, "ui\\shell\\main_menu\\halo_logo", 0, 640, 251, Color.white);

            // the main menu column (the game's caption pictures: 232 x 32 used, drawn 290 x 40)
            p.NavRoot = HaloUi.Place(root, "Nav", 0f, 0f, ContentX, height);
            const float Nx = 22f, Nw = 290f, Nh = 40f;
            // Halo PC's main menu order: CAMPAIGN (menu_load_campaign), MULTIPLAYER, SETTINGS, CREDITS
            p._campaign = HaloUi.MenuItem(p.NavRoot, "CAMPAIGN", "ui\\shell\\main_menu\\menu_load_campaign", Nx, 170f, Nw, Nh, () => p.SetTab(Tab.Campaign, true));
            p._mp = HaloUi.MenuItem(p.NavRoot, AppStr("main_menu_options", 2, "MULTIPLAYER"), "ui\\shell\\main_menu\\menu_multiplayer", Nx, 222f, Nw, Nh, () => p.SetTab(Tab.Multiplayer, true));
            p._settings = HaloUi.MenuItem(p.NavRoot, AppStr("main_menu_options", 3, "SETTINGS"), "ui\\shell\\main_menu\\menu_settings", Nx, 274f, Nw, Nh, () => p.SetTab(Tab.Settings, true));
            p._about = HaloUi.MenuItem(p.NavRoot, "CREDITS", "ui\\shell\\main_menu\\menu_credits", Nx, 326f, Nw, Nh, () => p.SetTab(Tab.About, true));
            // the in-game tabs (the tablet in a match): text buttons
            p._score = HaloUi.Button(p.NavRoot, "SCORES", Nx, 170f, Nw, 48f, 26f, () => p.PressTab(3));
            p._team = HaloUi.Button(p.NavRoot, "TEAM", Nx, 226f, Nw, 48f, 26f, () => p.PressTab(4));
            p._match = HaloUi.Button(p.NavRoot, "MATCH", Nx, 282f, Nw, 48f, 26f, () => p.PressTab(5));
            p._missionBtn = HaloUi.Button(p.NavRoot, "MISSION", Nx, 170f, Nw, 48f, 26f, () => p.SetTab(Tab.Mission, true));

            p.MultiplayerRoot = HaloUi.Place(root, "Multiplayer Tab", ContentX, ContentY, ContentW, ContentH);
            p.SettingsRoot = HaloUi.Place(root, "Settings Tab", ContentX, ContentY, ContentW, ContentH);
            p.AboutRoot = HaloUi.Place(root, "About Tab", ContentX, ContentY, ContentW, ContentH);
            p.ScoreRoot = HaloUi.Place(root, "Score Tab", ContentX, ContentY, ContentW, ContentH);
            p.TeamRoot = HaloUi.Place(root, "Team Tab", ContentX, ContentY, ContentW, ContentH);
            p.MatchRoot = HaloUi.Place(root, "Match Tab", ContentX, ContentY, ContentW, ContentH);
            p.CampaignRoot = HaloUi.Place(root, "Campaign Tab", ContentX, ContentY, ContentW, ContentH);
            p.MissionRoot = HaloUi.Place(root, "Mission Tab", ContentX, ContentY, ContentW, ContentH);

            HaloMasterLabelView.Build(root, "Instance Master", 22f, 600f, 296f, 34f, 20f);
            HaloUi.OneLine(HaloUi.Text(root, "Version", HaloVersion.Label, 15f, HaloUi.Grey, TextAlignmentOptions.Left, 22f, 640f, 296f, 22f, false), 9f);
            var tag = HaloUi.Text(root, "Tagline", "Played from your own Halo: Combat Evolved install - nothing of the game is in this world.", 14f, HaloUi.Grey, TextAlignmentOptions.TopLeft, 22f, 664f, 296f, 60f, false);
            tag.overflowMode = TextOverflowModes.Overflow;

            p._status = HaloUi.Text(root, "Status", p._statusText, 20f, Color.white, TextAlignmentOptions.Center, 40f, height - 72f, width - 80f, 62f, false);
            p._status.color = statusError ? HaloUi.Red : HaloUi.BlueBright;
            p.ApplyMode();
            p.SetTab(tab, false);
            p.SetInteractive(interactive);
            return p;
        }

        private static string AppStr(string list, int i, string fallback) { return HaloUiArt.S(list, i, fallback).ToUpperInvariant(); }

        /// <summary>The room panel: the logo floats above the page, larger (BO2's Bo2RoomLook), the button hints under it; the placer frames all of it.</summary>
        public void DecorateForRoom(HaloPanelPlacer placer, float metresPerUnit)
        {
            const float TitleBand = 250f, HintBand = 50f;
            try
            {
                TitleRoot.SetParent(Root, false);
                TitleRoot.localScale = Vector3.one * 2.1f;
                TitleRoot.anchoredPosition = new Vector2((Width - 300f * 2.1f) * 0.5f, TitleBand - 4f);
                // the nav moves up where the logo was
                NavRoot.anchoredPosition = new Vector2(0f, 120f);
                bool xr = HaloPlayer.XrPresent;
                string hints = xr ? "TRIGGER  Select          B / Y  Somnium tablet (the HALO app)          GRIP  Take your weapons in a match"
                                  : "CLICK  Select          TAB  Somnium tablet (the HALO app)          " + HaloPlayer.BackKeyName + "  Back to this ring from a match";
                HaloUi.OneLine(HaloUi.Text(Root, "Hints", hints, 20f, HaloUi.Blue, TextAlignmentOptions.Center, 20f, Height + 12f, Width - 40f, 30f, false), 12f);
                if (placer != null)
                {
                    placer.HeightMetres = (Height + TitleBand + HintBand) * metresPerUnit;
                    placer.CentreUp = (TitleBand - HintBand) * 0.5f * metresPerUnit;
                }
            }
            catch (Exception e)
            {
                HaloLog.Warn("Hub: dressing the panel failed (the plain page stays): " + e.Message);
            }
        }

        public void SetTab(Tab tab, bool byPlayer)
        {
            bool changed = tab != Current;
            Current = tab;
            Show(MultiplayerRoot, tab == Tab.Multiplayer);
            Show(SettingsRoot, tab == Tab.Settings);
            Show(AboutRoot, tab == Tab.About);
            Show(ScoreRoot, tab == Tab.Score);
            Show(TeamRoot, tab == Tab.Team);
            Show(MatchRoot, tab == Tab.Match);
            Show(CampaignRoot, tab == Tab.Campaign);
            Show(MissionRoot, tab == Tab.Mission);
            if (_campaign != null) _campaign.Selected = tab == Tab.Campaign;
            if (_missionBtn != null) _missionBtn.Selected = tab == Tab.Mission;
            if (_mp != null) _mp.Selected = tab == Tab.Multiplayer;
            if (_settings != null) _settings.Selected = tab == Tab.Settings;
            if (_about != null) _about.Selected = tab == Tab.About;
            if (_score != null) _score.Selected = tab == Tab.Score;
            if (_team != null) _team.Selected = tab == Tab.Team;
            if (_match != null) _match.Selected = tab == Tab.Match;
            if (byPlayer)
            {
                HaloLog.Info(LogName + ": tab " + tab + (changed ? "" : " (already shown)") + ".");
                var h = TabChanged;
                if (h != null && changed) h(tab);
            }
        }

        private static void Show(RectTransform r, bool on)
        {
            if (r != null && r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
        }

        public void PressTab(int i)
        {
            SetTab((Tab)Mathf.Clamp(i, 0, 7), true);
        }

        public void SetStatus(string text, bool error)
        {
            text = text ?? "";
            if (text == _statusText && error == _statusError) return;
            _statusText = text;
            _statusError = error;
            if (_status != null) { _status.text = text; _status.color = error ? HaloUi.Red : HaloUi.BlueBright; }
        }

        public void SetInteractive(bool interactive)
        {
            _interactive = interactive;
            bool on = interactive || GameMode;
            if (_mp != null) _mp.Interactable = on;
            if (_settings != null) _settings.Interactable = on;
            if (_about != null) _about.Interactable = on;
            if (_campaign != null) _campaign.Interactable = on;
        }

        /// <summary>A match starts or ends (the tablet page): the column shows SCORES / TEAM / MATCH, or the main menu again.</summary>
        public void SetGameMode(bool game) { SetGameMode(game, false); }

        public void SetGameMode(bool game, bool campaign)
        {
            if (_modeSet && game == GameMode && campaign == CampaignMode) return;
            _modeSet = true;
            bool changed = game != GameMode || campaign != CampaignMode;
            GameMode = game;
            CampaignMode = campaign;
            ApplyMode();
            if (changed) SetTab(game ? (campaign ? Tab.Mission : Tab.Score) : (campaign ? Tab.Campaign : Tab.Multiplayer), false);
        }

        private void ApplyMode()
        {
            if (_campaign != null) _campaign.gameObject.SetActive(!GameMode);
            if (_mp != null) _mp.gameObject.SetActive(!GameMode);
            if (_settings != null) _settings.gameObject.SetActive(!GameMode);
            if (_about != null) _about.gameObject.SetActive(!GameMode);
            if (_score != null) _score.gameObject.SetActive(GameMode && !CampaignMode);
            if (_team != null) _team.gameObject.SetActive(GameMode && !CampaignMode);
            if (_match != null) _match.gameObject.SetActive(GameMode && !CampaignMode);
            if (_missionBtn != null) _missionBtn.gameObject.SetActive(GameMode && CampaignMode);
            SetInteractive(_interactive);
        }

        public void AddRefresh(Action refresh) { if (refresh != null) _refreshers.Add(refresh); }

        public void RefreshAll()
        {
            for (int i = 0; i < _refreshers.Count; i++)
            {
                try { _refreshers[i](); }
                catch (Exception e) { HaloLog.Warn(LogName + ": refreshing a widget failed: " + e.Message); }
            }
        }

        public void Destroy()
        {
            _refreshers.Clear();
            if (Root != null) UnityEngine.Object.Destroy(Root.gameObject);
        }
    }

    /// <summary>On the page's root: whenever the page is enabled (the hub shown again, the tablet app opened) its widgets re-read the settings.</summary>
    public sealed class HaloMenuPanelHook : MonoBehaviour
    {
        public HaloMenuPanel Panel;
        private void OnEnable() { if (Panel != null) Panel.RefreshAll(); }
    }

    /// <summary>Who the instance master is ("INSTANCE MASTER: name" with a crown); the lobby sets it from the net link each frame (BO2's Bo2MasterLabel).</summary>
    public static class HaloMasterLabel
    {
        public static int Version { get; private set; }
        public static string Name { get; private set; }
        public static bool Mine { get; private set; }
        public static bool Known { get { return Name != null; } }

        public static string Text
        {
            get { return Name == null ? "INSTANCE MASTER: connecting..." : "INSTANCE MASTER: " + Name + (Mine && Name != "You" ? "  (YOU)" : ""); }
        }

        public static void Set(string name, bool mine)
        {
            if (name == Name && mine == Mine) return;
            Name = name;
            Mine = mine;
            Version++;
        }
    }

    public sealed class HaloMasterLabelView : MonoBehaviour
    {
        public TextMeshProUGUI Text;
        public Image Crown;
        private int _seen = -1;

        private void OnEnable() { _seen = -1; Apply(); }
        private void Update() { if (_seen != HaloMasterLabel.Version) Apply(); }

        private void Apply()
        {
            _seen = HaloMasterLabel.Version;
            if (Text != null)
            {
                Text.text = HaloMasterLabel.Text;
                Text.color = HaloMasterLabel.Mine ? HaloUi.Gold : HaloUi.Blue;
            }

            if (Crown != null) Crown.gameObject.SetActive(HaloMasterLabel.Known);
        }

        public static HaloMasterLabelView Build(Transform parent, string name, float x, float y, float w, float h, float fontSize)
        {
            RectTransform root = HaloUi.Place(parent, name, x, y, w, h);
            float crownW = h * 1.2f, crownH = h * 0.8f;
            Image crown = HaloUi.Box(root, "Crown", 0f, (h - crownH) * 0.5f, crownW, crownH, Color.white);
            crown.sprite = HaloCrown.Sprite;
            crown.preserveAspect = true;
            TextMeshProUGUI t = HaloUi.Text(root, "Text", HaloMasterLabel.Text, fontSize, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, crownW + 6f, 0f, w - crownW - 6f, h, true);
            HaloUi.OneLine(t, fontSize * 0.5f);
            var view = root.gameObject.AddComponent<HaloMasterLabelView>();
            view.Text = t;
            view.Crown = crown;
            view.Apply();
            return view;
        }
    }

    /// <summary>A small gold crown drawn in code (nothing of the game): the master's mark (BO2's Bo2Crown).</summary>
    public static class HaloCrown
    {
        private static Sprite _sprite;
        private static Texture2D _tex;

        private static readonly Vector2[] Shape =
        {
            new Vector2(0.04f, 0.18f), new Vector2(0.04f, 0.90f), new Vector2(0.27f, 0.52f), new Vector2(0.50f, 0.98f), new Vector2(0.73f, 0.52f), new Vector2(0.96f, 0.90f), new Vector2(0.96f, 0.18f),
        };

        public static Sprite Sprite { get { if (_sprite == null) Make(); return _sprite; } }

        private static void Make()
        {
            const int W = 64, H = 48;
            _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "Halo.Crown", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 3; sy++) for (int sx = 0; sx < 3; sx++) if (Inside((x + (sx + 0.5f) / 3f) / W, (y + (sy + 0.5f) / 3f) / H)) hit++;
                    Color c = HaloUi.Gold;
                    c.a = hit / 9f;
                    px[y * W + x] = c;
                }
            }

            _tex.SetPixels32(px);
            _tex.Apply(false, true);
            _sprite = Sprite.Create(_tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        }

        private static bool Inside(float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = Shape.Length - 1; i < Shape.Length; j = i++)
            {
                Vector2 a = Shape[i], b = Shape[j];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }

            return inside;
        }
    }
}

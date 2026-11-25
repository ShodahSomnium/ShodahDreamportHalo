using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] The MULTIPLAYER tab of one menu page: the lobby (the map with its preview picture and description, the game type with its
    /// picture and description, the rules, the players by team, CHANGE MAP / GAME TYPE / RULES / SWITCH TEAM / START GAME), the map select screen
    /// (the 19 maps' own previews, ui\shell\bitmaps\mp_map_grafix), the game type select screen and the rules screen (the game's own option labels
    /// and values from ui.map). It shows <see cref="HaloLobby"/> and is refreshed four times a second while shown, only when the lobby's signature
    /// changed. One instance per page (the hub's panel, the tablet app).
    /// </summary>
    public sealed class HaloLobbyPage
    {
        private const int TeamRows = 6;
        private const string Grafix = "ui\\shell\\bitmaps\\mp_map_grafix", Types = "ui\\shell\\bitmaps\\game_type_grafix";

        private readonly HaloMenuPanel _panel;
        private readonly RectTransform _root;
        private RectTransform _lobbyView, _mapView, _typeView, _rulesView;
        private int _view = -1, _want, _sig = int.MinValue, _rulesSig = int.MinValue;
        private float _next;

        // lobby view
        private TextMeshProUGUI _banner, _count, _mapName, _mapDesc, _typeName, _typeDesc, _rules1, _rules2, _rules3, _rules4, _rules5, _playersHead;
        private RawImage _preview, _typePic;
        private readonly TextMeshProUGUI[] _teamHead = new TextMeshProUGUI[2];
        private readonly TextMeshProUGUI[][] _teamRows = { new TextMeshProUGUI[TeamRows], new TextMeshProUGUI[TeamRows] };
        private HaloButton _changeMap, _gameType, _rulesBtn, _switchTeam, _start, _join;
        private string _shownMap;
        private int _shownPic = -1;

        // map / type / rules views
        private readonly List<KeyValuePair<HaloButton, string>> _mapButtons = new List<KeyValuePair<HaloButton, string>>();
        private readonly List<KeyValuePair<HaloButton, HaloGameTypes.Variant>> _typeButtons = new List<KeyValuePair<HaloButton, HaloGameTypes.Variant>>();
        private RawImage _typeViewPic;
        private TextMeshProUGUI _typeViewName, _typeViewDesc, _rulesHint;
        private readonly List<Row> _rows = new List<Row>();

        private sealed class Row
        {
            public HaloRuleRows.Def Def;
            public TextMeshProUGUI Label, Value;
            public HaloButton Dec, Inc;
            public RectTransform Root;
            public string LastValue, LastLabel;
        }

        public static readonly List<HaloLobbyPage> Pages = new List<HaloLobbyPage>();

        public HaloLobbyPage(HaloMenuPanel panel)
        {
            _panel = panel;
            _root = panel.MultiplayerRoot;
            Build();
            Pages.RemoveAll(p => p._panel == null || p._panel.Root == null);
            Pages.Add(this);
            panel.AddRefresh(() => { _sig = int.MinValue; Refresh(true); });
        }

        public bool Alive { get { return _panel != null && _panel.Root != null; } }
        public int View { get { return _view; } }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            _lobbyView = HaloUi.Place(_root, "Lobby View", 0f, 0f, HaloMenuPanel.ContentW, HaloMenuPanel.ContentH);
            _mapView = HaloUi.Place(_root, "Map Select View", 0f, 0f, HaloMenuPanel.ContentW, HaloMenuPanel.ContentH);
            _typeView = HaloUi.Place(_root, "Game Type View", 0f, 0f, HaloMenuPanel.ContentW, HaloMenuPanel.ContentH);
            _rulesView = HaloUi.Place(_root, "Rules View", 0f, 0f, HaloMenuPanel.ContentW, HaloMenuPanel.ContentH);
            BuildLobby(_lobbyView);
            BuildMaps(_mapView);
            BuildTypes(_typeView);
            BuildRules(_rulesView);
            ShowView(0);
        }

        private static void Header(RectTransform v, string bitmap, string fallback)
        {
            // the screens' header pictures (512 x 59 of 512 x 64), the text until read (no picture: text only)
            if (bitmap == null) { HaloUi.OneLine(HaloUi.Text(v, "Header text", fallback, 34f, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, 30f, 0f, 560f, 60f, true)); return; }
            RawImage h = HaloUi.Picture(v, "Header", -10f, -6f, 640f, 74f, bitmap, 0, 512, 59, Color.white);
            HaloUi.OneLine(HaloUi.Text(v, "Header text", fallback, 34f, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, 30f, 0f, 560f, 60f, true));
            h.gameObject.AddComponent<HaloHideTextWhenPicture>().Text = v.Find("Header text").gameObject;
        }

        private void BuildLobby(RectTransform v)
        {
            Header(v, "ui\\shell\\main_menu\\multiplayer_type_select\\header_multiplayer", "MULTIPLAYER");
            _banner = HaloUi.OneLine(HaloUi.Text(v, "Banner", "", 22f, HaloUi.BlueBright, TextAlignmentOptions.Right, 420f, 12f, 530f, 34f, true), 12f);
            _count = HaloUi.OneLine(HaloUi.Text(v, "Count", "", 26f, HaloUi.Gold, TextAlignmentOptions.Right, 420f, 46f, 530f, 34f, true), 12f);

            // the map
            HaloUi.Panel(v, "Map panel", 0f, 84f, 380f, 384f);
            _preview = HaloUi.Picture(v, "Map preview", 15f, 99f, 350f, 288f, Grafix, 19, HaloUiData.PreviewWidth, HaloUiData.PreviewHeight, Color.white);
            _mapName = HaloUi.OneLine(HaloUi.Text(v, "Map name", "", 30f, Color.white, TextAlignmentOptions.Left, 18f, 392f, 350f, 36f, true), 16f);
            _mapDesc = HaloUi.Text(v, "Map description", "", 20f, HaloUi.Blue, TextAlignmentOptions.TopLeft, 18f, 428f, 350f, 40f, false);

            // the game type
            HaloUi.Panel(v, "Type panel", 396f, 84f, 554f, 196f);
            _typePic = HaloUi.Picture(v, "Type picture", 410f, 100f, 154f, 126f, Types, 2, HaloUiData.PreviewWidth, HaloUiData.PreviewHeight, Color.white);
            _typeName = HaloUi.OneLine(HaloUi.Text(v, "Type name", "", 30f, Color.white, TextAlignmentOptions.Left, 578f, 96f, 362f, 38f, true), 16f);
            _typeDesc = HaloUi.Text(v, "Type description", "", 21f, HaloUi.Blue, TextAlignmentOptions.TopLeft, 578f, 136f, 362f, 136f, false);

            // the rules
            HaloUi.Panel(v, "Rules panel", 396f, 292f, 554f, 176f);
            _rules1 = HaloUi.OneLine(HaloUi.Text(v, "Rules 1", "", 21f, HaloUi.BlueBright, TextAlignmentOptions.Left, 414f, 304f, 520f, 30f, true), 10f);
            _rules2 = HaloUi.OneLine(HaloUi.Text(v, "Rules 2", "", 21f, HaloUi.BlueBright, TextAlignmentOptions.Left, 414f, 336f, 520f, 30f, true), 10f);
            _rules3 = HaloUi.OneLine(HaloUi.Text(v, "Rules 3", "", 21f, HaloUi.BlueBright, TextAlignmentOptions.Left, 414f, 368f, 520f, 30f, true), 10f);
            _rules4 = HaloUi.OneLine(HaloUi.Text(v, "Rules 4", "", 21f, HaloUi.BlueBright, TextAlignmentOptions.Left, 414f, 400f, 520f, 30f, true), 10f);
            _rules5 = HaloUi.OneLine(HaloUi.Text(v, "Rules 5", "", 21f, HaloUi.BlueBright, TextAlignmentOptions.Left, 414f, 432f, 520f, 30f, true), 10f);

            // the players
            HaloUi.Panel(v, "Players panel", 0f, 480f, 950f, 196f);
            _playersHead = HaloUi.OneLine(HaloUi.Text(v, "Players head", "", 20f, HaloUi.Grey, TextAlignmentOptions.Right, 600f, 486f, 334f, 26f, false), 10f);
            for (int t = 0; t < 2; t++)
            {
                float x = 20f + t * 470f;
                _teamHead[t] = HaloUi.OneLine(HaloUi.Text(v, "Team " + t, "", 22f, Color.white, TextAlignmentOptions.Left, x, 488f, 440f, 30f, true), 12f);
                for (int i = 0; i < TeamRows; i++) _teamRows[t][i] = HaloUi.OneLine(HaloUi.Text(v, "Player " + t + "." + i, "", 21f, HaloUi.BlueBright, TextAlignmentOptions.Left, x + 6f, 520f + i * 25f, 430f, 25f, false), 10f);
            }

            // the buttons
            const float By = 690f, Bh = 58f;
            _changeMap = HaloUi.Button(v, "CHANGE MAP", 0f, By, 182f, Bh, 24f, () => Open(1));
            _gameType = HaloUi.Button(v, "GAME TYPE", 192f, By, 182f, Bh, 24f, () => Open(2));
            _rulesBtn = HaloUi.Button(v, "RULES", 384f, By, 170f, Bh, 24f, () => Open(3));
            _switchTeam = HaloUi.Button(v, "SWITCH TEAM", 564f, By, 186f, Bh, 24f, () => Say(HaloLobby.SwitchTeam()));
            _start = HaloUi.Button(v, "START GAME", 760f, By, 190f, Bh, 24f, () => Say(HaloLobby.PressStart()));
            _join = HaloUi.Button(v, "JOIN GAME", 760f, By, 190f, Bh, 24f, () => HaloLobby.Join());
            _changeMap.gameObject.name = "Lobby Change Map";
            _gameType.gameObject.name = "Lobby Game Type";
            _rulesBtn.gameObject.name = "Lobby Rules";
            _switchTeam.gameObject.name = "Lobby Switch Team";
            _start.gameObject.name = "Lobby Start";
            _join.gameObject.name = "Lobby Join";
            _switchTeam.Refused = () => Say(HaloLobby.ViewRules != null && !HaloLobby.ViewRules.Teams ? "This game type has no teams." : "Teams are picked in the lobby, before the game starts.");
            _start.Refused = () => Say(HaloLobby.IsMaster ? "A match is loading or running." : "Only the instance master" + (HaloLobby.MasterName != null ? " (" + HaloLobby.MasterName + ")" : "") + " starts the game.");
        }

        private void BuildMaps(RectTransform v)
        {
            Header(v, "ui\\shell\\main_menu\\multiplayer_type_select\\mp_map_select\\header_select_map", "SELECT MAP");
            HaloUi.Button(v, AppS("common_button_captions", 0, "BACK"), 790f, 10f, 160f, 50f, 22f, () => Open(0)).gameObject.name = "Maps Back";
            List<string> maps = MapList();
            const float Cw = 186f, Ch = 160f, Iw = 154f, Ih = 127f;
            for (int i = 0; i < maps.Count && i < 20; i++)
            {
                string file = maps[i];
                float x = (i % 5) * (Cw + 5f), y = 82f + (i / 5) * (Ch + 8f);
                HaloButton b = HaloUi.Button(v, "", x, y, Cw, Ch, 18f, () => { HaloLobby.SetMap(file); Open(0); });
                b.gameObject.name = "Map " + file;
                int pic = HaloUiData.MenuIndex(file);
                HaloUi.Picture(b.transform, "Preview", (Cw - Iw) * 0.5f, 6f, Iw, Ih, Grafix, pic >= 0 ? pic : 19, HaloUiData.PreviewWidth, HaloUiData.PreviewHeight, Color.white);
                b.SetCaptionRect(8f, Ih + 6f, Cw - 16f, Ch - Ih - 8f, 17f);
                b.SetLabel(MapLabel(file));
                _mapButtons.Add(new KeyValuePair<HaloButton, string>(b, file));
            }
        }

        private void BuildTypes(RectTransform v)
        {
            Header(v, "ui\\shell\\main_menu\\gametype_select\\header_select_gametype", "SELECT GAME TYPE");
            HaloUi.Button(v, AppS("common_button_captions", 0, "BACK"), 790f, 10f, 160f, 50f, 22f, () => Open(0)).gameObject.name = "Types Back";
            List<HaloGameTypes.Variant> all = HaloGameTypes.All;
            for (int i = 0; i < all.Count; i++)
            {
                HaloGameTypes.Variant variant = all[i];
                HaloButton b = HaloUi.Button(v, variant.Name.ToUpperInvariant(), 0f, 82f + i * 54f, 430f, 48f, 22f, () => { HaloLobby.SetVariant(variant); _rulesSig = int.MinValue; Refresh(true); });
                b.gameObject.name = "Type " + variant.Name;
                if (!variant.Built)
                {
                    b.Interactable = false;
                    b.SetLabel(variant.Name.ToUpperInvariant() + "  (NOT BUILT)");
                    b.Refused = () => Say(variant.Name + " is not built in this Dreamport yet.");
                }

                _typeButtons.Add(new KeyValuePair<HaloButton, HaloGameTypes.Variant>(b, variant));
            }

            HaloUi.Panel(v, "Type info", 450f, 82f, 500f, 640f);
            _typeViewPic = HaloUi.Picture(v, "Type info picture", 560f, 106f, 280f, 230f, Types, 2, HaloUiData.PreviewWidth, HaloUiData.PreviewHeight, Color.white);
            _typeViewName = HaloUi.OneLine(HaloUi.Text(v, "Type info name", "", 32f, Color.white, TextAlignmentOptions.Center, 470f, 350f, 460f, 40f, true), 16f);
            _typeViewDesc = HaloUi.Text(v, "Type info description", "", 21f, HaloUi.Blue, TextAlignmentOptions.Top, 476f, 400f, 448f, 300f, false);
        }

        private void BuildRules(RectTransform v)
        {
            Header(v, null, "RULES");      // (ui.map's header_rules picture reads "STEP 1: SELECT GAME": the text instead)
            HaloUi.Button(v, AppS("common_button_captions", 0, "BACK"), 790f, 10f, 160f, 50f, 22f, () => Open(0)).gameObject.name = "Rules Back";
            _rulesHint = HaloUi.OneLine(HaloUi.Text(v, "Hint", "", 19f, HaloUi.Grey, TextAlignmentOptions.Left, 0f, 64f, 780f, 26f, false), 10f);
            List<HaloRuleRows.Def> defs = HaloRuleRows.All;
            for (int i = 0; i < defs.Count; i++)
            {
                HaloRuleRows.Def def = defs[i];
                var row = new Row { Def = def };
                row.Root = HaloUi.Place(v, "Rule " + def.Key, 0f, 0f, 470f, 42f);
                row.Label = HaloUi.OneLine(HaloUi.Text(row.Root, "Label", "", 20f, HaloUi.Blue, TextAlignmentOptions.Left, 0f, 0f, 240f, 42f, true), 9f);
                row.Value = HaloUi.OneLine(HaloUi.Text(row.Root, "Value", "", 21f, Color.white, TextAlignmentOptions.Left, 244f, 0f, 130f, 42f, true), 9f);
                Row captured = row;
                row.Dec = HaloUi.Button(row.Root, "<", 376f, 2f, 44f, 38f, 22f, () => Step(captured, -1));
                row.Inc = HaloUi.Button(row.Root, ">", 424f, 2f, 44f, 38f, 22f, () => Step(captured, 1));
                row.Dec.gameObject.name = "Rule " + def.Key + " <";
                row.Inc.gameObject.name = "Rule " + def.Key + " >";
                _rows.Add(row);
            }
        }

        private void Step(Row row, int d)
        {
            if (!HaloLobby.CanEdit || HaloLobby.Mine == null) { Say(HaloLobby.IsMaster ? "The rules cannot change while the game starts." : "Only the instance master changes the rules."); return; }
            row.Def.Step(HaloLobby.Mine, d);
            HaloLobby.Changed();
            _rulesSig = int.MinValue;
            Refresh(true);
        }

        private static string AppS(string list, int i, string fallback) { return HaloUiArt.S(list, i, fallback).ToUpperInvariant(); }

        /// <summary>The installed multiplayer maps in the game's own menu order, then any others (custom maps).</summary>
        public static List<string> MapList()
        {
            var l = new List<string>();
            HaloInstall inst = HaloGame.Current != null ? HaloGame.Current.Install : null;
            foreach (string m in HaloUiData.MenuMapOrder) if (inst == null || inst.Map(m) != null) l.Add(m);
            if (inst != null) foreach (HaloInstall.MapEntry e in inst.Multiplayer) if (!l.Contains(e.Name)) l.Add(e.Name);
            return l;
        }

        public static string MapLabel(string file)
        {
            HaloUiData d = HaloUiArt.Data;
            string s = d != null ? d.MapLabel(file) : null;
            return s ?? HaloInstall.Label(file ?? "");
        }

        // ------------------------------------------------------------------ views

        public void Open(int view)
        {
            if ((view == 1 || view == 2) && !HaloLobby.CanEdit)
            {
                Say(HaloLobby.IsMaster ? "Not while the game starts." : "Only the instance master" + (HaloLobby.MasterName != null ? " (" + HaloLobby.MasterName + ")" : "") + " picks the map and the game type.");
                return;
            }

            _want = view;
            Refresh(true);
        }

        private void ShowView(int v)
        {
            if (v == _view) return;
            _view = v;
            _lobbyView.gameObject.SetActive(v == 0);
            _mapView.gameObject.SetActive(v == 1);
            _typeView.gameObject.SetActive(v == 2);
            _rulesView.gameObject.SetActive(v == 3);
            _sig = int.MinValue;
            _rulesSig = int.MinValue;
        }

        private static void Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            HaloFront.Say(text, true);
        }

        // ------------------------------------------------------------------ refreshing

        public static void TickAll()
        {
            for (int i = Pages.Count - 1; i >= 0; i--)
            {
                HaloLobbyPage p = Pages[i];
                if (!p.Alive) { Pages.RemoveAt(i); continue; }
                try { p.Refresh(false); }
                catch (Exception e) { HaloLog.Warn("Lobby: refreshing a page failed: " + e); }
            }
        }

        public void Refresh(bool force)
        {
            if (!Alive) return;
            bool shown = _root.gameObject.activeInHierarchy;
            if (!shown && !force) return;
            if (!force && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            if ((_want == 1 || _want == 2) && !HaloLobby.CanEdit) _want = 0;
            ShowView(_want);
            if (_view == 0) RefreshLobby();
            else if (_view == 1) RefreshMaps();
            else if (_view == 2) RefreshTypes();
            else RefreshRules();
        }

        private void RefreshLobby()
        {
            int sig = HaloLobby.Signature;
            if (sig == _sig) return;
            _sig = sig;
            HaloRules r = HaloLobby.ViewRules ?? new HaloRules();
            int phase = HaloLobby.ViewPhase;
            bool master = HaloLobby.IsMaster, hub = HaloSession.Current == HaloSession.Phase.Hub;
            string masterName = HaloLobby.MasterName;

            _banner.text = master ? (HaloLobby.Online ? "YOU ARE THE INSTANCE MASTER" : "YOU SET UP THE GAME") : phase == HaloLobbyState.InMatch ? (masterName != null ? masterName.ToUpperInvariant() + "'S GAME IS RUNNING" : "A GAME IS RUNNING") : "WAITING FOR " + (masterName != null ? masterName.ToUpperInvariant() : "THE INSTANCE MASTER") + " TO START THE GAME";
            _count.text = phase == HaloLobbyState.Countdown ? "THE GAME STARTS IN " + HaloLobby.ViewCount : phase == HaloLobbyState.InMatch ? "GAME IN PROGRESS" : "";

            // the map and its picture
            if (r.MapName != _shownMap)
            {
                _shownMap = r.MapName;
                int pic = HaloUiData.MenuIndex(r.MapName);
                HaloUiArt.Rebind(_preview, Grafix, pic >= 0 ? pic : 19, HaloUiData.PreviewWidth, HaloUiData.PreviewHeight, Color.white);
                _mapName.text = MapLabel(r.MapName).ToUpperInvariant();
                HaloUiData d = HaloUiArt.Data;
                _mapDesc.text = d != null ? d.MapDescription(r.MapName).Replace("\n\n", "   ").Replace('\n', ' ') : "";
            }

            int tp = HaloGameTypes.Picture(r.Type);
            if (tp != _shownPic) { _shownPic = tp; HaloUiArt.Rebind(_typePic, Types, tp, HaloUiData.PreviewWidth, HaloUiData.PreviewHeight, Color.white); }
            HaloGameTypes.Variant v = HaloGameTypes.Of(r);
            _typeName.text = (v != null ? v.Name : r.Label).ToUpperInvariant();
            _typeDesc.text = v != null ? v.Description.Replace('\n', ' ') : "";

            _rules1.text = HaloRuleRows.Line(r, "score") + "     " + HaloRuleRows.Line(r, "time");
            _rules2.text = HaloRuleRows.Line(r, "lives") + "     " + HaloRuleRows.Line(r, "respawn");
            _rules3.text = HaloRuleRows.Line(r, "shields") + "     " + HaloRuleRows.Line(r, "radar");
            _rules4.text = HaloRuleRows.Line(r, "weapons") + "     " + HaloRuleRows.Line(r, "vehicles");
            _rules5.text = HaloRuleRows.Line(r, "teams") + (r.Teams ? "     " + HaloRuleRows.Line(r, "ff") : "");

            // the players
            List<HaloLobbyPlayer> players = HaloLobby.Players;
            _playersHead.text = players.Count + (players.Count == 1 ? " PLAYER" : " PLAYERS") + (HaloLobby.Online ? "" : "  (OFFLINE)");
            int[] shown = { 0, 0 };
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < players.Count; i++)
                {
                    HaloLobbyPlayer p = players[i];
                    if ((pass == 0) != p.Master) continue;
                    int col = r.Teams ? (p.Team == HaloTeams.Blue ? 1 : 0) : (shown[0] < TeamRows ? 0 : 1);
                    if (shown[col] >= TeamRows) continue;
                    TextMeshProUGUI row = _teamRows[col][shown[col]++];
                    row.text = (p.Name ?? "Player") + (p.Me ? "   (YOU)" : "") + (p.Master ? "   - MASTER" : "") + (p.InMatch ? "   - IN THE GAME" : "");
                    row.color = p.Me ? Color.white : r.Teams ? Color.Lerp(HaloTeams.Of(p.Team), Color.white, 0.45f) : HaloUi.BlueBright;
                }
            }

            for (int t = 0; t < 2; t++) for (int i = shown[t]; i < TeamRows; i++) _teamRows[t][i].text = "";
            if (r.Teams)
            {
                _teamHead[0].text = HaloUiArt.S("pregame\\teams", 0, "Red Team").ToUpperInvariant() + (HaloLocal.Team != HaloTeams.Blue ? "  (YOUR TEAM)" : "");
                _teamHead[1].text = HaloUiArt.S("pregame\\teams", 1, "Blue Team").ToUpperInvariant() + (HaloLocal.Team == HaloTeams.Blue ? "  (YOUR TEAM)" : "");
                _teamHead[0].color = HaloTeams.RedColor;
                _teamHead[1].color = HaloTeams.BlueColor;
            }
            else
            {
                _teamHead[0].text = "PLAYERS (FREE FOR ALL)";
                _teamHead[1].text = "";
                _teamHead[0].color = Color.white;
            }

            // the buttons
            bool edit = HaloLobby.CanEdit;
            _changeMap.gameObject.SetActive(master);
            _gameType.gameObject.SetActive(master);
            _changeMap.Interactable = edit;
            _gameType.Interactable = edit;
            _rulesBtn.SetLabel(master ? "RULES" : "VIEW RULES");
            _switchTeam.Interactable = r.Teams && hub && phase != HaloLobbyState.InMatch;
            bool canJoin = HaloLobby.CanJoin && !master;
            _join.gameObject.SetActive(canJoin);
            _start.gameObject.SetActive(!canJoin);
            _start.SetLabel(master ? (phase == HaloLobbyState.Countdown ? "CANCEL  (" + HaloLobby.ViewCount + ")" : phase == HaloLobbyState.InMatch ? "IN PROGRESS" : "START GAME") : "WAITING FOR " + (masterName != null ? masterName.ToUpperInvariant() : "THE MASTER"));
            _start.Interactable = master && hub && phase != HaloLobbyState.InMatch;
        }

        private void RefreshMaps()
        {
            string current = HaloLobby.ViewRules != null ? HaloLobby.ViewRules.MapName : null;
            for (int i = 0; i < _mapButtons.Count; i++) _mapButtons[i].Key.Selected = string.Equals(_mapButtons[i].Value, current, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshTypes()
        {
            HaloGameTypes.Variant cur = HaloGameTypes.Of(HaloLobby.ViewRules);
            for (int i = 0; i < _typeButtons.Count; i++) _typeButtons[i].Key.Selected = _typeButtons[i].Value == cur || (cur != null && _typeButtons[i].Value.NameIndex == cur.NameIndex);
            if (cur != null)
            {
                int pic = HaloGameTypes.Picture(cur.Type);
                if (_typeViewName.text != cur.Name.ToUpperInvariant())
                {
                    HaloUiArt.Rebind(_typeViewPic, Types, pic, HaloUiData.PreviewWidth, HaloUiData.PreviewHeight, Color.white);
                    _typeViewName.text = cur.Name.ToUpperInvariant();
                    _typeViewDesc.text = cur.Description;
                }
            }
        }

        private void RefreshRules()
        {
            HaloRules r = HaloLobby.ViewRules ?? new HaloRules();
            bool edit = HaloLobby.CanEdit;
            int sig = HaloLobby.Signature * 7 + (edit ? 1 : 0);
            if (sig == _rulesSig) return;
            _rulesSig = sig;
            _rulesHint.text = edit ? "Press < > to change a rule: it is shared with every player and kept for next time." : HaloLobby.IsMaster ? "The rules cannot change while the game starts or runs." : "Only " + (HaloLobby.MasterName ?? "the instance master") + " changes the rules: these are theirs.";
            int shown = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                bool on = row.Def.Shown(r);
                if (row.Root.gameObject.activeSelf != on) row.Root.gameObject.SetActive(on);
                if (!on) continue;
                int col = shown < 8 ? 0 : 1, line = shown < 8 ? shown : shown - 8;
                row.Root.anchoredPosition = new Vector2(col * 480f, -(100f + line * 46f));
                shown++;
                string label = row.Def.Label(r), value = row.Def.Value(r);
                if (label != row.LastLabel) { row.LastLabel = label; row.Label.text = label; }
                if (value != row.LastValue) { row.LastValue = value; row.Value.text = value; }
                row.Dec.gameObject.SetActive(edit);
                row.Inc.gameObject.SetActive(edit);
            }
        }
    }

    /// <summary>Hides a fallback text once its picture arrived.</summary>
    public sealed class HaloHideTextWhenPicture : MonoBehaviour
    {
        public GameObject Text;
        private RawImage _img;
        private void Awake() { _img = GetComponent<RawImage>(); }
        private void Update()
        {
            if (_img != null && _img.texture != null && Text != null) { Text.SetActive(false); enabled = false; }
        }
    }

    /// <summary>The rules screen's rows: Halo PC's own option labels and value lists (ui.map "..._labels" / "var_..." string lists), per game type.</summary>
    public static class HaloRuleRows
    {
        public sealed class Def
        {
            public string Key;
            public Func<HaloRules, bool> Shown;
            public Func<HaloRules, string> Label, Value;
            public Action<HaloRules, int> Step;
        }

        private static List<Def> _all;
        private const string P = "settings_select\\multiplayer_setup\\";

        private static string S(string list, int i, string fallback) { return HaloUiArt.S(list, i, fallback).ToUpperInvariant(); }
        private static string YesNo(bool b) { return S("strings\\var_boolean", b ? 1 : 0, b ? "YES" : "NO"); }

        private static int Step(int[] choices, int value, int d)
        {
            int at = 0;
            for (int i = 0; i < choices.Length; i++) if (choices[i] == value) { at = i; break; } else if (choices[i] < value) at = i;
            at = (at + d + choices.Length) % choices.Length;
            return choices[at];
        }

        private static int IndexOf(int[] choices, int value)
        {
            for (int i = 0; i < choices.Length; i++) if (choices[i] == value) return i;
            return -1;
        }

        private static readonly int[] Kills = { 5, 10, 15, 25, 50 }, Caps = { 1, 3, 5, 10, 15 }, Minutes = { 60, 120, 300, 600, 900 }, Laps = { 1, 3, 5, 10, 15, 25 };
        private static readonly int[] Times = { 0, 10, 15, 20, 25, 30, 45 }, Lives = { 0, 1, 3, 5 }, Respawn = { 0, 5, 10, 15 };

        private static int[] ScoreChoices(HaloRules r)
        {
            switch (r.Type)
            {
                case HaloScenario.GameType.Ctf: return Caps;
                case HaloScenario.GameType.King:
                case HaloScenario.GameType.Oddball: return Minutes;
                case HaloScenario.GameType.Race: return Laps;
                default: return Kills;
            }
        }

        private static string ScoreLabel(HaloRules r)
        {
            switch (r.Type)
            {
                case HaloScenario.GameType.Ctf: return S(P + "playlist_edit\\ctf_edit\\ctf_labels", 4, "CAPTURES TO WIN:");
                case HaloScenario.GameType.King: return S(P + "playlist_edit\\koth_edit\\koth_labels", 1, "SCORE TO WIN:");
                case HaloScenario.GameType.Oddball: return S(P + "playlist_edit\\oddball_edit\\oddball_labels", 6, "MINUTES/POINTS TO WIN:");
                case HaloScenario.GameType.Race: return S(P + "playlist_edit\\race_edit\\race_labels", 2, "LAPS TO WIN:");
                default: return S(P + "playlist_edit\\slayer_edit\\slayer_labels", 3, "KILLS TO WIN:");
            }
        }

        private static string ScoreValue(HaloRules r)
        {
            if (r.Type == HaloScenario.GameType.King || r.Type == HaloScenario.GameType.Oddball)
            {
                int i = IndexOf(Minutes, r.ScoreLimit);
                return i >= 0 ? S(P + "playlist_edit\\koth_edit\\var_score_to_win", i, (r.ScoreLimit / 60) + " MINUTES") : (r.ScoreLimit / 60f).ToString("0.#") + " MINUTES";
            }

            return r.ScoreLimit.ToString();
        }

        private static string TimeValue(HaloRules r)
        {
            int i = IndexOf(Times, r.TimeLimitMinutes);
            return i >= 0 ? S(P + "playlist_edit\\ctf_edit\\var_time_limit", i, r.TimeLimitMinutes <= 0 ? "NONE" : r.TimeLimitMinutes + " MINUTES") : r.TimeLimitMinutes + " MINUTES";
        }

        private static string Pick(string list, int[] choices, int value, string fallback)
        {
            int i = IndexOf(choices, value);
            return i >= 0 ? S(list, i, fallback) : fallback;
        }

        public static List<Def> All
        {
            get
            {
                if (_all != null) return _all;
                var l = new List<Def>();
                string po = P + "player_options_edit\\", io = P + "item_options_edit\\", vo = P + "vehicle_options_edit\\", ino = P + "indicator_options_edit\\", to = P + "teamplay_options_edit\\";
                l.Add(new Def { Key = "teams", Shown = r => r.Type != HaloScenario.GameType.Ctf, Label = r => S(P + "playlist_edit\\slayer_edit\\slayer_labels", 4, "TEAM PLAY:"), Value = r => YesNo(r.Teams), Step = (r, d) => { r.Teams = !r.Teams; if (r.Type == HaloScenario.GameType.Slayer && r.ScoreLimit == (r.Teams ? 25 : 50)) r.ScoreLimit = r.Teams ? 50 : 25; } });
                l.Add(new Def { Key = "score", Shown = r => true, Label = ScoreLabel, Value = ScoreValue, Step = (r, d) => r.ScoreLimit = Step(ScoreChoices(r), r.ScoreLimit, d) });
                l.Add(new Def { Key = "time", Shown = r => true, Label = r => S(P + "playlist_edit\\ctf_edit\\ctf_labels", 5, "TIME LIMIT:"), Value = TimeValue, Step = (r, d) => r.TimeLimitMinutes = Step(Times, r.TimeLimitMinutes, d) });
                l.Add(new Def { Key = "lives", Shown = r => true, Label = r => S(po + "player_options_labels", 0, "NUMBER OF LIVES:"), Value = r => Pick(po + "var_number_of_lives", Lives, r.Lives, r.Lives <= 0 ? "INFINITE" : r.Lives.ToString()), Step = (r, d) => r.Lives = Step(Lives, r.Lives, d) });
                l.Add(new Def { Key = "respawn", Shown = r => true, Label = r => S(po + "player_options_labels", 3, "RESPAWN TIME:"), Value = r => Pick(po + "var_respawn_time", Respawn, r.RespawnSeconds, r.RespawnSeconds + " SECONDS"), Step = (r, d) => r.RespawnSeconds = Step(Respawn, r.RespawnSeconds, d) });
                l.Add(new Def { Key = "suicide", Shown = r => true, Label = r => S(po + "player_options_labels", 7, "SUICIDE PENALTY:"), Value = r => Pick(po + "var_suicide_penalty", Respawn, r.SuicidePenaltySeconds, r.SuicidePenaltySeconds + " SECONDS"), Step = (r, d) => r.SuicidePenaltySeconds = Step(Respawn, r.SuicidePenaltySeconds, d) });
                l.Add(new Def { Key = "shields", Shown = r => true, Label = r => S(po + "player_options_labels", 2, "SHIELDS:"), Value = r => S(po + "var_shields", r.Shields ? 0 : 1, r.Shields ? "YES" : "NO"), Step = (r, d) => r.Shields = !r.Shields });
                l.Add(new Def { Key = "radar", Shown = r => true, Label = r => S(ino + "indicator_options_labels", 2, "OTHER PLAYERS ON RADAR:"), Value = r => YesNo(r.MotionTracker), Step = (r, d) => r.MotionTracker = !r.MotionTracker });
                l.Add(new Def { Key = "weapons", Shown = r => true, Label = r => S(io + "item_options_labels", 1, "WEAPON SET:"), Value = r => S(io + "var_weapon_set", r.WeaponSet, "SET " + r.WeaponSet), Step = (r, d) => r.WeaponSet = (r.WeaponSet + d + 13) % 13 });
                l.Add(new Def { Key = "vehicles", Shown = r => true, Label = r => S(vo + "vehicle_options_labels", 0, "VEHICLE SET:"), Value = r => S(vo + "var_vehicle_set", r.Vehicles, "SET " + r.Vehicles), Step = (r, d) => r.Vehicles = (r.Vehicles + d + 8) % 8 });
                l.Add(new Def { Key = "grenades", Shown = r => true, Label = r => "GRENADES:", Value = r => YesNo(r.Grenades), Step = (r, d) => r.Grenades = !r.Grenades });
                l.Add(new Def { Key = "ff", Shown = r => r.Teams, Label = r => S(to + "teamplay_options_labels", 0, "FRIENDLY FIRE:"), Value = r => YesNo(r.FriendlyFire), Step = (r, d) => r.FriendlyFire = !r.FriendlyFire });
                l.Add(new Def { Key = "hill", Shown = r => r.Type == HaloScenario.GameType.King, Label = r => S(P + "playlist_edit\\koth_edit\\koth_labels", 0, "MOVING HILL:"), Value = r => YesNo(r.KingMovingHill), Step = (r, d) => r.KingMovingHill = !r.KingMovingHill });
                l.Add(new Def { Key = "racetype", Shown = r => r.Type == HaloScenario.GameType.Race, Label = r => S(P + "playlist_edit\\race_edit\\race_labels", 1, "RACE TYPE:"), Value = r => S(P + "playlist_edit\\race_edit\\var_race_type", r.RaceType, "NORMAL"), Step = (r, d) => r.RaceType = (r.RaceType + d + 3) % 3 });
                l.Add(new Def { Key = "racescore", Shown = r => r.Type == HaloScenario.GameType.Race && r.Teams, Label = r => S(P + "playlist_edit\\race_edit\\race_labels", 0, "TEAM SCORING:"), Value = r => S(P + "playlist_edit\\race_edit\\var_team_scoring", r.RaceTeamScoring, "MINIMUM"), Step = (r, d) => r.RaceTeamScoring = (r.RaceTeamScoring + d + 3) % 3 });
                l.Add(new Def { Key = "flaghome", Shown = r => r.Type == HaloScenario.GameType.Ctf, Label = r => S(P + "playlist_edit\\ctf_edit\\ctf_labels", 3, "FLAG AT HOME TO SCORE:"), Value = r => YesNo(r.FlagAtHomeToScore), Step = (r, d) => r.FlagAtHomeToScore = !r.FlagAtHomeToScore });
                if (HaloUiArt.Ready) _all = l;
                return l;
            }
        }

        /// <summary>"LABEL value" of one row for the lobby's summary.</summary>
        public static string Line(HaloRules r, string key)
        {
            foreach (Def d in All) if (d.Key == key) return d.Label(r) + " " + d.Value(r);
            return "";
        }
    }
}

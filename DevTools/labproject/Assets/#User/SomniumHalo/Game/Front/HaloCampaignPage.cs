using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] The CAMPAIGN tab of one menu page, Halo PC's campaign menu in one screen: the level select (the 10 missions by the game's own
    /// names, map_list_oneline, with their pictures ui\shell\bitmaps\sp_levels and descriptions solo_level_select\map_data; a mission whose map is
    /// not installed shows the game's lock picture and "You cannot play this level... yet!"), the difficulty select (EASY / NORMAL / HEROIC /
    /// LEGENDARY: difficulty_options pictures, difficulty_options strings, difficulty_descriptions) and START MISSION. The instance master picks
    /// (co-op: everybody in the instance is pulled into the mission through HaloLobby, which calls HaloCampaign.StartMission); alone you are
    /// the master. Everyone else sees the master's choice read-only. Also the tablet's MISSION tab while a mission runs: RESUME, RESTART FROM
    /// CHECKPOINT (HaloCampaign.Revert), LEAVE MISSION (HaloCampaign.LeaveMission; the master's leave ends it for everyone).
    /// </summary>
    public sealed class HaloCampaignPage
    {
        private const string Levels = "ui\\shell\\bitmaps\\sp_levels", Diffs = "ui\\shell\\main_menu\\difficulty_select\\difficulty_options";
        public const int PicW = HaloUiData.PreviewWidth, PicH = HaloUiData.PreviewHeight;

        private readonly HaloMenuPanel _panel;
        private readonly List<KeyValuePair<HaloButton, string>> _missions = new List<KeyValuePair<HaloButton, string>>();
        private readonly HaloButton[] _diffs = new HaloButton[4];
        private RawImage _picture;
        private TextMeshProUGUI _name, _desc, _diffName, _diffDesc, _banner, _count;
        private HaloButton _start, _join;
        private int _sig = int.MinValue, _shownPic = -1;
        private float _next;

        public static readonly List<HaloCampaignPage> Pages = new List<HaloCampaignPage>();

        public HaloCampaignPage(HaloMenuPanel panel)
        {
            _panel = panel;
            Build(panel.CampaignRoot);
            Pages.RemoveAll(p => p._panel == null || p._panel.Root == null);
            Pages.Add(this);
            panel.AddRefresh(() => { _sig = int.MinValue; Refresh(true); });
        }

        private bool Alive { get { return _panel != null && _panel.Root != null; } }

        private static string S(string list, int i, string fallback) { return HaloUiArt.S(list, i, fallback); }

        private static bool Installed(string mission)
        {
            HaloInstall inst = HaloGame.Current != null ? HaloGame.Current.Install : null;
            return inst != null && inst.Map(mission) != null;
        }

        public static string MissionName(string mission)
        {
            HaloUiData d = HaloUiArt.Data;
            string s = d != null ? d.MissionName(mission) : null;
            return s ?? HaloInstall.Label(mission ?? "");
        }

        public static string DifficultyName(int d)
        {
            return S("difficulty_select\\difficulty_options", d, d == 0 ? "EASY" : d == 1 ? "NORMAL" : d == 2 ? "HEROIC" : "LEGENDARY").ToUpperInvariant();
        }

        // ------------------------------------------------------------------ building

        private void Build(RectTransform v)
        {
            RawImage h = HaloUi.Picture(v, "Header", -10f, -6f, 640f, 74f, "ui\\shell\\main_menu\\solo_level_select\\header_load_level", 0, 512, 59, Color.white);
            GameObject ht = HaloUi.OneLine(HaloUi.Text(v, "Header text", "CAMPAIGN", 34f, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, 30f, 0f, 560f, 60f, true)).gameObject;
            h.gameObject.AddComponent<HaloHideTextWhenPicture>().Text = ht;
            _banner = HaloUi.OneLine(HaloUi.Text(v, "Banner", "", 22f, HaloUi.BlueBright, TextAlignmentOptions.Right, 420f, 12f, 530f, 34f, true), 12f);
            _count = HaloUi.OneLine(HaloUi.Text(v, "Count", "", 26f, HaloUi.Gold, TextAlignmentOptions.Right, 420f, 46f, 530f, 34f, true), 12f);

            // the 10 missions (the game's list of the level select screen)
            for (int i = 0; i < HaloUiData.MissionOrder.Length; i++)
            {
                string mission = HaloUiData.MissionOrder[i];
                HaloButton b = HaloUi.Button(v, MissionName(mission).ToUpperInvariant(), 0f, 84f + i * 55f, 400f, 50f, 22f, () => PickMission(mission));
                b.gameObject.name = "Mission " + mission;
                if (!Installed(mission))
                {
                    b.Interactable = false;
                    b.Refused = () => HaloFront.Say(S("solo_level_select\\map_data", HaloUiData.LockedPicture, "You cannot play this level... yet!").Replace('\n', ' ') + " (" + mission + ".map is not in your MAPS folder)", true);
                }

                _missions.Add(new KeyValuePair<HaloButton, string>(b, mission));
            }

            // the chosen mission: its picture and description
            HaloUi.Panel(v, "Mission panel", 416f, 84f, 534f, 330f);
            _picture = HaloUi.Picture(v, "Mission picture", 430f, 98f, 244f, 200f, Levels, 0, PicW, PicH, Color.white);
            _name = HaloUi.Text(v, "Mission name", "", 28f, Color.white, TextAlignmentOptions.TopLeft, 688f, 98f, 252f, 100f, true);
            _desc = HaloUi.Text(v, "Mission description", "", 21f, HaloUi.Blue, TextAlignmentOptions.TopLeft, 432f, 310f, 506f, 100f, false);

            // the difficulty: the game's four pictures (200 x 211 widget of 256 x 256)
            HaloUi.Panel(v, "Difficulty panel", 416f, 424f, 534f, 260f);
            RawImage dh = HaloUi.Picture(v, "Difficulty header", 422f, 426f, 420f, 48f, "ui\\shell\\main_menu\\difficulty_select\\header_choose_difficulty", 0, 512, 59, Color.white);
            GameObject dht = HaloUi.OneLine(HaloUi.Text(v, "Difficulty header text", "CHOOSE DIFFICULTY", 24f, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, 440f, 430f, 400f, 40f, true)).gameObject;
            dh.gameObject.AddComponent<HaloHideTextWhenPicture>().Text = dht;
            for (int d = 0; d < 4; d++)
            {
                int level = d;
                HaloButton b = HaloUi.Button(v, "", 428f + d * 128f, 478f, 122f, 128f, 16f, () => PickDifficulty(level));
                b.gameObject.name = "Difficulty " + d;
                HaloUi.Picture(b.transform, "Picture", 11f, 6f, 100f, 106f, Diffs, d, 200, 211, Color.white);
                b.SetCaptionRect(4f, 108f, 114f, 20f, 15f);
                b.SetLabel(DifficultyName(d));
                _diffs[d] = b;
            }

            _diffName = HaloUi.OneLine(HaloUi.Text(v, "Difficulty name", "", 22f, Color.white, TextAlignmentOptions.Left, 432f, 610f, 506f, 28f, true), 12f);
            _diffDesc = HaloUi.Text(v, "Difficulty description", "", 17f, HaloUi.Blue, TextAlignmentOptions.TopLeft, 432f, 638f, 506f, 46f, false);

            _start = HaloUi.Button(v, "START MISSION", 416f, 696f, 534f, 60f, 26f, () => Say(HaloLobby.PressStartCampaign()));
            _join = HaloUi.Button(v, "JOIN MISSION", 416f, 696f, 534f, 60f, 26f, () => HaloLobby.Join());
            _start.gameObject.name = "Campaign Start";
            _join.gameObject.name = "Campaign Join";
            _start.Refused = () => Say(HaloLobby.IsMaster ? "A game is loading or running." : "Only the instance master" + (HaloLobby.MasterName != null ? " (" + HaloLobby.MasterName + ")" : "") + " starts the mission.");
        }

        private static void Say(string text) { if (!string.IsNullOrEmpty(text)) HaloFront.Say(text, true); }

        private void PickMission(string mission)
        {
            if (!HaloLobby.CanEdit) { Say(HaloLobby.IsMaster ? "Not while a game starts or runs." : "Only the instance master picks the mission."); return; }
            HaloLobby.CampaignMission = mission;
            HaloLog.Info("Campaign page: mission " + mission);
            Refresh(true);
        }

        private void PickDifficulty(int d)
        {
            if (!HaloLobby.CanEdit) { Say(HaloLobby.IsMaster ? "Not while a game starts or runs." : "Only the instance master picks the difficulty."); return; }
            HaloLobby.CampaignDifficulty = (HaloCampaign.Difficulty)d;
            HaloLog.Info("Campaign page: difficulty " + (HaloCampaign.Difficulty)d);
            Refresh(true);
        }

        // ------------------------------------------------------------------ refreshing

        public static void TickAll()
        {
            for (int i = Pages.Count - 1; i >= 0; i--)
            {
                HaloCampaignPage p = Pages[i];
                if (!p.Alive) { Pages.RemoveAt(i); continue; }
                try { p.Refresh(false); }
                catch (Exception e) { HaloLog.Warn("Campaign page: refreshing failed: " + e); }
            }
        }

        public void Refresh(bool force)
        {
            if (!Alive) return;
            if (!force && (!_panel.CampaignRoot.gameObject.activeInHierarchy || Time.unscaledTime < _next)) return;
            _next = Time.unscaledTime + 0.25f;
            bool master = HaloLobby.IsMaster;
            // the master shows its own pick; everyone else the master's (published while a mission is chosen / runs)
            string mission = master || string.IsNullOrEmpty(HaloLobby.ViewMission) ? HaloLobby.CampaignMission : HaloLobby.ViewMission;
            int diff = master || string.IsNullOrEmpty(HaloLobby.ViewMission) ? (int)HaloLobby.CampaignDifficulty : HaloLobby.ViewDifficulty;
            int sig = HaloLobby.Signature * 31 + (mission != null ? mission.GetHashCode() : 0) + diff * 7;
            if (sig == _sig && !force) return;
            _sig = sig;

            int index = HaloUiData.MissionIndex(mission);
            bool installed = Installed(mission);
            int pic = installed && index >= 0 ? index : HaloUiData.LockedPicture;
            if (pic != _shownPic) { _shownPic = pic; HaloUiArt.Rebind(_picture, Levels, pic, PicW, PicH, Color.white); }
            _name.text = MissionName(mission).ToUpperInvariant();
            HaloUiData data = HaloUiArt.Data;
            _desc.text = data != null ? data.MissionDescription(installed ? index : HaloUiData.LockedPicture) : "";
            for (int i = 0; i < _missions.Count; i++) _missions[i].Key.Selected = _missions[i].Value == mission;
            for (int d = 0; d < 4; d++) _diffs[d].Selected = d == diff;
            _diffName.text = DifficultyName(diff);
            string dd = S("difficulty_select\\difficulty_descriptions", diff, "");
            _diffDesc.text = dd.Replace("\r\n", " ").Replace('\n', ' ');

            bool campaignGame = !string.IsNullOrEmpty(HaloLobby.ViewMission);
            int phase = HaloLobby.ViewPhase;
            bool hub = HaloSession.Current == HaloSession.Phase.Hub;
            string masterName = HaloLobby.MasterName;
            _banner.text = master ? (HaloLobby.Online ? "YOU ARE THE INSTANCE MASTER: CO-OP FOR EVERYONE HERE" : "SOLO") : "THE INSTANCE MASTER" + (masterName != null ? " (" + masterName.ToUpperInvariant() + ")" : "") + " PICKS THE MISSION";
            _count.text = phase == HaloLobbyState.Countdown && campaignGame ? "THE MISSION STARTS IN " + HaloLobby.ViewCount : phase == HaloLobbyState.InMatch ? (campaignGame ? "MISSION IN PROGRESS" : "A MULTIPLAYER GAME IS RUNNING") : "";
            bool canJoin = HaloLobby.CanJoin && !master && campaignGame;
            _join.gameObject.SetActive(canJoin);
            _start.gameObject.SetActive(!canJoin);
            _start.SetLabel(master ? (phase == HaloLobbyState.Countdown && campaignGame ? "CANCEL  (" + HaloLobby.ViewCount + ")" : phase == HaloLobbyState.InMatch ? "IN PROGRESS" : "START MISSION") : "WAITING FOR " + (masterName != null ? masterName.ToUpperInvariant() : "THE MASTER"));
            _start.Interactable = master && hub && installed && phase != HaloLobbyState.InMatch;
            bool edit = HaloLobby.CanEdit;
            for (int i = 0; i < _missions.Count; i++) if (Installed(_missions[i].Value)) _missions[i].Key.Interactable = edit || !master;
        }

        // ------------------------------------------------------------------ the tablet's MISSION tab

        /// <summary>Builds the MISSION tab (RESUME / RESTART FROM CHECKPOINT / LEAVE MISSION) into a page's MissionRoot.</summary>
        public static void BuildMissionTab(HaloMenuPanel panel, Action resume)
        {
            RectTransform m = panel.MissionRoot;
            if (m == null || m.Find("Head") != null) return;
            HaloUi.OneLine(HaloUi.Text(m, "Head", "MISSION", 34f, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, 30f, 0f, 560f, 60f, true));
            TextMeshProUGUI line = HaloUi.Text(m, "Line", "", 24f, Color.white, TextAlignmentOptions.TopLeft, 0f, 76f, 940f, 80f, true);
            RawImage pic = HaloUi.Picture(m, "Picture", 600f, 150f, 340f, 279f, Levels, 0, PicW, PicH, Color.white);
            HaloButton resumeBtn = HaloUi.Button(m, "RESUME", 0f, 170f, 560f, 70f, 28f, resume);
            HaloButton revert = HaloUi.Button(m, "RESTART FROM CHECKPOINT", 0f, 260f, 560f, 70f, 28f, () => { HaloLog.Info("Front: restart from the last checkpoint (tablet)."); HaloCampaign.Revert(); if (resume != null) resume(); });
            HaloButton leave = HaloUi.Button(m, "LEAVE MISSION", 0f, 350f, 560f, 70f, 28f, () => LeaveMission());
            resumeBtn.gameObject.name = "Mission Resume";
            revert.gameObject.name = "Mission Revert";
            leave.gameObject.name = "Mission Leave";
            TextMeshProUGUI note = HaloUi.Text(m, "Note", "", 19f, HaloUi.Grey, TextAlignmentOptions.TopLeft, 0f, 440f, 940f, 80f, false);
            int shown = -2;
            panel.AddRefresh(() =>
            {
                string mission = HaloCampaign.Mission;
                line.text = MissionName(mission).ToUpperInvariant() + "     " + DifficultyName((int)HaloCampaign.Level);
                int i = HaloUiData.MissionIndex(mission);
                if (i != shown) { shown = i; HaloUiArt.Rebind(pic, Levels, i >= 0 ? i : HaloUiData.LockedPicture, PicW, PicH, Color.white); }
                note.text = HaloLobby.Online ? (HaloLobby.IsMaster ? "You host this co-op mission: if you leave, everybody returns to the ring." : "Leaving takes only you back to the ring; JOIN MISSION there takes you back in.") : "";
            });
        }

        private static void LeaveMission()
        {
            HaloLog.Info("Front: the player leaves the mission (tablet).");
            if (HaloLobby.IsMaster) HaloLobby.EndForEveryone();
            else HaloCampaign.LeaveMission();
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The HUD's words, all in Halo's font and wording (ui multiplayer_game_text, hud messages): HUD messages under the weapon panel
    /// (hud globals' place, up time and fade time), the kill feed above the motion tracker ("X was killed by Y", "You killed X",
    /// "Double Kill!"...), the respawn countdown ("Rejoin in 3"), the end of the game ("You won", "Your team lost"), the score and the
    /// time left (bottom right; the time flashes in hud globals' "not much time left" colours in the last minute) and the scoreboard
    /// (Place / Name / Kills / Assists / Deaths, team totals).
    /// </summary>
    public sealed partial class HaloHudView
    {
        /// <summary>ours: kill feed lines kept, and how long a feed line stays before it fades (s).</summary>
        public const int FeedLines = 5;
        public static float FeedSeconds = 5f;

        HaloHudLabel _help, _center, _center2, _result, _objective;
        string _objSeen; float _objAt = -100f;
        HaloHudDigits _timer;
        /// <summary>ours: how long a new campaign objective stays on the HUD (s).</summary>
        public static float ObjectiveSeconds = 8f;
        float _helpUntil = -1f, _helpFadeEnd = -1f;
        readonly HaloHudLabel[] _feed = new HaloHudLabel[FeedLines];
        readonly string[] _feedText = new string[FeedLines];
        readonly float[] _feedAt = new float[FeedLines];
        int _lastRespawn = -1;

        // multi kills
        float _lastKillAt = -100f; int _multi;

        // bottom right score
        HaloHudLabel _scoreName1, _scoreName2;
        HaloHudDigits _score1, _score2, _clock;
        RawImage _scoreBar1, _scoreBar2;
        int _s1 = int.MinValue, _s2 = int.MinValue;
        readonly HudNumberEl _scoreNum = new HudNumberEl();

        // scoreboard
        RawImage _boardBack, _boardMe;
        HaloHudLabel _boardTitle;
        const int BoardRows = 18, BoardCols = 5;
        readonly HaloHudLabel[] _board = new HaloHudLabel[BoardRows * BoardCols];
        readonly RawImage[] _boardRowBack = new RawImage[BoardRows];
        readonly List<HaloMatchPlayer> _sorted = new List<HaloMatchPlayer>();
        float _boardRefresh;

        void BuildText()
        {
            _help = new HaloHudLabel(_layerText, "Message");
            _objective = new HaloHudLabel(_layerText, "Objective");
            _timer = new HaloHudDigits(_layerText, "Script timer", 7);
            for (int i = 0; i < FeedLines; i++) { _feed[i] = new HaloHudLabel(_layerText, "Feed " + i); _feedAt[i] = -100f; }
            _center = new HaloHudLabel(_layerText, "Centre");
            _center2 = new HaloHudLabel(_layerText, "Centre 2");
            _scoreBar1 = HaloHudKit.Raw(_layerText, "Score bar 1"); _scoreBar1.texture = HaloTextures.White; _scoreBar1.enabled = false;
            _scoreBar2 = HaloHudKit.Raw(_layerText, "Score bar 2"); _scoreBar2.texture = HaloTextures.White; _scoreBar2.enabled = false;
            _scoreName1 = new HaloHudLabel(_layerText, "Score name 1");
            _scoreName2 = new HaloHudLabel(_layerText, "Score name 2");
            _score1 = new HaloHudDigits(_layerText, "Score 1", 4);
            _score2 = new HaloHudDigits(_layerText, "Score 2", 4);
            _clock = new HaloHudDigits(_layerText, "Time left", 6);

            _boardBack = HaloHudKit.Raw(_layerBoard, "Scoreboard"); _boardBack.texture = HaloTextures.White;
            _boardMe = HaloHudKit.Raw(_layerBoard, "Scoreboard me"); _boardMe.texture = HaloTextures.White; _boardMe.enabled = false;
            for (int r = 0; r < BoardRows; r++) { _boardRowBack[r] = HaloHudKit.Raw(_layerBoard, "Row " + r); _boardRowBack[r].texture = HaloTextures.White; _boardRowBack[r].enabled = false; }
            _boardTitle = new HaloHudLabel(_layerBoard, "Scoreboard title");
            for (int i = 0; i < _board.Length; i++) _board[i] = new HaloHudLabel(_layerBoard, "Cell " + i);
            _result = new HaloHudLabel(_layerBoard, "Result");
            HaloHudKit.SetActive(_layerBoard.gameObject, false);
        }

        void TeardownText()
        {
            if (_help == null) return;
            _help.Destroy(); _objective.Destroy(); _center.Destroy(); _center2.Destroy(); _result.Destroy(); _boardTitle.Destroy(); _scoreName1.Destroy(); _scoreName2.Destroy();
            for (int i = 0; i < FeedLines; i++) _feed[i].Destroy();
            for (int i = 0; i < _board.Length; i++) _board[i].Destroy();
        }

        float TextScale { get { return _mp ? HaloHudKit.MpScale : 1f; } }
        HudColor TextColor { get { var g = HaloHudKit.Globals; return g != null && !g.TextColor.IsZero ? g.TextColor : new HudColor(128, 117, 186, 255); } }
        float UpTime { get { var g = HaloHudKit.Globals; return g != null && g.UpTime > 0f ? g.UpTime : 2f; } }
        float FadeTime { get { var g = HaloHudKit.Globals; return g != null && g.FadeTime > 0f ? g.FadeTime : 2f; } }

        // ------------------------------------------------------------------ events

        public void OnMessage(string text, float seconds)
        {
            if (string.IsNullOrEmpty(text)) { _helpUntil = -1f; return; }
            _help.Set(text, HaloHudKit.Font);
            _helpUntil = _time + (seconds > 0f ? seconds : UpTime);
            _helpFadeEnd = _helpUntil + FadeTime;
        }

        public void OnKilled(HaloKill k)
        {
            int me = HaloLocal.NetId;
            string victim = k.Victim == me ? HaloHud.Name(me) : HaloHud.Name(k.Victim);
            string line;
            if (k.Suicide || k.Killer == k.Victim && k.Victim >= 0) line = Fmt(HaloHudKit.Mp(81, "%s committed suicide"), victim, null);
            else if (k.Killer < 0) line = k.Splatter ? Fmt(HaloHudKit.Mp(77, "%s was killed by a vehicle"), victim, null) : Fmt(HaloHudKit.Mp(75, "%s died"), victim, null);
            else if (k.Betrayal && k.Killer == me) line = Fmt(HaloHudKit.Mp(82, "You betrayed %s"), victim, null);
            else if (k.Betrayal) line = Fmt(HaloHudKit.Mp(79, "%s was betrayed by %s"), victim, HaloHud.Name(k.Killer));
            else if (k.Killer == me) line = Fmt(HaloHudKit.Mp(88, "You killed %s"), victim, null);
            else line = Fmt(HaloHudKit.Mp(78, "%s was killed by %s"), victim, HaloHud.Name(k.Killer));
            AddFeed(line);
            if (k.Killer == me && k.Victim != me && !k.Betrayal && !k.Suicide)
            {
                float window = HaloMatchControl.MultiKillSeconds;
                _multi = _time - _lastKillAt <= window ? _multi + 1 : 1;
                _lastKillAt = _time;
                if (_multi == 2) AddFeed(HaloHudKit.Mp(85, "Double Kill!"));
                else if (_multi == 3) AddFeed(HaloHudKit.Mp(84, "Triple Kill!"));
                else if (_multi >= 4) AddFeed(HaloHudKit.Mp(83, "Killtacular!"));
                int spree = HaloLocal.Spree;
                if (spree == 5) AddFeed(HaloHudKit.Mp(87, "You are on a killing spree!"));
                else if (spree == 10) AddFeed(HaloHudKit.Mp(86, "Running Riot!"));
            }
            if (k.Victim == me) { _multi = 0; }
        }

        void AddFeed(string line)
        {
            for (int i = 0; i < FeedLines - 1; i++) { _feedText[i] = _feedText[i + 1]; _feedAt[i] = _feedAt[i + 1]; }
            _feedText[FeedLines - 1] = line; _feedAt[FeedLines - 1] = _time;
            for (int i = 0; i < FeedLines; i++) _feed[i].Set(_feedText[i], HaloHudKit.Font);
        }

        /// <summary>Fills Halo's printf-style string (%s, %d) with up to two values.</summary>
        public static string Fmt(string f, string a, string b)
        {
            if (f == null) return "";
            var sb = new System.Text.StringBuilder(f.Length + 16);
            int arg = 0;
            for (int i = 0; i < f.Length; i++)
            {
                char c = f[i];
                if (c == '%' && i + 1 < f.Length)
                {
                    char n = f[i + 1];
                    if (n == 's' || n == 'd') { sb.Append(arg++ == 0 ? a : b); i++; continue; }
                    if (n == '%') { sb.Append('%'); i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ per frame

        void TickText(bool alive, bool ending, float dt)
        {
            float ts = TextScale;
            Color tc = HaloHudKit.Additive(TextColor);
            var g = HaloHudKit.Globals;

            // HUD message under the weapon panel (hud globals' anchor offset). Campaign: the script's help text stays while it is shown
            // (hud globals' "hud help" colours, flashing while enable_hud_help_flash is on); other messages use the same line when it is free.
            float hx = g != null ? g.Place.X * ts + 12f * ts : 9f, hy = g != null ? g.Place.Y * ts : 45f;
            string scriptHelp = _campaign ? HaloScriptFns.HelpText : null;
            if (!string.IsNullOrEmpty(scriptHelp) && !_hidden && alive)
            {
                _help.Set(scriptHelp, HaloHudKit.Font);
                HudColor hc = g != null && !g.HelpColor.Default.IsZero ? g.HelpColor.Default : TextColor;
                if (HaloHud.HelpFlashing && g != null && !g.HelpColor.Flashing.IsZero)
                {
                    float period = g.HelpColor.Period > 0.05f ? g.HelpColor.Period : 1f;
                    int flashes = g.HelpColor.Count > 0 ? g.HelpColor.Count : 3;
                    if (HaloHud.HelpFlashTime < period * flashes) hc = HaloHudKit.FlashColor(g.HelpColor, true, HaloHud.HelpFlashTime);
                }
                _help.Place(HudAnchor.TopLeft, hx, hy, ts * 0.75f);
                _help.Image.color = HaloHudKit.Additive(hc);
                HaloHudKit.SetOn(_help.Image, true);
                _helpUntil = -1f;
            }
            else if (_helpUntil > 0f && _time < _helpFadeEnd && _help.Text != null && !ending && alive && !_hidden)
            {
                float a = _time < _helpUntil ? 1f : 1f - (_time - _helpUntil) / Mathf.Max(0.05f, FadeTime);
                _help.Place(HudAnchor.TopLeft, hx, hy, ts * 0.75f);
                _help.Image.color = new Color(tc.r, tc.g, tc.b, tc.a * a);
                HaloHudKit.SetOn(_help.Image, true);
            }
            else HaloHudKit.SetOn(_help.Image, false);

            // campaign objective (hud_set_objective_text): shown under the help line for a while when it changes
            string obj = _campaign ? HaloScriptFns.Objective : null;
            if (!string.IsNullOrEmpty(obj) && obj != _objSeen) { _objSeen = obj; _objAt = _time; _objective.Set(obj, HaloHudKit.Font); }
            float objAge = _time - _objAt;
            if (_campaign && _objective.Text != null && objAge < ObjectiveSeconds + FadeTime && !_hidden && alive)
            {
                float a = objAge < ObjectiveSeconds ? 1f : 1f - (objAge - ObjectiveSeconds) / Mathf.Max(0.05f, FadeTime);
                HudColor oc = g != null && !g.ObjectiveColor.Default.IsZero ? g.ObjectiveColor.Default : TextColor;
                Color c = HaloHudKit.Additive(oc);
                float lh = (HaloHudKit.Font != null ? HaloHudKit.Font.Ascend + HaloHudKit.Font.Descend + 1 : 22) * ts * 0.75f;
                _objective.Place(HudAnchor.TopLeft, hx, hy + (_help.Image.enabled ? lh + 2f : 0f), ts * 0.75f);
                _objective.Image.color = new Color(c.r, c.g, c.b, c.a * a);
                HaloHudKit.SetOn(_objective.Image, true);
            }
            else HaloHudKit.SetOn(_objective.Image, false);

            // the kill feed: newest line at the bottom, above the motion tracker
            float lineH = (HaloHudKit.Font != null ? HaloHudKit.Font.Ascend + HaloHudKit.Font.Descend + 1 : 22) * ts * 0.75f;
            float baseY = TrackerTop() + TrackerMargin + 4f;
            int shown = 0;
            for (int i = FeedLines - 1; i >= 0; i--)
            {
                var l = _feed[i];
                float age = _time - _feedAt[i];
                if (string.IsNullOrEmpty(_feedText[i]) || age > FeedSeconds + FadeTime || ending || _campaign || _hidden) { HaloHudKit.SetOn(l.Image, false); continue; }
                float a = age < FeedSeconds ? 1f : 1f - (age - FeedSeconds) / Mathf.Max(0.05f, FadeTime);
                l.Place(HudAnchor.BottomLeft, 12f * ts, baseY + shown * lineH, ts * 0.75f);
                l.Image.color = new Color(tc.r, tc.g, tc.b, tc.a * a);
                HaloHudKit.SetOn(l.Image, true);
                shown++;
            }

            // centre: respawn countdown / out of lives
            if (!alive && !ending && !_campaign)
            {
                string s = null;
                if (HaloMatchControl.OutOfLives) s = HaloHudKit.Mp(96, "You are out of lives");
                else if (HaloLocal.RespawnIn > 0f)
                {
                    int n = Mathf.CeilToInt(HaloLocal.RespawnIn);
                    if (n != _lastRespawn || _center.Text == null) { _lastRespawn = n; _center.Set(Fmt(HaloHudKit.Mp(97, "Rejoin in %d"), n.ToString(), null), HaloHudKit.Font); }
                }
                if (s != null) _center.Set(s, HaloHudKit.Font);
                _center.Place(HudAnchor.Center, 0f, 40f, ts);
                _center.Image.color = tc;
                HaloHudKit.SetOn(_center.Image, _center.Text != null);
            }
            else { HaloHudKit.SetOn(_center.Image, false); _lastRespawn = -1; }
            HaloHudKit.SetOn(_center2.Image, false);

            TickScore(ending, ts, tc);
            TickBoard(ending, ts, tc, dt);
        }

        float TrackerTop()
        {
            var u = HaloHudKit.Unit;
            if (u == null || u.MotionBack == null) return 60f;
            var sp = HaloHudKit.Sprite(u.MotionBack.Bitmap, u.MotionBack.Sequence);
            float s = HaloHudKit.SizeScale(u.MotionBack.Place, _mp);
            float top = sp != null ? sp.H * s : 96f;
            // the disc fills the lower part of its picture: start just above the disc (centre + radius)
            float os = HaloHudKit.OffsetScale(u.MotionCenter, _mp);
            float disc = (u.MotionCenter.Y + Mathf.Min(u.MotionCenter.X, u.MotionCenter.Y)) * os;
            return disc > 4f ? Mathf.Min(top, disc) : top;
        }

        // ------------------------------------------------------------------ score and time (bottom right)

        void TickScore(bool ending, float ts, Color tc)
        {
            var g = HaloHudKit.Globals;
            var digits = g != null ? g.Digits : null;
            bool on = HaloSession.Playing && !ending && digits != null && !_campaign && !_hidden;
            TickScriptTimer(ts);
            HaloHudKit.SetOn(_scoreBar1, on); HaloHudKit.SetOn(_scoreBar2, on);
            if (!on) { _score1.Hide(); _score2.Hide(); _clock.Hide(); HaloHudKit.SetOn(_scoreName1.Image, false); HaloHudKit.SetOn(_scoreName2.Image, false); return; }
            bool teams = HaloSession.Rules != null && HaloSession.Rules.Teams;
            int mine, other; string n1, n2; Color c1, c2;
            if (teams)
            {
                int myTeam = HaloLocal.Team == HaloTeams.Blue ? HaloTeams.Blue : HaloTeams.Red;
                int theirs = 1 - myTeam;
                mine = HaloMatchControl.TeamScore(myTeam); other = HaloMatchControl.TeamScore(theirs);
                n1 = HaloHudKit.Mp(myTeam == HaloTeams.Red ? 17 : 18, HaloTeams.Name(myTeam));
                n2 = HaloHudKit.Mp(theirs == HaloTeams.Red ? 17 : 18, HaloTeams.Name(theirs));
                c1 = HaloTeams.Of(myTeam); c2 = HaloTeams.Of(theirs);
            }
            else
            {
                mine = HaloLocal.Score;
                var leader = HaloMatchControl.Leader;
                HaloMatchPlayer best = null;
                // the leader, or (when we lead) the best of the others
                for (int i = 0; i < HaloRoster.Players.Count; i++) { var p = HaloRoster.Players[i]; if (!p.Local && (best == null || p.Score > best.Score)) best = p; }
                if (leader != null && !leader.Local) best = leader;
                other = best != null ? best.Score : 0;
                n1 = HaloHud.Name(HaloLocal.NetId); if (string.IsNullOrEmpty(HaloLocal.Nick)) n1 = "You";
                n2 = best != null ? best.Name : "";
                c1 = HaloRoster.Me.Color; c2 = best != null ? best.Color : Color.grey;
            }
            float row = 12f * ts, right = 12f * ts, bottom = 10f * ts, barW = 4f * ts;
            var num = _scoreNum; num.Place = new HudPlace { X = 0, Y = 0, WScale = 1f, HScale = 1f, Flags = 3 }; num.MaxDigits = 3;
            num.Place.X = (short)Mathf.RoundToInt(right + 6f * ts); num.Place.Y = (short)Mathf.RoundToInt(bottom + row + 2f);
            float digitScale = ts;
            // own score on top, the other below (Halo: you and the leader / your team and the other)
            _score1.Draw(Scaled(num, digitScale), HudAnchor.BottomRight, false, mine, tc);
            num.Place.Y = (short)Mathf.RoundToInt(bottom);
            _score2.Draw(Scaled(num, digitScale), HudAnchor.BottomRight, false, other, new Color(tc.r, tc.g, tc.b, tc.a * 0.75f));
            HaloHudKit.Place(_scoreBar1.rectTransform, HudAnchor.BottomRight, right, bottom + row + 2f, barW, row - 2f);
            HaloHudKit.Place(_scoreBar2.rectTransform, HudAnchor.BottomRight, right, bottom, barW, row - 2f);
            _scoreBar1.color = new Color(c1.r, c1.g, c1.b, 0.85f); _scoreBar2.color = new Color(c2.r, c2.g, c2.b, 0.6f);
            _scoreName1.Set(n1, HaloHudKit.SmallFont);
            _scoreName2.Set(n2, HaloHudKit.SmallFont);
            float nameX = right + 6f * ts + 3f * 9f * digitScale * 0.75f + 6f * ts;
            _scoreName1.Place(HudAnchor.BottomRight, nameX, bottom + row + 1f, ts * 0.6f, 1f);
            _scoreName2.Place(HudAnchor.BottomRight, nameX, bottom - 1f, ts * 0.6f, 1f);
            _scoreName1.Image.color = tc; _scoreName2.Image.color = new Color(tc.r, tc.g, tc.b, tc.a * 0.75f);
            HaloHudKit.SetOn(_scoreName1.Image, !string.IsNullOrEmpty(n1)); HaloHudKit.SetOn(_scoreName2.Image, !string.IsNullOrEmpty(n2));
            _s1 = mine; _s2 = other;

            // time left (minutes:seconds) above the scores
            float left = HaloMatchControl.TimeLeft;
            if (left >= 0f)
            {
                int secs = Mathf.CeilToInt(left);
                bool hurry = secs <= 60;
                HudColor hc = g.NotMuchTimeLeft.Default.IsZero ? TextColor : HaloHudKit.FlashColor(g.NotMuchTimeLeft, hurry, _time);
                num.Place.Y = (short)Mathf.RoundToInt(bottom + 2f * row + 6f);
                num.MaxDigits = 0;
                // mm:ss drawn as the number mm.ss with the colon picture (the digits sheet carries ':' as sprite 11)
                _clock.DrawClock(Scaled(num, digitScale), HudAnchor.BottomRight, secs / 60, secs % 60, HaloHudKit.Additive(hurry ? hc : TextColor));
            }
            else _clock.Hide();
        }

        static HudNumberEl Scaled(HudNumberEl n, float s)
        {
            n.Place.WScale = s; n.Place.HScale = s;
            return n;
        }

        // ------------------------------------------------------------------ scoreboard

        void TickBoard(bool ending, float ts, Color tc, float dt)
        {
            bool on = HaloHud.ScoreboardShown || ending;
            HaloHudKit.SetActive(_layerBoard.gameObject, on);
            if (!on) return;
            _boardRefresh -= dt;
            if (_boardRefresh > 0f) return;
            _boardRefresh = 0.25f;
            if (_campaign) { ObjectivesBoard(tc); return; }

            bool teams = HaloSession.Rules != null && HaloSession.Rules.Teams;
            _sorted.Clear();
            _sorted.AddRange(HaloRoster.Players);
            _sorted.Sort(teams ? (System.Comparison<HaloMatchPlayer>)CompareTeam : CompareScore);

            var font = HaloHudKit.Font;
            float s = 0.75f;                             // ours: the board's font scale on the 480-line canvas
            float rowH = (font != null ? font.Ascend + font.Descend + 2 : 22) * s;
            float w = 440f, x0 = -w * 0.5f, top = -150f;
            float[] col = { 10f, 70f, 260f, 320f, 380f };    // Place, Name, Kills, Assists, Deaths (ours)

            // title: the game over result, or the game type
            string title;
            if (ending) title = ResultLine(teams);
            else title = GameTypeName();
            _result.Set(title, font);
            _result.Place(HudAnchor.Center, 0f, top - 4f, 1f, 0.5f);
            _result.Image.color = tc;
            HaloHudKit.SetOn(_result.Image, true);
            HaloHudKit.SetOn(_boardTitle.Image, false);

            int rows = 1;
            SetCell(0, 0, HaloHudKit.Mp(67, "Place"), col, x0, top + 26f, s, tc);
            SetCell(0, 1, HaloHudKit.Mp(68, "Name"), col, x0, top + 26f, s, tc);
            SetCell(0, 2, HaloHudKit.Mp(69, "Kills"), col, x0, top + 26f, s, tc);
            SetCell(0, 3, HaloHudKit.Mp(70, "Assists"), col, x0, top + 26f, s, tc);
            SetCell(0, 4, HaloHudKit.Mp(71, "Deaths"), col, x0, top + 26f, s, tc);
            HaloHudKit.SetOn(_boardRowBack[0], false);
            int lastTeam = int.MinValue, place = 0, prevScore = int.MinValue, shownPlace = 0;
            HaloHudKit.SetOn(_boardMe, false);
            for (int i = 0; i < _sorted.Count && rows < BoardRows; i++)
            {
                var p = _sorted[i];
                float y = top + 26f + rows * rowH;
                if (teams && p.Team != lastTeam && rows < BoardRows - 1)
                {
                    lastTeam = p.Team;
                    int score = p.Team >= 0 ? HaloMatchControl.TeamScore(p.Team) : 0;
                    string head = p.Team >= 0 ? HaloHudKit.Mp(p.Team == HaloTeams.Red ? 17 : 18, HaloTeams.Name(p.Team)) : HaloTeams.Name(p.Team);
                    Color c = HaloTeams.Of(p.Team);
                    SetCell(rows, 0, "", col, x0, y, s, tc);
                    SetCell(rows, 1, head, col, x0, y, s, new Color(c.r, c.g, c.b, 1f));
                    SetCell(rows, 2, score.ToString(), col, x0, y, s, new Color(c.r, c.g, c.b, 1f));
                    SetCell(rows, 3, "", col, x0, y, s, tc); SetCell(rows, 4, "", col, x0, y, s, tc);
                    RowBack(rows, x0, y, w, rowH, new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 0.55f));
                    rows++; place = 0; prevScore = int.MinValue;
                    y = top + 26f + rows * rowH;
                }
                place++;
                if (p.Score != prevScore) shownPlace = place;
                prevScore = p.Score;
                Color pc = p.Local ? new Color(1f, 1f, 1f, 1f) : tc;
                SetCell(rows, 0, HaloHudKit.Mp(35 + Mathf.Clamp(shownPlace, 1, 16), shownPlace.ToString()), col, x0, y, s, pc);
                SetCell(rows, 1, string.IsNullOrEmpty(p.Name) ? HaloHud.Name(p.Id) : p.Name, col, x0, y, s, pc);
                SetCell(rows, 2, p.Kills.ToString(), col, x0, y, s, pc);
                SetCell(rows, 3, p.Assists.ToString(), col, x0, y, s, pc);
                SetCell(rows, 4, p.Deaths.ToString(), col, x0, y, s, pc);
                Color pcol = p.Color;
                RowBack(rows, x0, y, w, rowH, p.Local ? new Color(0.16f, 0.59f, 1f, 0.35f) : new Color(pcol.r * 0.2f, pcol.g * 0.2f, pcol.b * 0.2f, 0.25f));
                rows++;
            }
            for (int r = rows; r < BoardRows; r++)
            {
                HaloHudKit.SetOn(_boardRowBack[r], false);
                for (int c = 0; c < BoardCols; c++) HaloHudKit.SetOn(_board[r * BoardCols + c].Image, false);
            }
            float hgt = 26f + rows * rowH + 10f;
            HaloHudKit.Place(_boardBack.rectTransform, HudAnchor.Center, 0f, top - 24f + (hgt + 24f) * 0.5f, w + 20f, hgt + 24f, (w + 20f) * 0.5f, (hgt + 24f) * 0.5f);
            _boardBack.color = new Color(0f, 0.045f, 0.10f, 0.72f);
        }

        void SetCell(int row, int c, string text, float[] col, float x0, float y, float s, Color color)
        {
            var l = _board[row * BoardCols + c];
            l.Set(text, HaloHudKit.Font);
            if (string.IsNullOrEmpty(text)) { HaloHudKit.SetOn(l.Image, false); return; }
            l.Place(HudAnchor.Center, x0 + col[c], y, s, 0f);
            l.Rect.pivot = new Vector2(0f, 1f);
            l.Image.color = color;
            HaloHudKit.SetOn(l.Image, true);
        }

        void RowBack(int row, float x0, float y, float w, float h, Color c)
        {
            var img = _boardRowBack[row];
            HaloHudKit.Place(img.rectTransform, HudAnchor.Center, x0, y, w, h - 1f, 0f, 0f);
            img.color = c;
            HaloHudKit.SetOn(img, true);
        }

        static int CompareScore(HaloMatchPlayer a, HaloMatchPlayer b)
        {
            int c = b.Score.CompareTo(a.Score);
            if (c != 0) return c;
            c = b.Kills.CompareTo(a.Kills);
            return c != 0 ? c : a.Deaths.CompareTo(b.Deaths);
        }

        static int CompareTeam(HaloMatchPlayer a, HaloMatchPlayer b)
        {
            int ta = a.Team < 0 ? 99 : a.Team, tb = b.Team < 0 ? 99 : b.Team;
            if (ta != tb)
            {
                int sa = a.Team >= 0 ? HaloMatchControl.TeamScore(a.Team) : int.MinValue, sb = b.Team >= 0 ? HaloMatchControl.TeamScore(b.Team) : int.MinValue;
                if (sa != sb) return sb.CompareTo(sa);
                return ta.CompareTo(tb);
            }
            return CompareScore(a, b);
        }

        /// <summary>Halo's end-of-game line (ui multiplayer_game_text 55-59).</summary>
        static string ResultLine(bool teams)
        {
            if (teams)
            {
                int w = HaloMatchControl.WinnerTeam;
                if (w == HaloTeams.None) return HaloHudKit.Mp(55, "Game ends in a draw");
                return w == HaloLocal.Team ? HaloHudKit.Mp(58, "Your team won") : HaloHudKit.Mp(56, "Your team lost");
            }
            int id = HaloMatchControl.WinnerId;
            if (id == int.MinValue) return HaloHudKit.Mp(55, "Game ends in a draw");
            return id == HaloLocal.NetId ? HaloHudKit.Mp(59, "You won") : HaloHudKit.Mp(57, "You lost");
        }

        static string GameTypeName()
        {
            var r = HaloSession.Rules;
            if (r == null) return HaloHudKit.Mp(4, "Slayer");
            switch (r.Type)
            {
                case HaloScenario.GameType.Ctf: return HaloHudKit.Mp(3, "Capture the Flag");
                case HaloScenario.GameType.Oddball: return HaloHudKit.Mp(5, "Oddball");
                case HaloScenario.GameType.King: return HaloHudKit.Mp(6, "King of the Hill");
                case HaloScenario.GameType.Race: return HaloHudKit.Mp(7, "Race");
                default: return HaloHudKit.Mp(4, "Slayer");
            }
        }

        // ------------------------------------------------------------------ campaign

        /// <summary>The campaign's "scores" key: the mission and its current objective (Halo shows them on the pause screen).</summary>
        void ObjectivesBoard(Color tc)
        {
            var font = HaloHudKit.Font;
            float top = -60f;
            _result.Set(HaloCampaign.Label(HaloCampaign.Mission), font);
            _result.Place(HudAnchor.Center, 0f, top, 1f, 0.5f);
            _result.Image.color = tc;
            HaloHudKit.SetOn(_result.Image, !string.IsNullOrEmpty(_result.Text));
            HaloHudKit.SetOn(_boardTitle.Image, false);
            string obj = HaloScriptFns.Objective;
            var l = _board[0];
            l.Set(string.IsNullOrEmpty(obj) ? "" : obj, font);
            if (!string.IsNullOrEmpty(obj)) { l.Place(HudAnchor.Center, 0f, top + 30f, 0.75f, 0.5f); l.Image.color = tc; HaloHudKit.SetOn(l.Image, true); }
            for (int i = 1; i < _board.Length; i++) HaloHudKit.SetOn(_board[i].Image, false);
            for (int r = 0; r < BoardRows; r++) HaloHudKit.SetOn(_boardRowBack[r], false);
            HaloHudKit.SetOn(_boardMe, false);
            float w = Mathf.Max(300f, (l.PixelWidth + 40) * 0.75f), h = 80f;
            HaloHudKit.Place(_boardBack.rectTransform, HudAnchor.Center, 0f, top + 14f, w, h, w * 0.5f, h * 0.5f);
            _boardBack.color = new Color(0f, 0.045f, 0.10f, 0.72f);
        }

        /// <summary>The script's HUD timer (d40's countdown): m:ss in the counter digits at hud_set_timer_position's corner, flashing in
        /// hud globals' "time out" colours below the warning time.</summary>
        void TickScriptTimer(float ts)
        {
            var g = HaloHudKit.Globals;
            if (!HaloHud.TimerShown || _hidden || g == null || g.Digits == null) { _timer.Hide(); return; }
            int secs = Mathf.CeilToInt(HaloHud.TimerLeft);
            bool warn = HaloHud.TimerWarningNow;
            HudColor c = warn && !g.TimeOutFlash.Default.IsZero ? HaloHudKit.FlashColor(g.TimeOutFlash, true, _time) : TextColor;
            var n = _timerNum;
            n.Place = new HudPlace { X = (short)(HaloHud.TimerX + 12), Y = (short)(HaloHud.TimerY + 12), WScale = 1.5f, HScale = 1.5f, Flags = 3 };   // ours: a margin, 1.5x digits
            _timer.DrawClock(n, HaloHud.TimerAnchor, secs / 60, secs % 60, HaloHudKit.Additive(c));
        }
        readonly HudNumberEl _timerNum = new HudNumberEl();
    }
}

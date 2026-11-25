using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// What the HUD draws over the world: the objective waypoints of hud globals (flags, the ball, the hill's crown, the next race
    /// checkpoint; the "on screen" picture over the object, the "off screen" arrow at the screen's edge pointing to it, team colours and
    /// opacity from the tag), the multiplayer HUD bitmap's marker over allies' heads in team games, and Halo's red damage arrows
    /// (hud globals "hud damage": the multiplayer picture at the screen's edges, towards where the hit came from).
    /// </summary>
    public sealed partial class HaloHudView
    {
        const int MaxWaypoints = 8, MaxAllies = 15;
        readonly RawImage[] _wp = new RawImage[MaxWaypoints];
        readonly RawImage[] _allies = new RawImage[MaxAllies];
        readonly RawImage[] _dmg = new RawImage[4];
        readonly float[] _dmgLevel = new float[4];
        int _wpUsed;

        void BuildWorld()
        {
            for (int i = 0; i < MaxWaypoints; i++) { _wp[i] = HaloHudKit.Raw(_layerWorld, "Waypoint " + i); _wp[i].enabled = false; }
            for (int i = 0; i < MaxAllies; i++) { _allies[i] = HaloHudKit.Raw(_layerWorld, "Ally " + i); _allies[i].enabled = false; }
            for (int i = 0; i < 4; i++) { _dmg[i] = HaloHudKit.Raw(_layerDamage, "Damage " + i); _dmg[i].enabled = false; }
        }

        void TeardownWorld() { }

        void OnModeChanged() { ClearDamage(); }

        void ClearDamage() { for (int i = 0; i < 4; i++) _dmgLevel[i] = 0f; }

        public void OnDamaged(Vector3 from, float amount)
        {
            Camera head = _lab ? _labCam : HaloPlayer.Head();
            if (head == null) return;
            Vector3 d = from - head.transform.position; d.y = 0f;
            Vector3 f = head.transform.forward; f.y = 0f;
            if (d.sqrMagnitude < 1e-4f || f.sqrMagnitude < 1e-4f) { for (int i = 0; i < 4; i++) _dmgLevel[i] = 1f; return; }
            d.Normalize(); f.Normalize();
            Vector3 r = new Vector3(f.z, 0f, -f.x);
            float fw = Vector3.Dot(d, f), rt = Vector3.Dot(d, r);
            float lvl = Mathf.Clamp(0.6f + amount, 0.6f, 1f);
            // 0 front (top edge), 1 back (bottom), 2 left, 3 right
            if (fw > 0.38f) _dmgLevel[0] = Mathf.Max(_dmgLevel[0], lvl * fw);
            if (fw < -0.38f) _dmgLevel[1] = Mathf.Max(_dmgLevel[1], lvl * -fw);
            if (rt < -0.38f) _dmgLevel[2] = Mathf.Max(_dmgLevel[2], lvl * -rt);
            if (rt > 0.38f) _dmgLevel[3] = Mathf.Max(_dmgLevel[3], lvl * rt);
        }

        void TickWorld(bool show, Camera head, float dt)
        {
            _wpUsed = 0;
            if (show && head != null) { Waypoints(head); NavPoints(head); }
            for (int i = _wpUsed; i < MaxWaypoints; i++) HaloHudKit.SetOn(_wp[i], false);
            Allies(show, head);
            Damage(show, dt);
        }

        /// <summary>Where a world point lands on the HUD frame (canvas units from the centre, y up); false when it is behind the view.</summary>
        bool ToFrame(Camera head, Vector3 world, out Vector2 p)
        {
            p = Vector2.zero;
            if (_vr || _lab)
            {
                Vector3 v = Quaternion.Inverse(_visorRot) * (world - head.transform.position);
                if (v.z < 0.05f) { p = new Vector2(v.x, v.y).normalized * 10000f; return false; }
                float dist = (_lab && !_labVr) ? 0.5f : VrDistance;
                float k = transform.localScale.y > 1e-6f ? transform.localScale.y : 1f;
                p = new Vector2(v.x, v.y) * (dist / v.z) / k;
                return true;
            }
            Vector3 vp = head.WorldToViewportPoint(world);
            Rect r = _frame.rect;
            if (vp.z < 0.05f) { p = new Vector2(0.5f - vp.x, 0.5f - vp.y).normalized * 10000f; return false; }
            p = new Vector2((vp.x - 0.5f) * r.width, (vp.y - 0.5f) * r.height);
            return true;
        }

        void Waypoints(Camera head)
        {
            var g = HaloHudKit.Globals;
            if (g == null || g.ArrowBitmap == null || !HaloObjectives.Active) return;
            var rules = HaloSession.Rules;
            bool teams = rules != null && rules.Teams;
            int myTeam = HaloLocal.Team;
            var type = rules != null ? rules.Type : HaloScenario.GameType.Slayer;
            if (type == HaloScenario.GameType.Ctf)
            {
                Flag(head, HaloObjectives.RedFlag, myTeam);
                Flag(head, HaloObjectives.BlueFlag, myTeam);
            }
            else if (type == HaloScenario.GameType.Oddball)
            {
                var b = HaloObjectives.Ball;
                if (b.Present && HaloLocal.Carry != HaloObjectives.CarryBall)
                {
                    bool ally = false;
                    if (b.State == HaloObjectives.State.Carried && teams) { var c = HaloRoster.Find(b.Carrier); ally = c != null && c.Team == myTeam; }
                    Arrow(head, ally ? "ball_blue" : "ball_red", b.Position + Vector3.up * 1.0f);
                }
            }
            else if (type == HaloScenario.GameType.King)
            {
                var h = HaloObjectives.CurrentHill;
                if (h != null)
                {
                    int owner = HaloObjectives.HillOwner;
                    bool ally = owner != HaloObjectives.HillNobody && owner != HaloObjectives.HillContested && (teams ? owner == myTeam : owner == HaloLocal.NetId);
                    Arrow(head, ally ? "crown_blue" : "crown_red", h.Center + Vector3.up * 1.0f);
                }
            }
            else if (type == HaloScenario.GameType.Race)
            {
                Vector3 at;
                if (HaloObjectives.NextCheckpoint(out at)) Arrow(head, "target_blue", at + Vector3.up * 1.0f);
            }
        }

        /// <summary>The scripts' nav points (activate_team_nav_point_flag / _object): hud globals' arrow of that type over the flag / object.</summary>
        void NavPoints(Camera head)
        {
            var navs = HaloHud.NavPoints;
            for (int i = 0; i < navs.Count; i++)
            {
                var n = navs[i];
                Vector3 at = n.At;
                if (n.Follow != null) at = n.Follow.position;
                else if (n.Follow == null && n.At == Vector3.zero) continue;
                Arrow(head, string.IsNullOrEmpty(n.Type) ? "default" : n.Type, at + Vector3.up * (n.OffsetUnits * HaloSpace.Unit));
            }
        }

        void Flag(Camera head, HaloObjectives.Item f, int myTeam)
        {
            if (!f.Present) return;
            bool mine = f.Team == myTeam;
            if (HaloLocal.Carry == f.Kind) return;                              // we carry it
            if (mine && f.State == HaloObjectives.State.Home) return;            // our flag at home: nothing to chase
            Arrow(head, f.Team == HaloTeams.Blue ? "flag_blue" : "flag_red", f.Position + Vector3.up * 1.2f);
        }

        void Arrow(Camera head, string name, Vector3 world)
        {
            if (_wpUsed >= MaxWaypoints) return;
            var g = HaloHudKit.Globals;
            HudWaypointArrow def = null;
            for (int i = 0; i < g.Arrows.Count; i++) if (g.Arrows[i].Name == name) { def = g.Arrows[i]; break; }
            if (def == null) for (int i = 0; i < g.Arrows.Count; i++) if (g.Arrows[i].Name == "default") { def = g.Arrows[i]; break; }
            if (def == null) return;
            Vector2 p;
            bool front = ToFrame(head, world, out p);
            Rect r = _frame.rect;
            float ts = TextScale;
            float mx = r.width * 0.5f - g.RightOffset * ts, my = r.height * 0.5f - g.TopOffset * ts;
            bool onScreen = front && Mathf.Abs(p.x) <= mx && Mathf.Abs(p.y) <= my;
            int seq = onScreen ? def.OnScreen : def.OffScreen;
            var sp = HaloHudKit.Sprite(g.ArrowBitmap, seq);
            var img = _wp[_wpUsed++];
            if (sp == null) { HaloHudKit.SetOn(img, false); return; }
            img.texture = sp.Tex; img.uvRect = sp.Uv;
            float s = 0.5f * ts;                                               // ours: the 34-pixel sprites at half size
            float angle = 0f;
            if (!onScreen)
            {
                // clamp onto the edge rectangle along the direction from the centre
                Vector2 d = p.sqrMagnitude > 1e-6f ? p.normalized : Vector2.up;
                float t = Mathf.Min(mx / Mathf.Max(1e-4f, Mathf.Abs(d.x)), my / Mathf.Max(1e-4f, Mathf.Abs(d.y)));
                p = d * t;
                if ((def.Flags & 1) == 0) angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg + 90f;     // the off-screen arrow's picture points down
            }
            HaloHudKit.Place(img.rectTransform, HudAnchor.Center, p.x, -p.y, sp.W * s, sp.H * s, sp.RegX * s, sp.RegY * s);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            img.color = HaloHudKit.Additive(def.Color, def.Opacity > 0f ? def.Opacity : 0.5f);
            HaloHudKit.SetOn(img, true);
        }

        void Allies(bool show, Camera head)
        {
            int n = 0;
            var g = HaloHudKit.Globals;
            bool teams = HaloSession.Rules != null && HaloSession.Rules.Teams;
            var sp = g != null && show && teams && !_campaign && head != null ? HaloHudKit.Sprite(g.MultiplayerBitmap, 0) : null;
            if (sp != null)
            {
                var list = HaloRoster.Players;
                float s = 0.3f * TextScale;                                    // ours: the 64x32 marker drawn small
                for (int i = 0; i < list.Count && n < MaxAllies; i++)
                {
                    var p = list[i];
                    if (p.Local || !p.Alive || p.Team != HaloLocal.Team || p.Team == HaloTeams.None) continue;
                    if ((p.Position - head.transform.position).sqrMagnitude > 90f * 90f) continue;
                    Vector2 at;
                    if (!ToFrame(head, p.Position + Vector3.up * 2.15f, out at)) continue;
                    var img = _allies[n++];
                    img.texture = sp.Tex; img.uvRect = sp.Uv;
                    HaloHudKit.Place(img.rectTransform, HudAnchor.Center, at.x, -at.y, sp.W * s, sp.H * s, sp.RegX * s, sp.H * s);
                    img.color = new Color(1f, 1f, 1f, 0.85f);
                    HaloHudKit.SetOn(img, true);
                }
            }
            for (int i = n; i < MaxAllies; i++) HaloHudKit.SetOn(_allies[i], false);
        }

        void Damage(bool show, float dt)
        {
            var g = HaloHudKit.Globals;
            var sp = g != null && show ? HaloHudKit.Sprite(g.DamageBitmap, _mp ? g.DamageMpSequence : g.DamageSequence) : null;
            Rect r = _frame.rect;
            float ts = TextScale;
            for (int i = 0; i < 4; i++)
            {
                _dmgLevel[i] = Mathf.Max(0f, _dmgLevel[i] - dt);                // ours: a second to fade
                var img = _dmg[i];
                if (sp == null || _dmgLevel[i] <= 0.01f) { HaloHudKit.SetOn(img, false); continue; }
                img.texture = sp.Tex; img.uvRect = sp.Uv;
                float w = sp.W * ts, h = sp.H * ts;
                float x = 0f, y = 0f, rot = 0f;
                switch (i)
                {
                    case 0: y = -(r.height * 0.5f - g.DamageTop * ts - h * 0.5f); rot = 180f; break;
                    case 1: y = r.height * 0.5f - g.DamageBottom * ts - h * 0.5f; rot = 0f; break;
                    case 2: x = -(r.width * 0.5f - g.DamageLeft * ts - h * 0.5f); rot = -90f; break;
                    default: x = r.width * 0.5f - g.DamageRight * ts - h * 0.5f; rot = 90f; break;
                }
                HaloHudKit.Place(img.rectTransform, HudAnchor.Center, x, y, w, h, w * 0.5f, h * 0.5f);
                img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rot);
                img.color = HaloHudKit.Additive(g.DamageColor, Mathf.Clamp01(_dmgLevel[i]));
                HaloHudKit.SetOn(img, true);
            }
        }
    }
}

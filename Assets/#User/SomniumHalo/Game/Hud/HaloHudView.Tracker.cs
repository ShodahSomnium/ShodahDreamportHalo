using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The motion tracker (bottom left): the unit HUD's sensor background and foreground, the globals' sweep ring and blip pictures,
    /// range = hud globals "motion sensor range" (world units). As in Halo it shows only players who move faster than a walk or have just
    /// fired (crouch-walking and standing still hide you); enemies red, allies yellow; positions are taken at each sweep and the blips fade
    /// until the next one. Facing up = where the player looks.
    /// </summary>
    public sealed partial class HaloHudView
    {
        /// <summary>ours: seconds between sweeps, the speed (m/s) above which a player shows, how long a shot shows him (s), the blip size (canvas px).</summary>
        public static float SweepSeconds = 1f, BlipSpeed = 1.2f, BlipFireSeconds = 0.6f, BlipSize = 8f, TrackerMargin = 10f;
        static readonly HudColor EnemyBlip = new HudColor(0, 255, 85, 43);      // Halo's HUD red (hud globals "default_red" arrow #FF552B)
        static readonly HudColor AllyBlip = new HudColor(0, 255, 223, 0);       // Halo's HUD yellow (the unit HUD's "medium health" colour)

        RawImage _trackBack, _trackFore, _sweep;
        readonly RawImage[] _blips = new RawImage[24];
        readonly Vector2[] _blipAt = new Vector2[24];
        readonly bool[] _blipAlly = new bool[24];
        int _blipCount;
        float _sweepT;
        RectTransform _trackRoot;

        sealed class Track { public Vector3 Last; public float Speed; public float FiredAt = -100f; public bool Has; }
        readonly Dictionary<int, Track> _tracks = new Dictionary<int, Track>();

        void BuildTracker()
        {
            _trackRoot = HaloHudKit.Node(_layerTracker, "Tracker");
            Stretch(_trackRoot);
            _trackBack = HaloHudKit.Raw(_trackRoot, "Sensor background");
            _sweep = HaloHudKit.Raw(_trackRoot, "Sweep");
            for (int i = 0; i < _blips.Length; i++) { _blips[i] = HaloHudKit.Raw(_trackRoot, "Blip " + i); _blips[i].enabled = false; }
            _trackFore = HaloHudKit.Raw(_trackRoot, "Sensor foreground");
        }

        /// <summary>Lab / debugging: the blips of the last sweep and a line about the tracked players.</summary>
        public int BlipCount { get { return _blipCount; } }
        public string TrackerDebug()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _tracks) sb.Append(kv.Key).Append(":").Append(kv.Value.Speed.ToString("0.0")).Append(" ");
            return "blips " + _blipCount + ", roster " + HaloRoster.Players.Count + ", speeds " + sb + "; sensor back " + Describe(_trackBack) + ", fore " + Describe(_trackFore);
        }

        static string Describe(RawImage i)
        {
            if (i == null) return "none";
            var rt = i.rectTransform;
            return (i.enabled ? "on " : "off ") + (i.texture != null ? i.texture.name : "no texture") + " size " + rt.rect.size + " at " + rt.anchoredPosition + " colour " + i.color + " active " + i.gameObject.activeInHierarchy;
        }

        public void OnFired(int who)
        {
            if (who < 0 || who == HaloLocal.NetId) return;
            Track t;
            if (!_tracks.TryGetValue(who, out t)) { t = new Track(); _tracks[who] = t; }
            t.FiredAt = _time;
        }

        void TickTracker(bool show, Camera head, float dt)
        {
            var u = HaloHudKit.Unit;
            var g = HaloHudKit.Globals;
            HaloHudKit.SetActive(_trackRoot.gameObject, show && u != null);
            UpdateTracks(dt);
            if (!show || u == null) return;
            Static(_trackBack, u.MotionBack, HudAnchor.BottomLeft, false);
            Static(_trackFore, u.MotionFore, HudAnchor.BottomLeft, false);
            // ours: the sensor disc reaches its picture's edges; a small margin keeps it whole on the screen (lead review)
            float mg = TrackerMargin;
            _trackBack.rectTransform.anchoredPosition += new Vector2(mg, mg);
            _trackFore.rectTransform.anchoredPosition += new Vector2(mg, mg);

            float os = HaloHudKit.OffsetScale(u.MotionCenter, _mp);
            float cx = u.MotionCenter.X * os + mg, cy = u.MotionCenter.Y * os + mg;
            float radius = Mathf.Min(u.MotionCenter.X, u.MotionCenter.Y) * os;      // ours: the sensor's disc reaches from its centre to the corner's edges
            float range = (g != null && g.MotionRange > 0f ? g.MotionRange : 20f) * HaloSpace.Unit;

            // the sweep: a ring growing from the centre once per period; the blips are taken when it starts
            _sweepT += dt / Mathf.Max(0.2f, SweepSeconds);
            if (_sweepT >= 1f) { _sweepT -= Mathf.Floor(_sweepT); Sample(head, range, radius); }
            var sp = g != null ? HaloHudKit.Sprite(g.SweepBitmap, 0) : null;
            if (sp != null)
            {
                _sweep.texture = sp.Tex; _sweep.uvRect = sp.Uv;
                float d = 2f * radius * _sweepT;
                HaloHudKit.Place(_sweep.rectTransform, HudAnchor.BottomLeft, cx - d * 0.5f, cy - d * 0.5f, d, d);
                Color fc = HaloHudKit.Additive(u.MotionFore.Color.Default, 0.6f * (1f - _sweepT));
                _sweep.color = fc;
                HaloHudKit.SetOn(_sweep, true);
            }
            else HaloHudKit.SetOn(_sweep, false);

            var bs = g != null ? HaloHudKit.Sprite(g.BlipBitmap, 0) : null;
            float size = BlipSize * (_mp ? HaloHudKit.MpScale : 1f);
            float fade = 1f - 0.7f * _sweepT;
            for (int i = 0; i < _blips.Length; i++)
            {
                var b = _blips[i];
                if (i >= _blipCount || bs == null) { HaloHudKit.SetOn(b, false); continue; }
                b.texture = bs.Tex; b.uvRect = bs.Uv;
                HaloHudKit.Place(b.rectTransform, HudAnchor.BottomLeft, cx + _blipAt[i].x * radius - size * 0.5f, cy + _blipAt[i].y * radius - size * 0.5f, size, size);
                b.color = HaloHudKit.Additive(_blipAlly[i] ? AllyBlip : EnemyBlip, fade);
                HaloHudKit.SetOn(b, true);
            }
        }

        /// <summary>Speeds of everyone in the roster (from their positions frame to frame).</summary>
        void UpdateTracks(float dt)
        {
            var list = HaloRoster.Players;
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p.Local) continue;
                Track t;
                if (!_tracks.TryGetValue(p.Id, out t)) { t = new Track(); _tracks[p.Id] = t; }
                Step(t, p.Position, dt);
            }
            // campaign: every biped (AI actors, scripted units)
            var units = HaloBipeds.All;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null) continue;
                Track t;
                if (!_unitTracks.TryGetValue(u, out t)) { t = new Track(); _unitTracks[u] = t; }
                Step(t, u.transform.position, dt);
            }
            if (_unitTracks.Count > units.Count + 16) PruneUnits();
        }

        static void Step(Track t, Vector3 pos, float dt)
        {
            if (t.Has && dt > 1e-4f)
            {
                Vector3 d = pos - t.Last; d.y = 0f;
                float v = d.magnitude / dt;
                if (v > 40f) v = 0f;                                   // a teleport / respawn, not running
                t.Speed = Mathf.Lerp(t.Speed, v, 1f - Mathf.Exp(-6f * dt));
            }
            t.Last = pos; t.Has = true;
        }

        readonly Dictionary<HaloUnit, Track> _unitTracks = new Dictionary<HaloUnit, Track>();
        readonly List<HaloUnit> _dead = new List<HaloUnit>();

        void PruneUnits()
        {
            _dead.Clear();
            foreach (var kv in _unitTracks) if (kv.Key == null || !HaloBipeds.All.Contains(kv.Key)) _dead.Add(kv.Key);
            for (int i = 0; i < _dead.Count; i++) _unitTracks.Remove(_dead[i]);
        }

        bool _firedHooked;
        void HookFired(bool on)
        {
            if (on == _firedHooked) return;
            _firedHooked = on;
            if (on) HaloBipeds.Fired += OnUnitFired; else HaloBipeds.Fired -= OnUnitFired;
        }

        void OnUnitFired(HaloUnit u, HaloTag w, Vector3 p, Vector3 d)
        {
            if (u == null) return;
            Track t;
            if (!_unitTracks.TryGetValue(u, out t)) { t = new Track(); _unitTracks[u] = t; }
            t.FiredAt = _time;
        }

        void OnEnable() { HookFired(true); }
        void OnDisable() { HookFired(false); }

        void Sample(Camera head, float range, float radius)
        {
            _blipCount = 0;
            Vector3 me = HaloLocal.Position;
            Vector3 fwd = HaloLocal.Forward;
            if (head != null && (me == Vector3.zero || _lab)) { me = head.transform.position; fwd = head.transform.forward; }
            else if (head != null) fwd = head.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            bool teams = HaloSession.Rules != null && HaloSession.Rules.Teams;
            var list = HaloRoster.Players;
            for (int i = 0; i < list.Count && _blipCount < _blips.Length; i++)
            {
                var p = list[i];
                if (p.Local || !p.Alive) continue;
                Track t;
                bool moving = _tracks.TryGetValue(p.Id, out t) && (t.Speed > BlipSpeed || _time - t.FiredAt < BlipFireSeconds);
                if (!moving) continue;
                // campaign co-op: other players are allies
                AddBlip(p.Position - me, right, fwd, range, _campaign || teams && p.Team == HaloLocal.Team && p.Team != HaloTeams.None);
            }
            // campaign units: enemies red, the player's friends (marines, crew) yellow - Halo's allegiance table
            var units = HaloBipeds.All;
            for (int i = 0; i < units.Count && _blipCount < _blips.Length; i++)
            {
                var u = units[i];
                if (u == null || !u.Alive || !u.gameObject.activeInHierarchy) continue;
                Track t;
                bool moving = _unitTracks.TryGetValue(u, out t) && (t.Speed > BlipSpeed || _time - t.FiredAt < BlipFireSeconds);
                if (!moving) continue;
                AddBlip(u.transform.position - me, right, fwd, range, HaloAllegiance.AreFriends(HaloTeam.Player, u.Team));
            }
        }

        void AddBlip(Vector3 d, Vector3 right, Vector3 fwd, float range, bool ally)
        {
            d.y = 0f;
            if (d.magnitude > range) return;
            _blipAt[_blipCount] = new Vector2(Vector3.Dot(d, right) / range, Vector3.Dot(d, fwd) / range);
            _blipAlly[_blipCount] = ally;
            _blipCount++;
        }
    }
}

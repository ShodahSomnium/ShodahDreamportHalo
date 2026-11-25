using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The game types' objects from the scenario's netgame flags, played with Halo PC's rules:
    /// <list type="bullet">
    /// <item>Capture the Flag: the two "ctf flag" flags (usage id = team) are the flag stands; the flag is the globals' multiplayer flag weapon.
    ///   An enemy touches your flag = takes it; you touch your own dropped flag = it returns; you bring the enemy flag to your own stand
    ///   (with your flag at home when the variant says so) = a capture (+1). A dropped flag returns by itself after <see cref="ReturnSeconds"/>.</item>
    /// <item>Oddball: the ball (globals' ball weapon) at an "oddball spawn" flag; carrying it scores one point a second.</item>
    /// <item>King of the Hill: "hill flags" with the same usage id are the corners of one hill; one player (or one team) alone in it scores a
    ///   point a second; a moving hill goes to the next hill every <see cref="HillMoveSeconds"/>.</item>
    /// <item>Race: "race track" flags (usage id = order) are the checkpoints; a lap is all of them (in order / any order / rally).</item>
    /// <item>Teleporters (every game type): "teleporter entrance" flags send a player on foot to an "exit" flag with the same usage id.</item>
    /// </list>
    /// State changes are events of the player who caused them (picked up, dropped, returned, captured): the local ones go out through the
    /// net record (HaloLocal.ObjEvent*), remote ones come in through <see cref="RemoteEvent"/>, so every client keeps the same state without a
    /// referee. Carried state is also read from every player's Carry (late joiners). Touch radii and timers marked "ours" are not tag data.
    /// </summary>
    public static class HaloObjectives
    {
        public const int CarryNone = 0, CarryRedFlag = 1, CarryBlueFlag = 2, CarryBall = 3;
        public const int EvPickUp = 1, EvDrop = 2, EvReturn = 3, EvCapture = 4, EvReset = 5;

        public const float TouchRadius = 0.4f;       // ours, world units (~1.2 m): touching a flag / the ball
        public const float CaptureRadius = 0.5f;     // ours: the carrier at the own flag stand
        public const float ReturnSeconds = 30f;      // ours: a dropped flag / ball goes home by itself
        public const float HillMoveSeconds = 60f;    // Halo CE: the moving hill moves every minute
        public const float CheckpointRadius = 1.5f;  // ours: passing a race flag (~4.6 m, vehicles are fast)
        public const float TeleportRadius = 0.35f;   // ours: stepping onto a teleporter
        public const float TeleportCooldown = 0.5f;  // ours: the client applies a teleport a few frames later

        public enum State { Home, Carried, Dropped }

        /// <summary>A flag or the ball.</summary>
        public sealed class Item
        {
            public int Kind;                 // Carry* code
            public int Team = HaloTeams.None;
            public Vector3 Home;
            public float HomeYaw;
            public State State;
            public int Carrier = int.MinValue;
            public Vector3 Position;
            public float DroppedAt, MismatchAt;
            public GameObject View;
            public bool Present;
            public string Label { get { return Kind == CarryBall ? "the ball" : HaloTeams.Name(Team) + " flag"; } }
        }

        public sealed class Hill
        {
            public int Id;
            public Vector3[] Outline;        // Unity space, ordered around the centre
            public Vector3 Center;
            public float MinY, MaxY;
            public GameObject View;
        }

        public static readonly Item RedFlag = new Item { Kind = CarryRedFlag, Team = HaloTeams.Red };
        public static readonly Item BlueFlag = new Item { Kind = CarryBlueFlag, Team = HaloTeams.Blue };
        public static readonly Item Ball = new Item { Kind = CarryBall };
        public static readonly List<Hill> Hills = new List<Hill>();
        public static readonly List<Vector3> Checkpoints = new List<Vector3>();
        public static Hill CurrentHill { get; private set; }
        /// <summary>Who holds the hill: a team (team games) or a player id (FFA); <see cref="HillNobody"/> / <see cref="HillContested"/>.</summary>
        public static int HillOwner { get; private set; } = HillNobody;
        public const int HillNobody = int.MinValue, HillContested = int.MinValue + 1;

        struct Teleporter { public int Id; public Vector3 At; }
        struct Exit { public int Id; public Vector3 At; public float Yaw; }
        static readonly List<Teleporter> Entrances = new List<Teleporter>();
        static readonly List<Exit> Exits = new List<Exit>();
        static readonly List<int> ExitPick = new List<int>();
        static readonly List<int> AnyOrderDone = new List<int>();

        static HaloRules _rules;
        static Transform _root;
        static bool _on;
        static float _scoreSeconds, _teleportAgain, _failSaidAt;
        static bool _teleportArmed = true;
        static int _rallyTarget = -1;
        static bool _saidMinute, _saidThirty;
        static readonly System.Random Rng = new System.Random();
        static Material _hillFallback;

        public static bool Active { get { return _on; } }
        public static Item ItemFor(int carry) { return carry == CarryRedFlag ? RedFlag : carry == CarryBlueFlag ? BlueFlag : carry == CarryBall ? Ball : null; }

        /// <summary>The next race checkpoint of the local player (Unity space), for the HUD's waypoint; false when not racing.</summary>
        public static bool NextCheckpoint(out Vector3 at)
        {
            at = Vector3.zero;
            if (!_on || _rules.Type != HaloScenario.GameType.Race || Checkpoints.Count == 0) return false;
            int i = _rules.RaceType == 2 ? _rallyTarget : HaloLocal.RaceNext;
            if (_rules.RaceType == 1) { for (int k = 0; k < Checkpoints.Count; k++) if (!AnyOrderDone.Contains(k)) { i = k; break; } }
            if (i < 0 || i >= Checkpoints.Count) return false;
            at = Checkpoints[i];
            return true;
        }

        // ------------------------------------------------------------------ setup

        public static void Begin(HaloScenario sc, HaloRules rules, Transform root)
        {
            End();
            _rules = rules; _root = root; _on = true;
            HaloCombat.ObjectiveDropped -= OnCombatDropped;
            HaloCombat.ObjectiveDropped += OnCombatDropped;
            _scoreSeconds = 0f; _teleportAgain = 0f; _teleportArmed = true; _saidMinute = _saidThirty = false;
            HaloLocal.Carry = CarryNone; HaloLocal.RaceLaps = 0; HaloLocal.RaceNext = 0; HaloLocal.InHill = false;
            AnyOrderDone.Clear();
            if (sc == null) return;

            var hillPoints = new Dictionary<int, List<Vector3>>();
            var race = new SortedDictionary<int, Vector3>();
            bool ballSet = false; int ballId = int.MaxValue;
            foreach (HaloScenario.NetgameFlag f in sc.Flags)
            {
                Vector3 p = HaloSpace.MapOrigin + HaloSpace.Pos(f.Position);
                switch (f.Type)
                {
                    case HaloScenario.FlagType.CtfFlag:
                        Item it = f.UsageId == HaloTeams.Blue ? BlueFlag : f.UsageId == HaloTeams.Red ? RedFlag : null;
                        if (it != null) { it.Home = p; it.HomeYaw = HaloSpace.Yaw(f.Facing).eulerAngles.y; it.Present = true; }
                        break;
                    case HaloScenario.FlagType.OddballSpawn:
                        if (f.UsageId < ballId) { ballId = f.UsageId; Ball.Home = p; Ball.HomeYaw = HaloSpace.Yaw(f.Facing).eulerAngles.y; ballSet = true; }
                        break;
                    case HaloScenario.FlagType.HillFlag:
                        List<Vector3> l;
                        if (!hillPoints.TryGetValue(f.UsageId, out l)) { l = new List<Vector3>(); hillPoints[f.UsageId] = l; }
                        l.Add(p);
                        break;
                    case HaloScenario.FlagType.RaceTrack:
                        if (!race.ContainsKey(f.UsageId)) race[f.UsageId] = p;
                        break;
                    case HaloScenario.FlagType.TeleportFrom:
                        Entrances.Add(new Teleporter { Id = f.UsageId, At = p });
                        break;
                    case HaloScenario.FlagType.TeleportTo:
                        Exits.Add(new Exit { Id = f.UsageId, At = p, Yaw = HaloSpace.Yaw(f.Facing).eulerAngles.y });
                        break;
                }
            }
            Ball.Present = ballSet;
            foreach (var kv in race) Checkpoints.Add(kv.Value);
            var ids = new List<int>(hillPoints.Keys);
            ids.Sort();
            foreach (int id in ids) { Hill h = MakeHill(id, hillPoints[id]); if (h != null) Hills.Add(h); }

            switch (rules.Type)
            {
                case HaloScenario.GameType.Ctf:
                    Reset(RedFlag); Reset(BlueFlag);
                    if (!RedFlag.Present || !BlueFlag.Present) HaloLog.Warn("Match: CTF on a map without both flag stands (red " + RedFlag.Present + ", blue " + BlueFlag.Present + ")");
                    if (RedFlag.Present) RedFlag.View = BuildItem(HaloAnnouncer.FlagWeapon, "Red Flag");
                    if (BlueFlag.Present) BlueFlag.View = BuildItem(HaloAnnouncer.FlagWeapon, "Blue Flag");
                    break;
                case HaloScenario.GameType.Oddball:
                    Reset(Ball);
                    if (!Ball.Present) HaloLog.Warn("Match: Oddball on a map without an oddball spawn");
                    else { Ball.View = BuildItem(HaloAnnouncer.BallWeapon, "Oddball"); HaloAnnouncer.Say("play_ball"); }
                    break;
                case HaloScenario.GameType.King:
                    if (Hills.Count == 0) HaloLog.Warn("Match: King of the Hill on a map without hill flags");
                    SetHill(HillFor(0f), false);
                    break;
                case HaloScenario.GameType.Race:
                    if (Checkpoints.Count == 0) HaloLog.Warn("Match: Race on a map without race track flags");
                    _rallyTarget = Checkpoints.Count > 0 ? Rng.Next(Checkpoints.Count) : -1;
                    break;
            }

            HaloLog.Info("Match: objectives: " + (RedFlag.Present ? "red flag " : "") + (BlueFlag.Present ? "blue flag " : "") + (Ball.Present ? "ball " : "")
                         + Hills.Count + " hills, " + Checkpoints.Count + " race checkpoints, " + Entrances.Count + " teleporter entrances / " + Exits.Count + " exits");
        }

        public static void End()
        {
            if (_on && HaloLocal.Carry != CarryNone) OutOfHand();
            _on = false;
            HaloCombat.ObjectiveDropped -= OnCombatDropped;
            foreach (Item it in new[] { RedFlag, BlueFlag, Ball })
            {
                if (it.View != null) UnityEngine.Object.Destroy(it.View);
                it.View = null; it.Present = false; it.State = State.Home; it.Carrier = int.MinValue;
            }
            foreach (Hill h in Hills) if (h.View != null) UnityEngine.Object.Destroy(h.View);
            Hills.Clear(); Checkpoints.Clear(); Entrances.Clear(); Exits.Clear(); AnyOrderDone.Clear();
            CurrentHill = null; HillOwner = HillNobody;
            HaloLocal.Carry = CarryNone; HaloLocal.InHill = false;
            if (_hillFallback != null) { UnityEngine.Object.Destroy(_hillFallback); _hillFallback = null; }
        }

        static void Reset(Item it)
        {
            it.State = State.Home; it.Carrier = int.MinValue; it.Position = it.Home; it.DroppedAt = 0f; it.MismatchAt = 0f;
        }

        static Hill MakeHill(int id, List<Vector3> pts)
        {
            if (pts.Count < 3) return null;
            Vector3 c = Vector3.zero;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 p in pts) { c += p; minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            c /= pts.Count;
            var sorted = new List<Vector3>(pts);
            sorted.Sort((a, b) => Mathf.Atan2(a.z - c.z, a.x - c.x).CompareTo(Mathf.Atan2(b.z - c.z, b.x - c.x)));
            return new Hill { Id = id, Outline = sorted.ToArray(), Center = c, MinY = minY - 1.0f, MaxY = maxY + 2.5f };      // ours: the hill's height band
        }

        /// <summary>The hill that is live <paramref name="elapsed"/> seconds into the game (the first hill, or moving through them in order).</summary>
        static Hill HillFor(float elapsed)
        {
            if (Hills.Count == 0) return null;
            if (!_rules.KingMovingHill) return Hills[0];
            int i = (int)Mathf.Floor(Mathf.Max(0f, elapsed) / HillMoveSeconds) % Hills.Count;
            return Hills[i];
        }

        static void SetHill(Hill h, bool announce)
        {
            if (h == CurrentHill) return;
            if (CurrentHill != null && CurrentHill.View != null) CurrentHill.View.SetActive(false);
            CurrentHill = h;
            HillOwner = HillNobody;
            if (h == null) return;
            if (h.View == null) h.View = BuildHillView(h);
            else h.View.SetActive(true);
            if (announce) { HaloAnnouncer.Say("hill_move"); HaloLog.Info("Match: the hill moved to hill " + h.Id); }
        }

        // ------------------------------------------------------------------ views

        static GameObject BuildItem(HaloTag weapon, string name)
        {
            var go = new GameObject("Halo " + name);
            go.transform.SetParent(_root, false);
            if (weapon != null)
            {
                HaloTag model = null;
                try { model = weapon.Root.Ref(40); } catch (Exception) { model = null; }      // object: model dependency at +40
                HaloModelBuilder.Built b = model != null ? HaloModelBuilder.Build(model, go.transform) : null;
                if (b == null) HaloLog.Warn("Match: " + name + ": no model from " + weapon);
            }
            return go;
        }

        static GameObject BuildHillView(Hill h)
        {
            var go = new GameObject("Halo Hill " + h.Id);
            go.transform.SetParent(_root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = h.Outline.Length;
            for (int i = 0; i < h.Outline.Length; i++) lr.SetPosition(i, h.Outline[i] + Vector3.up * 0.06f);
            lr.widthMultiplier = 0.18f;      // ours
            lr.textureMode = LineTextureMode.Tile;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            Material m = HaloAnnouncer.HillShader != null ? HaloMaterials.Get(HaloAnnouncer.HillShader) : null;
            if (m == null || m == HaloMaterials.Fallback)
            {
                if (_hillFallback == null) { _hillFallback = HaloMaterials.NewUnlit(HaloMaterials.Kind.Additive); HaloMaterials.SetColor(_hillFallback, new Color(1f, 0.85f, 0.3f, 0.9f)); }
                m = _hillFallback;
            }
            lr.sharedMaterial = m;
            return go;
        }

        static void PlaceViews(Vector3 localFeet)
        {
            PlaceItem(RedFlag, localFeet); PlaceItem(BlueFlag, localFeet); PlaceItem(Ball, localFeet);
        }

        static void PlaceItem(Item it, Vector3 localFeet)
        {
            if (it.View == null) return;
            Transform t = it.View.transform;
            bool show = !(it.State == State.Carried && it.Carrier == HaloLocal.NetId && HaloCombat.CarryingObjective);      // Combat draws it in the hand
            if (it.View.activeSelf != show) it.View.SetActive(show);
            if (!show) return;
            if (it.State != State.Carried)
            {
                if (t.parent != _root) t.SetParent(_root, true);
                t.SetPositionAndRotation(it.Position, Quaternion.Euler(0f, it.HomeYaw, 0f));
                return;
            }
            if (it.Carrier == HaloLocal.NetId)
            {
                Transform hand = HaloPlayer.RightHand;
                if (hand != null)
                {
                    if (t.parent != hand) { t.SetParent(hand, false); t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; }
                    return;
                }
                if (t.parent != _root) t.SetParent(_root, true);
                Vector3 f = HaloLocal.Forward; f.y = 0f; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                t.SetPositionAndRotation(localFeet + Vector3.up * 1.1f + f.normalized * 0.35f, Quaternion.LookRotation(f.normalized, Vector3.up));
                return;
            }
            HaloMatchPlayer c = HaloRoster.Find(it.Carrier);
            if (t.parent != _root) t.SetParent(_root, true);
            if (c != null) t.SetPositionAndRotation(c.Position + Vector3.up * 1.1f, Quaternion.LookRotation(Flat(c.Forward), Vector3.up));
        }

        static Vector3 Flat(Vector3 f) { f.y = 0f; return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward; }

        // ------------------------------------------------------------------ the tick

        /// <summary>Every frame while playing: the local player's touches, scores and teleports; drops by timer; the views.</summary>
        public static void Tick(float dt, float elapsed, Vector3 feet, bool alive, bool frozen)
        {
            if (!_on) return;
            SyncCarriers();
            float now = Time.time;
            for (int i = 0; i < 3; i++)
            {
                Item it = i == 0 ? RedFlag : i == 1 ? BlueFlag : Ball;
                if (!it.Present || it.State != State.Dropped || now - it.DroppedAt < ReturnSeconds) continue;
                Reset(it);
                if (it.Kind == CarryBall) HaloLog.Info("Match: the ball reset");
                else { HaloAnnouncer.Say(HaloAnnouncer.TeamWord(it.Team) + "_team_flag_returned"); HaloLog.Info("Match: the " + it.Label + " returned by itself"); }
            }

            if (!frozen)
            {
                if (HaloLocal.Carry != CarryNone && !HaloLocal.Alive) DropLocal("died");
                switch (_rules.Type)
                {
                    case HaloScenario.GameType.Ctf: TickCtf(feet, alive); break;
                    case HaloScenario.GameType.Oddball: TickBall(dt, feet, alive); break;
                    case HaloScenario.GameType.King: TickHill(dt, elapsed, feet, alive); break;
                    case HaloScenario.GameType.Race: TickRace(feet, alive); break;
                }
                if (alive) TickTeleport(feet);
            }

            PlaceViews(feet);
        }

        /// <summary>Remote players' Carry fields (from their records): who holds what (covers events missed before joining).</summary>
        static void SyncCarriers()
        {
            for (int i = 0; i < HaloRoster.Players.Count; i++)
            {
                HaloMatchPlayer p = HaloRoster.Players[i];
                if (p.Local || p.Carry == CarryNone) continue;
                Item it = ItemFor(p.Carry);
                if (it != null && it.Present && (it.State != State.Carried || it.Carrier != p.Id) && p.Alive) { it.State = State.Carried; it.Carrier = p.Id; it.MismatchAt = 0f; }
            }
            // a remote carrier who left or no longer carries (the drop event lost): the item goes home
            for (int i = 0; i < 3; i++)
            {
                Item it = i == 0 ? RedFlag : i == 1 ? BlueFlag : Ball;
                if (it.State != State.Carried || it.Carrier == HaloLocal.NetId) continue;
                HaloMatchPlayer c = HaloRoster.Find(it.Carrier);
                if (c != null && c.Carry == it.Kind) { it.MismatchAt = 0f; continue; }
                if (it.MismatchAt <= 0f) { it.MismatchAt = Time.time; continue; }
                if (c != null && Time.time - it.MismatchAt < 1.5f) continue;
                HaloLog.Info("Match: " + it.Label + " goes home (its carrier " + (c != null ? c.Name + " no longer has it" : it.Carrier + " left") + ")");
                Reset(it);
            }
        }

        static bool Touch(Vector3 feet, Vector3 at, float radius)
        {
            Vector3 d = feet - at;
            float dy = d.y; d.y = 0f;
            float r = radius * HaloSpace.Unit;
            return d.sqrMagnitude < r * r && dy > -1.5f && dy < 2.2f;
        }

        static void TickCtf(Vector3 feet, bool alive)
        {
            if (!alive) return;
            int me = HaloLocal.NetId, team = HaloLocal.Team;
            for (int i = 0; i < 2; i++)
            {
                Item it = i == 0 ? RedFlag : BlueFlag;
                if (!it.Present || it.State == State.Carried) continue;
                if (!Touch(feet, it.Position, TouchRadius)) continue;
                if (it.Team == team)
                {
                    if (it.State == State.Dropped)
                    {
                        Reset(it);
                        Event(EvReturn, it.Kind, feet);
                        HaloAnnouncer.Say(HaloAnnouncer.TeamWord(it.Team) + "_team_flag_returned");
                        HaloLog.Info("Match: you returned the " + it.Label);
                    }
                }
                else if (HaloLocal.Carry == CarryNone && HaloLocal.Vehicle == null)
                {
                    it.State = State.Carried; it.Carrier = me;
                    HaloLocal.Carry = it.Kind;
                    TakeIntoHand(it);
                    Event(EvPickUp, it.Kind, feet);
                    HaloAnnouncer.Say(HaloAnnouncer.TeamWord(team) + "_team_has_the_flag");
                    HaloEvents.RaiseMessage("You have the " + it.Label + "!", 3f);
                    HaloLog.Info("Match: you took the " + it.Label);
                }
            }

            // a capture: the enemy flag at the own stand
            Item carried = ItemFor(HaloLocal.Carry);
            Item own = team == HaloTeams.Blue ? BlueFlag : team == HaloTeams.Red ? RedFlag : null;
            if (carried != null && carried != Ball && own != null && own.Present && Touch(feet, own.Home, CaptureRadius))
            {
                if (_rules.FlagAtHomeToScore && own.State != State.Home)
                {
                    if (Time.time > _failSaidAt) { HaloEvents.RaiseMessage("Your flag must be at home to score", 2f); HaloAnnouncer.Say("flag_failure"); _failSaidAt = Time.time + 3f; }
                    return;
                }
                HaloLocal.Score++;
                HaloLocal.Carry = CarryNone;
                OutOfHand();
                Reset(carried);
                Event(EvCapture, carried.Kind, feet);
                HaloAnnouncer.Say(HaloAnnouncer.TeamWord(team) + "_team_score");
                HaloLog.Info("Match: you captured the " + carried.Label + " (" + HaloLocal.Score + " captures)");
            }
        }

        static void TickBall(float dt, Vector3 feet, bool alive)
        {
            if (!Ball.Present) return;
            if (alive && HaloLocal.Carry == CarryNone && Ball.State != State.Carried && HaloLocal.Vehicle == null && Touch(feet, Ball.Position, TouchRadius))
            {
                Ball.State = State.Carried; Ball.Carrier = HaloLocal.NetId;
                HaloLocal.Carry = CarryBall;
                TakeIntoHand(Ball);
                Event(EvPickUp, CarryBall, feet);
                HaloEvents.RaiseMessage("You have the ball!", 3f);
                HaloLog.Info("Match: you have the ball");
            }
            if (alive && HaloLocal.Carry == CarryBall) Accumulate(dt);
        }

        static void TickHill(float dt, float elapsed, Vector3 feet, bool alive)
        {
            SetHill(HillFor(elapsed), true);
            Hill h = CurrentHill;
            HaloLocal.InHill = alive && h != null && InHill(h, feet);
            if (h == null) { HillOwner = HillNobody; return; }
            bool teams = _rules.Teams;
            int owner = HillNobody, creditId = int.MaxValue, count = 0;
            for (int i = 0; i < HaloRoster.Players.Count; i++)
            {
                HaloMatchPlayer p = HaloRoster.Players[i];
                bool inside = p.Local ? HaloLocal.InHill : p.Alive && InHill(h, p.Position);
                if (!inside) continue;
                count++;
                int key = teams ? p.Team : p.Id;
                if (owner == HillNobody) owner = key;
                else if (owner != key) owner = HillContested;
                if (p.Id < creditId) creditId = p.Id;
            }
            int was = HillOwner;
            HillOwner = owner;
            if (HaloLocal.InHill && owner != was)
            {
                if (owner == HillContested) HaloAnnouncer.Say("hill_contested");
                else if (owner != HillNobody && was == HillNobody) HaloAnnouncer.Say(count > 1 ? "hill_occupied" : "hill_controlled");
            }
            // one player (FFA) or one team alone in the hill: a point a second, credited once per team (the lowest id inside)
            if (HaloLocal.InHill && owner != HillContested && owner != HillNobody && creditId == HaloLocal.NetId) Accumulate(dt);
        }

        public static bool InHill(Hill h, Vector3 p)
        {
            if (p.y < h.MinY || p.y > h.MaxY) return false;
            bool inside = false;
            Vector3[] o = h.Outline;
            for (int i = 0, j = o.Length - 1; i < o.Length; j = i++)
            {
                if ((o[i].z > p.z) != (o[j].z > p.z) && p.x < (o[j].x - o[i].x) * (p.z - o[i].z) / (o[j].z - o[i].z) + o[i].x) inside = !inside;
            }
            return inside;
        }

        static void Accumulate(float dt)
        {
            _scoreSeconds += dt;
            while (_scoreSeconds >= 1f) { _scoreSeconds -= 1f; HaloLocal.Score++; }
            int limit = _rules.ScoreLimit;
            if (limit <= 0) return;
            int mine = _rules.Teams ? HaloMatchControl.TeamScore(HaloLocal.Team) : HaloLocal.Score;
            string who = _rules.Teams ? HaloAnnouncer.TeamWord(HaloLocal.Team) + "_team_" : "";
            if (!_saidMinute && limit > 90 && mine >= limit - 60) { _saidMinute = true; HaloAnnouncer.Say(_rules.Teams ? who + "minute_to_win" : "one_minute_to_win"); }
            if (!_saidThirty && limit > 45 && mine >= limit - 30) { _saidThirty = true; HaloAnnouncer.Say(_rules.Teams ? who + "30_to_win" : "30_seconds_to_win"); }
        }

        static void TickRace(Vector3 feet, bool alive)
        {
            if (!alive || Checkpoints.Count == 0) return;
            int n = Checkpoints.Count;
            if (_rules.RaceType == 1)
            {
                for (int k = 0; k < n; k++)
                {
                    if (AnyOrderDone.Contains(k) || !Touch(feet, Checkpoints[k], CheckpointRadius)) continue;
                    AnyOrderDone.Add(k);
                    HaloAnnouncer.Say("ting");
                    if (AnyOrderDone.Count >= n) { AnyOrderDone.Clear(); Lap(); }
                }
                return;
            }
            int target = _rules.RaceType == 2 ? _rallyTarget : HaloLocal.RaceNext;
            if (target < 0 || target >= n || !Touch(feet, Checkpoints[target], CheckpointRadius)) return;
            HaloAnnouncer.Say("ting");
            if (_rules.RaceType == 2)
            {
                int next = n > 1 ? Rng.Next(n - 1) : 0;
                if (n > 1 && next >= target) next++;
                _rallyTarget = next;
                HaloLocal.RaceNext++;
                if (HaloLocal.RaceNext >= n) { HaloLocal.RaceNext = 0; Lap(); }
                return;
            }
            HaloLocal.RaceNext++;
            if (HaloLocal.RaceNext >= n) { HaloLocal.RaceNext = 0; Lap(); }
        }

        static void Lap()
        {
            HaloLocal.RaceLaps++;
            HaloLocal.Score = HaloLocal.RaceLaps;
            HaloEvents.RaiseMessage("Lap " + HaloLocal.RaceLaps, 2f);
            HaloLog.Info("Match: lap " + HaloLocal.RaceLaps);
        }

        /// <summary>Teleporters: stepping onto an entrance sends the player to an exit with the same usage id. Two-way teleporters have an
        /// entrance on each exit: after a jump the teleporters re-arm only once the player stood on no entrance (and the client's
        /// asynchronous teleport had time to land).</summary>
        static void TickTeleport(Vector3 feet)
        {
            if (Entrances.Count == 0 || Exits.Count == 0 || HaloLocal.Vehicle != null || Time.time < _teleportAgain) return;
            int on = -1;
            for (int i = 0; i < Entrances.Count; i++) if (Touch(feet, Entrances[i].At, TeleportRadius)) { on = i; break; }
            if (on < 0) { _teleportArmed = true; return; }
            if (!_teleportArmed) return;
            ExitPick.Clear();
            for (int k = 0; k < Exits.Count; k++) if (Exits[k].Id == Entrances[on].Id) ExitPick.Add(k);
            if (ExitPick.Count == 0) return;
            Exit e = Exits[ExitPick[Rng.Next(ExitPick.Count)]];
            Vector3 to = HaloSpawns.Ground(e.At);
            string how;
            HaloCombat.NoteTeleport();
            if (HaloPlayer.Teleport(to, e.Yaw, out how))
            {
                _teleportArmed = false;
                _teleportAgain = Time.time + TeleportCooldown;
                HaloAnnouncer.At("teleporter_activate", Entrances[on].At);
                HaloAnnouncer.At("teleporter_activate", to);
                HaloLog.Info("Match: teleporter " + Entrances[on].Id + " -> " + to + " (" + how + ")");
            }
        }

        // ------------------------------------------------------------------ events

        /// <summary>The local player lets go of what they carry (death, the match ending, Combat's drop).</summary>
        public static void DropLocal(string why)
        {
            Vector3 feet;
            if (!HaloPlayer.TryFeet(out feet)) feet = HaloLocal.Position;
            DropLocalAt(why, feet, true);
        }

        /// <summary>Combat: the player let go of the flag / ball (or died holding it) at a point.</summary>
        static void OnCombatDropped(HaloTag weapon, Vector3 at, Vector3 vel)
        {
            if (_on && HaloLocal.Carry != CarryNone) DropLocalAt("let go", at, false);
        }

        static void DropLocalAt(string why, Vector3 feet, bool takeFromHand)
        {
            Item it = ItemFor(HaloLocal.Carry);
            HaloLocal.Carry = CarryNone;
            if (it == null) return;
            if (takeFromHand) OutOfHand();
            it.State = State.Dropped; it.Carrier = int.MinValue; it.Position = HaloSpawns.Ground(feet); it.DroppedAt = Time.time;
            Event(EvDrop, it.Kind, it.Position);
            HaloLog.Info("Match: you dropped " + it.Label + " (" + why + ")");
        }

        /// <summary>The flag / ball becomes the held weapon (Combat: no firing, melee allowed); Match's own model hides meanwhile.</summary>
        static void TakeIntoHand(Item it)
        {
            HaloTag w = it.Kind == CarryBall ? HaloAnnouncer.BallWeapon : HaloAnnouncer.FlagWeapon;
            if (w != null) HaloGame.Guard("carry objective", () => HaloCombat.CarryObjective(w));
        }

        static void OutOfHand()
        {
            HaloGame.Guard("drop objective", () => HaloCombat.DropObjective());
        }

        static void Event(int ev, int kind, Vector3 at)
        {
            HaloLocal.ObjEventCode = ev * 16 + kind;
            HaloLocal.ObjEventPos = at;
            HaloLocal.ObjEvent++;
        }

        /// <summary>The instance master's state of an item (late join): 0 home, 1 carried by <paramref name="carrier"/>, 2 dropped at
        /// <paramref name="pos"/> <paramref name="secondsAgo"/> seconds ago.</summary>
        public static void ApplySnapshot(int kind, int state, int carrier, Vector3 pos, float secondsAgo)
        {
            Item it = ItemFor(kind);
            if (!_on || it == null || !it.Present) return;
            if (state == 1 && carrier >= 0) { it.State = State.Carried; it.Carrier = carrier; }
            else if (state == 2) { it.State = State.Dropped; it.Carrier = int.MinValue; it.Position = pos; it.DroppedAt = Time.time - Mathf.Max(0f, secondsAgo); }
            else Reset(it);
            HaloLog.Info("Match: " + it.Label + " from the master: " + it.State);
        }

        /// <summary>A remote player's objective event (from the net layer).</summary>
        public static void RemoteEvent(int id, int code, Vector3 pos)
        {
            if (!_on) return;
            int ev = code / 16;
            Item it = ItemFor(code % 16);
            if (it == null || !it.Present) return;
            HaloMatchPlayer who = HaloRoster.Find(id);
            string name = who != null ? who.Name : "Player " + id;
            switch (ev)
            {
                case EvPickUp:
                    if (it.State == State.Carried && it.Carrier == HaloLocal.NetId)
                    {
                        // both took it in the same moment: the lower id keeps it
                        if (id > HaloLocal.NetId) return;
                        HaloLocal.Carry = CarryNone;
                        OutOfHand();
                    }
                    it.State = State.Carried; it.Carrier = id;
                    if (it.Kind != CarryBall) HaloAnnouncer.Say(HaloAnnouncer.TeamWord(who != null ? who.Team : (it.Team == HaloTeams.Red ? HaloTeams.Blue : HaloTeams.Red)) + "_team_has_the_flag");
                    HaloEvents.RaiseMessage(name + " has " + it.Label, 3f);
                    break;
                case EvDrop:
                    it.State = State.Dropped; it.Carrier = int.MinValue; it.Position = pos; it.DroppedAt = Time.time;
                    break;
                case EvReturn:
                    Reset(it);
                    HaloAnnouncer.Say(HaloAnnouncer.TeamWord(it.Team) + "_team_flag_returned");
                    break;
                case EvCapture:
                    Reset(it);
                    if (who != null) HaloAnnouncer.Say(HaloAnnouncer.TeamWord(who.Team) + "_team_score");
                    HaloEvents.RaiseMessage(name + " captured the " + it.Label, 3f);
                    break;
                case EvReset:
                    Reset(it);
                    break;
            }
        }
    }
}

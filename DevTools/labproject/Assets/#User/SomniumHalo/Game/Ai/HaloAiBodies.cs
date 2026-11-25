using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] Everything an actor can perceive, target or follow: the local player (the avatar's head / body: HaloPlayer + HaloLocal),
    /// the remote players of a co-op mission (HaloNetGame.Players) and every live HaloUnit (AI actors, scripted bipeds such as Keyes). Rebuilt
    /// once per AI tick into pooled entries (no allocations in steady state); an entry keeps its identity while its thing lives so actors can
    /// hold it as their target. Sight lines are raycasts against the world (<see cref="Blockers"/>) under a per-tick budget.
    /// </summary>
    public sealed class HaloAiBody
    {
        public enum Kinds { Unit, LocalPlayer, RemotePlayer, StandIn }
        public Kinds Kind;
        public HaloUnit Unit;
        public HaloActor Actor;          // when the unit is one of ours
        public HaloNetPlayer Remote;
        public Transform StandIn;
        public HaloTeam Team;
        public Vector3 Feet, Center, Head, Velocity;
        public bool Alive, Seen;
        public bool Disregarded, Preferred;
        /// <summary>The body sits in a vehicle (players: HaloLocal.Vehicle; units: a seat).</summary>
        public bool InVehicle;
        /// <summary>The vehicle the body sits in (null on foot / unknown) and its unit "ai vehicle radius" in metres (0 = none).</summary>
        public HaloVehicle Vehicle;
        public float VehicleRadius;
        /// <summary>How visible the body is (1 normal, active camouflage 0.2 (ours) up to 1 while it fires / is hit: scales the perception
        /// accumulation).</summary>
        public float Visibility = 1f;
        /// <summary>HaloAi.Time of the body's last shot heard by the AI (players' and AI shots).</summary>
        public float FiredAt = -100f;
        internal int Key;
        internal bool Touched;
        internal Vector3 LastFeet; internal bool HasLast;

        public bool IsPlayer => Kind != Kinds.Unit;
        public override string ToString()
        {
            switch (Kind)
            {
                case Kinds.LocalPlayer: return "player";
                case Kinds.RemotePlayer: return "player " + (Remote != null ? Remote.Name : "?");
                case Kinds.StandIn: return "stand-in player";
                default: return Unit != null ? (string.IsNullOrEmpty(Unit.ObjectName) ? Unit.name : Unit.ObjectName) : "unit";
            }
        }
    }

    public static class HaloAiBodies
    {
        public static readonly List<HaloAiBody> All = new List<HaloAiBody>(64);
        private static readonly Dictionary<int, HaloAiBody> ByKey = new Dictionary<int, HaloAiBody>();
        private static readonly Stack<HaloAiBody> Free = new Stack<HaloAiBody>();
        /// <summary>Units that are not our actors (scripted bipeds): refreshed from the scene every couple of seconds.</summary>
        private static readonly List<HaloUnit> Others = new List<HaloUnit>(32);
        private static float _nextScan;
        /// <summary>A stand-in player for labs (a transform with a head 1.6 m above it).</summary>
        public static Transform StandInPlayer;
        public static HaloTeam StandInTeam = HaloTeam.Player;
        /// <summary>Labs: the stand-in player wears active camouflage.</summary>
        public static bool StandInCamo;
        /// <summary>What blocks sight: map geometry and props (not hit boxes, not avatars).</summary>
        public const int Blockers = (1 << HaloLayers.World) | (1 << HaloLayers.Environment);
        /// <summary>Sight-line raycasts allowed per AI tick (all actors together). Ours.</summary>
        public static int RayBudget = 48;
        public static int RaysThisTick { get; private set; }
        public static int RaysTotal { get; private set; }
        static readonly HashSet<int> DisregardKeys = new HashSet<int>(), PreferKeys = new HashSet<int>();

        public static HaloAiBody LocalPlayer { get; private set; }

        public static void Clear()
        {
            foreach (var b in All) Release(b);
            All.Clear(); ByKey.Clear(); Others.Clear(); DisregardKeys.Clear(); PreferKeys.Clear(); VehicleRadii.Clear();      // (keyed by tags: they hold the map)
            LocalPlayer = null; _nextScan = 0f;
        }

        static void Release(HaloAiBody b) { b.Unit = null; b.Actor = null; b.Remote = null; b.StandIn = null; b.HasLast = false; b.Vehicle = null; b.VehicleRadius = 0f; b.Visibility = 1f; b.FiredAt = -100f; Free.Push(b); }

        /// <summary>Camouflaged bodies: how much of the perception they still give (ours; Halo's own factor is not in the tags).</summary>
        public const float CamoVisibility = 0.2f;

        static readonly Dictionary<HaloTag, float> VehicleRadii = new Dictionary<HaloTag, float>();
        /// <summary>A vehicle's unit "ai vehicle radius" (vehi @640, the unit block) in metres; cached per tag.</summary>
        public static float AiVehicleRadius(HaloVehicle v)
        {
            if (v == null || v.Def == null || v.Def.Tag == null) return 0f;
            float r;
            if (!VehicleRadii.TryGetValue(v.Def.Tag, out r))
            {
                try { r = v.Def.Tag.Root.F32(640) * HaloSpace.Unit; } catch (System.Exception) { r = 0f; }
                if (r < 0f || r > 60f) r = 0f;
                VehicleRadii[v.Def.Tag] = r;
                HaloLog.Info("AI: " + HaloAi.Short(v.Def.Tag) + " ai vehicle radius " + (r / HaloSpace.Unit).ToString("0.##") + " wu");
            }
            return r;
        }

        static void SetVehicle(HaloAiBody b, HaloVehicle v)
        {
            b.Vehicle = v;
            b.VehicleRadius = v != null ? AiVehicleRadius(v) : 0f;
        }

        static HaloAiBody Get(int key, HaloAiBody.Kinds kind)
        {
            HaloAiBody b;
            if (!ByKey.TryGetValue(key, out b))
            {
                b = Free.Count > 0 ? Free.Pop() : new HaloAiBody();
                b.Key = key; b.Kind = kind; b.HasLast = false; b.FiredAt = -100f; b.Visibility = 1f;
                ByKey[key] = b; All.Add(b);
            }
            b.Kind = kind;
            b.Touched = true;
            return b;
        }

        /// <summary>Notes a unit the AI should know about (scripted bipeds); our actors' units are known anyway.</summary>
        public static void Note(HaloUnit u) { if (u != null && !Others.Contains(u)) Others.Add(u); }

        public static void SetDisregard(HaloUnit u, bool on) { if (u == null) return; int k = u.GetInstanceID(); if (on) DisregardKeys.Add(k); else DisregardKeys.Remove(k); }
        public static void SetPrefer(HaloUnit u, bool on) { if (u == null) return; int k = u.GetInstanceID(); if (on) PreferKeys.Add(k); else PreferKeys.Remove(k); }
        public static void SetDisregardPlayers(bool on) { if (on) DisregardKeys.Add(-1); else DisregardKeys.Remove(-1); }
        public static void SetPreferPlayers(bool on) { if (on) PreferKeys.Add(-1); else PreferKeys.Remove(-1); }

        /// <summary>Rebuilds the list (once per AI tick).</summary>
        public static void Refresh(List<HaloActor> actors, float dt)
        {
            RaysThisTick = 0;
            for (int i = 0; i < All.Count; i++) All[i].Touched = false;

            // the local player
            LocalPlayer = null;
            if (StandInPlayer != null)
            {
                var b = Get(-3, HaloAiBody.Kinds.StandIn);
                b.StandIn = StandInPlayer;
                b.Team = StandInTeam;
                b.Feet = StandInPlayer.position; b.Center = b.Feet + Vector3.up * 1.0f; b.Head = b.Feet + Vector3.up * 1.6f;
                b.Alive = StandInPlayer.gameObject.activeInHierarchy;
                b.InVehicle = false; SetVehicle(b, null);
                b.Visibility = StandInCamo ? CamoVisibility : 1f;
                LocalPlayer = b;
            }
            else if (HaloSession.InMatch || HaloCampaign.Active)
            {
                Vector3 feet = HaloLocal.Position;
                Camera head = HaloPlayer.Head();
                if (feet != Vector3.zero || head != null)
                {
                    var b = Get(-1, HaloAiBody.Kinds.LocalPlayer);
                    b.Team = HaloTeam.Player;
                    Vector3 hp = head != null ? head.transform.position : feet + Vector3.up * 1.6f;
                    if (feet == Vector3.zero) feet = hp - Vector3.up * 1.6f;
                    b.Feet = feet; b.Head = hp; b.Center = Vector3.Lerp(feet, hp, 0.55f);
                    b.Alive = HaloLocal.Alive;
                    b.InVehicle = HaloLocal.Vehicle != null;
                    SetVehicle(b, b.InVehicle ? HaloLocal.Vehicle.GetComponentInParent<HaloVehicle>() : null);
                    // active camouflage: CamoVisibility shows how much firing / hits reveal the player
                    b.Visibility = HaloLocal.Camo ? Mathf.Clamp(HaloLocal.CamoVisibility, CamoVisibility, 1f) : 1f;
                    LocalPlayer = b;
                }
            }

            // remote players (co-op)
            if (HaloNetGame.Up)
            {
                var players = HaloNetGame.Players;
                for (int i = 0; i < players.Count; i++)
                {
                    var p = players[i];
                    if (p == null || !p.InMatch) continue;
                    var b = Get(-1000 - p.Slot, HaloAiBody.Kinds.RemotePlayer);
                    b.Remote = p;
                    b.Team = HaloTeam.Player;
                    b.Feet = p.Position; b.Head = p.Head != Vector3.zero ? p.Head : p.Position + Vector3.up * 1.6f; b.Center = Vector3.Lerp(b.Feet, b.Head, 0.55f);
                    b.Alive = p.Alive;
                    b.InVehicle = p.VehicleIndex >= 0;
                    SetVehicle(b, b.InVehicle ? HaloVehicles.Find(p.VehicleIndex) : null);
                    b.Visibility = p.Camo ? CamoVisibility : 1f;
                }
            }

            // our actors' units
            for (int i = 0; i < actors.Count; i++)
            {
                var a = actors[i];
                if (a.Unit == null) continue;
                var b = Get(a.Unit.GetInstanceID(), HaloAiBody.Kinds.Unit);
                FillUnit(b, a.Unit);
                b.Actor = a;
                a.Body = b;
            }

            // other units (scripted bipeds): scan the scene now and then
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + 0.5f;
                Others.Clear();
                var found = HaloBipeds.All;
                for (int i = 0; i < found.Count; i++) if (found[i] != null) Others.Add(found[i]);
            }
            for (int i = Others.Count - 1; i >= 0; i--)
            {
                var u = Others[i];
                if (u == null) { Others.RemoveAt(i); continue; }
                HaloAiBody b;
                int key = u.GetInstanceID();
                if (ByKey.TryGetValue(key, out b) && b.Touched) continue;     // an actor's
                b = Get(key, HaloAiBody.Kinds.Unit);
                FillUnit(b, u);
                b.Actor = null;
            }

            // drop what went away; velocity estimate; script preferences
            for (int i = All.Count - 1; i >= 0; i--)
            {
                var b = All[i];
                if (!b.Touched) { ByKey.Remove(b.Key); All.RemoveAt(i); Release(b); continue; }
                if (b.HasLast && dt > 0f) b.Velocity = Vector3.Lerp(b.Velocity, (b.Feet - b.LastFeet) / dt, 0.5f);
                else b.Velocity = Vector3.zero;
                if (b.Velocity.sqrMagnitude > 900f) b.Velocity = Vector3.zero;   // a teleport
                b.LastFeet = b.Feet; b.HasLast = true;
                int k = b.Kind == HaloAiBody.Kinds.Unit ? b.Key : -1;
                b.Disregarded = DisregardKeys.Count > 0 && DisregardKeys.Contains(k);
                b.Preferred = PreferKeys.Count > 0 && PreferKeys.Contains(k);
            }
        }

        static void FillUnit(HaloAiBody b, HaloUnit u)
        {
            b.Unit = u;
            b.Team = u.Team;
            b.Feet = u.transform.position;
            b.Head = u.Eye;
            b.Center = u.Center;
            b.Alive = u.Alive;
            b.InVehicle = u.Seat != null;
            if (!b.InVehicle) { if (b.Vehicle != null) SetVehicle(b, null); }
            else if (b.Vehicle == null || !u.Seat.IsChildOf(b.Vehicle.transform)) SetVehicle(b, u.Seat.GetComponentInParent<HaloVehicle>());
            b.Visibility = u.Camouflaged ? CamoVisibility : 1f;
        }

        public static HaloAiBody Of(HaloUnit u)
        {
            if (u == null) return null;
            HaloAiBody b;
            return ByKey.TryGetValue(u.GetInstanceID(), out b) ? b : null;
        }

        /// <summary>The nearest living player body to a point (null = none).</summary>
        public static HaloAiBody NearestPlayer(Vector3 p)
        {
            HaloAiBody best = null; float bd = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var b = All[i];
                if (!b.IsPlayer || !b.Alive) continue;
                float d = (b.Feet - p).sqrMagnitude;
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }

        /// <summary>A sight line between two points (true = clear). Counts against the per-tick budget: when the budget is spent it answers
        /// <paramref name="whenOverBudget"/> without casting.</summary>
        public static bool Clear(Vector3 from, Vector3 to, bool whenOverBudget)
        {
            if (RaysThisTick >= RayBudget) return whenOverBudget;
            RaysThisTick++; RaysTotal++;
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.05f) return true;
            return !Physics.Raycast(from, d / len, len - 0.05f, Blockers, QueryTriggerInteraction.Ignore);
        }

        public static bool BudgetLeft => RaysThisTick < RayBudget;

        /// <summary>The nearest recent corpse (HaloBipeds.Corpses) within <paramref name="r"/> metres of a point, or null.</summary>
        public static HaloUnit CorpseNear(Vector3 p, float r)
        {
            var list = HaloBipeds.Corpses;
            if (list == null) return null;
            HaloUnit best = null; float bd = r * r;
            for (int i = 0; i < list.Count; i++)
            {
                var u = list[i];
                if (u == null) continue;
                float d = (u.transform.position - p).sqrMagnitude;
                if (d <= bd) { bd = d; best = u; }
            }
            return best;
        }
    }
}

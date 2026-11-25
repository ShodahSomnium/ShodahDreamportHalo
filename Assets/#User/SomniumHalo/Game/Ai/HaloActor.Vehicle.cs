using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] Actors and vehicles: boarding (ai_go_to_vehicle: walk to the seat's enter marker, then V's SeatUnit), getting out
    /// (ai_exit_vehicle), and riding: a driver drives (V's SetAiInput throttle / turn, SteerToward) toward the leader it follows, its command
    /// list / goal, or around its target at the variant's combat range; a gunner (or the driver of a vehicle whose driver seat has the guns)
    /// aims at its target and fires while it sees it. One SetAiInput per vehicle and tick: the seats' wishes are merged in HaloAi.
    /// </summary>
    public sealed partial class HaloActor
    {
        HaloVehicle _board; int _boardSeat = -1; float _boardUntil;
        Vector3 _vehLast; bool _vehSampled;

        public HaloVehicle Vehicle
        {
            get
            {
                if (Unit == null || Unit.Seat == null) return null;
                return Unit.Seat.GetComponentInParent<HaloVehicle>();
            }
        }

        public bool Boarding => _board != null;

        /// <summary>Heads for a free seat of the vehicle whose label contains <paramref name="seatPart"/> ("" = any; drivers first).</summary>
        public bool GoToVehicle(HaloVehicle v, string seatPart, bool overrideCurrent)
        {
            if (v == null || Unit == null || v.Destroyed) return false;
            if (_board != null && !overrideCurrent) return false;
            int seat = FreeSeat(v, seatPart);
            if (seat < 0) return false;
            _board = v; _boardSeat = seat; _boardUntil = Now + 30f; _riderOfPlayer = false;
            return true;
        }

        static int FreeSeat(HaloVehicle v, string part)
        {
            int best = -1;
            for (int i = 0; i < v.Seats.Count; i++)
            {
                var s = v.Seats[i];
                if (s.Occupant >= 0 || s.Unit != null || HaloAi.SeatClaimed(v, i)) continue;
                if (!string.IsNullOrEmpty(part) && s.Label.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (best < 0 || Rank(s) > Rank(v.Seats[best])) best = i;
            }
            return best;
        }

        static int Rank(HaloVehicle.SeatInfo s) { return s.Def.Driver ? 2 : (s.Def.Gunner || s.Guns ? 1 : 0); }

        /// <summary>A free gunner / passenger seat for a follower joining the player's vehicle - never the driver's, never a locked or
        /// invisible seat; actv "prefer passenger seat" ranks passengers (seats that allow weapons) above the gunner.</summary>
        int FreeRiderSeat(HaloVehicle v)
        {
            bool preferPassenger = V != null && V.Has(HaloActorVariantDef.PreferPassengerSeat);
            int best = -1, bestRank = -1;
            for (int i = 0; i < v.Seats.Count; i++)
            {
                var s = v.Seats[i];
                if (s.Def.Driver || s.Def.Invisible || (s.Def.Flags & HaloVehicleDef.SeatLocked) != 0) continue;
                if (s.Occupant >= 0 || s.Unit != null || HaloAi.SeatClaimed(v, i)) continue;
                bool gunner = s.Def.Gunner || s.Guns;
                if (!gunner && !s.Def.AllowsWeapons) continue;       // cargo / troop bays: not a rider seat
                int rank = gunner ? (preferPassenger ? 1 : 2) : (preferPassenger ? 2 : 1);
                if (rank > bestRank) { bestRank = rank; best = i; }
            }
            return best;
        }

        bool _riderOfPlayer; float _nextJoinCheck;
        static int _joinLogs, _ownGunLogs;

        /// <summary>actr "gets in vehicles with player": a follower whose leader (the local player) drives a vehicle within 6 wu (ours)
        /// heads for its gunner / passenger seat (checked once a second).</summary>
        bool TryJoinPlayerVehicle()
        {
            if (A == null || !A.Has(HaloActorDef.GetsInVehiclesWithPlayer) || Unit == null || Unit.Seat != null || Now < _nextJoinCheck) return false;
            _nextJoinCheck = Now + 1f;
            if (HaloLocal.Vehicle == null || !HaloLocal.Driving || !FollowLeader(out var lead) || lead.Kind != HaloAiBody.Kinds.LocalPlayer) return false;
            var v = HaloLocal.Vehicle.GetComponent<HaloVehicle>();
            if (v == null) v = HaloLocal.Vehicle.GetComponentInParent<HaloVehicle>();
            if (v == null || v.Destroyed || (v.transform.position - Position).magnitude > 6f * HaloSpace.Unit) return false;
            int seat = FreeRiderSeat(v);
            if (seat < 0) return false;
            _board = v; _boardSeat = seat; _boardUntil = Now + 10f; _riderOfPlayer = true;      // ours: 10 s to get in
            if (_joinLogs++ < 3) HaloLog.Info("AI: " + Label + " joins the player's " + v.name + " (" + v.Seats[seat].Label + ")");
            return true;
        }

        public int BoardSeat => _board != null ? _boardSeat : -1;
        public HaloVehicle BoardTarget => _board;

        public void ExitVehicle()
        {
            _board = null;
            var v = Vehicle;
            if (v != null) v.UnseatUnit(Unit);
        }

        /// <summary>Boarding walk (true = it took the tick).</summary>
        bool TickBoarding()
        {
            if (_board == null && !TryJoinPlayerVehicle()) return false;
            if (_board.Destroyed || Now > _boardUntil || _boardSeat >= _board.Seats.Count) { _board = null; return false; }
            var s = _board.Seats[_boardSeat];
            if (s.Unit != null && s.Unit != Unit)
            {
                int other = _riderOfPlayer ? FreeRiderSeat(_board) : FreeSeat(_board, "");
                if (other < 0) { _board = null; return false; }
                _boardSeat = other; s = _board.Seats[other];
            }
            Vector3 at = s.Enter != null ? s.Enter.position : _board.transform.position;
            float d = Flat(at - Position);
            if (d < 1.6f)
            {
                // the enter marker must be in reach in 3D too (a hovering Pelican / dropship): wait below it until it comes down
                if (Mathf.Abs(at.y - Position.y) > 2f)
                {
                    Stop();
                    Face(Unit.Eye + (at - Position).normalized * 10f, false);
                    return true;
                }
                if (_board.SeatUnit(_boardSeat, Unit)) { HaloAiVoice.Say(this, HaloAiVoice.VehicleEntry, 0.5f); HaloAi.NoteBoarded(this, _board, s.Label); Stop(); }
                _board = null;
                return true;
            }
            MoveTo(OnNav(at), 1f, 1.2f);
            Face(Unit.Eye + (at - Position).normalized * 10f, false);
            Steer(Dt);
            return true;
        }

        const float Dt = 1f / 30f;
        /// <summary>Riding (true = it took the tick).</summary>
        bool TickRiding()
        {
            var v = Vehicle;
            if (v == null) return false;
            Unit.MoveVelocity = Vector3.zero;
            int seat = v.SeatOfUnit(Unit);
            var s = seat >= 0 ? v.Seats[seat] : null;
            if (s == null) return true;
            if (v.Destroyed) { v.UnseatUnit(Unit); return true; }
            // riders who got in with the player get out when the player is no longer driving it
            if (_riderOfPlayer && (HaloLocal.Vehicle == null || HaloLocal.Vehicle.GetComponentInParent<HaloVehicle>() != v))
            {
                _riderOfPlayer = false;
                v.UnseatUnit(Unit);
                return true;
            }
            bool flying = v.Def.Type == HaloVehicleDef.Kind.AlienFighter || v.Def.Type == HaloVehicleDef.Kind.HumanPlane;
            bool visible = Target != null && TargetVisible;
            Vector3 aim = visible ? AimPointFor(Target.Head) : Vector3.zero;
            if (aim != Vector3.zero) { Unit.LookPoint = Target.Head; Unit.AimPoint = aim; }
            bool shoots = s.Guns || s.Def.Gunner;
            // seats that "allow weapons" (warthog passenger, dropship troop seats): the rider fires its own weapon at what it sees
            if (!shoots && s.Def.AllowsWeapons && Unit.Weapon != null && !RangedInhibited)
            {
                if (visible) Shoot(Dt, true);
                if (_ownGunLogs == 0 && Unit.Firing) { _ownGunLogs++; HaloLog.Info("AI: " + Label + " fires its own weapon from " + v.name + " (" + s.Label + ", seat allows weapons)"); }
                if (!s.Def.Driver) return true;
            }
            // bursts by the vehicle's own actor variant (the "banshee pilot" / "warthog gunner" actv whose unit is this vehicle), else ours
            var vv = HaloAi.VehicleVariant(v.Def.Tag) ?? V;
            float range = vv != null && vv.MaximumFiringDistance > 0f ? vv.MaximumFiringDistance : 40f;
            bool can = shoots && visible && !RangedInhibited && (Target.Center - v.transform.position).magnitude < range * HaloSpace.Unit;
            // fixed guns (banshee): when the nose points at the target and the line from the nose is clear (the pilot's own eye sits in the hull)
            if (flying && shoots && Target != null && Target.Alive && !RangedInhibited)
            {
                Vector3 to = Target.Center - v.transform.position; float dd = to.magnitude;
                can = dd < Mathf.Max(range * HaloSpace.Unit, 90f) && Vector3.Dot(v.transform.forward, to / dd) > 0.88f && HaloAiBodies.Clear(v.transform.position + v.transform.forward * 3f, Target.Center, true);
                if (can) { TargetSeenAt = Now; TargetLastSeen = Target.Feet; }
                // never through a friendly vehicle (another banshee on the same run): Halo's "avoid friends' line of fire"
                RaycastHit fh;
                if (can && Physics.SphereCast(v.transform.position + v.transform.forward * 3f, 2f, to / dd, out fh, dd, 1 << HaloLayers.Vehicle, QueryTriggerInteraction.Ignore)
                    && fh.collider.GetComponentInParent<HaloVehicle>() != v) can = false;
            }
            bool fire = VehicleBurst(vv, can);
            bool fire2 = fire && _vehFire2;
            if (s.Def.Driver)
            {
                if (flying) Fly(v, aim, fire && s.Guns, fire2 && s.Guns);
                else Drive(v, aim, fire && s.Guns);
                HaloAi.NoteDrove(this, v);
            }
            else if (shoots) HaloAi.VehicleWish(v, 0f, 0f, false, aim, fire);
            return true;
        }

        bool _vBursting, _vehFire2; float _vBurstUntil, _vNextBurst;

        /// <summary>The vehicle weapon's bursts: actv burst duration / separation (x difficulty), the secondary trigger (fuel rod / special
        /// fire) at the variant's special fire chance per burst.</summary>
        bool VehicleBurst(HaloActorVariantDef vv, bool can)
        {
            if (_vBursting)
            {
                if (!can || Now >= _vBurstUntil)
                {
                    _vBursting = false;
                    float sep = vv != null && vv.BurstSeparation.Max > 0f ? vv.BurstSeparation.Pick(HaloAi.Rng) : 0.7f;
                    _vNextBurst = Now + Mathf.Max(0.15f, sep * HaloAi.DiffScale(HaloAi.DiffTable.BurstSeparation));
                }
            }
            else if (can && Now >= _vNextBurst)
            {
                _vBursting = true;
                float len = vv != null && vv.BurstDuration.Max > 0f ? vv.BurstDuration.Pick(HaloAi.Rng) : 1f;
                _vBurstUntil = Now + Mathf.Max(0.2f, len);
                float sp = vv != null && vv.SpecialFireChance > 0f ? vv.SpecialFireChance : 0.15f;     // ours when the tag has none
                _vehFire2 = HaloAi.Chance(sp);
                HaloAi.NoteShot(this);
            }
            return _vBursting && can;
        }

        void Drive(HaloVehicle v, Vector3 aim, bool fire)
        {
            Vector3 goal; float stop;
            if (!DriveGoal(v, out goal, out stop)) { HaloAi.VehicleWish(v, 0f, 0f, true, aim, fire); return; }
            Vector3 to = goal - v.transform.position; to.y = 0f;
            float dist = to.magnitude;
            float turn = v.SteerToward(goal);
            float ahead = dist > 0.01f ? Vector3.Dot(v.transform.forward, to / dist) : 1f;
            // forward speed from our own position samples (m/s)
            Vector3 vp = v.transform.position;
            float speed = _vehSampled ? Vector3.Dot(vp - _vehLast, v.transform.forward) / Dt : 0f;
            _vehLast = vp; _vehSampled = true;
            float throttle = dist > stop ? Mathf.Clamp(dist / 12f, 0.35f, 1f) : 0f;
            if (Mathf.Abs(turn) > 0.85f && dist < 15f) throttle *= 0.5f;
            // ours: brake when faster than the distance left allows (about 0.6 s to cover it), and stop at the goal
            float allowed = Mathf.Max(3f, (dist - stop) * 0.6f);
            if (_ramming) allowed = 99f;
            if (speed > allowed + 1f) throttle = -1f;
            else if (dist <= stop && speed > 0.5f) throttle = -1f;
            if (ahead < -0.3f && dist < 10f && speed < 1f) { throttle = -0.6f; turn = -turn; }     // behind and close: back up
            HaloAi.VehicleWish(v, throttle, turn, true, aim, fire);
        }

        bool _ramming;
        int _runPhase; float _runUntil; Vector3 _runExit;

        /// <summary>
        /// Flying (banshees, dropships): the command list's 3D points / script goal first; with a target, Halo's attack runs - dive at it
        /// firing along the nose, pass over, pull up and away, turn and come back; else hold near home. Never below 4 m over the ground.
        /// </summary>
        void Fly(HaloVehicle v, Vector3 aim, bool fire, bool fire2)
        {
            Vector3 p = v.transform.position;
            Vector3 to; float throttle;
            if (_hasGoal && !_arrived)
            {
                to = _goal;
                float d = (to - p).magnitude;
                float accept = Mathf.Max(Mathf.Max(4f, _acceptRadius), (_vehSampled ? ((p - _vehLast) / Dt).magnitude : 0f) * 0.8f);   // a fast flyer passes through its point
                if (d < accept) { _arrived = true; }
                throttle = Mathf.Clamp(d / 25f, 0.35f, 1f);
            }
            else if (Target != null && Target.Alive)
            {
                Vector3 tp = Target.Center;
                float d = (tp - p).magnitude;
                if (_runPhase == 0)
                {
                    // the run: straight at the target, a little above it
                    to = tp + Vector3.up * (d > 40f ? 8f : 4f);
                    throttle = d > 40f ? 1f : 0.75f;
                    if (d < 24f)
                    {
                        _runPhase = 1; _runUntil = Now + HaloAi.Rand(2.5f, 3.5f);
                        Vector3 flat = v.transform.forward; flat.y = 0f; if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
                        _runExit = tp + flat.normalized * HaloAi.Rand(45f, 60f) + Vector3.Cross(Vector3.up, flat.normalized) * ((Id % 2 == 0 ? 1f : -1f) * HaloAi.Rand(15f, 30f)) + Vector3.up * HaloAi.Rand(14f, 22f);
                    }
                }
                else
                {
                    // pull out past the target, then turn back for the next run
                    to = _runExit;
                    throttle = 1f;
                    fire = false; fire2 = false;
                    if (Now >= _runUntil || (to - p).magnitude < 8f) _runPhase = 0;
                }
            }
            else
            {
                to = Home + Vector3.up * 3f;
                throttle = (to - p).magnitude > 10f ? 0.6f : 0.15f;
            }
            // ground clearance (ours): 6 m over what is below, and pull up when the ground is in the way ahead (1.5 s of flight)
            RaycastHit h;
            if (Physics.Raycast(p, Vector3.down, out h, 14f, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore) && h.distance < 6f && to.y < p.y + 3f)
                to.y = p.y + 8f;
            Vector3 vel = _vehSampled ? (p - _vehLast) / Dt : v.transform.forward * 10f;
            _vehLast = p; _vehSampled = true;
            float look = Mathf.Clamp(vel.magnitude * 1.5f, 10f, 45f);
            if (vel.sqrMagnitude > 1f && Physics.Raycast(p, vel.normalized, out h, look, HaloAiBodies.Blockers, QueryTriggerInteraction.Ignore))
            { to = p + Vector3.up * 20f + Vector3.ProjectOnPlane(vel, Vector3.up).normalized * 5f; throttle = Mathf.Min(throttle, 0.5f); }
            // two banshees on one target: keep 8 m apart (ours)
            for (int i = 0; i < HaloAi.Actors.Count; i++) { var o = HaloAi.Actors[i]; if (o == this || !o.Alive || o.Vehicle == null || o.Vehicle == v) continue; Vector3 dd = p - o.Vehicle.transform.position; if (dd.sqrMagnitude < 64f) to += dd.normalized * 8f; }
            HaloAi.VehicleFlight(v, throttle, to, aim, fire, fire2);
        }

        /// <summary>Where the driver goes: its boarding passengers wait, the followed leader (stopping 8 m short), the command list / script
        /// goal, else circling its target at the variant's desired range.</summary>
        bool DriveGoal(HaloVehicle v, out Vector3 goal, out float stop)
        {
            goal = Vector3.zero; stop = 4f;
            if (HaloAi.SomeoneBoarding(v)) return false;
            if (FollowLeader(out var lead)) { goal = lead.Feet; stop = 8f; return true; }
            _ramming = false;
            if (_hasGoal && !_arrived)
            {
                Vector3 dg = _goal - v.transform.position; dg.y = 0f;
                if (dg.magnitude < Mathf.Max(4f, _acceptRadius)) _arrived = true;     // command list / script goal reached
                else { goal = _goal; stop = 3f; return true; }
            }
            if (Target != null && v.SeatOfUnit(Unit) >= 0 && v.Seats[v.SeatOfUnit(Unit)].Guns && TargetVisible)
            {
                // a driver with the guns (ghost): straight at the target, strafing it, running it over when it stays (ours: 25 m)
                goal = Target.Feet + Vector3.Cross(Vector3.up, (Target.Feet - v.transform.position).normalized) * (Mathf.Sin(Now * 0.7f + Id) * 6f);
                _ramming = (Target.Feet - v.transform.position).magnitude < 25f;
                stop = 0f;
                return true;
            }
            if (Target != null)
            {
                float r = V != null ? Mathf.Max(6f, V.DesiredCombatRange.Max * HaloSpace.Unit) : 20f;
                Vector3 d = v.transform.position - TargetLastSeen; d.y = 0f;
                if (d.sqrMagnitude < 0.01f) d = v.transform.forward;
                Vector3 side = Vector3.Cross(Vector3.up, d.normalized);
                goal = TargetLastSeen + d.normalized * r + side * r * 0.6f;
                stop = 3f;
                return true;
            }
            return false;
        }
    }

    public static partial class HaloAi
    {
        sealed class Wish { public float Throttle, Turn; public bool Drive, Fire, Fire2, Fly; public Vector3 Aim, FlyTo; public bool HasAim; }
        static readonly Dictionary<HaloVehicle, Wish> Wishes = new Dictionary<HaloVehicle, Wish>();
        static readonly List<HaloVehicle> WishOrder = new List<HaloVehicle>(8);

        internal static void VehicleWish(HaloVehicle v, float throttle, float turn, bool drive, Vector3 aim, bool fire)
        {
            Wish w;
            if (!Wishes.TryGetValue(v, out w)) { w = new Wish(); Wishes[v] = w; }
            if (!WishOrder.Contains(v)) { WishOrder.Add(v); w.Throttle = 0f; w.Turn = 0f; w.Drive = false; w.Fire = false; w.HasAim = false; w.Aim = Vector3.zero; w.Fly = false; w.Fire2 = false; }
            if (drive) { w.Throttle = throttle; w.Turn = turn; w.Drive = true; }
            if (aim != Vector3.zero && (!w.HasAim || !drive)) { w.Aim = aim; w.HasAim = true; }
            w.Fire |= fire;
        }

        static void ApplyVehicleWishes()
        {
            for (int i = 0; i < WishOrder.Count; i++)
            {
                var v = WishOrder[i];
                if (v == null) continue;
                var w = Wishes[v];
                try { if (w.Fly) v.SetAiFlight(w.Throttle, w.FlyTo, w.Aim, w.Fire, w.Fire2); else v.SetAiInput(w.Throttle, w.Turn, w.Aim, w.Fire); } catch (System.Exception e) { if (!_wishFailed) { _wishFailed = true; HaloLog.Warn("AI: SetAiInput failed: " + e.Message); } }
            }
            WishOrder.Clear();
        }

        static bool _wishFailed;

        internal static void VehicleFlight(HaloVehicle v, float throttle, Vector3 flyTo, Vector3 aim, bool fire, bool fire2)
        {
            VehicleWish(v, throttle, 0f, true, aim, fire);
            var w = Wishes[v];
            w.Fly = true; w.FlyTo = flyTo; w.Fire2 |= fire2;
            if (_flew++ == 0) HaloLog.Info("AI: first AI flight: " + v.name + " (" + v.Def.Type + ")");
        }

        static int _flew;
        static readonly Dictionary<HaloTag, HaloActorVariantDef> VehicleVariants = new Dictionary<HaloTag, HaloActorVariantDef>();

        /// <summary>The actor variant whose unit is this vehicle ("banshee pilot", "warthog gunner", "c gun turret gunner"): its firing data.</summary>
        internal static HaloActorVariantDef VehicleVariant(HaloTag vehicle)
        {
            if (vehicle == null || Map == null || Catalog == null) return null;
            HaloActorVariantDef v;
            if (VehicleVariants.TryGetValue(vehicle, out v)) return v;
            v = null;
            foreach (var t in Map.OfClass("actv")) { var r = t.Root; if (r.Ref(20) == vehicle) { var c = Catalog.Variant(t); if (c != null && c.BurstDuration.Max > 0f && c.BurstDuration.Max < 20f) { v = c; break; } } }
            VehicleVariants[vehicle] = v;
            return v;
        }

        internal static bool SeatClaimed(HaloVehicle v, int seat)
        {
            for (int i = 0; i < Actors.Count; i++) { var a = Actors[i]; if (a.Alive && a.BoardTarget == v && a.BoardSeat == seat) return true; }
            return false;
        }

        internal static bool SomeoneBoarding(HaloVehicle v)
        {
            for (int i = 0; i < Actors.Count; i++) { var a = Actors[i]; if (a.Alive && a.BoardTarget == v) return true; }
            return false;
        }

        static int _boarded, _drove;
        internal static void NoteBoarded(HaloActor a, HaloVehicle v, string seat) { if (_boarded++ < 3) HaloLog.Info("AI: " + a.Label + " boarded " + v.name + " (" + seat + ")"); }
        internal static void NoteDrove(HaloActor a, HaloVehicle v) { if (_drove++ == 0) HaloLog.Info("AI: first AI driver: " + a.Label + " drives " + v.name); }
        public static int Boarded => _boarded;
    }
}

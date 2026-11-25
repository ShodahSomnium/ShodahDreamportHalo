using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The campaign side of a vehicle (phase 2): scenario placements with their name (HaloObjects), vitality from the collision model
    /// (campaign banshees 100, ghosts 160 body points; the MP tags have none), AI / scripted units in seats (the Characters module's
    /// HaloUnit.EnterSeat parents the unit under the seat marker - such a seat counts as taken), the AI module's driving surface
    /// (<see cref="SetAiInput"/>), and the script's vehicle switches (enterable by the player, hover, doors).
    /// </summary>
    public sealed partial class HaloVehicle
    {
        /// <summary>A seat a HaloUnit sits in (SeatInfo.Occupant): units are not net slots.</summary>
        public const int UnitOccupant = 1 << 20;

        /// <summary>Placed for a campaign mission: no MP abandoned-respawn, the tag's vitality applies.</summary>
        public bool Campaign;
        /// <summary>The scenario object name ("" = unnamed).</summary>
        public string ObjectName = "";
        /// <summary>Body vitality left (fraction; campaign vehicles whose collision model has body vitality).</summary>
        public float Vitality = 1f;
        public bool Destroyed { get; private set; }
        /// <summary>unit_set_enterable_by_player: false = the player cannot get in.</summary>
        public bool EnterableByPlayer = true;
        /// <summary>vehicle_hover: held still in the air where it is.</summary>
        public bool Hovering { get; private set; }

        public static event Action<HaloVehicle> VehicleDestroyed;

        // ------------------------------------------------------------------ units in seats

        /// <summary>The HaloUnit sitting on a seat (its GameObject under the seat marker), or null.</summary>
        public HaloUnit UnitIn(int seat)
        {
            var s = SeatOf(seat);
            if (s == null || s.Cargo != null || s.Marker == null || s.Marker.childCount == 0) return null;
            var u = s.Marker.GetComponentInChildren<HaloUnit>();
            return u != null && u.Alive ? u : null;
        }

        /// <summary>The seat a unit sits in here (-1 none).</summary>
        public int SeatOfUnit(HaloUnit u)
        {
            if (u == null || u.Seat == null) return -1;
            for (int i = 0; i < Seats.Count; i++) if (Seats[i].Marker == u.Seat) return i;
            return -1;
        }

        // ------------------------------------------------------------------ vehicles in seats (cargo)

        /// <summary>A seat a carried vehicle occupies (SeatInfo.Occupant).</summary>
        public const int CargoOccupant = 1 << 21;

        /// <summary>The vehicle carrying this one on a cargo seat (null = free).</summary>
        public HaloVehicle CarriedBy { get; private set; }
        private Transform _cargoHome;
        private bool _cargoEnterable = true;

        /// <summary>
        /// Carries a cargo vehicle on a seat (Halo: vehicles are units, so a warthog / ghost can occupy a vehicle seat - pelican "cargo",
        /// c_dropship "cargo_ghost01..03" / "cargo_wraith", seat flags 0x410): the cargo's root sits on the seat marker (the marker's +X =
        /// Halo forward is its forward, as for units), kinematic, not enterable by the player, its contact spheres ignore the carrier's.
        /// False when the seat is taken or the cargo cannot ride.
        /// </summary>
        public bool SeatVehicle(int seat, HaloVehicle cargo)
        {
            var s = SeatOf(seat);
            if (s == null || cargo == null || cargo == this || cargo.Destroyed || cargo.Body == null) return false;
            if (s.Cargo == cargo) return true;
            if (s.Cargo != null || s.Unit != null || (s.Occupant >= 0 && s.Occupant != CargoOccupant)) return false;
            if (transform.IsChildOf(cargo.transform)) return false;      // (a carrier inside its own cargo)
            if (cargo.CarriedBy != null) cargo.CarriedBy.ReleaseCargo(cargo, false);
            cargo._cargoHome = cargo.transform.parent;
            cargo._cargoEnterable = cargo.EnterableByPlayer;
            cargo.CarriedBy = this;                                     // (before Park: it never settles while carried)
            cargo._aiDriving = false;
            cargo.Controls = default(HaloVehicleControls);
            cargo.Park(false);
            cargo.Body.interpolation = RigidbodyInterpolation.None;      // a kinematic child body follows its parent: no stale interpolated pose
            Transform at = s.Marker != null ? s.Marker : transform;
            cargo.transform.SetParent(at, false);
            cargo.transform.localPosition = Vector3.zero;
            cargo.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // the seat marker's +X (Halo forward) is the cargo's forward
            cargo.EnterableByPlayer = false;
            IgnoreCargo(cargo);
            s.Cargo = cargo; s.Occupant = CargoOccupant;
            _sweepDirty = true;
            LastUsed = Time.time;
            HaloLog.Info("Vehicles: " + cargo.name + " rides as cargo in " + name + " seat '" + s.Label + "' (marker " + (s.Marker != null && s.Marker != transform ? s.Marker.name : "none: the hull origin") + ").");
            return true;
        }

        /// <summary>Releases a cargo vehicle at its current pose: unparented, dynamic with the carrier's velocity, it drops and settles
        /// (Park(true)) - vehicle_unload / unit_exit_vehicle on a carried warthog or ghost.</summary>
        public void UnseatVehicle(HaloVehicle cargo) { ReleaseCargo(cargo, true); }

        private void ReleaseCargo(HaloVehicle cargo, bool drop)
        {
            if (cargo == null || cargo.CarriedBy != this) return;
            string label = "";
            for (int i = 0; i < Seats.Count; i++)
                if (Seats[i].Cargo == cargo) { label = Seats[i].Label; Seats[i].Cargo = null; Seats[i].Occupant = -1; }
            Vector3 vel = Velocity;
            Transform home = cargo._cargoHome;
            cargo.transform.SetParent(home != null ? home : (transform.parent), true);
            cargo.CarriedBy = null;
            cargo.EnterableByPlayer = cargo._cargoEnterable;
            cargo.Body.interpolation = RigidbodyInterpolation.Interpolate;
            _sweepDirty = true;
            if (!drop) return;
            Physics.SyncTransforms();
            cargo.Park(true);
            if (!cargo.Body.isKinematic) cargo.Body.linearVelocity = vel;
            float h = HeightOverGround(cargo);
            HaloLog.Info("Vehicles: " + cargo.name + " released from " + name + " seat '" + label + "' " + (h >= 0f ? h.ToString("0.0") + " m over the ground" : "(no ground under it)") + (cargo.Body.isKinematic ? ", held (no ground / no physics)" : ", drops and settles") + ".");
        }

        /// <summary>The carrier's and the cargo's contact spheres never touch each other (ours: a released jeep must not catch on the hull).</summary>
        private void IgnoreCargo(HaloVehicle cargo)
        {
            for (int i = 0; i < Shapes.Count; i++)
                for (int k = 0; k < cargo.Shapes.Count; k++)
                    if (Shapes[i] != null && cargo.Shapes[k] != null) Physics.IgnoreCollision(Shapes[i], cargo.Shapes[k], true);
        }

        /// <summary>The cargo vehicle on a seat (null none).</summary>
        public HaloVehicle CargoIn(int seat) { var s = SeatOf(seat); return s != null ? s.Cargo : null; }

        /// <summary>The seat a carried vehicle occupies here (-1 none).</summary>
        public int SeatOfCargo(HaloVehicle cargo)
        {
            if (cargo == null) return -1;
            for (int i = 0; i < Seats.Count; i++) if (Seats[i].Cargo == cargo) return i;
            return -1;
        }

        /// <summary>Lets a cargo vehicle go where it is without dropping it (kinematic, unparented, enterable as before) - checkpoint revert.</summary>
        internal void ReleaseCargoInPlace(HaloVehicle cargo) { ReleaseCargo(cargo, false); }

        /// <summary>The vehicle's clearance: metres its contact shapes could still drop before one meets the ground (-1 = none within
        /// 500 m). Per contact sphere, from its centre down (a single ray from the bounds' centre missed the floor of a hull resting on a
        /// slope or a cliff edge - its lowest point is then under the terrain at the centre - and logged "no ground under it").</summary>
        private static float HeightOverGround(HaloVehicle v)
        {
            float best = -1f;
            for (int s = 0; s < v.Shapes.Count; s++)
            {
                var c = v.Shapes[s] as SphereCollider;
                if (c == null) continue;
                Vector3 o = c.transform.TransformPoint(c.center);
                float rad = c.radius * c.transform.lossyScale.x;
                float d = DownToGround(v, o, rad);
                if (d >= 0f && (best < 0f || d < best)) best = d;
            }
            if (best >= 0f) return best;
            // no sphere has ground under it (or the vehicle has no spheres): the old bounds test
            Bounds b = v.ShapeBounds();
            return DownToGround(v, new Vector3(b.center.x, b.min.y + 0.5f, b.center.z), 0.5f);
        }

        /// <summary>Metres from <paramref name="from"/> down to the first ground hit, minus <paramref name="less"/>, at least 0 (-1 = none).</summary>
        private static float DownToGround(HaloVehicle v, Vector3 from, float less)
        {
            int n = Physics.RaycastNonAlloc(from, Vector3.down, Hits, 500f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore);
            float best = -1f;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null || v.IsOwn(h.collider)) continue;
                if (best < 0f || h.distance < best) best = h.distance;
            }
            return best < 0f ? -1f : Mathf.Max(0f, best - less);
        }

        // ------------------------------------------------------------------ recorded control (recording_play on a vehicle)

        /// <summary>What the last recording played on this vehicle did (log / lab).</summary>
        public struct RecordingStats
        {
            public string Name;
            public int Ticks;
            /// <summary>The first tick a sweep met the ground (-1 never).</summary>
            public int FirstContactTick;
            /// <summary>Ticks a descent stopped on the ground (landing) / ticks the hull met terrain sideways or above (should be 0).</summary>
            public int Landings, TerrainContacts;
            /// <summary>Ticks a contact slid a human plane upward (terrain under / ahead of the hull lifted it, Halo's collision response).</summary>
            public int Climbs;
            /// <summary>Ticks it flew with its collision off (object_set_collideable false: the retail exits pass through hills).</summary>
            public int PassThrough;
            /// <summary>Metres from the lowest contact shape to the ground at the end (-1 none).</summary>
            public float EndHeight;
            /// <summary>The largest hull pitch / roll seen (degrees; 0 for human planes: heading only).</summary>
            public float MaxTilt;
            public bool Hover;
        }

        /// <summary>The last finished recording's numbers.</summary>
        public RecordingStats LastRecording { get; private set; }
        /// <summary>A recording drives this vehicle right now (its own recorded control).</summary>
        public bool RecordingDrives => _rec;

        private bool _rec;
        private Vector3 _recPrev, _recPos, _recHeading, _recFace;
        private Quaternion _recPrevRot, _recRot;
        private float _recSpeed, _recSide, _recVert;     // world units per tick: forward, left, up
        private Vector3 _recVel;                         // a human plane's horizontal velocity, world units per tick (world frame)
        private HaloPlaneState _plane;                   // a human plane's recorded flight (HaloPlaneFlight, Halo space)
        private bool _saidGhost, _ghostTick;
        private RecordingStats _recStats;
        private static bool _saidPlane, _saidFighter, _saidGround;

        // the shapes swept against the BSP: the contact spheres (and a carried vehicle's) in the root frame, plus a bounding sphere
        private Vector3[] _sweepLocal = new Vector3[0];
        private float[] _sweepRadius = new float[0];
        private int _sweepCount;
        private bool _sweepDirty = true;
        private Vector3 _sweepCenter; private float _sweepBound;
        private static readonly RaycastHit[] SweepHits = new RaycastHit[16];
        private const float Skin = 0.02f;    // ours: metres kept between a swept shape and the ground

        /// <summary>
        /// One game tick of a recording (Halo plays the recorded unit control as this vehicle's control; the vehicle's own physics moves it).
        /// Human planes (c_dropship, pelican - vehi type 3) fly <see cref="HaloPlaneFlight"/> (Data/Vehicles/HaloVehicleDef.cs, the same code
        /// the offline probe measures against the retail scenarios): a level hull turning toward the recorded facing, control speeds ramped
        /// by the vehicle tag's accelerations, the hull velocity following them and losing the physics tag's air friction every tick, Jump /
        /// Crouch asking for maximum forward speed up / down. Every move is swept against the BSP: a descent stops on the ground, a
        /// horizontal move into terrain slides along it (the velocity into the surface is lost). Alien fighters (banshee) fly along their 3D
        /// facing; ground vehicles follow the floor.
        /// </summary>
        public void RecordedControl(HaloRecordedControl c)
        {
            if (Def == null) return;
            if (!_rec) BeginRecorded();
            _recPrev = _recPos; _recPrevRot = _recRot;
            _recStats.Ticks++;
            switch (Def.Type)
            {
                case HaloVehicleDef.Kind.HumanPlane: PlaneTick(ref c); break;
                case HaloVehicleDef.Kind.AlienFighter: FighterTick(ref c); break;
                default: GroundTick(ref c); break;
            }
            if (Def.Type != HaloVehicleDef.Kind.HumanPlane)
            {
                Vector3 e = _recRot.eulerAngles;
                float tilt = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(0f, e.x)), Mathf.Abs(Mathf.DeltaAngle(0f, e.z)));
                if (tilt > _recStats.MaxTilt) _recStats.MaxTilt = tilt;
            }
        }

        /// <summary>The recording ended (<paramref name="hover"/>: recording_play_and_hover - held in the air where it is) or was killed.</summary>
        public void RecordedControlEnd(bool hover)
        {
            if (!_rec) return;
            _rec = false;
            transform.SetPositionAndRotation(_recPos, _recRot);
            Physics.SyncTransforms();
            _recStats.Hover = hover;
            _recStats.EndHeight = HeightOverGround(this);
            LastRecording = _recStats;
            HaloLog.Info("Vehicles: " + name + " recording '" + _recStats.Name + "' " + (hover ? "ends hovering" : "ends") + " after " + _recStats.Ticks + " ticks: "
                         + (_recStats.EndHeight >= 0f ? (_recStats.EndHeight / HaloSpace.Unit).ToString("0.0") + " wu (" + _recStats.EndHeight.ToString("0.0") + " m) over the ground" : "no ground under it")
                         + ", ground contact " + (_recStats.FirstContactTick >= 0 ? "from tick " + _recStats.FirstContactTick : "never") + " (" + _recStats.Landings + " landing ticks, "
                         + _recStats.TerrainContacts + " terrain-contact ticks, " + _recStats.Climbs + " ticks slid upward, " + _recStats.PassThrough + " ticks not collideable), max tilt " + _recStats.MaxTilt.ToString("0") + " deg.");
            _recSpeed = 0f; _recSide = 0f; _recVert = 0f; _recVel = Vector3.zero; _plane = default(HaloPlaneState);
            if (Body == null) return;
            if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            if (hover) { Body.isKinematic = true; return; }   // held where it is (kinematic) until the next recording / vehicle_hover false
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            // planes / fighters stay where the recording left them (Halo: a human plane hovers; an AI pilot takes a banshee from here); ground
            // vehicles settle on the floor
            bool flier = Def.Type == HaloVehicleDef.Kind.HumanPlane || Def.Type == HaloVehicleDef.Kind.AlienFighter;
            Park(!flier);
        }

        private void BeginRecorded()
        {
            _rec = true;
            _recPos = _recPrev = transform.position;
            _recRot = _recPrevRot = transform.rotation;
            Vector3 f = transform.forward; f.y = 0f;
            _recHeading = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            _recFace = transform.forward;
            _recSpeed = 0f; _recSide = 0f; _recVert = 0f; _recVel = Vector3.zero;
            _settleUntil = 0f;
            LocalDriven = false; RemoteDriven = false;
            _aiDriving = false;
            Controls = default(HaloVehicleControls);
            if (Body != null)
            {
                if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
                Body.isKinematic = true;
                Body.interpolation = RigidbodyInterpolation.None;   // moved by its transform between ticks (RecordedFrame)
            }
            _sweepDirty = true;
            string rec = HaloCinematic.RecordingName(gameObject);
            _recStats = new RecordingStats { Name = rec ?? "?", FirstContactTick = -1, EndHeight = -1f };
            _saidGhost = false;
            if (Def.Type == HaloVehicleDef.Kind.HumanPlane) _plane = HaloPlaneFlight.Start(HaloYaw(_recHeading));
            if (Def.Type == HaloVehicleDef.Kind.HumanPlane && !_saidPlane)
            {
                _saidPlane = true;
                HaloLog.Info("Vehicles: recorded human-plane flight (" + Def.Name + "): hull turns toward the facing (" + HaloPlaneFlight.YawResponse.ToString("0.###") + " a tick), control speeds forward "
                             + Def.MaxForward.ToString("0.###") + " wu/tick (accel " + Def.SpeedAcceleration.ToString("0.###") + " / decel " + Def.SpeedDeceleration.ToString("0.###") + "), Jump / Crouch "
                             + Def.MaxForward.ToString("0.###") + " wu/tick up / down, velocity follows at " + HaloPlaneFlight.VelocityResponse.ToString("0.###") + " a tick with air friction "
                             + HaloPlaneFlight.AirFriction(Def).ToString("0.###") + ", swept against the BSP.");
            }
            else if (Def.Type == HaloVehicleDef.Kind.AlienFighter && !_saidFighter) { _saidFighter = true; HaloLog.Info("Vehicles: recorded fighter flight (" + Def.Name + "): along the 3D facing, tag speeds, swept against the BSP."); }
            else if (Def.Type != HaloVehicleDef.Kind.HumanPlane && Def.Type != HaloVehicleDef.Kind.AlienFighter && !_saidGround) { _saidGround = true; HaloLog.Info("Vehicles: recorded ground-vehicle motion (" + Def.Name + "): level heading, tag speeds, following the floor."); }
        }

        /// <summary>A recorded flight / drive in progress, as a checkpoint (HaloCinematic's "recordings" part) or the co-op host state keeps it:
        /// the last two tick poses, heading / facing, control and hull speeds, a human plane's <see cref="HaloPlaneState"/>, whether it is
        /// collideable (object_set_collideable) and the recording's numbers so far.</summary>
        public sealed class RecordedState
        {
            public Vector3 Prev, Pos, Heading, Face, Vel;
            public Quaternion PrevRot, Rot;
            public float Speed, Side, Vert;
            public HaloPlaneState Plane;
            public bool Collideable = true, SaidGhost;
            public RecordingStats Stats;
        }

        /// <summary>The recorded state now (null when no recording drives this vehicle).</summary>
        public RecordedState CaptureRecorded()
        {
            if (!_rec) return null;
            return new RecordedState
            {
                Prev = _recPrev, Pos = _recPos, Heading = _recHeading, Face = _recFace, Vel = _recVel, PrevRot = _recPrevRot, Rot = _recRot,
                Speed = _recSpeed, Side = _recSide, Vert = _recVert, Plane = _plane, Collideable = Collideable(), SaidGhost = _saidGhost, Stats = _recStats,
            };
        }

        /// <summary>A checkpoint revert / host hand-over: the recording was restarted and stepped to its tick (HaloCinematic, no control applied);
        /// this puts the flight back exactly as it was at that tick, so the next tick goes on as if nothing had happened.</summary>
        public void RestoreRecorded(RecordedState s)
        {
            if (Def == null || s == null) return;
            BeginRecorded();     // kinematic, no driver, fresh sweep shapes (a re-seated cargo is swept with the hull)
            _recPrev = s.Prev; _recPos = s.Pos; _recHeading = s.Heading; _recFace = s.Face; _recVel = s.Vel;
            _recPrevRot = s.PrevRot; _recRot = s.Rot;
            _recSpeed = s.Speed; _recSide = s.Side; _recVert = s.Vert;
            _plane = s.Plane;
            _recStats = s.Stats;
            _saidGhost = s.SaidGhost;
            if (s.Collideable != Collideable())
            {
                // object_set_collideable as the scripts do it (HaloScriptFns.Objects): every collider of the object on / off
                var cols = GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++) cols[i].enabled = s.Collideable;
            }
            transform.SetPositionAndRotation(_recPos, _recRot);
            Physics.SyncTransforms();
            _sweepDirty = true;
        }

        /// <summary>object_set_collideable: the scripts switch a dropship's collision off 30 ticks into its exit (a30 lz / fort / pipe /
        /// back / rubble cships: (recording_play_and_delete x exit) (sleep 30) (unit_close x) (object_set_collideable x false)), and Halo
        /// then flies it through the hills on its recorded track. All contact spheres off = not collideable: the flight is not swept.</summary>
        private bool Collideable()
        {
            for (int i = 0; i < Shapes.Count; i++) if (Shapes[i] != null && Shapes[i].enabled) return true;
            return Shapes.Count == 0;
        }

        /// <summary>v toward t by at most a (a &lt;= 0: at once).</summary>
        private static float Toward(float v, float t, float a)
        {
            if (a <= 0f) return t;
            return v < t ? Mathf.Min(t, v + a) : Mathf.Max(t, v - a);
        }

        /// <summary>Forward (and slide) speed toward the throttle's target: acceleration while the target is faster, deceleration otherwise.</summary>
        private void RecordedSpeeds(ref HaloRecordedControl c)
        {
            var d = Def;
            float top = c.ThrottleForward >= 0f ? d.MaxForward * c.ThrottleForward : d.MaxReverse * c.ThrottleForward;
            _recSpeed = Toward(_recSpeed, top, Mathf.Abs(top) > Mathf.Abs(_recSpeed) ? d.SpeedAcceleration : d.SpeedDeceleration);
            float side = c.ThrottleLeft * (c.ThrottleLeft >= 0f ? d.MaxLeftSlide : d.MaxRightSlide);
            _recSide = Toward(_recSide, side, Mathf.Abs(side) > Mathf.Abs(_recSide) ? d.SlideAcceleration : d.SlideDeceleration);
        }

        private void PlaneTick(ref HaloRecordedControl c)
        {
            HaloPlaneFlight.Step(Def, ref _plane, ref c);
            Quaternion rr = WorldRootRotation();
            Vector3 h = rr * HaloSpace.Dir((float)System.Math.Cos(_plane.Yaw), (float)System.Math.Sin(_plane.Yaw), 0f);
            h.y = 0f;
            if (h.sqrMagnitude > 1e-6f) _recHeading = h.normalized;
            Vector3 v = rr * HaloSpace.Dir(_plane.Vx, _plane.Vy, 0f);
            v.y = 0f;
            _recVel = v; _recVert = _plane.Vz;                                    // world units per tick
            Vector3 left = Vector3.Cross(_recHeading, Vector3.up);
            _recSpeed = Vector3.Dot(_recVel, _recHeading); _recSide = Vector3.Dot(_recVel, left);
            _recRot = Quaternion.LookRotation(_recHeading, Vector3.up);       // heading only: the hull never pitches with the look
            float u = HaloSpace.Unit;
            MoveSwept(_recVel * u, Vector3.up * (_recVert * u), true);
            // contacts removed the velocity into the surface (LoseVelocityInto) / stopped the descent: back into the flight state
            Vector3 hv = HaloSpace.DirToHalo(Quaternion.Inverse(rr) * _recVel);
            _plane.Vx = hv.x; _plane.Vy = hv.y; _plane.Vz = _recVert;
        }

        private static Quaternion WorldRootRotation()
        {
            var root = HaloSession.WorldRoot;
            return root != null ? root.rotation : Quaternion.identity;
        }

        /// <summary>Halo yaw (radians, 0 = +X, counter-clockwise about +Z) of a world direction.</summary>
        private static float HaloYaw(Vector3 worldDir)
        {
            Vector3 hd = HaloSpace.DirToHalo(Quaternion.Inverse(WorldRootRotation()) * worldDir);
            return Mathf.Atan2(hd.y, hd.x);
        }

        private void FighterTick(ref HaloRecordedControl c)
        {
            if (!c.Has(HaloRecordedControl.LookDontTurn))
            {
                Vector3 f = HaloCinematic.RecordedDirection(c.FacingYaw, c.FacingPitch);
                if (f.sqrMagnitude > 1e-6f) _recFace = f.normalized;
                Vector3 h = new Vector3(_recFace.x, 0f, _recFace.z);
                if (h.sqrMagnitude > 1e-6f) _recHeading = h.normalized;
            }
            RecordedSpeeds(ref c);
            Vector3 left = Vector3.Cross(_recHeading, Vector3.up);
            _recRot = Quaternion.LookRotation(_recFace, Vector3.up);
            float u = HaloSpace.Unit;
            MoveSwept((_recFace * _recSpeed + left * _recSide) * u, Vector3.zero);
        }

        private void GroundTick(ref HaloRecordedControl c)
        {
            if (!c.Has(HaloRecordedControl.LookDontTurn))
            {
                Vector3 f = HaloCinematic.RecordedDirection(c.FacingYaw, 0);
                f.y = 0f;
                if (f.sqrMagnitude > 1e-6f) _recHeading = f.normalized;
            }
            RecordedSpeeds(ref c);
            Vector3 left = Vector3.Cross(_recHeading, Vector3.up);
            _recRot = Quaternion.LookRotation(_recHeading, Vector3.up);
            Vector3 next = _recPos + (_recHeading * _recSpeed + left * _recSide) * HaloSpace.Unit;
            // follow the floor (ground vehicles stay on the BSP)
            int n = Physics.RaycastNonAlloc(next + Vector3.up * 0.9f, Vector3.down, Hits, 2.5f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; float y = next.y;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null || IsOwn(h.collider)) continue;
                if (h.distance < best) { best = h.distance; y = h.point.y; }
            }
            next.y = y;
            _recPos = next;
        }

        /// <summary>Moves the tick pose by a horizontal (sliding along what it meets) then a vertical displacement (stopping on contact),
        /// both swept with the vehicle's contact spheres against the ground layers.</summary>
        private void MoveSwept(Vector3 horiz, Vector3 vert, bool climb = false)
        {
            _ghostTick = !Collideable();
            if (_ghostTick)
            {
                _recStats.PassThrough++;
                if (!_saidGhost) { _saidGhost = true; HaloLog.Info("Vehicles: " + name + " is not collideable (object_set_collideable false) from tick " + _recStats.Ticks + " of '" + _recStats.Name + "': it flies its recorded track through terrain, as in Halo."); }
            }
            Vector3 p = _recPos;
            Vector3 n;
            float y0 = p.y;
            bool hit = false;
            Vector3 move = horiz;
            // Halo's collision response: the hull stops at the contact, the part of the move into the surface is lost and the rest slides
            // along it (a wall turns it aside, a slope under the hull lifts it); a few passes for corners
            for (int pass = 0; pass < 3; pass++)
            {
                float dist = move.magnitude;
                if (dist <= 1e-5f) break;
                Vector3 dir = move / dist;
                float free = Sweep(p, _recRot, dir, dist, out n);
                if (free >= dist) { p += move; break; }
                p += dir * free;
                if (!hit) { hit = true; Contact(false); LoseVelocityInto(n); }
                move = Vector3.ProjectOnPlane(dir * (dist - free), n);
            }
            if (hit && climb && p.y > y0 + 1e-3f) _recStats.Climbs++;
            float vdist = vert.magnitude;
            if (vdist > 1e-5f)
            {
                Vector3 dir = vert / vdist;
                float free = Sweep(p, _recRot, dir, vdist, out n);
                if (free < vdist) { Contact(dir.y < 0f); p += dir * free; _recVert = 0f; }
                else p += vert;
            }
            _recPos = p;
        }

        /// <summary>Halo's collision response on the hull's velocity: the component into the surface is removed, the rest is kept (sliding
        /// along a wall keeps the speed along it; a head-on wall stops it; the throttle then accelerates it again at the tag's rates).
        /// Our velocity is forward / slide / vertical speed in the hull's level frame (wu per tick).</summary>
        private void LoseVelocityInto(Vector3 n)
        {
            if (Def.Type == HaloVehicleDef.Kind.HumanPlane)
            {
                Vector3 w = _recVel + Vector3.up * _recVert;
                float wn = Vector3.Dot(w, n);
                if (wn >= 0f) return;
                w -= n * wn;
                _recVert = w.y; w.y = 0f; _recVel = w;
                return;
            }
            Vector3 left = Vector3.Cross(_recHeading, Vector3.up);
            Vector3 v = _recHeading * _recSpeed + left * _recSide + Vector3.up * _recVert;
            float vn = Vector3.Dot(v, n);
            if (vn >= 0f) return;
            v -= n * vn;
            _recSpeed = Vector3.Dot(v, _recHeading);
            _recSide = Vector3.Dot(v, left);
            _recVert = v.y;
        }

        private void Contact(bool landing)
        {
            if (_recStats.FirstContactTick < 0) _recStats.FirstContactTick = _recStats.Ticks;
            if (landing) _recStats.Landings++; else _recStats.TerrainContacts++;
        }

        /// <summary>How far (metres, up to <paramref name="dist"/>) the vehicle's swept shapes move along <paramref name="dir"/> from pose
        /// (<paramref name="pos"/>, <paramref name="rot"/>) before touching the ground layers; <paramref name="normal"/> = the nearest hit's.</summary>
        private float Sweep(Vector3 pos, Quaternion rot, Vector3 dir, float dist, out Vector3 normal)
        {
            normal = -dir;
            if (_ghostTick) return dist;
            if (_sweepDirty) BuildSweepShapes();
            if (_sweepCount == 0) return dist;
            // broad phase: the bounding sphere around the whole move touches nothing -> free
            Vector3 c = pos + rot * _sweepCenter;
            if (!Physics.CheckSphere(c + dir * (dist * 0.5f), _sweepBound + dist * 0.5f + Skin, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) return dist;
            float free = dist;
            for (int i = 0; i < _sweepCount; i++)
            {
                Vector3 o = pos + rot * _sweepLocal[i];
                float r = _sweepRadius[i] * 0.95f;  // ours: a hair inside the sphere so resting contact is not a hit
                int n = Physics.SphereCastNonAlloc(o, r, dir, SweepHits, free + Skin, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore);
                for (int k = 0; k < n; k++)
                {
                    var h = SweepHits[k];
                    if (h.collider == null || IsOwn(h.collider)) continue;
                    if (h.distance <= 0f && h.point == Vector3.zero) continue;   // started inside it: let the hull move out
                    float f = Mathf.Max(0f, h.distance - Skin);
                    if (f < free) { free = f; normal = h.normal; }
                }
            }
            return free;
        }

        /// <summary>The contact spheres in the root frame (Halo's mass points: a dropship's 11, a pelican's 19), and those of carried vehicles
        /// (spheres and wheels) so cargo hanging under the hull stops on the ground too. Rebuilt only when cargo changes.</summary>
        private void BuildSweepShapes()
        {
            _sweepDirty = false;
            int need = Shapes.Count;
            for (int i = 0; i < Seats.Count; i++) if (Seats[i].Cargo != null) need += Seats[i].Cargo.Shapes.Count + Seats[i].Cargo.Wheels.Count;
            if (_sweepLocal.Length < need) { _sweepLocal = new Vector3[need]; _sweepRadius = new float[need]; }
            _sweepCount = 0;
            Quaternion inv = Quaternion.Inverse(transform.rotation);
            Vector3 root = transform.position;
            for (int i = 0; i < Shapes.Count; i++) AddSweepShape(Shapes[i], root, inv);
            for (int i = 0; i < Seats.Count; i++)
            {
                var cargo = Seats[i].Cargo;
                if (cargo == null) continue;
                for (int k = 0; k < cargo.Shapes.Count; k++) AddSweepShape(cargo.Shapes[k], root, inv);
                for (int k = 0; k < cargo.Wheels.Count; k++)
                {
                    var w = cargo.Wheels[k];
                    _sweepLocal[_sweepCount] = inv * (cargo.transform.TransformPoint(w.Local) - root);
                    _sweepRadius[_sweepCount] = Mathf.Max(0.05f, w.Radius);
                    _sweepCount++;
                }
            }
            if (_sweepCount == 0) return;
            Vector3 mid = Vector3.zero;
            for (int i = 0; i < _sweepCount; i++) mid += _sweepLocal[i];
            mid /= _sweepCount;
            float bound = 0f;
            for (int i = 0; i < _sweepCount; i++) bound = Mathf.Max(bound, (_sweepLocal[i] - mid).magnitude + _sweepRadius[i]);
            _sweepCenter = mid; _sweepBound = bound;
        }

        private void AddSweepShape(Collider c, Vector3 root, Quaternion inv)
        {
            if (c == null) return;
            var s = c as SphereCollider;
            Vector3 world; float r;
            if (s != null) { world = s.transform.TransformPoint(s.center); r = s.radius * s.transform.lossyScale.x; }
            else { Bounds b = c.bounds; world = b.center; r = Mathf.Min(b.extents.x, Mathf.Min(b.extents.y, b.extents.z)); }  // (a box hull: its inscribed sphere)
            _sweepLocal[_sweepCount] = inv * (world - root);
            _sweepRadius[_sweepCount] = Mathf.Max(0.05f, r);
            _sweepCount++;
        }

        /// <summary>Per frame (LateUpdate): the recording-driven pose between the last two ticks.</summary>
        private void RecordedFrame()
        {
            if (!_rec) return;
            float f = HaloCinematic.TickFraction;
            transform.SetPositionAndRotation(Vector3.Lerp(_recPrev, _recPos, f), Quaternion.Slerp(_recPrevRot, _recRot, f));
        }

        /// <summary>Seats a unit (vehicle_load_magic / unit_enter_vehicle / AI boarding): the Characters module's EnterSeat on the seat marker.
        /// False when the seat is taken.</summary>
        public bool SeatUnit(int seat, HaloUnit u)
        {
            var s = SeatOf(seat);
            if (s == null || u == null || s.Marker == null) return false;
            if (s.Occupant >= 0 && s.Unit != u) return false;
            u.EnterSeat(s.Marker, s.Label);
            s.Occupant = UnitOccupant; s.Unit = u;
            LastUsed = Time.time;
            return true;
        }

        /// <summary>Gets a unit out at the seat's exit point (Halo's enter marker / the exit clip's end).</summary>
        public void UnseatUnit(HaloUnit u)
        {
            int seat = SeatOfUnit(u);
            if (seat < 0) return;
            Vector3 at; float yaw;
            HaloVehicleSeat.StandPoint(this, Seats[seat], out at, out yaw);
            u.ExitSeat(at, yaw);
            Seats[seat].Occupant = -1; Seats[seat].Unit = null;
        }

        /// <summary>Marks the seats units sit in as taken (every frame, after the net reset of the occupants).</summary>
        internal void NoteUnits()
        {
            for (int i = 0; i < Seats.Count; i++)
            {
                var s = Seats[i];
                s.Unit = null;
                if (s.Cargo == null) s.Cargo = null;                 // (a destroyed cargo: Unity's fake null -> a real one)
                else if (s.Cargo.CarriedBy != this) s.Cargo = null;  // (taken by another carrier)
                if (s.Cargo != null) { s.Occupant = CargoOccupant; continue; }
                if (s.Occupant >= 0 || s.Marker == null || s.Marker.childCount == 0) continue;
                var u = s.Marker.GetComponentInChildren<HaloUnit>();
                if (u != null && u.Alive) { s.Occupant = UnitOccupant; s.Unit = u; }
            }
        }

        // ------------------------------------------------------------------ the AI's driving surface

        private float _aiAt = -10f, _aiThrottle, _aiTurn;
        private Vector3 _aiAim;
        private bool _aiFire, _aiFire2, _aiDriving, _saidAi;

        /// <summary>An AI (or script) drives / fires this tick.</summary>
        public bool AiDriven => _aiDriving;

        /// <summary>
        /// The AI module's control of this vehicle for this tick (call every AI tick; the input lapses 0.5 s after the last call):
        /// throttle -1..1 (back / forward), turn -1..1 (left / right; <see cref="SteerToward"/> gives it for a point), aimPoint = where the gun
        /// should point (world; zero = straight ahead), fire / fire2 = the vehicle weapon's triggers. The driver seat must hold a HaloUnit for
        /// the throttle / turn to drive; a unit in the gunner seat (or a driver of a ghost / banshee / scorpion) fires. Runs on the host (the
        /// AI's client): the vehicle is simulated there and published like a player's.
        /// </summary>
        public void SetAiInput(float throttle, float turn, Vector3 aimPoint, bool fire, bool fire2 = false)
        {
            _aiAt = Time.time;
            _aiThrottle = Mathf.Clamp(throttle, -1f, 1f); _aiTurn = Mathf.Clamp(turn, -1f, 1f);
            _aiAim = aimPoint; _aiFire = fire; _aiFire2 = fire2;
        }

        /// <summary>The turn (-1 left .. 1 right) that heads the vehicle at a world point (full lock beyond the vehicle's maximum turn).</summary>
        public float SteerToward(Vector3 point)
        {
            Vector3 to = point - transform.position;
            float a = SignedYaw(transform.forward, to);
            float full = Def.Type == HaloVehicleDef.Kind.HumanJeep ? Mathf.Max(5f, Def.MaxLeftTurn) : 30f;
            return Mathf.Clamp(a / full, -1f, 1f);
        }

        /// <summary>The vehicle's own frame for the AI each frame (the director calls it); the local player's driving takes precedence.</summary>
        private Vector3 _aiFlyTo;
        private bool _aiFlight;

        /// <summary>
        /// 3D flight for an AI pilot (banshee; any flier): throttle -1..1 along the nose, <paramref name="flyToward"/> = the world point the nose
        /// turns to (climbs and dives included: the banshee pitches up to 60 deg toward it at the seat's yaw rate, its own flight numbers -
        /// top speed 0.22 wu/tick = 20 m/s, acceleration 0.008 wu/tick^2 - from the vehicle tag), aimPoint / fire / fire2 as SetAiInput (the
        /// banshee's guns are fixed: they fire along the nose). Same lapse rule (0.5 s).
        /// </summary>
        public void SetAiFlight(float throttle, Vector3 flyToward, Vector3 aimPoint, bool fire, bool fire2 = false)
        {
            SetAiInput(throttle, 0f, aimPoint, fire, fire2);
            _aiFlyTo = flyToward; _aiFlight = true;
        }

        internal void AiStep(float dt)
        {
            bool fresh = Time.time - _aiAt < 0.5f;
            if (_rec || HaloCinematic.IsRecording(gameObject)) { _aiDriving = false; return; }      // a recording flies it (Halo: the recording replaces the controls)
            if (CarriedBy != null) { _aiDriving = false; return; }                                // cargo: carried, not driven
            if (!fresh) _aiFlight = false;
            if (LocalSeated && HaloVehicleSeat.Local.Driver && HaloVehicleSeat.Local.Vehicle == this) { _aiDriving = false; return; }
            var drv = DriverSeat;
            bool unitDrives = drv != null && drv.Unit != null;
            if (!fresh)
            {
                if (_aiDriving)
                {
                    _aiDriving = false;
                    Controls = default(HaloVehicleControls);
                    if (!unitDrives && !LocalSeated) Park(true);
                }
                return;
            }
            if (!_aiDriving && !_saidAi) { _saidAi = true; HaloLog.Info("Vehicles: " + name + " is driven / manned by AI (" + (unitDrives ? drv.Unit.name + " drives" : "no driver") + ")."); }
            _aiDriving = true;
            if (unitDrives && !RemoteDriven && Def.Type != HaloVehicleDef.Kind.Turret)
            {
                if (!LocalDriven) TakeControl();
                if (_aiFlight && (_aiFlyTo - transform.position).sqrMagnitude > 1f)
                    Controls = new HaloVehicleControls { Throttle = _aiThrottle, Aim = (_aiFlyTo - transform.position).normalized, SteerToAim = true };
                else Controls = new HaloVehicleControls { Throttle = _aiThrottle, Turn = _aiTurn, Aim = transform.forward };
            }
            if (Gun == null) return;
            for (int i = 0; i < Seats.Count; i++)
            {
                var s = Seats[i];
                if (!s.Guns || s.Unit == null) continue;
                if (HaloVehicleInput.FixedGuns(this)) { AimYaw = 0f; AimPitch = 0f; }
                else if (_aiAim.sqrMagnitude > 0.01f) { Vector3 d = _aiAim - GunPivot.position; if (d.sqrMagnitude > 0.25f) AimToward(d, s, dt); }
                Gun.Tick(_aiFire, _aiFire2, dt, s.Label, -2);
                break;
            }
        }

        // ------------------------------------------------------------------ script switches

        /// <summary>vehicle_hover: true = held still where it is (kinematic, no gravity); false = back to its physics.</summary>
        public void Hover(bool on)
        {
            if (Hovering == on) return;
            Hovering = on;
            if (Body == null) return;
            if (on) { if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; } Body.isKinematic = true; }
            else if (LocalDriven) Body.isKinematic = false;
            else Park(true);
        }

        // ------------------------------------------------------------------ unit_open / unit_close (cryotube lid, pelican / dropship doors)

        /// <summary>The unit seat animation slots of Halo's "stand" block for a unit's own model: 27 opening, 28 closing, 29 hovering
        /// (the a10 cryotube's "stand opening" 154 frames; the dropship's "stand opening" / "stand closing" 14 frames, "stand hovering").</summary>
        private const int AnimOpening = 27, AnimClosing = 28;

        private HaloAnimGraph _ownGraph;
        private bool _ownGraphRead;
        private HaloAnimation _doorClip;
        private bool _doorReverse, _doorPlaying;
        private float _doorT;
        private Transform[] _doorNodes;
        private Vector3[] _doorRestPos;
        private Quaternion[] _doorRestRot;
        private static bool _saidDoors;

        /// <summary>Opened by unit_open (false before / after unit_close). A unit with an opening animation lets nobody out while closed.</summary>
        public bool IsOpen { get; private set; }
        /// <summary>The vehicle's own graph has an opening animation (cryotube, dropship): its seats can only be left while open.</summary>
        public bool HasHatch => OwnSeatAnim(AnimOpening) != null;

        private HaloAnimation OwnSeatAnim(int id)
        {
            if (!_ownGraphRead)
            {
                _ownGraphRead = true;
                try { if (Def.Animations != null) _ownGraph = new HaloAnimGraph(Def.Animations); }
                catch (Exception e) { HaloLog.Warn("Vehicles: animation graph of " + Def.Name + ": " + e.Message); }
            }
            if (_ownGraph == null) return null;
            var stand = _ownGraph.FindSeat("stand");
            var a = stand != null ? _ownGraph.Get(stand.Anim(id)) : null;
            return a != null && a.Decode() ? a : null;
        }

        /// <summary>
        /// unit_open / unit_close: Halo's unit opening / closing animation of the vehicle's own graph ("stand opening", "stand closing"; a
        /// unit with only an opening plays it backwards to close), on the model's nodes over the clip's own length (30 frames a second), held
        /// on its last frame. Pelicans have no such animation in their graph (nothing to play: logged once).
        /// </summary>
        public void Open(bool open)
        {
            if (Model == null) return;
            HaloAnimation clip = open ? OwnSeatAnim(AnimOpening) : (OwnSeatAnim(AnimClosing) ?? OwnSeatAnim(AnimOpening));
            bool reverse = !open && OwnSeatAnim(AnimClosing) == null;
            IsOpen = open;
            if (clip == null)
            {
                if (!_saidDoors) { _saidDoors = true; HaloLog.Info("Vehicles: unit_" + (open ? "open" : "close") + " on " + Def.Name + ": its animation graph has no opening / closing animation - nothing to move."); }
                return;
            }
            if (_doorNodes == null || _doorClip == null || _doorClip.NodeCount != clip.NodeCount) BindDoor(clip);
            _doorClip = clip; _doorReverse = reverse; _doorT = 0f; _doorPlaying = true;
            HaloLog.Info("Vehicles: " + name + " " + (open ? "opens" : "closes") + ": '" + clip.Name + "'" + (reverse ? " backwards" : "") + " over " + clip.Duration.ToString("0.00") + " s.");
        }

        private void BindDoor(HaloAnimation clip)
        {
            int n = _ownGraph.NodeNames.Length;
            _doorNodes = new Transform[n]; _doorRestPos = new Vector3[n]; _doorRestRot = new Quaternion[n];
            for (int k = 0; k < n; k++)
            {
                _doorNodes[k] = Model.Node(_ownGraph.NodeNames[k]);
                if (_doorNodes[k] != null) { _doorRestPos[k] = _doorNodes[k].localPosition; _doorRestRot[k] = _doorNodes[k].localRotation; }
            }
        }

        /// <summary>Every frame (LateUpdate, after the node animation): the opening / closing clip on the nodes it animates.</summary>
        private void StepDoor(float dt)
        {
            if (_doorClip == null || _doorNodes == null) return;
            if (_doorPlaying) { _doorT += dt; if (_doorT >= _doorClip.Duration) { _doorT = _doorClip.Duration; _doorPlaying = false; } }
            var a = _doorClip;
            float frame = Mathf.Clamp(_doorT / HaloAnimation.FrameTime, 0f, a.Frames - 1);
            if (_doorReverse) frame = a.Frames - 1 - frame;
            int f0 = Mathf.Clamp(Mathf.FloorToInt(frame), 0, a.Frames - 1), f1 = Mathf.Min(a.Frames - 1, f0 + 1);
            float u = frame - f0;
            int n = a.NodeCount;
            float s = HaloSpace.Unit;
            for (int k = 0; k < n && k < _doorNodes.Length; k++)
            {
                var t = _doorNodes[k];
                if (t == null) continue;
                bool all = a.Type == HaloAnimation.Kind.Base;
                if (all || a.Rotates(k))
                {
                    int i0 = (f0 * n + k) * 4, i1 = (f1 * n + k) * 4;
                    var q0 = new Quaternion(a.Rot[i0], a.Rot[i0 + 1], a.Rot[i0 + 2], a.Rot[i0 + 3]);
                    var q1 = new Quaternion(a.Rot[i1], a.Rot[i1 + 1], a.Rot[i1 + 2], a.Rot[i1 + 3]);
                    Quaternion q = Quaternion.Slerp(q0, q1, u);
                    t.localRotation = HaloSpace.Rot(q.x, q.y, q.z, q.w);      // decoded rotations are standard (not Halo's inverse): no conjugate
                }
                if (all || a.Translates(k))
                {
                    int i0 = (f0 * n + k) * 3, i1 = (f1 * n + k) * 3;
                    float x = Mathf.Lerp(a.Trans[i0], a.Trans[i1], u), y = Mathf.Lerp(a.Trans[i0 + 1], a.Trans[i1 + 1], u), z = Mathf.Lerp(a.Trans[i0 + 2], a.Trans[i1 + 2], u);
                    t.localPosition = new Vector3(x * s, z * s, y * s);
                }
            }
        }

        // ------------------------------------------------------------------ campaign vitality

        /// <summary>Campaign damage: the collision model's body vitality (points); true when it applied (the MP rule is then skipped).</summary>
        private bool CampaignDamage(HaloHit hit)
        {
            var c = Def.Collision;
            if (c == null || c.MaxBody <= 0f || Destroyed) return false;
            float amount = hit.Instant ? c.MaxBody : Mathf.Max(0f, hit.Amount);
            Vitality = Mathf.Max(0f, Vitality - amount / c.MaxBody);
            if (Body != null && LocalDriven && hit.Explosion && hit.Direction.sqrMagnitude > 0f)
                Body.AddForceAtPosition(hit.Direction.normalized * Mathf.Min(hit.Amount, 200f) * 40f, hit.Point, ForceMode.Impulse); // ours
            if (LocalSeated && !hit.Instant && hit.SourcePlayer != HaloLocal.NetId && Def.RiderDamageFraction > 0f)
            {
                HaloHit pass = hit; pass.Amount = hit.Amount * Def.RiderDamageFraction;
                HaloGame.Guard("vehicle rider damage", () => HaloCombat.TakeHit(pass));
            }
            if (Vitality <= 0f) Destroy(hit);
            return true;
        }

        /// <summary>Destroyed (campaign): the collision model's destroyed / depleted effect, everybody out, a dead wreck (no seats, no driving).</summary>
        private void Destroy(HaloHit hit)
        {
            Destroyed = true;
            var c = Def.Collision;
            HaloTag fx = c.BodyDestroyedEffect ?? c.BodyDepletedEffect;
            if (fx == null) foreach (var r in c.Regions) if (r.DestroyedEffect != null) { fx = r.DestroyedEffect; break; }
            if (fx == null) fx = Def.Effect;
            Vector3 at = ShapeBounds().center;
            if (fx != null) HaloFx.Play(fx, at, transform.rotation, Model != null ? Model.Root.transform : transform);
            HaloLog.Info("Vehicles: " + name + (ObjectName.Length > 0 ? " '" + ObjectName + "'" : "") + " destroyed (" + c.MaxBody.ToString("0") + " body points" + (fx != null ? ", effect " + fx.Name : ", no effect in its tags") + ").");
            if (HaloVehicleSeat.Local.Vehicle == this) HaloVehicleSeat.Local.GetOut("vehicle destroyed");
            for (int i = 0; i < Seats.Count; i++) if (Seats[i].Unit != null) UnseatUnit(Seats[i].Unit);
            for (int i = 0; i < Seats.Count; i++) if (Seats[i].Cargo != null) UnseatVehicle(Seats[i].Cargo);
            if (CarriedBy != null) CarriedBy.UnseatVehicle(this);
            _aiDriving = false;
            Controls = default(HaloVehicleControls);
            EnterableByPlayer = false;
            if (Body != null && !Body.isKinematic) Body.AddForce(Vector3.up * Body.mass * 4f, ForceMode.Impulse); // ours: the blast lifts the wreck
            var h = VehicleDestroyed;
            if (h != null) HaloGame.Guard("vehicle destroyed", () => h(this));
        }
    }
}

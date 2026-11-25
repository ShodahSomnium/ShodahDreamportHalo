using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// One vehicle of the match (a scenario vehicle placement): the gbxmodel built from the map (HaloModelBuilder), a Rigidbody with
    /// Halo's own contact shapes - one sphere per mass point of the physics tag (Halo vehicles touch the world with those spheres) -
    /// the collision model's BSP as a hit mesh per node (bullets hit what Halo hits; it touches nothing), the seats from the unit tag,
    /// node animation (wheels, steering wheel, suspension, turret aim, spinning barrels) and the driving (HaloVehicle.Drive.cs).
    ///
    /// Frames: the root GameObject is in Unity convention (z forward, y up); the model hangs under it turned -90 degrees about y so
    /// Halo's model axes (x forward, y left, z up) land on it: a Halo model point (x, y, z) is (-y, z, x) * 3.048 m in the root's frame
    /// (<see cref="ModelToRoot"/>). Halo node rotations by an angle about a Halo axis are AngleAxis(-angle, mapped axis) in the node's
    /// Unity frame (the axis swap is a reflection).
    ///
    /// Authority: the local driver simulates (dynamic body); a vehicle driven elsewhere is kinematic and placed from the net
    /// (HaloVehicleNet); an empty vehicle is kinematic (parked, solid for walkers - ETS lesson: a kinematic avatar capsule shoves a
    /// dynamic body) after a short settle, owned by the instance master. While anyone local sits in it the vehicle's shapes ignore the
    /// avatar layers 2 | 7 | 24 (ETS: SetCollisionDisableState does not stop the avatar's capsule pushing the vehicle).
    /// </summary>
    public sealed partial class HaloVehicle : MonoBehaviour, IHaloDamageable, IHaloRecordedControl
    {
        public const float Tick = 30f;                         // Halo's simulation rate (ticks per second): tag speeds are per tick
        public static float PerTick(float wuPerTick) { return wuPerTick * Tick * HaloSpace.Unit; }        // -> m/s
        public static float PerTick2(float wuPerTick2) { return wuPerTick2 * Tick * Tick * HaloSpace.Unit; } // -> m/s^2

        /// <summary>The avatar's capsule (2), the VR hands' physical boxes (7) and the player layer (24): what a vehicle someone sits in must not touch (ETS SeatedBodyExcludes).</summary>
        public const int AvatarLayers = (1 << 2) | (1 << 7) | (1 << HaloLayers.Hitbox);

        /// <summary>
        /// The contact spheres' layer: 31 (the client's IkContact) - its matrix row collides with everything (world 0 / 14 / 23, the avatar 2,
        /// other vehicles' spheres) but it is not in HaloLayers.ShotMask, so bullets pass Halo's big mass-point spheres (0.9 m on the
        /// warthog hull) and hit the collision model's hit meshes (layer 28) instead. Decoded from ProjectSettings/DynamicsManager.asset.
        /// </summary>
        public const int ShapeLayer = 31;

        public sealed class SeatInfo
        {
            public HaloVehicleDef.Seat Def;
            public Transform Marker, Enter, Camera, LeftFoot, RightFoot, LeftGrip, RightGrip;
            /// <summary>Standing seat (the warthog gunner behind the chain gun): the avatar stands on the foot markers instead of sitting.</summary>
            public bool Standing;
            /// <summary>This seat fires the vehicle's weapon (driver of a ghost / banshee / scorpion, gunner of a warthog / turret).</summary>
            public bool Guns;
            /// <summary>Who sits here: local = HaloLocal.NetId (or 0 offline), remote = their slot; -1 = empty.</summary>
            public int Occupant = -1;
            /// <summary>The AI / scripted HaloUnit in this seat (Occupant = UnitOccupant), null otherwise.</summary>
            public HaloUnit Unit;
            /// <summary>The vehicle carried on this seat (pelican "cargo", c_dropship "cargo_ghost01..03" / "cargo_wraith"; Occupant =
            /// CargoOccupant), null otherwise.</summary>
            public HaloVehicle Cargo;
            public string Label => Def.Label;
        }

        public sealed class Wheel
        {
            public HaloVehicleDef.MassPoint Point;
            public Vector3 Local;              // mass point centre in the root frame
            public float Radius, MaxDist, MinDist; // metres: ray length at full extension / at full compression (centre to ground)
            public float Spring, Damper;
            public bool Front, Powered, Steers, Left;
            public float GripSide, GripForward;
            public Transform Suspension, Hub, Tire; // nodes moved for the look (any may be null)
            public Vector3 SuspensionRest; public Quaternion HubRest, TireRest;
            public float Spin;                  // degrees
            public bool Grounded; public float Distance; public Vector3 Normal; public Collider Ground;
        }

        public sealed class HoverPoint
        {
            public HaloVehicleDef.MassPoint Point;
            public HaloVehicleDef.PoweredPoint Powered;
            public Vector3 Local;
            public float Height, Strength, Mass;
        }

        public int Id { get; private set; }
        public HaloVehicleDef Def { get; private set; }
        public HaloScenario.Placement Placement { get; private set; }
        public HaloModelBuilder.Built Model { get; private set; }
        public Rigidbody Body { get; private set; }
        public readonly List<SeatInfo> Seats = new List<SeatInfo>();
        public readonly List<Wheel> Wheels = new List<Wheel>();
        public readonly List<HoverPoint> Hovers = new List<HoverPoint>();
        public readonly List<Collider> Shapes = new List<Collider>();
        public readonly List<Collider> HitMeshes = new List<Collider>();
        public readonly List<Transform> Muzzles = new List<Transform>();
        public HaloVehicleGun Gun { get; private set; }
        public Transform ModelRoot { get; private set; }
        public Vector3 SpawnPosition { get; private set; }
        public Quaternion SpawnRotation { get; private set; }
        public int Team => Placement != null ? Placement.Team : -1;
        public string Label { get; private set; }

        /// <summary>Simulated here (local driver): the body is dynamic and this client publishes the pose.</summary>
        public bool LocalDriven { get; private set; }
        /// <summary>Driven by a remote player: placed from the net every frame.</summary>
        public bool RemoteDriven { get; private set; }
        /// <summary>Somebody local sits in it (driver or rider).</summary>
        public bool LocalSeated { get; private set; }
        /// <summary>Time.time the vehicle was last occupied or moved by a driver (respawn timer).</summary>
        public float LastUsed { get; set; }

        public float SpeedMs => Velocity.magnitude;
        /// <summary>Simulated here: the body's velocity; placed from the net: the reported one; moved by a recording / a co-op follower
        /// record without one: measured from the transform's motion (engine pitch, riders' lurch).</summary>
        public Vector3 Velocity => Body != null && LocalDriven && !Body.isKinematic ? Body.linearVelocity : (_netVelocity.sqrMagnitude > 0.01f ? _netVelocity : _measuredVel);
        private Vector3 _measuredVel, _lastPos; private bool _posKnown;

        // node animation
        private Transform _steeringWheel, _gunYawNode, _gunPitchNode, _barrels;
        private Quaternion _steeringRest, _gunYawRest, _gunPitchRest, _barrelsRest;
        private float _barrelSpin, _barrelSpinRate;
        private Vector3 _netVelocity;
        private float _settleUntil;
        private bool _excludingAvatars;
        private bool _saidNoGround;
        private bool _flipping; private float _flipT; private Quaternion _flipFrom, _flipTo; private Vector3 _flipPos;

        /// <summary>Gun aim in the vehicle's frame (radians; yaw left positive, pitch up positive) - drawn and fired along.</summary>
        public float AimYaw, AimPitch;

        public static HaloVehicle Create(int id, HaloScenario.Placement placement, HaloVehicleDef def, Transform parent)
        {
            var go = new GameObject("Halo Vehicle " + id + " " + def.Name);
            go.transform.SetParent(parent, false);
            go.layer = HaloLayers.Vehicle;
            Vector3 pos = HaloSpace.Pos(placement.Position);
            Quaternion rot = HaloSpace.Euler(placement.Rotation.X, placement.Rotation.Y, placement.Rotation.Z);
            go.transform.SetPositionAndRotation(pos, rot);
            var v = go.AddComponent<HaloVehicle>();
            v.Id = id; v.Def = def; v.Placement = placement; v.SpawnPosition = pos; v.SpawnRotation = rot; v.LastUsed = Time.time;
            v.Build();
            return v;
        }

        /// <summary>A Halo model-space point (world units) in the root's frame (metres).</summary>
        public static Vector3 ModelToRoot(HVec3 h) { return new Vector3(-h.Y, h.Z, h.X) * HaloSpace.Unit; }

        private void Build()
        {
            Label = LabelOf(Def);
            var modelHolder = new GameObject("model");
            modelHolder.transform.SetParent(transform, false);
            modelHolder.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            ModelRoot = modelHolder.transform;
            var opts = new HaloModelBuilder.Options { Layer = HaloLayers.Vehicle };
            if (Placement != null && Placement.DesiredPermutation > 0 && Def.Model != null)
            {
                // the placement's desired permutation (campaign: damaged / variant looks): that permutation in every region that has it
                try
                {
                    var hm = new HaloModel(Def.Model);
                    opts.Permutations = new Dictionary<string, string>();
                    foreach (var reg in hm.Regions)
                        if (Placement.DesiredPermutation < reg.Permutations.Count) opts.Permutations[reg.Name] = reg.Permutations[Placement.DesiredPermutation].Name;
                }
                catch (Exception e) { HaloLog.Warn("Vehicles: permutation " + Placement.DesiredPermutation + " of " + Def.Tag + ": " + e.Message); }
            }
            Model = HaloModelBuilder.Build(Def.Model, ModelRoot, opts);
            if (Model == null) HaloLog.Warn("Vehicles: the model of " + Def.Tag + " did not build; the vehicle drives invisible.");

            Body = gameObject.AddComponent<Rigidbody>();
            var phys = Def.Physics;
            Body.mass = phys != null && phys.Mass > 0f ? phys.Mass : 2000f;
            Body.interpolation = RigidbodyInterpolation.Interpolate;
            Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Body.linearDamping = phys != null ? Mathf.Clamp(phys.AirFriction * Tick, 0f, 1f) : 0.1f;
            Body.angularDamping = 0.5f; // ours
            Body.maxDepenetrationVelocity = 3f; // ours: a shove out of a wall, never a launch
            BuildShapes();
            if (phys != null)
            {
                Body.automaticCenterOfMass = false;
                Body.centerOfMass = ModelToRoot(phys.CenterOfMass);
                if (phys.XX > 0f && phys.YY > 0f && phys.ZZ > 0f)
                {
                    // Halo moments (mass * wu^2) about its x (forward), y (left), z (up) -> the root's x (right), y (up), z (forward)
                    float s = HaloSpace.Unit * HaloSpace.Unit * Mathf.Max(0.05f, phys.MomentScale);
                    Body.automaticInertiaTensor = false;
                    Body.inertiaTensor = new Vector3(phys.YY * s, phys.ZZ * s, phys.XX * s);
                    Body.inertiaTensorRotation = Quaternion.identity;
                }
            }
            BuildHitMeshes();
            BuildSeats();
            BuildWheels();
            BuildNodes();
            if (Def.Weapons.Count > 0 && Def.Weapons[0] != null) Gun = new HaloVehicleGun(this, Def.Weapons[0]);
            Park(true);
        }

        public static string LabelOf(HaloVehicleDef def)
        {
            string n = def.Tag.Name.ToLowerInvariant();
            if (n.Contains("rwarthog")) return "Rocket Warthog";
            if (n.Contains("warthog")) return "Warthog";
            if (n.Contains("scorpion")) return "Scorpion";
            if (n.Contains("ghost")) return "Ghost";
            if (n.Contains("banshee")) return "Banshee";
            if (n.Contains("turret")) return "Gun Turret";
            return def.Name;
        }

        // ------------------------------------------------------------------ building

        private void BuildShapes()
        {
            var phys = Def.Physics;
            if (phys == null || phys.Points.Count == 0)
            {
                var box = gameObject.AddComponent<BoxCollider>();
                box.size = new Vector3(2f, 1.5f, 4f); box.center = new Vector3(0f, 1f, 0f);
                Shapes.Add(box);
                return;
            }
            // Every mass point that is not a wheel (suspension) is a solid sphere: Halo's contact model for vehicles.
            var holder = new GameObject("shapes");
            holder.transform.SetParent(transform, false);
            holder.layer = ShapeLayer;
            foreach (var p in phys.Points)
            {
                if (p.HasSuspension) continue;
                bool hoverPoint = p.Powered >= 0 && p.Powered < phys.Powered.Count && phys.Powered[p.Powered].Antigrav;
                var s = holder.AddComponent<SphereCollider>();
                s.center = ModelToRoot(p.Position);
                s.radius = Mathf.Max(0.05f, p.Radius * HaloSpace.Unit * (hoverPoint ? 0.8f : 1f));
                Shapes.Add(s);
            }
            // The physical material: Halo's ground friction (0.2 - 0.5) as a low-friction contact so hulls slide off walls.
            var mat = new PhysicsMaterial("halo vehicle") { dynamicFriction = phys.GroundFriction, staticFriction = phys.GroundFriction, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
            // hit boxes (24: players', units' - also the riders' own, parented under the seat markers) never push the hull: a kinematic hit box
            // inside the vehicle would pin it (lab: an AI-driven warthog crawled at 0.4 m/s with two marines aboard); run-overs are an overlap test
            foreach (var c in Shapes) { c.sharedMaterial = mat; c.excludeLayers = c.excludeLayers.value | (1 << HaloLayers.Hitbox); }
        }

        /// <summary>The collision model's BSP polygons, per node, as non-convex hit meshes that touch nothing (raycasts only).</summary>
        private void BuildHitMeshes()
        {
            var tag = Def.CollisionTag;
            if (tag == null || Model == null) return;
            try
            {
                var r = tag.Root;
                int ni = 0;
                foreach (var node in r.Block(652, 64))
                {
                    int nodeIndex = ni++;
                    var verts = new List<Vector3>(); var tris = new List<int>();
                    foreach (var bsp in node.Block(52, 96))
                    {
                        int baseV = verts.Count;
                        var vlist = new List<HVec3>();
                        foreach (var v in bsp.Block(84, 16)) vlist.Add(v.Vec3(0));
                        int edgeCount = bsp.Count(72);
                        if (edgeCount <= 0 || vlist.Count == 0) continue;
                        var edgeAt = bsp.Element(72, 0, 24);
                        foreach (var hv in vlist) verts.Add(new Vector3(hv.X, hv.Z, hv.Y) * HaloSpace.Unit);
                        int surfaces = bsp.Count(60);
                        var surfAt = bsp.Element(60, 0, 12);
                        var poly = new List<int>(16);
                        for (int si = 0; si < surfaces; si++)
                        {
                            poly.Clear();
                            uint first = surfAt.U32(si * 12 + 4);
                            uint e = first;
                            for (int guard = 0; guard < 64; guard++)
                            {
                                if (e >= edgeCount) break;
                                int eo = (int)e * 24;
                                uint left = edgeAt.U32(eo + 16), right = edgeAt.U32(eo + 20);
                                bool leftSide = left == (uint)si;
                                uint vtx = leftSide ? edgeAt.U32(eo) : edgeAt.U32(eo + 4);
                                poly.Add(baseV + (int)vtx);
                                e = leftSide ? edgeAt.U32(eo + 8) : edgeAt.U32(eo + 12);
                                if (e == first) break;
                            }
                            for (int k = 1; k + 1 < poly.Count; k++)
                            {
                                // both windings: the reflection to Unity space and the edge order are not worth a guess for a hit mesh
                                tris.Add(poly[0]); tris.Add(poly[k + 1]); tris.Add(poly[k]);
                                tris.Add(poly[0]); tris.Add(poly[k]); tris.Add(poly[k + 1]);
                            }
                        }
                    }
                    if (tris.Count == 0) continue;
                    string nodeName = node.Str32(0);
                    Transform at = Model.Node(nodeName);
                    if (at == null) at = nodeIndex < Model.Nodes.Length ? Model.Nodes[nodeIndex] : Model.Root.transform;
                    var go = new GameObject("hit " + nodeName);
                    go.layer = HaloLayers.Vehicle;
                    go.transform.SetParent(at, false);
                    // the coll BSP is in the node's own space (the node at rest): undo the node's world transform at build time
                    // is not needed - coll vertices are node-relative like the render vertices are model-relative per node rest.
                    var mesh = new Mesh { name = "hit " + nodeName };
                    mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
                    var kin = go.AddComponent<Rigidbody>(); kin.isKinematic = true; kin.interpolation = RigidbodyInterpolation.None;
                    var mc = go.AddComponent<MeshCollider>();
                    mc.convex = false; mc.sharedMesh = mesh;
                    mc.excludeLayers = ~0; mc.providesContacts = false;
                    HitMeshes.Add(mc);
                }
                foreach (var h in HitMeshes) foreach (var s in Shapes) Physics.IgnoreCollision(h, s, true);
            }
            catch (Exception e)
            {
                HaloLog.Warn("Vehicles: hit mesh of " + tag + " failed: " + e.Message);
            }
        }

        private void BuildSeats()
        {
            foreach (var s in Def.Seats)
            {
                var info = new SeatInfo { Def = s };
                if (Model != null)
                {
                    info.Marker = Model.Marker(s.Marker);
                    info.Enter = Model.Marker(s.EnterMarker);
                    if (!string.IsNullOrEmpty(s.CameraMarker)) info.Camera = Model.Marker(s.CameraMarker);
                    if (info.Camera == null) info.Camera = Model.Marker("camera " + s.Marker);
                    if (info.Camera == null && s.Driver) info.Camera = Model.Marker("camera driver");
                    info.LeftFoot = Model.Marker(s.Marker + " left foot");
                    info.RightFoot = Model.Marker(s.Marker + " right foot");
                    info.LeftGrip = Model.Marker(s.Marker + " left hand");
                    info.RightGrip = Model.Marker(s.Marker + " right hand");
                    if (s.Driver && info.LeftGrip == null) { info.LeftGrip = Model.Marker("left hand wheel grip"); info.RightGrip = Model.Marker("right hand wheel grip"); }
                }
                if (info.Marker == null) info.Marker = transform;
                info.Standing = info.LeftFoot != null && info.RightFoot != null && s.Gunner;
                bool driverGuns = s.Driver && Def.GunnerSeat == null;   // ghost, banshee, scorpion, turret: the driver fires
                info.Guns = Def.Weapons.Count > 0 && (s.Gunner || driverGuns);
                Seats.Add(info);
            }
            if (Model != null)
            {
                CollectMarkers("primary trigger", Muzzles);
            }
        }

        private void CollectMarkers(string name, List<Transform> into)
        {
            into.Clear();
            var all = Model.Root.GetComponentsInChildren<Transform>(true);
            string want = "marker:" + name;
            foreach (var t in all) if (t.name == want || t.name.StartsWith(want + "#", StringComparison.Ordinal)) into.Add(t);
        }

        private void BuildWheels()
        {
            var phys = Def.Physics;
            if (phys == null) return;
            float u = HaloSpace.Unit;
            int n = 0;
            foreach (var p in phys.Points) if (p.HasSuspension) n++;
            float g = Physics.gravity.magnitude;
            foreach (var p in phys.Points)
            {
                if (p.HasSuspension)
                {
                    // The suspension depths (antr) are the ground's depth below the mass point's centre: 0.314 wu at full extension, 0.135 at
                    // full compression on the warthog. The model's rest pose has the point (the tyre's centre) at its height above the model
                    // origin (0.19 wu), the ground: that height is the tyre's drawn radius and the rest of the suspension.
                    var w = new Wheel { Point = p, Local = ModelToRoot(p.Position), Radius = Mathf.Clamp(p.Position.Z, 0.05f, Mathf.Max(0.05f, p.Radius)) * u };
                    w.MaxDist = -p.SuspensionExtension * u;
                    w.MinDist = -p.SuspensionCompression * u;
                    if (w.MaxDist <= w.MinDist + 0.02f) w.MaxDist = w.MinDist + 0.3f;
                    float restCompression = w.Radius > w.MinDist && w.Radius < w.MaxDist ? w.MaxDist - w.Radius : 0.45f * (w.MaxDist - w.MinDist);
                    float share = p.Mass > 0f && phys.Mass > 0f ? p.Mass / TotalSuspensionMass(phys) : 1f / Mathf.Max(1, n);
                    float load = Body.mass * g * share;
                    // the static load holds the point at the model's rest height (else 45 % of the travel); damping 0.55 of critical (ours)
                    w.Spring = load / Mathf.Max(0.05f, restCompression);
                    w.Damper = 2f * Mathf.Sqrt(w.Spring * Body.mass * share) * 0.55f;
                    w.Front = p.Position.X > 0f;
                    w.Left = p.Position.Y > 0f;
                    w.Powered = p.Powered >= 0;
                    w.Steers = Def.Type == HaloVehicleDef.Kind.HumanJeep && w.Front;
                    // Halo friction scales: forward = parallel, side = perpendicular (tires); treads grip hard both ways
                    w.GripForward = p.FrictionType == 1 ? p.FrictionParallel : 1f;
                    w.GripSide = p.FrictionType == 1 ? p.FrictionPerpendicular : 1.2f;
                    if (Model != null && p.Node >= 0 && p.Node < Model.Nodes.Length)
                    {
                        w.Tire = Model.Nodes[p.Node];
                        w.Hub = w.Tire.parent != null && w.Tire.parent.name.EndsWith("engine", StringComparison.Ordinal) ? w.Tire.parent : null;
                        Transform up = w.Hub != null ? w.Hub.parent : w.Tire.parent;
                        w.Suspension = up != null && up.name.EndsWith("suspension", StringComparison.Ordinal) ? up : null;
                        if (Def.Type == HaloVehicleDef.Kind.HumanTank) { w.Suspension = null; w.Hub = null; w.Tire = null; } // treads: one node per tread
                        if (w.Suspension != null) w.SuspensionRest = w.Suspension.localPosition;
                        if (w.Hub != null) w.HubRest = w.Hub.localRotation;
                        if (w.Tire != null) w.TireRest = w.Tire.localRotation;
                    }
                    Wheels.Add(w);
                }
                else if (p.Powered >= 0 && p.Powered < phys.Powered.Count && phys.Powered[p.Powered].Antigrav)
                {
                    var pp = phys.Powered[p.Powered];
                    Hovers.Add(new HoverPoint { Point = p, Powered = pp, Local = ModelToRoot(p.Position), Height = pp.AntigravHeight * u, Strength = pp.AntigravStrength, Mass = p.Mass });
                }
            }
        }

        private static float TotalSuspensionMass(HaloVehicleDef.PhysicsDef phys)
        {
            float m = 0f;
            foreach (var p in phys.Points) if (p.HasSuspension) m += p.Mass;
            return m > 0f ? m : 1f;
        }

        private void BuildNodes()
        {
            if (Model == null) return;
            _steeringWheel = Model.Node("frame steering wheel");
            switch (Def.Type)
            {
                case HaloVehicleDef.Kind.HumanJeep:
                    _gunYawNode = Model.Node("frame gun mount base"); _gunPitchNode = Model.Node("frame gun"); _barrels = Model.Node("frame barrels");
                    break;
                case HaloVehicleDef.Kind.HumanTank:
                    _gunYawNode = Model.Node("frame turret rotator"); _gunPitchNode = Model.Node("frame turret");
                    break;
                case HaloVehicleDef.Kind.Turret:
                    _gunYawNode = Model.Node("frame turret"); _gunPitchNode = Model.Node("frame turret end");
                    break;
                case HaloVehicleDef.Kind.AlienScout:
                    _gunPitchNode = Model.Node("frame guns");
                    break;
            }
            if (_steeringWheel != null) _steeringRest = _steeringWheel.localRotation;
            if (_gunYawNode != null) _gunYawRest = _gunYawNode.localRotation;
            if (_gunPitchNode != null) _gunPitchRest = _gunPitchNode.localRotation;
            if (_barrels != null) _barrelsRest = _barrels.localRotation;
        }

        // ------------------------------------------------------------------ state

        /// <summary>Kinematic, solid, still (nobody drives it here).</summary>
        public void Park(bool settle)
        {
            LocalDriven = false;
            RemoteDriven = false;
            if (Body == null) return;
            if (settle && CarriedBy != null) settle = false;    // cargo: rides kinematic on the carrier's seat until released
            if (settle && Def.Physics == null) settle = false; // no physics tag (the a10 cryotube): a fixture, never settled
            if (settle && Def.Type == HaloVehicleDef.Kind.HumanPlane) settle = false; // dropships / pelicans hover where they are left (Halo), never drop
            if (settle && !Physics.Raycast(transform.position + Vector3.up * 2f, Vector3.down, 500f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                settle = false; // no ground built under it (yet): hold it where the scenario put it
                if (!_saidNoGround) { _saidNoGround = true; HaloLog.Info("Vehicles: " + name + " has no ground under it (none within 500 m: the world is not built); held where it is."); }
            }
            if (settle)
            {
                // a few seconds dynamic so it rests on the ground the scenario placed it over; avatars cannot shove it meanwhile
                Body.isKinematic = false;
                _settleUntil = Time.time + 3f;
                ExcludeAvatars(true);
            }
            else
            {
                if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
                Body.isKinematic = true;
                _settleUntil = 0f;
                ExcludeAvatars(LocalSeated);
            }
        }

        /// <summary>The local player drives: dynamic, simulated here.</summary>
        public void TakeControl()
        {
            if (CarriedBy != null) return;                     // cargo: held on the carrier's seat (a carried vehicle does not drive)
            if (Body != null && !_rec && Body.interpolation == RigidbodyInterpolation.None) Body.interpolation = RigidbodyInterpolation.Interpolate;
            LocalDriven = true; RemoteDriven = false;
            _settleUntil = 0f;
            // a gun turret never moves: it stays kinematic (it only aims)
            if (Body != null) { Body.isKinematic = Def.Type == HaloVehicleDef.Kind.Turret; if (!Body.isKinematic) Body.WakeUp(); }
            LastUsed = Time.time;
        }

        /// <summary>A remote player drives: kinematic, the net places it.</summary>
        public void Remote()
        {
            if (!RemoteDriven) HaloLog.Info("Vehicles: " + name + " is driven by another player (placed from the net).");
            RemoteDriven = true; LocalDriven = false;
            _settleUntil = 0f;
            if (Body != null) Body.isKinematic = true;
            LastUsed = Time.time;
        }

        public void SetLocalSeated(bool seated)
        {
            LocalSeated = seated;
            ExcludeAvatars(seated || _settleUntil > 0f);
        }

        private void ExcludeAvatars(bool on)
        {
            if (_excludingAvatars == on) return;
            _excludingAvatars = on;
            foreach (var c in Shapes)
                if (c != null) c.excludeLayers = on ? (c.excludeLayers.value | AvatarLayers) : ((c.excludeLayers.value & ~AvatarLayers) | (1 << HaloLayers.Hitbox));
        }

        /// <summary>Placed from the net (remote driver / master's parked pose).</summary>
        public void Place(Vector3 pos, Quaternion rot, Vector3 vel)
        {
            if (Body == null) return;
            if (!Body.isKinematic) Body.isKinematic = true;
            Body.MovePosition(pos); Body.MoveRotation(rot);
            transform.SetPositionAndRotation(pos, rot);
            _netVelocity = vel;
        }

        /// <summary>Back to the scenario's spot (respawn of an abandoned vehicle).</summary>
        public void Respawn()
        {
            HaloLog.Info("Vehicles: " + name + " respawned at its scenario spot.");
            if (Body != null) { if (!Body.isKinematic) { Body.linearVelocity = Vector3.zero; Body.angularVelocity = Vector3.zero; } Body.isKinematic = true; }
            transform.SetPositionAndRotation(SpawnPosition, SpawnRotation);
            Physics.SyncTransforms();
            AimYaw = 0f; AimPitch = 0f;
            Park(true);
            LastUsed = Time.time;
            HaloVehicleNet.NoteRespawn(this);
        }

        /// <summary>Upside down or on its side (Halo flips it back when a player tries to get in).</summary>
        public bool Overturned => transform.up.y < 0.35f;

        /// <summary>Halo's "flip": rights an overturned vehicle (scripted over 0.7 s, ours).</summary>
        public void Flip()
        {
            if (_flipping) return;
            _flipping = true; _flipT = 0f;
            _flipFrom = transform.rotation;
            Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            _flipTo = Quaternion.LookRotation(f.normalized, Vector3.up);
            _flipPos = transform.position + Vector3.up * 1.2f;
            if (Body != null) Body.isKinematic = true;
            HaloLog.Info("Vehicles: " + name + " flipped back onto its wheels.");
        }

        private void StepFlip(float dt)
        {
            _flipT += dt / 0.7f;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_flipT));
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, _flipPos, 0.2f), Quaternion.Slerp(_flipFrom, _flipTo, t));
            if (_flipT >= 1f)
            {
                _flipping = false;
                if (LocalDriven) TakeControl(); else Park(true);
            }
        }

        public void TakeHit(HaloHit hit)
        {
            if (Campaign && CampaignDamage(hit)) return;
            // Halo CE multiplayer vehicles are invulnerable (body vitality 0 in every MP coll tag); a hit pushes and reaches the riders.
            if (Body != null && LocalDriven && hit.Explosion && hit.Direction.sqrMagnitude > 0f)
                Body.AddForceAtPosition(hit.Direction.normalized * Mathf.Min(hit.Amount, 200f) * 40f, hit.Point, ForceMode.Impulse); // ours
            if (LocalSeated && !hit.Instant && hit.SourcePlayer != HaloLocal.NetId && Def.RiderDamageFraction > 0f)
            {
                HaloHit pass = hit;
                pass.Amount = hit.Amount * Def.RiderDamageFraction;
                HaloGame.Guard("vehicle rider damage", () => HaloCombat.TakeHit(pass));
            }
        }

        // ------------------------------------------------------------------ per frame

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (_flipping) { StepFlip(dt); return; }
            if (_settleUntil > 0f)
            {
                SettleStep(dt);
                // held again once at rest (after at least 1 s); a vehicle still falling / rolling (a released cargo jeep on a slope) keeps
                // its physics past the 3 s until it rests, at most 8 s more (ours) - never frozen mid-air or mid-tumble
                bool resting = Body.isKinematic || (Body.linearVelocity.sqrMagnitude < 0.01f && Body.angularVelocity.sqrMagnitude < 0.01f);
                if ((resting && Time.time > _settleUntil - 2f) || Time.time >= _settleUntil + 8f) Park(false);
                return;
            }
            if (LocalDriven) DriveStep(dt);
        }

        private void LateUpdate()
        {
            RecordedFrame();
            float dtA = Time.deltaTime;
            if (dtA > 1e-4f) { Vector3 pos = transform.position; if (_posKnown) _measuredVel = Vector3.Lerp(_measuredVel, (pos - _lastPos) / dtA, 1f - Mathf.Exp(-dtA * 10f)); _lastPos = pos; _posKnown = true; }
            if (dtA > 1e-4f) { Vector3 vel = Velocity; _accel = Vector3.Lerp(_accel, (vel - _lastVel) / dtA, 1f - Mathf.Exp(-dtA * 8f)); _lastVel = vel; }
            AnimateNodes(Time.deltaTime);
            StepDoor(Time.deltaTime);
            Engine();
        }

        private HaloFx.Attachments _fx;
        /// <summary>The engine loops' scale now (speed / top speed) and how many loops run (the lab reads them).</summary>
        public float EngineScale { get; private set; }
        public int EngineLoops => _fx != null && !_fx.Stopped ? _fx.Sounds.Count : 0;
        private bool _engineOn, _saidFx;

        /// <summary>
        /// The vehicle's own attachments (Object +320 through HaloFx.Attach: the engine sound_looping, headlights / taillights, the
        /// banshee's wingtip contrails) run while someone drives or rides it; the looping sounds are scaled by the speed (Halo's
        /// attachment "primary scale" = the vehicle's speed function: warthog_7_engine pitch x1..x2 and its idle / low / mid / high range).
        /// </summary>
        private void Engine()
        {
            bool want = LocalSeated || LocalDriven || RemoteDriven || !Empty || HaloCinematic.IsRecording(gameObject);
            if (_fx != null && !_fx.Stopped)
            {
                float top = PerTick(Def.MaxForward);
                float scale = top > 0f ? Mathf.Clamp01(SpeedMs / top) : (Gun != null && (Gun.Firing || Gun.ReplayFiring) ? 1f : 0f);
                for (int i = 0; i < _fx.Sounds.Count; i++) HaloAudio.SetScale(_fx.Sounds[i], scale);
                EngineScale = scale;
            }
            if (want == _engineOn) return;
            _engineOn = want;
            if (!want) { if (_fx != null) HaloFx.Detach(_fx); _fx = null; return; }
            _fx = HaloFx.Attach(Def.Tag, Model != null ? Model.Root.transform : transform, false, true);
            if (!_saidFx) { _saidFx = true; HaloLog.Info("Vehicles: " + name + " powered up: " + _fx.Sounds.Count + " looping sound(s), " + _fx.Lights.Count + " light(s), " + _fx.Trails.Count + " contrail(s)."); }
        }

        private void OnDestroy()
        {
            if (_fx != null) HaloFx.Detach(_fx);
            _fx = null;
        }

        private Vector3 _accel, _lastVel;
        private float _bank, _lastYaw; private bool _yawKnown;

        /// <summary>The banshee leans into its turns (ours: 0.35 deg per deg/s of yaw rate, 40 deg at most, the model only: a rider's view stays
        /// level - the seat takes the heading alone).</summary>
        private void Bank(float dt)
        {
            if (Def.Type != HaloVehicleDef.Kind.AlienFighter || ModelRoot == null || dt <= 1e-5f) return;
            Vector3 f = transform.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float rate = _yawKnown ? Mathf.DeltaAngle(_lastYaw, yaw) / dt : 0f;
            _lastYaw = yaw; _yawKnown = true;
            float want = Mathf.Clamp(-rate * 0.35f, -40f, 40f) * (SpeedMs > 2f ? 1f : 0f);
            _bank = Mathf.Lerp(_bank, want, 1f - Mathf.Exp(-dt * 4f));
            ModelRoot.localRotation = Quaternion.AngleAxis(_bank, Vector3.forward) * Quaternion.Euler(0f, -90f, 0f);
        }
        /// <summary>The vehicle's acceleration (m/s^2, smoothed over about 0.1 s): the riders' lurch overlays.</summary>
        public Vector3 Acceleration => _accel;

        public float Steer { get; private set; }    // -1 right .. 1 left (drawn on the wheel and the tires)

        private void AnimateNodes(float dt)
        {
            if (Model == null) return;
            Bank(dt);
            float steerDeg = Steer * Mathf.Max(Def.MaxLeftTurn, 1f);
            if (_steeringWheel != null) _steeringWheel.localRotation = _steeringRest * Quaternion.AngleAxis(-steerDeg * 3f, Vector3.right);
            Vector3 vel = Velocity;
            float forwardSpeed = Vector3.Dot(vel, transform.forward);
            for (int i = 0; i < Wheels.Count; i++)
            {
                var w = Wheels[i];
                if (w.Suspension != null)
                {
                    // the tyre drawn on the ground: its centre the drawn radius above the hit, within the suspension travel
                    float drop = Mathf.Clamp((w.Grounded ? w.Distance : w.MaxDist) - w.Radius, w.MinDist - w.Radius, w.MaxDist - w.Radius);
                    w.Suspension.localPosition = w.SuspensionRest - new Vector3(0f, drop, 0f);
                }
                if (w.Hub != null && w.Steers) w.Hub.localRotation = w.HubRest * Quaternion.AngleAxis(-steerDeg, Vector3.up);
                if (w.Tire != null)
                {
                    w.Spin = Mathf.Repeat(w.Spin + forwardSpeed * dt / Mathf.Max(0.1f, w.Radius) * Mathf.Rad2Deg, 360f);
                    w.Tire.localRotation = w.TireRest * Quaternion.AngleAxis(-w.Spin, Vector3.forward);
                }
            }
            if (_gunYawNode != null) _gunYawNode.localRotation = _gunYawRest * Quaternion.AngleAxis(-AimYaw * Mathf.Rad2Deg, Vector3.up);
            if (_gunPitchNode != null) _gunPitchNode.localRotation = _gunPitchRest * Quaternion.AngleAxis(AimPitch * Mathf.Rad2Deg, Vector3.forward);
            if (_barrels != null)
            {
                _barrelSpinRate = Mathf.MoveTowards(_barrelSpinRate, Gun != null && Gun.Firing ? 1440f : 0f, 1440f * dt);
                _barrelSpin = Mathf.Repeat(_barrelSpin + _barrelSpinRate * dt, 360f);
                _barrels.localRotation = _barrelsRest * Quaternion.AngleAxis(-_barrelSpin, Vector3.right);
            }
        }

        /// <summary>The gun's aim direction in the world (from the yaw / pitch the seat set), and the pivot it turns about.</summary>
        public Vector3 AimDirection
        {
            get
            {
                Quaternion q = transform.rotation * Quaternion.Euler(-AimPitch * Mathf.Rad2Deg, -AimYaw * Mathf.Rad2Deg, 0f);
                return q * Vector3.forward;
            }
        }

        public Transform GunPivot => _gunPitchNode != null ? _gunPitchNode : (_gunYawNode != null ? _gunYawNode : transform);

        /// <summary>Turns the gun toward a world direction at the seat's rates, within the gun's limits (Halo seat yaw/pitch rate,
        /// the animation graph's aim range).</summary>
        public void AimToward(Vector3 worldDir, SeatInfo seat, float dt)
        {
            if (worldDir.sqrMagnitude < 1e-6f) return;
            Vector3 local = Quaternion.Inverse(transform.rotation) * worldDir.normalized;
            float yaw = -Mathf.Atan2(local.x, local.z);                         // left positive
            float pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f));
            float yawRate = (seat != null && seat.Def.YawRate > 0f ? seat.Def.YawRate : 90f) * Mathf.Deg2Rad;
            float pitchRate = (seat != null && seat.Def.PitchRate > 0f ? seat.Def.PitchRate : yawRate * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            float yawMax = Mathf.PI;
            if (Def.HasAimLimits && Def.Type != HaloVehicleDef.Kind.HumanJeep && Def.Type != HaloVehicleDef.Kind.HumanTank && Def.Type != HaloVehicleDef.Kind.Turret)
                yawMax = Mathf.Max(Def.AimLeft, Def.AimRight);
            yaw = Mathf.Clamp(yaw, -yawMax, yawMax);
            float pMin = seat != null && seat.Def.PitchMin < 0f ? seat.Def.PitchMin : -0.5f;
            float pMax = seat != null && seat.Def.PitchMax > 0f ? seat.Def.PitchMax : 0.6f;
            pitch = Mathf.Clamp(pitch, pMin, pMax);
            float dy = Mathf.DeltaAngle(AimYaw * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            AimYaw = yawMax >= Mathf.PI ? Mathf.Repeat(AimYaw + Mathf.Clamp(dy, -yawRate * dt, yawRate * dt) + Mathf.PI, 2f * Mathf.PI) - Mathf.PI
                                         : Mathf.Clamp(AimYaw + Mathf.Clamp(dy, -yawRate * dt, yawRate * dt), -yawMax, yawMax);
            AimPitch = Mathf.MoveTowards(AimPitch, pitch, pitchRate * dt);
        }

        public SeatInfo SeatOf(int index) { return index >= 0 && index < Seats.Count ? Seats[index] : null; }

        public SeatInfo DriverSeat { get { foreach (var s in Seats) if (s.Def.Driver) return s; return Seats.Count > 0 ? Seats[0] : null; } }

        public bool HasDriver { get { var d = DriverSeat; return d != null && d.Occupant >= 0; } }

        public bool Empty { get { foreach (var s in Seats) if (s.Occupant >= 0) return false; return true; } }
    }
}

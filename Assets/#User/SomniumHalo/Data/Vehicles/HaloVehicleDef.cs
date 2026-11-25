using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A vehicle tag (vehi : unit : object) read for driving: the vehicle numbers (type, speeds, turn rates, slide), the unit seats
    /// (label, marker, flags, aim rates and limits, camera marker), the unit's weapons, the physics tag (mass points: Halo's own
    /// vehicle physics - powered / steerable / friction / antigrav), the collision model (body and shield vitality, damage
    /// thresholds and effects), the animation graph's vehicle aim limits and suspension depths, and the object attachments
    /// (engine loops, lights). Offsets: DevTools/reference/layouts.txt (struct Vehicle, Unit, UnitSeat, Physics, PhysicsMassPoint,
    /// PhysicsPoweredMassPoint, ModelCollisionGeometry, ModelAnimations*). Halo units (world units, seconds); Unity-free.
    ///
    /// Halo CE has no "enter marker" or "enter radius" in the seat: the model carries "&lt;seat marker&gt; enter" markers
    /// ("driver enter", "passenger lf enter") and the game lets a player in near them; <see cref="Seat.EnterMarker"/> is that name.
    /// </summary>
    public sealed class HaloVehicleDef
    {
        /// <summary>VehicleType (vehicle.json): the driving model.</summary>
        public enum Kind { HumanTank = 0, HumanJeep = 1, HumanBoat = 2, HumanPlane = 3, AlienScout = 4, AlienFighter = 5, Turret = 6 }

        /// <summary>UnitSeatFlags bits.</summary>
        public const uint SeatInvisible = 1, SeatLocked = 2, SeatDriver = 4, SeatGunner = 8, SeatThirdPerson = 16, SeatAllowsWeapons = 32,
            SeatThirdPersonOnEnter = 64, SeatFirstPersonSlavedToGun = 128, SeatNotValidWithoutDriver = 512;

        /// <summary>VehicleFlags bits.</summary>
        public const uint VehicleKillsRidersAtTerminalVelocity = 1u << 6, VehicleCausesCollisionDamage = 1u << 7,
            VehicleSteersDirectly = 1u << 15, VehicleHasEbrake = 1u << 17, VehicleNoFrictionWithDriver = 1u << 19;

        /// <summary>PhysicsPoweredMassPointFlags bits.</summary>
        public const uint PoweredGroundFriction = 1, PoweredWaterFriction = 2, PoweredAirFriction = 4, PoweredWaterLift = 8, PoweredAirLift = 16,
            PoweredThrust = 32, PoweredAntigrav = 64;

        public sealed class Seat
        {
            public int Index;
            public uint Flags;
            public string Label = "", Marker = "", CameraMarker = "";
            public float YawRate, PitchRate;           // degrees per second (Halo stores degrees for these)
            public float PitchAutoLevel, PitchMin, PitchMax, YawMin, YawMax; // radians
            public HVec3 AccelerationScale;
            public HaloTag HudInterface;
            /// <summary>The seat's camera track (trak), first of its block; null = the unit's.</summary>
            public CameraTrack Track;
            public bool Driver => (Flags & SeatDriver) != 0;
            public bool Gunner => (Flags & SeatGunner) != 0;
            public bool Invisible => (Flags & SeatInvisible) != 0;
            public bool AllowsWeapons => (Flags & SeatAllowsWeapons) != 0;
            /// <summary>The model marker a player enters this seat at (Halo CE convention: "&lt;marker&gt; enter").</summary>
            public string EnterMarker => Marker + " enter";
            public override string ToString() => Label + " [" + Marker + "] flags " + Flags.ToString("X");
        }

        public sealed class MassPoint
        {
            public string Name = "";
            public int Powered, Node;
            public uint Flags;
            public float RelativeMass, Mass, RelativeDensity, Density;
            public HVec3 Position, Forward, Up;
            /// <summary>PhysicsFrictionType: 0 point, 1 forward, 2 left, 3 up.</summary>
            public int FrictionType;
            public float FrictionParallel, FrictionPerpendicular, Radius;
            /// <summary>From the animation graph's suspension block (0 = none): ground depth at full extension / compression.</summary>
            public float SuspensionExtension, SuspensionCompression;
            public bool HasSuspension;
            public override string ToString() => Name + " node " + Node + " powered " + Powered + " @" + Position + " r " + Radius.ToString("0.###") + " friction " + FrictionType;
        }

        public sealed class PoweredPoint
        {
            public string Name = "";
            public uint Flags;
            public float AntigravStrength, AntigravOffset, AntigravHeight, AntigravDamp, AntigravNormalK1, AntigravNormalK0;
            public bool Antigrav => (Flags & PoweredAntigrav) != 0;
            public bool Thrust => (Flags & PoweredThrust) != 0;
            public override string ToString() => Name + " flags " + Flags.ToString("X") + (Antigrav ? " antigrav " + AntigravStrength.ToString("0.###") + " h " + AntigravHeight.ToString("0.###") : "");
        }

        public sealed class PhysicsDef
        {
            public float Radius, MomentScale, Mass, Density, GravityScale, GroundFriction, GroundDepth, GroundDamp, GroundNormalK1, GroundNormalK0;
            public float WaterFriction, WaterDepth, WaterDensity, AirFriction, XX, YY, ZZ;
            public HVec3 CenterOfMass;
            public readonly List<PoweredPoint> Powered = new List<PoweredPoint>();
            public readonly List<MassPoint> Points = new List<MassPoint>();
        }

        public sealed class Region { public string Name = ""; public float DamageThreshold; public HaloTag DestroyedEffect; }
        public sealed class Material { public string Name = ""; public int Type; public float ShieldLeak, ShieldMultiplier, BodyMultiplier; }

        public sealed class CollisionDef
        {
            public HaloTag Tag;
            public uint Flags;
            public float MaxBody, MaxShield, BodySystemShock, FriendlyDamageResistance;
            public float BodyDamagedThreshold, BodyDestroyedThreshold, ShieldDamagedThreshold, ShieldRechargeRate, StunTime, RechargeTime;
            public HaloTag BodyDamagedEffect, BodyDepletedEffect, BodyDestroyedEffect, ShieldDepletedEffect, LocalizedDamageEffect, AreaDamageEffect;
            public readonly List<Region> Regions = new List<Region>();
            public readonly List<Material> Materials = new List<Material>();
            /// <summary>Bounding box of the collision model (Halo units, model space).</summary>
            public float XMin, XMax, YMin, YMax, ZMin, ZMax;
        }

        /// <summary>A camera track (trak): control points (position in the camera marker's frame, Halo units; orientation), interpolated by
        /// the look pitch (first = looking down, last = looking up).</summary>
        public sealed class CameraTrack
        {
            public HaloTag Tag;
            public readonly List<HVec3> Points = new List<HVec3>();
            public static CameraTrack Read(HaloTag t)
            {
                if (t == null) return null;
                var c = new CameraTrack { Tag = t };
                foreach (var p in t.Root.Block(4, 60)) c.Points.Add(p.Vec3(0));
                return c;
            }
            public override string ToString() { var s = Tag.Name + ":"; foreach (var p in Points) s += " " + p; return s; }
        }

        public sealed class Attachment { public HaloTag Type; public string Marker = ""; public int PrimaryScale, SecondaryScale; }

        public readonly HaloTag Tag;
        public HaloTag Model, Animations, CollisionTag, PhysicsTag, CreationEffect, MeleeDamage;
        public uint ObjectFlags, UnitFlags, VehicleFlags;
        public Kind Type;
        public float BoundingRadius, AccelerationScale;
        public HVec3 BoundingOffset, OriginOffset;
        public float RiderDamageFraction, CameraFov, CameraStiffness, PitchAutoLevel, PitchMin, PitchMax;
        public string CameraMarker = "";
        public CameraTrack Track;
        public float MaxForward, MaxReverse, SpeedAcceleration, SpeedDeceleration, MaxLeftTurn, MaxRightTurn, WheelCircumference, TurnRate, BlurSpeed;
        public float MaxLeftSlide, MaxRightSlide, SlideAcceleration, SlideDeceleration, MinFlipAngularVelocity, MaxFlipAngularVelocity;
        public float FixedGunYaw, FixedGunPitch;
        public HaloTag SuspensionSound, CrashSound, MaterialEffects, Effect;
        public float DriverPowerupTime, DriverPowerdownTime;
        public readonly List<Seat> Seats = new List<Seat>();
        public readonly List<HaloTag> Weapons = new List<HaloTag>();
        public readonly List<Attachment> Attachments = new List<Attachment>();
        public PhysicsDef Physics;
        public CollisionDef Collision;

        /// <summary>Aim limits of the vehicle's gun from the animation graph (radians; 0 when the graph has no vehicle block).</summary>
        public float AimRight, AimLeft, AimDown, AimUp;
        public bool HasAimLimits;

        public HaloVehicleDef(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            ObjectFlags = r.U16(2);
            BoundingRadius = r.F32(4); BoundingOffset = r.Vec3(8); OriginOffset = r.Vec3(20); AccelerationScale = r.F32(32);
            Model = r.Ref(40); Animations = r.Ref(56); CollisionTag = r.Ref(112); PhysicsTag = r.Ref(128); CreationEffect = r.Ref(160);
            foreach (var a in r.Block(320, 72))
                Attachments.Add(new Attachment { Type = a.Ref(0), Marker = a.Str32(16), PrimaryScale = a.U16(48), SecondaryScale = a.U16(50) });

            // unit
            UnitFlags = r.U32(380);
            RiderDamageFraction = r.F32(388);
            CameraFov = r.F32(416); CameraStiffness = r.F32(420); CameraMarker = r.Str32(424);
            PitchAutoLevel = r.F32(488); PitchMin = r.F32(492); PitchMax = r.F32(496);
            foreach (var c in r.Block(500, 28)) { Track = CameraTrack.Read(c.Ref(0)); break; }
            MeleeDamage = r.Ref(648);
            foreach (var p in r.Block(716, 68)) { DriverPowerupTime = p.F32(4); DriverPowerdownTime = p.F32(8); break; }
            foreach (var w in r.Block(728, 36)) Weapons.Add(w.Ref(0));
            int si = 0;
            foreach (var s in r.Block(740, 284))
            {
                var seat = new Seat
                {
                    Index = si++, Flags = s.U32(0), Label = s.Str32(4), Marker = s.Str32(36), AccelerationScale = s.Vec3(100),
                    YawRate = s.F32(124), PitchRate = s.F32(128), CameraMarker = s.Str32(132), PitchAutoLevel = s.F32(196),
                    PitchMin = s.F32(200), PitchMax = s.F32(204), YawMin = s.F32(240), YawMax = s.F32(244),
                };
                foreach (var h in s.Block(220, 48)) { seat.HudInterface = h.Ref(0); break; }
                foreach (var c in s.Block(208, 28)) { seat.Track = CameraTrack.Read(c.Ref(0)); break; }
                Seats.Add(seat);
            }

            // vehicle
            VehicleFlags = r.U32(752); Type = (Kind)r.U16(756);
            MaxForward = r.F32(760); MaxReverse = r.F32(764); SpeedAcceleration = r.F32(768); SpeedDeceleration = r.F32(772);
            MaxLeftTurn = r.F32(776); MaxRightTurn = r.F32(780); WheelCircumference = r.F32(784); TurnRate = r.F32(788); BlurSpeed = r.F32(792);
            MaxLeftSlide = r.F32(816); MaxRightSlide = r.F32(820); SlideAcceleration = r.F32(824); SlideDeceleration = r.F32(828);
            MinFlipAngularVelocity = r.F32(832); MaxFlipAngularVelocity = r.F32(836);
            FixedGunYaw = r.F32(864); FixedGunPitch = r.F32(868);
            SuspensionSound = r.Ref(944); CrashSound = r.Ref(960); MaterialEffects = r.Ref(976); Effect = r.Ref(992);

            if (PhysicsTag != null) Physics = ReadPhysics(PhysicsTag);
            if (CollisionTag != null) Collision = ReadCollision(CollisionTag);
            if (Animations != null) ReadAnimations(Animations);
        }

        public string Name => System.IO.Path.GetFileName(Tag.Name);

        public Seat DriverSeat { get { foreach (var s in Seats) if (s.Driver) return s; return null; } }
        public Seat GunnerSeat { get { foreach (var s in Seats) if (s.Gunner) return s; return null; } }

        static PhysicsDef ReadPhysics(HaloTag t)
        {
            var r = t.Root;
            var p = new PhysicsDef
            {
                Radius = r.F32(0), MomentScale = r.F32(4), Mass = r.F32(8), CenterOfMass = r.Vec3(12), Density = r.F32(24), GravityScale = r.F32(28),
                GroundFriction = r.F32(32), GroundDepth = r.F32(36), GroundDamp = r.F32(40), GroundNormalK1 = r.F32(44), GroundNormalK0 = r.F32(48),
                WaterFriction = r.F32(56), WaterDepth = r.F32(60), WaterDensity = r.F32(64), AirFriction = r.F32(72),
                XX = r.F32(80), YY = r.F32(84), ZZ = r.F32(88),
            };
            foreach (var s in r.Block(104, 128))
                p.Powered.Add(new PoweredPoint
                {
                    Name = s.Str32(0), Flags = s.U32(32), AntigravStrength = s.F32(36), AntigravOffset = s.F32(40), AntigravHeight = s.F32(44),
                    AntigravDamp = s.F32(48), AntigravNormalK1 = s.F32(52), AntigravNormalK0 = s.F32(56),
                });
            foreach (var s in r.Block(116, 128))
                p.Points.Add(new MassPoint
                {
                    Name = s.Str32(0), Powered = s.I16(32), Node = s.I16(34), Flags = s.U32(36), RelativeMass = s.F32(40), Mass = s.F32(44),
                    RelativeDensity = s.F32(48), Density = s.F32(52), Position = s.Vec3(56), Forward = s.Vec3(68), Up = s.Vec3(80),
                    FrictionType = s.U16(92), FrictionParallel = s.F32(96), FrictionPerpendicular = s.F32(100), Radius = s.F32(104),
                });
            return p;
        }

        static CollisionDef ReadCollision(HaloTag t)
        {
            var r = t.Root;
            var c = new CollisionDef
            {
                Tag = t, Flags = r.U32(0), MaxBody = r.F32(8), BodySystemShock = r.F32(12), FriendlyDamageResistance = r.F32(68),
                LocalizedDamageEffect = r.Ref(112), AreaDamageEffect = r.Ref(132),
                BodyDamagedThreshold = r.F32(148), BodyDamagedEffect = r.Ref(152), BodyDepletedEffect = r.Ref(168),
                BodyDestroyedThreshold = r.F32(184), BodyDestroyedEffect = r.Ref(188), MaxShield = r.F32(204),
                StunTime = r.F32(268), RechargeTime = r.F32(272), ShieldDamagedThreshold = r.F32(388), ShieldDepletedEffect = r.Ref(408),
                ShieldRechargeRate = r.F32(448),
                XMin = r.F32(616), XMax = r.F32(620), YMin = r.F32(624), YMax = r.F32(628), ZMin = r.F32(632), ZMax = r.F32(636),
            };
            foreach (var m in r.Block(564, 72))
                c.Materials.Add(new Material { Name = m.Str32(0), Type = m.U16(36), ShieldLeak = m.F32(40), ShieldMultiplier = m.F32(44), BodyMultiplier = m.F32(60) });
            foreach (var g in r.Block(576, 84))
                c.Regions.Add(new Region { Name = g.Str32(0), DamageThreshold = g.F32(40), DestroyedEffect = g.Ref(56) });
            return c;
        }

        void ReadAnimations(HaloTag t)
        {
            var r = t.Root;
            foreach (var v in r.Block(36, 116))
            {
                AimRight = v.F32(0) * v.U16(8); AimLeft = v.F32(4) * v.U16(10);
                AimDown = v.F32(12) * v.U16(20); AimUp = v.F32(16) * v.U16(22);
                HasAimLimits = AimRight + AimLeft + AimDown + AimUp > 0f;
                if (Physics != null)
                    foreach (var s in v.Block(104, 20))
                    {
                        int mp = s.I16(0);
                        if (mp < 0 || mp >= Physics.Points.Count) continue;
                        var p = Physics.Points[mp];
                        p.HasSuspension = true; p.SuspensionExtension = s.F32(4); p.SuspensionCompression = s.F32(8);
                    }
                break;
            }
        }

        /// <summary>One line per fact, for the log and the harness.</summary>
        public IEnumerable<string> Describe()
        {
            yield return Tag.Name + ": " + Type + " flags " + VehicleFlags.ToString("X") + " unit " + UnitFlags.ToString("X") + " model " + Model + " phys " + PhysicsTag + " coll " + CollisionTag;
            yield return $"  speed fwd {MaxForward:0.###} rev {MaxReverse:0.###} accel {SpeedAcceleration:0.####} decel {SpeedDeceleration:0.####} turn L {MaxLeftTurn:0.###} R {MaxRightTurn:0.###} rate {TurnRate:0.####} wheel {WheelCircumference:0.###} slide L {MaxLeftSlide:0.###} R {MaxRightSlide:0.###} acc {SlideAcceleration:0.####} dec {SlideDeceleration:0.####} flip {MinFlipAngularVelocity:0.###}..{MaxFlipAngularVelocity:0.###} gun {FixedGunYaw:0.###},{FixedGunPitch:0.###}";
            yield return $"  camera '{CameraMarker}' fov {CameraFov:0.###} stiff {CameraStiffness:0.###} pitch {PitchMin:0.###}..{PitchMax:0.###} rider dmg {RiderDamageFraction:0.###} aim R {AimRight:0.###} L {AimLeft:0.###} D {AimDown:0.###} U {AimUp:0.###} power up {DriverPowerupTime:0.###} down {DriverPowerdownTime:0.###}";
            foreach (var s in Seats) yield return $"  seat {s.Index} '{s.Label}' marker '{s.Marker}' flags {s.Flags:X} yaw rate {s.YawRate:0.###} pitch rate {s.PitchRate:0.###} cam '{s.CameraMarker}' yaw {s.YawMin:0.###}..{s.YawMax:0.###} pitch {s.PitchMin:0.###}..{s.PitchMax:0.###} hud {s.HudInterface}";
            foreach (var s in Seats) if (s.Track != null) yield return "   seat " + s.Index + " track " + s.Track;
            if (Track != null) yield return "  unit track " + Track;
            foreach (var w in Weapons) yield return "  weapon " + w;
            foreach (var a in Attachments) yield return "  attach " + a.Type + " @'" + a.Marker + "' scale " + a.PrimaryScale + "," + a.SecondaryScale;
            if (Physics != null)
            {
                var p = Physics;
                yield return $"  phys radius {p.Radius:0.###} moment {p.MomentScale:0.###} mass {p.Mass:0.###} com {p.CenterOfMass} density {p.Density:0.###} gravity {p.GravityScale:0.###} ground friction {p.GroundFriction:0.###} depth {p.GroundDepth:0.###} damp {p.GroundDamp:0.###} k1 {p.GroundNormalK1:0.###} k0 {p.GroundNormalK0:0.###} air {p.AirFriction:0.###} water {p.WaterFriction:0.###} I {p.XX:0.###},{p.YY:0.###},{p.ZZ:0.###}";
                foreach (var q in p.Powered) yield return "   powered " + q;
                foreach (var q in p.Points) yield return $"   point {q.Name} node {q.Node} powered {q.Powered} flags {q.Flags:X} relmass {q.RelativeMass:0.###} mass {q.Mass:0.###} reldens {q.RelativeDensity:0.###} dens {q.Density:0.###} @{q.Position} fwd {q.Forward} up {q.Up} fr {q.FrictionType} par {q.FrictionParallel:0.###} perp {q.FrictionPerpendicular:0.###} r {q.Radius:0.###}" + (q.HasSuspension ? $" susp {q.SuspensionExtension:0.###}..{q.SuspensionCompression:0.###}" : "");
            }
            if (Collision != null)
            {
                var c = Collision;
                yield return $"  coll body {c.MaxBody:0.###} shield {c.MaxShield:0.###} damaged {c.BodyDamagedThreshold:0.###} destroyed {c.BodyDestroyedThreshold:0.###} destroyed fx {c.BodyDestroyedEffect} depleted fx {c.BodyDepletedEffect} damaged fx {c.BodyDamagedEffect} box x {c.XMin:0.###}..{c.XMax:0.###} y {c.YMin:0.###}..{c.YMax:0.###} z {c.ZMin:0.###}..{c.ZMax:0.###}";
                foreach (var g in c.Regions) yield return $"   region {g.Name} threshold {g.DamageThreshold:0.###} fx {g.DestroyedEffect}";
                foreach (var m in c.Materials) yield return $"   material {m.Name} type {m.Type} body x{m.BodyMultiplier:0.###} shield x{m.ShieldMultiplier:0.###}";
            }
        }
    }

    /// <summary>A human plane's flight state while a recording drives it (Halo space: world units, ticks, radians).</summary>
    public struct HaloPlaneState
    {
        /// <summary>Hull heading (Halo yaw: 0 = +X, counter-clockwise about +Z).</summary>
        public float Yaw;
        /// <summary>The control's forward / slide speed (wu per tick), ramped by the tag's accelerations toward the throttle.</summary>
        public float Speed, Side;
        /// <summary>The hull's velocity, Halo world frame, wu per tick.</summary>
        public float Vx, Vy, Vz;
    }

    /// <summary>
    /// How a recording flies a human plane (vehicle type 3: c_dropship, pelican), one 30 Hz tick at a time, in Halo space. Shared by the
    /// game (HaloVehicle.PlaneTick, HaloCinematic's fallback mover) and the offline probe (DevTools/agents/Q/carrier "fly"), so what the
    /// probe measures against the retail scenarios is exactly what the game flies.
    ///   1. Heading: the hull turns toward the recorded facing yaw by <see cref="YawResponse"/> of the remaining angle each tick (held while
    ///      "look don't turn"); the facing / look pitch never tilts it.
    ///   2. Control speeds (tag): forward speed toward throttle x maximum forward (reverse) speed at the speed acceleration while the target
    ///      is faster, the speed deceleration otherwise; slide toward throttle-left x maximum left / right slide at the slide acceleration /
    ///      deceleration. Jump / Crouch ask for maximum forward speed straight up / down.
    ///   3. Hull velocity: the target velocity (heading x speed + left x slide + up x vertical) is approached by <see cref="VelocityResponse"/>
    ///      of the difference per tick, then the physics tag's air friction (0.02 for both) takes its fraction off every axis.
    ///   Steady level flight is therefore 0.05 / (0.05 + 0.02 - 0.001) = 0.72 x the tag's maximum speed, and it takes about 14 ticks to
    ///   follow a change.
    /// Retail Halo's own vehicle code is not available (the PC executable is encrypted), so the two response constants are measured: with
    /// them, and no per-recording change, every a30 wave dropship drops inside the LZ volume its name gives (distance from the volume's
    /// centre: lz_landing 10.9, first_fort_lz 5.7, first_pipe_lz 5.8, first_back_lz 5.4, rubble_rock_lz 2.0, rubble_middle_lz 2.5,
    /// rubble_boat_lz 7.9 wu), and every drop that ends with Crouch comes down onto the floor (probe "fly eval": a30, b30, b40, c10, a50).
    /// Remaining misses (the model's dead reckoning is good to about 2..11 wu over 150..300 wu flights, for pelicans and dropships alike):
    ///   - a30 foehammer_first_in leaves the jeep (pelican "cargo" marker) 10.6 wu from the centre of first_drop (2 x 3 wu: outside), short
    ///     along its final approach; b30 hog_pelican_in leaves it 8.1 wu from jeep_delivery (3 x 5 wu: outside), mostly across its track.
    ///   - b30 insertion pelicans end 3.1 / 8.8 wu from the designers' placements for their "out" flights.
    ///   - a30 river_boat_cship ends 48.5 wu from river_boat_lz (that volume is not where it drops; logged only).
    ///   Ruled out as the jeep misses' cause (probe, 2026-11-04): the pelican's vehicle tag equals the dropship's (speeds, accelerations,
    ///   slides, turn rate) and so do mass / air friction / gravity / ground values - only centre of mass, moments and mass points differ,
    ///   none of which this model uses; neither recording holds LookDontTurn beyond 3 start ticks; the cargo marker is (-1.662, 0, -0.428)
    ///   on the hull's root node, identity rotation; the jeep drops after the recording ends at near zero velocity (unit_exit_vehicle
    ///   follows sleep (recording_time)); the scripts teleport the pelican only to its start flag (= its placement on a30) before the
    ///   recording, and first_drop / jeep_delivery are the drop zones (each script keeps players out of it before unit_exit_vehicle).
    ///   No global rule tried brings both jeeps in without pushing dropships out: yaw response 0.02..1, rate-limited turning at the
    ///   tag's turn rate, second-order (angular velocity) turning, velocity response 0.03..0.12, separate decelerating / sideways
    ///   responses, scaled tag accelerations / decelerations, horizontal speed x cos(facing pitch). foehammer_first stays 7..13 wu off in
    ///   all of them except a uniform 0.92..0.93 speed scale, which hog_pelican and the insertion pelicans (best at 1.00..1.01) refute.
    /// </summary>
    public static class HaloPlaneFlight
    {
        /// <summary>Fraction of the remaining yaw toward the recorded facing the hull turns per tick (ours, measured: see the class).</summary>
        public const float YawResponse = 0.04f;
        /// <summary>Fraction of the difference to the target velocity the hull's velocity takes per tick (ours, measured: see the class).</summary>
        public const float VelocityResponse = 0.05f;

        static float Toward(float v, float t, float a) { if (a <= 0f) return t; return v < t ? Math.Min(t, v + a) : Math.Max(t, v - a); }

        public static float WrapAngle(float a)
        {
            while (a > (float)Math.PI) a -= 2f * (float)Math.PI;
            while (a < -(float)Math.PI) a += 2f * (float)Math.PI;
            return a;
        }

        /// <summary>A fresh state (a recording starts from hover: no velocity) at a heading.</summary>
        public static HaloPlaneState Start(float yaw) { return new HaloPlaneState { Yaw = yaw }; }

        /// <summary>One tick: updates heading, control speeds and the hull velocity (s.Vx / Vy / Vz = this tick's move, wu).</summary>
        public static void Step(HaloVehicleDef d, ref HaloPlaneState s, ref HaloRecordedControl c)
        {
            if (!c.Has(HaloRecordedControl.LookDontTurn)) s.Yaw = WrapAngle(s.Yaw + WrapAngle(c.FacingYawRadians - s.Yaw) * YawResponse);
            float top = c.ThrottleForward >= 0f ? d.MaxForward * c.ThrottleForward : d.MaxReverse * c.ThrottleForward;
            float side = c.ThrottleLeft * (c.ThrottleLeft >= 0f ? d.MaxLeftSlide : d.MaxRightSlide);
            s.Speed = Toward(s.Speed, top, Math.Abs(top) > Math.Abs(s.Speed) ? d.SpeedAcceleration : d.SpeedDeceleration);
            s.Side = Toward(s.Side, side, Math.Abs(side) > Math.Abs(s.Side) ? d.SlideAcceleration : d.SlideDeceleration);
            float up = c.Has(HaloRecordedControl.JumpBit) ? d.MaxForward : c.Has(HaloRecordedControl.Crouch) ? -d.MaxForward : 0f;
            float hx = (float)Math.Cos(s.Yaw), hy = (float)Math.Sin(s.Yaw);
            float tx = hx * s.Speed - hy * s.Side, ty = hy * s.Speed + hx * s.Side;
            float k = VelocityResponse, keep = 1f - AirFriction(d);
            s.Vx = (s.Vx + (tx - s.Vx) * k) * keep;
            s.Vy = (s.Vy + (ty - s.Vy) * k) * keep;
            s.Vz = (s.Vz + (up - s.Vz) * k) * keep;
        }

        /// <summary>The physics tag's air friction (fraction of the velocity lost per tick); 0 without a physics tag.</summary>
        public static float AirFriction(HaloVehicleDef d)
        {
            float f = d.Physics != null ? d.Physics.AirFriction : 0f;
            return f < 0f ? 0f : f > 0.5f ? 0.5f : f;
        }
    }
}

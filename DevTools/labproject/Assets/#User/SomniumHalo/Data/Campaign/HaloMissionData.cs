using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Mission module, K] What a campaign scenario carries beyond <see cref="HaloScenario"/> for the mission flow: every object placement
    /// of every kind with its scenario name (object names +516), the bipeds (+552), device data of machines / controls / light fixtures
    /// (power group, position group, device flags, machine / control flags), device groups (+648), trigger volumes (+864), bsp switch
    /// trigger volumes (+924), player starting profiles (+840), cutscene flags (+1252) and each structure BSP's world bounds (to know which
    /// objects belong to which BSP). Offsets: DevTools/reference/layouts.txt (struct Scenario*). Positions in Halo units / space. Unity-free.
    /// </summary>
    public sealed class HaloMissionData
    {
        /// <summary>ObjectType as the scenario numbers it.</summary>
        public enum Kind { Biped = 0, Vehicle = 1, Weapon = 2, Equipment = 3, Garbage = 4, Projectile = 5, Scenery = 6, Machine = 7, Control = 8, LightFixture = 9, Placeholder = 10, SoundScenery = 11 }

        /// <summary>One scenario placement of any kind.</summary>
        public sealed class Obj
        {
            public Kind Kind;
            /// <summary>Index in its kind's block (scenery 0.., machines 0.., ...).</summary>
            public int Index;
            public HaloTag Type;
            public int PaletteIndex, NameIndex, DesiredPermutation;
            /// <summary>"" when the placement has no name.</summary>
            public string Name = "";
            /// <summary>ScenarioSpawnNotPlaced: bit 0 automatically (a script creates it), 1 on easy, 2 on normal, 3 on hard (heroic + legendary).</summary>
            public ushort NotPlaced;
            public HVec3 Position, Rotation;
            /// <summary>Scenery / light fixtures: the BSPs the compiler found it in (bit per BSP; 0 = not stored).</summary>
            public ushort BspMask;
            // devices (machines, controls, light fixtures)
            public int PowerGroup = -1, PositionGroup = -1;
            /// <summary>ScenarioDeviceFlags: 0 initially open (position 1), 1 initially off (power 0), 2 can change only once, 3 position reversed, 4 not usable from any side.</summary>
            public uint DeviceFlags;
            /// <summary>Machines: 0 does not operate automatically, 1 one sided, 2 never appears locked, 3 opened by melee. Controls: 0 usable from both sides.</summary>
            public uint SubFlags;
            public int CustomControlName = -1;
            // units
            public float BodyVitality;
            /// <summary>ScenarioUnitFlags bit 0 = dead.</summary>
            public uint UnitFlags;
            // items
            public int RoundsLoaded, RoundsReserved;
            public uint ItemFlags;

            public bool AutomaticallyPlaced => (NotPlaced & 1) == 0;
            public bool Dead => (UnitFlags & 1) != 0;
            /// <summary>Halo's "not placed" difficulty bits: easy = bit 1, normal = bit 2, hard = bit 3 (heroic and legendary).</summary>
            public bool PlacedOn(int difficulty)
            {
                int bit = difficulty <= 0 ? 1 : difficulty == 1 ? 2 : 3;
                return (NotPlaced & (1 << bit)) == 0;
            }
            public override string ToString() => Kind + " " + Index + (Name.Length > 0 ? " '" + Name + "'" : "") + " " + (Type != null ? Type.Name : "?") + " @" + Position;
        }

        public sealed class DeviceGroup { public string Name = ""; public float Initial; public uint Flags; public bool OnlyOnce => (Flags & 1) != 0; }

        public sealed class TriggerVolume
        {
            public int Type; public string Name = "";
            public HVec3 Forward, Up, Corner, Extent;
            /// <summary>Halo's point test: the point relative to the starting corner, projected on forward / left (up x forward) / up, lies between 0
            /// and the ending corner offset on every axis.</summary>
            public bool Contains(float x, float y, float z)
            {
                float dx = x - Corner.X, dy = y - Corner.Y, dz = z - Corner.Z;
                float lx = Up.Y * Forward.Z - Up.Z * Forward.Y, ly = Up.Z * Forward.X - Up.X * Forward.Z, lz = Up.X * Forward.Y - Up.Y * Forward.X;
                float a = dx * Forward.X + dy * Forward.Y + dz * Forward.Z;
                float b = dx * lx + dy * ly + dz * lz;
                float c = dx * Up.X + dy * Up.Y + dz * Up.Z;
                return Between(a, Extent.X) && Between(b, Extent.Y) && Between(c, Extent.Z);
            }
            static bool Between(float v, float e) { return e >= 0f ? v >= 0f && v <= e : v <= 0f && v >= e; }
            /// <summary>Centre of the box (Halo space).</summary>
            public HVec3 Center
            {
                get
                {
                    float lx = Up.Y * Forward.Z - Up.Z * Forward.Y, ly = Up.Z * Forward.X - Up.X * Forward.Z, lz = Up.X * Forward.Y - Up.Y * Forward.X;
                    float a = Extent.X * 0.5f, b = Extent.Y * 0.5f, c = Extent.Z * 0.5f;
                    return new HVec3(Corner.X + Forward.X * a + lx * b + Up.X * c, Corner.Y + Forward.Y * a + ly * b + Up.Y * c, Corner.Z + Forward.Z * a + lz * b + Up.Z * c);
                }
            }
        }

        public sealed class BspSwitch { public int TriggerVolume, Source, Destination; }

        public sealed class StartingProfile
        {
            public string Name = "";
            public float Health, Shield;
            public HaloTag Primary, Secondary;
            public int PrimaryLoaded, PrimaryReserved, SecondaryLoaded, SecondaryReserved;
            public int Frag, Plasma;
        }

        public sealed class CutsceneFlag { public string Name = ""; public HVec3 Position; public float Yaw, Pitch; }

        public sealed class BspBounds { public float MinX, MaxX, MinY, MaxY, MinZ, MaxZ; public bool Valid; }

        public readonly HaloScenario Scenario;
        public readonly List<Obj> Objects = new List<Obj>();
        /// <summary>Object names (+516) in order: the index scripts use.</summary>
        public readonly List<string> Names = new List<string>();
        public readonly List<DeviceGroup> DeviceGroups = new List<DeviceGroup>();
        public readonly List<TriggerVolume> TriggerVolumes = new List<TriggerVolume>();
        public readonly List<BspSwitch> BspSwitches = new List<BspSwitch>();
        public readonly List<StartingProfile> Profiles = new List<StartingProfile>();
        public readonly List<CutsceneFlag> CutsceneFlags = new List<CutsceneFlag>();
        public readonly List<BspBounds> Bounds = new List<BspBounds>();
        public readonly List<string> Problems = new List<string>();

        public HaloMissionData(HaloScenario scenario)
        {
            Scenario = scenario;
            var r = scenario.Tag.Root;
            foreach (var n in r.Block(516, 36)) Names.Add(n.Map.Str(n.At, 32));
            Read(r, 528, 72, 540, Kind.Scenery);
            Read(r, 552, 120, 564, Kind.Biped);
            Read(r, 576, 120, 588, Kind.Vehicle);
            Read(r, 600, 40, 612, Kind.Equipment);
            Read(r, 624, 92, 636, Kind.Weapon);
            Read(r, 660, 64, 672, Kind.Machine);
            Read(r, 684, 64, 696, Kind.Control);
            Read(r, 708, 88, 720, Kind.LightFixture);
            Read(r, 732, 40, 744, Kind.SoundScenery);
            foreach (var g in r.Block(648, 52)) DeviceGroups.Add(new DeviceGroup { Name = g.Str32(0), Initial = g.F32(32), Flags = g.U32(36) });
            foreach (var t in r.Block(864, 96))
                TriggerVolumes.Add(new TriggerVolume { Type = t.U16(0), Name = t.Str32(4), Forward = t.Vec3(48), Up = t.Vec3(60), Corner = t.Vec3(72), Extent = t.Vec3(84) });
            foreach (var s in r.Block(924, 8)) BspSwitches.Add(new BspSwitch { TriggerVolume = s.I16(0), Source = s.I16(2), Destination = s.I16(4) });
            foreach (var p in r.Block(840, 104))
                Profiles.Add(new StartingProfile
                {
                    Name = p.Str32(0), Health = p.F32(32), Shield = p.F32(36),
                    Primary = p.Ref(40), PrimaryLoaded = p.I16(56), PrimaryReserved = p.I16(58),
                    Secondary = p.Ref(60), SecondaryLoaded = p.I16(76), SecondaryReserved = p.I16(78),
                    Frag = p.I8(80), Plasma = p.I8(81),
                });
            foreach (var f in r.Block(1252, 92))
                CutsceneFlags.Add(new CutsceneFlag { Name = f.Str32(4), Position = f.Vec3(36), Yaw = f.F32(48), Pitch = f.F32(52) });
            for (int i = 0; i < scenario.Bsps.Count; i++)
            {
                var b = new BspBounds();
                try
                {
                    var br = scenario.BspRoot(i);
                    b.MinX = br.F32(200); b.MaxX = br.F32(204); b.MinY = br.F32(208); b.MaxY = br.F32(212); b.MinZ = br.F32(216); b.MaxZ = br.F32(220);
                    b.Valid = b.MaxX > b.MinX && b.MaxY > b.MinY && b.MaxZ >= b.MinZ;
                }
                catch (Exception e) { Problems.Add("bsp " + i + " bounds: " + e.Message); }
                Bounds.Add(b);
            }
        }

        void Read(TagReader r, int blockAt, int stride, int paletteAt, Kind kind)
        {
            var palette = new List<HaloTag>();
            foreach (var p in r.Block(paletteAt, 48)) palette.Add(p.Ref(0));
            int index = 0;
            foreach (var s in r.Block(blockAt, stride))
            {
                int type = s.I16(0), name = s.I16(2);
                var o = new Obj
                {
                    Kind = kind, Index = index++, PaletteIndex = type, Type = type >= 0 && type < palette.Count ? palette[type] : null,
                    NameIndex = name, Name = name >= 0 && name < Names.Count ? Names[name] : "",
                    NotPlaced = s.U16(4), DesiredPermutation = s.I16(6), Position = s.Vec3(8), Rotation = s.Vec3(20), BspMask = s.U16(32),
                };
                switch (kind)
                {
                    case Kind.Biped: case Kind.Vehicle: o.BodyVitality = s.F32(72); o.UnitFlags = s.U32(76); break;
                    case Kind.Weapon: o.RoundsReserved = s.I16(72); o.RoundsLoaded = s.I16(74); o.ItemFlags = s.U16(76); break;
                    case Kind.Equipment: o.ItemFlags = s.U16(34); break;
                    case Kind.Machine: case Kind.Control: case Kind.LightFixture:
                        o.PowerGroup = s.I16(40); o.PositionGroup = s.I16(42); o.DeviceFlags = s.U32(44);
                        if (kind != Kind.LightFixture) o.SubFlags = s.U32(48);
                        if (kind == Kind.Control) o.CustomControlName = s.I16(52);
                        break;
                }
                Objects.Add(o);
            }
        }

        /// <summary>The placement with this scenario name (null when none).</summary>
        public Obj Named(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < Objects.Count; i++) if (string.Equals(Objects[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Objects[i];
            return null;
        }

        public int TriggerVolumeIndex(string name)
        {
            for (int i = 0; i < TriggerVolumes.Count; i++) if (string.Equals(TriggerVolumes[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public int DeviceGroupIndex(string name)
        {
            for (int i = 0; i < DeviceGroups.Count; i++) if (string.Equals(DeviceGroups[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public CutsceneFlag Flag(string name)
        {
            for (int i = 0; i < CutsceneFlags.Count; i++) if (string.Equals(CutsceneFlags[i].Name, name, StringComparison.OrdinalIgnoreCase)) return CutsceneFlags[i];
            return null;
        }

        /// <summary>Does object <paramref name="o"/> belong to structure BSP <paramref name="bsp"/>: its compiled bsp bits when stored, else its
        /// position inside the BSP's world bounds (with a little margin, Halo units). Objects inside no BSP's bounds go with the start BSP.</summary>
        public bool InBsp(Obj o, int bsp)
        {
            if (bsp < 0 || bsp >= Bounds.Count) return true;
            if (Bounds.Count == 1) return true;
            if (o.BspMask != 0 && (o.Kind == Kind.Scenery || o.Kind == Kind.LightFixture)) return (o.BspMask & (1 << bsp)) != 0;
            if (Inside(Bounds[bsp], o.Position, 0.5f)) return true;
            for (int i = 0; i < Bounds.Count; i++) if (Inside(Bounds[i], o.Position, 0.5f)) return false;
            return bsp == 0;
        }

        /// <summary>The BSP whose bounds contain a point (first match; -1 none).</summary>
        public int BspAt(HVec3 p)
        {
            for (int i = 0; i < Bounds.Count; i++) if (Inside(Bounds[i], p, 0f)) return i;
            return -1;
        }

        static bool Inside(BspBounds b, HVec3 p, float m)
        {
            return b.Valid && p.X >= b.MinX - m && p.X <= b.MaxX + m && p.Y >= b.MinY - m && p.Y <= b.MaxY + m && p.Z >= b.MinZ - m && p.Z <= b.MaxZ + m;
        }
    }

    /// <summary>
    /// [Mission module, K] A device definition (device_machine / device_control / device_light_fixture: struct Device +380..): transition and
    /// acceleration times, the open / close / opened / closed / depowered / repowered sounds or effects, the automatic activation radius; machines:
    /// type (door / platform / gear), flags (pathfinding obstacle, but not when open, elevator), door open time, collision response, elevator node;
    /// controls: type (toggle switch, on button, off button, call button), triggers when (touched by player / destroyed), call value, on / off / deny.
    /// Unity-free.
    /// </summary>
    public sealed class HaloDeviceDef
    {
        public readonly HaloTag Tag;
        public readonly int ObjectType;
        public readonly HaloTag Model, AnimationGraph, CollisionModel;
        /// <summary>DeviceFlags: bit 0 position loops, bit 1 position not interpolated.</summary>
        public readonly uint Flags;
        public readonly float PowerTransition, PowerAcceleration, PositionTransition, PositionAcceleration, DepoweredTransition, DepoweredAcceleration;
        /// <summary>DeviceIn of the object functions A..D (none, power, change in power, position, change in position, locked, delay).</summary>
        public readonly int[] In = new int[4];
        public readonly HaloTag Open, Close, Opened, Closed, Depowered, Repowered, DelayEffect;
        public readonly float DelayTime, ActivationRadius;
        // machines
        /// <summary>0 door, 1 platform, 2 gear.</summary>
        public readonly int MachineType;
        /// <summary>MachineFlags: bit 0 pathfinding obstacle, 1 but not when open, 2 elevator.</summary>
        public readonly uint MachineFlags;
        public readonly float DoorOpenTime;
        /// <summary>0 pause until crushed, 1 reverse directions.</summary>
        public readonly int CollisionResponse;
        public readonly int ElevatorNode;
        // controls
        /// <summary>0 toggle switch, 1 on button, 2 off button, 3 call button.</summary>
        public readonly int ControlType;
        /// <summary>0 touched by player, 1 destroyed.</summary>
        public readonly int TriggersWhen;
        public readonly float CallValue;
        public readonly HaloTag On, Off, Deny;

        public bool IsMachine => ObjectType == 7;
        public bool IsControl => ObjectType == 8;
        public bool IsDoor => IsMachine && MachineType == 0;
        public bool IsElevator => IsMachine && (MachineFlags & 4) != 0;
        public bool PositionLoops => (Flags & 1) != 0;

        public HaloDeviceDef(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            ObjectType = r.U16(0);
            Model = r.Ref(40); AnimationGraph = r.Ref(56); CollisionModel = r.Ref(112);
            Flags = r.U32(380);
            PowerTransition = r.F32(384); PowerAcceleration = r.F32(388);
            PositionTransition = r.F32(392); PositionAcceleration = r.F32(396);
            DepoweredTransition = r.F32(400); DepoweredAcceleration = r.F32(404);
            for (int i = 0; i < 4; i++) In[i] = r.U16(408 + 2 * i);
            Open = r.Ref(416); Close = r.Ref(432); Opened = r.Ref(448); Closed = r.Ref(464); Depowered = r.Ref(480); Repowered = r.Ref(496);
            DelayTime = r.F32(512); DelayEffect = r.Ref(524); ActivationRadius = r.F32(540);
            if (ObjectType == 7)
            {
                MachineType = r.U16(656); MachineFlags = r.U16(658); DoorOpenTime = r.F32(660); CollisionResponse = r.U16(744); ElevatorNode = r.I16(746);
            }
            else if (ObjectType == 8)
            {
                ControlType = r.U16(656); TriggersWhen = r.U16(658); CallValue = r.F32(660); On = r.Ref(744); Off = r.Ref(760); Deny = r.Ref(776);
            }
        }

        public override string ToString()
        {
            string kind = IsMachine ? (MachineType == 0 ? "door" : MachineType == 1 ? "platform" : "gear") + ((MachineFlags & 4) != 0 ? " elevator" : "")
                : IsControl ? (ControlType == 0 ? "toggle switch" : ControlType == 1 ? "on button" : ControlType == 2 ? "off button" : "call button") : "light fixture";
            return System.IO.Path.GetFileName(Tag.Name) + " (" + kind + ", position " + PositionTransition.ToString("0.##") + " s, power " + PowerTransition.ToString("0.##") + " s"
                + (ActivationRadius > 0f ? ", auto radius " + ActivationRadius.ToString("0.##") : "") + (IsDoor ? ", open " + DoorOpenTime.ToString("0.##") + " s" : "") + ")";
        }
    }
}

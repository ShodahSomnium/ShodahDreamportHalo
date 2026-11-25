using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The scenario tag (scnr) of a map: structure BSPs (with their own file offset / address base), skies, placed objects
    /// (scenery, vehicles, weapons, equipment, machines, controls, light fixtures, sound scenery) with their palettes, player starting
    /// locations, netgame flags (CTF flags, oddball, race, teleporters, hills), netgame equipment (weapon / powerup spawns of item
    /// collections) and starting equipment. Offsets: DevTools/reference/layouts.txt (struct Scenario). Positions stay in Halo units /
    /// space (HaloSpace converts). Unity-free.
    /// </summary>
    public sealed class HaloScenario
    {
        /// <summary>Game types as the scenario stores them (ScenarioSpawnType): 0 none, 1 ctf, 2 slayer, 3 oddball, 4 king, 5 race,
        /// 6 terminator, 12 all games, 13 all except ctf, 14 all except race and ctf.</summary>
        public enum GameType { None = 0, Ctf = 1, Slayer = 2, Oddball = 3, King = 4, Race = 5, Terminator = 6, All = 12, AllButCtf = 13, AllButRaceCtf = 14 }

        /// <summary>Netgame flag kinds (ScenarioNetgameFlagType).</summary>
        public enum FlagType { CtfFlag = 0, CtfVehicle = 1, OddballSpawn = 2, RaceTrack = 3, RaceVehicle = 4, VegasBank = 5, TeleportFrom = 6, TeleportTo = 7, HillFlag = 8 }

        public sealed class Bsp { public int FileOffset, Size; public uint Address; public HaloTag Tag; }

        public sealed class Placement
        {
            /// <summary>Palette entry (object definition tag), resolved.</summary>
            public HaloTag Type;
            public int PaletteIndex, NameIndex, DesiredPermutation;
            public ushort NotPlaced;
            public HVec3 Position;
            /// <summary>Yaw, pitch, roll in radians (Halo).</summary>
            public HVec3 Rotation;
            /// <summary>Kind-specific extras.</summary>
            public int Team = -1; public ushort MpSpawnFlags; public int RoundsLoaded, RoundsReserved; public uint Flags;
            public float LightIntensity; public HVec3 LightColor; public float LightFalloff, LightCutoff;
            public override string ToString() => (Type != null ? Type.Name : "?") + " @" + Position;
        }

        public sealed class StartLocation
        {
            public HVec3 Position; public float Facing; public int Team, BspIndex;
            public readonly GameType[] Types = new GameType[4];
            public bool Allows(GameType g) => HaloScenario.Allows(Types, g);
        }

        public sealed class NetgameFlag { public HVec3 Position; public float Facing; public FlagType Type; public int UsageId; public HaloTag WeaponGroup; }

        public sealed class NetgameEquipment
        {
            public uint Flags; public readonly GameType[] Types = new GameType[4]; public int Team; public int SpawnTime;
            public HVec3 Position; public float Facing; public HaloTag ItemCollection;
            public bool Levitate => (Flags & 1) != 0;
            public bool Allows(GameType g) => HaloScenario.Allows(Types, g);
        }

        public sealed class StartingEquipment
        {
            public uint Flags; public readonly GameType[] Types = new GameType[4]; public readonly HaloTag[] Collections = new HaloTag[6];
            public bool NoGrenades => (Flags & 1) != 0;
            public bool PlasmaGrenadesOnly => (Flags & 2) != 0;
            public bool Allows(GameType g) => HaloScenario.Allows(Types, g);
        }

        public readonly HaloTag Tag;
        public readonly List<Bsp> Bsps = new List<Bsp>();
        public readonly List<HaloTag> Skies = new List<HaloTag>();
        public readonly List<Placement> Scenery = new List<Placement>(), Vehicles = new List<Placement>(), Weapons = new List<Placement>(),
            Equipment = new List<Placement>(), Machines = new List<Placement>(), Controls = new List<Placement>(),
            LightFixtures = new List<Placement>(), SoundScenery = new List<Placement>();
        public readonly List<StartLocation> StartLocations = new List<StartLocation>();
        public readonly List<NetgameFlag> Flags = new List<NetgameFlag>();
        public readonly List<NetgameEquipment> NetEquipment = new List<NetgameEquipment>();
        public readonly List<StartingEquipment> Starting = new List<StartingEquipment>();
        public readonly List<string> ObjectNames = new List<string>();

        public HaloScenario(HaloMap map) : this(map.Scenario) { }

        public HaloScenario(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            foreach (var s in r.Block(48, 16)) { var sky = s.Ref(0); if (sky != null) Skies.Add(sky); }
            foreach (var n in r.Block(516, 36)) ObjectNames.Add(n.Map.Str(n.At, 32));
            Read(r, 528, 72, 540, Scenery, 0);
            Read(r, 576, 120, 588, Vehicles, 1);
            Read(r, 600, 40, 612, Equipment, 3);
            Read(r, 624, 92, 636, Weapons, 2);
            Read(r, 660, 64, 672, Machines, 7);
            Read(r, 684, 64, 696, Controls, 8);
            Read(r, 708, 88, 720, LightFixtures, 9);
            Read(r, 732, 40, 744, SoundScenery, 11);
            foreach (var s in r.Block(852, 52))
            {
                var l = new StartLocation { Position = s.Vec3(0), Facing = s.F32(12), Team = s.I16(16), BspIndex = s.I16(18) };
                for (int i = 0; i < 4; i++) l.Types[i] = (GameType)s.U16(20 + 2 * i);
                StartLocations.Add(l);
            }
            foreach (var s in r.Block(888, 148))
                Flags.Add(new NetgameFlag { Position = s.Vec3(0), Facing = s.F32(12), Type = (FlagType)s.U16(16), UsageId = s.I16(18), WeaponGroup = s.Ref(20) });
            foreach (var s in r.Block(900, 144))
            {
                var e = new NetgameEquipment { Flags = s.U32(0), Team = s.I16(12), SpawnTime = s.I16(14), Position = s.Vec3(64), Facing = s.F32(76), ItemCollection = s.Ref(80) };
                for (int i = 0; i < 4; i++) e.Types[i] = (GameType)s.U16(4 + 2 * i);
                NetEquipment.Add(e);
            }
            foreach (var s in r.Block(912, 204))
            {
                var e = new StartingEquipment { Flags = s.U32(0) };
                for (int i = 0; i < 4; i++) e.Types[i] = (GameType)s.U16(4 + 2 * i);
                for (int i = 0; i < 6; i++) e.Collections[i] = s.Ref(60 + 16 * i);
                Starting.Add(e);
            }
            foreach (var s in r.Block(1444, 32))
                Bsps.Add(new Bsp { FileOffset = s.I32(0), Size = s.I32(4), Address = s.U32(8), Tag = s.Ref(16) });
        }

        void Read(TagReader r, int blockAt, int stride, int paletteAt, List<Placement> into, int kind)
        {
            var palette = new List<HaloTag>();
            foreach (var p in r.Block(paletteAt, 48)) palette.Add(p.Ref(0));
            foreach (var s in r.Block(blockAt, stride))
            {
                int type = s.I16(0);
                var pl = new Placement
                {
                    PaletteIndex = type, Type = type >= 0 && type < palette.Count ? palette[type] : null, NameIndex = s.I16(2),
                    NotPlaced = s.U16(4), DesiredPermutation = s.I16(6), Position = s.Vec3(8), Rotation = s.Vec3(20),
                };
                switch (kind)
                {
                    case 1: pl.Team = s.I8(88); pl.MpSpawnFlags = s.U16(90); pl.Flags = s.U32(76); break;
                    case 2: pl.RoundsReserved = s.I16(72); pl.RoundsLoaded = s.I16(74); pl.Flags = s.U16(76); break;
                    case 3: pl.Flags = s.U16(34); break;
                    case 7: case 8: pl.Flags = s.U32(44); break;
                    case 9: pl.Flags = s.U32(44); pl.LightColor = s.Vec3(48); pl.LightIntensity = s.F32(60); pl.LightFalloff = s.F32(64); pl.LightCutoff = s.F32(68); break;
                }
                into.Add(pl);
            }
        }

        /// <summary>The game type set of a spawn / item (four slots; "none" in all four = every game type).</summary>
        public static bool Allows(GameType[] types, GameType g)
        {
            bool any = false;
            foreach (var t in types)
            {
                if (t == GameType.None) continue;
                any = true;
                if (t == g || t == GameType.All) return true;
                if (t == GameType.AllButCtf && g != GameType.Ctf) return true;
                if (t == GameType.AllButRaceCtf && g != GameType.Ctf && g != GameType.Race) return true;
            }
            return !any;
        }

        /// <summary>A reader for a structure BSP's tag data: BSPs carry their own address base (address - file offset).</summary>
        public TagReader BspRoot(int index)
        {
            var b = Bsps[index];
            long magic = (long)b.Address - b.FileOffset;
            // the BSP data starts with a 16-byte header: u32 pointer to the sbsp tag data, ...
            var head = new TagReader(Tag.Map, b.FileOffset, magic);
            uint tagData = head.U32(0);
            return new TagReader(Tag.Map, head.Off(tagData), magic);
        }
    }
}

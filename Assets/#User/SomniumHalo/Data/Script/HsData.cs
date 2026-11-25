using System;
using System.Collections.Generic;
using System.Text;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>HaloScript value types (scenario "script value type" enum, RIAT value_type.rs order).</summary>
    public enum HsType : ushort
    {
        Unparsed, SpecialForm, FunctionName, Passthrough, Void, Boolean, Real, Short, Long, String, Script, TriggerVolume, CutsceneFlag,
        CutsceneCameraPoint, CutsceneTitle, CutsceneRecording, DeviceGroup, Ai, AiCommandList, StartingProfile, Conversation, Navpoint,
        HudMessage, ObjectList, Sound, Effect, Damage, LoopingSound, AnimationGraph, ActorVariant, DamageEffect, ObjectDefinition,
        GameDifficulty, Team, AiDefaultState, ActorType, HudCorner, Object, Unit, Vehicle, Weapon, Device, Scenery, ObjectName, UnitName,
        VehicleName, WeaponName, DeviceName, SceneryName, Count
    }

    public enum HsScriptType : ushort { Startup = 0, Dormant = 1, Continuous = 2, Static = 3, Stub = 4 }

    /// <summary>One compiled HaloScript node (scenario script syntax data, 20 bytes).
    /// salt +0, index union +2 (function / script index; for a constant its type), type +4, flags +6, next node id +8, string offset +12,
    /// data +16 (function call: id of its function-name node = first child; constants: bool / short / long / real / tag id / block index;
    /// global: global index; local: parameter index).</summary>
    public struct HsNode
    {
        public ushort Salt, Index;
        public HsType Type;
        public ushort Flags;
        /// <summary>Next sibling node index (-1 = none).</summary>
        public int Next;
        public int StringOffset;
        public uint Data;

        public const ushort FlagPrimitive = 1, FlagScriptCall = 2, FlagGlobal = 4, FlagGarbage = 8, FlagLocal = 16;
        public bool IsPrimitive => (Flags & FlagPrimitive) != 0;
        public bool IsScriptCall => (Flags & FlagScriptCall) != 0;
        public bool IsGlobal => (Flags & FlagGlobal) != 0;
        public bool IsLocal => (Flags & FlagLocal) != 0;
        /// <summary>Function call: index of its first child (the function-name node).</summary>
        public int Child => (Data & 0xFFFF) == 0xFFFF ? -1 : (int)(Data & 0xFFFF);
        public bool Bool => (Data & 0xFF) != 0;
        public short Short => (short)(Data & 0xFFFF);
        public int Long => (int)Data;
        public float Real => BitConverter.ToSingle(BitConverter.GetBytes(Data), 0);
    }

    /// <summary>A scenario trigger volume (oriented box): forward +48, up +60, starting corner +72, ending corner offset +84 (Halo units).</summary>
    public struct HsVolume
    {
        public HVec3 Forward, Up, Corner, Extent;
        /// <summary>Halo's point test: relative to the starting corner, projected on forward / left (up x forward) / up, between 0 and the
        /// ending corner offset on every axis.</summary>
        public bool Contains(float x, float y, float z)
        {
            float dx = x - Corner.X, dy = y - Corner.Y, dz = z - Corner.Z;
            float lx = Up.Y * Forward.Z - Up.Z * Forward.Y, ly = Up.Z * Forward.X - Up.X * Forward.Z, lz = Up.X * Forward.Y - Up.Y * Forward.X;
            return Between(dx * Forward.X + dy * Forward.Y + dz * Forward.Z, Extent.X) && Between(dx * lx + dy * ly + dz * lz, Extent.Y)
                && Between(dx * Up.X + dy * Up.Y + dz * Up.Z, Extent.Z);
        }
        static bool Between(float v, float e) { return e >= 0f ? v >= 0f && v <= e : v <= 0f && v >= e; }
    }

    public sealed class HsScript
    {
        public string Name;
        public HsScriptType Type;
        public HsType Returns;
        public int Root;
        public string[] ParamNames = new string[0];
        public HsType[] ParamTypes = new HsType[0];
        public override string ToString() => Name;
    }

    public sealed class HsGlobal
    {
        public string Name;
        public HsType Type;
        public int Init;
        public override string ToString() => Name;
    }

    /// <summary>
    /// [Script module, S] A campaign scenario's compiled HaloScript, read from the scenario tag: script syntax data (+1140: a "script node"
    /// data array: 56-byte header, then 20-byte nodes), script string data (+1160), scripts (+1180, 92 B), globals (+1192, 92 B),
    /// references (+1204) plus the scenario blocks the constants index (object names, trigger volumes, cutscene flags / cameras / titles,
    /// recorded animations, device groups, encounters / squads, command lists, starting profiles, conversations).
    /// Unity-free (harness: dotnet run -- hsc a10).
    /// </summary>
    public sealed class HsData
    {
        public HsNode[] Nodes = new HsNode[0];
        public byte[] Strings = new byte[0];
        public HsScript[] Scripts = new HsScript[0];
        public HsGlobal[] Globals = new HsGlobal[0];
        /// <summary>Scenario blocks the constant nodes index (short data = element index).</summary>
        public string[] ObjectNames = new string[0], TriggerVolumes = new string[0], CutsceneFlags = new string[0], CutsceneCameras = new string[0],
            CutsceneTitles = new string[0], Recordings = new string[0], DeviceGroups = new string[0], CommandLists = new string[0],
            StartingProfiles = new string[0], Conversations = new string[0], Encounters = new string[0];
        public string[][] Squads = new string[0][], Platoons = new string[0][];
        /// <summary>Cutscene flags: position (Halo units) and facing yaw / pitch (radians); trigger volume boxes; cutscene camera points.</summary>
        public HVec3[] FlagPositions = new HVec3[0], CameraPositions = new HVec3[0];
        public float[] FlagYaw = new float[0], FlagPitch = new float[0];
        public HsVolume[] Volumes = new HsVolume[0];
        public HaloMap Map;
        public HaloTag Scenario;
        /// <summary>Problems met while reading (bad ids, out of range offsets).</summary>
        public readonly List<string> Problems = new List<string>();

        readonly Dictionary<int, string> _strCache = new Dictionary<int, string>();

        public static HsData Read(HaloMap map) { return Read(map, map.Scenario); }

        public static HsData Read(HaloMap map, HaloTag scenario)
        {
            var d = new HsData { Map = map, Scenario = scenario };
            if (scenario == null) { d.Problems.Add("no scenario"); return d; }
            var r = scenario.Root;
            byte[] syn = r.DataBytes(1140);
            d.Strings = r.DataBytes(1160);
            if (syn.Length >= 56)
            {
                int elem = BitConverter.ToUInt16(syn, 34);
                int max = BitConverter.ToUInt16(syn, 32);
                if (elem != 20) d.Problems.Add("node size " + elem);
                int count = Math.Min(max, (syn.Length - 56) / 20);
                d.Nodes = new HsNode[count];
                for (int i = 0; i < count; i++)
                {
                    int o = 56 + i * 20;
                    uint next = BitConverter.ToUInt32(syn, o + 8);
                    d.Nodes[i] = new HsNode
                    {
                        Salt = BitConverter.ToUInt16(syn, o),
                        Index = BitConverter.ToUInt16(syn, o + 2),
                        Type = (HsType)BitConverter.ToUInt16(syn, o + 4),
                        Flags = BitConverter.ToUInt16(syn, o + 6),
                        Next = next == 0xFFFFFFFF ? -1 : (int)(next & 0xFFFF),
                        StringOffset = BitConverter.ToInt32(syn, o + 12),
                        Data = BitConverter.ToUInt32(syn, o + 16),
                    };
                }
            }
            else d.Problems.Add("no script syntax data");

            var scripts = new List<HsScript>();
            foreach (var s in r.Block(1180, 92))
            {
                var hs = new HsScript { Name = s.Str32(0), Type = (HsScriptType)s.U16(32), Returns = (HsType)s.U16(34) };
                uint root = s.U32(36);
                hs.Root = root == 0xFFFFFFFF ? -1 : (int)(root & 0xFFFF);
                int pc = s.Count(80);
                if (pc > 0 && pc <= 16)
                {
                    hs.ParamNames = new string[pc]; hs.ParamTypes = new HsType[pc];
                    int k = 0;
                    foreach (var p in s.Block(80, 36)) { hs.ParamNames[k] = p.Str32(0); hs.ParamTypes[k] = (HsType)p.U16(32); k++; }
                }
                scripts.Add(hs);
            }
            d.Scripts = scripts.ToArray();
            var globals = new List<HsGlobal>();
            foreach (var g in r.Block(1192, 92))
            {
                uint init = g.U32(40);
                globals.Add(new HsGlobal { Name = g.Str32(0), Type = (HsType)g.U16(32), Init = init == 0xFFFFFFFF ? -1 : (int)(init & 0xFFFF) });
            }
            d.Globals = globals.ToArray();

            d.ObjectNames = Names(r, 516, 36, 0);
            d.TriggerVolumes = Names(r, 864, 96, 4);
            d.CutsceneFlags = Names(r, 1252, 92, 4);
            d.CutsceneCameras = Names(r, 1264, 104, 4);
            d.CutsceneTitles = Names(r, 1276, 96, 4);
            d.Recordings = Names(r, 876, 64, 0);
            d.DeviceGroups = Names(r, 648, 52, 0);
            d.CommandLists = Names(r, 1080, 96, 0);
            d.StartingProfiles = Names(r, 840, 104, 0);
            d.Conversations = Names(r, 1128, 116, 0);
            d.Encounters = Names(r, 1068, 176, 0);
            int nf = d.CutsceneFlags.Length;
            d.FlagPositions = new HVec3[nf]; d.FlagYaw = new float[nf]; d.FlagPitch = new float[nf];
            for (int i = 0; i < nf; i++) { var e = r.Element(1252, i, 92); d.FlagPositions[i] = e.Vec3(36); d.FlagYaw[i] = e.F32(48); d.FlagPitch[i] = e.F32(52); }
            int nc = d.CutsceneCameras.Length;
            d.CameraPositions = new HVec3[nc];
            for (int i = 0; i < nc; i++) d.CameraPositions[i] = r.Element(1264, i, 104).Vec3(40);
            int nv = d.TriggerVolumes.Length;
            d.Volumes = new HsVolume[nv];
            for (int i = 0; i < nv; i++) { var e = r.Element(864, i, 96); d.Volumes[i] = new HsVolume { Forward = e.Vec3(48), Up = e.Vec3(60), Corner = e.Vec3(72), Extent = e.Vec3(84) }; }
            int ne = d.Encounters.Length;
            d.Squads = new string[ne][]; d.Platoons = new string[ne][];
            for (int i = 0; i < ne; i++)
            {
                var e = r.Element(1068, i, 176);
                d.Squads[i] = Names(e, 128, 232, 0);
                d.Platoons[i] = Names(e, 140, 172, 0);
            }
            return d;
        }

        static string[] Names(TagReader r, int block, int stride, int nameAt)
        {
            int n = r.Count(block);
            if (n <= 0 || n > 100000) return new string[0];
            var a = new string[n];
            int i = 0;
            foreach (var e in r.Block(block, stride)) a[i++] = e.Str32(nameAt);
            return a;
        }

        public bool Valid(int node) => node >= 0 && node < Nodes.Length;

        /// <summary>The C string at a script string data offset ("" when out of range).</summary>
        public string Str(int offset)
        {
            if (offset < 0 || offset >= Strings.Length) return "";
            string s;
            if (_strCache.TryGetValue(offset, out s)) return s;
            int e = offset; while (e < Strings.Length && Strings[e] != 0) e++;
            s = Encoding.ASCII.GetString(Strings, offset, e - offset);
            _strCache[offset] = s;
            return s;
        }

        public int ScriptIndex(string name)
        {
            for (int i = 0; i < Scripts.Length; i++) if (string.Equals(Scripts[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public int GlobalIndex(string name)
        {
            for (int i = 0; i < Globals.Length; i++) if (string.Equals(Globals[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The name of a function call node: the called script, or the engine function as the map's own string data names it
        /// (the function-name child node). The index union is NOT reliable for engine functions: the ten campaign maps were compiled
        /// with different function tables (a10's cinematic_skip_start_internal is 302, other maps 296 / 298; RIAT's gbx-retail says 298),
        /// so the VM binds by this name. Falls back to the RIAT table when the string is missing.</summary>
        public string CallName(int node)
        {
            if (!Valid(node)) return "?";
            var n = Nodes[node];
            if (n.IsScriptCall) return n.Index < Scripts.Length ? Scripts[n.Index].Name : "script_" + n.Index;
            int nameNode = n.Child;
            if (Valid(nameNode))
            {
                string s = Str(Nodes[nameNode].StringOffset);
                if (s.Length > 0) return s;
            }
            return HsDefs.FunctionName(n.Index);
        }

        /// <summary>Halo's ai value: encounter index in the low 16 bits; bit 31 = squad (index in bits 16..23), bit 30 = platoon.
        /// (Checked against the decompiled names: see S/NOTES.md.)</summary>
        public string AiName(uint v)
        {
            if (v == 0xFFFFFFFF) return "none";
            int enc = (int)(v & 0xFFFF);
            string e = enc < Encounters.Length ? Encounters[enc] : "encounter_" + enc;
            int sub = (int)((v >> 16) & 0xFF);
            if ((v & 0x80000000u) != 0) return e + "/" + (enc < Squads.Length && sub < Squads[enc].Length ? Squads[enc][sub] : "squad_" + sub);
            if ((v & 0x40000000u) != 0) return e + "/" + (enc < Platoons.Length && sub < Platoons[enc].Length ? Platoons[enc][sub] : "platoon_" + sub);
            return e;
        }

        public static string At(string[] a, int i) => i >= 0 && i < a.Length ? a[i] : null;
    }

    public static partial class HsDefs
    {
        public static string FunctionName(int index) => index >= 0 && index < FunctionNames.Length && FunctionNames[index] != null ? FunctionNames[index] : "function_" + index;

        static Dictionary<string, int> _fnByName, _glByName;

        public static int FunctionIndex(string name)
        {
            if (_fnByName == null)
            {
                var d = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < FunctionNames.Length; i++) if (FunctionNames[i] != null && !d.ContainsKey(FunctionNames[i])) d[FunctionNames[i]] = i;
                _fnByName = d;
            }
            int v; return name != null && _fnByName.TryGetValue(name, out v) ? v : -1;
        }

        public static int GlobalIndex(string name)
        {
            if (_glByName == null)
            {
                var d = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < GlobalNames.Length; i++) if (GlobalNames[i] != null && !d.ContainsKey(GlobalNames[i])) d[GlobalNames[i]] = i;
                _glByName = d;
            }
            int v; return name != null && _glByName.TryGetValue(name, out v) ? v : -1;
        }

        static readonly string[] TypeNames = { "unparsed", "special_form", "function_name", "passthrough", "void", "boolean", "real", "short", "long", "string", "script", "trigger_volume", "cutscene_flag", "cutscene_camera_point", "cutscene_title", "cutscene_recording", "device_group", "ai", "ai_command_list", "starting_profile", "conversation", "navpoint", "hud_message", "object_list", "sound", "effect", "damage", "looping_sound", "animation_graph", "actor_variant", "damage_effect", "object_definition", "game_difficulty", "team", "ai_default_state", "actor_type", "hud_corner", "object", "unit", "vehicle", "weapon", "device", "scenery", "object_name", "unit_name", "vehicle_name", "weapon_name", "device_name", "scenery_name" };
        public static string TypeName(HsType t) => (int)t < TypeNames.Length ? TypeNames[(int)t] : "type_" + (int)t;
    }
}

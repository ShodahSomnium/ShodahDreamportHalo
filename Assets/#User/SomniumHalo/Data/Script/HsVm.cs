using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>An engine function bound by name (the Game side fills <see cref="HsVm.Natives"/>). Returns the value in the function's
    /// own return type (HsDefs): bool 0/1, short/long as int, real as float bits (<see cref="HsVm.R"/>), objects as handles (-1 none).</summary>
    public delegate int HsNative(HsArgs a);

    /// <summary>The evaluated arguments of an engine function call, each already in its argument node's type.</summary>
    public struct HsArgs
    {
        public readonly HsVm Vm;
        readonly HsThread _t;
        readonly int _base;
        public readonly int Count;
        /// <summary>The function id (RIAT gbx-retail index of its name) and the call node.</summary>
        public readonly int Fn, Node;

        internal HsArgs(HsVm vm, HsThread t, int b, int count, int fn, int node) { Vm = vm; _t = t; _base = b; Count = count; Fn = fn; Node = node; }

        public bool Has(int i) => i >= 0 && i < Count;
        public HsType Type(int i) => Has(i) ? Vm.D.Nodes[_t.ArgNodes[_base + i]].Type : HsType.Void;
        public int Raw(int i) => Has(i) ? _t.Values[_base + i] : 0;
        public int Int(int i) { if (!Has(i)) return 0; int v = _t.Values[_base + i]; return Type(i) == HsType.Real ? (int)HsVm.F(v) : v; }
        public float Real(int i) { if (!Has(i)) return 0f; int v = _t.Values[_base + i]; return Type(i) == HsType.Real ? HsVm.F(v) : v; }
        public bool Bool(int i) => Has(i) && _t.Values[_base + i] != 0;
        /// <summary>Object handle (-1 none). Object names are handles already (= their object name index).</summary>
        public int Obj(int i) => Has(i) ? _t.Values[_base + i] : -1;
        /// <summary>String constant / tag path / name: the text the compiler kept for a constant argument, else the resolved name.</summary>
        public string Str(int i)
        {
            if (!Has(i)) return "";
            var n = Vm.D.Nodes[_t.ArgNodes[_base + i]];
            if (n.Type == HsType.String) return Vm.D.Str(_t.Values[_base + i]);
            string s = Vm.NameOf(n.Type, _t.Values[_base + i]);
            // hud messages, navpoints, enums...: the name the compiler kept on the constant
            if (s.Length == 0 && n.IsPrimitive && !n.IsGlobal && !n.IsLocal) s = Vm.D.Str(n.StringOffset);
            return s;
        }
        /// <summary>Tag of a tag-typed argument (sound, effect, damage, looping sound, animation graph, actor variant, object definition).</summary>
        public HaloTag Tag(int i) => Has(i) && Vm.D.Map != null ? Vm.D.Map.Tag((uint)_t.Values[_base + i]) : null;
        public int ArgNode(int i) => Has(i) ? _t.ArgNodes[_base + i] : -1;
        public string FunctionName => Vm.FunctionName(Fn);
    }

    public enum HsThreadState : byte { Asleep, Running, Dead }

    /// <summary>One script thread (startup / dormant / continuous script): an explicit evaluation stack so a sleep anywhere (also inside a
    /// called static script) suspends it.</summary>
    public sealed class HsThread
    {
        public int Script;
        public HsScriptType Type;
        public HsThreadState State;
        /// <summary>Runs again when the VM tick reaches this (int.MaxValue = asleep until woken).</summary>
        public int WakeTick;
        public int Starts, Sleeps, Wakes, Finished;
        internal HsFrame[] Frames = new HsFrame[48];
        internal int Depth;
        internal int[] Values = new int[128], ArgNodes = new int[128];
        internal int ValueTop;
        internal int Result; internal bool HasResult;
        public bool Started => Starts > 0;
        public bool Sleeping => State == HsThreadState.Asleep;
    }

    /// <summary>A checkpoint copy of the script state (HsVm.Capture / Restore).</summary>
    public sealed class HsSnapshot
    {
        public bool Filled;
        public int Tick;
        public int[] Globals, Externals;
        internal ThreadState[] Threads;
        internal sealed class ThreadState
        {
            public HsThreadState State; public int WakeTick, Depth, ValueTop, Result, Starts; public bool HasResult;
            public HsFrame[] Frames; public int[] Values, ArgNodes;
        }

        const int Magic = 0x31534853;   // "SHS1"

        /// <summary>Compact bytes (host hand-over): a header (node / script / global counts, to refuse another map's state), tick,
        /// globals, engine globals, then each thread's state, wake tick, result, starts, its value stack and its frames.
        /// Object-list handles inside values point into the old host's list pool and read as other / empty lists (they are short-lived).</summary>
        public byte[] ToBytes(HsData d)
        {
            if (!Filled) return null;
            var ms = new System.IO.MemoryStream(4096);
            var w = new System.IO.BinaryWriter(ms);
            w.Write(Magic); w.Write(d.Nodes.Length); w.Write(d.Scripts.Length); w.Write(Globals.Length);
            w.Write(Tick);
            for (int i = 0; i < Globals.Length; i++) w.Write(Globals[i]);
            w.Write(Externals.Length); for (int i = 0; i < Externals.Length; i++) w.Write(Externals[i]);
            w.Write(Threads.Length);
            for (int i = 0; i < Threads.Length; i++)
            {
                var t = Threads[i];
                w.Write((byte)t.State); w.Write(t.WakeTick); w.Write(t.Result); w.Write(t.HasResult); w.Write(t.Starts);
                w.Write(t.ValueTop); for (int k = 0; k < t.ValueTop; k++) { w.Write(t.Values[k]); w.Write(t.ArgNodes[k]); }
                w.Write(t.Depth);
                for (int k = 0; k < t.Depth; k++)
                {
                    var f = t.Frames[k];
                    w.Write(f.Node); w.Write(f.Phase); w.Write(f.Arg); w.Write(f.ValueBase); w.Write(f.Count); w.Write(f.Aux); w.Write(f.Aux2); w.Write(f.Local); w.Write(f.Mask);
                }
            }
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>Back from <see cref="ToBytes"/>; null when the bytes are not this map's script state (or broken).</summary>
        public static HsSnapshot FromBytes(byte[] b, HsData d, out string why)
        {
            why = null;
            if (b == null || b.Length < 24) { why = "no bytes"; return null; }
            try
            {
                var r = new System.IO.BinaryReader(new System.IO.MemoryStream(b));
                if (r.ReadInt32() != Magic) { why = "not a script state"; return null; }
                int nodes = r.ReadInt32(), scripts = r.ReadInt32(), globals = r.ReadInt32();
                if (nodes != d.Nodes.Length || scripts != d.Scripts.Length || globals != d.Globals.Length) { why = "another map's scripts"; return null; }
                var s = new HsSnapshot { Tick = r.ReadInt32(), Globals = new int[globals] };
                for (int i = 0; i < globals; i++) s.Globals[i] = r.ReadInt32();
                int ne = r.ReadInt32(); if (ne < 0 || ne > 4096) { why = "bad engine globals"; return null; }
                s.Externals = new int[ne]; for (int i = 0; i < ne; i++) s.Externals[i] = r.ReadInt32();
                int nt = r.ReadInt32(); if (nt < 0 || nt > 4096) { why = "bad threads"; return null; }
                s.Threads = new ThreadState[nt];
                for (int i = 0; i < nt; i++)
                {
                    var t = new ThreadState { State = (HsThreadState)r.ReadByte(), WakeTick = r.ReadInt32(), Result = r.ReadInt32(), HasResult = r.ReadBoolean(), Starts = r.ReadInt32() };
                    t.ValueTop = r.ReadInt32(); if (t.ValueTop < 0 || t.ValueTop > 1 << 16) { why = "bad stack"; return null; }
                    t.Values = new int[t.ValueTop]; t.ArgNodes = new int[t.ValueTop];
                    for (int k = 0; k < t.ValueTop; k++) { t.Values[k] = r.ReadInt32(); t.ArgNodes[k] = r.ReadInt32(); }
                    t.Depth = r.ReadInt32(); if (t.Depth < 0 || t.Depth > 1 << 14) { why = "bad frames"; return null; }
                    t.Frames = new HsFrame[t.Depth];
                    for (int k = 0; k < t.Depth; k++)
                        t.Frames[k] = new HsFrame { Node = r.ReadInt32(), Phase = r.ReadInt32(), Arg = r.ReadInt32(), ValueBase = r.ReadInt32(), Count = r.ReadInt32(), Aux = r.ReadInt32(), Aux2 = r.ReadInt32(), Local = r.ReadInt32(), Mask = r.ReadUInt64() };
                    s.Threads[i] = t;
                }
                s.Filled = true;
                return s;
            }
            catch (Exception e) { why = e.GetType().Name; return null; }
        }
    }

    internal struct HsFrame
    {
        public int Node, Phase, Arg, ValueBase, Count, Aux, Aux2, Local;
        public ulong Mask;
    }

    /// <summary>
    /// [Script module, S] The HaloScript virtual machine (Unity-free): runs a scenario's compiled scripts (HsData) at Halo's fixed 30 ticks
    /// a second. Startup scripts run once from the first tick, continuous scripts every tick, dormant scripts sleep until woken, static
    /// scripts are called. Special forms (begin, begin_random, if, and, or, set, sleep, sleep_until, wake, arithmetic, comparisons,
    /// script calls) are done here; every other function goes to <see cref="Natives"/> by name (bound by the Game side). Never throws on a
    /// bad node: a bad node evaluates to the type's default and is logged once.
    /// </summary>
    public sealed class HsVm
    {
        public readonly HsData D;
        /// <summary>Engine functions by function id (= RIAT gbx-retail index of the name). Null = unimplemented (logged once, default).</summary>
        public readonly HsNative[] Natives = new HsNative[HsDefs.FunctionNames.Length];
        /// <summary>Calls per function id since start.</summary>
        public readonly int[] Calls = new int[HsDefs.FunctionNames.Length];
        /// <summary>Uses (call nodes) per function id in this map's scripts.</summary>
        public readonly int[] Uses = new int[HsDefs.FunctionNames.Length];
        public readonly int[] Globals;
        public readonly HsThread[] Threads;
        /// <summary>Thread by script index (null for static / stub scripts).</summary>
        public readonly HsThread[] ThreadOf;
        public int Tick { get; private set; }
        public Action<string> Log = delegate { };
        /// <summary>Optional trace of thread events (start / sleep / wake / finish) for the lab and harness; null = off.</summary>
        public Action<string> Trace;
        /// <summary>Engine (external) globals: value per slot, name per slot.</summary>
        public readonly List<string> ExternalNames = new List<string>();
        public readonly List<int> ExternalValues = new List<int>();
        public readonly List<HsType> ExternalTypes = new List<HsType>();
        public int OpsPerThreadTick = 20000;
        public readonly Random Rng = new Random();

        // per node: function id for engine calls (-1 unknown name), special form kind; external global slot for global nodes
        readonly short[] _fn;
        readonly byte[] _special;
        readonly short[] _ext;
        readonly bool[] _loggedNode;
        readonly bool[] _loggedFn = new bool[HsDefs.FunctionNames.Length];
        int _unknownNames;
        public readonly List<string> UnknownFunctions = new List<string>();

        const byte SpNone = 0, SpBegin = 1, SpBeginRandom = 2, SpIf = 3, SpAnd = 4, SpOr = 5, SpSet = 6, SpSleep = 7, SpSleepUntil = 8, SpWake = 9,
            SpAdd = 10, SpSub = 11, SpMul = 12, SpDiv = 13, SpMin = 14, SpMax = 15, SpEq = 16, SpNe = 17, SpGt = 18, SpLt = 19, SpGe = 20, SpLe = 21,
            SpNot = 22, SpScript = 23;

        public HsVm(HsData d)
        {
            D = d;
            int n = d.Nodes.Length;
            _fn = new short[n]; _special = new byte[n]; _ext = new short[n]; _loggedNode = new bool[n];
            for (int i = 0; i < n; i++)
            {
                _fn[i] = -1; _ext[i] = -1;
                var nd = d.Nodes[i];
                if (nd.Salt == 0) continue;
                if (nd.IsPrimitive)
                {
                    if (nd.IsGlobal && (nd.Data & 0x8000) != 0) _ext[i] = (short)ExternalSlot(d.Str(nd.StringOffset), nd.Type);
                    continue;
                }
                if (nd.IsScriptCall) { _special[i] = SpScript; continue; }
                string name = d.CallName(i);
                int f = HsDefs.FunctionIndex(name);
                _fn[i] = (short)f;
                if (f < 0) { _unknownNames++; if (!UnknownFunctions.Contains(name)) UnknownFunctions.Add(name); continue; }
                Uses[f]++;
                _special[i] = SpecialOf(name);
            }
            Globals = new int[d.Globals.Length];
            ThreadOf = new HsThread[d.Scripts.Length];
            var threads = new List<HsThread>();
            for (int i = 0; i < d.Scripts.Length; i++)
            {
                var s = d.Scripts[i];
                if (s.Type != HsScriptType.Startup && s.Type != HsScriptType.Dormant && s.Type != HsScriptType.Continuous) continue;
                var t = new HsThread { Script = i, Type = s.Type };
                ThreadOf[i] = t; threads.Add(t);
            }
            Threads = threads.ToArray();
        }

        static byte SpecialOf(string name)
        {
            switch (name)
            {
                case "begin": return SpBegin;
                case "begin_random": return SpBeginRandom;
                case "if": return SpIf;
                case "and": return SpAnd;
                case "or": return SpOr;
                case "set": return SpSet;
                case "sleep": return SpSleep;
                case "sleep_until": return SpSleepUntil;
                case "wake": return SpWake;
                case "+": return SpAdd;
                case "-": return SpSub;
                case "*": return SpMul;
                case "/": return SpDiv;
                case "min": return SpMin;
                case "max": return SpMax;
                case "=": return SpEq;
                case "!=": return SpNe;
                case ">": return SpGt;
                case "<": return SpLt;
                case ">=": return SpGe;
                case "<=": return SpLe;
                case "not": return SpNot;
            }
            return SpNone;
        }

        int ExternalSlot(string name, HsType type)
        {
            int k = ExternalNames.IndexOf(name);
            if (k >= 0) return k;
            ExternalNames.Add(name); ExternalValues.Add(0);
            int gi = HsDefs.GlobalIndex(name);
            ExternalTypes.Add(gi >= 0 ? (HsType)HsDefs.GlobalTypes[gi] : type);
            return ExternalNames.Count - 1;
        }

        public string FunctionName(int fn) => HsDefs.FunctionName(fn);
        public int ExternalIndex(string name) => ExternalNames.IndexOf(name);
        public int UnknownFunctionNodes => _unknownNames;

        // ------------------------------------------------------------------ values

        public static float F(int bits) => BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        public static int R(float f) => BitConverter.ToInt32(BitConverter.GetBytes(f), 0);

        public static bool IsObjectType(HsType t) => t >= HsType.Object && t <= HsType.Scenery;
        public static bool IsObjectNameType(HsType t) => t >= HsType.ObjectName && t <= HsType.SceneryName;
        public static bool IsNumeric(HsType t) => t == HsType.Real || t == HsType.Short || t == HsType.Long;

        public static int Default(HsType t)
        {
            if (IsObjectType(t) || IsObjectNameType(t) || t == HsType.ObjectList || t == HsType.Ai) return -1;
            if (t >= HsType.Sound && t <= HsType.ObjectDefinition) return -1;
            return 0;
        }

        /// <summary>Converts a value between HaloScript types (numbers, object -> object list, names -> objects).</summary>
        public int Convert(int v, HsType from, HsType to)
        {
            if (from == to || to == HsType.Void || to == HsType.Passthrough || from == HsType.Passthrough) return v;
            if (to == HsType.Real) return from == HsType.Short || from == HsType.Long ? R(v) : v;
            if (to == HsType.Short) return from == HsType.Real ? (short)(int)F(v) : from == HsType.Long ? (short)v : v;
            if (to == HsType.Long) return from == HsType.Real ? (int)F(v) : v;
            if (to == HsType.ObjectList && (IsObjectType(from) || IsObjectNameType(from)))
            {
                int l = ListNew(); if (v != -1) ListAdd(l, v); return l;
            }
            if (to == HsType.Boolean && IsNumeric(from)) return from == HsType.Real ? (F(v) != 0f ? 1 : 0) : (v != 0 ? 1 : 0);
            return v;
        }

        /// <summary>The scenario name of an index-typed value (trigger volume, cutscene flag, object name, ai...).</summary>
        public string NameOf(HsType t, int v)
        {
            switch (t)
            {
                case HsType.TriggerVolume: return HsData.At(D.TriggerVolumes, v) ?? "";
                case HsType.CutsceneFlag: return HsData.At(D.CutsceneFlags, v) ?? "";
                case HsType.CutsceneCameraPoint: return HsData.At(D.CutsceneCameras, v) ?? "";
                case HsType.CutsceneTitle: return HsData.At(D.CutsceneTitles, v) ?? "";
                case HsType.CutsceneRecording: return HsData.At(D.Recordings, v) ?? "";
                case HsType.DeviceGroup: return HsData.At(D.DeviceGroups, v) ?? "";
                case HsType.AiCommandList: return HsData.At(D.CommandLists, v) ?? "";
                case HsType.StartingProfile: return HsData.At(D.StartingProfiles, v) ?? "";
                case HsType.Conversation: return HsData.At(D.Conversations, v) ?? "";
                case HsType.Ai: return AiName(v);
                case HsType.Script: return v >= 0 && v < D.Scripts.Length ? D.Scripts[v].Name : "";
                case HsType.String: return D.Str(v);
            }
            if (IsObjectNameType(t) || IsObjectType(t)) return v >= 0 && v < D.ObjectNames.Length ? D.ObjectNames[v] : "";
            if (t >= HsType.Sound && t <= HsType.ObjectDefinition) { var tag = D.Map != null ? D.Map.Tag((uint)v) : null; return tag != null ? tag.Name : ""; }
            return "";
        }

        readonly Dictionary<int, string> _aiNames = new Dictionary<int, string>();
        public string AiName(int v)
        {
            string s;
            if (_aiNames.TryGetValue(v, out s)) return s;
            s = D.AiName((uint)v); _aiNames[v] = s; return s;
        }

        // ------------------------------------------------------------------ object lists (pooled ring; handles = slot)

        const int ListSlots = 1024;
        readonly List<int>[] _lists = new List<int>[ListSlots];
        int _listNext;
        public int ListNew()
        {
            int h = _listNext; _listNext = (_listNext + 1) % ListSlots;
            var l = _lists[h]; if (l == null) _lists[h] = l = new List<int>(16); else l.Clear();
            return h;
        }
        public void ListAdd(int list, int obj) { if (list >= 0 && list < ListSlots && _lists[list] != null) _lists[list].Add(obj); }
        public int ListCount(int list) => list >= 0 && list < ListSlots && _lists[list] != null ? _lists[list].Count : 0;
        public int ListGet(int list, int i) => i >= 0 && i < ListCount(list) ? _lists[list][i] : -1;

        // ------------------------------------------------------------------ running

        public bool Started { get; private set; }

        /// <summary>Initialises the globals (their expressions in order) and readies the threads: startup and continuous run from the first
        /// tick, dormant ones sleep until woken.</summary>
        public void Start()
        {
            Tick = 0;
            var init = new HsThread { Script = -1, Type = HsScriptType.Static };
            for (int g = 0; g < D.Globals.Length; g++)
            {
                int node = D.Globals[g].Init;
                Globals[g] = Default(D.Globals[g].Type);
                if (!D.Valid(node)) continue;
                init.Depth = 0; init.ValueTop = 0; init.HasResult = false; init.State = HsThreadState.Running; init.WakeTick = 0;
                Push(init, node);
                Run(init);
                if (init.Depth == 0 && init.HasResult) Globals[g] = init.Result;
                else Log("Script: global " + D.Globals[g].Name + " initialiser did not finish (slept?)");
            }
            foreach (var t in Threads)
            {
                t.Depth = 0; t.ValueTop = 0; t.HasResult = false;
                if (t.Type == HsScriptType.Dormant) { t.State = HsThreadState.Asleep; t.WakeTick = int.MaxValue; }
                else { t.State = HsThreadState.Asleep; t.WakeTick = 0; }
            }
            Started = true;
        }

        /// <summary>A new host without any script state of the old one: globals initialised, the startup scripts counted as already run
        /// (they set the mission up once - it is up), continuous ones run, dormant ones wait to be woken; the clock starts at <paramref name="tick"/>.</summary>
        public void StartInPlace(int tick)
        {
            Start();
            Tick = tick < 0 ? 0 : tick;
            foreach (var t in Threads)
            {
                if (t.Type == HsScriptType.Startup) { t.State = HsThreadState.Dead; t.Starts = 1; }
                else if (t.Type == HsScriptType.Continuous) t.WakeTick = Tick;
            }
        }

        /// <summary>One game tick (1/30 s): every thread due runs until it sleeps or ends, in scenario order.</summary>
        public void Step()
        {
            if (!Started) return;
            for (int i = 0; i < Threads.Length; i++)
            {
                var t = Threads[i];
                if (t.State == HsThreadState.Dead || t.WakeTick > Tick) continue;
                if (t.Depth == 0)
                {
                    var s = D.Scripts[t.Script];
                    if (!D.Valid(s.Root)) { t.State = HsThreadState.Dead; continue; }
                    t.ValueTop = 0; t.HasResult = false;
                    Push(t, s.Root);
                    t.Starts++;
                    if (Trace != null && (t.Type != HsScriptType.Continuous || t.Starts == 1)) Trace("start " + s.Name);
                }
                t.State = HsThreadState.Running;
                _current = t;
                Run(t);
                _current = null;
                if (t.Depth == 0)
                {
                    t.Finished++;
                    if (t.Type == HsScriptType.Continuous) { t.State = HsThreadState.Asleep; if (t.WakeTick <= Tick) t.WakeTick = Tick + 1; }
                    else { t.State = HsThreadState.Dead; if (Trace != null) Trace("end " + D.Scripts[t.Script].Name); }
                }
                else t.State = HsThreadState.Asleep;
            }
            Tick++;
        }

        // ------------------------------------------------------------------ checkpoints

        HsThread _current;
        bool _inNative;

        /// <summary>The script state for a checkpoint: globals, engine globals, every thread (state, sleep timer, its evaluation stack).
        /// Taken inside a script call (game_save_totally_unsafe saves at once) the calling thread is stored as
        /// having finished that call, so a revert resumes right after the save (cinematic_skip_start then sees game_reverted).</summary>
        public HsSnapshot Capture()
        {
            var s = new HsSnapshot();
            Fill(s);
            return s;
        }

        void Fill(HsSnapshot s)
        {
            s.Tick = Tick;
            s.Globals = (int[])Globals.Clone();
            s.Externals = ExternalValues.ToArray();
            s.Threads = new HsSnapshot.ThreadState[Threads.Length];
            for (int i = 0; i < Threads.Length; i++)
            {
                var t = Threads[i];
                var ts = new HsSnapshot.ThreadState { State = t.State, WakeTick = t.WakeTick, Depth = t.Depth, ValueTop = t.ValueTop, Result = t.Result, HasResult = t.HasResult, Starts = t.Starts };
                if (t == _current && _inNative && t.Depth > 0)
                {
                    // mid-call: the call (the save) counts as done with a void result
                    ts.Depth = t.Depth - 1; ts.ValueTop = t.Frames[t.Depth - 1].ValueBase; ts.HasResult = true; ts.Result = 0;
                    ts.State = HsThreadState.Asleep; ts.WakeTick = Tick + 1;
                }
                ts.Frames = new HsFrame[ts.Depth]; Array.Copy(t.Frames, ts.Frames, ts.Depth);
                ts.Values = new int[ts.ValueTop]; Array.Copy(t.Values, ts.Values, ts.ValueTop);
                ts.ArgNodes = new int[ts.ValueTop]; Array.Copy(t.ArgNodes, ts.ArgNodes, ts.ValueTop);
                s.Threads[i] = ts;
            }
            s.Filled = true;
        }

        /// <summary>Back to a checkpoint's script state (call between ticks).</summary>
        public bool Restore(HsSnapshot s)
        {
            if (s == null || !s.Filled || s.Threads == null || s.Threads.Length != Threads.Length || s.Globals.Length != Globals.Length) return false;
            Tick = s.Tick;
            Array.Copy(s.Globals, Globals, Globals.Length);
            for (int i = 0; i < s.Externals.Length && i < ExternalValues.Count; i++) ExternalValues[i] = s.Externals[i];
            for (int i = 0; i < Threads.Length; i++)
            {
                var t = Threads[i]; var ts = s.Threads[i];
                t.State = ts.State; t.WakeTick = ts.WakeTick; t.Result = ts.Result; t.HasResult = ts.HasResult; t.Starts = ts.Starts;
                if (t.Frames.Length < ts.Depth) t.Frames = new HsFrame[ts.Depth * 2];
                Array.Copy(ts.Frames, t.Frames, ts.Depth); t.Depth = ts.Depth;
                if (t.Values.Length < ts.ValueTop) { t.Values = new int[ts.ValueTop * 2]; t.ArgNodes = new int[ts.ValueTop * 2]; }
                Array.Copy(ts.Values, t.Values, ts.ValueTop); Array.Copy(ts.ArgNodes, t.ArgNodes, ts.ValueTop); t.ValueTop = ts.ValueTop;
            }
            return true;
        }

        /// <summary>wake: a sleeping script runs at its next chance (this tick when it comes later in the order, else the next).</summary>
        public void Wake(int script)
        {
            var t = script >= 0 && script < ThreadOf.Length ? ThreadOf[script] : null;
            if (t == null || t.State == HsThreadState.Dead) return;
            if (t.WakeTick > Tick) { t.WakeTick = Tick; t.Wakes++; if (Trace != null) Trace("wake " + D.Scripts[script].Name); }
        }

        /// <summary>sleep with a script argument (ticks &lt; 0 = until woken).</summary>
        public void SleepScript(int script, int ticks)
        {
            var t = script >= 0 && script < ThreadOf.Length ? ThreadOf[script] : null;
            if (t == null || t.State == HsThreadState.Dead) return;
            t.WakeTick = ticks == -1 ? int.MaxValue : Tick + System.Math.Max(ticks, 0);
        }

        void Push(HsThread t, int node)
        {
            if (t.Depth >= t.Frames.Length) Array.Resize(ref t.Frames, t.Frames.Length * 2);
            int local = t.Depth > 0 ? t.Frames[t.Depth - 1].Local : -1;
            t.Frames[t.Depth] = new HsFrame { Node = node, Phase = 0, Arg = -1, ValueBase = t.ValueTop, Local = local };
            t.Depth++;
        }

        void PushValue(HsThread t, int v, int node)
        {
            if (t.ValueTop >= t.Values.Length) { Array.Resize(ref t.Values, t.Values.Length * 2); Array.Resize(ref t.ArgNodes, t.ArgNodes.Length * 2); }
            t.Values[t.ValueTop] = v; t.ArgNodes[t.ValueTop] = node; t.ValueTop++;
        }

        void Return(HsThread t, int v)
        {
            t.ValueTop = t.Frames[t.Depth - 1].ValueBase;
            t.Depth--;
            t.Result = v; t.HasResult = true;
        }

        int FirstArg(int call)
        {
            int nameNode = D.Nodes[call].Child;
            return D.Valid(nameNode) ? D.Nodes[nameNode].Next : -1;
        }

        int NextOf(int node) => D.Valid(node) ? D.Nodes[node].Next : -1;

        void Bad(int node, string what)
        {
            if (node >= 0 && node < _loggedNode.Length) { if (_loggedNode[node]) return; _loggedNode[node] = true; }
            Log("Script: bad node " + node + " (" + what + ")");
        }

        /// <summary>Runs a thread until it sleeps (returns with Depth &gt; 0) or ends (Depth 0, Result = the value).</summary>
        void Run(HsThread t)
        {
            int ops = 0;
            while (t.Depth > 0)
            {
                if (++ops > OpsPerThreadTick)
                {
                    Log("Script: " + (t.Script >= 0 ? D.Scripts[t.Script].Name : "global init") + " ran " + OpsPerThreadTick + " steps in one tick - paused to the next tick");
                    t.WakeTick = Tick + 1; return;
                }
                int fi = t.Depth - 1;
                int node = t.Frames[fi].Node;
                if (!D.Valid(node)) { Bad(node, "out of range"); t.HasResult = false; Return(t, 0); continue; }
                var n = D.Nodes[node];

                // ---- primitives
                if (n.IsPrimitive)
                {
                    t.HasResult = false;
                    Return(t, Primitive(t, node, n));
                    continue;
                }

                byte sp = _special[node];
                ref HsFrame f = ref t.Frames[fi];
                bool got = t.HasResult; int res = t.Result; t.HasResult = false;

                switch (sp)
                {
                    case SpBegin:
                    {
                        if (f.Phase == 0) { f.Phase = 1; f.Arg = FirstArg(node); if (f.Arg < 0) { Return(t, Default(n.Type)); break; } Push(t, f.Arg); break; }
                        f.Aux = got ? res : 0;
                        f.Arg = NextOf(f.Arg);
                        if (f.Arg < 0) { Return(t, f.Aux); break; }
                        Push(t, f.Arg); break;
                    }
                    case SpBeginRandom:
                    {
                        if (f.Phase == 0)
                        {
                            f.Phase = 1; f.Count = 0;
                            for (int c = FirstArg(node); c >= 0 && f.Count < 64; c = NextOf(c)) f.Count++;
                            f.Mask = 0; f.Aux2 = 0;
                        }
                        else f.Aux = got ? res : 0;
                        if (f.Aux2 >= f.Count) { Return(t, f.Aux); break; }
                        int pick = Rng.Next(f.Count - f.Aux2), k = 0, child = FirstArg(node);
                        for (int c = 0; c < f.Count; c++, child = NextOf(child))
                        {
                            if ((f.Mask & (1UL << c)) != 0) continue;
                            if (k++ == pick) { f.Mask |= 1UL << c; break; }
                        }
                        f.Aux2++;
                        if (child < 0) { Return(t, f.Aux); break; }
                        Push(t, child); break;
                    }
                    case SpIf:
                    {
                        if (f.Phase == 0) { f.Phase = 1; f.Arg = FirstArg(node); if (f.Arg < 0) { Bad(node, "if without condition"); Return(t, Default(n.Type)); break; } Push(t, f.Arg); break; }
                        if (f.Phase == 1)
                        {
                            int then = NextOf(f.Arg);
                            int branch = got && res != 0 ? then : NextOf(then);
                            if (branch < 0) { Return(t, Default(n.Type)); break; }
                            f.Phase = 2; Push(t, branch); break;
                        }
                        Return(t, got ? res : Default(n.Type)); break;
                    }
                    case SpAnd:
                    case SpOr:
                    {
                        bool isAnd = sp == SpAnd;
                        if (f.Phase == 0) { f.Phase = 1; f.Arg = FirstArg(node); if (f.Arg < 0) { Return(t, isAnd ? 1 : 0); break; } Push(t, f.Arg); break; }
                        bool v = got && res != 0;
                        if (isAnd && !v) { Return(t, 0); break; }
                        if (!isAnd && v) { Return(t, 1); break; }
                        f.Arg = NextOf(f.Arg);
                        if (f.Arg < 0) { Return(t, isAnd ? 1 : 0); break; }
                        Push(t, f.Arg); break;
                    }
                    case SpSet:
                    {
                        int target = FirstArg(node);
                        if (f.Phase == 0)
                        {
                            int val = NextOf(target);
                            if (!D.Valid(target) || val < 0) { Bad(node, "set without variable / value"); Return(t, 0); break; }
                            f.Phase = 1; Push(t, val); break;
                        }
                        int v = got ? res : 0;
                        var tn = D.Nodes[target];
                        if (tn.IsLocal) { if (f.Local >= 0) { int li = f.Local + (int)tn.Data; if (li < t.ValueTop) t.Values[li] = v; } }
                        else if (_ext[target] >= 0) ExternalValues[_ext[target]] = v;
                        else
                        {
                            int gi = (int)(tn.Data & 0xFFFF);
                            if (gi < Globals.Length) Globals[gi] = v; else Bad(target, "set: global " + gi);
                        }
                        Return(t, v); break;
                    }
                    case SpSleepUntil:
                    {
                        // Phase 0: test the condition. 1: got it. Frame.Aux = period, Aux2 = timeout, Count = ticks waited so far, Mask bit0 = params read
                        if (f.Phase == 0) { f.Phase = 1; f.Arg = FirstArg(node); if (f.Arg < 0) { Return(t, 0); break; } Push(t, f.Arg); break; }
                        if (f.Phase == 1)
                        {
                            if (got && res != 0) { Return(t, 0); break; }
                            if ((f.Mask & 1) == 0)
                            {
                                // evaluate the period / timeout once (they may be expressions)
                                int p = NextOf(f.Arg);
                                f.Aux = 30; f.Aux2 = -1; f.Count = 0;
                                if (p >= 0) { f.Phase = 2; Push(t, p); break; }
                                f.Mask |= 1;
                            }
                            goto sleepUntilWait;
                        }
                        if (f.Phase == 2)
                        {
                            if (got) f.Aux = ValueAsInt(res, D.Nodes[NextOf(f.Arg)].Type);
                            int to = NextOf(NextOf(f.Arg));
                            if (to >= 0) { f.Phase = 3; Push(t, to); break; }
                            f.Mask |= 1; goto sleepUntilWait;
                        }
                        if (f.Phase == 3)
                        {
                            if (got) f.Aux2 = ValueAsInt(res, D.Nodes[NextOf(NextOf(f.Arg))].Type);
                            f.Mask |= 1; goto sleepUntilWait;
                        }
                        Return(t, 0); break;
                    sleepUntilWait:
                        {
                            int period = f.Aux < 1 ? 1 : f.Aux;
                            if (f.Aux2 >= 0 && f.Count >= f.Aux2) { Return(t, 0); break; }
                            f.Count += period;
                            f.Phase = 0;
                            t.WakeTick = Tick + period;
                            t.Sleeps++;
                            return;
                        }
                    }
                    default:
                    {
                        // generic: evaluate the arguments in order, then do the function
                        if (f.Phase == 0)
                        {
                            f.Phase = 1; f.ValueBase = t.ValueTop; f.Count = 0;
                            f.Arg = FirstArg(node);
                            if (f.Arg >= 0) { Push(t, f.Arg); break; }
                        }
                        else if (f.Phase == 1)
                        {
                            PushValue(t, got ? res : 0, f.Arg); f.Count++;
                            f.Arg = NextOf(f.Arg);
                            if (f.Arg >= 0) { Push(t, f.Arg); break; }
                        }
                        else
                        {
                            // phase 2: a called script returned
                            int st = n.Index < D.Scripts.Length ? (int)D.Scripts[n.Index].Returns : (int)HsType.Void;
                            Return(t, Convert(got ? res : 0, (HsType)st, n.Type));
                            break;
                        }
                        // all arguments are on the value stack
                        if (sp == SpScript)
                        {
                            int si = n.Index;
                            if (si >= D.Scripts.Length || !D.Valid(D.Scripts[si].Root)) { Bad(node, "script call " + si); Return(t, Default(n.Type)); break; }
                            f.Phase = 2;
                            int localBase = f.ValueBase;
                            Push(t, D.Scripts[si].Root);
                            t.Frames[t.Depth - 1].Local = localBase;
                            t.Frames[t.Depth - 1].ValueBase = t.ValueTop;
                            break;
                        }
                        if (sp == SpSleep)
                        {
                            int ticks = f.Count > 0 ? ArgInt(t, f.ValueBase) : 0;
                            if (f.Count >= 2)
                            {
                                SleepScript(t.Values[f.ValueBase + 1], ticks);
                                Return(t, 0); break;
                            }
                            Return(t, 0);
                            t.HasResult = true;
                            t.WakeTick = ticks < 0 ? int.MaxValue : Tick + System.Math.Max(ticks, 0);
                            if (ticks <= 0 && ticks != -1) t.WakeTick = Tick + 1;
                            t.Sleeps++;
                            if (Trace != null && ticks == -1) Trace("sleep forever " + (t.Script >= 0 ? D.Scripts[t.Script].Name : "?"));
                            return;
                        }
                        if (sp == SpWake)
                        {
                            if (f.Count > 0) Wake(t.Values[f.ValueBase]);
                            Return(t, 0); break;
                        }
                        if (sp >= SpAdd && sp <= SpNot) { Return(t, Arith(t, sp, f.ValueBase, f.Count, n.Type)); break; }
                        int fn = _fn[node];
                        Return(t, CallNative(t, fn, node, f.ValueBase, f.Count, n.Type));
                        break;
                    }
                }
            }
        }

        int ArgInt(HsThread t, int at) => ValueAsInt(t.Values[at], D.Nodes[t.ArgNodes[at]].Type);
        static int ValueAsInt(int v, HsType type) => type == HsType.Real ? (int)F(v) : v;
        float ArgReal(HsThread t, int at) { var ty = D.Nodes[t.ArgNodes[at]].Type; int v = t.Values[at]; return ty == HsType.Real ? F(v) : v; }

        int Arith(HsThread t, byte sp, int b, int count, HsType type)
        {
            if (sp == SpNot) return count > 0 && t.Values[b] != 0 ? 0 : 1;
            if (sp >= SpEq)
            {
                if (count < 2) return 0;
                var ta = D.Nodes[t.ArgNodes[b]].Type; var tb = D.Nodes[t.ArgNodes[b + 1]].Type;
                int c;
                if (ta == HsType.Real || tb == HsType.Real) { float x = ArgReal(t, b), y = ArgReal(t, b + 1); c = x < y ? -1 : x > y ? 1 : 0; }
                else { int x = t.Values[b], y = t.Values[b + 1]; c = x < y ? -1 : x > y ? 1 : 0; }
                switch (sp)
                {
                    case SpEq: return c == 0 ? 1 : 0;
                    case SpNe: return c != 0 ? 1 : 0;
                    case SpGt: return c > 0 ? 1 : 0;
                    case SpLt: return c < 0 ? 1 : 0;
                    case SpGe: return c >= 0 ? 1 : 0;
                    default: return c <= 0 ? 1 : 0;
                }
            }
            float r = count > 0 ? ArgReal(t, b) : 0f;
            for (int i = 1; i < count; i++)
            {
                float y = ArgReal(t, b + i);
                switch (sp)
                {
                    case SpAdd: r += y; break;
                    case SpSub: r -= y; break;
                    case SpMul: r *= y; break;
                    case SpDiv: r = y != 0f ? r / y : 0f; break;
                    case SpMin: if (y < r) r = y; break;
                    case SpMax: if (y > r) r = y; break;
                }
            }
            if (sp == SpSub && count == 1) r = -r;
            return Convert(R(r), HsType.Real, type);
        }

        int CallNative(HsThread t, int fn, int node, int b, int count, HsType nodeType)
        {
            if (fn < 0)
            {
                Bad(node, "unknown function '" + D.CallName(node) + "'");
                return Default(nodeType);
            }
            Calls[fn]++;
            var f = Natives[fn];
            HsType ret = (HsType)HsDefs.FunctionReturns[fn];
            if (f == null)
            {
                if (!_loggedFn[fn]) { _loggedFn[fn] = true; Log("Script: unimplemented " + HsDefs.FunctionName(fn) + " (" + Uses[fn] + " uses in " + MapName + ")"); }
                return Default(nodeType);
            }
            int v;
            _inNative = true;
            try { v = f(new HsArgs(this, t, b, count, fn, node)); }
            catch (Exception e)
            {
                if (!_loggedFn[fn]) { _loggedFn[fn] = true; Log("Script: " + HsDefs.FunctionName(fn) + " threw " + e.GetType().Name + ": " + e.Message); }
                return Default(nodeType);
            }
            finally { _inNative = false; }
            if (ret == HsType.Passthrough || ret == HsType.Void) return v;
            return Convert(v, ret, nodeType);
        }

        string MapName => D.Map != null ? D.Map.Name : "?";

        int Primitive(HsThread t, int node, HsNode n)
        {
            if (n.IsLocal)
            {
                var fr = t.Frames[t.Depth - 1];
                int li = fr.Local + (int)n.Data;
                return fr.Local >= 0 && li < t.ValueTop ? t.Values[li] : Default(n.Type);
            }
            if (n.IsGlobal)
            {
                if (_ext[node] >= 0) { int s = _ext[node]; return Convert(ExternalValues[s], ExternalTypes[s], n.Type); }
                int gi = (int)(n.Data & 0xFFFF);
                if (gi >= Globals.Length) { Bad(node, "global " + gi); return Default(n.Type); }
                return Convert(Globals[gi], D.Globals[gi].Type, n.Type);
            }
            // constants: the index union holds the constant's own type, the node type is the type it was converted to
            HsType src = n.Index < (ushort)HsType.Count ? (HsType)n.Index : n.Type;
            int v;
            switch (src)
            {
                case HsType.Boolean: v = (n.Data & 0xFF) != 0 ? 1 : 0; break;
                case HsType.Real: v = (int)n.Data; break;
                case HsType.Long: v = (int)n.Data; break;
                case HsType.String: v = n.StringOffset; break;
                case HsType.Ai: v = (int)n.Data; break;
                case HsType.Void: return 0;
                default:
                    if (src >= HsType.Sound && src <= HsType.ObjectDefinition) { v = (int)n.Data; break; }
                    if (IsObjectType(src) && (n.Data & 0xFFFF) == 0xFFFF) { v = -1; break; }
                    v = (short)(n.Data & 0xFFFF); break;
            }
            return Convert(v, src, n.Type);
        }

        // ------------------------------------------------------------------ binding helpers

        /// <summary>Binds an engine function by its HaloScript name; returns false when no such function exists.</summary>
        public bool Bind(string name, HsNative f)
        {
            int i = HsDefs.FunctionIndex(name);
            if (i < 0) return false;
            Natives[i] = f; return true;
        }

        public int ScriptIndex(string name) => D.ScriptIndex(name);
        public string ScriptName(int i) => i >= 0 && i < D.Scripts.Length ? D.Scripts[i].Name : "";

        /// <summary>Script and engine global values for tools (by name; scenario globals first).</summary>
        public bool TryGlobal(string name, out int value, out HsType type)
        {
            int gi = D.GlobalIndex(name);
            if (gi >= 0) { value = Globals[gi]; type = D.Globals[gi].Type; return true; }
            int e = ExternalNames.IndexOf(name);
            if (e >= 0) { value = ExternalValues[e]; type = ExternalTypes[e]; return true; }
            value = 0; type = HsType.Void; return false;
        }

        public void SetExternal(string name, int value) { int e = ExternalNames.IndexOf(name); if (e >= 0) ExternalValues[e] = value; }

        /// <summary>Function ids used by this map that have no native bound (for the coverage report).</summary>
        public void Unbound(List<int> into) { for (int i = 0; i < Uses.Length; i++) if (Uses[i] > 0 && Natives[i] == null && !IsVmSpecial(i)) into.Add(i); }

        public static bool IsVmSpecial(int fn)
        {
            string n = HsDefs.FunctionName(fn);
            return SpecialOf(n) != SpNone;
        }
    }
}

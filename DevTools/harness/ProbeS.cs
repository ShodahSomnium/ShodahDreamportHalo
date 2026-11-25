using System;
using System.IO;
using System.Text;
using SomniumSpace.Worlds.SomniumHalo;

// [S] HaloScript probes (Script module): hsc <map> [out] = decompile to HSC; hsrun <map> <s> [q] = run the VM world-less; hsuses = function uses per map;
// hshmt <map> = hud message texts; hsraw <map> [n] [start] = node dump; hsdefs <definition.json> <out.cs> = regenerate Data/Script/HsDefs.cs; hsfnidx / hstypes / hsglobs = encoding checks
static partial class Probe
{
    public static int S(string[] a)
    {
        string cmd = a[0];
        if (cmd.StartsWith("hs") && cmd != "hsc") cmd = cmd.Substring(2);   // shared harness names: hsraw, hsrun, hsuses, hshmt, hsdefs, hsfnidx, hstypes, hsglobs
        if (cmd == "raw") return SRaw(a);
        if (cmd == "defs") return SDefs(a);
        if (cmd == "hsc") return SHsc(a);
        if (cmd == "fnidx") return SFnIdx(a);
        if (cmd == "globs") return SGlobNodes(a);
        if (cmd == "types") return STypes(a);
        if (cmd == "uses") return SUses(a);
        if (cmd == "run") return SRun(a);
        if (cmd == "gvals") return SGVals(a);
        if (cmd == "handover") return SHandover(a);
        if (cmd == "hmt") return SHmt(a);
        Console.WriteLine("S: unknown " + cmd); return 1;
    }

    static int SRaw(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var r = m.Scenario.Root;
        byte[] syn = r.DataBytes(1140), str = r.DataBytes(1160);
        Console.WriteLine($"syntax {syn.Length} strings {str.Length} scripts {r.Count(1180)} globals {r.Count(1192)}");
        Console.WriteLine("header name '" + Encoding.ASCII.GetString(syn, 0, 32).TrimEnd('\0') + $"' max {BitConverter.ToUInt16(syn, 32)} size {BitConverter.ToUInt16(syn, 34)} one {syn[36]} data {BitConverter.ToUInt32(syn, 40):X8} s46 {BitConverter.ToUInt16(syn, 46)} count {BitConverter.ToUInt16(syn, 48)} next {BitConverter.ToUInt16(syn, 50)} first {BitConverter.ToUInt32(syn, 52):X8}");
        int n = int.Parse(a.Length > 2 ? a[2] : "40"), start = int.Parse(a.Length > 3 ? a[3] : "0");
        for (int i = start; i < start + n; i++)
        {
            int o = 56 + i * 20;
            ushort salt = BitConverter.ToUInt16(syn, o), idx = BitConverter.ToUInt16(syn, o + 2), type = BitConverter.ToUInt16(syn, o + 4), flags = BitConverter.ToUInt16(syn, o + 6);
            uint next = BitConverter.ToUInt32(syn, o + 8), so = BitConverter.ToUInt32(syn, o + 12), data = BitConverter.ToUInt32(syn, o + 16);
            string s = so < str.Length ? CStr(str, (int)so) : "-";
            Console.WriteLine($"{i,5} salt {salt:X4} idx {idx,5} type {type,2} flags {flags:X2} next {next:X8} str {so,6} '{s}' data {data:X8}");
        }
        int k = 0;
        foreach (var sc in r.Block(1180, 92)) { if (k++ < 10) Console.WriteLine($"script {sc.Str32(0)} type {sc.U16(32)} ret {sc.U16(34)} root {sc.U32(36):X8} params {sc.Count(80)}"); }
        k = 0;
        foreach (var g in r.Block(1192, 92)) { if (k++ < 10) Console.WriteLine($"global {g.Str32(0)} type {g.U16(32)} init {g.U32(40):X8}"); }
        return 0;
    }

    // defs <definition.json> <out.cs>: the function / external global tables by Halo PC retail index (generated, Data/Script/HsDefs.cs)
    static int SDefs(string[] a)
    {
        string[] types = { "unparsed", "special_form", "function_name", "passthrough", "void", "boolean", "real", "short", "long", "string", "script", "trigger_volume", "cutscene_flag", "cutscene_camera_point", "cutscene_title", "cutscene_recording", "device_group", "ai", "ai_command_list", "starting_profile", "conversation", "navpoint", "hud_message", "object_list", "sound", "effect", "damage", "looping_sound", "animation_graph", "actor_variant", "damage_effect", "object_definition", "game_difficulty", "team", "ai_default_state", "actor_type", "hud_corner", "object", "unit", "vehicle", "weapon", "device", "scenery", "object_name", "unit_name", "vehicle_name", "weapon_name", "device_name", "scenery_name" };
        Func<string, int> T = s => { int i = Array.IndexOf(types, s.Replace(' ', '_')); if (i < 0) throw new Exception("type " + s); return i; };
        var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(a[1]));
        var fn = new System.Collections.Generic.SortedDictionary<int, string>();
        int maxF = -1, maxG = -1;
        var fRet = new System.Collections.Generic.Dictionary<int, int>(); var fPar = new System.Collections.Generic.Dictionary<int, string>();
        foreach (var f in doc.RootElement.GetProperty("functions").EnumerateArray())
        {
            System.Text.Json.JsonElement en, e; if (!f.TryGetProperty("engines", out en) || !en.TryGetProperty("gbx-retail", out e)) continue;
            if (e.ValueKind != System.Text.Json.JsonValueKind.Number) continue;
            int i = e.GetInt32(); fn[i] = f.GetProperty("name").GetString(); fRet[i] = T(f.GetProperty("type").GetString()); maxF = Math.Max(maxF, i);
            var sb = new StringBuilder();
            System.Text.Json.JsonElement ps;
            if (f.TryGetProperty("parameters", out ps))
                foreach (var p in ps.EnumerateArray())
                {
                    if (sb.Length > 0) sb.Append(',');
                    System.Text.Json.JsonElement x;
                    if (p.TryGetProperty("optional", out x) && x.GetBoolean()) sb.Append('?');
                    if (p.TryGetProperty("many", out x) && x.GetBoolean()) sb.Append('*');
                    sb.Append(T(p.GetProperty("type").GetString()));
                }
            fPar[i] = sb.ToString();
        }
        var gn = new System.Collections.Generic.SortedDictionary<int, string>(); var gT = new System.Collections.Generic.Dictionary<int, int>();
        foreach (var g in doc.RootElement.GetProperty("globals").EnumerateArray())
        {
            System.Text.Json.JsonElement en, e; if (!g.TryGetProperty("engines", out en) || !en.TryGetProperty("gbx-retail", out e)) continue;
            int i = gn.Count; gn[i] = g.GetProperty("name").GetString(); gT[i] = T(g.GetProperty("type").GetString()); maxG = Math.Max(maxG, i);
        }
        var o = new StringBuilder();
        o.AppendLine("// GENERATED by DevTools/harness ProbeS.cs \"defs\" from DevTools/reference/riat/definition.json (RIAT), engine \"gbx-retail\" (Halo PC 01.00.10.0621).");
        o.AppendLine("// Do not edit by hand. Function / external global names, return types and parameter types by their Halo PC index.");
        o.AppendLine("namespace SomniumSpace.Worlds.SomniumHalo");
        o.AppendLine("{");
        o.AppendLine("    public static partial class HsDefs");
        o.AppendLine("    {");
        o.AppendLine("        /// <summary>Engine function names by gbx-retail index (null = unused index).</summary>");
        o.AppendLine("        public static readonly string[] FunctionNames = {");
        for (int i = 0; i <= maxF; i++) o.AppendLine("            " + (fn.ContainsKey(i) ? "\"" + fn[i] + "\"" : "null") + ",");
        o.AppendLine("        };");
        o.AppendLine("        /// <summary>Return value type (HsType) by function index.</summary>");
        o.Append("        public static readonly byte[] FunctionReturns = {");
        for (int i = 0; i <= maxF; i++) { if (i % 32 == 0) o.Append("\n            "); o.Append(fRet.ContainsKey(i) ? fRet[i] : 0).Append(", "); }
        o.AppendLine("\n        };");
        o.AppendLine("        /// <summary>Parameter value types (HsType numbers, ',' separated; '?' optional, '*' repeated) by function index.</summary>");
        o.AppendLine("        public static readonly string[] FunctionParams = {");
        for (int i = 0; i <= maxF; i++) o.AppendLine("            " + (fPar.ContainsKey(i) ? "\"" + fPar[i] + "\"" : "null") + ",");
        o.AppendLine("        };");
        o.AppendLine("        /// <summary>External (engine) globals of Halo PC (RIAT lists no PC indices for them: scripts name them in the string data).</summary>");
        o.AppendLine("        public static readonly string[] GlobalNames = {");
        for (int i = 0; i <= maxG; i++) o.AppendLine("            " + (gn.ContainsKey(i) ? "\"" + gn[i] + "\"" : "null") + ",");
        o.AppendLine("        };");
        o.Append("        public static readonly byte[] GlobalTypes = {");
        for (int i = 0; i <= maxG; i++) { if (i % 32 == 0) o.Append("\n            "); o.Append(gT.ContainsKey(i) ? gT[i] : 0).Append(", "); }
        o.AppendLine("\n        };");
        o.AppendLine("    }");
        o.AppendLine("}");
        File.WriteAllText(a[2], o.ToString());
        Console.WriteLine($"functions {fn.Count} (max index {maxF}), globals {gn.Count} (max index {maxG}) -> {a[2]}");
        return 0;
    }

    // hsc <map> [outfile]: decompile every script + global of a campaign map back to HSC text
    static int SHsc(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var d = HsData.Read(m);
        var dc = new HsDecompiler(d);
        string text = dc.All();
        if (a.Length > 2) File.WriteAllText(a[2], text); else Console.Write(text);
        Console.Error.WriteLine($"{a[1]}: scripts {d.Scripts.Length} globals {d.Globals.Length} nodes visited {dc.Nodes} bad {dc.BadNodes} string mismatches {dc.Mismatches} problems {d.Problems.Count}");
        if (dc.Mismatches > 0) Console.Error.Write(dc.MismatchLog.ToString());
        return 0;
    }

    // fnidx: the function names the maps' own string data give for each function index vs RIAT's gbx-retail table
    static int SFnIdx(string[] a)
    {
        var seen = new System.Collections.Generic.SortedDictionary<int, string>();
        foreach (var mn in new[] { "a10", "a30", "a50", "b30", "b40", "c10", "c20", "c40", "d20", "d40" })
        {
            var d = HsData.Read(new HaloMap(Maps + mn + ".map"));
            for (int i = 0; i < d.Nodes.Length; i++)
            {
                var n = d.Nodes[i];
                if (n.Salt == 0 || n.Type != HsType.FunctionName || !n.IsPrimitive) continue;
                string s = d.Str(n.StringOffset);
                if (d.ScriptIndex(s) >= 0) continue;
                string prev;
                if (seen.TryGetValue(n.Index, out prev)) { if (prev != s && !prev.Contains("|" )) seen[n.Index] = prev + "|" + s; }
                else seen[n.Index] = s;
            }
        }
        int same = 0, diff = 0;
        foreach (var kv in seen)
        {
            string r = HsDefs.FunctionName(kv.Key);
            if (r == kv.Value) same++; else diff++;
            Console.WriteLine($"{kv.Key,4} {(r == kv.Value ? "  " : "!=")} map '{kv.Value}' riat '{r}' riatIndexOfMapName {HsDefs.FunctionIndex(kv.Value)}");
        }
        Console.WriteLine($"same {same} diff {diff}");
        return 0;
    }

    static int SGlobNodes(string[] a)
    {
        var d = HsData.Read(new HaloMap(Maps + a[1] + ".map"));
        var seen = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < d.Nodes.Length; i++)
        {
            var n = d.Nodes[i];
            if (n.Salt == 0 || !n.IsPrimitive || !(n.IsGlobal || n.IsLocal)) continue;
            string s = d.Str(n.StringOffset);
            if (!seen.Add(s + n.Data)) continue;
            Console.WriteLine($"{(n.IsLocal ? "local " : "global")} {s,-40} idx {n.Index} type {HsDefs.TypeName(n.Type)} data {n.Data:X8} scen {d.GlobalIndex(s)} ext {HsDefs.GlobalIndex(s)}");
        }
        return 0;
    }

    // types <map>: constants per value type: count + examples (data vs string)
    static int STypes(string[] a)
    {
        var d = HsData.Read(new HaloMap(Maps + a[1] + ".map"));
        var count = new int[64]; var ex = new string[64];
        // only nodes reachable from scripts / globals (salt != 0)
        for (int i = 0; i < d.Nodes.Length; i++)
        {
            var n = d.Nodes[i];
            if (n.Salt == 0 || !n.IsPrimitive || n.IsGlobal || n.IsLocal || n.Type == HsType.FunctionName) continue;
            int t = (int)n.Type; if (t >= 64) continue;
            count[t]++;
            if (count[t] <= 3) ex[t] += $" [{n.Data:X8} '{d.Str(n.StringOffset)}' idx {n.Index}]";
        }
        for (int t = 0; t < 64; t++) if (count[t] > 0) Console.WriteLine($"{HsDefs.TypeName((HsType)t),-22} {count[t],5}{ex[t]}");
        Console.WriteLine("object names: " + d.ObjectNames.Length + " first " + string.Join(",", d.ObjectNames, 0, Math.Min(5, d.ObjectNames.Length)));
        return 0;
    }

    // uses: function -> uses per campaign map (the coverage base)
    static readonly string[] SMaps = { "a10", "a30", "a50", "b30", "b40", "c10", "c20", "c40", "d20", "d40" };
    static int SUses(string[] a)
    {
        var per = new int[SMaps.Length][];
        var unknown = new System.Collections.Generic.List<string>();
        for (int m = 0; m < SMaps.Length; m++)
        {
            var vm = new HsVm(HsData.Read(new HaloMap(Maps + SMaps[m] + ".map")));
            per[m] = (int[])vm.Uses.Clone();
            foreach (var u in vm.UnknownFunctions) unknown.Add(SMaps[m] + ":" + u);
        }
        var rows = new System.Collections.Generic.List<(int fn, int total)>();
        for (int f = 0; f < HsDefs.FunctionNames.Length; f++) { int s = 0; for (int m = 0; m < SMaps.Length; m++) s += per[m][f]; if (s > 0) rows.Add((f, s)); }
        rows.Sort((x, y) => y.total.CompareTo(x.total));
        Console.WriteLine("function".PadRight(42) + "total " + string.Join(" ", SMaps));
        foreach (var r in rows)
        {
            var sb = new StringBuilder(HsDefs.FunctionNames[r.fn].PadRight(42) + r.total.ToString().PadLeft(5));
            for (int m = 0; m < SMaps.Length; m++) sb.Append(per[m][r.fn].ToString().PadLeft(4));
            Console.WriteLine(sb);
        }
        Console.WriteLine("functions used: " + rows.Count + "; unknown names: " + string.Join(", ", unknown));
        return 0;
    }

    // run <map> <seconds> [quiet]: runs the map's scripts in the VM with a world-less test host (players = 1 fake player, volumes empty)
    static int SRun(string[] a)
    {
        var d = HsData.Read(new HaloMap(Maps + a[1] + ".map"));
        var vm = new HsVm(d);
        bool quiet = a.Length > 3 && a[3] == "q";
        vm.Log = s => Console.WriteLine($"[{vm.Tick,5}] {s}");
        if (!quiet) vm.Trace = s => Console.WriteLine($"[{vm.Tick,5}] {s}");
        HsCore.Bind(vm);
        HsCore.Print = quiet ? null : (System.Action<string>)(s => Console.WriteLine($"[{vm.Tick,5}] print: {s}"));
        vm.Bind("players", x => { int l = x.Vm.ListNew(); x.Vm.ListAdd(l, 0x10000); return l; });
        vm.Bind("game_safe_to_save", x => 1);
        vm.Bind("game_all_quiet", x => 1);
        vm.Bind("sound_impulse_time", x => 45);
        vm.Bind("recording_time", x => 0);
        // checkpoint test (hsrun <map> <s> v snap): game_save_totally_unsafe captures, at half time the VM reverts with game_reverted true
        bool snapTest = a.Length > 4 && a[4] == "snap"; HsSnapshot snap = null; bool reverted = false;
        if (snapTest) { vm.Bind("game_save_totally_unsafe", x => { snap = x.Vm.Capture(); Console.WriteLine($"[{x.Vm.Tick,5}] checkpoint captured"); return 0; }); vm.Bind("game_reverted", x => reverted ? 1 : 0); }
        int seconds = int.Parse(a[2]);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        vm.Start();
        long t0 = sw.ElapsedTicks;
        for (int i = 0; i < seconds * 30; i++)
        {
            vm.Step();
            if (snapTest && i == seconds * 15 && snap != null) { int g0 = vm.Tick; bool ok = vm.Restore(snap); reverted = true; Console.WriteLine($"[{g0,5}] REVERT to tick {snap.Tick}: {ok}"); }
        }
        double ms = (sw.ElapsedTicks - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        int run = 0, asleep = 0, dead = 0, dormant = 0;
        foreach (var t in vm.Threads) { if (t.State == HsThreadState.Dead) dead++; else if (t.WakeTick == int.MaxValue) dormant++; else asleep++; if (t.Starts > 0) run++; }
        Console.WriteLine($"{a[1]}: {vm.Tick} ticks in {ms:0.0} ms ({ms / System.Math.Max(1, vm.Tick) * 1000:0} us/tick); threads {vm.Threads.Length}: started {run}, finished {dead}, sleeping {asleep}, dormant {dormant}");
        var top = new System.Collections.Generic.List<int>();
        for (int f = 0; f < vm.Calls.Length; f++) if (vm.Calls[f] > 0) top.Add(f);
        top.Sort((x, y) => vm.Calls[y].CompareTo(vm.Calls[x]));
        var sb = new StringBuilder("calls:");
        for (int k = 0; k < System.Math.Min(25, top.Count); k++) sb.Append(' ').Append(HsDefs.FunctionNames[top[k]]).Append('=').Append(vm.Calls[top[k]]);
        Console.WriteLine(sb);
        foreach (var t in vm.Threads) if (t.Starts > 0 && !quiet) Console.WriteLine($"   {d.Scripts[t.Script].Name,-36} {t.Type,-10} {t.State,-8} starts {t.Starts} sleeps {t.Sleeps} wakes {t.Wakes} wake@{(t.WakeTick == int.MaxValue ? "never" : t.WakeTick.ToString())}");
        return 0;
    }

    // hmt <map>: the scenario's hud message text (hud_message_text +1428): name -> text with element types
    static int SHmt(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var t = m.Scenario.Root.Ref(1428);
        if (t == null) { Console.WriteLine("no hud messages"); return 0; }
        var ht = HsHudText.Read(m.Scenario);
        for (int i = 0; i < ht.Count; i++) Console.WriteLine("  " + ht.NameAt(i).PadRight(30) + " = " + ht.Get(ht.NameAt(i)).Replace((char)10, (char)47));
        if (a.Length < 3) return 0;
        var r = t.Root;
        byte[] text = r.DataBytes(0);
        var els = new System.Collections.Generic.List<(int type, int data)>();
        foreach (var e in r.Block(20, 2)) els.Add((e.I8(0), e.U8(1)));
        Console.WriteLine($"{t.Name}: text {text.Length} B, elements {els.Count}, messages {r.Count(32)}");
        foreach (var msg in r.Block(32, 64))
        {
            int ti = msg.I16(32), ei = msg.I16(34), panels = msg.U8(36);
            var sb = new StringBuilder();
            int c = ti;
            for (int k = ei; k < els.Count; k++)
            {
                var e = els[k];
                if (e.type == 0) { int n = e.data; for (int j = 0; j < n; j++) { int at = (c + j) * 2; if (at + 1 < text.Length) { char ch = (char)(text[at] | text[at + 1] << 8); if (ch != 0) sb.Append(ch); } } c += n; }
                else sb.Append($"<{e.type}:{e.data}>");
                if (e.type == 0 && e.data == 0) break;
                if (k - ei > 40) break;
            }
            Console.WriteLine($"  {msg.Str32(0),-32} text@{ti} el@{ei} panels {panels}: {sb}");
        }
        return 0;
    }

    // gvals <map>: the scenario globals after their initialisers ran (VM arithmetic / conversions check)
    static int SGVals(string[] a)
    {
        var d = HsData.Read(new HaloMap(Maps + a[1] + ".map"));
        var vm = new HsVm(d); HsCore.Bind(vm); vm.Log = s => Console.WriteLine(s);
        vm.Start();
        for (int i = 0; i < d.Globals.Length; i++)
        {
            var g = d.Globals[i]; int v = vm.Globals[i];
            string s = g.Type == HsType.Real ? HsVm.F(v).ToString("0.###") : g.Type == HsType.Boolean ? (v != 0 ? "true" : "false") : vm.NameOf(g.Type, v) is string n && n.Length > 0 ? n : v.ToString();
            Console.WriteLine($"  {HsDefs.TypeName(g.Type),-12} {g.Name,-36} = {s}");
        }
        return 0;
    }

    // handover <map> <seconds>: host runs N s, capture -> bytes -> a fresh VM (own HsData) adopts -> same globals / threads; both run on 10 s
    static int SHandover(string[] a)
    {
        string map = a[1]; int secs = a.Length > 2 ? int.Parse(a[2]) : 60;
        HsVm Make()
        {
            var vm = new HsVm(HsData.Read(new HaloMap(Maps + map + ".map")));
            HsCore.Bind(vm); vm.Log = s => { };
            vm.Bind("players", x => { int l = x.Vm.ListNew(); x.Vm.ListAdd(l, 0x20000); return l; });
            vm.Bind("game_safe_to_save", x => 1); vm.Bind("sound_impulse_time", x => 45);
            return vm;
        }
        var host = Make(); host.Start();
        for (int i = 0; i < secs * 30; i++) host.Step();
        byte[] bytes = host.Capture().ToBytes(host.D);
        var heir = Make(); string why;
        var snap = HsSnapshot.FromBytes(bytes, heir.D, out why);
        if (snap == null) { Console.WriteLine("FAIL: " + why); return 1; }
        heir.Start(); bool ok = heir.Restore(snap);
        int gdiff = 0, tdiff = 0;
        Func<HsThread, string> sig = t => $"{t.State}/{t.WakeTick}/{t.Starts}/{t.HasResult}/{t.Result}";
        for (int i = 0; i < host.Globals.Length; i++) if (host.Globals[i] != heir.Globals[i]) gdiff++;
        for (int i = 0; i < host.Threads.Length; i++) if (sig(host.Threads[i]) != sig(heir.Threads[i])) tdiff++;
        int started = 0; foreach (var t in heir.Threads) if (t.Starts > 0) started++;
        Console.WriteLine($"{map}: host tick {host.Tick}, {bytes.Length} bytes; adopt {ok} at tick {heir.Tick}: globals differing {gdiff}/{host.Globals.Length}, threads differing {tdiff}/{host.Threads.Length} (started {started})");
        for (int i = 0; i < 300; i++) { host.Step(); heir.Step(); }
        gdiff = 0; tdiff = 0;
        for (int i = 0; i < host.Globals.Length; i++) if (host.Globals[i] != heir.Globals[i]) gdiff++;
        for (int i = 0; i < host.Threads.Length; i++) if (sig(host.Threads[i]) != sig(heir.Threads[i])) tdiff++;
        Console.WriteLine($"  both on 300 ticks to {host.Tick}/{heir.Tick}: globals differing {gdiff}, threads differing {tdiff} (random numbers may differ)");
        var bad = HsSnapshot.FromBytes(bytes, new HsVm(HsData.Read(new HaloMap(Maps + (map == "a10" ? "a30" : "a10") + ".map"))).D, out why);
        Console.WriteLine("  another map refuses it: " + (bad == null ? "yes (" + why + ")" : "NO"));
        return gdiff == 0 && tdiff == 0 ? 0 : 1;
    }

    static string CStr(byte[] b, int o) { int e = o; while (e < b.Length && b[e] != 0) e++; return Encoding.ASCII.GetString(b, o, e - o); }
}

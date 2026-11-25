using System;
using System.Globalization;
using System.Text;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Script module, S] Turns a scenario's compiled HaloScript (HsData) back into readable HSC source - the proof that the node reader
    /// is right (harness: dotnet run -- hsc a10). Constants are printed from their data (block index / tag id / number) and checked against
    /// the string the compiler kept (<see cref="Mismatches"/> counts disagreements).
    /// </summary>
    public sealed class HsDecompiler
    {
        readonly HsData _d;
        public int Mismatches, BadNodes, Nodes;
        public readonly StringBuilder MismatchLog = new StringBuilder();
        HsScript _script;

        public HsDecompiler(HsData d) { _d = d; }

        public string All()
        {
            var sb = new StringBuilder();
            sb.Append(";; ").Append(_d.Scenario != null ? _d.Scenario.Name : "?").Append(" - decompiled from the map by the Halo Dreamport (S) HsDecompiler\n");
            sb.Append(";; ").Append(_d.Scripts.Length).Append(" scripts, ").Append(_d.Globals.Length).Append(" globals, ").Append(_d.Nodes.Length).Append(" node slots\n\n");
            for (int i = 0; i < _d.Globals.Length; i++) sb.Append(Global(i)).Append('\n');
            sb.Append('\n');
            for (int i = 0; i < _d.Scripts.Length; i++) sb.Append(Script(i)).Append("\n\n");
            return sb.ToString();
        }

        public string Global(int i)
        {
            var g = _d.Globals[i];
            _script = null;
            return "(global " + HsDefs.TypeName(g.Type) + " " + g.Name + " " + Expr(g.Init, 0, 0) + ")";
        }

        public string Script(int i)
        {
            var s = _d.Scripts[i];
            _script = s;
            var sb = new StringBuilder();
            sb.Append("(script ").Append(s.Type.ToString().ToLowerInvariant()).Append(' ');
            if (s.Type == HsScriptType.Static || s.Type == HsScriptType.Stub) sb.Append(HsDefs.TypeName(s.Returns)).Append(' ');
            if (s.ParamNames.Length > 0)
            {
                sb.Append('(').Append(s.Name);
                for (int p = 0; p < s.ParamNames.Length; p++) sb.Append(" (").Append(HsDefs.TypeName(s.ParamTypes[p])).Append(' ').Append(s.ParamNames[p]).Append(')');
                sb.Append(')');
            }
            else sb.Append(s.Name);
            // the root is (begin ...): print its expressions as the script body
            int root = s.Root;
            if (_d.Valid(root) && !_d.Nodes[root].IsPrimitive && !_d.Nodes[root].IsScriptCall && _d.Nodes[root].Index == 0)
            {
                int c = FirstArg(root), guard = 0;
                while (c >= 0 && guard++ < 10000) { sb.Append("\n    ").Append(Expr(c, 1, 4)); c = _d.Valid(c) ? _d.Nodes[c].Next : -1; }
            }
            else sb.Append("\n    ").Append(Expr(root, 1, 4));
            sb.Append(')');
            _script = null;
            return sb.ToString();
        }

        int FirstArg(int call)
        {
            int nameNode = _d.Nodes[call].Child;
            return _d.Valid(nameNode) ? _d.Nodes[nameNode].Next : -1;
        }

        string Expr(int i, int depth, int indent)
        {
            Nodes++;
            if (!_d.Valid(i) || depth > 200) { BadNodes++; return "<bad node " + i + ">"; }
            var n = _d.Nodes[i];
            if (n.IsPrimitive) return Primitive(n);
            string name = _d.CallName(i);
            if (name == "if" && IsCondChain(i, 0)) return Cond(i, depth, indent);
            // one line if short, else one argument per line
            var parts = new System.Collections.Generic.List<string>();
            int c = FirstArg(i), guard = 0;
            int len = name.Length + 2;
            bool multi = false;
            while (c >= 0 && guard++ < 1000)
            {
                string e = Expr(c, depth + 1, indent + 4);
                parts.Add(e); len += e.Length + 1;
                if (e.IndexOf('\n') >= 0) multi = true;
                c = _d.Valid(c) ? _d.Nodes[c].Next : -1;
            }
            if (!_d.Valid(_d.Nodes[i].Child)) BadNodes++;
            var sb = new StringBuilder();
            sb.Append('(').Append(name);
            bool block = name == "begin" || name == "begin_random" || name == "if" && (multi || len + indent > 110);
            if (!multi && !block && len + indent <= 110) { foreach (var p in parts) sb.Append(' ').Append(p); }
            else
            {
                string pad = "\n" + new string(' ', indent + 4);
                int k = 0;
                foreach (var p in parts)
                {
                    // keep the condition of an if / the first argument on the call's line
                    if (k == 0 && (name == "if" || name == "sleep_until" || name == "set")) sb.Append(' ').Append(p);
                    else sb.Append(pad).Append(p);
                    k++;
                }
            }
            sb.Append(')');
            return sb.ToString();
        }

        // The compiler turns (cond (c1 e..) (c2 e..)) into (if c1 (begin e..) (if c2 (begin e..) <void constant>)): print it back as cond.
        bool IsCondChain(int i, int depth)
        {
            if (depth > 100 || !_d.Valid(i)) return false;
            int c = FirstArg(i); if (!_d.Valid(c)) return false;
            int t = _d.Nodes[c].Next; if (!_d.Valid(t)) return false;
            int e = _d.Nodes[t].Next; if (!_d.Valid(e)) return false;
            var en = _d.Nodes[e];
            if (en.IsPrimitive) return en.Type == HsType.Void && !en.IsGlobal && !en.IsLocal;
            return !en.IsScriptCall && _d.CallName(e) == "if" && IsCondChain(e, depth + 1);
        }

        string Cond(int i, int depth, int indent)
        {
            var sb = new StringBuilder("(cond");
            string pad = "\n" + new string(' ', indent + 4);
            int guard = 0;
            while (_d.Valid(i) && !_d.Nodes[i].IsPrimitive && guard++ < 200)
            {
                int c = FirstArg(i), t = _d.Nodes[c].Next;
                sb.Append(pad).Append('(').Append(Expr(c, depth + 1, indent + 4));
                // the branch is a begin block: its expressions go straight into the clause
                if (_d.Valid(t) && !_d.Nodes[t].IsPrimitive && !_d.Nodes[t].IsScriptCall && _d.CallName(t) == "begin")
                {
                    int b = FirstArg(t), g2 = 0;
                    while (b >= 0 && g2++ < 1000) { sb.Append(pad).Append("    ").Append(Expr(b, depth + 2, indent + 8)); b = _d.Valid(b) ? _d.Nodes[b].Next : -1; }
                }
                else sb.Append(pad).Append("    ").Append(Expr(t, depth + 2, indent + 8));
                sb.Append(')');
                i = _d.Nodes[t].Next;
            }
            return sb.Append(')').ToString();
        }

        string Primitive(HsNode n)
        {
            if (n.IsLocal)
            {
                int p = (int)n.Data;
                return _script != null && p >= 0 && p < _script.ParamNames.Length ? _script.ParamNames[p] : _d.Str(n.StringOffset);
            }
            if (n.IsGlobal)
            {
                string s = _d.Str(n.StringOffset);
                // scenario global: data = its index; engine (external) global: 0x8000 | the engine's own index (named by the string)
                int gi = (int)(n.Data & 0xFFFF);
                if ((gi & 0x8000) == 0)
                {
                    if (gi < _d.Globals.Length) { Check(n, _d.Globals[gi].Name, s); return _d.Globals[gi].Name; }
                    Mismatch(n, "global index out of range", s);
                }
                else if (HsDefs.GlobalIndex(s) < 0) Mismatch(n, "unknown engine global", s);
                return s;
            }
            string str = _d.Str(n.StringOffset);
            switch (n.Type)
            {
                case HsType.Boolean: return n.Bool ? "true" : "false";
                case HsType.Real: return n.Real.ToString("0.0##", CultureInfo.InvariantCulture);
                case HsType.Short: return n.Short.ToString(CultureInfo.InvariantCulture);
                case HsType.Long: return n.Long.ToString(CultureInfo.InvariantCulture);
                case HsType.String: return "\"" + str + "\"";
                case HsType.Script: return Named(n, n.Short >= 0 && n.Short < _d.Scripts.Length ? _d.Scripts[n.Short].Name : null, str);
                case HsType.TriggerVolume: return Named(n, HsData.At(_d.TriggerVolumes, n.Short), str);
                case HsType.CutsceneFlag: return Named(n, HsData.At(_d.CutsceneFlags, n.Short), str);
                case HsType.CutsceneCameraPoint: return Named(n, HsData.At(_d.CutsceneCameras, n.Short), str);
                case HsType.CutsceneTitle: return Named(n, HsData.At(_d.CutsceneTitles, n.Short), str);
                case HsType.CutsceneRecording: return Named(n, HsData.At(_d.Recordings, n.Short), str);
                case HsType.DeviceGroup: return Named(n, HsData.At(_d.DeviceGroups, n.Short), str);
                case HsType.AiCommandList: return Named(n, HsData.At(_d.CommandLists, n.Short), str);
                case HsType.StartingProfile: return Named(n, HsData.At(_d.StartingProfiles, n.Short), str);
                case HsType.Conversation: return Named(n, HsData.At(_d.Conversations, n.Short), str);
                case HsType.Ai: return Named(n, _d.AiName(n.Data), str);
                case HsType.ObjectName: case HsType.UnitName: case HsType.VehicleName: case HsType.WeaponName: case HsType.DeviceName: case HsType.SceneryName:
                    return Named(n, n.Short == -1 ? "none" : HsData.At(_d.ObjectNames, n.Short), str);
                case HsType.Sound: case HsType.Effect: case HsType.Damage: case HsType.LoopingSound: case HsType.AnimationGraph: case HsType.ActorVariant:
                case HsType.DamageEffect: case HsType.ObjectDefinition:
                {
                    var t = _d.Map != null ? _d.Map.Tag(n.Data) : null;
                    return Named(n, t != null ? t.Name : (n.Data == 0xFFFFFFFF ? "none" : null), str, true);
                }
                default:
                    return str.Length > 0 ? (str.IndexOf(' ') >= 0 ? "\"" + str + "\"" : str) : "<" + HsDefs.TypeName(n.Type) + " " + n.Data.ToString("X8") + ">";
            }
        }

        string Named(HsNode n, string resolved, string str, bool path = false)
        {
            if (resolved == null) { Mismatch(n, "unresolved data " + n.Data.ToString("X8"), str); return str.Length > 0 ? Quote(str) : "<" + HsDefs.TypeName(n.Type) + " " + n.Data.ToString("X8") + ">"; }
            Check(n, resolved, str);
            return Quote(path ? str.Length > 0 ? str : resolved : resolved);
        }

        static string Quote(string s) => s.IndexOf(' ') >= 0 || s.IndexOf('\\') >= 0 ? "\"" + s + "\"" : s;

        void Check(HsNode n, string resolved, string str)
        {
            if (str.Length == 0 || resolved == "none") return;
            string a = resolved.Replace('/', '\\'), b = str.Replace('/', '\\');
            if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) Mismatch(n, resolved, str);
        }

        void Mismatch(HsNode n, string resolved, string str)
        {
            Mismatches++;
            if (MismatchLog.Length < 4000) MismatchLog.Append(HsDefs.TypeName(n.Type)).Append(": data ").Append(n.Data.ToString("X8")).Append(" -> '").Append(resolved).Append("' but string '").Append(str).Append("'\n");
        }
    }
}

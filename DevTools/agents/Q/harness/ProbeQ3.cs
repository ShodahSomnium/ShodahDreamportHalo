using System;
using System.Text;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    // crude script dump (for Q's own reading only; S owns the real VM / decompiler)
    public static int QHsc(string[] a)
    {
        var m = new HaloMap(QMaps + a[1] + ".map");
        var r = m.Scenario.Root;
        byte[] syn = r.DataBytes(1140), str = r.DataBytes(1160);
        int count = BitConverter.ToUInt16(syn, 48);
        string S(int off) { if (off < 0 || off >= str.Length) return "?"; int e = off; while (e < str.Length && str[e] != 0) e++; return Encoding.ASCII.GetString(str, off, e - off); }
        void Node(uint id, StringBuilder sb, int depth)
        {
            if (id == 0xFFFFFFFF) return;
            int i = (int)(id & 0xFFFF); if (i >= count) { sb.Append("<bad>"); return; }
            int at = 56 + i * 20;
            int flags = BitConverter.ToUInt16(syn, at + 6); uint next = BitConverter.ToUInt32(syn, at + 8); int so = BitConverter.ToInt32(syn, at + 12); uint data = BitConverter.ToUInt32(syn, at + 16);
            if ((flags & 1) != 0) { int ty = BitConverter.ToUInt16(syn, at + 4); if ((flags & 4) == 0 && ty == 5) sb.Append(syn[at + 16] != 0 ? "true" : "false"); else if ((flags & 4) == 0 && ty == 6) sb.Append(BitConverter.ToSingle(syn, at + 16).ToString("0.###")); else if ((flags & 4) == 0 && ty == 7) sb.Append(BitConverter.ToInt16(syn, at + 16)); else if ((flags & 4) == 0 && ty == 8) sb.Append(BitConverter.ToInt32(syn, at + 16)); else sb.Append(S(so)); return; }
            sb.Append('\n').Append(new string(' ', depth * 2)).Append('(');
            uint c = data; bool first = true;
            while (c != 0xFFFFFFFF) { if (!first) sb.Append(' '); first = false; Node(c, sb, depth + 1); int ci = (int)(c & 0xFFFF); c = BitConverter.ToUInt32(syn, 56 + ci * 20 + 8); }
            sb.Append(')');
        }
        foreach (var s in r.Block(1180, 92))
        {
            string name = s.Str32(0);
            if (a.Length > 2 && !name.Contains(a[2])) continue;
            var sb = new StringBuilder();
            Node(s.U32(36), sb, 1);
            Console.WriteLine($"(script {s.I16(32)} {name}{sb})\n");
        }
        return 0;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    public static int QAngles(string[] a)
    {
        var flagVals = new Dictionary<int, int>(); var animVals = new Dictionary<int, int>(); var wVals = new Dictionary<int, int>();
        int pitchOut = 0, total = 0;
        foreach (var mn in new[] { "a10", "a30", "a50", "b30", "b40", "c10", "c20", "c40", "d20", "d40" })
        {
            var m = new HaloMap(QMaps + mn + ".map");
            foreach (var c in m.Scenario.Root.Block(876, 64))
            {
                byte[] b = c.DataBytes(44); int ucv = c.I8(34);
                int hp = ucv >= 4 ? 60 : ucv >= 1 ? 58 : 52; int p = hp + 12;
                int yaw = BitConverter.ToInt16(b, hp + 8), pitch = BitConverter.ToInt16(b, hp + 10);
                int y0 = yaw, minP = pitch, maxP = pitch, minY = yaw, maxY = yaw;
                while (p < b.Length)
                {
                    int e = b[p++]; int tc = e & 3, type = e & 0xFC;
                    if (tc == 2) p++; else if (tc == 3) p += 2;
                    if (type == 0x04) break;
                    switch (type)
                    {
                        case 0x08: animVals[b[p]] = animVals.TryGetValue(b[p], out var n1) ? n1 + 1 : 1; p += 1; break;
                        case 0x10: { int v = BitConverter.ToUInt16(b, p); flagVals[v] = flagVals.TryGetValue(v, out var n2) ? n2 + 1 : 1; p += 2; break; }
                        case 0x14: { int v = BitConverter.ToInt16(b, p); wVals[v] = wVals.TryGetValue(v, out var n3) ? n3 + 1 : 1; p += 2; break; }
                        case 0x18: p += 8; break;
                        case 0x38: yaw += (sbyte)b[p]; pitch += (sbyte)b[p + 1]; p += 2; break;
                        case 0x58: yaw += BitConverter.ToInt16(b, p); pitch += BitConverter.ToInt16(b, p + 2); p += 4; break;
                        default: Console.WriteLine("?? " + type); p = b.Length; break;
                    }
                    minP = Math.Min(minP, pitch); maxP = Math.Max(maxP, pitch); minY = Math.Min(minY, yaw); maxY = Math.Max(maxY, yaw);
                }
                total++;
                if (minP < -500 || maxP > 500) pitchOut++;
                if (a.Length > 1 && (a[1] == mn || a[1] == "all")) Console.WriteLine($"{mn} {c.Str32(0),-28} ucv{ucv} hdr anim {b[0]} b1 {b[1]} flags {BitConverter.ToUInt16(b, 2):X} w {BitConverter.ToInt16(b, 4)} b6 {b[6]} b7 {b[7]} thr {BitConverter.ToSingle(b, 8):0.##},{BitConverter.ToSingle(b, 12):0.##} yaw {y0}->{yaw} [{minY},{maxY}] pitch [{minP},{maxP}]");
            }
        }
        Console.WriteLine($"total {total} pitch out of +-500: {pitchOut}");
        Console.WriteLine("anim: " + string.Join(" ", animVals.Select(k => k.Key + "x" + k.Value)));
        Console.WriteLine("flags: " + string.Join(" ", flagVals.OrderBy(k => k.Key).Select(k => k.Key.ToString("X") + "x" + k.Value)));
        Console.WriteLine("weapon: " + string.Join(" ", wVals.Select(k => k.Key + "x" + k.Value)));
        return 0;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    // survey: parse all streams of all campaign maps with a size table; report unknown types
    public static int QSurvey(string[] a)
    {
        var sizes = new Dictionary<int, int>();
        for (int i = 2; i + 1 < a.Length; i += 2) sizes[Convert.ToInt32(a[i], 16)] = int.Parse(a[i + 1]);
        var seen = new Dictionary<int, int>();
        int ok = 0, bad = 0, lenOk = 0;
        foreach (var mn in new[] { "a10", "a30", "a50", "b30", "b40", "c10", "c20", "c40", "d20", "d40" })
        {
            var m = new HaloMap(QMaps + mn + ".map");
            foreach (var c in m.Scenario.Root.Block(876, 64))
            {
                byte[] b = c.DataBytes(44);
                int ucv = c.I8(34); int p = ucv >= 4 ? 72 : 70, ticks = 0; bool end = false; string err = null;
                while (p < b.Length)
                {
                    int e = b[p]; int tc = e & 3, type = e & 0xFC; int at = p; p++;
                    int d = tc == 0 ? 0 : tc == 1 ? 1 : tc == 2 ? b[p++] : (b[p++] | b[p++] << 8);
                    ticks += d;
                    seen[type] = seen.TryGetValue(type, out var n) ? n + 1 : 1;
                    if (type == 0x04) { end = true; break; }
                    if (!sizes.TryGetValue(type, out int sz)) { err = $"unknown {type:X2} at {at}: " + string.Join(" ", b.Skip(Math.Max(52, at - 12)).Take(36).Select(x => x.ToString("X2"))); break; }
                    p += sz;
                }
                if (err != null) { bad++; if (bad < 60) Console.WriteLine($"{mn} {c.Str32(0)} ucv{c.I8(34)} {err}"); }
                else if (end && p == b.Length) { ok++; if (ticks == c.I16(36)) lenOk++; else if (a.Length > 1 && a[1] == "v") Console.WriteLine($"{mn} {c.Str32(0)} ticks {ticks} len {c.I16(36)}"); }
                else { bad++; Console.WriteLine($"{mn} {c.Str32(0)} end {end} p {p} of {b.Length}"); }
            }
        }
        Console.WriteLine($"ok {ok} (ticks==len {lenOk}) bad {bad}");
        foreach (var kv in seen.OrderBy(k => k.Key)) Console.WriteLine($"type {kv.Key:X2} x{kv.Value}");
        return 0;
    }
}

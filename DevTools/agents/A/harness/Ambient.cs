using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static class Ambient
{
    // ambient <map>...: BSP background sound palette (clusters per entry) and the sound scenery's lsnd attachments
    public static int Run(string[] a)
    {
        for (int k = 1; k < a.Length; k++)
        {
            var m = new HaloMap(MainA.Maps + a[k] + ".map");
            var sc = new HaloScenario(m);
            for (int b = 0; b < sc.Bsps.Count; b++)
            {
                var r = sc.BspRoot(b);
                var pal = new List<string>();
                foreach (var e in r.Block(508, 116)) pal.Add(e.Str32(0) + "=" + (e.Ref(32) != null ? e.Ref(32).Name : "none"));
                var count = new int[pal.Count + 1];
                int clusters = 0;
                foreach (var c in r.Block(308, 104)) { clusters++; int i = c.I16(4); if (i >= 0 && i < pal.Count) count[i]++; else count[pal.Count]++; }
                Console.Write($"{a[k]} bsp {b}: {clusters} clusters;");
                for (int i = 0; i < pal.Count; i++) Console.Write($" [{pal[i]} x{count[i]}]");
                Console.WriteLine($" [none x{count[pal.Count]}]");
            }
            var seen = new Dictionary<string, int>();
            foreach (var p in sc.SoundScenery)
            {
                string n = p.Type != null ? p.Type.Name : "?";
                if (p.Type != null)
                    foreach (var at in p.Type.Root.Block(320, 72)) { var t = at.Ref(0); if (t != null) n += " -> " + t; }
                seen[n] = seen.TryGetValue(n, out var c) ? c + 1 : 1;
            }
            foreach (var kv in seen) Console.WriteLine($"   sound scenery x{kv.Value}: {kv.Key}");
        }
        return 0;
    }
}

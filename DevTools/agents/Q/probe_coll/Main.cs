using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static class P
{
    static int Main(string[] a)
    {
        HaloLog.Sink = s => { };
        var m = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\" + a[0] + ".map");
        foreach (var t in m.OfClass("coll"))
        {
            if (t.Name.IndexOf(a[1], StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine("COLL " + t.Name);
            var c = t.Root;
            var mats = new List<string>(); foreach (var mt in c.Block(564, 72)) mats.Add(mt.Str32(0));
            foreach (var n in c.Block(652, 64))
            {
                var cnt = new Dictionary<int, int>(); int bi = 0;
                foreach (var b in n.Block(52, 96))
                {
                    cnt.Clear();
                    foreach (var s in b.Block(60, 12)) { int mi = s.I16(10); cnt.TryGetValue(mi, out var k); cnt[mi] = k + 1; }
                    var sb = ""; foreach (var kv in cnt) sb += (kv.Key >= 0 && kv.Key < mats.Count ? mats[kv.Key] : kv.Key.ToString()) + "=" + kv.Value + " ";
                    Console.WriteLine($"  node '{n.Str32(0)}' region {n.I16(32)} bsp{bi++}: {sb}");
                }
            }
        }
        return 0;
    }
}

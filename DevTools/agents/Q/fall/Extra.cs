using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static class Extra
{
    public static void Run(string name)
    {
        var m = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\" + name + ".map");
        var sc = new HaloScenario(m);
        var cols = new HaloCollisionBsp[sc.Bsps.Count];
        for (int b = 0; b < cols.Length; b++) { var root = sc.BspRoot(b); foreach (var c in root.Block(176, 96)) { cols[b] = new HaloCollisionBsp(c); break; } }
        var r = m.Scenario.Root;
        var byEnc = new Dictionary<string, int[]>();
        foreach (var e in r.Block(1068, 176))
        {
            string en = e.Str32(0); uint fl = e.U32(32); int manual = e.I16(42), pre = e.I16(126);
            int bsp = (fl & 64) != 0 && manual >= 0 ? manual : pre;
            foreach (var sq in e.Block(128, 232))
                foreach (var st in sq.Block(208, 28))
                {
                    float x = st.F32(0), y = st.F32(4), z = st.F32(8);
                    int bi = bsp >= 0 && bsp < cols.Length ? bsp : 0;
                    if (cols[bi].Leaf(x, y, z + 0.1f) >= 0) continue;
                    string inOther = "";
                    for (int b = 0; b < cols.Length; b++) if (cols[b].Leaf(x, y, z + 0.1f) >= 0) inOther += b + " ";
                    string k = en + " (bsp " + bsp + (((fl & 64) != 0) ? " manual" : "") + ") in other: " + (inOther == "" ? "none" : inOther);
                    int[] c2; if (!byEnc.TryGetValue(k, out c2)) byEnc[k] = c2 = new int[1]; c2[0]++;
                }
        }
        foreach (var kv in byEnc) Console.WriteLine("  " + kv.Value[0] + " x " + kv.Key);
    }
}

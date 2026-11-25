using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static class Program
{
    const string M = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static bool Inside2D(HaloCollisionBsp col, HaloCollisionBsp.Surface s, float x, float y)
    {
        int n = s.Ring.Length; var V = col.Vertices;
        for (int k = 0; k < n; k++)
        {
            int p = s.Ring[k] * 3, q = s.Ring[(k + 1) % n] * 3;
            float cr = (V[q] - V[p]) * (y - V[p + 1]) - (V[q + 1] - V[p + 1]) * (x - V[p]);
            if (cr < -1e-5f) return false;
        }
        return true;
    }
    // highest up-facing surface at (x,y) with z <= zTop; returns its z and nz
    static bool Below(HaloCollisionBsp col, float x, float y, float zTop, out float zHit, out float nzHit)
    {
        zHit = -1e9f; nzHit = 0; bool f = false;
        foreach (var s in col.Surfaces)
        {
            float a, b, c, d; col.PlaneOf(s, out a, out b, out c, out d);
            if (c <= 0.05f) continue;
            float z = (d - a * x - b * y) / c;
            if (z > zTop || z <= zHit) continue;
            if (!Inside2D(col, s, x, y)) continue;
            zHit = z; nzHit = c; f = true;
        }
        return f;
    }
    static int Main(string[] a)
    {
        if (a[0] == "e") { for (int i = 1; i < a.Length; i++) Extra5.Run(a[i]); return 0; }
        if (a[0] == "w") { Extra4.Run(a[1], float.Parse(a[2]), float.Parse(a[3]), float.Parse(a[4]), float.Parse(a[5])); return 0; }
        if (a[0] == "z") { for (int i = 1; i < a.Length; i++) Extra3.Run(a[i]); return 0; }
        if (a[0] == "y") { Extra2.Run(a[1], a[2]); return 0; }
        if (a[0] == "x") { for (int i = 1; i < a.Length; i++) { Console.WriteLine("== " + a[i]); Extra.Run(a[i]); } return 0; }
        foreach (var name in a)
        {
            var m = new HaloMap(M + name + ".map");
            var sc = new HaloScenario(m);
            var cols = new HaloCollisionBsp[sc.Bsps.Count];
            for (int b = 0; b < cols.Length; b++) { var root = sc.BspRoot(b); foreach (var c in root.Block(176, 96)) { cols[b] = new HaloCollisionBsp(c); break; } }
            var r = m.Scenario.Root;
            int total = 0, below = 0, high = 0, none = 0, steep = 0, outside = 0, noBsp = 0;
            var ex = new List<string>();
            foreach (var e in r.Block(1068, 176))
            {
                string en = e.Str32(0); uint fl = e.U32(32); int manual = e.I16(42), pre = e.I16(126);
                int bsp = (fl & 64) != 0 && manual >= 0 ? manual : pre;
                if (bsp < 0) noBsp++;
                foreach (var sq in e.Block(128, 232))
                    foreach (var st in sq.Block(208, 28))
                    {
                        total++;
                        float x = st.F32(0), y = st.F32(4), z = st.F32(8);
                        int bi = bsp >= 0 && bsp < cols.Length ? bsp : 0;
                        var col = cols[bi];
                        if (col.Leaf(x, y, z + 0.1f) < 0) outside++;
                        float zh, nz;
                        // our snap: ray from start+0.6 m (0.197 wu) down 4.6 m (1.509 wu)
                        if (!Below(col, x, y, z + 0.197f, out zh, out nz)) { none++; if (ex.Count < 12) ex.Add($"  none: {en}/{sq.Str32(0)} ({x:0.##},{y:0.##},{z:0.##}) bsp {bi}"); continue; }
                        float dz = z - zh;
                        if (dz > 1.312f) { high++; if (ex.Count < 12) ex.Add($"  high {dz * 3.048f:0.00} m: {en}/{sq.Str32(0)}"); }
                        if (nz <= 0.5f) { steep++; if (ex.Count < 12) ex.Add($"  steep nz {nz:0.00} under {en}/{sq.Str32(0)}"); }
                        // is there a floor ABOVE the start within 1 m (start under the floor)?
                        float zh2, nz2;
                        if (Below(col, x, y, z + 0.5f, out zh2, out nz2) && zh2 > z + 0.197f) { below++; if (ex.Count < 12) ex.Add($"  start {(zh2 - z) * 3.048f:0.00} m UNDER a floor: {en}/{sq.Str32(0)}"); }
                    }
            }
            Console.WriteLine($"{name}: starts {total}; encounters without bsp {noBsp}; outside their bsp {outside}; no floor below {none}; >4 m above floor {high}; floor under is steep {steep}; start 0.6-1.5 m under a floor {below}");
            foreach (var s in ex) Console.WriteLine(s);
        }
        return 0;
    }
}

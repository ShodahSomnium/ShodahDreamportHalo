using System;
using SomniumSpace.Worlds.SomniumHalo;
static class Extra3
{
    public static void Run(string name)
    {
        var m = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\" + name + ".map");
        var sc = new HaloScenario(m);
        var cols = new HaloCollisionBsp[sc.Bsps.Count];
        for (int b = 0; b < cols.Length; b++) { var root = sc.BspRoot(b); foreach (var c in root.Block(176, 96)) { cols[b] = new HaloCollisionBsp(c); break; } }
        var hist = new int[8]; var dirs = new int[3]; int n = 0;
        foreach (var e in m.Scenario.Root.Block(1068, 176))
        {
            uint fl = e.U32(32); int manual = e.I16(42), pre = e.I16(126);
            int bsp = (fl & 64) != 0 && manual >= 0 ? manual : pre; if (bsp < 0) bsp = 0;
            foreach (var sq in e.Block(128, 232))
                foreach (var st in sq.Block(208, 28))
                {
                    float x = st.F32(0), y = st.F32(4), z = st.F32(8);
                    if (cols[bsp].Leaf(x, y, z + 0.1f) >= 0) continue;
                    n++;
                    float best = 99; int bd = -1;
                    for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        float l = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                        for (float d = 0.02f; d < 3f && d < best; d += 0.02f)
                            if (cols[bsp].Leaf(x + dx / l * d, y + dy / l * d, z + dz / l * d) >= 0) { best = d; bd = dz == 0 ? 0 : dz > 0 ? 1 : 2; break; }
                    }
                    int h = best < 0.1f ? 0 : best < 0.2f ? 1 : best < 0.4f ? 2 : best < 0.8f ? 3 : best < 1.6f ? 4 : best < 3f ? 5 : 6;
                    hist[h]++; if (bd >= 0) dirs[bd]++;
                }
        }
        Console.WriteLine($"{name}: outside starts {n}; nearest inside point <0.3m {hist[0]}, <0.6m {hist[1]}, <1.2m {hist[2]}, <2.4m {hist[3]}, <4.9m {hist[4]}, <9m {hist[5]}, none {hist[6]}; direction horizontal {dirs[0]} up {dirs[1]} down {dirs[2]}");
    }
}

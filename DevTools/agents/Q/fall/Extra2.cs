using System;
using SomniumSpace.Worlds.SomniumHalo;
static class Extra2
{
    public static void Run(string name, string encPrefix)
    {
        var m = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\" + name + ".map");
        var sc = new HaloScenario(m);
        var cols = new HaloCollisionBsp[sc.Bsps.Count];
        for (int b = 0; b < cols.Length; b++) { var root = sc.BspRoot(b); foreach (var c in root.Block(176, 96)) { cols[b] = new HaloCollisionBsp(c); break; } }
        int shown = 0;
        foreach (var e in m.Scenario.Root.Block(1068, 176))
        {
            string en = e.Str32(0); if (!en.StartsWith(encPrefix)) continue;
            uint fl = e.U32(32); int manual = e.I16(42), pre = e.I16(126);
            int bsp = (fl & 64) != 0 && manual >= 0 ? manual : pre; if (bsp < 0) bsp = 0;
            foreach (var sq in e.Block(128, 232))
                foreach (var st in sq.Block(208, 28))
                {
                    float x = st.F32(0), y = st.F32(4), z = st.F32(8);
                    if (cols[bsp].Leaf(x, y, z + 0.1f) >= 0) continue;
                    // distance to the closest surface plane (any orientation) whose ring contains the projection
                    float nx, ny, nz; bool on = cols[bsp].SurfaceAt(x, y, z, 0.5f, out nx, out ny, out nz);
                    // probe up / down for being inside: first point above that is inside the bsp
                    float up = -1; for (float d = 0.02f; d < 3f; d += 0.02f) if (cols[bsp].Leaf(x, y, z + d) >= 0) { up = d; break; }
                    if (shown++ < 400) Console.WriteLine($"  {en}/{sq.Str32(0)} @@ ({x:0.00},{y:0.00},{z:0.00}) surface within 0.5wu: {on} n=({nx:0.00},{ny:0.00},{nz:0.00}); inside {up * 3.048f:0.00} m higher");
                }
        }
    }
}

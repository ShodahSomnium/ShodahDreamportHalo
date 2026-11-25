using System;
using System.Linq;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static double[] Solve(double[,] A, double[] y)
    {
        int n = A.GetLength(1), m = A.GetLength(0);
        var M = new double[n, n + 1];
        for (int r = 0; r < m; r++) for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) M[i, j] += A[r, i] * A[r, j]; M[i, n] += A[r, i] * y[r]; }
        for (int i = 0; i < n; i++) { int p = i; for (int k = i + 1; k < n; k++) if (Math.Abs(M[k, i]) > Math.Abs(M[p, i])) p = k; for (int j = 0; j <= n; j++) { var t = M[i, j]; M[i, j] = M[p, j]; M[p, j] = t; }
            for (int k = 0; k < n; k++) if (k != i) { double f = M[k, i] / M[i, i]; for (int j = 0; j <= n; j++) M[k, j] -= f * M[i, j]; } }
        var x = new double[n]; for (int i = 0; i < n; i++) x[i] = M[i, n] / M[i, i]; return x;
    }
    static int Detail2(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        var b = new HaloBsp(sc, 0);
        var d = b.DetailObjects;
        var cells = d.Block(0, 32).ToList(); var inst = d.Block(12, 6).ToList(); var counts = d.Block(24, 2).Select(x => (int)x.U16(0)).ToList(); var zr = d.Block(36, 16).ToList();
        float S = 8f;
        foreach (int ci in new[] { 20, 45, 80, 120 })
        {
            var c = cells[ci];
            int cx = c.I16(0), cy = c.I16(2), cz = c.I16(4), oz = c.I16(6), start = c.I32(12), cnt = counts[c.I32(16)];
            var z = zr[c.I32(16)];
            var rows = new List<double[]>(); var ys = new List<double>();
            for (int k = 0; k < cnt; k++)
            {
                var i = inst[start + k];
                double xl = i.U8(0) / 256.0, yl = i.U8(1) / 256.0, zb = i.I8(2) / 128.0;
                float gz = GroundZ(b, (float)(cx + xl) * S, (float)(cy + yl) * S);
                if (float.IsNaN(gz)) continue;
                rows.Add(new[] { xl, yl, zb, 1.0 }); ys.Add((gz - cz * S) / S);
            }
            if (rows.Count < 6) continue;
            var A = new double[rows.Count, 4]; for (int r = 0; r < rows.Count; r++) for (int j = 0; j < 4; j++) A[r, j] = rows[r][j];
            var sol = Solve(A, ys.ToArray());
            double err = 0; for (int r = 0; r < rows.Count; r++) { double p = 0; for (int j = 0; j < 4; j++) p += sol[j] * rows[r][j]; err += Math.Abs(p - ys[r]); }
            Console.WriteLine($"cell {cx},{cy},{cz} offz {oz} n {cnt} fit {rows.Count}: z_local = {sol[0]:0.000} x + {sol[1]:0.000} y + {sol[2]:0.000} zb + {sol[3]:0.000} (mean err {err / rows.Count:0.0000}) | zref {z.F32(0):0.000},{z.F32(4):0.000},{z.F32(8):0.000},{z.F32(12):0.000}");
        }
        return 0;
    }
}

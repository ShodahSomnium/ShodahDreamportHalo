using System;
using System.Linq;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Detail3(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        var b = new HaloBsp(sc, 0);
        var d = b.DetailObjects;
        var cells = d.Block(0, 32).ToList(); var inst = d.Block(12, 6).ToList(); var counts = d.Block(24, 2).Select(x => (int)x.U16(0)).ToList(); var zr = d.Block(36, 16).ToList();
        float S = 8f;
        var R = new List<double[]>(); var Y = new List<double>();
        for (int ci = 0; ci < cells.Count; ci += 3)
        {
            var c = cells[ci];
            int cx = c.I16(0), cy = c.I16(2), cz = c.I16(4), oz = c.I16(6), start = c.I32(12), cnt = counts[c.I32(16)];
            var z = zr[c.I32(16)];
            for (int k = 0; k < cnt; k += 7)
            {
                var i = inst[start + k];
                double xl = i.U8(0) / 256.0, yl = i.U8(1) / 256.0;
                float gz = GroundZ(b, (float)(cx + xl) * S, (float)(cy + yl) * S);
                if (float.IsNaN(gz)) continue;
                double t = (gz - cz * S) / S - (z.F32(0) * xl + z.F32(4) * yl + z.F32(12));
                if (Math.Abs(t) > 2) continue;
                R.Add(new double[] { z.F32(8), oz / 128.0, z.F32(8) * oz / 128.0, i.I8(2) / 128.0, z.F32(8) * i.I8(2) / 128.0, 1 }); Y.Add(t);
            }
        }
        var A = new double[R.Count, 6]; for (int r = 0; r < R.Count; r++) for (int j = 0; j < 6; j++) A[r, j] = R[r][j];
        var s = Solve(A, Y.ToArray());
        double err = 0; for (int r = 0; r < R.Count; r++) { double p = 0; for (int j = 0; j < 6; j++) p += s[j] * R[r][j]; err += Math.Abs(p - Y[r]); }
        Console.WriteLine($"n {R.Count}: residual = {s[0]:0.000} k + {s[1]:0.000} offz + {s[2]:0.000} k*offz + {s[3]:0.000} zb + {s[4]:0.000} k*zb + {s[5]:0.000}  mean err {err / R.Count:0.0000}");
        return 0;
    }
}

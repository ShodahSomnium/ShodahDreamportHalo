using System;
using System.Collections.Generic;
using System.Globalization;
using SomniumSpace.Worlds.SomniumHalo;
// pic <chainObj> <z> <x0> <y0> <x1> <y1> <out.png> model:scale ...
static class MapPic
{
    public static int Run(string[] a)
    {
        var inv = CultureInfo.InvariantCulture;
        string obj = a[1]; double z0 = double.Parse(a[2], inv), x0 = double.Parse(a[3], inv), y0 = double.Parse(a[4], inv), x1 = double.Parse(a[5], inv), y1 = double.Parse(a[6], inv);
        string outp = a[7];
        Drop.Load();
        const double px = 0.25; int w = (int)((x1 - x0) / px), h = (int)((y1 - y0) / px);
        var img = new byte[w * h * 4];
        for (int j = 0; j < h; j++) for (int i = 0; i < w; i++)
        {
            double x = x0 + i * px, y = y1 - j * px;
            bool o0 = Drop.Open(x, y, z0), o1 = Drop.Open(x, y, z0 + 1.7), o2 = Drop.Open(x, y, z0 + 3.4);
            byte v = (byte)(o0 && o1 && o2 ? 235 : (!o0 && !o1 && !o2) ? 40 : 120);
            int k = (j * w + i) * 4; img[k] = v; img[k + 1] = v; img[k + 2] = v; img[k + 3] = 255;
        }
        byte[][] colors = { new byte[]{230,30,30}, new byte[]{30,90,230}, new byte[]{20,170,40}, new byte[]{230,140,0}, new byte[]{170,0,170} };
        for (int m = 8; m < a.Length; m++)
        {
            var parts = a[m].Split(':'); var col = colors[(m - 8) % colors.Length];
            foreach (var p in Drop.Path(obj, parts[0], double.Parse(parts[1], inv)))
            {
                int i = (int)((p[0] - x0) / px), j = (int)((y1 - p[1]) / px);
                int r = ((int)p[3] % 30 == 0) ? 2 : 0;
                for (int dj = -r; dj <= r; dj++) for (int di = -r; di <= r; di++)
                {
                    int ii = i + di, jj = j + dj; if (ii < 0 || jj < 0 || ii >= w || jj >= h) continue;
                    int k = (jj * w + ii) * 4; img[k] = col[0]; img[k + 1] = col[1]; img[k + 2] = col[2];
                }
            }
        }
        Png.Write(outp, w, h, img);
        Console.WriteLine("wrote " + outp + " " + w + "x" + h);
        return 0;
    }
}

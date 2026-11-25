using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    // cube3 map name out mode(0 face-major,1 mip-major): grid 3x2 faces, scaled 3x
    static int Cube3(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var res = HaloResources.Get(Maps + "bitmaps.map");
        var t = m.OfClass("bitm").First(x => x.Name.EndsWith(a[2]));
        var img = new HaloBitmap(t).Images[0];
        var raw = HaloBitmap.Raw(m, img, res);
        int w = img.Width, S = 3; int mode = int.Parse(a[4]);
        int ls = HaloBitmap.LevelSize(img.Format, w, w);
        int W2 = w * S * 3, H2 = w * S * 2;
        var o = new byte[W2 * H2 * 4];
        for (int f = 0; f < 6; f++)
        {
            byte[] px;
            if (mode == 0) px = HaloBitmap.DecodeRgba(img, raw, f, 0);
            else { var sub = new byte[ls]; Buffer.BlockCopy(raw, f * ls, sub, 0, ls); px = HaloBitmap.DecodeRgba(new HaloBitmap.Image { Width = w, Height = w, Format = img.Format }, sub, 0, 0); }
            int gx = (f % 3) * w * S, gy = (f / 3) * w * S;
            for (int y = 0; y < w * S; y++) for (int x = 0; x < w * S; x++)
            {
                int si = ((y / S) * w + x / S) * 4, di = ((gy + y) * W2 + gx + x) * 4;
                o[di] = px[si]; o[di + 1] = px[si + 1]; o[di + 2] = px[si + 2]; o[di + 3] = 255;
            }
        }
        Png.Write(a[3], W2, H2, o);
        return 0;
    }
}

using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    // rgb <map> <bitmap name end> <out.png> : rgb with alpha forced 255, plus an alpha-as-grey image beside it
    static int Rgb(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var res = HaloResources.Get(Maps + "bitmaps.map");
        var t = m.OfClass("bitm").First(x => x.Name.EndsWith(a[2]));
        var img = new HaloBitmap(t).Images[a.Length > 4 ? int.Parse(a[4]) : 0];
        var px = HaloBitmap.DecodeRgba(img, HaloBitmap.Raw(m, img, res));
        int w = img.Width, h = img.Height;
        var o = new byte[w * 2 * h * 4];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int s = (y * w + x) * 4, d1 = (y * w * 2 + x) * 4, d2 = (y * w * 2 + w + x) * 4;
            o[d1] = px[s]; o[d1 + 1] = px[s + 1]; o[d1 + 2] = px[s + 2]; o[d1 + 3] = 255;
            o[d2] = o[d2 + 1] = o[d2 + 2] = px[s + 3]; o[d2 + 3] = 255;
        }
        Png.Write(a[3], w * 2, h, o);
        Console.WriteLine($"{t.Name} {w}x{h} {img.Format} corner rgba {px[0]},{px[1]},{px[2]},{px[3]}");
        return 0;
    }
}

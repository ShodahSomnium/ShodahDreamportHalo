using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Cube(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var res = HaloResources.Get(Maps + "bitmaps.map");
        var t = m.OfClass("bitm").First(x => x.Name.Contains(a[2]));
        var bm = new HaloBitmap(t);
        var img = bm.Images[0];
        Console.WriteLine($"{t.Name} {img.Width}x{img.Height} type {img.Type} fmt {img.Format} mips {img.Mips}");
        var raw = HaloBitmap.Raw(m, img, res);
        int w = img.Width;
        var strip = new byte[w * 6 * w * 4];
        for (int f = 0; f < 6; f++)
        {
            var px = HaloBitmap.DecodeRgba(img, raw, f, 0);
            for (int y = 0; y < w; y++) Buffer.BlockCopy(px, y * w * 4, strip, (y * w * 6 + f * w) * 4, w * 4);
        }
        for (int i = 3; i < strip.Length; i += 4) strip[i] = 255;
        Png.Write(a[3], w * 6, w, strip);
        return 0;
    }
}

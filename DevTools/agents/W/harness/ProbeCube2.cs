using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Cube2(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var res = HaloResources.Get(Maps + "bitmaps.map");
        var t = m.OfClass("bitm").First(x => x.Name.Contains(a[2]));
        var img = new HaloBitmap(t).Images[0];
        var raw = HaloBitmap.Raw(m, img, res);
        int w = img.Width; int ls = HaloBitmap.LevelSize(img.Format, w, w);
        Console.WriteLine($"raw {raw.Length} level0 {ls} x6 = {ls*6}");
        var strip = new byte[w * 6 * w * 4];
        for (int f = 0; f < 6; f++)
        {
            var sub = new byte[ls]; Buffer.BlockCopy(raw, f * ls, sub, 0, ls);
            var one = new HaloBitmap.Image { Width = w, Height = w, Format = img.Format, Type = 0, Mips = 0 };
            var px = HaloBitmap.DecodeRgba(one, sub, 0, 0);
            for (int y = 0; y < w; y++) Buffer.BlockCopy(px, y * w * 4, strip, (y * w * 6 + f * w) * 4, w * 4);
        }
        for (int i = 3; i < strip.Length; i += 4) strip[i] = 255;
        Png.Write(a[3], w * 6, w, strip);
        return 0;
    }
}

using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class F
{
    // sheet <bitmap name end> <cropW> <cropH> <cols> <out>
    public static int Sheet(string[] a)
    {
        var m = new HaloMap(Maps + "ui.map");
        var res = HaloResources.Get(Maps + "bitmaps.map");
        var t = m.Tags.First(x => x.ClassName == "bitm" && x.Name.EndsWith(a[1]));
        var b = new HaloBitmap(t);
        int cw = int.Parse(a[2]), ch = int.Parse(a[3]), cols = int.Parse(a[4]);
        int n = b.Images.Count, rows = (n + cols - 1) / cols;
        int W = cols * (cw + 4), H = rows * (ch + 4);
        var o = new byte[W * H * 4];
        for (int i = 0; i < n; i++)
        {
            var img = b.Images[i];
            var px = HaloBitmap.DecodeRgba(img, HaloBitmap.Raw(m, img, res));
            int ox = (i % cols) * (cw + 4), oy = (i / cols) * (ch + 4);
            for (int y = 0; y < ch && y < img.Height; y++) for (int x = 0; x < cw && x < img.Width; x++)
            { int s = (y * img.Width + x) * 4, d = ((oy + y) * W + ox + x) * 4; o[d] = px[s]; o[d + 1] = px[s + 1]; o[d + 2] = px[s + 2]; o[d + 3] = 255; }
        }
        Png.Write(a[5], W, H, o);
        return 0;
    }
}

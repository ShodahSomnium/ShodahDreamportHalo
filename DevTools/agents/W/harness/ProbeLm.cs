using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int LmStats(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        var b = new HaloBsp(sc, 0);
        var res = HaloResources.Get(Maps + "bitmaps.map");
        var bm = new HaloBitmap(b.LightmapBitmap);
        string outDir = a.Length > 2 ? a[2] : null;
        if (outDir != null) Directory.CreateDirectory(outDir);
        for (int i = 0; i < bm.Images.Count; i++)
        {
            var img = bm.Images[i];
            var px = HaloBitmap.DecodeRgba(img, HaloBitmap.Raw(m, img, res));
            var lum = new int[px.Length / 4];
            for (int k = 0; k < lum.Length; k++) lum[k] = Math.Max(px[k * 4], Math.Max(px[k * 4 + 1], px[k * 4 + 2]));
            Array.Sort(lum);
            Console.WriteLine($"page {i} {img.Width}x{img.Height} {img.Format}: max-channel p10 {lum[lum.Length/10]} p50 {lum[lum.Length/2]} p90 {lum[lum.Length*9/10]} max {lum[lum.Length-1]}");
            if (outDir != null) Png.Write(Path.Combine(outDir, "lm" + i + ".png"), img.Width, img.Height, px);
        }
        return 0;
    }
}

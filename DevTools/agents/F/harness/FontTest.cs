using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class F
{
    public static int FontTest(string[] a)
    {
        var m = new HaloMap(Maps + "ui.map");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var f = HaloUiFont.Prepare(m.Find("font", a[1]));
        Console.WriteLine($"{f.Name}: {f.Glyphs.Count} glyphs atlas {f.AtlasWidth}x{f.AtlasHeight} asc {f.Ascent} desc {f.Descent} in {sw.ElapsedMilliseconds} ms");
        string text = a.Length > 2 ? a[2] : "MULTIPLAYER Blood Gulch, Hang 'Em High 0123";
        int sign = a.Length > 3 ? int.Parse(a[3]) : 1;
        int W = 1600, H = (int)(f.Ascent + f.Descent) + 40;
        var rgba = new byte[W * H * 4];
        float pen = 10, baseline = f.Ascent + 20;
        foreach (char ch in text)
        {
            var g = f.Glyphs.FirstOrDefault(x => x.Code == ch);
            if (g == null) { pen += 30; continue; }
            if (g.W > 0)
            {
                float left = pen + (sign > 0 ? g.BearingX : -g.BearingX - 2 * HaloUiFont.Spread);
                float top = baseline - g.BearingY;
                for (int y = 0; y < g.H; y++) for (int x = 0; x < g.W; x++)
                {
                    int ax = g.X + x, ay = g.Y + (g.H - 1 - y); // bottom-up atlas
                    byte v = f.Atlas[ay * f.AtlasWidth + ax];
                    int px = (int)left + x, py = (int)top + y;
                    if (px < 0 || py < 0 || px >= W || py >= H) continue;
                    int o = (py * W + px) * 4;
                    byte c = (byte)(v >= 128 ? 255 : 0);
                    rgba[o] = Math.Max(rgba[o], c); rgba[o + 1] = Math.Max(rgba[o + 1], v); rgba[o + 2] = Math.Max(rgba[o + 2], c); rgba[o + 3] = 255;
                }
            }
            pen += g.Advance;
        }
        for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        Png.Write(a.Length > 4 ? a[4] : "../png/fonttest.png", W, H, rgba);
        // the atlas
        var at = new byte[f.AtlasWidth * f.AtlasHeight * 4];
        for (int y = 0; y < f.AtlasHeight; y++) for (int x = 0; x < f.AtlasWidth; x++) { byte v = f.Atlas[(f.AtlasHeight - 1 - y) * f.AtlasWidth + x]; int o = (y * f.AtlasWidth + x) * 4; at[o] = at[o + 1] = at[o + 2] = v; at[o + 3] = 255; }
        Png.Write("../png/atlas.png", f.AtlasWidth, f.AtlasHeight, at);
        return 0;
    }
    public static int Used(string[] a)
    {
        var m = new HaloMap(Maps + "ui.map");
        var res = HaloResources.Get(Maps + "bitmaps.map");
        var t = m.Tags.First(x => x.ClassName == "bitm" && x.Name.EndsWith(a[1]));
        var b = new HaloBitmap(t);
        for (int i = 0; i < b.Images.Count; i++)
        {
            var img = b.Images[i];
            var px = HaloBitmap.DecodeRgba(img, HaloBitmap.Raw(m, img, res));
            int mx = 0, my = 0;
            for (int y = 0; y < img.Height; y++) for (int x = 0; x < img.Width; x++) { int o = (y * img.Width + x) * 4; if (px[o + 3] > 8 && px[o] + px[o + 1] + px[o + 2] > 12) { mx = Math.Max(mx, x); my = Math.Max(my, y); } }
            Console.Write($"[{i}] {img.Width}x{img.Height} used to {mx + 1}x{my + 1}  ");
        }
        Console.WriteLine();
        return 0;
    }
}

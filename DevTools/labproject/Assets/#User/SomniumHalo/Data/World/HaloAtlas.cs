using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Packs a BSP's lightmap pages into one RGBA32 atlas (shelf packing, tallest first, power-of-two width), each page with a border
    /// of its own edge texels so bilinear filtering never reads a neighbour. One atlas = one material per shader for the whole BSP
    /// (the meshes of a shader combine; few draw calls). Rows as stored (v = 0 at the top, like every Halo texture here). Unity-free.
    /// </summary>
    public sealed class HaloAtlas
    {
        public const int Pad = 2;

        public struct Rect { public int X, Y, W, H; }

        public int Width, Height;
        public byte[] Rgba;
        public readonly Dictionary<int, Rect> Rects = new Dictionary<int, Rect>();

        /// <summary>Atlas uv of a page uv.</summary>
        public void Map(int page, float u, float v, out float au, out float av)
        {
            Rect r;
            if (!Rects.TryGetValue(page, out r)) { au = u; av = v; return; }
            au = (r.X + u * r.W) / Width;
            av = (r.Y + v * r.H) / Height;
        }

        public static HaloAtlas Pack(Dictionary<int, HaloLightGrid.Page> pages, int maxSize = 4096)
        {
            var a = new HaloAtlas();
            var order = new List<int>(pages.Keys);
            order.Sort((x, y) => pages[y].Height != pages[x].Height ? pages[y].Height.CompareTo(pages[x].Height) : x.CompareTo(y));
            long area = 0;
            foreach (int k in order) area += (long)(pages[k].Width + 2 * Pad) * (pages[k].Height + 2 * Pad);
            int width = 64;
            while ((long)width * width < area && width < maxSize) width *= 2;
            for (; ; )
            {
                a.Rects.Clear();
                int x = 0, y = 0, shelf = 0;
                bool ok = true;
                foreach (int k in order)
                {
                    int w = pages[k].Width + 2 * Pad, h = pages[k].Height + 2 * Pad;
                    if (w > width) { ok = false; break; }
                    if (x + w > width) { y += shelf; x = 0; shelf = 0; }
                    a.Rects[k] = new Rect { X = x + Pad, Y = y + Pad, W = pages[k].Width, H = pages[k].Height };
                    x += w; shelf = Math.Max(shelf, h);
                }
                int height = y + shelf;
                if (ok && height <= width) { a.Width = width; a.Height = 1; while (a.Height < height) a.Height *= 2; break; }
                if (width >= maxSize) { a.Width = width; a.Height = 1; while (a.Height < height) a.Height *= 2; break; }
                width *= 2;
            }
            a.Rgba = new byte[a.Width * a.Height * 4];
            foreach (var kv in a.Rects)
            {
                var p = pages[kv.Key]; var r = kv.Value;
                for (int yy = -Pad; yy < r.H + Pad; yy++)
                {
                    int sy = Math.Max(0, Math.Min(r.H - 1, yy));
                    int dy = r.Y + yy;
                    if (dy < 0 || dy >= a.Height) continue;
                    for (int xx = -Pad; xx < r.W + Pad; xx++)
                    {
                        int sx = Math.Max(0, Math.Min(r.W - 1, xx));
                        int dx = r.X + xx;
                        if (dx < 0 || dx >= a.Width) continue;
                        Buffer.BlockCopy(p.Rgba, (sy * p.Width + sx) * 4, a.Rgba, (dy * a.Width + dx) * 4, 4);
                    }
                }
            }
            return a;
        }
    }
}

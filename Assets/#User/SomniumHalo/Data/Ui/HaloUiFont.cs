using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] A Halo font tag (ui.map: ui\large_ui, ui\small_ui) turned into a signed distance field atlas TextMeshPro can draw at any
    /// size, Unity-free (worker thread). The tag holds every character's coverage bitmap (8 bits a pixel, row by row, at "pixels offset"
    /// in the font's pixel data) with its advance and origin (FontCharacter: character, character width, bitmap width / height, origin x / y).
    /// Each glyph is enlarged <see cref="Scale"/> times (bilinear), cut at half coverage and its Euclidean distance field computed
    /// (Felzenszwalb's two-pass transform), <see cref="Spread"/> atlas pixels each way, packed into rows of an Alpha8 atlas.
    /// Atlas rows are bottom-up (Unity's order). Metrics in atlas pixels (font pixels x Scale).
    /// </summary>
    public sealed class HaloUiFont
    {
        public const int Scale = 6, Spread = 6;

        public sealed class Glyph
        {
            public int Code;
            public float Advance;               // atlas pixels
            public int X, Y, W, H;              // the glyph's rect in the atlas (Y from the bottom), padding included
            public float BearingX, BearingY;    // from the pen to the rect's left / top edge (y up), padding included
        }

        public string Name;
        public int AtlasWidth, AtlasHeight;
        public byte[] Atlas;                    // Alpha8, bottom row first
        public float Ascent, Descent, Leading;  // atlas pixels
        public readonly List<Glyph> Glyphs = new List<Glyph>();

        public static HaloUiFont Prepare(HaloTag font)
        {
            if (font == null) return null;
            TagReader r = font.Root;
            byte[] pixels = r.DataBytes(136);
            if (pixels == null || pixels.Length == 0) return null;
            var f = new HaloUiFont
            {
                Name = font.Name,
                Ascent = r.I16(4) * Scale, Descent = r.I16(6) * Scale, Leading = r.I16(8) * Scale,
            };

            // the glyphs' fields first, then the packing
            var fields = new List<float[]>();
            var sizes = new List<int[]>();
            foreach (TagReader c in r.Block(124, 20))
            {
                int code = c.U16(0);
                if (code < 32 || code > 0x2122) continue;
                int adv = c.I16(2), bw = c.I16(4), bh = c.I16(6), ox = c.I16(8), oy = c.I16(10), off = c.I32(16);
                var g = new Glyph { Code = code, Advance = adv * Scale };
                if (bw <= 0 || bh <= 0 || off < 0 || off + bw * bh > pixels.Length)
                {
                    g.W = g.H = 0;
                    f.Glyphs.Add(g);
                    fields.Add(null);
                    sizes.Add(null);
                    continue;
                }

                int w = bw * Scale + Spread * 2, h = bh * Scale + Spread * 2;
                float[] field = Field(pixels, off, bw, bh, w, h);
                g.W = w; g.H = h;
                // origin y: the bitmap's top row is that far above the baseline
                g.BearingX = ox * Scale - Spread;      // the bitmap starts origin x to the right of the pen (checked by a harness render: even spacing)
                g.BearingY = oy * Scale + Spread;
                f.Glyphs.Add(g);
                fields.Add(field);
                sizes.Add(new[] { w, h });
            }

            // shelf packing into a 1024- or 2048-wide atlas
            int atlasW = 1024;
            int rowH = 0, x = 1, y = 1;
            for (int pass = 0; pass < 2; pass++)
            {
                x = 1; y = 1; rowH = 0;
                for (int i = 0; i < f.Glyphs.Count; i++)
                {
                    Glyph g = f.Glyphs[i];
                    if (g.W == 0) continue;
                    if (x + g.W + 1 > atlasW) { x = 1; y += rowH + 1; rowH = 0; }
                    g.X = x; g.Y = y;      // top-down for now
                    x += g.W + 1;
                    rowH = Math.Max(rowH, g.H);
                }

                if (y + rowH + 1 <= atlasW || pass == 1) break;
                atlasW = 2048;
            }

            int atlasH = 64;
            while (atlasH < y + rowH + 1) atlasH *= 2;
            f.AtlasWidth = atlasW; f.AtlasHeight = atlasH;
            f.Atlas = new byte[atlasW * atlasH];
            for (int i = 0; i < f.Glyphs.Count; i++)
            {
                Glyph g = f.Glyphs[i];
                float[] field = fields[i];
                if (field == null) continue;
                for (int gy = 0; gy < g.H; gy++)
                {
                    int dstRow = atlasH - 1 - (g.Y + gy);      // bottom-up
                    for (int gx = 0; gx < g.W; gx++)
                    {
                        float d = field[gy * g.W + gx];        // signed distance in pixels, + inside
                        float v = 0.5f + d / (2f * Spread);
                        if (v < 0f) v = 0f; else if (v > 1f) v = 1f;
                        f.Atlas[dstRow * atlasW + g.X + gx] = (byte)(v * 255f + 0.5f);
                    }
                }

                g.Y = atlasH - (g.Y + g.H);      // the rect from the bottom
            }

            return f;
        }

        /// <summary>The glyph's coverage enlarged (bilinear, pixel centres), cut at 0.5, and its signed distance (+ inside) in enlarged pixels.</summary>
        static float[] Field(byte[] pixels, int off, int bw, int bh, int w, int h)
        {
            var inside = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                float sy = (y - Spread + 0.5f) / Scale - 0.5f;
                for (int x = 0; x < w; x++)
                {
                    float sx = (x - Spread + 0.5f) / Scale - 0.5f;
                    inside[y * w + x] = Sample(pixels, off, bw, bh, sx, sy) >= 0.5f;
                }
            }

            float[] din = Edt(inside, w, h, true), dout = Edt(inside, w, h, false);
            var d = new float[w * h];
            for (int i = 0; i < d.Length; i++) d[i] = inside[i] ? (float)Math.Sqrt(din[i]) - 0.5f : 0.5f - (float)Math.Sqrt(dout[i]);
            return d;
        }

        static float Sample(byte[] p, int off, int bw, int bh, float x, float y)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            float a = Px(p, off, bw, bh, x0, y0), b = Px(p, off, bw, bh, x0 + 1, y0), c = Px(p, off, bw, bh, x0, y0 + 1), d = Px(p, off, bw, bh, x0 + 1, y0 + 1);
            return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
        }

        static float Px(byte[] p, int off, int bw, int bh, int x, int y)
        {
            if (x < 0 || y < 0 || x >= bw || y >= bh) return 0f;
            return p[off + y * bw + x] / 255f;
        }

        const float Inf = 1e20f;

        /// <summary>Squared distance of every pixel to the nearest pixel whose inside flag differs from <paramref name="of"/> (0 where it differs).</summary>
        static float[] Edt(bool[] inside, int w, int h, bool of)
        {
            var g = new float[w * h];
            for (int i = 0; i < g.Length; i++) g[i] = inside[i] == of ? Inf : 0f;
            int n = Math.Max(w, h);
            var f = new float[n]; var dd = new float[n]; var v = new int[n]; var z = new float[n + 1];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++) f[y] = g[y * w + x];
                Edt1(f, h, dd, v, z);
                for (int y = 0; y < h; y++) g[y * w + x] = dd[y];
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) f[x] = g[y * w + x];
                Edt1(f, w, dd, v, z);
                for (int x = 0; x < w; x++) g[y * w + x] = dd[x];
            }

            return g;
        }

        static void Edt1(float[] f, int n, float[] d, int[] v, float[] z)
        {
            int k = 0;
            v[0] = 0; z[0] = -Inf; z[1] = Inf;
            for (int q = 1; q < n; q++)
            {
                float s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2 * q - 2 * v[k]);
                while (s <= z[k])
                {
                    k--;
                    s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2 * q - 2 * v[k]);
                }

                k++;
                v[k] = q; z[k] = s; z[k + 1] = Inf;
            }

            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                float dq = q - v[k];
                d[q] = dq * dq + f[v[k]];
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A bitmap tag (bitm): its images (one per BitmapData entry) and sequences (sprite sheets / animated frames).
    /// Pixel data comes from MAPS\bitmaps.map (retail PC: every entry is flagged external, offset absolute in that file) or, for a
    /// Custom Edition map that keeps it inside, from the map itself. Layout from the tag definitions (DevTools/reference/layouts.txt):
    /// Bitmap: type +0, format +2, usage +4, sequences +84 (64 B), bitmaps +96 (48 B). BitmapData: width +4, height +6, depth +8,
    /// type +10 (0 2d, 1 3d, 2 cube, 3 white), format +12, flags +14 (bit 8 external), registration +16, mips +20 (levels BELOW the base),
    /// offset +24, size +28. Mip chain: base first, each level halves, compressed levels pad up to one 4x4 block (sizes verified on bloodgulch).
    /// Unity-free.
    /// </summary>
    public sealed class HaloBitmap
    {
        public enum Format : ushort
        {
            A8 = 0, Y8 = 1, AY8 = 2, A8Y8 = 3, R5G6B5 = 6, A1R5G5B5 = 8, A4R4G4B4 = 9, X8R8G8B8 = 10, A8R8G8B8 = 11,
            DXT1 = 14, DXT3 = 15, DXT5 = 16, P8Bump = 17,
        }

        public sealed class Image
        {
            public int Width, Height, Depth, Type, Mips, Offset, Size;
            public Format Format;
            public ushort Flags;
            public short RegX, RegY;
            public bool External => (Flags & 0x100) != 0;
            public bool Compressed => Format == Format.DXT1 || Format == Format.DXT3 || Format == Format.DXT5;
            public bool IsCube => Type == 2;
            public int Faces => Type == 2 ? 6 : 1;
            public int Levels => Mips + 1;
        }

        public sealed class Sprite { public int Bitmap; public float Left, Right, Top, Bottom, RegX, RegY; }
        public sealed class Sequence { public string Name; public int First, Count; public readonly List<Sprite> Sprites = new List<Sprite>(); }

        public readonly HaloTag Tag;
        public readonly int BitmapType, Usage;
        public readonly List<Image> Images = new List<Image>();
        public readonly List<Sequence> Sequences = new List<Sequence>();

        public HaloBitmap(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            BitmapType = r.U16(0);
            Usage = r.U16(4);
            foreach (var s in r.Block(84, 64))
            {
                var q = new Sequence { Name = s.Map.Str(s.At, 32), First = s.I16(32), Count = s.U16(34) };
                foreach (var p in s.Block(52, 32))
                    q.Sprites.Add(new Sprite { Bitmap = p.I16(0), Left = p.F32(8), Right = p.F32(12), Top = p.F32(16), Bottom = p.F32(20), RegX = p.F32(24), RegY = p.F32(28) });
                Sequences.Add(q);
            }
            foreach (var d in r.Block(96, 48))
            {
                Images.Add(new Image
                {
                    Width = d.U16(4), Height = d.U16(6), Depth = d.U16(8), Type = d.U16(10), Format = (Format)d.U16(12), Flags = d.U16(14),
                    RegX = d.I16(16), RegY = d.I16(18), Mips = d.U16(20), Offset = d.I32(24), Size = d.I32(28),
                });
            }
        }

        /// <summary>The raw pixel bytes of one image (all faces / slices and mips, as stored).</summary>
        public static byte[] Raw(HaloMap map, Image img, HaloResources bitmaps)
        {
            if (img.Size <= 0) return new byte[0];
            if (img.External)
            {
                if (bitmaps == null) throw new InvalidOperationException("bitmaps.map is needed for " + map.Name);
                return bitmaps.Read(img.Offset, img.Size);
            }
            var b = new byte[img.Size];
            Buffer.BlockCopy(map.Data, img.Offset, b, 0, img.Size);
            return b;
        }

        public static int BitsPerPixel(Format f)
        {
            switch (f)
            {
                case Format.A8: case Format.Y8: case Format.AY8: case Format.P8Bump: return 8;
                case Format.A8Y8: case Format.R5G6B5: case Format.A1R5G5B5: case Format.A4R4G4B4: return 16;
                case Format.X8R8G8B8: case Format.A8R8G8B8: return 32;
                case Format.DXT1: return 4;
                case Format.DXT3: case Format.DXT5: return 8;
                default: return 32;
            }
        }

        /// <summary>Byte size of one level (one face) of a w x h image.</summary>
        public static int LevelSize(Format f, int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            if (f == Format.DXT1 || f == Format.DXT3 || f == Format.DXT5)
                return ((w + 3) / 4) * ((h + 3) / 4) * (f == Format.DXT1 ? 8 : 16);
            return w * h * BitsPerPixel(f) / 8;
        }

        /// <summary>
        /// Decodes level 0 of face/slice 0 (or <paramref name="face"/> of a cube map, faces stored one after another, each with its mips)
        /// to RGBA32 rows, top row first (as stored). <paramref name="level"/> picks a smaller mip.
        /// </summary>
        public static byte[] DecodeRgba(Image img, byte[] raw, int face = 0, int level = 0)
        {
            int w = img.Width, h = img.Height;
            // The cache stores multi-face / multi-slice images LEVEL-major: level 0 of every face (cube: 6, 3d: depth slices),
            // then level 1 of every face, ... (World module, verified on b30's beach cube map).
            int at = 0;
            for (int l = 0; l < level; l++)
            {
                int faces = img.Type == 2 ? 6 : img.Type == 1 ? Math.Max(1, img.Depth >> l) : 1;
                at += LevelSize(img.Format, Math.Max(1, w >> l), Math.Max(1, h >> l)) * faces;
            }
            at += face * LevelSize(img.Format, Math.Max(1, w >> level), Math.Max(1, h >> level));
            w = Math.Max(1, w >> level); h = Math.Max(1, h >> level);
            var o = new byte[w * h * 4];
            switch (img.Format)
            {
                case Format.DXT1: HaloDxt.Decode(raw, at, w, h, o, 1); break;
                case Format.DXT3: HaloDxt.Decode(raw, at, w, h, o, 3); break;
                case Format.DXT5: HaloDxt.Decode(raw, at, w, h, o, 5); break;
                default:
                    for (int i = 0; i < w * h; i++)
                    {
                        byte r = 255, g = 255, b = 255, a = 255;
                        switch (img.Format)
                        {
                            case Format.A8: a = raw[at + i]; break;
                            case Format.Y8: r = g = b = raw[at + i]; break;
                            case Format.AY8: r = g = b = a = raw[at + i]; break;
                            case Format.A8Y8: r = g = b = raw[at + i * 2]; a = raw[at + i * 2 + 1]; break;
                            case Format.P8Bump: r = g = b = raw[at + i]; break;
                            case Format.R5G6B5:
                            {
                                int v = raw[at + i * 2] | raw[at + i * 2 + 1] << 8;
                                r = (byte)((v >> 11 & 31) * 255 / 31); g = (byte)((v >> 5 & 63) * 255 / 63); b = (byte)((v & 31) * 255 / 31); break;
                            }
                            case Format.A1R5G5B5:
                            {
                                int v = raw[at + i * 2] | raw[at + i * 2 + 1] << 8;
                                a = (byte)((v >> 15) != 0 ? 255 : 0); r = (byte)((v >> 10 & 31) * 255 / 31); g = (byte)((v >> 5 & 31) * 255 / 31); b = (byte)((v & 31) * 255 / 31); break;
                            }
                            case Format.A4R4G4B4:
                            {
                                int v = raw[at + i * 2] | raw[at + i * 2 + 1] << 8;
                                a = (byte)((v >> 12 & 15) * 17); r = (byte)((v >> 8 & 15) * 17); g = (byte)((v >> 4 & 15) * 17); b = (byte)((v & 15) * 17); break;
                            }
                            case Format.X8R8G8B8: b = raw[at + i * 4]; g = raw[at + i * 4 + 1]; r = raw[at + i * 4 + 2]; break;
                            case Format.A8R8G8B8: b = raw[at + i * 4]; g = raw[at + i * 4 + 1]; r = raw[at + i * 4 + 2]; a = raw[at + i * 4 + 3]; break;
                        }
                        o[i * 4] = r; o[i * 4 + 1] = g; o[i * 4 + 2] = b; o[i * 4 + 3] = a;
                    }
                    break;
            }
            return o;
        }
    }

    /// <summary>BC1/BC2/BC3 (DXT1/3/5) block decoding to RGBA32.</summary>
    public static class HaloDxt
    {
        public static void Decode(byte[] src, int at, int w, int h, byte[] dst, int kind)
        {
            int bw = (w + 3) / 4, bh = (h + 3) / 4;
            int blockBytes = kind == 1 ? 8 : 16;
            var c = new byte[16];
            var alpha = new byte[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int b = at + (by * bw + bx) * blockBytes;
                    if (b + blockBytes > src.Length) return;
                    int colorAt = b;
                    if (kind == 3)
                    {
                        for (int i = 0; i < 16; i++) { int v = src[b + i / 2] >> (4 * (i & 1)) & 15; alpha[i] = (byte)(v * 17); }
                        colorAt = b + 8;
                    }
                    else if (kind == 5)
                    {
                        int a0 = src[b], a1 = src[b + 1];
                        ulong bits = 0; for (int i = 0; i < 6; i++) bits |= (ulong)src[b + 2 + i] << (8 * i);
                        for (int i = 0; i < 16; i++)
                        {
                            int idx = (int)(bits >> (3 * i) & 7);
                            int v;
                            if (idx == 0) v = a0; else if (idx == 1) v = a1;
                            else if (a0 > a1) v = ((8 - idx) * a0 + (idx - 1) * a1) / 7;
                            else if (idx == 6) v = 0; else if (idx == 7) v = 255;
                            else v = ((6 - idx) * a0 + (idx - 1) * a1) / 5;
                            alpha[i] = (byte)v;
                        }
                        colorAt = b + 8;
                    }
                    int c0 = src[colorAt] | src[colorAt + 1] << 8, c1 = src[colorAt + 2] | src[colorAt + 3] << 8;
                    Rgb(c0, c, 0); Rgb(c1, c, 4);
                    bool fourColor = kind != 1 || c0 > c1;
                    for (int k = 0; k < 3; k++)
                    {
                        if (fourColor) { c[8 + k] = (byte)((2 * c[k] + c[4 + k]) / 3); c[12 + k] = (byte)((c[k] + 2 * c[4 + k]) / 3); }
                        else { c[8 + k] = (byte)((c[k] + c[4 + k]) / 2); c[12 + k] = 0; }
                    }
                    c[3] = c[7] = c[11] = 255; c[15] = (byte)(fourColor ? 255 : 0);
                    uint idxBits = (uint)(src[colorAt + 4] | src[colorAt + 5] << 8 | src[colorAt + 6] << 16 | src[colorAt + 7] << 24);
                    for (int py = 0; py < 4; py++)
                        for (int px = 0; px < 4; px++)
                        {
                            int x = bx * 4 + px, y = by * 4 + py;
                            if (x >= w || y >= h) continue;
                            int i = py * 4 + px;
                            int ci = (int)(idxBits >> (2 * i) & 3) * 4;
                            int o = (y * w + x) * 4;
                            dst[o] = c[ci]; dst[o + 1] = c[ci + 1]; dst[o + 2] = c[ci + 2];
                            dst[o + 3] = kind == 1 ? c[ci + 3] : alpha[i];
                        }
                }
        }

        static void Rgb(int v, byte[] c, int o)
        {
            c[o] = (byte)((v >> 11 & 31) * 255 / 31);
            c[o + 1] = (byte)((v >> 5 & 63) * 255 / 63);
            c[o + 2] = (byte)((v & 31) * 255 / 31);
        }
    }
}

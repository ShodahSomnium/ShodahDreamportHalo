using System;
using System.IO;
using System.IO.Compression;

static class Png
{
    public static void Write(string path, int w, int h, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        Be(ihdr, 0, w); Be(ihdr, 4, h); ihdr[8] = 8; ihdr[9] = 6;
        Chunk(ms, "IHDR", ihdr);
        var raw = new byte[h * (w * 4 + 1)];
        for (int y = 0; y < h; y++) { raw[y * (w * 4 + 1)] = 0; Buffer.BlockCopy(rgba, y * w * 4, raw, y * (w * 4 + 1) + 1, w * 4); }
        using var z = new MemoryStream();
        using (var zl = new ZLibStream(z, CompressionLevel.Fastest, true)) zl.Write(raw);
        Chunk(ms, "IDAT", z.ToArray());
        Chunk(ms, "IEND", new byte[0]);
        File.WriteAllBytes(path, ms.ToArray());
    }
    static void Be(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; Be(len, 0, data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
        uint crc = Crc(t, 0xFFFFFFFF); crc = Crc(data, crc) ^ 0xFFFFFFFF;
        var c = new byte[4]; Be(c, 0, (int)crc); s.Write(c);
    }
    static uint[] _t;
    static uint Crc(byte[] b, uint c)
    {
        if (_t == null) { _t = new uint[256]; for (uint n = 0; n < 256; n++) { uint x = n; for (int k = 0; k < 8; k++) x = (x & 1) != 0 ? 0xEDB88320 ^ (x >> 1) : x >> 1; _t[n] = x; } }
        foreach (var v in b) c = _t[(c ^ v) & 0xFF] ^ (c >> 8);
        return c;
    }
}

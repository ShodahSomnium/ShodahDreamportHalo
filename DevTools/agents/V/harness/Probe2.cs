using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    static int More(string[] a)
    {
        switch (a[0])
        {
            case "bitm":
            {
                var m = new HaloMap(Maps + a[1] + ".map");
                int n = 0, ext = 0, inl = 0;
                foreach (var t in m.OfClass("bitm"))
                {
                    var r = t.Root;
                    foreach (var d in r.Block(96, 48))
                    {
                        ushort flags = d.U16(14);
                        bool external = (flags & 0x100) != 0;
                        if (external) ext++; else inl++;
                        if (n++ < int.Parse(a.Length > 2 ? a[2] : "12"))
                            Console.WriteLine($"{t.Name,-60} {d.U16(4)}x{d.U16(6)}x{d.U16(8)} type {d.U16(10)} fmt {d.U16(12)} flags {flags:X4} mips {d.U16(20)} off {d.U32(24):X} size {d.U32(28):X}");
                    }
                }
                Console.WriteLine($"bitmaps {n}: external {ext}, internal {inl}");
                return 0;
            }
            case "res":
            {
                var b = File.ReadAllBytes(Maps + a[1] + ".map");
                int type = BitConverter.ToInt32(b, 0), paths = BitConverter.ToInt32(b, 4), table = BitConverter.ToInt32(b, 8), count = BitConverter.ToInt32(b, 12);
                Console.WriteLine($"type {type} paths {paths:X} table {table:X} count {count}");
                for (int i = 0; i < Math.Min(count, int.Parse(a.Length > 2 ? a[2] : "10")); i++)
                {
                    int e = table + i * 12;
                    int po = BitConverter.ToInt32(b, e), sz = BitConverter.ToInt32(b, e + 4), off = BitConverter.ToInt32(b, e + 8);
                    int s = paths + po, k = 0; while (b[s + k] != 0) k++;
                    Console.WriteLine($"  {i,5} off {off:X8} size {sz:X6} {System.Text.Encoding.ASCII.GetString(b, s, k)}");
                }
                return 0;
            }
            case "models": return Models(a);
            case "png": return BitmapPng(a);
            case "scen": return Scen(a);
            case "vehi": return V(a);
            case "vcoll": return VColl(a);
            case "vseats": return VSeats(a);
            case "vmarkers": return VMarkers(a);
            case "vanim": return VAnim(a);
            case "vfoot": return VFoot(a);
        }
        Console.WriteLine("unknown " + a[0]); return 1;
    }
}

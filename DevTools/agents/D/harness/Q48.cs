using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Q48(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var t = m.Tags.First(x => x.ClassName == "antr" && x.Name == a[2]);
        var an = t.Root.Element(116, int.Parse(a[3]), 180);
        byte[] d = an.DataBytes(160);
        int nodes = an.U16(44);
        int def = BitConverter.ToInt32(d, 4);
        // uncompressed twin
        var twinTag = m.Tags.First(x => x.ClassName == "antr" && x.Name == a[4]);
        var g = new HaloAnimGraph(twinTag);
        var tw = g.Get(an.Str32(0)); tw.Decode();
        for (int n = 0; n < nodes; n++)
        {
            int o = def + n * 6;
            ushort s0 = BitConverter.ToUInt16(d, o), s1 = BitConverter.ToUInt16(d, o + 2), s2 = BitConverter.ToUInt16(d, o + 4);
            ulong v = (ulong)s0 | (ulong)s1 << 16 | (ulong)s2 << 32;
            int i = n * 4; // twin frame 0 (decoded = conjugated: undo)
            Console.WriteLine($"node {n,2}: raw {s0:X4} {s1:X4} {s2:X4} bits {Convert.ToString((long)v, 2).PadLeft(48, '0')}  twin stored q ({-tw.Rot[i]:0.0000},{-tw.Rot[i+1]:0.0000},{-tw.Rot[i+2]:0.0000},{tw.Rot[i+3]:0.0000})");
        }
        return 0;
    }
}

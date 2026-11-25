using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Brute(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var t = m.Tags.First(x => x.ClassName == "antr" && x.Name == a[2]);
        var an = t.Root.Element(116, int.Parse(a[3]), 180);
        byte[] d = an.DataBytes(160);
        int nodes = an.U16(44);
        int def = BitConverter.ToInt32(d, 4);
        var g = new HaloAnimGraph(m.Tags.First(x => x.ClassName == "antr" && x.Name == a[4]));
        var tw = g.Get(an.Str32(0)); tw.Decode();
        var vals = new ulong[nodes]; var q = new float[nodes, 4];
        for (int n = 0; n < nodes; n++)
        {
            int o = def + n * 6; ulong w0 = BitConverter.ToUInt16(d, o), w1 = BitConverter.ToUInt16(d, o + 2), w2 = BitConverter.ToUInt16(d, o + 4); int pm = int.Parse(a[5]); ulong[] ws = { w0, w1, w2 }; int[][] P6 = { new[]{0,1,2}, new[]{0,2,1}, new[]{1,0,2}, new[]{1,2,0}, new[]{2,0,1}, new[]{2,1,0} }; var pp = P6[pm]; vals[n] = ws[pp[0]] | ws[pp[1]] << 16 | ws[pp[2]] << 32;
            q[n, 0] = -tw.Rot[n * 4]; q[n, 1] = -tw.Rot[n * 4 + 1]; q[n, 2] = -tw.Rot[n * 4 + 2]; q[n, 3] = tw.Rot[n * 4 + 3];
        }
        string[] cn = { "i", "j", "k", "w" };
        for (int len = 10; len <= 16; len++)
        for (int st = 0; st + len <= 48; st++)
        {
            for (int c = 0; c < 4; c++)
            foreach (int sg in new[] { 1, -1 })
            foreach (bool twos in new[] { true, false })
            {
                bool ok = true; double maxe = 0;
                for (int n = 0; n < nodes && ok; n++)
                {
                    long raw = (long)((vals[n] >> st) & ((1UL << len) - 1));
                    double x;
                    if (twos) { if (raw >= 1L << (len - 1)) raw -= 1L << len; x = raw / (double)((1 << (len - 1)) - 1); }
                    else { x = raw / (double)((1 << len) - 1) * 2 - 1; }
                    // quaternion sign ambiguity: compare with the twin as is or negated per node: skip, use abs agreement with consistent overall sign
                    double e = Math.Abs(sg * x - q[n, c]);
                    double e2 = Math.Abs(-sg * x - q[n, c]);
                    maxe = Math.Max(maxe, e);
                    if (e > 0.004) ok = false;
                }
                if (ok) Console.WriteLine($"{cn[c]}: bits {st}..{st + len - 1} len {len} {(twos ? "signed" : "offset")} sign {sg} maxerr {maxe:0.0000}");
            }
        }
        return 0;
    }
}

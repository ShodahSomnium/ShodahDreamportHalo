using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Fmt(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var tag = m.Tags.First(x => x.ClassName == "antr" && x.Name.Contains("cyborg"));
        int idx = 0;
        foreach (var an in tag.Root.Block(116, 180))
        {
            string name = an.Str32(0);
            if (!name.Contains(a[2])) { idx++; continue; }
            int nodes = an.U16(44), frames = an.U16(34);
            ulong rot = an.U32(108) | (ulong)an.U32(112) << 32, tr = an.U32(92) | (ulong)an.U32(96) << 32, sc = an.U32(124) | (ulong)an.U32(128) << 32;
            byte[] def = an.DataBytes(140), fr = an.DataBytes(160);
            Console.WriteLine($"{idx} {name} nodes {nodes} frames {frames} def {def.Length} frame {fr.Length}");
            // default, interleaved per node
            int o = 0;
            for (int n = 0; n < nodes; n++)
            {
                string s = "";
                if ((rot >> n & 1) == 0) { s += Q(def, o, false) + "/" + Q(def, o, true); o += 8; }
                if ((tr >> n & 1) == 0) { s += $" t({BitConverter.ToSingle(def, o):0.###},{BitConverter.ToSingle(def, o + 4):0.###},{BitConverter.ToSingle(def, o + 8):0.###})"; o += 12; }
                if ((sc >> n & 1) == 0) { s += $" s{BitConverter.ToSingle(def, o):0.###}"; o += 4; }
                Console.WriteLine($"  def node {n}: {s}");
            }
            Console.WriteLine($"  default used {o} of {def.Length}");
            for (int f = 0; f < Math.Min(frames, 3); f++)
            {
                o = f * an.U16(36);
                for (int n = 0; n < nodes; n++)
                {
                    string s = "";
                    if ((rot >> n & 1) != 0) { s += Q(fr, o, false); o += 8; }
                    if ((tr >> n & 1) != 0) { s += $" t({BitConverter.ToSingle(fr, o):0.###},{BitConverter.ToSingle(fr, o + 4):0.###},{BitConverter.ToSingle(fr, o + 8):0.###})"; o += 12; }
                    if ((sc >> n & 1) != 0) { s += $" s{BitConverter.ToSingle(fr, o):0.###}"; o += 4; }
                    if (s != "") Console.WriteLine($"  f{f} node {n}: {s}");
                }
            }
            idx++;
        }
        return 0;
    }
    static string Q(byte[] b, int o, bool be)
    {
        float[] v = new float[4];
        for (int i = 0; i < 4; i++) { int k = o + i * 2; short s = be ? (short)(b[k] << 8 | b[k + 1]) : (short)(b[k] | b[k + 1] << 8); v[i] = s / 32767f; }
        float mag = (float)Math.Sqrt(v.Sum(x => x * x));
        return $"q({v[0]:0.###},{v[1]:0.###},{v[2]:0.###},{v[3]:0.###})|{mag:0.###}|";
    }
}

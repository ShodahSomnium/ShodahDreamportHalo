using System;
using SomniumSpace.Worlds.SomniumHalo;
static class Cont
{
    // cont <map> : ADPCM block continuity (next block header predictor vs. last decoded sample of the block before)
    public static int Run(string[] a)
    {
        var m = new HaloMap(MainA.Maps + a[1] + ".map");
        var res = HaloResources.Get(MainA.Maps + "sounds.map");
        long blocks = 0, exact = 0, near = 0; double sumd = 0, sumIndexD = 0;
        foreach (var t in m.OfClass("snd!"))
        {
            var s = HaloSound.Read(t);
            if (s.Format != 1 || (a.Length > 2 && s.Channels != int.Parse(a[2]))) continue;
            foreach (var pr in s.PitchRanges)
                foreach (var p in pr.Permutations)
                {
                    var d = res.Read(p.Offset, p.Size);
                    var f = HaloAdpcm.DecodeAdpcm(d, 0, p.Size, s.Channels);
                    int bl = 36 * s.Channels, nb = p.Size / bl;
                    for (int b = 1; b < nb; b++)
                        for (int c = 0; c < s.Channels; c++)
                        {
                            int h = b * bl + c * 4;
                            int head = (short)(d[h] | d[h + 1] << 8);
                            int last = (int)Math.Round(f[((b * 65) - 1) * s.Channels + c] * 32768f);
                            int diff = Math.Abs(head - last);
                            blocks++; if (diff == 0) exact++; if (diff <= 16) near++; sumd += diff;
                        }
                }
        }
        Console.WriteLine($"{a[1]}: block joins {blocks} exact {exact} (|d|<=16: {near}) mean |d| {sumd / Math.Max(1, blocks):0.0}");
        return 0;
    }
}

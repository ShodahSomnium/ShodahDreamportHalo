using System;
using SomniumSpace.Worlds.SomniumHalo;
static class Cont2
{
    static readonly int[] StepTable = { 7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767 };
    static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
    // mono only; variant: swap nibble order, exact (multiply) vs shift diff
    public static int Run(string[] a)
    {
        var m = new HaloMap(MainA.Maps + a[1] + ".map");
        var res = HaloResources.Get(MainA.Maps + "sounds.map");
        for (int variant = 0; variant < 4; variant++)
        {
            bool swap = (variant & 1) != 0, mul = (variant & 2) != 0;
            long blocks = 0, exact = 0, idxExact = 0; double sumd = 0, step = 0; long steps = 0;
            int nsnd = 0;
            foreach (var t in m.OfClass("snd!"))
            {
                var s = HaloSound.Read(t);
                if (s.Format != 1 || s.Channels != 1) continue;
                if (++nsnd > 60) break;
                foreach (var pr in s.PitchRanges)
                    foreach (var p in pr.Permutations)
                    {
                        var d = res.Read(p.Offset, p.Size);
                        int nb = p.Size / 36;
                        int lastP = 0, lastI = 0;
                        for (int b = 0; b < nb; b++)
                        {
                            int h = b * 36;
                            int pred = (short)(d[h] | d[h + 1] << 8), ix = d[h + 2];
                            if (b > 0) { blocks++; int df = Math.Abs(pred - lastP); if (df == 0) exact++; if (ix == lastI) idxExact++; sumd += df; }
                            int prev = pred;
                            for (int n = 0; n < 64; n++)
                            {
                                int bt = d[h + 4 + (n >> 1)];
                                int code = ((n & 1) == 0) != swap ? bt & 0xF : bt >> 4;
                                int st = StepTable[ix];
                                int diff;
                                if (mul) diff = ((2 * (code & 7) + 1) * st) >> 3;
                                else { diff = st >> 3; if ((code & 1) != 0) diff += st >> 2; if ((code & 2) != 0) diff += st >> 1; if ((code & 4) != 0) diff += st; }
                                if ((code & 8) != 0) pred -= diff; else pred += diff;
                                if (pred > 32767) pred = 32767; else if (pred < -32768) pred = -32768;
                                ix += IndexTable[code]; if (ix < 0) ix = 0; else if (ix > 88) ix = 88;
                                step += Math.Abs(pred - prev); steps++; prev = pred;
                            }
                            lastP = pred; lastI = ix;
                        }
                    }
            }
            Console.WriteLine($"swap {swap} mul {mul}: joins {blocks} exact pred {exact} exact index {idxExact} mean |d| {sumd / Math.Max(1, blocks):0.0} mean step {step / Math.Max(1, steps):0.0}");
        }
        return 0;
    }
}

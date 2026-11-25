using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static class Budget
{
    // budget <map>: what HaloAudio.Preload decodes (every snd! but dialog / music, chains under 20 s) - seconds, floats, MB
    public static int Run(string[] a)
    {
        var m = new HaloMap(MainA.Maps + a[1] + ".map");
        long floats = 0; int clips = 0; double secs = 0; var byClass = new Dictionary<string, double>();
        foreach (var t in m.OfClass("snd!"))
        {
            var s = HaloSound.Read(t);
            int c = s.Class;
            bool skip = c == 19 || c == 44 || c == 46 || c == 47 || c == 32;
            for (int pr = 0; pr < s.PitchRanges.Length; pr++)
                for (int p = 0; p < s.PitchRanges[pr].ActualPermutationCount; p++)
                {
                    int fr = s.ChainFrames(pr, p);
                    if (fr > 20 * s.SampleRate) continue;
                    double sec = (double)fr / s.SampleRate;
                    string k = (skip ? "skip " : "") + HaloSound.ClassGroup(c) + " c" + c;
                    byClass[k] = (byClass.TryGetValue(k, out var v) ? v : 0) + sec;
                    if (skip) continue;
                    clips++; floats += (long)fr * s.Channels; secs += sec;
                }
        }
        Console.WriteLine($"{a[1]}: preload {clips} clips, {secs:0} s, {floats * 4 / 1048576.0:0.0} MB as floats");
        foreach (var kv in byClass.OrderByDescending(k => k.Value)) Console.WriteLine($"   {kv.Key,-24} {kv.Value,7:0.0} s");
        return 0;
    }
}

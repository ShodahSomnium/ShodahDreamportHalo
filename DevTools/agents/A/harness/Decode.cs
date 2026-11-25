using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static class Decode
{
    // decode <map> [filter] [wavdir]
    public static int Run(string[] a)
    {
        var m = new HaloMap(MainA.Maps + a[1] + ".map");
        var res = HaloResources.Get(MainA.Maps + "sounds.map");
        string filter = a.Length > 2 ? a[2] : "*";
        string wav = a.Length > 3 ? a[3] : null;
        int sounds = 0, pieces = 0, fail = 0, silent = 0, nan = 0, oggBufOk = 0, oggBufBad = 0, longOk = 0, longBad = 0;
        double secs = 0; var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var t in m.OfClass("snd!"))
        {
            if (filter != "*" && t.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            sounds++;
            HaloSound s;
            try { s = HaloSound.Read(t); } catch (Exception e) { fail++; Console.WriteLine("READ FAIL " + t.Name + ": " + e.Message); continue; }
            float longest = 0;
            for (int pr = 0; pr < s.PitchRanges.Length; pr++)
            {
                var R = s.PitchRanges[pr];
                for (int pi = 0; pi < R.ActualPermutationCount; pi++)
                {
                    try
                    {
                        // every piece alone (ogg buffer-size check), then the chain
                        foreach (int k in s.Chain(pr, pi))
                        {
                            var P = R.Permutations[k];
                            int ch, rate;
                            var f = HaloPcm.DecodePiece(s, P, res, out ch, out rate);
                            pieces++;
                            if (P.Format == 3) { if (P.BufferSize == f.Length * 2) oggBufOk++; else { oggBufBad++; if (oggBufBad < 8) Console.WriteLine($"  ogg buf {t.Name} {P.Name}: decoded {f.Length * 2} bytes vs buffer {P.BufferSize} (ch {ch} rate {rate})"); } }
                            if (ch != s.Channels || rate != s.SampleRate) Console.WriteLine($"  layout {t.Name} {P.Name}: ogg {ch}ch {rate} vs tag {s.Channels}ch {s.SampleRate}");
                        }
                        var pcm = HaloPcm.Decode(s, pr, pi, res);
                        pcm.Levels(out float peak, out float rms); int clip = 0; foreach (var v in pcm.Samples) if (v >= 0.9999f || v <= -0.9999f) clip++; if (clip > pcm.Samples.Length / 200) Console.WriteLine($"  clipping {pcm.Name} {clip}/{pcm.Samples.Length}");
                        if (float.IsNaN(rms)) nan++;
                        if (peak < 0.001f) { silent++; Console.WriteLine($"  silent {pcm.Name} {pcm.Seconds:0.00}s"); }
                        secs += pcm.Seconds;
                        if (pcm.Seconds * (R.NaturalPitch > 0 ? 1 : 1) > longest) longest = pcm.Seconds;
                        if (filter != "*" || a.Length > 4) Console.WriteLine($"  {pcm.Name} '{R.Permutations[pi].Name}' fmt {s.Format} {pcm.Channels}ch {pcm.Rate}Hz {pcm.Seconds:0.000}s pieces {s.Chain(pr, pi).Length} peak {peak:0.000} rms {rms:0.0000}");
                        if (wav != null && pi < 2)
                        {
                            Directory.CreateDirectory(wav);
                            File.WriteAllBytes(Path.Combine(wav, pcm.Name.Replace((char)92, '_').Replace(' ', '_') + ".wav"), pcm.Wav());
                        }
                    }
                    catch (Exception e) { fail++; Console.WriteLine($"DECODE FAIL {t.Name} pr {pr} perm {pi}: {e.GetType().Name} {e.Message}"); }
                }
            }
            float lms = longest * 1000f;
            float want = s.Format == 1 ? lms * 1.125f : lms; if (Math.Abs(want - s.LongestPermutationMs) <= Math.Max(2f, want * 0.005f)) longOk++; else { longBad++; if (longBad < 10 && a.Length > 4) Console.WriteLine($"  longest {t.Name}: decoded {lms:0} ms vs tag {s.LongestPermutationMs}"); }
        }
        Console.WriteLine($"{a[1]}: sounds {sounds} pieces {pieces} fail {fail} silent {silent} nan {nan} total {secs:0.0}s  ogg buffer match {oggBufOk}/{oggBufOk + oggBufBad}  longest-ms match {longOk}/{longOk + longBad}  time {sw.ElapsedMilliseconds} ms");
        return fail > 0 ? 2 : 0;
    }
}

using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static class Info
{
    // info <map> <filter> : sound tag parameters
    public static int Run(string[] a)
    {
        var m = new HaloMap(MainA.Maps + a[1] + ".map");
        foreach (var t in m.OfClass("snd!"))
        {
            if (t.Name.IndexOf(a[2], StringComparison.OrdinalIgnoreCase) < 0) continue;
            var s = HaloSound.Read(t);
            Console.WriteLine($"{t.Name} class {s.Class} {HaloSound.ClassGroup(s.Class)} flags {s.Flags} {s.Channels}ch {s.SampleRate} fmt {s.Format} dist {s.MinDistance:0.##}-{s.MaxDistance:0.##} skip {s.SkipFraction:0.##} rpitch {s.RandomPitchMin:0.###}..{s.RandomPitchMax:0.###} rgain {s.RandomGain:0.##} cone {s.InnerCone:0.##}/{s.OuterCone:0.##}/{s.OuterConeGain:0.##} bend/s {s.MaxBendPerSecond:0.##} zero s/g/p {s.ZeroSkip:0.##}/{s.ZeroGain:0.##}/{s.ZeroPitch:0.##} one {s.OneSkip:0.##}/{s.OneGain:0.##}/{s.OnePitch:0.##} promo {s.Promotion} x{s.PromotionCount} prs {s.PitchRanges.Length} perms {string.Join(",", s.PitchRanges.Select(p => p.ActualPermutationCount + "/" + p.Permutations.Length))}");
        }
        foreach (var t in m.OfClass("lsnd"))
        {
            if (t.Name.IndexOf(a[2], StringComparison.OrdinalIgnoreCase) < 0) continue;
            var r = t.Root;
            Console.WriteLine($"LSND {t.Name} flags {r.U32(0)} zero period {r.F32(4)} one period {r.F32(16)} maxdist {r.F32(32)} cdmg {r.Ref(44)} tracks {r.Count(60)} details {r.Count(72)}");
            foreach (var k in r.Block(60, 160)) Console.WriteLine($"   track flags {k.U32(0)} gain {k.F32(4)} fade {k.F32(8)}/{k.F32(12)} start {k.Ref(48)} loop {k.Ref(64)} end {k.Ref(80)} alt {k.Ref(128)} altend {k.Ref(144)}");
            foreach (var k in r.Block(72, 104)) Console.WriteLine($"   detail {k.Ref(0)} period {k.F32(16)}..{k.F32(20)} gain {k.F32(24)} flags {k.U32(28)} yaw {k.F32(80):0.##}..{k.F32(84):0.##} pitch {k.F32(88):0.##}..{k.F32(92):0.##} dist {k.F32(96)}..{k.F32(100)}");
        }
        return 0;
    }
}

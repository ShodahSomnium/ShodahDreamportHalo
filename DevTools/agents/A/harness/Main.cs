using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static class MainA
{
    public const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static int Main(string[] a)
    {
        if (a[0] == "hex") return Hex.Run(a);
        if (a[0] == "decode") return Decode.Run(a);
        if (a[0] == "cont") return Cont.Run(a);
        if (a[0] == "cont2") return Cont2.Run(a);
        if (a[0] == "info") return Info.Run(a);
        if (a[0] == "mp") return Mp.Run(a);
        if (a[0] == "budget") return Budget.Run(a);
        if (a[0] == "ambient") return Ambient.Run(a);
        if (a[0] == "foot") return Foot.Run(a);
        if (a[0] == "stride") return Stride.Run(a);
        if (a[0] == "scan") return Scan.Run(a);
        switch (a[0])
        {
            case "raw":
            {
                var m = new HaloMap(Maps + a[1] + ".map");
                int n = 0;
                foreach (var t in m.OfClass("snd!"))
                {
                    if (a.Length > 2 && t.Name.IndexOf(a[2], StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var r = t.Root;
                    Console.WriteLine($"{t.Name} flags {r.U32(0):X} class {r.U16(4)} rate {r.U16(6)} dist {r.F32(8):0.##}-{r.F32(12):0.##} ch {r.U16(108)} fmt {r.U16(110)} longest {r.U32(132)} pr {r.Count(152)}");
                    foreach (var pr in r.Block(152, 72))
                    {
                        Console.WriteLine($"   pr '{pr.Str32(0)}' nat {pr.F32(32)} bend {pr.F32(36)}..{pr.F32(40)} actual {pr.U16(44)} perms {pr.Count(60)}");
                        int i = 0;
                        foreach (var p in pr.Block(60, 124))
                        {
                            if (i++ > 6) break;
                            Console.WriteLine($"      p '{p.Str32(0)}' skip {p.F32(32):0.##} gain {p.F32(36):0.##} fmt {p.U16(40)} next {p.I16(42)} buf {p.U32(56)} samples size {p.I32(64)} flags {p.I32(68):X} off {p.I32(72):X} addr {p.U32(76):X} mouth {p.I32(84)} sub {p.I32(104)}");
                        }
                    }
                    if (++n > 5) break;
                }
                return 0;
            }
        }
        return 1;
    }
}

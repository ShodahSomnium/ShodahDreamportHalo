using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static class P
{
    static int Main(string[] a)
    {
        var m = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\" + a[0] + ".map");
        var tag = m.OfClass("antr").First(x => x.Name.EndsWith(a[1]));
        var g = new HaloAnimGraph(tag);
        for (int i = 2; i < a.Length; i++)
        {
            var x = g.Get(a[i]); if (x == null) { Console.WriteLine("none " + a[i]); continue; }
            x.Decode();
            int n = x.NodeCount;
            Console.WriteLine($"{x.Name} {x.Type} fr{x.Frames}");
            for (int f = 0; f < x.Frames; f++)
            {
                Console.Write($"  f{f}:");
                for (int k = 0; k < n; k++) if (x.Rotates(k)) { float w = Math.Abs(x.Rot[(f*n+k)*4+3]); float ang = 2f*(float)Math.Acos(Math.Min(1f,w))*57.3f; Console.Write($" {g.NodeNames[k].Replace("bip01 ","")}={ang:0}"); }
                Console.WriteLine();
            }
        }
        return 0;
    }
}

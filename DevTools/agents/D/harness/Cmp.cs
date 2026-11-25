using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static readonly string[] Camp = { "a10", "a30", "a50", "b30", "b40", "c10", "c20", "c40", "d20", "d40" };
    static int Cmp(string[] a)
    {
        int tot = 0, comp = 0;
        foreach (var mn in Camp)
        {
            var m = new HaloMap(Maps + mn + ".map");
            int c = 0, n = 0;
            foreach (var t in m.OfClass("antr"))
            {
                int i = 0;
                foreach (var an in t.Root.Block(116, 180))
                {
                    n++;
                    if ((an.U16(58) & 1) != 0)
                    {
                        c++;
                        if (a.Length > 1 && (t.Name.Contains(a[1])))
                            Console.WriteLine($"  {mn} {t.Name} #{i} '{an.Str32(0)}' frames {an.U16(34)} nodes {an.U16(44)} fsize {an.U16(36)} finfo {an.U16(38)} rot {an.U32(108):X8}{an.U32(112):X8} tr {an.U32(92):X8} sc {an.U32(124):X8} compOff {an.U32(136)} default {an.DataSize(140)} frame {an.DataSize(160)} ext {an.DataExternal(160)}");
                    }
                    i++;
                }
            }
            Console.WriteLine($"{mn}: animations {n}, compressed {c}");
            tot += n; comp += c;
        }
        Console.WriteLine($"all: {tot} animations, {comp} compressed");
        return 0;
    }
}

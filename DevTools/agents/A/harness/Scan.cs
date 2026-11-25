using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static class Scan
{
    public static int Run(string[] a)
    {
        var res = HaloResources.Get(MainA.Maps + "sounds.map");
        var byOff = new Dictionary<int, int>();
        for (int i = 0; i < res.Count; i++) byOff[res.Offset(i)] = i;
        var files = a.Length > 1 ? a.Skip(1).ToArray() : Directory.GetFiles(MainA.Maps, "*.map").Select(Path.GetFileNameWithoutExtension).Where(n => n != "sounds" && n != "bitmaps").ToArray();
        var fmt = new Dictionary<string, int>();
        foreach (var f in files)
        {
            var m = new HaloMap(MainA.Maps + f + ".map");
            int snd = 0, perms = 0, ext = 0, inl = 0, match = 0, nameOk = 0, sizeOk = 0, chained = 0;
            foreach (var t in m.OfClass("snd!"))
            {
                snd++;
                var r = t.Root;
                string key = $"fmt{r.U16(110)} ch{r.U16(108)} rate{r.U16(6)}";
                fmt[key] = fmt.TryGetValue(key, out var c) ? c + 1 : 1;
                int pri = 0;
                foreach (var pr in r.Block(152, 72))
                {
                    int pi = 0;
                    foreach (var p in pr.Block(60, 124))
                    {
                        perms++;
                        if (p.I16(42) >= 0) chained++;
                        bool e = (p.I32(68) & 1) != 0;
                        if (e) ext++; else inl++;
                        if (e && byOff.TryGetValue(p.I32(72), out int k))
                        {
                            match++;
                            if (res.Size(k) == p.I32(64)) sizeOk++;
                            if (res.Name(k) == t.Name + "__" + pri + "__" + pi) nameOk++;
                        }
                        if (p.U16(40) != r.U16(110)) { string kk = "permfmt-differs"; fmt[kk] = fmt.TryGetValue(kk, out var c2) ? c2 + 1 : 1; }
                        pi++;
                    }
                    pri++;
                }
            }
            Console.WriteLine($"{f,-16} snd {snd} perms {perms} chained {chained} ext {ext} inl {inl} offMatch {match} sizeOk {sizeOk} nameOk {nameOk}");
        }
        foreach (var kv in fmt.OrderBy(k => k.Key)) Console.WriteLine($"  {kv.Key}: {kv.Value}");
        return 0;
    }
}

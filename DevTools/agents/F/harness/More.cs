using System;
using System.IO;
using System.Linq;
using System.Text;
using SomniumSpace.Worlds.SomniumHalo;

static partial class F
{
    static string[] Ustr(HaloTag t)
    {
        var l = new System.Collections.Generic.List<string>();
        foreach (var s in t.Root.Block(0, 20))
        {
            byte[] b = s.DataBytes(0);
            string v = b == null ? "" : Encoding.Unicode.GetString(b).TrimEnd('\0');
            l.Add(v);
        }
        return l.ToArray();
    }
    static int More(string[] a)
    {
        var m = new HaloMap(Maps + "ui.map");
        switch (a[0])
        {
            case "ustr":
                foreach (var t in m.OfClass("ustr").Where(t => t.Name.Contains(a[1])))
                {
                    Console.WriteLine("== " + t.Name);
                    int i = 0; foreach (var s in Ustr(t)) Console.WriteLine($"  [{i++}] {s.Replace("\r", "\r").Replace("\n", "\n")}");
                }
                return 0;
            case "mply":
            {
                var t = m.OfClass("mply").First();
                int i = 0;
                foreach (var e in t.Root.Block(0, 68))
                {
                    var bm = e.Ref(0); var us = e.Ref(16);
                    Console.WriteLine($"[{i++}] bitmap {bm?.Name} name {us?.Name} ({(us != null ? string.Join("|", Ustr(us)) : "")}) path {e.Str32(32)}");
                }
                return 0;
            }
            case "bm":
            {
                var res = HaloResources.Get(Maps + "bitmaps.map");
                Directory.CreateDirectory(a[2]);
                foreach (var t in m.OfClass("bitm").Where(x => x.Name.Contains(a[1])))
                {
                    var b = new HaloBitmap(t);
                    Console.WriteLine($"== {t.Name}: images {b.Images.Count} seqs {b.Sequences.Count} type {b.BitmapType} usage {b.Usage}");
                    foreach (var s in b.Sequences) Console.WriteLine($"   seq '{s.Name}' first {s.First} count {s.Count} sprites {s.Sprites.Count}" + (s.Sprites.Count > 0 ? $" s0 bm{s.Sprites[0].Bitmap} {s.Sprites[0].Left:0.###}-{s.Sprites[0].Right:0.###} x {s.Sprites[0].Top:0.###}-{s.Sprites[0].Bottom:0.###}" : ""));
                    int max = a.Length > 3 ? int.Parse(a[3]) : 30;
                    for (int i = 0; i < b.Images.Count && i < max; i++)
                    {
                        var img = b.Images[i];
                        Console.Write($"   [{i}] {img.Width}x{img.Height} {img.Format} type {img.Type} ext {img.External}");
                        try
                        {
                            var raw = HaloBitmap.Raw(m, img, res);
                            var rgba = HaloBitmap.DecodeRgba(img, raw);
                            // flip? write as is
                            string f = Path.Combine(a[2], Path.GetFileName(t.Name).Replace(' ', '_') + "_" + i + ".png");
                            Png.Write(f, img.Width, img.Height, rgba);
                            Console.WriteLine(" -> " + f);
                        }
                        catch (Exception e) { Console.WriteLine(" FAIL " + e.Message); }
                    }
                }
                return 0;
            }
            case "dela": foreach (var t in m.OfClass("DeLa").Where(x => x.Name.Contains(a[1]))) { Widget(t, 0, a.Length > 2 ? int.Parse(a[2]) : 0, 0, 0); if (a.Length > 2 && int.Parse(a[2]) > 0) break; } return 0;
            case "ring": return Ring(a);
            case "scen": return ScenUi(a);
            case "fonttest": return FontTest(a);
            case "used": return Used(a);
            case "sheet": return Sheet(a);
            case "ringgeo": return RingGeo(a);
            case "font":
            {
                foreach (var t in m.OfClass("font"))
                {
                    var r = t.Root;
                    int chars = r.Count(124);
                    byte[] px = r.DataBytes(136);
                    Console.WriteLine($"== {t.Name}: flags {r.U32(0):X} asc {r.I16(4)} desc {r.I16(6)} lead {r.I16(8)}/{r.I16(10)} chars {chars} pixels {(px == null ? -1 : px.Length)} bold {r.Ref(60)?.Name} italic {r.Ref(76)?.Name} cond {r.Ref(92)?.Name} under {r.Ref(108)?.Name}");
                    int n = 0;
                    foreach (var c in r.Block(124, 20))
                    {
                        if (n++ < 6 || (c.U16(0) >= 'A' && c.U16(0) <= 'C')) Console.WriteLine($"   '{(char)c.U16(0)}' w {c.I16(2)} bm {c.I16(4)}x{c.I16(6)} org {c.I16(8)},{c.I16(10)} hw {c.U16(12)} off {c.I32(16)}");
                    }
                }
                return 0;
            }
        }
        Console.WriteLine("unknown"); return 1;
    }
}

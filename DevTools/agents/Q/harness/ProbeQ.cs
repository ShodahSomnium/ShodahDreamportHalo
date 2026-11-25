using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    const string QMaps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    public static int Q(string[] a)
    {
        string mapName = a.Length > 1 ? a[1] : "a10";
        string what = a.Length > 2 ? a[2] : "all";
        var m = new HaloMap(QMaps + mapName + ".map");
        var r = m.Scenario.Root;
        if (what == "all" || what == "cams")
            foreach (var c in r.Block(1264, 104)) Console.WriteLine($"cam {c.Str32(4),-32} pos {c.Vec3(40)} ori {c.Vec3(52)} fov {c.F32(64) * 57.29578f:0.##}");
        if (what == "all" || what == "flags")
            foreach (var c in r.Block(1252, 92)) Console.WriteLine($"flag {c.Str32(4),-32} pos {c.Vec3(36)} facing {c.F32(48) * 57.29578f:0.#} {c.F32(52) * 57.29578f:0.#}");
        if (what == "all" || what == "titles")
        {
            Console.WriteLine("custom object names " + r.Ref(1396) + " help " + r.Ref(1412) + " hud " + r.Ref(1428));
            foreach (var t in m.OfClass("ustr")) Console.WriteLine("ustr " + t.Name);
            foreach (var c in r.Block(1276, 96))
                Console.WriteLine($"title {c.Str32(4),-24} bounds {c.I16(40)},{c.I16(42)},{c.I16(44)},{c.I16(46)} str {c.I16(48)} style {c.I16(50)} just {c.I16(52)} flags {c.U32(56):X} col {c.U32(60):X8} shadow {c.U32(64):X8} in {c.F32(68)} up {c.F32(72)} out {c.F32(76)}");
        }
        if (what == "all" || what == "rec")
            foreach (var c in r.Block(876, 64))
            {
                byte[] b = c.DataBytes(44);
                Console.WriteLine($"rec {c.Str32(0),-32} v{c.I8(32)} raw{c.I8(33)} ucv{c.I8(34)} len {c.I16(36)} stream {b.Length} B  " + string.Join(" ", b.Take(48).Select(x => x.ToString("X2"))));
            }
        if (what == "hex")
        {
            string name = a[3];
            foreach (var c in r.Block(876, 64)) if (c.Str32(0) == name)
            {
                byte[] b = c.DataBytes(44);
                Console.WriteLine($"rec {name} len {c.I16(36)} bytes {b.Length}");
                for (int i = 0; i < b.Length; i += 32) Console.WriteLine($"{i,6}: " + string.Join(" ", b.Skip(i).Take(32).Select(x => x.ToString("X2"))));
                if (a.Length > 4) File.WriteAllBytes(a[4], b);
            }
        }
        return 0;
    }
}
static partial class Probe
{
    public static int QStrings(string[] a)
    {
        var m = new HaloMap(QMaps + a[1] + ".map");
        byte[] s = m.Scenario.Root.DataBytes(1160);
        int st = 0; var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++) if (s[i] == 0) { if (i > st) sb.Append(System.Text.Encoding.ASCII.GetString(s, st, i - st)).Append(' '); st = i + 1; }
        Console.WriteLine(sb.ToString());
        return 0;
    }
}
static partial class Probe
{
    public static int QUstr(string[] a)
    {
        var m = new HaloMap(QMaps + a[1] + ".map");
        foreach (var t in m.OfClass("ustr")) if (t.Name.Contains(a[2])) { int i = 0; foreach (var s in HaloStrings.Read(t)) Console.WriteLine($"{t.Name} [{i++}] {s.Replace("\r", "\r").Replace("\n", "\n")}"); }
        return 0;
    }
}
static partial class Probe
{
    public static int QFind(string[] a)
    {
        var m = new HaloMap(QMaps + a[1] + ".map");
        var needle = System.Text.Encoding.Unicode.GetBytes(a[2]);
        var d = m.Data;
        for (int i = 0; i + needle.Length < d.Length; i++)
        {
            int k = 0; while (k < needle.Length && d[i + k] == needle[k]) k++;
            if (k == needle.Length) { Console.WriteLine($"utf16 at {i:X}"); }
        }
        var t = m.Scenario.Root.Ref(1428);
        if (t != null) { var hm = HudMessages.Read(t); foreach (var n in hm.Names) { var sb = new System.Text.StringBuilder(); foreach (var p in hm.ByName[n]) sb.Append(p.Icon ? "[icon]" : p.Text); Console.WriteLine($"hmt {n}: {sb}"); } }
        return 0;
    }
}
static partial class Probe
{
    public static int QWho(string[] a)
    {
        var m = new HaloMap(QMaps + a[1] + ".map");
        int off = Convert.ToInt32(a[2], 16);
        foreach (var t in m.Tags)
        {
            if (t.Address == 0) continue;
            int s = t.Root.At;
            if (s <= off && off < s + 0x4000) Console.WriteLine($"near tag {t} start {s:X} (+{off - s:X})");
        }
        // also: who has a data ref pointing near
        foreach (var t in m.OfClass("ustr")) foreach (var s in t.Root.Block(0, 20)) { int o = s.DataOffset(0); if (Math.Abs(o - off) < 0x200) Console.WriteLine($"ustr {t.Name} string data at {o:X}"); }
        return 0;
    }
}
static partial class Probe
{
    public static int QText(string[] a)
    {
        var m = new HaloMap(QMaps + a[1] + ".map");
        int off = Convert.ToInt32(a[2], 16), len = int.Parse(a[3]);
        var s = System.Text.Encoding.Unicode.GetString(m.Data, off, len);
        Console.WriteLine(s.Replace("\0", "|").Replace("\r", "\r").Replace("\n", "\n"));
        return 0;
    }
}

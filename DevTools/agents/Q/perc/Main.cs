using System;
using System.Collections.Generic;
using System.Reflection;
using SomniumSpace.Worlds.SomniumHalo;

// I-area perception/combat probe: dumps every parsed actr/actv field for actv tags matching a filter.
static class PercProbe
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static string F(object o)
    {
        if (o == null) return "-";
        if (o is HaloTag t) { int i = t.Name.LastIndexOf((char)92); return i >= 0 ? t.Name.Substring(i + 1) : t.Name; }
        if (o is float f) return f.ToString("0.###");
        if (o is uint u) return "0x" + u.ToString("X8");
        return o.ToString();
    }
    static void Dump(object obj)
    {
        var l = new List<string>();
        foreach (var fi in obj.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (fi.Name == "Tag" || fi.Name == "Actor") continue;
            l.Add(fi.Name + "=" + F(fi.GetValue(obj)));
        }
        Console.WriteLine("    " + string.Join(" ", l));
    }
    static int Main(string[] a)
    {
        HaloLog.Sink = Console.WriteLine;
        var m = new HaloMap(Maps + a[0] + ".map");
        string filt = a.Length > 1 ? a[1] : "";
        var seen = new HashSet<HaloTag>();
        foreach (var t in m.OfClass("actv"))
        {
            if (t.Name.IndexOf(filt, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var v = new HaloActorVariantDef(t);
            Console.WriteLine("actv " + t.Name);
            Dump(v);
            if (v.ActorTag != null && seen.Add(v.ActorTag)) { Console.WriteLine("  actr " + v.ActorTag.Name); Dump(new HaloActorDef(v.ActorTag)); }
        }
        return 0;
    }
}

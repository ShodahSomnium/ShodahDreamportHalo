using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class P
{
    static int Weaps(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("weap")) { var r = t.Root; var md = r.Ref(40); int markers = 0; string mk = ""; if (md != null) { var hm = new HaloModel(md); markers = hm.Markers.Count; mk = string.Join(",", hm.Markers.Select(x => x.Name)); } Console.WriteLine($"{t.Name} label '{r.Str32(780)}' model {md?.Name} markers {mk}"); }
        return 0;
    }
}

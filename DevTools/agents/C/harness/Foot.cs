using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class P
{
    static int Foot(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("foot")) { var r = t.Root; Console.Write($"{t.Name}: kinds {r.Count(0)}:"); int k = 0; foreach (var e in r.Block(0, 28)) { int n = 0; foreach (var mm in e.Block(0, 48)) if (mm.Ref(16) != null || mm.Ref(0) != null) n++; Console.Write($" [{k++}]{n}"); } Console.WriteLine(); }
        return 0;
    }
}

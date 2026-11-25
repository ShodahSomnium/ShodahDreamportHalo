using System;
using System.Linq;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int BitmFormats(string[] a)
    {
        var cnt = new Dictionary<string, int>();
        foreach (var name in (a.Length > 1 ? new[] { a[1] } : Mp))
        {
            var m = new HaloMap(Maps + name + ".map");
            foreach (var t in m.OfClass("bitm"))
            {
                var b = new HaloBitmap(t);
                foreach (var img in b.Images) { string k = "type " + b.BitmapType + " usage " + b.Usage + " fmt " + img.Format + " imgtype " + img.Type; cnt[k] = cnt.GetValueOrDefault(k) + 1; }
            }
        }
        foreach (var kv in cnt.OrderBy(k => k.Key)) Console.WriteLine(kv.Key + ": " + kv.Value);
        return 0;
    }
}

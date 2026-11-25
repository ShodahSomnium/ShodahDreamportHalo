using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static float Cut(HaloMap m, HaloTag t, HaloResources res)
    {
        if (t == null) return -1;
        var img = new HaloBitmap(t).Images[0];
        var px = HaloBitmap.DecodeRgba(img, HaloBitmap.Raw(m, img, res));
        int c = 0; for (int i = 3; i < px.Length; i += 4) if (px[i] < 128) c++;
        return c / (float)(px.Length / 4);
    }
    static int Alpha(string[] a)
    {
        var res = HaloResources.Get(Maps + "bitmaps.map");
        foreach (var name in new[] { "a10","a30","a50","b30","b40","c10","c20","c40","d20","d40","timberland","bloodgulch","deathisland","dangercanyon","icefields" })
        {
            var m = new HaloMap(Maps + name + ".map");
            foreach (var t in m.OfClass("senv"))
            {
                var e = HaloShaderDef.Of(t).Environment;
                if (!e.AlphaTested) continue;
                Console.WriteLine($"{name} {t.Name}: base cut {Cut(m, e.BaseMap, res):0.00} bump {(e.BumpMap != null ? Cut(m, e.BumpMap, res).ToString("0.00") : "-")}");
            }
        }
        return 0;
    }
}

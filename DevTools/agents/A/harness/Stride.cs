using System;
using SomniumSpace.Worlds.SomniumHalo;
static class Stride
{
    // stride <map> [filter]: cyborg antr move animations: frames, foot frames, root distance (frame info) -> stride
    public static int Run(string[] a)
    {
        var m = new HaloMap(MainA.Maps + a[1] + ".map");
        string f = a.Length > 2 ? a[2] : "move-front";
        foreach (var t in m.OfClass("antr"))
        {
            if (t.Name.IndexOf("cyborg", StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine(t.Name);
            int i = 0;
            foreach (var an in t.Root.Block(116, 180))
            {
                string name = an.Str32(0);
                if (name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int frames = an.U16(34), fit = an.U16(38);
                    var info = an.DataBytes(72);
                    int stride = fit == 1 ? 8 : fit == 2 ? 12 : fit == 3 ? 16 : 0;
                    double dist = 0;
                    for (int k = 0; stride > 0 && k + stride <= info.Length; k += stride)
                    {
                        float dx = BitConverter.ToSingle(info, k), dy = BitConverter.ToSingle(info, k + 4);
                        dist += Math.Sqrt(dx * dx + dy * dy);
                    }
                    Console.WriteLine($"  {i,4} {name,-40} frames {frames} info {fit} ({info.Length} B) lfoot {an.I8(64)} rfoot {an.I8(65)} sound {an.I16(60)}@{an.I16(62)} dist {dist:0.###} wu = {dist * 3.048:0.##} m, speed {(frames > 0 ? dist * 30 / frames * 3.048 : 0):0.##} m/s");
                }
                i++;
            }
        }
        return 0;
    }
}

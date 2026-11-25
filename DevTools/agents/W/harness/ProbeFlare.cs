using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Flares(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        var b = new HaloBsp(sc, int.Parse(a[2]));
        float x = float.Parse(a[3]), y = float.Parse(a[4]), z = float.Parse(a[5]);
        foreach (var mk in b.LensFlareMarkers.OrderBy(k => Math.Pow(k.Position.X - x, 2) + Math.Pow(k.Position.Y - y, 2) + Math.Pow(k.Position.Z - z, 2)).Take(6))
            Console.WriteLine($"{mk.Position} dir ({mk.Dx:0.00},{mk.Dy:0.00},{mk.Dz:0.00}) flare {b.LensFlares[mk.Flare]?.Name}");
        return 0;
    }
}

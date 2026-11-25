using System;
using System.IO;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    public static int Ladders(string[] a)
    {
        var inst = HaloInstall.Open(@"C:\Program Files (x86)\Microsoft Games\Halo");
        foreach (var e in inst.Multiplayer)
        {
            var m = new HaloMap(e.File);
            var sc = new HaloScenario(m);
            int climb = 0, total = 0;
            for (int i = 0; i < sc.Bsps.Count; i++)
            {
                var b = new HaloBsp(sc, i);
                if (b.Collision == null) continue;
                foreach (var s in b.Collision.Surfaces) { total++; if ((s.Flags & HaloCollisionBsp.Climbable) != 0) climb++; }
            }
            Console.WriteLine($"{e.Label,-20} climbable surfaces {climb} of {total}");
        }
        return 0;
    }
}

using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Break(string[] a)
    {
        foreach (var name in new[] { "a30", "b40", "c40", "a10", "d40", "boardingaction" })
        {
            var m = new HaloMap(Maps + name + ".map");
            var sc = new HaloScenario(m);
            for (int i = 0; i < sc.Bsps.Count; i++)
            {
                var b = new HaloBsp(sc, i);
                if (b.BreakableSurfaces.Count == 0) continue;
                int secs = b.Sections.Count(s => s.Breakable >= 0);
                int cs = b.Collision.Surfaces.Count(s => (s.Flags & HaloCollisionBsp.Breakable) != 0);
                var idx = b.Collision.Surfaces.Where(s => (s.Flags & HaloCollisionBsp.Breakable) != 0).Select(s => s.Breakable).Distinct().Count();
                Console.WriteLine($"{name} bsp{i}: breakable {b.BreakableSurfaces.Count}, render sections {secs} ({string.Join(",", b.Sections.Where(s => s.Breakable >= 0).Select(s => s.Shader?.Name).Distinct())}), collision surfaces {cs} using {idx} indices; mats {string.Join(",", b.Collision.Surfaces.Where(s => (s.Flags & 8) != 0).Select(s => b.MaterialType(s)).Distinct())}");
            }
        }
        return 0;
    }
}

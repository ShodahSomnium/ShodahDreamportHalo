using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class F
{
    public static int ScenUi(string[] a)
    {
        var m = new HaloMap(Maps + "ui.map");
        var s = new HaloScenario(m);
        Console.WriteLine($"bsps {s.Bsps.Count} skies {string.Join(",", s.Skies.Select(x => x?.Name))} scenery {s.Scenery.Count}");
        foreach (var p in s.Scenery) Console.WriteLine($"   scenery {p} rot {p.Rotation} perm {p.DesiredPermutation} notplaced {p.NotPlaced}");
        var r = m.Scenario.Root;
        foreach (var c in r.Block(1264, 104)) Console.WriteLine($"   camera '{c.Str32(4)}' pos {c.Vec3(40)} ypr ({c.F32(52):0.###},{c.F32(56):0.###},{c.F32(60):0.###}) fov {c.F32(64)}");
        foreach (var sk in s.Skies)
        {
            if (sk == null) continue;
            var k = sk.Root;
            Console.WriteLine($"sky {sk.Name} model {k.Ref(0)?.Name}");
            for (int i = 0; i < 64; i += 4) Console.Write($"{k.F32(i + 32):0.###} ");
            Console.WriteLine();
        }
        return 0;
    }
}

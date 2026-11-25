using System;
using System.Linq;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Dev(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        var names = new Dictionary<string, int>();
        int ok = 0, comp = 0, none = 0;
        foreach (var g in sc.Machines.Concat(sc.Controls).Where(p => p.Type != null).GroupBy(p => p.Type.Id))
        {
            var def = new HaloObjectDef(g.First().Type);
            if (def.AnimationGraph == null) { none++; continue; }
            var ag = new HaloAnimGraph(def.AnimationGraph);
            var list = string.Join(", ", ag.Animations.Select(x => x.Name + (x.Compressed ? "(c)" : "") + "/" + x.Frames));
            Console.WriteLine($"{g.First().Type.Name} x{g.Count()}: {list}");
            foreach (var x in ag.Animations) { if (x.Compressed) comp++; else ok++; }
        }
        Console.WriteLine($"anims ok {ok} compressed {comp}; devices without graph {none}");
        var b = new HaloBsp(sc, 0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var pd = new HaloPathData(b);
        int withN = pd.Surfaces.Count(s => s.Neighbours.Any(n => n >= 0));
        Console.WriteLine($"path data {pd.Surfaces.Count} surfaces, walkable {pd.WalkableCount}, with neighbours {withN}, {sw.ElapsedMilliseconds} ms; bytes " + string.Join(",", b.PathfindingSurfaces.Take(20)));
        var st = sc.StartLocations[0];
        Console.WriteLine($"surface under start: {pd.SurfaceUnder(st.Position.X, st.Position.Y, st.Position.Z + 0.1f)}");
        return 0;
    }
}

using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Leaves(string[] a)
    {
        foreach (var name in (a.Length > 1 ? new[] { a[1] } : Mp))
        {
            var m = new HaloMap(Maps + name + ".map");
            var sc = new HaloScenario(m);
            var b = new HaloBsp(sc, 0);
            int inside = 0, outside = 0, unknown = 0; var cl = new System.Collections.Generic.Dictionary<int,int>();
            foreach (var s in sc.StartLocations)
            {
                int c = b.ClusterAt(s.Position.X, s.Position.Y, s.Position.Z + 0.1f);
                if (c >= 0) { inside++; cl[c] = 1; } else if (c == -1) outside++; else unknown++;
            }
            float cx = (b.BoundsMinX + b.BoundsMaxX) / 2, cy = (b.BoundsMinY + b.BoundsMaxY) / 2;
            int below = b.ClusterAt(cx, cy, b.BoundsMinZ - 5), above = b.ClusterAt(cx, cy, b.BoundsMaxZ + 50), far = b.ClusterAt(b.BoundsMaxX + 100, cy, 0);
            int skyc = 0; foreach (var k in cl.Keys) if (b.Clusters[k].Sky >= 0) skyc++;
            Console.WriteLine($"{name}: starts {sc.StartLocations.Count} inside {inside} outside {outside} unknown {unknown} clusters {cl.Count} (with sky {skyc}) | below {below} above {above} far {far} | nodes {b.Collision.Nodes.Length/3} leaves {b.LeafClusters.Length}");
        }
        return 0;
    }
}

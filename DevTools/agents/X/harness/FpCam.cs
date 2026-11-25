using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

// first-person animation: where the FP gun's nodes / muzzle marker sit relative to the camera (the graph's root) in an animation frame
static class FpCam
{
    public static void Run(HaloMap m, string weapon, string animName)
    {
        var w = m.OfClass("weap").First(t => t.Name.EndsWith(weapon));
        var antr = w.Root.Ref(1132); var fpm = w.Root.Ref(1116);
        Console.WriteLine($"{w.Name}: fp model {fpm} fp anims {antr}");
        var g = new HaloAnimGraph(antr);
        for (int i = 0; i < g.NodeNames.Length; i++) Console.Write($"{i}:{g.NodeNames[i]}<{g.NodeParents[i]} ");
        Console.WriteLine();
        Console.WriteLine(string.Join(", ", g.Animations.Select(a => a.Name)));
        var an = g.Animations.First(a => a.Name.Contains(animName));
        if (!an.Decode()) { Console.WriteLine("decode " + an.Error); return; }
        int n = an.NodeCount;
        var mr = new float[n * 4]; var mt = new float[n * 3];
        var q = new float[4];
        for (int k = 0; k < n; k++)
        {
            int i = k; // frame 0
            HQ.Std(an.Rot[i * 4], an.Rot[i * 4 + 1], an.Rot[i * 4 + 2], an.Rot[i * 4 + 3], q, 0);
            float tx = an.Trans[i * 3], ty = an.Trans[i * 3 + 1], tz = an.Trans[i * 3 + 2];
            int p = g.NodeParents[k];
            if (p < 0) { Array.Copy(q, 0, mr, k * 4, 4); mt[k * 3] = tx; mt[k * 3 + 1] = ty; mt[k * 3 + 2] = tz; }
            else
            {
                HQ.Mul(mr, p * 4, q, 0, mr, k * 4);
                float x, y, z; HQ.Rotate(mr, p * 4, tx, ty, tz, out x, out y, out z);
                mt[k * 3] = mt[p * 3] + x; mt[k * 3 + 1] = mt[p * 3 + 1] + y; mt[k * 3 + 2] = mt[p * 3 + 2] + z;
            }
            float fx, fy, fz; HQ.Rotate(mr, k * 4, 1, 0, 0, out fx, out fy, out fz);
            Console.WriteLine($"  {g.NodeNames[k],-24} pos ({mt[k*3]:0.###},{mt[k*3+1]:0.###},{mt[k*3+2]:0.###}) fwd ({fx:0.##},{fy:0.##},{fz:0.##})");
        }
        int gun = Array.FindIndex(g.NodeNames, s => s.Trim().EndsWith("gun"));
        if (gun >= 0)
        {
            foreach (var mk in new[] { (0.106f, 0f, 0.021f), (0.106f, 0f, -0.019f) })
            {
                float x, y, z; HQ.Rotate(mr, gun * 4, mk.Item1, mk.Item2, mk.Item3, out x, out y, out z);
                x += mt[gun * 3]; y += mt[gun * 3 + 1]; z += mt[gun * 3 + 2];
                Console.WriteLine($"  primary trigger (fp) at ({x:0.###},{y:0.###},{z:0.###}) dist {Math.Sqrt(x*x+y*y+z*z):0.###} wu");
                float ux, uy, uz; HQ.Rotate(mr, gun * 4, -0.5f, 0, 0.87f, out ux, out uy, out uz);
                Console.WriteLine($"    upper petal direction in camera frame ({ux:0.##},{uy:0.##},{uz:0.##})");
            }
        }
    }
}

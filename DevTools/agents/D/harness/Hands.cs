using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Hands(string[] a)
    {
        Load(a[1]);
        var pose = new HaloPose(_sk);
        pose.Apply(_g.Get(a[2]), _map, 0, true, 1f); pose.ToModel();
        HaloSkeleton.Marker mk; _sk.FindMarker("right hand", out mk);
        pose.MarkerFrame(mk, out float px, out float py, out float pz, out float fx, out float fy, out float fz, out float ux, out float uy, out float uz);
        // gun frame: F, L = U x F, U
        float lx = uy * fz - uz * fy, ly = uz * fx - ux * fz, lz = ux * fy - uy * fx;
        foreach (var hn in new[] { "bip01 r hand", "bip01 l hand" })
        {
            int h = _sk.Node(hn);
            foreach (var ax in new[] { (1f, 0f, 0f, "X"), (0f, 1f, 0f, "Y"), (0f, 0f, 1f, "Z") })
            {
                pose.Axis(h, ax.Item1, ax.Item2, ax.Item3, out float x, out float y, out float z);
                Console.WriteLine($"{hn} {ax.Item4} in gun frame: fwd {x * fx + y * fy + z * fz:0.00} left {x * lx + y * ly + z * lz:0.00} up {x * ux + y * uy + z * uz:0.00}");
            }
            Console.WriteLine($"   wrist in gun frame: fwd {(pose.MTrans[h*3]-px)*fx+(pose.MTrans[h*3+1]-py)*fy+(pose.MTrans[h*3+2]-pz)*fz:0.000} left {(pose.MTrans[h*3]-px)*lx+(pose.MTrans[h*3+1]-py)*ly+(pose.MTrans[h*3+2]-pz)*lz:0.000} up {(pose.MTrans[h*3]-px)*ux+(pose.MTrans[h*3+1]-py)*uy+(pose.MTrans[h*3+2]-pz)*uz:0.000}");
        }
        return 0;
    }
}

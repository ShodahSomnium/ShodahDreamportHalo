using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Grid(string[] a)
    {
        Load(a[1]);
        var basea = _g.Get(a[2]); var ov = _g.Get(a[3]);
        HaloSkeleton.Marker mk; _sk.FindMarker("right hand", out mk);
        var pose = new HaloPose(_sk);
        for (int f = 0; f < ov.Frames; f++)
        {
            pose.Reset(); pose.Apply(basea, _map, 0, true, 1f); pose.Overlay(ov, _map, f, f, 0f, 1f); pose.ToModel();
            pose.MarkerFrame(mk, out float px, out float py, out float pz, out float fx, out float fy, out float fz, out float ux, out float uy, out float uz);
            ov.Decode();
            float pel = 2f * (float)Math.Acos(Math.Min(1f, Math.Abs(ov.Rot[(f * ov.NodeCount) * 4 + 3]))) * 57.3f;
            Console.WriteLine($"frame {f}: gun yaw {Math.Atan2(fy, fx) * 180 / Math.PI:0.0} pitch {Math.Asin(fz) * 180 / Math.PI:0.0}  pelvis delta {pel:0.0} deg");
        }
        return 0;
    }
}

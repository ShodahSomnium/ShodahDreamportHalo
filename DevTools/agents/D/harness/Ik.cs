using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    // ik <map> <anim> <weapon tag>: the animation's left hand marker vs the weapon's "left hand cyborg" marker, in the gun frame (units)
    static int Ik(string[] a)
    {
        Load(a[1]);
        var pose = new HaloPose(_sk);
        var an = _g.Get(a[2]);
        float t = a.Length > 4 ? float.Parse(a[4]) : 0f;
        pose.Apply(an, _map, t, false, 1f); pose.ToModel();
        HaloSkeleton.Marker rm, lm; _sk.FindMarker("right hand", out rm); _sk.FindMarker("left hand", out lm);
        pose.MarkerFrame(rm, out float px, out float py, out float pz, out float fx, out float fy, out float fz, out float ux, out float uy, out float uz);
        float lx = uy * fz - uz * fy, ly = uz * fx - ux * fz, lz = ux * fy - uy * fx;
        pose.MarkerFrame(lm, out float qx, out float qy, out float qz, out _, out _, out _, out _, out _, out _);
        float dx = qx - px, dy = qy - py, dz = qz - pz;
        Console.WriteLine($"anim left hand marker in gun frame: fwd {dx*fx+dy*fy+dz*fz:0.000} left {dx*lx+dy*ly+dz*lz:0.000} up {dx*ux+dy*uy+dz*uz:0.000}");
        var w = _m.Find("weap", a[3]);
        var hm = new HaloModel(w.Root.Ref(40));
        var mk = hm.FindMarker("left hand cyborg");
        var x = mk.Instances[0];
        Console.WriteLine($"weapon marker 'left hand cyborg' (node {x.Node}): fwd {x.Translation.X:0.000} left {x.Translation.Y:0.000} up {x.Translation.Z:0.000}");
        return 0;
    }
}

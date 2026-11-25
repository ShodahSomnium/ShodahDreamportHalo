using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int ModelWinding(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        int shown = 0;
        foreach (var t in m.OfClass("mod2"))
        {
            var mo = new HaloModel(t);
            int pa = 0, pd = 0;
            foreach (var g in mo.Geometries) foreach (var part in g.Parts)
            {
                var d = mo.Read(part);
                for (int i = 0; i + 2 < d.Triangles.Length; i += 3)
                {
                    var A = d.Position[d.Triangles[i]]; var B = d.Position[d.Triangles[i + 1]]; var C = d.Position[d.Triangles[i + 2]];
                    var N = d.Normal[d.Triangles[i]];
                    float ux = B.X - A.X, uy = B.Y - A.Y, uz = B.Z - A.Z, vx = C.X - A.X, vy = C.Y - A.Y, vz = C.Z - A.Z;
                    float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                    if (cx * N.X + cy * N.Y + cz * N.Z > 0) pa++; else pd++;
                }
            }
            if (shown++ < 40) Console.WriteLine($"{t.Name}: ccw {pa} cw {pd}  nodes {mo.Nodes.Count}");
        }
        return 0;
    }
}

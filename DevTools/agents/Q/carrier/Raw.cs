using System;
using SomniumSpace.Worlds.SomniumHalo;
static class Raw
{
    public static int Run(string[] a)
    {
        var m = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\" + a[1] + ".map");
        foreach (HaloTag t in m.OfClass("vehi"))
        {
            if (t.Name.IndexOf(a[2]) < 0) continue;
            var r = t.Root; var d = new HaloVehicleDef(t);
            if (a.Length > 3) { var hm = new HaloModel(d.Model); foreach (var s in d.Seats) if (s.Label.Contains(a[3])) foreach (var mk in hm.Markers) if (mk.Name == s.Marker) foreach (var mi in mk.Instances) { var tr = mi.Translation; int n = mi.Node; string ch = ""; while (n >= 0) { var nd = hm.Nodes[n]; ch += $" {nd.Name}({nd.Translation} q {nd.Qi:0.##},{nd.Qj:0.##},{nd.Qk:0.##},{nd.Qw:0.##})"; n = nd.Parent; } Console.WriteLine($"seat {s.Label} marker {mk.Name} node {mi.Node} t {tr} q {mi.Qi:0.##},{mi.Qj:0.##},{mi.Qk:0.##},{mi.Qw:0.##} chain{ch}"); } }
            if (Environment.GetEnvironmentVariable("DESC") == "1") foreach (var ln in d.Describe()) Console.WriteLine(ln);
            Console.WriteLine($"{t.Name}: accel scale {r.F32(32)} bound r {r.F32(4)} seat accel scale {r.Vec3(512)} flags {d.VehicleFlags:X} type {d.Type} unit flags {r.U32(380):X}");
            Console.Write("  vehicle 752..944 floats:");
            for (int o = 752; o < 944; o += 4) { float f = r.F32(o); if (f != 0) Console.Write($" @{o}={f}"); }
            Console.WriteLine();
            if (d.PhysicsTag != null)
            {
                var p = d.PhysicsTag.Root; Console.Write("  physics 0..104:");
                for (int o = 0; o < 104; o += 4) { float f = p.F32(o); if (f != 0) Console.Write($" @{o}={f}"); }
                Console.WriteLine();
                foreach (var im in p.Block(92, 72)) { Console.Write("  inertial:"); for (int o = 0; o < 72; o += 4) Console.Write(" " + im.F32(o).ToString("0.######")); Console.WriteLine(); }
            }
        }
        return 0;
    }
}

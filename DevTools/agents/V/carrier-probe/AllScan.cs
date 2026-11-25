using System;
using System.Collections.Generic;
using System.Globalization;
using SomniumSpace.Worlds.SomniumHalo;

// all <model> <scale> [maps]: hull touch ticks of every scripted human-plane recording chain (collideable flights), per map
static class AllScan
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    public static int Run(string[] a)
    {
        string model = a[1]; double spd = double.Parse(a[2], CultureInfo.InvariantCulture);
        string[] maps = a.Length > 3 ? a[3].Split(',') : new[] { "a10", "a30", "a50", "b30", "b40", "c10", "c40", "d20", "d40" };
        bool verbose = Environment.GetEnvironmentVariable("V") == "1";
        int total = 0, flights = 0, bad = 0;
        foreach (var mn in maps)
        {
            var m = new HaloMap(Maps + mn + ".map"); var sc = new HaloScenario(m); var cd = new HaloCutsceneData(m.Scenario);
            Drop.SetCols(sc);
            int mt = 0;
            foreach (var ch in Carrier.Chains(mn))
            {
                int ni = sc.ObjectNames.FindIndex(n => string.Equals(n, ch.Obj, StringComparison.OrdinalIgnoreCase));
                HaloScenario.Placement veh = null; foreach (var p in sc.Vehicles) if (p.NameIndex == ni && ni >= 0) veh = p;
                if (veh == null || veh.Type == null) continue;
                var d = new HaloVehicleDef(veh.Type);
                if (d.Type != HaloVehicleDef.Kind.HumanPlane || d.Physics == null) continue;
                var s = new Drop.St();
                if (ch.Flag != null) { var f = cd.FindFlag(ch.Flag); if (f == null) continue; s.x = f.Position.X; s.y = f.Position.Y; s.z = f.Position.Z; s.yaw = f.Yaw; }
                else { s.x = veh.Position.X; s.y = veh.Position.Y; s.z = veh.Position.Z; s.yaw = veh.Rotation.X; }
                string line = "";
                if (ch.Flag == null) continue;
                foreach (var r in ch.Recs.GetRange(0, 1))
                {
                    var rec = cd.Recording(r.rec); if (rec == null) break;
                    // a30 makes its wave dropships non-collideable 30 ticks into their exits (object_set_collideable false): not counted
                    bool ghost = mn == "a30" && r.rec.EndsWith("_exit") && ch.Obj.EndsWith("_cship");
                    s.speed = s.side = s.vert = s.vx = s.vy = 0;
                    var pb = new HaloRecordingPlayback(rec); int n = 0, t = 0, first = -1;
                    do { Drop.Step(model, spd, d, pb.Control, s); n++; if ((!ghost || n < 30) && Drop.TouchPub(d, s.x, s.y, s.z, s.yaw)) { t++; if (first < 0) first = n; } } while (pb.Step());
                    flights++; mt += t; if (t > 0) bad++;
                    line += $" {r.rec}:{t}{(first >= 0 ? "@" + first : "")}";
                }
                if (verbose) Console.WriteLine($"  {mn} {ch.Obj}:{line}");
            }
            Console.WriteLine($" {mn}: touch {mt}");
            total += mt;
        }
        Console.WriteLine($"{model} {spd}: total touch ticks {total}, flights touching {bad} of {flights}");
        return 0;
    }
}

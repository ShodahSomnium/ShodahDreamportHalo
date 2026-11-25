using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

// eval: every flag-started human-plane chain on the given maps, its hover recordings played in order (enter -> drop), measured against
// the scenario's own truth: LZ volume (script volume the drop is tested against), designer placement, BSP contacts, end clearance.
static class Eval
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    // (map, object) -> volume the script ties to that drop
    static readonly Dictionary<string, string> Vol = new Dictionary<string, string>
    {
        {"a30/lz_cship","lz_landing"},{"a30/fort_cship","first_fort_lz"},{"a30/pipe_cship","first_pipe_lz"},{"a30/back_cship","first_back_lz"},
        {"a30/rubble_rock_cship","rubble_rock_lz"},{"a30/rubble_middle_cship","rubble_middle_lz"},{"a30/rubble_boat_cship","rubble_boat_lz"},
        {"a30/river_boat_cship","river_boat_lz"},{"a30/foehammer_first","first_drop"},{"b30/hog_pelican","jeep_delivery"},
    };
    static readonly HashSet<string> PlacementTruth = new HashSet<string> { "b30/insertion_pelican_1", "b30/insertion_pelican_2" };
    static readonly Dictionary<string, string> Extra = new Dictionary<string, string>();
    // drops judged at the cargo (jeep) marker: pelican cargo marker (-1.662, 0) in the hull frame
    static readonly HashSet<string> Cargo = new HashSet<string> { "a30/foehammer_first", "b30/hog_pelican" };

    static List<HaloCollisionBsp> Cols = new List<HaloCollisionBsp>();
    sealed class MapData { public List<Fly.Flight> Flights; public HaloMissionData Md; public HaloCutsceneData Cd; public List<HaloCollisionBsp> Cols = new List<HaloCollisionBsp>(); }
    static readonly Dictionary<string, MapData> Cache = new Dictionary<string, MapData>();
    static bool Open(double x, double y, double z) { foreach (var c in Cols) if (c != null && c.Leaf((float)x, (float)y, (float)z) >= 0) return true; return false; }
    public static double Gnd(double x, double y, double z) { if (!Open(x, y, z)) return -2; for (double d = 0; d < 400; d += 0.1) if (!Open(x, y, z - d)) return d; return -1; }
    static readonly double[][] Dirs = { new double[] { 1, 0, 0 }, new double[] { -1, 0, 0 }, new double[] { 0, 1, 0 }, new double[] { 0, -1, 0 }, new double[] { 0, 0, 1 }, new double[] { 0, 0, -1 }, new double[] { 0, 0, 0 } };
    public static bool Touch(HaloVehicleDef d, double x, double y, double z, double yaw)
    {
        if (d.Physics == null) return false;
        double c = Math.Cos(yaw), s = Math.Sin(yaw);
        foreach (var p in d.Physics.Points)
        {
            double px = x + c * p.Position.X - s * p.Position.Y, py = y + s * p.Position.X + c * p.Position.Y, pz = z + p.Position.Z;
            foreach (var dd in Dirs) if (!Open(px + dd[0] * p.Radius, py + dd[1] * p.Radius, pz + dd[2] * p.Radius)) return true;
        }
        return false;
    }

    public sealed class Row { public string Name, Recs, Target; public double Miss = double.NaN; public bool? Inside; public double Clear; public int Touch, Stops; public string End; public bool Truth; public int TouchEnter, TouchEnterFirst = -1, Crouch, Jump, CrouchLast, JumpLast; public double PitchClimb, PitchClimbLast, Z0, Zend; }

    public static List<Row> Rows(Fly.Model m, string[] maps, bool collide = true)
    {
        var rows = new List<Row>();
        foreach (var mn in maps)
        {
            MapData D;
            if (!Cache.TryGetValue(mn, out D))
            {
                D = new MapData { Flights = Fly.Flights(mn) };
                var map = new HaloMap(Maps + mn + ".map"); var scn = new HaloScenario(map); D.Md = new HaloMissionData(scn); D.Cd = new HaloCutsceneData(map.Scenario);
                for (int i = 0; i < scn.Bsps.Count; i++) { HaloCollisionBsp col = null; foreach (var cb in scn.BspRoot(i).Block(176, 96)) { col = new HaloCollisionBsp(cb); break; } D.Cols.Add(col); }
                Cache[mn] = D;
            }
            var flights = D.Flights; if (flights.Count == 0) continue;
            var md = D.Md; var cd = D.Cd; Cols = D.Cols;
            var done = new HashSet<string>();
            foreach (var f in flights)
            {
                if (f.From == "placement" || f.Vehicle == "fighterbomber") continue;
                var recs = new List<HaloRecording>();
                for (int k = 0; k < f.R.Count; k++) { if (f.Recs[k].kind == "rp_and_delete") break; recs.Add(f.R[k]); if (f.Recs[k].kind == "rp") break; }
                string ex; if (Extra.TryGetValue(mn + "/" + f.Obj, out ex)) recs.Add(cd.Recording(ex));
                if (recs.Count == 0) continue;
                string key = mn + "/" + f.Obj + "/" + string.Join(",", recs.Select(r => r.Name));
                if (!done.Add(key)) continue;
                var row = new Row { Name = mn + " " + f.Obj, Recs = string.Join("+", recs.Select(r => r.Name)) };
                var s = new Fly.St { x = f.Start.X, y = f.Start.Y, z = f.Start.Z, yaw = f.Yaw0 };
                bool wasTouch = Touch(f.Def, s.x, s.y, s.z, s.yaw);
                foreach (var r in recs)
                {
                    s.speed = s.side = s.vert = s.vx = s.vy = s.w = 0;
                    var pb = new HaloRecordingPlayback(r);
                    do
                    {
                        double ox = s.x, oy = s.y, oz = s.z;
                        Fly.Step(m, f.Def, pb.Control, s);
                        { bool last = r == recs[recs.Count - 1]; double ps = Math.Sin(pb.Control.FacingPitchRadians) * Math.Sqrt(s.vx * s.vx + s.vy * s.vy); row.PitchClimb += ps; if (last) row.PitchClimbLast += ps; if (pb.Control.Has(HaloRecordedControl.Crouch)) { row.Crouch++; if (last) row.CrouchLast++; } if (pb.Control.Has(HaloRecordedControl.JumpBit)) { row.Jump++; if (last) row.JumpLast++; } }
                        if (collide && !wasTouch && Touch(f.Def, s.x, s.y, s.z, s.yaw))
                        {
                            // the hull stops at the contact; keep the part of the move that stays clear (horizontal, then vertical)
                            row.Stops++;
                            double nx = s.x, ny = s.y, nz = s.z;
                            if (!Touch(f.Def, nx, ny, oz, s.yaw)) { s.z = oz; s.vert = 0; }
                            else if (!Touch(f.Def, ox, oy, nz, s.yaw)) { s.x = ox; s.y = oy; s.speed = 0; s.vx = s.vy = 0; }
                            else { s.x = ox; s.y = oy; s.z = oz; s.speed = 0; s.vx = s.vy = 0; s.vert = 0; }
                        }
                        bool t = Touch(f.Def, s.x, s.y, s.z, s.yaw);
                        if (t) { row.Touch++; if (r == recs[0] && recs.Count > 1) { row.TouchEnter++; if (row.TouchEnterFirst < 0) row.TouchEnterFirst = pb.Tick; } }
                        wasTouch = t;
                    } while (pb.Step());
                }
                row.End = $"({s.x:0.0},{s.y:0.0},{s.z:0.0})"; row.Z0 = f.Start.Z; row.Zend = s.z;
                row.Clear = Gnd(s.x, s.y, s.z);
                string vn;
                if (Vol.TryGetValue(mn + "/" + f.Obj, out vn))
                {
                    int vi = md.TriggerVolumeIndex(vn);
                    if (vi >= 0)
                    {
                        if (Cargo.Contains(mn + "/" + f.Obj)) { s.x += Math.Cos(s.yaw) * -1.662; s.y += Math.Sin(s.yaw) * -1.662; }
                        var v = md.TriggerVolumes[vi]; var cc = v.Center;
                        row.Target = vn + $" {Math.Abs(v.Extent.X):0}x{Math.Abs(v.Extent.Y):0}";
                        row.Miss = Math.Sqrt((s.x - cc.X) * (s.x - cc.X) + (s.y - cc.Y) * (s.y - cc.Y));
                        row.Inside = v.Contains((float)s.x, (float)s.y, cc.Z);
                        row.Truth = vn != "river_boat_lz";
                    }
                }
                else if (f.Target != null && PlacementTruth.Contains(mn + "/" + f.Obj))
                {
                    var t = f.Target.Value; row.Target = "placement";
                    row.Miss = Math.Sqrt((s.x - t.X) * (s.x - t.X) + (s.y - t.Y) * (s.y - t.Y)); row.Truth = true;
                    row.End += $" dz {s.z - t.Z:0.0}";
                }
                rows.Add(row);
            }
        }
        return rows;
    }

    public static int Run(Fly.Model m, string[] maps)
    {
        var rows = Rows(m, maps, Environment.GetEnvironmentVariable("NOCOLL") != "1");
        Console.WriteLine("model " + m);
        double sum = 0; int n = 0, inside = 0, vols = 0, touchFlights = 0;
        foreach (var r in rows)
        {
            Console.WriteLine($"{r.Name,-30} {r.Recs,-58} end {r.End,-30} clear {(r.Clear == -2 ? "SOLID" : r.Clear.ToString("0.0")),6} touch {r.Touch,4} (enter {r.TouchEnter}{(r.TouchEnterFirst >= 0 ? "@" + r.TouchEnterFirst : "")}) stops {r.Stops,4}" + (r.Target != null ? $" | {r.Target} miss {r.Miss:0.0}" + (r.Inside == null ? "" : r.Inside.Value ? " INSIDE" : " out") : ""));
            if (r.Truth) { sum += r.Miss; n++; }
            if (r.Inside != null && r.Truth) { vols++; if (r.Inside.Value) inside++; }
            if (r.Touch > 0) touchFlights++;
        }
        Console.WriteLine($"SCORE truth miss sum {sum:0.0} over {n}; inside {inside}/{vols}; flights touching {touchFlights}/{rows.Count}");
        return 0;
    }
}
static class BestS
{
    // per truth flight: best speed scale (min miss) and the miss there, collisions off
    public static int Run(Fly.Model m, string[] maps)
    {
        var res = new Dictionary<string, List<(double s, double miss, bool? inside)>>();
        for (double sc = 0.5; sc <= 1.05; sc += 0.01)
        {
            var m2 = m.Clone(); m2.S = sc;
            foreach (var r in Eval.Rows(m2, maps, false))
            {
                if (!r.Truth) continue;
                if (!res.ContainsKey(r.Name)) res[r.Name] = new List<(double, double, bool?)>();
                res[r.Name].Add((sc, r.Miss, r.Inside));
            }
        }
        Console.WriteLine("model " + m);
        foreach (var kv in res)
        {
            var b = kv.Value.OrderBy(v => v.miss).First();
            var ins = kv.Value.Where(v => v.inside == true).Select(v => v.s).ToList();
            Console.WriteLine($"{kv.Key,-30} best S {b.s:0.00} miss {b.miss:0.0}" + (ins.Count > 0 ? $"  inside for S {ins.Min():0.00}..{ins.Max():0.00}" : ""));
        }
        return 0;
    }
}
static class Scan
{
    public static Fly.Model Parse(Fly.Model b, string cfg)
    {
        var m = b.Clone();
        foreach (var kv in cfg.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = kv.Split('='); double v; double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
            switch (p[0])
            {
                case "S": m.S = v; break; case "VEL": m.Vel = p[1]; break; case "TURN": m.Turn = v; break; case "VUP": m.VUp = v; break;
                case "VACC": m.VAcc = v; break; case "PITCHK": m.PitchK = v; break; case "COSP": m.CosP = v != 0; break; case "A": m.A = v; break;
                case "YAWK": m.YawK = v; break; case "VMODE": m.VMode = p[1]; break; case "GS": m.GS = v; break; case "AIRF": m.AirF = v; break; case "PITCHNEG": m.PitchNeg = v != 0; break; case "KV": m.KV = v; m.KVZ = v; break; case "KVZ": m.KVZ = v; break; case "RAMP": m.Ramp = v != 0; break; case "AIRFV": m.AirFV = v; break; case "KVD": m.KVD = v; break; case "KL": m.KL = v; break; case "WA": m.WA = v; break; case "ACCS": m.AccS = v; break; case "DECS": m.DecS = v; break; case "WD": m.WD = v; break;
            }
        }
        return m;
    }
    public static int Run(Fly.Model b, string[] maps, string[] cfgs)
    {
        foreach (var cfg in cfgs)
        {
            double bestSum = 1e9, bestS = 0; int bestIn = -1, nv = 0; string worst = "";
            double smin = double.Parse(Environment.GetEnvironmentVariable("SMIN") ?? "0.6", System.Globalization.CultureInfo.InvariantCulture), smax = double.Parse(Environment.GetEnvironmentVariable("SMAX") ?? "1.0", System.Globalization.CultureInfo.InvariantCulture);
            for (double sc = smin; sc <= smax + 1e-4; sc += 0.01)
            {
                var m = Parse(b, cfg); m.S = sc;
                var rows = Eval.Rows(m, maps, false);
                double sum = 0; int ins = 0; nv = 0; double wm = 0; string wn = "";
                foreach (var r in rows) { if (!r.Truth) continue; sum += r.Miss; if (r.Inside != null) { nv++; if (r.Inside.Value) ins++; } if (r.Miss > wm) { wm = r.Miss; wn = r.Name; } }
                if (ins > bestIn || (ins == bestIn && sum < bestSum)) { bestIn = ins; bestSum = sum; bestS = sc; worst = wn + " " + wm.ToString("0.0"); }
            }
            Console.WriteLine($"{cfg,-40} best S {bestS:0.00} inside {bestIn}/{nv} miss sum {bestSum:0.0} worst {worst}");
        }
        return 0;
    }
}
static class Vert
{
    public static int Run(Fly.Model m, string[] maps)
    {
        var mv = Scan.Parse(m, "VUP=0"); // horizontal only: ground under the end point
        Console.WriteLine("model " + mv + "  (no vertical motion; ground = z - clear)");
        foreach (var r in Eval.Rows(mv, maps, false))
        {
            double ground = r.Clear >= 0 ? r.Zend - r.Clear : double.NaN;
            Console.WriteLine($"{r.Name,-28} {r.Recs,-56} z0 {r.Z0,6:0.0} ground {ground,6:0.0} need {r.Z0 - ground,6:0.0} | crouch {r.Crouch,4} (last {r.CrouchLast,4}) jump {r.Jump,4} (last {r.JumpLast,3}) sum sin(p)*v {r.PitchClimb,6:0.0} (last {r.PitchClimbLast,5:0.0})" + (r.Target != null ? " miss " + r.Miss.ToString("0.0") : ""));
        }
        return 0;
    }
}

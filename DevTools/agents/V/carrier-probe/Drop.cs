using System;
using System.Collections.Generic;
using System.Globalization;
using SomniumSpace.Worlds.SomniumHalo;

// drop <model> <speedScale> [trace rec]
static class Drop
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static List<HaloCollisionBsp> Cols = new List<HaloCollisionBsp>();
    public static bool Open(double x, double y, double z) { foreach (var c in Cols) if (c != null && c.Leaf((float)x, (float)y, (float)z) >= 0) return true; return false; }
    static double Gnd(double x, double y, double z) { if (!Open(x, y, z)) return -2; for (double d = 0; d < 300; d += 0.1) if (!Open(x, y, z - d)) return d; return -1; }
    static HaloScenario sc; static HaloCutsceneData cd; static HaloMissionData md;
    public static string MapName = Environment.GetEnvironmentVariable("MAP") ?? "a30";

    public static readonly string[][] A30 =
    {
        new[]{"lz_cship","lz_cship_flag","lz_landing","lz_cship_enter","lz_cship_landing_drop"},
        new[]{"fort_cship","fort_cship_flag","first_fort_lz","fort_cship_enter","fort_cship_drop"},
        new[]{"pipe_cship","pipe_cship_flag","first_pipe_lz","pipe_cship_enter","pipe_cship_drop"},
        new[]{"back_cship","back_cship_flag","first_back_lz","back_cship_enter","back_cship_drop"},
        new[]{"rubble_rock_cship","rubble_rock_cship_flag","rubble_rock_lz","rubble_rock_cship_enter","rubble_rock_cship_drop"},
        new[]{"rubble_middle_cship","rubble_middle_cship_flag","rubble_middle_lz","rubble_middle_cship_enter","rubble_middle_cship_drop"},
        new[]{"rubble_boat_cship","rubble_boat_cship_flag","rubble_boat_lz","rubble_boat_cship_enter","rubble_boat_cship_drop"},
        new[]{"river_boat_cship","river_boat_cship_flag","river_boat_lz","river_boat_cship_enter","river_boat_cship_drop"},
        new[]{"foehammer_first","foehammer_first_flag","first_drop","foehammer_first_in"},
    };

    public static void SetCols(HaloScenario s) { Cols.Clear(); for (int i = 0; i < s.Bsps.Count; i++) { HaloCollisionBsp col = null; foreach (var cb in s.BspRoot(i).Block(176, 96)) { col = new HaloCollisionBsp(cb); break; } Cols.Add(col); } }
    public static bool TouchPub(HaloVehicleDef d, double x, double y, double z, double yaw) { return Touch(d, x, y, z, yaw); }
    public static void Load()
    {
        if (sc != null) return;
        var m = new HaloMap(Maps + MapName + ".map"); sc = new HaloScenario(m);
        cd = new HaloCutsceneData(m.Scenario); md = new HaloMissionData(sc);
        for (int i = 0; i < sc.Bsps.Count; i++) { HaloCollisionBsp col = null; foreach (var cb in sc.BspRoot(i).Block(176, 96)) { col = new HaloCollisionBsp(cb); break; } Cols.Add(col); }
    }

    static HaloVehicleDef Def(string obj)
    {
        int ni = sc.ObjectNames.FindIndex(n => string.Equals(n, obj, StringComparison.OrdinalIgnoreCase));
        foreach (var p in sc.Vehicles) if (p.NameIndex == ni && ni >= 0) return new HaloVehicleDef(p.Type);
        return null;
    }

    public sealed class St { public double x, y, z, yaw, pitch, speed, side, vert, vx, vy; }

    public static void Step(string model, double spd, HaloVehicleDef d, HaloRecordedControl c, St s)
    {
        double vsc = 1;
        if (!c.Has(HaloRecordedControl.LookDontTurn)) { s.yaw = c.FacingYawRadians; s.pitch = c.FacingPitchRadians; }
        double top = (c.ThrottleForward >= 0 ? d.MaxForward : d.MaxReverse) * c.ThrottleForward * spd;
        s.speed = Toward(s.speed, top, Math.Abs(top) > Math.Abs(s.speed) ? d.SpeedAcceleration : d.SpeedDeceleration);
        double st = c.ThrottleLeft * (c.ThrottleLeft >= 0 ? d.MaxLeftSlide : d.MaxRightSlide);
        s.side = Toward(s.side, st, Math.Abs(st) > Math.Abs(s.side) ? d.SlideAcceleration : d.SlideDeceleration);
        double vt = c.Has(HaloRecordedControl.JumpBit) ? d.MaxLeftSlide * vsc : c.Has(HaloRecordedControl.Crouch) ? -d.MaxRightSlide * vsc : 0;
        s.vert = Toward(s.vert, vt, Math.Abs(vt) > Math.Abs(s.vert) ? d.SlideAcceleration : d.SlideDeceleration);
        double cp = model == "pitch" || model == "cosp" ? Math.Cos(s.pitch) : 1, sp = model == "pitch" ? Math.Sin(s.pitch) : 0;
        double hx = Math.Cos(s.yaw), hy = Math.Sin(s.yaw);
        if (model.StartsWith("vec"))
        {
            double tx = hx * top - hy * st, ty = hy * top + hx * st;
            double dx = tx - s.vx, dy = ty - s.vy, dl = Math.Sqrt(dx * dx + dy * dy);
            double acc = Math.Sqrt(tx * tx + ty * ty) > Math.Sqrt(s.vx * s.vx + s.vy * s.vy) + 1e-9 ? d.SpeedAcceleration : d.SpeedDeceleration;
            if (model == "vecA") acc = d.SpeedAcceleration;
            if (dl > acc) { dx *= acc / dl; dy *= acc / dl; }
            s.vx += dx; s.vy += dy;
            s.x += s.vx; s.y += s.vy; s.z += s.vert; s.speed = Math.Sqrt(s.vx * s.vx + s.vy * s.vy);
        }
        else if (model == "slide")
        {
            double dx = hx * s.speed - hy * s.side, dy = hy * s.speed + hx * s.side, dz = s.vert;
            if (!Touch(d, s.x + dx, s.y + dy, s.z + dz, s.yaw)) { s.x += dx; s.y += dy; s.z += dz; return; }
            for (int it = 0; it < 3; it++)
            {
                double nx, ny, nz; Normal(d, s.x + dx, s.y + dy, s.z + dz, s.yaw, out nx, out ny, out nz);
                double dot = dx * nx + dy * ny + dz * nz;
                if (dot < 0) { dx -= nx * dot; dy -= ny * dot; dz -= nz * dot; double len = Math.Sqrt(dx * dx + dy * dy + dz * dz) + 1e-9; double into = -dot / (len + Math.Abs(dot)); }
                if (!Touch(d, s.x + dx, s.y + dy, s.z + dz, s.yaw)) { s.x += dx; s.y += dy; s.z += dz; Slid++; return; }
            }
            Stuck++;
        }
        else { s.x += hx * cp * s.speed - hy * s.side; s.y += hy * cp * s.speed + hx * s.side; s.z += sp * s.speed + s.vert; }
    }

    public static List<double[]> Path(string obj, string model, double spd)
    {
        Load();
        string[] ch = null; foreach (var c in A30) if (c[0] == obj) ch = c;
        var d = Def(obj); var f = cd.FindFlag(ch[1]);
        var s = new St { x = f.Position.X, y = f.Position.Y, z = f.Position.Z, yaw = f.Yaw };
        var res = new List<double[]>();
        var pb = new HaloRecordingPlayback(cd.Recording(ch[3])); int n = 0;
        do { Step(model, spd, d, pb.Control, s); n++; res.Add(new[] { s.x, s.y, s.z, n }); } while (pb.Step());
        return res;
    }

    public static int Run(string[] a)
    {
        string model = a[1]; double spd = double.Parse(a[2], CultureInfo.InvariantCulture);
        string trace = a.Length > 3 ? a[3] : null;
        int every = int.Parse(Environment.GetEnvironmentVariable("EVERY") ?? "30");
        Load();
        double sumMiss = 0; int sumTouch = 0;
        foreach (var ch in A30)
        {
            var d = Def(ch[0]); var f = cd.FindFlag(ch[1]);
            if (d == null || f == null) continue;
            var s = new St { x = f.Position.X, y = f.Position.Y, z = f.Position.Z, yaw = f.Yaw };
            string line = ch[0] + ":";
            for (int k = 3; k < ch.Length; k++)
            {
                var rec = cd.Recording(ch[k]); var pb = new HaloRecordingPlayback(rec);
                s.speed = s.side = s.vert = s.vx = s.vy = 0;
                int n = 0, touch = 0, firstTouch = -1;
                do
                {
                    var c = pb.Control;
                    Step(model, spd, d, c, s);
                    n++;
                    bool tch = k == 3 && d.Physics != null && Touch(d, s.x, s.y, s.z, s.yaw);
                    if (tch) { touch++; if (firstTouch < 0) firstTouch = n; }
                    if (trace != null && rec.Name == trace && n % every == 0) Console.WriteLine($"   t{n} ({s.x:0.0},{s.y:0.0},{s.z:0.0}) gnd {Gnd(s.x, s.y, s.z):0.0} spd {s.speed:0.000} yaw {s.yaw * 57.3:0} pitch {s.pitch * 57.3:0} c{(c.Has(1) ? 1 : 0)}j{(c.Has(2) ? 1 : 0)} {(tch ? Why : "")}");
                } while (pb.Step());
                double ge = Gnd(s.x, s.y, s.z);
                if (k == 3 && d.Physics != null) { double fl = s.z - (ge >= 0 ? ge : 0); double hz = fl; for (double zz = s.z; zz > fl - 1; zz -= 0.1) if (Touch(d, s.x, s.y, zz, s.yaw)) { hz = zz; break; } line += $" [descends to {hz - fl:0.0} over floor {fl:0.0}: {Why}]"; }
                line += $" | {rec.Name} {n}t end ({s.x:0.0},{s.y:0.0},{s.z:0.0}) gnd {(ge == -2 ? "SOLID" : ge.ToString("0.0"))}" + (k == 3 ? $" TOUCH {touch}" + (firstTouch >= 0 ? "@" + firstTouch : "") : "");
                if (k == 3) sumTouch += touch;
            }
            int vi = md.TriggerVolumeIndex(ch[2]);
            if (vi >= 0)
            {
                var v = md.TriggerVolumes[vi]; var cc = v.Center;
                double dist = Math.Sqrt((s.x - cc.X) * (s.x - cc.X) + (s.y - cc.Y) * (s.y - cc.Y));
                bool inside = v.Contains((float)s.x, (float)s.y, cc.Z);
                line += $" || {ch[2]} {dist:0.0} wu{(inside ? " INSIDE" : "")}";
                if (ch[0] != "river_boat_cship") sumMiss += dist;
            }
            Console.WriteLine(line);
        }
        Console.WriteLine($"SUM miss {sumMiss:0} wu, enter touch ticks {sumTouch}, slid {Slid} stuck {Stuck}");
        return 0;
    }

    static readonly double[][] Dirs = { new double[]{1,0,0}, new double[]{-1,0,0}, new double[]{0,1,0}, new double[]{0,-1,0}, new double[]{0,0,1}, new double[]{0,0,-1}, new double[]{0,0,0} };
    public static int Slid, Stuck;
    static void Normal(HaloVehicleDef d, double x, double y, double z, double yaw, out double nx, out double ny, out double nz)
    {
        double c = Math.Cos(yaw), s = Math.Sin(yaw); nx = ny = nz = 0;
        foreach (var p in d.Physics.Points)
        {
            double px = x + c * p.Position.X - s * p.Position.Y, py = y + s * p.Position.X + c * p.Position.Y, pz = z + p.Position.Z;
            for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++) for (int k = -1; k <= 1; k++)
            {
                if (i == 0 && j == 0 && k == 0) continue;
                double l = Math.Sqrt(i * i + j * j + k * k);
                if (!Open(px + i / l * p.Radius, py + j / l * p.Radius, pz + k / l * p.Radius)) { nx -= i / l; ny -= j / l; nz -= k / l; }
            }
        }
        double n = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (n < 1e-9) { nz = 1; return; } nx /= n; ny /= n; nz /= n;
    }
    public static string Why = "";
    static bool Touch(HaloVehicleDef d, double x, double y, double z, double yaw)
    {
        double c = Math.Cos(yaw), s = Math.Sin(yaw); Why = "";
        foreach (var p in d.Physics.Points)
        {
            double px = x + c * p.Position.X - s * p.Position.Y, py = y + s * p.Position.X + c * p.Position.Y, pz = z + p.Position.Z;
            int bad = 0; for (int i = 0; i < Dirs.Length; i++) { var dd = Dirs[i]; if (!Open(px + dd[0] * p.Radius, py + dd[1] * p.Radius, pz + dd[2] * p.Radius)) bad++; }
            if (bad > 0) Why += p.Name.Replace(" ", "") + "/" + bad + " ";
        }
        return Why.Length > 0;
    }
    static double Toward(double v, double t, double a) { if (a <= 0) return t; return v < t ? Math.Min(t, v + a) : Math.Max(t, v - a); }
}
static class DropExt { }

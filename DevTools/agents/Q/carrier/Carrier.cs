using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SomniumSpace.Worlds.SomniumHalo;

// Investigation probe (carriers): dead-reckons scripted vehicle recordings (dropships / pelicans) from their object_teleport flag
// with several flight models and measures them against the BSP collision (solid ticks, end height over ground, chain continuity).
static class Carrier
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    const string Hsc = @"D:\Dreamport\Halo\Halo Dreamport\DevTools\agents\S\hsc\";

    static double VScale = 1; static bool VFwd = Environment.GetEnvironmentVariable("VFWD") == "1"; static double PK = double.Parse(Environment.GetEnvironmentVariable("PK") ?? "1", System.Globalization.CultureInfo.InvariantCulture); static bool PowOnly = Environment.GetEnvironmentVariable("POWONLY") == "1"; static string Trace = Environment.GetEnvironmentVariable("TRACE"); static int[] Models = { 0, 1, 2, 3 };
    static bool Solid(List<HaloCollisionBsp> cols, State s) { foreach (var c in cols) if (c != null && c.Leaf((float)s.X, (float)s.Y, (float)s.Z) >= 0) return false; return true; }
    public static int Run(string[] a)
    {
        var ms = Environment.GetEnvironmentVariable("MODELS"); if (ms != null) Models = ms.Split(",").Select(int.Parse).ToArray();
        var vs = Environment.GetEnvironmentVariable("VSCALE"); if (vs != null) VScale = double.Parse(vs, System.Globalization.CultureInfo.InvariantCulture);
        string only = a.Length > 0 ? a[0] : null;
        if (a.Length > 2 && a[1] == "place")
        {
            var m = new HaloMap(Maps + only + ".map"); var sc = new HaloScenario(m);
            var cols = new List<HaloCollisionBsp>();
            for (int i = 0; i < sc.Bsps.Count; i++) { HaloCollisionBsp col = null; foreach (var cb in sc.BspRoot(i).Block(176, 96)) { col = new HaloCollisionBsp(cb); break; } cols.Add(col); }
            foreach (var p in sc.Vehicles)
            {
                string nm = p.NameIndex >= 0 && p.NameIndex < sc.ObjectNames.Count ? sc.ObjectNames[p.NameIndex] : "";
                if (!Regex.IsMatch(nm, a[2])) continue;
                var st = new State { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z };
                var b = BestBsp(cols, st);
                Console.WriteLine($"{nm} {p.Type?.Name} @ {Fmt(p.Position)} gnd+{Height(b, st):0.0} notplaced {p.NotPlaced}");
            }
            return 0;
        }
        if (a.Length > 4 && a[1] == "near")
        {
            // scenery / machines within r wu (horizontal) of (x,y): objects the BSP height test cannot see
            var m = new HaloMap(Maps + only + ".map"); var sc = new HaloScenario(m);
            double x = double.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture), y = double.Parse(a[3], System.Globalization.CultureInfo.InvariantCulture), r = double.Parse(a[4], System.Globalization.CultureInfo.InvariantCulture);
            foreach (var (kind, list) in new[] { ("scen", sc.Scenery), ("mach", sc.Machines) })
                foreach (var p in list)
                {
                    double dx = p.Position.X - x, dy = p.Position.Y - y;
                    if (dx * dx + dy * dy > r * r) continue;
                    Console.WriteLine($"{kind} {p.Type?.Name} @ {Fmt(p.Position)} d {Math.Sqrt(dx * dx + dy * dy):0.0}");
                }
            return 0;
        }
        bool stats = a.Length > 1 && a[1] == "stats";
        foreach (var mn in new[] { "a10", "a30", "a50", "b30", "b40", "c10", "c40", "d20", "d40" })
        {
            if (only != null && only != "all" && only != mn) continue;
            Map(mn, stats);
        }
        return 0;
    }

    internal sealed class Chain { public string Obj, Flag; public List<(string kind, string rec)> Recs = new List<(string, string)>(); public string Script; }

    internal static List<Chain> Chains(string mn)
    {
        var text = File.ReadAllText(Hsc + mn + ".hsc");
        var res = new List<Chain>();
        var scripts = Regex.Split(text, @"\n\(script ");
        foreach (var s in scripts)
        {
            string sname = s.Split('\n')[0];
            Chain cur = null;
            foreach (Match m in Regex.Matches(s, @"\((object_teleport|recording_play_and_hover|recording_play_and_delete|recording_play) (\(unit )?([A-Za-z0-9_]+)\)? ([A-Za-z0-9_]+)\)"))
            {
                string fn = m.Groups[1].Value, obj = m.Groups[3].Value, arg = m.Groups[4].Value;
                if (fn == "object_teleport") { cur = new Chain { Obj = obj, Flag = arg, Script = sname }; res.Add(cur); continue; }
                if (cur != null && cur.Obj == obj) cur.Recs.Add((fn.Replace("recording_play", "rp"), arg));
                else { var c = new Chain { Obj = obj, Flag = null, Script = sname }; c.Recs.Add((fn.Replace("recording_play", "rp"), arg)); res.Add(c); cur = c; }
            }
        }
        return res.Where(c => c.Recs.Count > 0).ToList();
    }

    static void Map(string mn, bool statsOnly)
    {
        var m = new HaloMap(Maps + mn + ".map");
        var sc = new HaloScenario(m);
        var cd = new HaloCutsceneData(m.Scenario);
        var cols = new List<HaloCollisionBsp>();
        for (int i = 0; i < sc.Bsps.Count; i++) { HaloCollisionBsp col = null; foreach (var cb in sc.BspRoot(i).Block(176, 96)) { col = new HaloCollisionBsp(cb); break; } cols.Add(col); }
        Cols = cols;
        Console.WriteLine($"===== {mn}: {sc.Bsps.Count} bsps");
        foreach (var ch in Chains(mn))
        {
            int ni = sc.ObjectNames.FindIndex(n => string.Equals(n, ch.Obj, StringComparison.OrdinalIgnoreCase));
            var veh = sc.Vehicles.FirstOrDefault(p => p.NameIndex == ni && ni >= 0);
            if (veh == null || veh.Type == null) continue;     // bipeds etc.
            var def = new HaloVehicleDef(veh.Type);
            if (def.Type != HaloVehicleDef.Kind.HumanPlane && def.Type != HaloVehicleDef.Kind.AlienFighter) continue;
            HVec3 start; float yaw0;
            if (ch.Flag != null) { var f = cd.FindFlag(ch.Flag); if (f == null) continue; start = f.Position; yaw0 = f.Yaw; }
            else { start = veh.Position; yaw0 = veh.Rotation.X; }
            Console.WriteLine($"-- {ch.Script.Trim()} : {ch.Obj} ({def.Name.Split('\\').Last()} {def.Type}) from {(ch.Flag ?? "placement")} {Fmt(start)} yaw {yaw0 * 57.3f:0} : " + string.Join(" -> ", ch.Recs.Select(r => r.kind + " " + r.rec)));
            if (statsOnly) { foreach (var r in ch.Recs) Stats(cd.Recording(r.rec)); continue; }
            foreach (int model in Models)
            {
                var st = new State { X = start.X, Y = start.Y, Z = start.Z, Yaw = yaw0 };
                string line = "   M" + model + ":";
                foreach (var r in ch.Recs)
                {
                    var rec = cd.Recording(r.rec);
                    if (rec == null) { line += " [missing " + r.rec + "]"; break; }
                    var pb = new HaloRecordingPlayback(rec);
                    int solid = 0, n = 0; Blocked = 0; Landed = 0;
                    var sx = st.X; var sy = st.Y; var sz = st.Z;
                    var bsp = BestBsp(cols, st);
                    if (model == 3 || model == 1 || model == 2) { /* a new recording keeps the vehicle's velocity */ }
                    do
                    {
                        Step(model, def, pb.Control, st);
                        if (Trace != null && r.rec == Trace && n % 40 == 0) { var tb = BestBsp(cols, st); Console.WriteLine($"      t{n} M{model} {Fmt(st)} gnd {Height(tb, st):0.0} solid {(Solid(cols, st) ? 1 : 0)} thr {pb.Control.ThrottleForward:0.0} pitch {pb.Control.FacingPitchRadians * 57.3:0} up {Up(cols, st):0.0} c{(pb.Control.Has(HaloRecordedControl.Crouch) ? 1 : 0)}j{(pb.Control.Has(HaloRecordedControl.JumpBit) ? 1 : 0)}"); }
                        n++;
                        if (Solid(cols, st)) solid++;
                    } while (pb.Step());
                    if (r.kind != "rp_and_hover") { st.Speed = 0; st.Side = 0; }
                    var eb = BestBsp(cols, st) ?? bsp;
                    double h = Height(eb, st);
                    line += $" | {r.rec}: {n}t {Dist(sx, sy, sz, st):0}wu end {Fmt(st)} gnd+{(h == -2 ? "IN-SOLID" : h < 0 ? "none" : h.ToString("0.0"))} solid {solid}" + (model >= 6 ? $" landed {Landed} blocked {Blocked}" : "");
                    // hover: velocity zeroed (held), as recording_play_and_hover / vehicle_hover
                    st.Speed = 0; st.Side = 0; st.VX = st.VY = st.VZ = 0;
                }
                Console.WriteLine(line);
            }
        }
    }

    static HaloCollisionBsp BestBsp(List<HaloCollisionBsp> cols, State s)
    {
        foreach (var c in cols) if (c != null && c.Leaf((float)s.X, (float)s.Y, (float)s.Z) >= 0 && Height(c, s) >= 0) return c;
        return cols.FirstOrDefault(c => c != null);
    }

    static double Height(HaloCollisionBsp c, State s)
    {
        if (c == null) return -1;
        if (c.Leaf((float)s.X, (float)s.Y, (float)s.Z) == -1) return -2;
        for (double d = 0; d < 300; d += 0.1) if (c.Leaf((float)s.X, (float)s.Y, (float)(s.Z - d)) == -1) return d;
        return -1;
    }

    sealed class State { public double X, Y, Z, Yaw, Pitch, Speed, VX, VY, VZ, Side; }
    static List<HaloCollisionBsp> Cols; static int Blocked, Landed;
    static bool SolidAt(double x, double y, double z) { foreach (var c in Cols) if (c != null && c.Leaf((float)x, (float)y, (float)z) >= 0) return false; return true; }

    // model 0 = HaloCinematic.StepKinematic (flying): facing incl. pitch snaps, speed MoveTowards(top, max(acc, dec)), lateral = thrL * |top| * 0.5
    // model 1 = model 0 with decel when slowing, lateral from max slide with slide accel/decel
    // model 2 = model 1 but horizontal motion only along facing (pitch ignored)
    // model 3 = inertial velocity vector: approaches facing3D * thrF * maxF + left * thrL * maxSlide by accel (speeding) / decel (slowing) per tick
    static void Step(int model, HaloVehicleDef d, HaloRecordedControl c, State s)
    {
        double yaw = c.FacingYawRadians, pitch = c.FacingPitchRadians;
        double fx = Math.Cos(yaw) * Math.Cos(pitch), fy = Math.Sin(yaw) * Math.Cos(pitch), fz = Math.Sin(pitch);
        double hx = Math.Cos(yaw), hy = Math.Sin(yaw), lx = -hy, ly = hx;
        double top = c.ThrottleForward >= 0 ? d.MaxForward * c.ThrottleForward : d.MaxReverse * c.ThrottleForward;
        if (model == 9) { double pk = PK; bool powered = Math.Abs(top) > 1e-6; if (!c.Has(HaloRecordedControl.LookDontTurn)) { s.Yaw = yaw; s.Pitch = pitch; } double h9x = Math.Cos(s.Yaw), h9y = Math.Sin(s.Yaw); s.Speed = Toward(s.Speed, top, Math.Abs(top) > Math.Abs(s.Speed) ? d.SpeedAcceleration : d.SpeedDeceleration); double ts9 = c.ThrottleLeft * (c.ThrottleLeft >= 0 ? d.MaxLeftSlide : d.MaxRightSlide); s.Side = Toward(s.Side, ts9, Math.Abs(ts9) > Math.Abs(s.Side) ? d.SlideAcceleration : d.SlideDeceleration); double tv9 = c.Has(HaloRecordedControl.JumpBit) ? d.MaxLeftSlide * VScale : c.Has(HaloRecordedControl.Crouch) ? -d.MaxRightSlide * VScale : 0; s.VZ = Toward(s.VZ, tv9, Math.Abs(tv9) > Math.Abs(s.VZ) ? d.SlideAcceleration : d.SlideDeceleration); double nx = s.X + h9x * s.Speed - h9y * s.Side, ny = s.Y + h9y * s.Speed + h9x * s.Side; s.X = nx; s.Y = ny; double nz = s.Z + s.VZ + (powered || !PowOnly ? pk * Math.Sin(s.Pitch) * s.Speed : 0); if (Cols == null || SolidAt(s.X, s.Y, s.Z) || !SolidAt(s.X, s.Y, nz)) s.Z = nz; else { s.VZ = 0; Landed++; } return; }
        if (model == 7 || model == 8)
        {
            // M7 = M6 but forward speed moves along the 3D facing (pitch climbs / sinks while moving); M8 = M7 without crouch/jump
            if (!c.Has(HaloRecordedControl.LookDontTurn)) { s.Yaw = yaw; s.Pitch = pitch; }
            double cp = Math.Cos(s.Pitch), sp = Math.Sin(s.Pitch);
            double hx7 = Math.Cos(s.Yaw), hy7 = Math.Sin(s.Yaw), lx7 = -hy7, ly7 = hx7;
            s.Speed = Toward(s.Speed, top, Math.Abs(top) > Math.Abs(s.Speed) ? d.SpeedAcceleration : d.SpeedDeceleration);
            double ts7 = c.ThrottleLeft * (c.ThrottleLeft >= 0 ? d.MaxLeftSlide : d.MaxRightSlide);
            s.Side = Toward(s.Side, ts7, Math.Abs(ts7) > Math.Abs(s.Side) ? d.SlideAcceleration : d.SlideDeceleration);
            double tv7 = model == 8 ? 0 : c.Has(HaloRecordedControl.JumpBit) ? d.MaxLeftSlide * VScale : c.Has(HaloRecordedControl.Crouch) ? -d.MaxRightSlide * VScale : 0;
            s.VZ = Toward(s.VZ, tv7, Math.Abs(tv7) > Math.Abs(s.VZ) ? d.SlideAcceleration : d.SlideDeceleration);
            double nx = s.X + hx7 * cp * s.Speed + lx7 * s.Side, ny = s.Y + hy7 * cp * s.Speed + ly7 * s.Side;
            if (Cols != null && !SolidAt(s.X, s.Y, s.Z) && SolidAt(nx, ny, s.Z)) Blocked++;
            s.X = nx; s.Y = ny;
            double nz = s.Z + s.VZ + sp * s.Speed;
            if (Cols == null || SolidAt(s.X, s.Y, s.Z) || !SolidAt(s.X, s.Y, nz)) s.Z = nz; else { s.VZ = 0; Landed++; }
            return;
        }
        if (model == 6)
        {
            // M6 = HaloVehicle.RecordedControl (workstream 4): level heading (held while LookDontTurn), forward speed accel/decel, its own
            // slide speed, Jump / Crouch at max left / right slide with slide accel / decel; the BSP stops a move (Leaf test as the sweep)
            if (!c.Has(HaloRecordedControl.LookDontTurn)) s.Yaw = yaw;
            double hx6 = Math.Cos(s.Yaw), hy6 = Math.Sin(s.Yaw), lx6 = -hy6, ly6 = hx6;
            s.Speed = Toward(s.Speed, top, Math.Abs(top) > Math.Abs(s.Speed) ? d.SpeedAcceleration : d.SpeedDeceleration);
            double ts6 = c.ThrottleLeft * (c.ThrottleLeft >= 0 ? d.MaxLeftSlide : d.MaxRightSlide);
            s.Side = Toward(s.Side, ts6, Math.Abs(ts6) > Math.Abs(s.Side) ? d.SlideAcceleration : d.SlideDeceleration);
            double vmax6 = VFwd ? d.MaxForward : d.MaxLeftSlide * VScale; double tv6 = c.Has(HaloRecordedControl.JumpBit) ? vmax6 : c.Has(HaloRecordedControl.Crouch) ? -vmax6 : 0;
            s.VZ = Toward(s.VZ, tv6, Math.Abs(tv6) > Math.Abs(s.VZ) ? d.SlideAcceleration : d.SlideDeceleration);
            double nx = s.X + hx6 * s.Speed + lx6 * s.Side, ny = s.Y + hy6 * s.Speed + ly6 * s.Side;
            // like the Unity sweep: a move out of open space into solid is stopped; a hull already outside / in solid moves freely
            // horizontal: counted, not stopped (a point Leaf test cannot tell terrain from the void outside the BSP, which Unity has no collider for)
            if (Cols != null && !SolidAt(s.X, s.Y, s.Z) && SolidAt(nx, ny, s.Z)) Blocked++;
            s.X = nx; s.Y = ny;
            double nz = s.Z + s.VZ;
            if (Cols == null || SolidAt(s.X, s.Y, s.Z) || !SolidAt(s.X, s.Y, nz)) s.Z = nz; else { s.VZ = 0; Landed++; }
            return;
        }
        if (model == 4 || model == 5)
        {
            double acc = Math.Abs(top) > Math.Abs(s.Speed) ? d.SpeedAcceleration : d.SpeedDeceleration;
            s.Speed = Toward(s.Speed, top, acc);
            double tv = c.Has(HaloRecordedControl.JumpBit) ? d.MaxLeftSlide * VScale : c.Has(HaloRecordedControl.Crouch) ? -d.MaxRightSlide * VScale : 0;
            s.VZ = Toward(s.VZ, tv, Math.Abs(tv) > Math.Abs(s.VZ) ? d.SlideAcceleration : d.SlideDeceleration);
            if (model == 4) { s.X += hx * s.Speed; s.Y += hy * s.Speed; s.Z += s.VZ; }
            else { s.X += fx * s.Speed; s.Y += fy * s.Speed; s.Z += fz * s.Speed + s.VZ; }
            return;
        }
        if (model == 0)
        {
            double acc = Math.Max(0.0005 / 3.048, Math.Max(d.SpeedAcceleration, d.SpeedDeceleration));
            s.Speed = Toward(s.Speed, top, acc);
            double lat = c.ThrottleLeft * Math.Abs(top) * 0.5;
            s.X += fx * s.Speed + lx * lat; s.Y += fy * s.Speed + ly * lat; s.Z += fz * s.Speed;
            return;
        }
        if (model == 1 || model == 2)
        {
            double acc = Math.Abs(top) > Math.Abs(s.Speed) ? d.SpeedAcceleration : d.SpeedDeceleration;
            s.Speed = Toward(s.Speed, top, acc);
            double ts = c.ThrottleLeft * (c.ThrottleLeft >= 0 ? d.MaxLeftSlide : d.MaxRightSlide);
            s.Side = Toward(s.Side, ts, Math.Abs(ts) > Math.Abs(s.Side) ? d.SlideAcceleration : d.SlideDeceleration);
            if (model == 1) { s.X += fx * s.Speed + lx * s.Side; s.Y += fy * s.Speed + ly * s.Side; s.Z += fz * s.Speed; }
            else
            {
                // H2: throttle moves along the flat heading; the facing pitch alone drives the climb / sink (even at throttle 0)
                s.X += hx * s.Speed + lx * s.Side; s.Y += hy * s.Speed + ly * s.Side;
                double tv = d.MaxForward * Math.Sin(pitch) * VScale;
                s.VZ = Toward(s.VZ, tv, Math.Abs(tv) > Math.Abs(s.VZ) ? d.SpeedAcceleration : d.SpeedDeceleration);
                s.Z += s.VZ;
            }
            return;
        }
        {
            double ts = c.ThrottleLeft * d.MaxLeftSlide;
            double tx = fx * top + lx * ts, ty = fy * top + ly * ts, tz = fz * top;
            double dx = tx - s.VX, dy = ty - s.VY, dz = tz - s.VZ, dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            double tl = Math.Sqrt(tx * tx + ty * ty + tz * tz), vl = Math.Sqrt(s.VX * s.VX + s.VY * s.VY + s.VZ * s.VZ);
            double acc = tl > vl ? d.SpeedAcceleration : d.SpeedDeceleration;
            if (dl > acc) { dx *= acc / dl; dy *= acc / dl; dz *= acc / dl; }
            s.VX += dx; s.VY += dy; s.VZ += dz;
            s.X += s.VX; s.Y += s.VY; s.Z += s.VZ;
        }
    }

    static double Toward(double v, double t, double a) { return v < t ? Math.Min(t, v + a) : Math.Max(t, v - a); }

    static void Stats(HaloRecording r)
    {
        if (r == null) return;
        var pb = new HaloRecordingPlayback(r);
        int n = 0, side = 0, neg = 0, pitched = 0, crouch = 0, jump = 0; double maxP = 0, minP = 0; var thr = new Dictionary<string, int>();
        do
        {
            var c = pb.Control; n++;
            if (Math.Abs(c.ThrottleLeft) > 0.01) side++;
            if (c.ThrottleForward < -0.01) neg++; if (c.Has(HaloRecordedControl.Crouch)) crouch++; if (c.Has(HaloRecordedControl.JumpBit)) jump++;
            double p = c.FacingPitch * 0.18; if (Math.Abs(p) > 2) pitched++; maxP = Math.Max(maxP, p); minP = Math.Min(minP, p);
            string k = c.ThrottleForward.ToString("0.0") + "/" + c.ThrottleLeft.ToString("0.0");
            int q; thr[k] = thr.TryGetValue(k, out q) ? q + 1 : 1;
            string fk = "F" + c.Flags.ToString("X") + "A" + c.AnimationState; thr[fk] = thr.TryGetValue(fk, out q) ? q + 1 : 1;
        } while (pb.Step());
        Console.WriteLine($"     {r.Name}: {n} ticks, crouch {crouch} jump {jump}, ctlver {r.ControlVersion} flags {r.Initial.Flags:X} anim {r.Initial.AnimationState} aimspd {r.Initial.AimingSpeed}; strafe ticks {side}, reverse {neg}, pitched(>2deg) {pitched} pitch {minP:0}..{maxP:0} deg; aim-facing yaw diff0 {(r.Initial.AimYaw - r.Initial.FacingYaw) * 0.18:0}; throttle f/l: " + string.Join(" ", thr.OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key + "x" + kv.Value)));
    }

    static double Up(List<HaloCollisionBsp> cols, State s) { for (double d = 0; d < 300; d += 0.5) { var t = new State { X = s.X, Y = s.Y, Z = s.Z + d }; if (!Solid(cols, t)) return d; } return -1; }
    static string Fmt(HVec3 v) => $"({v.X:0.0},{v.Y:0.0},{v.Z:0.0})";
    static string Fmt(State s) => $"({s.X:0.0},{s.Y:0.0},{s.Z:0.0})";
    static double Dist(double x, double y, double z, State s) => Math.Sqrt((s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y) + (s.Z - z) * (s.Z - z));
}

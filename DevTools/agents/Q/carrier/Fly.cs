using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

// fly: offline dead-reckoning of every scripted human-plane recording chain on every campaign map with a parameterised flight model,
// measured against ground truth the scenarios carry: (1) the designer's placement of a vehicle whose later chain starts "from
// placement" (the skip / follow-up recording starts where the earlier one ended), (2) the scripts' LZ volumes, (3) the BSP (hull
// touch ticks, hover clearance at the end of a drop).
static class Fly
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    static string Env(string k, string d) { return Environment.GetEnvironmentVariable(k) ?? d; }
    static double EnvD(string k, double d) { var s = Environment.GetEnvironmentVariable(k); return s == null ? d : double.Parse(s, IC); }

    public sealed class Model
    {
        public double S = 1;            // speed scale (diagnostic only)
        public string Vel = "flat";     // flat: scalar forward speed + scalar slide along the hull heading; vec: velocity vector toward target
        public double Turn = 0;         // hull yaw rate limit, rad / tick (0 = hull takes the facing at once)
        public double VUp = 0.1, VAcc = 0.01; // vertical target speed for jump / crouch and its accel
        public bool LookDontTurn = true;
        public Model Clone() { return (Model)MemberwiseClone(); }
        public double KV = 0.05, KVZ = 0.05, AirFV = 0.02, KVD = 0, KL = 0; public bool Ramp = true;
        public string VMode = "slide"; public double GS = 1, AirF = 0.02; public bool PitchNeg = false;
        public double A = 1, YawK = 0; // A: accel scale; YawK: first-order hull yaw lag factor per tick (0 = none)
        public double AccS = 1, DecS = 1; public double WA = 0, WD = 0; // second-order hull yaw: angular velocity w += WA * error, w *= 1 - WD, yaw += w (WA > 0)
        public double PitchK = 0;      // climb / sink = PitchK * sin(facing pitch) * forward speed (1 = speed along the 3D facing)
        public bool CosP = false;      // horizontal forward speed scaled by cos(pitch)
        public override string ToString() => $"{Vel} S{S:0.###} turn{Turn:0.####} vup{VUp:0.###}/{VAcc:0.###} pitchK{PitchK}{(PitchNeg ? "neg" : "")} cosp{CosP} A{A} yawK{YawK} vmode {VMode} gs{GS} airf{AirF} kv{KV}/{KVZ} ramp{Ramp}";
    }

    public sealed class St { public double x, y, z, yaw, pitch, speed, side, vert, vx, vy, w; }

    static double Toward(double v, double t, double a) { if (a <= 0) return t; return v < t ? Math.Min(t, v + a) : Math.Max(t, v - a); }
    static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

    public static void Step(Model m, HaloVehicleDef d, HaloRecordedControl c, St s)
    {
        if (m.Vel == "game")
        {
            // the game's own model (Data/Vehicles/HaloVehicleDef.cs HaloPlaneFlight), bit for bit
            var ps = new HaloPlaneState { Yaw = (float)s.yaw, Speed = (float)s.speed, Side = (float)s.side, Vx = (float)s.vx, Vy = (float)s.vy, Vz = (float)s.vert };
            HaloPlaneFlight.Step(d, ref ps, ref c);
            s.yaw = ps.Yaw; s.speed = ps.Speed; s.side = ps.Side; s.vx = ps.Vx; s.vy = ps.Vy; s.vert = ps.Vz;
            s.x += s.vx; s.y += s.vy; s.z += s.vert;
            return;
        }
        double want = c.FacingYawRadians;
        if (!(m.LookDontTurn && c.Has(HaloRecordedControl.LookDontTurn)))
        {
            s.pitch = c.FacingPitchRadians;
            if (m.WA > 0) { s.w = (s.w + Wrap(want - s.yaw) * m.WA) * (1 - m.WD); s.yaw = Wrap(s.yaw + s.w); }
            else if (m.YawK > 0) s.yaw = Wrap(s.yaw + Wrap(want - s.yaw) * m.YawK);
            else if (m.Turn <= 0) s.yaw = want;
            else { double dy = Wrap(want - s.yaw); s.yaw = Wrap(s.yaw + Math.Max(-m.Turn, Math.Min(m.Turn, dy))); }
        }
        double top = (c.ThrottleForward >= 0 ? d.MaxForward : d.MaxReverse) * c.ThrottleForward * m.S;
        double st = c.ThrottleLeft * (c.ThrottleLeft >= 0 ? d.MaxLeftSlide : d.MaxRightSlide) * m.S;
        double vt = c.Has(HaloRecordedControl.JumpBit) ? m.VUp : c.Has(HaloRecordedControl.Crouch) ? -m.VUp : 0;
        if (m.Vel == "phys")
        {
            // physical: the control speed / slide ramp per the tag (accel / decel); the hull velocity responds first-order (KV) toward
            // the target velocity (along the 3D facing when PitchK = 1); air friction damps every tick; crouch / jump = gravity off / doubled
            const double g = 0.0035651791;
            if (m.Ramp) { s.speed = Toward(s.speed, top, Math.Abs(top) > Math.Abs(s.speed) ? d.SpeedAcceleration * m.AccS : d.SpeedDeceleration * m.DecS); s.side = Toward(s.side, st, Math.Abs(st) > Math.Abs(s.side) ? d.SlideAcceleration * m.AccS : d.SlideDeceleration * m.DecS); }
            else { s.speed = top; s.side = st; }
            double hx3 = Math.Cos(s.yaw), hy3 = Math.Sin(s.yaw), cp3 = m.PitchK > 0 ? Math.Cos(s.pitch) : 1, sp3 = Math.Sin(s.pitch) * m.PitchK;
            double tx = hx3 * cp3 * s.speed - hy3 * s.side, ty = hy3 * cp3 * s.speed + hx3 * s.side, tz = sp3 * s.speed;
            double kh = m.KVD > 0 && tx * tx + ty * ty < s.vx * s.vx + s.vy * s.vy ? m.KVD : m.KV;
            if (m.KL > 0) { double vf = s.vx * hx3 + s.vy * hy3, vl = -s.vx * hy3 + s.vy * hx3, tf = tx * hx3 + ty * hy3, tl = -tx * hy3 + ty * hx3; vf += (tf - vf) * kh; vl += (tl - vl) * m.KL; s.vx = vf * hx3 - vl * hy3; s.vy = vf * hy3 + vl * hx3; }
            else { s.vx += (tx - s.vx) * kh; s.vy += (ty - s.vy) * kh; }
            if (m.VMode == "tgt") { if (c.Has(HaloRecordedControl.JumpBit)) tz += d.MaxForward * m.VUp; else if (c.Has(HaloRecordedControl.Crouch)) tz -= d.MaxForward * m.VUp; s.vert += (tz - s.vert) * m.KVZ; }
            else if (c.Has(HaloRecordedControl.JumpBit)) s.vert += g * m.GS; else if (c.Has(HaloRecordedControl.Crouch)) s.vert -= g * m.GS; else s.vert += (tz - s.vert) * m.KVZ;
            double k = 1 - m.AirF; s.vx *= k; s.vy *= k; s.vert *= 1 - m.AirFV;
            s.x += s.vx; s.y += s.vy; s.z += s.vert;
            return;
        }
        if (m.VMode == "grav")
        {
            // crouch: lift off, the hull falls (gravity, air friction); jump: lift beyond gravity; neither: lift = gravity, vertical drifts and damps
            const double g = 0.0035651791;
            if (c.Has(HaloRecordedControl.JumpBit)) s.vert += g * m.GS; else if (c.Has(HaloRecordedControl.Crouch)) s.vert -= g * m.GS;
            s.vert *= 1 - m.AirF;
        }
        else s.vert = Toward(s.vert, vt, m.VAcc);
        double hx = Math.Cos(s.yaw), hy = Math.Sin(s.yaw);
        if (m.Vel == "vec")
        {
            double tx = hx * top - hy * st, ty = hy * top + hx * st;
            double dx = tx - s.vx, dy = ty - s.vy, dl = Math.Sqrt(dx * dx + dy * dy);
            double acc = Math.Sqrt(tx * tx + ty * ty) > Math.Sqrt(s.vx * s.vx + s.vy * s.vy) + 1e-9 ? d.SpeedAcceleration * m.A : d.SpeedDeceleration * m.A;
            if (dl > acc) { dx *= acc / dl; dy *= acc / dl; }
            s.vx += dx; s.vy += dy;
        }
        else
        {
            s.speed = Toward(s.speed, top, Math.Abs(top) > Math.Abs(s.speed) ? d.SpeedAcceleration * m.A : d.SpeedDeceleration * m.A);
            s.side = Toward(s.side, st, Math.Abs(st) > Math.Abs(s.side) ? d.SlideAcceleration * m.A : d.SlideDeceleration * m.A);
            s.vx = hx * s.speed - hy * s.side; s.vy = hy * s.speed + hx * s.side;
        }
        double sp = Math.Sin(s.pitch), cp = m.CosP ? Math.Cos(s.pitch) : 1;
        double fwd = m.Vel == "vec" ? (s.vx * hx + s.vy * hy) : s.speed;
        double pz = m.PitchK * sp * fwd; if (m.PitchNeg && pz > 0) pz = 0;
        s.x += s.vx * cp; s.y += s.vy * cp; s.z += s.vert + pz;
    }

    public sealed class Flight
    {
        public string Map, Obj, Vehicle, Script, From; public HaloVehicleDef Def; public HVec3 Start; public double Yaw0;
        public List<(string kind, string rec)> Recs; public List<HaloRecording> R = new List<HaloRecording>();
        public HVec3? Target; public string TargetName;   // placement of the follow-up chain
    }

    static readonly Dictionary<string, HaloScenario> Scen = new Dictionary<string, HaloScenario>();
    public static List<Flight> Flights(string mn)
    {
        var m = new HaloMap(Maps + mn + ".map"); var sc = new HaloScenario(m); var cd = new HaloCutsceneData(m.Scenario);
        Scen[mn] = sc;
        var res = new List<Flight>();
        var chains = Carrier.Chains(mn);
        var seen = new HashSet<string>();
        for (int ci = 0; ci < chains.Count; ci++)
        {
            var ch = chains[ci];
            int ni = sc.ObjectNames.FindIndex(n => string.Equals(n, ch.Obj, StringComparison.OrdinalIgnoreCase));
            var veh = sc.Vehicles.FirstOrDefault(p => p.NameIndex == ni && ni >= 0);
            if (veh == null || veh.Type == null) continue;
            var def = new HaloVehicleDef(veh.Type);
            if (def.Type != HaloVehicleDef.Kind.HumanPlane) continue;
            var f = new Flight { Map = mn, Obj = ch.Obj, Vehicle = def.Name, Def = def, Script = ch.Script.Trim(), Recs = ch.Recs };
            if (ch.Flag != null) { var fl = cd.FindFlag(ch.Flag); if (fl == null) continue; f.Start = fl.Position; f.Yaw0 = fl.Yaw; f.From = ch.Flag; }
            else { f.Start = veh.Position; f.Yaw0 = veh.Rotation.X; f.From = "placement"; }
            string key = f.Obj + "|" + f.From + "|" + string.Join(",", ch.Recs.Select(r => r.rec));
            if (!seen.Add(key)) continue;
            foreach (var r in ch.Recs) f.R.Add(cd.Recording(r.rec));
            if (f.R.Any(r => r == null)) continue;
            if (ch.Flag != null)
            {
                // a later chain of the same object from its placement: the designer put the placement where this chain ends
                var follow = chains.FirstOrDefault(o => o != ch && o.Obj == ch.Obj && o.Flag == null);
                if (follow != null) { f.Target = veh.Position; f.TargetName = "placement(" + follow.Recs[0].rec + ")"; }
            }
            res.Add(f);
        }
        return res;
    }

    public static St Run(Model m, Flight f, int recCount, Action<int, St, HaloRecordedControl> each = null)
    {
        var s = new St { x = f.Start.X, y = f.Start.Y, z = f.Start.Z, yaw = f.Yaw0 };
        for (int k = 0; k < recCount && k < f.R.Count; k++)
        {
            s.speed = s.side = s.vert = s.vx = s.vy = s.w = 0;
            var pb = new HaloRecordingPlayback(f.R[k]);
            int n = 0;
            do { Step(m, f.Def, pb.Control, s); each?.Invoke(n++, s, pb.Control); } while (pb.Step());
        }
        return s;
    }

    public static int Run(string[] a)
    {
        string cmd = a.Length > 1 ? a[1] : "place";
        string[] maps = (a.Length > 2 ? a[2] : "a10,a30,a50,b30,b40,c10,c20,c40,d20,d40").Split(',');
        var m = new Model { S = EnvD("S", 1), Vel = Env("VEL", "flat"), Turn = EnvD("TURN", 0), VUp = EnvD("VUP", 0.1), VAcc = EnvD("VACC", 0.01), A = EnvD("A", 1), YawK = EnvD("YAWK", 0), VMode = Env("VMODE", "slide"), GS = EnvD("GS", 1), AirF = EnvD("AIRF", 0.02), PitchNeg = Env("PITCHNEG", "0") == "1", KV = EnvD("KV", 0.05), AirFV = EnvD("AIRFV", 0.02), KVZ = EnvD("KVZ", EnvD("KV", 0.05)), Ramp = Env("RAMP", "1") == "1", PitchK = EnvD("PITCHK", 0), CosP = Env("COSP", "0") == "1", KVD = EnvD("KVD", 0), KL = EnvD("KL", 0), WA = EnvD("WA", 0), WD = EnvD("WD", 0) };
        if (cmd == "eval") return Eval.Run(m, maps);
        if (cmd == "bests") return BestS.Run(m, maps);
        if (cmd == "vert") return Vert.Run(m, maps);
        if (cmd == "grid") return Grid.Run(m, maps, a.Length > 3 ? a[3] : "");
        if (cmd == "scan") return Scan.Run(m, maps, a[3].Split(' '));
        if (cmd == "list")
        {
            foreach (var mn in maps) foreach (var f in Flights(mn))
                    Console.WriteLine($"{mn} {f.Obj} ({f.Vehicle}) {f.Script} from {f.From} {f.Start} : {string.Join(" -> ", f.Recs.Select(r => r.kind + " " + r.rec + "[" + f.R[f.Recs.IndexOf(r)].Length + "]"))}" + (f.Target != null ? $"  TARGET {f.TargetName} {f.Target}" : ""));
            return 0;
        }
        if (cmd == "place")
        {
            // ground truth 1: end of a flag chain's first recording vs the vehicle's placement
            Console.WriteLine("model " + m);
            foreach (var mn in maps) foreach (var f in Flights(mn))
                {
                    if (f.Target == null) continue;
                    var t = f.Target.Value;
                    for (int k = 1; k <= f.R.Count; k++)
                    {
                        var e = Run(m, f, k);
                        double dh = Math.Sqrt((e.x - t.X) * (e.x - t.X) + (e.y - t.Y) * (e.y - t.Y));
                        // best speed scale for this one
                        double bestS = 0, bestD = 1e9;
                        for (double sc = 0.3; sc <= 1.4; sc += 0.005)
                        {
                            var m2 = new Model { S = sc, Vel = m.Vel, Turn = m.Turn, VUp = m.VUp, VAcc = m.VAcc, PitchK = m.PitchK, CosP = m.CosP, A = m.A, YawK = m.YawK };
                            var e2 = Run(m2, f, k); double d2 = Math.Sqrt((e2.x - t.X) * (e2.x - t.X) + (e2.y - t.Y) * (e2.y - t.Y));
                            if (d2 < bestD) { bestD = d2; bestS = sc; }
                        }
                        double travel = Math.Sqrt((f.Start.X - t.X) * (f.Start.X - t.X) + (f.Start.Y - t.Y) * (f.Start.Y - t.Y));
                        Console.WriteLine($"{mn} {f.Obj} {string.Join("+", f.Recs.Take(k).Select(r => r.rec))}: end ({e.x:0.0},{e.y:0.0},{e.z:0.0}) target {f.TargetName} {t} start->target {travel:0.0}  miss {dh:0.0} dz {e.z - t.Z:0.0} | best S {bestS:0.000} miss {bestD:0.0}");
                    }
                }
            return 0;
        }
        return 1;
    }
}
static class FlyTrace
{
    public static int Run(string[] a)
    {
        string mn = a[1], obj = a[2]; int idx = a.Length > 3 ? int.Parse(a[3]) : 0; int every = a.Length > 4 ? int.Parse(a[4]) : 15;
        foreach (var f in Fly.Flights(mn))
        {
            if (f.Obj != obj || (Environment.GetEnvironmentVariable("FROM") != null && f.From != Environment.GetEnvironmentVariable("FROM"))) continue;
            var r = f.R[idx]; var pb = new HaloRecordingPlayback(r); int n = 0;
            Console.WriteLine($"{f.Obj} {r.Name} len {r.Length} from {f.From} {f.Start} yaw0 {f.Yaw0 * 57.3:0} initial facing {r.Initial.FacingYaw * 0.18:0}/{r.Initial.FacingPitch * 0.18:0}");
            string last = "";
            do
            {
                var c = pb.Control;
                string s = $"thr {c.ThrottleForward:0.##}/{c.ThrottleLeft:0.##} fl {c.Flags:X} yaw {c.FacingYaw * 0.18:0} pitch {c.FacingPitch * 0.18:0} aim {c.AimYaw * 0.18:0}/{c.AimPitch * 0.18:0}";
                if (n % every == 0 || s.Substring(0, 16) != (last.Length >= 16 ? last.Substring(0, 16) : last)) Console.WriteLine($"  t{n} {s}");
                last = s; n++;
            } while (pb.Step());
            break;
        }
        return 0;
    }
}

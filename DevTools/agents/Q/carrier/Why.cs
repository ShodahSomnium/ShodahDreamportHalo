using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

// why <map> <obj> <volume> [every]: one flag chain's first recording flown with the game model (no collision), sampled, and the end's
// cargo-marker offset from the volume: along / across the hull heading and along / across the start->end chord.
static class Why
{
    public static int Run(string[] a)
    {
        string mn = a[1], obj = a[2], vn = a[3]; int every = a.Length > 4 ? int.Parse(a[4]) : 60;
        var m = new Fly.Model { Vel = "game" };
        var f = Fly.Flights(mn).First(x => x.Obj == obj && x.From != "placement");
        var map = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\" + mn + ".map");
        var md = new HaloMissionData(new HaloScenario(map));
        var v = md.TriggerVolumes[md.TriggerVolumeIndex(vn)]; var cc = v.Center;
        Console.WriteLine($"{mn} {obj} {f.R[0].Name} len {f.R[0].Length} start {f.Start} yaw0 {f.Yaw0 * 57.2958:0.0}");
        Console.WriteLine($"volume {vn} corner {v.Corner} fwd {v.Forward} up {v.Up} extent {v.Extent} centre {cc}");
        double path = 0, px = f.Start.X, py = f.Start.Y;
        var s = Fly.Run(m, f, 1, (n, st, c) =>
        {
            path += Math.Sqrt((st.x - px) * (st.x - px) + (st.y - py) * (st.y - py)); px = st.x; py = st.y;
            if (n % every == 0) Console.WriteLine($"  t{n} ({st.x:0.0},{st.y:0.0},{st.z:0.0}) hull {st.yaw * 57.2958:0} facing {c.FacingYawRadians * 57.2958:0} v {Math.Sqrt(st.vx * st.vx + st.vy * st.vy):0.000} thr {c.ThrottleForward}/{c.ThrottleLeft} fl {c.Flags:X}");
        });
        double cx = s.x + Math.Cos(s.yaw) * -1.662, cy = s.y + Math.Sin(s.yaw) * -1.662;
        double dx = cc.X - cx, dy = cc.Y - cy, hx = Math.Cos(s.yaw), hy = Math.Sin(s.yaw);
        double chx = s.x - f.Start.X, chy = s.y - f.Start.Y, chl = Math.Sqrt(chx * chx + chy * chy); chx /= chl; chy /= chl;
        Console.WriteLine($"end ({s.x:0.00},{s.y:0.00},{s.z:0.00}) hull {s.yaw * 57.2958:0.0} cargo ({cx:0.00},{cy:0.00}) path {path:0.0} chord {chl:0.0}");
        Console.WriteLine($"centre - cargo ({dx:0.00},{dy:0.00}) = {Math.Sqrt(dx * dx + dy * dy):0.00}: hull fwd {dx * hx + dy * hy:0.00} left {-dx * hy + dy * hx:0.00}; chord along {dx * chx + dy * chy:0.00} across {-dx * chy + dy * chx:0.00}; inside {v.Contains((float)cx, (float)cy, cc.Z)}");
        return 0;
    }
}

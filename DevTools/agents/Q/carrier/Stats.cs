using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

// stats [maps]: per flag-chain recording (human planes) - ticks, LookDontTurn ticks, total facing turn, fastest facing rate, the game model's
// largest / mean hull lag behind the facing, and the throttle-off ticks while the facing turns > 1 deg/tick (coasting turns).
static class Stats
{
    public static int Run(string[] a)
    {
        foreach (var mn in (a.Length > 1 ? a[1] : "a30,b30").Split(','))
            foreach (var f in Fly.Flights(mn))
            {
                if (f.From == "placement") continue;
                for (int k = 0; k < f.R.Count; k++)
                {
                    if (f.Recs[k].kind == "rp_and_delete") break;
                    var pb = new HaloRecordingPlayback(f.R[k]); var s = new HaloPlaneState { Yaw = (float)f.Yaw0 };
                    int n = 0, ldt = 0, coastTurn = 0; double turn = 0, maxRate = 0, maxLag = 0, sumLag = 0, last = double.NaN;
                    do
                    {
                        var c = pb.Control; HaloPlaneFlight.Step(f.Def, ref s, ref c);
                        if (c.Has(HaloRecordedControl.LookDontTurn)) ldt++;
                        double fy = c.FacingYawRadians;
                        if (!double.IsNaN(last)) { double dd = Math.Abs(HaloPlaneFlight.WrapAngle((float)(fy - last))); turn += dd; maxRate = Math.Max(maxRate, dd); if (c.ThrottleForward == 0 && dd > 1 / 57.3) coastTurn++; }
                        last = fy;
                        double lag = Math.Abs(HaloPlaneFlight.WrapAngle((float)(fy - s.Yaw))); maxLag = Math.Max(maxLag, lag); sumLag += lag; n++;
                    } while (pb.Step());
                    Console.WriteLine($"{mn} {f.Obj,-22} {f.Vehicle,-10} {f.R[k].Name,-28} ticks {n,5} LookDontTurn {ldt,4} facing turn {turn * 57.3,6:0} deg  max {maxRate * 57.3,4:0.0} deg/tick  hull lag max {maxLag * 57.3,4:0} mean {sumLag / n * 57.3,4:0.0}  coasting-turn ticks {coastTurn,4}");
                }
            }
        return 0;
    }
}

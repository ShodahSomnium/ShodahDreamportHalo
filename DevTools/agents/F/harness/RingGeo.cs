using System;
static partial class F
{
    public static int RingGeo(string[] a)
    {
        double S = 40, R = 19.05 * S, u = 3.048;
        double cx = 12.3 * S, cy = -1.8 * S, cz = 15.5 * S;
        double yaw = -0.419, roll = -0.175;
        // ring plane spanned by model X and model Z (the ring's axis is model Y); apply roll about X, then yaw about Z
        double minD = 1e9, maxEl = -90, minEl = 90; int n = 0, overhead = 0;
        for (int i = 0; i < 360; i++)
        {
            double t = i * Math.PI / 180, mx = R * Math.Cos(t), my = 0, mz = R * Math.Sin(t);
            double y1 = my * Math.Cos(roll) - mz * Math.Sin(roll), z1 = my * Math.Sin(roll) + mz * Math.Cos(roll), x1 = mx;
            double x2 = x1 * Math.Cos(yaw) - y1 * Math.Sin(yaw), y2 = x1 * Math.Sin(yaw) + y1 * Math.Cos(yaw);
            double px = cx + x2, py = cy + y2, pz = cz + z1;
            double d = Math.Sqrt(px * px + py * py + pz * pz) * u, el = Math.Atan2(pz, Math.Sqrt(px * px + py * py)) * 180 / Math.PI;
            minD = Math.Min(minD, d); maxEl = Math.Max(maxEl, el); minEl = Math.Min(minEl, el); if (el > 60) overhead++;
            if (i % 30 == 0) Console.WriteLine($"{i,3} deg: {d:0} m, elevation {el:0}, azimuth {Math.Atan2(py, px) * 180 / Math.PI:0}");
        }
        Console.WriteLine($"nearest {minD:0} m, elevation {minEl:0}..{maxEl:0}, points above 60 deg: {overhead}");
        return 0;
    }
}

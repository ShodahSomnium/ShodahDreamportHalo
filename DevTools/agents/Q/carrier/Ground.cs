using System;
using System.Globalization;
// ground <map> x0 y0 x1 y1 [z]: BSP floor height (first solid below z) at 11 points from (x0,y0) to (x1,y1)
static class Ground
{
    public static int Run(string[] a)
    {
        var ic = CultureInfo.InvariantCulture;
        Eval.Rows(new Fly.Model { Vel = "game" }, new[] { a[1] }, false);
        double x0 = double.Parse(a[2], ic), y0 = double.Parse(a[3], ic), x1 = double.Parse(a[4], ic), y1 = double.Parse(a[5], ic), z = a.Length > 6 ? double.Parse(a[6], ic) : 120;
        for (int i = 0; i <= 10; i++) { double x = x0 + (x1 - x0) * i / 10, y = y0 + (y1 - y0) * i / 10; double c = Eval.Gnd(x, y, z); Console.WriteLine($"({x:0.0},{y:0.0}) floor {(c < 0 ? "?" : (z - c).ToString("0.00"))}"); }
        return 0;
    }
}

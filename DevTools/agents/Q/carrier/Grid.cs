using System;
using System.Linq;
using System.Globalization;

// grid <maps> "<base cfg>": 2-D grid over GX / GY (env "NAME:from:to:step", names as in scan cfgs; default YAWK x KV) on top of the base
// cfg; prints the configs with the most volumes inside (jeep volumes counted) and those with the smallest worst jeep miss.
static class Grid
{
    static readonly CultureInfo IC = CultureInfo.InvariantCulture;
    public static int Run(Fly.Model b, string[] maps, string cfg)
    {
        var gx = (Environment.GetEnvironmentVariable("GX") ?? "YAWK:0.02:0.1:0.0025").Split(':');
        var gy = (Environment.GetEnvironmentVariable("GY") ?? "KV:0.03:0.12:0.005").Split(':');
        var res = new System.Collections.Generic.List<(int ins, double sum, double maxJ, string desc)>();
        for (double x = double.Parse(gx[1], IC); x <= double.Parse(gx[2], IC) + 1e-9; x += double.Parse(gx[3], IC))
            for (double y = double.Parse(gy[1], IC); y <= double.Parse(gy[2], IC) + 1e-9; y += double.Parse(gy[3], IC))
            {
                var m = Scan.Parse(b, cfg + "/" + gx[0] + "=" + x.ToString(IC) + "/" + gy[0] + "=" + y.ToString(IC));
                var rows = Eval.Rows(m, maps, Environment.GetEnvironmentVariable("NOCOLL") != "1");
                int ins = 0; double sum = 0, mj = 0; string d = "";
                foreach (var r in rows)
                {
                    if (!r.Truth) continue; sum += r.Miss;
                    if (r.Inside == true) ins++;
                    if (r.Name.Contains("foehammer_first") || r.Name.Contains("hog_pelican")) mj = Math.Max(mj, r.Miss);
                    d += r.Name.Split(' ')[1] + " " + r.Miss.ToString("0.0", IC) + (r.Inside == true ? "*" : "") + " ";
                }
                res.Add((ins, sum, mj, $"{gx[0]} {x:0.#####} {gy[0]} {y:0.#####} inside {ins} sum {sum:0.0} jeepmax {mj:0.0} | {d}"));
            }
        foreach (var r in res.OrderByDescending(r => r.ins).ThenBy(r => r.sum).Take(6)) Console.WriteLine(r.desc);
        Console.WriteLine("-- best jeep max");
        foreach (var r in res.OrderBy(r => r.maxJ).Take(6)) Console.WriteLine(r.desc);
        return 0;
    }
}

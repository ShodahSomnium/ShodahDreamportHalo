using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static double Ang(float[] a, int ai, float[] b, int bi)
    {
        double d = Math.Abs(a[ai] * b[bi] + a[ai + 1] * b[bi + 1] + a[ai + 2] * b[bi + 2] + a[ai + 3] * b[bi + 3]);
        return 2 * Math.Acos(Math.Min(1.0, d)) * 180 / Math.PI;
    }

    static int Twin(string[] a)
    {
        int total = 0, ok = 0, bad = 0, twins = 0;
        double worstJump = 0, worstTwin = 0, worstTr = 0;
        foreach (var mn in Camp)
        {
            var m = new HaloMap(Maps + mn + ".map");
            var graphs = m.OfClass("antr").Select(t => new HaloAnimGraph(t)).ToList();
            foreach (var g in graphs)
            foreach (var an in g.Animations)
            {
                if (!an.Compressed) continue;
                total++;
                if (!an.Decode()) { bad++; Console.WriteLine($"  FAIL {mn} {g.Tag.Name} '{an.Name}': {an.Error}"); continue; }
                ok++;
                int n = an.NodeCount, f = Math.Max(1, an.Frames);
                double jump = 0, unit = 0;
                for (int fr = 0; fr < f; fr++)
                    for (int k = 0; k < n; k++)
                    {
                        int i = (fr * n + k) * 4;
                        double len = Math.Sqrt(an.Rot[i] * an.Rot[i] + an.Rot[i + 1] * an.Rot[i + 1] + an.Rot[i + 2] * an.Rot[i + 2] + an.Rot[i + 3] * an.Rot[i + 3]);
                        unit = Math.Max(unit, Math.Abs(len - 1));
                        if (fr > 0) jump = Math.Max(jump, Ang(an.Rot, i, an.Rot, ((fr - 1) * n + k) * 4));
                    }
                worstJump = Math.Max(worstJump, jump);
                // twin: an uncompressed animation of the same name, node count and frames in this map
                HaloAnimation tw = null;
                foreach (var g2 in graphs) { var c = g2.Get(an.Name); if (c != null && !c.Compressed && c.NodeCount == n && c.Frames == an.Frames && c.Decode() && g2.NodeNames.SequenceEqual(g.NodeNames)) { tw = c; break; } }
                string ts = "";
                if (tw != null)
                {
                    twins++;
                    double e = 0, et = 0;
                    for (int fr = 0; fr < f; fr++) for (int k = 0; k < n; k++)
                    {
                        if (an.Rotates(k) || tw.Rotates(k)) e = Math.Max(e, Ang(an.Rot, (fr * n + k) * 4, tw.Rot, (fr * n + k) * 4));
                        for (int c = 0; c < 3; c++) et = Math.Max(et, Math.Abs(an.Trans[(fr * n + k) * 3 + c] - tw.Trans[(fr * n + k) * 3 + c]) * 304.8);
                    }
                    worstTwin = Math.Max(worstTwin, e); worstTr = Math.Max(worstTr, et);
                    ts = $" twin max {e:0.00} deg / {et:0.00} cm";
                }
                if (a.Length > 1 || tw != null || jump > 20) Console.WriteLine($"  {mn} {g.Tag.Name} '{an.Name}' {f}f: max frame-to-frame {jump:0.0} deg, |q|-1 {unit:0.0000}{ts}");
            }
        }
        Console.WriteLine($"compressed {total}: decoded {ok}, failed {bad}; twins {twins}: worst {worstTwin:0.00} deg, {worstTr:0.00} cm; worst frame-to-frame turn {worstJump:0.0} deg");
        return 0;
    }
}

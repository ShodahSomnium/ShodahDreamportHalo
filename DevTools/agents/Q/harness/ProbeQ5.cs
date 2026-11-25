using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    // all maps: parse with the module's own reader
    public static int QRec(string[] a)
    {
        int recs = 0, errs = 0, endOk = 0, titles = 0, cams = 0, flags = 0;
        foreach (var mn in new[] { "a10", "a30", "a50", "b30", "b40", "c10", "c20", "c40", "d20", "d40" })
        {
            var m = new HaloMap(QMaps + mn + ".map");
            var d = new HaloCutsceneData(m.Scenario);
            int e = 0, ok = 0;
            foreach (var r in d.Recordings) { if (r.Error != null) { e++; Console.WriteLine($"  {mn} {r}"); } if (r.EndTick == r.Length - 1) ok++; }
            recs += d.Recordings.Count; errs += e; endOk += ok; titles += d.Titles.Count; cams += d.CameraPoints.Count; flags += d.Flags.Count;
            Console.WriteLine($"{mn}: {d.CameraPoints.Count} camera points, {d.Flags.Count} flags, {d.Titles.Count} titles, {d.Recordings.Count} recordings ({e} errors, end tick = length-1 in {ok})");
            foreach (var t in d.Titles) Console.WriteLine("    title " + t);
        }
        Console.WriteLine($"TOTAL recordings {recs} errors {errs} endOk {endOk}; titles {titles} cams {cams} flags {flags}");
        return 0;
    }

    // dead-reckon a recording from a cutscene flag over a BSP's collision; draw it top-down
    public static int QPath(string[] a)
    {
        string mn = a[1], recName = a[2], flagName = a[3]; int bsp = int.Parse(a[4]); string objName = a.Length > 5 ? a[5] : null; string png = a.Length > 6 ? a[6] : null;
        var m = new HaloMap(QMaps + mn + ".map");
        var sc = new HaloScenario(m);
        var d = new HaloCutsceneData(m.Scenario);
        var rec = d.Recording(recName); var flag = d.FindFlag(flagName);
        if (rec == null || flag == null) { Console.WriteLine("missing recording or flag"); return 1; }
        HaloCollisionBsp col = null;
        foreach (var cb in sc.BspRoot(bsp).Block(176, 96)) { col = new HaloCollisionBsp(cb); break; }
        // biped speed: the named biped's animation graph "move-front"
        float speed = 0.05f; string how = "default"; HaloTag bipdTag = null;
        if (objName != null)
        {
            int nameIdx = sc.ObjectNames.FindIndex(n => string.Equals(n, objName, StringComparison.OrdinalIgnoreCase));
            var root = m.Scenario.Root;
            var pal = root.Block(564, 48).ToList();
            foreach (var b in root.Block(552, 120)) if (b.I16(2) == nameIdx && nameIdx >= 0)
            {
                var bipd = pal[b.I16(0)].Ref(0); bipdTag = bipd;
                var g = new HaloAnimGraph(bipd.Root.Ref(56));
                string how2; float sp2 = QUnitSpeed(bipd, Environment.GetEnvironmentVariable("QSEAT") ?? "stand", (b.I8(36) >= 0 || true) ? "unarmed" : "rifle", out how2); if (sp2 > 0) { speed = sp2; how = how2; } else how = how2; if (Environment.GetEnvironmentVariable("QSPEED") != null) { speed = float.Parse(Environment.GetEnvironmentVariable("QSPEED"), System.Globalization.CultureInfo.InvariantCulture); how = "QSPEED"; }
                foreach (var an in new HaloAnimation[0])
                {
                    an.Decode();
                    float sx = 0, sy = 0; if (an.Info != null) for (int f = 0; f < an.Frames; f++) { sx += an.Info[f * 4]; sy += an.Info[f * 4 + 1]; }
                    speed = (float)Math.Sqrt(sx * sx + sy * sy) / Math.Max(1, an.Frames); how = bipd.Name + " '" + an.Name + "' " + an.Frames + " frames";
                    break;
                }
            }
        }
        Console.WriteLine($"{rec}  from {flag}  bsp {bsp}  speed {speed:0.####} wu/tick ({speed * 30 * 3.048f:0.##} m/s) from {how}");
        var pb = new HaloRecordingPlayback(rec);
        double x = flag.Position.X, y = flag.Position.Y, z = flag.Position.Z;
        var pts = new List<double[]>(); pts.Add(new[] { x, y });
        int open = 0, floor = 0, n = 0;
        do
        {
            var c = pb.Control;
            if (bipdTag != null) speed = HaloRecordingSpeed.WorldUnitsPerTick(bipdTag, c.Has(HaloRecordedControl.ForceAlert), c.Has(HaloRecordedControl.Crouch), c.Weapon < 0 ? "unarmed" : "rifle");
            double yaw = c.FacingYawRadians;
            double fx = Math.Cos(yaw), fy = Math.Sin(yaw), lx = -fy, ly = fx;
            x += (fx * c.ThrottleForward + lx * c.ThrottleLeft) * speed;
            y += (fy * c.ThrottleForward + ly * c.ThrottleLeft) * speed;
            pts.Add(new[] { x, y });
            n++;
            if (col.Leaf((float)x, (float)y, (float)z + 0.25f) >= 0) open++;
            if (col.Leaf((float)x, (float)y, (float)z - 0.1f) < 0) floor++;
        } while (pb.Step());
        Console.WriteLine($"ticks {n}: end ({x:0.###},{y:0.###}) moved {Math.Sqrt((x - flag.Position.X) * (x - flag.Position.X) + (y - flag.Position.Y) * (y - flag.Position.Y)):0.##} wu; body point open {open}/{n}, solid floor under {floor}/{n}; end facing {pb.Control.FacingYaw * 0.18f:0.#} deg");
        if (png != null) Draw(col, pts, z, png);
        return 0;
    }

    static void Draw(HaloCollisionBsp col, List<double[]> pts, double z, string path)
    {
        double minX = pts.Min(p => p[0]) - 2, maxX = pts.Max(p => p[0]) + 2, minY = pts.Min(p => p[1]) - 2, maxY = pts.Max(p => p[1]) + 2;
        double s = 600 / Math.Max(maxX - minX, maxY - minY);
        int w = (int)((maxX - minX) * s), h = (int)((maxY - minY) * s);
        var px = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++) { px[i * 4] = 20; px[i * 4 + 1] = 20; px[i * 4 + 2] = 30; px[i * 4 + 3] = 255; }
        // sample: walkable where the body point is open and the floor is solid
        for (int py = 0; py < h; py++) for (int pxx = 0; pxx < w; pxx++)
        {
            double hx = minX + pxx / s, hy = maxY - py / s;
            int i = (py * w + pxx) * 4;
            double top = z + 0.6; if (col.Leaf((float)hx, (float)hy, (float)top) < 0) continue;
            double fz = top; while (fz > z - 2 && col.Leaf((float)hx, (float)hy, (float)fz) >= 0) fz -= 0.02;
            if (fz <= z - 2) { px[i] = 40; px[i + 1] = 40; px[i + 2] = 80; continue; }
            double t = Math.Max(0, Math.Min(1, (fz - (z - 1)) / 1.6)); px[i] = (byte)(60 + 190 * t); px[i + 1] = (byte)(60 + 190 * t); px[i + 2] = (byte)(60 + 120 * t);
        }
        for (int k = 0; k < pts.Count; k++)
        {
            int cx = (int)((pts[k][0] - minX) * s), cy = (int)((maxY - pts[k][1]) * s);
            byte r = 255, g = 0, b = 0; int rad = 1;
            if (k == 0) { r = 0; g = 255; rad = 4; } else if (k == pts.Count - 1) { r = 0; b = 255; rad = 4; }
            for (int dy = -rad; dy <= rad; dy++) for (int dx = -rad; dx <= rad; dx++)
            {
                int xx = cx + dx, yy = cy + dy; if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                int i = (yy * w + xx) * 4; px[i] = r; px[i + 1] = g; px[i + 2] = b;
            }
        }
        Png.Write(path, w, h, px);
        Console.WriteLine("picture " + path + " " + w + "x" + h + " (1 wu = " + s.ToString("0") + " px)");
    }
}

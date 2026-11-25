using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class I
{
    static int PathData(HaloMap m, HaloScenario sc)
    {
        for (int b = 0; b < sc.Bsps.Count; b++)
        {
            var r = sc.BspRoot(b);
            int ps = r.Count(484), pe = r.Count(496);
            var cbs = r.Block(176, 96).ToList();
            Console.WriteLine($"bsp {b} {sc.Bsps[b].Tag?.Name}: pathfinding surfaces {ps} edges {pe} collision bsps {cbs.Count}");
            if (cbs.Count == 0) continue;
            var cb = new HaloCollisionBsp(cbs[0]);
            int walk = 0;
            foreach (var s in cb.Surfaces) { cb.PlaneOf(s, out float nx, out float ny, out float nz, out float d); if (nz > 0.7f) walk++; }
            Console.WriteLine($"    collision surfaces {cb.Surfaces.Count} (+{cb.BadSurfaces} bad) raw {cbs[0].Count(60)} edges {cbs[0].Count(72)} verts {cb.Vertices.Length / 3} walkable(nz>0.7) {walk}");
            if (ps > 0)
            {
                var bytes = r.Block(484, 1).Select(x => x.U8(0)).ToList();
                var hist = bytes.GroupBy(x => x).OrderBy(g => g.Key).Select(g => g.Key.ToString("X2") + ":" + g.Count());
                Console.WriteLine("    surface bytes: " + string.Join(" ", hist.Take(60)));
                // flag vs normal
                int fl = 0, flSteep = 0, flDown = 0, unflUp = 0;
                for (int i = 0; i < cb.Surfaces.Count && i < bytes.Count; i++)
                {
                    cb.PlaneOf(cb.Surfaces[i], out float nx, out float ny, out float nz, out float d);
                    bool f = (bytes[i] & 0x40) != 0;
                    if (f) { fl++; if (nz < 0.7f) flSteep++; if (nz < 0f) flDown++; } else if (nz > 0.9f) unflUp++;
                }
                Console.WriteLine($"    flagged {fl}: nz<0.7 {flSteep}, nz<0 {flDown}; unflagged with nz>0.9: {unflUp}");
            }
        }
        return 0;
    }

    static int Nav(HaloMap m, HaloScenario sc, string[] a)
    {
        var ai = new HaloAiScenario(sc);
        int only = a.Length > 2 ? int.Parse(a[2]) : -1;
        var rng = new Random(1);
        for (int b = 0; b < sc.Bsps.Count; b++)
        {
            if (only >= 0 && b != only) continue;
            var sw = Stopwatch.StartNew();
            var nav = HaloAiNav.ForBsp(sc, b);
            sw.Stop();
            if (nav == null) { Console.WriteLine($"bsp {b}: no nav"); continue; }
            var compSize = new Dictionary<int, int>();
            for (int p = 0; p < nav.PolyCount; p++) { int c = nav.Component(p); compSize[c] = compSize.TryGetValue(c, out int n) ? n + 1 : 1; }
            var big = compSize.OrderByDescending(k => k.Value).Take(6).Select(k => k.Value.ToString());
            Console.WriteLine($"bsp {b} {sc.Bsps[b].Tag?.Name}: polys {nav.PolyCount} links {nav.LinkCount} step links {nav.StepLinks} components {nav.Components} (largest {string.Join(",", big)}) from data {nav.FromPathfindingData} built {sw.ElapsedMilliseconds} ms");

            // encounters on this bsp: locate their starts and firing positions, path start -> each firing position
            int failA = 0, failB = 0, failC = 0, failX = 0; int starts = 0, startsOn = 0, fps = 0, fpsOn = 0, paths = 0, pathsOk = 0; long ticks = 0; int maxExp = 0;
            var pts = new List<HVec3>();
            foreach (var e in ai.Encounters)
            {
                if (e.Bsp != b && !(e.Bsp < 0 && b == 0)) continue;
                foreach (var f in e.FiringPositions) { fps++; if (nav.Locate(f.Position.X, f.Position.Y, f.Position.Z + 0.05f) >= 0) fpsOn++; }
                foreach (var s in e.Squads)
                    foreach (var l in s.Starts)
                    {
                        starts++;
                        if (nav.Locate(l.Position.X, l.Position.Y, l.Position.Z + 0.05f) >= 0) startsOn++;
                        for (int k = 0; k < 3 && e.FiringPositions.Count > 0; k++)
                        {
                            var f = e.FiringPositions[rng.Next(e.FiringPositions.Count)];
                            var t0 = Stopwatch.GetTimestamp();
                            bool ok = nav.FindPath(new HVec3(l.Position.X, l.Position.Y, l.Position.Z + 0.05f), new HVec3(f.Position.X, f.Position.Y, f.Position.Z + 0.05f), 0.1f, pts);
                            ticks += Stopwatch.GetTimestamp() - t0;
                            if (!ok) { int la = nav.Locate(l.Position.X, l.Position.Y, l.Position.Z + 0.05f), lb = nav.Locate(f.Position.X, f.Position.Y, f.Position.Z + 0.05f, 0.3f, 2.5f, 1.5f); if (la < 0) failA++; else if (lb < 0) failB++; else if (nav.Component(la) != nav.Component(lb)) failC++; else failX++; }
                            paths++; if (ok) pathsOk++; if (nav.LastExpanded > maxExp) maxExp = nav.LastExpanded;
                        }
                    }
            }
            Console.WriteLine($"    encounter starts on nav {startsOn}/{starts}, firing positions on nav {fpsOn}/{fps}, start->firing paths {pathsOk}/{paths} avg {(paths > 0 ? ticks * 1000.0 / Stopwatch.Frequency / paths : 0):0.000} ms max expanded {maxExp} fail: start off {failA} goal off {failB} split {failC} other {failX}");
        }
        return 0;
    }

    /// <summary>Top-down picture of one BSP's navigation with an encounter's starts (green), firing positions (yellow) and paths (red).</summary>
    static int NavPng(HaloMap m, HaloScenario sc, string[] a)
    {
        int b = int.Parse(a[2]); string encName = a.Length > 3 ? a[3] : null; string outPath = a.Length > 4 ? a[4] : "nav.png";
        var ai = new HaloAiScenario(sc);
        var nav = HaloAiNav.ForBsp(sc, b);
        var enc = encName != null ? ai.FindEncounter(encName) : null;
        // bounds: the encounter's points (+ margin) or the whole nav
        float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
        void Grow(HVec3 p) { if (p.X < x0) x0 = p.X; if (p.Y < y0) y0 = p.Y; if (p.X > x1) x1 = p.X; if (p.Y > y1) y1 = p.Y; }
        if (enc != null) { foreach (var f in enc.FiringPositions) Grow(f.Position); foreach (var s in enc.Squads) foreach (var l in s.Starts) Grow(l.Position); x0 -= 4; y0 -= 4; x1 += 4; y1 += 4; }
        else for (int p = 0; p < nav.PolyCount; p++) Grow(nav.Centroid(p));
        int W = 1024; float scale = W / Math.Max(x1 - x0, y1 - y0); int H = W;
        var img = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { img[i * 4 + 3] = 255; }
        float zRef = 0; int zc = 0; if (enc != null) { foreach (var f in enc.FiringPositions) { zRef += f.Position.Z; zc++; } if (zc > 0) zRef /= zc; }
        // polygons: fill by sampling
        for (int py = 0; py < H; py++)
            for (int px = 0; px < W; px++)
            {
                float x = x0 + px / scale, y = y1 - py / scale;
                int p = nav.Locate(x, y, enc != null ? zRef + 0.5f : 1000f, 0.6f, enc != null ? 1.2f : 2000f, 0f);
                if (p < 0) continue;
                int c = nav.Component(p);
                int o = (py * W + px) * 4;
                img[o] = (byte)(60 + (c * 53) % 120); img[o + 1] = (byte)(60 + (c * 97) % 120); img[o + 2] = (byte)(90 + (p * 31) % 60);
            }
        void Dot(HVec3 p, byte r, byte g, byte bb, int rad) { int cx = (int)((p.X - x0) * scale), cy = (int)((y1 - p.Y) * scale); for (int dy = -rad; dy <= rad; dy++) for (int dx = -rad; dx <= rad; dx++) { int X = cx + dx, Y = cy + dy; if (X < 0 || Y < 0 || X >= W || Y >= H) continue; int o = (Y * W + X) * 4; img[o] = r; img[o + 1] = g; img[o + 2] = bb; } }
        void Line(HVec3 p, HVec3 q, byte r, byte g, byte bb) { int n = (int)(Math.Max(Math.Abs(p.X - q.X), Math.Abs(p.Y - q.Y)) * scale) + 1; for (int i = 0; i <= n; i++) { float t = (float)i / n; Dot(new HVec3(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t, 0), r, g, bb, 0); } }
        if (enc != null)
        {
            var pts = new List<HVec3>();
            var rng = new Random(2);
            foreach (var s in enc.Squads)
                foreach (var l in s.Starts)
                {
                    if (enc.FiringPositions.Count == 0) break;
                    var f = enc.FiringPositions[rng.Next(enc.FiringPositions.Count)];
                    var from = new HVec3(l.Position.X, l.Position.Y, l.Position.Z + 0.05f);
                    bool ok = nav.FindPath(from, new HVec3(f.Position.X, f.Position.Y, f.Position.Z + 0.05f), 0.1f, pts);
                    var prev = from;
                    foreach (var q in pts) { Line(prev, q, ok ? (byte)255 : (byte)128, 40, 40); prev = q; }
                }
            foreach (var f in enc.FiringPositions) Dot(f.Position, 255, 230, 0, 2);
            foreach (var s in enc.Squads) foreach (var l in s.Starts) Dot(l.Position, 0, 255, 0, 3);
        }
        Png.Write(outPath, W, H, img);
        Console.WriteLine($"wrote {outPath} ({x1 - x0:0.#} x {y1 - y0:0.#} wu)");
        return 0;
    }
}

static partial class I
{
    public static int SquadsOnNav(HaloMap m, HaloScenario sc)
    {
        var ai = new HaloAiScenario(sc);
        var navs = new System.Collections.Generic.Dictionary<int, HaloAiNav>();
        foreach (var e in ai.Encounters)
        {
            int b = e.Bsp < 0 ? 0 : e.Bsp;
            if (!navs.TryGetValue(b, out var nav)) navs[b] = nav = HaloAiNav.ForBsp(sc, b);
            foreach (var s in e.Squads)
            {
                if (s.Starts.Count == 0) continue;
                int on = 0; float dz = 0;
                foreach (var l in s.Starts) { int p = nav.Locate(l.Position.X, l.Position.Y, l.Position.Z + 0.05f); if (p >= 0) { on++; } else { int q = nav.Locate(l.Position.X, l.Position.Y, l.Position.Z, 0.15f, 50f, 0f); if (q >= 0) dz = Math.Max(dz, l.Position.Z - nav.HeightAt(q, l.Position.X, l.Position.Y)); } }
                Console.WriteLine($"{e.Name}/{s.Name} bsp {b} starts {s.Starts.Count} on nav {on} (highest above floor {dz:0.0} wu) n{s.NormalCount} {Short(ai.PaletteVariant(s.ActorType))} state {s.InitialState} at {s.Starts[0].Position}");
            }
        }
        return 0;
    }
}

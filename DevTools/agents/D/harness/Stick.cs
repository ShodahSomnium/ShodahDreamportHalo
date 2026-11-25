using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static HaloMap _m; static HaloSkeleton _sk; static HaloAnimGraph _g; static int[] _map;
    static void Load(string map)
    {
        _m = new HaloMap(Maps + map + ".map");
        var model = new HaloModel(_m.Tags.First(x => x.ClassName == "mod2" && x.Name == @"characters\cyborg\cyborg"));
        _sk = new HaloSkeleton(model);
        _g = new HaloAnimGraph(_m.Tags.First(x => x.ClassName == "antr" && x.Name == @"characters\cyborg\cyborg"));
        _map = _sk.MapFrom(_g);
    }

    // check bind vs vertices
    static int Bind(string[] a)
    {
        Load(a[1]);
        var model = new HaloModel(_m.Tags.First(x => x.ClassName == "mod2" && x.Name == @"characters\cyborg\cyborg"));
        var pose = new HaloPose(_sk); pose.ToModel();
        var sum = new double[_sk.Count, 4];
        foreach (var g in model.Geometries.Take(1)) foreach (var p in g.Parts)
        {
            var d = model.Read(p);
            for (int i = 0; i < d.Position.Length; i++)
            {
                if (d.Weight0[i] < 0.95f) continue;
                int n = p.LocalNodes != null ? p.LocalNodes[d.Node0[i]] : d.Node0[i];
                if (n < 0 || n >= _sk.Count) continue;
                sum[n, 0] += d.Position[i].X; sum[n, 1] += d.Position[i].Y; sum[n, 2] += d.Position[i].Z; sum[n, 3]++;
            }
        }
        for (int n = 0; n < _sk.Count; n++)
        {
            double c = Math.Max(1, sum[n, 3]);
            Console.WriteLine($"{_sk.Names[n],-20} node ({pose.MTrans[n*3]:0.000},{pose.MTrans[n*3+1]:0.000},{pose.MTrans[n*3+2]:0.000}) verts({sum[n,3]}) avg ({sum[n,0]/c:0.000},{sum[n,1]/c:0.000},{sum[n,2]/c:0.000})");
        }
        return 0;
    }

    static readonly int[,] Bones = { {0,1},{1,4},{4,8},{0,2},{2,5},{5,11},{0,3},{3,6},{6,9},{9,12},{6,7},{7,13},{13,15},{15,17},{6,10},{10,14},{14,16},{16,18} };

    static void DrawPose(byte[] img, int W, int H, HaloPose pose, int ox, int view, byte r, byte gg, byte b)
    {
        // view 0 = front (from +X), 1 = side (from -Y, the right side), 2 = top
        Func<float, float, float, (int, int)> pr = (x, y, z) =>
        {
            float sx, sy;
            if (view == 0) { sx = y; sy = z; } else if (view == 1) { sx = x; sy = z; } else { sx = -y; sy = x; }
            return (ox + (int)(sx * 400) + 150, H - 20 - (int)(sy * 400) - (view == 2 ? 200 : 0));
        };
        for (int i = 0; i < Bones.GetLength(0); i++)
        {
            int p = Bones[i, 0], c = Bones[i, 1];
            var A = pr(pose.MTrans[p*3], pose.MTrans[p*3+1], pose.MTrans[p*3+2]); var B = pr(pose.MTrans[c*3], pose.MTrans[c*3+1], pose.MTrans[c*3+2]);
            byte rr = c == 17 || c == 15 || c == 13 || c == 4 || c == 8 || c == 1 ? (byte)(r / 2) : r;   // left side darker
            Line(img, W, H, A.Item1, A.Item2, B.Item1, B.Item2, rr, gg, b);
        }
        HaloSkeleton.Marker mk;
        if (_sk.FindMarker("right hand", out mk))
        {
            pose.MarkerFrame(mk, out float px, out float py, out float pz, out float fx, out float fy, out float fz, out float ux, out float uy, out float uz);
            var A = pr(px, py, pz); var F = pr(px + fx * 0.25f, py + fy * 0.25f, pz + fz * 0.25f); var U = pr(px + ux * 0.06f, py + uy * 0.06f, pz + uz * 0.06f);
            Line(img, W, H, A.Item1, A.Item2, F.Item1, F.Item2, 255, 60, 60); Line(img, W, H, A.Item1, A.Item2, U.Item1, U.Item2, 60, 255, 60);
        }
        if (_sk.FindMarker("left hand", out mk))
        {
            pose.MarkerFrame(mk, out float px, out float py, out float pz, out float fx, out float fy, out float fz, out float ux, out float uy, out float uz);
            var A = pr(px, py, pz); var F = pr(px + fx * 0.06f, py + fy * 0.06f, pz + fz * 0.06f);
            Line(img, W, H, A.Item1, A.Item2, F.Item1, F.Item2, 255, 255, 60);
        }
    }

    static void Line(byte[] img, int W, int H, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) + 1;
        for (int i = 0; i <= n; i++)
        {
            int x = x0 + (x1 - x0) * i / n, y = y0 + (y1 - y0) * i / n;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                int o = (yy * W + xx) * 4; img[o] = r; img[o + 1] = g; img[o + 2] = b; img[o + 3] = 255;
            }
        }
    }

    // stick <map> <out.png> <anim name> [time] [overlay name] [yaw] [pitch]
    static int Stick(string[] a)
    {
        Load(a[1]);
        int W = 900, H = 520;
        var img = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { img[i*4] = 30; img[i*4+1] = 30; img[i*4+2] = 34; img[i*4+3] = 255; }
        var pose = new HaloPose(_sk);
        pose.Reset();
        var an = _g.Get(a[3]);
        if (an == null && a[3] != "bind") { Console.WriteLine("no anim " + a[3]); return 1; }
        float t = a.Length > 4 ? float.Parse(a[4]) : 0f;
        if (an != null) { pose.Apply(an, _map, t, true, 1f); Console.WriteLine(an + " err " + an.Error); }
        if (a.Length > 5)
        {
            var ov = _g.Get(a[5]);
            float yaw = float.Parse(a[6]) * (float)Math.PI / 180f, pitch = float.Parse(a[7]) * (float)Math.PI / 180f;
            if (a.Length > 8) HaloPose.OverlayPost = a[8] == "post";
            var seat = _g.FindSeat("stand"); var wc = seat.Weapon(a.Length > 9 ? a[9] : "rifle");
            Console.WriteLine($"overlay {ov} weapon yaw {wc.RightYawPerFrame} {wc.LeftYawPerFrame} {wc.RightFrames}/{wc.LeftFrames} pitch {wc.DownPitchPerFrame} {wc.UpPitchPerFrame} {wc.DownFrames}/{wc.UpFrames}");
            pose.Aim(ov, _map, yaw, pitch, seat.RightYawPerFrame, seat.LeftYawPerFrame, seat.RightFrames, seat.LeftFrames, seat.DownPitchPerFrame, seat.UpPitchPerFrame, seat.DownFrames, seat.UpFrames, 1f);
        }
        pose.ToModel();
        DrawPose(img, W, H, pose, 0, 0, 220, 220, 220);
        DrawPose(img, W, H, pose, 300, 1, 220, 220, 220);
        DrawPose(img, W, H, pose, 600, 2, 220, 220, 220);
        HaloSkeleton.Marker mk; _sk.FindMarker("right hand", out mk);
        pose.MarkerFrame(mk, out float px, out float py, out float pz, out float fx, out float fy, out float fz, out float ux, out float uy, out float uz);
        Console.WriteLine($"gun at ({px:0.000},{py:0.000},{pz:0.000}) fwd ({fx:0.00},{fy:0.00},{fz:0.00}) yaw {Math.Atan2(fy, fx) * 180 / Math.PI:0.0} pitch {Math.Asin(fz) * 180 / Math.PI:0.0} up ({ux:0.00},{uy:0.00},{uz:0.00})");
        Console.WriteLine($"head ({pose.MTrans[36]:0.000},{pose.MTrans[37]:0.000},{pose.MTrans[38]:0.000}) l hand ({pose.MTrans[51]:0.000},{pose.MTrans[52]:0.000},{pose.MTrans[53]:0.000}) r foot z {pose.MTrans[35]:0.000}");
        Png.Write(a[2], W, H, img);
        return 0;
    }
}

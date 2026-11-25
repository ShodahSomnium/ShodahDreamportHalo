using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;

static class ProbeLayout
{
    // layout <map> <out.png> : Halo's MP HUD composed offline on a 640x480 canvas with the HUD module's placement rules (preview)
    static byte[] C; const int W = 640, H = 480; static HaloResources Res; static HaloMap M; static float Mp = 0.75f;
    public static int Run(HaloMap m, string[] a)
    {
        M = m; Res = HaloResources.Get(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\bitmaps.map");
        C = new byte[W * H * 4];
        for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) { int o = (y * W + x) * 4; C[o] = (byte)(60 + y / 8); C[o + 1] = (byte)(80 + y / 10); C[o + 2] = 70; C[o + 3] = 255; }
        var g = HudGlobals.Read(m); Mp = g.MultiplayerScale;
        HaloTag biped = null; foreach (var t in m.OfClass("matg")) foreach (var mi in t.Root.Block(356, 160)) { biped = mi.Ref(16); break; }
        HaloTag unhi = null; foreach (var h in biped.Root.Block(680, 48)) { unhi = h.Ref(0); break; }
        var u = HudUnit.Read(unhi);
        Static(u.MotionBack, HudAnchor.BottomLeft); Static(u.MotionFore, HudAnchor.BottomLeft);
        Static(u.ShieldBack, u.Anchor); Meter(u.Shield, u.Anchor, 0.7f, -1, new HudColor(0, 0x28, 0x96, 0xff));
        Static(u.HealthBack, u.Anchor); Meter(u.Health, u.Anchor, 1f, -1, u.Health.Max);
        HaloTag ar = null; foreach (var t in m.OfClass("weap")) if (t.Name.EndsWith("assault rifle")) ar = t;
        var w = HudWeapon.Read(ar.Root.Ref(1152));
        var chain = new List<HudWeapon>(); for (var x = w; x != null; x = x.Child) chain.Add(x);
        for (int c = chain.Count - 1; c >= 0; c--)
        {
            var e = chain[c];
            foreach (var s in e.Statics) if (s.View != 2) Static(s, e.Anchor);
            foreach (var s in e.Meters) if (s.View != 2) Meter(s, e.Anchor, 32 / 60f, 32, s.Max);
            foreach (var n in e.Numbers) if (n.View == 1 && n.State == 0) Number(g.Digits, n, e.Anchor, 180);
            foreach (var oe in e.Overlays) if (oe.View != 2) foreach (var o in oe.Overlays) Sprite(oe.Bitmap, o.Sequence, e.Anchor, o.Place, o.Color.Default, false);
            foreach (var cr in e.Crosshairs) if (cr.Type == 0) foreach (var o in cr.Overlays) Sprite(cr.Bitmap, o.Sequence, HudAnchor.Center, o.Place, o.Color.Default, true);
        }
        float gx = 0;
        foreach (var gt in g.GrenadeHuds)
        {
            var gh = HudGrenade.Read(gt);
            Static(gh.Background, gh.Anchor, gx); Static(gh.TotalBack, gh.Anchor, gx);
            foreach (var o in gh.Overlays) if ((o.Type & 4) != 0) Sprite(gh.OverlayBitmap, o.Sequence, gh.Anchor, o.Place, o.Color.Default, false, gx);
            Number(g.Digits, gh.Numbers, gh.Anchor, 2, gx);
            gx += 40 * 0.75f;
        }
        if (a.Length > 6) { int cx = int.Parse(a[3]), cy = int.Parse(a[4]), cw = int.Parse(a[5]), ch = int.Parse(a[6]), S = 4; var cr = new byte[cw * S * ch * S * 4]; for (int yy = 0; yy < ch * S; yy++) for (int xx = 0; xx < cw * S; xx++) { int so = ((cy + yy / S) * W + cx + xx / S) * 4, d = (yy * cw * S + xx) * 4; cr[d] = C[so]; cr[d + 1] = C[so + 1]; cr[d + 2] = C[so + 2]; cr[d + 3] = 255; } Png.Write(a[2], cw * S, ch * S, cr); return 0; }
        // 2x for viewing
        var big = new byte[W * 2 * H * 2 * 4];
        for (int y = 0; y < H * 2; y++) for (int x = 0; x < W * 2; x++) { int so = ((y / 2) * W + x / 2) * 4, d = (y * W * 2 + x) * 4; big[d] = C[so]; big[d + 1] = C[so + 1]; big[d + 2] = C[so + 2]; big[d + 3] = 255; }
        Png.Write(a[2], W * 2, H * 2, big);
        return 0;
    }

    struct Spr { public byte[] Px; public int Iw, Sx, Sy, Sw, Sh; public float Rx, Ry; }

    static float SS(HudPlace p) { if ((p.Flags & 2) != 0) return 1f; return (p.HighRes ? 0.5f : 1f) * Mp; }
    static float OS(HudPlace p) { return (p.Flags & 1) != 0 ? 1f : Mp; }

    static Spr Get(HaloTag bm, int seq, int frame = 0)
    {
        var b = new HaloBitmap(bm); if (seq < 0) seq = 0;
        var q = seq < b.Sequences.Count ? b.Sequences[seq] : null; int img; float l = 0, t = 0, r = 1, bo = 1, rx = -1, ry = -1;
        if (q != null && q.Sprites.Count > 0) { var s = q.Sprites[Math.Min(frame, q.Sprites.Count - 1)]; img = s.Bitmap; l = s.Left; t = s.Top; r = s.Right; bo = s.Bottom; rx = s.RegX; ry = s.RegY; }
        else if (q != null && q.Count > 0) img = q.First + frame; else img = seq;
        var im = b.Images[img]; var px = HaloBitmap.DecodeRgba(im, HaloBitmap.Raw(M, im, Res));
        return new Spr { Px = px, Iw = im.Width, Sx = (int)(l * im.Width), Sy = (int)(t * im.Height), Sw = Math.Max(1, (int)((r - l) * im.Width)), Sh = Math.Max(1, (int)((bo - t) * im.Height)), Rx = rx >= 0 ? rx * im.Width : im.RegX, Ry = ry >= 0 ? ry * im.Height : im.RegY };
    }

    static void Pos(HudAnchor a, float ox, float oy, float w, float h, out float x, out float y)
    {
        switch (a)
        {
            case HudAnchor.TopRight: x = W - ox - w; y = oy; break;
            case HudAnchor.BottomLeft: x = ox; y = H - oy - h; break;
            case HudAnchor.BottomRight: x = W - ox - w; y = H - oy - h; break;
            case HudAnchor.Center: x = W / 2 + ox; y = H / 2 + oy; break;
            default: x = ox; y = oy; break;
        }
    }

    static void Blit(Spr s, float x, float y, float sc, Func<int, float[]> col)
    {
        int dw = (int)(s.Sw * sc), dh = (int)(s.Sh * sc);
        for (int j = 0; j < dh; j++) for (int i = 0; i < dw; i++)
        {
            int px = (int)x + i, py = (int)y + j; if (px < 0 || py < 0 || px >= W || py >= H) continue;
            int si = (s.Sy + (int)(j / sc)) * s.Iw + s.Sx + (int)(i / sc);
            var c = col(si); if (c[3] <= 0) continue;
            int o = (py * W + px) * 4;
            for (int k = 0; k < 3; k++) C[o + k] = (byte)Math.Min(255, C[o + k] * (1 - c[3]) + c[k] * 255 * c[3]);
        }
    }

    static float[] Add(HudColor c, float texA)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f, m = Math.Max(r, Math.Max(g, b)); if (m < 0.004f) return new float[4];
        float a = c.A > 0 ? c.A / 255f : 1f;
        if (m < 0.3f && c.A > 0) return new[] { r, g, b, a * texA };
        return new[] { r / m, g / m, b / m, m * a * texA };
    }

    static void Static(HudStatic s, HudAnchor a, float extra = 0)
    {
        if (s == null || s.Bitmap == null) return;
        var sp = Get(s.Bitmap, s.Sequence); float sc = SS(s.Place);
        Pos(a, s.Place.X * OS(s.Place) + extra, s.Place.Y * OS(s.Place), sp.Sw * sc, sp.Sh * sc, out float x, out float y);
        Blit(sp, x, y, sc, si => Add(s.Color.Default, sp.Px[si * 4 + 3] / 255f));
    }

    static void Sprite(HaloTag bm, int seq, HudAnchor a, HudPlace p, HudColor col, bool reg, float extra = 0)
    {
        var sp = Get(bm, seq); float sc = SS(p); float x, y;
        if (reg) { x = W / 2 + p.X * OS(p) - sp.Rx * sc; y = H / 2 + p.Y * OS(p) - sp.Ry * sc; }
        else Pos(a, p.X * OS(p) + extra, p.Y * OS(p), sp.Sw * sc, sp.Sh * sc, out x, out y);
        Blit(sp, x, y, sc, si => Add(col, sp.Px[si * 4 + 3] / 255f));
    }

    static void Meter(HudMeter mt, HudAnchor a, float f, int count, HudColor lit)
    {
        var sp = Get(mt.Bitmap, mt.Sequence); float sc = SS(mt.Place);
        Pos(a, mt.Place.X * OS(mt.Place), mt.Place.Y * OS(mt.Place), sp.Sw * sc, sp.Sh * sc, out float x, out float y);
        float thr = count >= 0 ? count * Math.Max(1, (int)mt.AlphaMul) + mt.AlphaBias : mt.ValueScale > 0 ? f * mt.ValueScale * Math.Max(1, (int)mt.AlphaMul) + mt.AlphaBias : f * 255;
        if (thr < mt.MinValue) thr = mt.MinValue;
        float al = 1 - mt.Translucency * 0.5f;
        Blit(sp, x, y, sc, si => { bool on = sp.Px[si * 4 + 3] <= thr; var c = Add(on ? lit : mt.Empty, al); c[3] *= sp.Px[si * 4] / 255f; return c; });
    }

    static void Number(HudDigits d, HudNumberEl n, HudAnchor a, int v, float extra = 0)
    {
        string s = v.ToString(); if (n.LeadingZeros) s = s.PadLeft(n.MaxDigits, '0');
        float sc = SS(n.Place); float pic = (float)d.ScreenDigitWidth / d.BitmapDigitWidth;
        float x = n.Place.X * OS(n.Place) + extra + d.XOffset * sc, y = n.Place.Y * OS(n.Place);
        foreach (char ch in s)
        {
            var sp = Get(d.Bitmap, 0, ch - '0');
            Pos(a, x, y, sp.Sw * pic * sc, sp.Sh * pic * sc, out float px, out float py);
            Blit(sp, px, py, pic * sc, si => Add(n.Color.Default, sp.Px[si * 4 + 3] / 255f));
            x += d.ScreenDigitWidth * sc;
        }
    }
}

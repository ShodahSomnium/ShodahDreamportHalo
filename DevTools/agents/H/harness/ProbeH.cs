using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static class ProbeH
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";

    static int Main(string[] a)
    {
        var m = new HaloMap(Maps + (a.Length > 1 ? a[1] : "bloodgulch") + ".map");
        switch (a[0])
        {
            case "hud": return Hud(m, a);
            case "png": return PngAll(m, a[2]);
            case "str": foreach (var t in m.OfClass("ustr")) if (a.Length < 3 || t.Name.Contains(a[2])) { Console.WriteLine("== " + t.Name); int i = 0; foreach (var s in HaloStrings.Read(t)) Console.WriteLine($"  {i++,3}: {s.Replace("\r", "\\r").Replace("\n", "\\n")}"); } return 0;
            case "font": return Font(m, a);
            case "row": return ProbeRow.Run(m, a);
            case "layout": return ProbeLayout.Run(m, a);
            case "sheet": return ProbeSheet.Run(m, a);
        }
        Console.WriteLine("hud|png|str|font"); return 1;
    }

    static int Hud(HaloMap m, string[] a)
    {
        var g = HudGlobals.Read(m);
        Console.WriteLine($"hudg {g.Tag} anchor {g.Anchor} {g.Place} font {g.Font} split {g.SplitFont} up {g.UpTime} fade {g.FadeTime} icon {g.IconColor} text {g.TextColor} spacing {g.TextSpacing}");
        Console.WriteLine($"  item msgs {g.ItemMessages} icon bitmap {g.IconBitmap} alt {g.AlternateIconText} hud msgs {g.HudMessages}");
        Console.WriteLine($"  help {g.HelpColor} objective {g.ObjectiveColor} up {g.ObjectiveUp} fade {g.ObjectiveFade} offsets t{g.TopOffset} b{g.BottomOffset} l{g.LeftOffset} r{g.RightOffset}");
        Console.WriteLine($"  arrow {g.ArrowBitmap} mp scale {g.MultiplayerScale} default wphi {g.DefaultWeaponHud} motion range {g.MotionRange} vel {g.MotionVelocity} scale {g.MotionScale}");
        Console.WriteLine($"  damage {g.DamageTop},{g.DamageBottom},{g.DamageLeft},{g.DamageRight} {g.DamageBitmap} seq {g.DamageSequence}/{g.DamageMpSequence} {g.DamageColor}");
        Console.WriteLine($"  not much time {g.NotMuchTimeLeft} timeout {g.TimeOutFlash} carnage {g.CarnageBitmap}");
        foreach (var w in g.Arrows) Console.WriteLine($"  arrow '{w.Name}' {w.Color} op {w.Opacity} tr {w.Translucency} seq {w.OnScreen}/{w.OffScreen}/{w.Occluded} f{w.Flags}");
        int bi = 0; foreach (var b in g.ButtonIcons) Console.WriteLine($"  button {bi++}: seq {b.Sequence} w {b.WidthOffset} at {b.X},{b.Y} {b.Color} fps {b.FrameRate} flags {b.Flags} text {b.Text}");
        Console.WriteLine($"matg: sweep {g.SweepBitmap} mask {g.SweepMask} mp {g.MultiplayerBitmap} blip {g.BlipBitmap} sys font {g.SystemFont} term {g.TerminalFont} digits {g.Digits}");
        for (int i = 0; i < g.GrenadeHuds.Count; i++) Console.WriteLine($"  grenade {i}: {g.GrenadeHuds[i]} max {g.GrenadeMax[i]}");
        foreach (var t in m.OfClass("unhi"))
        {
            var u = HudUnit.Read(t);
            Console.WriteLine($"unhi {t.Name} anchor {u.Anchor} canvas {u.CanvasSize} overlay anchor {u.OverlayAnchor}");
            Console.WriteLine($"  background   {u.Background}");
            Console.WriteLine($"  shield back  {u.ShieldBack}");
            Console.WriteLine($"  shield meter {u.Shield}");
            Console.WriteLine($"  overcharge {u.OverMin} {u.OverMax} {u.OverFlash} {u.OverEmpty}");
            Console.WriteLine($"  health back  {u.HealthBack}");
            Console.WriteLine($"  health meter {u.Health} medium {u.MediumHealth} cut {u.MaxColorHealthCutoff}/{u.MinColorHealthCutoff}");
            Console.WriteLine($"  motion back  {u.MotionBack}");
            Console.WriteLine($"  motion fore  {u.MotionFore}");
            Console.WriteLine($"  motion centre {u.MotionCenter}");
            foreach (var o in u.AuxOverlays) Console.WriteLine($"  aux overlay type {o.State} flags {o.View} {o}");
            foreach (var s in u.Sounds) Console.WriteLine($"  sound {s.Key} latch {s.Value:X}");
        }
        foreach (var t in m.OfClass("grhi"))
        {
            var gr = HudGrenade.Read(t);
            Console.WriteLine($"grhi {t.Name} anchor {gr.Anchor} flash cutoff {gr.FlashCutoff} overlay {gr.OverlayBitmap} msg seq {gr.MsgSequence} w {gr.MsgWidthOffset} at {gr.MsgX},{gr.MsgY} f{gr.MsgFlags} text {gr.MsgText}");
            Console.WriteLine($"  background {gr.Background}");
            Console.WriteLine($"  total back {gr.TotalBack}");
            Console.WriteLine($"  numbers    {gr.Numbers}");
            foreach (var o in gr.Overlays) Console.WriteLine($"  overlay {o}");
            foreach (var s in gr.Sounds) Console.WriteLine($"  sound {s.Key} latch {s.Value:X}");
        }
        foreach (var t in m.OfClass("wphi"))
        {
            if (a.Length > 2 && !t.Name.Contains(a[2])) continue;
            DumpW(HudWeapon.Read(t), "");
        }
        foreach (var t in m.OfClass("weap"))
        {
            var r = t.Root;
            var wh = r.Ref(1152);
            int mags = r.Count(1264);
            string mag = "";
            foreach (var mg in r.Block(1264, 112)) { mag += $" mag(rounds recharged {mg.I16(4)} total initial {mg.I16(6)} max {mg.I16(8)} loaded max {mg.I16(10)})"; }
            Console.WriteLine($"weap {t.Name}: hud {wh?.Name} zoom levels {r.I16(986)} pickup text {r.I16(384)} msg idx {r.I16(316)}{mag}");
        }
        foreach (var t in m.OfClass("bipd")) { var r = t.Root; foreach (var h in r.Block(680, 48)) Console.WriteLine($"bipd {t.Name}: unhi {h.Ref(0)?.Name}"); }
        foreach (var t in m.OfClass("hmt "))
        {
            var hm = HudMessages.Read(t);
            foreach (var n in hm.Names) Console.WriteLine($"hmt '{n}': {hm.Plain(n)}");
        }
        return 0;
    }

    static void DumpW(HudWeapon w, string ind)
    {
        if (w == null) return;
        Console.WriteLine($"{ind}wphi {w} flags {w.Flags} cutoffs total {w.TotalAmmoCutoff} loaded {w.LoadedAmmoCutoff} heat {w.HeatCutoff} age {w.AgeCutoff} xtypes {w.CrosshairTypes:X} zoom mask {w.ZoomMask} mflags {w.ZoomMaskFlags} msg seq {w.MsgSequence} w {w.MsgWidthOffset} at {w.MsgX},{w.MsgY} f{w.MsgFlags} text {w.MsgText}");
        foreach (var s in w.Statics) Console.WriteLine($"{ind}  static state {s.State} view {s.View} anchor {s.ChildAnchor} {s}");
        foreach (var s in w.Meters) Console.WriteLine($"{ind}  meter state {s.State} view {s.View} anchor {s.ChildAnchor} {s}");
        foreach (var s in w.Numbers) Console.WriteLine($"{ind}  number state {s.State} view {s.View} anchor {s.ChildAnchor} {s}");
        foreach (var c in w.Crosshairs) { Console.WriteLine($"{ind}  crosshair {c}"); foreach (var o in c.Overlays) Console.WriteLine($"{ind}     {o}"); }
        foreach (var c in w.Overlays) { Console.WriteLine($"{ind}  overlay el state {c.State} view {c.View} anchor {c.ChildAnchor} {c.Bitmap?.Name}"); foreach (var o in c.Overlays) Console.WriteLine($"{ind}     {o}"); }
        if (w.Child != null) DumpW(w.Child, ind + "    ");
    }

    static int PngAll(HaloMap m, string outDir)
    {
        var res = HaloResources.Get(Maps + "bitmaps.map");
        Directory.CreateDirectory(outDir);
        foreach (var t in m.OfClass("bitm"))
        {
            if (!t.Name.StartsWith("ui\\hud") && !t.Name.Contains("hud") && !t.Name.Contains("ui\\")) continue;
            var b = new HaloBitmap(t);
            string seqs = string.Join(" | ", b.Sequences.Select((s, i) => $"{i}:{s.Name} first {s.First} n {s.Count} sprites [{string.Join(" ", s.Sprites.Select(p => $"b{p.Bitmap} {p.Left:0.###},{p.Top:0.###}-{p.Right:0.###},{p.Bottom:0.###} reg {p.RegX:0.###},{p.RegY:0.###}"))}]"));
            Console.WriteLine($"{t.Name} type {b.BitmapType} usage {b.Usage} images {b.Images.Count} seqs {seqs}");
            for (int i = 0; i < b.Images.Count && i < 40; i++)
            {
                var img = b.Images[i];
                Console.WriteLine($"   img {i} {img.Width}x{img.Height} {img.Format} type {img.Type} reg {img.RegX},{img.RegY}");
                if (img.Type != 0) continue;
                try
                {
                    var raw = HaloBitmap.Raw(m, img, res);
                    var rgba = HaloBitmap.DecodeRgba(img, raw);
                    string f = Path.Combine(outDir, t.Name.Replace('\\', '_').Replace(' ', '_') + "_" + i + ".png");
                    Png.Write(f, img.Width, img.Height, rgba);
                    // alpha channel as grey (the meters' gradient)
                    var al = new byte[rgba.Length];
                    for (int k = 0; k < rgba.Length; k += 4) { al[k] = al[k + 1] = al[k + 2] = rgba[k + 3]; al[k + 3] = 255; }
                    Png.Write(f.Replace(".png", "_a.png"), img.Width, img.Height, al);
                    var rgb = (byte[])rgba.Clone(); for (int k = 3; k < rgb.Length; k += 4) rgb[k] = 255; Png.Write(f.Replace(".png", "_rgb.png"), img.Width, img.Height, rgb);
                }
                catch (Exception e) { Console.WriteLine("     " + e.Message); }
            }
        }
        return 0;
    }

    static int Font(HaloMap m, string[] a)
    {
        foreach (var t in m.OfClass("font"))
        {
            var f = HaloFont.Read(t);
            Console.WriteLine($"{t.Name}: ascend {f.Ascend} descend {f.Descend} lead {f.LeadHeight}/{f.LeadWidth} glyphs {f.Glyphs.Count} pixels {f.Pixels.Length}");
            var A = f.Get('A'); if (A != null) Console.WriteLine($"   A adv {A.Advance} {A.W}x{A.H} origin {A.OriginX},{A.OriginY} px {A.Pixels}");
            string s = a.Length > 2 ? a[2] : "Press E to pick up 0123 Shield";
            int w = f.Measure(s) + 8, h = f.LineHeight + 8;
            var img = new byte[w * h * 4];
            int x = 4, base0 = 4 + f.Ascend;
            foreach (char c in s)
            {
                var gl = f.Get(c); if (gl == null) continue;
                for (int yy = 0; yy < gl.H; yy++) for (int xx = 0; xx < gl.W; xx++)
                {
                    int px = x + xx - gl.OriginX, py = base0 + yy - gl.OriginY;
                    if (px < 0 || py < 0 || px >= w || py >= h) continue;
                    int si = gl.Pixels + yy * gl.W + xx; if (si >= f.Pixels.Length) continue;
                    byte v = f.Pixels[si]; int o = (py * w + px) * 4;
                    img[o] = img[o + 1] = img[o + 2] = v; img[o + 3] = 255;
                }
                x += gl.Advance;
            }
            Directory.CreateDirectory(@"D:\Dreamport\Halo\Halo Dreamport\DevTools\agents\H\png");
            int S = 4; var big = new byte[w * S * h * S * 4]; for (int yy = 0; yy < h * S; yy++) for (int xx = 0; xx < w * S; xx++) { int so = ((yy / S) * w + xx / S) * 4, dO = (yy * w * S + xx) * 4; big[dO] = img[so]; big[dO + 1] = img[so + 1]; big[dO + 2] = img[so + 2]; big[dO + 3] = 255; }
            Png.Write(Path.Combine(@"D:\Dreamport\Halo\Halo Dreamport\DevTools\agents\H\png", "font_" + Path.GetFileName(t.Name) + ".png"), w * S, h * S, big);
        }
        return 0;
    }
}
static class ProbeRow
{
    // row <map> <bitmap name part> <image> <y> [x0 x1 step]
    public static int Run(HaloMap m, string[] a)
    {
        var res = HaloResources.Get(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\bitmaps.map");
        HaloTag t = null; foreach (var x in m.OfClass("bitm")) if (x.Name.EndsWith(a[2])) { t = x; break; }
        var b = new HaloBitmap(t); var img = b.Images[int.Parse(a[3])];
        var rgba = HaloBitmap.DecodeRgba(img, HaloBitmap.Raw(m, img, res));
        int y = int.Parse(a[4]); int x0 = a.Length > 5 ? int.Parse(a[5]) : 0, x1 = a.Length > 6 ? int.Parse(a[6]) : img.Width, st = a.Length > 7 ? int.Parse(a[7]) : 4;
        for (int x = x0; x < x1; x += st) { int o = (y * img.Width + x) * 4; Console.Write($"{x}:{rgba[o]}/{rgba[o + 3]} "); }
        Console.WriteLine();
        return 0;
    }
}
static class ProbeSheet
{
    // sheet <map> <out.png> <bitmap suffix>:<image> ...   (alpha-blended over dark grey, stacked, a gap between)
    public static int Run(HaloMap m, string[] a)
    {
        var res = HaloResources.Get(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\bitmaps.map");
        var items = new List<(int w, int h, byte[] px)>();
        for (int i = 3; i < a.Length; i++)
        {
            var parts = a[i].Split(':');
            HaloTag t = null; foreach (var x in m.OfClass("bitm")) if (x.Name.EndsWith(parts[0])) { t = x; break; }
            var b = new HaloBitmap(t); var img = b.Images[parts.Length > 1 ? int.Parse(parts[1]) : 0];
            items.Add((img.Width, img.Height, HaloBitmap.DecodeRgba(img, HaloBitmap.Raw(m, img, res))));
        }
        int W = 0, H = 4; foreach (var it in items) { W = Math.Max(W, it.w + 8); H += it.h + 4; }
        var o = new byte[W * H * 4];
        for (int k = 0; k < o.Length; k += 4) { o[k] = 40; o[k + 1] = 44; o[k + 2] = 52; o[k + 3] = 255; }
        int y0 = 4;
        foreach (var it in items)
        {
            for (int y = 0; y < it.h; y++) for (int x = 0; x < it.w; x++)
            {
                int s = (y * it.w + x) * 4, d = ((y0 + y) * W + 4 + x) * 4; float al = it.px[s + 3] / 255f;
                for (int c = 0; c < 3; c++) o[d + c] = (byte)(o[d + c] * (1 - al) + it.px[s + c] * al);
            }
            // a red frame line under it
            for (int x = 0; x < it.w; x++) { int d = ((y0 + it.h + 1) * W + 4 + x) * 4; o[d] = 160; o[d + 1] = 30; o[d + 2] = 30; }
            y0 += it.h + 4;
        }
        Png.Write(a[2], W, H, o);
        return 0;
    }
}

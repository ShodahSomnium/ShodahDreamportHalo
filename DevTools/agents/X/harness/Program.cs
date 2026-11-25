using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static class Program
{
    public const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";

    static int Main(string[] a)
    {
        if (a[0] == "lib") { Lib.Run(Maps); return 0; }
        var m = new HaloMap(Maps + a[1] + ".map");
        switch (a[0])
        {
            case "count":
                foreach (var c in new[] { "effe", "part", "pctl", "cont", "ligh", "deca", "lens", "foot", "matg", "proj", "weap" })
                    Console.WriteLine(c + " " + m.OfClass(c).Count());
                return 0;
            case "list":
                foreach (var t in m.OfClass(a[2])) if (a.Length < 4 || t.Name.Contains(a[3])) Console.WriteLine(t.Id.ToString("X8") + " " + t.Name);
                return 0;
            case "effe":
                foreach (var t in m.OfClass("effe")) if (t.Name.Contains(a[2])) DumpEffect(t);
                return 0;
            case "part":
                foreach (var t in m.OfClass("part")) if (t.Name.Contains(a[2])) DumpParticle(t);
                return 0;
            case "cont":
                foreach (var t in m.OfClass("cont")) if (t.Name.Contains(a[2])) DumpContrail(t);
                return 0;
            case "ligh":
                foreach (var t in m.OfClass("ligh")) if (t.Name.Contains(a[2])) DumpLight(t);
                return 0;
            case "deca":
                foreach (var t in m.OfClass("deca")) if (t.Name.Contains(a[2])) DumpDecal(t);
                return 0;
            case "survey":
            {
                var cl = new System.Collections.Generic.Dictionary<string,int>(); var lo = new System.Collections.Generic.Dictionary<string,int>();
                foreach (var f in System.IO.Directory.GetFiles(Maps, "*.map")) { var n = System.IO.Path.GetFileName(f); if (n == "bitmaps.map" || n == "sounds.map" || n == "ui.map" || n.Length < 6) continue; Survey.Run(new HaloMap(f), cl, lo); }
                foreach (var kv in cl) Console.WriteLine("class " + kv.Key + " " + kv.Value);
                foreach (var kv in lo) Console.WriteLine("loc \"" + kv.Key + "\" " + kv.Value);
                return 0;
            }
            case "pctl":
                foreach (var t in m.OfClass("pctl")) if (t.Name.Contains(a[2])) Pctl.Dump(t);
                return 0;
            case "spr":
                foreach (var t in m.OfClass("bitm")) if (t.Name.Contains(a[2])) Spr.Dump(t);
                return 0;
            case "att":
                Att.Dump(m, a[2]);
                return 0;
            case "lens":
                foreach (var t in m.OfClass("lens")) if (t.Name.Contains(a[2])) DumpLens(t);
                return 0;
            case "objm":
                foreach (var t in m.OfClass(a[2])) if (a.Length < 4 || t.Name.Contains(a[3])) { var r = t.Root; Console.WriteLine(t.Name + ": model " + T(r.Ref(40)) + " anim " + T(r.Ref(56)) + " modshader " + T(r.Ref(144)) + " create " + T(r.Ref(160)) + " fns a " + r.U16(264) + " b " + r.U16(266)); }
                return 0;
            case "widg":
                foreach (var t in m.OfClass(a[2])) { var w = t.Root.Block(332, 32).Select(x => T(x.Ref(0))).ToList(); if (w.Count > 0) Console.WriteLine(t.Name + ": " + string.Join("; ", w)); }
                return 0;
            case "mgs2":
                foreach (var t in m.OfClass("mgs2")) { var r = t.Root; Console.WriteLine($"== {t.Name}: marker '{r.Str32(0)}' flags {r.U16(34):X} fade {r.F32(52)}..{r.F32(56)} perp {r.F32(60)} par {r.F32(64)} bsrc {r.U16(68)} map {T(r.Ref(92))} seq {r.I16(108)} count {r.I16(110)} fsrc {r.U16(184)}"); foreach (var f in r.Block(288, 176)) Console.WriteLine($"   frame off {f.F32(16)} offexp {f.F32(20)} len {f.F32(24)} rad {f.F32(60)}..{f.F32(64)} radexp {f.F32(68)} tint ({f.F32(104):0.###},{f.F32(108):0.###},{f.F32(112):0.###},{f.F32(116):0.###})-({f.F32(120):0.###},{f.F32(124):0.###},{f.F32(128):0.###},{f.F32(132):0.###}) texp {f.F32(136)} bexp {f.F32(140)}"); }
                return 0;
            case "matgr":
                foreach (var t in m.OfClass("matg")) foreach (var rd in t.Root.Block(308, 428)) for (int o = 0; o <= 300; o += 16) { if (o == 112 || o == 128 || o == 144 || o == 160) continue; Console.WriteLine(o + " " + T(rd.Ref(o))); }
                return 0;
            case "pix":
                { var bt = m.OfClass("bitm").First(x => x.Name.Contains(a[2])); var bm = new HaloBitmap(bt); var img = bm.Images[0]; var res = HaloResources.Get(Maps + "bitmaps.map"); var px = HaloBitmap.DecodeRgba(img, HaloBitmap.Raw(m, img, res)); for (int x = 0; x <= img.Width / 2; x += 2) { int o = ((img.Height / 2) * img.Width + x) * 4; Console.WriteLine($"x {x}: {px[o]} {px[o+1]} {px[o+2]} {px[o+3]}"); } }
                return 0;
            case "asurv": AttSurv.Run(m); return 0;
            case "fpcam": FpCam.Run(m, a[2], a[3]); return 0;
            case "psurv": PartSurv.Run(m); return 0;
            case "pphy":
                foreach (var t in m.OfClass("pphy")) { var r = t.Root; Console.WriteLine($"{t.Name}: flags {r.U32(0):X} mass {r.F32(4)} wgrav {r.F32(8)} agrav {r.F32(12)} dens {r.F32(32)} afric {r.F32(36)} wfric {r.F32(40)} sfric {r.F32(44)} elast {r.F32(48)}"); }
                return 0;
        }
        return 1;
    }

    static string T(HaloTag t) => t == null ? "-" : t.ToString();

    static void DumpEffect(HaloTag t)
    {
        var r = t.Root;
        Console.WriteLine($"== {t}: flags {r.U32(0):X} loop {r.I16(4)}..{r.I16(6)} locations [{string.Join(",", r.Block(40, 32).Select(l => l.Str32(0)))}]");
        int ei = 0;
        foreach (var e in r.Block(52, 68))
        {
            Console.WriteLine($"  event {ei++}: skip {e.F32(4)} delay {e.F32(8)}..{e.F32(12)} duration {e.F32(16)}..{e.F32(20)}");
            foreach (var p in e.Block(44, 104))
                Console.WriteLine($"    part {HaloMap.FourCC(p.U32(20))} {T(p.Ref(24))} in {p.U16(0)} loc {p.I16(4)} flags {p.U16(6):X} vel {p.F32(64)}..{p.F32(68)} cone {p.F32(72)} radmod {p.F32(84)}..{p.F32(88)} A {p.U32(96):X} B {p.U32(100):X}");
            foreach (var p in e.Block(56, 232))
                Console.WriteLine($"    particle {T(p.Ref(84))} create {p.U16(4)} loc {p.I16(8)} dir ({p.F32(12):0.##},{p.F32(16):0.##}) off ({p.F32(20):0.##},{p.F32(24):0.##},{p.F32(28):0.##}) dvec ({p.F32(32):0.##},{p.F32(36):0.##},{p.F32(40):0.##}) flags {p.U32(100):X} dist {p.U16(104)} count {p.I16(108)}..{p.I16(110)} drad {p.F32(112):0.###}..{p.F32(116):0.###} vel {p.F32(132):0.###}..{p.F32(136):0.###} cone {p.F32(140):0.##} angv {p.F32(144):0.##}..{p.F32(148):0.##} rad {p.F32(160):0.###}..{p.F32(164):0.###} tint ({p.F32(176):0.##},{p.F32(180):0.##},{p.F32(184):0.##},{p.F32(188):0.##})-({p.F32(192):0.##},{p.F32(196):0.##},{p.F32(200):0.##},{p.F32(204):0.##}) A {p.U32(224):X} B {p.U32(228):X}");
        }
    }

    static void DumpParticle(HaloTag t)
    {
        var r = t.Root;
        var bm = r.Ref(4);
        string bms = "-";
        if (bm != null) { var b = new HaloBitmap(bm); bms = bm.Name + " type " + b.BitmapType + " imgs " + b.Images.Count + " seqs " + b.Sequences.Count + " [" + string.Join(" ", b.Sequences.Select(s => s.First + "+" + s.Count + "/" + s.Sprites.Count)) + "] img0 " + (b.Images.Count > 0 ? b.Images[0].Width + "x" + b.Images[0].Height + " " + b.Images[0].Format : ""); }
        Console.WriteLine($"== {t}: flags {r.U32(0):X} bitmap {bms} phys {T(r.Ref(20))} life {r.F32(56)}..{r.F32(60)} fade {r.F32(64)}/{r.F32(68)} coll {T(r.Ref(72))} death {T(r.Ref(88))} minsize {r.F32(104)} radanim {r.F32(116)}..{r.F32(120)} anim {r.F32(128)}..{r.F32(132)} fadesize {r.F32(140)}/{r.F32(144)} seq {r.I16(152)} {r.I16(154)}/{r.I16(156)}/{r.I16(158)} sprite {r.F32(168)} orient {r.U16(172)} shtype {r.U16(212)} shflags {r.U16(214 + 2):X} blend {r.U16(218)} fade {r.U16(220)} bmflags {r.U16(222)} sec {T(r.Ref(252))} anchor {r.U16(268)} uv {r.U16(272)}/{r.U16(274)} {r.F32(276)} {r.F32(284)} rot {r.U16(304)}/{r.U16(306)} {r.F32(308)} {r.F32(316)}");
    }

    static void DumpContrail(HaloTag t)
    {
        var r = t.Root;
        var bm = r.Ref(48);
        string bms = "-";
        if (bm != null) { var b = new HaloBitmap(bm); bms = bm.Name + " type " + b.BitmapType + " imgs " + b.Images.Count + " seqs " + b.Sequences.Count + " [" + string.Join(" ", b.Sequences.Select(s => s.First + "+" + s.Count + "/" + s.Sprites.Count)) + "]"; }
        Console.WriteLine($"== {t}: flags {r.U16(0):X} scale {r.U16(2):X} rate {r.F32(4)} vel {r.F32(8)}..{r.F32(12)} cone {r.F32(16)} inh {r.F32(20)} render {r.U16(24)} rep {r.F32(28)}x{r.F32(32)} anim {r.F32(36)},{r.F32(40)} rate {r.F32(44)} bitmap {bms} seq {r.I16(64)}+{r.I16(66)} blend {r.U16(174)} fade {r.U16(176)} sec {T(r.Ref(208))}");
        foreach (var s in r.Block(312, 104))
            Console.WriteLine($"   state dur {s.F32(0)}..{s.F32(4)} trans {s.F32(8)}..{s.F32(12)} phys {T(s.Ref(16))} width {s.F32(64)} col ({s.F32(68):0.##},{s.F32(72):0.##},{s.F32(76):0.##},{s.F32(80):0.##})-({s.F32(84):0.##},{s.F32(88):0.##},{s.F32(92):0.##},{s.F32(96):0.##}) scale {s.U32(100):X}");
    }

    static void DumpLight(HaloTag t)
    {
        var r = t.Root;
        Console.WriteLine($"== {t}: angles {r.F32(16):0.###}/{r.F32(20):0.###} lfonly {r.F32(24)} cos {r.F32(28):0.###}/{r.F32(32):0.###} flags {r.U32(0):X} radius {r.F32(4)} mod {r.F32(8)}..{r.F32(12)} col ({r.F32(56):0.##},{r.F32(60):0.##},{r.F32(64):0.##},{r.F32(68):0.##})-({r.F32(72):0.##},{r.F32(76):0.##},{r.F32(80):0.##},{r.F32(84):0.##}) lens {T(r.Ref(172))} intensity {r.F32(212)} color ({r.F32(216):0.##},{r.F32(220):0.##},{r.F32(224):0.##}) duration {r.F32(244)} falloff {r.U16(250)}");
    }

    static void DumpLens(HaloTag t)
    {
        var r = t.Root;
        var bm = r.Ref(32);
        string bms = "-";
        if (bm != null) { var b = new HaloBitmap(bm); bms = bm.Name + " type " + b.BitmapType + " imgs " + b.Images.Count + " seqs " + b.Sequences.Count + " [" + string.Join(" ", b.Sequences.Select(s => s.First + "+" + s.Count + "/" + s.Sprites.Count)) + "] img0 " + (b.Images.Count > 0 ? b.Images[0].Width + "x" + b.Images[0].Height + " " + b.Images[0].Format : ""); }
        Console.WriteLine($"== {t}: fall {r.F32(0)} cut {r.F32(4)} cos {r.F32(8)}/{r.F32(12)} occl {r.F32(16)} dir {r.U16(20)} near {r.F32(24)} far {r.F32(28)} flags {r.U16(48):X} rotfn {r.U16(128)} rotscale {r.F32(132)} hscale {r.F32(160)} vscale {r.F32(164)} bitmap {bms}");
        foreach (var f in r.Block(196, 128))
            Console.WriteLine($"   refl flags {f.U16(0):X} bm {f.I16(4)} pos {f.F32(28)} rotoff {f.F32(32)} radius {f.F32(40)}..{f.F32(44)} rscaled {f.U16(48)} bright {f.F32(52)}..{f.F32(56)} bscaled {f.U16(60)} tint ({f.F32(64):0.##},{f.F32(68):0.##},{f.F32(72):0.##},{f.F32(76):0.##}) lo ({f.F32(80):0.##},{f.F32(84):0.##},{f.F32(88):0.##},{f.F32(92):0.##}) hi ({f.F32(96):0.##},{f.F32(100):0.##},{f.F32(104):0.##},{f.F32(108):0.##}) more {f.U16(112):X} anim {f.U16(114)} {f.F32(116)} {f.F32(120)}");
    }

    static void DumpDecal(HaloTag t)
    {
        var r = t.Root;
        var bm = r.Ref(216);
        string bms = "-";
        if (bm != null) { var b = new HaloBitmap(bm); bms = bm.Name + " type " + b.BitmapType + " imgs " + b.Images.Count + " seqs " + b.Sequences.Count + " [" + string.Join(" ", b.Sequences.Select(s => s.First + "+" + s.Count + "/" + s.Sprites.Count)) + "] img0 " + (b.Images.Count > 0 ? b.Images[0].Width + "x" + b.Images[0].Height + " " + b.Images[0].Format : ""); }
        Console.WriteLine($"== {t}: flags {r.U16(0):X} type {r.U16(2)} layer {r.U16(4)} next {T(r.Ref(8))} radius {r.F32(24)}..{r.F32(28)} int {r.F32(44)}..{r.F32(48)} col ({r.F32(52):0.##},{r.F32(56):0.##},{r.F32(60):0.##})-({r.F32(64):0.##},{r.F32(68):0.##},{r.F32(72):0.##}) anim {r.I16(88)}/{r.I16(90)} life {r.F32(120)}..{r.F32(124)} decay {r.F32(128)}..{r.F32(132)} blend {r.U16(192)} map {bms} extent {r.F32(252)}");
    }
}
static class Survey
{
    public static void Run(HaloMap m, System.Collections.Generic.Dictionary<string,int> classes, System.Collections.Generic.Dictionary<string,int> locs)
    {
        foreach (var t in m.OfClass("effe"))
        {
            var r = t.Root;
            foreach (var l in r.Block(40, 32)) { string n = l.Str32(0); locs[n] = locs.TryGetValue(n, out var c) ? c + 1 : 1; }
            foreach (var e in r.Block(52, 68))
                foreach (var p in e.Block(44, 104)) { string k = HaloMap.FourCC(p.U32(20)); var tg = p.Ref(24); if (tg != null) k += ":" + tg.ClassName; classes[k] = classes.TryGetValue(k, out var c) ? c + 1 : 1; }
        }
    }
}
static class Pctl
{
    static string T(HaloTag t) => t == null ? "-" : t.ToString();
    public static void Dump(HaloTag t)
    {
        var r = t.Root;
        Console.WriteLine($"== {t}: phys {T(r.Ref(56))} upd {r.U16(72)} consts [{string.Join(",", r.Block(80, 4).Select(k => k.F32(0)))}]");
        foreach (var ty in r.Block(92, 128))
        {
            Console.WriteLine($"  type '{ty.Str32(0)}' flags {ty.U32(32):X} initial {ty.U16(36)} render {ty.U16(40)} radius {ty.F32(44)} creation {ty.U16(84)} consts [{string.Join(",", ty.Block(92, 4).Select(k => k.F32(0)))}]");
            foreach (var s in ty.Block(104, 192))
                Console.WriteLine($"    state '{s.Str32(0)}' dur {s.F32(32)}..{s.F32(36)} trans {s.F32(40)}..{s.F32(44)} scale {s.F32(52)} anim {s.F32(56)} rot {s.F32(60)} col ({s.F32(64):0.##},{s.F32(68):0.##},{s.F32(72):0.##},{s.F32(76):0.##}) radius {s.F32(80)} mincount {s.F32(84)} rate {s.F32(88)} cphys {s.U16(176)} uphys {s.U16(178)} consts [{string.Join(",", s.Block(180, 4).Select(k => k.F32(0)))}]");
            foreach (var p in ty.Block(116, 376))
                Console.WriteLine($"    pstate '{p.Str32(0)}' dur {p.F32(32)}..{p.F32(36)} trans {p.F32(40)}..{p.F32(44)} bitmap {T(p.Ref(48))} seq {p.I16(64)} scale {p.F32(72)}..{p.F32(76)} anim {p.F32(80)}..{p.F32(84)} rot {p.F32(88)}..{p.F32(92)} c1 ({p.F32(96):0.##},{p.F32(100):0.##},{p.F32(104):0.##},{p.F32(108):0.##}) c2 ({p.F32(112):0.##},{p.F32(116):0.##},{p.F32(120):0.##},{p.F32(124):0.##}) radmul {p.F32(128)} phys {T(p.Ref(132))} shflags {p.U16(224):X} blend {p.U16(226)} fade {p.U16(228)} sec {T(p.Ref(260))} anchor {p.U16(276)} consts [{string.Join(",", p.Block(364, 4).Select(k => k.F32(0)))}]");
        }
    }
}
static class Spr
{
    public static void Dump(HaloTag t)
    {
        var b = new HaloBitmap(t);
        Console.WriteLine($"== {t} type {b.BitmapType} usage {b.Usage} imgs {string.Join(" ", b.Images.Select(i => i.Width + "x" + i.Height + "/" + i.Format + " reg(" + i.RegX + "," + i.RegY + ")"))}");
        foreach (var s in b.Sequences)
        {
            Console.WriteLine($"  seq '{s.Name}' first {s.First} count {s.Count}");
            foreach (var p in s.Sprites) Console.WriteLine($"    img {p.Bitmap} l {p.Left:0.###} r {p.Right:0.###} t {p.Top:0.###} b {p.Bottom:0.###} reg ({p.RegX:0.###},{p.RegY:0.###})");
        }
    }
}
static class Lib
{
    public static void Run(string maps)
    {
        foreach (var f in System.IO.Directory.GetFiles(maps, "*.map"))
        {
            var n = System.IO.Path.GetFileName(f);
            if (n == "bitmaps.map" || n == "sounds.map" || n == "ui.map") continue;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var m = new HaloMap(f);
            var lib = new HaloFxLibrary(m);
            int got = lib.PreloadAll();
            int missE = 0, missP = 0, missPart = 0;
            foreach (var t in m.OfClass("effe")) if (lib.Effect(t) == null) missE++;
            foreach (var t in m.OfClass("part")) if (lib.Particle(t) == null) missPart++;
            foreach (var t in m.OfClass("pctl")) if (lib.System(t) == null) missP++;
            int nc = 0; foreach (var t in m.OfClass("cont")) if (lib.Contrail(t) == null) nc++;
            int nd = 0; foreach (var t in m.OfClass("deca")) if (lib.Decal(t) == null) nd++;
            Console.WriteLine($"{n,-22} parsed {got,4} in {sw.ElapsedMilliseconds,4} ms; null effe {missE} part {missPart} pctl {missP} cont {nc} deca {nd}; sheets {lib.Sheets().Count()}");
        }
    }
}
static class Att
{
    public static void Dump(HaloMap m, string cls)
    {
        foreach (var t in m.OfClass(cls))
        {
            var r = t.Root;
            var list = r.Block(320, 72).Select(a => (a.Ref(0)?.ToString() ?? "-") + " @'" + a.Str32(16) + "'").ToList();
            string extra = cls == "proj" ? $" effect {r.Ref(428)} detonation-started {r.Ref(500)} super {r.Ref(396)}" : "";
            Console.WriteLine($"{t}: {string.Join("; ", list)}{extra}");
        }
    }
}

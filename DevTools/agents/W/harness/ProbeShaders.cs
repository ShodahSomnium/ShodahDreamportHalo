using System;
using System.Linq;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static string N(HaloTag t) => t == null ? "-" : t.Name.Substring(t.Name.LastIndexOf((char)92) + 1);
    static int Shaders(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        string only = a.Length > 2 ? a[2] : null;
        foreach (var t in m.Tags.Where(t => t.Is("shdr") && t.ClassName != "shdr"))
        {
            if (only != null && t.ClassName != only && !t.Name.Contains(only)) continue;
            var d = HaloShaderDef.Of(t);
            string s = $"{t.ClassName} {t.Name}: ";
            if (d.Environment != null) { var e = d.Environment; s += $"type {e.Type} fl {e.Flags} base {N(e.BaseMap)} det{e.DetailFunction} {N(e.PrimaryDetail)}x{e.PrimaryDetailScale} {N(e.SecondaryDetail)}x{e.SecondaryDetailScale} micro{e.MicroFunction} {N(e.MicroDetail)}x{e.MicroDetailScale} bump {N(e.BumpMap)}x{e.BumpScale} self {N(e.SelfIllumMap)} spec fl{e.SpecularFlags} br {e.Brightness} perp {e.PerpendicularColor} par {e.ParallelColor} refl t{e.ReflectionType} lmsc {e.LightmapBrightnessScale} pb {e.PerpendicularBrightness} parb {e.ParallelBrightness} cube {N(e.ReflectionCube)} mat {e.MaterialColor}"; }
            if (d.Object != null) { var o = d.Object; s += $"fl {o.Flags} base {N(o.BaseMap)} multi {N(o.MultipurposeMap)} det {N(o.DetailMap)} f{o.DetailFunction} mask{o.DetailMask} x{o.DetailScale} uv {o.UScale},{o.VScale} cc {o.ChangeColorSource} self src {o.ColorSource} {o.AnimLower}-{o.AnimUpper} refl {N(o.ReflectionCube)} pb {o.PerpendicularBrightness} {o.PerpendicularTint} parb {o.ParallelBrightness} {o.ParallelTint} trans {o.Translucency}"; }
            if (d.Transparent != null) { var c = d.Transparent; s += $"fl {c.Flags} first {c.FirstMapType} blend {c.Blend} fade {c.FadeMode} extra {c.ExtraLayers.Count} maps " + string.Join(" | ", c.Maps.Select(mp => $"{N(mp.Map)} c{mp.ColorFunction} a{mp.AlphaFunction} s{mp.UScale},{mp.VScale} f{mp.Flags} ua{mp.UAnim.Function}/{mp.UAnim.Period}/{mp.UAnim.Scale} va{mp.VAnim.Function}/{mp.VAnim.Period}/{mp.VAnim.Scale}")); }
            if (d.Glassy != null) { var g = d.Glassy; s += $"fl {g.Flags} tint {g.BackgroundTint} {N(g.BackgroundTintMap)} refl {N(g.ReflectionMap)} {g.PerpendicularBrightness}/{g.ParallelBrightness} bump {N(g.BumpMap)} diff {N(g.DiffuseMap)} spec {N(g.SpecularMap)}"; }
            if (d.Watery != null) { var w = d.Watery; s += $"fl {w.Flags} base {N(w.BaseMap)} refl {N(w.ReflectionMap)} ripple {N(w.RippleMaps)} {w.PerpendicularBrightness} {w.PerpendicularTint} / {w.ParallelBrightness} {w.ParallelTint} ripples {w.Ripples.Count}"; }
            if (d.Gauge != null) { var g = d.Gauge; s += $"fl {g.Flags} map {N(g.Map)} {g.GradientMin}-{g.GradientMax} bg {g.Background}"; }
            if (d.Energy != null) { var p = d.Energy; s += $"noise {N(p.PrimaryNoise)} {N(p.SecondaryNoise)} perp {p.PerpendicularTint} par {p.ParallelTint}"; }
            Console.WriteLine(s + $" | power {d.Power} emit {d.EmittedLight} mat {d.MaterialType} sfl {d.ShaderFlags}");
        }
        return 0;
    }
    static int ShaderStats(string[] a)
    {
        var cls = new Dictionary<string, int>(); var blends = new Dictionary<string, int>(); var funcs = new Dictionary<string,int>();
        foreach (var name in Mp)
        {
            var m = new HaloMap(Maps + name + ".map");
            foreach (var t in m.Tags.Where(t => t.Is("shdr") && t.ClassName != "shdr"))
            {
                var d = HaloShaderDef.Of(t);
                cls[t.ClassName] = cls.GetValueOrDefault(t.ClassName) + 1;
                if (d.Transparent != null) { string k = t.ClassName + " blend " + d.Transparent.Blend + " first " + d.Transparent.FirstMapType + " maps " + d.Transparent.Maps.Count; blends[k] = blends.GetValueOrDefault(k) + 1;
                    foreach (var mp in d.Transparent.Maps) { string f = "c" + mp.ColorFunction + " a" + mp.AlphaFunction; funcs[f] = funcs.GetValueOrDefault(f) + 1; } }
                if (d.Environment != null) { string k = "senv type " + d.Environment.Type + " det " + d.Environment.DetailFunction + " micro " + d.Environment.MicroFunction + " refl " + d.Environment.ReflectionType + (d.Environment.ReflectionCube != null ? " cube" : "") + (d.Environment.BumpMap != null ? " bump" : ""); blends[k] = blends.GetValueOrDefault(k) + 1; }
            }
        }
        foreach (var kv in cls.OrderBy(k => k.Key)) Console.WriteLine(kv.Key + " " + kv.Value);
        foreach (var kv in blends.OrderBy(k => k.Key)) Console.WriteLine("  " + kv.Key + ": " + kv.Value);
        foreach (var kv in funcs.OrderByDescending(k => k.Value)) Console.Write(kv.Key + ":" + kv.Value + "  ");
        Console.WriteLine();
        return 0;
    }
}

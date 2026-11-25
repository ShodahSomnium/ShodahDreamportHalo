using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Light(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        var b = new HaloBsp(sc, 0);
        Console.WriteLine($"default ambient {b.DefaultAmbient} l0 {b.DefaultLight0Color} {b.DefaultLight0Dir} l1 {b.DefaultLight1Color} {b.DefaultLight1Dir} shadow {b.DefaultShadowDir} {b.DefaultShadowColor}");
        foreach (var s in b.Sections.Take(int.Parse(a.Length > 2 ? a[2] : "20")))
            Console.WriteLine($"{s.Shader?.Name,-60} lm {s.Lightmap,3} fl {s.Flags} amb {s.Ambient} n {s.DistantLights} l0 {s.Light0Color} {s.Light0Dir} l1 {s.Light1Color} {s.Light1Dir} sh {s.ShadowDir} {s.ShadowColor} tint {s.ReflectionTintA:0.##},{s.ReflectionTintR:0.##},{s.ReflectionTintG:0.##},{s.ReflectionTintB:0.##}");
        return 0;
    }
}

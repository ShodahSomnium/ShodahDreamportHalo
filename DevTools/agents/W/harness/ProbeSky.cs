using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Sky(string[] a)
    {
        foreach (var name in (a.Length > 1 ? new[] { a[1] } : Mp))
        {
            var m = new HaloMap(Maps + name + ".map");
            var sc = new HaloScenario(m);
            foreach (var t in sc.Skies)
            {
                var s = new HaloSky(t);
                Console.WriteLine($"{name} {t.Name}: model {s.Model?.Name} anim {s.AnimationGraph?.Name} amb in {s.IndoorAmbient}x{s.IndoorAmbientPower} out {s.OutdoorAmbient}x{s.OutdoorAmbientPower} fog out {s.OutdoorFogColor} d{s.OutdoorFogDensity} {s.OutdoorFogStart}-{s.OutdoorFogOpaque} in {s.IndoorFogColor} d{s.IndoorFogDensity} {s.IndoorFogStart}-{s.IndoorFogOpaque} screen {s.IndoorFogScreen?.Name} anims {s.Animations.Count} lights " + string.Join(" | ", s.Lights.Select(l => $"{l.LensFlare?.Name} fl{l.Flags} {l.Color}x{l.Power} y{l.Yaw:0.00} p{l.Pitch:0.00}")));
            }
            var b = new HaloBsp(sc, 0);
            foreach (var f in b.FogPalette) Console.WriteLine($"   fog palette {f.Name} {f.Fog?.Name} fn '{f.ScaleFunction}'");
            foreach (var f in b.FogPlanes) Console.WriteLine($"   fog plane region {f.FrontRegion} n ({f.Nx:0.00},{f.Ny:0.00},{f.Nz:0.00}) d {f.D:0.00} verts {f.Vertices.Length}");
            foreach (var f in b.FogRegions) Console.WriteLine($"   fog region fog {f.Fog} weather {f.Weather}");
        }
        return 0;
    }
}

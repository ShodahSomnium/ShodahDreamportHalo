using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Placed(string[] a)
    {
        foreach (var name in (a.Length > 1 ? new[] { a[1] } : Mp))
        {
            var m = new HaloMap(Maps + name + ".map");
            var sc = new HaloScenario(m);
            Console.WriteLine($"{name}: scenery {sc.Scenery.Count} machines {sc.Machines.Count} controls {sc.Controls.Count} lights {sc.LightFixtures.Count} sound {sc.SoundScenery.Count}");
            foreach (var g in sc.Machines.GroupBy(p => p.Type?.Name)) Console.WriteLine($"   mach {g.Key} x{g.Count()} type {(g.First().Type != null ? new HaloObjectDef(g.First().Type).Type : -1)} anim {(g.First().Type != null ? new HaloObjectDef(g.First().Type).AnimationGraph?.Name : "")}");
            foreach (var g in sc.SoundScenery.GroupBy(p => p.Type?.Name)) Console.WriteLine($"   ssce {g.Key} x{g.Count()}");
            foreach (var g in sc.LightFixtures.GroupBy(p => p.Type?.Name)) Console.WriteLine($"   lifi {g.Key} x{g.Count()}");
            foreach (var g in sc.Controls.GroupBy(p => p.Type?.Name)) Console.WriteLine($"   ctrl {g.Key} x{g.Count()}");
            int np = sc.Scenery.Count(p => (p.NotPlaced & 1) != 0);
            Console.WriteLine($"   scenery not placed automatically: {np}; with desired permutation: {sc.Scenery.Count(p => p.DesiredPermutation > 0)}");
        }
        return 0;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static class Program
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static int Main(string[] a)
    {
        var names = a.Length > 1 && a[1] != "all" ? new[] { a[1] } : HaloInstall.Open(@"C:\Program Files (x86)\Microsoft Games\Halo").Multiplayer.Select(m => m.Name).ToArray();
        foreach (var n in names) Dump(n, a[0]);
        return 0;
    }

    static void Dump(string name, string what)
    {
        var m = new HaloMap(Maps + name + ".map");
        var s = new HaloScenario(m);
        Console.WriteLine("=== " + name);
        if (what == "mp" || what == "full")
        {
            var g = m.OfClass("matg").First();
            Console.WriteLine("matg " + g.Name);
            foreach (var mp in g.Root.Block(356, 160))
            {
                Console.WriteLine("  flag " + mp.Ref(0) + " unit " + mp.Ref(16) + " ball " + mp.Ref(76) + " hill shader " + mp.Ref(44) + " flag shader " + mp.Ref(60));
                int i = 0; foreach (var v in mp.Block(32, 16)) Console.WriteLine("  vehicle " + v.Ref(0));
                foreach (var so in mp.Block(92, 16)) Console.WriteLine("  sound " + (i++) + " " + so.Ref(0));
            }
        }
        var types = new[] { HaloScenario.GameType.Slayer, HaloScenario.GameType.Ctf, HaloScenario.GameType.King, HaloScenario.GameType.Oddball, HaloScenario.GameType.Race };
        Console.WriteLine("starts " + s.StartLocations.Count + ": " + string.Join(" ", types.Select(t => t + "=" + s.StartLocations.Count(l => l.Allows(t)) + "(r" + s.StartLocations.Count(l => l.Allows(t) && l.Team == 0) + "/b" + s.StartLocations.Count(l => l.Allows(t) && l.Team == 1) + ")")));
        Console.WriteLine("teams in starts: " + string.Join(",", s.StartLocations.Select(l => l.Team).Distinct()) + " bsps " + string.Join(",", s.StartLocations.Select(l => l.BspIndex).Distinct()));
        foreach (var grp in s.Flags.GroupBy(f => f.Type)) Console.WriteLine("  flags " + grp.Key + ": " + string.Join(" ", grp.Select(f => "u" + f.UsageId + (f.WeaponGroup != null ? "[" + f.WeaponGroup.Name + "]" : "") + f.Position)));
        if (what == "full")
        {
            foreach (var l in s.StartLocations) Console.WriteLine($"  start {l.Position} facing {l.Facing:0.00} team {l.Team} bsp {l.BspIndex} types {string.Join("/", l.Types)}");
            float minZ = s.StartLocations.Min(l => l.Position.Z), maxZ = s.StartLocations.Max(l => l.Position.Z);
            Console.WriteLine($"  start z {minZ:0.00}..{maxZ:0.00}");
        }
    }
}

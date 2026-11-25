using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    public static int Scen(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var s = new HaloScenario(m);
        Console.WriteLine($"skies {s.Skies.Count} ({string.Join(", ", s.Skies)}) bsps {s.Bsps.Count}");
        foreach (var b in s.Bsps)
        {
            Console.WriteLine($"  bsp {b.Tag} file {b.FileOffset:X} size {b.Size:X} addr {b.Address:X}");
            var head = string.Join(" ", Enumerable.Range(0, 8).Select(i => m.U32(b.FileOffset + i * 4).ToString("X8")));
            Console.WriteLine("    head " + head + " fourcc " + HaloMap.FourCC(m.U32(b.FileOffset + 12)));
        }
        Console.WriteLine($"scenery {s.Scenery.Count} vehicles {s.Vehicles.Count} weapons {s.Weapons.Count} equipment {s.Equipment.Count} machines {s.Machines.Count} controls {s.Controls.Count} lights {s.LightFixtures.Count} sound scenery {s.SoundScenery.Count}");
        Console.WriteLine($"starts {s.StartLocations.Count} flags {s.Flags.Count} net equipment {s.NetEquipment.Count} starting {s.Starting.Count}");
        foreach (var v in s.Vehicles) Console.WriteLine($"  vehicle {v} team {v.Team} mp {v.MpSpawnFlags:X4}");
        foreach (var f in s.Flags) Console.WriteLine($"  flag {f.Type} {f.Position} use {f.UsageId} {f.WeaponGroup}");
        foreach (var e in s.NetEquipment.Take(40)) Console.WriteLine($"  equip {e.ItemCollection} {e.Position} types {string.Join("/", e.Types)} spawn {e.SpawnTime} lev {e.Levitate}");
        foreach (var e in s.Starting) Console.WriteLine($"  starting {string.Join("/", e.Types)} {string.Join(", ", e.Collections.Where(c => c != null))} flags {e.Flags}");
        foreach (var l in s.StartLocations.Take(6)) Console.WriteLine($"  start {l.Position} facing {l.Facing:0.00} team {l.Team} types {string.Join("/", l.Types)}");
        foreach (var x in s.Scenery.Take(8)) Console.WriteLine($"  scenery {x} rot {x.Rotation}");
        return 0;
    }
}

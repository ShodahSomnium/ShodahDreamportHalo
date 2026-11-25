using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class NetProbe
{
    public const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static int Main(string[] a)
    {
        HaloLog.Sink = Console.WriteLine;
        switch (a.Length > 0 ? a[0] : "")
        {
            case "counts": return Counts();
            case "net": return NetTests.Run(a);
            case "weapons": return Weapons(a[1]);
        }
        Console.WriteLine("commands: counts | net");
        return 1;
    }

    static int Counts()
    {
        var inst = HaloInstall.Find(@"C:\Program Files (x86)\Microsoft Games\Halo");
        foreach (var e in inst.Multiplayer)
        {
            var m = new HaloMap(e.File);
            var s = new HaloScenario(m);
            Console.WriteLine($"{e.Name,-14} equip {s.NetEquipment.Count,3} vehicles {s.Vehicles.Count,3} weapons {s.Weapons.Count,3} flags {s.Flags.Count,3} ({string.Join(",", s.Flags.GroupBy(f => f.Type).Select(g => g.Key + ":" + g.Count()))}) tags {m.Tags.Length} weap {m.OfClass("weap").Count()} vehi {m.OfClass("vehi").Count()}");
        }
        return 0;
    }
}

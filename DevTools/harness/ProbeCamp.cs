using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    public static int Camp(string[] a)
    {
        var inst = HaloInstall.Open(@"C:\Program Files (x86)\Microsoft Games\Halo");
        foreach (var e in inst.Campaign)
        {
            var m = new HaloMap(e.File);
            var sc = new HaloScenario(m);
            var r = sc.Tag.Root;
            int squads = 0, actors = 0;
            foreach (var enc in r.Block(1068, 176)) foreach (var sq in enc.Block(128, 232)) { squads++; actors += sq.Block(208, 28).Count(); }
            Console.WriteLine($"{e.Name,-4} {e.Label,-32} {e.Size / 1048576,4} MB bsps {sc.Bsps.Count} scripts {r.Count(1180)} globals {r.Count(1192)} syntax {r.DataSize(1140)} B strings {r.DataSize(1160)} B actor palette {r.Count(1056)} encounters {r.Count(1068)} squads {squads} actor starts {actors} cmdlists {r.Count(1080)} recorded {r.Count(876)} cutscene cams {r.Count(1264)} bipd tags {m.OfClass("bipd").Count()} actv {m.OfClass("actv").Count()} devices m{sc.Machines.Count}/c{sc.Controls.Count} vehicles {sc.Vehicles.Count} weapons {sc.Weapons.Count}");
            if (a.Length > 1 && a[1] == e.Name) foreach (var t in m.OfClass("actv")) Console.WriteLine("    " + t.Name);
        }
        return 0;
    }
}

using System;
using SomniumSpace.Worlds.SomniumHalo;
static class Extra4
{
    public static void Run(string name, float x, float y, float z, float r)
    {
        var m = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\" + name + ".map");
        var sc = new HaloScenario(m);
        void L(string k, System.Collections.Generic.List<HaloScenario.Placement> l)
        {
            foreach (var p in l) { float dx = p.Position.X - x, dy = p.Position.Y - y, dz = p.Position.Z - z; if (dx * dx + dy * dy + dz * dz < r * r) Console.WriteLine($"  {k} {p}"); }
        }
        L("scen", sc.Scenery); L("mach", sc.Machines); L("vehi", sc.Vehicles);
    }
}

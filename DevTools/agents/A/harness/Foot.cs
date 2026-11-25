using System;
using SomniumSpace.Worlds.SomniumHalo;
static class Foot
{
    // foot <map> [name]: material effects (foot) tags: per effect index, per material type -> effect / sound
    public static int Run(string[] a)
    {
        var m = new HaloMap(MainA.Maps + a[1] + ".map");
        string want = a.Length > 2 ? a[2] : "cyborg";
        foreach (var t in m.OfClass("foot"))
        {
            if (t.Name.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0) continue;
            Console.WriteLine(t.Name);
            int ei = 0;
            foreach (var e in t.Root.Block(0, 28))
            {
                int mi = 0;
                foreach (var mt in e.Block(0, 48))
                {
                    var fx = mt.Ref(0); var sn = mt.Ref(16);
                    if (fx != null || sn != null) Console.WriteLine($"  effect {ei,2} material {mi,2}: fx {(fx != null ? fx.Name : "-")}  snd {(sn != null ? sn.Name : "-")}");
                    mi++;
                }
                Console.WriteLine($"  effect {ei,2}: {e.Count(0)} materials");
                ei++;
            }
        }
        // the MP biped
        foreach (var t in m.OfClass("bipd")) { var f = t.Root.Ref(908); Console.WriteLine($"bipd {t.Name}: footsteps {(f != null ? f.Name : "none")}"); }
        return 0;
    }
}

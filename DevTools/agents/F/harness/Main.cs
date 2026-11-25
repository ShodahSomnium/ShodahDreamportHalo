using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class F
{
    public const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static int Main(string[] a)
    {
        switch (a[0])
        {
            case "tags":
            {
                var m = new HaloMap(Maps + a[1] + ".map");
                foreach (var g in m.Tags.GroupBy(t => t.ClassName).OrderByDescending(g => g.Count())) Console.Write($"{g.Key}:{g.Count()} ");
                Console.WriteLine();
                foreach (var t in m.Tags.Where(t => a.Length < 3 || t.ClassName == a[2] || a[2] == "*")) Console.WriteLine($"  {t.Index,5} {t.ClassName} {t.Name}");
                return 0;
            }
        }
        return More(a);
    }
}

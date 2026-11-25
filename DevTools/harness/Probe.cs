using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class Probe
{
    public const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    public static int Run(string[] a)
    {
        switch (a[0])
        {
            case "head":
            {
                var m = new HaloMap(Maps + a[1] + ".map");
                Console.WriteLine($"{m.Name} v{m.Version} build {m.Build} type {m.MapType} tagdata {m.TagDataOffset:X}+{m.TagDataSize:X} tags {m.Tags.Length} scenario {m.Scenario}");
                Console.WriteLine($"model data {m.ModelDataOffset:X} index {m.ModelIndexOffset:X} size {m.ModelDataSize:X} file {m.Data.Length:X}");
                foreach (var g in m.Tags.GroupBy(t => t.ClassName).OrderByDescending(g => g.Count())) Console.Write($"{g.Key}:{g.Count()} ");
                Console.WriteLine();
                Console.WriteLine("indexed: " + m.Tags.Count(t => t.Indexed) + " e.g. " + string.Join(", ", m.Tags.Where(t => t.Indexed).Take(5)));
                if (a.Length > 2) foreach (var t in m.Tags.Where(t => t.ClassName == a[2] || a[2] == "*")) Console.WriteLine($"  {t.Index,5} {t.Id:X8} {t}{(t.Indexed ? " [indexed " + t.Address + "]" : "")}");
                return 0;
            }
        }
        return More(a);
    }
}

using System;
using System.IO;
using System.Text.Json;
static partial class W
{
    // enum/bitfield option lists from the invader defs: defs Name1 Name2 ...
    static int DefsCmd(string[] a)
    {
        string dir = @"D:\Dreamport\Halo\Halo Dreamport\DevTools\reference\invader-defs";
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (!e.TryGetProperty("name", out var n)) continue;
                string name = n.GetString();
                if (Array.IndexOf(a, name) < 1) continue;
                string kind = e.GetProperty("type").GetString();
                var list = e.TryGetProperty("options", out var o) ? o : e.TryGetProperty("fields", out var fl) ? fl : default;
                Console.Write(name + " (" + kind + "): ");
                int i = 0;
                if (list.ValueKind == JsonValueKind.Array) foreach (var x in list.EnumerateArray()) Console.Write((i++) + "=" + (x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()) + "; ");
                Console.WriteLine();
            }
        }
        return 0;
    }
}

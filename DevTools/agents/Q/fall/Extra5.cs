using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static class Extra5
{
    public static void Run(string name)
    {
        var m = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\" + name + ".map");
        var sc = new HaloScenario(m);
        var seen = new Dictionary<string, int>();
        foreach (var p in sc.Machines)
        {
            if (p.Type == null) continue;
            var r = p.Type.Root;
            int type = r.U16(656), flags = r.U16(658);
            if (type != 1 && (flags & 4) == 0) continue;
            string k = p.Type.Name + " type " + (type == 0 ? "door" : type == 1 ? "platform" : "gear") + ((flags & 4) != 0 ? " ELEVATOR" : "") + " node " + r.I16(746);
            int c; seen.TryGetValue(k, out c); seen[k] = c + 1;
        }
        foreach (var kv in seen) Console.WriteLine($"  {name}: {kv.Value} x {kv.Key}");
    }
}

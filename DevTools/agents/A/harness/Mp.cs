using System;
using SomniumSpace.Worlds.SomniumHalo;
static class Mp
{
    public static int Run(string[] a)
    {
        var m = new HaloMap(MainA.Maps + a[1] + ".map");
        var s = HaloMpSounds.Read(m);
        Console.WriteLine($"{a[1]}: {s.Length} multiplayer sounds");
        for (int i = 0; i < s.Length; i++) Console.WriteLine($"  {i,2} {(i < HaloMpSounds.Names.Length ? HaloMpSounds.Names[i] : "?"),-24} {s[i]}");
        return 0;
    }
}

using System;
using SomniumSpace.Worlds.SomniumHalo;
static class Dump { public static int Run(string[] a) { var m = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\" + a[1] + ".map"); foreach (HaloTag t in m.OfClass("vehi")) { if (a.Length > 2 && t.Name.IndexOf(a[2]) < 0) continue; var d = new HaloVehicleDef(t); foreach (var s in d.Describe()) Console.WriteLine(s); } return 0; } }

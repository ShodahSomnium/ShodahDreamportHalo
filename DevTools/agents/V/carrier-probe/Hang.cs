using System;
using System.Globalization;
using SomniumSpace.Worlds.SomniumHalo;
static class Hang { public static int Run(string[] a) {
  var inv = CultureInfo.InvariantCulture; Drop.Load();
  var m = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\a30.map"); HaloVehicleDef d = null; foreach (HaloTag t in m.OfClass("vehi")) if (t.Name.EndsWith("c_dropship")) d = new HaloVehicleDef(t);
  double x = double.Parse(a[1], inv), y = double.Parse(a[2], inv), yaw = double.Parse(a[3], inv) / 57.2958;
  for (double z = double.Parse(a[4], inv); z > 50; z -= 0.1) if (Drop.TouchPub(d, x, y, z, yaw)) { Console.WriteLine($"first touch at z {z:0.0}: {Drop.Why}"); return 0; }
  return 0; } }

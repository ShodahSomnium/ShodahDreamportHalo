using System;
using System.Collections.Generic;
using System.Globalization;
using SomniumSpace.Worlds.SomniumHalo;
// col a30 x y : solid/open runs of the column from z 150 down to 0
static class Col { public static int Run(string[] a) {
  var m = new HaloMap(@"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\" + a[1] + ".map"); var sc = new HaloScenario(m);
  var cols = new List<HaloCollisionBsp>(); for (int i = 0; i < sc.Bsps.Count; i++) { foreach (var cb in sc.BspRoot(i).Block(176, 96)) { cols.Add(new HaloCollisionBsp(cb)); break; } }
  for (int k = 2; k + 1 < a.Length; k += 2) {
   double x = double.Parse(a[k], CultureInfo.InvariantCulture), y = double.Parse(a[k+1], CultureInfo.InvariantCulture);
   string s = $"({x},{y}):"; bool prev = false; for (double z = 150; z > 0; z -= 0.25) { bool open = false; foreach (var c in cols) if (c.Leaf((float)x,(float)y,(float)z) >= 0) open = true; if (z == 150 || open != prev) s += (open ? " open<" : " solid<") + z.ToString("0.0"); prev = open; }
   Console.WriteLine(s); }
  return 0; } }

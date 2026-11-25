using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Nums(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("bipd"))
        {
            var r = t.Root;
            Console.WriteLine($"{t.Name}: maxv {r.F32(820)} side {r.F32(824)} acc {r.F32(828)} dec {r.F32(832)} angv {r.F32(836)} anga {r.F32(840)} crouchmod {r.F32(844)} movturn {r.F32(752)} statturn {r.F32(760)} jump {r.F32(948)} stand h {r.F32(1060)} crouch h {r.F32(1064)} rad {r.F32(1068)} cam {r.F32(1024)}/{r.F32(1028)} softping {r.F32(536)}/{r.F32(540)} hardping {r.F32(544)}/{r.F32(548)} harddeath {r.F32(552)} feign {r.F32(556)}/{r.F32(560)}/{r.F32(580)} evade {r.F32(564)} dive {r.F32(568)} gren {r.F32(704)} type {r.U16(708)} cnt {r.I16(710)} pings {r.I16(712)}/{r.I16(714)} aimv {r.F32(612)} lookv {r.F32(624)} pelvis {r.I16(1252)} head {r.I16(1254)} melee {r.Ref(648)?.Name} footsteps {r.Ref(908)?.Name} spawned {r.Ref(588)?.Name} {r.I16(604)}-{r.I16(606)} accscale {r.F32(32)} landing {r.F32(980)}/{r.F32(984)}/{r.F32(988)}/{r.F32(992)}/{r.F32(996)}/{r.F32(1000)}");
            Console.Write("   unit weapons:"); foreach (var w in r.Block(728, 36)) Console.Write(" " + w.Ref(0)?.Name); Console.WriteLine();
            Console.Write("   change colors:"); foreach (var c in r.Block(356, 44)) { Console.Write($" [{c.F32(8):0.00},{c.F32(12):0.00},{c.F32(16):0.00}-{c.F32(20):0.00},{c.F32(24):0.00},{c.F32(28):0.00} perms {c.Count(32)}]"); } Console.WriteLine();
            var an = r.Ref(56);
            if (an == null) continue;
            var g = new HaloAnimGraph(an);
            var s = g.FindSeat("stand") ?? (g.Seats.Count > 0 ? g.Seats[0] : null);
            if (s != null) foreach (var w in s.Weapons) Console.WriteLine($"   class {w.Name} grip '{w.GripMarker}' hand '{w.HandMarker}' ik {string.Join(",", w.Ik.Select(k => k.Marker + "->" + k.AttachTo))}");
        }
        return 0;
    }
}

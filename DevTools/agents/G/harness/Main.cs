using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class G
{
    public const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static int Main(string[] a)
    {
        HaloLog.Sink = Console.WriteLine;
        var m = new HaloMap(Maps + a[1] + ".map");
        switch (a[0])
        {
            case "weap":
                foreach (var t in m.OfClass("weap"))
                {
                    var r = t.Root;
                    Console.WriteLine($"{t.Name} label '{r.Str32(780)}' type {r.U16(1250)} flags {r.U32(776):X} ready {r.F32(824)} heatRec {r.F32(844)} overheat {r.F32(848)} heatLoss {r.F32(860)} zoom {r.I16(986)} mag {r.F32(988)}-{r.F32(992)} autoaim {r.F32(996)} {r.F32(1000)} melee {r.Ref(916)} model {r.Ref(40)} fp {r.Ref(1116)} pickupIdx {r.I16(384)}");
                    foreach (var g in r.Block(1264, 112))
                        Console.WriteLine($"   mag flags {g.U32(0)} recharged {g.I16(4)} total {g.I16(6)} reservedMax {g.I16(8)} loadedMax {g.I16(10)} reload {g.F32(20)} reloaded {g.I16(24)} chamber {g.F32(28)} fx {g.Ref(56)} {g.Ref(72)} objs {g.Count(100)}");
                    foreach (var g in r.Block(1276, 276))
                    {
                        Console.WriteLine($"   trig flags {g.U32(0):X} rof {g.F32(4)}-{g.F32(8)} acc {g.F32(12)} dec {g.F32(16)} magI {g.I16(32)} rps {g.I16(34)} minLoaded {g.I16(36)} err {g.F32(48)}-{g.F32(52)} charge {g.F32(72)} charged {g.F32(76)} over {g.U16(80)} spew {g.F32(88)} dist {g.U16(108)} pps {g.I16(110)} distAng {g.F32(112)} minErr {g.F32(120)} errAng {g.F32(124)}-{g.F32(128)} proj {g.Ref(148)} heat {g.F32(184)} age {g.F32(188)} overload {g.F32(196)} chargingFx {g.Ref(92)} chargedIllum {g.F32(84)}");
                        foreach (var f in g.Block(264, 132)) Console.WriteLine($"      fx {f.I16(0)}-{f.I16(2)} fire {f.Ref(36)} empty {f.Ref(68)} dmg {f.Ref(84)}");
                    }
                }
                return 0;
            case "proj":
                foreach (var t in m.OfClass("proj"))
                {
                    var r = t.Root;
                    Console.WriteLine($"{t.Name} flags {r.U32(380):X} timerStarts {r.U16(384)} collR {r.F32(416)} arming {r.F32(420)} effect {r.Ref(428)} timer {r.F32(444)}-{r.F32(448)} minV {r.F32(452)} range {r.F32(456)} grav {r.F32(460)} airDmg {r.F32(464)}-{r.F32(468)} v {r.F32(484)}->{r.F32(488)} guided {r.F32(492)} attDmg {r.Ref(532)} impDmg {r.Ref(548)} model {r.Ref(40)} atts {r.Count(320)}");
                    foreach (var x in r.Block(320, 72)) Console.WriteLine($"   attach {x.Ref(0)} marker '{x.Str32(16)}'");
                    int i = 0;
                    foreach (var x in r.Block(576, 160)) { if (i++ < 40 && (a.Length > 2)) Console.WriteLine($"   mat {i - 1} flags {x.U16(0)} resp {x.U16(2)} eff {x.Ref(4)} pot {x.U16(36)} potEff {x.Ref(60)} det {x.Ref(104)}"); }
                    Console.WriteLine($"   materials {r.Count(576)}");
                }
                return 0;
            case "jpt":
                foreach (var t in m.OfClass("jpt!"))
                {
                    var r = t.Root;
                    Console.WriteLine($"{t.Name} radius {r.F32(0)}-{r.F32(4)} cutoff {r.F32(8)} flags {r.U32(12)} cat {r.U16(454)} dflags {r.U32(456):X} core {r.F32(460)} lower {r.F32(464)} upper {r.F32(468)}-{r.F32(472)} camo {r.F32(480)} accel {r.Vec3(500)} cybArmor {r.F32(596)} cybShield {r.F32(600)} metalThick {r.F32(540)}");
                }
                return 0;
            case "eqip":
                foreach (var t in m.OfClass("eqip"))
                {
                    var r = t.Root;
                    Console.WriteLine($"{t.Name} powerup {r.U16(776)} grenade {r.U16(778)} time {r.F32(780)} sound {r.Ref(784)} model {r.Ref(40)} pickupIdx {r.I16(384)} msgScale {r.I16(392)}");
                }
                return 0;
            case "itmc":
                foreach (var t in m.OfClass("itmc"))
                {
                    var r = t.Root;
                    Console.WriteLine($"{t.Name} spawn {r.I16(12)}: " + string.Join(", ", r.Block(0, 84).Select(p => p.Ref(36) + " w" + p.F32(32))));
                }
                return 0;
            case "netequip":
            {
                var s = new HaloScenario(m);
                foreach (var e in s.NetEquipment) Console.WriteLine($"{e.ItemCollection} at {e.Position} spawn {e.SpawnTime} team {e.Team} types {string.Join(",", e.Types)} lev {e.Levitate}");
                foreach (var e in s.Starting) Console.WriteLine($"start flags {e.Flags} types {string.Join(",", e.Types)}: {string.Join(", ", e.Collections.Select(c => c?.ToString() ?? "-"))}");
                return 0;
            }
            case "matg":
            {
                var gt = m.OfClass("matg").First();
                var r = gt.Root;
                Console.WriteLine(gt);
                foreach (var g in r.Block(296, 68)) Console.WriteLine($"grenade max {g.I16(0)} mp {g.I16(2)} throw {g.Ref(4)} hud {g.Ref(20)} eq {g.Ref(36)} proj {g.Ref(52)}");
                foreach (var g in r.Block(356, 160)) Console.WriteLine($"mp flag {g.Ref(0)} unit {g.Ref(16)} ball {g.Ref(76)}");
                foreach (var g in r.Block(368, 244)) Console.WriteLine($"player unit {g.Ref(0)} walk {g.F32(44)} run {g.F32(52)} grenadeOrigin {g.Vec3(104)}");
                foreach (var g in r.Block(392, 152)) Console.WriteLine($"falling harmful {g.F32(8)}-{g.F32(12)} dmg {g.Ref(16)} max {g.F32(40)} dist {g.Ref(44)} maxV {g.F32(140)} harmV {g.F32(144)}-{g.F32(148)} flaming {g.Ref(108)}");
                foreach (var g in r.Block(332, 16)) Console.WriteLine($"weapon list {g.Ref(0)}");
                return 0;
            }
            case "unit":
            {
                foreach (var t in m.Tags.Where(x => x.ClassName == "bipd"))
                {
                    var r = t.Root;
                    var coll = r.Ref(112);
                    Console.WriteLine($"{t.Name} coll {coll} melee {r.Ref(648)} grenadeV {r.F32(704)} gType {r.U16(708)} gCount {r.I16(710)} model {r.Ref(40)}");
                    if (coll != null)
                    {
                        var c = coll.Root;
                        foreach (var w in r.Block(728, 36)) Console.WriteLine($"   unit weapon {w.Ref(0)}");
                        Console.WriteLine($"   minStun {c.F32(264)} body {c.F32(8)} shield {c.F32(204)} shieldMat {c.U16(210)} stunTime {c.F32(268)} recharge {c.F32(272)} rate {c.F32(448)} failThr {c.F32(240)} leak {c.F32(244)} flags {c.U32(0)}");
                        foreach (var x in c.Block(564, 72)) Console.WriteLine($"   mat '{x.Str32(0)}' flags {x.U32(32)} type {x.U16(36)} leak {x.F32(40)} shieldMul {x.F32(44)} bodyMul {x.F32(60)}");
                        foreach (var x in c.Block(576, 84)) Console.WriteLine($"   region '{x.Str32(0)}' flags {x.U32(32)} dmgThr {x.F32(40)}");
                    }
                }
                return 0;
            }
        }
        return Extra(m, a);
    }
}

using System;
using System.Linq;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;

static class P
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static string Sp(HaloAnimGraph g, int idx)
    {
        var a = g.Get(idx); if (a == null) return "-";
        a.Decode();
        float sx=0, sy=0, sz=0, yw=0;
        if (a.Info != null) for (int f = 0; f < a.Frames; f++) { sx += a.Info[f*4]; sy += a.Info[f*4+1]; sz += a.Info[f*4+2]; yw += a.Info[f*4+3]; }
        float d = Math.Max(1e-3f, a.Duration);
        return $"{a.Name}[{a.Type},fr{a.Frames},it{a.FrameInfoType},key{a.KeyFrame}/{a.SecondKeyFrame},loop{a.LoopFrame},nx{a.Next}] v=({sx/d:0.00},{sy/d:0.00},{sz/d:0.00})wu/s yaw={yw/d*57.3f:0}deg/s tot=({sx:0.00},{sy:0.00},{sz:0.00},{yw*57.3f:0})";
    }
    static int Main(string[] args)
    {
        var m = new HaloMap(Maps + args[0] + ".map");
        foreach (var t in m.OfClass("bipd"))
        {
            if (args.Length > 1 && !t.Name.Contains(args[1])) continue;
            var r = t.Root;
            Console.WriteLine($"## {t.Name} flags {r.U32(756):X} accscale {r.F32(32)} maxv {r.F32(820)} side {r.F32(824)} acc {r.F32(828)} dec {r.F32(832)} angv {r.F32(836)} anga {r.F32(840)} crouchmod {r.F32(844)} slope {r.F32(856)*57.3f:0} movturn {r.F32(752)*57.3f:0}deg/s statthr {r.F32(760)*57.3f:0}deg crouchT {r.F32(1032)} aimv {r.F32(612)*57.3f:0} aima {r.F32(616)*57.3f:0} lookv {r.F32(624)*57.3f:0} looka {r.F32(628)*57.3f:0} casual {r.F32(620)} jump {r.F32(948)} land {r.F32(980)}/{r.F32(984)}/{r.F32(988)}/{r.F32(992)}/{r.F32(996)}/{r.F32(1000)} h {r.F32(1060)}/{r.F32(1064)} rad {r.F32(1068)} evade {r.F32(564)} dive {r.F32(568)} stunmove {r.F32(576)}");
            Console.WriteLine($"  spawned {r.Ref(588)?.Name} {r.I16(604)}-{r.I16(606)} v {r.F32(608)} coll {r.Ref(112)?.Name} creation {r.Ref(160)?.Name}");
            if (r.Ref(112) != null) { var cd = new HaloCollisionDef(r.Ref(112)); Console.WriteLine($"  coll flags {cd.Flags:X} body {cd.MaximumBody} destroyedThr {cd.BodyDestroyedThreshold} depleted {cd.BodyDepletedEffect?.Name} destroyed {cd.BodyDestroyedEffect?.Name} area {cd.AreaDamageEffect?.Name}"); }
            var an = r.Ref(56); if (an == null) continue;
            var g = new HaloAnimGraph(an);
            var ar = an.Root;
            Console.WriteLine($"  graph {an.Name} limp r? flags? anims {g.Animations.Length}");
            foreach (var s in g.Seats)
            {
                Console.Write($"  seat '{s.Label}' look {s.RightYawPerFrame*57.3f:0.0}/{s.LeftYawPerFrame*57.3f:0.0}deg x{s.RightFrames}/{s.LeftFrames} pitch {s.DownPitchPerFrame*57.3f:0.0}/{s.UpPitchPerFrame*57.3f:0.0} x{s.DownFrames}/{s.UpFrames} own:");
                for (int i = 0; i < s.Anims.Length; i++) if (s.Anims[i] >= 0) Console.Write($" [{i}]{g.Get(s.Anims[i]).Name}");
                Console.WriteLine();
                foreach (var w in s.Weapons)
                {
                    Console.WriteLine($"    class '{w.Name}' aim {w.RightYawPerFrame*57.3f:0.0}/{w.LeftYawPerFrame*57.3f:0.0}deg x{w.RightFrames}/{w.LeftFrames} pitch {w.DownPitchPerFrame*57.3f:0.0}/{w.UpPitchPerFrame*57.3f:0.0} x{w.DownFrames}/{w.UpFrames} ids:{string.Join(",", Enumerable.Range(0, w.Anims.Length).Where(i => w.Anims[i] >= 0))}");
                    foreach (int id in new[]{0,2,3,4,8,9,10,11,16,17,18,20,25,26,30,31,39})
                        if (w.Anim(id) >= 0) Console.WriteLine($"      {id}: {Sp(g, w.Anim(id))}");
                    foreach (var ty in w.Types) { Console.Write($"      type '{ty.Label}':"); for (int i=0;i<ty.Anims.Length;i++) if (ty.Anims[i]>=0) { var x=g.Get(ty.Anims[i]); Console.Write($" [{i}]{x.Name}({x.Type},fr{x.Frames},key{x.KeyFrame})"); } Console.WriteLine(); }
                }
                if (args.Length < 3) { }
            }
            foreach (int di in new[]{88,99,110,121,132,143,154,165}) if (di < g.UnitDamage.Length && g.UnitDamage[di] >= 0) Console.WriteLine("    dmg" + di + ": " + Sp(g, g.UnitDamage[di]));
            foreach (var s2 in g.Seats) if (s2.Label=="stand") { Console.WriteLine("    airdead: " + Sp(g, s2.Anim(0))); Console.WriteLine("    landdead: " + Sp(g, s2.Anim(1))); }
            Console.Write("  damage:");
            for (int i = 0; i < g.UnitDamage.Length; i++) if (g.UnitDamage[i] >= 0) { var x = g.Get(g.UnitDamage[i]); Console.Write($" [{i}]{x.Name}({x.Type},{x.Frames})"); }
            Console.WriteLine();
        }
        return 0;
    }
}

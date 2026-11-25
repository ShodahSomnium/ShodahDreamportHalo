using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;

static class FloodProbe
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static string[] UnitF = {"circular aiming","destroyed after dying","half speed interpolation","fires from camera","entrance inside bounding sphere","unused","causes passenger dialogue","resists pings","melee attack is fatal","dont reface during pings","has no aiming","simple creature","impact melee attaches to unit","impact melee dies on shields","cannot open doors automatically","melee attackers cannot attach","not instantly killed by melee","shield sapping","runs around flaming","inconsequential","special cinematic unit","ignored by autoaiming","shields fry infection forms","integrated light cntrls weapon","integrated light lasts forever"};
    static string[] BipF = {"turns without animating","uses player physics","flying","physics pill centered at origin","spherical","passes through other bipeds","can climb any surface","immune to falling damage","rotate while airborne","uses limp body physics","has no dying airborne","random speed increase","unit uses old ntsc player physics"};
    static string[] ActF = {"can see in darkness","sneak uncovering target","sneak uncovering pursuit position","unused","shoot at target s last location","try to stay still when crouched","crouch when not in combat","crouch when guarding","unused 1","must crouch to shoot","panic when surprised","always charge at enemies","gets in vehicles with player","start firing before aligned","standing must move forward","crouching must move forward","defensive crouch while charging","use stalking behavior","stalking freeze if exposed","always berserk in attacking mode","berserking uses panicked movement","flying","panicked by unopposable enemy","crouch when hiding from unopposable","always charge in attacking mode","dive off ledges","swarm","suicidal melee attack","cannot move while crouching","fixed crouch facing","crouch when in line of fire","avoid friends line of fire"};
    static string[] ActMF = {"avoid all enemy attack vectors","must stand to fire","must stop to fire","disallow vehicle combat","pathfinding ignores danger","panic in groups","no corpse shooting"};
    static string[] ActvF = {"can shoot while flying","interpolate color in hsv","has unlimited grenades","moveswitch stay w friends","active camouflage","super active camouflage","cannot use ranged weapons","prefer passenger seat"};
    static string Bits(uint v, string[] n) { var l = new List<string>(); for (int i = 0; i < 32; i++) if ((v & (1u << i)) != 0) l.Add(i < n.Length ? n[i] : "bit" + i); return string.Join(", ", l); }
    static string N(HaloTag t) => t == null ? "-" : t.Name + "." + t.ClassName;

    static int Main(string[] a)
    {
        HaloLog.Sink = Console.WriteLine;
        var m = new HaloMap(Maps + a[0] + ".map");
        string filt = a.Length > 1 ? a[1] : "flood";
        if (filt == "effect") { DumpEffect(m.Find("effe", a[2])); return 0; }
        if (filt == "antr") { var g = m.Find("antr", a[2]); var l = new List<string>(); foreach (var an in g.Root.Block(116, 180)) l.Add(an.Str32(0) + "(" + an.U16(34) + ")"); Console.WriteLine(string.Join(" | ", l)); return 0; }
        if (filt == "jpt") { var j = m.Find("jpt!", a[2]); var jr = j.Root; Console.WriteLine($"{j.Name}: radius {jr.F32(0)}..{jr.F32(4)} flags {jr.U32(12):X} side {jr.U16(452)} cat {jr.U16(454)} dflags {jr.U32(456):X} core {jr.F32(460)} lower {jr.F32(464)} upper {jr.F32(468)}..{jr.F32(472)} accel {jr.F32(500)},{jr.F32(504)},{jr.F32(508)}"); return 0; }
        var seenU = new HashSet<HaloTag>();
        foreach (var t in m.OfClass("actv"))
        {
            if (t.Name.IndexOf(filt, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var v = new HaloActorVariantDef(t);
            Console.WriteLine("=== actv " + t.Name);
            Console.WriteLine("  flags: " + Bits(v.Flags, ActvF));
            Console.WriteLine("  " + v.Describe());
            Console.WriteLine($"  movement {v.MovementType} crouch {v.InitialCrouchChance} crouchTime {v.CrouchTime} runTime {v.RunTime} desired {v.DesiredCombatRange} meleeR {v.MeleeRange} abort {v.MeleeAbortRange} berserkFire {v.BerserkFiringRanges} berserkMelee {v.BerserkMeleeRange}/{v.BerserkMeleeAbortRange} maxFire {v.MaximumFiringDistance} special {v.SpecialFireMode}/{v.SpecialFireSituation}/{v.SpecialFireChance} superBall {v.SuperBallisticRange} bomb {v.BombardmentRange} vision {v.ModifiedVisionRange} deathWild {v.DeathFireWildlyChance}/{v.DeathFireWildlyTime} surprise {v.SurpriseDelayTime}/{v.SurpriseFireWildlyTime} dropLoaded {v.DropWeaponLoaded} equipment {N(v.Equipment)}");
            var ac = v.ActorTag != null ? new HaloActorDef(v.ActorTag) : null;
            if (ac != null)
            {
                Console.WriteLine("  actr " + ac.Tag.Name + " type " + ac.Type);
                Console.WriteLine("    flags: " + Bits(ac.Flags, ActF));
                Console.WriteLine("    more: " + Bits(ac.MoreFlags, ActMF));
                Console.WriteLine("    " + ac.Describe());
                Console.WriteLine($"    meleeLeap range {ac.MeleeLeapRange} vel {ac.MeleeLeapVelocity} chance {ac.MeleeLeapChance} ballistic {ac.MeleeLeapBallistic} fudge {ac.MeleeFudgeFactor} charge {ac.MeleeChargeTime} berserk amt {ac.BerserkDamageAmount} thr {ac.BerserkDamageThreshold} prox {ac.BerserkProximity} suicide {ac.SuicideSensingDist} bgren {ac.BerserkGrenadeChance}");
                Console.WriteLine($"    crouchType {ac.DefensiveCrouchType} attCrouch {ac.AttackingCrouchThreshold} defCrouch {ac.DefendingCrouchThreshold} minStand {ac.MinStandTime} minCrouch {ac.MinCrouchTime} evade a{ac.AttackingEvasionThreshold} d{ac.DefendingEvasionThreshold} seekCover {ac.EvasionSeekCoverChance} delay {ac.EvasionDelayTime} diveCover {ac.DiveIntoCoverChance} diveGren {ac.DiveFromGrenadeChance} pathR {ac.PathfindingRadius} stalk {ac.StalkingDiscoveryTime}/{ac.StalkingMaxDistance}");
                Console.WriteLine($"    danger unreach {ac.UnreachableDangerTrigger} veh {ac.VehicleDangerTrigger} player {ac.PlayerDangerTrigger} time {ac.DangerTriggerTime} friendsKilled {ac.FriendsKilledTrigger} retreating {ac.FriendsRetreatingTrigger} panicDmg {ac.PanicDamageThreshold} surprise {ac.SurpriseDistance} hideShield {ac.HideShieldFraction} hide {ac.HideBehindCoverTime} pos coord {ac.NumPositionsCoord} normal {ac.NumPositionsNormal} oldAvoid {ac.OldPositionAvoidDist} friendAvoid {ac.FriendAvoidDist} hear {ac.HearingDistance} vision {ac.MaxVisionDistance} percept {ac.CombatPerceptionTime}");
            }
            if (v.Unit != null && seenU.Add(v.Unit)) DumpUnit(v.Unit);
            else if (v.Unit != null) Console.WriteLine("  unit " + v.Unit.Name + " (above)");
        }
        foreach (var t in m.OfClass("bipd")) if (t.Name.IndexOf(filt, StringComparison.OrdinalIgnoreCase) >= 0 && seenU.Add(t)) DumpUnit(t);
        return 0;
    }

    static void DumpUnit(HaloTag u)
    {
        var r = u.Root;
        Console.WriteLine("  --- unit " + u.Name);
        Console.WriteLine("    unit flags: " + Bits(r.U32(380), UnitF));
        Console.WriteLine("    biped flags: " + Bits(r.U32(756), BipF));
        Console.WriteLine($"    team {r.U16(384)} ping soft {r.F32(536)}/{r.F32(540)} hard {r.F32(544)}/{r.F32(548)} hardDeath {r.F32(552)} feign thr {r.F32(556)} time {r.F32(560)} chance {r.F32(580)} repeat {r.F32(584)} evadeDist {r.F32(564)} diveDist {r.F32(568)} stunnedMove {r.F32(576)}");
        Console.WriteLine($"    spawned actor {N(r.Ref(588))} count {r.I16(604)}..{r.I16(606)} vel {r.F32(608)} aiVehR {r.F32(640)} dangerR {r.F32(644)} melee {N(r.Ref(648))} grenade {r.I16(708)}x{r.I16(710)}");
        Console.WriteLine($"    maxV {r.F32(820)} side {r.F32(824)} acc {r.F32(828)} dec {r.F32(832)} jump {r.F32(948)} height {r.F32(1060)}/{r.F32(1064)} radius {r.F32(1068)} creation {N(r.Ref(160))} model {N(r.Ref(40))} anim {N(r.Ref(56))}");
        Console.WriteLine($"    landing soft {r.F32(988)} hard {r.F32(992)}..{r.F32(996)} death {r.F32(1000)} maxSlope {r.F32(856) * 57.3f}");
        var w = new List<string>(); foreach (var x in r.Block(728, 36)) w.Add(N(x.Ref(0))); Console.WriteLine("    weapons: " + string.Join(", ", w));
        var c = r.Ref(112);
        if (c != null)
        {
            var cr = c.Root;
            Console.WriteLine($"    coll {c.Name}: flags {cr.U32(0):X} body {cr.F32(8)} damagedThr {cr.F32(148)} damaged {N(cr.Ref(152))} depleted {N(cr.Ref(168))} destroyedThr {cr.F32(184)} destroyed {N(cr.Ref(188))} shield {cr.F32(204)} areaThr {cr.F32(128)} area {N(cr.Ref(132))} localized {N(cr.Ref(112))}");
            foreach (var mt in cr.Block(564, 72)) Console.WriteLine($"      material {mt.Str32(0)} flags {mt.U32(32):X} type {mt.U16(36)} body x{mt.F32(60)} shield x{mt.F32(44)}");
            foreach (var rg in cr.Block(576, 84)) Console.WriteLine($"      region {rg.Str32(0)} flags {rg.U32(32):X} thr {rg.F32(40)} destroyed {N(rg.Ref(56))}");
        }
    }

    // effect: events (+52, 68 B) -> parts (+44? see layouts), just list the referenced tags
    static void DumpEffect(HaloTag e)
    {
        if (e == null) { Console.WriteLine("no effect"); return; }
        Console.WriteLine("effect " + e.Name);
        var r = e.Root;
        foreach (var ev in r.Block(52, 68))
        {
            Console.WriteLine($"  event delay {ev.F32(8)}..{ev.F32(12)}");
            foreach (var p in ev.Block(44, 104)) Console.WriteLine($"    part {N(p.Ref(24))} create-in {p.U16(0)}");
        }
    }
}

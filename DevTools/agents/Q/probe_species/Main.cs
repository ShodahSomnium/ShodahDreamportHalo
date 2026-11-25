using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static class P
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static string N(HaloTag t) => t == null ? "-" : t.Name;
    static int Main(string[] a)
    {
        HaloLog.Sink = s => { };
        var seen = new HashSet<string>();
        foreach (var mn in a)
        {
            var m = new HaloMap(Maps + mn + ".map");
            Console.WriteLine("===== " + mn);
            var cat = new HaloActorCatalog(m);
            foreach (var t in m.OfClass("actr"))
            {
                if (!seen.Add("actr" + t.Name)) continue;
                var x = cat.Actor(t); var r = t.Root;
                Console.WriteLine($"ACTR {t.Name} type {x.Type} flags {x.Flags:X8} more {x.MoreFlags:X2} surpriseDist {x.SurpriseDistance} panicDmg {x.PanicDamageThreshold} berserkAmt {x.BerserkDamageAmount} berserkThr {x.BerserkDamageThreshold} berserkProx {x.BerserkProximity} suicideDist {x.SuicideSensingDist} berserkGren {x.BerserkGrenadeChance} leader {x.LeaderType} leaderPanic {x.LeaderKilledPanicChance} friendPanic {x.FriendKilledPanicChance} friendsKilledTrig {x.FriendsKilledTrigger} friendsRetreatTrig {x.FriendsRetreatingTrigger} dangerTrig u{x.UnreachableDangerTrigger}/v{x.VehicleDangerTrigger}/p{x.PlayerDangerTrigger} {x.DangerTriggerTime} crouchType {x.DefensiveCrouchType} atkCrouch {x.AttackingCrouchThreshold} defCrouch {x.DefendingCrouchThreshold} minStand {x.MinStandTime} minCrouch {x.MinCrouchTime} defHideMod {x.DefendingHideTimeModifier} evade a{x.AttackingEvasionThreshold}/d{x.DefendingEvasionThreshold} seekCover {x.EvasionSeekCoverChance} evDelay {x.EvasionDelayTime} maxSeekCover {x.MaxSeekCoverDistance} coverDmg {x.CoverDamageThreshold} stalk {x.StalkingDiscoveryTime}/{x.StalkingMaxDistance} hideTime {x.HideBehindCoverTime} hideNotVis {x.HideTargetNotVisibleTime} shield h{x.HideShieldFraction}/a{x.AttackShieldFraction}/p{x.PursueShieldFraction} uncover {x.UncoverDelayTime} pursuit {x.PursuitPositionTime} melee delay {x.MeleeAttackDelay} fudge {x.MeleeFudgeFactor} charge {x.MeleeChargeTime} leap {x.MeleeLeapRange} v{x.MeleeLeapVelocity} p{x.MeleeLeapChance} ball{x.MeleeLeapBallistic} diveCover {x.DiveIntoCoverChance} emerge {x.EmergeFromCoverChance} diveGren {x.DiveFromGrenadeChance} retreat {x.RetreatTime} cower {x.CoweringTime} ffSidestep {x.FreeFlyingSidestep} pathR {x.PathfindingRadius}");
            }
            foreach (var t in m.OfClass("actv"))
            {
                if (!seen.Add("actv" + t.Name)) continue;
                var v = cat.Variant(t);
                Console.WriteLine($"ACTV {t.Name} flags {v.Flags:X2} actor {N(v.ActorTag)} unit {N(v.Unit)} weapon {N(v.Weapon)} major {N(v.MajorVariant)} moveType {v.MovementType} initCrouch {v.InitialCrouchChance} crouchT {v.CrouchTime} runT {v.RunTime} special mode {v.SpecialFireMode} sit {v.SpecialFireSituation} ch {v.SpecialFireChance} dly {v.SpecialFireDelay} berserk fire {v.BerserkFiringRanges} bMelee {v.BerserkMeleeRange}/{v.BerserkMeleeAbortRange} bBurst {v.BerserkBurstDuration}/{v.BerserkBurstSeparation} rof {v.BerserkRateOfFire} melee {v.MeleeRange}/{v.MeleeAbortRange} superBall {v.SuperBallisticRange} bombard {v.BombardmentRange} visRange {v.ModifiedVisionRange} deathWild {v.DeathFireWildlyChance}/{v.DeathFireWildlyTime} surprise {v.SurpriseDelayTime}/{v.SurpriseFireWildlyTime} gren type {v.GrenadeType} cnt {v.GrenadeCountMin}-{v.GrenadeCountMax} shieldSap {v.ShieldSappingRadius} body {v.BodyVitality} shield {v.ShieldVitality}");
            }
            foreach (var t in m.OfClass("bipd"))
            {
                if (!seen.Add("bipd" + t.Name)) continue;
                var r = t.Root;
                var coll = r.Ref(112);
                Console.WriteLine($"BIPD {t.Name} unitFlags {r.U32(380):X8} bipedFlags {r.U32(756):X8} maxSlope {r.F32(856)*57.3f:0} maxV {r.F32(820)} jumpV {r.F32(948)} deathLandV {r.F32(1000)} team {r.U16(384)} feign thr {r.F32(556)} t {r.F32(560)} ch {r.F32(580)} rep {r.F32(584)} evade {r.F32(564)} dive {r.F32(568)} spawned {N(r.Ref(588))} {r.I16(604)}-{r.I16(606)} melee {N(r.Ref(648))} dialogueVariants {r.Count(692)} grenades {r.I16(708)}x{r.I16(710)} coll {N(coll)}");
                foreach (var dv in r.Block(692, 24)) Console.WriteLine($"   dialogue var {dv.I16(0)} {N(dv.Ref(8))}");
                if (coll != null && seen.Add("coll" + coll.Name))
                {
                    var c = coll.Root;
                    Console.WriteLine($"   COLL flags {c.U32(0):X} body {c.F32(8)} shield {c.F32(204)} shMat {c.U16(210)} failThr {c.F32(240)} failLeak {c.F32(244)} minStun {c.F32(264)} stun {c.F32(268)} recharge {c.F32(272)} rate {c.F32(448)} bodyDestroyThr {c.F32(184)}");
                    foreach (var mt in c.Block(564, 72)) Console.WriteLine($"     mat '{mt.Str32(0)}' flags {mt.U32(32):X} type {mt.U16(36)} leak {mt.F32(40)} shMul {mt.F32(44)} bodyMul {mt.F32(60)}");
                    foreach (var rg in c.Block(576, 84)) Console.WriteLine($"     region '{rg.Str32(0)}' flags {rg.U32(32):X} thr {rg.F32(40)} destroyed {N(rg.Ref(56))}");
                }
            }
        }
        return 0;
    }
}

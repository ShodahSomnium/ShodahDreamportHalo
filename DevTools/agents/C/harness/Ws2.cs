using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

// Workstream 2: the new HaloCharacterDef fields per bipd / actv of a map (ws2 <map> [name part])
static partial class P
{
    static int Ws2(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        string part = a.Length > 2 ? a[2] : "";
        foreach (var t in m.OfClass("actv").Concat(m.OfClass("bipd")))
        {
            if (part.Length > 0 && t.Name.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var d = HaloCharacterDef.From(t);
            if (d == null) continue;
            bool hard;
            float ls = d.LandingSeconds(d.MinHardLandingVelocity + 0.5f * (d.MaxHardLandingVelocity - d.MinHardLandingVelocity), out hard);
            Console.WriteLine($"{t.ClassName} {t.Name}: cosSlope {d.CosMaxSlope:0.###} climbAny {d.CanClimbAnySurface} accScale {d.AccelerationScale:0.##} spawned {N(d.SpawnedActor)} {d.SpawnedCountMin}..{d.SpawnedCountMax} v {d.SpawnedVelocity} " +
                $"destroyedAfterDying {d.DestroyedAfterDying} meleeFatal {d.MeleeIsFatal} notInstaMelee {d.NotInstantlyKilledByMelee} attach {d.ImpactMeleeAttachesToUnit} diesOnShields {d.ImpactMeleeDiesOnShields} " +
                $"cannotAttach {d.MeleeAttackersCannotAttach} fry {d.ShieldsFryInfectionForms} feign {d.FeignDeathThreshold}/{d.FeignDeathChance}/{d.FeignRepeatChance}/{d.FeignDeathTime}s danger {d.AiDangerRadius} vehR {d.AiVehicleRadius} " +
                $"land {d.MaxSoftLandingTime}/{d.MaxHardLandingTime}s mid-hard {ls:0.00}s ({hard}) deathLand {d.DeathHardLandingVelocity} grenV {d.VariantGrenadeVelocity}/{d.GrenadeVelocity} traj {d.GrenadeTrajectory} special {d.SpecialFireMode} ch {d.SpecialFireChance} dly {d.SpecialFireDelay} " +
                $"camo {d.ActiveCamouflage}/{d.SuperActiveCamouflage} meleeDelay {d.MeleeAttackDelay} coll {(d.Coll != null ? d.Coll.Nodes.Sum(n => n.Bsps.Sum(b => b.Parts != null ? b.Parts.Length : 1)) : 0)} parts");
        }
        return 0;
    }
}

static partial class P
{
    // ws2anim <map> <name part> [weapon label]: strafe speeds, strafe capability, feign / resurrect, land cut, melee root, leap clips
    static int Ws2Anim(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("actv").Concat(m.OfClass("bipd")))
        {
            if (t.Name.IndexOf(a[2], StringComparison.OrdinalIgnoreCase) < 0) continue;
            var d = HaloCharacterDef.From(t);
            if (d == null || d.Graph == null || d.Model == null) continue;
            var g = new HaloAnimGraph(d.Graph);
            var an = new HaloUnitAnimator(new HaloSkeleton(new HaloModel(d.Model)), g, 5);
            string label = a.Length > 3 ? a[3] : (d.Weapon != null ? d.Weapon.Root.Str32(780) : null);
            var inp = new HaloUnitAnimInput { Seat = "stand", WeaponLabel = label };
            an.Step(inp, 0.03f);
            Console.WriteLine($"{t.Name} [{an.ClassName}] strafe {an.StrafeCapable} front {an.MaxSpeedFor(1, 0):0.00} back {an.MaxSpeedFor(-1, 0):0.00} right {an.MaxSpeedFor(0, -1):0.00} left {an.MaxSpeedFor(0, 1):0.00} diag {an.MaxSpeedFor(0.7f, 0.7f):0.00} wu/s");
            // slow drift -> idle; strafe without clips -> face movement
            inp.MoveX = 0f; inp.MoveY = an.NaturalSpeed * 0.2f; an.Step(inp, 0.03f);
            Console.WriteLine($"   slow strafe 20%: base {an.BaseName} face {an.FaceMovement}");
            inp.MoveY = an.NaturalSpeed; an.Step(inp, 0.03f);
            Console.WriteLine($"   full strafe: base {an.BaseName} face {an.FaceMovement}");
            inp.MoveY = 0f;
            bool melee = an.Melee();
            an.Step(inp, 0.03f);
            Console.WriteLine($"   melee {melee} {an.ActionName} blocks {an.BlocksMovement}");
            for (int i = 0; i < 100; i++) an.Step(inp, 0.03f);
            bool land = an.Land(true, 0.3f); float tl = 0f;
            while (an.Action != HaloUnitAnimator.ActionKind.None && tl < 5f) { an.Step(inp, 0.03f); tl += 0.03f; }
            Console.WriteLine($"   land(0.3 s) {land} lasted {tl:0.00} s");
            an.Feign(false, 0, 0); an.Step(inp, 0.03f);
            Console.WriteLine($"   feign {an.DeathName} blocks {an.BlocksMovement} feigning {an.Feigning}");
            for (int i = 0; i < 60; i++) an.Step(inp, 0.03f);
            bool up = an.Resurrect(false); an.Step(inp, 0.03f);
            Console.WriteLine($"   resurrect {up} {an.ActionName} dying {an.Dying}");
            for (int i = 0; i < 60; i++) an.Step(inp, 0.03f);
            bool ls = an.PlayLeap(HaloUnitAnimator.LeapPhase.Start), la = an.PlayLeap(HaloUnitAnimator.LeapPhase.Airborne);
            inp.Airborne = true; an.Step(inp, 0.03f);
            Console.WriteLine($"   leap start {ls} airborne {la} base {an.BaseName} action {an.ActionName}");
            inp.Airborne = false; an.Step(inp, 0.03f);
            Console.WriteLine($"   landed base {an.BaseName}; gesture46 {an.Gesture(HaloUnitAnimator.AnimMeleeContinuous)} {an.ActionName}");
            if (a.Length <= 4) break;
        }
        return 0;
    }
}

using System;
using SomniumSpace.Worlds.SomniumHalo;
static partial class I
{
    public static int Gren(HaloMap m)
    {
        var cat = new HaloActorCatalog(m);
        foreach (var t in m.OfClass("actv")) { var v = cat.Variant(t); if (v == null) continue;
            Console.WriteLine($"{Short(t)}: type {v.GrenadeType} stim {v.GrenadeStimulus} traj {v.TrajectoryType} count {v.GrenadeCountMin}-{v.GrenadeCountMax} chance {v.GrenadeChance} check {v.GrenadeCheckTime} enc timeout {v.EncounterGrenadeTimeout} min enemies {v.MinimumEnemyCount} radius {v.EnemyRadius} collateral {v.CollateralDamageRadius} ranges {v.GrenadeRanges} vel {v.GrenadeVelocity}"); }
        return 0;
    }
}

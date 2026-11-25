using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The globals' (matg +284) difficulty table: per difficulty (0 easy, 1 normal, 2 heroic "hard", 3 legendary "imposs") the enemy /
    /// friend damage, vitality, shield and recharge scales, the AI's rate of fire, projectile error, burst error, new target delay, burst
    /// separation, target tracking / leading, overcharge chance, special fire delay, guidance vs player, melee delay, grenade chance / timer
    /// scales and the major upgrade fractions (normal / few / many). Offsets: layouts.txt struct GlobalsDifficulty. Unity-free.
    /// </summary>
    public sealed class HaloAiDifficulty
    {
        public readonly float[] EnemyDamage = One(), EnemyVitality = One(), EnemyShield = One(), EnemyRecharge = One();
        public readonly float[] FriendDamage = One(), FriendVitality = One(), FriendShield = One(), FriendRecharge = One();
        public readonly float[] InfectionForms = new float[4], RateOfFire = One(), ProjectileError = One(), BurstError = One(), NewTargetDelay = One();
        public readonly float[] BurstSeparation = One(), TargetTracking = One(), TargetLeading = One(), OverchargeChance = new float[4], SpecialFireDelay = One();
        public readonly float[] GuidanceVsPlayer = One(), MeleeDelayBase = new float[4], MeleeDelayScale = One(), GrenadeChanceScale = One(), GrenadeTimerScale = One();
        public readonly float[] MajorUpgradeNormal = new float[4], MajorUpgradeFew = new float[4], MajorUpgradeMany = new float[4];
        public readonly bool FromTag;

        static float[] One() { return new[] { 1f, 1f, 1f, 1f }; }

        public HaloAiDifficulty() { }

        public HaloAiDifficulty(HaloMap map)
        {
            HaloTag globals = null;
            foreach (var t in map.OfClass("matg")) { globals = t; break; }
            if (globals == null) return;
            foreach (var d in globals.Root.Block(284, 644))
            {
                Read(d, 0, EnemyDamage); Read(d, 16, EnemyVitality); Read(d, 32, EnemyShield); Read(d, 48, EnemyRecharge);
                Read(d, 64, FriendDamage); Read(d, 80, FriendVitality); Read(d, 96, FriendShield); Read(d, 112, FriendRecharge);
                Read(d, 128, InfectionForms); Read(d, 160, RateOfFire); Read(d, 176, ProjectileError); Read(d, 192, BurstError);
                Read(d, 208, NewTargetDelay); Read(d, 224, BurstSeparation); Read(d, 240, TargetTracking); Read(d, 256, TargetLeading);
                Read(d, 272, OverchargeChance); Read(d, 288, SpecialFireDelay); Read(d, 304, GuidanceVsPlayer); Read(d, 320, MeleeDelayBase);
                Read(d, 336, MeleeDelayScale); Read(d, 368, GrenadeChanceScale); Read(d, 384, GrenadeTimerScale);
                Read(d, 448, MajorUpgradeNormal); Read(d, 464, MajorUpgradeFew); Read(d, 480, MajorUpgradeMany);
                FromTag = true;
                break;
            }
        }

        static void Read(TagReader r, int o, float[] into) { for (int i = 0; i < 4; i++) into[i] = r.F32(o + 4 * i); }

        /// <summary>The fraction of a squad that upgrades to the major variant (squad "major upgrade": 0 normal, 1 few, 2 many, 3 none, 4 all).</summary>
        public float MajorFraction(int squadMajorUpgrade, int difficulty)
        {
            int d = Math.Max(0, Math.Min(3, difficulty));
            switch (squadMajorUpgrade)
            {
                case 1: return MajorUpgradeFew[d];
                case 2: return MajorUpgradeMany[d];
                case 3: return 0f;
                case 4: return 1f;
                default: return MajorUpgradeNormal[d];
            }
        }

        public string Describe(int d)
        {
            return $"enemy dmg {EnemyDamage[d]:0.##} vit {EnemyVitality[d]:0.##} shield {EnemyShield[d]:0.##} friend dmg {FriendDamage[d]:0.##} vit {FriendVitality[d]:0.##} rof {RateOfFire[d]:0.##} err {ProjectileError[d]:0.##} burst err {BurstError[d]:0.##} sep {BurstSeparation[d]:0.##} track {TargetTracking[d]:0.##} lead {TargetLeading[d]:0.##} newTarget {NewTargetDelay[d]:0.##} melee {MeleeDelayBase[d]:0.##}+x{MeleeDelayScale[d]:0.##} grenade x{GrenadeChanceScale[d]:0.##} major n{MajorUpgradeNormal[d]:0.##}/f{MajorUpgradeFew[d]:0.##}/m{MajorUpgradeMany[d]:0.##} infection {InfectionForms[d]:0.##}";
        }
    }
}

using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A damage effect (jpt!): how much (lower bound, upper bound range), how far (area radius, core radius), what kind (category: bullet,
    /// grenade, melee ...), the flags (headshots, skips shields, does not hurt owner / friends) and the per-material modifiers (cyborg energy
    /// shield, cyborg armor, metal ...). Halo vitality points: the MP cyborg has 75 body and 75 shield. Unity-free.
    ///
    /// Our reading of the numbers (the engine's code is not available): a direct hit does a random amount in the upper bound range, falling
    /// to the lower bound over the projectile's air damage range (full up to its min distance, lower bound at its max distance); an area
    /// effect does full damage within the inner radius (or the core radius) and falls off linearly to nothing at the outer radius. The
    /// material modifier multiplies the amount for the surface it lands on (a modifier of 0 = no damage to that material).
    /// </summary>
    public sealed class HaloDamageDef
    {
        public enum Category { None = 0, Falling, Bullet, Grenade, HighExplosive, Sniper, Melee, Flame, MountedWeapon, Vehicle, Plasma, Needle, Shotgun }

        /// <summary>Halo material types (MaterialType) the multiplayer cyborg uses.</summary>
        public const int CyborgArmor = 21, CyborgEnergyShield = 22, MaterialCount = 33;

        public readonly HaloTag Tag;
        public readonly float RadiusMin, RadiusMax, CutoffScale;
        public readonly uint Flags, DamageFlags;
        public readonly Category Kind;
        public readonly int SideEffect;
        public readonly float CoreRadius, LowerBound, UpperMin, UpperMax, VehiclePassthroughPenalty, ActiveCamouflageDamage, Stun, MaximumStun, StunTime;
        public readonly HVec3 InstantaneousAcceleration;
        public readonly HaloTag Sound;
        public readonly float ScreenFlashDuration, ScreenFlashIntensity, ShakeDuration;
        public readonly float[] Modifiers = new float[MaterialCount];

        public HaloDamageDef(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            RadiusMin = r.F32(0); RadiusMax = r.F32(4); CutoffScale = r.F32(8);
            Flags = r.U32(12);
            ScreenFlashDuration = r.F32(52); ScreenFlashIntensity = r.F32(68);
            ShakeDuration = r.F32(204);
            Sound = r.Ref(276);
            SideEffect = r.U16(452);
            Kind = (Category)r.U16(454);
            DamageFlags = r.U32(456);
            CoreRadius = r.F32(460);
            LowerBound = r.F32(464);
            UpperMin = r.F32(468); UpperMax = r.F32(472);
            VehiclePassthroughPenalty = r.F32(476);
            ActiveCamouflageDamage = r.F32(480);
            Stun = r.F32(484); MaximumStun = r.F32(488); StunTime = r.F32(492);
            InstantaneousAcceleration = r.Vec3(500);
            for (int i = 0; i < MaterialCount; i++) Modifiers[i] = r.F32(512 + 4 * i);
        }

        public bool DoNotScaleByDistance => (Flags & 1) != 0;
        public bool DoesNotHurtOwner => (DamageFlags & 1) != 0;
        public bool CanCauseHeadshots => (DamageFlags & 2) != 0 || (DamageFlags & (1u << 11)) != 0;
        public bool DoesNotHurtFriends => (DamageFlags & 8) != 0;
        public bool OnlyHurtsShields => (DamageFlags & (1u << 6)) != 0;
        public bool SkipsShields => (DamageFlags & (1u << 9)) != 0;
        public bool DoesNotHurtPlayers => (DamageFlags & (1u << 15)) != 0;
        // damage flags (bit numbers verified against invader-defs damage_effect.json DamageEffectDamageFlags)
        /// <summary>Bit 5 "detonates explosives": grenades within the radius go off.</summary>
        public bool DetonatesExplosives => (DamageFlags & (1u << 5)) != 0;
        /// <summary>Bit 10 "only hurts one infection form" (of a swarm in the radius).</summary>
        public bool OnlyHurtsOneInfectionForm => (DamageFlags & (1u << 10)) != 0;
        /// <summary>Bit 12 "infection form pop": kills infection forms outright (their own body destroyed jpt!: the chain pop).</summary>
        public bool InfectionFormPop => (DamageFlags & (1u << 12)) != 0;
        /// <summary>Bit 16 "use 3d instantaneous acceleration" (else only the i component counts, away from the centre).</summary>
        public bool Use3dAcceleration => (DamageFlags & (1u << 16)) != 0;

        /// <summary>The instantaneous acceleration's size (the i component, or the vector's length with the 3d flag). Raw tag value; the
        /// tags carry frag 4, rocket 6, carrier burst 5 - we read it as world units / s (a world-units-per-tick reading would throw a grunt
        /// at 365 m/s).</summary>
        public float AccelerationMagnitude => Use3dAcceleration
            ? (float)Math.Sqrt(InstantaneousAcceleration.X * InstantaneousAcceleration.X + InstantaneousAcceleration.Y * InstantaneousAcceleration.Y + InstantaneousAcceleration.Z * InstantaneousAcceleration.Z)
            : Math.Abs(InstantaneousAcceleration.X);

        /// <summary>The area falloff 0..1 at <paramref name="distance"/> world units (the shape of <see cref="AreaAmount"/> without the random amount).</summary>
        public float AreaFalloff(float distance)
        {
            if (DoNotScaleByDistance) return distance <= RadiusMax ? 1f : 0f;
            float inner = Math.Max(RadiusMin, CoreRadius);
            if (distance <= inner) return 1f;
            if (distance >= RadiusMax || RadiusMax <= inner) return 0f;
            return 1f - (distance - inner) / (RadiusMax - inner);
        }
        /// <summary>Does nothing to anyone (camera shake, a screen flash: "shock wave", "trigger" damage of a shot).</summary>
        public bool Harmless => UpperMax <= 0f && LowerBound <= 0f;
        public bool IsArea => RadiusMax > 0f && !Harmless;

        /// <summary>The material modifier (1 when out of range).</summary>
        public float Modifier(int material) => material >= 0 && material < MaterialCount ? Modifiers[material] : 1f;

        /// <summary>A direct hit's amount after <paramref name="distance"/> world units of flight (air damage range of the projectile).</summary>
        public float DirectAmount(Random rng, float distance, float rangeMin, float rangeMax)
        {
            float a = UpperMin + (float)(rng != null ? rng.NextDouble() : 0.5) * (UpperMax - UpperMin);
            if (rangeMax > rangeMin && distance > rangeMin)
            {
                float u = (distance - rangeMin) / (rangeMax - rangeMin);
                if (u > 1f) u = 1f;
                a = a + (LowerBound - a) * u;
            }

            return a;
        }

        /// <summary>The amount an area effect does at <paramref name="distance"/> world units from its centre (0 beyond the outer radius).</summary>
        public float AreaAmount(Random rng, float distance)
        {
            float a = UpperMin + (float)(rng != null ? rng.NextDouble() : 0.5) * (UpperMax - UpperMin);
            if (DoNotScaleByDistance) return distance <= RadiusMax ? a : 0f;
            float inner = Math.Max(RadiusMin, CoreRadius);
            if (distance <= inner) return a;
            if (distance >= RadiusMax || RadiusMax <= inner) return 0f;
            return a * (1f - (distance - inner) / (RadiusMax - inner));
        }

        /// <summary>The damage-effect parts of an effect tag (effe events -> parts of class jpt!): a detonation's blast.</summary>
        public static List<HaloTag> PartsOf(HaloTag effect)
        {
            var list = new List<HaloTag>();
            if (effect == null || !effect.Is("effe")) return list;
            uint jpt = HaloMap.FourCC("jpt!");
            foreach (var ev in effect.Root.Block(52, 68))
                foreach (var p in ev.Block(44, 104))
                {
                    if (p.U32(20) != jpt && p.RefClass(24) != jpt) continue;
                    var t = p.Ref(24);
                    if (t != null) list.Add(t);
                }

            return list;
        }

        public override string ToString() => Tag.Name;
    }

    /// <summary>
    /// The vitality of a unit from its collision model (coll): body and shield points, the shield stun (no recharge) time after damage and
    /// the full recharge time, the shield and body materials, and the materials flagged "head" (headshots). Unity-free.
    /// </summary>
    public sealed class HaloVitality
    {
        public readonly HaloTag Tag;
        public readonly float MaximumBody, MaximumShield, MinimumStunDamage, StunTime, RechargeTime, ShieldRechargeRatePerTick;
        public readonly int ShieldMaterial;
        public readonly HaloTag ShieldDamagedEffect, ShieldDepletedEffect, ShieldRechargingEffect, BodyDepletedEffect;
        public readonly List<string> Materials = new List<string>();
        public readonly List<int> MaterialTypes = new List<int>();
        public readonly List<bool> Heads = new List<bool>();
        public readonly List<float> BodyMultipliers = new List<float>(), ShieldMultipliers = new List<float>();

        public HaloVitality(HaloTag coll)
        {
            Tag = coll;
            var c = coll.Root;
            MaximumBody = c.F32(8);
            MaximumShield = c.F32(204);
            ShieldMaterial = c.U16(210);
            MinimumStunDamage = c.F32(264);
            StunTime = c.F32(268);
            RechargeTime = c.F32(272);
            ShieldRechargeRatePerTick = c.F32(448);
            BodyDepletedEffect = c.Ref(168);
            ShieldDamagedEffect = c.Ref(392);
            ShieldDepletedEffect = c.Ref(408);
            ShieldRechargingEffect = c.Ref(424);
            foreach (var m in c.Block(564, 72))
            {
                Materials.Add(m.Str32(0));
                Heads.Add((m.U32(32) & 1) != 0);
                MaterialTypes.Add(m.U16(36));
                ShieldMultipliers.Add(m.F32(44));
                BodyMultipliers.Add(m.F32(60));
            }
        }

        /// <summary>The body material type (the first material's, "cyborg armor" for the cyborg).</summary>
        public int BodyMaterial => MaterialTypes.Count > 0 ? MaterialTypes[0] : HaloDamageDef.CyborgArmor;

        /// <summary>Seconds for the shields to refill from empty (the rate per tick when the recharge time is 0).</summary>
        public float FullRechargeSeconds => RechargeTime > 0f ? RechargeTime : ShieldRechargeRatePerTick > 0f ? 1f / (ShieldRechargeRatePerTick * HaloProjectileDef.TicksPerSecond) : 4f;
    }
}

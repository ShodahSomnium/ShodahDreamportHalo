using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A projectile tag (proj): how it flies (initial / final velocity, gravity scale, guidance, range), when it goes off (timer and when the
    /// timer starts, arming time), what it does (impact damage, attached detonation damage, the detonation effect whose damage parts are the
    /// blast) and how it reacts to each material (disappear, detonate, reflect, overpenetrate, attach). The cache file stores velocities in
    /// world units per TICK (30 ticks a second): <see cref="InitialSpeed"/> etc. are converted to world units per second. Unity-free.
    /// </summary>
    public sealed class HaloProjectileDef
    {
        public const float TicksPerSecond = 30f;
        /// <summary>Halo's gravity: 0.0035651791 world units per tick squared (= 9.78 m/s^2).</summary>
        public const float GravityUnitsPerSecond2 = 0.0035651791f * TicksPerSecond * TicksPerSecond;

        public enum Response { Disappear = 0, Detonate = 1, Reflect = 2, Overpenetrate = 3, Attach = 4 }
        public enum TimerStart { Immediately = 0, AfterFirstBounce = 1, WhenAtRest = 2 }

        public sealed class MaterialResponse
        {
            public ushort Flags;
            public Response Default; public HaloTag DefaultEffect;
            public Response Potential; public ushort PotentialFlags; public float PotentialSkipFraction;
            public float PotentialAngleMin, PotentialAngleMax, PotentialSpeedMin, PotentialSpeedMax;
            public HaloTag PotentialEffect, DetonationEffect;
            public float InitialFriction, MaximumDistance, ParallelFriction, PerpendicularFriction;
            /// <summary>Flag bit 0 "cannot be overpenetrated".</summary>
            public bool CannotBeOverpenetrated => (Flags & 1) != 0;
        }

        public readonly HaloTag Tag;
        public readonly uint Flags;
        public readonly TimerStart DetonationTimerStarts;
        public readonly HaloTag Model, SuperDetonation, DetonationEffect, DetonationStarted, FlybySound, AttachedDetonationDamage, ImpactDamage;
        public readonly float CollisionRadius, ArmingTime, DangerRadius, TimerMin, TimerMax;
        public readonly float MinimumSpeed, MaximumRange, AirGravityScale, AirDamageRangeMin, AirDamageRangeMax, WaterGravityScale;
        public readonly float InitialSpeed, FinalSpeed;          // world units a second
        public readonly float GuidedAngularVelocity;             // radians a second
        public readonly List<MaterialResponse> Materials = new List<MaterialResponse>();
        /// <summary>Object attachments (contrail, light, particle system, effect, looping sound) with their marker names.</summary>
        public readonly List<KeyValuePair<HaloTag, string>> Attachments = new List<KeyValuePair<HaloTag, string>>();

        public bool OrientedAlongVelocity => (Flags & 1) != 0;
        public bool DetonationMaxTimeIfAttached => (Flags & 4) != 0;
        public bool HasSuperCombiningExplosion => (Flags & 8) != 0;
        public bool CombineInitialVelocityWithParent => (Flags & 16) != 0;

        public HaloProjectileDef(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            Model = r.Ref(40);
            foreach (var a in r.Block(320, 72)) { var t = a.Ref(0); if (t != null) Attachments.Add(new KeyValuePair<HaloTag, string>(t, a.Str32(16))); }
            Flags = r.U32(380);
            DetonationTimerStarts = (TimerStart)r.U16(384);
            SuperDetonation = r.Ref(396);
            CollisionRadius = r.F32(416);
            ArmingTime = r.F32(420);
            DangerRadius = r.F32(424);
            DetonationEffect = r.Ref(428);
            TimerMin = r.F32(444); TimerMax = r.F32(448);
            MinimumSpeed = r.F32(452) * TicksPerSecond;
            MaximumRange = r.F32(456);
            AirGravityScale = r.F32(460);
            AirDamageRangeMin = r.F32(464); AirDamageRangeMax = r.F32(468);
            WaterGravityScale = r.F32(472);
            InitialSpeed = r.F32(484) * TicksPerSecond;
            FinalSpeed = r.F32(488) * TicksPerSecond;
            GuidedAngularVelocity = r.F32(492);
            DetonationStarted = r.Ref(500);
            FlybySound = r.Ref(516);
            AttachedDetonationDamage = r.Ref(532);
            ImpactDamage = r.Ref(548);
            foreach (var m in r.Block(576, 160))
                Materials.Add(new MaterialResponse
                {
                    Flags = m.U16(0), Default = (Response)m.U16(2), DefaultEffect = m.Ref(4),
                    Potential = (Response)m.U16(36), PotentialFlags = m.U16(38), PotentialSkipFraction = m.F32(40),
                    PotentialAngleMin = m.F32(44), PotentialAngleMax = m.F32(48), PotentialSpeedMin = m.F32(52), PotentialSpeedMax = m.F32(56),
                    PotentialEffect = m.Ref(60), DetonationEffect = m.Ref(104),
                    InitialFriction = m.F32(144), MaximumDistance = m.F32(148), ParallelFriction = m.F32(152), PerpendicularFriction = m.F32(156),
                });
        }

        /// <summary>The response block of a Halo material type (MaterialType: 0 dirt .. 32 hunter shield), null when the tag has none.</summary>
        public MaterialResponse For(int material)
        {
            if (material >= 0 && material < Materials.Count) return Materials[material];
            return Materials.Count > 0 ? Materials[0] : null;
        }

        /// <summary>A grenade-like projectile: thrown with no own speed, goes off on a timer.</summary>
        public bool Thrown => InitialSpeed <= 0.0001f && FinalSpeed <= 0.0001f;

        /// <summary>The speed after travelling <paramref name="distance"/> world units (initial to final over the maximum range, or over 1 s when the range is unlimited - ours).</summary>
        public float SpeedAt(float distance, float seconds)
        {
            float u;
            if (MaximumRange > 0f) u = distance / MaximumRange;
            else u = seconds;
            if (u < 0f) u = 0f; else if (u > 1f) u = 1f;
            return InitialSpeed + (FinalSpeed - InitialSpeed) * u;
        }

        /// <summary>Lines for logs and the harness.</summary>
        public IEnumerable<string> Describe()
        {
            yield return "projectile " + Tag.Name + " v " + InitialSpeed.ToString("0.###") + "->" + FinalSpeed.ToString("0.###") + " wu/s range " + MaximumRange.ToString("0.###")
                         + " gravity " + AirGravityScale.ToString("0.###") + " radius " + CollisionRadius.ToString("0.###") + " timer " + TimerMin.ToString("0.###") + ".." + TimerMax.ToString("0.###")
                         + " (" + DetonationTimerStarts + ") arm " + ArmingTime.ToString("0.###") + " guided " + GuidedAngularVelocity.ToString("0.###") + " rad/s model " + Model + " detonation " + DetonationEffect
                         + " impact " + ImpactDamage + " attached " + AttachedDetonationDamage;
        }

        public override string ToString() => Tag.Name;
    }
}

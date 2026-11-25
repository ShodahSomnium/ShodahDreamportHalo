using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A weapon tag (weap) as the game stores it: triggers (rate of fire, projectiles per shot, error, charging, heat, age), magazines
    /// (rounds, reload / chamber time, the magazine equipment), heat and battery ("age"), zoom, melee damage, autoaim, the third and
    /// first person models, the firing / empty / misfire effects per trigger. Offsets: DevTools/reference/layouts.txt (struct Weapon :
    /// Item : Object). Units as the cache file stores them: seconds, radians, world units, fractions. Unity-free.
    /// </summary>
    public sealed class HaloWeaponDef
    {
        /// <summary>How the weapon is handled in VR (ours: Halo has one weapon kind; the reload / holster rules follow from it).</summary>
        public enum Family { Rifle, Pistol, Sniper, Shotgun, Rocket, Needler, Flamethrower, PlasmaRifle, PlasmaPistol, FuelRod, Flag, Ball, Other }

        public sealed class FiringEffect
        {
            public int ShotMin, ShotMax;
            public HaloTag Firing, Misfire, Empty, FiringDamage, MisfireDamage, EmptyDamage;
        }

        public sealed class Trigger
        {
            public uint Flags;
            public float RateMin, RateMax;               // rounds a second at the start / after accelerating (0 = once per pull)
            public float AccelerationTime, DecelerationTime;
            public int Magazine;                         // -1 = none (heat only)
            public int RoundsPerShot, MinimumRoundsLoaded, ProjectilesBetweenContrails;
            public float ErrorMin, ErrorMax, ErrorAccelerationTime, ErrorDecelerationTime;
            public float ChargingTime, ChargedTime; public int OverchargedAction; public float SpewTime;
            public HaloTag ChargingEffect;
            public int Distribution, ProjectilesPerShot; public float DistributionAngle;
            public float MinimumError, ErrorAngleMin, ErrorAngleMax;   // radians
            public HVec3 FirstPersonOffset;
            public HaloTag Projectile;
            public float HeatPerRound, AgePerRound, OverloadTime;
            public readonly List<FiringEffect> Effects = new List<FiringEffect>();

            public bool DoesNotRepeat => (Flags & (1u << 3)) != 0;
            public bool CanFireWithPartialAmmo => (Flags & (1u << 2)) != 0;
            public bool RandomFiringEffects => (Flags & (1u << 1)) != 0;
            public bool UseErrorWhenUnzoomed => (Flags & (1u << 10)) != 0;
            public bool IdenticalError => (Flags & (1u << 12)) != 0;
            public bool ProjectilesUseWeaponOrigin => (Flags & (1u << 5)) != 0;

            /// <summary>The firing effect for the n-th shot of a burst (Halo picks by the shot count bounds).</summary>
            public FiringEffect EffectFor(int shot, Random rng)
            {
                if (Effects.Count == 0) return null;
                if (RandomFiringEffects && rng != null) return Effects[rng.Next(Effects.Count)];
                foreach (var e in Effects) if (shot >= e.ShotMin && (shot <= e.ShotMax || e.ShotMax <= 0)) return e;
                return Effects[0];
            }
        }

        public sealed class MagazineObject { public int Rounds; public HaloTag Equipment; }

        public sealed class Magazine
        {
            public uint Flags;
            public int RoundsRecharged, RoundsTotalInitial, RoundsReservedMaximum, RoundsLoadedMaximum, RoundsReloaded;
            public float ReloadTime, ChamberTime;
            public HaloTag ReloadingEffect, ChamberingEffect;
            public readonly List<MagazineObject> Objects = new List<MagazineObject>();
            public bool WastesRoundsWhenReloaded => (Flags & 1) != 0;
            public bool EveryRoundMustBeChambered => (Flags & 2) != 0;
        }

        public readonly HaloTag Tag;
        public readonly string Code;                     // the tag's own short label ("ar", "hp", ...)
        public string Name;                              // the game's pickup name ("Assault Rifle"), filled by HaloCombatCatalog from the HUD strings
        public readonly int WeaponType;                  // 0 undefined, 1 shotgun, 2 needler, 3 plasma pistol, 4 plasma rifle, 5 rocket launcher
        public readonly uint Flags;
        public readonly int PickupTextIndex;
        public readonly HaloTag Model, AnimationGraph, FirstPersonModel, FirstPersonAnimations, HudInterface;
        public readonly HaloTag PickupSound, ZoomInSound, ZoomOutSound, ReadyEffect, OverheatedEffect, OverheatDetonation;
        public readonly HaloTag MeleeDamage, MeleeResponse, DetonatingEffect, DetonationEffect, CollisionSound;
        public readonly float ReadyTime;
        public readonly float HeatRecoveryThreshold, OverheatedThreshold, HeatDetonationThreshold, HeatDetonationFraction, HeatLossRate;
        public readonly int ZoomLevels; public readonly float ZoomMin, ZoomMax;
        public readonly float AutoaimAngle, AutoaimRange, MagnetismAngle, MagnetismRange, DeviationAngle;
        public readonly float AgeHeatRecoveryPenalty, AgeRateOfFirePenalty, AgeMisfireStart, AgeMisfireChance;
        public readonly float ActiveCamoDing, ActiveCamoRegrowthRate;
        public readonly List<Magazine> Magazines = new List<Magazine>();
        public readonly List<Trigger> Triggers = new List<Trigger>();
        /// <summary>An object attachment (Object +320) whose primary scale follows the weapon's "primary / secondary charged" input
        /// (weapon a..d in -> object function -> attachment primary scale): the plasma pistol's overcharge light (+ lens flare) at
        /// "secondary trigger flare1" and its charging.lsnd at "primary trigger". Scale = clamp(charge, Lower, Upper).</summary>
        public sealed class ChargeAttachment { public HaloTag Type; public string Marker; public float Lower, Upper; public bool Secondary; }

        public readonly List<ChargeAttachment> ChargeAttachments = new List<ChargeAttachment>();


        /// <summary>Seconds of the first-person animations (30 frames a second; 0 = the weapon has none): melee, overheated vent, reloads.</summary>
        public float MeleeSeconds, OverheatSeconds, ReloadEmptySeconds, ReloadFullSeconds;

        public readonly Family Kind;

        public HaloWeaponDef(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            Model = r.Ref(40);
            AnimationGraph = r.Ref(56);
            PickupTextIndex = r.I16(384);
            CollisionSound = r.Ref(600);
            DetonatingEffect = r.Ref(744);
            DetonationEffect = r.Ref(760);
            Flags = r.U32(776);
            Code = r.Str32(780);
            ReadyTime = r.F32(824);
            ReadyEffect = r.Ref(828);
            HeatRecoveryThreshold = r.F32(844);
            OverheatedThreshold = r.F32(848);
            HeatDetonationThreshold = r.F32(852);
            HeatDetonationFraction = r.F32(856);
            HeatLossRate = r.F32(860);
            OverheatedEffect = r.Ref(884);
            OverheatDetonation = r.Ref(900);
            MeleeDamage = r.Ref(916);
            MeleeResponse = r.Ref(932);
            ZoomLevels = r.I16(986);
            ZoomMin = r.F32(988); ZoomMax = r.F32(992);
            AutoaimAngle = r.F32(996); AutoaimRange = r.F32(1000);
            MagnetismAngle = r.F32(1004); MagnetismRange = r.F32(1008);
            DeviationAngle = r.F32(1012);
            AgeHeatRecoveryPenalty = r.F32(1088); AgeRateOfFirePenalty = r.F32(1092);
            AgeMisfireStart = r.F32(1096); AgeMisfireChance = r.F32(1100);
            FirstPersonModel = r.Ref(1116);
            FirstPersonAnimations = r.Ref(1132);
            HudInterface = r.Ref(1152);
            PickupSound = r.Ref(1168);
            ZoomInSound = r.Ref(1184);
            ZoomOutSound = r.Ref(1200);
            ActiveCamoDing = r.F32(1228); ActiveCamoRegrowthRate = r.F32(1232);
            WeaponType = r.U16(1250);

            foreach (var g in r.Block(1264, 112))
            {
                var m = new Magazine
                {
                    Flags = g.U32(0), RoundsRecharged = g.I16(4), RoundsTotalInitial = g.I16(6), RoundsReservedMaximum = g.I16(8), RoundsLoadedMaximum = g.I16(10),
                    ReloadTime = g.F32(20), RoundsReloaded = g.I16(24), ChamberTime = g.F32(28), ReloadingEffect = g.Ref(56), ChamberingEffect = g.Ref(72),
                };
                foreach (var o in g.Block(100, 28)) m.Objects.Add(new MagazineObject { Rounds = o.I16(0), Equipment = o.Ref(12) });
                Magazines.Add(m);
            }

            foreach (var g in r.Block(1276, 276))
            {
                var t = new Trigger
                {
                    Flags = g.U32(0), RateMin = g.F32(4), RateMax = g.F32(8), AccelerationTime = g.F32(12), DecelerationTime = g.F32(16),
                    Magazine = g.I16(32), RoundsPerShot = g.I16(34), MinimumRoundsLoaded = g.I16(36), ProjectilesBetweenContrails = g.I16(38),
                    ErrorMin = g.F32(48), ErrorMax = g.F32(52), ErrorAccelerationTime = g.F32(56), ErrorDecelerationTime = g.F32(60),
                    ChargingTime = g.F32(72), ChargedTime = g.F32(76), OverchargedAction = g.U16(80), SpewTime = g.F32(88), ChargingEffect = g.Ref(92),
                    Distribution = g.U16(108), ProjectilesPerShot = g.I16(110), DistributionAngle = g.F32(112),
                    MinimumError = g.F32(120), ErrorAngleMin = g.F32(124), ErrorAngleMax = g.F32(128), FirstPersonOffset = g.Vec3(132),
                    Projectile = g.Ref(148), HeatPerRound = g.F32(184), AgePerRound = g.F32(188), OverloadTime = g.F32(196),
                };
                foreach (var f in g.Block(264, 132))
                    t.Effects.Add(new FiringEffect
                    {
                        ShotMin = f.I16(0), ShotMax = f.I16(2), Firing = f.Ref(36), Misfire = f.Ref(52), Empty = f.Ref(68),
                        FiringDamage = f.Ref(84), MisfireDamage = f.Ref(100), EmptyDamage = f.Ref(116),
                    });
                Triggers.Add(t);
            }

            ReadChargeAttachments(r);
            ReadAnimationTimes();
            Kind = Classify();
        }

        // ------------------------------------------------------------------ what kind of weapon

        public bool MustBePickedUp => (Flags & (1u << 7)) != 0;
        public bool PreventsGrenadeThrowing => (Flags & (1u << 6)) != 0;
        public bool PreventsMelee => (Flags & (1u << 9)) != 0;
        public bool DetonatesWhenDropped => (Flags & (1u << 10)) != 0;
        public bool CannotFireAtMaximumAge => (Flags & (1u << 11)) != 0;
        public bool DoesNotCountTowardMaximum => (Flags & (1u << 4)) != 0;

        /// <summary>Uses heat (plasma weapons, the flamethrower): overheats at the threshold, cools at the loss rate.</summary>
        public bool UsesHeat { get { foreach (var t in Triggers) if (t.HeatPerRound > 0f) return true; return false; } }
        /// <summary>A battery weapon (Halo's "age"): no magazines, the charge runs down per shot.</summary>
        public bool UsesBattery { get { foreach (var t in Triggers) if (t.AgePerRound > 0f) return true; return false; } }
        public bool HasMagazine => Magazines.Count > 0 && Magazines[0].RoundsLoadedMaximum > 0;
        public Magazine PrimaryMagazine => Magazines.Count > 0 ? Magazines[0] : null;
        public Trigger PrimaryTrigger => Triggers.Count > 0 ? Triggers[0] : null;

        /// <summary>Halo's one-handed weapons (the cyborg holds them in one hand): pistol, plasma pistol, needler, plasma rifle; the flag and the ball.</summary>
        public bool OneHanded
        {
            get
            {
                switch (Kind)
                {
                    case Family.Pistol: case Family.PlasmaPistol: case Family.Needler: case Family.PlasmaRifle: case Family.Flag: case Family.Ball: return true;
                    default: return false;
                }
            }
        }

        /// <summary>Carried on the back (long guns) rather than at the waist (ours: the user's rule, see Game/Combat NOTES).</summary>
        public bool LongGun => !OneHanded;

        private Family Classify()
        {
            string n = Tag.Name.ToLowerInvariant();
            if (n.Contains("\\flag\\")) return Family.Flag;
            if (n.Contains("\\ball\\")) return Family.Ball;
            if (WeaponType == 1 || n.Contains("shotgun")) return Family.Shotgun;
            if (WeaponType == 2 || n.Contains("needler")) return Family.Needler;
            if (WeaponType == 3 || n.Contains("plasma pistol")) return Family.PlasmaPistol;
            if (WeaponType == 4 || n.Contains("plasma rifle")) return Family.PlasmaRifle;
            if (WeaponType == 5 || n.Contains("rocket")) return Family.Rocket;
            if (n.Contains("flamethrower")) return Family.Flamethrower;
            if (n.Contains("plasma_cannon") || n.Contains("fuel rod")) return Family.FuelRod;
            if (n.Contains("sniper")) return Family.Sniper;
            if (n.Contains("pistol")) return Family.Pistol;
            if (Magazines.Count > 0) return Family.Rifle;
            return Family.Other;
        }

        /// <summary>Weapon a..d in (+816: 10 primary charged, 11 secondary charged) feed the object functions A..D (+344, "scale function
        /// by" 1..4 = a..d in, bounds +40/+44); an attachment's primary scale (+48: 1..4 = A..D out) picks a function.</summary>
        private void ReadChargeAttachments(TagReader r)
        {
            int fnCount = r.Count(344);
            foreach (var at in r.Block(320, 72))
            {
                HaloTag type = at.Ref(0);
                int outIndex = at.U16(48) - 1;
                if (type == null || outIndex < 0 || outIndex >= fnCount) continue;
                var fn = r.Element(344, outIndex, 360);
                int input = fn.U16(12) - 1;
                if (input < 0 || input > 3) continue;
                int weaponIn = r.U16(816 + 2 * input);
                if (weaponIn != 10 && weaponIn != 11) continue;
                float lo = fn.F32(40), hi = fn.F32(44);
                if (hi <= lo) { lo = 0f; hi = 1f; }
                ChargeAttachments.Add(new ChargeAttachment { Type = type, Marker = at.Str32(16), Lower = lo, Upper = hi, Secondary = weaponIn == 11 });
            }
        }

        private void ReadAnimationTimes()
        {
            var at = FirstPersonAnimations;
            if (at == null) return;
            try
            {
                foreach (var a in at.Root.Block(116, 180))
                {
                    string name = a.Str32(0);
                    float s = a.U16(34) / 30f;
                    if (name.EndsWith(" melee")) MeleeSeconds = s;
                    else if (name.EndsWith(" overheated")) OverheatSeconds = s;
                    else if (name.EndsWith(" reload-empty")) ReloadEmptySeconds = s;
                    else if (name.EndsWith(" reload-full")) ReloadFullSeconds = s;
                }
            }
            catch (Exception) { /* a weapon without readable animations keeps zeros (ours: defaults apply) */ }
        }

        public override string ToString() => (Name ?? Code) + " (" + Tag.Name + ")";
    }
}

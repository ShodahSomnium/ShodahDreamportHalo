using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>Halo's actor types (actr "type", ActorType).</summary>
    public enum HaloActorType
    {
        Elite = 0, Jackal = 1, Grunt = 2, Hunter = 3, Engineer = 4, Assassin = 5, Player = 6, Marine = 7, Crew = 8,
        CombatForm = 9, InfectionForm = 10, CarrierForm = 11, Monitor = 12, Sentinel = 13, None = 14, MountedWeapon = 15,
    }

    /// <summary>A min..max pair as the tags store them (float[2]).</summary>
    public struct HRange
    {
        public float Min, Max;
        public HRange(float min, float max) { Min = min; Max = max; }
        public float Pick(Random r) { return Max > Min ? Min + (float)r.NextDouble() * (Max - Min) : Min; }
        public float Mid => (Min + Max) * 0.5f;
        public override string ToString() => Min.ToString("0.###") + ".." + Max.ToString("0.###");
    }

    /// <summary>
    /// An actor definition (actr): what a kind of AI perceives and how it behaves - vision cones, hearing, perception times, cover,
    /// danger / panic / retreat triggers, crouching, evasion, melee, berserk, firing position timing. Offsets: layouts.txt struct Actor.
    /// Angles in radians, distances in Halo world units, times in seconds. Unity-free.
    /// </summary>
    public sealed class HaloActorDef
    {
        // ActorFlags bits
        public const uint CanSeeInDarkness = 1u << 0, SneakUncoveringTarget = 1u << 1, ShootAtTargetsLastLocation = 1u << 4,
            TryToStayStillWhenCrouched = 1u << 5, CrouchWhenNotInCombat = 1u << 6, CrouchWhenGuarding = 1u << 7, MustCrouchToShoot = 1u << 9,
            PanicWhenSurprised = 1u << 10, AlwaysChargeAtEnemies = 1u << 11, GetsInVehiclesWithPlayer = 1u << 12, StartFiringBeforeAligned = 1u << 13,
            StandingMustMoveForward = 1u << 14, CrouchingMustMoveForward = 1u << 15, DefensiveCrouchWhileCharging = 1u << 16, UsesStalkingBehavior = 1u << 17,
            StalkingFreezeIfExposed = 1u << 18, AlwaysBerserkInAttackingMode = 1u << 19, BerserkingUsesPanickedMovement = 1u << 20, Flying = 1u << 21,
            PanickedByUnopposableEnemy = 1u << 22, CrouchWhenHidingFromUnopposable = 1u << 23, AlwaysChargeInAttackingMode = 1u << 24,
            DiveOffLedges = 1u << 25, Swarm = 1u << 26, SuicidalMeleeAttack = 1u << 27, CannotMoveWhileCrouching = 1u << 28,
            FixedCrouchFacing = 1u << 29, CrouchWhenInLineOfFire = 1u << 30, AvoidFriendsLineOfFire = 1u << 31;
        // ActorMoreFlags bits
        public const uint AvoidAllEnemyAttackVectors = 1u << 0, MustStandToFire = 1u << 1, MustStopToFire = 1u << 2, DisallowVehicleCombat = 1u << 3,
            PathfindingIgnoresDanger = 1u << 4, PanicInGroups = 1u << 5, NoCorpseShooting = 1u << 6;

        public readonly HaloTag Tag;
        public readonly uint Flags, MoreFlags;
        public readonly HaloActorType Type;
        public readonly float MaxVisionDistance, CentralVisionAngle, MaxVisionAngle, PeripheralVisionAngle, PeripheralDistance;
        public readonly HVec3 StandingGunOffset, CrouchingGunOffset;
        public readonly float HearingDistance, NoticeProjectileChance, NoticeVehicleChance;
        public readonly float CombatPerceptionTime, GuardPerceptionTime, NonCombatPerceptionTime;
        public readonly float DiveIntoCoverChance, EmergeFromCoverChance, DiveFromGrenadeChance, PathfindingRadius, GlassIgnoranceChance;
        public readonly float StationaryMovementDist, FreeFlyingSidestep, BeginMovingAngle;
        public readonly float MaxAimingDeviationYaw, MaxAimingDeviationPitch, MaxLookingDeviationYaw, MaxLookingDeviationPitch;
        public readonly float NoncombatLookDeltaL, NoncombatLookDeltaR, CombatLookDeltaL, CombatLookDeltaR;
        public readonly HRange NoncombatIdleFacing, CombatIdleFacing, GuardIdleFacing;
        public readonly int UnreachableDangerTrigger, VehicleDangerTrigger, PlayerDangerTrigger;
        public readonly HRange DangerTriggerTime;
        public readonly int FriendsKilledTrigger, FriendsRetreatingTrigger;
        public readonly HRange RetreatTime, CoweringTime;
        public readonly float FriendKilledPanicChance;
        public readonly HaloActorType LeaderType;
        public readonly float LeaderKilledPanicChance, PanicDamageThreshold, SurpriseDistance;
        public readonly HRange HideBehindCoverTime;
        public readonly float HideTargetNotVisibleTime, HideShieldFraction, AttackShieldFraction, PursueShieldFraction;
        /// <summary>0 never, 1 danger, 2 low shields, 3 hide behind shield, 4 any target, 5 flood shamble.</summary>
        public readonly int DefensiveCrouchType;
        public readonly float AttackingCrouchThreshold, DefendingCrouchThreshold, MinStandTime, MinCrouchTime, DefendingHideTimeModifier;
        public readonly float AttackingEvasionThreshold, DefendingEvasionThreshold, EvasionSeekCoverChance, EvasionDelayTime;
        public readonly float MaxSeekCoverDistance, CoverDamageThreshold, StalkingDiscoveryTime, StalkingMaxDistance, StationaryFacingAngle, ChangeFacingStandTime;
        public readonly HRange UncoverDelayTime, TargetSearchTime, PursuitPositionTime;
        public readonly int NumPositionsCoord, NumPositionsNormal;
        public readonly float MeleeAttackDelay, MeleeFudgeFactor, MeleeChargeTime;
        public readonly HRange MeleeLeapRange;
        public readonly float MeleeLeapVelocity, MeleeLeapChance, MeleeLeapBallistic;
        public readonly float BerserkDamageAmount, BerserkDamageThreshold, BerserkProximity, SuicideSensingDist, BerserkGrenadeChance;
        public readonly HRange GuardPositionTime, CombatPositionTime;
        public readonly float OldPositionAvoidDist, FriendAvoidDist;
        public readonly HRange NoncombatIdleSpeechTime, CombatIdleSpeechTime;

        public bool Has(uint flag) => (Flags & flag) != 0;
        public bool HasMore(uint flag) => (MoreFlags & flag) != 0;
        public bool IsFlying => Has(Flying);
        public bool IsSwarm => Has(Swarm);

        public HaloActorDef(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            Flags = r.U32(0); MoreFlags = r.U32(4);
            Type = (HaloActorType)r.U16(20);
            MaxVisionDistance = r.F32(24); CentralVisionAngle = r.F32(28); MaxVisionAngle = r.F32(32);
            PeripheralVisionAngle = r.F32(40); PeripheralDistance = r.F32(44);
            StandingGunOffset = r.Vec3(52); CrouchingGunOffset = r.Vec3(64);
            HearingDistance = r.F32(76); NoticeProjectileChance = r.F32(80); NoticeVehicleChance = r.F32(84);
            CombatPerceptionTime = r.F32(96); GuardPerceptionTime = r.F32(100); NonCombatPerceptionTime = r.F32(104);
            DiveIntoCoverChance = r.F32(128); EmergeFromCoverChance = r.F32(132); DiveFromGrenadeChance = r.F32(136);
            PathfindingRadius = r.F32(140); GlassIgnoranceChance = r.F32(144); StationaryMovementDist = r.F32(148);
            FreeFlyingSidestep = r.F32(152); BeginMovingAngle = r.F32(156);
            MaxAimingDeviationYaw = r.F32(164); MaxAimingDeviationPitch = r.F32(168);
            MaxLookingDeviationYaw = r.F32(172); MaxLookingDeviationPitch = r.F32(176);
            NoncombatLookDeltaL = r.F32(180); NoncombatLookDeltaR = r.F32(184); CombatLookDeltaL = r.F32(188); CombatLookDeltaR = r.F32(192);
            NoncombatIdleFacing = R(r, 220); GuardIdleFacing = R(r, 244); CombatIdleFacing = R(r, 268);
            UnreachableDangerTrigger = r.U16(616); VehicleDangerTrigger = r.U16(618); PlayerDangerTrigger = r.U16(620);
            DangerTriggerTime = R(r, 624);
            FriendsKilledTrigger = r.I16(632); FriendsRetreatingTrigger = r.I16(634);
            RetreatTime = R(r, 648); CoweringTime = R(r, 664);
            FriendKilledPanicChance = r.F32(672); LeaderType = (HaloActorType)r.U16(676);
            LeaderKilledPanicChance = r.F32(680); PanicDamageThreshold = r.F32(684); SurpriseDistance = r.F32(688);
            HideBehindCoverTime = R(r, 720); HideTargetNotVisibleTime = r.F32(728);
            HideShieldFraction = r.F32(732); AttackShieldFraction = r.F32(736); PursueShieldFraction = r.F32(740);
            DefensiveCrouchType = r.U16(760);
            AttackingCrouchThreshold = r.F32(764); DefendingCrouchThreshold = r.F32(768); MinStandTime = r.F32(772); MinCrouchTime = r.F32(776);
            DefendingHideTimeModifier = r.F32(780); AttackingEvasionThreshold = r.F32(784); DefendingEvasionThreshold = r.F32(788);
            EvasionSeekCoverChance = r.F32(792); EvasionDelayTime = r.F32(796); MaxSeekCoverDistance = r.F32(800);
            CoverDamageThreshold = r.F32(804); StalkingDiscoveryTime = r.F32(808); StalkingMaxDistance = r.F32(812);
            StationaryFacingAngle = r.F32(816); ChangeFacingStandTime = r.F32(820);
            UncoverDelayTime = R(r, 828); TargetSearchTime = R(r, 836); PursuitPositionTime = R(r, 844);
            NumPositionsCoord = r.U16(852); NumPositionsNormal = r.U16(854);
            MeleeAttackDelay = r.F32(888); MeleeFudgeFactor = r.F32(892); MeleeChargeTime = r.F32(896);
            MeleeLeapRange = R(r, 900); MeleeLeapVelocity = r.F32(908); MeleeLeapChance = r.F32(912); MeleeLeapBallistic = r.F32(916);
            BerserkDamageAmount = r.F32(920); BerserkDamageThreshold = r.F32(924); BerserkProximity = r.F32(928);
            SuicideSensingDist = r.F32(932); BerserkGrenadeChance = r.F32(936);
            GuardPositionTime = R(r, 952); CombatPositionTime = R(r, 960);
            OldPositionAvoidDist = r.F32(968); FriendAvoidDist = r.F32(972);
            NoncombatIdleSpeechTime = R(r, 1016); CombatIdleSpeechTime = R(r, 1024);
        }

        internal static HRange R(TagReader r, int o) => new HRange(r.F32(o), r.F32(o + 4));

        public string Describe()
        {
            return $"{Type} flags {Flags:X8}/{MoreFlags:X2} vision {MaxVisionDistance:0.#}wu cone {Deg(CentralVisionAngle)}/{Deg(MaxVisionAngle)} periph {Deg(PeripheralVisionAngle)} {PeripheralDistance:0.#}wu hear {HearingDistance:0.#}"
                + $" percept c{CombatPerceptionTime:0.##}/g{GuardPerceptionTime:0.##}/n{NonCombatPerceptionTime:0.##} cover dive {DiveIntoCoverChance:0.##} grenade-dive {DiveFromGrenadeChance:0.##}"
                + $" panic friend {FriendKilledPanicChance:0.##} leader({LeaderType}) {LeaderKilledPanicChance:0.##} dmg {PanicDamageThreshold:0.##} cower {CoweringTime} retreat {RetreatTime} friendsKilled {FriendsKilledTrigger}"
                + $" shields hide {HideShieldFraction:0.##} attack {AttackShieldFraction:0.##} pursue {PursueShieldFraction:0.##} crouch {DefensiveCrouchType} evade a{AttackingEvasionThreshold:0.##}/d{DefendingEvasionThreshold:0.##}"
                + $" melee delay {MeleeAttackDelay:0.##} charge {MeleeChargeTime:0.##} leap {MeleeLeapRange} p{MeleeLeapChance:0.##} berserk dmg {BerserkDamageThreshold:0.##} prox {BerserkProximity:0.##}"
                + $" pos guard {GuardPositionTime} combat {CombatPositionTime} search {TargetSearchTime}";
        }

        internal static string Deg(float rad) => (rad * 57.29578f).ToString("0");
    }

    /// <summary>
    /// An actor variant (actv): an actor definition + the unit it drives + its weapon and how it uses it (rate of fire, error, bursts,
    /// ranges), grenades, drops, vitality and the major variant it upgrades to. Offsets: layouts.txt struct ActorVariant. Unity-free.
    /// </summary>
    public sealed class HaloActorVariantDef
    {
        public const uint CanShootWhileFlying = 1u << 0, HasUnlimitedGrenades = 1u << 2, MovementSwitchingStayWithFriends = 1u << 3,
            ActiveCamouflage = 1u << 4, SuperActiveCamouflage = 1u << 5, CannotUseRangedWeapons = 1u << 6, PreferPassengerSeat = 1u << 7;

        public readonly HaloTag Tag;
        public readonly uint Flags;
        public readonly HaloTag ActorTag, Unit, MajorVariant, Weapon, Equipment;
        public HaloActorDef Actor;
        /// <summary>0 always run, 1 always crouch, 2 switch types.</summary>
        public readonly int MovementType;
        public readonly float InitialCrouchChance;
        public readonly HRange CrouchTime, RunTime;
        public readonly float MaximumFiringDistance, RateOfFire, ProjectileError;
        public readonly HRange FirstBurstDelayTime;
        public readonly float NewTargetFiringPatternTime, SurpriseDelayTime, SurpriseFireWildlyTime, DeathFireWildlyChance, DeathFireWildlyTime;
        public readonly HRange DesiredCombatRange;
        public readonly HVec3 CustomStandGunOffset, CustomCrouchGunOffset;
        public readonly float TargetTracking, TargetLeading, WeaponDamageModifier, DamagePerSecond;
        public readonly float BurstOriginRadius, BurstOriginAngle;
        public readonly HRange BurstReturnLength;
        public readonly float BurstReturnAngle;
        public readonly HRange BurstDuration, BurstSeparation;
        public readonly float BurstAngularVelocity;
        public readonly float SpecialDamageModifier, SpecialProjectileError;
        public readonly float NewTargetBurstDuration, NewTargetBurstSeparation, NewTargetRateOfFire, NewTargetProjectileError;
        public readonly float MovingBurstDuration, MovingBurstSeparation, MovingRateOfFire, MovingProjectileError;
        public readonly float BerserkBurstDuration, BerserkBurstSeparation, BerserkRateOfFire, BerserkProjectileError;
        public readonly float SuperBallisticRange, BombardmentRange, ModifiedVisionRange;
        /// <summary>0 none, 1 overcharge, 2 secondary trigger.</summary>
        public readonly int SpecialFireMode;
        /// <summary>0 never, 1 enemy visible, 2 enemy out of sight, 3 strafing.</summary>
        public readonly int SpecialFireSituation;
        public readonly float SpecialFireChance, SpecialFireDelay;
        public readonly float MeleeRange, MeleeAbortRange;
        public readonly HRange BerserkFiringRanges;
        public readonly float BerserkMeleeRange, BerserkMeleeAbortRange;
        /// <summary>0 frag, 1 plasma (GrenadeType).</summary>
        public readonly int GrenadeType;
        /// <summary>0 toss, 1 lob, 2 bounce.</summary>
        public readonly int TrajectoryType;
        /// <summary>0 never, 1 visible target, 2 seek cover.</summary>
        public readonly int GrenadeStimulus;
        public readonly int MinimumEnemyCount;
        public readonly float EnemyRadius, GrenadeVelocity;
        public readonly HRange GrenadeRanges;
        public readonly float CollateralDamageRadius, GrenadeChance, GrenadeCheckTime, EncounterGrenadeTimeout;
        public readonly int GrenadeCountMin, GrenadeCountMax;
        public readonly float DontDropGrenadesChance;
        public readonly HRange DropWeaponLoaded;
        public readonly int DropWeaponAmmoMin, DropWeaponAmmoMax;
        public readonly float BodyVitality, ShieldVitality, ShieldSappingRadius;
        public readonly int ForcedShaderPermutation;

        public bool Has(uint flag) => (Flags & flag) != 0;

        public HaloActorVariantDef(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            Flags = r.U32(0);
            ActorTag = r.Ref(4); Unit = r.Ref(20); MajorVariant = r.Ref(36);
            MovementType = r.U16(76); InitialCrouchChance = r.F32(80);
            CrouchTime = HaloActorDef.R(r, 84); RunTime = HaloActorDef.R(r, 92);
            Weapon = r.Ref(100);
            MaximumFiringDistance = r.F32(116); RateOfFire = r.F32(120); ProjectileError = r.F32(124);
            FirstBurstDelayTime = HaloActorDef.R(r, 128); NewTargetFiringPatternTime = r.F32(136);
            SurpriseDelayTime = r.F32(140); SurpriseFireWildlyTime = r.F32(144); DeathFireWildlyChance = r.F32(148); DeathFireWildlyTime = r.F32(152);
            DesiredCombatRange = HaloActorDef.R(r, 156);
            CustomStandGunOffset = r.Vec3(164); CustomCrouchGunOffset = r.Vec3(176);
            TargetTracking = r.F32(188); TargetLeading = r.F32(192); WeaponDamageModifier = r.F32(196); DamagePerSecond = r.F32(200);
            BurstOriginRadius = r.F32(204); BurstOriginAngle = r.F32(208); BurstReturnLength = HaloActorDef.R(r, 212); BurstReturnAngle = r.F32(220);
            BurstDuration = HaloActorDef.R(r, 224); BurstSeparation = HaloActorDef.R(r, 232); BurstAngularVelocity = r.F32(240);
            SpecialDamageModifier = r.F32(248); SpecialProjectileError = r.F32(252);
            NewTargetBurstDuration = r.F32(256); NewTargetBurstSeparation = r.F32(260); NewTargetRateOfFire = r.F32(264); NewTargetProjectileError = r.F32(268);
            MovingBurstDuration = r.F32(280); MovingBurstSeparation = r.F32(284); MovingRateOfFire = r.F32(288); MovingProjectileError = r.F32(292);
            BerserkBurstDuration = r.F32(304); BerserkBurstSeparation = r.F32(308); BerserkRateOfFire = r.F32(312); BerserkProjectileError = r.F32(316);
            SuperBallisticRange = r.F32(328); BombardmentRange = r.F32(332); ModifiedVisionRange = r.F32(336);
            SpecialFireMode = r.U16(340); SpecialFireSituation = r.U16(342); SpecialFireChance = r.F32(344); SpecialFireDelay = r.F32(348);
            MeleeRange = r.F32(352); MeleeAbortRange = r.F32(356); BerserkFiringRanges = HaloActorDef.R(r, 360);
            BerserkMeleeRange = r.F32(368); BerserkMeleeAbortRange = r.F32(372);
            GrenadeType = r.U16(384); TrajectoryType = r.U16(386); GrenadeStimulus = r.U16(388); MinimumEnemyCount = r.I16(390);
            EnemyRadius = r.F32(392); GrenadeVelocity = r.F32(400); GrenadeRanges = HaloActorDef.R(r, 404);
            CollateralDamageRadius = r.F32(412); GrenadeChance = r.F32(416); GrenadeCheckTime = r.F32(420); EncounterGrenadeTimeout = r.F32(424);
            Equipment = r.Ref(448);
            GrenadeCountMin = r.I16(464); GrenadeCountMax = r.I16(466);
            DontDropGrenadesChance = r.F32(468); DropWeaponLoaded = HaloActorDef.R(r, 472);
            DropWeaponAmmoMin = r.I16(480); DropWeaponAmmoMax = r.I16(482);
            BodyVitality = r.F32(512); ShieldVitality = r.F32(516); ShieldSappingRadius = r.F32(520);
            ForcedShaderPermutation = r.I16(524);
        }

        public string Describe()
        {
            return $"unit {Name(Unit)} weapon {Name(Weapon)} major {Name(MajorVariant)} flags {Flags:X2} move {MovementType} crouch {InitialCrouchChance:0.##}"
                + $" fire dist {MaximumFiringDistance:0.#}wu rof {RateOfFire:0.##} err {HaloActorDef.Deg(ProjectileError)} burst {BurstDuration}s every {BurstSeparation}s first {FirstBurstDelayTime}"
                + $" range {DesiredCombatRange} dmg x{WeaponDamageModifier:0.##} lead {TargetLeading:0.##} track {TargetTracking:0.##}"
                + $" grenade {(GrenadeType == 0 ? "frag" : "plasma")} {GrenadeCountMin}-{GrenadeCountMax} chance {GrenadeChance:0.##} stim {GrenadeStimulus} traj {TrajectoryType} v {GrenadeVelocity:0.##} ranges {GrenadeRanges}"
                + $" melee {MeleeRange:0.##}/{MeleeAbortRange:0.##} drop loaded {DropWeaponLoaded} ammo {DropWeaponAmmoMin}-{DropWeaponAmmoMax} body {BodyVitality:0.#} shield {ShieldVitality:0.#}";
        }

        static string Name(HaloTag t) { if (t == null) return "-"; int i = t.Name.LastIndexOf('\\'); return i >= 0 ? t.Name.Substring(i + 1) : t.Name; }
    }

    /// <summary>The actor variants / actors of one map, parsed once.</summary>
    public sealed class HaloActorCatalog
    {
        public readonly HaloMap Map;
        readonly Dictionary<HaloTag, HaloActorVariantDef> _variants = new Dictionary<HaloTag, HaloActorVariantDef>();
        readonly Dictionary<HaloTag, HaloActorDef> _actors = new Dictionary<HaloTag, HaloActorDef>();

        public HaloActorCatalog(HaloMap map) { Map = map; }

        public HaloActorVariantDef Variant(HaloTag tag)
        {
            if (tag == null || !tag.Is("actv")) return null;
            HaloActorVariantDef v;
            if (_variants.TryGetValue(tag, out v)) return v;
            try { v = new HaloActorVariantDef(tag); v.Actor = Actor(v.ActorTag); }
            catch (Exception e) { HaloLog.Warn("AI: actor variant " + tag.Name + " unreadable: " + e.Message); v = null; }
            _variants[tag] = v;
            return v;
        }

        public HaloActorDef Actor(HaloTag tag)
        {
            if (tag == null) return null;
            HaloActorDef a;
            if (_actors.TryGetValue(tag, out a)) return a;
            try { a = new HaloActorDef(tag); }
            catch (Exception e) { HaloLog.Warn("AI: actor " + tag.Name + " unreadable: " + e.Message); a = null; }
            _actors[tag] = a;
            return a;
        }
    }
}

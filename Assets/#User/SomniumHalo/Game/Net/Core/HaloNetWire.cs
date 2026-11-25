using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>What a player's slot says about its owner: not there, in the Dreamport (hub / lobby / loading), or in a match.</summary>
    public enum HaloNetStatus { Free = 0, Present = 1, Playing = 2 }

    /// <summary>The match the writer is in. Values are on the wire.</summary>
    public enum HaloMatchState { None = 0, Running = 1, Finished = 2 }

    /// <summary>A position (metres) without a Unity type, so the wire runs in the offline harness too.</summary>
    public struct HaloV3
    {
        public float X, Y, Z;

        public HaloV3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }

        public static HaloV3 Lerp(HaloV3 a, HaloV3 b, float t)
        {
            return new HaloV3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        }

        public static float Distance(HaloV3 a, HaloV3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static HaloV3 operator +(HaloV3 a, HaloV3 b) { return new HaloV3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static HaloV3 operator -(HaloV3 a, HaloV3 b) { return new HaloV3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }

        public override string ToString() { return "(" + X.ToString("0.###") + ", " + Y.ToString("0.###") + ", " + Z.ToString("0.###") + ")"; }
    }

    /// <summary>A rotation (x, y, z, w) without a Unity type.</summary>
    public struct HaloQ
    {
        public float X, Y, Z, W;

        public HaloQ(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }

        public static HaloQ Identity { get { return new HaloQ(0f, 0f, 0f, 1f); } }

        /// <summary>The angle between two rotations in degrees.</summary>
        public static float AngleDeg(HaloQ a, HaloQ b)
        {
            float d = Math.Abs(a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W);
            if (d > 1f) d = 1f;
            return (float)(2.0 * Math.Acos(d) * 180.0 / Math.PI);
        }

        /// <summary>Normalised linear interpolation along the shorter way.</summary>
        public static HaloQ Nlerp(HaloQ a, HaloQ b, float t)
        {
            float dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            float s = dot < 0f ? -1f : 1f;
            var r = new HaloQ(a.X + (b.X * s - a.X) * t, a.Y + (b.Y * s - a.Y) * t, a.Z + (b.Z * s - a.Z) * t, a.W + (b.W * s - a.W) * t);
            float len = (float)Math.Sqrt(r.X * r.X + r.Y * r.Y + r.Z * r.Z + r.W * r.W);
            if (len < 1e-6f) return Identity;
            return new HaloQ(r.X / len, r.Y / len, r.Z / len, r.W / len);
        }
    }

    /// <summary>One hit of the shooter's hit ring: who was hit, for how much (Halo vitality points), with what, how.</summary>
    public struct HaloNetHit
    {
        public int Seq;          // 0..255: the shooter's running hit number
        public int Victim;       // slot
        public float Amount;     // vitality points, 0.25 steps, up to 1023.75
        public int Cause;        // tag index of the weapon / grenade / vehicle (0xFFFF none)
        public int Effect;       // tag index of the damage effect (jpt!) (0xFFFF none)
        public bool Melee, Head, Explosion, Back, Instant;
        /// <summary>The host's AI dealt it (campaign co-op: the victim applies it as from the AI, SourcePlayer -2).</summary>
        public bool Ai;

        public bool Valid { get { return Amount > 0f; } }
    }

    /// <summary>
    /// Everything one player says about itself, decoded (or about to be encoded). The counters (fire, throw, melee, deaths, hit seq, objective
    /// event, pickups, drops) are the events: a reader that sees one change plays that event once.
    /// </summary>
    public struct HaloNetState
    {
        // int 4 flags
        public HaloNetStatus Status;
        public bool Vr, Alive, Master, Camo, Zoomed, Crouch, Reloading, GrenadeInHand, GrenadePlasma, TriggerHeld, Overheated;
        /// <summary>A weapon is in the hand (else both are on the body); the writer drives its vehicle (its pose is in the record).</summary>
        public bool InHand, Driving;
        public int Team;                  // the side 0 red / 1 blue (absolute)
        public HaloMatchState MatchState;
        public int Carry;                 // HaloLocal.Carry code 0..7
        public int Color;                 // FFA armour colour 0..30, 31 = none
        // ints 5-7
        public float Health, Shield;      // 0..1, 0..3
        public int Kills, Deaths, Assists, Score, LivesLeft;   // Deaths is also the death counter (mod 4096); LivesLeft -1 = infinite
        public int MapPrint;              // 16-bit fingerprint of the loaded map file (tag count / scenario id): the same map build on both sides
        // ints 8-9
        public int ZoneHash, MatchStart;  // the map name hash, the match start UTC seconds
        // ints 10-11
        public int Held, Stowed;          // tag indices, 0xFFFF none
        public int FireCounter, ThrowCounter, MeleeCounter;   // 8 / 4 / 4 bits
        public int Frags, Plasmas;
        /// <summary>Reload counter (4 bits, +1 per reload start: the Desk module plays the reload on monitor avatars).</summary>
        public int ReloadCounter;
        /// <summary>The last reload started on an empty magazine (the weapon plays "reload-empty" rather than "reload-full").</summary>
        public bool ReloadEmpty;
        // int 12 death event
        public int Killer;                // slot, HaloNetWire.NoSlot = none
        public int DeathCause;            // tag index, 0xFFFF none
        public bool DeathMelee, DeathHead, DeathExplosion, DeathSplatter, DeathSuicide, DeathGrenade, DeathBetrayal;
        // ints 13-21 hits
        public int HitSeq;
        public HaloNetHit Hit0, Hit1, Hit2, Hit3;
        // pose
        public float RootYaw;             // degrees 0..360
        public HaloV3 Root, Head, LHand, RHand;
        public HaloQ HeadRot, LRot, RRot;
        // last shot / throw
        public HaloV3 Muzzle, MuzzleDir;
        public int ShotWeapon;            // tag index of the weapon of the last shot
        public HaloV3 ThrowPos, ThrowVel;
        public int ThrowTag;              // tag index (proj / eqip) of the last throw
        // vehicle
        public int Vehicle, Seat;         // scenario vehicle index / seat index, -1 = on foot
        public float VehicleHealth;       // 0..1
        public int VehicleFlags;          // 8 bits (the Vehicles module's)
        public HaloV3 VehiclePos, VehicleVel, Aim;
        public HaloQ VehicleRot;
        // the instance master's command and lobby (only the master's record matters)
        public int GameZone, GameSeq, MatchSeq, GameStart;
        public int LobbyPhase, LobbyCount, LobbyZone, RulesVersion, RulesA, RulesB, RulesC;
        public bool RulesOk;
        // objective event, pickups, drops
        public int ObjCounter, ObjCode, ObjArg;
        public HaloV3 ObjPos;
        public int PickupCounter, Pickup0, Pickup1;        // items of the ring entries (the newest is at PickupCounter & 1)
        public int DropCounter, DropWeapon, DropRounds, DropReserve, DropCharge;
        public HaloV3 DropPos, DropVel;

        public HaloNetHit HitAt(int ring)
        {
            switch (ring & 3)
            {
                case 0: return Hit0;
                case 1: return Hit1;
                case 2: return Hit2;
                default: return Hit3;
            }
        }

        public void SetHit(int ring, HaloNetHit h)
        {
            switch (ring & 3)
            {
                case 0: Hit0 = h; break;
                case 1: Hit1 = h; break;
                case 2: Hit2 = h; break;
                default: Hit3 = h; break;
            }
        }

        /// <summary>A fresh state: nothing held, nobody killed me, no vehicle.</summary>
        public static HaloNetState Empty()
        {
            var s = default(HaloNetState);
            s.Killer = HaloNetWire.NoSlot;
            s.Held = s.Stowed = s.ShotWeapon = s.ThrowTag = s.DeathCause = s.DropWeapon = HaloNetWire.NoTag;
            s.Vehicle = s.Seat = -1;
            s.Color = 31;
            s.LivesLeft = -1;
            s.HeadRot = s.LRot = s.RRot = s.VehicleRot = HaloQ.Identity;
            return s;
        }
    }

    /// <summary>
    /// A player slot's 64 ints, "Halo Net Player 0..49" (a NetworkBridgeData64 each): the Black Ops 2 / Euro Truck Sim / WoW Dreamports' slot
    /// pattern. Only the slot's owner writes it. Pure C# (the offline harness runs encode / decode). Positions are int millimetres ("mm") or
    /// two ints of centimetres ("cm2": 3 x 21 bits signed, +-10 km), hands are offsets from the head (3 x 10 bits of 2.5 mm, +-1.28 m),
    /// rotations "smallest three" (10 bits a component), directions yaw 16 | pitch 16, velocities 3 x 10 bits of 0.1 m/s (+-51 m/s).
    /// Written about every 100 ms; Fusion sends an int only when it changes.
    ///
    ///   0  magic 'HLO1' (0 = never written)
    ///   1  seq, +1 on every write            2  stamp: the writer's clock in ms (the time of the pose samples)
    ///   3  owner PlayerId+1 (0-15) | claim generation (16-23) | protocol (24-27)
    ///   4  flags: status (0-1) | VR (2) | alive (3) | team (4) | match state (5-6) | instance master bit (7) | camo (8) | zoomed (9) |
    ///      crouch (10) | reloading (11) | grenade in hand (12) | it is plasma (13) | trigger held (14) | overheated (15) | carry (16-18) |
    ///      colour (19-23, 31 none) | weapon in hand (24) | driving (25)
    ///   5  health 0-255 of 1 (0-7) | shield 0-255 of 3 (8-15) | kills (16-27)
    ///   6  deaths counter mod 4096 (0-11, a change = the owner died) | assists (12-21) | lives left (22-25, 15 = infinite)
    ///   7  score, signed 16 (0-15) | map fingerprint (16-31)
    ///   8  zone: FNV-1a of the lower-case map name      9  match start, UTC seconds
    ///  10  held weapon tag index (0-15, 0xFFFF none) | fire counter (16-23) | throw counter (24-27) | melee counter (28-31)
    ///  11  stowed weapon tag index (0-15) | frag grenades (16-19) | plasma grenades (20-23) | reload counter (24-27) | that reload was on an empty magazine (28)
    ///  12  death event: killer slot (0-5, 63 none) | cause tag index (6-21) | melee 22 | head 23 | explosion 24 | splatter 25 | suicide 26 |
    ///      grenade 27 | betrayal 28 - valid for the latest death
    ///  13  hit number counter (0-7) | root yaw (8-23)
    ///  14-17 hit ring: victim (0-5) | amount in 0.25 points (6-17) | hit number (18-25) | melee 26 | head 27 | explosion 28 | back 29 | instant 30.
    ///        The SHOOTER measures a hit against the victim's hit boxes and writes it; the VICTIM applies it once.
    ///  18-19 the hits' cause tag indices (two 16-bit per int)   20-21 the hits' damage effect tag indices
    ///  22-24 avatar root (mm)   25-27 head (mm)   28 head rotation
    ///  29 left hand offset from the head   30 its rotation   31 right hand offset   32 its rotation   (0 for a monitor player)
    ///  33-34 muzzle at the last shot (cm2)   35 its direction
    ///  36-37 grenade release position (cm2)  38 its velocity
    ///  39  last throw tag index (0-15) | last shot weapon tag index (16-31)
    ///  40  vehicle index (0-7, 255 none) | seat (8-15, 255 none) | vehicle health 0-255 (16-23) | vehicle flags (24-31)
    ///  41-42 vehicle position (cm2, the driver's)  43 vehicle rotation  44 vehicle velocity  45 aim direction (turret / vehicle weapon)
    ///  46  the game the instance master commanded: map name hash (0 = none)
    ///  47  the master's command number 1..65535 (0-15) | the number of the game this player is in (16-31, 0 = none)
    ///  48  the commanded game's start, UTC seconds
    ///  49  lobby: phase (0-1: 0 none, 1 open, 2 countdown) | countdown (2-6) | rules version (7-18) | rules check (19-26)
    ///  50-52 the rules (HaloNetRules.Pack: three ints)   53 the lobby's map name hash
    ///  54  objective event counter (0-7) | its code (8-15) | pickup counter (16-19) | drop counter (20-23) | the objective event argument (24-31)
    ///  55-56 the objective event's position (cm2)
    ///  57  pickup ring: entry 0 item (0-15) | entry 1 item (16-31); the newest entry is at pickup counter & 1
    ///  58  last drop: weapon tag index (0-15) | rounds loaded (16-31)
    ///  59-60 drop position (cm2)   61 drop velocity
    ///  62  the drop: rounds in reserve (0-15) | battery 0-255 (16-23)
    ///  63  spare 0
    /// </summary>
    public static class HaloNetWire
    {
        /// <summary>Scene objects "Halo Net Player 0".."Halo Net Player 49".</summary>
        public const string ObjectPrefix = "Halo Net Player ";

        public const int Magic = 0x484C4F31;   // 'HLO1'
        public const int Protocol = 1;
        public const int Ints = 64;
        public const int Used = 63;
        public const int MaxSlots = 50;
        public const int NoSlot = 63;
        public const int NoTag = 0xFFFF;
        public const int HitRing = 4;

        public const int AtMagic = 0, AtSeq = 1, AtStamp = 2, AtOwner = 3, AtFlags = 4, AtVitals = 5, AtCounts = 6, AtScore = 7, AtZone = 8, AtMatch = 9,
            AtHeld = 10, AtStowed = 11, AtDeath = 12, AtHitSeq = 13, AtHit = 14, AtHitCause = 18, AtHitEffect = 20, AtRoot = 22, AtHead = 25, AtHeadRot = 28,
            AtLHand = 29, AtLRot = 30, AtRHand = 31, AtRRot = 32, AtMuzzle = 33, AtMuzzleDir = 35, AtThrowPos = 36, AtThrowVel = 38, AtTags = 39,
            AtVehicle = 40, AtVehiclePos = 41, AtVehicleRot = 43, AtVehicleVel = 44, AtAim = 45, AtGame = 46, AtGameSeq = 47, AtGameStart = 48,
            AtLobby = 49, AtRulesA = 50, AtRulesB = 51, AtRulesC = 52, AtLobbyZone = 53, AtEvents = 54, AtObjPos = 55, AtPickups = 57, AtDrop = 58,
            AtDropPos = 59, AtDropVel = 61, AtDropAmmo = 62;

        private const int FlagVr = 1 << 2, FlagAlive = 1 << 3, FlagTeam = 1 << 4, FlagMaster = 1 << 7, FlagCamo = 1 << 8, FlagZoom = 1 << 9, FlagCrouch = 1 << 10,
            FlagReload = 1 << 11, FlagGrenade = 1 << 12, FlagPlasma = 1 << 13, FlagTrigger = 1 << 14, FlagOverheat = 1 << 15, FlagInHand = 1 << 24, FlagDriving = 1 << 25;

        // ------------------------------------------------------------------ the slot header

        /// <summary>The slot's owner as a PlayerId, -1 when never written or free.</summary>
        public static int OwnerOf(int[] from)
        {
            return from != null && from.Length > AtOwner && from[AtMagic] == Magic ? (from[AtOwner] & 0xFFFF) - 1 : -1;
        }

        public static int GenerationOf(int[] from)
        {
            return from != null && from.Length > AtOwner && from[AtMagic] == Magic ? (from[AtOwner] >> 16) & 0xFF : 0;
        }

        public static HaloNetStatus StatusOf(int[] from)
        {
            if (from == null || from.Length < Used || from[AtMagic] != Magic) return HaloNetStatus.Free;
            int s = from[AtFlags] & 3;
            return s <= 2 ? (HaloNetStatus)s : HaloNetStatus.Free;
        }

        /// <summary>The free record: the magic, seq + 1, the stamp, owner 0 with the claim generation kept, all else 0.</summary>
        public static void WriteFree(int[] into, int stampMs)
        {
            int seq = into[AtSeq] + 1;
            int generation = GenerationOf(into);
            Array.Clear(into, 0, into.Length);
            into[AtMagic] = Magic;
            into[AtSeq] = seq;
            into[AtStamp] = stampMs;
            into[AtOwner] = generation << 16;
        }

        /// <summary>A fresh claim: every int cleared, then owner = this client, the new generation, status Present.</summary>
        public static void WriteClaim(int[] into, int localId, int generation, int stampMs)
        {
            int seq = into[AtMagic] == Magic ? into[AtSeq] + 1 : 1;
            Array.Clear(into, 0, into.Length);
            into[AtMagic] = Magic;
            into[AtSeq] = seq;
            into[AtStamp] = stampMs;
            into[AtOwner] = ((localId < 0 ? 0 : localId + 1) & 0xFFFF) | ((generation & 0xFF) << 16) | (Protocol << 24);
            into[AtFlags] = (int)HaloNetStatus.Present;
            into[AtHeld] = NoTag;
            into[AtStowed] = NoTag;
            into[AtDeath] = NoSlot | (NoTag << 6);
            into[AtVehicle] = 0xFFFF;
        }

        // ------------------------------------------------------------------ encode

        /// <summary>Ints 4-61 from a state; the header (0-3) is kept. True when anything changed.</summary>
        public static bool Pack(in HaloNetState s, int[] into)
        {
            int flags = ((int)s.Status & 3) | (s.Vr ? FlagVr : 0) | (s.Alive ? FlagAlive : 0) | ((s.Team & 1) != 0 ? FlagTeam : 0) | (((int)s.MatchState & 3) << 5)
                        | (s.Master ? FlagMaster : 0) | (s.Camo ? FlagCamo : 0) | (s.Zoomed ? FlagZoom : 0) | (s.Crouch ? FlagCrouch : 0) | (s.Reloading ? FlagReload : 0)
                        | (s.GrenadeInHand ? FlagGrenade : 0) | (s.GrenadePlasma ? FlagPlasma : 0) | (s.TriggerHeld ? FlagTrigger : 0) | (s.Overheated ? FlagOverheat : 0)
                        | ((s.Carry & 7) << 16) | ((s.Color & 31) << 19) | (s.InHand ? FlagInHand : 0) | (s.Driving ? FlagDriving : 0);
            bool c = false;
            c |= Set(into, AtFlags, flags);
            c |= Set(into, AtVitals, Unit8(s.Health, 1f) | (Unit8(s.Shield, 3f) << 8) | (Clamp(s.Kills, 0, 4095) << 16));
            c |= Set(into, AtCounts, (s.Deaths & 0xFFF) | (Clamp(s.Assists, 0, 1023) << 12) | ((s.LivesLeft < 0 ? 15 : Clamp(s.LivesLeft, 0, 14)) << 22));
            c |= Set(into, AtScore, (Clamp(s.Score, -32768, 32767) & 0xFFFF) | ((s.MapPrint & 0xFFFF) << 16));
            c |= Set(into, AtZone, s.ZoneHash);
            c |= Set(into, AtMatch, s.MatchStart);
            c |= Set(into, AtHeld, (s.Held & 0xFFFF) | ((s.FireCounter & 0xFF) << 16) | ((s.ThrowCounter & 0xF) << 24) | ((s.MeleeCounter & 0xF) << 28));
            c |= Set(into, AtStowed, (s.Stowed & 0xFFFF) | (Clamp(s.Frags, 0, 15) << 16) | (Clamp(s.Plasmas, 0, 15) << 20) | ((s.ReloadCounter & 0xF) << 24) | (s.ReloadEmpty ? 1 << 28 : 0));
            c |= Set(into, AtDeath, (Clamp(s.Killer, 0, NoSlot) & 0x3F) | ((s.DeathCause & 0xFFFF) << 6) | (s.DeathMelee ? 1 << 22 : 0) | (s.DeathHead ? 1 << 23 : 0)
                                    | (s.DeathExplosion ? 1 << 24 : 0) | (s.DeathSplatter ? 1 << 25 : 0) | (s.DeathSuicide ? 1 << 26 : 0) | (s.DeathGrenade ? 1 << 27 : 0)
                                    | (s.DeathBetrayal ? 1 << 28 : 0));
            c |= Set(into, AtHitSeq, (s.HitSeq & 0xFF) | (Yaw16(s.RootYaw) << 8));
            for (int i = 0; i < HitRing; i++) c |= Set(into, AtHit + i, PackHit(s.HitAt(i)));
            c |= Set(into, AtHitCause, (s.Hit0.Cause & 0xFFFF) | ((s.Hit1.Cause & 0xFFFF) << 16));
            c |= Set(into, AtHitCause + 1, (s.Hit2.Cause & 0xFFFF) | ((s.Hit3.Cause & 0xFFFF) << 16));
            c |= Set(into, AtHitEffect, (s.Hit0.Effect & 0xFFFF) | ((s.Hit1.Effect & 0xFFFF) << 16));
            c |= Set(into, AtHitEffect + 1, (s.Hit2.Effect & 0xFFFF) | ((s.Hit3.Effect & 0xFFFF) << 16));
            c |= SetMm(into, AtRoot, s.Root);
            c |= SetMm(into, AtHead, s.Head);
            c |= Set(into, AtHeadRot, PackQuat(s.HeadRot));
            if (s.Vr)
            {
                c |= Set(into, AtLHand, PackOffset(s.LHand - s.Head));
                c |= Set(into, AtLRot, PackQuat(s.LRot));
                c |= Set(into, AtRHand, PackOffset(s.RHand - s.Head));
                c |= Set(into, AtRRot, PackQuat(s.RRot));
            }
            else
            {
                for (int i = 0; i < 4; i++) c |= Set(into, AtLHand + i, 0);
            }

            c |= SetCm(into, AtMuzzle, s.Muzzle);
            c |= Set(into, AtMuzzleDir, PackDir(s.MuzzleDir));
            c |= SetCm(into, AtThrowPos, s.ThrowPos);
            c |= Set(into, AtThrowVel, PackVel(s.ThrowVel));
            c |= Set(into, AtTags, (s.ThrowTag & 0xFFFF) | ((s.ShotWeapon & 0xFFFF) << 16));
            bool inVehicle = s.Vehicle >= 0 && s.Vehicle < 255;
            c |= Set(into, AtVehicle, (inVehicle ? s.Vehicle & 0xFF : 0xFF) | ((s.Seat >= 0 && s.Seat < 255 ? s.Seat & 0xFF : 0xFF) << 8)
                                      | (Unit8(s.VehicleHealth, 1f) << 16) | ((s.VehicleFlags & 0xFF) << 24));
            if (inVehicle)
            {
                c |= SetCm(into, AtVehiclePos, s.VehiclePos);
                c |= Set(into, AtVehicleRot, PackQuat(s.VehicleRot));
                c |= Set(into, AtVehicleVel, PackVel(s.VehicleVel));
            }
            else
            {
                for (int i = 0; i < 4; i++) c |= Set(into, AtVehiclePos + i, 0);
            }

            c |= Set(into, AtAim, PackDir(s.Aim));
            c |= Set(into, AtGame, s.GameZone);
            c |= Set(into, AtGameSeq, (s.GameSeq & 0xFFFF) | ((s.MatchSeq & 0xFFFF) << 16));
            c |= Set(into, AtGameStart, s.GameStart);
            c |= Set(into, AtLobby, (s.LobbyPhase & 3) | ((s.LobbyCount & 31) << 2) | ((s.RulesVersion & 0xFFF) << 7) | (RulesCheck(s.RulesA, s.RulesB, s.RulesC) << 19));
            c |= Set(into, AtRulesA, s.RulesA);
            c |= Set(into, AtRulesB, s.RulesB);
            c |= Set(into, AtRulesC, s.RulesC);
            c |= Set(into, AtLobbyZone, s.LobbyZone);
            c |= Set(into, AtEvents, (s.ObjCounter & 0xFF) | ((s.ObjCode & 0xFF) << 8) | ((s.PickupCounter & 0xF) << 16) | ((s.DropCounter & 0xF) << 20) | ((s.ObjArg & 0xFF) << 24));
            c |= SetCm(into, AtObjPos, s.ObjPos);
            c |= Set(into, AtPickups, (s.Pickup0 & 0xFFFF) | ((s.Pickup1 & 0xFFFF) << 16));
            c |= Set(into, AtDrop, (s.DropWeapon & 0xFFFF) | (Clamp(s.DropRounds, 0, 0xFFFF) << 16));
            c |= SetCm(into, AtDropPos, s.DropPos);
            c |= Set(into, AtDropVel, PackVel(s.DropVel));
            c |= Set(into, AtDropAmmo, Clamp(s.DropReserve, 0, 0xFFFF) | (Clamp(s.DropCharge, 0, 255) << 16));
            return c;
        }

        // ------------------------------------------------------------------ decode

        /// <summary>False unless the slot holds this build's magic. Everything is overwritten.</summary>
        public static bool Unpack(int[] from, out HaloNetState s)
        {
            s = HaloNetState.Empty();
            if (from == null || from.Length < Used || from[AtMagic] != Magic) return false;
            int f = from[AtFlags];
            int st = f & 3;
            s.Status = st <= 2 ? (HaloNetStatus)st : HaloNetStatus.Free;
            s.Vr = (f & FlagVr) != 0;
            s.Alive = (f & FlagAlive) != 0;
            s.Team = (f & FlagTeam) != 0 ? 1 : 0;
            int ms = (f >> 5) & 3;
            s.MatchState = ms <= 2 ? (HaloMatchState)ms : HaloMatchState.None;
            s.Master = (f & FlagMaster) != 0;
            s.Camo = (f & FlagCamo) != 0;
            s.Zoomed = (f & FlagZoom) != 0;
            s.Crouch = (f & FlagCrouch) != 0;
            s.Reloading = (f & FlagReload) != 0;
            s.GrenadeInHand = (f & FlagGrenade) != 0;
            s.GrenadePlasma = (f & FlagPlasma) != 0;
            s.TriggerHeld = (f & FlagTrigger) != 0;
            s.Overheated = (f & FlagOverheat) != 0;
            s.Carry = (f >> 16) & 7;
            s.Color = (f >> 19) & 31;
            s.InHand = (f & FlagInHand) != 0;
            s.Driving = (f & FlagDriving) != 0;
            int v = from[AtVitals];
            s.Health = (v & 0xFF) / 255f;
            s.Shield = ((v >> 8) & 0xFF) * (3f / 255f);
            s.Kills = (v >> 16) & 0xFFF;
            int n = from[AtCounts];
            s.Deaths = n & 0xFFF;
            s.Assists = (n >> 12) & 0x3FF;
            int lives = (n >> 22) & 0xF;
            s.LivesLeft = lives == 15 ? -1 : lives;
            s.Score = (short)(from[AtScore] & 0xFFFF);
            s.MapPrint = (from[AtScore] >> 16) & 0xFFFF;
            s.ZoneHash = from[AtZone];
            s.MatchStart = from[AtMatch];
            int g = from[AtHeld];
            s.Held = g & 0xFFFF;
            s.FireCounter = (g >> 16) & 0xFF;
            s.ThrowCounter = (g >> 24) & 0xF;
            s.MeleeCounter = (g >> 28) & 0xF;
            int w = from[AtStowed];
            s.Stowed = w & 0xFFFF;
            s.Frags = (w >> 16) & 0xF;
            s.Plasmas = (w >> 20) & 0xF;
            s.ReloadCounter = (w >> 24) & 0xF;
            s.ReloadEmpty = (w & (1 << 28)) != 0;
            int d = from[AtDeath];
            s.Killer = d & 0x3F;
            s.DeathCause = (d >> 6) & 0xFFFF;
            s.DeathMelee = (d & (1 << 22)) != 0;
            s.DeathHead = (d & (1 << 23)) != 0;
            s.DeathExplosion = (d & (1 << 24)) != 0;
            s.DeathSplatter = (d & (1 << 25)) != 0;
            s.DeathSuicide = (d & (1 << 26)) != 0;
            s.DeathGrenade = (d & (1 << 27)) != 0;
            s.DeathBetrayal = (d & (1 << 28)) != 0;
            s.HitSeq = from[AtHitSeq] & 0xFF;
            s.RootYaw = ((from[AtHitSeq] >> 8) & 0xFFFF) * (360f / 65536f);
            for (int i = 0; i < HitRing; i++)
            {
                HaloNetHit h = UnpackHit(from[AtHit + i]);
                int cause = from[AtHitCause + (i >> 1)], effect = from[AtHitEffect + (i >> 1)];
                h.Cause = (i & 1) == 0 ? cause & 0xFFFF : (cause >> 16) & 0xFFFF;
                h.Effect = (i & 1) == 0 ? effect & 0xFFFF : (effect >> 16) & 0xFFFF;
                s.SetHit(i, h);
            }

            s.Root = GetMm(from, AtRoot);
            s.Head = GetMm(from, AtHead);
            s.HeadRot = UnpackQuat(from[AtHeadRot]);
            if (s.Vr)
            {
                s.LHand = s.Head + UnpackOffset(from[AtLHand]);
                s.LRot = UnpackQuat(from[AtLRot]);
                s.RHand = s.Head + UnpackOffset(from[AtRHand]);
                s.RRot = UnpackQuat(from[AtRRot]);
            }

            s.Muzzle = GetCm(from, AtMuzzle);
            s.MuzzleDir = UnpackDir(from[AtMuzzleDir]);
            s.ThrowPos = GetCm(from, AtThrowPos);
            s.ThrowVel = UnpackVel(from[AtThrowVel]);
            s.ThrowTag = from[AtTags] & 0xFFFF;
            s.ShotWeapon = (from[AtTags] >> 16) & 0xFFFF;
            int veh = from[AtVehicle];
            s.Vehicle = (veh & 0xFF) == 0xFF ? -1 : veh & 0xFF;
            s.Seat = ((veh >> 8) & 0xFF) == 0xFF ? -1 : (veh >> 8) & 0xFF;
            s.VehicleHealth = ((veh >> 16) & 0xFF) / 255f;
            s.VehicleFlags = (veh >> 24) & 0xFF;
            if (s.Vehicle >= 0)
            {
                s.VehiclePos = GetCm(from, AtVehiclePos);
                s.VehicleRot = UnpackQuat(from[AtVehicleRot]);
                s.VehicleVel = UnpackVel(from[AtVehicleVel]);
            }

            s.Aim = UnpackDir(from[AtAim]);
            s.GameZone = from[AtGame];
            s.GameSeq = from[AtGameSeq] & 0xFFFF;
            s.MatchSeq = (from[AtGameSeq] >> 16) & 0xFFFF;
            s.GameStart = from[AtGameStart];
            int lobby = from[AtLobby];
            s.LobbyPhase = lobby & 3;
            s.LobbyCount = (lobby >> 2) & 31;
            s.RulesVersion = (lobby >> 7) & 0xFFF;
            s.RulesA = from[AtRulesA];
            s.RulesB = from[AtRulesB];
            s.RulesC = from[AtRulesC];
            s.LobbyZone = from[AtLobbyZone];
            s.RulesOk = s.RulesVersion != 0 && ((lobby >> 19) & 0xFF) == RulesCheck(s.RulesA, s.RulesB, s.RulesC);
            int ev = from[AtEvents];
            s.ObjCounter = ev & 0xFF;
            s.ObjCode = (ev >> 8) & 0xFF;
            s.PickupCounter = (ev >> 16) & 0xF;
            s.DropCounter = (ev >> 20) & 0xF;
            s.ObjArg = (ev >> 24) & 0xFF;
            s.ObjPos = GetCm(from, AtObjPos);
            s.Pickup0 = from[AtPickups] & 0xFFFF;
            s.Pickup1 = (from[AtPickups] >> 16) & 0xFFFF;
            s.DropWeapon = from[AtDrop] & 0xFFFF;
            s.DropRounds = (from[AtDrop] >> 16) & 0xFFFF;
            s.DropPos = GetCm(from, AtDropPos);
            s.DropVel = UnpackVel(from[AtDropVel]);
            s.DropReserve = from[AtDropAmmo] & 0xFFFF;
            s.DropCharge = (from[AtDropAmmo] >> 16) & 0xFF;
            return true;
        }

        // ------------------------------------------------------------------ the parts

        public static int PackHit(HaloNetHit h)
        {
            if (!(h.Amount > 0f)) return 0;
            int q = (int)Math.Round(h.Amount * 4.0);
            if (q < 1) q = 1;
            if (q > 4095) q = 4095;
            uint v = (uint)(Clamp(h.Victim, 0, 63) & 0x3F) | (uint)(q << 6) | (uint)((h.Seq & 0xFF) << 18)
                     | (h.Melee ? 1u << 26 : 0u) | (h.Head ? 1u << 27 : 0u) | (h.Explosion ? 1u << 28 : 0u) | (h.Back ? 1u << 29 : 0u) | (h.Instant ? 1u << 30 : 0u) | (h.Ai ? 1u << 31 : 0u);
            return unchecked((int)v);
        }

        public static HaloNetHit UnpackHit(int packed)
        {
            uint v = unchecked((uint)packed);
            return new HaloNetHit
            {
                Victim = (int)(v & 0x3F),
                Amount = ((v >> 6) & 0xFFF) / 4f,
                Seq = (int)((v >> 18) & 0xFF),
                Melee = (v & (1u << 26)) != 0,
                Head = (v & (1u << 27)) != 0,
                Explosion = (v & (1u << 28)) != 0,
                Back = (v & (1u << 29)) != 0,
                Instant = (v & (1u << 30)) != 0,
                Ai = (v & (1u << 31)) != 0,
                Cause = NoTag,
                Effect = NoTag,
            };
        }

        /// <summary>A value of 0..max as 0..255.</summary>
        public static int Unit8(float value, float max)
        {
            if (!(value > 0f)) return 0;
            int q = (int)Math.Round(value / max * 255.0);
            return q > 255 ? 255 : q;
        }

        /// <summary>Millimetres, clamped to the int range.</summary>
        public static int Mm(float metres)
        {
            double v = Math.Round(metres * 1000.0);
            if (double.IsNaN(v)) return 0;
            if (v > int.MaxValue) return int.MaxValue;
            if (v < int.MinValue) return int.MinValue;
            return (int)v;
        }

        public static float FromMm(int mm) { return mm / 1000f; }

        private static bool SetMm(int[] a, int at, HaloV3 p)
        {
            bool c = Set(a, at, Mm(p.X));
            c |= Set(a, at + 1, Mm(p.Y));
            c |= Set(a, at + 2, Mm(p.Z));
            return c;
        }

        private static HaloV3 GetMm(int[] a, int at)
        {
            return new HaloV3(FromMm(a[at]), FromMm(a[at + 1]), FromMm(a[at + 2]));
        }

        private const int Cm21Max = (1 << 20) - 1;

        private static long Cm21(float metres)
        {
            double v = Math.Round(metres * 100.0);
            if (double.IsNaN(v)) v = 0;
            if (v > Cm21Max) v = Cm21Max;
            if (v < -Cm21Max) v = -Cm21Max;
            return ((long)v) & 0x1FFFFF;
        }

        private static float FromCm21(long q)
        {
            long v = q & 0x1FFFFF;
            if ((v & 0x100000) != 0) v -= 0x200000;
            return v / 100f;
        }

        /// <summary>A position in two ints: 3 x 21 bits of signed centimetres (x 0-20, y 21-41, z 42-62 of the 64-bit pair).</summary>
        public static void PackCm2(HaloV3 p, out int lo, out int hi)
        {
            long bits = Cm21(p.X) | (Cm21(p.Y) << 21) | (Cm21(p.Z) << 42);
            lo = unchecked((int)(bits & 0xFFFFFFFFL));
            hi = unchecked((int)((bits >> 32) & 0xFFFFFFFFL));
        }

        public static HaloV3 UnpackCm2(int lo, int hi)
        {
            long bits = ((long)(uint)lo) | ((long)(uint)hi << 32);
            return new HaloV3(FromCm21(bits), FromCm21(bits >> 21), FromCm21(bits >> 42));
        }

        public static bool SetCm(int[] a, int at, HaloV3 p)
        {
            int lo, hi;
            PackCm2(p, out lo, out hi);
            bool c = Set(a, at, lo);
            c |= Set(a, at + 1, hi);
            return c;
        }

        public static HaloV3 GetCm(int[] a, int at) { return UnpackCm2(a[at], a[at + 1]); }

        /// <summary>An offset of up to +-1.28 m as 3 x 10 bits of 2.5 mm (a hand from the head).</summary>
        public static int PackOffset(HaloV3 d)
        {
            return O10(d.X) << 20 | O10(d.Y) << 10 | O10(d.Z);
        }

        public static HaloV3 UnpackOffset(int packed)
        {
            return new HaloV3((((packed >> 20) & 0x3FF) - 512) * 0.0025f, (((packed >> 10) & 0x3FF) - 512) * 0.0025f, ((packed & 0x3FF) - 512) * 0.0025f);
        }

        private static int O10(float v)
        {
            if (float.IsNaN(v)) return 512;
            int i = (int)Math.Round(v / 0.0025f) + 512;
            return i < 0 ? 0 : i > 1023 ? 1023 : i;
        }

        private const float R = 0.70710678f;

        /// <summary>"Smallest three": the largest component is left out (2 bits say which), the other three take 10 bits each, in [-0.7071, 0.7071].</summary>
        public static int PackQuat(HaloQ q)
        {
            float x = q.X, y = q.Y, z = q.Z, w = q.W;
            float len = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
            if (len < 1e-6f || float.IsNaN(len)) { x = y = z = 0f; w = 1f; }
            else { x /= len; y /= len; z /= len; w /= len; }
            int idx = 3;
            float max = Math.Abs(w);
            if (Math.Abs(x) > max) { idx = 0; max = Math.Abs(x); }
            if (Math.Abs(y) > max) { idx = 1; max = Math.Abs(y); }
            if (Math.Abs(z) > max) { idx = 2; }
            float big = idx == 0 ? x : idx == 1 ? y : idx == 2 ? z : w;
            float sign = big < 0f ? -1f : 1f;
            float a, b, c;
            switch (idx)
            {
                case 0: a = y; b = z; c = w; break;
                case 1: a = x; b = z; c = w; break;
                case 2: a = x; b = y; c = w; break;
                default: a = x; b = y; c = z; break;
            }

            uint v = ((uint)idx << 30) | ((uint)Q10(a * sign) << 20) | ((uint)Q10(b * sign) << 10) | (uint)Q10(c * sign);
            return unchecked((int)v);
        }

        public static HaloQ UnpackQuat(int packed)
        {
            uint v = unchecked((uint)packed);
            int idx = (int)(v >> 30);
            float a = U10((int)((v >> 20) & 0x3FF)), b = U10((int)((v >> 10) & 0x3FF)), c = U10((int)(v & 0x3FF));
            float big = (float)Math.Sqrt(Math.Max(0f, 1f - a * a - b * b - c * c));
            switch (idx)
            {
                case 0: return new HaloQ(big, a, b, c);
                case 1: return new HaloQ(a, big, b, c);
                case 2: return new HaloQ(a, b, big, c);
                default: return new HaloQ(a, b, c, big);
            }
        }

        private static int Q10(float v)
        {
            float t = v / R * 0.5f + 0.5f;
            int i = (int)Math.Round(t * 1023f);
            return i < 0 ? 0 : i > 1023 ? 1023 : i;
        }

        private static float U10(int q) { return ((q / 1023f) - 0.5f) * 2f * R; }

        /// <summary>A direction (need not be unit) as yaw 16 bits | pitch 16 bits; yaw = atan2(x, z), pitch = asin(y). Zero length = (0, 0, 1).</summary>
        public static int PackDir(HaloV3 d)
        {
            float len = d.Length;
            if (len < 1e-6f || float.IsNaN(len)) return unchecked((int)(32768u << 16 | 32768u));
            float x = d.X / len, y = d.Y / len, z = d.Z / len;
            double yaw = Math.Atan2(x, z);
            double pitch = Math.Asin(Math.Max(-1.0, Math.Min(1.0, y)));
            uint qy = (uint)Math.Round((yaw / Math.PI * 0.5 + 0.5) * 65535.0);
            uint qp = (uint)Math.Round((pitch / (Math.PI / 2.0) * 0.5 + 0.5) * 65535.0);
            return unchecked((int)((qy << 16) | (qp & 0xFFFF)));
        }

        public static HaloV3 UnpackDir(int packed)
        {
            uint v = unchecked((uint)packed);
            double yaw = (((v >> 16) & 0xFFFF) / 65535.0 - 0.5) * 2.0 * Math.PI;
            double pitch = ((v & 0xFFFF) / 65535.0 - 0.5) * 2.0 * (Math.PI / 2.0);
            double cp = Math.Cos(pitch);
            return new HaloV3((float)(cp * Math.Sin(yaw)), (float)Math.Sin(pitch), (float)(cp * Math.Cos(yaw)));
        }

        /// <summary>Three 10-bit components of 0.1 m/s with an offset of 51.2 (so +-51 m/s).</summary>
        public static int PackVel(HaloV3 v)
        {
            return V10(v.X) << 20 | V10(v.Y) << 10 | V10(v.Z);
        }

        public static HaloV3 UnpackVel(int packed)
        {
            return new HaloV3((((packed >> 20) & 0x3FF) - 512) * 0.1f, (((packed >> 10) & 0x3FF) - 512) * 0.1f, ((packed & 0x3FF) - 512) * 0.1f);
        }

        private static int V10(float v)
        {
            if (float.IsNaN(v)) return 512;
            int i = (int)Math.Round(v * 10f) + 512;
            return i < 0 ? 0 : i > 1023 ? 1023 : i;
        }

        /// <summary>Degrees (any) as 16 bits of 0..360.</summary>
        public static int Yaw16(float deg)
        {
            if (float.IsNaN(deg)) return 0;
            double d = deg % 360.0;
            if (d < 0.0) d += 360.0;
            int q = (int)Math.Round(d / 360.0 * 65536.0);
            return q & 0xFFFF;
        }

        public static bool Set(int[] a, int i, int v)
        {
            if (a[i] == v) return false;
            a[i] = v;
            return true;
        }

        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        /// <summary>8 bits that must agree with the three rules ints (a torn or foreign record fails the check and its rules are not used).</summary>
        public static int RulesCheck(int a, int b, int c)
        {
            uint h = 2166136261u;
            h = (h ^ unchecked((uint)a)) * 16777619u;
            h = (h ^ unchecked((uint)b)) * 16777619u;
            h = (h ^ unchecked((uint)c)) * 16777619u;
            return (int)((h ^ (h >> 8) ^ (h >> 16) ^ (h >> 24)) & 0xFF);
        }

        /// <summary>FNV-1a 32 of the lower-case text (the map name on the wire); 0 for none.</summary>
        public static int NameHash(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            uint h = 2166136261u;
            for (int i = 0; i < name.Length; i++)
            {
                h ^= char.ToLowerInvariant(name[i]);
                h *= 16777619u;
            }

            return h == 0 ? 1 : unchecked((int)h);
        }

        /// <summary>A 16-bit fingerprint of a loaded map build (two clients with different map files must not mix tag indices).</summary>
        public static int MapPrint(int tagCount, uint scenarioId, int tagDataSize)
        {
            uint h = 2166136261u;
            h = (h ^ (uint)tagCount) * 16777619u;
            h = (h ^ scenarioId) * 16777619u;
            h = (h ^ (uint)tagDataSize) * 16777619u;
            int p = (int)((h ^ (h >> 16)) & 0xFFFF);
            return p == 0 ? 1 : p;
        }
    }
}

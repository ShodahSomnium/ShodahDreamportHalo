using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A sound tag (snd!, struct Sound in layouts.txt) read from a map. The tag keeps the playback parameters; the samples of every
    /// permutation live in MAPS\sounds.map (retail PC: all external, the data offset absolute in that file - verified for all 30 maps,
    /// 11102 entries named "&lt;tag path&gt;__&lt;pitch range&gt;__&lt;permutation&gt;"). Unity-free.
    /// Permutations below a pitch range's "actual permutation count" are the random choices; the rest are the continuation pieces of
    /// long sounds, reached through "next permutation index" chains.
    /// </summary>
    public sealed class HaloSound
    {
        public const int FormatPcm16 = 0, FormatXboxAdpcm = 1, FormatImaAdpcm = 2, FormatOgg = 3;

        // SoundClass enum (invader-defs/sound.json, option order = value).
        public const int ClassProjectileImpact = 0, ClassProjectileDetonation = 1, ClassWeaponFire = 4, ClassWeaponReady = 5,
            ClassWeaponReload = 6, ClassWeaponEmpty = 7, ClassWeaponCharge = 8, ClassWeaponOverheat = 9, ClassWeaponIdle = 10,
            ClassObjectImpacts = 13, ClassParticleImpacts = 14, ClassSlowParticleImpacts = 15, ClassUnitFootsteps = 18, ClassUnitDialog = 19,
            ClassVehicleCollision = 22, ClassVehicleEngine = 23, ClassDeviceDoor = 26, ClassDeviceForceField = 27, ClassDeviceMachinery = 28,
            ClassDeviceNature = 29, ClassDeviceComputers = 30, ClassMusic = 32, ClassAmbientNature = 33, ClassAmbientMachinery = 34,
            ClassAmbientComputers = 35, ClassFirstPersonDamage = 39, ClassScriptedDialogPlayer = 44, ClassScriptedEffect = 45,
            ClassScriptedDialogOther = 46, ClassScriptedDialogForceUnspatialized = 47, ClassGameEvent = 50;

        public HaloTag Tag;
        public uint Flags;
        public int Class, SampleRate, Channels, Format;
        /// <summary>Distances in world units (x 3.048 = metres).</summary>
        public float MinDistance, MaxDistance;
        /// <summary>The distances as stored in the tag (0 = the sound class default, which MinDistance/MaxDistance then hold).</summary>
        public float TagMinDistance, TagMaxDistance;
        public float SkipFraction, RandomPitchMin, RandomPitchMax, InnerCone, OuterCone, OuterConeGain, RandomGain, MaxBendPerSecond;
        /// <summary>Modifiers at scale 0 and scale 1 (the play's "scale" input, e.g. an engine's power): gain and pitch multiply.</summary>
        public float ZeroSkip, ZeroGain, ZeroPitch, OneSkip, OneGain, OnePitch;
        public HaloTag Promotion;
        public int PromotionCount;
        public uint LongestPermutationMs;
        public PitchRange[] PitchRanges;

        public sealed class PitchRange
        {
            public string Name;
            public float NaturalPitch, BendMin, BendMax;
            public int ActualPermutationCount;
            public Permutation[] Permutations;
        }

        public sealed class Permutation
        {
            public string Name;
            public float Skip, Gain;
            public int Format, Next, BufferSize;
            public bool External;
            /// <summary>Size and offset of the samples (absolute in sounds.map when External, else in the map file).</summary>
            public int Size, Offset;
        }

        public bool Stereo => Channels == 2;
        public bool Split => (Flags & 2) != 0;

        static readonly Dictionary<HaloTag, HaloSound> Cache = new Dictionary<HaloTag, HaloSound>();

        /// <summary>The parsed tag (cached per tag object), or null when <paramref name="t"/> is not a snd!.</summary>
        public static HaloSound Get(HaloTag t)
        {
            if (t == null || t.ClassName != "snd!") return null;
            lock (Cache)
            {
                HaloSound s;
                if (!Cache.TryGetValue(t, out s)) { s = Read(t); Cache[t] = s; }
                return s;
            }
        }

        /// <summary>Forgets the parsed tags of one map (on unload).</summary>
        public static void Forget(HaloMap map)
        {
            lock (Cache)
            {
                var drop = new List<HaloTag>();
                foreach (var k in Cache.Keys) if (k.Map == map) drop.Add(k);
                foreach (var k in drop) Cache.Remove(k);
            }
        }

        public static HaloSound Read(HaloTag t)
        {
            var r = t.Root;
            var s = new HaloSound
            {
                Tag = t,
                Flags = r.U32(0),
                Class = r.U16(4),
                SampleRate = r.U16(6) == 1 ? 44100 : 22050,
                MinDistance = r.F32(8), MaxDistance = r.F32(12),
                SkipFraction = r.F32(16), RandomPitchMin = r.F32(20), RandomPitchMax = r.F32(24),
                InnerCone = r.F32(28), OuterCone = r.F32(32), OuterConeGain = r.F32(36), RandomGain = r.F32(40), MaxBendPerSecond = r.F32(44),
                ZeroSkip = r.F32(60), ZeroGain = r.F32(64), ZeroPitch = r.F32(68),
                OneSkip = r.F32(84), OneGain = r.F32(88), OnePitch = r.F32(92),
                Channels = r.U16(108) == 1 ? 2 : 1,
                Format = r.U16(110),
                Promotion = r.Ref(112), PromotionCount = r.U16(128),
                LongestPermutationMs = r.U32(132),
            };
            s.TagMinDistance = s.MinDistance; s.TagMaxDistance = s.MaxDistance;
            if (s.MinDistance <= 0f || s.MaxDistance <= 0f)
            {
                float dmin, dmax;
                DefaultDistances(s.Class, out dmin, out dmax);
                if (s.MinDistance <= 0f) s.MinDistance = dmin;
                if (s.MaxDistance <= 0f) s.MaxDistance = dmax;
            }
            if (s.RandomPitchMin <= 0f) s.RandomPitchMin = 1f;
            if (s.RandomPitchMax <= 0f) s.RandomPitchMax = 1f;
            if (s.RandomGain <= 0f || s.RandomGain > 1f) s.RandomGain = 1f;
            if (s.ZeroPitch == 0f && s.OnePitch == 0f) { s.ZeroPitch = 1f; s.OnePitch = 1f; }
            if (s.ZeroGain == 0f && s.OneGain == 0f) s.OneGain = 1f;
            var prs = new List<PitchRange>();
            foreach (var pr in r.Block(152, 72))
            {
                var p = new PitchRange
                {
                    Name = pr.Str32(0), NaturalPitch = pr.F32(32), BendMin = pr.F32(36), BendMax = pr.F32(40),
                    ActualPermutationCount = pr.U16(44),
                };
                var perms = new List<Permutation>();
                foreach (var e in pr.Block(60, 124))
                    perms.Add(new Permutation
                    {
                        Name = e.Str32(0), Skip = e.F32(32), Gain = e.F32(36), Format = e.U16(40), Next = e.I16(42), BufferSize = e.I32(56),
                        Size = e.I32(64), External = (e.I32(68) & 1) != 0, Offset = e.I32(72),
                    });
                p.Permutations = perms.ToArray();
                if (p.ActualPermutationCount <= 0 || p.ActualPermutationCount > p.Permutations.Length) p.ActualPermutationCount = p.Permutations.Length;
                if (p.NaturalPitch <= 0f) p.NaturalPitch = 1f;
                prs.Add(p);
            }
            s.PitchRanges = prs.ToArray();
            return s;
        }

        /// <summary>The permutation indices of one sound: <paramref name="first"/> then its "next permutation" chain.</summary>
        public int[] Chain(int pitchRange, int first)
        {
            var perms = PitchRanges[pitchRange].Permutations;
            var list = new List<int>();
            int i = first;
            while (i >= 0 && i < perms.Length && list.Count < perms.Length) { if (list.Contains(i)) break; list.Add(i); i = perms[i].Next; }
            return list.ToArray();
        }

        /// <summary>The pitch range for a pitch (1 = natural): of the ranges whose bend bounds hold it the one whose natural pitch is
        /// closest (ranges overlap: warthog idle 1..1.2 and low 1..1.5 both hold 1 -> idle), else the nearest. Single-range sounds: 0.</summary>
        public int PitchRangeFor(float pitch)
        {
            if (PitchRanges.Length <= 1) return 0;
            int inside = -1; float insideD = float.MaxValue;
            for (int i = 0; i < PitchRanges.Length; i++)
            {
                var p = PitchRanges[i];
                if (pitch < p.BendMin || pitch > p.BendMax) continue;
                float d = Math.Abs(pitch - p.NaturalPitch);
                if (d < insideD) { insideD = d; inside = i; }
            }
            if (inside >= 0) return inside;
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < PitchRanges.Length; i++)
            {
                var p = PitchRanges[i];
                float d = pitch < p.BendMin ? p.BendMin - pitch : pitch - p.BendMax;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>The distances (world units) a sound class falls back to when the tag leaves them 0 - retail maps do for most
        /// weapon, UI and dialog sounds. Table as Invader's sound pre-compile step applies it (the values tool.exe / the engine use).</summary>
        public static void DefaultDistances(int cls, out float min, out float max)
        {
            switch (cls)
            {
                case ClassDeviceMachinery: case ClassDeviceForceField: case ClassAmbientMachinery: case ClassAmbientNature:
                case ClassDeviceDoor: case ClassMusic: case ClassDeviceNature:
                    min = 0.9f; max = 5f; return;
                case ClassWeaponEmpty: case ClassWeaponIdle: case ClassWeaponReady: case ClassWeaponReload: case ClassWeaponCharge:
                case ClassWeaponOverheat:
                    min = 1f; max = 9f; return;
                case ClassScriptedDialogOther: case ClassScriptedDialogPlayer: case ClassGameEvent: case ClassUnitDialog:
                case ClassScriptedDialogForceUnspatialized:
                    min = 3f; max = 20f; return;
                case ClassFirstPersonDamage: case ClassObjectImpacts: case ClassAmbientComputers: case ClassParticleImpacts:
                case ClassDeviceComputers: case ClassSlowParticleImpacts:
                    min = 0.5f; max = 3f; return;
                case ClassVehicleEngine: case ClassProjectileImpact: case ClassVehicleCollision:
                    min = 1.4f; max = 8f; return;
                case ClassWeaponFire: min = 4f; max = 70f; return;
                case ClassScriptedEffect: min = 2f; max = 5f; return;
                case ClassProjectileDetonation: min = 8f; max = 120f; return;
                case ClassUnitFootsteps: min = 0.9f; max = 10f; return;
                default: min = 1f; max = 10f; return;   // ours (unlisted classes)
            }
        }

        /// <summary>Classes Halo plays without position (the announcer, music, first person damage, forced unspatialized dialog).</summary>
        public static bool Unspatialized(HaloSound s) =>
            s.Class == ClassGameEvent ? s.TagMinDistance <= 0f && s.TagMaxDistance <= 0f : Unspatialized(s.Class);

        /// <summary>By class alone. "game event" sounds with their own distances (the teleporter hum: 0.5-2.5 units) are placed in the world: use
        /// <see cref="Unspatialized(HaloSound)"/> (the client found the teleporter loop playing full volume everywhere).</summary>
        public static bool Unspatialized(int cls) =>
            cls == ClassGameEvent || cls == ClassMusic || cls == ClassScriptedDialogForceUnspatialized || cls == ClassFirstPersonDamage;

        /// <summary>Total frames of a permutation chain without decoding (ADPCM exact; Ogg from the 16-bit buffer size).</summary>
        public int ChainFrames(int pitchRange, int first)
        {
            int n = 0;
            var perms = PitchRanges[pitchRange].Permutations;
            foreach (int i in Chain(pitchRange, first))
            {
                var p = perms[i];
                if (p.Format == FormatOgg) n += p.BufferSize / (2 * Channels);
                else if (p.Format == FormatPcm16) n += p.Size / (2 * Channels);
                else n += HaloAdpcm.AdpcmFrames(p.Size, Channels);
            }
            return n;
        }

        /// <summary>The volume group of a sound class (ours: weapon / vehicle / music / announcer / ambient / effect).</summary>
        public static string ClassGroup(int cls)
        {
            switch (cls)
            {
                case ClassWeaponFire: case ClassWeaponReady: case ClassWeaponReload: case ClassWeaponEmpty: case ClassWeaponCharge:
                case ClassWeaponOverheat: case ClassWeaponIdle: case ClassProjectileImpact: case ClassProjectileDetonation:
                    return "weapon";
                case ClassVehicleCollision: case ClassVehicleEngine: return "vehicle";
                case ClassMusic: return "music";
                case ClassGameEvent: case ClassScriptedDialogPlayer: case ClassScriptedDialogOther: case ClassScriptedDialogForceUnspatialized:
                case ClassUnitDialog:
                    return "announcer";
                case ClassAmbientNature: case ClassAmbientMachinery: case ClassAmbientComputers: case ClassDeviceDoor: case ClassDeviceForceField:
                case ClassDeviceMachinery: case ClassDeviceNature: case ClassDeviceComputers:
                    return "ambient";
                default: return "effect";
            }
        }

        public override string ToString() => Tag != null ? Tag.Name : "sound";
    }
}

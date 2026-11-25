using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A looping sound tag (lsnd, struct SoundLooping in layouts.txt): tracks (each a start / loop / end snd! with an alternate loop and
    /// end, a gain and fades) played together, plus detail sounds (random one-shots every few seconds around the source). Engines,
    /// projectiles in flight, shield alarms, ambience beds, the title music. Unity-free.
    /// </summary>
    public sealed class HaloLoopingSound
    {
        public const uint FlagNotALoop = 2, FlagStopsMusic = 4;
        public const uint TrackFadeInAtStart = 1, TrackFadeOutAtStop = 2, TrackFadeInAlternate = 4;
        public const uint DetailNotWithAlternate = 1, DetailOnlyWithAlternate = 2;

        public HaloTag Tag;
        public uint Flags;
        /// <summary>Detail sound period multipliers at scale 0 / 1.</summary>
        public float ZeroDetailPeriod, OneDetailPeriod;
        /// <summary>World units; beyond it the loop is not heard (0 = the tracks' own distances).</summary>
        public float MaxDistance;
        public Track[] Tracks;
        public Detail[] Details;

        public sealed class Track
        {
            public uint Flags;
            public float Gain, FadeIn, FadeOut;
            public HaloTag Start, Loop, End, AlternateLoop, AlternateEnd;
        }

        public sealed class Detail
        {
            public HaloTag Sound;
            public float PeriodMin, PeriodMax, Gain;
            public uint Flags;
            public float YawMin, YawMax, PitchMin, PitchMax, DistanceMin, DistanceMax;
        }

        static readonly Dictionary<HaloTag, HaloLoopingSound> Cache = new Dictionary<HaloTag, HaloLoopingSound>();

        public static HaloLoopingSound Get(HaloTag t)
        {
            if (t == null || t.ClassName != "lsnd") return null;
            lock (Cache)
            {
                HaloLoopingSound s;
                if (!Cache.TryGetValue(t, out s)) { s = Read(t); Cache[t] = s; }
                return s;
            }
        }

        public static void Forget(HaloMap map)
        {
            lock (Cache)
            {
                var drop = new List<HaloTag>();
                foreach (var k in Cache.Keys) if (k.Map == map) drop.Add(k);
                foreach (var k in drop) Cache.Remove(k);
            }
        }

        public static HaloLoopingSound Read(HaloTag t)
        {
            var r = t.Root;
            var s = new HaloLoopingSound
            {
                Tag = t, Flags = r.U32(0), ZeroDetailPeriod = r.F32(4), OneDetailPeriod = r.F32(16), MaxDistance = r.F32(32),
            };
            if (s.ZeroDetailPeriod <= 0f) s.ZeroDetailPeriod = 1f;
            if (s.OneDetailPeriod <= 0f) s.OneDetailPeriod = 1f;
            var tracks = new List<Track>();
            foreach (var k in r.Block(60, 160))
                tracks.Add(new Track
                {
                    Flags = k.U32(0), Gain = k.F32(4), FadeIn = k.F32(8), FadeOut = k.F32(12),
                    Start = k.Ref(48), Loop = k.Ref(64), End = k.Ref(80), AlternateLoop = k.Ref(128), AlternateEnd = k.Ref(144),
                });
            s.Tracks = tracks.ToArray();
            var details = new List<Detail>();
            foreach (var k in r.Block(72, 104))
                details.Add(new Detail
                {
                    Sound = k.Ref(0), PeriodMin = k.F32(16), PeriodMax = k.F32(20), Gain = k.F32(24), Flags = k.U32(28),
                    YawMin = k.F32(80), YawMax = k.F32(84), PitchMin = k.F32(88), PitchMax = k.F32(92), DistanceMin = k.F32(96), DistanceMax = k.F32(100),
                });
            s.Details = details.ToArray();
            return s;
        }

        /// <summary>Every snd! this loop can play (tracks and details).</summary>
        public IEnumerable<HaloTag> Sounds()
        {
            foreach (var k in Tracks)
            {
                if (k.Start != null) yield return k.Start;
                if (k.Loop != null) yield return k.Loop;
                if (k.End != null) yield return k.End;
                if (k.AlternateLoop != null) yield return k.AlternateLoop;
                if (k.AlternateEnd != null) yield return k.AlternateEnd;
            }
            foreach (var d in Details) if (d.Sound != null) yield return d.Sound;
        }

        public override string ToString() => Tag != null ? Tag.Name : "lsnd";
    }
}

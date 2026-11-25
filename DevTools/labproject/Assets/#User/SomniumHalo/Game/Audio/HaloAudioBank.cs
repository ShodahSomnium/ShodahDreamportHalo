using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>One decodable piece of sound: a whole permutation chain joined (short sounds) or one permutation of a long chain
    /// (music, long dialog: played piece after piece, decoded just ahead and released after it played).</summary>
    internal sealed class HaloAudioEntry
    {
        public HaloAudioBank Bank;
        public int Pr, Perm;
        /// <summary>True: only permutation <see cref="Perm"/>; false: the whole chain starting there, joined.</summary>
        public bool Piece;
        public AudioClip Clip;
        public bool Requested, Failed, Dead, Background;
        /// <summary>The worker took it (set on the worker; a second job for it is skipped).</summary>
        public volatile bool Decoded;
        /// <summary>Voices that hold or have queued this entry (streamed entries are released at 0).</summary>
        public int Users;
        public bool Streamed;
        public float Seconds, Peak, Rms;
        public float RequestedAt;
        public HaloSound Sound => Bank.Sound;
        public bool Ready => Clip != null;
        public string Name => Bank.Sound.Tag.Name + "__" + Pr + "__" + Perm + (Piece ? "p" : "");
    }

    /// <summary>The clips of one sound tag (per pitch range and permutation) plus the per-range "last permutation" memory Halo uses to
    /// avoid playing the same permutation twice in a row.</summary>
    internal sealed class HaloAudioBank
    {
        /// <summary>A chain longer than this is played piece by piece (streamed) instead of joined into one clip. // ours</summary>
        public const float StreamSeconds = 20f;

        public readonly HaloSound Sound;
        readonly HaloAudioEntry[][] _joined, _pieces;
        readonly bool[][] _long;
        readonly int[] _last;

        public HaloAudioBank(HaloSound s)
        {
            Sound = s;
            int n = s.PitchRanges.Length;
            _joined = new HaloAudioEntry[n][]; _pieces = new HaloAudioEntry[n][]; _long = new bool[n][]; _last = new int[n];
            for (int i = 0; i < n; i++)
            {
                int pc = s.PitchRanges[i].Permutations.Length;
                _joined[i] = new HaloAudioEntry[pc]; _pieces[i] = new HaloAudioEntry[pc]; _long[i] = new bool[pc];
                _last[i] = -1;
                int act = s.PitchRanges[i].ActualPermutationCount;
                for (int p = 0; p < act && p < pc; p++)
                    _long[i][p] = s.ChainFrames(i, p) > StreamSeconds * s.SampleRate;
            }
        }

        public bool IsLong(int pr, int perm) => _long[pr][perm];

        /// <summary>A random start permutation of a pitch range, not the one played last when there is a choice (Halo's rule).</summary>
        public int PickPermutation(int pr, System.Random rng)
        {
            int n = Sound.PitchRanges[pr].ActualPermutationCount;
            if (n <= 1) { _last[pr] = 0; return 0; }
            int p = rng.Next(n);
            if (p == _last[pr]) p = (p + 1 + rng.Next(n - 1)) % n;
            _last[pr] = p;
            return p;
        }

        /// <summary>Appends the entries that play permutation <paramref name="perm"/> (one joined entry, or every piece of a long chain).</summary>
        public void Sequence(int pr, int perm, List<HaloAudioEntry> into)
        {
            if (!_long[pr][perm])
            {
                var e = _joined[pr][perm];
                if (e == null) { e = new HaloAudioEntry { Bank = this, Pr = pr, Perm = perm, Piece = false }; _joined[pr][perm] = e; }
                into.Add(e);
                return;
            }
            foreach (int k in Sound.Chain(pr, perm))
            {
                var e = _pieces[pr][k];
                if (e == null) { e = new HaloAudioEntry { Bank = this, Pr = pr, Perm = k, Piece = true, Streamed = true }; _pieces[pr][k] = e; }
                into.Add(e);
            }
        }

        public IEnumerable<HaloAudioEntry> Entries()
        {
            for (int i = 0; i < _joined.Length; i++)
            {
                foreach (var e in _joined[i]) if (e != null) yield return e;
                foreach (var e in _pieces[i]) if (e != null) yield return e;
            }
        }
    }
}

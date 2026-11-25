using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>Decoded samples of one sound permutation (with its "next permutation" chain joined): interleaved floats -1..1.
    /// Unity-free; the Unity side turns it into an AudioClip on the main thread.</summary>
    public sealed class HaloPcm
    {
        public float[] Samples;
        public int Channels, Rate;
        public string Name;
        public int Frames => Channels > 0 && Samples != null ? Samples.Length / Channels : 0;
        public float Seconds => Rate > 0 ? (float)Frames / Rate : 0f;

        /// <summary>Peak and RMS level (0..1) of the samples (measured, for the harness / lab).</summary>
        public void Levels(out float peak, out float rms)
        {
            peak = 0f; double sum = 0;
            var s = Samples;
            for (int i = 0; i < s.Length; i++) { float v = s[i] < 0 ? -s[i] : s[i]; if (v > peak) peak = v; sum += s[i] * (double)s[i]; }
            rms = s.Length > 0 ? (float)Math.Sqrt(sum / s.Length) : 0f;
        }

        /// <summary>Decodes one piece (a single permutation's samples) - from sounds.map when it is external.</summary>
        public static float[] DecodePiece(HaloSound s, HaloSound.Permutation p, HaloResources sounds, out int channels, out int rate)
        {
            channels = s.Channels; rate = s.SampleRate;
            if (p.Size <= 0) return new float[0];
            byte[] d; int at;
            if (p.External)
            {
                if (sounds == null) throw new InvalidOperationException("sounds.map not open for " + s);
                d = sounds.Read(p.Offset, p.Size); at = 0;
            }
            else { d = s.Tag.Map.Data; at = p.Offset; }
            int fmt = p.Format;
            switch (fmt)
            {
                case HaloSound.FormatXboxAdpcm:
                case HaloSound.FormatImaAdpcm:
                    return HaloAdpcm.DecodeAdpcm(d, at, p.Size, channels);
                case HaloSound.FormatPcm16:
                    return HaloAdpcm.DecodePcm16(d, at, p.Size);
                case HaloSound.FormatOgg:
                    return HaloOgg.Decode(d, at, p.Size, out channels, out rate);
                default:
                    throw new NotSupportedException("sound format " + fmt + " in " + s);
            }
        }

        /// <summary>Decodes permutation <paramref name="perm"/> of pitch range <paramref name="pitchRange"/> and every piece its
        /// "next permutation index" chain points at, joined into one buffer.</summary>
        public static HaloPcm Decode(HaloSound s, int pitchRange, int perm, HaloResources sounds)
        {
            var chain = s.Chain(pitchRange, perm);
            var perms = s.PitchRanges[pitchRange].Permutations;
            var parts = new List<float[]>();
            int ch = s.Channels, rate = s.SampleRate, total = 0;
            for (int i = 0; i < chain.Length; i++)
            {
                int c, r;
                var piece = DecodePiece(s, perms[chain[i]], sounds, out c, out r);
                if (i == 0) { ch = c; rate = r; }
                else if (c != ch) continue;   // never seen; keep the first piece's layout
                parts.Add(piece); total += piece.Length;
            }
            var o = new float[total];
            int at = 0;
            foreach (var p in parts) { Array.Copy(p, 0, o, at, p.Length); at += p.Length; }
            return new HaloPcm { Samples = o, Channels = ch, Rate = rate, Name = s.Tag.Name + "__" + pitchRange + "__" + perm };
        }

        /// <summary>A RIFF WAVE file (16-bit) of the samples - harness checks only.</summary>
        public byte[] Wav()
        {
            int n = Samples.Length;
            var b = new byte[44 + n * 2];
            void W32(int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
            void W16(int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
            b[0] = (byte)'R'; b[1] = (byte)'I'; b[2] = (byte)'F'; b[3] = (byte)'F'; W32(4, 36 + n * 2);
            b[8] = (byte)'W'; b[9] = (byte)'A'; b[10] = (byte)'V'; b[11] = (byte)'E';
            b[12] = (byte)'f'; b[13] = (byte)'m'; b[14] = (byte)'t'; b[15] = (byte)' '; W32(16, 16); W16(20, 1); W16(22, Channels);
            W32(24, Rate); W32(28, Rate * Channels * 2); W16(32, Channels * 2); W16(34, 16);
            b[36] = (byte)'d'; b[37] = (byte)'a'; b[38] = (byte)'t'; b[39] = (byte)'a'; W32(40, n * 2);
            for (int i = 0; i < n; i++)
            {
                float v = Samples[i]; if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                W16(44 + i * 2, (short)(v * 32767f));
            }
            return b;
        }
    }
}

using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Decoders for the sample formats of Halo PC sound permutations (Unity-free, worker-thread safe):
    /// Xbox ADPCM (format 1: 36-byte blocks per channel = 4-byte header (int16 predictor, u8 step index, u8 0) + 32 bytes of 4-bit codes,
    /// 65 samples: the header sample first, then the 64 codes; stereo blocks interleave the two headers, then 4-byte groups L R L R ...
    /// - the layout of MS IMA ADPCM WAV, as adpcm-xq / ffmpeg decode it. Harness: the next block header sits one typical step past the
    /// last decoded sample (mean 373 vs mean step 364 on Blood Gulch), so the header is a sample of its own, not a repeat), IMA ADPCM (format 2, same layout) and 16-bit PCM (format 0, little endian). Output: interleaved floats -1..1.
    /// </summary>
    public static class HaloAdpcm
    {
        static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
            157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411,
            1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442,
            11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
        };
        static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };

        public const int BlockBytes = 36, BlockSamples = 65;

        /// <summary>Frames (samples per channel) an ADPCM buffer of <paramref name="bytes"/> holds.</summary>
        public static int AdpcmFrames(int bytes, int channels) => bytes / (BlockBytes * channels) * BlockSamples;

        /// <summary>Decodes Xbox / IMA ADPCM; a trailing partial block is ignored.</summary>
        public static float[] DecodeAdpcm(byte[] d, int start, int length, int channels)
        {
            if (channels < 1) channels = 1;
            int block = BlockBytes * channels, blocks = length / block;
            var o = new float[blocks * BlockSamples * channels];
            var pred = new int[channels];
            var index = new int[channels];
            const float k = 1f / 32768f;
            for (int b = 0; b < blocks; b++)
            {
                int at = start + b * block;
                for (int c = 0; c < channels; c++)
                {
                    int h = at + c * 4;
                    pred[c] = (short)(d[h] | d[h + 1] << 8);
                    index[c] = d[h + 2];
                    if (index[c] > 88) index[c] = 88;
                }
                int data = at + 4 * channels;
                int frame0 = b * BlockSamples;
                for (int c = 0; c < channels; c++) o[frame0 * channels + c] = pred[c] * k;
                frame0++;
                for (int g = 0; g < 8; g++)            // 8 groups of 8 samples per channel
                {
                    for (int c = 0; c < channels; c++)
                    {
                        int w = data + (g * channels + c) * 4;
                        int p = pred[c], ix = index[c];
                        for (int n = 0; n < 8; n++)
                        {
                            int bt = d[w + (n >> 1)];
                            int code = (n & 1) == 0 ? bt & 0xF : bt >> 4;
                            int step = StepTable[ix];
                            int diff = step >> 3;
                            if ((code & 1) != 0) diff += step >> 2;
                            if ((code & 2) != 0) diff += step >> 1;
                            if ((code & 4) != 0) diff += step;
                            if ((code & 8) != 0) p -= diff; else p += diff;
                            if (p > 32767) p = 32767; else if (p < -32768) p = -32768;
                            ix += IndexTable[code];
                            if (ix < 0) ix = 0; else if (ix > 88) ix = 88;
                            o[(frame0 + g * 8 + n) * channels + c] = p * k;
                        }
                        pred[c] = p; index[c] = ix;
                    }
                }
            }
            return o;
        }

        /// <summary>16-bit little-endian PCM, interleaved.</summary>
        public static float[] DecodePcm16(byte[] d, int start, int length)
        {
            int n = length / 2;
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = (short)(d[start + 2 * i] | d[start + 2 * i + 1] << 8) / 32768f;
            return o;
        }
    }
}

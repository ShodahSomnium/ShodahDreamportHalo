// Vendored from NVorbis 0.10.5 (https://github.com/NVorbis/NVorbis), MIT License, Copyright (c) 2020 Andrew Ward;
// notice in LICENSE.md beside this file. Changed here: the namespace, moved under the world's own; Span<float> read
// buffers replaced by float[]; Enum.HasFlag (it boxes) replaced by a mask test. The Ogg container, VorbisReader and the
// obsolete interfaces are left out - HaloOgg (Data/Audio) feeds the Ogg pages of the sound tags. Wrapped in the nested class NVorbis (one namespace in this world).



namespace SomniumSpace.Worlds.SomniumHalo
{
    static partial class NVorbis
{
    // each channel gets its own pass, with the dimensions interleaved
    internal class Residue1 : Residue0
    {
        protected override bool WriteVectors(ICodebook codebook, IPacket packet, float[][] residue, int channel, int offset, int partitionSize)
        {
            var res = residue[channel];

            for (int i = 0; i < partitionSize;)
            {
                var entry = codebook.DecodeScalar(packet);
                if (entry == -1)
                {
                    return true;
                }
                for (int j = 0; j < codebook.Dimensions; i++, j++)
                {
                    res[offset + i] += codebook[entry, j];
                }
            }

            return false;
        }
    }
}
}

// Vendored from NVorbis 0.10.5 (https://github.com/NVorbis/NVorbis), MIT License, Copyright (c) 2020 Andrew Ward;
// notice in LICENSE.md beside this file. Changed here: the namespace, moved under the world's own; Span<float> read
// buffers replaced by float[]; Enum.HasFlag (it boxes) replaced by a mask test. The Ogg container, VorbisReader and the
// obsolete interfaces are left out - HaloOgg (Data/Audio) feeds the Ogg pages of the sound tags. Wrapped in the nested class NVorbis (one namespace in this world).

namespace SomniumSpace.Worlds.SomniumHalo
{
    static partial class NVorbis
{
    internal interface IMdct
    {
        void Reverse(float[] samples, int sampleCount);
    }
}
}

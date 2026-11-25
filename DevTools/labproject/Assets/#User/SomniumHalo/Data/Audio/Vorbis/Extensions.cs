// Vendored from NVorbis 0.10.5 (https://github.com/NVorbis/NVorbis), MIT License, Copyright (c) 2020 Andrew Ward;
// notice in LICENSE.md beside this file. Changed here: the namespace, moved under the world's own; Span<float> read
// buffers replaced by float[]; Enum.HasFlag (it boxes) replaced by a mask test. The Ogg container, VorbisReader and the
// obsolete interfaces are left out - HaloOgg (Data/Audio) feeds the Ogg pages of the sound tags. Wrapped in the nested class NVorbis (one namespace in this world).


using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Provides extension methods for NVorbis types.
    /// </summary>
    internal static class NVorbisExtensions
    {
        /// <summary>
        /// Reads into the specified buffer.
        /// </summary>
        /// <param name="packet">The packet instance to use.</param>
        /// <param name="buffer">The buffer to read into.</param>
        /// <param name="index">The index into the buffer to use.</param>
        /// <param name="count">The number of bytes to read into the buffer.</param>
        /// <returns>The number of bytes actually read into the buffer.</returns>
        public static int Read(this NVorbis.IPacket packet, byte[] buffer, int index, int count)
        {
            if (index < 0 || index >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (count < 0 || index + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));
            for (int i = 0; i < count; i++)
            {
                var value = (byte)packet.TryPeekBits(8, out var bitsRead);
                if (bitsRead == 0)
                {
                    return i;
                }
                buffer[index++] = value;
                packet.SkipBits(8);
            }
            return count;
        }

        /// <summary>
        /// Reads the specified number of bytes from the packet and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="count">The number of bytes to read.</param>
        /// <returns>A byte array holding the data read.</returns>
        public static byte[] ReadBytes(this NVorbis.IPacket packet, int count)
        {
            var buf = new byte[count];
            var cnt = Read(packet, buf, 0, count);
            if (cnt < count)
            {
                var temp = new byte[cnt];
                Buffer.BlockCopy(buf, 0, temp, 0, cnt);
                return temp;
            }
            return buf;
        }

        /// <summary>
        /// Reads one bit from the packet and advances the read position.
        /// </summary>
        /// <returns><see langword="true"/> if the bit was a one, otehrwise <see langword="false"/>.</returns>
        public static bool ReadBit(this NVorbis.IPacket packet)
        {
            return packet.ReadBits(1) == 1;
        }

        /// <summary>
        /// Reads the next byte from the packet. Does not advance the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The byte read from the packet.</returns>
        public static byte PeekByte(this NVorbis.IPacket packet)
        {
            return (byte)packet.TryPeekBits(8, out _);
        }

        /// <summary>
        /// Reads the next byte from the packet and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The byte read from the packet.</returns>
        public static byte ReadByte(this NVorbis.IPacket packet)
        {
            return (byte)packet.ReadBits(8);
        }

        /// <summary>
        /// Reads the next 16 bits from the packet as a <see cref="short"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 16 bits.</returns>
        public static short ReadInt16(this NVorbis.IPacket packet)
        {
            return (short)packet.ReadBits(16);
        }

        /// <summary>
        /// Reads the next 32 bits from the packet as a <see cref="int"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 32 bits.</returns>
        public static int ReadInt32(this NVorbis.IPacket packet)
        {
            return (int)packet.ReadBits(32);
        }

        /// <summary>
        /// Reads the next 64 bits from the packet as a <see cref="long"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 64 bits.</returns>
        public static long ReadInt64(this NVorbis.IPacket packet)
        {
            return (long)packet.ReadBits(64);
        }

        /// <summary>
        /// Reads the next 16 bits from the packet as a <see cref="ushort"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 16 bits.</returns>
        public static ushort ReadUInt16(this NVorbis.IPacket packet)
        {
            return (ushort)packet.ReadBits(16);
        }

        /// <summary>
        /// Reads the next 32 bits from the packet as a <see cref="uint"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 32 bits.</returns>
        public static uint ReadUInt32(this NVorbis.IPacket packet)
        {
            return (uint)packet.ReadBits(32);
        }

        /// <summary>
        /// Reads the next 64 bits from the packet as a <see cref="ulong"/> and advances the position counter.
        /// </summary>
        /// <param name="packet"></param>
        /// <returns>The value of the next 64 bits.</returns>
        public static ulong ReadUInt64(this NVorbis.IPacket packet)
        {
            return packet.ReadBits(64);
        }

        /// <summary>
        /// Advances the position counter by the specified number of bytes.
        /// </summary>
        /// <param name="packet"></param>
        /// <param name="count">The number of bytes to advance.</param>
        public static void SkipBytes(this NVorbis.IPacket packet, int count)
        {
            packet.SkipBits(count * 8);
        }
    }
}

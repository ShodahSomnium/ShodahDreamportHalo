using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Ogg Vorbis permutations (sound format 3: each permutation's samples are one whole Ogg stream - "OggS" pages with the three
    /// Vorbis headers first). The Ogg container is read here (pages, lacing, packets spanning pages, granule positions) and the
    /// packets go to the vendored NVorbis StreamDecoder (Data/Audio/Vorbis, MIT). Unity-free, worker-thread safe.
    /// </summary>
    public static class HaloOgg
    {
        /// <summary>Decodes a whole Ogg Vorbis stream to interleaved floats; channels / rate come from the stream's id header.</summary>
        public static float[] Decode(byte[] d, int start, int length, out int channels, out int rate)
        {
            var packets = Packets(d, start, length);
            var provider = new Provider(packets);
            using (var dec = new NVorbis.StreamDecoder(provider))
            {
                dec.ClipSamples = true;
                channels = dec.Channels; rate = dec.SampleRate;
                long granule = 0;
                for (int i = packets.Count - 1; i >= 0; i--) if (packets[i].GranulePosition.HasValue) { granule = packets[i].GranulePosition.Value; break; }
                var outList = new List<float[]>();
                int total = 0;
                var buf = new float[4096 * Math.Max(1, channels)];
                while (true)
                {
                    int n = dec.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    var c = new float[n]; Array.Copy(buf, c, n); outList.Add(c); total += n;
                }
                // end trimming: the last page's granule position is the stream's length in frames
                if (granule > 0 && granule * channels < total) total = (int)granule * channels;
                var o = new float[total];
                int at = 0;
                foreach (var c in outList) { int n = Math.Min(c.Length, total - at); if (n <= 0) break; Array.Copy(c, 0, o, at, n); at += n; }
                return o;
            }
        }

        static bool IsOggS(byte[] d, int o) => o + 27 <= d.Length && d[o] == 'O' && d[o + 1] == 'g' && d[o + 2] == 'g' && d[o + 3] == 'S';

        /// <summary>The packets of the first logical stream (one serial number) in a byte range.</summary>
        static List<Packet> Packets(byte[] d, int start, int length)
        {
            var list = new List<Packet>();
            int end = Math.Min(d.Length, start + length), at = start;
            var pending = new List<byte>();
            bool havePending = false;
            int serial = 0; bool haveSerial = false;
            while (at + 27 <= end)
            {
                if (!IsOggS(d, at)) { at++; continue; }   // resync to the next page
                int type = d[at + 5];
                long granule = BitConverter.ToInt64(d, at + 6);
                int ser = BitConverter.ToInt32(d, at + 14);
                int segs = d[at + 26];
                int body = at + 27 + segs;
                if (body > end) break;
                int bodyLen = 0;
                for (int i = 0; i < segs; i++) bodyLen += d[at + 27 + i];
                if (body + bodyLen > end) break;
                if (!haveSerial) { serial = ser; haveSerial = true; }
                if (ser != serial) { at = body + bodyLen; continue; }
                if ((type & 1) == 0 && havePending) { pending.Clear(); havePending = false; }  // not a continuation: drop a dangling piece
                int p = body;
                int lastDone = -1;
                for (int i = 0; i < segs; i++)
                {
                    int lace = d[at + 27 + i];
                    for (int k = 0; k < lace; k++) pending.Add(d[p + k]);
                    havePending = true;
                    p += lace;
                    if (lace < 255)
                    {
                        list.Add(new Packet(pending.ToArray()));
                        pending.Clear(); havePending = false;
                        lastDone = list.Count - 1;
                    }
                }
                if (lastDone >= 0)
                {
                    if (granule >= 0) list[lastDone].GranulePosition = granule;
                    if ((type & 4) != 0) list[lastDone].IsEndOfStream = true;
                }
                at = body + bodyLen;
            }
            if (list.Count > 0) list[list.Count - 1].IsEndOfStream = true;
            return list;
        }

        sealed class Packet : NVorbis.DataPacket
        {
            readonly byte[] _d; int _i;
            public Packet(byte[] d) { _d = d; }
            protected override int TotalBits => _d.Length * 8;
            protected override int ReadNextByte() => _i < _d.Length ? _d[_i++] : -1;
            public override void Reset() { _i = 0; base.Reset(); }
        }

        sealed class Provider : NVorbis.IPacketProvider
        {
            readonly List<Packet> _p; int _i;
            public Provider(List<Packet> p) { _p = p; }
            public bool CanSeek => false;
            public int StreamSerial => 0;
            public long GetGranuleCount()
            {
                for (int i = _p.Count - 1; i >= 0; i--) if (_p[i].GranulePosition.HasValue) return _p[i].GranulePosition.Value;
                return 0;
            }
            public long SeekTo(long granulePos, int preRoll, NVorbis.GetPacketGranuleCount getPacketGranuleCount) => throw new NotSupportedException();
            public NVorbis.IPacket PeekNextPacket() => _i < _p.Count ? _p[_i] : null;
            public NVorbis.IPacket GetNextPacket() => _i < _p.Count ? _p[_i++] : null;
        }
    }
}

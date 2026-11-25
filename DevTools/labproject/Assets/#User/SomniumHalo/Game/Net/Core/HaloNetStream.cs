using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A byte blob streamed through a few scene records ("Halo Net Host 0..3"): the campaign host's HOST STATE (every module's part, for a hand-over).
    /// The writer cuts the blob into chunks of 224 bytes and writes the next chunk into each record on every write (round and round, so a reader that
    /// missed a chunk gets it on the next pass); a reader assembles the chunks of one blob id and keeps the last complete blob (checksum checked).
    /// Record: 0 magic 'HLH1' | 1 seq | 2 blob id | 3 chunk index (0-15) | chunk count (16-31) | 4 blob length in bytes | 5 game map hash |
    /// 6 game number | 7 FNV-1a of the whole blob | 8-63 the chunk's 224 bytes (little-endian ints). Pure C#.
    /// </summary>
    public sealed class HaloNetStream
    {
        public const int Magic = 0x484C4831;      // 'HLH1'
        public const int Header = 8, PayloadInts = HaloNetWire.Ints - Header, ChunkBytes = PayloadInts * 4;   // 224
        public const int MaxBytes = 0xFFFF * ChunkBytes;

        // writer
        private byte[] _blob;
        private int _blobId, _cursor, _sum, _counter;

        /// <summary>The writing client's player id (set by the session every tick).</summary>
        public int Writer;
        private readonly int[][] _out;
        // reader
        private byte[] _assembly;
        private bool[] _got;
        private int _assemblyId = -1, _assemblyLen, _assemblySum, _missing;
        private int _zone, _seq;

        /// <summary>The last complete blob read (or written), its id; null when none.</summary>
        public byte[] Complete { get; private set; }
        public int CompleteId { get; private set; } = -1;
        /// <summary>Chunks of the blob being assembled still missing (0 when nothing is in progress).</summary>
        public int Missing { get { return _missing; } }

        public HaloNetStream(int records)
        {
            _out = new int[records][];
            for (int i = 0; i < records; i++) _out[i] = new int[HaloNetWire.Ints];
        }

        public static int Checksum(byte[] b, int len)
        {
            uint h = 2166136261u;
            for (int i = 0; i < len; i++) { h ^= b[i]; h *= 16777619u; }
            return unchecked((int)h);
        }

        /// <summary>The game the blobs are about (another game: everything read is forgotten).</summary>
        internal void SetGame(int zone, int seq)
        {
            if (zone == _zone && seq == _seq) return;
            _zone = zone; _seq = seq;
            Complete = null; CompleteId = -1; _assemblyId = -1; _missing = 0;
        }

        // ------------------------------------------------------------------ writer

        /// <summary>The host's new blob (it is streamed from now on; a reader gets it within one pass).</summary>
        public void SetBlob(byte[] blob)
        {
            if (blob == null || blob.Length > MaxBytes) return;
            _blob = blob;
            _counter = (_counter + 1) & 0xFFFFF;
            _blobId = (((Writer + 1) & 0x7FF) << 20) | _counter;      // (the writer in the id: a new host's blobs never look like the old host's)
            _cursor = 0;
            _sum = Checksum(blob, blob.Length);
            Complete = blob;
            CompleteId = _blobId;
        }

        public bool HasBlob { get { return _blob != null; } }

        public int ChunkCount { get { return _blob == null ? 0 : Math.Max(1, (_blob.Length + ChunkBytes - 1) / ChunkBytes); } }

        /// <summary>The record for one stream object on this write: the next chunk (null when there is no blob).</summary>
        internal int[] Outgoing(int record)
        {
            if (_blob == null || record < 0 || record >= _out.Length) return null;
            int count = ChunkCount;
            int chunk = _cursor;
            _cursor = (_cursor + 1) % count;
            int[] o = _out[record];
            o[0] = Magic;
            o[1]++;
            o[2] = _blobId;
            o[3] = (chunk & 0xFFFF) | (count << 16);
            o[4] = _blob.Length;
            o[5] = _zone;
            o[6] = _seq & 0xFFFF;
            o[7] = _sum;
            int at = chunk * ChunkBytes;
            for (int i = 0; i < PayloadInts; i++)
            {
                int v = 0;
                for (int b = 0; b < 4; b++)
                {
                    int k = at + i * 4 + b;
                    if (k < _blob.Length) v |= _blob[k] << (b * 8);
                }

                o[Header + i] = v;
            }

            return o;
        }

        // ------------------------------------------------------------------ reader

        internal void Take(int[] rec)
        {
            if (rec[0] != Magic || rec[5] != _zone || (rec[6] & 0xFFFF) != _seq || _zone == 0) return;
            int id = rec[2], chunk = rec[3] & 0xFFFF, count = (rec[3] >> 16) & 0xFFFF, len = rec[4];
            if (id == CompleteId || len < 0 || len > MaxBytes || count <= 0 || chunk >= count) return;
            if (id != _assemblyId)
            {
                _assemblyId = id;
                _assemblyLen = len;
                _assemblySum = rec[7];
                _assembly = new byte[len];
                _got = new bool[count];
                _missing = count;
            }

            if (_got == null || chunk >= _got.Length || _got[chunk]) return;
            _got[chunk] = true;
            _missing--;
            int at = chunk * ChunkBytes;
            for (int i = 0; i < PayloadInts; i++)
            {
                int v = rec[Header + i];
                for (int b = 0; b < 4; b++)
                {
                    int k = at + i * 4 + b;
                    if (k < _assemblyLen) _assembly[k] = (byte)((v >> (b * 8)) & 0xFF);
                }
            }

            if (_missing > 0) return;
            if (Checksum(_assembly, _assemblyLen) == _assemblySum) { Complete = _assembly; CompleteId = id; }
            _assembly = null; _got = null; _missing = 0; _assemblyId = -1;
        }
    }

    public sealed partial class HaloNet
    {
        /// <summary>The campaign host's state stream ("Halo Net Host 0..3").</summary>
        public readonly HaloNetStream HostStream = new HaloNetStream(HaloNetCoop.HostBlocks);

        private double _nextStreamWrite, _nextStreamAsk;

        private void StepHostStream(double now, int localId)
        {
            GameInfo g = _game;
            bool on = g.On && CoopMission;
            HostStream.SetGame(on ? g.ZoneHash : 0, on ? g.Seq : 0);
            HostStream.Writer = localId;
            int first = HaloNetCoop.FirstActor + HaloNetCoop.CoopBlocks;
            int have = Math.Max(0, _coopCount - HaloNetCoop.CoopBlocks);
            bool writer = on && Coop.Writing;
            if (!writer)
            {
                for (int b = 0; b < have; b++) if (_valid[first + b]) HostStream.Take(_slots[first + b]);
                return;
            }

            if (!HostStream.HasBlob || have == 0) return;
            if (now >= _nextStreamAsk)
            {
                _nextStreamAsk = now + 1.0;
                for (int b = 0; b < have; b++) if (_valid[first + b] && !_authority[first + b]) _t.RequestAuthority(first + b);
            }

            if (now < _nextStreamWrite) return;
            _nextStreamWrite = now + 0.1;
            for (int b = 0; b < have; b++)
            {
                if (!_valid[first + b] || !_authority[first + b]) continue;
                int[] rec = HostStream.Outgoing(b);
                if (rec != null) Push(first + b, rec);
            }
        }
    }
}

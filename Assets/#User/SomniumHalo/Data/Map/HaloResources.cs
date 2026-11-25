using System;
using System.Collections.Generic;
using System.IO;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A resource cache file of Halo PC (MAPS\bitmaps.map = type 1, sounds.map = type 2). Retail PC maps keep every bitmap's pixels
    /// and every sound's samples here: the tag's data offset (flag "external") is an absolute offset into this file. The files are
    /// large (300 / 200 MB), so they stay on disk and ranges are read on demand (thread-safe). Unity-free.
    /// Layout: u32 type, u32 paths offset, u32 table offset, u32 count; table entries (u32 path offset, u32 size, u32 data offset).
    /// </summary>
    public sealed class HaloResources : IDisposable
    {
        public readonly string Path;
        public readonly int Type, Count;
        readonly FileStream _file;
        readonly object _lock = new object();
        readonly int[] _sizes, _offsets;
        readonly string[] _names;

        static readonly Dictionary<string, HaloResources> Open = new Dictionary<string, HaloResources>(StringComparer.OrdinalIgnoreCase);

        /// <summary>One shared reader per file for the whole run.</summary>
        public static HaloResources Get(string path)
        {
            lock (Open)
            {
                HaloResources r;
                if (!Open.TryGetValue(path, out r)) { r = new HaloResources(path); Open[path] = r; }
                return r;
            }
        }

        public HaloResources(string path)
        {
            Path = path;
            _file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            var head = Read(0, 16);
            Type = BitConverter.ToInt32(head, 0);
            int paths = BitConverter.ToInt32(head, 4), table = BitConverter.ToInt32(head, 8);
            Count = BitConverter.ToInt32(head, 12);
            var t = Read(table, Count * 12);
            _sizes = new int[Count]; _offsets = new int[Count]; _names = new string[Count];
            int pathEnd = table > paths ? table : (int)_file.Length;
            var p = Read(paths, Math.Max(0, pathEnd - paths));
            for (int i = 0; i < Count; i++)
            {
                int po = BitConverter.ToInt32(t, i * 12);
                _sizes[i] = BitConverter.ToInt32(t, i * 12 + 4);
                _offsets[i] = BitConverter.ToInt32(t, i * 12 + 8);
                int k = po; while (k < p.Length && p[k] != 0) k++;
                _names[i] = po < p.Length ? System.Text.Encoding.ASCII.GetString(p, po, k - po) : "";
            }
        }

        public string Name(int i) => _names[i];
        public int Size(int i) => _sizes[i];
        public int Offset(int i) => _offsets[i];

        /// <summary>Reads <paramref name="count"/> bytes at an absolute file offset.</summary>
        public byte[] Read(long offset, int count)
        {
            var b = new byte[count];
            lock (_lock)
            {
                _file.Position = offset;
                int got = 0;
                while (got < count)
                {
                    int n = _file.Read(b, got, count - got);
                    if (n <= 0) throw new EndOfStreamException(Path + " @" + offset);
                    got += n;
                }
            }
            return b;
        }

        public void Dispose() { lock (_lock) _file.Dispose(); }
    }
}

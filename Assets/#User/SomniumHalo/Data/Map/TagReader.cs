using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>A cursor into tag data: a file offset in a map plus the address base it was reached through
    /// (0 = the map's tag data base; BSP data uses its own).</summary>
    public struct TagReader
    {
        public readonly HaloMap Map;
        public readonly int At;
        /// <summary>Address minus file offset for blocks reached from here (0 = use the map's tag base).</summary>
        public readonly long Magic;

        public TagReader(HaloMap map, int at, long magic) { Map = map; At = at; Magic = magic; }

        public bool Valid => Map != null && At > 0 && At < Map.Data.Length;
        public TagReader Skip(int bytes) => new TagReader(Map, At + bytes, Magic);
        public int Off(uint address) => Magic != 0 ? (int)(address - Magic) : Map.Off(address);

        public byte U8(int o) => Map.U8(At + o);
        public sbyte I8(int o) => Map.I8(At + o);
        public short I16(int o) => Map.I16(At + o);
        public ushort U16(int o) => Map.U16(At + o);
        public int I32(int o) => Map.I32(At + o);
        public uint U32(int o) => Map.U32(At + o);
        public float F32(int o) => Map.F32(At + o);
        public bool Flag(int o, int bit) => (Map.U32(At + o) & (1u << bit)) != 0;
        public bool Flag16(int o, int bit) => (Map.U16(At + o) & (1 << bit)) != 0;
        public string Str32(int o) => Map.Str(At + o, 32);
        public HVec3 Vec3(int o) => new HVec3(F32(o), F32(o + 4), F32(o + 8));

        /// <summary>A block (reflexive): count at +0, address at +4.</summary>
        public int Count(int o) => I32(o);
        public TagReader Element(int o, int index, int stride)
        {
            uint addr = U32(o + 4);
            return new TagReader(Map, Off(addr) + index * stride, Magic);
        }
        public IEnumerable<TagReader> Block(int o, int stride)
        {
            int n = Count(o);
            if (n <= 0 || n > 1000000) yield break;
            int start = Off(U32(o + 4));
            for (int i = 0; i < n; i++) yield return new TagReader(Map, start + i * stride, Magic);
        }

        /// <summary>A tag reference (dependency): class +0, name address +4, tag id +12.</summary>
        public HaloTag Ref(int o) => Map.Tag(U32(o + 12));
        public uint RefClass(int o) => U32(o);

        /// <summary>A data reference: size +0, flags +4 (bit 0 = external / in another file), file offset +8, address +12.</summary>
        public int DataSize(int o) => I32(o);
        public bool DataExternal(int o) => (I32(o + 4) & 1) != 0;
        public int DataFileOffset(int o) => I32(o + 8);
        public int DataOffset(int o)
        {
            uint addr = U32(o + 12);
            return addr != 0 ? Off(addr) : I32(o + 8);
        }
        public byte[] DataBytes(int o)
        {
            int n = DataSize(o); if (n <= 0) return new byte[0];
            int at = DataOffset(o);
            var b = new byte[n]; Buffer.BlockCopy(Map.Data, at, b, 0, n); return b;
        }
    }

    /// <summary>Unity-free vector (Data/ is shared with the dotnet harness). Halo units: 1 world unit = 10 feet = 3.048 m; Z up.</summary>
    public struct HVec3
    {
        public float X, Y, Z;
        public HVec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public override string ToString() => $"({X:0.###},{Y:0.###},{Z:0.###})";
    }
}

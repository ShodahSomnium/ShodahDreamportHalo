using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>A Halo PC cache file (.map, version 7) read from the player's own install. Tag data addresses are memory
    /// addresses (base 0x40440000); BSP tags have their own base, given by the scenario's structure bsp entries.</summary>
    public sealed class HaloMap
    {
        public const uint TagBase = 0x40440000;
        public readonly string Path;
        public readonly byte[] Data;
        public readonly int Version, TagDataOffset, TagDataSize, MapType;
        public readonly string Name, Build;
        public readonly uint ScenarioTagId;
        public readonly int ModelDataOffset, ModelIndexOffset, ModelDataSize;
        public readonly HaloTag[] Tags;
        readonly Dictionary<uint, HaloTag> _byId = new Dictionary<uint, HaloTag>();
        readonly long _magic;

        public HaloMap(string path) : this(path, File.ReadAllBytes(path)) { }

        public HaloMap(string path, byte[] data)
        {
            Path = path; Data = data;
            if (U32(0) != 0x68656164) throw new InvalidDataException("not a Halo map (head): " + path);
            Version = I32(4);
            TagDataOffset = I32(0x10);
            TagDataSize = I32(0x14);
            Name = Str(0x20, 32);
            Build = Str(0x40, 32);
            MapType = U16(0x60);
            _magic = (long)TagBase - TagDataOffset;
            int h = TagDataOffset;
            uint tagArray = U32(h);
            ScenarioTagId = U32(h + 4);
            int count = I32(h + 0xC);
            ModelDataOffset = I32(h + 0x14);
            ModelIndexOffset = I32(h + 0x1C);
            ModelDataSize = I32(h + 0x20);
            Tags = new HaloTag[count];
            int at = Off(tagArray);
            for (int i = 0; i < count; i++, at += 32)
            {
                var t = new HaloTag
                {
                    Map = this, Index = i,
                    Class = U32(at), Parent = U32(at + 4), Grandparent = U32(at + 8),
                    Id = U32(at + 12), PathAddress = U32(at + 16), Address = U32(at + 20), Indexed = U32(at + 24) != 0,
                };
                t.Name = CStr(Off(t.PathAddress));
                Tags[i] = t; _byId[t.Id] = t;
            }
        }

        /// <summary>File offset of a tag-data address.</summary>
        public int Off(uint address) => (int)(address - _magic);

        public HaloTag Tag(uint id) => id != 0xFFFFFFFF && _byId.TryGetValue(id, out var t) ? t : null;
        public HaloTag Scenario => Tag(ScenarioTagId);

        public HaloTag Find(string cls, string name)
        {
            uint c = FourCC(cls);
            foreach (var t in Tags) if (t.Class == c && string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }

        public IEnumerable<HaloTag> OfClass(string cls)
        {
            uint c = FourCC(cls);
            foreach (var t in Tags) if (t.Class == c) yield return t;
        }

        public static uint FourCC(string s)
        {
            s = (s + "    ").Substring(0, 4);
            return (uint)(s[0] << 24 | s[1] << 16 | s[2] << 8 | s[3]);
        }

        public static string FourCC(uint v)
        {
            if (v == 0xFFFFFFFF) return "none";
            var c = new char[4];
            for (int i = 0; i < 4; i++) { int b = (int)(v >> (24 - 8 * i)) & 0xFF; c[i] = b >= 32 && b < 127 ? (char)b : '?'; }
            return new string(c);
        }

        // --- primitives (little endian) ---
        public byte U8(int o) => Data[o];
        public sbyte I8(int o) => (sbyte)Data[o];
        public ushort U16(int o) => (ushort)(Data[o] | Data[o + 1] << 8);
        public short I16(int o) => (short)(Data[o] | Data[o + 1] << 8);
        public int I32(int o) => Data[o] | Data[o + 1] << 8 | Data[o + 2] << 16 | Data[o + 3] << 24;
        public uint U32(int o) => (uint)I32(o);
        public float F32(int o) => BitConverter.ToSingle(Data, o);
        public string Str(int o, int max)
        {
            int n = 0; while (n < max && Data[o + n] != 0) n++;
            return Encoding.ASCII.GetString(Data, o, n);
        }
        public string CStr(int o)
        {
            if (o < 0 || o >= Data.Length) return "";
            int n = 0; while (o + n < Data.Length && Data[o + n] != 0) n++;
            return Encoding.ASCII.GetString(Data, o, n);
        }
    }

    public sealed class HaloTag
    {
        public HaloMap Map;
        public int Index;
        public uint Class, Parent, Grandparent, Id, PathAddress, Address;
        public bool Indexed;
        public string Name;
        public string ClassName => HaloMap.FourCC(Class);
        public bool Is(string cls) { uint c = HaloMap.FourCC(cls); return Class == c || Parent == c || Grandparent == c; }
        /// <summary>A reader at the start of this tag's data.</summary>
        public TagReader Root => new TagReader(Map, Map.Off(Address), 0);
        public override string ToString() => Name + "." + ClassName;
    }
}

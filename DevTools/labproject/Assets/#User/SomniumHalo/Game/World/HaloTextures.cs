using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Unity textures from bitmap tags, cached per (map file, tag, image index). DXT1 / DXT5 upload as they are (Unity's BC1 / BC3
    /// match), every other format is decoded to RGBA32 on the CPU (DXT3 has no Unity format). Rows: Halo stores the top row first and its
    /// texture coordinates are Direct3D's (v = 0 at the top); Unity's raw upload puts the first stored row at v = 0 too, so mesh uvs are
    /// used AS THEY ARE (no flip). Only flat UI pictures (RawImage, screen v = 0 at the bottom) show them upside down: use
    /// uvRect (0, 1, 1, -1) there. Main thread only (Texture2D); decode work can be done ahead on a worker with <see cref="Prepare"/>.
    /// </summary>
    public static class HaloTextures
    {
        sealed class Key : IEquatable<Key>
        {
            public string Map; public uint Tag; public int Index; public bool Linear;
            public bool Equals(Key o) => o != null && o.Tag == Tag && o.Index == Index && o.Linear == Linear && o.Map == Map;
            public override bool Equals(object o) => Equals(o as Key);
            public override int GetHashCode() => (Map.GetHashCode() * 31 + (int)Tag) * 31 + Index * 2 + (Linear ? 1 : 0);
        }

        /// <summary>Pixel data made ready on a worker (decode only; no Unity API).</summary>
        public sealed class Prepared
        {
            public int Width, Height, Levels; public TextureFormat Format; public byte[] Data; public bool Cube; public string Name;
        }

        static readonly Dictionary<Key, Texture> Cache = new Dictionary<Key, Texture>();
        static Texture2D _white, _black, _flatNormal;

        public static HaloResources BitmapsFor(HaloMap map)
        {
            try
            {
                string p = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(map.Path), "bitmaps.map");
                return System.IO.File.Exists(p) ? HaloResources.Get(p) : null;
            }
            catch (Exception) { return null; }
        }

        public static Texture2D White { get { if (_white == null) _white = Solid(Color.white, "white"); return _white; } }
        public static Texture2D Black { get { if (_black == null) _black = Solid(Color.black, "black"); return _black; } }
        public static Texture2D FlatNormal { get { if (_flatNormal == null) _flatNormal = Solid(new Color(0.5f, 0.5f, 1f, 1f), "flat normal"); return _flatNormal; } }

        static Texture2D Solid(Color c, string name)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = name };
            t.SetPixel(0, 0, c); t.Apply(false, true);
            return t;
        }

        /// <summary>Worker-safe: reads and decodes one image of a bitmap tag. Cube maps return six faces in Data (Unity face order).</summary>
        public static Prepared Prepare(HaloTag bitmapTag, int index, HaloResources bitmaps)
        {
            var bm = new HaloBitmap(bitmapTag);
            if (index < 0 || index >= bm.Images.Count) return null;
            var img = bm.Images[index];
            byte[] raw = HaloBitmap.Raw(bitmapTag.Map, img, bitmaps);
            var p = new Prepared { Width = img.Width, Height = img.Height, Name = bitmapTag.Name + "#" + index };
            if (img.Type == 2)
            {
                // cube: stored LEVEL-major (level 0 of the six faces, then level 1 of the six, ...; verified on bloodgulch "cubemap beach"),
                // faces in Direct3D order +x -x +y -y +z -z of HALO axes (the game looks them up with Halo-space vectors). Level 0, RGBA32.
                p.Cube = true; p.Format = TextureFormat.RGBA32; p.Levels = 1;
                int n = img.Width * img.Height * 4;
                int ls = HaloBitmap.LevelSize(img.Format, img.Width, img.Height);
                p.Data = new byte[n * 6];
                var one = new HaloBitmap.Image { Width = img.Width, Height = img.Height, Format = img.Format, Type = 0, Mips = 0 };
                var face = new byte[ls];
                for (int f = 0; f < 6; f++)
                {
                    if ((f + 1) * ls > raw.Length) break;
                    Buffer.BlockCopy(raw, f * ls, face, 0, ls);
                    Buffer.BlockCopy(HaloBitmap.DecodeRgba(one, face, 0, 0), 0, p.Data, f * n, n);
                }
                return p;
            }
            if (img.Type == 1)
            {
                // 3d texture: first slice only (Halo uses them for few effects)
                p.Format = TextureFormat.RGBA32; p.Levels = 1; p.Data = HaloBitmap.DecodeRgba(img, raw, 0, 0); return p;
            }
            p.Levels = img.Levels;
            if (img.Format == HaloBitmap.Format.DXT1 || img.Format == HaloBitmap.Format.DXT5)
            {
                // Unity needs block-aligned levels down to the smallest it allocates; Halo pads every level to one block, which matches.
                p.Format = img.Format == HaloBitmap.Format.DXT1 ? TextureFormat.DXT1 : TextureFormat.DXT5;
                if (img.Width % 4 != 0 || img.Height % 4 != 0) { p.Format = TextureFormat.RGBA32; p.Levels = 1; p.Data = HaloBitmap.DecodeRgba(img, raw); return p; }
                int total = 0;
                for (int l = 0; l < p.Levels; l++) total += HaloBitmap.LevelSize(img.Format, Math.Max(1, img.Width >> l), Math.Max(1, img.Height >> l));
                if (total > raw.Length) { p.Levels = 1; total = HaloBitmap.LevelSize(img.Format, img.Width, img.Height); }
                if (total == raw.Length) { p.Data = raw; return p; }   // no second copy (less garbage while a map loads)
                p.Data = new byte[total];
                Buffer.BlockCopy(raw, 0, p.Data, 0, total);
                return p;
            }
            // everything else: RGBA32 with its mip chain decoded level by level
            p.Format = TextureFormat.RGBA32;
            int size = 0;
            for (int l = 0; l < p.Levels; l++) size += Math.Max(1, img.Width >> l) * Math.Max(1, img.Height >> l) * 4;
            p.Data = new byte[size];
            int at = 0;
            for (int l = 0; l < p.Levels; l++)
            {
                byte[] lv;
                try { lv = HaloBitmap.DecodeRgba(img, raw, 0, l); }
                catch (Exception) { p.Levels = l; break; }
                Buffer.BlockCopy(lv, 0, p.Data, at, lv.Length); at += lv.Length;
            }
            if (p.Levels < 1) { p.Levels = 1; p.Data = HaloBitmap.DecodeRgba(img, raw); }
            return p;
        }

        /// <summary>Main thread: a Unity texture from prepared data.</summary>
        public static Texture Upload(Prepared p, bool linear)
        {
            if (p == null) return null;
            if (p.Cube)
            {
                // raw rows as stored (Direct3D face layout = Unity's); faces in Direct3D order = CubemapFace order
                var c = new Cubemap(p.Width, linear ? UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm : UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB,
                    UnityEngine.Experimental.Rendering.TextureCreationFlags.None) { name = p.Name, filterMode = FilterMode.Bilinear };
                int n = p.Width * p.Height * 4;
                for (int f = 0; f < 6; f++) c.SetPixelData(p.Data, 0, (CubemapFace)f, f * n);
                c.Apply(false, true);
                return c;
            }
            var t = new Texture2D(p.Width, p.Height, p.Format, p.Levels, linear) { name = p.Name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            try
            {
                t.LoadRawTextureData(p.Data);
                t.Apply(false, true);
            }
            catch (Exception e)
            {
                HaloLog.Warn("Textures: " + p.Name + " (" + p.Format + " " + p.Width + "x" + p.Height + " x" + p.Levels + ") did not upload: " + e.Message);
                UnityEngine.Object.Destroy(t);
                return White;
            }
            return t;
        }

        /// <summary>Main thread: the cached texture of image <paramref name="index"/> of a bitmap tag (null tag = null).</summary>
        public static Texture Get(HaloTag bitmapTag, int index = 0, bool linear = false)
        {
            if (bitmapTag == null) return null;
            var k = new Key { Map = bitmapTag.Map.Path, Tag = bitmapTag.Id, Index = index, Linear = linear };
            Texture t;
            if (Cache.TryGetValue(k, out t) && t != null) return t;
            Prepared ready = null;
            lock (Ahead) { if (Ahead.TryGetValue(k, out ready)) Ahead.Remove(k); }
            try { t = Upload(ready ?? Prepare(bitmapTag, index, BitmapsFor(bitmapTag.Map)), linear); }
            catch (Exception e) { HaloLog.Warn("Textures: " + bitmapTag.Name + " failed: " + e.Message); t = null; }
            Cache[k] = t;
            return t;
        }

        static readonly Dictionary<Key, Prepared> Ahead = new Dictionary<Key, Prepared>();

        /// <summary>Worker thread: decodes an image ahead so a later <see cref="Get"/> (main thread) only uploads it. Failures are left for Get to report.</summary>
        public static void PrepareAhead(HaloTag bitmapTag, int index, bool linear, HaloResources bitmaps)
        {
            if (bitmapTag == null) return;
            var k = new Key { Map = bitmapTag.Map.Path, Tag = bitmapTag.Id, Index = index, Linear = linear };
            lock (Ahead) { if (Ahead.ContainsKey(k)) return; }
            Prepared p;
            try { p = Prepare(bitmapTag, index, bitmaps); }
            catch (Exception) { return; }
            if (p == null) return;
            lock (Ahead) Ahead[k] = p;
        }

        /// <summary>Main thread: true when the texture is already uploaded (cheap to Get).</summary>
        public static bool Cached(HaloTag bitmapTag, int index, bool linear)
        {
            if (bitmapTag == null) return true;
            var k = new Key { Map = bitmapTag.Map.Path, Tag = bitmapTag.Id, Index = index, Linear = linear };
            Texture t;
            return Cache.TryGetValue(k, out t);
        }

        public static Texture2D Get2D(HaloTag bitmapTag, int index = 0, bool linear = false) { return Get(bitmapTag, index, linear) as Texture2D; }

        /// <summary>Destroys every cached texture of one map file (when a match's map is unloaded).</summary>
        public static void Release(string mapPath)
        {
            lock (Ahead)
            {
                var stale = new List<Key>();
                foreach (var kv in Ahead) if (kv.Key.Map == mapPath) stale.Add(kv.Key);
                foreach (var k in stale) Ahead.Remove(k);
            }
            var dead = new List<Key>();
            foreach (var kv in Cache) if (kv.Key.Map == mapPath) dead.Add(kv.Key);
            foreach (var k in dead) { var t = Cache[k]; if (t != null && t != _white && t != _black && t != _flatNormal) UnityEngine.Object.Destroy(t); Cache.Remove(k); }
        }
    }
}

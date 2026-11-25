using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// One animation of a model_animations tag. Frame data as cache files keep it (checked on Blood Gulch: every rotation decodes to a unit quaternion):
    /// little-endian; for each frame, for each node in order: rotation (int16 i, j, k, w / 32767) if the node's bit is set in the rotation flags,
    /// translation (3 floats, Halo units, in the parent's frame) if set in the translation flags, scale (float) if set in the scale flags (bits above
    /// the node count are ignored: the tool leaves garbage there). The node values that are not animated come once from the default data, in the same
    /// interleaved order. Halo's stored quaternions are the inverse of the node's rotation (the pelvis' (0.5, 0.5, 0.5, 0.5) turns its X - the spine -
    /// up only when conjugated), so the decoded rotations here are conjugated: a standard rotation taking the node's frame into its parent's.
    /// Types: base (the whole body), overlay (deltas on top: aim-still / aim-move are a yaw x pitch grid, fire-1 a recoil), replacement (the nodes it
    /// animates replace the base's: reload, melee). Compressed animations are not decoded (none in the multiplayer maps checked). Unity-free.
    /// </summary>
    public sealed class HaloAnimation
    {
        public enum Kind { Base = 0, Overlay = 1, Replacement = 2 }

        public readonly int Index;
        public readonly string Name;
        public readonly Kind Type;
        public readonly int Frames, FrameSize, NodeCount, FrameInfoType, LoopFrame, Next, KeyFrame, SecondKeyFrame;
        public readonly bool Compressed, WorldRelative, Pal25;
        public readonly float Weight;
        public readonly ulong RotMask, TransMask, ScaleMask;
        private readonly HaloMap _map;
        private readonly int _defaultAt, _defaultSize, _frameAt, _frameSize, _infoAt, _infoSize;
        private readonly uint _compOff;
        private bool _decoded;

        /// <summary>Decoded (after <see cref="Decode"/>): per frame and node the rotation (x, y, z, w; standard, Halo axes), translation (Halo units), scale.</summary>
        public float[] Rot, Trans, Scale;
        /// <summary>Root motion per frame (dx, dy, dz, dyaw; Halo units / radians in the unit's frame), zero when the animation has none.</summary>
        public float[] Info;
        public string Error;

        /// <summary>Seconds per frame: Halo animates at 30 frames a second.</summary>
        public const float FrameTime = 1f / 30f;

        public float Duration { get { return Frames * FrameTime; } }

        public HaloAnimation(TagReader a, int index)
        {
            Index = index;
            _map = a.Map;
            Name = a.Str32(0);
            Type = (Kind)a.U16(32);
            Frames = a.U16(34);
            FrameSize = a.U16(36);
            FrameInfoType = a.U16(38);
            NodeCount = a.U16(44);
            LoopFrame = a.I16(46);
            Weight = a.F32(48);
            KeyFrame = a.I16(52);
            SecondKeyFrame = a.I16(54);
            Next = a.I16(56);
            ushort flags = a.U16(58);
            Compressed = (flags & 1) != 0;
            WorldRelative = (flags & 2) != 0;
            Pal25 = (flags & 4) != 0;
            ulong all = NodeCount >= 64 ? ulong.MaxValue : (1UL << NodeCount) - 1UL;
            TransMask = (a.U32(92) | (ulong)a.U32(96) << 32) & all;
            RotMask = (a.U32(108) | (ulong)a.U32(112) << 32) & all;
            ScaleMask = (a.U32(124) | (ulong)a.U32(128) << 32) & all;
            _infoSize = a.DataSize(72); _infoAt = _infoSize > 0 ? a.DataOffset(72) : 0;
            _defaultSize = a.DataSize(140); _defaultAt = _defaultSize > 0 ? a.DataOffset(140) : 0;
            _frameSize = a.DataSize(160); _frameAt = _frameSize > 0 ? a.DataOffset(160) : 0;
            _compOff = a.U32(136);
        }

        public bool Rotates(int node) { return node >= 0 && node < 64 && (RotMask >> node & 1UL) != 0; }
        public bool Translates(int node) { return node >= 0 && node < 64 && (TransMask >> node & 1UL) != 0; }
        public bool Scales(int node) { return node >= 0 && node < 64 && (ScaleMask >> node & 1UL) != 0; }

        /// <summary>Decodes the frames once (thread-safe enough for one reader: called on the main thread or a loader). False + Error when it cannot.</summary>
        public bool Decode()
        {
            if (_decoded) return Error == null;
            _decoded = true;
            try
            {
                int n = NodeCount, f = Math.Max(1, Frames);
                if (n <= 0 || n > 64) { Error = "node count " + n; return false; }
                var rot = new float[f * n * 4];
                var tr = new float[f * n * 3];
                var sc = new float[f * n];
                byte[] d = _map.Data;
                if (Compressed) { if (!DecodeCompressed(d, n, f, rot, tr, sc)) return false; goto info; }
                int o = _defaultAt;
                int end = _defaultAt + _defaultSize;
                for (int k = 0; k < n; k++)
                {
                    float qx = 0f, qy = 0f, qz = 0f, qw = 1f, tx = 0f, ty = 0f, tz = 0f, s = 1f;
                    if (!Rotates(k)) { if (o + 8 > end) { Error = "default data short"; return false; } ReadQ(d, o, out qx, out qy, out qz, out qw); o += 8; }
                    if (!Translates(k)) { if (o + 12 > end) { Error = "default data short"; return false; } tx = F(d, o); ty = F(d, o + 4); tz = F(d, o + 8); o += 12; }
                    if (!Scales(k)) { if (o + 4 > end) { Error = "default data short"; return false; } s = F(d, o); o += 4; }
                    for (int fr = 0; fr < f; fr++)
                    {
                        int i = fr * n + k;
                        if (!Rotates(k)) { rot[i * 4] = qx; rot[i * 4 + 1] = qy; rot[i * 4 + 2] = qz; rot[i * 4 + 3] = qw; }
                        if (!Translates(k)) { tr[i * 3] = tx; tr[i * 3 + 1] = ty; tr[i * 3 + 2] = tz; }
                        if (!Scales(k)) sc[i] = s;
                    }
                }

                if (FrameSize > 0)
                {
                    if (_frameSize < FrameSize * Frames) { Error = "frame data " + _frameSize + " < " + FrameSize + " x " + Frames; return false; }
                    for (int fr = 0; fr < Frames; fr++)
                    {
                        o = _frameAt + fr * FrameSize;
                        for (int k = 0; k < n; k++)
                        {
                            int i = fr * n + k;
                            if (Rotates(k)) { ReadQ(d, o, out rot[i * 4], out rot[i * 4 + 1], out rot[i * 4 + 2], out rot[i * 4 + 3]); o += 8; }
                            if (Translates(k)) { tr[i * 3] = F(d, o); tr[i * 3 + 1] = F(d, o + 4); tr[i * 3 + 2] = F(d, o + 8); o += 12; }
                            if (Scales(k)) { sc[i] = F(d, o); o += 4; }
                        }
                    }
                }

            info:
                var info = new float[f * 4];
                int stride = FrameInfoType == 1 ? 8 : FrameInfoType == 2 ? 12 : FrameInfoType == 3 ? 16 : 0;
                if (stride > 0 && _infoSize >= stride * Frames)
                    for (int fr = 0; fr < Frames; fr++)
                    {
                        int at = _infoAt + fr * stride;
                        info[fr * 4] = F(d, at);
                        info[fr * 4 + 1] = F(d, at + 4);
                        if (FrameInfoType == 2) info[fr * 4 + 3] = F(d, at + 8);
                        if (FrameInfoType == 3) { info[fr * 4 + 2] = F(d, at + 8); info[fr * 4 + 3] = F(d, at + 12); }
                    }

                Rot = rot; Trans = tr; Scale = sc; Info = info;
                return true;
            }
            catch (Exception e)
            {
                Error = e.GetType().Name + ": " + e.Message;
                Rot = null; Trans = null; Scale = null;
                return false;
            }
        }

        /// <summary>
        /// Halo's compressed animations (the "compressed data" flag; the campaign's cinematic graphs), worked out from the campaign maps and checked
        /// against uncompressed twins (harness `cmp` / `twin`). The frame data, from "offset to compressed data", holds a 44-byte header of 11 offsets
        /// (relative to that start) followed by the sections they bound:
        ///   +44 rotation keyframe headers (one uint32 per ROTATED node, in node order: low 12 bits = key count, high 20 = first key index),
        ///   [0] rotation key frames (uint16 frame numbers, ascending, frame 0 never listed), [1] rotation defaults (EVERY node, 6 bytes = the value at
        ///   frame 0 or the constant one), [2] rotation key values (6 bytes each, key order); [3] translation headers, [4] keys, [5] defaults (every node,
        ///   3 floats), [6] values (3 floats); [7] scale headers, [8] keys, [9] defaults (every node, float), [10] values (float); end = the data size.
        /// A 48-bit quaternion is three little-endian uint16 words taken high word first (v = w0 &lt;&lt; 32 | w1 &lt;&lt; 16 | w2): i = bits 36-47, j = 24-35,
        /// k = 12-23, w = 0-11, each a signed 12-bit value / 2047, in the same inverted convention as the uncompressed ones. Between keys the value is
        /// linear (quaternions nlerped); after the last key it holds. Expanded here into the same per-frame arrays as uncompressed data.
        /// </summary>
        private bool DecodeCompressed(byte[] d, int n, int f, float[] rot, float[] tr, float[] sc)
        {
            int b = _frameAt + (int)_compOff;
            int size = _frameSize - (int)_compOff;
            if (_frameSize <= 0 || size < 44) { Error = "compressed: no data"; return false; }
            var h = new int[12];
            for (int i = 0; i < 11; i++) { h[i] = BitConverter.ToInt32(d, b + i * 4); if (h[i] < 44 || h[i] > size) { Error = "compressed: header offset " + i + " = " + h[i]; return false; } }
            h[11] = size;
            var q = new float[8];
            // rotations
            int rh = 44, rk = h[0], rd = h[1], rv = h[2];
            int slot = 0;
            for (int k = 0; k < n; k++)
            {
                Q48(d, b + rd + k * 6, q, 0);
                for (int fr = 0; fr < f; fr++) { int i = (fr * n + k) * 4; rot[i] = q[0]; rot[i + 1] = q[1]; rot[i + 2] = q[2]; rot[i + 3] = q[3]; }
                if (!Rotates(k)) continue;
                if (rh + slot * 4 + 4 > h[0]) { Error = "compressed: rotation headers short"; return false; }
                uint hd = BitConverter.ToUInt32(d, b + rh + slot * 4);
                slot++;
                int count = (int)(hd & 0xFFF), first = (int)(hd >> 12);
                int prevF = 0;
                q[4] = q[0]; q[5] = q[1]; q[6] = q[2]; q[7] = q[3];
                for (int key = 0; key < count; key++)
                {
                    int at = rk + (first + key) * 2, va = rv + (first + key) * 6;
                    if (at + 2 > h[1] || va + 6 > h[3]) { Error = "compressed: rotation keys short"; return false; }
                    int kf = BitConverter.ToUInt16(d, b + at);
                    Q48(d, b + va, q, 0);
                    for (int fr = prevF + 1; fr <= kf && fr < f; fr++)
                    {
                        float u = kf > prevF ? (fr - prevF) / (float)(kf - prevF) : 1f;
                        HQ.Nlerp(q, 4, q, 0, u, rot, (fr * n + k) * 4);
                    }

                    prevF = kf;
                    q[4] = q[0]; q[5] = q[1]; q[6] = q[2]; q[7] = q[3];
                }

                for (int fr = prevF + 1; fr < f; fr++) { int i = (fr * n + k) * 4; rot[i] = q[4]; rot[i + 1] = q[5]; rot[i + 2] = q[6]; rot[i + 3] = q[7]; }
            }

            // translations (3 floats), scales (1 float)
            if (!Tracks(d, b, n, f, 3, h[3], h[4], h[5], h[6], h[7], tr, TransMask, "translation")) return false;
            if (!Tracks(d, b, n, f, 1, h[7], h[8], h[9], h[10], h[11], sc, ScaleMask, "scale")) return false;
            return true;
        }

        private bool Tracks(byte[] d, int b, int n, int f, int dim, int heads, int keys, int defs, int vals, int end, float[] o, ulong mask, string what)
        {
            int slot = 0;
            var prev = new float[dim];
            for (int k = 0; k < n; k++)
            {
                for (int c = 0; c < dim; c++)
                {
                    float v = defs + (k * dim + c) * 4 + 4 <= vals ? F(d, b + defs + (k * dim + c) * 4) : (dim == 1 ? 1f : 0f);
                    for (int fr = 0; fr < f; fr++) o[(fr * n + k) * dim + c] = v;
                    prev[c] = v;
                }

                if (k >= 64 || (mask >> k & 1UL) == 0) continue;
                if (heads + slot * 4 + 4 > keys) { Error = "compressed: " + what + " headers short"; return false; }
                uint hd = BitConverter.ToUInt32(d, b + heads + slot * 4);
                slot++;
                int count = (int)(hd & 0xFFF), first = (int)(hd >> 12);
                int prevF = 0;
                for (int key = 0; key < count; key++)
                {
                    int at = keys + (first + key) * 2, va = vals + (first + key) * dim * 4;
                    if (at + 2 > defs || va + dim * 4 > end) { Error = "compressed: " + what + " keys short"; return false; }
                    int kf = BitConverter.ToUInt16(d, b + at);
                    for (int fr = prevF + 1; fr <= kf && fr < f; fr++)
                    {
                        float u = kf > prevF ? (fr - prevF) / (float)(kf - prevF) : 1f;
                        for (int c = 0; c < dim; c++) o[(fr * n + k) * dim + c] = prev[c] + (F(d, b + va + c * 4) - prev[c]) * u;
                    }

                    prevF = kf;
                    for (int c = 0; c < dim; c++) prev[c] = F(d, b + va + c * 4);
                }

                for (int fr = prevF + 1; fr < f; fr++) for (int c = 0; c < dim; c++) o[(fr * n + k) * dim + c] = prev[c];
            }

            return true;
        }

        /// <summary>A 48-bit compressed quaternion (see DecodeCompressed), normalised and conjugated into a standard rotation at o[at..at+3].</summary>
        private static void Q48(byte[] d, int o, float[] q, int at)
        {
            ulong v = (ulong)(uint)(d[o] | d[o + 1] << 8) << 32 | (ulong)(uint)(d[o + 2] | d[o + 3] << 8) << 16 | (uint)(d[o + 4] | d[o + 5] << 8);
            float i = S12(v >> 36), j = S12(v >> 24), k = S12(v >> 12), w = S12(v);
            float m = (float)Math.Sqrt(i * i + j * j + k * k + w * w);
            if (m < 1e-6f) { q[at] = 0f; q[at + 1] = 0f; q[at + 2] = 0f; q[at + 3] = 1f; return; }
            q[at] = -i / m; q[at + 1] = -j / m; q[at + 2] = -k / m; q[at + 3] = w / m;
        }

        private static float S12(ulong v) { int x = (int)(v & 0xFFF); if (x >= 2048) x -= 4096; return x / 2047f; }

        private static float F(byte[] d, int o) { return BitConverter.ToSingle(d, o); }

        /// <summary>A stored quaternion (int16 i, j, k, w), normalised and conjugated into a standard rotation.</summary>
        private static void ReadQ(byte[] d, int o, out float x, out float y, out float z, out float w)
        {
            float i = (short)(d[o] | d[o + 1] << 8), j = (short)(d[o + 2] | d[o + 3] << 8), k = (short)(d[o + 4] | d[o + 5] << 8), r = (short)(d[o + 6] | d[o + 7] << 8);
            float m = (float)Math.Sqrt(i * i + j * j + k * k + r * r);
            if (m < 1e-6f) { x = y = z = 0f; w = 1f; return; }
            x = -i / m; y = -j / m; z = -k / m; w = r / m;
        }

        public override string ToString() { return Name + " (" + Type + ", " + Frames + " frames)"; }
    }
}

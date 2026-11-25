using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A character's skeleton from its gbxmodel (nodes: names, parents, rest rotation / translation) and its markers (the "right hand" the weapon sits
    /// on, the "left hand" that is IK'd onto the weapon's "left hand cyborg"). Stored node and marker quaternions are inverted (see HaloAnimation): kept
    /// here as standard rotations (x, y, z, w) in Halo axes (x forward, y left, z up; units of 10 feet). Unity-free.
    /// </summary>
    public sealed class HaloSkeleton
    {
        public struct Marker { public string Name; public int Node; public float Tx, Ty, Tz, Qx, Qy, Qz, Qw; }

        public readonly string ModelName;
        public readonly int Count;
        public readonly string[] Names;
        public readonly int[] Parent;
        public readonly float[] BindRot, BindTrans;
        public readonly List<Marker> Markers = new List<Marker>();

        public HaloSkeleton(HaloModel model)
        {
            ModelName = model.Tag != null ? model.Tag.Name : "";
            Count = model.Nodes.Count;
            Names = new string[Count];
            Parent = new int[Count];
            BindRot = new float[Count * 4];
            BindTrans = new float[Count * 3];
            for (int i = 0; i < Count; i++)
            {
                HaloModel.Node n = model.Nodes[i];
                Names[i] = n.Name;
                Parent[i] = n.Parent >= 0 && n.Parent < i ? n.Parent : (n.Parent >= 0 && n.Parent < Count && n.Parent != i ? n.Parent : -1);
                HQ.Std(n.Qi, n.Qj, n.Qk, n.Qw, BindRot, i * 4);
                BindTrans[i * 3] = n.Translation.X; BindTrans[i * 3 + 1] = n.Translation.Y; BindTrans[i * 3 + 2] = n.Translation.Z;
            }

            for (int m = 0; m < model.Markers.Count; m++)
            {
                HaloModel.Marker mk = model.Markers[m];
                if (mk.Instances.Count == 0) continue;
                HaloModel.MarkerInstance x = mk.Instances[0];
                var r = new float[4];
                HQ.Std(x.Qi, x.Qj, x.Qk, x.Qw, r, 0);
                Markers.Add(new Marker { Name = mk.Name, Node = x.Node, Tx = x.Translation.X, Ty = x.Translation.Y, Tz = x.Translation.Z, Qx = r[0], Qy = r[1], Qz = r[2], Qw = r[3] });
            }
        }

        public int Node(string name)
        {
            for (int i = 0; i < Count; i++) if (string.Equals(Names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public bool FindMarker(string name, out Marker m)
        {
            for (int i = 0; i < Markers.Count; i++) if (string.Equals(Markers[i].Name, name, StringComparison.OrdinalIgnoreCase)) { m = Markers[i]; return true; }
            m = default(Marker);
            return false;
        }

        /// <summary>For each node of the animation graph, the skeleton node of the same name (-1 none).</summary>
        public int[] MapFrom(HaloAnimGraph g)
        {
            var map = new int[g.NodeNames.Length];
            for (int i = 0; i < map.Length; i++) map[i] = Node(g.NodeNames[i]);
            return map;
        }
    }

    /// <summary>
    /// One pose of a <see cref="HaloSkeleton"/>: local rotations / translations (from the rest pose, then animations on top) and the model-space result.
    /// Base animations set every node they carry, replacements only the nodes they animate (reload, melee: the arms and torso over the legs), overlays
    /// turn on top (the aim grid, recoil): q = q_base * delta (the delta in the node's own frame; checked in the harness: the aim grid then turns the
    /// rifle onto its yaw / pitch without twisting it). Frames are interpolated (nlerp), weights blend from what is there. No allocation per call.
    /// </summary>
    public sealed class HaloPose
    {
        public readonly HaloSkeleton Skel;
        public readonly float[] LRot, LTrans, MRot, MTrans;
        private readonly float[] _q = new float[4], _q2 = new float[4];

        /// <summary>Lab / harness: false = overlays as delta * base (the other order), to compare.</summary>
        public static bool OverlayPost = true;

        public HaloPose(HaloSkeleton s)
        {
            Skel = s;
            LRot = new float[s.Count * 4];
            LTrans = new float[s.Count * 3];
            MRot = new float[s.Count * 4];
            MTrans = new float[s.Count * 3];
            Reset();
        }

        public void Reset()
        {
            Array.Copy(Skel.BindRot, LRot, LRot.Length);
            Array.Copy(Skel.BindTrans, LTrans, LTrans.Length);
        }

        /// <summary>Frame position of a time: the two frames and the fraction between (looping wraps to the loop frame, else clamps).</summary>
        public static void FrameAt(HaloAnimation a, float time, bool loop, out int f0, out int f1, out float u)
        {
            int n = Math.Max(1, a.Frames);
            float x = time / HaloAnimation.FrameTime;
            if (n == 1 || x <= 0f) { f0 = f1 = 0; u = 0f; return; }
            if (loop)
            {
                x %= n;
                f0 = (int)x;
                u = x - f0;
                f1 = f0 + 1 >= n ? Math.Max(0, Math.Min(n - 1, a.LoopFrame)) : f0 + 1;
                return;
            }

            if (x >= n - 1) { f0 = f1 = n - 1; u = 0f; return; }
            f0 = (int)x;
            u = x - f0;
            f1 = f0 + 1;
        }

        /// <summary>A base or replacement animation at a time, blended in by weight (1 = exactly the animation's values).</summary>
        public void Apply(HaloAnimation a, int[] map, float time, bool loop, float weight)
        {
            if (a == null || weight <= 0f || !a.Decode()) return;
            int f0, f1;
            float u;
            FrameAt(a, time, loop, out f0, out f1, out u);
            ApplyFrames(a, map, f0, f1, u, weight);
        }

        public void ApplyFrames(HaloAnimation a, int[] map, int f0, int f1, float u, float weight)
        {
            if (a == null || weight <= 0f || !a.Decode()) return;
            bool all = a.Type == HaloAnimation.Kind.Base;
            int n = a.NodeCount;
            for (int k = 0; k < n && k < map.Length; k++)
            {
                int s = map[k];
                if (s < 0) continue;
                bool rot = all || a.Rotates(k), tr = all || a.Translates(k);
                if (rot)
                {
                    HQ.Nlerp(a.Rot, (f0 * n + k) * 4, a.Rot, (f1 * n + k) * 4, u, _q, 0);
                    if (weight >= 0.999f) { LRot[s * 4] = _q[0]; LRot[s * 4 + 1] = _q[1]; LRot[s * 4 + 2] = _q[2]; LRot[s * 4 + 3] = _q[3]; }
                    else HQ.Nlerp(LRot, s * 4, _q, 0, weight, LRot, s * 4);
                }

                if (tr)
                {
                    int i0 = (f0 * n + k) * 3, i1 = (f1 * n + k) * 3;
                    for (int c = 0; c < 3; c++)
                    {
                        float v = a.Trans[i0 + c] + (a.Trans[i1 + c] - a.Trans[i0 + c]) * u;
                        LTrans[s * 3 + c] += (v - LTrans[s * 3 + c]) * weight;
                    }
                }
            }
        }

        /// <summary>An overlay's frame pair on top of the pose (rotations only: the overlays' translations are the rest values), by weight.</summary>
        public void Overlay(HaloAnimation a, int[] map, int f0, int f1, float u, float weight)
        {
            if (a == null || weight <= 0f || !a.Decode()) return;
            int n = a.NodeCount;
            for (int k = 0; k < n && k < map.Length; k++)
            {
                int s = map[k];
                if (s < 0 || !a.Rotates(k)) continue;
                HQ.Nlerp(a.Rot, (f0 * n + k) * 4, a.Rot, (f1 * n + k) * 4, u, _q, 0);
                if (weight < 0.999f) { _q2[0] = 0f; _q2[1] = 0f; _q2[2] = 0f; _q2[3] = 1f; HQ.Nlerp(_q2, 0, _q, 0, weight, _q, 0); }
                if (OverlayPost) HQ.Mul(LRot, s * 4, _q, 0, LRot, s * 4);
                else HQ.Mul(_q, 0, LRot, s * 4, LRot, s * 4);
            }
        }

        /// <summary>
        /// The aiming overlay (aim-still / aim-move) for a yaw and pitch (radians, + = left / up as Halo's), the way the game lays the grid out: columns
        /// from full right to full left yaw, rows from full down to full up pitch (rightFrames + 1 + leftFrames by downFrames + 1 + upFrames frames);
        /// bilinear between the four nearest frames.
        /// </summary>
        public void Aim(HaloAnimation a, int[] map, float yaw, float pitch, float rightYawPerFrame, float leftYawPerFrame, int rightFrames, int leftFrames,
                        float downPitchPerFrame, float upPitchPerFrame, int downFrames, int upFrames, float weight)
        {
            if (a == null || weight <= 0f || !a.Decode()) return;
            int cols = rightFrames + 1 + leftFrames, rows = downFrames + 1 + upFrames;
            if (cols * rows > a.Frames || cols <= 0 || rows <= 0) { cols = Math.Max(1, a.Frames); rows = 1; }
            float cx = yaw >= 0f ? rightFrames + (leftYawPerFrame > 1e-4f ? yaw / leftYawPerFrame : 0f) : rightFrames + (rightYawPerFrame > 1e-4f ? yaw / rightYawPerFrame : 0f);
            float cy = pitch >= 0f ? downFrames + (upPitchPerFrame > 1e-4f ? pitch / upPitchPerFrame : 0f) : downFrames + (downPitchPerFrame > 1e-4f ? pitch / downPitchPerFrame : 0f);
            cx = Math.Max(0f, Math.Min(cols - 1, cx));
            cy = Math.Max(0f, Math.Min(rows - 1, cy));
            int x0 = (int)cx, y0 = (int)cy;
            int x1 = Math.Min(cols - 1, x0 + 1), y1 = Math.Min(rows - 1, y0 + 1);
            float ux = cx - x0, uy = cy - y0;
            int n = a.NodeCount;
            for (int k = 0; k < n && k < map.Length; k++)
            {
                int s = map[k];
                if (s < 0 || !a.Rotates(k)) continue;
                HQ.Nlerp(a.Rot, ((y0 * cols + x0) * n + k) * 4, a.Rot, ((y0 * cols + x1) * n + k) * 4, ux, _q, 0);
                HQ.Nlerp(a.Rot, ((y1 * cols + x0) * n + k) * 4, a.Rot, ((y1 * cols + x1) * n + k) * 4, ux, _q2, 0);
                HQ.Nlerp(_q, 0, _q2, 0, uy, _q, 0);
                if (weight < 0.999f) { _q2[0] = 0f; _q2[1] = 0f; _q2[2] = 0f; _q2[3] = 1f; HQ.Nlerp(_q2, 0, _q, 0, weight, _q, 0); }
                if (OverlayPost) HQ.Mul(LRot, s * 4, _q, 0, LRot, s * 4);
                else HQ.Mul(_q, 0, LRot, s * 4, LRot, s * 4);
            }
        }

        /// <summary>Local to model space (parents come before children in Halo's node lists).</summary>
        public void ToModel()
        {
            int n = Skel.Count;
            for (int i = 0; i < n; i++)
            {
                int p = Skel.Parent[i];
                if (p < 0 || p >= i)
                {
                    Array.Copy(LRot, i * 4, MRot, i * 4, 4);
                    Array.Copy(LTrans, i * 3, MTrans, i * 3, 3);
                    continue;
                }

                HQ.Mul(MRot, p * 4, LRot, i * 4, MRot, i * 4);
                float x, y, z;
                HQ.Rotate(MRot, p * 4, LTrans[i * 3], LTrans[i * 3 + 1], LTrans[i * 3 + 2], out x, out y, out z);
                MTrans[i * 3] = MTrans[p * 3] + x;
                MTrans[i * 3 + 1] = MTrans[p * 3 + 1] + y;
                MTrans[i * 3 + 2] = MTrans[p * 3 + 2] + z;
            }
        }

        /// <summary>A model-space point of a node's frame (its local offset x, y, z turned and moved with the node).</summary>
        public void Point(int node, float lx, float ly, float lz, out float x, out float y, out float z)
        {
            HQ.Rotate(MRot, node * 4, lx, ly, lz, out x, out y, out z);
            x += MTrans[node * 3]; y += MTrans[node * 3 + 1]; z += MTrans[node * 3 + 2];
        }

        /// <summary>A model-space direction of a node's frame.</summary>
        public void Axis(int node, float lx, float ly, float lz, out float x, out float y, out float z)
        {
            HQ.Rotate(MRot, node * 4, lx, ly, lz, out x, out y, out z);
        }

        /// <summary>A marker's model-space frame: origin and its forward (x) and up (z) axes.</summary>
        public void MarkerFrame(HaloSkeleton.Marker m, out float px, out float py, out float pz, out float fx, out float fy, out float fz, out float ux, out float uy, out float uz)
        {
            Point(m.Node, m.Tx, m.Ty, m.Tz, out px, out py, out pz);
            _q[0] = m.Qx; _q[1] = m.Qy; _q[2] = m.Qz; _q[3] = m.Qw;
            HQ.Mul(MRot, m.Node * 4, _q, 0, _q2, 0);
            HQ.Rotate(_q2, 0, 1f, 0f, 0f, out fx, out fy, out fz);
            HQ.Rotate(_q2, 0, 0f, 0f, 1f, out ux, out uy, out uz);
        }
    }

    /// <summary>Quaternion helpers on float arrays (x, y, z, w), Unity-free and allocation-free.</summary>
    public static class HQ
    {
        /// <summary>A stored Halo quaternion (i, j, k, w; inverted) as a standard rotation.</summary>
        public static void Std(float i, float j, float k, float w, float[] o, int at)
        {
            float m = (float)Math.Sqrt(i * i + j * j + k * k + w * w);
            if (m < 1e-6f) { o[at] = 0f; o[at + 1] = 0f; o[at + 2] = 0f; o[at + 3] = 1f; return; }
            o[at] = -i / m; o[at + 1] = -j / m; o[at + 2] = -k / m; o[at + 3] = w / m;
        }

        public static void Mul(float[] a, int ai, float[] b, int bi, float[] o, int oi)
        {
            float ax = a[ai], ay = a[ai + 1], az = a[ai + 2], aw = a[ai + 3];
            float bx = b[bi], by = b[bi + 1], bz = b[bi + 2], bw = b[bi + 3];
            o[oi] = aw * bx + ax * bw + ay * bz - az * by;
            o[oi + 1] = aw * by - ax * bz + ay * bw + az * bx;
            o[oi + 2] = aw * bz + ax * by - ay * bx + az * bw;
            o[oi + 3] = aw * bw - ax * bx - ay * by - az * bz;
        }

        public static void Rotate(float[] q, int qi, float vx, float vy, float vz, out float x, out float y, out float z)
        {
            float qx = q[qi], qy = q[qi + 1], qz = q[qi + 2], qw = q[qi + 3];
            float tx = 2f * (qy * vz - qz * vy), ty = 2f * (qz * vx - qx * vz), tz = 2f * (qx * vy - qy * vx);
            x = vx + qw * tx + (qy * tz - qz * ty);
            y = vy + qw * ty + (qz * tx - qx * tz);
            z = vz + qw * tz + (qx * ty - qy * tx);
        }

        /// <summary>Normalised lerp along the shorter arc (o may be a or b).</summary>
        public static void Nlerp(float[] a, int ai, float[] b, int bi, float u, float[] o, int oi)
        {
            float ax = a[ai], ay = a[ai + 1], az = a[ai + 2], aw = a[ai + 3];
            float bx = b[bi], by = b[bi + 1], bz = b[bi + 2], bw = b[bi + 3];
            if (ax * bx + ay * by + az * bz + aw * bw < 0f) { bx = -bx; by = -by; bz = -bz; bw = -bw; }
            float x = ax + (bx - ax) * u, y = ay + (by - ay) * u, z = az + (bz - az) * u, w = aw + (bw - aw) * u;
            float m = (float)Math.Sqrt(x * x + y * y + z * z + w * w);
            if (m < 1e-8f) { o[oi] = ax; o[oi + 1] = ay; o[oi + 2] = az; o[oi + 3] = aw; return; }
            o[oi] = x / m; o[oi + 1] = y / m; o[oi + 2] = z / m; o[oi + 3] = w / m;
        }
    }
}

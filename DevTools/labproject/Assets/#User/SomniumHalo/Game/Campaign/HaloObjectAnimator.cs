using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Mission module, K] A custom animation on a scenario object (script scenery_animation_start / _at_frame): an animation of a graph
    /// played once on the object's model nodes on the game clock (one frame per game tick, interpolated between ticks), holding its last
    /// frame. Base animations set the nodes, overlays turn / move them from the rest pose. Named scenery is built skinned for this.
    /// </summary>
    public sealed class HaloObjectAnimator : MonoBehaviour
    {
        HaloModelBuilder.Built _built;
        HaloAnimation _anim;
        int[] _map;
        Vector3[] _restPos;
        Quaternion[] _restRot;
        float _frame, _prevFrame;
        bool _done, _finalDrawn;

        static readonly Dictionary<HaloTag, HaloAnimGraph> Graphs = new Dictionary<HaloTag, HaloAnimGraph>();
        internal static readonly List<HaloObjectAnimator> Playing = new List<HaloObjectAnimator>();

        public string AnimationName { get { return _anim != null ? _anim.Name : ""; } }
        public bool Done { get { return _done; } }
        public float Frame { get { return _frame; } }

        internal static void ClearCache() { Graphs.Clear(); Playing.Clear(); }

        static HaloAnimGraph GraphOf(HaloTag tag)
        {
            if (tag == null) return null;
            HaloAnimGraph g;
            if (Graphs.TryGetValue(tag, out g)) return g;
            try { g = new HaloAnimGraph(tag); }
            catch (Exception e) { HaloLog.Warn("Campaign: animation graph " + tag + " unreadable: " + e.Message); g = null; }
            Graphs[tag] = g;
            return g;
        }

        /// <summary>Starts <paramref name="animation"/> of <paramref name="graph"/> (null = the object's own graph) at <paramref name="frame"/>.</summary>
        internal bool Play(HaloModelBuilder.Built built, HaloTag graph, string animation, int frame, out string why)
        {
            why = "";
            if (built == null || built.Nodes == null || built.Renderer == null) { why = "the model is not animatable (static)"; return false; }
            var g = GraphOf(graph);
            if (g == null) { why = "no animation graph"; return false; }
            var a = g.Get(animation);
            if (a == null) { why = "no animation '" + animation + "' in " + graph.Name; return false; }
            if (!a.Decode()) { why = "animation '" + animation + "' not decodable (" + a.Error + ")"; return false; }
            if (_built != built || _restPos == null)
            {
                _built = built;
                _restPos = new Vector3[built.Nodes.Length];
                _restRot = new Quaternion[built.Nodes.Length];
                for (int k = 0; k < built.Nodes.Length; k++) { _restPos[k] = built.Nodes[k].localPosition; _restRot[k] = built.Nodes[k].localRotation; }
            }
            else
            {
                for (int k = 0; k < built.Nodes.Length; k++) { built.Nodes[k].localPosition = _restPos[k]; built.Nodes[k].localRotation = _restRot[k]; }
            }
            _anim = a;
            _map = new int[a.NodeCount];
            for (int k = 0; k < a.NodeCount; k++)
            {
                int s = -1;
                if (g.NodeNames != null && k < g.NodeNames.Length && !string.IsNullOrEmpty(g.NodeNames[k]) && built.Model != null) s = built.Model.NodeIndex(g.NodeNames[k]);
                if (s < 0 && k < built.Nodes.Length) s = k;
                _map[k] = s;
            }
            _frame = _prevFrame = Mathf.Clamp(frame, 0, Mathf.Max(0, a.Frames - 1));
            _done = false; _finalDrawn = false;
            if (!Playing.Contains(this)) Playing.Add(this);
            Pose(_frame);
            return true;
        }

        /// <summary>One game tick: the next frame (holds the last).</summary>
        internal void Step()
        {
            if (_anim == null || _done) return;
            _prevFrame = _frame;
            _frame = Mathf.Min(_frame + 1f, Mathf.Max(0, _anim.Frames - 1));
            if (_frame >= _anim.Frames - 1) _done = true;
        }

        /// <summary>Every frame: the pose between the last two ticks.</summary>
        internal void Draw(float u)
        {
            if (_anim == null || _finalDrawn) return;
            if (_done) { _finalDrawn = true; Pose(_frame); return; }
            Pose(_prevFrame + (_frame - _prevFrame) * u);
        }

        void Pose(float x)
        {
            if (_built == null || _anim == null) return;
            int frames = Mathf.Max(1, _anim.Frames);
            int f0 = Mathf.Clamp((int)x, 0, frames - 1), f1 = Mathf.Min(frames - 1, f0 + 1);
            float w = x - f0;
            int n = _anim.NodeCount;
            bool all = _anim.Type == HaloAnimation.Kind.Base;
            bool overlay = _anim.Type == HaloAnimation.Kind.Overlay;
            float s = HaloSpace.Unit;
            for (int k = 0; k < n && k < _map.Length; k++)
            {
                int node = _map[k];
                if (node < 0 || node >= _built.Nodes.Length) continue;
                Transform t = _built.Nodes[node];
                if (all || _anim.Translates(k))
                {
                    int a = (f0 * n + k) * 3, b = (f1 * n + k) * 3;
                    float tx = _anim.Trans[a] + (_anim.Trans[b] - _anim.Trans[a]) * w;
                    float ty = _anim.Trans[a + 1] + (_anim.Trans[b + 1] - _anim.Trans[a + 1]) * w;
                    float tz = _anim.Trans[a + 2] + (_anim.Trans[b + 2] - _anim.Trans[a + 2]) * w;
                    Vector3 v = new Vector3(tx * s, tz * s, ty * s);
                    t.localPosition = overlay ? _restPos[node] + v : v;
                }
                if (all || _anim.Rotates(k))
                {
                    int a = (f0 * n + k) * 4, b = (f1 * n + k) * 4;
                    Quaternion q = Quaternion.Slerp(HaloSpace.Rot(_anim.Rot[a], _anim.Rot[a + 1], _anim.Rot[a + 2], _anim.Rot[a + 3]),
                                                    HaloSpace.Rot(_anim.Rot[b], _anim.Rot[b + 1], _anim.Rot[b + 2], _anim.Rot[b + 3]), w);
                    t.localRotation = overlay ? _restRot[node] * q : q;
                }
            }
        }

        private void OnDestroy() { Playing.Remove(this); }
    }
}

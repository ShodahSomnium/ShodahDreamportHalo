using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A placed device (scenario machine or control) as the World module builds it: the skinned model with its nodes, its collision on the
    /// nodes, and a pose driven by the device's own animation graph. The Mission module (K) owns the logic (power, position groups,
    /// timings, carrying players on lifts) and calls <see cref="SetPosition"/> / <see cref="SetPower"/>; World only poses the model.
    /// Halo animates a device by sampling its "device position" animation at the device's position (0 closed / bottom .. 1 open / top)
    /// and "device power" at its power. Node values: decoded animation frames (Data/Anims/HaloAnimation, standard rotations in Halo
    /// axes) mapped like HaloModelBuilder's rest pose. No allocations per call.
    /// </summary>
    public sealed class HaloWorldDevice
    {
        public string Kind;
        public int Index;
        public HaloScenario.Placement Placement;
        public HaloObjectDef Def;
        public GameObject Root;
        public HaloModelBuilder.Built Model;
        public string Name;
        /// <summary>BSP membership bits (0 = every BSP).</summary>
        public int BspMask;
        public float Position { get; private set; }
        public float Power { get; private set; }

        /// <summary>A device pose driver for a model someone else built (the Mission module's own devices): the object tag (machine / control)
        /// and its HaloModelBuilder model (skinned, not Static).</summary>
        public static HaloWorldDevice Create(HaloTag objectTag, GameObject root, HaloModelBuilder.Built model)
        {
            var d = new HaloWorldDevice { Kind = objectTag != null && objectTag.Is("ctrl") ? "control" : "machine", Index = -1, Root = root, Model = model, Name = root != null ? root.name : "device" };
            try { d.Def = objectTag != null ? new HaloObjectDef(objectTag) : null; } catch (Exception) { d.Def = null; }
            return d;
        }

        HaloAnimGraph _graph;
        HaloAnimation _position, _power;
        int[] _nodeMap;
        bool _looked;

        void Look()
        {
            if (_looked) return;
            _looked = true;
            if (Def == null || Def.AnimationGraph == null || Model == null) return;
            try
            {
                _graph = new HaloAnimGraph(Def.AnimationGraph);
                _position = Find("device position");
                _power = Find("device power");
                if (_position == null && _graph.Animations.Length > 0 && _graph.Animations.Length <= 2) _position = _graph.Animations[0];
                if (_position != null && !_position.Decode()) { HaloLog.Warn("World: device " + Name + " position animation: " + _position.Error); _position = null; }
                if (_power != null && !_power.Decode()) _power = null;
                // graph node -> model node by name
                _nodeMap = new int[_graph.NodeNames.Length];
                for (int i = 0; i < _nodeMap.Length; i++) _nodeMap[i] = Model.Model.NodeIndex(_graph.NodeNames[i]);
            }
            catch (Exception e) { HaloLog.Warn("World: device " + Name + " animations failed: " + e.Message); _position = _power = null; }
        }

        HaloAnimation Find(string name)
        {
            foreach (var a in _graph.Animations) if (a != null && string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            foreach (var a in _graph.Animations) if (a != null && a.Name != null && a.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) return a;
            return null;
        }

        /// <summary>True when the device has a position animation World can pose.</summary>
        public bool Animated { get { Look(); return _position != null; } }

        /// <summary>Poses the model at a position 0..1 (the "device position" animation).</summary>
        public void SetPosition(float t)
        {
            Position = Mathf.Clamp01(t);
            Look();
            if (_position != null) Apply(_position, Position);
        }

        /// <summary>Poses the power animation 0..1 when the device has one ("device power"); position otherwise unchanged.</summary>
        public void SetPower(float t)
        {
            Power = Mathf.Clamp01(t);
            Look();
            if (_power != null) Apply(_power, Power);
        }

        public void Show(bool shown) { if (Root != null && Root.activeSelf != shown) Root.SetActive(shown); }

        void Apply(HaloAnimation a, float t)
        {
            if (a.Frames <= 0 || a.Rot == null) return;
            float f = t * (a.Frames - 1);
            int f0 = Mathf.Clamp((int)f, 0, a.Frames - 1), f1 = Mathf.Min(f0 + 1, a.Frames - 1);
            float k = f - f0;
            int n = a.NodeCount;
            float s = HaloSpace.Unit;
            for (int i = 0; i < n && i < _nodeMap.Length; i++)
            {
                int mi = _nodeMap[i];
                if (mi < 0 || mi >= Model.Nodes.Length) continue;
                int r0 = (f0 * n + i) * 4, r1 = (f1 * n + i) * 4, t0 = (f0 * n + i) * 3, t1 = (f1 * n + i) * 3;
                if (r1 + 3 >= a.Rot.Length || t1 + 2 >= a.Trans.Length) continue;
                var q0 = HaloSpace.Rot(a.Rot[r0], a.Rot[r0 + 1], a.Rot[r0 + 2], a.Rot[r0 + 3]);
                var q1 = HaloSpace.Rot(a.Rot[r1], a.Rot[r1 + 1], a.Rot[r1 + 2], a.Rot[r1 + 3]);
                var node = Model.Nodes[mi];
                node.localRotation = Quaternion.Slerp(q0, q1, k);
                node.localPosition = new Vector3(Mathf.Lerp(a.Trans[t0], a.Trans[t1], k) * s, Mathf.Lerp(a.Trans[t0 + 2], a.Trans[t1 + 2], k) * s,
                                                 Mathf.Lerp(a.Trans[t0 + 1], a.Trans[t1 + 1], k) * s);
            }
        }
    }
}

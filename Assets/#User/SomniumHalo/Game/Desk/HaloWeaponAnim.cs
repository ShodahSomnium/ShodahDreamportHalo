using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Plays a weapon's own animations (HaloWeaponAnims: the game's third-person weapon graph, else the moving parts of its first-person animations)
    /// on a third-person weapon model HaloModelBuilder built: the slide, bolt, pump, charging handle, tubes, vents move on their node Transforms; the
    /// root ("frame gun") never moves. Combat (the held weapon) and Net (remote weapons) call <see cref="Play"/> on fire / reload / ready / overheat
    /// and <see cref="SetState"/> for held poses (an empty pistol's slide back, open vents). A part the player holds in VR is skipped
    /// (<see cref="SetHeld"/>): the animation never fights the hand; <see cref="Node"/> gives the part's Transform for the grab code.
    /// Runs in LateUpdate (order 9997, after Desk placed the weapon); writes only the parts it animates and puts them back to rest when done.
    /// No allocation per frame. Player.log: "Weapon anim: ..." once per weapon tag.
    /// </summary>
    [DefaultExecutionOrder(9997)]
    public sealed class HaloWeaponAnim : MonoBehaviour
    {
        private static readonly Dictionary<HaloTag, HaloWeaponAnims> Cache = new Dictionary<HaloTag, HaloWeaponAnims>();
        private static readonly HashSet<HaloTag> Said = new HashSet<HaloTag>();

        private HaloWeaponAnims _data;
        private Transform[] _nodes;
        private Vector3[] _restPos;
        private Quaternion[] _restRot;
        private bool[] _held, _wrote, _moved, _skip;
        private float[] _rot, _trans;
        private float _unit = HaloSpace.Unit;
        private HaloWeaponAnims.Clip _clip, _stateClip;
        private float _t, _speed = 1f, _stateT;
        private string _state;
        private readonly float[] _q = new float[4];

        public HaloTag Weapon { get { return _data != null ? _data.Weapon : null; } }
        /// <summary>The animation playing now (its game name), null when none.</summary>
        public string Playing { get { return _clip != null ? _clip.Name : null; } }
        /// <summary>Seconds left of the animation playing (0 when none).</summary>
        public float Remaining { get { return _clip != null ? Mathf.Max(0f, (_clip.Duration - _t) / Mathf.Max(0.01f, _speed)) : 0f; } }
        public string State { get { return _state; } }

        /// <summary>Every kind <see cref="Play"/> understands (HaloWeaponAnims.Kinds): fire, fire-2, chamber, reload-empty, reload-full, reload-exit, ready, put-away, overheating, overheated, overheat-exit, misfire, charged, melee, posing.</summary>
        public static string[] KindNames()
        {
            var k = new string[HaloWeaponAnims.Kinds.Length];
            for (int i = 0; i < k.Length; i++) k[i] = HaloWeaponAnims.Kinds[i][0];
            return k;
        }

        /// <summary>Adds (or finds) the player on a built weapon model. Null when the model has no animated part (the AR fires without one, the fuel rod has none).</summary>
        public static HaloWeaponAnim Attach(HaloModelBuilder.Built weapon, HaloTag weap)
        {
            if (weapon == null || weapon.Root == null || weap == null || weapon.Nodes == null) return null;
            HaloWeaponAnim a = weapon.Root.GetComponent<HaloWeaponAnim>();
            if (a != null && a._data != null && a._data.Weapon == weap) return a;
            HaloWeaponAnims d;
            if (!Cache.TryGetValue(weap, out d))
            {
                try { d = new HaloWeaponAnims(weap); }
                catch (Exception e) { HaloLog.Warn("Weapon anim: " + weap.Name + ": " + e.Message); d = null; }
                Cache[weap] = d;
            }

            if (d == null || d.Clips.Count == 0 || d.NodeNames.Length != weapon.Nodes.Length) return null;
            if (a == null) a = weapon.Root.AddComponent<HaloWeaponAnim>();
            a.Bind(d, weapon.Nodes);
            if (Said.Add(weap))
            {
                var sb = new System.Text.StringBuilder();
                for (int k = 0; k < HaloWeaponAnims.Kinds.Length; k++)
                {
                    HaloWeaponAnims.Clip c = d.Find(HaloWeaponAnims.Kinds[k][0]);
                    if (c != null) sb.Append(' ').Append(HaloWeaponAnims.Kinds[k][0]).Append("='").Append(c.Name).Append('\'');
                }

                HaloLog.Info("Weapon anim: " + weap.Name + ": " + d.Clips.Count + " animations on its parts:" + sb);
            }

            return a;
        }

        /// <summary>The cached data of a weapon (the clips, node names) without a model.</summary>
        public static HaloWeaponAnims DataOf(HaloTag weap)
        {
            if (weap == null) return null;
            HaloWeaponAnims d;
            if (!Cache.TryGetValue(weap, out d)) { try { d = new HaloWeaponAnims(weap); } catch (Exception) { d = null; } Cache[weap] = d; }
            return d;
        }

        private void Bind(HaloWeaponAnims d, Transform[] nodes)
        {
            Restore();
            _data = d;
            _nodes = nodes;
            int n = nodes.Length;
            _restPos = new Vector3[n];
            _restRot = new Quaternion[n];
            _held = new bool[n];
            _wrote = new bool[n];
            _moved = new bool[n];
            _skip = new bool[n];
            _rot = new float[n * 4];
            _trans = new float[n * 3];
            _unit = HaloSpace.Unit;
            for (int i = 0; i < n; i++)
            {
                if (nodes[i] == null) continue;
                _restPos[i] = nodes[i].localPosition;
                _restRot[i] = nodes[i].localRotation;
                float h = Mathf.Sqrt(d.RestTrans[i * 3] * d.RestTrans[i * 3] + d.RestTrans[i * 3 + 1] * d.RestTrans[i * 3 + 1] + d.RestTrans[i * 3 + 2] * d.RestTrans[i * 3 + 2]);
                if (h > 1e-4f) _unit = _restPos[i].magnitude / h;      // (the builder's scale: metres per Halo unit for this model)
            }

            _clip = null;
            _state = null;
            _stateClip = null;
        }

        /// <summary>Has this weapon an animation for the kind (or of that name)?</summary>
        public bool Has(string kind) { return _data != null && _data.Find(kind) != null; }

        /// <summary>
        /// Plays a kind once ("fire", "reload-empty", ... or an animation's own name). duration &gt; 0 stretches it to that many seconds (e.g. the
        /// weapon's reload time). False when the weapon has no such animation (nothing changes then).
        /// </summary>
        public bool Play(string kind, float duration = -1f)
        {
            HaloWeaponAnims.Clip c = _data != null ? _data.Find(kind) : null;
            if (c == null) return false;
            _clip = c;
            _t = 0f;
            _speed = duration > 0.01f && c.Duration > 0.01f ? c.Duration / duration : 1f;
            return true;
        }

        /// <summary>Stops the animation playing (the parts go back to rest or to the held state).</summary>
        public void Stop() { _clip = null; }

        /// <summary>
        /// A held pose while on: "empty" (the slide / bolt held back - the reload-empty animation's moment the part is furthest back), "overheated"
        /// (the vents open: the overheated / overheating animation's middle), or any kind / animation name with at = 0..1 of its length. off = rest.
        /// </summary>
        public void SetState(string state, bool on, float at = -1f)
        {
            if (!on || string.IsNullOrEmpty(state) || _data == null) { _state = null; _stateClip = null; return; }
            string kind = state == "empty" ? "reload-empty" : state == "overheated" ? "overheated" : state;
            HaloWeaponAnims.Clip c = _data.Find(kind);
            if (c == null && state == "overheated") c = _data.Find("overheating");
            if (c == null) { _state = null; _stateClip = null; return; }
            _state = state;
            _stateClip = c;
            for (int i = 0; i < _skip.Length; i++)
            {
                string nm = _data.NodeNames[i];
                _skip[i] = state == "empty" && (nm.IndexOf("magazine", StringComparison.OrdinalIgnoreCase) >= 0 || nm.IndexOf("bullet", StringComparison.OrdinalIgnoreCase) >= 0);      // (empty: only the slide / bolt stays back, the magazine stays in)
            }

            _stateT = at >= 0f ? Mathf.Clamp01(at) * c.Duration : state == "empty" ? FurthestBack(c) : c.Duration * 0.5f;
        }

        /// <summary>The time in the clip where its parts are furthest from rest (an empty pistol's slide back, the sniper's bolt open).</summary>
        private float FurthestBack(HaloWeaponAnims.Clip c)
        {
            float best = 0f, bestT = 0f;
            int n = _nodes.Length;
            for (int f = 0; f < c.Anim.Frames && f < 40; f++)
            {
                float t = f * HaloAnimation.FrameTime;
                if (!_data.Sample(c, t, _rot, _trans, _moved)) break;
                for (int i = 0; i < n; i++)
                {
                    if (!_moved[i] || _skip[i]) continue;
                    float dx = _trans[i * 3] - _data.RestTrans[i * 3], dy = _trans[i * 3 + 1] - _data.RestTrans[i * 3 + 1], dz = _trans[i * 3 + 2] - _data.RestTrans[i * 3 + 2];
                    float d = dx * dx + dy * dy + dz * dz;
                    if (d > best + 1e-9f) { best = d; bestT = t; }
                }
            }

            return bestT;
        }

        /// <summary>The player's hand holds this part (VR: the slide, the charging handle, the bolt, the pump): the animation leaves it alone while held.</summary>
        public void SetHeld(string node, bool held)
        {
            int i = _data != null ? _data.Node(node) : -1;
            if (i < 0) return;
            _held[i] = held;
            if (held) _wrote[i] = false;      // (the hand owns it now: never put back by us)
        }

        /// <summary>A part's Transform by its node name ("slide" or "frame slide"), null when the model has none.</summary>
        public Transform Node(string name)
        {
            int i = _data != null ? _data.Node(name) : -1;
            return i >= 0 && _nodes != null ? _nodes[i] : null;
        }

        /// <summary>A part's rest pose (local, as built), for grab code that slides it along its track.</summary>
        public bool Rest(string name, out Vector3 localPos, out Quaternion localRot)
        {
            int i = _data != null ? _data.Node(name) : -1;
            localPos = i >= 0 ? _restPos[i] : Vector3.zero;
            localRot = i >= 0 ? _restRot[i] : Quaternion.identity;
            return i >= 0;
        }

        private void LateUpdate()
        {
            if (_data == null || _nodes == null) return;
            Step(Time.deltaTime);
        }

        /// <summary>Advances and writes the pose now (the lab steps it directly).</summary>
        public void Step(float dt)
        {
            HaloWeaponAnims.Clip c = null;
            float t = 0f;
            if (_clip != null)
            {
                _t += dt * _speed;
                if (_t >= _clip.Duration) _clip = null;
                else { c = _clip; t = _t; }
            }

            if (c == null && _stateClip != null) { c = _stateClip; t = _stateT; }
            int n = _nodes.Length;
            bool sampled = c != null && _data.Sample(c, t, _rot, _trans, _moved);
            for (int i = 0; i < n; i++)
            {
                Transform x = _nodes[i];
                if (x == null || _held[i]) continue;
                if (sampled && _moved[i] && !(c == _stateClip && _clip == null && _skip[i]))
                {
                    x.localPosition = new Vector3(_trans[i * 3] * _unit, _trans[i * 3 + 2] * _unit, _trans[i * 3 + 1] * _unit);
                    x.localRotation = HaloSpace.Rot(_rot[i * 4], _rot[i * 4 + 1], _rot[i * 4 + 2], _rot[i * 4 + 3]);
                    _wrote[i] = true;
                }
                else if (_wrote[i])
                {
                    x.localPosition = _restPos[i];
                    x.localRotation = _restRot[i];
                    _wrote[i] = false;
                }
            }
        }

        /// <summary>Every part back to rest (the held ones too).</summary>
        public void Restore()
        {
            if (_nodes == null) return;
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_nodes[i] == null || !_wrote[i]) continue;
                _nodes[i].localPosition = _restPos[i];
                _nodes[i].localRotation = _restRot[i];
                _wrote[i] = false;
            }

            _clip = null;
        }

        private void OnDisable() { Restore(); }
    }
}

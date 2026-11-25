using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Halo lights its objects from the lightmap of the ground beneath them: this component samples <see cref="HaloWorld.LightAt"/>
    /// under the object and gives every renderer below it (SomniumHalo/Model and /Env materials) an ambient + directional light from
    /// that colour and the lightmap's incident direction (MaterialPropertyBlock: _ObjAmb, _ObjL0Dir/_ObjL0Col, _ObjL1Dir/_ObjL1Col).
    /// Static objects sample once; moving ones re-sample a few times a second when they moved. Without a world (hub) the map defaults
    /// (globals) light them. Use: <c>HaloObjectLight.Attach(modelRoot, isStatic)</c>; call <see cref="Refresh"/> after adding renderers
    /// (a weapon swapped in). No allocations per frame.
    /// </summary>
    public sealed class HaloObjectLight : MonoBehaviour
    {
        // how the lightmap colour C under an object becomes its light (ours: tuned against the game's look of objects on lit ground)
        public const float AmbientShare = 0.45f;   // ours
        public const float DirectShare = 0.75f;    // ours
        public const float SkyShare = 0.12f;       // ours

        static readonly List<HaloObjectLight> All = new List<HaloObjectLight>();
        static readonly int IdAmb = Shader.PropertyToID("_ObjAmb"), IdL0Dir = Shader.PropertyToID("_ObjL0Dir"), IdL0Col = Shader.PropertyToID("_ObjL0Col"),
            IdL1Dir = Shader.PropertyToID("_ObjL1Dir"), IdL1Col = Shader.PropertyToID("_ObjL1Col");

        public bool Static;
        /// <summary>Height above the object's origin the ground search starts from (metres).</summary>
        public float ProbeHeight = 0.3f;

        Renderer[] _renderers = new Renderer[0];
        MaterialPropertyBlock _block;
        Vector3 _sampledAt = new Vector3(float.MaxValue, 0f, 0f);
        bool _sampled, _worldWasReady;
        float _next;

        public static HaloObjectLight Attach(GameObject root, bool isStatic)
        {
            if (root == null) return null;
            var l = root.GetComponent<HaloObjectLight>();
            if (l == null) l = root.AddComponent<HaloObjectLight>();
            l.Static = isStatic;
            l.Refresh();
            return l;
        }

        /// <summary>Forgets every sample (a new world: everything samples again).</summary>
        public static void Clear()
        {
            for (int i = 0; i < All.Count; i++) if (All[i] != null) { All[i]._sampled = false; All[i]._sampledAt = new Vector3(float.MaxValue, 0f, 0f); }
        }

        /// <summary>Collects the renderers again and samples now.</summary>
        public void Refresh()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _sampled = false;
            Sample();
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Update()
        {
            bool ready = HaloWorld.Ready;
            if (ready != _worldWasReady) { _worldWasReady = ready; _sampled = false; }
            if (_sampled && Static) return;
            if (Time.time < _next) return;
            _next = Time.time + 0.2f + (GetInstanceID() & 7) * 0.01f;
            if (_sampled && (transform.position - _sampledAt).sqrMagnitude < 0.0625f) return;
            Sample();
        }

        void Sample()
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            Vector3 p = transform.position + Vector3.up * ProbeHeight;
            Color c = Color.white; Vector3 d = Vector3.up;
            bool found = HaloWorld.Ready && HaloWorld.LightAt(p, out c, out d);
            _sampledAt = transform.position;
            _sampled = true;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                if (found)
                {
                    _block.SetVector(IdAmb, new Vector4(c.r * AmbientShare, c.g * AmbientShare, c.b * AmbientShare, 1f));
                    _block.SetVector(IdL0Dir, new Vector4(d.x, d.y, d.z, 0f));
                    _block.SetVector(IdL0Col, new Vector4(c.r * DirectShare, c.g * DirectShare, c.b * DirectShare, 0f));
                    _block.SetVector(IdL1Dir, new Vector4(0f, 1f, 0f, 0f));
                    _block.SetVector(IdL1Col, new Vector4(c.r * SkyShare, c.g * SkyShare, c.b * SkyShare, 0f));
                }
                else _block.SetVector(IdAmb, Vector4.zero);
                r.SetPropertyBlock(_block);
            }
        }
    }
}

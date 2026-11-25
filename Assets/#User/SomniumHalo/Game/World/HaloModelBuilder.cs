using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Builds a gbxmodel as Unity objects: one child Transform per model node (named as the node, so wheels, turrets, barrels and the
    /// steering wheel can be turned by name), one SkinnedMeshRenderer over all parts of the chosen permutation / level of detail
    /// (submesh per shader), and markers as empty children of their node ("marker:name"). The vertices are in the model's rest space,
    /// so bind poses are the inverse rest transforms of the node objects and the rest pose draws exactly as stored whatever the node
    /// rotation convention. Halo node rotation quaternions are applied CONJUGATED (Halo stores the inverse rotation), see
    /// <see cref="NodeRotation"/>: the vehicle and weapon modules verify it with their own animations.
    /// Halo space -> Unity: (x, y, z) -> (x, z, y) * 3.048 m, winding reversed (HaloSpace). Mesh building can be split: <see cref="Prepare"/>
    /// (any thread, Unity-free arrays) then <see cref="Build"/> (main thread).
    /// </summary>
    public static class HaloModelBuilder
    {
        public sealed class Options
        {
            /// <summary>Level of detail, 4 = super high (default) .. 0 = super low.</summary>
            public int Lod = 4;
            /// <summary>Permutation name per region (null / missing = the region's first permutation, "__base" when present).</summary>
            public Dictionary<string, string> Permutations;
            /// <summary>Regions to leave out (by name).</summary>
            public HashSet<string> SkipRegions;
            public bool CastShadows = true;
            /// <summary>Layer for every created object (-1 = keep the parent's).</summary>
            public int Layer = -1;
            /// <summary>Uniform scale on top of the metre conversion (a weapon scaled to the avatar, ...).</summary>
            public float Scale = 1f;
            /// <summary>A plain MeshRenderer in the rest pose instead of a SkinnedMeshRenderer (scenery that never animates; cheaper).
            /// The node transforms and markers are still created.</summary>
            public bool Static;
            /// <summary>Sky geometry: drawn around the eye at the far plane with the sky variants of the materials (HaloMaterials).</summary>
            public bool Sky;
            /// <summary>Light the model from the lightmap under it (adds a <see cref="HaloObjectLight"/> to the root; default on).</summary>
            public bool Light = true;
        }

        public sealed class Prepared
        {
            public HaloModel Model;
            public Vector3[] Positions, Normals; public Vector4[] Tangents; public Vector2[] Uvs; public BoneWeight[] Weights;
            public List<int[]> SubMeshes = new List<int[]>();
            public List<int> SubShaders = new List<int>();
        }

        /// <summary>A built model: root, node transforms by index, the renderer.</summary>
        public sealed class Built
        {
            public GameObject Root;
            public Transform[] Nodes;
            /// <summary>The skinned renderer (null for a static or sky build: see <see cref="Mesh"/>).</summary>
            public SkinnedMeshRenderer Renderer;
            /// <summary>The renderer of the model, skinned or static.</summary>
            public Renderer Mesh;
            public HaloModel Model;
            public readonly Dictionary<string, Transform> Markers = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);

            public Transform Node(string name)
            {
                int i = Model.NodeIndex(name);
                return i >= 0 ? Nodes[i] : null;
            }

            public Transform Marker(string name)
            {
                Transform t;
                return Markers.TryGetValue(name, out t) ? t : null;
            }
        }

        public static Quaternion NodeRotation(HaloModel.Node n)
        {
            // Halo stores node rotations inverted: conjugate, then change basis (HaloSpace.Rot).
            return HaloSpace.Rot(-n.Qi, -n.Qj, -n.Qk, n.Qw);
        }

        public static Quaternion MarkerRotation(HaloModel.MarkerInstance m)
        {
            return HaloSpace.Rot(-m.Qi, -m.Qj, -m.Qk, m.Qw);
        }

        /// <summary>Unity-free part of the build (worker thread friendly).</summary>
        public static Prepared Prepare(HaloModel model, Options o)
        {
            o = o ?? new Options();
            var p = new Prepared { Model = model };
            var pos = new List<Vector3>(); var nor = new List<Vector3>(); var tan = new List<Vector4>(); var uv = new List<Vector2>(); var bw = new List<BoneWeight>();
            var byShader = new Dictionary<int, List<int>>();
            float s = HaloSpace.Unit * o.Scale;
            foreach (var region in model.Regions)
            {
                if (o.SkipRegions != null && o.SkipRegions.Contains(region.Name)) continue;
                HaloModel.Permutation perm = PickPermutation(region, o);
                if (perm == null) continue;
                int geoIndex = -1;
                for (int l = Mathf.Clamp(o.Lod, 0, 4); l >= 0 && geoIndex < 0; l--) geoIndex = perm.Lod[l];
                if (geoIndex < 0 || geoIndex >= model.Geometries.Count) continue;
                foreach (var part in model.Geometries[geoIndex].Parts)
                {
                    var d = model.Read(part);
                    int baseV = pos.Count;
                    for (int i = 0; i < part.VertexCount; i++)
                    {
                        var hp = d.Position[i];
                        pos.Add(new Vector3(hp.X * s, hp.Z * s, hp.Y * s));
                        nor.Add(new Vector3(d.Normal[i].X, d.Normal[i].Z, d.Normal[i].Y));
                        // Halo tangent frame: the reflection flips handedness, so w = -1
                        tan.Add(new Vector4(d.Tangent[i].X, d.Tangent[i].Z, d.Tangent[i].Y, -1f));
                        uv.Add(new Vector2(d.U[i], d.V[i]));
                        int n0 = Mathf.Clamp(d.Node0[i], 0, Math.Max(0, model.Nodes.Count - 1));
                        int n1 = d.Node1[i] >= 0 && d.Node1[i] < model.Nodes.Count ? d.Node1[i] : n0;
                        float w0 = d.Weight0[i], w1 = d.Node1[i] >= 0 ? d.Weight1[i] : 0f;
                        if (w0 + w1 <= 0.0001f) { w0 = 1f; w1 = 0f; }
                        float sum = w0 + w1;
                        bw.Add(new BoneWeight { boneIndex0 = n0, weight0 = w0 / sum, boneIndex1 = n1, weight1 = w1 / sum });
                    }
                    List<int> list;
                    if (!byShader.TryGetValue(part.Shader, out list)) { list = new List<int>(); byShader[part.Shader] = list; }
                    // Halo's strips run clockwise about the normal in its right-handed space; the axis swap mirrors that into Unity's
                    // clockwise front face, so the order stays (verified: all Blood Gulch models, triangle normal vs vertex normal).
                    for (int t = 0; t + 2 < d.Triangles.Length; t += 3)
                    {
                        list.Add(baseV + d.Triangles[t]); list.Add(baseV + d.Triangles[t + 1]); list.Add(baseV + d.Triangles[t + 2]);
                    }
                }
            }
            p.Positions = pos.ToArray(); p.Normals = nor.ToArray(); p.Tangents = tan.ToArray(); p.Uvs = uv.ToArray(); p.Weights = bw.ToArray();
            foreach (var kv in byShader) { p.SubShaders.Add(kv.Key); p.SubMeshes.Add(kv.Value.ToArray()); }
            return p;
        }

        static HaloModel.Permutation PickPermutation(HaloModel.Region region, Options o)
        {
            if (region.Permutations.Count == 0) return null;
            string want;
            if (o.Permutations != null && o.Permutations.TryGetValue(region.Name, out want))
                foreach (var p in region.Permutations) if (string.Equals(p.Name, want, StringComparison.OrdinalIgnoreCase)) return p;
            foreach (var p in region.Permutations) if (p.Name == "__base") return p;
            // permutations starting with '~' are damaged / blurred / alternates: skip them by default
            foreach (var p in region.Permutations) if (!p.Name.StartsWith("~")) return p;
            return region.Permutations[0];
        }

        /// <summary>Builds the model under <paramref name="parent"/> (main thread). Returns null on failure (logged).</summary>
        public static Built Build(HaloTag modelTag, Transform parent, Options o = null)
        {
            if (modelTag == null) return null;
            try
            {
                var model = new HaloModel(modelTag);
                return Build(Prepare(model, o), parent, o);
            }
            catch (Exception e)
            {
                HaloLog.Warn("Model: " + modelTag + " failed: " + e.Message);
                return null;
            }
        }

        public static Built Build(Prepared p, Transform parent, Options o = null)
        {
            o = o ?? new Options();
            var model = p.Model;
            var b = new Built { Model = model };
            b.Root = new GameObject(System.IO.Path.GetFileName(model.Tag.Name));
            b.Root.transform.SetParent(parent, false);
            float s = HaloSpace.Unit * o.Scale;
            int nodeCount = Math.Max(1, model.Nodes.Count);
            b.Nodes = new Transform[nodeCount];
            if (model.Nodes.Count == 0) b.Nodes[0] = b.Root.transform;
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                var n = model.Nodes[i];
                var go = new GameObject(n.Name);
                b.Nodes[i] = go.transform;
            }
            for (int i = 0; i < model.Nodes.Count; i++)
            {
                var n = model.Nodes[i];
                Transform par = n.Parent >= 0 && n.Parent < model.Nodes.Count ? b.Nodes[n.Parent] : b.Root.transform;
                b.Nodes[i].SetParent(par, false);
                b.Nodes[i].localPosition = new Vector3(n.Translation.X * s, n.Translation.Z * s, n.Translation.Y * s);
                b.Nodes[i].localRotation = NodeRotation(n);
            }
            foreach (var mk in model.Markers)
            {
                for (int k = 0; k < mk.Instances.Count; k++)
                {
                    var inst = mk.Instances[k];
                    Transform node = inst.Node < b.Nodes.Length ? b.Nodes[inst.Node] : b.Root.transform;
                    var go = new GameObject("marker:" + mk.Name + (k > 0 ? "#" + k : ""));
                    go.transform.SetParent(node, false);
                    go.transform.localPosition = new Vector3(inst.Translation.X * s, inst.Translation.Z * s, inst.Translation.Y * s);
                    go.transform.localRotation = MarkerRotation(inst);
                    if (k == 0) b.Markers[mk.Name] = go.transform;
                }
            }
            if (p.Positions.Length > 0)
            {
                // submeshes: one per shader, plus a copy of the triangles for every extra layer of a chicago shader (drawn after it)
                var subs = new List<int[]>();
                var mats = new List<Material>();
                for (int i = 0; i < p.SubMeshes.Count; i++)
                {
                    int si = p.SubShaders[i];
                    HaloTag sh = si >= 0 && si < model.Shaders.Count ? model.Shaders[si].Shader : null;
                    subs.Add(p.SubMeshes[i]);
                    mats.Add(sh != null ? HaloMaterials.Get(sh, o.Sky) : HaloMaterials.Fallback);
                    if (sh != null)
                        foreach (var extra in HaloMaterials.ExtraLayers(sh, o.Sky)) { subs.Add(p.SubMeshes[i]); mats.Add(extra); }
                }
                var mesh = new Mesh { name = b.Root.name };
                if (p.Positions.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = p.Positions; mesh.normals = p.Normals; mesh.tangents = p.Tangents; mesh.uv = p.Uvs;
                mesh.subMeshCount = subs.Count;
                for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i, false);
                mesh.RecalculateBounds();
                if (o.Sky) mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
                var rgo = new GameObject("mesh");
                rgo.transform.SetParent(b.Root.transform, false);
                // the world is lightmapped and nothing receives realtime shadows: casting would only cost (Options.CastShadows kept for later)
                var shadows = o.CastShadows && !o.Sky && HaloWorld.ObjectShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                if (o.Static || o.Sky)
                {
                    rgo.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = rgo.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = mats.ToArray();
                    mr.shadowCastingMode = shadows;
                    mr.receiveShadows = false;
                    mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    b.Mesh = mr;
                }
                else
                {
                    mesh.boneWeights = p.Weights;
                    var bind = new Matrix4x4[nodeCount];
                    for (int i = 0; i < nodeCount; i++) bind[i] = b.Nodes[i].worldToLocalMatrix * b.Root.transform.localToWorldMatrix;
                    mesh.bindposes = bind;
                    var smr = rgo.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = mesh;
                    smr.bones = b.Nodes;
                    smr.rootBone = b.Nodes[0];
                    smr.sharedMaterials = mats.ToArray();
                    smr.updateWhenOffscreen = false;
                    smr.localBounds = mesh.bounds;
                    smr.shadowCastingMode = shadows;
                    smr.receiveShadows = false;
                    smr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    smr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    b.Renderer = smr;
                    b.Mesh = smr;
                }
            }
            if (o.Layer >= 0) SetLayer(b.Root.transform, o.Layer);
            if (o.Light && !o.Sky && b.Mesh != null) HaloObjectLight.Attach(b.Root, o.Static);
            return b;
        }

        public static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }
    }
}

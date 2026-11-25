using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>The unit's body: the model with its permutations and change colour, the hit boxes, the animation written onto the nodes.</summary>
    public partial class HaloUnit
    {
        /// <summary>One hit box: a collider on a model node, from a collision-model node's BSP (its region, material).</summary>
        public sealed class Hitbox
        {
            public BoxCollider Box;
            public int Node, CollNode, Region, Material;
            public bool Head;
            /// <summary>Halo's unit damage table region (0 gut, 1 chest, 2 head, 3 l-arm, 4 l-hand, 5 l-leg, 6 l-foot, 7 r-arm, 8 r-hand, 9 r-leg, 10 r-foot).</summary>
            public int AnimRegion;
        }

        public HaloModelBuilder.Built Built { get; private set; }
        /// <summary>The model holder (child of the unit root, turned so the model's +X looks along the unit's forward).</summary>
        public Transform ModelHolder { get; private set; }
        public readonly List<Hitbox> Hitboxes = new List<Hitbox>();
        public HaloUnitAnimator Animator { get; private set; }
        /// <summary>Active camouflage (actv flags 0x10 active / 0x20 super active camouflage: the stealth elites): the body and weapon are not
        /// drawn (as the players' camouflage: HaloRemoteView), shown for a moment when it fires or is hit; the shield overlay shimmers faintly.
        /// Drops with the death.</summary>
        public bool Camouflaged { get; private set; }
        /// <summary>The permutation chosen per model region (region name -> permutation name).</summary>
        public readonly Dictionary<string, string> Permutations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Color ChangeColorA { get; private set; } = Color.white;

        private Transform[] _nodes;
        private Transform _headNode, _pelvisNode;
        private HaloSkeleton _skel;
        private Rigidbody _boxBody;
        private int[] _skelToNode;
        private readonly HashSet<string> _destroyedRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A model marker of the unit ("right hand", "head", "body", "primary trigger" ...), null when none.</summary>
        public Transform Marker(string name) { return Built != null ? Built.Marker(name) : null; }

        private void BuildBody()
        {
            if (ModelHolder == null)
            {
                var holder = new GameObject("model");
                holder.transform.SetParent(transform, false);
                holder.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);      // HaloModelBuilder: Halo +X (forward) -> the unit's +Z
                ModelHolder = holder.transform;
                ChoosePermutations();
                float r, g, b;
                if (Def.ColorA(_rng, out r, out g, out b)) ChangeColorA = new Color(r, g, b, 1f);
                Camouflaged = Def.ActiveCamouflage || Def.SuperActiveCamouflage;
                if (Camouflaged && _camoLogged++ < 4) HaloLog.Info("Characters: " + Def.Name + " has active camouflage" + (Def.SuperActiveCamouflage ? " (super)" : ""));
            }

            HoldLatchers();      // (infection forms latched to the old model's body marker would go with it)
            if (_weaponBuilt != null && _weaponBuilt.Root != null && _weaponBuilt.Root.transform.parent != null) _weaponBuilt.Root.transform.parent.SetParent(transform, false);
            if (Built != null && Built.Root != null) Destroy(Built.Root);
            Built = null;
            Hitboxes.Clear();
            if (Def.Model == null) { HaloLog.Warn("Characters: " + Def.Name + " has no model"); ReattachLatchers(); return; }
            var o = new HaloModelBuilder.Options { Permutations = new Dictionary<string, string>(Permutations, StringComparer.OrdinalIgnoreCase) };
            Built = HaloModelBuilder.Build(Def.Model, ModelHolder, o);
            if (Built == null) { ReattachLatchers(); return; }
            if (Built.Mesh != null)
            {
                HaloMaterials.SetChangeColor(Built.Mesh, ChangeColorA);
                if (Built.Renderer != null) Built.Renderer.updateWhenOffscreen = false;
            }

            _nodes = Built.Nodes;
            _skel = HaloBipeds.SkeletonOf(Def.Model, Built.Model);
            _skelToNode = new int[_skel.Count];
            for (int i = 0; i < _skel.Count; i++) _skelToNode[i] = i < _nodes.Length ? i : -1;
            _headNode = Def.HeadNode >= 0 && Def.HeadNode < _nodes.Length ? _nodes[Def.HeadNode] : Built.Node("bip01 head");
            _pelvisNode = Def.PelvisNode >= 0 && Def.PelvisNode < _nodes.Length ? _nodes[Def.PelvisNode] : Built.Node("bip01 pelvis");
            if (Animator == null)
            {
                HaloAnimGraph g = HaloBipeds.GraphOf(Def.Graph);
                if (g != null) Animator = new HaloUnitAnimator(_skel, g, _rng.Next());
            }

            if (_pelvisHeight < 0f) MeasurePelvis();
            BuildHitboxes();
            BuildShieldOverlay();
            if (_weaponBuilt != null) AttachWeaponModel();
            if (_camoHidden) SetCamoHidden(true);      // (a rebuilt model stays camouflaged)
            ReattachLatchers();
        }

        private struct HeldLatcher { public HaloUnit Unit; public Vector3 LocalPos; public Quaternion LocalRot; }
        private static readonly List<HeldLatcher> _heldLatchers = new List<HeldLatcher>(4);
        private int _heldFrom, _heldCount;

        /// <summary>A rebuild (the jackal's shield region going / coming back) replaces the model: infection forms latched to its body marker
        /// are lifted onto the unit's root meanwhile (their pose relative to the marker kept), so they are not destroyed with the old model.</summary>
        private void HoldLatchers()
        {
            _heldFrom = _heldLatchers.Count;
            _heldCount = 0;
            var all = HaloBipeds.All;
            for (int i = 0; i < all.Count; i++)
            {
                HaloUnit u = all[i];
                if (u == null || u == this || !u.Latched || u.LatchTarget != this) continue;
                Transform t = u.transform;
                _heldLatchers.Add(new HeldLatcher { Unit = u, LocalPos = t.localPosition, LocalRot = t.localRotation });
                t.SetParent(transform, true);
                _heldCount++;
            }
        }

        /// <summary>The held infection forms back onto the new model's body marker, where they were on the old one.</summary>
        private void ReattachLatchers()
        {
            if (_heldCount > 0)
            {
                Transform m = BodyMarker();
                int end = Mathf.Min(_heldLatchers.Count, _heldFrom + _heldCount);
                for (int i = _heldFrom; i < end; i++)
                {
                    HeldLatcher h = _heldLatchers[i];
                    if (h.Unit == null || !h.Unit.Latched || h.Unit.LatchTarget != this) continue;
                    if (m == transform) continue;      // (no body marker / pelvis on the new model: it stays on the root)
                    Transform t = h.Unit.transform;
                    t.SetParent(m, false);
                    t.localPosition = h.LocalPos;
                    t.localRotation = h.LocalRot;
                }

                if (_latchReattachLogged++ < 5) HaloLog.Info("Characters: " + Def.Name + "'s model was rebuilt: " + _heldCount + " infection form(s) kept latched");
            }

            if (_heldFrom < _heldLatchers.Count) _heldLatchers.RemoveRange(_heldFrom, _heldLatchers.Count - _heldFrom);
            _heldCount = 0;
        }

        private static int _latchReattachLogged;
        private static int _camoLogged;
        /// <summary>The shield overlay's glow on a hidden camouflaged unit (ours: Halo draws a refraction we have no shader for).</summary>
        public static float CamoShimmer = 0.12f;

        /// <summary>A camouflaged unit stops being so (scripts; not used by Halo's AI): drawn again.</summary>
        public void SetCamouflage(bool on)
        {
            Camouflaged = on;
            if (!on && _camoHidden) SetCamoHidden(false);
        }

        // ------------------------------------------------------------------ the shield's glow

        private SkinnedMeshRenderer _shieldR;
        private MaterialPropertyBlock _shieldBlock;
        private Color _shieldPerp, _shieldPar;
        private static readonly int IdPerp = Shader.PropertyToID("_PerpColor"), IdPar = Shader.PropertyToID("_ParColor");

        /// <summary>
        /// The biped's modifier shader (elites: "shield plasma", the cyborg: "shield hit") drawn over the whole body while the shield takes hits:
        /// Halo drives it by the object's "recent shield damage" function (bipd A in); here by <see cref="ShieldFlash"/>.
        /// </summary>
        private void BuildShieldOverlay()
        {
            _shieldR = null;
            if (Def.ModifierShader == null || Def.MaxShield <= 0f || Built == null || Built.Renderer == null) return;
            Material m = HaloMaterials.Get(Def.ModifierShader);
            if (m == null || m == HaloMaterials.Fallback) return;
            SkinnedMeshRenderer src = Built.Renderer;
            var go = new GameObject("shield");
            go.transform.SetParent(src.transform.parent, false);
            var r = go.AddComponent<SkinnedMeshRenderer>();
            r.sharedMesh = src.sharedMesh;
            r.bones = src.bones;
            r.rootBone = src.rootBone;
            r.localBounds = src.localBounds;
            var mats = new Material[src.sharedMesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = m;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            r.enabled = false;
            _shieldPerp = m.HasProperty(IdPerp) ? m.GetColor(IdPerp) : Color.white;
            _shieldPar = m.HasProperty(IdPar) ? m.GetColor(IdPar) : Color.white;
            _shieldBlock = new MaterialPropertyBlock();
            _shieldR = r;
        }

        private void TickShieldGlow()
        {
            if (_shieldR == null) return;
            float glow = Mathf.Max(_shieldFlash, _camoHidden ? CamoShimmer : 0f);
            bool on = glow > 0.01f && Alive;
            if (_shieldR.enabled != on) _shieldR.enabled = on;
            if (!on) return;
            _shieldBlock.SetColor(IdPerp, _shieldPerp * glow);
            _shieldBlock.SetColor(IdPar, _shieldPar * glow);
            _shieldR.SetPropertyBlock(_shieldBlock);
        }

        /// <summary>
        /// Halo's region permutations: one random variant number per object among the numbered permutations (name suffix "-10" .. "-99", not
        /// flagged "cannot be chosen randomly", e.g. the marines' and crewmen's heads and helmets) or none (the base); every region takes the
        /// permutation with that number when it has one, else its base; numbers of 100 and up (Sgt. Johnson, the damaged torso) and "~" names
        /// (damaged states) are never drawn.
        /// </summary>
        private void ChoosePermutations()
        {
            Permutations.Clear();
            HaloModel m = HaloBipeds.ModelOf(Def.Model);
            if (m == null) return;
            var numbers = new List<int> { 0 };
            foreach (HaloModel.Region reg in m.Regions)
                foreach (HaloModel.Permutation p in reg.Permutations)
                {
                    int n = Number(p.Name);
                    if (n > 0 && n < 100 && (p.Flags & 1) == 0 && !p.Name.StartsWith("~") && !numbers.Contains(n)) numbers.Add(n);
                }

            int want = numbers[_rng.Next(numbers.Count)];
            foreach (HaloModel.Region reg in m.Regions)
            {
                HaloModel.Permutation pick = null, basePerm = null;
                foreach (HaloModel.Permutation p in reg.Permutations)
                {
                    if (p.Name.StartsWith("~")) continue;
                    int n = Number(p.Name);
                    if (want > 0 && n == want) { pick = p; break; }
                    if (basePerm == null && (n == 0 || p.Name == "__base")) basePerm = p;
                }

                if (pick == null) pick = basePerm;
                if (pick == null) foreach (HaloModel.Permutation p in reg.Permutations) if (!p.Name.StartsWith("~") && Number(p.Name) < 100) { pick = p; break; }
                if (pick != null) Permutations[reg.Name] = pick.Name;
            }
        }

        private static int Number(string name)
        {
            int dash = name.LastIndexOf('-');
            if (dash < 0 || dash == name.Length - 1) return 0;
            int n = 0;
            for (int i = dash + 1; i < name.Length; i++) { char c = name[i]; if (c < '0' || c > '9') return 0; n = n * 10 + (c - '0'); }
            return n;
        }

        /// <summary>Hit boxes: per collision-model node with a BSP, a box (its vertices' bounds in the node frame) on the model node of the same name.</summary>
        private void BuildHitboxes()
        {
            HaloCollisionDef c = Def.Coll;
            if (c == null || Built == null) return;
            if (_boxBody == null)
            {
                _boxBody = ModelHolder.gameObject.AddComponent<Rigidbody>();
                _boxBody.isKinematic = true;
                _boxBody.useGravity = false;
                _boxBody.interpolation = RigidbodyInterpolation.None;
            }

            for (int i = 0; i < c.Nodes.Count; i++)
            {
                HaloCollisionDef.Node cn = c.Nodes[i];
                if (cn.Bsps.Count == 0) continue;
                int node = Built.Model.NodeIndex(cn.Name);
                if (node < 0 || node >= _nodes.Length) continue;
                int perm = 0;
                if (cn.Region >= 0 && cn.Region < c.Regions.Count)
                {
                    HaloCollisionDef.Region reg = c.Regions[cn.Region];
                    string want;
                    if (_destroyedRegions.Contains(reg.Name)) { int d = reg.Permutations.FindIndex(x => x.StartsWith("~")); if (d >= 0) perm = d; }
                    else if (Permutations.TryGetValue(reg.Name, out want)) { int k = reg.Permutations.FindIndex(x => string.Equals(x, want, StringComparison.OrdinalIgnoreCase)); if (k >= 0) perm = k; }
                }

                if (perm >= cn.Bsps.Count) perm = 0;
                HaloCollisionDef.Box b = cn.Bsps[perm];
                if (b.Max.X < b.Min.X) continue;
                // one box per material of the node's BSP (Halo takes each surface's own material: the hunter's flesh between its armour
                // plates, the arm that is not all shield); the whole BSP's box when it has one material only
                if (b.Parts != null && b.Parts.Length > 1)
                    for (int k = 0; k < b.Parts.Length; k++) AddHitbox(c, cn, i, node, b.Parts[k].Min, b.Parts[k].Max, b.Parts[k].Material, " " + k);
                else AddHitbox(c, cn, i, node, b.Min, b.Max, b.Material, "");
            }
        }

        private void AddHitbox(HaloCollisionDef c, HaloCollisionDef.Node cn, int collNode, int node, HVec3 min, HVec3 max, int material, string suffix)
        {
            float s = HaloSpace.Unit;
            if (max.X < min.X) return;
            var go = new GameObject("hitbox " + cn.Name + suffix);
            go.layer = HaloLayers.Hitbox;
            go.transform.SetParent(_nodes[node], false);
            var box = go.AddComponent<BoxCollider>();
            Vector3 mn = new Vector3(min.X, min.Z, min.Y) * s, mx = new Vector3(max.X, max.Z, max.Y) * s;
            box.center = (mn + mx) * 0.5f;
            box.size = new Vector3(Mathf.Max(0.04f, mx.x - mn.x), Mathf.Max(0.04f, mx.y - mn.y), Mathf.Max(0.04f, mx.z - mn.z));
            HaloCollisionDef.Material mat = c.MaterialAt(material);
            Hitboxes.Add(new Hitbox
            {
                Box = box, Node = node, CollNode = collNode, Region = cn.Region, Material = material, Head = mat != null && mat.Head,
                AnimRegion = AnimRegionOf(cn.Name, mat != null && mat.Head),
            });
        }

        /// <summary>The unit damage table's body region of a node by its name.</summary>
        private static int AnimRegionOf(string node, bool head)
        {
            string n = node.ToLowerInvariant();
            if (head || n.Contains("head") || n.Contains("neck")) return 2;
            bool l = n.Contains(" l ") || n.Contains(" left") || n.EndsWith(" l"), r = n.Contains(" r ") || n.Contains(" right") || n.EndsWith(" r");
            if (l || r)
            {
                int side = l ? 3 : 7;
                if (n.Contains("hand") || n.Contains("claw") || n.Contains("finger")) return side + 1;
                if (n.Contains("foot") || n.Contains("toe") || n.Contains("horselink")) return side + 3;
                if (n.Contains("thigh") || n.Contains("calf") || n.Contains("knee") || n.Contains("leg")) return side + 2;
                if (n.Contains("arm") || n.Contains("clavicle") || n.Contains("shoulder")) return side;
            }

            if (n.Contains("spine1") || n.Contains("chest") || n.Contains("spine mid") || n.Contains("spine end")) return 1;
            return 0;
        }

        /// <summary>A region was shot off (flood arms / heads): its "~damaged" permutation, its hit box from the damaged BSP.</summary>
        private void DestroyRegion(HaloCollisionDef.Region reg)
        {
            if (!_destroyedRegions.Add(reg.Name)) return;
            HaloModel m = HaloBipeds.ModelOf(Def.Model);
            string dmg = null;
            if (m != null)
                foreach (HaloModel.Region r in m.Regions)
                    if (string.Equals(r.Name, reg.Name, StringComparison.OrdinalIgnoreCase))
                        foreach (HaloModel.Permutation p in r.Permutations) if (p.Name.StartsWith("~")) { dmg = p.Name; break; }
            if (reg.DestroyedEffect != null) HaloCombatFx.Play(reg.DestroyedEffect, Center, transform.rotation, null, this);
            if (dmg == null) return;
            Permutations[reg.Name] = dmg;
            BuildBody();
            HaloLog.Info("Characters: " + Def.Name + " lost its " + reg.Name + " (" + dmg + ")");
        }

        // ------------------------------------------------------------------ animation

        private bool _bodyDown;
        private float _animDt;
        private static int _viewFrame = -1;
        private static Vector3 _view;
        private static bool _hasView;

        /// <summary>Units farther than this from the view animate every 2nd frame, twice as far every 4th (ours: cost with many units).</summary>
        public static float AnimLodDistance = 35f;

        private void TickAnimation(float dt)
        {
            if (Animator == null || _nodes == null) return;
            // distant units: fewer pose updates (the time is carried, the animation keeps its pace)
            if (_viewFrame != Time.frameCount)
            {
                _viewFrame = Time.frameCount;
                Camera c = HaloFx.ViewCamera != null ? HaloFx.ViewCamera : Camera.main;
                _hasView = c != null;
                if (_hasView) _view = c.transform.position;
            }

            _animDt += dt;
            if (_hasView && AnimLodDistance > 0f && Animator.CustomPlaying == false)
            {
                float d2 = (transform.position - _view).sqrMagnitude, l = AnimLodDistance * AnimLodDistance;
                int every = d2 > 4f * l ? 4 : d2 > l ? 2 : 1;
                if (every > 1 && ((_frames + (GetInstanceID() & 3)) % every) != 0 && _animDt < 0.25f) return;
            }

            dt = _animDt;
            _animDt = 0f;
            HaloUnitAnimInput inp = AnimInput();
            Animator.Step(inp, dt);
            if (_destroyPending) { TickDestroyPending(); if (_bodyGone) return; }
            if (!Alive && !_bodyDown && Animator.DeathDone)
            {
                // the body hits the ground: the unit's material effects "impact" for the surface under it (Audio's HaloImpacts); biped
                // footsteps tags (globalsgrunt, lite, borg ...) have no impact kind (6 kinds: walk .. land): their "land" effect is the thud
                _bodyDown = true;
                Vector3 at = _pelvisNode != null ? _pelvisNode.position : transform.position;
                try { if (HaloImpacts.Play(UnitTag, HaloImpacts.Impact, at, -1, 1f) || HaloImpacts.Play(UnitTag, HaloFootsteps.Land, at, -1, 1f)) SfxBodyImpact++; } catch (System.Exception) { }
            }

            if (_exiting && (!Alive || Animator.CustomDone || !Animator.CustomPlaying)) { FinishExit(); Animator.Step(AnimInput(), 0.0001f); }
            AfterAnimation();
            WritePose();
            ApplyIk(dt);
        }

        /// <summary>The pose onto the node transforms (Halo standard rotations -> Unity: conjugate basis swap; translations (x, z, y) * 3.048).</summary>
        private void WritePose()
        {
            HaloPose p = Animator.Pose;
            float[] r = p.LRot, t = p.LTrans;
            float s = HaloSpace.Unit;
            int n = Mathf.Min(_skel.Count, _nodes.Length);
            for (int i = 0; i < n; i++)
            {
                Transform x = _nodes[i];
                if (x == null) continue;
                int q = i * 4, v = i * 3;
                x.localRotation = new Quaternion(-r[q], -r[q + 2], -r[q + 1], r[q + 3]);
                x.localPosition = new Vector3(t[v] * s, t[v + 2] * s, t[v + 1] * s);
            }
        }
    }
}

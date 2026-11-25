using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The multiplayer cyborg as the puppets need it, read from the match's own map: the biped "characters\cyborg_mp\cyborg_mp" (any biped named
    /// cyborg as a fallback) -> its gbxmodel (skeleton, the "right hand" / "left hand" markers) and its model_animations (the graph: seats, weapon
    /// classes, types, animations). Weapons' grip data (the "left hand cyborg" marker of their gbxmodel, where Halo IKs the left hand) is cached per tag.
    /// Built on the main thread when first asked in a map (a few ms; animations decode on first use). Player.log: "Desk: cyborg ..." once per map.
    /// </summary>
    public sealed class HaloCyborg
    {
        public readonly HaloMap Map;
        public readonly HaloTag Biped;
        public readonly HaloSkeleton Skel;
        public readonly HaloAnimGraph Graph;
        public readonly int[] NodeMap;
        public readonly int Pelvis, Spine, Spine1, Neck, Head, LClav, RClav, LUpper, RUpper, LFore, RFore, LHand, RHand, LThigh, RThigh, LCalf, RCalf, LFoot, RFoot;
        public readonly bool HasRightMarker, HasLeftMarker;
        public readonly HaloSkeleton.Marker RightMarker, LeftMarker, LeftFootMarker, RightFootMarker;
        /// <summary>Nodes of the upper body (spine and above): a base-type animation played as an event (throw-grenade) only moves these.</summary>
        public readonly bool[] Upper;

        private static HaloCyborg _current;
        private static HaloMap _failedFor;
        private readonly Dictionary<HaloTag, WeaponGrip> _grips = new Dictionary<HaloTag, WeaponGrip>();

        /// <summary>Where Halo IKs the left hand on a weapon ("left hand cyborg" of the weapon's gbxmodel, node 0's frame), in the model's own space (Halo units).</summary>
        public sealed class WeaponGrip
        {
            public bool HasLeft;
            public Vector3 LeftPos;      // Halo model space (x forward, y left, z up), units
            public Quaternion LeftRot;   // standard rotation in Halo axes, as Unity's struct (x, y, z, w)
            public string Label = "";
            /// <summary>The model's bounds (first level of detail; Halo model space, units): the first-person framing pins its rear-top corner.</summary>
            public bool HasBox;
            public Vector3 BoxMin, BoxMax;
            public HaloTag Model;
        }

        /// <summary>The cyborg of the current match's map (null outside a match or when the map has none).</summary>
        public static HaloCyborg Current
        {
            get
            {
                HaloMap m = HaloSession.Map;
                if (m == null) return _current;
                if (_current != null && _current.Map == m) return _current;
                if (_failedFor == m) return null;
                string why;
                HaloCyborg c = Load(m, out why);
                if (c == null) { _failedFor = m; HaloLog.Warn("Desk: no cyborg in " + m.Name + " (" + why + "): avatars keep the modelled hold"); return null; }
                _current = c;
                return c;
            }
        }

        /// <summary>Lab: use this map's cyborg.</summary>
        public static void Use(HaloCyborg c) { _current = c; }

        public static HaloCyborg Load(HaloMap map, out string why)
        {
            why = null;
            try
            {
                HaloTag bipd = map.Find("bipd", @"characters\cyborg_mp\cyborg_mp");
                if (bipd == null)
                    foreach (HaloTag t in map.OfClass("bipd")) if (t.Name.IndexOf("cyborg", StringComparison.OrdinalIgnoreCase) >= 0) { bipd = t; break; }
                if (bipd == null) { why = "no cyborg biped"; return null; }
                HaloTag model = bipd.Root.Ref(40), antr = bipd.Root.Ref(56);
                if (model == null || antr == null) { why = "the biped has no model or animation graph"; return null; }
                var c = new HaloCyborg(map, bipd, new HaloModel(model), new HaloAnimGraph(antr));
                if (c.RHand < 0 || c.LHand < 0 || c.Pelvis < 0 || c.Head < 0) { why = "the skeleton lacks the biped's hands / pelvis / head"; return null; }
                HaloAnimGraph.Seat stand = c.Graph.FindSeat("stand");
                HaloLog.Info("Desk: cyborg '" + bipd.Name + "' of " + map.Name + ": " + c.Skel.Count + " nodes, " + c.Graph.Animations.Length + " animations (" + c.Graph.Compressed + " compressed), " +
                             c.Graph.Seats.Count + " seats, stand classes " + (stand != null ? stand.Weapons.Count : 0) + ", hand markers " + c.HasRightMarker + "/" + c.HasLeftMarker);
                return c;
            }
            catch (Exception e)
            {
                why = e.GetType().Name + ": " + e.Message;
                return null;
            }
        }

        private HaloCyborg(HaloMap map, HaloTag biped, HaloModel model, HaloAnimGraph graph)
        {
            Map = map;
            Biped = biped;
            Skel = new HaloSkeleton(model);
            Graph = graph;
            NodeMap = Skel.MapFrom(graph);
            Pelvis = Skel.Node("bip01 pelvis"); Spine = Skel.Node("bip01 spine"); Spine1 = Skel.Node("bip01 spine1"); Neck = Skel.Node("bip01 neck"); Head = Skel.Node("bip01 head");
            LClav = Skel.Node("bip01 l clavicle"); RClav = Skel.Node("bip01 r clavicle");
            LUpper = Skel.Node("bip01 l upperarm"); RUpper = Skel.Node("bip01 r upperarm");
            LFore = Skel.Node("bip01 l forearm"); RFore = Skel.Node("bip01 r forearm");
            LHand = Skel.Node("bip01 l hand"); RHand = Skel.Node("bip01 r hand");
            LThigh = Skel.Node("bip01 l thigh"); RThigh = Skel.Node("bip01 r thigh");
            LCalf = Skel.Node("bip01 l calf"); RCalf = Skel.Node("bip01 r calf");
            LFoot = Skel.Node("bip01 l foot"); RFoot = Skel.Node("bip01 r foot");
            HaloSkeleton.Marker m;
            HasRightMarker = Skel.FindMarker("right hand", out m); RightMarker = m;
            HasLeftMarker = Skel.FindMarker("left hand", out m); LeftMarker = m;
            Skel.FindMarker("left foot", out m); LeftFootMarker = m;
            Skel.FindMarker("right foot", out m); RightFootMarker = m;
            Upper = new bool[Skel.Count];
            for (int i = 0; i < Skel.Count; i++)
            {
                // spine and everything under it (Halo lists parents before children)
                int p = i;
                while (p >= 0 && p != Spine) p = Skel.Parent[p];
                Upper[i] = p == Spine;
            }
        }

        /// <summary>The weapon's label ("ar", "hp", ...) and where Halo puts the left hand on it.</summary>
        public WeaponGrip Grip(HaloTag weapon)
        {
            if (weapon == null) return null;
            WeaponGrip g;
            if (_grips.TryGetValue(weapon, out g)) return g;
            g = new WeaponGrip();
            try
            {
                TagReader r = weapon.Root;
                if (weapon.Is("weap")) g.Label = r.Str32(780);
                HaloTag model = r.Ref(40);
                g.Model = model;
                if (model != null)
                {
                    var hm = new HaloModel(model);
                    Bounds(hm, g);
                    HaloModel.Marker mk = hm.FindMarker("left hand cyborg") ?? hm.FindMarker("left hand");
                    if (mk != null && mk.Instances.Count > 0)
                    {
                        HaloModel.MarkerInstance x = mk.Instances[0];
                        // the marker's node in the model's rest pose (weapons: node 0 at the origin; the shotgun's pump is node 1)
                        var sk = new HaloSkeleton(hm);
                        var pose = new HaloPose(sk);
                        pose.ToModel();
                        int node = x.Node >= 0 && x.Node < sk.Count ? x.Node : 0;
                        float px, py, pz;
                        pose.Point(node, x.Translation.X, x.Translation.Y, x.Translation.Z, out px, out py, out pz);
                        var q = new float[4];
                        HQ.Std(x.Qi, x.Qj, x.Qk, x.Qw, q, 0);
                        var o = new float[4];
                        HQ.Mul(pose.MRot, node * 4, q, 0, o, 0);
                        g.HasLeft = true;
                        g.LeftPos = new Vector3(px, py, pz);
                        g.LeftRot = new Quaternion(o[0], o[1], o[2], o[3]);
                    }
                }
            }
            catch (Exception e)
            {
                HaloLog.Warn("Desk: grip of " + weapon.Name + ": " + e.Message);
            }

            _grips[weapon] = g;
            return g;
        }

        private static void Bounds(HaloModel hm, WeaponGrip g)
        {
            if (hm.Geometries.Count == 0) return;
            float x0 = 1e9f, y0 = 1e9f, z0 = 1e9f, x1 = -1e9f, y1 = -1e9f, z1 = -1e9f;
            HaloModel.Geometry geo = hm.Geometries[0];
            for (int p = 0; p < geo.Parts.Count; p++)
            {
                HVec3[] v = hm.Read(geo.Parts[p]).Position;
                for (int i = 0; i < v.Length; i++)
                {
                    if (v[i].X < x0) x0 = v[i].X; if (v[i].X > x1) x1 = v[i].X;
                    if (v[i].Y < y0) y0 = v[i].Y; if (v[i].Y > y1) y1 = v[i].Y;
                    if (v[i].Z < z0) z0 = v[i].Z; if (v[i].Z > z1) z1 = v[i].Z;
                }
            }

            if (x1 < x0) return;
            g.HasBox = true;
            g.BoxMin = new Vector3(x0, y0, z0);
            g.BoxMax = new Vector3(x1, y1, z1);
        }

        // ------------------------------------------------------------------ Halo model space -> the puppet / Unity frames

        /// <summary>Halo model space (x forward, y left, z up; units) to the puppet frame (x right, y up, z forward; metres).</summary>
        public static Vector3 P(float x, float y, float z) { return new Vector3(-y * HaloSpace.Unit, z * HaloSpace.Unit, x * HaloSpace.Unit); }
        /// <summary>A Halo model-space direction in the puppet frame (unit length kept).</summary>
        public static Vector3 D(float x, float y, float z) { return new Vector3(-y, z, x); }

        /// <summary>
        /// The rotation of a model HaloModelBuilder built (its local x = Halo forward, y = up, z = Halo left) so that its barrel points along
        /// <paramref name="forward"/> with <paramref name="up"/> up (world or any frame).
        /// </summary>
        public static Quaternion ModelRotation(Vector3 forward, Vector3 up)
        {
            Vector3 left = Vector3.Cross(forward, up);
            if (left.sqrMagnitude < 1e-8f) return Quaternion.LookRotation(forward.sqrMagnitude > 1e-8f ? forward : Vector3.forward) * ModelFromGun;
            return Quaternion.LookRotation(left, up);
        }

        /// <summary>From the gun frame (+z barrel, +y up) to a HaloModelBuilder model's frame (+x barrel): gunRot * this = the model's rotation.</summary>
        public static readonly Quaternion ModelFromGun = Quaternion.Euler(0f, -90f, 0f);
    }
}

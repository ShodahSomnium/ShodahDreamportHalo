using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A unit's collision model (model_collision_geometry, "coll") as the damage code needs it: body and shield vitality, the shield's material
    /// type, stun / recharge, the damage effects (body damaged / depleted / destroyed, shield damaged / depleted / recharging), the materials
    /// (Halo material type, the "head" flag, shield and body damage multipliers, shield leak), the regions (damage threshold, destroyed effect,
    /// flags, their permutation names) and the nodes with their collision BSPs (one per permutation of the node's region: here the bounds of
    /// each BSP's vertices in the node's own frame and the material most of its surfaces use) - the hit boxes are made from those.
    /// Layout: DevTools/reference/layouts.txt (ModelCollisionGeometry*). Unity-free.
    /// </summary>
    public sealed class HaloCollisionDef
    {
        public sealed class Material
        {
            public string Name;
            public bool Head;
            public int Type;               // Halo MaterialType (11 grunt, 14 elite, 15 jackal, 16 jackal energy shield, 21 cyborg armor, 23 human armor, 24 human skin ...)
            public float ShieldLeak, ShieldMultiplier, BodyMultiplier;
        }

        public sealed class Region
        {
            public string Name;
            public uint Flags;
            public float DamageThreshold;  // fraction of the body vitality this region takes before it is destroyed (0 = never)
            public HaloTag DestroyedEffect;
            public readonly List<string> Permutations = new List<string>();
            public bool LivesUntilObjectDies => (Flags & 1) != 0;
            public bool ForcesObjectToDie => (Flags & 2) != 0;
            public bool DiesWhenObjectDies => (Flags & 4) != 0;
            public bool DiesWhenObjectIsDamaged => (Flags & 8) != 0;
            public bool DisappearsWhenShieldIsOff => (Flags & 16) != 0;
            public bool InhibitsMelee => (Flags & 32) != 0;
            public bool InhibitsWeaponAttack => (Flags & 64) != 0;
            public bool InhibitsWalking => (Flags & 128) != 0;
            public bool ForcesDropWeapon => (Flags & 256) != 0;
            public bool CausesHeadMaimedScream => (Flags & 512) != 0;
        }

        /// <summary>One collision BSP of a node: the bounds of its vertices (node frame, Halo units) and its main material (-1 none).</summary>
        public struct Box
        {
            public HVec3 Min, Max;
            public int Material;
            public int Surfaces;
            /// <summary>The same BSP split by surface material: per material the bounds of the vertices of its surfaces (hunter: the arm's
            /// "shield" plates and its "body" flesh, the back's "skin"); null when the BSP has no surfaces.</summary>
            public Part[] Parts;
        }

        /// <summary>One material's share of a collision BSP: the bounds of its surfaces' vertices (node frame, Halo units).</summary>
        public struct Part
        {
            public HVec3 Min, Max;
            public int Material;
            public int Surfaces;
        }

        public sealed class Node
        {
            public string Name;
            public int Region, Parent;
            public readonly List<Box> Bsps = new List<Box>();
        }

        public readonly HaloTag Tag;
        public readonly uint Flags;
        public readonly float MaximumBody, MaximumShield, BodySystemShock, FriendlyDamageResistance;
        public readonly float BodyDamagedThreshold, BodyDestroyedThreshold, AreaDamageEffectThreshold;
        public readonly HaloTag LocalizedDamageEffect, AreaDamageEffect, BodyDamagedEffect, BodyDepletedEffect, BodyDestroyedEffect;
        public readonly int ShieldMaterialType;
        public readonly float ShieldFailureThreshold, FailingShieldLeak, MinimumStunDamage, StunTime, RechargeTime, ShieldDamagedThreshold, ShieldRechargeRate;
        public readonly HaloTag ShieldDamagedEffect, ShieldDepletedEffect, ShieldRechargingEffect;
        public readonly List<Material> Materials = new List<Material>();
        public readonly List<Region> Regions = new List<Region>();
        public readonly List<Node> Nodes = new List<Node>();

        public HaloCollisionDef(HaloTag coll)
        {
            Tag = coll;
            TagReader c = coll.Root;
            Flags = c.U32(0);
            MaximumBody = c.F32(8);
            BodySystemShock = c.F32(12);
            FriendlyDamageResistance = c.F32(68);
            LocalizedDamageEffect = c.Ref(112);
            AreaDamageEffectThreshold = c.F32(128);
            AreaDamageEffect = c.Ref(132);
            BodyDamagedThreshold = c.F32(148);
            BodyDamagedEffect = c.Ref(152);
            BodyDepletedEffect = c.Ref(168);
            BodyDestroyedThreshold = c.F32(184);
            BodyDestroyedEffect = c.Ref(188);
            MaximumShield = c.F32(204);
            ShieldMaterialType = c.U16(210);
            ShieldFailureThreshold = c.F32(240);
            FailingShieldLeak = c.F32(244);
            MinimumStunDamage = c.F32(264);
            StunTime = c.F32(268);
            RechargeTime = c.F32(272);
            ShieldDamagedThreshold = c.F32(388);
            ShieldDamagedEffect = c.Ref(392);
            ShieldDepletedEffect = c.Ref(408);
            ShieldRechargingEffect = c.Ref(424);
            ShieldRechargeRate = c.F32(448);
            foreach (TagReader m in c.Block(564, 72))
                Materials.Add(new Material { Name = m.Str32(0), Head = (m.U32(32) & 1) != 0, Type = m.U16(36), ShieldLeak = m.F32(40), ShieldMultiplier = m.F32(44), BodyMultiplier = m.F32(60) });
            foreach (TagReader g in c.Block(576, 84))
            {
                var r = new Region { Name = g.Str32(0), Flags = g.U32(32), DamageThreshold = g.F32(40), DestroyedEffect = g.Ref(56) };
                foreach (TagReader p in g.Block(72, 32)) r.Permutations.Add(p.Str32(0));
                Regions.Add(r);
            }

            var counts = new Dictionary<int, int>();
            foreach (TagReader n in c.Block(652, 64))
            {
                var node = new Node { Name = n.Str32(0), Region = n.I16(32), Parent = n.I16(34) };
                foreach (TagReader b in n.Block(52, 96))
                {
                    var box = new Box { Material = -1 };
                    float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
                    int nv = b.Count(84);
                    if (nv > 0 && nv < 100000)
                        foreach (TagReader v in b.Block(84, 16))
                        {
                            float x = v.F32(0), y = v.F32(4), z = v.F32(8);
                            if (x < x0) x0 = x; if (x > x1) x1 = x;
                            if (y < y0) y0 = y; if (y > y1) y1 = y;
                            if (z < z0) z0 = z; if (z > z1) z1 = z;
                        }

                    if (x1 >= x0) { box.Min = new HVec3(x0, y0, z0); box.Max = new HVec3(x1, y1, z1); }
                    counts.Clear();
                    int ns = b.Count(60);
                    if (ns > 0 && ns < 100000)
                        foreach (TagReader s in b.Block(60, 12))
                        {
                            int mi = s.I16(10);
                            int k;
                            counts.TryGetValue(mi, out k);
                            counts[mi] = k + 1;
                        }

                    int best = 0;
                    foreach (KeyValuePair<int, int> kv in counts) if (kv.Value > best && kv.Key >= 0 && kv.Key < Materials.Count) { best = kv.Value; box.Material = kv.Key; }
                    box.Surfaces = ns;
                    box.Parts = MaterialParts(b, ns, nv);
                    node.Bsps.Add(box);
                }

                Nodes.Add(node);
            }
        }

        /// <summary>
        /// Splits one collision BSP by material: each surface's vertices are found by walking its edge ring (an edge whose left surface is
        /// this one continues with its forward edge from its start vertex, else with its reverse edge from its end vertex) and grow the
        /// bounds of the surface's material. Null when the BSP has no surfaces / edges / vertices or is malformed.
        /// </summary>
        private Part[] MaterialParts(TagReader bsp, int ns, int nv)
        {
            int ne = bsp.Count(72);
            if (ns <= 0 || ns >= 100000 || nv <= 0 || nv >= 100000 || ne <= 0 || ne >= 400000) return null;
            var vx = new float[nv * 3];
            int i = 0;
            foreach (TagReader v in bsp.Block(84, 16)) { if (i >= nv) break; vx[i * 3] = v.F32(0); vx[i * 3 + 1] = v.F32(4); vx[i * 3 + 2] = v.F32(8); i++; }
            var edges = new int[ne * 6];
            i = 0;
            foreach (TagReader e in bsp.Block(72, 24))
            {
                if (i >= ne) break;
                for (int k = 0; k < 6; k++) edges[i * 6 + k] = (int)e.U32(k * 4);
                i++;
            }

            var parts = new List<Part>();
            int s = 0;
            foreach (TagReader sf in bsp.Block(60, 12))
            {
                int mat = sf.I16(10);
                int first = (int)sf.U32(4);
                int slot = -1;
                for (int k = 0; k < parts.Count; k++) if (parts[k].Material == mat) { slot = k; break; }
                if (slot < 0)
                {
                    parts.Add(new Part { Material = mat, Min = new HVec3(float.MaxValue, float.MaxValue, float.MaxValue), Max = new HVec3(float.MinValue, float.MinValue, float.MinValue) });
                    slot = parts.Count - 1;
                }

                Part p = parts[slot];
                p.Surfaces++;
                int e = first, guard = 0;
                while (e >= 0 && e < ne && guard++ < 64)
                {
                    bool left = edges[e * 6 + 4] == s;
                    int vi = left ? edges[e * 6] : edges[e * 6 + 1];
                    if (vi >= 0 && vi < nv)
                    {
                        float x = vx[vi * 3], y = vx[vi * 3 + 1], z = vx[vi * 3 + 2];
                        if (x < p.Min.X) p.Min.X = x; if (x > p.Max.X) p.Max.X = x;
                        if (y < p.Min.Y) p.Min.Y = y; if (y > p.Max.Y) p.Max.Y = y;
                        if (z < p.Min.Z) p.Min.Z = z; if (z > p.Max.Z) p.Max.Z = z;
                    }

                    e = left ? edges[e * 6 + 2] : edges[e * 6 + 3];
                    if (e == first) break;
                }

                parts[slot] = p;
                s++;
            }

            for (int k = parts.Count - 1; k >= 0; k--) if (parts[k].Max.X < parts[k].Min.X) parts.RemoveAt(k);
            return parts.Count > 0 ? parts.ToArray() : null;
        }

        public bool TakesShieldDamageForChildren => (Flags & 1) != 0;
        public bool OnlyDamagedByExplosives => (Flags & 32) != 0;

        /// <summary>The material type of the shield (while it is up the hit's effects and the damage modifier are for this type).</summary>
        public Material MaterialAt(int index) { return index >= 0 && index < Materials.Count ? Materials[index] : null; }

        /// <summary>Seconds for the shield to refill from empty (the coll's recharge time, else from the per-tick rate; 4 s when it says nothing).</summary>
        public float FullRechargeSeconds => RechargeTime > 0f ? RechargeTime : ShieldRechargeRate > 0f ? 1f / (ShieldRechargeRate * 30f) : 4f;

        public int RegionIndex(string name)
        {
            for (int i = 0; i < Regions.Count; i++) if (string.Equals(Regions[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }
    }
}

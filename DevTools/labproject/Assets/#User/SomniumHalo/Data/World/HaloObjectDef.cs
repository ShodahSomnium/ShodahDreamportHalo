using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The parts of an object definition (any obje subclass: scenery, machines, controls, light fixtures, sound scenery, vehicles,
    /// weapons, ...) the world needs to place it: object type, model (+40), animation graph (+56), collision model (+112), physics
    /// (+128), modifier shader (+144), attachments (+320), change colours (+356: per colour lower / upper bound and weighted
    /// permutations). Layout: DevTools/reference/layouts.txt (struct Object). Unity-free.
    /// </summary>
    public sealed class HaloObjectDef
    {
        public sealed class ChangeColor
        {
            public int DarkenBy, ScaleBy; public uint Flags; public HVec3 Lower, Upper;
            public readonly List<float[]> Permutations = new List<float[]>(); // weight, lower rgb, upper rgb
        }

        public readonly HaloTag Tag, Model, AnimationGraph, CollisionModel, Physics, ModifierShader;
        /// <summary>ObjectType: 0 biped, 1 vehicle, 2 weapon, 3 equipment, 4 garbage, 5 projectile, 6 scenery, 7 machine, 8 control,
        /// 9 light fixture, 10 placeholder, 11 sound scenery.</summary>
        public readonly int Type;
        public readonly uint Flags;
        public readonly float BoundingRadius;
        public readonly HVec3 BoundingOffset, OriginOffset;
        public readonly List<ChangeColor> ChangeColors = new List<ChangeColor>();

        public HaloObjectDef(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            Type = r.U16(0); Flags = r.U16(2); BoundingRadius = r.F32(4); BoundingOffset = r.Vec3(8); OriginOffset = r.Vec3(20);
            Model = r.Ref(40); AnimationGraph = r.Ref(56); CollisionModel = r.Ref(112); Physics = r.Ref(128); ModifierShader = r.Ref(144);
            foreach (var c in r.Block(356, 44))
            {
                var cc = new ChangeColor { DarkenBy = c.U16(0), ScaleBy = c.U16(2), Flags = c.U32(4), Lower = c.Vec3(8), Upper = c.Vec3(20) };
                foreach (var p in c.Block(32, 28)) cc.Permutations.Add(new float[] { p.F32(0), p.F32(4), p.F32(8), p.F32(12), p.F32(16), p.F32(20), p.F32(24) });
                ChangeColors.Add(cc);
            }
        }

        /// <summary>A change colour as the game picks it for a placed object without a function: the first permutation's lower bound
        /// (deterministic per <paramref name="seed"/> among weighted permutations), else the colour's lower bound. Null when none.</summary>
        public float[] PickColor(int index, int seed)
        {
            if (index < 0 || index >= ChangeColors.Count) return null;
            var c = ChangeColors[index];
            if (c.Permutations.Count == 0) return new[] { c.Lower.X, c.Lower.Y, c.Lower.Z };
            float total = 0f;
            foreach (var p in c.Permutations) total += Math.Max(0f, p[0]);
            float pick = total > 0f ? (float)((uint)(seed * 2654435761u) % 10000u) / 10000f * total : 0f;
            foreach (var p in c.Permutations)
            {
                pick -= Math.Max(0f, p[0]);
                if (pick <= 0f) return new[] { p[1], p[2], p[3] };
            }
            var l = c.Permutations[c.Permutations.Count - 1];
            return new[] { l[1], l[2], l[3] };
        }
    }

    /// <summary>
    /// A collision model (coll): materials (material type per index) and per node the collision BSPs, in the node's own space (Halo
    /// units). Regions / permutations are left out (MP scenery uses the base). Layout: struct ModelCollisionGeometry. Unity-free.
    /// </summary>
    public sealed class HaloCollisionModel
    {
        public sealed class Node { public string Name; public int Region, Parent; public readonly List<HaloCollisionBsp> Bsps = new List<HaloCollisionBsp>(); }

        public readonly HaloTag Tag;
        public readonly List<int> MaterialTypes = new List<int>();
        public readonly List<string> MaterialNames = new List<string>();
        public readonly List<Node> Nodes = new List<Node>();

        public HaloCollisionModel(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            foreach (var m in r.Block(564, 72)) { MaterialNames.Add(m.Str32(0)); MaterialTypes.Add(m.U16(36)); }
            foreach (var n in r.Block(652, 64))
            {
                var node = new Node { Name = n.Str32(0), Region = n.I16(32), Parent = n.I16(34) };
                foreach (var b in n.Block(52, 96)) node.Bsps.Add(new HaloCollisionBsp(b));
                Nodes.Add(node);
            }
        }

        public int MaterialType(int material) { return material >= 0 && material < MaterialTypes.Count ? MaterialTypes[material] : -1; }
    }
}

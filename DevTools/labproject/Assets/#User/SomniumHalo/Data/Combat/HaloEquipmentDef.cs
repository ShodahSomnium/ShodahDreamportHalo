using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Equipment (eqip): powerups (over shield, active camouflage, health pack, double speed, full-spectrum vision), grenade pickups (frag,
    /// plasma) and weapon ammunition boxes (powerup type none: the magazine objects of a weapon point at them). Unity-free.
    /// </summary>
    public sealed class HaloEquipmentDef
    {
        public enum Powerup { None = 0, DoubleSpeed = 1, OverShield = 2, ActiveCamouflage = 3, FullSpectrumVision = 4, Health = 5, Grenade = 6 }

        public readonly HaloTag Tag;
        public readonly HaloTag Model, PickupSound, CollisionSound;
        public readonly Powerup Kind;
        public readonly int GrenadeType;          // 0 frag, 1 plasma
        public readonly float PowerupTime;        // seconds
        public readonly int PickupTextIndex;
        public string Name;                       // "over shield" ..., from the HUD item strings (HaloCombatCatalog)

        public HaloEquipmentDef(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            Model = r.Ref(40);
            PickupTextIndex = r.I16(384);
            CollisionSound = r.Ref(600);
            Kind = (Powerup)r.U16(776);
            GrenadeType = r.U16(778);
            PowerupTime = r.F32(780);
            PickupSound = r.Ref(784);
        }

        public override string ToString() => Tag.Name + " (" + Kind + ")";
    }

    /// <summary>An item collection (itmc): weighted items (weapons or equipment) and the default respawn time. Unity-free.</summary>
    public sealed class HaloItemCollection
    {
        public readonly HaloTag Tag;
        public readonly List<HaloTag> Items = new List<HaloTag>();
        public readonly List<float> Weights = new List<float>();
        public readonly int DefaultSpawnTime;     // seconds (0 = the game's default: 30 s for weapons in our reading)

        public HaloItemCollection(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            foreach (var p in r.Block(0, 84))
            {
                var t = p.Ref(36);
                if (t == null) continue;
                Items.Add(t);
                Weights.Add(Math.Max(0f, p.F32(32)));
            }

            DefaultSpawnTime = r.I16(12);
        }

        /// <summary>A weighted random item (null when empty).</summary>
        public HaloTag Pick(Random rng)
        {
            if (Items.Count == 0) return null;
            float sum = 0f;
            foreach (var w in Weights) sum += w;
            if (sum <= 0f) return Items[0];
            float x = (float)rng.NextDouble() * sum;
            for (int i = 0; i < Items.Count; i++) { x -= Weights[i]; if (x <= 0f) return Items[i]; }
            return Items[Items.Count - 1];
        }
    }
}

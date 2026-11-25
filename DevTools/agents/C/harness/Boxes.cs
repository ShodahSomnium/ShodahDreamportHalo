using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Boxes(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var t = m.OfClass("bipd").First(x => x.Name.EndsWith(a[2]));
        var d = HaloCharacterDef.From(t);
        var c = d.Coll;
        Console.WriteLine($"{t.Name}: coll {c.Tag.Name} body {c.MaximumBody} shield {c.MaximumShield} type {c.ShieldMaterialType} stun {c.StunTime} recharge {c.RechargeTime}/{c.ShieldRechargeRate} full {c.FullRechargeSeconds:0.0}s damaged {c.BodyDamagedThreshold} {c.BodyDamagedEffect?.Name} depleted {c.BodyDepletedEffect?.Name} destroyed {c.BodyDestroyedThreshold} {c.BodyDestroyedEffect?.Name} shield fx {c.ShieldDamagedEffect?.Name}/{c.ShieldDepletedEffect?.Name}/{c.ShieldRechargingEffect?.Name} friendly {c.FriendlyDamageResistance} flags {c.Flags:X}");
        foreach (var n in c.Nodes)
            for (int i = 0; i < n.Bsps.Count; i++)
            {
                var b = n.Bsps[i];
                if (b.Parts != null) foreach (var pp in b.Parts) Console.WriteLine($"      part mat {pp.Material} ({c.MaterialAt(pp.Material)?.Name}) surf {pp.Surfaces} min {pp.Min} max {pp.Max}");
                Console.WriteLine($"  {n.Name,-24} r{n.Region} bsp{i} min {b.Min} max {b.Max} size ({(b.Max.X - b.Min.X) * 3.048:0.00},{(b.Max.Y - b.Min.Y) * 3.048:0.00},{(b.Max.Z - b.Min.Z) * 3.048:0.00}) m mat {b.Material} ({c.MaterialAt(b.Material)?.Name}) surf {b.Surfaces}");
            }
        foreach (var r in c.Regions) Console.WriteLine($"  region {r.Name} flags {r.Flags:X} thr {r.DamageThreshold} fx {r.DestroyedEffect?.Name} perms {string.Join(",", r.Permutations)}");
        return 0;
    }
}

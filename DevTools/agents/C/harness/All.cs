using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    // all <maps...>: every bipd / actv: animator through all states, counts
    static int AllMaps(string[] a)
    {
        int fails = 0, units = 0;
        foreach (var name in a.Skip(1))
        {
            var m = new HaloMap(Maps + name + ".map");
            foreach (var t in m.OfClass("bipd").Concat(m.OfClass("actv")))
            {
                HaloCharacterDef d = null;
                try { d = HaloCharacterDef.From(t); } catch (Exception e) { Console.WriteLine($"{name} {t.Name}: def {e.Message}"); fails++; continue; }
                if (d == null || d.Model == null || d.Graph == null) { if (t.ClassName == "bipd") Console.WriteLine($"{name} {t.Name}: no model/graph"); continue; }
                units++;
                try
                {
                    var sk = new HaloSkeleton(new HaloModel(d.Model));
                    var g = new HaloAnimGraph(d.Graph);
                    var an = new HaloUnitAnimator(sk, g, 3);
                    string label = d.Weapon != null && d.Weapon.Is("weap") ? d.Weapon.Root.Str32(780) : null;
                    var inp = new HaloUnitAnimInput { Seat = "stand", WeaponLabel = label, Aiming = true, Looking = true, AimYaw = 0.3f, AimPitch = 0.2f };
                    int bad = 0;
                    string[] seats = { "stand", "crouch", "alert", "flee" };
                    foreach (var s in seats)
                    {
                        inp.Seat = s;
                        for (int k = 0; k < 20; k++) { inp.MoveX = k < 10 ? an.NaturalSpeed : 0; inp.MoveY = k % 3 == 0 ? 1 : 0; an.Step(inp, 1 / 30f); }
                    }
                    inp.Seat = "stand";
                    an.Fire(); an.Step(inp, 0.1f); an.Reload(); for (int k = 0; k < 40; k++) an.Step(inp, 1 / 30f);
                    an.Throw(); for (int k = 0; k < 40; k++) an.Step(inp, 1 / 30f);
                    an.Melee(); an.Dive(2, true); an.Ping(true, 1, 2); for (int k = 0; k < 40; k++) an.Step(inp, 1 / 30f);
                    an.Kill(false, 3, 2); for (int k = 0; k < 200; k++) an.Step(inp, 1 / 30f);
                    for (int i = 0; i < an.Pose.LRot.Length; i++) if (float.IsNaN(an.Pose.LRot[i])) bad++;
                    int undecodable = g.Animations.Count(x => !x.Decode());
                    Console.WriteLine($"{name} {t.Name}: natural {an.NaturalSpeed * 3.048:0.00} m/s death {an.DeathName} done {an.DeathDone} nan {bad} undecodable {undecodable}/{g.Animations.Length} weapon {label}");
                    if (bad > 0) fails++;
                }
                catch (Exception e) { Console.WriteLine($"{name} {t.Name}: EXCEPTION {e}"); fails++; }
            }
        }
        Console.WriteLine($"units {units} fails {fails}");
        return fails;
    }
}

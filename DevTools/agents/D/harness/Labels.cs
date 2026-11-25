using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Labels(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("weap")) { var r = t.Root; Console.WriteLine($"weap {t.Name,-50} label '{r.Str32(780)}' antr {r.Ref(56)?.Name} model {r.Ref(40)?.Name}"); }
        foreach (var t in m.Tags.Where(x => x.ClassName == "vehi" || x.ClassName == "bipd"))
        {
            var r = t.Root;
            Console.WriteLine($"{t.ClassName} {t.Name} model {r.Ref(40)?.Name} antr {r.Ref(56)?.Name}");
            // unit block starts after object (380)
            foreach (var s in r.Block(740, 284)) Console.WriteLine($"   seat '{s.Str32(4)}' marker '{s.Str32(36)}' flags {s.U32(0):X}");
        }
        return 0;
    }
}
static partial class P
{
    static int Markers(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("mod2"))
        {
            if (!t.Name.Contains(a[2])) continue;
            var model = new HaloModel(t);
            Console.WriteLine($"{t.Name}: nodes {model.Nodes.Count}");
            for (int i = 0; i < model.Nodes.Count; i++) { var n = model.Nodes[i]; Console.WriteLine($"   node {i} {n.Name} parent {n.Parent} t {n.Translation} q ({n.Qi:0.###},{n.Qj:0.###},{n.Qk:0.###},{n.Qw:0.###}) d {n.DistanceFromParent:0.###}"); }
            foreach (var mk in model.Markers) foreach (var x in mk.Instances) Console.WriteLine($"   marker '{mk.Name}' node {x.Node} t {x.Translation} q ({x.Qi:0.###},{x.Qj:0.###},{x.Qk:0.###},{x.Qw:0.###}) r{x.Region} p{x.Permutation}");
        }
        var tag = m.Tags.First(x => x.ClassName == "antr" && x.Name.Contains("cyborg"));
        foreach (var u in tag.Root.Block(12, 100))
        {
            foreach (var ik in u.Block(76, 64)) Console.WriteLine($" seat {u.Str32(0)} ik '{ik.Str32(0)}' -> '{ik.Str32(32)}'");
            foreach (var w in u.Block(88, 188)) foreach (var ik in w.Block(164, 64)) Console.WriteLine($" seat {u.Str32(0)} weapon {w.Str32(0)} ik '{ik.Str32(0)}' -> '{ik.Str32(32)}'");
        }
        return 0;
    }
}

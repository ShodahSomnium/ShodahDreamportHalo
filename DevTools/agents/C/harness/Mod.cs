using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Mod(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("bipd"))
        {
            var d = HaloCharacterDef.From(t);
            Console.WriteLine($"{t.Name}: modifier shader {d.ModifierShader?.Name} ({d.ModifierShader?.ClassName}) creation {d.CreationEffect?.Name} functions {t.Root.Count(344)} a-in {t.Root.U16(264)} b {t.Root.U16(266)} c {t.Root.U16(268)} d {t.Root.U16(270)} attachments {t.Root.Count(320)}");
            foreach (var f in t.Root.Block(320, 72)) Console.WriteLine($"   attach {f.Ref(0)?.Name} marker '{f.Str32(16)}'");
        }
        return 0;
    }
}

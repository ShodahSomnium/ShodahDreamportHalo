using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int All(string[] a)
    {
        foreach (var f in Directory.GetFiles(Maps, "*.map"))
        {
            string n = Path.GetFileNameWithoutExtension(f);
            if (n == "bitmaps" || n == "sounds" || n == "ui" || n.Length <= 3) continue;
            var m = new HaloMap(f);
            var b = m.Find("bipd", @"characters\cyborg_mp\cyborg_mp");
            if (b == null) { Console.WriteLine(n + ": no cyborg_mp"); continue; }
            var g = new HaloAnimGraph(b.Root.Ref(56));
            int bad = 0; foreach (var an in g.Animations) if (!an.Decode()) bad++;
            var model = new HaloModel(b.Root.Ref(40));
            Console.WriteLine($"{n,-16} antr {b.Root.Ref(56).Name} anims {g.Animations.Length} compressed {g.Compressed} decode-failed {bad} seats {g.Seats.Count} nodes {g.NodeNames.Length} model nodes {model.Nodes.Count}");
        }
        return 0;
    }
}

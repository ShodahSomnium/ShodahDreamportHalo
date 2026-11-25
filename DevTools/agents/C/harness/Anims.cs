using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static int Anims(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var tag = m.OfClass("antr").First(x => x.Name.EndsWith(a[2]));
        var g = new HaloAnimGraph(tag);
        Console.WriteLine($"== {tag.Name}: {g.Animations.Length} anims");
        foreach (var s in g.Seats)
        {
            Console.Write($" seat '{s.Label}' yaw {s.RightYawPerFrame:0.000}/{s.LeftYawPerFrame:0.000} {s.RightFrames}/{s.LeftFrames} pitch {s.DownPitchPerFrame:0.000}/{s.UpPitchPerFrame:0.000} {s.DownFrames}/{s.UpFrames} own:");
            for (int i = 0; i < s.Anims.Length; i++) if (s.Anims[i] >= 0) Console.Write($" [{i}]{g.Get(s.Anims[i]).Name}");
            Console.WriteLine();
            foreach (var w in s.Weapons)
            {
                Console.Write($"   class '{w.Name}' yaw {w.RightYawPerFrame:0.000}/{w.LeftYawPerFrame:0.000} {w.RightFrames}/{w.LeftFrames} pitch {w.DownPitchPerFrame:0.000}/{w.UpPitchPerFrame:0.000} {w.DownFrames}/{w.UpFrames}:");
                for (int i = 0; i < w.Anims.Length; i++) if (w.Anims[i] >= 0) Console.Write($" [{i}]{g.Get(w.Anims[i]).Name}");
                Console.WriteLine();
                foreach (var t in w.Types)
                {
                    Console.Write($"     type '{t.Label}':");
                    for (int i = 0; i < t.Anims.Length; i++) if (t.Anims[i] >= 0) Console.Write($" [{i}]{g.Get(t.Anims[i]).Name}");
                    Console.WriteLine();
                }
            }
            if (a.Length < 4) break;
        }
        Console.Write(" damage:");
        for (int i = 0; i < g.UnitDamage.Length; i++) if (g.UnitDamage[i] >= 0) Console.Write($" [{i}]{g.Get(g.UnitDamage[i]).Name}");
        Console.WriteLine();
        if (a.Length > 3) for (int i = 0; i < g.Animations.Length; i++) { var x = g.Animations[i]; Console.WriteLine($"  {i} {x.Name} {x.Type} fr{x.Frames} info{x.FrameInfoType} loop{x.LoopFrame} next{x.Next} comp{x.Compressed}"); }
        return 0;
    }
}

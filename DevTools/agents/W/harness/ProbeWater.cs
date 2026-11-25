using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    static int Water(string[] a)
    {
        foreach (var name in (a.Length > 2 ? new[] { a[2] } : Mp))
        {
            var m = new HaloMap(Maps + name + ".map");
            var sc = new HaloScenario(m);
            var b = new HaloBsp(sc, 0);
            foreach (var s in b.Sections.Where(s => s.Shader != null && (s.Shader.ClassName == "swat" || s.Shader.ClassName == "sgla" || (a.Length > 1 && s.Shader.Name.Contains(a[1])))))
            {
                double x = 0, y = 0, z = 0; int n = s.VertexCount;
                for (int i = 0; i < n; i++) { x += s.Position[i * 3]; y += s.Position[i * 3 + 1]; z += s.Position[i * 3 + 2]; }
                Console.WriteLine($"{name} {s.Shader.ClassName} {s.Shader.Name} tris {s.Triangles.Length / 3} centre ({x / n:0.0},{y / n:0.0},{z / n:0.00}) fogplane {s.FogPlane}");
            }
        }
        return 0;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

// Reads Invader's tag definition JSONs (format reference only) and prints every struct's field offsets.
static class Defs
{
    class Def { public string Name, Type, Inherits; public int Size = -1; public int Width = 32; public JsonElement Fields; public bool HasFields; }
    static readonly Dictionary<string, Def> All = new Dictionary<string, Def>();
    static readonly Dictionary<string, int> Prim = new Dictionary<string, int> {
        {"int8",1},{"uint8",1},{"int16",2},{"uint16",2},{"Index",2},{"int32",4},{"uint32",4},{"float",4},{"Angle",4},{"Fraction",4},
        {"TagID",4},{"Pointer",4},{"ColorARGBInt",4},{"Point2DInt",4},{"Point2D",8},{"Vector2D",8},{"Euler2D",8},{"Point3D",12},{"Vector3D",12},
        {"Euler3D",12},{"Quaternion",16},{"Plane2D",12},{"Plane3D",16},{"ColorRGB",12},{"ColorARGB",16},{"TagString",32},{"String32",32},
        {"TagDependency",16},{"TagReflexive",12},{"TagDataOffset",20},{"Matrix",36},{"Rectangle2D",8},{"ScenarioScriptNodeValue",4},{"TagGroupFourCC",4},{"TagFourCC",4},{"ID",4},{"File",0},
        {"CompressedVector3D",4},{"CompressedFloat",2},{"CompressedVector2D",4}
    };

    public static void Run(string dir, string outFile)
    {
        foreach (var f in Directory.GetFiles(dir, "*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(f));
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var d = new Def { Name = e.GetProperty("name").GetString(), Type = e.GetProperty("type").GetString() };
                if (e.TryGetProperty("inherits", out var inh)) d.Inherits = inh.GetString();
                if (e.TryGetProperty("size", out var sz)) d.Size = sz.GetInt32();
                if (e.TryGetProperty("width", out var w)) d.Width = w.GetInt32();
                if (e.TryGetProperty("fields", out var fl) && d.Type == "struct") { d.Fields = fl.Clone(); d.HasFields = true; }
                All[d.Name] = d;
            }
        }
        var sb = new StringBuilder();
        foreach (var d in All.Values.Where(x => x.Type == "struct").OrderBy(x => x.Name))
        {
            int size = Dump(d, sb, 0, "", true);
            string check = d.Size >= 0 && d.Size != size ? $"  !!! SIZE MISMATCH declared {d.Size}" : "";
            sb.Insert(0, ""); sb.AppendLine($"== end {d.Name} computed {size}{check}");
            sb.AppendLine();
        }
        File.WriteAllText(outFile, sb.ToString());
        Console.WriteLine($"{All.Count} definitions -> {outFile}");
        foreach (var line in sb.ToString().Split('\n').Where(l => l.Contains("MISMATCH"))) Console.WriteLine(line);
    }

    static int SizeOf(string type, JsonElement field)
    {
        if (Prim.TryGetValue(type, out int p)) return p;
        if (All.TryGetValue(type, out var d))
        {
            if (d.Type == "enum") return 2;
            if (d.Type == "bitfield") return d.Width / 8;
            if (d.Type == "struct") return StructSize(d);
        }
        throw new Exception("unknown type " + type);
    }

    static readonly Dictionary<string, int> Sizes = new Dictionary<string, int>();
    static int StructSize(Def d)
    {
        if (Sizes.TryGetValue(d.Name, out int s)) return s;
        s = Dump(d, null, 0, "", false); Sizes[d.Name] = s; return s;
    }

    // returns the struct size; writes lines when sb != null
    static int Dump(Def d, StringBuilder sb, int baseOff, string indent, bool header)
    {
        int off = 0;
        if (header && sb != null) sb.AppendLine($"struct {d.Name}" + (d.Inherits != null ? $" : {d.Inherits}" : "") + (d.Size >= 0 ? $" (size {d.Size})" : ""));
        if (d.Inherits != null)
        {
            var b = All[d.Inherits];
            if (sb != null) off = Dump(b, sb, baseOff, indent, false); else off = StructSize(b);
        }
        if (!d.HasFields) return off;
        foreach (var f in d.Fields.EnumerateArray())
        {
            string t = f.GetProperty("type").GetString();
            if (t == "editor_section") continue;
            int n;
            if (t == "pad") { n = f.GetProperty("size").GetInt32(); if (sb != null) sb.AppendLine($"{indent}  @{baseOff + off,5} pad {n}"); off += n; continue; }
            n = SizeOf(t, f);
            int count = 1;
            if (f.TryGetProperty("count", out var c)) count = c.GetInt32();
            if (f.TryGetProperty("bounds", out var bo) && bo.ValueKind == JsonValueKind.True) count *= 2;
            string name = f.TryGetProperty("name", out var nm) ? nm.GetString() : "?";
            string extra = "";
            if (t == "TagReflexive" && f.TryGetProperty("struct", out var st)) extra = " -> " + st.GetString();
            if (t == "TagDependency" && f.TryGetProperty("classes", out var cl)) extra = " [" + string.Join(",", cl.EnumerateArray().Select(x => x.GetString())) + "]";
            if (sb != null) sb.AppendLine($"{indent}  @{baseOff + off,5} {t}{(count > 1 ? "[" + count + "]" : "")} {name}{extra}");
            off += n * count;
        }
        return off;
    }
}

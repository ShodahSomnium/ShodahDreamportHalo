using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class W
{
    public const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static int Main(string[] a)
    {
        switch (a[0])
        {
            case "raw": return Raw(a);
            case "defs": return DefsCmd(a);
            case "bsp": return Bsp(a);
            case "break": return Break(a);
            case "alpha": return Alpha(a);
            case "flares": return Flares(a);
            case "detail3": return Detail3(a);
            case "detail2": return Detail2(a);
            case "detail": return Detail(a);
            case "dev": return Dev(a);
            case "extras": return Extras(a);
            case "camp": return Camp(a);
            case "water": return Water(a);
            case "rgb": return Rgb(a);
            case "placed": return Placed(a);
            case "nodes": return Nodes(a);
            case "leaf": return Leaves(a);
            case "sky": return Sky(a);
            case "cube3": return Cube3(a);
            case "cube2": return Cube2(a);
            case "cube": return Cube(a);
            case "lm": return LmStats(a);
            case "bfmt": return BitmFormats(a);
            case "shaders": return Shaders(a);
            case "sstats": return ShaderStats(a);
            case "light": return Light(a);
            case "mwind": return ModelWinding(a);
        }
        return More(a);
    }

    static int Raw(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        Console.WriteLine($"bsps {sc.Bsps.Count} skies {sc.Skies.Count}: {string.Join(", ", sc.Skies)}");
        for (int bi = 0; bi < sc.Bsps.Count; bi++)
        {
            var b = sc.Bsps[bi];
            var r = sc.BspRoot(bi);
            Console.WriteLine($"bsp {bi} {b.Tag} file {b.FileOffset:X} size {b.Size:X} addr {b.Address:X} root at {r.At:X}");
            Console.WriteLine($"  lightmaps bitmap {r.Ref(0)}  ambient {r.Vec3(44)}");
            Console.WriteLine($"  counts: collmat {r.Count(164)} collbsp {r.Count(176)} nodes {r.Count(188)} leaves {r.Count(224)} surfaces {r.Count(248)} lightmaps {r.Count(260)} clusters {r.Count(308)} fogplanes {r.Count(376)} fogregions {r.Count(388)} fogpal {r.Count(400)} weather {r.Count(436)} markers {r.Count(576)} detail {r.Count(588)}");
            int lmi = 0;
            foreach (var lm in r.Block(260, 32))
            {
                int mats = lm.Count(20);
                Console.WriteLine($"  lightmap {lmi++} bitmap {lm.I16(0)} materials {mats}");
                int k = 0;
                foreach (var mt in lm.Block(20, 256))
                {
                    if (k++ >= int.Parse(a.Length > 2 ? a[2] : "3")) break;
                    Console.WriteLine($"    {mt.Ref(0)} surf {mt.I32(20)}+{mt.I32(24)} rv type {mt.U16(176)} n {mt.U32(180)} off {mt.U32(184)} lv type {mt.U16(196)} n {mt.U32(200)} off {mt.U32(204)} unc size {mt.DataSize(216)} flags {mt.I32(220)} foff {mt.I32(224)} addr {mt.U32(228):X} comp size {mt.DataSize(236)} -> exp {mt.U32(180)*56 + mt.U32(200)*20}");
                }
            }
        }
        return 0;
    }
}

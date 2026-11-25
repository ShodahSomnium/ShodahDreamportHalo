using System;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class P
{
    static void Line(byte[] img, int W, int H, int x0, int y0, int x1, int y1, byte r, byte g, byte b)
    {
        int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) + 1;
        for (int i = 0; i <= n; i++)
        {
            int x = x0 + (x1 - x0) * i / n, y = y0 + (y1 - y0) * i / n;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            {
                int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= W || yy >= H) continue;
                int o = (yy * W + xx) * 4; img[o] = r; img[o + 1] = g; img[o + 2] = b; img[o + 3] = 255;
            }
        }
    }

    // panel: side view (x forward to the right, z up) and front view (y left to the left)
    static void Draw(byte[] img, int W, int H, HaloPose pose, int ox, int oy, float scale, bool front)
    {
        var sk = pose.Skel;
        for (int i = 0; i < sk.Count; i++)
        {
            int p = sk.Parent[i]; if (p < 0) continue;
            float ax = front ? -pose.MTrans[p * 3 + 1] : pose.MTrans[p * 3], az = pose.MTrans[p * 3 + 2];
            float bx = front ? -pose.MTrans[i * 3 + 1] : pose.MTrans[i * 3], bz = pose.MTrans[i * 3 + 2];
            bool left = sk.Names[i].Contains(" l ") || sk.Names[i].Contains("left");
            Line(img, W, H, ox + (int)(ax * scale), oy - (int)(az * scale), ox + (int)(bx * scale), oy - (int)(bz * scale), left ? (byte)120 : (byte)230, left ? (byte)160 : (byte)230, 230);
        }
        foreach (var m in sk.Markers)
        {
            if (!(m.Name.Contains("right hand") || m.Name.Contains("left hand") || m.Name == "head")) continue;
            pose.MarkerFrame(m, out float px, out float py, out float pz, out float fx, out float fy, out float fz, out float ux, out float uy, out float uz);
            float sx = front ? -py : px, dx = front ? -fy : fx;
            Line(img, W, H, ox + (int)(sx * scale), oy - (int)(pz * scale), ox + (int)((sx + dx * 0.08f) * scale), oy - (int)((pz + fz * 0.08f) * scale), 255, 70, 70);
        }
        Line(img, W, H, ox - 60, oy, ox + 60, oy, 80, 80, 80);
    }

    // poses <map> <bipd name end> <out.png> : idle, run, strafe left, aim 60 left 30 up, fire, reload, throw, death
    static int Poses(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var bipd = m.OfClass("bipd").First(x => x.Name.EndsWith(a[2]));
        var def = HaloCharacterDef.From(bipd);
        var model = new HaloModel(def.Model);
        var sk = new HaloSkeleton(model);
        var g = new HaloAnimGraph(def.Graph);
        string label = a.Length > 4 ? a[4] : null;
        if (label == null && def.Weapon != null) label = def.Weapon.Root.Str32(780);
        Console.WriteLine($"{bipd.Name}: weapon {def.Weapon?.Name} label '{label}' body {def.MaxBody} shield {def.MaxShield} team {def.Team} flying {def.Flying}");
        string[] names = { "idle", "run", "strafe-l", "back", "aim", "fire", "reload", "throw", "melee", "ping", "death", "dead" };
        int W = 150 * names.Length, H = 520;
        var img = new byte[W * H * 4];
        for (int i = 0; i < W * H; i++) { img[i * 4] = 30; img[i * 4 + 1] = 30; img[i * 4 + 2] = 34; img[i * 4 + 3] = 255; }
        float scale = 200f / Math.Max(0.2f, def.StandingHeight > 0 ? def.StandingHeight : 0.6f) * 0.5f;
        for (int k = 0; k < names.Length; k++)
        {
            var an = new HaloUnitAnimator(sk, g, 7);
            var inp = new HaloUnitAnimInput { Seat = "stand", WeaponLabel = label, Aiming = true, Looking = true };
            float dt = 1f / 60f;
            for (int s = 0; s < 3; s++) an.Step(inp, dt);
            float ns = an.NaturalSpeed;
            int steps = 30;
            switch (names[k])
            {
                case "run": inp.MoveX = ns; break;
                case "strafe-l": inp.MoveY = ns; break;
                case "back": inp.MoveX = -ns; break;
                case "aim": inp.AimYaw = 1.0f; inp.AimPitch = 0.5f; break;
                case "fire": an.Fire(); steps = 3; break;
                case "reload": an.Reload(); steps = 40; break;
                case "throw": an.Throw(); steps = 20; break;
                case "melee": an.Melee(); steps = 12; break;
                case "ping": an.Ping(true, 0, 0); steps = 8; break;
                case "death": an.Kill(false, 0, 0); steps = 25; break;
                case "dead": an.Kill(false, 0, 0); steps = 400; break;
            }
            float rx = 0, ry = 0; int keys = 0, feet = 0;
            for (int s = 0; s < steps; s++) { an.Step(inp, dt); rx += an.RootX; ry += an.RootY; if (an.ActionKey) keys++; if (an.FootDown) feet++; }
            an.Pose.ToModel();
            Console.WriteLine($"  {names[k],-8} base {an.BaseName} action {an.ActionName} death {an.DeathName} class {an.ClassName} natural {ns:0.00} wu/s root ({rx:0.000},{ry:0.000}) keys {keys} feet {feet}");
            Draw(img, W, H, an.Pose, 75 + k * 150, 230, scale, false);
            Draw(img, W, H, an.Pose, 75 + k * 150, 500, scale, true);
        }
        Png.Write(a[3], W, H, img);
        return 0;
    }
}

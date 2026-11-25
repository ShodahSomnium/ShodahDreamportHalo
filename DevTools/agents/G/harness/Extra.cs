using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class G
{
    static int Extra(HaloMap m, string[] a)
    {
        switch (a[0])
        {
            case "funcs": return Funcs(m, a);
            case "effe":
                foreach (var t in m.OfClass("effe"))
                {
                    if (a.Length > 2 && !t.Name.Contains(a[2])) continue;
                    var r = t.Root;
                    Console.WriteLine($"{t.Name} flags {r.U32(0)} maxDmgR {r.F32(8)} locs {string.Join(",", r.Block(40, 32).Select(l => l.Str32(0)))}");
                    int e = 0;
                    foreach (var ev in r.Block(52, 68))
                    {
                        foreach (var p in ev.Block(44, 104))
                            Console.WriteLine($"   ev{e} delay {ev.F32(8)} part {HaloMap.FourCC(p.U32(20))} {p.Ref(24)} loc {p.I16(4)}");
                        e++;
                    }
                }
                return 0;
            case "models":
                foreach (var t in m.OfClass("mod2"))
                {
                    if (!t.Name.Contains(a[2])) continue;
                    var model = new HaloModel(t);
                    Console.WriteLine($"{t.Name}: nodes {model.Nodes.Count} markers {model.Markers.Count}");
                    foreach (var n in model.Nodes) Console.WriteLine($"   node {n.Name} parent {n.Parent} t {n.Translation} q ({n.Qi:0.###},{n.Qj:0.###},{n.Qk:0.###},{n.Qw:0.###})");
                    foreach (var mk in model.Markers) foreach (var x in mk.Instances) Console.WriteLine($"   marker {mk.Name} node {x.Node} t {x.Translation} q ({x.Qi:0.###},{x.Qj:0.###},{x.Qk:0.###},{x.Qw:0.###})");
                    foreach (var r in model.Regions) foreach (var p in r.Permutations) Console.WriteLine($"   region {r.Name} perm {p.Name} lods {string.Join(",", p.Lod)}");
                    // bounds of the highest LOD
                    float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f, z0 = 1e9f, z1 = -1e9f;
                    foreach (var r in model.Regions)
                    {
                        if (r.Permutations.Count == 0) continue;
                        int gi = r.Permutations[0].Lod[4]; if (gi < 0) continue;
                        foreach (var part in model.Geometries[gi].Parts)
                        {
                            var d = model.Read(part);
                            foreach (var p in d.Position) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z); }
                        }
                    }
                    Console.WriteLine($"   bounds x {x0:0.###}..{x1:0.###} y {y0:0.###}..{y1:0.###} z {z0:0.###}..{z1:0.###} (units; m = x3.048)");
                }
                return 0;
            case "weapmodels":
                foreach (var w in m.OfClass("weap").Concat(m.OfClass("eqip")))
                {
                    var mt = w.Root.Ref(40);
                    if (mt == null) continue;
                    var model = new HaloModel(mt);
                    Console.WriteLine($"{w.Name}: model {mt.Name} nodes {model.Nodes.Count}");
                    foreach (var mk in model.Markers) if (!mk.Name.Contains("marine") && !mk.Name.Contains("ejection") && !mk.Name.Contains("flashlight")) foreach (var x in mk.Instances) Console.WriteLine($"   marker {mk.Name} node {x.Node} t {x.Translation}");
                    float x0 = 1e9f, x1 = -1e9f, y0 = 1e9f, y1 = -1e9f, z0 = 1e9f, z1 = -1e9f;
                    foreach (var r in model.Regions)
                    {
                        if (r.Permutations.Count == 0) continue;
                        int gi = r.Permutations[0].Lod[4]; if (gi < 0) continue;
                        foreach (var part in model.Geometries[gi].Parts)
                        {
                            var d = model.Read(part);
                            foreach (var p in d.Position) { x0 = Math.Min(x0, p.X); x1 = Math.Max(x1, p.X); y0 = Math.Min(y0, p.Y); y1 = Math.Max(y1, p.Y); z0 = Math.Min(z0, p.Z); z1 = Math.Max(z1, p.Z); }
                        }
                    }
                    Console.WriteLine($"   bounds x {x0:0.###}..{x1:0.###} y {y0:0.###}..{y1:0.###} z {z0:0.###}..{z1:0.###}");
                }
                return 0;
            case "fpanim":
                foreach (var w in m.OfClass("weap"))
                {
                    var at = w.Root.Ref(1132);
                    if (at == null) continue;
                    var names = at.Root.Block(116, 180).Select(x => x.Str32(0) + ":" + x.U16(34) + (x.I16(52) >= 0 ? "k" + x.I16(52) : "")).ToArray();
                    Console.WriteLine($"{w.Name}: {string.Join(" ", names)}");
                }
                return 0;
            case "hmt":
            {
                var t = m.OfClass("hmt ").First();
                var r = t.Root;
                var text = r.DataBytes(0);
                var els = r.Block(20, 2).ToArray();
                foreach (var msg in r.Block(32, 64))
                {
                    int ti = msg.I16(32), ei = msg.I16(34), panels = msg.U8(36);
                    var sb = new System.Text.StringBuilder();
                    int at = ti * 2;
                    int e = ei;
                    // elements: type 0 = text run of 'data' chars, 1 = an icon / input
                    for (int k = 0; k < 40 && e < els.Length; k++, e++)
                    {
                        int type = els[e].I8(0), data = els[e].U8(1);
                        if (type == 0) { if (data == 0) break; sb.Append(System.Text.Encoding.Unicode.GetString(text, at, Math.Min(data * 2, text.Length - at)).TrimEnd('\0')); at += data * 2; }
                        else sb.Append("[" + type + ":" + data + "]");
                    }
                    Console.WriteLine($"{msg.Str32(0)}: {sb}");
                }
                return 0;
            }
            case "catalog":
            {
                var c = new HaloCombatCatalog(m);
                Console.WriteLine($"grenades: " + string.Join(" | ", c.Grenades.Select(g => $"max {g.MaximumCount} mp {g.MultiplayerSpawn} {g.Projectile}")));
                Console.WriteLine($"unit {c.MultiplayerUnit} body {c.Vitality?.MaximumBody} shield {c.Vitality?.MaximumShield} stun {c.Vitality?.StunTime} recharge {c.Vitality?.FullRechargeSeconds} melee {c.UnitMelee} grenadeV {c.GrenadeVelocity} weapons {string.Join(", ", c.UnitWeapons)}");
                Console.WriteLine($"falling {c.HarmfulFallingSpeedMin}-{c.HarmfulFallingSpeedMax} max {c.MaximumFallingSpeed} wu/s, {c.FallingDamage} {c.DistanceDamage}");
                foreach (var n in c.PromptNames) Console.WriteLine($"prompt {n}: '{c.Prompt(n, "[grip]", "Shotgun", "Pistol")}'");
                foreach (var t in m.OfClass("weap"))
                {
                    var d = c.Weapon(t);
                    if (d.Model == null) continue;
                    var p = c.Projectile(d.PrimaryTrigger?.Projectile);
                    var dmg = p != null ? c.Damage(p.ImpactDamage) : null;
                    var blast = p != null ? c.Blast(p.DetonationEffect) : null;
                    Console.WriteLine($"{d.Name,-18} {d.Kind,-12} oneHand {d.OneHanded} mag {d.PrimaryMagazine?.RoundsLoadedMaximum}/{d.PrimaryMagazine?.RoundsTotalInitial} heat {d.UsesHeat} batt {d.UsesBattery} melee {d.MeleeSeconds:0.00}s vent {d.OverheatSeconds:0.00}s proj {p?.InitialSpeed * 3.048f:0}m/s->{p?.FinalSpeed * 3.048f:0} grav {p?.AirGravityScale} dmg {dmg?.UpperMin}-{dmg?.UpperMax} blast {(blast != null ? string.Join(",", blast) : "")} name '{d.Name}' ammo '{c.AmmoMessage(d.PickupTextIndex, 30)}'");
                }
                foreach (var t in m.OfClass("eqip")) { var e = c.Equipment(t); Console.WriteLine($"eqip {e.Name} {e.Kind} {e.PowerupTime}s grenade {e.GrenadeType}"); }
                return 0;
            }
            case "sim":
            {
                var c = new HaloCombatCatalog(m);
                foreach (var t in m.OfClass("weap"))
                {
                    var d = c.Weapon(t);
                    if (d.Model == null || d.PrimaryTrigger == null || d.PrimaryTrigger.Projectile == null) continue;
                    var s = new HaloWeaponState(d);
                    var buf = new HaloWeaponState.Shot[8];
                    int shots = 0, charged = 0; float overAt = -1, errMax = 0; float dt = 1f / 90f;
                    int startLoaded = s.Loaded, startReserve = s.Reserve;
                    for (int f = 0; f < 270; f++)   // hold 3 s
                    {
                        int n = s.Step(dt, true, false, buf);
                        shots += n; for (int i = 0; i < n; i++) { if (buf[i].Charged) charged++; errMax = Math.Max(errMax, buf[i].ErrorAngle); }
                        if (s.JustOverheated && overAt < 0) overAt = f * dt;
                    }
                    s.Step(dt, false, false, buf);
                    int released = 0; for (int f = 0; f < 1; f++) released += s.Step(dt, false, false, buf);
                    // cool down
                    float cool = 0; while (s.Overheated && cool < 20) { s.Step(dt, false, false, buf); cool += dt; }
                    Console.WriteLine($"{d.Name,-18} 3 s held: {shots} shots ({charged} charged) loaded {startLoaded}->{s.Loaded} reserve {startReserve} heat {s.Heat:0.00} overheated at {overAt:0.00}s cooled after {cool:0.00}s age {s.Age:0.000} maxErr {errMax * 57.3f:0.0} deg");
                }
                // the plasma pistol: tap, then a held charge
                var ppTag = m.Find("weap", "weapons\\plasma pistol\\plasma pistol");
                if (ppTag != null)
                {
                    var s = new HaloWeaponState(c.Weapon(ppTag)); var buf = new HaloWeaponState.Shot[8]; float dt = 1f / 90f;
                    int tap = 0; for (int f = 0; f < 5; f++) tap += s.Step(dt, true, false, buf); tap += s.Step(dt, false, false, buf);
                    for (int f = 0; f < 60; f++) s.Step(dt, false, false, buf);
                    int held = 0, ch = 0; for (int f = 0; f < 90; f++) held += s.Step(dt, true, false, buf);
                    int rel = s.Step(dt, false, false, buf); if (rel > 0 && buf[0].Charged) ch++;
                    Console.WriteLine($"plasma pistol: tap {tap} shot(s); 1 s hold {held} shot(s) then release {rel} (charged {ch}) heat {s.Heat:0.00} overheated {s.Overheated}");
                }
                return 0;
            }
            case "nodes":
                foreach (var w in m.OfClass("weap"))
                {
                    foreach (int which in new[] { 40, 1116 })
                    {
                        var mt = w.Root.Ref(which);
                        if (mt == null) continue;
                        if (a.Length > 2 && !w.Name.Contains(a[2])) continue;
                        var model = new HaloModel(mt);
                        Console.WriteLine($"{w.Name} {(which == 40 ? "3P" : "FP")} {mt.Name}: {model.Nodes.Count} nodes, regions {string.Join(",", model.Regions.Select(r => r.Name + "[" + string.Join("/", r.Permutations.Select(p => p.Name)) + "]"))}");
                        int nn = model.Nodes.Count;
                        var mn = new float[nn, 3]; var mx = new float[nn, 3]; var cnt = new int[nn];
                        for (int i = 0; i < nn; i++) { for (int k = 0; k < 3; k++) { mn[i, k] = 1e9f; mx[i, k] = -1e9f; } }
                        foreach (var r in model.Regions)
                        {
                            if (r.Permutations.Count == 0) continue;
                            int gi = r.Permutations[0].Lod[4]; if (gi < 0) continue;
                            foreach (var part in model.Geometries[gi].Parts)
                            {
                                var d = model.Read(part);
                                for (int v = 0; v < d.Position.Length; v++)
                                {
                                    int n0 = d.Node0[v]; if (n0 < 0 || n0 >= nn || d.Weight0[v] < 0.5f) continue;
                                    var p = d.Position[v]; cnt[n0]++;
                                    mn[n0, 0] = Math.Min(mn[n0, 0], p.X); mn[n0, 1] = Math.Min(mn[n0, 1], p.Y); mn[n0, 2] = Math.Min(mn[n0, 2], p.Z);
                                    mx[n0, 0] = Math.Max(mx[n0, 0], p.X); mx[n0, 1] = Math.Max(mx[n0, 1], p.Y); mx[n0, 2] = Math.Max(mx[n0, 2], p.Z);
                                }
                            }
                        }
                        for (int i = 0; i < nn; i++)
                        {
                            var n = model.Nodes[i];
                            Console.WriteLine($"   node {i} {n.Name} parent {n.Parent} t {n.Translation} verts {cnt[i]}" + (cnt[i] > 0 ? $" x {mn[i, 0]:0.###}..{mx[i, 0]:0.###} y {mn[i, 1]:0.###}..{mx[i, 1]:0.###} z {mn[i, 2]:0.###}..{mx[i, 2]:0.###}" : ""));
                        }
                    }
                }
                return 0;
            case "points":
            {
                var c = new HaloCombatCatalog(m);
                foreach (var t in m.OfClass("weap")) { var d = c.Weapon(t); if (d.Model == null || d.Magazines.Count == 0 && d.Kind != HaloWeaponDef.Family.Pistol) continue; Console.WriteLine(d.Name + ": " + HaloWeaponPoints.Measure(d).Describe()); }
                return 0;
            }
            case "shaders":
                foreach (var mt in m.OfClass("mod2"))
                {
                    if (!mt.Name.Contains(a[2])) continue;
                    var model = new HaloModel(mt);
                    foreach (var s in model.Shaders)
                    {
                        var d = HaloShaderDef.Of(s.Shader);
                        string info = d.Object != null ? $"soso flags {d.Object.Flags} base {d.Object.BaseMap} multi {d.Object.MultipurposeMap} detail {d.Object.DetailMap} cube {d.Object.ReflectionCube} colorSrc {d.Object.ColorSource} changeSrc {d.Object.ChangeColorSource} xbox {d.Object.XboxChannels}"
                            : d.Transparent != null ? $"schi first {d.Transparent.FirstMapType} blend {d.Transparent.Blend} maps {string.Join(",", d.Transparent.Maps.Select(x => x.Map?.Name))} extra {d.Transparent.ExtraLayers.Count}" : "other";
                        Console.WriteLine($"{mt.Name}: {s.Shader} {info}");
                    }
                }
                return 0;
            case "animsnd":
                foreach (var w in m.OfClass("weap"))
                {
                    var at = w.Root.Ref(1132);
                    if (at == null) continue;
                    var snds = at.Root.Block(84, 20).Select(x => x.Ref(0)).ToArray();
                    foreach (var x in at.Root.Block(116, 180)) { int si = x.I16(60); if (si >= 0) Console.WriteLine($"{w.Name}: {x.Str32(0)} frames {x.U16(34)} sound [{si}] {(si < snds.Length ? snds[si]?.Name : "?")} at frame {x.I16(62)}"); }
                }
                return 0;
            case "reloadsounds":
            {
                var c = new HaloCombatCatalog(m);
                foreach (var t in m.OfClass("weap")) { var d = c.Weapon(t); if (d.Model == null) continue; var p = HaloWeaponPoints.Measure(d); Console.WriteLine(d.Name + ": " + HaloReloadSounds.Build(d, p).Describe()); }
                return 0;
            }
            case "flash":
                foreach (var t in m.OfClass("bipd"))
                {
                    var r = t.Root;
                    Console.WriteLine(t.Name + ": integrated light toggle " + r.Ref(392) + ", attachments: " + string.Join(", ", r.Block(320, 72).Select(x => x.Ref(0) + " @'" + x.Str32(16) + "' primary " + x.U16(48))));
                }
                foreach (var t in m.OfClass("weap")) { var r = t.Root; if ((r.U32(776) & (1u << 14)) != 0 || r.Ref(1056) != null) Console.WriteLine(t.Name + ": night vision flag " + ((r.U32(776) & (1u << 14)) != 0) + " light on " + r.F32(1048) + " " + r.Ref(1056) + " off " + r.F32(1052) + " " + r.Ref(1072)); }
                return 0;
            case "ustr":
                foreach (var t in m.OfClass("ustr"))
                {
                    if (a.Length > 2 && !t.Name.Contains(a[2])) continue;
                    var r = t.Root;
                    int i = 0;
                    foreach (var s in r.Block(0, 20))
                    {
                        var b = s.DataBytes(0);
                        string str = System.Text.Encoding.Unicode.GetString(b).TrimEnd('\0');
                        Console.WriteLine($"{t.Name} [{i++}] {str.Replace("\r", "\\r").Replace("\n", "\\n")}");
                    }
                }
                return 0;
            case "tags":
                foreach (var t in m.Tags) if (t.Name.Contains(a[2])) Console.WriteLine($"{t.ClassName} {t.Name}");
                return 0;
        }
        Console.WriteLine("?"); return 1;
    }
}

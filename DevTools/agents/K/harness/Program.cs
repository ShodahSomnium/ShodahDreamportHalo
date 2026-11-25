using System;
using System.Collections.Generic;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static class Program
{
    const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static int Main(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        var d = new HaloMissionData(sc);
        switch (a[0])
        {
            case "sum":
            {
                Console.WriteLine($"{m.Name}: objects {d.Objects.Count} names {d.Names.Count} groups {d.DeviceGroups.Count} volumes {d.TriggerVolumes.Count} bspswitch {d.BspSwitches.Count} profiles {d.Profiles.Count} flags {d.CutsceneFlags.Count} bsps {d.Bounds.Count}");
                foreach (var k in d.Objects.GroupBy(o => o.Kind)) Console.WriteLine($"  {k.Key}: {k.Count()} (named {k.Count(o => o.Name.Length > 0)}, not auto {k.Count(o => !o.AutomaticallyPlaced)}, bspmask {k.Count(o => o.BspMask != 0)})");
                for (int i = 0; i < d.Bounds.Count; i++) { var b = d.Bounds[i]; Console.WriteLine($"  bsp {i} {sc.Bsps[i].Tag?.Name}: x {b.MinX:0.0}..{b.MaxX:0.0} y {b.MinY:0.0}..{b.MaxY:0.0} z {b.MinZ:0.0}..{b.MaxZ:0.0} objs {d.Objects.Count(o => d.InBsp(o, i))}"); }
                foreach (var p in d.Profiles) Console.WriteLine($"  profile '{p.Name}' health {p.Health} shield {p.Shield} {p.Primary?.Name} {p.PrimaryLoaded}/{p.PrimaryReserved} {p.Secondary?.Name} {p.SecondaryLoaded}/{p.SecondaryReserved} frag {p.Frag} plasma {p.Plasma}");
                foreach (var s in sc.StartLocations) Console.WriteLine($"  start {s.Position} facing {s.Facing:0.00} team {s.Team} bsp {s.BspIndex} types {string.Join(",", s.Types)}");
                foreach (var s in d.BspSwitches) Console.WriteLine($"  bspswitch vol {s.TriggerVolume} '{(s.TriggerVolume >= 0 && s.TriggerVolume < d.TriggerVolumes.Count ? d.TriggerVolumes[s.TriggerVolume].Name : "?")}' {s.Source}->{s.Destination}");
                foreach (var g in d.DeviceGroups) Console.WriteLine($"  group '{g.Name}' init {g.Initial} flags {g.Flags}");
                return 0;
            }
            case "vol":
                Console.WriteLine("centre inside: " + d.TriggerVolumes.Count(t => { var c = t.Center; return t.Contains(c.X, c.Y, c.Z); }) + "/" + d.TriggerVolumes.Count + ", start in: " + string.Join(",", d.TriggerVolumes.Where(t => t.Contains(sc.StartLocations[0].Position.X, sc.StartLocations[0].Position.Y, sc.StartLocations[0].Position.Z + 0.3f)).Select(t => t.Name)));
                foreach (var t in d.TriggerVolumes.Take(8)) Console.WriteLine($"  vol '{t.Name}' type {t.Type} corner {t.Corner} ext {t.Extent} fwd {t.Forward} up {t.Up} centre {t.Center}");
                return 0;
            case "dev":
            {
                var seen = new HashSet<HaloTag>();
                foreach (var o in d.Objects.Where(o => o.Kind == HaloMissionData.Kind.Machine || o.Kind == HaloMissionData.Kind.Control))
                {
                    if (o.Type != null && seen.Add(o.Type))
                    {
                        var dd = new HaloDeviceDef(o.Type);
                        Console.WriteLine("DEF " + dd + " in " + string.Join(",", dd.In) + " anim " + dd.AnimationGraph?.Name + " open " + dd.Open?.Name + " elevnode " + dd.ElevatorNode + " flags " + dd.Flags + " mflags " + dd.MachineFlags + " coll " + dd.CollisionResponse);
                        if (dd.AnimationGraph != null)
                        {
                            try { var g = new HaloAnimGraph(dd.AnimationGraph); Console.WriteLine("   nodes " + string.Join(",", g.NodeNames) + " anims " + string.Join(", ", g.Animations.Select(x => x.Name + "/" + x.Frames + "/" + x.Type))); }
                            catch (Exception e) { Console.WriteLine("   graph " + e.Message); }
                        }
                    }
                }
                if (a.Length > 2) foreach (var o in d.Objects.Where(o => o.Kind == HaloMissionData.Kind.Machine || o.Kind == HaloMissionData.Kind.Control)) Console.WriteLine($"  {o} pg {o.PowerGroup} posg {o.PositionGroup} dflags {o.DeviceFlags} sub {o.SubFlags} np {o.NotPlaced}");
                return 0;
            }
            case "anim": AnimProbe.Run(m, a[2]); return 0;
            case "rock": RockProbe.Run(m, a[2], a[3]); return 0;
            case "att":
                foreach (var t in m.OfClass("mach").Concat(m.OfClass("ctrl"))) { var dd = new HaloDeviceDef(t); foreach (var at in t.Root.Block(320, 72)) { var x = at.Ref(0); if (x != null) Console.WriteLine($"{System.IO.Path.GetFileName(t.Name)} in {string.Join(",", dd.In)} att {x.ClassName} {x.Name} marker {at.Str32(16)} scale {at.U16(48)}"); } Console.WriteLine($"{System.IO.Path.GetFileName(t.Name)} open {dd.Open?.ClassName} {dd.Open?.Name} close {dd.Close?.Name} opened {dd.Opened?.Name} closed {dd.Closed?.Name} on {dd.On?.Name} off {dd.Off?.Name}"); }
                return 0;
            case "names":
                foreach (var o in d.Objects.Where(o => o.Name.Length > 0)) Console.WriteLine($"  {o} np {o.NotPlaced}");
                return 0;
        }
        return 1;
    }
}

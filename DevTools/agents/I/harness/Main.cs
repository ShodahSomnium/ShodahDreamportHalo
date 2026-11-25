using System;
using System.IO;
using System.Linq;
using SomniumSpace.Worlds.SomniumHalo;

static partial class I
{
    public const string Maps = @"C:\Program Files (x86)\Microsoft Games\Halo\MAPS\";
    static int Main(string[] a)
    {
        HaloLog.Sink = Console.WriteLine;
        var m = new HaloMap(Maps + a[1] + ".map");
        var sc = new HaloScenario(m);
        switch (a[0])
        {
            case "actors": return Actors(m, sc);
            case "enc": return Enc(m, sc, a.Length > 2 ? a[2] : null);
            case "cmd": return Cmd(m, sc);
            case "conv": return Conv(m, sc);
            case "path": return PathData(m, sc);
            case "nav": return Nav(m, sc, a);
            case "gren": return Gren(m);
            case "diff": { var df = new HaloAiDifficulty(m); for (int d = 0; d < 4; d++) Console.WriteLine(d + ": " + df.Describe(d)); return 0; }
            case "navpng": return NavPng(m, sc, a);
            case "squads": return SquadsOnNav(m, sc);
        }
        Console.WriteLine("actors|enc [name]|cmd|conv|path|nav <map>");
        return 1;
    }

    static int Actors(HaloMap m, HaloScenario sc)
    {
        var ai = new HaloAiScenario(sc);
        var cat = new HaloActorCatalog(m);
        Console.WriteLine($"actor palette {ai.ActorPalette.Count}");
        for (int i = 0; i < ai.ActorPalette.Count; i++)
        {
            var v = cat.Variant(ai.ActorPalette[i]);
            Console.WriteLine($"[{i}] {ai.ActorPalette[i]?.Name}");
            if (v == null) continue;
            Console.WriteLine("    actv " + v.Describe());
            if (v.Actor != null) Console.WriteLine("    actr " + v.Actor.Tag.Name + " " + v.Actor.Describe());
        }
        Console.WriteLine("all actv tags:");
        foreach (var t in m.OfClass("actv")) { var v = cat.Variant(t); Console.WriteLine("  " + t.Name + " -> " + (v?.Actor?.Type.ToString() ?? "?") + " rof " + v?.RateOfFire + " burst " + v?.BurstDuration + " sep " + v?.BurstSeparation); }
        return 0;
    }

    static int Enc(HaloMap m, HaloScenario sc, string only)
    {
        var ai = new HaloAiScenario(sc);
        int total = 0;
        foreach (var e in ai.Encounters)
        {
            if (only != null && !e.Name.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"ENC [{e.Index}] '{e.Name}' flags {e.Flags:X} team {e.Team} search {e.SearchBehavior} bsp m{e.ManualBsp}/p{e.PrecomputedBsp} respawn {e.RespawnDelay} squads {e.Squads.Count} platoons {e.Platoons.Count} fpos {e.FiringPositions.Count}");
            foreach (var p in e.Platoons) Console.WriteLine($"   PLATOON '{p.Name}' flags {p.Flags} change {p.ChangeWhen}->{p.ChangeHappensTo} maneuver {p.ManeuverWhen}->{p.ManeuverHappensTo}");
            foreach (var s in e.Squads)
            {
                total += s.Starts.Count;
                Console.WriteLine($"   SQUAD '{s.Name}' actor [{s.ActorType}] {Short(ai.PaletteVariant(s.ActorType))} platoon {s.Platoon} state {s.InitialState}->{s.ReturnState} flags {s.Flags:X} leader {s.UniqueLeaderType} maneuverTo {s.ManeuverToSquad} delay {s.SquadDelayTime} count n{s.NormalCount}/i{s.InsaneCount} major {s.MajorUpgrade} respawn {s.RespawnMin}-{s.RespawnMax} tot {s.RespawnTotal}"
                    + $" groups att {HaloAiScenario.GroupLetters(s.Attacking)} attS {HaloAiScenario.GroupLetters(s.AttackingSearch)} attG {HaloAiScenario.GroupLetters(s.AttackingGuard)} def {HaloAiScenario.GroupLetters(s.Defending)} defS {HaloAiScenario.GroupLetters(s.DefendingSearch)} defG {HaloAiScenario.GroupLetters(s.DefendingGuard)} purs {HaloAiScenario.GroupLetters(s.Pursuing)} starts {s.Starts.Count} moves {s.MovePositions.Count}");
                foreach (var l in s.Starts) Console.WriteLine($"      start {l.Position} face {l.Facing * 57.3f:0} cl {l.Cluster} seq {l.SequenceId} req {l.Required} st {l.InitialState}->{l.ReturnState} actor {l.ActorType} cmd {l.CommandList}");
                foreach (var mp in s.MovePositions) Console.WriteLine($"      move {mp.Position} face {mp.Facing * 57.3f:0} w {mp.Weight} t {mp.Time} anim {mp.Animation} seq {mp.SequenceId}");
            }
            if (only != null) foreach (var f in e.FiringPositions) Console.WriteLine($"      fpos {f.Position} group {(char)('a' + f.Group)} cl {f.Cluster} surf {f.Surface}");
            else
            {
                var groups = e.FiringPositions.GroupBy(f => f.Group).OrderBy(g => g.Key).Select(g => (char)('a' + g.Key) + ":" + g.Count());
                Console.WriteLine("   fpos groups " + string.Join(" ", groups));
            }
        }
        Console.WriteLine($"total starting locations {total}");
        return 0;
    }

    static int Cmd(HaloMap m, HaloScenario sc)
    {
        var ai = new HaloAiScenario(sc);
        var atoms = new System.Collections.Generic.Dictionary<HaloAtom, int>();
        foreach (var c in ai.CommandLists)
        {
            Console.WriteLine($"CMDLIST [{c.Index}] '{c.Name}' flags {c.Flags:X} bsp {c.ManualBsp}/{c.PrecomputedBsp} points {c.Points.Count}");
            foreach (var a in c.Commands) { Console.WriteLine("    " + a); atoms[a.Atom] = atoms.TryGetValue(a.Atom, out int n) ? n + 1 : 1; }
        }
        Console.WriteLine("atoms used: " + string.Join(", ", atoms.OrderByDescending(k => k.Value).Select(k => k.Key + " " + k.Value)));
        Console.WriteLine("ai animations: " + string.Join(", ", ai.Animations.Select(x => x.Name + "(" + Short(x.Graph) + ")")));
        Console.WriteLine("ai scripts: " + string.Join(", ", ai.ScriptReferences));
        Console.WriteLine("ai recordings: " + string.Join(", ", ai.RecordingReferences));
        return 0;
    }

    static int Conv(HaloMap m, HaloScenario sc)
    {
        var ai = new HaloAiScenario(sc);
        foreach (var c in ai.Conversations)
        {
            Console.WriteLine($"CONV [{c.Index}] '{c.Name}' flags {c.Flags:X} trig {c.TriggerDistance} run {c.RunToPlayerDistance} parts {c.Participants.Count} lines {c.Lines.Count}");
            foreach (var p in c.Participants) Console.WriteLine($"    part flags {p.Flags} sel {p.SelectionType} actor {p.ActorType} obj {p.UseThisObject} newname {p.SetNewName} enc '{p.EncounterName}' {p.EncounterIndex} variants {string.Join(",", p.VariantNumbers)}");
            foreach (var l in c.Lines) Console.WriteLine($"    line flags {l.Flags} part {l.Participant} addr {l.Addressee}/{l.AddresseeParticipant} delay {l.Delay} snd {string.Join(" | ", l.Variants.Where(v => v != null).Select(Short))}");
        }
        return 0;
    }

    public static string Short(HaloTag t) { if (t == null) return "-"; int i = t.Name.LastIndexOf('\\'); return i >= 0 ? t.Name.Substring(i + 1) : t.Name; }
}

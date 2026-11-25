using System;
using SomniumSpace.Worlds.SomniumHalo;

static class NetTests4
{
    public static void Stream(Action<bool, string> check)
    {
        double now = 2000.0;
        Func<double> clock = () => now;
        var w = new HaloLoopbackWorld();
        var a = new HaloNet(w.Join(1, clock), clock, "[host]");
        var b = new HaloNet(w.Join(2, clock), clock, "[B]");
        var c = new HaloNet(w.Join(3, clock), clock, "[C]");
        var all = new System.Collections.Generic.List<HaloNet> { a, b, c };
        Action<double> run = s => { double end = now + s; while (now < end) { now += 1.0 / 60.0; foreach (var n in all) n.Tick(); } };
        run(3);
        int zone = HaloNetWire.NameHash("a10");
        a.PublishLobbyState(3, 0, zone, 1, 0, 0, HaloNetRules_CampaignBit, true, 1, 1760000000);
        foreach (var n in all) { n.CoopMission = true; n.Own.Status = HaloNetStatus.Playing; n.Own.ZoneHash = zone; n.Own.MatchSeq = 1; n.Own.MatchState = HaloMatchState.Running; n.Own.Alive = true; }
        run(0.5);
        var blob = new byte[5000];
        var rng = new Random(3);
        rng.NextBytes(blob);
        a.HostStream.SetBlob(blob);
        double t0 = now;
        while (now - t0 < 5 && b.HostStream.Complete == null) { now += 1.0 / 60.0; foreach (var n in all) n.Tick(); }
        double took = now - t0;
        run(0.5);
        check(Same(b.HostStream.Complete, blob) && Same(c.HostStream.Complete, blob), "stream: B and C assemble the host's 5000-byte blob (" + took.ToString("0.00") + " s, " + a.HostStream.ChunkCount + " chunks)");
        var blob2 = new byte[300]; rng.NextBytes(blob2);
        a.HostStream.SetBlob(blob2);
        run(1.0);
        check(Same(b.HostStream.Complete, blob2), "stream: a new blob replaces the old one");
        // the host leaves: B (lowest id now) hosts, still holds the last blob, and streams its own with a higher id
        a.Transport.Dispose(); all.Remove(a);
        run(3);
        check(b.Coop.Writing && Same(b.HostStream.Complete, blob2), "stream: the master left - B hosts and holds the old host's last blob");
        int oldId = c.HostStream.CompleteId;
        var blob3 = new byte[1000]; rng.NextBytes(blob3);
        b.HostStream.SetBlob(blob3);
        run(1.5);
        check(Same(c.HostStream.Complete, blob3) && c.HostStream.CompleteId != oldId, "stream: C gets the new host's blob (id " + c.HostStream.CompleteId.ToString("x") + ", was " + oldId.ToString("x") + ")");
    }

    const int HaloNetRules_CampaignBit = 1 << 4;

    static bool Same(byte[] x, byte[] y)
    {
        if (x == null || y == null || x.Length != y.Length) return false;
        for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
        return true;
    }
}

static class NetTests5
{
    public static void VehicleActor(Action<bool, string> check)
    {
        var o = new int[64];
        var v = new HaloNetActor { IsVehicle = true, Serial = HaloNetCoop.VehicleSerialBase + 12, Alive = true, UnitTag = 1500, VehicleId = 12, Position = new HaloV3(-120.5f, 33.25f, 410f),
            Rotation = new HaloQ(0.1f, 0.6f, -0.2f, 0.77f), Velocity = new HaloV3(12f, -1.5f, 3f), MuzzleOffset = new HaloV3(1.25f, -0.5f, 4.1f), ShotDir = new HaloV3(0, -0.3f, 1),
            FireCounter = 201, Vitality = 0.6f, Open = true, Hover = true };
        HaloNetCoop.PackActor(v, o, 8);
        var r = HaloNetCoop.UnpackActor(o, 8);
        float qn = (float)Math.Sqrt(0.1 * 0.1 + 0.6 * 0.6 + 0.2 * 0.2 + 0.77 * 0.77);
        var qq = new HaloQ(0.1f / qn, 0.6f / qn, -0.2f / qn, 0.77f / qn);
        check(r.IsVehicle && r.Serial == v.Serial && r.VehicleId == 12 && r.UnitTag == 1500 && r.Alive && HaloV3.Distance(r.Position, v.Position) < 0.011f && HaloQ.AngleDeg(r.Rotation, qq) < 0.3f
              && HaloV3.Distance(r.Velocity, v.Velocity) < 0.06f && HaloV3.Distance(r.MuzzleOffset, v.MuzzleOffset) < 0.006f && r.FireCounter == 201 && Math.Abs(r.Vitality - 0.6f) < 0.005f && r.Open && r.Hover
              && r.ShotDir.Z > 0.9f, "coop: a vehicle record round-trips (pose, velocity, muzzle, fire counter, vitality, open, hover)");
        var u = new HaloNetActor { Serial = 5, Team = 3, Alive = true, UnitTag = 1, VariantTag = 2, WeaponTag = 3, SeatVehicle = -1, Seat = -1 };
        HaloNetCoop.PackActor(u, o, 8);
        r = HaloNetCoop.UnpackActor(o, 8);
        check(!r.IsVehicle && r.SeatVehicle == -1 && r.Seat == -1 && !r.Exiting, "coop: a unit on foot has no seat");
    }
}

static class NetTests6
{
    public static void LateFirst(Action<bool, string> check)
    {
        var tl = new HaloNetTimeline();
        double view = 100.0;
        float last = float.NaN, maxStep = 0f;
        for (int f = 0; f < 600; f++)
        {
            view += 1.0 / 60.0;
            if (f % 6 == 0)
            {
                double stamp = view - (f == 0 ? 0.5 : 0.05);      // the first record arrives half a second late, the rest quickly
                var s = new HaloNetTimeline.Sample { Root = new HaloV3((float)(stamp * 10.0), 0, 0), InVehicle = true, VehiclePos = new HaloV3((float)(stamp * 10.0), 0, 0), HeadRot = HaloQ.Identity, LRot = HaloQ.Identity, RRot = HaloQ.Identity, VehicleRot = HaloQ.Identity };
                tl.Add(stamp, view, in s);
            }

            HaloNetTimeline.Sample o;
            if (!tl.Evaluate(view, out o)) continue;
            if (!float.IsNaN(last)) maxStep = Math.Max(maxStep, Math.Abs(o.VehiclePos.X - last));
            last = o.VehiclePos.X;
        }

        check(maxStep < 0.3f, "timeline: a late first sample does not make a 10 m/s pose jump (largest step " + maxStep.ToString("0.000") + " m a frame)");
    }
}

static class NetTests7
{
    public static void Sounds(Action<bool, string> check)
    {
        double now = 3000.0;
        Func<double> clock = () => now;
        var w = new HaloLoopbackWorld();
        var a = new HaloNet(w.Join(1, clock), clock, "[host]");
        var b = new HaloNet(w.Join(2, clock), clock, "[B]");
        var all = new System.Collections.Generic.List<HaloNet> { a, b };
        Action<double> run = s => { double end = now + s; while (now < end) { now += 1.0 / 60.0; foreach (var n in all) n.Tick(); } };
        run(3);
        int zone = HaloNetWire.NameHash("a10");
        a.PublishLobbyState(3, 0, zone, 1, 0, 0, 1 << 4, true, 1, 1760000000);
        foreach (var n in all) { n.CoopMission = true; n.Own.Status = HaloNetStatus.Playing; n.Own.ZoneHash = zone; n.Own.MatchSeq = 1; n.Own.MatchState = HaloMatchState.Running; n.Own.Alive = true; }
        run(0.5);
        for (int i = 0; i < 6; i++) a.Coop.AddSound(100 + i, 200 + i, 300 + i);      // a burst of six one-shots in one write
        a.Coop.SetLoop(2, 0x12345, 77);
        run(0.6);
        bool ok = b.Coop.NewSounds.Count == 18;
        for (int i = 0; i < 6 && ok; i++) ok = b.Coop.NewSounds[i * 3] == 100 + i && b.Coop.NewSounds[i * 3 + 2] == 300 + i;
        check(ok, "sounds: a burst of six one-shots reaches B once, in order (" + b.Coop.NewSounds.Count / 3 + ")");
        check(b.Coop.LoopA(2) == 0x12345 && b.Coop.LoopB(2) == 77, "sounds: B reads the playing loop");
        var c = new HaloNet(w.Join(3, clock), clock, "[C]"); c.CoopMission = true; all.Add(c);
        c.Own.Status = HaloNetStatus.Playing; c.Own.ZoneHash = zone; c.Own.MatchSeq = 1; c.Own.MatchState = HaloMatchState.Running; c.Own.Alive = true;
        run(3);
        check(c.Coop.NewSounds.Count == 0 && c.Coop.LoopA(2) == 0x12345, "sounds: a late joiner gets the loop that plays now, not the old one-shots");
        a.Coop.AddSound(7, 8, 9);
        a.Coop.SetLoop(2, 0, 0);
        run(0.6);
        check(c.Coop.NewSounds.Count == 3 && c.Coop.NewSounds[0] == 7 && c.Coop.LoopA(2) == 0, "sounds: a new one-shot reaches the late joiner; the stopped loop is gone");
        // a follower presses a device control: only the host gets it, once, as that player's press
        b.NoteControl(17);
        int[] uses = Controls(all, run, 17);
        check(uses[0] == 1 && uses[1] == 0 && uses[2] == 0 && uses[3] == b.MySlot, "controls: B's press of placement 17 reaches the host once as B's (host " + uses[0] + ", others " + (uses[1] + uses[2]) + ")");
        a.Transport.Dispose(); all.Remove(a);
        run(3);
        check(b.Coop.Writing && b.Coop.LoopA(2) == 0, "sounds: the master left - B hosts with the loop table it read");
        b.Coop.AddSound(42, 0, 0);
        c.Coop.NewSounds.Clear();
        run(0.6);
        check(c.Coop.NewSounds.Count == 3 && c.Coop.NewSounds[0] == 42, "sounds: the new host's one-shot reaches C once (the ring continues)");
        c.NoteControl(23);
        int[] uses2 = Controls(all, run, 23);
        check(uses2[0] == 1 && uses2[1] == 0 && uses2[3] == c.MySlot, "controls: after the hand-over C's press reaches the new host B once (" + uses2[0] + ", " + uses2[1] + ", from " + uses2[3] + ")");
    }


    public static void Presses(Action<bool, string> check)
    {
        double now = 5000.0;
        Func<double> clock = () => now;
        var w = new HaloLoopbackWorld();
        var a = new HaloNet(w.Join(1, clock), clock, "[host]");
        var b = new HaloNet(w.Join(2, clock), clock, "[B]");
        var all = new System.Collections.Generic.List<HaloNet> { a, b };
        Action<double> run = s => { double end = now + s; while (now < end) { now += 1.0 / 60.0; foreach (var n in all) n.Tick(); } };
        run(3);
        int zone = HaloNetWire.NameHash("a10");
        a.PublishLobbyState(3, 0, zone, 1, 0, 0, 1 << 4, true, 1, 1760000000);
        foreach (var n in all) { n.CoopMission = true; n.Own.Status = HaloNetStatus.Playing; n.Own.ZoneHash = zone; n.Own.MatchSeq = 1; n.Own.MatchState = HaloMatchState.Running; n.Own.Alive = true; }
        run(0.5);
        a.Coop.NotePress(17, 1, false);
        a.Coop.NotePress(40, 1, true);
        run(0.6);
        check(Sum(b, 17) == 1 && Sum(b, 40) == 1 && Denied(b, 40) && !Denied(b, 17), "presses: B hears control 17 pressed once and control 40 denied once");
        b.Coop.NewPresses.Clear();
        run(0.6);
        check(b.Coop.NewPresses.Count == 0, "presses: nothing twice");
        var c = new HaloNet(w.Join(3, clock), clock, "[C]"); c.CoopMission = true; all.Add(c);
        c.Own.Status = HaloNetStatus.Playing; c.Own.ZoneHash = zone; c.Own.MatchSeq = 1; c.Own.MatchState = HaloMatchState.Running; c.Own.Alive = true;
        run(3);
        check(c.Coop.NewPresses.Count == 0, "presses: a late joiner takes the counts without hearing old presses");
        for (int p = 100; p < 112; p++) a.Coop.NotePress(p, 1, false);      // 12 controls: more than the 10 slots, the oldest are reused
        a.Coop.NotePress(17, 2, false);
        b.Coop.NewPresses.Clear(); c.Coop.NewPresses.Clear();
        run(0.6);
        int heard = 0; for (int p = 100; p < 112; p++) heard += Sum(c, p);
        check(Sum(c, 17) == 2 && heard >= 9, "presses: control 17 pressed twice more reaches the late joiner as 2; " + heard + " of 12 other controls in one write (10 slots)");
        a.Transport.Dispose(); all.Remove(a);
        run(3);
        b.Coop.NewPresses.Clear(); c.Coop.NewPresses.Clear();
        b.Coop.NotePress(17, 1, false);
        run(0.6);
        check(b.Coop.Writing && Sum(c, 17) == 1 && c.Coop.NewPresses.Count == 1, "presses: the new host B continues the counts - C hears its press of 17 once, nothing else");
    }

    static int Sum(HaloNet n, int placement) { int s = 0; foreach (int e in n.Coop.NewPresses) if ((e & 0xFF) == placement) s += (e >> 8) & 7; return s; }
    static bool Denied(HaloNet n, int placement) { bool d = false; foreach (int e in n.Coop.NewPresses) if ((e & 0xFF) == placement) d = (e & (1 << 11)) != 0; return d; }

    public static void WorldWriter(Action<bool, string> check)
    {
        double now = 7000.0;
        Func<double> clock = () => now;
        var w = new HaloLoopbackWorld();
        var a = new HaloNet(w.Join(1, clock), clock, "[A]");
        var b = new HaloNet(w.Join(2, clock), clock, "[B]");
        var c = new HaloNet(w.Join(3, clock), clock, "[C]");
        var all = new System.Collections.Generic.List<HaloNet> { a, b, c };
        int most = 0;
        Action<double> run = s => { double end = now + s; while (now < end) { now += 1.0 / 60.0; int n = 0; foreach (var x in all) { x.Tick(); if (x.World.Writing) n++; } if (n > most) most = n; } };
        run(3);
        int zone = HaloNetWire.NameHash("bloodgulch");
        a.PublishLobbyState(3, 0, zone, 1, 0, 0, 0, true, 1, 1760000000);
        Action<HaloNet> play = x => { x.Own.Status = HaloNetStatus.Playing; x.Own.ZoneHash = zone; x.Own.MatchSeq = a.MasterGameSeq; x.Own.MatchStart = 1760000000; x.Own.MatchState = HaloMatchState.Running; x.Own.Alive = true; };
        run(1);
        foreach (var x in all) play(x);
        run(2);
        check(a.World.Writing && !b.World.Writing && !c.World.Writing && a.WorldOwner && b.WorldWriterId == 1, "writer: the master writes the world while it plays (game " + a.MasterGameSeq + ")");
        a.Own.Kills = 2; a.Own.Score = 2;
        run(0.5);
        int tk0, ts0; c.TeamTotals(a.Own.Team & 1, out tk0, out ts0);
        a.LeaveMatch();      // the master goes to the hub (stays in the room)
        run(2);
        check(b.World.Writing && !a.World.Writing && !c.World.Writing && b.WorldOwner && !a.WorldOwner && c.WorldWriterId == 2, "writer: the master in the hub - B (lowest id in the game) writes the world");
        check(a.Game.On && c.Game.On, "writer: the game stays on (the master still commands it)");
        b.World.SetVehicle(4, false, 0.5f, 2, new HaloV3(10f, 2f, 30f), HaloQ.Identity);
        b.NotePickup(5);
        run(1);
        var d = new HaloNet(w.Join(4, clock), clock, "[D]"); all.Add(d);
        run(3);
        play(d);
        run(1);
        bool des; float hp; int vf; HaloV3 vp; HaloQ vr; int at;
        check(d.World.Vehicle(4, out des, out hp, out vf, out vp, out vr) && vf == 2 && Math.Abs(vp.X - 10f) < 0.05f && d.World.ItemTakenAt(5, out at), "writer: late joiner D reads B's fresh parked vehicle and pickup time");
        play(a);      // the master rejoins
        a.Own.Kills = 1; a.Own.Score = 1;      // (fresh counters after the rejoin, one new kill)
        run(2);
        check(a.World.Writing && !b.World.Writing && a.World.Vehicle(4, out des, out hp, out vf, out vp, out vr) && vf == 2 && a.World.ItemTakenAt(5, out at), "writer: the master back in the game takes the writer role back and continues B's world");
        check(most == 1, "writer: never two writers at once (most at once " + most + ")");
        int tk1, ts1; c.TeamTotals(a.Own.Team & 1, out tk1, out ts1);
        check(tk0 == 2 && tk1 == 3 && ts1 == 3, "score: A's 2 kills stay with its side after it rejoins with fresh counters and makes 1 more (" + tk0 + " -> " + tk1 + ", side " + a.Own.Team + ", C sees " + c.Remotes.Count + ")");
        a.LeaveMatch(); b.LeaveMatch();      // the master and B (the next master) both go to the hub; C and D play on
        int seqBefore = c.Game.Seq;
        run(2);
        int tk2, ts2; c.TeamTotals(1, out tk2, out ts2); int tk2b, ts2b; c.TeamTotals(0, out tk2b, out ts2b); tk2 += tk2b; ts2 += ts2b;
        check(tk2 == 3 && ts2 == 3, "score: A and B in the hub - their kills stay kept for the side (" + tk2 + ")");
        a.Transport.Dispose(); all.Remove(a);      // the master leaves the room from the hub
        run(3);
        check(b.IsInstanceMaster && !b.World.Writing && c.World.Writing && c.Game.On && d.Game.On && d.Game.Seq == seqBefore && d.World.Vehicle(4, out des, out hp, out vf, out vp, out vr), "writer: the master left the room from the hub - B (in the hub) is master and keeps game " + seqBefore + " on, C (lowest in it) writes, D still reads the vehicle");
        check(most == 1, "writer: still never two writers (" + most + ")");
    }
    /// <summary>Runs 0.6 s collecting UseControl events of a placement: [hosts that got it, followers 1, followers 2, slot it came from].</summary>
    static int[] Controls(System.Collections.Generic.List<HaloNet> all, Action<double> run, int placement)
    {
        var r = new int[4];
        r[3] = -1;
        for (int f = 0; f < 36; f++)
        {
            run(1.0 / 60.0);
            int k = 0;
            foreach (var n in all)
            {
                foreach (var e in n.Events)
                {
                    if (e.Kind != HaloNetEventKind.UseControl || e.Value != placement) continue;
                    if (n.Coop.Writing) { r[0]++; r[3] = e.Slot; } else r[1 + Math.Min(k, 1)]++;
                }
                if (!n.Coop.Writing) k++;
            }
        }
        return r;
    }
}

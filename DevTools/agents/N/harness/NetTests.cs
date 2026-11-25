using System;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;

/// <summary>Unity-free tests of Game/Net/Core: the wire, the claim over a Fusion-like loopback, the master, the lobby, the game command, events, world blocks.</summary>
static class NetTests
{
    static int _fails, _oks;
    static double _now;
    static double Clock() { return _now; }

    static void Check(bool ok, string what)
    {
        if (ok) _oks++; else _fails++;
        Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
    }

    static bool Near(float a, float b, float tol) { return Math.Abs(a - b) <= tol; }
    static bool Near(HaloV3 a, HaloV3 b, float tol) { return HaloV3.Distance(a, b) <= tol; }

    public static int Run(string[] a)
    {
        HaloNet.LogSink = s => { if (Verbose) Console.WriteLine("   " + s); };
        Verbose = a.Length > 1 && a[1] == "-v";
        Wire();
        Session();
        NetTests2.LobbyLink(Check);
        NetTests3.Coop(Check);
        NetTests4.Stream(Check);
        NetTests5.VehicleActor(Check);
        NetTests6.LateFirst(Check);
        NetTests7.Sounds(Check);
        NetTests7.Presses(Check);
        NetTests7.WorldWriter(Check);
        Console.WriteLine("net tests: " + _oks + " ok, " + _fails + " failed");
        return _fails == 0 ? 0 : 1;
    }

    static bool Verbose;

    // ------------------------------------------------------------------ wire

    static void Wire()
    {
        var s = HaloNetState.Empty();
        s.Status = HaloNetStatus.Playing; s.Vr = true; s.Alive = true; s.Team = 1; s.MatchState = HaloMatchState.Running; s.Master = true; s.Camo = true; s.Zoomed = true;
        s.Crouch = true; s.Reloading = true; s.GrenadeInHand = true; s.GrenadePlasma = true; s.TriggerHeld = true; s.Overheated = true; s.Carry = 3; s.Color = 7;
        s.Health = 0.62f; s.Shield = 2.4f; s.Kills = 1234; s.Deaths = 4000; s.Assists = 77; s.Score = -12; s.LivesLeft = 3; s.MapPrint = 0xBEEF;
        s.ZoneHash = HaloNetWire.NameHash("bloodgulch"); s.MatchStart = 1760000000; s.Held = 1203; s.Stowed = 77; s.FireCounter = 250; s.ThrowCounter = 9; s.MeleeCounter = 14;
        s.Frags = 4; s.Plasmas = 2; s.Killer = 17; s.DeathCause = 999; s.DeathMelee = true; s.DeathHead = true; s.DeathSplatter = true; s.DeathBetrayal = true;
        s.HitSeq = 201;
        s.SetHit(1, new HaloNetHit { Seq = 201, Victim = 33, Amount = 37.75f, Cause = 1203, Effect = 1500, Head = true, Back = true });
        s.SetHit(0, new HaloNetHit { Seq = 200, Victim = 2, Amount = 900f, Cause = 5, Effect = 6, Melee = true, Instant = true, Explosion = true });
        s.RootYaw = 271.5f; s.Root = new HaloV3(-512.345f, 3001.25f, 87.001f); s.Head = s.Root + new HaloV3(0.1f, 1.7f, -0.05f);
        s.HeadRot = new HaloQ(0.1f, 0.7f, -0.1f, 0.7f); s.LHand = s.Head + new HaloV3(-0.3f, -0.5f, 0.4f); s.RHand = s.Head + new HaloV3(0.31f, -0.45f, 0.55f);
        s.LRot = new HaloQ(0.5f, 0.5f, 0.5f, 0.5f); s.RRot = HaloQ.Identity;
        s.Muzzle = new HaloV3(100.12f, -3.5f, 2000.3f); s.MuzzleDir = new HaloV3(0f, 0.2f, 1f); s.ShotWeapon = 1203;
        s.ThrowPos = new HaloV3(-7000.5f, 12f, 3f); s.ThrowVel = new HaloV3(8.3f, 4.1f, -12.2f); s.ThrowTag = 444;
        s.Vehicle = 12; s.Seat = 0; s.VehicleHealth = 0.5f; s.VehicleFlags = 0xA5; s.VehiclePos = new HaloV3(-300.01f, 20.5f, 150.25f); s.VehicleRot = new HaloQ(0f, 0.38f, 0f, 0.92f);
        s.VehicleVel = new HaloV3(20f, -1f, 30f); s.Aim = new HaloV3(1f, -0.1f, 0.2f);
        s.GameZone = s.ZoneHash; s.GameSeq = 65000; s.MatchSeq = 64999; s.GameStart = 1760000007; s.LobbyPhase = 2; s.LobbyCount = 4; s.LobbyZone = 12345;
        s.RulesVersion = 4000; s.RulesA = -5; s.RulesB = 123456; s.RulesC = 7;
        s.ObjCounter = 255; s.ObjCode = 9; s.ObjPos = new HaloV3(1.5f, 2.5f, -3.5f); s.PickupCounter = 13; s.Pickup0 = 1500; s.Pickup1 = 77;
        s.DropCounter = 11; s.DropWeapon = 333; s.DropRounds = 60; s.DropPos = new HaloV3(4f, 5f, 6f); s.DropVel = new HaloV3(0.5f, 1f, -2f);

        var rec = new int[HaloNetWire.Ints];
        HaloNetWire.WriteClaim(rec, 5, 3, 1000);
        Check(HaloNetWire.OwnerOf(rec) == 5 && HaloNetWire.GenerationOf(rec) == 3 && HaloNetWire.StatusOf(rec) == HaloNetStatus.Present, "claim record: owner, generation, status");
        Check(HaloNetWire.Pack(in s, rec), "pack reports a change");
        Check(!HaloNetWire.Pack(in s, rec), "a second pack of the same state changes nothing");
        HaloNetState d;
        Check(HaloNetWire.Unpack(rec, out d), "unpack accepts the record");
        Check(d.Status == s.Status && d.Vr && d.Alive && d.Team == 1 && d.MatchState == HaloMatchState.Running && d.Master && d.Camo && d.Zoomed && d.Crouch && d.Reloading
              && d.GrenadeInHand && d.GrenadePlasma && d.TriggerHeld && d.Overheated && d.Carry == 3 && d.Color == 7, "flags round trip");
        Check(Near(d.Health, 0.62f, 0.003f) && Near(d.Shield, 2.4f, 0.01f) && d.Kills == 1234 && d.Deaths == 4000 && d.Assists == 77 && d.Score == -12 && d.LivesLeft == 3 && d.MapPrint == 0xBEEF,
              "vitals and counters (health " + d.Health + ", shield " + d.Shield + ", score " + d.Score + ")");
        Check(d.ZoneHash == s.ZoneHash && d.MatchStart == s.MatchStart && d.Held == 1203 && d.Stowed == 77 && d.FireCounter == 250 && d.ThrowCounter == 9 && d.MeleeCounter == 14
              && d.Frags == 4 && d.Plasmas == 2, "map, weapons, counters");
        Check(d.Killer == 17 && d.DeathCause == 999 && d.DeathMelee && d.DeathHead && !d.DeathExplosion && d.DeathSplatter && !d.DeathSuicide && d.DeathBetrayal, "death event");
        HaloNetHit h1 = d.HitAt(1), h0 = d.HitAt(0);
        Check(d.HitSeq == 201 && h1.Seq == 201 && h1.Victim == 33 && Near(h1.Amount, 37.75f, 0.001f) && h1.Cause == 1203 && h1.Effect == 1500 && h1.Head && h1.Back && !h1.Melee,
              "hit 1 (" + h1.Amount + ")");
        Check(h0.Seq == 200 && h0.Victim == 2 && Near(h0.Amount, 900f, 0.001f) && h0.Cause == 5 && h0.Effect == 6 && h0.Melee && h0.Instant && h0.Explosion && !d.HitAt(2).Valid, "hit 0 and an empty ring slot");
        Check(Near(d.RootYaw, 271.5f, 0.01f) && Near(d.Root, s.Root, 0.001f) && Near(d.Head, s.Head, 0.001f) && HaloQ.AngleDeg(d.HeadRot, Norm(s.HeadRot)) < 0.3f, "root, head (mm), head rotation");
        Check(Near(d.LHand, s.LHand, 0.003f) && Near(d.RHand, s.RHand, 0.003f) && HaloQ.AngleDeg(d.LRot, s.LRot) < 0.3f && HaloQ.AngleDeg(d.RRot, s.RRot) < 0.3f,
              "hands (offsets from the head, 2.5 mm): L err " + HaloV3.Distance(d.LHand, s.LHand).ToString("0.0000"));
        Check(Near(d.Muzzle, s.Muzzle, 0.01f) && Near(Dot(d.MuzzleDir, NormV(s.MuzzleDir)), 1f, 1e-4f) && d.ShotWeapon == 1203, "muzzle (cm2) and direction");
        Check(Near(d.ThrowPos, s.ThrowPos, 0.01f) && Near(d.ThrowVel, s.ThrowVel, 0.06f) && d.ThrowTag == 444, "throw");
        Check(d.Vehicle == 12 && d.Seat == 0 && Near(d.VehicleHealth, 0.5f, 0.003f) && d.VehicleFlags == 0xA5 && Near(d.VehiclePos, s.VehiclePos, 0.01f) && HaloQ.AngleDeg(d.VehicleRot, Norm(s.VehicleRot)) < 0.3f
              && Near(d.VehicleVel, s.VehicleVel, 0.06f), "vehicle");
        Check(d.GameZone == s.GameZone && d.GameSeq == 65000 && d.MatchSeq == 64999 && d.GameStart == s.GameStart && d.LobbyPhase == 2 && d.LobbyCount == 4 && d.LobbyZone == 12345, "game command and lobby");
        Check(d.RulesOk && d.RulesVersion == 4000 && d.RulesA == -5 && d.RulesB == 123456 && d.RulesC == 7, "rules with their check");
        Check(d.ObjCounter == 255 && d.ObjCode == 9 && Near(d.ObjPos, s.ObjPos, 0.01f) && d.PickupCounter == 13 && d.Pickup0 == 1500 && d.Pickup1 == 77, "objective event and pickups");
        Check(d.DropCounter == 11 && d.DropWeapon == 333 && d.DropRounds == 60 && Near(d.DropPos, s.DropPos, 0.01f) && Near(d.DropVel, s.DropVel, 0.06f), "drop");
        rec[HaloNetWire.AtRulesB]++;
        HaloNetWire.Unpack(rec, out d);
        Check(!d.RulesOk, "a torn rules block fails its check");
        // monitor player: no hands, no vehicle
        s.Vr = false; s.Vehicle = -1; s.Seat = -1;
        HaloNetWire.Pack(in s, rec);
        HaloNetWire.Unpack(rec, out d);
        Check(!d.Vr && d.Vehicle == -1 && d.Seat == -1 && rec[HaloNetWire.AtLHand] == 0 && rec[HaloNetWire.AtVehiclePos] == 0, "a monitor player on foot sends no hands and no vehicle pose");
        // cm2 range
        HaloV3 far = new HaloV3(-10400f, 9999.99f, 0.01f);
        int lo, hi;
        HaloNetWire.PackCm2(far, out lo, out hi);
        Check(Near(HaloNetWire.UnpackCm2(lo, hi), far, 0.011f), "cm2 holds +-10 km");
        Check(HaloNetWire.Used <= HaloNetWire.Ints && HaloNetWorld.Payload == 228, "layout sizes");
    }

    static HaloQ Norm(HaloQ q) { float l = (float)Math.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W); return new HaloQ(q.X / l, q.Y / l, q.Z / l, q.W / l); }
    static HaloV3 NormV(HaloV3 v) { float l = v.Length; return new HaloV3(v.X / l, v.Y / l, v.Z / l); }
    static float Dot(HaloV3 a, HaloV3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }

    // ------------------------------------------------------------------ session

    sealed class Client
    {
        public HaloNet Net;
        public HaloLoopbackTransport T;
        public readonly List<HaloNetEvent> Seen = new List<HaloNetEvent>();
        public void Tick() { Net.Tick(); Seen.AddRange(Net.Events); }
        public int Count(HaloNetEventKind k) { int n = 0; foreach (var e in Seen) if (e.Kind == k) n++; return n; }
        public HaloNetEvent Last(HaloNetEventKind k) { HaloNetEvent r = default(HaloNetEvent); foreach (var e in Seen) if (e.Kind == k) r = e; return r; }
    }

    static Client Join(HaloLoopbackWorld w, int id, string label)
    {
        var c = new Client();
        c.T = w.Join(id, Clock);
        c.Net = new HaloNet(c.T, Clock, label);
        return c;
    }

    static void Run(List<Client> cs, double seconds)
    {
        double end = _now + seconds;
        while (_now < end)
        {
            _now += 1.0 / 60.0;
            foreach (var c in cs) c.Tick();
        }
    }

    static void Play(Client c, int zone, int seq, int start)
    {
        c.Net.Own.Status = HaloNetStatus.Playing;
        c.Net.Own.MatchState = HaloMatchState.Running;
        c.Net.Own.ZoneHash = zone;
        c.Net.Own.MatchSeq = seq;
        c.Net.Own.MatchStart = start;
        c.Net.Own.MapPrint = 0x1234;
        c.Net.Own.Alive = true;
        c.Net.Own.Health = 1f;
    }

    static void Session()
    {
        _now = 100.0;
        HaloNet.UtcClock = () => 1760000000.0 + _now;
        var w = new HaloLoopbackWorld();
        var cs = new List<Client>();
        var A = Join(w, 1, "[A]"); cs.Add(A);
        Run(cs, 3.0);
        Check(A.Net.Claimed && A.Net.MySlot == 1 % HaloNet.MaxSlots, "A claims a slot alone (slot " + A.Net.MySlot + ")");
        Check(A.Net.IsInstanceMaster && A.Net.MasterId == 1, "A alone is the instance master");
        var B = Join(w, 2, "[B]"); cs.Add(B);
        var C = Join(w, 3, "[C]"); cs.Add(C);
        Run(cs, 3.0);
        Check(B.Net.Claimed && C.Net.Claimed && B.Net.MySlot != A.Net.MySlot && C.Net.MySlot != B.Net.MySlot && C.Net.MySlot != A.Net.MySlot, "B and C claim their own slots (" + B.Net.MySlot + ", " + C.Net.MySlot + ")");
        Check(B.Net.MasterId == 1 && C.Net.MasterId == 1 && !B.Net.IsInstanceMaster, "everybody agrees A (the room master) is the instance master");
        Check(A.Net.Listed.Count == 2 && B.Net.Listed.Count == 2, "every client lists the two others");
        Check(A.Net.HasRecord(2) && !A.Net.HasRecord(9), "HasRecord: B yes, an unknown player no");
        int sides = (A.Net.Own.Team & 1) + (B.Net.Own.Team & 1) + (C.Net.Own.Team & 1);
        Check(sides == 1 || sides == 2, "lobby sides are balanced 2 : 1 (" + A.Net.Own.Team + B.Net.Own.Team + C.Net.Own.Team + ")");

        // the lobby: map, rules, countdown, game
        int zone = HaloNetWire.NameHash("bloodgulch");
        Check(!B.Net.LobbyOpen(zone), "a non-master cannot open the lobby");
        Check(A.Net.LobbyOpen(zone), "the master opens the lobby");
        A.Net.PublishRules(11, 22, 33);
        Run(cs, 0.5);
        Check(B.Net.Lobby.Open && B.Net.Lobby.ZoneHash == zone && B.Net.Lobby.RulesOk && B.Net.Lobby.RulesA == 11 && B.Net.Lobby.RulesC == 33 && B.Net.Lobby.MasterId == 1,
              "B reads the master's lobby and rules (version " + B.Net.Lobby.Version + ")");
        int v1 = A.Net.Lobby.Version;
        A.Net.PublishRules(11, 22, 33);
        Check(A.Net.Lobby.Version == v1, "publishing the same rules again does not bump the version");
        A.Net.PublishRules(12, 22, 33);
        Check(A.Net.Lobby.Version == v1 + 1, "changed rules bump the version");
        Check(A.Net.LobbyStart(5), "the master starts the countdown");
        bool started = false;
        double t0 = _now;
        int seenCount = 0;
        while (!started && _now - t0 < 8.0)
        {
            _now += 1.0 / 60.0;
            foreach (var c in cs) c.Tick();
            if (B.Net.Lobby.Phase == 2) seenCount = Math.Max(seenCount, B.Net.Lobby.Count);
            started = A.Net.LobbyStep();
        }

        Check(started && Math.Abs(_now - t0 - 5.0) < 0.1, "the countdown ends after 5 s (" + (_now - t0).ToString("0.00") + ")");
        Check(seenCount == 5, "B saw the countdown from 5");
        int startUtc = (int)HaloNet.UtcClock();
        int seq = A.Net.StartGame(zone, startUtc);
        Check(seq == 1, "game 1 commanded");
        Run(cs, 0.3);
        Check(B.Net.Game.On && B.Net.Game.Seq == 1 && B.Net.Game.ZoneHash == zone && B.Net.Game.Start == startUtc, "B reads the master's game command with its start");
        Play(A, zone, seq, startUtc); Play(B, zone, seq, startUtc);
        A.Net.TeamGame = B.Net.TeamGame = C.Net.TeamGame = true;
        Run(cs, 1.0);
        Check(A.Net.Remotes.Count == 1 && B.Net.Remotes.Count == 1 && A.Count(HaloNetEventKind.Joined) == 1, "A and B are in the same match, C (hub) is not");
        Check(C.Net.Remotes.Count == 0 && C.Net.Listed.Count == 2, "C lists both but plays with nobody");

        // late join: C plays the same game
        Play(C, zone, seq, startUtc);
        Run(cs, 1.0);
        Check(A.Net.Remotes.Count == 2 && C.Net.Remotes.Count == 2, "C (late) is in the match with both");

        // shots, throws, melee
        B.Net.NoteShot(500, new HaloV3(1, 2, 3), new HaloV3(0, 0, 1));
        B.Net.NoteShot(500, new HaloV3(1, 2, 3), new HaloV3(0, 0, 1));
        B.Net.NoteThrow(600, new HaloV3(4, 5, 6), new HaloV3(1, 2, 3));
        B.Net.NoteMelee();
        Run(cs, 0.5);
        HaloNetEvent shot = A.Last(HaloNetEventKind.Shot);
        Check(A.Count(HaloNetEventKind.Shot) >= 1 && shot.Tag == 500 && Near(shot.Pos, new HaloV3(1, 2, 3), 0.01f), "A sees B's shots (" + A.Count(HaloNetEventKind.Shot) + " event(s), count " + shot.Count + ")");
        Check(A.Count(HaloNetEventKind.Throw) == 1 && A.Last(HaloNetEventKind.Throw).Tag == 600 && A.Count(HaloNetEventKind.Melee) == 1, "A sees B's throw and melee once");

        // hits: B hits A three times quickly (merged), from the other side, then a team mate hit is filtered
        int bTeam = B.Net.Own.Team, aTeam = A.Net.Own.Team;
        Client enemyOfA = (bTeam & 1) != (aTeam & 1) ? B : C;
        Client mateOfA = enemyOfA == B ? C : B;
        if ((mateOfA.Net.Own.Team & 1) != (aTeam & 1)) mateOfA = null;
        enemyOfA.Net.NoteHit(A.Net.MySlot, 10f, 500, 501, false, false, false, false, false);
        enemyOfA.Net.NoteHit(A.Net.MySlot, 12.5f, 500, 501, false, true, false, false, false);
        enemyOfA.Net.NoteHit(A.Net.MySlot, 30f, 600, 601, false, false, true, false, false);
        Run(cs, 0.5);
        int hits = A.Count(HaloNetEventKind.HitMe);
        float sum = 0f; foreach (var e in A.Seen) if (e.Kind == HaloNetEventKind.HitMe) sum += e.Amount;
        Check(hits == 2 && Near(sum, 52.5f, 0.01f), "A applies the enemy's hits once: " + hits + " hit(s), " + sum + " points (two of one cause merged)");
        if (mateOfA != null)
        {
            mateOfA.Net.NoteHit(A.Net.MySlot, 50f, 500, 501, false, false, false, false, false);
            Run(cs, 0.5);
            Check(A.Count(HaloNetEventKind.HitMe) == hits, "a team mate's hit is ignored (no friendly fire)");
        }

        // death
        A.Net.NoteDeath(enemyOfA.Net.MySlot, 500, false, true, false, false, false, false, false);
        A.Net.Own.Alive = false;
        Run(cs, 0.5);
        HaloNetEvent death = enemyOfA.Last(HaloNetEventKind.Death);
        Check(enemyOfA.Count(HaloNetEventKind.Death) == 1 && death.Slot == A.Net.MySlot && death.Other == enemyOfA.Net.MySlot && death.Tag == 500 && death.Head, "the killer sees A's death with itself as the killer");
        int before = A.Count(HaloNetEventKind.HitMe);
        enemyOfA.Net.NoteHit(A.Net.MySlot, 10f, 500, 501, false, false, false, false, false);
        Run(cs, 0.5);
        Check(A.Count(HaloNetEventKind.HitMe) == before, "a dead player is not hit");
        A.Net.Own.Alive = true;

        // objective, pickups, drops
        B.Net.NoteObjective(4, 3, new HaloV3(9, 8, 7));
        B.Net.NotePickup(5); B.Net.NotePickup(6); B.Net.NotePickup(7);
        int dropId = B.Net.NoteDrop(321, 42, 120, 0.5f, new HaloV3(1, 1, 1), new HaloV3(0, 1, 0));
        Run(cs, 1.0);
        int picks = 0; var items = new List<int>();
        foreach (var e in C.Seen) if (e.Kind == HaloNetEventKind.Pickup && e.Slot == B.Net.MySlot) { picks++; items.Add(e.Value); }
        Check(C.Count(HaloNetEventKind.Objective) == 1 && C.Last(HaloNetEventKind.Objective).Value == 4 && C.Last(HaloNetEventKind.Objective).Other == 3, "C sees B's objective event");
        Check(picks == 3 && items[0] == 5 && items[1] == 6 && items[2] == 7, "C sees B's three pickups in order (" + string.Join(",", items) + ")");
        HaloNetEvent drop = C.Last(HaloNetEventKind.Drop);
        Check(C.Count(HaloNetEventKind.Drop) == 1 && drop.Tag == 321 && drop.Value == 42 && drop.Count == 120 && Math.Abs(drop.Amount - 0.5f) < 0.01f && drop.Other == dropId, "C sees B's drop with the id B was given (" + dropId + ")");

        // world blocks: the master (A) records the pickups; it writes an objective and a parked vehicle
        int taken = -1;
        Check(A.Net.World.Writing && A.Net.World.ItemTakenAt(6, out taken), "the master recorded B's pickup 6 (at " + taken + " s)");
        A.Net.World.SetObjective(1, 2, HaloWorldNoCarrier, 30, 1, new HaloV3(10, 20, 30));
        A.Net.World.SetVehicle(3, false, 0.75f, 5, new HaloV3(-50, 2, 70), new HaloQ(0, 0.7071f, 0, 0.7071f));
        A.Net.World.SetMisc(0, 4);
        Run(cs, 1.0);
        int st, car, tm, fl; HaloV3 op;
        Check(C.Net.World.Objective(1, out st, out car, out tm, out fl, out op) && st == 2 && car == -1 && tm == 30 && fl == 1 && Near(op, new HaloV3(10, 20, 30), 0.01f), "C reads objective 1 from the world blocks");
        bool des; float hp; int vf; HaloV3 vp; HaloQ vr;
        Check(C.Net.World.Vehicle(3, out des, out hp, out vf, out vp, out vr) && !des && Near(hp, 0.75f, 0.005f) && vf == 5 && Near(vp, new HaloV3(-50, 2, 70), 0.01f), "C reads parked vehicle 3");
        int misc;
        Check(C.Net.World.Misc(0, out misc) && misc == 4 && C.Net.World.ItemTakenAt(7, out taken) && !C.Net.World.ItemTakenAt(9, out taken), "C reads misc and item times (7 taken, 9 never)");

        // allocation: a steady tick allocates nothing
        Run(cs, 1.0);
        long b0 = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
        {
            _now += 1.0 / 60.0;
            B.Net.Own.Root = new HaloV3((float)Math.Sin(_now), 0, 0);
            foreach (var c in cs) { c.Net.Tick(); }
        }

        long alloc = GC.GetAllocatedBytesForCurrentThread() - b0;
        Check(alloc < 2048, "300 steady ticks of 3 clients allocate " + alloc + " bytes");

        // interpolation: B moves at 1 m/s along x; A draws it 0.16 s behind and smoothly
        var remB = A.Net.RemoteAt(B.Net.MySlot);
        float maxJump = 0f, lastX = float.NaN;
        for (int i = 0; i < 180; i++)
        {
            _now += 1.0 / 60.0;
            B.Net.Own.Root = new HaloV3((float)(_now - 100.0), 0, 0);
            foreach (var c in cs) c.Net.Tick();
            HaloNetTimeline.Sample smp;
            if (remB.Timeline.Evaluate(_now, out smp))
            {
                if (!float.IsNaN(lastX) && i > 60) maxJump = Math.Max(maxJump, Math.Abs(smp.Root.X - lastX));
                lastX = smp.Root.X;
            }
        }

        float lag = (float)(_now - 100.0) - lastX;
        Check(maxJump < 0.04f && lag > 0.1f && lag < 0.35f, "interpolated remote pose: steps of at most " + maxJump.ToString("0.000") + " m a frame, " + lag.ToString("0.000") + " s behind");

        // master hand-over: A leaves; B (lowest id) becomes master and keeps commanding the running game and the world state
        A.T.Dispose();
        cs.Remove(A);
        Run(cs, 3.0);
        Check(B.Net.IsInstanceMaster && C.Net.MasterId == 2, "A left: B is the instance master on both clients");
        Check(C.Net.Game.On && C.Net.Game.Seq == 1 && C.Net.Game.ZoneHash == zone, "the new master keeps commanding the running game");
        Check(C.Net.World.Objective(1, out st, out car, out tm, out fl, out op) && st == 2 && C.Net.World.Vehicle(3, out des, out hp, out vf, out vp, out vr), "the world state survives the hand-over (written by B now)");
        Check(C.Net.Lobby.Open && C.Net.Lobby.ZoneHash == zone && C.Net.Lobby.RulesOk && C.Net.Lobby.RulesA == 12, "the lobby and rules survive the hand-over");
        int k0, s0, k1, s1;
        B.Net.TeamTotals(0, out k0, out s0); B.Net.TeamTotals(1, out k1, out s1);
        Check(true, "team totals after A left: red " + k0 + " kills, blue " + k1);

        // leaving the match keeps the counters; the next game is a new game
        B.Net.LeaveMatch();
        Run(cs, 0.5);
        Check(C.Net.Remotes.Count == 0 && B.Net.Own.Status == HaloNetStatus.Present, "B left the match: C plays alone");
        int seq2 = B.Net.StartGame(zone, (int)HaloNet.UtcClock());
        Check(seq2 == 2, "the new master's next game is number 2");

        // world blocks absent: everything else still works
        var w2 = new HaloLoopbackWorld { WorldBlocks = 0 };
        var cs2 = new List<Client>();
        var X = Join(w2, 1, "[X]"); var Y = Join(w2, 2, "[Y]"); cs2.Add(X); cs2.Add(Y);
        Run(cs2, 3.0);
        X.Net.StartGame(zone, 5);
        Run(cs2, 0.5);
        Play(X, zone, 1, 5); Play(Y, zone, 1, 5);
        Run(cs2, 1.0);
        X.Net.World.SetVehicle(0, false, 1, 0, new HaloV3(1, 1, 1), HaloQ.Identity);
        Run(cs2, 1.0);
        Check(X.Net.Remotes.Count == 1 && !Y.Net.World.Vehicle(0, out des, out hp, out vf, out vp, out vr), "without world blocks: the match works, parked vehicles are simply not shared");
    }

    const int HaloWorldNoCarrier = -1;
}

static class NetTests2
{
    public static void LobbyLink(System.Action<bool, string> check)
    {
        double now = 500.0;
        System.Func<double> clock = () => now;
        var w = new HaloLoopbackWorld();
        var a = new HaloNet(w.Join(4, clock), clock, "[A]");
        var b = new HaloNet(w.Join(6, clock), clock, "[B]");
        System.Action run = () => { for (int i = 0; i < 180; i++) { now += 1.0 / 60.0; a.Tick(); b.Tick(); } };
        run();
        int zone = HaloNetWire.NameHash("chillout");
        a.PublishLobbyState(1, 0, zone, 7, 1, 2, 3, false, 0, 0);
        run();
        check(b.Lobby.Phase == 1 && b.Lobby.RulesOk && b.Lobby.RulesA == 1 && !b.Game.On, "lobby link: B reads the master's lobby (no game yet)");
        a.PublishLobbyState(2, 4, zone, 8, 1, 2, 3, false, 0, 0);
        run();
        check(b.Lobby.Phase == 2 && b.Lobby.Count == 4 && b.Lobby.Version != 0, "lobby link: the countdown");
        a.PublishLobbyState(3, 0, zone, 9, 1, 2, 3, true, 5, 0);
        run();
        check(b.Game.On && b.Game.Seq == 5 && b.Game.Start == 0 && b.Game.ZoneHash == zone, "lobby link: in a match, its number, the start not known yet");
        a.Own.Status = HaloNetStatus.Playing; a.Own.MatchState = HaloMatchState.Running; a.Own.ZoneHash = zone; a.Own.MatchSeq = 5; a.Own.Alive = true;
        run();
        a.World.SetMisc(2, 77);
        run();
        a.PublishLobbyState(3, 0, zone, 10, 1, 2, 3, true, 5, 1760000123);
        run();
        int m;
        check(b.Game.Start == 1760000123 && b.World.Misc(2, out m) && m == 77, "lobby link: the start arrives later and the world state stays (same game)");
        a.PublishLobbyState(1, 0, zone, 11, 1, 2, 3, false, 5, 0);
        run();
        check(!b.Game.On && b.MasterGameSeq == 5 && b.Lobby.Phase == 1, "lobby link: back in the lobby, the game closed, its number kept");
    }
}

static class NetTests3
{
    public static void Coop(System.Action<bool, string> check)
    {
        double now = 900.0;
        System.Func<double> clock = () => now;
        var w = new HaloLoopbackWorld();
        var a = new HaloNet(w.Join(1, clock), clock, "[host]");
        var b = new HaloNet(w.Join(2, clock), clock, "[B]");
        var list = new System.Collections.Generic.List<HaloNet> { a, b };
        var seen = new System.Collections.Generic.List<HaloNetEvent>();
        System.Action<double> run = s => { double end = now + s; while (now < end) { now += 1.0 / 60.0; foreach (var n in list) { n.Tick(); if (n == a) seen.AddRange(a.Events); } } };
        run(3);
        int zone = HaloNetWire.NameHash("a10");
        a.PublishLobbyState(3, 0, zone, 1, 0, 0, 1 << 4, true, 1, 1760000000);
        a.CoopMission = b.CoopMission = true;
        run(0.5);
        check(a.Coop.Writing && !b.Coop.Writing, "coop: the master hosts, B reads");
        var act = new HaloNetActor { Serial = 17, Team = 3, Alive = true, UnitTag = 1200, VariantTag = 1300, WeaponTag = 900, Position = new HaloV3(10.5f, -2.25f, 300f),
            Velocity = new HaloV3(1, 0, -2), BodyYaw = 90f, AimYaw = 120f, AimPitch = -15f, Body = 0.5f, Shield = 1f, FireCounter = 7, Crouch = true, Firing = true, SeatVehicle = 37, Seat = 3, Exiting = true };
        a.Coop.SetActor(40, act);
        a.Coop.SetMission(2, 3, true, true, 41, 5);
        a.Coop.AddEvent(HaloNetMissionEvent.Create, 123);
        a.Coop.AddEvent(HaloNetMissionEvent.Destroy, 77);
        a.Coop.AddEvent(HaloNetMissionEvent.Voice, 17 | (2345 << 12));
        a.Coop.SetDevice(200, 0.5f);
        run(0.6);
        HaloNetActor r = b.Coop.Actor(40);
        check(r.Serial == 17 && r.Team == 3 && r.Alive && r.UnitTag == 1200 && r.VariantTag == 1300 && r.WeaponTag == 900 && HaloV3.Distance(r.Position, act.Position) < 0.011f
              && System.Math.Abs(r.BodyYaw - 90f) < 0.01f && System.Math.Abs(r.AimPitch + 15f) < 0.01f && System.Math.Abs(r.Body - 0.5f) < 0.005f && r.FireCounter == 7 && r.Crouch && r.Firing && r.SeatVehicle == 37 && r.Seat == 3 && r.Exiting,
              "coop: B reads actor 40 (serial 17) with every field");
        check(r.BodyState == 0 && !r.CamoHidden && HaloV3.Distance(r.Velocity, act.Velocity) < 0.06f, "coop: no body state, the velocity intact");
        {
            // body states (HaloUnit -> puppet): feigning carries its death clip instead of the velocity; latched / leaping keep the velocity;
            // camouflage in the position's free bit; nothing else moves
            var o = new int[16];
            var f = act; f.SeatVehicle = -1; f.Seat = -1; f.Exiting = false; f.BodyState = HaloNetActor.BodyFeigning; f.CamoHidden = true; f.FeignHard = true; f.FeignDir = 3; f.FeignRegion = 10;
            f.Position = new HaloV3(-10000f, -5000f, -10000f);
            HaloNetCoop.PackActor(f, o, 8);
            var g = HaloNetCoop.UnpackActor(o, 8);
            check(g.BodyState == HaloNetActor.BodyFeigning && g.CamoHidden && g.FeignHard && g.FeignDir == 3 && g.FeignRegion == 10 && g.Velocity.X == 0f && g.Velocity.Z == 0f
                  && HaloV3.Distance(g.Position, f.Position) < 0.011f && g.Serial == 17 && g.WeaponTag == 900 && g.VariantTag == 1300 && g.Crouch && g.Firing,
                  "coop: a feigning, camouflaged unit (hard, from behind, region 10) round-trips; position at -10000 untouched by the camo bit");
            for (int st = 2; st <= 3; st++)
            {
                f.BodyState = st; f.CamoHidden = st == 3; f.Velocity = new HaloV3(-51.1f, 51.1f, 3.3f);
                HaloNetCoop.PackActor(f, o, 8);
                g = HaloNetCoop.UnpackActor(o, 8);
                check(g.BodyState == st && g.CamoHidden == (st == 3) && HaloV3.Distance(g.Velocity, f.Velocity) < 0.06f && HaloV3.Distance(g.Position, f.Position) < 0.011f,
                      "coop: body state " + (st == 2 ? "latched" : "leaping") + " round-trips with the full velocity range");
            }

            var lr = act; lr.BodyState = HaloNetActor.BodyLatched; lr.CamoHidden = true;
            a.Coop.SetActor(41, lr);
            run(0.3);
            HaloNetActor r2 = b.Coop.Actor(41);
            check(r2.Serial == 17 && r2.BodyState == HaloNetActor.BodyLatched && r2.CamoHidden && r2.SeatVehicle == 37, "coop: B reads a latched, camouflaged actor over the wire");
            a.Coop.ClearActor(41);
        }
        check(b.Coop.HaveMission && b.Coop.Difficulty == 2 && b.Coop.Bsp == 3 && b.Coop.Cutscene && b.Coop.Letterbox && b.Coop.CameraPoint == 41 && b.Coop.Checkpoints == 5
              && System.Math.Abs(b.Coop.Device(200) - 0.5f) < 0.005f, "coop: B reads the mission state and a device position");
        check(b.Coop.NewEvents.Count == 3 && b.Coop.NewEvents[0].Kind == 1 && b.Coop.NewEvents[0].Value == 123 && b.Coop.NewEvents[1].Value == 77 && b.Coop.NewEvents[2].Kind == HaloNetMissionEvent.Voice && b.Coop.NewEvents[2].Value == (17 | (2345 << 12)), "coop: B gets the three events once, in order (a voice: serial 17, sound 2345)");
        b.Coop.NewEvents.Clear();
        run(0.5);
        check(b.Coop.NewEvents.Count == 0, "coop: no event twice");
        // B hits the actor; the host hits B as the AI
        b.NoteHitActor(17, 12.5f, 900, false, true, false, false, false);
        a.Own.Status = HaloNetStatus.Playing;
        b.Own.Status = HaloNetStatus.Playing; b.Own.Alive = true; b.Own.ZoneHash = zone; b.Own.MatchSeq = 1; b.Own.MatchState = HaloMatchState.Running;
        a.Own.ZoneHash = zone; a.Own.MatchSeq = 1; a.Own.MatchState = HaloMatchState.Running; a.Own.Alive = true;
        run(0.5);
        b.NoteHitActor(17, 12.5f, 900, false, true, false, false, false);
        a.NoteHit(b.MySlot, 30f, 901, 902, false, false, false, false, false, true);
        var bseen = new System.Collections.Generic.List<HaloNetEvent>();
        double end2 = now + 0.6; while (now < end2) { now += 1.0 / 60.0; a.Tick(); seen.AddRange(a.Events); b.Tick(); bseen.AddRange(b.Events); }
        int ha = 0; float amt = 0; foreach (var e in seen) if (e.Kind == HaloNetEventKind.HitActor && e.Value == 17) { ha++; amt += e.Amount; }
        int hb = 0; bool ai = false; foreach (var e in bseen) if (e.Kind == HaloNetEventKind.HitMe) { hb++; ai = e.Ai; }
        check(ha >= 1 && amt >= 12.5f, "coop: the host gets B's hit on actor 17 (" + ha + " hit(s), " + amt + " points)");
        check(hb == 1 && ai, "coop: B gets the AI's hit once, marked as the AI's");
        // a late joiner replays the ring; a host hand-over keeps the actors and the ring
        var c = new HaloNet(w.Join(3, clock), clock, "[C]"); c.CoopMission = true; list.Add(c);
        run(3);
        check(c.Coop.Actor(40).Serial == 17 && c.Coop.NewEvents.Count == 2, "coop: the late joiner reads the actors and the ring's two object events (not the old voice line)");
        a.Transport.Dispose(); list.Remove(a);
        run(3);
        check(b.Coop.Writing && b.Coop.Actor(40).Serial == 17, "coop: the master left - B hosts and keeps the actors");
        b.Coop.AddEvent(HaloNetMissionEvent.Create, 5);
        c.Coop.NewEvents.Clear();
        run(1);
        check(c.Coop.NewEvents.Count == 1 && c.Coop.NewEvents[0].Value == 5, "coop: the new host's event continues the ring (C gets exactly it)");
    }
}

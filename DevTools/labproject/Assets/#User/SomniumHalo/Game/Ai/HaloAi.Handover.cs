using System;
using System.IO;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] Co-op host hand-over (Net's HaloNetHostState part "ai", order 30): the host captures the encounters' state (created,
    /// team, switches, follow, squads' timers / counts / respawns, platoons' attacking / defending) and every live actor keyed by its unit's
    /// net serial (encounter, squad, states, home, target memory, firing position, command list position); a promoted follower adopts it on
    /// the units Net already turned back into its own (HaloNetGame.UnitOfSerial) and the AI goes on. No blob: every live AI unit joins the
    /// nearest encounter of its team.
    /// </summary>
    public static partial class HaloAi
    {
        const int HandoverVersion = 2;
        /// <summary>Labs: the serial of a unit / the unit of a serial (default: Net's HaloNetGame.SerialOf / UnitOfSerial).</summary>
        public static Func<HaloUnit, int> SerialOfUnit = u => HaloNetGame.SerialOf(u);
        public static Func<int, HaloUnit> UnitOfSerialFn = s => HaloNetGame.UnitOfSerial(s);
        static bool _netRegistered;

        /// <summary>Registers the "ai" part with Net (called when the AI data loads; harmless offline).</summary>
        static void RegisterHandover()
        {
            if (_netRegistered) return;
            try { HaloNetHostState.Register("ai", 30, CaptureNet, AdoptNet); _netRegistered = true; }
            catch (Exception e) { HaloLog.Warn("AI: host hand-over registration failed: " + e.Message); }
        }

        /// <summary>Followers: load the AI data now so a hand-over can adopt (Net / K may call it on a co-op client).</summary>
        public static bool Prepare() { bool ok = Ensure(); if (ok) RegisterHandover(); return ok; }

        public static byte[] CaptureNet()
        {
            if (Data == null) return null;
            var ms = new MemoryStream(4096);
            var w = new BinaryWriter(ms);
            w.Write(HandoverVersion);
            w.Write(Data.Encounters.Count);
            for (int i = 0; i < Encounters.Count; i++)
            {
                var e = Encounters[i];
                w.Write(e.Created); w.Write((int)e.Team);
                int f = (e.Blind ? 1 : 0) | (e.Deaf ? 2 : 0) | (e.Braindead ? 4 : 0) | (e.Respawn ? 8 : 0) | (e.ForceActive ? 16 : 0) | (e.Playfight ? 32 : 0) | (e.EncFollowPlayers ? 64 : 0) | (e.ManeuverEnabled ? 128 : 0);
                w.Write(f); w.Write(e.FollowDistance); w.Write(Time - e.LastGrenade);
                w.Write(e.Squads.Length);
                for (int k = 0; k < e.Squads.Length; k++)
                {
                    var s = e.Squads[k];
                    w.Write(s.Placed); w.Write(s.Killed); w.Write(s.TimerStarted); w.Write(s.TimerExpired);
                    w.Write(s.TimerEnds >= 0f ? s.TimerEnds - Time : -1f); w.Write(s.RespawnsLeft); w.Write(s.AutomaticMigrationTarget); w.Write(s.HasFollow); w.Write(s.FollowPlayers);
                }
                w.Write(e.Platoons.Length);
                for (int k = 0; k < e.Platoons.Length; k++) { var p = e.Platoons[k]; w.Write(p.Defending); w.Write(p.Changed); w.Write(p.Maneuvered); w.Write(p.ManeuverEnabled); }
            }
            int count = 0; long countAt = ms.Position; w.Write(0);
            for (int i = 0; i < Actors.Count; i++)
            {
                var a = Actors[i];
                if (!a.Alive || a.Unit == null) continue;
                int serial = SerialOfUnit(a.Unit);
                if (serial == 0) continue;
                count++;
                w.Write(serial);
                w.Write(a.Enc != null ? a.Enc.Index : -1); w.Write(a.Squad);
                w.Write(a.V != null ? a.V.Tag.Id : 0xFFFFFFFFu);
                w.Write((int)a.InitialState); w.Write((int)a.ReturnState); w.Write((int)a.State);
                w.Write(a.Home.x); w.Write(a.Home.y); w.Write(a.Home.z); w.Write(a.HomeYaw);
                int flags = (a.Braindead ? 1 : 0) | (a.Blind ? 2 : 0) | (a.Deaf ? 4 : 0) | (a.Free ? 8 : 0) | (a.Berserking ? 16 : 0) | (a.FightPlayer ? 32 : 0) | (a.MeleeInhibited ? 64 : 0) | (a.RangedInhibited ? 128 : 0);
                w.Write(flags);
                // target memory: a unit serial, -1 a player, 0 none
                int target = 0;
                if (a.Target != null) target = a.Target.IsPlayer ? -1 : SerialOfUnit(a.Target.Unit);
                w.Write(target);
                w.Write(a.TargetLastSeen.x); w.Write(a.TargetLastSeen.y); w.Write(a.TargetLastSeen.z); w.Write(Time - a.TargetSeenAt);
                w.Write(a.FiringPosition);
                w.Write(a.Commands != null && a.Commands.Active && a.Commands.List != null ? a.Commands.List.Index : -1);
                w.Write(a.Commands != null ? a.Commands.Index : 0);
                w.Write(a.FightAi ?? "");
            }
            long end = ms.Position;
            ms.Position = countAt; w.Write(count); ms.Position = end;
            w.Flush();
            return ms.ToArray();
        }

        public static void AdoptNet(byte[] blob)
        {
            Enabled = true;
            if (!Ensure()) { HaloLog.Warn("AI: hand-over without AI data (no map)"); return; }
            _initialPlaced = true;                 // the old host placed them; never again
            int adopted = 0, missing = 0;
            if (blob != null && blob.Length > 8)
            {
                try
                {
                    var r = new BinaryReader(new MemoryStream(blob));
                    if (r.ReadInt32() != HandoverVersion) throw new InvalidDataException("version");
                    int ne = r.ReadInt32();
                    for (int i = 0; i < ne; i++)
                    {
                        var e = i < Encounters.Count ? Encounters[i] : null;
                        bool created = r.ReadBoolean(); int team = r.ReadInt32(); int f = r.ReadInt32(); float fd = r.ReadSingle(); float sinceGrenade = r.ReadSingle();
                        if (e != null)
                        {
                            e.Created = created; e.Team = (HaloTeam)team; e.Blind = (f & 1) != 0; e.Deaf = (f & 2) != 0; e.Braindead = (f & 4) != 0; e.Respawn = (f & 8) != 0;
                            e.ForceActive = (f & 16) != 0; e.Playfight = (f & 32) != 0; e.SetFollow(-1, (f & 64) != 0, null, null); e.ManeuverEnabled = (f & 128) != 0;
                            e.FollowDistance = fd; e.LastGrenade = Time - sinceGrenade;
                        }
                        int ns = r.ReadInt32();
                        for (int k = 0; k < ns; k++)
                        {
                            int placed = r.ReadInt32(), killed = r.ReadInt32(); bool ts = r.ReadBoolean(), te = r.ReadBoolean(); float left = r.ReadSingle(); int resp = r.ReadInt32(); bool amt = r.ReadBoolean(); bool hf = r.ReadBoolean(), fp = r.ReadBoolean();
                            if (e == null || k >= e.Squads.Length) continue;
                            var s = e.Squads[k];
                            s.Placed = placed; s.Killed = killed; s.TimerStarted = ts; s.TimerExpired = te; s.TimerEnds = left >= 0f ? Time + left : -1f; s.RespawnsLeft = resp; s.AutomaticMigrationTarget = amt;
                            if (hf) e.SetFollow(k, fp, null, null);
                        }
                        int np = r.ReadInt32();
                        for (int k = 0; k < np; k++)
                        {
                            bool d = r.ReadBoolean(), c = r.ReadBoolean(), m = r.ReadBoolean(), me = r.ReadBoolean();
                            if (e == null || k >= e.Platoons.Length) continue;
                            var p = e.Platoons[k]; p.Defending = d; p.Changed = c; p.Maneuvered = m; p.ManeuverEnabled = me;
                        }
                    }
                    int na = r.ReadInt32();
                    for (int i = 0; i < na; i++)
                    {
                        int serial = r.ReadInt32(), ei = r.ReadInt32(), squad = r.ReadInt32(); uint vid = r.ReadUInt32();
                        var init = (HaloAiState)r.ReadInt32(); var ret = (HaloAiState)r.ReadInt32(); var mode = (HaloActor.Mode)r.ReadInt32();
                        var home = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); float homeYaw = r.ReadSingle();
                        int flags = r.ReadInt32(); int target = r.ReadInt32();
                        var seen = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); float seenAgo = r.ReadSingle();
                        int fpos = r.ReadInt32(); int cl = r.ReadInt32(); int ci = r.ReadInt32(); string fight = r.ReadString();
                        var u = UnitOfSerialFn(serial);
                        if (u == null || !u.Alive) { missing++; continue; }
                        var v = Catalog.Variant(Map.Tag(vid)) ?? Catalog.Variant(u.ActorVariant);
                        if (v == null) { missing++; continue; }
                        var a = ActorOf(u) ?? Wrap(u, v);
                        var enc = ei >= 0 && ei < Encounters.Count ? Encounters[ei] : null;
                        a.InitialState = init; a.ReturnState = ret; a.Home = home; a.HomeYaw = homeYaw;
                        a.Braindead = (flags & 1) != 0; a.Blind = (flags & 2) != 0; a.Deaf = (flags & 4) != 0; a.Free = (flags & 8) != 0;
                        a.FightPlayer = (flags & 32) != 0; a.MeleeInhibited = (flags & 64) != 0; a.RangedInhibited = (flags & 128) != 0;
                        a.FightAi = fight.Length > 0 ? fight : null;
                        if (enc != null) { a.MoveToSquad(enc, Mathf.Clamp(squad, 0, Mathf.Max(0, enc.Data.Squads.Count - 1))); enc.Created = true; }
                        a.EnterAiState(init);
                        if (cl >= 0 && cl < Data.CommandLists.Count) { a.RunCommandList(Data.CommandLists[cl]); a.Commands.Index = Mathf.Max(0, ci); }
                        a.AdoptTarget(target, seen, seenAgo, mode, fpos);
                        if ((flags & 16) != 0) a.GoBerserk("hand-over");
                        adopted++;
                    }
                }
                catch (Exception e) { HaloLog.Warn("AI: hand-over blob unreadable (" + e.Message + ") - re-seeding from the units"); blob = null; }
            }
            if (blob == null || blob.Length <= 8) adopted += SeedFromUnits();
            HaloLog.Info("AI: host hand-over adopted " + adopted + " actors" + (missing > 0 ? " (" + missing + " units gone)" : "") + (blob == null ? " from the units (no blob)" : ""));
        }

        /// <summary>No state: every live AI-capable unit (an actor variant, not an actor yet) joins the nearest encounter of its team.</summary>
        static int SeedFromUnits()
        {
            int n = 0;
            var all = HaloBipeds.All;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || !u.Alive || u.ActorVariant == null || ActorOf(u) != null) continue;
                var v = Catalog.Variant(u.ActorVariant);
                if (v == null) continue;
                var a = Wrap(u, v);
                HaloAiEncounter best = null; int bestSquad = 0; float bd = float.MaxValue;
                foreach (var e in Encounters)
                {
                    if (e.Team != HaloTeam.None && e.Team != u.Team) continue;
                    for (int s = 0; s < e.Data.Squads.Count; s++)
                        foreach (var st in e.Data.Squads[s].Starts)
                        {
                            float d = (HaloSpace.Pos(st.Position) - u.transform.position).sqrMagnitude;
                            if (d < bd) { bd = d; best = e; bestSquad = s; }
                        }
                }
                if (best != null && bd < 60f * 60f) a.MoveToSquad(best, bestSquad); else a.Free = true;
                a.EnterAiState(HaloAiState.Alert);
                n++;
            }
            return n;
        }
    }

    public sealed partial class HaloActor
    {
        int _adoptTarget; float _adoptUntil = -1f;

        public int FiringPosition => _fpos;

        /// <summary>Hand-over: the old host's target memory and mode (the target is re-found by serial / as a player on the next ticks).</summary>
        internal void AdoptTarget(int target, Vector3 lastSeen, float seenAgo, Mode mode, int fpos)
        {
            TargetLastSeen = lastSeen;
            TargetSeenAt = Now - Mathf.Max(0f, seenAgo);
            _adoptTarget = target; _adoptUntil = Now + 2f;
            if (fpos >= 0 && Enc != null && fpos < Enc.Positions.Length) TakePosition(fpos, 3f);
            if (target != 0 && (mode == Mode.Combat || mode == Mode.Search || mode == Mode.Hide)) SetState(mode == Mode.Hide ? Mode.Combat : mode, mode == Mode.Search ? SearchTime() : 0f);
        }

        /// <summary>Resolves the adopted target once the bodies know the units (called from UpdateTarget).</summary>
        void ResolveAdoptedTarget()
        {
            if (_adoptUntil < 0f) return;
            if (Now > _adoptUntil) { _adoptUntil = -1f; return; }
            HaloAiBody b = null;
            if (_adoptTarget == -1) b = HaloAiBodies.NearestPlayer(TargetLastSeen);
            else if (_adoptTarget > 0) b = HaloAiBodies.Of(HaloAi.UnitOfSerialFn(_adoptTarget));
            if (b == null) return;
            Target = b; TargetSince = Now; _adoptUntil = -1f;
        }
    }
}

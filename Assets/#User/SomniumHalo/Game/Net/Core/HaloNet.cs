using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    public enum HaloNetEventKind
    {
        Joined,        // another player is now in my match (Slot)
        Left,          // ... and not any more
        Shot,          // Slot fired: Count shots, Tag = weapon, Pos = muzzle, Dir
        Throw,         // Slot threw a grenade: Tag (proj / eqip), Pos, Vel
        Melee,         // Slot swung (melee)
        Death,         // Slot (the victim) died: Other = killer slot (NoSlot none), Tag = cause, flags
        HitMe,         // Slot (the shooter) hit ME: Amount, Tag = cause, Value = damage effect tag, flags - already filtered
        Objective,     // Slot's objective event: Value = code, Pos
        Pickup,        // Slot took item Value
        Drop,          // Slot dropped Tag (weapon) with Value rounds at Pos, Vel; Other = drop id
        TeamChosen,    // my side is Value (0 / 1)
        HitActor,      // (the host) Slot (a player) hit the AI unit with serial Value: Amount, Tag = cause, flags
        UseControl,    // (the host) Slot (a player following the mission) pressed the device control at scenario placement Value
    }

    /// <summary>Something another player did, read from its record (see <see cref="HaloNet.Events"/>).</summary>
    public struct HaloNetEvent
    {
        public HaloNetEventKind Kind;
        public int Slot, Other, Tag, Count, Value;
        public float Amount;
        public bool Melee, Head, Explosion, Back, Instant, Splatter, Suicide, Grenade, Betrayal, Ai;
        public HaloV3 Pos, Dir, Vel;
    }

    /// <summary>
    /// Everyone's presence over the world's Photon Fusion session, the Black Ops 2 Dreamport's slot layer (Bo2Net) for Halo: networked objects placed
    /// in the scene by hand ("Halo Net Player 0..49", a NetworkObject + NetworkBridgeData64 each), because none can be made at run time. Each client
    /// claims one free slot (<see cref="HaloSlotClaim"/>), writes its own record there (<see cref="HaloNetWire"/>, about every 100 ms) and reads
    /// everyone else's. Leaving sends nothing: the slot's owner is no longer in the room, every client stops counting it, and whoever holds its
    /// authority writes it free. The instance master also writes the world blocks ("Halo Net World 0..3", <see cref="HaloNetWorld"/>).
    ///
    /// Pure C# over an <see cref="IHaloSlotTransport"/> (the real one is HaloFusionTransport, the test one a loopback), so two logical players can
    /// run in one process. The game layer (HaloNetGame) fills <see cref="Own"/> each frame, calls <see cref="Tick"/>, and acts on <see cref="Events"/>:
    ///  - "the same match" = playing, the same map (name hash and map file fingerprint) and the same game number of the instance master;
    ///  - events from counters: shots, throws, melee, deaths, objective events, pickups, drops, and the hits aimed at this player (each hit number
    ///    applied once, never from the own team in team games without friendly fire, never while dead);
    ///  - team totals = the members' counters, kept for members who left;
    ///  - interpolation data per remote (<see cref="HaloNetTimeline"/>).
    /// Never writes a record it does not hold, never throws into the caller.
    /// </summary>
    public sealed partial class HaloNet
    {
        public const int MaxSlots = HaloNetWire.MaxSlots;
        public const int Records = MaxSlots + HaloNetWorld.MaxBlocks + HaloNetCoop.Records;

        /// <summary>At most this many own-record writes a second (Fusion itself sends at its own rate).</summary>
        public double WriteEvery = 0.1;
        public const double ServeEvery = 0.25;
        public const double SettleWindow = 3.0;

        /// <summary>Another player's slot (in the Dreamport, maybe in my match).</summary>
        public sealed class Remote
        {
            public int Slot, PlayerId;
            public HaloNetState State;
            public HaloNetTimeline Timeline = new HaloNetTimeline();
            /// <summary>Same map, same game, playing: the player I fight with or against.</summary>
            public bool InMatch;
            /// <summary>The Somnium nickname (set by the game layer).</summary>
            public string Name;
            /// <summary>The game layer's own object for this player.</summary>
            public object Tag;
            public double JoinedAt, LastShotAt = -100.0;
            public int StampMs;
            /// <summary>The record names another build of the map (log once, never in my match).</summary>
            public bool MapMismatch;

            internal int Seq = -1, Generation = -1;
            internal bool Fresh, Decoded, MismatchLogged;
            internal int LastFire, LastThrow, LastMelee, LastDeaths, LastHitSeq, LastObj, LastPickup, LastDrop;
            internal int InKills, InScore, InTeam;      // (the counters last seen while in my match: kept when it leaves - its leaving record may already be reset)
        }

        private readonly IHaloSlotTransport _t;
        private readonly Func<double> _clock;
        private readonly string _label;
        private readonly int[][] _slots = new int[Records][];
        private readonly bool[] _valid = new bool[Records];
        private readonly bool[] _authority = new bool[Records];
        private readonly Remote[] _remoteBySlot = new Remote[MaxSlots];
        private readonly List<Remote> _remotes = new List<Remote>(MaxSlots);
        private readonly List<Remote> _listed = new List<Remote>(MaxSlots);
        private readonly HaloSlotClaim _claim = new HaloSlotClaim();
        private readonly int[] _own = new int[HaloNetWire.Ints];
        private readonly int[] _free = new int[HaloNetWire.Ints];
        private readonly int[] _departedKills = new int[2], _departedScore = new int[2];
        private readonly Dictionary<int, int> _bankedKills = new Dictionary<int, int>(), _bankedScore = new Dictionary<int, int>(), _bankedTeam = new Dictionary<int, int>();
        private readonly HaloNetHit[] _pending = new HaloNetHit[8];
        private int _pendingCount;
        private readonly int[] _pickupQueue = new int[16];
        private int _pickupCount;
        private struct QueuedDrop { public int Weapon, Rounds, Reserve, Charge; public HaloV3 Pos, Vel; }
        private readonly QueuedDrop[] _dropQueue = new QueuedDrop[8];
        private int _dropCount;
        private struct QueuedObj { public int Code, Arg; public HaloV3 Pos; }
        private readonly QueuedObj[] _objQueue = new QueuedObj[8];
        private int _objCount;
        private readonly double _t0;
        private int _slotCount, _worldCount;
        private double _nextServe, _nextWrite, _ownedAt = -1.0;
        private readonly int[] _seqSeen = new int[MaxSlots];
        private readonly double[] _changedAt = new double[MaxSlots];
        private bool _forceWrite, _wasOnline, _teamChosen;
        private double _teamChosenAt;
        private int _writes, _reads, _errors, _logs;
        private bool _disposed;

        /// <summary>What this player says about itself: the game layer sets it before every <see cref="Tick"/>.</summary>
        public HaloNetState Own = HaloNetState.Empty();

        /// <summary>What happened since the last <see cref="Tick"/> began (cleared at the start of each).</summary>
        public readonly List<HaloNetEvent> Events = new List<HaloNetEvent>(64);

        /// <summary>The instance master's world blocks (items, objectives, unoccupied vehicles).</summary>
        public readonly HaloNetWorld World = new HaloNetWorld();

        /// <summary>The current match's rules: team game (the side matters) and friendly fire (hits from the own side apply). Set by the game layer.</summary>
        public bool TeamGame, FriendlyFire;

        /// <summary>The game layer decides my side during a match (the Match module owns HaloLocal.Team): the session does not choose or balance it then.</summary>
        public bool TeamFromGame;

        /// <summary>Optional log sink (HaloLog.Info by default).</summary>
        public static Action<string> LogSink;

        public HaloNet(IHaloSlotTransport transport, Func<double> clock, string label)
        {
            _t = transport;
            _clock = clock;
            _label = label ?? "";
            _t0 = clock();
            for (int k = 0; k < Records; k++) _slots[k] = new int[HaloNetWire.Ints];
        }

        public IHaloSlotTransport Transport { get { return _t; } }

        /// <summary>In a room with the slots found. False alone in the editor, before the room is joined, or without slot objects.</summary>
        public bool Online { get { return _t.Online && _t.SlotCount > 0; } }

        public int LocalId { get { return _t.LocalId; } }

        /// <summary>This client's slot, -1 until claimed.</summary>
        public int MySlot { get { return _claim.Holding ? _claim.Slot : -1; } }

        /// <summary>The claim is complete (the record is held and confirmed).</summary>
        public bool Claimed { get { return _claim.Current == HaloSlotClaim.Stage.Owned; } }

        public int SlotCount { get { return _slotCount; } }

        public int WorldCount { get { return _worldCount; } }

        /// <summary>The other players in my match (same map and game, playing).</summary>
        public IReadOnlyList<Remote> Remotes { get { return _remotes; } }

        /// <summary>Every other Dreamport player in the instance (decoded, not free), in slot order.</summary>
        public IReadOnlyList<Remote> Listed { get { return _listed; } }

        public Remote RemoteAt(int slot)
        {
            return slot >= 0 && slot < MaxSlots ? _remoteBySlot[slot] : null;
        }

        public double Now { get { return _clock(); } }

        private int StampMs() { return (int)((_clock() - _t0) * 1000.0); }

        /// <summary>Nothing to wait for: not online, or the claim is done (or took too long).</summary>
        public bool Settled
        {
            get
            {
                if (!Online) return true;
                double now = _clock();
                return (_ownedAt >= 0.0 && now - _ownedAt > 0.5) || now - _t0 > 4.5;
            }
        }

        /// <summary>The kills and score of one side in my match: its members' counters, mine, and those of members who have left.</summary>
        public void TeamTotals(int team, out int kills, out int score)
        {
            kills = _departedKills[team & 1];
            score = _departedScore[team & 1];
            for (int i = 0; i < _remotes.Count; i++)
                if ((_remotes[i].State.Team & 1) == (team & 1)) { kills += _remotes[i].State.Kills; score += _remotes[i].State.Score; }
            if (Own.Status == HaloNetStatus.Playing && (Own.Team & 1) == (team & 1)) { kills += Own.Kills; score += Own.Score; }
        }

        /// <summary>Forget the kept counters of players who left (a new match starts).</summary>
        public void ResetMatch()
        {
            _departedKills[0] = _departedKills[1] = 0;
            _departedScore[0] = _departedScore[1] = 0;
            _bankedKills.Clear();
            _bankedScore.Clear();
            _bankedTeam.Clear();
            _teamChosen = false;
            _pendingCount = _pickupCount = _dropCount = _objCount = 0;
        }

        // ------------------------------------------------------------------ the frame

        /// <summary>Once per frame: read every record, step the claim, keep house, find who is in my match, write the own record. Never throws.</summary>
        public void Tick()
        {
            Events.Clear();
            if (_disposed) return;
            try
            {
                TickCore(_clock());
            }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 3) Warn("Net" + _label + ": the tick threw (" + _errors + (_errors == 3 ? ", later ones not logged" : "") + "): " + e);
            }
        }

        private void TickCore(double now)
        {
            _t.Update(now);
            bool online = Online;
            if (online != _wasOnline)
            {
                _wasOnline = online;
                if (online) Log("room joined (player id " + LocalId + ", room master " + _t.IsMaster + ", " + _t.SlotCount + " slots, " + _t.WorldCount + " world blocks) - " + _t.Describe());
                else Log("offline (no room: nobody else is shown)");
            }

            _slotCount = online ? Math.Min(_t.SlotCount, MaxSlots) : 0;
            _worldCount = online ? Math.Min(_t.WorldCount, HaloNetWorld.MaxBlocks) : 0;
            _coopCount = online ? Math.Min(_t.CoopCount, HaloNetCoop.Records) : 0;
            IReadOnlyList<int> present = _t.Present;
            int localId = LocalId;
            for (int k = 0; k < Records; k++)
            {
                bool inRange = k < MaxSlots ? k < _slotCount : k < HaloNetCoop.FirstActor ? k - MaxSlots < _worldCount : k - HaloNetCoop.FirstActor < _coopCount;
                bool valid = inRange && _t.IsValid(k);
                _valid[k] = valid;
                _authority[k] = valid && _t.HasAuthority(k);
                if (!valid)
                {
                    if (_slots[k][HaloNetWire.AtMagic] != 0) Array.Clear(_slots[k], 0, _slots[k].Length);
                    continue;
                }

                if (!_t.Read(k, _slots[k])) _valid[k] = false;
                else
                {
                    _reads++;
                    if (k < MaxSlots && _slots[k][HaloNetWire.AtSeq] != _seqSeen[k]) { _seqSeen[k] = _slots[k][HaloNetWire.AtSeq]; _changedAt[k] = now; }
                }
            }

            StepClaim(online, localId, present, now);
            if (now >= _nextServe)
            {
                _nextServe = now + ServeEvery;
                Housekeeping(localId, present);
            }

            RefreshRemotes(now, localId, present);
            StepMaster(now, localId);
            StepTeam(now);
            StepLobbyTeam(now);
            StepWorld(now, localId);
            StepCoop(now, localId);
            StepHostStream(now, localId);
            WriteOwn(now, localId);
        }

        // ------------------------------------------------------------------ the claim and the house

        private void StepClaim(bool online, int localId, IReadOnlyList<int> present, double now)
        {
            var input = new HaloSlotClaim.Inputs { Now = now, LocalId = localId, Allowed = online && _slotCount > 0, SlotCount = _slotCount };
            _claim.Step(input, _slots, _valid, _authority, present);
            if (_claim.AskAuthority >= 0) _t.RequestAuthority(_claim.AskAuthority);
            if (_claim.WriteClaim && _claim.Slot >= 0)
            {
                HaloNetWire.WriteClaim(_own, localId, _claim.Generation, StampMs());
                _forceWrite = true;
                _teamChosen = false;
            }

            if (_claim.Current == HaloSlotClaim.Stage.Owned) { if (_ownedAt < 0.0) _ownedAt = now; }
            else _ownedAt = -1.0;
            string note = _claim.TakeNote();
            if (note != null) Log(note + ".");
        }

        private void Housekeeping(int localId, IReadOnlyList<int> present)
        {
            for (int k = 0; k < _slotCount; k++)
            {
                if (!_valid[k] || !HaloSlotRules.ToFree(_slots[k], _authority[k], k == _claim.Slot, localId, present)) continue;
                int gone = HaloNetWire.OwnerOf(_slots[k]);
                Array.Copy(_slots[k], _free, _free.Length);
                HaloNetWire.WriteFree(_free, StampMs());
                Push(k, _free);
                Log("slot " + k + " written free (" + (gone == localId ? "this client's own record, left behind by a lost claim" : "player " + gone + " left") + ").");
            }
        }

        private void Push(int record, int[] values)
        {
            if (record < 0 || record >= Records || !_valid[record] || !_authority[record]) return;
            try { _writes += _t.Write(record, values); }
            catch (Exception e) { _errors++; if (_errors <= 3) Warn("Net" + _label + ": writing record " + record + " failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ the others

        private void RefreshRemotes(double now, int localId, IReadOnlyList<int> present)
        {
            int mySlot = MySlot;
            _listed.Clear();
            for (int k = 0; k < MaxSlots; k++)
            {
                Remote r = _remoteBySlot[k];
                int[] slot = _slots[k];
                bool listed = k < _slotCount && _valid[k] && k != mySlot && HaloSlotRules.Listed(slot, localId, present) && HaloNetWire.StatusOf(slot) != HaloNetStatus.Free && !Shadowed(k);
                if (!listed)
                {
                    if (r != null) DropRemote(k, r, now);
                    continue;
                }

                int owner = HaloNetWire.OwnerOf(slot);
                int generation = HaloNetWire.GenerationOf(slot);
                if (r != null && (r.PlayerId != owner || r.Generation != generation))
                {
                    DropRemote(k, r, now);
                    r = null;
                }

                if (r == null)
                {
                    r = new Remote { Slot = k, PlayerId = owner, Generation = generation, JoinedAt = now, Name = "Player " + owner };
                    _remoteBySlot[k] = r;
                }

                int seq = slot[HaloNetWire.AtSeq];
                r.Fresh = false;
                if (!r.Decoded || seq != r.Seq)
                {
                    r.Seq = seq;
                    r.Decoded = HaloNetWire.Unpack(slot, out r.State);
                    r.StampMs = slot[HaloNetWire.AtStamp];
                    r.Fresh = r.Decoded;
                }

                if (r.Decoded) _listed.Add(r);
            }

            // who is in my match
            bool ownPlaying = Own.Status == HaloNetStatus.Playing && Own.ZoneHash != 0 && Own.MatchSeq != 0;
            for (int k = 0; k < _slotCount; k++)
            {
                Remote r = _remoteBySlot[k];
                if (r == null) continue;
                bool same = ownPlaying && r.Decoded && r.State.Status == HaloNetStatus.Playing && r.State.ZoneHash == Own.ZoneHash && r.State.MatchSeq == Own.MatchSeq
                            && r.State.MatchState != HaloMatchState.None;
                r.MapMismatch = same && Own.MapPrint != 0 && r.State.MapPrint != 0 && r.State.MapPrint != Own.MapPrint;
                if (r.MapMismatch && !r.MismatchLogged)
                {
                    r.MismatchLogged = true;
                    Warn("Net" + _label + ": player " + r.PlayerId + " (slot " + k + ") plays the same game on another build of the map (fingerprint " + r.State.MapPrint.ToString("x4") + ", mine "
                         + Own.MapPrint.ToString("x4") + "): weapons would not match, so they are not shown in my match.");
                }

                bool inMatch = same && !r.MapMismatch;
                if (inMatch) { r.InKills = r.State.Kills; r.InScore = r.State.Score; r.InTeam = r.State.Team & 1; }
                if (inMatch && !r.InMatch) JoinedMatch(r, now);
                else if (!inMatch && r.InMatch) LeftMatch(r, now, "left the match");
            }

            // events and samples of the fresh records of the players in my match
            for (int i = 0; i < _remotes.Count; i++)
            {
                Remote r = _remotes[i];
                if (!r.Fresh) continue;
                HaloNetState st = r.State;
                AddSample(r, now);
                int df = (st.FireCounter - r.LastFire) & 0xFF;
                if (df != 0 && df < 128)
                {
                    r.LastShotAt = now;
                    Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.Shot, Slot = r.Slot, Count = df, Tag = st.ShotWeapon != HaloNetWire.NoTag ? st.ShotWeapon : st.Held, Pos = st.Muzzle, Dir = st.MuzzleDir });
                }

                r.LastFire = st.FireCounter;
                int dt = (st.ThrowCounter - r.LastThrow) & 0xF;
                if (dt != 0 && dt < 8) Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.Throw, Slot = r.Slot, Tag = st.ThrowTag, Pos = st.ThrowPos, Vel = st.ThrowVel });
                r.LastThrow = st.ThrowCounter;
                int dm = (st.MeleeCounter - r.LastMelee) & 0xF;
                if (dm != 0 && dm < 8) Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.Melee, Slot = r.Slot, Tag = st.Held });
                r.LastMelee = st.MeleeCounter;
                int dd = (st.Deaths - r.LastDeaths) & 0xFFF;
                if (dd != 0 && dd < 2048)
                    Events.Add(new HaloNetEvent
                    {
                        Kind = HaloNetEventKind.Death, Slot = r.Slot, Other = st.Killer, Tag = st.DeathCause, Melee = st.DeathMelee, Head = st.DeathHead, Explosion = st.DeathExplosion,
                        Splatter = st.DeathSplatter, Suicide = st.DeathSuicide, Grenade = st.DeathGrenade, Betrayal = st.DeathBetrayal, Pos = st.Root,
                    });
                r.LastDeaths = st.Deaths;
                int dob = (st.ObjCounter - r.LastObj) & 0xFF;
                if (dob != 0 && dob < 128) Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.Objective, Slot = r.Slot, Value = st.ObjCode, Other = st.ObjArg, Pos = st.ObjPos, Count = dob });
                r.LastObj = st.ObjCounter;
                ReadPickups(r);
                int ddr = (st.DropCounter - r.LastDrop) & 0xF;
                if (ddr != 0 && ddr < 8)
                    Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.Drop, Slot = r.Slot, Tag = st.DropWeapon, Value = st.DropRounds, Count = st.DropReserve, Amount = st.DropCharge / 255f, Pos = st.DropPos, Vel = st.DropVel, Other = DropId(r.Slot, st.DropCounter) });
                r.LastDrop = st.DropCounter;
                ReadHits(r, mySlot);
            }
        }

        /// <summary>The id of a dropped weapon: 1024 + slot * 16 + the dropper's drop counter (the pickup ids of the scenario's items are below 1024).</summary>
        public static int DropId(int slot, int counter) { return 1024 + (slot & 63) * 16 + (counter & 15); }

        private void ReadPickups(Remote r)
        {
            HaloNetState st = r.State;
            int delta = (st.PickupCounter - r.LastPickup) & 0xF;
            if (delta != 0 && delta < 8)
            {
                if (delta > 2) Log("player " + r.PlayerId + ": " + (delta - 2) + " pickup event(s) were overwritten before they were read.");
                int n = Math.Min(delta, 2);
                for (int j = n - 1; j >= 0; j--)
                {
                    int c = (st.PickupCounter - j) & 0xF;
                    int item = (c & 1) == 0 ? st.Pickup0 : st.Pickup1;
                    Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.Pickup, Slot = r.Slot, Value = item });
                    WorldPickup(item);
                }
            }

            r.LastPickup = st.PickupCounter;
        }

        /// <summary>
        /// Two written slots naming the same owner: a client whose claim was lost and who claimed another slot leaves the old record behind until it is freed. Only the
        /// record still being written counts.
        /// </summary>
        private bool Shadowed(int k)
        {
            int owner = HaloNetWire.OwnerOf(_slots[k]);
            for (int j = 0; j < _slotCount; j++)
            {
                if (j == k || !_valid[j] || HaloNetWire.OwnerOf(_slots[j]) != owner || HaloNetWire.StatusOf(_slots[j]) == HaloNetStatus.Free) continue;
                if (_changedAt[j] > _changedAt[k] + 0.35 || (Math.Abs(_changedAt[j] - _changedAt[k]) <= 0.35 && j > k)) return true;
            }

            return false;
        }

        private void AddSample(Remote r, double now)
        {
            HaloNetState st = r.State;
            var s = new HaloNetTimeline.Sample
            {
                Vr = st.Vr, RootYaw = st.RootYaw, Root = st.Root, Head = st.Head, HeadRot = st.HeadRot, LHand = st.LHand, RHand = st.RHand, LRot = st.LRot, RRot = st.RRot,
                InVehicle = st.Vehicle >= 0 && st.Driving, VehiclePos = st.VehiclePos, VehicleRot = st.VehicleRot, VehicleVel = st.VehicleVel, VehicleFlags = st.VehicleFlags,
            };
            r.Timeline.Add(r.StampMs / 1000.0, now, s);
        }

        /// <summary>The shooter's hit ring: every hit number after the last one applied, once, if it is about me.</summary>
        private void ReadHits(Remote r, int mySlot)
        {
            HaloNetState st = r.State;
            int delta = (st.HitSeq - r.LastHitSeq) & 0xFF;
            if (delta == 0) return;
            if (delta >= 128) { r.LastHitSeq = st.HitSeq; return; }
            int count = Math.Min(delta, HaloNetWire.HitRing);
            for (int n = count - 1; n >= 0; n--)
            {
                int seq = (st.HitSeq - n) & 0xFF;
                HaloNetHit h = st.HitAt(seq & 3);
                if (!h.Valid || h.Seq != seq) continue;
                if (h.Victim == ControlVictim)
                {
                    if (Coop.Writing) Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.UseControl, Slot = r.Slot, Value = h.Effect, Pos = r.State.Head });
                    continue;
                }

                if (h.Victim == ActorVictim)
                {
                    // a player hit one of the host's AI units: only the host applies it
                    if (Coop.Writing) Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.HitActor, Slot = r.Slot, Value = h.Effect, Amount = h.Amount, Tag = h.Cause, Melee = h.Melee, Head = h.Head, Explosion = h.Explosion, Back = h.Back, Instant = h.Instant, Pos = r.State.Head });
                    continue;
                }

                if (mySlot < 0 || h.Victim != mySlot) continue;
                if (Own.Status != HaloNetStatus.Playing || !Own.Alive) continue;                                    // dead (or not playing) players are not hit
                if (!h.Ai && TeamGame && !FriendlyFire && (r.State.Team & 1) == (Own.Team & 1)) continue;      // never from my own side (unless friendly fire); the AI's are the AI's
                Events.Add(new HaloNetEvent
                {
                    Kind = HaloNetEventKind.HitMe, Slot = r.Slot, Amount = h.Amount, Tag = h.Cause, Value = h.Effect, Melee = h.Melee, Head = h.Head, Explosion = h.Explosion,
                    Back = h.Back, Instant = h.Instant, Ai = h.Ai, Pos = r.State.Head,
                });
            }

            r.LastHitSeq = st.HitSeq;
        }

        private void JoinedMatch(Remote r, double now)
        {
            r.InMatch = true;
            r.Timeline.Clear();
            HaloNetState st = r.State;
            r.LastFire = st.FireCounter;
            r.LastThrow = st.ThrowCounter;
            r.LastMelee = st.MeleeCounter;
            r.LastDeaths = st.Deaths;
            r.LastHitSeq = st.HitSeq;
            r.LastObj = st.ObjCounter;
            r.LastPickup = st.PickupCounter;
            r.LastDrop = st.DropCounter;
            int kept;
            if (_bankedKills.TryGetValue(r.PlayerId, out kept) && st.Kills >= kept && st.Score >= _bankedScore[r.PlayerId])
            {
                // the record carries what was kept (it came back with its counters): count it from the record again
                int t = _bankedTeam[r.PlayerId] & 1;
                _departedKills[t] -= kept;
                _departedScore[t] -= _bankedScore[r.PlayerId];
                _bankedKills.Remove(r.PlayerId);
                _bankedScore.Remove(r.PlayerId);
                _bankedTeam.Remove(r.PlayerId);
            }
            // (else it rejoined with fresh counters - the Match module restarts them: its earlier kills stay kept for its side)

            _remotes.Add(r);
            AddSample(r, now);
            Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.Joined, Slot = r.Slot });
            Log("player " + r.PlayerId + " (slot " + r.Slot + ") is in my match: side " + st.Team + ", " + st.Kills + " kills, " + st.Deaths + " deaths. " + _remotes.Count + " other player(s) in the match.");
        }

        private void LeftMatch(Remote r, double now, string why)
        {
            r.InMatch = false;
            _remotes.Remove(r);
            int kills = r.InKills, score = r.InScore, team = r.InTeam, before;
            if (_bankedKills.TryGetValue(r.PlayerId, out before))
            {
                // it rejoined with fresh counters and leaves again: what it made this time comes on top of what was kept
                _bankedKills[r.PlayerId] = before + kills;
                _bankedScore[r.PlayerId] += score;
            }
            else
            {
                _bankedKills[r.PlayerId] = kills;
                _bankedScore[r.PlayerId] = score;
                _bankedTeam[r.PlayerId] = team;
            }

            _departedKills[team] += kills;
            _departedScore[team] += score;
            Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.Left, Slot = r.Slot });
            Log("player " + r.PlayerId + " (slot " + r.Slot + ") " + why + " with " + kills + " kills / " + score + " score kept for side " + team + ". " + _remotes.Count + " other player(s) in the match.");
        }

        private void DropRemote(int slot, Remote r, double now)
        {
            if (r.InMatch) LeftMatch(r, now, "is gone");
            _remoteBySlot[slot] = null;
        }

        // ------------------------------------------------------------------ the side in a match

        private void StepTeam(double now)
        {
            if (TeamFromGame && Own.Status == HaloNetStatus.Playing) { _teamChosen = true; return; }
            if (Own.Status != HaloNetStatus.Playing || MySlot < 0)
            {
                if (Own.Status != HaloNetStatus.Playing) _teamChosen = false;
                return;
            }

            int n0 = 0, n1 = 0;
            for (int i = 0; i < _remotes.Count; i++)
            {
                if ((_remotes[i].State.Team & 1) == 0) n0++; else n1++;
            }

            if (!_teamChosen)
            {
                int team = _lobbyTeamSet ? (Own.Team & 1) : (n0 < n1 ? 0 : n1 < n0 ? 1 : (LocalId & 1));
                Own.Team = team;
                _teamChosen = true;
                _teamChosenAt = _lobbyTeamSet ? double.NegativeInfinity : now;
                Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.TeamChosen, Value = team });
                Log("side " + team + " in the match (" + n0 + " on red, " + n1 + " on blue).");
                return;
            }

            if (!TeamGame || now - _teamChosenAt > SettleWindow) return;
            int mine = Own.Team & 1;
            int cMine = (mine == 0 ? n0 : n1) + 1, cOther = mine == 0 ? n1 : n0;
            if (cMine - cOther < 2) return;
            for (int i = 0; i < _remotes.Count; i++)
                if ((_remotes[i].State.Team & 1) == mine && _remotes[i].PlayerId > LocalId) return;
            Own.Team = 1 - mine;
            Events.Add(new HaloNetEvent { Kind = HaloNetEventKind.TeamChosen, Value = Own.Team });
            Log("side changed to " + Own.Team + " (the match was lopsided " + cMine + " : " + cOther + ").");
        }

        // ------------------------------------------------------------------ own record

        private void WriteOwn(double now, int localId)
        {
            int k = MySlot;
            if (k < 0 || k >= _slotCount || !_valid[k] || !_authority[k]) return;
            if (HaloNetWire.OwnerOf(_own) != localId) return;   // the claim record is written first (StepClaim)
            if (_forceWrite)
            {
                _forceWrite = false;
                _nextWrite = now + WriteEvery;
                _own[HaloNetWire.AtSeq]++;
                _own[HaloNetWire.AtStamp] = StampMs();
                Push(k, _own);
                return;
            }

            if (now < _nextWrite) return;
            _nextWrite = now + WriteEvery;
            _lastWriteAt = now;
            FlushQueues();
            HaloNetState own = Own;
            if (own.Status == HaloNetStatus.Free) own.Status = HaloNetStatus.Present;
            HaloNetWire.Pack(in own, _own);
            _own[HaloNetWire.AtSeq]++;
            _own[HaloNetWire.AtStamp] = StampMs();
            Push(k, _own);
        }

        /// <summary>The shortest time between two writes (Fusion sends at about 16 Hz: two writes inside one send arrive as one, which would overrun the event rings).</summary>
        public const double MinWriteGap = 0.07;
        private double _lastWriteAt = double.NegativeInfinity;

        /// <summary>Something worth an early write happened: the next write comes as soon as <see cref="MinWriteGap"/> allows.</summary>
        private void Early()
        {
            double at = _lastWriteAt + MinWriteGap;
            if (at < _nextWrite) _nextWrite = at;
        }

        /// <summary>Moves the waiting hits (four at most), pickups (two), one drop and one objective event into the record.</summary>
        private void FlushQueues()
        {
            int n = Math.Min(_pendingCount, HaloNetWire.HitRing);
            for (int i = 0; i < n; i++)
            {
                HaloNetHit h = _pending[i];
                Own.HitSeq = (Own.HitSeq + 1) & 0xFF;
                h.Seq = Own.HitSeq;
                Own.SetHit(Own.HitSeq & 3, h);
            }

            Shift(_pending, ref _pendingCount, n);
            int p = Math.Min(_pickupCount, 2);
            for (int i = 0; i < p; i++)
            {
                Own.PickupCounter = (Own.PickupCounter + 1) & 0xF;
                if ((Own.PickupCounter & 1) == 0) Own.Pickup0 = _pickupQueue[i]; else Own.Pickup1 = _pickupQueue[i];
            }

            Shift(_pickupQueue, ref _pickupCount, p);
            if (_dropCount > 0)
            {
                QueuedDrop d = _dropQueue[0];
                Own.DropCounter = (Own.DropCounter + 1) & 0xF;
                Own.DropWeapon = d.Weapon;
                Own.DropRounds = d.Rounds;
                Own.DropReserve = d.Reserve;
                Own.DropCharge = d.Charge;
                Own.DropPos = d.Pos;
                Own.DropVel = d.Vel;
                Shift(_dropQueue, ref _dropCount, 1);
            }

            if (_objCount > 0)
            {
                QueuedObj o = _objQueue[0];
                Own.ObjCounter = (Own.ObjCounter + 1) & 0xFF;
                Own.ObjCode = o.Code;
                Own.ObjArg = o.Arg;
                Own.ObjPos = o.Pos;
                Shift(_objQueue, ref _objCount, 1);
            }

            // (more waiting: the next regular write takes it - two writes inside one Fusion send would overrun the rings)
        }

        private static void Shift<T>(T[] a, ref int count, int n)
        {
            if (n <= 0) return;
            for (int i = n; i < count; i++) a[i - n] = a[i];
            count -= n;
        }

        /// <summary>The own record is written free (this client leaves the world). Idempotent.</summary>
        public void Stop()
        {
            int k = MySlot;
            if (k >= 0 && k < _slotCount && _valid[k] && _authority[k])
            {
                HaloNetWire.WriteFree(_own, StampMs());
                Push(k, _own);
                Log("slot " + k + " released.");
            }

            _claim.Reset();
            for (int j = 0; j < _remotes.Count; j++) _remotes[j].InMatch = false;
            _remotes.Clear();
            _listed.Clear();
            for (int j = 0; j < MaxSlots; j++) _remoteBySlot[j] = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            try { Stop(); }
            catch (Exception e) { Warn("Net" + _label + ": stopping failed: " + e.Message); }
            _disposed = true;
            try { _t.Dispose(); }
            catch (Exception) { /* the world is going away */ }
        }

        // ------------------------------------------------------------------ what the game tells us

        /// <summary>A shot left the muzzle of the held weapon.</summary>
        public void NoteShot(int weaponTag, HaloV3 muzzle, HaloV3 direction)
        {
            Own.FireCounter = (Own.FireCounter + 1) & 0xFF;
            Own.ShotWeapon = weaponTag;
            Own.Muzzle = muzzle;
            Own.MuzzleDir = direction;
        }

        /// <summary>A grenade was let go.</summary>
        public void NoteThrow(int tag, HaloV3 position, HaloV3 velocity)
        {
            Own.ThrowCounter = (Own.ThrowCounter + 1) & 0xF;
            Own.ThrowTag = tag;
            Own.ThrowPos = position;
            Own.ThrowVel = velocity;
            Early();
        }

        public void NoteMelee()
        {
            Own.MeleeCounter = (Own.MeleeCounter + 1) & 0xF;
        }

        /// <summary>
        /// My shot / blast / melee hit another player's hit boxes: publish it. Hits of one cause on one victim between two writes are merged (a shotgun's pellets
        /// or an automatic burst arrive as one hit, amounts summed).
        /// </summary>
        public void NoteHit(int victimSlot, float amount, int cause, int effect, bool melee, bool head, bool explosion, bool back, bool instant, bool ai = false)
        {
            if (victimSlot < 0 || (victimSlot >= MaxSlots && victimSlot != ActorVictim && victimSlot != ControlVictim) || !(amount > 0f)) return;
            for (int i = 0; i < _pendingCount; i++)
            {
                HaloNetHit p = _pending[i];
                if (p.Victim != victimSlot || p.Cause != cause || p.Effect != effect || p.Melee != melee || p.Explosion != explosion || p.Ai != ai) continue;
                p.Amount = Math.Min(1023.75f, p.Amount + amount);
                p.Head |= head;
                p.Back |= back;
                p.Instant |= instant;
                _pending[i] = p;
                return;
            }

            if (_pendingCount == _pending.Length) Shift(_pending, ref _pendingCount, 1);
            _pending[_pendingCount++] = new HaloNetHit { Victim = victimSlot, Amount = Math.Min(1023.75f, amount), Cause = cause, Effect = effect, Melee = melee, Head = head, Explosion = explosion, Back = back, Instant = instant, Ai = ai };
            Early();                                           // a hit is worth an early write
        }

        /// <summary>The hit ring's victim number for "one of the host's AI units" (the unit's serial travels in the hit's effect field).</summary>
        public const int ActorVictim = 62;

        /// <summary>The hit ring's victim number for "I pressed a device control" (its scenario placement index in the effect field): only the host acts.</summary>
        public const int ControlVictim = 61;

        /// <summary>Campaign co-op follower: I pressed the device control at <paramref name="placement"/> - the host runs it as my press.</summary>
        public void NoteControl(int placement)
        {
            if (placement < 0 || placement >= 0xFFFF) return;
            NoteHit(ControlVictim, 1f, 0, placement, false, false, false, false, false);
        }

        /// <summary>Campaign co-op: my shot / blast / melee hit the host's AI unit <paramref name="serial"/> (a puppet on my screen): the host applies it.</summary>
        public void NoteHitActor(int serial, float amount, int cause, bool melee, bool head, bool explosion, bool back, bool instant)
        {
            if (serial <= 0 || serial > 0xFFF) return;
            NoteHit(ActorVictim, amount, cause, serial, melee, head, explosion, back, instant);
        }

        /// <summary>I died: who killed me (a slot, or NoSlot) and how. The death counter goes up here (the counter is the event).</summary>
        public void NoteDeath(int killerSlot, int causeTag, bool melee, bool head, bool explosion, bool splatter, bool suicide, bool grenade, bool betrayal)
        {
            Own.Killer = killerSlot < 0 || killerSlot >= MaxSlots ? HaloNetWire.NoSlot : killerSlot;
            Own.DeathCause = causeTag;
            Own.DeathMelee = melee;
            Own.DeathHead = head;
            Own.DeathExplosion = explosion;
            Own.DeathSplatter = splatter;
            Own.DeathSuicide = suicide;
            Own.DeathGrenade = grenade;
            Own.DeathBetrayal = betrayal;
            Own.Deaths = (Own.Deaths + 1) & 0xFFF;
            Early();
        }

        /// <summary>I took an item (a scenario pickup index, or a drop id).</summary>
        public void NotePickup(int item)
        {
            if (item < 0 || item > 0xFFFF) return;
            if (_pickupCount == _pickupQueue.Length) Shift(_pickupQueue, ref _pickupCount, 1);
            _pickupQueue[_pickupCount++] = item;
            WorldPickup(item);
            Early();
        }

        /// <summary>I dropped a weapon. Returns the drop id the others will know it by.</summary>
        public int NoteDrop(int weaponTag, int loaded, int reserve, float charge, HaloV3 pos, HaloV3 vel)
        {
            if (_dropCount == _dropQueue.Length) Shift(_dropQueue, ref _dropCount, 1);
            _dropQueue[_dropCount++] = new QueuedDrop { Weapon = weaponTag, Rounds = loaded, Reserve = reserve, Charge = HaloNetWire.Unit8(charge, 1f), Pos = pos, Vel = vel };
            Early();
            int counterAtWrite = (Own.DropCounter + _dropCount) & 0xF;
            return MySlot >= 0 ? DropId(MySlot, counterAtWrite) : -1;
        }

        /// <summary>An objective / game event of mine (the Match module's kind and argument, 8 bits each).</summary>
        public void NoteObjective(int code, int arg, HaloV3 pos)
        {
            if (_objCount == _objQueue.Length) Shift(_objQueue, ref _objCount, 1);
            _objQueue[_objCount++] = new QueuedObj { Code = code & 0xFF, Arg = arg & 0xFF, Pos = pos };
            Early();
        }

        // ------------------------------------------------------------------ for the isolation and the log

        /// <summary>
        /// Access isolation (Game/Access/HaloGear): true while that player (Fusion PlayerId = Bridge PhotonId) holds a written slot here - claimed, in the room
        /// and not free. A client that never started the Dreamport (a visitor) never has one.
        /// </summary>
        public bool HasRecord(int playerId)
        {
            if (playerId < 0 || !Online) return false;
            int localId = LocalId;
            IReadOnlyList<int> present = _t.Present;
            if (playerId == localId || !HaloSlotRules.Contains(present, playerId)) return false;
            for (int k = 0; k < _slotCount; k++)
            {
                int[] slot = _slots[k];
                if (_valid[k] && HaloNetWire.OwnerOf(slot) == playerId && HaloNetWire.StatusOf(slot) != HaloNetStatus.Free) return true;
            }

            return false;
        }

        /// <summary>For the log: slots, the own slot, players in the match, ints read / written since the last call.</summary>
        public string StatsLine()
        {
            string s = "Net" + _label + ": " + _slotCount + " slots, " + _worldCount + " world blocks, mine " + MySlot + ", " + _listed.Count + " other Dreamport player(s), " + _remotes.Count
                       + " in my match, " + _reads + " record reads, " + _writes + " ints written";
            _reads = 0;
            _writes = 0;
            return s;
        }

        internal void Log(string text)
        {
            if (_logs >= 300) return;
            _logs++;
            string line = "Net" + _label + ": " + text + (_logs == 300 ? " (later lines not logged)" : "");
            Action<string> sink = LogSink;
            if (sink != null) sink(line); else HaloLog.Info(line);
        }

        private static void Warn(string text) { HaloLog.Warn(text); }
    }
}

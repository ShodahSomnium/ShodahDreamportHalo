using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The instance master's world state, in the optional scene objects "Halo Net World 0..3" (NetworkBridgeData64 each, records 50..53 of the transport):
    /// what a late joiner cannot learn from the players' event counters - when each pickup was last taken, the game type's objectives (CTF flags, the oddball,
    /// the hill) and the unoccupied vehicles. Only the instance master writes it (it asks for the blocks' state authority); everybody reads it. A block is
    /// about one game: its header names the master's game (map hash, number, start) and a block of another game is not used. Pure C#.
    ///
    /// Each block: 0 magic 'HLW1' | 1 seq | 2 stamp | 3 writer PlayerId+1 (0-15), block index (16-19), protocol (24-27) | 4 game map hash | 5 game number |
    /// 6 game start UTC | 7-63 payload. The payload of the four blocks is one array of 228 ints:
    ///   0-11    objectives 0..3, three ints each: state (0-3) | carrier slot (4-9, 63 none) | timer (10-25) | flags (26-31); position (cm2, two ints)
    ///   12-15   four free ints of the Match module (hill index, ...)
    ///   16-79   pickups 0..127: when each was last taken, 16 bits each (seconds since the game's start + 1; 0 = never)
    ///   80-227  vehicles 0..36, four ints each: present (0) | destroyed (1) | health 0-255 (2-9) | flags (10-17); position (cm2); rotation
    /// </summary>
    public sealed class HaloNetWorld
    {
        public const string ObjectPrefix = "Halo Net World ";
        public const int MaxBlocks = 4;
        public const int Magic = 0x484C5731;   // 'HLW1'
        public const int Protocol = 1;
        public const int Header = 7;
        public const int PerBlock = HaloNetWire.Ints - Header;      // 57
        public const int Payload = PerBlock * MaxBlocks;            // 228
        public const int Objectives = 4, MiscInts = 4, Items = 128, Vehicles = 37;
        public const int AtObjectives = 0, AtMisc = 12, AtItems = 16, AtVehicles = 80;
        public const int NoCarrier = 63;

        private readonly int[] _read = new int[Payload];
        private readonly int[] _mine = new int[Payload];
        private readonly bool[] _blockOk = new bool[MaxBlocks];
        private readonly bool[] _dirty = new bool[MaxBlocks];
        private readonly int[][] _out = new int[MaxBlocks][];
        private int _zone, _seq, _start;
        private bool _master, _haveMine;
        private int _blocks;

        public HaloNetWorld()
        {
            for (int b = 0; b < MaxBlocks; b++) _out[b] = new int[HaloNetWire.Ints];
        }

        /// <summary>The game the state is about (the master's command); 0 = none.</summary>
        public int GameZone { get { return _zone; } }
        public int GameSeq { get { return _seq; } }
        public int GameStart { get { return _start; } }

        /// <summary>World blocks exist in the scene (some of the state can be shared).</summary>
        public int Blocks { get { return _blocks; } }

        /// <summary>This client writes the state (the instance master of a game).</summary>
        public bool Writing { get { return _master; } }

        private int[] Data { get { return _master ? _mine : _read; } }

        /// <summary>Whether the block holding a payload int arrived (or is mine).</summary>
        public bool Has(int payloadIndex)
        {
            if (_master) return true;
            int b = payloadIndex / PerBlock;
            return b >= 0 && b < MaxBlocks && _blockOk[b];
        }

        // ------------------------------------------------------------------ reading

        /// <summary>When a pickup was last taken: seconds since the game's start (false = never, or the state is not known).</summary>
        public bool ItemTakenAt(int item, out int secondsSinceStart)
        {
            secondsSinceStart = 0;
            if (item < 0 || item >= Items) return false;
            int p = AtItems + (item >> 1);
            if (!Has(p)) return false;
            int v = (item & 1) == 0 ? Data[p] & 0xFFFF : (Data[p] >> 16) & 0xFFFF;
            if (v == 0) return false;
            secondsSinceStart = v - 1;
            return true;
        }

        public bool Objective(int i, out int state, out int carrier, out int timer, out int flags, out HaloV3 pos)
        {
            state = 0; carrier = -1; timer = 0; flags = 0; pos = default(HaloV3);
            if (i < 0 || i >= Objectives) return false;
            int p = AtObjectives + i * 3;
            if (!Has(p)) return false;
            int a = Data[p];
            if (a == 0 && Data[p + 1] == 0 && Data[p + 2] == 0) return false;
            state = a & 0xF;
            int c = (a >> 4) & 0x3F;
            carrier = c == NoCarrier ? -1 : c;
            timer = (a >> 10) & 0xFFFF;
            flags = (a >> 26) & 0x3F;
            pos = HaloNetWire.UnpackCm2(Data[p + 1], Data[p + 2]);
            return true;
        }

        public bool Misc(int i, out int value)
        {
            value = 0;
            if (i < 0 || i >= MiscInts || !Has(AtMisc + i)) return false;
            value = Data[AtMisc + i];
            return true;
        }

        public bool Vehicle(int v, out bool destroyed, out float health, out int flags, out HaloV3 pos, out HaloQ rot)
        {
            destroyed = false; health = 0f; flags = 0; pos = default(HaloV3); rot = HaloQ.Identity;
            if (v < 0 || v >= Vehicles) return false;
            int p = AtVehicles + v * 4;
            if (!Has(p)) return false;
            int a = Data[p];
            if ((a & 1) == 0) return false;
            destroyed = (a & 2) != 0;
            health = ((a >> 2) & 0xFF) / 255f;
            flags = (a >> 10) & 0xFF;
            pos = HaloNetWire.UnpackCm2(Data[p + 1], Data[p + 2]);
            rot = HaloNetWire.UnpackQuat(Data[p + 3]);
            return true;
        }

        // ------------------------------------------------------------------ writing (the master)

        private void Put(int p, int value)
        {
            if (!_master || p < 0 || p >= Payload || _mine[p] == value) return;
            _mine[p] = value;
            _dirty[p / PerBlock] = true;
        }

        public void SetItemTaken(int item, int secondsSinceStart)
        {
            if (item < 0 || item >= Items) return;
            int p = AtItems + (item >> 1);
            int v = HaloNetWire.Clamp(secondsSinceStart + 1, 1, 0xFFFF);
            int cur = _mine[p];
            Put(p, (item & 1) == 0 ? (cur & unchecked((int)0xFFFF0000)) | v : (cur & 0xFFFF) | (v << 16));
        }

        public void SetObjective(int i, int state, int carrier, int timer, int flags, HaloV3 pos)
        {
            if (i < 0 || i >= Objectives) return;
            int p = AtObjectives + i * 3;
            int lo, hi;
            HaloNetWire.PackCm2(pos, out lo, out hi);
            Put(p, (state & 0xF) | ((carrier < 0 || carrier > 62 ? NoCarrier : carrier) << 4) | ((timer & 0xFFFF) << 10) | ((flags & 0x3F) << 26));
            Put(p + 1, lo);
            Put(p + 2, hi);
        }

        public void SetMisc(int i, int value)
        {
            if (i >= 0 && i < MiscInts) Put(AtMisc + i, value);
        }

        public void SetVehicle(int v, bool destroyed, float health, int flags, HaloV3 pos, HaloQ rot)
        {
            if (v < 0 || v >= Vehicles) return;
            int p = AtVehicles + v * 4;
            int lo, hi;
            HaloNetWire.PackCm2(pos, out lo, out hi);
            Put(p, 1 | (destroyed ? 2 : 0) | (HaloNetWire.Unit8(health, 1f) << 2) | ((flags & 0xFF) << 10));
            Put(p + 1, lo);
            Put(p + 2, hi);
            Put(p + 3, HaloNetWire.PackQuat(rot));
        }

        public void ClearVehicle(int v)
        {
            if (v < 0 || v >= Vehicles) return;
            int p = AtVehicles + v * 4;
            for (int i = 0; i < 4; i++) Put(p + i, 0);
        }

        // ------------------------------------------------------------------ the session's side (HaloNet.StepWorld)

        /// <summary>A new game (or none): the state starts over. A master that takes over the same game keeps what it read.</summary>
        internal void SetGame(int zone, int seq, int start, bool master, int blocks)
        {
            _blocks = blocks;
            bool same = zone == _zone && seq == _seq;      // (the start may be published a moment after the game: same game)
            _start = start;
            if (!same)
            {
                _zone = zone; _seq = seq; _start = start;
                Array.Clear(_read, 0, _read.Length);
                Array.Clear(_mine, 0, _mine.Length);
                Array.Clear(_blockOk, 0, _blockOk.Length);
                _haveMine = false;
            }

            if (master && !_haveMine)
            {
                Array.Copy(_read, _mine, Payload);      // what the old master wrote (zeros for a fresh game)
                _haveMine = true;
                for (int b = 0; b < MaxBlocks; b++) _dirty[b] = true;
            }
            else if (!master) _haveMine = false;

            _master = master && zone != 0;
        }

        /// <summary>A block as read: used when it is about the current game.</summary>
        internal void Take(int block, int[] rec)
        {
            bool ok = rec[0] == Magic && ((rec[3] >> 16) & 0xF) == block && rec[4] == _zone && (rec[5] & 0xFFFF) == _seq && _zone != 0;
            _blockOk[block] = ok;
            if (ok) Array.Copy(rec, Header, _read, block * PerBlock, PerBlock);
        }

        internal void Lost(int block) { _blockOk[block] = false; }

        /// <summary>The master's record for a block when it changed (null when nothing to write).</summary>
        internal int[] Outgoing(int block, int writerId, int stampMs, bool force)
        {
            if (!_master || (!_dirty[block] && !force)) return null;
            _dirty[block] = false;
            int[] o = _out[block];
            o[0] = Magic;
            o[1]++;
            o[2] = stampMs;
            o[3] = ((writerId + 1) & 0xFFFF) | ((block & 0xF) << 16) | (Protocol << 24);
            o[4] = _zone;
            o[5] = _seq & 0xFFFF;
            o[6] = _start;
            Array.Copy(_mine, block * PerBlock, o, Header, PerBlock);
            return o;
        }

        internal void MarkDirty(int block) { if (block >= 0 && block < MaxBlocks) _dirty[block] = true; }
    }

    public sealed partial class HaloNet
    {
        /// <summary>UTC seconds (Unix time, fractional) - the clock of game starts and pickup times.</summary>
        public static Func<double> UtcClock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        private double _nextWorldWrite, _nextWorldAsk, _nextWorldForce;
        private int _worldLogged = -1;

        /// <summary>Seconds since the start of the game I am in (or the master's game), 0 when none.</summary>
        public int GameSeconds
        {
            get
            {
                int start = Own.Status == HaloNetStatus.Playing && Own.MatchStart != 0 ? Own.MatchStart : _game.Start;
                if (start == 0) return 0;
                double s = UtcClock() - start;
                return s < 0 ? 0 : (int)s;
            }
        }


        // ------------------------------------------------------------------ the world writer

        /// <summary>
        /// Who writes the world blocks of the running game (parked vehicles, pickup times, objectives) and owns the empty vehicles: the master while it
        /// plays that game (or while nobody plays it yet), else the lowest-id player who plays it (the master sits in the hub). Every client works it out
        /// from the same records. A client stops writing at once and starts only after it has been the pick for <see cref="WriterSettle"/> s, so two
        /// never write at once. -1 = no game.
        /// </summary>
        public int WorldWriterId { get; private set; } = -1;

        /// <summary>This client owns the shared world now (offline / no game: the instance master).</summary>
        public bool WorldOwner { get { return !Online || MasterId < 0 || !_game.On ? IsInstanceMaster : WorldWriterId == LocalId; } }

        public const double WriterSettle = 0.5;
        private int _writerPick = -1;
        private double _writerPickAt;

        private static bool PlaysGame(in HaloNetState s, in GameInfo g)
        {
            return s.Status == HaloNetStatus.Playing && s.MatchState == HaloMatchState.Running && s.ZoneHash == g.ZoneHash && (s.MatchSeq & 0xFFFF) == (g.Seq & 0xFFFF);
        }

        private void StepWriter(double now, int localId)
        {
            GameInfo g = _game;
            int pick = -1;
            if (Online && g.On && MasterId >= 0)
            {
                int master = MasterId, lowest = -1;
                bool masterIn = false;
                if (localId >= 0 && PlaysGame(in Own, in g)) { lowest = localId; masterIn = localId == master; }
                for (int i = 0; i < _listed.Count; i++)
                {
                    Remote r = _listed[i];
                    if (!PlaysGame(in r.State, in g)) continue;
                    if (r.PlayerId == master) masterIn = true;
                    if (lowest < 0 || r.PlayerId < lowest) lowest = r.PlayerId;
                }

                pick = masterIn || lowest < 0 ? master : lowest;
            }

            if (pick != _writerPick) { _writerPick = pick; _writerPickAt = now; }
            int was = WorldWriterId;
            if (pick != localId || localId < 0) WorldWriterId = pick;                  // (stop at once)
            else if (now - _writerPickAt >= WriterSettle) WorldWriterId = pick;      // (start once the old writer has surely stopped)
            if (WorldWriterId != was && WorldWriterId >= 0 && Online)
                Log("world writer: " + (WorldWriterId == localId ? "this client" : "player " + WorldWriterId) + (WorldWriterId == MasterId ? " (the master)" : " (the master is not in the game)") + ".");
        }
        private void StepWorld(double now, int localId)
        {
            StepWriter(now, localId);
            bool master = Online && localId >= 0 && WorldWriterId == localId;      // (the master, or the lowest player in the game while the master is not in it)
            GameInfo g = _game;
            World.SetGame(g.On ? g.ZoneHash : 0, g.On ? g.Seq : 0, g.On ? g.Start : 0, master && g.On, _worldCount);
            for (int b = 0; b < HaloNetWorld.MaxBlocks; b++)
            {
                int k = MaxSlots + b;
                if (b < _worldCount && _valid[k]) World.Take(b, _slots[k]);
                else World.Lost(b);
            }

            if (_worldCount != _worldLogged)
            {
                _worldLogged = _worldCount;
                if (Online) Log(_worldCount + " world block(s) found" + (_worldCount == 0 ? " (no 'Halo Net World' objects in the scene: parked vehicles, objectives and pickup timers are not shared with late joiners)." : "."));
            }

            if (!World.Writing || _worldCount == 0) return;
            if (now >= _nextWorldAsk)
            {
                _nextWorldAsk = now + 1.0;
                for (int b = 0; b < _worldCount; b++)
                    if (_valid[MaxSlots + b] && !_authority[MaxSlots + b]) _t.RequestAuthority(MaxSlots + b);
            }

            if (now < _nextWorldWrite) return;
            _nextWorldWrite = now + 0.2;
            bool force = now >= _nextWorldForce;
            if (force) _nextWorldForce = now + 2.0;
            for (int b = 0; b < _worldCount; b++)
            {
                int k = MaxSlots + b;
                if (!_valid[k] || !_authority[k]) { World.MarkDirty(b); continue; }
                int[] rec = World.Outgoing(b, localId, StampMs(), force);
                if (rec != null) Push(k, rec);
            }
        }

        /// <summary>The master notes when an item was taken (its own pickups and everybody's pickup events).</summary>
        private void WorldPickup(int item)
        {
            if (World.Writing && item >= 0 && item < HaloNetWorld.Items) World.SetItemTaken(item, GameSeconds);
        }
    }
}

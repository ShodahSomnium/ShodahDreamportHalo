using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>One AI unit of the host as it travels (see <see cref="HaloNetCoop"/>).</summary>
    public struct HaloNetActor
    {
        /// <summary>The host's number for the unit (1..4095; 0 = an empty place).</summary>
        public int Serial;
        public int Team;                 // HaloTeam 0..7
        public bool Alive, Crouch, Firing;
        public int UnitTag, VariantTag, WeaponTag;      // tag indices (0xFFFF none)
        public HaloV3 Position, Velocity;
        public float BodyYaw, AimYaw, AimPitch;         // degrees
        public float Body, Shield;                       // 0..1 (shield 0..3)
        public int FireCounter, MeleeCounter, GrenadeCounter, Anim;
        /// <summary>A unit in a vehicle seat: the vehicle's id (HaloVehicle.Id, -1 on foot), the seat index; Exiting = its exit clip plays.</summary>
        public int SeatVehicle, Seat;
        public bool Exiting;
        /// <summary>A unit's body state (HaloUnit): none, feigning death (with the death clip it holds: hard, direction 0-3, damage region 0-31),
        /// latched onto a unit (infection form), leaping (flood); CamoHidden = its active camouflage hides it right now.</summary>
        public const int BodyNone = 0, BodyFeigning = 1, BodyLatched = 2, BodyLeaping = 3;
        public int BodyState;
        public bool CamoHidden, FeignHard;
        public int FeignDir, FeignRegion;
        /// <summary>A vehicle record (team 7 on the wire) instead of a unit: its id, rotation, vitality, the last shot (muzzle offset in the vehicle's frame and direction), hatch open, hovering.</summary>
        public bool IsVehicle, Open, Hover;
        public int VehicleId;
        public HaloQ Rotation;
        public HaloV3 MuzzleOffset, ShotDir;
        public float Vitality;
    }

    /// <summary>Something the host's mission did that every client replays (an object created / destroyed by its scenario name index).</summary>
    public struct HaloNetMissionEvent
    {
        public const int Create = 1, Destroy = 2, Voice = 3;
        /// <summary>Voice: Value = the unit's serial (0-11) | the sound tag index (12-27).</summary>
        public int Kind, Value;
    }

    /// <summary>
    /// The campaign co-op state the host (the instance master) writes and every other client reads: the AI units ("Halo Net Actor 0..8", 63 units)
    /// and the mission ("Halo Net Mission 0": BSP, cutscene camera, checkpoint, an event ring; "Halo Net Mission 1": device positions). CoopBlocks
    /// <see cref="FirstActor"/>.. of the transport. Keyed on the master's game (map hash + number) like the world blocks. Pure C#.
    /// Layouts: DevTools/agents/N/NOTES.md "Campaign co-op".
    /// </summary>
    public sealed class HaloNetCoop
    {
        public const string ActorPrefix = "Halo Net Actor ", MissionPrefix = "Halo Net Mission ";
        public const int ActorBlocks = 9, MissionBlocks = 2, PerBlock = 7, Ints = 8, Header = 8;
        public const int MaxActors = ActorBlocks * PerBlock;      // 63
        public const int Devices = 256, Ring = 32;
        public const int FirstActor = HaloNetWire.MaxSlots + HaloNetWorld.MaxBlocks;      // record 54
        public const int FirstMission = FirstActor + ActorBlocks;                          // record 63
        public const int CoopBlocks = ActorBlocks + MissionBlocks, HostBlocks = 4;
        public const int Records = CoopBlocks + HostBlocks;
        public const string HostPrefix = "Halo Net Host ";
        public const int ActorMagic = 0x484C4131, MissionMagic = 0x484C4D31;     // 'HLA1', 'HLM1'

        // ---- what the host says (writer) / what was read (reader)
        private readonly HaloNetActor[] _actors = new HaloNetActor[MaxActors];
        private readonly int[][] _out = new int[CoopBlocks][];
        private readonly bool[] _dirty = new bool[CoopBlocks];
        private readonly bool[] _blockOk = new bool[CoopBlocks];
        private readonly int[][] _in = new int[CoopBlocks][];
        private readonly int[] _stamp = new int[ActorBlocks];

        /// <summary>The writer's clock (ms) of the block holding an actor place: the time of its pose (followers interpolate on it).</summary>
        public int StampOf(int place) { return place >= 0 && place < MaxActors ? _stamp[place / PerBlock] : 0; }
        private int _zone, _seq;
        private bool _writing;

        // mission state
        public int Difficulty, Bsp = -1, CameraPoint = -1, Checkpoints;
        public bool Cutscene, Letterbox;
        private readonly byte[] _devices = new byte[Devices];
        private readonly HaloNetMissionEvent[] _ring = new HaloNetMissionEvent[Ring];
        private int _events;            // the host's event counter (writer) / the last one read (reader)
        private int _readEvents = -1;

        /// <summary>Events read since the last <see cref="TakeEvents"/> (reader side).</summary>
        public readonly List<HaloNetMissionEvent> NewEvents = new List<HaloNetMissionEvent>(32);

        public HaloNetCoop()
        {
            for (int b = 0; b < CoopBlocks; b++) { _out[b] = new int[HaloNetWire.Ints]; _in[b] = new int[HaloNetWire.Ints]; }
        }

        public bool Writing { get { return _writing; } }

        /// <summary>The actor blocks of the current game have arrived (reader) or are mine (writer).</summary>
        public bool HaveActors { get { if (_writing) return true; for (int b = 0; b < ActorBlocks; b++) if (_blockOk[b]) return true; return false; } }

        public bool HaveMission { get { return _writing || _blockOk[ActorBlocks]; } }

        /// <summary>The actor at a place 0..62 (Serial 0 = empty).</summary>
        public HaloNetActor Actor(int i) { return i >= 0 && i < MaxActors ? _actors[i] : default(HaloNetActor); }

        public float Device(int i) { return i >= 0 && i < Devices ? _devices[i] / 255f : 0f; }


        // ------------------------------------------------------------------ control presses (K's PressCount / LastPressDenied for followers)

        /// <summary>
        /// Press table: header int 5 bits 16-31 of the 10 header blocks (9 actor blocks + mission block 0), one control each: placement (0-7) |
        /// press count mod 8 (8-10) | denied (11) | used (12). The host puts the controls pressed last there (a slot is reused oldest first);
        /// everybody keeps each placement's count, so a follower plays a press that does not move the device (and a deny) once. A late joiner's
        /// first read only takes the counts. A new host continues the counts it read.
        /// </summary>
        public const int PressSlots = ActorBlocks + 1;
        private readonly int[] _press = new int[PressSlots], _pressAge = new int[PressSlots];
        private readonly bool[] _pressSeen = new bool[PressSlots];
        private readonly byte[] _pressCount = new byte[Devices];
        private int _pressClock;

        /// <summary>Follower: presses read since the last call (placement 0-7 | presses 8-10 | denied 11), to apply and clear.</summary>
        public readonly List<int> NewPresses = new List<int>(16);

        /// <summary>Host: the control at <paramref name="placement"/> was pressed <paramref name="presses"/> more times (the last one denied or not).</summary>
        public void NotePress(int placement, int presses, bool denied)
        {
            if (!_writing || placement < 0 || placement >= Devices || presses <= 0) return;
            int c = (_pressCount[placement] + presses) & 7;
            _pressCount[placement] = (byte)c;
            int slot = -1, oldest = 0;
            for (int s = 0; s < PressSlots; s++)
            {
                int e = _press[s];
                if ((e & (1 << 12)) != 0 && (e & 0xFF) == placement) { slot = s; break; }
                if (slot < 0 && (e & (1 << 12)) == 0) slot = s;
                if (_pressAge[s] < _pressAge[oldest]) oldest = s;
            }

            if (slot < 0) slot = oldest;
            _press[slot] = (placement & 0xFF) | (c << 8) | (denied ? 1 << 11 : 0) | (1 << 12);
            _pressAge[slot] = ++_pressClock;
            _dirty[slot] = true;
        }

        public int PressEntry(int slot) { return slot >= 0 && slot < PressSlots ? _press[slot] : 0; }

        private void ReadPress(int b, int entry)
        {
            _press[b] = entry;
            bool first = !_pressSeen[b];
            _pressSeen[b] = true;
            if ((entry & (1 << 12)) == 0) return;
            int p = entry & 0xFF, c = (entry >> 8) & 7;
            int delta = (c - _pressCount[p]) & 7;
            _pressCount[p] = (byte)c;
            if (first || delta == 0) return;
            NewPresses.Add(p | (delta << 8) | ((entry & (1 << 11)) != 0 ? 1 << 11 : 0));
        }
        // ------------------------------------------------------------------ the host

        public void SetActor(int i, in HaloNetActor a)
        {
            if (!_writing || i < 0 || i >= MaxActors) return;
            _actors[i] = a;
            _dirty[i / PerBlock] = true;
        }

        public void ClearActor(int i)
        {
            if (!_writing || i < 0 || i >= MaxActors || _actors[i].Serial == 0) return;
            _actors[i] = default(HaloNetActor);
            _dirty[i / PerBlock] = true;
        }

        public void SetDevice(int i, float position01)
        {
            if (!_writing || i < 0 || i >= Devices) return;
            byte v = (byte)HaloNetWire.Unit8(position01, 1f);
            if (_devices[i] == v) return;
            _devices[i] = v;
            _dirty[ActorBlocks + 1] = true;
        }

        public void SetMission(int difficulty, int bsp, bool cutscene, bool letterbox, int cameraPoint, int checkpoints)
        {
            if (!_writing) return;
            if (Difficulty == difficulty && Bsp == bsp && Cutscene == cutscene && Letterbox == letterbox && CameraPoint == cameraPoint && Checkpoints == checkpoints) return;
            Difficulty = difficulty; Bsp = bsp; Cutscene = cutscene; Letterbox = letterbox; CameraPoint = cameraPoint; Checkpoints = checkpoints;
            _dirty[ActorBlocks] = true;
        }

        public void AddEvent(int kind, int value)
        {
            if (!_writing) return;
            _ring[_events % Ring] = new HaloNetMissionEvent { Kind = kind & 0xF, Value = value & 0xFFFFFFF };
            _events = (_events + 1) & 0xFFFF;
            _dirty[ActorBlocks] = true;
        }

        // ------------------------------------------------------------------ sounds: a ring of one-shots and a table of loops

        /// <summary>Mission block 0, ints 40-63: the sound ring - 8 entries of 3 ints (see HaloNetGame.CoopSound); its counter is int 7's high half.</summary>
        public const int SoundRing = 8, SoundAt = 40;
        /// <summary>Loops playing now: one per actor block, in its header ints 6-7 (state: a late joiner gets them).</summary>
        public const int Loops = ActorBlocks;

        private readonly int[] _soundRing = new int[SoundRing * 3];
        private int _sounds, _readSounds = -1;
        private readonly int[] _loopA = new int[ActorBlocks], _loopB = new int[ActorBlocks];

        /// <summary>One-shot sound / effect entries read since the last clear (reader side): 3 ints each.</summary>
        public readonly List<int> NewSounds = new List<int>(48);

        /// <summary>Host: a one-shot (an impulse, a stop, an effect, a conversation line, a class gain) - three ints of HaloNetGame.CoopSound's layout.</summary>
        public void AddSound(int a, int b, int c)
        {
            if (!_writing) return;
            int at = (_sounds % SoundRing) * 3;
            _soundRing[at] = a; _soundRing[at + 1] = b; _soundRing[at + 2] = c;
            _sounds = (_sounds + 1) & 0xFFFF;
            _dirty[ActorBlocks] = true;
        }

        /// <summary>Host: a loop slot 0..8 (a = 0: none).</summary>
        public void SetLoop(int slot, int a, int b)
        {
            if (!_writing || slot < 0 || slot >= Loops) return;
            if (_loopA[slot] == a && _loopB[slot] == b) return;
            _loopA[slot] = a; _loopB[slot] = b;
            _dirty[slot] = true;
        }

        public int LoopA(int slot) { return slot >= 0 && slot < Loops ? _loopA[slot] : 0; }
        public int LoopB(int slot) { return slot >= 0 && slot < Loops ? _loopB[slot] : 0; }

        private void ReadSounds(int[] rec, bool late)
        {
            int counter = (rec[7] >> 16) & 0xFFFF;
            if (_readSounds < 0) _readSounds = counter;      // (a late joiner does not replay one-shots)
            int delta = (counter - _readSounds) & 0xFFFF;
            if (delta > SoundRing) delta = SoundRing;
            for (int n = delta; n > 0; n--)
            {
                int e = (counter - n) & 0xFFFF;
                int at = SoundAt + (e % SoundRing) * 3;
                NewSounds.Add(rec[at]); NewSounds.Add(rec[at + 1]); NewSounds.Add(rec[at + 2]);
            }

            _readSounds = counter;
            _sounds = counter;      // (a new host continues the ring)
            for (int i = 0; i < SoundRing * 3; i++) _soundRing[i] = rec[SoundAt + i];
        }

        // ------------------------------------------------------------------ the session's side (HaloNet.StepCoop)

        /// <summary>The game (map hash + number) this state is about, and whether this client writes it (the host).</summary>
        internal void SetGame(int zone, int seq, bool writing)
        {
            if (zone != _zone || seq != _seq)
            {
                _zone = zone; _seq = seq;
                Array.Clear(_actors, 0, _actors.Length);
                Array.Clear(_devices, 0, _devices.Length);
                Array.Clear(_blockOk, 0, _blockOk.Length);
                Bsp = -1; CameraPoint = -1; Cutscene = false; Letterbox = false; Checkpoints = 0; Difficulty = 0;
                _readEvents = -1;
                _readSounds = -1;
                Array.Clear(_loopA, 0, _loopA.Length);
                Array.Clear(_loopB, 0, _loopB.Length);
                Array.Clear(_press, 0, _press.Length); Array.Clear(_pressAge, 0, _pressAge.Length); Array.Clear(_pressSeen, 0, _pressSeen.Length); Array.Clear(_pressCount, 0, _pressCount.Length);
                NewPresses.Clear();
                NewEvents.Clear();
                if (writing) _events = 0;
            }

            if (writing && !_writing) for (int b = 0; b < CoopBlocks; b++) _dirty[b] = true;      // (a new host writes everything once; it continues from what it read)
            _writing = writing && zone != 0;
        }

        /// <summary>A block as read (b = 0..10).</summary>
        internal void Take(int b, int[] rec)
        {
            bool actor = b < ActorBlocks;
            int magic = actor ? ActorMagic : MissionMagic;
            int index = actor ? b : b - ActorBlocks;
            bool ok = actor || index == 0
                ? _zone != 0 && rec[0] == magic && ((rec[3] >> 16) & 0xF) == index && rec[4] == _zone && (rec[5] & 0xFFFF) == _seq
                : _blockOk[ActorBlocks];      // (the device block has no header: it belongs to the mission block's game)
            _blockOk[b] = ok;
            if (!ok || _writing) return;
            Array.Copy(rec, _in[b], HaloNetWire.Ints);
            if (actor) { _stamp[b] = rec[2]; _loopA[b] = rec[6]; _loopB[b] = rec[7]; }
            if (actor || index == 0) ReadPress(b, (rec[5] >> 16) & 0xFFFF);
            if (actor)
            {
                for (int k = 0; k < PerBlock; k++) _actors[b * PerBlock + k] = UnpackActor(rec, Header + k * Ints);
            }
            else if (index == 0) ReadMission(rec);
            else for (int i = 0; i < Devices; i++) _devices[i] = (byte)((rec[i >> 2] >> ((i & 3) * 8)) & 0xFF);
        }

        internal void Lost(int b) { _blockOk[b] = false; }

        private void ReadMission(int[] rec)
        {
            int s = rec[6];
            Difficulty = s & 3;
            int bsp = (s >> 2) & 0xFF;
            Bsp = bsp == 0xFF ? -1 : bsp;
            Cutscene = (s & (1 << 10)) != 0;
            Letterbox = (s & (1 << 11)) != 0;
            CameraPoint = ((s >> 12) & 0xFFF) - 1;
            Checkpoints = (s >> 24) & 0xFF;
            int counter = rec[7] & 0xFFFF;
            bool late = _readEvents < 0;
            if (late) _readEvents = (counter - Math.Min(counter, Ring)) & 0xFFFF;      // a late joiner replays what the ring still holds
            int delta = (counter - _readEvents) & 0xFFFF;
            if (delta > Ring) delta = Ring;
            for (int n = delta; n > 0; n--)
            {
                int e = (counter - n) & 0xFFFF;
                int v = rec[8 + (e % Ring)];
                if (late && (v & 0xF) == HaloNetMissionEvent.Voice) continue;      // (a late joiner does not hear old lines)
                NewEvents.Add(new HaloNetMissionEvent { Kind = v & 0xF, Value = (v >> 4) & 0xFFFFFFF });
            }

            _readEvents = counter;
            _events = counter;      // (a new host continues the ring)
            ReadSounds(rec, late);
            for (int i = 0; i < Ring; i++) _ring[i] = new HaloNetMissionEvent { Kind = rec[8 + i] & 0xF, Value = (rec[8 + i] >> 4) & 0xFFFFFFF };
        }

        /// <summary>The host's record for a block when it changed (null when nothing to write).</summary>
        internal int[] Outgoing(int b, int writerId, int stampMs, bool force)
        {
            if (!_writing || (!_dirty[b] && !force)) return null;
            _dirty[b] = false;
            int[] o = _out[b];
            bool actor = b < ActorBlocks;
            int index = actor ? b : b - ActorBlocks;
            if (actor || index == 0)
            {
                o[0] = actor ? ActorMagic : MissionMagic;
                o[1]++;
                o[2] = stampMs;
                o[3] = ((writerId + 1) & 0xFFFF) | ((index & 0xF) << 16) | (1 << 24);
                o[4] = _zone;
                o[5] = (_seq & 0xFFFF) | ((_press[b] & 0xFFFF) << 16);
            }

            if (actor)
            {
                o[6] = _loopA[b];
                o[7] = _loopB[b];
                for (int k = 0; k < PerBlock; k++) PackActor(_actors[b * PerBlock + k], o, Header + k * Ints);
            }
            else if (index == 0)
            {
                o[6] = (Difficulty & 3) | (((Bsp < 0 ? 0xFF : Bsp) & 0xFF) << 2) | (Cutscene ? 1 << 10 : 0) | (Letterbox ? 1 << 11 : 0) | (((CameraPoint + 1) & 0xFFF) << 12) | ((Checkpoints & 0xFF) << 24);
                o[7] = (_events & 0xFFFF) | ((_sounds & 0xFFFF) << 16);
                for (int i = 0; i < SoundRing * 3; i++) o[SoundAt + i] = _soundRing[i];
                for (int i = 0; i < Ring; i++) o[8 + i] = (_ring[i].Kind & 0xF) | ((_ring[i].Value & 0xFFFFFFF) << 4);
            }
            else
            {
                for (int i = 0; i < HaloNetWire.Ints; i++)
                    o[i] = _devices[i * 4] | (_devices[i * 4 + 1] << 8) | (_devices[i * 4 + 2] << 16) | (_devices[i * 4 + 3] << 24);
            }

            return o;
        }

        internal void MarkDirty(int b) { if (b >= 0 && b < CoopBlocks) _dirty[b] = true; }

        // ------------------------------------------------------------------ an actor's 8 ints

        /// <summary>The team value on the wire that marks a vehicle record.</summary>
        public const int VehicleTeam = 7;
        /// <summary>Unit serials stay below this; a vehicle's serial is VehicleSerialBase + its id.</summary>
        public const int VehicleSerialBase = 3840;
        public const int NoVehicle = 511;

        /// <summary>
        /// An actor's 8 ints. A unit: a0 serial (0-11) | team (12-14) | alive (15) | unit tag (16-31); a1-a2 position (cm2); a3 body yaw 16 | aim yaw 16;
        /// a4 aim pitch 16 | body 8 | shield 8; a5 velocity; a6 weapon tag 16 | fire 8 | melee 4 | grenade 4; a7 variant tag 16 | crouch (16) |
        /// firing (17) | exiting (18) | seat (19-22, 15 none) | vehicle id (23-31, 511 none).
        /// Body state (units only, in bits the position / velocity leave free): a2 bit 31 = camouflage hides it; a5 bits 30-31 = body state (0 none,
        /// 1 feigning death, 2 latched, 3 leaping); while feigning a5 bits 0-29 carry the held death clip instead of the velocity (a feigning
        /// unit lies still): hard (0) | direction (1-2) | damage region (3-7).
        /// A vehicle (team 7): a0 serial | 7 | alive (= not destroyed) | vehicle tag; a1-a2 position; a3 rotation (smallest three); a4 the last shot's
        /// muzzle in the vehicle's frame (3 x 10 bits of 1 cm) | open (30) | hovering (31); a5 velocity; a6 vehicle id (0-15) | fire counter (16-23) |
        /// vitality (24-31); a7 the last shot's direction.
        /// </summary>
        public static void PackActor(in HaloNetActor a, int[] o, int at)
        {
            if (a.Serial == 0) { for (int i = 0; i < Ints; i++) o[at + i] = 0; return; }
            int lo, hi;
            HaloNetWire.PackCm2(a.Position, out lo, out hi);
            o[at + 1] = lo;
            o[at + 2] = hi;
            o[at + 5] = HaloNetWire.PackVel(a.Velocity);
            if (a.IsVehicle)
            {
                o[at] = (a.Serial & 0xFFF) | (VehicleTeam << 12) | (a.Alive ? 1 << 15 : 0) | ((a.UnitTag & 0xFFFF) << 16);
                o[at + 3] = HaloNetWire.PackQuat(a.Rotation);
                o[at + 4] = (Cm10(a.MuzzleOffset.X) << 20) | (Cm10(a.MuzzleOffset.Y) << 10) | Cm10(a.MuzzleOffset.Z) | (a.Open ? 1 << 30 : 0) | (a.Hover ? 1 << 31 : 0);
                o[at + 6] = (a.VehicleId & 0xFFFF) | ((a.FireCounter & 0xFF) << 16) | (HaloNetWire.Unit8(a.Vitality, 1f) << 24);
                o[at + 7] = HaloNetWire.PackDir(a.ShotDir);
                return;
            }

            o[at] = (a.Serial & 0xFFF) | ((a.Team & 7) << 12) | (a.Alive ? 1 << 15 : 0) | ((a.UnitTag & 0xFFFF) << 16);
            o[at + 3] = HaloNetWire.Yaw16(a.BodyYaw) | (HaloNetWire.Yaw16(a.AimYaw) << 16);
            int pitch = (int)Math.Round((Math.Max(-90f, Math.Min(90f, a.AimPitch)) + 90f) / 180f * 65535f);
            o[at + 4] = (pitch & 0xFFFF) | (HaloNetWire.Unit8(a.Body, 1f) << 16) | (HaloNetWire.Unit8(a.Shield, 3f) << 24);
            o[at + 6] = (a.WeaponTag & 0xFFFF) | ((a.FireCounter & 0xFF) << 16) | ((a.MeleeCounter & 0xF) << 24) | ((a.GrenadeCounter & 0xF) << 28);
            bool seated = a.SeatVehicle >= 0 && a.SeatVehicle < NoVehicle && a.Seat >= 0 && a.Seat < 15;
            o[at + 7] = (a.VariantTag & 0xFFFF) | (a.Crouch ? 1 << 16 : 0) | (a.Firing ? 1 << 17 : 0) | (a.Exiting ? 1 << 18 : 0)
                        | ((seated ? a.Seat : 15) << 19) | ((seated ? a.SeatVehicle : NoVehicle) << 23);
            int body = a.BodyState & 3;
            if (body == HaloNetActor.BodyFeigning) o[at + 5] = (a.FeignHard ? 1 : 0) | ((a.FeignDir & 3) << 1) | ((a.FeignRegion & 31) << 3);
            o[at + 5] = (o[at + 5] & 0x3FFFFFFF) | (body << 30);
            if (a.CamoHidden) o[at + 2] |= 1 << 31;
        }

        public static HaloNetActor UnpackActor(int[] r, int at)
        {
            var a = default(HaloNetActor);
            a.SeatVehicle = -1; a.Seat = -1;
            int h = r[at];
            a.Serial = h & 0xFFF;
            if (a.Serial == 0) return a;
            a.Team = (h >> 12) & 7;
            a.Alive = (h & (1 << 15)) != 0;
            a.UnitTag = (h >> 16) & 0xFFFF;
            a.Position = HaloNetWire.UnpackCm2(r[at + 1], r[at + 2]);
            a.Velocity = HaloNetWire.UnpackVel(r[at + 5]);
            if (a.Team == VehicleTeam)
            {
                a.IsVehicle = true;
                a.Rotation = HaloNetWire.UnpackQuat(r[at + 3]);
                int m = r[at + 4];
                a.MuzzleOffset = new HaloV3(FromCm10((m >> 20) & 0x3FF), FromCm10((m >> 10) & 0x3FF), FromCm10(m & 0x3FF));
                a.Open = (m & (1 << 30)) != 0;
                a.Hover = (m & (1 << 31)) != 0;
                a.VehicleId = r[at + 6] & 0xFFFF;
                a.FireCounter = (r[at + 6] >> 16) & 0xFF;
                a.Vitality = ((r[at + 6] >> 24) & 0xFF) / 255f;
                a.ShotDir = HaloNetWire.UnpackDir(r[at + 7]);
                a.WeaponTag = HaloNetWire.NoTag;
                a.VariantTag = HaloNetWire.NoTag;
                return a;
            }

            a.BodyYaw = (r[at + 3] & 0xFFFF) * (360f / 65536f);
            a.AimYaw = ((r[at + 3] >> 16) & 0xFFFF) * (360f / 65536f);
            a.AimPitch = (r[at + 4] & 0xFFFF) / 65535f * 180f - 90f;
            a.Body = ((r[at + 4] >> 16) & 0xFF) / 255f;
            a.Shield = ((r[at + 4] >> 24) & 0xFF) * (3f / 255f);
            a.WeaponTag = r[at + 6] & 0xFFFF;
            a.FireCounter = (r[at + 6] >> 16) & 0xFF;
            a.MeleeCounter = (r[at + 6] >> 24) & 0xF;
            a.GrenadeCounter = (r[at + 6] >> 28) & 0xF;
            int s = r[at + 7];
            a.VariantTag = s & 0xFFFF;
            a.Crouch = (s & (1 << 16)) != 0;
            a.Firing = (s & (1 << 17)) != 0;
            a.Exiting = (s & (1 << 18)) != 0;
            int seat = (s >> 19) & 0xF, veh = (s >> 23) & 0x1FF;
            if (seat != 15 && veh != NoVehicle) { a.Seat = seat; a.SeatVehicle = veh; }
            int v = r[at + 5];
            a.BodyState = (v >> 30) & 3;
            a.CamoHidden = (r[at + 2] & (1 << 31)) != 0;
            if (a.BodyState == HaloNetActor.BodyFeigning)
            {
                a.FeignHard = (v & 1) != 0;
                a.FeignDir = (v >> 1) & 3;
                a.FeignRegion = (v >> 3) & 31;
                a.Velocity = default(HaloV3);
            }
            else a.Velocity = HaloNetWire.UnpackVel(v & 0x3FFFFFFF);
            return a;
        }

        private static int Cm10(float metres)
        {
            int i = (int)Math.Round(metres * 100f) + 512;
            return i < 0 ? 0 : i > 1023 ? 1023 : i;
        }

        private static float FromCm10(int q) { return (q - 512) / 100f; }
    }

    public sealed partial class HaloNet
    {
        /// <summary>The campaign co-op state (the host writes, everybody reads).</summary>
        public readonly HaloNetCoop Coop = new HaloNetCoop();

        /// <summary>The game layer says a campaign mission of the master's game runs here (the host's state is written only then).</summary>
        public bool CoopMission;

        private int _coopCount, _coopLogged = -1;
        private double _nextCoopWrite, _nextCoopAsk, _nextCoopForce;

        private void StepCoop(double now, int localId)
        {
            bool master = IsInstanceMaster && Online && MasterId >= 0;
            GameInfo g = _game;
            bool on = g.On && CoopMission;
            Coop.SetGame(on ? g.ZoneHash : 0, on ? g.Seq : 0, master && on);
            for (int b = 0; b < HaloNetCoop.CoopBlocks; b++)
            {
                int k = HaloNetCoop.FirstActor + b;
                if (b < _coopCount && _valid[k]) Coop.Take(b, _slots[k]);
                else Coop.Lost(b);
            }

            if (_coopCount != _coopLogged)
            {
                _coopLogged = _coopCount;
                if (Online) Log(_coopCount + " co-op block(s) found" + (_coopCount < HaloNetCoop.Records ? " (the scene needs 'Halo Net Actor 0..8', 'Halo Net Mission 0..1' and 'Halo Net Host 0..3' for campaign co-op and its hand-over)." : "."));
            }

            if (!Coop.Writing || _coopCount == 0) return;
            if (now >= _nextCoopAsk)
            {
                _nextCoopAsk = now + 1.0;
                for (int b = 0; b < Math.Min(_coopCount, HaloNetCoop.CoopBlocks); b++)
                {
                    int k = HaloNetCoop.FirstActor + b;
                    if (_valid[k] && !_authority[k]) _t.RequestAuthority(k);
                }
            }

            if (now < _nextCoopWrite) return;
            _nextCoopWrite = now + 0.1;
            bool force = now >= _nextCoopForce;
            if (force) _nextCoopForce = now + 2.0;
            for (int b = 0; b < Math.Min(_coopCount, HaloNetCoop.CoopBlocks); b++)
            {
                int k = HaloNetCoop.FirstActor + b;
                if (!_valid[k] || !_authority[k]) { Coop.MarkDirty(b); continue; }
                int[] rec = Coop.Outgoing(b, localId, StampMs(), force);
                if (rec != null) Push(k, rec);
            }
        }
    }
}

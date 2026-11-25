using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The unit control a recorded animation drives each tick (Halo's unit_control_data): animation state, aiming speed, control flags,
    /// weapon / grenade index, throttle (forward, left; the unit's own frame, -1..1) and the facing / aiming / looking directions as yaw /
    /// pitch in Halo's recording units (pi / 1000 radians; yaw 0 = +X, counter-clockwise about +Z; pitch up positive). Unity-free.
    /// </summary>
    public struct HaloRecordedControl
    {
        /// <summary>One angle unit of a recording, radians (verified: header yaw / pitch pairs match the header's float vectors).</summary>
        public const float AngleUnit = (float)(Math.PI / 1000.0);

        // Halo's unit control flags (bit order of unit_control_data.control_flags)
        public const int Crouch = 1 << 0, JumpBit = 1 << 1, User1 = 1 << 2, User2 = 1 << 3, Light = 1 << 4, ExactFacing = 1 << 5,
            Action = 1 << 6, MeleeBit = 1 << 7, LookDontTurn = 1 << 8, ForceAlert = 1 << 9, Reload = 1 << 10, PrimaryTrigger = 1 << 11,
            SecondaryTrigger = 1 << 12, GrenadeBit = 1 << 13, SwapWeapon = 1 << 14;

        public int AnimationState, AimingSpeed, Flags, Weapon, Grenade;
        public float ThrottleForward, ThrottleLeft;
        public int FacingYaw, FacingPitch, AimYaw, AimPitch, LookYaw, LookPitch;

        public bool Has(int flag) { return (Flags & flag) != 0; }
        public float FacingYawRadians => FacingYaw * AngleUnit;
        public float FacingPitchRadians => FacingPitch * AngleUnit;
        public float AimYawRadians => AimYaw * AngleUnit;
        public float AimPitchRadians => AimPitch * AngleUnit;
        public float LookYawRadians => LookYaw * AngleUnit;
        public float LookPitchRadians => LookPitch * AngleUnit;

        /// <summary>A Halo-space unit vector from a yaw / pitch pair in recording units.</summary>
        public static HVec3 Direction(int yaw, int pitch)
        {
            double y = yaw * AngleUnit, p = pitch * AngleUnit;
            return new HVec3((float)(Math.Cos(y) * Math.Cos(p)), (float)(Math.Sin(y) * Math.Cos(p)), (float)Math.Sin(p));
        }

        public override string ToString()
        {
            return "anim " + AnimationState + " flags " + Flags.ToString("X") + " weapon " + Weapon + " throttle " + ThrottleForward.ToString("0.##") + "," + ThrottleLeft.ToString("0.##")
                   + " facing " + FacingYaw + "/" + FacingPitch + " aim " + AimYaw + "/" + AimPitch;
        }
    }

    /// <summary>
    /// One scenario recorded animation (scenario +876, 64 B: name, version, raw animation data, unit control data version, length in
    /// ticks, event stream data ref +44) = Halo's cutscene recording: the unit control of a unit, tick by tick, played back by
    /// recording_play. Reverse-engineered stream (all 349 recordings of the 10 campaign maps parse to their end; the event delays add up
    /// to length - 1 in every one):
    ///   header: u8 animation state, u8 aiming speed, u16 control flags, i16 weapon index, (unit control version >= 1: i8 grenade index,
    ///     i8 zoom), 8 B throttle (2 floats), facing / aiming / looking vectors (3 x 3 floats) = 52 B; then version 0: the three yaw / pitch
    ///     i16 pairs (12 B); version 3: 4 B + 2 B + pairs; version 4: 4 B + 4 B + pairs - events follow.
    ///   event: one byte = kind (bits 2..7) | time code (bits 0..1: 0 = same tick, 1 = next tick, 2 = u8 ticks follow, 3 = u16 ticks
    ///     follow), then the kind's data:
    ///     0x04 end (no data); 0x08 animation state (u8); 0x10 control flags (u16); 0x14 weapon index (i16); 0x18 throttle (2 floats:
    ///     forward, left); 0x38 direction change (2 x i8: yaw, pitch); 0x58 direction change (2 x i16). Direction changes move facing,
    ///     aiming and looking together (yaw stays in -1000..1000 = -pi..pi in every stream, pitch in -500..500).
    /// </summary>
    public sealed class HaloRecording
    {
        public const int KindEnd = 0x04, KindAnimationState = 0x08, KindFlags = 0x10, KindWeapon = 0x14, KindThrottle = 0x18,
            KindTurnSmall = 0x38, KindTurnLarge = 0x58;

        public struct Event
        {
            /// <summary>The tick (from the start) the event takes effect on.</summary>
            public int Tick;
            public int Kind;
            /// <summary>AnimationState / Flags / Weapon: the value; turns: yaw change.</summary>
            public int A;
            /// <summary>Turns: pitch change.</summary>
            public int B;
            /// <summary>Throttle: forward, left.</summary>
            public float X, Y;
        }

        public string Name = "";
        public int Index, Version, RawAnimationData, ControlVersion;
        /// <summary>Length in ticks (30 a second), from the tag; the last event (end) lands on tick Length - 1.</summary>
        public int Length;
        public HaloRecordedControl Initial;
        public Event[] Events = new Event[0];
        /// <summary>Header bytes 6..7 (grenade / zoom for control version >= 1) and the unknown header words, kept for diagnosis.</summary>
        public int HeaderExtra;
        /// <summary>null when the stream parsed to its end event; else what went wrong (the events before it are kept).</summary>
        public string Error;
        public int StreamBytes;
        /// <summary>The tick of the end event (Length - 1 in every retail stream).</summary>
        public int EndTick;

        /// <summary>Every recorded animation of a scenario tag, in block order (scripts address them by index).</summary>
        public static List<HaloRecording> ReadAll(HaloTag scenario)
        {
            var list = new List<HaloRecording>();
            if (scenario == null) return list;
            int i = 0;
            foreach (var r in scenario.Root.Block(876, 64))
            {
                HaloRecording rec;
                try { rec = Parse(r.Str32(0), r.I8(32), r.I8(33), r.I8(34), r.I16(36), r.DataBytes(44)); }
                catch (Exception e) { rec = new HaloRecording { Name = r.Str32(0), Error = "read failed: " + e.Message }; }
                rec.Index = i++;
                list.Add(rec);
            }
            return list;
        }

        public static HaloRecording Parse(string name, int version, int raw, int controlVersion, int length, byte[] s)
        {
            var rec = new HaloRecording { Name = name ?? "", Version = version, RawAnimationData = raw, ControlVersion = controlVersion, Length = length };
            if (s == null || s.Length < 64) { rec.Error = "stream too short (" + (s == null ? 0 : s.Length) + " B)"; return rec; }
            rec.StreamBytes = s.Length;
            var c = new HaloRecordedControl
            {
                AnimationState = s[0], AimingSpeed = s[1], Flags = U16(s, 2), Weapon = I16(s, 4),
                Grenade = controlVersion >= 1 ? (sbyte)s[6] : -1,
                ThrottleForward = F32(s, 8), ThrottleLeft = F32(s, 12),
            };
            rec.HeaderExtra = U16(s, 6);
            int pairs = controlVersion >= 4 ? 60 : controlVersion >= 1 ? 58 : 52;
            if (s.Length < pairs + 12) { rec.Error = "header too short"; return rec; }
            c.FacingYaw = I16(s, pairs); c.FacingPitch = I16(s, pairs + 2);
            c.AimYaw = I16(s, pairs + 4); c.AimPitch = I16(s, pairs + 6);
            c.LookYaw = I16(s, pairs + 8); c.LookPitch = I16(s, pairs + 10);
            rec.Initial = c;

            var ev = new List<Event>(64);
            int p = pairs + 12, tick = 0;
            bool ended = false;
            while (p < s.Length)
            {
                int b = s[p++], tc = b & 3, kind = b & 0xFC;
                if (tc == 1) tick += 1;
                else if (tc == 2) { if (p >= s.Length) break; tick += s[p++]; }
                else if (tc == 3) { if (p + 1 >= s.Length) break; tick += U16(s, p); p += 2; }
                var e = new Event { Tick = tick, Kind = kind };
                int need = kind == KindEnd ? 0 : kind == KindAnimationState ? 1 : kind == KindFlags || kind == KindWeapon || kind == KindTurnSmall ? 2
                         : kind == KindTurnLarge ? 4 : kind == KindThrottle ? 8 : -1;
                if (need < 0) { rec.Error = "unknown event 0x" + kind.ToString("X2") + " at byte " + (p - 1); break; }
                if (p + need > s.Length) { rec.Error = "event 0x" + kind.ToString("X2") + " cut off at byte " + (p - 1); break; }
                switch (kind)
                {
                    case KindAnimationState: e.A = s[p]; break;
                    case KindFlags: e.A = U16(s, p); break;
                    case KindWeapon: e.A = I16(s, p); break;
                    case KindThrottle: e.X = F32(s, p); e.Y = F32(s, p + 4); break;
                    case KindTurnSmall: e.A = (sbyte)s[p]; e.B = (sbyte)s[p + 1]; break;
                    case KindTurnLarge: e.A = I16(s, p); e.B = I16(s, p + 2); break;
                }
                p += need;
                ev.Add(e);
                if (kind == KindEnd) { ended = true; rec.EndTick = tick; break; }
            }
            if (!ended && rec.Error == null) rec.Error = "no end event";
            rec.Events = ev.ToArray();
            return rec;
        }

        /// <summary>Applies one event to a control state (the order the stream gives).</summary>
        public static void Apply(ref HaloRecordedControl c, ref Event e)
        {
            switch (e.Kind)
            {
                case KindAnimationState: c.AnimationState = e.A; break;
                case KindFlags: c.Flags = e.A; break;
                case KindWeapon: c.Weapon = e.A; break;
                case KindThrottle: c.ThrottleForward = e.X; c.ThrottleLeft = e.Y; break;
                case KindTurnSmall:
                case KindTurnLarge:
                    c.FacingYaw += e.A; c.FacingPitch += e.B;
                    c.AimYaw += e.A; c.AimPitch += e.B;
                    c.LookYaw += e.A; c.LookPitch += e.B;
                    break;
            }
        }

        static int U16(byte[] b, int o) { return b[o] | b[o + 1] << 8; }
        static int I16(byte[] b, int o) { return (short)(b[o] | b[o + 1] << 8); }
        static float F32(byte[] b, int o) { return BitConverter.ToSingle(b, o); }

        public override string ToString() { return Name + " (" + Length + " ticks, " + Events.Length + " events" + (Error != null ? ", " + Error : "") + ")"; }
    }

    /// <summary>
    /// Plays a <see cref="HaloRecording"/>: <see cref="Step"/> once per game tick (30 a second) advances the control state through the
    /// events due; <see cref="Remaining"/> is what recording_time returns. Tick 0 = the header state plus the events at tick 0.
    /// No allocations.
    /// </summary>
    public sealed class HaloRecordingPlayback
    {
        public readonly HaloRecording Recording;
        public HaloRecordedControl Control;
        /// <summary>Ticks played (0 before the first Step).</summary>
        public int Tick { get; private set; }
        public bool Finished { get; private set; }
        int _next;

        public HaloRecordingPlayback(HaloRecording recording)
        {
            Recording = recording;
            Restart();
        }

        public void Restart()
        {
            Control = Recording.Initial;
            Tick = 0; _next = 0; Finished = false;
            Due(0);
        }

        /// <summary>Ticks left (recording_time).</summary>
        public int Remaining => Finished ? 0 : Math.Max(0, Recording.Length - Tick);

        /// <summary>One game tick. Returns false once the recording is over (its length played or its end event reached).</summary>
        public bool Step()
        {
            if (Finished) return false;
            Tick++;
            Due(Tick);
            if (Tick >= Recording.Length || (_next >= Recording.Events.Length && Tick > Recording.EndTick)) Finished = true;
            return !Finished;
        }

        void Due(int tick)
        {
            var ev = Recording.Events;
            while (_next < ev.Length && ev[_next].Tick <= tick)
            {
                if (ev[_next].Kind == HaloRecording.KindEnd) { _next++; continue; }
                HaloRecording.Apply(ref Control, ref ev[_next]);
                _next++;
            }
        }
    }
}

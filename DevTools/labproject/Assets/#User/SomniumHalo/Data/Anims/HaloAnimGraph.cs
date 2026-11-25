using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A model_animations tag (antr): the animation graph of a character, weapon or vehicle. Read from the map exactly as the game keeps it:
    /// - units (one "seat" per stance / vehicle seat: "stand", "crouch", "W-driver", "W-gunner", "W-passenger", "G-driver", "B-driver",
    ///   "scorpion-driver", "GT-gunner", ...): the seat's own animations (by <see cref="HaloAnimIds.Seat"/>), its aiming ranges, its IK points and
    ///   per weapon class ("rifle", "pistol", "missile", "plasmacannon", "flamethrower", "unarmed", "fixed") the class animations (by
    ///   <see cref="HaloAnimIds.Weapon"/>) and the weapon types under it (the weapon tag's label: "ar", "hp", "sg", ...) with their animations
    ///   (by <see cref="HaloAnimIds.WeaponType"/>);
    /// - nodes (the skeleton the animations are for: names and parents);
    /// - animations (<see cref="HaloAnimation"/>: frame data decoded on first use).
    /// Layout: DevTools/reference/layouts.txt (ModelAnimations*). Verified on Blood Gulch's characters\cyborg\cyborg: 19 nodes, 254 animations,
    /// 24 seats, none compressed. Unity-free.
    /// </summary>
    public sealed class HaloAnimGraph
    {
        public struct IkPoint { public string Marker, AttachTo; }

        public sealed class WeaponType
        {
            public string Label;
            public short[] Anims;
            public int Anim(int id) { return Anims != null && id >= 0 && id < Anims.Length ? Anims[id] : -1; }
        }

        public sealed class WeaponClass
        {
            public string Name, GripMarker, HandMarker;
            public float RightYawPerFrame, LeftYawPerFrame, DownPitchPerFrame, UpPitchPerFrame;
            public int RightFrames, LeftFrames, DownFrames, UpFrames;
            public short[] Anims;
            public readonly List<WeaponType> Types = new List<WeaponType>();
            public readonly List<IkPoint> Ik = new List<IkPoint>();
            public int Anim(int id) { return Anims != null && id >= 0 && id < Anims.Length ? Anims[id] : -1; }

            public WeaponType Type(string label)
            {
                if (label == null) return null;
                for (int i = 0; i < Types.Count; i++) if (string.Equals(Types[i].Label, label, StringComparison.OrdinalIgnoreCase)) return Types[i];
                return null;
            }
        }

        public sealed class Seat
        {
            public string Label;
            public float RightYawPerFrame, LeftYawPerFrame, DownPitchPerFrame, UpPitchPerFrame;
            public int RightFrames, LeftFrames, DownFrames, UpFrames;
            public short[] Anims;
            public readonly List<WeaponClass> Weapons = new List<WeaponClass>();
            public readonly List<IkPoint> Ik = new List<IkPoint>();
            public int Anim(int id) { return Anims != null && id >= 0 && id < Anims.Length ? Anims[id] : -1; }

            public WeaponClass Weapon(string name)
            {
                if (name == null) return null;
                for (int i = 0; i < Weapons.Count; i++) if (string.Equals(Weapons[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Weapons[i];
                return null;
            }
        }

        public readonly HaloTag Tag;
        public readonly string[] NodeNames;
        public readonly int[] NodeParents;
        public readonly HaloAnimation[] Animations;
        public readonly List<Seat> Seats = new List<Seat>();
        /// <summary>The unit damage table: 4 kinds (soft ping, hard ping, soft kill, hard kill) x 4 directions (front, left, right, back) x 11 body regions (0 gut, 1 chest, 2 head, ...) -> animation index (cyborg: "s-kill front gut", "h-kill left head" ...).</summary>
        public readonly short[] UnitDamage;
        public readonly int Compressed;
        private readonly Dictionary<string, int> _byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public HaloAnimGraph(HaloTag tag)
        {
            Tag = tag;
            TagReader r = tag.Root;
            var names = new List<string>();
            var parents = new List<int>();
            foreach (TagReader n in r.Block(104, 64)) { names.Add(n.Str32(0)); parents.Add(n.I16(36)); }
            NodeNames = names.ToArray();
            NodeParents = parents.ToArray();
            var anims = new List<HaloAnimation>();
            foreach (TagReader a in r.Block(116, 180)) anims.Add(new HaloAnimation(a, anims.Count));
            Animations = anims.ToArray();
            for (int i = 0; i < Animations.Length; i++)
            {
                if (Animations[i].Compressed) Compressed++;
                if (!_byName.ContainsKey(Animations[i].Name)) _byName[Animations[i].Name] = i;
            }

            UnitDamage = Shorts(r, 60);
            foreach (TagReader u in r.Block(12, 100))
            {
                var s = new Seat
                {
                    Label = u.Str32(0), RightYawPerFrame = u.F32(32), LeftYawPerFrame = u.F32(36), RightFrames = u.U16(40), LeftFrames = u.U16(42),
                    DownPitchPerFrame = u.F32(44), UpPitchPerFrame = u.F32(48), DownFrames = u.U16(52), UpFrames = u.U16(54), Anims = Shorts(u, 64),
                };
                foreach (TagReader k in u.Block(76, 64)) s.Ik.Add(new IkPoint { Marker = k.Str32(0), AttachTo = k.Str32(32) });
                foreach (TagReader w in u.Block(88, 188))
                {
                    var c = new WeaponClass
                    {
                        Name = w.Str32(0), GripMarker = w.Str32(32), HandMarker = w.Str32(64),
                        RightYawPerFrame = w.F32(96), LeftYawPerFrame = w.F32(100), RightFrames = w.U16(104), LeftFrames = w.U16(106),
                        DownPitchPerFrame = w.F32(108), UpPitchPerFrame = w.F32(112), DownFrames = w.U16(116), UpFrames = w.U16(118),
                        Anims = Shorts(w, 152),
                    };
                    foreach (TagReader k in w.Block(164, 64)) c.Ik.Add(new IkPoint { Marker = k.Str32(0), AttachTo = k.Str32(32) });
                    foreach (TagReader t in w.Block(176, 60)) c.Types.Add(new WeaponType { Label = t.Str32(0), Anims = Shorts(t, 48) });
                    s.Weapons.Add(c);
                }

                Seats.Add(s);
            }
        }

        private static short[] Shorts(TagReader r, int block)
        {
            int n = r.Count(block);
            if (n <= 0 || n > 4096) return new short[0];
            var a = new short[n];
            int i = 0;
            foreach (TagReader e in r.Block(block, 2)) a[i++] = e.I16(0);
            return a;
        }

        public HaloAnimation Get(int index) { return index >= 0 && index < Animations.Length ? Animations[index] : null; }

        public HaloAnimation Get(string name)
        {
            int i;
            return name != null && _byName.TryGetValue(name, out i) ? Animations[i] : null;
        }

        /// <summary>A death (kill = true) or flinch animation from the unit damage table (kind: hard = an explosion or a big hit; dir 0 front, 1 left, 2 right, 3 back; region 0 gut, 1 chest, 2 head).</summary>
        public HaloAnimation Damage(bool kill, bool hard, int dir, int region)
        {
            int kind = (kill ? 2 : 0) + (hard ? 1 : 0);
            int i = (kind * 4 + Math.Max(0, Math.Min(3, dir))) * 11 + Math.Max(0, Math.Min(10, region));
            HaloAnimation a = UnitDamage != null && i < UnitDamage.Length ? Get(UnitDamage[i]) : null;
            if (a == null && hard) return Damage(kill, false, dir, region);
            if (a == null && region != 0) return Damage(kill, hard, dir, 0);
            return a;
        }

        public Seat FindSeat(string label)
        {
            if (label == null) return null;
            for (int i = 0; i < Seats.Count; i++) if (string.Equals(Seats[i].Label, label, StringComparison.OrdinalIgnoreCase)) return Seats[i];
            return null;
        }

        /// <summary>
        /// The weapon class a weapon is held as, from its label (the weapon tag's "label": "ar", "hp", "sg", "rl", ...): the first class of the seat with a
        /// weapon type of that label that has an animation, else the first with that label at all, else "rifle" (the game's own fallback is the
        /// unit's first class; for the cyborg that is "rifle"). Needler: "ne" sits under "rifle" with no animations and under "pistol" with its reload, so pistol.
        /// </summary>
        public WeaponClass ClassFor(Seat seat, string weaponLabel, out WeaponType type)
        {
            type = null;
            if (seat == null) return null;
            WeaponClass first = null;
            WeaponType firstType = null;
            for (int i = 0; i < seat.Weapons.Count; i++)
            {
                WeaponType t = seat.Weapons[i].Type(weaponLabel);
                if (t == null) continue;
                bool any = false;
                for (int k = 0; k < t.Anims.Length; k++) if (t.Anims[k] >= 0) any = true;
                if (any) { type = t; return seat.Weapons[i]; }
                if (first == null) { first = seat.Weapons[i]; firstType = t; }
            }

            if (first != null) { type = firstType; return first; }
            WeaponClass rifle = seat.Weapon("rifle");
            return rifle ?? (seat.Weapons.Count > 0 ? seat.Weapons[0] : null);
        }
    }

    /// <summary>Indexes into the graph's animation lists (the game's enums; checked against the cyborg's names: "stand rifle idle" is class 0 etc.).</summary>
    public static class HaloAnimIds
    {
        /// <summary>A unit seat's own animations.</summary>
        public static class Seat
        {
            public const int AirborneDead = 0, LandingDead = 1, AccFrontBack = 2, AccLeftRight = 3, AccUpDown = 4, Push = 5, Twist = 6, Enter = 7, Exit = 8, Look = 9, Talk = 10, Emotions = 11;
        }

        /// <summary>A weapon class's animations.</summary>
        public static class Weapon
        {
            public const int Idle = 0, Gesture = 1, TurnLeft = 2, TurnRight = 3, DiveFront = 4, DiveBack = 5, DiveLeft = 6, DiveRight = 7,
                MoveFront = 8, MoveBack = 9, MoveLeft = 10, MoveRight = 11, SlideFront = 12, SlideBack = 13, SlideLeft = 14, SlideRight = 15,
                Airborne = 16, LandSoft = 17, LandHard = 18, ThrowGrenade = 20, Disarm = 21, Drop = 22, Ready = 23, PutAway = 24, AimStill = 25, AimMove = 26,
                StunnedFront = 35, StunnedBack = 36, StunnedLeft = 37, StunnedRight = 38, Melee = 39;
        }

        /// <summary>A weapon type's animations (under its class).</summary>
        public static class WeaponType
        {
            public const int Reload1 = 0, Reload2 = 1, Chamber1 = 2, Chamber2 = 3, Fire1 = 4, Fire2 = 5, Charged1 = 6, Charged2 = 7, Melee = 8, Overheat = 9;
        }
    }
}

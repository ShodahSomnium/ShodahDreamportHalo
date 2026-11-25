using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>Halo's AI states as squads / starting locations / scripts name them (ScenarioReturnState, "ai default state").</summary>
    public enum HaloAiState
    {
        None = 0, Sleeping = 1, Alert = 2, MovingRepeatSamePosition = 3, MovingLoop = 4, MovingLoopBackAndForth = 5, MovingLoopRandomly = 6,
        MovingRandomly = 7, Guarding = 8, GuardingAtGuardPosition = 9, Searching = 10, Fleeing = 11,
    }

    /// <summary>Command list atoms (ScenarioAtomType).</summary>
    public enum HaloAtom
    {
        Pause = 0, GoTo = 1, GoToAndFace = 2, MoveInDirection = 3, Look = 4, AnimationMode = 5, Crouch = 6, Shoot = 7, Grenade = 8, Vehicle = 9,
        RunningJump = 10, TargetedJump = 11, Script = 12, Animate = 13, Recording = 14, Action = 15, Vocalize = 16, Targeting = 17, Initiative = 18,
        Wait = 19, Loop = 20, Die = 21, MoveImmediate = 22, LookRandom = 23, LookPlayer = 24, LookObject = 25, SetRadius = 26, Teleport = 27,
    }

    /// <summary>
    /// The scenario's AI data (Unity-free, Halo space): the actor palette, encounters with their squads (starting locations, move positions,
    /// difficulty counts, attacking / defending / guarding firing position groups), platoons, firing positions, command lists (atoms and
    /// points), AI animation / script / recording references and AI conversations. Offsets: layouts.txt struct Scenario (+1056 ..) and
    /// ScenarioEncounter / ScenarioSquad / ScenarioPlatoon / ScenarioFiringPosition / ScenarioActorStartingLocation / ScenarioMovePosition /
    /// ScenarioCommandList / ScenarioCommand / ScenarioAIConversation*. Names: "encounter", "encounter/squad", "encounter/platoon" as scripts use them.
    /// </summary>
    public sealed class HaloAiScenario
    {
        // ScenarioEncounterFlags
        public const uint EncNotInitiallyCreated = 1, EncRespawnEnabled = 2, EncInitiallyBlind = 4, EncInitiallyDeaf = 8, EncInitiallyBraindead = 16,
            Enc3dFiringPositions = 32, EncManualBspIndex = 64;
        // ScenarioSquadFlags
        public const uint SqNeverSearch = 2, SqStartTimerImmediately = 4, SqNoTimerDelayForever = 8, SqMagicSightAfterTimer = 16, SqAutomaticMigration = 32;
        // ScenarioPlatoonFlags
        public const uint PlFleeWhenManeuvering = 1, PlSayAdvancingWhenManeuver = 2, PlStartInDefendingState = 4;

        public sealed class StartingLocation
        {
            public HVec3 Position; public float Facing; public int Cluster, SequenceId; public bool Required;
            public HaloAiState ReturnState, InitialState; public int ActorType = -1, CommandList = -1;
        }

        public sealed class MovePosition
        {
            public HVec3 Position; public float Facing, Weight; public HRange Time; public int Animation = -1, SequenceId, Cluster; public int Surface;
        }

        public sealed class Squad
        {
            public Encounter Encounter; public int Index;
            public string Name = "";
            public int ActorType = -1, Platoon = -1;
            public HaloAiState InitialState, ReturnState;
            public uint Flags;
            /// <summary>0 normal, 1 none, 2 random, 3 sgt johnson, 4 sgt lehto.</summary>
            public int UniqueLeaderType;
            public int ManeuverToSquad = -1;
            public float SquadDelayTime;
            /// <summary>Firing position group masks (bit 0 = group a ... bit 25 = group z).</summary>
            public uint Attacking, AttackingSearch, AttackingGuard, Defending, DefendingSearch, DefendingGuard, Pursuing;
            public int NormalCount, InsaneCount;
            /// <summary>0 normal, 1 few, 2 many, 3 none, 4 all.</summary>
            public int MajorUpgrade;
            public int RespawnMin, RespawnMax, RespawnTotal;
            public HRange RespawnDelay;
            public readonly List<MovePosition> MovePositions = new List<MovePosition>();
            public readonly List<StartingLocation> Starts = new List<StartingLocation>();
            public string Path => Encounter.Name + "/" + Name;
            public bool Has(uint f) => (Flags & f) != 0;
        }

        public sealed class Platoon
        {
            public int Index; public string Name = ""; public uint Flags;
            /// <summary>ScenarioChangeAttackingDefendingStateWhen: 0 never, 1 75% strength, 2 50% strength, 3 25% strength, 4 anybody dead,
            /// 5 25% dead, 6 50% dead, 7 75% dead, 8 all but one dead, 9 all dead.</summary>
            public int ChangeWhen, ChangeHappensTo = -1, ManeuverWhen, ManeuverHappensTo = -1;
        }

        public sealed class FiringPosition { public HVec3 Position; public int Group, Cluster; public int Surface; }

        public sealed class Encounter
        {
            public int Index; public string Name = ""; public uint Flags;
            /// <summary>ScenarioTeamIndex: 0 default by unit, 1 player, 2 human, 3 covenant, 4 flood, 5 sentinel.</summary>
            public int Team;
            /// <summary>0 normal, 1 never, 2 tenacious.</summary>
            public int SearchBehavior;
            public int ManualBsp = -1, PrecomputedBsp = -1;
            public HRange RespawnDelay;
            public readonly List<Squad> Squads = new List<Squad>();
            public readonly List<Platoon> Platoons = new List<Platoon>();
            public readonly List<FiringPosition> FiringPositions = new List<FiringPosition>();
            public bool Has(uint f) => (Flags & f) != 0;
            public int Bsp => Has(EncManualBspIndex) && ManualBsp >= 0 ? ManualBsp : PrecomputedBsp;
        }

        public sealed class Command
        {
            public HaloAtom Atom; public int Modifier; public float Param1, Param2;
            public int Point1 = -1, Point2 = -1, Animation = -1, Script = -1, Recording = -1, CommandIndex = -1, ObjectName = -1;
            public override string ToString() => $"{Atom}({Modifier}) p{Param1:0.##},{Param2:0.##} pt {Point1},{Point2} anim {Animation} script {Script} rec {Recording} cmd {CommandIndex} obj {ObjectName}";
        }

        public sealed class CommandList
        {
            public int Index; public string Name = ""; public uint Flags; public int ManualBsp = -1, PrecomputedBsp = -1;
            public readonly List<Command> Commands = new List<Command>();
            public readonly List<HVec3> Points = new List<HVec3>();
            public bool AllowInitiative => (Flags & 1) != 0;
            public bool AllowTargeting => (Flags & 2) != 0;
            public bool DisableLooking => (Flags & 4) != 0;
            public bool DisableCommunication => (Flags & 8) != 0;
            public bool DisableFallingDamage => (Flags & 16) != 0;
        }

        public sealed class AnimationReference { public string Name = ""; public HaloTag Graph; }

        public sealed class ConversationParticipant
        {
            public int Flags;
            /// <summary>0 friendly actor, 1 disembodied, 2 in player's vehicle, 3 not in a vehicle, 4 prefer sergeant, 5 any actor, 6 radio unit, 7 radio sergeant.</summary>
            public int SelectionType;
            public int ActorType = -1, UseThisObject = -1, SetNewName = -1;
            public readonly int[] VariantNumbers = new int[6];
            public string EncounterName = ""; public int EncounterIndex = -1;
            public bool Optional => (Flags & 1) != 0;
        }

        public sealed class ConversationLine
        {
            public int Flags, Participant = -1;
            /// <summary>0 none, 1 player, 2 participant.</summary>
            public int Addressee; public int AddresseeParticipant = -1;
            public float Delay;
            public readonly HaloTag[] Variants = new HaloTag[6];
        }

        public sealed class Conversation
        {
            public int Index; public string Name = ""; public int Flags; public float TriggerDistance, RunToPlayerDistance;
            public readonly List<ConversationParticipant> Participants = new List<ConversationParticipant>();
            public readonly List<ConversationLine> Lines = new List<ConversationLine>();
        }

        public readonly HaloScenario Scenario;
        public readonly List<HaloTag> ActorPalette = new List<HaloTag>();
        public readonly List<Encounter> Encounters = new List<Encounter>();
        public readonly List<CommandList> CommandLists = new List<CommandList>();
        public readonly List<AnimationReference> Animations = new List<AnimationReference>();
        public readonly List<string> ScriptReferences = new List<string>(), RecordingReferences = new List<string>();
        public readonly List<Conversation> Conversations = new List<Conversation>();
        readonly Dictionary<string, Encounter> _encByName = new Dictionary<string, Encounter>(StringComparer.OrdinalIgnoreCase);

        public HaloAiScenario(HaloScenario scenario)
        {
            Scenario = scenario;
            var r = scenario.Tag.Root;
            foreach (var p in r.Block(1056, 16)) ActorPalette.Add(p.Ref(0));

            int ei = 0;
            foreach (var e in r.Block(1068, 176))
            {
                var enc = new Encounter
                {
                    Index = ei++, Name = e.Str32(0), Flags = e.U32(32), Team = e.U16(36), SearchBehavior = e.U16(40), ManualBsp = e.I16(42),
                    RespawnDelay = new HRange(e.F32(44), e.F32(48)), PrecomputedBsp = e.I16(126),
                };
                int si = 0;
                foreach (var s in e.Block(128, 232))
                {
                    var sq = new Squad
                    {
                        Encounter = enc, Index = si++, Name = s.Str32(0), ActorType = s.I16(32), Platoon = s.I16(34),
                        InitialState = (HaloAiState)s.U16(36), ReturnState = (HaloAiState)s.U16(38), Flags = s.U32(40), UniqueLeaderType = s.U16(44),
                        ManeuverToSquad = s.I16(78), SquadDelayTime = s.F32(80),
                        Attacking = s.U32(84), AttackingSearch = s.U32(88), AttackingGuard = s.U32(92), Defending = s.U32(96),
                        DefendingSearch = s.U32(100), DefendingGuard = s.U32(104), Pursuing = s.U32(108),
                        NormalCount = s.I16(124), InsaneCount = s.I16(126), MajorUpgrade = s.U16(128),
                        RespawnMin = s.I16(132), RespawnMax = s.I16(134), RespawnTotal = s.I16(136), RespawnDelay = new HRange(s.F32(140), s.F32(144)),
                    };
                    foreach (var m in s.Block(196, 80))
                        sq.MovePositions.Add(new MovePosition
                        {
                            Position = m.Vec3(0), Facing = m.F32(12), Weight = m.F32(16), Time = new HRange(m.F32(20), m.F32(24)),
                            Animation = m.I16(28), SequenceId = m.I8(30), Cluster = m.I16(40), Surface = m.I32(76),
                        });
                    foreach (var l in s.Block(208, 28))
                        sq.Starts.Add(new StartingLocation
                        {
                            Position = l.Vec3(0), Facing = l.F32(12), Cluster = l.I16(16), SequenceId = l.I8(18), Required = (l.U8(19) & 1) != 0,
                            ReturnState = (HaloAiState)l.U16(20), InitialState = (HaloAiState)l.U16(22), ActorType = l.I16(24), CommandList = l.I16(26),
                        });
                    enc.Squads.Add(sq);
                }
                int pi = 0;
                foreach (var p in e.Block(140, 172))
                    enc.Platoons.Add(new Platoon { Index = pi++, Name = p.Str32(0), Flags = p.U32(32), ChangeWhen = p.U16(48), ChangeHappensTo = p.I16(50), ManeuverWhen = p.U16(60), ManeuverHappensTo = p.I16(62) });
                foreach (var f in e.Block(152, 24))
                    enc.FiringPositions.Add(new FiringPosition { Position = f.Vec3(0), Group = f.U16(12), Cluster = f.I16(14), Surface = f.I32(20) });
                Encounters.Add(enc);
                if (!_encByName.ContainsKey(enc.Name)) _encByName[enc.Name] = enc;
            }

            int ci = 0;
            foreach (var c in r.Block(1080, 96))
            {
                var cl = new CommandList { Index = ci++, Name = c.Str32(0), Flags = c.U32(32), ManualBsp = c.I16(44), PrecomputedBsp = c.I16(46) };
                foreach (var a in c.Block(48, 32))
                    cl.Commands.Add(new Command
                    {
                        Atom = (HaloAtom)a.U16(0), Modifier = a.I16(2), Param1 = a.F32(4), Param2 = a.F32(8), Point1 = a.I16(12), Point2 = a.I16(14),
                        Animation = a.I16(16), Script = a.I16(18), Recording = a.I16(20), CommandIndex = a.I16(22), ObjectName = a.I16(24),
                    });
                foreach (var p in c.Block(60, 20)) cl.Points.Add(p.Vec3(0));
                CommandLists.Add(cl);
            }

            foreach (var a in r.Block(1092, 60)) Animations.Add(new AnimationReference { Name = a.Str32(0), Graph = a.Ref(32) });
            foreach (var a in r.Block(1104, 40)) ScriptReferences.Add(a.Str32(0));
            foreach (var a in r.Block(1116, 40)) RecordingReferences.Add(a.Str32(0));

            int vi = 0;
            foreach (var c in r.Block(1128, 116))
            {
                var cv = new Conversation { Index = vi++, Name = c.Str32(0), Flags = c.U16(32), TriggerDistance = c.F32(36), RunToPlayerDistance = c.F32(40) };
                foreach (var p in c.Block(80, 84))
                {
                    var cp = new ConversationParticipant
                    {
                        Flags = p.U16(2), SelectionType = p.U16(4), ActorType = p.I16(6), UseThisObject = p.I16(8), SetNewName = p.I16(10),
                        EncounterName = p.Str32(36), EncounterIndex = p.I32(68),
                    };
                    for (int k = 0; k < 6; k++) cp.VariantNumbers[k] = p.U16(24 + 2 * k);
                    cv.Participants.Add(cp);
                }
                foreach (var l in c.Block(92, 124))
                {
                    var ln = new ConversationLine { Flags = l.U16(0), Participant = l.I16(2), Addressee = l.U16(4), AddresseeParticipant = l.I16(6), Delay = l.F32(12) };
                    for (int k = 0; k < 6; k++) ln.Variants[k] = l.Ref(28 + 16 * k);
                    cv.Lines.Add(ln);
                }
                Conversations.Add(cv);
            }
        }

        public Encounter FindEncounter(string name) { Encounter e; return name != null && _encByName.TryGetValue(name, out e) ? e : null; }

        public CommandList FindCommandList(string name)
        {
            if (name == null) return null;
            foreach (var c in CommandLists) if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        public Conversation FindConversation(string name)
        {
            if (name == null) return null;
            foreach (var c in Conversations) if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>
        /// Resolves a script "ai" name: "encounter" (squad = platoon = -1), "encounter/squad" or "encounter/platoon" (platoon >= 0).
        /// Returns false when the encounter is unknown or the part after the slash names neither a squad nor a platoon.
        /// </summary>
        public bool Resolve(string ai, out Encounter enc, out int squad, out int platoon)
        {
            enc = null; squad = -1; platoon = -1;
            if (string.IsNullOrEmpty(ai)) return false;
            int slash = ai.IndexOf('/');
            enc = FindEncounter(slash < 0 ? ai : ai.Substring(0, slash));
            if (enc == null) return false;
            if (slash < 0) return true;
            string part = ai.Substring(slash + 1);
            for (int i = 0; i < enc.Squads.Count; i++) if (string.Equals(enc.Squads[i].Name, part, StringComparison.OrdinalIgnoreCase)) { squad = i; return true; }
            for (int i = 0; i < enc.Platoons.Count; i++) if (string.Equals(enc.Platoons[i].Name, part, StringComparison.OrdinalIgnoreCase)) { platoon = i; return true; }
            return false;
        }

        public HaloTag PaletteVariant(int index) => index >= 0 && index < ActorPalette.Count ? ActorPalette[index] : null;

        /// <summary>
        /// How many actors a squad places at a difficulty (0 easy .. 3 legendary): "normal diff count" on easy and normal, "insane diff
        /// count" on heroic and legendary (Halo's squad fields; Sapien's tool tips: normal = easy/normal, insane = heroic/legendary).
        /// A count of 0 with starting locations places one actor per location that is flagged "required" (at least 1 when there are locations).
        /// </summary>
        public static int CountFor(Squad sq, int difficulty)
        {
            int n = difficulty >= 2 ? sq.InsaneCount : sq.NormalCount;
            if (n <= 0)
            {
                int req = 0; foreach (var l in sq.Starts) if (l.Required) req++;
                n = req;
            }
            return n;
        }

        public static string GroupLetters(uint mask)
        {
            if (mask == 0) return "-";
            var c = new char[26]; int k = 0;
            for (int i = 0; i < 26; i++) if ((mask & (1u << i)) != 0) c[k++] = (char)('a' + i);
            return new string(c, 0, k);
        }
    }
}

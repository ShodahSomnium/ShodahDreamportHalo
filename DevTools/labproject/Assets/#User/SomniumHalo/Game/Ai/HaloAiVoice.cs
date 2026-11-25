using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] Halo's AI vocalizations: the unit tag's "dialogue variants" (+692) name a dialogue tag (udlg) whose sound slots
    /// (layouts.txt struct Dialogue) the actors speak on what happens - "new combat" / "unexpected enemy" / "surprise" on a first sight,
    /// "grenade throwing", "grenade sighted", "dive", "flee" / "flee leader died", "berserk", "melee attack", "leap attack", "search start",
    /// "lost contact", "killed enemy", "friend died", "cover", "advance" / "retreat", "shooting", "taunt", "vehicle entry", "celebration".
    /// One dialogue variant per actor (picked at random among the unit's), at most one line per actor every 2.5 s and a few at once
    /// (ours: the rates). Played at the unit through HaloAudio; ai_dialogue_triggers false silences them.
    /// </summary>
    public static class HaloAiVoice
    {
        public const int NewCombatAlone = 1744, NewEnemyRecentCombat = 1760, OldEnemySighted = 1776, UnexpectedEnemy = 1792, GrenadeThrowing = 1856,
            GrenadeSighted = 1872, GrenadeDangerSelf = 1920, SearchStart = 2112, LostContact = 2592, Advance = 2240, Retreat = 2272, Cover = 2304,
            Shooting = 2400, ShootingBerserk = 2432, Taunt = 2480, Flee = 2512, FleeLeaderDied = 2544, VehicleEntry = 2624, Surprise = 2848,
            Berserk = 2864, MeleeAttack = 2880, Dive = 2896, LeapAttack = 2928, Celebration = 3024, KilledEnemy = 864, KilledEnemyPlayer = 896,
            FriendDied = 1552, FriendPlayerDied = 1568, IdleCombat = 32, IdleNoncombat = 16, PainBodyMajor = 128, ScreamFear = 176, Pleading = 2736;

        static readonly Dictionary<HaloTag, HaloTag[]> VariantsOf = new Dictionary<HaloTag, HaloTag[]>();
        static float _globalNext; static int _spoken;
        public static int Spoken => _spoken;

        public static void Clear() { VariantsOf.Clear(); _spoken = 0; _globalNext = 0f; }

        /// <summary>The dialogue tag an actor speaks with (one of its unit's dialogue variants), null when the unit has none.</summary>
        public static HaloTag DialogueFor(HaloUnit u)
        {
            if (u == null || u.UnitTag == null) return null;
            HaloTag[] list;
            if (!VariantsOf.TryGetValue(u.UnitTag, out list))
            {
                var l = new List<HaloTag>();
                try { foreach (var d in u.UnitTag.Root.Block(692, 24)) { var t = d.Ref(8); if (t != null) l.Add(t); } } catch (System.Exception) { }
                list = l.ToArray();
                VariantsOf[u.UnitTag] = list;
            }
            return list.Length == 0 ? null : list[HaloAi.Rng.Next(list.Length)];
        }

        /// <summary>The sound of one slot (byte offset in the dialogue tag), null when the slot is empty.</summary>
        public static HaloTag Sound(HaloTag dialogue, int slot)
        {
            if (dialogue == null) return null;
            try { return dialogue.Root.Ref(slot); } catch (System.Exception) { return null; }
        }

        /// <summary>The actor says a slot when it may (its own 2.5 s spacing, 0.4 s between any two lines nearby).</summary>
        public static bool Say(HaloActor a, int slot, float chance = 1f)
        {
            if (!HaloAi.DialogueTriggers || a == null || a.Unit == null || !a.Unit.Alive) return false;
            if (a.Dialogue == null) return false;
            float now = HaloAi.Time;
            if (now < a.NextVocal || now < _globalNext) return false;
            if (chance < 1f && !HaloAi.Chance(chance)) return false;
            var snd = Sound(a.Dialogue, slot);
            if (snd == null) return false;
            a.NextVocal = now + 2.5f;
            _globalNext = now + 0.4f;
            try { HaloAudio.Play(snd, a.Unit.transform); } catch (System.Exception) { return false; }
            HaloNetGame.NoteVoice(a.Unit, snd);      // (N, campaign co-op) the followers hear the line from the unit's puppet once
            if (_spoken++ == 0) HaloLog.Info("AI: first vocalization: " + a.Label + " says " + HaloAi.Short(snd));
            return true;
        }
    }

    public sealed partial class HaloActor
    {
        public HaloTag Dialogue;
        public float NextVocal;
    }
}

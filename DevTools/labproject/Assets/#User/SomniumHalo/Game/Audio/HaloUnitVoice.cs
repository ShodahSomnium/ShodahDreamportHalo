using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The unit-level dialogue Halo plays whatever the AI thinks: pain (body minor / major, shield), death (quiet, violent, instant,
    /// falling) and a friend's "damaged friend player" ("watch your fire!") when the player hurts an ally - from the unit's own dialogue
    /// tag (unit +692 dialogue variants -> udlg, struct Dialogue in layouts.txt). AI units through HaloBipeds.Damaged / Died; the local
    /// player (the globals' player unit, or the MP unit) through HaloLocal.DamagedAt and HaloEvents.LocalDied, played 2D.
    /// The AI's tactical lines (sighting, grenades, cover, kills...) stay with the AI module (HaloAiVoice).
    /// </summary>
    public static class HaloUnitVoice
    {
        public const int PainBodyMinor = 112, PainBodyMajor = 128, PainShield = 144, PainFalling = 160, DeathQuiet = 240, DeathViolent = 256,
            DeathFalling = 272, DeathInstant = 304, DamagedFriendPlayer = 368;

        public static bool Enabled = true;
        /// <summary>Seconds between two pain lines of one unit. // ours</summary>
        public static float PainSpacing = 1.2f;

        static readonly Dictionary<HaloUnit, HaloTag> DialogueOf = new Dictionary<HaloUnit, HaloTag>();
        static readonly Dictionary<HaloUnit, float> NextPain = new Dictionary<HaloUnit, float>();
        static bool _hooked;
        static HaloMap _playerMap;
        static HaloTag _playerDialogue;
        static float _lastDamagedAt = -10f, _nextOwnPain;
        public static int Pains, Deaths, FriendlyFire, OwnLines;

        internal static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            HaloBipeds.Damaged += OnDamaged;
            HaloBipeds.Died += OnDied;
            HaloEvents.LocalDied += OnLocalDied;
        }

        internal static void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            HaloBipeds.Damaged -= OnDamaged;
            HaloBipeds.Died -= OnDied;
            HaloEvents.LocalDied -= OnLocalDied;
            DialogueOf.Clear(); NextPain.Clear(); _playerMap = null;
        }

        static HaloTag Dialogue(HaloUnit u)
        {
            HaloTag d;
            if (DialogueOf.TryGetValue(u, out d)) return d;
            d = HaloAiVoice.DialogueFor(u);   // one of the unit's dialogue variants, kept for the unit's life
            if (DialogueOf.Count > 512) DialogueOf.Clear();
            DialogueOf[u] = d;
            return d;
        }

        static HaloTag Slot(HaloTag dialogue, int slot) => HaloAiVoice.Sound(dialogue, slot);

        static void OnDamaged(HaloUnit u, HaloHit hit)
        {
            if (!Enabled || u == null || !u.Alive) return;
            var d = Dialogue(u);
            if (d == null) return;
            float now = Time.time, next;
            if (NextPain.TryGetValue(u, out next) && now < next) return;
            HaloTag snd = null;
            if (hit.SourcePlayer >= 0 && HaloAllegiance.AreFriends(u.Team, HaloTeam.Player)) { snd = Slot(d, DamagedFriendPlayer); if (snd != null) FriendlyFire++; }
            if (snd == null)
            {
                bool shield = u.MaxShield > 0f && u.Shield > 0.01f;
                bool major = hit.Explosion || hit.Melee || (u.MaxBody > 0f && hit.Amount / u.MaxBody >= 0.3f);
                snd = shield ? Slot(d, PainShield) : null;
                if (snd == null) snd = Slot(d, major ? PainBodyMajor : PainBodyMinor) ?? Slot(d, PainBodyMinor);
            }
            if (snd == null) return;
            NextPain[u] = now + PainSpacing;
            Pains++;
            HaloAudio.Play(snd, u.transform);
        }

        static void OnDied(HaloUnit u, HaloHit hit)
        {
            if (!Enabled || u == null) return;
            var d = Dialogue(u);
            NextPain.Remove(u);
            if (d == null) return;
            int slot = hit.Headshot ? DeathInstant : (hit.Explosion || hit.Instant) ? DeathViolent : (hit.Cause == null && hit.SourcePlayer < 0 && hit.Effect == null) ? DeathFalling : DeathQuiet;
            var snd = Slot(d, slot) ?? Slot(d, DeathQuiet) ?? Slot(d, DeathViolent);
            DialogueOf.Remove(u);
            if (snd == null) return;
            Deaths++;
            HaloAudio.Play(snd, u.transform);
        }

        /// <summary>The local player's dialogue tag: the globals' player unit (campaign: the cyborg) or the MP unit.</summary>
        static HaloTag PlayerDialogue()
        {
            var map = HaloSession.Map;
            if (map == null) return null;
            if (map == _playerMap) return _playerDialogue;
            _playerMap = map; _playerDialogue = null;
            try
            {
                HaloTag g = null;
                foreach (var t in map.OfClass("matg")) { g = t; break; }
                HaloTag unit = null;
                if (g != null && g.Root.Count(368) > 0) unit = g.Root.Element(368, 0, 244).Ref(0);
                if (unit == null && g != null && g.Root.Count(356) > 0) unit = g.Root.Element(356, 0, 160).Ref(16);
                if (unit != null) foreach (var dv in unit.Root.Block(692, 24)) { var t = dv.Ref(8); if (t != null) { _playerDialogue = t; break; } }
                HaloLog.Info("Audio: player dialogue " + (_playerDialogue != null ? _playerDialogue.Name : "none") + " (unit " + (unit != null ? unit.Name : "none") + ")");
            }
            catch (Exception) { }
            return _playerDialogue;
        }

        static void OnLocalDied(HaloKill k)
        {
            if (!Enabled) return;
            var d = PlayerDialogue();
            if (d == null) return;
            int slot = k.Headshot ? DeathInstant : k.Grenade ? DeathViolent : (k.Cause == null && k.Killer < 0) ? DeathFalling : DeathQuiet;
            var snd = Slot(d, slot) ?? Slot(d, DeathQuiet);
            if (snd != null) { OwnLines++; HaloAudio.PlayFlat(snd, 0.8f, 1f, 1f); }
        }

        /// <summary>Once a frame: the local player's pain (2D, quieter - it is in the own head). // ours: the gain</summary>
        internal static void Tick()
        {
            if (!Enabled || !HaloSession.InMatch) return;
            float at = HaloLocal.DamagedAt;
            if (at <= _lastDamagedAt) return;
            _lastDamagedAt = at;
            if (!HaloLocal.Alive || Time.time < _nextOwnPain || Time.time - at > 0.5f) return;
            var d = PlayerDialogue();
            if (d == null) return;
            var snd = HaloLocal.Shield > 0.01f ? Slot(d, PainShield) : Slot(d, HaloLocal.Health < 0.5f ? PainBodyMajor : PainBodyMinor);
            if (snd == null) return;
            _nextOwnPain = Time.time + PainSpacing * 1.5f;
            OwnLines++;
            HaloAudio.PlayFlat(snd, 0.7f, 1f, 1f);
        }

        public static string Stats() => Pains + " pain, " + Deaths + " death, " + FriendlyFire + " friendly-fire lines, " + OwnLines + " own";
    }
}

using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Campaign co-op: everything the HOST's scripts and AI make audible, for followers (who run neither). One-shots go through the mission
    /// block's sound ring (8 entries of 3 ints a write; a late joiner does not replay them): script impulses (dialogue, SFX: sound_impulse_start /
    /// stop), script effects (effect_new at a cutscene flag, effect_new_on_object_marker), AI conversation lines (HaloAi.LineSpoken), sound
    /// class gains. Loops are STATE (one per actor block header, 9 at once: music, ambience - sound_looping_start / stop / set_alternate /
    /// set_scale): a late joiner starts the ones playing now. A sound "on an object" plays from that object on the follower: a unit's puppet,
    /// a vehicle, a named scenario object. A promoted host keeps the loops it played as a follower and stops them when its script stops them.
    ///
    /// Entry: a = kind (0-3) | tag index or class (4-19) | scale x64 / yaw (20-27) | target kind (28-30); b = target value (0-15) | aux (16-31);
    /// c = marker hash / position. Kinds: 1 impulse, 2 impulse stop, 3 effect, 4 conversation line, 5 class gain (b = gain x1000, c = seconds x1000).
    /// Targets: 0 none (2D), 1 unit serial, 2 vehicle id, 3 scenario object name index, 4 position (b, c = cm2), 5 object + marker (c = marker hash).
    /// Loop: a = tag (0-15) | scale x255 (16-23) | alternate (24) | target kind (25-27) | on (28); b = target value.
    /// </summary>
    public static partial class HaloNetGame
    {
        public const int SndImpulse = 1, SndStop = 2, SndEffect = 3, SndLine = 4, SndClassGain = 5;
        public const int TNone = 0, TUnit = 1, TVehicle = 2, TObject = 3, TPosition = 4;

        /// <summary>A ring entry's first int (b and c are the target value / aux and the marker hash or position).</summary>
        public static int PackSound(int kind, int tag, int byte8, int targetKind) { return (kind & 0xF) | ((tag & 0xFFFF) << 4) | ((byte8 & 0xFF) << 20) | ((targetKind & 7) << 28); }

        /// <summary>A loop table entry's first int (b = the target value).</summary>
        public static int PackLoop(int tag, float scale, bool alternate, int targetKind) { return (tag & 0xFFFF) | (HaloNetWire.Unit8(scale, 1f) << 16) | (alternate ? 1 << 24 : 0) | ((targetKind & 7) << 25) | (1 << 28); }

        /// <summary>Loops this client plays for the host (as a follower, or kept since it was promoted).</summary>
        public static int NetLoopsPlaying { get { int n = 0; for (int i = 0; i < NetLoop.Length; i++) if (NetLoop[i] != null) n++; return n; } }

        /// <summary>Lab / log counters: one-shots sent by the host, played by a follower (impulses + lines, effects, stops, class gains), loops started on a follower.</summary>
        public static int StatSoundsSent, StatImpulsesPlayed, StatEffectsPlayed, StatStopsPlayed, StatGainsPlayed, StatLoopsStarted;

        private static readonly HaloTag[] NetLoopTag = new HaloTag[HaloNetCoop.Loops];
        private static readonly HaloAudio.Handle[] NetLoop = new HaloAudio.Handle[HaloNetCoop.Loops];
        private static readonly int[] NetLoopA = new int[HaloNetCoop.Loops], NetLoopB = new int[HaloNetCoop.Loops];
        private static bool _soundSubscribed;

        private static bool Hosting { get { return Up && Net.Coop.Writing; } }

        // ------------------------------------------------------------------ host: the glue calls (Script / AI modules)

        /// <summary>sound_impulse_start: a one-shot on an object (null = 2D) at a scale.</summary>
        public static void NoteSound(HaloTag sound, GameObject on, float scale)
        {
            if (!Hosting || sound == null) return;
            int kind, value;
            TargetOf(on, out kind, out value);
            SendSound(SndImpulse, HaloNetU.TagIndex(sound), Scale64(scale), kind, value, 0, 0);
        }

        /// <summary>sound_impulse_stop.</summary>
        public static void NoteSoundStop(HaloTag sound)
        {
            if (!Hosting || sound == null) return;
            SendSound(SndStop, HaloNetU.TagIndex(sound), 0, TNone, 0, 0, 0);
        }

        /// <summary>effect_new at a world position (a cutscene flag) with a yaw.</summary>
        public static void NoteEffectAt(HaloTag effect, Vector3 position, float yawDegrees)
        {
            if (!Hosting || effect == null) return;
            int lo, hi;
            HaloNetWire.PackCm2(HaloNetU.V(position), out lo, out hi);
            int yaw = (int)(Mathf.Repeat(yawDegrees, 360f) / 360f * 256f) & 0xFF;
            Net.Coop.AddSound((SndEffect & 0xF) | ((HaloNetU.TagIndex(effect) & 0xFFFF) << 4) | (yaw << 20) | (TPosition << 28), lo, hi);
            StatSoundsSent++;
        }

        /// <summary>effect_new_on_object_marker.</summary>
        public static void NoteEffectOn(HaloTag effect, GameObject on, string marker)
        {
            if (!Hosting || effect == null || on == null) return;
            int kind, value;
            TargetOf(on, out kind, out value);
            SendSound(SndEffect, HaloNetU.TagIndex(effect), 0, kind, value, 0, string.IsNullOrEmpty(marker) ? 0 : HaloNetWire.NameHash(marker.ToLowerInvariant()));
        }

        /// <summary>sound_class_set_gain.</summary>
        public static void NoteClassGain(string soundClass, float gain, float seconds)
        {
            if (!Hosting) return;
            int cls = HaloAudioSystem.ClassIndex(soundClass);
            if (cls < 0) return;
            Net.Coop.AddSound((SndClassGain & 0xF) | (cls << 4), (int)Mathf.Round(Mathf.Clamp(gain, 0f, 60f) * 1000f), (int)Mathf.Round(Mathf.Max(0f, seconds) * 1000f));
            StatSoundsSent++;
        }

        /// <summary>sound_looping_start (a slot is taken while it plays; the same sound again replaces it).</summary>
        public static void NoteLoopStart(HaloTag sound, GameObject on, float scale)
        {
            if (!Hosting || sound == null) return;
            int tag = HaloNetU.TagIndex(sound);
            int slot = LoopSlot(tag);
            if (slot < 0) for (int i = 0; i < HaloNetCoop.Loops && slot < 0; i++) if ((Net.Coop.LoopA(i) & (1 << 28)) == 0) slot = i;
            if (slot < 0) { HaloLog.Warn("Net: more than " + HaloNetCoop.Loops + " script loops at once - " + sound.Name + " is not shared"); return; }
            int kind, value;
            TargetOf(on, out kind, out value);
            Net.Coop.SetLoop(slot, PackLoop(tag, scale, false, kind), value);
        }

        public static void NoteLoopStop(HaloTag sound)
        {
            if (!Hosting || sound == null) return;
            int slot = LoopSlot(HaloNetU.TagIndex(sound));
            if (slot >= 0) Net.Coop.SetLoop(slot, 0, 0);
            StopNetLoopOf(sound);      // (a loop this client started as a follower before it became the host)
        }

        public static void NoteLoopAlternate(HaloTag sound, bool on)
        {
            if (!Hosting || sound == null) return;
            int slot = LoopSlot(HaloNetU.TagIndex(sound));
            if (slot < 0) return;
            int a = Net.Coop.LoopA(slot);
            Net.Coop.SetLoop(slot, on ? a | (1 << 24) : a & ~(1 << 24), Net.Coop.LoopB(slot));
            for (int i = 0; i < NetLoop.Length; i++) if (NetLoopTag[i] == sound && NetLoop[i] != null) HaloAudio.SetAlternate(NetLoop[i], on);
        }

        public static void NoteLoopScale(HaloTag sound, float scale)
        {
            if (!Hosting || sound == null) return;
            int slot = LoopSlot(HaloNetU.TagIndex(sound));
            if (slot < 0) return;
            int a = Net.Coop.LoopA(slot);
            Net.Coop.SetLoop(slot, (a & ~(0xFF << 16)) | (HaloNetWire.Unit8(scale, 1f) << 16), Net.Coop.LoopB(slot));
            for (int i = 0; i < NetLoop.Length; i++) if (NetLoopTag[i] == sound && NetLoop[i] != null) HaloAudio.SetScale(NetLoop[i], scale);
        }

        private static int LoopSlot(int tag)
        {
            for (int i = 0; i < HaloNetCoop.Loops; i++)
            {
                int a = Net.Coop.LoopA(i);
                if ((a & (1 << 28)) != 0 && (a & 0xFFFF) == (tag & 0xFFFF)) return i;
            }

            return -1;
        }

        private static int Scale64(float s) { return Mathf.Clamp(Mathf.RoundToInt(s * 64f), 0, 255); }

        private static void SendSound(int kind, int tag, int scale64, int targetKind, int value, int aux, int c)
        {
            Net.Coop.AddSound(PackSound(kind, tag, scale64, targetKind), (value & 0xFFFF) | ((aux & 0xFFFF) << 16), c);
            StatSoundsSent++;
        }

        /// <summary>What a follower can find the object by: a unit's serial, a vehicle's id, a scenario object's name index (else 2D).</summary>
        private static void TargetOf(GameObject g, out int kind, out int value)
        {
            kind = TNone; value = 0;
            if (g == null) return;
            HaloUnit u = g.GetComponent<HaloUnit>();
            int serial = u != null ? SerialOf(u) : 0;
            if (serial > 0) { kind = TUnit; value = serial; return; }
            HaloVehicle v = g.GetComponent<HaloVehicle>();
            if (v != null && v.Id >= 0 && v.Id < 0xFFFF) { kind = TVehicle; value = v.Id; return; }
            HaloScenario sc = HaloSession.Scenario;
            if (sc == null) return;
            for (int i = 0; i < sc.ObjectNames.Count; i++)
                if (HaloObjects.Find(sc.ObjectNames[i]) == g) { kind = TObject; value = i; return; }
        }

        /// <summary>Host: an AI conversation line (HaloAi.LineSpoken) - from its speaker, or 2D.</summary>
        private static void OnLineSpoken(HaloUnit speaker, HaloTag sound, string conversation)
        {
            if (!Hosting || sound == null) return;
            int kind, value;
            TargetOf(speaker != null ? speaker.gameObject : null, out kind, out value);
            SendSound(SndLine, HaloNetU.TagIndex(sound), 64, kind, value, 0, 0);
        }

        private static void SubscribeSound()
        {
            if (_soundSubscribed) return;
            _soundSubscribed = true;
            HaloAi.LineSpoken += OnLineSpoken;
        }

        // ------------------------------------------------------------------ a follower

        private static Transform TargetTransform(int kind, int value)
        {
            switch (kind)
            {
                case TUnit:
                    for (int i = 0; i < Puppets.Length; i++) if (Puppets[i] != null && Puppets[i].Serial == value) return Puppets[i].transform;
                    HaloUnit u = UnitOfSerial(value);
                    return u != null ? u.transform : null;
                case TVehicle:
                    HaloVehicle v = HaloVehicles.Find(value);
                    return v != null ? v.transform : null;
                case TObject:
                    HaloScenario sc = HaloSession.Scenario;
                    GameObject g = sc != null && value >= 0 && value < sc.ObjectNames.Count ? HaloObjects.Find(sc.ObjectNames[value]) : null;
                    return g != null ? g.transform : null;
            }

            return null;
        }

        /// <summary>The follower's one-shots of this frame (from ClientStep) and its loops (state).</summary>
        private static void FollowSounds()
        {
            SubscribeSound();
            var list = Net.Coop.NewSounds;
            for (int i = 0; i + 2 < list.Count; i += 3)
            {
                try { PlayOne(list[i], list[i + 1], list[i + 2]); }
                catch (Exception e) { HaloLog.Warn("Net: a host sound failed: " + e.Message); }
            }

            list.Clear();
            for (int s = 0; s < HaloNetCoop.Loops; s++)
            {
                int a = Net.Coop.LoopA(s), b = Net.Coop.LoopB(s);
                if (a == NetLoopA[s] && b == NetLoopB[s] && (NetLoop[s] != null || (a & (1 << 28)) == 0)) continue;
                try { FollowLoop(s, a, b); }
                catch (Exception e) { HaloLog.Warn("Net: a host loop failed: " + e.Message); NetLoopA[s] = a; NetLoopB[s] = b; }
            }
        }

        private static void PlayOne(int a, int b, int c)
        {
            int kind = a & 0xF, tagIdx = (a >> 4) & 0xFFFF, byte8 = (a >> 20) & 0xFF, tkind = (a >> 28) & 7;
            if (kind == SndClassGain)
            {
                if (tagIdx < HaloAudioSystem.ClassNames.Length) { HaloAudio.SetClassGain(HaloAudioSystem.ClassNames[tagIdx], b / 1000f, c / 1000f); StatGainsPlayed++; }
                return;
            }

            HaloTag tag = HaloNetU.TagOf(tagIdx);
            if (tag == null) return;
            if (kind == SndStop) { HaloAudio.StopSound(tag); StatStopsPlayed++; return; }
            if (kind == SndEffect)
            {
                if (tkind == TPosition)
                {
                    Vector3 p = HaloNetU.V(HaloNetWire.UnpackCm2(b, c));
                    HaloFx.Play(tag, p, Quaternion.Euler(0f, byte8 * (360f / 256f), 0f));
                }
                else
                {
                    Transform t = TargetTransform(tkind, b & 0xFFFF);
                    if (t == null) return;
                    Transform m = MarkerByHash(t, c) ?? t;
                    HaloFx.Play(tag, m.position, m.rotation, m);
                }

                StatEffectsPlayed++;
                if (StatEffectsPlayed == 1) HaloLog.Info("Net: first co-op script effect from the host: " + tag.Name);
                return;
            }

            // an impulse or a conversation line: from its object, else 2D
            float scale = byte8 / 64f;
            Transform at = tkind != TNone ? TargetTransform(tkind, b & 0xFFFF) : null;
            if (at != null) HaloAudio.Play(tag, at, scale);
            else HaloAudio.Play2D(tag, scale);
            StatImpulsesPlayed++;
            if (StatImpulsesPlayed <= 3) HaloLog.Info("Net: co-op " + (kind == SndLine ? "conversation line" : "script sound") + " from the host: " + tag.Name + (at != null ? " on " + at.name : " (2D)"));
        }

        private static readonly System.Collections.Generic.List<Transform> MarkerBuf = new System.Collections.Generic.List<Transform>(64);

        private static Transform MarkerByHash(Transform root, int hash)
        {
            if (hash == 0) return null;
            MarkerBuf.Clear();
            root.GetComponentsInChildren(true, MarkerBuf);
            for (int i = 0; i < MarkerBuf.Count; i++)
            {
                string n = MarkerBuf[i].name;
                if (!n.StartsWith("marker:", StringComparison.Ordinal)) continue;
                string key = n.Substring(7);
                int cut = key.IndexOf('#');
                if (cut >= 0) key = key.Substring(0, cut);
                if (HaloNetWire.NameHash(key.ToLowerInvariant()) == hash) return MarkerBuf[i];
            }

            return null;
        }

        private static void FollowLoop(int s, int a, int b)
        {
            bool on = (a & (1 << 28)) != 0;
            int oldA = NetLoopA[s];
            bool sameSound = (oldA & 0xFFFF) == (a & 0xFFFF) && ((oldA >> 25) & 7) == ((a >> 25) & 7) && NetLoopB[s] == b && NetLoop[s] != null;
            NetLoopA[s] = a; NetLoopB[s] = b;
            if (!on)
            {
                if (NetLoop[s] != null) { HaloAudio.Stop(NetLoop[s]); NetLoop[s] = null; NetLoopTag[s] = null; }
                return;
            }

            float scale = ((a >> 16) & 0xFF) / 255f;
            bool alt = (a & (1 << 24)) != 0;
            if (!sameSound)
            {
                if (NetLoop[s] != null) HaloAudio.Stop(NetLoop[s]);
                NetLoop[s] = null; NetLoopTag[s] = null;
                HaloTag tag = HaloNetU.TagOf(a & 0xFFFF);
                if (tag == null) return;
                int tk = (a >> 25) & 7;
                Transform at = tk != TNone ? TargetTransform(tk, b) : null;
                if (tk != TNone && at == null) { NetLoopA[s] = 0; return; }      // (its object is not here yet: tried again next frame)
                NetLoop[s] = at != null ? HaloAudio.Loop(tag, at, 1f) : HaloAudio.Loop2D(tag, 1f);
                NetLoopTag[s] = tag;
                StatLoopsStarted++;
                HaloLog.Info("Net: co-op loop from the host: " + tag.Name + (at != null ? " on " + at.name : " (2D)"));
            }

            if (NetLoop[s] != null) { HaloAudio.SetScale(NetLoop[s], scale); HaloAudio.SetAlternate(NetLoop[s], alt); }
        }

        /// <summary>The new host's script stopped a loop this client had started as a follower.</summary>
        private static void StopNetLoopOf(HaloTag sound)
        {
            for (int i = 0; i < NetLoop.Length; i++)
            {
                if (NetLoopTag[i] != sound) continue;
                if (NetLoop[i] != null) HaloAudio.Stop(NetLoop[i]);
                NetLoop[i] = null; NetLoopTag[i] = null; NetLoopA[i] = 0; NetLoopB[i] = 0;
            }
        }

        private static void ClearSounds()
        {
            for (int i = 0; i < NetLoop.Length; i++)
            {
                if (NetLoop[i] != null) { try { HaloAudio.StopNow(NetLoop[i], 0.5f); } catch (Exception) { } }
                NetLoop[i] = null; NetLoopTag[i] = null; NetLoopA[i] = 0; NetLoopB[i] = 0;
            }
        }
    }
}

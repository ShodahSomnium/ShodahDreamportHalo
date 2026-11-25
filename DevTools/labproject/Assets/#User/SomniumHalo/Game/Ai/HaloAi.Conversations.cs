using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] AI conversations (scenario +1128): ai_conversation starts one - its participants are found (a named object, an actor
    /// of the named encounter of the participant's actor type, or a friendly actor near the player; "disembodied" ones speak without a
    /// body), then its lines play in order on their speakers (the line's sound, the line delay before it; "wait until told to advance" waits
    /// for ai_conversation_advance). Status as ai_conversation_status reports it: 0 none, 1 trying to begin, 2 waiting for the speakers,
    /// 3 playing, 4 waiting to advance, 5 could not begin, 6 finished, 7 finished unsuccessfully.
    /// </summary>
    public static partial class HaloAi
    {
        public sealed class ConversationRun
        {
            public HaloAiScenario.Conversation Data;
            public int Status, Line = -1;
            public float NextAt, LineEnds;
            public HaloUnit[] Speakers;
            public bool[] Disembodied;
            public bool AdvanceAllowed;
        }

        static readonly Dictionary<int, ConversationRun> Conversations = new Dictionary<int, ConversationRun>();
        static readonly List<ConversationRun> ConversationTick = new List<ConversationRun>(8);
        /// <summary>A line started: speaker (null = disembodied / radio), sound, conversation name (for subtitles).</summary>
        public static event System.Action<HaloUnit, HaloTag, string> LineSpoken;

        static void ClearConversations() { Conversations.Clear(); ConversationTick.Clear(); }

        /// <summary>ai_conversation: queues a conversation; false when its required speakers cannot be found.</summary>
        public static bool Conversation(string name)
        {
            if (!Ensure()) return false;
            var c = Data.FindConversation(name);
            if (c == null) { Unimplemented("unknown conversation '" + name + "'"); return false; }
            ConversationRun run;
            if (Conversations.TryGetValue(c.Index, out run) && (run.Status >= 1 && run.Status <= 4)) return true;
            run = new ConversationRun { Data = c, Status = 1, Speakers = new HaloUnit[c.Participants.Count], Disembodied = new bool[c.Participants.Count] };
            for (int i = 0; i < c.Participants.Count; i++)
            {
                var p = c.Participants[i];
                var u = FindSpeaker(p);
                run.Speakers[i] = u;
                run.Disembodied[i] = p.SelectionType == 1 || p.SelectionType == 6 || p.SelectionType == 7;
                if (u == null && !run.Disembodied[i] && !p.Optional && (c.Flags & KeepTrying) == 0)
                {
                    run.Status = 5;
                    Conversations[c.Index] = run;
                    if (Verbose) HaloLog.Info("AI: conversation " + name + " could not begin (participant " + i + ")");
                    return false;
                }
                if (u != null && p.SetNewName >= 0) { string nn = ObjectName(p.SetNewName); if (!string.IsNullOrEmpty(nn)) u.SetName(nn); }
            }
            Conversations[c.Index] = run;
            if (!ConversationTick.Contains(run)) ConversationTick.Add(run);
            if (_convs++ == 0) HaloLog.Info("AI: first conversation: " + name + " (" + c.Participants.Count + " participants, " + c.Lines.Count + " lines)");
            return true;
        }

        static int _convs;

        static HaloUnit FindSpeaker(HaloAiScenario.ConversationParticipant p)
        {
            if (p.UseThisObject >= 0)
            {
                var go = HaloObjects.Find(ObjectName(p.UseThisObject));
                var u = go != null ? go.GetComponent<HaloUnit>() : null;
                if (u != null && u.Alive) return u;
            }
            if (!string.IsNullOrEmpty(p.EncounterName))
            {
                HaloAiEncounter enc; int s, pl;
                if (Resolve(p.EncounterName, out enc, out s, out pl))
                    for (int i = 0; i < enc.Actors.Count; i++)
                    {
                        var a = enc.Actors[i];
                        if (!a.Alive || a.Unit == null || !HaloAiEncounter.Matches(a, s, pl)) continue;
                        if (p.ActorType >= 0 && a.A != null && (int)a.A.Type != p.ActorType) continue;
                        if (Speaking(a.Unit)) continue;
                        return a.Unit;
                    }
            }
            if (p.SelectionType == 0 || p.SelectionType == 4 || p.SelectionType == 5 || p.SelectionType == 3)
            {
                // a friendly actor near the player
                var me = HaloAiBodies.LocalPlayer;
                HaloUnit best = null; float bd = float.MaxValue;
                for (int i = 0; i < Actors.Count; i++)
                {
                    var a = Actors[i];
                    if (!a.Alive || a.Unit == null || Speaking(a.Unit)) continue;
                    if (p.SelectionType != 5 && !HaloAllegiance.AreFriends(a.Team, HaloTeam.Player)) continue;
                    if (p.ActorType >= 0 && a.A != null && (int)a.A.Type != p.ActorType) continue;
                    float d = me != null ? (a.Position - me.Feet).sqrMagnitude : 0f;
                    if (d < bd) { bd = d; best = a.Unit; }
                }
                return best;
            }
            return null;
        }

        static bool Speaking(HaloUnit u)
        {
            for (int i = 0; i < ConversationTick.Count; i++)
            {
                var r = ConversationTick[i];
                if (r.Status < 1 || r.Status > 4) continue;
                for (int k = 0; k < r.Speakers.Length; k++) if (r.Speakers[k] == u) return true;
            }
            return false;
        }

        public static void ConversationStop(string name)
        {
            var r = Run(name);
            if (r != null && r.Status >= 1 && r.Status <= 4) r.Status = 7;
        }

        public static void ConversationAdvance(string name) { var r = Run(name); if (r != null) r.AdvanceAllowed = true; }
        /// <summary>ai_conversation_line: the line playing, 999 when not playing.</summary>
        public static int ConversationLine(string name) { var r = Run(name); return r != null && (r.Status == 3 || r.Status == 4) ? Mathf.Max(0, r.Line) : 999; }
        public static int ConversationStatus(string name) { var r = Run(name); return r != null ? r.Status : 0; }

        static ConversationRun Run(string name)
        {
            if (!Ensure()) return null;
            var c = Data.FindConversation(name);
            ConversationRun r;
            return c != null && Conversations.TryGetValue(c.Index, out r) ? r : null;
        }

        const int KeepTrying = 64;
        const int StopIfDeath = 1, StopIfDamaged = 2, StopIfVisibleEnemy = 4, StopIfAlerted = 8, PlayerMustBeVisible = 16;
        const int LineWaitUntilAdvance = 8;

        static void TickConversations()
        {
            for (int i = ConversationTick.Count - 1; i >= 0; i--)
            {
                var r = ConversationTick[i];
                if (r.Status < 1 || r.Status > 4) { ConversationTick.RemoveAt(i); continue; }
                var c = r.Data;
                // a speaker died
                if ((c.Flags & StopIfDeath) != 0)
                    for (int k = 0; k < r.Speakers.Length; k++)
                        if (!r.Disembodied[k] && r.Speakers[k] != null && !r.Speakers[k].Alive) { r.Status = 7; break; }
                if (r.Status == 7) continue;
                if (r.Status == 1 || r.Status == 2)
                {
                    if (!Begin(r)) continue;
                    r.Status = 3; r.Line = -1; r.NextAt = Time;
                }
                if (r.Status == 4)
                {
                    if (!r.AdvanceAllowed) continue;
                    r.AdvanceAllowed = false; r.Status = 3;
                }
                if (r.Status == 3 && Time >= r.NextAt)
                {
                    int next = r.Line + 1;
                    if (next >= c.Lines.Count) { r.Status = 6; continue; }
                    // a line waits for advance after the previous one when flagged
                    if (r.Line >= 0 && (c.Lines[r.Line].Flags & LineWaitUntilAdvance) != 0 && !r.AdvanceAllowed) { r.Status = 4; continue; }
                    r.AdvanceAllowed = false;
                    r.Line = next;
                    var ln = c.Lines[next];
                    HaloUnit sp = ln.Participant >= 0 && ln.Participant < r.Speakers.Length ? r.Speakers[ln.Participant] : null;
                    HaloTag snd = PickVariant(ln, sp);
                    float dur = 2f;
                    if (snd != null)
                    {
                        var s = HaloSound.Get(snd);
                        if (s != null && s.LongestPermutationMs > 0) dur = s.LongestPermutationMs / 1000f;
                        if (sp != null) HaloAudio.Play(snd, sp.transform); else HaloAudio.Play2D(snd);
                    }
                    var h = LineSpoken; if (h != null) { try { h(sp, snd, c.Name); } catch (System.Exception e) { HaloLog.Warn("AI: LineSpoken listener: " + e.Message); } }
                    // the speaker looks at whom it addresses
                    if (sp != null)
                    {
                        var a = ActorOf(sp);
                        if (a != null)
                        {
                            if (ln.Addressee == 1) { var pl = HaloAiBodies.NearestPlayer(sp.transform.position); if (pl != null) sp.LookPoint = pl.Head; }
                            else if (ln.Addressee == 2 && ln.AddresseeParticipant >= 0 && ln.AddresseeParticipant < r.Speakers.Length && r.Speakers[ln.AddresseeParticipant] != null)
                                a.LookAtObject = r.Speakers[ln.AddresseeParticipant].gameObject;
                        }
                    }
                    float delay = next + 1 < c.Lines.Count ? Mathf.Max(0f, c.Lines[next + 1].Delay) : 0f;
                    r.NextAt = Time + dur + delay;
                }
            }
        }

        /// <summary>
        /// Status 1 / 2: missing speakers are looked for again (the "keep trying to play" flag), the nearest player must be within the
        /// trigger distance of the first speaker, then speakers farther than the run-to-player distance walk over (status 2, at most 8 s).
        /// </summary>
        static bool Begin(ConversationRun r)
        {
            var c = r.Data;
            HaloUnit first = null;
            for (int k = 0; k < r.Speakers.Length; k++)
            {
                if (r.Speakers[k] == null && !r.Disembodied[k]) { r.Speakers[k] = FindSpeaker(c.Participants[k]); }
                if (first == null) first = r.Speakers[k];
            }
            if (first == null) return true;          // disembodied only
            var p = HaloAiBodies.NearestPlayer(first.transform.position);
            if (p == null) return true;
            if (c.TriggerDistance > 0f && (p.Feet - first.transform.position).magnitude > c.TriggerDistance * HaloSpace.Unit && r.Status == 1) return false;
            if (r.Status == 1) { r.Status = 2; r.NextAt = Time + 8f; }
            bool ready = true;
            float run = c.RunToPlayerDistance * HaloSpace.Unit;
            if (run > 0f)
                for (int k = 0; k < r.Speakers.Length; k++)
                {
                    var u = r.Speakers[k];
                    if (u == null) continue;
                    if ((u.transform.position - p.Feet).magnitude <= run) continue;
                    var a = ActorOf(u);
                    if (a != null) { a.MoveTo(HaloActor.OnNav(p.Feet + (u.transform.position - p.Feet).normalized * run * 0.8f), 0.8f, 0.6f); ready = false; }
                }
            return ready || Time >= r.NextAt;
        }

        static HaloTag PickVariant(HaloAiScenario.ConversationLine ln, HaloUnit speaker)
        {
            // the speaker's dialogue variant would pick one of the six; without it the first present one
            for (int k = 0; k < ln.Variants.Length; k++) if (ln.Variants[k] != null) return ln.Variants[k];
            return null;
        }
    }
}

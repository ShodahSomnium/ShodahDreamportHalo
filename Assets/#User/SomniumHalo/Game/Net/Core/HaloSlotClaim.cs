using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// What every client makes of the player slots it reads (EtsSlotRules' / WowSlotRules' / Bo2SlotRules' rules, unchanged in meaning). "present" is the room's
    /// ActivePlayers as PlayerIds, "slot" a slot's ints as last read.
    /// </summary>
    public static class HaloSlotRules
    {
        public static bool Contains(IReadOnlyList<int> present, int playerId)
        {
            if (present == null || playerId < 0) return false;
            for (int i = 0; i < present.Count; i++) if (present[i] == playerId) return true;
            return false;
        }

        /// <summary>A slot this client may claim: never written, written free, owned by a player no longer in the room, or already ours.</summary>
        public static bool FreeFor(int[] slot, int localId, IReadOnlyList<int> present)
        {
            if (slot == null || slot.Length < HaloNetWire.Used || slot[HaloNetWire.AtMagic] != HaloNetWire.Magic) return true;
            int owner = HaloNetWire.OwnerOf(slot);
            return owner < 0 || owner == localId || !Contains(present, owner);
        }

        /// <summary>Written, its owner in the room and not this client.</summary>
        public static bool Listed(int[] slot, int localId, IReadOnlyList<int> present)
        {
            int owner = HaloNetWire.OwnerOf(slot);
            return owner >= 0 && owner != localId && Contains(present, owner);
        }

        /// <summary>A slot whose player is in a match: Listed and playing.</summary>
        public static bool Counts(int[] slot, int localId, IReadOnlyList<int> present)
        {
            return Listed(slot, localId, present) && HaloNetWire.StatusOf(slot) == HaloNetStatus.Playing;
        }

        /// <summary>
        /// Housekeeping by whoever holds a slot's authority: write it free when it names a player who has left (the master gets the orphans from
        /// SceneNetworking), or this client itself while it is not the slot this client claims.
        /// </summary>
        public static bool ToFree(int[] slot, bool haveAuthority, bool claimedHere, int localId, IReadOnlyList<int> present)
        {
            if (!haveAuthority || claimedHere || slot == null || slot.Length < HaloNetWire.Used || slot[HaloNetWire.AtMagic] != HaloNetWire.Magic) return false;
            int owner = HaloNetWire.OwnerOf(slot);
            return owner >= 0 && (owner == localId || !Contains(present, owner));
        }
    }

    /// <summary>
    /// This client's claim on one player slot: EtsSlotClaim's / WowSlotClaim's state machine (Idle -> Asking -> Confirming -> Owned, the same timings),
    /// which the ETS probe room ran against a simulated Fusion through joins, leaves, simultaneous claims and a master hand-over. HaloNet feeds it what the
    /// room says each frame and carries out what it asks (RequestStateAuthority on AskAuthority; the claim record on WriteClaim). Nothing is sent on
    /// leaving: the slot's owner stops being present and whoever holds the authority writes it free.
    /// </summary>
    public sealed class HaloSlotClaim
    {
        public const int Slots = 50;
        public const double AuthorityWait = 3.0;
        public const double ConfirmAfter = 1.0;
        public const double AskEvery = 1.0;
        public const double LostPatience = 1.5;
        public const double RoundPause = 5.0;

        public enum Stage { Idle, Asking, Confirming, Owned }

        public struct Inputs
        {
            public double Now;
            public int LocalId;
            public bool Allowed;     // the room is up and the slots are found
            public int SlotCount;
        }

        public Stage Current { get; private set; }
        public int Slot { get; private set; } = -1;
        public int Generation { get; private set; }
        public int AskAuthority { get; private set; } = -1;
        public bool WriteClaim { get; private set; }
        public bool Holding { get { return Slot >= 0 && (Current == Stage.Confirming || Current == Stage.Owned); } }
        public double ClaimSeconds { get; private set; }

        private readonly bool[] _tried = new bool[Slots];
        private double _since, _nextAsk, _lostAt = -1.0, _roundAt = double.NegativeInfinity, _began;
        private int _localId = -1;
        private string _note;

        public string TakeNote()
        {
            string note = _note;
            _note = null;
            return note;
        }

        public void Reset()
        {
            Current = Stage.Idle;
            Slot = -1;
            _lostAt = -1.0;
            Array.Clear(_tried, 0, _tried.Length);
            _roundAt = double.NegativeInfinity;
        }

        public void Step(in Inputs input, int[][] slots, bool[] valid, bool[] authority, IReadOnlyList<int> present)
        {
            AskAuthority = -1;
            WriteClaim = false;

            if (!input.Allowed || input.LocalId < 0 || input.LocalId != _localId)
            {
                if (Current != Stage.Idle || Slot >= 0)
                    _note = "claim dropped (" + (!input.Allowed ? "the room is not up" : "a new local player id") + ")";
                Reset();
                _localId = input.LocalId;
                if (!input.Allowed || input.LocalId < 0) return;
            }

            double now = input.Now;
            int count = Math.Min(Math.Min(input.SlotCount, Slots), slots.Length);

            switch (Current)
            {
                case Stage.Idle:
                {
                    if (now < _roundAt) return;
                    int k = FindFree(input.LocalId, count, slots, valid, present);
                    if (k < 0)
                    {
                        bool any = false;
                        for (int i = 0; i < count; i++) any |= _tried[i];
                        Array.Clear(_tried, 0, _tried.Length);
                        _roundAt = now + RoundPause;
                        _note = any ? "no slot could be claimed this round; trying all again in " + RoundPause + " s" : "no free slot (" + count + " slots)";
                        return;
                    }

                    Slot = k;
                    Current = Stage.Asking;
                    _since = now;
                    _began = now;
                    _nextAsk = now + AskEvery;
                    AskAuthority = k;
                    return;
                }

                case Stage.Asking:
                {
                    if (!valid[Slot] || !HaloSlotRules.FreeFor(slots[Slot], input.LocalId, present))
                    {
                        Give("slot " + Slot + " taken by player " + HaloNetWire.OwnerOf(slots[Slot]) + " before the claim; trying the next");
                        return;
                    }

                    if (authority[Slot])
                    {
                        Generation = (HaloNetWire.GenerationOf(slots[Slot]) + 1) & 0xFF;
                        WriteClaim = true;
                        Current = Stage.Confirming;
                        _since = now;
                        return;
                    }

                    if (now - _since >= AuthorityWait)
                    {
                        Give("no authority over slot " + Slot + " in " + AuthorityWait + " s; trying the next");
                        return;
                    }

                    if (now >= _nextAsk)
                    {
                        AskAuthority = Slot;
                        _nextAsk = now + AskEvery;
                    }

                    return;
                }

                case Stage.Confirming:
                {
                    int owner = HaloNetWire.OwnerOf(slots[Slot]);
                    if (owner >= 0 && owner != input.LocalId && HaloSlotRules.Contains(present, owner))
                    {
                        Give("slot " + Slot + " lost to player " + owner + ", trying " + ((Slot + 1) % Math.Max(1, count)));
                        return;
                    }

                    if (!authority[Slot])
                    {
                        if (owner == input.LocalId)
                        {
                            // Our record stands but a later request took the authority (see EtsSlotClaim): ask it back
                            // after LostPatience and confirm afresh once it is here again.
                            if (_lostAt < 0.0)
                            {
                                _lostAt = now;
                                _nextAsk = now + LostPatience;
                            }
                            else if (now >= _nextAsk)
                            {
                                AskAuthority = Slot;
                                _nextAsk = now + AskEvery;
                            }

                            return;
                        }

                        if (now - _since >= AuthorityWait) Give("authority over slot " + Slot + " went elsewhere and nobody wrote it; trying the next");
                        return;
                    }

                    if (_lostAt >= 0.0)
                    {
                        _lostAt = -1.0;
                        _since = now;
                        return;
                    }

                    if (owner != input.LocalId)
                    {
                        if (now - _since >= ConfirmAfter)
                        {
                            WriteClaim = true;
                            _since = now;
                        }

                        return;
                    }

                    if (now - _since >= ConfirmAfter)
                    {
                        Current = Stage.Owned;
                        ClaimSeconds = now - _began;
                        _lostAt = -1.0;
                        Array.Clear(_tried, 0, _tried.Length);
                        _note = "slot " + Slot + " claimed (generation " + Generation + ") in " + Math.Round(ClaimSeconds * 1000.0) + " ms";
                    }

                    return;
                }

                case Stage.Owned:
                {
                    int owner = HaloNetWire.OwnerOf(slots[Slot]);
                    if (owner >= 0 && owner != input.LocalId && HaloSlotRules.Contains(present, owner))
                    {
                        int lost = Slot;
                        Reset();
                        _note = "slot " + lost + " was taken over by player " + owner + "; claiming again";
                        return;
                    }

                    if (!authority[Slot])
                    {
                        if (_lostAt < 0.0)
                        {
                            _lostAt = now;
                            _nextAsk = now + LostPatience;
                        }
                        else if (now >= _nextAsk)
                        {
                            AskAuthority = Slot;
                            _nextAsk = now + AskEvery;
                        }

                        return;
                    }

                    _lostAt = -1.0;
                    if (owner != input.LocalId)
                    {
                        Generation = (Generation + 1) & 0xFF;
                        WriteClaim = true;
                        _note = "slot " + Slot + " read " + (owner < 0 ? "free" : "as player " + owner + "'s (gone)") + " while held; claimed again (generation " + Generation + ")";
                    }

                    return;
                }
            }
        }

        private void Give(string why)
        {
            if (Slot >= 0 && Slot < _tried.Length) _tried[Slot] = true;
            Current = Stage.Idle;
            Slot = -1;
            _lostAt = -1.0;
            _note = why;
        }

        private int FindFree(int localId, int count, int[][] slots, bool[] valid, IReadOnlyList<int> present)
        {
            if (count <= 0) return -1;
            int k0 = localId % count;
            for (int i = 0; i < count; i++)
            {
                int k = (k0 + i) % count;
                if (valid[k] && !_tried[k] && HaloSlotRules.FreeFor(slots[k], localId, present)) return k;
            }

            return -1;
        }
    }
}

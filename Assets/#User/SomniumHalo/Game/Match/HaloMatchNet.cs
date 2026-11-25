using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The Match module's side of the net layer (Game/Net HaloNetGame): the remote players of my match into <see cref="HaloRoster"/> every
    /// frame, the local objective state out (SetCarrying / NoteGameEvent) and the remote players' game events in (GameEvent), and as
    /// instance master the objective snapshot for late joiners (SetObjective / TryObjective). Alone / offline everything here is a no-op.
    /// </summary>
    static class HaloMatchNet
    {
        static bool _hooked;
        static int _sentCarry = -2, _sentEvent;
        static readonly int[] SentState = new int[3];
        static readonly int[] SentCarrier = new int[3];

        public static void Begin()
        {
            if (!_hooked)
            {
                HaloNetGame.GameEvent += OnGameEvent;
                HaloNetGame.PlayerLeft += OnPlayerLeft;
                _hooked = true;
            }
            _sentCarry = -2; _sentEvent = HaloLocal.ObjEvent;
            for (int i = 0; i < 3; i++) { SentState[i] = -1; SentCarrier[i] = int.MinValue; }
            // a late joiner takes the master's objective snapshot
            for (int i = 0; i < 3; i++)
            {
                HaloNetObjective o;
                if (HaloNetGame.TryObjective(i, out o)) HaloObjectives.ApplySnapshot(i + 1, o.State, o.Carrier, o.Position, o.Timer);
            }
        }

        public static void End()
        {
            if (_hooked)
            {
                HaloNetGame.GameEvent -= OnGameEvent;
                HaloNetGame.PlayerLeft -= OnPlayerLeft;
                _hooked = false;
            }
            try { HaloNetGame.SetCarrying(-1); } catch (System.Exception) { }
        }

        /// <summary>Every frame in a match: roster in, objective state out.</summary>
        public static void Tick()
        {
            var list = HaloNetGame.Players;
            for (int i = 0; i < list.Count; i++)
            {
                HaloNetPlayer n = list[i];
                if (n == null || !n.InMatch) continue;
                HaloMatchPlayer p = HaloRoster.Remote(n.Slot);
                p.Name = n.Name; p.Team = n.Team; p.Alive = n.Alive; p.Position = n.Position;
                p.Forward = n.Forward;
                p.Kills = n.Kills; p.Deaths = n.Deaths; p.Assists = n.Assists; p.Score = n.Score;
                p.Carry = n.Carry;
                p.Seen = Time.time;
            }
            // remotes that are no longer in my match
            for (int i = HaloRoster.Players.Count - 1; i >= 0; i--)
            {
                HaloMatchPlayer p = HaloRoster.Players[i];
                if (p.Local) continue;
                HaloNetPlayer n = HaloNetGame.Find(p.Id);
                if (n == null || !n.InMatch) HaloMatchControl.PlayerLeft(p.Id);
            }

            if (HaloLocal.Carry - 1 != _sentCarry) { _sentCarry = HaloLocal.Carry - 1; HaloNetGame.SetCarrying(_sentCarry); }
            if (HaloLocal.ObjEvent != _sentEvent)
            {
                _sentEvent = HaloLocal.ObjEvent;
                HaloNetGame.NoteGameEvent(HaloLocal.ObjEventCode / 16, HaloLocal.ObjEventCode % 16);
            }
            if (HaloNetGame.WorldOwner && HaloNetGame.Networked)      // (N: the world owner publishes the objectives)
            {
                for (int i = 0; i < 3; i++)
                {
                    HaloObjectives.Item it = HaloObjectives.ItemFor(i + 1);
                    if (!it.Present) continue;
                    int st = (int)it.State;
                    if (st == SentState[i] && it.Carrier == SentCarrier[i]) continue;
                    SentState[i] = st; SentCarrier[i] = it.Carrier;
                    HaloNetGame.SetObjective(i, st, it.State == HaloObjectives.State.Carried ? it.Carrier : -1, it.Position, (int)(Time.time - it.DroppedAt));
                }
            }
        }

        static void OnGameEvent(int slot, int kind, int arg)
        {
            if (kind < 1 || kind > 15 || slot == HaloLocal.NetId) return;
            HaloNetPlayer n = HaloNetGame.Find(slot);
            Vector3 at = n != null ? n.Position : Vector3.zero;
            HaloMatchControl.RemoteObjectiveEvent(slot, kind * 16 + arg, at);
        }

        static void OnPlayerLeft(HaloNetPlayer n)
        {
            if (n != null) HaloMatchControl.PlayerLeft(n.Slot);
        }
    }
}

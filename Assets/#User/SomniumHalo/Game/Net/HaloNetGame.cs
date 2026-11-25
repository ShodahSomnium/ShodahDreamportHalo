using System;
using System.Collections.Generic;
using SomniumSpace.Bridge;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The net layer's public face (the Black Ops 2 Dreamport's Bo2Instance + Bo2NetGame for Halo). ONE <see cref="HaloNet"/> session lives from the moment
    /// the Dreamport starts (HaloGame.NetStart) to the end of the visit: the player's slot record (present in the hub, playing in a match), the instance
    /// master, the lobby and its rules, the game the master starts (everybody in the instance is pulled into it; a player who arrives while it runs joins it),
    /// the other players (<see cref="Players"/>: name, side, scores, pose; in my match also drawn: weapons, hit boxes, shots, grenades), the hits between
    /// players (shooter measures, victim applies once), deaths with their killer (the kill feed through HaloMatchControl.ReportKill), the objective events
    /// and the world state of the instance master (pickups, objectives, parked vehicles).
    ///
    /// Alone, offline, in the editor or without the scene's slot objects nothing is sent and nothing else is drawn: <see cref="IsMaster"/> is true and
    /// <see cref="StartMatch"/> starts a solo match after the same countdown. Every entry point catches its own errors (a failure is a log line, never a
    /// broken game). Main thread only. The API is documented in DevTools/agents/N/NOTES.md.
    /// </summary>
    public static partial class HaloNetGame
    {
        /// <summary>Lab and tests: where the records come from instead of the Fusion session (null = the real one).</summary>
        public static Func<IHaloSlotTransport> TransportFactory;

        /// <summary>The clock of the net layer in seconds (record stamps, write rate, claim timings): real time that does not stop with the game.</summary>
        public static Func<double> Clock = () => Time.unscaledTimeAsDouble;

        /// <summary>The session (null before the Dreamport started). For the lab and tests; modules use the statics below.</summary>
        public static HaloNet Net { get; private set; }

        private static bool _up;
        private static HaloGame _game;
        private static readonly HaloNetPlayer[] BySlot = new HaloNetPlayer[HaloNetWire.MaxSlots];
        private static readonly List<HaloNetPlayer> List = new List<HaloNetPlayer>(HaloNetWire.MaxSlots);
        private static float _nextNames, _nextStats;
        private static int _errors;
        private static string _localNick;

        /// <summary>The net layer runs (the Dreamport started for a whitelisted player).</summary>
        public static bool Up { get { return _up && Net != null; } }

        /// <summary>In a Fusion room with the slot objects (else: alone, everything local).</summary>
        public static bool Online { get { return Up && Net.Online; } }

        /// <summary>My slot is claimed (<see cref="HaloLocal.NetId"/> is my slot).</summary>
        public static bool Claimed { get { return Up && Net.Claimed; } }

        /// <summary>I am the instance master (true alone / offline / before the layer runs).</summary>
        public static bool IsMaster { get { return !Up || Net.IsInstanceMaster; } }

        /// <summary>False while online and the master is not known yet (my claim is not done): grey out the master's controls.</summary>
        public static bool MasterKnown { get { return !Up || !Net.MasterPending; } }

        /// <summary>The instance master's name ("connecting..." while not known).</summary>
        public static string MasterName
        {
            get
            {
                if (!Online) return LocalNick();
                if (Net.MasterPending) return "connecting...";
                if (Net.IsInstanceMaster) return LocalNick();
                HaloNet.Remote r = Net.RemoteOfPlayer(Net.MasterId);
                return r != null ? r.Name : "Player " + Net.MasterId;
            }
        }

        /// <summary>The other Dreamport players in the instance (hub, lobby or a match), in slot order. Do not keep the list; the objects stay valid while listed.</summary>
        public static IReadOnlyList<HaloNetPlayer> Players { get { return List; } }

        /// <summary>Dreamport players in the instance, me included.</summary>
        public static int PlayerCount { get { return List.Count + 1; } }

        /// <summary>Another human is in MY match.</summary>
        public static bool Networked { get { return Up && Net.Remotes.Count > 0; } }

        /// <summary>A listed player by slot (null when nobody of ours holds it).</summary>
        public static HaloNetPlayer Find(int slot)
        {
            return slot >= 0 && slot < BySlot.Length ? BySlot[slot] : null;
        }

        /// <summary>A player's name by slot, me included ("?" when unknown).</summary>
        public static string NameOf(int slot)
        {
            if (slot >= 0 && slot == HaloLocal.NetId) return LocalNick();
            HaloNetPlayer p = Find(slot);
            return p != null ? p.Name : slot < 0 ? "?" : "Player " + slot;
        }

        /// <summary>A player came into / went out of MY match (their drawn weapons and hit boxes exist between the two).</summary>
        public static event Action<HaloNetPlayer> PlayerJoined, PlayerLeft;

        /// <summary>The isolation (Game/Access/HaloGear): true when that player (PhotonId) holds a claimed slot; <paramref name="known"/> = the layer is online and can tell.</summary>
        public static bool TryHasRecord(int photonId, out bool known)
        {
            known = Up && Net.Online;
            return known && Net.HasRecord(photonId);
        }

        // ------------------------------------------------------------------ the session's life (HaloGame.Net.cs)

        internal static void Start(HaloGame game)
        {
            if (_up) return;
            _game = game;
            IHaloSlotTransport transport = TransportFactory != null ? TransportFactory() : new HaloFusionTransport();
            Net = new HaloNet(transport, () => Clock(), "");
            HaloNet.UtcClock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            _up = true;
            Subscribe();
            HaloLobbyNet.Net = Link;
            HaloLog.Info("Net: started (" + (TransportFactory != null ? "test transport" : "Fusion session") + "); alone or offline everything stays local");
        }

        internal static void Stop()
        {
            if (!_up) return;
            _up = false;
            try
            {
                Unsubscribe();
                ClearViews();
                ClearCoop();
                for (int k = 0; k < BySlot.Length; k++) BySlot[k] = null;
                List.Clear();
                if (Net != null) Net.Dispose();
            }
            catch (Exception e)
            {
                HaloLog.Warn("Net: stopping failed: " + e.Message);
            }

            HaloLocal.NetId = -1;
            HaloLocal.NetOnline = false;
            if (HaloLobbyNet.Net == Link) HaloLobbyNet.Net = null;
            Net = null;
            HaloLog.Info("Net: stopped");
        }

        /// <summary>Once a frame (HaloGame.Update, before the Front and the Match): publish, tick, act on what the others did, the lobby and the game. Never throws.</summary>
        internal static void Tick()
        {
            if (!Up) return;
            try
            {
                Publish();
                Net.Tick();
                HaloLocal.NetId = Net.Claimed ? Net.MySlot : -1;
                HaloLocal.NetOnline = Net.Online;
                HaloLocal.NetMaster = IsMaster;
                RefreshPlayers();
                Consume();
                StepInstance();
                StepCoopGame();
                if (Time.unscaledTime >= _nextStats)
                {
                    _nextStats = Time.unscaledTime + 120f;
                    if (Net.Online) HaloLog.Info(Net.StatsLine());
                }
            }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 3) HaloLog.Warn("Net: the frame failed (" + _errors + (_errors == 3 ? ", later ones not logged" : "") + "): " + e);
            }
        }

        /// <summary>Once a frame after the animation (HaloNetDriver.LateUpdate): the remote players' drawn pose, weapons and hit boxes. Never throws.</summary>
        internal static void Late()
        {
            if (!Up) return;
            try
            {
                LateViews();
            }
            catch (Exception e)
            {
                _errors++;
                if (_errors <= 3) HaloLog.Warn("Net: the late frame failed (" + _errors + (_errors == 3 ? ", later ones not logged" : "") + "): " + e);
            }
        }

        // ------------------------------------------------------------------ the players list

        private static void RefreshPlayers()
        {
            bool names = Time.unscaledTime >= _nextNames;
            if (names) _nextNames = Time.unscaledTime + 1f;
            IReadOnlyList<HaloNet.Remote> listed = Net.Listed;
            // gone: every slot not listed any more
            for (int k = 0; k < BySlot.Length; k++)
            {
                HaloNetPlayer p = BySlot[k];
                if (p == null) continue;
                HaloNet.Remote r = Net.RemoteAt(k);
                if (r != null && r.Decoded && r.PlayerId == p.PlayerId) continue;
                BySlot[k] = null;
                List.Remove(p);
            }

            for (int i = 0; i < listed.Count; i++)
            {
                HaloNet.Remote r = listed[i];
                HaloNetPlayer p = BySlot[r.Slot];
                if (p == null)
                {
                    p = new HaloNetPlayer { Slot = r.Slot, PlayerId = r.PlayerId };
                    BySlot[r.Slot] = p;
                    InsertSorted(p);
                    names = true;
                }

                if (names) ResolveName(r, p);
                Fill(p, r);
            }
        }

        private static void InsertSorted(HaloNetPlayer p)
        {
            int at = 0;
            while (at < List.Count && List[at].Slot < p.Slot) at++;
            List.Insert(at, p);
        }

        private static void Fill(HaloNetPlayer p, HaloNet.Remote r)
        {
            HaloNetState st = r.State;
            bool teams = Net.TeamGame;
            p.IsMaster = r.PlayerId == Net.MasterId;
            p.Status = st.Status;
            p.Reloads = st.ReloadCounter; p.Melees = st.MeleeCounter; p.Throws = st.ThrowCounter;
            p.InMatch = r.InMatch;
            p.Alive = st.Alive && st.Status == HaloNetStatus.Playing;
            p.Team = st.Team & 1;
            p.Enemy = !teams || (st.Team & 1) != (MyTeam & 1);
            p.Kills = st.Kills; p.Deaths = st.Deaths; p.Assists = st.Assists; p.Score = st.Score; p.LivesLeft = st.LivesLeft;
            p.Carry = st.Carry;
            p.Health = st.Health; p.Shield = st.Shield;
            p.Camo = st.Camo; p.Vr = st.Vr; p.Zoomed = st.Zoomed; p.Crouch = st.Crouch; p.Reloading = st.Reloading; p.InHand = st.InHand;
            p.HeldWeapon = r.InMatch ? HaloNetU.TagOf(st.Held) : null;
            p.StowedWeapon = r.InMatch ? HaloNetU.TagOf(st.Stowed) : null;
            p.Frags = st.Frags; p.Plasmas = st.Plasmas;
            p.VehicleIndex = st.Vehicle; p.Seat = st.Seat; p.Driving = st.Driving;
            p.Aim = HaloNetU.V(st.Aim);
            p.FiredRecently = r.LastShotAt > Net.Now - 0.5;
            if (r.Fresh) p.Seen = Time.time;
            if (!r.InMatch)
            {
                // not drawn: the record's own pose (not interpolated)
                Vector3 pos = HaloNetU.V(st.Root);
                p.Velocity = Vector3.zero;
                p.Moving = false;
                p.Position = pos;
                p.Head = HaloNetU.V(st.Head);
                p.HeadRot = HaloNetU.Q(st.HeadRot);
                p.Yaw = st.RootYaw;
                p.Forward = Quaternion.Euler(0f, st.RootYaw, 0f) * Vector3.forward;
            }
        }

        private static void ResolveName(HaloNet.Remote r, HaloNetPlayer p)
        {
            try
            {
                IReadOnlyList<HaloRemotes.Remote> all = HaloRemotes.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Id != r.PlayerId) continue;
                    if (!string.IsNullOrEmpty(all[i].Nick)) r.Name = all[i].Nick;
                    p.Root = all[i].Root;
                    try { p.BridgeHead = all[i].Player != null && all[i].Player.References != null && all[i].Player.References.Body != null ? all[i].Player.References.Body.Head : null; }
                    catch (Exception) { p.BridgeHead = null; }
                    break;
                }
            }
            catch (Exception) { /* the Bridge is not up */ }

            if (LabNames != null)
            {
                string n = LabNames(r.PlayerId);
                if (!string.IsNullOrEmpty(n)) r.Name = n;
            }

            p.Name = r.Name;
        }

        /// <summary>Lab: names of the scripted players (null = the Bridge's nicknames).</summary>
        public static Func<int, string> LabNames;

        /// <summary>My Somnium nickname (Bridge: PlayersContainer.LocalPlayer.Properties.NickName); "You" when the Bridge is not up (the editor).</summary>
        public static string LocalNick()
        {
            if (!string.IsNullOrEmpty(_localNick)) return _localNick;
            try
            {
                var p = SomniumBridge.PlayersContainer != null ? SomniumBridge.PlayersContainer.LocalPlayer : null;
                string n = p != null && p.Properties != null ? p.Properties.NickName : null;
                if (!string.IsNullOrEmpty(n)) { _localNick = n; return n; }
            }
            catch (Exception) { /* no Bridge */ }

            return string.IsNullOrEmpty(HaloLocal.Nick) ? "You" : HaloLocal.Nick;
        }

        /// <summary>
        /// The Desk module poses monitor players' avatars (and seated ones) from what Net reports: once a frame per drawn player of my match, after the
        /// views moved (LateUpdate 1000, before Desk's 9996). The held weapon model is mine (Desk places it in the posed hand of a monitor player).
        /// </summary>
        private static void ReportDesk(HaloRemoteView v)
        {
            HaloNetPlayer p = v.Player;
            HaloNet.Remote r = v.Record;
            if (p == null || r == null) return;
            GameObject vehicle = null;
            string seat = null;
            if (p.VehicleIndex >= 0)
            {
                HaloVehicle hv = HaloVehicles.Find(p.VehicleIndex);
                if (hv != null)
                {
                    vehicle = hv.gameObject;
                    if (p.Seat >= 0 && p.Seat < hv.Seats.Count) seat = hv.Seats[p.Seat].Label;
                }
            }

            var d = new HaloDeskRemote
            {
                Slot = p.Slot, Vr = p.Vr, Alive = p.Alive, Crouch = p.Crouch, Zoomed = p.Zoomed, Head = p.Head, Feet = p.Position, HeadRot = p.HeadRot,
                Avatar = p.Avatar, Root = p.Root, Weapon = p.InHand ? p.HeldWeapon : null, WeaponObject = v.HeldModel,
                Reloads = p.Reloads, Melees = p.Melees, Throws = p.Throws, Vehicle = vehicle, SeatLabel = seat,
            };
            HaloDesk.ReportRemote(in d);
        }
    }

    /// <summary>Runs the net layer's late frame (remote players' drawn pose and hit boxes after the avatars' animation). Added by HaloGame.Net.cs.</summary>
    [DefaultExecutionOrder(1000)]
    public sealed class HaloNetDriver : MonoBehaviour
    {
        private void LateUpdate()
        {
            HaloNetGame.Late();
        }
    }
}

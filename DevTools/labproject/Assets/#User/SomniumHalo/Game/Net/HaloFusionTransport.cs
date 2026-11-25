using System;
using System.Collections.Generic;
using System.Reflection;
using Fusion;
using SomniumSpace.Network.Bridge;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The records over the world's Photon Fusion session (Bo2FusionTransport ported): the Community Modules' shared-mode session every player in the instance
    /// is already in (SceneNetworking, this project's copy), the networked objects placed in the scene by hand - "Halo Net Player 0..49" and the optional
    /// "Halo Net World 0..3" (a NetworkObject + NetworkBridgeData64 each; none can be made at run time), found by name. Alone, in the editor, before the room
    /// is joined or without the slot objects it is simply not <see cref="Online"/>. Every Fusion access is inside try / catch with throttled logging, and only
    /// members other worlds have passed the upload server's compile with are used (DevTools/fusionapi/check.sh).
    /// </summary>
    public sealed class HaloFusionTransport : IHaloSlotTransport
    {
        private readonly NetworkBridgeData[] _objects = new NetworkBridgeData[HaloNet.Records];
        private readonly List<int> _present = new List<int>(64);
        private int _slotCount, _worldCount, _coopCount;
        private bool _slotsComplete, _subscribed, _presentRead;
        private int _slotsLogged = -1, _worldLogged = -1;
        private double _nextFind, _presentAt;
        private int _errors;

        private static NetworkRunner Runner { get { return SceneNetworking.NetworkRunnerRef; } }

        public bool Online { get { return SceneNetworking.IsNetworkReady && Runner != null && _slotCount > 0; } }

        public int LocalId
        {
            get
            {
                try { return Runner != null && SceneNetworking.IsNetworkReady ? Runner.LocalPlayer.PlayerId : -1; }
                catch (Exception e) { Warn("reading the local player id", e); return -1; }
            }
        }

        public int SlotCount { get { return _slotCount; } }

        public int WorldCount { get { return _worldCount; } }

        public int CoopCount { get { return _coopCount; } }

        public bool IsMaster { get { return SceneNetworking.IsMasterClient; } }

        public IReadOnlyList<int> Present { get { return _present; } }

        public void Update(double now)
        {
            try
            {
                bool room = SceneNetworking.IsNetworkReady && Runner != null;
                if (room && !_subscribed)
                {
                    SceneNetworking.OnOtherPlayerJoined += OnRoomPlayersChanged;
                    SceneNetworking.OnOtherPlayerLeft += OnRoomPlayersChanged;
                    _subscribed = true;
                }

                if (room) Find(now);
                RefreshPresent(room, now);
            }
            catch (Exception e)
            {
                Warn("the update", e);
            }
        }

        private void OnRoomPlayersChanged(PlayerRef player)
        {
            _presentAt = 0.0;
        }

        private static bool Valid(NetworkBehaviour behaviour)
        {
            return behaviour != null && behaviour.Object != null && behaviour.Object.IsValid;
        }

        private bool InRange(int record)
        {
            if (record < 0) return false;
            if (record < HaloNetWire.MaxSlots) return record < _slotCount;
            if (record < HaloNetCoop.FirstActor) return record - HaloNetWire.MaxSlots < _worldCount;
            return record - HaloNetCoop.FirstActor < _coopCount;
        }

        public bool IsValid(int record)
        {
            return InRange(record) && Valid(_objects[record]) && SceneNetworking.IsNetworkReady && Runner != null;
        }

        public bool HasAuthority(int record)
        {
            try { return IsValid(record) && _objects[record].Object.HasStateAuthority; }
            catch (Exception e) { Warn("reading the state authority of record " + record, e); return false; }
        }

        public bool Read(int record, int[] into)
        {
            try
            {
                if (!IsValid(record)) return false;
                NetworkArray<int> ints = _objects[record].IntArray;
                int n = Math.Min(ints.Length, into.Length);
                for (int i = 0; i < n; i++) into[i] = ints.Get(i);
                return true;
            }
            catch (Exception e)
            {
                Warn("reading record " + record, e);
                return false;
            }
        }

        /// <summary>Sets the ints that differ (Fusion sends a changed int only); nothing without the object's authority.</summary>
        public int Write(int record, int[] values)
        {
            try
            {
                NetworkBridgeData data = InRange(record) ? _objects[record] : null;
                if (!Valid(data) || !data.Object.HasStateAuthority) return 0;
                NetworkArray<int> ints = data.IntArray;
                int n = Math.Min(ints.Length, values.Length);
                int changed = 0;
                for (int i = 0; i < n; i++)
                {
                    if (ints.Get(i) != values[i]) { ints.Set(i, values[i]); changed++; }
                }

                return changed;
            }
            catch (Exception e)
            {
                Warn("writing record " + record, e);
                return 0;
            }
        }

        private bool _requestRefused;

        public void RequestAuthority(int record)
        {
            try
            {
                NetworkBridgeData data = InRange(record) ? _objects[record] : null;
                if (!Valid(data)) return;
#if FUSION_2_1_OR_NEWER
                // Fusion 2.1: false = not sent (the object lacks AllowStateAuthorityOverride, or no runner) - the claim / block writer asks again in a second
                if (!data.Object.RequestStateAuthority() && !_requestRefused)
                {
                    _requestRefused = true;
                    HaloLog.Warn("Net: Fusion refused a state authority request for '" + data.name + "' (record " + record + ") - does the object allow state authority override?");
                }
#else
                data.Object.RequestStateAuthority();
#endif
            }
            catch (Exception e)
            {
                Warn("requesting the state authority of record " + record, e);
            }
        }

        /// <summary>The scene objects by name, once a second until all are found, then every 5 s while some are missing.</summary>
        private void Find(double now)
        {
            if (_slotsComplete || now < _nextFind) return;
            _nextFind = now + 1.0;
            int found = 0, worlds = 0, highest = -1;
            foreach (NetworkBridgeData data in UnityEngine.Object.FindObjectsByType<NetworkBridgeData>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string name = data.name;
                int k, record;
                if (name.StartsWith(HaloNetWire.ObjectPrefix, StringComparison.Ordinal) && int.TryParse(name.Substring(HaloNetWire.ObjectPrefix.Length), out k) && k >= 0 && k < HaloNetWire.MaxSlots)
                    record = k;
                else if (name.StartsWith(HaloNetWorld.ObjectPrefix, StringComparison.Ordinal) && int.TryParse(name.Substring(HaloNetWorld.ObjectPrefix.Length), out k) && k >= 0 && k < HaloNetWorld.MaxBlocks)
                    record = HaloNetWire.MaxSlots + k;
                else if (name.StartsWith(HaloNetCoop.ActorPrefix, StringComparison.Ordinal) && int.TryParse(name.Substring(HaloNetCoop.ActorPrefix.Length), out k) && k >= 0 && k < HaloNetCoop.ActorBlocks)
                    record = HaloNetCoop.FirstActor + k;
                else if (name.StartsWith(HaloNetCoop.MissionPrefix, StringComparison.Ordinal) && int.TryParse(name.Substring(HaloNetCoop.MissionPrefix.Length), out k) && k >= 0 && k < HaloNetCoop.MissionBlocks)
                    record = HaloNetCoop.FirstMission + k;
                else if (name.StartsWith(HaloNetCoop.HostPrefix, StringComparison.Ordinal) && int.TryParse(name.Substring(HaloNetCoop.HostPrefix.Length), out k) && k >= 0 && k < HaloNetCoop.HostBlocks)
                    record = HaloNetCoop.FirstActor + HaloNetCoop.CoopBlocks + k;
                else continue;
                if (data.IntCount < HaloNetWire.Ints)
                {
                    HaloLog.Warn("Net: '" + name + "' holds " + data.IntCount + " ints, a record needs " + HaloNetWire.Ints + "; use a NetworkBridgeData64.");
                    continue;
                }

                _objects[record] = data;
                if (record < HaloNetWire.MaxSlots) { found++; highest = Math.Max(highest, record); }
                else worlds++;
            }

            int count = 0;
            while (count < HaloNetWire.MaxSlots && _objects[count] != null) count++;
            int wc = 0;
            while (wc < HaloNetWorld.MaxBlocks && _objects[HaloNetWire.MaxSlots + wc] != null) wc++;
            int cc = 0;
            while (cc < HaloNetCoop.Records && _objects[HaloNetCoop.FirstActor + cc] != null) cc++;
            _coopCount = cc;
            _slotCount = count;
            _worldCount = wc;
            _slotsComplete = count == HaloNetWire.MaxSlots && wc == HaloNetWorld.MaxBlocks && cc == HaloNetCoop.Records;
            if (found != _slotsLogged || worlds != _worldLogged)
            {
                _slotsLogged = found;
                _worldLogged = worlds;
                HaloLog.Info("Net: " + found + " player slot object(s) found ('" + HaloNetWire.ObjectPrefix + "0.." + highest + "'), " + count + " usable; " + wc + " world block(s), " + cc + " co-op block(s)"
                             + (_slotsComplete ? "." : "; still looking every 5 s."));
            }

            if (!_slotsComplete) _nextFind = now + 5.0;
        }

        private void RefreshPresent(bool room, double now)
        {
            if (!room)
            {
                if (_present.Count > 0) _present.Clear();
                _presentRead = false;
                return;
            }

            if (now < _presentAt) return;
            _presentAt = now + 1.0;
            _present.Clear();
            foreach (PlayerRef player in Runner.ActivePlayers) _present.Add(player.PlayerId);
            _presentRead = true;
        }

        /// <summary>"Fusion session #xxxxxxxx, region R, P of M player(s)" - NetworkRunner.SessionInfo read by reflection (not a proven member).</summary>
        public string Describe()
        {
            try
            {
                var ids = new System.Text.StringBuilder();
                for (int i = 0; i < _present.Count; i++)
                {
                    if (i > 0) ids.Append(", ");
                    ids.Append(_present[i]);
                }

                string session = "Fusion session not readable";
                object info = Prop(Runner, "SessionInfo");
                if (info != null)
                {
                    string name = Prop(info, "Name") as string;
                    object region = Prop(info, "Region"), count = Prop(info, "PlayerCount"), max = Prop(info, "MaxPlayers");
                    session = "Fusion session " + Fingerprint(name) + " (a fingerprint of its name: the same on two players' logs = the same instance), region "
                              + (region ?? "?") + ", " + (count ?? "?") + " of " + (max ?? "?") + " player(s)";
                }

                return session + "; players read " + (_presentRead ? "(ids " + ids + ")" : "not yet");
            }
            catch (Exception e)
            {
                return "Fusion session not readable (" + e.GetType().Name + ")";
            }
        }

        private static object Prop(object o, string name)
        {
            if (o == null) return null;
            try
            {
                PropertyInfo p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return p != null ? p.GetValue(o, null) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>FNV-1a (32 bit) of the text as "#xxxxxxxx" ("#none" for null / empty).</summary>
        public static string Fingerprint(string text)
        {
            if (string.IsNullOrEmpty(text)) return "#none";
            uint h = 2166136261u;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                h ^= (uint)(c & 0xFF);
                h *= 16777619u;
                h ^= (uint)(c >> 8);
                h *= 16777619u;
            }

            return "#" + h.ToString("x8");
        }

        private void Warn(string what, Exception e)
        {
            _errors++;
            if (_errors <= 3) HaloLog.Warn("Net: Fusion access failed (" + what + ", " + _errors + (_errors == 3 ? ", later ones not logged" : "") + "): " + e.Message);
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                try
                {
                    SceneNetworking.OnOtherPlayerJoined -= OnRoomPlayersChanged;
                    SceneNetworking.OnOtherPlayerLeft -= OnRoomPlayersChanged;
                }
                catch (Exception) { /* the scene's networking is gone */ }

                _subscribed = false;
            }
        }
    }
}

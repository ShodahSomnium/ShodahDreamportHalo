using System;
using System.IO;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Co-op host hand-over (user decision: the master leaves mid-mission -> the next player hosts and the mission goes on). The host's
    /// "mission" part of HaloNetHostState (order 10): game ticks, game speed, countdown timer, the active BSP, the checkpoint count / label,
    /// every placement's state (built / destroyed / not yet), device groups and device logic. On HaloNetHostState.Promoted this client becomes
    /// the host IN PLACE: Host = true, scripts / AI / device logic run from the next tick (S and I adopted their parts before), no reload, no
    /// teleport, no revert; a checkpoint "host hand-over" is taken at once so the new host has a revert point for its next death.
    /// </summary>
    public static partial class HaloCampaign
    {
        const int HandoverVersion = 1;
        static bool _handoverHooked;

        static void RegisterHandover()
        {
            try
            {
                HaloNetHostState.Register("mission", 10, CaptureHostState, AdoptHostState);
                if (!_handoverHooked) { HaloNetHostState.Promoted += OnPromoted; _handoverHooked = true; }
            }
            catch (Exception e) { HaloLog.Warn("Campaign: host state registration failed: " + e.Message); }
        }

        static void UnregisterHandover()
        {
            try
            {
                HaloNetHostState.Unregister("mission");
                if (_handoverHooked) { HaloNetHostState.Promoted -= OnPromoted; _handoverHooked = false; }
            }
            catch (Exception) { }
        }

        static byte[] CaptureHostState()
        {
            if (!Active || Data == null) return null;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(HandoverVersion);
            w.Write(Mission);
            w.Write(Ticks);
            w.Write(_speed);
            w.Write(_countdownMs); w.Write(_countdownRunning);
            w.Write(BspIndex);
            w.Write(HaloCheckpoints.Taken); w.Write(HaloCheckpoints.Label ?? "");
            int n = HaloObjects.PlacementCount;
            w.Write(n);
            for (int i = 0; i < n; i++) w.Write((byte)HaloObjects.StateOf(i));
            HaloDevices.Write(w);
            w.Flush();
            return ms.ToArray();
        }

        static void AdoptHostState(byte[] b)
        {
            if (!Active) return;
            if (b == null) { HaloLog.Warn("Campaign: host hand-over without a mission part - game time and devices go on from what this client saw"); return; }
            try
            {
                var r = new BinaryReader(new MemoryStream(b));
                if (r.ReadInt32() != HandoverVersion) { HaloLog.Warn("Campaign: host hand-over: unknown mission part version"); return; }
                string mission = r.ReadString();
                if (!string.Equals(mission, Mission, StringComparison.OrdinalIgnoreCase)) { HaloLog.Warn("Campaign: host hand-over part is for " + mission + ", not " + Mission); return; }
                Ticks = r.ReadInt32();
                _speed = r.ReadSingle();
                _countdownMs = r.ReadInt64(); _countdownRunning = r.ReadBoolean();
                int bsp = r.ReadInt32();
                int cps = r.ReadInt32(); string label = r.ReadString();
                int n = r.ReadInt32();
                int destroyed = 0;
                for (int i = 0; i < n; i++)
                {
                    var st = (HaloObjects.State)r.ReadByte();
                    // destroyed by the old host's scripts: never placed automatically here (objects that exist stay where they are)
                    if (st == HaloObjects.State.Destroyed && HaloObjects.StateOf(i) == HaloObjects.State.NotYet) { HaloObjects.SetState(i, HaloObjects.State.Destroyed); destroyed++; }
                }
                HaloDevices.Read(r);
                if (bsp != BspIndex) HaloLog.Warn("Campaign: host hand-over: the old host was on BSP " + bsp + ", this client on " + BspIndex + " (Net's BSP sync decides)");
                HaloLog.Info("Campaign: adopted the old host's mission state: game time " + (Ticks / 30f).ToString("0") + " s, speed " + _speed.ToString("0.##") + ", countdown "
                             + (_countdownMs / 1000) + " s" + (_countdownRunning ? " running" : "") + ", its checkpoint " + cps + " ('" + label + "'), " + destroyed + " placements marked destroyed, "
                             + HaloDevices.Count + " devices");
            }
            catch (Exception e) { HaloLog.Error("Campaign: adopting the host state failed: " + e); }
        }

        /// <summary>This client is the mission's host now: the host-only ticks start in place.</summary>
        static void OnPromoted()
        {
            if (!Active) return;
            if (Host) { HaloLog.Info("Campaign: promoted to host - already hosting"); return; }
            Host = true;
            HaloObjects.SkipBipeds = false;
            HaloAi.Enabled = true;
            HaloAi.Difficulty = Level;
            if (!HaloScript.Running) HaloLog.Warn("Campaign: promoted to host but the scripts are not running (the Script module's adopt did not start them)");
            _acc = 0f;
            HaloLog.Info("Campaign: promoted to host of " + Mission + " at " + (Ticks / 30f).ToString("0") + " s game time - scripts " + (HaloScript.Running ? "running" : "NOT running")
                         + ", AI on, devices and checkpoints here now (no reload, no teleport)");
            // the old host's checkpoint lived on its machine: the hand-over itself is this host's first revert point
            HaloCheckpoints.Take("host hand-over");
        }
    }
}

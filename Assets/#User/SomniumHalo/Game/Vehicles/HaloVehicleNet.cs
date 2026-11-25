using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The vehicles' side of the wire (the Net module owns the slots - Game/Net/HaloNetGame.World.cs / .Publish.cs). The rules:
    ///  - seats: every player's record names the vehicle and seat they sit in (Net reads HaloLocal.VehicleId / SeatIndex / Driving);
    ///    occupants come back through HaloNetGame.Players (VehicleIndex, Seat). A seat two players took at once is kept by the lower
    ///    slot: the higher one gets out again;
    ///  - the driver is authoritative: their client simulates the vehicle and publishes pose + velocity (PublishDriving) and the gun's
    ///    aim (PublishAim); every other client draws it kinematic from TryDriven (interpolated by Net, 0.16 s behind);
    ///  - an empty vehicle belongs to the instance master: it settles / respawns there and is published parked (PublishParked, only
    ///    changes go out); everyone else places it from TryParked (late joiners too);
    ///  - remote riders are drawn on each client's own drawn vehicle (HaloVehicleRiders), so a passenger never lags their seat;
    ///  - remote vehicle-gun shots: Net raises HaloEvents.Fired for them; V replays them on the vehicle (HaloVehicleGun.Replay).
    /// Alone / offline (HaloNetGame not up) this client owns every vehicle.
    /// </summary>
    public static class HaloVehicleNet
    {
        private static bool _saidMode, _subscribed;
        private static readonly Dictionary<int, bool> RemoteDrivenBefore = new Dictionary<int, bool>();

        /// <summary>The instance master owns the empty vehicles (true alone / offline).</summary>
        public static bool IsMaster => HaloNetGame.WorldOwner;      // (N: the world owner - the master, or the lowest player in the game while the master sits in the hub)

        /// <summary>Lab: stand-in riders added to the remote riders every frame.</summary>
        public static List<HaloVehicleRiders.Rider> LabRiders;

        private static bool Up => HaloNetGame.Up && HaloNetGame.Online;

        public static bool SeatFree(HaloVehicle v, int seat)
        {
            var s = v.SeatOf(seat);
            if (s == null || s.Occupant >= 0) return false;
            return !Up || HaloNetGame.Occupant(v.Id, seat) == null;
        }

        /// <summary>The local player sat down (v, seat) or got out (null, -1): Net reads HaloLocal's seat fields itself.</summary>
        public static void NoteSeat(HaloVehicle v, int seat) { }

        public static void NoteRespawn(HaloVehicle v) { }

        private static void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            HaloEvents.Fired += OnFired;
        }

        private static void OnFired(HaloTag weapon, Vector3 muzzle, Vector3 dir, int who)
        {
            if (who < 0 || who == HaloLocal.NetId || !HaloVehicles.IsVehicleWeapon(weapon)) return;
            HaloVehicles.ReplayShot(weapon, muzzle, dir, who);
        }

        /// <summary>Early in the frame: occupants, remote drivers' poses, parked poses from the master, the riders to draw.</summary>
        public static void Receive(List<HaloVehicle> all)
        {
            Subscribe();
            var riders = HaloVehicleRiders.Current;
            riders.Clear();
            if (LabRiders != null) riders.AddRange(LabRiders);
            var mine = HaloVehicleSeat.Local;
            int me = HaloLocal.NetId >= 0 ? HaloLocal.NetId : 0;
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (v == null) continue;
                for (int s = 0; s < v.Seats.Count; s++)
                    v.Seats[s].Occupant = mine.Seated && mine.Vehicle == v && mine.SeatIndex == s ? me : -1;
                v.NoteUnits();
            }
            if (!_saidMode)
            {
                _saidMode = true;
                HaloLog.Info("Vehicles: net " + (Up ? "online - drivers publish, the master parks the empty ones, riders are drawn on each client's vehicle" : "not up - this client owns every vehicle") + ".");
            }
            if (!Up) return;
            var players = HaloNetGame.Players;
            for (int k = 0; k < players.Count; k++)
            {
                var p = players[k];
                if (p == null || !p.InMatch || p.VehicleIndex < 0) continue;
                var v = HaloVehicles.Find(p.VehicleIndex);
                if (v == null) continue;
                var seat = v.SeatOf(p.Seat);
                if (seat == null) continue;
                if (seat.Occupant == me && mine.Vehicle == v && p.Slot < me)
                {
                    HaloLog.Info("Vehicles: " + p.Name + " took seat " + p.Seat + " of " + v.name + " at the same moment (lower slot keeps it); getting out.");
                    mine.GetOut("seat taken");
                }
                seat.Occupant = p.Slot;
                if (seat.Guns && v.Gun != null && p.Aim.sqrMagnitude > 0.5f) v.AimToward(p.Aim, seat, Time.deltaTime);
                var rp = PlayerOf(p.PlayerId);
                if (rp != null) riders.Add(new HaloVehicleRiders.Rider { PlayerId = p.PlayerId, Player = rp, Vehicle = v, Seat = p.Seat, Xr = p.Vr });
            }
            bool master = HaloNetGame.WorldOwner;      // (N: world owner)
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (v == null || v.LocalDriven && mine.Vehicle == v) continue;
                if (HaloNetGame.CoopVehicle(v.Id)) continue;      // (N, campaign co-op) a follower draws the host's AI / scripted vehicle from its vehicle record
                HaloNetVehicle nv;
                bool was;
                RemoteDrivenBefore.TryGetValue(v.Id, out was);
                if (HaloNetGame.TryDriven(v.Id, out nv))
                {
                    if (v.LocalDriven) v.Park(false);
                    v.Remote();
                    v.Place(nv.Position, nv.Rotation, nv.Velocity);
                    RemoteDrivenBefore[v.Id] = true;
                    continue;
                }
                if (was)
                {
                    // the driver got out (or left): the master lets it settle and parks it; the others wait for its parked pose
                    RemoteDrivenBefore[v.Id] = false;
                    if (master) v.Park(true); else v.Park(false);
                }
                if (!master && v.Empty && HaloNetGame.TryParked(v.Id, out nv)) v.Place(nv.Position, nv.Rotation, Vector3.zero);
            }
        }

        private static SomniumSpace.Bridge.Player.ISomniumPlayer PlayerOf(int playerId)
        {
            var list = HaloRemotes.All;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].Id == playerId) return list[i].Player;
            return null;
        }

        /// <summary>After the local simulation: the local driver's pose and aim, the master's parked vehicles.</summary>
        public static void Send(List<HaloVehicle> all)
        {
            if (!Up) return;
            var mine = HaloVehicleSeat.Local;
            if (mine.Seated && mine.Vehicle != null)
            {
                var v = mine.Vehicle;
                if (mine.Driver) HaloNetGame.PublishDriving(v.transform.position, v.transform.rotation, v.Velocity, 1f);
                if (mine.Seat.Guns) HaloNetGame.PublishAim(v.AimDirection);
            }
            if (!HaloNetGame.WorldOwner) return;      // (N: world owner)
            for (int i = 0; i < all.Count; i++)
            {
                var v = all[i];
                if (v == null) continue;
                if (v.RemoteDriven || v.LocalDriven) HaloNetGame.ClearParked(v.Id);
                else if (v.Empty) HaloNetGame.PublishParked(v.Id, v.transform.position, v.transform.rotation, 1f, 0);
            }
        }
    }
}

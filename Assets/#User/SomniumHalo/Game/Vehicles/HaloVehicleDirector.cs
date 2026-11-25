using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The vehicles' frame, early (order -90: before the client's order-0 scripts read the body root - the seat is written before the
    /// client places the avatar, so a monitor avatar does not trail the seat by a frame): net poses in, the seat's controls, the gun,
    /// the body on the seat, getting in / out, projectiles, abandoned-vehicle respawn, riders. Lives on "Halo Vehicles" under the
    /// match's world root (HaloVehicles.Spawn adds it).
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class HaloVehicleDirector : MonoBehaviour
    {
        private float _nextRespawnCheck;
        private float _tabletUntil;
        private float _hatchSaid;
        private bool _saidTablet;

        /// <summary>The client's tablet buttons (B / Y - B also gets out, Y switches the view) are held off now (the lab reads it).</summary>
        public static bool TabletHeld { get; private set; }

        /// <summary>
        /// Headset: the Somnium tablet opens on a tap of B or Y - the buttons that get out of the vehicle and switch its view. While seated and
        /// for 1 s after getting in or out (the press that got out is released after the seat is gone), the client's tablet actions are
        /// switched off through Combat's HaloTabletGuard (which keeps them off 0.8 s past the last call and switches back only what it switched off).
        /// </summary>
        private void Tablet(bool seated)
        {
            bool vr = HaloPlayer.XrPresent || HaloVehicleInput.LabVr;
            if (seated) _tabletUntil = Time.time + 1f;
            TabletHeld = vr && (seated || Time.time < _tabletUntil);
            if (!TabletHeld) return;
            HaloTabletGuard.Update(true);
            if (!_saidTablet) { _saidTablet = true; HaloLog.Info("Vehicles: the Somnium tablet buttons (B / Y) are held off while seated and 1 s around getting in / out."); }
        }

        private void Update()
        {
            try { Early(); }
            catch (Exception e) { HaloLog.Error("Vehicles: frame failed: " + e); }
        }

        private void Early()
        {
            var all = HaloVehicles.All;
            float dt = Time.deltaTime;
            HaloVehicleNet.Receive(all);
            var seat = HaloVehicleSeat.Local;
            if (seat.Seated)
            {
                var v = seat.Vehicle;
                if (v == null || !HaloLocal.Alive) { HaloVehicles.ExitSeat(); }
                else
                {
                    // cutscenes hold the player's game actions (drive, fire, get out): Halo plays them with the controls off
                    var intent = HaloCinematic.Playing ? default(HaloVehicleInput.Intent) : HaloVehicleInput.Read(v, seat.Seat);
                    if (seat.Driver) v.Controls = intent.Drive;
                    if (seat.Seat.Guns && v.Gun != null)
                    {
                        if (HaloVehicleInput.FixedGuns(v)) { v.AimYaw = 0f; v.AimPitch = 0f; }      // ghost / banshee: the bolts go where the hull points
                        else if (intent.GunAim.sqrMagnitude > 0.01f) v.AimToward(intent.GunAim, seat.Seat, dt);
                        v.Gun.Tick(intent.Fire, intent.Fire2, dt, seat.Seat.Label);
                    }
                    if (intent.ToggleView) seat.ToggleView();
                    if (intent.Exit && v.Campaign && v.HasHatch && !v.IsOpen) { if (Time.time > _hatchSaid) { _hatchSaid = Time.time + 2f; HaloLog.Info("Vehicles: " + v.name + " is closed - getting out waits for unit_open."); } }
                    else if (intent.Exit) seat.GetOut(HaloVehicleSeat.Vr ? "B" : "E");
                    else seat.Place(false);
                }
            }
            else if (HaloSession.Playing || HaloVehicles.LabMode)
            {
                if (HaloCinematic.Playing) HaloVehicleEntry.Clear(); else HaloVehicleEntry.Tick(all);
            }
            for (int k = 0; k < all.Count; k++) if (all[k] != null) all[k].AiStep(dt);
            Tablet(seat.Seated);
            HaloVehicleShots.Tick(dt);
            HaloVehicleRiders.Early();
            if (Time.time >= _nextRespawnCheck) { _nextRespawnCheck = Time.time + 1f; HaloVehicles.RespawnAbandoned(); }
            HaloVehicleNet.Send(all);
        }
    }

    /// <summary>The vehicles' late frame (order 10000, after Fusion's Render and the client's camera rig): the seat again, the monitor
    /// camera, the riders again.</summary>
    [DefaultExecutionOrder(10000)]
    public sealed class HaloVehicleLate : MonoBehaviour
    {
        private void LateUpdate()
        {
            try
            {
                var seat = HaloVehicleSeat.Local;
                if (seat.Seated && seat.Vehicle != null)
                {
                    seat.Place(true);
                    if (!HaloPlayer.XrPresent) HaloVehicleCamera.Place(seat.Vehicle, seat.Seat, seat.ThirdPerson, seat.EyePoint());
                }
                HaloVehicleRiders.Late();
            }
            catch (Exception e) { HaloLog.Error("Vehicles: late frame failed: " + e); }
        }
    }
}

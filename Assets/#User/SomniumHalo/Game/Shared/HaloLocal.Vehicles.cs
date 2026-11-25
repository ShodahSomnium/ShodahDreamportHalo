using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Vehicles module] The local player's vehicle state beyond the lead's Vehicle / Seat / Driving fields (HaloLocal.cs), kept
    /// current every frame by Game/Vehicles. The HUD reads it for the vehicle HUD (unit hud interface of the seat, the seat's weapon
    /// HUD and heat), the Net module for the player's record, Combat to holster while seated without "allows weapons".
    /// </summary>
    public static partial class HaloLocal
    {
        /// <summary>The vehi tag of the vehicle we sit in (null on foot).</summary>
        public static HaloTag VehicleTag;
        /// <summary>The vehicle's id in this match (its scenario placement index; -1 on foot).</summary>
        public static int VehicleId = -1;
        /// <summary>Index of our seat in the vehicle tag's seat block (-1 on foot).</summary>
        public static int SeatIndex = -1;
        /// <summary>We man the vehicle's gun (driver of a ghost / banshee / scorpion / turret, gunner of a warthog).</summary>
        public static bool Gunning;
        /// <summary>The seat's own weapon tag (the vehicle's weapon when <see cref="Gunning"/>, else null).</summary>
        public static HaloTag VehicleWeapon;
        /// <summary>The seat's HUD (unit hud interface, unhi) and the vehicle weapon's HUD (wphi), null when the tag has none.</summary>
        public static HaloTag SeatHud, VehicleWeaponHud;
        /// <summary>Vehicle weapon heat 0..1 (gun turret), and whether it is overheated.</summary>
        public static float VehicleHeat;
        public static bool VehicleOverheated;
        /// <summary>Speed of the vehicle we sit in, metres per second (0 on foot).</summary>
        public static float VehicleSpeed;
        /// <summary>The seat allows our own weapons (warthog passenger): Combat keeps the gun in hand.</summary>
        public static bool SeatAllowsWeapons;
        /// <summary>The vehicle we could get into right now ("Press E to drive the Warthog"), null when none is in reach.</summary>
        public static GameObject VehicleInReach;
        public static string VehicleInReachSeat = "";
        /// <summary>The third-person vehicle camera is on (monitor) - the Desk module leaves the camera alone while this is true.</summary>
        public static bool VehicleCamera;
    }
}

using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The local player's game state that several modules read: health / shields (Combat writes), weapons and grenades (Combat),
    /// the vehicle seat (Vehicles writes), team and score (Match / Net write). The HUD and the Net record read it every frame.
    /// Owners extend it with their own partial files (HaloLocal.Combat.cs, HaloLocal.Vehicles.cs, ...) - never change another
    /// owner's fields. Plain fields (no events) so it costs nothing per frame.
    /// </summary>
    public static partial class HaloLocal
    {
        // ---- Match / Net
        public static int NetId = -1;            // our net player id (slot), -1 offline
        public static int Team = HaloTeams.None;
        public static int Kills, Deaths, Assists, Score;
        public static string Nick = "";

        // ---- Combat
        public static bool Alive;
        /// <summary>0..1 of max (Halo: 1 = full; overshield takes Shield up to 3).</summary>
        public static float Health = 1f, Shield = 1f;
        public static bool ShieldRecharging, ShieldLow;
        public static float CamoUntil;           // Time.time the active camouflage ends (0 = none)
        /// <summary>Respawn countdown (seconds left, 0 when alive).</summary>
        public static float RespawnIn;

        /// <summary>One carried weapon (Halo: at most two plus grenades).</summary>
        public sealed class Weapon
        {
            public HaloTag Tag;                  // weap tag in the current map
            public int Loaded, Reserve;          // rounds in the magazine / carried (ballistic weapons)
            public float Charge = 1f;            // battery left 0..1 (plasma weapons; Halo's "age")
            public float Heat;                   // 0..1, overheated at 1
            public bool Overheated;
            public string Label;                 // HUD / pickup name ("Assault Rifle")
        }

        public static readonly Weapon[] Weapons = new Weapon[2];
        public static int Held;                  // index into Weapons of the gun in hand (-1 = none / in a vehicle seat without weapon)
        public static int FragGrenades, PlasmaGrenades;
        public static int GrenadeSelected;       // 0 frag, 1 plasma
        public static bool Zoomed; public static int ZoomLevel;

        // ---- Vehicles
        /// <summary>The vehicle we sit in (null = on foot) and the seat label ("W-driver", "W-gunner", ...).</summary>
        public static GameObject Vehicle;
        public static string Seat = "";
        public static bool Driving;

        // ---- where we are (written each frame by whoever moves the player: Vr / Desk / Vehicles)
        public static Vector3 Position, Forward = Vector3.forward;

        public static Weapon HeldWeapon => Held >= 0 && Held < Weapons.Length ? Weapons[Held] : null;

        /// <summary>Back to a fresh life (called by Match on spawn; Combat fills weapons from the starting equipment).</summary>
        public static void ResetLife()
        {
            Alive = true; Health = 1f; Shield = 1f; ShieldRecharging = false; ShieldLow = false; CamoUntil = 0f; RespawnIn = 0f;
            Weapons[0] = null; Weapons[1] = null; Held = 0; FragGrenades = 0; PlasmaGrenades = 0; GrenadeSelected = 0;
            Zoomed = false; ZoomLevel = 0; Vehicle = null; Seat = ""; Driving = false;
        }
    }
}

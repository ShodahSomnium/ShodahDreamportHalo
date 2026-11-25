using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Combat module] The local player's fighting state beyond the lead's fields (HaloLocal.cs: Alive, Health, Shield, Weapons, Held,
    /// FragGrenades, PlasmaGrenades, GrenadeSelected, Zoomed, ZoomLevel, CamoUntil). Written every frame by Game/Combat; the HUD and the
    /// Net record read it. Plain fields, no events.
    /// </summary>
    public static partial class HaloLocal
    {
        /// <summary>A weapon is in a hand right now (VR: gripped; monitor: always when carrying one). <see cref="Held"/> is the gun in hand,
        /// or the last one drawn while both are holstered (so the HUD keeps showing its ammo).</summary>
        public static bool InHand;
        /// <summary>The shields are above 1 (an over shield: up to 3).</summary>
        public static bool OverShield;
        /// <summary>Active camouflage is on (until <see cref="CamoUntil"/>) and how visible the player is 0 (invisible) .. 1 (firing / hit shows).</summary>
        public static bool Camo;
        public static float CamoVisibility = 1f;
        /// <summary>The held weapon is being reloaded (magazine out, a shell going in, the monitor's timed reload) and how far 0..1 (monitor).</summary>
        public static bool Reloading;
        public static float ReloadProgress;
        /// <summary>The plasma pistol's charge 0..1 (charging trigger held).</summary>
        public static float Charge;
        /// <summary>Zoom magnification of the held weapon's current zoom level (1 = none); the monitor camera divides its field of view by it.</summary>
        public static float ZoomMagnification = 1f;
        /// <summary>Grenade limits of the map (globals: 4 / 4 in multiplayer).</summary>
        public static int FragMax = 4, PlasmaMax = 4;
        /// <summary>Last time (Time.time) this player's shot or swing hurt someone, and whether it killed (for a hit marker if the HUD wants one).</summary>
        public static float HitDealtAt = -10f;
        public static bool HitDealtKilled;
        /// <summary>Last time (Time.time) we took damage and where it came from (world point; the HUD's damage arrows).</summary>
        public static float DamagedAt = -10f;
        public static Vector3 DamageFrom;
        /// <summary>The pickup prompt for what is in reach ("Hold [grip] to pick up Shotgun"), empty when nothing; HaloEvents.Message is raised too.</summary>
        public static string PickupPrompt = "";
        /// <summary>Carrying the game type's objective as the held weapon (flag, oddball) through HaloCombat.CarryObjective.</summary>
        public static HaloTag Objective;
        /// <summary>The flashlight is on; night vision is on (the sniper scope in the dark: the HUD / screen effect draws it).</summary>
        public static bool Flashlight, NightVision;
    }
}

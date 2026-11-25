using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Optional extra facts a hit box (Net's remote players) or a vehicle body can give the shooter's code. Implement it next to
    /// IHaloDamageable on the same component; without it Combat guesses: the slot is unknown (-1), a collider whose name contains "head"
    /// is the head, the facing is the component's transform.forward, the surface is the cyborg's energy shield (players) / thick metal
    /// (anything else), and the target is alive.
    /// </summary>
    public interface IHaloHitInfo
    {
        /// <summary>Net player id (slot) of the target, -1 for none (vehicles, dummies).</summary>
        int HitSlot { get; }
        /// <summary>This collider is the head (headshots).</summary>
        bool HitIsHead(Collider c);
        /// <summary>Where the target faces (melee from behind kills instantly).</summary>
        Vector3 HitFacing { get; }
        /// <summary>Halo material type at the point (21 cyborg armor, 22 cyborg energy shield while the shields are up, 7 metal thick ...).</summary>
        int HitMaterial(Vector3 point);
        /// <summary>False when dead (no hits, no impact effects of flesh).</summary>
        bool HitAlive { get; }
    }
}

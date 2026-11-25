using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// One hit box of a remote player (a capsule or sphere on layer <see cref="HaloLayers.Hitbox"/>, following their avatar's bones or the body their record
    /// describes). The Combat module measures a shot / blast / melee against it and calls <see cref="TakeHit"/> with the damage it decided (shooter
    /// authoritative, as in the Black Ops 2 Dreamport): the hit is published for the victim's own client, which applies it once. Nothing is applied here.
    /// <see cref="IHaloHitInfo"/> tells the shooter's code the slot, the head, the facing (melee from behind), the surface (cyborg energy shield while
    /// their shields are up, else cyborg armour) and whether they are alive.
    /// </summary>
    public sealed class HaloNetHitbox : MonoBehaviour, IHaloDamageable, IHaloHitInfo
    {
        public const int Head = 0, Torso = 1, Arm = 2, Leg = 3;

        /// <summary>The player this box belongs to.</summary>
        public HaloNetPlayer Player;
        /// <summary>0 head, 1 torso, 2 arm, 3 leg.</summary>
        public int Region;

        public bool IsHead { get { return Region == Head; } }

        public void TakeHit(HaloHit hit)
        {
            HaloNetGame.PublishHit(this, hit);
        }

        public int HitSlot { get { return Player != null ? Player.Slot : -1; } }

        public bool HitIsHead(Collider c)
        {
            if (c == null) return IsHead;
            HaloNetHitbox hb = c.GetComponent<HaloNetHitbox>();
            return hb != null ? hb.IsHead : IsHead;
        }

        public Vector3 HitFacing { get { return Player != null ? Player.Forward : transform.forward; } }

        public int HitMaterial(Vector3 point)
        {
            return Player != null && Player.Shield > 0.01f ? HaloNetReplay.CyborgShield : HaloNetReplay.CyborgArmor;
        }

        public bool HitAlive { get { return Player != null && Player.Alive; } }
    }
}

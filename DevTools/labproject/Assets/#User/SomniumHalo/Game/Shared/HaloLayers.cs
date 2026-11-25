namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Physics layers, matching the Somnium client's collision matrix (measured for the Euro Truck Simulator Dreamport; client and SDK
    /// matrices are byte-identical). The avatar's CharacterController is on layer 2 (Ignore Raycast) and collides with 0,1,2,3,7,11-14,
    /// 16,21-24,28,29,31; desktop clicks reach layers 0,1,4,5,14,16; the VR pointer reaches 0,5,6,10,14,16,23.
    /// </summary>
    public static class HaloLayers
    {
        /// <summary>Walkable map geometry (BSP collision), scenery the player stands on.</summary>
        public const int World = 0;
        /// <summary>The avatar's own capsule (client).</summary>
        public const int Avatar = 2;
        /// <summary>Clickable / grabbable world objects (weapons on the floor, vehicle doors) - reached by desktop clicks and the VR pointer.</summary>
        public const int Interactable = 16;
        /// <summary>Props and scenery that block players but are not floor-like.</summary>
        public const int Environment = 23;
        /// <summary>Hit boxes of players (own and remote): bullets only, never the avatar's capsule.</summary>
        public const int Hitbox = 24;
        /// <summary>Vehicle bodies (collide with world, environment, avatars and vehicles).</summary>
        public const int Vehicle = 28;

        /// <summary>What a bullet / projectile raycast hits: world, environment, hit boxes, vehicles, interactables.</summary>
        public const int ShotMask = (1 << World) | (1 << Environment) | (1 << Hitbox) | (1 << Vehicle);
        /// <summary>Ground for placing things (items, vehicles, footstep probes).</summary>
        public const int GroundMask = (1 << World) | (1 << Environment) | (1 << 14);
    }
}

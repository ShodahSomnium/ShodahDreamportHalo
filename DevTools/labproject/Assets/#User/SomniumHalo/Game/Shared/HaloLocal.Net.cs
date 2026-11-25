namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Net module] The local player's net state (written by Game/Net every frame; <see cref="NetId"/> in HaloLocal.cs is Net's too).</summary>
    public static partial class HaloLocal
    {
        /// <summary>In a Fusion room with the slot objects (other Dreamport players can be seen).</summary>
        public static bool NetOnline;
        /// <summary>I am the instance master (true alone / offline).</summary>
        public static bool NetMaster = true;
    }
}

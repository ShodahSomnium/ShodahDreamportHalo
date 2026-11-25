namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The net layer's hooks (Game/Net/HaloNetGame): one session for the whole visit, started with the Dreamport (a whitelisted player only - a visitor never
    /// claims a slot), ticked every frame before the Front and the Match, stopped when the world closes (the own slot is written free).
    /// </summary>
    public sealed partial class HaloGame
    {
        partial void NetStart()
        {
            HaloNetGame.Start(this);
            AddOnce<HaloNetDriver>();
        }

        partial void NetTick()
        {
            HaloNetGame.Tick();
        }

        partial void NetStop()
        {
            HaloNetGame.Stop();
        }
    }
}

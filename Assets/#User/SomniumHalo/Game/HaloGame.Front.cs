namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] The front end's hooks: HaloGame calls them only after the whitelist gate let the player in (StartDreamport), so a visitor
    /// never gets the ring, the menu, the tablet app or ui.map read. Everything is in Game/Front (HaloFront is the conductor).
    /// </summary>
    public sealed partial class HaloGame
    {
        partial void FrontStart() { HaloFront.Start(this); }
        partial void FrontBootDone() { HaloFront.BootDone(); }
        partial void FrontTick() { HaloFront.Tick(); }
        partial void FrontStop() { HaloFront.Stop(); }
    }
}

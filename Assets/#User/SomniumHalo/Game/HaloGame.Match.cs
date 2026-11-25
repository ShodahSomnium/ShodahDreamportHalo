namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>The Match module's hooks: the match loop runs every frame (Game/Match/HaloMatchControl), and goes with the scene.</summary>
    public sealed partial class HaloGame
    {
        partial void MatchTick() { HaloMatchControl.Tick(); }
        partial void MatchStop() { HaloMatchControl.Stop(); }
    }
}

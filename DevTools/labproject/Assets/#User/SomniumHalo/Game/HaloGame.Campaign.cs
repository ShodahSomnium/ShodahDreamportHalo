namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>The Mission module's hooks: the campaign's frame (Game/Campaign/HaloCampaign: the 30 Hz game tick for scripts, AI, devices,
    /// trigger volumes; checkpoints, death and revert) runs every frame, and the mission goes with the scene.</summary>
    public sealed partial class HaloGame
    {
        partial void CampaignTick() { HaloCampaign.Tick(); HaloCampaignDriver.Ensure(); }
        partial void CampaignStop() { HaloCampaign.Stop(); }
    }
}

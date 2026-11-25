namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>Version stamp of the Dreamport: bump Iteration / Date / Summary on every upload and add a row to README "Iterations".</summary>
    public static class HaloVersion
    {
        public const int Iteration = 14;
        public const string Date = "2026-11-04";
        public const string Summary = "Halo-accurate AI: NPCs no longer fall through floors, dropships fly their recordings to their landing zones, Flood carriers burst, perception/melee/grenades/positions from the tags";

        public static string Label
        {
            get { return "Halo Dreamport - iteration " + Iteration + " (" + Date + ")"; }
        }
    }
}

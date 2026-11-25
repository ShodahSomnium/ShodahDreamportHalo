namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>Version stamp of the Dreamport: bump Iteration / Date / Summary on every upload and add a row to README "Iterations".</summary>
    public static class HaloVersion
    {
        public const int Iteration = 15;
        public const string Date = "2026-11-10";
        public const string Summary = "Plasma bolts visible (light volumes + lens flares), Halo-tick particle physics fixes the muzzle flash, seated guns in the hands, gunner pose";

        public static string Label
        {
            get { return "Halo Dreamport - iteration " + Iteration + " (" + Date + ")"; }
        }
    }
}

namespace SomniumSpace.Worlds.SomniumHalo
{
    public static class HaloScript
    {
        public static bool Running { get; private set; }
        public static void Start(HaloMap map, HaloScenario scenario) { Running = true; }
        public static void Stop() { Running = false; }
        public static void Tick() { }
    }
}

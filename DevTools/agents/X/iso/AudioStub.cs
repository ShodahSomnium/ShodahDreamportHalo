using UnityEngine;
namespace SomniumSpace.Worlds.SomniumHalo
{
    public static class HaloAudio
    {
        public sealed class Handle { public bool Alive; }
        public static void Play(HaloTag sound, Vector3 position, float gain = 1f) { }
        public static void Play(HaloTag sound, Transform follow, float gain = 1f) { }
        public static Handle Loop(HaloTag sound, Transform at, float gain = 1f) { return null; }
        public static void Stop(Handle h) { }
    }
}

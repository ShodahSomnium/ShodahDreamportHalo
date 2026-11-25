using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The multiplayer announcer and game-event sounds: the globals tag's (matg) "multiplayer information" block, its "sounds" list,
    /// indexed by Halo's MultiplayerInformationSound enum (invader-defs/globals.json). Unity-free.
    /// </summary>
    public static class HaloMpSounds
    {
        /// <summary>The enum, in order (index = the slot in the globals' list).</summary>
        public static readonly string[] Names =
        {
            "play ball", "game over", "one minute to win", "30 seconds to win", "red team minute to win", "red team 30 to win",
            "blue team minute to win", "blue team 30 to win", "blue team has the flag", "blue team flag returned", "blue team score",
            "red team has the flag", "red team flag returned", "red team score", "double kill", "triple kill", "killtacular",
            "running riot", "killing spree", "oddball", "race", "slayer", "capture the flag", "warthog", "ghost", "scorpion",
            "countdown timer", "teleporter activate", "flag failure", "countdown for respawn", "hill move", "player respawn",
            "team king of the hill", "team oddball", "team race", "team slayer", "king of the hill", "blue team ctf", "red team ctf",
            "hill contested", "hill controlled", "hill occupied", "countdown timer end", "ting",
        };

        /// <summary>The enum index of a key: "double kill", "Double_Kill", "doublekill" all match; also a plain number. -1 = unknown.</summary>
        public static int Index(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;
            int n;
            if (int.TryParse(key, out n)) return n >= 0 && n < Names.Length ? n : -1;
            string k = Squash(key);
            for (int i = 0; i < Names.Length; i++) if (Squash(Names[i]) == k) return i;
            return -1;
        }

        static string Squash(string s)
        {
            var c = new char[s.Length]; int n = 0;
            foreach (char ch in s) if (char.IsLetterOrDigit(ch)) c[n++] = char.ToLowerInvariant(ch);
            return new string(c, 0, n);
        }

        /// <summary>The sound tags of the map's globals multiplayer information, by enum index (null entries where missing).</summary>
        public static HaloTag[] Read(HaloMap map)
        {
            HaloTag g = null;
            foreach (var t in map.OfClass("matg")) { g = t; break; }
            if (g == null) return new HaloTag[0];
            var r = g.Root;
            if (r.Count(356) <= 0) return new HaloTag[0];
            var mp = r.Element(356, 0, 160);
            int n = mp.Count(92);
            if (n <= 0 || n > 200) return new HaloTag[0];
            var o = new HaloTag[n];
            int i = 0;
            foreach (var e in mp.Block(92, 16)) o[i++] = e.Ref(0);
            return o;
        }
    }
}

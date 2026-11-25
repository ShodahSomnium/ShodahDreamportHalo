using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The multiplayer announcer and game-flow sounds of the map's globals (matg multiplayer information "sounds": "slayer", "double kill",
    /// "red team has the flag", "hill moved", the respawn countdown beeps, the teleporter...), found by their file names so the order of the
    /// block does not matter. Played through the Audio module (HaloAudio.Play2D / Play).
    /// </summary>
    public static class HaloAnnouncer
    {
        static readonly Dictionary<string, HaloTag> Sounds = new Dictionary<string, HaloTag>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The globals' multiplayer flag and ball weapons (CTF flag, oddball), read with the sounds.</summary>
        public static HaloTag FlagWeapon { get; private set; }
        public static HaloTag BallWeapon { get; private set; }
        public static HaloTag HillShader { get; private set; }

        public static void Load(HaloMap map)
        {
            Sounds.Clear();
            FlagWeapon = null; BallWeapon = null; HillShader = null;

            if (map == null) return;
            HaloTag globals = null;
            foreach (HaloTag t in map.OfClass("matg")) { globals = t; break; }
            if (globals == null) { HaloLog.Warn("Match: the map has no globals tag - no announcer, flag or ball"); return; }
            foreach (TagReader mp in globals.Root.Block(356, 160))
            {
                FlagWeapon = mp.Ref(0);
                BallWeapon = mp.Ref(76);
                HillShader = mp.Ref(44);
                foreach (TagReader s in mp.Block(92, 16))
                {
                    HaloTag snd = s.Ref(0);
                    if (snd == null) continue;
                    string n = System.IO.Path.GetFileName(snd.Name);
                    if (!Sounds.ContainsKey(n)) Sounds[n] = snd;
                }
                break;
            }
            HaloLog.Info("Match: globals multiplayer information: flag " + Name(FlagWeapon) + ", ball " + Name(BallWeapon) + ", " + Sounds.Count + " announcer / game sounds");
        }

        public static void Clear() { Sounds.Clear(); FlagWeapon = null; BallWeapon = null; HillShader = null; }

        static string Name(HaloTag t) { return t != null ? t.Name : "none"; }

        public static HaloTag Sound(string name)
        {
            HaloTag t;
            return Sounds.TryGetValue(name, out t) ? t : null;
        }

        /// <summary>A head-relative announcer line / UI sound by file name ("double_kill", "game_over", "countdown_for_respawn").</summary>
        public static void Say(string name)
        {
            HaloTag t = Sound(name);
            if (t == null) return;
            try { HaloAudio.Play2D(t); }
            catch (Exception e) { HaloLog.Warn("Match: announcer " + name + ": " + e.Message); }
        }

        /// <summary>A game sound at a point (the teleporter).</summary>
        public static void At(string name, Vector3 where)
        {
            HaloTag t = Sound(name);
            if (t == null) return;
            try { HaloAudio.Play(t, where); }
            catch (Exception e) { HaloLog.Warn("Match: sound " + name + ": " + e.Message); }
        }

        /// <summary>"Slayer", "Team Slayer", "Red team, capture the flag", ... at the start of a game.</summary>
        public static void GameType(HaloRules r, int team)
        {
            switch (r.Type)
            {
                case HaloScenario.GameType.Ctf: Say(team == HaloTeams.Blue ? "blue_team_ctf" : "red_team_ctf"); break;
                case HaloScenario.GameType.King: Say(r.Teams ? "team_king_of_the_hill" : "king_of_the_hill"); break;
                case HaloScenario.GameType.Oddball: Say(r.Teams ? "team_oddball" : "oddball"); break;
                case HaloScenario.GameType.Race: Say(r.Teams ? "team_race" : "race"); break;
                default: Say(r.Teams ? "team_slayer" : "slayer"); break;
            }
        }

        /// <summary>"Red team" / "blue team" prefix of the team announcer files.</summary>
        public static string TeamWord(int team) { return team == HaloTeams.Blue ? "blue" : "red"; }
    }
}

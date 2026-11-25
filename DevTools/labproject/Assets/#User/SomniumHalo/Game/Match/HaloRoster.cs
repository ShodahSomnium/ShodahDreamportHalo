using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>One player of the match as the scoreboard, the spawn weighting and the objectives see them.</summary>
    public sealed class HaloMatchPlayer
    {
        /// <summary>Net player id (slot); the local player offline is -1.</summary>
        public int Id;
        public string Name = "";
        public bool Local;
        public int Team = HaloTeams.None;
        public bool Alive;
        public Vector3 Position, Forward = Vector3.forward;
        public int Kills, Deaths, Assists;
        /// <summary>The game type's score: slayer kills - suicides - betrayals, CTF captures, KOTH / oddball seconds, race laps.</summary>
        public int Score;
        /// <summary>HaloObjectives.Carry* code (flag / ball carried).</summary>
        public int Carry;
        /// <summary>Halo's FFA armour colour (team colour in team games).</summary>
        public Color Color = Color.white;
        /// <summary>Time.time of the last update (remote: the net layer's last record).</summary>
        public float Seen;
        public override string ToString() { return Name + " (" + Id + ")"; }
    }

    /// <summary>
    /// Everybody in the current match: the local player (kept by the Match module from HaloLocal every frame) and the remote players
    /// (filled by the Net module from their records: <see cref="Remote"/> each tick, <see cref="Remove"/> when a slot goes).
    /// Read by Match (spawns, team scores, objectives) and the HUD (scoreboard, motion tracker). Main thread only.
    /// </summary>
    public static class HaloRoster
    {
        public static readonly List<HaloMatchPlayer> Players = new List<HaloMatchPlayer>();
        public static readonly HaloMatchPlayer Me = new HaloMatchPlayer { Local = true, Id = -1 };

        /// <summary>Halo's 18 multiplayer armour colours (white, black, red, blue, gray, yellow, green, pink, purple, cyan, cobalt, orange,
        /// teal, sage, brown, tan, maroon, salmon). RGB values are ours (the game takes them from the cyborg's change colours).</summary>
        public static readonly Color[] ArmorColors =
        {
            new Color(0.95f, 0.95f, 0.95f), new Color(0.12f, 0.12f, 0.12f), new Color(0.80f, 0.12f, 0.10f), new Color(0.15f, 0.25f, 0.85f),
            new Color(0.50f, 0.50f, 0.50f), new Color(0.95f, 0.85f, 0.15f), new Color(0.15f, 0.70f, 0.15f), new Color(1.00f, 0.55f, 0.75f),
            new Color(0.50f, 0.15f, 0.65f), new Color(0.30f, 0.90f, 0.95f), new Color(0.25f, 0.35f, 0.60f), new Color(1.00f, 0.50f, 0.05f),
            new Color(0.15f, 0.60f, 0.55f), new Color(0.55f, 0.65f, 0.45f), new Color(0.45f, 0.30f, 0.15f), new Color(0.80f, 0.70f, 0.50f),
            new Color(0.45f, 0.05f, 0.10f), new Color(0.95f, 0.55f, 0.45f),
        };

        static HaloRoster() { Players.Add(Me); }

        /// <summary>The colour a player is drawn in: the team's in team games, an armour colour by id in free for all.</summary>
        public static Color ColorFor(int id, int team, bool teams)
        {
            if (teams && team >= 0) return HaloTeams.Of(team);
            int i = id < 0 ? 2 : id % ArmorColors.Length;
            return ArmorColors[i];
        }

        /// <summary>The remote player with this id (created when new). The Net module fills its fields.</summary>
        public static HaloMatchPlayer Remote(int id)
        {
            for (int i = 0; i < Players.Count; i++) if (!Players[i].Local && Players[i].Id == id) return Players[i];
            var p = new HaloMatchPlayer { Id = id, Seen = Time.time };
            Players.Add(p);
            return p;
        }

        public static HaloMatchPlayer Find(int id)
        {
            if (id == Me.Id) return Me;
            for (int i = 0; i < Players.Count; i++) if (Players[i].Id == id) return Players[i];
            return null;
        }

        public static void Remove(int id)
        {
            for (int i = Players.Count - 1; i >= 0; i--) if (!Players[i].Local && Players[i].Id == id) Players.RemoveAt(i);
        }

        /// <summary>Remote players only (everyone but the local entry).</summary>
        public static int RemoteCount { get { return Players.Count - 1; } }

        /// <summary>Drops every remote entry (leaving the match).</summary>
        public static void ClearRemotes()
        {
            for (int i = Players.Count - 1; i >= 0; i--) if (!Players[i].Local) Players.RemoveAt(i);
        }

        /// <summary>Counts the players on a team (the local one included).</summary>
        public static int CountTeam(int team)
        {
            int n = 0;
            for (int i = 0; i < Players.Count; i++) if (Players[i].Team == team) n++;
            return n;
        }

        /// <summary>Copies the local player's state from HaloLocal (Match does it every frame).</summary>
        public static void SyncLocal(bool teams)
        {
            Me.Id = HaloLocal.NetId;
            Me.Name = string.IsNullOrEmpty(HaloLocal.Nick) ? "You" : HaloLocal.Nick;
            Me.Team = HaloLocal.Team;
            Me.Alive = HaloLocal.Alive;
            Me.Position = HaloLocal.Position;
            Me.Forward = HaloLocal.Forward;
            Me.Kills = HaloLocal.Kills; Me.Deaths = HaloLocal.Deaths; Me.Assists = HaloLocal.Assists; Me.Score = HaloLocal.Score;
            Me.Carry = HaloLocal.Carry;
            Me.Color = ColorFor(Me.Id, Me.Team, teams);
            Me.Seen = Time.time;
        }
    }
}

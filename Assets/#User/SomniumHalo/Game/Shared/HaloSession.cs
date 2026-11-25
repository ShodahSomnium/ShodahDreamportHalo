using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The shared state of the current game session: which phase we are in (hub, loading, playing), the loaded map and scenario,
    /// the world root, the rules. Owned by the Match module (Game/Match); everyone else READS it and subscribes to the events.
    /// All events are raised on the main thread; every handler is called on its own, so one that throws is logged and the rest still run.
    /// </summary>
    public static class HaloSession
    {
        public enum Phase { Hub, Loading, Playing, Ending }

        public static Phase Current { get; private set; } = Phase.Hub;
        public static HaloInstall.MapEntry MapEntry { get; private set; }
        public static HaloMap Map { get; private set; }
        public static HaloScenario Scenario { get; private set; }
        /// <summary>Everything built for the match hangs under this (destroyed when the match ends).</summary>
        public static Transform WorldRoot { get; private set; }
        public static HaloRules Rules { get; private set; } = new HaloRules();
        /// <summary>Shared match start (UTC unix seconds) agreed through the net layer; 0 = not started. The net layer may set it before
        /// StartMatch (a late joiner takes the master's); the Match module sets it to "now" when the world is entered and it is still 0.</summary>
        public static double StartUtc { get; set; }

        /// <summary>Phase changed (old, new).</summary>
        public static event Action<Phase, Phase> PhaseChanged;
        /// <summary>The map file is open and its world root exists (before the world is built): modules may start preparing.</summary>
        public static event Action MapOpened;
        /// <summary>The world (BSP, collision, scenery) is built: modules place their objects (vehicles, items, flags...).</summary>
        public static event Action WorldBuilt;
        /// <summary>The match is about to be torn down (world root still alive): modules clean up what they own.</summary>
        public static event Action Unloading;

        /// <summary>UTC now in unix seconds (the clock every client shares).</summary>
        public static double UtcNow { get { return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds; } }

        // ---- called by the Match module only
        public static void SetPhase(Phase p)
        {
            if (p == Current) return;
            var old = Current; Current = p;
            HaloLog.Info("Session: " + old + " -> " + p);
            var h = PhaseChanged;
            if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<Phase, Phase>)d)(old, p); }
                catch (Exception e) { HaloLog.Error("Session: a PhaseChanged handler failed: " + e); }
            }
        }

        public static void Open(HaloInstall.MapEntry entry, HaloMap map, HaloScenario scenario, Transform root, HaloRules rules)
        {
            MapEntry = entry; Map = map; Scenario = scenario; WorldRoot = root; Rules = rules ?? new HaloRules();
            Raise(MapOpened, "MapOpened");
        }

        public static void NotifyWorldBuilt() { Raise(WorldBuilt, "WorldBuilt"); }

        public static void Close()
        {
            Raise(Unloading, "Unloading");
            MapEntry = null; Map = null; Scenario = null; WorldRoot = null; StartUtc = 0;
        }

        /// <summary>Calls every handler of an event separately so one failure does not stop the rest.</summary>
        static void Raise(Action handlers, string what)
        {
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { ((Action)d)(); }
                catch (Exception e) { HaloLog.Error("Session: a " + what + " handler failed: " + e); }
            }
        }

        public static bool Playing => Current == Phase.Playing;
        /// <summary>In a match (playing or showing the results): the map is there.</summary>
        public static bool InMatch => Current == Phase.Playing || Current == Phase.Ending;
    }

    /// <summary>
    /// The game variant (Halo's multiplayer game options), chosen by the instance master in the lobby and published through the net
    /// layer. Defaults are Halo PC's own "Slayer" variant. The Front module edits it, the Net module carries it, the Match module plays it.
    /// Use <see cref="Defaults"/> for the default variant of another game type.
    /// </summary>
    [Serializable]
    public sealed class HaloRules
    {
        public HaloScenario.GameType Type = HaloScenario.GameType.Slayer;
        public bool Teams = false;
        /// <summary>Slayer: kills; CTF: captures; Race: laps; King of the Hill / Oddball: SECONDS (Halo shows minutes: 2 min = 120).</summary>
        public int ScoreLimit = 25;
        public int TimeLimitMinutes = 0;     // 0 = none (Halo default for slayer)
        /// <summary>The variant's "respawn time" option in seconds (0 = Halo's "instant"); the countdown is Halo's fixed 3 s plus this.</summary>
        public int RespawnSeconds = 0;
        public bool Shields = true;
        public bool MotionTracker = true;
        public bool FriendlyFire = false;
        public int WeaponSet = 0;            // 0 normal, then Halo's sets: pistols, rifles, plasma, snipers, no sniping, rocket launchers, shotguns, short range, human, covenant, classic, heavy weapons
        public int Vehicles = 0;             // 0 = the map's default set for the game type, 1 none, 2 warthogs, 3 ghosts, 4 scorpions, 5 rocket warthogs, 6 banshees, 7 gun turrets, 8 custom
        public bool Grenades = true;
        public bool StartWithOddball = false;
        public float SpeedScale = 1f;
        public string MapName = "bloodgulch";

        // ---- more of Halo's variant options (Match module)
        /// <summary>Number of lives (0 = infinite; Halo offers 1, 3, 5).</summary>
        public int Lives = 0;
        /// <summary>Extra respawn seconds after a suicide (Halo's "suicide penalty": 0, 5, 10, 15).</summary>
        public int SuicidePenaltySeconds = 0;
        /// <summary>King of the Hill: the hill moves (to the next hill of the map) every minute.</summary>
        public bool KingMovingHill = false;
        /// <summary>Race: 0 normal (checkpoints in order), 1 any order, 2 rally (one random checkpoint at a time).</summary>
        public int RaceType = 0;
        /// <summary>Team race: the team's laps are 0 the minimum, 1 the maximum, 2 the sum of its members' laps.</summary>
        public int RaceTeamScoring = 0;
        /// <summary>CTF: a capture counts only while the own flag is at home.</summary>
        public bool FlagAtHomeToScore = true;

        public HaloRules Clone() { return (HaloRules)MemberwiseClone(); }

        /// <summary>Halo PC's default variant for a game type (what the lobby shows when the type is picked).</summary>
        public static HaloRules Defaults(HaloScenario.GameType type, bool teams)
        {
            var r = new HaloRules { Type = type, Teams = teams };
            switch (type)
            {
                case HaloScenario.GameType.Ctf: r.Teams = true; r.ScoreLimit = 3; r.TimeLimitMinutes = 0; break;
                case HaloScenario.GameType.King: r.ScoreLimit = 120; break;          // 2 minutes in the hill
                case HaloScenario.GameType.Oddball: r.ScoreLimit = 120; break;       // 2 minutes with the ball  // ours (Halo's menu default not verified)
                case HaloScenario.GameType.Race: r.ScoreLimit = 4; r.Vehicles = 0; break;   // laps  // ours (Halo's menu default not verified)
                default: r.Type = HaloScenario.GameType.Slayer; r.ScoreLimit = teams ? 50 : 25; break;
            }
            return r;
        }

        /// <summary>The variant's name as Halo's menu shows it ("Team Slayer", "CTF", ...).</summary>
        public string Label
        {
            get
            {
                string t;
                switch (Type)
                {
                    case HaloScenario.GameType.Ctf: return "Capture the Flag";
                    case HaloScenario.GameType.King: t = "King of the Hill"; break;
                    case HaloScenario.GameType.Oddball: t = "Oddball"; break;
                    case HaloScenario.GameType.Race: t = "Race"; break;
                    default: t = "Slayer"; break;
                }
                return Teams ? "Team " + t : t;
            }
        }

        public override string ToString()
        {
            return Label + " on " + MapName + ", score " + ScoreLimit + ", time " + (TimeLimitMinutes > 0 ? TimeLimitMinutes + " min" : "none")
                + ", respawn +" + RespawnSeconds + " s" + (Lives > 0 ? ", " + Lives + " lives" : "") + (FriendlyFire ? ", friendly fire" : "")
                + (Shields ? "" : ", no shields") + ", weapons " + WeaponSet + ", vehicles " + Vehicles;
        }
    }
}

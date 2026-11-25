using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>One player of the instance as the lobby lists them.</summary>
    public struct HaloLobbyPlayer
    {
        public int Id;               // the net player id (slot / photon id), -1 for the local player offline
        public string Name;
        public int Team;             // HaloTeams.Red / Blue / None
        public bool Me, Master, InMatch;
    }

    /// <summary>
    /// What the instance master's lobby publishes (and every other client reads): the phase, the countdown, the game (rules incl. the map),
    /// the running match. <see cref="Version"/> grows with every change the master makes (rules or phase), so a reader can skip unchanged states.
    /// </summary>
    public struct HaloLobbyState
    {
        public const int Lobby = 0, Countdown = 1, InMatch = 2;

        public int Phase;            // Lobby, Countdown (Count seconds left), InMatch
        public int Count;            // seconds left of the countdown
        public int MatchId;          // grows with every match the master starts (a new match while one runs = a fresh game)
        public double StartUtc;      // the running match's start (HaloSession.StartUtc of the master), 0 = not known yet
        public int Version;
        public HaloRules Rules;      // the game: type, options and MapName (the map file name, e.g. "bloodgulch")
        /// <summary>The campaign (phase 2): the mission the master picked ("a10" .. "d40"), null or "" = a multiplayer game. While InMatch with a
        /// mission the clients play HaloCampaign.StartMission(Mission, Difficulty) instead of the multiplayer rules.</summary>
        public string Mission;
        /// <summary>HaloCampaign.Difficulty as an int (0 Easy .. 3 Legendary).</summary>
        public int Difficulty;
    }

    /// <summary>
    /// [Front module - the contract with the Net module] The lobby talks to the instance (slot) layer only through this interface. The Net module
    /// implements it and sets <see cref="HaloLobbyNet.Net"/> once its layer is up (null when it goes). Without it (offline, the editor, the lab)
    /// the lobby runs on its own: this client is the master and START starts the match at once.
    /// Rules: <see cref="Publish"/> is called by the master's lobby whenever its state changes (and once a second): carry it in the master's net
    /// record; <see cref="ReadMaster"/> returns what the current master last published (false while nothing arrived). The Front module does
    /// the pull-in: a non-master in the hub whose master's state is InMatch starts the same match (HaloMatchControl.StartMatch with the
    /// master's rules), and leaves it when the master's state leaves InMatch or its MatchId changes.
    /// </summary>
    public interface IHaloLobbyNet
    {
        /// <summary>In a room with a claimed slot: the other members below are real.</summary>
        bool Online { get; }
        /// <summary>The instance master is decided (false while connecting).</summary>
        bool MasterKnown { get; }
        bool IsMaster { get; }
        /// <summary>The master's Somnium name (null while not known).</summary>
        string MasterName { get; }
        /// <summary>The local player's Somnium name.</summary>
        string LocalName { get; }
        /// <summary>Everyone with a Halo slot (the local player included, Me = true). Must not allocate per call beyond the list's growth.</summary>
        void Players(List<HaloLobbyPlayer> into);
        /// <summary>The master's lobby state (master only; ignored otherwise).</summary>
        void Publish(HaloLobbyState state);
        /// <summary>The state the current master last published.</summary>
        bool ReadMaster(out HaloLobbyState state);
        /// <summary>Publishes the local player's team (HaloLocal.Team is already set). False with a reason when the net layer refuses it.</summary>
        bool SetTeam(int team, out string why);
    }

    public static class HaloLobbyNet
    {
        private static IHaloLobbyNet _net;

        /// <summary>Set by the Net module (Game/Net) when its slot layer is up; null = the lobby runs local.</summary>
        public static IHaloLobbyNet Net
        {
            get { return _net; }
            set
            {
                if (_net == value) return;
                _net = value;
                HaloLog.Info("Lobby: net link " + (value != null ? "set (" + value.GetType().Name + ")" : "cleared - the lobby runs local"));
            }
        }
    }
}

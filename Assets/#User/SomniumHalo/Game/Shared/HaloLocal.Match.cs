using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>The Match module's fields of the local player (written by Game/Match, carried on the wire by Net, read by the HUD).</summary>
    public static partial class HaloLocal
    {
        /// <summary>What the player carries of the game type's objects: <see cref="HaloObjectives.CarryNone"/> 0, red flag 1, blue flag 2, oddball 3.</summary>
        public static int Carry;

        /// <summary>The last objective event of this player (picked up / dropped / returned / captured a flag, the ball...): a counter that
        /// goes up by one per event, its code (<see cref="HaloObjectives"/>.Ev*) and where it happened. The net layer sends them; every other
        /// client applies them through HaloMatchControl.RemoteObjectiveEvent.</summary>
        public static int ObjEvent, ObjEventCode;
        public static Vector3 ObjEventPos;

        /// <summary>Lives left (-1 = infinite) and the current killing spree (kills since the last death).</summary>
        public static int LivesLeft = -1, Spree;

        /// <summary>Race: laps done and the index of the next checkpoint (netgame race flag usage id) to pass.</summary>
        public static int RaceLaps, RaceNext;

        /// <summary>King of the Hill: the player stands in the hill.</summary>
        public static bool InHill;
    }
}

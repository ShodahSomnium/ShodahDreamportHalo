using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>A vehicle's pose as the net layer knows it: driven (from the driver's record, interpolated) or parked (the instance master's world block).</summary>
    public struct HaloNetVehicle
    {
        public Vector3 Position, Velocity;
        /// <summary>Radians a second (world axis x angle), derived from the drawn pose 50 ms apart (driven only).</summary>
        public Vector3 AngularVelocity;
        /// <summary>The aim the driver / gunner publishes (world direction); seconds since the newest record arrived (driven only).</summary>
        public Vector3 Aim;
        public float Age;
        public Quaternion Rotation;
        public float Health;
        public int Flags;
        public bool Destroyed;
        /// <summary>The driver (null when parked).</summary>
        public HaloNetPlayer Driver;
    }

    /// <summary>An objective (CTF flag, oddball, hill...) as the instance master's world block says: Match's own state codes, the carrier slot (-1 none), a timer, flags, a position.</summary>
    public struct HaloNetObjective
    {
        public int State, Carrier, Timer, Flags;
        public Vector3 Position;
    }

    /// <summary>
    /// The vehicles, pickups and objectives of the match over the net (Vehicles, Combat, Match). Driven vehicles: the driver is authoritative and its record
    /// carries the pose (HaloNetGame.PublishDriving / HaloLocal.Vehicle). Unoccupied ones, the pickups' last-taken times (for late joiners) and the objectives
    /// live in the instance master's world blocks ("Halo Net World 0..3"); without those scene objects the Try* calls return false.
    /// </summary>
    public static partial class HaloNetGame
    {
        // ------------------------------------------------------------------ vehicles

        /// <summary>A vehicle somebody else drives: their record's pose, interpolated 0.16 s behind (false when nobody else drives it).</summary>
        public static bool TryDriven(int vehicle, out HaloNetVehicle v)
        {
            v = default(HaloNetVehicle);
            if (!Up || vehicle < 0) return false;
            for (int i = 0; i < ViewList.Count; i++)
            {
                HaloRemoteView view = ViewList[i];
                HaloNet.Remote r = view.Record;
                if (r == null || r.State.Vehicle != vehicle || !r.State.Driving) continue;
                HaloNetTimeline.Sample s;
                if (!r.Timeline.Evaluate(Net.Now, out s) || !s.InVehicle) return false;
                v.Position = HaloNetU.V(s.VehiclePos);
                v.Rotation = HaloNetU.Q(s.VehicleRot);
                v.Velocity = HaloNetU.V(s.VehicleVel);
                v.Health = r.State.VehicleHealth;
                v.Flags = r.State.VehicleFlags;
                v.Driver = view.Player;
                v.Aim = HaloNetU.V(r.State.Aim);
                v.Age = (float)(Net.Now - r.Timeline.NewestArrival);
                HaloNetTimeline.Sample s0;
                if (r.Timeline.Evaluate(Net.Now - 0.05, out s0) && s0.InVehicle)
                {
                    Quaternion d = v.Rotation * Quaternion.Inverse(HaloNetU.Q(s0.VehicleRot));
                    float ang; Vector3 axis;
                    d.ToAngleAxis(out ang, out axis);
                    if (ang > 180f) ang -= 360f;
                    v.AngularVelocity = float.IsNaN(axis.x) ? Vector3.zero : axis * (ang * Mathf.Deg2Rad / 0.05f);
                }
                return true;
            }

            return false;
        }

        /// <summary>Who sits in a vehicle's seat (another player of my match), null when nobody else.</summary>
        public static HaloNetPlayer Occupant(int vehicle, int seat)
        {
            if (!Up || vehicle < 0) return null;
            for (int i = 0; i < ViewList.Count; i++)
            {
                HaloNet.Remote r = ViewList[i].Record;
                if (r != null && r.State.Vehicle == vehicle && r.State.Seat == seat) return ViewList[i].Player;
            }

            return null;
        }

        /// <summary>The master's unoccupied vehicle (call every frame or when it changes: only changes are sent, about 5 times a second).</summary>
        public static void PublishParked(int vehicle, Vector3 pos, Quaternion rot, float health01, int flags, bool destroyed = false)
        {
            if (Up && Net.World.Writing) Net.World.SetVehicle(vehicle, destroyed, health01, flags, HaloNetU.V(pos), HaloNetU.Q(rot));
        }

        /// <summary>The master forgets a vehicle (it is gone for good, or somebody drives it).</summary>
        public static void ClearParked(int vehicle)
        {
            if (Up && Net.World.Writing) Net.World.ClearVehicle(vehicle);
        }

        /// <summary>An unoccupied vehicle as the master's world block says (false when not known: no world blocks, or the master has not written it).</summary>
        public static bool TryParked(int vehicle, out HaloNetVehicle v)
        {
            v = default(HaloNetVehicle);
            bool destroyed; float health; int flags; HaloV3 pos; HaloQ rot;
            if (!Up || !Net.World.Vehicle(vehicle, out destroyed, out health, out flags, out pos, out rot)) return false;
            v.Position = HaloNetU.V(pos);
            v.Rotation = HaloNetU.Q(rot);
            v.Health = health;
            v.Flags = flags;
            v.Destroyed = destroyed;
            return true;
        }

        /// <summary>The instance master writes the world state (parked vehicles, objectives): this client is it, in a game.</summary>
        public static bool WorldWriter { get { return Up && Net.World.Writing; } }

        /// <summary>This client owns the shared world of the running game (empty vehicles settle / respawn here, objectives are published from here): the
        /// instance master while it plays the game, else the lowest-id player in it (the master sits in the hub). Offline / no game: the master.</summary>
        public static bool WorldOwner { get { return !Up || Net.WorldOwner; } }

        // ------------------------------------------------------------------ pickups

        /// <summary>When a pickup (scenario NetEquipment index &lt; 128) was last taken, in seconds ago (late join; false = never, or not known).</summary>
        public static bool TryItemTaken(int item, out float secondsAgo)
        {
            secondsAgo = 0f;
            int at;
            if (!Up || !Net.World.ItemTakenAt(item, out at)) return false;
            secondsAgo = Mathf.Max(0f, Net.GameSeconds - at);
            return true;
        }

        // ------------------------------------------------------------------ objectives (Match)

        /// <summary>The master sets an objective (0..3) in its world block.</summary>
        public static void SetObjective(int index, int state, int carrierSlot, Vector3 pos, int timer, int flags = 0)
        {
            if (Up && Net.World.Writing) Net.World.SetObjective(index, state, carrierSlot, timer, flags, HaloNetU.V(pos));
        }

        public static bool TryObjective(int index, out HaloNetObjective o)
        {
            o = default(HaloNetObjective);
            int state, carrier, timer, flags; HaloV3 pos;
            if (!Up || !Net.World.Objective(index, out state, out carrier, out timer, out flags, out pos)) return false;
            o.State = state; o.Carrier = carrier; o.Timer = timer; o.Flags = flags; o.Position = HaloNetU.V(pos);
            return true;
        }

        /// <summary>Four free ints of the master's world block for the Match module (hill index, ...).</summary>
        public static void SetMatchInt(int index, int value)
        {
            if (Up && Net.World.Writing) Net.World.SetMisc(index, value);
        }

        public static bool TryMatchInt(int index, out int value)
        {
            value = 0;
            return Up && Net.World.Misc(index, out value);
        }

        // ------------------------------------------------------------------ build budget

        private static int _builtFrame = -1;

        /// <summary>One remote weapon model is built per frame at most (each costs a few milliseconds).</summary>
        internal static bool TakeBuildTurn()
        {
            if (_builtFrame == Time.frameCount) return false;
            _builtFrame = Time.frameCount;
            return true;
        }
    }
}

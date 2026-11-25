using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Campaign co-op, the host's vehicles and voices (DevTools/agents/N/NOTES.md "Campaign co-op - vehicles"). A vehicle the host's AI, a recording
    /// or the scripts move (a dropship flying in, a banshee, a ghost), one with units in its seats or its hatch open, travels as a vehicle record in
    /// the actor blocks (team 7: pose, velocity, last shot, open, hover, vitality; ~10 Hz) and followers draw it kinematic, 0.1 s behind with its
    /// velocity, replay its gunfire on its own gun and open / close its hatch. A player-driven vehicle keeps the player's own path (the driver's
    /// record); an idle empty one the world block's parked pose. Voices: the host's AI lines (HaloAiVoice) go through the mission ring as
    /// (serial, sound) and play once from the follower's puppet. Nothing allocates per frame.
    /// </summary>
    public static partial class HaloNetGame
    {
        /// <summary>Seconds a vehicle stays on the wire after it last moved / had units in it / was open (then it falls back to the parked pose).</summary>
        public static float VehicleLinger = 10f;

        /// <summary>Counters for the lab: vehicle records published (host) / drawn (follower), vehicle shots replayed, voice lines sent / played.</summary>
        public static int StatVehiclesPublished, StatVehiclesDrawn, StatVehicleShots, StatVoicesSent, StatVoicesPlayed;
        /// <summary>The largest step a followed vehicle took between two frames (lab: smoothness).</summary>
        public static float StatVehicleMaxStep;
        /// <summary>The worst frame step of a followed vehicle beyond what its own speed explains, as a fraction of that (lab: 0 = perfectly smooth).</summary>
        public static float StatVehicleJerk;
        public static string StatVehicleJerkInfo = "";
        private static float _hitchAt = -10f;
        /// <summary>Frames longer than 0.1 s while a vehicle was followed (lab: the smoothness numbers leave the 0.5 s after each out).</summary>
        public static int StatHitches;

        private static readonly HaloVehicle[] VehAt = new HaloVehicle[HaloNetCoop.MaxActors];
        private static readonly float[] VehActiveAt = new float[HaloNetCoop.MaxActors];
        private static readonly Vector3[] VehLastPos = new Vector3[HaloNetCoop.MaxActors];
        private static readonly int[] VehFire = new int[HaloNetCoop.MaxActors];
        private static readonly Vector3[] VehMuzzle = new Vector3[HaloNetCoop.MaxActors], VehDir = new Vector3[HaloNetCoop.MaxActors];
        private static bool _vehSubscribed;

        // follower: interpolation per place
        private static readonly HaloNetActor[] VPrev = new HaloNetActor[HaloNetCoop.MaxActors], VCur = new HaloNetActor[HaloNetCoop.MaxActors];
        private static readonly float[] VPrevAt = new float[HaloNetCoop.MaxActors], VCurAt = new float[HaloNetCoop.MaxActors];
        private static readonly bool[] VHave = new bool[HaloNetCoop.MaxActors], VOpenSeen = new bool[HaloNetCoop.MaxActors];
        private static readonly int[] VFire = new int[HaloNetCoop.MaxActors];
        private static readonly HaloVehicle[] VDrawn = new HaloVehicle[HaloNetCoop.MaxActors];
        private static readonly Vector3[] VShown = new Vector3[HaloNetCoop.MaxActors];
        private static readonly double[] VShownAt = new double[HaloNetCoop.MaxActors];      // (the net clock of the last draw: the lab metric's frame time)
        private static readonly HaloNetTimeline[] VTimeline = new HaloNetTimeline[HaloNetCoop.MaxActors];
        private static readonly int[] VStamp = new int[HaloNetCoop.MaxActors];
        private static readonly int[] CoopDrivenFrame = new int[HaloNetCoop.NoVehicle + 1];

        /// <summary>A follower draws this vehicle from the host's vehicle record (the Vehicles module leaves it alone then).</summary>
        public static bool CoopVehicle(int id)
        {
            return id >= 0 && id < CoopDrivenFrame.Length && CoopDrivenFrame[id] >= Time.frameCount - 1 && CoopDrivenFrame[id] > 0;
        }

        /// <summary>Host: an AI vocalization (HaloAiVoice) - followers play it once from the unit's puppet.</summary>
        public static void NoteVoice(HaloUnit u, HaloTag sound)
        {
            if (!Up || !Net.Coop.Writing || u == null || sound == null) return;
            int serial = SerialOf(u), tag = HaloNetU.TagIndex(sound);
            if (serial <= 0 || tag == HaloNetWire.NoTag) return;
            Net.Coop.AddEvent(HaloNetMissionEvent.Voice, (serial & 0xFFF) | (tag << 12));
            StatVoicesSent++;
        }

        private static bool PlaceFree(int p) { return Places[p] == null && VehAt[p] == null; }

        // ------------------------------------------------------------------ the host

        private static void SubscribeVehicles()
        {
            if (_vehSubscribed) return;
            _vehSubscribed = true;
            HaloEvents.Fired += OnAnyFired;
        }

        /// <summary>The host's AI vehicle guns raise Fired with who = -2: counted on the vehicle whose gun it is.</summary>
        private static void OnAnyFired(HaloTag weapon, Vector3 muzzle, Vector3 dir, int who)
        {
            if (who != -2 || !Up || !Net.Coop.Writing || weapon == null) return;
            int best = -1;
            float bestD = 400f;
            for (int p = 0; p < VehAt.Length; p++)
            {
                HaloVehicle v = VehAt[p];
                if (v == null || v.Gun == null || v.Gun.Tag != weapon) continue;
                float d = (v.transform.position - muzzle).sqrMagnitude;
                if (d < bestD) { bestD = d; best = p; }
            }

            if (best < 0) return;
            VehFire[best]++;
            VehMuzzle[best] = VehAt[best].transform.InverseTransformPoint(muzzle);
            VehDir[best] = dir;
        }

        /// <summary>A vehicle that should travel as a vehicle record: a campaign vehicle not driven by a player, active within the last seconds.</summary>
        private static bool VehicleActive(HaloVehicle v, float now, int place)
        {
            if (v == null || !v.Campaign || v.RemoteDriven) return false;
            HaloVehicleSeat mine = HaloVehicleSeat.Local;
            if (mine != null && mine.Seated && mine.Vehicle == v && mine.Driver) return false;      // (the local player drives it: the player's record)
            bool units = false;
            for (int s = 0; s < v.Seats.Count; s++) if (v.Seats[s].Unit != null) { units = true; break; }
            Vector3 pos = v.transform.position;
            bool moved = place >= 0 && (pos - VehLastPos[place]).sqrMagnitude > 0.0001f;
            return v.AiDriven || units || moved || v.IsOpen || v.Hovering || HaloCinematic.IsRecording(v.gameObject);
        }

        /// <summary>The host's vehicle records (from HostStep's write, ~10 Hz).</summary>
        private static void HostVehicles(float now, HaloNetCoop c)
        {
            SubscribeVehicles();
            var all = HaloVehicles.All;
            for (int i = 0; i < all.Count; i++)
            {
                HaloVehicle v = all[i];
                if (v == null || v.Id < 0 || v.Id >= HaloNetCoop.NoVehicle || !v.Campaign) continue;
                int place = PlaceOfVehicle(v);
                if (place < 0)
                {
                    if (!VehicleActive(v, now, -1) && !(v.transform.position != v.SpawnPosition)) continue;
                    place = FreeVehiclePlace();
                    if (place < 0) continue;
                    VehAt[place] = v;
                    VehActiveAt[place] = now;
                    VehLastPos[place] = v.transform.position;
                    VehFire[place] = 0;
                }
            }

            int published = 0;
            for (int p = 0; p < VehAt.Length; p++)
            {
                HaloVehicle v = VehAt[p];
                if (v == null) continue;
                if (v == null || !v.gameObject.activeInHierarchy) { VehAt[p] = null; c.ClearActor(p); continue; }
                if (VehicleActive(v, now, p)) VehActiveAt[p] = now;
                VehLastPos[p] = v.transform.position;
                if (now - VehActiveAt[p] > VehicleLinger || v.RemoteDriven) { VehAt[p] = null; c.ClearActor(p); continue; }
                Transform t = v.transform;
                var a = new HaloNetActor
                {
                    IsVehicle = true, Serial = HaloNetCoop.VehicleSerialBase + v.Id, Alive = !v.Destroyed, UnitTag = HaloNetU.TagIndex(v.Def != null ? v.Def.Tag : null),
                    VehicleId = v.Id, Position = HaloNetU.V(t.position), Rotation = HaloNetU.Q(t.rotation), Velocity = HaloNetU.V(v.Velocity),
                    MuzzleOffset = HaloNetU.V(VehMuzzle[p]), ShotDir = HaloNetU.V(VehDir[p]), FireCounter = VehFire[p] & 0xFF, Vitality = v.Vitality,
                    Open = v.IsOpen, Hover = v.Hovering,
                };
                c.SetActor(p, a);
                published++;
            }

            StatVehiclesPublished = published;
        }

        private static int PlaceOfVehicle(HaloVehicle v)
        {
            for (int p = 0; p < VehAt.Length; p++) if (VehAt[p] == v) return p;
            return -1;
        }

        /// <summary>Vehicles take places from the top (units from the bottom).</summary>
        private static int FreeVehiclePlace()
        {
            for (int p = VehAt.Length - 1; p >= 0; p--) if (PlaceFree(p)) return p;
            return -1;
        }

        // ------------------------------------------------------------------ a follower

        /// <summary>One vehicle record read this frame (ClientStep): drawn kinematic, 0.1 s behind, its shots and hatch.</summary>
        private static void FollowVehicle(int p, in HaloNetActor a)
        {
            HaloVehicle v = VDrawn[p] != null && VDrawn[p].Id == a.VehicleId ? VDrawn[p] : HaloVehicles.Find(a.VehicleId);
            if (v == null) return;      // (not created here yet: the host's object event creates it)
            if (VDrawn[p] != v)
            {
                VDrawn[p] = v;
                if (VTimeline[p] != null) VTimeline[p].Clear();
                VHave[p] = false;
                StatVehiclesDrawn++;
                HaloLog.Info("Net: co-op vehicle " + v.name + " (id " + v.Id + ") follows the host's record");
            }

            float now = Time.time;
            HaloNetActor cur = VCur[p];
            bool changed = !VHave[p] || a.Position.X != cur.Position.X || a.Position.Y != cur.Position.Y || a.Position.Z != cur.Position.Z || a.FireCounter != cur.FireCounter
                           || a.Rotation.X != cur.Rotation.X || a.Rotation.Y != cur.Rotation.Y || a.Rotation.W != cur.Rotation.W || a.Open != cur.Open;
            if (changed)
            {
                if (!VHave[p]) { VPrev[p] = a; VPrevAt[p] = now - 0.1f; VFire[p] = a.FireCounter; VShown[p] = HaloNetU.V(a.Position); }
                else { VPrev[p] = cur; VPrevAt[p] = VCurAt[p]; }
                VCur[p] = a;
                VCurAt[p] = now;
                VHave[p] = true;
                int shots = (a.FireCounter - VFire[p]) & 0xFF;
                VFire[p] = a.FireCounter;
                if (shots > 0 && shots < 64 && v.Gun != null)
                {
                    Vector3 dir = HaloNetU.V(a.ShotDir);
                    Vector3 muzzle = v.transform.TransformPoint(HaloNetU.V(a.MuzzleOffset));
                    try { for (int i = 0; i < shots && i < 4; i++) v.Gun.Replay(muzzle, dir.sqrMagnitude > 0.5f ? dir.normalized : v.transform.forward); }
                    catch (Exception e) { HaloLog.Warn("Net: replaying " + v.name + "'s gun failed: " + e.Message); }
                    StatVehicleShots += shots;
                }
            }

            if (!VOpenSeen[p] || a.Open != v.IsOpen)
            {
                VOpenSeen[p] = true;
                if (a.Open != v.IsOpen) { try { v.Open(a.Open); } catch (Exception e) { HaloLog.Warn("Net: " + v.name + " open / close failed: " + e.Message); } }
            }

            // the pose: the host's samples on the host's clock (the block's stamp), drawn 0.16 s behind like players (HaloNetTimeline)
            HaloNetTimeline tl = VTimeline[p] ?? (VTimeline[p] = new HaloNetTimeline());
            int stamp = Net.Coop.StampOf(p);
            double netNow = Net.Now;
            if (stamp != VStamp[p] || tl.Count == 0)
            {
                VStamp[p] = stamp;
                var smp = new HaloNetTimeline.Sample { Root = a.Position, Head = a.Position, HeadRot = HaloQ.Identity, LRot = HaloQ.Identity, RRot = HaloQ.Identity, InVehicle = true, VehiclePos = a.Position, VehicleRot = a.Rotation, VehicleVel = a.Velocity };
                tl.Add(stamp / 1000.0, netNow, in smp);
            }

            HaloNetTimeline.Sample at;
            if (!tl.Evaluate(netNow, out at)) return;
            Vector3 pos = HaloNetU.V(at.VehiclePos);
            Quaternion rot = HaloNetU.Q(at.VehicleRot);
            if (!v.RemoteDriven) v.Remote();
            v.Place(pos, rot, HaloNetU.V(VCur[p].Velocity));
            v.Vitality = a.Vitality;
            float step = (pos - VShown[p]).magnitude;
            if (Time.unscaledDeltaTime > 0.1f && Time.unscaledTime - _hitchAt > 0.05f) { StatHitches++; _hitchAt = Time.unscaledTime; }
            if (Time.unscaledTime - _hitchAt < 0.5f) step = 0f;      // (after a long frame the pose catches up with real time: not a network jerk)
            if (step > StatVehicleMaxStep && step < 50f) StatVehicleMaxStep = step;
            float since = VShownAt[p] > 0.0 && netNow > VShownAt[p] ? (float)(netNow - VShownAt[p]) : Time.unscaledDeltaTime;
            float expect = HaloNetU.V(at.VehicleVel).magnitude * Mathf.Max(since, 0.001f);      // (the net clock between two draws: the timeline runs on it, not on the frame time)
            float ratio = (step - expect) / Mathf.Max(0.05f, expect);      // how much more than its own speed a frame moved it (0 = exactly its speed)
            if (step < 50f && ratio > StatVehicleJerk) { StatVehicleJerk = ratio; StatVehicleJerkInfo = "step " + step.ToString("0.000") + " m in " + since.ToString("0.000") + " s at " + HaloNetU.V(at.VehicleVel).magnitude.ToString("0.0") + " m/s, samples " + tl.Count + ", frame " + Time.frameCount; }
            VShown[p] = pos;
            VShownAt[p] = netNow;
            if (v.Id >= 0 && v.Id < CoopDrivenFrame.Length) CoopDrivenFrame[v.Id] = Time.frameCount;
        }

        /// <summary>A voice event from the host's ring: the line from the unit's puppet, once.</summary>
        private static void PlayVoice(int value)
        {
            int serial = value & 0xFFF, tag = (value >> 12) & 0xFFFF;
            HaloTag sound = HaloNetU.TagOf(tag);
            if (sound == null) return;
            Transform at = null;
            for (int i = 0; i < Puppets.Length; i++)
                if (Puppets[i] != null && Puppets[i].Serial == serial) { at = Puppets[i].transform; break; }
            if (at == null) return;
            try { HaloAudio.Play(sound, at); }
            catch (Exception e) { HaloLog.Warn("Net: a co-op voice line failed: " + e.Message); return; }
            StatVoicesPlayed++;
            if (StatVoicesPlayed == 1) HaloLog.Info("Net: first co-op voice line from the host: " + sound.Name + " (unit " + serial + ")");
        }

        /// <summary>The hand-over: the vehicles this follower drew from records stay where they are, held (the AI / scripts drive them again from here).</summary>
        private static void PromoteVehicles()
        {
            int kept = 0;
            float now = Time.unscaledTime;
            for (int p = 0; p < VDrawn.Length; p++)
            {
                HaloVehicle v = VDrawn[p];
                VDrawn[p] = null;
                VHave[p] = false;
                if (v == null) continue;
                v.Park(false);
                if (PlaceFree(p)) { VehAt[p] = v; VehActiveAt[p] = now; VehLastPos[p] = v.transform.position; VehFire[p] = VCur[p].FireCounter; kept++; }
            }

            if (kept > 0) HaloLog.Info("Net: hand-over - " + kept + " vehicle(s) taken over where the old host left them");
        }

        private static void ClearVehicles()
        {
            for (int p = 0; p < VehAt.Length; p++) { VehAt[p] = null; VDrawn[p] = null; VHave[p] = false; VOpenSeen[p] = false; }
        }
    }
}

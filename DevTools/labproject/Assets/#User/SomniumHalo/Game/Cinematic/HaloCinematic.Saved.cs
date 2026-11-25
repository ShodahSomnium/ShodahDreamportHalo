using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The running recordings in a checkpoint (HaloCheckpoints part "recordings") and in the co-op host state (HaloNetHostState part
    /// "recordings", order 40): Halo's saved game holds every recorded animation in progress, so a revert to a checkpoint taken while a
    /// dropship flies its enter recording (a30 save_first_wave_2 during pipe_cship_enter) goes on with the flight instead of leaving the ship
    /// hanging where it was. Per recording: its target (scenario placement index, object name, net unit serial, the live object), the
    /// recorded animation, the playback tick, the end mode, and the target's recorded state - a HaloVehicle's flight
    /// (<see cref="HaloVehicle.RecordedState"/>: poses, heading, speeds, HaloPlaneState, collideable), a kinematic mover's poses and speeds,
    /// a unit's control of that tick. Restore: the recording starts again, is stepped to its tick without applying any control (nothing
    /// moves), then the target's state is put back - the next game tick goes on exactly where the checkpoint was.
    /// Player.log: "Cinematic: checkpoint ... recording(s)" / "Cinematic: hand-over ... recording(s)".
    /// </summary>
    public static partial class HaloCinematic
    {
        const string SavePart = "recordings";
        const int SaveVersion = 1;
        static bool _netPart;

        /// <summary>One recording in progress, as saved.</summary>
        public sealed class SavedRecording
        {
            public int Placement = -1, Serial;
            public string Name = "", Recording = "";
            public GameObject Target;
            public int Tick;
            public RecordingEnd End;
            public HaloVehicle.RecordedState Vehicle;
            public bool Kinematic, BodyWasKinematic;
            public Vector3 PrevPos, Pos; public Quaternion PrevRot, Rot;
            public float Speed, Side;
            public HaloPlaneState PlaneState;
        }

        /// <summary>Registers the checkpoint part (again after a mission leave cleared the parts) and the host-state part, during a campaign.</summary>
        static void EnsureSaveParts()
        {
            if (!HaloCampaign.Active) return;
            if (!HaloCheckpoints.Registered(SavePart)) HaloCheckpoints.Register(SavePart, CaptureCheckpoint, RestoreCheckpoint);
            if (!_netPart) { _netPart = true; HaloNetHostState.Register(SavePart, 40, NetCapture, NetAdopt); }
        }

        static object CaptureCheckpoint() { return CaptureRecordings(); }
        static void RestoreCheckpoint(object state) { RestoreRecordings(state as List<SavedRecording>, "checkpoint"); }

        /// <summary>Ticks of its recording played on this object (0 when none plays).</summary>
        public static int RecordingTick(GameObject target)
        {
            for (int i = 0; i < _recs.Count; i++) if (_recs[i].Target == target) return _recs[i].Playback.Tick;
            return 0;
        }

        /// <summary>Every recording in progress with its target's recorded state (the checkpoint part; labs).</summary>
        public static List<SavedRecording> CaptureRecordings()
        {
            var l = new List<SavedRecording>(_recs.Count);
            for (int i = 0; i < _recs.Count; i++)
            {
                var p = _recs[i];
                if (p.Target == null || p.Playback.Finished) continue;
                var s = new SavedRecording { Target = p.Target, Name = p.Name ?? "", Recording = p.Playback.Recording.Name, Tick = p.Playback.Tick, End = p.End };
                var mo = p.Target.GetComponent<HaloMissionObject>();
                if (mo != null && mo.PlacementIndex >= 0 && HaloObjects.LiveAt(mo.PlacementIndex) == p.Target) s.Placement = mo.PlacementIndex;
                if (p.Unit != null) { try { s.Serial = HaloNetGame.SerialOf(p.Unit); } catch (System.Exception) { } }
                if (p.Vehicle != null && p.Custom != null) s.Vehicle = p.Vehicle.CaptureRecorded();
                if (p.Kinematic)
                {
                    s.Kinematic = true; s.BodyWasKinematic = p.BodyWasKinematic;
                    s.PrevPos = p.PrevPos; s.Pos = p.Pos; s.PrevRot = p.PrevRot; s.Rot = p.Rot; s.Speed = p.Speed; s.Side = p.Side; s.PlaneState = p.PlaneState;
                }
                l.Add(s);
            }
            return l;
        }

        /// <summary>The object a saved recording plays on now: its placement, else its name, else the same live object, else its net unit.</summary>
        static GameObject Resolve(SavedRecording s)
        {
            GameObject g = s.Placement >= 0 ? HaloObjects.LiveAt(s.Placement) : null;
            if (g == null && !string.IsNullOrEmpty(s.Name)) g = HaloObjects.Find(s.Name);
            if (g == null && s.Target != null) g = s.Target;
            if (g == null && s.Serial != 0)
            {
                HaloUnit u = null;
                try { u = HaloNetGame.UnitOfSerial(s.Serial); } catch (System.Exception) { }
                if (u != null) g = u.gameObject;
            }
            return g;
        }

        /// <summary>Puts saved recordings back (after the objects are where the checkpoint had them); returns how many play again.</summary>
        public static int RestoreRecordings(List<SavedRecording> saved, string why)
        {
            if (saved == null) return 0;
            int ok = 0;
            string text = "";
            for (int i = 0; i < saved.Count; i++)
            {
                var s = saved[i];
                var g = Resolve(s);
                if (g == null) { HaloLog.Warn("Cinematic: " + why + ": the object of recording " + s.Recording + " (" + (s.Name.Length > 0 ? s.Name : "placement " + s.Placement) + ") is gone - not resumed"); continue; }
                var p = Resume(g, s);
                if (p == null) continue;
                ok++;
                text += (text.Length > 0 ? ", " : "") + g.name + " " + s.Recording + " at tick " + p.Playback.Tick + "/" + p.Playback.Recording.Length + (s.Vehicle != null ? " (flight state" + (s.Vehicle.Collideable ? "" : ", not collideable") + ")" : "");
            }
            HaloLog.Info("Cinematic: " + why + ": " + ok + " of " + saved.Count + " recording(s) resumed" + (text.Length > 0 ? ": " + text : ""));
            return ok;
        }

        /// <summary>Starts the recording again on <paramref name="g"/>, steps it to the saved tick without applying any control, and puts the
        /// target's recorded state back.</summary>
        static RecPlay Resume(GameObject g, SavedRecording s)
        {
            HaloCinemaView.Ensure();
            var d = D;
            var rec = d != null ? d.Recording(s.Recording) : null;
            if (rec == null) { HaloLog.Warn("Cinematic: no recorded animation \"" + s.Recording + "\" to resume"); return null; }
            KillRecording(g, false);
            var p = new RecPlay
            {
                Target = g, Name = s.Name, End = s.End, Playback = new HaloRecordingPlayback(rec),
                Custom = g.GetComponent<IHaloRecordedControl>(), Unit = g.GetComponent<HaloUnit>(),
                Vehicle = g.GetComponent<HaloVehicle>(), Puppet = g.GetComponent<HaloRecordedPuppet>(),
            };
            if (string.IsNullOrEmpty(p.Name) && p.Unit != null) p.Name = p.Unit.ObjectName;
            for (int t = 0; t < s.Tick && !p.Playback.Finished; t++) p.Playback.Step();
            if (p.Playback.Finished) { HaloLog.Warn("Cinematic: recording " + s.Recording + " had ended by tick " + s.Tick + " - not resumed"); return null; }
            if (p.Custom == null && p.Unit == null)
            {
                StartKinematic(p);
                if (s.Kinematic)
                {
                    p.PrevPos = s.PrevPos; p.Pos = s.Pos; p.PrevRot = s.PrevRot; p.Rot = s.Rot; p.Speed = s.Speed; p.Side = s.Side; p.PlaneState = s.PlaneState;
                    p.BodyWasKinematic = s.BodyWasKinematic;
                    g.transform.SetPositionAndRotation(p.Pos, p.Rot);
                }
            }
            _recs.Add(p);
            if (p.Vehicle != null && p.Custom != null)
            {
                if (s.Vehicle != null) p.Vehicle.RestoreRecorded(s.Vehicle);
                else p.Custom.RecordedControl(p.Playback.Control);     // (saved without its state: it starts flying from where it stands)
            }
            else if (p.Custom != null) p.Custom.RecordedControl(p.Playback.Control);   // a unit: this tick's recorded control (facing, throttle, aim)
            else if (p.Unit != null) { var c = p.Playback.Control; ApplyUnit(p.Unit, ref c); }
            return p;
        }

        // ------------------------------------------------------------------ co-op host hand-over

        static byte[] NetCapture()
        {
            if (!HaloCampaign.Active) return null;
            var l = CaptureRecordings();
            var ms = new MemoryStream(256);
            var w = new BinaryWriter(ms);
            w.Write(SaveVersion);
            w.Write(l.Count);
            for (int i = 0; i < l.Count; i++)
            {
                var s = l[i];
                w.Write(s.Placement); w.Write(s.Serial); w.Write(s.Name ?? ""); w.Write(s.Recording ?? ""); w.Write(s.Tick); w.Write((int)s.End);
                w.Write(s.Kinematic);
                if (s.Kinematic)
                {
                    W(w, s.PrevPos); W(w, s.Pos); W(w, s.PrevRot); W(w, s.Rot); w.Write(s.Speed); w.Write(s.Side); W(w, s.PlaneState); w.Write(s.BodyWasKinematic);
                }
                var v = s.Vehicle;
                w.Write(v != null);
                if (v != null)
                {
                    W(w, v.Prev); W(w, v.Pos); W(w, v.Heading); W(w, v.Face); W(w, v.Vel); W(w, v.PrevRot); W(w, v.Rot);
                    w.Write(v.Speed); w.Write(v.Side); w.Write(v.Vert); W(w, v.Plane); w.Write(v.Collideable); w.Write(v.SaidGhost);
                    var st = v.Stats;
                    w.Write(st.Name ?? ""); w.Write(st.Ticks); w.Write(st.FirstContactTick); w.Write(st.Landings); w.Write(st.TerrainContacts); w.Write(st.Climbs);
                    w.Write(st.PassThrough); w.Write(st.EndHeight); w.Write(st.MaxTilt); w.Write(st.Hover);
                }
            }
            w.Flush();
            return ms.ToArray();
        }

        /// <summary>The new host: the old host's recordings go on from the blob's tick and state (the same moment the adopted scripts are at).</summary>
        static void NetAdopt(byte[] b)
        {
            if (b == null) { HaloLog.Info("Cinematic: hand-over - no recordings part in the host state"); return; }
            try
            {
                var r = new BinaryReader(new MemoryStream(b));
                if (r.ReadInt32() != SaveVersion) { HaloLog.Warn("Cinematic: hand-over - unknown recordings part version"); return; }
                int n = r.ReadInt32();
                var l = new List<SavedRecording>(n);
                for (int i = 0; i < n; i++)
                {
                    var s = new SavedRecording { Placement = r.ReadInt32(), Serial = r.ReadInt32(), Name = r.ReadString(), Recording = r.ReadString(), Tick = r.ReadInt32(), End = (RecordingEnd)r.ReadInt32() };
                    s.Kinematic = r.ReadBoolean();
                    if (s.Kinematic)
                    {
                        s.PrevPos = V(r); s.Pos = V(r); s.PrevRot = Q(r); s.Rot = Q(r); s.Speed = r.ReadSingle(); s.Side = r.ReadSingle(); s.PlaneState = P(r); s.BodyWasKinematic = r.ReadBoolean();
                    }
                    if (r.ReadBoolean())
                    {
                        var v = new HaloVehicle.RecordedState();
                        v.Prev = V(r); v.Pos = V(r); v.Heading = V(r); v.Face = V(r); v.Vel = V(r); v.PrevRot = Q(r); v.Rot = Q(r);
                        v.Speed = r.ReadSingle(); v.Side = r.ReadSingle(); v.Vert = r.ReadSingle(); v.Plane = P(r); v.Collideable = r.ReadBoolean(); v.SaidGhost = r.ReadBoolean();
                        var st = new HaloVehicle.RecordingStats();
                        st.Name = r.ReadString(); st.Ticks = r.ReadInt32(); st.FirstContactTick = r.ReadInt32(); st.Landings = r.ReadInt32(); st.TerrainContacts = r.ReadInt32(); st.Climbs = r.ReadInt32();
                        st.PassThrough = r.ReadInt32(); st.EndHeight = r.ReadSingle(); st.MaxTilt = r.ReadSingle(); st.Hover = r.ReadBoolean();
                        v.Stats = st;
                        s.Vehicle = v;
                    }
                    l.Add(s);
                }
                RestoreRecordings(l, "hand-over");
            }
            catch (System.Exception e) { HaloLog.Error("Cinematic: hand-over - reading the recordings part failed: " + e); }
        }

        static void W(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static void W(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        static void W(BinaryWriter w, HaloPlaneState s) { w.Write(s.Yaw); w.Write(s.Speed); w.Write(s.Side); w.Write(s.Vx); w.Write(s.Vy); w.Write(s.Vz); }
        static Vector3 V(BinaryReader r) { return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        static Quaternion Q(BinaryReader r) { return new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
        static HaloPlaneState P(BinaryReader r) { return new HaloPlaneState { Yaw = r.ReadSingle(), Speed = r.ReadSingle(), Side = r.ReadSingle(), Vx = r.ReadSingle(), Vy = r.ReadSingle(), Vz = r.ReadSingle() }; }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Carrier lab (workstream 4: recorded vehicle flight and vehicle cargo), on a30 WITH the BSP built (the older vehicles-dropship lab
    /// had no world, which is why it passed while dropships flew 78 m up in the game).
    /// "carrier": mission_lz_dropship by hand - lz_cship with four grunts (vehicle_load_magic "passenger"), object_teleport lz_cship_flag,
    /// recording_play_and_hover lz_cship_enter -> lz_cship_landing_drop -> unit_open + vehicle_unload -> recording_play_and_delete
    /// lz_cship_landing_exit. Checks: the hull ends the drop no more than ~3 wu above the floor, the grunts land within 1 m of the ground,
    /// the exit has 0 terrain-contact ticks, the hull pitch stays 0 throughout. Pictures: the drop hover from the side, the unloaded grunts.
    /// "carrier-pelican": a30 "first" - the jeep as cargo of foehammer_first (unit_enter_vehicle jeep foehammer_first "cargo"),
    /// foehammer_first_flag, recording_play_and_hover foehammer_first_in, then unit_exit_vehicle jeep: the jeep rides under the pelican
    /// and, released, lands on its wheels.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabCarrier
    {
        static HaloLabCarrier()
        {
            HaloLabRunner.Register("carrier", r => Dropship(r));
            HaloLabRunner.Register("carrier-pelican", r => Pelican(r));
            HaloLabRunner.Register("carrier-drops", r => Drops(r));
            HaloLabRunner.Register("carrier-revert", r => Revert(r));
        }

        static HaloMap _map;
        static HaloScenario _sc;
        static GameObject _stage;
        static HaloMissionData _mission;

        /// <summary>Horizontal metres from a point to the centre of a scenario trigger volume (-1 = no such volume); inside = within its box
        /// (Halo's own point test at the volume's centre height).</summary>
        static float ToVolume(Vector3 p, string volume, out bool inside, out Vector3 centre)
        {
            inside = false; centre = Vector3.zero;
            int i = _mission != null ? _mission.TriggerVolumeIndex(volume) : -1;
            if (i < 0) return -1f;
            var v = _mission.TriggerVolumes[i];
            centre = HaloSpace.MapOrigin + HaloSpace.Pos(v.Center);
            Vector3 hp = p - HaloSpace.MapOrigin;
            inside = v.Contains(hp.x / HaloSpace.Unit, hp.z / HaloSpace.Unit, v.Center.Z);
            Vector3 d = p - centre; d.y = 0f;
            return d.magnitude;
        }

        static IEnumerator Seconds(float s) { int n = Mathf.Max(1, Mathf.RoundToInt(s * 60f)); for (int i = 0; i < n; i++) yield return null; }

        /// <summary>a30 with the BSP that holds <paramref name="flagName"/> built (no scenario objects), the cutscene data loaded.</summary>
        static IEnumerator World(HaloLabRunner r, string flagName, int bspOverride = -1)
        {
            HaloInstall.MapEntry entry = null;
            foreach (var e in r.Game.Install.Campaign) if (e.Name == "a30") entry = e;
            if (!r.Check(entry != null, "a30 in the install")) yield break;
            _map = new HaloMap(entry.File);
            _sc = new HaloScenario(_map);
            Time.captureFramerate = 60;
            HaloLocal.Alive = true;
            HaloVehicles.LabMode = true; HaloVehicles.CampaignLab = true;
            HaloCombat.LabMap = _map; HaloCombat.LabScenario = _sc;
            try { HaloFx.Preload(_map); } catch (System.Exception e) { HaloLabRunner.Log("fx preload: " + e.Message); }
            HaloCinematic.Load(_map);
            var hub = GameObject.Find("Halo Hub"); if (hub != null) hub.SetActive(false);
            _stage = new GameObject("carrier lab");
            HaloBipeds.LabParent = _stage.transform;
            var sun = new GameObject("sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);

            Vector3 flagPos; Quaternion flagRot;
            if (!r.Check(HaloCinematic.Flag(flagName, out flagPos, out flagRot), "cutscene flag " + flagName)) yield break;
            // the BSP whose collision has an open leaf under the flag (where the drop happens)
            int bsp = 0;
            HVec3 hf = new HVec3(flagPos.x / HaloSpace.Unit, flagPos.z / HaloSpace.Unit, flagPos.y / HaloSpace.Unit);
            for (int i = 0; i < _sc.Bsps.Count; i++)
            {
                HaloCollisionBsp col = null;
                foreach (var cb in _sc.BspRoot(i).Block(176, 96)) { col = new HaloCollisionBsp(cb); break; }
                if (col == null) continue;
                bool open = false;
                for (float d = 0f; d < 60f && !open; d += 2f) open = col.Leaf(hf.X, hf.Y, hf.Z - d) >= 0;
                if (open) { bsp = i; break; }
            }
            if (bspOverride >= 0 && bspOverride < _sc.Bsps.Count) bsp = bspOverride;
            _mission = new HaloMissionData(_sc);
            HaloLabRunner.Log("building a30 bsp " + bsp + " (holds " + flagName + " at " + flagPos.ToString("F0") + ")");
            HaloWorld.PlaceObjects = false;
            var root = new GameObject("Lab World Root");
            var it = HaloWorld.Build(_map, _sc, root.transform, null, bsp);
            while (it.MoveNext()) yield return it.Current;
            HaloWorldDriver.View = r.Cam;
            Physics.SyncTransforms();
            yield return null;
        }

        static HaloVehicle SpawnNamed(HaloLabRunner r, string objectName)
        {
            HaloScenario.Placement cp = null;
            foreach (var p in _sc.Vehicles)
                if (p.Type != null && p.NameIndex >= 0 && p.NameIndex < _sc.ObjectNames.Count && _sc.ObjectNames[p.NameIndex] == objectName) cp = p;
            if (!r.Check(cp != null, "the scenario's " + objectName + " placement")) return null;
            var v = HaloVehicles.SpawnPlacement(cp, objectName, _stage.transform);
            r.Check(v != null, objectName + " (" + cp.Type.Name + ") placed");
            return v;
        }

        /// <summary>Metres from a point down to the ground (-1 none within 300 m).</summary>
        static float Below(Vector3 p)
        {
            RaycastHit h;
            return Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out h, 300f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore) ? h.distance - 0.3f : -1f;
        }

        static float Tilt(Transform t)
        {
            Vector3 e = t.eulerAngles;
            return Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(0f, e.x)), Mathf.Abs(Mathf.DeltaAngle(0f, e.z)));
        }

        static void Side(HaloLabRunner r, Transform t, string name, float back = 40f, float up = 6f)
        {
            Vector3 side = t.right;
            r.Aim(t.position + side * back + Vector3.up * up, t.position - Vector3.up * 4f, 60f);
            r.Shot(name);
        }

        /// <summary>Plays one recording and waits for it; tracks the hull tilt every frame.</summary>
        static IEnumerator Play(HaloLabRunner r, HaloVehicle v, string rec, HaloCinematic.RecordingEnd end, float[] maxTilt)
        {
            r.Check(HaloCinematic.PlayRecording(v.gameObject, rec, end, v.ObjectName), "recording " + rec + " (" + end + ")");
            int ticks = HaloCinematic.RecordingTime(v.gameObject);
            float until = Time.time + ticks / 30f + 2f;
            while (Time.time < until && v != null && HaloCinematic.IsRecording(v.gameObject))
            {
                maxTilt[0] = Mathf.Max(maxTilt[0], Tilt(v.transform));
                yield return null;
            }
            yield return null;
        }

        static string Stats(HaloVehicle.RecordingStats s)
        {
            return "'" + s.Name + "' " + s.Ticks + " ticks, end " + s.EndHeight.ToString("0.0") + " m (" + (s.EndHeight / HaloSpace.Unit).ToString("0.0") + " wu) over the ground, first contact " + s.FirstContactTick
                   + ", landing ticks " + s.Landings + ", terrain contacts " + s.TerrainContacts + ", slid-upward ticks " + s.Climbs;
        }

        // ------------------------------------------------------------------ lz_cship

        static IEnumerator Dropship(HaloLabRunner r)
        {
            yield return World(r, "lz_cship_flag");
            if (r.Failed) yield break;
            var ship = SpawnNamed(r, "lz_cship");
            if (ship == null) yield break;
            ship.Open(false);
            Vector3 flagPos; Quaternion flagRot;
            HaloCinematic.Flag("lz_cship_flag", out flagPos, out flagRot);
            ship.transform.SetPositionAndRotation(flagPos, flagRot);
            Physics.SyncTransforms();

            HaloTag grunt = null;
            foreach (HaloTag t in _map.OfClass("actv")) if (t.Name.IndexOf("grunt minor", System.StringComparison.OrdinalIgnoreCase) >= 0) { grunt = t; break; }
            if (grunt == null) foreach (HaloTag t in _map.OfClass("bipd")) if (t.Name.Contains("grunt")) { grunt = t; break; }
            var troops = new List<HaloUnit>();
            for (int s = 0; s < ship.Seats.Count && troops.Count < 4; s++)
            {
                if (ship.Seats[s].Label.IndexOf("passenger", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var u = HaloBipeds.Spawn(grunt, flagPos, 0f, HaloTeam.Covenant);
                if (u != null && ship.SeatUnit(s, u)) troops.Add(u);
            }
            r.Check(troops.Count == 4, "four grunts in the passenger seats (" + troops.Count + ")");
            r.Check(ship.GetComponent<IHaloRecordedControl>() == (IHaloRecordedControl)ship, "HaloVehicle drives its own recordings (IHaloRecordedControl)");

            var tilt = new float[1];
            yield return Play(r, ship, "lz_cship_enter", HaloCinematic.RecordingEnd.Hover, tilt);
            var enter = ship.LastRecording;
            HaloLabRunner.Log("  enter: " + Stats(enter) + ", at " + ship.transform.position.ToString("F1"));
            Side(r, ship.transform, "carrier_enter_end");

            yield return Play(r, ship, "lz_cship_landing_drop", HaloCinematic.RecordingEnd.Hover, tilt);
            var drop = ship.LastRecording;
            HaloLabRunner.Log("  drop: " + Stats(drop) + ", at " + ship.transform.position.ToString("F1"));
            Side(r, ship.transform, "carrier_drop_hover_side");
            Side(r, ship.transform, "carrier_drop_hover_low", 30f, -2f);
            r.Check(drop.EndHeight >= 0f && drop.EndHeight <= 3f * HaloSpace.Unit, "the dropship ends the drop at most ~3 wu over the floor (" + (drop.EndHeight / HaloSpace.Unit).ToString("0.0") + " wu)");
            bool inLz; Vector3 lzC;
            float toLz = ToVolume(ship.transform.position, "lz_landing", out inLz, out lzC);
            // a30 mission_lz_dropship warns the player (lz_cship_danger) when they stand in lz_landing right before the drop: the ship comes
            // down inside that volume (Halo's own point test, at the volume's centre height)
            r.Check(toLz >= 0f && inLz, "lz_cship drops inside the script's lz_landing (" + (toLz / HaloSpace.Unit).ToString("0.0") + " wu from its centre" + (inLz ? ", inside" : ", outside") + ")");
            Vector3 hover = ship.transform.position;
            yield return Seconds(1f);
            r.Check((ship.transform.position - hover).magnitude < 0.05f, "it hovers after the drop (moved " + (ship.transform.position - hover).magnitude.ToString("0.00") + " m in 1 s)");

            // unit_open, then vehicle_unload seat by seat (5 ticks apart)
            ship.Open(true);
            yield return Seconds(1f);
            foreach (var u in troops) { if (u != null && ship.SeatOfUnit(u) >= 0) ship.UnseatUnit(u); yield return Seconds(5f / 30f); }
            yield return Seconds(5f);
            int onGround = 0;
            foreach (var u in troops)
            {
                if (u == null) continue;
                float b = Below(u.transform.position);
                HaloLabRunner.Log("  grunt at " + u.transform.position.ToString("F1") + " seat " + (u.Seat != null) + " grounded " + u.Grounded + ", " + b.ToString("0.000") + " m over the ground");
                if (u.Seat == null && b >= -0.1f && b <= 1f) onGround++;   // feet resting a few mm into the floor (ground skin) count; -1 = no floor under it
            }
            r.Check(onGround == troops.Count, "the grunts are out and within 1 m of the ground (" + onGround + "/" + troops.Count + ")");
            if (troops.Count > 0 && troops[0] != null)
            {
                Vector3 g = troops[0].transform.position;
                r.Aim(g + ship.transform.right * 12f + Vector3.up * 3f, g, 60f);
                r.Shot("carrier_troops_unloaded");
            }

            // a30 mission_lz_dropship: (recording_play_and_delete lz_cship lz_cship_landing_exit) (sleep 30) (unit_close lz_cship)
            // (object_set_collideable lz_cship false) - the ship then flies its recorded track through the hills
            var shipRef = ship;
            r.Check(HaloCinematic.PlayRecording(ship.gameObject, "lz_cship_landing_exit", HaloCinematic.RecordingEnd.Delete, ship.ObjectName), "recording lz_cship_landing_exit (Delete)");
            int exitTicks = HaloCinematic.RecordingTime(ship.gameObject);
            yield return Seconds(1f);   // (sleep 30)
            ship.Open(false);
            var cols = ship.GetComponentsInChildren<Collider>(true);     // object_set_collideable false (HaloScriptFns.Objects: every collider off)
            for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
            float exitUntil = Time.time + exitTicks / 30f + 2f;
            int pics = 0; float lowest = float.MaxValue;
            while (Time.time < exitUntil && shipRef != null && HaloCinematic.IsRecording(shipRef.gameObject))
            {
                tilt[0] = Mathf.Max(tilt[0], Tilt(shipRef.transform));
                float b = Below(shipRef.transform.position + Vector3.up * 50f) - 50f;   // the hull origin's height over the terrain under it (negative: inside a hill)
                if (b > -50f && b < lowest) lowest = b;
                if (Time.frameCount % 240 == 0 && pics < 3) Side(r, shipRef.transform, "carrier_exit_" + (pics++), 45f, 8f);
                yield return null;
            }
            yield return null;
            var exit = ship.LastRecording;      // (managed data: readable after the deletion)
            HaloLabRunner.Log("  exit: " + Stats(exit) + ", ticks not collideable " + exit.PassThrough + ", lowest hull origin over the terrain " + lowest.ToString("0.0") + " m");
            r.Check(exit.Ticks > 0 && exit.TerrainContacts == 0, "the exit makes no terrain contact while it is collideable (" + exit.TerrainContacts + " ticks)");
            r.Check(exit.PassThrough >= exit.Ticks - 40 && exit.Climbs == 0, "after object_set_collideable false the exit flies its recorded track (" + exit.PassThrough + " of " + exit.Ticks + " ticks not collideable, " + exit.Climbs + " slid-upward ticks)");
            r.Check(tilt[0] < 0.5f, "the hull pitch / roll stays 0 throughout (max " + tilt[0].ToString("0.0") + " deg)");
            yield return Seconds(0.5f);
            r.Check(shipRef == null, "recording_play_and_delete removed the dropship");

            // the other drops of this BSP (a30 first_wave 2..6: fort / pipe / back cships) on the same world
            yield return DropChains(r, DropsBsp0);
        }

        // ------------------------------------------------------------------ the other a30 dropship drops (enter + drop, no troops)

        // object, its object_teleport flag, the LZ volume the script tests for players before the unload, enter, drop
        static readonly string[][] DropsBsp0 =
        {
            new[] { "fort_cship", "fort_cship_flag", "first_fort_lz", "fort_cship_enter", "fort_cship_drop" },
            new[] { "pipe_cship", "pipe_cship_flag", "first_pipe_lz", "pipe_cship_enter", "pipe_cship_drop" },
            new[] { "back_cship", "back_cship_flag", "first_back_lz", "back_cship_enter", "back_cship_drop" },
        };
        static readonly string[][] DropsBsp1 =
        {
            new[] { "rubble_rock_cship", "rubble_rock_cship_flag", "rubble_rock_lz", "rubble_rock_cship_enter", "rubble_rock_cship_drop" },
            new[] { "rubble_middle_cship", "rubble_middle_cship_flag", "rubble_middle_lz", "rubble_middle_cship_enter", "rubble_middle_cship_drop" },
            new[] { "rubble_boat_cship", "rubble_boat_cship_flag", "rubble_boat_lz", "rubble_boat_cship_enter", "rubble_boat_cship_drop" },
            // river_boat_lz is not where river_boat_cship drops (53 wu off at every speed scale): its distance is logged, not checked
            new[] { "river_boat_cship", "river_boat_cship_flag", "river_boat_lz", "river_boat_cship_enter", "river_boat_cship_drop" },
        };

        /// <summary>"carrier-drops -halobsp 0|1": the a30 wave dropships of that BSP one after another, each from its flag through its enter
        /// and drop recordings (recording_play_and_hover both, as the scripts): the drop must end at most ~3 wu over the floor (the script
        /// unloads 60 ticks before its end) and near the LZ volume the script guards for that drop.</summary>
        static IEnumerator Drops(HaloLabRunner r)
        {
            int bsp = int.Parse(HaloLabBoot.Arg("-halobsp", "0"));
            var list = bsp == 1 ? DropsBsp1 : DropsBsp0;
            yield return World(r, list[0][1], bsp);
            if (r.Failed) yield break;
            yield return DropChains(r, list);
        }

        /// <summary>Flies each chain (one game tick per frame to keep the run short) and checks where its drop ends.</summary>
        static IEnumerator DropChains(HaloLabRunner r, string[][] list)
        {
            int fps = Time.captureFramerate;
            Time.captureFramerate = 30;
            foreach (var d in list)
            {
                var ship = SpawnNamed(r, d[0]);
                if (ship == null) continue;
                ship.Open(false);
                Vector3 flagPos; Quaternion flagRot;
                if (!r.Check(HaloCinematic.Flag(d[1], out flagPos, out flagRot), "cutscene flag " + d[1])) { HaloVehicles.Remove(ship); continue; }
                ship.transform.SetPositionAndRotation(flagPos, flagRot);
                Physics.SyncTransforms();
                var tilt = new float[1];
                yield return Play(r, ship, d[3], HaloCinematic.RecordingEnd.Hover, tilt);
                var enter = ship.LastRecording;
                yield return Play(r, ship, d[4], HaloCinematic.RecordingEnd.Hover, tilt);
                var drop = ship.LastRecording;
                bool inLz; Vector3 lzC;
                float toLz = ToVolume(ship.transform.position, d[2], out inLz, out lzC);
                HaloLabRunner.Log("  " + d[0] + ": enter " + Stats(enter) + " | drop " + Stats(drop) + " | at " + ship.transform.position.ToString("F1") + ", "
                                  + (toLz / HaloSpace.Unit).ToString("0.0") + " wu across from " + d[2] + " " + lzC.ToString("F1") + (inLz ? " (inside)" : ""));
                r.Check(drop.EndHeight >= 0f && drop.EndHeight <= 3f * HaloSpace.Unit, d[0] + " ends " + d[4] + " at most ~3 wu over the floor (" + (drop.EndHeight / HaloSpace.Unit).ToString("0.0") + " wu)");
                if (d[0] != "river_boat_cship")
                    r.Check(toLz >= 0f && inLz, d[0] + " drops inside the script's " + d[2] + " (" + (toLz / HaloSpace.Unit).ToString("0.0") + " wu from its centre" + (inLz ? ", inside" : ", outside") + "; the script waits for the players to leave that volume before the unload)");
                r.Check(tilt[0] < 0.5f, d[0] + " hull stays level (max " + tilt[0].ToString("0.0") + " deg)");
                Side(r, ship.transform, "drop_" + d[0], 40f, 4f);
                HaloVehicles.Remove(ship);
                yield return null;
            }
            Time.captureFramerate = fps;
        }

        // ------------------------------------------------------------------ checkpoint revert in the middle of a recording

        /// <summary>"carrier-revert": a30 mission_first_wave_3 - pipe_cship with four grunts flies pipe_cship_enter from pipe_cship_flag; at
        /// tick ~250 a checkpoint takes the "recordings" part (HaloCinematic.CaptureRecordings) and the hull pose (K's movable placement), the
        /// flight goes on 120 ticks, then the revert does what HaloCheckpoints.Restore does (HaloCinematic.Clear kills every recording, the
        /// hull goes back to its checkpoint pose, the part restores). The resumed enter must end where an uninterrupted enter ends, then
        /// pipe_cship_drop ends inside first_pipe_lz at most ~3 wu over the floor and the grunts unload onto the ground.</summary>
        static IEnumerator Revert(HaloLabRunner r)
        {
            const string Obj = "pipe_cship", Flag = "pipe_cship_flag", Lz = "first_pipe_lz", Enter = "pipe_cship_enter", Drop = "pipe_cship_drop";
            yield return World(r, Flag);
            if (r.Failed) yield break;
            int fps = Time.captureFramerate;
            Time.captureFramerate = 30;     // one game tick a frame
            Vector3 flagPos; Quaternion flagRot;
            HaloCinematic.Flag(Flag, out flagPos, out flagRot);

            // the reference: the same enter without a revert
            var refShip = SpawnNamed(r, Obj);
            if (refShip == null) yield break;
            refShip.Open(false);
            refShip.transform.SetPositionAndRotation(flagPos, flagRot);
            Physics.SyncTransforms();
            var tilt = new float[1];
            yield return Play(r, refShip, Enter, HaloCinematic.RecordingEnd.Hover, tilt);
            var refStats = refShip.LastRecording;
            Vector3 refEnd = refShip.transform.position;
            HaloLabRunner.Log("  reference enter: " + Stats(refStats) + ", at " + refEnd.ToString("F2"));
            HaloVehicles.Remove(refShip);
            yield return null;

            // the run with a checkpoint at tick ~250 and a revert 120 ticks later
            var ship = SpawnNamed(r, Obj);
            if (ship == null) yield break;
            ship.Open(false);
            ship.transform.SetPositionAndRotation(flagPos, flagRot);
            Physics.SyncTransforms();
            HaloTag grunt = null;
            foreach (HaloTag t in _map.OfClass("actv")) if (t.Name.IndexOf("grunt minor", System.StringComparison.OrdinalIgnoreCase) >= 0) { grunt = t; break; }
            if (grunt == null) foreach (HaloTag t in _map.OfClass("bipd")) if (t.Name.Contains("grunt")) { grunt = t; break; }
            var troops = new List<HaloUnit>();
            for (int s = 0; s < ship.Seats.Count && troops.Count < 4; s++)
            {
                if (ship.Seats[s].Label.IndexOf("passenger", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var u = HaloBipeds.Spawn(grunt, flagPos, 0f, HaloTeam.Covenant);
                if (u != null && ship.SeatUnit(s, u)) troops.Add(u);
            }
            r.Check(troops.Count == 4, "four grunts in the passenger seats (" + troops.Count + ")");

            r.Check(HaloCinematic.PlayRecording(ship.gameObject, Enter, HaloCinematic.RecordingEnd.Hover, Obj), "recording_play_and_hover " + Obj + " " + Enter);
            int total = HaloCinematic.RecordingTime(ship.gameObject);
            float until = Time.time + 20f;
            while (Time.time < until && HaloCinematic.RecordingTick(ship.gameObject) < 250) yield return null;
            // the checkpoint: K's movable placement pose + the recordings part
            Vector3 cpPos = ship.transform.position; Quaternion cpRot = ship.transform.rotation;
            var saved = HaloCinematic.CaptureRecordings();
            int cpTick = saved.Count > 0 ? saved[0].Tick : -1, cpLeft = HaloCinematic.RecordingTime(ship.gameObject);
            r.Check(saved.Count == 1 && saved[0].Recording == Enter && saved[0].Vehicle != null, "the checkpoint holds " + Enter + " at tick " + cpTick + " of " + total + " with the flight state (" + saved.Count + " recording(s))");
            Side(r, ship.transform, "revert_checkpoint", 40f, 4f);
            until = Time.time + 20f;
            while (Time.time < until && HaloCinematic.RecordingTick(ship.gameObject) < cpTick + 120) yield return null;
            Vector3 lateAt = ship.transform.position;

            // the revert (HaloCheckpoints.Restore / Apply): every recording killed, the hull back at its checkpoint pose, the parts restored
            HaloCinematic.Clear();
            r.Check(!HaloCinematic.IsRecording(ship.gameObject), "HaloCinematic.Clear killed the recording (as the revert does)");
            ship.transform.SetPositionAndRotation(cpPos, cpRot);
            Physics.SyncTransforms();
            int resumed = HaloCinematic.RestoreRecordings(saved, "lab revert");
            r.Check(resumed == 1 && HaloCinematic.RecordingName(ship.gameObject) == Enter && HaloCinematic.RecordingTick(ship.gameObject) == cpTick && HaloCinematic.RecordingTime(ship.gameObject) == cpLeft,
                    "the revert resumes " + Enter + " at the checkpoint's tick (tick " + HaloCinematic.RecordingTick(ship.gameObject) + " = " + cpTick + ", " + HaloCinematic.RecordingTime(ship.gameObject) + " ticks left = " + cpLeft + "; it had flown on to " + lateAt.ToString("F1") + ")");
            r.Check(ship.RecordingDrives && (ship.transform.position - saved[0].Vehicle.Pos).magnitude < 0.001f, "the hull is back at its flight state's tick pose (" + (ship.transform.position - saved[0].Vehicle.Pos).magnitude.ToString("0.000") + " m off)");
            yield return null;
            Side(r, ship.transform, "revert_resumed", 40f, 4f);
            until = Time.time + cpLeft / 30f + 5f;
            while (Time.time < until && HaloCinematic.IsRecording(ship.gameObject)) { tilt[0] = Mathf.Max(tilt[0], Tilt(ship.transform)); yield return null; }
            yield return null;
            var enter = ship.LastRecording;
            float off = (ship.transform.position - refEnd).magnitude;
            HaloLabRunner.Log("  reverted enter: " + Stats(enter) + ", at " + ship.transform.position.ToString("F2") + ", " + off.ToString("0.000") + " m from the reference end");
            r.Check(enter.Ticks == refStats.Ticks, "the resumed enter counts the whole recording (" + enter.Ticks + " ticks, reference " + refStats.Ticks + ")");
            r.Check(off < 0.1f, "the resumed enter ends where the uninterrupted enter ends (" + off.ToString("0.000") + " m apart)");

            // (sleep (recording_time pipe_cship)) (vehicle_hover pipe_cship false) (recording_play_and_hover pipe_cship pipe_cship_drop)
            yield return Play(r, ship, Drop, HaloCinematic.RecordingEnd.Hover, tilt);
            var drop = ship.LastRecording;
            bool inLz; Vector3 lzC;
            float toLz = ToVolume(ship.transform.position, Lz, out inLz, out lzC);
            HaloLabRunner.Log("  drop after the revert: " + Stats(drop) + ", at " + ship.transform.position.ToString("F1") + ", " + (toLz / HaloSpace.Unit).ToString("0.0") + " wu from " + Lz + " " + lzC.ToString("F1") + (inLz ? " (inside)" : ""));
            r.Check(drop.EndHeight >= 0f && drop.EndHeight <= 3f * HaloSpace.Unit, Obj + " ends " + Drop + " at most ~3 wu over the floor after the revert (" + (drop.EndHeight / HaloSpace.Unit).ToString("0.0") + " wu)");
            r.Check(toLz >= 0f && inLz, Obj + " drops inside the script's " + Lz + " after the revert (" + (toLz / HaloSpace.Unit).ToString("0.0") + " wu from its centre" + (inLz ? ", inside" : ", outside") + ")");
            r.Check(tilt[0] < 0.5f, "the hull stays level (max " + tilt[0].ToString("0.0") + " deg)");
            Side(r, ship.transform, "revert_drop_hover", 30f, -2f);

            // unit_open, vehicle_unload seat by seat
            Time.captureFramerate = fps;
            ship.Open(true);
            yield return Seconds(1f);
            foreach (var u in troops) { if (u != null && ship.SeatOfUnit(u) >= 0) ship.UnseatUnit(u); yield return Seconds(5f / 30f); }
            yield return Seconds(5f);
            int onGround = 0;
            foreach (var u in troops)
            {
                if (u == null) continue;
                float b = Below(u.transform.position);
                bool inside; Vector3 c;
                float to = ToVolume(u.transform.position, Lz, out inside, out c);
                HaloLabRunner.Log("  grunt at " + u.transform.position.ToString("F1") + " seat " + (u.Seat != null) + " grounded " + u.Grounded + ", " + b.ToString("0.000") + " m over the ground, " + (to / HaloSpace.Unit).ToString("0.0") + " wu from the LZ centre");
                if (u.Seat == null && b >= -0.1f && b <= 1f) onGround++;
            }
            r.Check(troops.Count == 4 && onGround == troops.Count, "the grunts unload onto the ground after the revert (" + onGround + "/" + troops.Count + ")");
            if (troops.Count > 0 && troops[0] != null)
            {
                Vector3 g = troops[0].transform.position;
                r.Aim(g + ship.transform.right * 12f + Vector3.up * 3f, g, 60f);
                r.Shot("revert_troops_unloaded");
            }
        }

        // ------------------------------------------------------------------ foehammer_first with the jeep

        static IEnumerator Pelican(HaloLabRunner r)
        {
            yield return World(r, "foehammer_first_flag");
            if (r.Failed) yield break;
            var pel = SpawnNamed(r, "foehammer_first");
            var jeep = SpawnNamed(r, "jeep");
            if (pel == null || jeep == null) yield break;
            yield return Seconds(0.5f);
            int cargo = -1;
            for (int s = 0; s < pel.Seats.Count; s++) if (pel.Seats[s].Label.IndexOf("cargo", System.StringComparison.OrdinalIgnoreCase) >= 0) cargo = s;
            if (!r.Check(cargo >= 0, "the pelican has a 'cargo' seat")) yield break;
            // unit_enter_vehicle jeep foehammer_first "cargo", then object_teleport foehammer_first foehammer_first_flag
            r.Check(pel.SeatVehicle(cargo, jeep), "unit_enter_vehicle jeep foehammer_first \"cargo\" seats the jeep");
            r.Check(jeep.CarriedBy == pel && jeep.Body.isKinematic && !jeep.EnterableByPlayer, "the jeep is carried (kinematic, not enterable)");
            // checkpoint revert (HaloCheckpoints.Apply): every carried vehicle is let go in place first, then seated again if the checkpoint had it
            Transform jeepHome = jeep.transform.parent;
            var released = new List<HaloVehicle>();
            HaloVehicles.ReleaseAllCargo(released);
            bool freed = jeep.CarriedBy == null && jeep.EnterableByPlayer && !jeep.transform.IsChildOf(pel.transform) && pel.CargoIn(cargo) == null && released.Contains(jeep);
            r.Check(freed, "a revert lets the carried jeep go (not carried, not the pelican's child, enterable, seat free; " + released.Count + " released)");
            r.Check(pel.SeatVehicle(cargo, jeep) && jeep.CarriedBy == pel && jeep.transform.parent == jeepHome && jeep.Body.isKinematic && !jeep.EnterableByPlayer && pel.SeatOfCargo(jeep) == cargo, "the revert can seat it on the same cargo seat again");
            Vector3 flagPos; Quaternion flagRot;
            HaloCinematic.Flag("foehammer_first_flag", out flagPos, out flagRot);
            pel.transform.SetPositionAndRotation(flagPos, flagRot);
            Physics.SyncTransforms();

            var tilt = new float[1];
            r.Check(HaloCinematic.PlayRecording(pel.gameObject, "foehammer_first_in", HaloCinematic.RecordingEnd.Hover, "foehammer_first"), "recording_play_and_hover foehammer_first foehammer_first_in");
            int ticks = HaloCinematic.RecordingTime(pel.gameObject);
            float until = Time.time + ticks / 30f + 2f, maxOff = 0f;
            int pic = 0;
            Transform marker = pel.Seats[cargo].Marker;
            while (Time.time < until && HaloCinematic.IsRecording(pel.gameObject))
            {
                tilt[0] = Mathf.Max(tilt[0], Tilt(pel.transform));
                if (marker != null) maxOff = Mathf.Max(maxOff, (jeep.transform.position - marker.position).magnitude);
                if (Time.frameCount % 600 == 0 && pic < 3) Side(r, pel.transform, "pelican_flying_" + (pic++), 35f, 4f);
                yield return null;
            }
            var st = pel.LastRecording;
            HaloLabRunner.Log("  foehammer_first_in: " + Stats(st) + ", jeep " + (jeep.transform.position - pel.transform.position).ToString("F1") + " from the hull origin");
            r.Check(maxOff < 0.05f, "the jeep rides on the cargo marker the whole flight (max offset " + maxOff.ToString("0.000") + " m)");
            r.Check(tilt[0] < 0.5f, "the pelican hull stays level (max tilt " + tilt[0].ToString("0.0") + " deg)");
            // a30: (sleep_until (not (volume_test_objects first_drop (players)))) (unit_exit_vehicle jeep) - the drop zone is that volume
            bool inDrop; Vector3 dropC;
            float toDrop = ToVolume(jeep.transform.position, "first_drop", out inDrop, out dropC);
            HaloLabRunner.Log("  the jeep hangs " + (toDrop / HaloSpace.Unit).ToString("0.0") + " wu (" + toDrop.ToString("0.0") + " m) across from the centre of the script's drop zone first_drop " + dropC.ToString("F1") + (inDrop ? " (inside it)" : ""));
            r.Check(toDrop >= 0f && inDrop, "foehammer_first_in brings the jeep into the script's drop zone first_drop (" + (toDrop / HaloSpace.Unit).ToString("0.0") + " wu across from its centre" + (inDrop ? ", inside" : ", outside") + "; a30 waits for the players to leave it, then unit_exit_vehicle jeep)");
            r.Check(st.EndHeight >= 0f && st.EndHeight <= 3f * HaloSpace.Unit, "the pelican ends its flight low over the LZ (" + (st.EndHeight / HaloSpace.Unit).ToString("0.0") + " wu, at most ~3 wu)");
            Side(r, pel.transform, "pelican_hover_with_jeep", 30f, -1f);

            // unit_exit_vehicle jeep
            Vector3 releasedAt = jeep.transform.position;   // (the hull origin is 5 m off the cargo marker: measure from the jeep itself)
            pel.UnseatVehicle(jeep);
            r.Check(jeep.CarriedBy == null && jeep.EnterableByPlayer, "unit_exit_vehicle releases the jeep");
            for (float w = 0f; w < 12f && (w < 5f || !jeep.Body.isKinematic); w += 0.25f) yield return Seconds(0.25f);   // until it rests (the settle holds it again)
            float below = Below(jeep.ShapeBounds().min + Vector3.up * 0.05f);
            Vector3 lz = releasedAt; lz.y = jeep.transform.position.y;
            HaloLabRunner.Log("  jeep after it settled at " + jeep.transform.position.ToString("F1") + " up " + jeep.transform.up.ToString("F2") + ", lowest shape " + below.ToString("0.00") + " m over the ground, " + (jeep.transform.position - lz).magnitude.ToString("0.0") + " m from under where it was let go, kinematic " + jeep.Body.isKinematic);
            r.Check(jeep.transform.up.y > 0.85f && !jeep.Overturned, "the jeep lands on its wheels (up.y " + jeep.transform.up.y.ToString("0.00") + ")");
            // resting = every wheel's mass point has the ground within its own suspension reach (the physics tag's extension, Halo data); a
            // single ray from the bounds' lowest corner read under the surface on a slope
            int reach = 0; string wheels = "";
            foreach (var w in jeep.Wheels)
            {
                float b = Below(jeep.transform.TransformPoint(w.Local));
                wheels += (wheels.Length > 0 ? " " : "") + b.ToString("0.00") + "/" + w.MaxDist.ToString("0.00");
                if (b >= -0.1f && b <= w.MaxDist + 0.05f) reach++;
            }
            r.Check(jeep.Wheels.Count > 0 && reach == jeep.Wheels.Count, "the jeep rests on the ground: " + reach + " of " + jeep.Wheels.Count + " wheels within their suspension reach (m to the ground / reach: " + wheels + ")");
            HaloLabRunner.Log("  the jeep came to rest " + (jeep.transform.position - lz).magnitude.ToString("0.0") + " m from under where the pelican let it go (no retail number to check it against)");
            r.Aim(jeep.transform.position + pel.transform.right * 14f + Vector3.up * 4f, jeep.transform.position, 60f);
            r.Shot("pelican_jeep_landed");
        }
    }
}

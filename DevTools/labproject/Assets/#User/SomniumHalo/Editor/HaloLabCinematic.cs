using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Cinematics labs (DevTools/lab.sh cinematic | cinematic-script):
    /// cinematic: a10's bridge (BSP 1) built, Keyes (captain) and three crewmen spawned by Characters at the script's cutscene flags; the
    ///   bridge part of a10's x10 driven directly: cinematic_start, camera_control, letterbox, fade_in, camera_set keyes_x10_deck_1a 0 -> 1b
    ///   200, recording_play keyes_walk_1 + x10_crew_walk_1/3/4, then keyes_x10_display_walk_1a -> 1b 200 with the title "bridge".
    ///   Pictures: cine_tNNN.png = the cutscene camera's own picture (4:3) at ticks; vr_*.png = the VR cinema screen seen from the
    ///   player's head (dimmed bridge around it); mon_*.png = the monitor overlay. Checks: recordings end where the script expects
    ///   (Keyes at keyes_peer), camera_time / recording_time count down, the camera reaches its point.
    /// cinematic-script: the real a10 start (K's StartMission + S's scripts) watched for cinematic_start / camera / fades (once K and S run).
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabCinematic
    {
        static HaloLabCinematic()
        {
            HaloLabRunner.Register("cinematic", r => Bridge(r));
            HaloLabRunner.Register("cinematic-script", r => Script(r));
        }

        static HaloMap _map;

        static IEnumerator BuildA10(HaloLabRunner r, int bsp)
        {
            var entry = r.Game.Install.Map("a10");
            if (!r.Check(entry != null, "a10 in the install")) yield break;
            HaloSession.SetPhase(HaloSession.Phase.Loading);
            yield return null;
            var hub = GameObject.Find("Halo Hub");
            if (hub != null) hub.SetActive(false);
            _map = new HaloMap(entry.File);
            var sc = new HaloScenario(_map);
            var root = new GameObject("Lab World Root");
            root.transform.position = HaloSpace.MapOrigin;
            float t0 = Time.realtimeSinceStartup;
            var it = HaloWorld.Build(_map, sc, root.transform, null, bsp);
            while (it.MoveNext()) yield return it.Current;
            r.Check(HaloWorld.Ready, "a10 BSP " + bsp + " built in " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s");
            HaloBipeds.LabParent = root.transform;
            HaloLocal.Alive = true;
            HaloCombat.LabMap = _map;
            try { HaloFx.Preload(_map); } catch (System.Exception e) { HaloLabRunner.Log("fx preload: " + e.Message); }
        }

        static HaloUnit SpawnAt(HaloLabRunner r, string bipd, string flag)
        {
            var tag = _map.Find("bipd", bipd);
            Vector3 p = Vector3.zero; float yaw = 0f;
            bool found = HaloCinematic.Flag(flag, out p, out yaw);
            if (!r.Check(tag != null && found, "biped " + bipd + " at flag " + flag)) return null;
            var u = HaloBipeds.Spawn(tag, p, yaw, HaloTeam.Human);
            r.Check(u != null, "spawned " + bipd + " at " + flag + " " + p.ToString("F2") + " yaw " + yaw.ToString("0"));
            return u;
        }

        static void SaveRt(HaloLabRunner r, RenderTexture rt, string name)
        {
            if (rt == null) { r.Fail("no cutscene picture for " + name); return; }
            NoFog();
            RenderCut();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            var px = tex.GetPixels32();
            long sum = 0; for (int i = 0; i < px.Length; i += 11) sum += px[i].r + px[i].g + px[i].b;
            File.WriteAllBytes(Path.Combine(r.Out, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            HaloLabRunner.Log("picture " + name + ".png (cutscene camera " + rt.width + "x" + rt.height + ") brightness " + (sum / (3f * 255f * (px.Length / 11 + 1))).ToString("0.00"));
        }

        /// <summary>Batch mode does not draw cameras by itself: render the cutscene camera into its picture now.</summary>
        /// <summary>Lab only: a10's space sky has fog density 1 over 0-0 m (World turns that into a black-out at 1 m - reported to W);
        /// without fog the pictures show the bridge.</summary>
        static void NoFog() { if (!FogOn) { HaloMaterials.SetFog(Color.black, 0f, 0f, 100000f); RenderSettings.fog = false; } }
        static bool FogOn = HaloLabBoot.Arg("-halofog", "0") == "1";

        static void RenderCut()
        {
            var v = HaloCinemaView.Instance;
            if (v != null && v.CutsceneCamera != null && v.CutsceneCamera.enabled) v.CutsceneCamera.Render();
        }

        static IEnumerator Ticks(int n)
        {
            int target = HaloCinematic.Ticks + n;
            int guard = n * 8 + 20;
            while (HaloCinematic.Ticks < target && guard-- > 0) yield return null;
        }

        static IEnumerator Bridge(HaloLabRunner r)
        {
            Time.captureFramerate = 60;
            yield return BuildA10(r, 1);
            if (r.Failed) yield break;
            var data = HaloCinematic.Load(_map);
            r.Check(data != null && data.CameraPoints.Count == 292 && data.Recordings.Count == 99 && data.RecordingErrors == 0,
                "a10 cutscene data: " + (data != null ? data.CameraPoints.Count + " camera points, " + data.Recordings.Count + " recordings, " + data.RecordingErrors + " unreadable, " + data.Titles.Count + " titles" : "none"));
            HaloCinemaView.Ensure();
            HaloCinemaView.Instance.UseFontOf(_map);

            var keyes = SpawnAt(r, "characters\\captain\\captain", "keyes_deck");
            var crew1 = SpawnAt(r, "characters\\crewman\\crewman", "x10_walk_1_base");
            var crew3 = SpawnAt(r, "characters\\crewman\\crewman", "x10_walk_3_base");
            var crew4 = SpawnAt(r, "characters\\crewman\\crewman", "x10_walk_4_base");
            yield return r.Frames(30);

            // the bridge part of x10 (a10.hsc "bridge"), driven directly
            HaloCinematic.Begin();
            HaloCinematic.CameraControl(true);
            HaloCinematic.Letterbox(true);
            HaloCinematic.Fade(true, Color.black, 30);
            HaloCinematic.CameraSet("keyes_x10_deck_1a", 0);
            HaloCinematic.CameraSet("keyes_x10_deck_1b", 200);
            r.Check(HaloCinematic.CameraTime == 200, "camera_time after camera_set 1b 200 = " + HaloCinematic.CameraTime);
            if (keyes != null) r.Check(HaloCinematic.PlayRecording(keyes, "keyes_walk_1"), "recording_play keyes keyes_walk_1");
            if (crew1 != null) HaloCinematic.PlayRecording(crew1, "x10_crew_walk_1");
            if (crew3 != null) HaloCinematic.PlayRecording(crew3, "x10_crew_walk_3");
            if (crew4 != null) HaloCinematic.PlayRecording(crew4, "x10_crew_walk_4");
            int rec0 = keyes != null ? HaloCinematic.RecordingTime(keyes.gameObject) : 0;
            r.Check(rec0 == 350, "recording_time keyes = " + rec0 + " (keyes_walk_1 is 350 ticks)");

            var view = HaloCinemaView.Instance;
            yield return r.Frames(2);
            SaveRt(r, view.Texture, "cine_t000");
            yield return Ticks(15);
            SaveRt(r, view.Texture, "cine_t015_fading_in");
            yield return Ticks(85);
            SaveRt(r, view.Texture, "cine_t100");
            r.Check(Mathf.Abs(HaloCinematic.CameraTime - 100) <= 2, "camera_time half way = " + HaloCinematic.CameraTime);
            yield return Ticks(100);
            SaveRt(r, view.Texture, "cine_t200_deck_1b");
            Vector3 cp; Quaternion cr; float cf;
            HaloCinematic.CameraPose(out cp, out cr, out cf);
            var p1b = data.Camera("keyes_x10_deck_1b");
            r.Check(HaloCinematic.CameraTime == 0 && (cp - HaloSpace.Pos(p1b.Position)).magnitude < 0.01f, "camera reached keyes_x10_deck_1b (" + cp.ToString("F2") + ")");

            // the walk to the console, the camera following along the display
            HaloCinematic.CameraSet("keyes_x10_display_walk_1a", 0);
            HaloCinematic.CameraSet("keyes_x10_display_walk_1b", 200);
            HaloCinematic.Title("bridge");
            yield return Ticks(40);
            SaveRt(r, view.Texture, "cine_t240_title");
            // the same pose through the lab camera (a plain camera on the screen): tells the scene from the cutscene camera
            { Vector3 lp; Quaternion lr; float lf; HaloCinematic.CameraPose(out lp, out lr, out lf); r.Cam.transform.SetPositionAndRotation(lp, lr); r.Cam.fieldOfView = view.CutsceneCamera.fieldOfView; r.Cam.nearClipPlane = view.CutsceneCamera.nearClipPlane; r.Shot("labcam_t240", 640, 480); bool fog = RenderSettings.fog; HaloLabRunner.Log("fog " + fog + " mode " + RenderSettings.fogMode + " colour " + RenderSettings.fogColor + " density " + RenderSettings.fogDensity + " start " + RenderSettings.fogStartDistance + " end " + RenderSettings.fogEndDistance + " ambient " + RenderSettings.ambientLight + " " + RenderSettings.ambientMode); RenderSettings.fog = false; r.Shot("labcam_t240_nofog", 640, 480); RenderSettings.fog = fog; r.Cam.nearClipPlane = 0.05f; }
            // VR: the screen from the player's head (player0_base_x10_bridge flag)
            Vector3 hp; float hy;
            HaloCinematic.Flag("player0_base_x10_bridge", out hp, out hy);
            var head = new GameObject("lab head").transform;
            head.SetPositionAndRotation(hp + Vector3.up * 1.7f, Quaternion.Euler(0f, hy, 0f));
            HaloCinemaView.LabHead = head;
            HaloCinemaView.ForceVr = true;
            yield return r.Frames(3);
            r.Aim(head.position - head.forward * 0.4f, head.position + head.forward * 3f, 90f);
            NoFog(); RenderCut();
            r.Shot("vr_screen_t243");
            r.Aim(head.position, head.position + Quaternion.Euler(0f, 60f, 0f) * head.forward * 3f, 90f);
            NoFog(); RenderCut();
            r.Shot("vr_side_t243");
            HaloCinemaView.ForceVr = false;
            HaloCinemaView.LabHead = null;
            yield return Ticks(60);
            SaveRt(r, view.Texture, "cine_t300");
            // monitor: the overlay drawn into the lab camera
            HaloCinemaView.LabMonitorCamera = r.Cam;
            yield return r.Frames(3);
            NoFog(); RenderCut();
            r.Shot("mon_overlay_t303", 960, 540);
            HaloCinemaView.LabMonitorCamera = null;
            yield return Ticks(50);

            // where the walkers ended (keyes_walk_1 = 350 ticks: done now)
            if (keyes != null)
            {
                Vector3 peer; float py;
                HaloCinematic.Flag("keyes_peer", out peer, out py);
                Vector3 k = keyes.transform.position;
                float d = new Vector2(k.x - peer.x, k.z - peer.z).magnitude;
                r.Check(d < 1.2f, "Keyes ended " + d.ToString("0.00") + " m from flag keyes_peer (script teleports him there next; harness dead-reckoning: 0.4-0.8 m), yaw " + keyes.transform.eulerAngles.y.ToString("0") + " vs flag " + py.ToString("0"));
                r.Check(HaloCinematic.RecordingTime(keyes.gameObject) == 0, "keyes' recording over");
            }
            if (crew1 != null) HaloLabRunner.Log("crew 1 at " + crew1.transform.position.ToString("F2") + " (harness end (1.75, 0.57) wu = " + HaloSpace.Pos(1.746f, 0.568f, 0f).ToString("F2") + ")");
            if (crew4 != null) HaloLabRunner.Log("crew 4 at " + crew4.transform.position.ToString("F2") + " (harness end (-0.37, 2.33) wu = " + HaloSpace.Pos(-0.374f, 2.325f, 0f).ToString("F2") + ")");

            HaloCinematic.Fade(false, Color.black, 15);
            yield return Ticks(8);
            SaveRt(r, view.Texture, "cine_fading_out");
            HaloCinematic.End();
            HaloCinematic.CameraControl(false);
            HaloCinematic.Clear();
            yield return r.Frames(2);
            r.Check(!HaloCinematic.Playing && HaloCinematic.RecordingsPlaying == 0, "cleared");
        }

        /// <summary>The mission's own start: K's StartMission and S's scripts (x10 begins with cinematic_start).</summary>
        static IEnumerator Script(HaloLabRunner r)
        {
            Time.captureFramerate = 60;
            HaloCampaign.StartMission("a10", HaloCampaign.Difficulty.Normal);
            yield return r.Wait(() => HaloCinematic.CameraControlled || r.Failed, 240f, "x10's camera_control");
            if (r.Failed) yield break;
            var view = HaloCinemaView.Instance;
            Vector3 feet0; HaloPlayer.TryFeet(out feet0);
            HaloLabRunner.Log("x10 took the camera at tick " + HaloCinematic.Ticks + "; player feet " + feet0.ToString("F1") + ", deferred teleport " + HaloCinematic.PlayerTeleportPending);
            for (int i = 0; i < 20; i++)
            {
                yield return Ticks(150);
                if (view != null && HaloCinematic.CameraControlled) SaveRt(r, view.Texture, "script_" + i.ToString("00") + "_" + (HaloCinematic.CameraPoint >= 0 ? HaloCinematic.Data.CameraPoints[HaloCinematic.CameraPoint].Name : "none"));
                HaloLabRunner.Log("tick " + HaloCinematic.Ticks + ": playing " + HaloCinematic.Playing + " camera " + HaloCinematic.CameraControlled + " letterbox " + HaloCinematic.Letterboxed + " fade " + HaloCinematic.FadeColour.a.ToString("0.00") + " recordings " + HaloCinematic.RecordingsPlaying + " title " + (HaloCinematic.CurrentTitle(out float ta) != null) + " player held " + HaloCinematic.HoldsPlayer + " pending tp " + HaloCinematic.PlayerTeleportPending + " feet " + (HaloPlayer.TryFeet(out Vector3 ff) ? ff.ToString("F1") : "?") + " bsp " + HaloWorld.ActiveBsp);
            }
        }
    }
}

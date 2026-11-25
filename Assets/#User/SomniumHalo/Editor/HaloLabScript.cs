using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Script module lab (DevTools/lab.sh script [-halomission a10] [-haloseconds 40]): starts the mission through HaloCampaign.StartMission
    /// (Mission K) and lets the map's own scripts run for some seconds of game time; logs which scripts started / slept forever / woke /
    /// ended (trace), the thread states, calls per function and the map's unimplemented list.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabScript
    {
        static HaloLabScript()
        {
            HaloLabRunner.Register("script", r => Run(r));
        }

        static readonly List<string> Events = new List<string>();

        static IEnumerator Seconds(float s)
        {
            float t = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < t) yield return null;
        }

        static void EnsurePlayer()
        {
            CharacterController sdk = Object.FindFirstObjectByType<CharacterController>();
            if (sdk != null)
            {
                foreach (MonoBehaviour b in sdk.GetComponents<MonoBehaviour>()) b.enabled = false;
                return;
            }
            var go = new GameObject("Lab Player");
            go.transform.position = HaloSpace.RoomOrigin;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            go.layer = HaloLayers.Avatar;
        }

        static IEnumerator Run(HaloLabRunner r)
        {
            EnsurePlayer();
            yield return r.Wait(() => HaloGame.Current != null && HaloGame.Current.Install != null, 120f, "the install");
            if (HaloGame.Current == null || HaloGame.Current.Install == null) { r.Fail("no install"); yield break; }
            string mission = HaloLabBoot.Arg("-halomission", "a10");
            float seconds = float.Parse(HaloLabBoot.Arg("-haloseconds", "40"), System.Globalization.CultureInfo.InvariantCulture);

            Events.Clear();
            HaloScript.Trace = s => { var vm = HaloScript.Vm; Events.Add("[" + (vm != null ? vm.Tick : 0) + "] " + s); };
            HaloCampaign.StartMission(mission, HaloCampaign.Difficulty.Normal);
            yield return r.Wait(() => HaloSession.Playing && HaloCampaign.Active, 900f, mission + ": the mission to start");
            if (!r.Check(HaloSession.Playing && HaloCampaign.Active, mission + ": playing")) yield break;
            if (!r.Check(HaloScript.Running && HaloScript.Vm != null, mission + ": scripts running")) yield break;
            var vm0 = HaloScript.Vm;
            HaloLabRunner.Log(mission + ": " + HaloScript.CoverageLine());

            int tick0 = vm0.Tick;
            float t0 = Time.realtimeSinceStartup;
            int shot = 0, postShots = 0; float lastShot = -100f;
            Vector3 stood; HaloPlayer.TryFeet(out stood);
            float maxMoveInCutscene = 0f; bool sawCutscene = false, cutsceneEnded = false; Vector3 after = Vector3.zero;
            float lastLog = 0f;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                yield return Seconds(0.5f);
                var vm = HaloScript.Vm;
                if (vm == null) break;
                Vector3 feet; bool haveFeet = HaloPlayer.TryFeet(out feet);
                if (HaloCinematic.HoldsPlayer) { sawCutscene = true; if (haveFeet) maxMoveInCutscene = Mathf.Max(maxMoveInCutscene, (feet - stood).magnitude); }
                else if (sawCutscene && !cutsceneEnded && haveFeet && Time.realtimeSinceStartup - t0 > 1f) { cutsceneEnded = true; HaloLabRunner.Log(mission + ": cutscene over at tick " + vm.Tick + ", player at " + feet.ToString("0.0")); }
                if (cutsceneEnded && haveFeet) after = feet;
                if (Time.realtimeSinceStartup - lastLog < 10f) continue;
                lastLog = Time.realtimeSinceStartup;
                HaloLabRunner.Log(mission + " player feet " + (haveFeet ? feet.ToString("0.0") : "?") + " (stood at " + stood.ToString("0.0") + ") holds " + HaloCinematic.HoldsPlayer);
                HaloLabRunner.Log(mission + " @" + vm.Tick + " ticks: " + HaloScript.ThreadsLine() + "; objective '" + HaloScriptFns.Objective + "' help '" + HaloScriptFns.HelpText + "' cinematic " + HaloCinematic.Playing);
                Camera head = HaloPlayer.Head();
                bool post = cutsceneEnded && postShots < 4;
                if (head != null && (post || (shot < 6 && Time.realtimeSinceStartup - lastShot > 40f))) { lastShot = Time.realtimeSinceStartup; if (post) postShots++; r.Aim(head.transform.position, head.transform.position + head.transform.forward * 10f, 75f); r.Shot(mission + "-script-" + (shot++) + (post ? "-after-intro" : "") + "-t" + vm.Tick); }
            }
            if (sawCutscene) r.Check(maxMoveInCutscene < 1.5f, mission + ": the player stayed where they stood during the cutscene (moved at most " + maxMoveInCutscene.ToString("0.00") + " m)");
            if (cutsceneEnded) { var sl = HaloSession.Scenario != null ? HaloSession.Scenario.StartLocations : null; HaloLabRunner.Log(mission + ": after the cutscene the player is at " + after.ToString("0.0") + (sl != null && sl.Count > 0 ? " (scenario start " + (HaloSpace.MapOrigin + HaloSpace.Pos(sl[0].Position)).ToString("0.0") + ")" : "")); }
            else if (sawCutscene) HaloLabRunner.Log(mission + ": the cutscene was still running when the lab ended");
            var v = HaloScript.Vm;
            if (v == null) { r.Fail("the VM went away"); yield break; }
            float rate = (v.Tick - tick0) / Mathf.Max(0.01f, Time.realtimeSinceStartup - t0);
            r.Check(rate > 20f && rate < 40f, mission + ": script ticks at " + rate.ToString("0.0") + " a second (" + (v.Tick - tick0) + " ticks)");
            int started = 0;
            foreach (var t in v.Threads) if (t.Starts > 0) started++;
            r.Check(started > 0, mission + ": " + started + " script threads ran");
            int mainIdx = v.ScriptIndex("mission_" + mission);
            if (mainIdx >= 0) r.Check(v.ThreadOf[mainIdx] != null && v.ThreadOf[mainIdx].Starts > 0, mission + ": the main script mission_" + mission + " started");

            HaloLabRunner.Log(mission + ": " + HaloScript.ThreadsLine());
            HaloLabRunner.Log(mission + ": " + HaloScript.CallsLine(40));
            HaloLabRunner.Log(mission + ": " + HaloScript.CoverageLine());
            for (int i = 0; i < Events.Count && i < 150; i++) HaloLabRunner.Log("trace " + Events[i]);
            if (Events.Count > 150) HaloLabRunner.Log("trace ... " + (Events.Count - 150) + " more");
            foreach (var t in v.Threads)
                if (t.Starts > 0) HaloLabRunner.Log("thread " + v.ScriptName(t.Script) + " " + t.Type + " " + t.State + " starts " + t.Starts + " sleeps " + t.Sleeps + " wakes " + t.Wakes + " wake@" + (t.WakeTick == int.MaxValue ? "never" : t.WakeTick.ToString()));
            HaloScript.Trace = null;
            HaloCampaign.LeaveMission();
            yield return Seconds(2f);
        }
    }
}

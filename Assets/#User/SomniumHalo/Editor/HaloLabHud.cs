using System.Collections;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// HUD lab (DevTools/lab.sh hud): a Slayer match on Blood Gulch, then the local player's state set by hand (it is re-applied every
    /// frame because Combat writes HaloLocal in its Update) and pictures of the monitor HUD (drawn into the lab camera's view at 16:9)
    /// and of the headset visor: full shields with the assault rifle 32 + 180 and grenades 2 / 1, low shields and half health with an
    /// almost empty rifle ("Reload"), the sniper rifle zoomed (scope mask, lines, rangefinder), the plasma pistol (battery, heat), dead
    /// ("Rejoin in 3"), the scoreboard, a team game (allies on the tracker), and the VR visor. Moving fake players feed the tracker.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabHud
    {
        static HaloLabHud() { HaloLabRunner.Register("hud", r => Run(r)); HaloLabRunner.Register("hud-campaign", r => Campaign(r)); }

        static void EnsurePlayer()
        {
            if (Object.FindFirstObjectByType<CharacterController>() != null) return;
            var go = new GameObject("Lab Player");
            go.transform.position = HaloSpace.RoomOrigin;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            go.layer = HaloLayers.Avatar;
        }

        sealed class State
        {
            public float Shield = 1f, Health = 1f; public bool Alive = true, Low, Recharging; public float Respawn;
            public string Weapon = "assault rifle"; public int Loaded = 32, Reserve = 180; public float Charge = 1f, Heat;
            public int Frag = 2, Plasma = 1, Selected; public bool Zoomed; public int ZoomLevel;
        }

        static HaloTag Weap(string name)
        {
            var map = HaloSession.Map;
            if (map == null || name == null) return null;
            foreach (var t in map.OfClass("weap")) if (t.Name.EndsWith("\\" + name)) return t;
            return null;
        }

        static void Apply(State s)
        {
            HaloLocal.Shield = s.Shield; HaloLocal.Health = s.Health; HaloLocal.Alive = s.Alive; HaloLocal.ShieldLow = s.Low; HaloLocal.ShieldRecharging = s.Recharging;
            HaloLocal.RespawnIn = s.Respawn;
            var w = HaloLocal.Weapons[0];
            var tag = Weap(s.Weapon);
            if (w == null || w.Tag != tag) { w = new HaloLocal.Weapon { Tag = tag, Label = s.Weapon }; HaloLocal.Weapons[0] = w; }
            w.Loaded = s.Loaded; w.Reserve = s.Reserve; w.Charge = s.Charge; w.Heat = s.Heat; w.Overheated = s.Heat >= 1f;
            HaloLocal.Held = 0;
            HaloLocal.FragGrenades = s.Frag; HaloLocal.PlasmaGrenades = s.Plasma; HaloLocal.GrenadeSelected = s.Selected;
            HaloLocal.Zoomed = s.Zoomed; HaloLocal.ZoomLevel = s.ZoomLevel;
            HaloLocal.Vehicle = null;
        }

        static Vector3 _eye, _fwd;
        static float _t;

        /// <summary>Moves the fake players (a circle walk ahead of the player) and re-applies the state for some frames.</summary>
        static IEnumerator Hold(State s, int frames, float minSeconds = 0f)
        {
            float until = Time.time + minSeconds;
            for (int i = 0; i < frames || Time.time < until; i++)
            {
                Apply(s);
                _t += Time.deltaTime;
                Vector3 right = new Vector3(_fwd.z, 0f, -_fwd.x);
                Fake(101, _eye + _fwd * 14f + right * (6f * Mathf.Sin(_t)), true);          // walking ahead, 14 m
                Fake(102, _eye + _fwd * 6f - right * 9f + _fwd * Mathf.Sin(_t * 1.3f) * 3f, true);   // left, running
                Fake(103, _eye - _fwd * 20f + right * 5f, false);                           // behind, standing (not on the tracker)
                Fake(104, _eye - _fwd * 12f - right * 10f + right * Mathf.Cos(_t) * 4f, true);       // behind left, moving
                yield return null;
            }
        }

        static void Fake(int id, Vector3 at, bool moving)
        {
            var p = HaloRoster.Remote(id);
            p.Name = id == 101 ? "Sergeant Johnson" : id == 102 ? "Spartan-117" : id == 103 ? "Keyes" : "Cortana";
            p.Alive = true;
            if (moving || p.Position == Vector3.zero) p.Position = at;
            p.Kills = id % 5; p.Deaths = id % 3; p.Assists = 1; p.Score = p.Kills;
            p.Team = HaloSession.Rules != null && HaloSession.Rules.Teams ? (id % 2 == 0 ? HaloLocal.Team : 1 - HaloLocal.Team) : HaloTeams.None;
            p.Color = HaloRoster.ColorFor(id, p.Team, HaloSession.Rules != null && HaloSession.Rules.Teams);
            p.Seen = Time.time;
        }

        static IEnumerator Run(HaloLabRunner r)
        {
            EnsurePlayer();
            if (HaloLocal.NetId < 0) HaloLocal.NetId = 7;
            HaloLocal.Nick = "Lab Chief";
            var rules = HaloRules.Defaults(HaloScenario.GameType.Slayer, false);
            rules.MapName = HaloLabBoot.Arg("-halomap", "bloodgulch");
            rules.TimeLimitMinutes = 10;
            HaloMatchControl.StartMatch(rules);
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Playing, 600f, "the match to start");
            if (r.Failed) yield break;
            if (!HaloHud.Running) HaloHud.Start();
            r.Check(HaloHud.Running, "the HUD runs (started by the match)");
            if (HaloLocal.NetId < 0) HaloLocal.NetId = 7;      // offline the net layer leaves -1: a slot so "You killed" lines run
            r.Check(HaloHudKit.Unit != null && HaloHudKit.Globals != null && HaloHudKit.Globals.Digits != null, "unit HUD, hud globals and digits read: " + (HaloHudKit.Unit != null ? HaloHudKit.Unit.Tag.Name : "none"));
            r.Check(HaloHudKit.Font != null && HaloHudKit.MpText.Count > 100, "font and " + HaloHudKit.MpText.Count + " multiplayer strings");

            Vector3 feet;
            if (!HaloPlayer.TryFeet(out feet)) feet = Vector3.zero;
            float yaw; string src;
            if (!HaloPlayer.TryFacingYaw(out yaw, out src)) yaw = 0f;
            _fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            _eye = feet + Vector3.up * 1.7f;
            r.Aim(_eye, _eye + _fwd * 10f - Vector3.up * 0.6f, 70f);
            HaloLocal.Position = feet; HaloLocal.Forward = _fwd;
            HaloHud.LabAttach(r.Cam, false);

            var s = new State();
            yield return Hold(s, 90);
            HaloEvents.RaiseMessage(HaloHud.Hmt("pickup", "E", "Shotgun") ?? "Hold E to pick up Shotgun", 6f);
            HaloEvents.RaiseKilled(new HaloKill { Killer = HaloLocal.NetId, Victim = 102 });
            HaloEvents.RaiseKilled(new HaloKill { Killer = HaloLocal.NetId, Victim = 104 });
            HaloEvents.RaiseKilled(new HaloKill { Killer = 101, Victim = 103 });
            yield return Hold(s, 40, 1.6f);
            r.Shot("monitor-full", 1280, 720);
            HaloLabRunner.Log("tracker: " + (HaloHud.View != null ? HaloHud.View.TrackerDebug() : "no view") + "; net id " + HaloLocal.NetId);
            r.Check(HaloHud.View != null, "view exists");

            // low shields, half health, an almost empty rifle with rounds left: "Reload"; a hit from the left
            s.Shield = 0.12f; s.Low = true; s.Health = 0.45f; s.Loaded = 6; s.Reserve = 40; s.Frag = 0; s.Selected = 1;
            HaloHud.Damaged(_eye - new Vector3(_fwd.z, 0f, -_fwd.x) * 5f, 1f);
            yield return Hold(s, 12);
            r.Shot("monitor-low", 1280, 720);

            // the sniper rifle zoomed
            s = new State { Weapon = "sniper rifle", Loaded = 3, Reserve = 12, Zoomed = true, ZoomLevel = 1 };
            r.Cam.fieldOfView = 70f / 2f;
            yield return Hold(s, 20);
            r.Shot("monitor-sniper", 1280, 720);
            r.Cam.fieldOfView = 70f;

            // the plasma pistol (battery 64 %, heat 40 %) and the pistol
            s = new State { Weapon = "plasma pistol", Charge = 0.64f, Heat = 0.4f, Frag = 4, Plasma = 4 };
            yield return Hold(s, 20);
            r.Shot("monitor-plasma", 1280, 720);
            s = new State { Weapon = "pistol", Loaded = 12, Reserve = 36, Shield = 1f };
            yield return Hold(s, 12);
            r.Shot("monitor-pistol", 1280, 720);

            // dead: "Rejoin in 3"
            s = new State { Alive = false, Respawn = 2.4f };
            yield return Hold(s, 12);
            r.Shot("monitor-dead", 1280, 720);

            // the scoreboard
            s = new State();
            HaloHud.ShowScoreboard(true);
            yield return Hold(s, 20);
            r.Shot("monitor-scores", 1280, 720);
            HaloHud.ShowScoreboard(false);

            // the headset visor (the lab camera as the head)
            HaloHud.LabAttach(r.Cam, true);
            r.Cam.fieldOfView = 90f;
            yield return Hold(new State(), 40, 1.2f);
            r.Shot("vr-full", 1600, 1600);      // about a headset eye: ~18 pixels per degree over 90 degrees
            r.Cam.fieldOfView = 70f;

            for (int id = 101; id <= 104; id++) HaloRoster.Remove(id);
            HaloMatchControl.LeaveMatch();
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Hub, 10f, "back to the hub");
            r.Check(!HaloHud.Running, "the HUD stopped with the match");
        }

        static IEnumerator Wait(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) yield return null; }

        static string HudState()
        {
            var v = HaloHud.View;
            return "hidden " + HaloHud.Hidden + ", cinematic " + HaloCinematic.Playing + ", alive " + HaloLocal.Alive + ", help '" + HaloScriptFns.HelpText + "', objective '" + HaloScriptFns.Objective
                   + "', nav points " + HaloHud.NavPoints.Count + ", timer " + HaloHud.TimerShown + ", units " + HaloBipeds.All.Count + ", " + (v != null ? v.TrackerDebug().Split(';')[0] : "no view");
        }

        /// <summary>DevTools/lab.sh hud-campaign [-halomission a10] : the mission from its scripts; the HUD hidden in the intro cutscene, then
        /// (skipped) the campaign HUD with the scripts' help / objective, nav points and the AI on the motion tracker.</summary>
        static IEnumerator Campaign(HaloLabRunner r)
        {
            EnsurePlayer();
            string mission = HaloLabBoot.Arg("-halomission", "a10");
            HaloCampaign.StartMission(mission, HaloCampaign.Difficulty.Normal);
            yield return r.Wait(() => HaloSession.Playing && HaloCampaign.Active, 900f, mission + ": the mission to start");
            if (r.Failed) yield break;
            r.Check(HaloHud.Running, mission + ": the HUD runs (unit hud " + (HaloHudKit.Unit != null ? HaloHudKit.Unit.Tag.Name : "none") + ")");
            yield return Wait(6f);
            Camera head = HaloPlayer.Head();
            HaloLabRunner.Log(mission + " at 6 s: " + HudState());
            if (head != null) r.Aim(head.transform.position, head.transform.position + head.transform.forward * 10f, 75f);
            HaloHud.LabAttach(r.Cam, false);
            yield return null;
            r.Shot(mission + "-hud-1", 1280, 720);
            if (HaloCinematic.Playing) { bool sk = HaloCinematic.Skip(); HaloLabRunner.Log(mission + ": skip the cutscene -> " + sk); }
            for (int i = 2; i <= 4; i++)
            {
                yield return Wait(i == 2 ? 4f : 12f);
                head = HaloPlayer.Head();
                if (head != null) r.Aim(head.transform.position, head.transform.position + head.transform.forward * 10f, 75f);
                HaloLabRunner.Log(mission + " shot " + i + ": " + HudState());
                yield return null;
                r.Shot(mission + "-hud-" + i, 1280, 720);
            }
            // the scripts' HUD API by hand: nav point ahead, timer, blinking shield
            head = HaloPlayer.Head();
            Vector3 ahead = head != null ? head.transform.position + head.transform.forward * 15f : Vector3.zero;
            HaloHud.ShowHud(true);
            HaloHud.NavPoint("lab", "default_red", ahead, null, 0.5f);
            HaloHud.TimerSet(1, 30); HaloHud.TimerWarning(2, 0); HaloHud.TimerPosition(0, 0, "bottom_right"); HaloHud.TimerShow(true);
            yield return Wait(1.5f);
            r.Shot(mission + "-hud-api", 1280, 720);
            r.Check(HaloHud.TimerTicks > 0 && HaloHud.TimerTicks < 90 * 30, "timer counts down: " + HaloHud.TimerTicks + " ticks");
            HaloHud.ClearNavPoint("lab"); HaloHud.TimerShow(false);
            HaloCampaign.LeaveMission();
            yield return Wait(3f);
            r.Check(!HaloHud.Running, "the HUD stopped with the mission");
        }
    }
}

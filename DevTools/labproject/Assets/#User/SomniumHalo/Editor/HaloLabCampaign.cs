using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Mission module lab (DevTools/lab.sh campaign [-halomission a10,a30]): for each mission StartMission(m, Normal) from code - the loading
    /// phase, the world, the scenario's objects placed with their names, the player at the campaign start with the starting profile, the
    /// scripts ticking at 30 Hz, a door opening (by itself for a door, by device_set_position otherwise) and closing, a control used, a
    /// checkpoint + a death -> revert (player, a destroyed object and a device back), a BSP switch volume (multi-BSP maps), back to the hub.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabCampaign
    {
        static HaloLabCampaign()
        {
            HaloLabRunner.Register("campaign", r => Run(r));
        }

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
                foreach (MonoBehaviour b in sdk.GetComponents<MonoBehaviour>()) { b.enabled = false; HaloLabRunner.Log("lab player: disabled " + b.GetType().Name + " on " + sdk.name); }
                return;
            }
            var go = new GameObject("Lab Player");
            go.transform.position = HaloSpace.RoomOrigin;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            go.layer = HaloLayers.Avatar;
        }

        static Vector3 Feet() { Vector3 f; return HaloPlayer.TryFeet(out f) ? f : Vector3.zero; }

        static Vector3 Ground(Vector3 p)
        {
            RaycastHit h;
            if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out h, 6f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) return h.point + Vector3.up * 0.02f;
            return p;
        }

        static void Put(Vector3 p, float yaw)
        {
            string how;
            HaloCombat.NoteTeleport();
            HaloPlayer.Teleport(p, yaw, out how);
            Physics.SyncTransforms();
        }

        static void Views(HaloLabRunner r, string name, Vector3 feet, float yaw)
        {
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            r.Aim(feet + Vector3.up * 1.7f, feet + Vector3.up * 1.5f + fwd * 10f, 75f);
            r.Shot(name + "-eye");
            r.Aim(feet + Vector3.up * 5f - fwd * 6f, feet + Vector3.up * 0.8f, 65f);
            r.Shot(name + "-above");
        }

        static IEnumerator Run(HaloLabRunner r)
        {
            EnsurePlayer();
            yield return r.Wait(() => HaloGame.Current != null && HaloGame.Current.Install != null, 120f, "the install");
            if (HaloGame.Current == null || HaloGame.Current.Install == null) { r.Fail("no install"); yield break; }
            string list = HaloLabBoot.Arg("-halomission", "a10,a30");
            foreach (string m in list.Split(','))
            {
                if (m.Trim().Length == 0) continue;
                yield return Mission(r, m.Trim());
            }
        }

        static IEnumerator Mission(HaloLabRunner r, string mission)
        {
            bool sawLoading = false;
            System.Action<HaloSession.Phase, HaloSession.Phase> watch = (a, b) => { if (b == HaloSession.Phase.Loading) sawLoading = true; };
            HaloSession.PhaseChanged += watch;
            float t0 = Time.realtimeSinceStartup;
            HaloCampaign.StartMission(mission, HaloCampaign.Difficulty.Normal);
            yield return r.Wait(() => HaloSession.Playing && HaloCampaign.Active, 900f, mission + ": the mission to start");
            HaloSession.PhaseChanged -= watch;
            if (!r.Check(HaloSession.Playing && HaloCampaign.Active, mission + ": playing (" + HaloCampaign.Mission + ", " + HaloCampaign.Name(HaloCampaign.Level) + ") after " + (Time.realtimeSinceStartup - t0).ToString("0.0") + " s")) yield break;
            r.Check(sawLoading, mission + ": went through the loading phase (loading screen shown: " + HaloLoading.Showing + " now)");
            r.Check(HaloWorld.Ready, mission + ": world ready, BSP " + HaloCampaign.BspIndex + ", bounds " + HaloWorld.Bounds.min.ToString("0") + ".." + HaloWorld.Bounds.max.ToString("0"));
            var data = HaloCampaign.Data;
            r.Check(data != null && HaloObjects.Count > 0 && HaloObjects.NamedCount > 0, mission + ": " + HaloObjects.Count + " objects placed, " + HaloObjects.NamedCount + " with names, " + HaloDevices.Count + " devices");
            if (data == null) yield break;
            string names = ""; int shown = 0;
            foreach (var o in data.Objects)
            {
                if (o.Name.Length == 0) continue;
                var g = HaloObjects.Find(o.Name);
                if (g == null) continue;
                if (shown++ < 12) names += (names.Length > 0 ? ", " : "") + o.Name + " (" + o.Kind + ")";
            }
            HaloLabRunner.Log(mission + ": named objects found by name: " + names);

            // the player at the campaign start with the starting profile
            yield return Seconds(1.5f);
            Vector3 feet = Feet();
            var sl = HaloSession.Scenario.StartLocations;
            Vector3 start = sl.Count > 0 ? HaloSpace.MapOrigin + HaloSpace.Pos(sl[0].Position) : Vector3.zero;
            Vector3 dd = feet - start; dd.y = 0f;
            r.Check(sl.Count > 0 && dd.magnitude < 1.0f, mission + ": the player stands on the start (" + dd.magnitude.ToString("0.00") + " m away, at " + feet.ToString("0.0") + ")");
            var prof = data.Profiles.Count > 0 ? data.Profiles[0] : null;
            string want = prof == null ? "none" : (prof.Primary != null ? prof.Primary.Name : "-") + " + " + (prof.Secondary != null ? prof.Secondary.Name : "-") + ", " + prof.Frag + "/" + prof.Plasma;
            string have = (HaloLocal.Weapons[0] != null && HaloLocal.Weapons[0].Tag != null ? HaloLocal.Weapons[0].Tag.Name + " " + HaloLocal.Weapons[0].Loaded + "/" + HaloLocal.Weapons[0].Reserve : "-") + " + "
                          + (HaloLocal.Weapons[1] != null && HaloLocal.Weapons[1].Tag != null ? HaloLocal.Weapons[1].Tag.Name + " " + HaloLocal.Weapons[1].Loaded + "/" + HaloLocal.Weapons[1].Reserve : "-") + ", " + HaloLocal.FragGrenades + "/" + HaloLocal.PlasmaGrenades;
            bool profOk = prof == null
                || ((prof.Primary == null || (HaloLocal.Weapons[0] != null && HaloLocal.Weapons[0].Tag == prof.Primary) || (HaloLocal.Weapons[1] != null && HaloLocal.Weapons[1].Tag == prof.Primary))
                    && HaloLocal.FragGrenades == prof.Frag && HaloLocal.PlasmaGrenades == prof.Plasma);
            r.Check(HaloLocal.Alive && profOk, mission + ": alive with the starting profile '" + (prof != null ? prof.Name : "?") + "' (wants " + want + "; has " + have + ")");
            float yaw; string src;
            HaloPlayer.TryFacingYaw(out yaw, out src);
            Views(r, mission + "-start", feet, yaw);

            // scripts ticking at 30 Hz
            int ticks = HaloCampaign.Ticks;
            float tt = Time.realtimeSinceStartup;
            yield return Seconds(2f);
            float rate = (HaloCampaign.Ticks - ticks) / Mathf.Max(0.01f, Time.realtimeSinceStartup - tt);
            r.Check(rate > 20f && rate < 40f, mission + ": game ticks at " + rate.ToString("0.0") + " a second (script running " + HaloScript.Running + ")");

            // the mission's own scripts move on (a10 switches to its space BSP for the intro): for the device / revert checks the lab stops
            // them and goes back to the start BSP
            HaloLabRunner.Log(mission + ": after 2 s of scripts: BSP " + HaloCampaign.BspIndex + ", cinematic " + HaloCinematic.Playing + ", " + HaloObjects.Count + " objects (" + HaloObjects.NamedCount + " named)");
            HaloScript.Stop();
            HaloCinematic.Clear();
            int startBsp = sl.Count > 0 ? sl[0].BspIndex : 0;
            if (HaloCampaign.BspIndex != startBsp)
            {
                HaloCampaign.SwitchBsp(startBsp);
                yield return r.Wait(() => HaloCampaign.BspIndex == startBsp && !HaloCampaign.SwitchingBsp, 120f, mission + ": back to the start BSP");
                r.Check(HaloCampaign.BspIndex == startBsp && HaloWorld.ActiveBsp == startBsp, mission + ": SwitchBsp(" + startBsp + ") - the World shows BSP " + HaloWorld.ActiveBsp);
            }
            Put(start, sl.Count > 0 ? 90f - sl[0].Facing * Mathf.Rad2Deg : 0f);
            yield return Seconds(0.5f);
            feet = Feet();
            HaloPlayer.TryFacingYaw(out yaw, out src);
            Views(r, mission + "-startbsp", feet, yaw);

            // game_speed, the countdown timer, a scenery animation
            yield return Extras(r, mission);

            // a door
            yield return Door(r, mission);

            // a control (when the mission has one)
            yield return Control(r, mission);

            // checkpoint + death -> revert
            yield return Revert(r, mission, data);

            // a BSP switch volume
            yield return BspSwitch(r, mission, data);

            HaloCampaign.LeaveMission();
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Hub && !HaloCampaign.Active, 20f, mission + ": back to the hub");
            yield return null;
            r.Check(!HaloCampaign.Active && HaloSession.Map == null && GameObject.Find("Halo Mission " + mission) == null && HaloObjects.Count == 0, mission + ": left - the mission root and objects are gone");
        }

        static IEnumerator Extras(HaloLabRunner r, string mission)
        {
            int t0 = HaloCampaign.Ticks; float s0 = Time.realtimeSinceStartup;
            HaloCampaign.Speed = 5f;
            yield return Seconds(1f);
            float rate = (HaloCampaign.Ticks - t0) / (Time.realtimeSinceStartup - s0);
            HaloCampaign.Speed = 1f;
            r.Check(rate > 100f, mission + ": game_speed 5 -> " + rate.ToString("0") + " game ticks a second");
            HaloCampaign.CountdownSet(65000, true);
            yield return Seconds(1f);
            long left = HaloCampaign.CountdownMs;
            HaloCampaign.CountdownStop();
            r.Check(left < 64500 && left > 63000, mission + ": countdown 65 s -> " + left + " ms after 1 s, digits " + HaloCampaign.CountdownDigit(0) + HaloCampaign.CountdownDigit(1) + ":" + HaloCampaign.CountdownDigit(2) + HaloCampaign.CountdownDigit(3));

            if (mission != "b30") yield break;
            var g = HaloObjects.Find("rock_kick");
            if (g == null) { HaloLabRunner.Log(mission + ": rock_kick is not in the start BSP - object_create"); g = HaloObjects.Create("rock_kick"); if (g != null && !g.activeSelf) g.SetActive(true); }
            if (!r.Check(g != null, mission + ": scenery 'rock_kick' placed")) yield break;
            HaloTag graph = HaloSession.Map.Find("antr", @"scenery\cutscene_small_rock\cutscene_small_rock");
            var mo = g.GetComponent<HaloMissionObject>();
            Transform node = mo != null && mo.Built != null && mo.Built.Nodes != null && mo.Built.Nodes.Length > 0 ? mo.Built.Nodes[0] : null;
            Vector3 before = node != null ? node.position : Vector3.zero;
            Vector3 eye = g.transform.position + new Vector3(6f, 3f, 6f);
            r.Aim(eye, g.transform.position, 60f);
            r.Shot(mission + "-rock-before");
            bool ok = HaloObjects.AnimateScenery("rock_kick", graph, "clip01-rockfalling", 0);
            r.Check(ok, mission + ": scenery_animation_start rock_kick clip01-rockfalling (" + (graph != null ? graph.Name : "no graph") + ")");
            yield return Seconds(3f);
            var an = g.GetComponent<HaloObjectAnimator>();
            float moved = node != null ? (node.position - before).magnitude : 0f;
            r.Check(an != null && an.Frame > 80f && moved > 1f, mission + ": the animation runs - frame " + (an != null ? an.Frame.ToString("0") : "-") + ", root node moved " + moved.ToString("0.00") + " m");
            r.Aim(eye, g.transform.position, 60f);
            r.Shot(mission + "-rock-after");
        }

        static HaloDevice NearestDevice(System.Func<HaloDevice, bool> pick, Vector3 from)
        {
            HaloDevice best = null; float bd = 1e9f;
            foreach (var d in HaloDevices.All)
            {
                if (d == null || !d.isActiveAndEnabled || !pick(d)) continue;
                float dist = (d.Center - from).sqrMagnitude;
                if (dist < bd) { bd = dist; best = d; }
            }
            return best;
        }

        static IEnumerator Door(HaloLabRunner r, string mission)
        {
            Vector3 start = Feet();
            HaloDevice door = NearestDevice(d => d.Def.IsDoor && d.Automatic && d.Power > 0f && d.HasAnimation && d.Position < 0.01f, start);
            if (door != null)
            {
                Vector3 front = door.transform.right; front.y = 0f; front = front.sqrMagnitude > 0.01f ? front.normalized : Vector3.forward;
                Vector3 far = Ground(door.Center + front * 5f);
                float yaw = Quaternion.LookRotation(-front).eulerAngles.y;
                Put(far, yaw);
                yield return Seconds(1.0f);
                r.Check(door.Position < 0.01f, mission + ": door '" + door.Label + "' (" + door.Def + ") closed while nobody is near (position " + door.Position.ToString("0.00") + ")");
                r.Aim(far + Vector3.up * 1.7f, door.Center, 70f);
                r.Shot(mission + "-door-closed");
                Put(Ground(door.Center + front * 0.9f), yaw);
                float until = Time.realtimeSinceStartup + 4f + door.Def.PositionTransition;
                while (door.Position < 0.99f && Time.realtimeSinceStartup < until) yield return null;
                r.Check(door.Position >= 0.99f, mission + ": the door opened for the player standing at it (position " + door.Position.ToString("0.00") + ")");
                Put(far, yaw);
                yield return Seconds(0.3f);
                r.Aim(far + Vector3.up * 1.7f, door.Center, 70f);
                r.Shot(mission + "-door-open");
                until = Time.realtimeSinceStartup + door.Def.DoorOpenTime + door.Def.PositionTransition + 3f;
                while (door.Position > 0.01f && Time.realtimeSinceStartup < until) yield return null;
                r.Check(door.Position <= 0.01f, mission + ": the door closed again " + door.Def.DoorOpenTime.ToString("0.0") + " s after the player left (position " + door.Position.ToString("0.00") + ")");
            }
            else HaloLabRunner.Log(mission + ": no automatic door with power and animation in this BSP");

            // a machine by script (device_set_position)
            HaloDevice m = NearestDevice(d => d.Def.IsMachine && d.HasAnimation && d.Power > 0f && d.Placement.Name.Length > 0 && d.Position < 0.01f && d.Def.PositionTransition > 0f, start);
            if (m != null)
            {
                string nm = m.Placement.Name;
                HaloDevices.SetPosition(nm, 1f);
                Vector3 eye = Ground(m.Center + m.transform.right * 4f) + Vector3.up * 1.7f;
                float until = Time.realtimeSinceStartup + m.Def.PositionTransition + 3f;
                while (HaloDevices.Position(nm) < 0.99f && Time.realtimeSinceStartup < until) yield return null;
                r.Check(HaloDevices.Position(nm) >= 0.99f, mission + ": device_set_position '" + nm + "' 1 -> position " + HaloDevices.Position(nm).ToString("0.00") + " (" + m.Def + ")");
                r.Aim(eye, m.Center, 70f);
                r.Shot(mission + "-machine-" + nm.Replace(' ', '_') + "-open");
                HaloDevices.SetPositionImmediate(nm, 0f);
                r.Check(HaloDevices.Position(nm) <= 0.01f, mission + ": device_set_position_immediate '" + nm + "' 0");
            }
            else HaloLabRunner.Log(mission + ": no named machine to move by script");
            Put(start, 0f);
            yield return Seconds(0.5f);
        }

        static IEnumerator Control(HaloLabRunner r, string mission)
        {
            HaloDevice c = NearestDevice(d => d.Def.IsControl, Feet());
            if (c == null) { HaloLabRunner.Log(mission + ": no control in this BSP"); yield break; }
            float before = c.Desired;
            int uses = HaloDevices.Uses;
            HaloDevices.LabUse = c;
            yield return Seconds(0.3f);
            r.Check(HaloDevices.Uses == uses + 1 || c.Power <= 0f, mission + ": control '" + c.Label + "' (" + c.Def + ", power " + c.Power.ToString("0.##") + ") used: desired " + before.ToString("0.##") + " -> " + c.Desired.ToString("0.##"));
            Vector3 eye = Ground(c.Center + c.transform.right * 2f) + Vector3.up * 1.6f;
            r.Aim(eye, c.Center, 60f);
            r.Shot(mission + "-control");
        }

        static IEnumerator Revert(HaloLabRunner r, string mission, HaloMissionData data)
        {
            Vector3 saved = Feet();
            HaloCampaign.SaveTotallyUnsafe();
            r.Check(HaloCheckpoints.HasSave && HaloCheckpoints.Label == "game_save_totally_unsafe", mission + ": checkpoint taken (" + HaloCheckpoints.Taken + " so far)");
            // after the checkpoint: the player walks away, a named object goes, a named machine moves
            string gone = null;
            foreach (var o in data.Objects)
                if (o.Name.Length > 0 && o.Kind == HaloMissionData.Kind.Scenery && HaloObjects.Find(o.Name) != null) { gone = o.Name; break; }
            if (gone != null) HaloObjects.Destroy(gone);
            HaloDevice m = NearestDevice(d => d.Def.IsMachine && d.Placement.Name.Length > 0 && d.Position < 0.01f, saved);
            string mn = m != null ? m.Placement.Name : null;
            if (mn != null) HaloDevices.SetPositionImmediate(mn, 1f);
            Put(Ground(saved + new Vector3(4f, 0f, 3f)), 90f);
            yield return Seconds(0.5f);
            r.Check(gone == null || HaloObjects.Find(gone) == null, mission + ": object_destroy '" + gone + "' after the checkpoint");
            // death -> revert
            int reverts = HaloCheckpoints.Reverts;
            HaloLocal.Alive = false;
            HaloEvents.RaiseLocalDied(new HaloKill { Killer = -1, Victim = HaloLocal.NetId });
            yield return null;
            r.Check(HaloCampaign.PlayerDead, mission + ": the player died - revert in " + HaloLocal.RespawnIn.ToString("0.0") + " s");
            yield return r.Wait(() => HaloCheckpoints.Reverts > reverts && HaloLocal.Alive, HaloCampaign.RevertDelay + 10f, mission + ": the revert");
            yield return Seconds(0.5f);
            Vector3 f = Feet();
            Vector3 d3 = f - saved; d3.y = 0f;
            r.Check(HaloLocal.Alive && d3.magnitude < 1f, mission + ": reverted - alive at the checkpoint (" + d3.magnitude.ToString("0.00") + " m from it)");
            r.Check(gone == null || HaloObjects.Find(gone) != null, mission + ": '" + gone + "' is back after the revert");
            r.Check(mn == null || HaloDevices.Position(mn) < 0.01f, mission + ": machine '" + mn + "' is back at its checkpoint position (" + (mn != null ? HaloDevices.Position(mn).ToString("0.00") : "-") + ")");
            float yaw; string src;
            HaloPlayer.TryFacingYaw(out yaw, out src);
            Views(r, mission + "-reverted", f, yaw);
        }

        static IEnumerator BspSwitch(HaloLabRunner r, string mission, HaloMissionData data)
        {
            int cur = HaloCampaign.BspIndex;
            HaloMissionData.BspSwitch sw = null;
            foreach (var s in data.BspSwitches) if (s.Source == cur && s.TriggerVolume >= 0 && s.TriggerVolume < data.TriggerVolumes.Count) { sw = s; break; }
            if (sw == null) { HaloLabRunner.Log(mission + ": no BSP switch volume from BSP " + cur); yield break; }
            var v = data.TriggerVolumes[sw.TriggerVolume];
            Vector3 c = HaloSpace.MapOrigin + HaloSpace.Pos(v.Center);
            r.Check(HaloTriggers.Contains(v, c), mission + ": the volume '" + v.Name + "' contains its own centre");
            Put(c - Vector3.up * 0.9f, 0f);
            yield return r.Wait(() => HaloCampaign.BspIndex == sw.Destination && !HaloCampaign.SwitchingBsp, 120f, mission + ": the BSP switch " + cur + " -> " + sw.Destination);
            r.Check(HaloCampaign.BspIndex == sw.Destination && HaloWorld.Ready, mission + ": BSP " + HaloCampaign.BspIndex + " active (world ready " + HaloWorld.Ready + ", bounds " + HaloWorld.Bounds.min.ToString("0") + ".." + HaloWorld.Bounds.max.ToString("0") + ", " + HaloObjects.Count + " objects)");
            Views(r, mission + "-bsp" + sw.Destination, c - Vector3.up * 0.9f, 0f);
        }
    }
}

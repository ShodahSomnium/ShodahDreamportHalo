using System.Collections;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// "audio-sfx" (-halomission a30 -haloenc cave_floor -haloallies first_marine/left_back -haloseconds 40): the real mission flow, an
    /// enemy encounter placed 14 m in front of the player and allied marines beside it fighting, then the player in a warthog for a few
    /// seconds. Logs the Audio coverage line (per sound class: map tags, one-shots asked / started / skipped / dropped, loops; classes
    /// never asked for) before the fight and after.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabAudioSfx
    {
        static HaloLabAudioSfx() { HaloLabRunner.Register("audio-sfx", r => Run(r)); }

        static IEnumerator Secs(float s) { float t = Time.realtimeSinceStartup + s; while (Time.realtimeSinceStartup < t) yield return null; }

        static IEnumerator Run(HaloLabRunner r)
        {
            string mission = HaloLabBoot.Arg("-halomission", "a30");
            string enc = HaloLabBoot.Arg("-haloenc", "cave_floor");
            string allies = HaloLabBoot.Arg("-haloallies", "first_marine/left_back");
            float seconds = float.Parse(HaloLabBoot.Arg("-haloseconds", "40"), System.Globalization.CultureInfo.InvariantCulture);
            HaloCampaign.StartMission(mission, HaloCampaign.Difficulty.Normal);
            yield return r.Wait(() => HaloSession.Playing && HaloCampaign.Active, 900f, mission + " to start");
            if (!r.Check(HaloSession.Playing, mission + " playing")) yield break;
            yield return Secs(5f);
            HaloLabRunner.Log("coverage before the fight: " + HaloAudio.Coverage());

            Vector3 feet; HaloPlayer.TryFeet(out feet);
            var head = HaloPlayer.Head();
            Vector3 fwd = head != null ? Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
            HaloAi.Place(enc);
            yield return null;
            var foes = HaloAi.Units(enc);
            for (int i = 0; i < foes.Count; i++) foes[i].Teleport(HaloActor.OnNav(feet + fwd * 14f + Vector3.Cross(Vector3.up, fwd) * (i * 2f - foes.Count)), 180f);
            HaloAi.Place(allies);
            yield return null;
            var friends = HaloAi.Units(allies);
            for (int i = 0; i < friends.Count; i++) friends[i].Teleport(HaloActor.OnNav(feet + Quaternion.Euler(0f, 120f * i, 0f) * fwd * 2.5f), 0f);
            HaloAi.FollowTargetPlayers(allies);
            HaloLabRunner.Log("placed " + foes.Count + " of " + enc + " 14 m ahead, " + friends.Count + " of " + allies + " beside the player");
            yield return Secs(seconds);
            int alive = 0; foreach (var u in foes) if (u != null && u.Alive) alive++;
            // pain, death and a friend hurt by the player (unit voice)
            for (int i = 0; i < foes.Count && i < 3; i++) if (foes[i] != null && foes[i].Alive) { foes[i].TakeHit(new HaloHit { Amount = 15f, SourcePlayer = 0, Point = foes[i].transform.position + Vector3.up, Direction = fwd }); yield return Secs(0.3f); }
            for (int i = 3; i < foes.Count && i < 5; i++) if (foes[i] != null && foes[i].Alive) { foes[i].TakeHit(new HaloHit { Amount = 1e5f, SourcePlayer = 0, Explosion = i == 4, Point = foes[i].transform.position + Vector3.up, Direction = fwd }); yield return Secs(0.3f); }
            if (friends.Count > 0 && friends[0] != null && friends[0].Alive) friends[0].TakeHit(new HaloHit { Amount = 2f, SourcePlayer = 0, Point = friends[0].transform.position + Vector3.up, Direction = fwd });
            yield return Secs(2f);
            HaloLabRunner.Log("unit voice: " + HaloUnitVoice.Stats());
            HaloLabRunner.Log("after " + seconds + " s: " + alive + "/" + foes.Count + " enemies alive; " + HaloAudio.Diag());

            // a vehicle: the first scenario vehicle placement, the player in its driver seat
            var sc = HaloSession.Scenario;
            if (sc != null && sc.Vehicles.Count > 0)
            {
                var v = HaloVehicles.SpawnPlacement(sc.Vehicles[0], "lab_audio_vehicle", HaloSession.WorldRoot);
                if (v != null)
                {
                    v.transform.position = HaloActor.OnNav(feet + fwd * -6f) + Vector3.up * 0.5f;
                    yield return Secs(1f);
                    bool inside = HaloVehicleSeat.Local.GetIn(v, 0);
                    HaloLabRunner.Log("vehicle " + (sc.Vehicles[0].Type != null ? sc.Vehicles[0].Type.Name : "?") + " spawned, player in: " + inside);
                    yield return Secs(5f);
                    HaloVehicleSeat.Local.GetOut("lab");
                    yield return Secs(1f);
                }
            }
            HaloLabRunner.Log("coverage after: " + HaloAudio.Coverage());
            HaloCampaign.LeaveMission();
            yield return Secs(2f);
        }
    }
}

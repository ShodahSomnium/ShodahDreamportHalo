using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Footsteps lab ("steps", -halomap bloodgulch | chillout ...): starts a Slayer match, finds a spot of every floor material the
    /// map has (a grid of rays down), walks the stand-in player across each (Halo run pace and a walk), drops it from 3 m (landing),
    /// and runs a remote walker beside it. The "Audio: step ..." lines give the material -> sound table.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabAudioSteps
    {
        static HaloLabAudioSteps() { HaloLabRunner.Register("steps", r => Run(r)); }

        static Transform Player()
        {
            var cc = Object.FindFirstObjectByType<CharacterController>();
            if (cc != null)
            {
                foreach (MonoBehaviour b in cc.GetComponents<MonoBehaviour>()) b.enabled = false;
                return cc.transform;
            }
            var go = new GameObject("Lab Player");
            var c = go.AddComponent<CharacterController>();
            c.height = 1.8f; c.radius = 0.3f; c.center = new Vector3(0f, 0.9f, 0f);
            go.layer = HaloLayers.Avatar;
            return go.transform;
        }

        static void Place(Transform p, Vector3 feet)
        {
            var cc = p.GetComponent<CharacterController>();
            bool was = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            p.position = feet;
            if (cc != null) cc.enabled = was;
        }

        static IEnumerator Walk(Transform p, Vector3 from, Vector3 dir, float speed, float seconds)
        {
            float t0 = Time.realtimeSinceStartup;
            Vector3 at = from;
            Place(p, at);
            yield return null;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                at += dir * speed * Time.unscaledDeltaTime;
                float y; int m;
                if (HaloFootsteps.Ground(at + Vector3.up * 0.5f, out y, out m)) at.y = y;
                Place(p, at);
                yield return null;
            }
        }

        static IEnumerator Run(HaloLabRunner r)
        {
            string map = HaloLabBoot.Arg("-halomap", "bloodgulch");
            var player = Player();
            if (Object.FindFirstObjectByType<AudioListener>() == null) r.Cam.gameObject.AddComponent<AudioListener>();
            HaloMatchControl.StartMatch(new HaloRules { Type = HaloScenario.GameType.Slayer, MapName = map });
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Playing, 600f, "the match");
            if (!r.Check(HaloSession.Playing && HaloWorld.Ready, "playing " + map)) yield break;
            float w0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - w0 < 2f) yield return null;
            HaloLabRunner.Log("alive " + HaloLocal.Alive + ", vehicle " + (HaloLocal.Vehicle != null) + "; " + HaloAudio.Stats());

            // a spot of each floor material: rays down on a grid
            var spots = new Dictionary<int, Vector3>();
            var counts = new Dictionary<int, int>();
            Bounds b = HaloWorld.Bounds;
            var hits = new RaycastHit[4];
            for (float x = b.min.x + 2f; x < b.max.x; x += 3f)
                for (float z = b.min.z + 2f; z < b.max.z; z += 3f)
                {
                    int n = Physics.RaycastNonAlloc(new Vector3(x, b.max.y + 5f, z), Vector3.down, hits, b.size.y + 10f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore);
                    int best = -1; float bd = float.MaxValue;
                    for (int i = 0; i < n; i++) if (hits[i].distance < bd) { bd = hits[i].distance; best = i; }
                    if (best < 0) continue;
                    int m = HaloWorld.SurfaceMaterial(hits[best]);
                    int c; counts.TryGetValue(m, out c); counts[m] = c + 1;
                    // a spot whose 6 m to +x is the same material (so a walk stays on it)
                    if (spots.ContainsKey(m)) continue;
                    float y2; int m2;
                    if (HaloFootsteps.Ground(hits[best].point + new Vector3(6f, 0.3f, 0f), out y2, out m2) && m2 == m && Mathf.Abs(y2 - hits[best].point.y) < 1f)
                        spots[m] = hits[best].point;
                }
            var sb = new System.Text.StringBuilder();
            foreach (var kv in counts) sb.Append(HaloFootsteps.MaterialName(kv.Key)).Append(" x").Append(kv.Value).Append("  ");
            HaloLabRunner.Log("floor materials (grid hits): " + sb);

            int before = HaloFootsteps.Played;
            foreach (var kv in spots)
            {
                int p0 = HaloFootsteps.Played;
                yield return Walk(player, kv.Value, Vector3.right, 6.5f, 1.2f);     // Halo run pace
                yield return Walk(player, kv.Value + Vector3.right * 6f, Vector3.left, 1.6f, 2f);    // a walk back
                float y; int gm; HaloFootsteps.Ground(kv.Value + Vector3.up, out y, out gm);
                HaloLabRunner.Log("walked on " + HaloFootsteps.MaterialName(kv.Key) + ": " + (HaloFootsteps.Played - p0) + " steps played; run -> "
                    + Name(HaloFootsteps.SoundFor(HaloFootsteps.Run, kv.Key)) + ", walk -> " + Name(HaloFootsteps.SoundFor(HaloFootsteps.Walk, kv.Key)) + "; " + HaloFootsteps.Stats());
                float t = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t < 0.4f) yield return null;
            }
            r.Check(HaloFootsteps.Played - before >= 6, "own steps played: " + (HaloFootsteps.Played - before));

            // a drop from 3 m: landing
            Vector3 g = new List<Vector3>(spots.Values)[0];
            int l0 = HaloFootsteps.Lands;
            Place(player, g); for (int i = 0; i < 10; i++) yield return null;
            float drop = 3f;
            Place(player, g + Vector3.up * drop); yield return null; yield return null;
            while (drop > 0f) { drop -= 7f * Time.unscaledDeltaTime; Place(player, g + Vector3.up * Mathf.Max(0f, drop)); yield return null; }
            for (int i = 0; i < 10; i++) yield return null;
            r.Check(HaloFootsteps.Lands > l0, "landing after a 3 m drop (" + HaloFootsteps.Stats() + ")");

            // a remote player running past (the same walker the remote players get)
            var remote = new HaloFootsteps.Walker();
            int r0 = HaloFootsteps.Played;
            Vector3 at = g + Vector3.forward * 3f;
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 2f)
            {
                at += Vector3.right * 6.5f * Time.unscaledDeltaTime;
                float y; int m; if (HaloFootsteps.Ground(at + Vector3.up * 0.5f, out y, out m)) at.y = y;
                remote.Update(at, at + Vector3.up * 1.7f, Time.unscaledDeltaTime, false);
                yield return null;
            }
            r.Check(remote.Steps >= 3, "remote walker: " + remote.Steps + " steps at " + remote.Speed.ToString("0.0") + " m/s, stride " + remote.LastStride.ToString("0.00") + " m");

            // the whole table of the map's foot tag
            var tb = new System.Text.StringBuilder();
            for (int m = 0; m < HaloFootsteps.MaterialNames.Length; m++)
            {
                var s = HaloFootsteps.SoundFor(HaloFootsteps.Run, m);
                if (s != null) tb.Append(HaloFootsteps.MaterialName(m)).Append("=").Append(s.Name.Replace("sound\\sfx\\impulse\\footsteps\\cyborg\\", "")).Append("  ");
            }
            HaloLabRunner.Log("run table: " + tb);
            HaloLabRunner.Log("steps: " + HaloFootsteps.Stats());
            HaloMatchControl.LeaveMatch();
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Hub, 10f, "back to the hub");
        }

        static string Name(HaloTag t) => t != null ? t.Name : "(none)";
    }
}

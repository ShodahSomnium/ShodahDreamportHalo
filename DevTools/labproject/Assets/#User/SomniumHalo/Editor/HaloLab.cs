using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// The play-mode test bench, run from the command line (batch mode WITH a graphics device; DevTools/lab.sh does it):
    ///   Unity -batchmode -projectPath ... -halolab &lt;scenario&gt; -halout D:\dir [-halomap bloodgulch] -logFile lab.log
    /// It opens Assets/Halo Scene.unity, enters play mode (the real HaloGame boots: in the editor the whitelist always lets in), adds
    /// the runner, and runs the scenario: pictures with <see cref="HaloLabRunner.Shot"/>, "LAB:" log lines, then exits (code 1 on a
    /// failure or an exception). Each module adds its own scenarios in its own Editor/HaloLab*.cs file:
    ///   [InitializeOnLoad] static class MyLab { static MyLab() { HaloLabRunner.Register("myname", r => MyScenario(r)); } ... }
    /// </summary>
    [InitializeOnLoad]
    public static class HaloLabBoot
    {
        static HaloLabBoot()
        {
            if (Arg("-halolab", null) == null) return;
            EditorApplication.playModeStateChanged += OnState;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                EditorSceneManager.OpenScene("Assets/Halo Scene.unity");
                EditorApplication.EnterPlaymode();
            };
        }

        public static string Arg(string name, string fallback)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return fallback;
        }

        private static void OnState(PlayModeStateChange s)
        {
            if (s != PlayModeStateChange.EnteredPlayMode) return;
            var go = new GameObject("Halo Lab");
            go.AddComponent<HaloLabRunner>();
        }
    }

    public sealed class HaloLabRunner : MonoBehaviour
    {
        static readonly Dictionary<string, Func<HaloLabRunner, IEnumerator>> Scenarios = new Dictionary<string, Func<HaloLabRunner, IEnumerator>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Adds a scenario (call from a static constructor of an [InitializeOnLoad] class).</summary>
        public static void Register(string name, Func<HaloLabRunner, IEnumerator> run) { Scenarios[name] = run; }

        public Camera Cam { get; private set; }
        public string Out { get; private set; }
        public string Scenario { get; private set; }
        public bool Failed { get; set; }
        public HaloGame Game { get { return HaloGame.Current; } }

        private void Start()
        {
            Application.runInBackground = true;
            Out = HaloLabBoot.Arg("-halout", @"D:\Dreamport\Halo\Halo Dreamport\DevTools\lab");
            Scenario = HaloLabBoot.Arg("-halolab", "boot");
            Directory.CreateDirectory(Out);
            var go = new GameObject("Lab Camera");
            Cam = go.AddComponent<Camera>();
            Cam.nearClipPlane = 0.05f; Cam.farClipPlane = 5000f; Cam.fieldOfView = 70f;
            Cam.clearFlags = CameraClearFlags.SolidColor; Cam.backgroundColor = new Color(0.1f, 0.12f, 0.16f);
            Cam.enabled = false;
            StartCoroutine(Guarded(Main()));
        }

        public static void Log(string s) { Debug.Log("LAB: " + s); }
        public void Fail(string why) { Log("FAIL " + why); Failed = true; }
        public bool Check(bool ok, string what) { Log((ok ? "ok   " : "FAIL ") + what); if (!ok) Failed = true; return ok; }

        /// <summary>Steps nested IEnumerators itself so an exception anywhere ends the lab (Unity would just stop the inner coroutine and hang).</summary>
        private IEnumerator Guarded(IEnumerator body)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(body);
            float started = Time.realtimeSinceStartup;
            float limit = float.Parse(HaloLabBoot.Arg("-halotimeout", "900"), System.Globalization.CultureInfo.InvariantCulture);
            while (stack.Count > 0)
            {
                if (Time.realtimeSinceStartup - started > limit) { Log("TIMEOUT: the scenario ran over " + limit + " s"); Failed = true; break; }
                object cur;
                try
                {
                    IEnumerator top = stack.Peek();
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    cur = top.Current;
                }
                catch (Exception e)
                {
                    Log("EXCEPTION " + e);
                    Failed = true;
                    break;
                }

                var nested = cur as IEnumerator;
                if (nested != null) { stack.Push(nested); continue; }
                yield return cur;
            }

            Log("done " + (Failed ? "(FAILED)" : "(passed)"));
            EditorApplication.Exit(Failed ? 1 : 0);
        }

        /// <summary>Waits until <paramref name="cond"/> is true; marks the lab failed after <paramref name="timeout"/> seconds.</summary>
        public IEnumerator Wait(Func<bool> cond, float timeout, string what)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond())
            {
                if (Time.realtimeSinceStartup - t0 > timeout) { Log("TIMEOUT waiting for " + what + " (status: " + (Game != null ? Game.Status : "no game") + ")"); Failed = true; yield break; }
                yield return null;
            }
        }

        public IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        private IEnumerator Main()
        {
            yield return Wait(() => HaloGame.Current != null && HaloGame.Current.Install != null, 120f, "the boot (install found)");
            if (Failed) yield break;
            Log("booted: " + Game.Status);
            if (Scenario == "boot") yield break;
            Func<HaloLabRunner, IEnumerator> run;
            if (!Scenarios.TryGetValue(Scenario, out run)) { Fail("unknown scenario '" + Scenario + "' (known: " + string.Join(", ", Scenarios.Keys) + ")"); yield break; }
            yield return run(this);
        }

        /// <summary>Places the lab camera (Unity space).</summary>
        public void Aim(Vector3 eye, Vector3 lookAt, float fov = 70f)
        {
            Cam.transform.position = eye;
            Vector3 d = lookAt - eye;
            Cam.transform.rotation = d.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(d, Vector3.up) : Quaternion.identity;
            Cam.fieldOfView = fov;
        }

        /// <summary>Renders the lab camera to Out/&lt;name&gt;.png (width x height) and returns the average colour (logged).</summary>
        public Color Shot(string name, int width = 960, int height = 540)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            Cam.targetTexture = rt;
            Cam.Render();
            Cam.targetTexture = null;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = tex.GetPixels32();
            long r = 0, g = 0, b = 0;
            for (int i = 0; i < px.Length; i += 7) { r += px[i].r; g += px[i].g; b += px[i].b; }
            int n = (px.Length + 6) / 7;
            var avg = new Color(r / (255f * n), g / (255f * n), b / (255f * n));
            string path = Path.Combine(Out, name + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            Log("picture " + path + " average " + avg.r.ToString("0.00") + "," + avg.g.ToString("0.00") + "," + avg.b.ToString("0.00"));
            return avg;
        }
    }
}

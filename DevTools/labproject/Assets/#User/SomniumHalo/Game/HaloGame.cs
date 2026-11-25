using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The scene's root ("Halo Game" object). Access first (private testing): Awake sets the log sinks and runs the gate -
    /// nothing below starts before Core/HaloAccess says the player is on the whitelist; a visitor gets only Game/Access/HaloIsolation
    /// (every other player hidden and silent), no text, no ring, no menu. Then <see cref="StartDreamport"/>: shader carriers, the
    /// ring hub with the menu, the tablet app, the instance/net layer, and on a worker thread the look for the player's own Halo install.
    ///
    /// Modules hook in through the partial methods below (each lives in its own HaloGame.*.cs file); a module that is not there
    /// compiles away. Everything a module does is called inside try/catch: a missing piece becomes a status line, never a crash.
    /// </summary>
    public sealed partial class HaloGame : MonoBehaviour
    {
        [Tooltip("Every carrier material of a custom shader (Resources/Halo*.mat). Shaders reach the upload only through them.")]
        [SerializeField] private Material[] _shaderCarriers;

        [Tooltip("Where Halo is installed (the folder with halo.exe and MAPS).")]
        [SerializeField] private string _installHint = @"C:\Program Files (x86)\Microsoft Games\Halo";

        public static HaloGame Current { get; private set; }

        private bool _started, _isolating;
        private HaloInstall _install;

        // Boot (worker thread -> main thread).
        private Thread _bootThread;
        private volatile bool _bootDone;
        private volatile string _bootError;
        private volatile string _bootHintProblem;     // why the folder set in Settings was not used (null when it was, or none was set)
        private HaloInstall _bootInstall;
        private long _bootMs;
        private bool _bootHandled;

        public HaloInstall Install { get { return _install; } }

        /// <summary>Looks for the install again (the player set the folder after "not found"); false while a search is still running.</summary>
        public bool Rescan()
        {
            if (_install != null || !_bootDone || !_bootHandled) return false;
            _bootHandled = false;
            StartBoot();
            return true;
        }
        public string InstallHint { get { return _installHint; } }
        public Material[] ShaderCarriers { get { return _shaderCarriers; } }

        /// <summary>The menu-side status line (the hub's panel shows it; set by boot and by loads).</summary>
        public string Status { get; private set; }
        public bool StatusIsError { get; private set; }
        public event Action<string, bool> StatusChanged;

        public void SetStatus(string text, bool error)
        {
            Status = text; StatusIsError = error;
            try { var h = StatusChanged; if (h != null) h(text, error); }
            catch (Exception e) { HaloLog.Warn("Status: " + e.Message); }
        }

        // ------------------------------------------------------------------ module hooks (implemented in HaloGame.*.cs)

        /// <summary>Front end (Game/Front): the ring hub, the room menu, the tablet app, the loading screen.</summary>
        partial void FrontStart();
        partial void FrontBootDone();
        partial void FrontTick();
        partial void FrontStop();

        /// <summary>Net + instance (Game/Net): the slot layer, the instance master, the lobby.</summary>
        partial void NetStart();
        partial void NetTick();
        partial void NetStop();

        /// <summary>The match (Game/Match): map load, players, rules.</summary>
        partial void MatchTick();
        partial void MatchStop();

        /// <summary>The campaign (Game/Campaign): mission flow, scripts / AI ticking, devices, checkpoints.</summary>
        partial void CampaignTick();
        partial void CampaignStop();

        private readonly List<KeyValuePair<int, string>> _heldLog = new List<KeyValuePair<int, string>>();

        private void Awake()
        {
            // A visitor must see "nothing about Halo": the world's log lines are held back until the whitelist verdict -
            // flushed to Player.log for a whitelisted player, dropped (and nothing more written) for a visitor.
            _heldLog.Clear();
            HaloLog.Sink = s => HoldLog(0, s);
            HaloLog.WarnSink = s => HoldLog(1, s);
            HaloLog.ErrorSink = s => HoldLog(2, s);
            StartCoroutine(Gate());
        }

        private void HoldLog(int level, string line)
        {
            if (_heldLog.Count < 500) _heldLog.Add(new KeyValuePair<int, string>(level, line));
        }

        private void ReleaseLog(bool allowed)
        {
            if (!allowed)
            {
                HaloLog.Sink = null;
                HaloLog.WarnSink = null;
                HaloLog.ErrorSink = null;
                _heldLog.Clear();
                return;
            }

            HaloLog.Sink = Debug.Log;
            HaloLog.WarnSink = Debug.LogWarning;
            HaloLog.ErrorSink = Debug.LogError;
            foreach (var kv in _heldLog)
            {
                if (kv.Key == 0) Debug.Log(kv.Value);
                else if (kv.Key == 1) Debug.LogWarning(kv.Value);
                else Debug.LogError(kv.Value);
            }

            _heldLog.Clear();
        }

        /// <summary>
        /// The whitelist gate (Core/HaloAccess). While the client has not reported the username, and for good when the player is
        /// not on the list, only the isolation runs with the rule "nobody is with me": every other player is hidden and silent
        /// on this screen, and NOTHING of the Dreamport is built, read, added, loaded or saved.
        /// </summary>
        private IEnumerator Gate()
        {
            IEnumerator check = HaloAccess.Check();
            bool waited = false;
            while (check.MoveNext())
            {
                if (!_isolating) StartIsolation(NobodyRule("waiting for the whitelist check"));
                if (!waited)
                {
                    waited = true;
                    ShowPlainFloor();
                }

                yield return check.Current;
            }

            if (!HaloAccess.Allowed)
            {
                StartIsolation(NobodyRule("visitor (not on the whitelist)"));
                ShowPlainFloor();
                ReleaseLog(false);
                yield break;
            }

            ReleaseLog(true);
            HidePlainFloor();
            StartDreamport();
        }

        // ------------------------------------------------------------------ the visitor's plane

        private GameObject _plainFloor;

        /// <summary>Plain grey walkable plane under the spawn point: all a visitor (or anyone before the verdict) ever sees.</summary>
        private void ShowPlainFloor()
        {
            if (_plainFloor != null) return;
            try
            {
                _plainFloor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                _plainFloor.name = "Floor";
                _plainFloor.transform.SetParent(transform, false);
                _plainFloor.transform.position = HaloSpace.RoomOrigin;
                _plainFloor.transform.rotation = Quaternion.identity;
                _plainFloor.transform.localScale = new Vector3(6f, 1f, 6f);
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
                var r = _plainFloor.GetComponent<Renderer>();
                if (r != null && sh != null)
                {
                    var m = new Material(sh);
                    Color grey = new Color(0.42f, 0.42f, 0.42f, 1f);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", grey);
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
                    r.sharedMaterial = m;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Floor: " + e.Message);
            }
        }

        private void HidePlainFloor()
        {
            if (_plainFloor == null) return;
            var r = _plainFloor.GetComponent<Renderer>();
            if (r != null && r.sharedMaterial != null) Destroy(r.sharedMaterial);
            Destroy(_plainFloor);
            _plainFloor = null;
        }

        private HaloIsolation.Rule NobodyRule(string who)
        {
            return new HaloIsolation.Rule
            {
                Name = who + ": nobody is with you",
                IsWithMe = id => false,
                WithText = "with you",
                ApartText = "not shown to a visitor",
                WorldRoot = transform,
            };
        }

        /// <summary>A whitelisted player's rule: the other player is on the whitelist too (see HaloGear). Everyone else is a visitor: hidden and silent.</summary>
        private HaloIsolation.Rule WhitelistRule()
        {
            return new HaloIsolation.Rule
            {
                Name = "whitelisted: other whitelisted players",
                IsWithMe = HaloGear.HasRecord,
                WithText = "is on the whitelist (a Dreamport player)",
                ApartText = "not on the whitelist (a visitor)",
                ShowDelay = 0f,
                DecideInterval = 0f,
                WorldRoot = transform,
            };
        }

        private void StartIsolation(HaloIsolation.Rule rule)
        {
            try
            {
                HaloIsolation.Start(rule);
                _isolating = true;
                AddOnce<HaloIsolationDriver>();
            }
            catch (Exception e)
            {
                HaloLog.Error("Access: starting the isolation failed: " + e);
            }
        }

        public T AddOnce<T>() where T : Component
        {
            try
            {
                T c = GetComponent<T>();
                if (c == null) c = gameObject.AddComponent<T>();
                return c;
            }
            catch (Exception e)
            {
                HaloLog.Error("Awake: adding " + typeof(T).Name + " failed: " + e);
                return null;
            }
        }

        // ------------------------------------------------------------------ the Dreamport

        /// <summary>Everything the world does, for a whitelisted player only.</summary>
        private void StartDreamport()
        {
            _started = true;
            Current = this;
            HaloLog.Info("Halo Dreamport " + HaloVersion.Label + ": " + HaloVersion.Summary);
            HaloShaders.Carry(_shaderCarriers);
            StartIsolation(WhitelistRule());
            SetStatus("Looking for your Halo install...", false);
            Guard("front start", () => FrontStart());
            Guard("net start", () => NetStart());
            StartBoot();
        }

        /// <summary>Runs a module call, logging (not throwing) a failure.</summary>
        public static void Guard(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { HaloLog.Error(what + ": " + e); }
        }

        /// <summary>Worker thread: find the install and its maps (disk only, no Unity API).</summary>
        private void StartBoot()
        {
            string hint = HaloSettings.InstallOverride;
            if (string.IsNullOrEmpty(hint)) hint = _installHint;
            _bootDone = false;
            _bootError = null;
            _bootThread = new Thread(() => Boot(hint)) { IsBackground = true, Name = "Halo boot" };
            _bootThread.Start();
        }

        private void Boot(string hint)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                HaloInstall inst = HaloInstall.Find(hint);
                _bootHintProblem = HaloInstall.HintProblem;
                if (inst == null)
                    _bootError = _bootHintProblem != null
                        ? "Halo was not found: " + _bootHintProblem + ". Set the folder with halo.exe and MAPS in Settings."
                        : "Halo was not found. Install Halo: Combat Evolved for PC (the folder with halo.exe and MAPS), or set the folder in Settings.";
                else _bootInstall = inst;
            }
            catch (Exception e)
            {
                _bootError = "Reading the install failed: " + e.Message;
            }

            _bootMs = sw.ElapsedMilliseconds;
            _bootDone = true;
        }

        private void Update()
        {
            if (!_started) return;
            if (_bootDone && !_bootHandled)
            {
                _bootHandled = true;
                FinishBoot();
            }

            Guard("net tick", () => NetTick());
            Guard("front tick", () => FrontTick());
            Guard("match tick", () => MatchTick());
            Guard("campaign tick", () => CampaignTick());
        }

        private void FinishBoot()
        {
            if (_bootError != null)
            {
                HaloLog.Warn("Boot: " + _bootError);
                SetStatus(_bootError, true);
                return;
            }

            _install = _bootInstall;
            HaloLog.Info("Boot: install " + _install.Root + ", " + _install.Multiplayer.Count + " multiplayer maps (" + _bootMs + " ms)");
            if (_bootHintProblem != null) HaloLog.Warn("Boot: the folder set in Settings was not used (" + _bootHintProblem + "); found the install in a usual place instead");
            SetStatus("Halo found: " + _install.Root + "  -  " + _install.Multiplayer.Count + " multiplayer maps", false);
            Guard("front boot", () => FrontBootDone());
        }

        private void OnDestroy()
        {
            Guard("campaign stop", () => CampaignStop());
            Guard("match stop", () => MatchStop());
            Guard("net stop", () => NetStop());
            Guard("front stop", () => FrontStop());
            // First and on its own: other players' hidden renderers, colliders and voices must be restored even if a later
            // step throws (idempotent: the driver's OnDestroy calls it too). Then the verdict is forgotten (statics survive).
            try
            {
                HaloRemotes.Reset();
            }
            catch (Exception e)
            {
                HaloLog.Error("Access: isolation shutdown failed: " + e);
            }

            HaloAccess.Reset();
            _isolating = false;
            if (Current == this) Current = null;
        }
    }
}

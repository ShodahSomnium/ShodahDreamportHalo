using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] The "HALO" app on the Somnium tablet (BO2's Bo2TabletApp, like the ETS 2 / WoW apps): the same menu page the hub shows
    /// (HaloMenuPanel filled by HaloFront.AddPage) on the home screen of the client's own tablet, VR and desktop (<see cref="HaloTabletLink"/> finds
    /// and borrows it). The hub's panel always stays too. In a match the page's column switches to SCORES / TEAM / MATCH (leave, and for the master:
    /// end the game for everyone, a new game on another map). A match starting closes the app and the client's tablet.
    /// HaloGame.OnDestroy -> FrontStop -> <see cref="Shutdown"/> takes everything off the client's tablet (it outlives the world).
    /// Steady state: a few property reads a frame, nothing allocated.
    /// </summary>
    [DefaultExecutionOrder(1001)]
    public sealed class HaloTabletApp : MonoBehaviour
    {
        public static HaloTabletApp Instance { get; private set; }

        public HaloTabletLink Link { get { return _link; } }
        public HaloMenuPanel Page { get { return _panel; } }

        private HaloGame _game;
        private HaloHub _hub;
        private HaloTabletLink _link;
        private HaloMenuPanel _panel;
        private Texture2D _icon;
        private bool _wasLoading, _tornDown;

        public static HaloTabletApp Create(HaloGame game, HaloHub hub)
        {
            if (Instance != null) Shutdown();
            HaloTabletApp app = game.gameObject.AddComponent<HaloTabletApp>();
            app._game = game;
            app._hub = hub;
            app.Setup();
            return app;
        }

        public static void Shutdown()
        {
            HaloTabletApp app = Instance;
            if (app != null) app.Teardown();
        }

        private void Awake() { Instance = this; }

        private void Setup()
        {
            _icon = HaloTabletIcon.Paint(128);
            _link = _game.gameObject.AddComponent<HaloTabletLink>();
            _link.AppIcon = _icon;
            _link.FrameWidth = HaloMenuPanel.Width;
            _link.FrameHeight = HaloMenuPanel.Height;
            _link.LinkChanged += OnLinkChanged;
            HaloLog.Info("Tablet: searching for the Somnium tablet (the \"" + _link.AppTitle + "\" app goes on its home screen; the hub panel stays the menu too).");
        }

        private void OnDestroy()
        {
            Teardown();
            if (Instance == this) Instance = null;
        }

        private void Teardown()
        {
            if (_tornDown) return;
            _tornDown = true;
            enabled = false;
            try
            {
                if (_link != null)
                {
                    _link.LinkChanged -= OnLinkChanged;
                    _link.Shutdown();
                    Destroy(_link);
                }

                if (_icon != null) Destroy(_icon);
            }
            catch (Exception e)
            {
                HaloLog.Warn("Tablet: shutting the app down failed: " + e.GetType().Name + ": " + e.Message);
            }

            _link = null;
            _icon = null;
            _panel = null;
            if (Instance == this) Instance = null;
            if (this != null) Destroy(this);
        }

        private void Update()
        {
            if (_game == null || _link == null) return;
            bool linked = _link.IsLinked;
            if (_panel != null && _panel.Root == null) _panel = null;
            if (linked && _panel == null && _link.Frame != null) BuildPage();

            bool loading = HaloSession.Current == HaloSession.Phase.Loading;
            if (linked && loading && !_wasLoading) _link.CloseTablet("a match is starting");
            _wasLoading = loading;
            _link.AppAvailable = !loading;
            HaloCombat.TabletOpen = linked && _link.TabletOpen;      // (Combat: no firing while the Somnium tablet is open)
            if (_panel != null) _panel.SetGameMode(HaloSession.InMatch, HaloCampaign.Active);
        }

        private void BuildPage()
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            _panel = HaloMenuPanel.Build(_link.Frame, HaloMenuPanel.Width, HaloMenuPanel.Height, HaloMenuPanel.Tab.Multiplayer, "Tablet menu", _game.Status, _game.StatusIsError, true);
            HaloFront.AddPage(_panel, true);
            _link.ApplyLayers();
            HaloLog.Info("Tablet: the menu page was built into the app frame in " + watch.ElapsedMilliseconds + " ms.");
        }

        /// <summary>Widgets added to the page later (the lobby once ui.map is read) go onto the tablet's UI layer.</summary>
        private void LateUpdate()
        {
            if (_link != null && _panel != null && Time.frameCount % 30 == 0) _link.ApplyLayers();
        }

        private void OnLinkChanged()
        {
            if (_link == null || !_link.IsLinked) _panel = null;
        }
    }

    /// <summary>The app's home-screen icon, drawn here (nothing of the game): a navy rounded slab with Halo's blue ring and its rim (ours).</summary>
    public static class HaloTabletIcon
    {
        public static Texture2D Paint(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Halo.Tablet.IconTexture", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[n * n];
            Color slab = new Color(0.0f, 0.05f, 0.12f, 1f), blue = HaloUi.Blue, light = new Color(0.75f, 0.9f, 1f, 1f);
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x - c) / n, v = (y - c) / n;
                    float box = RoundBox(u, v, 0.47f, 0.47f, 0.17f);
                    Color col = slab;
                    col = Color.Lerp(col, blue, Cover(Mathf.Abs(box + 0.028f) - 0.010f, n));
                    // the ring seen at a slant (an ellipse band), the far side thinner
                    float ex = u / 0.36f, ey = (v + 0.02f) / 0.16f;
                    float e = Mathf.Sqrt(ex * ex + ey * ey);
                    float band = Mathf.Abs(e - 1f) - (v < -0.02f ? 0.16f : 0.08f);
                    col = Color.Lerp(col, light, Cover(band * 0.16f, n));
                    col = Color.Lerp(col, blue, Cover((Mathf.Abs(e - 1f) - 0.04f) * 0.16f, n) * 0.6f);
                    col.a = Cover(box, n);
                    pixels[y * n + x] = col;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }

        private static float Cover(float d, int n) { return Mathf.Clamp01(0.5f - d * n); }

        private static float RoundBox(float x, float y, float hw, float hh, float r)
        {
            float qx = Mathf.Abs(x) - hw + r, qy = Mathf.Abs(y) - hh + r;
            float o = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            return o + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }
    }
}

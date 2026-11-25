using System;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// How a cutscene is shown. A camera ("Halo Cinematic Camera") renders the scripted camera into a 4:3 picture (Halo's own frame;
    /// the camera point's field of view is Halo's horizontal one) whenever camera_control is on. Over it, in Halo's 640 x 480 layout:
    /// the letterbox bars (1/8 of the height each - the chapter titles sit in the bottom bar as their bounds 425..446 say), the title
    /// (Halo's large_ui font, text colour + shadow, fade in / up / fade out), the screen-effect wash and the fade colour.
    ///   VR: the player's view never moves - the picture is a 3.6 m screen 3.2 m ahead (ours) at the head's height and yaw when the
    ///       camera is taken, riding with the avatar root (teleports, lifts), re-centred when the head stays turned away; the world around
    ///       is dimmed by a dark box; drawn over everything (ZTest Always). Fades and titles outside camera control: a head-locked layer.
    ///   Monitor: a screen overlay - the picture fitted 4:3 on black, bars across the screen, title, fade (the client camera keeps running
    ///       underneath, nothing of it is changed: nothing to restore).
    /// Created on first use by HaloCinematic; lives under its own object (no allocations per frame).
    /// </summary>
    public sealed class HaloCinemaView : MonoBehaviour
    {
        public static HaloCinemaView Instance { get; private set; }
        /// <summary>Lab: present as in a headset with this transform as the head.</summary>
        public static bool ForceVr;
        public static Transform LabHead;
        /// <summary>Lab: draw the monitor overlay into this camera (screen-space camera canvas) so a picture can see it.</summary>
        public static Camera LabMonitorCamera;

        public const int SortDim = 31000, SortScreen = 31001, SortHead = 31002, SortOverlay = 100;
        const float FrameW = 640f, FrameH = 480f, Bar = 60f;
        const float ScreenWidth = 3.6f, ScreenDistance = 3.2f; // ours
        const float HeadDistance = 0.6f;

        Camera _cam;
        RenderTexture _rt;
        int _uiLayer = 5;
        Material _onTop;
        HaloFont _font;
        HaloMap _fontMap;

        // layers: monitor overlay, VR screen, VR head
        Layer _overlay, _screen, _head;
        Canvas _overlayCanvas, _screenCanvas, _headCanvas, _dimCanvas;
        // VR screen anchor: yaw and offset from the avatar root
        float _screenYaw; Vector3 _screenOffset; bool _screenPlaced; float _awayTime;
        Camera _prevWorldView, _prevFxView;
        bool _viewsTaken;

        public RenderTexture Texture => _rt;
        public Camera CutsceneCamera => _cam;

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("Halo Cinematic");
            var parent = HaloGame.Current != null ? HaloGame.Current.transform : null;
            if (parent != null) go.transform.SetParent(parent, false);
            Instance = go.AddComponent<HaloCinemaView>();
        }

        void Awake()
        {
            Instance = this;
            HaloSession.Unloading += OnUnloading;
        }

        void OnDestroy()
        {
            HaloSession.Unloading -= OnUnloading;
            ReleaseViews();
            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
            if (_overlay != null) _overlay.Destroy();
            if (_screen != null) _screen.Destroy();
            if (_head != null) _head.Destroy();
            if (Instance == this) Instance = null;
        }

        static void OnUnloading() { HaloCinematic.Clear(); }

        void Update()
        {
            HaloCinematic.SelfClock(Time.deltaTime);
            HaloCinematic.Frame();
        }

        void LateUpdate()
        {
            try
            {
                HaloCinematic.RecordingsFrame();
                Present();
            }
            catch (Exception e)
            {
                if (!_saidError) { _saidError = true; HaloLog.Error("Cinematic: view: " + e); }
            }
        }

        bool _saidError, _saidMode;

        // ------------------------------------------------------------------ frame

        void Present()
        {
            bool vr = ForceVr || HaloPlayer.XrPresent;
            bool pic = HaloCinematic.CameraControlled;
            Color fade = HaloCinematic.FadeColour;
            float titleAlpha;
            var title = HaloCinematic.CurrentTitle(out titleAlpha);
            bool letter = HaloCinematic.Letterboxed;
            Color wash = pic ? HaloCinematic.ScreenWash() : Color.clear;
            bool any = pic || fade.a > 0.001f || title != null || letter;

            if (!_saidMode && any) { _saidMode = true; HaloLog.Info("Cinematic: presenting on " + (vr ? "the VR cinema screen (" + ScreenWidth + " m at " + ScreenDistance + " m)" : "the monitor overlay")); }

            // the cutscene camera
            if (pic) CameraFrame(vr); else if (_cam != null && _cam.enabled) { _cam.enabled = false; ReleaseViews(); }

            if (!any)
            {
                Show(_overlayCanvas, false); Show(_screenCanvas, false); Show(_headCanvas, false); Show(_dimCanvas, false);
                _screenPlaced = false;
                return;
            }
            EnsureFont();
            if (vr)
            {
                Show(_overlayCanvas, false);
                if (pic)
                {
                    BuildScreen();
                    PlaceScreen();
                    Show(_screenCanvas, true); Show(_dimCanvas, true); Show(_headCanvas, false);
                    _screen.Apply(true, _rt, letter, title, titleAlpha, wash, fade, _font);
                }
                else
                {
                    _screenPlaced = false;
                    BuildHead();
                    PlaceHead();
                    Show(_screenCanvas, false); Show(_dimCanvas, false); Show(_headCanvas, true);
                    _head.Apply(false, null, false, title, titleAlpha, Color.clear, fade, _font);
                }
            }
            else
            {
                Show(_screenCanvas, false); Show(_dimCanvas, false); Show(_headCanvas, false);
                BuildOverlay();
                OverlayMode();
                Show(_overlayCanvas, true);
                FitOverlay();
                _overlay.Apply(pic, _rt, letter, title, titleAlpha, wash, fade, _font);
            }
        }

        static void Show(Canvas c, bool on) { if (c != null && c.enabled != on) c.enabled = on; }

        void EnsureFont()
        {
            var map = _fontOverride ?? HaloSession.Map;
            if (map == null || map == _fontMap) return;
            _fontMap = map;
            _font = null;
            try
            {
                _font = HaloFont.Read(map.Find("font", "ui\\large_ui")) ?? HaloFont.Read(map.Find("font", "ui\\small_ui"));
                if (_font == null) foreach (var t in map.OfClass("font")) { _font = HaloFont.Read(t); if (_font != null) break; }
            }
            catch (Exception e) { HaloLog.Warn("Cinematic: title font: " + e.Message); }
            HaloLog.Info("Cinematic: title font " + (_font != null ? _font.Tag.Name + " (line " + _font.LineHeight + " px)" : "none"));
        }

        /// <summary>The lab sets the font map when it loads the data without a session.</summary>
        public void UseFontOf(HaloMap map) { _fontMap = null; _fontOverride = map; }
        HaloMap _fontOverride;

        // ------------------------------------------------------------------ camera

        void CameraFrame(bool vr)
        {
            if (_cam == null)
            {
                var go = new GameObject("Halo Cinematic Camera");
                go.transform.SetParent(transform, false);
                _cam = go.AddComponent<Camera>();
                _cam.enabled = false;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = Color.black;
                _cam.stereoTargetEye = StereoTargetEyeMask.None;
                _cam.allowHDR = false;
                _cam.depth = -50f;
            }
            int h = Mathf.Clamp(HaloSettings.Int("cinematic.height", vr ? 960 : Mathf.Min(Screen.height, 1080)), 240, 2160);
            int w = Mathf.RoundToInt(h * 4f / 3f);
            if (_rt == null || _rt.height != h)
            {
                if (_rt != null) { _cam.targetTexture = null; _rt.Release(); Destroy(_rt); }
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "Halo Cinematic", antiAliasing = 2 };
                _rt.Create();
                _cam.targetTexture = _rt;
                HaloLog.Info("Cinematic: cutscene picture " + w + "x" + h);
            }
            if (!_cam.enabled)
            {
                Camera head = HaloPlayer.Head();
                int mask = head != null ? head.cullingMask : ~0;
                _uiLayer = 5;
                _cam.cullingMask = mask & ~(1 << _uiLayer);
                _cam.farClipPlane = Mathf.Max(3000f, head != null ? head.farClipPlane : 3000f);
                _cam.enabled = true;
                TakeViews();
            }
            Vector3 p; Quaternion r; float hfov;
            if (HaloCinematic.CameraPose(out p, out r, out hfov))
            {
                _cam.transform.SetPositionAndRotation(p, r);
                float half = Mathf.Clamp(hfov, 1f, 170f) * 0.5f * Mathf.Deg2Rad;
                _cam.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(half) * 0.75f) * Mathf.Rad2Deg;
            }
            _cam.nearClipPlane = HaloCinematic.NearClip;
        }

        void TakeViews()
        {
            if (_viewsTaken) return;
            _viewsTaken = true;
            _prevWorldView = HaloWorldDriver.View; HaloWorldDriver.View = _cam;
            _prevFxView = HaloFx.ViewCamera; HaloFx.ViewCamera = _cam;
        }

        void ReleaseViews()
        {
            if (!_viewsTaken) return;
            _viewsTaken = false;
            if (HaloWorldDriver.View == _cam) HaloWorldDriver.View = _prevWorldView;
            if (HaloFx.ViewCamera == _cam) HaloFx.ViewCamera = _prevFxView;
        }

        // ------------------------------------------------------------------ monitor overlay

        void BuildOverlay()
        {
            if (_overlayCanvas != null) return;
            var go = new GameObject("Halo Cinematic Overlay", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _overlayCanvas = go.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlayCanvas.sortingOrder = SortOverlay;
            _overlay = new Layer((RectTransform)go.transform, null, true);
        }

        void OverlayMode()
        {
            var cam = LabMonitorCamera;
            var want = cam != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            if (_overlayCanvas.renderMode != want) _overlayCanvas.renderMode = want;
            if (cam != null) { _overlayCanvas.worldCamera = cam; _overlayCanvas.planeDistance = 0.3f; }
        }

        void FitOverlay()
        {
            var root = (RectTransform)_overlayCanvas.transform;
            float sw = root.rect.width, sh = root.rect.height;
            if (sw <= 1f || sh <= 1f) return;
            float s = Mathf.Min(sw / FrameW, sh / FrameH);
            _overlay.Fit(s, sw, sh);
        }

        // ------------------------------------------------------------------ VR screen + dim box

        Material OnTop
        {
            get
            {
                if (_onTop == null)
                {
                    Material baseMat = Canvas.GetDefaultCanvasMaterial();
                    _onTop = new Material(baseMat) { name = "Halo Cinematic (on top)", renderQueue = 4000 };
                    _onTop.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                }
                return _onTop;
            }
        }

        void BuildScreen()
        {
            if (_screenCanvas != null) return;
            var go = new GameObject("Halo Cinema Screen", typeof(RectTransform));
            go.layer = _uiLayer;
            go.transform.SetParent(transform, false);
            _screenCanvas = go.AddComponent<Canvas>();
            _screenCanvas.renderMode = RenderMode.WorldSpace;
            _screenCanvas.sortingOrder = SortScreen;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(FrameW, FrameH);
            rt.localScale = Vector3.one * (ScreenWidth / FrameW);
            _screen = new Layer(rt, OnTop, false);
            _screen.Fit(1f, FrameW, FrameH);

            // the dim box: six dark faces around the head, behind the screen in draw order
            var dg = new GameObject("Halo Cinema Dim", typeof(RectTransform));
            dg.layer = _uiLayer;
            dg.transform.SetParent(transform, false);
            _dimCanvas = dg.AddComponent<Canvas>();
            _dimCanvas.renderMode = RenderMode.WorldSpace;
            _dimCanvas.sortingOrder = SortDim;
            var drt = (RectTransform)dg.transform;
            drt.sizeDelta = new Vector2(100f, 100f);
            drt.localScale = Vector3.one * 0.1f; // 1 canvas unit = 0.1 m
            var dim = new Color(0f, 0f, 0f, HaloSettings.Num("cinematic.dim", 0.85f)); // ours
            for (int i = 0; i < 6; i++)
            {
                var f = Layer.Img(drt, "face " + i, dim, OnTop);
                var fr = f.rectTransform;
                fr.anchorMin = fr.anchorMax = new Vector2(0.5f, 0.5f);
                fr.sizeDelta = new Vector2(120f, 120f);  // 12 m faces
                Vector3 n = i == 0 ? Vector3.forward : i == 1 ? Vector3.back : i == 2 ? Vector3.left : i == 3 ? Vector3.right : i == 4 ? Vector3.up : Vector3.down;
                fr.localPosition = n * 60f;              // 6 m out
                fr.localRotation = Quaternion.LookRotation(n, Mathf.Abs(n.y) > 0.5f ? Vector3.forward : Vector3.up);
            }
        }

        Transform HeadTransform()
        {
            if (LabHead != null) return LabHead;
            var c = HaloPlayer.Head();
            return c != null ? c.transform : null;
        }

        void PlaceScreen()
        {
            Transform head = HeadTransform();
            if (head == null) return;
            Transform root = LabHead != null ? null : HaloPlayer.Root;
            Vector3 hp = head.position;
            float headYaw = head.eulerAngles.y;
            if (!_screenPlaced)
            {
                _screenPlaced = true;
                _screenYaw = headYaw;
                _screenOffset = root != null ? hp - root.position : hp;
                _awayTime = 0f;
            }
            else
            {
                // re-centre (slowly) when the player keeps looking elsewhere
                float off = Mathf.Abs(Mathf.DeltaAngle(_screenYaw, headYaw));
                _awayTime = off > 70f ? _awayTime + Time.deltaTime : 0f;
                if (_awayTime > 1.5f) _screenYaw = Mathf.MoveTowardsAngle(_screenYaw, headYaw, 120f * Time.deltaTime);
            }
            Vector3 eye = root != null ? root.position + _screenOffset : _screenOffset;
            Quaternion yaw = Quaternion.Euler(0f, _screenYaw, 0f);
            var t = _screenCanvas.transform;
            t.SetPositionAndRotation(eye + yaw * Vector3.forward * ScreenDistance, yaw);
            _dimCanvas.transform.SetPositionAndRotation(hp, yaw);
        }

        // ------------------------------------------------------------------ VR head-locked layer (fades / titles without the screen)

        void BuildHead()
        {
            if (_headCanvas != null) return;
            var go = new GameObject("Halo Cinematic Head", typeof(RectTransform));
            go.layer = _uiLayer;
            go.transform.SetParent(transform, false);
            _headCanvas = go.AddComponent<Canvas>();
            _headCanvas.renderMode = RenderMode.WorldSpace;
            _headCanvas.sortingOrder = SortHead;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(FrameW, FrameH);
            float width = 2f * HeadDistance * Mathf.Tan(35f * Mathf.Deg2Rad); // 640 units = 70 degrees across (ours)
            rt.localScale = Vector3.one * (width / FrameW);
            _head = new Layer(rt, OnTop, false);
            _head.Fit(1f, FrameW, FrameH);
            _head.FadeCoversAll(8f);
        }

        void PlaceHead()
        {
            Transform head = HeadTransform();
            if (head == null) return;
            _headCanvas.transform.SetPositionAndRotation(head.position + head.forward * HeadDistance, head.rotation);
        }

        // ------------------------------------------------------------------ one presentation layer in Halo's 640 x 480 frame

        sealed class Layer
        {
            readonly RectTransform _root, _frame, _textFrame;
            readonly Image _black, _barTop, _barBottom, _wash, _fade;
            readonly RawImage _picture;
            readonly HaloHudLabel _title, _shadow;
            readonly bool _fullScreen;
            HaloCutsceneData.Title _shown;
            HaloFont _shownFont;

            public Layer(RectTransform root, Material mat, bool fullScreen)
            {
                _root = root; _fullScreen = fullScreen;
                _black = Img(root, "black", Color.black, mat); Stretch(_black.rectTransform);
                _frame = Node(root, "frame");
                _frame.anchorMin = _frame.anchorMax = new Vector2(0.5f, 0.5f);
                _frame.pivot = new Vector2(0.5f, 0.5f);
                _frame.sizeDelta = new Vector2(FrameW, FrameH);
                var pg = Node(_frame, "picture");
                _picture = pg.gameObject.AddComponent<RawImage>();
                _picture.raycastTarget = false;
                if (mat != null) _picture.material = mat;
                Stretch(pg);
                _wash = Img(_frame, "wash", Color.clear, mat); Stretch(_wash.rectTransform);
                _barTop = Img(root, "bar top", Color.black, mat);
                _barBottom = Img(root, "bar bottom", Color.black, mat);
                // the title goes over the bars (it sits in the bottom one)
                _textFrame = Node(root, "text frame");
                _textFrame.anchorMin = _textFrame.anchorMax = new Vector2(0.5f, 0.5f);
                _textFrame.pivot = new Vector2(0.5f, 0.5f);
                _textFrame.sizeDelta = new Vector2(FrameW, FrameH);
                _shadow = new HaloHudLabel(_textFrame, "title shadow");
                _title = new HaloHudLabel(_textFrame, "title");
                if (mat != null) { _shadow.Image.material = mat; _title.Image.material = mat; }
                _fade = Img(root, "fade", Color.clear, mat); Stretch(_fade.rectTransform);
            }

            public void Destroy() { _title.Destroy(); _shadow.Destroy(); }

            public static Image Img(Transform parent, string name, Color c, Material mat)
            {
                var rt = Node(parent, name);
                var img = rt.gameObject.AddComponent<Image>();
                img.raycastTarget = false;
                img.color = c;
                if (mat != null) img.material = mat;
                return img;
            }

            static RectTransform Node(Transform parent, string name)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.layer = parent.gameObject.layer;
                go.transform.SetParent(parent, false);
                return (RectTransform)go.transform;
            }

            static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero; }

            /// <summary>Frame scale <paramref name="s"/> inside a root of w x h (bars span the root's width, 1/8 of the frame height).</summary>
            public void Fit(float s, float w, float h)
            {
                _frame.localScale = new Vector3(s, s, 1f);
                _textFrame.localScale = new Vector3(s, s, 1f);
                float bar = Bar * s, top = (h - FrameH * s) * 0.5f;
                SetBar(_barTop.rectTransform, true, top + bar);
                SetBar(_barBottom.rectTransform, false, top + bar);
            }

            static void SetBar(RectTransform r, bool top, float height)
            {
                r.anchorMin = new Vector2(0f, top ? 1f : 0f);
                r.anchorMax = new Vector2(1f, top ? 1f : 0f);
                r.pivot = new Vector2(0.5f, top ? 1f : 0f);
                r.sizeDelta = new Vector2(0f, height);
                r.anchoredPosition = Vector2.zero;
            }

            public void FadeCoversAll(float times)
            {
                var r = _fade.rectTransform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(FrameW * times, FrameH * times);
                r.anchoredPosition = Vector2.zero;
            }

            public void Apply(bool picture, Texture tex, bool letterbox, HaloCutsceneData.Title title, float titleAlpha, Color wash, Color fade, HaloFont font)
            {
                SetOn(_black, picture && _fullScreen);
                if (_picture.enabled != picture) _picture.enabled = picture;
                if (picture && _picture.texture != tex) _picture.texture = tex;
                SetOn(_barTop, letterbox); SetOn(_barBottom, letterbox);
                SetOn(_wash, picture && wash.a > 0.002f); if (_wash.enabled) _wash.color = wash;
                SetOn(_fade, fade.a > 0.002f); if (_fade.enabled) _fade.color = fade;

                if (title == null || font == null || titleAlpha <= 0.002f)
                {
                    if (_title.Image.enabled) { _title.Image.enabled = false; _shadow.Image.enabled = false; }
                    return;
                }
                if (title != _shown || font != _shownFont)
                {
                    _shown = title; _shownFont = font;
                    _title.Set(title.Text, font);
                    _shadow.Set(title.Text, font);
                    Place(_title, title, 0f);
                    Place(_shadow, title, 1f);
                }
                _title.Image.enabled = true;
                Color tc = Argb(title.TextColor), sc = Argb(title.ShadowColor);
                tc.a *= titleAlpha; sc.a *= titleAlpha;
                _title.Image.color = tc;
                _shadow.Image.enabled = sc.a > 0.002f;
                _shadow.Image.color = sc;
            }

            static void SetOn(Behaviour b, bool on) { if (b.enabled != on) b.enabled = on; }

            static Color Argb(uint c) { return new Color(((c >> 16) & 255) / 255f, ((c >> 8) & 255) / 255f, (c & 255) / 255f, ((c >> 24) & 255) / 255f); }

            /// <summary>The title's text in its bounds (top, left, bottom, right on the 640 x 480 frame) by its justification and flags.</summary>
            static void Place(HaloHudLabel l, HaloCutsceneData.Title t, float shadowOffset)
            {
                var r = l.Rect;
                float w = l.PixelWidth, h = l.PixelHeight;
                float x = t.Justification == HaloCutsceneData.Title.JustifyRight ? t.Right - w
                        : t.Justification == HaloCutsceneData.Title.JustifyCenter ? (t.Left + t.Right - w) * 0.5f : t.Left;
                float y = t.Top;
                if ((t.TextFlags & 8) != 0) y = t.Bottom - h;                       // bottom justify
                else if ((t.TextFlags & 4) != 0) y = (t.Top + t.Bottom - h) * 0.5f; // center vertically
                r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
                r.pivot = new Vector2(0f, 1f);
                r.sizeDelta = new Vector2(w, h);
                r.anchoredPosition = new Vector2(x + shadowOffset, -(y + shadowOffset));
            }
        }
    }
}

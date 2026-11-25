using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] The loading screen between the hub and a match, in Halo PC's look (BO2's Bo2LoadingScreen recipe): the menu's own background
    /// picture (ui\shell\bitmaps\background: the ring and Threshold) dimmed, the map's preview picture (mp_map_grafix) large, the map's name and
    /// description, the game type, the game's loading line ("Network server started. Loading map %s.", ui\shell\strings\loading) and a blue bar that
    /// follows the Match module's Progress. A monitor sees a screen overlay; a headset a 3.4 m screen 2.5 m ahead (the hub stays round it). The menu
    /// music fades out as the load starts. When the match begins (Hide, or the phase turning Playing) the view is black at once and fades into the
    /// map over a second; leaving a match (Leave, or the phase turning Hub) blacks out and fades into the hub. Everything is made in code; a cover
    /// that cannot be made leaves the plain cut (never an error). Log lines "Loading: ...".
    /// </summary>
    public static class HaloLoading
    {
        public static bool Showing { get; private set; }

        public static void Show(HaloInstall.MapEntry map, HaloRules rules)
        {
            Showing = true;
            try
            {
                HaloFront.StopMusic(1.5f);
                HaloLoadingScreen.Begin(map, rules);
                HaloLog.Info("Loading: " + (map != null ? map.Label : "?") + " (" + (rules != null ? HaloGameTypes.Name(rules) : "campaign, " + HaloCampaign.Level) + ")");
            }
            catch (Exception e) { HaloLog.Warn("Loading: the screen could not start: " + e.Message); }
        }

        public static void Progress(float fraction, string label)
        {
            HaloLoadingScreen s = HaloLoadingScreen.Current;
            if (s != null) s.SetProgress(fraction, label);
        }

        /// <summary>The player stands in the map: black at once, then the black fades away.</summary>
        public static void Hide()
        {
            bool was = Showing;
            Showing = false;
            HaloLoadingScreen s = HaloLoadingScreen.Current;
            if (s != null) { s.Cover(); s.Reveal(1.0f); }
            if (was) HaloLog.Info("Loading: done - fading into the match.");
        }

        /// <summary>The match is being left: fade to black while it is torn down; the hub fades in by itself once the player stands in it.</summary>
        public static void Leave()
        {
            HaloLoadingScreen s = HaloLoadingScreen.CoverNow();
            if (s != null) s.FadeToBlack(0.35f);
            HaloLog.Info("Loading: leaving the match - black until the hub is back.");
        }

        /// <summary>Black now and the hub fades in when the player stands on the platform (or after a few seconds).</summary>
        public static void BackToHub()
        {
            HaloLoadingScreen s = HaloLoadingScreen.CoverNow();
            if (s != null) s.RevealInHub();
        }
    }

    public sealed class HaloLoadingScreen : MonoBehaviour
    {
        public static HaloLoadingScreen Current { get; private set; }

        private const float CardWidth = 1920f, CardHeight = 1080f, CardMetresPerUnit = 3.4f / 1920f;
        private const float BarX = 120f, BarY = 1000f, BarW = 1680f, BarH = 10f;

        /// <summary>Lab: build the headset's world-space screen on a monitor too.</summary>
        public static bool ForceWorld;

        private Canvas _card;
        private CanvasGroup _group;
        private TextMeshProUGUI _stage, _percent, _time;
        private RectTransform _fill;
        private string _map = "", _mapFile = "", _type = "", _desc = "";
        private int _mission = -1;
        private float _startedAt, _shown, _target, _cardAlpha;
        private int _shownPercent = -1, _shownSeconds = -1;
        private bool _overlay, _placed, _cardGone, _hubWait;
        private Vector3 _cardPos;
        private Quaternion _cardRot = Quaternion.identity;
        private float _hubWaitUntil;

        // the black cover round the head
        private MeshRenderer _cover;
        private Material _coverMat;
        private float _alpha, _fadeFrom, _fadeTo, _fadeElapsed, _fadeLength;
        private bool _fading, _destroyWhenClear;

        public bool Covered { get { return _alpha >= 0.999f && !_fading; } }
        public bool CardShown { get { return _card != null && !_cardGone; } }
        public float CoverAlpha { get { return _alpha; } }
        public float Shown { get { return _shown; } }
        public bool Overlay { get { return _overlay; } }
        public Canvas Card { get { return _card; } }

        public static HaloLoadingScreen Begin(HaloInstall.MapEntry map, HaloRules rules)
        {
            float carried = 0f;
            if (Current != null) { carried = Current._alpha; Current.Dispose(); }
            var go = new GameObject("Halo Loading");
            if (HaloGame.Current != null) go.transform.SetParent(HaloGame.Current.transform, false);
            var s = go.AddComponent<HaloLoadingScreen>();
            s._mapFile = map != null ? map.Name : "";
            s._map = map != null ? HaloLobbyPage.MapLabel(map.Name) : "";
            s._type = rules != null ? HaloGameTypes.Name(rules) : "";
            HaloUiData d = HaloUiArt.Data;
            s._desc = d != null && map != null ? d.MapDescription(map.Name).Replace("\n\n", "     ").Replace('\n', ' ') : "";
            int mission = map != null ? HaloUiData.MissionIndex(map.Name) : -1;
            if (mission >= 0)
            {
                // a campaign mission: its level select picture (sp_levels), its name, the difficulty and the level select's description
                s._mission = mission;
                s._map = HaloCampaignPage.MissionName(map.Name);
                s._type = "CAMPAIGN  -  " + HaloCampaignPage.DifficultyName((int)HaloCampaign.Level);
                s._desc = d != null ? d.MissionDescription(mission) : "";
            }

            s._startedAt = Time.realtimeSinceStartup;
            try { s.BuildCard(); }
            catch (Exception e) { HaloLog.Warn("Loading: the card could not be built: " + e.Message); }
            Current = s;
            if (carried > 0.01f) { s.SetAlpha(carried); s.FadeCover(0.5f); }
            return s;
        }

        public static HaloLoadingScreen CoverNow()
        {
            try
            {
                HaloLoadingScreen s = Current;
                if (s == null)
                {
                    var go = new GameObject("Halo Loading");
                    if (HaloGame.Current != null) go.transform.SetParent(HaloGame.Current.transform, false);
                    s = go.AddComponent<HaloLoadingScreen>();
                    Current = s;
                }

                return s;
            }
            catch (Exception e)
            {
                HaloLog.Warn("Loading: the cover could not start: " + e.Message);
                return null;
            }
        }

        public void SetProgress(float f, string label)
        {
            if (f > _target) _target = Mathf.Clamp01(f);
            if (_stage != null && !string.IsNullOrEmpty(label) && _stage.text != label) _stage.text = label;
        }

        public void Cover()
        {
            _cardGone = true;
            DestroyCard();
            SetAlpha(1f);
            _fading = false;
            _hubWait = false;
        }

        public void Reveal(float seconds)
        {
            _fadeFrom = _alpha;
            _fadeTo = 0f;
            _fadeElapsed = 0f;
            _fadeLength = Mathf.Max(0.05f, seconds);
            _fading = true;
            _destroyWhenClear = true;
        }

        public void FadeToBlack(float seconds)
        {
            _cardGone = true;
            DestroyCard();
            _fadeFrom = _alpha;
            _fadeTo = 1f;
            _fadeElapsed = 0f;
            _fadeLength = Mathf.Max(0.05f, seconds);
            _fading = true;
            _destroyWhenClear = false;
        }

        /// <summary>Black now; fades in once the player stands in the hub (4 s at most).</summary>
        public void RevealInHub()
        {
            Cover();
            _hubWait = true;
            _hubWaitUntil = Time.realtimeSinceStartup + 4f;
        }

        private void FadeCover(float seconds)
        {
            _fadeFrom = _alpha;
            _fadeTo = 0f;
            _fadeElapsed = 0f;
            _fadeLength = Mathf.Max(0.05f, seconds);
            _fading = true;
            _destroyWhenClear = false;
        }

        private void Dispose()
        {
            if (Current == this) Current = null;
            if (_coverMat != null) Destroy(_coverMat);
            if (_cover != null) Destroy(_cover.gameObject);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_cover != null) Destroy(_cover.gameObject);
        }

        private void DestroyCard()
        {
            if (_card != null) Destroy(_card.gameObject);
            _card = null;
            _group = null;
        }

        // ------------------------------------------------------------------ the card

        private void BuildCard()
        {
            _overlay = !HaloPlayer.XrPresent && !ForceWorld;
            RectTransform frame;
            if (_overlay)
            {
                var go = new GameObject("Halo Loading Card");
                go.layer = HaloUi.UiLayer;
                go.transform.SetParent(transform, false);
                _card = go.AddComponent<Canvas>();
                _card.renderMode = RenderMode.ScreenSpaceOverlay;
                _card.sortingOrder = -5;
                var sc = go.AddComponent<CanvasScaler>();
                sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                sc.referenceResolution = new Vector2(CardWidth, CardHeight);
                sc.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                sc.matchWidthOrHeight = 1f;
                Full(go.transform, "Black", Color.black);
                frame = (RectTransform)new GameObject("Frame", typeof(RectTransform)).transform;
                frame.gameObject.layer = HaloUi.UiLayer;
                frame.SetParent(go.transform, false);
                frame.anchorMin = frame.anchorMax = frame.pivot = new Vector2(0.5f, 0.5f);
                frame.sizeDelta = new Vector2(CardWidth, CardHeight);
            }
            else
            {
                _card = HaloUi.MakeCanvas("Halo Loading Card", transform, CardWidth, CardHeight, CardMetresPerUnit);
                foreach (GraphicRaycaster r in _card.GetComponents<GraphicRaycaster>()) Destroy(r);
                foreach (Component c in _card.GetComponents<Component>()) if (c != null && c.GetType().Name == "TrackedDeviceGraphicRaycaster") Destroy(c);
                Transform catcher = _card.transform.Find("Pointer catcher");
                if (catcher != null) Destroy(catcher.gameObject);
                frame = HaloUi.Place(_card.transform, "Frame", 0f, 0f, CardWidth, CardHeight);
                HaloUi.Box(frame, "Black", 0f, 0f, CardWidth, CardHeight, Color.black);
                _card.worldCamera = HaloPlayer.Head();
            }

            _group = _card.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // the menu's background picture (640 x 480 of 1024 x 512): 4:3, centred, dimmed; the map's preview over it
            HaloUi.Picture(frame, "Background", (CardWidth - 1440f) * 0.5f, 0f, 1440f, 1080f, "ui\\shell\\bitmaps\\background", 0, 640, 480, new Color(0.42f, 0.42f, 0.46f, 1f));
            HaloUi.Box(frame, "Shade", 0f, 640f, CardWidth, 440f, new Color(0f, 0f, 0f, 0.55f));
            int pic = HaloUiData.MenuIndex(_mapFile);
            HaloUi.Box(frame, "Preview rim", 116f, 116f, 568f, 470f, HaloUi.Rim);
            HaloUi.Box(frame, "Preview back", 120f, 120f, 560f, 462f, Color.black);
            if (_mission >= 0) HaloUi.Picture(frame, "Preview", 120f, 120f, 560f, 460f, "ui\\shell\\bitmaps\\sp_levels", _mission, HaloUiData.PreviewWidth, HaloUiData.PreviewHeight, Color.white);
            else HaloUi.Picture(frame, "Preview", 120f, 120f, 560f, 460f, "ui\\shell\\bitmaps\\mp_map_grafix", pic >= 0 ? pic : 19, HaloUiData.PreviewWidth, HaloUiData.PreviewHeight, Color.white);
            HaloUi.Picture(frame, "Logo", 1180f, 100f, 640f, 251f, "ui\\shell\\main_menu\\halo_logo", 0, 640, 251, Color.white);

            HaloUi.OneLine(HaloUi.Text(frame, "Map", _map.ToUpperInvariant(), 96f, Color.white, TextAlignmentOptions.BottomLeft, 116f, 640f, 1700f, 120f, true), 40f);
            HaloUi.OneLine(HaloUi.Text(frame, "Mode", _type.ToUpperInvariant(), 42f, HaloUi.Blue, TextAlignmentOptions.Left, 120f, 770f, 1600f, 54f, true), 20f);
            HaloUi.OneLine(HaloUi.Text(frame, "Description", _desc, 28f, HaloUi.BlueBright, TextAlignmentOptions.Left, 120f, 830f, 1600f, 40f, false), 14f);
            string line = HaloUiArt.S("strings\\loading", 6, "Loading map %s.");
            line = line.Replace("%s", _map);
            HaloUi.OneLine(HaloUi.Text(frame, "Loading line", line, 26f, HaloUi.Grey, TextAlignmentOptions.Left, 120f, 890f, 1300f, 36f, false), 12f);
            _stage = HaloUi.OneLine(HaloUi.Text(frame, "Stage", "Starting...", 26f, new Color(0.86f, 0.9f, 0.96f, 1f), TextAlignmentOptions.Left, 120f, 944f, 1300f, 40f, false), 12f);
            _percent = HaloUi.OneLine(HaloUi.Text(frame, "Percent", "0 %", 34f, Color.white, TextAlignmentOptions.Right, 1500f, 940f, 300f, 46f, true));
            _time = HaloUi.OneLine(HaloUi.Text(frame, "Time", "", 22f, HaloUi.Grey, TextAlignmentOptions.Right, 1500f, 896f, 300f, 34f, false));
            HaloUi.Box(frame, "Bar track", BarX, BarY, BarW, BarH, new Color(0.18f, 0.55f, 1f, 0.22f));
            _fill = HaloUi.Box(frame, "Bar fill", BarX, BarY, 0f, BarH, HaloUi.Blue).rectTransform;
            _shownPercent = _shownSeconds = -1;
        }

        private static void Full(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = HaloUi.UiLayer;
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
        }

        private void PlaceCard(Camera head)
        {
            bool vr = HaloPlayer.XrPresent;
            Vector3 eye = head.transform.position, fwd = head.transform.forward;
            float tan = Mathf.Tan(Mathf.Clamp(head.fieldOfView, 20f, 120f) * 0.5f * Mathf.Deg2Rad);
            float height = CardHeight * CardMetresPerUnit, width = CardWidth * CardMetresPerUnit;
            float distance = vr ? 2.5f : Mathf.Clamp(Mathf.Min(height / (2f * tan), width / (2f * tan * Mathf.Max(0.5f, head.aspect))) * 0.98f, 0.5f, 8f);
            fwd.y = vr ? 0f : fwd.y;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 target = eye + fwd * distance;
            if (vr) target.y = eye.y;
            Quaternion look = vr ? Quaternion.LookRotation(target - eye, Vector3.up) : head.transform.rotation;
            if (!_placed || !vr) { _cardPos = target; _cardRot = look; _placed = true; }
            else
            {
                float k = 1f - Mathf.Exp(-2.5f * Time.unscaledDeltaTime);
                _cardPos = Vector3.Lerp(_cardPos, target, k);
                _cardRot = Quaternion.Slerp(_cardRot, look, k);
            }

            _card.transform.SetPositionAndRotation(_cardPos, _cardRot);
        }

        // ------------------------------------------------------------------ the cover

        private void BuildCover()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Halo Loading Cover";
            Destroy(go.GetComponent<Collider>());
            Camera head = HaloPlayer.Head();
            go.transform.SetParent(head != null ? head.transform : transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 0.6f;
            _cover = go.GetComponent<MeshRenderer>();
            Shader sh = HaloShaders.Find("Universal Render Pipeline/Unlit");
            _coverMat = new Material(sh) { name = "Halo loading cover" };
            _coverMat.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f));
            _coverMat.SetFloat("_Surface", 1f);
            _coverMat.SetFloat("_Blend", 0f);
            _coverMat.SetOverrideTag("RenderType", "Transparent");
            _coverMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _coverMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _coverMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _coverMat.SetFloat("_ZWrite", 0f);
            _coverMat.SetFloat("_Cull", 0f);
            _coverMat.renderQueue = 4000;
            _cover.sharedMaterial = _coverMat;
            _cover.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cover.receiveShadows = false;
            go.SetActive(false);
        }

        private void SetAlpha(float a)
        {
            _alpha = Mathf.Clamp01(a);
            if (_cover == null && _alpha > 0.001f)
            {
                try { BuildCover(); }
                catch (Exception e) { HaloLog.Warn("Loading: the cover could not be made: " + e.Message); }
            }

            if (_cover == null) return;
            bool on = _alpha > 0.002f;
            if (_cover.gameObject.activeSelf != on) _cover.gameObject.SetActive(on);
            if (on) _coverMat.SetColor("_BaseColor", new Color(0f, 0f, 0f, _alpha));
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            Camera head = HaloPlayer.Head();
            if (_cover != null && head != null && _cover.transform.parent != head.transform)
            {
                _cover.transform.SetParent(head.transform, false);
                _cover.transform.localPosition = Vector3.zero;
                _cover.transform.localRotation = Quaternion.identity;
            }

            if (_hubWait)
            {
                Vector3 feet;
                bool there = HaloPlayer.TryFeet(out feet) && !HaloHub.IsAway(feet);
                if (there || Time.realtimeSinceStartup > _hubWaitUntil)
                {
                    _hubWait = false;
                    HaloLog.Info("Loading: back in the hub (" + (there ? "the player stands on the platform" : "waited 4 s") + ") - fading in.");
                    Reveal(0.8f);
                }
            }

            if (_fading)
            {
                _fadeElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                float u = Mathf.Clamp01(_fadeElapsed / _fadeLength);
                u = u * u * (3f - 2f * u);
                SetAlpha(Mathf.Lerp(_fadeFrom, _fadeTo, u));
                if (u >= 1f)
                {
                    _fading = false;
                    if (_destroyWhenClear && _alpha <= 0.002f) { Dispose(); return; }
                }
            }

            if (_card == null) return;
            _cardAlpha = Mathf.MoveTowards(_cardAlpha, 1f, Time.unscaledDeltaTime * 2.5f);
            if (_group != null) _group.alpha = _cardAlpha;
            if (head != null && !_overlay)
            {
                _card.worldCamera = head;
                PlaceCard(head);
            }

            float now = Time.realtimeSinceStartup;
            float ceiling = Mathf.Min(0.985f, _target + 0.05f);
            float want = Mathf.Max(_shown, Mathf.Min(ceiling, _shown + Time.unscaledDeltaTime * 0.012f));
            if (_target > want) want = Mathf.MoveTowards(_shown, _target, Time.unscaledDeltaTime * 0.5f);
            _shown = Mathf.Clamp01(want);
            if (_fill != null) _fill.sizeDelta = new Vector2(BarW * _shown, BarH);
            int percent = Mathf.RoundToInt(_shown * 100f), seconds = (int)(now - _startedAt);
            if (_percent != null && percent != _shownPercent) { _shownPercent = percent; _percent.text = percent + " %"; }
            if (_time != null && seconds != _shownSeconds) { _shownSeconds = seconds; _time.text = seconds + " s"; }
        }
    }
}

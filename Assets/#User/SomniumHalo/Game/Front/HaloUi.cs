using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] World-space UI in Halo PC's menu look (ui.map): dark navy list slabs with the blue rim (the game's own
    /// sel_list_item_bkd pictures as sliced sprites once ui.map is read), Halo's blue text (the widgets' text colour ARGB 1, 0.16, 0.59, 1)
    /// in Halo's own large_ui / small_ui fonts (HaloFonts; TextMeshPro's default until built). Layouts are in canvas units with (0, 0) at
    /// the top-left and y growing downwards (BO2's Bo2Ui). The canvases take the client's mouse (GraphicRaycaster) and its VR ray
    /// (TrackedDeviceGraphicRaycaster), the recipe every Dreamport uses.
    /// </summary>
    public static class HaloUi
    {
        public const int UiLayer = 5;

        /// <summary>The menu widgets' text colour (ui.map list items: ARGB 1, 0.16, 0.59, 1).</summary>
        public static readonly Color Blue = new Color(0.16f, 0.59f, 1f, 1f);
        public static readonly Color BlueBright = new Color(0.62f, 0.84f, 1f, 1f);
        public static readonly Color BlueDim = new Color(0.10f, 0.33f, 0.58f, 1f);
        public static readonly Color Navy = new Color(0.0f, 0.045f, 0.10f, 0.92f);
        public static readonly Color NavyLight = new Color(0.0f, 0.13f, 0.30f, 0.95f);
        public static readonly Color Rim = new Color(0.18f, 0.55f, 1f, 1f);
        public static readonly Color Grey = new Color(0.60f, 0.66f, 0.74f, 1f);
        public static readonly Color Red = new Color(1f, 0.30f, 0.25f, 1f);
        public static readonly Color Gold = new Color(1f, 0.82f, 0.30f, 1f);

        /// <summary>A world-space canvas the client's pointer can use in both modes.</summary>
        public static Canvas MakeCanvas(string name, Transform parent, float width, float height, float metresPerUnit)
        {
            var go = new GameObject(name);
            go.layer = UiLayer;
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = HaloPlayer.Head();
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2f;
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one * metresPerUnit;

            var catcher = Place(go.transform, "Pointer catcher", 0f, 0f, width, height).gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            return canvas;
        }

        /// <summary>A child rect at (x, y) from the parent's top-left, w x h canvas units.</summary>
        public static RectTransform Place(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UiLayer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -y);
            r.sizeDelta = new Vector2(w, h);
            return r;
        }

        public static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color, bool raycast = false)
        {
            Image img = Place(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        /// <summary>A game picture (ui.map bitmap <paramref name="bitmap"/>, image <paramref name="index"/>, its used w x h pixels from the top-left), filled in when ui.map is read.</summary>
        public static RawImage Picture(Transform parent, string name, float x, float y, float w, float h, string bitmap, int index, int usedW, int usedH, Color color)
        {
            RawImage img = Place(parent, name, x, y, w, h).gameObject.AddComponent<RawImage>();
            img.color = new Color(color.r, color.g, color.b, 0f);
            img.raycastTarget = false;
            HaloUiArt.Bind(img, bitmap, index, usedW, usedH, color);
            return img;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align, float x, float y, float w, float h, bool large = true)
        {
            var t = Place(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
            HaloFonts.Apply(t, large);
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.richText = false;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.text = text ?? "";
            return t;
        }

        public static TextMeshProUGUI OneLine(TextMeshProUGUI t, float minSize = 0f)
        {
            t.textWrappingMode = TextWrappingModes.NoWrap;
            if (minSize > 0f)
            {
                t.enableAutoSizing = true;
                t.fontSizeMax = t.fontSize;
                t.fontSizeMin = minSize;
            }

            return t;
        }

        /// <summary>The game's description panel (sel_list_desc_bkd: navy with the blue rim), a plain slab until ui.map is read.</summary>
        public static Image Panel(Transform parent, string name, float x, float y, float w, float h)
        {
            Image img = Box(parent, name, x, y, w, h, Navy);
            HaloUiArt.BindSliced(img, "ui\\shell\\bitmaps\\sel_list_desc_bkd", 0, 220, 335, 14, Color.white);
            return img;
        }

        public static HaloButton Button(Transform parent, string label, float x, float y, float w, float h, float textSize, Action onClick)
        {
            RectTransform r = Place(parent, "Button " + label, x, y, w, h);
            var b = r.gameObject.AddComponent<HaloButton>();
            b.Build(label, textSize, onClick);
            return b;
        }

        /// <summary>A main menu item drawn by the game's own picture of its caption (ui\shell\main_menu\menu_*: [0] plain, [1] lit) with a text fallback.</summary>
        public static HaloButton MenuItem(Transform parent, string label, string bitmap, float x, float y, float w, float h, Action onClick)
        {
            RectTransform r = Place(parent, "Menu item " + label, x, y, w, h);
            var b = r.gameObject.AddComponent<HaloButton>();
            b.BuildPicture(label, bitmap, onClick);
            return b;
        }
    }

    /// <summary>
    /// A Halo menu button: the list slab (sel_list_item_bkd [1], lit [2] when pointed at or selected) and its caption in Halo's blue; or, for the
    /// main menu items, the game's caption picture (menu_* [0], [1] lit). The UI sounds are ui.map's (cursor on hover, forward on a press,
    /// flag_failure when refused).
    /// </summary>
    public sealed class HaloButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private Image _slab;
        private RawImage _plain, _lit;
        private TextMeshProUGUI _label;
        private Action _onClick;
        private bool _hover, _down, _selected, _interactable = true, _picture;
        private float _lastClick;

        public string Label { get { return _label != null ? _label.text : name; } }
        public TextMeshProUGUI Caption { get { return _label; } }
        /// <summary>A press that is refused (not interactable) still plays the game's "failure" sound and calls this.</summary>
        public Action Refused;

        public void Build(string label, float textSize, Action onClick)
        {
            _onClick = onClick;
            var rect = (RectTransform)transform;
            float w = rect.sizeDelta.x, h = rect.sizeDelta.y;
            _slab = gameObject.AddComponent<Image>();
            _slab.raycastTarget = true;
            _slab.color = HaloUi.Navy;
            _label = HaloUi.Text(transform, "Label", label, textSize, HaloUi.Blue, TextAlignmentOptions.Center, 10f, 0f, w - 20f, h, true);
            HaloUi.OneLine(_label, textSize * 0.55f);
            _labelPos = _label.rectTransform.anchoredPosition;
            HaloUiArt.BindButton(this);
            Refresh();
        }

        public void BuildPicture(string label, string bitmap, Action onClick)
        {
            _onClick = onClick;
            _picture = true;
            var rect = (RectTransform)transform;
            float w = rect.sizeDelta.x, h = rect.sizeDelta.y;
            var hit = gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            // the caption in text until the pictures are read (and if they never are); made first: a picture served at once hides it
            _label = HaloUi.Text(transform, "Label", label, h * 0.62f, HaloUi.Blue, TextAlignmentOptions.MidlineLeft, 8f, 0f, w - 8f, h, true);
            HaloUi.OneLine(_label);
            // the picture's used part is 232 x 32 of 256 x 64 (lit) / 228 x 27 (plain): both drawn at the lit one's size so they line up
            _plain = HaloUi.Picture(transform, "Plain", 0f, 0f, w, h, bitmap, 0, 232, 32, Color.white);
            _lit = HaloUi.Picture(transform, "Lit", 0f, 0f, w, h, bitmap, 1, 232, 32, Color.white);
            Refresh();
        }

        public bool Selected { get { return _selected; } set { if (_selected == value) return; _selected = value; Refresh(); } }

        public bool Interactable
        {
            get { return _interactable; }
            set { if (_interactable == value) return; _interactable = value; if (!value) { _hover = false; _down = false; } Refresh(); }
        }

        public void SetLabel(string text)
        {
            if (_label != null && _label.text != text) _label.text = text;
        }

        /// <summary>The picture arrived (HaloUiArt): the text caption goes.</summary>
        public void PictureReady()
        {
            if (_picture && _label != null) _label.gameObject.SetActive(false);
            Refresh();
        }

        public void SlabReady(Sprite plain, Sprite lit)
        {
            _plainSprite = plain;
            _litSprite = lit;
            Refresh();
        }

        private Sprite _plainSprite, _litSprite;
        private Vector2 _labelPos;

        /// <summary>Moves the caption inside the button (canvas units from its top-left), e.g. under a picture.</summary>
        public void SetCaptionRect(float x, float y, float w, float h, float size)
        {
            if (_label == null) return;
            _labelPos = new Vector2(x, -y);
            _label.rectTransform.anchoredPosition = _labelPos;
            _label.rectTransform.sizeDelta = new Vector2(w, h);
            _label.fontSize = size;
            _label.fontSizeMax = size;
        }

        private void Refresh()
        {
            bool lit = _interactable && (_hover || _selected);
            if (_picture)
            {
                float a = _interactable ? 1f : 0.35f;
                if (_plain != null) SetAlpha(_plain, lit ? 0f : a * (_plain.texture != null ? 1f : 0f));
                if (_lit != null) SetAlpha(_lit, lit ? (_lit.texture != null ? 1f : 0f) : 0f);
                if (_label != null) _label.color = !_interactable ? HaloUi.BlueDim : lit ? HaloUi.BlueBright : HaloUi.Blue;
                return;
            }

            if (_slab == null) return;
            if (_plainSprite != null)
            {
                _slab.sprite = lit && _litSprite != null ? _litSprite : _plainSprite;
                _slab.type = Image.Type.Sliced;
                _slab.color = !_interactable ? new Color(0.55f, 0.55f, 0.6f, 0.55f) : Color.white;
            }
            else
            {
                _slab.color = !_interactable ? new Color(0.02f, 0.03f, 0.05f, 0.6f) : lit ? HaloUi.NavyLight : HaloUi.Navy;
            }

            _label.color = !_interactable ? new Color(0.30f, 0.38f, 0.48f, 1f) : _selected ? Color.white : _hover ? HaloUi.BlueBright : HaloUi.Blue;
            _label.rectTransform.anchoredPosition = _labelPos + new Vector2(_down ? 1f : 0f, _down ? -1f : 0f);
        }

        private static void SetAlpha(RawImage img, float a)
        {
            Color c = img.color;
            c.a = a;
            img.color = c;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            if (!_hover && _interactable) HaloUiArt.PlayUi(HaloUiArt.UiSound.Cursor);
            _hover = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData e) { _hover = false; _down = false; Refresh(); }
        public void OnPointerDown(PointerEventData e) { _down = true; Refresh(); }
        public void OnPointerUp(PointerEventData e) { _down = false; Refresh(); }

        public void OnPointerClick(PointerEventData e)
        {
            if (Time.unscaledTime - _lastClick < 0.3f) return;
            _lastClick = Time.unscaledTime;
            if (!_interactable)
            {
                HaloUiArt.PlayUi(HaloUiArt.UiSound.Fail);
                if (Refused != null) { try { Refused(); } catch (Exception ex) { HaloLog.Warn("UI: refusal of '" + Label + "' failed: " + ex.Message); } }
                return;
            }

            HaloLog.Info("UI: '" + Label + "' pressed (" + (e == null ? "code" : e.pointerId < 0 ? "mouse" : "pointer " + e.pointerId) + ")");
            HaloUiArt.PlayUi(HaloUiArt.UiSound.Forward);
            try
            {
                if (_onClick != null) _onClick();
            }
            catch (Exception ex)
            {
                HaloLog.Error("UI: '" + Label + "' failed: " + ex);
            }
        }

        /// <summary>The lab (and code) presses the button as a click would.</summary>
        public void Press()
        {
            _lastClick = -1f;
            OnPointerClick(null);
        }
    }

    /// <summary>
    /// Keeps a hub canvas where the player can read it (BO2's Bo2PanelPlacer, iteration 25): upright ON THE HUB FLOOR (its whole framed group,
    /// the logo included, from FloorGap above the floor), facing the player along the view's level direction; a headset at VrDistance, a monitor
    /// far enough that it fits the view from eye height (and beyond the avatar in third person). Placed 1 s after it is shown and again when the
    /// player looked away for 4 s or walked away.
    /// </summary>
    public sealed class HaloPanelPlacer : MonoBehaviour
    {
        public float HeightMetres = 1.5f;
        public float VrDistance = 2.6f;
        public string What = "panel";
        /// <summary>Metres the framed group's centre is above the canvas's centre (the logo above it): the canvas is placed that much lower.</summary>
        public float CentreUp;
        public float FloorGap = 0.2f;

        private Canvas _canvas;
        private float _shownAt, _nextCheck, _awayFor;
        private bool _placed;

        private void OnEnable()
        {
            _canvas = GetComponent<Canvas>();
            _shownAt = Time.unscaledTime;
            _placed = false;
            _awayFor = 0f;
        }

        public void Replace()
        {
            _placed = false;
            _shownAt = Time.unscaledTime;
        }

        public bool Placed { get { return _placed; } }

        private void LateUpdate()
        {
            Camera head = HaloPlayer.Head();
            if (_canvas != null) _canvas.worldCamera = head;
            if (head == null || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.25f;
            Vector3 eye = head.transform.position;
            if ((eye - HaloSpace.RoomOrigin).magnitude > HaloHub.KeepRadius + 6f) return;

            if (!_placed)
            {
                if (Time.unscaledTime - _shownAt > 1f) Place(head, "shown");
                return;
            }

            Vector3 to = transform.position - eye, fwd = head.transform.forward;
            to.y = 0f;
            fwd.y = 0f;
            if (to.magnitude > 9f || to.magnitude < 0.8f)
            {
                Place(head, to.magnitude > 9f ? "the player moved away" : "the player walked into it");
                return;
            }

            bool away = fwd.sqrMagnitude > 1e-4f && Vector3.Angle(fwd, to) > 100f;
            _awayFor = away ? _awayFor + 0.25f : 0f;
            if (_awayFor > 4f) Place(head, "the player looked away");
        }

        public void Place(Camera head, string why)
        {
            bool vr = HaloPlayer.XrPresent;
            Vector3 eye = head.transform.position;
            Vector3 fwd = head.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            float tan = Mathf.Tan(Mathf.Clamp(head.fieldOfView, 20f, 120f) * 0.5f * Mathf.Deg2Rad);
            float distance = vr ? VrDistance : Mathf.Clamp(HeightMetres / 0.62f / (2f * tan), 1.5f, 12f);
            Transform root = HaloPlayer.Root;
            if (!vr && root != null)
            {
                Vector3 toAvatar = root.position - eye;
                toAvatar.y = 0f;
                float along = Vector3.Dot(toAvatar, fwd);
                if (along > 0.5f) distance = Mathf.Max(distance, along + 1.8f);
            }

            float floor = HaloSpace.RoomOrigin.y;
            Vector3 at = eye + fwd * distance;
            at.y = floor + FloorGap + HeightMetres * 0.5f;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
            transform.position = at - transform.up * CentreUp;
            _placed = true;
            _awayFor = 0f;
            HaloLog.Info("Hub: " + What + " placed on the floor " + distance.ToString("0.00") + " m in front of the " + (vr ? "headset" : "camera (" + head.fieldOfView.ToString("0") + " degrees)") + ", " + HeightMetres.ToString("0.00") + " m tall (" + why + ").");
        }
    }
}

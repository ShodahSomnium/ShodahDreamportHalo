using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A line of text in Halo's own font (the font tag's 8-bit glyph pictures), drawn on the CPU into a texture when the text changes
    /// (never per frame). Tabs advance to the next column of <see cref="TabWidth"/> pixels (Halo's string lists use them in tables).
    /// </summary>
    public sealed class HaloHudLabel
    {
        public readonly RawImage Image;
        public RectTransform Rect { get { return Image.rectTransform; } }
        Texture2D _tex;
        Color32[] _px = new Color32[0];
        string _text;
        HaloFont _font;
        public int PixelWidth { get; private set; }
        public int PixelHeight { get; private set; }
        public int TabWidth = 64;

        public HaloHudLabel(Transform parent, string name)
        {
            Image = HaloHudKit.Raw(parent, name);
            Image.enabled = false;
        }

        public string Text { get { return _text; } }

        /// <summary>Sets the text (re-drawn only when text or font changed). Returns true when it changed.</summary>
        public bool Set(string text, HaloFont font)
        {
            if (font == null) font = HaloHudKit.Font;
            if (text == _text && font == _font) return false;
            _text = text; _font = font;
            if (string.IsNullOrEmpty(text) || font == null) { Image.enabled = false; PixelWidth = 0; return true; }
            Draw(text, font);
            Image.enabled = true;
            return true;
        }

        public int Measure(string s, HaloFont f)
        {
            int x = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\t') { x = (x / TabWidth + 1) * TabWidth; continue; }
                var g = f.Get(c);
                if (g != null) x += g.Advance;
            }
            return x;
        }

        void Draw(string s, HaloFont f)
        {
            int w = Mathf.Max(1, Measure(s, f) + 2), h = Mathf.Max(1, f.Ascend + f.Descend + 1);
            PixelWidth = w; PixelHeight = h;
            if (_tex == null || _tex.width != w || _tex.height != h)
            {
                if (_tex != null) Object.Destroy(_tex);
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Halo HUD text", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _px = new Color32[w * h];
            }
            var clear = new Color32(255, 255, 255, 0);
            for (int i = 0; i < _px.Length; i++) _px[i] = clear;
            int x = 1, baseline = f.Ascend;
            byte[] pix = f.Pixels;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\t') { x = (x / TabWidth + 1) * TabWidth; continue; }
                var g = f.Get(c);
                if (g == null) continue;
                for (int yy = 0; yy < g.H; yy++)
                {
                    int py = baseline - g.OriginY + yy;              // from the top
                    if (py < 0 || py >= h) continue;
                    int row = (h - 1 - py) * w;                      // Unity rows from the bottom
                    for (int xx = 0; xx < g.W; xx++)
                    {
                        int px = x - g.OriginX + xx;
                        if (px < 0 || px >= w) continue;
                        int si = g.Pixels + yy * g.W + xx;
                        if (si < 0 || si >= pix.Length) continue;
                        byte v = pix[si];
                        if (v > _px[row + px].a) _px[row + px] = new Color32(255, 255, 255, v);
                    }
                }
                x += g.Advance;
            }
            _tex.SetPixels32(_px);
            _tex.Apply(false, false);
            Image.texture = _tex;
            Image.uvRect = new Rect(0f, 0f, 1f, 1f);
        }

        /// <summary>Places the label: Halo offset from an anchor corner, <paramref name="scale"/> canvas units per font pixel;
        /// <paramref name="align"/> 0 left, 0.5 centre, 1 right of the point.</summary>
        public void Place(HudAnchor a, float x, float y, float scale, float align = 0f)
        {
            if (PixelWidth <= 0) return;
            float w = PixelWidth * scale, h = PixelHeight * scale;
            HaloHudKit.Place(Rect, a, x, y, w, h);
            if (a == HudAnchor.Center || a == HudAnchor.TopCenter || a == HudAnchor.BottomCenter || align > 0f)
            {
                Vector2 p = Rect.pivot;
                Rect.pivot = new Vector2(align, p.y);
            }
        }

        public void Destroy()
        {
            if (_tex != null) Object.Destroy(_tex);
            _tex = null;
        }
    }
}

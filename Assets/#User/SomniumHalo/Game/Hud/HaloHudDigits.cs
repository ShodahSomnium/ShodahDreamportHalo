using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A HUD number in Halo's counter digits (globals "hud digits definition", hud#: sprite i of sequence 0 is digit i, then '.', ':',
    /// '-', 'm'). Each digit cell is "screen digit width" wide, the digit picture scaled by screen / bitmap digit width; leading zeros
    /// fill "maximum number of digits" when the element asks for them, otherwise the number is right-aligned in that many cells.
    /// </summary>
    public sealed class HaloHudDigits
    {
        public const int Dot = 10, Colon = 11, Minus = 12, M = 13;
        readonly RawImage[] _cells;
        readonly int[] _chars;
        int _count;

        public HaloHudDigits(Transform parent, string name, int cells)
        {
            cells = Mathf.Clamp(cells, 1, 12);
            var root = HaloHudKit.Node(parent, name);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero;
            _cells = new RawImage[cells];
            _chars = new int[cells];
            for (int i = 0; i < cells; i++) { _cells[i] = HaloHudKit.Raw(root, "Digit " + i); _cells[i].enabled = false; }
        }

        public void Hide() { for (int i = 0; i < _cells.Length; i++) HaloHudKit.SetOn(_cells[i], false); }

        /// <summary>Lays out the characters of a value (integer part, then the element's fractional digits).</summary>
        int Compose(float value, int maxDigits, int fraction, bool leading, bool trailingM)
        {
            int n = 0;
            bool neg = value < 0f;
            if (neg) value = -value;
            fraction = Mathf.Clamp(fraction, 0, 4);
            long scale = 1; for (int i = 0; i < fraction; i++) scale *= 10;
            long whole = (long)Mathf.Round(value * scale);
            long ip = whole / scale, fp = whole % scale;
            int digits = 0; long t = ip;
            do { digits++; t /= 10; } while (t > 0);
            int want = maxDigits > 0 ? maxDigits : digits;
            if (digits > want) { ip = 1; for (int i = 0; i < want; i++) ip *= 10; ip -= 1; digits = want; }    // clamp to 9..9
            int lead = leading ? want - digits : 0;
            int pad = leading ? 0 : want - digits;            // empty cells on the left (right alignment)
            int total = pad + lead + digits + (neg ? 1 : 0) + (fraction > 0 ? fraction + 1 : 0) + (trailingM ? 1 : 0);
            if (total > _chars.Length) { pad = 0; total = lead + digits + (neg ? 1 : 0) + (fraction > 0 ? fraction + 1 : 0) + (trailingM ? 1 : 0); }
            if (total > _chars.Length) return 0;
            for (int i = 0; i < pad; i++) _chars[n++] = -1;
            if (neg) _chars[n++] = Minus;
            for (int i = 0; i < lead; i++) _chars[n++] = 0;
            int start = n; n += digits;
            for (int i = n - 1; i >= start; i--) { _chars[i] = (int)(ip % 10); ip /= 10; }
            if (fraction > 0)
            {
                _chars[n++] = Dot;
                int fs = n; n += fraction;
                for (int i = n - 1; i >= fs; i--) { _chars[i] = (int)(fp % 10); fp /= 10; }
            }
            if (trailingM) _chars[n++] = M;
            return n;
        }

        /// <summary>Draws <paramref name="value"/> at a number element's placement (anchor corner + offset + extra).</summary>
        public void Draw(HudNumberEl d, HudAnchor a, bool mp, float value, Color color, float extraX = 0f, float extraY = 0f)
        {
            var dg = HaloHudKit.Globals != null ? HaloHudKit.Globals.Digits : null;
            if (d == null || dg == null || dg.Bitmap == null) { Hide(); return; }
            _count = Compose(value, d.MaxDigits, d.Fraction, d.LeadingZeros, d.TrailingM);
            Layout(dg, d, a, mp, color, extraX, extraY);
        }

        /// <summary>A clock "m:ss" with the digits sheet's colon.</summary>
        public void DrawClock(HudNumberEl d, HudAnchor a, int minutes, int seconds, Color color)
        {
            var dg = HaloHudKit.Globals != null ? HaloHudKit.Globals.Digits : null;
            if (d == null || dg == null || dg.Bitmap == null) { Hide(); return; }
            int n = 0;
            minutes = Mathf.Clamp(minutes, 0, 999);
            if (minutes >= 100 && n < _chars.Length) _chars[n++] = minutes / 100;
            if (minutes >= 10 && n < _chars.Length) _chars[n++] = minutes / 10 % 10;
            if (n < _chars.Length) _chars[n++] = minutes % 10;
            if (n + 3 <= _chars.Length) { _chars[n++] = Colon; _chars[n++] = seconds / 10 % 10; _chars[n++] = seconds % 10; }
            _count = n;
            Layout(dg, d, a, false, color, 0f, 0f);
        }

        void Layout(HudDigits dg, HudNumberEl d, HudAnchor a, bool mp, Color color, float extraX, float extraY)
        {
            float s = HaloHudKit.SizeScale(d.Place, mp) * d.Place.WScale, os = HaloHudKit.OffsetScale(d.Place, mp);
            float cell = Mathf.Max(1, dg.ScreenDigitWidth) * s;
            float pic = dg.BitmapDigitWidth > 0 ? (float)dg.ScreenDigitWidth / dg.BitmapDigitWidth : 1f;
            bool rightAnchor = a == HudAnchor.TopRight || a == HudAnchor.BottomRight || a == HudAnchor.RightCenter;
            float y0 = d.Place.Y * os + extraY + dg.YOffset * s;
            // left anchors: cells run right from the offset; right anchors: the offset is the number's right end
            float x = d.Place.X * os + extraX + dg.XOffset * s;
            for (int k = 0; k < _cells.Length; k++)
            {
                int i = rightAnchor ? _count - 1 - k : k;
                if (k >= _count) { HaloHudKit.SetOn(_cells[k], false); continue; }
                int ch = _chars[i];
                var img = _cells[i];
                float advance = ch == Dot ? Mathf.Max(1, dg.DecimalWidth) * s : ch == Colon ? Mathf.Max(1, dg.ColonWidth) * s : cell;
                var sp = ch >= 0 ? HaloHudKit.Sprite(dg.Bitmap, 0, ch) : null;
                if (sp == null) { HaloHudKit.SetOn(img, false); x += advance; continue; }
                img.texture = sp.Tex; img.uvRect = sp.Uv;
                float w = sp.W * pic * s, h = sp.H * pic * s;
                HaloHudKit.Place(img.rectTransform, a, x, y0, w, h);
                img.color = color;
                HaloHudKit.SetOn(img, true);
                x += advance;
            }
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// One Halo HUD meter (shield, health, the bullets of a magazine, heat, battery) drawn the way the tags describe it: the meter bitmap's
    /// colour channels are the shape, its alpha a gradient; a pixel is lit when its gradient is at most the meter's threshold
    /// (value x alpha multiplier + alpha bias; value = rounds for an ammo meter, fraction x value scale otherwise), lit pixels take the
    /// colour between "at minimum" and "at maximum" (or the flash colour), the others the empty colour. Checked against the tags: the
    /// assault rifle's 60 bullets carry gradient 4 x n with multiplier 4 / bias 2, the pistol's 12 carry 20 x n with 20 / 0, the cyborg
    /// shield runs 63..243 with value scale 198 / bias 55. The texture is redrawn on the CPU only when the threshold or colour changes.
    /// </summary>
    public sealed class HaloHudMeter
    {
        public readonly RawImage Image;
        HudMeter _def;
        HaloHudSprite _sprite;
        Texture2D _tex;
        Color32[] _out;
        byte[] _src;
        int _lastThr = -1;
        Color32 _lastLit, _lastEmpty;

        public HaloHudMeter(Transform parent, string name)
        {
            Image = HaloHudKit.Raw(parent, name);
            Image.enabled = false;
        }

        public HudMeter Def { get { return _def; } }

        public void Bind(HudMeter def)
        {
            if (def == _def) return;
            _def = def; _lastThr = -1;
            _sprite = def != null ? HaloHudKit.Sprite(def.Bitmap, def.Sequence) : null;
            _src = _sprite != null ? HaloHudKit.ImagePixels(def.Bitmap, _sprite.Image) : null;
            if (_sprite == null || _src == null) { Image.enabled = false; return; }
            int w = _sprite.PxW, h = _sprite.PxH;
            if (_tex == null || _tex.width != w || _tex.height != h)
            {
                if (_tex != null) Object.Destroy(_tex);
                _tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Halo HUD meter", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _out = new Color32[w * h];
            }
            Image.texture = _tex;
            Image.uvRect = new Rect(0f, 0f, 1f, 1f);
        }

        /// <summary>Lays the meter out at its placement (the sprite's size, the anchor's corner).</summary>
        public void Place(HudAnchor a, bool mp, float extraX = 0f, float extraY = 0f)
        {
            if (_def == null || _sprite == null) return;
            HudPlace p = _def.Place;
            float s = HaloHudKit.SizeScale(p, mp), os = HaloHudKit.OffsetScale(p, mp);
            HaloHudKit.Place(Image.rectTransform, a, p.X * os + extraX, p.Y * os + extraY, _sprite.PxW * s * p.WScale, _sprite.PxH * s * p.HScale);
        }

        /// <summary>The threshold for a value: <paramref name="count"/> &gt;= 0 = a round count (ammo meters), else the fraction.</summary>
        public static int Threshold(HudMeter d, float fraction, int count)
        {
            float thr;
            if (count >= 0) thr = count * Mathf.Max(1, (int)d.AlphaMul) + d.AlphaBias;
            else if (d.ValueScale > 0) thr = fraction * d.ValueScale * Mathf.Max(1, (int)d.AlphaMul) + d.AlphaBias;
            else thr = fraction * 255f;
            if ((fraction > 0f || count > 0) && thr < d.MinValue) thr = d.MinValue;
            if (fraction <= 0f && count <= 0) thr = -1f;
            return Mathf.Clamp(Mathf.RoundToInt(thr), -1, 256);
        }

        /// <summary>Draws the meter. <paramref name="lit"/>/<paramref name="empty"/> are Halo colours (see <see cref="HaloHudKit.Additive"/>).</summary>
        public void Draw(float fraction, int count, HudColor lit, HudColor empty, float alpha)
        {
            if (_def == null || _src == null || _tex == null) { Image.enabled = false; return; }
            int thr = Threshold(_def, fraction, count);
            Color32 cl = HaloHudKit.Additive(lit, alpha), ce = HaloHudKit.Additive(empty, alpha);
            if (thr == _lastThr && Same(cl, _lastLit) && Same(ce, _lastEmpty)) { if (!Image.enabled) Image.enabled = true; return; }
            _lastThr = thr; _lastLit = cl; _lastEmpty = ce;
            int w = _sprite.PxW, h = _sprite.PxH, iw = _sprite.Bitmap.Images[_sprite.Image].Width;
            bool invert = _def.Invert;
            for (int y = 0; y < h; y++)
            {
                int srow = (_sprite.PxY + y) * iw + _sprite.PxX;
                int drow = (h - 1 - y) * w;
                for (int x = 0; x < w; x++)
                {
                    int si = (srow + x) * 4;
                    if (si + 3 >= _src.Length) { _out[drow + x] = new Color32(0, 0, 0, 0); continue; }
                    int shape = _src[si];                         // the colour channels carry the shape (grey)
                    int grad = _src[si + 3];
                    bool on = invert ? grad >= 255 - thr : grad <= thr;
                    Color32 c = on ? cl : ce;
                    _out[drow + x] = new Color32(c.r, c.g, c.b, (byte)(c.a * shape / 255));
                }
            }
            _tex.SetPixels32(_out);
            _tex.Apply(false, false);
            if (!Image.enabled) Image.enabled = true;
        }

        static bool Same(Color32 a, Color32 b) { return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a; }

        public void Hide() { if (Image.enabled) Image.enabled = false; }

        public void Destroy() { if (_tex != null) Object.Destroy(_tex); _tex = null; }
    }
}

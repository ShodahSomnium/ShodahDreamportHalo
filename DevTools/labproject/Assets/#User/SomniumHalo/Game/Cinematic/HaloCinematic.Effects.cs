using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// cinematic_screen_effect_*: Halo's cutscene screen filters (a10's end of x10: blur + desaturation tinted purple while Cortana
    /// appears; c10's record view video effect). Kept as state; the view shows the desaturation tint as a translucent wash over the
    /// cutscene picture (ours: no screen shader is shipped for a true desaturation / convolution blur).
    /// </summary>
    public static partial class HaloCinematic
    {
        public static bool ScreenEffectOn { get; private set; }
        static float _desatStart, _desatEnd, _lightStart, _lightEnd, _filterTicks, _filterAge;
        static bool _filterInvert;
        static Color _desatTint = Color.white;

        /// <summary>cinematic_screen_effect_start (true = start cleared).</summary>
        public static void ScreenEffectStart(bool clear)
        {
            ScreenEffectOn = true;
            if (clear) { _desatStart = _desatEnd = 0f; _lightStart = _lightEnd = 0f; _filterTicks = 0f; _filterAge = 0f; _desatTint = Color.white; }
        }

        /// <summary>cinematic_screen_effect_stop.</summary>
        public static void ScreenEffectStop() { ScreenEffectOn = false; _desatStart = _desatEnd = 0f; }

        /// <summary>cinematic_screen_effect_set_convolution (iterations, mode, radius start, radius end, seconds): not drawn (logged once).</summary>
        public static void ScreenEffectConvolution(int iterations, int mode, float radiusStart, float radiusEnd, float seconds)
        {
            Once("convolution", "Cinematic: unimplemented cinematic_screen_effect_set_convolution (blur) - the picture stays sharp");
        }

        /// <summary>cinematic_screen_effect_set_filter (desaturation start, end, light enhancement start, end, invert, seconds).</summary>
        public static void ScreenEffectFilter(float desaturationStart, float desaturationEnd, float lightStart, float lightEnd, bool invert, float seconds)
        {
            ScreenEffectOn = true;
            _desatStart = desaturationStart; _desatEnd = desaturationEnd; _lightStart = lightStart; _lightEnd = lightEnd; _filterInvert = invert;
            _filterTicks = Mathf.Max(0f, seconds) * TicksPerSecond; _filterAge = 0f;
            Once("filter", "Cinematic: cinematic_screen_effect_set_filter shown as a tint wash (ours: no desaturation shader)");
        }

        /// <summary>cinematic_screen_effect_set_filter_desaturation_tint (r, g, b).</summary>
        public static void ScreenEffectDesaturationTint(float r, float g, float b) { _desatTint = new Color(r, g, b, 1f); }

        /// <summary>cinematic_screen_effect_set_video (noise intensity, overbright): not drawn (logged once).</summary>
        public static void ScreenEffectVideo(int noise, float overbright)
        {
            Once("video", "Cinematic: unimplemented cinematic_screen_effect_set_video");
        }

        /// <summary>The wash the view lays over the cutscene picture for the desaturation filter (alpha 0 = none).</summary>
        internal static Color ScreenWash()
        {
            if (!ScreenEffectOn) return Color.clear;
            if (_filterTicks > 0f && _filterAge < _filterTicks) _filterAge += Time.deltaTime * TicksPerSecond;
            float k = _filterTicks > 0f ? Mathf.Clamp01(_filterAge / _filterTicks) : 1f;
            float desat = Mathf.Lerp(_desatStart, _desatEnd, k);
            if (desat <= 0.01f) return Color.clear;
            var c = _desatTint;
            c.a = Mathf.Clamp01(desat) * 0.45f; // ours
            return c;
        }
    }
}

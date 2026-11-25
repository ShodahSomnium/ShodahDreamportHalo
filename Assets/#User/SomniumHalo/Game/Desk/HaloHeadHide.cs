using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Monitor first person: the own avatar's head shrunk away on this screen only, for the frame being drawn (BO2's Bo2HeadHide: the client never hides
    /// it, and its first-person camera sits inside it). Shrunk in LateUpdate after the pose, given back when every camera has drawn
    /// (RenderPipelineManager.endContextRendering): nothing is networked from it, others see the head, and the mirror copy (rotations only) keeps it.
    /// </summary>
    public static class HaloHeadHide
    {
        /// <summary>Lab: keep it shrunk between frames (the lab draws its pictures outside the frame's rendering).</summary>
        public static bool LabHold;
        private static Transform _head;
        private static Vector3 _scale;
        private static bool _hooked;

        public static bool Hidden { get { return _head != null; } }

        public static void Hide(Transform head)
        {
            if (head == null) return;
            if (_head != null && _head != head) Restore();
            if (_head == null)
            {
                _head = head;
                _scale = head.localScale;
            }

            head.localScale = _scale * 0.0001f;
            if (!_hooked)
            {
                _hooked = true;
                RenderPipelineManager.endContextRendering += OnEnd;
            }
        }

        private static void OnEnd(ScriptableRenderContext context, List<Camera> cams)
        {
            if (!LabHold) Restore();
        }

        public static void Restore()
        {
            try { if (_head != null) _head.localScale = _scale; }
            catch (System.Exception) { /* the avatar went away */ }
            _head = null;
        }

        /// <summary>The match is over: unhook (the static event would otherwise outlive the world).</summary>
        public static void Unhook()
        {
            Restore();
            if (_hooked) { RenderPipelineManager.endContextRendering -= OnEnd; _hooked = false; }
        }
    }
}

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The held weapon drawn at another scale on this screen only, for the frame being drawn (first person: shrunk toward the eye so the hands reach it
    /// with the same picture; hidden while zoomed, as Halo hides its view model): set in LateUpdate, given back when every camera has drawn.
    /// </summary>
    public static class HaloViewScale
    {
        public static bool LabHold;
        private static Transform _t;
        private static Vector3 _scale;
        private static bool _hooked;

        public static void Set(Transform t, float factor)
        {
            if (t == null) return;
            if (_t != null && _t != t) Restore();
            if (_t == null) { _t = t; _scale = t.localScale; }
            t.localScale = _scale * factor;
            if (!_hooked)
            {
                _hooked = true;
                RenderPipelineManager.endContextRendering += OnEnd;
            }
        }

        private static void OnEnd(ScriptableRenderContext context, List<Camera> cams)
        {
            if (!LabHold) Restore();
        }

        public static void Restore()
        {
            try { if (_t != null) _t.localScale = _scale; }
            catch (System.Exception) { /* the weapon went away */ }
            _t = null;
        }

        public static void Unhook()
        {
            Restore();
            if (_hooked) { RenderPipelineManager.endContextRendering -= OnEnd; _hooked = false; }
        }
    }
}

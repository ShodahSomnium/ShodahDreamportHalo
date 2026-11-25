using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The scripted cutscene camera: camera_set (a camera point, moving over N ticks), camera_set_relative (the point is in an object's
    /// frame and the camera rides with it), camera_set_first_person (a unit's eyes), camera_set_dead (circling a unit), camera_time.
    /// A move eases from the current pose to the point with a cubic Hermite in progress: from rest it starts and stops gently (smoothstep);
    /// a move given while another runs starts with the camera's current speed along the new path, so Bungie's chained moves (camera_set
    /// b 300, sleep 150, camera_set c 300) flow without a hitch. Halo's exact curve is not verified (ours). Position and field of view
    /// follow the progress linearly, orientation by slerp. Camera points' field of view is Halo's horizontal one at 4:3.
    /// </summary>
    public static partial class HaloCinematic
    {
        enum CamMode { Points, FirstPerson, Dead }

        static CamMode _mode;
        static bool _camValid;
        // the move (world space, or the relative object's local frame when _relative is set)
        static Vector3 _fromPos, _toPos;
        static Quaternion _fromRot = Quaternion.identity, _toRot = Quaternion.identity;
        static float _fromFov = 70f, _toFov = 70f, _m0;
        static int _moveTicks, _moveElapsed;
        static Transform _relative;
        static GameObject _follow;
        static int _deadTicks;
        // last two ticks' poses (world) for the camera's speed
        static Vector3 _tickPos, _lastTickPos;
        static Quaternion _tickRot = Quaternion.identity, _lastTickRot = Quaternion.identity;

        /// <summary>camera_time: ticks left in the camera's current move.</summary>
        public static int CameraTime => Mathf.Max(0, _moveTicks - _moveElapsed);

        /// <summary>camera_set: move the cutscene camera to a named camera point over <paramref name="ticks"/> (30 per second).</summary>
        public static void CameraSet(string cameraPoint, int ticks)
        {
            HaloCinemaView.Ensure();
            var d = D;
            var p = d != null ? d.Camera(cameraPoint) : null;
            if (p == null) { Once("cam?" + cameraPoint, "Cinematic: no cutscene camera point \"" + cameraPoint + "\""); return; }
            MoveTo(p, ticks, null);
        }

        /// <summary>camera_set by camera point index (co-op clients mirroring the host).</summary>
        public static void CameraSet(int pointIndex, int ticks)
        {
            HaloCinemaView.Ensure();
            var d = D;
            var p = d != null ? d.Camera(pointIndex) : null;
            if (p != null) MoveTo(p, ticks, null);
        }

        /// <summary>camera_set_relative: the point's position / orientation are in <paramref name="relativeTo"/>'s frame (it rides along).</summary>
        public static void CameraSetRelative(string cameraPoint, int ticks, GameObject relativeTo)
        {
            HaloCinemaView.Ensure();
            var d = D;
            var p = d != null ? d.Camera(cameraPoint) : null;
            if (p == null) { Once("cam?" + cameraPoint, "Cinematic: no cutscene camera point \"" + cameraPoint + "\""); return; }
            if (relativeTo == null) { Once("camrel?" + cameraPoint, "Cinematic: camera_set_relative " + cameraPoint + " without an object - absolute"); MoveTo(p, ticks, null); return; }
            MoveTo(p, ticks, relativeTo.transform);
        }

        /// <summary>camera_set_first_person: the camera looks through a unit's eyes.</summary>
        public static void CameraSetFirstPerson(GameObject unit)
        {
            HaloCinemaView.Ensure();
            if (unit == null) return;
            _mode = CamMode.FirstPerson; _follow = unit; _relative = null; _moveTicks = 0; _moveElapsed = 0; CameraPoint = -1;
            _camValid = true;
        }

        /// <summary>camera_set_dead: the camera backs away and circles a unit as if it were dead.</summary>
        public static void CameraSetDead(GameObject unit)
        {
            HaloCinemaView.Ensure();
            if (unit == null) return;
            _mode = CamMode.Dead; _follow = unit; _relative = null; _moveTicks = 0; _moveElapsed = 0; _deadTicks = 0; CameraPoint = -1;
            _camValid = true;
            Once("dead", "Cinematic: camera_set_dead (circling the unit; ours: Halo's dead camera path not decoded)");
        }

        static void MoveTo(HaloCutsceneData.CameraPoint p, int ticks, Transform relative)
        {
            Vector3 pos; Quaternion rot;
            float fov = Mathf.Clamp(p.FieldOfView * Mathf.Rad2Deg, 1f, 170f);
            if (relative != null)
            {
                // Halo object frame (x forward, y left, z up) -> the object's Unity local frame (+Z forward, -X left, +Y up)
                pos = new Vector3(-p.Position.Y, p.Position.Z, p.Position.X) * HaloSpace.Unit;
                rot = LocalFrame * HaloSpace.Euler(p.Yaw, p.Pitch, p.Roll);
            }
            else
            {
                pos = ToWorld(HaloSpace.Pos(p.Position));
                rot = HaloSpace.Euler(p.Yaw, p.Pitch, p.Roll);
                var root = HaloSession.WorldRoot;
                if (root != null) rot = root.rotation * rot;
            }

            Vector3 curPos = pos; Quaternion curRot = rot; float curFov = fov;
            if (!_camValid && ticks > 0) StartAtPlayer();
            if (_camValid)
            {
                Vector3 wp; Quaternion wr; float wf;
                PoseAt(0f, out wp, out wr, out wf);
                curPos = WorldToFrame(wp, relative); curRot = RotToFrame(wr, relative); curFov = wf;
            }

            // the camera's current speed along the new path keeps chained moves smooth
            float m0 = 0f;
            if (ticks > 0 && _camValid && _mode == CamMode.Points && _moveElapsed < _moveTicks)
            {
                Vector3 v = _tickPos - _lastTickPos;
                Vector3 dWorld = FrameToWorld(pos, relative) - FrameToWorld(curPos, relative);
                if (dWorld.sqrMagnitude > 1e-4f) m0 = Vector3.Dot(v, dWorld) / dWorld.sqrMagnitude * ticks;
                else
                {
                    float total = Quaternion.Angle(curRot, rot), w = Quaternion.Angle(_lastTickRot, _tickRot);
                    if (total > 0.01f) m0 = w / total * ticks;
                }
                m0 = Mathf.Clamp(m0, 0f, 3f);
            }

            _mode = CamMode.Points;
            _follow = null;
            _relative = relative;
            CameraPoint = p.Index;
            _fromPos = curPos; _fromRot = curRot; _fromFov = curFov;
            _toPos = pos; _toRot = rot; _toFov = fov;
            _moveTicks = Mathf.Max(0, ticks); _moveElapsed = 0; _m0 = m0;
            if (_moveTicks == 0) { _fromPos = pos; _fromRot = rot; _fromFov = fov; }
            _camValid = true;
            if (_moveTicks == 0) { Vector3 w; Quaternion r; float f; PoseAt(0f, out w, out r, out f); _tickPos = _lastTickPos = w; _tickRot = _lastTickRot = r; }
        }

        static readonly Quaternion LocalFrame = Quaternion.Euler(0f, -90f, 0f);

        static Vector3 WorldToFrame(Vector3 world, Transform frame)
        {
            if (frame == null) return world;
            return Quaternion.Inverse(frame.rotation) * (world - frame.position);
        }

        static Vector3 FrameToWorld(Vector3 local, Transform frame) { return frame == null ? local : frame.position + frame.rotation * local; }
        static Quaternion RotToFrame(Quaternion world, Transform frame) { return frame == null ? world : Quaternion.Inverse(frame.rotation) * world; }

        static void StartAtPlayer()
        {
            Camera head = HaloPlayer.Head();
            if (head == null) return;
            _tickPos = _lastTickPos = head.transform.position;
            _tickRot = _lastTickRot = Quaternion.Euler(0f, head.transform.eulerAngles.y, 0f);
            _fromPos = _toPos = _tickPos; _fromRot = _toRot = _tickRot; _fromFov = _toFov = 70f;
            _moveTicks = 0; _moveElapsed = 0; _relative = null; _mode = CamMode.Points;
            _camValid = true;
        }

        static void CameraStep()
        {
            if (!_camValid) return;
            if (_moveElapsed < _moveTicks) _moveElapsed++;
            if (_mode == CamMode.Dead) _deadTicks++;
            _lastTickPos = _tickPos; _lastTickRot = _tickRot;
            float f;
            PoseAt(0f, out _tickPos, out _tickRot, out f);
        }

        /// <summary>The cutscene camera's pose now (between ticks by <see cref="TickFraction"/>): Unity world position, rotation and the
        /// horizontal field of view (degrees, Halo's 4:3). False when the camera has never been set.</summary>
        public static bool CameraPose(out Vector3 position, out Quaternion rotation, out float horizontalFov)
        {
            if (!_camValid) { position = Vector3.zero; rotation = Quaternion.identity; horizontalFov = 70f; return false; }
            PoseAt(TickFraction, out position, out rotation, out horizontalFov);
            return true;
        }

        static void PoseAt(float fraction, out Vector3 pos, out Quaternion rot, out float fov)
        {
            if (_mode == CamMode.FirstPerson && _follow != null)
            {
                var u = _follow.GetComponent<HaloUnit>();
                pos = u != null ? u.Eye : _follow.transform.position + Vector3.up * 1.6f;
                Vector3 look = u != null && (u.AimPoint - pos).sqrMagnitude > 0.01f ? u.AimPoint - pos : _follow.transform.forward;
                rot = Quaternion.LookRotation(look, Vector3.up);
                fov = 70f;
                return;
            }
            if (_mode == CamMode.Dead && _follow != null)
            {
                // ours: rise and back away from the body over ~3 s while circling slowly
                Vector3 c = _follow.transform.position + Vector3.up * 0.5f;
                float t = _deadTicks + fraction, k = Mathf.Clamp01(t / 90f);
                float dist = Mathf.Lerp(1.5f, 4.5f, k * (2f - k)), up = Mathf.Lerp(1f, 3f, k);
                float ang = t * 0.4f * Mathf.Deg2Rad;
                Vector3 back = -_follow.transform.forward;
                Vector3 dir = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * back;
                pos = c + dir * dist + Vector3.up * up;
                rot = Quaternion.LookRotation(c - pos, Vector3.up);
                fov = 70f;
                return;
            }
            float s = 1f;
            if (_moveTicks > 0)
            {
                float u = Mathf.Clamp01((_moveElapsed + (_moveElapsed < _moveTicks ? fraction : 0f)) / _moveTicks);
                float u2 = u * u, u3 = u2 * u;
                s = (u3 - 2f * u2 + u) * _m0 + (-2f * u3 + 3f * u2);
            }
            Vector3 lp = Vector3.LerpUnclamped(_fromPos, _toPos, s);
            Quaternion lr = Quaternion.SlerpUnclamped(_fromRot, _toRot, Mathf.Clamp(s, -0.25f, 1.25f));
            fov = Mathf.LerpUnclamped(_fromFov, _toFov, Mathf.Clamp01(s));
            if (_relative != null)
            {
                pos = _relative.position + _relative.rotation * lp;
                rot = _relative.rotation * lr;
            }
            else { pos = lp; rot = lr; }
        }
    }
}

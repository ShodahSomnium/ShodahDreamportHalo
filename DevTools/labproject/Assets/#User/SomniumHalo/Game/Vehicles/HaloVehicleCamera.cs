using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The monitor view while seated. Like the client's own desktop vehicle view (NonXRCameraFeature: a chase orbit round the
    /// vehicle's bounds centre, orbit by mouse, collision against world geometry only - layers 0 / 14 / 16 / 23, radius 0.1 m) with
    /// Halo's numbers: the seat's camera track (trak control points: the camera's offset from the anchor as the look pitch goes from
    /// straight down to straight up; the warthog's middle point is 8.4 m behind and 2.1 m above) orbiting with the look yaw, and the
    /// vehicle steering toward where the camera looks (Halo's control). Seats without Halo's third-person flag look from the seat's eye.
    /// The client's camera rig is held off meanwhile: its CinemachineBrain (found by type name - a Unity package, not obfuscated, as
    /// EtsTruckCamera does) is disabled and given back on release; the camera object is written here after everything else.
    /// </summary>
    public static class HaloVehicleCamera
    {
        public static float Yaw, Pitch;      // degrees: look yaw (Unity, about +y) and pitch (up positive)
        public static Vector3 AimPoint;      // what the crosshair is on (the gun aims there)
        public static bool Active { get; private set; }

        private static Camera _camera;
        private static Behaviour _brain;
        private static bool _known, _lockedHere, _axesBroken, _said;
        private static CursorLockMode _lockWas;
        private static bool _visibleWas;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private const int ObstacleMask = (1 << 0) | (1 << 14) | (1 << 16) | (1 << 23); // the client's CinemachineCollider layers + Halo's world

        public static float Sensitivity => HaloSettings.Num("vehicles.mouse", 2.2f);

        /// <summary>Start looking from behind the vehicle (called on getting in).</summary>
        public static void Reset(HaloVehicle v)
        {
            Vector3 f = Vector3.ProjectOnPlane(v.transform.forward, Vector3.up);
            Yaw = f.sqrMagnitude > 1e-6f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            Pitch = -8f;
            _known = true;
        }

        /// <summary>Reads the mouse (Update): the look yaw / pitch within the seat's pitch range.</summary>
        public static void Look(HaloVehicle.SeatInfo seat)
        {
            if (!_known) return;
            float dx = 0f, dy = 0f;
            if (!_axesBroken)
            {
                try { dx = Input.GetAxisRaw("Mouse X"); dy = Input.GetAxisRaw("Mouse Y"); }
                catch (Exception) { _axesBroken = true; }
            }
            if (_axesBroken) { Vector3 d = Input.mousePositionDelta; dx = d.x * 0.05f; dy = d.y * 0.05f; }
            Yaw = Mathf.Repeat(Yaw + dx * Sensitivity + 180f, 360f) - 180f;
            float min = seat != null && seat.Def.PitchMin < 0f ? seat.Def.PitchMin * Mathf.Rad2Deg : -60f;
            float max = seat != null && seat.Def.PitchMax > 0f ? seat.Def.PitchMax * Mathf.Rad2Deg : 60f;
            // Halo's third-person orbit spans the whole track (straight down to straight up); clamp to the seat's range plus the track's
            Pitch = Mathf.Clamp(Pitch + dy * Sensitivity, Mathf.Min(min, -75f), Mathf.Max(max, 60f));
        }

        public static Vector3 LookDirection => Quaternion.Euler(-Pitch, Yaw, 0f) * Vector3.forward;

        /// <summary>Places the camera (LateUpdate, late). Third person: the track; first person: the seat's eye.</summary>
        public static void Place(HaloVehicle v, HaloVehicle.SeatInfo seat, bool thirdPerson, Vector3 eye)
        {
            if (_camera == null || !_camera.isActiveAndEnabled) _camera = HaloPlayer.Head();
            if (_camera == null) return;
            Hold();
            Active = true;
            Quaternion look = Quaternion.Euler(-Pitch, Yaw, 0f);
            Vector3 pos;
            if (thirdPerson)
            {
                Vector3 anchor = Anchor(v, seat);
                Vector3 offset = TrackOffset(v, seat, Pitch);
                Vector3 wanted = anchor + Quaternion.Euler(0f, Yaw, 0f) * offset;
                pos = Unblocked(anchor, wanted, v);
            }
            else pos = eye;
            _camera.transform.SetPositionAndRotation(pos, look);
            // the crosshair's target: what the view ray hits past the vehicle
            AimPoint = pos + look * Vector3.forward * 400f;
            int n = Physics.RaycastNonAlloc(pos, look * Vector3.forward, Hits, 400f, HaloLayers.ShotMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(v.transform) || h.distance < 0.5f) continue;
                if (HaloPlayer.Root != null && h.collider.transform.IsChildOf(HaloPlayer.Root)) continue;
                if (h.distance < best) { best = h.distance; AimPoint = h.point; }
            }
            if (!_said) { _said = true; HaloLog.Info("Vehicles: monitor vehicle camera on (" + (thirdPerson ? "Halo chase track " + TrackName(v, seat) : "seat eye") + "); the client's camera rig is held off" + (_brain != null ? " (CinemachineBrain disabled)" : "") + "."); }
        }

        private static string TrackName(HaloVehicle v, HaloVehicle.SeatInfo seat)
        {
            var t = seat != null && seat.Def.Track != null ? seat.Def.Track : v.Def.Track;
            return t != null ? t.Tag.Name : "(none: ours)";
        }

        /// <summary>The orbit's centre: the seat's camera marker (the warthog gunner's "camera gunner"), else the vehicle's centre (the client's).</summary>
        public static Vector3 Anchor(HaloVehicle v, HaloVehicle.SeatInfo seat)
        {
            if (seat != null && !string.IsNullOrEmpty(seat.Def.CameraMarker) && seat.Camera != null) return seat.Camera.position;
            Bounds b = v.ShapeBounds();
            return new Vector3(v.transform.position.x, b.center.y, v.transform.position.z);
        }

        /// <summary>The track's offset at a look pitch, in the yaw frame (metres): control points from looking down (-90) to up (+90).</summary>
        public static Vector3 TrackOffset(HaloVehicle v, HaloVehicle.SeatInfo seat, float pitch)
        {
            var track = seat != null && seat.Def.Track != null ? seat.Def.Track : v.Def.Track;
            if (track == null || track.Points.Count < 2) return new Vector3(0f, 1.74f, -7.3f); // ours: the client's default vehicle view
            float t = Mathf.Clamp01((pitch + 90f) / 180f) * (track.Points.Count - 1);
            int i = Mathf.Min(track.Points.Count - 2, Mathf.FloorToInt(t));
            float f = t - i;
            HVec3 a = track.Points[i], b = track.Points[i + 1];
            var h = new HVec3(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, a.Z + (b.Z - a.Z) * f);
            return HaloVehicle.ModelToRoot(h);
        }

        private static Vector3 Unblocked(Vector3 from, Vector3 to, HaloVehicle v)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return to;
            int n = Physics.SphereCastNonAlloc(from, 0.1f, d / len, Hits, len, ObstacleMask, QueryTriggerInteraction.Ignore);
            float best = len;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null || h.collider.transform.IsChildOf(v.transform) || h.distance <= 0f) continue;
                if (h.distance < best) best = h.distance;
            }
            return from + d / len * Mathf.Max(0.1f, best);
        }

        private static void Hold()
        {
            if (_brain == null || _brain.gameObject != _camera.gameObject)
            {
                GiveBack();
                _brain = _camera.GetComponent("CinemachineBrain") as Behaviour;
            }
            if (_brain != null && _brain.enabled) _brain.enabled = false;
            if (!Application.isEditor && !_lockedHere && Cursor.lockState != CursorLockMode.Locked)
            {
                _lockWas = Cursor.lockState; _visibleWas = Cursor.visible;
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
                _lockedHere = true;
            }
        }

        private static void GiveBack()
        {
            if (_brain != null && !_brain.enabled) _brain.enabled = true;
            _brain = null;
        }

        /// <summary>Hands the camera back to the client (getting out).</summary>
        public static void Release()
        {
            GiveBack();
            if (_lockedHere) { Cursor.lockState = _lockWas; Cursor.visible = _visibleWas; _lockedHere = false; }
            Active = false; _known = false; _said = false;
        }
    }
}

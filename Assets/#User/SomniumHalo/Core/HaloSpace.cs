using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Halo space (right-handed, Z up, units = world units; 1 world unit = 10 feet = 3.048 m) to Unity space (left-handed, Y up, metres).
    /// The mapping is the plain swap of Y and Z (a reflection, so handedness changes as it must): (x, y, z)halo -> (x, z, y)unity * 3.048.
    /// A reflection flips the winding of every triangle: mesh builders reverse index order (verified per builder by a render).
    /// Rotations: a Halo quaternion (i, j, k, w) about the swapped axes maps to (-i, -k, -j, w); Halo stores the INVERSE (conjugate)
    /// rotation in model nodes and animations, which each reader states where it applies.
    /// </summary>
    public static class HaloSpace
    {
        /// <summary>Metres per Halo world unit.</summary>
        public const float Unit = 3.048f;

        /// <summary>The ring hub (menu room) hangs 3 km above the maps so the two never overlap (as the other Dreamports).</summary>
        public static readonly Vector3 RoomOrigin = new Vector3(0f, 3000f, 0f);

        /// <summary>Where a match's map is built (map coordinates are added to this).</summary>
        public static readonly Vector3 MapOrigin = Vector3.zero;

        public static Vector3 Pos(float x, float y, float z) { return new Vector3(x * Unit, z * Unit, y * Unit); }
        public static Vector3 Pos(HVec3 h) { return Pos(h.X, h.Y, h.Z); }
        public static Vector3 Dir(float x, float y, float z) { return new Vector3(x, z, y); }
        public static Vector3 Dir(HVec3 h) { return new Vector3(h.X, h.Z, h.Y); }
        public static Vector3 ToHalo(Vector3 u) { return new Vector3(u.x / Unit, u.z / Unit, u.y / Unit); }
        public static Vector3 DirToHalo(Vector3 u) { return new Vector3(u.x, u.z, u.y); }

        /// <summary>A Halo rotation quaternion (i, j, k, w; right-handed, Z up) as a Unity rotation.</summary>
        public static Quaternion Rot(float i, float j, float k, float w) { return new Quaternion(-i, -k, -j, w); }

        /// <summary>Halo yaw (radians, about +Z, 0 = +X) as a Unity rotation about Y.</summary>
        public static Quaternion Yaw(float radians) { return Quaternion.Euler(0f, 90f - radians * Mathf.Rad2Deg, 0f); }

        /// <summary>Halo yaw/pitch/roll (radians) as a Unity rotation: forward = (cos y cos p, sin y cos p, sin p) in Halo axes.</summary>
        public static Quaternion Euler(float yaw, float pitch, float roll)
        {
            Vector3 f = Dir(Mathf.Cos(yaw) * Mathf.Cos(pitch), Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch));
            Quaternion q = Quaternion.LookRotation(f, Vector3.up);
            return q * Quaternion.AngleAxis(-roll * Mathf.Rad2Deg, Vector3.forward);
        }

        /// <summary>A Halo forward + up pair as a Unity rotation.</summary>
        public static Quaternion Basis(HVec3 forward, HVec3 up)
        {
            Vector3 f = Dir(forward), u = Dir(up);
            if (f.sqrMagnitude < 1e-8f) return Quaternion.identity;
            if (u.sqrMagnitude < 1e-8f) u = Vector3.up;
            return Quaternion.LookRotation(f, u);
        }
    }
}

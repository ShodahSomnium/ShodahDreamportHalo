using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The other copies of a posed avatar model. The client keeps one model per "root" of an avatar holder (the Skyrim Dreamport's GearAvatarRoots): the
    /// MAIN copy the main camera draws (the one HaloDeskBody poses), the MIRROR copy that mirrors and the tablet's camera draw (with its own Animator: it kept
    /// playing the client's idle, so a mirror or a photo showed the player standing idle), the TELEPORT preview and the WEARABLES copy (the local player's
    /// shadow; the client copies the main bones into it at LateUpdate order 0, before our pose). After the main copy is posed, its bones' local rotations
    /// (and the hips' local position) are copied onto every other copy, bone by humanoid bone. Scale is not copied (the own head / arms hidden on this screen
    /// by shrinking stay whole in a mirror). Allocates only when it binds.
    /// </summary>
    public sealed class HaloAvatarCopies
    {
        private const string Mirror = "MirrorRoot", Teleport = "TeleportRoot", Wearables = "WearablesRoot";
        private static readonly List<MonoBehaviour> Buf = new List<MonoBehaviour>(64);

        private Transform[] _src = new Transform[0], _dst = new Transform[0];
        private Transform _hipsSrc;
        private Transform[] _hipsDst = new Transform[0];

        public Animator Main { get; private set; }
        /// <summary>How many other copies were bound (for the log).</summary>
        public int Copies { get; private set; }

        /// <summary>Binds the copies of the avatar this main copy belongs to (searched under its holder); an empty binding when there is none.</summary>
        public static HaloAvatarCopies Bind(Animator main)
        {
            var c = new HaloAvatarCopies { Main = main };
            if (main == null || !main.isHuman) return c;
            Transform holder = HolderOf(main.transform);
            if (holder == null) return c;
            var src = new List<Transform>(64);
            var dst = new List<Transform>(64);
            var hips = new List<Transform>(4);
            Buf.Clear();
            holder.GetComponentsInChildren(true, Buf);
            for (int i = 0; i < Buf.Count; i++)
            {
                MonoBehaviour m = Buf[i];
                if (m == null) continue;
                string n = m.GetType().Name;
                if (n != Mirror && n != Teleport && n != Wearables) continue;
                Animator a = m.GetComponentInChildren<Animator>(true);
                if (a == null || a == main || !a.isHuman || a.avatar == null) continue;
                int before = src.Count;
                for (int b = 0; b < (int)HumanBodyBones.LastBone; b++)
                {
                    Transform s = main.GetBoneTransform((HumanBodyBones)b), d = a.GetBoneTransform((HumanBodyBones)b);
                    if (s == null || d == null || s == d) continue;
                    src.Add(s);
                    dst.Add(d);
                    if (b == (int)HumanBodyBones.Hips) { c._hipsSrc = s; hips.Add(d); }
                }

                if (src.Count > before) c.Copies++;
            }

            Buf.Clear();
            c._src = src.ToArray();
            c._dst = dst.ToArray();
            c._hipsDst = hips.ToArray();
            return c;
        }

        /// <summary>After the main copy was posed this frame: the same pose on the other copies.</summary>
        public void Copy()
        {
            for (int i = 0; i < _src.Length; i++)
            {
                Transform s = _src[i], d = _dst[i];
                if (s == null || d == null) continue;
                d.localRotation = s.localRotation;
            }

            if (_hipsSrc != null)
                for (int i = 0; i < _hipsDst.Length; i++)
                    if (_hipsDst[i] != null) _hipsDst[i].localPosition = _hipsSrc.localPosition;
        }

        private static Transform HolderOf(Transform t)
        {
            for (int up = 0; t != null && up <= 12; up++, t = t.parent)
            {
                Buf.Clear();
                t.GetComponents(Buf);
                for (int i = 0; i < Buf.Count; i++)
                    if (Buf[i] != null && Buf[i].GetType().Name.EndsWith("RootsHolder", System.StringComparison.Ordinal)) { Buf.Clear(); return t; }
            }

            Buf.Clear();
            return null;
        }
    }
}

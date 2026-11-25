using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Finds a Somnium player's avatar model the way the Skyrim Dreamport does (GearBody.Find / GearAvatarRoots, proven in the client): the avatar is NOT under
    /// the player's object (Player.log of iteration 16: under 'Original Player: [Shodah] ...' only the tablet's and UI Animators). The client instantiates the
    /// model under every "root" of an avatar holder somewhere in the scene: NonXRMainRoot / MainRoot (what the main camera draws), MirrorRoot (mirrors and
    /// the tablet camera), TeleportRoot, WearablesRoot (shadows only), each with its own humanoid Animator; a far or loading avatar shows a stand-in
    /// (CullingRootsHolder / DummyRootsHolder). Those MonoBehaviour type names survive the client's obfuscation. So: every active humanoid Animator in the scene
    /// whose head bone is within 0.6 m of the player's head; a main copy of the player's own avatar wins, then one of no client root, then a stand-in's
    /// main copy, then any other copy. Allocates: only when called (a search, at most once a second per player).
    /// </summary>
    public static class HaloNetAvatar
    {
        private const string Main = "MainRoot", NonXrMain = "NonXRMainRoot", Mirror = "MirrorRoot", Teleport = "TeleportRoot", Wearables = "WearablesRoot";
        private const string CullingHolder = "CullingRootsHolder", DummyHolder = "DummyRootsHolder";
        private static readonly List<MonoBehaviour> Buf = new List<MonoBehaviour>(16);

        /// <summary>The humanoid Animator of the avatar whose head is at headPos (null when none within 0.6 m); kind = which copy it is (for the log).</summary>
        public static Animator ByHead(Vector3 headPos, out string kind)
        {
            Animator best = null, other = null, stand = null, second = null;
            float bestD = 0.6f, otherD = 0.6f, standD = 0.6f, secondD = 0.6f;
            Animator[] all = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Animator a = all[i];
                if (a == null || !a.gameObject.activeInHierarchy || !a.isHuman || a.avatar == null) continue;
                Transform h = a.GetBoneTransform(HumanBodyBones.Head);
                if (h == null) continue;
                float d = (h.position - headPos).magnitude;
                if (d >= 0.6f) continue;
                string k = KindOf(a.transform);
                bool main = k == Main || k == NonXrMain;
                if (main && IsStandIn(a.transform)) { if (d < standD) { standD = d; stand = a; } }
                else if (main) { if (d < bestD) { bestD = d; best = a; } }
                else if (k == null) { if (d < otherD) { otherD = d; other = a; } }
                else if (d < secondD) { secondD = d; second = a; }
            }

            Animator pick = best ?? other ?? stand ?? second;
            kind = pick == null ? "none" : pick == best ? "main copy" : pick == other ? "no client root" : pick == stand ? "the client's stand-in" : "a secondary copy (" + KindOf(pick.transform) + ")";
            return pick;
        }

        /// <summary>The client root type on this object or up to 3 parents above it, null when none.</summary>
        public static string KindOf(Transform t)
        {
            for (int up = 0; t != null && up <= 3; up++, t = t.parent)
            {
                Buf.Clear();
                t.GetComponents(Buf);
                for (int i = 0; i < Buf.Count; i++)
                {
                    if (Buf[i] == null) continue;
                    string n = Buf[i].GetType().Name;
                    if (n == Main || n == NonXrMain || n == Mirror || n == Teleport || n == Wearables) { Buf.Clear(); return n; }
                }
            }

            Buf.Clear();
            return null;
        }

        private static bool IsStandIn(Transform t)
        {
            for (int up = 0; t != null && up <= 12; up++, t = t.parent)
            {
                Buf.Clear();
                t.GetComponents(Buf);
                for (int i = 0; i < Buf.Count; i++)
                {
                    if (Buf[i] == null) continue;
                    string n = Buf[i].GetType().Name;
                    if (!n.EndsWith("RootsHolder", System.StringComparison.Ordinal)) continue;
                    Buf.Clear();
                    return n == CullingHolder || n == DummyHolder;
                }
            }

            Buf.Clear();
            return false;
        }
    }
}

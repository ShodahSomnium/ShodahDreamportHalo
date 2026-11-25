using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A weapon's own model animations, for its third-person model (the one HaloModelBuilder builds from the weap tag's model): the moving parts
    /// (pistol slide / magazine / slide release, sniper bolt ("ophandle") and magazine, shotgun pump, AR charging handle, rocket launcher tubes /
    /// body / push bar, plasma rifle rods and vents, plasma pistol cover, needler body) as the game animates them. Two sources, both the game's:
    /// - the weapon's own model_animations (weap +56: only pistol "fire-1", shotgun "chamber-1" / "reload-2", sniper "fire-1" in the MP maps),
    ///   absolute local values of that model's nodes;
    /// - the first-person animations (weap +1132, "first-person fire-1", "reload-empty", "reload-full", "ready", "overheating", ...) that move
    ///   the first-person weapon model's nodes (weap +1116) - the same part names as the third-person model: applied as the change from the
    ///   first-person model's rest pose onto the third-person node's rest pose. The root node ("frame gun", the needler's "frame body") is never
    ///   moved (the hand / holster place the weapon). Kinds (<see cref="Kinds"/>) pick the animation by the game's names; any animation can also be
    ///   asked for by its own name. Measured on Blood Gulch (harness `wan`): pistol slide 4.7 cm back on fire, shotgun pump 9 cm, sniper bolt 16 cm.
    /// Unity-free; frames decode on first use.
    /// </summary>
    public sealed class HaloWeaponAnims
    {
        /// <summary>One playable animation on the third-person model's nodes.</summary>
        public sealed class Clip
        {
            public string Name;
            public HaloAnimation Anim;
            public bool FirstPerson;
            /// <summary>Per third-person model node: the animation's node (-1 = not moved by this clip).</summary>
            public int[] Map;
            public float Duration { get { return Anim != null ? Anim.Duration : 0f; } }
        }

        public readonly HaloTag Weapon;
        public readonly HaloModel Model;
        public readonly string[] NodeNames;
        /// <summary>The third-person model's rest pose per node: rotation (x, y, z, w, standard, Halo axes) and translation (Halo units).</summary>
        public readonly float[] RestRot, RestTrans;
        public readonly List<Clip> Clips = new List<Clip>();
        private readonly float[] _fpRestRot, _fpRestTrans;      // the first-person model's rest per THIRD-person node index (NaN = no such node)
        private readonly float[] _q = new float[4], _q2 = new float[4];

        /// <summary>
        /// The kinds Combat asks for, each with the game's animation names tried in order (third-person graph first, then first-person):
        /// fire, fire-2, chamber (shotgun pump / rack after a shot), reload-empty, reload-full, reload-exit (shotgun pump after loading shells),
        /// ready, put-away, overheating (vents opening), overheated, overheat-exit, misfire, charged, melee, posing.
        /// </summary>
        public static readonly string[][] Kinds =
        {
            new[] { "fire", "fire-1", "first-person fire-1", "first-person firing" },
            new[] { "fire-2", "fire-2", "first-person fire-2" },
            new[] { "chamber", "chamber-1", "first-person chamber-1" },
            new[] { "reload-empty", "reload-1", "first-person reload-empty", "first-person reload-full", "first-person reload-full2", "reload-2" },
            new[] { "reload-full", "reload-2", "first-person reload-full", "first-person reload-full2", "first-person reload-empty", "reload-1" },
            new[] { "reload-exit", "first-person exit-empty", "first-person exit-full", "chamber-1" },
            new[] { "ready", "first-person ready" },
            new[] { "put-away", "first-person put-away" },
            new[] { "overheating", "first-person overheating", "first-person overheating-again", "first-person overheated" },
            new[] { "overheated", "first-person overheated", "first-person overheating-again" },
            new[] { "overheat-exit", "first-person o-h-exit", "first-person o-h-s-enter" },
            new[] { "misfire", "first-person misfire-1", "first-person misfire-2" },
            new[] { "charged", "charged-1", "first-person overcharged" },
            new[] { "melee", "first-person melee" },
            new[] { "posing", "first-person posing" },
        };

        public HaloWeaponAnims(HaloTag weapon)
        {
            Weapon = weapon;
            TagReader r = weapon.Root;
            HaloTag model = r.Ref(40);
            if (model == null) { NodeNames = new string[0]; RestRot = new float[0]; RestTrans = new float[0]; _fpRestRot = new float[0]; _fpRestTrans = new float[0]; return; }
            Model = new HaloModel(model);
            int n = Model.Nodes.Count;
            NodeNames = new string[n];
            RestRot = new float[n * 4];
            RestTrans = new float[n * 3];
            _fpRestRot = new float[n * 4];
            _fpRestTrans = new float[n * 3];
            for (int i = 0; i < n; i++)
            {
                HaloModel.Node nd = Model.Nodes[i];
                NodeNames[i] = nd.Name;
                HQ.Std(nd.Qi, nd.Qj, nd.Qk, nd.Qw, RestRot, i * 4);
                RestTrans[i * 3] = nd.Translation.X; RestTrans[i * 3 + 1] = nd.Translation.Y; RestTrans[i * 3 + 2] = nd.Translation.Z;
                _fpRestRot[i * 4] = float.NaN;
            }

            bool isWeap = weapon.Is("weap");
            HaloTag tp = r.Ref(56);
            HaloTag fpModel = isWeap ? r.Ref(1116) : null, fp = isWeap ? r.Ref(1132) : null;
            if (fpModel != null)
            {
                var fm = new HaloModel(fpModel);
                for (int i = 0; i < n; i++)
                {
                    int k = fm.NodeIndex(NodeNames[i]);
                    if (k < 0) continue;
                    HaloModel.Node nd = fm.Nodes[k];
                    HQ.Std(nd.Qi, nd.Qj, nd.Qk, nd.Qw, _fpRestRot, i * 4);
                    _fpRestTrans[i * 3] = nd.Translation.X; _fpRestTrans[i * 3 + 1] = nd.Translation.Y; _fpRestTrans[i * 3 + 2] = nd.Translation.Z;
                }
            }

            if (tp != null) AddClips(new HaloAnimGraph(tp), false);
            if (fp != null) AddClips(new HaloAnimGraph(fp), true);
        }

        private void AddClips(HaloAnimGraph g, bool firstPerson)
        {
            int n = NodeNames.Length;
            for (int a = 0; a < g.Animations.Length; a++)
            {
                HaloAnimation an = g.Animations[a];
                if (an.Compressed || an.Type == HaloAnimation.Kind.Overlay) continue;
                var map = new int[n];
                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    map[i] = -1;
                    if (i == 0 || Model.Nodes[i].Parent < 0) continue;      // (the root stays where the hand / holster put the weapon)
                    if (firstPerson && float.IsNaN(_fpRestRot[i * 4])) continue;
                    int k = Array.IndexOf(g.NodeNames, NodeNames[i]);
                    if (k < 0 || k >= an.NodeCount || (!an.Rotates(k) && !an.Translates(k))) continue;
                    map[i] = k;
                    any = true;
                }

                if (any) Clips.Add(new Clip { Name = an.Name, Anim = an, FirstPerson = firstPerson, Map = map });
            }
        }

        public int Node(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            for (int i = 0; i < NodeNames.Length; i++)
                if (string.Equals(NodeNames[i], name, StringComparison.OrdinalIgnoreCase) || string.Equals(NodeNames[i], "frame " + name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The clip for a kind ("fire", "reload-empty", ... see <see cref="Kinds"/>) or an animation's own name; null when the weapon has none.</summary>
        public Clip Find(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return null;
            for (int k = 0; k < Kinds.Length; k++)
            {
                if (!string.Equals(Kinds[k][0], kind, StringComparison.OrdinalIgnoreCase)) continue;
                for (int j = 1; j < Kinds[k].Length; j++)
                {
                    Clip c = ByName(Kinds[k][j], false) ?? ByName(Kinds[k][j], true);
                    if (c != null) return c;
                }

                return null;
            }

            return ByName(kind, false) ?? ByName(kind, true);
        }

        private Clip ByName(string name, bool firstPerson)
        {
            for (int i = 0; i < Clips.Count; i++)
                if (Clips[i].FirstPerson == firstPerson && string.Equals(Clips[i].Name, name, StringComparison.OrdinalIgnoreCase)) return Clips[i];
            return null;
        }

        /// <summary>
        /// The clip's pose at a time for every third-person node it moves: local rotation (standard, Halo axes) and translation (Halo units) into
        /// rot / trans (n x 4, n x 3), moved[i] true. False when it cannot decode.
        /// </summary>
        public bool Sample(Clip c, float time, float[] rot, float[] trans, bool[] moved)
        {
            if (c == null || c.Anim == null || !c.Anim.Decode()) return false;
            HaloAnimation a = c.Anim;
            int f0, f1;
            float u;
            HaloPose.FrameAt(a, time, false, out f0, out f1, out u);
            int an = a.NodeCount;
            for (int i = 0; i < NodeNames.Length; i++)
            {
                int k = c.Map[i];
                moved[i] = k >= 0;
                if (k < 0) continue;
                HQ.Nlerp(a.Rot, (f0 * an + k) * 4, a.Rot, (f1 * an + k) * 4, u, _q, 0);
                float tx = Lerp(a.Trans[(f0 * an + k) * 3], a.Trans[(f1 * an + k) * 3], u);
                float ty = Lerp(a.Trans[(f0 * an + k) * 3 + 1], a.Trans[(f1 * an + k) * 3 + 1], u);
                float tz = Lerp(a.Trans[(f0 * an + k) * 3 + 2], a.Trans[(f1 * an + k) * 3 + 2], u);
                if (!c.FirstPerson)
                {
                    rot[i * 4] = _q[0]; rot[i * 4 + 1] = _q[1]; rot[i * 4 + 2] = _q[2]; rot[i * 4 + 3] = _q[3];
                    trans[i * 3] = tx; trans[i * 3 + 1] = ty; trans[i * 3 + 2] = tz;
                    continue;
                }

                // first person: the change from the first-person model's rest, onto the third-person rest
                _q2[0] = -_fpRestRot[i * 4]; _q2[1] = -_fpRestRot[i * 4 + 1]; _q2[2] = -_fpRestRot[i * 4 + 2]; _q2[3] = _fpRestRot[i * 4 + 3];
                HQ.Mul(_q2, 0, _q, 0, _q, 0);
                HQ.Mul(RestRot, i * 4, _q, 0, rot, i * 4);
                trans[i * 3] = RestTrans[i * 3] + tx - _fpRestTrans[i * 3];
                trans[i * 3 + 1] = RestTrans[i * 3 + 1] + ty - _fpRestTrans[i * 3 + 1];
                trans[i * 3 + 2] = RestTrans[i * 3 + 2] + tz - _fpRestTrans[i * 3 + 2];
            }

            return true;
        }

        private static float Lerp(float a, float b, float u) { return a + (b - a) * u; }

        /// <summary>How far (Halo units) a clip moves its parts at most from rest over its length (for the log / lab).</summary>
        public float Travel(Clip c)
        {
            if (c == null || !c.Anim.Decode()) return 0f;
            int n = NodeNames.Length;
            var rot = new float[n * 4];
            var tr = new float[n * 3];
            var mv = new bool[n];
            float best = 0f;
            for (int f = 0; f < c.Anim.Frames; f++)
            {
                Sample(c, f * HaloAnimation.FrameTime, rot, tr, mv);
                for (int i = 0; i < n; i++)
                {
                    if (!mv[i]) continue;
                    float dx = tr[i * 3] - RestTrans[i * 3], dy = tr[i * 3 + 1] - RestTrans[i * 3 + 1], dz = tr[i * 3 + 2] - RestTrans[i * 3 + 2];
                    float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d > best) best = d;
                }
            }

            return best;
        }
    }
}

using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Where the hands work a weapon when reloading, measured from the weapon's own models (ours: Halo reloads on a button; the places are
    /// read off the parts the game animates). Every point is in the THIRD-person model's space, Halo units (x forward along the barrel,
    /// y left, z up; origin = the model origin the hand grips).
    /// - the magazine well: the bottom of the "frame magazine" node's geometry (the pistol and the sniper rifle have it in the third-person
    ///   model; the assault rifle only in its first-person model, which shares the frame: its "frame gun" node sits at the same place), the
    ///   needles on top of the needler, the back of the rocket launcher's tubes, the shotgun's loading port under the receiver at its
    ///   first-person shell ("frame bullet");
    /// - the part pulled back: the pistol's "frame slide", the assault rifle's and the sniper rifle's "frame ophandle" (the operating
    ///   handle), the shotgun's "frame pump"; with how far it travels (ours: 1.8 cm of Halo slide, 3 cm of handle / pump);
    /// - the cup point of a one-handed weapon: its own "left hand cyborg" marker (where Halo's support hand goes), else under the grip.
    /// Unity-free: the harness prints them (`points bloodgulch`).
    /// </summary>
    public sealed class HaloWeaponPoints
    {
        public HVec3 Well;                 // where a magazine / shell / rocket pair goes in
        public HVec3 Seat;                 // where the magazine sits when seated (the magazine node's middle)
        public bool HasWell;
        public string WellFrom = "";       // what the well was measured from (for the log)
        public string PullNode;            // the node that moves when pulled (null = nothing to pull)
        public HVec3 Pull;                 // where the hand takes it
        public float PullTravel;           // world units it moves back (along -x)
        public HVec3 Cup;                  // a one-handed weapon's support point
        public string CupFrom = "";
        /// <summary>The magazine node lives only in the first-person model (the assault rifle): its geometry is there, at <see cref="FirstPersonOffset"/> from the third-person frame.</summary>
        public bool MagInFirstPerson;
        public HVec3 FirstPersonOffset;

        public sealed class NodeBox { public string Name; public int Count; public HVec3 Min, Max; public HVec3 Center => new HVec3((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2, (Min.Z + Max.Z) / 2); }

        /// <summary>The vertices weighted mostly to each node, as boxes (the model's best level of detail).</summary>
        public static Dictionary<string, NodeBox> Boxes(HaloModel model, out HVec3 frameGun)
        {
            var d = new Dictionary<string, NodeBox>(StringComparer.OrdinalIgnoreCase);
            frameGun = new HVec3();
            int nn = model.Nodes.Count;
            var boxes = new NodeBox[nn];
            for (int i = 0; i < nn; i++)
            {
                boxes[i] = new NodeBox { Name = model.Nodes[i].Name, Min = new HVec3(1e9f, 1e9f, 1e9f), Max = new HVec3(-1e9f, -1e9f, -1e9f) };
                if (model.Nodes[i].Name.EndsWith("frame gun", StringComparison.OrdinalIgnoreCase) || model.Nodes[i].Name.EndsWith("frame body", StringComparison.OrdinalIgnoreCase)) frameGun = model.Nodes[i].Translation;
            }

            foreach (var r in model.Regions)
            {
                if (r.Permutations.Count == 0) continue;
                int gi = -1;
                for (int l = 4; l >= 0 && gi < 0; l--) gi = r.Permutations[0].Lod[l];
                if (gi < 0 || gi >= model.Geometries.Count) continue;
                foreach (var part in model.Geometries[gi].Parts)
                {
                    var pd = model.Read(part);
                    for (int v = 0; v < pd.Position.Length; v++)
                    {
                        int n = pd.Node0[v];
                        if (n < 0 || n >= nn || pd.Weight0[v] < 0.5f) continue;
                        var b = boxes[n];
                        var p = pd.Position[v];
                        b.Count++;
                        b.Min = new HVec3(Math.Min(b.Min.X, p.X), Math.Min(b.Min.Y, p.Y), Math.Min(b.Min.Z, p.Z));
                        b.Max = new HVec3(Math.Max(b.Max.X, p.X), Math.Max(b.Max.Y, p.Y), Math.Max(b.Max.Z, p.Z));
                    }
                }
            }

            foreach (var b in boxes) if (b.Count > 0 && !d.ContainsKey(b.Name)) d[b.Name] = b;
            return d;
        }

        public static HaloWeaponPoints Measure(HaloWeaponDef def)
        {
            var p = new HaloWeaponPoints();
            if (def == null || def.Model == null) return p;
            var tp = new HaloModel(def.Model);
            HVec3 gun3;
            var b3 = Boxes(tp, out gun3);
            Dictionary<string, NodeBox> bf = null;
            HVec3 off = new HVec3();
            if (def.FirstPersonModel != null)
            {
                try
                {
                    HVec3 gunF;
                    bf = Boxes(new HaloModel(def.FirstPersonModel), out gunF);
                    off = new HVec3(gun3.X - gunF.X, gun3.Y - gunF.Y, gun3.Z - gunF.Z);       // the first-person frame onto the third-person one
                }
                catch (Exception) { bf = null; }
            }

            // the whole model's box
            HVec3 mn = new HVec3(1e9f, 1e9f, 1e9f), mx = new HVec3(-1e9f, -1e9f, -1e9f);
            foreach (var b in b3.Values)
            {
                mn = new HVec3(Math.Min(mn.X, b.Min.X), Math.Min(mn.Y, b.Min.Y), Math.Min(mn.Z, b.Min.Z));
                mx = new HVec3(Math.Max(mx.X, b.Max.X), Math.Max(mx.Y, b.Max.Y), Math.Max(mx.Z, b.Max.Z));
            }

            NodeBox mag = Find(b3, "frame magazine"), magF = mag == null ? Find(bf, "frame magazine") : null;
            switch (def.Kind)
            {
                case HaloWeaponDef.Family.Needler:
                {
                    // the needles sticking out of the top: their boxes in the first-person model
                    HVec3 lo = new HVec3(1e9f, 1e9f, 1e9f), hi = new HVec3(-1e9f, -1e9f, -1e9f); int n = 0;
                    if (bf != null) foreach (var b in bf.Values) if (b.Name.IndexOf("needle", StringComparison.OrdinalIgnoreCase) >= 0) { n++; lo = Min(lo, b.Min); hi = Max(hi, b.Max); }
                    if (n > 0) { p.Well = new HVec3((lo.X + hi.X) / 2 + off.X, off.Y, hi.Z + off.Z); p.WellFrom = n + " first-person needle nodes (top)"; }
                    else { p.Well = new HVec3((mn.X + mx.X) / 2, 0, mx.Z); p.WellFrom = "the top of the model"; }
                    p.Seat = p.Well; p.HasWell = true;
                    break;
                }
                case HaloWeaponDef.Family.Rocket:
                {
                    NodeBox tubes = Find(b3, "frame tubes");
                    if (tubes != null) { p.Well = new HVec3(tubes.Min.X, tubes.Center.Y, tubes.Center.Z); p.WellFrom = "the back of 'frame tubes'"; }
                    else { p.Well = new HVec3(mn.X, 0, (mn.Z + mx.Z) / 2); p.WellFrom = "the back of the model"; }
                    p.Seat = new HVec3(p.Well.X + 0.03f, p.Well.Y, p.Well.Z); p.HasWell = true;
                    break;
                }
                case HaloWeaponDef.Family.Shotgun:
                {
                    NodeBox shell = Find(bf, "frame bullet");
                    float x = shell != null ? shell.Center.X + off.X : (mn.X + mx.X) / 2;
                    p.Well = new HVec3(x, 0, MinZNear(b3, x, mn.Z)); p.WellFrom = shell != null ? "under the receiver at the first-person 'frame bullet'" : "under the receiver";
                    p.Seat = p.Well; p.HasWell = true;
                    break;
                }
                default:
                    if (mag != null) { p.Well = new HVec3(mag.Center.X, mag.Center.Y, mag.Min.Z); p.Seat = mag.Center; p.WellFrom = "the bottom of 'frame magazine'"; p.HasWell = true; }
                    else if (magF != null) { p.Well = new HVec3(magF.Center.X + off.X, magF.Center.Y + off.Y, magF.Min.Z + off.Z); p.Seat = Add(magF.Center, off); p.WellFrom = "the bottom of the first-person 'frame magazine'"; p.HasWell = true; p.MagInFirstPerson = true; }
                    else if (def.HasMagazine) { p.Well = new HVec3((mn.X + mx.X) / 2, 0, mn.Z); p.Seat = p.Well; p.WellFrom = "under the model (no magazine node)"; p.HasWell = true; }
                    break;
            }

            // the part pulled back
            NodeBox pull = null;
            switch (def.Kind)
            {
                case HaloWeaponDef.Family.Pistol: pull = Find(b3, "frame slide"); if (pull != null) { p.Pull = new HVec3(pull.Min.X + 0.008f, pull.Center.Y, pull.Max.Z - 0.004f); p.PullTravel = 0.018f; } break;
                case HaloWeaponDef.Family.Rifle: case HaloWeaponDef.Family.Sniper:
                    pull = Find(b3, "frame ophandle") ?? Find(bf, "frame ophandle");
                    if (pull != null) { p.Pull = pull == Find(b3, "frame ophandle") ? pull.Center : Add(pull.Center, off); p.PullTravel = 0.03f; }
                    break;
                case HaloWeaponDef.Family.Shotgun: pull = Find(b3, "frame pump"); if (pull != null) { p.Pull = pull.Center; p.PullTravel = 0.03f; } break;
            }

            if (pull != null) p.PullNode = pull.Name;
            p.FirstPersonOffset = off;
            return p;
        }

        /// <summary>The lowest point of the model near <paramref name="x"/> (the receiver's underside there).</summary>
        private static float MinZNear(Dictionary<string, NodeBox> boxes, float x, float fallback)
        {
            NodeBox g = Find(boxes, "frame gun");
            return g != null ? Math.Max(g.Min.Z, fallback) + 0.012f : fallback;
        }

        private static NodeBox Find(Dictionary<string, NodeBox> d, string name)
        {
            if (d == null) return null;
            NodeBox b;
            return d.TryGetValue(name, out b) ? b : null;
        }

        private static HVec3 Add(HVec3 a, HVec3 b) { return new HVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        private static HVec3 Min(HVec3 a, HVec3 b) { return new HVec3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)); }
        private static HVec3 Max(HVec3 a, HVec3 b) { return new HVec3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)); }

        public string Describe()
        {
            return "well " + (HasWell ? Well + " (" + WellFrom + ")" : "none") + ", pull " + (PullNode != null ? "'" + PullNode + "' at " + Pull + " travel " + PullTravel.ToString("0.###") : "none");
        }
    }
}

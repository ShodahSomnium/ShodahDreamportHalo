using System;
using System.Linq;
using System.Collections.Generic;
using SomniumSpace.Worlds.SomniumHalo;
static partial class W
{
    // quaternion helpers (x,y,z,w) right-handed Halo space
    static double[] QMul(double[] a, double[] b) => new[] {
        a[3]*b[0] + a[0]*b[3] + a[1]*b[2] - a[2]*b[1],
        a[3]*b[1] - a[0]*b[2] + a[1]*b[3] + a[2]*b[0],
        a[3]*b[2] + a[0]*b[1] - a[1]*b[0] + a[2]*b[3],
        a[3]*b[3] - a[0]*b[0] - a[1]*b[1] - a[2]*b[2] };
    static double[] QRot(double[] q, double[] v)
    {
        var p = new[] { v[0], v[1], v[2], 0 };
        var qc = new[] { -q[0], -q[1], -q[2], q[3] };
        var r = QMul(QMul(q, p), qc);
        return new[] { r[0], r[1], r[2] };
    }
    static int Nodes(string[] a)
    {
        var m = new HaloMap(Maps + a[1] + ".map");
        foreach (var t in m.OfClass("mod2").Where(t => a.Length < 3 || t.Name.Contains(a[2])))
        {
            var mo = new HaloModel(t);
            if (mo.Nodes.Count < 2 || mo.Markers.Count == 0) continue;
            // vertices by dominant node (lod: geometry 0)
            var byNode = new Dictionary<int, List<HVec3>>();
            foreach (var g in mo.Geometries.Take(1)) foreach (var part in g.Parts)
            {
                var d = mo.Read(part);
                for (int i = 0; i < d.Position.Length; i++)
                {
                    int n = d.Weight0[i] >= 0.5f ? d.Node0[i] : d.Node1[i];
                    if (!byNode.TryGetValue(n, out var l)) byNode[n] = l = new List<HVec3>();
                    l.Add(d.Position[i]);
                }
            }
            foreach (int conj in new[] { 0, 1 })
            {
                // node world transforms
                var wq = new double[mo.Nodes.Count][]; var wp = new double[mo.Nodes.Count][];
                for (int i = 0; i < mo.Nodes.Count; i++)
                {
                    var n = mo.Nodes[i];
                    var q = conj == 1 ? new double[] { -n.Qi, -n.Qj, -n.Qk, n.Qw } : new double[] { n.Qi, n.Qj, n.Qk, n.Qw };
                    var tr = new double[] { n.Translation.X, n.Translation.Y, n.Translation.Z };
                    if (n.Parent < 0) { wq[i] = q; wp[i] = tr; }
                    else { var pq = wq[n.Parent]; var pp = wp[n.Parent]; var r = QRot(pq, tr); wp[i] = new[] { pp[0] + r[0], pp[1] + r[1], pp[2] + r[2] }; wq[i] = QMul(pq, q); }
                }
                double sum = 0; int cnt = 0;
                foreach (var mk in mo.Markers) foreach (var inst in mk.Instances)
                {
                    if (inst.Node >= mo.Nodes.Count || !byNode.ContainsKey(inst.Node)) continue;
                    var r = QRot(wq[inst.Node], new double[] { inst.Translation.X, inst.Translation.Y, inst.Translation.Z });
                    var p = new[] { wp[inst.Node][0] + r[0], wp[inst.Node][1] + r[1], wp[inst.Node][2] + r[2] };
                    double best = double.MaxValue;
                    foreach (var v in byNode[inst.Node]) { double dx = v.X - p[0], dy = v.Y - p[1], dz = v.Z - p[2]; best = Math.Min(best, dx*dx + dy*dy + dz*dz); }
                    sum += Math.Sqrt(best); cnt++;
                }
                Console.WriteLine($"{t.Name} {(conj == 1 ? "conjugated" : "as stored ")}: markers {cnt} mean distance to own node's vertices {sum / Math.Max(1, cnt):0.0000} units");
            }
        }
        return 0;
    }
}

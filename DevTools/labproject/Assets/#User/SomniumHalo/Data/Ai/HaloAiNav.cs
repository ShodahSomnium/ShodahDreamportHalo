using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The AI's navigation for one structure BSP, from the BSP's own data the way Halo pathfinds: Halo's AI walks on the collision BSP's
    /// surfaces. The sbsp "pathfinding surfaces" block holds one byte per collision surface (retail maps: 0x00 or 0x40, a30 has 3 x 0xC0);
    /// bit 6 (0x40) marks the surfaces the AI may stand on (a10a: 2568 of 20960, a30_a: 7460 of 19138). The "pathfinding edges" block is
    /// empty in the retail cache files, so connectivity comes from the collision edges (an edge's left / right surface) plus "step links"
    /// across short risers (a non-walkable surface no taller than <see cref="MaxStep"/> joins the walkable surfaces around it: stairs,
    /// kerbs). When a BSP has no pathfinding bytes, surfaces facing up (normal z &gt; 0.7, not invisible) are used instead.
    /// Queries: <see cref="Locate"/> (the surface under a point), <see cref="FindPath"/> (A* over surfaces through the shared edges,
    /// then a funnel pass that pulls the path tight, kept <see cref="FindPath"/>'s radius away from the portal ends).
    /// Halo space and world units. Unity-free; not thread safe (one query at a time, main thread).
    /// </summary>
    public sealed class HaloAiNav
    {
        public const byte WalkableBit = 0x40;
        /// <summary>Tallest riser joined by a step link (world units; 0.12 wu = 0.37 m). Ours.</summary>
        public static float MaxStep = 0.12f;
        /// <summary>Steep (non-walkable) surfaces still crossed by a link when their normal z is at least this and they rise at most
        /// <see cref="MaxSlopeRise"/> (terrain bumps between walkable triangles): 0.5 = cos 60, the steepest any biped stands on (the
        /// Covenant's maximum slope); the rise is a step height (0.2 wu). Ours. Each link keeps its surface's normal z, and
        /// <see cref="FindPath(HVec3, HVec3, float, List{HVec3}, int, float)"/> skips links steeper than the walker's own maximum slope.</summary>
        public static float SlopeNz = 0.5f, MaxSlopeRise = 0.2f;
        /// <summary>cos 45: the human bipeds' maximum slope (marines, the default for paths that do not say).</summary>
        public const float HumanSlopeNz = 0.707f;

        public readonly int BspIndex;
        public readonly bool FromPathfindingData;
        public int PolyCount { get; private set; }
        public int LinkCount { get; private set; }
        public int StepLinks { get; private set; }
        /// <summary>Of the step links, the ones across slopes (normal z at least <see cref="SlopeNz"/>, rise at most <see cref="MaxSlopeRise"/>).</summary>
        public int SlopeLinks { get; private set; }
        public int Components { get; private set; }

        float[] _v;                    // collision vertices xyz
        int[] _ringStart, _ringLen, _ring;
        float[] _pnx, _pny, _pnz, _pd; // polygon planes (facing)
        float[] _cx, _cy, _cz;         // centroids
        float[] _minX, _minY, _maxX, _maxY, _minZ, _maxZ;
        int[] _comp;
        // adjacency (CSR): for poly p, links _adjStart[p] .. _adjStart[p + 1] - 1
        int[] _adjStart, _adjPoly;
        float[] _pax, _pay, _paz, _pbx, _pby, _pbz;   // portal segment per link (a = left end seen from p, b = right end)
        bool[] _adjStep;
        float[] _adjNz;               // the normal z of the surface a slope link crosses (1 = a shared edge / a step)
        // grid over XY
        float _gx0, _gy0, _cell; int _gw, _gh; int[] _cellStart, _cellPolys;

        /// <summary>Builds the navigation of a structure BSP: <paramref name="collisionBsp"/> = the sbsp's collision bsp element (+176 block,
        /// element 0), <paramref name="pathfindingBytes"/> = the sbsp's pathfinding surfaces bytes (null / wrong length = derive from the normals).</summary>
        public HaloAiNav(int bspIndex, TagReader cb, byte[] pathfindingBytes)
        {
            BspIndex = bspIndex;
            HaloMap map = cb.Map;
            int nPlanes = Math.Max(0, cb.Count(12));
            var planes = new float[nPlanes * 4];
            int i = 0;
            foreach (var p in cb.Block(12, 16)) { for (int k = 0; k < 4; k++) planes[i * 4 + k] = p.F32(4 * k); i++; }
            int nVerts = Math.Max(0, cb.Count(84));
            _v = new float[nVerts * 3];
            i = 0;
            foreach (var v in cb.Block(84, 16)) { _v[i * 3] = v.F32(0); _v[i * 3 + 1] = v.F32(4); _v[i * 3 + 2] = v.F32(8); i++; }
            int nEdges = Math.Max(0, cb.Count(72));
            int nSurf = Math.Max(0, cb.Count(60));
            var eStart = new int[nEdges]; var eEnd = new int[nEdges]; var eFwd = new int[nEdges]; var eRev = new int[nEdges]; var eLeft = new int[nEdges]; var eRight = new int[nEdges];
            if (nEdges > 0)
            {
                int edgeAt = cb.Off(cb.U32(76));
                for (int e = 0; e < nEdges; e++)
                {
                    int o = edgeAt + e * 24;
                    eStart[e] = map.I32(o); eEnd[e] = map.I32(o + 4); eFwd[e] = map.I32(o + 8); eRev[e] = map.I32(o + 12); eLeft[e] = map.I32(o + 16); eRight[e] = map.I32(o + 20);
                }
            }

            // surfaces: plane, first edge, flags
            var sPlane = new int[nSurf]; var sFlip = new bool[nSurf]; var sFirst = new int[nSurf]; var sFlags = new int[nSurf];
            i = 0;
            foreach (var s in cb.Block(60, 12)) { uint pl = s.U32(0); sPlane[i] = (int)(pl & 0x7FFFFFFF); sFlip[i] = (pl & 0x80000000) != 0; sFirst[i] = s.I32(4); sFlags[i] = s.U8(8); i++; }
            FromPathfindingData = pathfindingBytes != null && pathfindingBytes.Length == nSurf && nSurf > 0;

            // walkable surfaces -> polygons
            var polyOf = new int[nSurf];
            var ringTmp = new List<int>(16);
            var ringAll = new List<int>(nSurf * 4);
            var starts = new List<int>(); var lens = new List<int>(); var surfOfPoly = new List<int>();
            var surfZMin = new float[nSurf]; var surfZMax = new float[nSurf];
            var surfRingStart = new int[nSurf]; var surfRingLen = new int[nSurf];
            var ringSurf = new List<int>(nSurf * 4);   // rings of every surface (for the riser heights)
            for (int s = 0; s < nSurf; s++)
            {
                polyOf[s] = -1;
                ringTmp.Clear();
                int first = sFirst[s], e = first, guard = 0; bool bad = false;
                do
                {
                    if (e < 0 || e >= nEdges) { bad = true; break; }
                    if (eLeft[e] == s) { ringTmp.Add(eStart[e]); e = eFwd[e]; } else { ringTmp.Add(eEnd[e]); e = eRev[e]; }
                    if (++guard > 256) { bad = true; break; }
                } while (e != first);
                if (bad || ringTmp.Count < 3) { surfRingLen[s] = 0; surfZMin[s] = 1e9f; surfZMax[s] = -1e9f; continue; }
                float zMin = 1e9f, zMax = -1e9f;
                surfRingStart[s] = ringSurf.Count; surfRingLen[s] = ringTmp.Count;
                foreach (int vi in ringTmp) { if (vi < 0 || vi >= nVerts) { bad = true; break; } float z = _v[vi * 3 + 2]; if (z < zMin) zMin = z; if (z > zMax) zMax = z; ringSurf.Add(vi); }
                if (bad) { surfRingLen[s] = 0; continue; }
                surfZMin[s] = zMin; surfZMax[s] = zMax;
                int pi = sPlane[s] * 4; if (pi + 3 >= planes.Length) continue;
                float f = sFlip[s] ? -1f : 1f;
                float nz = planes[pi + 2] * f;
                bool walk = FromPathfindingData ? (pathfindingBytes[s] & WalkableBit) != 0 && nz >= 0.7f : nz > 0.7f && (sFlags[s] & HaloCollisionBsp.Invisible) == 0;
                if (!walk) continue;
                polyOf[s] = starts.Count;
                starts.Add(ringAll.Count); lens.Add(ringTmp.Count); surfOfPoly.Add(s);
                ringAll.AddRange(ringTmp);
            }

            int n = starts.Count;
            PolyCount = n;
            _ringStart = starts.ToArray(); _ringLen = lens.ToArray(); _ring = ringAll.ToArray();
            _pnx = new float[n]; _pny = new float[n]; _pnz = new float[n]; _pd = new float[n];
            _cx = new float[n]; _cy = new float[n]; _cz = new float[n];
            _minX = new float[n]; _minY = new float[n]; _maxX = new float[n]; _maxY = new float[n]; _minZ = new float[n]; _maxZ = new float[n];
            for (int p = 0; p < n; p++)
            {
                int s = surfOfPoly[p];
                int pi = sPlane[s] * 4; float f = sFlip[s] ? -1f : 1f;
                _pnx[p] = planes[pi] * f; _pny[p] = planes[pi + 1] * f; _pnz[p] = planes[pi + 2] * f; _pd[p] = planes[pi + 3] * f;
                float sx = 0, sy = 0, sz = 0, x0 = 1e9f, y0 = 1e9f, z0 = 1e9f, x1 = -1e9f, y1 = -1e9f, z1 = -1e9f;
                for (int k = 0; k < _ringLen[p]; k++)
                {
                    int vi = _ring[_ringStart[p] + k] * 3;
                    float x = _v[vi], y = _v[vi + 1], z = _v[vi + 2];
                    sx += x; sy += y; sz += z;
                    if (x < x0) x0 = x; if (y < y0) y0 = y; if (z < z0) z0 = z; if (x > x1) x1 = x; if (y > y1) y1 = y; if (z > z1) z1 = z;
                }
                float inv = 1f / _ringLen[p];
                _cx[p] = sx * inv; _cy[p] = sy * inv; _cz[p] = sz * inv;
                _minX[p] = x0; _minY[p] = y0; _minZ[p] = z0; _maxX[p] = x1; _maxY[p] = y1; _maxZ[p] = z1;
            }

            // links: shared collision edges between two walkable surfaces
            var lists = new List<int>[n];
            var portals = new List<float[]>[n];
            var steps = new List<bool>[n];
            var nzs = new List<float>[n];
            for (int p = 0; p < n; p++) { lists[p] = new List<int>(4); portals[p] = new List<float[]>(4); steps[p] = new List<bool>(4); nzs[p] = new List<float>(4); }
            int links = 0;
            for (int e = 0; e < nEdges; e++)
            {
                int l = eLeft[e], r = eRight[e];
                if (l < 0 || r < 0 || l >= nSurf || r >= nSurf || l == r) continue;
                int pl = polyOf[l], pr = polyOf[r];
                if (pl < 0 || pr < 0) continue;
                // the edge runs start -> end counter-clockwise for the left surface: seen from the left polygon, its "left end" is the end vertex
                float[] a = V3(eEnd[e]), b = V3(eStart[e]);
                AddLink(lists, portals, steps, nzs, pl, pr, a, b, false, 1f);
                AddLink(lists, portals, steps, nzs, pr, pl, b, a, false, 1f);
                links++;
            }

            // step links across short risers
            int stepLinks = 0;
            var around = new List<int>(8); var aroundEdge = new List<int>(8);
            for (int s = 0; s < nSurf; s++)
            {
                if (polyOf[s] >= 0 || surfRingLen[s] == 0) continue;
                float span = surfZMax[s] - surfZMin[s];
                int spi = sPlane[s] * 4; float snz = spi + 3 < planes.Length ? planes[spi + 2] * (sFlip[s] ? -1f : 1f) : 0f;
                bool slope = span > MaxStep && snz >= SlopeNz && span <= MaxSlopeRise;
                if (span > MaxStep && !slope) continue;
                around.Clear(); aroundEdge.Clear();
                int first = sFirst[s], e = first, guard = 0;
                do
                {
                    if (e < 0 || e >= nEdges) break;
                    int other = eLeft[e] == s ? eRight[e] : eLeft[e];
                    if (other >= 0 && other < nSurf && polyOf[other] >= 0 && !around.Contains(polyOf[other])) { around.Add(polyOf[other]); aroundEdge.Add(e); }
                    e = eLeft[e] == s ? eFwd[e] : eRev[e];
                } while (e != first && ++guard < 256);
                for (int x = 0; x < around.Count; x++)
                    for (int y = x + 1; y < around.Count; y++)
                    {
                        int pa = around[x], pb = around[y];
                        if (Linked(lists, pa, pb)) continue;
                        // only when the two surfaces meet the riser at its top and bottom (not two floors beside one low wall)
                        float za = EdgeZ(aroundEdge[x], eStart, eEnd), zb = EdgeZ(aroundEdge[y], eStart, eEnd);
                        if (!slope && Math.Abs(za - zb) < 0.01f) continue;
                        float[] ma = Mid(aroundEdge[x], eStart, eEnd), mb = Mid(aroundEdge[y], eStart, eEnd);
                        float dx = ma[0] - mb[0], dy = ma[1] - mb[1];
                        if (dx * dx + dy * dy > (slope ? 2.5f : 0.3f) * (slope ? 2.5f : 0.3f)) continue;
                        float[] pa0 = V3(eStart[aroundEdge[x]]), pa1 = V3(eEnd[aroundEdge[x]]);
                        AddLink(lists, portals, steps, nzs, pa, pb, pa0, pa1, true, slope ? snz : 1f);
                        AddLink(lists, portals, steps, nzs, pb, pa, pa1, pa0, true, slope ? snz : 1f);
                        if (slope) SlopeLinks++;
                        stepLinks++;
                    }
            }

            LinkCount = links; StepLinks = stepLinks;
            _adjStart = new int[n + 1];
            int total = 0;
            for (int p = 0; p < n; p++) { _adjStart[p] = total; total += lists[p].Count; }
            _adjStart[n] = total;
            _adjPoly = new int[total]; _adjStep = new bool[total]; _adjNz = new float[total];
            _pax = new float[total]; _pay = new float[total]; _paz = new float[total]; _pbx = new float[total]; _pby = new float[total]; _pbz = new float[total];
            for (int p = 0; p < n; p++)
                for (int k = 0; k < lists[p].Count; k++)
                {
                    int j = _adjStart[p] + k;
                    _adjPoly[j] = lists[p][k]; _adjStep[j] = steps[p][k]; _adjNz[j] = nzs[p][k];
                    float[] seg = portals[p][k];
                    _pax[j] = seg[0]; _pay[j] = seg[1]; _paz[j] = seg[2]; _pbx[j] = seg[3]; _pby[j] = seg[4]; _pbz[j] = seg[5];
                }

            // connected components
            _comp = new int[n];
            for (int p = 0; p < n; p++) _comp[p] = -1;
            int comps = 0; var stack = new Stack<int>();
            for (int p = 0; p < n; p++)
            {
                if (_comp[p] >= 0) continue;
                _comp[p] = comps; stack.Push(p);
                while (stack.Count > 0)
                {
                    int q = stack.Pop();
                    for (int j = _adjStart[q]; j < _adjStart[q + 1]; j++) { int o = _adjPoly[j]; if (_comp[o] < 0) { _comp[o] = comps; stack.Push(o); } }
                }
                comps++;
            }
            Components = comps;
            BuildGrid();
            _g = new float[n]; _f = new float[n]; _from = new int[n]; _fromLink = new int[n]; _stamp = new int[n]; _closed = new int[n];
            _heap = new int[Math.Max(16, n)];
        }

        float[] V3(int vi) { int o = vi * 3; return new[] { _v[o], _v[o + 1], _v[o + 2] }; }
        float EdgeZ(int e, int[] s, int[] en) { return (_v[s[e] * 3 + 2] + _v[en[e] * 3 + 2]) * 0.5f; }
        float[] Mid(int e, int[] s, int[] en) { int a = s[e] * 3, b = en[e] * 3; return new[] { (_v[a] + _v[b]) * 0.5f, (_v[a + 1] + _v[b + 1]) * 0.5f, (_v[a + 2] + _v[b + 2]) * 0.5f }; }

        static bool Linked(List<int>[] lists, int a, int b) { return lists[a].Contains(b); }

        static void AddLink(List<int>[] lists, List<float[]>[] portals, List<bool>[] steps, List<float>[] nzs, int from, int to, float[] a, float[] b, bool step, float nz)
        {
            int k = lists[from].IndexOf(to);
            if (k >= 0)
            {
                // two polygons sharing several edges (a fan): keep the longest portal
                float[] old = portals[from][k];
                float lo = (old[0] - old[3]) * (old[0] - old[3]) + (old[1] - old[4]) * (old[1] - old[4]);
                float ln = (a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1]);
                if (ln > lo) portals[from][k] = new[] { a[0], a[1], a[2], b[0], b[1], b[2] };
                if (nz > nzs[from][k]) nzs[from][k] = nz;     // the gentler crossing wins
                return;
            }
            lists[from].Add(to); portals[from].Add(new[] { a[0], a[1], a[2], b[0], b[1], b[2] }); steps[from].Add(step); nzs[from].Add(nz);
        }

        void BuildGrid()
        {
            int n = PolyCount;
            if (n == 0) { _gw = _gh = 0; _cellStart = new int[1]; _cellPolys = new int[0]; return; }
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            for (int p = 0; p < n; p++) { if (_minX[p] < x0) x0 = _minX[p]; if (_minY[p] < y0) y0 = _minY[p]; if (_maxX[p] > x1) x1 = _maxX[p]; if (_maxY[p] > y1) y1 = _maxY[p]; }
            _cell = 2f;   // world units (6 m)
            _gx0 = x0; _gy0 = y0;
            _gw = Math.Max(1, (int)Math.Ceiling((x1 - x0) / _cell) + 1);
            _gh = Math.Max(1, (int)Math.Ceiling((y1 - y0) / _cell) + 1);
            while ((long)_gw * _gh > 400000) { _cell *= 1.5f; _gw = Math.Max(1, (int)Math.Ceiling((x1 - x0) / _cell) + 1); _gh = Math.Max(1, (int)Math.Ceiling((y1 - y0) / _cell) + 1); }
            var counts = new int[_gw * _gh + 1];
            for (int p = 0; p < n; p++) ForCells(p, (c) => counts[c]++);
            _cellStart = new int[_gw * _gh + 1];
            int t = 0;
            for (int c = 0; c < _gw * _gh; c++) { _cellStart[c] = t; t += counts[c]; }
            _cellStart[_gw * _gh] = t;
            _cellPolys = new int[t];
            var fill = new int[_gw * _gh];
            for (int p = 0; p < n; p++) { int pp = p; ForCells(p, (c) => { _cellPolys[_cellStart[c] + fill[c]++] = pp; }); }
        }

        void ForCells(int p, Action<int> f)
        {
            int ax = Clamp((int)((_minX[p] - _gx0) / _cell), 0, _gw - 1), bx = Clamp((int)((_maxX[p] - _gx0) / _cell), 0, _gw - 1);
            int ay = Clamp((int)((_minY[p] - _gy0) / _cell), 0, _gh - 1), by = Clamp((int)((_maxY[p] - _gy0) / _cell), 0, _gh - 1);
            for (int y = ay; y <= by; y++) for (int x = ax; x <= bx; x++) f(y * _gw + x);
        }

        static int Clamp(int v, int a, int b) { return v < a ? a : v > b ? b : v; }

        // ------------------------------------------------------------------ queries

        public HVec3 Centroid(int p) => new HVec3(_cx[p], _cy[p], _cz[p]);
        public int Component(int p) => p >= 0 && p < PolyCount ? _comp[p] : -1;
        public float Area(int p) { float a = 0; int s = _ringStart[p], l = _ringLen[p]; for (int k = 0; k < l; k++) { int i = _ring[s + k] * 3, j = _ring[s + (k + 1) % l] * 3; a += _v[i] * _v[j + 1] - _v[j] * _v[i + 1]; } return Math.Abs(a) * 0.5f; }

        /// <summary>Height of polygon p's plane at (x, y).</summary>
        public float HeightAt(int p, float x, float y) { return _pnz[p] > 1e-4f ? (_pd[p] - _pnx[p] * x - _pny[p] * y) / _pnz[p] : _cz[p]; }

        bool InsideXY(int p, float x, float y)
        {
            int s = _ringStart[p], l = _ringLen[p];
            bool inside = false;
            for (int k = 0, j = l - 1; k < l; j = k++)
            {
                int a = _ring[s + k] * 3, b = _ring[s + j] * 3;
                float ax = _v[a], ay = _v[a + 1], bx = _v[b], by = _v[b + 1];
                if ((ay > y) != (by > y) && x < (bx - ax) * (y - ay) / (by - ay) + ax) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// The walkable polygon under a point: the highest one whose plane is at most <paramref name="up"/> above and <paramref name="down"/>
        /// below the point; when none contains it, the nearest polygon centroid within <paramref name="snap"/> (world units). -1 = none.
        /// </summary>
        public int Locate(float x, float y, float z, float up = 0.15f, float down = 1.5f, float snap = 1.0f)
        {
            if (PolyCount == 0 || _gw == 0) return -1;
            int cx = (int)((x - _gx0) / _cell), cy = (int)((y - _gy0) / _cell);
            int best = -1; float bestZ = -1e9f;
            if (cx >= 0 && cy >= 0 && cx < _gw && cy < _gh)
            {
                int c = cy * _gw + cx;
                for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++)
                {
                    int p = _cellPolys[k];
                    if (x < _minX[p] || x > _maxX[p] || y < _minY[p] || y > _maxY[p]) continue;
                    if (!InsideXY(p, x, y)) continue;
                    float h = HeightAt(p, x, y);
                    if (h > z + up || h < z - down) continue;
                    if (h > bestZ) { bestZ = h; best = p; }
                }
            }
            if (best >= 0 || snap <= 0f) return best;
            // nearest by edge distance in the neighbouring cells
            float bd = snap * snap; int r = (int)Math.Ceiling(snap / _cell);
            for (int gy = cy - r; gy <= cy + r; gy++)
                for (int gx = cx - r; gx <= cx + r; gx++)
                {
                    if (gx < 0 || gy < 0 || gx >= _gw || gy >= _gh) continue;
                    int c = gy * _gw + gx;
                    for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++)
                    {
                        int p = _cellPolys[k];
                        float h = _cz[p];
                        if (h > z + up + 0.3f || h < z - down) continue;
                        float d = DistXY2(p, x, y);
                        if (d < bd) { bd = d; best = p; }
                    }
                }
            return best;
        }

        float DistXY2(int p, float x, float y)
        {
            if (InsideXY(p, x, y)) return 0f;
            float best = 1e18f;
            int s = _ringStart[p], l = _ringLen[p];
            for (int k = 0; k < l; k++)
            {
                int a = _ring[s + k] * 3, b = _ring[s + (k + 1) % l] * 3;
                float d = SegDist2(x, y, _v[a], _v[a + 1], _v[b], _v[b + 1]);
                if (d < best) best = d;
            }
            return best;
        }

        static float SegDist2(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            float t = l2 > 1e-9f ? ((px - ax) * dx + (py - ay) * dy) / l2 : 0f;
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            float qx = ax + t * dx - px, qy = ay + t * dy - py;
            return qx * qx + qy * qy;
        }

        /// <summary>The closest point inside polygon p to (x, y) (on its plane).</summary>
        public HVec3 ClosestPoint(int p, float x, float y)
        {
            if (InsideXY(p, x, y)) return new HVec3(x, y, HeightAt(p, x, y));
            float best = 1e18f, bx = _cx[p], by = _cy[p];
            int s = _ringStart[p], l = _ringLen[p];
            for (int k = 0; k < l; k++)
            {
                int a = _ring[s + k] * 3, b = _ring[s + (k + 1) % l] * 3;
                float ax = _v[a], ay = _v[a + 1], ex = _v[b] - ax, ey = _v[b + 1] - ay, l2 = ex * ex + ey * ey;
                float t = l2 > 1e-9f ? ((x - ax) * ex + (y - ay) * ey) / l2 : 0f; t = t < 0 ? 0 : t > 1 ? 1 : t;
                float qx = ax + t * ex, qy = ay + t * ey, d = (qx - x) * (qx - x) + (qy - y) * (qy - y);
                if (d < best) { best = d; bx = qx; by = qy; }
            }
            // a little inside from the edge
            bx += (_cx[p] - bx) * 0.05f; by += (_cy[p] - by) * 0.05f;
            return new HVec3(bx, by, HeightAt(p, bx, by));
        }

        // A* scratch
        float[] _g, _f; int[] _from, _fromLink, _stamp, _closed; int _gen; int[] _heap; int _heapN;
        readonly List<int> _corridor = new List<int>(64), _corridorLinks = new List<int>(64);

        /// <summary>Polygons expanded by the last query (for the budget log).</summary>
        public int LastExpanded { get; private set; }

        /// <summary>
        /// A path from <paramref name="from"/> to <paramref name="to"/> (Halo space) as waypoints (excluding the start, including the goal).
        /// <paramref name="radius"/> keeps the path that far from the portal ends (the actor's pathfinding radius). Returns false when either
        /// end is off the navigation, they are not connected, or the search exceeded <paramref name="maxExpand"/> polygons (out = the straight goal).
        /// </summary>
        public bool FindPath(HVec3 from, HVec3 to, float radius, List<HVec3> outPoints, int maxExpand = 3000) { return FindPath(from, to, radius, outPoints, maxExpand, HumanSlopeNz); }

        /// <summary>As <see cref="FindPath(HVec3, HVec3, float, List{HVec3}, int)"/> for a walker whose steepest slope has normal z
        /// <paramref name="minSlopeNz"/> (the biped's cosine of its maximum slope: 0.707 humans, 0.5 Covenant): slope links across steeper
        /// surfaces are closed to it.</summary>
        public bool FindPath(HVec3 from, HVec3 to, float radius, List<HVec3> outPoints, int maxExpand, float minSlopeNz)
        {
            outPoints.Clear();
            LastExpanded = 0;
            int a = Locate(from.X, from.Y, from.Z), b = Locate(to.X, to.Y, to.Z, 0.3f, 2.5f, 1.5f);
            if (a < 0 || b < 0) { outPoints.Add(to); return false; }
            if (a == b) { outPoints.Add(to); return true; }
            if (_comp[a] != _comp[b]) { outPoints.Add(to); return false; }
            _gen++;
            if (_gen == int.MaxValue) { _gen = 1; Array.Clear(_stamp, 0, _stamp.Length); Array.Clear(_closed, 0, _closed.Length); }
            _heapN = 0;
            Open(a, 0f, H(a, to), -1, -1);
            int found = -1;
            while (_heapN > 0)
            {
                int p = Pop();
                if (_closed[p] == _gen) continue;
                _closed[p] = _gen;
                if (p == b) { found = p; break; }
                if (++LastExpanded > maxExpand) break;
                // the point we entered p through (portal midpoint) for the cost
                float px, py, pz;
                if (_fromLink[p] >= 0) { int j = _fromLink[p]; px = (_pax[j] + _pbx[j]) * 0.5f; py = (_pay[j] + _pby[j]) * 0.5f; pz = (_paz[j] + _pbz[j]) * 0.5f; }
                else { px = from.X; py = from.Y; pz = from.Z; }
                for (int j = _adjStart[p]; j < _adjStart[p + 1]; j++)
                {
                    int q = _adjPoly[j];
                    if (_closed[q] == _gen) continue;
                    if (_adjNz[j] < minSlopeNz) continue;          // a slope steeper than this walker stands on
                    // a portal narrower than the actor is closed to it
                    float wx = _pax[j] - _pbx[j], wy = _pay[j] - _pby[j];
                    if (!_adjStep[j] && wx * wx + wy * wy < radius * radius * 1.0f) continue;
                    float mx = (_pax[j] + _pbx[j]) * 0.5f, my = (_pay[j] + _pby[j]) * 0.5f, mz = (_paz[j] + _pbz[j]) * 0.5f;
                    float g = _g[p] + Dist(px, py, pz, mx, my, mz);
                    if (_stamp[q] == _gen && g >= _g[q]) continue;
                    Open(q, g, g + H(q, to), p, j);
                }
            }
            if (found < 0) { outPoints.Add(to); return false; }

            // corridor
            _corridor.Clear(); _corridorLinks.Clear();
            for (int p = found; p >= 0 && _corridor.Count < 4096; p = _from[p]) { _corridor.Add(p); _corridorLinks.Add(_fromLink[p]); }
            _corridor.Reverse(); _corridorLinks.Reverse();
            Funnel(from, to, radius, outPoints);
            return true;
        }

        float H(int p, HVec3 to) { return Dist(_cx[p], _cy[p], _cz[p], to.X, to.Y, to.Z) * 0.9f; }
        static float Dist(float ax, float ay, float az, float bx, float by, float bz) { float dx = ax - bx, dy = ay - by, dz = az - bz; return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz); }

        void Open(int p, float g, float f, int from, int link)
        {
            _stamp[p] = _gen; _g[p] = g; _f[p] = f; _from[p] = from; _fromLink[p] = link;
            if (_heapN >= _heap.Length) Array.Resize(ref _heap, _heap.Length * 2);
            int i = _heapN++;
            _heap[i] = p;
            while (i > 0) { int par = (i - 1) >> 1; if (_f[_heap[par]] <= _f[_heap[i]]) break; int t = _heap[par]; _heap[par] = _heap[i]; _heap[i] = t; i = par; }
        }

        int Pop()
        {
            int top = _heap[0];
            _heap[0] = _heap[--_heapN];
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < _heapN && _f[_heap[l]] < _f[_heap[m]]) m = l;
                if (r < _heapN && _f[_heap[r]] < _f[_heap[m]]) m = r;
                if (m == i) break;
                int t = _heap[m]; _heap[m] = _heap[i]; _heap[i] = t; i = m;
            }
            return top;
        }

        /// <summary>Simple stupid funnel over the corridor's portals (XY), shrunk by the radius; heights from the polygons.</summary>
        void Funnel(HVec3 from, HVec3 to, float radius, List<HVec3> outPoints)
        {
            int n = _corridorLinks.Count;   // _corridorLinks[0] = -1 (start polygon)
            // portals as (left, right) seen walking forward; append the goal as a zero-width portal
            float apx = from.X, apy = from.Y;
            float lx = apx, ly = apy, rx = apx, ry = apy;
            int apIdx = 0, lIdx = 0, rIdx = 0;
            int count = n;   // portals 1..n-1 then the goal at n
            int guard = 0;
            for (int i = 1; i <= count && guard++ < 10000; i++)
            {
                float plx, ply, prx, pry;
                if (i < count) Portal(i, radius, out plx, out ply, out prx, out pry);
                else { plx = prx = to.X; ply = pry = to.Y; }
                // right side
                if (Tri(apx, apy, rx, ry, prx, pry) >= 0f)
                {
                    if ((apx == rx && apy == ry) || Tri(apx, apy, lx, ly, prx, pry) < 0f) { rx = prx; ry = pry; rIdx = i; }
                    else
                    {
                        Emit(lx, ly, lIdx, outPoints);
                        apx = lx; apy = ly; apIdx = lIdx; rx = apx; ry = apy; rIdx = apIdx; i = apIdx; continue;
                    }
                }
                // left side
                if (Tri(apx, apy, lx, ly, plx, ply) <= 0f)
                {
                    if ((apx == lx && apy == ly) || Tri(apx, apy, rx, ry, plx, ply) > 0f) { lx = plx; ly = ply; lIdx = i; }
                    else
                    {
                        Emit(rx, ry, rIdx, outPoints);
                        apx = rx; apy = ry; apIdx = rIdx; lx = apx; ly = apy; lIdx = apIdx; i = apIdx; continue;
                    }
                }
            }
            outPoints.Add(to);
        }

        void Emit(float x, float y, int portalIndex, List<HVec3> outPoints)
        {
            int poly = portalIndex < _corridor.Count ? _corridor[portalIndex] : _corridor[_corridor.Count - 1];
            float z = HeightAt(poly, x, y);
            if (outPoints.Count > 0) { var last = outPoints[outPoints.Count - 1]; if (Math.Abs(last.X - x) < 1e-4f && Math.Abs(last.Y - y) < 1e-4f) return; }
            outPoints.Add(new HVec3(x, y, z));
        }

        /// <summary>Portal i of the corridor (the link into _corridor[i]) as left / right ends seen walking forward, shrunk by the radius.</summary>
        void Portal(int i, float radius, out float lx, out float ly, out float rx, out float ry)
        {
            int j = _corridorLinks[i];
            // stored: a = left end seen from the polygon we leave (counter-clockwise ring: walking out, a is on the left)
            float ax = _pax[j], ay = _pay[j], bx = _pbx[j], by = _pby[j];
            float dx = bx - ax, dy = by - ay, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len > 2f * radius + 1e-4f) { float k = radius / len; ax += dx * k; ay += dy * k; bx -= dx * k; by -= dy * k; }
            else { ax = bx = (ax + bx) * 0.5f; ay = by = (ay + by) * 0.5f; }
            // make sure "left" really is left of the walking direction (from the previous polygon's centroid to the next one's)
            int p0 = _corridor[i - 1], p1 = _corridor[i];
            float wx = _cx[p1] - _cx[p0], wy = _cy[p1] - _cy[p0];
            float cross = wx * (ay - _cy[p0]) - wy * (ax - _cx[p0]);
            if (cross >= 0f) { lx = ax; ly = ay; rx = bx; ry = by; } else { lx = bx; ly = by; rx = ax; ry = ay; }
        }

        /// <summary>Twice the signed area of (a, b, c): &gt; 0 when c is left of a->b.</summary>
        static float Tri(float ax, float ay, float bx, float by, float cx, float cy) { return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax); }

        /// <summary>
        /// The point of island <paramref name="component"/> nearest to (x, y, z) within <paramref name="maxRadius"/> world units (grid search
        /// in growing rings; height differences count double). False when the island has no polygon that close.
        /// </summary>
        public bool NearestInComponent(int component, float x, float y, float z, float maxRadius, out HVec3 point)
        {
            point = new HVec3(x, y, z);
            if (component < 0 || PolyCount == 0 || _gw == 0) return false;
            int cx = (int)((x - _gx0) / _cell), cy = (int)((y - _gy0) / _cell);
            int rings = Math.Max(1, (int)Math.Ceiling(maxRadius / _cell));
            int best = -1; float bd = maxRadius * maxRadius;
            for (int r = 0; r <= rings; r++)
            {
                for (int gy = cy - r; gy <= cy + r; gy++)
                    for (int gx = cx - r; gx <= cx + r; gx++)
                    {
                        if (Math.Max(Math.Abs(gx - cx), Math.Abs(gy - cy)) != r) continue;   // the ring only
                        if (gx < 0 || gy < 0 || gx >= _gw || gy >= _gh) continue;
                        int c = gy * _gw + gx;
                        for (int k = _cellStart[c]; k < _cellStart[c + 1]; k++)
                        {
                            int p = _cellPolys[k];
                            if (_comp[p] != component) continue;
                            float dz = (_cz[p] - z) * 2f;
                            float d = DistXY2(p, x, y) + dz * dz;
                            if (d < bd) { bd = d; best = p; }
                        }
                    }
                if (best >= 0 && (r + 1) * _cell > Math.Sqrt(bd)) break;   // nothing closer can come from farther rings
            }
            if (best < 0) return false;
            point = ClosestPoint(best, x, y);
            return true;
        }

        /// <summary>A random point on a polygon near a centre within a radius (for search / wander): null-free; returns false when none found.</summary>
        public bool RandomPointNear(HVec3 centre, float radius, Random rng, out HVec3 point)
        {
            point = centre;
            int start = Locate(centre.X, centre.Y, centre.Z);
            if (start < 0) return false;
            // walk a few random links
            int p = start;
            int hops = 1 + rng.Next(4);
            for (int h = 0; h < hops; h++)
            {
                int deg = _adjStart[p + 1] - _adjStart[p];
                if (deg == 0) break;
                int q = _adjPoly[_adjStart[p] + rng.Next(deg)];
                float dx = _cx[q] - centre.X, dy = _cy[q] - centre.Y;
                if (dx * dx + dy * dy > radius * radius) break;
                p = q;
            }
            point = new HVec3(_cx[p], _cy[p], _cz[p]);
            return true;
        }

        /// <summary>Reads the navigation of BSP <paramref name="index"/> of a scenario (null when the BSP has no collision).</summary>
        public static HaloAiNav ForBsp(HaloScenario scenario, int index)
        {
            if (scenario == null || index < 0 || index >= scenario.Bsps.Count) return null;
            var r = scenario.BspRoot(index);
            if (r.Count(176) <= 0) return null;
            TagReader cb = default(TagReader);
            foreach (var c in r.Block(176, 96)) { cb = c; break; }
            byte[] pf = null;
            int n = r.Count(484);
            if (n > 0)
            {
                pf = new byte[n];
                int at = r.Off(r.U32(488));
                Buffer.BlockCopy(r.Map.Data, at, pf, 0, n);
            }
            return new HaloAiNav(index, cb, pf);
        }
    }
}

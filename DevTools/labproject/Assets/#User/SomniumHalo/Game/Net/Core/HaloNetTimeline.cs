using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A remote player's pose over time, timestamped by the writer (the stamp of the record), so what is drawn moves smoothly although Fusion delivers
    /// the writer's 10 Hz records in bursts of about 16 Hz (WoW's ride-pose "stamped samples" idea, Stage B). Each new record adds a sample; the viewer draws
    /// the writer's clock minus a delay (<see cref="Delay"/>) by interpolating between the two samples around that time. The writer's clock is mapped to the
    /// viewer's by the smallest "arrival minus stamp" seen (the quickest delivery defines the offset), drifting up a little with every sample so a clock that
    /// runs fast on one side is followed. Pure C#.
    /// </summary>
    public sealed class HaloNetTimeline
    {
        public struct Sample
        {
            public double Stamp;        // the writer's clock, seconds
            public bool Vr;
            public float RootYaw;
            public HaloV3 Root, Head, LHand, RHand;
            public HaloQ HeadRot, LRot, RRot;
            /// <summary>The writer drives a vehicle (seat 0): its pose and velocity (the Vehicles module draws it from here).</summary>
            public bool InVehicle;
            public HaloV3 VehiclePos, VehicleVel;
            public HaloQ VehicleRot;
            /// <summary>The Vehicles module's flags; bit 1 = "respawned": a jump that is not interpolated.</summary>
            public int VehicleFlags;
        }

        /// <summary>How far behind the newest data the pose is drawn (seconds). A record every 100 ms, delivered about every 62 ms: 0.16 s keeps two samples around.</summary>
        public double Delay = 0.16;

        /// <summary>A jump of the root this far between two samples (a respawn, a teleport) is not interpolated (metres).</summary>
        public float SnapDistance = 6f;

        /// <summary>How fast the drawn time catches up when a quicker delivery shows it was too far behind (seconds per second: 0.3 = played 30 %
        /// faster until caught up, instead of a jump).</summary>
        public double CatchUpRate = 0.3;
        private double _target, _evalAt;

        private const int Size = 12;
        private readonly Sample[] _s = new Sample[Size];
        private int _count, _next;
        private double _offset;
        private bool _haveOffset;
        private int _snapIndex = -1;

        public int Count { get { return _count; } }

        /// <summary>The newest sample's stamp (0 when empty).</summary>
        public double NewestStamp { get { return _count == 0 ? 0.0 : _s[(_next + Size - 1) % Size].Stamp; } }

        /// <summary>The viewer's time the newest sample arrived (0 when empty).</summary>
        public double NewestArrival { get; private set; }

        public void Clear()
        {
            _count = 0;
            _next = 0;
            _haveOffset = false;
            _snapIndex = -1;
        }

        /// <summary>A record arrived (a new seq): its pose, stamped with the writer's clock, seen at the viewer's time <paramref name="viewerNow"/>.</summary>
        public void Add(double stampSeconds, double viewerNow, in Sample s)
        {
            NewestArrival = viewerNow;
            double lag = viewerNow - stampSeconds;
            if (!_haveOffset) { _offset = lag; _target = lag; _haveOffset = true; _evalAt = viewerNow; }
            else if (lag >= _target) _target = Math.Min(_target + 0.00005, lag);
            else _target = lag;      // a quicker delivery: the drawn time catches up gradually in Evaluate (a late first sample must not make the pose jump ahead)

            // the stamp must grow: a writer's clock that went back (a re-claimed slot) starts the timeline again
            if (_count > 0 && stampSeconds < _s[(_next + Size - 1) % Size].Stamp - 0.5)
            {
                _count = 0;
                _next = 0;
                _offset = lag;
                _target = lag;
                _snapIndex = -1;
            }

            bool snap = false;
            if (_count > 0)
            {
                Sample last = _s[(_next + Size - 1) % Size];
                snap = HaloV3.Distance(last.Root, s.Root) > SnapDistance || HaloV3.Distance(last.Head, s.Head) > SnapDistance
                       || last.InVehicle != s.InVehicle || (s.InVehicle && HaloV3.Distance(last.VehiclePos, s.VehiclePos) > SnapDistance * 4f)
                       || ((last.VehicleFlags ^ s.VehicleFlags) & 2) != 0;
            }

            if (_snapIndex == _next) _snapIndex = -1;      // that slot of the ring is about to be overwritten
            if (snap) _snapIndex = _next;
            _s[_next] = s;
            _s[_next].Stamp = stampSeconds;
            _next = (_next + 1) % Size;
            if (_count < Size) _count++;
        }

        /// <summary>The pose to draw at the viewer's time; false when there is no sample yet. Holds the newest sample when the data is late.</summary>
        public bool Evaluate(double viewerNow, out Sample result)
        {
            result = default(Sample);
            if (_count == 0) return false;
            double dt = viewerNow - _evalAt;
            _evalAt = viewerNow;
            if (_offset > _target) _offset = Math.Max(_target, _offset - Math.Max(0.0, dt) * CatchUpRate);
            else _offset = _target;
            double t = viewerNow - _offset - Delay;
            int newest = (_next + Size - 1) % Size;
            int oldest = (_next + Size - _count) % Size;
            if (t >= _s[newest].Stamp) { result = _s[newest]; return true; }
            if (t <= _s[oldest].Stamp) { result = _s[oldest]; return true; }
            // the newest sample at or before t, walking back from the newest
            int hi = newest;
            for (int n = 0; n < _count - 1; n++)
            {
                int lo = (hi + Size - 1) % Size;
                if (_s[lo].Stamp <= t)
                {
                    if (hi == _snapIndex) { result = _s[hi]; return true; }
                    double span = _s[hi].Stamp - _s[lo].Stamp;
                    float u = span > 1e-6 ? (float)((t - _s[lo].Stamp) / span) : 1f;
                    result = Blend(_s[lo], _s[hi], u);
                    return true;
                }

                hi = lo;
            }

            result = _s[oldest];
            return true;
        }

        private static Sample Blend(in Sample a, in Sample b, float u)
        {
            var r = new Sample
            {
                Stamp = a.Stamp + (b.Stamp - a.Stamp) * u,
                Vr = b.Vr,
                RootYaw = LerpAngle(a.RootYaw, b.RootYaw, u),
                Root = HaloV3.Lerp(a.Root, b.Root, u),
                Head = HaloV3.Lerp(a.Head, b.Head, u),
                HeadRot = HaloQ.Nlerp(a.HeadRot, b.HeadRot, u),
            };
            if (a.Vr && b.Vr)
            {
                r.LHand = HaloV3.Lerp(a.LHand, b.LHand, u);
                r.RHand = HaloV3.Lerp(a.RHand, b.RHand, u);
                r.LRot = HaloQ.Nlerp(a.LRot, b.LRot, u);
                r.RRot = HaloQ.Nlerp(a.RRot, b.RRot, u);
            }
            else
            {
                r.LHand = b.LHand;
                r.RHand = b.RHand;
                r.LRot = b.LRot;
                r.RRot = b.RRot;
            }

            r.InVehicle = b.InVehicle;
            r.VehicleFlags = b.VehicleFlags;
            if (a.InVehicle && b.InVehicle)
            {
                r.VehiclePos = HaloV3.Lerp(a.VehiclePos, b.VehiclePos, u);
                r.VehicleVel = HaloV3.Lerp(a.VehicleVel, b.VehicleVel, u);
                r.VehicleRot = HaloQ.Nlerp(a.VehicleRot, b.VehicleRot, u);
            }
            else
            {
                r.VehiclePos = b.VehiclePos;
                r.VehicleVel = b.VehicleVel;
                r.VehicleRot = b.VehicleRot;
            }

            return r;
        }

        private static float LerpAngle(float a, float b, float u)
        {
            float d = ((b - a) % 360f + 540f) % 360f - 180f;
            return (a + d * u + 360f) % 360f;
        }
    }
}

using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Moving the unit: kinematic, along <see cref="MoveVelocity"/>, walls stop it (a capsule cast against the world, scenery, vehicles and
    /// players' capsules, sliding along), the feet follow the ground (ray down, steps up to a third of the body), gravity when the ground
    /// drops away (airborne, landing soft / hard by the biped's landing velocities). Flying units (sentinels, the monitor) move in 3D at their
    /// tag's speed without gravity. The body turns toward what it looks / aims at (stationary: only past the biped's stationary turning
    /// threshold, the turn animations playing), else toward where it goes. Animation root motion (dives, evades, deaths) moves it too.
    /// </summary>
    public partial class HaloUnit
    {
        /// <summary>Gravity (m/s^2): Halo's globals gravity is 0.0036 world units / tick^2 ~ 9.8 m/s^2.</summary>
        public static float Gravity = 9.8f;
        /// <summary>What blocks a unit's body: map geometry, scenery, vehicles, the players' capsules.</summary>
        public static int BlockMask = HaloLayers.GroundMask | (1 << HaloLayers.Vehicle) | (1 << HaloLayers.Avatar);

        /// <summary>The body's facing (Unity yaw, degrees).</summary>
        public float Yaw => _yaw;
        /// <summary>On the ground (false while falling / jumping; flying units are never "on the ground").</summary>
        public bool Grounded => _grounded;
        /// <summary>The speed (m/s) of the unit's own move-front animation for its current stance and weapon: the pace it runs at in the game.</summary>
        public float NaturalSpeed => Animator != null ? Animator.NaturalSpeed * HaloSpace.Unit : 3f;
        /// <summary>Riding a vehicle seat (the unit sits on the seat marker; no own movement).</summary>
        public Transform Seat { get; private set; }
        public string SeatLabel { get; private set; }
        public Vector3 Velocity => _velocity;

        private float _yaw, _turnRate;
        private bool _grounded = true, _turning, _wasAirborne;
        private float _airTime, _fallSpeed;
        private Vector3 _velocity;
        private float _aimYaw, _aimPitch, _lookYaw, _lookPitch;
        private bool _aiming, _looking;
        private float _radius = 0.5f, _height = 1.8f, _stepUp = 0.6f;
        /// <summary>Spherical bipeds (infection forms: biped flag 'spherical' or a standing collision height of 0): one sphere of the collision radius.</summary>
        private bool _spherical;
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly Collider[] Overlaps = new Collider[16];
        /// <summary>Gap kept between the body and what it touches (m).</summary>
        private const float Skin = 0.02f;

        /// <summary>Lab / log counters: units put back from inside the floor, put back from outside the world, killed / removed out of the world,
        /// killed by a hard landing.</summary>
        public static int RecoveredFromFloor, RecoveredFromOutside, RemovedOutside, LandingDeaths;

        private void InitMove()
        {
            _spherical = Def.Spherical || Def.StandingHeight <= 0.01f;
            _radius = Mathf.Clamp(Def.Radius * HaloSpace.Unit, 0.1f, 1.5f);
            // a sphere is as high as wide; its step is its radius (infection forms climb what is under their middle)
            _height = _spherical ? 2f * _radius : Def.StandingHeight * HaloSpace.Unit;
            _stepUp = _spherical ? _radius : Mathf.Clamp(_height * 0.33f, 0.25f, 0.6f);      // ours: a third of the body (Halo's pill rolls over steps)
            if (Def.Flying) _grounded = false;
            else Settle();
        }

        /// <summary>Puts the feet on the ground under / just above the unit (spawning, teleports). Kept for callers: same as <see cref="Settle"/>.</summary>
        public void SnapToGround(float reach) { Settle(); }

        /// <summary>Suspended units skip TickMove / TickDeadMove / Vertical entirely (no gravity, no movement); clearing it runs <see cref="Settle"/>.</summary>
        public bool Suspended
        {
            get => _suspended;
            set
            {
                bool was = _suspended;
                _suspended = value;
                if (value) { _velocity = Vector3.zero; _fallSpeed = 0f; _platform = null; }
                if (was && !value && !_bspHidden && !Latched) Settle();      // (a latched infection form rides its host)
            }
        }
        private bool _suspended;

        /// <summary>
        /// Depenetrates from the ground (a start embedded in a ceiling or a floor: the nearest free point straight down / up within 2 m) then
        /// sweeps down up to 4 m (6 m from inside a solid) to walkable ground where the body fits, preferring the surface closest to the current feet height (spawn, teleport, restore, the end
        /// of a suspension). Without ground there the unit stays airborne and falls (swept).
        /// </summary>
        public void Settle()
        {
            if (Def == null || Def.Flying || Seat != null) return;
            Vector3 p = transform.position;
            Vector3 was = p;
            bool inside = InsideSolid(p);
            if (inside && _insideLogged++ < 6)
                HaloLog.Info("Characters: " + Def.Name + " starts inside a solid at " + p.ToString("F1") + " (" + (HaloWorld.Outside(p + Vector3.up * 0.1f) ? "outside the BSP" : "embedded: " + EmbedWhy(_embedWhy, _embedByKnee, _embedDist)) + "): settled on the nearest free floor");

            _velocity = Vector3.zero;
            _fallSpeed = 0f;
            _airTime = 0f;
            _platform = null;
            Leaping = false;
            float y;
            // the walkable floor nearest the feet height where the body fits (not inside a solid, room above it); a floor in a crack under
            // a slab is never chosen while a free one is near (else the unit is put there and found embedded at once)
            if (FreeGroundNear(p, inside ? 2f : 1f, inside ? 6f : 4f, out y) || FindGroundNear(p, 1f, inside ? 6f : 4f, out y))
            {
                p.y = y;
                _grounded = true;
                transform.position = p;
                // a spot against a wall (a squad's start point a few cm from it, a carrier's middle): the body out of the wall sideways
                if (PushOutOfWalls()) p = transform.position;
                MarkGround();
            }
            else
            {
                // no floor within reach: out of the solid (down first: a ceiling start has the room just below), then it falls
                for (int k = 1; inside && k <= 20; k++)
                {
                    float d = k * 0.1f;
                    if (!InsideSolid(p - Vector3.up * d)) { p -= Vector3.up * d; break; }
                    if (!InsideSolid(p + Vector3.up * d)) { p += Vector3.up * d; break; }
                }

                _grounded = false;
                transform.position = p;
            }

            if ((p - was).sqrMagnitude > 0.04f && _settlesLogged++ < 12)
                HaloLog.Info("Characters: " + Def.Name + " settled " + (p.y - was.y).ToString("+0.00;-0.00") + " m (" + (_grounded ? "on walkable ground" : "no ground within 4 m: falls") + ") at " + p.ToString("F1"));
        }

        private static int _settlesLogged, _insideLogged;
        private static readonly float[] FloorYs = new float[8];

        /// <summary>The walkable surfaces on the body's axis from <paramref name="up"/> above to <paramref name="down"/> below the feet (stepped
        /// down surface by surface: a ray reports one hit per collider), the one nearest the feet height where the unit is not inside a solid.</summary>
        private bool FreeGroundNear(Vector3 p, float up, float down, out float y)
        {
            y = p.y;
            int m = 0;
            Vector3 o = p + Vector3.up * up;
            float left = up + down;
            RaycastHit h;
            while (m < FloorYs.Length && left > 0f && Physics.Raycast(o, Vector3.down, out h, left, HaloLayers.GroundMask | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Ignore))
            {
                if (!IsOwn(h.collider) && Walkable(h.normal)) FloorYs[m++] = h.point.y;
                float step = h.distance + 0.02f;
                o.y -= step;
                left -= step;
            }

            float best = float.MaxValue;
            for (int i = 0; i < m; i++)
            {
                float d = Mathf.Abs(FloorYs[i] - p.y);
                if (d >= best || InsideSolid(new Vector3(p.x, FloorYs[i], p.z))) continue;
                best = d;
                y = FloorYs[i];
            }

            return best < float.MaxValue;
        }

        /// <summary>The last position the unit stood on walkable ground (checked 4 times a second; outside a solid).</summary>
        public Vector3 LastGroundPos => _hasGroundPos ? _lastGroundPos : transform.position;

        /// <summary>True from a <see cref="Leap"/> until landing or contact.</summary>
        public bool Leaping { get; private set; }

        /// <summary>One-shot: the unit was recovered from inside / under the floor (the AI consumes it to log and repath).</summary>
        public bool WasRecovered { get; set; }

        /// <summary>One ballistic launch (m/s): airborne, no re-aim; <see cref="Leaping"/> until landing or contact.</summary>
        public void Leap(Vector3 velocityMetersPerSecond)
        {
            if (!Alive || Seat != null || Def == null || Def.Flying || _suspended || _bspHidden) return;
            _velocity = new Vector3(velocityMetersPerSecond.x, 0f, velocityMetersPerSecond.z);
            _fallSpeed = -velocityMetersPerSecond.y;
            _grounded = false;
            _airTime = 0f;
            _platform = null;
            Leaping = true;
            _leapAirPlayed = false;
            Jump = false;
            if (Animator != null) Animator.PlayLeap(HaloUnitAnimator.LeapPhase.Start);
            if (_leapsLogged++ < 6) HaloLog.Info("Characters: " + Def.Name + " leaps at " + velocityMetersPerSecond.magnitude.ToString("0.0") + " m/s (" + velocityMetersPerSecond.ToString("F1") + ")");
        }

        private bool _leapAirPlayed;
        private static int _leapsLogged;

        /// <summary>Additive airborne launch (m/s) for living and dead units (explosions): the body flies, lands (living: airborne -> land;
        /// bodies: airborne-dead -> landing-dead) with the same swept fall.</summary>
        public void AddImpulse(Vector3 velocityMetersPerSecond)
        {
            if (Def == null || Seat != null || Puppet || _suspended || _bspHidden) return;
            Vector3 v = velocityMetersPerSecond;
            if (Def.Flying && Alive) { _velocity += v; return; }
            _velocity += new Vector3(v.x, 0f, v.z);
            _fallSpeed -= v.y;
            if (v.y > 0.1f || !_grounded)
            {
                _grounded = false;
                _airTime = 0f;
                _platform = null;
            }
        }

        /// <summary>The natural speed (m/s) of the clip for moving in a world direction (strafe / back at their own speed).</summary>
        public float NaturalSpeedToward(Vector3 dir)
        {
            if (Animator == null) return NaturalSpeed;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return NaturalSpeed;
            dir.Normalize();
            float mx = Vector3.Dot(dir, transform.forward), my = -Vector3.Dot(dir, transform.right);      // Halo: x forward, y left
            float s = Animator.MaxSpeedFor(mx, my);
            return s > 0.01f ? s * HaloSpace.Unit : NaturalSpeed;
        }

        /// <summary>Puts the unit at a point facing a Unity yaw (scripts, teleports, checkpoint restores): settled on the walkable surface closest
        /// to that point's height.</summary>
        public void Teleport(Vector3 position, float yawDegrees)
        {
            transform.position = position;
            _yaw = yawDegrees;
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _velocity = Vector3.zero;
            _fallSpeed = 0f;
            _platform = null;
            Leaping = false;
            if (!Def.Flying && Seat == null) Settle();
        }

        /// <summary>Sits the unit on a vehicle seat marker with the graph's seat of that label ("W-passenger", "G-driver" ...).</summary>
        public void EnterSeat(Transform seatMarker, string seatLabel)
        {
            Seat = seatMarker;
            SeatLabel = seatLabel;
            if (seatMarker != null)
            {
                transform.SetParent(seatMarker, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.Euler(0f, 90f, 0f);      // the seat marker's +X (Halo forward) is the unit's forward
            }

            _exiting = false;
            // the seat's own enter clip (unit seat animation 7: climbing in), played in the seat's frame
            HaloAnimation enter = SeatClip(HaloAnimIds.Seat.Enter);
            if (enter != null && Alive) Animator.PlayCustom(Animator.Graph, enter, true, false, false);
        }

        /// <summary>
        /// Out of the seat. With the seat's exit clip (unit seat animation 8: the dropship troops' jump out of the pod, a marine climbing out of a
        /// warthog / pelican) the unit stays on the seat while the clip moves its body out, then stands where the clip left its pelvis (falling
        /// from there when that is in the air: airborne, then landing); without one it is put at <paramref name="at"/> at once.
        /// </summary>
        public void ExitSeat(Vector3 at, float yawDegrees)
        {
            if (Seat != null && Alive && !_exiting)
            {
                HaloAnimation exit = SeatClip(HaloAnimIds.Seat.Exit);
                if (exit != null && Animator.PlayCustom(Animator.Graph, exit, false, false, true))
                {
                    _exiting = true;
                    _exitAt = at;
                    _exitYaw = yawDegrees;
                    return;
                }
            }

            _exiting = false;
            Seat = null;
            SeatLabel = null;
            transform.SetParent(HaloBipeds.Parent, true);
            Teleport(at, yawDegrees);
        }

        /// <summary>Getting out: the exit clip playing on the seat (the seat is let go when it ends).</summary>
        public bool Exiting => _exiting;

        private bool _exiting;
        private Vector3 _exitAt;
        private float _exitYaw, _pelvisHeight = -1f;

        private HaloAnimation SeatClip(int id)
        {
            if (Animator == null || string.IsNullOrEmpty(SeatLabel)) return null;
            HaloAnimGraph.Seat s = Animator.Graph.FindSeat(SeatLabel);
            HaloAnimation a = s != null ? Animator.Pick(s.Anim(id)) : null;
            return a != null && a.Decode() ? a : null;
        }

        /// <summary>The exit clip ended: stand where it left the body (feet under the pelvis at the body's own standing pelvis height), facing
        /// the way it faces; in the air it falls and lands (Halo's airborne / land animations), then moves normally.</summary>
        private void FinishExit()
        {
            _exiting = false;
            Vector3 pelvis = _pelvisNode != null ? _pelvisNode.position : transform.position;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            float yaw = fwd.sqrMagnitude > 1e-4f ? Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg : _exitYaw;
            float ph = _pelvisHeight > 0.05f ? _pelvisHeight : 0.9f;
            Vector3 feet = new Vector3(pelvis.x, pelvis.y - ph, pelvis.z);
            Seat = null;
            SeatLabel = null;
            transform.SetParent(HaloBipeds.Parent, true);
            // walkable ground under the pelvis (a clip ending crouched / at the ground): the feet on it, else they hang under the pelvis and fall
            bool onGround = false;
            float gy;
            if (ExitGround(pelvis, ph, out gy)) { feet.y = gy; onGround = true; }
            // never inside the vehicle's hull (or another vehicle)
            Vector3 pushed = PushOutOfVehicles(feet);
            bool wasPushed = (pushed - feet).sqrMagnitude > 1e-4f;
            feet = pushed;
            transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            _yaw = yaw;
            _velocity = Vector3.zero;
            _fallSpeed = 0f;
            _platform = null;
            _grounded = onGround && !wasPushed;
            _airTime = _grounded ? 0f : 0.15f;
            if (_grounded) MarkGround();
            if (Animator != null) Animator.StopCustom(false);      // (no blend: the clip's pelvis offset is now the root's move)
            if (_exitsLogged++ < 12) HaloLog.Info("Characters: " + Def.Name + " got out (exit clip) at " + feet.ToString("F1") + ", " + (feet - _exitAt).magnitude.ToString("0.0") + " m from the vehicle's stand point, " +
                                                  (onGround ? "feet on the ground " + (pelvis.y - ph - gy).ToString("+0.00;-0.00") + " m from under the pelvis" : "no ground within " + (ph + 0.5f).ToString("0.0") + " m: falls") + (wasPushed ? ", pushed out of a vehicle" : ""));
        }

        /// <summary>A sphere down from the pelvis + 1 m for the pelvis height + 1.5 m: the walkable ground's height (below the pelvis only).</summary>
        private bool ExitGround(Vector3 pelvis, float pelvisHeight, out float y)
        {
            y = 0f;
            float rr = _radius * 0.5f;
            Vector3 o = pelvis + Vector3.up * 1f;
            int n = Physics.SphereCastNonAlloc(o, rr, Vector3.down, Hits, pelvisHeight + 1.5f, HaloLayers.GroundMask | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Hits[i];
                if (h.collider == null || IsOwn(h.collider) || h.distance <= 0f || h.point.y > pelvis.y) continue;      // (the hull above the pelvis)
                if (h.distance >= bd) continue;
                bd = h.distance;
                found = Walkable(h.normal);
                if (found) y = GroundY(o.y - h.distance, rr, h);
            }

            return found;
        }

        private static CapsuleCollider _probeCapsule;

        private static void EnsureProbeCapsule()
        {
            if (_probeCapsule != null) return;
            var go = new GameObject("HaloUnit depenetration probe");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.position = new Vector3(0f, -10000f, 0f);      // (far away: only its shape is used, at explicit poses)
            _probeCapsule = go.AddComponent<CapsuleCollider>();
            _probeCapsule.isTrigger = true;
            _probeCapsule.direction = 1;
        }

        /// <summary>The body's capsule pushed out of overlapping vehicle colliders (Physics.ComputePenetration, up to 3 passes).</summary>
        private Vector3 PushOutOfVehicles(Vector3 feet)
        {
            EnsureProbeCapsule();
            float r = _radius * 0.9f;
            _probeCapsule.radius = r;
            _probeCapsule.height = Mathf.Max(2f * r, _height);
            _probeCapsule.center = new Vector3(0f, Mathf.Max(r, _height * 0.5f), 0f);
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3 a = feet + Vector3.up * r, b = feet + Vector3.up * Mathf.Max(r, _height - r);
                int n = Physics.OverlapCapsuleNonAlloc(a, b, r, Overlaps, 1 << HaloLayers.Vehicle, QueryTriggerInteraction.Ignore);
                bool moved = false;
                for (int i = 0; i < n; i++)
                {
                    Collider c = Overlaps[i];
                    if (c == null || IsOwn(c)) continue;
                    Vector3 dir; float dist;
                    if (Physics.ComputePenetration(_probeCapsule, feet, Quaternion.identity, c, c.transform.position, c.transform.rotation, out dir, out dist) && dist > 1e-4f)
                    {
                        feet += dir * (dist + Skin);
                        moved = true;
                    }
                }

                if (!moved) break;
            }

            return feet;
        }

        private static int _exitsLogged;

        /// <summary>The body's standing pelvis height (the stand seat's first class idle, frame 0): where the feet are under a pelvis.</summary>
        private void MeasurePelvis()
        {
            _pelvisHeight = -1f;
            if (Animator == null || _skel == null) return;
            int pv = Def.PelvisNode >= 0 && Def.PelvisNode < _skel.Count ? Def.PelvisNode : _skel.Node("bip01 pelvis");
            if (pv < 0) return;
            HaloAnimGraph g = Animator.Graph;
            HaloAnimGraph.Seat s = g.FindSeat("stand") ?? (g.Seats.Count > 0 ? g.Seats[0] : null);
            HaloAnimation idle = null;
            if (s != null) for (int i = 0; i < s.Weapons.Count && idle == null; i++) idle = g.Get(s.Weapons[i].Anim(HaloAnimIds.Weapon.Idle));
            var p = new HaloPose(_skel);
            p.Reset();
            if (idle != null) p.Apply(idle, Animator.Map, 0f, true, 1f);
            p.ToModel();
            _pelvisHeight = p.MTrans[pv * 3 + 2] * HaloSpace.Unit;
        }

        private void TickMove(float dt)
        {
            if (Seat != null) { _velocity = Vector3.zero; _platform = null; AimAngles(dt); return; }
            Carry();
            Vector3 want = Frozen || WalkingInhibited ? Vector3.zero : MoveVelocity;      // (WalkingInhibited: flood legs shot off)
            if (!Def.Flying) want.y = 0f;
            // dives, evades, landings, Base gestures / hard pings and Base melee own the body: it moves only by their root motion
            bool rooted = Animator != null && (Animator.Action == HaloUnitAnimator.ActionKind.Dive || Animator.CustomPlaying || Animator.BlocksMovement);
            if (rooted && !Leaping) want = Vector3.zero;
            // animation-driven bipeds (no unit max velocity: every NPC biped) move at most at the pace of the direction clip they play
            // (strafing / backing up slower than running, as Halo moves them by those clips' frame info)
            if (!Def.Flying && Def.MaxVelocity <= 0.01f && Animator != null)
            {
                float m2 = want.sqrMagnitude;
                if (m2 > 1e-4f)
                {
                    float m = Mathf.Sqrt(m2);
                    float mx = Vector3.Dot(want, transform.forward) / m, my = -Vector3.Dot(want, transform.right) / m;
                    float max = Animator.MaxSpeedFor(mx, my) * HaloSpace.Unit;
                    if (max > 0.05f && m > max) want *= max / m;
                }
            }

            Facing(dt, want);
            AimAngles(dt);
            if (Def.Flying) { Fly(dt, want); return; }

            if (Jump && _grounded && !Frozen && !Leaping)
            {
                Jump = false;
                _grounded = false;
                _platform = null;
                _fallSpeed = -Mathf.Max(0.5f, Def.JumpVelocity * 30f * HaloSpace.Unit);      // jump velocity: world units a tick
            }
            else Jump = false;

            if (Leaping && !_leapAirPlayed && _airTime > 0.15f && Animator != null)
            {
                _leapAirPlayed = true;
                Animator.PlayLeap(HaloUnitAnimator.LeapPhase.Airborne);
            }

            // accelerate toward the wanted velocity (Halo bipeds: quick on the ground, little control in the air; ours); a leap keeps its launch
            Vector3 flat = new Vector3(_velocity.x, 0f, _velocity.z);
            Vector3 v;
            if (Leaping) v = flat;
            else if (!_grounded) v = Vector3.MoveTowards(flat, want, 4f * dt);
            else v = Vector3.MoveTowards(flat, want, 40f * dt);
            _velocity = v;
            MoveHorizontal(v * dt);
            Vertical(dt);
        }

        private void TickDeadMove(float dt)
        {
            if (Seat != null || Def.Flying && Animator != null && !Animator.Dying) return;
            Carry();
            // a body slides to a stop on the ground (ours: 8 m/s^2); in the air (an explosion threw it) it keeps its flight
            Vector3 flat = new Vector3(_velocity.x, 0f, _velocity.z);
            _velocity = _grounded ? Vector3.MoveTowards(flat, Vector3.zero, 8f * dt) : flat;
            MoveHorizontal(_velocity * dt);
            Vertical(dt);
        }

        /// <summary>The body turns toward the aim / look (stationary: past the turning threshold), else toward the movement.</summary>
        private void Facing(float dt, Vector3 want)
        {
            if (Animator != null && Animator.CustomPlaying) { _turnRate = 0f; return; }      // (the custom animation turns the body itself)
            Vector3 flatMove = new Vector3(want.x, 0f, want.z);
            bool moving = flatMove.sqrMagnitude > 0.01f;
            Vector3 target = AimPoint != Vector3.zero ? AimPoint : LookPoint;
            float desired = _yaw;
            bool hasTarget = target != Vector3.zero;
            if (_recorded) { desired = _recordedYaw; hasTarget = false; moving = true; }      // a recording: the recorded facing, turned like moving
            else if (hasTarget && !(moving && Animator != null && Animator.FaceMovement))
            {
                Vector3 d = target - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) desired = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            }
            else if (moving) desired = Mathf.Atan2(flatMove.x, flatMove.z) * Mathf.Rad2Deg;

            float err = Mathf.DeltaAngle(_yaw, desired);
            float speed = Mathf.Max(90f, (Def.MovingTurningSpeed > 0.01f ? Def.MovingTurningSpeed : 3.14f) * Mathf.Rad2Deg);
            _turnRate = 0f;
            if (moving || Def.Flying || Def.TurnsWithoutAnimating || !hasTarget)
            {
                _turning = false;
                float stepDeg = Mathf.Clamp(err, -speed * dt, speed * dt);
                _yaw += stepDeg;
            }
            else
            {
                // standing: the legs stay until the aim leaves the threshold, then turn (the turn animations) until it is ahead again
                float threshold = Def.StationaryTurningThreshold > 0.01f ? Def.StationaryTurningThreshold * Mathf.Rad2Deg : 60f;
                if (!_turning && Mathf.Abs(err) > threshold) _turning = true;
                if (_turning)
                {
                    float stepDeg = Mathf.Clamp(err, -speed * dt, speed * dt);
                    _yaw += stepDeg;
                    _turnRate = -stepDeg * Mathf.Deg2Rad / dt;      // + = left (Halo)
                    if (Mathf.Abs(err) < 3f) _turning = false;
                }
            }

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        /// <summary>Aim and look angles relative to the body (Halo: yaw + left, pitch + up), turned at the biped's aiming / looking speeds.</summary>
        private void AimAngles(float dt)
        {
            Vector3 eye = Eye;
            Vector3 fwd = transform.forward;
            float ay = 0f, ap = 0f, ly = 0f, lp = 0f;
            _aiming = AimPoint != Vector3.zero;
            _looking = LookPoint != Vector3.zero || _aiming;
            Vector3 aimFrom = Muzzle != null ? Vector3.Lerp(Center, eye, 0.7f) : eye;
            if (_aiming) Angles(AimPoint - aimFrom, fwd, out ay, out ap);
            if (_looking) Angles((LookPoint != Vector3.zero ? LookPoint : AimPoint) - eye, fwd, out ly, out lp);
            float aimSpeed = (Def.AimingVelocityMaximum > 0.01f ? Def.AimingVelocityMaximum : 6.28f) * dt;
            float lookSpeed = (Def.LookingVelocityMaximum > 0.01f ? Def.LookingVelocityMaximum : 12.5f) * dt;
            _aimYaw = Mathf.MoveTowards(_aimYaw, ay, aimSpeed);
            _aimPitch = Mathf.MoveTowards(_aimPitch, ap, aimSpeed);
            _lookYaw = Mathf.MoveTowards(_lookYaw, ly, lookSpeed);
            _lookPitch = Mathf.MoveTowards(_lookPitch, lp, lookSpeed);
        }

        private static void Angles(Vector3 d, Vector3 fwd, out float yaw, out float pitch)
        {
            yaw = 0f; pitch = 0f;
            if (d.sqrMagnitude < 1e-4f) return;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            yaw = -Vector3.SignedAngle(new Vector3(fwd.x, 0f, fwd.z), flat, Vector3.up) * Mathf.Deg2Rad;
            pitch = Mathf.Atan2(d.y, flat.magnitude);
        }

        /// <summary>The last horizontal move hit something (a wall, a steep face, a body).</summary>
        private bool _blocked;

        /// <summary>Walks the body along a horizontal step, swept: the body above the step height stops at anything; the legs (5 cm above the
        /// feet up to the step height) stop at faces steeper than the biped's maximum slope that are not a step; it slides along what it hits.</summary>
        private void MoveHorizontal(Vector3 step)
        {
            _blocked = false;
            step.y = 0f;
            float len = step.magnitude;
            if (len < 1e-5f) return;
            Vector3 pos = transform.position;
            for (int pass = 0; pass < 3 && len > 1e-5f; pass++)
            {
                Vector3 dir = step / len;
                RaycastHit h;
                if (BodyCast(pos, dir, len, out h))
                {
                    if (!_climbHit) _blocked = true;      // (a climber going up a steep face is not blocked: it climbs)
                    float go = Mathf.Max(0f, h.distance - Skin);
                    pos += dir * go;
                    Vector3 n = h.normal; n.y = 0f;
                    if (n.sqrMagnitude < 1e-4f) break;
                    n.Normalize();
                    Vector3 rest = step - dir * go;
                    step = rest - n * Vector3.Dot(rest, n);
                    step.y = 0f;
                    len = step.magnitude;
                }
                else { pos += step; break; }
            }

            transform.position = pos;
            if (_blocked && Leaping) EndLeap(true);
        }

        /// <summary>A leap ends on landing or on contact (a wall, a body): its launch speed is gone.</summary>
        private void EndLeap(bool contact)
        {
            if (!Leaping) return;
            Leaping = false;
            if (contact) _velocity = Vector3.zero;
        }

        /// <summary>The nearest blocking hit of the body moving <paramref name="len"/> along <paramref name="dir"/> from the feet.</summary>
        private bool BodyCast(Vector3 feet, Vector3 dir, float len, out RaycastHit best)
        {
            best = default(RaycastHit);
            _climbHit = false;
            float bd = float.MaxValue;
            float r = _radius * 0.9f;
            int n;
            if (_spherical)
            {
                // one sphere: steep faces block, slopes and steps up to its radius are rolled over
                n = Physics.SphereCastNonAlloc(feet + Vector3.up * _radius, r, dir, Hits, len + Skin, BlockMask, QueryTriggerInteraction.Ignore);
                _castLen = len;
                Nearest(n, feet, dir, true, ref best, ref bd);
                return bd < float.MaxValue;
            }

            // the body above the step height: everything blocks
            float bottom = _stepUp + r;
            float top = Mathf.Max(bottom, _height - r);
            n = Physics.CapsuleCastNonAlloc(feet + Vector3.up * bottom, feet + Vector3.up * top, r, dir, Hits, len + Skin, BlockMask, QueryTriggerInteraction.Ignore);
            Nearest(n, feet, dir, false, ref best, ref bd);
            // the legs: from 5 cm above the feet to the step height - only faces steeper than the maximum slope that are not a step
            float lo = 0.05f + r, hi = Mathf.Max(lo, _stepUp - r);
            n = Physics.CapsuleCastNonAlloc(feet + Vector3.up * lo, feet + Vector3.up * hi, r, dir, Hits, len + Skin, BlockMask, QueryTriggerInteraction.Ignore);
            _castLen = len;
            Nearest(n, feet, dir, true, ref best, ref bd);
            return bd < float.MaxValue;
        }

        /// <summary>The length of the move being swept (for <see cref="RollsOver"/>).</summary>
        private float _castLen;

        /// <summary>The best hit of the last <see cref="BodyCast"/> is a steep face a climber goes up (<see cref="ClimbAdvance"/>), not a wall.</summary>
        private bool _climbHit;

        /// <summary>
        /// The legs meet a face they may go over (the ground judges the height): a face the biped can stand on. 'Can climb any surface'
        /// units (infection forms: any face that is not an overhang is ground) roll over faces up to 60 degrees; steeper ones they climb by
        /// <see cref="ClimbAdvance"/>; a leap ends against any face steeper than 45 degrees (ours), as Halo's leaping forms stop on contact.
        /// </summary>
        private bool RollsOver(Vector3 n)
        {
            if (!Walkable(n)) return false;
            if (Def == null || !Def.CanClimbAnySurface) return true;
            if (Leaping) return n.y >= 0.7f;
            return n.y >= 0.5f;
        }

        /// <summary>
        /// A climber ('can climb any surface', not leaping) walking into a steep face it can stand on (steeper than 60 degrees, not an
        /// overhang): how far it may go into the face this step - as far as the rise that needs is half the step height (the ground probe,
        /// which reaches a step up, then lifts it onto the face: up the face a little every frame, never through it). 0 = not a climb.
        /// </summary>
        private float ClimbAdvance(Vector3 n, Vector3 dir)
        {
            if (Def == null || !Def.CanClimbAnySurface || Leaping || !Walkable(n) || n.y >= 0.5f) return 0f;
            float nh = Mathf.Sqrt(Mathf.Max(0f, 1f - n.y * n.y));
            if (nh < 1e-4f) return 0f;
            float into = -(dir.x * n.x + dir.z * n.z) / nh;      // (the share of the move straight into the face)
            if (into < 0.05f) return float.MaxValue;              // (along or away from it: no rise needed)
            return _stepUp * 0.5f / (into * nh / Mathf.Max(n.y, 0.01f));
        }

        private void Nearest(int n, Vector3 feet, Vector3 dir, bool legs, ref RaycastHit best, ref float bd)
        {
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Hits[i];
                if (h.collider == null || IsOwn(h.collider)) continue;
                if (h.distance <= 0f)
                {
                    // started inside: only what faces us. The legs ask the face itself (an overlap's normal is only "against the move"):
                    // a walkable slope under the legs never blocks
                    if (Vector3.Dot(h.normal, dir) >= 0f) continue;
                    if (legs)
                    {
                        float lift = _spherical ? _radius : 0.05f + _radius * 0.9f;
                        RaycastHit f;
                        if (!h.collider.Raycast(new Ray(feet + Vector3.up * lift, dir), out f, _radius * 1.5f + Skin)) continue;
                        h.normal = f.normal;
                        h.point = f.point;
                    }

                    h.distance = 0f;
                }

                if (h.distance >= bd) continue;
                bool climb = false;
                if (legs)
                {
                    if (RollsOver(h.normal)) continue;
                    float adv = ClimbAdvance(h.normal, dir);
                    if (adv > 0f)
                    {
                        // a climber on a steep face: into it only as far as this step's lift (the rest of the move slides along the face)
                        if (h.distance + adv >= _castLen) continue;
                        h.distance += adv + Skin;      // (the mover stops Skin short of the hit: it ends adv into the face)
                        if (h.distance >= bd) continue;
                        climb = true;
                    }
                    else if (IsStep(feet, dir, h)) continue;
                }

                bd = h.distance;
                best = h;
                _climbHit = climb;
            }
        }

        /// <summary>A riser whose walkable top is at most the step height above the feet (stairs, curbs): climbed, not a wall.</summary>
        private bool IsStep(Vector3 feet, Vector3 dir, RaycastHit h)
        {
            float ahead = Mathf.Max(0f, h.distance) + _radius * 0.9f + 0.1f;
            Vector3 o = feet + dir * ahead + Vector3.up * (_stepUp + 0.05f);
            RaycastHit g;
            if (!Physics.Raycast(o, Vector3.down, out g, _stepUp + 0.1f, HaloLayers.GroundMask | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Ignore)) return false;
            if (!Walkable(g.normal) || IsOwn(g.collider)) return false;
            if (Def != null && Def.CanClimbAnySurface && g.normal.y < 0.5f) return false;      // (a climber's step has a floor on top, not the wall itself)
            return g.point.y - feet.y <= _stepUp + 0.01f && g.point.y >= h.point.y - 0.05f;
        }

        /// <summary>Ground the biped can stand on: normal z at least the tag's cosine of the maximum slope angle (45 deg marines / flood
        /// humans, 60 deg grunts / elites / jackals); 'can climb any surface' (infection forms): anything that is not an overhang.</summary>
        private bool Walkable(Vector3 n)
        {
            if (Def != null && Def.CanClimbAnySurface) return n.y >= 0.05f;
            float c = Def != null ? Mathf.Clamp(Def.CosMaxSlope, 0.3f, 0.95f) : 0.5f;      // (clamp: ours, against a broken tag)
            return n.y >= c - 0.001f;
        }

        /// <summary>
        /// Gravity and the ground. Grounded: the feet follow the walkable ground under them (steps up to the step height, down 0.35 m). Rising
        /// (jump, leap, explosion): a sphere of the body's radius sweeps up and a ceiling stops it. Falling: a sphere at the feet sweeps the
        /// fall; it lands on walkable ground, and on a face steeper than the maximum slope it stops and slides down along it - never through.
        /// </summary>
        private void Vertical(float dt)
        {
            Vector3 p = transform.position;
            if (_fallSpeed < 0f)
            {
                _grounded = false;
                _platform = null;
                _airTime += dt;
                _fallSpeed += Gravity * dt;
                float rise = -_fallSpeed * dt;
                if (rise > 0f)
                {
                    float r = _radius * 0.9f;
                    Vector3 c = p + Vector3.up * Mathf.Max(r, _height - r);
                    RaycastHit h;
                    if (SweepSphere(c, r, Vector3.up, rise + Skin, out h))
                    {
                        rise = Mathf.Max(0f, h.distance - Skin);
                        _fallSpeed = 0f;
                        EndLeap(true);
                    }

                    p.y += rise;
                }

                transform.position = p;
                return;
            }

            float fall = 0f;
            if (!_grounded)
            {
                _airTime += dt;
                _fallSpeed += Gravity * dt;
                fall = _fallSpeed * dt;
            }

            float rr = _spherical ? _radius * 0.9f : _radius * 0.5f;
            float reach = _grounded ? Mathf.Max(0.35f, _carryReach) : fall + Skin;
            bool leaving = Leaping && _airTime < 0.1f;      // a flat leap leaves the ground first
            float gy;
            Collider gc;
            if (!leaving && FindGround(p, _stepUp, reach, rr, out gy, out gc))
            {
                if (!_grounded) Landed();
                _grounded = true;
                _fallSpeed = 0f;
                _airTime = 0f;
                p.y = gy;
                transform.position = p;
                TrackGround(gc);
                return;
            }

            if (_grounded)
            {
                // walked off an edge, or onto a face too steep to stand on: airborne from here
                _grounded = false;
                _platform = null;
                _airTime += dt;
                _fallSpeed += Gravity * dt;
                fall = _fallSpeed * dt;
            }

            // nothing at all below and never stood anywhere (the world's collision not built yet): wait in place instead of falling forever
            if (!_hasGroundPos && !Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, 60f, HaloLayers.GroundMask | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Ignore))
            {
                _fallSpeed = 0f;
                _airTime = 0f;
                return;
            }

            if (fall <= 0f) { transform.position = p; return; }
            float rf = _spherical ? _radius * 0.9f : _radius * 0.85f;
            Vector3 fc = p + Vector3.up * (rf + 0.05f);
            RaycastHit fh;
            if (!SweepSphere(fc, rf, Vector3.down, fall + 0.05f + Skin, out fh)) { p.y -= fall; transform.position = p; return; }

            float stop = Mathf.Max(0f, fh.distance - Skin);
            float descend = Mathf.Max(0f, stop - 0.05f);
            p.y -= descend;
            if (Walkable(fh.normal) && !OnLip(p, fh))
            {
                // landed on what the narrow ground probe did not see (an edge, a lip): the next frame's probe puts the feet exactly on it
                if (!_grounded) Landed();
                _grounded = true;
                _fallSpeed = 0f;
                _airTime = 0f;
                transform.position = p;
                TrackGround(fh.collider);
                return;
            }

            if (Walkable(fh.normal))
            {
                // the sphere's rim caught a lip (an edge; the face under the body's middle is steep or not there): it rolls off the edge,
                // away from it, and keeps falling - never perched over the drop (grounded / airborne every other frame)
                transform.position = p;
                Vector3 off = fh.point - p; off.y = 0f;
                float od = off.magnitude;
                if (od > 1e-3f) MoveHorizontal(-off / od * Mathf.Min(0.5f, Mathf.Max(0.02f, rf - od + 0.02f)));
                if (_blocked || od <= 1e-3f) Wedged();      // (no way off it: held by both sides, as standing)
                if (_lipsLogged++ < 4) HaloLog.Info("Characters: " + Def.Name + " rolls off a lip at " + transform.position.ToString("F1"));
                return;
            }

            // steeper than the biped's maximum slope: the rest of the fall slides down along the face
            EndLeap(true);
            Vector3 nrm = fh.normal;
            float rest = fall - descend;
            _fallSpeed *= Mathf.Clamp01(1f - nrm.y * nrm.y);      // the fall speed along the face
            Vector3 slide = (Vector3.down - nrm * Vector3.Dot(Vector3.down, nrm)) * rest;
            float sl = slide.magnitude;
            if (sl > 1e-4f)
            {
                Vector3 sd = slide / sl;
                RaycastHit sh;
                Vector3 sc = p + Vector3.up * (rf + 0.05f);
                float wantSl = sl;
                if (SweepSphere(sc, rf, sd, sl + Skin, out sh)) sl = Mathf.Max(0f, sh.distance - Skin);
                p += sd * sl;
                if (sl < wantSl * 0.1f) Wedged();      // (between two steep faces: held there, not falling faster every frame)
            }

            transform.position = p;
            if (_slidesLogged++ < 6) HaloLog.Info("Characters: " + Def.Name + " slides on a " + (Mathf.Acos(Mathf.Clamp01(nrm.y)) * Mathf.Rad2Deg).ToString("0") + " deg face (max slope " + (Mathf.Acos(Mathf.Clamp(Def.CosMaxSlope, 0.3f, 0.95f)) * Mathf.Rad2Deg).ToString("0") + " deg) at " + p.ToString("F1"));
        }

        private static int _slidesLogged, _lipsLogged;

        /// <summary>Held between faces that are each too steep to stand on (a V, a lip against a wall): it stays put without building a fall
        /// speed or playing the airborne animation (the next fall sweep checks again).</summary>
        private void Wedged()
        {
            _fallSpeed = 0f;
            _airTime = 0f;
        }

        /// <summary>A sphere contact with a walkable-looking normal that is an edge, not a face: just inside the contact, toward the body's
        /// axis, the collider's own surface is steep (or missing). (A sphere on an edge reports the edge-to-centre direction as its normal.)</summary>
        private bool OnLip(Vector3 feet, RaycastHit h)
        {
            if (h.collider == null) return false;
            Vector3 off = h.point - feet; off.y = 0f;
            float od = off.magnitude;
            if (od < 0.02f) return false;      // (under the axis: the ground itself)
            Vector3 o = h.point - off / od * 0.03f + Vector3.up * 0.1f;
            RaycastHit f;
            if (!h.collider.Raycast(new Ray(o, Vector3.down), out f, 0.25f)) return true;
            return !Walkable(f.normal);
        }

        /// <summary>The nearest hit of a sphere swept from <paramref name="c"/> (not own colliders; an overlap at the start counts only when the
        /// surface faces the move; surfaces the move only grazes are skipped).</summary>
        private bool SweepSphere(Vector3 c, float r, Vector3 dir, float len, out RaycastHit best)
        {
            int n = Physics.SphereCastNonAlloc(c, r, dir, Hits, len, HaloLayers.GroundMask | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Ignore);
            best = default(RaycastHit);
            float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Hits[i];
                if (h.collider == null || IsOwn(h.collider)) continue;
                float facing = Vector3.Dot(h.normal, dir);
                if (h.distance <= 0f) { if (facing >= 0f) continue; h.distance = 0f; }
                else if (facing > -0.02f) continue;      // (a wall beside a fall: grazed, not hit)
                if (h.distance < bd) { bd = h.distance; best = h; }
            }

            return bd < float.MaxValue;
        }

        /// <summary>
        /// The walkable ground under the feet: a sphere (half the body radius) swept down from the step height above the feet to
        /// <paramref name="down"/> below them; the nearest walkable hit gives the feet height (a single ray when the sphere finds nothing).
        /// </summary>
        private bool FindGround(Vector3 feet, float up, float down, float rr, out float y, out Collider col)
        {
            y = feet.y;
            col = null;
            Vector3 o = feet + Vector3.up * (up + rr);
            int n = Physics.SphereCastNonAlloc(o, rr, Vector3.down, Hits, up + down, HaloLayers.GroundMask | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            int bi = -1;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Hits[i];
                if (h.collider == null || h.distance <= 0f || IsOwn(h.collider)) continue;
                if (h.distance >= bd || !Walkable(h.normal)) continue;
                bd = h.distance;
                bi = i;
            }

            if (bi >= 0)
            {
                RaycastHit h = Hits[bi];
                float cy = o.y - h.distance;
                float plane = GroundY(cy, rr, h);
                // what is under the body's axis decides how the sphere's contact is read (a sweep's normal on an edge or in a crease is
                // the edge-to-centre direction, not a face): walkable ground at or above the contact's plane = that ground (a slope, a
                // crease against a bevel: never sunk into the floor); walkable ground lower but within a step = standing on the edge
                // (the sphere's bottom); nothing walkable within a step under the middle = a lip: not ground (it rolls off as it falls)
                Vector3 ao = new Vector3(feet.x, Mathf.Min(o.y, cy + rr), feet.z);
                float axisLen = (ao.y - cy) + rr + _stepUp;
                int an = Physics.RaycastNonAlloc(ao, Vector3.down, Hits, axisLen, HaloLayers.GroundMask | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Ignore);
                float ad = float.MaxValue;
                RaycastHit a = default(RaycastHit);
                for (int i = 0; i < an; i++)
                {
                    if (Hits[i].collider == null || IsOwn(Hits[i].collider) || Hits[i].distance >= ad) continue;
                    ad = Hits[i].distance;
                    a = Hits[i];
                }

                if (ad < float.MaxValue && Walkable(a.normal))
                {
                    y = a.point.y >= plane - 0.03f ? a.point.y : cy - rr;
                    col = a.point.y >= plane - 0.03f ? a.collider : h.collider;
                    return true;
                }

                bd = float.MaxValue;      // (a lip: the ray below may still find a floor under the middle within reach)
            }
            // the sphere started touching something (no distance), or met only a steep face first (a sweep reports one hit per collider and
            // the BSP is one collider per material: a bevel beside the feet hides the floor of the same material): one ray down the middle
            n = Physics.RaycastNonAlloc(o, Vector3.down, Hits, up + rr + down, HaloLayers.GroundMask | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Hits[i];
                if (h.collider == null || IsOwn(h.collider) || h.distance >= bd || !Walkable(h.normal)) continue;
                bd = h.distance;
                y = h.point.y;
                col = h.collider;
            }

            return bd < float.MaxValue;
        }

        /// <summary>The feet height under a probe sphere resting on a surface: its centre minus the radius over the surface's normal z (exact on
        /// a plane, the sphere's bottom on flat ground).</summary>
        private static float GroundY(float centreY, float rr, RaycastHit h)
        {
            float y = centreY - rr / Mathf.Max(h.normal.y, 0.5f);
            return Mathf.Max(y, h.point.y - rr);
        }

        /// <summary>Spawn / teleport / restore: walkable ground near <paramref name="p"/> (up to <paramref name="up"/> above, <paramref name="down"/>
        /// below), the surface closest to the feet height (a ray from just above the feet, and one from <paramref name="up"/> above for a step).</summary>
        private bool FindGroundNear(Vector3 p, float up, float down, out float y)
        {
            y = p.y;
            float best = float.MaxValue;
            for (int k = 0; k < 2; k++)
            {
                float from = k == 0 ? 0.05f : up;
                int n = Physics.RaycastNonAlloc(p + Vector3.up * from, Vector3.down, Hits, from + down, HaloLayers.GroundMask | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    RaycastHit h = Hits[i];
                    if (h.collider == null || IsOwn(h.collider) || !Walkable(h.normal)) continue;
                    float d = Mathf.Abs(h.point.y - p.y);
                    if (d < best) { best = d; y = h.point.y; }
                }
            }

            return best < float.MaxValue;
        }

        /// <summary>
        /// Landing: a fall at the biped's 'death hard landing velocity' or faster kills it (unless 'immune to falling damage'); else the land
        /// animation, hard from the 'minimum hard landing velocity' (all world units a second).
        /// </summary>
        private void Landed()
        {
            float v = _fallSpeed / HaloSpace.Unit;      // world units a second, as the biped's landing velocities
            EndLeap(false);
            if (Alive && !Puppet && !Def.ImmuneToFallingDamage && Def.DeathHardLandingVelocity > 0f && v >= Def.DeathHardLandingVelocity)
            {
                LandingDeaths++;
                if (_landDeathsLogged++ < 8) HaloLog.Info("Characters: " + Def.Name + " died of a hard landing (" + v.ToString("0.0") + " wu/s, death hard landing velocity " + Def.DeathHardLandingVelocity.ToString("0.0") + ")");
                Die(new HaloHit { SourcePlayer = -1 }, true, 0, 0, false, 0f);
                return;
            }

            if (_airTime < 0.25f || Animator == null || !Alive) return;
            try { HaloFootsteps.Step(transform.position, HaloFootsteps.Land, UnitTag, Mathf.Clamp(0.6f + _airTime, 0.6f, 1.2f)); } catch (System.Exception) { }   // (A: the landing sound of the unit's footsteps tag)
            // Halo: the landing holds the biped for its maximum soft / hard landing time scaled by the landing speed (the clip is cut there)
            bool hard;
            float secs = Def.LandingSeconds(v, out hard);
            if (secs > 0f) Animator.Land(hard, secs);
        }

        private static int _landDeathsLogged;

        // ------------------------------------------------------------------ moving platforms (lifts, Library platforms, vehicles)

        private Transform _platform;
        private Vector3 _platPos;
        private Quaternion _platRot;
        private Collider _platCol;
        private Transform _platColMover;
        private float _carryReach;
        private static bool _carryLogged;

        /// <summary>The unit stands on a collider: when it belongs to something that moves (a machine, a kinematic body, a vehicle) it is
        /// remembered with its pose, so the next frames carry the unit with it.</summary>
        private void TrackGround(Collider c)
        {
            if (c != _platCol)
            {
                _platCol = c;
                _platColMover = null;
                if (c != null)
                {
                    Rigidbody rb = c.attachedRigidbody;
                    bool moving = rb != null && rb.isKinematic || c.gameObject.layer == HaloLayers.Vehicle || c.GetComponentInParent<HaloDevice>() != null;
                    if (moving) _platColMover = rb != null ? rb.transform : c.transform;
                }
            }

            Transform t = _platColMover;
            if (t != _platform)
            {
                _platform = t;
                if (t != null) { _platPos = t.position; _platRot = t.rotation; }
            }
        }

        /// <summary>Applies the moving ground's motion since the last frame (position + yaw about its pivot), as HaloDevices.Carry does for
        /// the player; a stalled frame carries the whole delta, so a lift never leaves its riders under its floor.</summary>
        private void Carry()
        {
            _carryReach = 0f;
            if (_platform == null) return;
            if (!_grounded) { _platform = null; return; }
            Vector3 np = _platform.position;
            Quaternion nr = _platform.rotation;
            if (np == _platPos && nr == _platRot) return;
            Quaternion d = nr * Quaternion.Inverse(_platRot);
            Vector3 feet = transform.position;
            Vector3 moved = d * (feet - _platPos) + np - feet;
            _platPos = np;
            _platRot = nr;
            if (moved.sqrMagnitude > 25f) { _platform = null; return; }      // the machine jumped (placed / reset): not a ride
            float yaw = Mathf.DeltaAngle(0f, d.eulerAngles.y);
            transform.position = feet + moved;
            if (Mathf.Abs(yaw) > 1e-4f) { _yaw += yaw; transform.rotation = Quaternion.Euler(0f, _yaw, 0f); }
            _carryReach = Mathf.Abs(moved.y) + 0.1f;
            Physics.SyncTransforms();      // (machines move by transform: their colliders where they are now for this frame's probes)
            if (!_carryLogged) { _carryLogged = true; HaloLog.Info("Characters: units ride moving ground (" + Def.Name + " on " + _platform.name + ")"); }
        }

        // ------------------------------------------------------------------ inside the floor / outside the world

        private Vector3 _lastGroundPos;
        private bool _hasGroundPos, _floorLogged, _outsideLogged;
        private float _lastGroundTime = -100f, _floorCheckAt, _outsideCheckAt;
        private int _outsideStrikes;
        private bool _embedIgnore;
        private Vector3 _embedIgnorePos;
        private static int _embedIgnoredLogged;

        private void MarkGround()
        {
            _lastGroundPos = transform.position;
            _hasGroundPos = true;
            _lastGroundTime = Time.time;
        }

        /// <summary>For the log: what the last positive embedded test met (which test, the collider, its layer and parent, how high).</summary>
        private static Collider _embedWhy;
        private static bool _embedByKnee;
        private static float _embedDist;
        private static string EmbedWhy(Collider c, bool knee, float at)
        {
            if (c == null) return "-";
            Transform t = c.transform;
            return (knee ? "knee overlap" : "ray up meets a back face") + " at +" + at.ToString("0.00") + " m: '" + t.name + "' layer " + c.gameObject.layer
                   + (t.parent != null ? " under '" + t.parent.name + "'" : "");
        }

        /// <summary>Inside a solid at these feet: a surface through the knees (a 6 cm sphere on the body axis just above the step height: stairs never reach it), or the feet
        /// under a face (a ray up from 15 cm above them meets the back of a floor first).</summary>
        private bool Embedded(Vector3 feet)
        {
            float rr = _radius * 0.5f;
            Vector3 knee = feet + Vector3.up * (_stepUp + rr);
            int n = Physics.OverlapSphereNonAlloc(knee, 0.06f, Overlaps, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = Overlaps[i];
                if (c == null || IsOwn(c)) continue;
                // a wall through the knees (the body pressed into / spawned against a wall, a form on a near-vertical face) is not a floor
                // through them: PushOutOfWalls moves it sideways; only a floor-like face (the shortest way out is up or down) is "inside the floor"
                if (WallAt(c, knee, 0.06f)) continue;
                _embedWhy = c; _embedByKnee = true; _embedDist = _stepUp + rr; return true;
            }
            bool was = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.15f, Vector3.up, Hits, Mathf.Max(0.3f, _height - 0.15f), HaloLayers.GroundMask, QueryTriggerInteraction.Ignore);
            Physics.queriesHitBackfaces = was;
            float bd = float.MaxValue;
            bool back = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Hits[i];
                if (h.collider == null || IsOwn(h.collider) || h.distance >= bd) continue;
                bd = h.distance;
                back = h.normal.y > 0.1f;      // (a ray going up meets a floor's top from below: its back)
                _embedWhy = h.collider;
                _embedByKnee = false;
                _embedDist = 0.15f + h.distance;
            }

            return back;
        }

        /// <summary>In the active BSP's solid (or the void around it), or embedded in a surface.</summary>
        private bool InsideSolid(Vector3 feet)
        {
            return HaloWorld.Outside(feet + Vector3.up * 0.1f) || Embedded(feet);
        }

        private static SphereCollider _probeSphere;

        /// <summary>A small sphere at <paramref name="at"/> overlapping <paramref name="c"/> gets out of it sideways (the face there is a wall:
        /// the penetration direction is within 60 degrees of horizontal). No penetration answer = not a wall (the old, strict reading).</summary>
        private static bool WallAt(Collider c, Vector3 at, float r)
        {
            if (_probeSphere == null)
            {
                var go = new GameObject("HaloUnit wall probe");
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.position = new Vector3(0f, -10000f, 0f);      // (far away: only its shape is used, at explicit poses)
                _probeSphere = go.AddComponent<SphereCollider>();
                _probeSphere.isTrigger = true;
            }

            _probeSphere.radius = r;
            Vector3 dir; float dist;
            if (!Physics.ComputePenetration(_probeSphere, at, Quaternion.identity, c, c.transform.position, c.transform.rotation, out dir, out dist)) return false;
            return Mathf.Abs(dir.y) < 0.5f;
        }

        private static int _wallPushLogged;
        /// <summary>Lab / log counter: units pushed sideways out of a wall they overlapped (spawned against it, pushed into it).</summary>
        public static int PushedOutOfWalls;

        /// <summary>
        /// The body (a sphere / capsule of 0.8 of its radius above the step height, so the ground and steps never count) overlapping a wall of
        /// the map or scenery is moved sideways out of it (the penetration's horizontal part, up to 3 passes, at most 2 radii in all); faces
        /// the biped could stand on are its ground, not walls. Not into the void (the new place must be inside the active BSP).
        /// </summary>
        private bool PushOutOfWalls()
        {
            if (Def == null || Def.Flying || Seat != null || Puppet) return false;
            EnsureProbeCapsule();
            float r = _radius * 0.8f;
            float lo = _spherical ? _radius : _stepUp + r;
            float hi = _spherical ? _radius : Mathf.Max(lo, _height - r);
            _probeCapsule.radius = r;
            _probeCapsule.height = (hi - lo) + 2f * r;
            _probeCapsule.center = new Vector3(0f, (lo + hi) * 0.5f, 0f);
            Vector3 start = transform.position, feet = start;
            for (int pass = 0; pass < 3; pass++)
            {
                int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * lo, feet + Vector3.up * hi, r, Overlaps, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore);
                Vector3 push = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    Collider c = Overlaps[i];
                    if (c == null || IsOwn(c)) continue;
                    Vector3 dir; float dist;
                    if (!Physics.ComputePenetration(_probeCapsule, feet, Quaternion.identity, c, c.transform.position, c.transform.rotation, out dir, out dist) || dist < 1e-3f) continue;
                    if (Walkable(dir) || dir.y < -0.5f) continue;      // (its ground / a ceiling: not a wall)
                    Vector3 h = new Vector3(dir.x, 0f, dir.z);
                    float hm = h.magnitude;
                    if (hm < 0.3f) continue;
                    push += h / hm * ((dist + Skin) / hm);      // (the horizontal move that clears the same depth along the face's normal)
                }

                if (push.sqrMagnitude < 1e-6f) break;
                feet += push;
                if ((feet - start).sqrMagnitude > 4f * _radius * _radius) return false;      // (deeper than the body: not a wall it leans into)
            }

            if ((feet - start).sqrMagnitude < 1e-6f) return false;
            if (HaloWorld.Outside(feet + Vector3.up * 0.1f)) return false;
            transform.position = feet;
            PushedOutOfWalls++;
            if (_wallPushLogged++ < 6) HaloLog.Info("Characters: " + Def.Name + " overlapped a wall at " + start.ToString("F1") + ": pushed " + (feet - start).magnitude.ToString("0.00") + " m out of it");
            return true;
        }

        /// <summary>Four times a second: a unit found inside the floor goes back to its last ground (else it is settled); a grounded unit
        /// outside any solid records its last ground.</summary>
        private void CheckFloor()
        {
            float now = Time.time;
            if (now < _floorCheckAt) return;
            _floorCheckAt = now + 0.25f + (GetInstanceID() & 7) * 0.004f;
            if (Seat != null || _exiting || Def.Flying && Alive) return;
            // overlapping a wall (spawned against it, a carrier's forms born at its middle, a shove): out of it sideways first
            if (Alive && _grounded && PushOutOfWalls()) { _embedIgnore = false; WasRecovered = true; }
            Vector3 p = transform.position;
            if (_embedIgnore && (p - _embedIgnorePos).sqrMagnitude < 0.25f) return;      // (a spot Settle could not get it out of: left alone)
            _embedIgnore = false;
            if (Embedded(p))
            {
                Collider why = _embedWhy; bool whyKnee = _embedByKnee; float whyAt = _embedDist;      // (for the log: Settle tests again)
                bool back = _hasGroundPos && (_lastGroundPos - p).sqrMagnitude < 900f && !Embedded(_lastGroundPos);
                if (back)
                {
                    transform.position = _lastGroundPos;
                    _grounded = true;
                    _fallSpeed = 0f;
                    _airTime = 0f;
                    _velocity = Vector3.zero;
                    _platform = null;
                    Leaping = false;
                }
                else
                {
                    Settle();
                    if ((transform.position - p).sqrMagnitude < 0.0025f)
                    {
                        // nowhere better within 2 m (one-sided collision over the body, a tight crawl space): not put back over and over
                        _embedIgnore = true;
                        _embedIgnorePos = p;
                        if (_embedIgnoredLogged++ < 6) HaloLog.Info("Characters: " + Def.Name + " looks embedded at " + p.ToString("F1") + " (" + EmbedWhy(why, whyKnee, whyAt) + ") but has no free spot within 2 m: left there");
                        return;
                    }
                }
                WasRecovered = true;
                RecoveredFromFloor++;
                if (!_floorLogged) { _floorLogged = true; HaloLog.Info("Characters: " + Def.Name + " recovered from inside the floor at " + p.ToString("F1") + " -> " + transform.position.ToString("F1") + (back ? " (its last ground)" : " (settled)")); }
                return;
            }

            // (the last ground is kept inside the active BSP: a unit standing on something outside it is not put back there)
            if (_grounded && !HaloWorld.Outside(p + Vector3.up * 0.1f)) MarkGround();
        }

        /// <summary>
        /// Twice a second (alive or dead, not seated, not a puppet): feet and body both outside the active BSP (in its solid or the void) twice
        /// in a row, or 6 m under the world's bounds: back to the last ground when it stood there within 2 s, else killed (bodies: removed) -
        /// Halo's units cannot leave the BSP, and objects outside the map are gone.
        /// </summary>
        private void CheckOutside()
        {
            float now = Time.time;
            if (now < _outsideCheckAt) return;
            _outsideCheckAt = now + 0.5f;
            if (Puppet || Seat != null || _exiting || !HaloWorld.Ready || HaloCampaign.SwitchingBsp || Def.Flying && Alive) { _outsideStrikes = 0; return; }
            if (_grounded && _platform != null) { _outsideStrikes = 0; return; }      // (on a machine: its floor is not the BSP's)
            Vector3 feet = transform.position;
            Bounds b = HaloWorld.Bounds;
            bool below = b.size.y > 0.5f && feet.y < b.min.y - 6f;
            bool outside = !below && HaloWorld.Outside(feet + Vector3.up * 0.1f) && HaloWorld.Outside(feet + Vector3.up * Mathf.Max(0.3f, _height * 0.6f));
            if (!below && !outside) { _outsideStrikes = 0; return; }
            if (!below && ++_outsideStrikes < 2) return;
            _outsideStrikes = 0;
            if (!below && Alive && _grounded && !Embedded(feet))
            {
                // standing on real collision (another BSP's floor still built, scenery) and not inside it: a legit unit the cluster test
                // disagrees with - left alone (killing it would remove an encounter's actor); a unit in the void falls, and one inside a
                // floor is embedded (CheckFloor puts it back)
                if (!_outsideLogged) { _outsideLogged = true; HaloLog.Info("Characters: " + Def.Name + " stands outside the BSP at " + feet.ToString("F1") + " on walkable ground: left alone"); }
                return;
            }

            bool recent = _hasGroundPos && now - _lastGroundTime <= 2f && !HaloWorld.Outside(_lastGroundPos + Vector3.up * 0.1f);
            string why = below ? "fell under the world" : "is outside the BSP";
            if (Alive && recent)
            {
                transform.position = _lastGroundPos;
                _grounded = true;
                _fallSpeed = 0f;
                _airTime = 0f;
                _velocity = Vector3.zero;
                _platform = null;
                Leaping = false;
                WasRecovered = true;
                RecoveredFromOutside++;
                if (!_outsideLogged) { _outsideLogged = true; HaloLog.Info("Characters: " + Def.Name + " " + why + " at " + feet.ToString("F1") + ": put back on its last ground " + _lastGroundPos.ToString("F1")); }
                return;
            }

            RemovedOutside++;
            if (!_outsideLogged) { _outsideLogged = true; HaloLog.Info("Characters: " + Def.Name + " " + why + " at " + feet.ToString("F1") + ": " + (Alive ? "killed" : "body removed")); }
            if (Alive) Kill();
            else Destroy(gameObject);
        }

        private void Fly(float dt, Vector3 want)
        {
            float max = Def.MaxVelocity > 0.01f ? Def.MaxVelocity * HaloSpace.Unit : 6f;      // world units a second
            if (want.magnitude > max) want = want.normalized * max;
            float acc = Mathf.Max(2f, (Def.Acceleration > 0f ? Def.Acceleration : 0.1f) * 30f * HaloSpace.Unit);
            _velocity = Vector3.MoveTowards(_velocity, want, acc * dt);
            Vector3 step = _velocity * dt;
            float len = step.magnitude;
            if (len < 1e-5f) return;
            Vector3 c = transform.position + (Def.PillCenteredAtOrigin ? Vector3.zero : Vector3.up * _radius);
            RaycastHit h;
            int n = Physics.SphereCastNonAlloc(c, _radius * 0.8f, step / len, Hits, len, BlockMask, QueryTriggerInteraction.Ignore);
            float best = len;
            bool hit = false;
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].collider == null || IsOwn(Hits[i].collider) || Hits[i].distance <= 0f) continue;
                if (Hits[i].distance < best) { best = Hits[i].distance; hit = true; h = Hits[i]; }
            }

            transform.position += step / len * Mathf.Max(0f, best - 0.02f);
            if (hit) _velocity *= 0.5f;
        }

        private bool IsOwn(Collider c) { return c.transform.IsChildOf(transform); }

        // ------------------------------------------------------------------ the animation input / output

        private HaloUnitAnimInput AnimInput()
        {
            var i = new HaloUnitAnimInput();
            if (Seat != null) i.Seat = SeatLabel;
            else if (Crouching) i.Seat = "crouch";
            else i.Seat = string.IsNullOrEmpty(Stance) ? "stand" : Stance;
            i.WeaponLabel = WeaponLabel;
            if (Alive && Seat == null)
            {
                Vector3 v = _velocity / HaloSpace.Unit;          // world units a second
                Vector3 f = transform.forward, r = transform.right;
                i.MoveX = Vector3.Dot(v, f);
                i.MoveY = -Vector3.Dot(v, r);
                if (Def.Flying) { i.MoveX = 0f; i.MoveY = 0f; }
                i.TurnRate = _turnRate;
                i.Airborne = !Def.Flying && !_grounded && _airTime > 0.12f;
            }
            else i.Airborne = !Def.Flying && !_grounded && _airTime > 0.12f;

            i.Aiming = _aiming && Alive;
            i.Looking = _looking && Alive;
            i.AimYaw = _aimYaw; i.AimPitch = _aimPitch;
            i.LookYaw = _lookYaw; i.LookPitch = _lookPitch;
            _wasAirborne = i.Airborne;
            return i;
        }

        /// <summary>After the animation step: its root motion moves the body; its key frames fire the action (grenade release, melee hit) and footsteps.</summary>
        private void AfterAnimation()
        {
            HaloUnitAnimator a = Animator;
            // a co-op puppet only animates in place: the host's pose (root motion included) arrives by net, so its own root motion and swept
            // moves would fight that pose (a melee lunge snapped back every frame)
            if ((a.RootX != 0f || a.RootY != 0f || a.RootYaw != 0f) && Seat == null && !Puppet)
            {
                Vector3 local = new Vector3(-a.RootY, 0f, a.RootX) * HaloSpace.Unit;      // Halo x forward, y left -> Unity +z forward, +x right
                MoveHorizontal(transform.rotation * local);
                if (a.RootYaw != 0f) { _yaw -= a.RootYaw * Mathf.Rad2Deg; transform.rotation = Quaternion.Euler(0f, _yaw, 0f); }
            }

            if (a.ActionKey) ActionKeyFrame(a.Action);
            if (a.FootDown && Alive && _grounded && Seat == null)
            {
                try { HaloFootsteps.Step(transform.position, _velocity.magnitude > 2.5f ? HaloFootsteps.Run : HaloFootsteps.Walk, UnitTag, 0.8f); }   // (A: the unit's own footsteps tag - grunts, elites, flood)
                catch (System.Exception) { }
            }
        }
    }
}

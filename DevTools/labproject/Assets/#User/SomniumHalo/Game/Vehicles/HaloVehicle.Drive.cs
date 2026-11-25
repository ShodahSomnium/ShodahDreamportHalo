using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>What the driver asks of the vehicle this frame (filled by HaloVehicleInput from keys / mouse / controllers).</summary>
    public struct HaloVehicleControls
    {
        /// <summary>-1 back .. 1 forward.</summary>
        public float Throttle;
        /// <summary>-1 left .. 1 right (strafe: ghost).</summary>
        public float Strafe;
        /// <summary>-1 left .. 1 right: direct steering (VR stick mode, A / D on a monitor).</summary>
        public float Turn;
        /// <summary>Where the player looks / aims (world direction): Halo steers toward it and the gun aims along it.</summary>
        public Vector3 Aim;
        /// <summary>Halo's control: the vehicle steers toward <see cref="Aim"/> (monitor; VR "look" setting).</summary>
        public bool SteerToAim;
        public bool Fire, Fire2;
        public bool Brake;
    }

    public sealed partial class HaloVehicle
    {
        public HaloVehicleControls Controls;

        private float _hoverPower;     // 0..1: the ghost / banshee power up / down (unit powered seat times)
        private float _splatterNext;
        private static readonly Collider[] Near = new Collider[16];

        /// <summary>Unoccupied, settling onto the ground after a spawn: suspension only.</summary>
        private void SettleStep(float dt)
        {
            if (Body.isKinematic) return;
            Suspension(dt, false);
        }

        private void DriveStep(float dt)
        {
            if (Body.isKinematic) return;
            float powerTime = HaloLocal.Driving ? Mathf.Max(0.05f, Def.DriverPowerupTime) : Mathf.Max(0.05f, Def.DriverPowerdownTime);
            _hoverPower = Mathf.MoveTowards(_hoverPower, HasDriver ? 1f : 0f, dt / powerTime);
            switch (Def.Type)
            {
                case HaloVehicleDef.Kind.HumanJeep: Jeep(dt); break;
                case HaloVehicleDef.Kind.HumanTank: Tank(dt); break;
                case HaloVehicleDef.Kind.AlienScout: Scout(dt); break;
                case HaloVehicleDef.Kind.AlienFighter: Fighter(dt); break;
                case HaloVehicleDef.Kind.HumanBoat: Jeep(dt); break;
                case HaloVehicleDef.Kind.HumanPlane: Fighter(dt); break;
                case HaloVehicleDef.Kind.Turret: break;
            }
            if ((Def.VehicleFlags & HaloVehicleDef.VehicleCausesCollisionDamage) != 0) Splatter();
            if (Body.linearVelocity.sqrMagnitude > 0.5f) LastUsed = Time.time;
        }

        // ------------------------------------------------------------------ wheels (warthog, scorpion)

        /// <summary>Raycast suspension at every mass point the animation graph gives a suspension to; returns the grounded count.
        /// <paramref name="grip"/>: side friction at the contacts (off while settling = it may slide to rest).</summary>
        private int Suspension(float dt, bool grip)
        {
            int grounded = 0;
            Vector3 up = transform.up;
            int mask = HaloLayers.GroundMask | (1 << HaloLayers.Vehicle);
            float massShare = Body.mass / Mathf.Max(1, Wheels.Count);
            for (int i = 0; i < Wheels.Count; i++)
            {
                var w = Wheels[i];
                Vector3 origin = transform.TransformPoint(w.Local);
                RaycastHit hit;
                w.Grounded = false;
                if (CastGround(origin, -up, w.MaxDist + 0.05f, mask, out hit))
                {
                    w.Grounded = true; w.Distance = hit.distance; w.Normal = hit.normal; w.Ground = hit.collider;
                    grounded++;
                    Vector3 pv = Body.GetPointVelocity(origin);
                    float compression = w.MaxDist - hit.distance;
                    float force = w.Spring * compression - w.Damper * Vector3.Dot(pv, up);
                    float slam = -Vector3.Dot(pv, up);
                    if (slam > 3.5f && Def.SuspensionSound != null && Time.time > _suspNext) { _suspNext = Time.time + 0.3f; HaloAudio.PlayAt(Def.SuspensionSound, origin, 1f, Mathf.Clamp01((slam - 3.5f) / 6f), 1f); SfxCount++; }
                    if (hit.distance < w.MinDist) force += w.Spring * 6f * (w.MinDist - hit.distance); // bump stop (ours)
                    if (force < 0f) force = 0f;
                    Body.AddForceAtPosition(up * force, origin, ForceMode.Force);
                    w.Normal = hit.normal;
                    if (grip)
                    {
                        Vector3 fwd = WheelForward(w, hit.normal);
                        Vector3 side = Vector3.Cross(hit.normal, fwd);
                        float vLat = Vector3.Dot(pv, side);
                        float lat = -vLat * massShare / dt * 0.8f;
                        float limit = force * Mathf.Max(0.2f, w.GripSide) * 3.5f; // ours: Halo's friction scale as a tyre coefficient (arcade grip: ~1.6 g front, 2.3 g rear on the warthog)
                        lat = Mathf.Clamp(lat, -limit, limit);
                        Body.AddForceAtPosition(side * lat, origin, ForceMode.Force); // at the wheel centre: less roll than at the contact
                        // the tyres skid: the vehicle's material effects "tire slip" (dirt_skid ...) for the surface under the wheel
                        float slip = Mathf.Abs(vLat);
                        if (slip > MaxSlip) MaxSlip = slip;
                        if (slip > 3f && HaloImpacts.Play(Def.Tag, HaloImpacts.TireSlip, hit.point, HaloWorld.SurfaceMaterial(hit), Mathf.Clamp01((slip - 3f) / 9f))) Skids++;
                    }
                    else
                    {
                        // nobody drives (settling, a driver got out, a gunner only): Halo's empty vehicle holds where it stops (no
                        // driver = brakes on). The springs push along the hull's up, so a hull resting 2-3 deg nose-up/down gets a
                        // steady horizontal push (g * sin(tilt) ~ 0.4 m/s^2); the old viscous drag (2 m v) let it creep at ~0.2 m/s
                        // forever and never count as resting. Now static friction at each grounded wheel: cancel the wheel's ground-
                        // plane velocity (80 % a tick) up to the tyre's grip on its load - a falling / tumbling hull still slides and
                        // rolls (no load = no friction), a resting one stops.
                        Vector3 vPlane = Vector3.ProjectOnPlane(pv, hit.normal);
                        Vector3 stop = -vPlane * (massShare / dt * 0.8f);
                        float limit = force * Mathf.Max(0.2f, Mathf.Min(w.GripSide, w.GripForward)) * 3.5f; // ours: the drive path's tyre coefficient
                        if (stop.sqrMagnitude > limit * limit) stop = stop.normalized * limit;
                        Body.AddForceAtPosition(stop, origin, ForceMode.Force);
                    }
                }
            }
            return grounded;
        }

        private bool CastGround(Vector3 origin, Vector3 dir, float length, int mask, out RaycastHit best)
        {
            best = default(RaycastHit);
            bool found = false;
            int n = Physics.RaycastNonAlloc(origin, dir, Hits, length, mask, QueryTriggerInteraction.Ignore);
            float bestD = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null || h.rigidbody == Body || IsOwn(h.collider)) continue;
                if (h.distance < bestD) { bestD = h.distance; best = h; found = true; }
            }
            return found;
        }

        private static readonly RaycastHit[] Hits = new RaycastHit[8];

        private bool IsOwn(Collider c)
        {
            Transform t = c.transform;
            return t == transform || t.IsChildOf(transform);
        }

        private Vector3 WheelForward(Wheel w, Vector3 normal)
        {
            Vector3 f = transform.forward;
            if (w.Steers && Steer != 0f) f = Quaternion.AngleAxis(-Steer * Def.MaxLeftTurn, transform.up) * f;
            return Vector3.ProjectOnPlane(f, normal).normalized;
        }

        private void Jeep(float dt)
        {
            var c = Controls;
            float maxTurn = Mathf.Max(1f, Def.MaxLeftTurn);
            Vector3 fwd = transform.forward;
            float vF = Vector3.Dot(Body.linearVelocity, fwd);
            // steering: Halo turns the wheels toward where the player looks (monitor), or the stick turns them (VR default)
            float steerTarget;
            if (c.SteerToAim && c.Aim.sqrMagnitude > 0.01f && (Mathf.Abs(c.Throttle) > 0.05f || Mathf.Abs(vF) > 1f))
            {
                float angle = SignedYaw(fwd, c.Aim);                // + = aim is to the right
                if (vF < -0.5f && c.Throttle <= 0f) angle = -angle;  // reversing: Halo steers the tail toward the view
                steerTarget = Mathf.Clamp(-angle / maxTurn, -1f, 1f);
            }
            else steerTarget = -c.Turn;
            float rate = (Def.TurnRate > 0f ? Def.TurnRate : 180f) / maxTurn;
            Steer = Mathf.MoveTowards(Steer, steerTarget, rate * dt);

            int grounded = Suspension(dt, true);
            int poweredGrounded = 0, powered = 0;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < Wheels.Count; i++)
                if (Wheels[i].Powered) { powered++; if (Wheels[i].Grounded) { poweredGrounded++; normal += Wheels[i].Normal; } }
            if (poweredGrounded == 0) return;
            normal.Normalize();
            float target = c.Throttle >= 0f ? c.Throttle * PerTick(Def.MaxForward) : c.Throttle * PerTick(Def.MaxReverse);
            float accel = Accel(vF, target, c.Throttle);
            float traction = (float)poweredGrounded / Mathf.Max(1, powered);
            Vector3 driveDir = Vector3.ProjectOnPlane(fwd, normal).normalized;
            Body.AddForce(driveDir * (Body.mass * accel * traction), ForceMode.Force);
        }

        /// <summary>Acceleration (m/s^2, signed along forward) that takes the forward speed toward the target the Halo way: speed
        /// acceleration toward it, speed deceleration when braking against the motion, a quarter of that coasting (ours).</summary>
        private float Accel(float v, float target, float throttle)
        {
            float up = PerTick2(Def.SpeedAcceleration), down = PerTick2(Def.SpeedDeceleration);
            if (down <= 0f) down = up;
            float dv = target - v;
            float limit;
            if (Mathf.Abs(throttle) < 0.05f) limit = down * 0.25f;
            else if (Mathf.Sign(target) != Mathf.Sign(v) && Mathf.Abs(v) > 0.5f) limit = down;
            else limit = Mathf.Abs(target) > Mathf.Abs(v) ? up : down * 0.5f;
            return Mathf.Clamp(dv / Time.fixedDeltaTime, -limit, limit);
        }

        private static float SignedYaw(Vector3 from, Vector3 to)
        {
            Vector3 a = Vector3.ProjectOnPlane(from, Vector3.up), b = Vector3.ProjectOnPlane(to, Vector3.up);
            if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.SignedAngle(a, b, Vector3.up);
        }

        private void Tank(float dt)
        {
            var c = Controls;
            Vector3 fwd = transform.forward;
            float vF = Vector3.Dot(Body.linearVelocity, fwd);
            int grounded = Suspension(dt, true);
            // turning: the hull turns toward the view (Halo) or by the stick, on its treads (ours: 50 deg/s, the driver seat's yaw rate)
            float rate = (Seats.Count > 0 && Seats[0].Def.YawRate > 0f ? Seats[0].Def.YawRate : 45f) * Mathf.Deg2Rad;
            float wantYawRate;
            if (c.SteerToAim && c.Aim.sqrMagnitude > 0.01f && Mathf.Abs(c.Throttle) > 0.05f)
            {
                float angle = SignedYaw(fwd, c.Aim);
                if (c.Throttle < 0f) angle = -SignedYaw(-fwd, c.Aim) ;
                wantYawRate = Mathf.Clamp(angle * Mathf.Deg2Rad * 3f, -rate, rate);
            }
            else wantYawRate = c.Turn * rate;
            Steer = -wantYawRate / rate;
            if (grounded == 0) return;
            float yawRate = Vector3.Dot(Body.angularVelocity, transform.up);
            Vector3 inertia = Body.inertiaTensor;
            float torque = (wantYawRate - yawRate) / dt * inertia.y * 0.5f;
            Body.AddRelativeTorque(new Vector3(0f, torque, 0f), ForceMode.Force);
            float target = c.Throttle >= 0f ? c.Throttle * PerTick(Def.MaxForward) : c.Throttle * PerTick(Def.MaxReverse);
            float accel = Accel(vF, target, c.Throttle);
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < Wheels.Count; i++) if (Wheels[i].Grounded) normal += Wheels[i].Normal;
            normal.Normalize();
            Body.AddForce(Vector3.ProjectOnPlane(fwd, normal).normalized * (Body.mass * accel * grounded / Mathf.Max(1, Wheels.Count)), ForceMode.Force);
        }

        // ------------------------------------------------------------------ antigrav (ghost, banshee)

        /// <summary>Halo's antigrav mass points: below their height above the ground they lift with strength x their weight,
        /// falling off linearly to the height (equilibrium at height (1 - 1/strength)); damped (ours: 0.6 of critical).</summary>
        private int Antigrav(float power)
        {
            if (power <= 0f) return 0;
            int n = 0;
            Vector3 up = transform.up;
            float g = Physics.gravity.magnitude;
            int mask = HaloLayers.GroundMask | (1 << HaloLayers.Vehicle);
            float scale = Body.mass / Mathf.Max(1f, TotalHoverMass());
            for (int i = 0; i < Hovers.Count; i++)
            {
                var h = Hovers[i];
                Vector3 origin = transform.TransformPoint(h.Local);
                RaycastHit hit;
                if (!CastGround(origin, Vector3.down, h.Height, mask, out hit)) continue;
                n++;
                float m = h.Mass * scale;
                float lift = h.Strength * m * g * (1f - hit.distance / h.Height);
                float k = h.Strength * m * g / h.Height;
                float damp = 2f * Mathf.Sqrt(k * m) * 0.6f;
                float vUp = Vector3.Dot(Body.GetPointVelocity(origin), Vector3.up);
                float f = Mathf.Max(0f, lift - damp * vUp) * power;
                Body.AddForceAtPosition(Vector3.up * f, origin, ForceMode.Force);
            }
            return n;
        }

        private float TotalHoverMass()
        {
            float m = 0f;
            for (int i = 0; i < Hovers.Count; i++) m += Hovers[i].Mass;
            return m;
        }

        /// <summary>Turns the body toward a heading (and optionally pitch) with an angular-velocity servo; keeps it upright.</summary>
        private void Orient(float wantYawDeg, float wantPitchDeg, bool pitch, float rateDeg, float uprightGain, float dt)
        {
            Vector3 fwd = transform.forward;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float dy = Mathf.DeltaAngle(yaw, wantYawDeg);
            float wantYawRate = Mathf.Clamp(dy * 4f, -rateDeg, rateDeg) * Mathf.Deg2Rad;
            Vector3 ang = Body.angularVelocity;
            float yawRate = ang.y;
            Vector3 torque = Vector3.zero;
            Vector3 inertia = Body.inertiaTensor;
            float iy = (inertia.x + inertia.y + inertia.z) / 3f;
            torque.y = (wantYawRate - yawRate) / dt * iy * 0.6f;
            // upright (and pitch toward the aim for the banshee): servo the up axis toward the wanted up
            Quaternion want = Quaternion.Euler(pitch ? -wantPitchDeg : 0f, wantYawDeg, 0f);
            Vector3 wantUp = want * Vector3.up;
            Vector3 err = Vector3.Cross(transform.up, wantUp);
            if (pitch)
            {
                Vector3 wantFwd = want * Vector3.forward;
                err += Vector3.Cross(transform.forward, wantFwd) * 0.5f;
                err.y = 0f;
            }
            Vector3 tilt = err * uprightGain - new Vector3(ang.x, 0f, ang.z) * (uprightGain * 0.35f);
            torque += tilt * iy;
            Body.AddTorque(torque, ForceMode.Force);
        }

        private void Scout(float dt)
        {
            var c = Controls;
            int over = Antigrav(_hoverPower);
            Vector3 fwd = transform.forward;
            float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float rate = Seats.Count > 0 && Seats[0].Def.YawRate > 0f ? Seats[0].Def.YawRate : 80f;
            float wantYaw = yaw;
            if (c.SteerToAim && c.Aim.sqrMagnitude > 0.01f) wantYaw = Mathf.Atan2(c.Aim.x, c.Aim.z) * Mathf.Rad2Deg;
            else wantYaw = yaw + c.Turn * rate * 0.25f;
            Orient(wantYaw, 0f, false, rate, 30f, dt);
            if (over == 0) return; // in the air: no grip on anything
            Vector3 flatF = Vector3.ProjectOnPlane(fwd, Vector3.up).normalized;
            Vector3 flatR = Vector3.Cross(Vector3.up, flatF);
            float vmax = PerTick(Def.MaxForward);
            float back = Def.MaxReverse > 0f ? PerTick(Def.MaxReverse) : vmax * 0.5f; // ours: the tag's 0 reverse, Halo's ghost backs off slowly
            Vector3 want = flatF * (c.Throttle >= 0f ? c.Throttle * vmax : c.Throttle * back) + flatR * (c.Strafe * vmax * 0.6f); // strafe: ours
            Vector3 v = Vector3.ProjectOnPlane(Body.linearVelocity, Vector3.up);
            Vector3 dv = want - v;
            float a = PerTick2(Def.SpeedAcceleration);
            if (want.sqrMagnitude < 0.01f) a *= 0.6f;
            Vector3 acc = Vector3.ClampMagnitude(dv / dt, a);
            Body.AddForce(acc * Body.mass, ForceMode.Force);
        }

        private void Fighter(float dt)
        {
            var c = Controls;
            Antigrav(_hoverPower);
            Vector3 aim = c.Aim.sqrMagnitude > 0.01f ? c.Aim.normalized : transform.forward;
            float rate = Seats.Count > 0 && Seats[0].Def.YawRate > 0f ? Seats[0].Def.YawRate : 80f;
            float wantYaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
            float wantPitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(aim.y, -1f, 1f)) * Mathf.Rad2Deg, -60f, 60f); // ours
            if (!c.SteerToAim)
            {
                Vector3 f = transform.forward;
                wantYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + c.Turn * rate * 0.25f;
            }
            if (_hoverPower > 0.5f)
            {
                // flying: weightless while powered (Halo's banshee holds its height), steering toward the view
                Body.AddForce(-Physics.gravity * Body.mass * _hoverPower, ForceMode.Force);
                Orient(wantYaw, wantPitch, true, rate, 20f, dt);
                float vmax = PerTick(Def.MaxForward), back = PerTick(Def.MaxReverse);
                Vector3 want = transform.forward * (c.Throttle >= 0f ? c.Throttle * vmax : c.Throttle * back) + transform.right * (c.Strafe * vmax * 0.3f);
                Vector3 dv = want - Body.linearVelocity;
                float a = Mathf.Abs(c.Throttle) > 0.05f ? PerTick2(Def.SpeedAcceleration) : PerTick2(Def.SpeedDeceleration) * 0.3f;
                Body.AddForce(Vector3.ClampMagnitude(dv / dt, a) * Body.mass, ForceMode.Force);
            }
            else Orient(wantYaw, 0f, false, rate, 10f, dt);
        }

        // ------------------------------------------------------------------ splatter

        /// <summary>Runs over players: Halo's "causes collision damage" - instant above a speed (ours: 8 m/s), scaled below it.</summary>
        private void Splatter()
        {
            if (Time.time < _splatterNext) return;
            float speed = Body.linearVelocity.magnitude;
            if (speed < 3f) return;
            Vector3 v = Body.linearVelocity;
            Bounds b = ShapeBounds();
            Vector3 centre = b.center + v * 0.06f;
            int n = Physics.OverlapBoxNonAlloc(centre, b.extents + new Vector3(0.2f, 0.1f, 0.2f), Near, transform.rotation, 1 << HaloLayers.Hitbox, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var col = Near[i];
                if (col == null || NearOccupiedSeat(col.transform.position)) continue;
                var target = col.GetComponentInParent<IHaloDamageable>();
                if (target == null) continue;
                Vector3 to = col.transform.position - transform.position;
                float closing = Vector3.Dot(v, to.normalized);
                if (closing < 2.5f) continue;
                var hit = new HaloHit
                {
                    Amount = closing >= 8f ? 1e6f : (closing - 2.5f) * 18f, // ours
                    Instant = closing >= 8f, SourcePlayer = AiDriven ? -2 : HaloLocal.NetId, Cause = Def.Tag, Point = col.transform.position, Direction = v.normalized,
                };
                HaloLog.Info("Vehicles: " + name + " hit a player at " + closing.ToString("0.0") + " m/s" + (hit.Instant ? " (splatter)" : ""));
                HaloGame.Guard("splatter", () => target.TakeHit(hit));
                _splatterNext = Time.time + 0.5f;
                break;
            }
        }

        private bool NearOccupiedSeat(Vector3 p)
        {
            for (int i = 0; i < Seats.Count; i++)
                if (Seats[i].Occupant >= 0 && Seats[i].Marker != null && (Seats[i].Marker.position - p).sqrMagnitude < 1.6f * 1.6f) return true;
            return false;
        }

        public Bounds ShapeBounds()
        {
            // in the root frame: from the shapes' centres and radii (cached once)
            if (_boundsKnown) return new Bounds(transform.TransformPoint(_localBounds.center), _localBounds.size);
            var lb = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var c in Shapes)
            {
                var s = c as SphereCollider;
                Bounds one = s != null ? new Bounds(s.center, Vector3.one * s.radius * 2f) : new Bounds(transform.InverseTransformPoint(c.bounds.center), c.bounds.size);
                if (first) { lb = one; first = false; } else lb.Encapsulate(one);
            }
            foreach (var w in Wheels) lb.Encapsulate(new Bounds(w.Local, Vector3.one * w.Radius * 2f));
            _localBounds = lb; _boundsKnown = true;
            return new Bounds(transform.TransformPoint(lb.center), lb.size);
        }

        private Bounds _localBounds;

        private float _suspNext, _crashNext;
        /// <summary>Vehicle sounds started by V (suspension, crash) - the lab's count.</summary>
        public static int SfxCount;
        /// <summary>The largest tyre side-slip seen (m/s; lab).</summary>
        public static float MaxSlip;
        /// <summary>Tyre-slip effects started (material effects 9: dirt_skid on dirt / sand, the slip effect on every surface; lab).</summary>
        public static int Skids;
        /// <summary>vehiclesvehicle.foot effect 10: the body scraping / hitting a surface (slip + vehicle_impact on stone / wood / metal).</summary>
        public const int BodyImpact = 10;

        /// <summary>
        /// A hard hit (a dynamic vehicle meeting the world, another vehicle or a wall faster than 4 m/s along the contact normal): the vehicle's own
        /// crash sound (vehi +960) scaled by the impact (full at 16 m/s, ours) and its material effects "impact" for the surface it hit (HaloImpacts).
        /// </summary>
        private void OnCollisionEnter(Collision c)
        {
            if (Body == null || Body.isKinematic || Time.time < _crashNext) return;
            float speed = c.relativeVelocity.magnitude;
            if (c.contactCount > 0) speed = Mathf.Abs(Vector3.Dot(c.relativeVelocity, c.GetContact(0).normal));
            if (speed < 4f) return;
            _crashNext = Time.time + 0.25f;
            Vector3 at = c.contactCount > 0 ? c.GetContact(0).point : transform.position;
            float strength = Mathf.Clamp01((speed - 4f) / 12f);
            if (Def.CrashSound != null) { HaloAudio.PlayAt(Def.CrashSound, at, 1f, strength, 1f); SfxCount++; }
            HaloImpacts.Play(Def.Tag, BodyImpact, at, -1, strength);
        }
        private bool _boundsKnown;
    }
}

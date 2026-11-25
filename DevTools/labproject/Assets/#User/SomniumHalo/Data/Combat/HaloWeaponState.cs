using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The firing mechanics of one weapon, from its tag: rate of fire (accelerating from the minimum to the maximum while the trigger is
    /// held), rounds per shot from the magazine, heat (overheats at the threshold, fires again below the recovery threshold, cools at the
    /// loss rate), the battery ("age" per round; a plasma weapon at full age cannot fire), the error angle (grows while firing, decays),
    /// the plasma pistol's charge (hold: charge, let go when charged: the charged trigger's projectile). It knows nothing about hands,
    /// models or projectiles: <see cref="Step"/> reports what happened this frame. Unity-free, no allocation per frame. Used by the
    /// player's guns (Game/Combat) and can be used by vehicle weapons (Vehicles module).
    /// </summary>
    public sealed class HaloWeaponState
    {
        public struct Shot
        {
            public int Trigger;          // trigger index whose projectile flies
            public float ErrorAngle;     // radians (cone half-angle for this shot)
            public bool Charged;         // the charged shot of a charging trigger
            public int Count;            // the shot's number in this burst (1 = first) for the firing effect choice
        }

        public readonly HaloWeaponDef Def;
        public int Loaded, Reserve;              // magazine 0 (Halo weapons in multiplayer use one magazine)
        public float Heat, Age, Charge;          // 0..1
        public bool Overheated, Charging, Charged;
        public float Error;                      // 0..1 of the trigger's error angle range
        public float RateU;                      // 0..1 of the rate of fire range
        public int BurstShots;                   // shots since the trigger went down
        /// <summary>This Step: a dry fire click / it just overheated / it just cooled down.</summary>
        public bool JustDry, JustOverheated, JustRecovered;

        private float _cooldown;                 // seconds until the next round may leave
        private bool _wasDown;
        private float _heldFor;
        private bool _firedThisStep;

        /// <summary>Seconds a press must be held before a charging trigger starts to charge (ours: a tap fires a normal shot).</summary>
        public static float ChargeAfter = 0.25f;
        /// <summary>The fastest a weapon with no rate of fire in its tag (once per pull) fires again (ours).</summary>
        public static float SemiAutoMinimumInterval = 0.1f;

        public HaloWeaponState(HaloWeaponDef def)
        {
            Def = def;
            var m = def.PrimaryMagazine;
            if (m != null && m.RoundsLoadedMaximum > 0)
            {
                Loaded = m.RoundsLoadedMaximum;
                Reserve = Math.Max(0, Math.Min(m.RoundsReservedMaximum > 0 ? m.RoundsReservedMaximum : int.MaxValue, m.RoundsTotalInitial - Loaded));
            }
        }

        public int LoadedMaximum => Def.PrimaryMagazine != null ? Def.PrimaryMagazine.RoundsLoadedMaximum : 0;
        public int ReserveMaximum => Def.PrimaryMagazine != null ? Def.PrimaryMagazine.RoundsReservedMaximum : 0;
        public bool UsesAmmo { get { var t = Def.PrimaryTrigger; return t != null && t.RoundsPerShot > 0 && Def.HasMagazine; } }
        public float Battery => 1f - Age;
        public bool Empty => UsesAmmo && Loaded <= 0;
        public bool OutOfEverything => UsesAmmo ? Loaded <= 0 && Reserve <= 0 : Def.UsesBattery && Age >= 1f;

        /// <summary>Cannot fire right now (overheated, flat battery, empty magazine).</summary>
        public bool Blocked(int trigger)
        {
            if (Overheated) return true;
            if (Def.UsesBattery && Age >= 1f) return true;
            var t = trigger >= 0 && trigger < Def.Triggers.Count ? Def.Triggers[trigger] : null;
            if (t == null || t.Projectile == null) return true;
            if (t.RoundsPerShot > 0 && Def.HasMagazine)
            {
                int need = Math.Max(t.RoundsPerShot, t.MinimumRoundsLoaded);
                if (Loaded < need && !(t.CanFireWithPartialAmmo && Loaded > 0)) return true;
            }

            return false;
        }

        /// <summary>
        /// One frame of the trigger. Writes up to <paramref name="shots"/>.Length shots and returns how many; <paramref name="zoomed"/> for the
        /// triggers that only spread when unzoomed.
        /// </summary>
        public int Step(float dt, bool down, bool zoomed, Shot[] shots)
        {
            JustDry = JustOverheated = JustRecovered = false;
            _firedThisStep = false;
            int n = 0;
            var t = Def.PrimaryTrigger;
            bool pressed = down && !_wasDown, released = !down && _wasDown;
            _wasDown = down;
            if (down) _heldFor += dt; else { if (!released) _heldFor = 0f; BurstShots = 0; }
            _cooldown -= dt;
            if (_cooldown < (down ? -0.1f : 0f)) _cooldown = down ? -0.1f : 0f;     // (a held trigger catches up within a frame or two, never a burst after a pause)

            if (t != null && t.Projectile != null)
            {
                bool charging = t.ChargingTime > 0f && Def.Triggers.Count > 1;
                if (charging)
                {
                    if (pressed && !Blocked(0) && _cooldown <= 0f) n = Fire(0, false, zoomed, shots, n);
                    else if (pressed && Blocked(0)) JustDry = true;
                    if (down && _heldFor >= ChargeAfter && !Overheated && !Blocked(1))
                    {
                        Charging = true;
                        Charge = Math.Min(1f, Charge + dt / t.ChargingTime);
                        if (Charge >= 1f) Charged = true;
                    }

                    if (released)
                    {
                        if (Charged && !Blocked(1) && n < shots.Length) n = Fire(1, true, zoomed, shots, n);
                        Charging = Charged = false; Charge = 0f;
                    }
                }
                else if (down)
                {
                    bool once = t.DoesNotRepeat || (t.RateMax <= 0f && t.RateMin <= 0f);
                    if (pressed && Blocked(0)) JustDry = true;
                    // a frame may let several rounds go at high rates (15 a second at 30 fps: one every other frame)
                    while (n < shots.Length && _cooldown <= 0f && !Blocked(0) && (!once || BurstShots == 0))
                    {
                        n = Fire(0, false, zoomed, shots, n);
                        if (once) break;
                    }
                }
            }

            // rate of fire, error, heat between shots
            if (t != null)
            {
                bool firing = down && t.Projectile != null && !Blocked(0) && t.ChargingTime <= 0f;
                if (firing)
                {
                    if (t.AccelerationTime > 0f) RateU = Math.Min(1f, RateU + dt / t.AccelerationTime); else RateU = 1f;
                }
                else if (!down)
                {
                    if (t.DecelerationTime > 0f) RateU = Math.Max(0f, RateU - dt / t.DecelerationTime); else RateU = 0f;
                }

                if (firing || _firedThisStep) Error = Math.Min(1f, Error + dt / (t.ErrorAccelerationTime > 0f ? t.ErrorAccelerationTime : 0.5f));
                else Error = Math.Max(0f, Error - dt / (t.ErrorDecelerationTime > 0f ? t.ErrorDecelerationTime : 0.3f));
            }

            if ((!down || Overheated) && !_firedThisStep && Heat > 0f)
            {
                float loss = Def.HeatLossRate * (1f - Def.AgeHeatRecoveryPenalty * Age);
                if (loss <= 0f) loss = 0.25f;
                Heat = Math.Max(0f, Heat - loss * dt);
            }

            if (Overheated && Heat <= Def.HeatRecoveryThreshold) { Overheated = false; JustRecovered = true; }
            return n;
        }

        private int Fire(int trigger, bool charged, bool zoomed, Shot[] shots, int n)
        {
            var t = Def.Triggers[trigger];
            var main = Def.PrimaryTrigger;
            // ammunition
            if (t.RoundsPerShot > 0 && Def.HasMagazine) Loaded = Math.Max(0, Loaded - t.RoundsPerShot);
            Heat = Math.Min(1f, Heat + t.HeatPerRound);
            Age = Math.Min(1f, Age + t.AgePerRound);
            if (Def.OverheatedThreshold > 0f && Heat >= Def.OverheatedThreshold && !Overheated) { Overheated = true; JustOverheated = true; Charging = Charged = false; Charge = 0f; }
            // rate
            float rate = main.RateMin + (main.RateMax - main.RateMin) * RateU;
            rate *= 1f - Def.AgeRateOfFirePenalty * Age;
            float interval = rate > 0f ? 1f / rate : SemiAutoMinimumInterval;
            _cooldown += interval;
            BurstShots++;
            _firedThisStep = true;
            // error
            float e = t.UseErrorWhenUnzoomed && zoomed ? 0f : Error;
            float angle = t.MinimumError + t.ErrorAngleMin + (t.ErrorAngleMax - t.ErrorAngleMin) * e;
            shots[n] = new Shot { Trigger = trigger, ErrorAngle = angle, Charged = charged, Count = BurstShots };
            return n + 1;
        }

        // ------------------------------------------------------------------ magazines (the immersive reload moves rounds through these)

        /// <summary>The magazine comes out: its rounds go back to the reserve unless the weapon wastes them. Returns the rounds it held.</summary>
        public int EjectMagazine()
        {
            int r = Loaded;
            Loaded = 0;
            var m = Def.PrimaryMagazine;
            if (m == null || !m.WastesRoundsWhenReloaded) Reserve += r;
            return r;
        }

        /// <summary>Rounds for one reload step taken off the reserve (a magazine; one shell for a shotgun; the rocket pair).</summary>
        public int TakeReload()
        {
            var m = Def.PrimaryMagazine;
            if (m == null) return 0;
            int step = m.RoundsReloaded > 0 ? m.RoundsReloaded : m.RoundsLoadedMaximum;
            int room = m.RoundsLoadedMaximum - Loaded;
            int n = Math.Min(Math.Min(step, room > 0 ? room : step), Reserve);
            Reserve -= n;
            return n;
        }

        public void Insert(int rounds)
        {
            int max = LoadedMaximum;
            Loaded = Math.Min(max > 0 ? max : int.MaxValue, Loaded + rounds);
        }

        /// <summary>Ammunition picked up (walking over a weapon of the same kind, an ammo box): capped at the reserve maximum. Returns what was taken.</summary>
        public int AddReserve(int rounds)
        {
            int max = ReserveMaximum > 0 ? ReserveMaximum : int.MaxValue;
            int take = Math.Max(0, Math.Min(rounds, max - Reserve));
            Reserve += take;
            return take;
        }

        /// <summary>A plain reload in the weapon's own time (monitor play, vehicles): fills the magazine from the reserve.</summary>
        public void ReloadNow()
        {
            int max = LoadedMaximum;
            int n = Math.Min(max - Loaded, Reserve);
            if (n <= 0) return;
            Loaded += n; Reserve -= n;
        }

        public bool CanReload => UsesAmmo && Reserve > 0 && Loaded < LoadedMaximum;
    }
}

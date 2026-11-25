using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The weapon in the unit's hand: the weapon's gbxmodel on the hand marker its animation class names ("right hand", "right hand elite",
    /// "left hand jackal"), Halo's own weapon part animations on it (Desk's HaloWeaponAnim: fire, reload), firing while <see cref="Firing"/>
    /// at the actor variant's rate of fire (the weapon's trigger rate without one) through Combat's projectiles (HaloCombat.FireProjectile:
    /// damage, impacts, material responses) from the "primary trigger" marker toward <see cref="AimPoint"/> within the variant's projectile
    /// error, the trigger's firing effect at the muzzle, the magazine (reload-1 when empty), melee (the unit's / weapon's melee damage at the
    /// animation's key frame), grenades thrown at the throw-grenade key frame (the globals' grenade of the variant's grenade type, along a lob
    /// to the aim point), and the weapon dropped where the unit dies (Combat's pickups).
    /// </summary>
    public partial class HaloUnit
    {
        /// <summary>The shooter id of AI shots (Combat / Net: -2 = "AI"; players are 0 and up, -1 the world).</summary>
        public const int AiShooter = -2;

        /// <summary>The weapon in hand (weap), null = unarmed.</summary>
        public HaloTag Weapon { get; private set; }
        /// <summary>The weapon's label ("ar", "pr" ...) - picks the animation class.</summary>
        public string WeaponLabel { get; private set; }
        /// <summary>Where shots leave the weapon (the weapon model's "primary trigger" marker; the unit's own for built-in weapons). Barrel = its +X (Unity right).</summary>
        public Transform Muzzle { get; private set; }
        /// <summary>Rounds in the magazine (-1 = none: heat / battery weapons fire on).</summary>
        public int Rounds = -1;
        public int Grenades;
        public int GrenadeType;
        /// <summary>Shots fired since spawn (labs, AI statistics).</summary>
        public int ShotsFired { get; private set; }

        /// <summary>Set by the AI each tick: multipliers on the fire rate (the actor variant's rate of fire) and on the projectile error (the
        /// variant's projectile error angle): the firing pattern modifiers (new target / moving / berserk) times the difficulty's. 1 = the tag's.</summary>
        public float AiRateScale = 1f, AiErrorScale = 1f;
        /// <summary>Seconds between two special fires at least (the jackal's overcharge: the variant's special fire delay, set at the spawn;
        /// the AI may scale it by the difficulty's special fire delay); 0 = no limit.</summary>
        public float SpecialFireMinInterval;
        private float _lastSpecial = -1000f;

        private HaloModelBuilder.Built _weaponBuilt;
        private HaloWeaponAnim _weaponAnim;
        private HaloWeaponDef _wdef;
        private float _nextShot;
        private int _burstShot;
        private bool _wasFiring;

        private void InitWeapon()
        {
            GrenadeType = Def.VariantGrenadeType >= 0 && Def.Variant != null ? Def.VariantGrenadeType : Def.GrenadeType;
            Grenades = Def.Variant != null ? Rnd(Def.GrenadeCountMin, Def.GrenadeCountMax) : Mathf.Max(0, Def.GrenadeCount);
            SpecialFireMinInterval = Mathf.Max(0f, Def.SpecialFireDelay);
            Give(Def.Weapon);
        }

        private int Rnd(int a, int b) { if (b < a) b = a; return a + _rng.Next(b - a + 1); }

        /// <summary>Puts a weapon in the unit's hand (null = unarmed): its model on the class's hand marker, a full magazine.</summary>
        public void Give(HaloTag weapon)
        {
            ReleaseWeapon();
            Weapon = weapon != null && weapon.Is("weap") ? weapon : null;
            WeaponLabel = Weapon != null ? Weapon.Root.Str32(780) : null;
            var cat = HaloCombat.Catalog;
            _wdef = Weapon != null && cat != null ? cat.Weapon(Weapon) : null;
            Rounds = _wdef != null && _wdef.HasMagazine ? _wdef.PrimaryMagazine.RoundsLoadedMaximum : -1;
            if (Weapon != null)
            {
                HaloTag model = Weapon.Root.Ref(40);
                if (model != null)
                {
                    var holder = new GameObject("weapon");
                    holder.transform.SetParent(transform, false);
                    _weaponBuilt = HaloModelBuilder.Build(model, holder.transform, new HaloModelBuilder.Options());
                    if (_weaponBuilt == null) Destroy(holder);
                }
            }

            if (_weaponBuilt != null) AttachWeaponModel();
            else Muzzle = Marker("primary trigger");      // built-in weapons (sentinel beam, hunter cannon): the unit's own marker
            if (_weaponBuilt != null && Weapon != null)
            {
                try { _weaponAnim = HaloWeaponAnim.Attach(_weaponBuilt, Weapon); } catch (System.Exception) { _weaponAnim = null; }
            }

            _heat = 0f; _overheated = false; _charging = false;
            // the weapon tag's attachments, as the player's gun runs them (Combat's HaloGunFx): every weapon attachment's primary scale comes from
            // an object function fed by a weapon input, so none is started unscaled - "ready" ones on now (the energy sword's glow), "primary
            // charged" ones with the overcharge (TickCharge), the vent with the overheat (FireOnce / TickHeat); the ready effect when given
            // after the spawn
            if (_wdef != null && _weaponBuilt != null && _weaponBuilt.Root != null) HaloGunFx.Ready(_weaponBuilt.Root.transform, _wdef, true);
            if (_frames > 0) PlayReady();
            if (_camoHidden) SetCamoHidden(true);      // (a weapon given to a camouflaged unit is hidden too)
        }

        /// <summary>The weapon model onto the hand marker the animation class names (the class is known after the first animation step).</summary>
        private void AttachWeaponModel()
        {
            if (_weaponBuilt == null || _weaponBuilt.Root == null) return;
            string hand = null;
            HaloAnimGraph g = Animator != null ? Animator.Graph : null;
            if (g != null)
            {
                HaloAnimGraph.Seat seat = g.FindSeat("stand") ?? (g.Seats.Count > 0 ? g.Seats[0] : null);
                HaloAnimGraph.WeaponType t;
                HaloAnimGraph.WeaponClass c = seat != null ? g.ClassFor(seat, WeaponLabel, out t) : null;
                if (c != null && !string.IsNullOrEmpty(c.HandMarker)) hand = c.HandMarker;
            }

            Transform m = (hand != null ? Marker(hand) : null) ?? Marker("right hand") ?? Marker("right hand elite") ?? (_nodes != null ? Built.Node("bip01 r hand") : null);
            Transform holder = _weaponBuilt.Root.transform.parent;
            holder.SetParent(m != null ? m : transform, false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;
            Muzzle = _weaponBuilt.Marker("primary trigger") ?? _weaponBuilt.Root.transform;
        }

        private void ReleaseWeapon()
        {
            HaloGunFx.Forget(WeaponRoot);      // the weapon's vent / charge glow / ready glow go with it
            if (_weaponBuilt != null && _weaponBuilt.Root != null)
            {
                Transform holder = _weaponBuilt.Root.transform.parent;
                Destroy(holder != null && holder != transform ? holder.gameObject : _weaponBuilt.Root);
            }

            _weaponBuilt = null;
            _weaponAnim = null;
            Muzzle = null;
        }

        // ------------------------------------------------------------------ every frame

        private void TickWeapon(float dt)
        {
            HaloUnitAnimator a = Animator;
            HaloUnitAnimator.ActionKind act = a != null ? a.Action : HaloUnitAnimator.ActionKind.None;
            bool busy = act == HaloUnitAnimator.ActionKind.Reload || act == HaloUnitAnimator.ActionKind.Melee || act == HaloUnitAnimator.ActionKind.Throw || act == HaloUnitAnimator.ActionKind.Dive;
            if (ThrowGrenade)
            {
                ThrowGrenade = false;
                if (!busy && Grenades > 0 && a != null && a.Throw()) busy = true;
            }

            if (Melee)
            {
                Melee = false;
                if (!busy && !MeleeInhibited && !Feigning && !Latched)
                {
                    if (a != null && a.Melee()) busy = true;
                    else MeleeStrike();      // (no melee clip - marines: the blow lands at once)
                }
            }

            // a lunging melee (the elite's root clip) stops where it meets a body, as Halo's biped collision stops it: the blow lands at the key frame
            if (a != null && a.Lunging && BodyAhead()) a.HoldLunge();
            TickHeat(dt);
            if (_charging && TickCharge(dt)) return;
            if (!Firing || WeaponInhibited || Feigning || Latched) { _wasFiring = false; _burstShot = 0; return; }
            if (busy || Seat != null && Weapon == null) return;
            if (Weapon == null && Muzzle == null) return;
            if (_overheated) return;
            if (Rounds == 0)
            {
                if (a != null && a.Reload())
                {
                    if (_weaponAnim != null) { try { _weaponAnim.Play("reload-empty"); } catch (System.Exception) { } }
                    HaloWeaponDef.Magazine mag = _wdef != null ? _wdef.PrimaryMagazine : null;
                    if (mag != null && mag.ReloadingEffect != null) { SfxReload++; HaloCombatFx.OnModel(mag.ReloadingEffect, WeaponRoot, false); }
                }
                Rounds = _wdef != null && _wdef.HasMagazine ? _wdef.PrimaryMagazine.RoundsLoadedMaximum : -1;      // the AI's reserve never runs out (Halo's AI: infinite ammo)
                return;
            }

            if (!_wasFiring && StartCharge()) { _wasFiring = true; return; }
            if (!_wasFiring) { _wasFiring = true; if (Time.time > _nextShot) _nextShot = Time.time; }
            if (Time.time < _nextShot) return;
            FireOnce();
            float rate = Def.RateOfFire > 0.01f ? Def.RateOfFire : (_wdef != null && _wdef.PrimaryTrigger != null ? Mathf.Max(_wdef.PrimaryTrigger.RateMax, _wdef.PrimaryTrigger.RateMin) : 2f);
            if (rate <= 0.01f) rate = 1f;
            if (AiRateScale > 0.01f) rate *= AiRateScale;
            _nextShot += 1f / rate;
            if (_nextShot < Time.time - 0.2f) _nextShot = Time.time;
        }

        /// <summary>One shot now (the AI may call it for single aimed shots): projectiles from the muzzle toward the aim, the firing effect, the recoil overlay.</summary>
        public void FireOnce() { FireOnce(false); }

        /// <summary>One volley; <paramref name="visualOnly"/> = a co-op puppet replaying the host's shot (no damage).</summary>
        public void FireOnce(bool visualOnly)
        {
            if (!Alive) return;
            Transform mz = Muzzle != null ? Muzzle : transform;
            Vector3 from = mz.position;
            Vector3 dir = AimPoint != Vector3.zero ? (AimPoint - from) : (Muzzle != null ? mz.right : transform.forward);
            if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
            dir.Normalize();
            ShotsFired++;
            _burstShot++;
            RevealCamo(0.5f);
            if (Rounds > 0) Rounds--;
            if (Animator != null) Animator.Fire();
            if (_weaponAnim != null) { try { _weaponAnim.Play("fire"); } catch (System.Exception) { } }
            var cat = HaloCombat.Catalog;
            HaloWeaponDef.Trigger t = _wdef != null && _wdef.Triggers.Count > _trigger ? _wdef.Triggers[_trigger] : null;
            if (t == null || cat == null) return;
            // the trigger's own firing effect (the weapon's fire sound, flash, shell) and, as the magazine runs dry, its empty click
            HaloWeaponDef.FiringEffect fe = t.EffectFor(_burstShot, null);
            if (fe != null && fe.Firing != null) { SfxFire++; HaloCombatFx.Play(fe.Firing, from, Quaternion.LookRotation(dir), mz); }
            if (Rounds == 0 && fe != null && fe.Empty != null) { SfxEmpty++; HaloCombatFx.OnModel(fe.Empty, WeaponRoot, false); }
            // heat (plasma weapons): past the overheated threshold the weapon vents with its overheated effect until it cools
            if (t.HeatPerRound > 0f && !visualOnly)
            {
                _heat = Mathf.Min(1f, _heat + t.HeatPerRound);
                if (_wdef.OverheatedThreshold > 0f && _heat >= _wdef.OverheatedThreshold && !_overheated)
                {
                    _overheated = true;
                    // the overheated effect loops (effect loop 0..0) for as long as the weapon stays overheated (TickHeat ends it), as the player's gun
                    if (_wdef.OverheatedEffect != null) { SfxOverheat++; HaloGunFx.Vent(WeaponRoot, _wdef, true); }
                    if (_weaponAnim != null) { try { _weaponAnim.Play("overheating"); _weaponAnim.SetState("overheated", true); } catch (System.Exception) { } }
                }
            }
            int n = Mathf.Max(1, t.ProjectilesPerShot);
            float err = Def.ProjectileError * Mathf.Max(0f, AiErrorScale) + (n > 1 ? t.ErrorAngleMax : t.ErrorAngleMin + t.MinimumError);
            for (int i = 0; i < n; i++)
            {
                Vector3 d = HaloHandling.Cone(dir, err);
                try { HaloCombat.FireProjectile(t.Projectile, from, Vector3.zero, AiShooter, Weapon, transform, d, visualOnly); }
                catch (System.Exception e) { if (!_fireFailed) { _fireFailed = true; HaloLog.Warn("Characters: " + Def.Name + " firing failed: " + e.Message); } }
            }

            HaloBipeds.RaiseFired(this, Weapon, from, dir);
        }

        private bool _fireFailed;

        // ------------------------------------------------------------------ the weapon's other sounds: ready, heat, overcharge

        /// <summary>Counts of the weapon sounds AI units played (labs / Player.log).</summary>
        public static int SfxFire, SfxReload, SfxEmpty, SfxOverheat, SfxReady, SfxCharge, SfxBodyImpact;
        /// <summary>AI overcharges that lit the weapon's "primary charged" attachments (labs).</summary>
        public static int ChargeGlows;

        private float _heat, _chargeT;
        private bool _overheated, _charging;
        private int _trigger;

        private Transform WeaponRoot => _weaponBuilt != null && _weaponBuilt.Root != null ? _weaponBuilt.Root.transform : (Muzzle != null ? Muzzle : transform);

        /// <summary>The weapon tag's ready effect (drawing it).</summary>
        private void PlayReady()
        {
            if (_wdef == null || _wdef.ReadyEffect == null || !Alive) return;
            SfxReady++;
            HaloCombatFx.OnModel(_wdef.ReadyEffect, WeaponRoot, false);
        }

        /// <summary>Heat bleeds off at the weapon's heat loss rate (a second) while not firing or while venting; vented below the recovery threshold.</summary>
        private void TickHeat(float dt)
        {
            if (_wdef == null || _heat <= 0f) return;
            if (!Firing || _overheated)
            {
                float loss = _wdef.HeatLossRate > 0f ? _wdef.HeatLossRate : 0.25f;
                _heat = Mathf.Max(0f, _heat - loss * dt);
            }

            if (_overheated && _heat <= _wdef.HeatRecoveryThreshold)
            {
                _overheated = false;
                HaloGunFx.Vent(WeaponRoot, _wdef, false);      // the vents close
                if (_weaponAnim != null) { try { _weaponAnim.SetState("overheated", false); _weaponAnim.Play("overheat-exit"); } catch (System.Exception) { } }
            }
        }

        /// <summary>
        /// The actor variant's special fire "overcharge" (plasma pistol grunts / jackals): at a burst's start, by the variant's special fire
        /// chance, the trigger charges (its charging effect: the charge hum) for its charging time, then the weapon's second trigger fires the
        /// charged bolt. False when the burst fires normally.
        /// </summary>
        private bool StartCharge()
        {
            if (Def.SpecialFireMode != 1 || _wdef == null || _wdef.Triggers.Count < 2 || _overheated) return false;
            HaloWeaponDef.Trigger t = _wdef.PrimaryTrigger;
            if (t == null || t.ChargingTime <= 0f) return false;
            if (SpecialFireMinInterval > 0f && Time.time - _lastSpecial < SpecialFireMinInterval) return false;      // (at most every special fire delay)
            if (_rng.NextDouble() >= Def.SpecialFireChance) return false;
            _lastSpecial = Time.time;
            _charging = true;
            _chargeT = 0f;
            if (t.ChargingEffect != null) { SfxCharge++; HaloCombatFx.OnModel(t.ChargingEffect, WeaponRoot, false); }
            // the weapon's "primary charged" input: its overcharge light + charging hum (plasma pistol), scaled by the charge in TickCharge
            if (_wdef.ChargeAttachments.Count > 0) { ChargeGlows++; HaloGunFx.Charge(WeaponRoot, _wdef, 0f); }
            return true;
        }

        /// <summary>Charging: true while it holds the shot; the charged bolt leaves when charged (or when the burst ends after charging).</summary>
        private bool TickCharge(float dt)
        {
            HaloWeaponDef.Trigger t = _wdef != null ? _wdef.PrimaryTrigger : null;
            if (t == null) { _charging = false; HaloGunFx.Charge(WeaponRoot, _wdef, -1f); return false; }
            _chargeT += dt;
            if (_chargeT < t.ChargingTime && Firing)
            {
                HaloGunFx.Charge(WeaponRoot, _wdef, Mathf.Clamp01(_chargeT / t.ChargingTime));
                return true;
            }
            _charging = false;
            HaloGunFx.Charge(WeaponRoot, _wdef, -1f);      // the glow goes with the shot (or the given-up charge)
            if (_chargeT >= t.ChargingTime * 0.5f)
            {
                _trigger = 1;
                FireOnce(false);
                _trigger = 0;
                _nextShot = Time.time + 1f;
            }

            return true;
        }

        // ------------------------------------------------------------------ the animation's key frames

        private void ActionKeyFrame(HaloUnitAnimator.ActionKind kind)
        {
            if (!Alive) return;
            // a co-op puppet only animates: the host owns the blow, the grenade and a carrier's fatal melee (its records arrive by net)
            if (Puppet) return;
            if (kind == HaloUnitAnimator.ActionKind.Throw) ReleaseGrenade();
            else if (kind == HaloUnitAnimator.ActionKind.Melee) MeleeStrike();
            else if (kind == HaloUnitAnimator.ActionKind.Reload && _weaponAnim != null) { }
        }

        /// <summary>The grenade leaves the hand: the globals' grenade of the unit's type, lobbed to land on the aim point.</summary>
        private void ReleaseGrenade()
        {
            var cat = HaloCombat.Catalog;
            if (cat == null || Grenades <= 0) return;
            int type = Mathf.Clamp(GrenadeType, 0, Mathf.Max(0, cat.Grenades.Count - 1));
            if (type >= cat.Grenades.Count) return;
            HaloTag proj = cat.Grenades[type].Projectile;
            if (proj == null) return;
            Grenades--;
            Transform hand = Marker("left hand") ?? Marker("right hand") ?? Marker("left hand elite") ?? transform;
            Vector3 from = hand.position;
            // the actor variant's grenade velocity is world units a TICK (grunt 0.13 -> 3.9 wu/s = 11.9 m/s, marine 0.23 -> 21 m/s); the unit's
            // own (@704: grunt / cyborg 10) is world units a second, as Combat throws the player's
            float speed = (Def.VariantGrenadeVelocity > 0.001f ? Def.VariantGrenadeVelocity * 30f : Def.GrenadeVelocity > 0.01f ? Def.GrenadeVelocity : 2f) * HaloSpace.Unit;
            Vector3 target = AimPoint != Vector3.zero ? AimPoint : transform.position + transform.forward * 10f;
            // actv trajectory type: 0 toss (the flatter arc), 1 lob (the higher arc), 2 bounce (flat, landing short and rolling on: ours 2/3 of the way)
            int traj = Def.GrenadeTrajectory;
            if (traj == 2) target = from + (target - from) * 0.67f;
            Vector3 vel = Lob(from, target, speed, traj == 1);
            HaloCombat.FireProjectile(proj, from, vel, AiShooter, proj, transform, vel.normalized);
            if (_grenadesLogged++ < 20) HaloLog.Info("Characters: " + Def.Name + " threw a " + proj.Name + " (" + speed.ToString("0.0") + " m/s, " + (traj == 1 ? "lob" : traj == 2 ? "bounce" : "toss") + ", " + (target - from).magnitude.ToString("0.0") + " m)");
        }

        private static int _grenadesLogged;

        /// <summary>A launch velocity of a given speed that lands on the target (the flatter of the two arcs, the higher one for a lob; 45 degrees when out of reach).</summary>
        private static Vector3 Lob(Vector3 from, Vector3 to, float speed, bool high = false)
        {
            Vector3 d = to - from;
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            float x = flat.magnitude, y = d.y, g = Gravity, v2 = speed * speed;
            if (x < 0.1f) return Vector3.up * speed;
            float disc = v2 * v2 - g * (g * x * x + 2f * y * v2);
            float ang = disc >= 0f ? Mathf.Atan((v2 + (high ? 1f : -1f) * Mathf.Sqrt(disc)) / (g * x)) : Mathf.PI / 4f;
            Vector3 h = flat / x;
            return (h * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang)) * speed;
        }

        /// <summary>The melee's moment (the clip's key frame, or at once without a clip): a "melee attack is fatal" unit (the flood carrier) dies
        /// by it - its body bursts once the death animation is done; any other unit strikes.</summary>
        private void MeleeStrike()
        {
            if (!Alive || Puppet) return;
            if (Def.MeleeIsFatal)
            {
                if (_fatalLogged++ < 10) HaloLog.Info("Characters: " + Def.Name + "'s melee is fatal: it goes off");
                Die(new HaloHit { SourcePlayer = -1, Melee = true, Direction = -transform.forward }, false, 0, 0, false, 0f);
                return;
            }

            MeleeHit();
        }

        private static int _fatalLogged;

        /// <summary>Reach of a melee blow beyond the body's radius when its damage (jpt!) has no radius. Normally the blow reaches the melee
        /// damage's own radius (elite 0.5 wu, hunter 0.3, flood 1); the actor variant's "melee range" is when the AI decides to melee, not how
        /// far the blow reaches. Fallback 0.5 wu (ours: the weapons' melee radius).</summary>
        public static float MeleeContactWu = 0.5f;

        /// <summary>The melee blow: the unit's melee damage (else the weapon's) to what is in front within contact reach (hit boxes and the local player).</summary>
        private void MeleeHit()
        {
            var cat = HaloCombat.Catalog;
            HaloTag jpt = Def.MeleeDamage ?? (_wdef != null ? _wdef.MeleeDamage : null);
            HaloDamageDef dd = jpt != null && cat != null ? cat.Damage(jpt) : null;
            if (dd == null) return;
            float contact = (dd.RadiusMax > 0.01f ? dd.RadiusMax : MeleeContactWu) * HaloSpace.Unit;      // the melee damage's own radius
            Vector3 c = Center + transform.forward * (_radius + contact * 0.5f);
            float amount = dd.DirectAmount(_rng, 0f, 0f, 0f);
            int n = Physics.OverlapSphereNonAlloc(c, contact * 0.5f, Overlap, (1 << HaloLayers.Hitbox) | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Collide);
            IHaloDamageable done = null;
            for (int i = 0; i < n; i++)
            {
                Collider col = Overlap[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                IHaloDamageable v = col.GetComponentInParent<IHaloDamageable>();
                if (v == null || v == done) continue;
                HaloUnit hu = v as HaloUnit;
                if (hu != null && (!hu.Alive || hu == this)) continue;      // (bodies are triggers too)
                // Halo's AI does not hurt its own side with melee: a swing passes a swarm-mate / squad-mate standing in front (Flood blows
                // killed infection forms around the target); the next hit box in reach, if any, takes it
                if (hu != null && !HaloAllegiance.AreEnemies(Team, hu.Team)) { MeleeSkippedAllies++; continue; }
                done = v;
                HaloProjectiles.Deliver(v, new HaloHit { Amount = amount, Effect = jpt, SourcePlayer = AiShooter, Cause = jpt, Point = col.ClosestPoint(c), Direction = transform.forward, Melee = true });
                break;
            }

            // the local player (no hit box of its own)
            Vector3 me = HaloCombat.LocalChest;
            if (HaloLocal.Alive && me != Vector3.zero && (me - c).magnitude < contact * 0.5f + PlayerBodyRadius && HaloAllegiance.AreEnemies(Team, HaloTeam.Player))
                HaloCombat.TakeHit(new HaloHit { Amount = amount, Effect = jpt, SourcePlayer = AiShooter, Cause = jpt, Point = me, Direction = transform.forward, Melee = true });
        }

        /// <summary>A living unit's hit box or the local player touching the front of this body (ours: 0.25 m ahead of the radius): a lunge stops.</summary>
        private bool BodyAhead()
        {
            const float Ahead = 0.25f;      // ours
            Vector3 c = Center + transform.forward * (_radius + Ahead);
            Vector3 me = HaloCombat.LocalChest;
            if (HaloLocal.Alive && me != Vector3.zero && (me - c).magnitude < Ahead + PlayerBodyRadius) return true;
            int n = Physics.OverlapSphereNonAlloc(c, Ahead, Overlap, 1 << HaloLayers.Hitbox, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider col = Overlap[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                HaloUnit hu = col.GetComponentInParent<HaloUnit>();
                if (hu != null && hu != this && hu.Alive) return true;
            }

            return false;
        }

        /// <summary>The local player's body radius for melee contact (ours: the player has no hit box of its own; the cyborg is 0.2 wu wide).</summary>
        public static float PlayerBodyRadius = 0.3f;

        private static readonly Collider[] Overlap = new Collider[32];
        /// <summary>Melee swings that passed over a non-enemy unit in reach (lab).</summary>
        public static int MeleeSkippedAllies;

        // ------------------------------------------------------------------ death: the weapon falls

        private void DropWeapon()
        {
            if (Weapon == null || _wdef == null) { ReleaseWeapon(); return; }
            Vector3 at = Muzzle != null && _weaponBuilt != null ? _weaponBuilt.Root.transform.position : Center;
            Quaternion rot = _weaponBuilt != null && _weaponBuilt.Root != null ? _weaponBuilt.Root.transform.rotation : transform.rotation;
            HaloTag w = Weapon;
            ReleaseWeapon();
            Weapon = null;
            WeaponLabel = null;
            try
            {
                HaloGun g = HaloGun.Create(_wdef, HaloPickups.Root);
                if (g == null) return;
                g.SetScale(1f);
                g.transform.position = at;
                g.transform.rotation = rot * Quaternion.Euler(0f, 90f, 0f);
                HaloPickups.Item it = HaloPickups.DropWeapon(g, _velocity + Vector3.up * 1f, false, at);
                if (it != null) HaloLog.Info("Characters: " + Def.Name + " dropped its " + w.Name);
            }
            catch (System.Exception e) { HaloLog.Warn("Characters: dropping " + w.Name + " failed: " + e.Message); }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Damage as Halo's units take it: the hit lands on a hit box (a collision-model node: its region and material); the shield takes it
    /// first where the material passes damage to the shield (elites everywhere; a jackal only on its arm shield), scaled by the damage
    /// effect's modifier for the shield's material type and the material's shield multiplier (the material's shield leak passes through);
    /// the body takes the rest by the modifier for the material type and the material's body multiplier; headshots (a "head" material, a
    /// damage that can cause headshots, no shield) kill. Regions with a damage threshold are shot off (flood arms / heads: their "~damaged"
    /// permutation) and may force the death or the weapon's drop. The shield recharges after the collision model's stun time over its
    /// recharge time, with the shield effects. Hit reactions (s-ping / h-ping by the biped's thresholds), deaths (s-kill / h-kill: hard for
    /// explosions and hits past the hard death threshold) by direction and body region; a body destroyed past its threshold plays the
    /// destroyed effect and goes (infection forms pop). Death raises HaloEvents.Killed (killer = the player's id, victim -1) and
    /// HaloBipeds.Died; the weapon falls; the body stays (HaloBipeds.BodySeconds).
    /// </summary>
    public partial class HaloUnit : IHaloHitInfo
    {
        private float _shieldStunUntil;
        private bool _shieldRechargeSounded = true;
        private float _nextPing;
        private float _shieldFlash;
        private readonly Dictionary<int, float> _regionDamage = new Dictionary<int, float>();
        private static int _deathsLogged;

        /// <summary>The shield's recent hit glow 0..1 (the shield effect; fades in a quarter second).</summary>
        public float ShieldFlash => _shieldFlash;

        /// <summary>Playing a feigned death (flood combat forms: alive, hit boxes solid, lying in a death pose) until the resurrect animation.</summary>
        public bool Feigning { get; private set; }
        /// <summary>Destroyed regions inhibit melee / weapon attacks / walking (flood forms' arms and legs shot off: coll region flags 0x20 / 0x40 / 0x80).
        /// The unit itself stops melee / firing; the AI stops walking it (and should not choose those attacks).</summary>
        public bool MeleeInhibited { get; private set; }
        public bool WeaponInhibited { get; private set; }
        public bool WalkingInhibited { get; private set; }
        /// <summary>An infection form attached to a target unit (Halo: impact melee attaches to unit) - it plays melee-continuous and strikes every
        /// actor melee attack delay until either dies; any damage pops it.</summary>
        public bool Latched { get; private set; }
        /// <summary>The unit an attached infection form holds on to (null when not latched).</summary>
        public HaloUnit LatchTarget => _latchTarget;

        private HaloUnit _latchTarget;
        private float _nextLatchStrike, _feignUntil;
        private bool _feignBack, _feignedOnce, _destroyPending, _shieldRegionOff;
        private static int _popsLogged, _latchLogged, _feignLogged;
        private static readonly System.Collections.Generic.HashSet<HaloTag> DestroyedLogged = new System.Collections.Generic.HashSet<HaloTag>();
        /// <summary>Mission unload (HaloBipeds.Clear): the tag-keyed caches let the old map go.</summary>
        internal static void ClearStaticCaches() { DestroyedLogged.Clear(); }

        /// <summary>
        /// Impact melee of an infection form (the AI calls it on leap / swarm contact; <paramref name="target"/> null = the local player):
        /// - the target's shield is up and its unit "shields fry infection forms" (the player cyborg, special elites) and this unit "impact melee
        ///   dies on shields": it pops (dies, destroyed after dying: its pop effect);
        /// - else the unit's melee damage lands once and, when this unit "impact melee attaches to unit" and the target lacks "melee attackers
        ///   cannot attach" (marines, crew, normal elites - not the player, hunters, sentinels), it latches on: parented to the target's body
        ///   marker, melee-continuous, a strike every actor melee attack delay. True when attached.
        /// </summary>
        public bool TryLatch(HaloUnit target)
        {
            if (!Alive || Def == null || Latched || Puppet) return Latched;
            HaloCharacterDef td;
            bool shieldUp;
            if (target == null)
            {
                if (!HaloLocal.Alive) return false;
                var cat = HaloCombat.Catalog;
                td = cat != null ? HaloBipeds.Def(cat.PlayerUnit) : null;
                shieldUp = HaloLocal.Shield > 0.01f;
            }
            else
            {
                if (!target.Alive || target == this || target.Def == null) return false;
                td = target.Def;
                shieldUp = target.Shield > 0.01f && target.MaxShield > 0f;
            }

            if (shieldUp && td != null && td.ShieldsFryInfectionForms && Def.ImpactMeleeDiesOnShields)
            {
                if (_popsLogged++ < 10) HaloLog.Info("Characters: " + Def.Name + " popped on " + (target != null ? target.Def.Name : "the player") + "'s shield");
                Die(new HaloHit { SourcePlayer = -1, Direction = -transform.forward }, true, 0, 0, false, 1f);
                return false;
            }

            StrikeTarget(target);
            if (target == null || !Def.ImpactMeleeAttachesToUnit || td == null || td.MeleeAttackersCannotAttach || !target.Alive) return false;
            Transform m = target.BodyMarker();
            Vector3 c = m.position, me = Center;
            Vector3 away = me - c; away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : -target.transform.forward;
            Suspended = true;                                             // (no own movement / gravity while attached)
            transform.SetParent(m, true);
            transform.position = c + away * Mathf.Max(0.15f, target._radius) - Vector3.up * (Center.y - transform.position.y);
            transform.rotation = Quaternion.LookRotation(-away, Vector3.up);
            Latched = true;
            _latchTarget = target;
            float delay = Def.MeleeAttackDelay > 0.05f ? Def.MeleeAttackDelay : 3f;      // (3 s: the infection form's actr when it has none)
            _nextLatchStrike = Time.time + delay;
            if (Animator != null) Animator.Gesture(HaloUnitAnimator.AnimMeleeContinuous);
            if (_latchLogged++ < 10) HaloLog.Info("Characters: " + Def.Name + " latched onto " + target.Def.Name + " (a strike every " + delay.ToString("0.0") + " s)");
            return true;
        }

        /// <summary>Lets go of the latch target (it died, or this unit pops): back under the world root, falling from where it is.</summary>
        public void Unlatch()
        {
            if (!Latched) return;
            Latched = false;
            _latchTarget = null;
            if (Puppet) return;      // (a co-op puppet is never parented to its target: Net places it)
            Vector3 p = transform.position;
            Vector3 f = transform.forward; f.y = 0f;
            float yaw = f.sqrMagnitude > 1e-4f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            transform.SetParent(HaloBipeds.Parent, true);
            Teleport(p, yaw);
            Suspended = false;
        }

        /// <summary>The unit's melee damage on one target (a unit, or the local player when null): the latch / impact strike.</summary>
        private void StrikeTarget(HaloUnit target)
        {
            var cat = HaloCombat.Catalog;
            HaloTag jpt = Def.MeleeDamage ?? (_wdef != null ? _wdef.MeleeDamage : null);
            HaloDamageDef dd = jpt != null && cat != null ? cat.Damage(jpt) : null;
            if (dd == null) return;
            float amount = dd.DirectAmount(_rng, 0f, 0f, 0f);
            if (target == null)
            {
                Vector3 me = HaloCombat.LocalChest;
                if (me == Vector3.zero || !HaloAllegiance.AreEnemies(Team, HaloTeam.Player)) return;
                HaloCombat.TakeHit(new HaloHit { Amount = amount, Effect = jpt, SourcePlayer = AiShooter, Cause = jpt, Point = me, Direction = (me - Center).normalized, Melee = true });
                return;
            }

            Vector3 at = target.Center;
            HaloProjectiles.Deliver(target, new HaloHit { Amount = amount, Effect = jpt, SourcePlayer = AiShooter, Cause = jpt, Point = at, Direction = (at - Center).normalized, Melee = true });
        }

        public virtual void TakeHit(HaloHit hit)
        {
            if (!Alive || Def == null) return;
            if (Invulnerable && !hit.Instant) return;
            if (Puppet) { try { HaloNetGame.PuppetHit(this, hit); } catch (System.Exception) { } return; }
            var cat = HaloCombat.Catalog;
            HaloDamageDef dd = hit.Effect != null && cat != null ? cat.Damage(hit.Effect) : null;
            HaloCollisionDef coll = Def.Coll;
            Hitbox hb = Nearest(hit.Point);
            HaloCollisionDef.Material mat = coll != null && hb != null ? coll.MaterialAt(hb.Material) : (coll != null && coll.Materials.Count > 0 ? coll.Materials[0] : null);
            LastHit = hit;
            LastHitTime = Time.time;
            RevealCamo(0.5f);
            int dir = Direction(hit.Direction);
            int region = hb != null ? hb.AnimRegion : 0;
            // hunters ("not instantly killed by melee"): a melee from behind is a normal melee
            if (hit.Melee && Def.NotInstantlyKilledByMelee) { hit.Instant = false; hit.BackHit = false; }
            // from behind: unshielded units die at once; a player's blow kills shielded ones too (Halo CE's back smack on an elite)
            if (hit.Instant || (hit.Melee && hit.BackHit && (MaxShield <= 0f || hit.SourcePlayer >= 0))) { Die(hit, true, dir, region, false, 1f); return; }
            // infection forms: an attached one pops on any damage; "infection form pop" damage (their own pop, the carrier's burst) pops them
            if (Def.InfectionForm && (Latched && hit.Amount > 0f || dd != null && dd.InfectionFormPop)) { Die(hit, true, dir, region, false, 1f); return; }
            if (coll != null && coll.OnlyDamagedByExplosives && !hit.Explosion) return;
            if (MaxBody <= 0f && !hit.Instant) return;      // no body vitality at all (Cortana, the monitor): cannot be hurt
            float amount = Mathf.Max(0f, hit.Amount);
            if (amount <= 0f) return;
            if (hit.SourcePlayer >= 0 && HaloAllegiance.AreFriends(Team, HaloTeam.Player) && coll != null) amount *= Mathf.Clamp01(1f - coll.FriendlyDamageResistance);

            float maxS = MaxShield, maxB = Mathf.Max(0.01f, MaxBody);
            float shieldPts = Shield * maxS, bodyPts = Body * maxB;
            float rest = amount, shieldTaken = 0f;
            bool hadShield = shieldPts > 0.01f;
            float shieldMult = mat != null ? mat.ShieldMultiplier : 1f;
            if (maxS > 0f) { _shieldStunUntil = Time.time + (coll != null && coll.StunTime > 0f ? coll.StunTime : 1f); _shieldRechargeSounded = false; }
            if (hadShield && shieldMult > 0f && !(dd != null && dd.SkipsShields))
            {
                float mod = (dd != null ? dd.Modifier(coll != null ? coll.ShieldMaterialType : HaloDamageDef.CyborgEnergyShield) : 1f) * shieldMult;
                if (mod > 0f)
                {
                    float s = rest * mod;
                    float leak = mat != null ? Mathf.Clamp01(mat.ShieldLeak) : 0f;
                    if (s <= shieldPts) { shieldPts -= s; shieldTaken = s; rest = rest * leak; }
                    else
                    {
                        shieldTaken = shieldPts;
                        rest = (s - shieldPts) / mod;
                        shieldPts = 0f;
                        if (coll != null && coll.ShieldDepletedEffect != null) HaloCombatFx.Play(coll.ShieldDepletedEffect, Center, transform.rotation, BodyMarker());
                    }

                    _shieldFlash = 1f;
                    if (coll != null && coll.ShieldDamagedEffect != null && shieldPts > 0f) HaloCombatFx.Play(coll.ShieldDamagedEffect, hit.Point != Vector3.zero ? hit.Point : Center, transform.rotation, null);
                }
            }

            if (dd != null && dd.OnlyHurtsShields) rest = 0f;
            Shield = maxS > 0f ? shieldPts / maxS : 0f;
            bool head = hit.Headshot || (mat != null && mat.Head && dd != null && dd.CanCauseHeadshots);
            if (head && dd != null && dd.CanCauseHeadshots && shieldPts <= 0.01f && rest > 0f) { Die(hit, false, dir, 2, true, 1f); return; }

            float bodyTaken = 0f;
            if (rest > 0f)
            {
                float mod = (dd != null && mat != null ? dd.Modifier(mat.Type) : 1f) * (mat != null ? mat.BodyMultiplier : 1f);
                bodyTaken = rest * mod;
                float before = bodyPts;
                bodyPts -= bodyTaken;
                if (coll != null && coll.BodyDamagedEffect != null && before > coll.BodyDamagedThreshold * maxB && bodyPts <= coll.BodyDamagedThreshold * maxB)
                    HaloCombatFx.Play(coll.BodyDamagedEffect, Center, transform.rotation, BodyMarker());
                if (hb != null && hb.Region >= 0 && coll != null && hb.Region < coll.Regions.Count) RegionDamage(coll.Regions[hb.Region], hb.Region, bodyTaken, hit);
                if (!Alive) return;
            }

            Body = bodyPts / maxB;
            float frac = (shieldTaken + bodyTaken) / (maxB + Mathf.Max(0f, maxS));
            if (hadShield && Shield <= 0f) ShieldRegionOff(true);
            HaloBipeds.RaiseDamaged(this, hit);
            if (bodyPts <= 0f)
            {
                bool hard = hit.Explosion || (Def.HardDeathThreshold > 0f && bodyTaken / maxB >= Def.HardDeathThreshold);
                Die(hit, hard, dir, region, false, -bodyPts / maxB);
                return;
            }

            // feigned death (flood combat forms): a hit taking at least the feign death threshold of the body, by the feign death chance the
            // first time and the feign repeat chance after; the death clip plays, the unit stays alive and gets up after the feign death time
            if (!Feigning && Animator != null && Def.FeignDeathThreshold > 0f && Def.FeignDeathTime > 0f && bodyTaken / maxB >= Def.FeignDeathThreshold && Seat == null)
            {
                float chance = _feignedOnce ? Def.FeignRepeatChance : Def.FeignDeathChance;
                if (chance > 0f && _rng.NextDouble() < chance)
                {
                    bool hardF = hit.Explosion || (Def.HardDeathThreshold > 0f && bodyTaken / maxB >= Def.HardDeathThreshold);
                    StartFeign(hardF, dir, region);
                    return;
                }
            }

            // hit reactions
            if (Animator != null && Time.time >= _nextPing)
            {
                bool hard = Def.HardPingThreshold > 0f && frac >= Def.HardPingThreshold || hit.Explosion && frac > 0.1f;
                bool soft = frac >= Def.SoftPingThreshold;
                if (hard || soft)
                {
                    if (Animator.Ping(hard, dir, region))
                        _nextPing = Time.time + Mathf.Max(0.25f, hard ? Def.HardPingInterruptTime : Def.SoftPingInterruptTime);
                }
            }
        }

        private void RegionDamage(HaloCollisionDef.Region reg, int index, float points, HaloHit hit)
        {
            float d;
            _regionDamage.TryGetValue(index, out d);
            d += points;
            _regionDamage[index] = d;
            if (reg.DamageThreshold <= 0f || d < reg.DamageThreshold * MaxBody || _destroyedRegions.Contains(reg.Name)) return;
            DestroyRegion(reg);
            if (reg.InhibitsMelee || reg.InhibitsWeaponAttack || reg.InhibitsWalking)
            {
                MeleeInhibited |= reg.InhibitsMelee;
                WeaponInhibited |= reg.InhibitsWeaponAttack;
                WalkingInhibited |= reg.InhibitsWalking;
                if (reg.InhibitsWeaponAttack) Firing = false;
                if (_inhibitLogged++ < 10) HaloLog.Info("Characters: " + Def.Name + " lost its " + reg.Name + ":" + (reg.InhibitsMelee ? " no melee" : "") + (reg.InhibitsWeaponAttack ? " no weapon attack" : "") + (reg.InhibitsWalking ? " cannot walk" : ""));
            }
            if (reg.ForcesDropWeapon && Weapon != null) DropWeapon();
            if (reg.ForcesObjectToDie) Die(hit, false, Direction(hit.Direction), 0, false, 0f);
        }

        /// <summary>Halo's hit direction for the damage animations: 0 front, 1 left, 2 right, 3 back (where the hit came from, relative to the body).</summary>
        private int Direction(Vector3 travel)
        {
            if (travel.sqrMagnitude < 1e-6f) return 0;
            Vector3 from = -travel;
            from.y = 0f;
            float f = Vector3.Dot(from.normalized, transform.forward), r = Vector3.Dot(from.normalized, transform.right);
            if (f >= 0.7071f) return 0;
            if (f <= -0.7071f) return 3;
            return r > 0f ? 2 : 1;
        }

        private Hitbox Nearest(Vector3 p)
        {
            if (Hitboxes.Count == 0) return null;
            if (p == Vector3.zero) return Hitboxes[0];
            Hitbox best = null;
            float bd = float.MaxValue, bv = float.MaxValue;
            const float Tie = 0.0001f;      // (1 cm squared: per-material boxes of one node overlap - the smaller, more specific box wins)
            for (int i = 0; i < Hitboxes.Count; i++)
            {
                Hitbox h = Hitboxes[i];
                if (h.Box == null || !h.Box.enabled) continue;
                float d = (h.Box.ClosestPoint(p) - p).sqrMagnitude;
                Vector3 sz = h.Box.size;
                float vol = sz.x * sz.y * sz.z;
                if (d < bd - Tie || d <= bd + Tie && vol < bv) { bd = Mathf.Min(d, bd); bv = vol; best = h; }
            }

            return best;
        }

        private Hitbox Of(Collider c)
        {
            for (int i = 0; i < Hitboxes.Count; i++) if (Hitboxes[i].Box == c) return Hitboxes[i];
            return null;
        }

        private Transform BodyMarker() { return Marker("body") ?? _pelvisNode ?? transform; }

        private void TickShield(float dt)
        {
            if (_shieldFlash > 0f) _shieldFlash = Mathf.Max(0f, _shieldFlash - dt * 2f);
            TickBodyState(dt);
            TickShieldGlow();
            if (MaxShield <= 0f || Time.time < _shieldStunUntil || Shield >= 1f) return;
            HaloCollisionDef coll = Def.Coll;
            if (!_shieldRechargeSounded)
            {
                _shieldRechargeSounded = true;
                if (coll != null && coll.ShieldRechargingEffect != null) HaloCombatFx.Play(coll.ShieldRechargingEffect, Center, transform.rotation, BodyMarker());
            }

            float secs = coll != null ? coll.FullRechargeSeconds : 4f;
            Shield = Mathf.Min(1f, Shield + dt / Mathf.Max(0.25f, secs));
            if (Shield > 0f && _shieldRegionOff) ShieldRegionOff(false);      // (the jackal's shield comes back after its stun)
        }

        /// <summary>
        /// A region flagged "disappears when shield is off" (the jackal's energy shield: region "shield", permutations base00 / ~shield_off) goes
        /// with the shield: the model shows its "~" permutation and the hit box is rebuilt from that permutation's BSP (the small armoured
        /// frame), so shots pass to the jackal; back when the shield recharges.
        /// </summary>
        private void ShieldRegionOff(bool off)
        {
            if (off == _shieldRegionOff || Def.Coll == null) return;
            HaloCollisionDef coll = Def.Coll;
            bool changed = false;
            for (int i = 0; i < coll.Regions.Count; i++)
            {
                HaloCollisionDef.Region reg = coll.Regions[i];
                if (!reg.DisappearsWhenShieldIsOff) continue;
                string offPerm = null, onPerm = null;
                for (int k = 0; k < reg.Permutations.Count; k++)
                {
                    string p = reg.Permutations[k];
                    if (p.StartsWith("~")) { if (offPerm == null) offPerm = p; }
                    else if (onPerm == null) onPerm = p;
                }

                if (offPerm == null) continue;
                string want = off ? offPerm : (_shieldRegionOn != null ? _shieldRegionOn : onPerm);
                string now;
                if (off && Permutations.TryGetValue(reg.Name, out now) && !now.StartsWith("~")) _shieldRegionOn = now;
                if (want == null) continue;
                Permutations[reg.Name] = want;
                changed = true;
            }

            _shieldRegionOff = off;
            if (!changed) return;
            BuildBody();
            if (_shieldOffLogged++ < 6) HaloLog.Info("Characters: " + Def.Name + "'s energy shield " + (off ? "is down (region off, shots pass)" : "is back"));
        }

        private string _shieldRegionOn;
        private static int _shieldOffLogged, _inhibitLogged;

        /// <summary>Per frame (alive, not a puppet): the feigned death's timer, the latch's strikes, the camouflage, the leap animations.</summary>
        private void TickBodyState(float dt)
        {
            if (Feigning && Time.time >= _feignUntil) EndFeign();
            if (Latched)
            {
                HaloUnit t = _latchTarget;
                if (t == null || !t.Alive) Unlatch();
                else
                {
                    if (Time.time >= _nextLatchStrike)
                    {
                        _nextLatchStrike = Time.time + (Def.MeleeAttackDelay > 0.05f ? Def.MeleeAttackDelay : 3f);
                        StrikeTarget(t);
                    }

                    if (Animator != null && Animator.Action == HaloUnitAnimator.ActionKind.None) Animator.Gesture(HaloUnitAnimator.AnimMeleeContinuous);
                }
            }

            TickCamo();
            // the flood leap (the mover's Leap): leap-start, then leap-airborne until it lands
            bool leaping = Leaping;
            if (leaping != _wasLeaping && Animator != null)
            {
                if (leaping) { Animator.PlayLeap(HaloUnitAnimator.LeapPhase.Start); Animator.PlayLeap(HaloUnitAnimator.LeapPhase.Airborne); }
                else Animator.EndLeap();
            }

            _wasLeaping = leaping;
        }

        private bool _wasLeaping;

        private void StartFeign(bool hard, int dir, int region)
        {
            Feigning = true;
            _feignedOnce = true;
            _feignHard = hard; _feignDir = dir; _feignRegion = region;
            _feignBack = dir == 3;
            _feignUntil = Time.time + Def.FeignDeathTime;
            Firing = false;
            MoveVelocity = Vector3.zero;
            Animator.Feign(hard, dir, region);
            if (_feignLogged++ < 10) HaloLog.Info("Characters: " + Def.Name + " feigned death (" + (Animator.DeathName ?? "-") + ", up in " + Def.FeignDeathTime.ToString("0.0") + " s)");
        }

        private void EndFeign()
        {
            Feigning = false;
            if (Animator != null && Alive) Animator.Resurrect(_feignBack);
        }

        // ------------------------------------------------------------------ active camouflage (stealth elites)

        private float _camoRevealUntil;
        private bool _camoHidden;
        private readonly List<Renderer> _camoRenderers = new List<Renderer>();

        /// <summary>Firing or a hit shows a camouflaged unit for a moment (ours: 0.5 s, half that with super camouflage; like the players' camo).</summary>
        private void RevealCamo(float seconds)
        {
            if (!Camouflaged) return;
            float s = Def.SuperActiveCamouflage ? seconds * 0.5f : seconds;
            if (Time.time + s > _camoRevealUntil) _camoRevealUntil = Time.time + s;
        }

        private void TickCamo()
        {
            bool hide = Camouflaged && Alive && Time.time >= _camoRevealUntil;
            if (hide == _camoHidden) return;
            SetCamoHidden(hide);
        }

        /// <summary>The body and weapon renderers off (the camouflage) or on; the shield overlay shimmers faintly while hidden.</summary>
        private void SetCamoHidden(bool hide)
        {
            _camoHidden = hide;
            _camoRenderers.Clear();
            GetComponentsInChildren(true, _camoRenderers);
            for (int i = 0; i < _camoRenderers.Count; i++)
            {
                Renderer r = _camoRenderers[i];
                if (r == null || r == _shieldR) continue;
                r.enabled = !hide;
            }
        }

        /// <summary>Kills the unit (script ai_kill / unit_kill): the soft death toward the front.</summary>
        public virtual void Kill()
        {
            if (!Alive) return;
            if (_frames == 0) { PlaceDead(); return; }      // killed before it ever drew: a scenario biped placed dead
            Die(new HaloHit { SourcePlayer = -1 }, false, 0, 0, false, 0f);
        }

        /// <summary>A body lying as placed (scenario bipeds flagged dead): the end of a death animation at once, no effects, no events, the weapon kept.</summary>
        public void PlaceDead()
        {
            if (!Alive) return;
            Alive = false;
            Body = 0f; Shield = 0f;
            Firing = false;
            DeadFor = -100000f;      // (placed bodies are never swept)
            _bodyDown = true;
            for (int i = 0; i < Hitboxes.Count; i++) if (Hitboxes[i].Box != null) Hitboxes[i].Box.isTrigger = true;
            if (Animator == null || _nodes == null) return;
            Animator.Kill(false, _rng.Next(4), 0);
            HaloUnitAnimInput inp = AnimInput();
            Animator.Step(inp, 60f);
            WritePose();
        }

        /// <summary>Kills it without a death animation or effects (unit_kill_silent): it falls still where it is.</summary>
        public void KillSilent()
        {
            if (!Alive) return;
            Alive = false;
            Body = 0f; Shield = 0f;
            Firing = false;
            if (Animator != null) Animator.Kill(false, 0, 0);
            HaloBipeds.RaiseDied(this, new HaloHit { SourcePlayer = -1 });
        }

        private void Die(HaloHit hit, bool hard, int dir, int region, bool headshot, float overkill)
        {
            if (!Alive) return;
            Alive = false;
            Body = 0f;
            Shield = 0f;
            Firing = false;
            MoveVelocity = Vector3.zero;
            if (Latched) Unlatch();
            bool wasFeigning = Feigning;
            Feigning = false;
            if (_camoHidden) SetCamoHidden(false);      // (the camouflage drops with the death)
            var cat = HaloCombat.Catalog;
            HaloDamageDef dd = hit.Effect != null && cat != null ? cat.Damage(hit.Effect) : null;
            HaloCollisionDef coll = Def.Coll;
            // Halo: the body is destroyed (its body destroyed effect: the carrier's burst, the infection form's pop) when the unit is "destroyed
            // after dying" (once the death animation has played), when the overkill passes the coll's destroyed threshold, or when it has no
            // death animation at all (sentinels)
            bool hasDeath = Animator != null && HasDeath();
            bool destroyNow = coll != null && coll.BodyDestroyedEffect != null && (coll.BodyDestroyedThreshold > 0f && overkill >= coll.BodyDestroyedThreshold || !hasDeath);
            bool destroyLater = !destroyNow && coll != null && coll.BodyDestroyedEffect != null && Def.DestroyedAfterDying;
            bool destroyed = destroyNow || destroyLater;
            if (_shieldR != null) _shieldR.enabled = false;
            if (Animator != null) Animator.Kill(hard, dir, region);
            if (coll != null && coll.BodyDepletedEffect != null) HaloCombatFx.Play(coll.BodyDepletedEffect, Center, transform.rotation, BodyMarker());
            if (Puppet || !DropsItems) ReleaseWeapon();      // (puppets: the host drops it; unit_doesnt_drop_items: it goes)
            else { DropWeapon(); DropGrenades(); }
            // the body no longer stops shots or players (triggers: Combat's casts ignore them; it still answers "dead")
            for (int i = 0; i < Hitboxes.Count; i++) if (Hitboxes[i].Box != null) Hitboxes[i].Box.isTrigger = true;
            if (destroyLater) { _destroyPending = true; _destroyBy = Time.time + 5f; }      // (5 s: ours, a death that never ends)
            else if (!destroyed) HaloBipeds.AddCorpse(this);
            if (wasFeigning && _feignLogged < 20) HaloLog.Info("Characters: " + Def.Name + " killed while feigning death");

            var k = new HaloKill
            {
                Killer = hit.SourcePlayer, Victim = -1, Cause = hit.Cause, Melee = hit.Melee, Headshot = headshot,
                Grenade = dd != null && dd.Kind == HaloDamageDef.Category.Grenade, Splatter = dd != null && dd.Kind == HaloDamageDef.Category.Vehicle,
            };
            if (_deathsLogged++ < 30)
                HaloLog.Info("Characters: " + Def.Name + " died (" + (Animator != null && Animator.DeathName != null ? Animator.DeathName : "no death animation") + (headshot ? ", headshot" : "") +
                             (destroyed ? ", body destroyed" : "") + (hit.SourcePlayer >= 0 ? ", by player " + hit.SourcePlayer : "") + (hit.Cause != null ? ", " + hit.Cause.Name : "") + ")");
            if (!Puppet) HaloEvents.RaiseKilled(k);
            HaloBipeds.RaiseDied(this, hit);
            if (destroyNow) DestroyBody();
        }

        private float _destroyBy;

        /// <summary>Dead and "destroyed after dying": destroyed once the death animation is done (the carrier's s-kill 10 frames, the infection
        /// form's h-kill 7; airborne-dead / landing-dead first when it falls) - Body's animation tick asks.</summary>
        private void TickDestroyPending()
        {
            if (!_destroyPending) return;
            if (Animator == null || Animator.DeathDone || Time.time >= _destroyBy) DestroyBody();
        }

        /// <summary>
        /// The body is destroyed: the collision model's body destroyed effect at the body centre (its 'jpt!' parts are applied as area damage by
        /// the effect runtime - the carrier's burst, the infection form's pop; Die only plays the effect), HaloBipeds.Destroyed (the AI releases
        /// the unit's spawned actors from it), the model hidden and the object gone.
        /// </summary>
        private void DestroyBody()
        {
            _destroyPending = false;
            if (_bodyGone) return;
            _bodyGone = true;
            HaloCollisionDef coll = Def.Coll;
            Vector3 at = Center;
            if (coll != null && coll.BodyDestroyedEffect != null) HaloCombatFx.Play(coll.BodyDestroyedEffect, at, transform.rotation, null, this);     // (its jpt! parts: Effects applies them, this unit the creator)
            if (DestroyedLogged.Add(Def.Variant ?? Def.Unit))
                HaloLog.Info("Characters: " + Def.Name + " destroyed after dying (" + (coll != null && coll.BodyDestroyedEffect != null ? coll.BodyDestroyedEffect.Name : "no effect") +
                             (Def.SpawnedActor != null ? ", spawns " + Def.SpawnedCountMin + ".." + Def.SpawnedCountMax + " " + Def.SpawnedActor.Name : "") + ")");
            HaloBipeds.ReleaseLatchers(this);
            // inside an area damage (a blast killed it outright) its own burst above was queued behind that blast: the destroyed event (the AI
            // spawns a carrier's infection forms on it) waits until the burst has run, so the forms are not caught in it
            if (!HaloProjectiles.DeferDestroyed(this)) HaloBipeds.RaiseDestroyed(this);
            if (Built != null && Built.Root != null) Built.Root.SetActive(false);
            if (_shieldR != null) _shieldR.enabled = false;
            for (int i = 0; i < Hitboxes.Count; i++) if (Hitboxes[i].Box != null) Hitboxes[i].Box.enabled = false;
            Destroy(gameObject);
        }

        private bool _bodyGone;

        /// <summary>Destroyed (popped, burst) - no body left.</summary>
        public bool BodyDestroyed => _bodyGone;

        private bool HasDeath()
        {
            short[] t = Animator.Graph.UnitDamage;
            if (t == null) return false;
            for (int i = 88; i < t.Length; i++) if (t[i] >= 0) return true;
            return false;
        }

        private void DropGrenades()
        {
            if (Grenades <= 0 || Def.Variant == null) return;
            if (_rng.NextDouble() < Def.DontDropGrenadesChance) return;
            var cat = HaloCombat.Catalog;
            if (cat == null) return;
            HaloTag eq = Def.VariantEquipment;
            if (eq == null && GrenadeType >= 0 && GrenadeType < cat.Grenades.Count) eq = cat.Grenades[GrenadeType].Equipment;
            if (eq == null) return;
            try { HaloPickups.PlaceAt(eq, Center + transform.right * 0.2f, _yaw); } catch (System.Exception) { }
        }

        // ------------------------------------------------------------------ IHaloHitInfo (Combat asks the shot's target)

        public int HitSlot => -1;
        public bool HitIsHead(Collider c) { Hitbox h = Of(c); return h != null && h.Head; }
        public Vector3 HitFacing => transform.forward;
        public bool HitAlive => Alive;

        /// <summary>The material at a point: the shield's while it is up there, else the hit box's (impact effects by material).</summary>
        public int HitMaterial(Vector3 point)
        {
            HaloCollisionDef coll = Def != null ? Def.Coll : null;
            if (coll == null) return 7;
            Hitbox h = Nearest(point);
            HaloCollisionDef.Material m = h != null ? coll.MaterialAt(h.Material) : (coll.Materials.Count > 0 ? coll.Materials[0] : null);
            if (Alive && Shield > 0.01f && MaxShield > 0f && (m == null || m.ShieldMultiplier > 0f)) return coll.ShieldMaterialType;
            return m != null ? m.Type : 7;
        }
    }
}

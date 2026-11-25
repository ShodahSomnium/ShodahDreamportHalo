using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The local player's shields and health, the multiplayer cyborg's own numbers (globals -> multiplayer information -> unit
    /// characters\cyborg_mp -> its collision model): 75 shield points and 75 body points, the shields stunned (no recharge) for the stun
    /// time after damage and refilled in the recharge time; Halo CE's body does not heal (health packs do). Damage comes in as
    /// HaloHit (the shooter's amount); the damage effect's material modifiers for the cyborg's energy shield (22) and armor (21) apply
    /// here, on the victim, because only the victim knows its shields. Headshots kill an unshielded player from a weapon whose damage can
    /// cause headshots; melee from behind and Instant hits kill outright. Over shield: up to 3 x the shield (no recharge above 1).
    /// Active camouflage: HaloLocal.CamoUntil; firing shows the player for a moment (the trigger's firing damage "active camouflage damage").
    /// Falling damage from the globals' harmful falling speeds. Death: HaloEvents.RaiseLocalDied (Match raises Killed and respawns).
    /// </summary>
    public static class HaloVitals
    {
        public static float MaxBody = 75f, MaxShield = 75f, StunTime = 6f, RechargeSeconds = 4f;
        public static int ShieldMaterial = HaloDamageDef.CyborgEnergyShield, BodyMaterial = HaloDamageDef.CyborgArmor;
        public static float Body = 75f, ShieldPoints = 75f;
        private static float _stunnedUntil;
        private static bool _rechargeSounded;
        private static int _lastAttacker = -1;
        private static HaloTag _lastCause;
        private static float _fallVy;
        private static Vector3 _lastFeet;
        private static bool _haveFeet;
        private static float _fallGraceUntil;

        /// <summary>A teleport or seat exit just moved the player: no falling damage for 1.5 s (Match / Vehicles call it).</summary>
        public static void NoteTeleport() { _fallGraceUntil = Time.time + 1.5f; _fallVy = 0f; _haveFeet = false; }
        private static float _camoShowUntil;
        private static bool _logged;
        private static HaloTag _configured;

        public static void Configure(HaloCombatCatalog cat)
        {
            if (cat == null) return;
            bool campaign = HaloCampaign.Active;
            var v = cat.VitalityFor(campaign);      // (the campaign: the player information unit cyborg; multiplayer: cyborg_mp)
            if (v == null) return;
            if (v.Tag != _configured) { _configured = v.Tag; _logged = false; }
            if (v.MaximumBody > 0f) MaxBody = v.MaximumBody;
            if (v.MaximumShield > 0f) MaxShield = v.MaximumShield;
            StunTime = v.StunTime > 0f ? v.StunTime : 2f;
            RechargeSeconds = v.FullRechargeSeconds;
            ShieldMaterial = v.ShieldMaterial;
            BodyMaterial = v.BodyMaterial;
            if (!_logged)
            {
                _logged = true;
                HaloLog.Info("Combat: vitality (" + (campaign ? "campaign player unit " + cat.PlayerUnit : "multiplayer unit " + cat.MultiplayerUnit) + ") from " + v.Tag.Name + ": body " + MaxBody + ", shield " + MaxShield + ", shields recharge " + StunTime.ToString("0.#") + " s after damage, full in " + RechargeSeconds.ToString("0.#") + " s");
            }
        }

        /// <summary>A fresh life: full shields and body, no camouflage.</summary>
        public static void Reset()
        {
            Body = MaxBody;
            ShieldPoints = HaloSession.Rules != null && !HaloSession.Rules.Shields ? 0f : MaxShield;
            _stunnedUntil = 0f;
            _rechargeSounded = true;
            _lastAttacker = -1; _lastCause = null;
            _haveFeet = false;
            _fallVy = 0f;
            HaloLocal.CamoUntil = 0f;
            Publish();
        }

        private static bool ShieldsOn => HaloSession.Rules == null || HaloSession.Rules.Shields;

        // ------------------------------------------------------------------ damage

        public static void TakeHit(HaloHit hit)
        {
            if (!HaloLocal.Alive) return;
            var cat = HaloCombat.Catalog;
            HaloDamageDef dd = hit.Effect != null && cat != null ? cat.Damage(hit.Effect) : null;
            if (hit.SourcePlayer >= 0) { _lastAttacker = hit.SourcePlayer; _lastCause = hit.Cause; }
            HaloLocal.DamagedAt = Time.time;
            HaloLocal.DamageFrom = hit.Point - hit.Direction * 2f;
            if (hit.SourcePlayer != -1 || hit.Explosion) { try { HaloHud.Damaged(HaloLocal.DamageFrom, Mathf.Clamp01(hit.Amount / Mathf.Max(1f, MaxShield))); } catch (Exception) { } }
            _camoShowUntil = Time.time + 0.6f;
            if (hit.Instant || (hit.Melee && hit.BackHit))
            {
                Die(hit, false);
                return;
            }

            if (dd != null && dd.DoesNotHurtPlayers) return;
            float amount = HaloCombat.ScaleForPlayer(Mathf.Max(0f, hit.Amount), hit.SourcePlayer);      // (an AI's hit: the level's enemy damage)
            if (amount <= 0f) return;
            float shieldMod = dd != null ? dd.Modifier(ShieldMaterial) : 1f, bodyMod = dd != null ? dd.Modifier(BodyMaterial) : 1f;
            bool hadShield = ShieldPoints > 0.01f;
            _stunnedUntil = Time.time + StunTime;
            _rechargeSounded = false;
            float rest = amount;
            if (ShieldsOn && hadShield && !(dd != null && dd.SkipsShields) && shieldMod > 0f)
            {
                float s = rest * shieldMod;
                if (s <= ShieldPoints) { ShieldPoints -= s; rest = 0f; }
                else { rest = (s - ShieldPoints) / shieldMod; ShieldPoints = 0f; HaloCombat.ShieldsDown(); }
            }

            if (dd != null && dd.OnlyHurtsShields) rest = 0f;
            // a headshot on a player without shields: dead (Halo CE's pistol and sniper rule)
            if (hit.Headshot && dd != null && dd.CanCauseHeadshots && !hadShield && rest > 0f) { Die(hit, true); return; }
            if (rest > 0f)
            {
                Body -= rest * bodyMod;
                if (Body <= 0f) { Body = 0f; Die(hit, hit.Headshot); return; }
            }

            Publish();
        }

        private static void Die(HaloHit hit, bool headshot)
        {
            Body = 0f; ShieldPoints = 0f;
            Publish();
            HaloLocal.Alive = false;
            var cat = HaloCombat.Catalog;
            HaloDamageDef dd = hit.Effect != null && cat != null ? cat.Damage(hit.Effect) : null;
            int killer = hit.SourcePlayer >= 0 ? hit.SourcePlayer : -1;
            var k = new HaloKill
            {
                Killer = killer, Victim = HaloLocal.NetId, Cause = hit.Cause,
                Melee = hit.Melee, Headshot = headshot, Splatter = dd != null && dd.Kind == HaloDamageDef.Category.Vehicle,
                Grenade = dd != null && dd.Kind == HaloDamageDef.Category.Grenade,
                Suicide = killer >= 0 && killer == HaloLocal.NetId,
            };
            HaloLog.Info("Combat: died (" + (killer >= 0 ? "killed by slot " + killer : "no killer") + (hit.Cause != null ? ", " + hit.Cause.Name : "") + (headshot ? ", headshot" : "") + (hit.Melee ? ", melee" : "") + (hit.Instant ? ", instant" : "") + ")");
            HaloCombat.OnLocalDeath(k);
            HaloEvents.RaiseLocalDied(k);
        }

        // ------------------------------------------------------------------ every frame

        public static void Tick(float dt, Vector3 feet, bool feetKnown)
        {
            if (HaloLocal.Alive)
            {
                // shields: back after the stun, then refilled in the recharge time (never above 1 by recharge)
                if (ShieldsOn && Time.time >= _stunnedUntil && ShieldPoints < MaxShield)
                {
                    if (!_rechargeSounded) { _rechargeSounded = true; HaloCombat.ShieldsRecharging(); }
                    ShieldPoints = Mathf.Min(MaxShield, ShieldPoints + MaxShield / Mathf.Max(0.5f, RechargeSeconds) * dt);
                }

                if (feetKnown) Falling(dt, feet);
            }

            // camouflage: on until CamoUntil; shown for a moment after firing or a hit
            bool camo = HaloLocal.CamoUntil > Time.time;
            HaloLocal.Camo = camo;
            float want = !camo ? 1f : Time.time < _camoShowUntil ? 0.6f : 0.04f;
            HaloLocal.CamoVisibility = Mathf.MoveTowards(HaloLocal.CamoVisibility, want, dt * 2.5f);
            Publish();
        }

        /// <summary>Firing reveals a camouflaged player for a moment (Halo: the trigger's firing damage carries "active camouflage damage").</summary>
        public static void Fired(float camoDamage)
        {
            if (HaloLocal.CamoUntil <= Time.time) return;
            _camoShowUntil = Time.time + Mathf.Clamp(0.2f + camoDamage * 2f, 0.2f, 1.5f);
        }

        private static void Falling(float dt, Vector3 feet)
        {
            if (!_haveFeet) { _lastFeet = feet; _haveFeet = true; _fallVy = 0f; return; }
            Vector3 d = feet - _lastFeet;
            _lastFeet = feet;
            if (dt <= 0f) return;
            // seated, or just out of a seat / teleported (the exit puts the avatar down in one frame): no fall for a moment
            if (HaloLocal.Vehicle != null) _fallGraceUntil = Time.time + 1.5f;
            if (Time.time < _fallGraceUntil) { _fallVy = 0f; return; }
            if (d.magnitude > 15f) { _fallVy = 0f; return; }            // a teleport (spawn, teleporter), not a fall
            float vy = d.y / dt;
            // faster downward than gravity can add in one frame (~9.8 m/s per second; generous x3 + 3 m/s slack): a jump, not a fall
            if (vy < _fallVy - 30f * dt - 3f) { _fallVy = 0f; return; }
            if (vy < _fallVy) { _fallVy = vy; return; }                 // still speeding up downward
            // the fall stopped (the vertical speed came back up by a lot within a frame): an impact at -_fallVy
            if (_fallVy < -1f && vy > _fallVy * 0.3f)
            {
                var cat = HaloCombat.Catalog;
                float speed = -_fallVy / HaloSpace.Unit;          // world units a second
                _fallVy = 0f;
                if (cat == null || cat.HarmfulFallingSpeedMin <= 0f || speed < cat.HarmfulFallingSpeedMin) return;
                if (speed >= cat.HarmfulFallingSpeedMax && cat.DistanceDamage != null)
                {
                    HaloLog.Info("Combat: a fatal fall (" + (speed * HaloSpace.Unit).ToString("0.0") + " m/s)");
                    TakeHit(new HaloHit { Amount = 1e6f, Effect = cat.DistanceDamage, SourcePlayer = -1, Point = feet, Direction = Vector3.down });
                    return;
                }

                var dd = cat.Damage(cat.FallingDamage);
                if (dd == null) return;
                float u = (speed - cat.HarmfulFallingSpeedMin) / Mathf.Max(0.01f, cat.HarmfulFallingSpeedMax - cat.HarmfulFallingSpeedMin);
                float amount = Mathf.Lerp(dd.LowerBound, dd.UpperMax > 0f ? dd.UpperMax : MaxBody, u);
                HaloLog.Info("Combat: falling damage " + amount.ToString("0") + " (" + (speed * HaloSpace.Unit).ToString("0.0") + " m/s)");
                TakeHit(new HaloHit { Amount = amount, Effect = cat.FallingDamage, SourcePlayer = -1, Point = feet, Direction = Vector3.down });
                return;
            }

            if (vy > -0.5f) _fallVy = 0f;
        }

        // ------------------------------------------------------------------ powerups

        public static void HealthPack()
        {
            Body = MaxBody;
            Publish();
        }

        public static void OverShield()
        {
            ShieldPoints = MaxShield * 3f;
            _stunnedUntil = 0f;
            Publish();
        }

        public static void Camouflage(float seconds)
        {
            HaloLocal.CamoUntil = Time.time + Mathf.Max(1f, seconds);
        }

        private static void Publish()
        {
            HaloLocal.Health = MaxBody > 0f ? Mathf.Clamp01(Body / MaxBody) : 0f;
            HaloLocal.Shield = MaxShield > 0f ? Mathf.Clamp(ShieldPoints / MaxShield, 0f, 3f) : 0f;
            HaloLocal.ShieldRecharging = HaloLocal.Alive && ShieldsOn && Time.time >= _stunnedUntil && ShieldPoints < MaxShield;
            HaloLocal.ShieldLow = HaloLocal.Alive && ShieldsOn && ShieldPoints < MaxShield * 0.25f;
            HaloLocal.OverShield = ShieldPoints > MaxShield + 0.01f;
        }
    }
}

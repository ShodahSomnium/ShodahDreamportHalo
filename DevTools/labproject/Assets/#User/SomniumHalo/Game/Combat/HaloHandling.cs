using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The local player's weapons in the hands, ported from the Black Ops 2 Dreamport's Bo2Handling and fitted to Halo: two weapons at
    /// most, carried on the body (long guns over the shoulders, small guns on the hips: <see cref="HaloGun.Holster"/>), grenades on the
    /// chest (frags left, plasma right), ammunition in a belt pouch; drawing with the grip, one or two hands on the gun (two-handed aim
    /// along the line between the hands for the long guns), firing with the trigger (<see cref="HaloWeaponState"/> from the weap tag),
    /// reloading by hand per weapon family, physical melee with the gun or the fist, throwing grenades with the hand's velocity, taking
    /// and dropping weapons. On a monitor the same gear runs on keys and the mouse (HaloHandling.Desk.cs). It is the combat module's one
    /// MonoBehaviour ("Halo Combat"): it also ticks the projectiles, the pickups and the local vitals.
    /// Partial files: Hands (VR grabbing and poses), Holsters (where things are carried), Reload, Melee, Desk.
    /// </summary>
    public sealed partial class HaloHandling : MonoBehaviour
    {
        public static HaloHandling Current { get; private set; }

        public readonly HaloBody Body = new HaloBody();
        public readonly HaloGun[] Guns = new HaloGun[2];
        private Transform _gear;

        private int _held = -1;                  // index into Guns of the gun in a hand (-1 = none)
        private int _lastHeld;                   // the last one drawn (the HUD's weapon while both are holstered)
        private int _gunHand = -1;               // 0 left, 1 right: the hand on the grip
        private int _supportHand = -1;
        private enum Support { None, Handguard, Rack, Cup }
        private Support _support;
        private Vector3 _supportGrabLocal;
        private bool _rackPulled;
        private float _twoHandBlend;
        private bool _desktop;
        private readonly HaloWeaponState.Shot[] _shots = new HaloWeaponState.Shot[16];
        private bool _wasCharging;
        private float _nextDryAt;

        public bool Desktop => _desktop;
        public HaloGun HeldGun => _held >= 0 ? Guns[_held] : null;
        public int GunHand => _gunHand;
        public bool TwoHanded => _held >= 0 && _supportHand >= 0 && _support == Support.Handguard;
        public Transform Gear => _gear;
        public Transform Anchor { get; private set; }

        public static HaloHandling Create(Transform parent)
        {
            var go = new GameObject("Halo Combat");
            if (parent != null) go.transform.SetParent(parent, false);
            var h = go.AddComponent<HaloHandling>();
            h._gear = new GameObject("Gear").transform;
            h._gear.SetParent(go.transform, false);
            h.Anchor = new GameObject("Local body anchor").transform;      // (what sticks to the local player: a plasma grenade from the AI)
            h.Anchor.SetParent(go.transform, false);
            Current = h;
            return h;
        }

        // ------------------------------------------------------------------ the carried weapons

        public int Carried { get { int n = 0; for (int i = 0; i < 2; i++) if (Guns[i] != null) n++; return n; } }

        public int IndexOf(HaloGun g) { for (int i = 0; i < 2; i++) if (Guns[i] == g && g != null) return i; return -1; }

        /// <summary>Takes a weapon onto the body (a free holster of its kind). Returns its index, -1 when both places are taken.</summary>
        public int Carry(HaloGun g)
        {
            int i = Guns[0] == null ? 0 : Guns[1] == null ? 1 : -1;
            if (i < 0 || g == null) return -1;
            Guns[i] = g;
            g.transform.SetParent(_gear, true);
            g.Slot = FreeHolster(g, i);
            g.ResetModel();
            if (HaloLocal.Weapons[i] == null) HaloLocal.Weapons[i] = new HaloLocal.Weapon();
            g.Info = HaloLocal.Weapons[i];
            g.SyncInfo();
            if (_held < 0 && (Guns[_lastHeld] == null || _lastHeld == i)) _lastHeld = i;      // (the first weapon carried stays the one drawn first)
            HaloLog.Info("Combat: carrying " + g.Def.Name + " (" + g.Slot + ", " + g.State.Loaded + "/" + g.State.Reserve + ")");
            return i;
        }

        /// <summary>The weapon leaves the body (dropped / swapped): no longer carried, not destroyed.</summary>
        public void Release(HaloGun g)
        {
            int i = IndexOf(g);
            if (i < 0) return;
            if (_held == i) { EndSupport(); _held = -1; _gunHand = -1; }
            g.StopFx();
            Guns[i] = null;
            HaloLocal.Weapons[i] = null;
            g.Info = null;
            g.Slot = HaloGun.Holster.None;
            if (_lastHeld == i) _lastHeld = Guns[1 - i] != null ? 1 - i : 0;
        }

        /// <summary>A gun builds for a weapon tag and goes onto the body (the starting weapons).</summary>
        public HaloGun Give(HaloTag weapon)
        {
            var def = HaloCombat.Catalog != null ? HaloCombat.Catalog.Weapon(weapon) : null;
            if (def == null) return null;
            var g = HaloGun.Create(def, _gear);
            if (g == null) { HaloLog.Warn("Combat: no model for " + weapon); return null; }
            if (Carry(g) < 0) { Destroy(g.gameObject); return null; }
            return g;
        }

        /// <summary>Everything goes: guns destroyed, hands empty (death drops them first; a match end just removes them).</summary>
        public void Unequip()
        {
            DropEverythingInHands(false);
            for (int i = 0; i < 2; i++)
            {
                if (Guns[i] != null) Destroy(Guns[i].gameObject);
                Guns[i] = null;
                HaloLocal.Weapons[i] = null;
            }

            _held = -1; _lastHeld = 0; _gunHand = _supportHand = -1;
            HaloLocal.InHand = false;
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            float dt = Time.deltaTime;
            HaloXr.Tick();
            bool bodyOk = Body.Update();
            _desktop = !HaloXr.Present && !Body.Vr;
            Vector3 feet = bodyOk ? Body.Feet : Vector3.zero;
            if (bodyOk) { HaloLocal.Position = feet; }
            HaloVitals.Tick(dt, feet, bodyOk);
            HaloProjectiles.Tick(dt);
            HaloPickups.Tick(dt, this);
            if (!bodyOk) return;
            if (Anchor != null) Anchor.SetPositionAndRotation(Body.Chest, Quaternion.LookRotation(Body.Forward, Vector3.up));

            float scale = Body.AvatarScale;
            for (int i = 0; i < 2; i++)
            {
                HaloGun gi = Guns[i];
                if (gi == null) continue;
                gi.SetScale(scale); gi.ResetModel();
                gi.HandsVr = !_desktop && i == _held;
                if (i != _held) { gi.State.Step(dt, false, false, _shots); HeatAnimation(gi); }      // (a holstered plasma weapon cools too)
                gi.StepParts(dt);
            }
            HaloXr.HapticsOn = HaloSettings.Haptics;

            HaloHand L = HaloXr.Left, R = HaloXr.Right;
            bool canFight = HaloLocal.Alive && HaloSession.Current != HaloSession.Phase.Ending;
            bool seated = HaloLocal.Vehicle != null && !HaloLocal.SeatAllowsWeapons;
            UpdateStowed();
            if (!canFight || seated)
            {
                DropEverythingInHands(false);
                if (!_desktop) HaloTabletGuard.Update(false);
                SyncLocal();
                return;
            }

            // the game's actions are held (script player_enable_input false, a cutscene): nothing fires, throws, swings, reloads or picks up -
            // the hands and what they hold still follow the player
            bool inputHeld = !HaloCombat.InputEnabled || CinematicPlaying();
            if (inputHeld)
            {
                HaloGun hg = HeldGun;
                if (hg != null) Trigger(hg, false, dt);
                HaloLocal.PickupPrompt = "";
            }
            else
            {
                if (_objective != null) StepObjective(dt);
                if (_desktop) { if (_objective == null) StepDesktop(dt); }
                else
                {
                    Hands(L, R, dt);
                    HaloTabletGuard.Update(_held >= 0 || _heldMag != null || _grenadeHand >= 0);
                }
            }

            UpdateHeldPoses(dt);
            HaloGun aimGun = HeldGun;
            if (aimGun != null) HaloHud.SetAim(aimGun.MuzzlePosition, aimGun.Muzzle != null ? aimGun.Muzzle.right : aimGun.Barrel);
            if (!inputHeld) StepMelee(L, R, dt);
            if (!inputHeld) StepFlashlight(L, R);
            StepReloadTimers(dt);
            SyncLocal();
        }

        /// <summary>HaloLocal from the guns: the HUD and the net record read it.</summary>
        private void SyncLocal()
        {
            for (int i = 0; i < 2; i++)
            {
                if (Guns[i] != null) { if (HaloLocal.Weapons[i] == null) HaloLocal.Weapons[i] = new HaloLocal.Weapon(); Guns[i].Info = HaloLocal.Weapons[i]; Guns[i].SyncInfo(); }
                else HaloLocal.Weapons[i] = null;
            }

            HaloLocal.InHand = _held >= 0;
            HaloLocal.Held = _held >= 0 ? _held : (Guns[_lastHeld] != null ? _lastHeld : (Guns[0] != null ? 0 : Guns[1] != null ? 1 : -1));
            HaloGun g = HeldGun;
            HaloLocal.Charge = g != null ? g.State.Charge : 0f;
            HaloLocal.Reloading = g != null && (!g.MagIn || _deskReload >= 0f || _shellsInHand > 0);
            Transform model = _objective != null ? _objective.ModelRoot : g != null ? g.ModelRoot : null;
            HaloCombat.HeldModel = model;
            HaloDesk.HeldWeapon = model;
        }

        /// <summary>Whatever is in the hands goes: a gun to its holster, a magazine falls, a grenade back on the chest.</summary>
        private void DropEverythingInHands(bool silent)
        {
            if (_held >= 0) { Holster(Guns[_held]); }
            EndSupport();
            if (_heldMag != null) { DropHeldMag(); }
            if (_grenadeHand >= 0) { PutGrenadeBack(); }
            _gunHand = -1;
            _held = -1;
            _shellHand = -1;
            HaloLocal.Zoomed = false; HaloLocal.ZoomLevel = 0; HaloLocal.ZoomMagnification = 1f;
        }

        // ------------------------------------------------------------------ firing

        /// <summary>The Somnium tablet is open (Front sets it): no firing then.</summary>
        public static bool TabletBlocksFire => HaloCombat.TabletOpen;

        /// <summary>One frame of the held gun's trigger.</summary>
        private void Trigger(HaloGun g, bool down, float dt)
        {
            bool allowed = !TabletBlocksFire && g.MagIn && !g.NeedsRack && HaloLocal.Objective == null && Time.time - g.OverheatedAt >= g.Def.OverheatSeconds;
            bool zoomed = HaloLocal.Zoomed;
            int n = g.State.Step(dt, down && allowed, zoomed, _shots);
            for (int i = 0; i < n; i++) FireShot(g, _shots[i]);
            var t = g.Def.PrimaryTrigger;
            if (t == null) return;
            // a click with nothing to fire (empty magazine, needs a rack, flat battery)
            bool click = (g.State.JustDry || (down && !allowed && _gunHandPressed)) && Time.time >= _nextDryAt;
            if (click)
            {
                _nextDryAt = Time.time + 0.3f;
                var fe = t.EffectFor(1, null);
                if (fe != null && fe.Empty != null) HaloCombatFx.OnModel(fe.Empty, g.ModelRoot, true);
                if (_gunHand >= 0) HaloXr.Pulse(_gunHand == 1, 0.15f, 0.03f);
            }

            if (g.State.JustOverheated)
            {
                g.OverheatedAt = Time.time;
                HaloLog.Info("Combat: " + g.Def.Name + " overheated (vents until heat " + g.Def.HeatRecoveryThreshold.ToString("0.##") + ")");
                if (_gunHand >= 0) HaloXr.Pulse(_gunHand == 1, 0.5f, 0.25f);
            }

            HeatAnimation(g);

            // (the plasma pistol has no trigger charging effect: its charge is the overcharge light + charging.lsnd, see HaloGunFx)
            if (g.State.Charging && !_wasCharging && t.ChargingEffect != null) HaloCombatFx.OnModel(t.ChargingEffect, g.ModelRoot, true);
            if (g.State.Charging && _gunHand >= 0) HaloXr.Pulse(_gunHand == 1, 0.08f + 0.25f * g.State.Charge, 0.02f);
            _wasCharging = g.State.Charging;
        }

        private bool _gunHandPressed;

        private static bool CinematicPlaying() { try { return HaloCinematic.Playing; } catch (Exception) { return false; } }

        /// <summary>The vents open with the overheat and close when it cools (Halo's overheating / overheated / overheat-exit animations);
        /// the weapon's overheated effect loops while it is overheated (HaloGunFx); the charge glow follows the trigger's charge.</summary>
        private static void HeatAnimation(HaloGun g)
        {
            if (g.State.JustOverheated) { g.Animate("overheating"); g.AnimateState("overheated", true); if (g.Sounds != null) g.Sound(g.Sounds.Vent, "vent"); }
            if (g.State.JustRecovered) { g.AnimateState("overheated", false); g.Animate("overheat-exit"); if (g.Sounds != null) g.Sound(g.Sounds.VentExit, "vent closes"); }
            if (g.State.Overheated || g.Fx.Venting) g.Fx.SetVent(g.State.Overheated);
            if (g.State.Charging || g.Fx.Charging) g.Fx.SetCharge(g.State.Charging ? g.State.Charge : -1f);
        }

        private void FireShot(HaloGun g, HaloWeaponState.Shot shot)
        {
            var cat = HaloCombat.Catalog;
            var t = g.Def.Triggers[shot.Trigger];
            var pd = cat != null ? cat.Projectile(t.Projectile) : null;
            Transform muzzle = g.NextMuzzle();
            Vector3 origin = muzzle.position;
            Vector3 dir = muzzle.right;                     // a marker's +X is the barrel (Halo +X)
            if (_desktop) DeskAim(origin, out dir);
            // the firing effect at the muzzle (flash, sound, shell)
            var fe = t.EffectFor(shot.Count, null);
            if (fe != null && fe.Firing != null) HaloCombatFx.OnModel(fe.Firing, g.ModelRoot, true);
            if (fe != null && fe.FiringDamage != null)
            {
                var fd = cat != null ? cat.Damage(fe.FiringDamage) : null;
                HaloVitals.Fired(fd != null ? fd.ActiveCamouflageDamage : 0.2f);
            }

            if (pd != null)
            {
                int count = Mathf.Max(1, t.ProjectilesPerShot);
                Transform target = pd.GuidedAngularVelocity > 0f ? FindGuideTarget(origin, dir, g.Def) : null;
                Vector3 same = Cone(dir, shot.ErrorAngle);
                for (int k = 0; k < count; k++)
                {
                    Vector3 d;
                    if (t.Distribution == 1 && count > 1)
                    {
                        // a horizontal fan across the distribution angle
                        float u = count > 1 ? k / (float)(count - 1) - 0.5f : 0f;
                        d = Quaternion.AngleAxis(u * t.DistributionAngle * Mathf.Rad2Deg, Vector3.up) * dir;
                        d = Cone(d, shot.ErrorAngle);
                    }
                    else d = t.IdenticalError ? same : Cone(dir, shot.ErrorAngle);
                    HaloProjectiles.Launch(pd, origin, d, Vector3.zero, HaloLocal.NetId, g.Def.Tag, true, true, null, target);
                }
            }

            HaloEvents.RaiseFired(g.Def.Tag, origin, dir, HaloLocal.NetId);
            // Halo's own weapon animation of the shot (pistol slide, sniper bolt; the shotgun pumps); a simple slide cycle without one
            bool anim = g.Def.Kind == HaloWeaponDef.Family.Shotgun ? (g.Animate("chamber") || g.Animate("fire")) : g.Animate(shot.Charged ? "charged" : "fire") || (shot.Charged && g.Animate("fire"));
            if (!anim) g.CycleSlide();
            float kick = g.KickStrength() * (shot.Charged ? 1.8f : 1f);
            g.Kick(kick * HaloCombat.RecoilScale);
            if (_gunHand >= 0) HaloXr.Pulse(_gunHand == 1, Mathf.Clamp01(0.25f + 0.35f * kick), 0.04f + 0.03f * kick);
            if (_supportHand >= 0 && _support == Support.Handguard) HaloXr.Pulse(_supportHand == 1, Mathf.Clamp01(0.15f + 0.25f * kick), 0.04f);
        }

        /// <summary>A random direction within <paramref name="angle"/> radians of <paramref name="dir"/> (uniform over the cone's disc).</summary>
        public static Vector3 Cone(Vector3 dir, float angle)
        {
            if (angle <= 0f) return dir;
            Vector2 r = UnityEngine.Random.insideUnitCircle * Mathf.Tan(angle);
            Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.95f ? Vector3.up : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(side, dir);
            return (dir + side * r.x + up * r.y).normalized;
        }

        private static readonly Collider[] GuideHits = new Collider[32];

        /// <summary>A guided projectile's target: the hit box closest to the aim within the weapon's autoaim angle (6 degrees at least) and range.</summary>
        private Transform FindGuideTarget(Vector3 origin, Vector3 dir, HaloWeaponDef def)
        {
            float range = Mathf.Max(5f, def.AutoaimRange) * HaloSpace.Unit;
            float maxAngle = Mathf.Max(6f, def.AutoaimAngle * Mathf.Rad2Deg);
            int n = Physics.OverlapSphereNonAlloc(origin, range, GuideHits, (1 << HaloLayers.Hitbox) | (1 << HaloLayers.Vehicle), QueryTriggerInteraction.Collide);
            Transform best = null;
            float ba = maxAngle;
            for (int i = 0; i < n; i++)
            {
                Collider c = GuideHits[i];
                if (c == null || HaloCombat.IsOwnCollider(c)) continue;
                if (c.GetComponentInParent<IHaloDamageable>() == null) continue;
                IHaloHitInfo info = c.GetComponentInParent<IHaloHitInfo>();
                if (info != null && !info.HitAlive) continue;
                Vector3 to = c.bounds.center - origin;
                float a = Vector3.Angle(dir, to);
                if (a < ba) { ba = a; best = c.transform; }
            }

            return best;
        }

        private void OnDestroy()
        {
            HaloTabletGuard.Release();
            if (Current == this) Current = null;
        }
    }
}

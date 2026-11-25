using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Combat module] The local player's fighting: weapons in VR / on a monitor, grenades, melee, damage, pickups, powerups.
    /// Entry points (Match): <see cref="Spawned"/> after every spawn teleport (the first one of a session also lays out the map's items),
    /// <see cref="TakeHit"/> for damage, <see cref="Clear"/> at game over / leave. The work happens in one MonoBehaviour,
    /// <see cref="HaloHandling"/> ("Halo Combat", made on the first Spawned under the Halo Game object). APIs for other modules:
    /// - Vehicles: <see cref="FireProjectile"/> (the game's projectile with damage, effects, material responses), <see cref="Catalog"/>
    ///   (weapon / projectile / damage tags parsed), HaloWeaponState (Data/Combat: a weapon's firing mechanics).
    /// - Net: <see cref="ReplayShot"/>, <see cref="ReplayThrow"/> (visual-only replays), the events <see cref="Thrown"/>,
    ///   <see cref="Meleed"/>, <see cref="Reloaded"/>, HaloPickups.Taken / LocalDropped / ApplyRemoteTaken / RemoteDrop, hits through
    ///   IHaloDamageable (+ optional IHaloHitInfo), HaloEvents.RaiseFired for every local shot.
    /// - Match: <see cref="CarryObjective"/> / <see cref="DropObjective"/> / <see cref="ObjectiveDropped"/> / <see cref="CarryingObjective"/>.
    /// - Desk: <see cref="HeldModel"/> (the held weapon's third-person model root).
    /// </summary>
    public static class HaloCombat
    {
        private static HaloCombatCatalog _catalog;
        private static HaloMap _catalogMap;
        private static bool _hooked;

        /// <summary>The lab's stand-in for HaloSession.Map (editor only; null in the world).</summary>
        public static HaloMap LabMap;
        public static HaloScenario LabScenario;

        /// <summary>The Somnium tablet is open (the Front module sets it): the trigger does nothing then.</summary>
        public static bool TabletOpen;
        /// <summary>Settings "combat.assisted": no rack after an empty reload; A chambers.</summary>
        public static bool AssistedReload { get { return HaloSettings.Flag("combat.assisted", false); } }
        public static float RecoilScale { get { return HaloSettings.Num("combat.recoil", 1f); } }
        /// <summary>The barrel this far above where the hand points (degrees; settings "combat.grippitch", 0 = along the client's laser).</summary>
        public static float GripPitchDegrees { get { return HaloSettings.Num("combat.grippitch", 0f); } }
        /// <summary>Show the holstered weapons on the body on a monitor (off: the camera-built body floats in third person).</summary>
        public static bool ShowStowedOnMonitor = false;

        /// <summary>The held weapon's third-person model root (HaloModelBuilder's root: origin = the weap's model origin, barrel +X), null when none.</summary>
        public static Transform HeldModel;

        public static HaloHandling Handling => HaloHandling.Current;

        /// <summary>The current map's combat tags, parsed (null outside a match).</summary>
        public static HaloCombatCatalog Catalog
        {
            get
            {
                HaloMap map = HaloSession.Map ?? LabMap;
                if (map == null) return null;
                if (_catalog == null || _catalogMap != map)
                {
                    try
                    {
                        _catalog = new HaloCombatCatalog(map);
                        _catalogMap = map;
                        HaloVitals.Configure(_catalog);
                        HaloLocal.FragMax = _catalog.Grenades.Count > 0 ? _catalog.Grenades[0].MaximumCount : 4;
                        HaloLocal.PlasmaMax = _catalog.Grenades.Count > 1 ? _catalog.Grenades[1].MaximumCount : 4;
                        HaloLog.Info("Combat: " + map.Name + ": " + _catalog.WeaponList.Count + " weapons in the globals' list, grenades " + HaloLocal.FragMax + "/" + HaloLocal.PlasmaMax + " (spawn with " + (_catalog.Grenades.Count > 0 ? _catalog.Grenades[0].MultiplayerSpawn : 0) + "/" + (_catalog.Grenades.Count > 1 ? _catalog.Grenades[1].MultiplayerSpawn : 0) + "), unit " + _catalog.MultiplayerUnit);
                    }
                    catch (Exception e)
                    {
                        HaloLog.Error("Combat: reading " + map.Name + "'s combat tags failed: " + e);
                        _catalog = null;
                        _catalogMap = map;
                    }
                }

                return _catalog;
            }
        }

        private static HaloScenario Scenario => HaloSession.Scenario ?? LabScenario;

        // ------------------------------------------------------------------ the stub's API (Match)

        /// <summary>A fresh life: the starting weapons for the rules' weapon set, the globals' grenades, full shields.</summary>
        public static void Spawned()
        {
            var h = EnsureDriver();
            var cat = Catalog;
            if (h == null || cat == null) { HaloLog.Warn("Combat: Spawned without a map"); return; }
            bool campaign = HaloCampaign.Active;
            if (!campaign) EnsureItems();      // (the campaign: no netgame items; the mission places its own weapons, one-off)
            HaloRules rules = HaloSession.Rules ?? new HaloRules();
            HaloGun.SizeFactor = Mathf.Clamp(HaloSettings.Num("combat.gunscale", 1f), 0.5f, 1.5f);
            h.DropObjective(Vector3.zero, false);
            h.Unequip();
            HaloLocal.Alive = true;
            HaloVitals.Configure(cat);      // (campaign or multiplayer unit: the session may have changed since the map was read)
            HaloVitals.Reset();
            HaloLocal.Zoomed = false; HaloLocal.ZoomLevel = 0; HaloLocal.ZoomMagnification = 1f;
            List<HaloTag> start = campaign ? new List<HaloTag>() : StartingWeapons(rules.WeaponSet);      // (the campaign: the mission gives its starting profile)
            for (int i = 0; i < start.Count && i < 2; i++) h.Give(start[i]);
            HaloLocal.FragGrenades = rules.Grenades && cat.Grenades.Count > 0 ? cat.Grenades[0].MultiplayerSpawn : 0;
            HaloLocal.PlasmaGrenades = rules.Grenades && cat.Grenades.Count > 1 ? cat.Grenades[1].MultiplayerSpawn : 0;
            HaloLocal.GrenadeSelected = HaloLocal.FragGrenades > 0 || HaloLocal.PlasmaGrenades == 0 ? 0 : 1;
            HaloLocal.Held = h.Guns[0] != null ? 0 : -1;
            HaloLog.Info("Combat: spawned with " + Describe(h) + ", " + HaloLocal.FragGrenades + " frag / " + HaloLocal.PlasmaGrenades + " plasma, shields " + HaloVitals.MaxShield + " body " + HaloVitals.MaxBody + (HaloDesk.Active ? " (monitor)" : " (VR)"));
        }

        /// <summary>Damage to the local player (a remote shooter through the net layer, a vehicle, the world, our own blast).</summary>
        public static void TakeHit(HaloHit hit)
        {
            try { HaloVitals.TakeHit(hit); }
            catch (Exception e) { HaloLog.Error("Combat: TakeHit failed: " + e); }
        }

        /// <summary>The match ended / the player left: everything in the hands and on the body goes, projectiles end.</summary>
        /// <summary>Vehicles / Match: the player was just moved (seat exit, spawn, teleporter) - no falling damage for a moment.</summary>
        public static void NoteTeleport() { HaloVitals.NoteTeleport(); }

        public static void Clear()
        {
            var h = HaloHandling.Current;
            if (h != null) { h.DropObjective(Vector3.zero, false); h.Unequip(); h.ForgetMapVisuals(); h.ForgetFlashlight(); }
            HaloGunFx.ClearKeyed();
            HaloProjectiles.Clear();
            HaloLocal.FragGrenades = HaloLocal.PlasmaGrenades = 0;
            HaloLocal.Zoomed = false; HaloLocal.ZoomMagnification = 1f;
            HaloLocal.PickupPrompt = "";
            HeldModel = null;
            if (HaloSession.Current == HaloSession.Phase.Hub) HaloPickups.Clear();
        }

        /// <summary>The name the game shows for a weapon / equipment tag ("Assault Rifle"), from the HUD item strings.</summary>
        public static string Label(HaloTag item)
        {
            if (item == null) return "";
            var cat = Catalog;
            if (cat != null)
            {
                if (item.Is("weap")) { var d = cat.Weapon(item); if (d != null && !string.IsNullOrEmpty(d.Name)) return d.Name; }
                if (item.Is("eqip")) { var e = cat.Equipment(item); if (e != null && !string.IsNullOrEmpty(e.Name)) return e.Name; }
            }

            return System.IO.Path.GetFileName(item.Name);
        }

        // ------------------------------------------------------------------ setup

        private static HaloHandling EnsureDriver()
        {
            if (HaloHandling.Current != null) return HaloHandling.Current;
            Transform parent = HaloGame.Current != null ? HaloGame.Current.transform : null;
            var h = HaloHandling.Create(parent);
            if (!_hooked)
            {
                _hooked = true;
                HaloSession.Unloading += OnUnloading;
                HaloProjectiles.HitDealt += (v, hit) => { HaloLocal.HitDealtAt = Time.time; };
            }

            HaloLog.Info("Combat: ready (" + (HaloPlayer.XrPresent ? "headset" : "monitor") + ")");
            return h;
        }

        /// <summary>The map's items, laid out once per session (the first spawn after HaloSession.WorldBuilt).</summary>
        private static void EnsureItems()
        {
            var s = Scenario;
            if (s == null || HaloPickups.PlacedFor(s)) return;
            HaloPickups.Place(s, HaloSession.Rules);
        }

        private static void OnUnloading()
        {
            try
            {
                Clear();
                HaloPickups.Clear();
                HaloProjectiles.Clear();
            }
            catch (Exception e) { HaloLog.Warn("Combat: unloading: " + e.Message); }
        }

        private static string Describe(HaloHandling h)
        {
            string s = "";
            for (int i = 0; i < 2; i++) if (h.Guns[i] != null) s += (s.Length > 0 ? " + " : "") + h.Guns[i].Def.Name;
            return s.Length > 0 ? s : "no weapons";
        }

        // ------------------------------------------------------------------ weapon sets (ours: Halo's game variant options, by weapon name)

        /// <summary>A map weapon by its file name ("rocket launcher" -> weapons\rocket launcher\rocket launcher), null when the map has none.</summary>
        public static HaloTag FindWeapon(string file)
        {
            var cat = Catalog;
            if (cat == null) return null;
            foreach (var t in cat.Map.OfClass("weap"))
                if (string.Equals(System.IO.Path.GetFileName(t.Name), file, StringComparison.OrdinalIgnoreCase) && t.Root.Ref(40) != null) return t;
            return null;
        }

        /// <summary>The weapons a player spawns with: "normal" = the multiplayer unit's own (assault rifle + pistol), else the set's pair.</summary>
        public static List<HaloTag> StartingWeapons(int set)
        {
            var list = new List<HaloTag>();
            var cat = Catalog;
            if (cat == null) return list;
            string a = null, b = null;
            switch (set)
            {
                case 1: a = "pistol"; break;
                case 2: a = "assault rifle"; b = "sniper rifle"; break;
                case 3: a = "plasma rifle"; b = "plasma pistol"; break;
                case 4: a = "sniper rifle"; b = "pistol"; break;
                case 6: a = "rocket launcher"; b = "pistol"; break;
                case 7: a = "shotgun"; b = "pistol"; break;
                case 8: a = "shotgun"; b = "plasma pistol"; break;
                case 10: a = "plasma rifle"; b = "needler"; break;
                case 12: a = "rocket launcher"; b = "plasma_cannon"; break;
            }

            if (a == null) { foreach (var w in cat.UnitWeapons) list.Add(w); return list; }
            HaloTag ta = FindWeapon(a), tb = b != null ? FindWeapon(b) : null;
            if (ta != null) list.Add(ta);
            if (tb != null) list.Add(tb);
            if (list.Count == 0) foreach (var w in cat.UnitWeapons) list.Add(w);
            return list;
        }

        /// <summary>A map spot's weapon under the rules' weapon set (null = none on that spot).</summary>
        public static HaloTag MapWeapon(HaloTag weap, int set)
        {
            if (weap == null || set == 0) return weap;
            string n = System.IO.Path.GetFileName(weap.Name).ToLowerInvariant();
            bool covenant = n.Contains("plasma") || n.Contains("needler");
            switch (set)
            {
                case 1: return FindWeapon("pistol") ?? weap;
                case 2: return n == "sniper rifle" ? weap : FindWeapon("assault rifle") ?? weap;
                case 3: case 10: return covenant ? weap : FindWeapon(CovenantFor(n)) ?? weap;
                case 4: return FindWeapon("sniper rifle") ?? weap;
                case 5: return n == "sniper rifle" ? null : weap;
                case 6: return FindWeapon("rocket launcher") ?? weap;
                case 7: return FindWeapon("shotgun") ?? weap;
                case 8: return n == "sniper rifle" || n == "rocket launcher" || n == "plasma_cannon" ? FindWeapon("shotgun") ?? weap : weap;
                case 9: return covenant ? FindWeapon(HumanFor(n)) ?? weap : weap;
                case 11: return n == "flamethrower" || n == "plasma_cannon" ? null : weap;
                case 12: return FindWeapon(weap.Index % 2 == 0 ? "rocket launcher" : "plasma_cannon") ?? FindWeapon("rocket launcher") ?? weap;
                default: return weap;
            }
        }

        private static string CovenantFor(string human)
        {
            switch (human)
            {
                case "pistol": return "plasma pistol";
                case "shotgun": case "sniper rifle": return "needler";
                case "rocket launcher": return "plasma_cannon";
                default: return "plasma rifle";
            }
        }

        private static string HumanFor(string cov)
        {
            switch (cov)
            {
                case "plasma pistol": return "pistol";
                case "plasma_cannon": return "rocket launcher";
                case "needler": return "shotgun";
                default: return "assault rifle";
            }
        }

        // ------------------------------------------------------------------ APIs for other modules

        /// <summary>
        /// Vehicles: flies the game's projectile (impact / detonation damage, effects, material responses). <paramref name="velocity"/> in m/s
        /// (zero = the tag's own speed along <paramref name="direction"/>); <paramref name="ignoreRoot"/> = the firing vehicle (its colliders
        /// are not hit). <paramref name="visualOnly"/> = a replay of another player's shot (no damage).
        /// </summary>
        public static HaloProjectiles.Flight FireProjectile(HaloTag projectile, Vector3 position, Vector3 velocity, int shooterSlot, HaloTag cause, Transform ignoreRoot, Vector3 direction = default(Vector3), bool visualOnly = false)
        {
            var cat = Catalog;
            var pd = cat != null ? cat.Projectile(projectile) : null;
            if (pd == null) return null;
            EnsureDriver();
            bool local = shooterSlot != AiShooter && (shooterSlot == HaloLocal.NetId || shooterSlot == -1);      // (AI shots: not ours - they hit the local player)
            return HaloProjectiles.Launch(pd, position, direction.sqrMagnitude > 1e-8f ? direction : velocity, velocity, shooterSlot, cause, !visualOnly, local && !visualOnly, ignoreRoot, null);
        }

        /// <summary>Net: another player's shot, replayed for the eyes and ears (firing effect at the muzzle, the projectiles fly, no damage).</summary>
        public static void ReplayShot(HaloTag weapon, Vector3 muzzle, Vector3 dir, int shooterSlot, Transform muzzleT)
        {
            var cat = Catalog;
            var def = cat != null ? cat.Weapon(weapon) : null;
            if (def == null || def.PrimaryTrigger == null) return;
            EnsureDriver();
            var t = def.PrimaryTrigger;
            var fe = t.EffectFor(1, null);
            if (fe != null && fe.Firing != null) HaloCombatFx.Play(fe.Firing, muzzle, Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir : Vector3.forward), muzzleT);
            var pd = cat.Projectile(t.Projectile);
            if (pd == null) return;
            int n = Mathf.Max(1, t.ProjectilesPerShot);
            float err = t.ErrorAngleMin + t.MinimumError;
            for (int i = 0; i < n; i++) HaloProjectiles.Launch(pd, muzzle, HaloHandling.Cone(dir, n > 1 ? t.ErrorAngleMax : err), Vector3.zero, shooterSlot, weapon, false, false, null, null);
        }

        /// <summary>Net: another player's grenade, flown for the eyes and ears (their client decides its blast).</summary>
        public static void ReplayThrow(HaloTag grenade, Vector3 pos, Vector3 vel, int throwerSlot)
        {
            var cat = Catalog;
            if (cat == null || grenade == null) return;
            HaloTag proj = grenade;
            if (grenade.Is("eqip")) foreach (var g in cat.Grenades) if (g.Equipment == grenade) proj = g.Projectile;
            var pd = cat.Projectile(proj);
            if (pd == null) return;
            EnsureDriver();
            HaloProjectiles.Launch(pd, pos, vel, vel, throwerSlot, proj, false, false, null, null);
        }

        /// <summary>Raised for the net record and the Desk module's puppet.</summary>
        public static event Action<HaloTag, Vector3, Vector3> Thrown;
        public static event Action Meleed;
        public static event Action Reloaded;
        /// <summary>The objective (flag / ball weapon tag) left the hand at a point with a velocity.</summary>
        public static event Action<HaloTag, Vector3, Vector3> ObjectiveDropped;

        public static void NoteThrow(HaloTag projectile, Vector3 pos, Vector3 vel) { HaloDesk.NoteThrow(); Raise3(Thrown, projectile, pos, vel); }
        public static void NoteMelee() { HaloDesk.NoteMelee(); Raise0(Meleed); }
        public static void NoteReload() { HaloDesk.NoteReload(); Raise0(Reloaded); }
        public static void RaiseObjectiveDropped(HaloTag t, Vector3 at, Vector3 vel) { Raise3(ObjectiveDropped, t, at, vel); }

        // ------------------------------------------------------------------ the objective (Match)

        public static bool CarryingObjective => HaloHandling.Current != null && HaloHandling.Current.CarryingObjective;

        /// <summary>The flag / oddball weapon into the right hand as the held weapon (no firing, melee allowed, guns stowed).</summary>
        public static void CarryObjective(HaloTag weapon)
        {
            var h = EnsureDriver();
            if (h != null && weapon != null) h.CarryObjective(weapon);
        }

        /// <summary>Takes the objective out of the hand (Match decided: captured, returned, died) without raising ObjectiveDropped.</summary>
        public static void DropObjective()
        {
            var h = HaloHandling.Current;
            if (h != null) h.DropObjective(Vector3.zero, false);
        }

        // ------------------------------------------------------------------ the campaign (Script S, Mission K, Characters C, AI I)

        /// <summary>The shooter id of AI / scripted units (HaloUnit): their projectiles hit the LOCAL player's body (it has no hit box) and blasts hurt it.</summary>
        public const int AiShooter = -2;

        // ---- difficulty (the globals' difficulty table, Data/Ai/HaloAiDifficulty: enemy / friend damage and vitality per level)

        /// <summary>Lab: force a difficulty (0 easy .. 3 legendary) outside a mission (-1 = only in the campaign, at HaloCampaign.Level).</summary>
        public static int LabDifficulty = -1;
        private static HaloAiDifficulty _diff;
        private static HaloMap _diffMap;

        public static bool DifficultyOn => HaloCampaign.Active || LabDifficulty >= 0;
        public static int DifficultyIndex => Mathf.Clamp(LabDifficulty >= 0 ? LabDifficulty : (int)HaloCampaign.Level, 0, 3);

        public static HaloAiDifficulty DifficultyTable
        {
            get
            {
                HaloMap map = HaloSession.Map ?? LabMap;
                if (map == null) return null;
                if (_diff == null || _diffMap != map)
                {
                    try { _diff = new HaloAiDifficulty(map); } catch (Exception e) { HaloLog.Warn("Combat: difficulty table: " + e.Message); _diff = new HaloAiDifficulty(); }
                    _diffMap = map;
                    HaloLog.Info("Combat: difficulty table " + (_diff.FromTag ? "from the globals" : "missing (all 1)") + ": enemy damage " + _diff.EnemyDamage[0].ToString("0.##") + " / " + _diff.EnemyDamage[1].ToString("0.##") + " / " + _diff.EnemyDamage[2].ToString("0.##") + " / " + _diff.EnemyDamage[3].ToString("0.##")
                                 + ", enemy vitality " + _diff.EnemyVitality[0].ToString("0.##") + " / " + _diff.EnemyVitality[1].ToString("0.##") + " / " + _diff.EnemyVitality[2].ToString("0.##") + " / " + _diff.EnemyVitality[3].ToString("0.##"));
                }

                return _diff;
            }
        }

        /// <summary>The actor variant's weapon damage modifier of the unit that fired (1 when none).</summary>
        public static float ShooterModifier(Transform shooterRoot)
        {
            if (shooterRoot == null) return 1f;
            HaloUnit u = shooterRoot.GetComponentInParent<HaloUnit>();
            float m = u != null && u.Def != null ? u.Def.WeaponDamageModifier : 1f;
            return m > 0f ? m : 1f;
        }

        private static bool PlayersFriend(HaloTeam t) { return t != HaloTeam.None && HaloAllegiance.AreFriends(t, HaloTeam.Player); }

        /// <summary>
        /// A hit on something other than the local player, scaled for the difficulty: an AI's shot by the friend / enemy damage of its side and
        /// its weapon damage modifier; the player's (or the world's) hit on a unit divided by that unit's side's vitality (Legendary enemies last longer).
        /// </summary>
        public static float ScaleForTarget(float amount, int shooter, Transform shooterRoot, IHaloDamageable victim)
        {
            if (!DifficultyOn) return amount;
            var t = DifficultyTable;
            if (t == null) return amount;
            int d = DifficultyIndex;
            HaloUnit su = shooterRoot != null ? shooterRoot.GetComponentInParent<HaloUnit>() : null;
            if (shooter == AiShooter || su != null)
            {
                bool friend = su != null && PlayersFriend(su.Team);
                return amount * (friend ? t.FriendDamage[d] : t.EnemyDamage[d]) * ShooterModifier(shooterRoot);
            }

            HaloUnit vu = victim as HaloUnit;
            if (vu == null) return amount;
            float vit = PlayersFriend(vu.Team) ? t.FriendVitality[d] : t.EnemyVitality[d];
            return vit > 0.01f ? amount / vit : amount;
        }

        /// <summary>An AI's hit on the local player scaled by the level's enemy damage (HaloVitals applies it to every hit from AiShooter).</summary>
        public static float ScaleForPlayer(float amount, int shooter)
        {
            if (!DifficultyOn || shooter != AiShooter) return amount;
            var t = DifficultyTable;
            return t != null ? amount * t.EnemyDamage[DifficultyIndex] : amount;
        }

        /// <summary>Script player_enable_input: false holds the player's game actions (fire, grenade, melee, reload, switch, pickups); the hands still move.</summary>
        public static bool InputEnabled = true;

        /// <summary>What sticks to the local player (an AI's plasma grenade): a transform at its chest.</summary>
        public static Transform LocalAnchor => HaloHandling.Current != null ? HaloHandling.Current.Anchor : null;

        /// <summary>The player's flashlight (script unit_set_desired_flashlight_state / units_set_desired_flashlight_state / unit_get_current_flashlight_state on the player).</summary>
        public static bool FlashlightOn
        {
            get { return HaloHandling.Current != null && HaloHandling.Current.FlashlightOn; }
            set { var h = EnsureDriver(); if (h != null) h.FlashlightOn = value; }
        }

        /// <summary>unit_solo_player_integrated_night_vision_is_active: night vision could be toggled with the flashlight button now (zoomed sniper scope); HaloLocal.NightVision = it is on.</summary>
        public static bool NightVisionAvailable => HaloHandling.Current != null && HaloHandling.Current.NightVisionAvailable;

        /// <summary>Script players_unzoom_all.</summary>
        public static void Unzoom() { HaloLocal.Zoomed = false; HaloLocal.ZoomLevel = 0; HaloLocal.ZoomMagnification = 1f; }

        /// <summary>
        /// Script damage_object: a damage effect on an object (its IHaloDamageable: a HaloUnit, a vehicle, a remote player's hit box; the local
        /// player when <paramref name="target"/> is null or the player's own body): the tag's full amount (upper bound; an area effect at its
        /// centre), shooter = the world. False when the object takes no damage.
        /// </summary>
        public static bool ApplyDamage(HaloTag jpt, GameObject target)
        {
            var dd = Catalog != null ? Catalog.Damage(jpt) : null;
            if (dd == null) return false;
            float amount = dd.IsArea ? dd.AreaAmount(DamageRng, 0f) : dd.DirectAmount(DamageRng, 0f, 0f, 0f);
            Transform root = HaloPlayer.Root;
            if (target == null || (root != null && target.transform.IsChildOf(root)) || (HaloHandling.Current != null && target == HaloHandling.Current.gameObject))
            {
                TakeHit(new HaloHit { Amount = amount, Effect = jpt, SourcePlayer = -1, Point = LocalChest, Direction = Vector3.down });
                return true;
            }

            IHaloDamageable v = target.GetComponentInParent<IHaloDamageable>() ?? target.GetComponentInChildren<IHaloDamageable>();
            if (v == null) return false;
            HaloProjectiles.Deliver(v, new HaloHit { Amount = amount, Effect = jpt, SourcePlayer = -1, Point = target.transform.position, Direction = Vector3.down });
            return true;
        }

        /// <summary>Script damage_new: a damage effect at a point with its own radius (an area effect: everything within, the local player too; a
        /// damage effect without a radius: what stands within 0.5 m).</summary>
        public static void ApplyDamageAt(HaloTag jpt, Vector3 at)
        {
            if (jpt == null) return;
            HaloProjectiles.BlastOne(jpt, at, -1, jpt, false, null);
        }

        /// <summary>
        /// A damage effect's area damage at a point (effect jpt! parts - Effects calls it for every effect it plays -, scripted blasts):
        /// every unit / hit box within its radius takes the falloff amount (IsArea hits), units are pushed by its instantaneous acceleration
        /// (bodies too), infection forms pop on "infection form pop", grenades go off on "detonates explosives", the local player is hurt
        /// too. <paramref name="sourcePlayer"/> = a player's net id (-1 = none: the AI when <paramref name="sourceUnit"/> is set, else the
        /// world); <paramref name="ignore"/> = a root not to hurt. An effect tag (effe) applies all of its damage parts. The same jpt! at
        /// the same point in one frame is applied once. Same path as projectile blasts; no allocations.
        /// </summary>
        public static void AreaDamage(HaloTag jpt, Vector3 at, int sourcePlayer, HaloUnit sourceUnit, Transform ignore)
        {
            if (jpt == null) return;
            try
            {
                if (jpt.Is("effe"))
                {
                    var parts = Catalog != null ? Catalog.Blast(jpt) : null;
                    if (parts != null) for (int i = 0; i < parts.Count; i++) AreaDamage(parts[i], at, sourcePlayer, sourceUnit, ignore);
                    return;
                }

                if (!jpt.Is("jpt!")) return;
                int shooter = sourcePlayer >= 0 ? sourcePlayer : sourceUnit != null ? AiShooter : -1;
                bool local = sourcePlayer >= 0 && sourcePlayer == HaloLocal.NetId;
                HaloTag cause = sourceUnit != null && sourceUnit.Def != null ? sourceUnit.Def.Unit : jpt;
                HaloProjectiles.Area(jpt, at, shooter, cause, local, true, null, sourceUnit != null ? sourceUnit.transform : null, ignore);
            }
            catch (Exception e) { HaloLog.Warn("Combat: area damage " + jpt.Name + " failed: " + e.Message); }
        }

        /// <summary>
        /// Effects' damage part (a jpt! in an effect event, HaloFxRuntime): applied as <see cref="AreaDamage"/> on the campaign host (campaign) / world owner only (other
        /// clients see the effect; the owner's blast reaches their players through the hit boxes), never for a projectile's detonation /
        /// impact effects (the projectile's own blast applies those, shooter-authoritative). <paramref name="creator"/> = the unit that made
        /// the effect (a living creator's own body is not hurt by it - ours, so a region / shield effect cannot feed back).
        /// </summary>
        public static void EffectDamage(HaloTag jpt, Vector3 at, HaloTag effect, HaloUnit creator)
        {
            if (jpt == null) return;
            try
            {
                // the side that simulates the units applies it: the campaign host in a campaign (units, AI and spawned forms run there; the
                // world writer can be another client while a hand-over settles), the world owner elsewhere; never a co-op puppet's copy
                if (HaloCampaign.Active ? !HaloCampaign.Host : !HaloNetGame.WorldOwner) return;
                if (creator != null && creator.Puppet) return;
                if (IsProjectileEffect(effect)) return;
                var dd = Catalog != null ? Catalog.Damage(jpt) : null;
                if (dd == null || dd.Harmless) return;
                if (_effectDamageLogged.Add(jpt.Id)) HaloLog.Info("Combat: effect " + (effect != null ? effect.Name : "?") + " applies its damage part " + jpt.Name + (creator != null && creator.Def != null ? " (by " + creator.Def.Name + ")" : ""));
                AreaDamage(jpt, at, -1, creator, creator != null && creator.Alive ? creator.transform : null);
            }
            catch (Exception e) { HaloLog.Warn("Combat: effect damage " + jpt.Name + " failed: " + e.Message); }
        }

        private static readonly HashSet<uint> _effectDamageLogged = new HashSet<uint>();
        private static readonly HashSet<uint> _projectileEffects = new HashSet<uint>();
        private static HaloMap _projectileEffectsMap;

        /// <summary>An effect some projectile tag of its map plays on detonation / impact (proj @428 detonation, @396 super detonation, material
        /// responses' default / potential / detonation effects): its damage is the projectile's (HaloProjectiles.Detonate, vehicle guns).</summary>
        public static bool IsProjectileEffect(HaloTag effect)
        {
            if (effect == null || effect.Map == null) return false;
            if (_projectileEffectsMap != effect.Map)
            {
                _projectileEffectsMap = effect.Map;
                _projectileEffects.Clear();
                try
                {
                    foreach (HaloTag p in effect.Map.OfClass("proj"))
                    {
                        var r = p.Root;
                        AddId(r.Ref(428)); AddId(r.Ref(396));
                        foreach (var m in r.Block(576, 160)) { AddId(m.Ref(4)); AddId(m.Ref(60)); AddId(m.Ref(104)); }
                    }
                }
                catch (Exception e) { HaloLog.Warn("Combat: projectile effects of " + effect.Map.Name + ": " + e.Message); }
                HaloLog.Info("Combat: " + _projectileEffects.Count + " projectile detonation / impact effects in " + effect.Map.Name + " (their damage parts stay the projectile's)");
            }

            return _projectileEffects.Contains(effect.Id);
        }

        private static void AddId(HaloTag t) { if (t != null) _projectileEffects.Add(t.Id); }

        private static readonly System.Random DamageRng = new System.Random();

        /// <summary>
        /// A starting profile / script player_add_equipment: the weapons (loaded / reserve &lt; 0 = the weapon's own defaults) and grenades; with
        /// <paramref name="reset"/> everything carried goes first (else they are added: a third weapon is not taken, grenades up to the maximum).
        /// </summary>
        public static void GiveProfile(HaloTag primary, int primaryLoaded, int primaryReserve, HaloTag secondary, int secondaryLoaded, int secondaryReserve, int frag, int plasma, bool reset)
        {
            var h = EnsureDriver();
            if (h == null || Catalog == null) return;
            if (reset) { h.DropObjective(Vector3.zero, false); h.Unequip(); HaloLocal.FragGrenades = 0; HaloLocal.PlasmaGrenades = 0; }
            GiveOne(h, primary, primaryLoaded, primaryReserve);
            GiveOne(h, secondary, secondaryLoaded, secondaryReserve);
            HaloLocal.FragGrenades = Mathf.Clamp(HaloLocal.FragGrenades + Mathf.Max(0, frag), 0, HaloLocal.FragMax);
            HaloLocal.PlasmaGrenades = Mathf.Clamp(HaloLocal.PlasmaGrenades + Mathf.Max(0, plasma), 0, HaloLocal.PlasmaMax);
            HaloLog.Info("Combat: profile given (" + (reset ? "reset" : "added") + "): " + Describe(h) + ", " + HaloLocal.FragGrenades + " frag / " + HaloLocal.PlasmaGrenades + " plasma");
        }

        private static void GiveOne(HaloHandling h, HaloTag weap, int loaded, int reserve)
        {
            if (weap == null || !weap.Is("weap") || h.Carried >= 2) return;
            HaloGun g = h.Give(weap);
            if (g == null) return;
            if (loaded >= 0) g.State.Loaded = Mathf.Min(loaded, Mathf.Max(loaded, g.State.LoadedMaximum));
            if (reserve >= 0) g.State.Reserve = reserve;
            g.SyncInfo();
        }

        /// <summary>Mission K: a fresh life with the scenario's starting profile instead of the multiplayer weapon set (health / shield 0..1, &lt;= 0 = full).</summary>
        public static void SpawnedWith(HaloTag primary, int primaryLoaded, int primaryReserve, HaloTag secondary, int secondaryLoaded, int secondaryReserve, int frag, int plasma, float healthFraction, float shieldFraction)
        {
            Spawned();
            GiveProfile(primary, primaryLoaded, primaryReserve, secondary, secondaryLoaded, secondaryReserve, frag, plasma, true);
            if (healthFraction > 0f && healthFraction < 1f) HaloVitals.Body = HaloVitals.MaxBody * healthFraction;
            if (shieldFraction > 0f && shieldFraction < 1f) HaloVitals.ShieldPoints = HaloVitals.MaxShield * shieldFraction;
        }

        /// <summary>Characters C: a dead unit's weapon falls where it stood (rounds &lt; 0 = the weapon's defaults). The campaign keeps drops longer.</summary>
        public static HaloPickups.Item DropWeaponAt(HaloTag weap, int loaded, int reserve, Vector3 at, Vector3 velocity)
        {
            var def = Catalog != null ? Catalog.Weapon(weap) : null;
            if (def == null) return null;
            HaloGun g = HaloGun.Create(def, HaloPickups.Root);
            if (g == null) return null;
            g.SetScale(1f);
            if (loaded >= 0) g.State.Loaded = loaded;
            if (reserve >= 0) g.State.Reserve = reserve;
            g.transform.position = at;
            return HaloPickups.DropWeapon(g, velocity, false);
        }

        // ------------------------------------------------------------------ helpers the combat code shares

        private static readonly HashSet<Collider> Own = new HashSet<Collider>();

        /// <summary>Colliders that are the local player's own (its hit box if the net layer makes one, a held shield...): shots and swings skip them.</summary>
        public static void RegisterOwn(Collider c) { if (c != null) Own.Add(c); }
        public static void UnregisterOwn(Collider c) { if (c != null) Own.Remove(c); }

        public static bool IsOwnCollider(Collider c)
        {
            if (c == null) return false;
            if (Own.Contains(c)) return true;
            if (HaloLocal.Vehicle != null && c.transform.IsChildOf(HaloLocal.Vehicle.transform)) return true;
            Transform root = HaloPlayer.Root;
            return root != null && c.transform.IsChildOf(root);
        }

        /// <summary>The local player's head for hits: the avatar's head bone on a monitor (the camera may be behind it in third person), the headset in VR.</summary>
        public static Vector3 LocalHead
        {
            get
            {
                var h = HaloHandling.Current;
                if (h == null || !h.Body.Valid) return Vector3.zero;
                Transform bone = h.Desktop ? HaloPlayer.HeadBone : null;
                return bone != null ? bone.position : h.Body.HeadPos;
            }
        }

        /// <summary>The local player's chest (for blasts and AI aim; zero when unknown).</summary>
        public static Vector3 LocalChest
        {
            get
            {
                var h = HaloHandling.Current;
                if (h == null || !h.Body.Valid) return Vector3.zero;
                if (!h.Desktop || HaloPlayer.HeadBone == null) return h.Body.Chest;
                return HaloPlayer.HeadBone.position - Vector3.up * (0.4f * h.Body.Scale);
            }
        }

        /// <summary>Where loose things (magazines, drops) live: the items root of the match.</summary>
        public static Transform ItemParent => HaloPickups.Root;

        public static void SayPickedUp(HaloWeaponDef def)
        {
            var cat = Catalog;
            if (cat == null || def == null) return;
            string s = def.PickupTextIndex >= 0 && def.PickupTextIndex < cat.ItemMessages.Count ? cat.ItemMessages[def.PickupTextIndex] : "Picked up " + def.Name;
            HaloEvents.RaiseMessage(s, 2f);
        }

        /// <summary>The local death: weapons fall where the player stood (Halo drops them), the hands empty.</summary>
        public static void OnLocalDeath(HaloKill k)
        {
            var h = HaloHandling.Current;
            if (h == null) return;
            h.DropObjective(Vector3.zero, true);
            Vector3 at = h.Body.Valid ? h.Body.Hips : HaloLocal.Position;
            for (int i = 0; i < 2; i++)
            {
                HaloGun g = h.Guns[i];
                if (g == null) continue;
                h.Release(g);
                g.transform.position = at + h.Body.Right * (i == 0 ? 0.2f : -0.2f);
                HaloPickups.DropWeapon(g, Vector3.up * 0.5f, true);
            }

            HaloLocal.Zoomed = false; HaloLocal.ZoomMagnification = 1f;
        }

        public static void ShieldsDown()
        {
            var v = Catalog != null ? Catalog.Vitality : null;
            if (v != null && v.ShieldDepletedEffect != null) HaloCombatFx.Play(v.ShieldDepletedEffect, LocalChest, Quaternion.identity, null);
        }

        public static void ShieldsRecharging()
        {
            var v = Catalog != null ? Catalog.Vitality : null;
            if (v != null && v.ShieldRechargingEffect != null) HaloCombatFx.Play(v.ShieldRechargingEffect, LocalChest, Quaternion.identity, null);
        }

        /// <summary>(A grenade bounced: its material response's effect already plays the sound.)</summary>
        /// <summary>A grenade bounced: the grenade equipment's own material effects ("impact" of its weapon.foot: dirt, metal, grating ...) by the surface, loud with the speed.</summary>
        public static void GrenadeBounced(HaloProjectiles.Flight f, Vector3 at, float speed)
        {
            var cat = Catalog;
            if (cat == null || f.GrenadeKind < 0 || f.GrenadeKind >= cat.Grenades.Count) return;
            HaloTag eq = cat.Grenades[f.GrenadeKind].Equipment;
            if (eq != null) { try { HaloImpacts.Play(eq, HaloImpacts.Impact, at, -1, Mathf.Clamp01(speed / 6f)); } catch (Exception) { } }
        }

        private static void Raise0(Action e)
        {
            if (e == null) return;
            foreach (Delegate d in e.GetInvocationList()) { try { ((Action)d)(); } catch (Exception x) { HaloLog.Warn("Combat: listener: " + x.Message); } }
        }

        private static void Raise3(Action<HaloTag, Vector3, Vector3> e, HaloTag a, Vector3 b, Vector3 c)
        {
            if (e == null) return;
            foreach (Delegate d in e.GetInvocationList()) { try { ((Action<HaloTag, Vector3, Vector3>)d)(a, b, c); } catch (Exception x) { HaloLog.Warn("Combat: listener: " + x.Message); } }
        }
    }
}

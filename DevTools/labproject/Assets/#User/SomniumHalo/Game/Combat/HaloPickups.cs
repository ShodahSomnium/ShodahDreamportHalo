using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Items lying in the world: the scenario's netgame equipment (an item collection per spot, weighted, with its respawn time; the game
    /// type's filter; the rules' weapon set swaps the weapons), weapons dropped by players (they stay, then go after <see cref="DropSeconds"/>).
    /// Halo's rules: walking over a weapon you carry takes its ammunition; walking over grenades (up to the globals' maximum), ammunition
    /// boxes and powerups (over shield, active camouflage, a health pack when hurt) takes them; a weapon you do not carry is taken by
    /// gripping it (VR) / E (monitor) - "Hold [grip] to pick up Shotgun" (the game's own HUD prompt) shows when one is in reach.
    /// A lying weapon IS its HaloGun (its ammunition stays with it). Net hooks: <see cref="Taken"/>, <see cref="LocalDropped"/>,
    /// <see cref="ApplyRemoteTaken"/>, <see cref="RemoteDrop"/>.
    /// </summary>
    public static class HaloPickups
    {
        public sealed class Item
        {
            public int Id;                               // scenario NetEquipment index, or >= 1024 for drops
            public HaloTag Tag;                          // weap or eqip
            public HaloScenario.NetgameEquipment Spawn;  // null for drops
            public Vector3 Home; public Quaternion HomeRot;
            public HaloGun Gun;                          // weapons
            public GameObject Visual;                    // equipment
            public HaloEquipmentDef Equipment;
            public bool Present;
            public float RespawnAt = -1f, RespawnSeconds;
            public bool Dropped; public float DespawnAt;
            public bool Falling; public Vector3 Velocity;
            public int Shooter = -1;                     // who dropped it (-1 = the map)
        }

        public const int DropIdBase = 1024;
        public static float HandReach = 0.6f;          // VR: a hand this close to a lying weapon takes it on a grip (ours: no bending to the exact spot)
        public static float WalkRadius = 0.7f;         // walking over: horizontal distance (ours)
        public static float DropSeconds = 30f;         // a dropped weapon goes after this (ours)
        public static float CampaignDropSeconds = 600f;   // the campaign: what the dead drop stays for the mission (ours: Halo garbage-collects later)
        public static float DefaultRespawn = 30f;      // spots and collections with no time (ours)

        public static readonly List<Item> Items = new List<Item>();
        private static Transform _root;
        private static HaloScenario _placedFor;
        private static int _nextDrop = DropIdBase;
        private static readonly System.Random Rng = new System.Random();
        private static float _nextWalk;

        /// <summary>The local player took an item (Net: NotePickup(item.Id)).</summary>
        public static event Action<Item> Taken;
        /// <summary>The local player dropped a weapon (Net: NoteDrop; it may set item.Id to its own drop id).</summary>
        public static event Action<Item> LocalDropped;

        public static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var go = new GameObject("Halo Items");
                    Transform parent = HaloSession.WorldRoot != null ? HaloSession.WorldRoot : (HaloGame.Current != null ? HaloGame.Current.transform : null);
                    if (parent != null) go.transform.SetParent(parent, false);
                    _root = go.transform;
                }

                return _root;
            }
        }

        public static bool PlacedFor(HaloScenario s) { return s != null && _placedFor == s && _root != null; }

        // ------------------------------------------------------------------ placing

        /// <summary>Lays out the scenario's netgame equipment for the rules (game type filter, weapon set).</summary>
        public static void Place(HaloScenario scenario, HaloRules rules)
        {
            Clear();
            _placedFor = scenario;
            if (scenario == null || HaloCombat.Catalog == null) return;
            rules = rules ?? new HaloRules();
            int weapons = 0, equipment = 0, skipped = 0;
            for (int i = 0; i < scenario.NetEquipment.Count; i++)
            {
                var e = scenario.NetEquipment[i];
                if (!e.Allows(rules.Type)) { skipped++; continue; }
                var item = new Item { Id = i, Spawn = e };
                item.Home = HaloSpace.Pos(e.Position) + HaloSpace.MapOrigin;
                item.HomeRot = HaloSpace.Yaw(e.Facing);
                var col = HaloCombat.Catalog.Collection(e.ItemCollection);
                item.RespawnSeconds = e.SpawnTime > 0 ? e.SpawnTime : (col != null && col.DefaultSpawnTime > 0 ? col.DefaultSpawnTime : DefaultRespawn);
                if (!Fill(item, rules)) { skipped++; continue; }
                float ago;
                if (i < 128 && HaloNetGame.TryItemTaken(i, out ago) && ago < item.RespawnSeconds)
                {
                    // a late joiner: someone took it before we came - it is away until its respawn
                    if (item.Gun != null) { UnityEngine.Object.Destroy(item.Gun.gameObject); item.Gun = null; }
                    if (item.Visual != null) { UnityEngine.Object.Destroy(item.Visual); item.Visual = null; }
                    item.Present = false;
                    item.RespawnAt = Time.time + (item.RespawnSeconds - ago);
                }
                Items.Add(item);
                if (item.Gun != null) weapons++; else equipment++;
            }

            HaloLog.Info("Combat: placed " + weapons + " weapons and " + equipment + " equipment items (" + skipped + " not for " + rules.Type + " / weapon set " + rules.WeaponSet + ")");
        }

        /// <summary>Picks the spot's item from its collection (weighted), maps it through the weapon set and builds it lying at home.</summary>
        private static bool Fill(Item item, HaloRules rules)
        {
            var cat = HaloCombat.Catalog;
            var col = cat.Collection(item.Spawn.ItemCollection);
            HaloTag t = col != null ? col.Pick(Rng) : null;
            if (t == null) return false;
            if (t.Is("weap"))
            {
                t = HaloCombat.MapWeapon(t, rules != null ? rules.WeaponSet : 0);
                if (t == null) return false;
                var def = cat.Weapon(t);
                var g = HaloGun.Create(def, Root);
                if (g == null) return false;
                item.Tag = t;
                item.Gun = g;
                g.SetScale(1f);
                Lay(item, item.Home, item.HomeRot);
            }
            else if (t.Is("eqip"))
            {
                var eq = cat.Equipment(t);
                if (eq == null || eq.Model == null) return false;
                item.Tag = t;
                item.Equipment = eq;
                item.Visual = new GameObject("Item " + eq.Name);
                item.Visual.transform.SetParent(Root, false);
                var b = HaloModelBuilder.Build(eq.Model, item.Visual.transform, new HaloModelBuilder.Options { CastShadows = true });
                if (b == null) { UnityEngine.Object.Destroy(item.Visual); item.Visual = null; return false; }
                item.Visual.transform.localScale = Vector3.one * (1.7f / HaloGun.CyborgHeight);
                Lay(item, item.Home, item.HomeRot);
            }
            else return false;
            item.Present = true;
            item.RespawnAt = -1f;
            return true;
        }

        /// <summary>On the ground below <paramref name="at"/> (2 m search): a weapon on its side, equipment upright; levitating items stay where they are.</summary>
        private static void Lay(Item item, Vector3 at, Quaternion facing)
        {
            Vector3 p = at;
            bool lev = item.Spawn != null && item.Spawn.Levitate;
            RaycastHit h;
            if (!lev && Physics.Raycast(at + Vector3.up * 0.6f, Vector3.down, out h, 3f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) p = h.point;
            if (item.Gun != null)
            {
                Vector3 f = facing * Vector3.forward; f.y = 0f;
                if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                f.Normalize();
                Quaternion side = Quaternion.LookRotation(f, Vector3.Cross(f, Vector3.up));       // lying on its side: the gun's right points up
                float half = Mathf.Max(0.02f, item.Gun.LocalBounds.extents.x) * item.Gun.transform.localScale.x;
                // the grip is at the origin; the middle of the gun goes over the spot
                Vector3 mid = side * Vector3.Scale(item.Gun.LocalBounds.center, item.Gun.transform.localScale);
                item.Gun.transform.SetPositionAndRotation(p + Vector3.up * (half + 0.01f) - new Vector3(mid.x, 0f, mid.z), side);
                item.Gun.gameObject.SetActive(true);
            }
            else if (item.Visual != null)
            {
                item.Visual.transform.SetPositionAndRotation(p + Vector3.up * 0.01f, facing);
                item.Visual.SetActive(true);
            }
        }

        /// <summary>One item lying at a point (a weapon or equipment tag; no respawn) - for the lab and for game types that place things.</summary>
        public static Item PlaceAt(HaloTag tag, Vector3 at, float yawDegrees)
        {
            var cat = HaloCombat.Catalog;
            if (cat == null || tag == null) return null;
            var item = new Item { Id = _nextDrop++, Tag = tag, Home = at, HomeRot = Quaternion.Euler(0f, yawDegrees, 0f), Dropped = false };
            if (tag.Is("weap"))
            {
                var g = HaloGun.Create(cat.Weapon(tag), Root);
                if (g == null) return null;
                g.SetScale(1f);
                item.Gun = g;
            }
            else if (tag.Is("eqip"))
            {
                var eq = cat.Equipment(tag);
                if (eq == null || eq.Model == null) return null;
                item.Equipment = eq;
                item.Visual = new GameObject("Item " + eq.Name);
                item.Visual.transform.SetParent(Root, false);
                if (HaloModelBuilder.Build(eq.Model, item.Visual.transform, new HaloModelBuilder.Options()) == null) { UnityEngine.Object.Destroy(item.Visual); return null; }
                item.Visual.transform.localScale = Vector3.one * (1.7f / HaloGun.CyborgHeight);
            }
            else return null;
            Lay(item, at, item.HomeRot);
            item.Present = true;
            Items.Add(item);
            return item;
        }

        public static void Clear()
        {
            for (int i = 0; i < Items.Count; i++)
            {
                Item it = Items[i];
                if (it.Gun != null) UnityEngine.Object.Destroy(it.Gun.gameObject);
                if (it.Visual != null) UnityEngine.Object.Destroy(it.Visual);
            }

            Items.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _root = null;
            _placedFor = null;
        }

        // ------------------------------------------------------------------ every frame

        public static void Tick(float dt, HaloHandling h)
        {
            float now = Time.time;
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                Item it = Items[i];
                if (it.Falling) Fall(it, dt);
                if (!it.Present && it.RespawnAt >= 0f && now >= it.RespawnAt && it.Spawn != null)
                {
                    if (Fill(it, HaloSession.Rules)) HaloLog.Info("Combat: " + Name(it) + " respawned at spot " + it.Id);
                    else it.RespawnAt = now + 5f;
                }

                if (it.Dropped && it.Present && now >= it.DespawnAt)
                {
                    if (it.Gun != null) UnityEngine.Object.Destroy(it.Gun.gameObject);
                    if (it.Visual != null) UnityEngine.Object.Destroy(it.Visual);
                    Items.RemoveAt(i);
                }
            }

            if (h != null && HaloLocal.Alive && now >= _nextWalk)
            {
                _nextWalk = now + 0.1f;
                WalkOver(h);
            }
        }

        private static void Fall(Item it, float dt)
        {
            Transform t = it.Gun != null ? it.Gun.transform : it.Visual != null ? it.Visual.transform : null;
            if (t == null) { it.Falling = false; return; }
            it.Velocity += Vector3.down * 9.78f * dt;
            Vector3 step = it.Velocity * dt;
            RaycastHit h;
            if (Physics.Raycast(t.position + Vector3.up * 0.05f, step.normalized, out h, step.magnitude + 0.1f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                it.Falling = false;
                // the item's own material effects ("impact": weapon.foot by the surface), loud with the landing speed
                if (it.Tag != null) { try { HaloImpacts.Play(it.Tag, HaloImpacts.Impact, h.point, -1, Mathf.Clamp01(it.Velocity.magnitude / 6f)); } catch (Exception) { } }
                Quaternion face = Quaternion.LookRotation(Flat(t.forward), Vector3.up);
                Lay(it, h.point, face);
                return;
            }

            t.position += step;
            if (t.position.y < -2000f) it.Falling = false;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward; }

        /// <summary>Walking over: ammunition from a weapon you carry, grenades, ammunition boxes, powerups.</summary>
        private static void WalkOver(HaloHandling h)
        {
            Vector3 feet = h.Body.Feet;
            var cat = HaloCombat.Catalog;
            if (cat == null) return;
            for (int i = 0; i < Items.Count; i++)
            {
                Item it = Items[i];
                if (!it.Present || it.Falling) continue;
                Vector3 p = it.Gun != null ? it.Gun.transform.position : it.Visual != null ? it.Visual.transform.position : it.Home;
                Vector3 d = p - feet;
                if (Mathf.Abs(d.y) > 1.3f) continue;
                d.y = 0f;
                if (d.magnitude > WalkRadius * h.Body.Scale) continue;
                if (it.Gun != null) TakeAmmoFrom(it, h);
                else if (it.Equipment != null) Touch(it, h);
            }
        }

        private static void TakeAmmoFrom(Item it, HaloHandling h)
        {
            HaloGun mine = null;
            for (int k = 0; k < 2; k++) if (h.Guns[k] != null && h.Guns[k].Def.Tag == it.Tag) mine = h.Guns[k];
            if (mine == null || !mine.State.UsesAmmo) return;
            var s = it.Gun.State;
            int there = s.Loaded + s.Reserve;
            if (there <= 0) return;
            int took = mine.State.AddReserve(there);
            if (took <= 0) return;
            int fromReserve = Math.Min(took, s.Reserve);
            s.Reserve -= fromReserve;
            s.Loaded -= took - fromReserve;
            string msg = HaloCombat.Catalog.AmmoMessage(mine.Def.PickupTextIndex, took);
            HaloEvents.RaiseMessage(msg ?? ("Picked up " + took + " rounds for " + mine.Def.Name), 2f);
            HaloTag ammoSound = AmmoSound(mine.Def);      // (Halo: the magazine object equipment's ammunition pickup sound)
            if (ammoSound != null) HaloAudio.Play2D(ammoSound, 1f);
            else if (mine.Def.PickupSound != null) HaloAudio.Play2D(mine.Def.PickupSound, 0.7f);
            if (s.Loaded + s.Reserve <= 0) Remove(it, true);
        }

        private static void Touch(Item it, HaloHandling h)
        {
            var eq = it.Equipment;
            bool took = false;
            switch (eq.Kind)
            {
                case HaloEquipmentDef.Powerup.Grenade:
                    if (eq.GrenadeType == 0 && HaloLocal.FragGrenades < HaloLocal.FragMax) { HaloLocal.FragGrenades++; took = true; }
                    else if (eq.GrenadeType == 1 && HaloLocal.PlasmaGrenades < HaloLocal.PlasmaMax) { HaloLocal.PlasmaGrenades++; took = true; }
                    break;
                case HaloEquipmentDef.Powerup.Health:
                    if (HaloLocal.Health < 0.999f) { HaloVitals.HealthPack(); took = true; }
                    break;
                case HaloEquipmentDef.Powerup.OverShield:
                    HaloVitals.OverShield(); took = true;
                    break;
                case HaloEquipmentDef.Powerup.ActiveCamouflage:
                    HaloVitals.Camouflage(eq.PowerupTime); took = true;
                    break;
                case HaloEquipmentDef.Powerup.None:
                    // an ammunition box: the rounds of the magazine object of a weapon carried that names this equipment
                    for (int k = 0; k < 2 && !took; k++)
                    {
                        HaloGun g = h.Guns[k];
                        if (g == null || g.Def.PrimaryMagazine == null) continue;
                        foreach (var o in g.Def.PrimaryMagazine.Objects)
                        {
                            if (o.Equipment != it.Tag) continue;
                            int n = g.State.AddReserve(o.Rounds > 0 ? o.Rounds : g.State.LoadedMaximum);
                            if (n > 0)
                            {
                                took = true;
                                string msg = HaloCombat.Catalog.AmmoMessage(g.Def.PickupTextIndex, n);
                                HaloEvents.RaiseMessage(msg ?? ("Picked up " + n + " rounds for " + g.Def.Name), 2f);
                            }

                            break;
                        }
                    }

                    break;
                default:
                    break;      // double speed, full-spectrum vision: not in Halo CE multiplayer's sandbox (ignored)
            }

            if (!took) return;
            HaloLog.Info("Combat: picked up " + eq.Name);
            if (eq.Kind != HaloEquipmentDef.Powerup.None)
            {
                string msg = HaloCombat.Catalog.ItemMessages.Count > eq.PickupTextIndex && eq.PickupTextIndex >= 0 ? HaloCombat.Catalog.ItemMessages[eq.PickupTextIndex] : "Picked up " + eq.Name;
                HaloEvents.RaiseMessage(msg, 2f);
            }

            if (eq.PickupSound != null) HaloAudio.Play2D(eq.PickupSound);
            Remove(it, true);
        }

        /// <summary>An item leaves the world: a spot waits for its respawn, a drop is gone. <paramref name="local"/> = we took it (Net hears).</summary>
        private static void Remove(Item it, bool local)
        {
            it.Present = false;
            if (it.Gun != null) { UnityEngine.Object.Destroy(it.Gun.gameObject); it.Gun = null; }
            if (it.Visual != null) { UnityEngine.Object.Destroy(it.Visual); it.Visual = null; }
            if (it.Spawn != null) it.RespawnAt = Time.time + it.RespawnSeconds;
            else Items.Remove(it);
            if (local) Raise(Taken, it);
        }

        // ------------------------------------------------------------------ weapons by hand

        /// <summary>The weapon lying nearest to <paramref name="pos"/> within <paramref name="radius"/> (to the gun's nearest point along its length).</summary>
        public static Item NearestWeapon(Vector3 pos, float radius, bool presentOnly)
        {
            Item best = null;
            float bd = radius;
            for (int i = 0; i < Items.Count; i++)
            {
                Item it = Items[i];
                if (it.Gun == null || (presentOnly && !it.Present)) continue;
                HaloGun g = it.Gun;
                Vector3 a = g.transform.TransformPoint(g.RearLocal), b = g.transform.TransformPoint(g.FrontLocal);
                Vector3 ab = b - a;
                float u = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(pos - a, ab) / ab.sqrMagnitude) : 0f;
                float d = (a + ab * u - pos).magnitude;
                if (d < bd) { bd = d; best = it; }
            }

            return best;
        }

        /// <summary>Takes a lying weapon: its HaloGun (with its ammunition) leaves the item, the spot waits for its respawn.</summary>
        public static HaloGun TakeWeapon(Item it)
        {
            if (it == null || it.Gun == null || !it.Present) return null;
            HaloGun g = it.Gun;
            it.Gun = null;
            it.Falling = false;
            HaloLog.Info("Combat: picked up " + g.Def.Name + " (" + g.State.Loaded + "/" + g.State.Reserve + ")" + (it.Spawn != null ? " at spot " + it.Id : " (dropped)"));
            if (g.Def.PickupSound != null) HaloAudio.Play(g.Def.PickupSound, g.transform, 1f);
            Remove(it, true);
            return g;
        }

        /// <summary>A gun becomes a lying item where it is (or at <paramref name="at"/>), falling to the ground; despawns after DropSeconds.</summary>
        public static Item DropWeapon(HaloGun g, Vector3 velocity, bool local, Vector3? at = null)
        {
            if (g == null) return null;
            g.transform.SetParent(Root, true);
            g.Slot = HaloGun.Holster.None;
            g.MagIn = true;           // (a magazine left out goes back in: the rounds are the weapon's)
            g.NeedsRack = false;
            g.ResetModel();
            g.gameObject.SetActive(true);
            if (at.HasValue) g.transform.position = at.Value + Vector3.up * 0.3f;
            Vector3 fl = Flat(g.transform.forward);
            g.transform.rotation = Quaternion.LookRotation(fl, Vector3.Cross(fl, Vector3.up));      // (it falls already lying on its side)
            var it = new Item
            {
                Id = _nextDrop++, Tag = g.Def.Tag, Gun = g, Present = true, Dropped = true, DespawnAt = Time.time + (HaloCampaign.Active ? CampaignDropSeconds : DropSeconds),
                Falling = true, Velocity = velocity, Shooter = local ? HaloLocal.NetId : -1,
            };
            Items.Add(it);
            if (local) Raise(LocalDropped, it);
            return it;
        }

        // ------------------------------------------------------------------ the net layer

        /// <summary>Someone else took an item (spot index or drop id): it goes from this world too.</summary>
        public static void ApplyRemoteTaken(int id)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Id != id || !Items[i].Present) continue;
                Remove(Items[i], false);
                return;
            }
        }

        /// <summary>Another player dropped a weapon (their drop id, weapon, rounds loaded + reserve, where, how fast).</summary>
        public static Item RemoteDrop(int id, HaloTag weapon, int loaded, int reserve, Vector3 pos, Vector3 vel, int slot)
        {
            var def = HaloCombat.Catalog != null ? HaloCombat.Catalog.Weapon(weapon) : null;
            if (def == null) return null;
            var g = HaloGun.Create(def, Root);
            if (g == null) return null;
            g.SetScale(1f);
            g.State.Loaded = loaded; g.State.Reserve = reserve;
            g.transform.position = pos;
            var it = DropWeapon(g, vel, false);
            if (it != null) { it.Id = id; it.Shooter = slot; }
            return it;
        }

        /// <summary>The pickup sound of a weapon's ammunition (its magazine object's equipment), null when none.</summary>
        private static HaloTag AmmoSound(HaloWeaponDef d)
        {
            var cat = HaloCombat.Catalog;
            if (cat == null || d.PrimaryMagazine == null) return null;
            foreach (var o in d.PrimaryMagazine.Objects) { var eq = cat.Equipment(o.Equipment); if (eq != null && eq.PickupSound != null) return eq.PickupSound; }
            return null;
        }

        private static string Name(Item it) { return it.Gun != null ? it.Gun.Def.Name : it.Equipment != null ? it.Equipment.Name : it.Tag != null ? it.Tag.Name : "?"; }

        private static void Raise(Action<Item> e, Item it)
        {
            if (e == null) return;
            foreach (Delegate d in e.GetInvocationList())
            {
                try { ((Action<Item>)d)(it); }
                catch (Exception ex) { HaloLog.Warn("Combat: a pickup listener failed: " + ex.Message); }
            }
        }
    }
}

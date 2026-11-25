using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Vehicles module] Vehicles of the match: placement, driving, seats, passengers, vehicle weapons.
    ///
    /// Which vehicles: the scenario's vehicle placements (Halo's "multiplayer spawn flags": bits 0-3 = default in slayer / ctf / king /
    /// oddball, bits 8-11 = allowed in them) chosen by the game variant's vehicle set (HaloRules.Vehicles): 0 the map's default set for
    /// the game type, 1 none, 2 warthogs, 3 ghosts, 4 scorpions, 5 rocket warthogs, 6 banshees, 7 gun turrets (every placement of that
    /// kind allowed in the game type), 8 custom (every allowed placement - ours). Race, Terminator and the other types use the slayer
    /// bits (ours). Halo CE multiplayer vehicles cannot be destroyed (their collision models have no body vitality); an empty one that
    /// was moved away from its spot or turned over goes back to it after "vehicles.respawn" seconds (default 30, ours).
    /// </summary>
    public static class HaloVehicles
    {
        public static readonly List<HaloVehicle> All = new List<HaloVehicle>();
        private static readonly Dictionary<HaloTag, HaloVehicleDef> Defs = new Dictionary<HaloTag, HaloVehicleDef>();
        private static GameObject _root;
        private static bool _subscribed;

        /// <summary>The lab runs vehicles outside a Playing session.</summary>
        public static bool LabMode;

        public static float RespawnSeconds => HaloSettings.Num("vehicles.respawn", 30f);

        /// <summary>Places the scenario's vehicles allowed by the rules (after HaloSession.WorldBuilt).</summary>
        public static void Spawn(HaloRules rules)
        {
            Clear();
            var scen = HaloSession.Scenario;
            if (scen == null) { HaloLog.Warn("Vehicles: no scenario to place vehicles from."); return; }
            Spawn(scen, rules ?? new HaloRules(), HaloSession.WorldRoot);
        }

        public static void Spawn(HaloScenario scen, HaloRules rules, Transform parent)
        {
            Clear();
            Subscribe();
            _root = new GameObject("Halo Vehicles");
            if (parent != null) _root.transform.SetParent(parent, false);
            _root.AddComponent<HaloVehicleDirector>();
            _root.AddComponent<HaloVehicleLate>();
            _root.AddComponent<HaloSeatPoses>();
            int bit = SpawnBit(rules.Type);
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < scen.Vehicles.Count; i++)
            {
                var p = scen.Vehicles[i];
                if (p.Type == null || !Wanted(p, rules.Vehicles, bit)) continue;
                HaloVehicleDef def;
                try { def = DefOf(p.Type); }
                catch (Exception e) { HaloLog.Warn("Vehicles: " + p.Type + " unreadable: " + e.Message); continue; }
                if (def == null) continue;
                try
                {
                    var v = HaloVehicle.Create(i, p, def, _root.transform);
                    All.Add(v);
                    int c; counts.TryGetValue(v.Label, out c); counts[v.Label] = c + 1;
                }
                catch (Exception e) { HaloLog.Error("Vehicles: building " + p.Type + " failed: " + e); }
            }
            string list = "";
            foreach (var kv in counts) list += (list.Length > 0 ? ", " : "") + kv.Value + " " + kv.Key;
            HaloLog.Info("Vehicles: placed " + All.Count + " of " + scen.Vehicles.Count + " scenario vehicles for " + rules.Type + " (vehicle set " + rules.Vehicles + "): " + (list.Length > 0 ? list : "none") + ".");
        }

        /// <summary>Lab / tools: one vehicle of a tag at a Unity position and heading (degrees), outside any scenario.</summary>
        public static HaloVehicle AddLab(HaloTag vehi, Vector3 position, float yawDegrees, Transform parent)
        {
            Subscribe();
            if (_root == null)
            {
                _root = new GameObject("Halo Vehicles");
                if (parent != null) _root.transform.SetParent(parent, false);
                _root.AddComponent<HaloVehicleDirector>();
                _root.AddComponent<HaloVehicleLate>();
                _root.AddComponent<HaloSeatPoses>();
            }
            Vector3 h = HaloSpace.ToHalo(position);
            var p = new HaloScenario.Placement { Type = vehi, Position = new HVec3(h.x, h.y, h.z), Rotation = new HVec3((90f - yawDegrees) * Mathf.Deg2Rad, 0f, 0f), Team = -1 };
            var v = HaloVehicle.Create(1000 + All.Count, p, DefOf(vehi), _root.transform);
            All.Add(v);
            return v;
        }

        /// <summary>
        /// Campaign (and any scripted) placement of ONE scenario vehicle: the placement's full rotation (yaw / pitch / roll) and desired
        /// permutation, no multiplayer spawn-flag / team filtering, no abandoned-vehicle respawn, the tag's own vitality (campaign banshees and
        /// ghosts can be destroyed), registered in HaloObjects under <paramref name="objectName"/>. Flying hulls (pelican, dropship: human
        /// plane) stay where they are placed (kinematic) instead of settling. Creates the vehicles' root and frame drivers when missing.
        /// </summary>
        public static HaloVehicle SpawnPlacement(HaloScenario.Placement p, string objectName, Transform parent)
        {
            int index = -1;
            var scen = HaloSession.Scenario;
            if (scen != null) index = scen.Vehicles.IndexOf(p);
            return SpawnPlacement(index >= 0 ? index : 2000 + All.Count, p, objectName, parent);
        }

        /// <summary>K's form: the scenario vehicle index is the vehicle's id (the net layer keys vehicles by it).</summary>
        public static HaloVehicle SpawnPlacement(int scenarioIndex, HaloScenario.Placement p)
        {
            string name = "";
            var scen = HaloSession.Scenario;
            if (scen != null && p != null && p.NameIndex >= 0 && p.NameIndex < scen.ObjectNames.Count) name = scen.ObjectNames[p.NameIndex];
            return SpawnPlacement(scenarioIndex, p, name, HaloSession.WorldRoot);
        }

        private static HaloVehicle SpawnPlacement(int id, HaloScenario.Placement p, string objectName, Transform parent)
        {
            if (p == null || p.Type == null) return null;
            Subscribe();
            if (_root == null)
            {
                _root = new GameObject("Halo Vehicles");
                if (parent != null) _root.transform.SetParent(parent, false);
                _root.AddComponent<HaloVehicleDirector>();
                _root.AddComponent<HaloVehicleLate>();
                _root.AddComponent<HaloSeatPoses>();
            }
            HaloVehicleDef def;
            try { def = DefOf(p.Type); }
            catch (Exception e) { HaloLog.Warn("Vehicles: " + p.Type + " unreadable: " + e.Message); return null; }
            HaloVehicle v;
            try { v = HaloVehicle.Create(id, p, def, _root.transform); }
            catch (Exception e) { HaloLog.Error("Vehicles: building " + p.Type + " failed: " + e); return null; }
            v.Campaign = HaloCampaign.Active || HaloVehicles.CampaignLab;
            v.ObjectName = objectName ?? "";
            if (v.ObjectName.Length > 0) { v.gameObject.name += " '" + v.ObjectName + "'"; HaloObjects.Register(v.ObjectName, v.gameObject); }
            if (def.Type == HaloVehicleDef.Kind.HumanPlane) v.Park(false);
            All.Add(v);
            if (!_saidCampaign)
            {
                _saidCampaign = true;
                HaloLog.Info("Vehicles: campaign placements: full rotation, desired permutation, named (HaloObjects), no MP respawn; vitality from the tags (" +
                             def.Name + ": " + (def.Collision != null && def.Collision.MaxBody > 0f ? def.Collision.MaxBody.ToString("0") + " body points" : "indestructible") + ").");
            }
            return v;
        }

        private static bool _saidCampaign;

        /// <summary>Lab: SpawnPlacement treats vehicles as campaign ones outside a mission.</summary>
        public static bool CampaignLab;

        /// <summary>object_destroy: the vehicle goes (the local player and seated units get out first).</summary>
        public static void Remove(HaloVehicle v)
        {
            if (v == null) return;
            if (HaloVehicleSeat.Local.Vehicle == v) HaloVehicleSeat.Local.Release();
            for (int i = 0; i < v.Seats.Count; i++) if (v.Seats[i].Unit != null) v.UnseatUnit(v.Seats[i].Unit);
            if (v.ObjectName.Length > 0 && HaloObjects.Find(v.ObjectName) == v.gameObject) HaloObjects.Unregister(v.ObjectName);
            All.Remove(v);
            UnityEngine.Object.Destroy(v.gameObject);
        }

        public static HaloVehicleDef DefOf(HaloTag tag)
        {
            HaloVehicleDef d;
            if (!Defs.TryGetValue(tag, out d)) { d = new HaloVehicleDef(tag); Defs[tag] = d; }
            return d;
        }

        private static int SpawnBit(HaloScenario.GameType t)
        {
            switch (t)
            {
                case HaloScenario.GameType.Ctf: return 1;
                case HaloScenario.GameType.King: return 2;
                case HaloScenario.GameType.Oddball: return 3;
                default: return 0;
            }
        }

        private static bool Wanted(HaloScenario.Placement p, int set, int bit)
        {
            bool isDefault = (p.MpSpawnFlags & (1 << bit)) != 0;
            bool allowed = (p.MpSpawnFlags & (1 << (8 + bit))) != 0 || isDefault;
            if (set == 0) return isDefault;
            if (set == 1) return false;
            if (set == 8) return allowed;
            string n = p.Type.Name.ToLowerInvariant();
            bool kind;
            switch (set)
            {
                case 2: kind = n.Contains("warthog") && !n.Contains("rwarthog"); break;
                case 3: kind = n.Contains("ghost"); break;
                case 4: kind = n.Contains("scorpion"); break;
                case 5: kind = n.Contains("rwarthog"); break;
                case 6: kind = n.Contains("banshee"); break;
                case 7: kind = n.Contains("turret"); break;
                default: kind = false; break;
            }
            return kind && allowed;
        }

        private static void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            HaloEvents.LocalDied += OnLocalDied;
            HaloSession.Unloading += Clear;
        }

        private static void OnLocalDied(HaloKill k) { ExitSeat(); }

        /// <summary>Gets the local player out of any seat (death, match end): released where they are (death) - no teleport.</summary>
        public static void ExitSeat()
        {
            if (HaloVehicleSeat.Local.Seated)
            {
                HaloLog.Info("Vehicles: released from the seat (" + (HaloLocal.Alive ? "match flow" : "died") + ").");
                HaloVehicleSeat.Local.Release();
            }
        }

        /// <summary>
        /// Checkpoint revert: every carried vehicle (cargo of a pelican / dropship seat) is let go where it hangs - unparented to where it
        /// lived before it was seated, enterable again as before, not dropped (still kinematic: the revert puts it where the checkpoint had
        /// it and then settles or re-seats it). Run before placements are rebuilt or removed, so a carrier that goes does not take its cargo
        /// with it (the cargo is its child). The released vehicles are added to <paramref name="released"/> (may be null).
        /// </summary>
        public static int ReleaseAllCargo(List<HaloVehicle> released)
        {
            int n = 0;
            for (int i = 0; i < All.Count; i++)
            {
                var v = All[i];
                if (v == null || v.CarriedBy == null) continue;
                v.CarriedBy.ReleaseCargoInPlace(v);
                if (released != null) released.Add(v);
                n++;
            }
            if (n > 0) HaloLog.Info("Vehicles: " + n + " carried vehicle(s) let go for the checkpoint revert.");
            return n;
        }

        public static void Clear()
        {
            ExitSeat();
            HaloVehicleEntry.Destroy();
            HaloVehicleShots.Clear();
            HaloVehicleRiders.Clear();
            HaloSeatPoses.Clear();
            HaloVehicleCamera.Release();
            foreach (var v in All) if (v != null) UnityEngine.Object.Destroy(v.gameObject);
            All.Clear();
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }

        public static HaloVehicle Find(int id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].Id == id) return All[i];
            return null;
        }

        /// <summary>A weapon tag one of the match's vehicles carries (the Net module routes such shots to <see cref="ReplayShot"/>).</summary>
        public static bool IsVehicleWeapon(HaloTag weapon)
        {
            if (weapon == null) return false;
            for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].Gun != null && All[i].Gun.Tag == weapon) return true;
            return false;
        }

        /// <summary>A remote player's vehicle shot: visuals on the vehicle nearest the muzzle that carries this weapon.</summary>
        public static void ReplayShot(HaloTag weapon, Vector3 muzzle, Vector3 dir, int slot)
        {
            HaloVehicle best = null; float bd = 15f * 15f;
            for (int i = 0; i < All.Count; i++)
            {
                var v = All[i];
                if (v == null || v.Gun == null || v.Gun.Tag != weapon) continue;
                float d = (v.transform.position - muzzle).sqrMagnitude;
                if (d < bd) { bd = d; best = v; }
            }
            if (best != null) best.Gun.Replay(muzzle, dir);
        }

        /// <summary>The master puts back empty vehicles left away from their spot (or overturned) for <see cref="RespawnSeconds"/>.</summary>
        public static void RespawnAbandoned()
        {
            if (!HaloVehicleNet.IsMaster) return;
            float wait = RespawnSeconds;
            if (wait <= 0f) return;
            for (int i = 0; i < All.Count; i++)
            {
                var v = All[i];
                if (v == null || v.Campaign || !v.Empty || v.RemoteDriven || v.LocalDriven) continue;
                bool away = (v.transform.position - v.SpawnPosition).sqrMagnitude > 3f * 3f || v.Overturned || v.transform.position.y < v.SpawnPosition.y - 60f;
                if (!away) { v.LastUsed = Time.time; continue; }
                if (Time.time - v.LastUsed > wait) v.Respawn();
            }
        }
    }
}

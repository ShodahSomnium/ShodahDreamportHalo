using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// What this player says about itself (the own record), filled every frame from what the other modules keep current: HaloSession (phase, map, start),
    /// HaloLocal (alive, health, shields, scores, weapons, grenades, camo, vehicle seat), the body (root, head, hands), and the events the modules raise:
    /// HaloEvents.Fired (my shots), HaloEvents.Killed (my death, as the Match module resolved it), HaloCombat.Thrown / Meleed / Reloaded, HaloPickups.Taken /
    /// LocalDropped, and the Match module's objective calls (<see cref="SetCarrying"/>, <see cref="NoteGameEvent"/>).
    /// </summary>
    public static partial class HaloNetGame
    {
        /// <summary>Lab: stands in for the Bridge player's root / head / hands of the local body (null in the world).</summary>
        public static Transform LabRoot, LabHead, LabLeft, LabRight;

        /// <summary>A remote player's objective / game event (the Match module's codes): slot, kind, argument. Their position is HaloNetPlayer.Position.</summary>
        public static event Action<int, int, int> GameEvent;

        private static readonly HaloBody Body = new HaloBody();
        private static bool _subscribed;
        private static int _carrying = -1, _reloads;
        private static int _drivingFrame = -1, _aimFrame = -1;
        private static Vector3 _drivePos, _driveVel, _aimDir = Vector3.forward;
        private static Quaternion _driveRot = Quaternion.identity;
        private static float _driveHealth = 1f;
        private static int _seatFrame = -1, _seatVehicle = -1, _seatIndex = -1;

        private static void Subscribe()
        {
            if (_subscribed) return;
            HaloEvents.Fired += OnFired;
            HaloEvents.Killed += OnKilled;
            HaloSession.MapOpened += OnMapOpened;
            HaloCombat.Thrown += OnThrown;
            HaloCombat.Meleed += OnMeleed;
            HaloCombat.Reloaded += OnReloaded;
            HaloPickups.Taken += OnTaken;
            HaloPickups.LocalDropped += OnLocalDropped;
            HaloHud.TeamOfCollider = HudTeamOf;
            HaloHud.NameOf = NameOf;
            _subscribed = true;
        }

        private static void Unsubscribe()
        {
            if (!_subscribed) return;
            HaloEvents.Fired -= OnFired;
            HaloEvents.Killed -= OnKilled;
            HaloSession.MapOpened -= OnMapOpened;
            HaloCombat.Thrown -= OnThrown;
            HaloCombat.Meleed -= OnMeleed;
            HaloCombat.Reloaded -= OnReloaded;
            HaloPickups.Taken -= OnTaken;
            HaloPickups.LocalDropped -= OnLocalDropped;
            if (HaloHud.TeamOfCollider == (Func<Collider, int>)HudTeamOf) HaloHud.TeamOfCollider = null;
            if (HaloHud.NameOf == (Func<int, string>)NameOf) HaloHud.NameOf = null;
            _subscribed = false;
        }

        /// <summary>The HUD's red crosshair: the team of a remote hit box (-1 a free-for-all player, -2 not a player).</summary>
        private static int HudTeamOf(Collider c)
        {
            HaloNetPlayer p;
            if (!IsRemoteHitbox(c, out p)) return -2;
            return Up && Net.TeamGame ? p.Team : -1;
        }

        // ------------------------------------------------------------------ the Note* calls (Combat, Vehicles, Match)

        /// <summary>Combat: a grenade (its proj or eqip tag) left my hand at <paramref name="pos"/> with <paramref name="vel"/> (HaloCombat.Thrown does it too).</summary>
        public static void NoteThrow(HaloTag grenade, Vector3 pos, Vector3 vel)
        {
            if (!Up) return;
            Net.NoteThrow(HaloNetU.TagIndex(grenade), HaloNetU.V(pos), HaloNetU.V(vel));
        }

        /// <summary>Combat: I swung a melee attack (HaloCombat.Meleed does it too).</summary>
        public static void NoteMelee()
        {
            if (Up) Net.NoteMelee();
        }

        /// <summary>Combat: I took a pickup (the scenario NetEquipment index) or a dropped weapon (its drop id, 1024 and up). HaloPickups.Taken does it too.</summary>
        public static void NotePickup(int item)
        {
            if (Up) Net.NotePickup(item);
        }

        /// <summary>Combat: I dropped a weapon (by hand or by dying). Returns the drop id the others will know it by (-1 offline / unclaimed). HaloPickups.LocalDropped does it too.</summary>
        public static int NoteDrop(HaloTag weapon, int loaded, int reserve, float charge, Vector3 pos, Vector3 vel)
        {
            if (!Up || !Net.Claimed) return -1;
            return Net.NoteDrop(HaloNetU.TagIndex(weapon), loaded, reserve, charge, HaloNetU.V(pos), HaloNetU.V(vel));
        }

        /// <summary>Match: the objective I carry (0 red flag, 1 blue flag, 2 oddball, -1 none). Without the call HaloLocal.Carry is published.</summary>
        public static void SetCarrying(int objective)
        {
            _carrying = objective < 0 || objective > 6 ? -1 : objective;
            _carryingSet = true;
        }

        private static bool _carryingSet;

        /// <summary>Match: an objective / game event of mine (kind and argument 0..255 each; the others get <see cref="GameEvent"/> with my slot).</summary>
        public static void NoteGameEvent(int kind, int arg)
        {
            if (!Up || !Net.Claimed) return;
            Body.Update();
            Net.NoteObjective(kind, arg, HaloNetU.V(Body.Valid ? Body.Feet : HaloLocal.Position));
        }

        /// <summary>Vehicles: the driver's vehicle pose this frame (else Net reads HaloLocal.Vehicle's transform and rigidbody).</summary>
        public static void PublishDriving(Vector3 pos, Quaternion rot, Vector3 vel, float health01)
        {
            _drivingFrame = Time.frameCount;
            _drivePos = pos; _driveRot = rot; _driveVel = vel; _driveHealth = health01;
        }

        /// <summary>Vehicles: the aim of the vehicle gun / turret I man this frame (else the head's forward).</summary>
        public static void PublishAim(Vector3 dir)
        {
            _aimFrame = Time.frameCount;
            _aimDir = dir;
        }

        /// <summary>Vehicles (optional): my seat this frame, overriding HaloLocal.VehicleId / SeatIndex (-1, -1 = on foot).</summary>
        public static void SetSeat(int vehicle, int seat)
        {
            _seatFrame = Time.frameCount;
            _seatVehicle = vehicle;
            _seatIndex = seat;
        }

        // ------------------------------------------------------------------ events of the other modules

        private static void OnFired(HaloTag weapon, Vector3 muzzle, Vector3 dir, int who)
        {
            if (!Up || who < 0 || who != HaloLocal.NetId || !Net.Claimed) return;
            Net.NoteShot(HaloNetU.TagIndex(weapon), HaloNetU.V(muzzle), HaloNetU.V(dir));
        }

        private static void OnKilled(HaloKill k)
        {
            if (!Up || !Net.Claimed || k.Victim != HaloLocal.NetId || k.Victim < 0) return;
            int killer = k.Killer >= 0 && k.Killer < HaloNetWire.MaxSlots ? k.Killer : HaloNetWire.NoSlot;
            Net.NoteDeath(killer, HaloNetU.TagIndex(k.Cause), k.Melee, k.Headshot, false, k.Splatter, k.Suicide, k.Grenade, k.Betrayal);
            HaloLog.Info("Net: my death announced (killer " + (killer == HaloNetWire.NoSlot ? "none" : NameOf(killer)) + ", " + (k.Cause != null ? k.Cause.Name : "no cause") + ")");
        }

        private static void OnThrown(HaloTag grenade, Vector3 pos, Vector3 vel) { NoteThrow(grenade, pos, vel); }

        private static void OnMeleed() { NoteMelee(); }

        private static void OnReloaded()
        {
            _reloads++;
            HaloLocal.Weapon w = HaloLocal.HeldWeapon;
            _reloadEmpty = w != null && w.Loaded <= 0;
        }

        private static bool _reloadEmpty;

        private static void OnTaken(HaloPickups.Item it)
        {
            if (it != null) NotePickup(it.Id);
        }

        private static void OnLocalDropped(HaloPickups.Item it)
        {
            if (it == null || !Up || !Net.Claimed) return;
            int loaded = 0, reserve = 0;
            float charge = 1f;
            if (it.Gun != null && it.Gun.State != null) { loaded = it.Gun.State.Loaded; reserve = it.Gun.State.Reserve; charge = it.Gun.State.Battery; }
            Vector3 pos = it.Gun != null ? it.Gun.transform.position : it.Home;
            int id = NoteDrop(it.Tag, loaded, reserve, charge, pos, it.Velocity);
            if (id >= 0) it.Id = id;
        }

        // ------------------------------------------------------------------ the own record

        private static void Publish()
        {
            ref HaloNetState o = ref Net.Own;
            HaloSession.Phase ph = HaloSession.Current;
            bool match = (ph == HaloSession.Phase.Playing || ph == HaloSession.Phase.Ending) && HaloSession.MapEntry != null;
            HaloRules rules = HaloSession.Rules;
            bool teams = rules != null && (rules.Teams || rules.Type == HaloScenario.GameType.Ctf);
            Net.TeamGame = match && teams;
            Net.FriendlyFire = rules != null && rules.FriendlyFire;
            Net.TeamFromGame = match;
            if (match)
            {
                if (o.Status != HaloNetStatus.Playing)
                {
                    o.Deaths = 0;      // a new match: the per-match death counter starts over (readers meet this record anew: the game number changed)
                    _carryingSet = false;
                    _carrying = -1;
                }

                o.Status = HaloNetStatus.Playing;
                o.MatchState = ph == HaloSession.Phase.Ending ? HaloMatchState.Finished : HaloMatchState.Running;
                o.ZoneHash = HaloNetWire.NameHash(HaloSession.MapEntry.Name);
                o.MatchSeq = _ownSeq;
                o.MatchStart = (int)HaloSession.StartUtc;
                o.MapPrint = HaloNetU.MapPrint();
                if (HaloLocal.Team == HaloTeams.Red || HaloLocal.Team == HaloTeams.Blue) o.Team = HaloLocal.Team;
            }
            else
            {
                if (o.Status == HaloNetStatus.Playing || o.Status == HaloNetStatus.Free) o.Status = HaloNetStatus.Present;
                o.MatchState = HaloMatchState.None;
                o.MatchSeq = 0;
                // in the hub the net side is the lobby's (HaloMatchControl.AssignTeam keeps a red / blue HaloLocal.Team for the match)
                if (Net.Claimed && Net.Online && ph == HaloSession.Phase.Hub) HaloLocal.Team = (o.Team & 1) == 0 ? HaloTeams.Red : HaloTeams.Blue;
            }

            o.Alive = match && HaloLocal.Alive;
            o.Health = HaloLocal.Health;
            o.Shield = HaloLocal.Shield;
            o.Kills = HaloLocal.Kills;
            o.Assists = HaloLocal.Assists;
            o.Score = HaloLocal.Score;
            o.LivesLeft = HaloLocal.LivesLeft;
            o.Carry = (_carryingSet ? _carrying + 1 : HaloLocal.Carry) & 7;
            o.Camo = HaloLocal.Camo || HaloLocal.CamoUntil > Time.time;
            o.Zoomed = HaloLocal.Zoomed;
            o.Reloading = HaloLocal.Reloading;
            o.InHand = HaloLocal.InHand;
            o.ReloadCounter = _reloads & 0xF;
            o.ReloadEmpty = _reloadEmpty;

            // weapons: the one in hand (or the drawn one) and the other; the objective (flag / ball) is held like a weapon
            HaloLocal.Weapon held = HaloLocal.HeldWeapon;
            HaloLocal.Weapon other = null;
            for (int i = 0; i < HaloLocal.Weapons.Length; i++)
                if (HaloLocal.Weapons[i] != null && HaloLocal.Weapons[i] != held) other = HaloLocal.Weapons[i];
            if (HaloLocal.Objective != null)
            {
                o.Held = HaloNetU.TagIndex(HaloLocal.Objective);
                o.InHand = true;
                o.Stowed = held != null ? HaloNetU.TagIndex(held.Tag) : HaloNetWire.NoTag;
            }
            else
            {
                o.Held = held != null ? HaloNetU.TagIndex(held.Tag) : HaloNetWire.NoTag;
                o.Stowed = other != null ? HaloNetU.TagIndex(other.Tag) : HaloNetWire.NoTag;
            }

            o.Overheated = held != null && held.Overheated;
            o.Frags = HaloLocal.FragGrenades;
            o.Plasmas = HaloLocal.PlasmaGrenades;

            // the body
            Body.OverrideRoot = LabRoot; Body.OverrideHead = LabHead; Body.OverrideLeft = LabLeft; Body.OverrideRight = LabRight;
            if (Body.Update())
            {
                o.Root = HaloNetU.V(Body.Feet);
                o.RootYaw = Mathf.Atan2(Body.Forward.x, Body.Forward.z) * Mathf.Rad2Deg;
                o.Head = HaloNetU.V(Body.HeadPos);
                o.HeadRot = HaloNetU.Q(Body.HeadRot);
                o.Vr = Body.Vr;
                o.Crouch = Body.HeadPos.y - Body.Feet.y < 1.15f * Body.AvatarScale;     // ours: two thirds of a standing 1.7 m head
                if (Body.Vr)
                {
                    Vector3 p;
                    Quaternion q;
                    Body.HandPose(false, out p, out q);
                    o.LHand = HaloNetU.V(p);
                    o.LRot = HaloNetU.Q(q);
                    Body.HandPose(true, out p, out q);
                    o.RHand = HaloNetU.V(p);
                    o.RRot = HaloNetU.Q(q);
                }
            }

            // the vehicle seat; the driver's vehicle pose
            bool seatCall = _seatFrame >= Time.frameCount - 1;
            o.Vehicle = !match ? -1 : seatCall ? _seatVehicle : HaloLocal.VehicleId;
            o.Seat = o.Vehicle >= 0 ? (seatCall ? _seatIndex : HaloLocal.SeatIndex) : -1;
            o.Driving = o.Vehicle >= 0 && HaloLocal.Driving;
            if (o.Driving)
            {
                if (_drivingFrame >= Time.frameCount - 1)
                {
                    o.VehiclePos = HaloNetU.V(_drivePos); o.VehicleRot = HaloNetU.Q(_driveRot); o.VehicleVel = HaloNetU.V(_driveVel); o.VehicleHealth = _driveHealth;
                }
                else if (HaloLocal.Vehicle != null)
                {
                    Transform t = HaloLocal.Vehicle.transform;
                    o.VehiclePos = HaloNetU.V(t.position);
                    o.VehicleRot = HaloNetU.Q(t.rotation);
                    Rigidbody rb = HaloLocal.Vehicle.GetComponent<Rigidbody>();
                    o.VehicleVel = rb != null ? HaloNetU.V(rb.linearVelocity) : default(HaloV3);
                    o.VehicleHealth = 1f;
                }
            }

            if (_aimFrame >= Time.frameCount - 1) o.Aim = HaloNetU.V(_aimDir);
            else o.Aim = HaloNetU.V(Body.Valid ? Body.HeadRot * Vector3.forward : Vector3.forward);
        }
    }
}

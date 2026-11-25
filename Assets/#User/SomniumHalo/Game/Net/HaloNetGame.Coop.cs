using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Campaign co-op (DevTools/agents/N/NOTES.md "Campaign co-op"). The instance master HOSTS the mission (scripts, AI, devices, checkpoints)
    /// and publishes its AI units and the mission's visible state; every other whitelisted player in the master's mission draws them: a puppet per
    /// unit (HaloBipeds.Spawn in puppet mode, or a stand-in until the Characters module has one), its shots, its death; the BSP, the cutscene, the
    /// objects the scripts create / destroy, the device positions. Hits: a client's hit on a puppet goes to the host (HaloNet.NoteHitActor), the
    /// host's AI hits on a remote player go to that player (the AI bit). Solo, offline or without the scene's co-op objects nothing here runs.
    /// </summary>
    public static partial class HaloNetGame
    {
        /// <summary>
        /// This client runs the mission's scripts and AI: true solo / offline / as the instance master; false for a player in the master's mission (it only
        /// draws what the host publishes). The Mission module reads it before HaloScript.Start / HaloAi.
        /// </summary>
        public static bool CampaignHost
        {
            get
            {
                if (!Up || !Net.Online || !Net.Claimed || Net.IsInstanceMaster) return true;
                return !(Net.Game.On && (LabCampaign || (Net.Lobby.RulesC & HaloNetRules.CampaignBit) != 0));
            }
        }

        /// <summary>Lab: treat the running match as a campaign mission (the co-op path without the Mission module).</summary>
        public static bool LabCampaign;

        /// <summary>Co-op is live: I host or follow a mission of the master's game with other players and the co-op blocks exist.</summary>
        public static bool CoopLive { get { return Up && Net.CoopMission && Net.Coop.HaveActors; } }

        /// <summary>Host: the Mission module (or Net itself from HaloObjects) tells a scenario object was created / destroyed (by its name).</summary>
        public static void NoteObjectCreated(string name) { NoteObjectEvent(HaloNetMissionEvent.Create, name); }
        public static void NoteObjectDestroyed(string name) { NoteObjectEvent(HaloNetMissionEvent.Destroy, name); }

        /// <summary>Host: the unit is tracked for the wire (the Characters module's units are found by themselves once a second; this is immediate).</summary>
        public static void TrackUnit(HaloUnit u)
        {
            if (u != null && !HostUnits.Contains(u)) HostUnits.Add(u);
        }

        /// <summary>The puppet / host unit with a serial (lab and tests).</summary>
        public static HaloUnit UnitOfSerial(int serial)
        {
            HaloUnit u;
            if (SerialUnit.TryGetValue(serial, out u) && u != null) return u;
            for (int i = 0; i < Puppets.Length; i++) if (Puppets[i] != null && Puppets[i].Serial == serial) return Puppets[i].Unit;
            return null;
        }

        /// <summary>The drawn puppet at an actor place 0..62 (null when none).</summary>
        public static HaloNetPuppet PuppetAt(int place) { return place >= 0 && place < Puppets.Length ? Puppets[place] : null; }

        /// <summary>Counters for the lab: actors published (host), puppets made / alive (client), events replayed, hits on actors sent / applied.</summary>
        public static int StatActorsPublished, StatPuppets, StatMissionEvents, StatActorHitsSent, StatActorHitsApplied;

        private static readonly List<HaloUnit> HostUnits = new List<HaloUnit>(64);
        private static readonly Dictionary<HaloUnit, int> UnitSerial = new Dictionary<HaloUnit, int>(64);
        private static readonly Dictionary<int, HaloUnit> SerialUnit = new Dictionary<int, HaloUnit>(64);
        private static readonly HaloUnit[] Places = new HaloUnit[HaloNetCoop.MaxActors];
        private static readonly float[] DeadAt = new float[HaloNetCoop.MaxActors];
        private static readonly int[] FireCount = new int[HaloNetCoop.MaxActors];
        private static readonly float[] NextShot = new float[HaloNetCoop.MaxActors];
        private static readonly bool[] WasFiring = new bool[HaloNetCoop.MaxActors], WasMelee = new bool[HaloNetCoop.MaxActors], WasThrow = new bool[HaloNetCoop.MaxActors], ExactShots = new bool[HaloNetCoop.MaxActors];
        private static readonly int[] LastShots = new int[HaloNetCoop.MaxActors], MeleeCount = new int[HaloNetCoop.MaxActors], GrenadeCount = new int[HaloNetCoop.MaxActors];
        private static readonly float[] DeviceShown = new float[HaloNetCoop.Devices];
        private static readonly HaloNetPuppet[] Puppets = new HaloNetPuppet[HaloNetCoop.MaxActors];
        private static int _nextSerial = 1, _checkpoints, _coopLoggedState = -1, _coopErrors;
        private static float _nextDiscover, _nextHostWrite;
        private static bool _coopSubscribed;
        private static Transform _puppetRoot;

        /// <summary>Seconds between the shots counted for a unit that holds its trigger (ours: until the Characters module counts its own shots).</summary>
        public static float AiShotInterval = 0.15f;

        private static void NoteObjectEvent(int kind, string name)
        {
            if (!Up || !Net.Coop.Writing || string.IsNullOrEmpty(name)) return;
            HaloScenario sc = HaloSession.Scenario;
            int index = sc != null ? sc.ObjectNames.IndexOf(name) : -1;
            if (index < 0) return;
            Net.Coop.AddEvent(kind, index);
        }

        // ------------------------------------------------------------------ every frame (from Tick, after the session ticked)

        private static void StepCoopGame()
        {
            bool campaign = (HaloCampaign.Active || LabCampaign) && HaloSession.InMatch && _ownSeq != 0;
            Net.CoopMission = campaign;
            if (!_coopSubscribed)
            {
                HaloCampaign.Checkpoint += OnCoopCheckpoint;
                _coopSubscribed = true;
            }

            int state = !campaign ? 0 : Net.Coop.Writing ? 1 : 2;
            if (state != _coopLoggedState)
            {
                int was = _coopLoggedState;
                _coopLoggedState = state;
                string mission = LabCampaign && HaloSession.MapEntry != null ? HaloSession.MapEntry.Name : HaloCampaign.Mission;
                if (state != 0) HaloLog.Info("Net: campaign co-op - " + (state == 1 ? "I host " + mission + " (scripts and AI run here)" : "I follow the master's " + mission + " (its AI is drawn here)")
                                             + (Net.Coop.HaveActors ? "" : "; waiting for the co-op blocks"));
                if (was == 2 && state == 1 && _ownSeq == _coopSeq && _followed) Promote();      // the master left mid-mission: I host it now
                if (state == 0) HaloNetHostState.Reset();
            }

            if (_coopSeq != (campaign ? _ownSeq : -1)) _followed = false;      // (another mission: nothing followed yet)
            _coopSeq = campaign ? _ownSeq : -1;
            if (!campaign) { ClearCoop(); return; }
            if (Net.Coop.Writing) { HostStep(); HaloNetHostState.HostTick(Net); SubscribeSound(); Net.Coop.NewSounds.Clear(); HostPresses(); }
            else { ClientStep(); FollowSounds(); FollowPresses(); HaloNetHostState.FollowerTick(Net); if (Net.Coop.HaveActors) _followed = true; }
        }

        private static int _coopSeq = -1;
        private static bool _followed;

        /// <summary>The host's serial of one of its units (0 when it is not on the wire).</summary>
        public static int SerialOf(HaloUnit u)
        {
            int s;
            return u != null && UnitSerial.TryGetValue(u, out s) ? s : 0;
        }

        /// <summary>
        /// The hand-over on the new host: every puppet that is a real unit becomes a hosted unit again (Puppet off, the same serial and place, so the
        /// other followers keep their puppets); stand-ins go (they had no unit to run). Then the host state's parts adopt the old host's state.
        /// </summary>
        private static void Promote()
        {
            int kept = 0, dropped = 0, maxSerial = 0;
            for (int p = 0; p < Puppets.Length; p++)
            {
                HaloNetPuppet pup = Puppets[p];
                if (pup == null) continue;
                Puppets[p] = null;
                HaloUnit u = pup.Unit;
                int serial = pup.Serial;
                if (u == null) { pup.Retire(); dropped++; continue; }
                UnityEngine.Object.Destroy(pup);
                u.Puppet = false;
                UnitSerial[u] = serial;
                SerialUnit[serial] = u;
                Places[p] = u;
                DeadAt[p] = 0f;
                HaloNetActor last = pup.Last;
                FireCount[p] = last.FireCounter;
                MeleeCount[p] = last.MeleeCounter;
                GrenadeCount[p] = last.GrenadeCounter;
                LastShots[p] = u.Shots;
                ExactShots[p] = false;
                if (!HostUnits.Contains(u)) HostUnits.Add(u);
                if (serial > maxSerial) maxSerial = serial;
                kept++;
            }

            _nextSerial = maxSerial % 4095 + 1;
            HaloLog.Info("Net: hand-over - " + kept + " unit(s) taken over from the old host" + (dropped > 0 ? ", " + dropped + " stand-in(s) dropped" : ""));
            PromoteVehicles();
            HaloNetHostState.Promote(Net);
        }

        private static void OnCoopCheckpoint() { _checkpoints++; HaloNetHostState.CheckpointTaken(); }

        private static void ClearCoop()
        {
            ClearSounds();
            _pressHosting = false;
            if (HostUnits.Count == 0 && UnitSerial.Count == 0 && StatPuppets == 0 && _puppetRoot == null) return;
            HostUnits.Clear();
            UnitSerial.Clear();
            SerialUnit.Clear();
            for (int i = 0; i < Places.Length; i++) { Places[i] = null; DeadAt[i] = 0f; FireCount[i] = 0; WasFiring[i] = false; }
            for (int i = 0; i < DeviceShown.Length; i++) DeviceShown[i] = -1f;
            ClearVehicles();
            for (int i = 0; i < Puppets.Length; i++)
            {
                if (Puppets[i] != null) Puppets[i].Retire();
                Puppets[i] = null;
            }

            if (_puppetRoot != null) UnityEngine.Object.Destroy(_puppetRoot.gameObject);
            _puppetRoot = null;
            StatPuppets = 0;
        }

        // ------------------------------------------------------------------ the host

        private static void HostStep()
        {
            float now = Time.unscaledTime;
            if (now >= _nextDiscover)
            {
                _nextDiscover = now + 0.5f;
                List<HaloUnit> all = HaloBipeds.All;
                for (int i = 0; i < all.Count; i++)
                {
                    HaloUnit u = all[i];
                    if (u != null && !u.Puppet && !HostUnits.Contains(u)) HostUnits.Add(u);
                }
            }

            // places: units keep theirs; the dead keep it 10 s (their bodies lie there), then it is free
            for (int i = HostUnits.Count - 1; i >= 0; i--)
            {
                HaloUnit u = HostUnits[i];
                if (u != null && UnitSerial.ContainsKey(u)) continue;
                if (u == null) { HostUnits.RemoveAt(i); continue; }
                int place = FreePlace();
                if (place < 0) continue;      // more than 63 units: the rest are not shown to the others until a place frees
                int serial = _nextSerial;
                _nextSerial = _nextSerial % (HaloNetCoop.VehicleSerialBase - 1) + 1;
                UnitSerial[u] = serial;
                SerialUnit[serial] = u;
                Places[place] = u;
                DeadAt[place] = 0f;
                FireCount[place] = 0;
                LastShots[place] = u.Shots;
                ExactShots[place] = false;
                MeleeCount[place] = GrenadeCount[place] = 0;
            }

            if (now < _nextHostWrite) return;
            _nextHostWrite = now + 0.1f;
            HaloNetCoop c = Net.Coop;
            for (int p = 0; p < Places.Length; p++)
            {
                HaloUnit u = Places[p];
                if (u == null)
                {
                    if (VehAt[p] == null) c.ClearActor(p);      // (a vehicle record lives on this place)
                    continue;
                }

                if (!u.Alive)
                {
                    if (DeadAt[p] == 0f) DeadAt[p] = now;
                    else if (now - DeadAt[p] > 10f) { FreeUnit(p); c.ClearActor(p); continue; }
                }

                // shots: the unit's own volley counter (Characters), else a guess from its trigger
                bool firing = u.Alive && u.Firing;
                int shots = u.Shots;
                if (shots != LastShots[p]) { FireCount[p] += (shots - LastShots[p]) & 0xFF; LastShots[p] = shots; ExactShots[p] = true; }
                else if (!ExactShots[p] && firing && (!WasFiring[p] || now >= NextShot[p])) { FireCount[p]++; NextShot[p] = now + AiShotInterval; }
                WasFiring[p] = firing;
                if (u.Melee && !WasMelee[p]) MeleeCount[p]++;
                WasMelee[p] = u.Melee;
                if (u.ThrowGrenade && !WasThrow[p]) GrenadeCount[p]++;
                WasThrow[p] = u.ThrowGrenade;
                Transform t = u.transform;
                HaloVehicle seatV = u.Seat != null ? u.Seat.GetComponentInParent<HaloVehicle>() : null;
                Vector3 aim = u.AimPoint.sqrMagnitude > 1e-6f ? u.AimPoint - u.Eye : t.forward;
                float aimYaw = aim.sqrMagnitude > 1e-4f ? Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg : t.eulerAngles.y;
                float aimPitch = aim.sqrMagnitude > 1e-4f ? Mathf.Asin(Mathf.Clamp(aim.normalized.y, -1f, 1f)) * Mathf.Rad2Deg : 0f;
                var a = new HaloNetActor
                {
                    Serial = UnitSerial[u], Team = (int)u.Team, Alive = u.Alive, Crouch = u.Crouching, Firing = firing,
                    UnitTag = HaloNetU.TagIndex(u.UnitTag), VariantTag = HaloNetU.TagIndex(u.ActorVariant), WeaponTag = HaloNetU.TagIndex(u.Weapon),
                    Position = HaloNetU.V(t.position), Velocity = HaloNetU.V(u.MoveVelocity), BodyYaw = t.eulerAngles.y, AimYaw = aimYaw, AimPitch = aimPitch,
                    Body = u.Body, Shield = u.Shield, FireCounter = FireCount[p] & 0xFF, MeleeCounter = MeleeCount[p] & 0xF, GrenadeCounter = GrenadeCount[p] & 0xF,
                    SeatVehicle = seatV != null ? seatV.Id : -1, Seat = seatV != null ? seatV.SeatOfUnit(u) : -1, Exiting = u.Exiting,
                    BodyState = u.NetBodyState, CamoHidden = u.Alive && u.CamoHidden, FeignHard = u.FeignHard, FeignDir = u.FeignDir, FeignRegion = u.FeignRegion,
                };
                c.SetActor(p, a);
            }

            HostVehicles(now, c);
            StatActorsPublished = HostUnits.Count;
            c.SetMission((int)HaloCampaign.Level, HaloCampaign.BspIndex, HaloCinematic.Playing, HaloCinematic.Letterboxed, HaloCinematic.CameraPoint, _checkpoints);
            List<HaloDevice> devices = HaloDevices.All;
            for (int i = 0; i < devices.Count; i++)
            {
                HaloDevice d = devices[i];
                int wire = d != null ? DeviceWire(d.PlacementIndex) : -1;
                if (wire >= 0) c.SetDevice(wire, d.Position);
            }
        }

        private static int FreePlace()
        {
            for (int i = 0; i < Places.Length; i++) if (PlaceFree(i)) return i;
            return -1;
        }

        private static void FreeUnit(int place)
        {
            HaloUnit u = Places[place];
            Places[place] = null;
            if (u == null) return;
            int serial;
            if (UnitSerial.TryGetValue(u, out serial)) { UnitSerial.Remove(u); SerialUnit.Remove(serial); }
            HostUnits.Remove(u);
        }


        // ------------------------------------------------------------------ device wire index

        private static HaloMissionData _wireData;
        private static int[] _wireOfPlacement = new int[0];
        private static readonly int[] PlacementOfWire = new int[HaloNetCoop.Devices];

        /// <summary>The devices' index on the wire (device bytes, press table): the ordinal of the placement among the mission's machines, controls and light
        /// fixtures (the same on every client; scenario placement indices count every object and pass 255 on most missions).</summary>
        private static void DeviceMap()
        {
            HaloMissionData data = HaloCampaign.Data;
            if (data == _wireData) return;
            _wireData = data;
            for (int i = 0; i < PlacementOfWire.Length; i++) PlacementOfWire[i] = -1;
            int n = data != null ? data.Objects.Count : 0;
            _wireOfPlacement = new int[n];
            int w = 0;
            for (int i = 0; i < n; i++)
            {
                HaloMissionData.Kind k = data.Objects[i].Kind;
                bool device = k == HaloMissionData.Kind.Machine || k == HaloMissionData.Kind.Control || k == HaloMissionData.Kind.LightFixture;
                _wireOfPlacement[i] = device && w < HaloNetCoop.Devices ? w : -1;
                if (device && w < HaloNetCoop.Devices) PlacementOfWire[w] = i;
                if (device) w++;
            }

            if (w > HaloNetCoop.Devices) HaloLog.Warn("Net: " + w + " devices in this mission - only the first " + HaloNetCoop.Devices + " are shared in co-op");
        }

        public static int DeviceWire(int placement) { DeviceMap(); return placement >= 0 && placement < _wireOfPlacement.Length ? _wireOfPlacement[placement] : -1; }
        public static int DevicePlacement(int wire) { DeviceMap(); return wire >= 0 && wire < HaloNetCoop.Devices ? PlacementOfWire[wire] : -1; }
        // ------------------------------------------------------------------ device controls pressed by a follower

        /// <summary>Lab / log counters: control presses sent to the host (follower), applied for another player (host).</summary>
        public static int StatControlsSent, StatControlsApplied;

        /// <summary>
        /// The Mission module's control use (HaloDevices.Use) asks first: on a co-op FOLLOWER the press goes to the host (true = sent, do not act
        /// locally); the host runs the same Use as that player's press and the device's new position comes back in the device bytes.
        /// </summary>
        public static bool NoteControlUse(HaloDevice c)
        {
            if (c == null || c.PlacementIndex < 0 || CampaignHost || !CoopLive) return false;
            Net.NoteControl(c.PlacementIndex);
            StatControlsSent++;
            HaloLog.Info("Net: control '" + c.Label + "' (placement " + c.PlacementIndex + ") pressed here - sent to the co-op host");
            return true;
        }

        /// <summary>Presses of controls heard as a follower (each makes K's device play its on / off / deny sound once).</summary>
        public static int StatPressesHeard;
        private static readonly int[] SeenPress = new int[HaloNetCoop.Devices];
        private static bool _pressHosting;

        /// <summary>Host, every step: K's PressCount changes of the controls go into the press table (the first step as host takes the counts as they are).</summary>
        private static void HostPresses()
        {
            var all = HaloDevices.All;
            for (int i = 0; i < all.Count; i++)
            {
                HaloDevice d = all[i];
                if (d == null || d.Def == null || !d.Def.IsControl) continue;
                int p = DeviceWire(d.PlacementIndex);
                if (p < 0) continue;
                if (_pressHosting && d.PressCount != SeenPress[p]) Net.Coop.NotePress(p, d.PressCount - SeenPress[p], d.LastPressDenied);
                SeenPress[p] = d.PressCount;
            }

            _pressHosting = true;
            Net.Coop.NewPresses.Clear();
        }

        /// <summary>Follower: the host's presses, onto K's devices (their press counter plays the sound).</summary>
        private static void FollowPresses()
        {
            _pressHosting = false;
            var list = Net.Coop.NewPresses;
            for (int i = 0; i < list.Count; i++)
            {
                int e = list[i];
                HaloDevice d = HaloDevices.ByPlacement(DevicePlacement(e & 0xFF));
                if (d == null) continue;
                d.PressCount += (e >> 8) & 7;
                d.LastPressDenied = (e & (1 << 11)) != 0;
                SeenPress[e & 0xFF] = d.PressCount;
                StatPressesHeard++;
                if (StatPressesHeard <= 3) HaloLog.Info("Net: the host pressed control '" + d.Label + "'" + (d.LastPressDenied ? " (denied)" : ""));
            }

            list.Clear();
        }
        /// <summary>Host: a follower pressed a control.</summary>
        private static void ApplyControlUse(in HaloNetEvent ev)
        {
            if (!Net.Coop.Writing) return;
            HaloDevice d = HaloDevices.ByPlacement(ev.Value);
            if (d == null) { HaloLog.Warn("Net: player " + ev.Slot + " pressed a control at placement " + ev.Value + " that is not here"); return; }
            StatControlsApplied++;
            try { HaloDevices.Use(d, "co-op player " + ev.Slot); }
            catch (Exception e) { HaloLog.Warn("Net: a co-op control press failed: " + e.Message); }
        }

        /// <summary>Host: a player's hit on one of my AI units (their puppet on their screen).</summary>
        private static void ApplyActorHit(in HaloNetEvent ev)
        {
            HaloUnit u;
            if (!SerialUnit.TryGetValue(ev.Value, out u) || u == null || !u.Alive) return;
            HaloNetPlayer shooter = Find(ev.Slot);
            Vector3 from = shooter != null ? shooter.Head : HaloNetU.V(ev.Pos);
            Vector3 to = u.Eye - from;
            var hit = new HaloHit
            {
                Amount = ev.Amount, Cause = HaloNetU.TagOf(ev.Tag), SourcePlayer = ev.Slot, Point = u.transform.position + Vector3.up * 1.2f,
                Direction = to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.forward, Melee = ev.Melee, Headshot = ev.Head, Explosion = ev.Explosion, BackHit = ev.Back, Instant = ev.Instant,
            };
            StatActorHitsApplied++;
            try { u.TakeHit(hit); }
            catch (Exception e) { HaloLog.Warn("Net: applying a co-op hit to " + u.name + " failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ a client

        private static void ClientStep()
        {
            HaloNetCoop c = Net.Coop;
            if (!c.HaveActors) return;
            if (_puppetRoot == null)
            {
                var go = new GameObject("Halo Net Puppets");
                Transform parent = HaloSession.WorldRoot != null ? HaloSession.WorldRoot : (_game != null ? _game.transform : null);
                if (parent != null) go.transform.SetParent(parent, false);
                _puppetRoot = go.transform;
                for (int i = 0; i < DeviceShown.Length; i++) DeviceShown[i] = -1f;      // (a fresh mission: every device position is applied once)
            }

            int alive = 0;
            for (int p = 0; p < HaloNetCoop.MaxActors; p++)
            {
                HaloNetActor a = c.Actor(p);
                HaloNetPuppet pup = Puppets[p];
                if (a.Serial == 0)
                {
                    if (pup != null) { pup.Retire(); Puppets[p] = null; }
                    if (VDrawn[p] != null) { VDrawn[p] = null; VHave[p] = false; }
                    continue;
                }

                if (a.IsVehicle)
                {
                    if (pup != null) { pup.Retire(); Puppets[p] = null; }
                    FollowVehicle(p, in a);
                    continue;
                }

                if (VDrawn[p] != null) { VDrawn[p] = null; VHave[p] = false; }
                if (pup != null && pup.Serial != a.Serial) { pup.Retire(); pup = null; Puppets[p] = null; }
                if (pup == null)
                {
                    pup = HaloNetPuppet.Create(a, _puppetRoot);
                    Puppets[p] = pup;
                    StatPuppets++;
                }

                pup.Take(in a);
                if (a.Alive) alive++;
            }

            // the mission: BSP, cutscene (camera, letterbox), devices, the scripts' objects (no lambdas: nothing allocates per frame)
            if (c.HaveMission)
            {
                try
                {
                    if (c.Bsp >= 0 && c.Bsp != HaloCampaign.BspIndex) HaloCampaign.SwitchBsp(c.Bsp);
                    if (c.Cutscene != HaloCinematic.Playing) { if (c.Cutscene) HaloCinematic.Begin(); else HaloCinematic.End(); }
                    if (c.Cutscene && c.CameraPoint >= 0 && c.CameraPoint != HaloCinematic.CameraPoint) HaloCinematic.CameraSet(c.CameraPoint, 0);
                    if (c.Letterbox != HaloCinematic.Letterboxed) HaloCinematic.Letterbox(c.Letterbox);
                    for (int i = 0; i < HaloNetCoop.Devices; i++)
                    {
                        float want = c.Device(i);
                        if (Mathf.Abs(want - DeviceShown[i]) < 0.003f) continue;
                        DeviceShown[i] = want;
                        int placement = DevicePlacement(i);
                        if (placement >= 0 && HaloDevices.Position(placement) >= 0f) HaloDevices.SetPosition(placement, want, false);
                    }
                }
                catch (Exception e)
                {
                    _coopErrors++;
                    if (_coopErrors <= 3) HaloLog.Warn("Net: following the host's mission failed: " + e.Message);
                }

                HaloScenario sc = HaloSession.Scenario;
                for (int i = 0; i < c.NewEvents.Count; i++)
                {
                    HaloNetMissionEvent e = c.NewEvents[i];
                    if (e.Kind == HaloNetMissionEvent.Voice) { PlayVoice(e.Value); continue; }
                    string name = sc != null && e.Value >= 0 && e.Value < sc.ObjectNames.Count ? sc.ObjectNames[e.Value] : null;
                    StatMissionEvents++;
                    if (name == null) continue;
                    try
                    {
                        if (e.Kind == HaloNetMissionEvent.Create) HaloObjects.Create(name);
                        else if (e.Kind == HaloNetMissionEvent.Destroy) HaloObjects.Destroy(name);
                    }
                    catch (Exception ex) { HaloLog.Warn("Net: the host's object event for " + name + " failed: " + ex.Message); }
                }

                c.NewEvents.Clear();
            }
        }

        /// <summary>A client's hit on a puppet (HaloNetPuppet.TakeHit): sent to the host, nothing applied here.</summary>
        internal static void PuppetHit(HaloNetPuppet pup, HaloHit hit)
        {
            if (!Up || pup == null || !pup.Alive || !(hit.Amount > 0f)) return;
            if (hit.SourcePlayer >= 0 && hit.SourcePlayer != HaloLocal.NetId) return;
            Net.NoteHitActor(pup.Serial, hit.Amount, HaloNetU.TagIndex(hit.Cause), hit.Melee, hit.Headshot, hit.Explosion, hit.BackHit, hit.Instant);
            StatActorHitsSent++;
        }

        /// <summary>The Characters module's puppet units forward their TakeHit here (when HaloUnit is in puppet mode).</summary>
        public static void PuppetHit(HaloUnit unit, HaloHit hit)
        {
            if (unit == null) return;
            HaloNetPuppet pup = unit.GetComponent<HaloNetPuppet>();
            if (pup != null) PuppetHit(pup, hit);
        }
    }
}

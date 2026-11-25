using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Mission module, K] Halo's devices: machines (doors, platforms, lifts, gears) and controls (switches, buttons, call buttons) from their
    /// device_machine / device_control tags and the scenario's device groups. Every device has a power (0..1) and a position (0..1) that
    /// moves to the desired position at the tag's transition time; the "device position" animation of its animation graph shows the position
    /// (overlay frame = position x (frames - 1)), the collision moves with the model's nodes. Device groups share one value among their
    /// members (power group -> power, position group -> desired position), so a switch moves its doors. Doors (machine type door) open by
    /// themselves when a unit comes near - unless "does not operate automatically" - and close after the door open time; depowered devices
    /// do not move. Controls are used by the player: VR = touch or trigger with a hand at the control, monitor = E while looking at it.
    /// Standing players ride moving machines (lifts) the way the Euro Truck Simulator's riders do (the body root is carried by the
    /// platform's motion each frame, heading only). Scripts reach all of it by object / group name (see the static members).
    /// Player.log: "Campaign: devices ..." lines.
    /// </summary>
    public static class HaloDevices
    {
        public static readonly List<HaloDevice> All = new List<HaloDevice>();
        static HaloMissionData _data;
        static float[] _groups = new float[0];
        static bool[] _groupChangedOnce = new bool[0];
        static readonly HashSet<string> Said = new HashSet<string>();
        static readonly Dictionary<HaloTag, HaloDeviceDef> Defs = new Dictionary<HaloTag, HaloDeviceDef>();
        static readonly Dictionary<HaloTag, HaloAnimGraph> Graphs = new Dictionary<HaloTag, HaloAnimGraph>();
        static readonly List<HaloUnit> UnitsScratch = new List<HaloUnit>();

        /// <summary>Reach of a hand to a control (metres, ours) and of the monitor player's E (metres from the eye, ours).</summary>
        public static float HandReach = 0.18f, DeskReach = 2.2f;
        /// <summary>The lab: a control use as if the player pressed it (set, then one frame).</summary>
        public static HaloDevice LabUse;

        public static int Count { get { return All.Count; } }
        public static int Uses { get; private set; }

        public static void Begin(HaloMissionData data)
        {
            Clear();
            _data = data;
            int n = data != null ? data.DeviceGroups.Count : 0;
            _groups = new float[n];
            _groupChangedOnce = new bool[n];
            for (int i = 0; i < n; i++) _groups[i] = data.DeviceGroups[i].Initial;
            if (n > 0)
            {
                string list = "";
                for (int i = 0; i < n && i < 24; i++) list += (i > 0 ? ", " : "") + data.DeviceGroups[i].Name + "=" + data.DeviceGroups[i].Initial.ToString("0.##");
                HaloLog.Info("Campaign: devices: " + n + " device groups: " + list);
            }
        }

        public static void Clear()
        {
            All.Clear();
            _data = null;
            _groups = new float[0];
            _groupChangedOnce = new bool[0];
            Said.Clear();
            Defs.Clear();
            Graphs.Clear();
            Uses = 0;
            LabUse = null;
            _carry = null;
        }

        static void Once(string key, string line) { if (Said.Add(key)) HaloLog.Info(line); }

        internal static HaloDeviceDef DefOf(HaloTag tag)
        {
            HaloDeviceDef d;
            if (Defs.TryGetValue(tag, out d)) return d;
            try { d = new HaloDeviceDef(tag); }
            catch (Exception e) { HaloLog.Warn("Campaign: device " + tag + " unreadable: " + e.Message); d = null; }
            Defs[tag] = d;
            return d;
        }

        static HaloAnimGraph GraphOf(HaloTag tag)
        {
            if (tag == null) return null;
            HaloAnimGraph g;
            if (Graphs.TryGetValue(tag, out g)) return g;
            try { g = new HaloAnimGraph(tag); }
            catch (Exception e) { HaloLog.Warn("Campaign: device animations " + tag + " unreadable: " + e.Message); g = null; }
            Graphs[tag] = g;
            return g;
        }

        /// <summary>A device object K built (HaloObjects): its state from the placement and the groups.</summary>
        internal static HaloDevice Add(GameObject go, HaloMissionData.Obj o, HaloModelBuilder.Built built)
        {
            var def = DefOf(o.Type);
            if (def == null) return null;
            // moving colliders: a kinematic body on the root (World module's advice) so the physics engine treats the nodes' colliders as moving
            if (def.IsMachine && go.GetComponent<Rigidbody>() == null) { var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; }
            var d = go.AddComponent<HaloDevice>();
            d.Init(o, def, built, GraphOf(def.AnimationGraph));
            // initial power / position: the groups when the device has them, else the placement's flags
            d.Power = d.PowerTarget = o.PowerGroup >= 0 && o.PowerGroup < _groups.Length ? _groups[o.PowerGroup] : ((o.DeviceFlags & 2) != 0 ? 0f : 1f);
            float pos = o.PositionGroup >= 0 && o.PositionGroup < _groups.Length ? _groups[o.PositionGroup] : ((o.DeviceFlags & 1) != 0 ? 1f : 0f);
            d.Position = d.Desired = d.Shown = d.PreviousPosition = pos;
            d.Pose(pos);
            d.InitLights();
            All.Add(d);
            if (def.IsDoor) Once("door " + o.Type.Name, "Campaign: devices: " + def + (d.HasAnimation ? "" : " - no 'device position' animation"));
            else if (def.IsControl || def.IsElevator) Once("dev " + o.Type.Name, "Campaign: devices: " + def + (d.HasAnimation ? "" : " - no 'device position' animation"));
            return d;
        }

        internal static void Remove(GameObject go)
        {
            if (go == null) return;
            var d = go.GetComponent<HaloDevice>();
            if (d != null) All.Remove(d);
        }

        // ------------------------------------------------------------------ script API (by object / group name)

        public static HaloDevice Find(string name)
        {
            var g = HaloObjects.Find(name);
            return g != null ? g.GetComponent<HaloDevice>() : null;
        }

        static HaloDevice Need(string name, string fn)
        {
            var d = Find(name);
            if (d == null) Once(fn + name, "Campaign: " + fn + " '" + name + "' - no such device (not created?)");
            return d;
        }

        /// <summary>device_get_position.</summary>
        public static float Position(string name) { var d = Find(name); return d != null ? d.Position : 0f; }
        /// <summary>device_set_position: the desired position (the device's position group when it has one: every member follows).</summary>
        public static bool SetPosition(string name, float value)
        {
            var d = Need(name, "device_set_position");
            if (d == null) return false;
            value = Mathf.Clamp01(value);
            if (d.Placement.PositionGroup >= 0 && d.Placement.PositionGroup < _groups.Length) GroupSet(d.Placement.PositionGroup, value, false);
            else d.Desired = value;
            return true;
        }
        /// <summary>device_set_position_immediate.</summary>
        public static void SetPositionImmediate(string name, float value)
        {
            var d = Need(name, "device_set_position_immediate");
            if (d == null) return;
            value = Mathf.Clamp01(value);
            if (d.Placement.PositionGroup >= 0 && d.Placement.PositionGroup < _groups.Length) GroupSet(d.Placement.PositionGroup, value, true);
            else d.Jump(value);
        }
        /// <summary>device_get_power.</summary>
        public static float Power(string name) { var d = Find(name); return d != null ? d.Power : 0f; }
        /// <summary>device_set_power (the power group when it has one).</summary>
        public static void SetPower(string name, float value)
        {
            var d = Need(name, "device_set_power");
            if (d == null) return;
            value = Mathf.Clamp01(value);
            if (d.Placement.PowerGroup >= 0 && d.Placement.PowerGroup < _groups.Length) { _groups[d.Placement.PowerGroup] = value; ApplyGroup(d.Placement.PowerGroup, false); }
            else d.PowerTarget = value;
        }
        /// <summary>device_get_position of a device's desired position (device_get_desired_position... not in Halo 1; kept for the lab).</summary>
        public static float DesiredPosition(string name) { var d = Find(name); return d != null ? d.Desired : 0f; }

        public static int GroupIndex(string group) { return _data != null ? _data.DeviceGroupIndex(group) : -1; }
        /// <summary>device_group_get.</summary>
        public static float GroupGet(string group) { int i = GroupIndex(group); return i >= 0 && i < _groups.Length ? _groups[i] : 0f; }
        /// <summary>device_group_set.</summary>
        public static void GroupSet(string group, float value)
        {
            int i = GroupIndex(group);
            if (i < 0) { Once("group " + group, "Campaign: device_group_set '" + group + "' - no such device group"); return; }
            GroupSet(i, Mathf.Clamp01(value), false);
        }
        /// <summary>device_group_set_immediate.</summary>
        public static void GroupSetImmediate(string group, float value)
        {
            int i = GroupIndex(group);
            if (i < 0) { Once("group " + group, "Campaign: device_group_set_immediate '" + group + "' - no such device group"); return; }
            GroupSet(i, Mathf.Clamp01(value), true);
        }
        /// <summary>device_group_change_only_once_more_set: the group may change once more (true) / is locked after its next change.</summary>
        public static void GroupChangeOnlyOnceMoreSet(string group, bool on)
        {
            int i = GroupIndex(group);
            if (i >= 0 && i < _groupChangedOnce.Length) _groupChangedOnce[i] = !on;
        }
        /// <summary>device_one_sided_set.</summary>
        public static void SetOneSided(string name, bool on) { var d = Need(name, "device_one_sided_set"); if (d != null) d.OneSided = on; }
        /// <summary>device_operates_automatically_set.</summary>
        public static void SetOperatesAutomatically(string name, bool on) { var d = Need(name, "device_operates_automatically_set"); if (d != null) d.Automatic = on; }
        /// <summary>device_set_never_appears_locked.</summary>
        public static void SetNeverAppearsLocked(string name, bool on) { var d = Need(name, "device_set_never_appears_locked"); if (d != null) d.NeverAppearsLocked = on; }
        /// <summary>device_closes_automatically_set.</summary>
        public static void SetClosesAutomatically(string name, bool on) { var d = Need(name, "device_closes_automatically_set"); if (d != null) d.ClosesAutomatically = on; }

        static void GroupSet(int i, float value, bool immediate)
        {
            if (i < 0 || i >= _groups.Length) return;
            var g = _data.DeviceGroups[i];
            if (_groupChangedOnce[i] && (g.OnlyOnce)) return;
            if (Mathf.Abs(_groups[i] - value) > 1e-4f && g.OnlyOnce) _groupChangedOnce[i] = true;
            _groups[i] = value;
            ApplyGroup(i, immediate);
        }

        static void ApplyGroup(int i, bool immediate)
        {
            float v = _groups[i];
            for (int k = 0; k < All.Count; k++)
            {
                var d = All[k];
                if (d == null) continue;
                if (d.Placement.PositionGroup == i) { if (immediate) d.Jump(v); else d.Desired = v; }
                if (d.Placement.PowerGroup == i) { d.PowerTarget = v; if (immediate) d.Power = v; }
            }
        }

        // ------------------------------------------------------------------ the game tick (30 Hz)

        internal static void Step(float dt)
        {
            if (All.Count == 0) return;
            UnitsNear();
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i];
                if (d == null || !d.isActiveAndEnabled) continue;
                d.Step(dt, UnitsScratch);
            }
        }

        /// <summary>Who opens doors: the player and every living unit the Characters module has (positions read each tick).</summary>
        static void UnitsNear()
        {
            UnitsScratch.Clear();
            var root = HaloSession.WorldRoot;
            if (root == null) return;
            // units register under the scenario names (HaloObjects) or live under the world root: collect the cheap way, a few per tick
            _unitScan -= 1;
            if (_unitScan > 0 && _units.Count > 0) { for (int i = 0; i < _units.Count; i++) if (_units[i] != null && _units[i].Alive) UnitsScratch.Add(_units[i]); return; }
            _unitScan = 15;      // twice a second
            _units.Clear();
            root.GetComponentsInChildren(false, _units);
            for (int i = 0; i < _units.Count; i++) if (_units[i] != null && _units[i].Alive) UnitsScratch.Add(_units[i]);
        }
        static int _unitScan;
        static readonly List<HaloUnit> _units = new List<HaloUnit>();

        /// <summary>Somebody (player or unit) stands within <paramref name="radius"/> of <paramref name="at"/> (metres); one sided: in front of <paramref name="forward"/>.</summary>
        internal static bool SomeoneNear(Vector3 at, float radius, bool oneSided, Vector3 forward, List<HaloUnit> units)
        {
            float r2 = radius * radius;
            if (HaloLocal.Alive)
            {
                Vector3 p;
                if (HaloPlayer.TryFeet(out p))
                {
                    Vector3 c = p + Vector3.up * 0.9f;
                    if ((c - at).sqrMagnitude <= r2 && (!oneSided || Vector3.Dot(c - at, forward) >= 0f)) return true;
                }
            }
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null) continue;
                Vector3 c = u.transform.position + Vector3.up * 0.9f;
                if ((c - at).sqrMagnitude <= r2 && (!oneSided || Vector3.Dot(c - at, forward) >= 0f)) return true;
            }
            // co-op: the other players of the mission
            try
            {
                var list = HaloNetGame.Players;
                if (list != null)
                    for (int i = 0; i < list.Count; i++)
                    {
                        var p = list[i];
                        if (p == null || !p.InMatch || !p.Alive || p.Slot == HaloLocal.NetId) continue;
                        Vector3 c = p.Position + Vector3.up * 0.9f;
                        if ((c - at).sqrMagnitude <= r2 && (!oneSided || Vector3.Dot(c - at, forward) >= 0f)) return true;
                    }
            }
            catch (Exception) { }
            return false;
        }

        // ------------------------------------------------------------------ co-op (Net): devices keyed by their scenario placement index

        /// <summary>The device built for a scenario placement (HaloMissionData.Objects index), null when none.</summary>
        public static HaloDevice ByPlacement(int placementIndex)
        {
            for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].PlacementIndex == placementIndex) return All[i];
            return null;
        }

        /// <summary>Position of the device at a placement index (-1 when it has no live device): the host publishes it.</summary>
        public static float Position(int placementIndex) { var d = ByPlacement(placementIndex); return d != null ? d.Position : -1f; }

        /// <summary>A co-op client follows the host: the device at a placement index moves to <paramref name="position"/> (immediately when
        /// <paramref name="immediate"/>, else at its own speed).</summary>
        public static void SetPosition(int placementIndex, float position, bool immediate = false)
        {
            var d = ByPlacement(placementIndex);
            if (d == null) return;
            position = Mathf.Clamp01(position);
            if (immediate) d.Jump(position); else d.Desired = position;
        }

        /// <summary>The player used a control (or the lab did): Halo's control types.</summary>
        internal static void Use(HaloDevice c, string how)
        {
            if (c == null || !c.Def.IsControl) return;
            if (HaloNetGame.NoteControlUse(c)) return;      // N: a co-op follower's press goes to the host (which runs this Use; the device bytes come back)
            var o = c.Placement;
            bool usable = c.Power > 0.001f && (o.DeviceFlags & 16) == 0 && !(c.ChangeOnlyOnce && c.Changed);
            if (o.PositionGroup >= 0 && o.PositionGroup < _groups.Length && _groupChangedOnce[o.PositionGroup] && _data.DeviceGroups[o.PositionGroup].OnlyOnce) usable = false;
            if (!usable)
            {
                c.PressCount++; c.LastPressDenied = true;      // the deny sound plays from the press counter (every client)
                HaloLog.Info("Campaign: control '" + c.Label + "' denied (" + how + ", power " + c.Power.ToString("0.##") + ")");
                return;
            }
            float target;
            switch (c.Def.ControlType)
            {
                case 1: target = 1f; break;                                   // on button
                case 2: target = 0f; break;                                   // off button
                case 3: target = Mathf.Clamp01(c.Def.CallValue); break;       // call button
                default: target = c.Desired >= 0.5f ? 0f : 1f; break;          // toggle switch
            }
            c.Changed = true;
            c.PressCount++; c.LastPressDenied = false;
            Uses++;
            if (o.PositionGroup >= 0 && o.PositionGroup < _groups.Length) GroupSet(o.PositionGroup, target, false);
            else c.Desired = target;
            HaloLog.Info("Campaign: control '" + c.Label + "' used (" + how + ") -> " + target.ToString("0.##") + (o.PositionGroup >= 0 && _data != null && o.PositionGroup < _data.DeviceGroups.Count ? " (group '" + _data.DeviceGroups[o.PositionGroup].Name + "')" : ""));
        }

        // ------------------------------------------------------------------ every frame (HaloCampaignDriver, early)

        static Transform _carry;                   // the moving collider the player stands on
        static Vector3 _carryPos;
        static Quaternion _carryRot;
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        /// <summary>Visuals between ticks, the ride on moving machines, the player's control use.</summary>
        internal static void Frame()
        {
            if (All.Count == 0) return;
            // 1. what the player stands on, before this frame's device poses
            Transform standing = null;
            Vector3 feet;
            bool haveFeet = HaloPlayer.TryFeet(out feet) && HaloLocal.Alive && HaloLocal.Vehicle == null;
            if (haveFeet) standing = GroundDevice(feet);
            if (standing != _carry) { _carry = standing; if (_carry != null) { _carryPos = _carry.position; _carryRot = _carry.rotation; } }
            else if (_carry != null && (_carryPos != _carry.position || _carryRot != _carry.rotation)) { _carryPos = _carry.position; _carryRot = _carry.rotation; }

            // 2. this frame's poses (interpolated between the last two game ticks)
            float u = HaloCampaign.TickFraction;
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i];
                if (d == null || !d.isActiveAndEnabled) continue;
                d.Frame(u);
            }

            // 3. ride: the player moves with the platform (heading only in VR; never pitched / rolled)
            if (_carry != null && haveFeet)
            {
                Vector3 np = _carry.position; Quaternion nr = _carry.rotation;
                if (np != _carryPos || nr != _carryRot)
                {
                    Quaternion delta = nr * Quaternion.Inverse(_carryRot);
                    Vector3 moved = delta * (feet - _carryPos) + np - feet;
                    float yaw = Mathf.DeltaAngle(0f, delta.eulerAngles.y);
                    if (moved.sqrMagnitude < 25f) Carry(moved, HaloPlayer.XrPresent ? yaw : 0f);
                    _carryPos = np; _carryRot = nr;
                }
            }

            // 4. controls
            UseInput();
        }

        /// <summary>A collider of a device under the feet (a ray down from knee height): its transform, else null.</summary>
        static Transform GroundDevice(Vector3 feet)
        {
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.5f, Vector3.down, Hits, 1.0f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore);
            float best = 1e9f; Transform t = null;
            for (int i = 0; i < n; i++)
            {
                var h = Hits[i];
                if (h.collider == null || h.distance >= best) continue;
                best = h.distance;
                t = h.collider.GetComponentInParent<HaloDevice>() != null ? h.collider.transform : null;
            }
            return t;
        }

        static string _carryHow = "";

        /// <summary>Moves the player's body root (and the editor's character controller) by a delta, as the client's moving platforms do.</summary>
        static void Carry(Vector3 delta, float yawDegrees)
        {
            Transform root = HaloPlayer.Root;
            if (root != null)
            {
                Quaternion r = Mathf.Abs(yawDegrees) > 1e-4f ? Quaternion.Euler(0f, yawDegrees, 0f) * root.rotation : root.rotation;
                root.SetPositionAndRotation(root.position + delta, r);
                Physics.SyncTransforms();
                if (_carryHow != "root") { _carryHow = "root"; HaloLog.Info("Campaign: riding a moving machine (the body root is carried)"); }
                return;
            }
            var cc = UnityEngine.Object.FindFirstObjectByType<CharacterController>();
            if (cc != null && Application.isEditor)
            {
                bool was = cc.enabled;
                cc.enabled = false;
                cc.transform.position += delta;
                if (Mathf.Abs(yawDegrees) > 1e-4f) cc.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f) * cc.transform.rotation;
                cc.enabled = was;
                Physics.SyncTransforms();
                if (_carryHow != "cc") { _carryHow = "cc"; HaloLog.Info("Campaign: riding a moving machine (editor character controller carried)"); }
            }
        }

        static readonly bool[] _handInside = new bool[2];

        static void UseInput()
        {
            if (LabUse != null) { var l = LabUse; LabUse = null; Use(l, "lab"); return; }
            if (!HaloLocal.Alive || HaloLocal.Vehicle != null || HaloCombat.TabletOpen) return;
            if (HaloPlayer.XrPresent)
            {
                for (int h = 0; h < 2; h++)
                {
                    Transform hand = h == 1 ? HaloPlayer.RightHand : HaloPlayer.LeftHand;
                    if (hand == null) { _handInside[h] = false; continue; }
                    HaloDevice best = null; float bd = HandReach;
                    for (int i = 0; i < All.Count; i++)
                    {
                        var d = All[i];
                        if (d == null || !d.Def.IsControl || !d.isActiveAndEnabled) continue;
                        float dist = d.DistanceTo(hand.position);
                        if (dist < bd) { bd = dist; best = d; }
                    }
                    bool inside = best != null && bd < 0.04f;
                    HaloHand st = HaloXr.Hand(h == 1);
                    if (best != null && st.TriggerPressed) Use(best, h == 1 ? "right trigger" : "left trigger");
                    else if (inside && !_handInside[h]) Use(best, h == 1 ? "right hand touch" : "left hand touch");
                    _handInside[h] = inside;
                }
                return;
            }
            bool e;
            try { e = Input.GetKeyDown(KeyCode.E); } catch (Exception) { e = false; }
            if (!e) return;
            if (!string.IsNullOrEmpty(HaloLocal.PickupPrompt) || HaloLocal.VehicleInReach != null) return;      // E takes the weapon / the seat first
            Ray ray;
            try { ray = HaloDesk.AimRay(); } catch (Exception) { return; }
            HaloDevice pick = null; float pd = DeskReach;
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i];
                if (d == null || !d.Def.IsControl || !d.isActiveAndEnabled) continue;
                Vector3 c = d.Center;
                Vector3 to = c - ray.origin;
                float along = Vector3.Dot(to, ray.direction);
                if (along < 0f || along > pd) continue;
                float off = (to - ray.direction * along).magnitude;
                if (off > 0.45f + along * 0.12f) continue;
                if (along < pd) { pd = along; pick = d; }
            }
            if (pick != null) Use(pick, "E");
        }

        // ------------------------------------------------------------------ checkpoints

        internal sealed class Snapshot
        {
            public float[] Groups; public bool[] GroupOnce;
            public readonly Dictionary<int, HaloDevice.State> Devices = new Dictionary<int, HaloDevice.State>();
        }

        /// <summary>Host hand-over (Net): groups and every device's state as bytes.</summary>
        internal static void Write(System.IO.BinaryWriter w)
        {
            w.Write(_groups.Length);
            for (int i = 0; i < _groups.Length; i++) { w.Write(_groups[i]); w.Write(_groupChangedOnce[i]); }
            int n = 0;
            for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].PlacementIndex >= 0) n++;
            w.Write(n);
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i];
                if (d == null || d.PlacementIndex < 0) continue;
                var s = d.Save();
                w.Write(d.PlacementIndex); w.Write(s.Power); w.Write(s.PowerTarget); w.Write(s.Position); w.Write(s.Desired);
                w.Write((byte)((s.OneSided ? 1 : 0) | (s.Automatic ? 2 : 0) | (s.NeverLocked ? 4 : 0) | (s.Changed ? 8 : 0) | (s.Closes ? 16 : 0)));
            }
        }

        /// <summary>The new host takes the old host's device logic state over (positions stay where they are drawn).</summary>
        internal static void Read(System.IO.BinaryReader r)
        {
            int g = r.ReadInt32();
            for (int i = 0; i < g; i++)
            {
                float v = r.ReadSingle(); bool once = r.ReadBoolean();
                if (i < _groups.Length) { _groups[i] = v; _groupChangedOnce[i] = once; }
            }
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                int pi = r.ReadInt32();
                var s = new HaloDevice.State { Power = r.ReadSingle(), PowerTarget = r.ReadSingle(), Position = r.ReadSingle(), Desired = r.ReadSingle() };
                byte f = r.ReadByte();
                s.OneSided = (f & 1) != 0; s.Automatic = (f & 2) != 0; s.NeverLocked = (f & 4) != 0; s.Changed = (f & 8) != 0; s.Closes = (f & 16) != 0;
                var d = ByPlacement(pi);
                if (d == null) continue;
                s.Position = d.Position;      // drawn where the follower already shows it
                d.Load(s);
            }
        }

        internal static Snapshot Capture()
        {
            var s = new Snapshot { Groups = (float[])_groups.Clone(), GroupOnce = (bool[])_groupChangedOnce.Clone() };
            for (int i = 0; i < All.Count; i++) if (All[i] != null && All[i].PlacementIndex >= 0) s.Devices[All[i].PlacementIndex] = All[i].Save();
            return s;
        }

        internal static void Restore(Snapshot s)
        {
            if (s == null) return;
            if (s.Groups != null && s.Groups.Length == _groups.Length) Array.Copy(s.Groups, _groups, _groups.Length);
            if (s.GroupOnce != null && s.GroupOnce.Length == _groupChangedOnce.Length) Array.Copy(s.GroupOnce, _groupChangedOnce, _groupChangedOnce.Length);
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i];
                if (d == null) continue;
                HaloDevice.State st;
                if (s.Devices.TryGetValue(d.PlacementIndex, out st)) d.Load(st);
            }
            _carry = null;
        }
    }

    /// <summary>[Mission module, K] One machine / control / light fixture of the mission (state + its model's "device position" pose).</summary>
    public sealed class HaloDevice : MonoBehaviour
    {
        public HaloMissionData.Obj Placement;
        public HaloDeviceDef Def;
        public int PlacementIndex = -1;
        public float Power, PowerTarget, Position, Desired;
        public bool OneSided, Automatic, NeverAppearsLocked, ChangeOnlyOnce, Changed, ClosesAutomatically = true;
        /// <summary>Position at the previous game tick and the one shown (interpolated) - visuals only.</summary>
        public float PreviousPosition, Shown = -1f;
        float _openUntil;
        int _moving;          // -1 closing, 0 still, 1 opening

        HaloModelBuilder.Built _built;
        HaloAnimation _anim;
        int[] _map;
        Vector3[] _restPos;
        Quaternion[] _restRot;
        float _radius;
        Collider[] _colliders;

        public bool HasAnimation { get { return _anim != null; } }
        public string Label { get { return Placement != null && Placement.Name.Length > 0 ? Placement.Name : name; } }

        /// <summary>The device's centre (bounding sphere) in the world.</summary>
        public Vector3 Center { get { return transform.TransformPoint(_center); } }
        Vector3 _center;

        internal void Init(HaloMissionData.Obj o, HaloDeviceDef def, HaloModelBuilder.Built built, HaloAnimGraph graph)
        {
            Placement = o; Def = def; _built = built;
            PlacementIndex = -1;
            var mo = GetComponent<HaloMissionObject>();
            OneSided = (o.SubFlags & 2) != 0 && def.IsMachine;
            Automatic = def.IsDoor && (o.SubFlags & 1) == 0;
            NeverAppearsLocked = (o.SubFlags & 4) != 0 && def.IsMachine;
            ChangeOnlyOnce = (o.DeviceFlags & 4) != 0;
            try
            {
                var od = new HaloObjectDef(o.Type);
                _radius = od.BoundingRadius * HaloSpace.Unit;
                _center = HaloSpace.Pos(od.BoundingOffset);
            }
            catch (Exception) { _radius = 1f; _center = Vector3.zero; }
            if (graph != null && built != null && built.Nodes != null)
            {
                _anim = graph.Get("device position");
                if (_anim != null && !_anim.Decode()) _anim = null;
                if (_anim != null)
                {
                    int n = _anim.NodeCount;
                    _map = new int[n];
                    for (int k = 0; k < n; k++)
                    {
                        int s = -1;
                        if (graph.NodeNames != null && k < graph.NodeNames.Length && !string.IsNullOrEmpty(graph.NodeNames[k]) && built.Model != null) s = built.Model.NodeIndex(graph.NodeNames[k]);
                        if (s < 0 && k < built.Nodes.Length) s = k;
                        _map[k] = s;
                    }
                    _restPos = new Vector3[built.Nodes.Length];
                    _restRot = new Quaternion[built.Nodes.Length];
                    for (int k = 0; k < built.Nodes.Length; k++) { _restPos[k] = built.Nodes[k].localPosition; _restRot[k] = built.Nodes[k].localRotation; }
                }
            }
            _colliders = GetComponentsInChildren<Collider>(true);
            try { MapLoops(); } catch (Exception e) { HaloLog.Warn("Campaign: device sounds of " + Label + ": " + e.Message); }
        }

        void Start()
        {
            var mo = GetComponent<HaloMissionObject>();
            if (mo != null) PlacementIndex = mo.PlacementIndex;
        }

        /// <summary>Distance from a point to the device (its colliders, else its bounding sphere), metres.</summary>
        public float DistanceTo(Vector3 p)
        {
            float best = 1e9f;
            if (_colliders != null)
                for (int i = 0; i < _colliders.Length; i++)
                {
                    var c = _colliders[i];
                    if (c == null || !c.enabled) continue;
                    Vector3 q = c.bounds.ClosestPoint(p);
                    float d = (q - p).magnitude;
                    if (d < best) best = d;
                }
            if (best > 1e8f) best = Mathf.Max(0f, (p - Center).magnitude - Mathf.Min(_radius, 0.3f));
            return best;
        }

        public void Jump(float v)
        {
            Desired = Position = PreviousPosition = Mathf.Clamp01(v);
            _moving = 0;
            Pose(Position);
            Shown = Position;
        }

        internal void Step(float dt, List<HaloUnit> units)
        {
            if (PlacementIndex < 0) { var mo = GetComponent<HaloMissionObject>(); if (mo != null) PlacementIndex = mo.PlacementIndex; }
            // power
            if (Mathf.Abs(Power - PowerTarget) > 1e-4f)
            {
                float was = Power;
                float sp = Def.PowerTransition > 1e-3f ? dt / Def.PowerTransition : 1f;
                Power = Mathf.MoveTowards(Power, PowerTarget, sp);
                if (was > 0f && Power <= 0f) PlaySound(Def.Depowered);
                else if (was <= 0f && Power > 0f) PlaySound(Def.Repowered);
            }
            // doors: open for anyone near (when powered), close after the door's open time
            if (Automatic && Def.IsDoor && Power > 0f)
            {
                float radius = Def.ActivationRadius > 0f ? Def.ActivationRadius * HaloSpace.Unit : Mathf.Max(_radius, 1.5f);
                if (HaloDevices.SomeoneNear(Center, radius, OneSided, transform.right, units))
                {
                    Desired = 1f;
                    _openUntil = Time.time + Mathf.Max(0.5f, Def.DoorOpenTime);
                }
                else if (ClosesAutomatically && Desired > 0f && Time.time > _openUntil) Desired = 0f;
            }
            // position
            PreviousPosition = Position;
            if (Mathf.Abs(Position - Desired) > 1e-4f)
            {
                if (Power <= 0f && Def.IsMachine) { if (_moving != 0) _moving = 0; Loops(); return; }      // a depowered machine stands still
                int dir = Desired > Position ? 1 : -1;
                if (dir != _moving)
                {
                    _moving = dir;
                    PlaySound(dir > 0 ? Def.Open : Def.Close);
                    if (Def.IsControl) PlaySound(dir > 0 ? Def.On : Def.Off);      // the panel's own on / off (every client: keyed on the position)
                    _startSoundThisTick = true;
                }
                float sp = Def.PositionTransition > 1e-3f ? dt / Def.PositionTransition : 1f;
                Position = Mathf.MoveTowards(Position, Desired, sp);
                if (Mathf.Abs(Position - Desired) <= 1e-4f)
                {
                    Position = Desired;
                    _moving = 0;
                    if (Position >= 0.999f) PlaySound(Def.Opened);
                    else if (Position <= 0.001f) PlaySound(Def.Closed);
                }
            }
            else _moving = 0;
            Loops();
        }

        // ---- the tag's looping sounds attached to the object, scaled by the device inputs (Object attachment "primary scale" = A..D,
        // Device +408 "a in".."d in": power, change in power, position, change in position, locked) - lifts' machinery, doors' motors
        HaloAudio.Handle[] _loopH;
        int[] _loopIn;
        float _lastPower;

        void MapLoops()
        {
            var mo = GetComponent<HaloMissionObject>();
            if (mo == null || mo.Attached == null || Placement == null || Placement.Type == null) return;
            var hs = mo.Attached.Sounds;
            var hl = new List<HaloAudio.Handle>(); var il = new List<int>();
            int k = 0;
            foreach (var at in Placement.Type.Root.Block(320, 72))
            {
                HaloTag t = at.Ref(0);
                if (t == null || (t.ClassName != "lsnd" && t.ClassName != "snd!")) continue;
                int fn = at.U16(48);      // FunctionOut: 0 none, 1..4 = A..D
                if (k < hs.Count && fn >= 1 && fn <= 4 && Def.In[fn - 1] != 0) { hl.Add(hs[k]); il.Add(Def.In[fn - 1]); }
                k++;
            }
            if (k != hs.Count) { HaloLog.Info("Campaign: devices: " + Label + " - " + hs.Count + " of " + k + " attached sounds started; their device scaling is left off"); return; }
            if (hl.Count == 0) return;
            _loopH = hl.ToArray(); _loopIn = il.ToArray();
            for (int i = 0; i < _loopH.Length; i++) { HaloAudio.SetGain(_loopH[i], 0f); HaloAudio.SetScale(_loopH[i], 0f); }
        }

        float InputValue(int input)
        {
            switch (input)
            {
                case 1: return Power;
                case 2: return Mathf.Abs(Power - _lastPower) > 1e-5f ? 1f : 0f;
                case 3: return Position;
                case 4: return Mathf.Abs(Position - PreviousPosition) > 1e-5f ? 1f : 0f;
                case 5: return Power <= 0f && !NeverAppearsLocked ? 1f : 0f;
                default: return 0f;
            }
        }

        // ---- presses (every client: Net carries PressCount / LastPressDenied to followers) and lights following power
        /// <summary>Accepted / denied presses of a control so far (the host counts in Use; Net copies it to followers).</summary>
        public int PressCount;
        public bool LastPressDenied;
        int _heardPress;
        bool _startSoundThisTick, _lightsByPower;

        void Press()
        {
            if (PressCount != _heardPress)
            {
                _heardPress = PressCount;
                if (LastPressDenied) PlaySound(Def.Deny);
                else if (!_startSoundThisTick) PlaySound(Desired >= 0.5f ? Def.On : Def.Off);   // no movement (target = current): Halo's press feedback anyway
            }
            _startSoundThisTick = false;
        }

        /// <summary>Attached lights follow power on devices whose functions read power (Device +408 "x in" = power).</summary>
        internal void InitLights()
        {
            _lightsByPower = Def.In[0] == 1 || Def.In[1] == 1 || Def.In[2] == 1 || Def.In[3] == 1;
            _heardPress = PressCount;
            _lastPower = Power;
            if (_lightsByPower && Power <= 0f) { var mo = GetComponent<HaloMissionObject>(); if (mo != null) HaloFx.SetLights(mo.Attached, 0f); }
        }

        void Loops()
        {
            Press();
            if (_lightsByPower && Mathf.Abs(Power - _lastPower) > 1e-5f && (Power <= 0f) != (_lastPower <= 0f)) { var mo = GetComponent<HaloMissionObject>(); if (mo != null) HaloFx.SetLights(mo.Attached, Power > 0f ? 1f : 0f); }
            if (_loopH != null)
                for (int i = 0; i < _loopH.Length; i++)
                {
                    float v = Mathf.Clamp01(InputValue(_loopIn[i]));
                    HaloAudio.SetScale(_loopH[i], v);
                    HaloAudio.SetGain(_loopH[i], v);
                }
            _lastPower = Power;
        }

        internal void Frame(float u)
        {
            float p = PreviousPosition + (Position - PreviousPosition) * u;
            if (Mathf.Abs(p - Shown) < 1e-5f) return;
            Pose(p);
        }

        /// <summary>The "device position" overlay at a position (frame = position x (frames - 1)), on the model's nodes.</summary>
        internal void Pose(float position)
        {
            Shown = position;
            if (_anim == null || _built == null) return;
            if ((Placement.DeviceFlags & 8) != 0) position = 1f - position;       // position reversed
            int frames = Mathf.Max(1, _anim.Frames);
            float x = Mathf.Clamp01(position) * (frames - 1);
            if (Def.PositionLoops) x = (position * frames) % frames;
            int f0 = Mathf.Clamp((int)x, 0, frames - 1);
            int f1 = Mathf.Min(frames - 1, f0 + 1);
            if (Def.PositionLoops && f0 == frames - 1) f1 = 0;
            float w = x - f0;
            if ((Def.Flags & 2) != 0) w = 0f;                                       // position not interpolated
            int n = _anim.NodeCount;
            float s = HaloSpace.Unit;
            for (int k = 0; k < n && k < _map.Length; k++)
            {
                int node = _map[k];
                if (node < 0 || node >= _built.Nodes.Length) continue;
                Transform t = _built.Nodes[node];
                if (_anim.Translates(k))
                {
                    int a = (f0 * n + k) * 3, b = (f1 * n + k) * 3;
                    float tx = _anim.Trans[a] + (_anim.Trans[b] - _anim.Trans[a]) * w;
                    float ty = _anim.Trans[a + 1] + (_anim.Trans[b + 1] - _anim.Trans[a + 1]) * w;
                    float tz = _anim.Trans[a + 2] + (_anim.Trans[b + 2] - _anim.Trans[a + 2]) * w;
                    if (_anim.Type == HaloAnimation.Kind.Overlay) t.localPosition = _restPos[node] + new Vector3(tx * s, tz * s, ty * s);
                    else t.localPosition = new Vector3(tx * s, tz * s, ty * s);
                }
                if (_anim.Rotates(k))
                {
                    int a = (f0 * n + k) * 4, b = (f1 * n + k) * 4;
                    Quaternion q0 = HaloSpace.Rot(_anim.Rot[a], _anim.Rot[a + 1], _anim.Rot[a + 2], _anim.Rot[a + 3]);
                    Quaternion q1 = HaloSpace.Rot(_anim.Rot[b], _anim.Rot[b + 1], _anim.Rot[b + 2], _anim.Rot[b + 3]);
                    Quaternion q = Quaternion.Slerp(q0, q1, w);
                    t.localRotation = _anim.Type == HaloAnimation.Kind.Overlay ? _restRot[node] * q : q;
                }
            }
        }

        internal void PlaySound(HaloTag t)
        {
            if (t == null) return;
            try
            {
                if (t.Is("snd!")) HaloAudio.Play(t, Center);
                else if (t.Is("effe")) HaloFx.Play(t, Center, transform.rotation);
            }
            catch (Exception e) { HaloLog.Warn("Campaign: device sound " + t + ": " + e.Message); }
        }

        public struct State { public float Power, PowerTarget, Position, Desired; public bool OneSided, Automatic, NeverLocked, Changed, Closes; }

        internal State Save() { return new State { Power = Power, PowerTarget = PowerTarget, Position = Position, Desired = Desired, OneSided = OneSided, Automatic = Automatic, NeverLocked = NeverAppearsLocked, Changed = Changed, Closes = ClosesAutomatically }; }

        internal void Load(State s)
        {
            Power = s.Power; PowerTarget = s.PowerTarget; OneSided = s.OneSided; Automatic = s.Automatic; NeverAppearsLocked = s.NeverLocked; Changed = s.Changed; ClosesAutomatically = s.Closes;
            Position = PreviousPosition = s.Position; Desired = s.Desired; _moving = 0;
            Pose(Position);
        }
    }
}

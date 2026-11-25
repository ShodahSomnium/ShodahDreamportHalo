using System;
using System.Collections.Generic;
using Fusion;
using SomniumSpace.Bridge.Player;
using SomniumSpace.Bridge.Player.Components;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Other players seated in a vehicle, put on THIS client's drawn vehicle every frame: the Euro Truck Simulator Dreamport's rider
    /// technique (EtsRiders, itself the client's MobilePlatformMover.UseLocalOffsetFromDriver), simplified for Halo's fixed seats -
    /// the rider's place is the seat marker (heading only), so nothing but "which vehicle, which seat" travels (HaloNetGame.SetSeat).
    /// The rigid correction (EtsRiderMath.Correct: the whole network body moved so its root lands on the seat, turned by the yaw
    /// difference) is written to BOTH the rider's Fusion transforms (Body/Root, Head, LeftHand, RightHand of their PlayerId - what the
    /// client's receive drivers copy from) AND the Bridge transforms they drive, early in Update (before the client's order-0 drivers)
    /// and again late (after Fusion's Render). A rider whose head is far from the seat (a headset driver in the client-style third
    /// person view, whose own avatar hangs under the chase camera) gets the head put on the seat's eye and the hands at the grips.
    /// IK legs and the falling animation are switched off for riders on this client (ETS Limbs) and given back when they leave.
    /// </summary>
    public static class HaloVehicleRiders
    {
        public struct Rider
        {
            public int PlayerId;           // Fusion PlayerRef.PlayerId (Photon id)
            public ISomniumPlayer Player;  // the Bridge player (HaloRemotes)
            public HaloVehicle Vehicle;
            public int Seat;
            public bool Xr;
        }

        private sealed class Remote
        {
            public int PlayerId; public ISomniumPlayer Player; public HaloVehicle Vehicle; public int Seat; public int Seen;
            public Transform NtRoot, NtHead, NtLeft, NtRight; public float NextBind; public bool BindSaid;
            public Pose RawRoot, RawHead, RawLeft, RawRight; public bool HaveRaw; public bool LegsOff;
        }

        private static readonly Dictionary<int, Remote> Remotes = new Dictionary<int, Remote>();
        private static readonly List<int> Stale = new List<int>();
        private static readonly List<Rider> Now = new List<Rider>();
        private static NetworkBehaviour[] _found;
        private static float _nextSearch;
        private static int _pass;

        /// <summary>The riders this frame (filled by HaloVehicleNet from the net records).</summary>
        public static List<Rider> Current => Now;

        public static void Early()
        {
            _pass++;
            for (int i = 0; i < Now.Count; i++)
            {
                var r = Now[i];
                if (r.Player == null || r.Vehicle == null) continue;
                Remote m;
                if (!Remotes.TryGetValue(r.PlayerId, out m)) { m = new Remote { PlayerId = r.PlayerId }; Remotes.Add(r.PlayerId, m); }
                if (m.Player != r.Player) { m.NtRoot = m.NtHead = m.NtLeft = m.NtRight = null; m.NextBind = 0f; m.LegsOff = false; }
                m.Player = r.Player; m.Vehicle = r.Vehicle; m.Seat = r.Seat; m.Seen = _pass;
                Bind(m);
                Cache(m);
                Apply(m);
                Limbs(m, true);
            }
            Stale.Clear();
            foreach (var kv in Remotes) if (kv.Value.Seen != _pass) Stale.Add(kv.Key);
            for (int i = 0; i < Stale.Count; i++) { Limbs(Remotes[Stale[i]], false); Remotes.Remove(Stale[i]); }
        }

        public static void Late()
        {
            foreach (var kv in Remotes)
            {
                var m = kv.Value;
                if (m.Seen != _pass || m.Vehicle == null) continue;
                Cache(m);
                Apply(m);
            }
        }

        private static void Bind(Remote m)
        {
            if (m.NtRoot != null || Time.unscaledTime < m.NextBind) return;
            m.NextBind = Time.unscaledTime + 1f;
            if (SceneNetworking.NetworkRunnerRef == null) return;
            try
            {
                if (_found == null || Time.unscaledTime >= _nextSearch)
                {
                    _nextSearch = Time.unscaledTime + 1f;
                    _found = UnityEngine.Object.FindObjectsByType<NetworkBehaviour>(FindObjectsSortMode.None);
                }
                for (int i = 0; i < _found.Length; i++)
                {
                    var nt = _found[i];
                    if (nt == null || nt.Object == null || nt.Object.StateAuthority.PlayerId != m.PlayerId) continue;
                    Transform t = nt.transform;
                    if (t.parent == null || t.parent.name != "Body") continue;
                    switch (t.name)
                    {
                        case "Root": m.NtRoot = t; break;
                        case "Head": m.NtHead = t; break;
                        case "LeftHand": m.NtLeft = t; break;
                        case "RightHand": m.NtRight = t; break;
                    }
                }
            }
            catch (Exception e) { HaloLog.Info("Vehicles: finding rider " + m.PlayerId + "'s network body threw " + e.GetType().Name); }
            if (!m.BindSaid || m.NtRoot != null)
            {
                m.BindSaid = true;
                HaloLog.Info("Vehicles: rider " + m.PlayerId + " in " + m.Vehicle.name + " seat " + m.Seat + ": " + (m.NtRoot != null ? "network body bound" : "transform-only fallback (the body root alone is moved)") + ".");
            }
        }

        private static void Cache(Remote m)
        {
            if (m.NtRoot != null)
            {
                m.RawRoot = new Pose(m.NtRoot.position, m.NtRoot.rotation);
                if (m.NtHead != null) m.RawHead = new Pose(m.NtHead.position, m.NtHead.rotation);
                if (m.NtLeft != null) m.RawLeft = new Pose(m.NtLeft.position, m.NtLeft.rotation);
                if (m.NtRight != null) m.RawRight = new Pose(m.NtRight.position, m.NtRight.rotation);
                m.HaveRaw = true; return;
            }
            Transform root = null;
            try { root = m.Player.References?.Body?.Root; } catch (Exception) { root = null; }
            if (root != null) { m.RawRoot = new Pose(root.position, root.rotation); m.HaveRaw = true; }
        }

        private static float YawOf(Quaternion q) { Vector3 f = q * Vector3.forward; return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; }

        private static Pose Correct(Pose raw, Vector3 rawRoot, float rawYaw, Vector3 target, float targetYaw)
        {
            Quaternion turn = Quaternion.Euler(0f, Mathf.DeltaAngle(rawYaw, targetYaw), 0f);
            return new Pose(target + turn * (raw.position - rawRoot), turn * raw.rotation);
        }

        private static void Apply(Remote m)
        {
            if (!m.HaveRaw) return;
            var seat = m.Vehicle.SeatOf(m.Seat);
            if (seat == null) return;
            Vector3 target = seat.Standing && seat.LeftFoot != null ? (seat.LeftFoot.position + seat.RightFoot.position) * 0.5f : seat.Marker.position;
            Vector3 f = Vector3.ProjectOnPlane(m.Vehicle.transform.forward, Vector3.up);
            float targetYaw = f.sqrMagnitude > 1e-6f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            float rawYaw = YawOf(m.RawRoot.rotation);
            Pose root = Correct(m.RawRoot, m.RawRoot.position, rawYaw, target, targetYaw);
            ISomniumPlayerBody body = null;
            try { body = m.Player.References?.Body; } catch (Exception) { body = null; }
            if (m.NtRoot != null) m.NtRoot.SetPositionAndRotation(root.position, root.rotation);
            if (body != null && body.Root != null) body.Root.SetPositionAndRotation(root.position, root.rotation);
            if (m.NtRoot == null) return;
            Vector3 eye = seat.Standing ? target + Vector3.up * 1.62f : target + Vector3.up * 0.72f;
            Pose head = m.NtHead != null ? Correct(m.RawHead, m.RawRoot.position, rawYaw, target, targetYaw) : new Pose(eye, root.rotation);
            bool detached = (head.position - eye).sqrMagnitude > 0.8f * 0.8f; // their view is not in the seat (VR third person)
            Vector3 headShift = detached ? eye - head.position : Vector3.zero;
            Place(m.NtHead, body != null ? body.Head : null, new Pose(head.position + headShift, head.rotation));
            Hand(m.NtLeft, body != null ? body.LeftHand : null, m.RawLeft, m, rawYaw, target, targetYaw, headShift, detached ? seat.LeftGrip : null);
            Hand(m.NtRight, body != null ? body.RightHand : null, m.RawRight, m, rawYaw, target, targetYaw, headShift, detached ? seat.RightGrip : null);
        }

        private static void Hand(Transform nt, Transform driven, Pose raw, Remote m, float rawYaw, Vector3 target, float targetYaw, Vector3 shift, Transform grip)
        {
            if (nt == null) return;
            Pose p = Correct(raw, m.RawRoot.position, rawYaw, target, targetYaw);
            p.position += shift;
            if (grip != null) p.position = grip.position;
            Place(nt, driven, p);
        }

        private static void Place(Transform nt, Transform driven, Pose p)
        {
            if (nt != null) nt.SetPositionAndRotation(p.position, p.rotation);
            if (driven != null) driven.SetPositionAndRotation(p.position, p.rotation);
        }

        private static void Limbs(Remote m, bool on)
        {
            if (m.LegsOff == on) return;
            m.LegsOff = on;
            try
            {
                var f = m.Player != null ? m.Player.Features : null;
                f?.Avatar?.SetVRIKLegMovementDisabled(on);
                f?.Animation?.SetFallingAnimationDisabled(on);
            }
            catch (Exception) { /* the player left */ }
        }

        public static void Clear()
        {
            foreach (var kv in Remotes) Limbs(kv.Value, false);
            Remotes.Clear(); Now.Clear(); _found = null;
        }
    }
}

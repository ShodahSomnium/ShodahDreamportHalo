using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Every player's avatar holds its weapon like Halo's cyborg, on this screen (BO2's Bo2DeskBody on Halo):
    /// - the own avatar on a monitor: found by its head (HaloAvatarFind, the Skyrim Dreamport's way: the model lives under the client's avatar holder,
    ///   not under the Bridge body), posed by HaloDeskPlayer after the client animated it, the held weapon (Combat's <see cref="HaloDesk.HeldWeapon"/>)
    ///   placed where Halo attaches it and aimed at the crosshair's point; in first person the spine, head and hips stay the client's (its camera rides
    ///   the head) and the head is hidden on this screen only;
    /// - every other player Net reports (<see cref="HaloDesk.ReportRemote"/>) without a headset: the same pose from their record (aim = their head);
    ///   their weapon model is built here when Net does not draw it;
    /// - anybody seated in a vehicle: the seat's own animation on the seat marker (VR: legs only);
    /// - the mirror / teleport / shadow copies of each posed avatar get the same pose (HaloAvatarCopies).
    /// VR players' arms are never touched. Runs after the client's Animator and IK (LateUpdate, order 9996: after the isolation driver's 9990), before
    /// skinning. Update: the monitor camera (first person forced through HaloFirstPerson unless the player chose third person with V; the client's
    /// clashing keys held off). Only local bone rotations are written; nothing is networked from them.
    /// </summary>
    [DefaultExecutionOrder(9996)]
    public sealed class HaloDeskBody : MonoBehaviour
    {
        /// <summary>Lab: the own avatar's Animator instead of the client's (null = the client's), and whether to treat the editor as "in a match".</summary>
        public static Func<Animator> LabOwnAvatar;
        public static bool LabInMatch;

        private sealed class Entry
        {
            public int Slot;
            public HaloDeskPlayer P;
            public Animator Anim;
            public float NextSearch, Seen;
            public bool Main, Logged, Posed;
            public int Fires;
            public HaloTag BuiltFor;
            public GameObject Built;
        }

        private readonly List<Entry> _entries = new List<Entry>(8);
        private readonly Entry _own = new Entry { Slot = -1 };
        private readonly Dictionary<int, int> _fires = new Dictionary<int, int>();
        private bool _warned, _ownFirst, _saidHide, _saidView, _released = true;
        private int _logs;

        private void OnEnable()
        {
            HaloEvents.Fired += OnFired;
            HaloEvents.Killed += OnKilled;
            HaloEvents.LocalDied += OnLocalDied;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering += OnEndCamera;
            _own.P = new HaloDeskPlayer("my own avatar");
            HaloLog.Info("Desk: started (" + (HaloDesk.Active ? "monitor" : "headset") + "; view " + (HaloDesk.ThirdPersonWanted ? "third" : "first") + " person, " + HaloDesk.ViewKey + " switches)");
        }

        private void OnDisable()
        {
            HaloEvents.Fired -= OnFired;
            HaloEvents.Killed -= OnKilled;
            HaloEvents.LocalDied -= OnLocalDied;
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            UnityEngine.Rendering.RenderPipelineManager.endCameraRendering -= OnEndCamera;
            for (int i = 0; i < _entries.Count; i++) Drop(_entries[i]);
            _entries.Clear();
            Drop(_own);
            HaloHeadHide.Unhook();
            HaloViewScale.Unhook();
            HaloDesk.HoldsWeapon = false;
            if (!_released) { HaloFirstPerson.Release(); _released = true; }
        }

        private void OnFired(HaloTag w, Vector3 p, Vector3 d, int who)
        {
            if (who < 0 || who == HaloLocal.NetId) { HaloDesk.LocalFires++; return; }
            int n;
            _fires.TryGetValue(who, out n);
            _fires[who] = n + 1;
        }

        /// <summary>How each player died last (slot -> hard, region head), for the death animation; the own one under -1.</summary>
        private readonly Dictionary<int, int> _deaths = new Dictionary<int, int>();

        private void OnKilled(HaloKill k) { if (k.Victim >= 0 && k.Victim != HaloLocal.NetId) _deaths[k.Victim] = DeathCode(k); }
        private void OnLocalDied(HaloKill k) { _deaths[-1] = DeathCode(k); }
        private static int DeathCode(HaloKill k) { return (k.Grenade || k.Splatter ? 1 : 0) | (k.Headshot ? 2 : 0); }

        /// <summary>First person on a monitor: the near plane held close just before the camera draws (the held weapon is 30-40 cm from the eye).</summary>
        private void OnBeginCamera(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null || !HaloDesk.Active || cam != HaloDesk.Cam()) return;
            // the weapon's zoom (Combat: pistol 2x, sniper 2x / 8x): the camera's own field of view divided while it draws, given back after
            if (HaloLocal.Zoomed && HaloLocal.ZoomMagnification > 1.01f && !HaloLocal.VehicleCamera)
            {
                _fovCam = cam;
                _fovBase = cam.fieldOfView;
                float tan = Mathf.Tan(_fovBase * 0.5f * Mathf.Deg2Rad) / HaloLocal.ZoomMagnification;
                cam.fieldOfView = 2f * Mathf.Atan(tan) * Mathf.Rad2Deg;
                if (!_saidZoom) { _saidZoom = true; HaloLog.Info("Desk: monitor zoom x" + HaloLocal.ZoomMagnification.ToString("0.#") + ": field of view " + _fovBase.ToString("0") + " -> " + cam.fieldOfView.ToString("0.0") + " deg while drawing"); }
            }

            if (!HaloDesk.FirstPerson || !HaloDesk.HoldsWeapon) return;
            if (cam.nearClipPlane > HaloDesk.FirstPersonNear) cam.nearClipPlane = HaloDesk.FirstPersonNear;
        }

        private Camera _fovCam;
        private float _fovBase;
        private bool _saidZoom;

        private void OnEndCamera(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            if (_fovCam == null || cam != _fovCam) return;
            cam.fieldOfView = _fovBase;
            _fovCam = null;
        }

        private bool InMatch { get { return LabInMatch || HaloSession.Current == HaloSession.Phase.Playing || HaloSession.Current == HaloSession.Phase.Ending; } }

        // ------------------------------------------------------------------ the camera (Update)

        private void Update()
        {
            try
            {
                bool desk = HaloDesk.Active && InMatch;
                if (!desk)
                {
                    if (!_released) { HaloFirstPerson.Release(); _released = true; }
                    return;
                }

                _released = false;
                if (KeyDown(HaloDesk.ViewKey))
                {
                    HaloDesk.ThirdPersonWanted = !HaloDesk.ThirdPersonWanted;
                    HaloLog.Info("Desk: view " + (HaloDesk.ThirdPersonWanted ? "third person (the client's camera: scroll the wheel out)" : "first person"));
                }

                HaloFirstPerson.ForceFirstPerson = !HaloDesk.ThirdPersonWanted && !HaloLocal.VehicleCamera && HaloLocal.Alive;
                HaloFirstPerson.Tick();
                if (!_saidView) { _saidView = true; HaloLog.Info("Desk: monitor camera " + (HaloFirstPerson.ForceFirstPerson ? "held in first person" : "left to the client") + " (" + HaloDesk.ViewKey + " switches first / third person)"); }
            }
            catch (Exception e)
            {
                if (!_warned) { _warned = true; HaloLog.Warn("Desk: camera step failed (later failures not logged): " + e); }
            }
        }

        private static bool _keyBroken;

        private static bool KeyDown(KeyCode k)
        {
            if (_keyBroken) return false;
            try { return Input.GetKeyDown(k); }
            catch (Exception) { _keyBroken = true; return false; }
        }

        // ------------------------------------------------------------------ the bodies (LateUpdate)

        private void LateUpdate()
        {
            try
            {
                if (HaloDesk.Active && InMatch) HaloFirstPerson.HoldKeys();
                Step(Time.deltaTime);
            }
            catch (Exception e)
            {
                if (!_warned) { _warned = true; HaloLog.Warn("Desk body: posing failed (later failures not logged): " + e); }
            }
        }

        /// <summary>Lab: the posing pass now (the lab draws its pictures from a coroutine, between Combat's Update and this LateUpdate).</summary>
        public void LabPoseNow() { Step(0f); }

        private void Step(float dt)
        {
            HaloDesk.HoldsWeapon = false;
            if (!InMatch) { Drop(_own); return; }
            float now = Time.unscaledTime;
            List<HaloDeskRemote> reps = HaloDesk.Reports;
            for (int i = reps.Count - 1; i >= 0; i--) if (now - reps[i].Seen > 0.5f) reps.RemoveAt(i);
            for (int i = 0; i < reps.Count; i++)
            {
                HaloDeskRemote r = reps[i];
                Entry e = EntryOf(r.Slot);
                e.Seen = now;
                Remote(e, r, dt);
            }

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (now - _entries[i].Seen < 0.5f) continue;
                Drop(_entries[i]);
                _entries.RemoveAt(i);
            }

            Own(dt);
            for (int i = 0; i < _entries.Count; i++) if (_entries[i].Posed) _entries[i].P.CopyPose(ref _logs);
            if (_own.Posed) _own.P.CopyPose(ref _logs);
        }

        private void Remote(Entry e, in HaloDeskRemote r, float dt)
        {
            e.Posed = false;
            if (HaloSeatPoses.OwnsSlot(r.Slot)) { Drop(e); return; }      // Vehicles poses seated riders (HaloSeatPoses): never twice
            bool seated = HaloDesk.PoseSeated && r.Vehicle != null && !string.IsNullOrEmpty(r.SeatLabel);
            if (!r.Alive && !r.Vr && HaloDesk.DeathFall && HaloDesk.PoseOthers)
            {
                Animator da = r.Avatar != null ? r.Avatar : Find(e, r.Head);
                if (da == null) { Drop(e); return; }
                if (e.Anim != da) { if (e.P != null) e.P.Release(); e.Anim = da; }
                if (e.P == null) e.P = new HaloDeskPlayer("player " + r.Slot);
                int code;
                _deaths.TryGetValue(r.Slot, out code);
                e.Posed = e.P.PoseDeath(da, (code & 1) != 0, r.Slot & 3, (code & 2) != 0 ? 2 : 0, dt);
                if (e.Built != null) e.Built.SetActive(false);
                return;
            }

            if (!r.Alive || (!HaloDesk.PoseOthers && !seated) || (r.Vr && !(seated && HaloDesk.SeatPoseVr))) { Drop(e); return; }
            Animator a = r.Avatar != null ? r.Avatar : Find(e, r.Head);
            if (a == null) { Drop(e); return; }
            if (e.Anim != a) { if (e.P != null) e.P.Release(); e.Anim = a; }
            if (e.P == null) e.P = new HaloDeskPlayer("player " + r.Slot);
            int fires;
            _fires.TryGetValue(r.Slot, out fires);
            var input = new HaloPuppetInput { Weapon = r.Weapon, Crouch = r.Crouch, Zoomed = r.Zoomed, Fire = fires, Reload = r.Reloads, Melee = r.Melees, Throw = r.Throws };
            Transform gun = r.Vr ? null : r.WeaponObject != null ? r.WeaponObject : RemoteGun(e, r.Weapon);
            Vector3 gp;
            Quaternion gq;
            if (seated)
            {
                Transform seat = r.SeatMarker != null ? r.SeatMarker : HaloDeskPlayer.FindMarker(r.Vehicle.transform, SeatMarkerName(r.SeatLabel));
                input.Seat = r.SeatLabel;
                e.Posed = e.P.PoseSeated(a, ref input, seat, r.Vehicle.transform, !r.Vr, !r.Vr, dt, out gp, out gq);
                if (gun != null) { gun.gameObject.SetActive(e.Posed && r.Weapon != null); if (e.Posed) gun.SetPositionAndRotation(gp, gq); }
                return;
            }

            Quaternion aim = r.HeadRot;
            Vector3 aimPoint = r.Head + aim * Vector3.forward * 50f;
            e.Posed = e.P.Pose(a, ref input, aim, aimPoint, true, gun != null ? gun.lossyScale.x : 1f, dt, out gp, out gq);
            if (gun != null)
            {
                if (r.WeaponObject == null) gun.gameObject.SetActive(e.Posed);
                if (e.Posed) gun.SetPositionAndRotation(gp, gq);
            }
        }

        /// <summary>The weapon model Desk builds for a remote monitor player whose weapon Net does not draw (one per player, rebuilt on a weapon change).</summary>
        private Transform RemoteGun(Entry e, HaloTag weapon)
        {
            if (weapon == e.BuiltFor) return e.Built != null ? e.Built.transform : null;
            if (e.Built != null) Destroy(e.Built);
            e.Built = null;
            e.BuiltFor = weapon;
            if (weapon == null) return null;
            HaloTag model = null;
            try { model = weapon.Root.Ref(40); } catch (Exception) { model = null; }
            // same size as Net's / Combat's weapons (an avatar-sized cyborg: 1.7 m / 2.146 m), not the full cyborg scale
            HaloModelBuilder.Built b = model != null ? HaloModelBuilder.Build(model, transform, new HaloModelBuilder.Options { Scale = HaloRemoteView.WeaponScale }) : null;
            if (b == null || b.Root == null) return null;
            b.Root.name = "Desk weapon of player " + e.Slot;
            e.Built = b.Root;
            return b.Root.transform;
        }

        private void Own(float dt)
        {
            _own.Posed = false;
            HaloDesk.FirstPerson = false;
            bool desk = HaloDesk.Active;
            bool seated = HaloDesk.PoseSeated && HaloLocal.Vehicle != null && HaloLocal.VehicleTag != null && HaloLocal.SeatIndex >= 0;
            bool alive = HaloLocal.Alive || LabOwnAvatar != null;
            if (!HaloLocal.Alive && LabOwnAvatar == null && desk && HaloDesk.DeathFall && HaloDesk.PoseOwn && _own.Anim != null)
            {
                int code;
                _deaths.TryGetValue(-1, out code);
                _own.Posed = _own.P.PoseDeath(_own.Anim, (code & 1) != 0, 0, (code & 2) != 0 ? 2 : 0, dt);      // (the first-person camera rides the falling head)
                HaloDesk.FirstPerson = false;
                return;
            }

            if (!alive || (!desk && !(seated && HaloDesk.SeatPoseVr)) || (!HaloDesk.PoseOwn && !seated)) { Drop(_own); return; }
            Animator a = LabOwnAvatar != null ? LabOwnAvatar() : OwnAvatar();
            if (a == null) { Drop(_own); NoAvatarHold(desk, seated); return; }
            if (_own.Anim != a) { _own.P.Release(); _own.Anim = a; }
            HaloDesk.Avatar = a;

            Camera cam = HaloDesk.Cam();
            Transform head = _own.P.Poser.Bound(a) ? _own.P.Poser.HeadBone : a.GetBoneTransform(HumanBodyBones.Head);
            Vector3 eye = head != null ? head.position : a.transform.position + Vector3.up * 1.6f;
            float firstR = _ownFirst ? 0.6f : 0.45f;      // (out at 60 cm: a head bob does not flip it)
            bool first = desk && cam != null && (eye - cam.transform.position).sqrMagnitude < firstR * firstR;
            _ownFirst = first;
            HaloDesk.FirstPerson = first;

            HaloLocal.Weapon held = HaloLocal.HeldWeapon;
            HaloTag weapon = held != null ? held.Tag : null;
            Transform gun = HaloDesk.HeldWeapon != null ? HaloDesk.HeldWeapon : HaloCombat.HeldModel;
            var input = new HaloPuppetInput
            {
                Weapon = weapon, Zoomed = HaloLocal.Zoomed,
                Fire = HaloDesk.LocalFires, Reload = HaloDesk.LocalReloads, Melee = HaloDesk.LocalMelees, Throw = HaloDesk.LocalThrows,
            };
            Vector3 gp;
            Quaternion gq;
            if (seated)
            {
                string label, marker;
                SeatOf(HaloLocal.VehicleTag, HaloLocal.SeatIndex, out label, out marker);
                Transform seat = HaloDeskPlayer.FindMarker(HaloLocal.Vehicle.transform, marker);
                input.Seat = label;
                _own.Posed = _own.P.PoseSeated(a, ref input, seat, HaloLocal.Vehicle.transform, desk, desk, dt, out gp, out gq);
                if (_own.Posed && desk && gun != null && HaloLocal.SeatAllowsWeapons && weapon != null && (!first || HaloDesk.PlaceHeldInFirstPerson))
                {
                    gun.SetPositionAndRotation(gp, gq);
                    HaloDesk.HoldsWeapon = true;
                }
            }
            else
            {
                Vector3 aimPoint = HaloDesk.AimPoint;
                Vector3 toAim = aimPoint - eye;
                Quaternion aim = first || toAim.sqrMagnitude < 0.01f ? (cam != null ? cam.transform.rotation : a.transform.rotation) : Quaternion.LookRotation(toAim, Vector3.up);
                float baseScale = gun != null ? gun.lossyScale.x : 1f, vs = 1f;
                bool fpView = first && cam != null && HaloDesk.PlaceHeldInFirstPerson && weapon != null;
                if (fpView)
                {
                    Vector3 vp;
                    Quaternion vr;
                    HaloDesk.FirstPersonPose(cam, weapon, aimPoint, baseScale, out vp, out vr, out vs);
                    _own.P.Poser.ViewSet = true;
                    _own.P.Poser.ViewGun = vp;
                    _own.P.Poser.ViewRot = vr;
                }

                _own.Posed = _own.P.Pose(a, ref input, aim, aimPoint, !first, baseScale * vs, dt, out gp, out gq);
                if (_own.Posed && gun != null && weapon != null && (!first || HaloDesk.PlaceHeldInFirstPerson))
                {
                    gun.SetPositionAndRotation(gp, gq);
                    HaloDesk.HoldsWeapon = true;
                    if (fpView) ViewScale(gun, vs);
                }
            }

            if (first && HaloDesk.HideHead && head != null)
            {
                HaloHeadHide.Hide(head);
                if (!_saidHide)
                {
                    _saidHide = true;
                    HaloLog.Info("Desk body: first person: my avatar's head '" + head.name + "' is hidden on this screen only, while it draws (others still see it)");
                }
            }
        }

        /// <summary>First person: the weapon at the view framing drawn at scale vs, hidden while zoomed (Halo hides its view model then).</summary>
        private void ViewScale(Transform gun, float vs)
        {
            float f = HaloLocal.Zoomed ? 0.0001f : vs;
            if (f < 0.999f) HaloViewScale.Set(gun, f);
            if (!_saidView2)
            {
                _saidView2 = true;
                HaloLog.Info("Desk body: first person: the weapon framed low right (rear-top at " + HaloDesk.FirstPersonScreen.ToString("0.00") + " of the half screen), drawn at x" + vs.ToString("0.00") + " so the hands reach it");
            }
        }

        private bool _saidView2, _saidNoAvatarHold;

        /// <summary>No humanoid avatar found (or none yet): on a monitor in first person the weapon is still framed in the view (no arms).</summary>
        private void NoAvatarHold(bool desk, bool seated)
        {
            if (!desk || seated || !HaloDesk.PlaceHeldInFirstPerson) return;
            Camera cam = HaloDesk.Cam();
            HaloLocal.Weapon held = HaloLocal.HeldWeapon;
            Transform gun = HaloDesk.HeldWeapon != null ? HaloDesk.HeldWeapon : HaloCombat.HeldModel;
            if (cam == null || held == null || held.Tag == null || gun == null) return;
            Transform hb = HaloPlayer.HeadBone;
            bool first = hb == null || HaloFirstPerson.ForceFirstPerson || (hb.position - cam.transform.position).sqrMagnitude < 0.36f;
            if (!first) return;
            HaloDesk.FirstPerson = true;
            Vector3 vp;
            Quaternion vr;
            float vs;
            HaloDesk.FirstPersonPose(cam, held.Tag, HaloDesk.AimPoint, gun.lossyScale.x, out vp, out vr, out vs);
            gun.SetPositionAndRotation(vp, vr * HaloCyborg.ModelFromGun);
            HaloDesk.HoldsWeapon = true;
            ViewScale(gun, vs);
            if (!_saidNoAvatarHold) { _saidNoAvatarHold = true; HaloLog.Info("Desk body: no avatar to hold the weapon: first person frames it in the view without arms"); }
        }

        /// <summary>The vehicle seat's label and marker name (the vehicle tag's seat block: label +4, marker name +36).</summary>
        private static void SeatOf(HaloTag vehicle, int seat, out string label, out string marker)
        {
            label = "";
            marker = "";
            try
            {
                TagReader r = vehicle.Root;
                if (seat < 0 || seat >= r.Count(740)) return;
                TagReader s = r.Element(740, seat, 284);
                label = s.Str32(4);
                marker = s.Str32(36);
            }
            catch (Exception) { /* a broken tag: no seat pose */ }
        }

        /// <summary>A seat's marker from its label when Net does not say (the MP vehicles' seats: driver, gunner, passenger, passenger lf / rf / lb / rb).</summary>
        private static string SeatMarkerName(string label)
        {
            if (string.IsNullOrEmpty(label)) return "";
            string l = label.ToLowerInvariant();
            if (l.EndsWith("lf")) return "passenger lf";
            if (l.EndsWith("rf")) return "passenger rf";
            if (l.EndsWith("lb")) return "passenger lb";
            if (l.EndsWith("rb")) return "passenger rb";
            if (l.Contains("driver")) return "driver";
            if (l.Contains("gunner")) return "gunner";
            if (l.Contains("passenger")) return "passenger";
            return "driver";
        }

        // ------------------------------------------------------------------ finding avatars

        private Entry EntryOf(int slot)
        {
            for (int i = 0; i < _entries.Count; i++) if (_entries[i].Slot == slot) return _entries[i];
            var e = new Entry { Slot = slot };
            _entries.Add(e);
            return e;
        }

        private float _ownNext;
        private Animator _ownAnim;
        private bool _ownMain, _ownLogged;

        /// <summary>The own avatar: by the Bridge's head bone (HaloAvatarFind), searched at most once a second while missing, every 2 s while not the main copy.</summary>
        private Animator OwnAvatar()
        {
            Transform hb = HaloPlayer.HeadBone;
            float now = Time.unscaledTime;
            bool alive = _ownAnim != null && _ownAnim.gameObject.activeInHierarchy;
            if (alive && (_ownMain || now < _ownNext)) return _ownAnim;
            if (!alive && now < _ownNext) return null;
            _ownNext = now + (alive ? 2f : 1f);
            if (hb == null) return alive ? _ownAnim : null;
            string kind;
            Animator a = HaloAvatarFind.ByHead(hb.position, out kind);
            if (a == null)
            {
                for (Transform t = hb; t != null && a == null; t = t.parent)
                {
                    Animator x = t.GetComponent<Animator>();
                    if (x != null && x.isHuman && x.avatar != null) a = x;
                }

                kind = "above the Bridge's head";
            }

            if (a != _ownAnim || !_ownLogged)
            {
                if (_logs < 30)
                {
                    _logs++;
                    HaloLog.Info(a != null ? "Desk body: posing my own avatar '" + a.name + "' (" + kind + ", head " + (a.GetBoneTransform(HumanBodyBones.Head).position - hb.position).magnitude.ToString("0.00") + " m from the Bridge's)"
                                           : "Desk body: no humanoid avatar found for my own body (not posed)");
                }

                _ownLogged = true;
            }

            _ownMain = kind == "main copy";
            _ownAnim = a;
            return a;
        }

        private Animator Find(Entry e, Vector3 head)
        {
            float now = Time.unscaledTime;
            bool alive = e.Anim != null && e.Anim.gameObject.activeInHierarchy;
            if (alive && (e.Main || now < e.NextSearch)) return e.Anim;
            if (!alive && now < e.NextSearch) return null;
            e.NextSearch = now + (alive ? 2f : 1f);
            string kind;
            Animator a = HaloAvatarFind.ByHead(head, out kind);
            if ((a != e.Anim || !e.Logged) && _logs < 30)
            {
                _logs++;
                e.Logged = true;
                HaloLog.Info(a != null ? "Desk body: posing player " + e.Slot + "'s avatar '" + a.name + "' (" + kind + ")" : "Desk body: no humanoid avatar found for player " + e.Slot + " near their head");
            }

            e.Main = kind == "main copy";
            return a;
        }

        private void Drop(Entry e)
        {
            if (e.P != null) e.P.Release();
            if (e.P != null) e.P.Copies = null;
            e.Anim = null;
            e.Posed = false;
            if (e.Built != null) { Destroy(e.Built); e.Built = null; }
            e.BuiltFor = null;
            if (e == _own) HaloHeadHide.Restore();
        }
    }
}

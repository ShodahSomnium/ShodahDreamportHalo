using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The seated player's controls.
    ///
    /// Monitor (Halo PC's own): W / S throttle, A / D strafe (ghost, banshee) or turn (warthog, scorpion), the mouse looks and the
    /// vehicle steers toward where the camera looks, left mouse fires the vehicle gun (or the gunner's), right mouse its secondary
    /// trigger (scorpion machine gun, banshee fuel rod), Space brakes, E gets out (Halo's action key; the client's own E "Rotate" is
    /// held off in a match by HaloFirstPerson).
    ///
    /// Headset, the Somnium client's own vehicle bindings (Inputs/VR/XRI Vehicle inputs.inputactions) for a vehicle without a gun in the
    /// driver's hands (warthog, rocket warthog): right trigger accelerate, left trigger brake / reverse, left stick steer (and its y as a
    /// throttle too), right B gets out, left Y switches third / first person. A driver who also fires (ghost, banshee, scorpion, turret):
    /// left stick drives (y throttle, x strafe for the ghost / turn), right stick x turns, right trigger fires where the right hand
    /// points, left trigger fires the secondary. Steering: the stick by default; "drive where you look" (Halo's control, the head's
    /// heading) is the setting vehicles.vrsteer = 1 - the head is free to look round while driving with the stick, which is why it is
    /// the default. The warthog gunner holds the chain gun's two grips (grip button within 25 cm of the "gunner left / right hand"
    /// markers) and aims by moving them; either trigger fires while a grip is held.
    /// </summary>
    public static class HaloVehicleInput
    {
        public struct Intent
        {
            public HaloVehicleControls Drive;
            public Vector3 GunAim;      // world direction the gun should turn to (zero = leave it)
            public bool Fire, Fire2;
            public bool Exit, ToggleView;
            public bool Holding;        // VR gunner has the grips
        }

        private static bool _keysBroken;
        private const float GripReach = 0.35f; // ours

        public static bool VrLookSteer => HaloSettings.Int("vehicles.vrsteer", 0) == 1;

        /// <summary>The lab drives with this instead of the devices.</summary>
        public static bool Simulated;
        public static Intent Sim;

        public static Intent Read(HaloVehicle v, HaloVehicle.SeatInfo seat)
        {
            var i = new Intent();
            if (v == null || seat == null) return i;
            if (Simulated) return Sim;
            if (HaloPlayer.XrPresent || LabVr) Vr(v, seat, ref i);
            else Desk(v, seat, ref i);
            return i;
        }

        private static bool Key(KeyCode k)
        {
            if (_keysBroken) return false;
            try { return Input.GetKey(k); } catch (Exception) { _keysBroken = true; return false; }
        }

        private static bool KeyDown(KeyCode k)
        {
            if (_keysBroken) return false;
            try { return Input.GetKeyDown(k); } catch (Exception) { _keysBroken = true; return false; }
        }

        private static bool Mouse(int b)
        {
            if (_keysBroken) return false;
            try { return Input.GetMouseButton(b); } catch (Exception) { _keysBroken = true; return false; }
        }

        private static void Desk(HaloVehicle v, HaloVehicle.SeatInfo seat, ref Intent i)
        {
            HaloVehicleCamera.Look(seat);
            float fwd = (Key(KeyCode.W) || Key(KeyCode.UpArrow) ? 1f : 0f) - (Key(KeyCode.S) || Key(KeyCode.DownArrow) ? 1f : 0f);
            float side = (Key(KeyCode.D) || Key(KeyCode.RightArrow) ? 1f : 0f) - (Key(KeyCode.A) || Key(KeyCode.LeftArrow) ? 1f : 0f);
            bool strafer = v.Def.Type == HaloVehicleDef.Kind.AlienScout || v.Def.Type == HaloVehicleDef.Kind.AlienFighter;
            i.Drive.Throttle = fwd;
            i.Drive.Strafe = strafer ? side : 0f;
            i.Drive.Turn = strafer ? 0f : side;
            i.Drive.Aim = HaloVehicleCamera.LookDirection;
            i.Drive.SteerToAim = side == 0f || strafer;
            i.Drive.Brake = Key(KeyCode.Space);
            Vector3 pivot = v.GunPivot.position;
            Vector3 to = HaloVehicleCamera.AimPoint - pivot;
            i.GunAim = to.sqrMagnitude > 1f ? to.normalized : HaloVehicleCamera.LookDirection;
            i.Fire = Mouse(0);
            i.Fire2 = Mouse(1);
            i.Exit = KeyDown(KeyCode.E);
        }

        /// <summary>Lab: run the headset mapping with HaloXr's simulated controllers and these stand-in hand / head transforms.</summary>
        public static bool LabVr;
        public static Transform LabLeftHand, LabRightHand, LabHead;

        private static Transform HandT(bool right) { Transform t = right ? LabRightHand : LabLeftHand; return t != null ? t : (right ? HaloPlayer.RightHand : HaloPlayer.LeftHand); }

        /// <summary>Guns fixed on the hull: the ghost's and the banshee's bolts go where the vehicle points (Halo: you steer to aim).</summary>
        public static bool FixedGuns(HaloVehicle v)
        {
            return v.Def.Type == HaloVehicleDef.Kind.AlienScout || v.Def.Type == HaloVehicleDef.Kind.AlienFighter;
        }

        private static void Vr(HaloVehicle v, HaloVehicle.SeatInfo seat, ref Intent i)
        {
            HaloHand l = HaloXr.Hand(false), r = HaloXr.Hand(true);
            i.Exit = r.SecondaryPressed || (v.Campaign && v.HasHatch && r.PrimaryPressed);      // a scripted hatch (the cryotube): A too (Halo's "Use [Action] to exit")
            i.ToggleView = l.SecondaryPressed;
            Transform headT = LabHead;
            if (headT == null) { Camera head = HaloPlayer.Head(); headT = head != null ? head.transform : null; }
            Vector3 headFwd = headT != null ? headT.forward : v.transform.forward;
            if (seat.Def.Driver)
            {
                bool gunDriver = seat.Guns;
                if (!gunDriver)
                {
                    // warthog / rocket warthog: the client's own vehicle bindings
                    float trig = r.Trigger - l.Trigger;
                    i.Drive.Throttle = Mathf.Clamp(Mathf.Abs(trig) > 0.05f ? trig : l.Stick.y, -1f, 1f);
                    i.Drive.Turn = l.Stick.x;
                }
                else
                {
                    // ghost / banshee / scorpion / turret: the left stick drives, the right stick turns, the triggers fire
                    i.Drive.Throttle = l.Stick.y;
                    bool strafer = v.Def.Type == HaloVehicleDef.Kind.AlienScout;
                    i.Drive.Strafe = strafer ? l.Stick.x : 0f;
                    i.Drive.Turn = Mathf.Clamp((strafer ? 0f : l.Stick.x) + r.Stick.x, -1f, 1f);
                    i.Fire = r.TriggerHeld;
                    i.Fire2 = l.TriggerHeld;
                    // the scorpion's cannon / the turret aim where the head looks; ghost and banshee guns are fixed forward
                    i.GunAim = FixedGuns(v) ? v.transform.forward : headFwd;
                }
                i.Drive.SteerToAim = VrLookSteer || v.Def.Type == HaloVehicleDef.Kind.AlienFighter && Mathf.Abs(i.Drive.Turn) < 0.1f;
                // the banshee flies where its pilot looks (pitch included); the others steer to the head's heading in "look" mode
                i.Drive.Aim = v.Def.Type == HaloVehicleDef.Kind.AlienFighter ? headFwd : Vector3.ProjectOnPlane(headFwd, Vector3.up);
                i.Drive.Brake = r.Primary;
                return;
            }
            if (!seat.Guns) return;
            // the gunner: either trigger fires, held on the grips or not
            i.Fire = l.TriggerHeld || r.TriggerHeld;
            if (seat.LeftGrip != null && seat.RightGrip != null)
            {
                Transform lh = HandT(false), rh = HandT(true);
                bool lHold = lh != null && l.GripHeld && (lh.position - seat.LeftGrip.position).sqrMagnitude < GripReach * GripReach;
                bool rHold = rh != null && r.GripHeld && (rh.position - seat.RightGrip.position).sqrMagnitude < GripReach * GripReach;
                i.Holding = lHold || rHold;
                if (i.Holding)
                {
                    // on the grips: the gun turns about its pivot so the grips follow the hands
                    Vector3 gripMid = lHold && rHold ? (seat.LeftGrip.position + seat.RightGrip.position) * 0.5f : (lHold ? seat.LeftGrip.position : seat.RightGrip.position);
                    Vector3 handMid = lHold && rHold ? (lh.position + rh.position) * 0.5f : (lHold ? lh.position : rh.position);
                    Vector3 pivot = v.GunPivot.position;
                    Vector3 dg = gripMid - pivot, dh = handMid - pivot;
                    if (dg.sqrMagnitude > 1e-4f && dh.sqrMagnitude > 1e-4f) i.GunAim = Quaternion.FromToRotation(dg, dh) * v.AimDirection;
                    return;
                }
            }
            // off the grips (and the shade): the gun turns to where the firing hand points (the right one unless only the left trigger is held)
            bool useLeft = l.TriggerHeld && !r.TriggerHeld;
            i.GunAim = HandAim(!useLeft, headFwd);
            if (seat.LeftGrip == null) { i.Fire = r.TriggerHeld; i.Fire2 = l.TriggerHeld; }
        }

        private static Vector3 HandAim(bool right, Vector3 fallback)
        {
            Transform h = HandT(right);
            if (h == null) return fallback;
            if (h == (right ? LabRightHand : LabLeftHand)) return h.forward;      // the lab's stand-in hands are in the aim frame
            return h.rotation * HaloBody.GripToAim * Vector3.forward;
        }
    }
}

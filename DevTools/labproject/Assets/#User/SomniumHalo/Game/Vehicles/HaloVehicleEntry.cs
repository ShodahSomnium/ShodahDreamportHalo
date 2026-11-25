using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Getting in. Halo lets a player into the free seat whose "enter" marker is nearest when they are close to it; the client's
    /// own rule is a 1.2 m reach round the player (NonXRVehicleFeature). Both are used: a seat is in reach when its enter marker is
    /// within 1.6 m (ours) horizontally of the player, and the nearest free one wins. In reach the HUD gets Halo's prompt
    /// (HaloEvents.Message) and:
    ///  - monitor: E gets in (Halo's action key); a click on the vehicle does too;
    ///  - headset: grip or trigger on the vehicle (the ETS door handle: a solid box on layer 16 that touches nothing, an XRI
    ///    XRSimpleInteractable for the client's Near-Far interactor - activate and select - and IPointerClickHandler for a desktop
    ///    click), or A on the right controller.
    /// An overturned vehicle in reach is flipped back instead (Halo). A seat a remote player holds is not offered.
    /// </summary>
    public static class HaloVehicleEntry
    {
        public const float Reach = 1.6f;   // ours
        private static HaloVehicle _target;
        private static int _targetSeat = -1;
        private static float _nextPrompt;
        private static string _lastPrompt;
        private static GameObject _handle;
        private static BoxCollider _box;
        private static bool _said;

        public static HaloVehicle Target => _target;
        public static int TargetSeat => _targetSeat;

        /// <summary>Every frame while on foot in a match.</summary>
        public static void Tick(System.Collections.Generic.List<HaloVehicle> all)
        {
            Vector3 feet;
            if (!HaloLocal.Alive || !HaloPlayer.TryFeet(out feet)) { Clear(); return; }
            Transform head = HaloPlayer.HeadBone;
            Vector3 p = head != null && HaloPlayer.XrPresent ? new Vector3(head.position.x, feet.y, head.position.z) : feet;
            HaloVehicle best; int bestSeat;
            Choose(all, p, out best, out bestSeat);
            if (best != _target || bestSeat != _targetSeat) { _target = best; _targetSeat = bestSeat; _nextPrompt = 0f; }
            HaloLocal.VehicleInReach = best != null ? best.gameObject : null;
            HaloLocal.VehicleInReachSeat = best != null ? best.Seats[bestSeat].Label : "";
            if (best == null) { HideHandle(); return; }
            Prompt(best, bestSeat);
            ShowHandle(best, bestSeat);
            bool pressed = false;
            try { pressed = !HaloPlayer.XrPresent ? Input.GetKeyDown(KeyCode.E) : HaloXr.Hand(true).PrimaryPressed; } catch (System.Exception) { pressed = false; }
            if (pressed) Use("key");
        }

        /// <summary>The seat a player standing at p gets (the one the prompt names and Use takes): the free, valid seat whose enter marker is
        /// nearest (within Reach), the driver's also from beside the hull.</summary>
        public static void Choose(System.Collections.Generic.List<HaloVehicle> all, Vector3 p, out HaloVehicle best, out int bestSeat)
        {
            best = null; bestSeat = -1; float bestD = Reach * Reach;
            for (int k = 0; k < all.Count; k++)
            {
                var v = all[k];
                if (v == null || !v.isActiveAndEnabled) continue;
                if (v.Destroyed || !v.EnterableByPlayer) continue;
                if ((v.transform.position - p).sqrMagnitude > 15f * 15f) continue;
                // how far the player is from the hull (for the driver seat: Halo lets you in from beside the hull or the hatch)
                Bounds hb = v.ShapeBounds();
                Vector3 onHull = hb.ClosestPoint(p);
                float hull = new Vector2(onHull.x - p.x, onHull.z - p.z).magnitude;
                bool hasDriver = v.HasDriver;
                for (int s = 0; s < v.Seats.Count; s++)
                {
                    var seat = v.Seats[s];
                    if (seat.Occupant >= 0 || seat.Def.Invisible) continue;
                    // the tag's seat flag "not valid without driver" (the scorpion's four riders): only once somebody drives
                    if ((seat.Def.Flags & HaloVehicleDef.SeatNotValidWithoutDriver) != 0 && !hasDriver) continue;
                    Vector3 e = seat.Enter != null ? seat.Enter.position : seat.Marker.position;
                    Vector3 d = e - p; d.y *= 0.35f;
                    float dd = d.sqrMagnitude;
                    // the driver seat also from beside the hull (counted as 1 m from its door: a nearer enter marker still wins)
                    if (seat.Def.Driver) { float viaHull = hull + 1f; if (viaHull * viaHull < dd) dd = viaHull * viaHull; }
                    if (dd < bestD) { bestD = dd; best = v; bestSeat = s; }
                }
            }
        }

        private static void Prompt(HaloVehicle v, int seat)
        {
            if (Time.time < _nextPrompt) return;
            _nextPrompt = Time.time + 1f;
            string key = HaloPlayer.XrPresent ? "A (or grip the vehicle)" : "E";
            string text;
            if (v.Overturned) text = "Press " + key + " to flip the " + v.Label;
            else
            {
                var s = v.Seats[seat];
                string what = s.Def.Driver ? (v.Def.Type == HaloVehicleDef.Kind.Turret ? "man the " : "drive the ") : s.Guns ? "man the gun of the " : "ride in the ";
                text = "Press " + key + " to " + what + v.Label;
            }
            HaloEvents.RaiseMessage(text, 1.2f);
            if (text != _lastPrompt) { _lastPrompt = text; HaloLog.Info("Vehicles: prompt '" + text + "'."); }
        }

        /// <summary>Gets in (or flips) the vehicle in reach.</summary>
        public static void Use(string how)
        {
            var v = _target; int s = _targetSeat;
            if (v == null || s < 0 || HaloVehicleSeat.Local.Seated) return;
            if (v.Overturned) { if (v.LocalDriven || !v.RemoteDriven) v.Flip(); return; }
            if (!HaloVehicleNet.SeatFree(v, s)) { HaloEvents.RaiseMessage("That seat is taken", 1.5f); return; }
            HaloLog.Info("Vehicles: " + how + " -> getting into " + v.name + " seat " + s + ".");
            if (HaloVehicleSeat.Local.GetIn(v, s)) { HaloVehicleCamera.Reset(v); Clear(); }
        }

        private static void ShowHandle(HaloVehicle v, int seat)
        {
            if (_handle == null)
            {
                _handle = new GameObject("Halo vehicle get in (grip / click)");
                _handle.SetActive(false);
                _handle.layer = HaloLayers.Interactable;
                _box = _handle.AddComponent<BoxCollider>();
                _box.isTrigger = false; _box.excludeLayers = ~0; _box.providesContacts = false;
                var xi = _handle.AddComponent<XRSimpleInteractable>();
                xi.interactionLayers = 1 << 4; // the client's Near-Far interactor carries XRI layer 4 'XRInteractable' (ETS)
                xi.activated.AddListener(OnActivated);
                xi.selectEntered.AddListener(OnSelected);
                _handle.AddComponent<HaloVehicleHandleClick>();
            }
            Bounds b = v.ShapeBounds();
            if (_handle.transform.parent != v.transform) _handle.transform.SetParent(v.transform, false);
            _handle.transform.localPosition = v.transform.InverseTransformPoint(b.center);
            _handle.transform.localRotation = Quaternion.identity;
            _box.size = new Vector3(Mathf.Max(1f, b.size.x * 0.9f), Mathf.Max(0.8f, b.size.y * 0.9f), Mathf.Max(1f, b.size.z * 0.9f));
            if (!_handle.activeSelf)
            {
                _handle.SetActive(true);
                if (!_said) { _said = true; HaloLog.Info("Vehicles: the vehicle in reach can be got into: " + (HaloPlayer.XrPresent ? "grip / trigger on it, or A." : "E, or click it.")); }
            }
        }

        private static void OnActivated(ActivateEventArgs a) { Use("trigger on the vehicle"); }
        private static void OnSelected(SelectEnterEventArgs a) { Use("grip on the vehicle"); }

        private static void HideHandle()
        {
            if (_handle != null && _handle.activeSelf) _handle.SetActive(false);
        }

        public static void Clear()
        {
            _target = null; _targetSeat = -1;
            HaloLocal.VehicleInReach = null; HaloLocal.VehicleInReachSeat = "";
            HideHandle();
        }

        public static void Destroy()
        {
            Clear();
            if (_handle != null) Object.Destroy(_handle);
            _handle = null; _box = null; _said = false;
        }
    }

    /// <summary>The desktop click on the get-in handle (the client's EventSystem delivers it; ETS EtsTruckDoorHandle).</summary>
    public sealed class HaloVehicleHandleClick : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) HaloVehicleEntry.Use("click on the vehicle");
        }
    }
}

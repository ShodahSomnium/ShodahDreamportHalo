using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] custom_animation*, unit_suspended, unit vitality, AI units in vehicle seats: HaloUnit / HaloBipeds (C),
    /// seats of HaloVehicle (V).</summary>
    public static partial class HaloScriptFns
    {
        /// <summary>Length (seconds) of the custom animation each unit was last given (unit_get_custom_animation_time).</summary>
        static readonly Dictionary<HaloUnit, float> CustomLength = new Dictionary<HaloUnit, float>();

        static void BindCharacters(HsVm vm)
        {
            vm.Bind("custom_animation", CustomAnimation);
            vm.Bind("custom_animation_list", CustomAnimationList);
            vm.Bind("unit_custom_animation_at_frame", CustomAnimationAtFrame);
            vm.Bind("unit_stop_custom_animation", StopCustomAnimation);
            vm.Bind("unit_get_custom_animation_time", CustomAnimationTime);
            vm.Bind("unit_suspended", UnitSuspended);
            vm.Bind("unit_set_current_vitality", UnitSetCurrentVitality);
            vm.Bind("units_set_current_vitality", UnitsSetCurrentVitality);
            vm.Bind("unit_kill", UnitKill);
            vm.Bind("unit_kill_silent", UnitKillSilent);
        }

        static bool PlayCustom(HaloUnit u, HaloTag graph, string anim, bool interpolate, int frame)
        {
            if (u == null || u.Animator == null) return false;
            HaloAnimGraph g = graph != null ? HaloBipeds.GraphOf(graph) : u.Animator.Graph;
            HaloAnimation an = g != null ? g.Get(anim) : null;
            if (an == null) { Once("anim " + anim, "Script: custom_animation '" + anim + "' not in " + (graph != null ? graph.Name : "the unit's graph")); return false; }
            bool ok = frame <= 0 ? u.CustomAnimation(graph, anim, interpolate) : u.Animator.PlayCustom(g, an, interpolate, false, false, frame / 30f);
            if (ok) CustomLength[u] = an.Duration;
            return ok;
        }

        static int CustomAnimation(HsArgs a) => B(PlayCustom(HaloScriptWorld.Unit(a.Obj(0)), a.Tag(1), a.Str(2), a.Bool(3), 0));
        static int CustomAnimationAtFrame(HsArgs a) => B(PlayCustom(HaloScriptWorld.Unit(a.Obj(0)), a.Tag(1), a.Str(2), a.Bool(3), a.Int(4)));

        static int CustomAnimationList(HsArgs a)
        {
            int l = ListOf(a, 0), n = Count(l);
            HaloTag g = a.Tag(1); string anim = a.Str(2); bool interp = a.Bool(3);
            bool any = false;
            for (int i = 0; i < n; i++) any |= PlayCustom(HaloScriptWorld.Unit(At(l, i)), g, anim, interp, 0);
            return B(any);
        }

        static int StopCustomAnimation(HsArgs a) { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null) u.StopCustomAnimation(); return 0; }

        /// <summary>Ticks left in the unit's custom animation (0 when it is over).</summary>
        static int CustomAnimationTime(HsArgs a)
        {
            var u = HaloScriptWorld.Unit(a.Obj(0));
            float len;
            if (u == null || !u.PlayingCustomAnimation || u.Animator == null || !CustomLength.TryGetValue(u, out len)) return 0;
            return Mathf.Max(0, Mathf.CeilToInt((len - u.Animator.CustomTime) * 30f));
        }

        static int UnitSuspended(HsArgs a)
        {
            int h = a.Obj(0);
            if (HaloScriptWorld.IsPlayer(h)) { Once("suspend player", "Script: unit_suspended on the player ignored (VR: the player is never frozen by a script)"); return 0; }
            var u = HaloScriptWorld.Unit(h);
            if (u != null) u.Frozen = a.Bool(1);
            return 0;
        }

        static void SetVitality(HaloUnit u, float body, float shield)
        {
            if (u != null) u.SetCurrentVitality(body, shield);   // C: points against MaxBody / MaxShield
        }

        static int UnitSetCurrentVitality(HsArgs a) { SetVitality(HaloScriptWorld.Unit(a.Obj(0)), a.Real(1), a.Real(2)); return 0; }

        static int UnitsSetCurrentVitality(HsArgs a)
        {
            int l = ListOf(a, 0), n = Count(l);
            for (int i = 0; i < n; i++) SetVitality(HaloScriptWorld.Unit(At(l, i)), a.Real(1), a.Real(2));
            return 0;
        }

        static int UnitKill(HsArgs a) { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null && u.Alive) u.Kill(); return 0; }
        static int UnitKillSilent(HsArgs a) { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null && u.Alive) u.KillSilent(); return 0; }

        // ------------------------------------------------------------------ AI / scripted units in seats

        static readonly List<HaloUnit> Seated = new List<HaloUnit>();

        static bool SeatTaken(HaloVehicle v, int seat)
        {
            if (v.Seats[seat].Occupant >= 0) return true;
            for (int i = 0; i < Seated.Count; i++) { var u = Seated[i]; if (u != null && u.Seat == v.Seats[seat].Marker) return true; }
            return false;
        }

        static void SeatUnit(HaloUnit u, HaloVehicle v, string part, string fn)
        {
            if (u == null || v == null || !u.Alive) return;
            if (u.Seat != null && u.Seat.GetComponentInParent<HaloVehicle>() == v) return;
            for (int i = 0; i < v.Seats.Count; i++)
            {
                var s = v.Seats[i];
                string label = s.Label ?? "";
                if (s.Marker == null || SeatTaken(v, i)) continue;
                if (!string.IsNullOrEmpty(part) && label.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!v.SeatUnit(i, u)) continue;   // V: EnterSeat on the marker + the seat marked taken
                if (!Seated.Contains(u)) Seated.Add(u);
                return;
            }
            Once(fn + " full " + v.name + part, "Script: " + fn + " - no free seat '" + part + "' in " + v.name + " for " + u.name);
        }

        static void UnseatUnit(HaloUnit u, HaloVehicle v)
        {
            if (u == null || u.Seat == null) return;
            var vv = u.Seat.GetComponentInParent<HaloVehicle>();
            if (v != null && vv != v) return;   // (not in v - also not in a vehicle carried by v)
            if (vv != null && vv.SeatOfUnit(u) >= 0) vv.UnseatUnit(u);
            else u.ExitSeat(u.transform.position + Vector3.right * 1.5f, u.Yaw);
            Seated.Remove(u);
        }

        static bool UnitIn(HaloUnit u, HaloVehicle v, string part)
        {
            if (u == null || u.Seat == null || v == null || !u.Seat.IsChildOf(v.transform)) return false;
            if (u.Seat.GetComponentInParent<HaloVehicle>() != v) return false;   // a rider of a vehicle carried as cargo is not v's rider
            return string.IsNullOrEmpty(part) || (u.SeatLabel ?? "").IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

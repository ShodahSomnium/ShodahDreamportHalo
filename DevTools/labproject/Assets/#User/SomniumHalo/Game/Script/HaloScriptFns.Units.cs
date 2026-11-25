using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] unit_* / vehicle_*: HaloUnit (C) vitality, the local player's seats through HaloVehicleSeat (V).
    /// AI units in vehicles and custom animations wait for the Characters / Vehicles / AI APIs (see REQUESTS.md).</summary>
    public static partial class HaloScriptFns
    {
        static void BindUnits(HsVm vm)
        {
            vm.Bind("unit_get_health", UnitGetHealth);
            vm.Bind("unit_get_shield", UnitGetShield);
            vm.Bind("unit_enter_vehicle", UnitEnterVehicle);
            vm.Bind("unit_exit_vehicle", UnitExitVehicle);
            vm.Bind("vehicle_load_magic", VehicleLoadMagic);
            vm.Bind("vehicle_unload", VehicleUnload);
            vm.Bind("vehicle_riders", VehicleRiders);
            vm.Bind("vehicle_test_seat_list", VehicleTestSeatList);
            vm.Bind("vehicle_hover", a => { var v = HaloScriptWorld.Vehicle(a.Obj(0)); if (v != null) v.Hover(a.Bool(1)); return 0; });
            vm.Bind("unit_open", a => { var v = HaloScriptWorld.Vehicle(a.Obj(0)); if (v != null) v.Open(true); return 0; });
            vm.Bind("unit_close", a => { var v = HaloScriptWorld.Vehicle(a.Obj(0)); if (v != null) v.Open(false); return 0; });
            vm.Bind("unit_set_enterable_by_player", a => { var v = HaloScriptWorld.Vehicle(a.Obj(0)); if (v != null) v.EnterableByPlayer = a.Bool(1); return 0; });
        }

        static int UnitGetHealth(HsArgs a)
        {
            int h = a.Obj(0);
            if (HaloScriptWorld.IsPlayer(h)) return R(HaloLocal.Alive ? HaloLocal.Health : 0f);
            var u = HaloScriptWorld.Unit(h);
            return R(u != null ? (u.Alive ? u.Body : 0f) : -1f);
        }

        static int UnitGetShield(HsArgs a)
        {
            int h = a.Obj(0);
            if (HaloScriptWorld.IsPlayer(h)) return R(HaloLocal.Shield);
            var u = HaloScriptWorld.Unit(h);
            return R(u != null ? u.Shield : -1f);
        }

        /// <summary>The first seat of the vehicle whose label contains <paramref name="part"/> ("" = any free seat).</summary>
        static int SeatMatching(HaloVehicle v, string part, bool free)
        {
            if (v == null) return -1;
            for (int i = 0; i < v.Seats.Count; i++)
            {
                var s = v.Seats[i];
                if (free && s.Occupant >= 0) continue;
                string label = s.Label ?? "";
                if (string.IsNullOrEmpty(part) || label.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) return i;
            }
            return -1;
        }

        static bool LocalIn(HaloVehicle v) => v != null && HaloVehicleSeat.Local.Vehicle == v;

        static void Seat(int unit, HaloVehicle v, string seat, string fn)
        {
            if (v == null) return;
            if (HaloScriptWorld.IsPlayer(unit))
            {
                if (LocalIn(v)) return;
                if (HaloCinematic.HoldsPlayer) { Once(fn + " held", "Script: " + fn + " of the player during a cutscene skipped (Q holds the VR player where they stand)"); return; }
                int si = SeatMatching(v, seat, true);
                if (si < 0) { Once(fn + " noseat " + seat, "Script: " + fn + " - no free seat '" + seat + "' in " + v.name); return; }
                bool ok = HaloVehicleSeat.Local.GetIn(v, si);
                Once(fn + " player", "Script: " + fn + " put the player in seat '" + v.Seats[si].Label + "' (" + (ok ? "ok" : "refused") + ")");
                return;
            }
            var u = HaloScriptWorld.Unit(unit);
            if (u != null) { SeatUnit(u, v, seat, fn); return; }
            // a vehicle into a vehicle's seat (Halo: vehicles are units - the pelican's "cargo", the dropship's "cargo_ghost01..03")
            var cargo = HaloScriptWorld.Vehicle(unit);
            if (cargo != null && cargo != v)
            {
                SeatVehicle(cargo, v, seat, fn);
                return;
            }
            Once(fn + " what " + unit, "Script: " + fn + " " + v.name + ": object " + unit + " is not a unit or vehicle - nothing seated");
        }

        /// <summary>Seats a cargo vehicle on the first free seat whose label (or marker) contains <paramref name="part"/>.</summary>
        static void SeatVehicle(HaloVehicle cargo, HaloVehicle v, string part, string fn)
        {
            if (cargo.CarriedBy == v) return;
            for (int i = 0; i < v.Seats.Count; i++)
            {
                var s = v.Seats[i];
                if (s.Occupant >= 0 || s.Cargo != null) continue;
                if (!string.IsNullOrEmpty(part) && (s.Label ?? "").IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0
                    && (s.Def.Marker ?? "").IndexOf(part, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (v.SeatVehicle(i, cargo))
                {
                    Once(fn + " cargo " + cargo.name, "Script: " + fn + " seated vehicle " + cargo.name + " as cargo in " + v.name + " seat '" + s.Label + "'");
                    return;
                }
            }
            Once(fn + " cargo full " + cargo.name + part, "Script: " + fn + " - no free seat '" + part + "' in " + v.name + " for vehicle " + cargo.name);
        }

        static int UnitEnterVehicle(HsArgs a) { Seat(a.Obj(0), HaloScriptWorld.Vehicle(a.Obj(1)), a.Str(2), "unit_enter_vehicle"); return 0; }

        static int VehicleLoadMagic(HsArgs a)
        {
            var v = HaloScriptWorld.Vehicle(a.Obj(0));
            int l = ListOf(a, 2), n = Count(l);
            for (int i = 0; i < n; i++) Seat(At(l, i), v, a.Str(1), "vehicle_load_magic");
            return 0;
        }

        static int UnitExitVehicle(HsArgs a)
        {
            int h = a.Obj(0);
            if (HaloScriptWorld.IsPlayer(h)) { if (HaloVehicleSeat.Local.Seated) HaloVehicleSeat.Local.GetOut("script"); return 0; }
            var u = HaloScriptWorld.Unit(h);
            if (u != null) { UnseatUnit(u, null); return 0; }
            // a carried vehicle (a30 "(unit_exit_vehicle jeep)" under the pelican, b40's ghosts under the dropship): released, it drops
            var cargo = HaloScriptWorld.Vehicle(h);
            if (cargo != null && cargo.CarriedBy != null) cargo.CarriedBy.UnseatVehicle(cargo);
            else if (cargo == null) Once("unit_exit_vehicle what " + h, "Script: unit_exit_vehicle: object " + h + " is not a unit or vehicle");
            return 0;
        }

        static int VehicleUnload(HsArgs a)
        {
            var v = HaloScriptWorld.Vehicle(a.Obj(0));
            string part = a.Str(1);
            for (int i = Seated.Count - 1; i >= 0; i--) if (UnitIn(Seated[i], v, part)) UnseatUnit(Seated[i], v);
            if (v != null)
                for (int i = 0; i < v.Seats.Count; i++)
                {
                    var s = v.Seats[i];
                    if (s.Cargo == null) continue;
                    if (string.IsNullOrEmpty(part) || (s.Label ?? "").IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) v.UnseatVehicle(s.Cargo);
                }
            if (!LocalIn(v)) return 0;
            string label = HaloVehicleSeat.Local.Seat != null ? HaloVehicleSeat.Local.Seat.Label ?? "" : "";
            if (string.IsNullOrEmpty(part) || label.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) HaloVehicleSeat.Local.GetOut("script");
            return 0;
        }

        static int VehicleRiders(HsArgs a)
        {
            int l = NewList();
            var v = HaloScriptWorld.Vehicle(a.Obj(0));
            if (LocalIn(v)) Add(l, HaloScriptWorld.PlayerBase);
            for (int i = 0; i < Seated.Count; i++) if (UnitIn(Seated[i], v, null)) Add(l, HaloScriptWorld.Handle(Seated[i].gameObject));
            return l;
        }

        static int VehicleTestSeatList(HsArgs a)
        {
            var v = HaloScriptWorld.Vehicle(a.Obj(0));
            if (v == null) return 0;
            int l = ListOf(a, 2), n = Count(l);
            string part = a.Str(1);
            for (int i = 0; i < n; i++)
            {
                int h = At(l, i);
                if (HaloScriptWorld.IsPlayer(h))
                {
                    if (!LocalIn(v)) continue;
                    string label = HaloVehicleSeat.Local.Seat != null ? HaloVehicleSeat.Local.Seat.Label ?? "" : "";
                    if (string.IsNullOrEmpty(part) || label.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) return 1;
                }
                else if (UnitIn(HaloScriptWorld.Unit(h), v, part)) return 1;
            }
            return 0;
        }
    }
}

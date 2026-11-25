namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] device_*: HaloDevices (K) by scenario object name / device group name.</summary>
    public static partial class HaloScriptFns
    {
        static void BindDevices(HsVm vm)
        {
            vm.Bind("device_set_position", DeviceSetPosition);
            vm.Bind("device_get_position", DeviceGetPosition);
            vm.Bind("device_set_position_immediate", DeviceSetPositionImmediate);
            vm.Bind("device_set_power", DeviceSetPower);
            vm.Bind("device_get_power", DeviceGetPower);
            vm.Bind("device_group_get", DeviceGroupGet);
            vm.Bind("device_group_set", DeviceGroupSet);
            vm.Bind("device_group_set_immediate", DeviceGroupSetImmediate);
            vm.Bind("device_group_change_only_once_more_set", DeviceGroupOnceMore);
            vm.Bind("device_one_sided_set", DeviceOneSided);
            vm.Bind("device_operates_automatically_set", DeviceAutomatic);
            vm.Bind("device_set_never_appears_locked", DeviceNeverLocked);
            vm.Bind("device_closes_automatically_set", DeviceClosesAutomatically);
        }

        static int DeviceSetPosition(HsArgs a) => B(HaloDevices.SetPosition(NameArg(a, 0), a.Real(1)));
        static int DeviceGetPosition(HsArgs a) => R(HaloDevices.Position(NameArg(a, 0)));
        static int DeviceSetPositionImmediate(HsArgs a) { HaloDevices.SetPositionImmediate(NameArg(a, 0), a.Real(1)); return 0; }
        static int DeviceSetPower(HsArgs a) { HaloDevices.SetPower(NameArg(a, 0), a.Real(1)); return 0; }
        static int DeviceGetPower(HsArgs a) => R(HaloDevices.Power(NameArg(a, 0)));
        static int DeviceGroupGet(HsArgs a) => R(HaloDevices.GroupGet(a.Str(0)));
        static int DeviceGroupSet(HsArgs a) { HaloDevices.GroupSet(a.Str(0), a.Real(1)); return 0; }
        static int DeviceGroupSetImmediate(HsArgs a) { HaloDevices.GroupSetImmediate(a.Str(0), a.Real(1)); return 0; }
        static int DeviceGroupOnceMore(HsArgs a) { HaloDevices.GroupChangeOnlyOnceMoreSet(a.Str(0), a.Bool(1)); return 0; }
        static int DeviceOneSided(HsArgs a) { HaloDevices.SetOneSided(NameArg(a, 0), a.Bool(1)); return 0; }
        static int DeviceAutomatic(HsArgs a) { HaloDevices.SetOperatesAutomatically(NameArg(a, 0), a.Bool(1)); return 0; }
        static int DeviceNeverLocked(HsArgs a) { HaloDevices.SetNeverAppearsLocked(NameArg(a, 0), a.Bool(1)); return 0; }
        static int DeviceClosesAutomatically(HsArgs a) { HaloDevices.SetClosesAutomatically(NameArg(a, 0), a.Bool(1)); return 0; }
    }
}

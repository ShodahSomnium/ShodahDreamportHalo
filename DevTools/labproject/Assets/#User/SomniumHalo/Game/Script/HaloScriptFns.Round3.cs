namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] Round 3 of the owners' deliveries: HaloUnit (C) damage / items / emotion / vitality / permutations /
    /// flashlight / weapon / grenades, the player's flashlight + night vision (G), breakable surfaces (W), attack inhibit (I).</summary>
    public static partial class HaloScriptFns
    {
        static void BindRound3(HsVm vm)
        {
            vm.Bind("object_cannot_take_damage", a => { SetInvulnerable(a, true); return 0; });
            vm.Bind("object_can_take_damage", a => { SetInvulnerable(a, false); return 0; });
            vm.Bind("unit_impervious", a => { SetInvulnerable(a, a.Bool(1)); return 0; });
            vm.Bind("unit_doesnt_drop_items", a => { int l = ListOf(a, 0), n = Count(l); for (int i = 0; i < n; i++) { var u = HaloScriptWorld.Unit(At(l, i)); if (u != null) u.DropsItems = false; } return 0; });
            vm.Bind("unit_set_emotion", a => { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null) u.SetEmotion(a.Int(1)); return 0; });
            vm.Bind("unit_set_maximum_vitality", a => { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null) u.SetMaximumVitality(a.Real(1), a.Real(2)); return 0; });
            vm.Bind("object_set_permutation", a => { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null) u.SetPermutation(a.Str(1), a.Str(2)); else Once("perm " + a.Str(1), "Script: object_set_permutation on a non-unit (" + HaloScriptWorld.NameOf(a.Obj(0)) + ") - only units change permutations"); return 0; });
            vm.Bind("units_set_desired_flashlight_state", a =>
            {
                int l = ListOf(a, 0), n = Count(l); bool on = a.Bool(1);
                for (int i = 0; i < n; i++)
                {
                    int h = At(l, i);
                    if (HaloScriptWorld.IsPlayer(h)) HaloCombat.FlashlightOn = on;
                    else { var u = HaloScriptWorld.Unit(h); if (u != null) u.FlashlightOn = on; }
                }
                return 0;
            });
            vm.Bind("unit_has_weapon_readied", a =>
            {
                int h = a.Obj(0); var w = a.Tag(1);
                if (HaloScriptWorld.IsPlayer(h)) { var hw = HaloLocal.HeldWeapon; return B(HaloLocal.InHand && hw != null && (w == null || hw.Tag == w)); }
                var u = HaloScriptWorld.Unit(h);
                return B(u != null && u.WeaponReadied && (w == null || u.Weapon == w));
            });
            vm.Bind("unit_get_total_grenade_count", a =>
            {
                int h = a.Obj(0);
                if (HaloScriptWorld.IsPlayer(h)) return HaloLocal.FragGrenades + HaloLocal.PlasmaGrenades;
                var u = HaloScriptWorld.Unit(h); return u != null ? u.TotalGrenades : 0;
            });
            vm.Bind("unit_solo_player_integrated_night_vision_is_active", a => B(HaloCombat.NightVisionAvailable));
            vm.Bind("breakable_surfaces_enable", a => { HaloWorld.BreakableSurfacesEnable(a.Bool(0)); return 0; });
            vm.Bind("breakable_surfaces_reset", a => { HaloWorld.BreakableSurfacesReset(); return 0; });
            vm.Bind("object_set_melee_attack_inhibited", a => { HaloAi.SetMeleeInhibited(HaloScriptWorld.Get(a.Obj(0)), a.Bool(1)); return 0; });
            vm.Bind("object_set_ranged_attack_inhibited", a => { HaloAi.SetRangedInhibited(HaloScriptWorld.Get(a.Obj(0)), a.Bool(1)); return 0; });
        }

        static void SetInvulnerable(HsArgs a, bool on)
        {
            int l = ListOf(a, 0), n = Count(l);
            for (int i = 0; i < n; i++) { var u = HaloScriptWorld.Unit(At(l, i)); if (u != null) u.Invulnerable = on; }
        }
    }
}

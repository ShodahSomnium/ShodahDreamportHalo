namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] player_effect_*: Halo's scripted screen shake + rumble. VR comfort: the view is never shaken; the
    /// rumble goes to the controllers (HaloXr haptics) with the script's own left / right strengths and attack / decay times.</summary>
    public static partial class HaloScriptFns
    {
        struct PlayerEffect { public float Left, Right, Target, Level, Step; public bool On; }
        static PlayerEffect _effect;

        static void BindPlayer(HsVm vm)
        {
            vm.Bind("player_effect_set_max_translation", PlayerEffectIgnore);
            vm.Bind("player_effect_set_max_rotation", PlayerEffectIgnore);
            vm.Bind("player_effect_set_max_rumble", PlayerEffectRumble);
            vm.Bind("player_effect_start", PlayerEffectStart);
            vm.Bind("player_effect_stop", PlayerEffectStop);
            // Combat (G)
            vm.Bind("player_enable_input", a => { HaloCombat.InputEnabled = a.Bool(0); return 0; });
            vm.Bind("players_unzoom_all", a => { HaloCombat.Unzoom(); return 0; });
            vm.Bind("damage_object", DamageObject);
            vm.Bind("damage_new", DamageNew);
            vm.Bind("player_add_equipment", PlayerAddEquipment);
        }

        static int DamageObject(HsArgs a)
        {
            var tag = a.Tag(0); int h = a.Obj(1);
            if (tag == null || h == -1) return 0;
            HaloCombat.ApplyDamage(tag, HaloScriptWorld.IsPlayer(h) ? null : HaloScriptWorld.Get(h));   // null = the local player
            return 0;
        }

        static int DamageNew(HsArgs a)
        {
            var tag = a.Tag(0);
            UnityEngine.Vector3 p; float yaw;
            if (tag != null && HaloScriptWorld.FlagPose(a.Int(1), out p, out yaw)) HaloCombat.ApplyDamageAt(tag, p);
            return 0;
        }

        /// <summary>player_add_equipment unit profile reset: the scenario's starting profile (+840, 104 B: primary +40, rounds +56/+58,
        /// secondary +60, +76/+78, frag +80, plasma +81) given to the local player.</summary>
        static int PlayerAddEquipment(HsArgs a)
        {
            if (!HaloScriptWorld.IsPlayer(a.Obj(0)) || _vm == null || _vm.D.Scenario == null) return 0;
            int i = a.Int(1);
            var r = _vm.D.Scenario.Root;
            if (i < 0 || i >= r.Count(840)) return 0;
            var e = r.Element(840, i, 104);
            HaloCombat.GiveProfile(e.Ref(40), e.I16(56), e.I16(58), e.Ref(60), e.I16(76), e.I16(78), e.I8(80), e.I8(81), a.Bool(2));
            Once("equip " + i, "Script: player_add_equipment " + a.Str(1));
            return 0;
        }

        // the view shake itself is left out on purpose (VR comfort)
        static int PlayerEffectIgnore(HsArgs a) => 0;
        static int PlayerEffectRumble(HsArgs a) { _effect.Left = a.Real(0); _effect.Right = a.Real(1); return 0; }

        static int PlayerEffectStart(HsArgs a)
        {
            _effect.Target = UnityEngine.Mathf.Clamp01(a.Real(0));
            float attack = a.Real(1) * 30f;   // seconds -> ticks
            _effect.Step = attack > 1f ? (_effect.Target - _effect.Level) / attack : _effect.Target - _effect.Level;
            _effect.On = true;
            return 0;
        }

        static int PlayerEffectStop(HsArgs a)
        {
            float decay = a.Real(0) * 30f;
            _effect.Target = 0f;
            _effect.Step = decay > 1f ? -_effect.Level / decay : -_effect.Level;
            return 0;
        }

        static void StepPlayerEffect()
        {
            if (!_effect.On) return;
            _effect.Level += _effect.Step;
            if (_effect.Step >= 0f && _effect.Level >= _effect.Target || _effect.Step < 0f && _effect.Level <= _effect.Target) { _effect.Level = _effect.Target; _effect.Step = 0f; }
            if (_effect.Level <= 0.001f && _effect.Target <= 0f) { _effect.On = false; return; }
            if (!HaloXr.Present) return;
            float l = _effect.Level * _effect.Left, r = _effect.Level * _effect.Right;
            if (l > 0.02f) HaloXr.Pulse(false, l, 0.05f);
            if (r > 0.02f) HaloXr.Pulse(true, r, 0.05f);
        }
    }
}

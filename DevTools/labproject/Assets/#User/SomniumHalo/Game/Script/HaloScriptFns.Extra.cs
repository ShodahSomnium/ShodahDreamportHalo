namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] Functions the owners delivered after the first pass: audio classes / impulse stop (A), game speed,
    /// scenery animations, the numeric countdown timer (K), the unit stance (C), all-quiet / safe-to-speak (I).</summary>
    public static partial class HaloScriptFns
    {
        static void BindExtra(HsVm vm)
        {
            // A: sound_class_set_gain "class" gain ticks
            vm.Bind("sound_class_set_gain", a => { HaloAudio.SetClassGain(a.Str(0), a.Real(1), a.Int(2) / 30f); HaloNetGame.NoteClassGain(a.Str(0), a.Real(1), a.Int(2) / 30f); return 0; });
            vm.Bind("sound_impulse_stop", a => { var t = a.Tag(0); if (t != null) { HaloAudio.StopSound(t); StartedAt.Remove(t); HaloNetGame.NoteSoundStop(t); } return 0; });
            // K
            vm.Bind("game_speed", a => { HaloCampaign.Speed = a.Real(0); return 0; });
            vm.Bind("scenery_animation_start", a => { HaloObjects.AnimateScenery(NameArg(a, 0), a.Tag(1), a.Str(2), 0); return 0; });
            vm.Bind("scenery_animation_start_at_frame", a => { HaloObjects.AnimateScenery(NameArg(a, 0), a.Tag(1), a.Str(2), a.Int(3)); return 0; });
            vm.Bind("scenery_get_animation_time", a => HaloObjects.AnimationDone(NameArg(a, 0)) ? 0 : 1);   // ticks left: K tells done / not done only
            vm.Bind("numeric_countdown_timer_set", a => { HaloCampaign.CountdownSet(a.Raw(0), a.Bool(1)); return 0; });
            vm.Bind("numeric_countdown_timer_get", a => HaloCampaign.CountdownDigit(a.Int(0)));
            vm.Bind("numeric_countdown_timer_stop", a => { HaloCampaign.CountdownStop(); return 0; });
            vm.Bind("numeric_countdown_timer_restart", a => { HaloCampaign.CountdownRestart(); return 0; });
            // C: unit_set_seat "alert" / "flee" ... = the unit's stance ("" = stand)
            vm.Bind("unit_set_seat", a => { var u = HaloScriptWorld.Unit(a.Obj(0)); if (u != null) { string s = a.Str(1); u.Stance = s.Length > 0 ? s : null; } return 0; });
            // I
            vm.Bind("game_all_quiet", a => B(HaloAi.AllQuiet));
            vm.Bind("game_safe_to_speak", a => B(HaloAi.SafeToSpeak));
        }
    }
}

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] hud_set_help_text / show_hud_help_text / hud_set_objective_text: the scenario's own HUD message texts
    /// (HsHudText) shown through HaloEvents.RaiseMessage (the HUD's message line) until the HUD module has a help-text panel.</summary>
    public static partial class HaloScriptFns
    {
        static string _helpText = "";
        static bool _helpShown;
        /// <summary>The current mission objective text (hud_set_objective_text), e.g. "» Find Captain Keyes on the Bridge".</summary>
        public static string Objective { get; private set; } = "";
        /// <summary>The help text the script shows now ("" = none).</summary>
        public static string HelpText => _helpShown ? _helpText : "";
        const float HelpSeconds = 6f;   // ours: Halo keeps it until show_hud_help_text false; the message line fades on its own

        static void BindHud(HsVm vm)
        {
            vm.Bind("hud_set_help_text", HudSetHelpText);
            vm.Bind("show_hud_help_text", ShowHudHelpText);
            vm.Bind("hud_set_objective_text", HudSetObjectiveText);
            vm.Bind("display_scenario_help", DisplayScenarioHelp);
            // H's script surface (Game/Hud/HaloHud.Script.cs)
            vm.Bind("show_hud", a => { HaloHud.ShowHud(a.Bool(0)); return 0; });
            vm.Bind("hud_show_health", a => { HaloHud.ShowPart("health", a.Bool(0)); return 0; });
            vm.Bind("hud_show_shield", a => { HaloHud.ShowPart("shield", a.Bool(0)); return 0; });
            vm.Bind("hud_show_motion_sensor", a => { HaloHud.ShowPart("motion_sensor", a.Bool(0)); return 0; });
            vm.Bind("hud_show_crosshair", a => { HaloHud.ShowPart("crosshair", a.Bool(0)); return 0; });
            vm.Bind("hud_blink_health", a => { HaloHud.BlinkPart("health", a.Bool(0)); return 0; });
            vm.Bind("hud_blink_shield", a => { HaloHud.BlinkPart("shield", a.Bool(0)); return 0; });
            vm.Bind("hud_blink_motion_sensor", a => { HaloHud.BlinkPart("motion_sensor", a.Bool(0)); return 0; });
            vm.Bind("enable_hud_help_flash", a => { HaloHud.HelpFlash(a.Bool(0)); return 0; });
            vm.Bind("hud_help_flash_restart", a => { HaloHud.HelpFlashRestart(); return 0; });
            vm.Bind("activate_team_nav_point_flag", NavFlag);
            vm.Bind("activate_nav_point_flag", NavFlag);
            vm.Bind("activate_team_nav_point_object", NavObject);
            vm.Bind("activate_nav_point_object", NavObject);
            vm.Bind("deactivate_team_nav_point_flag", a => { HaloHud.ClearNavPoint("flag:" + a.Int(1)); return 0; });
            vm.Bind("deactivate_nav_point_flag", a => { HaloHud.ClearNavPoint("flag:" + a.Int(1)); return 0; });
            vm.Bind("deactivate_team_nav_point_object", a => { HaloHud.ClearNavPoint("obj:" + a.Obj(1)); return 0; });
            vm.Bind("deactivate_nav_point_object", a => { HaloHud.ClearNavPoint("obj:" + a.Obj(1)); return 0; });
            vm.Bind("show_hud_timer", a => { HaloHud.TimerShow(a.Bool(0)); return 0; });
            vm.Bind("pause_hud_timer", a => { HaloHud.TimerPause(a.Bool(0)); return 0; });
            vm.Bind("hud_set_timer_time", a => { HaloHud.TimerSet(a.Int(0), a.Int(1)); return 0; });
            vm.Bind("hud_set_timer_warning_time", a => { HaloHud.TimerWarning(a.Int(0), a.Int(1)); return 0; });
            vm.Bind("hud_set_timer_position", a => { HaloHud.TimerPosition(a.Int(0), a.Int(1), a.Str(2)); return 0; });
            vm.Bind("hud_get_timer_ticks", a => HaloHud.TimerTicks);
        }

        /// <summary>(de)activate_[team_]nav_point_flag type team|unit flag offset: keyed by the flag (one waypoint per flag).</summary>
        static int NavFlag(HsArgs a)
        {
            UnityEngine.Vector3 p; float yaw;
            if (HaloScriptWorld.FlagPose(a.Int(2), out p, out yaw)) HaloHud.NavPoint("flag:" + a.Int(2), a.Str(0), p, null, a.Real(3));
            return 0;
        }

        static int NavObject(HsArgs a)
        {
            int h = a.Obj(2);
            var g = HaloScriptWorld.Get(h);
            if (g != null) HaloHud.NavPoint("obj:" + h, a.Str(0), g.transform.position, g.transform, a.Real(3));
            return 0;
        }

        static int HudSetHelpText(HsArgs a)
        {
            _helpText = _hud != null ? _hud.Get(a.Str(0)) : "";
            if (_helpShown && _helpText.Length > 0) HaloEvents.RaiseMessage(_helpText, HelpSeconds);
            return 0;
        }

        static int ShowHudHelpText(HsArgs a)
        {
            bool on = a.Bool(0);
            if (on && !_helpShown && _helpText.Length > 0) HaloEvents.RaiseMessage(_helpText, HelpSeconds);
            _helpShown = on;
            return 0;
        }

        static int DisplayScenarioHelp(HsArgs a)
        {
            string t = HsHudText.ScenarioHelp(_vm != null ? _vm.D.Scenario : null, a.Int(0));
            if (t.Length > 0) { HaloEvents.RaiseMessage(t, 10f); Once("help " + a.Int(0), "Script: scenario help " + a.Int(0) + ": " + t.Replace('\n', '/')); }
            return 0;
        }

        static int HudSetObjectiveText(HsArgs a)
        {
            string t = _hud != null ? _hud.Get(a.Str(0)) : "";
            if (t.Length == 0) return 0;
            Objective = t;
            Once("obj " + t, "Script: objective \"" + t + "\"");
            return 0;
        }
    }
}

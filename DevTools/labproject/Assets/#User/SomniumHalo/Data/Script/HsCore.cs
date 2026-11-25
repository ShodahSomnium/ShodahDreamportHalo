using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Script module, S] The engine functions that need no game world (lists, casts, random numbers, difficulty, time, print) plus the
    /// ones that have nothing to do in the Dreamport by design (resource prediction, rasterizer / debug switches): bound as no-ops so the
    /// unimplemented list only names real gaps. Shared by the Game host and the dotnet harness.
    /// </summary>
    public static class HsCore
    {
        /// <summary>Functions the Dreamport does not need (prediction / caching / debug / rasterizer tuning): done = nothing to do.</summary>
        public static readonly string[] NoOps =
        {
            "objects_predict", "object_type_predict", "object_beautify", "garbage_collect_now", "cls", "script_recompile", "script_doc", "help",
            "rasterizer_model_ambient_reflection_tint", "rasterizer_lights_reset_for_new_map", "rasterizer_decals_flush", "rasterizer_fps_accumulate",
            "render_lights", "time_code_show", "time_code_start", "time_code_reset", "object_pvs_activate", "object_pvs_set_object",
            "object_pvs_set_camera", "object_pvs_clear", "ai_debug_communication_suppress", "ai_debug_communication_ignore",
            "ai_debug_communication_focus", "debug_sounds_enable", "debug_sounds_distances", "sound_looping_predict", "profile_graph_toggle",
            "game_skip_ticks", "set_gamma", "objects_dump_memory",
        };

        public static int Difficulty = 1;
        public static bool Cooperative;
        /// <summary>print / inspect text (null = drop).</summary>
        public static System.Action<string> Print;

        public static void Bind(HsVm vm)
        {
            foreach (var n in NoOps) vm.Bind(n, NoOp);
            vm.Bind("unit", a => a.Obj(0));
            vm.Bind("list_get", a => a.Vm.ListGet(a.Raw(0), a.Int(1)));
            vm.Bind("list_count", a => a.Vm.ListCount(a.Raw(0)));
            vm.Bind("random_range", a => { int lo = a.Int(0), hi = a.Int(1); return hi > lo ? a.Vm.Rng.Next(lo, hi) : lo; });
            vm.Bind("real_random_range", a => { float lo = a.Real(0), hi = a.Real(1); return HsVm.R(lo + (float)a.Vm.Rng.NextDouble() * (hi - lo)); });
            vm.Bind("game_time", a => a.Vm.Tick);
            vm.Bind("game_difficulty_get", a => Difficulty);
            vm.Bind("game_difficulty_get_real", a => Difficulty);
            vm.Bind("game_is_cooperative", a => Cooperative ? 1 : 0);
            vm.Bind("print", a => { var p = Print; if (p != null) p(a.Str(0)); return 0; });
            vm.Bind("inspect", a => { var p = Print; if (p != null) p("inspect " + a.Str(0) + " = " + a.Raw(0)); return 0; });
        }

        static int NoOp(HsArgs a) => 0;

        public static bool IsNoOp(string name) { foreach (var n in NoOps) if (n == name) return true; return false; }
    }
}

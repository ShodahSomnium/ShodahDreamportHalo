using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] cinematic_*, camera_*, fade_*, recording_*: HaloCinematic (Q).</summary>
    public static partial class HaloScriptFns
    {
        static void BindCinematic(HsVm vm)
        {
            vm.Bind("cinematic_start", CinematicStart);
            vm.Bind("cinematic_stop", CinematicStop);
            vm.Bind("camera_control", CameraControl);
            vm.Bind("camera_set", CameraSet);
            vm.Bind("cinematic_show_letterbox", ShowLetterbox);
            vm.Bind("fade_in", FadeIn);
            vm.Bind("fade_out", FadeOut);
            vm.Bind("cinematic_set_title", SetTitle);
            vm.Bind("recording_play", RecordingPlay);
            vm.Bind("recording_play_and_delete", RecordingPlayAndDelete);
            vm.Bind("recording_play_and_hover", RecordingPlayAndHover);
            vm.Bind("recording_kill", RecordingKill);
            vm.Bind("recording_time", RecordingTime);
            vm.Bind("camera_time", CameraTime);
            vm.Bind("camera_set_relative", CameraSetRelative);
            vm.Bind("camera_set_first_person", CameraSetFirstPerson);
            vm.Bind("camera_set_dead", CameraSetDead);
            vm.Bind("cinematic_skip_start_internal", SkipStart);
            vm.Bind("cinematic_skip_stop_internal", SkipStop);
            vm.Bind("cinematic_set_near_clip_distance", NearClip);
            vm.Bind("cinematic_suppress_bsp_object_creation", SuppressBspObjects);
            vm.Bind("cinematic_set_title_delayed", SetTitleDelayed);
            vm.Bind("cinematic_abort", a => { HaloCinematic.End(); HaloCinematic.CameraControl(false); return 0; });
            vm.Bind("cinematic_screen_effect_start", a => { HaloCinematic.ScreenEffectStart(a.Bool(0)); return 0; });
            vm.Bind("cinematic_screen_effect_stop", a => { HaloCinematic.ScreenEffectStop(); return 0; });
            vm.Bind("cinematic_screen_effect_set_convolution", a => { HaloCinematic.ScreenEffectConvolution(a.Int(0), a.Int(1), a.Real(2), a.Real(3), a.Real(4)); return 0; });
            vm.Bind("cinematic_screen_effect_set_filter", a => { HaloCinematic.ScreenEffectFilter(a.Real(0), a.Real(1), a.Real(2), a.Real(3), a.Bool(4), a.Real(5)); return 0; });
            vm.Bind("cinematic_screen_effect_set_filter_desaturation_tint", a => { HaloCinematic.ScreenEffectDesaturationTint(a.Real(0), a.Real(1), a.Real(2)); return 0; });
            vm.Bind("cinematic_screen_effect_set_video", a => { HaloCinematic.ScreenEffectVideo(a.Int(0), a.Real(1)); return 0; });
        }

        static int CinematicStart(HsArgs a) { HaloCinematic.Begin(); return 0; }
        static int CinematicStop(HsArgs a) { HaloCinematic.End(); return 0; }
        static int CameraControl(HsArgs a) { HaloCinematic.CameraControl(a.Bool(0)); return 0; }
        static int CameraSet(HsArgs a) { HaloCinematic.CameraSet(a.Int(0), a.Int(1)); return 0; }   // the camera point by its scenario index
        static int ShowLetterbox(HsArgs a) { HaloCinematic.Letterbox(a.Bool(0)); return 0; }
        static int FadeIn(HsArgs a) { HaloCinematic.Fade(true, new Color(a.Real(0), a.Real(1), a.Real(2), 1f), a.Int(3)); return 0; }
        static int FadeOut(HsArgs a) { HaloCinematic.Fade(false, new Color(a.Real(0), a.Real(1), a.Real(2), 1f), a.Int(3)); return 0; }
        static int SetTitle(HsArgs a) { HaloCinematic.Title(a.Str(0)); return 0; }

        static int CameraTime(HsArgs a) => HaloCinematic.CameraTime;
        static int CameraSetRelative(HsArgs a) { HaloCinematic.CameraSetRelative(a.Str(0), a.Int(1), HaloScriptWorld.Get(a.Obj(2))); return 0; }
        static int CameraSetFirstPerson(HsArgs a) { HaloCinematic.CameraSetFirstPerson(HaloScriptWorld.Get(a.Obj(0))); return 0; }
        static int CameraSetDead(HsArgs a) { HaloCinematic.CameraSetDead(HaloScriptWorld.Get(a.Obj(0))); return 0; }
        static int SkipStart(HsArgs a) { HaloCinematic.SkipStartInternal(); return 0; }
        static int SkipStop(HsArgs a) { HaloCinematic.SkipStopInternal(); return 0; }
        static int NearClip(HsArgs a) { HaloCinematic.SetNearClip(a.Real(0)); return 0; }
        static int SuppressBspObjects(HsArgs a) { HaloCinematic.SuppressBspObjects(a.Bool(0)); return 0; }
        static int SetTitleDelayed(HsArgs a) { HaloCinematic.TitleDelayed(a.Str(0), a.Real(1)); return 0; }

        /// <summary>recording_play* unit recording: true when it started (bipeds and vehicles: the Cinematics module drives them).</summary>
        static int Recording(HsArgs a, HaloCinematic.RecordingEnd end)
        {
            int h = a.Obj(0);
            var g = HaloScriptWorld.IsPlayer(h) ? null : HaloScriptWorld.Get(h);
            if (g == null)
            {
                Once("rec none " + a.Str(1), "Script: recording " + a.Str(1) + " on '" + HaloScriptWorld.NameOf(h) + "' - no such live object");
                return 0;
            }
            string name = HaloScriptWorld.NameOf(h);
            return B(HaloCinematic.PlayRecording(g, a.Str(1), end, name.Length > 0 ? name : null));
        }

        static int RecordingPlay(HsArgs a) => Recording(a, HaloCinematic.RecordingEnd.Stop);
        static int RecordingPlayAndDelete(HsArgs a) => Recording(a, HaloCinematic.RecordingEnd.Delete);
        static int RecordingPlayAndHover(HsArgs a) => Recording(a, HaloCinematic.RecordingEnd.Hover);
        static int RecordingKill(HsArgs a) { var g = HaloScriptWorld.Get(a.Obj(0)); if (g != null) HaloCinematic.KillRecording(g); return 0; }
        static int RecordingTime(HsArgs a) { var g = HaloScriptWorld.Get(a.Obj(0)); return g != null ? HaloCinematic.RecordingTime(g) : 0; }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] The SETTINGS page's rows. The shared ones (volume, menu music, vibration, left-handed) are built in; every other module
    /// adds its own options here at its start (the values live in HaloSettings under the module's own key):
    ///   HaloSettingsUi.Toggle("combat.assist", "ASSISTED RELOAD", false);
    ///   HaloSettingsUi.Stepper("hud.scale", "HUD SIZE", 1f, 0.5f, 2f, 0.1f, "0 %", 100f);
    ///   HaloSettingsUi.Choice("vr.turn", "TURNING", new[] { "SMOOTH", "SNAP 30" }, 0);
    /// A row added after the pages were built shows the next time a page is built or <see cref="Version"/> is noticed (the pages rebuild their
    /// settings tab when it changes). A key registered twice keeps the first row.
    /// </summary>
    public static class HaloSettingsUi
    {
        public enum Kind { Toggle, Stepper, Choice }

        public sealed class Row
        {
            public string Key, Label, Module;
            public Kind Kind;
            public bool FlagDefault;
            public float NumDefault, Min, Max, Step, Show = 1f;
            public string Format;
            public string[] Options;

            public string Value()
            {
                switch (Kind)
                {
                    case Kind.Toggle: return HaloSettings.Flag(Key, FlagDefault) ? "ON" : "OFF";
                    case Kind.Choice:
                    {
                        int i = Mathf.Clamp(HaloSettings.Int(Key, (int)NumDefault), 0, Options.Length - 1);
                        return Options[i];
                    }
                    default:
                        return (HaloSettings.Num(Key, NumDefault) * Show).ToString(string.IsNullOrEmpty(Format) ? "0.##" : Format, System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            public void Change(int d)
            {
                switch (Kind)
                {
                    case Kind.Toggle: HaloSettings.SetFlag(Key, !HaloSettings.Flag(Key, FlagDefault)); break;
                    case Kind.Choice:
                    {
                        int n = Options.Length, i = Mathf.Clamp(HaloSettings.Int(Key, (int)NumDefault), 0, n - 1);
                        HaloSettings.SetInt(Key, (i + d + n) % n);
                        break;
                    }
                    default:
                    {
                        float v = Mathf.Clamp(HaloSettings.Num(Key, NumDefault) + d * Step, Min, Max);
                        HaloSettings.SetNum(Key, Mathf.Round(v / Step) * Step);
                        break;
                    }
                }
            }
        }

        private static readonly List<Row> _rows = new List<Row>();
        public static int Version { get; private set; }
        public static List<Row> Rows { get { return _rows; } }

        static HaloSettingsUi()
        {
            // the shared choices (HaloSettings): Halo's own MASTER VOLUME 0-10 (audio_option_labels / var_volumes)
            Stepper("volume", "MASTER VOLUME:", 1f, 0f, 1f, 0.1f, "0", 10f);
            Toggle("front.music", "MENU MUSIC:", true);
            Toggle("haptics", "CONTROLLER VIBRATION:", true);
            Toggle("lefthanded", "LEFT-HANDED:", false);
            Stepper("audio.music", "MUSIC VOLUME:", 1f, 0f, 1f, 0.1f, "0", 10f);
            // other modules' keys they documented (their defaults; a module registering the same key itself is ignored: the first row stays)
            Toggle("combat.assisted", "ASSISTED RELOAD:", false);
            Stepper("combat.recoil", "RECOIL:", 1f, 0f, 2f, 0.25f, "0 '%'", 100f);
            Stepper("combat.grippitch", "GUN ANGLE IN HAND:", 0f, -30f, 30f, 5f, "0", 1f);
            Stepper("combat.gunscale", "GUN SIZE:", 1f, 0.5f, 1.5f, 0.05f, "0 '%'", 100f);
            Choice("vehicles.vrsteer", "VR STEERING:", new[] { "STICK", "LOOK" }, 0);
            Toggle("vehicles.vrthird", "VR VEHICLE 3RD PERSON:", true);
            Stepper("vehicles.mouse", "VEHICLE MOUSE SPEED:", 2.2f, 0.4f, 6f, 0.2f, "0.0", 1f);
        }

        public static void Toggle(string key, string label, bool fallback)
        {
            Add(new Row { Key = key, Label = label, Kind = Kind.Toggle, FlagDefault = fallback });
        }

        /// <summary>A number from <paramref name="min"/> to <paramref name="max"/> in steps; shown as value x <paramref name="show"/> with the .NET format <paramref name="format"/>.</summary>
        public static void Stepper(string key, string label, float fallback, float min, float max, float step, string format, float show = 1f)
        {
            Add(new Row { Key = key, Label = label, Kind = Kind.Stepper, NumDefault = fallback, Min = min, Max = max, Step = Mathf.Max(1e-4f, step), Format = format, Show = show });
        }

        public static void Choice(string key, string label, string[] options, int fallback)
        {
            if (options == null || options.Length == 0) return;
            Add(new Row { Key = key, Label = label, Kind = Kind.Choice, Options = options, NumDefault = fallback });
        }

        private static void Add(Row r)
        {
            if (string.IsNullOrEmpty(r.Key)) return;
            foreach (Row o in _rows) if (string.Equals(o.Key, r.Key, StringComparison.OrdinalIgnoreCase)) return;
            int dot = r.Key.IndexOf('.');
            r.Module = dot > 0 ? r.Key.Substring(0, dot) : "";
            _rows.Add(r);
            Version++;
        }
    }
}

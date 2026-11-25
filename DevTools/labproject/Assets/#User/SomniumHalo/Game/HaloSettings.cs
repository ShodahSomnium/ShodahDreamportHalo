using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The player's saved choices, kept on this computer (Application.persistentDataPath/HaloDreamport/settings.txt) as key=value lines.
    /// Every module reads and writes its own keys through <see cref="Num"/>, <see cref="Flag"/>, <see cref="Text"/> and their setters (no
    /// central list to edit), and listens to <see cref="Changed"/> to apply them. Saving is debounced (one write a second at most).
    /// </summary>
    public static class HaloSettings
    {
        static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static bool _loaded, _dirty;
        static float _saveAt;

        /// <summary>Raised (main thread) after any setter changed a value; the argument is the key.</summary>
        public static event Action<string> Changed;

        public static string Folder
        {
            get
            {
                try { return Path.Combine(Application.persistentDataPath, "HaloDreamport"); }
                catch (Exception) { return null; }
            }
        }

        /// <summary>Where derived data that is slow to read out of the game files is kept between runs.</summary>
        public static string CacheFolder { get { string f = Folder; return f != null ? Path.Combine(f, "cache") : null; } }

        static string FilePath { get { string f = Folder; return f != null ? Path.Combine(f, "settings.txt") : null; } }

        // ---- shared choices (modules add their own keys freely)
        public static string InstallOverride { get { return Text("install", ""); } set { SetText("install", value ?? ""); } }
        public static float Volume { get { return Num("volume", 1f); } set { SetNum("volume", value); } }
        public static bool Haptics { get { return Flag("haptics", true); } set { SetFlag("haptics", value); } }
        public static bool LeftHanded { get { return Flag("lefthanded", false); } set { SetFlag("lefthanded", value); } }

        public static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                string p = FilePath;
                if (p == null || !File.Exists(p)) return;
                foreach (string line in File.ReadAllLines(p))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) Values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception e)
            {
                HaloLog.Warn("Settings: could not read the saved choices: " + e.Message);
            }
        }

        public static string Text(string key, string fallback)
        {
            Load();
            string v;
            return Values.TryGetValue(key, out v) ? v : fallback;
        }

        public static float Num(string key, float fallback)
        {
            float r;
            string v = Text(key, null);
            return v != null && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out r) ? r : fallback;
        }

        public static int Int(string key, int fallback) { return Mathf.RoundToInt(Num(key, fallback)); }
        public static bool Flag(string key, bool fallback) { return Num(key, fallback ? 1f : 0f) > 0.5f; }

        public static void SetText(string key, string value)
        {
            Load();
            value = (value ?? "").Replace('\n', ' ').Replace('\r', ' ');
            string old;
            if (Values.TryGetValue(key, out old) && old == value) return;
            Values[key] = value;
            _dirty = true;
            _saveAt = Time.unscaledTime + 1f;
            try { var h = Changed; if (h != null) h(key); }
            catch (Exception e) { HaloLog.Warn("Settings: a listener of '" + key + "' failed: " + e.Message); }
        }

        public static void SetNum(string key, float value) { SetText(key, value.ToString("0.####", CultureInfo.InvariantCulture)); }
        public static void SetInt(string key, int value) { SetText(key, value.ToString(CultureInfo.InvariantCulture)); }
        public static void SetFlag(string key, bool value) { SetText(key, value ? "1" : "0"); }

        /// <summary>Called every frame by the front end: writes the file a second after the last change.</summary>
        public static void Tick()
        {
            if (_dirty && Time.unscaledTime >= _saveAt) Save();
        }

        public static void Save()
        {
            _dirty = false;
            try
            {
                string p = FilePath;
                if (p == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                var l = new List<string>();
                foreach (var kv in Values) l.Add(kv.Key + "=" + kv.Value);
                l.Sort(StringComparer.OrdinalIgnoreCase);
                File.WriteAllLines(p, l.ToArray());
            }
            catch (Exception e)
            {
                HaloLog.Warn("Settings: could not save the choices: " + e.Message);
            }
        }
    }
}

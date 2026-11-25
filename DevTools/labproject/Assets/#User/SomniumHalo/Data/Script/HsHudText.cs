using System.Collections.Generic;
using System.Text;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Script module, S] The scenario's HUD messages (scenario +1428 hud_message_text: UTF-16 text blob +0, elements +20 (2 B: type,
    /// data), messages +32 (64 B: name, text start +32, element start +34, panel count +36)). A message is its elements up to the next
    /// message's first element: type 0 = text (data = characters incl. the terminating 0), type 1 = button icon (data = icon index).
    /// "|n" is a line break. Used by hud_set_help_text / hud_set_objective_text / display_scenario_help. Unity-free.
    /// </summary>
    public sealed class HsHudText
    {
        /// <summary>Button icons by index (Halo's hud message icon order; the words are ours for a VR / keyboard player).</summary>
        public static readonly string[] Icons =
        {
            "Accept", "Back", "X", "Y", "Black", "White", "Left Trigger", "Right Trigger", "Up", "Down", "Left", "Right", "Start", "Back",
            "Left Stick", "Right Stick", "Left Stick", "Right Stick", "Action", "Throw Grenade", "Fire", "Flashlight", "Jump", "Melee",
            "Switch Weapon", "Switch Grenade", "Zoom", "Crouch", "Accept", "Back", "Move", "Look",
        };

        readonly Dictionary<string, string> _byName = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        readonly List<string> _names = new List<string>();
        public int Count => _names.Count;
        public string NameAt(int i) => i >= 0 && i < _names.Count ? _names[i] : "";

        public static HsHudText Read(HaloTag scenario)
        {
            var h = new HsHudText();
            if (scenario == null) return h;
            var t = scenario.Root.Ref(1428);
            if (t == null) return h;
            var r = t.Root;
            byte[] text = r.DataBytes(0);
            int ne = r.Count(20);
            if (ne < 0 || ne > 100000) ne = 0;
            var type = new int[ne]; var data = new int[ne];
            int k = 0;
            foreach (var e in r.Block(20, 2)) { type[k] = e.I8(0); data[k] = e.U8(1); k++; }
            int nm = r.Count(32);
            if (nm <= 0 || nm > 100000) return h;
            var starts = new int[nm]; var elems = new int[nm]; var names = new string[nm];
            k = 0;
            foreach (var m in r.Block(32, 64)) { names[k] = m.Str32(0); starts[k] = m.I16(32); elems[k] = m.I16(34); k++; }
            var sb = new StringBuilder();
            for (int i = 0; i < nm; i++)
            {
                sb.Length = 0;
                int end = i + 1 < nm ? elems[i + 1] : ne;
                if (end < elems[i]) end = ne;
                int c = starts[i];
                for (int e = elems[i]; e >= 0 && e < end && e < ne; e++)
                {
                    if (type[e] == 0)
                    {
                        for (int j = 0; j < data[e]; j++)
                        {
                            int at = (c + j) * 2;
                            if (at + 1 >= text.Length) break;
                            char ch = (char)(text[at] | text[at + 1] << 8);
                            if (ch != 0) sb.Append(ch);
                        }
                        c += data[e];
                    }
                    else if (type[e] == 1) sb.Append('[').Append(data[e] < Icons.Length ? Icons[data[e]] : "button " + data[e]).Append(']');
                }
                string s = sb.ToString().Replace("|n", "\n").Trim();
                h._names.Add(names[i]);
                if (!h._byName.ContainsKey(names[i])) h._byName[names[i]] = s;
            }
            return h;
        }

        /// <summary>display_scenario_help: string <paramref name="index"/> of the scenario's "ingame help text" (+1412, unicode_string_list:
        /// strings +0, 20-byte data refs of UTF-16).</summary>
        public static string ScenarioHelp(HaloTag scenario, int index)
        {
            if (scenario == null) return "";
            var t = scenario.Root.Ref(1412);
            if (t == null || index < 0 || index >= t.Root.Count(0)) return "";
            byte[] b = t.Root.Element(0, index, 20).DataBytes(0);
            var sb = new StringBuilder();
            for (int i = 0; i + 1 < b.Length; i += 2) { char c = (char)(b[i] | b[i + 1] << 8); if (c == 0) break; sb.Append(c); }
            return sb.ToString().Replace("|n", "\n").Trim();
        }

        /// <summary>The text of a message by its name ("" when missing).</summary>
        public string Get(string name) { string s; return name != null && _byName.TryGetValue(name, out s) ? s : ""; }
    }
}

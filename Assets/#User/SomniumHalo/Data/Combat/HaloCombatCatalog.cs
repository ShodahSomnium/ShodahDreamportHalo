using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Everything combat reads from one map, parsed once and cached: weapons, projectiles, damage effects, equipment, item collections,
    /// the globals' grenades (maximum count, multiplayer spawn count, projectile, equipment), the multiplayer unit (cyborg_mp: vitality,
    /// melee damage, grenade throw speed, starting weapons), falling damage, the HUD item names ("Picked up a pistol" -> "Pistol") and the
    /// HUD prompts ("Hold %action to pick up %item"). Unity-free: the harness checks it (DevTools/agents/G/harness).
    /// </summary>
    public sealed class HaloCombatCatalog
    {
        public sealed class Grenade
        {
            public int MaximumCount, MultiplayerSpawn;
            public HaloTag ThrowingEffect, HudInterface, Equipment, Projectile;
        }

        public readonly HaloMap Map;
        public readonly List<Grenade> Grenades = new List<Grenade>();
        public readonly HaloTag MultiplayerUnit, Flag, Ball;
        public readonly HaloVitality Vitality;
        /// <summary>The campaign player's unit (globals player information: characters\cyborg\cyborg) and its vitality (its own collision model).</summary>
        public readonly HaloTag PlayerUnit;
        public readonly HaloVitality PlayerVitality;
        public HaloVitality VitalityFor(bool campaign) { return campaign && PlayerVitality != null ? PlayerVitality : Vitality ?? PlayerVitality; }
        public readonly HaloTag UnitMelee;
        public readonly float GrenadeVelocity;        // world units a second (the unit's "grenade velocity")
        public readonly List<HaloTag> UnitWeapons = new List<HaloTag>();
        public readonly List<HaloTag> WeaponList = new List<HaloTag>();
        public readonly float HarmfulFallingSpeedMin, HarmfulFallingSpeedMax, MaximumFallingSpeed;   // world units a second
        public readonly HaloTag FallingDamage, DistanceDamage, FlamingDeathDamage;
        public readonly HVec3 GrenadeOrigin;
        public readonly List<string> ItemMessages = new List<string>();
        private readonly Dictionary<string, string> _prompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<HaloTag, HaloWeaponDef> _weapons = new Dictionary<HaloTag, HaloWeaponDef>();
        private readonly Dictionary<HaloTag, HaloProjectileDef> _projectiles = new Dictionary<HaloTag, HaloProjectileDef>();
        private readonly Dictionary<HaloTag, HaloDamageDef> _damage = new Dictionary<HaloTag, HaloDamageDef>();
        private readonly Dictionary<HaloTag, HaloEquipmentDef> _equipment = new Dictionary<HaloTag, HaloEquipmentDef>();
        private readonly Dictionary<HaloTag, HaloItemCollection> _collections = new Dictionary<HaloTag, HaloItemCollection>();
        private readonly Dictionary<HaloTag, List<HaloTag>> _blast = new Dictionary<HaloTag, List<HaloTag>>();

        public HaloCombatCatalog(HaloMap map)
        {
            Map = map;
            HaloTag globals = null;
            foreach (var t in map.OfClass("matg")) { globals = t; break; }
            if (globals == null) throw new InvalidOperationException("no globals tag in " + map.Name);
            var r = globals.Root;
            foreach (var g in r.Block(296, 68))
                Grenades.Add(new Grenade { MaximumCount = g.I16(0), MultiplayerSpawn = g.I16(2), ThrowingEffect = g.Ref(4), HudInterface = g.Ref(20), Equipment = g.Ref(36), Projectile = g.Ref(52) });
            foreach (var g in r.Block(332, 16)) { var w = g.Ref(0); if (w != null) WeaponList.Add(w); }
            foreach (var g in r.Block(356, 160)) { Flag = g.Ref(0); MultiplayerUnit = g.Ref(16); Ball = g.Ref(76); break; }
            foreach (var g in r.Block(368, 244)) { GrenadeOrigin = g.Vec3(104); PlayerUnit = g.Ref(0); if (MultiplayerUnit == null) MultiplayerUnit = PlayerUnit; break; }
            if (PlayerUnit != null)
            {
                var pc = PlayerUnit.Root.Ref(112);
                if (pc != null) PlayerVitality = new HaloVitality(pc);
            }
            foreach (var g in r.Block(392, 152))
            {
                FallingDamage = g.Ref(16); DistanceDamage = g.Ref(44); FlamingDeathDamage = g.Ref(108);
                MaximumFallingSpeed = g.F32(140) * HaloProjectileDef.TicksPerSecond;
                HarmfulFallingSpeedMin = g.F32(144) * HaloProjectileDef.TicksPerSecond;
                HarmfulFallingSpeedMax = g.F32(148) * HaloProjectileDef.TicksPerSecond;
                break;
            }

            if (MultiplayerUnit != null)
            {
                var u = MultiplayerUnit.Root;
                var coll = u.Ref(112);
                if (coll != null) Vitality = new HaloVitality(coll);
                UnitMelee = u.Ref(648);
                GrenadeVelocity = u.F32(704);
                foreach (var w in u.Block(728, 36)) { var t = w.Ref(0); if (t != null) UnitWeapons.Add(t); }
            }

            ReadStrings(globals);
        }

        // ------------------------------------------------------------------ the parsed tags (cached)

        public HaloWeaponDef Weapon(HaloTag t)
        {
            if (t == null || !t.Is("weap")) return null;
            HaloWeaponDef d;
            if (!_weapons.TryGetValue(t, out d))
            {
                d = new HaloWeaponDef(t);
                d.Name = ItemName(d.PickupTextIndex, t);
                _weapons[t] = d;
            }

            return d;
        }

        public HaloProjectileDef Projectile(HaloTag t)
        {
            if (t == null || !t.Is("proj")) return null;
            HaloProjectileDef d;
            if (!_projectiles.TryGetValue(t, out d)) { d = new HaloProjectileDef(t); _projectiles[t] = d; }
            return d;
        }

        public HaloDamageDef Damage(HaloTag t)
        {
            if (t == null || !t.Is("jpt!")) return null;
            HaloDamageDef d;
            if (!_damage.TryGetValue(t, out d)) { d = new HaloDamageDef(t); _damage[t] = d; }
            return d;
        }

        public HaloEquipmentDef Equipment(HaloTag t)
        {
            if (t == null || !t.Is("eqip")) return null;
            HaloEquipmentDef d;
            if (!_equipment.TryGetValue(t, out d)) { d = new HaloEquipmentDef(t); d.Name = ItemName(d.Kind == HaloEquipmentDef.Powerup.None || d.PickupTextIndex <= 0 ? -1 : d.PickupTextIndex, t); _equipment[t] = d; }
            return d;
        }

        public HaloItemCollection Collection(HaloTag t)
        {
            if (t == null || !t.Is("itmc")) return null;
            HaloItemCollection d;
            if (!_collections.TryGetValue(t, out d)) { d = new HaloItemCollection(t); _collections[t] = d; }
            return d;
        }

        /// <summary>The harmful damage effects of an effect tag's damage parts (a detonation's blast), cached.</summary>
        public List<HaloTag> Blast(HaloTag effect)
        {
            if (effect == null) return null;
            List<HaloTag> l;
            if (!_blast.TryGetValue(effect, out l))
            {
                l = new List<HaloTag>();
                foreach (var t in HaloDamageDef.PartsOf(effect)) { var d = Damage(t); if (d != null && !d.Harmless) l.Add(t); }
                _blast[effect] = l;
            }

            return l;
        }

        /// <summary>The grenade kind (0 frag, 1 plasma) of a projectile tag, -1 when it is not one of the globals' grenades.</summary>
        public int GrenadeKind(HaloTag projectile)
        {
            for (int i = 0; i < Grenades.Count; i++) if (Grenades[i].Projectile == projectile) return i;
            return -1;
        }

        // ------------------------------------------------------------------ names and prompts

        /// <summary>
        /// The game's own name of an item: the HUD item message "Picked up a pistol" at the item's pickup text index, title-cased
        /// ("Pistol"); the tag's file name when there is none.
        /// </summary>
        public string ItemName(int index, HaloTag tag)
        {
            string s = index >= 0 && index < ItemMessages.Count ? ItemMessages[index] : null;
            if (s != null && s.StartsWith("Picked up ", StringComparison.OrdinalIgnoreCase) && s.IndexOf('%') < 0)
            {
                s = s.Substring(10);
                if (s.StartsWith("a ")) s = s.Substring(2);
                else if (s.StartsWith("an ")) s = s.Substring(3);
                else if (s.StartsWith("the ")) s = s.Substring(4);
                return Title(s.Trim());
            }

            return tag != null ? Title(System.IO.Path.GetFileName(tag.Name).Replace('_', ' ')) : "";
        }

        /// <summary>The HUD message "Picked up %d rounds for pistol" for the item after <paramref name="index"/> (the game keeps it at index + 1).</summary>
        public string AmmoMessage(int index, int rounds)
        {
            string s = index + 1 >= 0 && index + 1 < ItemMessages.Count ? ItemMessages[index + 1] : null;
            if (s == null || s.IndexOf("%d", StringComparison.Ordinal) < 0) return null;
            return s.Replace("%d", rounds.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// A HUD prompt of the game's hud message text (hmt) by name ("pickup", "swap_weapon", "swap_powerup"), with the button and the item names
        /// filled in: "Hold [grip] to pick up the Shotgun" style. Null when the map has no such message.
        /// </summary>
        public string Prompt(string name, string button, string item, string item2 = null)
        {
            string p;
            if (!_prompts.TryGetValue(name, out p)) return null;
            return p.Replace("{button}", button ?? "").Replace("{item}", item ?? "").Replace("{item2}", item2 ?? "");
        }

        public IEnumerable<string> PromptNames => _prompts.Keys;

        private static string Title(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool up = true;
            foreach (char c in s)
            {
                sb.Append(up ? char.ToUpperInvariant(c) : c);
                up = c == ' ' || c == '-';
            }

            return sb.ToString();
        }

        private void ReadStrings(HaloTag globals)
        {
            HaloTag hudg = null;
            foreach (var b in globals.Root.Block(320, 304)) { hudg = b.Ref(96); break; }
            HaloTag items = hudg != null ? hudg.Root.Ref(148) : null;
            HaloTag messages = hudg != null ? hudg.Root.Ref(240) : null;
            if (items == null) items = Map.Find("ustr", "ui\\hud\\hud_item_messages");
            if (items != null)
                foreach (var s in items.Root.Block(0, 20))
                {
                    var bytes = s.DataBytes(0);
                    ItemMessages.Add(Encoding.Unicode.GetString(bytes).TrimEnd('\0'));
                }

            if (messages == null) foreach (var t in Map.OfClass("hmt ")) { messages = t; break; }
            if (messages != null) ReadPrompts(messages);
        }

        /// <summary>The hud message text: a UTF-16 text blob, elements (type 0 = a run of N characters, type 1 = a variable), messages (name, first text char, first element).</summary>
        private void ReadPrompts(HaloTag hmt)
        {
            var r = hmt.Root;
            byte[] text = r.DataBytes(0);
            var types = new List<int>(); var datas = new List<int>();
            foreach (var e in r.Block(20, 2)) { types.Add(e.I8(0)); datas.Add(e.U8(1)); }
            var names = new List<string>(); var textStart = new List<int>(); var elemStart = new List<int>();
            foreach (var m in r.Block(32, 64)) { names.Add(m.Str32(0)); textStart.Add(m.I16(32)); elemStart.Add(m.I16(34)); }
            for (int i = 0; i < names.Count; i++)
            {
                int e0 = elemStart[i], e1 = i + 1 < names.Count ? elemStart[i + 1] : types.Count;
                int at = textStart[i] * 2;
                var sb = new StringBuilder();
                for (int e = e0; e < e1 && e < types.Count; e++)
                {
                    if (types[e] == 0)
                    {
                        int n = datas[e] * 2;
                        if (at + n > text.Length) n = Math.Max(0, text.Length - at);
                        sb.Append(Encoding.Unicode.GetString(text, at, n));
                        at += n;
                    }
                    else if (types[e] == 1)
                    {
                        int v = datas[e];
                        sb.Append(v == 32 ? "{item}" : v == 33 ? "{item2}" : "{button}");
                    }
                }

                string p = sb.ToString().Replace("\0", "");
                while (p.Contains("  ")) p = p.Replace("  ", " ");
                _prompts[names[i]] = p.Trim();
            }
        }
    }
}

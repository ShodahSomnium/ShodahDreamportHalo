using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// How fast a biped moves at throttle 1 when a recording drives it: Halo moves AI / scripted bipeds by their move animation's root
    /// motion, so the speed is the frame info (dx, dy per frame) of "&lt;seat&gt; &lt;weapon class&gt; move-front" from the unit's animation
    /// graph, with seat "alert" while the control flags carry force-alert (0x200), "crouch" while crouching, else "stand".
    /// Verified (harness qpath, a10 bridge BSP 1): keyes_walk_1 from flag keyes_deck ends 0.14-0.25 wu from flag keyes_peer (where the
    /// script teleports him next) facing 110 deg (flag 104); the crew walks x10_crew_walk_1/3/4 (force alert, unarmed -> "alert pistol
    /// move-front", 0.0225 wu/tick) stay on the bridge walkways and turn at the aisles; with the "stand" speed (0.075) they walk out of
    /// the bridge window. Results cached per graph. Unity-free.
    /// </summary>
    public static class HaloRecordingSpeed
    {
        static readonly Dictionary<long, Dictionary<string, float>> Cache = new Dictionary<long, Dictionary<string, float>>();
        static readonly Dictionary<uint, HaloAnimGraph> Graphs = new Dictionary<uint, HaloAnimGraph>();

        public static void Clear() { Cache.Clear(); Graphs.Clear(); }

        /// <summary>World units per tick at throttle 1 (0 when the unit has no such animation).</summary>
        public static float WorldUnitsPerTick(HaloTag unitTag, bool alert, bool crouch, string weaponClass)
        {
            if (unitTag == null) return 0f;
            HaloTag antr = unitTag.Root.Ref(56);
            if (antr == null) return 0f;
            int seatIndex = crouch ? 2 : alert ? 1 : 0;
            string seat = crouch ? "crouch" : alert ? "alert" : "stand";
            string cls = string.IsNullOrEmpty(weaponClass) ? "unarmed" : weaponClass;
            long key = (long)antr.Id << 2 | (long)seatIndex;
            Dictionary<string, float> byClass;
            if (!Cache.TryGetValue(key, out byClass)) { byClass = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase); Cache[key] = byClass; }
            float v;
            if (byClass.TryGetValue(cls, out v)) return v;
            v = Compute(antr, seat, cls);
            if (v <= 0f && seat != "stand") v = Compute(antr, "stand", cls);
            byClass[cls] = v;
            return v;
        }

        static float Compute(HaloTag antr, string seat, string cls)
        {
            HaloAnimGraph g;
            if (!Graphs.TryGetValue(antr.Id, out g))
            {
                try { g = new HaloAnimGraph(antr); } catch (Exception) { g = null; }
                Graphs[antr.Id] = g;
            }
            if (g == null) return 0f;
            var st = g.FindSeat(seat);
            if (st == null) return 0f;
            var wc = st.Weapon(cls);
            if (wc == null && st.Weapons.Count > 0) wc = st.Weapons[0];
            if (wc == null) return 0f;
            var an = g.Get(wc.Anim(HaloAnimIds.Weapon.MoveFront));
            if (an == null || an.Frames <= 0) return 0f;
            if (!an.Decode() || an.Info == null) return 0f;
            float sx = 0f, sy = 0f;
            for (int f = 0; f < an.Frames && f * 4 + 1 < an.Info.Length; f++) { sx += an.Info[f * 4]; sy += an.Info[f * 4 + 1]; }
            return (float)Math.Sqrt(sx * sx + sy * sy) / an.Frames;
        }
    }
}

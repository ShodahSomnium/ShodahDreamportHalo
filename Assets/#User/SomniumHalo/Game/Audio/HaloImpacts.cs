using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Object impact and slide sounds from the object's own material effects tag (items +584 e.g. sound\sfx\impulse\material_effects\
    /// weapon.foot, vehicles +976 vehicles\vehicle.foot, bipeds +908): effect index <see cref="Impact"/> (8: a dropped weapon / grenade /
    /// body hitting a surface - impacts_dirt / concrete / metal / grating / glass / water / grass) and <see cref="TireSlip"/> (9: a
    /// vehicle's tires skidding - dirt_skid + the slip effect), per material type of the surface. The impact's strength is Halo's
    /// scale (object impact sounds are silent at scale 0 and full at 1). Call it from the collision of the object (Combat, Characters,
    /// Vehicles own those).
    /// </summary>
    public static class HaloImpacts
    {
        public const int Impact = 8, TireSlip = 9;
        static readonly Dictionary<HaloTag, HaloTag> FootOf = new Dictionary<HaloTag, HaloTag>();
        static readonly Dictionary<HaloTag, float> Next = new Dictionary<HaloTag, float>();
        public static int Played, Silent;

        /// <summary>The material effects tag of an object tag (weapon / equipment / garbage, vehicle, biped), null when none.</summary>
        public static HaloTag MaterialEffectsOf(HaloTag obj)
        {
            if (obj == null) return null;
            if (obj.ClassName == "foot") return obj;
            HaloTag f;
            if (FootOf.TryGetValue(obj, out f)) return f;
            try
            {
                if (obj.Is("item")) f = obj.Root.Ref(584);
                else if (obj.Is("vehi")) f = obj.Root.Ref(976);
                else if (obj.Is("bipd")) f = obj.Root.Ref(908);
            }
            catch (Exception) { f = null; }
            if (f != null && f.ClassName != "foot") f = null;
            if (FootOf.Count > 256) FootOf.Clear();
            FootOf[obj] = f;
            return f;
        }

        /// <summary>Plays effect <paramref name="kind"/> of the object's material effects for the surface under / at
        /// <paramref name="point"/> (<paramref name="material"/> = Halo material type, -1 = look it up with a short ray down).
        /// <paramref name="strength"/> 0..1 (impact speed / a full-speed hit); the same object sounds at most every 0.15 s. // ours</summary>
        public static bool Play(HaloTag obj, int kind, Vector3 point, int material, float strength)
        {
            var foot = MaterialEffectsOf(obj);
            if (foot == null || kind < 0) return false;
            float now = Time.time, n;
            if (Next.TryGetValue(obj, out n) && now < n) return false;
            if (material < 0) { float y; if (!HaloFootsteps.Ground(point + Vector3.up * 0.3f, out y, out material)) material = -1; }
            if (material < 0) material = 2;   // stone // ours
            HaloTag snd = null, fx = null;
            try
            {
                var e = foot.Root;
                if (kind >= e.Count(0)) return false;
                var eff = e.Element(0, kind, 28);
                if (material >= eff.Count(0)) { Silent++; return false; }
                var m = eff.Element(0, material, 48);
                fx = m.Ref(0); snd = m.Ref(16);
            }
            catch (Exception) { return false; }
            if (snd == null && fx == null) { Silent++; return false; }
            if (Next.Count > 512) Next.Clear();
            Next[obj] = now + 0.15f;
            if (snd != null) { Played++; HaloAudio.PlayAt(snd, point, 1f, Mathf.Clamp01(strength), 1f); }
            if (fx != null) { try { HaloFx.Play(fx, point, Quaternion.identity); } catch (Exception) { } }
            return true;
        }
    }
}

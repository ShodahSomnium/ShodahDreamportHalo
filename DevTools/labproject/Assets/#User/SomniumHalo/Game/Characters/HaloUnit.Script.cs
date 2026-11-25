using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// What campaign scripts set on units (bound by the Script module): invulnerability (object_cannot_take_damage / object_can_take_damage /
    /// unit_impervious), unit_doesnt_drop_items, unit_set_emotion (the seat's "emotions" overlay frame), unit_set_maximum_vitality /
    /// unit_set_current_vitality (points), object_set_permutation / object_set_region_state (the model rebuilt with the region's permutation),
    /// units_set_desired_flashlight_state (the biped's flashlight), unit_has_weapon_readied / unit_ready_weapon, grenade counts.
    /// </summary>
    public partial class HaloUnit
    {
        /// <summary>Takes no damage at all (Instant hits - death barriers - and Kill() still kill).</summary>
        public bool Invulnerable;
        /// <summary>False: the weapon and grenades vanish with the death instead of falling (unit_doesnt_drop_items).</summary>
        public bool DropsItems = true;

        private float _maxBodyOverride = -1f, _maxShieldOverride = -1f;

        /// <summary>unit_set_emotion: the seat's emotions overlay frame (0 .. its frames - 1; -1 none).</summary>
        public void SetEmotion(int emotion) { if (Animator != null) Animator.Emotion = emotion; }
        public int Emotion => Animator != null ? Animator.Emotion : -1;

        /// <summary>unit_set_maximum_vitality: new maximum body / shield points (the current fractions are kept).</summary>
        public void SetMaximumVitality(float bodyPoints, float shieldPoints)
        {
            _maxBodyOverride = Mathf.Max(0f, bodyPoints);
            _maxShieldOverride = Mathf.Max(0f, shieldPoints);
            if (_maxShieldOverride > 0f && Shield <= 0f && Alive) Shield = 1f;
            if (_maxShieldOverride <= 0f) Shield = 0f;
        }

        /// <summary>unit_set_current_vitality: current body / shield points (clamped to the maxima; 0 body does not kill - Halo's own).</summary>
        public void SetCurrentVitality(float bodyPoints, float shieldPoints)
        {
            float mb = MaxBody, ms = MaxShield;
            if (mb > 0f) Body = Mathf.Clamp01(bodyPoints / mb);
            Shield = ms > 0f ? Mathf.Max(0f, shieldPoints / ms) : 0f;
        }

        /// <summary>object_set_permutation (region, permutation name) / object_set_region_state: the model rebuilt with it. False when the
        /// region or permutation does not exist (unknown names log once).</summary>
        public bool SetPermutation(string region, string permutation)
        {
            HaloModel m = Def != null ? HaloBipeds.ModelOf(Def.Model) : null;
            if (m == null || string.IsNullOrEmpty(region)) return false;
            foreach (HaloModel.Region r in m.Regions)
            {
                if (!string.Equals(r.Name, region, System.StringComparison.OrdinalIgnoreCase)) continue;
                foreach (HaloModel.Permutation p in r.Permutations)
                {
                    if (!string.Equals(p.Name, permutation, System.StringComparison.OrdinalIgnoreCase)) continue;
                    string now;
                    if (Permutations.TryGetValue(r.Name, out now) && now == p.Name) return true;
                    Permutations[r.Name] = p.Name;
                    BuildBody();
                    return true;
                }

                Once("perm " + Def.Name + region + permutation, "Characters: " + Def.Name + ": region '" + region + "' has no permutation '" + permutation + "'");
                return false;
            }

            Once("perm " + Def.Name + region, "Characters: " + Def.Name + ": no region '" + region + "'");
            return false;
        }

        // ------------------------------------------------------------------ flashlight

        private Light _flashlight;

        /// <summary>units_set_desired_flashlight_state: the light at the biped's "flashlight" marker (the cyborg's; other bipeds have none).</summary>
        public bool FlashlightOn
        {
            get { return _flashlight != null && _flashlight.enabled; }
            set
            {
                if (value && _flashlight == null)
                {
                    Transform m = Marker("flashlight");
                    if (m == null) return;
                    var go = new GameObject("flashlight");
                    go.transform.SetParent(m, false);
                    go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);      // the marker's +X (Halo forward) -> the light's +Z
                    _flashlight = go.AddComponent<Light>();
                    _flashlight.type = LightType.Spot;
                    _flashlight.range = 20f;        // ours
                    _flashlight.spotAngle = 45f;    // ours
                    _flashlight.intensity = 2f;     // ours
                    _flashlight.shadows = LightShadows.None;
                }

                if (_flashlight != null) _flashlight.enabled = value && Alive;
            }
        }

        // ------------------------------------------------------------------ weapon readied, grenades

        /// <summary>unit_has_weapon_readied: a weapon in hand and no ready / put-away / custom animation running.</summary>
        public bool WeaponReadied => Weapon != null && Alive && Animator != null && Animator.Action != HaloUnitAnimator.ActionKind.Ready && !Animator.CustomPlaying;

        /// <summary>unit_ready_weapon: plays the class's ready animation (false when it has none).</summary>
        public bool ReadyWeapon() { bool ok = Alive && Weapon != null && Animator != null && Animator.Ready(); if (ok) PlayReady(); return ok; }

        /// <summary>unit_get_total_grenade_count.</summary>
        public int TotalGrenades => Grenades;
        /// <summary>unit_set_grenade_count (AI units: their one grenade type).</summary>
        public void SetGrenadeCount(int count) { Grenades = Mathf.Max(0, count); }
    }
}

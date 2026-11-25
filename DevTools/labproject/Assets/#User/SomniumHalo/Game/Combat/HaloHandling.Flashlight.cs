using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The player's flashlight and the sniper scope's night vision (Halo CE: one button). The light is the player unit's own attachment on
    /// its "flashlight" marker (characters\cyborg\cyborg -> characters\cyborg\flashlight_cyborg.ligh, primary scale = the integrated light),
    /// started through the Effects module (HaloFx.Attach on a transform at the eye, looking where the head looks; lights scaled 0 / 1), and
    /// the unit's "integrated light toggle" effect (weapons\assault rifle\effects\flashlight: the click) plays on each toggle. While zoomed
    /// with a weapon whose zoom sounds are the sniper's nightscope (Halo: the sniper rifle), the button toggles night vision instead
    /// (HaloLocal.NightVision; the HUD / screen draws it). Keys (ours): monitor L (Halo PC's Q is the grenade switch here, L is not a client
    /// key), VR the stick click of the hand that does NOT hold the gun (the gun hand's stick click cycles the zoom).
    /// </summary>
    public sealed partial class HaloHandling
    {
        private Transform _lamp;
        private HaloFx.Attachments _lampFx;
        private HaloTag _lampUnit;
        private bool _lampOn;

        /// <summary>The flashlight is on (script unit_set_desired_flashlight_state / unit_get_current_flashlight_state for the player).</summary>
        public bool FlashlightOn
        {
            get { return _lampOn; }
            set { if (value != _lampOn) ToggleLight(false); }
        }

        /// <summary>Night vision could be switched on with the flashlight button now (zoomed with a night scope).</summary>
        public bool NightVisionAvailable
        {
            get
            {
                HaloGun g = HeldGun;
                if (g == null || !HaloLocal.Zoomed) return false;
                HaloTag z = g.Def.ZoomInSound;
                return g.Def.Kind == HaloWeaponDef.Family.Sniper || (z != null && z.Name.IndexOf("sniper", StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        private HaloTag PlayerUnitTag()
        {
            var cat = HaloCombat.Catalog;
            if (cat == null) return null;
            return HaloCampaign.Active ? (cat.PlayerUnit ?? cat.MultiplayerUnit) : (cat.MultiplayerUnit ?? cat.PlayerUnit);
        }

        /// <summary>The flashlight button: the light, or night vision when the scope has it.</summary>
        public void FlashlightButton()
        {
            if (NightVisionAvailable)
            {
                HaloLocal.NightVision = !HaloLocal.NightVision;
                HaloLog.Info("Combat: night vision " + (HaloLocal.NightVision ? "on" : "off"));
                return;
            }

            ToggleLight(true);
        }

        private void ToggleLight(bool fromButton)
        {
            _lampOn = !_lampOn;
            HaloTag unit = PlayerUnitTag();
            EnsureLamp(unit);
            if (_lampFx != null) { try { HaloFx.SetLights(_lampFx, _lampOn ? 1f : 0f); } catch (Exception) { } }
            HaloTag click = unit != null ? unit.Root.Ref(392) : null;      // (the unit's integrated light toggle effect)
            if (click != null && _lamp != null) HaloCombatFx.Play(click, _lamp.position, _lamp.rotation, _lamp);
            HaloLocal.Flashlight = _lampOn;
            HaloLog.Info("Combat: flashlight " + (_lampOn ? "on" : "off") + (fromButton ? "" : " (script)") + " (" + (unit != null ? unit.Name : "no unit") + ")");
        }

        private void EnsureLamp(HaloTag unit)
        {
            if (_lamp == null)
            {
                _lamp = new GameObject("Flashlight").transform;
                _lamp.SetParent(transform, false);
                var marker = new GameObject("marker:flashlight").transform;      // (the light hangs on the unit's "flashlight" marker: here the eye)
                marker.SetParent(_lamp, false);
                marker.localRotation = Quaternion.Euler(0f, -90f, 0f) * Quaternion.identity;
            }

            if (unit == _lampUnit && _lampFx != null) return;
            if (_lampFx != null) { try { HaloFx.Detach(_lampFx); } catch (Exception) { } }
            _lampFx = null;
            _lampUnit = unit;
            if (unit == null) return;
            try { _lampFx = HaloFx.Attach(unit, _lamp, true, true); }
            catch (Exception e) { HaloLog.Warn("Combat: the flashlight of " + unit + ": " + e.Message); }
        }

        private void StepFlashlight(HaloHand L, HaloHand R)
        {
            if (_lamp != null) _lamp.SetPositionAndRotation(Body.HeadPos, Body.HeadRot);
            if (!HaloLocal.Zoomed && HaloLocal.NightVision) HaloLocal.NightVision = false;      // (night vision goes with the zoom)
            bool press;
            if (_desktop) press = In("l", KeyCode.L, true);
            else
            {
                int free = _gunHand == 1 ? 0 : _gunHand == 0 ? 1 : 0;
                press = HandOf(free, L, R).StickClickPressed;
            }

            if (press) FlashlightButton();
        }

        /// <summary>The light off and gone (map change / leave).</summary>
        public void ForgetFlashlight()
        {
            if (_lampFx != null) { try { HaloFx.Detach(_lampFx); } catch (Exception) { } }
            _lampFx = null; _lampUnit = null; _lampOn = false;
            HaloLocal.Flashlight = false; HaloLocal.NightVision = false;
        }
    }
}

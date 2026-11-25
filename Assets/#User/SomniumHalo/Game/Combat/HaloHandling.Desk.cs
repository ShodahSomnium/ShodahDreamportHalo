using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Monitor play (no headset): mouse 1 fires, mouse 2 zooms (the weapon's own zoom levels: pistol 2x, sniper 2x / 8x ...), R reloads in
    /// the weapon's own time (the magazine's reload time; the shotgun shell by shell), F melee, G throws the selected grenade (along the
    /// view at the cyborg's grenade velocity), Q switches frag / plasma, 1 / 2 the weapons, E picks up the weapon in front (swapping the
    /// one in hand when two are carried). Shots go along the Desk module's aim (HaloDesk.AimRay: the camera through the crosshair),
    /// from the muzzle to what the crosshair is on. The client's own clashing keys are held off by HaloFirstPerson (the Desk module ticks it).
    /// </summary>
    public sealed partial class HaloHandling
    {
        private float _deskReload = -1f, _deskReloadLen;
        private bool _deskDrawn, _deskWasEmpty;
        private bool _keysBroken;
        private static readonly RaycastHit[] AimHits = new RaycastHit[16];

        private bool Key(KeyCode k) { if (_keysBroken) return false; try { return Input.GetKey(k); } catch (Exception) { _keysBroken = true; return false; } }
        private bool KeyDown(KeyCode k) { if (_keysBroken) return false; try { return Input.GetKeyDown(k); } catch (Exception) { _keysBroken = true; return false; } }
        private bool Mouse(int b) { if (_keysBroken) return false; try { return Input.GetMouseButton(b); } catch (Exception) { _keysBroken = true; return false; } }
        private bool MouseDown(int b) { if (_keysBroken) return false; try { return Input.GetMouseButtonDown(b); } catch (Exception) { _keysBroken = true; return false; } }

        /// <summary>Lab hook: simulated monitor input (null = the real keyboard and mouse).</summary>
        public static Func<string, bool> LabKeys;

        private bool In(string what, KeyCode k, bool down)
        {
            var lab = LabKeys;
            if (lab != null) return lab((down ? "down:" : "") + what);
            return down ? KeyDown(k) : Key(k);
        }

        private bool InMouse(int b, bool down)
        {
            var lab = LabKeys;
            if (lab != null) return lab((down ? "down:" : "") + "mouse" + b);
            return down ? MouseDown(b) : Mouse(b);
        }

        private void StepDesktop(float dt)
        {
            if (!_deskDrawn || (_held < 0 && Carried > 0))
            {
                int first = Guns[_lastHeld] != null ? _lastHeld : Guns[0] != null ? 0 : 1;
                if (Guns[first] != null) { Draw(first, 1); _deskDrawn = true; }
            }

            HaloGun g = HeldGun;
            bool tablet = TabletBlocksFire;

            // switching
            if (!tablet && _deskReload < 0f)
            {
                if (In("1", KeyCode.Alpha1, true) && Guns[0] != null && _held != 0) { Holster(HeldGun); Draw(0, 1); g = HeldGun; }
                if (In("2", KeyCode.Alpha2, true) && Guns[1] != null && _held != 1) { Holster(HeldGun); Draw(1, 1); g = HeldGun; }
            }

            // grenades: Q switches the kind, G throws
            if (!tablet && In("q", KeyCode.Q, true)) { HaloLocal.GrenadeSelected = 1 - HaloLocal.GrenadeSelected; }
            if (!tablet && In("g", KeyCode.G, true) && (g == null || !g.Def.PreventsGrenadeThrowing))
            {
                int kind = HaloLocal.GrenadeSelected;
                if ((kind == 0 ? HaloLocal.FragGrenades : HaloLocal.PlasmaGrenades) <= 0) kind = 1 - kind;
                if ((kind == 0 ? HaloLocal.FragGrenades : HaloLocal.PlasmaGrenades) > 0)
                {
                    if (kind == 0) HaloLocal.FragGrenades--; else HaloLocal.PlasmaGrenades--;
                    Ray r = HaloDesk.AimRay();
                    float v = HaloCombat.Catalog != null && HaloCombat.Catalog.GrenadeVelocity > 0f ? HaloCombat.Catalog.GrenadeVelocity * HaloSpace.Unit : 30f;
                    Vector3 from = Body.HeadPos + Body.HeadRot * new Vector3(0.15f, -0.15f, 0.35f);
                    ThrowGrenade(kind, from, (r.direction + Vector3.up * 0.08f).normalized * v);
                }
            }

            // melee, pick up
            if (!tablet && In("f", KeyCode.F, true)) DeskMelee();
            if (!tablet && In("e", KeyCode.E, true))
            {
                Ray r = HaloDesk.AimRay();
                HaloPickups.Item item = HaloPickups.NearestWeapon(Body.Feet + Vector3.up * 0.5f + Body.Forward * 0.4f, 1.6f, true);
                if (item != null) SwapFor(item, 1);
            }

            DeskPrompt();
            if (g == null) { HaloLocal.Zoomed = false; HaloLocal.ZoomMagnification = 1f; return; }

            // zoom
            if (!tablet && InMouse(1, true) && _deskReload < 0f) CycleZoom(g);

            // reload: R, or by itself when the trigger finds the magazine empty (Halo)
            bool wantReload = !tablet && In("r", KeyCode.R, true);
            if (_deskReload < 0f && g.State.CanReload && (wantReload || (g.State.Loaded <= 0 && InMouse(0, false)))) StartDeskReload(g);
            StepDeskReload(g, dt);

            bool fire = !tablet && _deskReload < 0f && InMouse(0, false);
            _gunHandPressed = !tablet && InMouse(0, true);
            Trigger(g, fire, dt);
        }

        private void StartDeskReload(HaloGun g)
        {
            var m = g.Def.PrimaryMagazine;
            if (m == null) return;
            bool shells = g.Def.Kind == HaloWeaponDef.Family.Shotgun;
            float len = shells ? (m.ReloadTime > 0f ? m.ReloadTime : 0.4f) : (m.ReloadTime > 0f ? m.ReloadTime : (g.State.Loaded <= 0 ? g.Def.ReloadEmptySeconds : g.Def.ReloadFullSeconds));
            if (len < 0.3f) len = 1.5f;
            _deskReload = 0f;
            _deskReloadLen = len;
            HaloLocal.Zoomed = false; HaloLocal.ZoomLevel = 0; HaloLocal.ZoomMagnification = 1f;
            if (m.ReloadingEffect != null) HaloCombatFx.OnModel(m.ReloadingEffect, g.ModelRoot, true);
            if (!shells) { g.Animate(g.State.Loaded <= 0 ? "reload-empty" : "reload-full", len); g.MagIn = false; }
            if (g.Sounds != null)
            {
                // Halo: the reload animation's own sound from its start (the shotgun: its "enter", then a sound per shell and the pump)
                if (shells) g.Sound(g.Sounds.ShellEnter, "shells taken");
                else
                {
                    HaloTag whole = g.State.Loaded <= 0 ? (g.Sounds.ReloadEmpty ?? g.Sounds.ReloadFull) : (g.Sounds.ReloadFull ?? g.Sounds.ReloadEmpty);
                    if (whole != null) g.Sound(new HaloReloadSounds.Piece { Sound = whole, From = "monitor reload" }, "reload");
                }

                _deskWasEmpty = g.State.Loaded <= 0;
            }      // (the gun's own magazine is hidden until seated at the end; the slide / handle animate)
            HaloCombat.NoteReload();
        }

        private void StepDeskReload(HaloGun g, float dt)
        {
            if (_deskReload < 0f) return;
            _deskReload += dt;
            HaloLocal.ReloadProgress = Mathf.Clamp01(_deskReload / _deskReloadLen);
            if (_deskReload < _deskReloadLen) return;
            if (g.Def.Kind == HaloWeaponDef.Family.Shotgun)
            {
                // shell by shell: one per reload time, until full, out of shells, or the trigger interrupts
                int n = g.State.TakeReload();
                if (n > 0) { g.State.Insert(n); if (g.Sounds != null) g.Sound(g.Sounds.Shell, "shell in"); }
                var m = g.Def.PrimaryMagazine;
                if (m != null && m.ReloadingEffect != null) HaloCombatFx.OnModel(m.ReloadingEffect, g.ModelRoot, true);
                if (n > 0 && g.State.CanReload && !InMouse(0, false)) { _deskReload = 0f; return; }
            }
            else
            {
                g.State.ReloadNow();
                var m = g.Def.PrimaryMagazine;
                if (m != null && m.ChamberingEffect != null) HaloCombatFx.OnModel(m.ChamberingEffect, g.ModelRoot, true);
            }

            if (g.Def.Kind == HaloWeaponDef.Family.Shotgun) { g.Animate("reload-exit"); if (g.Sounds != null) g.Sound(_deskWasEmpty ? g.Sounds.Pump : (g.Sounds.PumpFull ?? g.Sounds.Pump), "pump"); }
            g.MagIn = true;
            g.NeedsRack = false;
            _deskReload = -1f;
            HaloLocal.ReloadProgress = 0f;
        }

        /// <summary>The monitor's aim for a shot leaving <paramref name="muzzle"/>: toward what the crosshair is on (300 m at most).</summary>
        private void DeskAim(Vector3 muzzle, out Vector3 dir)
        {
            // the Desk module's crosshair point (its own avatar, capsule and hit boxes skipped in third person)
            try { dir = HaloDesk.ShotDirection(muzzle); if (dir.sqrMagnitude > 0.5f) return; }
            catch (Exception) { /* below: our own ray */ }
            Ray r = HaloDesk.AimRay();
            Vector3 point = r.origin + r.direction * 300f;
            int n = Physics.RaycastNonAlloc(r, AimHits, 300f, HaloLayers.ShotMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            float skip = (Body.HeadPos - r.origin).magnitude + 0.3f;       // (third person: nothing between the camera and the avatar counts)
            for (int i = 0; i < n; i++)
            {
                if (AimHits[i].collider == null || HaloCombat.IsOwnCollider(AimHits[i].collider)) continue;
                if (AimHits[i].distance < skip || AimHits[i].distance >= best) continue;
                best = AimHits[i].distance;
                point = AimHits[i].point;
            }

            Vector3 d = point - muzzle;
            dir = d.sqrMagnitude > 0.01f && Vector3.Dot(d, r.direction) > 0f ? d.normalized : r.direction;
        }

        /// <summary>The monitor's hold: the gun in front of the eye like a view model (low right; aimed at the crosshair).</summary>
        private void DeskHold(HaloGun g, ref Vector3 hp, ref Quaternion hr)
        {
            Ray r = HaloDesk.AimRay();
            hr = Quaternion.LookRotation(r.direction, Body.HeadRot * Vector3.up);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The weapon's HUD (weapon_hud_interface of the gun in hand, or of the vehicle gun we man) with its chain of child HUDs (e.g. assault
    /// rifle -> "master rounds" (panel, ammo counter, warnings) -> "master" (grenade warning)): static elements, meters (bullets, heat,
    /// battery), numbers, overlay icons, the crosshairs (aim reticle red over an enemy, zoom level, scope lines, "Reload" / "No Ammo"
    /// warnings) and the zoom mask on a monitor. Elements attached to a state the gun does not have are not drawn (ammo states for
    /// ballistic guns, heat / battery for plasma guns); flashing follows the top HUD's cutoffs.
    /// </summary>
    public sealed partial class HaloHudView
    {
        sealed class El { public int Kind; public HudWeapon Owner; public object Def; public RawImage Img; public HaloHudMeter Meter; public HaloHudDigits Digits; public HudCrosshair Cross; public HudOverlay Over; public HudOverlayElement OverEl; }
        const int KStatic = 0, KMeter = 1, KNumber = 2, KOverlay = 3, KCross = 4;

        RectTransform _weaponRoot, _reticleRoot, _crossRoot;
        RawImage _zoomMask, _zoomLeft, _zoomRight, _nightVision;
        readonly List<El> _els = new List<El>();
        HaloTag _wphiTag;
        HudWeapon _wphi;
        int _magSize;
        bool _ballistic;
        Canvas _reticleCanvas;

        // this frame's weapon state
        int _loaded, _reserve; float _heat, _charge; bool _overheated, _zoomed; int _zoomLevel; float _distance, _elevation; bool _onEnemy;

        void BuildWeapon()
        {
            _nightVision = HaloHudKit.Raw(_layerMask, "Night vision"); _nightVision.enabled = false; _nightVision.texture = HaloTextures.White;
            Stretch(_nightVision.rectTransform);
            _zoomMask = HaloHudKit.Raw(_layerMask, "Zoom mask"); _zoomMask.enabled = false;
            _zoomLeft = HaloHudKit.Raw(_layerMask, "Zoom left"); _zoomLeft.enabled = false; _zoomLeft.texture = HaloTextures.Black;
            _zoomRight = HaloHudKit.Raw(_layerMask, "Zoom right"); _zoomRight.enabled = false; _zoomRight.texture = HaloTextures.Black;
            _weaponRoot = HaloHudKit.Node(_layerPanels, "Weapon");
            Stretch(_weaponRoot);
            _reticleRoot = HaloHudKit.Node(_layerReticle, "Reticle");
            Stretch(_reticleRoot);
        }

        void TeardownWeapon()
        {
            ClearWeaponEls();
            if (_reticleCanvas != null) Destroy(_reticleCanvas.gameObject);
        }

        void ClearWeaponEls()
        {
            for (int i = 0; i < _els.Count; i++) if (_els[i].Meter != null) _els[i].Meter.Destroy();
            _els.Clear();
            if (_crossRoot != null) Destroy(_crossRoot.gameObject);
            for (int i = _weaponRoot.childCount - 1; i >= 0; i--) Destroy(_weaponRoot.GetChild(i).gameObject);
            _crossRoot = null;
        }

        /// <summary>The weapon HUD tag to show now (vehicle gun when manned, the held gun on foot or in a seat that allows weapons).</summary>
        HaloTag CurrentWphi(out HaloLocal.Weapon held, out HaloTag weap)
        {
            held = null; weap = null;
            if (HaloLocal.Vehicle != null)
            {
                if (HaloLocal.Gunning && HaloLocal.VehicleWeaponHud != null) { weap = HaloLocal.VehicleWeapon; return HaloLocal.VehicleWeaponHud; }
                if (!HaloLocal.SeatAllowsWeapons) return null;
            }
            held = HaloLocal.HeldWeapon;
            weap = held != null ? held.Tag : null;
            if (weap == null) return null;
            try { return weap.Root.Ref(1152); } catch (System.Exception) { return null; }
        }

        void Rebuild(HaloTag wphi, HaloTag weap)
        {
            ClearWeaponEls();
            _wphiTag = wphi;
            _wphi = HudWeapon.Read(wphi);
            _magSize = 0; _ballistic = false;
            if (weap != null)
            {
                // plasma guns carry an empty magazine block: ballistic = a magazine with rounds
                try { foreach (var m in weap.Root.Block(1264, 112)) { _magSize = m.I16(10); _ballistic = _magSize > 0 || m.I16(8) > 0; break; } } catch (System.Exception) { }
            }
            if (_wphi == null) return;
            _crossRoot = HaloHudKit.Node(_reticleRoot, "Crosshairs");
            _crossRoot.anchorMin = _crossRoot.anchorMax = new Vector2(0.5f, 0.5f); _crossRoot.sizeDelta = new Vector2(HaloHudKit.CanvasW, HaloHudKit.CanvasH);
            _crossRoot.anchoredPosition = Vector2.zero;
            var chain = new List<HudWeapon>();
            for (var w = _wphi; w != null && chain.Count < 6; w = w.Child) chain.Add(w);
            for (int c = chain.Count - 1; c >= 0; c--)       // deepest child first (the panel under the bullets)
            {
                var w = chain[c];
                var node = HaloHudKit.Node(_weaponRoot, System.IO.Path.GetFileName(w.Tag.Name));
                Stretch(node);
                foreach (var s in w.Statics) if (s.View != 2) _els.Add(new El { Kind = KStatic, Owner = w, Def = s, Img = HaloHudKit.Raw(node, "static") });
                foreach (var m in w.Meters) if (m.View != 2) { var me = new HaloHudMeter(node, "meter"); me.Bind(m); _els.Add(new El { Kind = KMeter, Owner = w, Def = m, Meter = me }); }
                foreach (var n in w.Numbers) if (n.View != 2) _els.Add(new El { Kind = KNumber, Owner = w, Def = n, Digits = new HaloHudDigits(node, "number", 8) });
                foreach (var oe in w.Overlays) if (oe.View != 2) foreach (var o in oe.Overlays) _els.Add(new El { Kind = KOverlay, Owner = w, OverEl = oe, Over = o, Img = HaloHudKit.Raw(node, "overlay") });
                foreach (var cr in w.Crosshairs) if (cr.View != 2) foreach (var o in cr.Overlays) _els.Add(new El { Kind = KCross, Owner = w, Cross = cr, Over = o, Img = HaloHudKit.Raw(_crossRoot, "crosshair " + cr.Type) });
            }
            HaloLog.Info("HUD: weapon HUD " + wphi.Name + " (" + chain.Count + " levels, " + _els.Count + " elements" + (_magSize > 0 ? ", magazine " + _magSize : ", energy") + ")");
        }

        void TickWeapon(bool show, Camera head)
        {
            HaloLocal.Weapon held = null; HaloTag weap = null;
            HaloTag wphi = show ? CurrentWphi(out held, out weap) : null;
            if (wphi != _wphiTag) Rebuild(wphi, weap);
            bool on = show && _wphi != null;
            HaloHudKit.SetActive(_weaponRoot.gameObject, on);
            HaloHudKit.SetActive(_reticleRoot.gameObject, on && HaloHud.PartVisible(HaloHud.PartCrosshair));
            if (!on) { HideZoom(); HaloHudKit.SetOn(_nightVision, false); if (_reticleCanvas != null) HaloHudKit.SetActive(_reticleCanvas.gameObject, false); return; }

            // state
            if (held != null) { _loaded = held.Loaded; _reserve = held.Reserve; _heat = held.Heat; _charge = held.Charge; _overheated = held.Overheated; }
            else { _loaded = _magSize > 0 ? _magSize : 999; _reserve = 999; _heat = HaloLocal.VehicleHeat; _charge = 1f; _overheated = HaloLocal.VehicleOverheated; }   // vehicle guns: endless
            _zoomed = HaloLocal.Zoomed && HaloLocal.Vehicle == null; _zoomLevel = HaloLocal.ZoomLevel;
            AimProbe(head);

            var top = _wphi;
            for (int i = 0; i < _els.Count; i++)
            {
                var e = _els[i];
                var cut = (e.Owner.Flags & 1) != 0 || e.Owner != top ? top : e.Owner;     // child HUDs use the parent's flashing cutoffs
                switch (e.Kind)
                {
                    case KStatic: TickStatic(e, (HudStatic)e.Def, cut); break;
                    case KMeter: TickMeter(e, (HudMeter)e.Def, cut); break;
                    case KNumber: TickNumber(e, (HudNumberEl)e.Def, cut); break;
                    case KOverlay: TickOverlay(e, cut); break;
                    case KCross: TickCross(e, cut); break;
                }
            }
            TickZoomMask();
            TickNightVision();
            PlaceReticle(head);
        }

        /// <summary>Elements are drawn for states the gun has: ammo (ballistic), heat / battery (energy); the rest only when zoomed.</summary>
        bool StateShown(int state)
        {
            switch (state)
            {
                case HudWeapon.TotalAmmo: case HudWeapon.LoadedAmmo: return _ballistic;
                case HudWeapon.Heat: case HudWeapon.Age: return !_ballistic || _zoomed;
                case HudWeapon.Distance: case HudWeapon.Elevation: return _zoomed;
                default: return false;
            }
        }

        bool StateFlashing(int state, HudWeapon cut)
        {
            switch (state)
            {
                case HudWeapon.TotalAmmo: return _ballistic && _reserve + _loaded < cut.TotalAmmoCutoff;
                case HudWeapon.LoadedAmmo: return _ballistic && _loaded < cut.LoadedAmmoCutoff;
                case HudWeapon.Heat: return _overheated || _heat * 100f >= cut.HeatCutoff && cut.HeatCutoff > 0;
                case HudWeapon.Age: return _charge * 100f <= cut.AgeCutoff;
                default: return false;
            }
        }

        HudAnchor AnchorOf(int childAnchor, HudWeapon owner)
        {
            return childAnchor > 0 ? (HudAnchor)(childAnchor - 1) : owner.Anchor;
        }

        void TickStatic(El e, HudStatic s, HudWeapon cut)
        {
            bool zoomOnly = s.State == HudWeapon.Age && _ballistic;          // e.g. the sniper's angle ticks beside the scope
            if (!StateShown(s.State) || s.Bitmap == null) { HaloHudKit.SetOn(e.Img, false); return; }
            if (!HaloHudKit.Show(e.Img, HaloHudKit.Sprite(s.Bitmap, s.Sequence), AnchorOf(s.ChildAnchor, e.Owner), s.Place, _mp && !zoomOnly)) return;
            if (zoomOnly && !_vr) { HaloHudKit.SetOn(e.Img, true); e.Img.color = HaloHudKit.Additive(s.Color.Default); return; }
            if (zoomOnly) { HaloHudKit.SetOn(e.Img, false); return; }
            e.Img.color = HaloHudKit.Additive(HaloHudKit.FlashColor(s.Color, StateFlashing(s.State, cut), _time));
            HaloHudKit.SetOn(e.Img, true);
        }

        void TickMeter(El e, HudMeter m, HudWeapon cut)
        {
            if (!StateShown(m.State)) { e.Meter.Hide(); return; }
            e.Meter.Place(AnchorOf(m.ChildAnchor, e.Owner), _mp);
            float f; int count = -1;
            switch (m.State)
            {
                case HudWeapon.LoadedAmmo: count = _loaded; f = _magSize > 0 ? (float)_loaded / _magSize : 0f; break;
                case HudWeapon.TotalAmmo: count = _reserve; f = 1f; break;
                case HudWeapon.Heat: f = Mathf.Clamp01(_heat); break;
                case HudWeapon.Age: f = Mathf.Clamp01(_charge); break;
                default: f = 0f; break;
            }
            HudColor c = HaloHudKit.Lerp(m.Min, m.Max, f);
            if (StateFlashing(m.State, cut)) c = HaloHudKit.FlashColor(new HudFlash { Default = c, Flashing = m.FlashColor, Period = 1f }, true, _time);
            e.Meter.Draw(f, count, c, m.Empty, MeterAlpha(m));
        }

        void TickNumber(El e, HudNumberEl n, HudWeapon cut)
        {
            if (!StateShown(n.State) || n.OnlyZoomed && !_zoomed) { e.Digits.Hide(); return; }
            float v;
            switch (n.State)
            {
                case HudWeapon.TotalAmmo: v = n.DivideByClip && _magSize > 0 ? Mathf.Ceil((float)_reserve / _magSize) : _reserve; break;
                case HudWeapon.LoadedAmmo: v = _loaded; break;
                case HudWeapon.Heat: v = Mathf.Round(_heat * 100f); break;
                case HudWeapon.Age: v = Mathf.Round(_charge * 100f); break;
                case HudWeapon.Distance: v = _distance; break;
                case HudWeapon.Elevation: v = _elevation; break;
                default: v = 0f; break;
            }
            bool zoomEl = n.OnlyZoomed;
            if (zoomEl && _vr) { e.Digits.Hide(); return; }
            Color c = HaloHudKit.Additive(HaloHudKit.FlashColor(n.Color, StateFlashing(n.State, cut), _time));
            e.Digits.Draw(n, AnchorOf(n.ChildAnchor, e.Owner), _mp && !zoomEl, v, c);
        }

        void TickOverlay(El e, HudWeapon cut)
        {
            var oe = e.OverEl; var o = e.Over;
            bool flashing = StateFlashing(oe.State, cut);
            bool emptyNow = _ballistic ? _loaded + _reserve <= 0 : _charge <= 0f;
            bool shown = StateShown(oe.State);
            if ((o.Type & HudOverlayElement.ShowAlways) != 0) { }
            else if ((o.Type & HudOverlayElement.ShowFlashing) != 0 && flashing) { }
            else if ((o.Type & HudOverlayElement.ShowEmpty) != 0 && emptyNow) { }
            else if ((o.Type & HudOverlayElement.ShowReloadOverheat) != 0 && _overheated) { }
            else if ((o.Type & HudOverlayElement.ShowDefault) != 0 && !flashing && !emptyNow) { }
            else shown = false;
            if (!shown || !HaloHudKit.Show(e.Img, HaloHudKit.Sprite(oe.Bitmap, o.Sequence), AnchorOf(oe.ChildAnchor, e.Owner), o.Place, _mp)) { HaloHudKit.SetOn(e.Img, false); return; }
            e.Img.color = HaloHudKit.Additive(HaloHudKit.FlashColor(o.Color, flashing && (o.Flags & 1) != 0, _time));
            HaloHudKit.SetOn(e.Img, true);
        }

        /// <summary>Whether a crosshair of a type shows now.</summary>
        bool CrossActive(int type, HudWeapon cut, out bool flashing)
        {
            flashing = false;
            bool lowLoaded = _ballistic && _loaded < cut.LoadedAmmoCutoff;
            switch (type)
            {
                case HudWeapon.AimCross: flashing = _onEnemy; return true;
                case HudWeapon.ZoomOverlay: return _zoomed;
                case HudWeapon.ShouldReload: flashing = true; return _ballistic && lowLoaded && _reserve > 0 && _loaded < _magSize;
                case HudWeapon.LowAmmoNoneLeft: flashing = true; return _ballistic && lowLoaded && _reserve <= 0 && _loaded > 0;
                case HudWeapon.FlashFiringNoAmmo: flashing = true; return _ballistic && _loaded <= 0 && _reserve <= 0;
                case HudWeapon.FlashHeat: flashing = true; return !_ballistic && cut.HeatCutoff > 0 && _heat * 100f >= cut.HeatCutoff;
                case HudWeapon.ReloadOverheat: flashing = true; return _overheated;
                case HudWeapon.FlashBattery: flashing = true; return !_ballistic && _charge > 0f && _charge * 100f <= cut.AgeCutoff;
                case 18: flashing = true; return !_ballistic && _charge <= 0f;       // flash when firing with depleted battery
                case HudWeapon.FlashTotalAmmo: flashing = true; return _ballistic && _reserve + _loaded < cut.TotalAmmoCutoff && _reserve + _loaded > 0;
                default: return false;
            }
        }

        void TickCross(El e, HudWeapon cut)
        {
            var c = e.Cross; var o = e.Over;
            bool flashing;
            bool shown = CrossActive(c.Type, cut, out flashing);
            bool onlyZoomed = (o.Flags & HudCrosshair.OnlyZoomed) != 0, notZoomed = (o.Flags & HudCrosshair.NotZoomed) != 0;
            if (onlyZoomed && !_zoomed || notZoomed && _zoomed) shown = false;
            if (onlyZoomed && _vr && c.Type != HudWeapon.ZoomOverlay) shown = false;      // the scope's own lines are a monitor overlay
            if (!shown) { HaloHudKit.SetOn(e.Img, false); return; }
            int frame = c.Type == HudWeapon.ZoomOverlay ? Mathf.Max(0, _zoomLevel - 1) : 0;
            if (o.FrameRate > 0f) frame = (int)(_time * o.FrameRate);
            HaloHudSprite sp = HaloHudKit.Sprite(c.Bitmap, o.Sequence, frame);
            // zoomed scope pieces are laid out for the full 640x480 screen (no multiplayer scaling)
            if (!HaloHudKit.Show(e.Img, sp, HudAnchor.Center, o.Place, _mp && !onlyZoomed, true)) return;
            bool flash = flashing && (o.Flags & HudCrosshair.FlashesWhenActive) != 0;
            e.Img.color = HaloHudKit.Additive(flash ? (c.Type == HudWeapon.AimCross ? o.Color.Flashing : HaloHudKit.FlashColor(o.Color, true, _time)) : o.Color.Default);
            HaloHudKit.SetOn(e.Img, e.Img.color.a > 0.01f);
        }

        // ------------------------------------------------------------------ zoom mask (monitor) and the aim probe

        void TickZoomMask()
        {
            var top = _wphi;
            bool on = !_vr && top != null && top.ZoomMask != null && _zoomed;
            if (!on) { HideZoom(); return; }
            var sp = HaloHudKit.Sprite(top.ZoomMask, 0);
            if (sp == null) { HideZoom(); return; }
            _zoomMask.texture = sp.Tex; _zoomMask.uvRect = sp.Uv; _zoomMask.color = Color.black;      // the mask darkens by its alpha
            // the mask covers the 4:3 middle of the screen; black beyond it on a wider screen
            HaloHudKit.Place(_zoomMask.rectTransform, HudAnchor.Center, 0f, 0f, HaloHudKit.CanvasW, HaloHudKit.CanvasH, HaloHudKit.CanvasW * 0.5f, HaloHudKit.CanvasH * 0.5f);
            float side = 2000f;
            HaloHudKit.Place(_zoomLeft.rectTransform, HudAnchor.Center, -HaloHudKit.CanvasW * 0.5f, 0f, side, HaloHudKit.CanvasH + 4f, side, (HaloHudKit.CanvasH + 4f) * 0.5f);
            HaloHudKit.Place(_zoomRight.rectTransform, HudAnchor.Center, HaloHudKit.CanvasW * 0.5f, 0f, side, HaloHudKit.CanvasH + 4f, 0f, (HaloHudKit.CanvasH + 4f) * 0.5f);
            HaloHudKit.SetOn(_zoomMask, true); HaloHudKit.SetOn(_zoomLeft, true); HaloHudKit.SetOn(_zoomRight, true);
        }

        void HideZoom() { HaloHudKit.SetOn(_zoomMask, false); HaloHudKit.SetOn(_zoomLeft, false); HaloHudKit.SetOn(_zoomRight, false); }

        Vector3 _aimPoint; bool _aimHit;

        /// <summary>The aim ray (monitor: the camera's centre; VR: the gun from Combat, else the right hand): distance for the rangefinder,
        /// the red reticle over an enemy, the VR reticle's spot.</summary>
        void AimProbe(Camera head)
        {
            Ray ray;
            if (_vr && !_lab)
            {
                if (HaloHud.AimFrame >= Time.frameCount - 1 && HaloHud.AimDirection.sqrMagnitude > 0.5f) ray = new Ray(HaloHud.AimOrigin, HaloHud.AimDirection);
                else
                {
                    Transform rh = HaloPlayer.RightHand;
                    ray = rh != null ? new Ray(rh.position, rh.forward) : head != null ? new Ray(head.transform.position, head.transform.forward) : new Ray(Vector3.zero, Vector3.forward);
                }
            }
            else ray = head != null ? new Ray(head.transform.position, head.transform.forward) : new Ray(Vector3.zero, Vector3.forward);
            RaycastHit hit;
            int mask = HaloLayers.ShotMask;
            _onEnemy = false;
            if (Physics.Raycast(ray, out hit, 300f, mask, QueryTriggerInteraction.Ignore))
            {
                _aimHit = true; _aimPoint = hit.point; _distance = hit.distance / HaloSpace.Unit;
                if (hit.collider.gameObject.layer == HaloLayers.Hitbox && hit.distance < 60f) _onEnemy = IsEnemy(hit.collider);
            }
            else { _aimHit = false; _aimPoint = ray.origin + ray.direction * 60f; _distance = 0f; }
            _elevation = Mathf.Asin(Mathf.Clamp(ray.direction.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        static bool IsEnemy(Collider c)
        {
            bool teams = HaloSession.Rules != null && HaloSession.Rules.Teams;
            if (HaloHud.TeamOfCollider != null)
            {
                int t;
                try { t = HaloHud.TeamOfCollider(c); } catch (System.Exception) { return false; }
                if (t == -2) return false;
                return !teams || t != HaloLocal.Team;
            }
            Transform own = HaloPlayer.Root;
            if (own != null && c.transform.IsChildOf(own)) return false;
            return !teams;
        }

        /// <summary>Monitor: the reticle in the middle of the HUD frame. VR: on its own small canvas at the aim point, the same angular size as on the visor.</summary>
        void PlaceReticle(Camera head)
        {
            if (!_vr || _lab && !_labVr)
            {
                if (_reticleRoot.parent != _layerReticle) { _reticleRoot.SetParent(_layerReticle, false); Stretch(_reticleRoot); }
                if (_reticleCanvas != null) HaloHudKit.SetActive(_reticleCanvas.gameObject, false);
                return;
            }
            if (_reticleCanvas == null)
            {
                var go = new GameObject("Halo HUD reticle (VR)");
                go.layer = HaloHudKit.UiLayer;
                go.transform.SetParent(transform.parent, false);
                _reticleCanvas = go.AddComponent<Canvas>();
                _reticleCanvas.renderMode = RenderMode.WorldSpace;
                _reticleCanvas.sortingOrder = WorldSortingOrder + 1;
                ((RectTransform)go.transform).sizeDelta = new Vector2(HaloHudKit.CanvasW, HaloHudKit.CanvasH);
            }
            HaloHudKit.SetActive(_reticleCanvas.gameObject, true);
            if (_reticleRoot.parent != _reticleCanvas.transform) { _reticleRoot.SetParent(_reticleCanvas.transform, false); Stretch(_reticleRoot); }
            if (head == null) return;
            Vector3 eye = head.transform.position;
            Vector3 p = _aimPoint;
            Vector3 d = p - eye;
            float dist = Mathf.Clamp(d.magnitude * 0.97f, 0.6f, _aimHit ? 40f : 30f);          // just in front of the surface hit (ours: 30 m into the open)
            Vector3 dir = d.sqrMagnitude > 1e-6f ? d.normalized : head.transform.forward;
            var t = _reticleCanvas.transform;
            t.position = eye + dir * dist;
            t.rotation = Quaternion.LookRotation(dir, head.transform.up);
            float k = 2f * dist * Mathf.Tan(VrFov * 0.5f * Mathf.Deg2Rad) / HaloHudKit.CanvasH;
            t.localScale = new Vector3(k, k, k);
            _reticleCanvas.worldCamera = head;
        }

        /// <summary>Night vision (HaloLocal.NightVision, the sniper scope): the weapon HUD's screen effect tint ("effect tint", night vision
        /// intensity) over the view, under the scope mask; ours: a see-through wash (Unity has no Halo night vision shader here), green when the
        /// tag gives no tint.</summary>
        void TickNightVision()
        {
            bool on = HaloLocal.NightVision && _wphi != null;
            if (!on) { HaloHudKit.SetOn(_nightVision, false); return; }
            float r = _wphi.TintR, g = _wphi.TintG, b = _wphi.TintB;
            if (r + g + b < 0.05f) { r = 0.35f; g = 1f; b = 0.35f; }
            float k = _wphi.NightVision > 0.01f ? Mathf.Clamp01(_wphi.NightVision) : 1f;
            _nightVision.color = new Color(r, g, b, 0.18f + 0.17f * k);
            HaloHudKit.SetOn(_nightVision, true);
        }
    }
}

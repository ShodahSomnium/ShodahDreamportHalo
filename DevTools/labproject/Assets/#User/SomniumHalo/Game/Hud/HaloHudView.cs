using System;
using UnityEngine;
using UnityEngine.UI;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The HUD's GameObject: one canvas whose 480-unit-high frame carries Halo's layout. Monitor: ScreenSpaceOverlay (scaled by the
    /// screen's height, so the corners follow the screen and nothing smears through the upscaler). Headset: the same canvas in world
    /// space as a visor ahead of the eyes (4:3, <see cref="VrFov"/> degrees high, <see cref="VrDistance"/> m, following the head with a
    /// little smoothing), the reticle on its own small canvas at the gun's aim point. The pieces live in the partial files:
    /// unit panel + grenades (here), weapon (.Weapon), motion tracker (.Tracker), texts / feed / score / scoreboard (.Text), waypoints,
    /// ally markers, damage arrows and the VR reticle (.World).
    /// </summary>
    public sealed partial class HaloHudView : MonoBehaviour
    {
        /// <summary>ours: the visor's vertical field of view (degrees), its distance (m) and the head-follow smoothing (1/s).</summary>
        public static float VrFov = 52f, VrDistance = 0.6f, VrFollow = 14f;
        /// <summary>Below the client's own overlays (menus at 0) like BO2's monitor HUD.</summary>
        public const int SortingOrder = -10;
        /// <summary>World space (visor, reticle): sorting order is the first key of the transparent sort, so a high order draws the HUD after
        /// the sky (order 0, drawn at the far plane without depth writes) and every other transparent thing.</summary>
        public const int WorldSortingOrder = 30000;

        Canvas _canvas;
        CanvasScaler _scaler;
        RectTransform _canvasRt, _frame;
        bool _vr, _lab, _labVr;
        Camera _labCam;
        Quaternion _visorRot = Quaternion.identity;
        bool _visorInit;
        float _time;
        bool _mp = false;

        // layers (sibling order = draw order)
        RectTransform _layerMask, _layerWorld, _layerTracker, _layerPanels, _layerReticle, _layerText, _layerBoard, _layerDamage;

        // unit panel
        RawImage _shieldBack, _healthBack, _unitBg, _seatBg;
        HaloHudMeter _shield, _health;
        HudUnit _seatUnit;

        // grenades: per type background, total background, count digits, icon
        sealed class GrenadePanel { public HudGrenade Def; public RawImage Back, TotalBack, Icon; public HaloHudDigits Count; public float X; }
        GrenadePanel[] _grenades = new GrenadePanel[0];

        public RectTransform Frame { get { return _frame; } }
        public float Time01 { get { return _time; } }

        public void Build()
        {
            gameObject.layer = HaloHudKit.UiLayer;
            // the player's own choices (settings keys "hud.vrfov" degrees, "hud.vrdist" metres)
            VrFov = Mathf.Clamp(HaloSettings.Num("hud.vrfov", 52f), 25f, 90f);
            VrDistance = Mathf.Clamp(HaloSettings.Num("hud.vrdist", 0.6f), 0.3f, 3f);
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = SortingOrder;
            _canvas.pixelPerfect = false;
            _scaler = gameObject.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(HaloHudKit.CanvasW, HaloHudKit.CanvasH);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 1f;
            _canvasRt = (RectTransform)transform;
            _frame = HaloHudKit.Node(transform, "Frame");
            Stretch(_frame);
            _layerMask = Layer("Zoom mask");
            _layerWorld = Layer("Waypoints");
            _layerTracker = Layer("Motion tracker");
            _layerPanels = Layer("Panels");
            _layerReticle = Layer("Reticle");
            _layerText = Layer("Text");
            _layerBoard = Layer("Scoreboard");
            _layerDamage = Layer("Damage");

            BuildUnit();
            BuildGrenades();
            BuildWeapon();
            BuildTracker();
            BuildText();
            BuildWorld();
            ApplyMode(HaloPlayer.XrPresent, true);
        }

        RectTransform Layer(string name)
        {
            var rt = HaloHudKit.Node(_frame, name);
            Stretch(rt);
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        public void Teardown()
        {
            if (_shield != null) _shield.Destroy();
            if (_health != null) _health.Destroy();
            TeardownWeapon();
            TeardownText();
            TeardownWorld();
        }

        // ------------------------------------------------------------------ monitor / headset / lab

        void ApplyMode(bool vr, bool force)
        {
            if (_lab) return;
            if (!force && vr == _vr) return;
            _vr = vr;
            if (vr)
            {
                _canvas.renderMode = RenderMode.WorldSpace; _canvas.sortingOrder = WorldSortingOrder;
                _canvas.worldCamera = HaloPlayer.Head();
                _canvasRt.sizeDelta = new Vector2(HaloHudKit.CanvasW, HaloHudKit.CanvasH);
                _visorInit = false;
            }
            else
            {
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = SortingOrder;
                transform.localScale = Vector3.one;
            }
            OnModeChanged();
            HaloLog.Info("HUD: " + (vr ? "headset visor (" + VrFov.ToString("0") + " degrees, " + VrDistance.ToString("0.00") + " m)" : "monitor screen overlay"));
        }

        /// <summary>Lab: the HUD in a camera's view (monitor: filling the frustum at 16:9; VR: the visor with that camera as the head).</summary>
        public void LabCamera(Camera cam, bool vr)
        {
            _lab = true; _labCam = cam; _labVr = vr; _vr = vr;
            _canvas.renderMode = RenderMode.WorldSpace; _canvas.sortingOrder = WorldSortingOrder;
            _canvas.worldCamera = cam;
            float aspect = vr ? HaloHudKit.CanvasW / HaloHudKit.CanvasH : 16f / 9f;
            _canvasRt.sizeDelta = new Vector2(HaloHudKit.CanvasH * aspect, HaloHudKit.CanvasH);
            _visorInit = false;
            OnModeChanged();
            PlaceCanvas(cam, 0f);
        }

        /// <summary>Puts the world-space canvas ahead of <paramref name="head"/> (visor) or filling its view (lab monitor).</summary>
        void PlaceCanvas(Camera head, float dt)
        {
            if (head == null) return;
            Transform h = head.transform;
            float dist, height;
            if (_lab && !_labVr)
            {
                dist = 0.5f;
                height = 2f * dist * Mathf.Tan(head.fieldOfView * 0.5f * Mathf.Deg2Rad);
                _visorRot = h.rotation;
            }
            else
            {
                dist = VrDistance;
                height = 2f * dist * Mathf.Tan(VrFov * 0.5f * Mathf.Deg2Rad);
                if (!_visorInit || dt <= 0f) { _visorRot = h.rotation; _visorInit = true; }
                else _visorRot = Quaternion.Slerp(_visorRot, h.rotation, 1f - Mathf.Exp(-VrFollow * dt));
            }
            transform.position = h.position + _visorRot * new Vector3(0f, 0f, dist);
            transform.rotation = _visorRot;
            float k = height / HaloHudKit.CanvasH;
            transform.localScale = new Vector3(k, k, k);
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            _time += dt;
            try
            {
                if (!_lab) ApplyMode(HaloPlayer.XrPresent, false);
                Camera head = _lab ? _labCam : HaloPlayer.Head();
                if (_vr || _lab) PlaceCanvas(head, dt);
                // Halo PC full screen draws the HUD tags at their own size on the 640x480 canvas; hud globals' multiplayer scale (0.75)
                // is the split-screen shrink, not used here (lead review against Halo PC: shield panel ~1/5 of the width, tracker ~1/5 of the height)
                _mp = false;
                bool alive = HaloLocal.Alive;
                bool ending = HaloSession.Current == HaloSession.Phase.Ending || HaloMatchControl.GameOver;
                bool campaign = HaloCampaign.Active;
                bool cine = HaloCinematic.Playing;
                _campaign = campaign;
                bool show = alive && !ending && !HaloHud.Hidden && !cine;
                _hidden = HaloHud.Hidden || cine;
                HaloHud.TickTimer(dt);
                TickInput();
                if (HaloLocal.DamagedAt > _damagedSeen) { _damagedSeen = HaloLocal.DamagedAt; if (Time.time - HaloLocal.DamagedAt < 0.5f) OnDamaged(HaloLocal.DamageFrom, 1f); }
                if (!string.IsNullOrEmpty(HaloLocal.PickupPrompt) && alive) { if (_help.Text != HaloLocal.PickupPrompt || _time > _helpUntil - 0.2f) OnMessage(HaloLocal.PickupPrompt, 0.3f); }
                TickUnit(show);
                TickGrenades(show);
                TickWeapon(show, head);
                TickTracker(show && HaloHud.PartVisible(HaloHud.PartMotionSensor) && (campaign || HaloSession.Rules == null || HaloSession.Rules.MotionTracker), head, dt);
                TickText(alive, ending, dt);
                TickWorld(show, head, dt);
            }
            catch (Exception e)
            {
                if (!_failed) HaloLog.Error("HUD: a frame failed: " + e);
                _failed = true;
            }
        }

        bool _failed;
        bool _campaign, _hidden;
        float _damagedSeen = -10f;
        bool _keyBroken;
        void TickInput()
        {
            bool held = false;
            if (!_lab)
            {
                if (_vr) { try { held = HaloXr.Hand(false).Secondary; } catch (Exception) { } }
                else if (!_keyBroken)
                {
                    try { held = Input.GetKey(KeyCode.F1); }
                    catch (Exception e) { _keyBroken = true; HaloLog.Warn("HUD: the scoreboard key (F1) is unavailable (legacy Input): " + e.Message); }
                }
            }
            HaloHud.SetScoreboardHeld(held);
        }

        // ------------------------------------------------------------------ unit panel (shields, health; top right)

        void BuildUnit()
        {
            _unitBg = HaloHudKit.Raw(_layerPanels, "Unit background");
            _seatBg = HaloHudKit.Raw(_layerPanels, "Seat background");
            _shieldBack = HaloHudKit.Raw(_layerPanels, "Shield panel");
            _shield = new HaloHudMeter(_layerPanels, "Shield meter");
            _healthBack = HaloHudKit.Raw(_layerPanels, "Health panel");
            _health = new HaloHudMeter(_layerPanels, "Health meter");
            var u = HaloHudKit.Unit;
            if (u != null) { _shield.Bind(u.Shield); _health.Bind(u.Health); }
        }

        float _shieldWas = 1f, _hitSoundAt;
        HaloAudio.Handle _loopCharge, _loopLow, _loopEmpty, _loopHeart;

        void TickUnit(bool show)
        {
            var u = HaloHudKit.Unit;
            if (u == null || !show)
            {
                HaloHudKit.SetOn(_unitBg, false); HaloHudKit.SetOn(_shieldBack, false); HaloHudKit.SetOn(_healthBack, false); HaloHudKit.SetOn(_seatBg, false);
                _shield.Hide(); _health.Hide();
                StopUnitSounds();
                return;
            }
            float sh = HaloLocal.Shield, hp = HaloLocal.Health;
            bool shieldsOn = HaloSession.Rules == null || HaloSession.Rules.Shields;
            bool low = HaloLocal.ShieldLow || (shieldsOn && sh < 0.25f);
            bool empty = shieldsOn && sh <= 0.001f;

            Static(_unitBg, u.Background, u.Anchor, false);
            bool shieldPart = HaloHud.PartVisible(HaloHud.PartShield), healthPart = HaloHud.PartVisible(HaloHud.PartHealth);
            if (shieldPart) Static(_shieldBack, u.ShieldBack, u.Anchor, low); else HaloHudKit.SetOn(_shieldBack, false);
            if (healthPart) Static(_healthBack, u.HealthBack, u.Anchor, hp < u.MinColorHealthCutoff); else HaloHudKit.SetOn(_healthBack, false);

            // the seat's own unit HUD (warthog driver reticle...) on top of the cyborg's
            HaloTag seatTag = HaloLocal.Vehicle != null ? HaloLocal.SeatHud : null;
            if (seatTag == null) _seatUnit = null;
            else if (_seatUnit == null || _seatUnit.Tag != seatTag) _seatUnit = HudUnit.Read(seatTag);
            if (_seatUnit != null) Static(_seatBg, _seatUnit.Background, _seatUnit.Anchor, false);
            else HaloHudKit.SetOn(_seatBg, false);

            // shield meter: colour between min and max by the value; over 1 = overshield (pulses towards the flash colour)
            HudMeter sm = u.Shield;
            if (shieldsOn && sm != null && shieldPart)
            {
                _shield.Place(u.Anchor, _mp);
                HudColor c = HaloHudKit.Lerp(sm.Min, sm.Max, Mathf.Clamp01(sh));
                if (sh > 1.001f) c = HaloHudKit.FlashColor(new HudFlash { Default = sm.Max, Flashing = sm.FlashColor, Period = 1f }, true, _time);
                _shield.Draw(Mathf.Clamp01(sh), -1, c, sm.Empty, MeterAlpha(sm));
            }
            else _shield.Hide();

            // health meter: Halo's three states (max colour above the max cutoff, the medium colour, min colour below the min cutoff)
            HudMeter hm = u.Health;
            if (hm != null && healthPart)
            {
                _health.Place(u.Anchor, _mp);
                HudColor c;
                if (hm.UseMinMaxForState)
                    c = hp >= u.MaxColorHealthCutoff ? hm.Max : hp >= u.MinColorHealthCutoff ? (u.MediumHealth.IsZero ? hm.Max : u.MediumHealth) : hm.Min;
                else c = HaloHudKit.Lerp(hm.Min, hm.Max, hp);
                if (hp < u.MinColorHealthCutoff) c = HaloHudKit.FlashColor(new HudFlash { Default = c, Flashing = hm.FlashColor, Period = 1f }, true, _time);
                _health.Draw(Mathf.Clamp01(hp), -1, c, hm.Empty, MeterAlpha(hm));
            }
            else _health.Hide();

            UnitSounds(u, sh, hp, low, empty, shieldsOn);
            _shieldWas = sh;
        }

        /// <summary>ours: Halo's meter "translucency" read as how much of the background shows through (1 - t / 4; the game adds the meter on top).</summary>
        static float MeterAlpha(HudMeter m) { return m.Opacity > 0.01f ? m.Opacity : 1f - Mathf.Clamp01(m.Translucency) * 0.25f; }

        /// <summary>Draws a static HUD element (bitmap, colour, flashing).</summary>
        void Static(RawImage img, HudStatic s, HudAnchor a, bool flashing)
        {
            if (s == null || s.Bitmap == null) { HaloHudKit.SetOn(img, false); return; }
            if (!HaloHudKit.Show(img, HaloHudKit.Sprite(s.Bitmap, s.Sequence), a, s.Place, _mp)) return;
            img.color = HaloHudKit.Additive(HaloHudKit.FlashColor(s.Color, flashing, _time));
            HaloHudKit.SetOn(img, true);
        }

        /// <summary>The unit HUD's warning sounds (shield hit, recharge, low, depleted, low-health heartbeat), played through the Audio module.</summary>
        void UnitSounds(HudUnit u, float sh, float hp, bool low, bool empty, bool shieldsOn)
        {
            if (_lab) return;
            Transform at = HaloPlayer.Head() != null ? HaloPlayer.Head().transform : transform;
            if (shieldsOn && sh < _shieldWas - 0.01f && _time > _hitSoundAt)
            {
                _hitSoundAt = _time + 0.25f;
                var t = u.Sound(HudUnit.ShieldDamaged);
                if (t != null) HaloGame.Guard("hud sound", () => HaloAudio.Play2D(t));
            }
            Loop(ref _loopCharge, u.Sound(HudUnit.ShieldRecharging), shieldsOn && HaloLocal.ShieldRecharging, at);
            Loop(ref _loopEmpty, u.Sound(HudUnit.ShieldEmpty), shieldsOn && empty, at);
            Loop(ref _loopLow, u.Sound(HudUnit.ShieldLow), shieldsOn && low && !empty, at);
            Loop(ref _loopHeart, u.Sound(HudUnit.HealthLow), hp < u.MinColorHealthCutoff, at);
        }

        static void Loop(ref HaloAudio.Handle h, HaloTag tag, bool on, Transform at)
        {
            if (on && tag != null && (h == null || !h.Alive))
            {
                try { h = HaloAudio.Loop(tag, at); } catch (Exception e) { HaloLog.Warn("HUD: sound " + tag.Name + ": " + e.Message); h = null; }
                if (h == null) h = new HaloAudio.Handle { Alive = true };      // stub audio: do not retry every frame
            }
            else if (!on && h != null)
            {
                try { HaloAudio.Stop(h); } catch (Exception) { }
                h = null;
            }
        }

        void StopUnitSounds()
        {
            Loop(ref _loopCharge, null, false, null); Loop(ref _loopLow, null, false, null);
            Loop(ref _loopEmpty, null, false, null); Loop(ref _loopHeart, null, false, null);
        }

        void OnDestroy() { StopUnitSounds(); }

        // ------------------------------------------------------------------ grenades (top left, next to the weapon)

        void BuildGrenades()
        {
            var defs = HaloHudKit.Grenades;
            _grenades = new GrenadePanel[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                var g = new GrenadePanel { Def = defs[i] };
                g.Back = HaloHudKit.Raw(_layerPanels, "Grenade " + i + " background");
                g.TotalBack = HaloHudKit.Raw(_layerPanels, "Grenade " + i + " outline");
                g.Icon = HaloHudKit.Raw(_layerPanels, "Grenade " + i + " icon");
                g.Count = new HaloHudDigits(_layerPanels, "Grenade " + i + " count", 2);
                // ours: both grenade HUD tags sit on the same spot; Halo shows the two counts side by side - the second panel goes one
                // panel width (the total-grenades outline's drawn width) to the right of the first.
                g.X = i == 0 ? 0f : PanelWidth(defs[0].TotalBack) * i;
                _grenades[i] = g;
            }
        }

        /// <summary>The drawn width of a static element's picture (its opaque columns), in its own bitmap pixels.</summary>
        static float PanelWidth(HudStatic s)
        {
            var sp = s != null ? HaloHudKit.Sprite(s.Bitmap, s.Sequence) : null;
            if (sp == null) return 40f;
            byte[] px = HaloHudKit.ImagePixels(s.Bitmap, sp.Image);
            if (px == null) return sp.W;
            int iw = sp.Bitmap.Images[sp.Image].Width, last = 0;
            for (int y = 0; y < sp.PxH; y++)
                for (int x = 0; x < sp.PxW; x++)
                    if (px[((sp.PxY + y) * iw + sp.PxX + x) * 4 + 3] > 24 && x > last) last = x;
            return last + 2;
        }

        void TickGrenades(bool show)
        {
            bool allowed = HaloSession.Rules == null || HaloSession.Rules.Grenades;
            for (int i = 0; i < _grenades.Length; i++)
            {
                var g = _grenades[i];
                var d = g.Def;
                bool on = show && allowed && (HaloLocal.Vehicle == null || HaloLocal.SeatAllowsWeapons);
                if (!on) { HaloHudKit.SetOn(g.Back, false); HaloHudKit.SetOn(g.TotalBack, false); HaloHudKit.SetOn(g.Icon, false); g.Count.Hide(); continue; }
                int n = i == 0 ? HaloLocal.FragGrenades : i == 1 ? HaloLocal.PlasmaGrenades : 0;
                bool selected = HaloLocal.GrenadeSelected == i;
                bool flashing = n <= d.FlashCutoff;
                float xs = g.X * HaloHudKit.SizeScale(d.TotalBack.Place, _mp);
                float dim = selected ? 1f : 0.55f;       // ours: the unselected type drawn dimmer
                StaticAt(g.Back, d.Background, d.Anchor, flashing && n == 0, xs, dim);
                StaticAt(g.TotalBack, d.TotalBack, d.Anchor, flashing, xs, dim);
                // icon: the overlay shown "on default" / "always" (or "on flashing" while low)
                HudOverlay ov = null;
                for (int k = 0; k < d.Overlays.Count; k++)
                {
                    var o = d.Overlays[k];
                    if ((o.Type & HudGrenade.ShowAlways) != 0 || (o.Type & HudGrenade.ShowDefault) != 0 && !flashing) { ov = o; }
                    if ((o.Type & HudGrenade.ShowFlashing) != 0 && flashing) { ov = o; break; }
                }
                if (ov != null && HaloHudKit.Show(g.Icon, HaloHudKit.Sprite(d.OverlayBitmap, ov.Sequence), d.Anchor, ov.Place, _mp, false, xs, 0f))
                {
                    g.Icon.color = HaloHudKit.Additive(HaloHudKit.FlashColor(ov.Color, flashing, _time), dim);
                    HaloHudKit.SetOn(g.Icon, true);
                }
                else HaloHudKit.SetOn(g.Icon, false);
                g.Count.Draw(d.Numbers, d.Anchor, _mp, n, HaloHudKit.Additive(HaloHudKit.FlashColor(d.Numbers.Color, flashing, _time), dim), xs, 0f);
            }
        }

        void StaticAt(RawImage img, HudStatic s, HudAnchor a, bool flashing, float x, float dim)
        {
            if (s == null || s.Bitmap == null) { HaloHudKit.SetOn(img, false); return; }
            if (!HaloHudKit.Show(img, HaloHudKit.Sprite(s.Bitmap, s.Sequence), a, s.Place, _mp, false, x, 0f)) return;
            img.color = HaloHudKit.Additive(HaloHudKit.FlashColor(s.Color, flashing, _time), dim);
            HaloHudKit.SetOn(img, true);
        }

        // called from HaloHud
        public void OnSpawned() { _shieldWas = 1f; ClearDamage(); }
    }
}

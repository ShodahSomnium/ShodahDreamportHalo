using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A weapon's state-driven effects, as Halo runs them on the weapon object:
    /// - the vent: the weapon tag's "overheated" effect (weap +884; plasma rifle / pistol: loop 0..0 = event 0 repeats, 0.05 s each, steam
    ///   at the "vent" markers) starts when the weapon overheats and keeps looping while it stays overheated; it ends when the heat falls to
    ///   the recovery threshold (the vents close, "overheat-exit"). One pass alone is a single 0.05 s puff.
    /// - the charge: the attachments fed by the weapon's "primary charged" input (plasma pistol: weapons\plasma rifle\overcharge.ligh with
    ///   its lens flare at "secondary trigger flare1", sound\sfx\weapons\plasma rifle\charging.lsnd at "primary trigger"), scaled by the
    ///   charge while the trigger is held (<see cref="HaloWeaponDef.ChargeAttachments"/>).
    /// Used by the local player's guns (HaloHandling) and callable by remote players / AI weapons (<see cref="Vent(Transform, HaloWeaponDef, bool, bool)"/>,
    /// <see cref="Charge"/>), keyed by the weapon model.
    /// </summary>
    public sealed class HaloGunFx
    {
        public HaloWeaponDef Def;
        public Transform Model;
        public bool FirstPerson;

        private HaloFx.Handle _vent;
        private bool _venting;

        private bool _charging;
        private readonly List<int> _lights = new List<int>(), _lightGens = new List<int>(), _flares = new List<int>(), _flareGens = new List<int>();
        private readonly List<float> _lightLo = new List<float>(), _lightHi = new List<float>(), _flareLo = new List<float>(), _flareHi = new List<float>();
        private readonly List<HaloAudio.Handle> _sounds = new List<HaloAudio.Handle>();
        private readonly List<float> _soundLo = new List<float>(), _soundHi = new List<float>();

        /// <summary>Lab / log counters: vent loops started, charge glows started.</summary>
        public static int VentsStarted, VentsStopped, ChargesStarted;
        public bool Venting => _venting && _vent.Alive;
        public bool Charging => _charging;

        public HaloGunFx(HaloWeaponDef def, Transform model, bool firstPerson) { Def = def; Model = model; FirstPerson = firstPerson; }

        /// <summary>Each frame (or on change): the vent loops while <paramref name="overheated"/>.</summary>
        public void SetVent(bool overheated)
        {
            if (overheated)
            {
                if (_venting && _vent.Alive) return;
                _venting = true;
                if (Def == null || Def.OverheatedEffect == null || Model == null) return;
                try
                {
                    if (Def.OverheatedEffect.ClassName == "effe")
                    {
                        // a Halo model keeps Halo's axes: the effect's frame faces the model's +X (as HaloFx.PlayOn)
                        _vent = HaloFx.Loop(Def.OverheatedEffect, Model.position, Model.rotation * HaloFx.HaloModelFrame, Model, FirstPerson);
                        VentsStarted++;
                    }
                    else if (Def.OverheatedEffect.ClassName == "snd!") HaloAudio.Play(Def.OverheatedEffect, Model, 1f);
                }
                catch (Exception e) { HaloLog.Warn("Combat: vent of " + Def.Name + " failed: " + e.Message); }
                return;
            }

            if (!_venting) return;
            _venting = false;
            if (_vent.Alive) { HaloFx.Stop(_vent); VentsStopped++; }
            _vent = default(HaloFx.Handle);
        }

        /// <summary>Each frame: <paramref name="charge"/> 0..1 while the trigger charges (&lt; 0 = not charging: the glow and sound go).</summary>
        public void SetCharge(float charge)
        {
            if (Def == null || Def.ChargeAttachments.Count == 0 || Model == null) return;
            if (charge < 0f) { if (_charging) StopCharge(); return; }
            if (!_charging) StartCharge();
            var rt = HaloFxRuntime.Instance;
            for (int i = 0; i < _lights.Count; i++)
                if (rt != null && rt.LightAlive(_lights[i], _lightGens[i])) rt.LightScale(_lights[i], Mathf.Clamp(charge, _lightLo[i], _lightHi[i]));
            for (int i = 0; i < _flares.Count; i++)
                if (rt != null && rt.FlareAlive(_flares[i], _flareGens[i])) rt.FlareScale(_flares[i], Mathf.Clamp(charge, _flareLo[i], _flareHi[i]));
            for (int i = 0; i < _sounds.Count; i++) HaloAudio.SetScale(_sounds[i], Mathf.Clamp(charge, _soundLo[i], _soundHi[i]));
        }

        private void StartCharge()
        {
            _charging = true;
            ChargesStarted++;
            try
            {
                var rt = HaloFxRuntime.Ensure();
                foreach (var a in Def.ChargeAttachments)
                {
                    Transform m = string.IsNullOrEmpty(a.Marker) ? null : HaloFx.Marker(Model, a.Marker);
                    Transform where = m != null ? m : Model;
                    switch (a.Type.ClassName)
                    {
                        case "ligh":
                        {
                            rt.UseMap(a.Type.Map);
                            var d = rt.Lib != null ? rt.Lib.Light(a.Type) : null;
                            if (d == null) break;
                            int s = rt.SpawnLight(d, where.position, where, 0f, true);
                            if (s >= 0) { rt.LightScale(s, 0f); _lights.Add(s); _lightGens.Add(rt.LightGen(s)); _lightLo.Add(a.Lower); _lightHi.Add(a.Upper); }
                            int f = rt.StartFlare(d, where.position, where, float.MaxValue, FirstPerson);
                            if (f >= 0) { rt.FlareScale(f, 0f); _flares.Add(f); _flareGens.Add(rt.FlareGen(f)); _flareLo.Add(a.Lower); _flareHi.Add(a.Upper); }
                            break;
                        }
                        case "lsnd":
                        {
                            var h = HaloAudio.Loop(a.Type, where);
                            if (h != null) { HaloAudio.SetScale(h, 0f); _sounds.Add(h); _soundLo.Add(a.Lower); _soundHi.Add(a.Upper); }
                            break;
                        }
                    }
                }
            }
            catch (Exception e) { HaloLog.Warn("Combat: charge glow of " + Def.Name + " failed: " + e.Message); }
        }

        private void StopCharge()
        {
            _charging = false;
            var rt = HaloFxRuntime.Instance;
            if (rt != null)
            {
                for (int i = 0; i < _lights.Count; i++) if (rt.LightAlive(_lights[i], _lightGens[i])) rt.KillLight(_lights[i]);
                for (int i = 0; i < _flares.Count; i++) if (rt.FlareAlive(_flares[i], _flareGens[i])) rt.KillFlare(_flares[i]);
            }
            for (int i = 0; i < _sounds.Count; i++) HaloAudio.Stop(_sounds[i]);
            _lights.Clear(); _lightGens.Clear(); _lightLo.Clear(); _lightHi.Clear();
            _flares.Clear(); _flareGens.Clear(); _flareLo.Clear(); _flareHi.Clear();
            _sounds.Clear(); _soundLo.Clear(); _soundHi.Clear();
        }

        // ------------------------------------------------------------------ the "ready" input (a drawn weapon: the energy sword's glow)

        private bool _ready;
        private readonly List<int> _readyLights = new List<int>(), _readyLightGens = new List<int>();
        private readonly List<HaloAudio.Handle> _readySounds = new List<HaloAudio.Handle>();
        private static readonly Dictionary<HaloWeaponDef, List<HaloWeaponDef.ChargeAttachment>> ReadyCache = new Dictionary<HaloWeaponDef, List<HaloWeaponDef.ChargeAttachment>>();

        /// <summary>
        /// The attachments whose primary scale comes (object function A..D out, "scale function by" a..d in) from the weapon input "ready"
        /// (weap +816..822 = 6; the energy sword's "energy sword dynamic.ligh" at "flare"): on while the weapon is in a hand, at the
        /// function's upper bound. Every other weapon attachment is driven by an input this code feeds elsewhere (primary charged:
        /// <see cref="SetCharge"/>) or not at all yet (heat, illumination, integrated light, age) - Halo scales those by their function, so
        /// they are never started unscaled.
        /// </summary>
        public void SetReady(bool on)
        {
            if (on == _ready) return;
            _ready = on;
            if (!on)
            {
                var rt0 = HaloFxRuntime.Instance;
                if (rt0 != null) for (int i = 0; i < _readyLights.Count; i++) if (rt0.LightAlive(_readyLights[i], _readyLightGens[i])) rt0.KillLight(_readyLights[i]);
                for (int i = 0; i < _readySounds.Count; i++) HaloAudio.Stop(_readySounds[i]);
                _readyLights.Clear(); _readyLightGens.Clear(); _readySounds.Clear();
                return;
            }

            if (Def == null || Model == null) return;
            var list = ReadyAttachments(Def);
            if (list.Count == 0) return;
            try
            {
                var rt = HaloFxRuntime.Ensure();
                foreach (var a in list)
                {
                    Transform m = string.IsNullOrEmpty(a.Marker) ? null : HaloFx.Marker(Model, a.Marker);
                    Transform where = m != null ? m : Model;
                    if (a.Type.ClassName == "ligh")
                    {
                        rt.UseMap(a.Type.Map);
                        var d = rt.Lib != null ? rt.Lib.Light(a.Type) : null;
                        int s = d != null ? rt.SpawnLight(d, where.position, where, 0f, true) : -1;
                        if (s >= 0) { rt.LightScale(s, a.Upper); _readyLights.Add(s); _readyLightGens.Add(rt.LightGen(s)); }
                    }
                    else if (a.Type.ClassName == "lsnd")
                    {
                        var h = HaloAudio.Loop(a.Type, where);
                        if (h != null) { HaloAudio.SetScale(h, a.Upper); _readySounds.Add(h); }
                    }
                }
            }
            catch (Exception e) { HaloLog.Warn("Combat: ready glow of " + Def.Name + " failed: " + e.Message); }
        }

        /// <summary>The weapon's "ready"-driven attachments (read once a definition; same reading as HaloWeaponDef.ReadChargeAttachments).</summary>
        private static List<HaloWeaponDef.ChargeAttachment> ReadyAttachments(HaloWeaponDef def)
        {
            List<HaloWeaponDef.ChargeAttachment> list;
            if (ReadyCache.TryGetValue(def, out list)) return list;
            list = new List<HaloWeaponDef.ChargeAttachment>();
            ReadyCache[def] = list;
            if (def.Tag == null) return list;
            try
            {
                var r = def.Tag.Root;
                int fnCount = r.Count(344);
                foreach (var at in r.Block(320, 72))
                {
                    HaloTag type = at.Ref(0);
                    int outIndex = at.U16(48) - 1;
                    if (type == null || outIndex < 0 || outIndex >= fnCount) continue;
                    var fn = r.Element(344, outIndex, 360);
                    int input = fn.U16(12) - 1;
                    if (input < 0 || input > 3) continue;
                    if (r.U16(816 + 2 * input) != 6) continue;      // WeaponFunctionIn 6 = ready
                    float lo = fn.F32(40), hi = fn.F32(44);
                    if (hi <= lo) { lo = 0f; hi = 1f; }
                    list.Add(new HaloWeaponDef.ChargeAttachment { Type = type, Marker = at.Str32(16), Lower = lo, Upper = hi });
                }
            }
            catch (Exception e) { HaloLog.Warn("Combat: ready attachments of " + def.Name + " unreadable: " + e.Message); }
            return list;
        }

        /// <summary>Everything off (the weapon is dropped, destroyed or changes hands).</summary>
        public void Stop() { SetVent(false); if (_charging) StopCharge(); SetReady(false); }

        // ------------------------------------------------------------------ keyed by model (remote players, AI)

        private static readonly List<HaloGunFx> Keyed = new List<HaloGunFx>();

        private static HaloGunFx For(Transform model, HaloWeaponDef def, bool firstPerson, bool create)
        {
            for (int i = Keyed.Count - 1; i >= 0; i--)
            {
                var k = Keyed[i];
                if (k.Model == null) { k.Stop(); Keyed.RemoveAt(i); continue; }
                if (k.Model == model)
                {
                    if (k.Def == def) return k;
                    k.Stop(); Keyed.RemoveAt(i);
                }
            }
            if (!create) return null;
            var n = new HaloGunFx(def, model, firstPerson);
            Keyed.Add(n);
            return n;
        }

        /// <summary>A weapon model's vent (remote players, AI): call each frame or on change with the weapon's overheated state.</summary>
        public static void Vent(Transform model, HaloWeaponDef def, bool overheated, bool firstPerson = false)
        {
            if (model == null || def == null) return;
            var g = For(model, def, firstPerson, overheated);
            if (g != null) g.SetVent(overheated);
        }

        /// <summary>A weapon model's charge glow (remote players, AI): 0..1 while charging, &lt; 0 when not.</summary>
        public static void Charge(Transform model, HaloWeaponDef def, float charge, bool firstPerson = false)
        {
            if (model == null || def == null) return;
            var g = For(model, def, firstPerson, charge >= 0f);
            if (g != null) g.SetCharge(charge);
        }

        /// <summary>A weapon model's "ready"-driven attachments (AI: on when the weapon is put in the hand, see <see cref="SetReady"/>).</summary>
        public static void Ready(Transform model, HaloWeaponDef def, bool on)
        {
            if (model == null || def == null) return;
            var g = For(model, def, false, on);
            if (g != null) g.SetReady(on);
        }

        /// <summary>Everything on this weapon model off and forgotten (the model is about to be destroyed or leaves the hand).</summary>
        public static void Forget(Transform model)
        {
            for (int i = Keyed.Count - 1; i >= 0; i--)
            {
                var k = Keyed[i];
                if (k.Model == null || k.Model == model) { k.Stop(); Keyed.RemoveAt(i); }
            }
        }

        /// <summary>All keyed effects off (map unload).</summary>
        public static void ClearKeyed() { for (int i = 0; i < Keyed.Count; i++) Keyed[i].Stop(); Keyed.Clear(); ReadyCache.Clear(); }
    }
}

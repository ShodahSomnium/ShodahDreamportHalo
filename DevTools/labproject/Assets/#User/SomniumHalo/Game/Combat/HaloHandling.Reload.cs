using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Reloading by hand, per weapon family (ours: Halo reloads on a button in its own time; the times stay the floor here):
    /// - magazine weapons (assault rifle, pistol, sniper rifle, needler, flamethrower canister, rocket launcher's rocket pair): the gun
    ///   hand's top button (B / Y) drops the magazine (its rounds go back to the reserve unless the weapon wastes them), the free hand takes
    ///   a fresh one from the belt pouch (the weapon's own magazine object model, e.g. "powerups\assault rifle ammo") and pushes it into the
    ///   well; it seats no sooner than 40 % of the magazine's reload time after the old one came out (ours). An empty gun then wants its
    ///   charging handle racked (the free hand on the back of the gun, pulled back 6 cm) unless "assisted reload" is on (Settings).
    /// - the shotgun: no magazine; the free hand takes a handful of shells from the pouch and holds it at the loading port: one shell goes
    ///   in every reload time of the magazine (0.4 s), as many as the hand took (up to 4, ours).
    /// - plasma weapons: nothing to load - they vent when overheated (the weapon's heat recovery) and their battery runs down.
    /// </summary>
    public sealed partial class HaloHandling
    {
        /// <summary>A magazine this close to the well seats (metres at a 1.7 m avatar; ours).</summary>
        public static float WellRadius = 0.11f;
        /// <summary>Fraction of the magazine's reload time that must pass between eject and seat (ours).</summary>
        public static float ReloadFloor = 0.4f;
        public static int ShellsPerHandful = 4;

        private HaloMagazine _heldMag;
        private int _magHand = -1;
        private int _shellHand = -1;
        private int _shellsInHand;
        private float _nextShellAt;
        private GameObject _shellVis;

        /// <summary>The magazine object's model (the weapon's ammunition equipment) as a loose prop; a small box when the weapon has none.</summary>
        private GameObject MagazineModel(HaloGun g, string name)
        {
            if (name != "Shells")
            {
                GameObject own = g.BuildMagazineProp(name + " " + g.Def.Name, _gear);
                if (own != null) return own;
            }

            var cat = HaloCombat.Catalog;
            HaloTag model = null;
            var m = g.Def.PrimaryMagazine;
            if (m != null && cat != null)
                foreach (var o in m.Objects) { var eq = cat.Equipment(o.Equipment); if (eq != null && eq.Model != null) { model = eq.Model; break; } }
            var holder = new GameObject(name + " " + g.Def.Name);
            holder.transform.SetParent(_gear, false);
            if (model != null)
            {
                var b = HaloModelBuilder.Build(model, holder.transform, new HaloModelBuilder.Options { CastShadows = false });
                if (b != null)
                {
                    b.Root.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                    // the ammunition boxes are a few rounds' worth of a whole pack: shown at 60 % so it reads as one magazine (ours)
                    b.Root.transform.localScale = Vector3.one * 0.6f;
                    return holder;
                }
            }

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(holder.transform, false);
            box.transform.localScale = new Vector3(0.012f, 0.035f, 0.02f);
            return holder;
        }

        public void EjectMagazine(HaloGun g)
        {
            if (g == null || !g.MagIn || !g.Def.HasMagazine || g.Def.Kind == HaloWeaponDef.Family.Shotgun) return;
            bool wasEmpty = g.State.Loaded <= 0;
            int r = g.State.EjectMagazine();
            g.MagIn = false;
            g.MagOutAt = Time.time;
            g.NeedsRack = wasEmpty && g.HasPull && !HaloCombat.AssistedReload && g.Def.Kind != HaloWeaponDef.Family.Rocket && g.Def.Kind != HaloWeaponDef.Family.Needler && g.Def.Kind != HaloWeaponDef.Family.Flamethrower;
            HaloLog.Info("Combat: " + g.Def.Name + " magazine out (" + r + " rounds back, reserve " + g.State.Reserve + ")");
            if (g.Sounds != null) g.Sound(g.Sounds.MagOut, "mag out");
            // the old magazine falls out of the well
            GameObject old = MagazineModel(g, "Spent");
            old.transform.SetPositionAndRotation(g.transform.TransformPoint(g.WellLocal), g.transform.rotation);
            old.transform.localScale = g.transform.lossyScale;
            var prop = old.AddComponent<HaloMagazine>();
            prop.Drop(-g.transform.up * 1.2f + g.transform.right * Random.Range(-0.3f, 0.3f));
            var m = g.Def.PrimaryMagazine;
            if (m != null && m.ReloadingEffect != null) HaloCombatFx.OnModel(m.ReloadingEffect, g.ModelRoot, true);
            if (_gunHand >= 0) HaloXr.Pulse(_gunHand == 1, 0.4f, 0.06f);
            HaloCombat.NoteReload();
        }

        /// <summary>The free hand at the pouch: a fresh magazine for a gun whose magazine is out, a handful of shells for the shotgun.</summary>
        private bool TakeFromPouch(int hand, HaloGun g)
        {
            if (g.Def.Kind == HaloWeaponDef.Family.Shotgun)
            {
                int room = g.State.LoadedMaximum - g.State.Loaded;
                if (room <= 0 || g.State.Reserve <= 0) { HaloXr.Pulse(hand == 1, 0.1f, 0.02f); return false; }
                _shellsInHand = Mathf.Min(ShellsPerHandful, Mathf.Min(room, g.State.Reserve));
                _shellHand = hand;
                if (_shellVis == null) _shellVis = MagazineModel(g, "Shells");
                _shellVis.SetActive(true);
                if (g.Sounds != null) g.Sound(g.Sounds.ShellEnter, "shells taken");
                _shellVis.transform.localScale = g.transform.lossyScale;
                HaloXr.Pulse(hand == 1, 0.3f, 0.05f);
                return true;
            }

            if (g.MagIn || !g.Def.HasMagazine) return false;
            if (g.State.Reserve <= 0) { HaloXr.Pulse(hand == 1, 0.1f, 0.02f); HaloLog.Info("Combat: no spare rounds for the " + g.Def.Name); return false; }
            int rounds = g.State.TakeReload();
            if (rounds <= 0) return false;
            GameObject go = MagazineModel(g, "Magazine");
            go.transform.localScale = g.transform.lossyScale;
            _heldMag = go.AddComponent<HaloMagazine>();
            _heldMag.Rounds = rounds;
            _magHand = hand;
            HaloXr.Pulse(hand == 1, 0.3f, 0.05f);
            return true;
        }

        private void StepMag(float dt)
        {
            HaloGun g = HeldGun;
            if (g == null || _heldMag == null || g.MagIn) return;
            Vector3 well = g.transform.TransformPoint(g.WellLocal), seat = g.transform.TransformPoint(g.SeatLocal);
            Vector3 c = _heldMag.transform.position;
            if (Mathf.Min((c - well).magnitude, (c - seat).magnitude) > WellRadius * 1.3f * Body.Scale) return;
            var m = g.Def.PrimaryMagazine;
            float floor = m != null ? m.ReloadTime * ReloadFloor : 0f;
            if (Time.time - g.MagOutAt < floor) return;       // (it seats the moment the weapon's time allows, while held at the well)
            SeatMag(g);
        }

        private void SeatMag(HaloGun g)
        {
            int rounds = _heldMag.Rounds;
            Destroy(_heldMag.gameObject);
            _heldMag = null;
            _magHand = -1;
            g.State.Insert(rounds);
            g.MagIn = true;
            if (HaloCombat.AssistedReload) g.NeedsRack = false;
            HaloLog.Info("Combat: " + g.Def.Name + " magazine seated (" + g.State.Loaded + " rounds" + (g.NeedsRack ? ", rack it" : "") + ")");
            if (g.Sounds != null) g.Sound(g.Sounds.MagIn, g.Def.Kind == HaloWeaponDef.Family.Rocket ? "rockets in" : "mag in");
            var mag = g.Def.PrimaryMagazine;
            if (mag != null && mag.ChamberingEffect != null && !g.NeedsRack) HaloCombatFx.OnModel(mag.ChamberingEffect, g.ModelRoot, true);
            else if (mag != null && mag.ReloadingEffect != null) HaloCombatFx.OnModel(mag.ReloadingEffect, g.ModelRoot, true);
            if (_gunHand >= 0) HaloXr.Pulse(_gunHand == 1, 0.5f, 0.07f);
        }

        /// <summary>A magazine let go of falls; its rounds were never fired, so they go back to the reserve.</summary>
        private void DropHeldMag()
        {
            if (_heldMag == null) { _magHand = -1; return; }
            HaloGun g = HeldGun ?? Guns[_lastHeld];
            if (g != null) g.State.Reserve += _heldMag.Rounds;
            _heldMag.Rounds = 0;
            _heldMag.Drop(_magHand >= 0 ? Body.HandVelocity(_magHand == 1) * 0.6f : Vector3.zero);
            _heldMag = null;
            _magHand = -1;
        }

        private void StepShells(float dt)
        {
            HaloGun g = HeldGun;
            if (g == null || _shellVis == null || _shellsInHand <= 0) return;
            Vector3 port = g.transform.TransformPoint(g.WellLocal);
            if ((_shellVis.transform.position - port).magnitude > WellRadius * 1.2f * Body.Scale) return;
            if (Time.time < _nextShellAt) return;
            var m = g.Def.PrimaryMagazine;
            _nextShellAt = Time.time + (m != null && m.ReloadTime > 0f ? m.ReloadTime : 0.4f);
            bool wasEmpty = g.State.Loaded <= 0;
            int n = g.State.TakeReload();
            if (n <= 0) { _shellsInHand = 0; }
            else
            {
                g.State.Insert(n);
                if (g.Sounds != null) g.Sound(g.Sounds.Shell, "shell in");
                if (wasEmpty && g.HasPull && !HaloCombat.AssistedReload) { g.NeedsRack = true; HaloLog.Info("Combat: " + g.Def.Name + " loaded from empty: pump it"); }
                _shellsInHand -= n;
                if (m != null && m.ReloadingEffect != null) HaloCombatFx.OnModel(m.ReloadingEffect, g.ModelRoot, true);
                if (_gunHand >= 0) HaloXr.Pulse(_gunHand == 1, 0.25f, 0.04f);
                if (_shellHand >= 0) HaloXr.Pulse(_shellHand == 1, 0.25f, 0.04f);
                if (g.State.Loaded == 1) HaloCombat.NoteReload();
            }

            if (_shellsInHand <= 0 || g.State.Loaded >= g.State.LoadedMaximum)
            {
                _shellsInHand = 0;
                _shellVis.SetActive(false);
            }
        }

        private void StepReloadTimers(float dt)
        {
            // (nothing timed here yet: the reload floors are checked where the magazine / shell goes in)
        }

        // ------------------------------------------------------------------ zoom

        /// <summary>The next zoom level of the weapon (Halo: none -> level 1 .. n -> none), magnification across the tag's range.</summary>
        private void CycleZoom(HaloGun g)
        {
            int n = g.Def.ZoomLevels;
            if (n <= 0) return;
            int level = HaloLocal.ZoomLevel + 1;
            if (level > n) level = 0;
            HaloLocal.ZoomLevel = level;
            HaloLocal.Zoomed = level > 0;
            float mag = 1f;
            if (level > 0) mag = n == 1 ? g.Def.ZoomMin : Mathf.Lerp(g.Def.ZoomMin, g.Def.ZoomMax, (level - 1) / (float)(n - 1));
            HaloLocal.ZoomMagnification = Mathf.Max(1f, mag);
            HaloTag s = level > 0 ? g.Def.ZoomInSound : g.Def.ZoomOutSound;
            if (s != null) HaloAudio.Play2D(s);
        }
    }
}

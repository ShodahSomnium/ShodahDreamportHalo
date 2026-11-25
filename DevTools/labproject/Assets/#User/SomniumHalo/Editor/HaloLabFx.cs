using System.Collections;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Effects lab ("fx"): opens a map (-halomap, default bloodgulch) WITHOUT a match, lays a grey floor (with a collider, so decals and
    /// bouncing casings have something to hit) and a sun far from the hub, fixes the frame time at 1/60 s, then plays the game's own
    /// effects and takes picture sequences of each: frag grenade explosion, assault rifle firing on the AR model (third and first person),
    /// plasma rifle impact on stone, a rocket in flight (attachments: exhaust contrail + particle system + light) and its explosion, plasma
    /// grenade explosion, AR / plasma / needler / sniper contrails. Pictures in DevTools/lab/fx/.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabFx
    {
        static HaloLabFx() { HaloLabRunner.Register("fx", r => Run(r)); HaloLabRunner.Register("fx-burst", r => Burst(r)); HaloLabRunner.Register("fx-plasma", r => Plasma(r)); }

        /// <summary>
        /// "fx-plasma": the non-bullet weapons through Combat's own code. For each weapon (plasma pistol, plasma rifle, needler, fuel rod)
        /// on a lit floor: the local gun's shot (HaloCombatFx.OnModel first person + the projectile) seen from the shooter's eye at the
        /// first frames (muzzle flash), the bolt in flight from the side and from behind; then a remote player's shot (HaloCombat.ReplayShot,
        /// third-person flash). Ghost / banshee bolts from the side. Pictures in DevTools/lab/fx-plasma/.
        /// </summary>
        static IEnumerator Plasma(HaloLabRunner r)
        {
            var entry = r.Game.Install.Map(HaloLabBoot.Arg("-halomap", "bloodgulch"));
            if (!r.Check(entry != null, "map in the install")) yield break;
            _map = new HaloMap(entry.File);
            HaloCombat.LabMap = _map;
            var cat = HaloCombat.Catalog;
            if (!r.Check(cat != null, "combat catalog")) yield break;
            Time.captureFramerate = 60;
            HaloFx.Preload(_map);
            yield return null;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(40f, 1f, 40f);
            floor.layer = HaloLayers.World;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.4f, 0.38f, 0.33f));
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            var sun = new GameObject("fx lab sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.0f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.42f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            HaloFx.ViewCamera = r.Cam;
            Physics.SyncTransforms();

            yield return HeldFire(r, cat, true);
            yield return HeldFire(r, cat, false);
            yield return PistolCharge(r, cat);
            if (HaloLabBoot.Arg("-held", "0") == "1") { HaloLabRunner.Log("end " + HaloFx.Stats); HaloFx.Clear(); Time.captureFramerate = 0; yield break; }

            string[] weapons = { @"weapons\plasma pistol\plasma pistol", @"weapons\plasma rifle\plasma rifle", @"weapons\needler\mp_needler", @"weapons\plasma_cannon\plasma_cannon" };
            string[] shorts = { "pp", "pr", "needler", "fuelrod" };
            for (int w = 0; w < weapons.Length; w++)
            {
                var wt = T("weap", weapons[w]);
                var def = wt != null ? cat.Weapon(wt) : null;
                if (!r.Check(def != null && def.PrimaryTrigger != null, weapons[w] + " parsed")) continue;
                var trig = def.PrimaryTrigger;
                var pd = cat.Projectile(trig.Projectile);
                var fe = trig.EffectFor(1, null);
                HaloLabRunner.Log(shorts[w] + ": projectile " + (pd != null ? pd.Tag.Name + " " + (pd.InitialSpeed * HaloSpace.Unit).ToString("0") + " m/s" : "-") + ", firing effect " + (fe != null && fe.Firing != null ? fe.Firing.Name : "-"));
                var built = HaloModelBuilder.Build(def.Model, null);
                var gun = built != null ? built.Root.transform : new GameObject("gun").transform;
                Vector3 g0 = new Vector3(w * 40f - 60f, 1.5f, -40f);
                gun.SetPositionAndRotation(g0, Quaternion.Euler(0f, -90f, 0f));   // model +X -> world +Z
                Physics.SyncTransforms();
                Transform muzzle = HaloFx.Marker(gun, "primary trigger") ?? gun;
                Vector3 m = muzzle.position;

                // the local gun (first person): the shooter's eye just behind and above the gun, the first frames of the shot
                r.Aim(g0 + new Vector3(0.12f, 0.22f, -0.55f), m + new Vector3(0f, 0f, 1.2f), 80f);
                Mark();
                if (fe != null && fe.Firing != null) HaloCombatFx.OnModel(fe.Firing, gun, true);
                if (pd != null) HaloCombat.FireProjectile(pd.Tag, m, Vector3.zero, 3, wt, gun, muzzle.right, true);
                HaloFx.DrawNow(); r.Shot(shorts[w] + "_fp_00"); HaloLabRunner.Log("  fp f0 " + HaloFx.Stats + ", projectiles " + HaloProjectiles.Count);
                yield return ShotAt(r, shorts[w] + "_fp_02", 2f / 60f);
                yield return ShotAt(r, shorts[w] + "_fp_06", 6f / 60f);
                yield return ShotAt(r, shorts[w] + "_fp_20", 20f / 60f);
                r.Check(pd == null || !(pd.Tag.Name.Contains("plasma") && pd.Tag.Name.Contains("bolt")) || HaloFx.FlareCount > 0, shorts[w] + ": the bolt's light flare is drawn: " + HaloFx.Stats);
                r.Check(pd == null || !(pd.Tag.Name.Contains("plasma") && pd.Tag.Name.Contains("bolt")) || HaloFx.VolumeCount > 0, shorts[w] + ": the bolt's light volume (mgs2 widget) is drawn: " + HaloFx.VolumeCount);
                yield return ShotAt(r, "wait", 2.5f);

                // the bolt in flight from the side (8 m off its path) and the muzzle flash of a remote player (third person)
                r.Aim(m + new Vector3(7f, 0.6f, 6f), m + new Vector3(0f, 0f, 6f), 70f);
                Mark();
                HaloCombat.ReplayShot(wt, m, muzzle.right, 3, muzzle);
                HaloFx.DrawNow(); r.Shot(shorts[w] + "_tp_side_00");
                float speed = pd != null ? Mathf.Max(5f, pd.InitialSpeed * HaloSpace.Unit) : 60f;
                yield return ShotAt(r, shorts[w] + "_side_flight", Mathf.Clamp(6f / speed, 0.05f, 1f));
                yield return ShotAt(r, "wait", 2.5f);

                // from behind (the shooter's view of the bolt flying away)
                r.Aim(m + new Vector3(0.25f, 0.35f, -1.2f), m + new Vector3(0f, 0f, 20f), 70f);
                Mark();
                HaloCombat.ReplayShot(wt, m, muzzle.right, 3, muzzle);
                yield return ShotAt(r, shorts[w] + "_behind_a", Mathf.Clamp(4f / speed, 0.05f, 1f));
                yield return ShotAt(r, shorts[w] + "_behind_b", Mathf.Clamp(15f / speed, 0.1f, 1.5f));
                yield return ShotAt(r, "wait", 2.5f);

                // coming at you (the target's view: 16 m down range, slightly off the path), the bolt about 8 m away
                r.Aim(m + new Vector3(0.4f, 0.3f, 16f), m, 70f);
                Mark();
                HaloCombat.ReplayShot(wt, m, muzzle.right, 3, muzzle);
                yield return ShotAt(r, shorts[w] + "_incoming", Mathf.Clamp(8f / speed, 0.05f, 1.5f));
                yield return ShotAt(r, "wait", 2.5f);
                Object.Destroy(gun.gameObject);
            }

            // vehicle bolts from the side
            string[] vb = { @"vehicles\ghost\ghost bolt", @"vehicles\banshee\banshee bolt" };
            for (int i = 0; i < vb.Length; i++)
            {
                var pt = T("proj", vb[i]);
                var pd = pt != null ? cat.Projectile(pt) : null;
                if (pd == null) continue;
                Vector3 s = new Vector3(i * 40f - 20f, 1.5f, 30f);
                r.Aim(s + new Vector3(7f, 0.6f, 6f), s + new Vector3(0f, 0f, 6f), 70f);
                Mark();
                HaloCombat.FireProjectile(pt, s, Vector3.zero, 3, pt, null, Vector3.forward, true);
                float speed = Mathf.Max(5f, pd.InitialSpeed * HaloSpace.Unit);
                yield return ShotAt(r, (i == 0 ? "ghost" : "banshee") + "_side_flight", Mathf.Clamp(6f / speed, 0.05f, 1f));
            }
            HaloLabRunner.Log("end " + HaloFx.Stats);
            HaloFx.Clear();
            Time.captureFramerate = 0;
        }

        /// <summary>
        /// The plasma rifle held down (as Combat fires it: the trigger's firing effect per round at its rate, accelerating from the
        /// minimum to the maximum rate, heat per round; the weapon's "overheated" effect when the heat passes the threshold), then released.
        /// <paramref name="local"/> = the local gun (first-person particles) seen from the shooter's eye, else a remote player's gun
        /// (third-person particles) seen from 3 m to the side. Pictures pr_held_[fp|tp]_0.2 / 0.6 / 1.2 / 2.0 s, and 0.3 / 1.0 s after release.
        /// </summary>
        static IEnumerator HeldFire(HaloLabRunner r, HaloCombatCatalog cat, bool local)
        {
            var wt = T("weap", @"weapons\plasma rifle\plasma rifle");
            var def = wt != null ? cat.Weapon(wt) : null;
            if (!r.Check(def != null && def.PrimaryTrigger != null, "plasma rifle parsed (held fire)")) yield break;
            var trig = def.PrimaryTrigger;
            var pd = cat.Projectile(trig.Projectile);
            var built = HaloModelBuilder.Build(def.Model, null);
            var gun = built != null ? built.Root.transform : new GameObject("gun").transform;
            Vector3 g0 = new Vector3(local ? 100f : 140f, 1.5f, -40f);
            gun.SetPositionAndRotation(g0, Quaternion.Euler(0f, -90f, 0f));   // model +X -> world +Z
            Physics.SyncTransforms();
            Transform muzzle = HaloFx.Marker(gun, "primary trigger") ?? gun;
            Vector3 m = muzzle.position;
            string pre = local ? "pr_held_fp_" : "pr_held_tp_";
            if (local) r.Aim(g0 + new Vector3(0.12f, 0.22f, -0.55f), m + new Vector3(0f, 0f, 1.2f), 80f);
            else r.Aim(m + new Vector3(3f, 0.4f, 0.5f), m + new Vector3(0f, 0.2f, 0.3f), 60f);
            HaloLabRunner.Log((local ? "held fire (local)" : "held fire (remote)") + ": rate " + trig.RateMin.ToString("0.#") + ".." + trig.RateMax.ToString("0.#")
                + "/s in " + trig.AccelerationTime.ToString("0.##") + " s, heat/round " + trig.HeatPerRound.ToString("0.###") + ", overheated at "
                + def.OverheatedThreshold.ToString("0.##") + ", effects " + trig.Effects.Count + ", overheated effect " + (def.OverheatedEffect != null ? def.OverheatedEffect.Name : "-"));
            Mark();
            float[] shots = { 0.2f, 0.6f, 1.2f, 2.0f };
            int next = 0, round = 0;
            float heat = 0f, nextRound = 0f;
            bool over = false;
            while (next < shots.Length)
            {
                float t = Time.time - _t0;
                if (!over && t >= nextRound)
                {
                    round++;
                    var fe = trig.EffectFor(round, null);
                    if (trig.Effects.Count > 1) fe = trig.Effects[(round - 1) % trig.Effects.Count];
                    if (local)
                    {
                        if (fe != null && fe.Firing != null) HaloCombatFx.OnModel(fe.Firing, gun, true);
                        if (pd != null) HaloCombat.FireProjectile(pd.Tag, m, Vector3.zero, 3, wt, gun, muzzle.right, true);
                    }
                    else HaloCombat.ReplayShot(wt, m, muzzle.right, 3, muzzle);
                    heat += trig.HeatPerRound;
                    float acc = trig.AccelerationTime > 0f ? Mathf.Clamp01(t / trig.AccelerationTime) : 1f;
                    float rate = Mathf.Max(1f, Mathf.Lerp(trig.RateMin, trig.RateMax, acc));
                    nextRound += 1f / rate;
                    if (def.OverheatedThreshold > 0f && heat >= def.OverheatedThreshold)
                    {
                        over = true;
                        HaloLabRunner.Log("  overheated after " + round + " rounds at t=" + t.ToString("0.00"));
                        if (def.OverheatedEffect != null) HaloCombatFx.OnModel(def.OverheatedEffect, gun, local);
                    }
                }
                if (t >= shots[next] - 0.001f)
                {
                    HaloFx.DrawNow(); r.Shot(pre + shots[next].ToString("0.0"));
                    HaloLabRunner.Log("  " + pre + shots[next].ToString("0.0") + " rounds " + round + " heat " + heat.ToString("0.00") + " " + HaloFx.Stats);
                    next++;
                }
                yield return null;
            }
            // released (or overheated): what stays of the flash
            Mark();
            yield return ShotAt(r, pre + "rel_0.3", 0.3f);
            yield return ShotAt(r, pre + "rel_1.0", 1.0f);
            yield return ShotAt(r, "wait", 2.0f);
            Object.Destroy(gun.gameObject);
        }

        /// <summary>
        /// The plasma pistol's overcharge (Combat's own HaloGunFx: the weapon's charge attachments - overcharge.ligh's light + lens flare at
        /// "secondary trigger flare1" - scaled by the charge) at charge 0.15 / 0.5 / 1.0 seen from the shooter's eye (pp_charge_*), then the
        /// charged bolt (weapons\plasma rifle\charged bolt: light, sparks effect, 0.5 wu wide contrail) from the side (pp_charged_side_*).
        /// Pictures in DevTools/lab/fx-plasma/.
        /// </summary>
        static IEnumerator PistolCharge(HaloLabRunner r, HaloCombatCatalog cat)
        {
            var wt = T("weap", @"weapons\plasma pistol\plasma pistol");
            var def = wt != null ? cat.Weapon(wt) : null;
            if (!r.Check(def != null && def.ChargeAttachments.Count > 0, "plasma pistol charge attachments (" + (def != null ? def.ChargeAttachments.Count : 0) + ")")) yield break;
            var built = HaloModelBuilder.Build(def.Model, null);
            var gun = built != null ? built.Root.transform : new GameObject("pp").transform;
            Vector3 g0 = new Vector3(60f, 1.5f, -40f);
            gun.SetPositionAndRotation(g0, Quaternion.Euler(0f, -90f, 0f));   // model +X -> world +Z
            Physics.SyncTransforms();
            Transform muzzle = HaloFx.Marker(gun, "primary trigger") ?? gun;
            Vector3 m = muzzle.position;
            var fx = new HaloGunFx(def, gun, true);
            float[] charges = { 0.15f, 0.5f, 1f };
            r.Aim(g0 + new Vector3(0.12f, 0.2f, -0.5f), m + new Vector3(0f, 0f, 1.2f), 80f);
            for (int i = 0; i < charges.Length; i++)
            {
                fx.SetCharge(charges[i]);
                yield return null;
                HaloFx.DrawNow();
                r.Shot("pp_charge_" + charges[i].ToString("0.00"));
                HaloLabRunner.Log("  pp_charge_" + charges[i].ToString("0.00") + " " + HaloFx.Stats);
            }
            r.Aim(m + new Vector3(1.6f, 0.25f, 0.2f), m + new Vector3(0f, 0f, 0.2f), 60f);
            yield return null;
            HaloFx.DrawNow();
            r.Shot("pp_charge_side_1.00");
            fx.SetCharge(-1f);
            Object.Destroy(gun.gameObject);

            var bt = T("proj", @"weapons\plasma rifle\charged bolt");
            if (!r.Check(bt != null, "charged bolt projectile")) yield break;
            Mark();
            var flight = HaloCombat.FireProjectile(bt, m, Vector3.zero, 3, wt, null, Vector3.forward, true);
            r.Check(flight != null, "charged bolt flies");
            float[] at = { 0.15f, 0.35f };
            for (int i = 0; i < at.Length; i++)
            {
                while (Time.time - _t0 < at[i] - 0.001f) yield return null;
                if (flight == null) break;
                Vector3 bp = flight.Position;
                r.Aim(bp + new Vector3(3f, 0.3f, -0.5f), bp + new Vector3(0f, 0f, -0.5f), 60f);
                HaloFx.DrawNow();
                r.Shot("pp_charged_side_" + at[i].ToString("0.00"));
                HaloLabRunner.Log("  pp_charged_side_" + at[i].ToString("0.00") + " bolt at " + bp.ToString("F2") + " " + HaloFx.Stats);
            }
            Mark();
            while (Time.time - _t0 < 2f) yield return null;
        }

        static HaloMap _map;
        static float _t0;

        static HaloTag T(string cls, string name)
        {
            var t = _map.Find(cls, name);
            if (t == null) HaloLabRunner.Log("  (no " + name + "." + cls + ")");
            return t;
        }

        /// <summary>Waits until <paramref name="t"/> seconds after the mark (fixed 1/60 s frames), redraws, takes a picture.</summary>
        static IEnumerator ShotAt(HaloLabRunner r, string name, float t)
        {
            while (Time.time - _t0 < t - 0.001f) yield return null;
            HaloFx.DrawNow();
            r.Shot(name);
            HaloLabRunner.Log("  " + name + " t=" + (Time.time - _t0).ToString("0.000") + " " + HaloFx.Stats);
        }

        static void Mark() { _t0 = Time.time; }

        static IEnumerator Run(HaloLabRunner r)
        {
            string name = HaloLabBoot.Arg("-halomap", "bloodgulch");
            var entry = r.Game.Install.Map(name);
            if (!r.Check(entry != null, "map " + name + " in the install")) yield break;
            _map = new HaloMap(entry.File);
            Time.captureFramerate = 60;
            HaloFx.Preload(_map);
            yield return null;

            // stage: floor + sun at the map origin (no map is built in this lab)
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "fx lab floor";
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(40f, 1f, 40f);
            floor.layer = HaloLayers.World;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f));
            mat.SetFloat("_Smoothness", 0.1f);
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            var sun = new GameObject("fx lab sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.0f;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.42f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            HaloFx.ViewCamera = r.Cam;
            Physics.SyncTransforms();

            // ---------------------------------------------------------------- frag grenade explosion
            var frag = T("effe", "weapons\\frag grenade\\effects\\explosion");
            Vector3 p = new Vector3(0f, 0f, 0f);
            r.Aim(p + new Vector3(0f, 3.5f, -11f), p + new Vector3(0f, 1.5f, 0f), 70f);
            Mark();
            HaloFx.Impact(frag, p, Vector3.up, Vector3.down);
            r.Check(HaloFx.Stats.Contains("particles"), "frag explosion started: " + HaloFx.Stats);
            yield return ShotAt(r, "frag_00_03", 0.03f);
            yield return ShotAt(r, "frag_01_10", 0.10f);
            yield return ShotAt(r, "frag_02_25", 0.25f);
            yield return ShotAt(r, "frag_03_50", 0.50f);
            yield return ShotAt(r, "frag_04_100", 1.00f);
            yield return ShotAt(r, "frag_05_200", 2.00f);
            yield return ShotAt(r, "frag_06_400", 4.00f);
            r.Check(HaloFx.DecalCount > 0, "frag left a scorch decal (" + HaloFx.DecalCount + ")");

            // ---------------------------------------------------------------- assault rifle firing on its model
            var arWeap = T("weap", "weapons\\assault rifle\\assault rifle");
            var arFire = T("effe", "weapons\\assault rifle\\effects\\fire bullet");
            Vector3 g = new Vector3(30f, 1.5f, 0f);
            GameObject gun = null;
            if (arWeap != null)
            {
                var model = arWeap.Root.Ref(40);
                var built = model != null ? HaloModelBuilder.Build(model, null) : null;
                gun = built != null ? built.Root : new GameObject("ar stand-in");
                gun.transform.position = g;
                gun.transform.rotation = Quaternion.identity; // Halo forward (+X) stays world +X
                var trig = HaloFx.Marker(gun.transform, "primary trigger");
                HaloLabRunner.Log("AR model " + (built != null) + ", marker 'primary trigger' " + (trig != null ? trig.position.ToString("0.000") + " right " + trig.right.ToString("0.00") : "missing"));
                Vector3 muzzle = trig != null ? trig.position : g + Vector3.right * 0.5f;
                r.Aim(muzzle + new Vector3(-0.2f, 0.25f, -1.3f), muzzle + new Vector3(0.1f, 0f, 0f), 60f);
                Mark();
                HaloFx.PlayOn(arFire, gun.transform, false);
                yield return ShotAt(r, "ar_tp_00", 0.0f);
                yield return ShotAt(r, "ar_tp_01", 1f / 60f);
                yield return ShotAt(r, "ar_tp_03", 3f / 60f);
                yield return ShotAt(r, "ar_tp_10", 10f / 60f);
                yield return ShotAt(r, "ar_tp_40", 40f / 60f);
                Mark();
                HaloFx.PlayOn(arFire, gun.transform, true);
                yield return ShotAt(r, "ar_fp_00", 0.0f);
                yield return ShotAt(r, "ar_fp_02", 2f / 60f);
                yield return ShotAt(r, "ar_fp_20", 20f / 60f);
                // a burst from behind the gun (the player's view)
                r.Aim(muzzle + new Vector3(-0.9f, 0.18f, 0.12f), muzzle + new Vector3(2f, 0f, 0f), 70f);
                Mark();
                for (int k = 0; k < 6; k++)
                {
                    HaloFx.PlayOn(arFire, gun.transform, true);
                    yield return ShotAt(r, "ar_burst_" + k, (k + 1) * (4f / 60f) - 0.0001f);
                }
            }

            // ---------------------------------------------------------------- plasma rifle impact on stone
            var plasmaHit = T("effe", "weapons\\plasma rifle\\effects\\impact stone");
            Vector3 q = new Vector3(-30f, 0f, 0f);
            r.Aim(q + new Vector3(0f, 1.2f, -3f), q + new Vector3(0f, 0.3f, 0f), 60f);
            Mark();
            HaloFx.Impact(plasmaHit, q, Vector3.up, new Vector3(0.3f, -1f, 0.6f).normalized);
            yield return ShotAt(r, "plasma_hit_00_02", 0.02f);
            yield return ShotAt(r, "plasma_hit_01_10", 0.10f);
            yield return ShotAt(r, "plasma_hit_02_30", 0.30f);
            yield return ShotAt(r, "plasma_hit_03_80", 0.80f);
            yield return ShotAt(r, "plasma_hit_04_250", 2.50f);

            // ---------------------------------------------------------------- AR bullet impact on dirt (decal + dust)
            var arDirt = T("effe", "weapons\\assault rifle\\effects\\impact dirt");
            Vector3 dz = new Vector3(-30f, 0f, 6f);
            r.Aim(dz + new Vector3(0f, 1.0f, -2f), dz, 60f);
            Mark();
            for (int k = 0; k < 5; k++) HaloFx.Impact(arDirt, dz + new Vector3(-0.4f + k * 0.2f, 0f, (k % 2) * 0.15f), Vector3.up, new Vector3(0.2f, -1f, 0.5f).normalized);
            yield return ShotAt(r, "ar_dirt_00_05", 0.05f);
            yield return ShotAt(r, "ar_dirt_01_30", 0.30f);
            yield return ShotAt(r, "ar_dirt_02_150", 1.50f);

            // ---------------------------------------------------------------- rocket in flight, then its explosion
            var rocketProj = T("proj", "weapons\\rocket launcher\\rocket");
            var rocketBoom = T("effe", "weapons\\rocket launcher\\effects\\rocket explosion");
            if (rocketProj != null)
            {
                float speed = rocketProj.Root.F32(484) * 30f * HaloSpace.Unit; // the cache keeps projectile speeds in world units per tick
                if (speed <= 0f) speed = 60f;
                HaloLabRunner.Log("rocket initial velocity " + rocketProj.Root.F32(484) + " wu/tick = " + speed.ToString("0.0") + " m/s");
                var rocket = new GameObject("lab rocket");
                Vector3 start = new Vector3(60f, 2f, 20f);
                rocket.transform.position = start;
                rocket.transform.rotation = Quaternion.LookRotation(Vector3.back);
                var att = HaloFx.Attach(rocketProj, rocket.transform);
                r.Aim(new Vector3(52f, 4f, 2f), new Vector3(60f, 2f, 8f), 70f);
                Mark();
                float flight = 18f / speed;
                int shot = 0;
                while (Time.time - _t0 < flight)
                {
                    rocket.transform.position = start + Vector3.back * (speed * (Time.time - _t0));
                    if (Time.frameCount % 6 == 0 && shot < 6) { HaloFx.DrawNow(); r.Shot("rocket_fly_" + shot++); HaloLabRunner.Log("  rocket " + HaloFx.Stats); }
                    yield return null;
                }
                Vector3 hit = rocket.transform.position; hit.y = 0f;
                HaloFx.Detach(att);
                Object.Destroy(rocket);
                Mark();
                HaloFx.Impact(rocketBoom, hit, Vector3.up, Vector3.back);
                yield return ShotAt(r, "rocket_boom_00_05", 0.05f);
                yield return ShotAt(r, "rocket_boom_01_30", 0.30f);
                yield return ShotAt(r, "rocket_boom_02_100", 1.00f);
            }

            // ---------------------------------------------------------------- plasma grenade explosion
            var plasmaNade = T("effe", "weapons\\plasma grenade\\effects\\explosion");
            Vector3 n = new Vector3(90f, 0f, 0f);
            r.Aim(n + new Vector3(0f, 3f, -10f), n + new Vector3(0f, 1.2f, 0f), 70f);
            Mark();
            HaloFx.Impact(plasmaNade, n, Vector3.up, Vector3.down);
            yield return ShotAt(r, "plasma_nade_00_05", 0.05f);
            yield return ShotAt(r, "plasma_nade_01_25", 0.25f);
            yield return ShotAt(r, "plasma_nade_02_80", 0.80f);

            // ---------------------------------------------------------------- projectile contrails side by side
            string[] conts = { "weapons\\assault rifle\\bullet", "weapons\\plasma rifle\\plasma", "weapons\\needler\\needler", "weapons\\sniper rifle\\sniper", "weapons\\fuel rod gun\\tail" };
            float[] speeds = { 120f, 40f, 30f, 300f, 30f };
            var heads = new GameObject[conts.Length];
            var trails = new HaloFx.Trail[conts.Length];
            for (int i = 0; i < conts.Length; i++)
            {
                heads[i] = new GameObject("lab head " + i);
                heads[i].transform.position = new Vector3(120f - 10f, 1f + i * 0.8f, 10f);
                var ct = T("cont", conts[i]);
                trails[i] = HaloFx.Contrail(ct, heads[i].transform);
            }
            r.Aim(new Vector3(120f, 3f, -1f), new Vector3(120f, 2.5f, 10f), 70f);
            Mark();
            float dur = 0.25f;
            while (Time.time - _t0 < dur)
            {
                for (int i = 0; i < conts.Length; i++) heads[i].transform.position = new Vector3(120f - 10f + Mathf.Min(20f, speeds[i] * (Time.time - _t0) * 0.15f), 1f + i * 0.8f, 10f);
                yield return null;
            }
            HaloFx.DrawNow(); r.Shot("contrails_0"); HaloLabRunner.Log("  contrails " + HaloFx.Stats);
            for (int k = 0; k < 6; k++)
            {
                for (int i = 0; i < conts.Length; i++) heads[i].transform.position += Vector3.right * (speeds[i] * 0.15f / 60f);
                yield return null;
            }
            HaloFx.DrawNow(); r.Shot("contrails_1");
            for (int i = 0; i < conts.Length; i++) r.Check(trails[i].Alive, conts[i] + " contrail alive");

            HaloLabRunner.Log("end " + HaloFx.Stats);
            HaloFx.Clear();
            yield return null;
            r.Check(HaloFx.ParticleCount == 0, "Clear() empties the pool");
            Time.captureFramerate = 0;
        }

        sealed class Bullet { public Transform T; public HaloFx.Attachments A; public Vector3 V; public bool Alive; }

        /// <summary>
        /// "fx-burst": what an assault-rifle burst leaves behind. A wall 8 m ahead, the local VR gun (first person) and a remote player's
        /// gun (third person) each fire 15 rounds at 15 a second; every round is a projectile anchor with its tag's attachments
        /// (HaloFx.Attach: the bullet contrail), detached on hitting the wall where the stone impact plays - Combat's own flow.
        /// Pictures during the burst and 0.5 / 2 / 5 s after the last round.
        /// </summary>
        static IEnumerator Burst(HaloLabRunner r)
        {
            var entry = r.Game.Install.Map(HaloLabBoot.Arg("-halomap", "bloodgulch"));
            if (!r.Check(entry != null, "map in the install")) yield break;
            _map = new HaloMap(entry.File);
            Time.captureFramerate = 90; // a headset's frame rate
            HaloFx.Preload(_map);
            yield return null;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            floor.layer = HaloLayers.World;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = new Vector3(0f, 3f, 8.25f);
            wall.transform.localScale = new Vector3(12f, 6f, 0.5f);
            wall.layer = HaloLayers.World;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f));
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            wall.GetComponent<Renderer>().sharedMaterial = mat;
            var sun = new GameObject("fx lab sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            HaloFx.ViewCamera = r.Cam;
            Physics.SyncTransforms();

            var weap = T("weap", @"weapons\assault rifle\assault rifle");
            var fire = T("effe", @"weapons\assault rifle\effects\fire bullet");
            var proj = T("proj", @"weapons\assault rifle\bullet");
            var hitFx = T("effe", @"weapons\assault rifle\effects\impact stone");
            if (weap == null || fire == null || proj == null) yield break;
            float speed = proj.Root.F32(484) * 30f * HaloSpace.Unit;
            HaloLabRunner.Log("AR bullet " + speed.ToString("0") + " m/s");
            var guns = new GameObject[2];
            for (int g = 0; g < 2; g++)
            {
                var b = HaloModelBuilder.Build(weap.Root.Ref(40), null);
                guns[g] = b != null ? b.Root : new GameObject("gun");
                guns[g].transform.position = new Vector3(g == 0 ? 0.25f : -1.5f, 1.4f, 0f);
                guns[g].transform.rotation = Quaternion.Euler(0f, -90f, 0f); // model +X -> world +Z (at the wall)
            }
            var bullets = new System.Collections.Generic.List<Bullet>();
            var free = new System.Collections.Generic.Stack<Transform>();
            r.Aim(new Vector3(0.6f, 1.75f, -1.6f), new Vector3(-0.3f, 0.9f, 6f), 80f);
            Mark();
            int shots = 0;
            float next = 0f;
            int pic = 0;
            while (shots < 15 || bullets.Count > 0)
            {
                float t = Time.time - _t0;
                if (shots < 15 && t >= next)
                {
                    for (int g = 0; g < 2; g++)
                    {
                        HaloFx.PlayOn(fire, guns[g].transform, g == 0);
                        Transform muzzle = HaloFx.Marker(guns[g].transform, "primary trigger") ?? guns[g].transform;
                        Transform a = free.Count > 0 ? free.Pop() : new GameObject("bullet").transform;
                        a.gameObject.SetActive(true);
                        Vector3 dir = (Vector3.forward + Random.insideUnitSphere * 0.03f).normalized;
                        a.SetPositionAndRotation(muzzle.position, Quaternion.LookRotation(dir));
                        bullets.Add(new Bullet { T = a, A = HaloFx.Attach(proj, a), V = dir * speed, Alive = true });
                    }
                    shots++;
                    next += 1f / 15f;
                }
                for (int i = bullets.Count - 1; i >= 0; i--)
                {
                    var b = bullets[i];
                    Vector3 p = b.T.position + b.V * Time.deltaTime;
                    if (p.z >= 8f)
                    {
                        Vector3 hit = b.T.position + b.V * ((8f - b.T.position.z) / Mathf.Max(1e-4f, b.V.z));
                        HaloFx.Detach(b.A);
                        b.T.gameObject.SetActive(false); free.Push(b.T);   // Combat's Release: detach, then pool the anchor
                        HaloFx.Impact(hitFx, hit, Vector3.back, b.V);
                        bullets.RemoveAt(i);
                    }
                    else b.T.position = p;
                }
                if (shots == 8 && pic == 0) { pic++; HaloFx.DrawNow(); r.Shot("burst_mid"); HaloLabRunner.Log("  mid " + HaloFx.Stats); }
                yield return null;
            }
            float end = Time.time - _t0;
            HaloLabRunner.Log("burst over at " + end.ToString("0.00") + " s: " + HaloFx.Stats);
            yield return ShotAt(r, "burst_after_0_5", end + 0.5f);
            yield return ShotAt(r, "burst_after_2", end + 2f);
            r.Aim(new Vector3(0.6f, 1.75f, -1.6f), new Vector3(0.2f, 0f, 1.5f), 80f); // look down at the shooters' feet
            yield return ShotAt(r, "burst_after_2_feet", end + 2.05f);
            r.Aim(new Vector3(0.6f, 1.75f, -1.6f), new Vector3(-0.3f, 0.9f, 6f), 80f);
            yield return ShotAt(r, "burst_after_5", end + 5f);
            r.Aim(new Vector3(0.6f, 1.75f, -1.6f), new Vector3(0.2f, 0f, 1.5f), 80f);
            yield return ShotAt(r, "burst_after_5_feet", end + 5.05f);
            r.Check(HaloFx.Stats.Contains("contrails 0"), "no contrail left 5 s after the burst: " + HaloFx.Stats);
            HaloFx.Clear();
            Time.captureFramerate = 0;
        }
    }
}

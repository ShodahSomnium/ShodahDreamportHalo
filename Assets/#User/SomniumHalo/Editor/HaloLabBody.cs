using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Lab "body" (workstream 2: unit body, damage, Flood death mechanics; -halomap d20 by default - it has every species): on a lab floor,
    /// - a flood carrier shot dead bursts once its death animation is done (HaloBipeds.Destroyed, the object gone; a grunt 1.5 m away takes
    ///   the burst's area damage when the effect runtime applies 'jpt!' parts), a second carrier's melee is fatal (it goes off by itself);
    /// - an infection form killed pops; one latches onto a grunt (strikes it), any damage pops it; one against the player's raised shield pops;
    /// - flood combat forms hit for 32% of their body feign death (by the tag's chance) and get up after the feign death time;
    /// - a hunter back-meleed survives ("not instantly killed by melee"); per-material hit boxes (the hunter has more boxes than nodes);
    /// - a jackal's energy shield drained: the shield region goes, its hit box shrinks, a bullet then hurts the jackal;
    /// - an elite swinging at a grunt 7 m away misses, at 1.2 m hits; a grunt's grenade leaves at the actv velocity x 30 (about 11.9 m/s).
    /// Pictures DevTools/lab/body/*.png; "LAB: body ..." lines.
    /// </summary>
    [InitializeOnLoadAttribute]
    static class HaloLabBody
    {
        static HaloLabBody() { HaloLabRunner.Register("body", r => Run(r)); }

        private static HaloMap _map;

        private static IEnumerator Seconds(float s)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(s * 60f));
            for (int i = 0; i < n; i++) yield return null;
        }

        private static IEnumerator Stage(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "d20");
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            _map = new HaloMap(entry.File);
            Time.captureFramerate = 60;
            HaloLocal.Alive = true;
            HaloCombat.LabMap = _map;
            try { HaloFx.Preload(_map); } catch (System.Exception e) { HaloLabRunner.Log("fx preload: " + e.Message); }
            var stage = new GameObject("body lab");
            HaloBipeds.LabParent = stage.transform;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "floor"; floor.transform.SetParent(stage.transform, false);
            floor.transform.localScale = new Vector3(30f, 1f, 30f);
            floor.layer = HaloLayers.World;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f)); mat.SetFloat("_Smoothness", 0.1f);
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.4f, 0.42f, 0.46f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            Physics.SyncTransforms();
            yield return null;
        }

        private static HaloTag Actv(string part, string not = null)
        {
            foreach (HaloTag v in _map.OfClass("actv"))
                if (v.Name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0 && (not == null || v.Name.IndexOf(not, System.StringComparison.OrdinalIgnoreCase) < 0)) return v;
            return null;
        }

        /// <summary>A hit of <paramref name="points"/> on a unit's centre from <paramref name="from"/> (player 0's, a generic bullet).</summary>
        private static void Hit(HaloUnit u, float points, Vector3 from, bool melee = false, bool back = false)
        {
            Vector3 c = u.Center;
            u.TakeHit(new HaloHit { Amount = points, SourcePlayer = 0, Point = c, Direction = (c - from).normalized, Melee = melee, BackHit = back, Instant = back });
        }

        private static IEnumerator Run(HaloLabRunner r)
        {
            yield return Stage(r);
            if (r.Failed) yield break;
            HaloTag carrier = Actv("floodcarrier"), infection = Actv(@"flood_infection\flood_infection", "nopop"), human = Actv("floodcombat_human unarmed") ?? Actv("floodcombat_human");
            HaloTag hunter = Actv(@"hunter\hunter"), jackal = Actv("jackal minor"), elite = Actv("elite minor plasma rifle") ?? Actv("elite minor"), grunt = Actv("grunt minor plasma pistol") ?? Actv("grunt minor");
            if (!r.Check(carrier != null && infection != null && human != null && hunter != null && jackal != null && elite != null && grunt != null,
                         "carrier / infection / combat form / hunter / jackal / elite / grunt variants in " + _map.Name)) yield break;
            var destroyed = new List<HaloUnit>();
            System.Action<HaloUnit> onDestroyed = u => { destroyed.Add(u); HaloLabRunner.Log("  destroyed: " + u.Def.Name + " (spawned actor " + (u.Def.SpawnedActor != null ? u.Def.SpawnedActor.Name + " " + u.Def.SpawnedCountMin + ".." + u.Def.SpawnedCountMax : "-") + ")"); };
            HaloBipeds.Destroyed += onDestroyed;
            Vector3 eye = new Vector3(0f, 1.7f, -7f);
            try
            {
                // ---- 1. the carrier bursts after its death animation; a grunt next to it takes the burst
                HaloUnit c1 = HaloBipeds.Spawn(carrier, new Vector3(0f, 0f, 0f), 180f, HaloTeam.None);
                HaloUnit dummy = HaloBipeds.Spawn(grunt, new Vector3(1.5f, 0f, 0f), 270f, HaloTeam.Covenant);
                r.Check(c1 != null && dummy != null, "carrier and grunt spawned");
                if (c1 == null || dummy == null) yield break;
                r.Check(c1.Def.DestroyedAfterDying && c1.Def.MeleeIsFatal && c1.Def.SpawnedCountMax >= 3, "carrier flags from the tag: destroyed after dying, melee is fatal, spawns " + c1.Def.SpawnedCountMin + ".." + c1.Def.SpawnedCountMax);
                yield return Seconds(1f);
                r.Aim(eye, new Vector3(0.5f, 0.6f, 0f), 55f);
                r.Shot("carrier_before");
                float g0 = dummy.Body * dummy.MaxBody + dummy.Shield * dummy.MaxShield;
                Hit(c1, 1000f, eye);
                r.Check(!c1.Alive, "the carrier dies (" + (c1.Animator != null ? c1.Animator.DeathName : "-") + ")");
                yield return Seconds(0.1f);
                r.Check(c1 != null && destroyed.Count == 0, "not destroyed before its death animation ends");
                yield return Seconds(0.5f);
                r.Shot("carrier_burst");
                yield return Seconds(0.6f);
                r.Check(destroyed.Count >= 1 && c1 == null, "the carrier's body is destroyed after dying (HaloBipeds.Destroyed, object gone)");
                float g1 = dummy != null ? dummy.Body * dummy.MaxBody + dummy.Shield * dummy.MaxShield : 0f;
                HaloLabRunner.Log("  grunt 1.5 m from the burst: " + g0.ToString("0") + " -> " + g1.ToString("0") + " points (alive " + (dummy != null && dummy.Alive) + ")");
                r.Check(dummy == null || !dummy.Alive || g1 < g0, "the burst's area damage hurts the grunt 1.5 m away (needs the effect runtime's jpt! area damage)");
                if (dummy != null) HaloBipeds.Erase(dummy);

                // ---- 2. a carrier's melee is fatal
                HaloUnit c2 = HaloBipeds.Spawn(carrier, new Vector3(-3f, 0f, 0f), 180f, HaloTeam.None);
                yield return Seconds(0.5f);
                int before = destroyed.Count;
                c2.Melee = true;
                yield return Seconds(3f);
                r.Check(c2 == null && destroyed.Count > before, "a carrier's own melee kills it and it bursts");

                // ---- 2b. a co-op puppet's melee only animates (the host owns the blow and a carrier's fatal melee)
                HaloUnit cp = HaloBipeds.Spawn(carrier, new Vector3(-3f, 0f, 0f), 180f, HaloTeam.None);
                yield return Seconds(0.3f);
                cp.Puppet = true;
                before = destroyed.Count;
                cp.PlayMelee();
                yield return Seconds(3f);
                r.Check(cp != null && cp.Alive && destroyed.Count == before, "a puppet carrier's melee animation does not kill it locally (the host's death record does)");
                if (cp != null) HaloBipeds.Erase(cp);

                // ---- 2c. a carrier killed outright inside an area damage (a grenade): its burst is queued behind the grenade's; the infection
                // forms spawned on its destroyed event (as the AI does) come after that burst, not inside it
                HaloTag frag = null;
                foreach (HaloTag t in _map.OfClass("jpt!")) if (t.Name.IndexOf(@"frag grenade\explosion", System.StringComparison.OrdinalIgnoreCase) >= 0) { frag = t; break; }
                HaloUnit c3 = HaloBipeds.Spawn(carrier, new Vector3(4f, 0f, -2f), 180f, HaloTeam.None);
                yield return Seconds(0.5f);
                var spawnedForms = new List<HaloUnit>();
                HaloUnit c3ref = c3;
                System.Action<HaloUnit> spawner = u =>
                {
                    if (u != c3ref) return;
                    for (int k = 0; k < 3; k++) spawnedForms.Add(HaloBipeds.Spawn(infection, u.Center + new Vector3(0.3f * (k - 1), -0.3f, 0.2f), 180f, HaloTeam.None));
                };
                HaloBipeds.Destroyed += spawner;
                try
                {
                    HaloLabRunner.Log("  carrier body destroyed threshold " + (c3.Def.Coll != null ? c3.Def.Coll.BodyDestroyedThreshold.ToString("0.00") : "-") + ", max body " + c3.MaxBody + ", grenade jpt " + (frag != null ? frag.Name : "none"));
                    if (r.Check(frag != null, "a frag grenade explosion jpt! in " + _map.Name))
                    {
                        before = destroyed.Count;
                        HaloCombat.EffectDamage(frag, c3.Center + Vector3.up * 0.1f, null, null);
                        bool now = destroyed.Count > before;
                        yield return Seconds(0.5f);
                        int alive = 0;
                        for (int k = 0; k < spawnedForms.Count; k++) if (spawnedForms[k] != null && spawnedForms[k].Alive) alive++;
                        HaloLabRunner.Log("  carrier in a grenade blast: destroyed " + (now ? "at once (inside the area)" : "later / not") + ", forms spawned " + spawnedForms.Count + ", alive " + alive);
                        if (now) r.Check(spawnedForms.Count == 3 && alive == 3, "forms spawned on a carrier destroyed inside a blast survive its own burst (" + alive + " of " + spawnedForms.Count + ")");
                        else HaloLabRunner.Log("  (the grenade did not destroy the carrier outright: the queued-burst path was not exercised)");
                    }
                }
                finally { HaloBipeds.Destroyed -= spawner; }
                for (int k = 0; k < spawnedForms.Count; k++) if (spawnedForms[k] != null) HaloBipeds.Erase(spawnedForms[k]);
                if (c3 != null) HaloBipeds.Erase(c3);

                // ---- 3. infection forms: pop, latch, pop on the player's shield
                HaloUnit i1 = HaloBipeds.Spawn(infection, new Vector3(0f, 0f, 1f), 180f, HaloTeam.None);
                yield return Seconds(0.5f);
                before = destroyed.Count;
                Hit(i1, 200f, eye);
                yield return Seconds(1f);
                r.Check(i1 == null && destroyed.Count > before, "a killed infection form pops (destroyed after its 7-frame death)");

                HaloUnit host = HaloBipeds.Spawn(grunt, new Vector3(2f, 0f, 2f), 180f, HaloTeam.Covenant);
                HaloUnit i2 = HaloBipeds.Spawn(infection, new Vector3(2f, 0f, 1.4f), 0f, HaloTeam.None);
                yield return Seconds(0.5f);
                float h0 = host.Body;
                bool latched = i2.TryLatch(host);
                r.Check(latched && i2.Latched && i2.transform.parent != null, "an infection form latches onto a grunt");
                r.Aim(new Vector3(2f, 1.2f, -1f), host.Center, 50f);
                yield return Seconds(0.3f);
                r.Shot("infection_latched");
                float delay = i2.Def.MeleeAttackDelay > 0.05f ? i2.Def.MeleeAttackDelay : 3f;
                yield return Seconds(delay + 0.5f);
                HaloLabRunner.Log("  latched grunt body " + h0.ToString("0.00") + " -> " + (host != null ? host.Body.ToString("0.00") : "gone") + " (strike every " + delay.ToString("0.0") + " s), action " + (i2 != null && i2.Animator != null ? i2.Animator.ActionName : "-"));
                r.Check(host == null || !host.Alive || host.Body < h0, "the latched form strikes its host");
                before = destroyed.Count;
                if (i2 != null && i2.Alive) Hit(i2, 1f, eye);
                yield return Seconds(1f);
                r.Check(i2 == null && destroyed.Count > before, "any damage pops a latched infection form");
                if (host != null) HaloBipeds.Erase(host);

                HaloUnit i3 = HaloBipeds.Spawn(infection, new Vector3(-2f, 0f, 1f), 180f, HaloTeam.None);
                yield return Seconds(0.3f);
                HaloLocal.Alive = true; HaloLocal.Shield = 1f;
                bool l3 = i3.TryLatch(null);
                yield return Seconds(1f);
                r.Check(!l3 && i3 == null, "an infection form against the player's raised shield pops (shields fry infection forms)");

                // ---- 4. flood combat forms feign death and get up
                var forms = new List<HaloUnit>();
                for (int k = 0; k < 6; k++) forms.Add(HaloBipeds.Spawn(human, new Vector3(-6f + k * 1.6f, 0f, 5f), 180f, HaloTeam.None));
                yield return Seconds(0.5f);
                int feigned = 0;
                HaloUnit feigner = null;
                for (int k = 0; k < forms.Count; k++)
                {
                    HaloUnit f = forms[k];
                    Hit(f, f.MaxBody * Mathf.Max(0.32f, f.Def.FeignDeathThreshold + 0.02f), eye);
                    if (f.Feigning) { feigned++; if (feigner == null) feigner = f; }
                }

                HaloLabRunner.Log("  combat forms feigning " + feigned + " / " + forms.Count + " (chance " + forms[0].Def.FeignDeathChance + ", threshold " + forms[0].Def.FeignDeathThreshold + ", time " + forms[0].Def.FeignDeathTime + " s)");
                r.Check(feigned >= 1, "flood combat forms feign death by the tag's chance");
                if (feigner != null)
                {
                    r.Check(feigner.Alive && feigner.Animator.Dying, "a feigning form is alive and lies in a death pose");
                    r.Aim(new Vector3(feigner.transform.position.x, 1.4f, 1.5f), feigner.Center, 50f);
                    yield return Seconds(1.6f);      // (the death clip has played out: it lies there)
                    r.Shot("feign_down");
                    yield return Seconds(Mathf.Max(0.2f, feigner.Def.FeignDeathTime - 1.6f + 0.3f));
                    r.Check(feigner != null && feigner.Alive && !feigner.Feigning, "it gets up after the feign death time (" + (feigner != null && feigner.Animator != null ? feigner.Animator.ActionName ?? "-" : "-") + ")");
                    yield return Seconds(0.4f);
                    r.Shot("feign_resurrect");
                }

                for (int k = 0; k < forms.Count; k++) if (forms[k] != null) HaloBipeds.Erase(forms[k]);

                // ---- 5. the hunter: back melee is a normal melee; per-material hit boxes
                HaloUnit hu = HaloBipeds.Spawn(hunter, new Vector3(6f, 0f, 0f), 180f, HaloTeam.None);
                yield return Seconds(0.5f);
                int nodesWithBsp = 0;
                foreach (HaloCollisionDef.Node n in hu.Def.Coll.Nodes) if (n.Bsps.Count > 0) nodesWithBsp++;
                HaloLabRunner.Log("  hunter hit boxes " + hu.Hitboxes.Count + " for " + nodesWithBsp + " collision nodes");
                r.Check(hu.Hitboxes.Count > nodesWithBsp, "the hunter gets one hit box per material (flesh between the armour)");
                var cat = HaloCombat.Catalog;
                HaloTag melee = cat != null ? cat.UnitMelee : null;
                Vector3 behind = hu.transform.position - hu.transform.forward * 1.5f + Vector3.up * 1.2f;
                hu.TakeHit(new HaloHit { Amount = 30f, Effect = melee, SourcePlayer = 0, Point = hu.Center, Direction = (hu.Center - behind).normalized, Melee = true, BackHit = true, Instant = true });
                r.Check(hu.Alive, "a back melee does not kill the hunter (body " + hu.Body.ToString("0.00") + ")");
                HaloBipeds.Erase(hu);

                // ---- 6. the jackal's shield goes down and comes back
                HaloUnit ja = HaloBipeds.Spawn(jackal, new Vector3(-6f, 0f, 0f), 180f, HaloTeam.None);
                yield return Seconds(0.5f);
                float shieldBox = ShieldBoxSize(ja);
                // an infection form latched onto the jackal stays on through the shield region's model rebuild (it is not destroyed with the old model)
                HaloUnit ij = HaloBipeds.Spawn(infection, ja.transform.position + ja.transform.forward * -0.6f, 0f, HaloTeam.None);
                yield return Seconds(0.3f);
                bool jl = ij != null && ij.TryLatch(ja);
                HaloLabRunner.Log("  infection form latched onto the jackal: " + jl);
                Vector3 front = ja.transform.position + ja.transform.forward * 3f + Vector3.up * 1f;
                Vector3 onShield = ShieldBoxCenter(ja, ja.Center + ja.transform.forward * 3f);      // (a hit on the held shield: the body materials pass shields)
                ja.TakeHit(new HaloHit { Amount = ja.MaxShield + 1f, SourcePlayer = 0, Point = onShield, Direction = -ja.transform.forward });
                yield return Seconds(0.2f);
                float shieldBox2 = ShieldBoxSize(ja);
                HaloLabRunner.Log("  jackal shield " + ja.Shield.ToString("0.00") + ", shield hit box " + shieldBox.ToString("0.00") + " -> " + shieldBox2.ToString("0.00") + " m, region 'shield' = " + Perm(ja, "shield"));
                r.Check(ja.Shield <= 0f && shieldBox2 < shieldBox * 0.6f, "the jackal's shield region goes with its shield (its hit box shrinks to the frame)");
                if (jl)
                {
                    bool kept = ij != null && ij.Alive && ij.Latched && ij.LatchTarget == ja && ij.transform.IsChildOf(ja.transform) && ij.transform.parent != ja.transform;
                    HaloLabRunner.Log("  latched form after the rebuild: " + (ij == null ? "gone" : "alive " + ij.Alive + ", latched " + ij.Latched + ", parent " + (ij.transform.parent != null ? ij.transform.parent.name : "-")
                                      + ", " + (ij.Center - ja.Center).magnitude.ToString("0.00") + " m from the jackal's centre"));
                    r.Check(kept && (ij.Center - ja.Center).magnitude < 1.5f, "a form latched onto the jackal stays latched on the rebuilt model (re-parented to its body marker)");
                }
                else HaloLabRunner.Log("  (the form did not latch onto the jackal: the rebuild case was not exercised)");
                r.Aim(new Vector3(-6f, 1.3f, 2.6f), ja.Center, 50f);
                yield return Seconds(0.2f);
                r.Shot("jackal_shield_down");
                float b0 = ja.Body;
                Hit(ja, 10f, front);
                r.Check(ja.Body < b0, "a bullet then hurts the jackal (" + b0.ToString("0.00") + " -> " + ja.Body.ToString("0.00") + ")");
                HaloBipeds.Erase(ja);
                if (ij != null) HaloBipeds.Erase(ij);

                // ---- 7. melee reach: an elite at 7 m misses (its pistol melee lunges about 1 wu: Halo's), at 1.2 m hits
                HaloUnit el = HaloBipeds.Spawn(elite, new Vector3(10f, 0f, 0f), 0f, HaloTeam.Covenant);
                HaloUnit target = HaloBipeds.Spawn(grunt, new Vector3(10f, 0f, 7f), 180f, HaloTeam.Human);
                yield return Seconds(0.5f);
                float t0 = target.Body;
                el.Melee = true;
                yield return Seconds(2f);
                float t1 = target.Body;
                target.Teleport(new Vector3(10f, 0f, el.transform.position.z + 1.2f), 180f);
                yield return Seconds(0.3f);
                el.Melee = true;
                yield return Seconds(2f);
                float t2 = target != null ? target.Body : 0f;
                HaloLabRunner.Log("  elite melee: at 7 m " + t0.ToString("0.00") + " -> " + t1.ToString("0.00") + ", at 1.2 m -> " + t2.ToString("0.00") + " (elite moved " + (el.transform.position - new Vector3(10f, 0f, 0f)).magnitude.ToString("0.00") + " m by its lunge)");
                r.Check(t1 >= t0 - 0.001f, "an elite 7 m away does not hit (contact reach, not the actv melee range)");
                r.Check(target == null || t2 < t1, "an elite 1.2 m away hits");
                r.Check(target == null || el.transform.position.z < target.transform.position.z, "the elite's lunge stops at the grunt (does not pass through it)");
                HaloBipeds.Erase(el); if (target != null) HaloBipeds.Erase(target);

                // ---- 7b. a blow never lands on the attacker's own side: a combat form swinging with a swarm-mate (an infection form) right in
                //          front of it - before, MeleeHit struck the first hit box of ANY team and popped the form
                HaloUnit cf = HaloBipeds.Spawn(human, new Vector3(14f, 0f, 0f), 0f, HaloTeam.Flood);
                HaloUnit mate = HaloBipeds.Spawn(infection, new Vector3(14f, 0f, 1.0f), 180f, HaloTeam.Flood);
                yield return Seconds(0.5f);
                int skipped0 = HaloUnit.MeleeSkippedAllies, destroyedBefore = destroyed.Count;
                float m0 = mate != null ? mate.Body : 0f;
                if (cf != null) cf.Melee = true;
                yield return Seconds(2f);
                int skipped = HaloUnit.MeleeSkippedAllies - skipped0;
                HaloLabRunner.Log("  flood melee with a swarm-mate in front: mate " + (mate != null ? (mate.Alive ? "alive, body " + mate.Body.ToString("0.00") : "dead") : "gone") + ", allies passed over " + skipped);
                r.Check(cf != null && mate != null && mate.Alive && mate.Body >= m0 - 0.001f && skipped > 0, "a combat form's blow passes over the infection form in front of it (same team; " + skipped + " ally hit box(es) skipped)");
                if (cf != null) HaloBipeds.Erase(cf); if (mate != null) HaloBipeds.Erase(mate);

                // ---- 8. a grunt's grenade at the variant's velocity per tick
                HaloUnit gr = HaloBipeds.Spawn(grunt, new Vector3(0f, 0f, -12f), 0f, HaloTeam.Covenant);
                yield return Seconds(0.5f);
                if (gr.Grenades <= 0) gr.SetGrenadeCount(1);
                int g0n = gr.Grenades;
                gr.AimPoint = gr.transform.position + Vector3.forward * 10f;
                gr.ThrowGrenade = true;
                // follow the grenade to its first touch-down (first bounce / rest / stick): Halo's toss lands on the aim point
                Vector3 aim = gr.AimPoint, land = Vector3.zero;
                bool landed = false;
                float until = Time.time + 3f;
                while (Time.time < until && !landed)
                {
                    HaloProjectiles.Flight fl = HaloProjectiles.Find(x => x.GrenadeKind >= 0);
                    if (fl != null && (fl.Bounced || fl.AtRest || fl.Stuck)) { landed = true; land = fl.Position; }
                    yield return null;
                }

                Vector3 dl = land - aim; dl.y = 0f;
                HaloLabRunner.Log("  grenade first touch-down " + (landed ? land.ToString("F2") + ", " + dl.magnitude.ToString("0.00") + " m from the aim point " + aim.ToString("F2") : "not seen"));
                r.Check(landed && dl.magnitude < 2f, "the grunt's grenade lands within 2 m of the aim point");
                yield return Seconds(0.5f);
                float want = gr.Def.VariantGrenadeVelocity * 30f * HaloSpace.Unit;
                HaloLabRunner.Log("  grunt grenade velocity " + gr.Def.VariantGrenadeVelocity + " wu/tick = " + want.ToString("0.0") + " m/s, grenades " + g0n + " -> " + gr.Grenades);
                r.Check(gr.Grenades < g0n && want > 8f, "the grunt throws at the actv grenade velocity x 30 (about 11.9 m/s; see 'threw' in the log)");
                HaloBipeds.Erase(gr);
            }
            finally { HaloBipeds.Destroyed -= onDestroyed; }
        }

        private static Vector3 ShieldBoxCenter(HaloUnit u, Vector3 fallback)
        {
            for (int i = 0; i < u.Hitboxes.Count; i++)
            {
                HaloUnit.Hitbox h = u.Hitboxes[i];
                if (h.Box != null && h.Box.enabled && h.Box.name.IndexOf("shield", System.StringComparison.OrdinalIgnoreCase) >= 0) return h.Box.ClosestPoint(fallback);      // (the plate's face toward the shooter, as a ray hits it)
            }

            return fallback;
        }

        private static float ShieldBoxSize(HaloUnit u)
        {
            float best = 0f;
            for (int i = 0; i < u.Hitboxes.Count; i++)
            {
                HaloUnit.Hitbox h = u.Hitboxes[i];
                if (h.Box == null || h.Box.name.IndexOf("shield", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                Vector3 s = Vector3.Scale(h.Box.size, h.Box.transform.lossyScale);
                best = Mathf.Max(best, Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
            }

            return best;
        }

        private static string Perm(HaloUnit u, string region) { string p; return u.Permutations.TryGetValue(region, out p) ? p : "-"; }
    }
}

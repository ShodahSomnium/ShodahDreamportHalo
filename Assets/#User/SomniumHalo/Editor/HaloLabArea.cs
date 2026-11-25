using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Lab "area-damage" (-halomap d20 by default: grunts, flood infection forms and carriers): workstream 3's glue between Combat and Effects.
    /// 1. A frag grenade goes off 1.5 m from three grunts (HaloProjectiles.Detonate, the player's): they take area hits (IsArea, the centre,
    ///    the acceleration) and are pushed (HaloUnit.AddImpulse - "Combat: impulse" lines); pictures area_frag_*.png while they fly; after 3 s
    ///    their feet are on the floor (not under it). The frag's detonation effect is a projectile effect (Effects must not apply it again).
    /// 2. Infection form pop: the infection form's own body destroyed jpt! ("infection form pop") through HaloCombat.AreaDamage next to two
    ///    infection forms kills both outright (the chain).
    /// 3. Carrier burst through Effects: the carrier's coll body destroyed effect played by HaloCombatFx.Play with the carrier as creator -
    ///    HaloFxRuntime applies its "bdoy destroyed" jpt! (HaloCombat.EffectDamage): a grunt 1.2 m away is hurt, an infection form 1 m away
    ///    pops, a grenade lying 1 m away is detonated ("detonates explosives"). Picture area_carrier_burst.png.
    /// 4. Recent flights: a pistol bullet in flight shows in HaloProjectiles.CopyRecentFlights (with its AI perception radius).
    /// 5. (Info only, workstream 2) a carrier killed outright: does it burst by itself (HaloBipeds.Destroyed)?
    /// "LAB: area ..." lines; Player.log lines "Combat: area damage ... hit n units", "Combat: impulse ...", "Combat: effect ... applies its damage part".
    /// </summary>
    [InitializeOnLoadAttribute]
    static class HaloLabArea
    {
        static HaloLabArea()
        {
            HaloLabRunner.Register("area-damage", r => Run(r));
        }

        private static HaloMap _map;

        /// <summary>A player stand-in (a hit box like a remote player's on the world owner): records the area hits it takes.</summary>
        sealed class PlayerStandIn : MonoBehaviour, IHaloDamageable
        {
            public float Taken, CarrierTaken; public int Hits; public bool Area; public HaloTag Effect;
            public void TakeHit(HaloHit hit) { Taken += hit.Amount; Hits++; Area |= hit.IsArea; Effect = hit.Effect; if (hit.Effect != null && hit.Effect.Name.IndexOf("floodcarrier", System.StringComparison.OrdinalIgnoreCase) >= 0) CarrierTaken += hit.Amount; }
        }

        private static IEnumerator Seconds(float s)
        {
            float end = Time.time + s;
            while (Time.time < end) yield return null;
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
            var stage = new GameObject("area lab");
            HaloBipeds.LabParent = stage.transform;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "floor"; floor.transform.SetParent(stage.transform, false);
            floor.transform.localScale = new Vector3(30f, 1f, 30f);
            floor.layer = HaloLayers.World;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f));
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            var grid = new Material(mat); grid.SetColor("_BaseColor", new Color(0.3f, 0.32f, 0.35f));
            for (int k = -10; k <= 10; k++)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(line.GetComponent<Collider>());
                line.transform.SetParent(stage.transform, false);
                line.transform.position = new Vector3(k * 1f, 0.005f, 0f); line.transform.localScale = new Vector3(0.02f, 0.01f, 40f);
                line.GetComponent<Renderer>().sharedMaterial = grid;
            }

            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.4f, 0.42f, 0.46f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            Physics.SyncTransforms();
            yield return null;
        }

        private static HaloTag Variant(string part)
        {
            foreach (HaloTag v in _map.OfClass("actv"))
                if (v.Name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) return v;
            foreach (HaloTag b in _map.OfClass("bipd"))
                if (b.Name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) return b;
            return null;
        }

        private static float Feet(HaloUnit u) { return u != null ? u.transform.position.y : 0f; }

        private static IEnumerator Run(HaloLabRunner r)
        {
            yield return Stage(r);
            if (r.Failed) yield break;
            var cat = HaloCombat.Catalog;
            if (!r.Check(cat != null && cat.Grenades.Count > 0, "the combat catalog with the globals' grenades")) yield break;

            // ---------------------------------------------------------------- 1. frag grenade among three grunts
            HaloTag grunt = Variant("grunt") ?? Variant("floodcombat_human");
            if (!r.Check(grunt != null, "a grunt (or flood human) in " + _map.Name)) yield break;
            var grunts = new List<HaloUnit>();
            Vector3 blast = new Vector3(0f, 0.15f, 0f);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                grunts.Add(HaloBipeds.Spawn(grunt, new Vector3(Mathf.Cos(a) * 1.5f, 0f, Mathf.Sin(a) * 1.5f), i * 120f, HaloTeam.Covenant));
            }

            int areaHits = 0;
            HaloHit lastArea = default(HaloHit);
            System.Action<HaloUnit, HaloHit> onDamaged = (u, h) => { if (h.IsArea) { areaHits++; lastArea = h; } };
            HaloBipeds.Damaged += onDamaged;
            yield return Seconds(1f);
            float[] y0 = new float[3];
            Vector3[] p0 = new Vector3[3];
            for (int i = 0; i < 3; i++) { y0[i] = Feet(grunts[i]); p0[i] = grunts[i].transform.position; }
            HaloLabRunner.Log("area: " + grunt.Name + " acceleration scale " + grunts[0].Def.AccelerationScale.ToString("0.##") + ", feet y " + y0[0].ToString("0.00"));
            r.Aim(new Vector3(0f, 2.2f, -6f), new Vector3(0f, 0.8f, 0f), 60f);
            r.Shot("area_frag_before", 960, 540);

            var fragDef = cat.Projectile(cat.Grenades[0].Projectile);
            if (!r.Check(fragDef != null, "the frag grenade projectile")) yield break;
            var frag = HaloProjectiles.Launch(fragDef, blast, Vector3.down, Vector3.down * 0.5f, 0, cat.Grenades[0].Equipment, true, true, null, null);
            if (!r.Check(frag != null, "a frag grenade launched")) yield break;
            HaloTag fragFx = fragDef.DetonationEffect;
            r.Check(fragFx == null || HaloCombat.IsProjectileEffect(fragFx), "the frag detonation effect is a projectile effect (Effects leaves its damage to the projectile)");
            HaloProjectiles.Detonate(frag, blast, Vector3.up, -1, null);
            float[] peak = new float[3];
            float[] moved = new float[3];
            for (int f = 0; f < 60; f++)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (grunts[i] == null) continue;
                    peak[i] = Mathf.Max(peak[i], Feet(grunts[i]) - y0[i]);
                    Vector3 d = grunts[i].transform.position - p0[i]; d.y = 0f;
                    moved[i] = Mathf.Max(moved[i], d.magnitude);
                }

                if (f == 6) r.Shot("area_frag_0.1s", 960, 540);
                if (f == 18) r.Shot("area_frag_0.3s", 960, 540);
                yield return null;
            }

            HaloLabRunner.Log("area: frag area hits " + areaHits + " (last: centre " + lastArea.ExplosionCenter + ", acceleration " + lastArea.InstantAcceleration.ToString("0.##") + ")");
            int pushed = 0;
            for (int i = 0; i < 3; i++)
            {
                HaloLabRunner.Log("area: grunt " + i + " peak +" + peak[i].ToString("0.00") + " m, moved " + moved[i].ToString("0.00") + " m, alive " + (grunts[i] != null && grunts[i].Alive));
                if (peak[i] > 0.2f || moved[i] > 0.4f) pushed++;
            }

            r.Check(areaHits >= 3 || CountDead(grunts) >= 3, "the frag's area damage reached all three grunts (IsArea hits)");
            r.Check(lastArea.ExplosionCenter == Vector3.zero || (lastArea.ExplosionCenter - blast).magnitude < 0.01f, "IsArea hits carry the explosion centre");
            r.Check(pushed >= 2, "the blast threw the grunts (AddImpulse: peak height / horizontal travel)");
            yield return Seconds(3f);
            r.Shot("area_frag_after", 960, 540);
            bool onFloor = true;
            for (int i = 0; i < 3; i++) if (grunts[i] != null && Feet(grunts[i]) < -0.1f) onFloor = false;
            r.Check(onFloor, "the thrown grunts (and bodies) lie on the floor, not under it");
            for (int i = 0; i < 3; i++) if (grunts[i] != null) HaloBipeds.Erase(grunts[i]);
            yield return null;

            // ---------------------------------------------------------------- 2. infection form pop (the chain)
            HaloTag infection = null;
            foreach (HaloTag v in _map.OfClass("actv"))
                if (v.Name.EndsWith(@"flood_infection\flood_infection", System.StringComparison.OrdinalIgnoreCase)) { infection = v; break; }
            if (infection == null) infection = Variant("flood_infection");
            if (r.Check(infection != null, "the flood infection form variant"))
            {
                HaloUnit i1 = HaloBipeds.Spawn(infection, new Vector3(-0.3f, 0f, 0f), 0f, HaloTeam.Flood);
                HaloUnit i2 = HaloBipeds.Spawn(infection, new Vector3(0.3f, 0f, 0.2f), 0f, HaloTeam.Flood);
                yield return Seconds(0.5f);
                HaloTag popFx = i1.Def.Coll != null ? i1.Def.Coll.BodyDestroyedEffect : null;
                List<HaloTag> parts = popFx != null ? cat.Blast(popFx) : null;
                HaloTag popJpt = parts != null && parts.Count > 0 ? parts[0] : null;
                var pd = cat.Damage(popJpt);
                HaloLabRunner.Log("area: infection body destroyed effect " + (popFx != null ? popFx.Name : "-") + ", jpt " + (popJpt != null ? popJpt.Name : "-") + (pd != null ? " pop " + pd.InfectionFormPop + " only one " + pd.OnlyHurtsOneInfectionForm + " radius " + pd.RadiusMin + ".." + pd.RadiusMax : "") +
                                  ", infection flags dies-on-shields " + i1.Def.ImpactMeleeDiesOnShields + " attaches " + i1.Def.ImpactMeleeAttachesToUnit);
                if (r.Check(pd != null && pd.InfectionFormPop, "the infection form's pop jpt! has 'infection form pop'"))
                {
                    HaloCombat.AreaDamage(popJpt, new Vector3(0f, 0.2f, 0.1f), -1, null, null);
                    yield return Seconds(0.2f);
                    r.Check((i1 == null || !i1.Alive) && (i2 == null || !i2.Alive), "the pop kills both neighbouring infection forms outright");
                }

                yield return Seconds(0.5f);
                if (i1 != null) HaloBipeds.Erase(i1);
                if (i2 != null) HaloBipeds.Erase(i2);
                yield return null;
            }

            // ---------------------------------------------------------------- 3. carrier burst through Effects
            HaloTag carrier = Variant("floodcarrier");
            if (r.Check(carrier != null, "the flood carrier variant") && grunt != null)
            {
                HaloUnit c = HaloBipeds.Spawn(carrier, new Vector3(0f, 0f, 0f), 0f, HaloTeam.Flood);
                HaloUnit g = HaloBipeds.Spawn(grunt, new Vector3(1.2f, 0f, 0f), 270f, HaloTeam.Covenant);
                HaloUnit inf = infection != null ? HaloBipeds.Spawn(infection, new Vector3(-1.0f, 0f, 0f), 90f, HaloTeam.Flood) : null;
                var standInGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                standInGo.name = "stand-in player"; standInGo.layer = HaloLayers.Hitbox;
                standInGo.transform.position = new Vector3(0f, 0.9f, -2.0f); standInGo.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
                var standIn = standInGo.AddComponent<PlayerStandIn>();
                var lying = HaloProjectiles.Launch(fragDef, new Vector3(0f, 0.1f, 1.0f), Vector3.down, Vector3.down * 0.1f, -1, cat.Grenades[0].Equipment, true, false, null, null);
                yield return Seconds(1f);
                float gBody = g.Body + g.Shield;
                HaloTag burst = c.Def.Coll != null ? c.Def.Coll.BodyDestroyedEffect : null;
                HaloLabRunner.Log("area: carrier body destroyed effect " + (burst != null ? burst.Name : "-") + ", projectile effect " + HaloCombat.IsProjectileEffect(burst) + ", grenade lying " + (lying != null && lying.Alive));
                r.Aim(new Vector3(0f, 2.5f, -5f), new Vector3(0f, 0.6f, 0f), 60f);
                if (r.Check(burst != null, "the carrier's coll has a body destroyed effect"))
                {
                    HaloCombatFx.Play(burst, c.Center, c.transform.rotation, null, c);
                    yield return null;
                    yield return null;
                    r.Shot("area_carrier_burst", 960, 540);
                    yield return Seconds(0.5f);
                    HaloLabRunner.Log("area: grunt vitality " + gBody.ToString("0.00") + " -> " + (g != null ? (g.Body + g.Shield).ToString("0.00") + (g.Alive ? "" : " (dead)") : "gone") + ", infection " + (inf == null ? "gone" : inf.Alive ? "alive" : "dead") + ", grenade " + (lying != null && lying.Alive ? "still lying" : "detonated"));
                    r.Check(g == null || !g.Alive || g.Body + g.Shield < gBody, "the carrier burst (Effects' jpt! part) hurts a grunt 1.2 m away");
                    r.Check(inf == null || !inf.Alive, "the carrier burst pops an infection form 1 m away");
                    r.Check(lying == null || !lying.Alive, "the carrier burst detonates a grenade 1 m away (detonates explosives)");
                    HaloLabRunner.Log("area: player stand-in 2 m from the carrier: " + standIn.Hits + " hit(s), " + standIn.Taken.ToString("0.0") + " damage (carrier burst " + standIn.CarrierTaken.ToString("0.0") + "), area " + standIn.Area + ", last " + (standIn.Effect != null ? standIn.Effect.Name : "-"));
                    r.Check(standIn.CarrierTaken > 0f, "the carrier burst hurts a player 2 m away");
                }

                yield return Seconds(0.5f);
                if (c != null) HaloBipeds.Erase(c);
                if (g != null) HaloBipeds.Erase(g);
                if (inf != null) HaloBipeds.Erase(inf);
                Object.Destroy(standInGo);
                yield return null;
            }

            // ---------------------------------------------------------------- 4. recent flights (AI projectile noticing)
            {
                HaloTag pistol = null;
                foreach (HaloTag w in _map.OfClass("weap")) if (w.Name == @"weapons\pistol\pistol" || w.Name.EndsWith(@"\plasma rifle")) { pistol = w; break; }
                HaloWeaponDef wd = pistol != null ? cat.Weapon(pistol) : null;
                if (r.Check(wd != null && wd.PrimaryTrigger != null, "a weapon with a projectile for the flight ring"))
                {
                    HaloCombat.FireProjectile(wd.PrimaryTrigger.Projectile, new Vector3(-10f, 1f, 5f), Vector3.zero, 0, pistol, null, Vector3.right);
                    yield return null;
                    yield return null;
                    var from = new Vector3[8]; var to = new Vector3[8]; var team = new HaloTeam[8]; var rad = new float[8];
                    int n = HaloProjectiles.CopyRecentFlights(from, to, team, rad, 8);
                    HaloLabRunner.Log("area: recent flights " + n + (n > 0 ? ": " + from[0] + " -> " + to[0] + " team " + team[0] + " perception radius " + rad[0].ToString("0.##") + " wu" : ""));
                    r.Check(n >= 1, "a flight in the air shows in CopyRecentFlights");
                    yield return Seconds(0.6f);
                    int later = HaloProjectiles.CopyRecentFlights(from, to, team, rad, 8);
                    HaloLabRunner.Log("area: recent flights 0.6 s later " + later);
                }
            }

            // ---------------------------------------------------------------- 5. info: a carrier killed outright (workstream 2's destroyed after dying)
            if (carrier != null)
            {
                int destroyed = 0;
                System.Action<HaloUnit> onDestroyed = u => destroyed++;
                HaloBipeds.Destroyed += onDestroyed;
                HaloUnit c2 = HaloBipeds.Spawn(carrier, new Vector3(0f, 0f, 3f), 0f, HaloTeam.Flood);
                yield return Seconds(0.5f);
                c2.TakeHit(new HaloHit { Amount = 1000f, SourcePlayer = 0, Point = c2.Center, Direction = Vector3.forward, Instant = true });
                yield return Seconds(2f);
                HaloLabRunner.Log("area: (info, workstream 2) carrier killed outright: destroyed events " + destroyed + ", object " + (c2 == null ? "gone" : "still there"));
                HaloBipeds.Destroyed -= onDestroyed;
                if (c2 != null) HaloBipeds.Erase(c2);
            }

            HaloBipeds.Damaged -= onDamaged;
        }

        private static int CountDead(List<HaloUnit> l)
        {
            int n = 0;
            for (int i = 0; i < l.Count; i++) if (l[i] == null || !l[i].Alive) n++;
            return n;
        }
    }
}

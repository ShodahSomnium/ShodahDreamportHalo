using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Combat lab ("combat"): opens a map (-halomap, default bloodgulch) WITHOUT a match, lays a flat floor, stands a rig (feet, a head
    /// camera, two simulated hands driven through HaloXr.Simulated / HaloBody overrides) and two dummies on the hit-box layer, then:
    /// spawns (the map's starting weapons on the body), pictures of the holsters, draws the pistol from the right hip and fires, drops the
    /// magazine (B), takes one from the pouch, seats it, racks; holsters; draws the assault rifle over the shoulder, two hands, fires a
    /// burst; melees a dummy from behind and from the front; throws a frag and a plasma grenade; drops the rifle and takes a shotgun from
    /// the floor (swap), walks over a pistol (ammunition) and an over shield; takes damage (shields, then body); overheats a plasma rifle;
    /// then monitor play on simulated keys (fire, reload, grenade, melee). Pictures in DevTools/lab/combat/. "combat-quick": up to the rifle.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabCombat
    {
        static HaloLabCombat()
        {
            HaloLabRunner.Register("combat", r => Run(r, true));
            HaloLabRunner.Register("combat-quick", r => Run(r, false));
            HaloLabRunner.Register("combat-reload", r => RunReload(r));
            HaloLabRunner.Register("combat-ai", r => RunAi(r));
            HaloLabRunner.Register("combat-glass", r => RunGlass(r));
        }

        sealed class Dummy : MonoBehaviour, IHaloDamageable, IHaloHitInfo
        {
            public int Hits, Heads; public float Amount; public bool Instant, Melee, Explosion;
            public float Shield = 75f;
            public void TakeHit(HaloHit hit)
            {
                // the victim decides the instant kill (as HaloVitals does for a player): Instant, or a melee from behind (BackHit)
                Hits++; Amount += hit.Amount; Instant |= hit.Instant || (hit.Melee && hit.BackHit); Melee |= hit.Melee; Explosion |= hit.Explosion;
                if (hit.Headshot) Heads++;
                Shield = Mathf.Max(0f, Shield - hit.Amount);
            }
            public int HitSlot => 7;
            public bool HitIsHead(Collider c) { return c != null && c.name == "head"; }
            public Vector3 HitFacing => transform.forward;
            public int HitMaterial(Vector3 p) { return Shield > 0f ? HaloDamageDef.CyborgEnergyShield : HaloDamageDef.CyborgArmor; }
            public bool HitAlive => true;
            public void Clear() { Hits = Heads = 0; Amount = 0f; Instant = Melee = Explosion = false; Shield = 75f; }
            public string State() { return name + ": " + Hits + " hits, " + Amount.ToString("0") + " damage" + (Heads > 0 ? ", " + Heads + " head" : "") + (Instant ? ", INSTANT" : "") + (Melee ? ", melee" : "") + (Explosion ? ", blast" : ""); }
        }

        static Transform _rig, _head, _lh, _rh;
        static Camera _eye;
        static HaloHandling H => HaloHandling.Current;
        static readonly HashSet<string> Keys = new HashSet<string>();

        static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

        static void Grip(bool right, float v) { if (right) HaloXr.SimRight.Grip = v; else HaloXr.SimLeft.Grip = v; }
        static void Trig(bool right, float v) { if (right) HaloXr.SimRight.Trigger = v; else HaloXr.SimLeft.Trigger = v; }

        static IEnumerator Press(bool right, bool top, float seconds)
        {
            if (right) { if (top) HaloXr.SimRight.Secondary = true; else HaloXr.SimRight.Primary = true; }
            else { if (top) HaloXr.SimLeft.Secondary = true; else HaloXr.SimLeft.Primary = true; }
            yield return Seconds(seconds);
            HaloXr.SimRight.Primary = HaloXr.SimRight.Secondary = HaloXr.SimLeft.Primary = HaloXr.SimLeft.Secondary = false;
        }

        static Quaternion Face => _rig.rotation;

        /// <summary>A smooth move of a hand (world target, aim-frame rotation).</summary>
        static IEnumerator Move(Transform hand, Vector3 to, Quaternion rot, float seconds)
        {
            Vector3 p0 = hand.position; Quaternion r0 = hand.rotation;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
                hand.SetPositionAndRotation(Vector3.Lerp(p0, to, u), Quaternion.Slerp(r0, rot, u));
                yield return null;
            }

            hand.SetPositionAndRotation(to, rot);
        }

        /// <summary>A straight move at constant speed; <paramref name="releaseAtEnd"/> lets go of the grip on the last frame (a throw).</summary>
        static IEnumerator Fling(Transform hand, Vector3 velocity, float seconds, bool right, bool releaseAtEnd)
        {
            float t = 0f;
            while (t < seconds)
            {
                float dt = Time.deltaTime;
                t += dt;
                hand.position += velocity * dt;
                if (releaseAtEnd && t >= seconds) Grip(right, 0f);
                yield return null;
            }
        }

        static string Guns()
        {
            var h = H;
            if (h == null) return "no handling";
            string s = "";
            for (int i = 0; i < 2; i++)
            {
                HaloGun g = h.Guns[i];
                s += "[" + i + "] " + (g == null ? "-" : g.Def.Name + " " + g.State.Loaded + "/" + g.State.Reserve + " " + g.Slot + (g.MagIn ? "" : " MAG OUT") + (g.NeedsRack ? " NEEDS RACK" : "") + (g.State.Overheated ? " OVERHEATED" : "") + (g.Def.UsesBattery ? " batt " + g.State.Battery.ToString("0.00") : "")) + "  ";
            }

            return s + "held " + (h.HeldGun != null ? h.HeldGun.Def.Name : "none") + (h.TwoHanded ? " (two hands)" : "") + " | frags " + HaloLocal.FragGrenades + " plasma " + HaloLocal.PlasmaGrenades
                   + " | shield " + HaloLocal.Shield.ToString("0.00") + " health " + HaloLocal.Health.ToString("0.00") + (HaloLocal.Alive ? "" : " DEAD");
        }

        static Dummy MakeDummy(string name, Vector3 at, float yaw)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.layer = HaloLayers.Hitbox;
            go.transform.position = at + Vector3.up * 0.95f;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(0.6f, 0.95f, 0.6f);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "head";
            head.layer = HaloLayers.Hitbox;
            head.transform.SetParent(go.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            head.transform.localScale = new Vector3(0.45f, 0.27f, 0.45f);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.7f, 0.25f, 0.2f));
            go.GetComponent<Renderer>().sharedMaterial = m;
            head.GetComponent<Renderer>().sharedMaterial = m;
            // a nose to show where it faces
            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(nose.GetComponent<Collider>());
            nose.transform.SetParent(go.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.95f, 0.45f);
            nose.transform.localScale = new Vector3(0.1f, 0.06f, 0.2f);
            return go.AddComponent<Dummy>();
        }

        static void Marker(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 scale, Color c)
        {
            var g = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = scale;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        static void Body(HaloLabRunner r, string name, Vector3 offsetLocal, float fov = 50f)
        {
            Vector3 centre = _rig.position + Vector3.up * 1.15f;
            r.Aim(_rig.TransformPoint(offsetLocal), centre, fov);
            r.Shot(name);
        }

        static void FirstPerson(HaloLabRunner r, string name, Vector3 lookAt, float fov = 80f)
        {
            r.Aim(_head.position, lookAt, fov);
            r.Shot(name);
        }

        static IEnumerator Run(HaloLabRunner r, bool full)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "bloodgulch");
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            var map = new HaloMap(entry.File);
            HaloCombat.LabMap = map;
            HaloCombat.LabScenario = new HaloScenario(map);
            Time.captureFramerate = 60;
            try { HaloFx.Preload(map); } catch (System.Exception e) { HaloLabRunner.Log("fx preload: " + e.Message); }

            // the stage: a floor at y 0, the rig at the origin facing +Z, two dummies
            var stage = new GameObject("combat lab");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "floor"; floor.transform.SetParent(stage.transform, false);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            floor.layer = HaloLayers.World;
            var fm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            fm.SetColor("_BaseColor", new Color(0.42f, 0.44f, 0.4f)); fm.SetFloat("_Smoothness", 0.05f);
            floor.GetComponent<Renderer>().sharedMaterial = fm;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

            _rig = new GameObject("Lab Rig").transform;
            _rig.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var camGo = new GameObject("Lab Eye");
            camGo.tag = "MainCamera";
            _eye = camGo.AddComponent<Camera>();
            _eye.nearClipPlane = 0.03f; _eye.fieldOfView = 80f;
            _head = camGo.transform;
            _head.SetParent(_rig, false);
            _head.localPosition = new Vector3(0f, 1.65f, 0f);
            _lh = new GameObject("Lab Left Hand").transform; _lh.SetParent(_rig, false);
            _rh = new GameObject("Lab Right Hand").transform; _rh.SetParent(_rig, false);
            _lh.localPosition = new Vector3(-0.22f, 0.95f, 0.15f);
            _rh.localPosition = new Vector3(0.22f, 0.95f, 0.15f);
            Marker(_rig, PrimitiveType.Capsule, new Vector3(0f, 1.1f, 0f), new Vector3(0.34f, 0.36f, 0.22f), new Color(0.25f, 0.3f, 0.35f));
            Marker(_head, PrimitiveType.Sphere, new Vector3(0f, 0.02f, -0.04f), new Vector3(0.19f, 0.22f, 0.21f), new Color(0.6f, 0.5f, 0.45f));
            Marker(_lh, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.06f, new Color(0.2f, 0.4f, 1f));
            Marker(_rh, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.06f, new Color(1f, 0.3f, 0.2f));
            Dummy far = MakeDummy("far dummy", new Vector3(0f, 0f, 8f), 180f);
            Dummy near = MakeDummy("near dummy", new Vector3(0.95f, 0f, 1.0f), 90f);   // faces +X: the swing comes from its back

            HaloXr.Simulated = true;
            HaloXr.SimLeft = default(HaloHand); HaloXr.SimRight = default(HaloHand);

            // ---- spawn
            HaloCombat.Spawned();
            var h = H;
            if (!r.Check(h != null, "the combat driver exists")) yield break;
            h.Body.OverrideRoot = _rig; h.Body.OverrideHead = _head; h.Body.OverrideLeft = _lh; h.Body.OverrideRight = _rh;
            HaloCombat.Spawned();      // (again, now with the rig's body: everything sized to it)
            yield return Seconds(0.5f);
            HaloLabRunner.Log("spawned: " + Guns());
            r.Check(h.Carried == 2, "two weapons carried at spawn");
            r.Check(!h.Desktop, "VR mode (simulated hands)");
            HaloLabRunner.Log("zones: back R " + h.ZoneCenter(HaloHandling.Zone.BackRight) + " hip R " + h.ZoneCenter(HaloHandling.Zone.HipRight) + " frags " + h.ZoneCenter(HaloHandling.Zone.Frags) + " pouch " + h.ZoneCenter(HaloHandling.Zone.Pouch));
            Body(r, "01_stowed_back", new Vector3(0.9f, 1.6f, -1.9f));
            Body(r, "02_stowed_front", new Vector3(-0.8f, 1.4f, 1.9f));

            // ---- the pistol from the right hip
            int pistol = h.Guns[0] != null && h.Guns[0].Def.OneHanded ? 0 : 1;
            HaloGun small = h.Guns[pistol], rifle = h.Guns[1 - pistol];
            yield return Move(_rh, h.ZoneCenter(HaloGun.Holster.HipRight == small.Slot ? HaloHandling.Zone.HipRight : HaloHandling.Zone.HipLeft), Face, 0.4f);
            Grip(true, 1f);
            yield return Seconds(0.15f);
            r.Check(h.HeldGun == small, "the right hand drew the " + small.Def.Name + " from the hip");
            Vector3 aimAt = far.transform.position + Vector3.up * 0.2f;
            Vector3 ready = _rig.position + new Vector3(0.12f, 1.42f, 0.42f);
            yield return Move(_rh, ready, Quaternion.LookRotation(aimAt - ready), 0.45f);
            yield return Seconds(0.2f);
            FirstPerson(r, "03_pistol_first_person", aimAt);
            Body(r, "04_pistol_side", new Vector3(1.4f, 1.6f, 0.6f), 45f);
            far.Clear();
            for (int i = 0; i < 5; i++) { Trig(true, 1f); yield return Seconds(0.05f); Trig(true, 0f); yield return Seconds(0.3f); }
            yield return Seconds(0.3f);
            HaloLabRunner.Log("after 5 pistol shots: " + Guns() + " | " + far.State());
            r.Check(far.Hits >= 3, "the pistol hit the far dummy (" + far.Hits + " of 5)");
            int loaded = small.State.Loaded;

            // ---- eject, pouch, seat, rack
            yield return Press(true, true, 0.1f);
            yield return Seconds(0.2f);
            r.Check(!small.MagIn, "B dropped the magazine");
            Body(r, "05_magazine_out", new Vector3(1.0f, 1.5f, 0.9f), 40f);
            yield return Move(_lh, h.ZoneCenter(HaloHandling.Zone.Pouch), Face, 0.45f);
            Grip(false, 1f);
            yield return Seconds(0.15f);
            Vector3 well = small.transform.TransformPoint(small.WellLocal);
            yield return Move(_lh, well, Quaternion.LookRotation(Vector3.up, -Vector3.forward), 0.5f);
            yield return Seconds(1.2f);
            r.Check(small.MagIn, "the magazine seated (" + small.State.Loaded + " rounds)");
            Grip(false, 0f);
            yield return Seconds(0.1f);
            if (small.NeedsRack)
            {
                Vector3 rack = small.transform.TransformPoint(small.RackLocal);
                yield return Move(_lh, rack, Face, 0.3f);
                Grip(false, 1f);
                yield return Seconds(0.1f);
                yield return Move(_lh, rack - small.transform.forward * 0.1f, Face, 0.2f);
                Grip(false, 0f);
                yield return Seconds(0.1f);
            }

            HaloLabRunner.Log("after the reload: " + Guns() + " (was " + loaded + ")");
            r.Check(small.MagIn && small.State.Loaded == small.State.LoadedMaximum && !small.NeedsRack, "the pistol is full and ready");
            yield return Move(_lh, _rig.TransformPoint(new Vector3(-0.22f, 0.95f, 0.15f)), Face, 0.3f);

            // ---- holster, the rifle over the shoulder, two hands
            yield return Move(_rh, h.ZoneCenter(HaloHandling.ZoneOf(small.Slot)), Face, 0.45f);
            Grip(true, 0f);
            yield return Seconds(0.2f);
            r.Check(h.HeldGun == null && small.Slot != HaloGun.Holster.None && h.IndexOf(small) >= 0, "the pistol went back into its holster");
            yield return Move(_rh, h.ZoneCenter(HaloHandling.ZoneOf(rifle.Slot)), Face, 0.5f);
            Grip(true, 1f);
            yield return Seconds(0.15f);
            r.Check(h.HeldGun == rifle, "the right hand drew the " + rifle.Def.Name + " over the shoulder");
            Vector3 shoulder = _rig.position + new Vector3(0.1f, 1.45f, 0.25f);
            yield return Move(_rh, shoulder, Quaternion.LookRotation(aimAt - shoulder), 0.45f);
            yield return Seconds(0.1f);
            Vector3 sup = rifle.transform.TransformPoint(rifle.SupportLocal);
            yield return Move(_lh, sup, Quaternion.LookRotation(aimAt - sup), 0.4f);
            Grip(false, 1f);
            yield return Seconds(0.2f);
            r.Check(h.TwoHanded, "the left hand took the handguard (two hands)");
            // swing the support hand a little to the left: the barrel follows the line between the hands
            Vector3 fwd0 = rifle.transform.forward;
            yield return Move(_lh, sup + Vector3.left * 0.08f, _lh.rotation, 0.3f);
            yield return Seconds(0.15f);
            float turned = Vector3.Angle(fwd0, rifle.transform.forward);
            r.Check(turned > 3f, "two-handed aim follows the support hand (" + turned.ToString("0.0") + " degrees)");
            yield return Move(_lh, sup, _lh.rotation, 0.25f);
            yield return Seconds(0.2f);
            Body(r, "06_rifle_two_hands", new Vector3(1.3f, 1.6f, 0.9f), 45f);
            far.Clear();
            Trig(true, 1f);
            yield return Seconds(0.25f);
            FirstPerson(r, "07_rifle_firing", aimAt, 70f);
            yield return Seconds(0.75f);
            Trig(true, 0f);
            yield return Seconds(0.4f);
            HaloLabRunner.Log("after a 1 s burst: " + Guns() + " | " + far.State() + " | projectiles " + HaloProjectiles.Count);
            r.Check(far.Hits >= 5, "the rifle burst hit the far dummy (" + far.Hits + ")");
            if (!full) { Finish(); yield break; }

            // ---- melee: a swing into the near dummy's back, then its front
            Grip(false, 0f);
            yield return Seconds(0.1f);
            near.Clear();
            Vector3 start = _rig.position + new Vector3(0.15f, 1.3f, 0.45f);
            yield return Move(_rh, start, Quaternion.LookRotation(Vector3.right + Vector3.forward * 0.6f), 0.3f);
            yield return Seconds(1.4f);          // (the melee cooldown of the last swing, if any)
            yield return Fling(_rh, new Vector3(5f, 0f, 0.6f), 0.18f, true, false);
            yield return Seconds(0.2f);
            HaloLabRunner.Log("melee from behind: " + near.State());
            r.Check(near.Melee && near.Instant, "a melee hit from behind kills instantly");
            near.transform.rotation = Quaternion.Euler(0f, -90f, 0f);    // now it faces the player
            near.Clear();
            yield return Move(_rh, start, Quaternion.LookRotation(Vector3.right + Vector3.forward * 0.6f), 0.3f);
            yield return Seconds(1.5f);
            yield return Fling(_rh, new Vector3(5f, 0f, 0.6f), 0.18f, true, false);
            yield return Seconds(0.2f);
            HaloLabRunner.Log("melee from the front: " + near.State());
            r.Check(near.Melee && !near.Instant && near.Amount > 30f, "a melee hit from the front does the weapon's melee damage");
            near.transform.position += Vector3.right * 3f;      // out of the way

            // ---- grenades: a frag from the left chest, armed with the trigger, thrown with the hand
            yield return Move(_rh, shoulder, Quaternion.LookRotation(aimAt - shoulder), 0.3f);
            HaloLocal.PlasmaGrenades = 1;
            int frags = HaloLocal.FragGrenades;
            yield return Move(_lh, h.ZoneCenter(HaloHandling.Zone.Frags), Face, 0.45f);
            Grip(false, 1f);
            yield return Seconds(0.15f);
            r.Check(HaloLocal.FragGrenades == frags - 1, "the left hand took a frag grenade from the chest");
            Trig(false, 1f); yield return Seconds(0.1f); Trig(false, 0f);
            Body(r, "08_grenade_in_hand", new Vector3(-1.1f, 1.5f, 1.2f), 40f);
            far.Clear();
            _lh.position = _rig.position + new Vector3(-0.2f, 1.35f, 0.0f);
            yield return null;
            yield return Fling(_lh, new Vector3(0f, 2.8f, 4.6f), 0.15f, false, true);
            yield return Seconds(0.5f);
            r.Aim(_rig.position + new Vector3(3.5f, 2.2f, 2.5f), _rig.position + new Vector3(0f, 1f, 5f), 60f);
            r.Shot("09_frag_flight");
            HaloProjectiles.Flight frag = HaloProjectiles.Find(f => f.GrenadeKind == 0);
            HaloLabRunner.Log("frag in flight: " + (frag != null ? "at " + frag.Position + " v " + frag.Velocity.magnitude.ToString("0.0") + " bounced " + frag.Bounced : "none"));
            yield return Seconds(2.5f);
            HaloLabRunner.Log("after the frag: " + far.State() + " | local " + Guns());
            r.Check(far.Explosion, "the frag's blast hurt the far dummy");

            // the plasma grenade, thrown straight at the dummy: it sticks
            yield return Move(_lh, h.ZoneCenter(HaloHandling.Zone.Plasma), Face, 0.45f);
            Grip(false, 1f);
            yield return Seconds(0.15f);
            Trig(false, 1f); yield return Seconds(0.1f); Trig(false, 0f);
            far.Clear();
            _lh.position = _rig.position + new Vector3(-0.15f, 1.45f, 0.1f);
            yield return null;
            yield return Fling(_lh, new Vector3(0f, 1.2f, 8.0f), 0.15f, false, true);
            yield return Seconds(0.9f);
            HaloProjectiles.Flight plasma = HaloProjectiles.Find(f => f.GrenadeKind == 1);
            HaloLabRunner.Log("plasma: " + (plasma != null ? (plasma.Stuck ? "STUCK to " + (plasma.StuckTo != null ? plasma.StuckTo.name : "?") : "free at " + plasma.Position) : "gone"));
            r.Check(plasma != null && plasma.Stuck && plasma.StuckVictim != null, "the plasma grenade stuck to the dummy");
            r.Aim(far.transform.position + new Vector3(1.2f, 0.6f, -1.6f), far.transform.position, 45f);
            r.Shot("10_plasma_stuck");
            yield return Seconds(2.6f);
            HaloLabRunner.Log("after the plasma: " + far.State());
            r.Check(far.Explosion && far.Amount >= 100f, "the stuck plasma grenade did its attached damage and blast");

            // ---- drop the rifle, take a shotgun from the floor (two carried: swap), walk over ammunition and an over shield
            yield return Move(_rh, _rig.position + new Vector3(0.6f, 1.0f, 0.6f), Face, 0.3f);
            Grip(true, 0f);
            yield return Seconds(1.0f);
            r.Check(h.IndexOf(rifle) < 0 && h.Carried == 1, "letting go of the rifle away from the holsters dropped it");
            HaloPickups.Item lyingRifle = null;
            foreach (var it in HaloPickups.Items) if (it.Gun == rifle) lyingRifle = it;
            r.Check(lyingRifle != null && lyingRifle.Present && !lyingRifle.Falling, "the rifle lies on the floor as a pickup");
            HaloTag shotgun = HaloCombat.FindWeapon("shotgun"), sniper = HaloCombat.FindWeapon("sniper rifle"), pr = HaloCombat.FindWeapon("plasma rifle");
            var sg = HaloPickups.PlaceAt(shotgun, _rig.position + new Vector3(-0.5f, 0f, 0.9f), 90f);
            HaloPickups.PlaceAt(sniper, _rig.position + new Vector3(0.6f, 0f, 1.4f), 200f);
            HaloPickups.PlaceAt(pr, _rig.position + new Vector3(-0.9f, 0f, 1.6f), 30f);
            yield return Seconds(0.3f);
            r.Aim(_rig.position + new Vector3(0.3f, 1.9f, -0.4f), _rig.position + new Vector3(0f, 0f, 1.2f), 70f);
            r.Shot("11_weapons_on_the_floor");
            foreach (var it in HaloPickups.Items) if (it.Gun != null && it.Present) { var rb = it.Gun.Built.Renderer.bounds; HaloLabRunner.Log("item " + it.Gun.Def.Name + " world size " + rb.size.ToString("0.00") + " at " + it.Gun.transform.position.ToString("0.00") + " scale " + it.Gun.transform.lossyScale.x.ToString("0.000")); }
            r.Check(sg.Gun.Built.Renderer.bounds.size.magnitude < 1.3f, "a shotgun on the floor is shotgun-sized (" + sg.Gun.Built.Renderer.bounds.size.magnitude.ToString("0.00") + " m diagonal)");
            // the left hand down to the shotgun (within reach, no bending to the exact spot)
            yield return Move(_lh, sg.Gun.transform.position + Vector3.up * 0.45f, Face, 0.5f);
            HaloLabRunner.Log("prompt: '" + HaloLocal.PickupPrompt + "'");
            r.Check(HaloLocal.PickupPrompt.Length > 0, "the game's pickup prompt shows");
            Grip(false, 1f);
            yield return Seconds(0.2f);
            HaloLabRunner.Log("after taking the shotgun: " + Guns());
            r.Check(h.HeldGun != null && h.HeldGun.Def.Tag == shotgun, "the left hand holds the shotgun");
            // two carried now (pistol + shotgun): the sniper rifle swaps for the one in hand
            yield return Move(_rh, _rig.position + new Vector3(0.6f, 0.5f, 1.4f), Face, 0.5f);
            Grip(true, 1f);
            yield return Seconds(0.2f);
            HaloLabRunner.Log("after the swap: " + Guns());
            r.Check(h.HeldGun != null && h.HeldGun.Def.Tag == sniper && h.Carried == 2, "the sniper rifle swapped in (two weapons max)");
            Grip(true, 0f); Grip(false, 0f);
            yield return Seconds(0.1f);
            // walk over a pistol (ammunition for the carried pistol) and an over shield
            int reserveBefore = small.State.Reserve;
            small.State.Reserve = 0;
            HaloPickups.PlaceAt(small.Def.Tag, _rig.position + new Vector3(0.1f, 0f, 0.15f), 0f);
            HaloTag os = null; foreach (var t in map.OfClass("eqip")) if (t.Name.EndsWith("over shield")) os = t;
            HaloPickups.PlaceAt(os, _rig.position + new Vector3(-0.1f, 0f, -0.1f), 0f);
            yield return Seconds(0.5f);
            HaloLabRunner.Log("walked over: " + Guns() + " (pistol reserve was " + reserveBefore + ")");
            r.Check(small.State.Reserve > 0, "walking over a pistol took its ammunition");
            r.Check(HaloLocal.OverShield, "the over shield took the shields to " + HaloLocal.Shield.ToString("0.0"));

            // ---- damage to the local player: shields first, then the body; the recharge
            HaloVitals.Reset();
            var pistolBullet = map.Find("jpt!", "weapons\\pistol\\bullet");
            for (int i = 0; i < 3; i++) HaloCombat.TakeHit(new HaloHit { Amount = 25f, Effect = pistolBullet, SourcePlayer = 3, Point = _head.position, Direction = Vector3.back });
            HaloLabRunner.Log("3 pistol bullets: " + Guns());
            r.Check(HaloLocal.Shield <= 0.01f && HaloLocal.Health >= 0.99f, "75 shield points took three pistol bullets");
            HaloCombat.TakeHit(new HaloHit { Amount = 25f, Effect = pistolBullet, SourcePlayer = 3, Point = _head.position, Direction = Vector3.back });
            HaloLabRunner.Log("a 4th: " + Guns());
            r.Check(HaloLocal.Health < 0.9f, "the body took the next one (x1.5 cyborg armor)");
            yield return Seconds(HaloVitals.StunTime + 1.5f);
            HaloLabRunner.Log("recharging after " + (HaloVitals.StunTime + 1.5f).ToString("0.0") + " s: " + Guns());
            r.Check(HaloLocal.Shield > 0.2f, "the shields recharge after the stun time");
            HaloCombat.TakeHit(new HaloHit { Amount = 25f, Effect = pistolBullet, SourcePlayer = 3, Headshot = true });
            HaloCombat.TakeHit(new HaloHit { Amount = 25f, Effect = pistolBullet, SourcePlayer = 3, Headshot = true });
            HaloCombat.TakeHit(new HaloHit { Amount = 25f, Effect = pistolBullet, SourcePlayer = 3, Headshot = true });
            HaloLabRunner.Log("headshots: " + Guns());
            r.Check(!HaloLocal.Alive, "a headshot on an unshielded player kills");
            yield return Seconds(0.3f);
            int lying = 0; foreach (var it in HaloPickups.Items) if (it.Dropped && it.Present) lying++;
            HaloLabRunner.Log("dead: items lying " + lying + ", carried " + h.Carried);
            r.Check(h.Carried == 0, "a dead player's weapons fell to the ground");

            // ---- a plasma rifle: heat and battery
            HaloCombat.Spawned();
            yield return Seconds(0.2f);
            var prItem = HaloPickups.PlaceAt(pr, _rig.position + new Vector3(0.4f, 0f, 0.6f), 0f);
            yield return Move(_rh, prItem.Gun.transform.position + Vector3.up * 0.4f, Face, 0.4f);
            Grip(true, 1f);
            yield return Seconds(0.2f);
            HaloGun prg = h.HeldGun;
            r.Check(prg != null && prg.Def.Tag == pr, "picked up the plasma rifle (swap)");
            yield return Move(_rh, shoulder, Quaternion.LookRotation(aimAt - shoulder), 0.3f);
            Trig(true, 1f);
            float t0 = Time.time; bool over = false;
            while (Time.time - t0 < 4f) { if (prg != null && prg.State.Overheated) { over = true; break; } yield return null; }
            float overAt = Time.time - t0;
            FirstPerson(r, "12_plasma_rifle", aimAt, 70f);
            HaloLabRunner.Log("12: eye " + _head.position + ", muzzle " + (prg != null && prg.ModelRoot != null ? (HaloFx.Marker(prg.ModelRoot, "primary trigger") ?? prg.ModelRoot).position.ToString() : "-") + ", " + HaloFx.Stats + ", farthest particles from the eye:" + HaloFx.DebugParticles(_head.position, 14)
                + "\n  particles above 1.2 m:" + HaloFx.DebugParticlesAbove(1.2f, 30) + "\n  last impacts:" + HaloFx.DebugImpacts(16));
            Trig(true, 0f);
            HaloLabRunner.Log("plasma rifle overheated " + over + " after " + overAt.ToString("0.00") + " s: " + Guns());
            r.Check(over, "the plasma rifle overheats under a held trigger");
            Grip(true, 0f);
            yield return Seconds(0.2f);

            // ---- overheat sequence: the weapon's overheated effect loops at the vents while overheated and stops when it cools
            //      (weap +884, loop 0..0); the plasma pistol's charge glow (overcharge light + flare, charging.lsnd) and its charged bolt
            {
                h.Unequip();
                yield return null;
                int v0 = HaloGunFx.VentsStarted, s0 = HaloGunFx.VentsStopped;
                HaloGun vg = h.Give(pr);
                if (r.Check(vg != null, "overheat: plasma rifle given"))
                {
                    yield return Move(_rh, h.ZoneCenter(HaloHandling.ZoneOf(vg.Slot)), Face, 0.4f);
                    Grip(true, 1f);
                    yield return Seconds(0.15f);
                    Vector3 vready = _rig.TransformPoint(new Vector3(0.12f, 1.35f, 0.4f));
                    yield return Move(_rh, vready, Quaternion.LookRotation(aimAt - vready), 0.3f);
                    Trig(true, 1f);
                    float o0 = Time.time;
                    while (!vg.State.Overheated && Time.time - o0 < 4f) yield return null;
                    Trig(true, 0f);
                    float hot = Time.time;
                    yield return Seconds(0.5f);
                    Vector3 gc = vg.transform.TransformPoint(vg.LocalBounds.center);
                    r.Aim(gc - vg.transform.right * 0.45f + Vector3.up * 0.12f, gc, 45f);
                    r.Shot("12v_pr_vent_0.5s");
                    bool loop05 = vg.Fx.Venting;
                    yield return Seconds(1.0f);
                    gc = vg.transform.TransformPoint(vg.LocalBounds.center);
                    r.Aim(gc - vg.transform.right * 0.45f + Vector3.up * 0.12f, gc, 45f);
                    r.Shot("12v_pr_vent_1.5s");
                    bool loop15 = vg.Fx.Venting && vg.State.Overheated;
                    HaloLabRunner.Log("overheat: plasma rifle heat " + vg.State.Heat.ToString("0.00") + ", venting at 0.5 s " + loop05 + ", at 1.5 s " + loop15 + " | " + HaloFx.Stats);
                    r.Check(loop05 && loop15, "overheat: the plasma rifle's overheated effect keeps looping while it is overheated");
                    while (vg.State.Overheated && Time.time - hot < 6f) yield return null;
                    float cooled = Time.time - hot;
                    yield return Seconds(0.1f);
                    HaloLabRunner.Log("overheat: cooled after " + cooled.ToString("0.00") + " s (tag: heat 1 -> recovery " + vg.Def.HeatRecoveryThreshold.ToString("0.##") + " at " + vg.Def.HeatLossRate.ToString("0.##") + "/s), vents started " + (HaloGunFx.VentsStarted - v0) + " stopped " + (HaloGunFx.VentsStopped - s0));
                    r.Check(!vg.State.Overheated && !vg.Fx.Venting && HaloGunFx.VentsStopped - s0 >= 1, "overheat: the vent effect stops when the plasma rifle has cooled");
                    yield return Seconds(0.5f);
                    gc = vg.transform.TransformPoint(vg.LocalBounds.center);
                    r.Aim(gc - vg.transform.right * 0.45f + Vector3.up * 0.12f, gc, 45f);
                    r.Shot("12v_pr_vent_closed");
                    Grip(true, 0f);
                    yield return Seconds(0.2f);
                }

                h.Unequip();
                yield return null;
                HaloTag ppTag = HaloCombat.FindWeapon("plasma pistol");
                HaloGun pp = ppTag != null ? h.Give(ppTag) : null;
                if (r.Check(pp != null, "charge: plasma pistol given (" + (pp != null ? pp.Def.ChargeAttachments.Count + " charge attachments" : "-") + ")"))
                {
                    yield return Move(_rh, h.ZoneCenter(HaloHandling.ZoneOf(pp.Slot)), Face, 0.4f);
                    Grip(true, 1f);
                    yield return Seconds(0.15f);
                    Vector3 vready = _rig.TransformPoint(new Vector3(0.12f, 1.35f, 0.4f));
                    yield return Move(_rh, vready, Quaternion.LookRotation(aimAt - vready), 0.3f);
                    int c0 = HaloGunFx.ChargesStarted;
                    Trig(true, 1f);
                    yield return Seconds(0.55f);
                    Vector3 gc = pp.transform.TransformPoint(pp.LocalBounds.center);
                    r.Aim(gc - pp.transform.right * 0.4f + Vector3.up * 0.08f, gc, 45f);
                    r.Shot("12c_pp_charging");
                    float midCharge = pp.State.Charge;
                    yield return Seconds(0.6f);
                    FirstPerson(r, "12c_pp_charged_fp", aimAt, 70f);
                    r.Aim(gc - pp.transform.right * 0.4f + Vector3.up * 0.08f, gc, 45f);
                    r.Shot("12c_pp_charged");
                    HaloLabRunner.Log("charge: plasma pistol charge " + midCharge.ToString("0.00") + " -> " + pp.State.Charge.ToString("0.00") + ", charged " + pp.State.Charged + ", glow on " + pp.Fx.Charging + " (started " + (HaloGunFx.ChargesStarted - c0) + ") | " + HaloFx.Stats);
                    r.Check(pp.State.Charged && pp.Fx.Charging, "charge: the plasma pistol charges and its overcharge glow is on");
                    Trig(true, 0f);
                    yield return null; yield return null;
                    var bolt = HaloProjectiles.Find(f => f.Def != null && f.Def.Tag != null && f.Def.Tag.Name.Contains("charged bolt"));
                    yield return Seconds(0.12f);
                    if (r.Check(bolt != null, "charge: the charged bolt flies"))
                    {
                        Vector3 bp = bolt.Position;
                        Vector3 side = Vector3.Cross(Vector3.up, (aimAt - _head.position).normalized);
                        r.Aim(bp + side * 2.5f + Vector3.up * 0.3f, bp, 50f);
                        r.Shot("12c_pp_charged_bolt_side");
                        FirstPerson(r, "12c_pp_charged_bolt_fp", aimAt, 70f);
                    }
                    r.Check(!pp.Fx.Charging, "charge: the glow goes with the shot");
                    yield return Seconds(0.3f);
                    gc = pp.transform.TransformPoint(pp.LocalBounds.center);
                    r.Aim(gc - pp.transform.right * 0.4f + Vector3.up * 0.08f, gc, 45f);
                    r.Shot("12c_pp_overheated_after_charged");
                    HaloLabRunner.Log("charge: after the charged shot overheated " + pp.State.Overheated + ", venting " + pp.Fx.Venting);
                    r.Check(pp.State.Overheated && pp.Fx.Venting, "charge: the charged shot overheats the pistol and it vents");
                    Grip(true, 0f);
                    yield return Seconds(0.2f);
                }
            }

            // ---- monitor play on simulated keys
            HaloXr.Simulated = false;
            h.Body.OverrideLeft = null; h.Body.OverrideRight = null;
            HaloHandling.LabKeys = k => Keys.Contains(k);
            HaloCombat.Spawned();
            _head.localRotation = Quaternion.LookRotation(aimAt - _head.position);
            yield return Seconds(0.3f);
            r.Check(h.Desktop && h.HeldGun != null, "monitor: the first weapon is drawn by itself (" + (h.HeldGun != null ? h.HeldGun.Def.Name : "none") + ")");
            r.Aim(_head.position, _head.position + _head.forward, 70f);
            r.Shot("13_monitor_first_person");
            far.Clear();
            Keys.Add("mouse0"); Keys.Add("down:mouse0");
            yield return null; Keys.Remove("down:mouse0");
            yield return Seconds(0.8f);
            Keys.Remove("mouse0");
            yield return Seconds(0.3f);
            HaloLabRunner.Log("monitor fire: " + Guns() + " | " + far.State());
            r.Check(far.Hits > 0, "monitor shots go along the aim and hit");
            Keys.Add("down:r"); yield return null; Keys.Remove("down:r");
            yield return Seconds(4f);
            HaloLabRunner.Log("monitor reload: " + Guns());
            r.Check(h.HeldGun != null && h.HeldGun.State.Loaded == h.HeldGun.State.LoadedMaximum, "R reloads in the weapon's time");
            int f0 = HaloLocal.FragGrenades;
            Keys.Add("down:g"); yield return null; Keys.Remove("down:g");
            yield return Seconds(0.2f);
            r.Check(HaloLocal.FragGrenades == f0 - 1 && HaloProjectiles.CountGrenades() > 0, "G throws a grenade");
            Keys.Add("down:2"); yield return null; Keys.Remove("down:2");
            yield return Seconds(0.3f);
            HaloLabRunner.Log("after 2: " + Guns());
            r.Check(h.HeldGun == h.Guns[1], "2 switches to the second weapon");
            yield return Seconds(2.5f);
            HaloLabRunner.Log("end: " + Guns());
            HaloLabRunner.Log("sounds: impacts played " + HaloImpacts.Played + " (silent " + HaloImpacts.Silent + "), reload pieces " + HaloSoundPiece.Played + " | " + HaloAudio.Diag());
            Finish();
        }

        // ------------------------------------------------------------------ "combat-reload": the reload points, a full reload per weapon, the cupped pistol, hover haptics

        static GameObject Dot(Vector3 at, Color c, float size)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.position = at;
            g.transform.localScale = Vector3.one * size;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", c);
            g.GetComponent<Renderer>().sharedMaterial = m;
            return g;
        }

        static IEnumerator RunReload(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "bloodgulch");
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            var map = new HaloMap(entry.File);
            HaloCombat.LabMap = map;
            HaloCombat.LabScenario = new HaloScenario(map);
            Time.captureFramerate = 60;
            try { HaloFx.Preload(map); } catch (System.Exception e) { HaloLabRunner.Log("fx preload: " + e.Message); }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            floor.layer = HaloLayers.World;
            var fm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            fm.SetColor("_BaseColor", new Color(0.42f, 0.44f, 0.4f));
            floor.GetComponent<Renderer>().sharedMaterial = fm;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            _rig = new GameObject("Lab Rig").transform;
            var camGo = new GameObject("Lab Eye");
            camGo.tag = "MainCamera";
            _eye = camGo.AddComponent<Camera>();
            _eye.nearClipPlane = 0.03f;
            _head = camGo.transform; _head.SetParent(_rig, false); _head.localPosition = new Vector3(0f, 1.65f, 0f);
            _lh = new GameObject("Lab Left Hand").transform; _lh.SetParent(_rig, false); _lh.localPosition = new Vector3(-0.22f, 0.95f, 0.15f);
            _rh = new GameObject("Lab Right Hand").transform; _rh.SetParent(_rig, false); _rh.localPosition = new Vector3(0.22f, 0.95f, 0.15f);
            Marker(_rig, PrimitiveType.Capsule, new Vector3(0f, 1.1f, 0f), new Vector3(0.34f, 0.36f, 0.22f), new Color(0.25f, 0.3f, 0.35f));
            Marker(_lh, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.04f, new Color(0.2f, 0.4f, 1f));
            Marker(_rh, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.04f, new Color(1f, 0.3f, 0.2f));
            Dummy far = MakeDummy("far dummy", new Vector3(0f, 0f, 8f), 180f);
            HaloXr.Simulated = true;
            HaloXr.SimLeft = default(HaloHand); HaloXr.SimRight = default(HaloHand);
            HaloCombat.Spawned();
            var h = H;
            if (!r.Check(h != null, "the combat driver exists")) yield break;
            h.Body.OverrideRoot = _rig; h.Body.OverrideHead = _head; h.Body.OverrideLeft = _lh; h.Body.OverrideRight = _rh;
            HaloCombat.Spawned();
            yield return Seconds(0.4f);
            Vector3 rest = _rig.TransformPoint(new Vector3(-0.3f, 1.0f, 0.3f));

            // ---- hover haptics: a gun on the hip and grenades on the chest buzz, an empty place does not
            int pistolIx = h.Guns[0] != null && h.Guns[0].Def.OneHanded ? 0 : 1;
            int p0 = HaloXr.PulseCount;
            yield return Move(_lh, h.ZoneCenter(HaloHandling.ZoneOf(h.Guns[pistolIx].Slot)), Face, 0.4f);
            yield return Seconds(0.1f);
            r.Check(h.HoverOf(0) == pistolIx && HaloXr.PulseCount > p0, "a hand over the hip holster with the pistol in it buzzes (zone " + h.HoverOf(0) + ")");
            int p1 = HaloXr.PulseCount;
            yield return Move(_lh, h.ZoneCenter(HaloHandling.Zone.Frags), Face, 0.4f);
            yield return Seconds(0.1f);
            r.Check(h.HoverOf(0) == 2 && HaloXr.PulseCount > p1, "a hand over the frag grenades buzzes");
            int frags = HaloLocal.FragGrenades;
            HaloLocal.FragGrenades = 0;
            yield return Move(_lh, rest, Face, 0.2f);
            int p2 = HaloXr.PulseCount;
            yield return Move(_lh, h.ZoneCenter(HaloHandling.Zone.Frags), Face, 0.3f);
            yield return Seconds(0.1f);
            r.Check(h.HoverOf(0) == -1 && HaloXr.PulseCount == p2, "no grenades there: nothing buzzes");
            HaloLocal.FragGrenades = frags;
            HaloSettings.Haptics = false;
            yield return Move(_lh, rest, Face, 0.2f);
            int p3 = HaloXr.PulseCount;
            yield return Move(_lh, h.ZoneCenter(HaloHandling.Zone.Frags), Face, 0.3f);
            yield return Seconds(0.1f);
            r.Check(HaloXr.PulseCount == p3, "haptics off in the settings: silent");
            HaloSettings.Haptics = true;
            yield return Move(_lh, rest, Face, 0.3f);

            // ---- the chest grenades survive a map change with live materials (they drew magenta after the old map's materials went)
            HaloLocal.PlasmaGrenades = 2;
            yield return Seconds(0.2f);
            Vector3 chest = h.Body.Chest;
            r.Aim(chest + h.Body.Forward * 0.55f + Vector3.up * 0.1f, chest, 40f);
            r.Shot("g_chest_grenades_before");
            HaloCombat.Clear();
            HaloMaterials.Release(map.Path);          // what the Match module does when a match ends
            HaloCombat.Spawned();
            HaloLocal.PlasmaGrenades = 2;
            yield return Seconds(0.3f);
            int bad = 0, seen = 0;
            foreach (var rend in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                Transform t = rend.transform;
                bool grenade = false;
                while (t != null) { if (t.name == "Frag grenade" || t.name == "Plasma grenade") grenade = true; t = t.parent; }
                if (!grenade || !rend.gameObject.activeInHierarchy) continue;
                seen++;
                foreach (var mat in rend.sharedMaterials) if (mat == null || mat.shader == null) bad++;
            }
            HaloLabRunner.Log("chest grenades after a map change: " + seen + " renderers, " + bad + " missing materials");
            r.Check(seen >= 2 && bad == 0, "the chest grenades are rebuilt with live materials after a map change");
            r.Aim(chest + h.Body.Forward * 0.55f + Vector3.up * 0.1f, chest, 40f);
            r.Shot("g_chest_grenades_after");
            yield return Move(_lh, h.ZoneCenter(HaloHandling.Zone.Frags), Face, 0.4f);
            Grip(false, 1f);
            yield return Seconds(0.15f);
            r.Aim(_lh.position + h.Body.Forward * 0.35f + Vector3.up * 0.1f, _lh.position, 40f);
            r.Shot("g_grenade_in_hand");
            yield return Move(_lh, h.ZoneCenter(HaloHandling.Zone.Frags), Face, 0.3f);
            Grip(false, 0f);          // (unarmed at the chest: back on the rig)
            yield return Seconds(0.15f);
            yield return Move(_lh, rest, Face, 0.3f);

            // ---- the back: a generous reach over either shoulder, not in front of the face / chest
            {
                int rifle = h.Guns[0] != null && h.Guns[0].Def.LongGun ? 0 : 1;
                Vector3 head = h.Body.HeadPos;
                Vector3 F = h.Body.Forward, Rt = h.Body.Right, U = Vector3.up;
                Vector3[] reach = { head - F * 0.15f + Rt * 0.15f + U * 0.05f, head - F * 0.2f - Rt * 0.2f - U * 0.1f, head - F * 0.25f + Rt * 0.05f - U * 0.45f };
                string[] what = { "over the right shoulder", "over the LEFT shoulder (one long gun: either side)", "behind the upper back" };
                for (int i = 0; i < reach.Length; i++)
                {
                    yield return Move(_lh, reach[i], Face, 0.35f);
                    yield return Seconds(0.1f);
                    r.Check(h.HoverOf(0) == rifle, "back reach " + what[i] + " finds the rifle (hover " + h.HoverOf(0) + ")");
                }

                Vector3[] front = { head + F * 0.15f + U * -0.05f, h.Body.Chest + F * 0.15f, head + F * 0.05f + Rt * 0.1f - U * 0.1f };
                for (int i = 0; i < front.Length; i++)
                {
                    yield return Move(_lh, front[i], Face, 0.3f);
                    yield return Seconds(0.1f);
                    r.Check(h.HoverOf(0) != rifle, "a hand in front (" + i + ") does not reach the back");
                }

                yield return Move(_lh, reach[1], Face, 0.35f);
                Grip(false, 1f);
                yield return Seconds(0.15f);
                r.Check(h.HeldGun == h.Guns[rifle] && h.GunHand == 0, "the left hand draws the rifle from behind the left shoulder");
                yield return Move(_lh, reach[2], Face, 0.35f);
                Grip(false, 0f);
                yield return Seconds(0.2f);
                r.Check(h.HeldGun == null && h.IndexOf(h.Guns[rifle]) >= 0, "let go behind the back: back on the shoulder (" + (h.Guns[rifle] != null ? h.Guns[rifle].Slot.ToString() : "-") + ")");
                yield return Move(_lh, rest, Face, 0.3f);
            }

            // ---- a full hand reload of each weapon
            string[] names = { "pistol", "assault rifle", "sniper rifle", "shotgun", "rocket launcher" };
            HaloSoundPiece.LogPlays = true;
            Vector3 aimAt = far.transform.position + Vector3.up * 0.3f;
            foreach (string n in names)
            {
                HaloTag tag = HaloCombat.FindWeapon(n);
                if (tag == null) { r.Fail("no " + n + " in the map"); continue; }
                h.Unequip();
                HaloGun g = h.Give(tag);
                if (!r.Check(g != null, n + ": given")) continue;
                yield return Seconds(0.8f);      // (the reload sound decodes)
                int sounds0 = HaloSoundPiece.Played;
                yield return Move(_rh, h.ZoneCenter(HaloHandling.ZoneOf(g.Slot)), Face, 0.4f);
                Grip(true, 1f);
                yield return Seconds(0.15f);
                Vector3 ready = _rig.TransformPoint(new Vector3(0.12f, 1.35f, 0.4f));
                yield return Move(_rh, ready, Quaternion.LookRotation(aimAt - ready), 0.4f);
                yield return Seconds(0.2f);
                if (!r.Check(h.HeldGun == g, n + ": in the right hand")) { Grip(true, 0f); continue; }
                string file = n.Replace(' ', '_');
                // the measured points: well (blue), seated magazine (cyan), part to pull (red), support / cup (green)
                var dots = new List<GameObject>();
                float k = g.transform.lossyScale.x;
                dots.Add(Dot(g.transform.TransformPoint(g.WellLocal), new Color(0.1f, 0.3f, 1f), 0.018f));
                dots.Add(Dot(g.transform.TransformPoint(g.SeatLocal), new Color(0.2f, 1f, 1f), 0.012f));
                if (g.HasPull) dots.Add(Dot(g.transform.TransformPoint(g.RackLocal), new Color(1f, 0.15f, 0.1f), 0.018f));
                dots.Add(Dot(g.transform.TransformPoint(g.SupportLocal), new Color(0.2f, 1f, 0.2f), 0.014f));
                Vector3 gc = g.transform.TransformPoint(g.LocalBounds.center);
                r.Aim(gc - g.transform.right * 0.45f + Vector3.up * 0.05f, gc, 45f);
                r.Shot("r_" + file + "_points_left");
                r.Aim(gc + g.transform.right * 0.45f - Vector3.up * 0.12f, gc, 45f);
                r.Shot("r_" + file + "_points_right_below");
                foreach (var d in dots) Object.Destroy(d);

                // empty it (as if the last round went)
                g.State.Loaded = 0;
                yield return Seconds(0.15f);
                if (n == "pistol") { r.Check(g.LockedBack, "pistol: the slide locks back on empty"); r.Aim(gc - g.transform.right * 0.35f + Vector3.up * 0.05f, gc, 40f); r.Shot("r_pistol_locked_back"); }
                if (n == "shotgun")
                {
                    yield return Move(_lh, h.ZoneCenter(HaloHandling.Zone.Pouch), Face, 0.4f);
                    Grip(false, 1f);
                    yield return Seconds(0.15f);
                    Vector3 port = g.transform.TransformPoint(g.WellLocal);
                    yield return Move(_lh, port - Quaternion.LookRotation(Vector3.up, -Vector3.forward) * new Vector3(0f, 0f, 0.04f), Quaternion.LookRotation(Vector3.up, -Vector3.forward), 0.45f);
                    yield return Seconds(2.0f);
                    HaloLabRunner.Log("shotgun after holding shells at the port: " + Guns() + " needs pump " + g.NeedsRack);
                    r.Check(g.State.Loaded >= 4, "shotgun: shells go in one by one at the port (" + g.State.Loaded + ")");
                    r.Check(g.NeedsRack, "shotgun: loaded from empty wants a pump");
                    Grip(false, 0f);
                }
                else
                {
                    yield return Press(true, true, 0.1f);
                    yield return Seconds(0.2f);
                    r.Check(!g.MagIn, n + ": B dropped the magazine");
                    yield return Move(_lh, h.ZoneCenter(HaloHandling.Zone.Pouch), Face, 0.4f);
                    Grip(false, 1f);
                    yield return Seconds(0.15f);
                    r.Check(HaloLocal.Reloading, n + ": a magazine from the pouch");
                    // the hand brings the magazine's middle to where it sits in the gun (the hand holds it 5 cm ahead of the fist)
                    Quaternion hold = Quaternion.LookRotation(g.transform.up, -g.transform.forward);
                    Vector3 target = g.transform.TransformPoint(g.SeatLocal) - hold * new Vector3(0f, 0f, 0.05f) * h.Body.Scale - g.transform.up * 0.03f;
                    yield return Move(_lh, target, hold, 0.5f);
                    r.Aim(gc - g.transform.right * 0.45f, gc, 45f);
                    r.Shot("r_" + file + "_magazine_at_well");
                    var mag = g.Def.PrimaryMagazine;
                    yield return Seconds((mag != null ? mag.ReloadTime * HaloHandling.ReloadFloor : 0f) + 0.4f);
                    r.Check(g.MagIn && g.State.Loaded == g.State.LoadedMaximum, n + ": the magazine seated (" + g.State.Loaded + "/" + g.State.LoadedMaximum + ")");
                    Grip(false, 0f);
                    yield return Seconds(0.1f);
                    if (g.HasPull) r.Check(g.NeedsRack, n + ": an empty reload wants the " + (g.Points != null ? g.Points.PullNode : "?") + " pulled");
                }

                // the pull: the free hand takes the slide / handle / pump and pulls it back
                if (g.HasPull)
                {
                    Vector3 pullAt = g.transform.TransformPoint(g.RackLocal);
                    yield return Move(_lh, pullAt, Face, 0.4f);
                    Grip(false, 1f);
                    yield return Seconds(0.1f);
                    r.Check(h.Racking, n + ": the free hand holds the part to pull");
                    float travel = g.PullTravel * g.transform.lossyScale.z;
                    yield return Move(_lh, pullAt - g.transform.forward * (travel * 1.1f), _lh.rotation, 0.25f);
                    yield return Seconds(0.05f);
                    r.Aim(gc - g.transform.right * 0.4f + Vector3.up * 0.05f, gc, 45f);
                    r.Shot("r_" + file + "_pulled");
                    HaloLabRunner.Log(n + ": pulled " + (travel * 110f).ToString("0.0") + " cm, part '" + (g.Points != null ? g.Points.PullNode : "-") + "' moved to " + (g.PullNode != null ? g.PullNode.localPosition.ToString("0.000") : "-") + " (rest " + g.PullRest.ToString("0.000") + ")");
                    r.Check(!g.NeedsRack, n + ": racked by the pull");
                    yield return Move(_lh, pullAt, _lh.rotation, 0.15f);
                    Grip(false, 0f);
                    yield return Seconds(0.1f);
                }

                int heard = HaloSoundPiece.Played - sounds0, want = n == "rocket launcher" ? 2 : n == "shotgun" ? 3 : 4;
                r.Check(heard >= want, n + ": every reload action played its sound (" + heard + " of " + want + ")");

                // ready: one shot
                int before = g.State.Loaded;
                Trig(true, 1f); yield return Seconds(0.05f); Trig(true, 0f);
                yield return Seconds(0.03f);
                string playing = g.Anim != null ? g.Anim.Playing : null;
                // the part's furthest trip from rest during the shot animation
                Vector3 partNow = g.PullNode != null ? g.PullNode.localPosition : Vector3.zero;
                float tEnd = Time.time + 0.7f;
                while (Time.time < tEnd && g.PullNode != null) { if ((g.PullNode.localPosition - g.PullRest).sqrMagnitude > (partNow - g.PullRest).sqrMagnitude) partNow = g.PullNode.localPosition; yield return null; }
                HaloLabRunner.Log(n + ": after the shot the weapon animation plays " + (playing ?? "nothing") + (g.Anim == null ? " (no HaloWeaponAnim for it)" : "") + (g.PullNode != null ? ", part at " + partNow.ToString("0.000") + " (rest " + g.PullRest.ToString("0.000") + ")" : ""));
                if (n == "pistol" || n == "shotgun" || n == "sniper rifle") r.Check(playing != null && (g.PullNode == null || (partNow - g.PullRest).magnitude > 0.003f), n + ": Halo's own shot animation moves the " + (g.Points != null ? g.Points.PullNode : "part"));
                r.Check(g.State.Loaded == before - 1 || (n == "shotgun" && g.State.Loaded < before), n + ": fires after the reload (" + before + " -> " + g.State.Loaded + ")");

                // the pistol cupped in two hands
                if (n == "pistol")
                {
                    Vector3 cup = g.transform.TransformPoint(g.SupportLocal);
                    yield return Move(_lh, cup, Quaternion.LookRotation(aimAt - cup), 0.4f);
                    Grip(false, 1f);
                    yield return Seconds(0.2f);
                    r.Check(h.Cupped, "pistol: the free hand under the grip cups it (two hands)");
                    Quaternion one = g.transform.rotation;
                    yield return Move(_lh, cup + Vector3.left * 0.06f, _lh.rotation * Quaternion.Euler(0f, -15f, 0f), 0.3f);
                    yield return Seconds(0.15f);
                    float turned = Quaternion.Angle(one, g.transform.rotation);
                    r.Check(turned > 1f, "pistol: the cupped aim follows both hands (" + turned.ToString("0.0") + " degrees)");
                    r.Aim(gc + new Vector3(-0.35f, 0.15f, -0.25f), gc, 50f);
                    r.Shot("r_pistol_cupped");
                    Grip(false, 0f);
                    yield return Seconds(0.15f);
                    r.Check(!h.Cupped, "pistol: letting go returns it to one hand");
                }

                yield return Move(_lh, rest, Face, 0.3f);
                Grip(true, 0f);
                yield return Seconds(0.2f);
            }

            // ---- the plasma rifle's vents open with the overheat and close when it cools (Halo's overheating / overheated / overheat-exit)
            {
                h.Unequip();
                HaloGun pr = h.Give(HaloCombat.FindWeapon("plasma rifle"));
                if (r.Check(pr != null, "plasma rifle: given"))
                {
                    yield return Move(_rh, h.ZoneCenter(HaloHandling.ZoneOf(pr.Slot)), Face, 0.4f);
                    Grip(true, 1f);
                    yield return Seconds(0.15f);
                    Vector3 ready = _rig.TransformPoint(new Vector3(0.12f, 1.35f, 0.4f));
                    yield return Move(_rh, ready, Quaternion.LookRotation(far.transform.position - ready), 0.3f);
                    Trig(true, 1f);
                    float t0 = Time.time;
                    while (!pr.State.Overheated && Time.time - t0 < 4f) yield return null;
                    yield return Seconds(0.3f);
                    Trig(true, 0f);
                    HaloLabRunner.Log("plasma rifle: overheated " + pr.State.Overheated + ", animation state " + (pr.Anim != null ? pr.Anim.State ?? "-" : "no anim") + ", playing " + (pr.Anim != null ? pr.Anim.Playing ?? "-" : "-"));
                    Vector3 gc = pr.transform.TransformPoint(pr.LocalBounds.center);
                    r.Aim(gc - pr.transform.right * 0.4f + Vector3.up * 0.15f, gc, 45f);
                    r.Shot("a_plasma_rifle_vents_open");
                    r.Check(pr.Anim != null && pr.Anim.State == "overheated", "plasma rifle: the vents are held open while overheated");
                    t0 = Time.time;
                    while (pr.State.Overheated && Time.time - t0 < 6f) yield return null;
                    yield return Seconds(0.1f);
                    r.Check(!pr.State.Overheated && pr.Anim.State == null, "plasma rifle: cooled, the vents close (" + (pr.Anim.Playing ?? "-") + ")");
                    Grip(true, 0f);
                    yield return Seconds(0.2f);
                }
            }

            // ---- a monitor reload plays Halo's reload in full
            {
                HaloXr.Simulated = false;
                h.Body.OverrideLeft = null; h.Body.OverrideRight = null;
                HaloHandling.LabKeys = k => Keys.Contains(k);
                HaloCombat.Spawned();
                yield return Seconds(0.3f);
                HaloGun g = h.HeldGun;
                if (r.Check(g != null && h.Desktop, "monitor: a weapon in hand (" + (g != null ? g.Def.Name : "none") + ")"))
                {
                    g.State.Loaded = 0;
                    yield return null;
                    Keys.Add("down:r"); yield return null; Keys.Remove("down:r");
                    yield return Seconds(0.4f);
                    string playing = g.Anim != null ? g.Anim.Playing : null;
                    HaloLabRunner.Log("monitor reload of the " + g.Def.Name + ": animation " + (playing ?? "none") + ", remaining " + (g.Anim != null ? g.Anim.Remaining.ToString("0.00") : "-") + " s");
                    r.Check(g.Anim == null || playing != null, "monitor: the reload animation plays (" + (playing ?? "-") + ")");
                    yield return Seconds(4f);
                    r.Check(g.State.Loaded == g.State.LoadedMaximum, "monitor: reloaded (" + g.State.Loaded + ")");
                }

                HaloHandling.LabKeys = null;
            }

            Finish();
        }

        // ------------------------------------------------------------------ "combat-ai": a grunt shoots the (stand-in) local player

        static IEnumerator RunAi(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "a10");
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            var map = new HaloMap(entry.File);
            HaloCombat.LabMap = map;
            HaloCombat.LabScenario = new HaloScenario(map);
            Time.captureFramerate = 60;
            try { HaloFx.Preload(map); } catch (System.Exception) { }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            floor.layer = HaloLayers.World;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            _rig = new GameObject("Lab Rig").transform;
            var camGo = new GameObject("Lab Eye"); camGo.tag = "MainCamera";
            _eye = camGo.AddComponent<Camera>();
            _head = camGo.transform; _head.SetParent(_rig, false); _head.localPosition = new Vector3(0f, 1.65f, 0f);
            _lh = new GameObject("Lab Left Hand").transform; _lh.SetParent(_rig, false); _lh.localPosition = new Vector3(-0.22f, 0.95f, 0.15f);
            _rh = new GameObject("Lab Right Hand").transform; _rh.SetParent(_rig, false); _rh.localPosition = new Vector3(0.22f, 0.95f, 0.15f);
            Marker(_rig, PrimitiveType.Capsule, new Vector3(0f, 1.1f, 0f), new Vector3(0.34f, 0.36f, 0.22f), new Color(0.25f, 0.3f, 0.35f));
            HaloXr.Simulated = true;
            HaloXr.SimLeft = default(HaloHand); HaloXr.SimRight = default(HaloHand);
            HaloCombat.Spawned();
            var h = H;
            if (!r.Check(h != null, "the combat driver exists")) yield break;
            h.Body.OverrideRoot = _rig; h.Body.OverrideHead = _head; h.Body.OverrideLeft = _lh; h.Body.OverrideRight = _rh;
            HaloCombat.Spawned();
            yield return Seconds(0.5f);
            var cat = HaloCombat.Catalog;
            HaloLabRunner.Log("campaign player unit " + cat.PlayerUnit + ": body " + (cat.PlayerVitality != null ? cat.PlayerVitality.MaximumBody : 0f) + ", shield " + (cat.PlayerVitality != null ? cat.PlayerVitality.MaximumShield : 0f)
                              + " (multiplayer unit " + cat.MultiplayerUnit + ")");
            r.Check(cat.PlayerVitality != null && cat.PlayerVitality.MaximumBody > 0f, "the campaign player unit's vitality is read");

            // our own shot never hits our own body
            float s0 = HaloLocal.Shield;
            HaloTag pistol = HaloCombat.FindWeapon("pistol") ?? HaloCombat.FindWeapon("assault rifle");
            var pdef = cat.Weapon(pistol);
            HaloCombat.FireProjectile(pdef.PrimaryTrigger.Projectile, _head.position + Vector3.forward * 0.3f, Vector3.zero, HaloLocal.NetId, pistol, null, Vector3.back);
            HaloCombat.FireProjectile(pdef.PrimaryTrigger.Projectile, _head.position + Vector3.forward * 0.3f, Vector3.zero, -1, pistol, null, Vector3.back);
            yield return Seconds(0.3f);
            r.Check(Mathf.Approximately(HaloLocal.Shield, s0), "the player's own shots fly through the player's own body (shield " + HaloLocal.Shield.ToString("0.00") + ")");

            // a grunt with its own weapon, aiming at the player's chest
            HaloTag grunt = null;
            foreach (HaloTag v in map.OfClass("actv")) if (grunt == null && v.Name.IndexOf("grunt", System.StringComparison.OrdinalIgnoreCase) >= 0) grunt = v;
            if (!r.Check(grunt != null, "a grunt variant in " + map.Name)) yield break;
            HaloUnit g = HaloBipeds.Spawn(grunt, new Vector3(0f, 0f, 7f), 180f, HaloTeam.None);
            if (!r.Check(g != null, "grunt spawned")) yield break;
            yield return Seconds(0.8f);
            HaloLabRunner.Log("grunt: " + g.Def.Name + " weapon " + (g.Weapon != null ? g.Weapon.Name : "none"));
            if (g.Weapon == null && pistol != null) g.Give(HaloCombat.FindWeapon("plasma pistol") ?? pistol);
            float hits0 = HaloLocal.DamagedAt;
            float t0 = Time.time;
            float minShield = HaloLocal.Shield, minHealth = HaloLocal.Health;
            bool shotShield = false, shotBody = false;
            while (HaloLocal.Alive && Time.time - t0 < 40f)
            {
                g.LookPoint = g.AimPoint = HaloCombat.LocalChest;
                g.Firing = true;
                if (HaloLocal.Shield < 0.99f) shotShield = true;
                if (HaloLocal.Health < 0.99f) shotBody = true;
                minShield = Mathf.Min(minShield, HaloLocal.Shield); minHealth = Mathf.Min(minHealth, HaloLocal.Health);
                if (shotShield && !shotBody && Time.time - t0 < 30f && HaloLocal.Shield < 0.5f && minHealth > 0.98f) { r.Aim(_head.position + new Vector3(1.2f, 0.2f, 1.5f), g.transform.position + Vector3.up, 60f); }
                yield return null;
            }

            g.Firing = false;
            HaloLabRunner.Log("after " + (Time.time - t0).ToString("0.0") + " s of grunt fire: shield min " + minShield.ToString("0.00") + ", health min " + minHealth.ToString("0.00") + ", alive " + HaloLocal.Alive + ", damaged at " + HaloLocal.DamagedAt.ToString("0.0") + " from " + HaloLocal.DamageFrom.ToString("0.0"));
            r.Check(shotShield, "the grunt's shots lower the player's shields");
            r.Check(shotBody, "then the body");
            r.Check(!HaloLocal.Alive, "and kill the player");
            r.Check(HaloLocal.DamagedAt > hits0, "the damage direction is reported (HUD arrows)");
            r.Aim(new Vector3(2.5f, 1.8f, 3.5f), new Vector3(0f, 1f, 3.5f), 60f);
            r.Shot("ai_grunt_shooting");

            // a script damage effect at a point and on an object
            HaloCombat.Spawned();
            yield return Seconds(0.2f);
            HaloTag blast = null;
            foreach (HaloTag t in map.OfClass("jpt!")) if (blast == null && t.Name.IndexOf("frag grenade\\explosion", System.StringComparison.OrdinalIgnoreCase) >= 0) blast = t;
            float sh = HaloLocal.Shield;
            HaloCombat.ApplyDamageAt(blast, HaloCombat.LocalChest + Vector3.forward * 1.5f);
            yield return null;
            r.Check(HaloLocal.Shield < sh, "damage_new: a blast at a point hurts the player (" + sh.ToString("0.00") + " -> " + HaloLocal.Shield.ToString("0.00") + ")");
            HaloUnit g2 = HaloBipeds.Spawn(grunt, new Vector3(2f, 0f, 4f), 180f, HaloTeam.None);
            yield return Seconds(0.5f);
            float b0 = g2.Body;
            bool hurt = HaloCombat.ApplyDamage(blast, g2.gameObject);
            yield return null;
            r.Check(hurt && (g2.Body < b0 || !g2.Alive), "damage_object: the effect on a unit hurts it (" + b0.ToString("0.00") + " -> " + g2.Body.ToString("0.00") + ")");
            var drop = HaloCombat.DropWeaponAt(HaloCombat.FindWeapon("plasma pistol") ?? pistol, -1, -1, new Vector3(-1f, 0.5f, 2f), Vector3.zero);
            r.Check(drop != null && drop.Present, "a dead unit's weapon can be dropped as a pickup");
            HaloCombat.InputEnabled = false;
            int shots = h.HeldGun != null ? h.HeldGun.State.Loaded : 0;
            Trig(true, 1f); yield return Seconds(0.3f); Trig(true, 0f);
            r.Check(h.HeldGun == null || h.HeldGun.State.Loaded == shots, "player_enable_input false: the trigger does nothing");
            HaloCombat.InputEnabled = true;

            // ---- difficulty: three grunt plasma-pistol bolts on the player, Easy vs Legendary (the globals' difficulty table)
            {
                HaloTag ppT = HaloCombat.FindWeapon("plasma pistol");
                HaloTag bolt = ppT != null ? cat.Weapon(ppT).PrimaryTrigger.Projectile : null;
                HaloUnit cov = HaloBipeds.Spawn(grunt, new Vector3(0f, 0f, 5f), 180f, HaloTeam.Covenant);
                yield return Seconds(0.5f);
                float[] per = new float[4];
                foreach (int d in new[] { 0, 3 })
                {
                    HaloCombat.LabDifficulty = d;
                    HaloCombat.Spawned();
                    yield return Seconds(0.2f);
                    float before = HaloVitals.ShieldPoints + HaloVitals.Body;
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 from = cov.transform.position + Vector3.up * 1.0f, to = HaloCombat.LocalChest;
                        HaloCombat.FireProjectile(bolt, from, Vector3.zero, HaloCombat.AiShooter, ppT, cov.transform, (to - from).normalized);
                        yield return Seconds(0.4f);
                    }

                    per[d] = (before - (HaloVitals.ShieldPoints + HaloVitals.Body)) / 3f;
                    HaloLabRunner.Log("difficulty " + d + ": a grunt's plasma pistol bolt takes " + per[d].ToString("0.0") + " points (enemy damage x" + HaloCombat.DifficultyTable.EnemyDamage[d].ToString("0.##") + ", weapon modifier x" + HaloCombat.ShooterModifier(cov.transform).ToString("0.##") + ")");
                }

                r.Check(per[3] > per[0] * 1.2f, "Legendary grunts hurt more than Easy ones (" + per[0].ToString("0.0") + " vs " + per[3].ToString("0.0") + " points a bolt)");
                HaloCombat.LabDifficulty = -1;
            }

            // ---- an AI's plasma pistol: its attachments (overcharge light + lens flare, charging hum, heat flares, muzzle light) are driven by the
            // weapon's functions, never on by themselves; held fire overheats it and the overheated effect loops at the vents until it cools
            {
                HaloTag ppT = HaloCombat.FindWeapon("plasma pistol");
                HaloUnit ai = ppT != null ? HaloBipeds.Spawn(grunt, new Vector3(-3f, 0f, 6f), 90f, HaloTeam.None) : null;
                if (r.Check(ai != null, "ai weapon: a grunt for the plasma pistol"))
                {
                    yield return Seconds(1.5f);      // earlier bolts' lights go out
                    int l0 = HaloFx.LightCount, f0 = HaloFx.FlareCount;
                    ai.Give(ppT);
                    yield return Seconds(0.5f);
                    HaloLabRunner.Log("ai weapon: plasma pistol given: lights " + l0 + " -> " + HaloFx.LightCount + ", flares " + f0 + " -> " + HaloFx.FlareCount);
                    r.Check(HaloFx.LightCount <= l0 && HaloFx.FlareCount <= f0, "ai weapon: the AI plasma pistol carries no light / flare by itself (lights " + l0 + " -> " + HaloFx.LightCount + ")");
                    r.Aim(ai.transform.position + new Vector3(0.2f, 1.2f, -1.6f), ai.transform.position + Vector3.up * 0.8f, 50f);
                    r.Shot("ai_pp_idle");
                    int v0 = HaloGunFx.VentsStarted, s0v = HaloGunFx.VentsStopped, o0 = HaloUnit.SfxOverheat;
                    Vector3 away = ai.transform.position + Vector3.left * 20f + Vector3.up * 1f;
                    float tf = Time.time;
                    while (HaloUnit.SfxOverheat == o0 && Time.time - tf < 20f) { ai.LookPoint = ai.AimPoint = away; ai.Firing = true; yield return null; }
                    HaloLabRunner.Log("ai weapon: overheated after " + (Time.time - tf).ToString("0.0") + " s, " + ai.ShotsFired + " shots, charge glows " + HaloUnit.ChargeGlows);
                    yield return Seconds(0.4f);
                    bool venting = HaloGunFx.VentsStarted - v0 == 1 && HaloGunFx.VentsStopped == s0v;
                    r.Check(HaloUnit.SfxOverheat > o0 && venting, "ai weapon: the AI's plasma pistol overheats and keeps venting (started " + (HaloGunFx.VentsStarted - v0) + ", stopped " + (HaloGunFx.VentsStopped - s0v) + ")");
                    r.Shot("ai_pp_vent");
                    ai.Firing = false;
                    float tc = Time.time;
                    while (HaloGunFx.VentsStopped == s0v && Time.time - tc < 6f) yield return null;
                    HaloLabRunner.Log("ai weapon: vent stopped after " + (Time.time - tc).ToString("0.00") + " s of cooling");
                    r.Check(HaloGunFx.VentsStopped - s0v == 1, "ai weapon: the vent stops when the AI's plasma pistol has cooled");
                }
            }

            Finish();
        }

        // ------------------------------------------------------------------ "combat-glass": shoot a breakable pane until it breaks (a30's world)

        static IEnumerator RunGlass(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "a30");
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            var map = new HaloMap(entry.File);
            var scen = new HaloScenario(map);
            HaloCombat.LabMap = map;
            HaloCombat.LabScenario = scen;
            try { HaloFx.Preload(map); } catch (System.Exception) { }
            var root = new GameObject("glass lab world").transform;
            yield return HaloWorld.Build(map, scen, root, null);
            int broken0;
            int panes = HaloWorld.BreakableCount(out broken0);
            HaloLabRunner.Log("world built: " + panes + " breakable surfaces");
            if (!r.Check(panes > 0, "the map has breakable surfaces")) yield break;
            // find a pane: a world collider whose own ray hit says breakable
            Collider pane = null; RaycastHit ph = default(RaycastHit);
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (!c.enabled) continue;
                Vector3 ctr = c.bounds.center;
                foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.up, Vector3.down })
                {
                    RaycastHit h;
                    if (c.Raycast(new Ray(ctr - dir * 2f, dir), out h, 4f) && HaloWorld.IsBreakable(h)) { pane = c; ph = h; break; }
                }

                if (pane != null) break;
            }

            if (!r.Check(pane != null, "found a breakable pane")) yield break;
            HaloTag ar = HaloCombat.FindWeapon("assault rifle") ?? HaloCombat.FindWeapon("pistol");
            HaloTag bullet = HaloCombat.Catalog.Weapon(ar).PrimaryTrigger.Projectile;
            Vector3 n = ph.normal.sqrMagnitude > 0.5f ? ph.normal : Vector3.up;
            Vector3 from = ph.point + n * 3f;
            r.Aim(ph.point + n * 2.5f + Vector3.up * 0.3f, ph.point, 60f);
            r.Shot("glass_before");
            int shots = 0;
            while (HaloWorld.IsBreakable(ph) && shots < 120)
            {
                HaloCombat.FireProjectile(bullet, from, Vector3.zero, -1, ar, null, (ph.point - from).normalized);
                shots++;
                yield return Seconds(0.07f);
            }

            int broken1;
            HaloWorld.BreakableCount(out broken1);
            HaloLabRunner.Log("pane at " + ph.point.ToString("0.0") + ": broke after " + shots + " " + System.IO.Path.GetFileName(ar.Name) + " bullets (" + broken0 + " -> " + broken1 + " broken)");
            r.Check(broken1 > broken0, "bullets break the pane");
            yield return Seconds(0.3f);
            r.Shot("glass_after");
            Finish();
        }

        static void Finish()
        {
            HaloHandling.LabKeys = null;
            HaloXr.Simulated = false;
            HaloLabRunner.Log("combat lab done");
        }
    }
}

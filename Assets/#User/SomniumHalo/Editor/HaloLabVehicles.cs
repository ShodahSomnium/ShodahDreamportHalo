using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Vehicles lab ("vehicles"): opens a map (-halomap, default bloodgulch) WITHOUT a match, lays a flat grey floor (layer 0) and
    /// a ramp, places one of each multiplayer vehicle of the map in a row (warthog, rocket warthog, ghost, scorpion, banshee, gun
    /// turret), lets them settle, and takes pictures (wheels on the floor, the lineup). Then it drives with simulated input: the
    /// warthog straight (top speed vs the tag's 0.275 wu/tick = 25.1 m/s) and turning, the chase view from Halo's camera track,
    /// a test dummy on the hit-box layer run over (splatter), the warthog gunner turning the chain gun and firing (projectiles, impacts),
    /// the ghost hovering (height vs the antigrav equilibrium) and at speed, the banshee climbing, the scorpion firing its shell.
    /// "vehicles-quick": the lineup only. Pictures in DevTools/lab/vehicles/.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabVehicles
    {
        static HaloLabVehicles()
        {
            HaloLabRunner.Register("vehicles", r => Run(r, true));
            HaloLabRunner.Register("vehicles-quick", r => Run(r, false));
            HaloLabRunner.Register("vehicles-seats", r => Seats(r));
            HaloLabRunner.Register("vehicles-vr", r => VrRun(r));
            HaloLabRunner.Register("vehicles-campaign", r => Campaign(r));
            HaloLabRunner.Register("vehicles-cryo", r => Cryo(r));
            HaloLabRunner.Register("vehicles-dropship", r => Dropship(r));
            HaloLabRunner.Register("vehicles-sfx", r => Sfx(r));
        }

        sealed class Dummy : MonoBehaviour, IHaloDamageable
        {
            public int Hits; public bool Instant; public float Amount;
            public void TakeHit(HaloHit hit) { Hits++; Instant |= hit.Instant; Amount += hit.Amount; }
        }

        static HaloMap _map;

        static HaloTag Vehi(string part)
        {
            foreach (var t in _map.OfClass("vehi")) if (t.Name.Contains(part)) return t;
            HaloLabRunner.Log("  (no vehicle " + part + " in the map)");
            return null;
        }

        static IEnumerator Seconds(float s) { float end = Time.time + s; while (Time.time < end) yield return null; }

        static void Sim(HaloVehicleControls c, Vector3 gunAim = default(Vector3), bool fire = false, bool fire2 = false)
        {
            HaloVehicleInput.Simulated = true;
            HaloVehicleInput.Sim = new HaloVehicleInput.Intent { Drive = c, GunAim = gunAim, Fire = fire, Fire2 = fire2 };
        }

        static void Chase(HaloLabRunner r, HaloVehicle v, int seat, float yawOffset, string name)
        {
            var s = v.SeatOf(seat);
            Vector3 f = Vector3.ProjectOnPlane(v.transform.forward, Vector3.up);
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg + yawOffset;
            float pitch = -8f;
            Vector3 anchor = HaloVehicleCamera.Anchor(v, s);
            Vector3 eye = anchor + Quaternion.Euler(0f, yaw, 0f) * HaloVehicleCamera.TrackOffset(v, s, pitch);
            r.Aim(eye, eye + Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward, 60f);
            r.Shot(name);
        }

        static IEnumerator Run(HaloLabRunner r, bool full)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "bloodgulch");
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            _map = new HaloMap(entry.File);
            Time.captureFramerate = 60;
            HaloVehicles.LabMode = true;
            HaloLocal.Alive = true;
            HaloFx.Preload(_map);
            HaloCombat.LabMap = _map;

            // stage: a 600 m floor at y 0 and a ramp
            var stage = new GameObject("vehicle lab");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "floor"; floor.transform.SetParent(stage.transform, false);
            floor.transform.localScale = new Vector3(60f, 1f, 60f);
            floor.layer = HaloLayers.World;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f)); mat.SetFloat("_Smoothness", 0.1f);
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            var grid = new Material(mat); grid.SetColor("_BaseColor", new Color(0.3f, 0.32f, 0.35f));
            for (int k = -10; k <= 10; k++)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(line.GetComponent<Collider>());
                line.transform.SetParent(stage.transform, false);
                line.transform.position = new Vector3(k * 10f, 0.005f, 100f); line.transform.localScale = new Vector3(0.15f, 0.01f, 400f);
                line.GetComponent<Renderer>().sharedMaterial = grid;
                var l2 = Object.Instantiate(line, stage.transform); l2.transform.position = new Vector3(0f, 0.005f, k * 10f + 100f); l2.transform.localScale = new Vector3(400f, 0.01f, 0.15f);
            }
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "ramp"; ramp.transform.SetParent(stage.transform, false);
            ramp.transform.position = new Vector3(-40f, 0.8f, 60f); ramp.transform.rotation = Quaternion.Euler(-12f, 0f, 0f); ramp.transform.localScale = new Vector3(8f, 0.5f, 16f);
            ramp.GetComponent<Renderer>().sharedMaterial = grid;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.4f, 0.42f, 0.46f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            Physics.SyncTransforms();

            // ---------------------------------------------------------------- the lineup
            string[] kinds = { "warthog\\mp_warthog", "rwarthog", "ghost", "scorpion", "banshee", "turret" };
            var vs = new List<HaloVehicle>();
            for (int k = 0; k < kinds.Length; k++)
            {
                var t = Vehi(kinds[k]);
                if (t == null) { vs.Add(null); continue; }
                var v = HaloVehicles.AddLab(t, new Vector3(-25f + k * 10f, 0.6f, 0f), 0f, stage.transform);
                vs.Add(v);
                r.Check(v.Model != null, v.name + ": model built (" + (v.Model != null ? v.Model.Nodes.Length + " nodes, " + v.Model.Markers.Count + " markers" : "none") + "), " +
                        v.Shapes.Count + " contact spheres, " + v.Wheels.Count + " wheels, " + v.Hovers.Count + " antigrav points, " + v.HitMeshes.Count + " hit meshes, " +
                        v.Seats.Count + " seats, " + v.Muzzles.Count + " muzzles, gun " + (v.Gun != null ? v.Gun.Tag.Name : "none"));
                foreach (var s in v.Seats)
                    HaloLabRunner.Log("    seat '" + s.Label + "' marker " + (s.Marker != v.transform ? "ok" : "MISSING") + " enter " + (s.Enter != null ? "ok" : "none") +
                                      " camera " + (s.Camera != null ? s.Camera.name : "none") + (s.Standing ? " standing" : "") + (s.Guns ? " guns" : "") +
                                      " track " + (s.Def.Track != null ? s.Def.Track.Tag.Name : "-") + (s.LeftGrip != null ? " grips ok" : ""));
            }
            yield return Seconds(3.5f);
            foreach (var v in vs)
            {
                if (v == null) continue;
                int g = 0; foreach (var w in v.Wheels) if (w.Grounded) g++;
                HaloLabRunner.Log("  settled " + v.name + ": y " + v.transform.position.y.ToString("0.00") + " up " + v.transform.up.y.ToString("0.000") + " kinematic " + v.Body.isKinematic + " wheels grounded " + g + "/" + v.Wheels.Count);
                bool tipsOnWings = v.Def.Type == HaloVehicleDef.Kind.AlienFighter; // an unpowered banshee rests on its wingtips and nose
                r.Check(v.transform.position.y > -0.5f && v.transform.position.y < 3f && v.transform.up.y > (tipsOnWings ? 0.85f : 0.95f), v.name + " rests upright on the floor");
            }
            r.Aim(new Vector3(0f, 9f, 26f), new Vector3(0f, 1f, 0f), 60f);
            r.Shot("lineup_front");
            r.Aim(new Vector3(-36f, 4f, -14f), new Vector3(-5f, 1f, 0f), 50f);
            r.Shot("lineup_side");
            var hog = vs[0];
            if (hog != null)
            {
                RaycastHit hh;
                Vector3 above = hog.transform.position + Vector3.up * 6f + hog.transform.forward * 1.2f;
                bool hitIt = Physics.Raycast(above, Vector3.down, out hh, 10f, HaloLayers.ShotMask, QueryTriggerInteraction.Ignore);
                HaloLabRunner.Log("  shot from above the warthog hood hits " + (hitIt ? hh.collider.name + " at y " + hh.point.y.ToString("0.00") : "nothing"));
                r.Check(hitIt && hh.collider.name.StartsWith("hit "), "a bullet from above hits the warthog's collision-model hit mesh");
                Vector3 side = hog.transform.position + Vector3.up * 1.0f - hog.transform.right * 6f;
                bool hitSide = Physics.Raycast(side, hog.transform.right, out hh, 12f, HaloLayers.ShotMask, QueryTriggerInteraction.Ignore);
                HaloLabRunner.Log("  shot from the side hits " + (hitSide ? hh.collider.name + " at " + (hh.distance).ToString("0.00") + " m" : "nothing"));
                r.Aim(hog.transform.position + new Vector3(-7f, 1.6f, 0.5f), hog.transform.position + new Vector3(0f, 0.8f, 0f), 50f);
                r.Shot("warthog_side_wheels");
                r.Aim(hog.transform.position + new Vector3(-3.5f, 5f, -5f), hog.transform.position + new Vector3(0f, 1.2f, 0f), 50f);
                r.Shot("warthog_above");
            }
            if (!full || hog == null) yield break;

            // ---------------------------------------------------------------- drive the warthog
            hog.transform.SetPositionAndRotation(new Vector3(-25f, 0.6f, 20f), Quaternion.identity); Physics.SyncTransforms();
            r.Check(HaloVehicleSeat.Local.GetIn(hog, 0), "a dummy gets into the warthog driver seat");
            r.Check(HaloLocal.Driving && HaloLocal.VehicleTag == hog.Def.Tag && hog.LocalDriven, "HaloLocal says driving the warthog; the warthog simulates here");
            Sim(new HaloVehicleControls { Throttle = 1f });
            float top = 0f; float t0 = Time.time; float t20 = -1f;
            while (Time.time - t0 < 5f) { top = Mathf.Max(top, hog.SpeedMs); if (t20 < 0f && hog.SpeedMs > 20f) t20 = Time.time - t0; yield return null; }
            float want = HaloVehicle.PerTick(hog.Def.MaxForward);
            HaloLabRunner.Log("  warthog: top " + top.ToString("0.0") + " m/s (tag " + want.ToString("0.0") + "), 0-20 m/s in " + t20.ToString("0.00") + " s, travelled to z " + hog.transform.position.z.ToString("0.0"));
            r.Check(top > want * 0.85f && top < want * 1.1f, "warthog top speed near the tag's");
            Chase(r, hog, 0, 0f, "warthog_chase_straight");
            float yaw0 = hog.transform.eulerAngles.y;
            Sim(new HaloVehicleControls { Throttle = 1f, Turn = 1f });
            yield return Seconds(1.5f);
            float turned = Mathf.DeltaAngle(yaw0, hog.transform.eulerAngles.y);
            HaloLabRunner.Log("  warthog: turned " + turned.ToString("0") + " deg right in 1.5 s at full steer, up " + hog.transform.up.y.ToString("0.00") + ", steer " + hog.Steer.ToString("0.00"));
            r.Check(turned > 20f && hog.transform.up.y > 0.8f, "warthog turns right on its wheels");
            Chase(r, hog, 0, 0f, "warthog_chase_turning");
            // Halo's steering toward the view: aim 90 deg left of the vehicle
            Vector3 left = Quaternion.Euler(0f, -90f, 0f) * hog.transform.forward;
            float yaw1 = hog.transform.eulerAngles.y;
            Sim(new HaloVehicleControls { Throttle = 0.6f, Aim = left, SteerToAim = true });
            yield return Seconds(1.5f);
            r.Check(Mathf.DeltaAngle(yaw1, hog.transform.eulerAngles.y) < -20f, "warthog steers toward the view (left) - turned " + Mathf.DeltaAngle(yaw1, hog.transform.eulerAngles.y).ToString("0"));
            Sim(new HaloVehicleControls { Throttle = 0f, Brake = true });
            yield return Seconds(3f);
            HaloLabRunner.Log("  warthog: coasted / braked to " + hog.SpeedMs.ToString("0.0") + " m/s");

            // splatter: a dummy on the hit-box layer in the warthog's path
            var dummyGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.Destroy(dummyGo.GetComponent<CapsuleCollider>());
            var cap = dummyGo.AddComponent<CapsuleCollider>(); cap.isTrigger = true; cap.height = 1.8f; cap.radius = 0.35f;
            dummyGo.layer = HaloLayers.Hitbox;
            var dummy = dummyGo.AddComponent<Dummy>();
            hog.Body.isKinematic = false;
            hog.transform.SetPositionAndRotation(new Vector3(20f, 0.6f, 20f), Quaternion.identity);
            hog.Body.linearVelocity = Vector3.zero; hog.Body.angularVelocity = Vector3.zero;
            dummyGo.transform.position = new Vector3(20f, 0.9f, 75f);
            Physics.SyncTransforms();
            Sim(new HaloVehicleControls { Throttle = 1f });
            float tEnd = Time.time + 6f;
            while (Time.time < tEnd && dummy.Hits == 0) yield return null;
            HaloLabRunner.Log("  splatter: hits " + dummy.Hits + " instant " + dummy.Instant + " at " + hog.SpeedMs.ToString("0.0") + " m/s");
            r.Check(dummy.Hits > 0 && dummy.Instant, "the warthog splatters a player in its path at speed");
            Sim(new HaloVehicleControls { Brake = true });
            yield return Seconds(2.5f);
            HaloVehicleSeat.Local.GetOut("lab");
            r.Check(!HaloLocal.Driving && HaloLocal.Vehicle == null, "out of the warthog; HaloLocal cleared");
            yield return Seconds(3.5f);
            r.Check(hog.Body.isKinematic, "the empty warthog is parked (kinematic)");

            // ---------------------------------------------------------------- the gunner and the chain gun
            r.Check(HaloVehicleSeat.Local.GetIn(hog, 2), "a dummy mans the warthog's chain gun");
            r.Check(HaloLocal.Gunning && HaloLocal.VehicleWeapon != null, "HaloLocal says gunning the " + (HaloLocal.VehicleWeapon != null ? HaloLocal.VehicleWeapon.Name : "?"));
            var gs = hog.SeatOf(2);
            Vector3 gripBefore = gs.LeftGrip != null ? gs.LeftGrip.position : Vector3.zero;
            Vector3 right = hog.transform.right + Vector3.down * 0.15f;
            Sim(default(HaloVehicleControls), right);
            yield return Seconds(2f);
            HaloLabRunner.Log("  gun aim yaw " + (hog.AimYaw * Mathf.Rad2Deg).ToString("0") + " pitch " + (hog.AimPitch * Mathf.Rad2Deg).ToString("0") + "; left grip moved " + (gs.LeftGrip != null ? (gs.LeftGrip.position - gripBefore).magnitude.ToString("0.00") : "?") + " m");
            r.Check(Mathf.Abs(hog.AimYaw * Mathf.Rad2Deg + 90f) < 8f, "the chain gun turned to the right (yaw -90)");
            r.Aim(hog.transform.position + new Vector3(-4f, 4.5f, -5f), hog.transform.position + new Vector3(0f, 2f, 0f), 50f);
            r.Shot("warthog_gun_right");
            int fired0 = HaloVehicleGun.TotalShots, imp0 = HaloVehicleShots.Impacts;
            Sim(default(HaloVehicleControls), right, true);
            yield return Seconds(1.5f);
            r.Aim(hog.transform.position + new Vector3(-2f, 3.5f, -9f), hog.transform.position + new Vector3(8f, 1f, 0f), 60f);
            r.Shot("warthog_gun_firing");
            Sim(default(HaloVehicleControls), right, false);
            yield return Seconds(1f);
            int fired = HaloVehicleGun.TotalShots - fired0, impacts = HaloVehicleShots.Impacts - imp0;
            HaloLabRunner.Log("  chain gun: " + fired + " rounds in 1.5 s (rate 8 -> 15 / s), " + impacts + " impacts, last at " + HaloVehicleShots.LastImpact.ToString("F1"));
            r.Check(fired >= 12 && fired <= 24, "the chain gun fires at its tag rate");
            HaloVehicleSeat.Local.GetOut("lab");

            // ---------------------------------------------------------------- ghost
            var ghost = vs[2];
            if (ghost != null)
            {
                r.Check(HaloVehicleSeat.Local.GetIn(ghost, 0), "a dummy drives the ghost");
                Sim(new HaloVehicleControls());
                yield return Seconds(3f);
                RaycastHit hit;
                float h = Physics.Raycast(ghost.transform.position + Vector3.up, Vector3.down, out hit, 20f, 1) ? hit.distance - 1f : -1f;
                var hp = ghost.Hovers.Count > 0 ? ghost.Hovers[0] : null;
                HaloLabRunner.Log("  ghost hovers: origin " + h.ToString("0.00") + " m over the floor (antigrav height " + (hp != null ? hp.Height.ToString("0.00") : "?") + " m, strength " + (hp != null ? hp.Strength.ToString("0.0") : "?") + "), up " + ghost.transform.up.y.ToString("0.000"));
                r.Check(h > 0.2f && h < 3f && ghost.transform.up.y > 0.95f, "the ghost hovers level");
                r.Aim(ghost.transform.position + new Vector3(-6f, 1.5f, -3f), ghost.transform.position + new Vector3(0f, 0.6f, 0f), 50f);
                r.Shot("ghost_hover");
                Sim(new HaloVehicleControls { Throttle = 1f });
                float gt = 0f; float end = Time.time + 3f;
                while (Time.time < end) { gt = Mathf.Max(gt, ghost.SpeedMs); yield return null; }
                HaloLabRunner.Log("  ghost: top " + gt.ToString("0.0") + " m/s (tag " + HaloVehicle.PerTick(ghost.Def.MaxForward).ToString("0.0") + ")");
                r.Check(gt > HaloVehicle.PerTick(ghost.Def.MaxForward) * 0.8f, "the ghost reaches its speed");
                int f0 = HaloVehicleGun.TotalShots;
                Sim(new HaloVehicleControls(), ghost.transform.forward, true);
                yield return Seconds(1f);
                Chase(r, ghost, 0, 0f, "ghost_chase_firing");
                r.Check(HaloVehicleGun.TotalShots - f0 >= 4, "the ghost's guns fire (" + (HaloVehicleGun.TotalShots - f0) + " bolts in 1 s)");
                Sim(new HaloVehicleControls());
                HaloVehicleSeat.Local.GetOut("lab");
            }

            // ---------------------------------------------------------------- banshee
            var banshee = vs[4];
            if (banshee != null)
            {
                r.Check(HaloVehicleSeat.Local.GetIn(banshee, 0), "a dummy flies the banshee");
                float y0 = banshee.transform.position.y;
                Vector3 up = Quaternion.Euler(-25f, 0f, 0f) * Vector3.forward;
                Sim(new HaloVehicleControls { Throttle = 1f, Aim = up, SteerToAim = true });
                yield return Seconds(4f);
                HaloLabRunner.Log("  banshee: climbed " + (banshee.transform.position.y - y0).ToString("0.0") + " m in 4 s at " + banshee.SpeedMs.ToString("0.0") + " m/s, pitch " + (-banshee.transform.eulerAngles.x).ToString("0"));
                r.Check(banshee.transform.position.y - y0 > 8f, "the banshee flies up where it looks");
                Chase(r, banshee, 0, 0f, "banshee_flying");
                Sim(new HaloVehicleControls { Throttle = 0f, Aim = Vector3.forward, SteerToAim = true });
                yield return Seconds(2f);
                float hy = banshee.transform.position.y;
                yield return Seconds(2f);
                HaloLabRunner.Log("  banshee: hovering, height change " + (banshee.transform.position.y - hy).ToString("0.00") + " m in 2 s, speed " + banshee.SpeedMs.ToString("0.0"));
                r.Check(Mathf.Abs(banshee.transform.position.y - hy) < 1.5f, "the banshee holds its height with no throttle");
                HaloVehicleSeat.Local.GetOut("lab");
            }

            // ---------------------------------------------------------------- scorpion
            var tank = vs[3];
            if (tank != null)
            {
                r.Check(HaloVehicleSeat.Local.GetIn(tank, 0), "a dummy drives the scorpion");
                Sim(new HaloVehicleControls { Throttle = 1f, Turn = 0.5f });
                yield return Seconds(3f);
                HaloLabRunner.Log("  scorpion: speed " + tank.SpeedMs.ToString("0.0") + " m/s (tag " + HaloVehicle.PerTick(tank.Def.MaxForward).ToString("0.0") + "), yaw " + tank.transform.eulerAngles.y.ToString("0"));
                r.Check(tank.SpeedMs > 5f, "the scorpion drives");
                int f0 = HaloVehicleGun.TotalShots;
                Vector3 aim = Quaternion.Euler(-5f, 30f, 0f) * tank.transform.forward;
                Sim(new HaloVehicleControls(), aim, false);
                yield return Seconds(1.5f);
                Sim(new HaloVehicleControls(), aim, true);
                yield return Seconds(0.3f);
                Chase(r, tank, 0, 0f, "scorpion_cannon");
                Sim(new HaloVehicleControls(), aim, false);
                yield return Seconds(0.6f);
                r.Check(HaloVehicleGun.TotalShots - f0 == 1, "the scorpion cannon fires one shell per pull (" + (HaloVehicleGun.TotalShots - f0) + ")");
                HaloVehicleSeat.Local.GetOut("lab");
            }
            HaloVehicleInput.Simulated = false;
            HaloVehicles.Clear();
        }

        // ------------------------------------------------------------------ seated avatars (Halo's seat animations)

        const string StandIn = "Assets/SomniumSpaceTemplates/ItemsTemplate/Models/Avatars/RPM Men.fbx";

        /// <summary>A painted stand-in avatar (RPM Men) for other labs (net: a seated remote player).</summary>
        internal static Animator LabStandIn(Vector3 at, Color c) { return StandInAt(at, c); }

        static Animator StandInAt(Vector3 at, Color c)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StandIn);
            if (prefab == null) return null;
            GameObject av = Object.Instantiate(prefab, at, Quaternion.identity);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", c);
            foreach (Renderer rr in av.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[rr.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                rr.sharedMaterials = mats;
                var smr = rr as SkinnedMeshRenderer;
                if (smr != null) smr.updateWhenOffscreen = true;
            }
            Animator a = av.GetComponentInChildren<Animator>();
            if (a != null) a.enabled = false;
            return a;
        }

        static void Look(HaloLabRunner r, HaloVehicle v, Vector3 localEye, Vector3 localAt, string name, float fov = 45f)
        {
            r.Aim(v.transform.TransformPoint(localEye), v.transform.TransformPoint(localAt), fov);
            r.Shot(name);
        }

        static IEnumerator Seats(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "bloodgulch");
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            _map = new HaloMap(entry.File);
            Time.captureFramerate = 60;
            HaloVehicles.LabMode = true;
            HaloLocal.Alive = true;
            HaloCombat.LabMap = _map;
            string why;
            var cyb = HaloCyborg.Load(_map, out why);
            if (!r.Check(cyb != null, "the cyborg's animations load (" + why + ")")) yield break;
            HaloCyborg.Use(cyb);

            var stage = new GameObject("vehicle seat lab");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.SetParent(stage.transform, false); floor.transform.localScale = new Vector3(30f, 1f, 30f); floor.layer = HaloLayers.World;
            var fm = new Material(Shader.Find("Universal Render Pipeline/Lit")); fm.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f));
            floor.GetComponent<Renderer>().sharedMaterial = fm;
            var sun = new GameObject("sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            Physics.SyncTransforms();

            var hog = HaloVehicles.AddLab(Vehi("warthog\\mp_warthog"), new Vector3(0f, 0.3f, 0f), 0f, stage.transform);
            var ghost = HaloVehicles.AddLab(Vehi("ghost"), new Vector3(12f, 0.3f, 0f), 0f, stage.transform);
            var tank = HaloVehicles.AddLab(Vehi("scorpion"), new Vector3(-14f, 0.3f, 0f), 0f, stage.transform);
            var banshee = HaloVehicles.AddLab(Vehi("banshee"), new Vector3(26f, 0.3f, 0f), 0f, stage.transform);
            var turret = HaloVehicles.AddLab(Vehi("c gun turret"), new Vector3(0f, 0.3f, -12f), 0f, stage.transform);
            yield return Seconds(3.5f);

            // stand-ins: -1 = me (warthog driver), 1 passenger, 2 gunner, 3 ghost, 4 scorpion driver, 5 scorpion rider LF, 6 banshee
            var avatars = new Dictionary<int, Animator>();
            Color[] colors = { new Color(0.9f, 0.3f, 0.2f), new Color(0.2f, 0.5f, 0.9f), new Color(0.2f, 0.8f, 0.3f), new Color(0.9f, 0.8f, 0.2f), new Color(0.7f, 0.3f, 0.8f), new Color(0.3f, 0.8f, 0.8f), new Color(0.9f, 0.5f, 0.1f), new Color(0.95f, 0.95f, 0.95f) };
            int[] keys = { -1, 1, 2, 3, 4, 5, 6, 7 };
            HaloVehicle[] at = { hog, hog, hog, ghost, tank, tank, banshee, turret };
            int[] seat = { 0, 1, 2, 0, 0, 1, 0, 0 };
            for (int i = 0; i < keys.Length; i++)
            {
                var s = at[i].SeatOf(seat[i]);
                var a = StandInAt(s.Enter != null ? s.Enter.position : s.Marker.position, colors[i]);
                if (!r.Check(a != null, "stand-in avatar for key " + keys[i])) yield break;
                avatars[keys[i]] = a;
            }
            HaloSeatPoses.LabAvatar = k => { Animator a; return avatars.TryGetValue(k, out a) ? a : null; };
            // the warthog passenger (key 1, Halo's seat flag "allows weapons") holds an assault rifle; the scorpion rider (key 5, no such flag) does not
            HaloTag ar = null;
            foreach (var t in _map.OfClass("weap")) if (t.Name.EndsWith("assault rifle")) { ar = t; break; }
            r.Check(ar != null, "the assault rifle tag");
            HaloSeatPoses.LabWeapon = k => k == 1 || k == 5 ? ar : null;
            var labGuns = new Dictionary<int, Transform>();
            var gunAt = new Dictionary<int, Vector3>();
            HaloSeatPoses.LabGun = (k, p, q) =>
            {
                Transform g;
                if (!labGuns.TryGetValue(k, out g))
                {
                    HaloTag model = ar != null ? ar.Root.Ref(40) : null;
                    var b = model != null ? HaloModelBuilder.Build(model, stage.transform, new HaloModelBuilder.Options { Scale = HaloRemoteView.WeaponScale }) : null;
                    g = b != null && b.Root != null ? b.Root.transform : null;
                    if (g == null) return;
                    g.name = "lab rider gun " + k;
                    labGuns[k] = g;
                }
                g.SetPositionAndRotation(p, q);
                gunAt[k] = p;
            };
            var riders = new List<HaloVehicleRiders.Rider>();
            for (int i = 1; i < keys.Length; i++) riders.Add(new HaloVehicleRiders.Rider { PlayerId = keys[i], Vehicle = at[i], Seat = seat[i] });

            // ---- the enter clip (me into the warthog's driver seat)
            r.Check(HaloVehicleSeat.Local.GetIn(hog, 0), "I get into the warthog driver seat");
            HaloVehicleInput.Simulated = true;
            HaloVehicleInput.Sim = default(HaloVehicleInput.Intent);
            Vector3 side = new Vector3(-6f, 2.2f, 0.6f), mid = new Vector3(0f, 1.1f, 0f);
            float t0 = Time.time;
            foreach (float t in new[] { 0.15f, 0.6f, 1.1f, 1.6f })
            {
                while (Time.time - t0 < t) yield return null;
                HaloLabRunner.Log("  enter t " + t.ToString("0.00") + ": " + HaloSeatPoses.Describe(-1));
                Look(r, hog, side, mid, "enter_" + ((int)(t * 100)).ToString("000"));
            }
            r.Check(HaloSeatPoses.Describe(-1).StartsWith("Enter") || HaloSeatPoses.Describe(-1).StartsWith("Sit"), "my avatar played the enter clip: " + HaloSeatPoses.Describe(-1));

            // ---- everyone seated
            HaloVehicleNet.LabRiders = riders;
            yield return Seconds(2.5f);
            for (int i = 0; i < keys.Length; i++)
            {
                int k = keys[i];
                var rr = HaloSeatPoses.Get(k);
                var s = at[i].SeatOf(seat[i]);
                Transform hips = avatars[k].GetBoneTransform(HumanBodyBones.Hips);
                float d = hips != null ? (hips.position - s.Marker.position).magnitude : -1f;
                HaloLabRunner.Log("  key " + k + " in " + s.Label + ": " + HaloSeatPoses.Describe(k) + ", hips " + d.ToString("0.00") + " m from the seat marker, posed " + (rr != null && rr.Poser.Posed));
                r.Check(rr != null && rr.Poser.Posed && d >= 0f && d < 0.6f, "key " + k + " sits in " + s.Label + " (hips on the seat)");
            }
            r.Check(HaloSeatPoses.Owns(-1) && !HaloDesk.PoseOwn && !HaloDesk.PoseSeated, "V owns the seated poses; Desk is held off");
            Look(r, hog, new Vector3(-5.5f, 2.6f, 1.5f), new Vector3(0f, 1.2f, -0.2f), "warthog_seated_side");
            Look(r, hog, new Vector3(-3f, 4.5f, -5.5f), new Vector3(0f, 1.3f, 0f), "warthog_seated_back");
            Look(r, hog, new Vector3(0.5f, 2.4f, 4.5f), new Vector3(0f, 1.3f, 0f), "warthog_seated_front");
            Look(r, ghost, new Vector3(-3.5f, 1.8f, 1.5f), new Vector3(0f, 0.8f, 0f), "ghost_seated");
            Look(r, tank, new Vector3(-6f, 4f, 4f), new Vector3(0f, 1.6f, 0f), "scorpion_seated");
            Look(r, banshee, new Vector3(-4f, 2.2f, 2.5f), new Vector3(0f, 1.0f, 0f), "banshee_seated");

            // ---- the passenger's weapon: in the seat animation's hands, inside the vehicle (user: "their gun is displayed outside the car far away")
            {
                var pr = HaloSeatPoses.Get(1);
                var ps = hog.SeatOf(1);
                Transform rhand = avatars[1].GetBoneTransform(HumanBodyBones.RightHand);
                bool has = pr != null && pr.GunValid;
                float toHand = has && rhand != null ? (pr.GunPos - rhand.position).magnitude : -1f;
                float toSeat = has ? (pr.GunPos - ps.Marker.position).magnitude : -1f;
                Bounds hb = hog.ShapeBounds();
                hb.Expand(0.3f);
                Vector3 muzzle = has ? pr.GunPos + pr.GunRot * Vector3.right * 0.6f : Vector3.zero;      // (Halo +X = the barrel; the AR is ~0.6 m long here)
                float lean = has ? Vector3.Angle(pr.GunRot * Vector3.right, hog.transform.forward) : -1f;
                HaloLabRunner.Log("  passenger gun: " + (has ? "valid" : "none") + ", playing '" + (pr != null && pr.Puppet != null ? pr.Puppet.Playing : "-") + "', grip " + toHand.ToString("0.00") + " m from the right hand bone, " +
                                  toSeat.ToString("0.00") + " m from the seat marker, barrel " + lean.ToString("0") + " deg from the hull's forward, in the hull's bounds " + (has && hb.Contains(pr.GunPos)));
                r.Check(has && pr.Puppet.Playing.Contains("rifle"), "the passenger plays the seat's rifle class with the AR (" + (pr != null && pr.Puppet != null ? pr.Puppet.Playing : "-") + ")");
                r.Check(has && toHand >= 0f && toHand < 0.15f, "the passenger's gun is in the posed right hand (" + toHand.ToString("0.00") + " m)");
                r.Check(has && toSeat < 1.0f && hb.Contains(pr.GunPos), "the passenger's gun is inside the vehicle (" + toSeat.ToString("0.00") + " m from the seat marker)");
                var lf = HaloSeatPoses.Get(5);
                r.Check(lf != null && !lf.GunValid, "the scorpion rider (seat without 'allows weapons') holds no weapon");
                Look(r, hog, new Vector3(2.8f, 2.0f, -2.6f), new Vector3(0f, 1.2f, -0.2f), "passenger_gun_side");
                Look(r, hog, new Vector3(1.0f, 2.2f, 4.2f), new Vector3(0.3f, 1.2f, 0f), "passenger_gun_front");
                // the passenger's own view: from the seat's camera marker, looking ahead and down at the hands
                var pcam = (ps.Camera != null ? ps.Camera.position : ps.Marker.position + Vector3.up * 0.72f) + hog.transform.forward * 0.22f;      // (in front of the stand-in's head)
                r.Aim(pcam, pcam + hog.transform.forward * 2f - Vector3.up * 0.9f, 70f);
                r.Shot("passenger_gun_view");
            }

            // ---- steering and the gunner's aim
            HaloVehicleInput.Sim = new HaloVehicleInput.Intent { Drive = new HaloVehicleControls { Throttle = 0.6f, Turn = 1f } };
            yield return Seconds(1.2f);
            hog.AimYaw = -1.1f; hog.AimPitch = 0.35f;
            yield return Seconds(0.3f);
            HaloLabRunner.Log("  steering " + hog.Steer.ToString("0.00") + ", accel " + hog.Acceleration.ToString("0.0") + "; driver " + HaloSeatPoses.Describe(-1));
            Look(r, hog, new Vector3(-2.5f, 3.8f, -4.5f), new Vector3(0f, 1.3f, 0f), "warthog_steer_gun", 50f);
            {
                var pr = HaloSeatPoses.Get(1);
                Transform rhand = avatars[1].GetBoneTransform(HumanBodyBones.RightHand);
                float toHand = pr != null && pr.GunValid && rhand != null ? (pr.GunPos - rhand.position).magnitude : -1f;
                Bounds hb = hog.ShapeBounds(); hb.Expand(0.5f);
                HaloLabRunner.Log("  driving " + hog.SpeedMs.ToString("0.0") + " m/s: passenger gun " + toHand.ToString("0.00") + " m from the hand, in the hull's bounds " + (pr != null && hb.Contains(pr.GunPos)));
                r.Check(toHand >= 0f && toHand < 0.15f && hb.Contains(pr.GunPos), "driving: the passenger's gun stays in the hand inside the vehicle (" + toHand.ToString("0.00") + " m)");
            }
            HaloVehicleInput.Sim = new HaloVehicleInput.Intent { Drive = new HaloVehicleControls { Brake = true } };
            yield return Seconds(2f);

            // ---- the warthog gunner: Halo's 'W-gunner fixed' idle + aim-still on the gun mount, hands on the gunner grips, following the aim
            {
                float[][] aims = { new[] { 0f, 0f }, new[] { 1.0f, 0.45f }, new[] { -1.2f, -0.3f } };
                string[] names = { "gunner_level", "gunner_left_up", "gunner_right_down" };
                var gr = HaloSeatPoses.Get(2);
                Animator ga = avatars[2];
                for (int i = 0; i < aims.Length; i++)
                {
                    hog.AimYaw = aims[i][0]; hog.AimPitch = aims[i][1];
                    yield return Seconds(0.4f);
                    Transform la = ga.GetBoneTransform(HumanBodyBones.LeftUpperArm), ra = ga.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    Vector3 across = ra.position - la.position;
                    Vector3 chestF = Vector3.Cross(across, Vector3.up); chestF.y = 0f;
                    Vector3 aimF = hog.AimDirection; aimF.y = 0f;
                    float off = Vector3.Angle(chestF, aimF);
                    var gs2 = hog.SeatOf(2);
                    Transform lh = ga.GetBoneTransform(HumanBodyBones.LeftHand), rh2 = ga.GetBoneTransform(HumanBodyBones.RightHand);
                    float lg = (lh.position - gs2.LeftGrip.position).magnitude, rg = (rh2.position - gs2.RightGrip.position).magnitude;
                    HaloLabRunner.Log("  gunner aim yaw " + aims[i][0].ToString("0.0") + " pitch " + aims[i][1].ToString("0.00") + ": " + HaloSeatPoses.Describe(2) + ", chest " + off.ToString("0") +
                                      " deg off the gun's heading, IK err L " + gr.Poser.LastLeftErr.ToString("0.000") + " R " + gr.Poser.LastRightErr.ToString("0.000") + " m, hands to the grip markers L " + lg.ToString("0.00") + " R " + rg.ToString("0.00") + " m");
                    r.Check(off < 40f, "gunner (" + names[i] + "): the body faces the gun (" + off.ToString("0") + " deg)");
                    r.Check(gr.Poser.LastLeftErr < 0.12f && gr.Poser.LastRightErr < 0.12f, "gunner (" + names[i] + "): the hands reach the gun's grips (L " + gr.Poser.LastLeftErr.ToString("0.00") + ", R " + gr.Poser.LastRightErr.ToString("0.00") + " m)");
                    Vector3 piv = hog.GunPivot.position;
                    Vector3 af = hog.AimDirection; Vector3 sideV = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(af, Vector3.up).normalized);
                    Vector3 eye = piv - Vector3.ProjectOnPlane(af, Vector3.up).normalized * 2.6f + sideV * 2.2f + Vector3.up * 1.0f;
                    r.Aim(eye, piv - Vector3.up * 0.5f, 50f);
                    r.Shot(names[i] + "_quarter");
                    eye = piv + sideV * 3.2f + Vector3.up * 0.2f;
                    r.Aim(eye, piv - Vector3.up * 0.6f, 50f);
                    r.Shot(names[i] + "_side");
                    HaloLabRunner.Log("  gunner overlay (aim in the seat marker's frame): yaw " + gr.AimYawNow.ToString("0.00") + " pitch " + gr.AimPitchNow.ToString("0.00") + " rad");
                    r.Check(Mathf.Abs(gr.AimYawNow) < 0.15f && Mathf.Abs(gr.AimPitchNow - aims[i][1]) < 0.15f,
                            "gunner (" + names[i] + "): the aim-still overlay is the gun's aim relative to the turning mount (yaw " + gr.AimYawNow.ToString("0.00") + " ~ 0, pitch " + gr.AimPitchNow.ToString("0.00") + " ~ " + aims[i][1].ToString("0.00") + ")");
                }
            }

            // ---- the gun turret gunner: the seat marker rides the turret end (yaw and pitch) - the overlay must not turn him a second time
            {
                var tr = HaloSeatPoses.Get(7);
                float[][] aims = { new[] { 0.8f, 0.3f }, new[] { -0.9f, -0.2f } };
                for (int i = 0; i < aims.Length; i++)
                {
                    turret.AimYaw = aims[i][0]; turret.AimPitch = aims[i][1];
                    yield return Seconds(0.4f);
                    var ts = turret.SeatOf(0);
                    Transform hips = avatars[7].GetBoneTransform(HumanBodyBones.Hips);
                    float d = hips != null ? (hips.position - ts.Marker.position).magnitude : -1f;
                    HaloLabRunner.Log("  turret aim yaw " + aims[i][0].ToString("0.0") + " pitch " + aims[i][1].ToString("0.0") + ": " + HaloSeatPoses.Describe(7) + ", overlay yaw " + tr.AimYawNow.ToString("0.00") + " pitch " + tr.AimPitchNow.ToString("0.00") + ", hips " + d.ToString("0.00") + " m from the seat");
                    r.Check(Mathf.Abs(tr.AimYawNow) < 0.15f && Mathf.Abs(tr.AimPitchNow) < 0.15f, "turret gunner: no double turn (overlay yaw " + tr.AimYawNow.ToString("0.00") + ", pitch " + tr.AimPitchNow.ToString("0.00") + ")");
                    Vector3 af = Vector3.ProjectOnPlane(turret.AimDirection, Vector3.up).normalized;
                    Vector3 sideV = Vector3.Cross(Vector3.up, af);
                    Vector3 piv = ts.Marker.position;
                    r.Aim(piv - af * 2.4f + sideV * 2.4f + Vector3.up * 1.2f, piv, 50f);
                    r.Shot("turret_gunner_" + i);
                }
            }

            // ---- the exit clip (me out of the warthog; the others get out too)
            HaloVehicleNet.LabRiders = null;
            Vector3 clipEnd;
            bool hasEnd = HaloSeatPoses.ExitPoint(hog, 0, out clipEnd);
            HaloVehicleSeat.Local.GetOut("lab");
            t0 = Time.time;
            foreach (float t in new[] { 0.1f, 0.4f, 0.75f })
            {
                while (Time.time - t0 < t) yield return null;
                HaloLabRunner.Log("  exit t " + t.ToString("0.00") + ": " + HaloSeatPoses.Describe(-1));
                Look(r, hog, side, mid, "exit_" + ((int)(t * 100)).ToString("000"));
            }
            r.Check(hasEnd, "the warthog driver exit clip ends at " + clipEnd.ToString("F2") + " (" + (clipEnd - hog.SeatOf(0).Marker.position).magnitude.ToString("0.00") + " m from the seat); the player is stood there");
            yield return Seconds(1f);
            r.Check(!HaloSeatPoses.Owns(-1) && HaloDesk.PoseOwn, "after the exit clip my avatar is Desk's again");
            HaloVehicleInput.Simulated = false;
            HaloSeatPoses.LabAvatar = null; HaloSeatPoses.LabWeapon = null; HaloSeatPoses.LabGun = null;
            HaloVehicles.Clear();
        }

        // ------------------------------------------------------------------ headset controls (simulated Touch controllers)

        static IEnumerator Press(bool right, int button)
        {
            if (right) { var h = HaloXr.SimRight; if (button == 1) h.Primary = true; else h.Secondary = true; HaloXr.SimRight = h; }
            else { var h = HaloXr.SimLeft; if (button == 1) h.Primary = true; else h.Secondary = true; HaloXr.SimLeft = h; }
            yield return null; yield return null;
            if (right) { var h = HaloXr.SimRight; h.Primary = false; h.Secondary = false; HaloXr.SimRight = h; }
            else { var h = HaloXr.SimLeft; h.Primary = false; h.Secondary = false; HaloXr.SimLeft = h; }
            yield return null;
        }

        static void Hands(float lTrig, float rTrig, float lGrip, float rGrip, Vector2 lStick, Vector2 rStick)
        {
            var l = HaloXr.SimLeft; l.Trigger = lTrig; l.Grip = lGrip; l.Stick = lStick; HaloXr.SimLeft = l;
            var r = HaloXr.SimRight; r.Trigger = rTrig; r.Grip = rGrip; r.Stick = rStick; HaloXr.SimRight = r;
        }

        static IEnumerator VrRun(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "bloodgulch");
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            _map = new HaloMap(entry.File);
            Time.captureFramerate = 60;
            HaloVehicles.LabMode = true;
            HaloLocal.Alive = true;
            HaloCombat.LabMap = _map;
            string why;
            var cyb = HaloCyborg.Load(_map, out why);
            if (cyb != null) HaloCyborg.Use(cyb);
            var stage = new GameObject("vehicle vr lab");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.SetParent(stage.transform, false); floor.transform.localScale = new Vector3(40f, 1f, 40f); floor.layer = HaloLayers.World;
            var fm = new Material(Shader.Find("Universal Render Pipeline/Lit")); fm.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f));
            floor.GetComponent<Renderer>().sharedMaterial = fm;
            var sun = new GameObject("sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            Physics.SyncTransforms();
            var hog = HaloVehicles.AddLab(Vehi("warthog\\mp_warthog"), new Vector3(0f, 0.3f, 0f), 0f, stage.transform);
            var tank = HaloVehicles.AddLab(Vehi("scorpion"), new Vector3(-20f, 0.3f, 0f), 0f, stage.transform);
            var ghost = HaloVehicles.AddLab(Vehi("ghost"), new Vector3(20f, 0.3f, 0f), 0f, stage.transform);
            yield return Seconds(3.5f);

            // the simulated headset: Touch controllers through HaloXr, stand-in hands and head (aim frame: forward = where they point)
            HaloXr.Simulated = true;
            HaloXr.SimLeft = default(HaloHand); HaloXr.SimRight = default(HaloHand);
            HaloVehicleInput.LabVr = true;
            var lh = new GameObject("lab left hand").transform; var rh = new GameObject("lab right hand").transform; var head = new GameObject("lab head").transform;
            HaloVehicleInput.LabLeftHand = lh; HaloVehicleInput.LabRightHand = rh; HaloVehicleInput.LabHead = head;

            // ---- seat choice: beside the scorpion's hull at its left-front rider door -> the DRIVER seat (riders need a driver)
            HaloVehicle cv; int cs;
            var lf = tank.SeatOf(1);
            HaloVehicleEntry.Choose(HaloVehicles.All, lf.Enter.position, out cv, out cs);
            r.Check(cv == tank && cs == 0, "at the scorpion's rider door the seat offered is the driver's (got " + (cv != null ? cv.name + " seat " + cs : "none") + ")");
            Vector3 hull = tank.transform.position + tank.transform.right * 2.6f;
            HaloVehicleEntry.Choose(HaloVehicles.All, hull, out cv, out cs);
            r.Check(cv == tank && cs == 0, "beside the scorpion's hull: the driver seat (got " + (cv != null ? cv.name + " seat " + cs : "none") + ")");
            HaloVehicleEntry.Choose(HaloVehicles.All, hog.SeatOf(2).Enter.position, out cv, out cs);
            r.Check(cv == hog && cs == 2, "at the warthog's tail: the gunner seat (got " + (cv != null ? cv.name + " seat " + cs : "none") + ")");
            HaloVehicleEntry.Choose(HaloVehicles.All, hog.SeatOf(0).Enter.position, out cv, out cs);
            r.Check(cv == hog && cs == 0, "at the warthog's left door: the driver seat (got " + (cv != null ? cv.name + " seat " + cs : "none") + ")");

            // ---- warthog gunner: trigger alone fires; on the grips the gun follows the hands; B gets out; the tablet is held off
            Vector3 hogAt = hog.transform.position;
            r.Check(HaloVehicleSeat.Local.GetIn(hog, 2), "into the warthog gunner seat");
            // my avatar (stand-in): a headset gunner - hips, legs and torso from Halo's W-gunner animation, the arms to the tracked hands
            var vrAv = StandInAt(hog.SeatOf(2).Marker.position, new Color(0.2f, 0.8f, 0.3f));
            HaloSeatPoses.LabAvatar = k => k == -1 ? vrAv : null;
            yield return null; yield return null;
            r.Check(HaloVehicleDirector.TabletHeld, "the tablet buttons are held off while seated");
            int s0 = HaloVehicleGun.TotalShots;
            rh.position = hog.transform.position + Vector3.up * 2f; rh.rotation = hog.transform.rotation;
            Hands(0f, 1f, 0f, 0f, Vector2.zero, Vector2.zero);
            yield return Seconds(1f);
            Hands(0f, 0f, 0f, 0f, Vector2.zero, Vector2.zero);
            int byTrigger = HaloVehicleGun.TotalShots - s0;
            r.Check(byTrigger >= 7, "the chain gun fires on the right trigger alone (" + byTrigger + " rounds in 1 s, spin-up 8 -> 15 / s)");
            yield return Seconds(0.5f);
            var gs = hog.SeatOf(2);
            Vector3 pivot = hog.GunPivot.position;
            Vector3 lg = gs.LeftGrip.position, rg = gs.RightGrip.position;
            Quaternion turn = Quaternion.Euler(0f, 25f, 0f);
            lh.position = pivot + turn * (lg - pivot); rh.position = pivot + turn * (rg - pivot);
            // the tracked hands ride the vehicle with the player (the client moves the rig with the seat): kept in the warthog's frame, never world-fixed
            Vector3 lLoc = hog.transform.InverseTransformPoint(lh.position), rLoc = hog.transform.InverseTransformPoint(rh.position);
            Quaternion lRotLoc = Quaternion.Inverse(hog.transform.rotation) * lh.rotation, rRotLoc = Quaternion.Inverse(hog.transform.rotation) * rh.rotation;
            float yaw0 = hog.AimYaw;
            s0 = HaloVehicleGun.TotalShots;
            Hands(1f, 0f, 1f, 1f, Vector2.zero, Vector2.zero);
            float tEnd = Time.time + 1.5f;
            yield return null;
            { var hl = HaloXr.Hand(false); var hr = HaloXr.Hand(true); HaloLabRunner.Log("  grips diag: L grip held " + hl.GripHeld + " (" + hl.Grip.ToString("0.0") + "), R " + hr.GripHeld + ", hands to the grip markers L " + (lh.position - gs.LeftGrip.position).magnitude.ToString("0.00") + " R " + (rh.position - gs.RightGrip.position).magnitude.ToString("0.00") + " m, simulated " + HaloXr.Simulated + ", LabVr " + HaloVehicleInput.LabVr); }
            while (Time.time < tEnd)
            {
                // the hands stay where they hold: the grips come round to them
                yield return null;
                lh.SetPositionAndRotation(hog.transform.TransformPoint(lLoc), hog.transform.rotation * lRotLoc);
                rh.SetPositionAndRotation(hog.transform.TransformPoint(rLoc), hog.transform.rotation * rRotLoc);
            }
            Hands(0f, 0f, 0f, 0f, Vector2.zero, Vector2.zero);
            HaloLabRunner.Log("  gunner on the grips: yaw " + (yaw0 * Mathf.Rad2Deg).ToString("0") + " -> " + (hog.AimYaw * Mathf.Rad2Deg).ToString("0") + " deg, " + (HaloVehicleGun.TotalShots - s0) + " rounds on the left trigger");
            r.Check(Mathf.Abs(Mathf.DeltaAngle(hog.AimYaw * Mathf.Rad2Deg, -25f)) < 6f, "the chain gun turned to the hands on its grips (25 deg right)");
            r.Check(HaloVehicleGun.TotalShots - s0 >= 10, "the left trigger fires while holding the grips");
            { float drift = (hog.transform.position - hogAt).magnitude; HaloLabRunner.Log("  gunner-only warthog: moved " + drift.ToString("0.000") + " m while gunning, kinematic " + hog.Body.isKinematic); r.Check(drift < 0.05f, "the undriven warthog stays put under its gunner (no creep)"); }
            Look(r, hog, new Vector3(-3f, 4f, -5f), new Vector3(0f, 1.8f, 1f), "vr_gunner_grips", 50f);
            {
                lh.SetPositionAndRotation(hog.transform.TransformPoint(lLoc), hog.transform.rotation * lRotLoc);
                rh.SetPositionAndRotation(hog.transform.TransformPoint(rLoc), hog.transform.rotation * rRotLoc);
                yield return null;      // (the pose of this frame with the hands where the vehicle took them)
                var meR = HaloSeatPoses.Get(-1);
                Transform al = vrAv.GetBoneTransform(HumanBodyBones.LeftHand), ar2 = vrAv.GetBoneTransform(HumanBodyBones.RightHand), hp = vrAv.GetBoneTransform(HumanBodyBones.Hips);
                float dl = (al.position - lh.position).magnitude, dr = (ar2.position - rh.position).magnitude, dh = (hp.position - hog.SeatOf(2).Marker.position).magnitude;
                HaloLabRunner.Log("  VR gunner body: " + HaloSeatPoses.Describe(-1) + ", hands to the tracked hands L " + dl.ToString("0.00") + " R " + dr.ToString("0.00") + " m, hips " + dh.ToString("0.00") + " m from the seat, overlay yaw " + (meR != null ? meR.AimYawNow.ToString("0.00") : "-") + " pitch " + (meR != null ? meR.AimPitchNow.ToString("0.00") : "-"));
                r.Check(meR != null && meR.Poser.Posed && dl < 0.12f && dr < 0.12f && dh < 0.4f, "VR gunner: body on the gun mount, the avatar's hands on the tracked hands at the grips");
                Vector3 piv2 = hog.GunPivot.position, af2 = Vector3.ProjectOnPlane(hog.AimDirection, Vector3.up).normalized;
                r.Aim(piv2 - af2 * 2.4f + Vector3.Cross(Vector3.up, af2) * 2.0f + Vector3.up * 0.9f, piv2 - Vector3.up * 0.5f, 50f);
                r.Shot("vr_gunner_body");
            }
            yield return Press(true, 2);
            r.Check(!HaloVehicleSeat.Local.Seated, "B got me out of the gunner seat");
            HaloSeatPoses.LabAvatar = null;
            yield return Seconds(0.5f);
            r.Check(HaloVehicleDirector.TabletHeld, "the tablet is still held off 0.5 s after getting out (the B press must not reach it)");
            HaloVehicleEntry.Clear();
            yield return Seconds(1.2f);
            r.Check(!HaloVehicleDirector.TabletHeld, "the tablet is given back after 1 s on foot");

            // ---- scorpion driver: left stick drives and turns, the cannon aims where the head looks, triggers fire
            r.Check(HaloVehicleSeat.Local.GetIn(tank, 0), "into the scorpion driver seat");
            float tyaw = tank.transform.eulerAngles.y;
            Hands(0f, 0f, 0f, 0f, new Vector2(0.4f, 1f), Vector2.zero);
            float top = 0f; tEnd = Time.time + 2.5f;
            while (Time.time < tEnd) { top = Mathf.Max(top, tank.SpeedMs); yield return null; }
            float turned = Mathf.DeltaAngle(tyaw, tank.transform.eulerAngles.y);
            HaloLabRunner.Log("  scorpion: " + top.ToString("0.0") + " m/s, turned " + turned.ToString("0") + " deg on the left stick");
            r.Check(top > 6f && turned > 10f, "the scorpion drives and turns on the left stick");
            Hands(0f, 0f, 0f, 0f, Vector2.zero, Vector2.zero);
            yield return Seconds(1.5f);
            head.rotation = tank.transform.rotation * Quaternion.Euler(-6f, 35f, 0f);
            yield return Seconds(1.2f);
            HaloLabRunner.Log("  scorpion cannon aim yaw " + (tank.AimYaw * Mathf.Rad2Deg).ToString("0") + " pitch " + (tank.AimPitch * Mathf.Rad2Deg).ToString("0") + " (head 35 right, 6 up)");
            r.Check(Mathf.Abs(Mathf.DeltaAngle(tank.AimYaw * Mathf.Rad2Deg, -35f)) < 6f, "the scorpion's turret follows the head");
            s0 = HaloVehicleGun.TotalShots;
            Hands(0f, 1f, 0f, 0f, Vector2.zero, Vector2.zero);
            yield return Seconds(0.2f);
            Hands(0f, 0f, 0f, 0f, Vector2.zero, Vector2.zero);
            yield return Seconds(0.2f);
            int shells = HaloVehicleGun.TotalShots - s0;
            Look(r, tank, new Vector3(-7f, 5f, -8f), new Vector3(0f, 2f, 4f), "vr_scorpion_cannon", 55f);
            s0 = HaloVehicleGun.TotalShots;
            Hands(1f, 0f, 0f, 0f, Vector2.zero, Vector2.zero);
            yield return Seconds(1f);
            Hands(0f, 0f, 0f, 0f, Vector2.zero, Vector2.zero);
            int mg = HaloVehicleGun.TotalShots - s0;
            r.Check(shells == 1 && mg >= 10, "the right trigger fires one shell (" + shells + "), the left trigger the machine gun (" + mg + " rounds in 1 s)");
            yield return Press(true, 2);
            r.Check(!HaloVehicleSeat.Local.Seated, "B got me out of the scorpion");
            yield return Seconds(1.5f);

            // ---- ghost: the bolts go straight ahead whatever the head or the hands do
            r.Check(HaloVehicleSeat.Local.GetIn(ghost, 0), "into the ghost");
            yield return Seconds(2f);
            head.rotation = ghost.transform.rotation * Quaternion.Euler(-20f, -60f, 0f);
            rh.rotation = ghost.transform.rotation * Quaternion.Euler(10f, 70f, 0f);
            s0 = HaloVehicleGun.TotalShots;
            Hands(0f, 1f, 0f, 0f, Vector2.zero, Vector2.zero);
            float worst = 0f; tEnd = Time.time + 1f;
            while (Time.time < tEnd) { yield return null; if (ghost.Gun.LastShotDir.sqrMagnitude > 0.5f) worst = Mathf.Max(worst, Vector3.Angle(ghost.Gun.LastShotDir, ghost.transform.forward)); }
            Hands(0f, 0f, 0f, 0f, Vector2.zero, Vector2.zero);
            HaloLabRunner.Log("  ghost: " + (HaloVehicleGun.TotalShots - s0) + " bolts, at most " + worst.ToString("0.0") + " deg off the ghost's facing (error cone " + (ghost.Gun.Def.Triggers[0].ErrorAngleMax * Mathf.Rad2Deg * 0.5f).ToString("0.0") + " deg), aim " + ghost.AimYaw + "," + ghost.AimPitch);
            r.Check(HaloVehicleGun.TotalShots - s0 >= 4 && worst < 3.5f && ghost.AimYaw == 0f && ghost.AimPitch == 0f, "the ghost's bolts go straight forward (head 60 left, hand 70 right)");
            Look(r, ghost, new Vector3(-2f, 2.5f, -5f), new Vector3(0f, 1f, 8f), "vr_ghost_forward", 60f);
            yield return Press(true, 2);

            // ---- the own head: never scaled by V; in the VR third-person view the client's shrink is undone, first person keeps it
            var me = StandInAt(hog.SeatOf(0).Enter.position, new Color(0.9f, 0.3f, 0.2f));
            var other = StandInAt(hog.SeatOf(1).Enter.position, new Color(0.2f, 0.5f, 0.9f));
            Transform myHead = me.GetBoneTransform(HumanBodyBones.Head);
            Vector3 normal = myHead.localScale;
            myHead.localScale = normal * 0.0001f;      // what the client does to the own headset avatar's head
            HaloSeatPoses.LabAvatar = k => k == -1 ? me : (k == 1 ? other : null);
            HaloVehicleNet.LabRiders = new List<HaloVehicleRiders.Rider> { new HaloVehicleRiders.Rider { PlayerId = 1, Vehicle = hog, Seat = 1 } };
            HaloSettings.SetFlag("vehicles.vrthird", true);
            r.Check(HaloVehicleSeat.Local.GetIn(hog, 0), "into the warthog driver seat (headset)");
            yield return Seconds(2.2f);
            r.Check(HaloVehicleSeat.Local.ThirdPerson, "the headset driver starts in the third-person view");
            Vector3 thirdScale = HaloSeatPoses.OwnHeadScale(me);
            Vector3 otherScale = HaloSeatPoses.OwnHeadScale(other);
            HaloLabRunner.Log("  head scale: mine " + thirdScale.ToString("0.0000") + " in third person, the passenger's " + otherScale.ToString("0.0000"));
            r.Check(thirdScale.x > 0.5f && otherScale.x > 0.5f, "third person: my head is shown (the client's shrink undone on this screen), the passenger's head untouched");
            Look(r, hog, new Vector3(-5f, 2.6f, 1f), new Vector3(0f, 1.3f, 0f), "vr_third_head", 45f);
            yield return Press(false, 2);      // left Y: first person
            yield return Seconds(0.3f);
            Vector3 firstScale = HaloSeatPoses.OwnHeadScale(me);
            HaloLabRunner.Log("  head scale in first person " + firstScale.ToString("0.0000"));
            r.Check(!HaloVehicleSeat.Local.ThirdPerson && firstScale.x < 0.01f, "first person: the client's own shrink is back");
            yield return Press(true, 2);
            yield return Seconds(1.5f);
            r.Check(HaloSeatPoses.OwnHeadScale(me).x < 0.01f && HaloSeatPoses.OwnHeadScale(other).x > 0.5f, "after getting out the heads are as the client left them");
            HaloVehicleNet.LabRiders = null;
            HaloSeatPoses.LabAvatar = null;
            HaloXr.Simulated = false;
            HaloVehicleInput.LabVr = false;
            HaloVehicleInput.LabLeftHand = HaloVehicleInput.LabRightHand = HaloVehicleInput.LabHead = null;
            HaloVehicles.Clear();
        }

        // ------------------------------------------------------------------ campaign placements, units in seats, AI driving

        static IEnumerator Campaign(HaloLabRunner r)
        {
            string mapName = HaloLabBoot.Arg("-halomap", "a30");
            var entry = r.Game.Install.Map(mapName);
            if (entry == null) { foreach (var e in r.Game.Install.Campaign) if (e.Name == mapName) entry = e; }
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            _map = new HaloMap(entry.File);
            var scen = new HaloScenario(_map);
            Time.captureFramerate = 60;
            HaloVehicles.LabMode = true;
            HaloVehicles.CampaignLab = true;
            HaloLocal.Alive = true;
            HaloCombat.LabMap = _map;
            string why;
            var cyb = HaloCyborg.Load(_map, out why);
            if (cyb != null) HaloCyborg.Use(cyb);

            HaloScenario.Placement hogP = null, bansheeP = null;
            string hogName = "";
            foreach (var p in scen.Vehicles)
            {
                if (p.Type == null) continue;
                string n = p.Type.Name;
                if (hogP == null && n.Contains("warthog") && p.NameIndex >= 0 && p.NameIndex < scen.ObjectNames.Count) { hogP = p; hogName = scen.ObjectNames[p.NameIndex]; }
                if (bansheeP == null && n.Contains("banshee")) bansheeP = p;
            }
            if (!r.Check(hogP != null, "a named warthog placement in " + mapName + " ('" + hogName + "')")) yield break;
            Vector3 at = HaloSpace.Pos(hogP.Position);
            var stage = new GameObject("vehicle campaign lab");
            HaloBipeds.LabParent = stage.transform;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.SetParent(stage.transform, false); floor.transform.position = at - Vector3.up * 0.6f; floor.transform.localScale = new Vector3(40f, 1f, 40f); floor.layer = HaloLayers.World;
            var fm = new Material(Shader.Find("Universal Render Pipeline/Lit")); fm.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f));
            floor.GetComponent<Renderer>().sharedMaterial = fm;
            var sun = new GameObject("sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            Physics.SyncTransforms();

            var hog = HaloVehicles.SpawnPlacement(hogP, hogName, stage.transform);
            r.Check(hog != null && hog.Campaign && HaloObjects.Find(hogName) == hog.gameObject, "the warthog is placed and registered in HaloObjects as '" + hogName + "'");
            float rotErr = Quaternion.Angle(hog.transform.rotation, HaloSpace.Euler(hogP.Rotation.X, hogP.Rotation.Y, hogP.Rotation.Z));
            r.Check(rotErr < 0.5f, "the placement's full rotation (yaw " + (hogP.Rotation.X * Mathf.Rad2Deg).ToString("0") + ", pitch " + (hogP.Rotation.Y * Mathf.Rad2Deg).ToString("0.0") + ", roll " + (hogP.Rotation.Z * Mathf.Rad2Deg).ToString("0.0") + "; off by " + rotErr.ToString("0.00") + " deg)");
            yield return Seconds(3.5f);

            // a marine on the chain gun, another at the wheel
            HaloTag marine = null;
            foreach (var t in _map.OfClass("bipd")) if (t.Name.Contains("marine")) { marine = t; break; }
            if (!r.Check(marine != null, "a marine biped in " + mapName + " (" + (marine != null ? marine.Name : "none") + ")")) yield break;
            var gunner = HaloBipeds.Spawn(marine, hog.transform.position + hog.transform.right * 3f, 0f, HaloTeam.Human);
            var driver = HaloBipeds.Spawn(marine, hog.transform.position - hog.transform.right * 3f, 0f, HaloTeam.Human);
            if (!r.Check(gunner != null && driver != null, "two marines spawned")) yield break;
            r.Check(hog.SeatUnit(2, gunner), "the marine takes the warthog's gunner seat (SeatUnit)");
            yield return null; yield return null;
            r.Check(hog.UnitIn(2) == gunner && hog.SeatOf(2).Occupant == HaloVehicle.UnitOccupant, "the gunner seat counts as taken by the unit");
            HaloVehicle cv; int cs;
            HaloVehicleEntry.Choose(HaloVehicles.All, hog.SeatOf(2).Enter.position, out cv, out cs);
            r.Check(cs != 2, "the player is not offered the marine's seat (offered " + cs + ")");
            yield return Seconds(1.5f);
            Look(r, hog, new Vector3(-3f, 3.5f, -5f), new Vector3(0f, 1.8f, 0f), "campaign_marine_gunner", 50f);

            // the AI fires the chain gun at a point to the right
            int s0 = HaloVehicleGun.TotalShots;
            Vector3 target = hog.transform.position + hog.transform.right * 30f + Vector3.up;
            float end = Time.time + 1.5f;
            while (Time.time < end) { hog.SetAiInput(0f, 0f, target, true); yield return null; }
            int shots = HaloVehicleGun.TotalShots - s0;
            HaloLabRunner.Log("  AI gunner: yaw " + (hog.AimYaw * Mathf.Rad2Deg).ToString("0") + " deg, " + shots + " rounds, shooter " + hog.Gun.Shooter);
            r.Check(shots >= 8 && Mathf.Abs(Mathf.DeltaAngle(hog.AimYaw * Mathf.Rad2Deg, -90f)) < 10f && hog.Gun.Shooter == -2, "the AI gunner turns the chain gun to its aim point and fires (shooter -2)");
            Look(r, hog, new Vector3(-4f, 4f, -6f), new Vector3(4f, 1.5f, 0f), "campaign_ai_gunner_firing", 55f);

            // the AI drives
            r.Check(hog.SeatUnit(0, driver), "the second marine takes the wheel");
            Vector3 from = hog.transform.position;
            end = Time.time + 3f;
            while (Time.time < end) { hog.SetAiInput(1f, hog.SteerToward(from + hog.transform.forward * 50f + hog.transform.right * 20f), target, false); yield return null; }
            float moved = (hog.transform.position - from).magnitude;
            HaloLabRunner.Log("  AI driver: " + moved.ToString("0.0") + " m in 3 s, " + hog.SpeedMs.ToString("0.0") + " m/s, AiDriven " + hog.AiDriven);
            r.Check(moved > 15f && hog.AiDriven, "the AI drives the warthog through SetAiInput");
            Look(r, hog, new Vector3(-6f, 3f, -7f), new Vector3(0f, 1.2f, 2f), "campaign_ai_driving", 55f);
            end = Time.time + 2f;
            while (Time.time < end) { hog.SetAiInput(0f, 0f, target, false); yield return null; }

            // campaign vitality: a banshee (100 body points) is destroyed; the MP rule would have shrugged it off
            if (bansheeP != null)
            {
                var b = HaloVehicles.SpawnPlacement(bansheeP, "", stage.transform);
                yield return null;
                b.TakeHit(new HaloHit { Amount = 60f, SourcePlayer = 0, Point = b.transform.position, Direction = Vector3.forward });
                float after60 = b.Vitality;
                b.TakeHit(new HaloHit { Amount = 60f, SourcePlayer = 0, Point = b.transform.position, Direction = Vector3.forward });
                r.Check(after60 > 0.35f && after60 < 0.45f && b.Destroyed && !b.EnterableByPlayer, "the campaign banshee takes 60 + 60 of its 100 body points and is destroyed (" + after60.ToString("0.00") + " after the first)");
            }
            HaloVehicles.Remove(hog);
            r.Check(HaloObjects.Find(hogName) == null, "Remove unregisters the name");
            HaloVehicles.CampaignLab = false;
            HaloVehicles.Clear();
        }

        // ------------------------------------------------------------------ a10's cryotube: unit_open, then out

        static IEnumerator Cryo(HaloLabRunner r)
        {
            HaloInstall.MapEntry entry = null;
            foreach (var e in r.Game.Install.Campaign) if (e.Name == "a10") entry = e;
            if (!r.Check(entry != null, "a10 in the install")) yield break;
            _map = new HaloMap(entry.File);
            var scen = new HaloScenario(_map);
            Time.captureFramerate = 60;
            HaloVehicles.LabMode = true; HaloVehicles.CampaignLab = true;
            HaloLocal.Alive = true;
            string why;
            var cyb = HaloCyborg.Load(_map, out why);
            if (cyb != null) HaloCyborg.Use(cyb);
            HaloScenario.Placement cp = null; string name = "";
            foreach (var p in scen.Vehicles)
                if (p.Type != null && p.Type.Name.Contains("cryotube")) { cp = p; name = p.NameIndex >= 0 && p.NameIndex < scen.ObjectNames.Count ? scen.ObjectNames[p.NameIndex] : ""; break; }
            if (!r.Check(cp != null, "a cryotube placement in a10 ('" + name + "')")) yield break;
            var stage = new GameObject("cryo lab");
            var sun = new GameObject("sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);
            r.Cam.backgroundColor = new Color(0.2f, 0.22f, 0.26f);
            var cryo = HaloVehicles.SpawnPlacement(cp, name, stage.transform);
            if (!r.Check(cryo != null && cryo.HasHatch, "the cryotube is placed and has Halo's opening animation")) yield break;
            var me = StandInAt(cryo.SeatOf(0).Marker.position, new Color(0.3f, 0.6f, 0.3f));
            HaloSeatPoses.LabAvatar = k => k == -1 ? me : null;
            r.Check(HaloVehicleSeat.Local.GetIn(cryo, 0), "the player is seated in the cryotube (" + cryo.SeatOf(0).Label + ")");
            yield return Seconds(1f);
            Transform door = cryo.Model.Node("frame cryo tube door");
            Vector3 doorWas = door.position; Quaternion doorRot = door.rotation;
            Vector3 eye = cryo.transform.position + cryo.transform.forward * 3.2f + Vector3.up * 1.4f, look = cryo.transform.position + Vector3.up * 1.2f;
            r.Aim(eye, look, 50f); r.Shot("cryo_closed");
            HaloVehicleInput.Simulated = true;
            HaloVehicleInput.Sim = new HaloVehicleInput.Intent { Exit = true };
            yield return null; yield return null;
            HaloVehicleInput.Sim = default(HaloVehicleInput.Intent);
            r.Check(HaloVehicleSeat.Local.Seated, "getting out is refused while the tube is closed");
            cryo.Open(true);
            yield return Seconds(2.5f);
            r.Aim(eye, look, 50f); r.Shot("cryo_opening");
            yield return Seconds(3f);
            float moved = Mathf.Max((door.position - doorWas).magnitude, Quaternion.Angle(door.rotation, doorRot) * Mathf.Deg2Rad);
            HaloLabRunner.Log("  cryotube door moved " + moved.ToString("0.00") + " (m or rad) (open " + cryo.IsOpen + ")");
            r.Check(moved > 0.3f, "unit_open swings the cryotube door (" + moved.ToString("0.00") + ")");
            r.Aim(eye, look, 50f); r.Shot("cryo_open");
            HaloVehicleInput.Sim = new HaloVehicleInput.Intent { Exit = true };
            yield return null; yield return null;
            HaloVehicleInput.Sim = default(HaloVehicleInput.Intent);
            r.Check(!HaloVehicleSeat.Local.Seated, "once open the player gets out (Action / B)");
            cryo.Open(false);
            yield return Seconds(5.5f);
            r.Check(Quaternion.Angle(door.rotation, doorRot) < 2f && (door.position - doorWas).magnitude < 0.05f, "unit_close shuts it again");
            HaloVehicleInput.Simulated = false; HaloSeatPoses.LabAvatar = null; HaloVehicles.CampaignLab = false;
            HaloVehicles.Clear();
        }

        // ------------------------------------------------------------------ a30's dropship (lz_cship) and an AI banshee

        static void Watch(HaloLabRunner r, Transform t, Vector3 offset, string name)
        {
            r.Aim(t.position + offset, t.position, 55f);
            r.Shot(name);
        }

        static IEnumerator Dropship(HaloLabRunner r)
        {
            HaloInstall.MapEntry entry = null;
            foreach (var e in r.Game.Install.Campaign) if (e.Name == "a30") entry = e;
            if (!r.Check(entry != null, "a30 in the install")) yield break;
            _map = new HaloMap(entry.File);
            var scen = new HaloScenario(_map);
            Time.captureFramerate = 60;
            HaloVehicles.LabMode = true; HaloVehicles.CampaignLab = true;
            HaloLocal.Alive = true;
            HaloCombat.LabMap = _map;
            string why;
            var cyb = HaloCyborg.Load(_map, out why);
            if (cyb != null) HaloCyborg.Use(cyb);
            HaloCinematic.Load(_map);
            var stage = new GameObject("dropship lab");
            HaloBipeds.LabParent = stage.transform;
            var sun = new GameObject("sun").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.5f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);

            HaloScenario.Placement cp = null;
            foreach (var p in scen.Vehicles)
                if (p.Type != null && p.NameIndex >= 0 && p.NameIndex < scen.ObjectNames.Count && scen.ObjectNames[p.NameIndex] == "lz_cship") cp = p;
            if (!r.Check(cp != null, "the scenario's lz_cship placement")) yield break;
            var ship = HaloVehicles.SpawnPlacement(cp, "lz_cship", stage.transform);
            if (!r.Check(ship != null, "lz_cship (" + cp.Type.Name + ") placed")) yield break;
            Vector3 flagPos; Quaternion flagRot;
            r.Check(HaloCinematic.Flag("lz_cship_flag", out flagPos, out flagRot), "cutscene flag lz_cship_flag");
            ship.transform.SetPositionAndRotation(flagPos, flagRot);
            ship.Open(false);

            // four grunts in the passenger seats (vehicle_load_magic lz_cship "passenger")
            HaloTag grunt = null;
            foreach (var t in _map.OfClass("bipd")) if (t.Name.Contains("grunt")) { grunt = t; break; }
            var troops = new List<HaloUnit>();
            for (int s = 0, n = 0; s < ship.Seats.Count && n < 4; s++)
            {
                if (ship.Seats[s].Label.IndexOf("passenger", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                var u = HaloBipeds.Spawn(grunt, flagPos, 0f, HaloTeam.Covenant);
                if (u != null && ship.SeatUnit(s, u)) { troops.Add(u); n++; }
            }
            r.Check(troops.Count == 4, "four grunts ride in the dropship's passenger seats (" + troops.Count + ")");
            r.Check(HaloCinematic.PlayRecording(ship.gameObject, "lz_cship_enter", HaloCinematic.RecordingEnd.Hover, "lz_cship"), "recording_play_and_hover lz_cship lz_cship_enter");
            int ticks = HaloCinematic.RecordingTime(ship.gameObject);
            HaloLabRunner.Log("  lz_cship_enter: " + ticks + " ticks (" + (ticks / 30f).ToString("0.0") + " s), from " + flagPos.ToString("F0"));
            Vector3 start = ship.transform.position;
            yield return Seconds(2f);
            Watch(r, ship.transform, new Vector3(-30f, 12f, -30f), "dropship_flying");
            float end = Time.time + ticks / 30f + 1f;
            while (Time.time < end && HaloCinematic.IsRecording(ship.gameObject)) yield return null;
            Vector3 hover = ship.transform.position;
            HaloLabRunner.Log("  flew " + (hover - start).magnitude.ToString("0") + " m; now at " + hover.ToString("F1") + ", kinematic " + ship.Body.isKinematic);
            r.Check((hover - start).magnitude > 20f, "the dropship flew its recording");
            // a floor 6 m under it: where the troops land
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.SetParent(stage.transform, false); floor.transform.position = hover - Vector3.up * 6f; floor.transform.localScale = new Vector3(10f, 1f, 10f); floor.layer = HaloLayers.World;
            Physics.SyncTransforms();
            yield return Seconds(2f);
            float drift = (ship.transform.position - hover).magnitude;
            r.Check(drift < 0.1f && ship.Body.isKinematic, "the dropship hovers at the end of the recording (moved " + drift.ToString("0.00") + " m in 2 s)");
            Watch(r, ship.transform, new Vector3(-22f, 4f, -22f), "dropship_hovering");
            ship.Open(true);
            yield return Seconds(1f);
            foreach (var u in troops)
            {
                int si = ship.SeatOfUnit(u);
                Vector3 sp; float sy;
                if (si >= 0) { HaloVehicleSeat.StandPoint(ship, ship.Seats[si], out sp, out sy); HaloLabRunner.Log("  unseat seat " + si + " " + ship.Seats[si].Label + " stand point " + sp.ToString("F1") + " bounds min y " + ship.ShapeBounds().min.y.ToString("F1")); }
                ship.UnseatUnit(u);
                HaloLabRunner.Log("  after unseat: seat " + (u.Seat != null ? u.Seat.name : "none") + " at " + u.transform.position.ToString("F1"));
            }
            yield return Seconds(0.5f);
            foreach (var u in troops) HaloLabRunner.Log("  0.5 s later at " + u.transform.position.ToString("F1") + " parent " + (u.transform.parent != null ? u.transform.parent.name : "-"));
            Watch(r, ship.transform, new Vector3(-20f, 0f, -20f), "dropship_drop");
            yield return Seconds(2.5f);
            int landed = 0;
            foreach (var u in troops) if (u != null && u.Seat == null && u.transform.position.y < hover.y - 4f) landed++;
            HaloLabRunner.Log("  troops out: " + landed + "/4 below the hull" + (troops.Count > 0 && troops[0] != null ? ", first at " + troops[0].transform.position.ToString("F1") : ""));
            r.Check(landed == 4, "the four grunts dropped out of the hovering dropship to the ground");
            Watch(r, ship.transform, new Vector3(-20f, -2f, -20f), "dropship_troops_down");

            // an AI banshee climbing and diving toward a point
            HaloScenario.Placement bp = null;
            foreach (var p in scen.Vehicles) if (p.Type != null && p.Type.Name.Contains("banshee")) { bp = p; break; }
            if (bp != null)
            {
                var b = HaloVehicles.SpawnPlacement(bp, "", stage.transform);
                b.transform.position = hover + new Vector3(30f, 0f, 0f);
                var pilot = HaloBipeds.Spawn(grunt, b.transform.position, 0f, HaloTeam.Covenant);
                r.Check(b.SeatUnit(0, pilot), "an AI pilot in the banshee");
                float y0 = b.transform.position.y;
                Vector3 goal = b.transform.position + b.transform.forward * 80f + Vector3.up * 30f;
                end = Time.time + 4f;
                while (Time.time < end) { b.SetAiFlight(1f, goal, Vector3.zero, false); yield return null; }
                float climb = b.transform.position.y - y0;
                HaloLabRunner.Log("  AI banshee: climbed " + climb.ToString("0.0") + " m in 4 s at " + b.SpeedMs.ToString("0.0") + " m/s");
                r.Check(climb > 8f && b.SpeedMs > 10f, "the AI banshee flies up toward its point (SetAiFlight)");
                Vector3 dive = b.transform.position + b.transform.forward * 60f - Vector3.up * 25f + b.transform.right * 40f;
                end = Time.time + 1.5f;
                while (Time.time < end) { b.SetAiFlight(1f, dive, Vector3.zero, true); yield return null; }
                Watch(r, b.transform, -b.transform.forward * 12f + Vector3.up * 3f, "banshee_ai_banking");
            }
            HaloVehicles.CampaignLab = false;
            HaloVehicles.Clear();
        }

        // ------------------------------------------------------------------ vehicle sounds (engine loops, skids, crashes)

        static IEnumerator Sfx(HaloLabRunner r)
        {
            var entry = r.Game.Install.Map(HaloLabBoot.Arg("-halomap", "bloodgulch"));
            if (!r.Check(entry != null, "the map")) yield break;
            _map = new HaloMap(entry.File);
            Time.captureFramerate = 60;
            HaloVehicles.LabMode = true; HaloLocal.Alive = true;
            HaloCombat.LabMap = _map;
            HaloAudio.Preload(_map);
            var stage = new GameObject("vehicle sfx lab");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.SetParent(stage.transform, false); floor.transform.localScale = new Vector3(60f, 1f, 60f); floor.layer = HaloLayers.World;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.SetParent(stage.transform, false); wall.transform.position = new Vector3(0f, 2f, 90f); wall.transform.localScale = new Vector3(30f, 4f, 1f); wall.layer = HaloLayers.World;
            Physics.SyncTransforms();
            string[] kinds = { "mp_warthog", "ghost", "scorpion", "banshee", "rwarthog" };
            var vs = new List<HaloVehicle>();
            for (int k = 0; k < kinds.Length; k++) { var t = Vehi(kinds[k]); if (t != null) vs.Add(HaloVehicles.AddLab(t, new Vector3(-40f + k * 12f, 0.6f, -60f), 0f, stage.transform)); }
            yield return Seconds(3.5f);
            // every vehicle moved like a co-op follower / remote driver: kinematic, no velocity given - the engine follows its motion
            float end = Time.time + 2f;
            while (Time.time < end)
            {
                foreach (var v in vs) { v.Remote(); v.Place(v.transform.position + v.transform.forward * 12f * Time.deltaTime, v.transform.rotation, Vector3.zero); }
                yield return null;
            }
            foreach (var v in vs)
            {
                HaloLabRunner.Log("  " + v.name + ": engine loops " + v.EngineLoops + ", scale " + v.EngineScale.ToString("0.00") + " at " + v.SpeedMs.ToString("0.0") + " m/s (measured)");
                r.Check(v.EngineLoops > 0 && v.EngineScale > 0.2f, v.name + ": its engine loop runs and follows a kinematic (follower) motion");
            }
            // skids and a crash with the warthog
            var hog = vs[0];
            hog.transform.SetPositionAndRotation(new Vector3(0f, 0.6f, 0f), Quaternion.identity); Physics.SyncTransforms();
            r.Check(HaloVehicleSeat.Local.GetIn(hog, 0), "into the warthog");
            int imp0 = HaloVehicle.Skids, sfx0 = HaloVehicle.SfxCount;
            Sim(new HaloVehicleControls { Throttle = 1f });
            yield return Seconds(3f);
            Sim(new HaloVehicleControls { Throttle = 1f, Turn = 1f });
            yield return Seconds(1.2f);
            int skids = HaloVehicle.Skids - imp0;
            HaloLabRunner.Log("  warthog hard turn: max slip " + HaloVehicle.MaxSlip.ToString("0.0") + " m/s, impacts silent " + HaloImpacts.Silent + ", foot " + HaloImpacts.MaterialEffectsOf(hog.Def.Tag) + "; " + skids + " tire-slip sounds, scale " + hog.EngineScale.ToString("0.00"));
            r.Check(skids > 0, "the warthog's tires skid in a hard turn (HaloImpacts TireSlip)");
            hog.transform.SetPositionAndRotation(new Vector3(0f, 0.6f, 40f), Quaternion.identity); hog.Body.linearVelocity = Vector3.forward * 20f; hog.Body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
            Sim(new HaloVehicleControls { Throttle = 1f });
            int crash0 = HaloVehicle.SfxCount, imp1 = HaloImpacts.Played;
            end = Time.time + 4f;
            while (Time.time < end && HaloVehicle.SfxCount == crash0) yield return null;
            HaloLabRunner.Log("  crash: vehicle sounds +" + (HaloVehicle.SfxCount - crash0) + ", impacts +" + (HaloImpacts.Played - imp1) + " (crash sound " + (hog.Def.CrashSound != null ? hog.Def.CrashSound.Name : "none") + ")");
            r.Check(HaloVehicle.SfxCount > crash0, "the warthog's crash sound plays when it hits the wall");
            HaloVehicleInput.Simulated = false;
            HaloVehicleSeat.Local.GetOut("lab");
            HaloVehicles.Clear();
        }
    }
}

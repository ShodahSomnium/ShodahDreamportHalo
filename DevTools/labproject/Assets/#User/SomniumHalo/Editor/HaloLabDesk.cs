using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Lab "desk": monitor players' avatars posed by Halo's own third-person animations (HaloDeskPlayer: the hidden cyborg puppet of Blood Gulch's
    /// characters\cyborg_mp, the BO2 / Skyrim poser) on a humanoid stand-in (the SDK template's RPM avatar, its Animator off), holding the real
    /// weapons built from the map (assault rifle, pistol). Cases, each its own copy stepped at 60 fps with a scripted state: AR idle, AR aimed 30 degrees
    /// up, AR reload half-way, AR firing, AR walking, pistol idle, pistol reload, first person (AR from the eye), and seated in a Warthog (driver with
    /// the hands on the wheel grips, gunner, passenger with the AR). Pictures desk_&lt;case&gt;.png, "LAB: desk ..." lines (animations, wrists' distance
    /// from their targets, the barrel's angle from the aim). Also lab "desk-data": the animation graph numbers only.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabDesk
    {
        private const string StandIn = "Assets/SomniumSpaceTemplates/ItemsTemplate/Models/Avatars/RPM Men.fbx";

        static HaloLabDesk()
        {
            HaloLabRunner.Register("desk", r => Run(r));
            HaloLabRunner.Register("desk-data", r => Data(r));
            HaloLabRunner.Register("desk-monitor", r => Monitor(r));
            HaloLabRunner.Register("weapon-anims", r => WeaponAnims(r));
            HaloLabRunner.Register("cine-pose", r => CinePose(r));
        }

        private static HaloMap OpenMap(HaloLabRunner r, string name)
        {
            HaloInstall inst = r.Game.Install;
            foreach (HaloInstall.MapEntry e in inst.Multiplayer) if (e.Name == name) return new HaloMap(e.File);
            return null;
        }

        private static IEnumerator Data(HaloLabRunner r)
        {
            HaloMap map = OpenMap(r, HaloLabBoot.Arg("-halomap", "bloodgulch"));
            string why;
            HaloCyborg c = map != null ? HaloCyborg.Load(map, out why) : null;
            r.Check(c != null, "cyborg loaded");
            if (c == null) yield break;
            int bad = 0;
            foreach (HaloAnimation a in c.Graph.Animations) if (!a.Decode()) { bad++; HaloLabRunner.Log("desk-data: " + a.Name + ": " + a.Error); }
            r.Check(bad == 0, "every animation decodes (" + c.Graph.Animations.Length + ", " + bad + " failed)");
            yield return null;
        }

        private struct Case
        {
            public string Name, Weapon, Seat;
            public bool First, Walk, Fire, Reload, Aim, Death;
            public float Pitch, Fov;
        }

        /// <summary>
        /// Lab "desk-monitor": a Blood Gulch slayer match as the HUD lab starts it (the real Combat weapons, the HUD), the lab camera standing for the
        /// client's monitor camera at the HUD lab's view: the first-person weapon framed without an avatar, then held by the stand-in avatar
        /// (70 and 40 degree cameras). Pictures monitor-*.png.
        /// </summary>
        private static IEnumerator Monitor(HaloLabRunner r)
        {
            if (Object.FindFirstObjectByType<CharacterController>() == null)
            {
                var pgo = new GameObject("Lab Player");
                pgo.transform.position = HaloSpace.RoomOrigin;
                var cc = pgo.AddComponent<CharacterController>();
                cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
                pgo.layer = HaloLayers.Avatar;
            }

            var rules = HaloRules.Defaults(HaloScenario.GameType.Slayer, false);
            rules.MapName = HaloLabBoot.Arg("-halomap", "bloodgulch");
            HaloMatchControl.StartMatch(rules);
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Playing, 600f, "the match to start");
            if (r.Failed) yield break;
            HaloDesk.LabCamera = () => r.Cam;
            // in the client Camera.main is both Desk's camera and Combat's head (HaloBody -> HaloPlayer.Head); the runner's camera is
            // disabled and untagged, so without this Combat had no head here and never drew a weapon
            string camTag = r.Cam.tag; bool camOn = r.Cam.enabled;
            r.Cam.tag = "MainCamera"; r.Cam.enabled = true;
            HaloViewScale.LabHold = true;
            Vector3 feet;
            if (!HaloPlayer.TryFeet(out feet)) feet = Vector3.zero;
            float yaw; string src;
            if (!HaloPlayer.TryFacingYaw(out yaw, out src)) yaw = 0f;
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 eye = feet + Vector3.up * 1.7f;
            r.Aim(eye, eye + fwd * 10f - Vector3.up * 0.6f, 70f);
            r.Cam.aspect = 16f / 9f;
            HaloHud.LabAttach(r.Cam, false);
            yield return r.Wait(() => HaloDesk.HeldWeapon != null || HaloCombat.HeldModel != null, 30f, "a held weapon (Combat)");
            yield return r.Frames(30);
            PoseNow(r);      // (Combat puts the model back under its pivot in Update, Desk places it in LateUpdate: the coroutine runs between - pose again now)
            r.Check(HaloDesk.HoldsWeapon, "Desk frames the held weapon in first person without an avatar");
            Debug(r);
            r.Shot("monitor-noavatar", 1280, 720);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StandIn);
            GameObject av = Object.Instantiate(prefab);
            Paint(av, new Material(Shader.Find("Universal Render Pipeline/Lit")));
            Animator anim = av.GetComponentInChildren<Animator>();
            anim.enabled = false;
            Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
            Quaternion facing = Quaternion.LookRotation(fwd, Vector3.up);
            av.transform.rotation = facing;
            Vector3 off = head.position - av.transform.position;
            av.transform.position = r.Cam.transform.position - facing * new Vector3(0f, 0.07f, 0.1f) - off;
            HaloDeskBody.LabOwnAvatar = () => anim;
            HaloHeadHide.LabHold = true;
            foreach (float fov in new[] { 70f, 40f })
            {
                r.Cam.fieldOfView = fov;
                yield return r.Frames(40);
                PoseNow(r);
                r.Check(HaloDesk.FirstPerson && HaloDesk.HoldsWeapon, "first person with the avatar holding the weapon (fov " + fov + ")");
                Debug(r);
                r.Shot("monitor-avatar" + (int)fov, 1280, 720);
            }

            HaloDeskBody.LabOwnAvatar = null;
            HaloHeadHide.LabHold = false;

            // ---- (Vehicles) seated with the weapon: the warthog passenger seat (Halo: "allows weapons", no third-person flag - a monitor player
            // looks from the seat's eye). The gun must stay in the vehicle (user: "their gun is displayed outside the car far away"): Combat's
            // view model under the seat's eye, the seated avatar's hands on it
            HaloVehicle hog = null;
            foreach (HaloVehicle v in HaloVehicles.All) if (v != null && v.SeatOf(1) != null && v.SeatOf(1).Def.AllowsWeapons) { hog = v; break; }
            r.Check(hog != null, "a vehicle with a weapon-allowing passenger seat in the match (" + HaloVehicles.All.Count + " vehicles)");
            if (hog != null)
            {
                HaloSeatPoses.LabAvatar = k => k == -1 ? anim : null;
                r.Cam.fieldOfView = 70f;
                r.Check(HaloVehicleSeat.Local.GetIn(hog, 1), "I get into " + hog.name + "'s passenger seat '" + hog.SeatOf(1).Label + "'");
                // (game time, not frames: with the lab camera on, 150 frames were only 0.23 s - mid 'W-passenger enter' (0.93 s))
                float seatedAt = Time.time + 2.5f;
                while (Time.time < seatedAt) yield return null;
                var me = HaloSeatPoses.Get(-1);
                Transform g = HaloDesk.HeldWeapon != null ? HaloDesk.HeldWeapon : HaloCombat.HeldModel;
                Transform seat = hog.SeatOf(1).Marker;
                Transform rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
                float gunToSeat = g != null ? (g.position - seat.position).magnitude : -1f;
                float gunToCam = g != null ? (g.position - r.Cam.transform.position).magnitude : -1f;
                float handToGun = g != null && rh != null ? (rh.position - g.position).magnitude : -1f;
                HaloLabRunner.Log("  seated: " + HaloSeatPoses.Describe(-1) + ", view " + (me != null && me.View) + ", gun " + gunToSeat.ToString("0.00") + " m from the seat marker, " + gunToCam.ToString("0.00") +
                                  " m from the camera, my right hand " + handToGun.ToString("0.00") + " m from the gun; camera " + (r.Cam.transform.position - seat.position).magnitude.ToString("0.00") + " m from the seat");
                r.Check(me != null && me.Poser.Posed && HaloSeatPoses.Describe(-1).StartsWith("Sit"), "my avatar sits in the passenger seat (" + HaloSeatPoses.Describe(-1) + ")");
                r.Check(g != null && g.gameObject.activeInHierarchy && gunToSeat >= 0f && gunToSeat < 1.3f, "my gun is in the vehicle, at the seat (" + gunToSeat.ToString("0.00") + " m from the seat marker)");
                r.Check(gunToCam >= 0f && gunToCam < 0.8f, "first person: my view draws Combat's view model under the seat's eye (" + gunToCam.ToString("0.00") + " m from the camera)");
                float viewHand = me != null ? me.ViewHandToGun : -1f;
                r.Check(me != null && me.ArmsValid && viewHand >= 0f && viewHand < 0.25f, "first person: my seated avatar's right hand holds the view model (" + viewHand.ToString("0.00") + " m, arms laid on for my camera only)");
                int drawn0 = HaloSeatPoses.LabViewGunDrawnFrame;
                HaloSeatPoses.LabViewHeadHiddenFrame = -1;
                r.Shot("monitor-passenger-view", 1280, 720);
                r.Check(HaloSeatPoses.LabViewGunDrawnFrame == drawn0, "the first-person camera at the seat's eye is not given the outside gun (it keeps the view model)");
                // Halo's first person shows the view model and the hands, nothing else of the body (user picture: a big white piece of the own
                // left arm / shoulder over the lower-left quarter): with the first-person arms laid on, the shoulders, chest, neck and head stay out
                // of the own camera's view; the left hand reaches its place on the view model; the head was shrunk for that camera only
                if (me != null && me.ArmsValid)
                {
                    HaloSeatPoses.LabViewBody(true);
                    Camera vc = r.Cam;
                    HumanBodyBones[] body = { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg };
                    string seen = "";
                    var sb = new System.Text.StringBuilder();
                    foreach (HumanBodyBones b in new[] { HumanBodyBones.Head, HumanBodyBones.Neck, HumanBodyBones.UpperChest, HumanBodyBones.Chest, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg })
                    {
                        Transform bt = anim.GetBoneTransform(b);
                        if (bt == null) continue;
                        Vector3 vpt = vc.WorldToViewportPoint(bt.position);
                        sb.Append(" " + b + " (" + vpt.x.ToString("0.00") + "," + vpt.y.ToString("0.00") + "," + vpt.z.ToString("0.00") + ")");
                    }
                    foreach (HumanBodyBones b in body)
                    {
                        Transform bt = anim.GetBoneTransform(b);
                        if (bt == null) continue;
                        Vector3 vpt = vc.WorldToViewportPoint(bt.position);
                        if (vpt.z > vc.nearClipPlane && vpt.x > -0.02f && vpt.x < 1.02f && vpt.y > -0.02f && vpt.y < 1.02f) seen += " " + b;
                    }
                    // the upper arms (shoulder -> elbow) along their length too: they must not pass beside the eye into the view (the forearms may)
                    for (int sd = 0; sd < 2; sd++)
                    {
                        Transform ua = anim.GetBoneTransform(sd == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm), la = anim.GetBoneTransform(sd == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                        if (ua == null || la == null) continue;
                        for (int i = 0; i <= 10; i++)
                        {
                            Vector3 vpt = vc.WorldToViewportPoint(Vector3.Lerp(ua.position, la.position, i * 0.09f));
                            if (vpt.z > vc.nearClipPlane && vpt.x > -0.02f && vpt.x < 1.02f && vpt.y > -0.02f && vpt.y < 1.02f) { seen += sd == 0 ? " left upper arm" : " right upper arm"; break; }
                        }
                    }
                    HaloSeatPoses.LabViewBody(false);
                    HaloLabRunner.Log("  first-person bones in the own camera (viewport x, y, depth):" + sb + "; left hand " + me.ViewLeftErr.ToString("0.00") + " m from its view-model target, view model drawn x" + me.ViewScale.ToString("0.00") + ", body drawn like on foot " + me.ViewBody + " (head moved " + me.ViewShift.ToString("0.00") + " m to the eye) for my camera");
                    r.Check(seen.Length == 0, "first person: no shoulder / upper arm / chest / neck / knee joint of my seated avatar in my own view (in view:" + (seen.Length > 0 ? seen : " none") + ")");
                    r.Check(me.ViewLeftErr >= 0f && me.ViewLeftErr < 0.06f, "first person: my left hand reaches its place on the view model (" + me.ViewLeftErr.ToString("0.00") + " m)");
                }
                r.Check(HaloSeatPoses.LabViewHeadHiddenFrame == Time.frameCount && HaloSeatPoses.OwnHeadScale(anim).x > 0.5f, "first person: my head was shrunk for my own seat camera only (given back after drawing: scale " + HaloSeatPoses.OwnHeadScale(anim).x.ToString("0.00") + ")");
                {
                    // the picture: the stand-in avatar is painted white - near-white pixels in the lower-left quarter (the motion tracker's corner left out)
                    var tex = new Texture2D(2, 2);
                    tex.LoadImage(System.IO.File.ReadAllBytes(System.IO.Path.Combine(r.Out, "monitor-passenger-view.png")));
                    Color32[] px = tex.GetPixels32();
                    int w = tex.width, h = tex.height, white = 0, all = 0;
                    for (int y = 0; y < h / 2; y++)
                        for (int x = 0; x < w / 2; x++)
                        {
                            if (x < w * 0.13f && y < h * 0.22f) continue;
                            Color32 c = px[y * w + x];
                            all++;
                            if (c.r > 190 && c.g > 190 && c.b > 190) white++;
                        }
                    Object.Destroy(tex);
                    float share = all > 0 ? white / (float)all : 1f;
                    HaloLabRunner.Log("  first-person picture: " + (share * 100f).ToString("0.0") + " % near-white (my avatar) in the lower-left quarter");
                    r.Check(share < 0.05f, "first person: no piece of my avatar over the lower-left quarter of my view (" + (share * 100f).ToString("0.0") + " % near-white)");
                }
                // (an avatar is skinned once a frame: the outside picture in a new frame, the own first-person body given back, as other cameras
                // than the own one see it when they draw first - mirrors draw the copies, other players their own seat pose)
                yield return null;
                HaloSeatPoses.LabViewBody(false);
                Vector3 side = hog.transform.right * (hog.transform.InverseTransformPoint(seat.position).x >= 0f ? 1f : -1f);
                r.Aim(seat.position + side * 2.6f + hog.transform.forward * 1.5f + Vector3.up * 1.1f, seat.position + Vector3.up * 0.5f, 50f);
                r.Shot("monitor-passenger-outside", 1280, 720);
                // seen from outside (any camera but the own first-person one): the gun in the seated avatar's hands, Halo's W-passenger rifle idle at
                // the chest - not the arms raised to a gun at the eye
                Transform hd = anim.GetBoneTransform(HumanBodyBones.Head);
                Vector3 drawn = HaloSeatPoses.LabViewGunDrawn;
                bool drew = HaloSeatPoses.LabViewGunDrawnFrame != drawn0;
                float handToDrawn = rh != null ? (rh.position - drawn).magnitude : -1f;
                float handBelowHead = rh != null && hd != null ? hd.position.y - rh.position.y : -1f;
                float drawnToSeat = (drawn - seat.position).magnitude;
                HaloLabRunner.Log("  outside: gun drawn " + drew + " at " + drawnToSeat.ToString("0.00") + " m from the seat marker, my right hand " + handToDrawn.ToString("0.00") + " m from it, " + handBelowHead.ToString("0.00") + " m below my head");
                r.Check(drew && handToDrawn >= 0f && handToDrawn < 0.25f && drawnToSeat < 1.3f, "seen from outside my seated avatar's right hand holds the gun in the vehicle (" + handToDrawn.ToString("0.00") + " m, " + drawnToSeat.ToString("0.00") + " m from the seat)");
                // (Halo's W-passenger rifle idle + aim-still shoulders the rifle: the gun hand is near head height by the game's own clip, so no
                // height rule here) - the outside arms must be the seat clip's, not the first-person ones reaching to the view model at the eye
                Vector3 outHand = rh != null ? rh.position : Vector3.zero;
                float viewToOut = g != null ? (g.position - drawn).magnitude : -1f;
                HaloLabRunner.Log("  outside: the view model would be " + viewToOut.ToString("0.00") + " m from the gun my avatar holds");
                r.Check(me != null && (outHand - (hog.transform.TransformPoint(me.GunLocalPos))).magnitude < 0.25f, "seen from outside my arms are the seat clip's (right hand " + (outHand - hog.transform.TransformPoint(me.GunLocalPos)).magnitude.ToString("0.00") + " m from the clip's gun)");
                HaloVehicleSeat.Local.GetOut("lab");
                yield return r.Frames(90);
                HaloSeatPoses.LabAvatar = null;
            }

            HaloViewScale.LabHold = false;
            HaloDesk.LabCamera = null;
            r.Cam.tag = camTag; r.Cam.enabled = camOn;
        }

        /// <summary>
        /// Lab "weapon-anims": the weapons' own animations on their third-person models (HaloWeaponAnim), each picture a weapon at rest (top) and
        /// animated (bottom), side view: pistol fire (slide back), pistol empty (slide held back), shotgun chamber (pump back), sniper fire (bolt
        /// back), AR reload (charging handle), plasma rifle overheating (vents).
        /// </summary>
        private static IEnumerator WeaponAnims(HaloLabRunner r)
        {
            HaloMap map = OpenMap(r, HaloLabBoot.Arg("-halomap", "bloodgulch"));
            if (!r.Check(map != null, "map")) yield break;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.55f, 0.57f, 0.6f));
            var lgo = new GameObject("Lab sun");
            var sun = lgo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            lgo.transform.rotation = Quaternion.Euler(35f, 20f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);
            string[][] cases =
            {
                new[] { "pistol_fire", @"weapons\pistol\pistol", "fire", "0.033" },
                new[] { "pistol_empty", @"weapons\pistol\pistol", "state:empty", "" },
                new[] { "shotgun_pump", @"weapons\shotgun\shotgun", "chamber", "0.55" },
                new[] { "sniper_bolt", @"weapons\sniper rifle\sniper rifle", "fire", "0.02" },
                new[] { "ar_reload", @"weapons\assault rifle\assault rifle", "reload-full", "-1" },
                new[] { "plasma_overheat", @"weapons\plasma rifle\plasma rifle", "overheating", "0.9" },
            };
            int ok = 0;
            for (int i = 0; i < cases.Length; i++)
            {
                string[] k = cases[i];
                HaloTag w = map.Find("weap", k[1]);
                if (w == null) { r.Fail("no weapon " + k[1]); continue; }
                Vector3 at = new Vector3(-300f + i * 5f, 1500f, 300f);
                HaloModelBuilder.Built rest = HaloModelBuilder.Build(w.Root.Ref(40), null), moving = HaloModelBuilder.Build(w.Root.Ref(40), null);
                if (rest == null || moving == null) { r.Fail("build " + k[1]); continue; }
                Paint(rest.Root, mat);
                Paint(moving.Root, mat);
                float len = 0.6f;
                foreach (Renderer rr in rest.Root.GetComponentsInChildren<Renderer>()) len = Mathf.Max(len, rr.bounds.size.x);
                rest.Root.transform.position = at + Vector3.up * len * 0.28f;
                moving.Root.transform.position = at - Vector3.up * len * 0.28f;
                HaloWeaponAnim a = HaloWeaponAnim.Attach(moving, w);
                if (!r.Check(a != null, k[0] + ": animated parts on " + w.Name)) continue;
                a.enabled = false;      // (stepped here)
                bool played;
                if (k[2].StartsWith("state:")) { a.SetState(k[2].Substring(6), true); played = a.State != null; a.Step(0f); }
                else
                {
                    played = a.Play(k[2]);
                    float t = float.Parse(k[3], System.Globalization.CultureInfo.InvariantCulture);
                    if (t < 0f)
                    {
                        // the moment the parts are furthest out
                        HaloWeaponAnims d = HaloWeaponAnim.DataOf(w);
                        HaloWeaponAnims.Clip c = d.Find(k[2]);
                        t = 0f;
                        float best = -1f;
                        int n = d.NodeNames.Length;
                        var rot = new float[n * 4]; var tr = new float[n * 3]; var mv = new bool[n];
                        for (int f = 0; f < c.Anim.Frames; f++)
                        {
                            d.Sample(c, f * HaloAnimation.FrameTime, rot, tr, mv);
                            for (int j = 0; j < n; j++) { if (!mv[j]) continue; float dd = Mathf.Abs(tr[j * 3] - d.RestTrans[j * 3]) + Mathf.Abs(tr[j * 3 + 1] - d.RestTrans[j * 3 + 1]) + Mathf.Abs(tr[j * 3 + 2] - d.RestTrans[j * 3 + 2]); if (dd > best) { best = dd; t = f * HaloAnimation.FrameTime; } }
                        }
                    }

                    a.Step(t);
                }

                string moved = "";
                foreach (string nm in HaloWeaponAnim.DataOf(w).NodeNames)
                {
                    Transform p = a.Node(nm); Vector3 lp; Quaternion lr;
                    if (p == null || !a.Rest(nm, out lp, out lr)) continue;
                    float dp = (p.localPosition - lp).magnitude, da = Quaternion.Angle(p.localRotation, lr);
                    if (dp > 0.001f || da > 0.5f) moved += " " + nm + " " + (dp * 100f).ToString("0.0") + " cm / " + da.ToString("0") + " deg;";
                }

                if (played && moved.Length > 0) ok++;
                HaloLabRunner.Log("weapon-anims " + k[0] + ": " + (a.Playing ?? a.State ?? "-") + " moved:" + (moved.Length > 0 ? moved : " NOTHING"));
                r.Aim(at + new Vector3(len * 0.15f, 0.02f, -len * 1.25f), at + new Vector3(len * 0.15f, 0f, 0f), 40f);
                r.Shot("weapon_" + k[0], 640, 480);
                yield return null;
            }

            r.Check(ok == cases.Length, "weapon-anims: " + ok + "/" + cases.Length + " weapons moved their parts");
        }

        /// <summary>
        /// Lab "cine-pose": Captain Keyes (a10's captain biped model) posed by a COMPRESSED cinematic animation of the a10 cutscenes
        /// (the captain's x10 cinematic graph, decoded by HaloAnimation's compressed path): rest pose, then three moments. Picture cine_keyes.png.
        /// </summary>
        private static IEnumerator CinePose(HaloLabRunner r)
        {
            HaloMap map = null;
            foreach (HaloInstall.MapEntry e in r.Game.Install.Campaign) if (e.Name == "a10") map = new HaloMap(e.File);
            if (!r.Check(map != null, "a10")) yield break;
            HaloTag bipd = map.Find("bipd", @"characters\captain\captain");
            HaloTag antr = map.Find("antr", @"cinematics\animations\captain\x10\x10");
            if (!r.Check(bipd != null && antr != null, "captain biped + x10 graph")) yield break;
            HaloTag model = bipd.Root.Ref(40);
            var skel = new HaloSkeleton(new HaloModel(model));
            var graph = new HaloAnimGraph(antr);
            int[] nm = skel.MapFrom(graph);
            string animName = HaloLabBoot.Arg("-haloanim", "well_thats_it_then");
            HaloAnimation an = graph.Get(animName);
            if (!r.Check(an != null && an.Compressed && an.Decode(), "compressed '" + animName + "' decodes (" + (an != null ? an.Frames + " frames, " + an.Error : "missing") + ")")) yield break;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.62f, 0.6f, 0.56f));
            var lgo = new GameObject("Lab sun");
            var sun = lgo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            lgo.transform.rotation = Quaternion.Euler(30f, 150f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            Vector3 at = new Vector3(600f, 1500f, -600f);
            float[] times = { -1f, 0.25f, 0.5f, 0.75f };
            var pose = new HaloPose(skel);
            for (int c = 0; c < times.Length; c++)
            {
                HaloModelBuilder.Built b = HaloModelBuilder.Build(model, null);
                if (!r.Check(b != null, "captain model built")) yield break;
                Paint(b.Root, mat);
                foreach (SkinnedMeshRenderer s in b.Root.GetComponentsInChildren<SkinnedMeshRenderer>()) s.updateWhenOffscreen = true;
                b.Root.transform.position = at + new Vector3(c * 1.3f, 0f, 0f);
                b.Root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);      // (Halo forward = Unity +x: face the camera at -z)
                pose.Reset();
                if (times[c] >= 0f) pose.Apply(an, nm, times[c] * an.Duration, false, 1f);
                for (int i = 0; i < b.Nodes.Length && i < skel.Count; i++)
                {
                    if (b.Nodes[i] == null) continue;
                    b.Nodes[i].localPosition = HaloSpace.Pos(pose.LTrans[i * 3], pose.LTrans[i * 3 + 1], pose.LTrans[i * 3 + 2]);
                    b.Nodes[i].localRotation = HaloSpace.Rot(pose.LRot[i * 4], pose.LRot[i * 4 + 1], pose.LRot[i * 4 + 2], pose.LRot[i * 4 + 3]);
                }
            }

            HaloLabRunner.Log("cine-pose: Keyes, '" + animName + "' ("+ an.Frames + " frames, compressed) at rest, 25 %, 50 %, 75 %");
            r.Aim(at + new Vector3(1.95f, 1.1f, -6.5f), at + new Vector3(1.95f, 0.95f, 0f), 30f);
            r.Shot("cine_keyes", 1280, 640);
            yield return null;
        }

        private static void PoseNow(HaloLabRunner r)
        {
            HaloDeskBody b = r.Game != null ? r.Game.GetComponent<HaloDeskBody>() : null;
            if (b != null) b.LabPoseNow();
        }

        private static void Debug(HaloLabRunner r)
        {
            Transform g = HaloDesk.HeldWeapon != null ? HaloDesk.HeldWeapon : HaloCombat.HeldModel;
            if (g == null) return;
            Vector3 vp; Quaternion vr; float vs;
            HaloDesk.FirstPersonPose(r.Cam, HaloLocal.HeldWeapon.Tag, HaloDesk.AimPoint, g.lossyScale.x / Mathf.Max(1e-4f, g.localScale.x), out vp, out vr, out vs);
            HaloCyborg.WeaponGrip gr = HaloCyborg.Current.Grip(HaloLocal.HeldWeapon.Tag);
            string b = "";
            foreach (Renderer rr in g.GetComponentsInChildren<Renderer>()) b += " [" + rr.name + " " + r.Cam.WorldToViewportPoint(rr.bounds.min).ToString("0.00") + ".." + r.Cam.WorldToViewportPoint(rr.bounds.max).ToString("0.00") + "]";
            HaloLabRunner.Log("desk-monitor: gun at " + r.Cam.transform.InverseTransformPoint(g.position).ToString("0.000") + " (wanted " + r.Cam.transform.InverseTransformPoint(vp).ToString("0.000") + "), lossy " + g.lossyScale.x.ToString("0.000") + " local " + g.localScale.x.ToString("0.000") + " view x" + vs.ToString("0.00") +
                ", box " + (gr != null && gr.HasBox ? gr.BoxMin.ToString("0.000") + ".." + gr.BoxMax.ToString("0.000") : "none") + ", cam fov " + r.Cam.fieldOfView + " aspect " + r.Cam.aspect.ToString("0.00") + ", aim " + HaloDesk.AimPoint.ToString("0") + b);
        }

        private static void Paint(GameObject go, Material m)
        {
            foreach (Renderer rr in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[rr.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                rr.sharedMaterials = mats;
            }
        }

        private static IEnumerator Run(HaloLabRunner r)
        {
            HaloMap map = OpenMap(r, HaloLabBoot.Arg("-halomap", "bloodgulch"));
            string why = "no map";
            HaloCyborg c = map != null ? HaloCyborg.Load(map, out why) : null;
            if (!r.Check(c != null, "cyborg of the map (" + why + ")")) yield break;
            HaloCyborg.Use(c);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StandIn);
            if (!r.Check(prefab != null, "stand-in avatar " + StandIn)) yield break;
            HaloTag ar = map.Find("weap", @"weapons\assault rifle\assault rifle"), pistol = map.Find("weap", @"weapons\pistol\pistol");
            HaloTag hog = map.Find("vehi", @"vehicles\warthog\mp_warthog");
            r.Check(ar != null && pistol != null && hog != null, "assault rifle, pistol, warthog tags");

            var cases = new List<Case>
            {
                new Case { Name = "ar_idle", Weapon = "ar" },
                new Case { Name = "ar_aim_up", Weapon = "ar", Pitch = -30f },
                new Case { Name = "ar_reload", Weapon = "ar", Reload = true },
                new Case { Name = "ar_fire", Weapon = "ar", Fire = true },
                new Case { Name = "ar_walk", Weapon = "ar", Walk = true },
                new Case { Name = "pistol_idle", Weapon = "pistol" },
                new Case { Name = "pistol_reload", Weapon = "pistol", Reload = true },
                new Case { Name = "ar_first", Weapon = "ar", First = true, Fov = 70f },
                new Case { Name = "ar_first40", Weapon = "ar", First = true, Fov = 40f },
                new Case { Name = "pistol_first", Weapon = "pistol", First = true, Fov = 70f },
                new Case { Name = "death_front", Weapon = "ar", Death = true },
                new Case { Name = "hog_driver", Seat = "W-driver" },
                new Case { Name = "hog_gunner", Seat = "W-gunner" },
                new Case { Name = "hog_passenger", Seat = "W-passenger", Weapon = "ar" },
            };

            // (plain colours: the map's own materials are the World module's and drew magenta in the lab)
            var gunMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            gunMat.SetColor("_BaseColor", new Color(0.15f, 0.22f, 0.15f));
            var hogMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            hogMat.SetColor("_BaseColor", new Color(0.35f, 0.38f, 0.3f));
            var grey = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            grey.SetColor("_BaseColor", new Color(0.62f, 0.58f, 0.52f));
            Vector3 origin = new Vector3(400f, 1500f, 400f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.position = origin;
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            floor.GetComponent<Renderer>().sharedMaterial = grey;
            var lgo = new GameObject("Lab sun");
            var sun = lgo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            lgo.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

            const float dt = 1f / 60f;
            int pass = 0;
            for (int ci = 0; ci < cases.Count; ci++)
            {
                Case k = cases[ci];
                Vector3 at = origin + new Vector3(ci * 6f, 0f, 0f);
                GameObject av = Object.Instantiate(prefab, at, Quaternion.identity);
                av.name = "Desk stand-in " + k.Name;
                foreach (Renderer rr in av.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[rr.sharedMaterials.Length];
                    for (int m = 0; m < mats.Length; m++) mats[m] = grey;
                    rr.sharedMaterials = mats;
                    var smr = rr as SkinnedMeshRenderer;
                    if (smr != null) smr.updateWhenOffscreen = true;
                }

                Animator anim = av.GetComponentInChildren<Animator>();
                if (!r.Check(anim != null && anim.isHuman, "humanoid stand-in for " + k.Name)) yield break;
                anim.enabled = false;
                HaloTag weapon = k.Weapon == "ar" ? ar : k.Weapon == "pistol" ? pistol : null;
                Transform gun = null;
                if (weapon != null)
                {
                    HaloModelBuilder.Built b = HaloModelBuilder.Build(weapon.Root.Ref(40), null);
                    if (b != null) { gun = b.Root.transform; Paint(b.Root, gunMat); }
                }

                Transform seat = null, vehicle = null;
                if (k.Seat != null && hog != null)
                {
                    HaloModelBuilder.Built vb = HaloModelBuilder.Build(hog.Root.Ref(40), null);
                    if (vb != null)
                    {
                        vehicle = vb.Root.transform;
                        Paint(vb.Root, hogMat);
                        vehicle.position = at + new Vector3(0f, 0.65f, 0f);
                        vehicle.rotation = Quaternion.identity;      // (Halo forward = Unity +x)
                        string marker = k.Seat == "W-driver" ? "driver" : k.Seat == "W-gunner" ? "gunner" : "passenger";
                        seat = HaloDeskPlayer.FindMarker(vehicle, marker);
                        av.transform.SetPositionAndRotation(seat != null ? seat.position - Vector3.up * 0.4f : at, Quaternion.LookRotation(Vector3.right));
                    }

                    r.Check(seat != null, k.Name + ": the warthog's seat marker");
                }

                var dp = new HaloDeskPlayer("lab " + k.Name);
                var input = new HaloPuppetInput { Weapon = weapon, Seat = k.Seat };
                Quaternion facing = av.transform.rotation;
                Quaternion aim = facing * Quaternion.Euler(k.Pitch, 0f, 0f);
                Transform headT = anim.GetBoneTransform(HumanBodyBones.Head);
                Vector3 aimPoint = headT.position + aim * Vector3.forward * 30f;
                HaloDeskPlayer.LabSpeed = k.Walk ? 2.6f : 0f;
                Vector3 gp = Vector3.zero;
                Quaternion gq = Quaternion.identity;
                bool posed = false;
                float t = 0f, nextShot = 0f;
                float fpScale = 1f;
                for (int step = 0; step < 400; step++)
                {
                    t += dt;
                    if (k.Fire && t > 0.4f && t >= nextShot) { input.Fire++; nextShot = t + 0.1f; }
                    if (k.Reload && t > 0.3f && input.Reload == 0) input.Reload = 1;
                    if (k.First)
                    {
                        Vector3 eye0 = headT.position + facing * new Vector3(0f, 0.07f, 0.1f);
                        r.Aim(eye0, eye0 + aim * Vector3.forward * 10f, k.Fov);
                        r.Cam.aspect = 16f / 9f;
                        Vector3 vp;
                        Quaternion vr;
                        HaloDesk.FirstPersonPose(r.Cam, weapon, aimPoint, 1f, out vp, out vr, out fpScale);
                        dp.Poser.ViewSet = true; dp.Poser.ViewGun = vp; dp.Poser.ViewRot = vr;
                    }

                    if (k.Death) posed = t < 0.5f ? dp.Pose(anim, ref input, aim, aimPoint, true, 1f, dt, out gp, out gq) : dp.PoseDeath(anim, false, 0, 0, dt);
                    else if (k.Seat != null) posed = dp.PoseSeated(anim, ref input, seat, vehicle, true, true, dt, out gp, out gq);
                    else posed = dp.Pose(anim, ref input, aim, aimPoint, !k.First, fpScale, dt, out gp, out gq);
                    HaloPuppet p = dp.Puppet;
                    if (p == null) continue;
                    bool done;
                    if (k.Reload) done = input.Reload > 0 && p.Playing == HaloPuppet.Shot.Reload && p.ShotTime >= 0.5f * p.ShotLength;
                    else if (k.Fire) done = t > 0.75f && nextShot - t > 0.05f;
                    else if (k.Walk) done = t >= 1.3f;
                    else if (k.Death) done = t >= 3.0f;
                    else done = t >= 1.0f;
                    if (done) break;
                }

                HaloDeskPlayer.LabSpeed = -1f;
                if (gun != null && posed && !k.Death) gun.SetPositionAndRotation(gp, gq);
                if (gun != null && k.First) gun.localScale = Vector3.one * fpScale;
                if (gun != null && k.Death) gun.gameObject.SetActive(false);
                HaloPuppet pu = dp.Puppet;
                HaloPuppetPoser po = dp.Poser;
                float gunAim = gun != null ? Vector3.Angle(gun.right, aimPoint - gun.position) : -1f;      // (the model's barrel is its +x)
                bool aimed = k.Seat == null && !k.Reload && !k.Death;
                bool ok = posed && pu != null && pu.Sample.Valid && (k.Death || k.Seat != null || po.LastRightErr < 0.04f) && (!aimed || (po.LastLeftErr < 0.06f && gunAim < 3f)) && (k.Seat == null || k.Death || po.LastRightErr < 0.06f);
                if (ok) pass++;
                HaloLabRunner.Log("desk " + k.Name + ": " + (pu == null ? "NO PUPPET" : "'" + pu.ClassName + "' base '" + pu.BaseName + "'" + (pu.ShotName != null ? " + '" + pu.ShotName + "' at " + pu.ShotTime.ToString("0.00") + "/" + pu.ShotLength.ToString("0.00") + " s" : "")) +
                    "; right wrist " + (po.LastRightErr * 100f).ToString("0.0") + " cm off, left " + (po.LastLeftErr * 100f).ToString("0.0") + " cm (on the gun " + po.LeftOnGun.ToString("0.00") + "), barrel " + gunAim.ToString("0.0") +
                    " deg from the aim (fix " + po.AimFix.ToString("0.0") + "), avatar arm " + po.ArmLength.ToString("0.00") + " m vs cyborg " + (pu != null ? pu.ArmLength.ToString("0.00") : "-") + " m" + (ok ? " ok" : " CHECK"));

                // pictures: 3/4 front (and from the eye in first person)
                Vector3 look = av.transform.position + Vector3.up * 1.15f;
                if (k.First)
                {
                    HaloHeadHide.LabHold = true;
                    HaloHeadHide.Hide(headT);
                    Vector3 eye = headT.position + facing * new Vector3(0f, 0.07f, 0.1f);
                    r.Aim(eye, eye + aim * Vector3.forward * 10f, k.Fov);
                    r.Cam.aspect = 16f / 9f;
                    r.Shot("desk_" + k.Name, 1280, 720);
                    r.Cam.ResetAspect();
                    HaloLabRunner.Log("desk " + k.Name + ": first-person weapon drawn at x" + fpScale.ToString("0.00") + " (fov " + k.Fov + ")");
                    HaloHeadHide.LabHold = false;
                    HaloHeadHide.Restore();
                }
                else if (k.Seat != null || k.Death)
                {
                    Vector3 c0 = seat != null ? seat.position : av.transform.position + Vector3.up * 0.5f;
                    if (k.Death) r.Aim(av.transform.position + new Vector3(2.5f, 2.2f, 1.5f), av.transform.position + facing * new Vector3(0f, 0.2f, 0.9f), 40f);
                    else r.Aim(c0 + new Vector3(0.5f, 0.9f, -2.8f), c0 + Vector3.up * 0.25f, 40f);
                    r.Shot("desk_" + k.Name, 640, 480);
                }
                else
                {
                    r.Aim(av.transform.position + facing * new Vector3(1.3f, 1.5f, 2.4f), look, 40f);
                    r.Shot("desk_" + k.Name, 480, 540);
                    r.Aim(av.transform.position + facing * new Vector3(2.6f, 1.3f, 0.2f), look, 40f);
                    r.Shot("desk_" + k.Name + "_side", 480, 540);
                }

                yield return null;
            }

            HaloLabRunner.Log("desk: " + pass + "/" + cases.Count + " cases posed within reach (aimed cases on the aim)");
            r.Check(pass >= cases.Count - 2, "desk cases posed");

            // the seat pose API for the Vehicles module
            HaloSeatPose sp;
            bool sok = HaloDesk.SeatPoseFor("W-driver", null, out sp);
            r.Check(sok, "HaloDesk.SeatPoseFor W-driver (" + (sok ? sp.Animation + ", pelvis " + sp.Pelvis.ToString("0.00") + ", right wrist " + sp.RWrist.ToString("0.00") : "-") + ")");
            yield return null;
        }
    }
}

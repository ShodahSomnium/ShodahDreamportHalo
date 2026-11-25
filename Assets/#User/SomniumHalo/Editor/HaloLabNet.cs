using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// "net": the multiplayer layer in a real match (the real install, graphics) with two scripted players over an in-memory loopback of the slot objects
    /// (Bo2LabNet ported): A = the local player (the real HaloGame with its net layer), B = a scripted HaloNet session standing 8 m in front of A, C = a third
    /// session that arrives while the match runs (late join). It checks: the claims, the instance master, the lobby rules and the countdown, everybody pulled
    /// into the master's game with the same start, B drawn (weapon, hit boxes), B's shots / grenade / melee replayed, hits both ways (A measures on B's hit
    /// boxes, B's hits applied to A), B's death in the kill feed, pickups / drops / objective events, the world blocks (late join), leaving. "LAB:" lines and
    /// pictures net_*.png in DevTools/lab/net/.
    /// </summary>
    [InitializeOnLoad]
    static class HaloLabNet
    {
        static HaloLoopbackWorld _world;

        static HaloLabNet()
        {
            HaloLabRunner.Register("net", r => Run(r));
            HaloLabRunner.Register("net-coop", r => Coop(r));
            HaloLabRunner.Register("net-coop-vehicles", r => CoopVehicles(r));
            string s = HaloLabBoot.Arg("-halolab", null);
            if (s != null && s.StartsWith("net"))
            {
                // before the Dreamport starts (this runs again after the play-mode domain reload): the loopback replaces the Fusion session, A is player 1
                _world = new HaloLoopbackWorld();
                HaloNetGame.Clock = () => Time.unscaledTimeAsDouble;
                HaloNetGame.TransportFactory = () => _world.Join(1, () => Time.unscaledTimeAsDouble);
                HaloNetGame.LabNames = id => id == 2 ? "Bravo (B)" : id == 3 ? "Charlie (C)" : null;
            }
        }

        static double Clock() { return Time.unscaledTimeAsDouble; }

        static IEnumerator Seconds(float s)
        {
            float t = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < t) yield return null;
        }

        /// <summary>A scripted player: its own HaloNet session over the loopback, ticked by the lab once a frame.</summary>
        sealed class Scripted
        {
            public HaloNet Net;
            public HaloLoopbackTransport T;
            public readonly List<HaloNetEvent> Seen = new List<HaloNetEvent>();
            public Vector3 Feet;
            public float Yaw;
            public bool Playing;

            public int Count(HaloNetEventKind k) { int n = 0; foreach (var e in Seen) if (e.Kind == k) n++; return n; }
            public float Sum(HaloNetEventKind k) { float n = 0; foreach (var e in Seen) if (e.Kind == k) n += e.Amount; return n; }

            public void Tick()
            {
                ref HaloNetState o = ref Net.Own;
                o.Root = HaloNetU.V(Feet);
                o.RootYaw = Yaw;
                o.Head = HaloNetU.V(Feet + Vector3.up * 1.65f);
                o.HeadRot = HaloNetU.Q(Quaternion.Euler(0f, Yaw, 0f));
                o.Vr = true;
                Quaternion face = Quaternion.Euler(0f, Yaw, 0f);
                o.RHand = HaloNetU.V(Feet + face * new Vector3(0.18f, 1.25f, 0.35f));
                o.RRot = HaloNetU.Q(face);
                o.LHand = HaloNetU.V(Feet + face * new Vector3(0.02f, 1.3f, 0.75f));
                o.LRot = HaloNetU.Q(face);
                Net.Tick();
                Seen.AddRange(Net.Events);
            }

            public void Play(int zone, int seq, int start, int mapPrint)
            {
                Playing = true;
                ref HaloNetState o = ref Net.Own;
                o.Status = HaloNetStatus.Playing;
                o.MatchState = HaloMatchState.Running;
                o.ZoneHash = zone;
                o.MatchSeq = seq;
                o.MatchStart = start;
                o.MapPrint = mapPrint;
                o.Alive = true;
                o.Health = 1f;
                o.Shield = 1f;
            }
        }

        static readonly List<Scripted> Scripts = new List<Scripted>();

        static Scripted Join(int id, string label)
        {
            var s = new Scripted();
            s.T = _world.Join(id, Clock);
            s.Net = new HaloNet(s.T, Clock, label);
            Scripts.Add(s);
            return s;
        }

        /// <summary>Runs the scripted sessions once a frame alongside the game.</summary>
        sealed class Ticker : MonoBehaviour
        {
            private void LateUpdate() { for (int i = 0; i < Scripts.Count; i++) Scripts[i].Tick(); }
        }

        static void EnsurePlayer()
        {
            if (Object.FindFirstObjectByType<CharacterController>() != null) return;
            var go = new GameObject("Lab Player");
            go.transform.position = HaloSpace.RoomOrigin;
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.3f; cc.center = new Vector3(0f, 0.9f, 0f);
            go.layer = HaloLayers.Avatar;
        }

        static HaloTag Weapon(string name)
        {
            HaloMap m = HaloSession.Map;
            if (m == null) return null;
            foreach (HaloTag t in m.OfClass("weap")) if (t.Name.EndsWith(name)) return t;
            return null;
        }

        static IEnumerator Run(HaloLabRunner r)
        {
            if (_world == null) { r.Fail("the loopback was not installed (run the scenario as 'net')"); yield break; }
            EnsurePlayer();
            new GameObject("Lab net ticker").AddComponent<Ticker>();
            Transform root = new GameObject("Lab A root").transform, head = new GameObject("Lab A head").transform;
            HaloNetGame.LabRoot = root;
            HaloNetGame.LabHead = head;
            root.position = HaloSpace.RoomOrigin;
            head.position = HaloSpace.RoomOrigin + Vector3.up * 1.65f;

            // ---- 0. alone, then B in the hub
            yield return r.Wait(() => HaloNetGame.Claimed, 30f, "A's claim");
            r.Check(HaloNetGame.Claimed && HaloLocal.NetId >= 0, "A claimed slot " + HaloLocal.NetId + " over the loopback");
            r.Check(HaloNetGame.IsMaster && HaloNetGame.MasterKnown, "A alone is the instance master");
            bool known;
            r.Check(!HaloNetGame.TryHasRecord(2, out known) && known, "the isolation's rule is decided by the records now (nobody else has one)");
            Scripted B = Join(2, "[B]");
            yield return r.Wait(() => B.Net.Claimed && HaloNetGame.Players.Count == 1, 20f, "B in the hub");
            HaloNetPlayer pb = HaloNetGame.Players.Count > 0 ? HaloNetGame.Players[0] : null;
            r.Check(pb != null && pb.Name == "Bravo (B)" && !pb.InMatch && pb.PlayerId == 2, "A lists B in the hub (" + (pb != null ? pb.ToString() : "none") + ")");
            r.Check(HaloGear.HasRecord(2), "HaloGear.HasRecord(B) is true");
            r.Check(B.Net.MasterId == 1 && !B.Net.IsInstanceMaster, "B agrees A is the instance master");
            int sides = (HaloNetGame.MyTeam & 1) + (B.Net.Own.Team & 1);
            r.Check(sides == 1, "the lobby put A and B on different sides (A " + HaloNetGame.MyTeam + ", B " + B.Net.Own.Team + ")");

            // ---- 1. the master's START: rules, countdown, everybody in the match
            // the Front module's lobby is the master's UI: set its game, press START GAME (F publishes through Net's IHaloLobbyNet link)
            yield return r.Wait(() => HaloLobby.Mine != null && HaloLobby.IsMaster && HaloLobby.CanEdit, 30f, "the lobby as master");
            r.Check(HaloLobbyNet.Net != null && HaloLobbyNet.Net.Online && HaloLobbyNet.Net.IsMaster, "the lobby's net link is Net's, online, master");
            HaloRules rules = HaloLobby.Mine;
            rules.Type = HaloScenario.GameType.Slayer;
            rules.Teams = true;
            rules.MapName = HaloLabBoot.Arg("-halomap", "bloodgulch");
            rules.ScoreLimit = 50;
            HaloLobby.Changed();
            yield return r.Frames(10);
            var lobbyList = new List<HaloLobbyPlayer>();
            HaloLobbyNet.Net.Players(lobbyList);
            r.Check(lobbyList.Count == 2 && lobbyList[0].Me && lobbyList[0].Master && lobbyList[1].Name == "Bravo (B)", "the lobby lists me (master) and B");
            string why = HaloLobby.PressStart();
            r.Check(why == null, "A presses START GAME (" + (why ?? "ok") + ")");
            int countSeen = 0;
            float until = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < until && !B.Net.Game.On)
            {
                if (B.Net.Lobby.Phase == 2) countSeen = Mathf.Max(countSeen, B.Net.Lobby.Count);
                yield return null;
            }

            r.Check(B.Net.Game.On, "B reads the master's game command when the countdown ends");
            r.Check(countSeen > 0 && B.Net.Lobby.RulesOk, "B saw the master's countdown (from " + countSeen + ") and the rules");
            HaloRules seenByB = HaloNetRules.Unpack(B.Net.Lobby.RulesA, B.Net.Lobby.RulesB, B.Net.Lobby.RulesC, HaloNetRules.MapOf(B.Net.Lobby.ZoneHash));
            r.Check(seenByB.Type == rules.Type && seenByB.Teams && seenByB.ScoreLimit == 50 && seenByB.MapName == rules.MapName, "B decodes the master's rules: " + seenByB);
            yield return r.Wait(() => HaloSession.Playing, 240f, "A playing");
            if (!HaloSession.Playing) { r.Fail("A never reached Playing"); yield break; }
            yield return r.Wait(() => B.Net.Game.On && B.Net.Game.Start != 0, 10f, "the master's start in its record");
            HaloNet.GameInfo g = B.Net.Game;
            r.Check(g.On && g.Seq == HaloNetGame.GameNumber && Mathf.Abs((float)(g.Start - HaloSession.StartUtc)) < 1f, "B reads game " + g.Seq + " with A's start " + g.Start);
            // A's body for the record: where the match put the player
            Vector3 feetA;
            if (!HaloPlayer.TryFeet(out feetA)) feetA = HaloLocal.Position;
            Vector3 fwd = HaloLocal.Forward.sqrMagnitude > 0.5f ? HaloLocal.Forward : Vector3.forward;
            fwd.y = 0f; fwd.Normalize();
            root.SetPositionAndRotation(feetA, Quaternion.LookRotation(fwd));
            head.SetPositionAndRotation(feetA + Vector3.up * 1.65f, Quaternion.LookRotation(fwd));
            // B: pulled into the same game, standing 8 m in front of A and facing A
            Vector3 bFeet = HaloSpawnsGround(feetA + fwd * 8f);
            B.Feet = bFeet;
            B.Yaw = Quaternion.LookRotation(-fwd).eulerAngles.y;
            HaloTag ar = Weapon("assault rifle"), pistol = Weapon("pistol");
            B.Play(g.ZoneHash, g.Seq, g.Start, HaloNetU.MapPrint());
            B.Net.Own.Held = HaloNetU.TagIndex(ar);
            B.Net.Own.Stowed = HaloNetU.TagIndex(pistol);
            B.Net.Own.InHand = true;
            B.Net.Own.Team = 1 - (HaloLocal.Team & 1);
            yield return r.Wait(() => HaloNetGame.Networked && HaloNetGame.ViewOf(B.Net.MySlot) != null, 20f, "B in A's match");
            yield return Seconds(1.5f);
            HaloRemoteView vb = HaloNetGame.ViewOf(B.Net.MySlot);
            r.Check(vb != null && pb.InMatch && HaloRoster.Find(B.Net.MySlot) != null, "B is in A's match, drawn, and in the Match module's roster");
            r.Check(B.Net.Remotes.Count == 1, "B sees A in the match");
            r.Check(vb != null && vb.HeldModel != null && vb.HeldModel.gameObject.activeSelf, "B's assault rifle is drawn in the hand");
            r.Check(vb != null && vb.StowedModel != null && vb.StowedModel.gameObject.activeSelf, "B's pistol is drawn on the hip");
            r.Check(vb != null && vb.HitBoxesOn && (vb.Box(HaloRemoteView.HeadBox).position - (bFeet + Vector3.up * 1.65f)).magnitude < 0.3f, "B's head hit box is at B's head");
            r.Check((pb.Position - bFeet).magnitude < 0.2f, "the players list has B's drawn position " + pb.Position.ToString("0.00"));
            Vector3 eye = feetA + Vector3.up * 1.7f - fwd * 1.5f + Vector3.Cross(Vector3.up, fwd) * 1.2f;
            r.Aim(eye, bFeet + Vector3.up * 1.2f, 50f);
            Visualise(vb, true);
            r.Shot("net_b_drawn");
            r.Aim(bFeet + Vector3.up * 1.4f + Quaternion.Euler(0f, B.Yaw, 0f) * new Vector3(1.6f, 0.2f, 1.4f), bFeet + Vector3.up * 1.2f, 45f);
            r.Shot("net_b_close");
            Visualise(vb, false);

            // ---- 1b. (Vehicles) B rides a warthog's passenger seat (Halo: "allows weapons") with the AR in hand, the pistol stowed. B's record puts
            // its head and hands 8 m behind the vehicle (a monitor rider's head is the chase camera): the weapons must still be drawn on B's
            // seated avatar - the AR in the seat animation's hands, the pistol on the body - inside the vehicle
            {
                HaloVehicle hog = null;
                foreach (HaloVehicle v in HaloVehicles.All) if (v != null && v.Id < 255 && v.Def != null && v.Def.Tag != null && v.Def.Tag.Name.EndsWith("mp_warthog") && v.SeatOf(1) != null) { hog = v; break; }
                r.Check(hog != null, "a multiplayer warthog in the match for B to ride (" + HaloVehicles.All.Count + " vehicles)");
                if (hog != null)
                {
                    var pseat = hog.SeatOf(1);
                    Animator standB = HaloLabVehicles.LabStandIn(pseat.Marker.position, new Color(0.2f, 0.5f, 0.9f));
                    HaloSeatPoses.LabAvatar = k => k == pb.PlayerId ? standB : null;
                    HaloVehicleNet.LabRiders = new List<HaloVehicleRiders.Rider> { new HaloVehicleRiders.Rider { PlayerId = pb.PlayerId, Vehicle = hog, Seat = 1 } };
                    Vector3 behind = hog.transform.position - hog.transform.forward * 8f;
                    B.Feet = behind + Vector3.up * 1.5f;
                    B.Net.Own.Vehicle = hog.Id; B.Net.Own.Seat = 1;
                    B.Net.Own.Held = HaloNetU.TagIndex(ar); B.Net.Own.Stowed = HaloNetU.TagIndex(pistol); B.Net.Own.InHand = true;
                    yield return Seconds(2.5f);
                    Transform held = vb.HeldModel, stowed = vb.StowedModel;
                    Transform rhB = standB.GetBoneTransform(HumanBodyBones.RightHand), hipsB = standB.GetBoneTransform(HumanBodyBones.Hips);
                    float toHand = held != null && held.gameObject.activeInHierarchy ? (held.position - rhB.position).magnitude : -1f;
                    float toSeat = held != null ? (held.position - pseat.Marker.position).magnitude : -1f;
                    float stowToHips = stowed != null && stowed.gameObject.activeInHierarchy ? (stowed.position - hipsB.position).magnitude : -1f;
                    float recordHead = (pb.Head - pseat.Marker.position).magnitude;
                    HaloLabRunner.Log("  B seated (" + HaloSeatPoses.Describe(pb.PlayerId) + "): record head " + recordHead.ToString("0.0") + " m from the seat; AR " + toHand.ToString("0.00") + " m from B's posed right hand, " +
                                      toSeat.ToString("0.00") + " m from the seat marker; pistol " + stowToHips.ToString("0.00") + " m from B's hips");
                    r.Check(recordHead > 5f, "B's record head is far from the seat (a monitor rider's chase camera: " + recordHead.ToString("0.0") + " m)");
                    r.Check(HaloSeatPoses.Owns(pb.PlayerId) && toHand >= 0f && toHand < 0.2f && toSeat < 1.0f, "B's AR is drawn in B's seated hands inside the warthog (" + toHand.ToString("0.00") + " m from the hand, " + toSeat.ToString("0.00") + " m from the seat)");
                    r.Check(stowToHips >= 0f && stowToHips < 0.8f, "B's pistol is drawn on B's seated body (" + stowToHips.ToString("0.00") + " m from the hips)");
                    Vector3 side = hog.transform.right * (hog.transform.InverseTransformPoint(pseat.Marker.position).x >= 0f ? 1f : -1f);
                    r.Aim(pseat.Marker.position + side * 2.6f + hog.transform.forward * 1.2f + Vector3.up * 1.0f, pseat.Marker.position + Vector3.up * 0.4f, 50f);
                    r.Shot("net_b_seated_gun");
                    r.Aim(hog.transform.position + side * 6f - hog.transform.forward * 6f + Vector3.up * 3f, hog.transform.position + Vector3.up * 0.8f, 50f);
                    r.Shot("net_b_seated_wide");
                    // back on foot where B stood
                    B.Net.Own.Vehicle = -1; B.Net.Own.Seat = -1; B.Feet = bFeet;
                    HaloVehicleNet.LabRiders = null;
                    yield return Seconds(1.5f);
                    HaloSeatPoses.LabAvatar = null;
                    Object.Destroy(standB.gameObject);
                    yield return Seconds(0.5f);
                }
            }

            // ---- 2. B fires at A, throws a grenade, swings
            int shots0 = HaloNetGame.StatShots, fired = 0;
            System.Action<HaloTag, Vector3, Vector3, int> onFired = (w, p, d, who) => { if (who == B.Net.MySlot) fired++; };
            HaloEvents.Fired += onFired;
            Vector3 muzzle = vb.HeldModel != null ? vb.HeldModel.position + vb.HeldModel.right * 0.5f : bFeet + Vector3.up * 1.3f;
            for (int i = 0; i < 5; i++)
            {
                B.Net.NoteShot(HaloNetU.TagIndex(ar), HaloNetU.V(muzzle), HaloNetU.V((feetA + Vector3.up * 1.3f - muzzle).normalized));
                yield return Seconds(0.12f);
            }

            yield return Seconds(0.6f);
            HaloEvents.Fired -= onFired;
            r.Check(HaloNetGame.StatShots - shots0 >= 4 && fired >= 3 && vb.ShotsPlayed >= 3, "A replays B's shots: " + (HaloNetGame.StatShots - shots0) + " shot(s) read, " + vb.ShotsPlayed + " played, " + fired + " Fired event(s)");
            HaloTag frag = null;
            if (HaloSession.Map != null) foreach (HaloTag t in HaloSession.Map.OfClass("proj")) if (t.Name.Contains("frag grenade")) frag = t;
            B.Net.NoteThrow(HaloNetU.TagIndex(frag), HaloNetU.V(bFeet + Vector3.up * 1.5f), HaloNetU.V(-fwd * -6f + Vector3.up * 4f));
            B.Net.NoteMelee();
            yield return Seconds(0.5f);
            HaloNetGrenade gren = Object.FindFirstObjectByType<HaloNetGrenade>();
            r.Check(vb.ThrowsPlayed == 1 && (gren != null || HaloCombat.Catalog != null), "B's frag grenade is replayed (" + (gren != null ? "Net's replay flying at " + gren.transform.position.ToString("0.0") : "by the Combat module's HaloCombat.ReplayThrow") + ")");
            r.Check(vb.Melees == 1, "B's melee swing is seen");
            if (gren != null)
            {
                r.Aim(eye, gren.transform.position, 50f);
                r.Shot("net_grenade");
            }

            // ---- 2b. the weapon's own parts (Desk's HaloWeaponAnim): B switches to the pistol, fires it, reloads it
            B.Net.Own.Held = HaloNetU.TagIndex(pistol);
            B.Net.Own.Stowed = HaloNetU.TagIndex(ar);
            yield return Seconds(1.0f);
            Transform pm = vb.HeldModel;
            Transform slide = null;
            if (pm != null) foreach (Transform t in pm.GetComponentsInChildren<Transform>(true)) if (t.name == "frame slide") slide = t;
            HaloWeaponAnim pa = pm != null ? pm.GetComponent<HaloWeaponAnim>() : null;
            r.Check(pm != null && pm.name.Contains("pistol") && slide != null && pa != null, "B's pistol is in the hand with its slide and a HaloWeaponAnim");
            if (slide != null)
            {
                Vector3 rest = slide.localPosition;
                B.Net.NoteShot(HaloNetU.TagIndex(pistol), HaloNetU.V(pm.position), HaloNetU.V(pm.right));
                float moved = 0f;
                float end = Time.realtimeSinceStartup + 0.8f;
                while (Time.realtimeSinceStartup < end) { moved = Mathf.Max(moved, (slide.localPosition - rest).magnitude); yield return null; }
                r.Check(moved > 0.005f, "the remote pistol's slide moves on a replayed shot (" + (moved * 1000f).ToString("0.0") + " mm)");
                r.Aim(pm.position + pm.forward * 0.35f + Vector3.up * 0.1f, pm.position, 40f);
                B.Net.Own.ReloadCounter = (B.Net.Own.ReloadCounter + 1) & 0xF;
                B.Net.Own.ReloadEmpty = true;
                string playing = null;
                end = Time.realtimeSinceStartup + 1.0f;
                while (Time.realtimeSinceStartup < end && playing == null) { if (pa.Playing != null) playing = pa.Playing; yield return null; }
                r.Check(playing != null, "the remote pistol plays its reload on the reload counter (" + (playing ?? "nothing") + ")");
                yield return Seconds(0.3f);
                r.Shot("net_pistol_reload");
            }

            // ---- 2c. the vent: B's plasma rifle overheated (the record's overheated bit) loops the weapon's overheated effect at its vents until it clears
            HaloTag prT = Weapon("plasma rifle");
            if (r.Check(prT != null, "vent: a plasma rifle in the map"))
            {
                B.Net.Own.Held = HaloNetU.TagIndex(prT);
                B.Net.Own.Overheated = false;
                yield return Seconds(1.0f);
                int shown0 = HaloRemoteView.VentsShown, st0 = HaloGunFx.VentsStarted, sp0 = HaloGunFx.VentsStopped;
                B.Net.Own.Overheated = true;
                yield return Seconds(0.6f);
                Transform prm = vb.HeldModel;
                int p0 = HaloFx.ParticleCount;
                yield return Seconds(0.6f);
                bool venting = HaloRemoteView.VentsShown - shown0 == 1 && HaloGunFx.VentsStarted - st0 == 1;
                HaloLabRunner.Log("vent: remote plasma rifle overheated: vents shown " + (HaloRemoteView.VentsShown - shown0) + ", started " + (HaloGunFx.VentsStarted - st0) + ", particles " + p0 + " -> " + HaloFx.ParticleCount + " | " + HaloFx.Stats);
                r.Check(venting, "vent: B's overheated plasma rifle vents (one loop, kept while overheated)");
                if (prm != null)
                {
                    r.Aim(prm.position + prm.forward * 0.6f + Vector3.up * 0.25f, prm.position, 45f);
                    r.Shot("net_pr_vent");
                }
                B.Net.Own.Overheated = false;
                yield return Seconds(0.6f);
                r.Check(HaloGunFx.VentsStopped - sp0 == 1, "vent: the remote vent stops when the overheated bit clears (stopped " + (HaloGunFx.VentsStopped - sp0) + ")");
            }

            B.Net.Own.Held = HaloNetU.TagIndex(ar);
            B.Net.Own.Stowed = HaloNetU.TagIndex(pistol);
            yield return Seconds(0.8f);

            // ---- 3. hits: B hits A (applied once), A hits B's hit boxes (published)
            int applied0 = HaloNetGame.StatHitsApplied;
            float hpBefore = HaloLocal.Health + HaloLocal.Shield;
            B.Net.NoteHit(HaloLocal.NetId, 12f, HaloNetU.TagIndex(ar), HaloNetWire.NoTag, false, false, false, false, false);
            B.Net.NoteHit(HaloLocal.NetId, 12f, HaloNetU.TagIndex(ar), HaloNetWire.NoTag, false, true, false, false, false);
            yield return Seconds(0.6f);
            r.Check(HaloNetGame.StatHitsApplied - applied0 == 1 && Mathf.Abs(HaloNetGame.StatPointsApplied - 24f) < 0.01f, "B's two hits reach A merged and once (" + (HaloNetGame.StatHitsApplied - applied0) + " hit, " + HaloNetGame.StatPointsApplied + " points)");
            r.Check(true, "A's health+shield " + hpBefore.ToString("0.00") + " -> " + (HaloLocal.Health + HaloLocal.Shield).ToString("0.00") + " (Combat's TakeHit)");
            // A's shot: a ray from A's eye to B's chest meets B's hit box (what the Combat module does), then TakeHit on it
            Vector3 aEye = feetA + Vector3.up * 1.65f;
            Vector3 chest = vb.Box(HaloRemoteView.ChestBox).position;
            RaycastHit hit;
            bool onBox = Physics.Raycast(aEye, (chest - aEye).normalized, out hit, 50f, 1 << HaloLayers.Hitbox, QueryTriggerInteraction.Ignore);
            HaloNetPlayer victim;
            r.Check(onBox && HaloNetGame.IsRemoteHitbox(hit.collider, out victim) && victim == pb, "A's ray meets B's hit box (" + (onBox ? hit.collider.name : "nothing") + ")");
            int bHits0 = B.Count(HaloNetEventKind.HitMe);
            if (onBox)
            {
                var dmg = hit.collider.GetComponent<IHaloDamageable>();
                dmg.TakeHit(new HaloHit { Amount = 20f, Cause = ar, SourcePlayer = HaloLocal.NetId, Point = hit.point, Direction = (chest - aEye).normalized });
            }

            yield return Seconds(0.6f);
            r.Check(B.Count(HaloNetEventKind.HitMe) == bHits0 + 1 && Mathf.Abs(B.Sum(HaloNetEventKind.HitMe) - 20f) < 0.01f, "B's client receives A's hit once (20 points)");

            // ---- 4. B dies, killed by A: the kill feed and A's kill
            int kills0 = HaloLocal.Kills, killed = 0;
            System.Action<HaloKill> onKill = k => { if (k.Victim == B.Net.MySlot && k.Killer == HaloLocal.NetId) killed++; };
            HaloEvents.Killed += onKill;
            B.Net.NoteDeath(HaloLocal.NetId, HaloNetU.TagIndex(ar), false, true, false, false, false, false, false);
            B.Net.Own.Alive = false;
            yield return Seconds(0.6f);
            HaloEvents.Killed -= onKill;
            r.Check(killed == 1 && HaloNetGame.StatDeathsSeen >= 1, "B's death reaches A's kill feed once (killer = A)");
            r.Check(HaloLocal.Kills == kills0 + 1, "A is credited with the kill by the Match module (" + kills0 + " -> " + HaloLocal.Kills + ")");
            r.Check(!vb.HitBoxesOn, "a dead player's hit boxes are off");
            B.Net.Own.Alive = true;

            // ---- 5. pickups, drops, objective events
            int taken = 0, dropped = 0;
            System.Action<int, int> onTaken = (item, slot) => { if (slot == B.Net.MySlot && item == 3) taken++; };
            int events = 0;
            System.Action<HaloNetDrop> onDrop = d => { if (d.Slot == B.Net.MySlot && d.Weapon == pistol && d.Rounds == 9 && d.Reserve == 40) dropped++; };
            System.Action<int, int, int> onEvent = (slot, kind, arg) => { if (slot == B.Net.MySlot && kind == 1 && arg == 2) events++; };
            HaloNetGame.GameEvent += onEvent;
            HaloNetGame.ItemTaken += onTaken;
            HaloNetGame.RemoteDropped += onDrop;
            B.Net.NotePickup(3);
            B.Net.NoteDrop(HaloNetU.TagIndex(pistol), 9, 40, 1f, HaloNetU.V(bFeet + Vector3.up), HaloNetU.V(Vector3.up));
            B.Net.NoteObjective(1, 2, HaloNetU.V(bFeet));
            yield return Seconds(0.8f);
            HaloNetGame.ItemTaken -= onTaken;
            HaloNetGame.RemoteDropped -= onDrop;
            HaloNetGame.GameEvent -= onEvent;
            r.Check(taken == 1 && dropped == 1 && events == 1, "B's pickup, drop (9 + 40 rounds) and game event reach A once each");
            bool placed = false, pickupGone = true;
            foreach (HaloPickups.Item it in HaloPickups.Items) { if (it.Dropped && it.Shooter == B.Net.MySlot) placed = true; if (it.Id == 3 && it.Present) pickupGone = false; }
            r.Check(placed && pickupGone, "the Combat module placed B's dropped pistol and took pickup 3 away (" + HaloPickups.Items.Count + " items)");
            float ago;
            r.Check(HaloNetGame.TryItemTaken(3, out ago) && ago < 5f, "the master's world block notes pickup 3 (" + ago.ToString("0.0") + " s ago)");

            // ---- 6. C arrives while the match runs: pulled in with the same start, reads the world state
            HaloNetGame.PublishParked(0, feetA + fwd * 3f + Vector3.up, Quaternion.identity, 0.8f, 2);
            Scripted C = Join(3, "[C]");
            yield return r.Wait(() => C.Net.Claimed && C.Net.Game.On, 20f, "C reads the master's game");
            r.Check(C.Net.Game.Seq == g.Seq && C.Net.Game.Start == g.Start, "C (late) is told game " + C.Net.Game.Seq + " and its start");
            C.Feet = bFeet + Vector3.Cross(Vector3.up, fwd) * 3f;
            C.Yaw = B.Yaw;
            C.Play(C.Net.Game.ZoneHash, C.Net.Game.Seq, C.Net.Game.Start, HaloNetU.MapPrint());
            yield return Seconds(1.5f);
            r.Check(HaloNetGame.ViewOf(C.Net.MySlot) != null && C.Net.Remotes.Count == 2, "C is in the match with A and B");
            bool des; float hp; int vf; HaloV3 vp; HaloQ vr;
            int at;
            r.Check(C.Net.World.Vehicle(0, out des, out hp, out vf, out vp, out vr) && vf == 2 && C.Net.World.ItemTakenAt(3, out at), "C reads the parked vehicle and pickup 3 from the world blocks");

            // ---- 7. A (the master) goes back to the hub: B and C play on (F's lobby keeps InMatch while others are in it; the hand-over rule)
            int k0a, s0a, k0b, s0b;
            C.Net.TeamTotals(0, out k0a, out s0a); C.Net.TeamTotals(1, out k0b, out s0b);
            HaloMatchControl.LeaveMatch();
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Hub, 30f, "A back in the hub");
            yield return Seconds(1f);
            r.Check(B.Net.Remotes.Count == 1 && C.Net.Remotes.Count == 1 && HaloNetGame.ViewOf(B.Net.MySlot) == null, "A left: B and C play on, A draws nobody");
            yield return Seconds(1.5f);
            r.Check(HaloNetGame.MatchRunning && C.Net.Game.On && C.Net.Game.Seq == g.Seq && C.Net.Game.Start == g.Start && C.Net.Lobby.Phase == 3,
                "the master went to the hub: game " + g.Seq + " goes on for B and C with the same start (C's lobby phase " + C.Net.Lobby.Phase + "; A in the hub still sees it running and can rejoin)");
            int k1a, s1a, k1b, s1b;
            C.Net.TeamTotals(0, out k1a, out s1a); C.Net.TeamTotals(1, out k1b, out s1b);
            r.Check(k1a == k0a && s1a == s0a && k1b == k0b && s1b == s0b && k0a + k0b >= 1, "the scoreboard is intact on C (kills " + k1a + " / " + k1b + ", score " + s1a + " / " + s1b + "; A's kill kept for its side)");

            // ---- 7b. while the master sits in the hub, B (the lowest id in the game) writes the world: a pickup and a parked vehicle stay fresh
            int bId = B.Net.LocalId, aId = HaloNetGame.Net.LocalId;
            yield return r.Wait(() => B.Net.World.Writing, 5f, "B the world writer");
            r.Check(B.Net.World.Writing && !HaloNetGame.Net.World.Writing && !C.Net.World.Writing && C.Net.WorldWriterId == bId && !HaloNetGame.WorldOwner,
                "the master is in the hub: B (player " + bId + ", the lowest id in the game) writes the world blocks, A and C do not");
            B.Net.NotePickup(7);
            B.Net.World.SetVehicle(0, false, 0.6f, 3, vp, vr);
            yield return Seconds(1f);
            Scripted D = Join(4, "[D]");
            yield return r.Wait(() => D.Net.Claimed && D.Net.Game.On, 20f, "D reads the game");
            D.Feet = C.Feet + fwd * 2f; D.Yaw = C.Yaw;
            D.Play(D.Net.Game.ZoneHash, D.Net.Game.Seq, D.Net.Game.Start, HaloNetU.MapPrint());
            yield return Seconds(1.5f);
            bool dDes; float dHp; int dVf; HaloV3 dVp; HaloQ dVr; int dAt;
            r.Check(D.Net.World.Vehicle(0, out dDes, out dHp, out dVf, out dVp, out dVr) && dVf == 3 && Mathf.Abs(dHp - 0.6f) < 0.02f && D.Net.World.ItemTakenAt(7, out dAt),
                "late joiner D reads B's fresh world: vehicle 0 parked (flags 3, health " + dHp.ToString("0.00") + ") and pickup 7 taken");

            // ---- 7c. the master rejoins: it takes the writer role back and goes on from B's world
            HaloLobby.Join();
            yield return r.Wait(() => HaloSession.Playing, 120f, "A back in the match");
            yield return r.Wait(() => HaloNetGame.Net.World.Writing, 10f, "A the world writer again");
            yield return Seconds(1f);
            bool aDes; float aHp; int aVf; HaloV3 aVp; HaloQ aVr; int aAt;
            bool aHas = HaloNetGame.Net.World.Vehicle(0, out aDes, out aHp, out aVf, out aVp, out aVr);
            float aOff = aHas ? Vector3.Distance(HaloNetU.V(aVp), HaloNetU.V(vp)) : -1f;
            r.Check(HaloNetGame.Net.World.Writing && HaloNetGame.WorldOwner && !B.Net.World.Writing && D.Net.WorldWriterId == aId && HaloNetGame.Net.World.ItemTakenAt(7, out aAt) && aHas && aOff < 0.5f,
                "A rejoined: the master writes the world again (B stopped), keeps pickup 7 and vehicle 0 where B parked it (" + aOff.ToString("0.00") + " m off)");
            r.Check(HaloNetGame.Net.Remotes.Count == 3, "A plays with B, C and D (" + HaloNetGame.Net.Remotes.Count + ")");
            r.Shot("net_master_rejoined");
            HaloMatchControl.LeaveMatch();
            yield return r.Wait(() => HaloSession.Current == HaloSession.Phase.Hub, 30f, "A back in the hub again");
            yield return Seconds(1.5f);

            // ---- 8. A leaves the room: the next player becomes the master, keeps the game and writes the world blocks
            _world.Leave(1);
            yield return r.Wait(() => B.Net.IsInstanceMaster || C.Net.IsInstanceMaster, 15f, "a new master");
            yield return Seconds(2.5f);
            Scripted M = B.Net.IsInstanceMaster ? B : C;
            string mName = M == B ? "B" : "C";
            r.Check(C.Net.Game.On && C.Net.Game.Seq == g.Seq && C.Net.Game.Start == g.Start && C.Net.Lobby.Phase == 3 && B.Net.Remotes.Count == 2,
                "A left the room: " + mName + " is the master and keeps game " + g.Seq + " running (same start, lobby phase " + C.Net.Lobby.Phase + "); B, C and D still in the match");
            bool des2; float hp2; int vf2; HaloV3 vp2; HaloQ vr2; int at2;
            r.Check(M.Net.World.Writing && C.Net.World.Vehicle(0, out des2, out hp2, out vf2, out vp2, out vr2) && C.Net.World.ItemTakenAt(3, out at2) && C.Net.World.ItemTakenAt(7, out at2) && Vector3.Distance(HaloNetU.V(vp2), HaloNetU.V(vp)) < 0.5f, mName + " writes the world blocks now: C still reads vehicle 0 where it was parked and pickups 3 and 7");
            int k2a, s2a, k2b, s2b;
            C.Net.TeamTotals(0, out k2a, out s2a); C.Net.TeamTotals(1, out k2b, out s2b);
            r.Check(k2a == k0a && s2a == s0a && k2b == k0b && s2b == s0b, "the scoreboard is still intact under the new master (kills " + k2a + " / " + k2b + ")");
            HaloLog.Info(HaloNetGame.Net.StatsLine());
            HaloNetGame.LabRoot = null;
            HaloNetGame.LabHead = null;
        }

        /// <summary>A lab AI unit on the host: counts the hits Net applies to it.</summary>
        sealed class LabUnit : HaloUnit
        {
            public int Hits; public float Points; public int LastSource = -9;
            public override void TakeHit(HaloHit hit) { Hits++; Points += hit.Amount; LastSource = hit.SourcePlayer; }
        }

        /// <summary>
        /// "net-coop": campaign co-op plumbing over the loopback, on Blood Gulch treated as a mission (HaloNetGame.LabCampaign; the Mission / AI /
        /// Characters modules are not needed). Phase 1: A hosts 3 lab units, scripted B reads them, hits one (applied on A's unit), A sends a
        /// mission event. Phase 2: scripted S (player 0, the lowest id) arrives and becomes the master: it publishes 2 units (moving, firing, one
        /// dies); A draws them as puppets (stand-ins), replays the shots, and A's ray on a puppet reaches S as a hit on that unit.
        /// </summary>
        static IEnumerator Coop(HaloLabRunner r)
        {
            if (_world == null) { r.Fail("the loopback was not installed"); yield break; }
            EnsurePlayer();
            new GameObject("Lab net ticker").AddComponent<Ticker>();
            HaloNetGame.LabCampaign = true;
            Transform root = new GameObject("Lab A root").transform, head = new GameObject("Lab A head").transform;
            HaloNetGame.LabRoot = root; HaloNetGame.LabHead = head;
            yield return r.Wait(() => HaloNetGame.Claimed, 30f, "A's claim");
            Scripted B = Join(2, "[B]");
            yield return r.Wait(() => B.Net.Claimed, 20f, "B's claim");
            yield return r.Wait(() => HaloLobby.Mine != null && HaloLobby.IsMaster && HaloLobby.CanEdit, 30f, "the lobby");
            HaloLobby.Mine.Type = HaloScenario.GameType.Slayer;
            HaloLobby.Mine.Teams = false;
            HaloLobby.Mine.MapName = HaloLabBoot.Arg("-halomap", "bloodgulch");
            HaloLobby.Changed();
            r.Check(HaloLobby.PressStart() == null, "A (master) starts the game the co-op rides on");
            yield return r.Wait(() => HaloSession.Playing, 240f, "A playing");
            if (!HaloSession.Playing) { r.Fail("A never reached Playing"); yield break; }
            yield return r.Wait(() => B.Net.Game.On && B.Net.Game.Start != 0, 10f, "the game command");
            HaloNet.GameInfo g = B.Net.Game;
            Vector3 feet;
            if (!HaloPlayer.TryFeet(out feet)) feet = HaloLocal.Position;
            Vector3 fwd = HaloLocal.Forward.sqrMagnitude > 0.5f ? HaloLocal.Forward : Vector3.forward;
            fwd.y = 0f; fwd.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            root.SetPositionAndRotation(feet, Quaternion.LookRotation(fwd));
            head.SetPositionAndRotation(feet + Vector3.up * 1.65f, Quaternion.LookRotation(fwd));
            B.Feet = feet + side * 3f;
            B.Play(g.ZoneHash, g.Seq, g.Start, HaloNetU.MapPrint());
            B.Net.CoopMission = true;
            yield return Seconds(1f);
            r.Check(HaloNetGame.CampaignHost && HaloNetGame.Net.Coop.Writing, "phase 1: A (the master) hosts the co-op mission");

            var units = new List<LabUnit>();
            for (int i = 0; i < 3; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Lab AI unit " + i;
                Object.Destroy(go.GetComponent<Collider>());
                LabUnit u = go.AddComponent<LabUnit>();
                u.Team = HaloTeam.Covenant;
                go.transform.position = HaloSpawnsGround(feet + fwd * (8f + 2f * i) + side * ((i - 1) * 2f)) + Vector3.up;
                u.AimPoint = feet + Vector3.up * 1.5f;
                HaloNetGame.TrackUnit(u);
                units.Add(u);
            }

            units[1].Firing = true;
            yield return Seconds(1.2f);
            int seen = 0, firing = 0, serial1 = 0;
            for (int p = 0; p < HaloNetCoop.MaxActors; p++)
            {
                HaloNetActor a = B.Net.Coop.Actor(p);
                if (a.Serial == 0) continue;
                seen++;
                if (Vector3.Distance(HaloNetU.V(a.Position), units[1].transform.position) < 0.05f) { serial1 = a.Serial; if (a.FireCounter > 0) firing++; }
            }

            r.Check(seen == 3 && serial1 != 0, "B reads the host's 3 units with their positions (" + seen + " actors)");
            r.Check(firing == 1, "B sees unit 1's shots counted (fire counter > 0)");
            units[1].Firing = false;
            B.Net.NoteHitActor(serial1, 15f, HaloNetWire.NoTag, false, true, false, false, false);
            yield return Seconds(0.8f);
            r.Check(units[1].Hits == 1 && Mathf.Abs(units[1].Points - 15f) < 0.01f && units[1].LastSource == B.Net.MySlot, "B's hit on the unit is applied once on the host (" + units[1].Hits + " hit, source " + units[1].LastSource + ")");
            HaloScenario sc = HaloSession.Scenario;
            if (sc != null && sc.ObjectNames.Count > 0)
            {
                HaloNetGame.NoteObjectCreated(sc.ObjectNames[0]);
                yield return Seconds(0.6f);
                bool got = false;
                foreach (HaloNetMissionEvent e in B.Net.Coop.NewEvents) if (e.Kind == HaloNetMissionEvent.Create && e.Value == 0) got = true;
                r.Check(got, "B gets the host's 'object created' event (" + sc.ObjectNames[0] + ")");
            }
            else r.Check(true, "(this map has no named scenario objects: the object event is checked in the harness)");

            // ---- phase 2: S (player 0) arrives and becomes the master; A follows and draws S's units
            for (int i = 0; i < units.Count; i++) Object.Destroy(units[i].gameObject);
            Scripted S = Join(0, "[S]");
            S.Feet = feet - side * 3f;
            S.Net.CoopMission = true;
            yield return r.Wait(() => S.Net.Claimed, 20f, "S's claim");
            S.Play(g.ZoneHash, g.Seq, g.Start, HaloNetU.MapPrint());
            yield return r.Wait(() => S.Net.IsInstanceMaster, 10f, "S the master");
            HaloNet.LobbyInfo lob = B.Net.Lobby;      // (what a real master's lobby does: keep commanding the running game)
            S.Net.PublishLobbyState(3, 0, g.ZoneHash, 99, lob.RulesA, lob.RulesB, lob.RulesC, true, g.Seq, g.Start);
            yield return r.Wait(() => S.Net.Game.On && S.Net.Coop.Writing, 20f, "S hosting");
            yield return Seconds(0.5f);
            r.Check(S.Net.Coop.Writing && !HaloNetGame.CampaignHost && !HaloNetGame.Net.Coop.Writing, "phase 2: S (lowest id) is the master and hosts; A follows");
            Vector3 p0 = HaloSpawnsGround(feet + fwd * 7f), p1 = HaloSpawnsGround(feet + fwd * 9f + side * 2f);
            float yaw = Quaternion.LookRotation(-fwd).eulerAngles.y;
            var a0 = new HaloNetActor { Serial = 101, Team = (int)HaloTeam.Covenant, Alive = true, Position = HaloNetU.V(p0), BodyYaw = yaw, UnitTag = HaloNetWire.NoTag, VariantTag = HaloNetWire.NoTag, WeaponTag = HaloNetWire.NoTag };
            var a1 = a0;
            a1.Serial = 102;
            a1.Position = HaloNetU.V(p1);
            for (int p = 0; p < HaloNetCoop.MaxActors; p++) S.Net.Coop.ClearActor(p);
            S.Net.Coop.SetActor(0, a0);
            S.Net.Coop.SetActor(5, a1);
            yield return Seconds(1.5f);
            HaloNetPuppet q0 = HaloNetGame.PuppetAt(0), q1 = HaloNetGame.PuppetAt(5);
            r.Check(q0 != null && q1 != null && q0.Serial == 101 && q1.Serial == 102, "A draws S's two units as puppets");
            r.Check(q0 != null && Vector3.Distance(q0.transform.position, p0 + Vector3.up) < 0.1f, "puppet 0 stands where S put it");
            float t0 = Time.realtimeSinceStartup;
            float maxStep = 0f;
            Vector3 last = q0 != null ? q0.transform.position : Vector3.zero;
            while (Time.realtimeSinceStartup - t0 < 2f)
            {
                float k = (Time.realtimeSinceStartup - t0) / 2f;
                a0.Position = HaloNetU.V(p0 + side * (3f * k));
                a0.FireCounter = (int)(k * 10f);
                S.Net.Coop.SetActor(0, a0);
                yield return null;
                if (q0 != null) { maxStep = Mathf.Max(maxStep, (q0.transform.position - last).magnitude); last = q0.transform.position; }
            }

            a0.Position = HaloNetU.V(p0 + side * 3f);
            a0.FireCounter = 10;
            S.Net.Coop.SetActor(0, a0);
            yield return Seconds(0.6f);
            r.Check(q0 != null && Vector3.Distance(q0.transform.position, p0 + side * 3f + Vector3.up) < 0.2f && maxStep < 0.25f, "puppet 0 follows S's unit smoothly (largest step " + maxStep.ToString("0.000") + " m)");
            r.Check(q0 != null && q0.ShotsPlayed >= 8, "puppet 0's shots are replayed (" + (q0 != null ? q0.ShotsPlayed : 0) + ")");
            r.Aim(feet + Vector3.up * 1.7f - fwd * 2f, feet + fwd * 8f + Vector3.up, 60f);
            r.Shot("net_coop_puppets");
            Vector3 eye = feet + Vector3.up * 1.65f;
            RaycastHit hit;
            bool onIt = q1 != null && Physics.Raycast(eye, (q1.transform.position - eye).normalized, out hit, 60f, 1 << HaloLayers.Hitbox, QueryTriggerInteraction.Ignore) && hit.collider.GetComponent<HaloNetPuppet>() == q1;
            r.Check(onIt, "A's ray meets puppet 1");
            int before = S.Count(HaloNetEventKind.HitActor);
            if (onIt) q1.GetComponent<IHaloDamageable>().TakeHit(new HaloHit { Amount = 30f, SourcePlayer = HaloLocal.NetId });
            yield return Seconds(0.8f);
            HaloNetEvent hitEv = default(HaloNetEvent);
            foreach (HaloNetEvent e in S.Seen) if (e.Kind == HaloNetEventKind.HitActor) hitEv = e;
            r.Check(S.Count(HaloNetEventKind.HitActor) == before + 1 && hitEv.Value == 102 && Mathf.Abs(hitEv.Amount - 30f) < 0.01f, "S (host) gets A's hit on unit 102 once (30 points)");
            float hp = HaloLocal.Health + HaloLocal.Shield;
            int applied = HaloNetGame.StatHitsApplied;
            S.Net.NoteHit(HaloLocal.NetId, 20f, HaloNetWire.NoTag, HaloNetWire.NoTag, false, false, false, false, false, true);
            a1.Alive = false;
            S.Net.Coop.SetActor(5, a1);
            yield return Seconds(0.8f);
            r.Check(q1 != null && !q1.Alive, "unit 102 dies on A's screen");
            r.Check(HaloNetGame.StatHitsApplied == applied + 1, "the AI's hit reaches A once: health+shield " + hp.ToString("0.00") + " -> " + (HaloLocal.Health + HaloLocal.Shield).ToString("0.00"));

            // ---- phase 3: the host S leaves mid-mission; A (the next master) takes over: the host state is adopted, its puppets become its units, B stays in
            HaloTag cyborg = HaloSession.Map != null ? HaloSession.Map.Find("bipd", "characters\\cyborg_mp\\cyborg_mp") : null;
            Vector3 p2 = HaloSpawnsGround(feet + fwd * 6f - side * 2f);
            var a2 = new HaloNetActor { Serial = 201, Team = (int)HaloTeam.Covenant, Alive = true, Position = HaloNetU.V(p2), BodyYaw = yaw, Body = 1f, UnitTag = HaloNetU.TagIndex(cyborg), VariantTag = HaloNetWire.NoTag, WeaponTag = HaloNetWire.NoTag };
            S.Net.Coop.SetActor(8, a2);
            byte[] labState = { 7, 7, 7, 42 };
            byte[] adopted = null;
            bool promoted = false;
            HaloNetHostState.Register("lab", 50, () => new byte[] { 1 }, b => adopted = b);
            System.Action onPromoted = () => promoted = true;
            HaloNetHostState.Promoted += onPromoted;
            int aBlob = HaloNetGame.Net.HostStream.CompleteId;
            S.Net.HostStream.SetBlob(HaloNetHostState.Pack(new List<KeyValuePair<string, byte[]>> { new KeyValuePair<string, byte[]>("lab", labState) }));
            yield return r.Wait(() => HaloNetGame.Net.HostStream.CompleteId != aBlob && HaloNetGame.PuppetAt(8) != null, 15f, "S's host state blob and the cyborg puppet");
            HaloNetPuppet q2 = HaloNetGame.PuppetAt(8);
            HaloUnit u2 = q2 != null ? q2.Unit : null;
            r.Check(HaloNetHostState.HaveBlob, "A holds S's host state blob (" + HaloNetHostState.BlobAge.ToString("0.0") + " s old)");
            r.Check(u2 != null && u2.Puppet, "A draws S's cyborg as a real Characters unit in puppet mode" + (cyborg == null ? " (no cyborg tag on this map)" : ""));
            r.Aim(feet + Vector3.up * 1.7f - fwd * 2f, p2 + Vector3.up, 50f);
            r.Shot("net_coop_before_handover");
            int promotions = HaloNetHostState.Promotions, bBlob = B.Net.HostStream.CompleteId;
            S.T.Dispose();
            Scripts.Remove(S);
            yield return r.Wait(() => HaloNetHostState.Promotions > promotions, 15f, "A's promotion to host");
            yield return Seconds(1.5f);
            r.Check(HaloNetGame.Net.IsInstanceMaster && HaloNetGame.Net.Coop.Writing && HaloNetGame.CampaignHost, "S left: A is the master and hosts the mission");
            r.Check(promoted && adopted != null && adopted.Length == 4 && adopted[3] == 42, "the parts adopted S's last host state, then Promoted was raised");
            r.Check(HaloSession.Playing && HaloLobby.ViewPhase == HaloLobbyState.InMatch, "no reload, nobody to the lobby: A still plays, the lobby stays InMatch");
            r.Check(u2 != null && !u2.Puppet && HaloNetGame.SerialOf(u2) == 201 && u2.Alive, "the cyborg is A's own unit now (Puppet off, serial 201 kept)");
            HaloNetActor seenByB = B.Net.Coop.Actor(8);
            r.Check(B.Net.Game.On && B.Net.Remotes.Count >= 1 && seenByB.Serial == 201 && HaloV3.Distance(seenByB.Position, a2.Position) < 0.3f, "B stays in the mission and reads unit 201 from A's blocks at the same place");
            if (u2 != null)
            {
                float body = u2.Body, shield = u2.Shield;
                B.Net.NoteHitActor(201, 40f, HaloNetWire.NoTag, false, false, false, false, false);
                yield return Seconds(1f);
                r.Check(u2.Body < body || u2.Shield < shield || !u2.Alive, "B's hit on unit 201 is applied by the new host (body " + body.ToString("0.00") + " -> " + u2.Body.ToString("0.00") + ", shield " + shield.ToString("0.00") + " -> " + u2.Shield.ToString("0.00") + ", applied " + HaloNetGame.StatActorHitsApplied + ")");
            }

            yield return r.Wait(() => B.Net.HostStream.CompleteId > bBlob && HaloNetHostState.Captures > 0, 10f, "the new host's own host state");
            r.Check(HaloNetHostState.Captures > 0 && B.Net.HostStream.Complete != null, "the new host streams its own host state (B received it)");
            r.Aim(feet + Vector3.up * 1.7f - fwd * 2f, p2 + Vector3.up, 50f);
            r.Shot("net_coop_after_handover");
            HaloNetHostState.Promoted -= onPromoted;
            HaloNetHostState.Unregister("lab");
            HaloNetGame.LabCampaign = false;
            HaloNetGame.LabRoot = null;
            HaloNetGame.LabHead = null;
        }

        // ------------------------------------------------------------------ net-coop-vehicles

        static HaloNetActor GruntRecord(int serial, HaloTag grunt, Vector3 pos, float yaw, int vehicle, int seat, bool exiting)
        {
            return new HaloNetActor
            {
                Serial = serial, Team = (int)HaloTeam.Covenant, Alive = true, UnitTag = HaloNetU.TagIndex(grunt), VariantTag = HaloNetWire.NoTag, WeaponTag = HaloNetWire.NoTag,
                Position = HaloNetU.V(pos), BodyYaw = yaw, AimYaw = yaw, Body = 1f, Shield = 0f, SeatVehicle = vehicle, Seat = seat, Exiting = exiting,
            };
        }

        static HaloNetActor VehicleRecord(HaloVehicle v, HaloTag tag, Vector3 pos, Quaternion rot, Vector3 vel, int fire, bool open)
        {
            return new HaloNetActor
            {
                IsVehicle = true, Serial = HaloNetCoop.VehicleSerialBase + v.Id, Alive = true, UnitTag = HaloNetU.TagIndex(tag), VehicleId = v.Id, Position = HaloNetU.V(pos),
                Rotation = HaloNetU.Q(rot), Velocity = HaloNetU.V(vel), FireCounter = fire, Vitality = 1f, Open = open, MuzzleOffset = new HaloV3(0f, 0.3f, 3f), ShotDir = HaloNetU.V(rot * Vector3.forward),
            };
        }

        /// <summary>
        /// "net-coop-vehicles": two clients in a30 over the loopback. S (scripted, the master) hosts: it creates lz_cship and a banshee by their
        /// scenario names, flies the dropship in with four grunts seated (records of the real a30 tags), opens its pod, the grunts get out, the banshee
        /// fires, a grunt speaks. A (the real game) follows the mission: the dropship drawn smoothly, the grunts seated as puppets, their exit clip,
        /// the banshee's gunfire replayed, the line heard once. Then S leaves: A becomes the host (hand-over), seats a grunt in the banshee and flies it
        /// with its gun firing (the AI surface), a grunt speaks - scripted follower C reads all of it from A's records.
        /// </summary>
        static IEnumerator CoopVehicles(HaloLabRunner r)
        {
            if (_world == null) { r.Fail("the loopback was not installed"); yield break; }
            EnsurePlayer();
            new GameObject("Lab net ticker").AddComponent<Ticker>();
            yield return r.Wait(() => HaloNetGame.Claimed, 30f, "A's claim");
            Scripted S = Join(0, "[S]");
            yield return r.Wait(() => S.Net.Claimed && S.Net.IsInstanceMaster && !HaloNetGame.IsMaster, 20f, "S the master");
            int zone = HaloNetWire.NameHash("a30");
            int start = HaloNetU.UtcSeconds();
            int ra, rb, rc;
            HaloNetRules.Pack(new HaloRules { MapName = "a30" }, out ra, out rb, out rc);
            S.Net.PublishLobbyState(3, 0, zone, 5, ra, rb, (rc & 0xF) | HaloNetRules.CampaignBit | (1 << 5), true, 1, start);
            S.Play(zone, 1, start, 0);
            S.Net.CoopMission = true;
            yield return r.Wait(() => HaloSession.Playing && HaloCampaign.Active, 400f, "A pulled into S's a30 mission");
            if (!HaloSession.Playing) { r.Fail("A never played a30"); yield break; }
            yield return Seconds(2f);
            r.Check(!HaloNetGame.CampaignHost && HaloCampaign.Mission == "a30" && !S.Net.Coop.Writing == false, "A follows S's a30 mission (no scripts / AI here); S hosts");
            HaloScenario sc = HaloSession.Scenario;
            HaloMap map = HaloSession.Map;
            int shipId = -1, bansheeId = -1, shipName = -1, bansheeName = -1;
            for (int i = 0; i < sc.Vehicles.Count; i++)
            {
                var p = sc.Vehicles[i];
                if (p.Type == null || p.NameIndex < 0 || p.NameIndex >= sc.ObjectNames.Count) continue;
                string n = sc.ObjectNames[p.NameIndex];
                if (n == "lz_cship") { shipId = i; shipName = p.NameIndex; }
                if (bansheeId < 0 && p.Type.Name.Contains("banshee")) { bansheeId = i; bansheeName = p.NameIndex; }
            }

            r.Check(shipId >= 0 && bansheeId >= 0, "a30 has lz_cship (vehicle " + shipId + ") and a named banshee (vehicle " + bansheeId + ")");
            if (shipId < 0 || bansheeId < 0) yield break;
            S.Net.Coop.AddEvent(HaloNetMissionEvent.Create, shipName);
            S.Net.Coop.AddEvent(HaloNetMissionEvent.Create, bansheeName);
            yield return r.Wait(() => HaloVehicles.Find(shipId) != null && HaloVehicles.Find(bansheeId) != null, 15f, "the host's object events create the dropship and the banshee here");
            HaloVehicle ship = HaloVehicles.Find(shipId), banshee = HaloVehicles.Find(bansheeId);
            if (ship == null || banshee == null) { r.Fail("the vehicles were not created on A"); yield break; }
            r.Check(true, "lz_cship and " + banshee.name + " exist on A with the host's ids (" + shipId + ", " + bansheeId + ")");
            HaloTag grunt = null, shipTag = sc.Vehicles[shipId].Type, bansheeTag = sc.Vehicles[bansheeId].Type;
            foreach (HaloTag t in map.OfClass("bipd")) if (t.Name.Contains("grunt")) { grunt = t; break; }
            var seats = new List<int>();
            for (int s = 0; s < ship.Seats.Count && seats.Count < 4; s++) if (ship.Seats[s].Label.IndexOf("passenger", System.StringComparison.OrdinalIgnoreCase) >= 0) seats.Add(s);
            r.Check(grunt != null && seats.Count == 4, "a grunt biped and four passenger seats on the dropship");
            Vector3 flagPos; Quaternion flagRot;
            if (!HaloCinematic.Flag("lz_cship_flag", out flagPos, out flagRot)) { flagPos = ship.transform.position; flagRot = ship.transform.rotation; }
            Vector3 fwd = flagRot * Vector3.forward;
            Vector3 from = flagPos - fwd * 60f + Vector3.up * 35f, to = flagPos;
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

            // ---- the flight in: 8 s, the dropship record and four seated grunts (10 Hz like the host)
            HaloNetGame.StatVehicleMaxStep = 0f;
            HaloNetGame.StatVehicleJerk = 0f;
            HaloNetGame.StatHitches = 0;
            float dur = 8f, t0 = Time.time, nextWrite = 0f;
            bool shotMid = false;
            while (Time.time - t0 < dur)
            {
                float k = (Time.time - t0) / dur;
                float e = k * k * (3f - 2f * k);
                Vector3 pos = Vector3.Lerp(from, to, e);
                Vector3 vel = (to - from) * (6f * k * (1f - k)) / dur;
                if (Time.time >= nextWrite)
                {
                    nextWrite = Time.time + 0.1f;
                    S.Net.Coop.SetActor(60, VehicleRecord(ship, shipTag, pos, rot, vel, 0, false));
                    for (int g = 0; g < 4; g++) S.Net.Coop.SetActor(g, GruntRecord(11 + g, grunt, pos, rot.eulerAngles.y, shipId, seats[g], false));
                }

                if (!shotMid && k > 0.55f)
                {
                    shotMid = true;
                    int seated = 0;
                    for (int g = 0; g < 4; g++) { HaloNetPuppet q = HaloNetGame.PuppetAt(g); if (q != null && q.Unit != null && q.Unit.Seat != null) seated++; }
                    r.Check(seated == 4, "the four grunts are seated in the dropship on A (" + seated + "/4 puppets in seats)");
                    r.Aim(ship.transform.position - fwd * 18f + Vector3.up * 6f + flagRot * Vector3.right * 14f, ship.transform.position, 55f);
                    r.Shot("netveh_dropship_flying");
                }

                yield return null;
            }

            S.Net.Coop.SetActor(60, VehicleRecord(ship, shipTag, to, rot, Vector3.zero, 0, false));
            yield return Seconds(0.6f);
            float off = Vector3.Distance(ship.transform.position, to);
            r.Check(off < 0.3f && HaloNetGame.StatVehicleJerk < 1.5f, "A draws the dropship smoothly to its hover point (largest step " + HaloNetGame.StatVehicleMaxStep.ToString("0.000") + " m a frame - at most " + (1f + HaloNetGame.StatVehicleJerk).ToString("0.00") + " x its own speed x the frame time; " + off.ToString("0.00") + " m off at the end; " + HaloNetGame.StatHitches + " long frame(s) of this lab left out) worst: " + HaloNetGame.StatVehicleJerkInfo);
            r.Check(HaloNetGame.CoopVehicle(shipId) && ship.RemoteDriven, "the Vehicles module leaves the dropship to the host's record");

            // ---- the pod opens, the grunts get out
            S.Net.Coop.SetActor(60, VehicleRecord(ship, shipTag, to, rot, Vector3.zero, 0, true));
            yield return Seconds(1f);
            r.Check(ship.IsOpen, "the dropship's pod opens on A (unit_open replayed)");
            for (int g = 0; g < 4; g++) S.Net.Coop.SetActor(g, GruntRecord(11 + g, grunt, to - Vector3.up * 5f + flagRot * Vector3.right * (g - 1.5f) * 1.5f, rot.eulerAngles.y, -1, -1, true));
            bool sawExiting = false;
            float until = Time.time + 2.5f;
            while (Time.time < until)
            {
                for (int g = 0; g < 4; g++) { HaloNetPuppet q = HaloNetGame.PuppetAt(g); if (q != null && q.Unit != null && q.Unit.Exiting) sawExiting = true; }
                yield return null;
            }

            int exits = 0;
            for (int g = 0; g < 4; g++) { HaloNetPuppet q = HaloNetGame.PuppetAt(g); if (q != null && q.Exits > 0 && q.Unit != null && q.Unit.Seat == null) exits++; }
            r.Check(exits == 4 && sawExiting, "the four grunts get out on A with their exit clip (" + exits + "/4 out, exit clip seen " + sawExiting + ")");
            r.Aim(to - fwd * 16f + Vector3.up * 2f + flagRot * Vector3.right * 10f, to - Vector3.up * 3f, 60f);
            r.Shot("netveh_troops_out");
            for (int g = 0; g < 4; g++) S.Net.Coop.SetActor(g, GruntRecord(11 + g, grunt, to - Vector3.up * 5f + flagRot * Vector3.right * (g - 1.5f) * 1.5f, rot.eulerAngles.y, -1, -1, false));
            yield return Seconds(0.5f);

            // ---- body states from the host's records: camouflage, feigned death (+ getting up), latched, leaping, the jackal's shield region
            {
                var gp = new Vector3[4];
                for (int g = 0; g < 4; g++) gp[g] = to - Vector3.up * 5f + flagRot * Vector3.right * (g - 1.5f) * 1.5f;
                float yaw = rot.eulerAngles.y;
                HaloTag jackal = null;
                foreach (HaloTag t in map.OfClass("bipd")) if (t.Name.Contains("jackal")) { jackal = t; break; }
                Vector3 jp = gp[3] + flagRot * Vector3.right * 2f;
                HaloNetActor jr = default(HaloNetActor);
                if (jackal != null) { jr = GruntRecord(16, jackal, jp, yaw, -1, -1, false); jr.Shield = 1f; S.Net.Coop.SetActor(4, jr); }
                HaloNetActor c0 = GruntRecord(11, grunt, gp[0], yaw, -1, -1, false); c0.CamoHidden = true;
                HaloNetActor f1 = GruntRecord(12, grunt, gp[1], yaw, -1, -1, false); f1.BodyState = HaloNetActor.BodyFeigning; f1.FeignHard = true; f1.FeignDir = 3; f1.FeignRegion = 1;
                HaloNetActor l2 = GruntRecord(13, grunt, gp[2], yaw, -1, -1, false); l2.BodyState = HaloNetActor.BodyLatched;
                HaloNetActor j3 = GruntRecord(14, grunt, gp[3] + Vector3.up * 1.2f, yaw, -1, -1, false); j3.BodyState = HaloNetActor.BodyLeaping; j3.Velocity = HaloNetU.V(flagRot * Vector3.right * 4f);
                S.Net.Coop.SetActor(0, c0); S.Net.Coop.SetActor(1, f1); S.Net.Coop.SetActor(2, l2); S.Net.Coop.SetActor(3, j3);
                yield return Seconds(1f);
                HaloNetPuppet q0 = HaloNetGame.PuppetAt(0), q1 = HaloNetGame.PuppetAt(1), q2 = HaloNetGame.PuppetAt(2), q3 = HaloNetGame.PuppetAt(3), q4 = jackal != null ? HaloNetGame.PuppetAt(4) : null;
                int shown = 0;
                if (q0 != null) foreach (Renderer rr in q0.GetComponentsInChildren<Renderer>()) if (rr.enabled && rr.GetComponent<SkinnedMeshRenderer>() != null) shown++;
                r.Check(q0 != null && q0.Unit != null && q0.Unit.CamoHidden && shown == 0, "A: the host's camouflaged grunt is hidden on its puppet (" + shown + " skinned renderers drawn)");
                r.Check(q1 != null && q1.Unit != null && q1.Unit.Feigning && q1.Unit.Animator != null && q1.Unit.Animator.Feigning && q1.Unit.FeignHard && q1.Unit.FeignDir == 3,
                        "A: the host's feigning grunt lies in its held death clip on the puppet (" + (q1 != null && q1.Unit != null && q1.Unit.Animator != null ? q1.Unit.Animator.DeathName : "-") + ", hard, from behind)");
                r.Check(q2 != null && q2.Unit != null && q2.Unit.Latched, "A: the host's latched unit plays melee-continuous on its puppet (action " + (q2 != null && q2.Unit != null && q2.Unit.Animator != null ? q2.Unit.Animator.Action.ToString() : "-") + ")");
                r.Check(q3 != null && q3.Unit != null && !q3.Unit.Grounded && (q3.StatesSeen & 8) != 0, "A: the host's leaping unit is airborne on its puppet (leap state seen)");
                Vector3 mid = (gp[0] + gp[3]) * 0.5f;
                r.Aim(mid - fwd * 7f + Vector3.up * 2.5f, mid + Vector3.up * 0.5f, 60f);
                r.Shot("netbody_states");
                if (jackal != null)
                {
                    string before = q4 != null && q4.Unit != null && q4.Unit.Permutations.ContainsKey("shield") ? q4.Unit.Permutations["shield"] : "-";
                    jr.Shield = 0f; S.Net.Coop.SetActor(4, jr);
                    yield return Seconds(0.8f);
                    string offP = q4 != null && q4.Unit != null && q4.Unit.Permutations.ContainsKey("shield") ? q4.Unit.Permutations["shield"] : "-";
                    r.Aim(jp - fwd * 4f + Vector3.up * 1.5f, jp + Vector3.up * 0.6f, 60f);
                    r.Shot("netbody_jackal_shield_off");
                    jr.Shield = 0.5f; S.Net.Coop.SetActor(4, jr);
                    yield return Seconds(0.8f);
                    string back = q4 != null && q4.Unit != null && q4.Unit.Permutations.ContainsKey("shield") ? q4.Unit.Permutations["shield"] : "-";
                    r.Check(q4 != null && q4.Unit != null && q4.Unit.MaxShield > 0f && !before.StartsWith("~") && offP.StartsWith("~") && !back.StartsWith("~"),
                            "A: the jackal puppet's shield region follows the host's shield (" + jackal.Name + ": " + before + " -> " + offP + " at 0 -> " + back + " when it recharges; max shield " + (q4 != null && q4.Unit != null ? q4.Unit.MaxShield.ToString("0") : "-") + ")");
                    S.Net.Coop.ClearActor(4);
                }
                else r.Check(false, "a30 has a jackal biped");

                for (int g = 0; g < 4; g++) S.Net.Coop.SetActor(g, GruntRecord(11 + g, grunt, gp[g], yaw, -1, -1, false));
                yield return Seconds(1f);
                shown = 0;
                if (q0 != null) foreach (Renderer rr in q0.GetComponentsInChildren<Renderer>()) if (rr.enabled && rr.GetComponent<SkinnedMeshRenderer>() != null) shown++;
                r.Check(q0 != null && !q0.Unit.CamoHidden && shown > 0 && q1 != null && !q1.Unit.Feigning && !q1.Unit.Animator.Feigning && q2 != null && !q2.Unit.Latched && q3 != null && q3.Unit.Grounded,
                        "A: back to normal when the host says so (camo off: " + shown + " renderers drawn; the feigning grunt gets up (action " + (q1 != null && q1.Unit != null && q1.Unit.Animator != null ? q1.Unit.Animator.Action.ToString() : "-") + "); unlatched; landed)");
            }

            // ---- the banshee flies past, firing
            Vector3 bFrom = to + flagRot * Vector3.right * 40f + Vector3.up * 10f, bTo = bFrom - flagRot * Vector3.right * 80f;
            Quaternion bRot = Quaternion.LookRotation(bTo - bFrom, Vector3.up);
            int shots0 = HaloNetGame.StatVehicleShots, fire = 0;
            t0 = Time.time; nextWrite = 0f;
            bool shotB = false;
            while (Time.time - t0 < 4f)
            {
                float k = (Time.time - t0) / 4f;
                if (Time.time >= nextWrite)
                {
                    nextWrite = Time.time + 0.1f;
                    if (k > 0.2f) fire++;
                    S.Net.Coop.SetActor(59, VehicleRecord(banshee, bansheeTag, Vector3.Lerp(bFrom, bTo, k), bRot, (bTo - bFrom) / 4f, fire, false));
                }

                if (!shotB && k > 0.5f && banshee.Gun != null && banshee.Gun.ReplayFiring)
                {
                    shotB = true;
                    r.Aim(banshee.transform.position - bRot * Vector3.forward * 14f + Vector3.up * 3f, banshee.transform.position + bRot * Vector3.forward * 10f, 55f);
                    r.Shot("netveh_banshee_firing");
                }

                yield return null;
            }

            r.Check(HaloNetGame.StatVehicleShots - shots0 >= 20 && shotB, "A replays the banshee's gunfire on its own gun (" + (HaloNetGame.StatVehicleShots - shots0) + " shots, " + (banshee.Gun != null ? banshee.Gun.Tag.Name : "no gun") + ")");

            // ---- a grunt speaks
            HaloNetPuppet speaker = HaloNetGame.PuppetAt(1);
            HaloTag line = null;
            if (speaker != null && speaker.Unit != null) line = HaloAiVoice.Sound(HaloAiVoice.DialogueFor(speaker.Unit), HaloAiVoice.NewCombatAlone);
            if (line == null) foreach (HaloTag t in map.OfClass("snd!")) if (t.Name.Contains("grunt")) { line = t; break; }
            int voices0 = HaloNetGame.StatVoicesPlayed;
            if (line != null) S.Net.Coop.AddEvent(HaloNetMissionEvent.Voice, 12 | (HaloNetU.TagIndex(line) << 12));
            yield return Seconds(1.5f);
            r.Check(line != null && HaloNetGame.StatVoicesPlayed - voices0 == 1, "the grunt's line (" + (line != null ? line.Name : "none") + ") plays once on A from its puppet");

            // ---- the host's script and AI sounds: a dialogue line on a grunt, a conversation line, an effect at a flag, the music loop (one write)
            HaloTag dialog = null, convo = null, music = null, effect = null;
            foreach (HaloTag t in map.OfClass("snd!"))
            {
                string n = t.Name.ToLowerInvariant();
                if (n.Contains("dialog") && n.Contains("a30") && !n.Contains("grunt")) { if (dialog == null) dialog = t; else if (convo == null) { convo = t; break; } }
            }

            foreach (HaloTag t in map.OfClass("lsnd")) if (t.Name.ToLowerInvariant().Contains("music")) { music = t; break; }
            foreach (HaloTag t in map.OfClass("effe")) if (t.Name.ToLowerInvariant().Contains("explosion")) { effect = t; break; }
            r.Check(dialog != null && convo != null && music != null && effect != null, "a30 tags: dialogue " + (dialog != null ? dialog.Name : "-") + ", line " + (convo != null ? convo.Name : "-") + ", music " + (music != null ? music.Name : "-") + ", effect " + (effect != null ? effect.Name : "-"));
            if (dialog == null || convo == null || music == null || effect == null) yield break;
            int imp0 = HaloNetGame.StatImpulsesPlayed, fx0 = HaloNetGame.StatEffectsPlayed, loops0 = HaloNetGame.StatLoopsStarted;
            Vector3 fxAt = to - Vector3.up * 4f + flagRot * Vector3.right * 6f;
            int fxLo, fxHi;
            HaloNetWire.PackCm2(HaloNetU.V(fxAt), out fxLo, out fxHi);
            S.Net.Coop.AddSound(HaloNetGame.PackSound(HaloNetGame.SndImpulse, HaloNetU.TagIndex(dialog), 64, HaloNetGame.TUnit), 12, 0);       // on grunt 12
            S.Net.Coop.AddSound(HaloNetGame.PackSound(HaloNetGame.SndLine, HaloNetU.TagIndex(convo), 64, HaloNetGame.TNone), 0, 0);
            S.Net.Coop.AddSound(HaloNetGame.PackSound(HaloNetGame.SndEffect, HaloNetU.TagIndex(effect), 0, HaloNetGame.TPosition), fxLo, fxHi);
            S.Net.Coop.SetLoop(0, HaloNetGame.PackLoop(HaloNetU.TagIndex(music), 1f, false, HaloNetGame.TNone), 0);
            yield return Seconds(0.4f);
            r.Aim(fxAt - fwd * 14f + Vector3.up * 3f, fxAt, 60f);
            r.Shot("netsnd_host_effect");
            yield return Seconds(1.6f);
            int imp = HaloNetGame.StatImpulsesPlayed - imp0, fx = HaloNetGame.StatEffectsPlayed - fx0, loops = HaloNetGame.StatLoopsStarted - loops0;
            r.Check(imp == 2 && fx == 1, "A hears the host's dialogue line (from the grunt's puppet) and conversation line once each, and sees its effect once (" + imp + " sounds, " + fx + " effect)");
            r.Check(loops == 1 && HaloNetGame.NetLoopsPlaying == 1, "A plays the host's music loop " + music.Name + " (" + loops + " started, " + HaloNetGame.NetLoopsPlaying + " playing)");

            // ---- A (a follower) presses a device control: it goes to the host S, nothing moves here by itself
            HaloDevice ctl = null;
            foreach (HaloDevice d in HaloDevices.All)
                if (d != null && d.Def != null && d.Def.IsControl && HaloNetGame.DeviceWire(d.PlacementIndex) >= 0) { ctl = d; break; }
            r.Check(ctl != null, "a30 has a control (" + (ctl != null ? ctl.Label + ", placement " + ctl.PlacementIndex + ", active " + ctl.isActiveAndEnabled + ", once " + ctl.ChangeOnlyOnce : "none of " + HaloDevices.All.Count + " devices") + ")");
            if (ctl == null) yield break;
            int ctlSent0 = HaloNetGame.StatControlsSent, ctlUses0 = HaloDevices.Uses, ctlSeen0 = CountControl(S, ctl.PlacementIndex);
            HaloDevices.LabUse = ctl;
            yield return Seconds(1f);
            int ctlSeen = CountControl(S, ctl.PlacementIndex) - ctlSeen0;
            r.Check(HaloNetGame.StatControlsSent - ctlSent0 == 1 && HaloDevices.Uses == ctlUses0 && ctlSeen == 1, "follower A's press of " + ctl.Label + " goes to the host S once (sent " + (HaloNetGame.StatControlsSent - ctlSent0) + ", S got " + ctlSeen + ", not used locally: " + (HaloDevices.Uses == ctlUses0) + ")");
            int heard0 = HaloNetGame.StatPressesHeard, count0 = ctl.PressCount;
            S.Net.Coop.NotePress(HaloNetGame.DeviceWire(ctl.PlacementIndex), 1, true);      // the host denied it (unpowered): K's PressCount + LastPressDenied
            yield return Seconds(1f);
            r.Check(HaloNetGame.StatPressesHeard - heard0 == 1 && ctl.PressCount - count0 == 1 && ctl.LastPressDenied, "A gets the host's denied press of " + ctl.Label + " once (PressCount +" + (ctl.PressCount - count0) + ", denied " + ctl.LastPressDenied + ": K plays the deny sound)");

            // ---- hand-over: S leaves, A hosts; C follows A
            Scripted C = Join(3, "[C]");
            yield return r.Wait(() => C.Net.Claimed, 20f, "C's claim");
            C.Play(zone, 1, start, 0);
            C.Net.CoopMission = true;
            yield return Seconds(1.5f);
            int musicIdx = HaloNetU.TagIndex(music);
            r.Check((C.Net.Coop.LoopA(0) & 0xFFFF) == musicIdx && (C.Net.Coop.LoopA(0) & (1 << 28)) != 0 && !HasSound(C, HaloNetU.TagIndex(dialog)), "late joiner C gets the running music loop but none of the earlier one-shots");
            C.Net.Coop.NewSounds.Clear();
            int promotions = HaloNetHostState.Promotions;
            S.T.Dispose();
            Scripts.Remove(S);
            yield return r.Wait(() => HaloNetHostState.Promotions > promotions, 15f, "A's promotion");
            yield return Seconds(1f);
            r.Check(HaloNetGame.Net.Coop.Writing && HaloNetGame.CampaignHost && HaloSession.Playing, "S left: A hosts a30 now (no reload)");
            HaloUnit pilot = HaloNetGame.UnitOfSerial(11), talker = HaloNetGame.UnitOfSerial(12);
            r.Check(pilot != null && !pilot.Puppet && talker != null, "the grunts are A's own units now");
            if (pilot == null) yield break;
            r.Check(banshee.SeatUnit(0, pilot), "A seats a grunt in the banshee's driver seat");
            int cFire0 = -1, cSeen = 0;
            Vector3 cPos0 = Vector3.zero;
            t0 = Time.time;
            Vector3 away = banshee.transform.position - HaloLocal.Position; away.y = 0f;
            Vector3 goal = banshee.transform.position + (away.sqrMagnitude > 1f ? away.normalized : Vector3.forward) * 80f + Vector3.up * 25f;
            int localFired = 0;
            System.Action<HaloTag, Vector3, Vector3, int> countFire = (w, mz, d, who) => { if (who == -2 && w == banshee.Gun.Tag) localFired++; };
            if (banshee.Gun != null) HaloEvents.Fired += countFire;
            while (Time.time - t0 < 4f)
            {
                banshee.SetAiFlight(1f, goal, Vector3.zero, Time.time - t0 > 1f);
                HaloNetActor seen = default(HaloNetActor);
                for (int p = 0; p < HaloNetCoop.MaxActors; p++) { HaloNetActor a = C.Net.Coop.Actor(p); if (a.IsVehicle && a.VehicleId == bansheeId) seen = a; }
                if (seen.Serial != 0) { if (cFire0 < 0) { cFire0 = seen.FireCounter; cPos0 = HaloNetU.V(seen.Position); } cSeen++; }
                yield return null;
            }

            HaloNetActor last = default(HaloNetActor), pilotRec = default(HaloNetActor);
            for (int p = 0; p < HaloNetCoop.MaxActors; p++)
            {
                HaloNetActor a = C.Net.Coop.Actor(p);
                if (a.IsVehicle && a.VehicleId == bansheeId) last = a;
                if (!a.IsVehicle && a.Serial == 11) pilotRec = a;
            }

            float moved = Vector3.Distance(HaloNetU.V(last.Position), cPos0);
            int fired = (last.FireCounter - cFire0) & 0xFF;
            r.Check(last.Serial != 0 && moved > 10f, "C reads the new host's AI banshee flying (" + moved.ToString("0") + " m in 4 s)");
            if (banshee.Gun != null) HaloEvents.Fired -= countFire;
            r.Check(fired > 0 && fired >= localFired - 2, "C reads its gunfire (fire counter +" + fired + " for " + localFired + " shots fired on A)");
            r.Check(pilotRec.SeatVehicle == bansheeId && pilotRec.Seat == 0, "C reads the pilot seated in the banshee (vehicle " + pilotRec.SeatVehicle + ", seat " + pilotRec.Seat + ")");
            int voicesC = 0;
            if (line != null) HaloNetGame.NoteVoice(talker, line);
            yield return Seconds(1f);
            foreach (HaloNetMissionEvent e in C.Net.Coop.NewEvents) if (e.Kind == HaloNetMissionEvent.Voice && (e.Value & 0xFFF) == 12) voicesC++;
            r.Check(voicesC == 1, "C gets the new host's voice line once");

            // ---- the new host keeps the music it knew; its script's dialogue / effect / loop stop reach C once (the glue's Note* calls)
            r.Check(HaloNetGame.NetLoopsPlaying == 1 && FindLoop(HaloNetGame.Net.Coop, musicIdx) >= 0 && FindLoop(C.Net.Coop, musicIdx) >= 0, "after the hand-over A still plays the music and keeps it in its loop table (C still reads it)");
            C.Net.Coop.NewSounds.Clear();
            HaloNetGame.NoteSound(dialog, talker != null ? talker.gameObject : null, 1f);
            HaloNetGame.NoteEffectAt(effect, fxAt, 90f);
            HaloNetGame.NoteLoopStop(music);
            yield return Seconds(1f);
            int cDialog = CountSound(C, HaloNetU.TagIndex(dialog)), cFx = CountSound(C, HaloNetU.TagIndex(effect));
            r.Check(cDialog == 1 && cFx == 1, "C gets the new host's dialogue line and effect once each (" + cDialog + ", " + cFx + ")");
            r.Check(FindLoop(C.Net.Coop, musicIdx) < 0 && HaloNetGame.NetLoopsPlaying == 0, "the new host's sound_looping_stop ends the music for C and on A");

            // ---- follower C presses the control: host A runs K's Use once as C's press, the device moves, C reads it back in the device bytes
            ctl.Power = ctl.PowerTarget = 1f;      // (a30 powers the light bridge control from its script: done here by hand)
            int applied0 = HaloNetGame.StatControlsApplied, usesA0 = HaloDevices.Uses;
            float desired0 = ctl.Desired, cDev0 = C.Net.Coop.Device(HaloNetGame.DeviceWire(ctl.PlacementIndex));
            C.Net.Coop.NewPresses.Clear();
            C.Net.NoteControl(ctl.PlacementIndex);
            yield return Seconds(3f);
            float cDev = C.Net.Coop.Device(HaloNetGame.DeviceWire(ctl.PlacementIndex));
            r.Check(HaloNetGame.StatControlsApplied - applied0 == 1 && HaloDevices.Uses - usesA0 == 1, "host A applies C's press of " + ctl.Label + " once (K's Use as co-op player " + C.Net.MySlot + ")");
            r.Check(Mathf.Abs(ctl.Desired - desired0) > 0.5f && Mathf.Abs(cDev - cDev0) > 0.2f, "the control moves on A (" + desired0.ToString("0.##") + " -> " + ctl.Desired.ToString("0.##") + ") and C sees it move in the device bytes (" + cDev0.ToString("0.##") + " -> " + cDev.ToString("0.##") + ")");
            int cPress = 0; bool cDenied = true;
            foreach (int e in C.Net.Coop.NewPresses) if ((e & 0xFF) == HaloNetGame.DeviceWire(ctl.PlacementIndex)) { cPress += (e >> 8) & 7; cDenied = (e & (1 << 11)) != 0; }
            r.Check(cPress == 1 && !cDenied, "C reads the host's accepted press of " + ctl.Label + " once in the press table (" + cPress + ")");
            r.Aim(banshee.transform.position - banshee.transform.forward * 14f + Vector3.up * 3f, banshee.transform.position, 55f);
            r.Shot("netveh_host_banshee");
        }

        static int CountSound(Scripted c, int tag)
        {
            int n = 0;
            var l = c.Net.Coop.NewSounds;
            for (int i = 0; i + 2 < l.Count; i += 3) if (((l[i] >> 4) & 0xFFFF) == tag) n++;
            return n;
        }

        static bool HasSound(Scripted c, int tag) { return CountSound(c, tag) > 0; }

        static int CountControl(Scripted s, int placement)
        {
            int n = 0;
            foreach (HaloNetEvent e in s.Seen) if (e.Kind == HaloNetEventKind.UseControl && e.Value == placement && e.Slot == HaloNetGame.Net.MySlot) n++;
            return n;
        }

        static int FindLoop(HaloNetCoop coop, int tag)
        {
            for (int i = 0; i < HaloNetCoop.Loops; i++) { int a = coop.LoopA(i); if ((a & (1 << 28)) != 0 && (a & 0xFFFF) == tag) return i; }
            return -1;
        }

        /// <summary>The floor under a point (from 3 m above, 10 m down; hit boxes are not floors).</summary>
        static Vector3 HaloSpawnsGround(Vector3 p)
        {
            RaycastHit h;
            if (Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out h, 10f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore)) return h.point;
            return p;
        }

        /// <summary>Lab only: draws B's hit boxes as see-through shapes for the picture.</summary>
        static void Visualise(HaloRemoteView v, bool on)
        {
            if (v == null) return;
            foreach (Collider c in v.GetComponentsInChildren<Collider>(true))
            {
                if (c.GetComponent<HaloNetHitbox>() == null) continue;
                Transform vis = c.transform.Find("lab vis");
                if (!on) { if (vis != null) Object.Destroy(vis.gameObject); continue; }
                if (vis != null) continue;
                GameObject g;
                var cap = c as CapsuleCollider;
                if (cap != null)
                {
                    g = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    g.transform.localScale = new Vector3(cap.radius * 2f, cap.height * 0.5f, cap.radius * 2f);
                }
                else
                {
                    g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    float d = ((SphereCollider)c).radius * 2f;
                    g.transform.localScale = new Vector3(d, d, d);
                }

                g.name = "lab vis";
                Object.Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(c.transform, false);
                var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.color = c.GetComponent<HaloNetHitbox>().IsHead ? new Color(1f, 0.3f, 0.2f) : new Color(0.3f, 0.8f, 1f);
                g.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }
    }
}

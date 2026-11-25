using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>A weapon another player dropped (by hand or by dying): the Combat module places it as a pickup with this id.</summary>
    public struct HaloNetDrop
    {
        public int Id, Slot, Rounds, Reserve;
        public float Battery;
        public HaloTag Weapon;
        public Vector3 Position, Velocity;
    }

    /// <summary>
    /// What the others did, as read from their records (HaloNet.Events), and the players of my match as drawn (<see cref="HaloRemoteView"/>: their weapons, hit
    /// boxes, shots, grenades, melee, camo, names). Who decides what (the Black Ops 2 Dreamport's rule): the SHOOTER measures a hit against the victim's hit
    /// boxes and publishes it (<see cref="PublishHit"/>); the VICTIM applies it once to itself (HaloCombat.TakeHit); the victim announces its death with the
    /// killer and every client raises the kill feed from that (HaloMatchControl.ReportKill), the killer's client credits itself (the Match module).
    /// </summary>
    public static partial class HaloNetGame
    {
        /// <summary>Optional (Combat): plays a remote player's shot the way a local shot looks, without damage - (weapon, muzzle, direction, shooter slot, muzzle transform or null). Null = Net's own replay from the tags.</summary>
        public static Action<HaloTag, Vector3, Vector3, int, Transform> ReplayShot;

        /// <summary>Optional (Combat): flies a remote player's grenade the way a local one flies, without damage - (grenade proj / eqip tag, position, velocity, thrower slot). Null = Net's own replay.</summary>
        public static Action<HaloTag, Vector3, Vector3, int> ReplayThrow;

        /// <summary>Somebody else took a pickup (scenario NetEquipment index) or a dropped weapon (its drop id): (item, slot of the taker).</summary>
        public static event Action<int, int> ItemTaken;

        /// <summary>Somebody else dropped a weapon (place it as a pickup with <see cref="HaloNetDrop.Id"/>).</summary>
        public static event Action<HaloNetDrop> RemoteDropped;

        /// <summary>The remote players are drawn (their weapons; off = only the hit boxes): a cost switch.</summary>
        public static bool DrawWeapons = true;

        /// <summary>Counters for the lab and the log: shots of others played, grenades replayed, hits of mine published (and their points), hits on me applied (and their points), deaths of others seen.</summary>
        public static int StatShots, StatThrows, StatHitsPublished, StatHitsApplied, StatDeathsSeen;
        public static float StatPointsPublished, StatPointsApplied;

        private static readonly HaloRemoteView[] Views = new HaloRemoteView[HaloNetWire.MaxSlots];
        private static readonly List<HaloRemoteView> ViewList = new List<HaloRemoteView>(16);
        private static Transform _viewRoot;

        /// <summary>The drawn view of a remote player in my match (null when not in my match).</summary>
        public static HaloRemoteView ViewOf(int slot) { return slot >= 0 && slot < Views.Length ? Views[slot] : null; }

        /// <summary>A collider of a remote player's hit boxes (and whose).</summary>
        public static bool IsRemoteHitbox(Collider c, out HaloNetPlayer player)
        {
            player = null;
            if (c == null) return false;
            HaloNetHitbox hb = c.GetComponent<HaloNetHitbox>();
            if (hb == null || hb.Player == null) return false;
            player = hb.Player;
            return true;
        }

        // ------------------------------------------------------------------ my hits on others

        /// <summary>A hit box was hit by this client (HaloNetHitbox.TakeHit): published for the victim's own client. The shown health dips at once.</summary>
        internal static void PublishHit(HaloNetHitbox box, HaloHit hit)
        {
            if (!Up || box == null || box.Player == null || !Net.Claimed) return;
            HaloNetPlayer p = box.Player;
            if (!p.InMatch || !p.Alive || !(hit.Amount > 0f)) return;
            if (hit.SourcePlayer >= 0 && hit.SourcePlayer != HaloLocal.NetId) return;          // another remote's damage is their own client's business
            bool ai = hit.SourcePlayer == -2;      // campaign co-op: the host's AI hit a remote player
            if (!ai && Net.TeamGame && !Net.FriendlyFire && (p.Team & 1) == (MyTeam & 1)) return;      // never a team mate (unless friendly fire)
            bool head = hit.Headshot || box.IsHead;
            Net.NoteHit(p.Slot, hit.Amount, HaloNetU.TagIndex(hit.Cause), HaloNetU.TagIndex(hit.Effect), hit.Melee, head, hit.Explosion, hit.BackHit, hit.Instant, ai);
            StatHitsPublished++;
            StatPointsPublished += hit.Amount;
            HaloRemoteView v = ViewOf(p.Slot);
            if (v != null) v.FeedbackHit(hit);
        }

        // ------------------------------------------------------------------ the events

        private static void Consume()
        {
            List<HaloNetEvent> events = Net.Events;
            for (int i = 0; i < events.Count; i++)
            {
                HaloNetEvent ev = events[i];
                try { Apply(in ev); }
                catch (Exception e) { HaloLog.Warn("Net: applying " + ev.Kind + " of slot " + ev.Slot + " failed: " + e.Message); }
            }
        }

        private static void Apply(in HaloNetEvent ev)
        {
            switch (ev.Kind)
            {
                case HaloNetEventKind.Joined: CreateView(ev.Slot); break;
                case HaloNetEventKind.Left: RemoveView(ev.Slot); break;
                case HaloNetEventKind.Shot:
                {
                    HaloRemoteView v = ViewOf(ev.Slot);
                    HaloNetPlayer p = Find(ev.Slot);
                    if (p != null) p.LastShotAt = Time.time;
                    if (v != null) v.OnShot(in ev);
                    StatShots += ev.Count;
                    break;
                }
                case HaloNetEventKind.Throw:
                {
                    HaloRemoteView v = ViewOf(ev.Slot);
                    if (v != null) v.OnThrow(in ev);
                    StatThrows++;
                    break;
                }
                case HaloNetEventKind.Melee:
                {
                    HaloRemoteView v = ViewOf(ev.Slot);
                    if (v != null) v.OnMelee();
                    break;
                }
                case HaloNetEventKind.Death: OnRemoteDeath(in ev); break;
                case HaloNetEventKind.HitMe: ApplyHit(in ev); break;
                case HaloNetEventKind.HitActor: ApplyActorHit(in ev); break;
                case HaloNetEventKind.UseControl: ApplyControlUse(in ev); break;
                case HaloNetEventKind.Objective:
                {
                    Action<int, int, int> h = GameEvent;
                    if (h != null)
                    {
                        try { h(ev.Slot, ev.Value, ev.Other); }
                        catch (Exception e) { HaloLog.Error("Net: a GameEvent listener failed: " + e); }
                    }

                    break;
                }
                case HaloNetEventKind.Pickup:
                {
                    int id = ev.Value;
                    HaloGame.Guard("remote pickup", () => HaloPickups.ApplyRemoteTaken(id));
                    Action<int, int> h = ItemTaken;
                    if (h != null) h(ev.Value, ev.Slot);
                    break;
                }
                case HaloNetEventKind.Drop:
                {
                    var d = new HaloNetDrop
                    {
                        Id = ev.Other, Slot = ev.Slot, Rounds = ev.Value, Reserve = ev.Count, Battery = ev.Amount, Weapon = HaloNetU.TagOf(ev.Tag), Position = HaloNetU.V(ev.Pos),
                        Velocity = HaloNetU.V(ev.Vel),
                    };
                    if (d.Weapon == null) break;
                    HaloGame.Guard("remote drop", () => HaloPickups.RemoteDrop(d.Id, d.Weapon, d.Rounds, d.Reserve, d.Position, d.Velocity, d.Slot));
                    Action<HaloNetDrop> h = RemoteDropped;
                    if (h != null) h(d);
                    break;
                }
                case HaloNetEventKind.TeamChosen:
                    HaloLog.Info("Net: my side is " + HaloTeams.Name(ev.Value));
                    break;
            }
        }

        private static void OnRemoteDeath(in HaloNetEvent ev)
        {
            StatDeathsSeen++;
            HaloRemoteView v = ViewOf(ev.Slot);
            if (v != null) v.OnDeath();
            HaloNetPlayer p = Find(ev.Slot);
            if (p != null) p.Alive = false;
            var k = new HaloKill
            {
                Killer = ev.Other == HaloNetWire.NoSlot ? -1 : ev.Other,
                Victim = ev.Slot,
                Cause = HaloNetU.TagOf(ev.Tag),
                Melee = ev.Melee, Headshot = ev.Head, Splatter = ev.Splatter, Grenade = ev.Grenade, Suicide = ev.Suicide, Betrayal = ev.Betrayal,
            };
            HaloLog.Info("Net: " + NameOf(ev.Slot) + " died" + (k.Killer >= 0 ? " - killed by " + NameOf(k.Killer) : k.Suicide ? " (suicide)" : "") + (k.Cause != null ? " with " + k.Cause.Name : "")
                         + (k.Headshot ? ", headshot" : "") + (k.Melee ? ", melee" : ""));
            HaloGame.Guard("report kill", () => HaloMatchControl.ReportKill(k));
        }

        /// <summary>A hit another player measured against my body: applied to me once (HaloNet already dropped it if it came from my side, while I was dead, or twice).</summary>
        private static void ApplyHit(in HaloNetEvent ev)
        {
            Vector3 me = Body.Valid ? Body.Chest : HaloLocal.Position + Vector3.up * 1.2f;
            HaloRemoteView v = ViewOf(ev.Slot);
            Vector3 from = v != null && v.Body.Valid ? v.Body.HeadPos : HaloNetU.V(ev.Pos);
            Vector3 dir = me - from;
            var hit = new HaloHit
            {
                Amount = ev.Amount,
                Effect = HaloNetU.TagOf(ev.Value),
                Cause = HaloNetU.TagOf(ev.Tag),
                SourcePlayer = ev.Ai ? -2 : ev.Slot,
                Point = me,
                Direction = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward,
                Melee = ev.Melee, Headshot = ev.Head, Explosion = ev.Explosion, BackHit = ev.Back, Instant = ev.Instant,
            };
            StatHitsApplied++;
            StatPointsApplied += ev.Amount;
            HaloGame.Guard("take hit", () => HaloCombat.TakeHit(hit));
        }

        // ------------------------------------------------------------------ the views of the players in my match

        private static void CreateView(int slot)
        {
            HaloNet.Remote rec = Net.RemoteAt(slot);
            HaloNetPlayer p = Find(slot);
            if (rec == null || p == null || Views[slot] != null) return;
            if (_viewRoot == null)
            {
                var root = new GameObject("Halo Net Remotes");
                if (_game != null) root.transform.SetParent(_game.transform, false);
                _viewRoot = root.transform;
            }

            var go = new GameObject("Remote player " + slot);
            go.transform.SetParent(_viewRoot, false);
            HaloRemoteView v = go.AddComponent<HaloRemoteView>();
            v.Setup(rec, p);
            Views[slot] = v;
            ViewList.Add(v);
            HaloLog.Info("Net: " + p.Name + " (player " + p.PlayerId + ", slot " + slot + ") joined my match - " + ViewList.Count + " other human(s) here");
            Action<HaloNetPlayer> h = PlayerJoined;
            if (h != null) { try { h(p); } catch (Exception e) { HaloLog.Error("Net: a PlayerJoined listener failed: " + e); } }
        }

        private static void RemoveView(int slot)
        {
            HaloRemoteView v = ViewOf(slot);
            HaloNetPlayer p = v != null ? v.Player : Find(slot);
            if (v != null)
            {
                Views[slot] = null;
                ViewList.Remove(v);
                v.Retire();
            }

            // (the Match module listens to PlayerLeft: it keeps their points for their team and drops them from its roster)
            if (p != null)
            {
                p.InMatch = false;
                HaloLog.Info("Net: " + p.Name + " (slot " + slot + ") left my match");
                Action<HaloNetPlayer> h = PlayerLeft;
                if (h != null) { try { h(p); } catch (Exception e) { HaloLog.Error("Net: a PlayerLeft listener failed: " + e); } }
            }
        }

        private static void ClearViews()
        {
            for (int k = 0; k < Views.Length; k++)
            {
                if (Views[k] == null) continue;
                try { Views[k].Retire(); } catch (Exception) { /* going anyway */ }
                Views[k] = null;
            }

            ViewList.Clear();
        }

        private static void LateViews()
        {
            if (ViewList.Count == 0) return;
            double now = Net.Now;
            float dt = Time.deltaTime;
            for (int i = 0; i < ViewList.Count; i++) { ViewList[i].Refresh(now, dt); ReportDesk(ViewList[i]); }
            Physics.SyncTransforms();      // the hit boxes moved: this frame's shots must find them where they are drawn
        }
    }
}

using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A client's copy of one of the host's AI units (campaign co-op). The Characters module's biped when it can make one (HaloBipeds.Spawn: Net moves it
    /// and sets its control fields - look, aim, move, crouch, fire - for its animation; the AI never drives it here), else a stand-in capsule. It follows
    /// the host's record 0.1 s behind (interpolated between the last two records), replays its shots, dies when the host says so. A hit on it is sent
    /// to the host (<see cref="HaloNetGame.PuppetHit(HaloNetPuppet, HaloHit)"/>); nothing is applied here.
    /// </summary>
    public sealed class HaloNetPuppet : MonoBehaviour, IHaloDamageable, IHaloHitInfo
    {
        public const float Delay = 0.1f;

        public int Serial { get; private set; }
        public HaloUnit Unit { get; private set; }
        public bool Alive { get; private set; } = true;
        public HaloNetActor Last { get { return _cur; } }
        /// <summary>Shots replayed (lab).</summary>
        public int ShotsPlayed;
        /// <summary>Records with a body state / camouflage applied, and which ones (bit = state 1-3, 16 = camouflage hidden) - lab.</summary>
        public int BodyStates, StatesSeen;

        private HaloNetActor _prev, _cur;
        private float _prevAt, _curAt;
        private bool _have, _standIn;
        private int _fire = -1, _melee = -1, _throw = -1, _weapon = -1;
        private Collider _standInCollider;

        public static HaloNetPuppet Create(in HaloNetActor a, Transform parent)
        {
            HaloTag look = HaloNetU.TagOf(a.VariantTag) ?? HaloNetU.TagOf(a.UnitTag);
            Vector3 pos = HaloNetU.V(a.Position);
            HaloUnit u = null;
            if (look != null)
            {
                try { u = HaloBipeds.Spawn(look, pos, a.BodyYaw, (HaloTeam)a.Team); }
                catch (System.Exception e) { HaloLog.Warn("Net: a co-op puppet of " + look.Name + " failed: " + e.Message); u = null; }
            }

            GameObject go;
            bool standIn = u == null;
            if (u != null) go = u.gameObject;
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "Co-op stand-in " + (look != null ? System.IO.Path.GetFileName(look.Name) : "unit") + " #" + a.Serial;
                go.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
                go.layer = HaloLayers.Hitbox;
                var r = go.GetComponent<Renderer>();
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (r != null && sh != null)
                {
                    var m = new Material(sh);
                    Color col = a.Team == (int)HaloTeam.Covenant ? new Color(0.55f, 0.25f, 0.7f) : a.Team == (int)HaloTeam.Flood ? new Color(0.7f, 0.65f, 0.3f) : new Color(0.35f, 0.5f, 0.3f);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                    r.sharedMaterial = m;
                }
            }

            if (parent != null && standIn) go.transform.SetParent(parent, true);
            HaloNetPuppet p = go.AddComponent<HaloNetPuppet>();
            p.Unit = u;
            if (u != null)
            {
                u.Puppet = true;
                HaloTag w = HaloNetU.TagOf(a.WeaponTag);
                if (w != null) u.SetWeapon(w);
            }
            p.Serial = a.Serial;
            p._standIn = standIn;
            p._standInCollider = standIn ? go.GetComponent<Collider>() : null;
            go.transform.SetPositionAndRotation(standIn ? pos + Vector3.up : pos, Quaternion.Euler(0f, a.BodyYaw, 0f));
            return p;
        }

        /// <summary>The host's record of this unit as read this frame (a new record when anything changed).</summary>
        public void Take(in HaloNetActor a)
        {
            float now = Time.time;
            bool changed = !_have || a.Position.X != _cur.Position.X || a.Position.Y != _cur.Position.Y || a.Position.Z != _cur.Position.Z || a.BodyYaw != _cur.BodyYaw
                           || a.FireCounter != _cur.FireCounter || a.Alive != _cur.Alive || a.AimYaw != _cur.AimYaw || a.AimPitch != _cur.AimPitch
                           || a.MeleeCounter != _cur.MeleeCounter || a.GrenadeCounter != _cur.GrenadeCounter || a.WeaponTag != _cur.WeaponTag || a.Crouch != _cur.Crouch
                           || a.SeatVehicle != _cur.SeatVehicle || a.Seat != _cur.Seat || a.Exiting != _cur.Exiting
                           || a.BodyState != _cur.BodyState || a.CamoHidden != _cur.CamoHidden || a.Shield != _cur.Shield || a.Body != _cur.Body;
            if (!changed) return;
            if (!_have) { _prev = a; _prevAt = now - 0.1f; _fire = a.FireCounter; _melee = a.MeleeCounter; _throw = a.GrenadeCounter; _weapon = a.WeaponTag; }
            else { _prev = _cur; _prevAt = _curAt; }
            _cur = a;
            _curAt = now;
            _have = true;
            int shots = (a.FireCounter - _fire) & 0xFF;
            _fire = a.FireCounter;
            if (shots > 0 && shots < 64 && a.Alive) Fire(in a, shots);
            if (Unit != null && a.Alive)
            {
                if (a.WeaponTag != _weapon) { _weapon = a.WeaponTag; HaloTag w = HaloNetU.TagOf(a.WeaponTag); if (w != null) Unit.SetWeapon(w); }
                if (((a.MeleeCounter - _melee) & 0xF) != 0) Unit.PlayMelee();
                if (((a.GrenadeCounter - _throw) & 0xF) != 0) Unit.PlayThrow();
                try { Unit.ApplyNetBody(a.BodyState, a.CamoHidden, a.FeignHard, a.FeignDir, a.FeignRegion); }
                catch (System.Exception e) { HaloLog.Warn("Net: a co-op puppet's body state failed: " + e.Message); }
                if (a.BodyState != 0 || a.CamoHidden) { BodyStates++; if (a.BodyState >= 0 && a.BodyState < 4) StatesSeen |= 1 << a.BodyState; if (a.CamoHidden) StatesSeen |= 16; }
            }

            _melee = a.MeleeCounter;
            _throw = a.GrenadeCounter;
            if (!a.Alive && Alive) Die();
        }

        private void Update()
        {
            if (!_have) return;
            float span = Mathf.Max(0.02f, _curAt - _prevAt);
            float u = Mathf.Clamp01((Time.time - Delay - _prevAt) / span);
            Vector3 pos = Vector3.Lerp(HaloNetU.V(_prev.Position), HaloNetU.V(_cur.Position), u);
            float yaw = Mathf.LerpAngle(_prev.BodyYaw, _cur.BodyYaw, u);
            if (!Alive) return;
            if (Unit != null && SyncSeat()) return;      // seated (the seat carries it) or playing its exit clip: the Characters module moves it
            transform.SetPositionAndRotation(_standIn ? pos + Vector3.up : pos, Quaternion.Euler(0f, yaw, 0f));
            if (Unit != null)
            {
                Vector3 eye = Unit.Eye;
                Vector3 aim = AimDir();
                Unit.LookPoint = eye + aim * 10f;
                Unit.AimPoint = eye + aim * 10f;
                Unit.MoveVelocity = HaloNetU.V(_cur.Velocity);
                Unit.Crouching = _cur.Crouch;
                Unit.Firing = _cur.Firing;
                Unit.Body = _cur.Body;
                Unit.Shield = _cur.Shield;
            }
        }

        /// <summary>Seats / unseats the unit as the host's record says (V's SeatUnit plays C's enter clip, UnseatUnit the exit clip). True while the
        /// seat or the exit clip owns the unit's transform.</summary>
        private bool SyncSeat()
        {
            Transform seatT = Unit.Seat;
            HaloVehicle cur = seatT != null ? seatT.GetComponentInParent<HaloVehicle>() : null;
            if (_cur.SeatVehicle >= 0)
            {
                HaloVehicle v = cur != null && cur.Id == _cur.SeatVehicle ? cur : HaloVehicles.Find(_cur.SeatVehicle);
                if (v != null && (cur != v || v.SeatOfUnit(Unit) != _cur.Seat))
                {
                    if (cur != null) cur.UnseatUnit(Unit);
                    if (v.SeatUnit(_cur.Seat, Unit))
                    {
                        Seatings++;
                        if (Seatings == 1) HaloLog.Info("Net: a co-op puppet (serial " + Serial + ") sits in " + v.name + " seat " + _cur.Seat + " as the host says");
                    }
                }
            }
            else if (cur != null && !Unit.Exiting)
            {
                cur.UnseatUnit(Unit);
                Exits++;
                HaloLog.Info("Net: a co-op puppet (serial " + Serial + ") gets out of " + cur.name + " (exit clip) as the host says");
            }

            return Unit.Seat != null || Unit.Exiting;
        }

        /// <summary>Times this puppet was seated / got out (lab).</summary>
        public int Seatings, Exits;

        private Vector3 AimDir()
        {
            return Quaternion.Euler(-_cur.AimPitch, _cur.AimYaw, 0f) * Vector3.forward;
        }

        private void Fire(in HaloNetActor a, int shots)
        {
            ShotsPlayed += shots;
            HaloTag weapon = HaloNetU.TagOf(a.WeaponTag);
            Vector3 eye = Unit != null ? Unit.Eye : transform.position + Vector3.up * 0.6f;
            Vector3 dir = AimDir();
            Vector3 muzzle = eye + dir * 0.5f;
            HaloEvents.RaiseFired(weapon, muzzle, dir, -2);
            if (Unit != null)
            {
                try { for (int i = 0; i < shots && i < 4; i++) Unit.PlayFire(); }      // the Characters module's own effect, recoil and a visual-only projectile
                catch (System.Exception e) { HaloLog.Warn("Net: a co-op puppet's shot failed: " + e.Message); }
            }
            else if (weapon != null && HaloCombat.Catalog != null)
            {
                try { HaloCombat.ReplayShot(weapon, muzzle, dir, -2, null); }
                catch (System.Exception e) { HaloLog.Warn("Net: a co-op shot replay failed: " + e.Message); }
            }
        }

        private void Die()
        {
            Alive = false;
            if (Unit != null)
            {
                try { Unit.Kill(); }
                catch (System.Exception e) { HaloLog.Warn("Net: a co-op puppet's death failed: " + e.Message); }
            }
            else
            {
                transform.rotation = Quaternion.Euler(90f, _cur.BodyYaw, 0f);      // the stand-in falls over
                transform.position = HaloNetU.V(_cur.Position) + Vector3.up * 0.35f;
                if (_standInCollider != null) _standInCollider.enabled = false;
            }
        }

        public void TakeHit(HaloHit hit) { HaloNetGame.PuppetHit(this, hit); }

        public int HitSlot { get { return -1; } }
        public bool HitIsHead(Collider c) { return false; }
        public Vector3 HitFacing { get { return transform.forward; } }
        public int HitMaterial(Vector3 point) { return _cur.Shield > 0.01f ? 22 : 21; }
        public bool HitAlive { get { return Alive; } }

        public void Retire()
        {
            if (this == null) return;
            Destroy(gameObject);
        }
    }
}

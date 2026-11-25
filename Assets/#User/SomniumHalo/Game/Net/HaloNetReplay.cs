using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Net's own replay of other players' shots and grenades, used while the Combat module has not set <see cref="HaloNetGame.ReplayShot"/> /
    /// <see cref="HaloNetGame.ReplayThrow"/>: the look and sound only, read from the game's tags - no damage (the shooter's client measured that).
    /// A shot: the trigger's firing effect at the muzzle (the effect carries the fire sound), then the projectile's material response effect where its line
    /// meets the world or a player (HaloWorld.SurfaceMaterial; a player is cyborg armour, or the cyborg energy shield while their shields are up).
    /// A grenade: the projectile's model flying with gravity, bouncing off the world, and its detonation effect when its timer runs out (frag: the timer starts
    /// at the first bounce when the tag says so; plasma: it sticks where it lands).
    /// </summary>
    public static class HaloNetReplay
    {
        public const int CyborgArmor = 21, CyborgShield = 22;

        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static readonly System.Random Rng = new System.Random(7);
        private static readonly Dictionary<HaloTag, HaloWeaponDef> Weapons = new Dictionary<HaloTag, HaloWeaponDef>();
        private static readonly Dictionary<HaloTag, HaloProjectileDef> Projectiles = new Dictionary<HaloTag, HaloProjectileDef>();
        private static HaloMap _defsMap;
        private static HaloCombatCatalog _catalog;

        private static void CheckMap()
        {
            HaloMap m = HaloSession.Map;
            if (m == _defsMap) return;
            _defsMap = m;
            Weapons.Clear();
            Projectiles.Clear();
            _catalog = null;
        }

        public static HaloWeaponDef WeaponDef(HaloTag t)
        {
            if (t == null || !t.Is("weap")) return null;
            CheckMap();
            HaloWeaponDef d;
            if (Weapons.TryGetValue(t, out d)) return d;
            try { d = new HaloWeaponDef(t); }
            catch (System.Exception e) { HaloLog.Warn("Net: reading " + t.Name + " failed: " + e.Message); d = null; }
            Weapons[t] = d;
            return d;
        }

        public static HaloProjectileDef ProjectileDef(HaloTag t)
        {
            if (t == null || !t.Is("proj")) return null;
            CheckMap();
            HaloProjectileDef d;
            if (Projectiles.TryGetValue(t, out d)) return d;
            try { d = new HaloProjectileDef(t); }
            catch (System.Exception e) { HaloLog.Warn("Net: reading " + t.Name + " failed: " + e.Message); d = null; }
            Projectiles[t] = d;
            return d;
        }

        /// <summary>The grenade projectile behind a thrown tag (a proj as it is, or the grenade equipment's projectile from the globals).</summary>
        public static HaloProjectileDef GrenadeProjectile(HaloTag t)
        {
            if (t == null) return null;
            if (t.Is("proj")) return ProjectileDef(t);
            CheckMap();
            try
            {
                if (_catalog == null && HaloSession.Map != null) _catalog = new HaloCombatCatalog(HaloSession.Map);
                if (_catalog != null)
                    for (int i = 0; i < _catalog.Grenades.Count; i++)
                        if (_catalog.Grenades[i].Equipment == t) return ProjectileDef(_catalog.Grenades[i].Projectile);
            }
            catch (System.Exception e) { HaloLog.Warn("Net: the grenade of " + t.Name + ": " + e.Message); }
            return null;
        }

        private static void PlayTag(HaloTag t, Vector3 pos, Quaternion rot, Transform attach)
        {
            if (t == null) return;
            if (t.Is("effe")) HaloFx.Play(t, pos, rot, attach);
            else if (t.Is("snd!") || t.Is("lsnd")) HaloAudio.Play(t, pos);
        }

        // ------------------------------------------------------------------ a shot

        public static void Shot(HaloWeaponDef def, HaloTag weapon, Vector3 origin, Vector3 dir, Transform muzzle, int count, Transform shooter)
        {
            if (def == null) def = WeaponDef(weapon);
            if (def == null || def.Triggers.Count == 0) return;
            HaloWeaponDef.Trigger trig = def.Triggers[0];
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            HaloWeaponDef.FiringEffect fe = trig.EffectFor(0, Rng);
            if (fe != null) PlayTag(fe.Firing, origin, rot, muzzle);

            // where the round went: the first thing on its line (not the shooter's own hit boxes)
            HaloProjectileDef proj = ProjectileDef(trig.Projectile);
            if (proj == null) return;
            int n = Physics.RaycastNonAlloc(origin, dir, Hits, 300f, HaloLayers.ShotMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bi = -1;
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].distance >= best) continue;
                HaloNetHitbox hb = Hits[i].collider.GetComponent<HaloNetHitbox>();
                if (hb != null && shooter != null && hb.transform.IsChildOf(shooter)) continue;
                best = Hits[i].distance;
                bi = i;
            }

            if (bi < 0) return;
            RaycastHit h = Hits[bi];
            int material;
            HaloNetHitbox box = h.collider.GetComponent<HaloNetHitbox>();
            if (box != null) material = box.Player != null && box.Player.Shield > 0.01f ? CyborgShield : CyborgArmor;
            else if (h.collider.gameObject.layer == HaloLayers.Hitbox) material = HaloLocal.Shield > 0.01f ? CyborgShield : CyborgArmor;     // my own body
            else material = HaloWorld.SurfaceMaterial(h);
            HaloProjectileDef.MaterialResponse mr = material >= 0 ? proj.For(material) : null;
            HaloTag effect = mr != null ? (mr.DefaultEffect ?? mr.DetonationEffect) : null;
            if (effect == null) effect = proj.DetonationEffect;
            PlayTag(effect, h.point, Quaternion.LookRotation(h.normal, Vector3.up), null);
        }

        // ------------------------------------------------------------------ a grenade

        public static void Throw(HaloTag grenade, Vector3 pos, Vector3 vel, HaloRemoteView thrower)
        {
            HaloProjectileDef proj = GrenadeProjectile(grenade);
            var go = new GameObject("Replayed grenade " + (grenade != null ? System.IO.Path.GetFileName(grenade.Name) : "?"));
            if (HaloSession.WorldRoot != null) go.transform.SetParent(HaloSession.WorldRoot, true);
            go.transform.position = pos;
            HaloModelBuilder.Built b = proj != null ? HaloModelBuilder.Build(proj.Model, go.transform, new HaloModelBuilder.Options { Lod = 2, CastShadows = false }) : null;
            if (b == null)
            {
                GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(ball.GetComponent<Collider>());
                ball.transform.SetParent(go.transform, false);
                ball.transform.localScale = Vector3.one * 0.08f;
            }
            else
            {
                foreach (Collider c in b.Root.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
            }

            var flight = go.AddComponent<HaloNetGrenade>();
            flight.Begin(proj, vel, thrower);
        }
    }

    /// <summary>A replayed grenade in flight (look only): gravity, bounces, the timer, the detonation effect.</summary>
    public sealed class HaloNetGrenade : MonoBehaviour
    {
        private HaloProjectileDef _proj;
        private Rigidbody _body;
        private float _timer = -1f, _born, _maxLife = 8f;
        private bool _stuck, _done;

        public void Begin(HaloProjectileDef proj, Vector3 vel, HaloRemoteView thrower)
        {
            _proj = proj;
            _born = Time.time;
            gameObject.layer = HaloLayers.Environment;
            var col = gameObject.AddComponent<SphereCollider>();
            col.radius = proj != null && proj.CollisionRadius > 0f ? proj.CollisionRadius * HaloSpace.Unit : 0.05f;
            _body = gameObject.AddComponent<Rigidbody>();
            _body.mass = 0.4f;
            _body.linearDamping = 0.05f;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.linearVelocity = vel;
            _body.angularVelocity = Random.insideUnitSphere * 8f;
            if (thrower != null) thrower.IgnoreCollisionWith(col);
            // Halo: the timer runs from the throw unless it starts at the first bounce / at rest (frag grenades)
            if (proj == null || proj.DetonationTimerStarts == HaloProjectileDef.TimerStart.Immediately) StartTimer();
        }

        private void StartTimer()
        {
            if (_timer >= 0f) return;
            float t = _proj != null ? Mathf.Max(0.05f, Random.Range(_proj.TimerMin, Mathf.Max(_proj.TimerMin, _proj.TimerMax))) : 3f;
            _timer = Time.time + t;
        }

        private void OnCollisionEnter(Collision c)
        {
            if (_done) return;
            if (_proj != null && _proj.DetonationTimerStarts != HaloProjectileDef.TimerStart.WhenAtRest) StartTimer();
            // a plasma grenade sticks (its material response is "attach")
            if (!_stuck && _proj != null && _proj.Materials.Count > 0 && _proj.Materials[0].Default == HaloProjectileDef.Response.Attach)
            {
                _stuck = true;
                _body.isKinematic = true;
                if (c.collider != null) transform.SetParent(c.collider.transform, true);
                StartTimer();
            }
        }

        private void Update()
        {
            if (_done) return;
            if (_timer < 0f && _proj != null && _proj.DetonationTimerStarts == HaloProjectileDef.TimerStart.WhenAtRest && _body.linearVelocity.sqrMagnitude < 0.04f && Time.time - _born > 0.3f) StartTimer();
            bool due = (_timer >= 0f && Time.time >= _timer) || Time.time - _born > _maxLife;
            if (!due) return;
            _done = true;
            if (_proj != null && _proj.DetonationEffect != null) HaloFx.Play(_proj.DetonationEffect, transform.position, Quaternion.identity, null);
            Destroy(gameObject);
        }
    }
}

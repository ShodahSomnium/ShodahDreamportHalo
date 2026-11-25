using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// One weapon as an object: the weapon tag's THIRD-person model (HaloModelBuilder; the cyborg's hand holds a Halo weapon at its model
    /// origin, or at its "right hand" marker: the flamethrower and the fuel rod gun), its firing state (<see cref="HaloWeaponState"/>), the
    /// places the hands use (the support hand at the model's "left hand cyborg" marker, the magazine well, the charging handle), the muzzle
    /// markers ("primary trigger", a second one for the plasma rifle's alternating barrels) and a recoil spring.
    ///
    /// Frame: the object's +Z is the barrel (Halo's +X), +Y up, origin = where the hand grips. The model sits under "pivot" (turned so that
    /// Halo +X runs along +Z); <see cref="ModelRoot"/> is the model root exactly as HaloModelBuilder builds it (the Desk module may place it
    /// on a monitor avatar's hand: it is reset under the pivot every frame before that). Scale: the cyborg is 0.704 world units
    /// (2.15 m) tall; the weapon is scaled to the player's avatar (<see cref="AvatarScale"/>).
    /// </summary>
    public sealed class HaloGun : MonoBehaviour
    {
        public enum Holster { None, BackRight, BackLeft, HipRight, HipLeft }

        /// <summary>The multiplayer cyborg's height in metres (0.704 world units, from characters\cyborg\cyborg's model bounds).</summary>
        public const float CyborgHeight = 0.704f * HaloSpace.Unit;
        /// <summary>Extra size factor on top of the avatar scale (ours; settings "combat.gunscale").</summary>
        public static float SizeFactor = 1f;

        public HaloWeaponDef Def;
        public HaloWeaponState State;
        public HaloLocal.Weapon Info;                 // the HUD's record (HaloLocal.Weapons[i]) while carried
        public HaloModelBuilder.Built Built;
        public Transform Pivot, ModelRoot;
        public Transform Muzzle, Muzzle2;
        private int _barrel;
        public Holster Slot;

        // places in the gun's own frame (metres before the avatar scale)
        public Vector3 SupportLocal, WellLocal, RackLocal, FrontLocal, RearLocal;
        public Bounds LocalBounds;
        public bool HasSupport;

        // the hand-reload state (Game/Combat/HaloHandling.Reload.cs)
        public bool MagIn = true;
        public bool NeedsRack;
        public float MagOutAt = -10f;
        public GameObject MagVisual;                  // the magazine shown in the well (a copy of the magazine prop), null when out / none
        public float OverheatedAt = -10f;             // the vent lasts at least the first-person "overheated" animation

        // recoil
        private float _kickPitch, _kickPitchVel, _kickBack, _kickBackVel, _kickYaw, _kickYawVel;

        public float AvatarScale = 1f;

        /// <summary>Builds a weapon object under <paramref name="parent"/> (null when the tag has no model).</summary>
        public static HaloGun Create(HaloWeaponDef def, Transform parent)
        {
            if (def == null || def.Model == null) return null;
            var go = new GameObject("Gun " + (def.Name ?? def.Code));
            if (parent != null) go.transform.SetParent(parent, false);
            var gun = go.AddComponent<HaloGun>();
            gun.Def = def;
            gun.State = new HaloWeaponState(def);
            var pivot = new GameObject("pivot").transform;
            pivot.SetParent(go.transform, false);
            pivot.localRotation = Quaternion.Euler(0f, -90f, 0f);      // Halo +X (barrel) -> +Z
            gun.Pivot = pivot;
            var built = HaloModelBuilder.Build(def.Model, pivot, new HaloModelBuilder.Options { CastShadows = true, Lod = 4 });
            if (built == null || built.Root == null)
            {
                Destroy(go);
                return null;
            }

            gun.Built = built;
            gun.ModelRoot = built.Root.transform;
            gun.Measure();
            try { gun.Anim = HaloWeaponAnim.Attach(built, def.Tag); }
            catch (Exception e) { HaloLog.Warn("Combat: weapon animations of " + def.Name + ": " + e.Message); gun.Anim = null; }
            gun.Sounds = SoundsOf(def, gun.Points);
            return gun;
        }

        /// <summary>The places of the hands, the well, the muzzles - read while the object is still at the origin, unturned, unscaled.</summary>
        private void Measure()
        {
            Transform t = transform;
            Vector3 p0 = t.position; Quaternion r0 = t.rotation; Vector3 s0 = t.localScale;
            t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            t.localScale = Vector3.one;
            // the grip: the "right hand" marker where there is one, else the model origin
            Transform rh = Built.Marker("right hand");
            Vector3 grip = rh != null ? Pivot.InverseTransformPoint(rh.position) : Vector3.zero;
            Pivot.localPosition = -(Pivot.localRotation * grip);
            Muzzle = Built.Marker("primary trigger");
            Muzzle2 = Built.Marker("primary trigger1");
            if (Muzzle == null) Muzzle = Built.Marker("primary trigger01");
            Transform lh = Built.Marker("left hand cyborg") ?? Built.Marker("left hand") ?? Built.Marker("cyborg left hand");
            // the model's bounds in the gun's frame
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            if (Built.Renderer != null && Built.Renderer.sharedMesh != null)
            {
                Bounds mb = Built.Renderer.sharedMesh.bounds;      // (model root space: the renderer object sits at the root)
                Vector3 c = mb.center, e = mb.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3((i & 1) != 0 ? e.x : -e.x, (i & 2) != 0 ? e.y : -e.y, (i & 4) != 0 ? e.z : -e.z);
                    Vector3 w = Built.Renderer.transform.TransformPoint(corner);
                    if (!any) { b = new Bounds(w, Vector3.zero); any = true; } else b.Encapsulate(w);
                }
            }

            LocalBounds = b;
            HasSupport = lh != null;
            SupportLocal = lh != null ? lh.position : new Vector3(0f, b.min.y * 0.3f, Mathf.Max(0.15f, b.max.z * 0.55f));
            FrontLocal = new Vector3(0f, b.center.y, b.max.z);
            RearLocal = new Vector3(0f, b.center.y, b.min.z);
            if (Muzzle != null) FrontLocal = Muzzle.position;
            WellLocal = WellFor(Def.Kind, b);
            RackLocal = new Vector3(0f, b.max.y - 0.01f, b.min.z * 0.45f);
            MeasureReloadPoints();
            t.SetPositionAndRotation(p0, r0);
            t.localScale = s0;
        }

        // ------------------------------------------------------------------ the reload points and the part that is pulled back

        public HaloWeaponPoints Points;
        public Transform PullNode;              // the model node that moves (pistol slide, operating handle, pump), null = none
        public Vector3 PullRest, PullDir;       // its rest local position and the local direction it travels (back)
        public float PullTravel;                // metres it travels (gun frame, before the avatar scale)
        public bool HasPull;
        public Vector3 SeatLocal;               // where a seated magazine sits
        public Transform MagNode;               // the third-person model's magazine node (pistol, sniper rifle), hidden while the magazine is out
        /// <summary>The slide / bolt is held back (empty after the last round, or waiting for a rack); a pull releases it.</summary>
        public bool LockedBack;
        private float _cycle;                   // the slide's quick trip back on a shot (1 -> 0)
        private float _pulled;                  // how far a hand holds it back right now (metres, gun frame)
        private static readonly System.Collections.Generic.HashSet<HaloTag> Logged = new System.Collections.Generic.HashSet<HaloTag>();

        private Vector3 FromHalo(HVec3 h) { return ModelRoot.TransformPoint(new Vector3(h.X, h.Z, h.Y) * HaloSpace.Unit); }

        /// <summary>The well and the pulled part measured from the models (HaloWeaponPoints), in the gun's frame; logged once per weapon.</summary>
        private void MeasureReloadPoints()
        {
            try { Points = HaloWeaponPoints.Measure(Def); }
            catch (Exception e) { HaloLog.Warn("Combat: reload points of " + Def.Name + " failed: " + e.Message); Points = null; }
            if (Points == null) return;
            if (Points.HasWell) { WellLocal = FromHalo(Points.Well); SeatLocal = FromHalo(Points.Seat); }
            else SeatLocal = WellLocal;
            MagNode = Built.Node("frame magazine");
            if (Points.PullNode != null)
            {
                PullNode = Built.Node(Points.PullNode);
                if (PullNode != null)
                {
                    HasPull = true;
                    RackLocal = FromHalo(Points.Pull);
                    PullRest = PullNode.localPosition;
                    Transform parent = PullNode.parent;
                    Vector3 back = ModelRoot.TransformDirection(Vector3.left);      // the model's -x (Halo: back along the barrel)
                    PullDir = parent != null ? parent.InverseTransformDirection(back).normalized : back;
                    PullTravel = Points.PullTravel * HaloSpace.Unit;
                }
            }

            if (Logged.Add(Def.Tag))
                HaloLog.Info("Combat: reload points for " + Def.Name + ": well " + (Points.HasWell ? Points.Well + " (" + Points.WellFrom + ") = " + WellLocal.ToString("0.000") + " m" : "none")
                             + ", pull " + (HasPull ? "'" + Points.PullNode + "' at " + Points.Pull + " = " + RackLocal.ToString("0.000") + " m, travel " + (PullTravel * 100f).ToString("0.0") + " cm" : "none")
                             + (HasSupport ? ", support " + SupportLocal.ToString("0.000") + " m" : ""));
        }

        /// <summary>
        /// The weapon's OWN magazine as a loose prop: the model (the third-person one, or the first-person one when only it has the magazine
        /// node: the assault rifle) with every triangle but the "frame magazine" node's dropped; its middle at the prop's origin, in the gun's
        /// frame (+Z barrel, +Y up). Null when the weapon has no magazine node (shells, rockets, needles: the ammunition box model then).
        /// </summary>
        public GameObject BuildMagazineProp(string name, Transform parent)
        {
            if (Points == null || !Points.HasWell) return null;
            HaloTag model = Points.MagInFirstPerson ? Def.FirstPersonModel : Def.Model;
            if (model == null || (Points.MagInFirstPerson == false && MagNode == null)) return null;
            try
            {
                var holder = new GameObject(name);
                holder.transform.SetParent(parent, false);
                var pivot = new GameObject("pivot").transform;
                pivot.SetParent(holder.transform, false);
                pivot.localRotation = Quaternion.Euler(0f, -90f, 0f);
                var b = HaloModelBuilder.Build(model, pivot, new HaloModelBuilder.Options { CastShadows = false });
                int mi = b != null ? b.Model.NodeIndex("frame magazine") : -1;
                if (b == null || b.Renderer == null || mi < 0) { Destroy(holder); return null; }
                Mesh src = b.Renderer.sharedMesh;
                BoneWeight[] bw = src.boneWeights;
                Mesh mesh = Instantiate(src);
                mesh.name = name;
                var keep = new System.Collections.Generic.List<int>();
                for (int s = 0; s < src.subMeshCount; s++)
                {
                    int[] tris = src.GetTriangles(s);
                    keep.Clear();
                    for (int i = 0; i + 2 < tris.Length; i += 3)
                        if (OnMag(bw, tris[i], mi) && OnMag(bw, tris[i + 1], mi) && OnMag(bw, tris[i + 2], mi)) { keep.Add(tris[i]); keep.Add(tris[i + 1]); keep.Add(tris[i + 2]); }
                    mesh.SetTriangles(keep, s);
                }

                b.Renderer.sharedMesh = mesh;
                // the magazine's middle onto the prop's origin
                HVec3 seat = Points.Seat;
                if (Points.MagInFirstPerson) seat = new HVec3(seat.X - Points.FirstPersonOffset.X, seat.Y - Points.FirstPersonOffset.Y, seat.Z - Points.FirstPersonOffset.Z);
                Vector3 c = holder.transform.InverseTransformPoint(b.Root.transform.TransformPoint(new Vector3(seat.X, seat.Z, seat.Y) * HaloSpace.Unit));
                pivot.localPosition = -c;
                return holder;
            }
            catch (Exception e)
            {
                HaloLog.Warn("Combat: the magazine of " + Def.Name + " could not be cut out: " + e.Message);
                return null;
            }
        }

        private static bool OnMag(BoneWeight[] bw, int v, int mag) { return v < bw.Length && bw[v].boneIndex0 == mag && bw[v].weight0 >= 0.5f; }

        /// <summary>A hand holds the pulled part back this far (metres in the world; 0 = let go).</summary>
        public void SetPulled(float worldMetres) { _pulled = worldMetres / Mathf.Max(0.01f, transform.lossyScale.z); }

        /// <summary>A round left: the slide (pistol) runs back and forward.</summary>
        public void CycleSlide() { if (Def.Kind == HaloWeaponDef.Family.Pistol) _cycle = 1f; }

        /// <summary>
        /// Moves the slide / handle / pump for this frame. One driver per part: while a VR hand holds the part or a rack is due, Combat moves
        /// it (it follows the hand; held back until racked) and tells the Desk module's HaloWeaponAnim to leave it (SetHeld); otherwise Halo's
        /// own weapon animations drive it (the fire cycle, the "empty" slide / bolt held back, reloads, vents). Without animations (no
        /// HaloWeaponAnim for this weapon) Combat runs a simple fire cycle and the empty lock itself.
        /// </summary>
        public void StepParts(float dt)
        {
            if (MagNode != null) MagNode.localScale = MagIn ? Vector3.one : Vector3.one * 0.0001f;      // (the model's own magazine leaves with an eject)
            if (Anim != null)
            {
                // the magazine stays under Combat's handling in VR (the first-person reload choreography flings it far off the gun)
                if (!_magHeld) { _magHeld = true; Anim.SetHeld("magazine", true); }      // (VR and monitor: the fp reload choreography would fling it far off the third-person gun)
                bool empty = State.UsesAmmo && State.Loaded <= 0 && MagIn && !NeedsRack;
                if (empty != _emptyState) { _emptyState = empty; Anim.SetState("empty", empty); }
            }

            if (!HasPull || PullNode == null) return;
            _cycle = Mathf.MoveTowards(_cycle, 0f, dt * 14f);
            bool mine = HandOnPull || NeedsRack || Anim == null;
            if (Anim != null && mine != _pullHeld)
            {
                _pullHeld = mine;
                Anim.SetHeld(Points.PullNode, mine);
                if (!mine) PullNode.localPosition = PullRest;       // (handed back to the animation at rest)
            }

            LockedBack = NeedsRack || (State.Loaded <= 0 && Def.Kind == HaloWeaponDef.Family.Pistol && State.UsesAmmo);
            if (!mine) return;
            float cycle = Anim == null ? _cycle : 0f;
            bool lockBack = NeedsRack || (Anim == null && LockedBack);
            float u = Mathf.Max(lockBack ? 0.85f : 0f, cycle, PullTravel > 0f ? Mathf.Clamp01(_pulled / PullTravel) : 0f);
            PullNode.localPosition = PullRest + PullDir * (PullTravel * u);
        }

        /// <summary>Halo's weapon animation of a kind ("fire", "chamber", "reload-empty", "ready", "overheating", "melee" ...), if it has one.</summary>
        public bool Animate(string kind, float duration = -1f)
        {
            if (Anim == null) return false;
            try { return Anim.Play(kind, duration); }
            catch (Exception e) { HaloLog.Warn("Combat: animation " + kind + " of " + Def.Name + ": " + e.Message); return false; }
        }

        public void AnimateState(string state, bool on)
        {
            if (Anim == null) return;
            try { Anim.SetState(state, on); } catch (Exception) { }
        }

        public HaloReloadSounds Sounds;        // the hand reload's sounds (pieces of the game's reload sounds)
        private static readonly System.Collections.Generic.Dictionary<HaloTag, HaloReloadSounds> SoundCache = new System.Collections.Generic.Dictionary<HaloTag, HaloReloadSounds>();

        private static HaloReloadSounds SoundsOf(HaloWeaponDef def, HaloWeaponPoints points)
        {
            HaloReloadSounds s;
            if (SoundCache.TryGetValue(def.Tag, out s)) return s;
            try { s = HaloReloadSounds.Build(def, points); }
            catch (Exception e) { HaloLog.Warn("Combat: reload sounds of " + def.Name + ": " + e.Message); s = new HaloReloadSounds(); }
            SoundCache[def.Tag] = s;
            HaloLog.Info("Combat: reload sounds for " + def.Name + ": " + s.Describe());
            HaloSoundPiece.Warm(s.MagOut); HaloSoundPiece.Warm(s.MagIn); HaloSoundPiece.Warm(s.Pull); HaloSoundPiece.Warm(s.Release);
            return s;
        }

        /// <summary>Plays one hand action's sound at the gun (null piece = nothing).</summary>
        public void Sound(HaloReloadSounds.Piece p, string action) { HaloSoundPiece.Play(p, ModelRoot != null ? ModelRoot : transform, action, Def.Name); }

        public HaloWeaponAnim Anim;            // Halo's own weapon animations on this model (Desk module), null when it has no animated part

        /// <summary>The vent (overheated effect, looping while overheated) and the charge glow / sound of this gun (local player's own).</summary>
        public HaloGunFx Fx { get { if (_fx == null) _fx = new HaloGunFx(Def, ModelRoot, true); return _fx; } }
        private HaloGunFx _fx;

        private void OnDestroy() { if (_fx != null) _fx.Stop(); }
        public void StopFx() { if (_fx != null) _fx.Stop(); }
        public bool HandsVr;                   // held in VR hands: the magazine stays Combat's
        public bool HandOnPull;                // a VR hand holds the slide / handle / pump
        private bool _magHeld, _emptyState, _pullHeld;

        /// <summary>Where a magazine / shell / rocket pair goes in, in the gun's frame (ours: Halo has no reload by hand; places read off the models).</summary>
        private static Vector3 WellFor(HaloWeaponDef.Family k, Bounds b)
        {
            switch (k)
            {
                case HaloWeaponDef.Family.Pistol: return new Vector3(0f, b.min.y + 0.02f, -0.01f);                   // the bottom of the grip
                case HaloWeaponDef.Family.Sniper: return new Vector3(0f, b.min.y + 0.03f, -0.12f);                   // bullpup: behind the grip
                case HaloWeaponDef.Family.Needler: return new Vector3(0f, b.max.y - 0.03f, -0.08f);                  // the needles on top
                case HaloWeaponDef.Family.Rocket: return new Vector3(0f, b.center.y, b.min.z + 0.05f);               // the back of the tube
                case HaloWeaponDef.Family.Shotgun: return new Vector3(0f, b.min.y + 0.03f, 0.06f);                  // the loading port under the receiver
                case HaloWeaponDef.Family.Flamethrower: return new Vector3(0f, b.max.y - 0.1f, b.min.z + 0.08f);     // the canister
                default: return new Vector3(0f, b.min.y + 0.03f, 0.08f);                                             // in front of the trigger guard
            }
        }

        /// <summary>The muzzle the next round leaves from (the plasma rifle alternates its two barrels).</summary>
        public Transform NextMuzzle()
        {
            if (Muzzle2 == null) return Muzzle != null ? Muzzle : transform;
            _barrel ^= 1;
            return _barrel == 0 ? Muzzle : Muzzle2;
        }

        public Vector3 MuzzlePosition => Muzzle != null ? Muzzle.position : transform.TransformPoint(FrontLocal);
        public Vector3 Barrel => transform.forward;

        /// <summary>The model root back under the pivot (the Desk module may have placed it on the avatar's hand last frame).</summary>
        public void ResetModel()
        {
            if (ModelRoot == null) return;
            ModelRoot.localPosition = Vector3.zero;
            ModelRoot.localRotation = Quaternion.identity;
            ModelRoot.localScale = Vector3.one;
        }

        public void SetScale(float avatarScale)
        {
            AvatarScale = avatarScale;
            float s = avatarScale * (1.7f / CyborgHeight) * SizeFactor;
            transform.localScale = Vector3.one * s;
        }

        // ------------------------------------------------------------------ the feel (ours: Halo has no recoil on the gun model)

        public void Kick(float strength)
        {
            float up = strength * 6f;
            _kickPitchVel += up * (0.9f + UnityEngine.Random.value * 0.35f) * 6f;
            _kickYawVel += UnityEngine.Random.Range(-1f, 1f) * up * 0.8f;
            _kickBackVel += 0.9f * strength;
        }

        public void StepRecoil(float dt, float steadiness, out Quaternion kick, out Vector3 shove)
        {
            const float k = 140f, c = 18f;
            _kickPitchVel += (-k * _kickPitch - c * _kickPitchVel) * dt;
            _kickPitch += _kickPitchVel * dt;
            _kickYawVel += (-k * _kickYaw - c * _kickYawVel) * dt;
            _kickYaw += _kickYawVel * dt;
            _kickBackVel += (-k * 1.5f * _kickBack - c * 1.2f * _kickBackVel) * dt;
            _kickBack += _kickBackVel * dt;
            float s = Mathf.Clamp01(steadiness);
            kick = Quaternion.Euler(-_kickPitch * s, _kickYaw * s, 0f);
            shove = new Vector3(0f, 0f, -_kickBack * 0.02f * s);
        }

        /// <summary>How strong a shot feels (ours, from the damage class of the projectile).</summary>
        public float KickStrength()
        {
            switch (Def.Kind)
            {
                case HaloWeaponDef.Family.Rocket: case HaloWeaponDef.Family.FuelRod: return 1.6f;
                case HaloWeaponDef.Family.Sniper: case HaloWeaponDef.Family.Shotgun: return 1.3f;
                case HaloWeaponDef.Family.Pistol: return 0.8f;
                case HaloWeaponDef.Family.Rifle: return 0.35f;
                case HaloWeaponDef.Family.PlasmaRifle: case HaloWeaponDef.Family.Needler: case HaloWeaponDef.Family.Flamethrower: return 0.25f;
                default: return 0.5f;
            }
        }

        /// <summary>Fills the HUD record from the firing state.</summary>
        public void SyncInfo()
        {
            if (Info == null) return;
            Info.Tag = Def.Tag;
            Info.Loaded = State.Loaded;
            Info.Reserve = State.Reserve;
            Info.Charge = Def.UsesBattery ? State.Battery : 1f;
            Info.Heat = State.Heat;
            Info.Overheated = State.Overheated;
            Info.Label = Def.Name;
        }
    }

    /// <summary>A loose magazine / shell / rocket pair: in a hand, falling to the ground (a simple fall onto the ground, no physics body), lying, gone after a while.</summary>
    public sealed class HaloMagazine : MonoBehaviour
    {
        public int Rounds;
        public bool Falling;
        private Vector3 _vel;
        private float _age;
        private bool _resting;

        public void Drop(Vector3 velocity)
        {
            transform.SetParent(HaloCombat.ItemParent, true);
            Falling = true;
            _resting = false;
            _vel = velocity;
            _age = 0f;
        }

        private void Update()
        {
            if (!Falling) return;
            float dt = Time.deltaTime;
            _age += dt;
            if (_age > 15f) { Destroy(gameObject); return; }
            if (_resting) return;
            _vel += Vector3.down * 9.78f * dt;
            Vector3 step = _vel * dt;
            RaycastHit h;
            if (Physics.Raycast(transform.position, step.normalized, out h, step.magnitude + 0.02f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore))
            {
                transform.position = h.point + h.normal * 0.015f;
                transform.rotation = Quaternion.FromToRotation(transform.up, h.normal) * transform.rotation;
                if (_vel.magnitude > 2f) { _vel = Vector3.Reflect(_vel, h.normal) * 0.25f; return; }
                _resting = true;
                return;
            }

            transform.position += step;
            transform.Rotate(240f * dt, 60f * dt, 0f, Space.Self);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Effects module] Plays the game's own effects from the current map: effect tags (effe: events with delays and durations; parts =
    /// sounds through HaloAudio, lights, decals, particle systems, nested effects; particles), contrails (cont), decals (deca), lights (ligh)
    /// and particle systems (pctl). Everything is read from the tags (Data/Effects/HaloFxData.cs) and drawn by one pooled runtime
    /// (Game/Fx/HaloFxRuntime*.cs, shader Shaders/Fx/HaloFx.shader). Main thread only. Damage parts (jpt!) are ignored: Combat does damage.
    /// Object parts (garbage, projectiles) are not created here: <see cref="ObjectSpawned"/> tells whoever wants them.
    ///
    /// Directions: an effect's own frame is <c>rotation</c> (Unity convention, +Z forward). Halo locations: "" = that frame, "normal" /
    /// "incident" / "negative incident" / "reflection" / "gravity" / "up" from <see cref="Impact"/>'s surface normal and incoming direction,
    /// any other name = the "marker:name" child of <c>attach</c> (HaloModelBuilder markers; falls back to the frame).
    /// First person: Halo effect particles can be "only in first person" / "only in third person" - pass firstPerson = true for the local
    /// player's own weapon (the VR hand gun), false (default) for everyone else's.
    /// </summary>
    public static class HaloFx
    {
        /// <summary>A playing effect (looping effects need <see cref="Stop"/>). Default = nothing.</summary>
        public struct Handle
        {
            internal FxRun Run; internal int Gen;
            public bool Alive { get { var rt = HaloFxRuntime.Instance; return rt != null && rt.IsAlive(Run, Gen); } }
        }

        /// <summary>A contrail behind a head (a followed transform or positions given to <see cref="MoveContrail"/>).</summary>
        public struct Trail
        {
            internal FxTrail T; internal int Gen;
            public bool Alive { get { var rt = HaloFxRuntime.Instance; return rt != null && rt.TrailAlive(T, Gen); } }
        }

        /// <summary>Everything an object tag carries on its markers (object attachments: contrails, lights, effects, particle systems,
        /// looping sounds), started by <see cref="Attach"/>.</summary>
        public sealed class Attachments
        {
            internal readonly List<Trail> Trails = new List<Trail>();
            internal readonly List<Handle> Effects = new List<Handle>();
            internal readonly List<FxSys> Systems = new List<FxSys>();
            internal readonly List<int> SystemGens = new List<int>();
            internal readonly List<int> Lights = new List<int>();
            internal readonly List<int> LightGens = new List<int>();
            internal readonly List<int> Flares = new List<int>();
            internal readonly List<int> FlareGens = new List<int>();
            internal readonly List<HaloAudio.Handle> Sounds = new List<HaloAudio.Handle>();
            public bool Stopped { get; internal set; }
        }

        /// <summary>Multiplies every particle that is not self-illuminated (smoke, dust, debris): Halo lights those by the environment. ours.</summary>
        public static Color Ambient = new Color(0.85f, 0.85f, 0.85f, 1f);
        /// <summary>The camera alpha-blended particles sort against (null = Camera.main).</summary>
        public static Camera ViewCamera;
        /// <summary>What particles bounce on and decals stick to (the static world).</summary>
        public static int CollisionMask = HaloLayers.GroundMask;
        public static int DecalMask = HaloLayers.GroundMask;

        /// <summary>An effect part that creates an object (garbage debris, projectiles): tag, position, initial velocity (m/s).</summary>
        public static event Action<HaloTag, Vector3, Vector3> ObjectSpawned;

        internal static void RaiseObjectSpawned(HaloTag t, Vector3 p, Vector3 v)
        {
            var h = ObjectSpawned;
            if (h == null) return;
            try { h(t, p, v); } catch (Exception e) { HaloLog.Warn("Fx: ObjectSpawned handler failed: " + e.Message); }
        }

        /// <summary>Unity forward (+Z) onto a Halo model's forward (+X), up kept.</summary>
        internal static readonly Quaternion HaloModelFrame = Quaternion.LookRotation(Vector3.right, Vector3.up);

        static HaloFxRuntime Rt { get { return HaloFxRuntime.Ensure(); } }

        // ------------------------------------------------------------------ effects

        /// <summary>Plays an effect tag at a point; <paramref name="attach"/> = follow this transform (muzzle flash on a gun).</summary>
        public static void Play(HaloTag effect, Vector3 position, Quaternion rotation, Transform attach = null)
        {
            Play(effect, position, rotation, attach, false);
        }

        /// <summary>Plays an effect; <paramref name="firstPerson"/> = the local player's own view (first-person-only particles show,
        /// third-person-only ones do not).</summary>
        public static Handle Play(HaloTag effect, Vector3 position, Quaternion rotation, Transform attach, bool firstPerson)
        {
            if (effect == null) return default(Handle);
            try
            {
                Vector3 f = rotation * Vector3.forward;
                var r = Rt.Begin(effect, position, rotation, attach, f, -f, firstPerson, false);
                return r != null ? new Handle { Run = r, Gen = r.Gen } : default(Handle);
            }
            catch (Exception e) { HaloLog.Warn("Fx: playing " + effect + " failed: " + e.Message); return default(Handle); }
        }

        /// <summary>Plays an effect on an object's markers: the effect's locations name markers of <paramref name="model"/>
        /// (e.g. a weapon's "fire" effect: "primary trigger", "primary ejection").</summary>
        public static Handle PlayOn(HaloTag effect, Transform model, bool firstPerson = false)
        {
            if (model == null) return default(Handle);
            // a Halo model keeps Halo's axes (+X forward, +Y up, +Z left): the effect's own frame faces the model's +X
            return Play(effect, model.position, model.rotation * HaloModelFrame, model, firstPerson);
        }

        /// <summary>An impact / detonation: <paramref name="normal"/> = the surface normal, <paramref name="incoming"/> = the direction the
        /// projectile travelled (unit or not). The effect's frame faces along the normal.</summary>
        public static Handle Impact(HaloTag effect, Vector3 point, Vector3 normal, Vector3 incoming, Transform attach = null)
        {
            if (effect == null) return default(Handle);
            { int k = _impN++ % ImpLog.Length; ImpLog[k].T = effect; ImpLog[k].P = point; ImpLog[k].N = normal; ImpLog[k].D = incoming; }
            try
            {
                if (normal.sqrMagnitude < 1e-8f) normal = incoming.sqrMagnitude > 1e-8f ? -incoming : Vector3.up;
                Vector3 up = Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
                var r = Rt.Begin(effect, point, Quaternion.LookRotation(normal, up), attach, normal, incoming, false, false);
                return r != null ? new Handle { Run = r, Gen = r.Gen } : default(Handle);
            }
            catch (Exception e) { HaloLog.Warn("Fx: impact " + effect + " failed: " + e.Message); return default(Handle); }
        }

        /// <summary>Starts an effect that loops (its loop start..stop events) until <see cref="Stop"/>. Non-looping effects just play.</summary>
        public static Handle Loop(HaloTag effect, Vector3 position, Quaternion rotation, Transform attach, bool firstPerson = false)
        {
            if (effect == null) return default(Handle);
            try
            {
                Vector3 f = rotation * Vector3.forward;
                var r = Rt.Begin(effect, position, rotation, attach, f, -f, firstPerson, true);
                return r != null ? new Handle { Run = r, Gen = r.Gen } : default(Handle);
            }
            catch (Exception e) { HaloLog.Warn("Fx: looping " + effect + " failed: " + e.Message); return default(Handle); }
        }

        /// <summary>Ends a loop (the events after the loop still play) or, with <paramref name="now"/>, removes the effect at once
        /// (its particles live on).</summary>
        public static void Stop(Handle h, bool now = false)
        {
            if (!h.Alive) return;
            if (now) h.Run.Alive = false;
            else h.Run.StopRequested = true;
        }

        // ------------------------------------------------------------------ contrails

        /// <summary>A contrail that follows <paramref name="follow"/> until it is destroyed / deactivated or <see cref="StopContrail"/>;
        /// the trail then fades out on its own.</summary>
        public static Trail Contrail(HaloTag contrail, Transform follow)
        {
            if (contrail == null || follow == null) return default(Trail);
            try { var t = Rt.StartTrail(contrail, follow, follow.position); return t != null ? new Trail { T = t, Gen = t.Gen } : default(Trail); }
            catch (Exception e) { HaloLog.Warn("Fx: contrail " + contrail + " failed: " + e.Message); return default(Trail); }
        }

        /// <summary>A contrail whose head the caller moves with <see cref="MoveContrail"/> (projectiles simulated without a transform).</summary>
        public static Trail Contrail(HaloTag contrail, Vector3 start)
        {
            if (contrail == null) return default(Trail);
            try { var t = Rt.StartTrail(contrail, null, start); return t != null ? new Trail { T = t, Gen = t.Gen } : default(Trail); }
            catch (Exception e) { HaloLog.Warn("Fx: contrail " + contrail + " failed: " + e.Message); return default(Trail); }
        }

        public static void MoveContrail(Trail t, Vector3 head) { if (t.Alive && !t.T.Following && !t.T.Stopped) t.T.Head = head; }

        /// <summary>No new points: the trail's points age out (a projectile hit something).</summary>
        public static void StopContrail(Trail t) { if (t.Alive) { t.T.Stopped = true; t.T.Following = false; t.T.Follow = null; } }

        // ------------------------------------------------------------------ decals, lights

        /// <summary>Puts a decal tag (and its chain) on the surface at <paramref name="point"/> facing <paramref name="normal"/>.</summary>
        public static void Decal(HaloTag decal, Vector3 point, Vector3 normal)
        {
            if (decal == null) return;
            try
            {
                var rt = Rt; rt.UseMap(decal.Map);
                var d = rt.Lib.Decal(decal);
                if (d != null) rt.PlaceDecal(d, point, normal, 1f, UnityEngine.Random.value * 2f * Mathf.PI, 0);
            }
            catch (Exception e) { HaloLog.Warn("Fx: decal " + decal + " failed: " + e.Message); }
        }

        /// <summary>A light tag's flash at a point for <paramref name="seconds"/> (or the light's own duration when it has one): weapons'
        /// muzzle-flash lights are object attachments that Halo turns on while firing - call this per shot.</summary>
        public static void Flash(HaloTag light, Vector3 position, Transform attach = null, float seconds = 2f / 30f)
        {
            if (light == null) return;
            try
            {
                var rt = Rt; rt.UseMap(light.Map);
                var d = rt.Lib.Light(light);
                if (d != null) rt.SpawnLight(d, position, attach, seconds);
            }
            catch (Exception e) { HaloLog.Warn("Fx: light " + light + " failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ object attachments

        /// <summary>
        /// Starts an object tag's attachments (Object +320: contrails, lights, effects, particle systems, looping sounds) on its markers under
        /// <paramref name="root"/> (HaloModelBuilder "marker:name" children; the root itself when the marker is missing or the model was not
        /// built). Projectiles: call on spawn, <see cref="Detach"/> on detonation. <paramref name="lights"/> = false leaves the lights out
        /// (weapons: their muzzle light is a firing flash, see <see cref="Flash"/>).
        /// </summary>
        public static Attachments Attach(HaloTag objectTag, Transform root, bool firstPerson = false, bool lights = true)
        {
            var a = new Attachments();
            if (objectTag == null || root == null || !objectTag.Is("obje")) return a;
            try
            {
                var rt = Rt; rt.UseMap(objectTag.Map);
                bool projectile = objectTag.Is("proj");
                // a projectile with a model is drawn by its built model root (+X forward, HaloProjectiles turns +X along the flight); without
                // one by a plain anchor (+Z forward)
                bool modelRoot = objectTag.Root.Ref(40) != null && !HaloFxMarkers.IsMarker(root);
                foreach (var at in objectTag.Root.Block(320, 72))
                {
                    HaloTag t = at.Ref(0);
                    if (t == null) continue;
                    string marker = at.Str32(16);
                    Transform m = string.IsNullOrEmpty(marker) ? null : HaloFxMarkers.Find(root, marker);
                    Transform where = m ?? root;
                    switch (t.ClassName)
                    {
                        case "cont": { var tr = rt.StartTrail(t, where, where.position); if (tr != null) a.Trails.Add(new Trail { T = tr, Gen = tr.Gen }); break; }
                        case "ligh":
                        {
                            if (!lights) break;
                            var d = rt.Lib.Light(t);
                            int s = d != null ? rt.SpawnLight(d, where.position, where, 0f, true) : -1;
                            if (s >= 0) { a.Lights.Add(s); a.LightGens.Add(rt.LightGen(s)); }
                            // the light's lens flare: what Halo draws of a projectile's light (plasma bolts). Other objects' light flares
                            // (vehicle lamps, flashlights, AI weapons) are not drawn yet.
                            if (projectile && d != null && d.Lens != null)
                            {
                                int fl = rt.StartFlare(d, where.position, where, float.MaxValue, firstPerson);
                                if (fl >= 0) { a.Flares.Add(fl); a.FlareGens.Add(rt.FlareGen(fl)); rt.FlareXForward(fl, modelRoot && where == root); }
                            }
                            break;
                        }
                        case "effe":
                        {
                            Quaternion rot = HaloFxMarkers.IsMarker(where) ? where.rotation * HaloModelFrame : where.rotation;
                            Vector3 fw = rot * Vector3.forward;
                            var r = rt.Begin(t, where.position, rot, where, fw, -fw, firstPerson, true);
                            if (r != null) a.Effects.Add(new Handle { Run = r, Gen = r.Gen });
                            break;
                        }
                        case "pctl":
                        {
                            var d = rt.Lib.System(t);
                            if (d == null) break;
                            var f = new FxFrameW();
                            HaloFxRuntime.FromTransform(where, ref f);
                            var s = rt.StartSystem(d, ref f, firstPerson);
                            if (s != null) { a.Systems.Add(s); a.SystemGens.Add(s.Gen); }
                            break;
                        }
                        case "lsnd":
                        case "snd!":
                        {
                            var h = HaloAudio.Loop(t, where);
                            if (h != null) a.Sounds.Add(h);
                            break;
                        }
                    }
                }
                // widgets (Object +332): light volumes - the visible body of the plasma / needle / ghost / banshee bolts and the rocket's
                // exhaust glow (projectiles only; weapons' flashlight volumes need the flashlight state)
                if (projectile)
                {
                    foreach (var w in objectTag.Root.Block(332, 32))
                    {
                        HaloTag t = w.Ref(0);
                        if (t == null || !t.Is("mgs2")) continue;
                        var v = rt.Lib.Volume(t);
                        if (v == null) continue;
                        Transform m = string.IsNullOrEmpty(v.Marker) ? null : HaloFxMarkers.Find(root, v.Marker);
                        int s = rt.StartVolume(v, m ?? root, firstPerson);
                        if (s >= 0) { a.Flares.Add(s); a.FlareGens.Add(rt.FlareGen(s)); rt.FlareXForward(s, modelRoot && m == null); }
                    }
                }
            }
            catch (Exception e) { HaloLog.Warn("Fx: attachments of " + objectTag + " failed: " + e.Message); }
            return a;
        }

        /// <summary>Stops an object's attachments: contrails stop growing and fade, loops end, systems stop emitting, lights and looping
        /// sounds go off.</summary>
        public static void Detach(Attachments a)
        {
            if (a == null || a.Stopped) return;
            a.Stopped = true;
            var rt = HaloFxRuntime.Instance;
            foreach (var t in a.Trails) StopContrail(t);
            foreach (var h in a.Effects) Stop(h);
            for (int i = 0; i < a.Systems.Count; i++) if (a.Systems[i].Alive && a.Systems[i].Gen == a.SystemGens[i]) { a.Systems[i].StopRequested = true; a.Systems[i].Attached = false; }
            if (rt != null) for (int i = 0; i < a.Lights.Count; i++) if (rt.LightAlive(a.Lights[i], a.LightGens[i])) rt.KillLight(a.Lights[i]);
            if (rt != null) for (int i = 0; i < a.Flares.Count; i++) if (rt.FlareAlive(a.Flares[i], a.FlareGens[i])) rt.KillFlare(a.Flares[i]);
            foreach (var s in a.Sounds) HaloAudio.Stop(s);
        }

        /// <summary>Scales an attachment set's lights (0 = off; e.g. a vehicle's headlights with its driver).</summary>
        public static void SetLights(Attachments a, float scale)
        {
            var rt = HaloFxRuntime.Instance;
            if (a == null || rt == null) return;
            for (int i = 0; i < a.Lights.Count; i++) if (rt.LightAlive(a.Lights[i], a.LightGens[i])) rt.LightScale(a.Lights[i], scale);
            for (int i = 0; i < a.Flares.Count; i++) if (rt.FlareAlive(a.Flares[i], a.FlareGens[i])) rt.FlareScale(a.Flares[i], scale);
        }

        // ------------------------------------------------------------------ lifetime

        /// <summary>Prepares a map's effects (tag definitions read now; textures on first use). Safe to call on the main thread at map
        /// open (~10-90 ms for a multiplayer map in a release build); <see cref="PreloadWorker"/> does the reading off the main thread.</summary>
        public static void Preload(HaloMap map)
        {
            if (map == null) return;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var rt = Rt; rt.UseMap(map);
                int n = rt.Lib.PreloadAll();
                HaloLog.Info("Fx: " + map.Name + " effects ready: " + n + " tags (effe/part/pctl/cont/ligh/deca) in " + sw.ElapsedMilliseconds + " ms");
            }
            catch (Exception e) { HaloLog.Warn("Fx: preload failed: " + e.Message); }
        }

        /// <summary>Worker-thread half of <see cref="Preload"/> (no Unity API): reads every effect tag of the map into its library.</summary>
        public static int PreloadWorker(HaloMap map) { return map != null ? HaloFxLibrary.For(map).PreloadAll() : 0; }

        /// <summary>Removes every running effect of the current match (particles, contrails, decals, lights).</summary>
        public static void Clear()
        {
            var rt = HaloFxRuntime.Instance;
            if (rt != null) rt.ClearAll();
            HaloFxMarkers.Forget();
        }

        /// <summary>Counts for logs and the lab.</summary>
        public static string Stats
        {
            get
            {
                var rt = HaloFxRuntime.Instance;
                if (rt == null) return "fx: not started";
                return "fx: effects " + rt.RunCount + ", particles " + rt.ParticleCount + ", systems " + rt.SystemCount + ", contrails " + rt.TrailCount
                    + ", decals " + rt.DecalCount + ", lights " + rt.LightCount + ", flares " + rt.FlareCount + ", quads " + (rt.Render != null ? rt.Render.QuadCount : 0)
                    + " in " + (rt.Render != null ? rt.Render.ActiveBatches : 0) + " batches; played " + rt.StatPlays + ", spawned " + rt.StatParticlesSpawned;
            }
        }

        public static int ParticleCount { get { var rt = HaloFxRuntime.Instance; return rt != null ? rt.ParticleCount : 0; } }
        public static int DecalCount { get { var rt = HaloFxRuntime.Instance; return rt != null ? rt.DecalCount : 0; } }
        public static int LightCount { get { var rt = HaloFxRuntime.Instance; return rt != null ? rt.LightCount : 0; } }
        public static int FlareCount { get { var rt = HaloFxRuntime.Instance; return rt != null ? rt.FlareCount : 0; } }
        /// <summary>Light volumes (mgs2 widgets of projectiles) being drawn.</summary>
        public static int VolumeCount { get { var rt = HaloFxRuntime.Instance; return rt != null ? rt.VolumeCount : 0; } }
        /// <summary>The "marker:name" child of a built model (first instance), or null.</summary>
        public static Transform Marker(Transform root, string name) { return HaloFxMarkers.Find(root, name); }

        /// <summary>Lab only (allocates): the live effect particles farthest from <paramref name="from"/> - tag, distance, height, radius,
        /// attached, age / life.</summary>
        public static string DebugParticles(Vector3 from, int max) { var rt = HaloFxRuntime.Instance; return rt != null ? rt.DebugParticles(from, max) : ""; }

        struct ImpRec { public HaloTag T; public Vector3 P, N, D; }
        static readonly ImpRec[] ImpLog = new ImpRec[24];
        static int _impN;
        /// <summary>Lab only (allocates): the last impacts given to <see cref="Impact"/> - effect, point, normal, incoming direction.</summary>
        public static string DebugImpacts(int max)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < max && i < _impN && i < ImpLog.Length; i++)
            {
                var r = ImpLog[(_impN - 1 - i) % ImpLog.Length];
                sb.Append("\n    ").Append(r.T != null ? r.T.Name : "-").Append(" at ").Append(r.P.ToString("F2")).Append(" n ").Append(r.N.ToString("F2")).Append(" in ").Append(r.D.normalized.ToString("F2"));
            }
            return sb.ToString();
        }
        /// <summary>Lab only (allocates): live effect particles above height <paramref name="minY"/> - tag, position, velocity, age.</summary>
        public static string DebugParticlesAbove(float minY, int max) { var rt = HaloFxRuntime.Instance; return rt != null ? rt.DebugParticlesAbove(minY, max) : ""; }

        /// <summary>Rebuilds the meshes now (after moving a camera by hand in the same frame, e.g. the lab).</summary>
        public static void DrawNow() { var rt = HaloFxRuntime.Instance; if (rt != null) rt.Draw(); }

    }
}

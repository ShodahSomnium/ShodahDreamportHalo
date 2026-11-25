using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Halo's own footsteps. The multiplayer biped (globals multiplayer information "unit" = characters\cyborg_mp\cyborg_mp.bipd)
    /// names its "footsteps" material effects tag (globals\cyborg.foot): per step kind (0 walk, 1 run, 2 sliding, 3 shuffle, 4 jump,
    /// 5 land) and per material type (MaterialType enum: dirt, sand, stone, ... leaves, ice) an effect and a sound. The material under
    /// the feet comes from a ray down on the world (HaloLayers.GroundMask) and HaloWorld.SurfaceMaterial.
    /// Who steps: the local player (the avatar root, or the head when the client does not move the root; on foot, alive, in a match) -
    /// played at the own feet, quieter - and every remote player in the match on foot (HaloNetGame.Players: their drawn feet), positional.
    /// A step every stride walked; the stride is Halo's (cyborg "stand rifle move-front": 4.57 m per 20-frame cycle with two foot
    /// frames = 2.29 m at Halo's 6.86 m/s run; "crouch rifle move-front": 1.37 m) blended down to a human stride at walking pace. Jumps
    /// push off, landings after a fall sound louder with the height. Port of the BO2 Dreamport's Bo2StepWalker.
    /// </summary>
    public static class HaloFootsteps
    {
        public const int Walk = 0, Run = 1, Sliding = 2, Shuffle = 3, Jump = 4, Land = 5, Kinds = 6;
        public static readonly string[] KindNames = { "walk", "run", "sliding", "shuffle", "jump", "land" };

        /// <summary>Halo's MaterialType enum (invader-defs/enum.json), index = value.</summary>
        public static readonly string[] MaterialNames =
        {
            "dirt", "sand", "stone", "snow", "wood", "metal hollow", "metal thin", "metal thick", "rubber", "glass", "force field", "grunt",
            "hunter armor", "hunter skin", "elite", "jackal", "jackal energy shield", "engineer skin", "engineer force field",
            "flood combat form", "flood carrier form", "cyborg armor", "cyborg energy shield", "human armor", "human skin", "sentinel",
            "monitor", "plastic", "water", "leaves", "elite energy shield", "ice", "hunter shield",
        };

        /// <summary>The local player's own steps, relative to a remote player's (they are right under the ears). // ours</summary>
        public static float OwnGain = 0.6f;
        /// <summary>Remote players farther than this (metres) are not tracked (Halo's footstep class reaches 10 world units = 30 m).</summary>
        public static float RemoteRange = 36f;
        public static bool Enabled = true;

        // Halo's strides (see the class comment) and the human walk they blend down to // ours
        public const float HaloRunSpeed = 6.86f, HaloRunStride = 2.286f, HaloCrouchStride = 1.37f, WalkSpeed = 1.5f, WalkStride = 0.75f;

        static HaloMap _map;
        static HaloTag _foot;
        static HaloTag[,] _snd, _fx;
        static readonly RaycastHit[] Hits = new RaycastHit[8];
        static readonly HashSet<int> Logged = new HashSet<int>();
        public static int Played, Silent, Lands, Jumps;

        static readonly Walker _own = new Walker { Own = true };
        static readonly Walker[] Remote = new Walker[64];

        /// <summary>The footstep table of a map (its globals' MP unit's material effects). False when the map has none.</summary>
        public static bool Load(HaloMap map)
        {
            if (map == null) return false;
            if (map == _map) return _foot != null;
            _map = map; _foot = null;
            _snd = new HaloTag[Kinds, MaterialNames.Length]; _fx = new HaloTag[Kinds, MaterialNames.Length];
            Logged.Clear();
            try
            {
                HaloTag g = null;
                foreach (var t in map.OfClass("matg")) { g = t; break; }
                HaloTag unit = null;
                if (g != null && g.Root.Count(356) > 0) unit = g.Root.Element(356, 0, 160).Ref(16);
                if (unit == null) foreach (var t in map.OfClass("bipd")) { unit = t; break; }
                if (unit == null || unit.ClassName != "bipd") { HaloLog.Warn("Audio: footsteps - no multiplayer biped in " + map.Name); return false; }
                _foot = unit.Root.Ref(908);
                if (_foot == null) { HaloLog.Warn("Audio: footsteps - " + unit.Name + " names no footsteps tag"); return false; }
                int kinds = 0, sounds = 0;
                foreach (var e in _foot.Root.Block(0, 28))
                {
                    if (kinds >= Kinds) break;
                    int m = 0;
                    foreach (var mt in e.Block(0, 48))
                    {
                        if (m >= MaterialNames.Length) break;
                        _fx[kinds, m] = mt.Ref(0); _snd[kinds, m] = mt.Ref(16);
                        if (_snd[kinds, m] != null) sounds++;
                        m++;
                    }
                    kinds++;
                }
                HaloLog.Info("Audio: footsteps from " + unit.Name + " -> " + _foot.Name + " (" + kinds + " step kinds, " + sounds + " sounds)");
                return true;
            }
            catch (Exception e) { HaloLog.Warn("Audio: reading the footsteps failed: " + e.Message); _foot = null; return false; }
        }

        /// <summary>The sound of a step kind on a material (null: Halo plays none there).</summary>
        public static HaloTag SoundFor(int kind, int material)
        {
            if (_snd == null || kind < 0 || kind >= Kinds || material < 0 || material >= MaterialNames.Length) return null;
            return _snd[kind, material];
        }

        public static string MaterialName(int m) => m >= 0 && m < MaterialNames.Length ? MaterialNames[m] : "unknown";

        /// <summary>The ground under a point: its height and Halo material (-1 unknown). False when nothing is within reach.</summary>
        public static bool Ground(Vector3 feet, out float y, out int material)
        {
            y = 0f; material = -1;
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.5f, Vector3.down, Hits, 2.5f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore);
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < n; i++) if (Hits[i].distance < bd) { bd = Hits[i].distance; best = i; }
            if (best < 0) return false;
            y = Hits[best].point.y;
            try { material = HaloWorld.SurfaceMaterial(Hits[best]); } catch (Exception) { material = -1; }
            return true;
        }

        /// <summary>One step of a kind at a point (material from the ground there). <paramref name="own"/>: the local player's.</summary>
        public static void Step(Vector3 feet, int kind, bool own, float gain)
        {
            float y; int m;
            if (!Ground(feet, out y, out m)) m = -1;
            StepOn(feet, kind, m, own, gain);
        }

        static readonly Dictionary<HaloTag, HaloTag[,]> Tables = new Dictionary<HaloTag, HaloTag[,]>();

        /// <summary>A campaign biped's own step (grunts, elites, flood sound like themselves): kind on the ground under the feet from
        /// the biped's footsteps tag (bipd +908, e.g. globals\grunt.foot); null tag = the MP cyborg table. Positional.</summary>
        public static void Step(Vector3 feet, int kind, HaloTag bipedOrFootTag, float gain)
        {
            if (!Enabled || kind < 0 || kind >= Kinds) return;
            HaloTag foot = bipedOrFootTag;
            if (foot != null && foot.ClassName == "bipd") { try { foot = foot.Root.Ref(908); } catch (Exception) { foot = null; } }
            if (foot == null || foot.ClassName != "foot") { Step(feet, kind, false, gain); return; }
            HaloTag[,] t;
            if (!Tables.TryGetValue(foot, out t))
            {
                t = new HaloTag[Kinds, MaterialNames.Length];
                try
                {
                    int k = 0;
                    foreach (var e in foot.Root.Block(0, 28))
                    {
                        if (k >= Kinds) break;
                        int m = 0;
                        foreach (var mt in e.Block(0, 48)) { if (m >= MaterialNames.Length) break; t[k, m] = mt.Ref(16); m++; }
                        k++;
                    }
                }
                catch (Exception) { }
                if (Tables.Count > 64) Tables.Clear();
                Tables[foot] = t;
            }
            float y; int mat;
            if (!Ground(feet, out y, out mat) || mat < 0) mat = 2;
            var snd = t[kind, mat];
            if (snd == null) { Silent++; return; }
            Played++;
            HaloAudio.PlayAt(snd, feet, gain, 1f, 1f);
        }

        static void StepOn(Vector3 feet, int kind, int material, bool own, float gain)
        {
            if (!Enabled) return;
            if (material < 0) material = 2;   // a collider without a Halo material: stone // ours
            var snd = SoundFor(kind, material);
            if (kind == Land) Lands++; else if (kind == Jump) Jumps++;
            if (snd == null) { Silent++; Note(kind, material, own, null); return; }
            Played++;
            HaloAudio.PlayAt(snd, feet, gain * (own ? OwnGain : 1f), 1f, 1f);
            var fx = _fx[kind, material];
            if (fx != null) { try { HaloFx.Play(fx, feet, Quaternion.identity); } catch (Exception) { } }
            Note(kind, material, own, snd);
        }

        static void Note(int kind, int material, bool own, HaloTag snd)
        {
            int key = (own ? 1 : 0) | kind << 1 | material << 4;
            if (!Logged.Add(key)) return;
            HaloLog.Info("Audio: step " + (own ? "own " : "remote ") + KindNames[kind] + " on " + MaterialName(material) + " -> " + (snd != null ? snd.Name : "(no sound in the tag)"));
        }

        /// <summary>Once a frame (HaloAudioSystem): the local player and the remote players.</summary>
        internal static void Tick(float dt)
        {
            if (!Enabled || !HaloSession.InMatch || !HaloWorld.Ready || !Load(HaloSession.Map))
            {
                _own.Hold(); for (int i = 0; i < Remote.Length; i++) if (Remote[i] != null) Remote[i].Hold();
                return;
            }
            Vector3 feet = Vector3.zero;
            if (HaloLocal.Alive && HaloLocal.Vehicle == null && HaloPlayer.TryFeet(out feet))
            {
                var cam = HaloPlayer.Head();
                _own.Update(feet, cam != null ? cam.transform.position : feet + Vector3.up * 1.6f, dt, false);
            }
            else _own.Hold();

            Vector3 ear = HaloAudioSystem.Instance != null ? HaloAudioSystem.Instance.ListenerPosition() : feet;
            var players = HaloNetGame.Players;
            int count = players != null ? players.Count : 0;
            for (int i = 0; i < count; i++)
            {
                var p = players[i];
                if (p == null || p.Slot < 0 || p.Slot >= Remote.Length) continue;
                var w = Remote[p.Slot];
                bool on = p.InMatch && p.Alive && p.VehicleIndex < 0 && (p.Position - ear).sqrMagnitude < RemoteRange * RemoteRange;
                if (!on) { if (w != null) w.Hold(); continue; }
                if (w == null) { w = new Walker(); Remote[p.Slot] = w; }   // once per slot
                w.Update(p.Position, p.Head, dt, p.Crouch);
            }
        }

        /// <summary>
        /// Turns one person's movement (feet, head) into steps: a step every stride of horizontal distance, run above 3.2 m/s (walk
        /// below 2.6), none in the air; a push-off when a jump starts, a landing after a fall. The avatar root may move in jerks or not
        /// at all (some client builds keep it as an anchor): the distance is summed per frame, the speed taken over 0.1 s, and when the
        /// root stays put while the head walks, the head is used (<see cref="UsingHead"/>).
        /// </summary>
        public sealed class Walker
        {
            public const float MinSpeed = 0.35f, TeleportMetres = 2.5f, AirGap = 0.32f, LandGap = 0.2f;
            public bool Own;
            public float Speed, LastStride;
            public bool UsingHead, Airborne;
            public int Steps, LandCount, JumpCount, Kind;

            Vector3 _last, _lastHead, _lastGround;
            bool _have, _moving;
            float _acc, _winDist, _winTime, _still, _rootWin, _headWin, _clock;
            float _gap, _gapRef = float.NaN, _apexY, _airTime, _stillAir;
            int _airFrames, _fullWindows, _groundMaterial = -1;

            public void Hold() { _have = false; }

            void Begin(Vector3 feet, Vector3 head)
            {
                _last = feet; _lastHead = head; _lastGround = feet; _have = true; _moving = false;
                _acc = _winDist = _winTime = _still = _rootWin = _headWin = _clock = 0f;
                _fullWindows = 0; Speed = 0f; Airborne = false; _airFrames = 0; _gapRef = float.NaN; _gap = 0f;
            }

            public static float StrideFor(float speed, bool crouch)
            {
                if (crouch) return HaloCrouchStride;
                float t = Mathf.Clamp01((speed - WalkSpeed) / (HaloRunSpeed - WalkSpeed));
                return Mathf.Lerp(WalkStride, HaloRunStride, t);
            }

            public void Update(Vector3 feet, Vector3 head, float dt, bool crouch)
            {
                if (!_have || dt <= 0f) { Begin(feet, head); return; }
                Vector3 d = feet - _last, dh = head - _lastHead;
                _last = feet; _lastHead = head;
                float dy = d.y;
                d.y = 0f; dh.y = 0f;
                float rootD = d.magnitude, headD = dh.magnitude;
                if (rootD > TeleportMetres) { Begin(feet, head); return; }   // a teleport / respawn
                if (headD > TeleportMetres) headD = 0f;

                if (Own)
                {
                    _rootWin += rootD; _headWin += headD; _clock += dt;
                    if (_clock >= 1f)
                    {
                        if (UsingHead) { if (_rootWin > 0.5f) UsingHead = false; }
                        else if (_headWin > 1.2f && _rootWin < 0.2f * _headWin) UsingHead = true;
                        _rootWin = _headWin = _clock = 0f;
                    }
                }
                float dist = UsingHead ? headD : rootD;
                _winDist += dist; _winTime += dt;
                if (_winTime >= 0.1f)
                {
                    Speed = _winDist / _winTime;
                    _winDist = _winTime = 0f;
                    _fullWindows = Speed >= MinSpeed ? _fullWindows + 1 : 0;
                }

                // the ground: looked at only when something moves
                float vy = dy / dt;
                if (Airborne || dist > 0.002f || Mathf.Abs(dy) > 0.004f || float.IsNaN(_gapRef))
                {
                    float gy; int gm;
                    bool found = Ground(feet, out gy, out gm);
                    if (found && gm >= 0) _groundMaterial = gm;   // a collider without a Halo material keeps the last known one
                    _gap = found ? feet.y - gy : 9f;
                    if (float.IsNaN(_gapRef)) _gapRef = found ? Mathf.Clamp(_gap, -0.3f, 1.2f) : 0f;
                    else if (!Airborne && !found && Mathf.Abs(vy) < 0.3f) _gap = _gapRef;
                }

                if (!Airborne)
                {
                    if (Mathf.Abs(vy) < 0.3f) _gapRef = Mathf.Lerp(_gapRef, Mathf.Clamp(_gap, -0.3f, 1.2f), 0.05f);
                    bool lifted = _gap > _gapRef + AirGap && (vy > 0.8f || vy < -1.5f);
                    _airFrames = lifted ? _airFrames + 1 : 0;
                    if (_airFrames >= 2)
                    {
                        Airborne = true; _airTime = 0f; _stillAir = 0f; _apexY = feet.y;
                        if (vy > 1.2f) { JumpCount++; HaloFootsteps.Step(_lastGround, Jump, Own, 1f); }
                    }
                    else _lastGround = feet;
                }
                else
                {
                    _airTime += dt;
                    if (feet.y > _apexY) _apexY = feet.y;
                    _stillAir = Mathf.Abs(vy) < 0.2f ? _stillAir + dt : 0f;
                    if (_gap < _gapRef + LandGap && vy <= 0.5f)
                    {
                        Airborne = false;
                        float fall = _apexY - feet.y;
                        if (_airTime >= 0.1f && fall >= 0.12f)
                        {
                            LandCount++;
                            StepOn(feet, Land, _groundMaterial, Own, Mathf.Clamp(0.6f + fall / 1.5f, 0.6f, 1.4f));   // a hop is softer than a drop
                        }
                        _airFrames = 0; _moving = false;
                        return;
                    }
                    if (_stillAir > 0.3f) { Airborne = false; _airFrames = 0; }   // hovering, not a fall
                    return;
                }

                if (Speed < MinSpeed)
                {
                    _still += dt;
                    if (_still > 0.25f) { _acc = 0f; _moving = false; }
                    return;
                }
                _still = 0f;
                if (_fullWindows < 2) return;
                // Halo's foot effect: run at running pace, walk below (with a gap so the boundary does not flip)
                if (Kind == Run) { if (Speed < 2.6f || crouch) Kind = Walk; }
                else if (Speed > 3.2f && !crouch) Kind = Run;
                float stride = StrideFor(Speed, crouch);
                LastStride = stride;
                if (!_moving) { _moving = true; _acc = stride * 0.55f; }   // the first foot comes down soon after setting off
                _acc += dist;
                if (_acc < stride) return;
                _acc -= stride;
                if (_acc > stride) _acc = 0f;   // a frame hitch: no burst
                Steps++;
                StepOn(feet, Kind, _groundMaterial, Own, 1f);
            }
        }

        /// <summary>The local player's walker (lab / log).</summary>
        public static Walker Local => _own;

        public static string Stats() => Played + " steps played, " + Silent + " silent (no sound for the material), " + Jumps + " jumps, " + Lands + " landings; own: "
            + _own.Steps + " steps, speed " + _own.Speed.ToString("0.0") + " m/s, stride " + _own.LastStride.ToString("0.00") + " m" + (_own.UsingHead ? " (by the head)" : "");
    }
}

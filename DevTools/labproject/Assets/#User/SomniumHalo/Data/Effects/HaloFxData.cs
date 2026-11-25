using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>An ARGB colour as Halo stores it (alpha first), 0..1.</summary>
    public struct FxColor
    {
        public float A, R, G, B;
        public FxColor(float a, float r, float g, float b) { A = a; R = r; G = g; B = b; }
        public static FxColor Read(TagReader r, int o) { return new FxColor(r.F32(o), r.F32(o + 4), r.F32(o + 8), r.F32(o + 12)); }
        public static FxColor ReadRgb(TagReader r, int o) { return new FxColor(1f, r.F32(o), r.F32(o + 4), r.F32(o + 8)); }
        public static FxColor Lerp(FxColor a, FxColor b, float t) { return new FxColor(a.A + (b.A - a.A) * t, a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t); }
        public override string ToString() { return "(" + A.ToString("0.##") + "," + R.ToString("0.##") + "," + G.ToString("0.##") + "," + B.ToString("0.##") + ")"; }
    }

    // ------------------------------------------------------------------ sprites

    /// <summary>One frame of a sprite sequence: which image of the bitmap, its uv rectangle (Halo / Direct3D: v = 0 at the top, which is
    /// also where Unity's raw upload puts the first row, so these are used as they are), the registration point (where the particle's
    /// position sits, in uv units from the frame's top-left) and its size in pixels.</summary>
    public sealed class FxFrame
    {
        public int Image;
        public float U0, V0, U1, V1, RegU, RegV;
        public float PxW, PxH;
        /// <summary>A whole image (not a sprite rectangle): a particle of radius r draws it r wide on each side whatever its pixel size.</summary>
        public bool Whole;
    }

    public sealed class FxSequence { public FxFrame[] Frames; }

    /// <summary>The frames of a bitmap tag, by sequence. Sprite bitmaps (type 3) carry rectangles in sheets; plain bitmaps use whole images.</summary>
    public sealed class FxSpriteSheet
    {
        public HaloTag Bitmap;
        public FxSequence[] Sequences;
        public int Images;
        /// <summary>Per image: the format has no alpha channel (X8R8G8B8, R5G6B5, Y8, P8). An "alpha blend" particle on such an image
        /// would draw its whole square: those are added instead (the lens flare sprites of explosions).</summary>
        public bool[] NoAlpha;
        /// <summary>1 / the largest frame's pixel extent: a particle of radius r draws its largest frame 2r wide (Halo's cached "sprite size").</summary>
        public float PixelScale;
        /// <summary>Free slot for the Unity side (textures, batches). Never touched by Data code.</summary>
        public object Gpu;

        public FxSequence Sequence(int i)
        {
            if (Sequences.Length == 0) return null;
            if (i < 0) i = 0;
            if (i >= Sequences.Length) i = Sequences.Length - 1;
            return Sequences[i];
        }

        public static FxSpriteSheet Read(HaloTag bitmap)
        {
            var bm = new HaloBitmap(bitmap);
            var sheet = new FxSpriteSheet { Bitmap = bitmap, Images = bm.Images.Count };
            sheet.NoAlpha = new bool[bm.Images.Count];
            for (int i = 0; i < bm.Images.Count; i++)
            {
                var f = bm.Images[i].Format;
                sheet.NoAlpha[i] = f == HaloBitmap.Format.X8R8G8B8 || f == HaloBitmap.Format.R5G6B5 || f == HaloBitmap.Format.Y8 || f == HaloBitmap.Format.P8Bump;
            }
            var seqs = new List<FxSequence>();
            float maxPx = 1f;
            foreach (var s in bm.Sequences)
            {
                var frames = new List<FxFrame>();
                if (s.Sprites.Count > 0)
                {
                    foreach (var p in s.Sprites)
                    {
                        if (p.Bitmap < 0 || p.Bitmap >= bm.Images.Count) continue;
                        var img = bm.Images[p.Bitmap];
                        var f = new FxFrame
                        {
                            Image = p.Bitmap, U0 = p.Left, U1 = p.Right, V0 = p.Top, V1 = p.Bottom, RegU = p.RegX, RegV = p.RegY,
                            PxW = (p.Right - p.Left) * img.Width, PxH = (p.Bottom - p.Top) * img.Height,
                        };
                        frames.Add(f);
                    }
                }
                else
                {
                    for (int i = s.First; i < s.First + s.Count && i < bm.Images.Count; i++) frames.Add(WholeImage(bm, i));
                }
                if (frames.Count > 0) seqs.Add(new FxSequence { Frames = frames.ToArray() });
            }
            if (seqs.Count == 0 && bm.Images.Count > 0)
            {
                var frames = new FxFrame[bm.Images.Count];
                for (int i = 0; i < frames.Length; i++) frames[i] = WholeImage(bm, i);
                seqs.Add(new FxSequence { Frames = frames });
            }
            foreach (var q in seqs) foreach (var f in q.Frames) maxPx = Math.Max(maxPx, Math.Max(f.PxW, f.PxH));
            sheet.Sequences = seqs.ToArray();
            sheet.PixelScale = 1f / maxPx;
            return sheet;
        }

        static FxFrame WholeImage(HaloBitmap bm, int i)
        {
            var img = bm.Images[i];
            float ru = img.Width > 0 && (img.RegX != 0 || img.RegY != 0) ? img.RegX / (float)img.Width : 0.5f;
            float rv = img.Height > 0 && (img.RegX != 0 || img.RegY != 0) ? img.RegY / (float)img.Height : 0.5f;
            return new FxFrame { Image = i, U0 = 0, V0 = 0, U1 = 1, V1 = 1, RegU = ru, RegV = rv, PxW = img.Width, PxH = img.Height, Whole = true };
        }
    }

    // ------------------------------------------------------------------ effect (effe)

    public sealed class FxEffectDef
    {
        public HaloTag Tag;
        public uint Flags;
        public int LoopStart = -1, LoopStop = -1;
        public string[] Locations;
        public FxEventDef[] Events;
        public int MaxEmits;
        public bool DeletedWhenDetached { get { return (Flags & 1) != 0; } }
        public bool Loops { get { return LoopStart >= 0 && LoopStart < Events.Length; } }
    }

    public sealed class FxEventDef
    {
        public float Skip, DelayMin, DelayMax, DurMin, DurMax;
        public FxPartDef[] Parts;
        public FxEmitDef[] Emits;
    }

    /// <summary>A part of an effect event: one tag created at a location (sound, light, decal, particle system, damage, object).</summary>
    public sealed class FxPartDef
    {
        public int CreateIn, Violence, Location, Flags;
        public string Class;
        public HaloTag Tag;
        public float VelMin, VelMax, Cone, AngMin, AngMax, RadModMin, RadModMax;
        /// <summary>Flag 0: decals face straight down whatever the location says.</summary>
        public bool FaceDown { get { return (Flags & 1) != 0; } }
    }

    /// <summary>A particles entry of an effect event (EffectParticle).</summary>
    public sealed class FxEmitDef
    {
        public int CreateIn, Violence, Camera, Location, Distribution;
        public HVec3 Direction, Offset;
        public HaloTag ParticleTag;
        public FxParticleDef Particle;
        public uint Flags;
        public int CountMin, CountMax;
        public float DistRadMin, DistRadMax, VelMin, VelMax, Cone, AngVelMin, AngVelMax, RadMin, RadMax;
        public FxColor TintLo, TintHi;
        public bool StayAttached { get { return (Flags & 1) != 0; } }
        public bool RandomAngle { get { return (Flags & 2) != 0; } }
        public bool TintFromObject { get { return (Flags & 4) != 0; } }
    }

    // ------------------------------------------------------------------ particle (part) + point physics (pphy)

    public sealed class FxPhysicsDef
    {
        public HaloTag Tag;
        public uint Flags;
        public float Mass, AirGravity, Density, AirFriction, SurfaceFriction, Elasticity;
        public bool Collides { get { return (Flags & 2) != 0; } }
        public bool NoGravity { get { return (Flags & 32) != 0; } }
        /// <summary>Quadratic air drag per world unit of particle radius: a = -Drag / r * |v| v (see the runtime). friction / mass, both as the cache stores them.</summary>
        public float Drag { get { return Mass > 0f ? AirFriction / Mass : 0f; } }

        public static FxPhysicsDef Vacuum = new FxPhysicsDef { Flags = 32 };
    }

    public sealed class FxParticleDef
    {
        public HaloTag Tag;
        public uint Flags;
        public FxSpriteSheet Sheet;
        public FxPhysicsDef Physics;
        public float LifeMin, LifeMax, FadeIn, FadeOut;
        public HaloTag CollisionEffect, DeathEffect;
        public float RadAnim0, RadAnim1, AnimMin, AnimMax;
        public int FirstSeq, InitialSeq, LoopSeq, FinalSeq;
        public float SpriteSize;
        public int Orientation, ShaderFlags, Blend, FadeMode;
        public bool CanAnimateBackwards { get { return (Flags & 1) != 0; } }
        public bool AnimStopsAtRest { get { return (Flags & 2) != 0; } }
        public bool RandomStartFrame { get { return (Flags & 4) != 0; } }
        public bool AnimateOncePerFrame { get { return (Flags & 8) != 0; } }
        public bool DiesAtRest { get { return (Flags & 16) != 0; } }
        public bool DiesOnStructure { get { return (Flags & 32) != 0; } }
        public bool DiesInAir { get { return (Flags & 256) != 0; } }
        public bool SelfIlluminated { get { return (Flags & 512) != 0; } }
        public bool MirrorH { get { return (Flags & 1024) != 0; } }
        public bool MirrorV { get { return (Flags & 2048) != 0; } }
    }

    // ------------------------------------------------------------------ particle system (pctl)

    public sealed class FxSystemDef
    {
        public HaloTag Tag;
        public FxPhysicsDef Physics;
        public int UpdatePhysics;
        public FxSystemType[] Types;
    }

    public sealed class FxSystemType
    {
        public string Name;
        public uint Flags;
        public int Initial, CreationPhysics;
        public float Radius;
        public float[] Constants;
        public FxSystemState[] States;
        public FxSystemParticleState[] ParticleStates;
        public bool NotInFirstPerson { get { return (Flags & 0x10000) != 0; } }
        public bool NotInThirdPerson { get { return (Flags & 0x20000) != 0; } }
        public bool Disabled { get { return (Flags & 0x100) != 0; } }
        public float K(int i, float fallback) { return Constants != null && i < Constants.Length ? Constants[i] : fallback; }
    }

    public sealed class FxSystemState
    {
        public string Name;
        public float DurMin, DurMax, TransMin, TransMax, Scale, AnimMul, RotMul, RadiusMul, MinCount, Rate;
        public FxColor Color;
    }

    public sealed class FxSystemParticleState
    {
        public string Name;
        public float DurMin, DurMax, TransMin, TransMax, ScaleMin, ScaleMax, AnimMin, AnimMax, RotMin, RotMax, RadiusMul;
        public FxSpriteSheet Sheet;
        public int Sequence, ShaderFlags, Blend;
        public FxColor C1, C2;
        public FxPhysicsDef Physics;
    }

    // ------------------------------------------------------------------ contrail (cont), light (ligh), decal (deca)

    public sealed class FxContrailDef
    {
        public HaloTag Tag;
        public int Flags, ScaleFlags, RenderType, Blend, FadeMode, FirstSeq, SeqCount;
        public float Rate, VelMin, VelMax, Cone, Inherit, RepU, RepV, AnimU, AnimV, AnimRate;
        public FxSpriteSheet Sheet;
        public FxContrailState[] States;
        public bool FirstPointUnfaded { get { return (Flags & 1) != 0; } }
        public bool LastPointUnfaded { get { return (Flags & 2) != 0; } }
    }

    public sealed class FxContrailState
    {
        public float DurMin, DurMax, TransMin, TransMax, Width;
        public FxPhysicsDef Physics;
        public FxColor ColLo, ColHi;
    }

    public sealed class FxLightDef
    {
        public HaloTag Tag;
        public uint Flags;
        public float Radius, RadModMin, RadModMax, Duration;
        public int Falloff;
        public FxColor ColLo, ColHi;
        /// <summary>The light's cutoff angle (radians; pi = all around). (Its lens flare has its own falloff / cutoff angles.)</summary>
        public float CutoffAngle;
        /// <summary>The light's lens flare (+172): what Halo DRAWS of a light - the plasma bolts are only this glow (plus a contrail).</summary>
        public FxLensDef Lens;
        public bool Dynamic { get { return (Flags & 1) != 0; } }
    }

    /// <summary>One reflection (glow) of a lens flare tag (LensFlareReflection, 128 bytes).</summary>
    public sealed class FxLensReflection
    {
        public int Flags, Bitmap, RadScaledBy, BriScaledBy;
        public float Position, RotOffset, RadMin, RadMax, BriMin, BriMax;
        /// <summary>ARGB; alpha 0 = take the light's colour.</summary>
        public FxColor Tint;
    }

    /// <summary>A lens flare tag ("lens", 240 bytes): bitmap, fades, angles, reflections.</summary>
    public sealed class FxLensDef
    {
        public HaloTag Tag;
        public float CosFalloff, CosCutoff, OcclusionRadius, NearFade, FarFade, HScale, VScale;
        public int Flags;
        public FxSpriteSheet Sheet;
        public FxLensReflection[] Reflections;
        public bool OnlyFirstPerson { get { return (Flags & 4) != 0; } }
        public bool OnlyThirdPerson { get { return (Flags & 8) != 0; } }
    }

    /// <summary>A light volume ("mgs2", 332 bytes): an object widget (Object +332) - a row of <see cref="Count"/> glow sprites along the
    /// object's forward axis. The plasma pistol / plasma rifle / needle / ghost / banshee bolts and the rocket's exhaust are drawn by these
    /// (the bolt's visible body from every angle; its light's lens flare only shows within the lens's cutoff angle).</summary>
    public sealed class FxLightVolumeDef
    {
        public HaloTag Tag;
        public string Marker;
        public float NearFade, FarFade, Perpendicular, Parallel;
        /// <summary>FunctionOut (0 none, 1..4 = the object's A..D function outputs).</summary>
        public int BrightnessSource, FrameSource, Sequence, Count;
        /// <summary>The volume's map, else the globals' rasterizer "glow" bitmap (Halo's default for a light volume without a map).</summary>
        public FxSpriteSheet Sheet;
        public FxLightVolumeFrame[] Frames;
    }

    /// <summary>One frame of a light volume (LightVolumeFrame, 176 bytes). Sprite i of n sits at t = i / (n - 1) from "hither" (t 0) to
    /// "yon" (t 1): offset + length * t^offset exponent world units along the forward axis, radius / colour / brightness interpolated by
    /// t^their exponents; the tint's alpha is the brightness.</summary>
    public sealed class FxLightVolumeFrame
    {
        public float Offset, OffsetExp, Length, RadHither, RadYon, RadExp, TintExp, BrightExp;
        public FxColor TintHither, TintYon;
    }

    public sealed class FxDecalDef
    {
        public HaloTag Tag;
        public int Flags, Type, Layer, Blend;
        public HaloTag Next;
        public float RadMin, RadMax, IntMin, IntMax, LifeMin, LifeMax, DecayMin, DecayMax;
        public FxColor ColLo, ColHi;
        public FxSpriteSheet Sheet;
        public bool NoRandomRotation { get { return (Flags & 8) != 0; } }
    }

    // ------------------------------------------------------------------ the library

    /// <summary>
    /// Reads and caches the effect tags of one map (Unity-free, thread-safe: <see cref="PreloadAll"/> can run on a worker while the main
    /// thread asks for single tags). Offsets from DevTools/reference/layouts.txt (Invader's definitions; cache-only fields - relative
    /// direction vector, sprite size, point physics mass / gravity scales - are read as the cache file has them computed).
    /// </summary>
    public sealed class HaloFxLibrary
    {
        public readonly HaloMap Map;
        readonly Dictionary<uint, object> _byTag = new Dictionary<uint, object>();
        readonly object _lock = new object();
        public int Count { get { lock (_lock) return _byTag.Count; } }

        static readonly uint C_effe = HaloMap.FourCC("effe"), C_part = HaloMap.FourCC("part"), C_pphy = HaloMap.FourCC("pphy"), C_pctl = HaloMap.FourCC("pctl"),
            C_cont = HaloMap.FourCC("cont"), C_ligh = HaloMap.FourCC("ligh"), C_deca = HaloMap.FourCC("deca"), C_bitm = HaloMap.FourCC("bitm"),
            C_mgs2 = HaloMap.FourCC("mgs2");

        readonly Func<HaloTag, FxEffectDef> _rEffect;
        readonly Func<HaloTag, FxParticleDef> _rParticle;
        readonly Func<HaloTag, FxPhysicsDef> _rPhysics;
        readonly Func<HaloTag, FxSystemDef> _rSystem;
        readonly Func<HaloTag, FxContrailDef> _rContrail;
        readonly Func<HaloTag, FxLightDef> _rLight;
        readonly Func<HaloTag, FxDecalDef> _rDecal;
        readonly Func<HaloTag, FxLightVolumeDef> _rVolume;

        public HaloFxLibrary(HaloMap map)
        {
            Map = map;
            _rEffect = ReadEffect; _rParticle = ReadParticle; _rPhysics = ReadPhysics; _rSystem = ReadSystem;
            _rContrail = ReadContrail; _rLight = ReadLight; _rDecal = ReadDecal; _rVolume = ReadVolume;
        }

        static HaloFxLibrary _current;
        /// <summary>The library of a map (one is kept: the match's map).</summary>
        public static HaloFxLibrary For(HaloMap map)
        {
            if (map == null) return null;
            var c = _current;
            if (c != null && c.Map == map) return c;
            lock (typeof(HaloFxLibrary))
            {
                if (_current == null || _current.Map != map) _current = new HaloFxLibrary(map);
                return _current;
            }
        }

        /// <summary>Parses every effect-related tag of the map (worker-thread friendly). Returns the number of tags.</summary>
        public int PreloadAll()
        {
            int n = 0;
            foreach (var t in Map.Tags)
            {
                if (t == null) continue;
                switch (t.ClassName)
                {
                    case "effe": if (Effect(t) != null) n++; break;
                    case "part": if (Particle(t) != null) n++; break;
                    case "pctl": if (System(t) != null) n++; break;
                    case "cont": if (Contrail(t) != null) n++; break;
                    case "ligh": if (Light(t) != null) n++; break;
                    case "deca": if (Decal(t) != null) n++; break;
                    case "mgs2": if (Volume(t) != null) n++; break;
                }
            }
            return n;
        }

        T Get<T>(HaloTag t, uint cls, Func<HaloTag, T> read) where T : class
        {
            if (t == null || t.Map != Map || t.Class != cls) return null;
            object o;
            lock (_lock) { if (_byTag.TryGetValue(t.Id, out o)) return o as T; }
            lock (_lock) { _byTag[t.Id] = null; } // in progress: a reference cycle (decal chains) reads as missing instead of looping
            T v = null;
            try { v = read(t); }
            catch (Exception) { v = null; }
            lock (_lock) { _byTag[t.Id] = v; }
            return v;
        }

        readonly Dictionary<uint, FxSpriteSheet> _sheets = new Dictionary<uint, FxSpriteSheet>();
        public FxSpriteSheet Sheet(HaloTag bitmap)
        {
            if (bitmap == null || bitmap.Class != C_bitm) return null;
            FxSpriteSheet s;
            lock (_lock) { if (_sheets.TryGetValue(bitmap.Id, out s)) return s; }
            try { s = FxSpriteSheet.Read(bitmap); } catch (Exception) { s = null; }
            if (s != null && s.Sequences.Length == 0) s = null;
            lock (_lock) { _sheets[bitmap.Id] = s; }
            return s;
        }

        public IEnumerable<FxSpriteSheet> Sheets() { lock (_lock) return new List<FxSpriteSheet>(_sheets.Values); }

        public FxEffectDef Effect(HaloTag t) { return Get(t, C_effe, _rEffect); }
        public FxParticleDef Particle(HaloTag t) { return Get(t, C_part, _rParticle); }
        public FxPhysicsDef Physics(HaloTag t) { return Get(t, C_pphy, _rPhysics); }
        public FxSystemDef System(HaloTag t) { return Get(t, C_pctl, _rSystem); }
        public FxContrailDef Contrail(HaloTag t) { return Get(t, C_cont, _rContrail); }
        public FxLightDef Light(HaloTag t) { return Get(t, C_ligh, _rLight); }
        public FxDecalDef Decal(HaloTag t) { return Get(t, C_deca, _rDecal); }
        public FxLightVolumeDef Volume(HaloTag t) { return Get(t, C_mgs2, _rVolume); }

        static int Index(int v) { return v == 0xFFFF ? -1 : (short)v; }

        FxEffectDef ReadEffect(HaloTag t)
        {
            var r = t.Root;
            var d = new FxEffectDef { Tag = t, Flags = r.U32(0), LoopStart = r.I16(4), LoopStop = r.I16(6) };
            var locs = new List<string>();
            foreach (var l in r.Block(40, 32)) locs.Add(l.Str32(0));
            d.Locations = locs.ToArray();
            var evs = new List<FxEventDef>();
            foreach (var e in r.Block(52, 68))
            {
                var ev = new FxEventDef { Skip = e.F32(4), DelayMin = e.F32(8), DelayMax = e.F32(12), DurMin = e.F32(16), DurMax = e.F32(20) };
                var parts = new List<FxPartDef>();
                foreach (var p in e.Block(44, 104))
                {
                    var pd = new FxPartDef
                    {
                        CreateIn = p.U16(0), Violence = p.U16(2), Location = p.I16(4), Flags = p.U16(6),
                        Class = HaloMap.FourCC(p.U32(20)), Tag = p.Ref(24),
                        VelMin = p.F32(64), VelMax = p.F32(68), Cone = p.F32(72), AngMin = p.F32(76), AngMax = p.F32(80),
                        RadModMin = p.F32(84), RadModMax = p.F32(88),
                    };
                    if (pd.Tag != null) { pd.Class = pd.Tag.ClassName; parts.Add(pd); }
                }
                ev.Parts = parts.ToArray();
                var emits = new List<FxEmitDef>();
                foreach (var p in e.Block(56, 232))
                {
                    var em = new FxEmitDef
                    {
                        CreateIn = p.U16(0), Violence = p.U16(2), Camera = p.U16(4), Location = p.I16(8),
                        Offset = p.Vec3(20), Direction = p.Vec3(32), ParticleTag = p.Ref(84), Flags = p.U32(100), Distribution = p.U16(104),
                        CountMin = p.I16(108), CountMax = p.I16(110), DistRadMin = p.F32(112), DistRadMax = p.F32(116),
                        VelMin = p.F32(132), VelMax = p.F32(136), Cone = p.F32(140), AngVelMin = p.F32(144), AngVelMax = p.F32(148),
                        RadMin = p.F32(160), RadMax = p.F32(164), TintLo = FxColor.Read(p, 176), TintHi = FxColor.Read(p, 192),
                    };
                    if (em.Direction.X == 0 && em.Direction.Y == 0 && em.Direction.Z == 0)
                    {
                        // the cache-only vector is missing: build it from the euler pair (yaw, pitch)
                        float yaw = p.F32(12), pitch = p.F32(16);
                        em.Direction = new HVec3((float)(Math.Cos(yaw) * Math.Cos(pitch)), (float)(Math.Sin(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch));
                    }
                    if (em.CountMax < em.CountMin) { int c = em.CountMax; em.CountMax = em.CountMin; em.CountMin = c; }
                    em.Particle = Particle(em.ParticleTag);
                    if (em.Particle != null) emits.Add(em);
                }
                ev.Emits = emits.ToArray();
                d.MaxEmits = Math.Max(d.MaxEmits, ev.Emits.Length);
                evs.Add(ev);
            }
            d.Events = evs.ToArray();
            if (d.LoopStart >= d.Events.Length) d.LoopStart = -1;
            if (d.LoopStop >= d.Events.Length) d.LoopStop = d.Events.Length - 1;
            // pre-read what the parts reference so a first play does not parse
            foreach (var ev in d.Events)
                foreach (var p in ev.Parts)
                {
                    switch (p.Class)
                    {
                        case "ligh": Light(p.Tag); break;
                        case "deca": Decal(p.Tag); break;
                        case "pctl": System(p.Tag); break;
                        case "effe": break; // nested effects are read on first use (no recursion loops)
                    }
                }
            return d;
        }

        FxPhysicsDef ReadPhysics(HaloTag t)
        {
            var r = t.Root;
            return new FxPhysicsDef
            {
                Tag = t, Flags = r.U32(0), Mass = r.F32(4), AirGravity = r.F32(12), Density = r.F32(32), AirFriction = r.F32(36),
                SurfaceFriction = r.F32(44), Elasticity = r.F32(48),
            };
        }

        FxPhysicsDef PhysicsOrVacuum(HaloTag t) { return Physics(t) ?? FxPhysicsDef.Vacuum; }

        FxParticleDef ReadParticle(HaloTag t)
        {
            var r = t.Root;
            var d = new FxParticleDef
            {
                Tag = t, Flags = r.U32(0), Sheet = Sheet(r.Ref(4)), Physics = PhysicsOrVacuum(r.Ref(20)),
                LifeMin = r.F32(56), LifeMax = r.F32(60), FadeIn = r.F32(64), FadeOut = r.F32(68),
                CollisionEffect = r.Ref(72), DeathEffect = r.Ref(88),
                RadAnim0 = r.F32(116), RadAnim1 = r.F32(120), AnimMin = r.F32(128), AnimMax = r.F32(132),
                FirstSeq = r.I16(152), InitialSeq = r.I16(154), LoopSeq = r.I16(156), FinalSeq = r.I16(158),
                SpriteSize = r.F32(168), Orientation = r.U16(172), ShaderFlags = r.U16(216), Blend = r.U16(218), FadeMode = r.U16(220),
            };
            if (d.RadAnim0 == 0 && d.RadAnim1 == 0) { d.RadAnim0 = 1; d.RadAnim1 = 1; }
            if (d.LifeMax < d.LifeMin) { float x = d.LifeMin; d.LifeMin = d.LifeMax; d.LifeMax = x; }
            if (d.FirstSeq < 0) d.FirstSeq = 0;
            if (d.Sheet == null) return null;
            return d;
        }

        FxSystemDef ReadSystem(HaloTag t)
        {
            var r = t.Root;
            var d = new FxSystemDef { Tag = t, Physics = PhysicsOrVacuum(r.Ref(56)), UpdatePhysics = r.U16(72) };
            var types = new List<FxSystemType>();
            foreach (var ty in r.Block(92, 128))
            {
                var st = new FxSystemType
                {
                    Name = ty.Str32(0), Flags = ty.U32(32), Initial = ty.U16(36), Radius = ty.F32(44), CreationPhysics = ty.U16(84),
                };
                var k = new List<float>();
                foreach (var c in ty.Block(92, 4)) k.Add(c.F32(0));
                st.Constants = k.ToArray();
                var states = new List<FxSystemState>();
                foreach (var s in ty.Block(104, 192))
                    states.Add(new FxSystemState
                    {
                        Name = s.Str32(0), DurMin = s.F32(32), DurMax = s.F32(36), TransMin = s.F32(40), TransMax = s.F32(44),
                        Scale = s.F32(52), AnimMul = s.F32(56), RotMul = s.F32(60), Color = FxColor.Read(s, 64), RadiusMul = s.F32(80),
                        MinCount = s.F32(84), Rate = s.F32(88),
                    });
                st.States = states.ToArray();
                var ps = new List<FxSystemParticleState>();
                foreach (var p in ty.Block(116, 376))
                    ps.Add(new FxSystemParticleState
                    {
                        Name = p.Str32(0), DurMin = p.F32(32), DurMax = p.F32(36), TransMin = p.F32(40), TransMax = p.F32(44),
                        Sheet = Sheet(p.Ref(48)), Sequence = p.I16(64), ScaleMin = p.F32(72), ScaleMax = p.F32(76),
                        AnimMin = p.F32(80), AnimMax = p.F32(84), RotMin = p.F32(88), RotMax = p.F32(92),
                        C1 = FxColor.Read(p, 96), C2 = FxColor.Read(p, 112), RadiusMul = p.F32(128), Physics = PhysicsOrVacuum(p.Ref(132)),
                        ShaderFlags = p.U16(224), Blend = p.U16(226),
                    });
                st.ParticleStates = ps.ToArray();
                if (st.ParticleStates.Length > 0 && st.States.Length > 0) types.Add(st);
            }
            d.Types = types.ToArray();
            return d;
        }

        FxContrailDef ReadContrail(HaloTag t)
        {
            var r = t.Root;
            var d = new FxContrailDef
            {
                Tag = t, Flags = r.U16(0), ScaleFlags = r.U16(2), Rate = r.F32(4), VelMin = r.F32(8), VelMax = r.F32(12), Cone = r.F32(16),
                Inherit = r.F32(20), RenderType = r.U16(24), RepU = r.F32(28), RepV = r.F32(32), AnimU = r.F32(36), AnimV = r.F32(40),
                AnimRate = r.F32(44), Sheet = Sheet(r.Ref(48)), FirstSeq = r.I16(64), SeqCount = r.I16(66), Blend = r.U16(174), FadeMode = r.U16(176),
            };
            var states = new List<FxContrailState>();
            foreach (var s in r.Block(312, 104))
                states.Add(new FxContrailState
                {
                    DurMin = s.F32(0), DurMax = s.F32(4), TransMin = s.F32(8), TransMax = s.F32(12), Physics = Physics(s.Ref(16)),
                    Width = s.F32(64), ColLo = FxColor.Read(s, 68), ColHi = FxColor.Read(s, 84),
                });
            d.States = states.ToArray();
            if (d.RepU == 0) d.RepU = 1;
            if (d.RepV == 0) d.RepV = 1;
            if (d.States.Length == 0 || d.Sheet == null) return null;
            return d;
        }

        FxLightDef ReadLight(HaloTag t)
        {
            var r = t.Root;
            var d = new FxLightDef
            {
                Tag = t, Flags = r.U32(0), Radius = r.F32(4), RadModMin = r.F32(8), RadModMax = r.F32(12),
                ColLo = FxColor.Read(r, 56), ColHi = FxColor.Read(r, 72), Duration = r.F32(244), Falloff = r.U16(250),
                CutoffAngle = r.F32(20),
            };
            if (d.RadModMin == 0 && d.RadModMax == 0) { d.RadModMin = 1; d.RadModMax = 1; }
            try { d.Lens = ReadLens(r.Ref(172)); } catch (Exception) { d.Lens = null; }
            return d;
        }

        /// <summary>A lens flare tag (offsets: layouts.txt LensFlare / LensFlareReflection). Null without a bitmap or reflections.</summary>
        FxLensDef ReadLens(HaloTag t)
        {
            if (t == null || !t.Is("lens")) return null;
            var r = t.Root;
            var d = new FxLensDef
            {
                Tag = t, CosFalloff = r.F32(8), CosCutoff = r.F32(12), OcclusionRadius = r.F32(16), NearFade = r.F32(24), FarFade = r.F32(28),
                Sheet = Sheet(r.Ref(32)), Flags = r.U16(48), HScale = r.F32(160), VScale = r.F32(164),
            };
            if (d.HScale <= 0f) d.HScale = 1f;
            if (d.VScale <= 0f) d.VScale = 1f;
            var list = new List<FxLensReflection>();
            foreach (var f in r.Block(196, 128))
                list.Add(new FxLensReflection
                {
                    Flags = f.U16(0), Bitmap = f.I16(4), Position = f.F32(28), RotOffset = f.F32(32), RadMin = f.F32(40), RadMax = f.F32(44),
                    RadScaledBy = f.U16(48), BriMin = f.F32(52), BriMax = f.F32(56), BriScaledBy = f.U16(60), Tint = FxColor.Read(f, 64),
                });
            d.Reflections = list.ToArray();
            if (d.Sheet == null || d.Reflections.Length == 0) return null;
            return d;
        }

        /// <summary>A light volume tag (layouts.txt LightVolume / LightVolumeFrame). Null without frames or a bitmap.</summary>
        FxLightVolumeDef ReadVolume(HaloTag t)
        {
            var r = t.Root;
            var d = new FxLightVolumeDef
            {
                Tag = t, Marker = r.Str32(0), NearFade = r.F32(52), FarFade = r.F32(56), Perpendicular = r.F32(60), Parallel = r.F32(64),
                BrightnessSource = r.U16(68), Sequence = r.I16(108), Count = r.I16(110), FrameSource = r.U16(184),
            };
            d.Sheet = Sheet(r.Ref(92)) ?? GlowSheet();
            var frames = new List<FxLightVolumeFrame>();
            foreach (var f in r.Block(288, 176))
                frames.Add(new FxLightVolumeFrame
                {
                    Offset = f.F32(16), OffsetExp = f.F32(20), Length = f.F32(24), RadHither = f.F32(60), RadYon = f.F32(64), RadExp = f.F32(68),
                    TintHither = FxColor.Read(f, 104), TintYon = FxColor.Read(f, 120), TintExp = f.F32(136), BrightExp = f.F32(140),
                });
            d.Frames = frames.ToArray();
            if (d.Count <= 0 || d.Frames.Length == 0 || d.Sheet == null) return null;
            return d;
        }

        FxSpriteSheet _glow; bool _glowRead;
        /// <summary>The globals tag's rasterizer data "glow" bitmap (matg +308 block, +96): rasterizer\glow, a 64x64 round linear falloff.</summary>
        FxSpriteSheet GlowSheet()
        {
            if (_glowRead) return _glow;
            _glowRead = true;
            foreach (var g in Map.OfClass("matg"))
            {
                foreach (var rd in g.Root.Block(308, 428)) { _glow = Sheet(rd.Ref(96)); break; }
                break;
            }
            return _glow;
        }

        FxDecalDef ReadDecal(HaloTag t)
        {
            var r = t.Root;
            var d = new FxDecalDef
            {
                Tag = t, Flags = r.U16(0), Type = r.U16(2), Layer = r.U16(4), Next = r.Ref(8), RadMin = r.F32(24), RadMax = r.F32(28),
                IntMin = r.F32(44), IntMax = r.F32(48), ColLo = FxColor.ReadRgb(r, 52), ColHi = FxColor.ReadRgb(r, 64),
                LifeMin = r.F32(120), LifeMax = r.F32(124), DecayMin = r.F32(128), DecayMax = r.F32(132), Blend = r.U16(192),
                Sheet = Sheet(r.Ref(216)),
            };
            if (d.IntMin == 0 && d.IntMax == 0) { d.IntMin = 1; d.IntMax = 1; }
            if (d.Next != null && d.Next != t) Decal(d.Next);
            if (d.Sheet == null) return null;
            return d;
        }
    }
}

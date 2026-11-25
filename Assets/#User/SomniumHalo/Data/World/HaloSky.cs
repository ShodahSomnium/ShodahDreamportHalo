using System;
using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// A sky tag (sky): model + animation graph, indoor / outdoor ambient radiosity, outdoor / indoor fog (colour, maximum density,
    /// start and opaque distance in world units), the indoor fog screen, shader functions, animations and lights (lens flare, colour,
    /// power, direction as yaw / pitch towards the light, diameter). Layout: DevTools/reference/layouts.txt (struct Sky). Unity-free.
    /// </summary>
    public sealed class HaloSky
    {
        public sealed class Light
        {
            public HaloTag LensFlare; public string Marker; public uint Flags; public HVec3 Color; public float Power, TestDistance, Yaw, Pitch, Diameter;
            public bool AffectsExteriors => (Flags & 1) != 0;
            public bool AffectsInteriors => (Flags & 2) != 0;
            /// <summary>Halo-space unit vector towards the light.</summary>
            public HVec3 Direction => new HVec3((float)(Math.Cos(Yaw) * Math.Cos(Pitch)), (float)(Math.Sin(Yaw) * Math.Cos(Pitch)), (float)Math.Sin(Pitch));
        }

        public sealed class Animation { public int Index; public float Period; }

        public readonly HaloTag Tag, Model, AnimationGraph, IndoorFogScreen;
        public readonly HVec3 IndoorAmbient, OutdoorAmbient, OutdoorFogColor, IndoorFogColor;
        public readonly float IndoorAmbientPower, OutdoorAmbientPower;
        public readonly float OutdoorFogDensity, OutdoorFogStart, OutdoorFogOpaque, IndoorFogDensity, IndoorFogStart, IndoorFogOpaque;
        public readonly List<string> Functions = new List<string>();
        public readonly List<Animation> Animations = new List<Animation>();
        public readonly List<Light> Lights = new List<Light>();

        public HaloSky(HaloTag tag)
        {
            Tag = tag;
            var r = tag.Root;
            Model = r.Ref(0); AnimationGraph = r.Ref(16);
            IndoorAmbient = r.Vec3(56); IndoorAmbientPower = r.F32(68);
            OutdoorAmbient = r.Vec3(72); OutdoorAmbientPower = r.F32(84);
            OutdoorFogColor = r.Vec3(88); OutdoorFogDensity = r.F32(108); OutdoorFogStart = r.F32(112); OutdoorFogOpaque = r.F32(116);
            IndoorFogColor = r.Vec3(120); IndoorFogDensity = r.F32(140); IndoorFogStart = r.F32(144); IndoorFogOpaque = r.F32(148);
            IndoorFogScreen = r.Ref(152);
            foreach (var f in r.Block(172, 36)) Functions.Add(f.Str32(4));
            foreach (var a in r.Block(184, 36)) Animations.Add(new Animation { Index = a.I16(0), Period = a.F32(4) });
            foreach (var l in r.Block(196, 116))
                Lights.Add(new Light
                {
                    LensFlare = l.Ref(0), Marker = l.Str32(16), Flags = l.U32(76), Color = l.Vec3(80), Power = l.F32(92), TestDistance = l.F32(96),
                    Yaw = l.F32(104), Pitch = l.F32(108), Diameter = l.F32(112),
                });
        }
    }
}

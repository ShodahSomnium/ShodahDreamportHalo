using System;
using System.Collections.Generic;
using System.Text;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Halo's HUD definitions read from the map's tags (Unity-free; shared with the harness): hud_globals (hudg), unit_hud_interface
    /// (unhi: shields, health, motion sensor), weapon_hud_interface (wphi: crosshairs, ammo meters / numbers, static elements, overlays,
    /// zoom mask), grenade_hud_interface (grhi), hud_number (hud#: the digits), unicode string lists and fonts. Offsets: DevTools/reference/layouts.txt.
    /// The HUD canvas is 640x480; anchors are corners / centre; offsets in canvas pixels.
    /// </summary>
    public struct HudColor
    {
        public byte A, R, G, B;
        public HudColor(byte a, byte r, byte g, byte b) { A = a; R = r; G = g; B = b; }
        /// <summary>ColorARGBInt as stored (little endian 0xAARRGGBB).</summary>
        public static HudColor Read(TagReader t, int o) { uint v = t.U32(o); return new HudColor((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v); }
        /// <summary>ColorARGB of four floats (a, r, g, b).</summary>
        public static HudColor ReadF(TagReader t, int o) { return new HudColor(B8(t.F32(o)), B8(t.F32(o + 4)), B8(t.F32(o + 8)), B8(t.F32(o + 12))); }
        static byte B8(float f) { return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(f * 255f))); }
        public bool IsZero => A == 0 && R == 0 && G == 0 && B == 0;
        public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
    }

    public enum HudAnchor { TopLeft, TopRight, BottomLeft, BottomRight, Center, TopCenter, BottomCenter, LeftCenter, RightCenter }

    /// <summary>Where an element sits: offset from its anchor, scale, scaling flags (bit0 don't scale offset, bit1 don't scale size, bit2 high res).</summary>
    public struct HudPlace
    {
        public short X, Y; public float WScale, HScale; public ushort Flags;
        public static HudPlace Read(TagReader t, int o)
        {
            var p = new HudPlace { X = t.I16(o), Y = t.I16(o + 2), WScale = t.F32(o + 4), HScale = t.F32(o + 8), Flags = t.U16(o + 12) };
            if (p.WScale == 0f) p.WScale = 1f;
            if (p.HScale == 0f) p.HScale = 1f;
            return p;
        }
        public bool HighRes => (Flags & 4) != 0;
        public override string ToString() => $"({X},{Y}) x{WScale:0.##}/{HScale:0.##} f{Flags}";
    }

    /// <summary>A colour with Halo's flashing parameters (default, flashing, period, delay, count, flags, length, disabled).</summary>
    public struct HudFlash
    {
        public HudColor Default, Flashing, Disabled; public float Period, Delay, Length; public short Count; public ushort Flags;
        public static HudFlash Read(TagReader t, int o)
        {
            return new HudFlash
            {
                Default = HudColor.Read(t, o), Flashing = HudColor.Read(t, o + 4), Period = t.F32(o + 8), Delay = t.F32(o + 12),
                Count = t.I16(o + 16), Flags = t.U16(o + 18), Length = t.F32(o + 20), Disabled = HudColor.Read(t, o + 24),
            };
        }
        public bool Reverse => (Flags & 1) != 0;
        public override string ToString() => $"{Default} flash {Flashing} p{Period:0.##} d{Delay:0.##} n{Count} l{Length:0.##}";
    }

    /// <summary>A bitmap element (placement, bitmap, colours, sequence).</summary>
    public sealed class HudStatic
    {
        public HudPlace Place; public HaloTag Bitmap; public HudFlash Color; public short Sequence;
        // weapon elements only
        public int State = -1, View, ChildAnchor;
        public static HudStatic Read(TagReader t, int p)
        {
            return new HudStatic { Place = HudPlace.Read(t, p), Bitmap = t.Ref(p + 36), Color = HudFlash.Read(t, p + 52), Sequence = t.I16(p + 84) };
        }
        public override string ToString() => $"{Place} {Name(Bitmap)}#{Sequence} {Color}";
        internal static string Name(HaloTag b) => b != null ? System.IO.Path.GetFileName(b.Name) : "-";
    }

    /// <summary>A meter (Halo fills its bitmap by a gradient threshold): colours at min / max, flash, empty, flags, value range.</summary>
    public sealed class HudMeter
    {
        public HudPlace Place; public HaloTag Bitmap;
        public HudColor Min, Max, FlashColor, Empty, Disabled;
        public byte Flags; public sbyte MinValue; public short Sequence; public byte AlphaMul, AlphaBias; public short ValueScale;
        public float Opacity, Translucency, MinAlpha;
        public int State = -1, View, ChildAnchor;
        public static HudMeter Read(TagReader t, int p)
        {
            return new HudMeter
            {
                Place = HudPlace.Read(t, p), Bitmap = t.Ref(p + 36),
                Min = HudColor.Read(t, p + 52), Max = HudColor.Read(t, p + 56), FlashColor = HudColor.Read(t, p + 60), Empty = HudColor.Read(t, p + 64),
                Flags = t.U8(p + 68), MinValue = t.I8(p + 69), Sequence = t.I16(p + 70), AlphaMul = t.U8(p + 72), AlphaBias = t.U8(p + 73),
                ValueScale = t.I16(p + 74), Opacity = t.F32(p + 76), Translucency = t.F32(p + 80), Disabled = HudColor.Read(t, p + 84), MinAlpha = t.F32(p + 88),
            };
        }
        public bool UseMinMaxForState => (Flags & 1) != 0;
        public bool InterpolateFlash => (Flags & 2) != 0;
        public bool Hsv => (Flags & 4) != 0;
        public bool MoreHsv => (Flags & 8) != 0;
        public bool Invert => (Flags & 16) != 0;
        public override string ToString() => $"{Place} {HudStatic.Name(Bitmap)}#{Sequence} min {Min} max {Max} flash {FlashColor} empty {Empty} flags {Flags} minv {MinValue} a*{AlphaMul}+{AlphaBias} vs {ValueScale} op {Opacity:0.##} tr {Translucency:0.##}";
    }

    /// <summary>A number (drawn with the hud_number digits).</summary>
    public sealed class HudNumberEl
    {
        public HudPlace Place; public HudFlash Color; public sbyte MaxDigits; public byte Flags; public sbyte Fraction;
        public ushort WeaponFlags; public int State = -1, View, ChildAnchor;
        public static HudNumberEl Read(TagReader t, int p)
        {
            return new HudNumberEl { Place = HudPlace.Read(t, p), Color = HudFlash.Read(t, p + 36), MaxDigits = t.I8(p + 68), Flags = t.U8(p + 69), Fraction = t.I8(p + 70) };
        }
        public bool LeadingZeros => (Flags & 1) != 0;
        public bool OnlyZoomed => (Flags & 2) != 0;
        public bool TrailingM => (Flags & 4) != 0;
        public bool DivideByClip => (WeaponFlags & 1) != 0;
        public override string ToString() => $"{Place} digits {MaxDigits} flags {Flags} frac {Fraction} wflags {WeaponFlags} {Color}";
    }

    /// <summary>One overlay of a crosshair / weapon overlay / grenade overlay (placement, colours, frame rate, sequence, flags).</summary>
    public sealed class HudOverlay
    {
        public HudPlace Place; public HudFlash Color; public float FrameRate; public short Sequence; public ushort Type; public uint Flags;
        public override string ToString() => $"{Place} #{Sequence} fps {FrameRate:0.#} type {Type} flags {Flags:X} {Color}";
    }

    public sealed class HudCrosshair
    {
        public int Type, View; public HaloTag Bitmap; public readonly List<HudOverlay> Overlays = new List<HudOverlay>();
        // crosshair overlay flags
        public const uint FlashesWhenActive = 1, NotASprite = 2, OnlyZoomed = 4, SniperData = 8, HideOutside = 16, OneZoomLevel = 32, NotZoomed = 64;
        public override string ToString() => $"type {Type} view {View} {HudStatic.Name(Bitmap)} overlays {Overlays.Count}";
    }

    public sealed class HudOverlayElement
    {
        public int State, View, ChildAnchor; public HaloTag Bitmap; public readonly List<HudOverlay> Overlays = new List<HudOverlay>();
        // overlay type flags (weapon)
        public const ushort ShowFlashing = 1, ShowEmpty = 2, ShowReloadOverheat = 4, ShowDefault = 8, ShowAlways = 16;
    }

    /// <summary>weapon_hud_interface (wphi).</summary>
    public sealed class HudWeapon
    {
        public HaloTag Tag; public HudWeapon Child;
        public ushort Flags; public short TotalAmmoCutoff, LoadedAmmoCutoff, HeatCutoff, AgeCutoff;
        public HudAnchor Anchor; public int CanvasSize;
        public readonly List<HudStatic> Statics = new List<HudStatic>();
        public readonly List<HudMeter> Meters = new List<HudMeter>();
        public readonly List<HudNumberEl> Numbers = new List<HudNumberEl>();
        public readonly List<HudCrosshair> Crosshairs = new List<HudCrosshair>();
        public readonly List<HudOverlayElement> Overlays = new List<HudOverlayElement>();
        public uint CrosshairTypes;
        public HaloTag ZoomMask; public ushort ZoomMaskFlags; public float NightVision; public float TintR, TintG, TintB;
        public short MsgSequence, MsgWidthOffset; public short MsgX, MsgY; public byte MsgFlags; public short MsgText;

        // states attached to
        public const int TotalAmmo = 0, LoadedAmmo = 1, Heat = 2, Age = 3, SecondaryTotal = 4, SecondaryLoaded = 5, Distance = 6, Elevation = 7;
        // crosshair types
        public const int AimCross = 0, ZoomOverlay = 1, Charge = 2, ShouldReload = 3, FlashHeat = 4, FlashTotalAmmo = 5, FlashBattery = 6, ReloadOverheat = 7,
            FlashFiringNoAmmo = 8, FlashThrowingNoGrenade = 9, LowAmmoNoneLeft = 10;

        public static HudWeapon Read(HaloTag tag, int depth = 0)
        {
            if (tag == null || !tag.Is("wphi")) return null;
            var t = tag.Root;
            var w = new HudWeapon
            {
                Tag = tag, Flags = t.U16(16), TotalAmmoCutoff = t.I16(20), LoadedAmmoCutoff = t.I16(22), HeatCutoff = t.I16(24), AgeCutoff = t.I16(26),
                Anchor = (HudAnchor)t.U16(60), CanvasSize = t.U16(62), CrosshairTypes = t.U32(156),
                MsgSequence = t.I16(316), MsgWidthOffset = t.I16(318), MsgX = t.I16(320), MsgY = t.I16(322), MsgFlags = t.U8(329), MsgText = t.I16(330),
            };
            HaloTag child = t.Ref(0);
            if (child != null && child != tag && depth < 4) w.Child = Read(child, depth + 1);
            foreach (var e in t.Block(96, 180)) { var s = HudStatic.Read(e, 36); s.State = e.U16(0); s.View = e.U16(4); s.ChildAnchor = e.U16(6); w.Statics.Add(s); }
            foreach (var e in t.Block(108, 180)) { var m = HudMeter.Read(e, 36); m.State = e.U16(0); m.View = e.U16(4); m.ChildAnchor = e.U16(6); w.Meters.Add(m); }
            foreach (var e in t.Block(120, 160)) { var n = HudNumberEl.Read(e, 36); n.State = e.U16(0); n.View = e.U16(4); n.ChildAnchor = e.U16(6); n.WeaponFlags = e.U16(120); w.Numbers.Add(n); }
            foreach (var e in t.Block(132, 104))
            {
                var c = new HudCrosshair { Type = e.U16(0), View = e.U16(4), Bitmap = e.Ref(36) };
                foreach (var o in e.Block(52, 108))
                    c.Overlays.Add(new HudOverlay { Place = HudPlace.Read(o, 0), Color = HudFlash.Read(o, 36), FrameRate = o.I16(68), Sequence = o.I16(70), Flags = o.U32(72) });
                w.Crosshairs.Add(c);
            }
            foreach (var e in t.Block(144, 104))
            {
                var c = new HudOverlayElement { State = e.U16(0), View = e.U16(4), ChildAnchor = e.U16(6), Bitmap = e.Ref(36) };
                foreach (var o in e.Block(52, 136))
                    c.Overlays.Add(new HudOverlay { Place = HudPlace.Read(o, 0), Color = HudFlash.Read(o, 36), FrameRate = o.I16(68), Sequence = o.I16(72), Type = o.U16(74), Flags = o.U32(76) });
                w.Overlays.Add(c);
            }
            foreach (var e in t.Block(172, 184)) { w.ZoomMaskFlags = e.U16(4); w.ZoomMask = e.Ref(24); w.NightVision = e.F32(112); w.TintR = e.F32(148); w.TintG = e.F32(152); w.TintB = e.F32(156); break; }
            return w;
        }

        public override string ToString() => $"{Tag?.Name} anchor {Anchor} statics {Statics.Count} meters {Meters.Count} numbers {Numbers.Count} crosshairs {Crosshairs.Count} overlays {Overlays.Count} child {Child?.Tag?.Name}";
    }

    /// <summary>unit_hud_interface (unhi): the shield / health panel and the motion sensor.</summary>
    public sealed class HudUnit
    {
        public HaloTag Tag; public HudAnchor Anchor; public int CanvasSize;
        public HudStatic Background, ShieldBack, HealthBack, MotionBack, MotionFore;
        public HudMeter Shield, Health;
        public HudColor OverMin, OverMax, OverFlash, OverEmpty, MediumHealth;
        public float MaxColorHealthCutoff, MinColorHealthCutoff;
        public HudPlace MotionCenter;
        public HudAnchor OverlayAnchor;
        public readonly List<HudStatic> AuxOverlays = new List<HudStatic>();
        public readonly List<KeyValuePair<HaloTag, uint>> Sounds = new List<KeyValuePair<HaloTag, uint>>();
        // sound latches
        public const uint ShieldRecharging = 1, ShieldDamaged = 2, ShieldLow = 4, ShieldEmpty = 8, HealthLow = 16, HealthEmpty = 32, HealthMinor = 64, HealthMajor = 128;

        public static HudUnit Read(HaloTag tag)
        {
            if (tag == null || !tag.Is("unhi")) return null;
            var t = tag.Root;
            var u = new HudUnit
            {
                Tag = tag, Anchor = (HudAnchor)t.U16(0), CanvasSize = t.U16(2),
                Background = HudStatic.Read(t, 36), ShieldBack = HudStatic.Read(t, 140), Shield = HudMeter.Read(t, 244),
                OverMin = HudColor.Read(t, 348), OverMax = HudColor.Read(t, 352), OverFlash = HudColor.Read(t, 356), OverEmpty = HudColor.Read(t, 360),
                HealthBack = HudStatic.Read(t, 380), Health = HudMeter.Read(t, 484), MediumHealth = HudColor.Read(t, 588),
                MaxColorHealthCutoff = t.F32(592), MinColorHealthCutoff = t.F32(596),
                MotionBack = HudStatic.Read(t, 620), MotionFore = HudStatic.Read(t, 724), MotionCenter = HudPlace.Read(t, 860),
                OverlayAnchor = (HudAnchor)t.U16(896),
            };
            foreach (var e in t.Block(932, 132)) { var s = HudStatic.Read(e, 0); s.State = e.U16(104); s.View = e.U16(106); u.AuxOverlays.Add(s); }
            foreach (var e in t.Block(960, 56)) u.Sounds.Add(new KeyValuePair<HaloTag, uint>(e.Ref(0), e.U32(16)));
            return u;
        }

        /// <summary>The first sound latched to <paramref name="latch"/> (null = none).</summary>
        public HaloTag Sound(uint latch)
        {
            foreach (var kv in Sounds) if ((kv.Value & latch) != 0 && kv.Key != null) return kv.Key;
            return null;
        }
    }

    /// <summary>grenade_hud_interface (grhi): the grenade count panel (top left).</summary>
    public sealed class HudGrenade
    {
        public HaloTag Tag; public HudAnchor Anchor;
        public HudStatic Background, TotalBack; public HudNumberEl Numbers; public short FlashCutoff;
        public HaloTag OverlayBitmap; public readonly List<HudOverlay> Overlays = new List<HudOverlay>();
        public readonly List<KeyValuePair<HaloTag, uint>> Sounds = new List<KeyValuePair<HaloTag, uint>>();
        public short MsgSequence, MsgWidthOffset, MsgX, MsgY; public byte MsgFlags; public short MsgText;
        public const ushort ShowFlashing = 1, ShowEmpty = 2, ShowDefault = 4, ShowAlways = 8;

        public static HudGrenade Read(HaloTag tag)
        {
            if (tag == null || !tag.Is("grhi")) return null;
            var t = tag.Root;
            var g = new HudGrenade
            {
                Tag = tag, Anchor = (HudAnchor)t.U16(0), Background = HudStatic.Read(t, 36), TotalBack = HudStatic.Read(t, 140),
                Numbers = HudNumberEl.Read(t, 244), FlashCutoff = t.I16(328), OverlayBitmap = t.Ref(332),
                MsgSequence = t.I16(440), MsgWidthOffset = t.I16(442), MsgX = t.I16(444), MsgY = t.I16(446), MsgFlags = t.U8(453), MsgText = t.I16(454),
            };
            foreach (var o in t.Block(348, 136))
                g.Overlays.Add(new HudOverlay { Place = HudPlace.Read(o, 0), Color = HudFlash.Read(o, 36), FrameRate = o.F32(68), Sequence = o.I16(72), Type = o.U16(74), Flags = o.U32(76) });
            foreach (var e in t.Block(360, 56)) g.Sounds.Add(new KeyValuePair<HaloTag, uint>(e.Ref(0), e.U32(16)));
            return g;
        }
    }

    /// <summary>hud_number (hud#): the digits bitmap and its spacing.</summary>
    public sealed class HudDigits
    {
        public HaloTag Bitmap; public int BitmapDigitWidth, ScreenDigitWidth, XOffset, YOffset, DecimalWidth, ColonWidth;
        public static HudDigits Read(HaloTag tag)
        {
            if (tag == null) return null;
            var t = tag.Root;
            return new HudDigits { Bitmap = t.Ref(0), BitmapDigitWidth = t.I8(16), ScreenDigitWidth = t.I8(17), XOffset = t.I8(18), YOffset = t.I8(19), DecimalWidth = t.I8(20), ColonWidth = t.I8(21) };
        }
        public override string ToString() => $"{HudStatic.Name(Bitmap)} bitmap digit {BitmapDigitWidth} screen digit {ScreenDigitWidth} off {XOffset},{YOffset} dot {DecimalWidth} colon {ColonWidth}";
    }

    public sealed class HudWaypointArrow
    {
        public string Name; public HudColor Color; public float Opacity, Translucency; public short OnScreen, OffScreen, Occluded; public uint Flags;
    }

    /// <summary>hud_globals (hudg) plus what globals (matg) adds for the HUD (motion sensor bitmaps, digits, multiplayer icons, grenades).</summary>
    public sealed class HudGlobals
    {
        public HaloTag Tag; public HudAnchor Anchor; public HudPlace Place;
        public HaloTag Font, SplitFont; public float UpTime, FadeTime; public HudColor IconColor, TextColor; public float TextSpacing;
        public HaloTag ItemMessages, IconBitmap, AlternateIconText, HudMessages;
        public HudFlash HelpColor, ObjectiveColor; public short ObjectiveUp, ObjectiveFade;
        public float TopOffset, BottomOffset, LeftOffset, RightOffset;
        public HaloTag ArrowBitmap; public readonly List<HudWaypointArrow> Arrows = new List<HudWaypointArrow>();
        public float MultiplayerScale; public HaloTag DefaultWeaponHud;
        public float MotionRange, MotionVelocity, MotionScale;
        public short DamageTop, DamageBottom, DamageLeft, DamageRight; public HaloTag DamageBitmap; public short DamageSequence, DamageMpSequence; public HudColor DamageColor;
        public HudFlash NotMuchTimeLeft, TimeOutFlash; public HaloTag CarnageBitmap;
        public readonly List<HudButtonIcon> ButtonIcons = new List<HudButtonIcon>();

        // from globals.matg
        public HaloTag SweepBitmap, SweepMask, MultiplayerBitmap, BlipBitmap, SystemFont, TerminalFont;
        public HudDigits Digits;
        public readonly List<HaloTag> GrenadeHuds = new List<HaloTag>();
        public readonly List<int> GrenadeMax = new List<int>();
        public HaloTag FirstPersonHands;

        public static HudGlobals Read(HaloMap map)
        {
            var g = new HudGlobals();
            HaloTag matg = null;
            foreach (var t in map.OfClass("matg")) { matg = t; break; }
            HaloTag hudg = null;
            if (matg != null)
            {
                var m = matg.Root;
                foreach (var ib in m.Block(320, 304))
                {
                    g.SystemFont = ib.Ref(0); g.TerminalFont = ib.Ref(16); hudg = ib.Ref(96); g.SweepBitmap = ib.Ref(112); g.SweepMask = ib.Ref(128);
                    g.MultiplayerBitmap = ib.Ref(144); g.Digits = HudDigits.Read(ib.Ref(176)); g.BlipBitmap = ib.Ref(192);
                    break;
                }
                foreach (var gr in m.Block(296, 68)) { g.GrenadeHuds.Add(gr.Ref(20)); g.GrenadeMax.Add(gr.I16(0)); }
                foreach (var fp in m.Block(380, 192)) { g.FirstPersonHands = fp.Ref(0); break; }
            }
            if (hudg == null) foreach (var t in map.OfClass("hudg")) { hudg = t; break; }
            if (hudg == null) return g;
            var h = hudg.Root;
            g.Tag = hudg; g.Anchor = (HudAnchor)h.U16(0); g.Place = HudPlace.Read(h, 36);
            g.Font = h.Ref(72); g.SplitFont = h.Ref(88); g.UpTime = h.F32(104); g.FadeTime = h.F32(108);
            g.IconColor = HudColor.ReadF(h, 112); g.TextColor = HudColor.ReadF(h, 128); g.TextSpacing = h.F32(144);
            g.ItemMessages = h.Ref(148); g.IconBitmap = h.Ref(164); g.AlternateIconText = h.Ref(180);
            foreach (var b in h.Block(196, 16))
                g.ButtonIcons.Add(new HudButtonIcon { Sequence = b.I16(0), WidthOffset = b.I16(2), X = b.I16(4), Y = b.I16(6), Color = HudColor.Read(b, 8), FrameRate = b.I8(12), Flags = b.U8(13), Text = b.I16(14) });
            g.HelpColor = HudFlash.Read(h, 208); g.HudMessages = h.Ref(240);
            g.ObjectiveColor = HudFlash.Read(h, 256); g.ObjectiveUp = h.I16(284); g.ObjectiveFade = h.I16(286);
            g.TopOffset = h.F32(288); g.BottomOffset = h.F32(292); g.LeftOffset = h.F32(296); g.RightOffset = h.F32(300);
            g.ArrowBitmap = h.Ref(336);
            foreach (var a in h.Block(352, 104))
                g.Arrows.Add(new HudWaypointArrow { Name = a.Str32(0), Color = HudColor.Read(a, 40), Opacity = a.F32(44), Translucency = a.F32(48), OnScreen = a.I16(52), OffScreen = a.I16(54), Occluded = a.I16(56), Flags = a.U32(76) });
            g.MultiplayerScale = h.F32(444); g.DefaultWeaponHud = h.Ref(704);
            g.MotionRange = h.F32(720); g.MotionVelocity = h.F32(724); g.MotionScale = h.F32(728);
            g.DamageTop = h.I16(784); g.DamageBottom = h.I16(786); g.DamageLeft = h.I16(788); g.DamageRight = h.I16(790);
            g.DamageBitmap = h.Ref(824); g.DamageSequence = h.I16(840); g.DamageMpSequence = h.I16(842); g.DamageColor = HudColor.Read(h, 844);
            g.NotMuchTimeLeft = HudFlash.Read(h, 864); g.TimeOutFlash = HudFlash.Read(h, 896); g.CarnageBitmap = h.Ref(968);
            return g;
        }
    }

    /// <summary>One button icon of hud_globals (the key pictures inside HUD messages).</summary>
    public sealed class HudButtonIcon
    {
        public short Sequence, WidthOffset, X, Y; public HudColor Color; public sbyte FrameRate; public byte Flags; public short Text;
    }

    /// <summary>unicode_string_list (ustr): UTF-16 strings.</summary>
    public static class HaloStrings
    {
        public static List<string> Read(HaloTag tag)
        {
            var l = new List<string>();
            if (tag == null) return l;
            foreach (var s in tag.Root.Block(0, 20))
            {
                byte[] b = s.DataBytes(0);
                int n = b.Length / 2;
                while (n > 0 && b[(n - 1) * 2] == 0 && b[(n - 1) * 2 + 1] == 0) n--;
                l.Add(Encoding.Unicode.GetString(b, 0, n * 2));
            }
            return l;
        }
    }

    /// <summary>hud_message_text (hmt): named messages made of text runs and icons (button pictures).</summary>
    public sealed class HudMessages
    {
        public sealed class Part { public bool Icon; public string Text; public int IconIndex; }
        public readonly Dictionary<string, List<Part>> ByName = new Dictionary<string, List<Part>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Names = new List<string>();

        public static HudMessages Read(HaloTag tag)
        {
            var hm = new HudMessages();
            if (tag == null) return hm;
            var t = tag.Root;
            byte[] blob = t.DataBytes(0);
            var elems = new List<KeyValuePair<int, int>>();
            foreach (var e in t.Block(20, 2)) elems.Add(new KeyValuePair<int, int>(e.I8(0), e.U8(1)));
            foreach (var m in t.Block(32, 64))
            {
                string name = m.Str32(0);
                int text = m.I16(32), first = m.I16(34), count = m.U8(36);
                var parts = new List<Part>();
                int at = text;
                for (int i = 0; i < count && first + i < elems.Count; i++)
                {
                    var e = elems[first + i];
                    if (e.Key == 0)
                    {
                        // a text run: data = its length in characters (with its terminating zero)
                        int len = e.Value;
                        var sb = new StringBuilder();
                        for (int k = 0; k < len; k++)
                        {
                            int o = (at + k) * 2;
                            if (o + 1 >= blob.Length) break;
                            char c = (char)(blob[o] | blob[o + 1] << 8);
                            if (c != 0) sb.Append(c);
                        }
                        at += len;
                        parts.Add(new Part { Text = sb.ToString() });
                    }
                    else parts.Add(new Part { Icon = true, IconIndex = e.Value });
                }
                hm.ByName[name] = parts; hm.Names.Add(name);
            }
            return hm;
        }

        /// <summary>The message as plain text, icons as "[icon n]" or through <paramref name="iconText"/>.</summary>
        public string Plain(string name, Func<int, string> iconText = null)
        {
            List<Part> parts;
            if (!ByName.TryGetValue(name, out parts)) return null;
            var sb = new StringBuilder();
            foreach (var p in parts) sb.Append(p.Icon ? (iconText != null ? iconText(p.IconIndex) : "[" + p.IconIndex + "]") : p.Text);
            return sb.ToString();
        }
    }

    /// <summary>font: glyphs as 8-bit coverage pictures.</summary>
    public sealed class HaloFont
    {
        public sealed class Glyph { public char C; public int Advance, W, H, OriginX, OriginY, Pixels; }
        public HaloTag Tag; public int Ascend, Descend, LeadHeight, LeadWidth;
        public byte[] Pixels;
        public readonly Dictionary<char, Glyph> Glyphs = new Dictionary<char, Glyph>();
        public int LineHeight => Ascend + Descend + LeadHeight;

        public static HaloFont Read(HaloTag tag)
        {
            if (tag == null || !tag.Is("font")) return null;
            var t = tag.Root;
            var f = new HaloFont { Tag = tag, Ascend = t.I16(4), Descend = t.I16(6), LeadHeight = t.I16(8), LeadWidth = t.I16(10), Pixels = t.DataBytes(136) };
            foreach (var c in t.Block(124, 20))
            {
                var g = new Glyph { C = (char)c.U16(0), Advance = c.I16(2), W = c.I16(4), H = c.I16(6), OriginX = c.I16(8), OriginY = c.I16(10), Pixels = c.I32(16) };
                if (!f.Glyphs.ContainsKey(g.C)) f.Glyphs.Add(g.C, g);
            }
            return f;
        }

        public Glyph Get(char c)
        {
            Glyph g;
            if (Glyphs.TryGetValue(c, out g)) return g;
            if (c >= 'a' && c <= 'z' && Glyphs.TryGetValue(char.ToUpperInvariant(c), out g)) return g;
            Glyphs.TryGetValue('?', out g);
            return g;
        }

        public int Measure(string s)
        {
            int w = 0;
            if (s == null) return 0;
            for (int i = 0; i < s.Length; i++) { var g = Get(s[i]); if (g != null) w += g.Advance; }
            return w;
        }
    }
}

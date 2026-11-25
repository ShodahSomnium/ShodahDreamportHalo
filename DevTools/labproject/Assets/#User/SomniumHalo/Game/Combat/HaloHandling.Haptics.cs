using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Haptics on hover (the user's rule: "if it does not vibrate there isn't one there"): a free hand entering a carrying place that holds
    /// something buzzes once - a gun in a holster (back or hip) firmly, a grenade on the chest lighter and shorter - and then ticks lightly
    /// every 0.6 s while it stays; an empty place does nothing. With a gun in the other hand the part to pull (when a rack is due) and the
    /// pouch (when the magazine is out and there are rounds to take) buzz too. HaloSettings.Haptics off = none (HaloXr.HapticsOn).
    /// </summary>
    public sealed partial class HaloHandling
    {
        private readonly int[] _hoverZone = { -1, -1 };
        private readonly float[] _hoverTick = new float[2];

        private const int HoverGun0 = 0, HoverGun1 = 1, HoverFrag = 2, HoverPlasma = 3, HoverPouch = 4, HoverRack = 5;

        private void HoverHaptics()
        {
            for (int h = 0; h < 2; h++)
            {
                int z = Busy(h) ? -1 : HoverZoneOf(h);
                if (z != _hoverZone[h])
                {
                    _hoverZone[h] = z;
                    _hoverTick[h] = Time.time + 0.6f;
                    if (z == HoverGun0 || z == HoverGun1) HaloXr.Pulse(h == 1, 0.45f, 0.06f);
                    else if (z == HoverFrag || z == HoverPlasma) HaloXr.Pulse(h == 1, 0.28f, 0.035f);
                    else if (z == HoverRack || z == HoverPouch) HaloXr.Pulse(h == 1, 0.35f, 0.045f);
                }
                else if (z >= 0 && Time.time >= _hoverTick[h])
                {
                    _hoverTick[h] = Time.time + 0.6f;
                    HaloXr.Pulse(h == 1, 0.1f, 0.02f);
                }
            }
        }

        /// <summary>The carrying place (with something in it) a hand is in, -1 none.</summary>
        private int HoverZoneOf(int h)
        {
            Vector3 hp = HandPos(h);
            HaloGun g = HeldGun;
            if (g != null)
            {
                if (g.NeedsRack && g.HasPull && (hp - g.transform.TransformPoint(g.RackLocal)).magnitude < RackRadius * 1.3f * Body.Scale) return HoverRack;
                bool wantsAmmo = (!g.MagIn || (g.Def.Kind == HaloWeaponDef.Family.Shotgun && g.State.Loaded < g.State.LoadedMaximum)) && g.State.Reserve > 0;
                if (wantsAmmo && (hp - ZoneCenter(Zone.Pouch)).magnitude < ZoneRadius(Zone.Pouch)) return HoverPouch;
            }

            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                if (Guns[i] == null || i == _held) continue;
                float d = GunReach(i, hp);
                if (d < bd) { bd = d; best = i; }
            }

            if (HaloLocal.FragGrenades > 0) { float d = (hp - ZoneCenter(Zone.Frags)).magnitude; if (d <= ZoneRadius(Zone.Frags) && d < bd) { bd = d; best = HoverFrag; } }
            if (HaloLocal.PlasmaGrenades > 0) { float d = (hp - ZoneCenter(Zone.Plasma)).magnitude; if (d <= ZoneRadius(Zone.Plasma) && d < bd) { bd = d; best = HoverPlasma; } }
            return best;
        }

        /// <summary>For the lab: which carrying place a hand hovers (-1 none, 0/1 a gun, 2 frags, 3 plasma, 4 pouch, 5 the part to pull).</summary>
        public int HoverOf(int hand) { return hand >= 0 && hand < 2 ? _hoverZone[hand] : -1; }
        public bool Cupped => _held >= 0 && _supportHand >= 0 && _support == Support.Cup;
        public bool Racking => _held >= 0 && _supportHand >= 0 && _support == Support.Rack;
    }
}

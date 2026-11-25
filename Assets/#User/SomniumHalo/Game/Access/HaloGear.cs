using System;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Who counts as "another Dreamport player" for the isolation's whitelisted rule. WoW decides it by a claimed network slot
    /// (Gear's record); until the Halo network records exist, a remote player counts when they are on the whitelist too -
    /// only whitelisted players ever run the Dreamport, so that is exactly the set that would hold a slot.
    /// </summary>
    public static class HaloGear
    {
        public static bool HasRecord(int photonId)
        {
            // Iteration 3: while the net layer is up in a room with the slot objects (Game/Net/HaloNetGame), the decision is WoW's: the player holds a written
            // "Halo Net Player" slot (claimed, in the room, not free). A visitor never claims one. Without the layer (the room, the editor, no slots) the
            // whitelist stand-in below decides, as before.
            bool known;
            bool has = HaloNetGame.TryHasRecord(photonId, out known);
            if (known) return has;
            try
            {
                foreach (HaloRemotes.Remote r in HaloRemotes.All)
                {
                    if (r.Id != photonId) continue;
                    string id = null;
                    try { id = r.Player != null && r.Player.Properties != null ? r.Player.Properties.Id : null; }
                    catch (Exception) { id = null; }
                    return HaloAccess.Matches(r.Nick, id);
                }
            }
            catch (Exception)
            {
                return false;
            }

            return false;
        }
    }
}

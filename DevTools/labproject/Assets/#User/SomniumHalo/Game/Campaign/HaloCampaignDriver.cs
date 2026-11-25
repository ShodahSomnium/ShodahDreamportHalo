using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Mission module, K] The campaign's early frame (order -95, before the client's order-0 scripts place the avatar): device visuals
    /// between game ticks, the player riding a moving machine (body root carried by the platform's motion, as the client's moving platforms
    /// and the Euro Truck Simulator's riders do), the player's control use. Lives on the "Halo Game" object while a mission is up.
    /// </summary>
    [DefaultExecutionOrder(-95)]
    public sealed class HaloCampaignDriver : MonoBehaviour
    {
        static HaloCampaignDriver _instance;
        bool _failed;

        /// <summary>Called every frame by HaloGame.CampaignTick: adds the driver while a mission runs, removes it after.</summary>
        public static void Ensure()
        {
            if (HaloCampaign.Active)
            {
                if (_instance == null && HaloGame.Current != null) _instance = HaloGame.Current.AddOnce<HaloCampaignDriver>();
            }
            else if (_instance != null)
            {
                Destroy(_instance);
                _instance = null;
            }
        }

        private void Update()
        {
            if (!HaloCampaign.Active || !HaloSession.Playing) return;
            try { HaloDevices.Frame(); HaloObjects.Draw(HaloCampaign.TickFraction); }
            catch (Exception e) { if (!_failed) { _failed = true; HaloLog.Error("Campaign: device frame failed: " + e); } }
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }
    }
}

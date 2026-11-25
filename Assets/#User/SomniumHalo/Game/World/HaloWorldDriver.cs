using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Per-frame part of the world (on the world root, lives as long as the world), four times a second: switches the atmospheric fog
    /// between the sky's outdoor and indoor fog as the camera moves between clusters with and without a sky, shows the weather of the
    /// camera's cluster (the BSP's weather palette entry; none indoors), and re-applies the fog when someone else reset RenderSettings.
    /// Also hosts the background BSP builder coroutine. No allocations.
    /// </summary>
    public sealed class HaloWorldDriver : MonoBehaviour
    {
        bool _mixed;
        float _next;
        int _lastCluster = -3, _weather = -2;
        /// <summary>Camera to follow (null = Camera.main).</summary>
        public static Camera View;

        public void Init(HaloWorldJob job)
        {
            bool anyIn = false, anyOut = false;
            if (job.Bsp != null) foreach (var c in job.Bsp.Clusters) { if (c.Sky < 0) anyIn = true; else anyOut = true; }
            _mixed = anyIn && anyOut;
            _lastCluster = -3;
            _weather = -2;
            _next = 0f;
        }

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            if (HaloWorldLook.Clobbered) HaloWorldLook.SetIndoor(HaloWorldLook.IndoorFog);
            var cam = View != null ? View : Camera.main;
            var bsp = HaloWorld.Bsp;
            if (cam == null || bsp == null) return;
            int cl = HaloWorld.ClusterAt(cam.transform.position);
            if (cl == _lastCluster || cl < 0 || cl >= bsp.Clusters.Count) return;
            _lastCluster = cl;
            var cluster = bsp.Clusters[cl];
            if (_mixed)
            {
                bool indoor = cluster.Sky < 0;
                if (indoor != HaloWorldLook.IndoorFog) HaloWorldLook.SetIndoor(indoor);
            }
            var extras = HaloWorld.ActiveExtras;
            if (extras == null || cluster.Weather == _weather) return;
            _weather = cluster.Weather;
            for (int i = 0; i < extras.Weather.Length; i++)
            {
                var w = extras.Weather[i];
                if (w != null) w.SetActive(i == _weather);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The map's atmosphere from its sky tag: the sky model (drawn around the eye at the far plane, sky material variants), the
    /// atmospheric fog (outdoor fog of the sky, or its indoor fog in indoor clusters), the BSP's planar fog (fog palette + fog plane),
    /// the default object light (sky lights: the sun's direction / colour / power; the outdoor ambient), one directional Unity light
    /// for other modules' URP materials, RenderSettings fog (kept ON and Linear: the client strips fog variants otherwise) and the
    /// ambient probe (set directly - the client does not rebuild it). <see cref="Restore"/> puts the previous settings back.
    /// </summary>
    public static class HaloWorldLook
    {
        sealed class Saved
        {
            public bool Fog; public FogMode Mode; public Color FogColor; public float Start, End, Density;
            public AmbientMode Ambient; public Color AmbientLight; public SphericalHarmonicsL2 Probe;
            public Material Skybox;
        }

        static Saved _saved;
        static GameObject _sun;
        public static HaloSky Sky { get; private set; }
        /// <summary>Unity world direction towards the sun (the brightest sky light), and its colour (gamma).</summary>
        public static Vector3 SunDirection { get; private set; } = new Vector3(-0.4f, 0.8f, -0.3f).normalized;
        public static Color SunColor { get; private set; } = new Color(0.9f, 0.9f, 0.85f);
        public static Color Ambient { get; private set; } = new Color(0.4f, 0.4f, 0.42f);
        public static bool IndoorFog { get; private set; }
        static Color _applied;
        /// <summary>True when someone else changed RenderSettings fog after us (the hub restoring its look): the driver re-applies.</summary>
        public static bool Clobbered { get { return RenderSettings.fogColor != _applied || !RenderSettings.fog; } }

        // fog settings of the map (metres)
        static Color _outFogColor, _inFogColor;
        static float _outDensity, _outStart, _outOpaque, _inDensity, _inStart, _inOpaque;

        static readonly Dictionary<uint, GameObject> SkyModels = new Dictionary<uint, GameObject>();

        /// <summary>Shows the sky of a BSP (sky models are built once per sky tag under <paramref name="root"/> and kept; the others hide)
        /// and applies its light and fog. Called for the start BSP and again at every BSP switch. Main thread.</summary>
        public static GameObject Apply(HaloWorldJob job, Transform root)
        {
            Save();
            Sky = job.Sky;
            GameObject skyGo = null;
            foreach (var kv in SkyModels) if (kv.Value != null) kv.Value.SetActive(false);
            var sky = job.Sky;
            float u = HaloSpace.Unit;
            if (sky != null)
            {
                _outFogColor = C(sky.OutdoorFogColor); _outDensity = Mathf.Clamp01(sky.OutdoorFogDensity); _outStart = sky.OutdoorFogStart * u; _outOpaque = sky.OutdoorFogOpaque * u;
                _inFogColor = C(sky.IndoorFogColor); _inDensity = Mathf.Clamp01(sky.IndoorFogDensity); _inStart = sky.IndoorFogStart * u; _inOpaque = sky.IndoorFogOpaque * u;
                // a fog with no range (space skies: density 1 over 0-0) is no fog in the game, not a black wall at 0 m
                if (_outOpaque <= _outStart + 0.001f) _outDensity = 0f;
                if (_inOpaque <= _inStart + 0.001f) _inDensity = 0f;
                if (sky.Model != null)
                {
                    if (SkyModels.TryGetValue(sky.Tag.Id, out skyGo) && skyGo != null) skyGo.SetActive(true);
                    else
                    {
                        var b = HaloModelBuilder.Build(sky.Model, root, new HaloModelBuilder.Options { Sky = true, Static = true, CastShadows = false });
                        if (b != null) { skyGo = b.Root; skyGo.name = "Sky " + System.IO.Path.GetFileName(sky.Tag.Name); SkyModels[sky.Tag.Id] = skyGo; }
                    }
                }
                // the brightest light is the sun
                HaloSky.Light sun = null;
                foreach (var l in sky.Lights) if (sun == null || l.Power * (l.Color.X + l.Color.Y + l.Color.Z) > sun.Power * (sun.Color.X + sun.Color.Y + sun.Color.Z)) sun = l;
                if (sun != null)
                {
                    SunDirection = HaloSpace.Dir(sun.Direction).normalized;
                    float k = Mathf.Clamp(sun.Power, 0f, 2f) * 0.5f;
                    SunColor = new Color(sun.Color.X * k, sun.Color.Y * k, sun.Color.Z * k);
                }
                float ap = Mathf.Clamp(sky.OutdoorAmbientPower, 0f, 1f);
                Ambient = new Color(Mathf.Lerp(0.35f, sky.OutdoorAmbient.X, ap), Mathf.Lerp(0.35f, sky.OutdoorAmbient.Y, ap), Mathf.Lerp(0.37f, sky.OutdoorAmbient.Z, ap)) * 0.5f;
            }
            else
            {
                _outFogColor = _inFogColor = Color.gray; _outDensity = _inDensity = 0f; _outStart = _inStart = 0f; _outOpaque = _inOpaque = 1f;
            }
            HaloMaterials.SetDefaultLight(Ambient, SunDirection, SunColor, new Color(0.12f, 0.13f, 0.16f));
            SetIndoor(job.Indoor);
            ApplyPlaneFog(job);

            // one sun for URP materials of other modules (no shadows: the world is lightmapped)
            if (_sun == null)
            {
                _sun = new GameObject("Halo Sun");
                _sun.transform.SetParent(root, false);
                _sun.AddComponent<Light>();
            }
            _sun.transform.rotation = Quaternion.LookRotation(-SunDirection, Vector3.up);
            var light = _sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = SunColor.linear;
            light.intensity = 1.2f;
            light.shadows = LightShadows.None;
            HaloLog.Info("World: sky " + (sky != null ? sky.Tag.Name : "none") + (skyGo != null ? " (model)" : "") + ", fog out " + _outDensity.ToString("0.00") + " " +
                         _outStart.ToString("0") + "-" + _outOpaque.ToString("0") + " m, in " + _inDensity.ToString("0.00") + ", " + (job.Indoor ? "indoor" : "outdoor") +
                         ", sun " + SunDirection.ToString("0.00") + " " + SunColor);
            return skyGo;
        }

        static Color C(HVec3 v) { return new Color(v.X, v.Y, v.Z, 1f); }

        /// <summary>Switches between the sky's outdoor and indoor fog (the driver calls it when the camera changes cluster kind).</summary>
        public static void SetIndoor(bool indoor)
        {
            IndoorFog = indoor;
            Color c = indoor ? _inFogColor : _outFogColor;
            _applied = c.linear;
            float d = indoor ? _inDensity : _outDensity, s = indoor ? _inStart : _outStart, o = indoor ? _inOpaque : _outOpaque;
            HaloMaterials.SetFog(c, d, s, o);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = c.linear;
            RenderSettings.fogStartDistance = s;
            RenderSettings.fogEndDistance = d > 0.001f ? s + Mathf.Max(1f, o - s) / d : 100000f;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight((Ambient * 2f).linear);
            sh.AddDirectionalLight(SunDirection, SunColor.linear, 0.6f);
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientProbe = sh;
            RenderSettings.ambientLight = (Ambient * 2f).linear;
        }

        static void ApplyPlaneFog(HaloWorldJob job)
        {
            var bsp = job.Bsp;
            if (bsp == null || bsp.FogPlanes.Count == 0 || bsp.FogPalette.Count == 0) { HaloMaterials.SetPlaneFog(Vector3.up, -100000f, Color.gray, 0f, 1f, 1f); return; }
            var fp = bsp.FogPlanes[0];
            int fogIndex = fp.FrontRegion >= 0 && fp.FrontRegion < bsp.FogRegions.Count ? bsp.FogRegions[fp.FrontRegion].Fog : 0;
            if (fogIndex < 0 || fogIndex >= bsp.FogPalette.Count || bsp.FogPalette[fogIndex].Fog == null) { HaloMaterials.SetPlaneFog(Vector3.up, -100000f, Color.gray, 0f, 1f, 1f); return; }
            var fog = bsp.FogPalette[fogIndex].Fog.Root;
            uint flags = fog.U32(0);
            float density = Mathf.Clamp01(fog.F32(88)), opaqueDist = fog.F32(96), opaqueDepth = fog.F32(104);
            if (opaqueDist <= 0f && opaqueDepth <= 0f) { HaloMaterials.SetPlaneFog(Vector3.up, -100000f, Color.gray, 0f, 1f, 1f); return; }   // no range = no fog
            var col = fog.Vec3(120);
            // the plane: Halo n.p = d, fog on the back side (below the water surface)
            Vector3 n = HaloSpace.Dir(fp.Nx, fp.Ny, fp.Nz).normalized;
            float dd = fp.D * HaloSpace.Unit;
            HaloMaterials.SetPlaneFog(n, dd, new Color(col.X, col.Y, col.Z), density > 0f ? density : 1f,
                Mathf.Max(0.1f, opaqueDepth * HaloSpace.Unit), Mathf.Max(0.5f, opaqueDist * HaloSpace.Unit));
            HaloLog.Info("World: planar fog " + bsp.FogPalette[fogIndex].Fog.Name + " flags " + flags + " density " + density.ToString("0.00") +
                         " depth " + (opaqueDepth * HaloSpace.Unit).ToString("0.0") + " m dist " + (opaqueDist * HaloSpace.Unit).ToString("0.0") + " m plane " + n.ToString("0.00") + " " + dd.ToString("0.0"));
        }

        static void Save()
        {
            if (_saved != null) return;
            _saved = new Saved
            {
                Fog = RenderSettings.fog, Mode = RenderSettings.fogMode, FogColor = RenderSettings.fogColor, Start = RenderSettings.fogStartDistance,
                End = RenderSettings.fogEndDistance, Density = RenderSettings.fogDensity, Ambient = RenderSettings.ambientMode,
                AmbientLight = RenderSettings.ambientLight, Probe = RenderSettings.ambientProbe, Skybox = RenderSettings.skybox,
            };
        }

        /// <summary>Puts the hub's settings back (Unload).</summary>
        public static void Restore()
        {
            if (_sun != null) UnityEngine.Object.Destroy(_sun);
            _sun = null;
            foreach (var kv in SkyModels) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            SkyModels.Clear();
            Sky = null;
            HaloMaterials.ResetGlobals();
            if (_saved == null) return;
            RenderSettings.fog = _saved.Fog; RenderSettings.fogMode = _saved.Mode; RenderSettings.fogColor = _saved.FogColor;
            RenderSettings.fogStartDistance = _saved.Start; RenderSettings.fogEndDistance = _saved.End; RenderSettings.fogDensity = _saved.Density;
            RenderSettings.ambientMode = _saved.Ambient; RenderSettings.ambientLight = _saved.AmbientLight; RenderSettings.ambientProbe = _saved.Probe;
            RenderSettings.skybox = _saved.Skybox;
            _saved = null;
        }
    }
}

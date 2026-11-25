using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Front module] The ring hub at HaloSpace.RoomOrigin (3 km above the maps): Halo PC's main menu scene around the player, made huge. ui.map's
    /// own scene is used as the game places it: the ring (scenery\halo\halo, shaders "halo inner ring" / "halo outer ring" / "halo inner ring shadow")
    /// where the ui scenario puts it relative to the menu camera "uicam" (12.3, -1.8, 15.5 world units ahead / up, turned yaw -0.419 roll -0.175), and
    /// the menu sky (sky\sky_ui\sky_ui: the star field, the galaxy, the planet Threshold, the moon Basis) - but <see cref="RingScale"/> times larger, so
    /// the band (about 2.3 km across, 150 m wide) sweeps past the platform and arcs over the player's head. The ring turns slowly about its axis
    /// (<see cref="TurnDegreesPerSecond"/>, ours). The sky is a dome 7 km round that follows the head (a sky box: no parallax); the client's far clip
    /// plane is 10 km (Player.XR.Body / NonXR CameraSystem prefabs), so everything is inside it.
    /// The player stands on a Forerunner platform (a 24 m disc in the ring's own outer-hull texture with a lit blue rim, colliders and an invisible
    /// fence so nobody walks off), lit by the hub's own sun. The menu stands on the platform in front of the player (HaloPanelPlacer); the first time
    /// it is placed the whole scene turns so the ring is seen behind it as the game's menu shows it.
    /// The hub hides while a match loads / runs (sky, ring, light, platform, panel) and comes back with the hub phase.
    /// </summary>
    public sealed class HaloHub : MonoBehaviour
    {
        public const float FloorRadius = 12f, KeepRadius = 14f, WallHeight = 4f, EyeHeight = 1.7f;

        /// <summary>The ring is drawn this many times the size the ui scenario gives it (ours: about 2.3 km radius).</summary>
        public const float RingScale = 40f;
        /// <summary>The sky model (20400 world units = 62 km) scaled to a 7 km dome (ours).</summary>
        public const float SkyScale = 7000f / (20400f * HaloSpace.Unit);
        /// <summary>The ring's slow turn about its axis (ours; the game's menu ring drifts slowly).</summary>
        public static float TurnDegreesPerSecond = 0.25f;

        public const float PanelMetresPerUnit = 0.002f;
        public static readonly Vector3 PanelCentre = new Vector3(0f, 1.5f, 2.7f);

        public static HaloHub Current { get; private set; }

        private Transform _scene, _sky, _ringPivot, _ringSpin;
        private GameObject _floor, _sunGo;
        private Light _sun;
        private Material _skyMat;
        private readonly List<Material> _mats = new List<Material>();
        private readonly List<Mesh> _meshes = new List<Mesh>();
        private bool _ringBuilt, _skyBuilt, _turned, _shown = true;
        private float _spin;
        private Canvas _panel;
        private HaloMenuPanel _menu;
        private HaloPanelPlacer _placer;
        private bool _savedAmbient;
        private UnityEngine.Rendering.AmbientMode _ambientMode;
        private Color _ambientLight;
        private bool _fog;

        public Canvas Panel { get { return _panel; } }
        public HaloMenuPanel Menu { get { return _menu; } }
        public HaloPanelPlacer Placer { get { return _placer; } }
        public bool RingBuilt { get { return _ringBuilt; } }
        public bool SkyBuilt { get { return _skyBuilt; } }
        public bool Shown { get { return _shown; } }
        public Transform Scene { get { return _scene; } }

        public static HaloHub Create(Transform parent)
        {
            var go = new GameObject("Halo Hub");
            go.transform.SetParent(parent, false);
            go.transform.position = HaloSpace.RoomOrigin;
            go.transform.rotation = Quaternion.identity;
            var hub = go.AddComponent<HaloHub>();
            Current = hub;
            hub.BuildBase();
            return hub;
        }

        private void OnDestroy()
        {
            RestoreLook();
            foreach (Material m in _mats) if (m != null) Destroy(m);
            foreach (Mesh m in _meshes) if (m != null) Destroy(m);
            if (Current == this) Current = null;
        }

        // ------------------------------------------------------------------ the platform, the black of space, the sun

        private void BuildBase()
        {
            _scene = new GameObject("Halo Hub Scene").transform;
            _scene.SetParent(transform, false);
            _scene.localPosition = new Vector3(0f, EyeHeight, 0f);

            // the space backdrop (black until the sky is read), a dome round the head
            _sky = new GameObject("Halo Hub Sky").transform;
            _sky.SetParent(transform, false);
            GameObject dome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dome.name = "Halo Hub Space";
            Destroy(dome.GetComponent<Collider>());
            dome.transform.SetParent(_sky, false);
            dome.transform.localScale = Vector3.one * 2f * 7600f;
            var mf = dome.GetComponent<MeshFilter>();
            Mesh inside = Instantiate(mf.sharedMesh);
            int[] tris = inside.triangles;
            for (int i = 0; i + 2 < tris.Length; i += 3) { int t = tris[i]; tris[i] = tris[i + 1]; tris[i + 1] = t; }
            inside.triangles = tris;
            _meshes.Add(inside);
            mf.sharedMesh = inside;
            _skyMat = Unlit(Color.black, null, 1000, false, 0);
            var mr = dome.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _skyMat;
            NoShadows(mr);

            // the platform: a disc in the ring's own outer hull texture (once read), a blue rim
            _floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _floor.name = "Halo Hub Platform";
            Destroy(_floor.GetComponent<Collider>());
            _floor.transform.SetParent(transform, false);
            _floor.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            _floor.transform.localScale = new Vector3(FloorRadius * 2f, 0.1f, FloorRadius * 2f);
            var fr = _floor.GetComponent<MeshRenderer>();
            fr.sharedMaterial = Lit(new Color(0.22f, 0.25f, 0.26f), null, 0.25f, new Color(0.012f, 0.016f, 0.02f));
            fr.receiveShadows = true;

            GameObject rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Halo Hub Platform Rim";
            Destroy(rim.GetComponent<Collider>());
            rim.transform.SetParent(transform, false);
            rim.transform.localPosition = new Vector3(0f, -0.13f, 0f);
            rim.transform.localScale = new Vector3(FloorRadius * 2f + 0.5f, 0.06f, FloorRadius * 2f + 0.5f);
            var rr = rim.GetComponent<MeshRenderer>();
            rr.sharedMaterial = Unlit(new Color(0.16f, 0.55f, 1f), null, 2000, true, 0);
            NoShadows(rr);

            for (int k = 0; k < 3; k++)
            {
                // the inner rings of light on the platform (Forerunner lines; ours)
                GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                line.name = "Halo Hub Platform Line " + k;
                Destroy(line.GetComponent<Collider>());
                line.transform.SetParent(transform, false);
                float r = k == 0 ? 2.2f : k == 1 ? 6.5f : 10.8f;
                line.transform.localPosition = new Vector3(0f, 0.004f + k * 0.0005f, 0f);
                line.transform.localScale = new Vector3(r * 2f, 0.001f, r * 2f);
                var lr = line.GetComponent<MeshRenderer>();
                lr.sharedMaterial = Unlit(new Color(0.10f, 0.36f, 0.75f, 0.55f), null, 2504, true, 0);
                NoShadows(lr);
                GameObject hole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                hole.name = "Halo Hub Platform Line Inner " + k;
                Destroy(hole.GetComponent<Collider>());
                hole.transform.SetParent(transform, false);
                hole.transform.localPosition = new Vector3(0f, 0.0045f + k * 0.0005f, 0f);
                hole.transform.localScale = new Vector3(r * 2f - 0.12f, 0.001f, r * 2f - 0.12f);
                hole.GetComponent<MeshRenderer>().sharedMaterial = fr.sharedMaterial;
                NoShadows(hole.GetComponent<MeshRenderer>());
            }

            // colliders: the floor and a fence of 24 boxes round the disc
            var floorCol = new GameObject("Halo Hub Floor Collider");
            floorCol.transform.SetParent(transform, false);
            floorCol.layer = 0;
            var mc = floorCol.AddComponent<MeshCollider>();
            mc.sharedMesh = _floor.GetComponent<MeshFilter>().sharedMesh;
            floorCol.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            floorCol.transform.localScale = new Vector3(FloorRadius * 2f, 0.1f, FloorRadius * 2f);
            var below = floorCol.AddComponent<BoxCollider>();
            below.center = new Vector3(0f, -4f, 0f);
            below.size = new Vector3(1.0f, 6f, 1.0f);
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                var w = new GameObject("Halo Hub Fence " + i);
                w.transform.SetParent(transform, false);
                w.transform.localPosition = new Vector3(Mathf.Sin(a) * (FloorRadius + 0.3f), WallHeight * 0.5f, Mathf.Cos(a) * (FloorRadius + 0.3f));
                w.transform.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
                var b = w.AddComponent<BoxCollider>();
                b.size = new Vector3(2f * Mathf.PI * (FloorRadius + 0.3f) / 24f + 0.4f, WallHeight, 0.5f);
            }

            // the hub's sun (ours: from behind the player's left, so the band in front shows its lit side)
            _sunGo = new GameObject("Halo Hub Sun");
            _sunGo.transform.SetParent(_scene, false);
            _sun = _sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.color = new Color(1f, 0.96f, 0.90f);
            _sun.intensity = 1.25f;
            _sun.shadows = LightShadows.Soft;
            _sunGo.transform.localRotation = Quaternion.Euler(28f, 125f, 0f);
            ApplyLook();
        }

        private void ApplyLook()
        {
            if (!_savedAmbient)
            {
                _savedAmbient = true;
                _ambientMode = RenderSettings.ambientMode;
                _ambientLight = RenderSettings.ambientLight;
                _fog = RenderSettings.fog;
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.18f, 0.24f);
            RenderSettings.fog = false;
        }

        private void RestoreLook()
        {
            if (!_savedAmbient) return;
            _savedAmbient = false;
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientLight = _ambientLight;
            RenderSettings.fog = _fog;
        }

        // ------------------------------------------------------------------ the ring and the sky (once ui.map is read)

        private void TryBuildScene()
        {
            if (!HaloUiArt.Ready) return;
            if (!_skyBuilt && HaloUiArt.SkyMesh != null && HaloUiArt.Texture("sky\\space\\bitmaps\\stars", 0) != null && HaloUiArt.Texture("sky\\planets\\threshold space", 0) != null)
            {
                _skyBuilt = true;
                try { BuildSky(); }
                catch (Exception e) { HaloLog.Warn("Hub: the sky failed: " + e); }
            }

            if (!_ringBuilt && HaloUiArt.RingMesh != null && HaloUiArt.Texture("scenery\\halo\\bitmaps\\halo outer ring", 0) != null && HaloUiArt.Texture("scenery\\halo\\bitmaps\\halo inner ring", 0) != null)
            {
                _ringBuilt = true;
                try { BuildRing(); }
                catch (Exception e) { HaloLog.Warn("Hub: the ring failed: " + e); }
            }
        }

        private void BuildSky()
        {
            HaloModelBuilder.Prepared p = HaloUiArt.SkyMesh;
            var root = new GameObject("Halo Hub Sky Model").transform;
            root.SetParent(_sky, false);
            int parts = 0;
            for (int i = 0; i < p.SubMeshes.Count; i++)
            {
                string shader = ShaderName(p, i);
                Material m;
                if (shader.EndsWith("space")) { m = Unlit(Color.white, HaloUiArt.Texture("sky\\space\\bitmaps\\stars", 0), 1001, false, 0); m.SetTextureScale("_BaseMap", new Vector2(6f, 3f)); }
                else if (shader.EndsWith("galaxy")) m = Unlit(new Color(1f, 1f, 1f, 0.9f), HaloUiArt.Texture("sky\\sky_x10\\bitmaps\\galaxy", 0), 2501, true, 2);
                else if (shader.EndsWith("threshold")) m = Unlit(Color.white, HaloUiArt.Texture("sky\\planets\\threshold space", 0), 2502, true, 0);
                else if (shader.EndsWith("basis")) m = Unlit(Color.white, HaloUiArt.Texture("sky\\planets\\basis space", 0), 2503, true, 0);
                else continue;
                MeshPart(root, "Sky " + System.IO.Path.GetFileName(shader), p, i, m, false);
                parts++;
            }

            _skyMat.SetColor("_BaseColor", Color.black);
            HaloLog.Info("Hub: the menu sky (sky\\sky_ui\\sky_ui) built: " + parts + " parts, a " + (20400f * HaloSpace.Unit * SkyScale / 1000f).ToString("0.0") + " km dome round the head.");
        }

        private void BuildRing()
        {
            HaloModelBuilder.Prepared p = HaloUiArt.RingMesh;
            HVec3 o = HaloUiArt.RingOffset, r = HaloUiArt.RingRotation;
            _ringPivot = new GameObject("Halo Hub Ring").transform;
            _ringPivot.SetParent(_scene, false);
            _ringPivot.localPosition = HaloSpace.Pos(o) * RingScale;
            _ringPivot.localRotation = HaloSpace.Euler(r.X, r.Y, r.Z);
            _ringSpin = new GameObject("Spin").transform;
            _ringSpin.SetParent(_ringPivot, false);
            for (int i = 0; i < p.SubMeshes.Count; i++)
            {
                string shader = ShaderName(p, i);
                Material m;
                if (shader.EndsWith("inner ring")) m = Lit(Color.white, HaloUiArt.Texture("scenery\\halo\\bitmaps\\halo inner ring", 0), 0.35f, new Color(0.10f, 0.11f, 0.13f));
                else if (shader.EndsWith("outer ring")) m = Lit(Color.white, HaloUiArt.Texture("scenery\\halo\\bitmaps\\halo outer ring", 0), 0.55f, new Color(0.07f, 0.075f, 0.08f));
                else if (shader.EndsWith("shadow")) m = Unlit(new Color(1f, 1f, 1f, 1f), HaloUiArt.Texture("scenery\\halo\\bitmaps\\halo ring inner shadow", 0), 2510, true, 3);
                else m = Lit(Color.grey, null, 0.3f, Color.black);
                MeshPart(_ringSpin, "Ring " + System.IO.Path.GetFileName(shader), p, i, m, !shader.EndsWith("shadow"));
            }

            // the platform takes the ring's outer hull texture (the Forerunner panels)
            Texture hull = HaloUiArt.Texture("scenery\\halo\\bitmaps\\halo outer ring", 0);
            Material fm = _floor.GetComponent<MeshRenderer>().sharedMaterial;
            if (hull != null && fm != null) { fm.SetTexture("_BaseMap", hull); fm.SetColor("_BaseColor", new Color(0.85f, 0.9f, 0.9f)); fm.SetTexture("_EmissionMap", hull); }
            Vector3 c = _ringPivot.position;
            HaloLog.Info("Hub: the ring (scenery\\halo\\halo) built x" + RingScale + ": radius about " + (19.1f * HaloSpace.Unit * RingScale / 1000f).ToString("0.00") + " km, centre " + (c - (transform.position + Vector3.up * EyeHeight)).ToString("0") + " m from the eye, turning " + TurnDegreesPerSecond + " degrees/s.");
        }

        private static string ShaderName(HaloModelBuilder.Prepared p, int sub)
        {
            int si = p.SubShaders[sub];
            HaloModel model = p.Model;
            HaloTag t = si >= 0 && si < model.Shaders.Count ? model.Shaders[si].Shader : null;
            return t != null ? t.Name : "";
        }

        private void MeshPart(Transform parent, string name, HaloModelBuilder.Prepared p, int sub, Material m, bool shadows)
        {
            int[] tris = p.SubMeshes[sub];
            // only the vertices this part uses
            var map = new Dictionary<int, int>();
            var pos = new List<Vector3>(); var nor = new List<Vector3>(); var uv = new List<Vector2>(); var tan = new List<Vector4>();
            var idx = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++)
            {
                int v = tris[i], n;
                if (!map.TryGetValue(v, out n))
                {
                    n = pos.Count;
                    map[v] = n;
                    pos.Add(p.Positions[v]); nor.Add(p.Normals[v]); tan.Add(p.Tangents[v]);
                    Vector2 t = p.Uvs[v];
                    uv.Add(t);      // as Halo: the texture rows went up top row first, so Halo's v (down the picture) samples them as they are
                }

                idx[i] = n;
            }

            var mesh = new Mesh { name = name };
            if (pos.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(pos); mesh.SetNormals(nor); mesh.SetTangents(tan); mesh.SetUVs(0, uv);
            mesh.SetTriangles(idx, 0);
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = shadows;
        }

        // ------------------------------------------------------------------ materials (the hub's own; URP Lit / Unlit)

        private Material Lit(Color c, Texture tex, float smooth, Color emission)
        {
            Shader sh = HaloShaders.Find("Universal Render Pipeline/Lit");
            var m = new Material(sh) { name = "Halo hub lit" };
            m.SetColor("_BaseColor", c);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.2f);
            if (emission.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                if (tex != null) m.SetTexture("_EmissionMap", tex);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            _mats.Add(m);
            return m;
        }

        /// <summary>Blended parts go in queues above 2500: a deferred renderer draws queues up to 2500 in its G-buffer pass, which ignores blending (seen in the lab).
        /// URP Unlit: <paramref name="blend"/> -1 opaque, 0 alpha, 2 additive, 3 multiply (URP's _Blend values); ZWrite off for the sky queue.</summary>
        private Material Unlit(Color c, Texture tex, int queue, bool transparent, int blend)
        {
            Shader sh = HaloShaders.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh) { name = "Halo hub unlit" };
            m.SetColor("_BaseColor", c);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", blend);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (blend == 2) { m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One); }
                else if (blend == 3) { m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.DstColor); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.Zero); m.EnableKeyword("_ALPHAMODULATE_ON"); }
                else { m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
                m.SetFloat("_ZWrite", 0f);
            }

            if (queue < 2000 || (queue >= 2501 && queue <= 2503)) { m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f); }
            m.renderQueue = queue;
            _mats.Add(m);
            return m;
        }

        private static void NoShadows(MeshRenderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ------------------------------------------------------------------ the panel

        /// <summary>(Re)builds the room's menu panel; the caller fills the pages (HaloFront).</summary>
        public HaloMenuPanel BuildPanel(HaloMenuPanel.Tab tab, string status, bool error)
        {
            if (_panel != null) Destroy(_panel.gameObject);
            _panel = HaloUi.MakeCanvas("Halo Hub Panel", transform, HaloMenuPanel.Width, HaloMenuPanel.Height, PanelMetresPerUnit);
            _panel.transform.localPosition = PanelCentre;
            _panel.transform.localRotation = Quaternion.identity;
            _placer = _panel.gameObject.AddComponent<HaloPanelPlacer>();
            _placer.VrDistance = PanelCentre.z;
            _placer.What = "hub menu";
            _menu = HaloMenuPanel.Build(_panel.transform, HaloMenuPanel.Width, HaloMenuPanel.Height, tab, "Hub", status, error, true);
            _menu.DecorateForRoom(_placer, PanelMetresPerUnit);
            return _menu;
        }

        public void SetPanelShown(bool shown)
        {
            if (_panel != null && _panel.gameObject.activeSelf != shown) _panel.gameObject.SetActive(shown);
        }

        /// <summary>The hub on / off (a match loads / runs: off).</summary>
        public void SetShown(bool shown)
        {
            if (_shown == shown) return;
            _shown = shown;
            gameObject.SetActive(shown);
            if (shown) ApplyLook(); else RestoreLook();
            HaloLog.Info("Hub: " + (shown ? "shown" : "hidden") + ".");
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            TryBuildScene();
            Camera head = HaloPlayer.Head();
            if (head != null) _sky.position = head.transform.position;
            if (_panel != null && _panel.gameObject.activeInHierarchy) _panel.worldCamera = head;
            if (_ringSpin != null)
            {
                _spin = (_spin + TurnDegreesPerSecond * Time.deltaTime) % 360f;
                _ringSpin.localRotation = Quaternion.AngleAxis(_spin, Vector3.forward);      // the ring's axis: Halo y = Unity z of the model
            }

            // the first placement of the menu turns the scene so the game's view (the menu camera's forward) is behind the menu
            if (!_turned && _placer != null && _placer.Placed)
            {
                _turned = true;
                Vector3 f = _panel.transform.forward;
                f.y = 0f;
                if (f.sqrMagnitude > 1e-4f) TurnTo(f.normalized);
            }
        }

        /// <summary>The menu camera's forward (Halo +x, yaw 0) along <paramref name="forward"/> (the scene and the sky).</summary>
        public void TurnTo(Vector3 forward)
        {
            Quaternion q = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, -90f, 0f);
            _scene.rotation = q;
            _sky.rotation = q;
            HaloLog.Info("Hub: the ring scene faces " + (Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg).ToString("0") + " degrees (behind the menu).");
        }

        /// <summary>True when feet are off the platform or far below / above it (the hub's keep rule).</summary>
        public static bool IsAway(Vector3 feet)
        {
            Vector3 d = feet - HaloSpace.RoomOrigin;
            return new Vector2(d.x, d.z).magnitude > KeepRadius || d.y < -3f || d.y > 25f;
        }
    }
}

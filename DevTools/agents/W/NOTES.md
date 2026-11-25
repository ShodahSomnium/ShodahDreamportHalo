# W notes (World: map look + collision)

## Status
Working and lab-verified: HaloWorld.Build for all 19 MP maps (BSP with lightmaps through an atlas, the game's shaders, collision, sky,
fog, placed objects), model shading for every module (winding fix, object light from the lightmap under the model, change colours).
- Lab `world` (Blood Gulch): built in 0.45 s (worker ~100 ms, worst main-thread step 16-31 ms), 17 meshes / 5503 tris, atlas 512x512,
  collision 5 pieces / 6162 tris, 49 objects; ray down hits collision from 72/72 starts; no start outside the BSP; pictures look like the
  game (grass detail, cliffs, bases, teleporters, Threshold + Basis + ring in the sky, outdoor fog).
- Lab `world-all`: all 19 build (0.1-0.6 s each, worst step <= 17 ms), every start location hits collision on every map, no start outside.
  `Outside` below Boarding Action is false (that BSP encloses the void under the ships) - Bounds-based kill height covers it.
- Lab `world-models`: warthog, cyborg (red change colour), ghost, AR, plasma rifle front-facing and shaded (self-illum, chrome).
- Lab `world-look` (any map, -haloviews "x,y,z,yaw,pitch;..."): Beaver Creek water is a vivid blue reflection (tag: reflection tint
  0.17,0.22,0.96 x1) - plausible, not compared side by side.

## Files
- Data/Bsp/HaloBsp.cs - sbsp: render sections (shader, lightmap page, vertices 56 B + lightmap vertices 20 B, triangles), collision,
  fog planes/regions/palette, clusters (sky/fog), leaves -> cluster, `ClusterAt(x,y,z)` (Halo space).
- Data/Bsp/HaloCollisionBsp.cs - any collision BSP (sbsp + coll nodes): surfaces as edge rings, planes, bsp3d nodes, `Leaf(x,y,z)`.
- Data/Shaders/HaloShaderDef.cs - every shader class read into numbers (senv, soso, schi, scex, sgla, swat, smet, spla).
- Data/World/HaloSky.cs, HaloObjectDef.cs (+HaloCollisionModel), HaloLightGrid.cs (lightmap under a point), HaloAtlas.cs (lightmap atlas).
- Game/World/HaloWorld.cs (API + Build), HaloWorldJob.cs (worker thread), HaloWorldLook.cs (sky/fog/light/RenderSettings),
  HaloCollision.cs (collision meshes, `AddModelColliders(coll, built, ...)`), HaloObjectLight.cs, HaloWorldDriver.cs (indoor/outdoor fog),
  HaloMaterials.cs, HaloTextures.cs, HaloModelBuilder.cs.
- Shaders/HaloCommon.hlsl, HaloEnv.shader, HaloModel.shader, HaloTransparent.shader, HaloGlass.shader; Resources/Halo{Env,Model,Transparent,Glass}.mat
  (all four + X's HaloFx.mat listed in the scene's `_shaderCarriers`).
- Editor/HaloLabWorld.cs - labs `world` (-halomap name), `world-models`, `world-all`.
- DevTools/agents/W/harness - my own dotnet harness (only Data/Map, Bitmaps, Models, Bsp, Shaders, World): bsp, leaf, sky, shaders, sstats,
  lm, cube3, nodes, mwind, light, defs <EnumName...>.

## Facts found (verified offline, harness)
- BSP vertex data: material +216 data = n x 56 rendered + n x 20 lightmap vertices (all 19 maps parse, indices in range, 0 problems).
- Winding: Halo BSP and model triangles are clockwise about their normal (5499/5503 BG BSP tris, all models) -> after the axis swap the
  order is Unity's front face: KEEP the order. Collision rings are counter-clockwise about their plane: reversed for Unity.
- Node rotations: conjugate (cyborg markers on own-node vertices 0.023 vs 0.36 units as stored).
- Cube maps: stored level-major (all faces of level 0, then level 1 ...), faces D3D order of Halo axes; sampled with Halo-space vectors.
- Lightmaps: R5G6B5, sunlit texels saturate at 255 -> light = lightmap x 1 (not 2x).
- Point-in-BSP: every start location of the 19 maps is in a leaf; far points are outside (-1).
- Retail BSP material lighting fields (ambient / distant lights) are tool defaults (0.2 grey, sun 1,1,1 at -0.577...): useless; objects are
  lit from the lightmap under them instead (HaloLightGrid).
- Shader stats over 19 maps: senv 300 (all detail function 0 = double biased multiply), soso 1983, schi 783, scex 183, sgla 117, smet 76,
  spla 19, swat 5; no sotr.

## API for other modules
- `HaloWorld.Build / Unload / Ready / Bounds / Root / Bsp`, `SurfaceMaterial(hit)`, `IsClimbable(hit)`, `Outside(p)`, `ClusterAt(p)`,
  `LightAt(p, out colour, out incident)`, `ObjectRotation(yaw,pitch,roll)` (placement rotation for HaloModelBuilder models).
- `HaloMaterials.Get(shader[, sky])`, `ForBsp`, `ExtraLayers`, `SetChangeColor(renderer, c)`, `SetDefaultLight`, `SetFog`, `ResetGlobals`.
- `HaloModelBuilder.Options.Static / Sky / Light`, `Built.Mesh`.
- `HaloObjectLight.Attach(root, static)` (the builder adds it), `HaloCollision.AddModelColliders(collTag, built, root, layer)`.

## Open
- Not drawn yet: lens flares (sun), weather (snow / rain), BSP detail objects (grass sprites), BSP decals, machine animation (Infinity
  beam emitters, Wizard alert lights stay in their rest pose). Glass (Carousel / Prisoner floors) and planar fog under water not pictured.
- Shader maths reconstructed from the game (detail blend order, bump attenuation, reflection fresnel, object light shares in
  HaloObjectLight): checked only by eye against memory of the game.
- The lab hides "Halo Hub" itself: the hub stays visible until phase Playing (F), and F's RestoreLook may reset RenderSettings fog after
  the world applied it - HaloWorldDriver re-applies within 0.25 s.
- labproject (slot B) was an incomplete copy (no Packages/Library) - W re-copied it on 2026-10-19.

## Campaign (phase 2) - status 2026-10-19
- Multi-BSP: `HaloWorld.Build(map, sc, root, progress, startBsp)` builds the start BSP (+ objects unless `PlaceObjects = false`, K's
  choice) then every other BSP in the background (worker job per BSP, ~3 ms of main thread a frame, built inactive);
  `SwitchBsp(i)` swaps render + collision + sky/fog + BSP-scoped objects + extras in 1-11 ms (lab a10 0->1: 2 ms, a30 11 ms).
  `ActiveBsp`, `BspBuilt(i)`, `BspActivated`, `BspData(i)`, `PathData(i)` (lazy, built on a worker at the first call; null until ready).
- Collision of every BSP cooked on a worker (Physics.BakeMesh) - no main-thread cooking.
- Lab `world-campaign -halomap <m> [-haloswitch i] [-haloviews ...]`: a10 (9 BSPs) start ready 2.0-2.6 s, all BSPs 6-8 s;
  a30 1.5 s; b30 1.6 s; c10 (6) 2.5 s; d40 (10) 1.25 s. Ray down at the player start hits collision and the start is inside on all five;
  BSP switch + ray down in the new BSP ok. Pictures: a10 cryo bay (correct), a10b space view, a30 lifeboat interior + dusk forest,
  b30 beach with the Cartographer arch and ocean, c10 rainy swamp with fog and rain streaks (weather works), c10 interior (black, as the
  game without a flashlight), d40 start corridor.
- Extras per BSP (HaloWorldExtras): lens flares at the BSP markers (+ sun flare; a10's "fluorescent glow" names no bitmap -> a soft
  procedural glow), BSP runtime decals as static quads (snapped to the collision plane when on one: a10a 51/65), weather particle boxes
  round the camera (snow b40/c40, rain + bugs c10, debris b30) switched by the camera cluster's weather index.
- Devices: `HaloWorld.Devices` / `HaloWorldDevice.Create(tag, root, model)` + `SetPosition` / `SetPower` from the device's own
  "device position" / "device power" animations (a10: 202 of 245 devices animated, all uncompressed).
- Pathfinding: Data/Bsp/HaloPathData.cs (walkable collision surfaces + neighbours + the BSP's pathfinding byte).
- Hitches while the other BSPs build in the background: the steps themselves stay <= 18 ms (logged "longest main-thread step"), but the
  editor's non-incremental GC pauses 100-430 ms a few times (heap ~500 MB: the 91 MB map + all BSPs' data). In a client with incremental
  GC these spread out; reduced garbage (pre-sized mesh lists, no DXT copy, lazy path data).
- Not done: BSP detail objects (grass sprites) - the instance z encoding is not understood (cell size 8 units and x/y = byte/256 of the
  cell verified, z: zref.i*x + zref.j*y + ... fits only some cells); weather polyhedra (no-weather volumes) ignored; lens flare ghost
  reflections along the screen line skipped; sun flare size is ours.
- FIX (alpha-tested environment): Halo cuts alpha-tested shader_environment with the BUMP map's alpha (a30 / Timberland trees_filler +
  treesclump_filler: base DXT1 opaque, bump alpha = tree silhouettes; a10 "burn alpha test"; BG teleporter base whose base alpha is not a
  mask). HaloMaterials sets _Flags.x = 2 (bump alpha) when the bump map has any alpha >= 0.5, else 1 (base alpha; c10_treeline's bump alpha
  is all clear). Lab world-look a30: the distant tree cards are cut out (pictures a30_view0..2).
- Breakable surfaces: per BSP breakable index -> own render mesh(es) + collider(s) (HaloBsp.Section.Breakable +172, collision surface
  breakable index, BreakableSurfaces +364); `HaloWorld.DamageSurface(hit, dmg)` (vitality = globals material +724, effect +740, sound +756),
  `BreakSurface(bsp, i)`, `BreakableSurfacesEnable(bool)`, `BreakableSurfacesReset()`. Data checked offline (a30 3, b40/c40 4-29 per BSP,
  d40 1; render sections and collision surfaces carry matching indices, material 9 glass). Not verified in a lab (needs G's call on hits).

# W - World (map look + collision)

Goal: a Halo multiplayer map in Unity that looks like the game and that players can walk on, exactly where the game has it.

Build (Data/Bsp, Data/Shaders, Data/World, Game/World, Shaders/, Resources/):
1. **Structure BSP render** (scenario_structure_bsp, `HaloScenario.BspRoot(i)`; struct ScenarioStructureBSP in layouts.txt): lightmaps
   -> materials -> rendered vertices (PC cache: uncompressed rendered vertices 56 B + lightmap vertices 20 B, check against the data),
   surfaces (triangles, u16 x3), per-material shader. Lightmap bitmap (the BSP's lightmaps bitmap tag, one image per lightmap index)
   on uv2 - Halo maps are lit almost entirely by lightmaps: get this right first, it is the look.
2. **Shaders**: replace HaloMaterials' inside with the real thing per type: shader_environment (base map, primary/secondary/micro detail
   maps with their blend function and scales, bump map, alpha test, specular/reflection cube map lite), shader_model (base, multipurpose
   map channels: A = color change?, R = reflection, G = self illumination, B = specular - check the order in the defs/comments; detail
   map; self-illumination; change color from the object), shader_transparent_chicago(+extended) stages, glass, water (swat), plasma,
   meter (smet). Custom URP shaders go in Shaders/ with carrier materials in Resources/ and must be listed in the scene's HaloGame
   `_shaderCarriers` (edit only that YAML line block; materials need .meta). Keep shaders stereo-correct (single pass instanced VR:
   UNITY_VERTEX_INPUT_INSTANCE_ID / UNITY_VERTEX_OUTPUT_STEREO etc.) - BO2's Bo2Lit.shader and ETS's EtsMulti/EtsDecal are examples.
   URP depth priming: anything drawn "first" (sky) must go in queue 2501+ (Metroid finding).
3. **Sky** (sky tag: models, animation, fog, lights, indoor/outdoor ambient - Blood Gulch's sky has the ring!), **fog** (fog palette /
   planar fog of the BSP; scene fog must stay ON + Linear or the client strips fog variants; set `RenderSettings.ambientProbe` directly
   - the client does not rebuild it).
4. **Collision**: the BSP's collision BSP (surfaces, edges, vertices; collision materials) -> MeshColliders on HaloLayers.World,
   one-sided / two-sided / invisible / climbable flags handled (invisible walls collide but do not draw; player clip vs bullet-only
   collision as the game's flags say). `HaloWorld.SurfaceMaterial(hit)` returns the collision material type for footsteps/impacts.
   Death barriers / kill volumes if the map has them.
5. **Placed objects**: scenery, machines (devices, e.g. Chill Out / Derelict doors/lifts if MP has them), light fixtures, sound scenery
   (call HaloAudio.Loop for their sound), with their models (`HaloModelBuilder`) and collision models (coll tag -> simple colliders).
   Object definition chain: scenery -> object (struct Object: model at +40?, check layouts) -> gbxmodel + collision + modifier shader.
6. **Model shading for everyone**: HaloModelBuilder + HaloMaterials must make weapons/vehicles look right too (soso multipurpose,
   change colours for team colours, self-illumination). Verify the node rotation convention with a skinned model (e.g. warthog wheels
   in their place; the builder conjugates Halo quaternions - prove or fix).
7. `HaloWorld.Build(map, scenario, root, progress)` as an IEnumerator over frames (heavy parsing on a worker thread, Unity objects on
   the main thread, ~8 ms per frame budget), `Unload()`, `Ready`, `Bounds`.
8. Performance: VR at 90 fps on a mid PC - static batching / combined meshes per material, sensible draw calls, no realtime shadows
   needed (lightmapped; one directional sun from the sky for dynamic objects is fine).

Verify with lab scenarios (`Editor/HaloLabWorld.cs`, e.g. "world-bloodgulch": build Blood Gulch under a root at HaloSpace.MapOrigin,
pictures from the player starts, from above; a walk check: raycast down from every start location must hit collision). Do Blood Gulch
first (outdoor, the classic), then a sample of indoor maps (Chill Out, Prisoner, Derelict/carousel, Hang 'Em High, Boarding Action)
and the rest of the 19. LOOK at the pictures and compare with what Halo looks like (you know the game).
Note the BSP may be in a separate address space (`BspRoot`); vertex/index buffers of the BSP live inside the BSP data block on PC.

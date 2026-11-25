# X notes (Effects)

## Files
- `Data/Effects/HaloFxData.cs` - Unity-free tag readers + cache (`HaloFxLibrary.For(map)`): effe (events/parts/particles), part, pphy,
  pctl (types/states/particle states), cont (+point states), ligh, deca, sprite sheets (`FxSpriteSheet`: sequences -> frames with uv
  rect, registration point, pixel size). All 29 maps parse with 0 failures (harness `DevTools/agents/X/harness`: `dotnet run -- lib`),
  9-90 ms per map.
- `Game/Fx/HaloFx.cs` - the public API (below). `HaloFxRuntime.cs` (effect runs, locations, parts), `HaloFxParticles.cs` (particle pool
  4096 + particle systems + physics + drawing), `HaloFxTrails.cs` (contrails), `HaloFxDecals.cs` (decals 512, lights 8), `HaloFxRender.cs`
  (one dynamic quad mesh per texture x blend function; alpha-blended batches sorted back to front).
- `Shaders/Fx/HaloFx.shader` ("SomniumHalo/Fx") + carrier `Shaders/Fx/HaloFx.mat` (guid df210229a519dbc49ccf7d0fe9bfc2dc) - quads are
  expanded per eye in the vertex shader (single-pass instanced safe), Halo's 8 framebuffer blend functions, URP fog. **Needs to be in the
  scene's HaloGame `_shaderCarriers`** (asked W in REQUESTS.md); without it the log says `Fx: shader SomniumHalo/Fx is missing`.
- `Editor/HaloLabFx.cs` - lab `fx` (pictures in DevTools/lab/fx/).

## HaloFx API (main thread)
- `Play(effect, pos, rot, attach = null)` (stub signature) / `Play(effect, pos, rot, attach, firstPerson)` -> `HaloFx.Handle`.
  `rot` = the effect's own frame, Unity convention (+Z forward). `attach` = follow it (particles flagged "stay attached" move with it).
- `PlayOn(effect, modelRoot, firstPerson)` - for effects whose locations are MARKERS of a HaloModelBuilder model (weapon "fire" effects:
  "primary trigger", "primary ejection"...). The model's +X is taken as forward (Halo axes). Markers are found as "marker:name" children.
- `Impact(effect, point, normal, incoming, attach = null)` - detonation / material response effects: Halo's locations "normal",
  "incident", "negative incident", "reflection", "gravity" come from these (decals project along them).
- `Loop(effect, pos, rot, attach, firstPerson)` + `Stop(handle, now = false)` - effects with loop start/stop events (overheat, jets).
- `Contrail(contTag, Transform follow)` / `Contrail(contTag, Vector3 start)` + `MoveContrail(trail, head)` + `StopContrail(trail)`;
  a followed trail stops by itself when the transform is destroyed or deactivated, then fades out.
- `Attach(objectTag, root, firstPerson = false, lights = true)` -> `Attachments` - an object's own attachments (Object +320): contrails,
  lights, effects (looped), particle systems, looping sounds (HaloAudio.Loop) on its markers (root when missing). `Detach(a)` stops them
  (contrails fade, systems stop emitting). Projectiles: Attach on spawn, Detach on detonation (rocket = exhaust contrail + exhaust
  effect + rocket exhaust pctl + lsnd; plasma bolts = light + contrail; frag = smoke effect + light). `SetLights(a, scale)`.
  For a non-marker root, Unity +Z is its forward (flight direction).
- `Decal(decalTag, point, normal)` (+ its "next decal in chain"). `Flash(lightTag, pos, attach, seconds)` - weapons' muzzle lights are
  object attachments Halo turns on while firing: Combat calls Flash per shot (e.g. weapons\assault rifle\muzzle flash.ligh @ primary trigger).
- `Preload(map)` (Match calls it at map open; ~10-90 ms), `PreloadWorker(map)` (worker-thread variant), `Clear()` (Match calls it on leave).
- `ObjectSpawned` event (tag, pos, velocity m/s) for effect parts that create objects (garbage debris, projectiles) - not created here.
- Settings: `Ambient` (multiplies non-self-illuminated particles), `ViewCamera` (sorting), `CollisionMask` / `DecalMask` (default
  HaloLayers.GroundMask), `LightIntensity` (HaloFxRuntime, 2.5). Stats: `Stats`, `ParticleCount`, `DecalCount`, `LightCount`.
- **Sounds inside effects are played by HaloFx** (snd! parts through HaloAudio.Play at the location / attached transform). E.g. the AR's
  "fire bullet" effect contains the fire sound and the casing sound: Combat must not play them a second time.
- First person: pass firstPerson = true for the local player's own gun (Halo's "only in first person" particles: e.g. the AR's FP flash
  petals); default false = third-person flashes (what remote players show).

## How Halo data is interpreted (verified / ours)
- Effect events run in order: delay, then parts once + particles over the duration by the distribution function; skip fraction; loops
  (loop start..stop) at most one pass per game tick (1/30 s). Locations: "" = the frame, special names above, else markers.
- Particles: size = radius (world units) x frame pixels x cached "sprite size" (1/largest frame px) for sprite sheets; whole images draw
  radius wide. Registration point honoured. Orientation screen-facing / parallel to velocity (sprite x along it) / perpendicular.
  Sequences: initial (played once) then looping; random start frame / mirroring / backwards flags; fade in/out; radius animation.
  "dies in air" particles (bubbles, water smoke) are not created (no water simulation).
- Point physics: gravity 0.0035651 wu/tick^2 (9.77 m/s^2) x air gravity scale (cached). Drag: a = -(air friction / mass scale) / r |v| v
  (cache values; Invader: friction x 10000, mass = density x sphere of 1 wu) - formula is OURS (consistent units; Halo's integrator unknown).
  Collisions (flag "collides with structures") raycast on CollisionMask: bounce with elasticity / surface friction, rest, dies-at-rest,
  collision effect (sound or effect) - budget 192 rays/frame.
- Particle systems: type states (duration + transition, colour multiplier, creation rate) and particle states (scale, colour, rotation,
  physics, bitmap); initial count at start. Explosion creation physics: speed between the type's constants 0..1 read as wu/tick (ours);
  system particle size = scale x frame pixels (diameter, ours).
- Contrails: points at "point generation rate", states duration + transition (width, colour), viewer-facing strip, u along the length x
  "texture repeats u", scrolled by "texture animation u".
- Lights: Unity point lights (pool 8), range = radius x modifier x 3.048 m; ligh "duration" read as TICKS (frag 45 -> 1.5 s; ours, the
  tag's "seconds" makes no sense); colour starts at the brighter bound and falls to the other by the falloff function; no-duration lights
  flash for the event's duration (min 2 ticks). Intensity 2.5 (ours).
- Decals: raycast from the part's location along its direction (reach = the part's velocity in wu), random sprite / rotation, radius x
  the part's radius modifier when set, lifetime then decay fade, chain, blend functions as the tag (multiply / double multiply / add).

## Colour
- Tints / colours are Halo gamma values: the shader converts vertex colours to linear (linear project). Double multiply (bullet holes,
  scorches) is done in gamma like Halo: 50% grey leaves the surface unchanged (Blend DstColor Zero with (2 x src_gamma)^2.2).
- "Alpha blend" particles on images without alpha (X8R8G8B8 / R5G6B5 / Y8 / P8: the explosions' lens-flare sprites) are added instead.

## Status (2026-10-19)
- Lab `fx` passed, pictures looked at (DevTools/lab/fx): frag explosion (fireball -> rising smoke column, scorch decal, lights), AR fire on
  the AR model (third-person star flash, first-person petals, casing ejected from the ejection marker, smoke), plasma rifle impact (cyan
  flash, gravel bouncing to rest, glow decal fading), AR dirt impacts (dust kick), rocket in flight via `Attach` (exhaust contrail + pctl
  fire/smoke puffs) and its explosion (lens flare flash, fire, smoke, scorch), plasma grenade (blue burst), contrails of AR / plasma /
  needler / sniper / fuel rod. `Clear()` empties the pool. No exceptions from Fx; no allocations in steady state by design (not profiled).
- NOT verified: in the Somnium client / VR (per-eye billboards are by construction), lights' look on a real BSP, decals on non-flat
  surfaces (flat quads, no clipping: they overhang edges), particles cut by the ground (no soft particles), water (none), weather (none),
  first-person-only effects drawn over the gun (no special depth), object parts (only the ObjectSpawned event), effect "scale" inputs.
- Player.log: `[SomniumHalo] Fx: <map> effects ready: N tags ... in X ms` at match load; `Shader 'SomniumHalo/Fx': carried`; problems:
  `Fx: shader SomniumHalo/Fx is missing`, `Fx: <tag> skipped (...)`, `Fx: ... failed: ...`.

## 2026-10-19 "bullet sprites exist for too long" (client test)
- Cause 1 (bug, fixed): FxBatch.End uploaded vertices before shrinking the index buffer after a batch had been hidden -> Unity refused
  ("Mesh.vertices is too small") and the mesh KEPT its old quads: sprites froze in place and stayed. Fixed (lab log: 0 such errors).
- Cause 2 (tag-faithful but wrong for VR): AR / pistol casings live 30 s (tag). Halo draws the local player's casings in the view-model
  space where they never land; ours landed at the VR player's feet (15 a second). Now: first-person particles (camera mode "only first
  person" / "first person if possible" of the local gun, `firstPerson = true`) end at their first world contact; any debris at rest is
  capped (`MaxResting` 96, oldest first). Contrails of bullets end at once on Detach (verified).
- Lab `fx-burst` (AR burst at a wall, local + remote gun, 90 fps): after 0.5 s smoke puffs at the wall + falling debris; after 2 s a few
  specks at the wall foot; after 5 s only the remote gun's casings on the ground and the bullet-hole decals (300 s, as the tag). No contrail.
- Combat's LineRenderer tracer fallback is off (`HaloProjectiles.PlainTracers = false`): not touched.

## 2026-10-20 collision sound flood (A)
- Particle collision effects/sounds: first contact per particle only + max 10/s per tag (ours; Halo has no flag). audio-sfx a30: metal_chips 9440 -> 250 asks.

## Workstream 3 (2026-11-04): effect damage parts
- HaloFxRuntime now applies "jpt!" parts: `HaloCombat.EffectDamage(part, part position, top-level effect, creator)` (Combat decides: world
  owner only, projectile detonation / impact effects skipped - their blast is the projectile's). FxRun carries RootTag (top-level effect,
  inherited by nested "effe" parts) and Creator (HaloCombatFx.Play(..., creator) via HaloFxRuntime.NextCreator, else the unit the effect
  hangs on). "cdmg" (continuous damage) is still not done. See G/NOTES.md "Workstream 3".

## 2026-11-10 plasma bolts invisible + strange muzzle flash (client report)
- Cause 1: Halo draws a light's LENS FLARE (ligh +172 -> lens). Plasma pistol bolt = only weapons\plasma pistol\plasma.ligh (lens
  flashlight_assault rifle: flares_generic, green from the light colour); plasma rifle / ghost / banshee bolt = bolt.ligh (lens "flares
  weapon lens", 0.125 wu) + a thin contrail. We drew no lens flares -> the PP bolt was nothing, the PR bolt a 10 cm streak.
  New `Game/Fx/HaloFxFlares.cs` (pool 256): flares of PROJECTILE attachments' lights (HaloFx.Attach, objectTag.Is("proj")); Detach /
  SetLights handle them; lens read in HaloFxData (FxLensDef / FxLensReflection, FxLightDef.Lens + CutoffAngle). Rules (ours): screen
  facing, world size = reflection radius; tint alpha 0 = the light's colour; fade out between near and far fade distance (as World's
  flares); angle falloff only for directional lights (bolt lights are all-around); ghosts (position != 0) and reflections scaled by camera
  rotation / strafing are not drawn; pulled toward the eye by its radius. Drawn in batch layer 7 with shader `_GammaAdd` = 1 (adds the
  gamma value: Halo adds in gamma, a linear add of a glow vanishes in daylight). NOT drawn yet: flares of vehicles / scenery / flashlights / AI weapons.
- Cause 2 (the "strange" flash): "stay attached" particles had NO point physics: the plasma weapons' petals ("flash c generic med",
  0.7-1 wu/s, life 1 s, cold smoke puff drag) flew 1-3 m out of the barrel. Attached particles now get their drag (local frame).
- Contrails' head is taken at draw time (a 150 m/s bolt's trail lagged its glow by one frame = 2.5 m).
- Lab `fx-plasma` (HaloLabFx): PP / PR / needler / fuel rod first-person shot frames, side + behind flights, remote ReplayShot; ghost /
  banshee bolts. Pictures DevTools/lab/fx-plasma. `fx` re-run: passed, AR flash unchanged.

## 2026-11-10 (round 2) plasma rifle held fire / overheat, particle size, PP bolt halo
- Particle SIZE was 2x Halo's: the effect particle "radius" is the FULL extent of the particle's largest frame (frame px x cached
  "sprite size"), not the half. Proof = casings, each sprite cropped tight: sniper 0.037 wu = 11.3 cm (14.5x114: 11.4), warthog gun
  0.033 wu = 10.1 cm (12.7x99: 9.9), AR 0.015 wu = 4.6 cm (7.62x51: 5.1), pistol 0.01 wu = 3 cm. HaloFxParticles.WriteParticle now
  halves (sprites and whole images). Every effect particle is half as big as before (AR flash, smoke, explosions, petals); pctl
  systems / contrails / decals / flares unchanged.
- Plasma rifle petals ("flash c generic med"): tag = life 1 s, fade in/out 0/0, radius animation 1..1, framebuffer fade mode none,
  tint alpha 1 (ColorARGB), 3 one-sprite initial sequences, no looping/final, self-illuminated, additive, scales A/B none: full bright
  for 1 s IS the tag (no fade to apply). Flag "animate once per frame" (all flash parts) not modelled (one shape per particle).
- combat/12_plasma_rifle "column 2 m+ above the gun" = NOT the muzzle: the plasma IMPACT sparks ("impact cyborg armor":
  sparks energy trail 10-25 per hit, 2-3.25 wu/s, cone 65 deg, "stones" physics, life ~1.1 s) of ~13 hits on the far dummy 8 m
  away, rising to y 4.4 m (lab log "12: ... farthest particles"). Tag-faithful speeds; now half the size.
- Overheated effect (loop 0..0, one 0.05 s event at "vent": 2-4 fast cyan puffs + slow plasma overheat puffs) is played ONCE by
  Combat (HaloHandling: PlayOn on JustOverheated) - Halo probably loops it while venting (G's call, not changed).
- Lens flares: "radius / brightness scaled by rotation (and strafing)" now read as scaled by the light's facing towards the eye
  (lens falloff / cutoff angles) - used by head/tail lights, flashlights, banshee cockpit and the PP bolt's 0.7 wu halo. A PP bolt
  coming at you = big green ball + core; flying away = core only. "distance from centre" still unscaled.
- Lab fx-plasma: held fire + overheat (pr_held_fp_/pr_held_tp_ 0.2/0.6/1.2/2.0 s, rel_0.3/rel_1.0; `-held 1` runs only that) and
  <weapon>_incoming pictures. HaloFx.DebugParticles(from, n) = lab-only listing (allocates).

## 2026-11-10 (round 3) plasma pistol bolt = light volume, flash flicker, impact spark cone
- ROOT CAUSE of the faint PP bolt: projectiles' WIDGETS (Object +332) were never drawn. Every plasma / needle / ghost / banshee bolt and
  the rocket carry a light volume ("mgs2"): weapons\plasma pistol\bolt.mgs2 = 20 glow sprites along the flight axis, offset -0.5 wu,
  length 0.7 wu, radius 0.035 -> 0.045 wu (exp 5), tint (a,r,g,b) 0.05,0.4,1,0.475 -> 0.35,0.333,1,0.451, fade 48..50 wu, no map ->
  globals rasterizer "glow" bitmap (rasterizer\glow, 64x64 linear round falloff, RGB = A). New: FxLightVolumeDef/Frame +
  HaloFxLibrary.Volume (Data), HaloFxRuntime.StartVolume/DrawVolume (HaloFxFlares.cs, pooled with the flares, layer 7 gamma add),
  HaloFx.Attach starts them for projectiles (HaloFx.VolumeCount). Reading (ours where not in the tag): sprite i at t = i/(n-1),
  pos offset + length*t^offexp along forward, radius/colour/brightness(alpha) hither->yon by t^their exponents, perpendicular /
  parallel brightness blended by |cos(axis, eye)|, frame 0 only, sprites within 0.3 m of the eye fade (first-person tail).
- Lens flares: the lens's falloff/cutoff angles now apply to EVERY light (they are the lens's: needle lens 100/160 deg on an
  all-around light only makes sense that way); the PP flare (AR flashlight lens 20/75 deg) shows only when the bolt comes at you.
  "Radius/brightness scaled by" = the input that picks between the BOUNDS (projectile lenses have equal bounds) - the round-2
  "facing" multiplier is dropped. Reflections flagged "radius NOT scaled by distance" (screen size; suns) are skipped. Model-root
  projectiles (needle, rocket) use +X as forward for flare angles / volumes (FxFlare.XForward).
- Particle flag "animate once per frame" (all flash particles): a new sprite every game tick (1/30 s, ours): multi-frame sequences step a
  frame, one-sprite sequences swap to another initial (or looping) sequence. The PR petals' 1 s life is the tag (unchanged).
- "velocity cone angle" (effect particles and object parts) is the FULL apex angle: half of it either side (tags use 3.14 for gravel
  hemispheres over the ground and 6.28 for all-around blood). Read as a half angle, the plasma impact sparks (cone 1.13, 6-10 m/s,
  location "reflection" = back towards the shooter) climbed to y 3.7 m (combat lab log, impacts and particles above 1.2 m listed).
  Contrail cones unchanged. Lab-only: HaloFx.DebugImpacts / DebugParticlesAbove (allocate).
- combat/12 "column" re-checked with the new log (impacts + particles above 1.2 m): after the cone fix the sparks stay below 1.6 m
  (were up to 3.7 m), but the tall cyan column in the picture is the LOCAL gun's first-person petals: "plasma rifle upper/lower fire"
  emits flash c generic med with relative direction (-0.5,0,0.87) (up, 30 deg back) at 0.7 wu/s, stay attached, life 1 s; at 10
  rounds/s they stack 0.2 m above the prongs, 0.4 m from the eye. Tag-faithful except the drag formula (ours). Not changed.
- Labs 2026-11-10 r3: fx-plasma, combat, fx passed (pictures pp_side_flight / pp_behind_a,b / pp_incoming / pp_fp_06 /
  pr_side_flight / needler_side_flight / pr_held_fp_1.2, combat/12, fx rocket_fly_2 / rocket_boom_01_30 / plasma_hit_01_10 / ar_tp_01).

## 2026-11-10 (round 4) plasma rifle FP petal column, PP overcharge flare size, charged bolt contrail edge
- Petal column cause: the drag INTEGRATOR, not the tag values. Halo steps point physics once per game tick (1/30 s); the petals'
  quadratic drag per tick k|v| = (air friction 350 / mass scale 118.6) / r(0.05-0.06 wu) x 0.023 wu/tick = 1.25-1.8, i.e. stiff enough
  to stop the particle within its first tick. Our implicit per-frame step let them creep on for the whole 1 s life (0.07 wu = 21 cm),
  so every round's petal sat at a different height: a column above and below the prongs (FP: the up/down petals point at the eye).
  New `HaloFxParticles.PhysicsTick` (all effect + system particles, attached ones without gravity): at each game tick crossed,
  gravity x air gravity scale, then v *= 1 - k|v|/30 clamped at 0 (stops instead of reversing); position moves with the current
  velocity between ticks (smooth). Petals now move v/30 = 2-3 cm and stay: the rounds' petals overlap into one steady cross.
  The tick integrator is ours (no Halo code), the units / values / 30 Hz tick are Halo's. Unit-free check: k v^2/r gives the same
  number in wu/tick and m/s. Halo FP framing (fp.antr "first-person idle" frame 0): the PR muzzle is 0.37 wu ahead, 0.08 right,
  0.06 down of the camera - the column would have been in Halo's view too, so the view model is not the cause.
  Buoyancy read: cached air gravity scale = (0.0011 - density) / (0.0011 + density) (air 0.0011 g/ml; water 1.0 for the water scale).
- Overcharge flare: a lens reflection's radius / brightness BOUNDS now follow the light's intensity (HaloFxFlares input = f.Scale,
  the attachment function: PP charge; 1 = full) when the lens has no "scaled by" input - overcharge.lens radius 0..0.175 wu,
  brightness 0..1 (overcharge.ligh radius modifier 0..1 likewise). Before: random radius, only brightness followed the charge.
  Equal-bound projectile lenses unchanged.
- Contrail end fades: flags "first point unfaded" (1) / "last point unfaded" (2) - without them the head / oldest end fades to 0.
  Only the AR / scorpion bullet tracers have flag 1. The charged bolt's 0.5 wu wide head was a hard vertical edge from the side.
- Labs: fx-plasma -held 1 (pr_held_fp_1.2: small steady cross at the prongs, no column; pr_held_tp_1.2 petals as before;
  new pp_charge_0.15 / 0.50 / 1.00 / pp_charge_side_1.00 and pp_charged_side_0.15 / 0.35 from HaloLabFx.PistolCharge),
  fx (frag / rocket / AR / plasma impact / contrails look as before), fx-plasma full (pr/pp/needler side flights visible). All passed.
- Open: the full-charge flare (0.53 m) still fills a first-person view; flare "stair steps" seen against the lab's skybox disc edge.

# X - Effects

Goal: the game's own visual effects at runtime: muzzle flashes, shell casings, bullet impacts per material, plasma bolts' glow,
needles, rocket and fuel-rod trails (contrails), explosions (frag, plasma, rocket, vehicle destruction), shield hit flare, blood,
dust/sparks, teleporter / flag / hill visuals if they are effects, and dynamic lights of effects.

Build (Data/Effects, Game/Fx, Shaders/Fx):
1. effect tag (effe): events (delays) -> parts (any tag type: particle (part) spawns, light (ligh), decal (deca), sound (snd!: call
   HaloAudio.Play), damage effect (ignore - Combat does damage), object spawns) and particles block (particle tag + count/velocity/
   radius/relative direction/emitter shapes). Struct Effect / EffectEvent / EffectPart / EffectParticle in layouts.txt.
2. particle tag (part): bitmap (sprite sequences), shader flags, framebuffer blend (alpha blend / add / multiply ...), radius,
   lifetime, fade, animation rate, rotation, colour; particle_system (pctl) for weather/continuous systems; contrail (cont) for trails
   (projectile tags reference them); light (ligh) for flashes; lens flare (lens) optional; decal (deca) for bullet holes / scorches.
3. Runtime: a pooled, allocation-free particle renderer (one dynamic mesh per material, camera-facing quads; stereo-correct shaders
   in Shaders/Fx with carrier materials in Resources/ - ask the World agent (REQUESTS.md) to list your carrier materials in the scene's
   `_shaderCarriers`, or add the lines yourself if W agrees), decals as projected quads on the hit surface, short-lived point lights
   (budget: a few at once).
4. Implement the `HaloFx` stub: `Play(effect, pos, rot, attach)`, `Preload(map)`, `Clear()`; add `Contrail(contrailTag, Transform)`
   returning a handle the projectile code updates/stops, and `Decal(decalTag, hit)` if useful. Combat / Vehicles / Net call these.
5. Material effects for impacts: the globals' materials block + weapon/projectile "material responses" choose effects per surface
   (Combat calls you with the effect tag; you just play it).
Lab: `Editor/HaloLabFx.cs` - spawn effects (frag explosion, AR muzzle flash, plasma impact, rocket trail) in front of the camera over a
grey floor, take picture sequences, LOOK at them.

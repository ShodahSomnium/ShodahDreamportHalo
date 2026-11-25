# D - Desk (monitor play: camera, the avatar holding the weapon, third person, Halo character animations)

Goal: playing on a monitor (no headset) works and looks right, and every player's AVATAR holds its weapon like a Halo Spartan for
everyone who sees it (monitor players; VR players' hands are tracked). User: "do 3rd person the same way" (as Black Ops 2 did it).
Read the BO2 Dreamport's story in its README iterations 11-31: the client's camera (Cinemachine; first person = zoom at minimum,
forced by queuing scroll notches through the Input System - Bo2FirstPerson, already ported as Game/Vr/HaloFirstPerson.cs), the clashing
client keys held off in a match, the avatar found with Bo2AvatarFind (Skyrim's find-by-head; the avatar lives under a RootsHolder, not
under the Bridge body), posing after the client animated it (LateUpdate / beginCameraRendering), the hidden BO2 soldier "puppet" playing
the game's third-person animations with the avatar's humanoid bones posed onto it (Bo2Puppet, Bo2PuppetPoser*, Bo2DeskPuppet,
Bo2DeskBody, Bo2DeskRig (procedural IK fallback), Bo2AvatarCopies (mirror/teleport copies posed too), Bo2VmGrip (hands on the grip),
Bo2DeskCamera (bob, kick), Bo2ArmsHide/head hide in first person). The lessons there are expensive - reuse them.

For Halo:
1. Data/Anims: the Halo animation graph (model_animations antr: units / weapons / animations blocks, per-animation frame data: node
   rotations (quantised quaternions), translations, scales; compressed animations flag - check what the MP maps use; struct
   ModelAnimations* in layouts.txt) for the MP cyborg (characters\cyborg\cyborg.model_animations or the one the cyborg_mp biped uses):
   stand/crouch idle per weapon class ("stand rifle idle", "stand pistol idle", ...), moving (front/back/left/right), fire, reload,
   melee, throw grenade, and the vehicle seat poses (warthog driver / passenger / gunner, ghost, banshee, scorpion) - those seat poses are
   also what the Vehicles module wants for the forced sitting pose; expose them.
2. The puppet: a hidden cyborg skeleton (the cyborg gbxmodel's nodes, no renderer) playing those animations at the avatar's pace;
   the avatar's humanoid bones posed onto it (Skyrim/BO2 GearPoser port); the held weapon (built by the Combat module - get its transform
   from HaloLocal / the Combat API; coordinate in REQUESTS.md) placed in the puppet's right hand marker / weapon node, left hand on the
   weapon's support point. Remote monitor players posed the same from their net record (Net module).
3. Camera: first person (forced like BO2, with the own head hidden on this screen only) and third person (Halo is first person on foot;
   the user asked for 3rd person "the same way" -> keep the client's third person available and posed correctly; make first/third a
   setting with first person the default in a match?). Crosshair aim = `HaloDesk.AimRay()` (camera centre ray; implement it properly:
   in third person from the camera through the screen centre onto what it hits, shots then go from the weapon toward that point).
   Desktop bob/kick light (BO2 Bo2DeskCamera) - optional.
4. Implement/extend the `HaloDesk` stub (Active, AimRay, plus CameraMode, Avatar transform, HeadHidden...).
Lab: `Editor/HaloLabDesk.cs` with a stand-in humanoid (BO2 used an RPM stand-in; Unity has no built-in humanoid - build a simple
humanoid hierarchy or load any humanoid available in the SDK templates/AvatarsScene) - pictures of the puppet holding an AR and a pistol
(idle, aim, reload), seated in a warthog pose.

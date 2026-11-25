Offline carrier probe (human-plane recordings: c_dropship, pelican). Build: dotnet build -c Release -v q
  dotnet run -c Release --no-build -- fly list [maps]          every flag/placement chain of a human plane on the campaign maps
  dotnet run -c Release --no-build -- fly eval [maps]          each flag chain's hover recordings (enter -> drop) vs the scenario's truth:
        LZ volume named for the drop (a30 lz_landing, first_fort_lz, first_pipe_lz, first_back_lz, rubble_rock_lz, rubble_middle_lz,
        rubble_boat_lz; jeep volumes first_drop / jeep_delivery judged at the pelican's cargo marker), the b30 insertion pelicans'
        designer placements (where their "out" recordings start), BSP touches (11 / 19 mass-point spheres, leaf test), end clearance.
  dotnet run -c Release --no-build -- fly scan <maps> "CFG CFG ..."   best speed scale per config (SMIN / SMAX env; SMIN=SMAX=1 = no scale)
  dotnet run -c Release --no-build -- fly bests <maps>         per-flight best speed scale     fly vert <maps>: descent needed vs crouch
  dotnet run -c Release --no-build -- trace <map> <obj> <recIndex> <every>   the recorded control (FROM=<flag> env to pick a chain)
  dotnet run -c Release --no-build -- raw <map> <vehicle> [seat]     raw vehicle / physics tag floats, a seat's marker transform
Model selection by env (Fly.Model): VEL=game is the game's own code (Data/Vehicles/HaloVehicleDef.cs HaloPlaneFlight). Others:
  VEL=flat|vec|phys, S (speed scale), YAWK (hull yaw response), KV/KVZ/KVD/KL (velocity responses), AIRF/AIRFV (air friction),
  VMODE=slide|grav|tgt, VUP/VACC, PITCHK/PITCHNEG/COSP, A (accel scale), RAMP, TURN, NOCOLL=1 (no contact stops).
Results (2026-11-04): fly eval with VEL=game: every a30 wave LZ inside, b30 insertion pelicans 3.1 / 8.8 wu from their placements,
  foehammer_first jeep 10.6 wu from first_drop (2x3, outside), hog_pelican jeep 8.1 wu from jeep_delivery (3x5, outside).
  The old fitted model (VEL=vec S=0.725, Jump / Crouch at max slide): 4 of 9 volumes inside, miss sum 142 vs 71.
Probe artifact: the leaf test counts space outside the BSP as solid; flights on b40 / c10 / a50 hangar / d20 "touch" for that reason.
Round 6 diagnostics (2026-11-04):
  dotnet run -c Release --no-build -- why <map> <obj> <volume> [every]   one chain's first recording with the game model (no collision),
        sampled, and the cargo marker's offset from the volume centre along / across the hull heading and the start->end chord
  dotnet run -c Release --no-build -- stats [maps]       per recording: LookDontTurn ticks, total / fastest facing turn, hull lag, coasting turns
  dotnet run -c Release --no-build -- ground <map> x0 y0 x1 y1 [z]   BSP floor profile between two points
  dotnet run -c Release --no-build -- fly grid <maps> "<cfg>"   2-D grid (env GX / GY = "NAME:from:to:step", default YAWK x KV) on top
        of a scan cfg; ranks by volumes inside (jeep volumes counted) and by the worst jeep miss.
  Extra model env (VEL=phys VMODE=tgt VUP=1 YAWK=0.04 = the game model exactly): KVD, KL (now read from env), WA / WD (second-order
  hull yaw: w += WA*err, w *= 1-WD), ACCS / DECS (scale the tag's accelerations / decelerations), PITCHK=0.000001 (= cos(pitch) speed).
  Findings: foehammer_first's 10.6 wu miss is along its final approach (short) and stays 7..13 wu for every yaw / velocity / decel
  variant; only a uniform speed scale 0.92..0.93 fits it, refuted by hog_pelican / insertion pelicans (best 1.00..1.01). hog_pelican's
  8.1 wu is mostly across track and needs yaw response ~0.025 + velocity response 0.04..0.045, which pushes lz_cship / back_cship out.
  Pelican and c_dropship vehicle tags are identical; physics differs only in centre of mass / moments / mass points.

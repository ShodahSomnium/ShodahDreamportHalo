namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The one-shot voice budget. A firefight asks for hundreds of particle-impact sounds a second (every spark / debris particle that
    /// hits something plays its collision sound: 8500 in 45 s on a30), which used to fill the 72 one-shot voices so weapon fire,
    /// footsteps and dialogue were dropped. Now: each sound tag may only play a few copies at once (more of the same impact adds
    /// nothing audible), and when the budget is full a new sound takes the voice of the oldest LESS important one-shot (impacts give
    /// way to weapons, weapons to dialogue). Loops never count here. // ours (Halo's own budget is not in the tags)
    /// </summary>
    public sealed partial class HaloAudioSystem
    {
        /// <summary>0 particle / object impacts, 1 footsteps / ambience / devices / effects, 2 weapons / projectiles / vehicles,
        /// 3 dialogue, game events, first person damage, music.</summary>
        internal static int Importance(int cls)
        {
            switch (cls)
            {
                case HaloSound.ClassObjectImpacts: case HaloSound.ClassParticleImpacts: case HaloSound.ClassSlowParticleImpacts: return 0;
                case HaloSound.ClassWeaponFire: case HaloSound.ClassWeaponReady: case HaloSound.ClassWeaponReload: case HaloSound.ClassWeaponEmpty:
                case HaloSound.ClassWeaponCharge: case HaloSound.ClassWeaponOverheat: case HaloSound.ClassWeaponIdle:
                case HaloSound.ClassProjectileImpact: case HaloSound.ClassProjectileDetonation:
                case HaloSound.ClassVehicleCollision: case HaloSound.ClassVehicleEngine:
                    return 2;
                case HaloSound.ClassUnitDialog: case HaloSound.ClassScriptedDialogPlayer: case HaloSound.ClassScriptedDialogOther:
                case HaloSound.ClassScriptedDialogForceUnspatialized: case HaloSound.ClassGameEvent: case HaloSound.ClassFirstPersonDamage:
                case HaloSound.ClassMusic: case HaloSound.ClassScriptedEffect:
                    return 3;
                default: return 1;
            }
        }

        /// <summary>Copies of one sound tag allowed at once, by importance. // ours</summary>
        static readonly int[] PerTag = { 2, 4, 6, 2 };

        /// <summary>Room for one more one-shot of <paramref name="b"/>: false = drop it (an impact past its per-tag limit, or nothing less
        /// important to give way). May stop an older voice of the same sound or a less important one.</summary>
        bool MakeRoom(HaloAudioBank b, HaloSound s)
        {
            int imp = Importance(s.Class);
            // per sound tag
            int same = 0; HaloVoice oldestSame = null;
            for (int i = 0; i < _voices.Count; i++)
            {
                var v = _voices[i];
                if (v.Done || v.OneShotBank != b) continue;
                same++;
                if (oldestSame == null || v.Born < oldestSame.Born) oldestSame = v;
            }
            if (same >= PerTag[imp])
            {
                if (imp == 0) return false;                       // one more spark of the same kind: inaudible anyway
                oldestSame.Stop(0.03f); oldestSame.Finish(); _oneShots--;   // a newer shot / line of the same sound takes over
            }
            if (_oneShots < MaxVoices) return true;
            // the budget is full: the oldest one-shot of the lowest importance below or equal to this one gives way
            HaloVoice victim = null;
            for (int i = 0; i < _voices.Count; i++)
            {
                var v = _voices[i];
                if (v.Done || v.Looping || v.OneShotBank == null || v.Importance > imp) continue;
                if (victim == null || v.Importance < victim.Importance || (v.Importance == victim.Importance && v.Born < victim.Born)) victim = v;
            }
            if (victim == null || (victim.Importance == imp && imp == 0)) return false;
            victim.Finish(); _oneShots--;
            return true;
        }
    }
}

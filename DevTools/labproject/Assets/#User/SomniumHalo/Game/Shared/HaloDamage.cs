using System;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>One hit, as every module passes it around (Combat makes them, Net carries them, Vehicles / players take them).</summary>
    public struct HaloHit
    {
        /// <summary>Damage in Halo vitality points (the jpt! tag's damage after its modifiers); shields take it first.</summary>
        public float Amount;
        /// <summary>The damage_effect tag (jpt!) that caused it (null = generic).</summary>
        public HaloTag Effect;
        /// <summary>Who did it: the shooter's net player id (-1 = the world / falling / self unknown).</summary>
        public int SourcePlayer;
        /// <summary>The weapon / vehicle / grenade tag behind it (for the kill feed icon and the "how").</summary>
        public HaloTag Cause;
        public Vector3 Point, Direction;
        public bool Melee, Headshot, Explosion, BackHit;
        /// <summary>Kill instantly whatever the shields (melee from behind, splatter at speed, falling into a death barrier).</summary>
        public bool Instant;
        /// <summary>Area damage (projectile blasts, effect jpt! parts, HaloCombat.AreaDamage): <see cref="ExplosionCenter"/> is the blast's
        /// centre, <see cref="InstantAcceleration"/> the jpt!'s instantaneous acceleration size (raw tag value, HaloDamageDef.AccelerationMagnitude).
        /// Informational: Combat already applied the knockback (HaloUnit.AddImpulse) - a target must not push itself again from it.</summary>
        public bool IsArea;
        public Vector3 ExplosionCenter;
        public float InstantAcceleration;
    }

    /// <summary>Anything that takes damage: remote players (their proxy), vehicles, destructible objects, the local player.</summary>
    public interface IHaloDamageable
    {
        void TakeHit(HaloHit hit);
    }

    /// <summary>A kill, for the scoreboard, the kill feed and the rules.</summary>
    public struct HaloKill
    {
        public int Killer, Victim;   // net player ids (-1 = none / the world)
        public HaloTag Cause;        // weapon / vehicle / grenade tag (null = fall, suicide, ...)
        public bool Melee, Headshot, Splatter, Grenade, Suicide, Betrayal;
    }

    /// <summary>The teams of Halo multiplayer (red 0, blue 1 as the scenario numbers them) and their HUD colours.</summary>
    public static class HaloTeams
    {
        public const int Red = 0, Blue = 1, None = -1;
        public static readonly Color RedColor = new Color(0.86f, 0.18f, 0.16f);
        public static readonly Color BlueColor = new Color(0.20f, 0.42f, 0.95f);
        public static Color Of(int team) { return team == Red ? RedColor : team == Blue ? BlueColor : new Color(0.9f, 0.9f, 0.9f); }
        public static string Name(int team) { return team == Red ? "Red Team" : team == Blue ? "Blue Team" : "No team"; }
    }

    /// <summary>Game-wide events between modules (main thread). Raise with the helpers so one failing listener cannot stop the rest.</summary>
    public static class HaloEvents
    {
        public static event Action<HaloKill> Killed;
        /// <summary>A local or remote shot left a weapon: weapon tag, muzzle position, direction, shooter id.</summary>
        public static event Action<HaloTag, Vector3, Vector3, int> Fired;
        /// <summary>The local player spawned at a position.</summary>
        public static event Action<Vector3> LocalSpawned;
        /// <summary>The local player died (the kill that did it).</summary>
        public static event Action<HaloKill> LocalDied;
        /// <summary>A HUD message (Halo's "Press E to pick up the Assault Rifle" etc.): text, seconds.</summary>
        public static event Action<string, float> Message;

        public static void RaiseKilled(HaloKill k) { Each(Killed, h => h(k)); }
        public static void RaiseFired(HaloTag w, Vector3 p, Vector3 d, int who) { Each(Fired, h => h(w, p, d, who)); }
        public static void RaiseLocalSpawned(Vector3 p) { Each(LocalSpawned, h => h(p)); }
        public static void RaiseLocalDied(HaloKill k) { Each(LocalDied, h => h(k)); }
        public static void RaiseMessage(string text, float seconds) { Each(Message, h => h(text, seconds)); }

        static void Each<T>(T handlers, Action<T> call) where T : Delegate
        {
            if (handlers == null) return;
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try { call((T)d); }
                catch (Exception e) { HaloLog.Error("Events: a listener failed: " + e); }
            }
        }
    }
}

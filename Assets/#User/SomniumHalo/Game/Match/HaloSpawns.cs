using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// Where a player (re)spawns, like Halo PC: the scenario's player starting locations allowed for the game type (their four type
    /// slots), the team's own in team games; each scored against the living players: an enemy close by or looking at it pushes it down,
    /// team mates near it pull it up (team games), anyone standing on it blocks it; the best one wins (a little random so equal points
    /// differ). Halo's exact spawn weights are code, not tag data: the distances and weights below are ours, in Halo world units.
    /// </summary>
    public static class HaloSpawns
    {
        const float BlockRadius = 0.8f;      // ours: someone stands on the spot
        const float EnemyRadius = 9f;        // ours: an enemy this close makes a start worse (Blood Gulch base to base is ~70)
        const float ViewRange = 25f;         // ours: an enemy within this range looking at a start makes it worse
        const float ViewCos = 0.75f;         // ours: ~41 degrees around the enemy's facing
        const float AllyRadius = 8f;         // ours: team mates within this make a start better (team games)
        const int MinTeamStarts = 4;         // ours: a team with fewer own starts uses every start of the game type

        static readonly List<HaloScenario.StartLocation> Candidates = new List<HaloScenario.StartLocation>();
        static readonly System.Random Rng = new System.Random();

        /// <summary>The starting locations a player of <paramref name="team"/> may use in the game type (never empty when the map has any).</summary>
        public static List<HaloScenario.StartLocation> For(HaloScenario sc, HaloRules rules, int team)
        {
            Candidates.Clear();
            if (sc == null) return Candidates;
            bool teams = rules.Teams || rules.Type == HaloScenario.GameType.Ctf;
            foreach (var s in sc.StartLocations)
                if (s.Allows(rules.Type) && (!teams || team < 0 || s.Team == team)) Candidates.Add(s);
            if (Candidates.Count < MinTeamStarts && teams)      // maps whose non-CTF starts are not split by team (Wizard, Prisoner, Boarding Action): all of them
            {
                Candidates.Clear();
                foreach (var s in sc.StartLocations) if (s.Allows(rules.Type)) Candidates.Add(s);
            }
            if (Candidates.Count == 0)
                foreach (var s in sc.StartLocations) Candidates.Add(s);
            return Candidates;
        }

        /// <summary>Picks a spawn for the local player. False (with a fallback position) when the map has no starting locations.</summary>
        public static bool Pick(HaloScenario sc, HaloRules rules, int team, out Vector3 pos, out float yawDegrees, out string note)
        {
            List<HaloScenario.StartLocation> list = For(sc, rules, team);
            bool teams = rules.Teams || rules.Type == HaloScenario.GameType.Ctf;
            HaloScenario.StartLocation best = null;
            float bestW = float.NegativeInfinity;
            int blocked = 0;
            foreach (var s in list)
            {
                Vector3 p = HaloSpace.MapOrigin + HaloSpace.Pos(s.Position);
                float w = (float)Rng.NextDouble() * 0.35f;
                bool block = false;
                for (int i = 0; i < HaloRoster.Players.Count; i++)
                {
                    HaloMatchPlayer o = HaloRoster.Players[i];
                    if (o.Local || !o.Alive) continue;
                    Vector3 to = p - o.Position;
                    float d = to.magnitude / HaloSpace.Unit;
                    if (d < BlockRadius) { block = true; break; }
                    bool ally = teams && o.Team == team && team >= 0;
                    if (ally)
                    {
                        if (d < AllyRadius) w += (AllyRadius - d) / AllyRadius * 0.5f;
                        continue;
                    }
                    if (d < EnemyRadius) w -= (EnemyRadius - d) / EnemyRadius * 2f;
                    if (d < ViewRange && d > 0.01f)
                    {
                        Vector3 f = o.Forward; f.y = 0f;
                        Vector3 dir = to; dir.y = 0f;
                        if (f.sqrMagnitude > 1e-4f && dir.sqrMagnitude > 1e-4f && Vector3.Dot(f.normalized, dir.normalized) > ViewCos
                            && !Physics.Linecast(o.Position + Vector3.up * 1.6f, p + Vector3.up * 1.2f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore))
                            w -= 1f;
                    }
                }
                if (block) { blocked++; w -= 100f; }
                if (w > bestW) { bestW = w; best = s; }
            }

            if (best == null)
            {
                pos = Fallback(out note);
                yawDegrees = 0f;
                return false;
            }

            pos = Ground(HaloSpace.MapOrigin + HaloSpace.Pos(best.Position));
            yawDegrees = HaloSpace.Yaw(best.Facing).eulerAngles.y;
            note = "start " + sc.StartLocations.IndexOf(best) + " of " + list.Count + " for " + rules.Type + (teams ? " team " + team : "") + " at " + best.Position
                   + " weight " + bestW.ToString("0.00") + (blocked > 0 ? " (" + blocked + " blocked)" : "");
            return true;
        }

        /// <summary>Puts a point on the floor below it when collision is there (the start locations sit at the feet, sometimes a hair above).</summary>
        public static Vector3 Ground(Vector3 p)
        {
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 0.4f, Vector3.down, out hit, 2.4f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.03f;
            return p + Vector3.up * 0.05f;
        }

        /// <summary>No starting location: the middle of the world from above, else the map origin (logged by the caller).</summary>
        static Vector3 Fallback(out string note)
        {
            if (HaloWorld.Ready && HaloWorld.Bounds.size.sqrMagnitude > 1f)
            {
                Bounds b = HaloWorld.Bounds;
                RaycastHit hit;
                if (Physics.Raycast(new Vector3(b.center.x, b.max.y + 5f, b.center.z), Vector3.down, out hit, b.size.y + 10f, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore))
                {
                    note = "no starting location: the floor in the middle of the world";
                    return hit.point + Vector3.up * 0.05f;
                }
            }
            note = "no starting location and no floor: the map origin";
            return HaloSpace.MapOrigin + Vector3.up * 2f;
        }
    }
}

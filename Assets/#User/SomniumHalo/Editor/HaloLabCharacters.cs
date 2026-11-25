using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo.Editor
{
    /// <summary>
    /// Lab "characters" (-halomap a10 | a30 ...): every biped of a campaign map (each actor variant with its biped, and the bipeds no variant
    /// uses) spawned in a line-up on a floor with HaloBipeds.Spawn, stepped by the real HaloUnit (60 fps): pictures idle, walking (at each one's
    /// own move-front pace), aiming up-left, firing (their own weapons through Combat's projectiles), dying and dead; close-ups of a few.
    /// "LAB: characters ..." lines: per unit the animation class, base / action / death animations, weapon, hit boxes, vitality.
    /// Lab "characters-damage": a grunt and an elite shot by the player's pistol bullets (Combat's projectiles, shooter = the player): the
    /// hit region, body / shield going down, the elite's shield recharging, the grunt's death animation, the dropped weapon. Pictures dmg_*.png.
    /// </summary>
    [InitializeOnLoadAttribute]
    static class HaloLabCharacters
    {
        static HaloLabCharacters()
        {
            HaloLabRunner.Register("characters", r => Lineup(r, null));
            HaloLabRunner.Register("characters-a30", r => Lineup(r, "a30"));
            HaloLabRunner.Register("characters-c10", r => Lineup(r, "c10"));
            HaloLabRunner.Register("characters-a50", r => Lineup(r, "a50"));
            HaloLabRunner.Register("characters-damage", r => Damage(r));
            HaloLabRunner.Register("characters-dropship", r => DropshipExit(r));
        }

        private static HaloMap _map;

        private static IEnumerator Stage(HaloLabRunner r, string map = null)
        {
            string mapName = map ?? HaloLabBoot.Arg("-halomap", "a10");
            var entry = r.Game.Install.Map(mapName);
            if (!r.Check(entry != null, "map " + mapName + " in the install")) yield break;
            _map = new HaloMap(entry.File);
            Time.captureFramerate = 60;
            HaloLocal.Alive = true;
            HaloCombat.LabMap = _map;
            try { HaloFx.Preload(_map); } catch (System.Exception e) { HaloLabRunner.Log("fx preload: " + e.Message); }
            try { HaloFootsteps.Load(_map); } catch (System.Exception) { }
            var stage = new GameObject("characters lab");
            HaloBipeds.LabParent = stage.transform;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "floor"; floor.transform.SetParent(stage.transform, false);
            floor.transform.localScale = new Vector3(30f, 1f, 30f);
            floor.layer = HaloLayers.World;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.45f, 0.45f, 0.45f)); mat.SetFloat("_Smoothness", 0.1f);
            floor.GetComponent<Renderer>().sharedMaterial = mat;
            var grid = new Material(mat); grid.SetColor("_BaseColor", new Color(0.3f, 0.32f, 0.35f));
            for (int k = -10; k <= 10; k++)
            {
                var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.Destroy(line.GetComponent<Collider>());
                line.transform.SetParent(stage.transform, false);
                line.transform.position = new Vector3(k * 5f, 0.005f, 0f); line.transform.localScale = new Vector3(0.05f, 0.01f, 200f);
                line.GetComponent<Renderer>().sharedMaterial = grid;
                var l2 = Object.Instantiate(line, stage.transform); l2.transform.position = new Vector3(0f, 0.005f, k * 5f); l2.transform.localScale = new Vector3(200f, 0.01f, 0.05f);
            }

            // a wall behind the line-up (shots stop on it)
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "wall"; wall.transform.SetParent(stage.transform, false);
            wall.transform.position = new Vector3(60f, 3f, 30f); wall.transform.localScale = new Vector3(300f, 6f, 1f);
            wall.layer = HaloLayers.World;
            wall.GetComponent<Renderer>().sharedMaterial = grid;
            var sun = new GameObject("sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.1f; sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.4f, 0.42f, 0.46f);
            r.Cam.backgroundColor = new Color(0.42f, 0.55f, 0.72f);
            Physics.SyncTransforms();
            yield return null;
        }

        /// <summary>The map's characters: every actor variant on a biped, then the bipeds no variant uses.</summary>
        private static List<HaloTag> Characters()
        {
            var list = new List<HaloTag>();
            var used = new HashSet<HaloTag>();
            foreach (HaloTag v in _map.OfClass("actv"))
            {
                HaloTag u = v.Root.Ref(20);
                if (u == null || !u.Is("bipd")) continue;
                list.Add(v);
                used.Add(u);
            }

            foreach (HaloTag b in _map.OfClass("bipd")) if (!used.Contains(b)) list.Add(b);
            return list;
        }

        private static IEnumerator Seconds(float s)
        {
            int n = Mathf.Max(1, Mathf.RoundToInt(s * 60f));
            for (int i = 0; i < n; i++) yield return null;
        }

        private const int PerRow = 7;
        private const float Spacing = 3.2f, RowGap = 40f;

        /// <summary>Rows side by side 40 m apart (nobody stands in another row's line of fire).</summary>
        private static Vector3 Spot(int i) { return new Vector3((i % PerRow - (PerRow - 1) * 0.5f) * Spacing + (i / PerRow) * RowGap, 0f, 0f); }

        private static IEnumerator Lineup(HaloLabRunner r, string map)
        {
            yield return Stage(r, map);
            if (r.Failed) yield break;
            List<HaloTag> chars = Characters();
            HaloLabRunner.Log("characters: " + _map.Name + ": " + chars.Count + " characters");
            var units = new List<HaloUnit>();
            for (int i = 0; i < chars.Count; i++)
            {
                HaloCharacterDef cd = HaloBipeds.Def(chars[i]);
                HaloUnit u = HaloBipeds.Spawn(chars[i], Spot(i) + (cd != null && cd.Flying ? Vector3.up * 1.6f : Vector3.zero), 180f, HaloTeam.None);
                units.Add(u);
                r.Check(u != null, "spawned " + chars[i].Name);
            }

            yield return Seconds(1f);
            int rows = (chars.Count + PerRow - 1) / PerRow;
            for (int i = 0; i < units.Count; i++)
            {
                HaloUnit u = units[i];
                if (u == null) continue;
                HaloLabRunner.Log("  " + u.Def.Name + ": model " + (u.Built != null ? u.Built.Nodes.Length + " nodes" : "NONE") + ", " + Anim(u) + ", weapon " + (u.Weapon != null ? u.Weapon.Name + " (" + u.WeaponLabel + ")" : "none") +
                                  " muzzle " + (u.Muzzle != null ? u.Muzzle.name : "none") + ", hit boxes " + u.Hitboxes.Count + ", body " + u.MaxBody + " shield " + u.MaxShield + ", team " + u.Team + ", natural speed " + u.NaturalSpeed.ToString("0.00") +
                                  " m/s, y " + u.transform.position.y.ToString("0.00") + ", height " + Height(u).ToString("0.00") + " m");
                r.Check(u.Built != null && u.Animator != null, u.Def.Name + " has a model and an animation graph");
            }

            Shots(r, rows, "idle");
            foreach (HaloUnit u in units) if (u != null) Closeups(r, u, "idle");

            // aiming: everyone aims up and to its left (the camera's right)
            foreach (HaloUnit u in units) if (u != null) { u.AimPoint = u.transform.position + new Vector3(6f, 4f, -6f); u.LookPoint = u.AimPoint; }
            yield return Seconds(1.2f);
            Shots(r, rows, "aim");
            foreach (HaloUnit u in units) if (u != null) HaloLabRunner.Log("  aim " + u.Def.Name + ": " + Anim(u));

            // firing at a point ahead
            foreach (HaloUnit u in units) if (u != null) { u.AimPoint = u.transform.position + new Vector3(0f, 1f, -20f); u.LookPoint = u.AimPoint; u.Firing = u.Weapon != null || u.Muzzle != null; }
            yield return Seconds(0.75f);
            Shots(r, rows, "fire");
            yield return Seconds(1f);
            foreach (HaloUnit u in units) if (u != null) { HaloLabRunner.Log("  fire " + u.Def.Name + ": shots " + u.ShotsFired + ", rounds " + u.Rounds + ", " + Anim(u)); u.Firing = false; }

            // walking toward the camera at each one's own pace
            var start = new Dictionary<HaloUnit, Vector3>();
            foreach (HaloUnit u in units) if (u != null) { u.AimPoint = Vector3.zero; u.LookPoint = Vector3.zero; start[u] = u.transform.position; u.MoveVelocity = new Vector3(0f, 0f, -u.NaturalSpeed); }
            yield return Seconds(0.6f);
            Shots(r, rows, "walk");
            foreach (HaloUnit u in units) if (u != null) HaloLabRunner.Log("  walk " + u.Def.Name + ": " + Anim(u) + ", moved " + (u.transform.position - start[u]).magnitude.ToString("0.00") + " m");
            // strafing (facing the camera, moving to its left)
            foreach (HaloUnit u in units) if (u != null) { u.LookPoint = u.transform.position + new Vector3(0f, 1.5f, -30f); u.MoveVelocity = new Vector3(u.NaturalSpeed * 0.6f, 0f, 0f); }
            yield return Seconds(0.6f);
            Shots(r, rows, "strafe");
            foreach (HaloUnit u in units) if (u != null) { HaloLabRunner.Log("  strafe " + u.Def.Name + ": " + Anim(u)); u.MoveVelocity = Vector3.zero; }
            yield return Seconds(0.5f);

            // back to the spots, then dying
            for (int i = 0; i < units.Count; i++) if (units[i] != null) { units[i].Teleport(Spot(i) + (units[i].Def.Flying ? Vector3.up * 1.6f : Vector3.zero), 180f); units[i].LookPoint = Vector3.zero; }
            yield return Seconds(0.3f);
            foreach (HaloUnit u in units) if (u != null) u.TakeHit(new HaloHit { Amount = 1e6f, SourcePlayer = 0, Point = u.Center, Direction = Vector3.forward });
            yield return Seconds(0.35f);
            Shots(r, rows, "dying");
            yield return Seconds(4f);
            Shots(r, rows, "dead");
            foreach (HaloUnit u in units) if (u != null) { HaloLabRunner.Log("  dead " + u.Def.Name + ": alive " + u.Alive + ", " + Anim(u)); if (Mortal(u)) r.Check(!u.Alive, u.Def.Name + " died"); else r.Check(u.Alive, u.Def.Name + " has no body vitality to take (body 0 or every body multiplier 0): it cannot die"); }
            HaloLabRunner.Log("characters: weapon sounds: fire " + HaloUnit.SfxFire + ", reload " + HaloUnit.SfxReload + ", empty " + HaloUnit.SfxEmpty + ", overheat " + HaloUnit.SfxOverheat + ", ready " + HaloUnit.SfxReady + ", charge " + HaloUnit.SfxCharge + "; body impacts " + HaloUnit.SfxBodyImpact + " (HaloImpacts played " + HaloImpacts.Played + ", silent " + HaloImpacts.Silent + ")");
            r.Check(HaloUnit.SfxFire > 0 && HaloUnit.SfxBodyImpact > 0, "weapon fire sounds and bodies hitting the ground were played");
            HaloLabRunner.Log("characters: pickups lying " + HaloPickups.Items.Count + ", projectiles flying " + HaloProjectiles.Count);
        }

        private static bool Mortal(HaloUnit u)
        {
            if (u.MaxBody <= 0f) return false;
            if (u.Def.Coll == null) return true;
            foreach (HaloCollisionDef.Material m in u.Def.Coll.Materials) if (m.BodyMultiplier > 0f) return true;
            return false;
        }

        private static string Anim(HaloUnit u)
        {
            HaloUnitAnimator a = u.Animator;
            if (a == null) return "no animator";
            return "class " + a.ClassName + " base " + (a.BaseName ?? "-") + (a.ActionName != null ? " action " + a.ActionName : "") + (a.DeathName != null ? " death " + a.DeathName : "");
        }

        private static float Height(HaloUnit u)
        {
            if (u.Built == null || u.Built.Mesh == null) return 0f;
            return Mathf.Max(u.Def.StandingHeight * HaloSpace.Unit, u.Built.Mesh.bounds.max.y - u.transform.position.y);
        }

        private static void Shots(HaloLabRunner r, int rows, string what)
        {
            Physics.SyncTransforms();
            for (int row = 0; row < rows; row++)
            {
                float x = row * RowGap;
                r.Aim(new Vector3(x, 3.4f, -21f), new Vector3(x, 1.1f, 0f), 50f);
                r.Shot("lineup_" + what + "_row" + row, 1280, 640);
            }
        }

        private static readonly string[] CloseKinds = { "grunt", "elite", "jackal", "marine_armored", "crewman", "captain", "hunter", "floodcombat", "flood_infection", "floodcarrier", "sentinel", "monitor" };

        private static void Closeups(HaloLabRunner r, HaloUnit u, string what)
        {
            string n = u.Def.Name;
            for (int k = 0; k < CloseKinds.Length; k++)
            {
                if (n.IndexOf("\\" + CloseKinds[k], System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!_closed.Add(CloseKinds[k] + what)) return;
                float h = Mathf.Max(0.8f, Height(u));
                Vector3 c = u.transform.position + Vector3.up * h * 0.55f;
                r.Aim(c + new Vector3(-1.2f, 0.25f, -2.6f) * Mathf.Max(0.6f, h / 1.9f), c, 45f);
                r.Shot("close_" + CloseKinds[k] + "_" + what, 640, 640);
                return;
            }
        }

        private static readonly HashSet<string> _closed = new HashSet<string>();

        // ------------------------------------------------------------------ damage

        private static IEnumerator Damage(HaloLabRunner r)
        {
            yield return Stage(r);
            if (r.Failed) yield break;
            HaloTag grunt = null, elite = null, pistol = null;
            foreach (HaloTag v in _map.OfClass("actv"))
            {
                if (grunt == null && v.Name.IndexOf("grunt minor", System.StringComparison.OrdinalIgnoreCase) >= 0) grunt = v;
                if (elite == null && v.Name.IndexOf("elite minor", System.StringComparison.OrdinalIgnoreCase) >= 0) elite = v;
            }

            foreach (HaloTag w in _map.OfClass("weap")) if (w.Name == @"weapons\pistol\pistol") pistol = w;
            if (!r.Check(grunt != null && elite != null && pistol != null, "grunt / elite variants and the pistol in " + _map.Name)) yield break;
            var cat = HaloCombat.Catalog;
            HaloWeaponDef pd = cat != null ? cat.Weapon(pistol) : null;
            if (!r.Check(pd != null && pd.PrimaryTrigger != null, "the pistol's trigger")) yield break;
            HaloTag bullet = pd.PrimaryTrigger.Projectile;
            HaloUnit g = HaloBipeds.Spawn(grunt, new Vector3(-2f, 0f, 0f), 180f, HaloTeam.None);
            HaloUnit e = HaloBipeds.Spawn(elite, new Vector3(2.5f, 0f, 0f), 180f, HaloTeam.None);
            int deaths = 0, damaged = 0;
            HaloHit lastHit = default(HaloHit);
            System.Action<HaloUnit, HaloHit> onDied = (u, h) => { deaths++; HaloLabRunner.Log("  died: " + u.Def.Name + " (killer " + h.SourcePlayer + ")"); };
            System.Action<HaloUnit, HaloHit> onDamaged = (u, h) => { damaged++; lastHit = h; };
            HaloBipeds.Died += onDied;
            HaloBipeds.Damaged += onDamaged;
            yield return Seconds(1f);
            r.Aim(new Vector3(0f, 1.6f, -6f), new Vector3(0f, 0.9f, 0f), 55f);
            r.Shot("dmg_before", 960, 540);

            // one body shot into the grunt's chest
            Vector3 gun = new Vector3(-2f, 1.0f, -6f);
            Vector3 chest = g.Center + Vector3.up * 0.15f;
            float b0 = g.Body;
            HaloCombat.FireProjectile(bullet, gun, Vector3.zero, 0, pistol, null, (chest - gun).normalized);
            yield return Seconds(0.3f);
            HaloUnit.Hitbox hb = NearestBox(g, lastHit.Point);
            HaloLabRunner.Log("  grunt hit: body " + b0.ToString("0.00") + " -> " + g.Body.ToString("0.00") + " (" + (g.Body * g.MaxBody).ToString("0") + " / " + g.MaxBody + " points), hit box " +
                              (hb != null ? hb.Box.name + " region " + hb.Region + " anim region " + hb.AnimRegion + " material " + hb.Material : "?") + ", action " + (g.Animator.ActionName ?? "-"));
            r.Check(g.Body < b0, "a pistol bullet hurts the grunt's body");
            r.Shot("dmg_grunt_hit", 960, 540);

            // the elite: the shield takes it, then recharges
            Vector3 gun2 = new Vector3(2.5f, 1.2f, -6f);
            float s0 = e.Shield;
            for (int i = 0; i < 3; i++) { HaloCombat.FireProjectile(bullet, gun2, Vector3.zero, 0, pistol, null, (e.Center + Vector3.up * 0.2f - gun2).normalized); yield return Seconds(0.12f); }
            yield return Seconds(0.2f);
            float s1 = e.Shield;
            HaloLabRunner.Log("  elite shield " + s0.ToString("0.00") + " -> " + s1.ToString("0.00") + ", body " + e.Body.ToString("0.00") + ", impact material " + e.HitMaterial(e.Center));
            r.Check(s1 < s0 && e.Body >= 0.999f, "pistol bullets take the elite's shield first, the body untouched");
            r.Shot("dmg_elite_shield", 960, 540);
            float stun = e.Def.Coll != null ? e.Def.Coll.StunTime : 2f;
            yield return Seconds(stun + 2f);
            HaloLabRunner.Log("  elite shield after " + (stun + 2f).ToString("0.0") + " s (stun " + stun + " s, full recharge " + (e.Def.Coll != null ? e.Def.Coll.FullRechargeSeconds : 0f).ToString("0.0") + " s): " + e.Shield.ToString("0.00"));
            r.Check(e.Shield > s1, "the elite's shield recharges");

            // a head shot kills the grunt (the pistol's bullets can cause headshots)
            Transform head = g.Marker("head");
            Vector3 hp = head != null ? head.position : g.Eye;
            HaloCombat.FireProjectile(bullet, gun, Vector3.zero, 0, pistol, null, (hp - gun).normalized);
            yield return Seconds(0.25f);
            if (g.Alive)
            {
                HaloLabRunner.Log("  the head shot did not kill (body " + g.Body.ToString("0.00") + ", head boxes " + CountHeads(g) + "): body shots");
                for (int i = 0; i < 12 && g.Alive; i++) { HaloCombat.FireProjectile(bullet, gun, Vector3.zero, 0, pistol, null, (g.Center - gun).normalized); yield return Seconds(0.15f); }
            }

            r.Check(!g.Alive, "the grunt dies (" + (g.Animator.DeathName ?? "no death animation") + ")");
            yield return Seconds(0.3f);
            r.Shot("dmg_grunt_dying", 960, 540);
            yield return Seconds(3f);
            r.Shot("dmg_grunt_dead", 960, 540);
            int drops = 0;
            foreach (HaloPickups.Item it in HaloPickups.Items) if (it.Dropped) drops++;
            HaloLabRunner.Log("  deaths " + deaths + ", damage events " + damaged + ", dropped weapons " + drops + ", grunt death done " + g.Animator.DeathDone);
            r.Check(deaths >= 1 && drops >= 1, "the death raised HaloBipeds.Died and the plasma pistol fell");
            HaloBipeds.Died -= onDied;
            HaloBipeds.Damaged -= onDamaged;
        }

        // ------------------------------------------------------------------ dropship exit (a30's lz_cship, V's vehicle)

        /// <summary>
        /// Lab "characters-dropship": a30's lz_cship placed hovering 6 m over a floor with its pods open (Vehicles' HaloVehicles.SpawnPlacement /
        /// Open), four grunts seated in its passenger seats (SeatUnit -> EnterSeat), then unseated (UnseatUnit -> ExitSeat): each plays its seat's
        /// exit clip ("CD-passengerL01 exit") on the seat, then falls (airborne) and lands. Pictures dropship_exit_*.png every quarter second.
        /// </summary>
        private static IEnumerator DropshipExit(HaloLabRunner r)
        {
            yield return Stage(r, "a30");
            if (r.Failed) yield break;
            var scen = new HaloScenario(_map);
            HaloVehicles.LabMode = true; HaloVehicles.CampaignLab = true;
            HaloScenario.Placement cp = null;
            foreach (var p in scen.Vehicles)
                if (p.Type != null && p.NameIndex >= 0 && p.NameIndex < scen.ObjectNames.Count && scen.ObjectNames[p.NameIndex] == "lz_cship") cp = p;
            if (!r.Check(cp != null, "the scenario's lz_cship placement")) yield break;
            var ship = HaloVehicles.SpawnPlacement(cp, "lz_cship", HaloBipeds.LabParent);
            if (!r.Check(ship != null, "lz_cship placed")) yield break;
            ship.transform.SetPositionAndRotation(new Vector3(0f, 7f, 0f), Quaternion.Euler(0f, 90f, 0f));
            if (ship.Body != null) { ship.Body.isKinematic = true; ship.Body.useGravity = false; }
            ship.Open(true);
            HaloTag grunt = null;
            foreach (HaloTag v in _map.OfClass("actv")) if (v.Name.IndexOf("grunt minor", System.StringComparison.OrdinalIgnoreCase) >= 0) { grunt = v; break; }
            var troops = new List<HaloUnit>();
            for (int s = 0; s < ship.Seats.Count && troops.Count < 4; s++)
            {
                if (ship.Seats[s].Label.IndexOf("passenger", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                HaloUnit u = HaloBipeds.Spawn(grunt, ship.transform.position, 0f, HaloTeam.Covenant);
                if (u != null && ship.SeatUnit(s, u)) { troops.Add(u); HaloLabRunner.Log("  seated in " + ship.Seats[s].Label); }
            }

            r.Check(troops.Count == 4, "four grunts in the passenger seats (" + troops.Count + ")");
            yield return Seconds(1.5f);
            HaloUnit first = troops.Count > 0 ? troops[0] : null;
            if (first == null) yield break;
            Vector3 seatPos = first.transform.position;
            Vector3 side = Vector3.ProjectOnPlane(seatPos - ship.transform.position, Vector3.up).normalized;
            if (side.sqrMagnitude < 0.5f) side = ship.transform.right;
            Vector3 eye = seatPos + side * 9f + Vector3.up * -1.5f + Vector3.Cross(Vector3.up, side) * 5f, look = seatPos + side * 2f - Vector3.up * 3f;
            r.Aim(eye, look, 60f);
            r.Shot("dropship_exit_0_seated", 960, 540);
            foreach (HaloUnit u in troops) ship.UnseatUnit(u);
            HaloLabRunner.Log("  unseated: first exiting " + first.Exiting + " custom " + (first.Animator.CustomName ?? "-"));
            r.Check(first.Exiting && first.Animator.CustomName != null && first.Animator.CustomName.Contains("exit"), "the grunt plays its seat's exit clip on the seat");
            for (int k = 1; k <= 10; k++)
            {
                yield return Seconds(0.25f);
                r.Aim(eye, look, 60f);
                r.Shot("dropship_exit_" + k, 960, 540);
                HaloLabRunner.Log("  t " + (k * 0.25f).ToString("0.00") + " s: at " + first.transform.position.ToString("F2") + " seat " + (first.Seat != null) + " grounded " + first.Grounded + " " + Anim(first) + (first.Animator.CustomName != null ? " custom " + first.Animator.CustomName : ""));
            }

            yield return Seconds(1.5f);
            int down = 0;
            foreach (HaloUnit u in troops) if (u != null && u.Seat == null && u.Grounded && u.transform.position.y < 1f) down++;
            r.Check(down == 4, "the four grunts are out and standing on the floor (" + down + ")");
            first.MoveVelocity = new Vector3(0f, 0f, first.NaturalSpeed);
            yield return Seconds(0.5f);
            HaloLabRunner.Log("  then moves: " + Anim(first) + " at " + first.transform.position.ToString("F2"));
            r.Check(first.Animator.BaseName != null && first.Animator.BaseName.Contains("move"), "it moves normally after landing");
            r.Aim(eye, look, 60f);
            r.Shot("dropship_exit_after", 960, 540);
        }

        private static int CountHeads(HaloUnit u) { int n = 0; foreach (HaloUnit.Hitbox h in u.Hitboxes) if (h.Head) n++; return n; }

        private static HaloUnit.Hitbox NearestBox(HaloUnit u, Vector3 p)
        {
            HaloUnit.Hitbox best = null;
            float bd = float.MaxValue;
            foreach (HaloUnit.Hitbox h in u.Hitboxes)
            {
                if (h.Box == null) continue;
                float d = (h.Box.ClosestPoint(p) - p).sqrMagnitude;
                if (d < bd) { bd = d; best = h; }
            }

            return best;
        }
    }
}

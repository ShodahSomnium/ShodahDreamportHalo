using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] object_*, objects_*, effect_new*: HaloObjects (K), transforms, markers, HaloFx (X).</summary>
    public static partial class HaloScriptFns
    {
        static void BindObjects(HsVm vm)
        {
            vm.Bind("object_create", ObjectCreate);
            vm.Bind("object_create_anew", ObjectCreateAnew);
            vm.Bind("object_destroy", ObjectDestroy);
            vm.Bind("object_create_containing", ObjectCreateContaining);
            vm.Bind("object_create_anew_containing", ObjectCreateAnewContaining);
            vm.Bind("object_destroy_containing", ObjectDestroyContaining);
            vm.Bind("object_destroy_all", ObjectDestroyAll);
            vm.Bind("object_teleport", ObjectTeleport);
            vm.Bind("object_set_facing", ObjectSetFacing);
            vm.Bind("objects_attach", ObjectsAttach);
            vm.Bind("objects_detach", ObjectsDetach);
            vm.Bind("object_set_scale", ObjectSetScale);
            vm.Bind("objects_can_see_object", ObjectsCanSeeObject);
            vm.Bind("objects_can_see_flag", ObjectsCanSeeFlag);
            vm.Bind("object_set_collideable", ObjectSetCollideable);
            vm.Bind("effect_new", EffectNew);
            vm.Bind("effect_new_on_object_marker", EffectNewOnMarker);
        }

        static string NameArg(HsArgs a, int i) => HaloScriptWorld.NameOf(a.Obj(i));

        static int ObjectCreate(HsArgs a) { string n = NameArg(a, 0); if (n.Length > 0) HaloObjects.Create(n); return 0; }
        static int ObjectCreateAnew(HsArgs a) { string n = NameArg(a, 0); if (n.Length > 0) HaloObjects.CreateAnew(n); return 0; }

        static int ObjectDestroy(HsArgs a)
        {
            int h = a.Obj(0);
            string n = HaloScriptWorld.NameOf(h);
            if (n.Length > 0) { HaloObjects.Destroy(n); return 0; }
            if (HaloScriptWorld.IsPlayer(h)) return 0;
            var g = HaloScriptWorld.Get(h);
            if (g != null) Object.Destroy(g);
            return 0;
        }

        static int ObjectCreateContaining(HsArgs a) { HaloObjects.CreateContaining(a.Str(0), false); return 0; }
        static int ObjectCreateAnewContaining(HsArgs a) { HaloObjects.CreateContaining(a.Str(0), true); return 0; }
        static int ObjectDestroyContaining(HsArgs a) { HaloObjects.DestroyContaining(a.Str(0)); return 0; }
        static int ObjectDestroyAll(HsArgs a) { HaloObjects.DestroyAll(); return 0; }

        /// <summary>object_teleport: the object (or the player: a teleport, VR-safe) to a cutscene flag's position and facing.</summary>
        static int ObjectTeleport(HsArgs a)
        {
            int h = a.Obj(0);
            Vector3 p; float yaw;
            if (!HaloScriptWorld.FlagPose(a.Int(1), out p, out yaw)) return 0;
            Place(h, p, yaw, true);
            return 0;
        }

        /// <summary>object_set_facing: turns the object to face the way the cutscene flag faces.</summary>
        static int ObjectSetFacing(HsArgs a)
        {
            int h = a.Obj(0);
            Vector3 p; float yaw;
            if (!HaloScriptWorld.FlagPose(a.Int(1), out p, out yaw)) return 0;
            Place(h, p, yaw, false);
            return 0;
        }

        static void Place(int h, Vector3 p, float yaw, bool move)
        {
            if (HaloScriptWorld.IsPlayer(h))
            {
                if (h != HaloScriptWorld.PlayerBase) return;
                Vector3 feet;
                if (!move && !HaloPlayer.TryFeet(out feet)) return;
                if (!move) HaloPlayer.TryFeet(out p);
                // Q: while a cutscene owns the view the VR player stays where they stand; the last placement applies after it
                if (HaloCinematic.DeferPlayerTeleport(p, yaw, !move)) return;
                string how;
                HaloPlayer.Teleport(p, yaw, out how);
                Once(move ? "tp" : "face", "Script: " + (move ? "object_teleport" : "object_set_facing") + " moved the player (" + how + ")");
                return;
            }
            var g = HaloScriptWorld.Get(h);
            if (g == null) return;
            var unit = g.GetComponent<HaloUnit>();
            if (unit != null && unit.Seat == null)
            {
                if (move) unit.Teleport(p, yaw);
                else unit.Teleport(g.transform.position, yaw);
                Vector3 fw = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                unit.LookPoint = unit.Eye + fw * 5f; unit.AimPoint = unit.LookPoint; unit.MoveVelocity = Vector3.zero;
                return;
            }
            var t = g.transform;
            var rb = g.GetComponent<Rigidbody>();
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            if (move) t.SetPositionAndRotation(p, rot); else t.rotation = rot;
            if (rb != null && !rb.isKinematic) { rb.position = t.position; rb.rotation = t.rotation; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            var u = g.GetComponent<HaloUnit>();
            if (u != null) { Vector3 f = rot * Vector3.forward; u.LookPoint = u.Eye + f * 5f; u.AimPoint = u.LookPoint; u.MoveVelocity = Vector3.zero; }
        }

        // ------------------------------------------------------------------ attach / detach

        sealed class Attached { public Transform OldParent; public bool HadBody, WasKinematic; }
        static readonly Dictionary<GameObject, Attached> AttachedTo = new Dictionary<GameObject, Attached>();

        static Transform MarkerOf(GameObject g, string marker)
        {
            if (g == null) return null;
            if (string.IsNullOrEmpty(marker)) return g.transform;
            var m = HaloFx.Marker(g.transform, marker);
            return m != null ? m : g.transform;
        }

        /// <summary>objects_attach parent "parent marker" child "child marker": the child rides on the parent's marker.</summary>
        static int ObjectsAttach(HsArgs a)
        {
            int ph = a.Obj(0), ch = a.Obj(2);
            if (HaloScriptWorld.IsPlayer(ch)) { Once("attach player", "Script: objects_attach of a player ignored (VR: the player is never carried by a script)"); return 0; }
            var parent = HaloScriptWorld.Get(ph); var child = HaloScriptWorld.Get(ch);
            if (parent == null || child == null) return 0;
            Transform pm = MarkerOf(parent, a.Str(1));
            Transform cm = MarkerOf(child, a.Str(3));
            Attached at;
            if (!AttachedTo.TryGetValue(child, out at))
            {
                at = new Attached { OldParent = child.transform.parent };
                var rb = child.GetComponent<Rigidbody>();
                if (rb != null) { at.HadBody = true; at.WasKinematic = rb.isKinematic; rb.isKinematic = true; }
                AttachedTo[child] = at;
            }
            var ct = child.transform;
            ct.SetParent(pm, false);
            ct.localPosition = Vector3.zero; ct.localRotation = Quaternion.identity;
            if (cm != null && cm != ct)
            {
                // put the child's own marker on the parent's marker
                Quaternion dr = pm.rotation * Quaternion.Inverse(cm.rotation);
                ct.rotation = dr * ct.rotation;
                ct.position += pm.position - cm.position;
            }
            return 0;
        }

        static int ObjectsDetach(HsArgs a)
        {
            var child = HaloScriptWorld.Get(a.Obj(1));
            if (child == null) return 0;
            Attached at;
            if (!AttachedTo.TryGetValue(child, out at)) return 0;
            AttachedTo.Remove(child);
            child.transform.SetParent(at.OldParent, true);
            if (at.HadBody) { var rb = child.GetComponent<Rigidbody>(); if (rb != null) rb.isKinematic = at.WasKinematic; }
            return 0;
        }

        // ------------------------------------------------------------------ scale over ticks

        struct Tween { public GameObject Go; public Vector3 From, To; public int Ticks, Done; }
        static readonly List<Tween> Tweens = new List<Tween>();
        static readonly Dictionary<GameObject, Vector3> BaseScale = new Dictionary<GameObject, Vector3>();

        static int ObjectSetScale(HsArgs a)
        {
            var g = HaloScriptWorld.Get(a.Obj(0));
            if (g == null || HaloScriptWorld.IsPlayer(a.Obj(0))) return 0;
            Vector3 b;
            if (!BaseScale.TryGetValue(g, out b)) { b = g.transform.localScale; BaseScale[g] = b; }
            Vector3 to = b * a.Real(1);
            int ticks = a.Int(2);
            for (int i = Tweens.Count - 1; i >= 0; i--) if (Tweens[i].Go == g) Tweens.RemoveAt(i);
            if (ticks <= 0) { g.transform.localScale = to; return 0; }
            Tweens.Add(new Tween { Go = g, From = g.transform.localScale, To = to, Ticks = ticks });
            return 0;
        }

        static void StepTweens()
        {
            for (int i = Tweens.Count - 1; i >= 0; i--)
            {
                var t = Tweens[i];
                if (t.Go == null) { Tweens.RemoveAt(i); continue; }
                t.Done++;
                float f = Mathf.Clamp01((float)t.Done / t.Ticks);
                t.Go.transform.localScale = Vector3.LerpUnclamped(t.From, t.To, f);
                if (t.Done >= t.Ticks) Tweens.RemoveAt(i); else Tweens[i] = t;
            }
        }

        // ------------------------------------------------------------------ sight

        static bool CanSee(int viewer, Vector3 target, float degrees, GameObject targetGo)
        {
            Vector3 eye, fwd;
            if (!HaloScriptWorld.TryEye(viewer, out eye, out fwd)) return false;
            Vector3 d = target - eye;
            float dist = d.magnitude;
            if (dist < 0.01f) return true;
            if (Vector3.Angle(fwd, d) > degrees) return false;
            RaycastHit hit;
            if (Physics.Linecast(eye, target, out hit, HaloLayers.GroundMask, QueryTriggerInteraction.Ignore))
                return targetGo != null && hit.collider != null && hit.collider.transform.IsChildOf(targetGo.transform);
            return true;
        }

        /// <summary>objects_can_see_object list target degrees: any viewer has the target within the cone and a clear line.</summary>
        static int ObjectsCanSeeObject(HsArgs a)
        {
            int l = ListOf(a, 0), n = Count(l);
            Vector3 c;
            if (!HaloScriptWorld.TryCenter(a.Obj(1), out c)) return 0;
            var tg = HaloScriptWorld.Get(a.Obj(1));
            float deg = a.Real(2);
            for (int i = 0; i < n; i++) if (CanSee(At(l, i), c, deg, tg)) return 1;
            return 0;
        }

        static int ObjectsCanSeeFlag(HsArgs a)
        {
            int l = ListOf(a, 0), n = Count(l);
            Vector3 p; float yaw;
            if (!HaloScriptWorld.FlagPose(a.Int(1), out p, out yaw)) return 0;
            float deg = a.Real(2);
            for (int i = 0; i < n; i++) if (CanSee(At(l, i), p, deg, null)) return 1;
            return 0;
        }

        static readonly List<Collider> _cols = new List<Collider>();
        static int ObjectSetCollideable(HsArgs a)
        {
            var g = HaloScriptWorld.Get(a.Obj(0));
            if (g == null || HaloScriptWorld.IsPlayer(a.Obj(0))) return 0;
            bool on = a.Bool(1);
            g.GetComponentsInChildren(true, _cols);
            for (int i = 0; i < _cols.Count; i++) _cols[i].enabled = on;
            _cols.Clear();
            return 0;
        }

        // ------------------------------------------------------------------ effects

        static int EffectNew(HsArgs a)
        {
            var tag = a.Tag(0);
            int f = a.Int(1);
            var d = HaloScript.Data;
            if (tag == null || d == null || f < 0 || f >= d.FlagPositions.Length) return 0;
            Vector3 p = HaloSpace.MapOrigin + HaloSpace.Pos(d.FlagPositions[f]);
            var rot = HaloSpace.Euler(d.FlagYaw[f], d.FlagPitch[f], 0f);
            HaloFx.Play(tag, p, rot);
            HaloNetGame.NoteEffectAt(tag, p, rot.eulerAngles.y);   // N: co-op followers see it
            return 0;
        }

        static int EffectNewOnMarker(HsArgs a)
        {
            var tag = a.Tag(0);
            var g = HaloScriptWorld.Get(a.Obj(1));
            if (tag == null || g == null) return 0;
            var m = MarkerOf(g, a.Str(2));
            HaloFx.Play(tag, m.position, m.rotation, m);
            HaloNetGame.NoteEffectOn(tag, g, a.Str(2));   // N
            return 0;
        }
    }
}

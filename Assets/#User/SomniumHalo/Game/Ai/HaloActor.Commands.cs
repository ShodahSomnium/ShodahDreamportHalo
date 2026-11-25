using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [AI module, I] A scenario command list running on one actor (ai_command_list): its atoms in order - pause, go to (and face), look
    /// (at a point / randomly / at the player / at an object), animation mode, crouch, shoot, grenade, jump, script, animate, action,
    /// vocalize, targeting / initiative switches, wait (for ai_command_list_advance), loop, die, move immediate / teleport, set radius.
    /// Status as ai_command_list_status reports it: 0 none, 1 finished, 2 waiting for a stimulus, 3 running.
    /// </summary>
    public sealed class HaloAiCommandRunner
    {
        public HaloAiScenario.CommandList List;
        public int Index;
        public int Status;      // 0 none, 1 finished, 2 waiting, 3 running
        public bool Active => List != null && (Status == 2 || Status == 3);
        /// <summary>The current atom holds the actor in place this tick (pause, look, wait, animate): the actor's steering must not add
        /// separation drift (HaloActor.Steer reads it).</summary>
        public bool Holding => Active && _holdTick == HaloAi.TickCount;
        int _holdTick = -1;
        float _until = -1f; bool _started; bool _advance;
        Vector3 _look; bool _lookSet; float _radius = 0.35f; bool _crouch;
        int _loops;

        public void Begin(HaloAiScenario.CommandList list)
        {
            List = list; Index = 0; Status = list != null ? 3 : 0; _started = false; _advance = false; _until = -1f; _lookSet = false; _radius = 0.35f; _crouch = false; _loops = 0;
        }

        public void Stop() { if (Status == 2 || Status == 3) Status = 1; }

        /// <summary>ai_command_list_advance: a waiting list may go on.</summary>
        public void Advance() { _advance = true; }

        Vector3 Point(int i, HaloActor a)
        {
            if (List == null || i < 0 || i >= List.Points.Count) return a.Position;
            return HaloSpace.Pos(List.Points[i]);
        }

        void Next() { Index++; _started = false; _until = -1f; }

        public void Step(HaloActor a, float dt)
        {
            var u = a.Unit;
            if (List == null || u == null) { Status = 0; return; }
            int guard = 0;
            while (Status == 3 || Status == 2)
            {
                if (++guard > 8) break;          // several instant atoms a tick, never a hang
                if (Index >= List.Commands.Count) { Status = 1; if (HaloAi.Verbose) HaloLog.Info("AI: " + a.Label + " finished command list " + List.Name); return; }
                var c = List.Commands[Index];
                bool first = !_started;
                _started = true;
                switch (c.Atom)
                {
                    case HaloAtom.Pause:
                        if (first) _until = a.Now + Mathf.Max(0f, c.Param1);
                        Hold(a);
                        if (a.Now >= _until) { Next(); continue; }
                        return;
                    case HaloAtom.GoTo:
                    case HaloAtom.GoToAndFace:
                    case HaloAtom.MoveImmediate:
                        {
                            Vector3 p = Point(c.Point1, a);
                            a.MoveTo(p, c.Modifier == 1 ? 0.5f : 1f, Mathf.Max(0.3f, _radius));
                            if (c.Atom == HaloAtom.GoToAndFace && c.Point2 >= 0) { _look = Point(c.Point2, a) + Vector3.up * 1.5f; _lookSet = true; }
                            LookOrMove(a);
                            if (a.Arrived) { Next(); continue; }
                            return;
                        }
                    case HaloAtom.MoveInDirection:
                        {
                            if (first)
                            {
                                Vector3 dir = Quaternion.Euler(0f, a.Unit.Yaw + c.Param2 * Mathf.Rad2Deg, 0f) * Vector3.forward;
                                a.MoveTo(a.Position + dir * Mathf.Max(0.5f, c.Param1 * HaloSpace.Unit), 1f);
                            }
                            LookOrMove(a);
                            if (a.Arrived) { Next(); continue; }
                            return;
                        }
                    case HaloAtom.Look:
                        if (first) { _look = Point(c.Point1, a) + Vector3.up * 1.5f; _lookSet = true; _until = a.Now + Mathf.Max(0f, c.Param1); }
                        Hold(a);
                        if (a.Now >= _until) { Next(); continue; }
                        return;
                    case HaloAtom.LookRandom:
                        if (first)
                        {
                            float yaw = a.Unit.Yaw + HaloAi.Rand(-60f, 60f);
                            _look = a.Unit.Eye + Quaternion.Euler(HaloAi.Rand(-10f, 10f), yaw, 0f) * Vector3.forward * 10f; _lookSet = true;
                            _until = a.Now + Mathf.Max(0.2f, HaloAi.Rand(Mathf.Min(c.Param1, c.Param2), Mathf.Max(c.Param1, c.Param2)));
                        }
                        Hold(a);
                        if (a.Now >= _until) { Next(); continue; }
                        return;
                    case HaloAtom.LookPlayer:
                        {
                            var p = HaloAiBodies.NearestPlayer(a.Position);
                            if (p != null) { _look = p.Head; _lookSet = true; }
                            if (first) _until = a.Now + Mathf.Max(0f, c.Param1);
                            Hold(a);
                            if (a.Now >= _until) { Next(); continue; }
                            return;
                        }
                    case HaloAtom.LookObject:
                        {
                            string n = HaloAi.ObjectName(c.ObjectName);
                            var go = n != null ? HaloObjects.Find(n) : null;
                            if (go != null) { _look = go.transform.position + Vector3.up * 1.5f; _lookSet = true; }
                            if (first) _until = a.Now + Mathf.Max(0f, c.Param1);
                            Hold(a);
                            if (a.Now >= _until) { Next(); continue; }
                            return;
                        }
                    case HaloAtom.AnimationMode:
                        // 0 none (the default seats); other modes name a seat in the graph (ours: alert / combat)
                        u.Stance = c.Modifier == 0 ? null : (c.Modifier == 1 ? "alert" : null);
                        Next(); continue;
                    case HaloAtom.Crouch:
                        _crouch = c.Modifier != 0 || c.Param1 > 0f;
                        u.Crouching = _crouch;
                        Next(); continue;
                    case HaloAtom.Shoot:
                        {
                            if (first) _until = a.Now + Mathf.Max(0.1f, c.Param1);
                            Vector3 p = Point(c.Point1, a);
                            a.Stop();
                            u.LookPoint = p; u.AimPoint = p; u.Firing = true;
                            if (a.Now >= _until) { u.Firing = false; Next(); continue; }
                            return;
                        }
                    case HaloAtom.Grenade:
                        {
                            Vector3 p = Point(c.Point1, a);
                            u.LookPoint = p; u.AimPoint = p; u.ThrowGrenade = true;
                            Next(); return;
                        }
                    case HaloAtom.RunningJump:
                    case HaloAtom.TargetedJump:
                        if (c.Point1 >= 0) a.MoveTo(Point(c.Point1, a), 1f);
                        u.Jump = true;
                        Next(); return;
                    case HaloAtom.Script:
                        HaloAi.RunScript(c.Script);
                        Next(); continue;
                    case HaloAtom.Animate:
                        if (first) { HaloAi.Animate(a, c.Animation); _until = a.Now + Mathf.Max(0.5f, c.Param1 > 0f ? c.Param1 : 2f); }
                        Hold(a);
                        if (a.Now >= _until) { Next(); continue; }
                        return;
                    case HaloAtom.Recording:
                        HaloAi.Unimplemented("command atom recording");
                        Next(); continue;
                    case HaloAtom.Action:
                        // ours: modifier 0 none, 1 sleep, 2 alert, 3 fight (enter combat on sight), 4 flee
                        if (c.Modifier == 1) a.InitialState = HaloAiState.Sleeping;
                        else if (c.Modifier == 2) a.InitialState = HaloAiState.Alert;
                        else if (c.Modifier == 4) a.Panic(null, 1f);
                        Next(); continue;
                    case HaloAtom.Vocalize:
                        HaloAi.Unimplemented("command atom vocalize");
                        Next(); continue;
                    case HaloAtom.Targeting:
                    case HaloAtom.Initiative:
                        Next(); continue;      // the list flags decide it (allow targeting / initiative)
                    case HaloAtom.Wait:
                        if (first) { _advance = false; _until = c.Param1 > 0f ? a.Now + c.Param1 : -1f; }
                        Status = 2;
                        Hold(a);
                        if (_advance || (c.Modifier == 0 && _until > 0f && a.Now >= _until)) { _advance = false; Status = 3; Next(); continue; }
                        return;
                    case HaloAtom.Loop:
                        if (++_loops > 1000000) _loops = 0;
                        Index = c.CommandIndex >= 0 && c.CommandIndex < List.Commands.Count ? c.CommandIndex : 0;
                        _started = false;
                        if (guard > 2) return;   // a loop of instant atoms: go on next tick
                        continue;
                    case HaloAtom.Die:
                        u.Kill();
                        Status = 1; return;
                    case HaloAtom.SetRadius:
                        _radius = Mathf.Max(0.1f, c.Param1 * HaloSpace.Unit);
                        Next(); continue;
                    case HaloAtom.Teleport:
                        u.Teleport(Point(c.Point1, a), u.Yaw);
                        u.Settle();             // on walkable ground (depenetrated), not just the first hit below
                        a.Stop();
                        Next(); continue;
                    case HaloAtom.Vehicle:
                        HaloAi.Unimplemented("command atom vehicle");
                        Next(); continue;
                    default:
                        HaloAi.Unimplemented("command atom " + c.Atom);
                        Next(); continue;
                }
            }
        }

        void Hold(HaloActor a)
        {
            _holdTick = HaloAi.TickCount;
            a.Stop();
            if (_lookSet) { a.Unit.LookPoint = _look; a.Unit.AimPoint = Vector3.zero; }
            a.Unit.Crouching = _crouch;
        }

        void LookOrMove(HaloActor a)
        {
            if (_lookSet && a.Arrived) a.Unit.LookPoint = _look;
            else a.Unit.LookPoint = a.Unit.Eye + (a.Unit.MoveVelocity.sqrMagnitude > 0.01f ? a.Unit.MoveVelocity.normalized : a.Unit.transform.forward) * 10f;
            a.Unit.AimPoint = Vector3.zero;
        }
    }

    public sealed partial class HaloActor
    {
        public HaloAiCommandRunner Commands;

        public void RunCommandList(HaloAiScenario.CommandList list)
        {
            if (list == null) return;
            if (Commands == null) Commands = new HaloAiCommandRunner();
            Commands.Begin(list);
            if (HaloAi.Verbose) HaloLog.Info("AI: " + Label + " runs command list " + list.Name);
        }

        public int CommandStatus => Commands != null ? Commands.Status : 0;
    }
}

using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>[Script module, S] player_action_test_* (a10's training): what the local player did since player_action_test_reset, from
    /// the VR controllers (HaloXr), the keyboard / mouse of a monitor player, Combat's shot / throw events and the head's look direction.
    /// player0_look_invert_pitch is remembered (the VR head is never inverted); player_camera_control is a VR no-op.</summary>
    public static partial class HaloScriptFns
    {
        struct ActionLatch { public bool Accept, Back, Action, Primary, Grenade, Jump, Up, Down, Left, Right; public Vector3 Forward; public bool HaveForward; }
        static ActionLatch _act;
        static bool _invertPitch, _keysBroken, _hooked;

        static void BindInput(HsVm vm)
        {
            vm.Bind("player_action_test_reset", ActionReset);
            vm.Bind("player_action_test_accept", a => B(_act.Accept));
            vm.Bind("player_action_test_back", a => B(_act.Back));
            vm.Bind("player_action_test_action", a => B(_act.Action));
            vm.Bind("player_action_test_primary_trigger", a => B(_act.Primary));
            vm.Bind("player_action_test_grenade_trigger", a => B(_act.Grenade));
            vm.Bind("player_action_test_jump", a => B(_act.Jump));
            vm.Bind("player_action_test_look_relative_up", a => B(_act.Up));
            vm.Bind("player_action_test_look_relative_down", a => B(_act.Down));
            vm.Bind("player_action_test_look_relative_left", a => B(_act.Left));
            vm.Bind("player_action_test_look_relative_right", a => B(_act.Right));
            vm.Bind("player_action_test_look_relative_all_directions", a => B(_act.Up && _act.Down && _act.Left && _act.Right));
            vm.Bind("player0_look_invert_pitch", a => { _invertPitch = a.Bool(0); return 0; });
            vm.Bind("player0_look_pitch_is_inverted", a => B(_invertPitch));
            vm.Bind("player0_joystick_set_is_normal", a => 0);
            vm.Bind("player_camera_control", PlayerCameraControl);
            if (!_hooked) { _hooked = true; HaloEvents.Fired += OnFired; HaloCombat.Thrown += OnThrown; }
        }

        static int PlayerCameraControl(HsArgs a)
        {
            Once("pcc", "Script: player_camera_control ignored (VR: the player's own head always has the view)");
            return 0;
        }

        static void OnFired(HaloTag w, Vector3 p, Vector3 d, int who) { if (who == HaloLocal.NetId || who < 0) _act.Primary = true; }
        static void OnThrown(HaloTag t, Vector3 p, Vector3 v) { _act.Grenade = true; }

        static int ActionReset(HsArgs a)
        {
            _act = default(ActionLatch);
            var cam = HaloPlayer.Head();
            if (cam != null) { _act.Forward = cam.transform.forward; _act.HaveForward = true; }
            return 0;
        }

        /// <summary>Every script tick: latch what the player does (held states, so a press between two ticks is not lost).</summary>
        static void StepInput()
        {
            if (HaloXr.Present)
            {
                var r = HaloXr.Right; var l = HaloXr.Left;
                if (r.Primary || r.PrimaryPressed) _act.Accept = true;
                if (r.Secondary || r.SecondaryPressed) _act.Back = true;
                if (r.GripHeld || l.GripHeld) _act.Action = true;
                if (r.TriggerHeld) _act.Primary = true;
                if (l.Primary || l.PrimaryPressed) _act.Jump = true;
            }
            if (!_keysBroken)
            {
                try
                {
                    if (Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)) _act.Accept = true;
                    if (Input.GetKey(KeyCode.Backspace) || Input.GetKey(KeyCode.Escape)) _act.Back = true;
                    if (Input.GetKey(KeyCode.E)) _act.Action = true;
                    if (Input.GetMouseButton(0)) _act.Primary = true;
                    if (Input.GetKey(KeyCode.Space)) _act.Jump = true;
                }
                catch (System.Exception) { _keysBroken = true; }
            }
            var cam = HaloPlayer.Head();
            if (cam == null) return;
            Vector3 f = cam.transform.forward;
            if (!_act.HaveForward) { _act.Forward = f; _act.HaveForward = true; return; }
            float pitch0 = Mathf.Asin(Mathf.Clamp(_act.Forward.y, -1f, 1f)) * Mathf.Rad2Deg, pitch = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (pitch - pitch0 > 20f) _act.Up = true;       // ours: degrees that count as "looked up / down / left / right"
            if (pitch0 - pitch > 20f) _act.Down = true;
            float yaw = Vector3.SignedAngle(new Vector3(_act.Forward.x, 0f, _act.Forward.z), new Vector3(f.x, 0f, f.z), Vector3.up);
            if (yaw > 25f) _act.Right = true;
            if (yaw < -25f) _act.Left = true;
        }
    }
}

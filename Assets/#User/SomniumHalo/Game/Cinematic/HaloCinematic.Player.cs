using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The local player during cutscenes. Halo's scripts park the player at cutscene bases (a10's x10: player0_x10_base, out in space;
    /// b30: player0_ledge_wait ... player0_ledge_done). Here the player is HELD where they stand while a cutscene owns the view (VR: they
    /// watch the cinema screen; monitor: the picture covers the screen): script teleports / facings of the player are deferred while
    /// <see cref="HoldsPlayer"/> and only the LAST one is applied once the cutscene is over (camera released, cinematic_stop, skip point
    /// closed), 10 ticks later (a teleport / vehicle entry the script itself does right after the cutscene wins: any non-deferred player
    /// placement drops the pending one), behind a short black fade (none when the script's own fade already covers the view). A pending
    /// teleport into nowhere (outside the active BSP, e.g. a space base after the BSP switched back) is dropped. Clear() / a revert drop it.
    /// S calls <see cref="DeferPlayerTeleport"/> at the top of its player teleport (object_teleport / object_set_facing on player0).
    /// </summary>
    public static partial class HaloCinematic
    {
        const int ReleaseDelayTicks = 10, FadeOutTicks = 8, FadeInTicks = 15; // ours

        static bool _pending, _pendMove;
        static Vector3 _pendPos;
        static float _pendYaw;
        static int _releaseAt = -1, _deferredCount;
        static int _xPhase, _xTicks; // 1 fading out, 2 fading in
        static float _xAlpha;

        /// <summary>A cutscene owns the view: cinematic_start .. stop, camera_control, or inside a skip point (cinematic_skip_start ..
        /// cinematic_skip_stop - x10 parks the player before its cinematic_start).</summary>
        public static bool HoldsPlayer => Playing || CameraControlled || SkipAvailable;

        /// <summary>A teleport of the player is deferred / applied later.</summary>
        public static bool PlayerTeleportPending => _pending;

        /// <summary>
        /// Script teleport of the local player (feet position, Unity yaw degrees; <paramref name="facingOnly"/>: object_set_facing).
        /// Returns true when the cutscene took it (do NOT move the player then); false = move the player yourself now (it also drops a
        /// pending one: the script placed the player after the cutscene).
        /// </summary>
        public static bool DeferPlayerTeleport(Vector3 feet, float yawDegrees, bool facingOnly)
        {
            if (!HoldsPlayer && _xPhase == 0)
            {
                if (_pending) Once("tpdrop", "Cinematic: a script placed the player after the cutscene - the deferred teleport is dropped");
                _pending = false; _releaseAt = -1;
                return false;
            }
            if (facingOnly) { if (!_pending) { _pending = true; _pendMove = false; } _pendYaw = yawDegrees; }
            else { _pending = true; _pendMove = true; _pendPos = feet; _pendYaw = yawDegrees; }
            _releaseAt = -1;
            _deferredCount++;
            Once("tpdefer", "Cinematic: the player is held where they stand during cutscenes - script teleports deferred (the last one applies after the cutscene)");
            return true;
        }

        static GameObject _floor;

        /// <summary>An invisible floor under the held player: the scripts switch BSPs during cutscenes (x10: to the space BSP 7), which
        /// takes the ground away from where the player stands - they would fall (and K would count a death). Ours.</summary>
        static void HoldFloor(bool want)
        {
            if (want == (_floor != null)) return;
            if (!want) { Object.Destroy(_floor); _floor = null; return; }
            Vector3 feet;
            if (!HaloPlayer.TryFeet(out feet)) return;
            _floor = new GameObject("Halo Cinema Floor");
            _floor.layer = HaloLayers.World;
            if (HaloCinemaView.Instance != null) _floor.transform.SetParent(HaloCinemaView.Instance.transform, true);
            _floor.transform.position = feet - new Vector3(0f, 0.1f, 0f);
            var box = _floor.AddComponent<BoxCollider>();
            box.size = new Vector3(8f, 0.2f, 8f);
            Once("floor", "Cinematic: the held player stands on a cinema floor at " + feet.ToString("F1") + " while the cutscene switches BSPs");
        }

        static void PlayerStep()
        {
            HoldFloor(HoldsPlayer || _pending || _xPhase == 1);
            if (_xPhase == 1)
            {
                _xTicks++;
                _xAlpha = Mathf.Clamp01((float)_xTicks / FadeOutTicks);
                if (_xTicks >= FadeOutTicks) { ApplyPending(); _xPhase = 2; _xTicks = 0; }
                return;
            }
            if (_xPhase == 2)
            {
                _xTicks++;
                _xAlpha = 1f - Mathf.Clamp01((float)_xTicks / FadeInTicks);
                if (_xTicks >= FadeInTicks) { _xPhase = 0; _xAlpha = 0f; }
                return;
            }
            if (!_pending) return;
            if (HoldsPlayer) { _releaseAt = -1; return; }
            if (_releaseAt < 0) { _releaseAt = Ticks + ReleaseDelayTicks; return; }
            if (Ticks < _releaseAt) return;
            if (FadeAlpha() >= 0.99f) { ApplyPending(); return; } // the script's own fade covers it
            _xPhase = 1; _xTicks = 0;
        }

        static void ApplyPending()
        {
            if (!_pending) return;
            _pending = false; _releaseAt = -1;
            Vector3 p = _pendPos;
            if (!_pendMove && !HaloPlayer.TryFeet(out p)) return;
            if (_pendMove && HaloWorld.Outside(p + Vector3.up * 0.3f))
            {
                HaloLog.Info("Cinematic: deferred player teleport to " + p.ToString("F1") + " dropped (outside the active BSP)");
                return;
            }
            string how;
            bool ok = HaloPlayer.Teleport(p, _pendYaw, out how);
            HaloLog.Info("Cinematic: after the cutscene the player " + (_pendMove ? "moves to " + p.ToString("F1") : "turns") + " yaw " + _pendYaw.ToString("0")
                         + " (" + _deferredCount + " deferred script teleports, last applied" + (ok ? "" : " - FAILED") + ": " + how + ")");
            _deferredCount = 0;
        }

        static void ClearPlayer() { HoldFloor(false); _pending = false; _releaseAt = -1; _xPhase = 0; _xAlpha = 0f; _deferredCount = 0; }

        /// <summary>The black of the deferred-teleport transition (0 = none).</summary>
        internal static float TransitionAlpha => _xAlpha;
    }
}

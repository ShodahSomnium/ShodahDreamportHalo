using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Script module, S] The engine functions of HaloScript bound to the Dreamport's modules (one partial file per area). Every native
    /// is a static method (no closures): HsVm calls it with the evaluated arguments and takes the result in the function's own type.
    /// A function a module cannot do yet is simply not bound: the VM logs "Script: unimplemented &lt;name&gt;" once and returns the default.
    /// </summary>
    public static partial class HaloScriptFns
    {
        static HsVm _vm;
        static HsHudText _hud;
        static readonly HashSet<string> Said = new HashSet<string>();

        public static void Bind(HsVm vm)
        {
            _vm = vm;
            Said.Clear();
            _hud = HsHudText.Read(vm.D.Scenario);
            BindGame(vm);
            BindObjects(vm);
            BindSound(vm);
            BindHud(vm);
            BindCinematic(vm);
            BindAi(vm);
            BindUnits(vm);
            BindPlayer(vm);
            BindDevices(vm);
            BindCharacters(vm);
            BindInput(vm);
            BindExtra(vm);
            BindRound3(vm);
        }

        /// <summary>After the VM started (globals initialised).</summary>
        internal static void Started()
        {
            _reverted = false;
            HaloCampaign.Reverted -= OnReverted; HaloCampaign.Reverted += OnReverted;
        }

        internal static void Stopped()
        {
            HaloCampaign.Reverted -= OnReverted;
            StopLoops();
            Tweens.Clear();
            Seated.Clear(); CustomLength.Clear(); AttachedTo.Clear(); BaseScale.Clear();
            _effect = default(PlayerEffect);
            _helpShown = false; _helpText = ""; Objective = "";
            _vm = null; _hud = null;
        }

        /// <summary>Before each VM step: object scale tweens, player effect rumble.</summary>
        internal static void PreTick()
        {
            StepTweens();
            StepPlayerEffect();
            StepInput();
        }

        /// <summary>Logs a one-time fact (per key) for Player.log.</summary>
        static void Once(string key, string message) { if (Said.Add(key)) HaloLog.Info(message); }

        // ------------------------------------------------------------------ list helpers

        static int ListOf(HsArgs a, int i) => a.Raw(i);
        static int Count(int list) => _vm != null ? _vm.ListCount(list) : 0;
        static int At(int list, int i) => _vm != null ? _vm.ListGet(list, i) : -1;
        static int NewList() => _vm.ListNew();
        static void Add(int list, int h) { if (h != -1) _vm.ListAdd(list, h); }

        static int B(bool v) => v ? 1 : 0;
        static int R(float f) => HsVm.R(f);
    }
}

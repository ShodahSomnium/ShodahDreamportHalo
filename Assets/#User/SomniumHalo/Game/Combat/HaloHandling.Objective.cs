using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// The game type's objective as the held weapon (CTF flag, oddball; the Match module owns the objective logic and calls
    /// HaloCombat.CarryObjective): it goes into the right hand (VR: it stays there until that hand squeezes and lets go; monitor: 1 / 2 or
    /// E drops it), the other guns stay holstered, no firing, melee with it (its own melee damage: 80). Letting go raises
    /// HaloCombat.ObjectiveDropped. It does not count toward the two weapons (the weapon flag "doesn't count toward maximum").
    /// The pickup prompt (HaloLocal.PickupPrompt + HaloEvents.Message) is kept here too.
    /// </summary>
    public sealed partial class HaloHandling
    {
        private HaloGun _objective;
        private bool _objectiveGripSeen;
        private string _prompt = "";
        private float _promptAgain;
        private readonly HaloPickups.Item[] _hover = new HaloPickups.Item[2];

        public bool CarryingObjective => _objective != null;

        public void CarryObjective(HaloTag weapon)
        {
            if (_objective != null && _objective.Def.Tag == weapon) return;
            DropObjective(Vector3.zero, false);
            var def = HaloCombat.Catalog != null ? HaloCombat.Catalog.Weapon(weapon) : null;
            if (def == null) return;
            DropEverythingInHands(true);
            _objective = HaloGun.Create(def, _gear);
            if (_objective == null) return;
            _objectiveGripSeen = false;
            HaloLocal.Objective = weapon;
            HaloLog.Info("Combat: carrying the objective " + def.Name);
        }

        /// <summary>The objective leaves the hand at its place with <paramref name="velocity"/>; raises ObjectiveDropped when <paramref name="tell"/>.</summary>
        public void DropObjective(Vector3 velocity, bool tell)
        {
            if (_objective == null) return;
            Vector3 at = _objective.transform.position;
            HaloTag tag = _objective.Def.Tag;
            Destroy(_objective.gameObject);
            _objective = null;
            HaloLocal.Objective = null;
            HaloLog.Info("Combat: objective dropped at " + at);
            if (tell) HaloCombat.RaiseObjectiveDropped(tag, at, velocity);
        }

        private void StepObjective(float dt)
        {
            if (_objective == null) return;
            _objective.SetScale(Body.AvatarScale);
            _objective.ResetModel();
            Vector3 hp; Quaternion hr;
            Body.HandPose(true, out hp, out hr);
            if (_desktop) DeskHold(_objective, ref hp, ref hr);
            _objective.transform.SetPositionAndRotation(hp, hr);
            if (!_desktop)
            {
                HaloHand R = HaloXr.Right;
                if (R.GripHeld) _objectiveGripSeen = true;
                if (_objectiveGripSeen && R.GripReleased) { DropObjective(Body.HandVelocity(true), true); return; }
                // melee with it
                Vector3 front = _objective.transform.TransformPoint(_objective.FrontLocal);
                Vector3 vel = _haveFront ? (front - _lastFront) / Mathf.Max(1e-4f, dt) : Vector3.zero;
                _lastFront = front; _haveFront = true;
                if (Time.time >= _meleeReadyAt && vel.magnitude > MeleeSpeed * Body.Scale && SwingAt(front, vel, _objective.Def.MeleeDamage, _objective.Def.Tag, 1))
                    _meleeReadyAt = Time.time + (_objective.Def.MeleeSeconds > 0.2f ? _objective.Def.MeleeSeconds : 1f);
            }
            else
            {
                if (In("1", KeyCode.Alpha1, true) || In("2", KeyCode.Alpha2, true) || In("e", KeyCode.E, true)) { DropObjective(Body.Forward * 1.5f, true); return; }
                if (In("f", KeyCode.F, true)) DeskMelee();
            }
        }

        // ------------------------------------------------------------------ the pickup prompt

        /// <summary>A free hand near a lying weapon buzzes once; the game's prompt shows while one is in reach.</summary>
        private void HoverPickups()
        {
            HaloPickups.Item show = null;
            for (int h = 0; h < 2; h++)
            {
                HaloPickups.Item near = !Busy(h) || (_held >= 0 && h != _gunHand && !Busy(h)) ? HaloPickups.NearestWeapon(HandPos(h), HaloPickups.HandReach * Body.Scale, true) : null;
                if (near != null && near != _hover[h]) HaloXr.Pulse(h == 1, 0.18f, 0.04f);
                _hover[h] = near;
                if (near != null) show = near;
            }

            SetPrompt(show, "[grip]");
        }

        private void DeskPrompt()
        {
            HaloPickups.Item it = HaloPickups.NearestWeapon(Body.Feet + Vector3.up * 0.5f + Body.Forward * 0.4f, 1.6f, true);
            SetPrompt(it, "E");
        }

        private HaloPickups.Item _promptItem;
        private string _promptButton;
        private int _promptCarried = -1;
        private string _promptText = "";

        private void SetPrompt(HaloPickups.Item it, string button)
        {
            // (built only when what is in reach changes: no strings per frame)
            if (it != _promptItem || button != _promptButton || Carried != _promptCarried)
            {
                _promptItem = it; _promptButton = button; _promptCarried = Carried;
                _promptText = BuildPrompt(it, button);
            }

            string p = _promptText;
            if (p != _prompt || (p.Length > 0 && Time.time >= _promptAgain))
            {
                if (p.Length > 0) HaloEvents.RaiseMessage(p, 1.5f);
                _prompt = p;
                _promptAgain = Time.time + 1.4f;
            }

            HaloLocal.PickupPrompt = p;
        }

        private string BuildPrompt(HaloPickups.Item it, string button)
        {
            string p = "";
            if (it != null && it.Gun != null && HaloCombat.Catalog != null)
            {
                bool carried = false;
                for (int i = 0; i < 2; i++) if (Guns[i] != null && Guns[i].Def.Tag == it.Tag) carried = true;
                if (!carried)
                {
                    string name = it.Gun.Def.Name;
                    p = HaloCombat.Catalog.Prompt(Carried >= 2 ? "swap_weapon" : "pickup", button, name) ?? ((Carried >= 2 ? "Hold " + button + " to swap for " : "Hold " + button + " to pick up ") + name);
                }
            }

            return p;
        }
    }
}

#!/bin/bash
# [S] coverage table: function -> status, uses per map (from DevTools/agents/S/uses.txt = harness "hsuses")
S="D:/Dreamport/Halo/Halo Dreamport/Assets/#User/SomniumHalo"
A="D:/Dreamport/Halo/Halo Dreamport/DevTools/agents/S"
BOUND=$(grep -oh 'vm.Bind("[^"]*"' "$S"/Game/Script/*.cs "$S"/Data/Script/HsCore.cs | sed 's/vm.Bind("//; s/"//' | sort -u)
NOOP=$(awk '/NoOps =/,/};/' "$S/Data/Script/HsCore.cs" | grep -o '"[^"]*"' | tr -d '"' | sort -u)
VM=$(printf "%s
" sleep begin sleep_until if set = wake not or and + "<" - "*" ">" "<=" max begin_random ">=" "!=" / min)
owner() {
  case "$1" in
    ai*) echo I;; unit_*|units_*|custom_animation*) echo C;; recording_*|camera_*|cinematic_*|fade_*) echo Q;;
    hud_*|show_hud*|*nav_point*|display_scenario_help|enable_hud_help_flash|pause_hud_timer|show_hud_timer) echo H;;
    device_*) echo K;; sound_*) echo A;; vehicle_*) echo V;; player_action_test*|player0_*|player_enable_input|player_camera_control|player_add_equipment) echo G;;
    object_set_permutation|object_cannot_take_damage|object_can_take_damage|damage_*) echo C/K;; scenery_animation*|breakable*) echo W;;
    *) echo "?";;
  esac
}
echo "| function | status | total | a10 a30 a50 b30 b40 c10 c20 c40 d20 d40 |"
echo "|---|---|---|---|"
tail -n +2 "$A/uses.txt" | grep -v "^functions used" | while read -r name total rest; do
  st="UNIMPLEMENTED ($(owner $name))"
  echo "$VM" | grep -qxF -- "$name" && st="VM"
  echo "$BOUND" | grep -qx -- "$name" && st="bound"
  echo "$NOOP" | grep -qx -- "$name" && st="no-op (by design)"
  echo "| $name | $st | $total | $rest |"
done

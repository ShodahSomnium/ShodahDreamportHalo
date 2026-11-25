#!/bin/bash
# Runs a play-mode lab scenario (Editor/HaloLab.cs + Editor/HaloLab*.cs) in Unity batch mode WITH a graphics device.
# Usage: lab.sh <scenario> [extra unity args, e.g. -halomap bloodgulch]
# Pictures + log go to DevTools/lab/<scenario>/. Prints LAB: lines, [SomniumHalo] lines and script / shader errors.
# Two slots run in parallel: A = the main project (only when no Unity editor has it open - mainopen.ps1; a stale Temp/UnityLockfile is ignored),
# B = the private copy DevTools/labproject (refreshed from the main tree on every run). One Unity per slot (atomic lock dirs).
# NEVER kill Unity processes (other projects' editors run on this machine); a hung lab is ended by its own timeout.
D="/d/Dreamport/Halo/Halo Dreamport/DevTools"
S="${1:-boot}"; shift
# the main project counts as open only when a Unity process has it as its project (a lab killed by its timeout leaves a stale Temp/UnityLockfile)
main_open() { ! powershell -NoProfile -ExecutionPolicy Bypass -File "D:/Dreamport/Halo/Halo Dreamport/DevTools/mainopen.ps1"; }
slot=""; waited=0
while [ -z "$slot" ]; do
  if ! main_open && mkdir "$D/lab.lockA" 2>/dev/null; then slot=A
  elif mkdir "$D/lab.lockB" 2>/dev/null; then slot=B
  else
    for L in "$D/lab.lockA" "$D/lab.lockB"; do
      if [ -d "$L" ] && [ $(( $(date +%s) - $(stat -c %Y "$L") )) -gt 3000 ]; then echo "stale $L - taking it"; rm -rf "$L"; fi
    done
    [ $waited = 0 ] && echo "waiting for a free lab slot..."
    sleep 10; waited=$((waited+10))
  fi
done
trap 'rm -rf "$D/lab.lock$slot"' EXIT
if [ $slot = A ]; then P='D:\Dreamport\Halo\Halo Dreamport'; else bash "$D/labcopy.sh" || exit 1; P='D:\Dreamport\Halo\Halo Dreamport\DevTools\labproject'; fi
OUTW="D:/Dreamport/Halo/Halo Dreamport/DevTools/lab/$S"; OUTU="$D/lab/$S"; mkdir -p "$OUTU"
LOG="$OUTU/lab.log"
echo "lab '$S' in slot $slot ($P)"
timeout 1500 "/c/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Unity.exe" -batchmode -projectPath "$P" -halolab "$S" -halout "$OUTW" -logFile "$OUTW/lab.log" "$@"
echo "unity exit $? (slot $slot)"
grep -a -n "Shader error\|error CS\|Exception" "$LOG" | grep -v "LogAssemblyErrors\|Meshia\|AssemblyUpdater\|Unity.Burst\|Mono.Cecil\|APIUpdater" | head -20
grep -a "LAB:\|SomniumHalo\]" "$LOG" | grep -v "^UnityEngine\|^ *at " | head -120

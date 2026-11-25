#!/bin/bash
# The private lab copy of the project (slot B of lab.sh): DevTools/labproject, outside Assets so nothing imports it twice.
# First run copies the whole project including Library (so it does not import from scratch); later runs mirror only the code
# tree (Assets/#User) and the scene. Uses PowerShell robocopy (Git Bash mangles /MIR).
D='D:\Dreamport\Halo\Halo Dreamport'
L="$D\DevTools\labproject"
if [ ! -f "/d/Dreamport/Halo/Halo Dreamport/DevTools/labproject/.copied" ] || [ ! -d "/d/Dreamport/Halo/Halo Dreamport/DevTools/labproject/Assets/Photon" ] || [ ! -d "/d/Dreamport/Halo/Halo Dreamport/DevTools/labproject/Assets/Plugins" ] || [ ! -d "/d/Dreamport/Halo/Halo Dreamport/DevTools/labproject/Assets/Urp" ]; then
  powershell -NoProfile -Command "foreach (\$d in 'Assets','Packages','ProjectSettings','Library') { robocopy \"$D\\$d\" \"$L\\$d\" /E /NFL /NDL /NJH /NJS /NP /XD Temp | Out-Null }" 
  touch "/d/Dreamport/Halo/Halo Dreamport/DevTools/labproject/.copied"
fi
powershell -NoProfile -Command "robocopy \"$D\Packages\" \"$L\Packages\" manifest.json packages-lock.json /NFL /NDL /NJH /NJS /NP | Out-Null; robocopy \"$D\ProjectSettings\" \"$L\ProjectSettings\" /E /NFL /NDL /NJH /NJS /NP | Out-Null"
powershell -NoProfile -Command "robocopy \"$D\Assets\#User\" \"$L\Assets\#User\" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null; Copy-Item -Force \"$D\Assets\Halo Scene.unity\",\"$D\Assets\Halo Scene.unity.meta\" \"$L\Assets\\\"" 
echo "lab copy ready"

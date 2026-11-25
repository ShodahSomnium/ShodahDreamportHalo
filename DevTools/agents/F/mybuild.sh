#!/bin/bash
# F's private compile while other modules' files are mid-edit: runtime minus Game/Combat (only its stub HaloCombat.cs) and minus files given as args; then the editor tools.
B="D:/Dreamport/Halo/Halo Dreamport/DevTools/build"; O="D:/Dreamport/Halo/Halo Dreamport/DevTools/agents/F/buildout"; mkdir -p "$O"
U="C:/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Data"; SRC="D:/Dreamport/Halo/Halo Dreamport/Assets/#User/SomniumHalo"
EX="${EXCLUDE:-/Game/Combat/}"
RT=$(find "$SRC" -name "*.cs" -not -path "*/Editor/*" | grep -v -E "$EX" | sort; echo "$SRC/Game/Combat/HaloCombat.cs")
ED=$(find "$SRC" -path "*/Editor/*" -name "*.cs" | grep -v -E "${EXED:-HaloLabVehicles|HaloLabCombat}" | sort)
for kind in runtime runtime_server; do
{ cat "$B/$kind.head"; echo "-out:\"$O/$kind.dll\""; echo "$RT" | while IFS= read -r f; do echo "\"$f\""; done; } > "$O/$kind.rsp"
"$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -noconfig @"$O/$kind.rsp" > "$O/$kind.out" 2>&1; echo "$kind errors $(grep -c 'error CS' "$O/$kind.out")"; grep "error CS" "$O/$kind.out" | sed 's|.*SomniumHalo.||' | sort -u | head -20
done
{ cat "$B/editortool.head"; echo "-out:\"$O/editor_tool.dll\""; echo "-r:\"$O/runtime.dll\""; echo "$ED" | while IFS= read -r f; do echo "\"$f\""; done; } > "$O/ed.rsp"
"$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -noconfig @"$O/ed.rsp" > "$O/ed.out" 2>&1; echo "editor errors $(grep -c 'error CS' "$O/ed.out")"; grep "error CS" "$O/ed.out" | sed 's|.*SomniumHalo.||' | sort -u | head -20

#!/bin/bash
# M's private compile: lead core + Match files + the lead's original module stubs (scratch), so M's code is checked while other
# modules' files are mid-edit. Runtime (editor-like + server-like heads) and the editor tool (HaloLab.cs + HaloLabMatch.cs).
S="C:/Users/derii/AppData/Local/Temp/claude/D--Dreamport-Halo-Halo-Dreamport/bc1d9b57-3067-4759-9da3-d6fc96d2fe95/scratchpad/mb"
B="D:/Dreamport/Halo/Halo Dreamport/DevTools/build"; R="D:/Dreamport/Halo/Halo Dreamport/Assets/#User/SomniumHalo"
U="C:/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Data"
FILES=$( { find "$R/Core" "$R/Data/Map" "$R/Data/Bitmaps" "$R/Data/Models" "$R/Game/Shared" "$R/Game/Access" "$R/Game/Match" -name "*.cs" | grep -v "HaloLocal\.\(Combat\|Vehicles\|Net\|Hud\|Front\|Desk\)"; echo "$R/Game/HaloGame.cs"; echo "$R/Game/HaloSettings.cs"; echo "$R/Game/HaloGame.Match.cs"; echo "$R/Game/Vr/HaloPlayer.cs"; for f in HaloTextures HaloMaterials HaloModelBuilder; do echo "$S/stubs/$f.cs"; done; echo "$S/stubs/stubs.cs"; } )
fail=0
for h in runtime runtime_server; do
  { cat "$B/$h.head"; echo "-out:\"$S/m_$h.dll\""; echo "$FILES" | while IFS= read -r f; do [ -n "$f" ] && echo "\"$f\""; done; } > "$S/$h.rsp"
  "$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -noconfig @"$S/$h.rsp" > "$S/$h.out" 2>&1
  echo "$h: errors $(grep -c 'error CS' "$S/$h.out") warnings $(grep -c 'warning CS' "$S/$h.out")"; grep -E 'error CS|warning CS' "$S/$h.out" | sed 's|.*SomniumHalo/||' | sort -u | head -20
  grep -q 'error CS' "$S/$h.out" && fail=1
done
{ cat "$B/editortool.head"; echo "-out:\"$S/m_tool.dll\""; echo "-r:\"$S/m_runtime.dll\""; echo "\"$R/Editor/HaloLab.cs\""; echo "\"$R/Editor/HaloLabMatch.cs\""; } > "$S/tool.rsp"
"$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -noconfig @"$S/tool.rsp" > "$S/tool.out" 2>&1
echo "tool: errors $(grep -c 'error CS' "$S/tool.out")"; grep -E 'error CS|warning CS' "$S/tool.out" | sed 's|.*SomniumHalo/||' | sort -u | head -20
grep -q 'error CS' "$S/tool.out" && fail=1
exit $fail

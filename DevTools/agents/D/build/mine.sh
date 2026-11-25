#!/bin/bash
# D's private compile check: the runtime minus the files of other modules that currently fail (iterated), then the editor tools with my lab.
B="D:/Dreamport/Halo/Halo Dreamport/DevTools/build"
SRC="D:/Dreamport/Halo/Halo Dreamport/Assets/#User/SomniumHalo"
OUT="D:/Dreamport/Halo/Halo Dreamport/DevTools/agents/D/build/out"; mkdir -p "$OUT"
U="C:/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Data"
EXCL=""
for it in 1 2 3 4 5 6; do
  FILES=$(find "$SRC" -name "*.cs" -not -path "*/Editor/*" | sort)
  for x in $EXCL; do FILES=$(echo "$FILES" | grep -v -F "$x"); done
  { cat "$B/runtime.head"; echo "-out:\"$OUT/editor_like.dll\""; echo "$FILES" | while IFS= read -r f; do [ -n "$f" ] && echo "\"$f\""; done; } > "$OUT/r.rsp"
  "$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -noconfig @"$OUT/r.rsp" > "$OUT/r.out" 2>&1
  bad=$(grep 'error CS' "$OUT/r.out" | sed 's|(.*||; s|.*SomniumHalo[/\]||' | sort -u | grep -v -E "Desk|Anims")
  mine=$(grep 'error CS' "$OUT/r.out" | grep -E "Desk[/\\]|Anims[/\\]")
  if [ -n "$mine" ]; then echo "MY ERRORS:"; echo "$mine" | sed 's|.*SomniumHalo[/\]||' | sort -u | head -30; fi
  [ -z "$bad" ] && break
  for b in $(echo "$bad" | tr "\\\\" "/"); do EXCL="$EXCL $b"; done
done
echo "excluded (others' failing files):$EXCL"
echo "runtime: $(grep -c 'error CS' "$OUT/r.out") errors"
grep 'error CS' "$OUT/r.out" | sed 's|.*SomniumHalo[/\]||' | sort -u | head -10
ED=$(find "$SRC" -path "*/Editor/*" -name "*.cs" | grep -E "HaloLab.cs|HaloLabDesk.cs")
{ cat "$B/editortool.head"; echo "-out:\"$OUT/editor_tool.dll\""; echo "-r:\"$OUT/editor_like.dll\""; echo "$ED" | while IFS= read -r f; do echo "\"$f\""; done; } > "$OUT/e.rsp"
"$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -noconfig @"$OUT/e.rsp" > "$OUT/e.out" 2>&1
echo "editor (lab + mine): $(grep -c 'error CS' "$OUT/e.out") errors"; grep 'error CS' "$OUT/e.out" | sed 's|.*SomniumHalo[/\]||' | sort -u | head -20
grep 'warning CS' "$OUT/r.out" | grep -E "Desk|Anims" | sed 's|.*SomniumHalo[/\]||' | sort -u | head

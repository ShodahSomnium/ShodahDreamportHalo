#!/bin/bash
# C's private compile (runtime editor-like only) skipping files matching $1 (a grep -E pattern) - for when other modules' files are mid-edit.
B="D:/Dreamport/Halo/Halo Dreamport/DevTools/build"
SRC="D:/Dreamport/Halo/Halo Dreamport/Assets/#User/SomniumHalo"
OUT="D:/Dreamport/Halo/Halo Dreamport/DevTools/agents/C/out"; mkdir -p "$OUT"
U="C:/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Data"
X="${1:-^$}"
RUNTIME_CS=$(find "$SRC" -name "*.cs" -not -path "*/Editor/*" -not -path "*/Tests/*" | grep -v -E "$X" | sort)
{ cat "$B/runtime.head"; echo "-out:\"$OUT/c.dll\""; echo "$RUNTIME_CS" | while IFS= read -r f; do [ -n "$f" ] && echo "\"$f\""; done; } > "$OUT/c.rsp"
"$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -noconfig @"$OUT/c.rsp" > "$OUT/c.out" 2>&1
grep -E 'error CS' "$OUT/c.out" | sed 's|.*SomniumHalo/||' | sort -u | head -60
grep -E 'warning CS' "$OUT/c.out" | grep Characters | sed 's|.*SomniumHalo/||' | sort -u | head -20
# the editor side: HaloLab.cs + my lab against c.dll
{ cat "$B/editortool.head"; echo "-out:\"$OUT/ced.dll\""; echo "-r:\"$OUT/c.dll\""; echo "\"$SRC/Editor/HaloLab.cs\""; echo "\"$SRC/Editor/HaloLabCharacters.cs\""; } > "$OUT/ced.rsp"
"$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -noconfig @"$OUT/ced.rsp" > "$OUT/ced.out" 2>&1
grep -E 'error CS' "$OUT/ced.out" | sed 's|.*SomniumHalo/||' | sort -u | head -30

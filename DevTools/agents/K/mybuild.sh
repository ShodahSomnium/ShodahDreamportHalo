#!/bin/bash
# K's private compile: the shared build.sh recipe, minus files given in $EXCLUDE (regex) that other agents are mid-edit on.
B="D:/Dreamport/Halo/Halo Dreamport/DevTools/build"
SRC="D:/Dreamport/Halo/Halo Dreamport/Assets/#User/SomniumHalo"
OUT="D:/Dreamport/Halo/Halo Dreamport/DevTools/agents/K/build"; mkdir -p "$OUT"
U="C:/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Data"
X="${EXCLUDE:-^$}"
RUNTIME_CS=$(find "$SRC" -name "*.cs" -not -path "*/Editor/*" -not -path "*/Tests/*" | grep -v -E "$X" | sort; [ -n "$STUBS" ] && ls "D:/Dreamport/Halo/Halo Dreamport/DevTools/agents/K/stubs/"*.cs)
EDITOR_CS=$(find "$SRC" -path "*/Editor/*" -name "*.cs" | grep -v -E "$X" | sort)
mk() { cat "$B/$1.head"; echo "-out:\"$OUT/$2\""; [ -n "$4" ] && echo "-r:\"$4\""; echo "$3" | while IFS= read -r f; do [ -n "$f" ] && echo "\"$f\""; done; }
mk runtime editor_like.dll "$RUNTIME_CS" > "$OUT/runtime.gen.rsp"
mk editortool editor_tool.dll "$EDITOR_CS" "$OUT/editor_like.dll" > "$OUT/editortool.gen.rsp"
for r in runtime editortool; do
  "$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -noconfig @"$OUT/$r.gen.rsp" > "$OUT/$r.out" 2>&1
  echo "$r: errors $(grep -c 'error CS' "$OUT/$r.out")"; grep 'error CS' "$OUT/$r.out" | sed 's|.*SomniumHalo/||' | sort -u | head -30
done

#!/bin/bash
# Offline compile of the Halo Dreamport two ways: editor-like and server-like (the Somnium upload server's reference set,
# with Fusion 2.1.3 from this project since the 2026-10-21 migration), plus the editor tools.
# .cs files are globbed every run. Usage: build.sh [outdir]. Prints error counts + first errors; exit 1 on errors.
B="D:/Dreamport/Halo/Halo Dreamport/DevTools/build"
SRC="D:/Dreamport/Halo/Halo Dreamport/Assets/#User/SomniumHalo"
OUT="${1:-$B/out}"; mkdir -p "$OUT"
U="C:/Program Files/Unity/Hub/Editor/6000.3.5f2/Editor/Data"
RUNTIME_CS=$(find "$SRC" -name "*.cs" -not -path "*/Editor/*" -not -path "*/Tests/*" | sort)
EDITOR_CS=$(find "$SRC" -path "*/Editor/*" -name "*.cs" | sort)
mk() { # $1 head, $2 out dll, $3 file list, $4 extra ref
  cat "$B/$1.head"; echo "-out:\"$OUT/$2\""; [ -n "$4" ] && echo "-r:\"$4\""
  echo "$3" | while IFS= read -r f; do [ -n "$f" ] && echo "\"$f\""; done
}
mk runtime editor_like.dll "$RUNTIME_CS" > "$OUT/runtime.gen.rsp"
mk runtime_server server_like.dll "$RUNTIME_CS" > "$OUT/runtime_server.gen.rsp"
mk editortool editor_tool.dll "$EDITOR_CS" "$OUT/editor_like.dll" > "$OUT/editortool.gen.rsp"
fail=0
for r in runtime runtime_server editortool; do
  if [ "$r" = editortool ] && [ -z "$EDITOR_CS" ]; then echo "editortool: no editor files"; continue; fi
  "$U/NetCoreRuntime/dotnet.exe" "$U/DotNetSdkRoslyn/csc.dll" -noconfig @"$OUT/$r.gen.rsp" > "$OUT/$r.out" 2>&1
  e=$(grep -c 'error CS' "$OUT/$r.out"); w=$(grep -c 'warning CS' "$OUT/$r.out")
  echo "$r: errors $e warnings $w"; [ "$e" != 0 ] && fail=1
  grep 'error CS' "$OUT/$r.out" | sed 's|.*SomniumHalo/||' | sort -u | head -40
done
# Span types: the Fusion 2.0 upload server's mscorlib had none (WoW Dreamport finding, CS7069). Fusion 2.1's INetworkRunnerCallbacks.OnReliableDataReceived
# takes a ReadOnlySpan<byte> (Community Modules 4.0's own SceneNetworking has it), so that one signature in SceneNetworking.cs is allowed; any other use is not
SPAN=$(grep -rn -E 'Span<|Memory<|stackalloc' "$SRC" --include=*.cs | grep -v -E '\.cs:[0-9]+:[[:space:]]*(//|\*|/\*)' | grep -v 'SceneNetworking.cs:.*OnReliableDataReceived(.*ReadOnlySpan<byte> data)')
if [ -n "$SPAN" ]; then echo "server_span: Span/Memory/stackalloc used outside SceneNetworking's Fusion 2.1 callback:"; echo "$SPAN" | sed 's|.*SomniumHalo/||' | head; fail=1; else echo "server_span: only SceneNetworking's Fusion 2.1 callback"; fi
exit $fail

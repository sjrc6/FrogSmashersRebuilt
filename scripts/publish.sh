#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
case "${1:-all}" in
  all) targets=(linux-x64 win-x64) ;;
  linux-x64|win-x64) targets=("$1") ;;
  *) printf 'Usage: %s [all|linux-x64|win-x64]\n' "$0" >&2; exit 2 ;;
esac
python3 src/tools/build_content.py --verify
dotnet build src/Tests/FrogSmashers.Tests.csproj -c Release -m:1 -nr:false
for rid in "${targets[@]}"; do
  out_dir="$repo_dir/.build/releases/FrogSmashersRebuilt-$rid"
  rm -rf -- "$out_dir"
  dotnet publish src/Client/FrogSmashers.Client.csproj -c Release -r "$rid" \
    --self-contained true -m:1 -nr:false -p:RuntimeFrameworkVersion=10.0.10 \
    -p:PublishTrimmed=false -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
    -p:DebugType=embedded -o "$out_dir"
  if [[ "$rid" == linux-x64 ]]; then
    rm -rf -- "$out_dir/runtimes/win-x64"
  else
    rm -rf -- "$out_dir/runtimes/linux-x64"
  fi
  printf '480\n' > "$out_dir/steam_appid.txt"
  mkdir -p "$out_dir/licenses"
  cp src/Notices/* "$out_dir/licenses/"
  cp LICENSE.md "$out_dir/"
  mkdir -p "$out_dir/docs"
  cp docs/*.md "$out_dir/docs/"
  build_dir="$repo_dir/.build/bin/FrogSmashers.Client/release_$rid"
  dotnet .build/bin/FrogSmashers.Tests/release/FrogSmashers.Tests.dll --describe-build "$build_dir" > "$build_dir/BuildInfo.json"
done
python3 scripts/package-archives.py "${targets[@]}"

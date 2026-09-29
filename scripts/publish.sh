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
    -p:PublishTrimmed=false -p:PublishSingleFile=false -o "$out_dir"
  if [[ "$rid" == linux-x64 ]]; then
    rm -f -- "$out_dir/steam_api64.dll"
  else
    rm -f -- "$out_dir/libsteam_api.so"
  fi
  printf '480\n' > "$out_dir/steam_appid.txt"
  mkdir -p "$out_dir/licenses"
  cp src/Notices/* "$out_dir/licenses/"
  cp src/ContentBuild/CREDITS.txt src/ContentBuild/ORIGINAL_LICENSE.md "$out_dir/"
  cp docs/PLAYER_README.md "$out_dir/README.md"
  cp LICENSE.md "$out_dir/"
  mkdir -p "$out_dir/docs"
  cp docs/NETWORKING.md "$out_dir/docs/"
  dotnet .build/bin/FrogSmashers.Tests/release/FrogSmashers.Tests.dll --describe-build "$out_dir" > "$out_dir/BuildInfo.json"
done
python3 scripts/package-archives.py "${targets[@]}"

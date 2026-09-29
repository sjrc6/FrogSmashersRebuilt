#!/usr/bin/env bash
set -euo pipefail
repo_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
exec dotnet "$repo_dir/.build/bin/FrogSmashers.Client/release/FrogSmashersRebuilt.dll" "$@"

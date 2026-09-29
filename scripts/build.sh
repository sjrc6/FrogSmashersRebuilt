#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
dotnet build FrogSmashersRebuilt.slnx -c Release -m:1 -nr:false "$@"

#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
dotnet build FrogSmashersRebuilt.slnx -c Release -m:1 -nr:false
dotnet .build/bin/FrogSmashers.Tests/release/FrogSmashers.Tests.dll "$@"
ALSOFT_DRIVERS=null dotnet .build/bin/FrogSmashers.Client.Tests/release/FrogSmashers.Client.Tests.dll --audio

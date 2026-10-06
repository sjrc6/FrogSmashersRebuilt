#!/usr/bin/env bash
source "$(dirname -- "${BASH_SOURCE[0]}")/common.sh"
dotnet build FrogSmashersRebuilt.slnx -c Release -m:1 -nr:false
dotnet .build/bin/GGCS.Tests/release/GGCS.Tests.dll "$@"
dotnet .build/bin/FrogSmashers.Tests/release/FrogSmashers.Tests.dll "$@"
dotnet .build/bin/FrogSmashers.Updater.Tests/release/FrogSmashers.Updater.Tests.dll
ALSOFT_DRIVERS=null dotnet .build/bin/FrogSmashers.Client.Tests/release/FrogSmashers.Client.Tests.dll --audio

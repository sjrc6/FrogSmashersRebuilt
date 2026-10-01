# Third-party components

These notices describe the pinned components bundled with this build. License text is copied from the listed packages or upstream source revisions.

| Component | Version and source | Notice |
| --- | --- | --- |
| .NET runtime | Microsoft.NETCore.App.Runtime, 10.0.10; https://github.com/dotnet/runtime | `dotnet-LICENSE.txt`, `dotnet-THIRD-PARTY-NOTICES.txt` from the runtime NuGet package |
| MonoGame DesktopGL | 3.8.5.1, https://github.com/MonoGame/MonoGame/tree/5e9fe9b100ffa67026dff97cc8adffae1414534e | `MonoGame-LICENSE.txt` (Microsoft Public License) |
| NVorbis | 0.10.4, https://github.com/NVorbis/NVorbis | `NVorbis-LICENSE.txt` from the NuGet package |
| SDL | MonoGame.Library.SDL 2.32.10.2, https://github.com/MonoGame/MonoGame.Library.SDL/tree/9f64afcc0c04d1714c412f8be2704d47096a6b9e | `SDL-LICENSE.txt` from the NuGet package |
| OpenAL Soft | MonoGame.Library.OpenAL 1.24.3.4, https://github.com/MonoGame/MonoGame.Library.OpenAL/tree/4d08985956a3278adad0bd51486fc1b217b829d2 | `OpenAL-COPYING.txt`, `OpenAL-BSD-NOTICE.txt` |
| Steamworks.NET | 2024.8.0, https://github.com/rlabrecque/Steamworks.NET/tree/a2fc889ab2672981ec3e6225d551d86ce6923121 | `Steamworks.NET-LICENSE.txt` (MIT wrapper license) |
| GGCS rollback | C# adaptation of GGRS/GGPO; pinned revisions listed in the notice | `GGCS-THIRD-PARTY-NOTICES.txt` (MIT) |

OpenAL Soft source corresponding to the bundled build is https://github.com/kcat/openal-soft/tree/dc7d7054a5b4f3bec1dc23a42fd616a0847af948.

`libsteam_api.so` and `steam_api64.dll` are Valve's Steamworks SDK 1.60 API redistributables, from the Steamworks.NET revision above. The MIT license applies to the C# wrapper. https://partner.steamgames.com/doc/sdk/api.

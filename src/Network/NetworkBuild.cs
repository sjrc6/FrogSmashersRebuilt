using System.Security.Cryptography;
using System.Text;
using FrogSmashers.Core;

namespace FrogSmashers.Network;

public static class NetworkBuild
{
    public const string Protocol = "FrogSmashersRebuilt.GGCS.1";

    public static string ContentFingerprint(string contentHash) =>
        ContentFingerprint(
            contentHash,
            typeof(World).Module.ModuleVersionId,
            typeof(NetworkSession).Module.ModuleVersionId,
            typeof(GGCS.SessionOptions).Module.ModuleVersionId
        );

    public static string ContentFingerprint(
        string contentHash,
        Guid coreModuleId,
        Guid networkModuleId,
        Guid rollbackModuleId
    ) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    $"{Protocol}|{coreModuleId:N}|{networkModuleId:N}|{rollbackModuleId:N}|{contentHash}"
                )
            )
        );
}

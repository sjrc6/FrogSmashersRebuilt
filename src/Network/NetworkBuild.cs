using System.Security.Cryptography;
using System.Text;
using FrogSmashers.Core;

namespace FrogSmashers.Network;

public static class NetworkBuild
{
    public static string ContentFingerprint(string contentHash) =>
        ContentFingerprint(
            contentHash,
            typeof(World).Module.ModuleVersionId,
            typeof(NetworkSession).Module.ModuleVersionId
        );

    public static string ContentFingerprint(string contentHash, Guid coreModuleId, Guid networkModuleId) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    $"FrogSmashersRebuilt.Protocol.2|{coreModuleId:N}|{networkModuleId:N}|{contentHash}"
                )
            )
        );
}

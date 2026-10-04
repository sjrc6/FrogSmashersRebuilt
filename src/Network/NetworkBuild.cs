using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FrogSmashers.Core;

namespace FrogSmashers.Network;

public static class NetworkBuild
{
    public static string Protocol { get; } = Metadata(typeof(NetworkBuild).Assembly, "NetworkProtocol");
    public const string IdentityMetadataKey = "SimulationInputHash";
    public static string CodeHash { get; } =
        CombineCodeHashes(
            Metadata(typeof(World).Assembly, IdentityMetadataKey),
            Metadata(typeof(NetworkSession).Assembly, IdentityMetadataKey),
            Metadata(typeof(GGCS.SessionOptions).Assembly, IdentityMetadataKey)
        );

    public static NetworkCompatibility Describe(string gameplayHash) => new(Protocol, CodeHash, gameplayHash);

    public static string Identity(string gameplayHash) => Describe(gameplayHash).Identifier;

    public static string CombineCodeHashes(string core, string network, string rollback) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{core}|{network}|{rollback}")));

    private static string Metadata(Assembly assembly, string key) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(value => value.Key == key).Value
        ?? throw new InvalidOperationException($"Missing simulation identity in {assembly.GetName().Name}");
}

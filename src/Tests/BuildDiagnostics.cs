using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestFixtures;

namespace FrogSmashers.Tests;

internal static class BuildDiagnostics
{
    public static void DescribeBuild(string directory)
    {
        string core = Path.Combine(directory, "FrogSmashers.Core.dll");
        string network = Path.Combine(directory, "FrogSmashers.Network.dll");
        string rollback = Path.Combine(directory, "GGCS.dll");
        string content = Path.Combine(directory, "Content", "content.json");
        string Metadata(string file, string key)
        {
            using var stream = File.OpenRead(file);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
            {
                var attribute = metadata.GetCustomAttribute(handle);
                if (attribute.Constructor.Kind != HandleKind.MemberReference)
                    continue;
                var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (constructor.Parent.Kind != HandleKind.TypeReference)
                    continue;
                var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
                if (
                    metadata.GetString(type.Namespace) != "System.Reflection"
                    || metadata.GetString(type.Name) != "AssemblyMetadataAttribute"
                )
                    continue;
                var value = metadata.GetBlobReader(attribute.Value);
                if (value.ReadUInt16() == 1 && value.ReadSerializedString() == key)
                    return value.ReadSerializedString()
                        ?? throw new InvalidDataException("Missing assembly metadata value");
            }
            throw new InvalidDataException($"Missing {key} in {file}");
        }

        string Sha(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        string coreInputHash = Metadata(core, NetworkBuild.IdentityMetadataKey);
        string networkInputHash = Metadata(network, NetworkBuild.IdentityMetadataKey);
        string rollbackInputHash = Metadata(rollback, NetworkBuild.IdentityMetadataKey);
        var data = GameContent.Load(content);
        var compatibility = new NetworkCompatibility(
            Metadata(network, "NetworkProtocol"),
            NetworkBuild.CombineCodeHashes(coreInputHash, networkInputHash, rollbackInputHash),
            data.ComputeGameplayHash()
        );
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    compatibility.Protocol,
                    compatibility.CodeHash,
                    compatibility.GameplayHash,
                    CoreInputHash = coreInputHash,
                    NetworkInputHash = networkInputHash,
                    RollbackInputHash = rollbackInputHash,
                    CoreSha256 = Sha(core),
                    NetworkSha256 = Sha(network),
                    RollbackSha256 = Sha(rollback),
                    ContentManifestSha256 = Sha(content),
                    data.ContentHash,
                    NetworkIdentity = compatibility.Identifier,
                },
                new JsonSerializerOptions { WriteIndented = true }
            )
        );
    }

    public static void Benchmark()
    {
        var world = MakeWorld(8);
        for (int tick = 0; tick < 900; tick++)
        {
            world.Tick(Enumerable.Range(0, 8).Select(s => Input(tick, s)).ToArray());
        }

        var snapshot = world.Capture();
        var times = new List<double>();
        for (int trial = 0; trial < 110; trial++)
        {
            var timer = Stopwatch.StartNew();
            world.Restore(snapshot);
            for (int frame = 900; frame < 924; frame++)
            {
                world.Tick(Enumerable.Range(0, 8).Select(s => Input(frame, s)).ToArray());
                _ = world.Capture();
                _ = world.HashState();
            }

            if (trial >= 10)
            {
                times.Add(timer.Elapsed.TotalMilliseconds);
            }
        }

        times.Sort();
        Console.WriteLine(
            $"8 players / 24 tick rollback / capture+hash: median={times[50]:F3}ms p95={times[95]:F3}ms max={times[^1]:F3}ms; snapshot={snapshot.Length} bytes"
        );
    }
}

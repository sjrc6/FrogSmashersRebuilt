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
        string content = Path.Combine(directory, "Content", "content.json");
        Guid ModuleId(string file)
        {
            using var stream = File.OpenRead(file);
            using var pe = new PEReader(stream);
            var metadata = pe.GetMetadataReader();
            return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
        }

        string Sha(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        Guid coreId = ModuleId(core);
        Guid networkId = ModuleId(network);
        string contentHash = GameContent.Load(content).ContentHash;
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    Protocol = 2,
                    CoreModuleId = coreId,
                    NetworkModuleId = networkId,
                    CoreSha256 = Sha(core),
                    NetworkSha256 = Sha(network),
                    ContentManifestSha256 = Sha(content),
                    ContentHash = contentHash,
                    NetworkFingerprint = NetworkBuild.ContentFingerprint(contentHash, coreId, networkId),
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

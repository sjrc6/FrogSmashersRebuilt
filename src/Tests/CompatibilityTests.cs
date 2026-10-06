using System.Net;
using System.Text.Json;
using FrogSmashers.Core;
using FrogSmashers.Network;
using static FrogSmashers.Tests.TestAssert;

namespace FrogSmashers.Tests;

internal static class CompatibilityTests
{
    public static void Run()
    {
        var content = new GameContent
        {
            Maps = [TestFixtures.Map()],
            PresentationScenes = new() { ["Lobby"] = TestFixtures.Map() },
        };
        string baseline = content.ComputeGameplayHash();
        GameContent Copy() => JsonSerializer.Deserialize<GameContent>(JsonSerializer.Serialize(content))!;
        Action<GameContent>[] cosmetics =
        [
            data => data.ContentHash = "different full asset manifest",
            data => data.AssetHashes["texture.xnb"] = "different pixels",
            data => data.Sounds["jump"] = ["different.wav"],
            data => data.CharacterVisualParameters["scale"] = 2,
            data => data.Maps[0].CameraX = 42,
            data => data.Maps[0].BackgroundColor = [1, 0, 0, 1],
            data => data.PresentationScenes["Title"] = TestFixtures.Map(),
            data => data.CharacterParameters["unusedPresentationValue"] = 12,
        ];
        foreach (var change in cosmetics)
        {
            var edited = Copy();
            change(edited);
            Check(edited.ComputeGameplayHash() == baseline, "Cosmetic data must not split online compatibility");
            Check(
                new World(content, new()).ConfigurationHash == new World(edited, new()).ConfigurationHash,
                "Cosmetic edits also preserve the world's deterministic configuration"
            );
        }
        var defaults = Copy();
        defaults.CharacterParameters["maxRunSpeed"] = 20;
        Check(defaults.ComputeGameplayHash() == baseline, "Explicit defaults and omitted defaults resolve identically");
        var numericFormatting = Copy();
        numericFormatting.Maps[0].Collision[0].Width = 42.000m;
        Check(numericFormatting.ComputeGameplayHash() == baseline, "Decimal formatting does not change gameplay data");
        Action<GameContent>[] gameplay =
        [
            data => data.CharacterParameters["maxRunSpeed"] = 21,
            data => data.Maps[0].Collision[0].Width += 1,
            data => data.Maps[0].Collision[0].Width += .000000001m,
            data => data.Maps[0].Collision[0].OneWay = true,
            data => data.Maps[0].Spawns[0].Y += 1,
            data => data.Maps[0].KillBounds.Bottom -= 1,
            data => data.Maps[0].FlySpawn.X += 1,
            data => data.PresentationScenes["Lobby"].BeachBallSpawn.X += 1,
            data => data.PresentationScenes["Lobby"].Collision[0].X += 1,
            data => data.Maps.Add(TestFixtures.Map()),
        ];
        foreach (var change in gameplay)
        {
            var edited = Copy();
            change(edited);
            Check(edited.ComputeGameplayHash() != baseline, "Simulation data changes must split compatibility");
        }
        var descriptor = NetworkBuild.Describe(baseline);
        Check(
            descriptor.CodeHash.Length == 64 && descriptor.Identifier.Length <= NetworkCompatibility.MaximumLength,
            "The generated descriptor contains a bounded deterministic code identity"
        );
        Check(
            NetworkCompatibility.Rejection(descriptor.Identifier, descriptor.Identifier) == null,
            "Matching descriptors can connect"
        );
        Check(
            NetworkCompatibility.Rejection(descriptor.Identifier, new string('A', 64)) != null,
            "Malformed descriptors cannot connect"
        );
        Check(
            NetworkBuild.CombineCodeHashes("core", "network", "rollback")
                != NetworkBuild.CombineCodeHashes("core", "network", "changed rollback"),
            "Rollback source inputs participate in the same build identity"
        );
        var reasons = new HashSet<string>();
        foreach (
            var other in new[]
            {
                descriptor with
                {
                    Protocol = "unsupported",
                },
                descriptor with
                {
                    CodeHash = new string('0', 64),
                },
                descriptor with
                {
                    GameplayHash = new string('0', 64),
                },
            }
        )
        {
            Check(
                NetworkCompatibility.Rejection(descriptor.Identifier, other.Identifier) is { Length: > 0 } reason
                    && reasons.Add(reason),
                "Admission identifies the incompatible part of the descriptor"
            );
            Check(
                !LanDiscovery.ReadQuery(LanDiscovery.Query(7, other.Identifier), descriptor.Identifier, out _),
                "LAN queries use the same compatibility descriptor"
            );
            var roster = new LobbyRoster();
            byte[] reply = LanDiscovery.Reply(7, other.Identifier, Guid.NewGuid(), 24802, "TEST", roster, true);
            Check(
                LanDiscovery.ReadReply(reply, new IPEndPoint(IPAddress.Loopback, 24802), descriptor.Identifier, 7)
                    == null,
                "LAN replies use the same compatibility descriptor"
            );
            var metadata = new Dictionary<string, string>
            {
                ["game"] = SteamLobby.GameTag,
                ["protocol"] = other.Protocol,
                ["compatibility"] = other.Identifier,
                ["state"] = "forming",
                ["capacity"] = "8",
                ["players"] = "1",
                ["open_slots"] = "7",
                ["friend_slots"] = "0",
                ["name"] = "TEST",
            };
            Check(
                SteamLobbyBrowser.ReadListing(
                    1,
                    key => metadata.GetValueOrDefault(key, ""),
                    descriptor.Identifier,
                    false,
                    1
                ) == null,
                "Steam discovery uses the same compatibility descriptor"
            );
        }
        Console.WriteLine("Generated code identity, gameplay-only hashing and discovery compatibility passed");
    }
}

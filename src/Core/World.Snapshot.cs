using System.Text;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private const int SnapshotMagic = 0x46535253;
    private const int SnapshotVersion = 7;
    private readonly MemoryStream snapshotBuffer = new(4096);

    public byte[] Capture()
    {
        snapshotBuffer.SetLength(0);
        using var writer = new BinaryWriter(snapshotBuffer, Encoding.UTF8, true);
        writer.Write(SnapshotMagic);
        writer.Write(SnapshotVersion);
        writer.Write(ConfigurationHash);
        writer.Write(TickNumber);
        writer.Write(randomState);
        Match.WriteSnapshot(writer);
        writer.Write(Players.Length);
        foreach (var player in Players)
        {
            player.WriteSnapshot(writer);
        }

        Fly.WriteSnapshot(writer);
        BeachBall.WriteSnapshot(writer);
        return snapshotBuffer.ToArray();
    }

    public void Restore(byte[] snapshot)
    {
        if (snapshot.Length > 64 * 1024)
        {
            throw new InvalidDataException("Oversized snapshot");
        }

        using var stream = new MemoryStream(snapshot, false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadInt32() != SnapshotMagic)
        {
            throw new InvalidDataException("Invalid snapshot format");
        }
        if (reader.ReadInt32() != SnapshotVersion)
        {
            throw new InvalidDataException(
                "This replay or snapshot uses an unsupported version. Record a new replay with this build."
            );
        }
        if (reader.ReadUInt64() != ConfigurationHash)
        {
            throw new InvalidDataException("Snapshot configuration mismatch");
        }

        var tick = reader.ReadInt64();
        var rng = reader.ReadUInt32();
        var match = MatchState.ReadSnapshot(reader, Rules, maps.Count);
        int count = reader.ReadInt32();
        if (count != Players.Length || tick < 0)
            throw new InvalidDataException("Invalid snapshot state");

        var players = new PlayerState[count];
        for (int i = 0; i < count; i++)
        {
            players[i] = PlayerState.ReadSnapshot(reader);
            if (
                players[i].ColorIndex is < 0 or > 7
                || players[i].Slot != i
                || players[i].Team != Rules.Teams[i]
                || players[i].Facing is not (-1 or 1)
                || !Enum.IsDefined(players[i].Mode)
                || !Enum.IsDefined(players[i].AttackPhase)
                || !Enum.IsDefined(players[i].TonguePhase)
            )
            {
                throw new InvalidDataException("Invalid player snapshot");
            }
        }

        var fly = FlyState.ReadSnapshot(reader);
        var ball = BeachBallState.ReadSnapshot(reader);
        if (
            stream.Position != stream.Length
            || fly.Owner < -1
            || fly.Owner >= count
            || fly.IngestedBy < -1
            || fly.IngestedBy >= count
        )
        {
            throw new InvalidDataException("Invalid snapshot payload");
        }

        TickNumber = tick;
        randomState = rng;
        Match = match;
        Players = players;
        Fly = fly;
        BeachBall = ball;
        events.Clear();
    }

    public ulong HashState() => Hash(Capture());

    public static ulong Hash(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte value in bytes)
        {
            hash = unchecked((hash ^ value) * 1099511628211UL);
        }

        return hash;
    }

    private ulong ComputeConfigurationHash()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(SnapshotVersion);
        writer.Write(TickRate);
        Rules.WriteConfiguration(writer);

        tuning.WriteConfiguration(writer);
        GameplayData.WriteMaps(writer, maps);

        return Hash(stream.ToArray());
    }
}

using System.Text;
using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private const int SnapshotMagic = 0x46535253;
    private const int SnapshotVersion = 3;
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
        writer.Write(CurrentMapIndex);
        writer.Write(RoundNumber);
        writer.Write((int)Phase);
        writer.Write(Winner);
        writer.Write(PhaseTicks);
        writer.Write(IsShowdown);
        writer.Write(Players.Length);
        foreach (var player in Players)
        {
            player.WriteSnapshot(writer);
        }

        Fly.WriteSnapshot(writer);
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
        var map = reader.ReadInt32();
        var round = reader.ReadInt32();
        var phase = (MatchPhase)reader.ReadInt32();
        var winner = reader.ReadInt32();
        var phaseTicks = reader.ReadInt32();
        var showdown = reader.ReadBoolean();
        int count = reader.ReadInt32();
        if (
            count != Players.Length
            || map < 0
            || map >= maps.Count
            || tick < 0
            || round < 1
            || !Enum.IsDefined(phase)
            || winner < -1
            || winner >= count
        )
        {
            throw new InvalidDataException("Invalid snapshot state");
        }

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
        CurrentMapIndex = map;
        RoundNumber = round;
        Phase = phase;
        Winner = winner;
        PhaseTicks = phaseTicks;
        IsShowdown = showdown;
        Players = players;
        Fly = fly;
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
        writer.Write(Rules.PlayerCount);
        writer.Write(Rules.Lobby);
        foreach (bool cpu in Rules.CpuPlayers)
            writer.Write(cpu);
        foreach (int color in Rules.Colors)
            writer.Write(color);
        writer.Write(Rules.TeamMode);
        foreach (var player in Players)
        {
            writer.Write(player.Team);
        }

        writer.Write(Rules.WinScore);
        writer.Write(Rules.MatchRounds);
        writer.Write(Rules.Showdown);
        writer.Write(Rules.RoundFinishTicks);
        writer.Write(Rules.ScoreScreenTicks);
        writer.Write(Rules.MapOrder.Length);
        foreach (int map in Rules.MapOrder)
        {
            writer.Write(map);
        }

        tuning.WriteConfiguration(writer);
        writer.Write(maps.Count);
        foreach (var map in maps)
        {
            writer.Write(map.Id);
            writer.Write(map.Name);
            writer.Write(FromDecimal(map.KillBounds.Left).Raw);
            writer.Write(FromDecimal(map.KillBounds.Right).Raw);
            writer.Write(FromDecimal(map.KillBounds.Bottom).Raw);
            writer.Write(FromDecimal(map.KillBounds.Top).Raw);
            writer.Write(map.Collision.Count);
            foreach (var box in map.Collision)
            {
                writer.Write(FromDecimal(box.X).Raw);
                writer.Write(FromDecimal(box.Y).Raw);
                writer.Write(FromDecimal(box.Width).Raw);
                writer.Write(FromDecimal(box.Height).Raw);
                writer.Write(box.OneWay);
            }

            writer.Write(map.Spawns.Count);
            foreach (var spawn in map.Spawns)
            {
                writer.Write(FromDecimal(spawn.X).Raw);
                writer.Write(FromDecimal(spawn.Y).Raw);
            }

            writer.Write(FromDecimal(map.FlySpawn.X).Raw);
            writer.Write(FromDecimal(map.FlySpawn.Y).Raw);
        }

        return Hash(stream.ToArray());
    }
}

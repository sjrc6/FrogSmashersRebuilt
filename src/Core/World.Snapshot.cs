using System.Text;
using static FrogSmashers.Core.Fixed;

namespace FrogSmashers.Core;

public sealed partial class World
{
    private const int SnapshotMagic = 0x46535253;
    private const int SnapshotVersion = 1;

    public byte[] Capture()
    {
        using var stream = new MemoryStream(4096);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
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
            WritePlayer(writer, player);
        }

        WriteFly(writer, Fly);
        return stream.ToArray();
    }

    public void Restore(byte[] snapshot)
    {
        if (snapshot.Length > 64 * 1024)
        {
            throw new InvalidDataException("Oversized snapshot");
        }

        using var stream = new MemoryStream(snapshot, false);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (
            reader.ReadInt32() != SnapshotMagic
            || reader.ReadInt32() != SnapshotVersion
            || reader.ReadUInt64() != ConfigurationHash
        )
        {
            throw new InvalidDataException("Snapshot configuration or version mismatch");
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
            players[i] = ReadPlayer(reader);
            if (
                players[i].Slot != i
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

        var fly = ReadFly(reader);
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
        writer.Write(Rules.TeamMode);
        foreach (var player in Players)
        {
            writer.Write(player.Team);
        }

        writer.Write(Rules.WinScore);
        writer.Write(Rules.MatchRounds);
        writer.Write(Rules.CharactersBounceEachOther);
        writer.Write(Rules.WeirdBounceTrajectories);
        writer.Write(Rules.OnlyBounceBeforeRecover);
        writer.Write(Rules.PreservePlatformEmbedding);
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

    private static void WritePlayer(BinaryWriter writer, PlayerState player)
    {
        writer.Write(player.Slot);
        writer.Write(player.Team);
        writer.Write(player.Facing);
        writer.Write(player.LastHitBy);
        writer.Write(player.HitsTaken);
        writer.Write(player.Score);
        writer.Write(player.RoundWins);
        writer.Write(player.WallSlideSide);
        writer.Write(player.SpawnTicks);
        writer.Write(player.HitstopTicks);
        writer.Write(player.Alive);
        writer.Write(player.Eliminated);
        writer.Write(player.OnGround);
        writer.Write(player.WallSliding);
        writer.Write(player.HasFly);
        writer.Write(player.WasHitDownwards);
        writer.Write(player.HasReachedApex);
        writer.Write(player.CanBounceDodge);
        writer.Write(player.HasBounceDodged);
        writer.Write(player.CanBounceTongue);
        writer.Write(player.HasBounceTongued);
        writer.Write(player.WasBouncingBeforeTongue);
        writer.Write(player.X.Raw);
        writer.Write(player.Y.Raw);
        writer.Write(player.VX.Raw);
        writer.Write(player.VY.Raw);
        writer.Write(player.AttackX.Raw);
        writer.Write(player.AttackY.Raw);
        writer.Write(player.TongueX.Raw);
        writer.Write(player.TongueY.Raw);
        writer.Write(player.TongueDistance.Raw);
        writer.Write(player.AttackCharge.Raw);
        writer.Write(player.AttackTimeLeft.Raw);
        writer.Write(player.AttackRecoverTimeLeft.Raw);
        writer.Write(player.TongueDelayLeft.Raw);
        writer.Write(player.JumpGraceLeft.Raw);
        writer.Write(player.GravityGraceLeft.Raw);
        writer.Write(player.JumpCooldownLeft.Raw);
        writer.Write(player.TimeSinceHit.Raw);
        writer.Write(player.BounceGravityRestore.Raw);
        writer.Write(player.SkidRecoverLeft.Raw);
        writer.Write(player.HitstopScale.Raw);
        writer.Write(player.LocalDelta.Raw);
        writer.Write(player.AnimationTime.Raw);
        writer.Write((int)player.Mode);
        writer.Write((int)player.AttackPhase);
        writer.Write((int)player.TonguePhase);
        writer.Write(player.PreviousInput.Packed);
        writer.Write(player.AnimationKey);
        writer.Write(player.StateStartTick);
    }

    private static PlayerState ReadPlayer(BinaryReader reader) =>
        new()
        {
            Slot = reader.ReadInt32(),
            Team = reader.ReadInt32(),
            Facing = reader.ReadInt32(),
            LastHitBy = reader.ReadInt32(),
            HitsTaken = reader.ReadInt32(),
            Score = reader.ReadInt32(),
            RoundWins = reader.ReadInt32(),
            WallSlideSide = reader.ReadInt32(),
            SpawnTicks = reader.ReadInt32(),
            HitstopTicks = reader.ReadInt32(),
            Alive = reader.ReadBoolean(),
            Eliminated = reader.ReadBoolean(),
            OnGround = reader.ReadBoolean(),
            WallSliding = reader.ReadBoolean(),
            HasFly = reader.ReadBoolean(),
            WasHitDownwards = reader.ReadBoolean(),
            HasReachedApex = reader.ReadBoolean(),
            CanBounceDodge = reader.ReadBoolean(),
            HasBounceDodged = reader.ReadBoolean(),
            CanBounceTongue = reader.ReadBoolean(),
            HasBounceTongued = reader.ReadBoolean(),
            WasBouncingBeforeTongue = reader.ReadBoolean(),
            X = new(reader.ReadInt64()),
            Y = new(reader.ReadInt64()),
            VX = new(reader.ReadInt64()),
            VY = new(reader.ReadInt64()),
            AttackX = new(reader.ReadInt64()),
            AttackY = new(reader.ReadInt64()),
            TongueX = new(reader.ReadInt64()),
            TongueY = new(reader.ReadInt64()),
            TongueDistance = new(reader.ReadInt64()),
            AttackCharge = new(reader.ReadInt64()),
            AttackTimeLeft = new(reader.ReadInt64()),
            AttackRecoverTimeLeft = new(reader.ReadInt64()),
            TongueDelayLeft = new(reader.ReadInt64()),
            JumpGraceLeft = new(reader.ReadInt64()),
            GravityGraceLeft = new(reader.ReadInt64()),
            JumpCooldownLeft = new(reader.ReadInt64()),
            TimeSinceHit = new(reader.ReadInt64()),
            BounceGravityRestore = new(reader.ReadInt64()),
            SkidRecoverLeft = new(reader.ReadInt64()),
            HitstopScale = new(reader.ReadInt64()),
            LocalDelta = new(reader.ReadInt64()),
            AnimationTime = new(reader.ReadInt64()),
            Mode = (CharacterMode)reader.ReadInt32(),
            AttackPhase = (AttackPhase)reader.ReadInt32(),
            TonguePhase = (TonguePhase)reader.ReadInt32(),
            PreviousInput = InputFrame.FromPacked(reader.ReadUInt32()),
            AnimationKey = reader.ReadString(),
            StateStartTick = reader.ReadInt64(),
        };

    private static void WriteFly(BinaryWriter writer, FlyState f)
    {
        writer.Write(f.Active);
        writer.Write(f.Owner);
        writer.Write(f.IngestedBy);
        writer.Write(f.ClaimTicks);
        writer.Write(f.SpawnTicks);
        writer.Write(f.DirectionTicks);
        writer.Write(f.X.Raw);
        writer.Write(f.Y.Raw);
        writer.Write(f.VX.Raw);
        writer.Write(f.VY.Raw);
        writer.Write(f.TargetVX.Raw);
        writer.Write(f.TargetVY.Raw);
    }

    private static FlyState ReadFly(BinaryReader reader) =>
        new()
        {
            Active = reader.ReadBoolean(),
            Owner = reader.ReadInt32(),
            IngestedBy = reader.ReadInt32(),
            ClaimTicks = reader.ReadInt32(),
            SpawnTicks = reader.ReadInt32(),
            DirectionTicks = reader.ReadInt32(),
            X = new(reader.ReadInt64()),
            Y = new(reader.ReadInt64()),
            VX = new(reader.ReadInt64()),
            VY = new(reader.ReadInt64()),
            TargetVX = new(reader.ReadInt64()),
            TargetVY = new(reader.ReadInt64()),
        };
}

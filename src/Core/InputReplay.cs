using System.Text;

namespace FrogSmashers.Core;

public sealed class InputReplay
{
    private const int FileMagic = 0x46535250;
    private const int FileVersion = 1;

    public byte[] InitialSnapshot { get; private set; } = [];
    public ulong ConfigurationHash { get; private set; }
    public int PlayerCount { get; private set; }
    public List<InputFrame[]> Frames { get; } = new();
    public Dictionary<int, ulong> Checkpoints { get; } = new();

    public static InputReplay Start(World world) =>
        new()
        {
            InitialSnapshot = world.Capture(),
            ConfigurationHash = world.ConfigurationHash,
            PlayerCount = world.Players.Length,
        };

    public void Record(InputFrame[] input, World afterTick)
    {
        if (input.Length != PlayerCount || afterTick.ConfigurationHash != ConfigurationHash)
        {
            throw new ArgumentException("Replay configuration mismatch");
        }

        Frames.Add((InputFrame[])input.Clone());
        if (Frames.Count % World.TickRate == 0)
        {
            Checkpoints[Frames.Count] = afterTick.HashState();
        }
    }

    public void Play(World world)
    {
        if (world.ConfigurationHash != ConfigurationHash)
        {
            throw new InvalidDataException("Replay configuration mismatch");
        }

        world.Restore(InitialSnapshot);
        for (int i = 0; i < Frames.Count; i++)
        {
            world.Tick(Frames[i]);
            if (Checkpoints.TryGetValue(i + 1, out var expected) && world.HashState() != expected)
            {
                throw new InvalidDataException($"Replay diverged at input frame {i + 1}");
            }
        }
    }

    public void Save(string path)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(FileMagic);
        writer.Write(FileVersion);
        writer.Write(ConfigurationHash);
        writer.Write(PlayerCount);
        writer.Write(InitialSnapshot.Length);
        writer.Write(InitialSnapshot);
        writer.Write(Frames.Count);
        foreach (var input in Frames)
        {
            foreach (var player in input)
            {
                writer.Write(player.Packed);
            }
        }

        writer.Write(Checkpoints.Count);
        foreach (var checkpoint in Checkpoints.OrderBy(c => c.Key))
        {
            writer.Write(checkpoint.Key);
            writer.Write(checkpoint.Value);
        }
    }

    public static InputReplay Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadInt32() != FileMagic || reader.ReadInt32() != FileVersion)
        {
            throw new InvalidDataException("Unrecognized replay");
        }

        var replay = new InputReplay { ConfigurationHash = reader.ReadUInt64(), PlayerCount = reader.ReadInt32() };
        if (replay.PlayerCount is < 2 or > 8)
        {
            throw new InvalidDataException("Invalid replay player count");
        }

        int bytes = reader.ReadInt32();
        if (bytes is < 1 or > 65536)
        {
            throw new InvalidDataException("Invalid replay snapshot");
        }

        replay.InitialSnapshot = reader.ReadBytes(bytes);
        if (replay.InitialSnapshot.Length != bytes)
        {
            throw new EndOfStreamException();
        }

        int frames = reader.ReadInt32();
        if (
            frames < 0
            || frames > World.TickRate * 60 * 60 * 24
            || (long)frames * replay.PlayerCount * 4 > stream.Length - stream.Position
        )
        {
            throw new InvalidDataException("Invalid replay length");
        }

        for (int i = 0; i < frames; i++)
        {
            var input = new InputFrame[replay.PlayerCount];
            for (int p = 0; p < input.Length; p++)
            {
                input[p] = InputFrame.FromPacked(reader.ReadUInt32());
            }

            replay.Frames.Add(input);
        }

        int checks = reader.ReadInt32();
        if (checks < 0 || checks > frames)
        {
            throw new InvalidDataException("Invalid replay checkpoints");
        }

        for (int i = 0; i < checks; i++)
        {
            int frame = reader.ReadInt32();
            if (frame < 1 || frame > frames || replay.Checkpoints.ContainsKey(frame))
            {
                throw new InvalidDataException("Invalid replay checkpoint");
            }

            replay.Checkpoints.Add(frame, reader.ReadUInt64());
        }

        if (stream.Position != stream.Length)
        {
            throw new InvalidDataException("Trailing replay data");
        }

        return replay;
    }
}

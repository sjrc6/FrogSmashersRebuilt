using System.Buffers.Binary;
using FrogSmashers.Core;
using GGCS;

namespace FrogSmashers.Network;

public readonly record struct RollbackInput(
    InputFrame Gameplay,
    byte Actions = 0,
    sbyte ColorStep = 0,
    sbyte TeamStep = 0
);

internal sealed class RollbackInputCodec : IInputCodec<RollbackInput>
{
    public int Size => 7;

    public void Encode(RollbackInput input, Span<byte> destination)
    {
        Validate(input);
        BinaryPrimitives.WriteUInt32LittleEndian(destination, input.Gameplay.Packed);
        destination[4] = input.Actions;
        destination[5] = unchecked((byte)input.ColorStep);
        destination[6] = unchecked((byte)input.TeamStep);
    }

    public RollbackInput Decode(ReadOnlySpan<byte> source)
    {
        var input = new RollbackInput(
            InputFrame.FromPacked(BinaryPrimitives.ReadUInt32LittleEndian(source)),
            source[4],
            unchecked((sbyte)source[5]),
            unchecked((sbyte)source[6])
        );
        Validate(input);
        return input;
    }

    private static void Validate(RollbackInput input)
    {
        _ = InputFrame.FromPacked(input.Gameplay.Packed);
        if ((input.Actions & ~3) != 0 || input.ColorStep is < -1 or > 1 || input.TeamStep is < -1 or > 1)
            throw new InvalidDataException("Invalid lobby selection input");
    }
}

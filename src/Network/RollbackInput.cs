using System.Buffers.Binary;
using FrogSmashers.Core;
using GGCS;

namespace FrogSmashers.Network;

public readonly record struct RollbackInput(
    InputFrame Gameplay,
    byte Actions = 0,
    sbyte ColorStep = 0,
    sbyte TeamStep = 0,
    LobbyCpuCommand Cpu = default,
    MatchCommand Match = default
);

internal sealed class RollbackInputCodec : IInputCodec<RollbackInput>
{
    public int Size => 23;

    public void Encode(RollbackInput input, Span<byte> destination)
    {
        Validate(input);
        BinaryPrimitives.WriteUInt32LittleEndian(destination, input.Gameplay.Packed);
        destination[4] = input.Actions;
        destination[5] = unchecked((byte)input.ColorStep);
        destination[6] = unchecked((byte)input.TeamStep);
        BinaryPrimitives.WriteInt32LittleEndian(destination[7..], input.Cpu.Revision);
        destination[11] = input.Cpu.Rooms;
        destination[12] = input.Cpu.Enabled;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[13..], input.Cpu.Colors);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[17..], input.Cpu.Teams);
        destination[21] = (byte)input.Match.Kind;
        destination[22] = input.Match.Selection;
    }

    public RollbackInput Decode(ReadOnlySpan<byte> source)
    {
        var input = new RollbackInput(
            InputFrame.FromPacked(BinaryPrimitives.ReadUInt32LittleEndian(source)),
            source[4],
            unchecked((sbyte)source[5]),
            unchecked((sbyte)source[6]),
            new(
                BinaryPrimitives.ReadInt32LittleEndian(source[7..]),
                source[11],
                source[12],
                BinaryPrimitives.ReadUInt32LittleEndian(source[13..]),
                BinaryPrimitives.ReadUInt32LittleEndian(source[17..])
            ),
            new MatchCommand((MatchCommandKind)source[21], source[22])
        );
        Validate(input);
        return input;
    }

    private static void Validate(RollbackInput input)
    {
        _ = InputFrame.FromPacked(input.Gameplay.Packed);
        input.Match.Validate();
        if (
            (input.Actions & ~7) != 0
            || input.ColorStep is < -1 or > 1
            || input.TeamStep is < -1 or > 1
            || !input.Cpu.IsValid
        )
            throw new InvalidDataException("Invalid lobby selection input");
    }
}

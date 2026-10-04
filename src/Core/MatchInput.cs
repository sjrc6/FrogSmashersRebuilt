namespace FrogSmashers.Core;

public enum MatchCommandKind : byte
{
    None,
    SelectFighter,
}

public readonly record struct MatchCommand(MatchCommandKind Kind, byte Player = 0)
{
    public bool IsValid =>
        Kind switch
        {
            MatchCommandKind.None => Player == 0,
            MatchCommandKind.SelectFighter => Player < 8,
            _ => false,
        };

    public void Validate()
    {
        if (!IsValid)
            throw new InvalidDataException("Invalid match command");
    }
}

public readonly record struct MatchInput(InputFrame Gameplay, MatchCommand Command = default)
{
    public void Validate()
    {
        _ = InputFrame.FromPacked(Gameplay.Packed);
        Command.Validate();
    }
}

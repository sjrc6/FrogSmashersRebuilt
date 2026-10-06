namespace FrogSmashers.Core;

public enum MatchCommandKind : byte
{
    None,
    ChooseCrew,
    SelectFighter,
    BackOutFighter,
    ToggleReady,
    SkipCelebration,
}

public readonly record struct MatchCommand(MatchCommandKind Kind, byte Selection = 0)
{
    public bool IsValid =>
        Kind switch
        {
            MatchCommandKind.None
            or MatchCommandKind.BackOutFighter
            or MatchCommandKind.ToggleReady
            or MatchCommandKind.SkipCelebration => Selection == 0,
            MatchCommandKind.SelectFighter => Selection < 8,
            MatchCommandKind.ChooseCrew => Selection < 2,
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

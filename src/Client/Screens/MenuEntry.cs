using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal enum MenuRole
{
    Normal,
    Positive,
    Destructive,
}

internal sealed record MenuEntry(
    string Id,
    string Label,
    Action? Activate = null,
    Action<int>? Change = null,
    Keys? Key = null,
    MenuRole Role = MenuRole.Normal,
    string? Value = null,
    string? ValueSample = null,
    string? DisabledReason = null,
    Buttons? Button = null,
    bool IsTitle = false,
    bool SeparatorBefore = false,
    bool RepeatAdjust = false,
    string? Help = null,
    ulong? Avatar = null,
    Color? LabelColor = null,
    Color? ValueColor = null
)
{
    public string Text => Value == null ? Label : Label + ": " + Value;

    public Color DisplayColor(bool selected)
    {
        if (DisabledReason != null)
            return new Color(135, 145, 136);
        var color = Role switch
        {
            MenuRole.Positive => new Color(115, 235, 130),
            MenuRole.Destructive => new Color(255, 85, 85),
            _ => Color.White,
        };
        return !selected ? color
            : Role == MenuRole.Normal ? new Color(255, 236, 164)
            : Color.Lerp(color, Color.White, .55f);
    }

    public void Select()
    {
        if (DisabledReason != null)
            return;
        if (Activate != null)
            Activate();
        else
            Change?.Invoke(1);
    }
}

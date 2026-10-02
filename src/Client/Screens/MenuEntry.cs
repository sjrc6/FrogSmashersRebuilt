using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal sealed record MenuEntry(
    string Label,
    Action? Activate = null,
    Action<int>? Change = null,
    Keys? Key = null,
    Color? Color = null,
    string? Value = null,
    string? DisabledReason = null,
    Buttons? Button = null,
    bool IsTitle = false,
    bool SeparatorBefore = false,
    bool RepeatAdjust = false
)
{
    public string Text => Value == null ? Label : Label + ": " + Value;

    public void Select()
    {
        if (Activate != null)
            Activate();
        else
            Change?.Invoke(1);
    }
}

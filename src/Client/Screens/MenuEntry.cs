using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

internal sealed record MenuEntry(
    string Label,
    Action? Activate = null,
    Action<int>? Change = null,
    Keys? Key = null,
    Color? Color = null
)
{
    public void Select()
    {
        if (Activate != null)
            Activate();
        else
            Change?.Invoke(1);
    }
}

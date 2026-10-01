using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class ToastRenderer(FrogGame game)
{
    private const int Width = 360;
    private const int Padding = 28;

    public void Draw()
    {
        var previous = Layout(game.Toasts.Previous);
        var current = Layout(game.Toasts.Current);
        int travel = Math.Max(previous?.Height ?? 0, current?.Height ?? 0);
        Draw(previous, game.Toasts.PreviousVisibility, travel);
        Draw(current, game.Toasts.CurrentVisibility, travel);
    }

    private ToastLayout? Layout(string? message)
    {
        if (message == null)
            return null;
        var font = game.Assets.Font;
        string text = font.Wrap(message, Width - Padding * 2);
        return new(text, (int)MathF.Ceiling(font.Measure(text).Y) + Padding * 2);
    }

    private void Draw(ToastLayout? layout, float visibility, int travel)
    {
        if (layout is not { } toast || visibility <= 0)
            return;
        var scale = game.Assets.Font.PixelScale(1);
        float y = -travel * (1 - visibility);
        y = MathF.Round(y / scale.Y) * scale.Y;
        var panel = new Rectangle(Renderer.Width - Width, (int)y, Width, toast.Height);
        game.Renderer.ToastPanel(panel);
        game.Renderer.Text(toast.Text, panel.Left + Padding, panel.Top + Padding, Color.White);
    }

    private readonly record struct ToastLayout(string Text, int Height);
}

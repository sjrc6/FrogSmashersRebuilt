using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class DisplaySettings
{
    private readonly GameWindow window;
    private readonly GraphicsDeviceManager graphics;
    private readonly ClientSettings settings;
    private readonly bool offscreen;
    private readonly Rectangle? tile;

    public DisplaySettings(
        GameWindow window,
        GraphicsDeviceManager graphics,
        ClientSettings settings,
        bool offscreen,
        Rectangle? tile = null
    )
    {
        this.window = window;
        this.graphics = graphics;
        this.settings = settings;
        this.offscreen = offscreen;
        this.tile = tile;
    }

    public void PositionWindow()
    {
        if (tile is not { } bounds)
            return;
        window.IsBorderless = true;
        if (!graphics.IsFullScreen)
            window.Position = bounds.Location;
    }

    public void MatchDesktopBackBuffer()
    {
        if (!graphics.IsFullScreen)
        {
            return;
        }

        var size = window.ClientBounds;
        var buffer = graphics.GraphicsDevice.PresentationParameters;
        if (
            size.Width <= 0
            || size.Height <= 0
            || size.Width == buffer.BackBufferWidth && size.Height == buffer.BackBufferHeight
        )
        {
            return;
        }

        graphics.PreferredBackBufferWidth = size.Width;
        graphics.PreferredBackBufferHeight = size.Height;
        graphics.ApplyChanges();
    }

    public void Apply()
    {
        graphics.HardwareModeSwitch = false;
        graphics.IsFullScreen = !offscreen && settings.Fullscreen;
        graphics.PreferredBackBufferWidth = tile?.Width ?? Renderer.Width;
        graphics.PreferredBackBufferHeight = tile?.Height ?? Renderer.Height;
        graphics.SynchronizeWithVerticalRetrace = !offscreen && settings.VSync;
        graphics.ApplyChanges();
        MatchDesktopBackBuffer();
        PositionWindow();
    }
}

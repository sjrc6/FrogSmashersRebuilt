using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

internal sealed class DisplaySettings
{
    private readonly GameWindow window;
    private readonly GraphicsDeviceManager graphics;
    private readonly ClientSettings settings;
    private readonly bool offscreen;

    public DisplaySettings(GameWindow window, GraphicsDeviceManager graphics, ClientSettings settings, bool offscreen)
    {
        this.window = window;
        this.graphics = graphics;
        this.settings = settings;
        this.offscreen = offscreen;
    }

    public void RememberWindowSize()
    {
        if (graphics.IsFullScreen)
        {
            return;
        }

        var size = window.ClientBounds;
        if (size.Width > 0 && size.Height > 0)
        {
            settings.Width = size.Width;
            settings.Height = size.Height;
        }
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
        RememberWindowSize();
        graphics.HardwareModeSwitch = false;
        graphics.IsFullScreen = !offscreen && settings.Fullscreen;
        graphics.PreferredBackBufferWidth = settings.Width;
        graphics.PreferredBackBufferHeight = settings.Height;
        graphics.SynchronizeWithVerticalRetrace = !offscreen && settings.VSync;
        graphics.ApplyChanges();
        MatchDesktopBackBuffer();
    }
}

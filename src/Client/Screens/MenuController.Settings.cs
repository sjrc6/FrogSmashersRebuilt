namespace FrogSmashers.Client;

internal sealed partial class MenuController
{
    private IReadOnlyList<MenuEntry> PersonalRows()
    {
        var personal = new List<MenuEntry>
        {
            new(
                "volume",
                "VOLUME",
                RepeatAdjust: true,
                Value: (int)MathF.Round(game.Settings.Volume * 100) + "%",
                ValueSample: "100%",
                Change: amount =>
                {
                    game.Settings.Volume =
                        Math.Clamp((int)MathF.Round(game.Settings.Volume * 20) + amount, 0, 20) / 20f;
                    game.Audio.Volume = game.Settings.Volume;
                }
            ),
        };
        if (ShowingMenuBackground)
            personal.Add(
                new(
                    "title-volume",
                    "TITLE VOLUME",
                    RepeatAdjust: true,
                    Value: (int)MathF.Round(game.Settings.TitleVolume * 100) + "%",
                    ValueSample: "100%",
                    Change: amount =>
                    {
                        game.Settings.TitleVolume =
                            Math.Clamp((int)MathF.Round(game.Settings.TitleVolume * 20) + amount, 0, 20) / 20f;
                        game.Audio.TitleVolume = game.Settings.TitleVolume;
                    }
                )
            );
        personal.Add(Link("GRAPHICS", GameScreen.Graphics));
        personal.Add(new("controls", "CONTROLS", () => OpenBindings(HintDevice)));
        personal.Add(Link("ROLLBACK", GameScreen.Rollback));
        return personal;
    }

    private IReadOnlyList<MenuEntry> GraphicsRows() =>
        [
            new(
                "fullscreen",
                "FULLSCREEN",
                Value: OnOff(game.Settings.Fullscreen),
                ValueSample: "OFF",
                Change: _ =>
                {
                    game.Settings.Fullscreen = !game.Settings.Fullscreen;
                    game.ApplyDisplay();
                }
            ),
            new(
                "vsync",
                "VSYNC",
                Value: OnOff(game.Settings.VSync),
                ValueSample: "OFF",
                Change: _ =>
                {
                    game.Settings.VSync = !game.Settings.VSync;
                    game.ApplyDisplay();
                }
            ),
            new(
                "fps-limit",
                "FPS LIMIT",
                RepeatAdjust: true,
                Value: game.Settings.FrameLimit.ToString(),
                ValueSample: "1000",
                DisabledReason: game.Settings.VSync ? "VSYNC ENABLED" : null,
                Change: amount =>
                    game.Settings.FrameLimit = FrameRates[
                        Wrap(Array.IndexOf(FrameRates, game.Settings.FrameLimit) + amount, FrameRates.Length)
                    ]
            ),
            new(
                "screen-shake",
                "SCREEN SHAKE",
                Value: OnOff(game.Settings.ScreenShake),
                ValueSample: "OFF",
                Change: _ =>
                {
                    game.Settings.ScreenShake = !game.Settings.ScreenShake;
                    game.Renderer.ShakeEnabled = game.Cinematics.ShakeEnabled = game.Settings.ScreenShake;
                }
            ),
        ];
}

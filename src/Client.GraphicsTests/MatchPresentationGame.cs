using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed class MatchPresentationGame : FrogGame
{
    private bool complete;
    public Exception? Failure { get; private set; }
    protected override bool LimitFrameRate => false;
    protected override bool SaveSettingsOnExit => false;

    public MatchPresentationGame(string contentDirectory)
        : base(OptionsForTest())
    {
        Content.RootDirectory = contentDirectory;
        FirewallCheck = Task.FromResult<bool?>(null);
        Controls.KeyboardSource = () => default;
        Controls.GamePadSource = _ => default;
        Controls.MouseSource = () => default;
    }

    private static LaunchOptions OptionsForTest()
    {
        var options = LaunchOptions.Parse(["--no-intro", "--no-audio"]);
        options.Offscreen = true;
        return options;
    }

    protected override void Update(GameTime gameTime) { }

    protected override void Draw(GameTime gameTime)
    {
        if (complete)
            return;
        complete = true;
        try
        {
            VerifyBunkerUpdate(gameTime);
            Match.StartLocal(new(new GameRules(mapOrder: [5]), 1), [new(0, 0), new(1, 1)]);
            Menus.ShowPlaying();
            var world = Match.World!;
            world.Match.WinRound(world.Rules, 0);
            world.Match.Phase = MatchPhase.RoundScores;
            world.Match.PhaseTicks = 0;
            Renderer.Update(0);
            base.Draw(gameTime);
            var scores = Pixels();
            world.Match.Phase = MatchPhase.MatchFinished;
            base.Draw(gameTime);
            Check(scores.SequenceEqual(Pixels()), "The final score screen stays visible while confirmation is pending");

            Controls.KeyboardSource = () => new(Keys.Tab);
            Controls.Poll();
            Check(!Connections.Visible, "Local matches do not show connections for Tab");
            Controls.KeyboardSource = () => default;
            Controls.GamePadSource = pad => pad == 0 ? new(Vector2.Zero, Vector2.Zero, 0, 0, Buttons.Back) : default;
            Controls.Poll();
            Check(!Connections.Visible, "Local matches do not show connections for Select");
            Menus.Update(0);
            Check(Menus.Screen == GameScreen.Outro && Cinematics.Active, "A confirmed match enters the outro");
            base.Draw(gameTime);
            Check(!scores.SequenceEqual(Pixels()), "The first outro frame replaces the score screen");
            Controls.GamePadSource = _ => default;
            Controls.KeyboardSource = () => new(Keys.Escape);
            Controls.Poll();
            Menus.Update(0);
            Check(Menus.Screen == GameScreen.Seats, "Escape remains available during the outro");
            Controls.KeyboardSource = () => new(Keys.Tab);
            Controls.Poll();
            Check(!Connections.Visible, "A local lobby does not show connections after returning from a match");
            Console.WriteLine(
                "Bunker game update, local pause, score screen, match transition, outro input and local connection overlay checks passed"
            );
        }
        catch (Exception error)
        {
            Failure = error;
            Console.Error.WriteLine(error);
        }
        Exit();
    }

    private Color[] Pixels()
    {
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[Renderer.Frame.Width * Renderer.Frame.Height];
        Renderer.Frame.GetData(pixels);
        return pixels;
    }

    private void VerifyBunkerUpdate(GameTime gameTime)
    {
        int explosions = 0;
        void Explode() => explosions++;
        Renderer.BackgroundExplosion += Explode;
        try
        {
            int map = Assets.Data.Maps.FindIndex(map => map.Id == "7Showdown");
            Match.StartLocal(new(new GameRules(mapOrder: [map]), 1), [new(0, 0), new(1, 1)]);
            Menus.ShowPlaying();
            var step = new GameTime(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(.02));
            base.Update(step);
            base.Draw(gameTime);
            Check(explosions == 1, "The game update runs the bunker presentation");
            Match.Paused = true;
            base.Update(step);
            base.Draw(gameTime);
            var paused = Pixels();
            for (int i = 0; i < 360; i++)
                base.Update(step);
            base.Draw(gameTime);
            Check(
                explosions == 1 && paused.SequenceEqual(Pixels()),
                "A paused local match freezes bunker audio and visuals through FrogGame"
            );
            Match.Paused = false;
            Match.World!.Match.Phase = MatchPhase.RoundScores;
            Match.World.Match.PhaseTicks = World.TickRate * 10;
            for (int i = 0; i < 360; i++)
                base.Update(step);
            Check(explosions == 1, "The actual score screen update does not keep triggering bunker explosions");
        }
        finally
        {
            Renderer.BackgroundExplosion -= Explode;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

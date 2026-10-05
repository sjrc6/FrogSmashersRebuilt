using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            string? captureDirectory = null;
            string? cinematicDirectory = null;
            string contentDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--capture":
                        captureDirectory = args[++index];
                        break;
                    case "--capture-cinematics":
                        cinematicDirectory = args[++index];
                        break;
                    case "--content":
                        contentDirectory = args[++index];
                        break;
                    default:
                        throw new ArgumentException("Unknown option: " + args[index]);
                }
            }

            using var game = new GraphicsTestGame(contentDirectory, captureDirectory, cinematicDirectory);
            game.Run();
            return game.Failure == null ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}

internal sealed class GraphicsTestGame : Game
{
    private readonly string contentDirectory;
    private readonly string? captureDirectory;
    private readonly string? cinematicDirectory;
    private readonly GraphicsDeviceManager graphics;
    private Assets assets = null!;
    private Renderer renderer = null!;
    private bool complete;
    public Exception? Failure { get; private set; }

    public GraphicsTestGame(string contentDirectory, string? captureDirectory, string? cinematicDirectory)
    {
        this.contentDirectory = contentDirectory;
        this.captureDirectory = captureDirectory;
        this.cinematicDirectory = cinematicDirectory;
        graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Renderer.Width,
            PreferredBackBufferHeight = Renderer.Height,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false,
        };
        IsFixedTimeStep = false;
        Window.Title = "Frog Smashers graphics checks";
    }

    protected override void LoadContent()
    {
        assets = new Assets(Content, contentDirectory);
        renderer = new Renderer(GraphicsDevice, assets);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (complete)
        {
            return;
        }

        complete = true;
        try
        {
            var suite = new PresentationChecks(renderer);
            if (cinematicDirectory != null)
            {
                suite.CaptureCinematics(cinematicDirectory);
                Console.WriteLine("Captured cinematic playback samples: " + cinematicDirectory);
                GraphicsDevice.SetRenderTarget(null);
                Exit();
                return;
            }

            string[] checks =
            [
                .. suite.VerifySourcePresentation(),
                .. suite.VerifyContent(),
                .. suite.VerifyMenuLayout(),
                .. suite.VerifyBeachBall(captureDirectory),
                .. suite.VerifyMenuPanels(
                    captureDirectory == null ? null : Path.Combine(captureDirectory, "menu-panels")
                ),
                .. suite.VerifySmokeRings(
                    captureDirectory == null ? null : Path.Combine(captureDirectory, "smoke-rings")
                ),
                .. suite.VerifySpawnEffects(
                    captureDirectory == null ? null : Path.Combine(captureDirectory, "spawn-effects")
                ),
            ];
            if (captureDirectory != null)
            {
                suite.CaptureNativeReferenceSamples(captureDirectory);
                suite.CaptureScorePresentation(Path.Combine(captureDirectory, "scores", "720p"));
                graphics.PreferredBackBufferWidth = 1920;
                graphics.PreferredBackBufferHeight = 1080;
                graphics.ApplyChanges();
                suite.CaptureScorePresentation(Path.Combine(captureDirectory, "scores", "1080p"));
                suite.VerifyBitmapFonts(Path.Combine(captureDirectory, "fonts"));
            }

            Console.WriteLine(JsonSerializer.Serialize(new { PresentationChecks = checks.Length, Checks = checks }));
        }
        catch (Exception error)
        {
            Failure = error;
            Console.Error.WriteLine(error);
        }

        GraphicsDevice.SetRenderTarget(null);
        Exit();
    }

    protected override void UnloadContent()
    {
        renderer?.Dispose();
        assets?.Dispose();
    }
}

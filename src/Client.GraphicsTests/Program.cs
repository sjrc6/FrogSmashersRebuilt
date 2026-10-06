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
            string contentDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
            for (int index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--capture":
                        captureDirectory = args[++index];
                        break;
                    case "--content":
                        contentDirectory = args[++index];
                        break;
                    default:
                        throw new ArgumentException("Unknown option: " + args[index]);
                }
            }

            using var game = new GraphicsTestGame(contentDirectory, captureDirectory);
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
    private readonly GraphicsDeviceManager graphics;
    private Assets assets = null!;
    private Renderer renderer = null!;
    private bool complete;
    public Exception? Failure { get; private set; }

    public GraphicsTestGame(string contentDirectory, string? captureDirectory)
    {
        this.contentDirectory = contentDirectory;
        this.captureDirectory = captureDirectory;
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
            string[] checks = [.. suite.VerifyContent(captureDirectory), .. suite.VerifyMenuLayout()];

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

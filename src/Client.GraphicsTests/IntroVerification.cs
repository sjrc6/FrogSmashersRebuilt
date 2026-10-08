using Microsoft.Xna.Framework;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyIntro(string? captureDirectory)
    {
        using var cinematic = new CinematicPlayer(device, assets);
        cinematic.StartIntro();
        cinematic.Update(2);
        float startupOpacity = assets.SpriteEffect.Parameters["SpriteOpacity"].GetValueSingle();
        var startup = Capture("intro-startup");
        assets.SpriteEffect.Parameters["SpriteOpacity"].SetValue(1f);
        var reference = Capture("intro-reference");
        Check(
            reference.Count(pixel => pixel.R > 16 || pixel.G > 16 || pixel.B > 16) > 1000,
            "Intro contains visible artwork"
        );
        Check(
            startup.SequenceEqual(reference),
            $"Cold-start intro must render at full opacity (loaded shader opacity: {startupOpacity})"
        );

        assets.SpriteEffect.Parameters["SpriteOpacity"].SetValue(.2f);
        Check(Capture("intro-after-dim").SequenceEqual(reference), "Cinematics cannot inherit stage light opacity");
        cinematic.Update(8);
        Check(!Capture("intro-10s").SequenceEqual(reference), "The launch intro advances its animation");
        cinematic.SkipIntro();
        Check(cinematic.TitleReady, "Skipping the intro reaches the title");
        var title = Capture("intro-title");
        Check(
            title.Count(pixel => pixel.R > 16 || pixel.G > 16 || pixel.B > 16) > 1000,
            "The title remains visible after skipping"
        );
        return ["Cold-start intro, shared shader state, animation and title skip checks passed"];

        Color[] Capture(string name)
        {
            var texture = cinematic.Draw();
            device.SetRenderTarget(null);
            var pixels = new Color[texture.Width * texture.Height];
            texture.GetData(pixels);
            if (captureDirectory != null)
            {
                Directory.CreateDirectory(captureDirectory);
                using var file = File.Create(Path.Combine(captureDirectory, name + ".png"));
                texture.SaveAsPng(file, texture.Width, texture.Height);
            }
            return pixels;
        }

        static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}

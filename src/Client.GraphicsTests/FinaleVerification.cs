using System.Reflection;
using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyFinale(string? captureDirectory)
    {
        void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        int map = assets.Data.Maps.FindIndex(map => map.Id == "6Finale");
        Check(map >= 0, "Finale is available");
        var world = new World(assets.Data, new GameRules(mapOrder: [map]));
        Check(!world.Fly.Active, "Finale starts without a simulated fly");
        Check(
            world.Map.Sprites.All(sprite => !assets.Data.Effects["Fly"].Frames.Contains(sprite.SpriteId)),
            "Finale contains no decorative copy of the gameplay fly"
        );
        VerifyEscapeShips(captureDirectory, Check);

        renderer.Reset();
        renderer.DrawWorld(world, null, 1);
        bool foundFrogs = false;
        for (int frame = 0; frame < 600; frame++)
        {
            renderer.Update(.1f);
            var frogs = renderer
                .Effects.Active.Where(effect => world.Map.ParallaxFrogEffects.Contains(effect.Name))
                .ToArray();
            if (frogs.Length == 0)
                continue;
            Check(
                frogs.All(frog => frog.Color == new Color(170, 176, 88)),
                "Background frogs use the original muted palette"
            );
            Check(
                frogs.All(frog => frog.FrameDelay == .1f && frog.Queue == 3000),
                "Background frogs preserve the source animation and draw order"
            );
            foundFrogs = true;
            renderer.DrawWorld(world, null, 1);
            if (captureDirectory != null)
                SaveFrame(Path.Combine(captureDirectory, "finale-background-frogs.png"));
            break;
        }
        Check(foundFrogs, "Finale escape ships release background frogs");

        using var audio = new Audio(assets, true);
        Check(audio.Enabled, "Audio backend is available for Finale checks");
        var ambient =
            (List<(SoundEffectInstance Instance, float Volume, bool Music)>)
                typeof(Audio).GetField("ambient", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(audio)!;
        audio.UpdateFlights(world, .01f);
        var music = ambient.Single(sound => sound.Music).Instance;
        var environment = ambient.Single(sound => !sound.Music).Instance;
        Check(
            music.State == SoundState.Playing && environment.State == SoundState.Playing,
            "Finale starts music and ambience"
        );
        world.Match.WinRound(world.Rules, 0);
        audio.UpdateFlights(world, .01f);
        Check(music.IsDisposed && ambient.All(sound => !sound.Music), "Winning stops Finale music during celebration");
        Check(environment.State == SoundState.Playing, "Winning leaves background ambience uninterrupted");
        world.Match.Phase = MatchPhase.Playing;
        audio.UpdateFlights(world, .01f);
        Check(
            ambient.Single(sound => sound.Music).Instance.State == SoundState.Playing,
            "Rolling back a predicted win restores music"
        );
        world.Match.Phase = MatchPhase.RoundScores;
        audio.UpdateFlights(world, .01f);
        Check(ambient.Count == 0, "The score screen stops arena audio");
        world.Match.Phase = MatchPhase.MatchFinished;
        audio.UpdateFlights(world, .01f);
        Check(ambient.Count == 0, "Waiting for final confirmation cannot restart arena audio");
        renderer.Reset();
        return ["Finale fly, background frogs, victory audio and rollback checks passed"];
    }

    private void VerifyEscapeShips(string? captureDirectory, Action<bool, string> check)
    {
        string Sprite(string name) =>
            assets.Data.Sprites.Single(pair => pair.Value.Path.EndsWith("/" + name, StringComparison.Ordinal)).Key;
        bool shake = renderer.ShakeEnabled;
        renderer.ShakeEnabled = false;
        try
        {
            string[] ships = ["BackgroundEscapeShip", "BackgroundEscapeShip (1)", "BackgroundEscapeShip (2)"];
            for (int variant = 0; variant < ships.Length; variant++)
            {
                var spawner = new ParallaxData
                {
                    Effects = [ships[variant]],
                    Probability = 1,
                    DontScale = true,
                };
                var map = new MapData
                {
                    Id = "ship",
                    OrthoSize = 18,
                    Parallax = [spawner],
                };
                var world = new World(map, new GameRules());
                renderer.Reset();
                renderer.DrawWorld(world, null, 1, showGameplayUi: false);
                renderer.Update(.001f);
                spawner.Probability = 0;
                renderer.Update(.06f);
                renderer.Update(.06f);
                renderer.DrawWorld(world, null, 1, showGameplayUi: false);
                var actual = ReadFrame();
                if (captureDirectory != null)
                    SaveFrame(Path.Combine(captureDirectory, $"finale-ship-{variant}.png"));

                var reference = new MapData
                {
                    OrthoSize = 18,
                    Sprites = [new() { SpriteId = Sprite("bgship") }, new() { SpriteId = Sprite("bgflame_03") }],
                };
                if (variant != 1)
                    reference.Sprites.Add(new() { SpriteId = Sprite("bg_frog_01") });
                if (variant != 0)
                    reference.Sprites.Add(new() { SpriteId = Sprite("bg_frog_01"), ScaleX = -1 });
                renderer.DrawBackdrop(reference, 0);
                if (captureDirectory != null)
                    SaveFrame(Path.Combine(captureDirectory, $"finale-ship-{variant}-reference.png"));
                check(
                    actual.SequenceEqual(ReadFrame()),
                    "Escape ships preserve their hull, independently animated flames and mirrored frog layers"
                );
            }
        }
        finally
        {
            renderer.ShakeEnabled = shake;
            renderer.Reset();
        }
    }

    private Color[] ReadFrame()
    {
        device.SetRenderTarget(null);
        var pixels = new Color[renderer.Frame.Width * renderer.Frame.Height];
        renderer.Frame.GetData(pixels);
        return pixels;
    }
}

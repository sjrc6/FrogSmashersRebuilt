using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifyColliders(string? captureDirectory)
    {
        var map = new MapData
        {
            Id = "collider-check",
            OrthoSize = 18,
            Spawns = [new() { X = -15 }, new() { X = 5 }],
        };
        var world = new World(map, new GameRules(lobby: true));
        var tongue = world.Players[0];
        tongue.Alive = true;
        tongue.X = -15;
        tongue.Mode = CharacterMode.Tongue;
        tongue.TonguePhase = TonguePhase.Extending;
        tongue.TongueX = 1;
        tongue.TongueDistance = 6;
        var bat = world.Players[1];
        bat.Alive = true;
        bat.X = 5;
        bat.Mode = CharacterMode.Attacking;
        bat.AttackPhase = AttackPhase.Swing;
        bat.AttackX = 1;
        bat.AttackCharge = 2;
        world.BeachBall.Active = true;
        world.BeachBall.X = 15;
        world.BeachBall.Y = -5;
        world.Fly.Active = true;
        world.Fly.X = -5;
        world.Fly.Y = 7;
        ulong state = world.HashState();
        bool shake = renderer.ShakeEnabled;
        bool colliders = renderer.ShowColliders;
        renderer.Reset();
        renderer.ShakeEnabled = false;
        try
        {
            renderer.ShowColliders = false;
            renderer.Update(0);
            renderer.DrawWorld(world, null, 1, showGameplayUi: false);
            var normal = ReadFrame();
            renderer.ShowColliders = true;
            renderer.DrawWorld(world, null, 1, showGameplayUi: false);
            var overlay = ReadFrame();
            foreach (
                var color in new[]
                {
                    Color.Yellow,
                    Color.Magenta,
                    Color.Violet,
                    Color.Red,
                    Color.Orange,
                    Color.White,
                    Color.DeepSkyBlue,
                }
            )
            {
                int changed = Enumerable.Range(0, overlay.Length).Count(i => overlay[i] == color && normal[i] != color);
                if (changed < 10)
                    throw new InvalidOperationException("Missing collision outline: " + color);
            }
            if (captureDirectory != null)
                SaveFrame(Path.Combine(captureDirectory, "object-colliders.png"));
            bat.AttackX = 0;
            bat.AttackY = -1;
            renderer.DrawWorld(world, null, 1, showGameplayUi: false);
            if (captureDirectory != null)
                SaveFrame(Path.Combine(captureDirectory, "downward-bat-collider.png"));
            bat.AttackX = 1;
            bat.AttackY = 0;
            renderer.ShowColliders = false;
            renderer.DrawWorld(world, null, 1, showGameplayUi: false);
            if (!normal.SequenceEqual(ReadFrame()) || state != world.HashState())
                throw new InvalidOperationException("Collision overlay changes normal rendering or simulation state");
        }
        finally
        {
            renderer.ShowColliders = colliders;
            renderer.ShakeEnabled = shake;
            renderer.Reset();
        }
        return ["Frog, tongue, bat, beach ball and fly collision outlines render without changing simulation"];
    }
}

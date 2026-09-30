using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client.GraphicsTests;

internal sealed partial class PresentationChecks
{
    public string[] VerifySpawnEffects(string? directory = null)
    {
        var checks = new List<string>();
        void Check(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            checks.Add(name);
        }

        var puff = assets.Data.Effects["SpawnPuff"];
        var sprite = assets.Data.Sprites[puff.InitialSpriteId];
        var texture = assets.Texture(sprite.Path);
        var source = new Color[texture.Width * texture.Height];
        texture.GetData(source);
        int inkBottom =
            Enumerable
                .Range(sprite.RectY, sprite.RectHeight)
                .Last(y =>
                    Enumerable.Range(sprite.RectX, sprite.RectWidth).Any(x => source[y * texture.Width + x].A > 0)
                ) + 1;
        float baseOffset = (sprite.RectY + sprite.RectHeight * (1 - sprite.PivotY) - inkBottom) / sprite.PixelsPerUnit;

        SimulationEvent Spawn(PointData point) =>
            new(0, 0, SimulationEventKind.Spawn, 0, -1, Fixed.FromDecimal(point.X), Fixed.FromDecimal(point.Y), 0);

        var lobby = assets.Data.PresentationScenes["Lobby"];
        foreach (var map in assets.Data.Maps.Append(lobby))
            for (int room = 0; room < map.Spawns.Count; room++)
            {
                var point = map.Spawns[room];
                var world = new World([map], new GameRules { PlayerCount = 2 }, 1, assets.Data.CharacterParameters);
                world.Players[1].Eliminated = true;
                var player = world.Players[0];
                player.Alive = true;
                player.X = Fixed.FromDecimal(point.X);
                player.Y = Fixed.FromDecimal(point.Y);
                for (int tick = 0; tick < 120 && !player.OnGround; tick++)
                    world.Tick(new InputFrame[2]);
                Check(player.OnGround, $"{map.Id} spawn {room}: simulation reaches supporting platform");

                renderer.Reset();
                renderer.Consume([Spawn(point)], world);
                var effect = renderer.Effects.Active.Single(e => e.Name == "SpawnPuff");
                var position = effect.Position;
                Check(
                    Math.Abs(position.Y + baseOffset - player.Y.ToFloat()) < .0001f
                        && Math.Abs(position.X - (float)point.X) < .0001f,
                    $"{map.Id} spawn {room}: puff base meets the frog's landing surface"
                );
                if (map == lobby)
                {
                    renderer.Reset();
                    renderer.LobbyColorEffect(map, room, room, 0);
                    Check(
                        Vector2.Distance(renderer.Effects.Active.Single().Position, position) < .0001f,
                        $"lobby room {room}: color change and spawn use the same puff origin"
                    );
                }
            }

        var lobbyWorld = new World(lobby, new GameRules { Lobby = true, PlayerCount = 8 });
        foreach (int height in new[] { 720, 1080 })
        {
            renderer.Reset();
            renderer.Update(0);
            lobbyWorld.SetLobbySlot(0, false, 0);
            lobbyWorld.SetLobbySlot(0, true, 0);
            lobbyWorld.Tick(new InputFrame[8]);
            renderer.Consume(lobbyWorld.Events, lobbyWorld);
            var effect = renderer.Effects.Active.Single(e => e.Name == "SpawnPuff");
            var origin = effect.Position;
            using var target = new RenderTarget2D(
                device,
                height * 16 / 9,
                height,
                false,
                SurfaceFormat.Color,
                DepthFormat.None,
                4,
                RenderTargetUsage.DiscardContents
            );
            var pixels = new Color[target.Width * target.Height];
            int floor = (int)
                MathF.Round(renderer.Canvas.Screen(new(origin.X, (float)lobby.Spawns[0].Y)).Y * height / 720f);
            for (int frame = 0; frame < 7; frame++)
            {
                if (frame > 0)
                    renderer.Effects.Update(.061f, frame * .061f);
                device.SetRenderTarget(target);
                device.Clear(Color.Transparent);
                Begin(renderer.Effects.ModeFor(puff));
                renderer.Canvas.DrawSprite(
                    effect.Sprite,
                    effect.Position,
                    effect.Color,
                    effect.CurrentScale,
                    effect.Rotation
                );
                End();
                device.SetRenderTarget(null);
                target.GetData(pixels);
                int bottom = Enumerable.Range(0, pixels.Length).Last(index => pixels[index].A > 0) / target.Width + 1;
                Check(
                    effect.Position == origin && (frame < 3 ? bottom == floor : bottom < floor),
                    $"spawn animation {height}p frame {frame}: {(frame < 3 ? "base touches platform" : "smoke rises from fixed origin")}"
                );
                if (directory != null)
                {
                    Directory.CreateDirectory(directory);
                    using var stream = File.Create(Path.Combine(directory, $"puff-{height}p-{frame}.png"));
                    target.SaveAsPng(stream, target.Width, target.Height);
                }
            }
            renderer.Effects.Update(.061f, .427f);
            Check(!renderer.Effects.Active.Any(), $"spawn animation {height}p ends after its final frame");

            renderer.Reset();
            renderer.Consume([Spawn(lobby.Spawns[0])], lobbyWorld, .08f);
            effect = renderer.Effects.Active.Single();
            Check(
                effect.Position == origin && effect.Sprite == puff.Frames[0],
                $"spawn animation {height}p: delayed event keeps ground alignment and animation age"
            );
        }
        renderer.Reset();
        return checks.ToArray();
    }
}

using FrogSmashers.Core;
using Microsoft.Xna.Framework;

namespace FrogSmashers.Client;

public sealed partial class Renderer
{
    private readonly CharacterPresentation[] characters = Enumerable
        .Range(0, 8)
        .Select(_ => new CharacterPresentation())
        .ToArray();
    private readonly Vector2[] characterPositions = new Vector2[8];
    private float lastPresentedTime = -1;
    private BeachBallPresentation ballPose = new();
    private Vector2 ballPosition;

    private enum DrawKind
    {
        MapSprite,
        LobbyBackground,
        LobbyPreview,
        Parallax,
        Shimmer,
        Smoke,
        Effect,
        Tongue,
        Character,
        OverheadScore,
        OffscreenMarker,
        Fly,
        BeachBall,
        ScoreText,
        ScoreIcons,
    }

    private readonly record struct DrawCommand(
        int Layer,
        int Order,
        int Queue,
        float Z,
        int Sequence,
        DrawKind Kind,
        object? Item = null,
        int Player = -1
    );

    private readonly List<DrawCommand> drawCommands = new();

    private int QueueFor(string material) =>
        assets.Data.Materials.TryGetValue(material, out var data) && data.RenderQueue >= 0 ? data.RenderQueue : 3000;

    private void DrawGameplayScene(World world, World? previous, float alpha, float dt, bool showGameplayUi)
    {
        compositor.BeginScene(EffectSystem.ToColor(world.Map.BackgroundColor));
        drawCommands.Clear();
        void AddCommand(
            int layer,
            int order,
            string material,
            float z,
            DrawKind kind,
            object? item = null,
            int player = -1
        ) => drawCommands.Add(new(layer, order, QueueFor(material), z, drawCommands.Count, kind, item, player));
        foreach (var sprite in world.Map.Sprites.Where(s => s.Active && s.Visible))
        {
            if (world.Rules.Lobby)
            {
                if (sprite.SpriteId == "lobby-background")
                    AddCommand(0, 0, "", sprite.Z, DrawKind.LobbyBackground);
            }
            else
                AddCommand(sprite.SortingLayer, sprite.Order, sprite.Material, sprite.Z, DrawKind.MapSprite, sprite);
        }

        foreach (var piece in parallax)
        {
            var data = assets.Data.Effects[piece.Name];
            if (data.Frames.Length == 0)
            {
                continue;
            }

            AddCommand(data.SortingLayer, data.Order, data.Material, piece.Z, DrawKind.Parallax, piece);
        }

        foreach (var shimmer in world.Map.SceneEffects.Where(s => s.Active))
        {
            AddCommand(0, 0, shimmer.Material, shimmer.Z, DrawKind.Shimmer, shimmer);
        }

        foreach (var emitter in world.Map.ParticleEmitters)
        {
            AddCommand(0, emitter.Order, "", emitter.Z, DrawKind.Smoke, emitter);
        }

        foreach (var effect in effects.Active.Where(e => !e.Dead))
        {
            var data = effect.Owner >= 0 ? assets.Data.Effects["FaderTrail"] : effect.Data!;
            drawCommands.Add(
                new(
                    data.SortingLayer,
                    data.Order,
                    effect.Queue >= 0 ? effect.Queue : QueueFor(data.Material),
                    effect.Z,
                    drawCommands.Count,
                    DrawKind.Effect,
                    effect
                )
            );
        }

        for (int i = 0; i < world.Players.Length; i++)
        {
            int slot = i;
            var p = world.Players[i];
            if (world.Rules.Lobby && lobbyPreviews[i] != null)
            {
                AddCommand(0, 0, "SelectiveColorReplace", .1f, DrawKind.LobbyPreview, player: i);
                if (!lobbyPreviews[i]!.Spawned)
                    continue;
            }
            if (!p.Alive)
                continue;

            var pose = characters[i];
            var pos = characterPositions[i];

            if (dt > 0 && pose.TongueVisible)
            {
                pose.TipClock.Step(dt, assets.Data.TongueTip.FrameSeconds, assets.Data.TongueTip.Frames.Length, false);
            }

            if (p.Mode != CharacterMode.Tongue || p.TonguePhase == TonguePhase.Stunned)
            {
                pose.TongueVisible = false;
            }
            else if (p.TonguePhase != TonguePhase.Burping)
            {
                pose.TongueVisible = true;
                var tip = new Vector2(p.TongueTip.X.ToFloat(), p.TongueTip.Y.ToFloat());
                if (
                    previous != null
                    && previous.Map.Id == world.Map.Id
                    && previous.Players[i].Alive
                    && previous.Players[i].Mode == CharacterMode.Tongue
                )
                {
                    var before = previous.Players[i];
                    tip = Vector2.Lerp(new(before.TongueTip.X.ToFloat(), before.TongueTip.Y.ToFloat()), tip, alpha);
                }

                pose.TongueOffset = tip - pos;
            }

            if (pose.TongueVisible)
            {
                AddCommand(0, 0, assets.Data.TongueLineMaterial, 1.1f, DrawKind.Tongue, player: slot);
            }

            AddCommand(0, 0, "SelectiveColorReplace", 0, DrawKind.Character, player: slot);
            var message = scores.OverheadMessage(slot);
            if (showGameplayUi && message != null)
            {
                AddCommand(0, 0, "", 0, DrawKind.OverheadScore, message, slot);
            }

            if (
                showGameplayUi
                && pos.Y > world.Map.ScreenTop
                && assets.Data.Effects.TryGetValue("Hexagon", out var dot)
            )
            {
                AddCommand(dot.SortingLayer, dot.Order, dot.Material, -6, DrawKind.OffscreenMarker, dot, slot);
            }
        }

        if (world.Fly.Active)
        {
            if (!flyWasActive)
            {
                flyClock = new();
            }

            var fly = assets.Data.Effects["Fly"];
            if (dt > 0)
            {
                flyClock.Step(dt, fly.FrameSeconds, fly.Frames.Length, false);
            }

            AddCommand(fly.SortingLayer, fly.Order, fly.Material, fly.Z, DrawKind.Fly, fly);
        }

        if (world.Rules.Lobby && world.BeachBall.Active)
        {
            AddCommand(0, 0, "SelectiveColorReplace", 0, DrawKind.BeachBall, ballPosition);
        }
        if (showGameplayUi && (!world.Match.IsShowdown || world.Rules.Scoring == ScoringMode.Stocks))
        {
            AddCommand(0, 0, "", 2.78f, DrawKind.ScoreText);
            AddCommand(0, 0, "SelectiveColorReplace", 2.78f, DrawKind.ScoreIcons);
        }

        flyWasActive = world.Fly.Active;
        drawCommands.Sort(CompareDrawCommands);
        foreach (var command in drawCommands)
        {
            DrawCommandItem(command, world);
        }

        if (ShowColliders)
        {
            canvas.Begin(0);
            foreach (var box in world.Map.Collision)
            {
                var a = Screen(new((float)(box.X - box.Width / 2), (float)(box.Y + box.Height / 2)));
                var b = Screen(new((float)(box.X + box.Width / 2), (float)(box.Y - box.Height / 2)));
                canvas.Outline(
                    new((int)a.X, (int)a.Y, (int)(b.X - a.X), (int)(b.Y - a.Y)),
                    box.OneWay ? Color.Cyan : Color.Lime,
                    2
                );
            }

            canvas.End();
        }
    }

    private static int CompareDrawCommands(DrawCommand left, DrawCommand right)
    {
        int result = left.Layer.CompareTo(right.Layer);
        if (result == 0)
            result = left.Order.CompareTo(right.Order);
        if (result == 0)
            result = left.Queue.CompareTo(right.Queue);
        if (result == 0)
            result = right.Z.CompareTo(left.Z);
        return result == 0 ? left.Sequence.CompareTo(right.Sequence) : result;
    }

    private void DrawCommandItem(DrawCommand command, World world)
    {
        switch (command.Kind)
        {
            case DrawKind.LobbyBackground:
                DrawLobbyBackground(world.Map);
                break;
            case DrawKind.LobbyPreview:
                DrawLobbyPreview(world, command.Player);
                break;
            case DrawKind.MapSprite:
                canvas.Begin(0);
                DrawSceneSprite((SceneSpriteData)command.Item!);
                canvas.End();
                break;
            case DrawKind.Parallax:
                DrawParallax((ParallaxPiece)command.Item!);
                break;
            case DrawKind.Shimmer:
                ApplyShimmer((SceneEffectData)command.Item!);
                break;
            case DrawKind.Smoke:
                DrawEmitter((ParticleEmitterData)command.Item!);
                break;
            case DrawKind.Effect:
                DrawEffect((EffectSystem.VisualEffect)command.Item!);
                break;
            case DrawKind.Tongue:
                var position = characterPositions[command.Player];
                var pose = characters[command.Player];
                DrawTongue(position + new Vector2(0, 1.5f), position + pose.TongueOffset, pose);
                break;
            case DrawKind.Character:
                DrawCharacter(world.Players[command.Player]);
                break;
            case DrawKind.OverheadScore:
                DrawOverheadScore(world, command.Player, (ScoreDisplay.ScoreMessage)command.Item!);
                break;
            case DrawKind.OffscreenMarker:
                var marker = (EffectData)command.Item!;
                canvas.Begin(effects.ModeFor(marker));
                canvas.DrawSprite(
                    marker.InitialSpriteId,
                    new(characterPositions[command.Player].X, world.Map.ScreenTop),
                    ColorFor(world, command.Player),
                    new(marker.ScaleX, marker.ScaleY),
                    0
                );
                canvas.End();
                break;
            case DrawKind.Fly:
                DrawFly(world, (EffectData)command.Item!);
                break;
            case DrawKind.BeachBall:
                canvas.Begin(1);
                canvas.DrawSprite(
                    ballPose.Sprite,
                    (Vector2)command.Item!,
                    Color.White,
                    new Vector2(ballPose.Scale),
                    ballPose.Rotation
                );
                canvas.End();
                break;
            case DrawKind.ScoreText:
                scores.Draw(world, false, 0, time, frameSeconds, false, true);
                break;
            case DrawKind.ScoreIcons:
                scores.Draw(world, false, 0, time, frameSeconds, true, false, false);
                break;
        }
    }

    private void DrawParallax(ParallaxPiece piece)
    {
        var data = assets.Data.Effects[piece.Name];
        string sprite =
            piece.Clock.Frame < 0 ? data.InitialSpriteId : data.Frames[piece.Clock.Frame % data.Frames.Length];
        var position = piece.Position + (piece.FollowCamera ? cameraController.Position : Vector2.Zero);
        canvas.Begin(effects.ModeFor(data));
        canvas.DrawSprite(sprite, position, EffectSystem.ToColor(data.Color), piece.Scale, 0);
        canvas.End();
    }

    private void DrawCharacter(PlayerState player)
    {
        var pose = characters[player.Slot];
        var position = characterPositions[player.Slot] + new Vector2(assets.Data.CharacterOffsetX, pose.OffsetY);
        canvas.Begin(1);
        canvas.DrawSprite(pose.Sprite, position, pose.Color, new(player.Facing, 1), pose.Rotation);
        canvas.End();
    }

    private void DrawOverheadScore(World world, int player, ScoreDisplay.ScoreMessage message)
    {
        float scale =
            cameraController.PixelsPerUnit / 20 * EffectAnimation.OverheadScoreScale(message.Life - message.Age);
        var location = Screen(characterPositions[player] + new Vector2(0, 3));
        var color = EffectAnimation.ScoreFlash(time, ColorFor(world, player));
        canvas.BeginFont();
        assets.ScoreFont.DrawCenteredVertical(Batch, message.Overhead ?? message.Text, location, color, scale, true);
        canvas.End();
    }

    private void DrawFly(World world, EffectData fly)
    {
        string sprite = flyClock.Frame < 0 ? fly.InitialSpriteId : fly.Frames[flyClock.Frame % fly.Frames.Length];
        canvas.Begin(effects.ModeFor(fly));
        canvas.DrawSprite(
            sprite,
            new(world.Fly.X.ToFloat(), world.Fly.Y.ToFloat()),
            EffectSystem.ToColor(fly.Color),
            new(fly.ScaleX, fly.ScaleY),
            0
        );
        canvas.End();
    }

    public void DrawWorld(World world, World? previous, float alpha, bool showGameplayUi = true)
    {
        SetMap(world.Map);
        float dt = time == lastPresentedTime ? 0 : frameSeconds;
        lastPresentedTime = time;
        if (dt > 0)
        {
            cameraController.Update(world, dt, time);
            UpdateConfetti(world, dt);
        }

        for (int i = 0; i < world.Players.Length; i++)
        {
            var p = world.Players[i];
            if (!p.Alive)
            {
                continue;
            }

            var pos = new Vector2(p.X.ToFloat(), p.Y.ToFloat());
            if (previous != null && previous.Map.Id == world.Map.Id && previous.Players[i].Alive)
            {
                var old = new Vector2(previous.Players[i].X.ToFloat(), previous.Players[i].Y.ToFloat());
                if (Vector2.DistanceSquared(pos, old) < 400)
                {
                    pos = Vector2.Lerp(old, pos, alpha);
                }
            }

            characterPositions[i] = pos;
            if (dt > 0 || characters[i].Sprite == null)
            {
                characters[i].Update(assets, p, world, dt);
            }

            if (dt > 0 && !characters[i].Transitioning)
            {
                effects.UpdateCharacter(p, world, pos, characters[i], dt);
            }
        }

        if (world.Rules.Lobby && world.BeachBall.Active)
        {
            ballPosition = new(world.BeachBall.X.ToFloat(), world.BeachBall.Y.ToFloat());
            if (previous?.BeachBall.Active == true && previous.Map.Id == world.Map.Id)
            {
                var before = new Vector2(previous.BeachBall.X.ToFloat(), previous.BeachBall.Y.ToFloat());
                ballPosition = Vector2.Lerp(before, ballPosition, alpha);
            }
            if (dt > 0 || ballPose.Sprite == null)
                ballPose.Update(assets, world.BeachBall, dt);
            if (dt > 0)
                effects.UpdateBeachBall(world, ballPosition, ballPose, dt);
        }
        else
        {
            effects.RemoveCharacter(8);
            ballPose = new();
        }
        DrawGameplayScene(world, previous, alpha, dt, showGameplayUi);
        PostProcess();
    }
}

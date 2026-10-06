using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

public sealed partial class Renderer
{
    private readonly CrewRosterLayout crewLayout = new();
    private Rectangle? crewSpriteBounds;

    internal void DrawCrewSelection(World world, IReadOnlyList<MatchPlayerView> players, Controls controls)
    {
        compositor.BeginScene(Color.Black);
        compositor.ComposeScene();
        DrawCrewRoster(world, players, controls, selecting: true);
        BeginUi();
        Text("CHOOSE FIGHTERS", Width / 2, 70, scale: 2, center: true);
        EndUi();
    }

    internal void DrawMatchOverlay(World world, IReadOnlyList<MatchPlayerView> players, Controls controls)
    {
        if (world.Rules.Format == MatchFormat.Crews)
            DrawCrewRoster(world, players, controls, selecting: false);
        if (world.Match.Phase == MatchPhase.RoundFinished)
        {
            int device = players.ElementAtOrDefault(world.Match.Winner)?.Device ?? -1;
            var glyph = device < 0 ? ButtonGlyph.Pad(Buttons.Start) : ButtonGlyph.Menu(device);
            BeginUi();
            DrawMatchHint([glyph], "TO SKIP", new Vector2(Width / 2, Height - 40), prefix: "PRESS");
            EndUi();
        }
    }

    private void DrawCrewRoster(World world, IReadOnlyList<MatchPlayerView> players, Controls controls, bool selecting)
    {
        crewLayout.Update(world, selecting, frameSeconds);
        int leftTeam = world.Rules.Teams.Take(world.Players.Length).Min();
        var sprite = assets.Data.Sprites[assets.Frame("idle", 1)!];
        var source = CrewSpriteBounds(sprite);
        var spriteScale = selecting ? assets.Font.PixelScale(1.4f) : ScoreDisplay.IconPixelScale(sprite, 20);
        var frogSize = new Vector2(source.Width, source.Height) * spriteScale;
        float labelScale = selecting ? 1.4f : 1;
        float nameGap = selecting ? 6 : 4;
        canvas.Begin(1);
        for (int slot = 0; slot < world.Players.Length; slot++)
        {
            var color = PlayerPalette.For(world, slot);
            if (world.Match.Players[slot].Stocks == 0)
                color *= .35f;
            var topLeft = CrewFrogTopLeft(slot, frogSize, spriteScale, selecting);
            Batch.Draw(
                assets.Texture(sprite.Path),
                topLeft,
                source,
                color,
                0,
                Vector2.Zero,
                spriteScale,
                world.Rules.Teams[slot] == leftTeam ? SpriteEffects.None : SpriteEffects.FlipHorizontally,
                0
            );
        }
        canvas.End();
        BeginUi();
        for (int slot = 0; slot < world.Players.Length; slot++)
        {
            var progress = world.Match.Players[slot];
            var view = players.ElementAtOrDefault(slot);
            int team = world.Rules.Teams[slot];
            int selected = world.Match.TeamSelections[team];
            bool active = selected == slot;
            bool left = team == leftTeam;
            var topLeft = CrewFrogTopLeft(slot, frogSize, spriteScale, selecting);
            var position = topLeft + frogSize / 2;
            string lives = progress.Stocks.ToString();
            float livesWidth = assets.ScoreFont.Measure(lives, labelScale).X;
            float livesOffset = frogSize.X / 2 + (selecting ? 9 : 6) + livesWidth / 2;
            bool livesOnRight = selecting ? !left : left;
            float livesX = position.X + (livesOnRight ? livesOffset : -livesOffset);
            DrawCrewLabel(
                lives,
                new Vector2(livesX, position.Y),
                labelScale,
                selecting ? Color.LimeGreen : Color.White
            );
            if (selecting || active)
            {
                string name = view?.Name ?? $"PLAYER {slot + 1}";
                float nameHeight = assets.ScoreFont.Measure(name, labelScale).Y;
                DrawCrewLabel(name, new Vector2(position.X, topLeft.Y - nameGap - nameHeight / 2), labelScale);
            }
            bool canFight =
                selecting && selected < 0 && progress.Participation == Participation.Waiting && view?.Device >= 0;
            if (canFight)
            {
                var fight = CrewGlyphs(controls, view!.Device, InputButtons.Attack);
                float hintOffset = frogSize.X / 2 + 12 + CrewHintWidth(fight, "PRESS") / 2;
                float hintX = position.X + (left ? hintOffset : -hintOffset);
                DrawCrewHint(fight, "PRESS", new Vector2(hintX, position.Y), labelFirst: true);
            }
            if (!selecting || !active)
                continue;
            float readyY = topLeft.Y + frogSize.Y + 16;
            if (progress.Ready)
                assets.Font.DrawCenteredVertical(
                    Batch,
                    "READY",
                    new Vector2(position.X, readyY),
                    Color.LimeGreen,
                    CrewHintScale,
                    center: true
                );
            else if (view?.Device >= 0)
                DrawCrewHint(
                    CrewGlyphs(controls, view.Device, InputButtons.Jump),
                    "READY",
                    new Vector2(position.X, readyY)
                );
            if (view?.Device >= 0 && world.Match.CanBackOut(world.Rules, slot))
                DrawCrewHint(
                    CrewGlyphs(controls, view.Device, InputButtons.Attack),
                    "TO BACKOUT",
                    new Vector2(position.X, readyY + 24)
                );
        }
        EndUi();
    }

    private void DrawCrewLabel(string text, Vector2 position, float scale, Color? color = null) =>
        assets.ScoreFont.DrawCenteredVertical(Batch, text, position, color ?? Color.White, scale, center: true);

    private float CrewHintScale
    {
        get
        {
            float outputScale = canvas.Device.Viewport.Height / (float)Height;
            float pixels = assets.Font.PixelScale(1).Y * outputScale;
            return Math.Max(1, pixels - 1) / pixels;
        }
    }

    private float CrewHintWidth(ButtonGlyph[] glyphs, string label)
    {
        float scale = CrewHintScale;
        float pixelWidth = assets.Font.PixelScale(scale).X;
        return assets.Font.Measure(label, scale).X
            + glyphs.Sum(glyph => (assets.Texture(glyph.Path).Width + 4) * pixelWidth);
    }

    private void DrawCrewHint(ButtonGlyph[] glyphs, string label, Vector2 center, bool labelFirst = false)
    {
        float scale = CrewHintScale;
        var pixels = assets.Font.PixelScale(scale);
        float x = center.X - CrewHintWidth(glyphs, label) / 2;
        if (labelFirst)
        {
            assets.Font.DrawCenteredVertical(Batch, label, new Vector2(x, center.Y), Color.White, scale);
            x += assets.Font.Measure(label, scale).X + 4 * pixels.X;
        }
        foreach (var glyph in glyphs)
        {
            var texture = assets.Texture(glyph.Path);
            var position = new Vector2(
                MathF.Round(x / pixels.X) * pixels.X,
                MathF.Round(center.Y / pixels.Y - texture.Height / 2f) * pixels.Y
            );
            Batch.Draw(texture, position, null, Color.White, 0, Vector2.Zero, pixels, SpriteEffects.None, 0);
            x += (texture.Width + 4) * pixels.X;
        }
        if (!labelFirst)
            assets.Font.DrawCenteredVertical(Batch, label, new Vector2(x, center.Y), Color.White, scale);
    }

    private Vector2 CrewFrogTopLeft(int slot, Vector2 size, Vector2 pixelScale, bool selecting)
    {
        var topLeft = crewLayout.Positions[slot] - size / 2;
        if (selecting)
            topLeft =
                new Vector2(MathF.Round(topLeft.X / pixelScale.X), MathF.Round(topLeft.Y / pixelScale.Y)) * pixelScale;
        return topLeft;
    }

    private Rectangle CrewSpriteBounds(SpriteData sprite)
    {
        if (crewSpriteBounds is { } bounds)
            return bounds;
        var source = new Rectangle(sprite.RectX, sprite.RectY, sprite.RectWidth, sprite.RectHeight);
        var pixels = new Color[source.Width * source.Height];
        assets.Texture(sprite.Path).GetData(0, source, pixels, 0, pixels.Length);
        int left = source.Width,
            top = source.Height,
            right = 0,
            bottom = 0;
        for (int y = 0; y < source.Height; y++)
        for (int x = 0; x < source.Width; x++)
        {
            if (pixels[y * source.Width + x].A == 0)
                continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x + 1);
            bottom = Math.Max(bottom, y + 1);
        }
        crewSpriteBounds = new Rectangle(source.X + left, source.Y + top, right - left, bottom - top);
        return crewSpriteBounds.Value;
    }

    private static ButtonGlyph[] CrewGlyphs(Controls controls, int device, InputButtons action)
    {
        if (device < 2)
        {
            var keys = controls.KeyboardBindings(device);
            return [ButtonGlyph.Key(action == InputButtons.Attack ? keys.Attack : keys.Jump)];
        }
        var pad = controls.ControllerBindings(device - 2);
        return ButtonGlyph.PadBinding(action == InputButtons.Attack ? pad.Attack : pad.Jump);
    }

    private float MatchHintWidth(ButtonGlyph[] glyphs, string label) =>
        glyphs.Sum(glyph => ImageSize(assets.Texture(glyph.Path).Bounds, 40).X + 8) + assets.Font.Measure(label).X;

    private void DrawMatchHint(ButtonGlyph[] glyphs, string label, Vector2 center, string? prefix = null)
    {
        float prefixWidth = prefix == null ? 0 : assets.Font.Measure(prefix).X + 10;
        float x = center.X - (MatchHintWidth(glyphs, label) + prefixWidth) / 2;
        if (prefix != null)
        {
            CenteredText(prefix, x, center.Y);
            x += prefixWidth;
        }
        foreach (var glyph in glyphs)
        {
            var source = assets.Texture(glyph.Path).Bounds;
            var size = ImageSize(source, 40);
            Image(glyph.Path, source, new Vector2(x + size.X / 2, center.Y - size.Y / 2), 40);
            x += size.X + 8;
        }
        CenteredText(label, x, center.Y);
    }
}

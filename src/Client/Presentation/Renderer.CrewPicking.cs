using FrogSmashers.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

public sealed partial class Renderer
{
    internal void DrawCrewPicking(World world, IReadOnlyList<MatchPlayerView> players, Controls controls)
    {
        compositor.BeginScene(Color.Black);
        compositor.ComposeScene();
        crewLayout.UpdateChoices(world, frameSeconds);
        var sprite = assets.Data.Sprites[assets.Frame("idle", 1)!];
        var source = CrewSpriteBounds(sprite);
        var pixels = assets.Font.PixelScale(1.4f);
        var size = new Vector2(source.Width, source.Height) * pixels;
        canvas.Begin(1);
        for (int slot = 0; slot < world.Players.Length; slot++)
            Batch.Draw(
                assets.Texture(sprite.Path),
                CrewFrogTopLeft(slot, size, pixels, selecting: true),
                source,
                PlayerPalette.For(world, slot),
                0,
                Vector2.Zero,
                pixels,
                world.Match.CrewTeams[slot] == 1 ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
                0
            );
        canvas.End();
        BeginUi();
        Text("CREW A", 260, 70, Color.Red, scale: 2, center: true);
        Text("CREW B", Width - 260, 70, Color.DodgerBlue, scale: 2, center: true);
        Text("CHOOSE CREW", Width / 2, Height - 50, scale: 2, center: true);
        for (int slot = 0; slot < world.Players.Length; slot++)
        {
            var view = players.ElementAtOrDefault(slot);
            var topLeft = CrewFrogTopLeft(slot, size, pixels, selecting: true);
            float x = topLeft.X + size.X / 2;
            string name = view?.Name ?? $"PLAYER {slot + 1}";
            float nameHeight = assets.ScoreFont.Measure(name, 1.4f).Y;
            DrawCrewLabel(name, new Vector2(x, topLeft.Y - 6 - nameHeight / 2), 1.4f);
            if (world.Match.CrewTeams[slot] < 0)
                continue;
            var readyPosition = new Vector2(x, topLeft.Y + size.Y + 16);
            if (world.Match.Players[slot].Ready)
                assets.Font.DrawCenteredVertical(
                    Batch,
                    "READY",
                    readyPosition,
                    Color.LimeGreen,
                    CrewHintScale,
                    center: true
                );
            else if (view?.Device >= 0)
                DrawCrewHint(CrewReadyGlyphs(controls, view.Device), "READY", readyPosition);
        }
        EndUi();
    }
}

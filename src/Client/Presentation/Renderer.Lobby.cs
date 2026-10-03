using FrogSmashers.Core;
using FrogSmashers.Network;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FrogSmashers.Client;

public sealed partial class Renderer
{
    private Texture2D? lobbyOutline;
    private Texture2D? lobbyBackground;
    private readonly LobbyPlayer?[] lobbyPreviews = new LobbyPlayer?[8];
    private readonly Color[] lobbyColors = PlayerPalette.Colors.ToArray();
    private int outlinedRoom = -1;

    internal void SetLobbyPreviews(LobbyRoster roster, bool teams = false)
    {
        var players = roster.Slots.Select(slot => slot.Player).ToArray();
        for (int room = 0; room < 8; room++)
        {
            lobbyPreviews[room] = roster.Slots[room].Player is { Cpu: false } player ? player : null;
            lobbyColors[room] = PlayerPalette.Lobby(players, room, teams);
        }
    }

    internal Vector2 LobbyPreviewPosition(MapData map, int room) =>
        new((float)map.Spawns[room].X, (float)map.Spawns[room].Y + assets.Data.CharacterOffsetY);

    internal void LobbyColorEffect(MapData map, int room, Color color, long tick, float age = 0)
    {
        SetMap(map);
        var point = map.Spawns[room];
        SpawnPuff(map, new((float)point.X, (float)point.Y), color, tick, age);
    }

    private void DrawLobbyPreview(World world, int room)
    {
        var player = lobbyPreviews[room]!;
        var color = lobbyColors[room];
        if (player.Spawned)
            color.A = 90;
        canvas.Begin(1);
        canvas.DrawSprite(
            "76e22ac1a032c3349a2537db42725c43:21300000",
            LobbyPreviewPosition(world.Map, room),
            color,
            Vector2.One,
            0
        );
        canvas.End();
    }

    private void DrawLobbyBackground(MapData map)
    {
        if (lobbyBackground == null)
        {
            var original = assets.Texture("Textures/Sprites/BackGrounds/lobby_background_v1");
            var pixels = new Color[LobbyLayout.Width * LobbyLayout.Height];
            original.GetData(pixels);
            for (int i = 0; i < pixels.Length; i++)
                if (pixels[i] != Color.Black)
                    pixels[i] = Color.Lerp(pixels[i], Color.White, 128 / 255f);
            float pixelsPerUnit = LobbyLayout.Height / (map.OrthoSize * 2);
            foreach (var sprite in map.Sprites.Where(sprite => sprite.SpriteId != "lobby-background"))
            {
                int left = (int)
                    MathF.Round(LobbyLayout.Width / 2f + (sprite.X - map.CameraX - sprite.ScaleX / 2) * pixelsPerUnit);
                int top = (int)
                    MathF.Round(LobbyLayout.Height / 2f - (sprite.Y - map.CameraY + sprite.ScaleY / 2) * pixelsPerUnit);
                int width = (int)MathF.Round(sprite.ScaleX * pixelsPerUnit);
                int height = (int)MathF.Round(sprite.ScaleY * pixelsPerUnit);
                var color = EffectSystem.ToColor(sprite.Color);
                for (int y = top; y < top + height; y++)
                    pixels.AsSpan(y * LobbyLayout.Width + left, width).Fill(color);
            }
            lobbyBackground = new Texture2D(canvas.Device, LobbyLayout.Width, LobbyLayout.Height);
            lobbyBackground.SetData(pixels);
        }
        canvas.Begin(0);
        Batch.Draw(lobbyBackground, new Rectangle(0, 0, Width, Height), Color.White);
        canvas.End();
    }

    internal void DrawRoomOutline(int room)
    {
        if (outlinedRoom != room)
        {
            lobbyOutline ??= new Texture2D(canvas.Device, LobbyLayout.Width, LobbyLayout.Height);
            var pixels = new Color[LobbyLayout.Width * LobbyLayout.Height];
            var interior = LobbyLayout.Interior(room);
            for (int y = interior.Top; y < interior.Bottom; y++)
                pixels.AsSpan(y * LobbyLayout.Width + interior.X, interior.Width).Fill(new Color(5, 10, 14, 225));
            foreach (var edge in LobbyLayout.SelectionEdges(room))
                for (int y = edge.Top; y < edge.Bottom; y++)
                    pixels.AsSpan(y * LobbyLayout.Width + edge.X, edge.Width).Fill(new Color(245, 60, 65));
            lobbyOutline.SetData(pixels);
            outlinedRoom = room;
        }
        EndUi();
        canvas.Begin(-1);
        Batch.Draw(lobbyOutline!, new Rectangle(0, 0, Width, Height), Color.White);
        canvas.End();
        BeginUi();
    }
}

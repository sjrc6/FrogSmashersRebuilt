using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FrogSmashers.Core;

internal static class GameplayData
{
    public static string Hash(GameContent content)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        new CharacterTuning(content.CharacterParameters).WriteConfiguration(writer);
        WriteMaps(writer, content.Maps);
        bool hasLobby = content.PresentationScenes.TryGetValue("Lobby", out var lobby);
        writer.Write(hasLobby);
        if (hasLobby)
            WriteMaps(writer, [lobby!]);
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    public static void WriteMaps(BinaryWriter writer, IReadOnlyList<MapData> maps)
    {
        writer.Write(maps.Count);
        foreach (var map in maps)
        {
            writer.Write(map.Id);
            writer.Write((int)map.Role);
            WriteNumber(writer, map.KillBounds.Left);
            WriteNumber(writer, map.KillBounds.Right);
            WriteNumber(writer, map.KillBounds.Bottom);
            WriteNumber(writer, map.KillBounds.Top);
            writer.Write(map.Collision.Count);
            foreach (var box in map.Collision)
            {
                WriteNumber(writer, box.X);
                WriteNumber(writer, box.Y);
                WriteNumber(writer, box.Width);
                WriteNumber(writer, box.Height);
                writer.Write(box.OneWay);
                writer.Write(box.BeachBallCollision);
            }
            writer.Write(map.Spawns.Count);
            foreach (var spawn in map.Spawns)
            {
                WriteNumber(writer, spawn.X);
                WriteNumber(writer, spawn.Y);
            }
            WriteNumber(writer, map.FlySpawn.X);
            WriteNumber(writer, map.FlySpawn.Y);
            WriteNumber(writer, map.BeachBallSpawn.X);
            WriteNumber(writer, map.BeachBallSpawn.Y);
        }
    }

    private static void WriteNumber(BinaryWriter writer, decimal value) =>
        writer.Write(value.ToString("G29", CultureInfo.InvariantCulture));
}

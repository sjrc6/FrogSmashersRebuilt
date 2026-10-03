using System.Text.Json;
using FrogSmashers.Network;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

public sealed class ClientSettings
{
    public RollbackPreferences Rollback { get; set; } = new();
    public SteamTransport SteamTransport { get; set; }
    public bool Fullscreen { get; set; }
    public bool VSync { get; set; } = true;
    public int FrameLimit { get; set; } = 240;
    public float Volume { get; set; } = .65f;
    public float TitleVolume { get; set; } = 1;
    public bool ScreenShake { get; set; } = true;
    public Dictionary<string, PadBindings> ControllerBindings { get; set; } = new();
    public KeyBindings[] Keyboard { get; set; } =
    [
        new(),
        new()
        {
            Left = Keys.Left,
            Right = Keys.Right,
            Up = Keys.Up,
            Down = Keys.Down,
            Jump = Keys.M,
            Attack = Keys.OemPeriod,
            Tongue = Keys.OemComma,
            Strafe = Keys.N,
        },
    ];

    public static string FilePath
    {
        get
        {
            string? folder = OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable("XDG_DATA_HOME") : null;
            if (string.IsNullOrEmpty(folder) || !Path.IsPathRooted(folder))
            {
                folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }

            if (string.IsNullOrEmpty(folder))
            {
                folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local",
                    "share"
                );
            }

            return Path.Combine(folder, "FrogSmashersRebuilt", "settings.json");
        }
    }

    public static ClientSettings Load()
    {
        try
        {
            return Parse(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    internal static ClientSettings Parse(string json)
    {
        var value = JsonSerializer.Deserialize<ClientSettings>(json) ?? new();
        value.Rollback = (value.Rollback ?? new()).Normalize();
        if (!Enum.IsDefined(value.SteamTransport))
            value.SteamTransport = SteamTransport.Sockets;
        value.FrameLimit = Math.Clamp(value.FrameLimit, 30, 1000);
        value.Volume = Math.Clamp(value.Volume, 0, 1);
        value.TitleVolume = Math.Clamp(value.TitleVolume, 0, 1);
        if (value.Keyboard == null || value.Keyboard.Length != 2 || value.Keyboard.Any(k => k == null))
        {
            value.Keyboard = new ClientSettings().Keyboard;
        }

        value.ControllerBindings ??= new();
        foreach (var key in value.ControllerBindings.Keys.ToArray())
            if (string.IsNullOrWhiteSpace(key) || value.ControllerBindings[key] == null)
                value.ControllerBindings.Remove(key);
        foreach (var pad in value.ControllerBindings.Values)
            pad.Normalize();
        var defaults = new ClientSettings().Keyboard;
        for (int i = 0; i < 2; i++)
        {
            value.Keyboard[i].RemoveMenuKey(defaults[i]);
        }

        return value;
    }

    public void Save() => Save(FilePath);

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })
            );
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}

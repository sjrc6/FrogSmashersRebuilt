using System.Text.Json;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

public sealed class ClientSettings
{
    public bool Fullscreen { get; set; }
    public bool VSync { get; set; } = true;
    public int FrameLimit { get; set; } = 240;
    public float Volume { get; set; } = .65f;
    public bool ScreenShake { get; set; } = true;
    public MatchPreferences MatchDefaults { get; set; } = new();
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
        value.FrameLimit = Math.Clamp(value.FrameLimit, 30, 1000);
        value.Volume = Math.Clamp(value.Volume, 0, 1);
        if (value.Keyboard == null || value.Keyboard.Length != 2 || value.Keyboard.Any(k => k == null))
        {
            value.Keyboard = new ClientSettings().Keyboard;
        }

        value.MatchDefaults ??= new();
        value.MatchDefaults.Normalize();
        var defaults = new ClientSettings().Keyboard;
        for (int i = 0; i < 2; i++)
        {
            value.Keyboard[i].RemoveMenuKey(defaults[i]);
        }

        return value;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

using System.Text.Json;
using FrogSmashers.Core;
using Microsoft.Xna.Framework.Input;

namespace FrogSmashers.Client;

public sealed class ClientSettings
{
    public bool Fullscreen { get; set; }
    public bool VSync { get; set; } = true;
    public int FrameLimit { get; set; } = 240;
    public float Volume { get; set; } = .65f;
    public bool ScreenShake { get; set; } = true;
    public int FontSmoothing { get; set; }
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public GameRules Rules { get; set; } = new();
    public int FirstMap { get; set; }
    public bool ShuffleMaps { get; set; }
    public int ExpectedPeers { get; set; } = 2;
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
            string json = File.ReadAllText(FilePath);
            var value = JsonSerializer.Deserialize<ClientSettings>(json) ?? new();
            value.Width = Math.Clamp(value.Width, 640, 7680);
            value.Height = Math.Clamp(value.Height, 360, 4320);
            value.FrameLimit = Math.Clamp(value.FrameLimit, 30, 1000);
            value.Volume = Math.Clamp(value.Volume, 0, 1);
            value.FontSmoothing = Math.Clamp(value.FontSmoothing, 0, 2);
            value.FirstMap = Math.Clamp(value.FirstMap, 0, 6);
            value.ExpectedPeers = Math.Clamp(value.ExpectedPeers, 2, 8);
            value.Rules ??= new();
            value.Rules.WinScore = Math.Clamp(value.Rules.WinScore, 0, 30);
            value.Rules.MatchRounds = Math.Clamp(value.Rules.MatchRounds, 1, 20);
            if (value.Keyboard == null || value.Keyboard.Length != 2 || value.Keyboard.Any(k => k == null))
            {
                value.Keyboard = new ClientSettings().Keyboard;
            }

            using var document = JsonDocument.Parse(json);
            if (
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Keyboard", out var keyboards)
                && keyboards.ValueKind == JsonValueKind.Array
                && keyboards.GetArrayLength() == 2
            )
            {
                for (int i = 0; i < 2; i++)
                {
                    if (keyboards[i].ValueKind == JsonValueKind.Object && !keyboards[i].TryGetProperty("Strafe", out _))
                    {
                        value.Keyboard[i].Strafe = i == 0 ? Keys.R : Keys.N;
                    }
                }
            }

            return value;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

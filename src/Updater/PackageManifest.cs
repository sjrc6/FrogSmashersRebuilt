using System.Security.Cryptography;
using System.Text.Json;

namespace FrogSmashers.Updater;

internal sealed record PackageFile(string Sha256, long Size, bool Executable);

internal sealed record PackageManifest(string Platform, string Commit, Dictionary<string, PackageFile> Files)
{
    public const string FileName = "installation.json";
    public const long MaximumFileSize = 512L * 1024 * 1024;
    public const long MaximumPackageSize = 2L * 1024 * 1024 * 1024;
    public const int MaximumFiles = 10000;
    public string GameExecutable => Platform == "win-x64" ? "FrogSmashersRebuilt.exe" : "FrogSmashersRebuilt";
    public string UpdaterExecutable => Platform == "win-x64" ? "FrogSmashersUpdater.exe" : "FrogSmashersUpdater";

    public static PackageManifest Read(string directory, string platform)
    {
        string path = SafePath(directory, FileName);
        if (!File.Exists(path))
            throw new InvalidDataException(
                "Installation manifest missing. Extract a complete release before updating."
            );
        if (new FileInfo(path).Length > 4 * 1024 * 1024)
            throw new InvalidDataException("Installation manifest is too large.");
        var manifest =
            JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Invalid installation manifest.");
        manifest.Validate(platform);
        return manifest;
    }

    public void Validate(string platform)
    {
        if (Platform != platform || Platform is not ("win-x64" or "linux-x64"))
            throw new InvalidDataException("This package is for a different platform.");
        if (Commit == null || Commit.Length != 40 || !Commit.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid package commit.");
        if (Files == null || Files.Count is 0 or > MaximumFiles)
            throw new InvalidDataException("Invalid package file list.");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long size = 0;
        foreach (var (path, file) in Files)
        {
            ValidatePath(path);
            if (path.Split('/')[0].Equals("crashlogs", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Crash logs must not be owned by the release package.");
            if (path.Equals(FileName, StringComparison.OrdinalIgnoreCase) || !paths.Add(path))
                throw new InvalidDataException("Duplicate package path: " + path);
            if (
                file == null
                || file.Size is < 0 or > MaximumFileSize
                || file.Sha256 == null
                || file.Sha256.Length != 64
                || !file.Sha256.All(Uri.IsHexDigit)
            )
                throw new InvalidDataException("Invalid file description: " + path);
            size += file.Size;
        }
        if (size > MaximumPackageSize || !Files.ContainsKey(GameExecutable) || !Files.ContainsKey(UpdaterExecutable))
            throw new InvalidDataException("Incomplete or oversized game package.");
    }

    public bool MatchesFiles(string directory)
    {
        foreach (var (name, expected) in Files)
        {
            string path = SafePath(directory, name);
            if (
                !File.Exists(path)
                || new FileInfo(path).Length != expected.Size
                || Hash(path) != expected.Sha256.ToUpperInvariant()
            )
                return false;
        }
        return true;
    }

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string SafePath(string directory, string relative)
    {
        ValidatePath(relative);
        string path = Path.GetFullPath(directory);
        RejectLink(path);
        foreach (string part in relative.Split('/'))
        {
            path = Path.Combine(path, part);
            RejectLink(path);
        }
        return path;
    }

    private static void RejectLink(string path)
    {
        if (new FileInfo(path).LinkTarget != null || new DirectoryInfo(path).LinkTarget != null)
            throw new IOException("Cannot update through a symbolic link: " + path);
    }

    public static void ValidatePath(string path)
    {
        if (
            string.IsNullOrEmpty(path)
            || path.Length > 240
            || path.Contains('\\')
            || path.Any(c => c < ' ' || ":*?\"<>|".Contains(c))
        )
            throw new InvalidDataException("Invalid package path: " + path);
        foreach (string part in path.Split('/'))
        {
            string stem = part.Split('.')[0].ToUpperInvariant();
            if (
                part.Length == 0
                || part is "." or ".."
                || part.EndsWith('.')
                || part.EndsWith(' ')
                || part.StartsWith(".update-", StringComparison.OrdinalIgnoreCase)
                || stem is "CON" or "PRN" or "AUX" or "NUL"
                || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])
            )
                throw new InvalidDataException("Invalid package path: " + path);
        }
    }
}

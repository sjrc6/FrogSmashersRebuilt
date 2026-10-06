using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FrogSmashers.Updater;

internal static class Program
{
    private static int checks;

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value)
            throw new Exception(message);
    }

    private static void Reject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            checks++;
            return;
        }
        throw new Exception(message);
    }

    private static async Task<int> Main()
    {
        try
        {
            foreach (string platform in new[] { "win-x64", "linux-x64" })
            {
                InstallAndRollback(platform);
                Recovery(platform);
                Archives(platform);
            }
            await Downloads();
            Console.WriteLine($"PASS: {checks} updater checks");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void InstallAndRollback(string platform)
    {
        using var fixture = new Fixture(platform);
        var old = fixture.Install("old", stale: true);
        fixture.UserFiles();
        string package = fixture.Archive("new");
        string staged = Path.Combine(fixture.Root, "stage");
        var next = PackageArchive.Extract(package, staged, platform);
        var installation = new Installation(fixture.Game, platform);
        installation.Apply(staged, old, next);
        Check(next.MatchesFiles(fixture.Game), "Installed files differ from verified package");
        Check(!File.Exists(Path.Combine(fixture.Game, "obsolete.txt")), "Removed package file remained installed");
        Check(fixture.UsersPreserved(), "Update removed crashlogs or custom files");
        Check(PackageManifest.Read(fixture.Game, platform).Commit == next.Commit, "Manifest did not advance");
        if (!OperatingSystem.IsWindows() && platform == "linux-x64")
            Check(
                (File.GetUnixFileMode(Path.Combine(fixture.Game, next.UpdaterExecutable)) & UnixFileMode.UserExecute)
                    != 0,
                "Linux updater lost executable permission"
            );

        old = fixture.Install("old", stale: true);
        File.Delete(Path.Combine(fixture.Game, "new.txt"));
        staged = Path.Combine(fixture.Root, "broken-stage");
        next = PackageArchive.Extract(package, staged, platform);
        File.Delete(Path.Combine(staged, "new.txt"));
        Reject(() => installation.Apply(staged, old, next), "Mid-install I/O failure did not fail");
        Check(!File.Exists(Path.Combine(staged, next.GameExecutable)), "Failure did not exercise partial installation");
        Check(old.MatchesFiles(fixture.Game), "Mid-install failure did not restore the previous package");
        Check(PackageManifest.Read(fixture.Game, platform).Commit == old.Commit, "Rollback did not restore manifest");
        Check(fixture.UsersPreserved(), "Rollback damaged user files");

        staged = Path.Combine(fixture.Root, "conflict-stage");
        next = PackageArchive.Extract(package, staged, platform);
        File.WriteAllText(Path.Combine(fixture.Game, "new.txt"), "my own file");
        Reject(() => installation.Apply(staged, old, next), "Updater overwrote an unowned file");
        Check(
            File.ReadAllText(Path.Combine(fixture.Game, "new.txt")) == "my own file" && old.MatchesFiles(fixture.Game),
            "Conflicting user file was changed"
        );
        File.Delete(Path.Combine(fixture.Game, "new.txt"));

        if (OperatingSystem.IsWindows())
        {
            using var locked = new FileStream(
                Path.Combine(fixture.Game, "data.txt"),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read
            );
            Reject(() => installation.Apply(staged, old, next), "Locked file update unexpectedly succeeded");
            Check(old.MatchesFiles(fixture.Game), "Locked-file failure did not restore earlier replaced files");
        }
        else
        {
            string outside = Path.Combine(fixture.Root, "outside.txt");
            File.WriteAllText(outside, "untouched");
            File.Delete(Path.Combine(fixture.Game, "data.txt"));
            File.CreateSymbolicLink(Path.Combine(fixture.Game, "data.txt"), outside);
            Reject(() => installation.Apply(staged, old, next), "Updater followed a symbolic link");
            Check(File.ReadAllText(outside) == "untouched", "Updater modified a linked user file");
        }
    }

    private static void Recovery(string platform)
    {
        using var fixture = new Fixture(platform);
        var old = fixture.Install("old", stale: true);
        fixture.UserFiles();
        string staged = Path.Combine(fixture.Root, "stage");
        var next = PackageArchive.Extract(fixture.Archive("new"), staged, platform);
        string backup = Path.Combine(fixture.Game, ".update-backup");
        Directory.CreateDirectory(Path.Combine(backup, "files"));
        File.WriteAllText(Path.Combine(backup, "owner"), "FrogSmashersRebuilt update backup");
        string[] existing = old.Files.Keys.Append(PackageManifest.FileName).ToArray();
        foreach (string name in existing)
            File.Copy(Path.Combine(fixture.Game, name), Path.Combine(backup, "files", name));
        File.WriteAllText(
            Path.Combine(backup, "journal.json"),
            JsonSerializer.Serialize(
                new
                {
                    Previous = old,
                    Next = next,
                    Existing = existing,
                }
            )
        );
        File.Copy(Path.Combine(staged, next.GameExecutable), Path.Combine(fixture.Game, next.GameExecutable), true);
        File.Copy(Path.Combine(staged, "new.txt"), Path.Combine(fixture.Game, "new.txt"));
        File.Delete(Path.Combine(fixture.Game, "obsolete.txt"));
        File.WriteAllText(Path.Combine(backup, "restoring-interrupted"), "unfinished earlier recovery");
        var installation = new Installation(fixture.Game, platform);
        installation.Recover();
        Check(
            old.MatchesFiles(fixture.Game) && !File.Exists(Path.Combine(fixture.Game, "new.txt")),
            "Interrupted install did not recover"
        );
        Check(fixture.UsersPreserved(), "Interrupted install recovery damaged user files");
        installation.Recover();
        Check(!Directory.Exists(backup), "Recovery did not clean its backup");
    }

    private static void Archives(string platform)
    {
        using var fixture = new Fixture(platform);
        var old = fixture.Install("old");
        foreach (
            string variant in new[]
            {
                "../escape",
                "/absolute",
                "C:/absolute",
                "a\\b",
                "data.txt",
                "unlisted.txt",
                "symlink",
                "corrupt",
                "missing",
            }
        )
        {
            string archive = fixture.Archive("new", variant);
            Reject(
                () =>
                    PackageArchive.Extract(archive, Path.Combine(fixture.Root, Guid.NewGuid().ToString("N")), platform),
                "Unsafe or corrupt archive accepted: " + variant
            );
            Check(old.MatchesFiles(fixture.Game), "Archive rejection changed the installed game");
        }
        string valid = fixture.Archive("new");
        Reject(
            () =>
                PackageArchive.Extract(
                    valid,
                    Path.Combine(fixture.Root, "wrong-platform"),
                    platform == "win-x64" ? "linux-x64" : "win-x64"
                ),
            "Wrong-platform archive was accepted"
        );
    }

    private static async Task Downloads()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("release archive");
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        var asset = new ReleaseAsset(
            "FrogSmashersRebuilt-linux.tar.gz",
            "https://github.com/sjrc6/FrogSmashersRebuilt/releases/download/v1/FrogSmashersRebuilt-linux.tar.gz",
            bytes.Length,
            "sha256:" + hash
        );
        var release = new GithubRelease("v1", new string('a', 40), false, false, [asset]);
        using var fixture = new Fixture("linux-x64");
        using var http = new HttpClient(
            new Handler(request =>
                request.RequestUri!.Host == "api.github.com"
                    ? new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(release)) }
                    : new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
            )
        );
        var client = new ReleaseClient(http);
        var latest = await client.Latest("linux-x64", default);
        Check(latest.Asset == asset, "Latest stable release asset selection failed");
        string output = Path.Combine(fixture.Root, "download");
        await client.Download(asset, output, _ => { }, default);
        Check(File.ReadAllBytes(output).SequenceEqual(bytes), "Downloaded archive changed");
        foreach (
            var invalid in new[]
            {
                asset with
                {
                    Size = bytes.Length + 1,
                },
                asset with
                {
                    Size = 1,
                },
                asset with
                {
                    Digest = "sha256:" + new string('0', 64),
                },
            }
        )
        {
            try
            {
                await client.Download(
                    invalid,
                    Path.Combine(fixture.Root, Guid.NewGuid().ToString("N")),
                    _ => { },
                    default
                );
                throw new Exception("Truncated or corrupt download was accepted");
            }
            catch (InvalidDataException)
            {
                checks++;
            }
        }
        using var offline = new HttpClient(new Handler(_ => throw new HttpRequestException("offline")));
        try
        {
            await new ReleaseClient(offline).Latest("linux-x64", default);
            throw new Exception("Offline check succeeded");
        }
        catch (HttpRequestException)
        {
            checks++;
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await client.Download(asset, Path.Combine(fixture.Root, "cancelled"), _ => { }, cancelled.Token);
            throw new Exception("Cancelled download succeeded");
        }
        catch (OperationCanceledException)
        {
            checks++;
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    private sealed class Fixture(string platform) : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("fsr-updater-tests-").FullName;
        public string Game => Path.Combine(Root, "game");

        private Dictionary<string, byte[]> Files(string version, bool stale = false) =>
            new()
            {
                [platform == "win-x64" ? "FrogSmashersRebuilt.exe" : "FrogSmashersRebuilt"] = Encoding.UTF8.GetBytes(
                    "game " + version
                ),
                [platform == "win-x64" ? "FrogSmashersUpdater.exe" : "FrogSmashersUpdater"] = Encoding.UTF8.GetBytes(
                    "updater " + version
                ),
                ["data.txt"] = Encoding.UTF8.GetBytes("data " + version),
                [stale ? "obsolete.txt" : "new.txt"] = Encoding.UTF8.GetBytes(version),
            };

        private PackageManifest Manifest(Dictionary<string, byte[]> files, string version) =>
            new(
                platform,
                new string(version == "old" ? 'a' : 'b', 40),
                files.ToDictionary(
                    pair => pair.Key,
                    pair => new PackageFile(
                        Convert.ToHexString(SHA256.HashData(pair.Value)),
                        pair.Value.Length,
                        !pair.Key.EndsWith(".txt")
                    )
                )
            );

        public PackageManifest Install(string version, bool stale = false)
        {
            Directory.CreateDirectory(Game);
            var files = Files(version, stale);
            var manifest = Manifest(files, version);
            foreach (var (name, bytes) in files)
                File.WriteAllBytes(Path.Combine(Game, name), bytes);
            File.WriteAllText(Path.Combine(Game, PackageManifest.FileName), JsonSerializer.Serialize(manifest));
            return manifest;
        }

        public void UserFiles()
        {
            Directory.CreateDirectory(Path.Combine(Game, "crashlogs"));
            File.WriteAllText(Path.Combine(Game, "crashlogs", "crash.txt"), "crash");
            File.WriteAllText(Path.Combine(Game, "my-notes.txt"), "mine");
        }

        public bool UsersPreserved() =>
            File.ReadAllText(Path.Combine(Game, "crashlogs", "crash.txt")) == "crash"
            && File.ReadAllText(Path.Combine(Game, "my-notes.txt")) == "mine";

        public string Archive(string version, string? variant = null)
        {
            var files = Files(version);
            var manifest = Manifest(files, version);
            files[PackageManifest.FileName] = JsonSerializer.SerializeToUtf8Bytes(manifest);
            if (variant == "corrupt")
                files["data.txt"] = [1, 2, 3];
            if (variant == "missing")
                files.Remove("data.txt");
            string root = platform == "win-x64" ? "FrogSmashersRebuilt-windows/" : "FrogSmashersRebuilt-linux/";
            string path = Path.Combine(
                Root,
                Guid.NewGuid().ToString("N") + (platform == "win-x64" ? ".zip" : ".tar.gz")
            );
            var entries = files.Select(pair => (Name: root + pair.Key, Bytes: pair.Value, Link: false)).ToList();
            if (variant != null && variant is not ("corrupt" or "missing"))
                entries.Add((root + variant, [7], variant == "symlink"));
            if (platform == "win-x64")
            {
                using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
                foreach (var item in entries)
                {
                    var entry = zip.CreateEntry(item.Name);
                    entry.ExternalAttributes = (item.Link ? 0xA1FF : 0x81A4) << 16;
                    using var stream = entry.Open();
                    stream.Write(item.Bytes);
                }
            }
            else
            {
                using var file = File.Create(path);
                using var gzip = new GZipStream(file, CompressionMode.Compress);
                using var tar = new TarWriter(gzip);
                foreach (var item in entries)
                {
                    var entry = new PaxTarEntry(
                        item.Link ? TarEntryType.SymbolicLink : TarEntryType.RegularFile,
                        item.Name
                    );
                    if (item.Link)
                        entry.LinkName = "../../outside";
                    else
                        entry.DataStream = new MemoryStream(item.Bytes);
                    tar.WriteEntry(entry);
                    entry.DataStream?.Dispose();
                }
            }
            return path;
        }

        public void Dispose() => Directory.Delete(Root, true);
    }
}

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FrogSmashers.Updater;

internal static class Program
{
    private sealed record Options(
        string Directory,
        string? Package,
        bool Check,
        bool Launch,
        bool Interactive,
        int Parent
    );

    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help"))
        {
            Console.WriteLine(
                "Frog Smashers Rebuilt Updater\nRun beside the game to install the latest stable release, then launch it.\n"
                    + "  --check                 Check without installing or launching\n"
                    + "  --no-launch             Do not launch the game afterward\n"
                    + "  --non-interactive       Do not wait for Enter after an error\n"
                    + "  --install-dir PATH      Update another extracted installation\n"
                    + "  --package PATH          Install a local release archive instead of downloading"
            );
            return 0;
        }
        Options? options = null;
        try
        {
            options = Parse(args);
            if (OperatingSystem.IsWindows() && !options.Check && options.Parent == 0)
                return StartWorker(options);
            if (options.Parent != 0)
            {
                try
                {
                    using var parent = Process.GetProcessById(options.Parent);
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await parent.WaitForExitAsync(deadline.Token);
                }
                catch (ArgumentException) { }
            }
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancellation.Cancel();
            };
            await Run(options, cancellation.Token);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                error is OperationCanceledException
                    ? "Update cancelled or timed out."
                    : "Update failed: " + error.Message
            );
            if (options != null)
                Pause(options);
            return 1;
        }
    }

    private static async Task Run(Options options, CancellationToken cancellation)
    {
        if (
            RuntimeInformation.ProcessArchitecture != Architecture.X64
            || !OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()
        )
            throw new PlatformNotSupportedException("The updater supports Windows x64 and Linux x64.");
        string platform = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";
        if (!Directory.Exists(options.Directory))
            throw new DirectoryNotFoundException("Game installation not found: " + options.Directory);
        PackageManifest.SafePath(options.Directory, PackageManifest.FileName);
        string lockPath = Path.Combine(options.Directory, ".update-lock");
        if (new FileInfo(lockPath).LinkTarget != null)
            throw new IOException("The update lock must not be a symbolic link.");
        using var updateLock = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            1,
            FileOptions.DeleteOnClose
        );
        var installation = new Installation(options.Directory, platform);
        if (Directory.Exists(Path.Combine(options.Directory, ".update-backup")))
        {
            if (options.Check)
                throw new IOException("An interrupted update needs recovery. Run the updater without --check first.");
            string executable = Path.Combine(
                options.Directory,
                platform == "win-x64" ? "FrogSmashersRebuilt.exe" : "FrogSmashersRebuilt"
            );
            await RunningGame.WaitForExit(executable, cancellation);
            installation.Recover();
        }
        var previous = PackageManifest.Read(options.Directory, platform);
        Console.WriteLine("Frog Smashers Rebuilt Updater");
        string work = Path.Combine(options.Directory, ".update-work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            string archive;
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            if (options.Package is { } local)
                archive = local;
            else
            {
                Console.WriteLine("Checking the latest stable release...");
                var client = new ReleaseClient(http);
                var (release, asset) = await client.Latest(platform, cancellation);
                if (release.Commit == previous.Commit && previous.MatchesFiles(options.Directory))
                {
                    Console.WriteLine("Already up to date: " + release.Tag);
                    if (options.Launch && !options.Check)
                        RunningGame.Launch(options.Directory, previous);
                    return;
                }
                Console.WriteLine("Downloading " + release.Tag + "...");
                archive = Path.Combine(work, asset.Name);
                await client.Download(
                    asset,
                    archive,
                    percent => Console.WriteLine($"Download: {percent}%"),
                    cancellation
                );
            }
            cancellation.ThrowIfCancellationRequested();
            Console.WriteLine("Verifying the release package...");
            string staged = Path.Combine(work, "package");
            var next = PackageArchive.Extract(archive, staged, platform);
            bool same =
                previous.Files.Count == next.Files.Count
                && next.Files.All(pair => previous.Files.TryGetValue(pair.Key, out var file) && file == pair.Value)
                && next.MatchesFiles(options.Directory);
            if (same)
                Console.WriteLine("Already up to date.");
            else if (options.Check)
                Console.WriteLine("An update is available. Run the updater without --check to install it.");
            else
            {
                await RunningGame.WaitForExit(
                    PackageManifest.SafePath(options.Directory, previous.GameExecutable),
                    cancellation
                );
                cancellation.ThrowIfCancellationRequested();
                Console.WriteLine("Installing update...");
                installation.Apply(staged, previous, next);
                Console.WriteLine("Update installed.");
            }
            if (options.Launch && !options.Check)
                RunningGame.Launch(options.Directory, next);
        }
        finally
        {
            try
            {
                Directory.Delete(work, true);
            }
            catch (IOException)
            {
                Console.WriteLine("Temporary files remain in " + work);
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("Temporary files remain in " + work);
            }
        }
    }

    private static Options Parse(string[] args)
    {
        string directory = AppContext.BaseDirectory;
        string? package = null;
        bool check = false,
            launch = true,
            interactive = OperatingSystem.IsWindows() && !Console.IsInputRedirected;
        int parent = 0;
        for (int i = 0; i < args.Length; i++)
        {
            string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
            switch (args[i])
            {
                case "--install-dir":
                    directory = Value();
                    break;
                case "--package":
                    package = Value();
                    break;
                case "--check":
                    check = true;
                    break;
                case "--no-launch":
                    launch = false;
                    break;
                case "--non-interactive":
                    interactive = false;
                    break;
                case "--worker-parent":
                    parent = int.Parse(Value());
                    break;
                default:
                    throw new ArgumentException("Unknown option: " + args[i]);
            }
        }
        return new(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)),
            package == null ? null : Path.GetFullPath(package),
            check,
            launch,
            interactive,
            parent
        );
    }

    private static int StartWorker(Options options)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(options.Directory.ToUpperInvariant())))[
            ..16
        ];
        string folder = Path.Combine(Path.GetTempPath(), "FrogSmashersUpdater-" + key);
        Directory.CreateDirectory(folder);
        string helper = Path.Combine(folder, "FrogSmashersUpdater.exe");
        PackageManifest.SafePath(folder, "FrogSmashersUpdater.exe");
        File.Copy(Environment.ProcessPath ?? throw new IOException("Cannot locate updater executable."), helper, true);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, WorkingDirectory = folder };
        start.ArgumentList.Add("--install-dir");
        start.ArgumentList.Add(options.Directory);
        start.ArgumentList.Add("--worker-parent");
        start.ArgumentList.Add(Environment.ProcessId.ToString());
        if (options.Package != null)
        {
            start.ArgumentList.Add("--package");
            start.ArgumentList.Add(options.Package);
        }
        if (!options.Launch)
            start.ArgumentList.Add("--no-launch");
        if (!options.Interactive)
            start.ArgumentList.Add("--non-interactive");
        using var worker = Process.Start(start) ?? throw new IOException("Could not start updater helper.");
        return 0;
    }

    private static void Pause(Options options)
    {
        if (!options.Interactive)
            return;
        Console.WriteLine("Press Enter to close.");
        Console.ReadLine();
    }
}

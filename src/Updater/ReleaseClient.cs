using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace FrogSmashers.Updater;

internal sealed record ReleaseAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string Url,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("digest")] string Digest
);

internal sealed record GithubRelease(
    [property: JsonPropertyName("tag_name")] string Tag,
    [property: JsonPropertyName("target_commitish")] string Commit,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] ReleaseAsset[] Assets
);

internal sealed class ReleaseClient(HttpClient http)
{
    public const string Repository = "sjrc6/FrogSmashersRebuilt";

    public async Task<(GithubRelease Release, ReleaseAsset Asset)> Latest(
        string platform,
        CancellationToken cancellation
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.github.com/repos/{Repository}/releases/latest"
        );
        request.Headers.UserAgent.ParseAdd("FrogSmashersUpdater");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await http.SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("No public release is available yet.");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("GitHub temporarily refused the update check. Try again later.");
        response.EnsureSuccessStatusCode();
        var release =
            await response.Content.ReadFromJsonAsync(UpdaterJson.Default.GithubRelease, cancellation)
            ?? throw new InvalidDataException("GitHub returned an empty release.");
        if (release.Draft || release.Prerelease || string.IsNullOrWhiteSpace(release.Tag) || release.Assets == null)
            throw new InvalidDataException("The latest release is not a complete stable release.");
        string name = platform == "win-x64" ? "FrogSmashersRebuilt-windows.zip" : "FrogSmashersRebuilt-linux.tar.gz";
        var assets = release.Assets.Where(asset => asset.Name == name).ToArray();
        if (assets.Length != 1)
            throw new InvalidOperationException("The latest release does not contain " + name + ".");
        var asset = assets[0];
        if (
            !Uri.TryCreate(asset.Url, UriKind.Absolute, out var url)
            || url.Scheme != "https"
            || url.Host != "github.com"
            || !url.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.Ordinal)
            || asset.Size is <= 0 or > PackageManifest.MaximumPackageSize
            || asset.Digest == null
            || !asset.Digest.StartsWith("sha256:", StringComparison.Ordinal)
            || asset.Digest.Length != 71
            || !asset.Digest[7..].All(Uri.IsHexDigit)
        )
            throw new InvalidDataException("Invalid release download information.");
        return (release, asset);
    }

    public async Task Download(
        ReleaseAsset asset,
        string destination,
        Action<int> progress,
        CancellationToken cancellation
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        request.Headers.UserAgent.ParseAdd("FrogSmashersUpdater");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync(cancellation);
        using var output = new FileStream(destination, FileMode.CreateNew);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        long total = 0;
        int previous = -1;
        while (true)
        {
            int count = await input.ReadAsync(buffer, cancellation);
            if (count == 0)
                break;
            total += count;
            if (total > asset.Size)
                throw new InvalidDataException("Download exceeds its expected size.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
            hash.AppendData(buffer, 0, count);
            int percent = (int)(total * 100 / asset.Size);
            if (percent / 10 != previous / 10 || previous == -1)
                progress(percent);
            previous = percent;
        }
        if (
            total != asset.Size
            || !Convert
                .ToHexString(hash.GetHashAndReset())
                .Equals(asset.Digest[7..], StringComparison.OrdinalIgnoreCase)
        )
            throw new InvalidDataException(
                "Download is incomplete or failed verification. The game has not been changed."
            );
    }
}
